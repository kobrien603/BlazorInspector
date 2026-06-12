// BlazorInspector — Phase 3a element picker (hover highlight + click-to-select).
//
// RCLs auto-load "{AssemblyName}.lib.module.js" as a JS initializer. `beforeStart` runs before the
// Blazor runtime boots; `afterStarted` runs once it is up (in WASM, and inside the MAUI
// BlazorWebView — see the MAUI note in samples/README.md).
//
// CLICK-TO-SELECT correlates a clicked DOM node to a Blazor componentId. On Blazor WASM the runtime
// applies render batches in JS via its internal BrowserRenderer, which keeps a componentId -> host
// DOM node map (`childComponentLocations`). That class and its registry are sealed in the framework
// module's closure (unreachable from here). Two facts make correlation possible anyway:
//   1. `Blazor._internal.renderBatch(rendererId, batchPtr)` is called with `batchPtr` as a raw
//      integer pointer into WASM linear memory. The framework decodes it with reader singletons that
//      are closure-private — BUT every read is just a fixed-offset load via `Blazor.platform` (which
//      IS exposed: readInt16/Int32/ObjectField, getArrayEntryPtr, ...). We reconstruct those readers
//      over the pointer in makeBatchReader(); the offsets mirror the RenderTree struct layouts.
//   2. Blazor's logical DOM tree is reachable from any node via two own Symbols — logical children
//      (an Array) and logical parent (a Node). We discover those symbols by value-type, not identity.
// So we wrap `renderBatch`, and AFTER the framework has applied the batch (logical tree now final) we
// re-walk the batch's reference frames read-only — mirroring BrowserRenderer.insertFrame — indexing
// into the already-built logical children to recover, for every component frame, its host node. That
// rebuilds our own componentId<->node maps without touching the closure-private state.
//
// Safety: we read the batch *after* calling the original but *before* our wrapper returns to .NET, so
// the batch's WASM memory is still alive (the call is synchronous; .NET is blocked on us, and no mono
// GC runs between synchronous statements). All framework interaction is wrapped in try/catch — the
// picker degrades to highlight-only, never throws.

let _blazor = null;
let _enabled = false;
let _highlight = null;
let _lastEl = null;

// componentId -> host DOM node (Comment for child components, Element for roots).
const idToNode = new Map();
// host DOM node -> componentId. WeakMap so detached hosts are collected automatically.
const nodeToId = new WeakMap();

// Blazor's logical-tree Symbols, discovered by the type of the value they hold (not by identity, which
// is closure-private). SYM_CHILDREN holds the logical-children Array; SYM_PARENT holds the logical
// parent Node.
let SYM_CHILDREN = null;
let SYM_PARENT = null;

// RenderTreeFrameType — stable public enum values (Microsoft.AspNetCore.Components.RenderTree).
const FRAME_ELEMENT = 1;
const FRAME_TEXT = 2;
const FRAME_COMPONENT = 4;
const FRAME_REGION = 5;
const FRAME_MARKUP = 8;
// RenderTreeEditType.
const EDIT_PREPEND_FRAME = 1;
const EDIT_STEP_IN = 6;
const EDIT_STEP_OUT = 7;

let _diagnosed = false; // one-time log describing the batch marshaling we actually observed
let _platform = null;   // Blazor.platform — the exposed WASM memory reader

// ── JS initializer hooks ────────────────────────────────────────────────────────────────────────

export function beforeStart() {
    try { installRenderBatchInterceptor(); } catch { /* highlight-only fallback */ }
}

export function afterStarted(blazor) {
    _blazor = blazor;
    // Belt-and-braces: ensure the interceptor is in place even if beforeStart didn't run (some hosts).
    try { installRenderBatchInterceptor(); } catch { /* ignore */ }

    window.blazorInspector = window.blazorInspector || {};
    window.blazorInspector.setPickerEnabled = setPickerEnabled;
    window.blazorInspector.isPickerSupported = () => true;
    window.blazorInspector.trackedComponentCount = () => idToNode.size; // diagnostics
}

// ── Render-batch interception ───────────────────────────────────────────────────────────────────

