# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Element picker click-to-select now works on Blazor WebAssembly.** Clicking an element in picker
  mode selects its component in the tree (previously highlight-only). The earlier limitation held that
  the render batch is an opaque WASM pointer with no JS-reachable path to DOM nodes — the pointer part
  is true, but the path exists: the JS module decodes the batch through the exposed `Blazor.platform`
  memory reader (struct offsets mirror the `RenderTree` layouts, identical on .NET 8/9/10) and walks
  Blazor's logical-element tree via the DOM nodes' own Symbols to recover each component's host node.
  No public-API or .NET-side changes; degrades to hover-highlight only if the internals are
  unavailable. Verified live against the WASM sample with Playwright.

### Known limitations

- Click-to-select is **Blazor WASM only**. MAUI Blazor Hybrid runs on the native runtime (no
  `Blazor.platform` memory reader, different batch marshaling), so the picker there stays
  hover-highlight only and the component is selected from the tree.

## [0.1.2] - 2026-06-09

### Fixed

- **The package no longer ships dead.** The Debug-only gate was a library-side `#if DEBUG`, which is
  evaluated when *BlazorInspector itself* is compiled — and the package is built in Release — so the
  published `0.1.1` had the tracking activator stripped out and the overlay forced off. Installed from
  NuGet, it was a permanent no-op in every consumer (it only ever worked via a project reference). The
  gate is now a **runtime check on the consuming app's** build — the entry assembly's
  `DebuggableAttribute`, falling back to the assembly that calls `AddBlazorInspector` on MAUI
  Android/iOS/Mac Catalyst, where `Assembly.GetEntryAssembly()` returns null — so installing the package
  and running your app in Debug actually shows the inspector, on every supported platform.
- A **Release consumer registers no component activator at all** (not merely a disabled one), so the
  inspector adds zero per-component overhead and keeps no tracking list there.

## [0.1.1] - 2026-06-07 — deprecated & unlisted

> **Deprecated — do not use.** Unlisted on nuget.org. Non-functional when installed from NuGet: the
> Debug-only gate was a library-side `#if DEBUG`, so the Release-compiled package shipped with the
> inspector stripped out and it never activated in any consumer. This was a re-release of `0.1.0`'s
> functionality but carried the same packaging defect. Use **`0.1.2`** or later (see `[0.1.2]`).

## [0.1.0] - 2026-06-07 — deprecated & unlisted

> **Deprecated — do not use.** Unlisted on nuget.org. Published before it was ready and pulled from
> listings; nuget.org versions can't be deleted, so `0.1.0` is retained but hidden. Like `0.1.1`, it is
> non-functional when installed from NuGet (the inspector was compiled out of the package). Use
> **`0.1.2`** or later (see `[0.1.2]`).

Initial release — an in-app, DevTools-style component inspector for Blazor WebAssembly and MAUI
Blazor Hybrid. Debug-only; a no-op in Release.

### Added

- **Live component tree** read from the renderer's `ComponentState` map (authoritative parent/child
  hierarchy), with automatic refresh while the panel is open and stable expansion/selection across
  refreshes.
- **Parameter & state inspection** — `[Parameter]`, `[CascadingParameter]`, and a **State** section
  for a component's own non-parameter fields/properties. Rich values are summarized safely
  (`RenderFragment` → `<fragment>`, `EventCallback` → `<event>`, collections → `Type (n items)`,
  throwing getters → `<error: …>`).
- **Expandable values** — drill into collections, dictionaries, and objects (and nested rows)
  DevTools-style; lazy, with reference-cycle / depth / breadth guards.
- **Inline editing** of writable scalar values (numbers, strings, bools, enums, `Guid`, dates/times),
  including values nested inside collections and objects; writes to the live object and re-renders the
  owning component so the host UI updates immediately.
- **Element picker** — hover the page to highlight the element under the cursor.
- **Jump-to-code** — a Roslyn source generator maps component types to their `.razor` path/line and
  the panel renders `vscode://file/...` links. Type-name resolution honors per-file `@namespace`
  directives and `_Imports.razor` inheritance, falling back to the `RootNamespace` + folder convention.
- **Two-line integration** (`AddBlazorInspector()` + `<InspectorOverlay />`), configurable corner,
  refresh interval, and start-open behavior.
- Multi-targets **net8.0 / net9.0 / net10.0**; identical behavior in Blazor WASM and MAUI Hybrid.
- Framework-internals reflection centralized in `RuntimeInternals.cs` behind named, version-documented
  constants, guarded by a test that fails if a member no longer resolves.

### Known limitations

- The element **picker is highlight-only on current Blazor WASM**: on .NET 10 the render batch is an
  opaque pointer, so DOM↔componentId correlation isn't reachable from JS. Hover-highlight works;
  click-to-select selects from the tree instead.
- Editing a top-level `[Parameter]` is reverted on the next parent re-render; state and nested
  object/collection edits persist.
- Debug-only: trimming / WASM AOT can strip the reflected members, so the inspector is disabled in
  Release builds.

[Unreleased]: https://github.com/kobrien603/BlazorInspector/commits/main
[0.1.2]: https://www.nuget.org/packages/BlazorInspector/0.1.2
[0.1.1]: https://www.nuget.org/packages/BlazorInspector/0.1.1
[0.1.0]: https://www.nuget.org/packages/BlazorInspector/0.1.0
