# BlazorInspector — Build Spec

This document is the build brief for an in-app component inspector for Blazor. It is written to be
handed to a coding agent. Read all of it before writing code. The architectural decisions in the
"Locked decisions" section are settled — implement against them, don't re-litigate them.

---

## Mission

Build a Vue/React-DevTools-style component inspector for **Blazor WebAssembly** and **MAUI Blazor
Hybrid**. The developer should be able to open a floating panel inside their running app, browse the
live component tree, inspect each component's current parameter values, and (eventually) click an
element to highlight its component and jump to its `.razor` source. It is a personal/local debugging
tool distributed as a Razor class library and referenced from the developer's own projects.

---

## Context: why this exists

There is an existing tool, `BlazorDeveloperTools` (a Chrome extension + NuGet package), but it only
supports InteractiveAuto/Server render mode. WebAssembly and MAUI Hybrid are unsupported. This
project fills that gap. We are **not** copying its Chrome-extension approach — see locked decisions.

---

## Locked decisions (do not change without strong reason)

1. **In-app overlay, not a browser extension.** A Chrome/Edge DevTools extension panel cannot load
   into a MAUI `BlazorWebView`, so it can't cover both targets. The inspector is a Blazor component
   that the host app renders. This also matches the desired "floating button at the bottom" UX.

2. **Read component data in-process, in C#.** Because the overlay runs in the same .NET runtime as
   the inspected components (the WASM runtime, or the native runtime behind the MAUI WebView), there
   is **no client/server boundary** to cross. Do not build a JS data bridge for the tree or parameter
   values. JS is only introduced in Phase 3 for the element picker / hover highlight.

3. **Capture components via `IComponentActivator`.** Registering a custom activator in DI is the
   supported seam to observe every component instance creation, with no base-class requirement and no
   DOM injection. The activator must reproduce default creation behavior and chain to any
   pre-existing activator (e.g. bUnit's).

4. **Debug-only.** The implementation reflects over private framework internals. That is acceptable
   for a debug tool but can be broken by IL trimming / WASM AOT in Release. All registration and the
   overlay must be gated behind `#if DEBUG` and documented as such. Never assume Release works.

5. **Distributed as a Razor Class Library (RCL).** Referenced via project or NuGet by the developer's
   WASM and MAUI Hybrid apps. License: Apache-2.0 (match the ecosystem norm).

---

## Targets & constraints

- Target the same TFM as the consuming apps. The RCL multi-targets `net8.0;net9.0;net10.0`; the
  reflected internals are verified on all three by the `RuntimeInternalsTests` guard.
- Must work, with identical code, in Blazor WASM (browser) and MAUI Blazor Hybrid (BlazorWebView).
- No third-party runtime dependencies beyond `Microsoft.AspNetCore.Components.Web`.
- Zero required code changes in the consumer's components for basic tracking (one DI call + one
  overlay tag is the entire integration).

---

## Repository layout

```
/BlazorInspector                  # the RCL (multi-targets net8.0;net9.0;net10.0)
  BlazorInspector.csproj
  RuntimeInternals.cs             # ALL framework-internals reflection (named, version-doc'd constants)
  ParameterReader.cs              # safe [Parameter]/[CascadingParameter]/state reading + formatting
  ValueReader.cs                  # expandable value tree + reflective inline-edit writes
  InspectorRegistry.cs            # weak-ref tracking; Snapshot() and BuildTree()
  InspectorComponentActivator.cs  # IComponentActivator that tracks instances + chains
  ServiceCollectionExtensions.cs  # AddBlazorInspector() (DEBUG-gated)
  InspectorOptions.cs             # corner / refresh interval / start-open
  InspectorInterop.cs             # [JSInvokable] picker callback
  ComponentSnapshot.cs            # ComponentNode / ParameterValue / ValueNode types
  SourceMap.cs                    # reads the generated jump-to-code map; builds vscode:// links
  InspectorOverlay.razor(.css)    # floating panel UI + scoped styles
  InspectorTreeNode.razor         # recursive component-tree row
  InspectorValueNode.razor        # recursive expandable/editable value row
  /wwwroot                        # BlazorInspector.lib.module.js (Phase 3 picker)
/BlazorInspector.SourceGen        # Roslyn source generator for jump-to-code (netstandard2.0)
/samples
  /SampleWasm                     # Blazor WASM manual test harness
  /SampleMaui                     # MAUI Hybrid manual test harness
/tests
  /BlazorInspector.Tests          # xUnit + bUnit + source-generator tests (net8/9/10)
README.md  CONTRIBUTING.md  CHANGELOG.md  LICENSE
CLAUDE.md                         # this file (the build spec)
```