// Wrap Blazor._internal.renderBatch the moment it is assigned, before the JSImport binds it (wrapping
// after start is too late — the import caches the original reference). We call the original first so
// the DOM/logical tree is fully applied, then harvest componentId<->node mappings from the batch.
function installRenderBatchInterceptor() {
    const wrap = (fn) => function (rendererId, batch) {
        const ret = fn.apply(this, arguments);
        try { harvestBatch(batch); } catch { /* harvesting is best-effort */ }
        return ret;
    };

    const hookInternal = (internal) => {
        if (!internal || internal.__biHook) {
            return;
        }
        let current = typeof internal.renderBatch === 'function' ? wrap(internal.renderBatch) : internal.renderBatch;
        try {
            Object.defineProperty(internal, 'renderBatch', {
                configurable: true,
                get() { return current; },
                set(fn) { current = typeof fn === 'function' ? wrap(fn) : fn; },
            });
            internal.__biHook = true;
        } catch { /* property locked; picker stays highlight-only */ }
    };

    const hookBlazor = (blazor) => {
        if (!blazor) {
            return false;
        }
        if (blazor._internal) {
            hookInternal(blazor._internal);
        } else {
            let internal;
            try {
                Object.defineProperty(blazor, '_internal', {
                    configurable: true,
                    get() { return internal; },
                    set(v) { internal = v; hookInternal(v); },
                });
            } catch { /* ignore */ }
        }
        return true;
    };

    if (hookBlazor(window.Blazor)) {
        return;
    }
    if (window.__biBlazorHook) {
        return;
    }
    window.__biBlazorHook = true;
    let blazorRef;
    try {
        Object.defineProperty(window, 'Blazor', {
            configurable: true,
            get() { return blazorRef; },
            set(v) { blazorRef = v; hookBlazor(v); },
        });
    } catch { /* ignore */ }
}

// ── Batch decode → componentId<->node maps ──────────────────────────────────────────────────────

function getPlatform() {
    if (_platform) {
        return _platform;
    }
    const p = (typeof Blazor !== 'undefined' && Blazor && Blazor.platform) ||
        (_blazor && _blazor.platform) || null;
    if (p && typeof p.readInt32Field === 'function') {
        _platform = p;
    }
    return _platform;
}

// Reconstruct the framework's SharedMemoryRenderBatch readers over Blazor.platform + the batch
// pointer. Offsets/structLengths mirror the RenderTree struct layouts (Microsoft.AspNetCore.
// Components.RenderTree), verified against blazor.webassembly.js on .NET 8 / 9 / 10. If a future
// runtime changes them, this factory and the live picker are where it surfaces.
function makeBatchReader(addr, p) {
    const i16 = (ptr, off) => p.readInt16Field(ptr, off || 0);
    const i32 = (ptr, off) => p.readInt32Field(ptr, off || 0);
    const objField = (ptr, off) => p.readObjectField(ptr, off || 0);
    const struct = (ptr, off) => p.readStructField(ptr, off || 0);
    const entry = (arr, idx, len) => p.getArrayEntryPtr(arr, idx, len);

    const FRAME_LEN = 36, DIFF_LEN = 16, EDIT_LEN = 20, RANGE_LEN = 8;

    return {
        arrayRangeReader: {
            values: (r) => objField(r, 0),
            count: (r) => i32(r, 4),
        },
        arrayBuilderSegmentReader: {
            values: (s) => objField(p.getObjectFieldsBaseAddress(objField(s, 0)), 0),
            offset: (s) => i32(s, 4),
            count: (s) => i32(s, 8),
        },
        diffReader: {
            componentId: (d) => i32(d, 0),
            edits: (d) => struct(d, 4),
            editsEntry: (vals, idx) => entry(vals, idx, EDIT_LEN),
        },
        editReader: {
            editType: (e) => i32(e, 0),
            siblingIndex: (e) => i32(e, 4),
            newTreeIndex: (e) => i32(e, 8),
            moveToSiblingIndex: (e) => i32(e, 8),
        },
        frameReader: {
            frameType: (f) => i16(f, 4),
            subtreeLength: (f) => i32(f, 8),
            componentId: (f) => i32(f, 12),
        },
        updatedComponents: () => struct(addr, 0),
        referenceFrames: () => struct(addr, RANGE_LEN),
        disposedComponentIds: () => struct(addr, 2 * RANGE_LEN),
        updatedComponentsEntry: (vals, idx) => entry(vals, idx, DIFF_LEN),
        referenceFramesEntry: (vals, idx) => entry(vals, idx, FRAME_LEN),
        disposedComponentIdsEntry: (vals, idx) => i32(entry(vals, idx, 4), 0),
    };
}

