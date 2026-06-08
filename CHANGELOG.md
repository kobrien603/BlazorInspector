# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

_Nothing yet._

## [0.1.0] - 2026-06-07 — unlisted

> **Unlisted on nuget.org.** This version was published before it was ready and has been pulled from
> listings. nuget.org versions can't be deleted, so `0.1.0` is retained but hidden; the same
> functionality is re-released as `0.1.1` (see `[Unreleased]`).

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
[0.1.0]: https://www.nuget.org/packages/BlazorInspector/0.1.0
