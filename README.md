# BlazorInspector

[![CI](https://github.com/kobrien603/BlazorInspector/actions/workflows/ci.yml/badge.svg)](https://github.com/kobrien603/BlazorInspector/actions/workflows/ci.yml)
[![Live demo](https://img.shields.io/badge/live%20demo-online-2ea043?logo=blazor&logoColor=white)](https://kobrien603.github.io/BlazorInspector/)
[![NuGet](https://img.shields.io/nuget/v/BlazorInspector.svg?logo=nuget)](https://www.nuget.org/packages/BlazorInspector)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)](#requirements)
[![Targets](https://img.shields.io/badge/Blazor-WASM%20%7C%20MAUI%20Hybrid-5b2dd6)](#requirements)

A Vue/React-DevTools-style **in-app component inspector** for **Blazor WebAssembly** and **MAUI Blazor
Hybrid**. It's a small, local debugging tool I built for my own use — shared openly in case it's
helpful to you too.

Open a floating panel inside your running app to browse the live component tree, inspect (and edit)
each component's parameters and state, hover the page to highlight elements, and jump straight to a
component's `.razor` source in VS Code.

🔗 **Try it live:** [kobrien603.github.io/BlazorInspector](https://kobrien603.github.io/BlazorInspector/) — a hosted Blazor WASM sample with the inspector already wired in. Click the floating button (bottom corner) to open the panel.

![BlazorInspector demo — opening the panel, expanding the component tree, drilling into a collection, and editing a value live so the page updates instantly](docs/demo.gif)

> [!IMPORTANT]
> **Debug-only by design.** The inspector reflects over private ASP.NET Core internals — fine for a
> local debugging tool, but can be broken by IL trimming / WASM AOT. It enables itself automatically
> **only when your app is built in Debug** (detected at runtime) and is a **no-op when your app is built
> in Release**, so it's safe to leave the registration and `<InspectorOverlay />` tag in place. Don't
> force it on in Release. (The [live demo](https://kobrien603.github.io/BlazorInspector/) is simply
> published in Debug — nothing is forced — which is why the overlay is active there.)

## Table of contents

- [Live demo](https://kobrien603.github.io/BlazorInspector/)
- [Who it's for](#who-its-for)
- [Features](#features)
- [Platform support (WASM vs MAUI Hybrid)](#platform-support-wasm-vs-maui-hybrid)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Configuration](#configuration)
- [Jump-to-code setup](#jump-to-code-setup)
- [How it works](#how-it-works)
- [Limitations & known issues](#limitations--known-issues)
- [Building from source](#building-from-source)
- [Contributing](#contributing)
- [License](#license)

## Who it's for

BlazorInspector is a personal, local debugging tool — built to inspect **Blazor WebAssembly** and
**MAUI Blazor Hybrid** apps from inside the running app. It's shared openly in case it's useful to you,
but it's intentionally small in scope and isn't trying to be a full DevTools suite.

If you want a mature, full-featured Blazor DevTools experience — a render profiler, flamegraphs, and a
browser DevTools panel, and especially on **Server** or **InteractiveAuto** — reach for
[**BlazorDeveloperTools**](https://github.com/joe-gregory/blazor-devtools)
([NuGet](https://www.nuget.org/packages/BlazorDeveloperTools) ·
[blazordevelopertools.com](https://blazordevelopertools.com)). It's the more polished option.

## Features

- **Live component tree** — the real parent/child hierarchy read from the renderer's component-state
  map (not guessed), auto-refreshing while the panel is open, with stable expansion/selection.
- **Parameter & state inspection** — `[Parameter]`, `[CascadingParameter]`, and a **State** section
  for a component's own non-parameter fields/properties (e.g. a page's private `currentCount`). Rich
  values are summarized safely: `RenderFragment` → `<fragment>`, `EventCallback` → `<event>`,
  collections → `Type (n items)`, throwing getters → `<error: …>`.
- **Expandable values** — drill into any value DevTools-style: expand a list/array to its items, a
  dictionary to its entries, or an object to its properties/fields, and keep going into nested rows.
  Lazy (children load only when expanded) and guarded against reference cycles, depth, breadth, and
  throwing getters.
- **Inline editing** — edit writable scalar values in place (numbers, strings, bools, enums, `Guid`,
  dates/times), including values nested deep inside collections and objects. The change is written to
  the live object and the owning component re-renders, so your app's UI updates immediately.
- **Element picker** — hover the page to highlight the element under the cursor, then click to select
  its component in the tree (click-to-select on Blazor WASM; hover-highlight on MAUI Hybrid).
- **Jump-to-code** — every component shows a `</> source` link that opens its `.razor` file at roughly
  the right line in VS Code (`vscode://file/...`).
- **One codebase, both targets** — identical behavior in Blazor WASM (browser) and MAUI Blazor Hybrid
  (`BlazorWebView`).
- **Tiny footprint** — two lines of integration, no third-party runtime dependencies beyond
  `Microsoft.AspNetCore.Components.Web`, and a public surface of just `AddBlazorInspector()`,
  `<InspectorOverlay />`, and the options type.

## Platform support (WASM vs MAUI Hybrid)

Everything works identically on both targets **except picker click-to-select**, which is WASM-only.

| Capability | Blazor WASM | MAUI Blazor Hybrid |
|---|:---:|:---:|
| Live component tree (real parent/child hierarchy) | ✅ | ✅ |
| Parameter & state inspection | ✅ | ✅ |
| Expandable values (collections / dictionaries / objects) | ✅ | ✅ |
| Inline editing of writable scalar values | ✅ | ✅ |
| Jump-to-code (`</> source` → VS Code) | ✅ | ✅ |
| Element picker — **hover highlight** | ✅ | ✅ |
| Element picker — **click-to-select** | ✅ | ❌ — highlight only; select from the tree |
| Auto-enable in Debug / no-op in Release | ✅ (entry assembly) | ✅ (calling-assembly fallback¹) |

**Why click-to-select differs.** On WASM the runtime applies render batches in JavaScript, so the
picker can decode each batch through the exposed `Blazor.platform` memory reader and walk Blazor's
logical-element tree (via the DOM nodes' own Symbols) to map a clicked element to its component. MAUI
Blazor Hybrid runs on the **native** runtime — there is no `Blazor.platform` and batches are marshaled
to the WebView differently — so that path isn't available and the picker stays hover-highlight only.
It degrades gracefully (never throws); you just select the component from the tree.

**Applies to both targets:**
- **Debug-only.** The inspector auto-enables in a Debug build of your app and is a complete no-op in
  Release (no overlay, no component-tracking activator, no framework-internals reflection).
- **Reflects private framework internals** (renderer component-state map; WASM batch/struct offsets in
  the picker). These are centralized and documented per .NET version (verified 8.0 / 9.0 / 10.0); a
  runtime that changes them degrades the affected feature rather than crashing.
- **Editing a top-level `[Parameter]`** is reverted on the next parent re-render; state and nested
  object/collection edits persist.
- **Not for Blazor Server / InteractiveAuto** — see [Who it's for](#who-its-for).

¹ On MAUI Android / iOS / Mac Catalyst `Assembly.GetEntryAssembly()` is null, so call
`AddBlazorInspector()` from your **app** project (e.g. `MauiProgram.cs`), not a shared Release-built
library — the calling assembly is the Debug/Release signal there.

## Requirements

| | |
|---|---|
| **.NET** | 8.0, 9.0, or 10.0 (the RCL multi-targets `net8.0;net9.0;net10.0`) |
| **Render modes** | Blazor WebAssembly, MAUI Blazor Hybrid |
| **Build** | Debug only — auto-enables in a Debug build of your app, no-ops in Release |
| **Runtime deps** | none beyond `Microsoft.AspNetCore.Components.Web` |

## Installation

Install the package into your Blazor WASM or MAUI Hybrid app:

```bash
dotnet add package BlazorInspector
```

> [!IMPORTANT]
> Use **0.1.2 or later**. `0.1.0` and `0.1.1` are deprecated and unlisted — they were compiled out of
> the package and do nothing when installed from NuGet (see [CHANGELOG](CHANGELOG.md)). If you pinned an
> earlier version, bump it.

> Working against the source instead of the published package? Add a `ProjectReference` to
> `BlazorInspector/BlazorInspector.csproj` (and, for jump-to-code, the source generator as an
> `Analyzer` reference — see [`samples/SampleWasm/SampleWasm.csproj`](samples/SampleWasm/SampleWasm.csproj)).

## Quick start

Two lines of integration:

```csharp
// Program.cs (WASM) or MauiProgram.cs (MAUI). Auto-enables when your app is built in
// Debug; a no-op when it's built in Release — no need to wrap this in your own #if DEBUG.
builder.Services.AddBlazorInspector();
```

```razor
@* MainLayout.razor (or any always-rendered component) *@
<InspectorOverlay />
```

Run the app in **Debug** and click the 🔍 button in the corner.

## Configuration

```csharp
builder.Services.AddBlazorInspector(o =>
{
    o.Corner = InspectorCorner.BottomLeft;               // floating-button corner
    o.RefreshInterval = TimeSpan.FromMilliseconds(750);  // tree auto-refresh cadence
    o.StartOpen = true;                                  // open the panel on load
});
```

| Option | Type | Default | Description |
|---|---|---|---|
| `Corner` | `InspectorCorner` | `BottomRight` | Which corner the floating button/panel docks to. |
| `RefreshInterval` | `TimeSpan` | `500 ms` | How often the tree refreshes while the panel is open. |
| `StartOpen` | `bool` | `false` | Open the panel automatically when the app starts. |

## Jump-to-code setup

For the `</> source` links to resolve, feed the consuming project's `.razor` files to the bundled
source generator:

```xml
<ItemGroup>
  <AdditionalFiles Include="**/*.razor" />
</ItemGroup>
```

Type → source mapping honors per-file `@namespace` directives and `_Imports.razor` namespace
inheritance, falling back to the `RootNamespace` + folder convention. Links use the
`vscode://file/{absolutePath}:{line}` scheme, so they open in your local VS Code.

## How it works

- **Tracking** — `AddBlazorInspector()` registers a custom `IComponentActivator` that observes every
  component instance as it's created (chaining to any pre-existing activator, e.g. bUnit's) and keeps
  weak references so it never extends component lifetimes.
- **Reading data in-process** — because the overlay runs in the *same .NET runtime* as the components,
  it reads the tree and values directly in C#. There's no client/server bridge. The parent/child
  hierarchy comes from reflecting the renderer's `ComponentState` map.
- **Jump-to-code** — a Roslyn `IIncrementalGenerator` emits a compile-time map of component type →
  `.razor` path/line into the consuming assembly, which the overlay reads via reflection.
- **JavaScript** — used only for the element picker (hover highlight + click-to-select). All
  tree/parameter/edit functionality is pure C#.

All reflection into framework internals is centralized in
[`BlazorInspector/RuntimeInternals.cs`](BlazorInspector/RuntimeInternals.cs) behind named constants,
documented with the .NET versions they were verified against (8.0, 9.0 & 10.0). The
`RuntimeInternalsTests.AllReflectedMembersResolve` test fails loudly if a future runtime renames one —
re-run the test suite when bumping the target framework.

## Limitations & known issues

- **Debug-only / Release no-op.** Trimming and WASM AOT can strip the reflected members, so the
  inspector disables itself when your app is built in Release — detected at runtime from your app
  assembly's `DebuggableAttribute` (the entry assembly, falling back to the assembly that calls
  `AddBlazorInspector`, since on MAUI Android/iOS/Mac Catalyst there is no managed entry assembly).
  Don't rely on it there. (The gate is a runtime check on *your* app's build, not a compile-time
  `#if DEBUG` in the package — the latter would be baked in when the package is compiled and could
  never react to how you build your app.) Two caveats, both solvable with an explicit
  `AddBlazorInspector(o => o.Enabled = true)`:
  - A build with `<DebugType>none</DebugType>` emits no `DebuggableAttribute`, so it is detected as
    Release and the inspector stays off.
  - On MAUI Android/iOS, call `AddBlazorInspector` directly from your app project (the usual
    `MauiProgram.cs`), not from a shared startup library — the calling assembly is the detection
    signal there, and a Release-built helper library would read as Release.
- **Element picker click-to-select is Blazor WASM only and relies on framework internals.** Mapping a
  clicked DOM node to a component id is done entirely in JS: the picker decodes each render batch (a
  raw WASM-memory pointer) through the exposed `Blazor.platform` reader using struct offsets that
  mirror the `RenderTree` layouts (verified on .NET 8/9/10), then walks Blazor's logical-element tree —
  reachable via the nodes' own Symbols — to recover each component's host node. It degrades to
  **hover-highlight only** (never throws) when those internals are absent — notably on **MAUI Blazor
  Hybrid**, which runs on the native runtime with no `Blazor.platform` and a different batch
  marshaling, so the picker there highlights and you select from the tree. One WASM edge case: a single
  render that *simultaneously* inserts and reorders sibling components in the same diff can briefly
  mis-map until the next render; the common cases (mount/unmount, nested components, state updates) are
  exact.
- **Editing a top-level `[Parameter]` is temporary.** It's reverted the next time the parent
  re-renders and re-supplies the parameter. Editing **state** and **nested** object/collection values
  persists.
- **Reflects private internals.** Member names can change across .NET versions — see the
  `RuntimeInternals` note above.

## Building from source

```bash
git clone https://github.com/kobrien603/BlazorInspector.git
cd BlazorInspector

dotnet build BlazorInspector.slnx          # builds the RCL (net8/9/10), generator, samples, tests
dotnet test tests/BlazorInspector.Tests    # unit + bUnit + source-generator tests (runs on net8/9/10)
dotnet run --project samples/SampleWasm     # manual test harness in the browser
```

The two sample apps (`samples/SampleWasm`, `samples/SampleMaui`) are the manual integration harness;
`samples/README.md` has a step-by-step manual test checklist.

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for the build/test workflow and
guidelines. Open an issue to discuss larger changes first, then send a PR. By contributing you agree
that your contributions are licensed under the project's Apache-2.0 license.

Release history is in [CHANGELOG.md](CHANGELOG.md).

## License

Licensed under the **Apache License 2.0** — see [LICENSE](LICENSE). It's permissive (free to use,
modify, and distribute), includes an explicit patent grant, and disclaims warranty/liability.

Copyright © kobrien603.