---

## Current state (implemented & verified — 2026-06)

All phases below are implemented and **verified live**: the xUnit/bUnit/source-generator suite passes
on net8.0/net9.0/net10.0, and the overlay, tree, parameter/state inspection, expandable values, inline
editing, picker highlight, and jump-to-code were exercised against the running WASM sample (via
Playwright) and inside the MAUI BlazorWebView (via WebView2 CDP). What exists:

- `InspectorRegistry` — weak-reference tracking; `Snapshot()` (flat) and `BuildTree()` (parent/child
  hierarchy from the renderer's `ComponentState` map); prunes dead refs.
- `InspectorComponentActivator : IComponentActivator` — tracks every created instance, skips the
  inspector's own namespace, chains to an inner activator.
- `ParameterReader` / `ValueReader` — safe parameter/state reading, expandable collection/dictionary/
  object drill-down, and reflective inline editing of writable scalar leaves (force-re-renders the
  owning component via `RuntimeInternals.TryInvokeStateHasChanged`).
- `AddBlazorInspector()` DI extension — DEBUG-gated; a no-op in Release.
- `InspectorOverlay.razor` (+ `InspectorTreeNode` / `InspectorValueNode`) — floating button, collapsible
  tree, detail pane with expandable + editable values, element picker, and jump-to-code links.
- `BlazorInspector.SourceGen` — the jump-to-code generator, packed into the package's analyzers folder.

Beyond the original brief, the repo also has full OSS packaging (NuGet metadata, README/CONTRIBUTING/
CHANGELOG, Apache-2.0 LICENSE) and GitHub Actions CI + NuGet-publish workflows. Extend/harden from here
— it is finished and correct for its current scope, not a starter.

---

## STEP 0 — Verify runtime internals before anything else

Everything hinges on reflecting private members whose names vary by .NET version. These are now
**confirmed for .NET 8 / 9 / 10** and recorded as named constants in `RuntimeInternals.cs`, guarded by
`RuntimeInternalsTests.AllReflectedMembersResolve`. When bumping the TFM, re-run that test (or a tiny
probe) to re-confirm the following before assuming anything still holds:

- On `ComponentBase`: the private field holding the render handle — `_renderHandle`.
- On `RenderHandle`: the component id is a **private field** `_componentId` (confirmed: a field, not a
  property). Also the internal reference to the `Renderer` — field `_renderer`.
- On `Renderer`: the dictionary mapping component id to component state (expected
  `_componentStateById`, a `Dictionary<int, ComponentState>`), and on `ComponentState` the members
  for component id, the component instance, and the parent state (expected `ParentComponentState`).

Record the confirmed names in code as named constants with a comment noting the .NET version checked.
If a future runtime breaks them, this is the one place to fix.

---

## Phase 1 — finish & harden

Goal: a reliable flat list of live components with correct ids and live parameter values, in both
targets.

Tasks:
- Apply STEP 0 findings; make componentId resolution actually work.
- Build the two sample apps and confirm the overlay renders and lists components in WASM and MAUI.
- Make parameter reading robust: handle `[Parameter]`, `[CascadingParameter]`, `RenderFragment`
  (show as `<fragment>` rather than dumping), `EventCallback`, and collections (show count/type, not
  a giant ToString). Never let a throwing getter crash the panel.
- Add a manual refresh (exists) and a components-count badge that's accurate after pruning.

Acceptance:
- In both sample apps, opening the panel lists every live component including framework ones.
- Each component shows a non-null componentId and its current parameter values, and values update
  after a refresh following a state change.
- No exceptions when components have RenderFragment/EventCallback/null/throwing parameters.

---

## Phase 2 — real component tree + live refresh

Goal: replace the flat list with the true parent/child hierarchy, refreshing automatically.

Approach: prefer the **renderer-reflection** path over the activator list for hierarchy, because it's
authoritative. From any tracked `ComponentBase`, reflect its render handle → the `Renderer` → the
`_componentStateById` dictionary. Walk `ComponentState.ParentComponentState` to build parent→children
links, keyed by component id. The activator list remains useful for type metadata and for components
not yet in the renderer map. Build the tree from the renderer snapshot each refresh.

Tasks:
- Add a `BuildTree()` that returns a root → children structure of `(componentId, type, parameters)`.
- Render it in the overlay as a collapsible tree (indentation + chevrons), replacing the flat list.
- Add live refresh without a manual click: simplest is a low-frequency timer (e.g. 500ms) while the
  panel is open, calling `StateHasChanged`. A render-notification hook is nicer but optional.
- Keep expansion state stable across refreshes (key by component id, not object identity).

Acceptance:
- The panel shows the same hierarchy you'd expect from the markup (parent components contain their
  children), verified in both sample apps including a nested/third-party component (e.g. a layout
  with nested components).