function harvestBatch(ptr) {
    const p = getPlatform();
    const readable = typeof ptr === 'number' && !!p;
    if (!_diagnosed) {
        _diagnosed = true;
        // eslint-disable-next-line no-console
        console.info('[BlazorInspector] render batch ' + (readable
            ? 'decoded via Blazor.platform; click-to-select enabled.'
            : 'not readable (typeof=' + (typeof ptr) + ', platform=' + !!p + '); highlight-only.'));
    }
    if (!readable) {
        return;
    }

    ensureSymbols();
    if (!SYM_CHILDREN) {
        return; // logical tree not introspectable yet; try again next batch
    }

    const batch = makeBatchReader(ptr, p);
    const range = batch.arrayRangeReader;
    const diff = batch.diffReader;
    const framesValues = range.values(batch.referenceFrames());

    const updated = batch.updatedComponents();
    const uVals = range.values(updated);
    const uCount = range.count(updated);

    // Collect this batch's component diffs; note whether any updated component is still unmapped.
    const diffs = [];
    let anyUnmapped = false;
    for (let i = 0; i < uCount; i++) {
        const d = batch.updatedComponentsEntry(uVals, i);
        diffs.push(d);
        if (!idToNode.has(diff.componentId(d))) {
            anyUnmapped = true;
        }
    }

    // Steady-state batches re-render already-mapped components — skip root seeding entirely then, so a
    // dormant (Release) consumer pays no per-render scan. Only mounts/first-render reach seedRoots.
    if (anyUnmapped) {
        seedRoots(batch, diffs, diff, framesValues);
    }

    // Process parents before children: a child's host node is mapped while walking its parent's
    // frames. Batches are usually ordered parent-first; a few fixpoint passes absorb any exceptions.
    let pending = diffs;
    for (let pass = 0; pass < 4 && pending.length; pass++) {
        const next = [];
        for (const d of pending) {
            const cid = diff.componentId(d);
            if (idToNode.has(cid)) {
                processDiff(batch, framesValues, idToNode.get(cid), d);
            } else {
                next.push(d);
            }
        }
        if (next.length === pending.length) {
            break; // no progress — remaining ids have no known parent (e.g. unseeded head roots)
        }
        pending = next;
    }

    // Drop disposed components.
    const disposed = batch.disposedComponentIds();
    const dVals = range.values(disposed);
    const dCount = range.count(disposed);
    for (let i = 0; i < dCount; i++) {
        const id = batch.disposedComponentIdsEntry(dVals, i);
        const node = idToNode.get(id);
        if (node) {
            nodeToId.delete(node);
        }
        idToNode.delete(id);
    }
}

// Map root components to their root logical elements. A root component is one never referenced as a
// child-component frame anywhere in the batch; its host is attached directly to a registered DOM
// element rather than inserted by a parent's edits, so it can't be recovered by frame walking. Root
// hosts are logical elements with children but no logical parent.
function seedRoots(batch, diffs, diff, framesValues) {
    const range = batch.arrayRangeReader;
    const fr = batch.frameReader;
    const seg = batch.arrayBuilderSegmentReader;

    // Every componentId referenced as a child-component frame anywhere in this batch (cheap: scans
    // this batch's frames, not the DOM).
    const frameCount = range.count(batch.referenceFrames());
    const childIds = new Set();
    for (let i = 0; i < frameCount; i++) {
        const f = batch.referenceFramesEntry(framesValues, i);
        if (fr.frameType(f) === FRAME_COMPONENT) {
            childIds.add(fr.componentId(f));
        }
    }

    // Unmapped roots = updated components never referenced as a child and not already mapped. A new
    // child mount won't qualify (it's in childIds), so the DOM scan below runs only on real first-time
    // roots — effectively just at app start.
    const roots = [];
    for (const d of diffs) {
        const cid = diff.componentId(d);
        if (!childIds.has(cid) && !idToNode.has(cid)) {
            roots.push({ cid, count: seg.count(diff.edits(d)) });
        }
    }
    if (!roots.length) {
        return;
    }
    roots.sort((a, b) => b.count - a.count); // richest diff first (app root builds most; head root least)

    // Free element roots: logical element, no logical parent, not yet mapped. (Comment roots such as
    // `head::after` aren't found by querySelectorAll and carry no clickable content — ignored.)
    const free = [];
    for (const el of document.querySelectorAll('*')) {
        if (SYM_CHILDREN in el && !el[SYM_PARENT] && !nodeToId.has(el)) {
            free.push(el);
        }
    }

    for (let i = 0; i < free.length && i < roots.length; i++) {
        mapComponent(roots[i].cid, free[i]);
    }
}

