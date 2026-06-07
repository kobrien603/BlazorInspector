// BlazorInspector — Phase 3a element picker.
//
// RCLs auto-load "{AssemblyName}.lib.module.js" as a JS initializer. `beforeStart` runs before the
// Blazor runtime boots; `afterStarted` runs once it is up (in WASM, and inside the MAUI
// BlazorWebView — see the MAUI note in samples/README.md).
//
// HOVER HIGHLIGHT works on every runtime. CLICK-TO-SELECT requires correlating a DOM node to a
// Blazor componentId, which is only reachable if the framework hands us a *readable* render batch.
//
// VERIFIED LIMITATION (.NET 10 WASM, Blazor 10.0.8): the runtime invokes `renderBatch` with the
// batch as a raw integer pointer into WASM linear memory (`typeof batch === 'number'`), not an
// object exposing `updatedComponents()`/`arrayRangeReader`. There is no JS-reachable map from a
// component's logical render tree to its concrete DOM nodes, so DOM↔componentId correlation cannot
// be built from JS on that runtime, and the picker degrades to HIGHLIGHT-ONLY there (clicking
// highlights but does not select — it logs a one-time note instead of silently doing nothing).
//
// The render-batch hook below is installed *before* the runtime binds the `renderBatch` JSImport
// (wrapping it after start is too late — the import caches the original reference). It is used to
// detect the batch marshaling kind and exposes a seam to populate `domToComponentId` on any runtime
// that does pass a readable batch object.

let _blazor = null;
let _enabled = false;
let _highlight = null;
let _lastEl = null;
let _correlation = 'unknown'; // 'pointer' (unreadable), 'object' (readable), or 'unknown'
let _noticeShown = false;

// DOM element -> componentId. WeakMap so detached nodes are collected automatically.
const domToComponentId = new WeakMap();

// Runs before Blazor boots: install the pre-bind interceptor so our wrapper survives JSImport binding.
export function beforeStart() {
    try { installRenderBatchInterceptor(); } catch { /* highlight-only fallback */ }
}

export function afterStarted(blazor) {
    _blazor = blazor;
    // Belt-and-braces: ensure the interceptor is in place even if beforeStart didn't run (some hosts).
    try { installRenderBatchInterceptor(); } catch { /* ignore */ }

    // Public API the .NET overlay calls via IJSRuntime.
    window.blazorInspector = window.blazorInspector || {};
    window.blazorInspector.setPickerEnabled = setPickerEnabled;
    window.blazorInspector.isPickerSupported = () => true;           // hover highlight always works
    window.blazorInspector.correlationKind = () => _correlation;     // 'pointer' | 'object' | 'unknown'
}

// Wrap Blazor._internal.renderBatch the moment it is assigned, before the JSImport binds it. The
// wrapper only observes the batch (to detect its kind / tag nodes); it never alters rendering.
function installRenderBatchInterceptor() {
    const wrap = (fn) => function (rendererId, batch) {
        try { observeBatch(batch); } catch { /* observation is best-effort */ }
        return fn.apply(this, arguments);
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
            // _internal is assigned during boot — intercept that assignment.
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
    // window.Blazor not defined yet — intercept its assignment.
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

// Detect how the runtime marshals the render batch. On a readable (object) batch this is the seam
// where DOM node tagging would be populated; on a pointer batch correlation is not JS-reachable.
function observeBatch(batch) {
    if (_correlation === 'object') {
        return;
    }
    if (typeof batch === 'number') {
        _correlation = 'pointer';
        return;
    }
    if (batch && typeof batch.updatedComponents === 'function') {
        _correlation = 'object';
        // A readable batch is available on this runtime; a future build could decode updated
        // components here and populate domToComponentId. Left unpopulated until validated live.
    }
}

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

// True for nodes inside the inspector overlay itself, so the picker never targets its own UI.
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

    if (componentId != null && _blazor) {
        try {
            DotNet.invokeMethodAsync('BlazorInspector', 'OnPick', componentId);
        } catch {
            // .NET side unavailable; ignore.
        }
    } else if (!_noticeShown) {
        // No correlation available (e.g. pointer-batch WASM): be explicit rather than silent.
        _noticeShown = true;
        // eslint-disable-next-line no-console
        console.info(
            '[BlazorInspector] Picker is highlight-only on this runtime: a DOM node could not be ' +
            'mapped to a componentId (render batch is not JS-readable here). Select components from ' +
            'the tree instead.');
    }
}

function onKeyDown(e) {
    if (_enabled && e.key === 'Escape') {
        setPickerEnabled(false);
    }
}

// Walk up from the target to the nearest DOM node we have a componentId for.
function findComponentId(el) {
    let node = el;
    while (node) {
        if (domToComponentId.has(node)) {
            return domToComponentId.get(node);
        }
        node = node.parentElement;
    }
    return null;
}
