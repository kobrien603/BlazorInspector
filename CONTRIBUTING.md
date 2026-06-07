# Contributing to BlazorInspector

Thanks for your interest in contributing! This is a small, focused debugging tool — bug reports,
fixes, and well-scoped features are all welcome.

## Ground rules

- **Open an issue first** for anything beyond a small fix, so we can agree on the approach before you
  invest time.
- **Keep the public surface tiny.** The intended public API is just `AddBlazorInspector()`,
  `<InspectorOverlay />`, and the options type. Everything else should be `internal`.
- **Debug-only.** The inspector must never run (or throw) in Release. Keep registration/overlay behind
  the `#if DEBUG` / options `Enabled` gating, and keep all reflection and value-getters fail-soft —
  degrade to partial data, never crash the host app.
- **No new runtime dependencies** beyond `Microsoft.AspNetCore.Components.Web`.

## Development setup

Requirements: the [.NET SDK](https://dotnet.microsoft.com/download) for the framework you're targeting
(8.0, 9.0, or 10.0). For the MAUI sample you'll also need the MAUI workload (`dotnet workload install maui`).

```bash
git clone https://github.com/kobrien603/BlazorInspector.git
cd BlazorInspector

dotnet build BlazorInspector.slnx          # RCL (net8/9/10), source generator, samples, tests
dotnet test tests/BlazorInspector.Tests    # runs on net8.0, net9.0, and net10.0
```

Run the manual harness and walk the checklist in [`samples/README.md`](samples/README.md):

```bash
dotnet run --project samples/SampleWasm
# MAUI (Windows): dotnet build -t:Run -f net10.0-windows10.0.19041.0 samples/SampleMaui/SampleMaui.csproj
```

## Project layout

| Path | What it is |
|---|---|
| `BlazorInspector/` | the Razor Class Library (the published package) |
| `BlazorInspector/RuntimeInternals.cs` | **all** reflection into framework internals, behind named, version-documented constants |
| `BlazorInspector.SourceGen/` | Roslyn source generator for jump-to-code |
| `samples/SampleWasm`, `samples/SampleMaui` | manual integration harnesses |
| `tests/BlazorInspector.Tests` | xUnit + bUnit + source-generator tests |

## Coding conventions

- C# with nullable reference types and implicit usings enabled.
- All reflection lives behind named constants in `RuntimeInternals.cs` — no scattered magic strings.
  If you add a reflected member, document the .NET version you verified it against and make sure
  `RuntimeInternalsTests.AllReflectedMembersResolve` still passes on net8/9/10.
- The overlay must not leak into the host: high z-index, `bi-`-prefixed scoped class names, and it
  must never be tracked by its own activator (the `BlazorInspector` namespace is skipped).
- **Razor gotcha:** a *string* component parameter written `Param="field"` passes the literal text,
  not the value — use `Param="@field"`. And never put `#if DEBUG` inside `.razor` markup (it renders
  literally); gate at runtime via the options `Enabled` flag.

## Tests

- Unit-test reflection-free logic (registry, parameter/value reading, the source generator) with fake
  `IComponent` types and the in-process generator driver.
- bUnit covers overlay rendering. The renderer-reflection, picker, and source-link features are
  exercised through the sample apps (see `samples/README.md`).
- Please add or update tests for any behavior change, and make sure the full suite passes on all three
  target frameworks before opening a PR.

## Submitting a pull request

1. Fork the repo and create a branch from `main`.
2. Make your change with tests; ensure `dotnet build` and `dotnet test` are green on net8/9/10.
3. Keep the change focused and the diff small; update `README.md` / `samples/README.md` if behavior
   changes.
4. Open the PR with a clear description of the what and why.

By submitting a contribution, you agree that it is licensed under the project's
[Apache License 2.0](LICENSE).