// Mirror BrowserRenderer.applyEdits' cursor (stepIn/stepOut) for one component's diff, calling
// readInsertFrame for each prependFrame. We read the *already-applied* logical tree, so a prepended
// frame's node is simply the logical child at the edit's sibling index.
function processDiff(batch, framesValues, rootNode, d) {
    const edits = batch.editReader;
    const diff = batch.diffReader;
    const seg = batch.arrayBuilderSegmentReader;

    const editsRange = diff.edits(d);
    const eVals = seg.values(editsRange);
    const start = seg.offset(editsRange);
    const end = start + seg.count(editsRange);

    let parent = rootNode; // current logical parent (r)
    let depth = 0;         // stepIn depth (c)

    for (let i = start; i < end; i++) {
        const e = diff.editsEntry(eVals, i);
        const type = edits.editType(e);
        if (type === EDIT_PREPEND_FRAME) {
            const frameIndex = edits.newTreeIndex(e);
            const siblingIndex = edits.siblingIndex(e);
            readInsertFrame(batch, framesValues, parent, siblingIndex, frameIndex);
        } else if (type === EDIT_STEP_IN) {
            const child = childAt(parent, edits.siblingIndex(e));
            if (!child) {
                return; // tree desync; bail rather than risk mismapping
            }
            parent = child;
            depth++;
        } else if (type === EDIT_STEP_OUT) {
            parent = logicalParent(parent) || parent;
            depth--;
        }
        // removeFrame / setAttribute / updateText / updateMarkup / permutation*: irrelevant to
        // componentId<->node mapping (disposal is handled separately; reorders keep node identity).
    }
}

// Read-only mirror of BrowserRenderer.insertFrame: returns the number of logical children the frame
// contributes to `parent`, and records any component host nodes encountered.
function readInsertFrame(batch, framesValues, parent, childIndex, frameIndex) {
    const fr = batch.frameReader;
    const frame = batch.referenceFramesEntry(framesValues, frameIndex);
    const type = fr.frameType(frame);

    switch (type) {
        case FRAME_ELEMENT: {
            const el = childAt(parent, childIndex);
            if (el) {
                readInsertFrameRange(batch, framesValues, el, 0, frameIndex + 1, frameIndex + fr.subtreeLength(frame));
            }
            return 1;
        }
        case FRAME_COMPONENT: {
            const host = childAt(parent, childIndex);
            if (host) {
                mapComponent(fr.componentId(frame), host);
            }
            return 1;
        }
        case FRAME_REGION:
            // Regions are transparent: their children are logical children of `parent` directly.
            return readInsertFrameRange(batch, framesValues, parent, childIndex, frameIndex + 1, frameIndex + fr.subtreeLength(frame));
        case FRAME_TEXT:
        case FRAME_MARKUP:
            return 1;
        default:
            return 0; // attribute, element/component reference capture, named event
    }
}

function readInsertFrameRange(batch, framesValues, parent, startChildIndex, startFrame, endFrame) {
    const fr = batch.frameReader;
    let childIndex = startChildIndex;
    for (let fi = startFrame; fi < endFrame; fi++) {
        const frame = batch.referenceFramesEntry(framesValues, fi);
        childIndex += readInsertFrame(batch, framesValues, parent, childIndex, fi);
        const type = fr.frameType(frame);
        if (type === FRAME_ELEMENT || type === FRAME_COMPONENT || type === FRAME_REGION) {
            fi += fr.subtreeLength(frame) - 1; // skip descendant frames; this frame consumed them
        }
    }
    return childIndex - startChildIndex;
}

function mapComponent(id, node) {
    idToNode.set(id, node);
    nodeToId.set(node, id);
}

