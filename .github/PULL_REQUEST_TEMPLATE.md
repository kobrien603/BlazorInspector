<!-- Thanks for contributing! Please keep PRs focused and the diff small. -->

## Summary

<!-- What does this change and why? Link any related issue: "Closes #123" -->

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Refactor / cleanup
- [ ] Docs only

## Checklist

- [ ] `dotnet test tests/BlazorInspector.Tests` passes on **net8.0, net9.0, and net10.0**
- [ ] Added/updated tests for the change
- [ ] The inspector still no-ops in Release (no behavior leaks past `#if DEBUG` / `Options.Enabled`)
- [ ] Reflection (if any) lives in `RuntimeInternals.cs` behind named, version-documented constants
- [ ] Public surface unchanged (or intentionally extended — call it out below)
- [ ] Updated `README.md` / `samples/README.md` if behavior changed
- [ ] Added a `CHANGELOG.md` entry under `[Unreleased]` for user-facing changes
- [ ] If a sample/manual step is involved, verified it in `samples/SampleWasm` (and MAUI if relevant)

## Notes for reviewers

<!-- Anything tricky: framework internals touched, runtime-specific behavior, manual verification done, etc. -->