- The tree updates on its own as components mount/unmount and parameters change.
- Expansion/selection survive refreshes.

---

## Phase 3 — element picker + jump-to-code

Goal: hover an element to highlight its component; click to select it in the tree; click a source
link to open the `.razor` file.

### 3a. Element picker (introduces JS)

Add `wwwroot/BlazorInspector.lib.module.js`. RCLs auto-load `{AssemblyName}.lib.module.js` via the JS
initializer mechanism. Export `afterStarted(blazor)` and wrap `Blazor._internal.renderBatch` to
observe which component ids are touched and correlate them to DOM nodes (the render batch carries the
edit positions Blazor applies to the DOM). Maintain a DOM-node ↔ componentId map. On picker mode:
hover walks up from `event.target` to the nearest mapped node and draws a highlight overlay; click
sends the matched componentId back to .NET (`DotNet.invokeMethodAsync`) to select it in the tree.

Verify the JS initializer fires inside the MAUI BlazorWebView, not just in WASM; if it doesn't,
fall back to registering the module via the host page for MAUI.

**Status (verified):** the JS initializer *does* fire inside the MAUI BlazorWebView on .NET 10 — no
host-page fallback needed. Hover-highlight works in both targets. **Click-to-select is not achievable
on current Blazor WASM:** the runtime hands `renderBatch` an opaque WASM-memory pointer (not a readable
batch object), so a DOM↔componentId map can't be built from JS — the picker ships **highlight-only** and
logs a one-time note on click. The hook is installed in a `beforeStart` initializer (wrapping
`renderBatch` in `afterStarted` is too late — the JSImport is already bound). See README "Limitations".

### 3b. Jump-to-code (introduces a source generator)

Add a Roslyn source generator (`IIncrementalGenerator`) that maps each component type to its `.razor`
source path and line. Pragmatic route: read `AdditionalFiles` for `*.razor`, map by convention (type
name ↔ file name, namespace ↔ folder), and emit a static `Dictionary<Type, (string File, int Line)>`.
Handle code-behind/partial cases by also honoring `#line` directives in the Razor-generated output if
the convention mapping misses. The overlay renders a `vscode://file/{absolutePath}:{line}` link per
component, which opens VS Code locally.