// ── Logical-tree helpers ────────────────────────────────────────────────────────────────────────

function childAt(node, index) {
    const children = node && node[SYM_CHILDREN];
    return children ? children[index] || null : null;
}

function logicalParent(node) {
    return (node && node[SYM_PARENT]) || null;
}

// Discover the logical-children and logical-parent Symbols by sampling DOM nodes. The children Symbol
// holds an Array; the parent Symbol holds a Node that is itself a logical element (distinguishing it
// from any other Node-valued own symbol).
function ensureSymbols() {
    if (SYM_CHILDREN && SYM_PARENT) {
        return;
    }
    const all = document.querySelectorAll('*');
    for (const el of all) {
        const syms = Object.getOwnPropertySymbols(el);
        if (!syms.length) {
            continue;
        }
        let childrenSym = null;
        for (const s of syms) {
            if (Array.isArray(el[s])) {
                childrenSym = s;
                break;
            }
        }
        if (childrenSym && !SYM_CHILDREN) {
            SYM_CHILDREN = childrenSym;
        }
        if (SYM_CHILDREN && !SYM_PARENT) {
            for (const s of syms) {
                const v = el[s];
                if (v && (v.nodeType === 1 || v.nodeType === 8) && SYM_CHILDREN in v) {
                    SYM_PARENT = s;
                    break;
                }
            }
        }
        if (SYM_CHILDREN && SYM_PARENT) {
            return;
        }
    }
}

// ── Picker UI (hover highlight + click) ─────────────────────────────────────────────────────────

function setPickerEnabled(enabled) {
    _enabled = !!enabled;
    if (_enabled) {
        ensureHighlight();
        document.addEventListener('mousemove', onMouseMove, true);
        document.addEventListener('click', onClick, true);
        document.addEventListener('keydown', onKeyDown, true);
    } else {
        document.removeEventListener('mousemove', onMouseMove, true);
        document.removeEventListener('click', onClick, true);
        document.removeEventListener('keydown', onKeyDown, true);
        hideHighlight();
        _lastEl = null;
    }
}

function ensureHighlight() {
    if (_highlight) {
        return;
    }
    _highlight = document.createElement('div');
    Object.assign(_highlight.style, {
        position: 'fixed',
        pointerEvents: 'none',
        zIndex: '2147483646',
        background: 'rgba(91, 45, 214, 0.25)',
        border: '1px solid #5b2dd6',
        borderRadius: '2px',
        display: 'none',
    });
    document.body.appendChild(_highlight);
}

function hideHighlight() {
    if (_highlight) {
        _highlight.style.display = 'none';
    }
}

function isInspectorNode(el) {
    return !!(el && el.closest && el.closest('.bi-root'));
}

function onMouseMove(e) {
    if (!_enabled) {
        return;
    }
    const el = e.target;
    if (!el || isInspectorNode(el)) {
        hideHighlight();
        _lastEl = null;
        return;
    }
    _lastEl = el;
    const rect = el.getBoundingClientRect();
    Object.assign(_highlight.style, {
        display: 'block',
        left: rect.left + 'px',
        top: rect.top + 'px',
        width: rect.width + 'px',
        height: rect.height + 'px',
    });
}

function onClick(e) {
    if (!_enabled) {
        return;
    }
    const el = e.target;
    if (isInspectorNode(el)) {
        return; // let clicks on the panel through
    }
    e.preventDefault();
    e.stopPropagation();

    const componentId = findComponentId(el);
    setPickerEnabled(false);

    if (componentId != null) {
        try {
            DotNet.invokeMethodAsync('BlazorInspector', 'OnPick', componentId);
        } catch {
            // .NET side unavailable; ignore.
        }
    }
}

function onKeyDown(e) {
    if (_enabled && e.key === 'Escape') {
        setPickerEnabled(false);
    }
}

// Walk up from the clicked node to the nearest component host. We prefer the logical parent chain
// (which passes through component host comments) and fall back to the DOM parent for nodes Blazor
// does not manage (e.g. raw markup content).
function findComponentId(el) {
    let node = el;
    const guard = 10000;
    for (let i = 0; node && i < guard; i++) {
        const id = nodeToId.get(node);
        if (id != null) {
            return id;
        }
        const lp = logicalParent(node);
        node = lp || node.parentNode;
    }
    return null;
}