**Status (implemented):** the generator maps `.razor` `AdditionalFiles` to type names honoring per-file
`@namespace` directives and `_Imports.razor` inheritance, then the RootNamespace + folder convention.
The `#line`-from-generated-output route is **not reachable** — source generators can't see each other's
output within one compilation, so this generator never sees the Razor-generated classes; `@namespace`
parsing is the equivalent fix for the cases the plain convention misses.

Acceptance:
- Picker: hovering the page highlights the component under the cursor in both targets; clicking
  selects and scrolls to it in the tree.
- Jump-to-code: each tree node shows its `.razor` file; clicking the source link opens that file at
  roughly the right line in VS Code.

---

## Coding conventions

- C# nullable enabled, implicit usings on. Keep the public surface tiny: `AddBlazorInspector()`,
  `<InspectorOverlay />`, and the option type. Everything else internal.
- All reflection lives behind named, version-documented constants in one file. No scattered magic
  strings.
- Fail soft: the inspector must never throw into the host app. Wrap reflection and getters; degrade
  to partial data rather than crashing.
- The overlay must not interfere with the host: high z-index, scoped class names (the `bi-` prefix),
  no global style leakage, and never tracked by its own activator.

---

## Testing strategy

- Unit-test `InspectorRegistry` parameter reading and pruning with fake `IComponent` types (no
  renderer needed).
- bUnit component tests for the overlay rendering given a stubbed registry. Note bUnit registers its
  own `IComponentActivator`; use this to exercise the chaining path.
- The two sample apps are the manual integration harness for the renderer-reflection, picker, and
  source-link features, which are hard to unit test. Document a manual test checklist in
  `/samples/README.md`.

---

## Pitfalls / gotchas

- Registering an `IComponentActivator` makes this library responsible for creating **all** components.
  The activator must always return a valid instance (chain → default), or the app won't render.
- Private member names differ across .NET versions; STEP 0 and the named-constants rule exist to
  contain that. Re-run the probe when bumping TFM.
- Trimming/AOT in Release can strip the reflected members. Keep everything Debug-gated; consider a
  `[DynamicDependency]` / trimmer-roots note only if you ever decide to support Release.
- Don't track the overlay's own components (namespace skip) or you'll get recursion/noise.
- `ToString()` on rich parameter values (collections, fragments) can be huge or throw — summarize,
  don't dump.
- **Razor markup gotchas (these have bitten this codebase — watch for them when editing the `.razor`
  components):**
  - A **string** component parameter written `Param="field"` passes the **literal text** `"field"`,
    not the value of `field`. Use `Param="@field"` / `Param="@(expr)"`. Non-string params (delegates,
    ints, objects) treat `Param="expr"` as a C# expression, which makes the string case a *silent*
    trap — it cost real debugging on the inline-value editor (the editor never opened because child
    nodes received the literal string `"_editingPath"`).
  - Never put `#if DEBUG` inside `.razor` **markup** — it renders as literal text. Gate the overlay at
    runtime via the options `Enabled` flag instead. Compile-time `#if` is fine in `.cs` / `@code`.
- The picker's render-batch hook must be installed **before** the runtime starts (a `beforeStart` JS
  initializer), not in `afterStarted` — by then the `renderBatch` JSImport is already bound and
  wrapping it has no effect. (And on .NET 10 WASM the batch is an opaque pointer anyway, so DOM↔id
  correlation isn't reachable from JS — the picker is highlight-only there; see README.)

---

## Out of scope (for now)

- ~~Editing parameter values live from the panel.~~ **Implemented** — writable scalar leaves
  (numbers, strings, bools, enums, Guid, dates) are editable in place, including values nested inside
  lists / dictionaries / objects. The edit writes via reflection and re-renders the owning component.
  Caveat: editing a top-level `[Parameter]` reverts on the next parent render; state and nested
  object/collection edits persist. See README.
- Server / InteractiveAuto render mode (the existing tool covers it).
- Standalone DevTools-panel extension.
- Persisting profiles or timeline/flamegraph profiling (could be a later phase, mirroring the
  existing tool, but not part of this build).
