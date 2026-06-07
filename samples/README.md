# Sample apps — manual test harness

These two apps are the manual integration harness for the renderer-reflection, element picker, and
source-link features that are hard to unit-test. Both reference the `BlazorInspector` RCL by project
reference and wire it up with the standard two-line integration (`AddBlazorInspector()` +
`<InspectorOverlay />`).

- **SampleWasm** — Blazor WebAssembly (`net10.0`). The primary, fast harness. Run it in the browser.
- **SampleMaui** — MAUI Blazor Hybrid (`net10.0`). Builds and runs on the Windows target; the overlay,
  tree, and reflection were verified live in the `BlazorWebView` (see **MAUI notes**).

Run in **Debug** — the inspector is a no-op in Release.

## Run SampleWasm

```bash
dotnet run --project samples/SampleWasm
```

Open the printed `http://localhost:<port>` URL.

## Manual test checklist

### Phase 1 — flat data
- [ ] The 🔍 floating button appears in the corner.
- [ ] Clicking it opens the panel; the count badge shows a non-zero number of live components.
- [ ] On the Counter page, after clicking **Click me**, select the `Counter` node — a **State**
      section shows `currentCount` with its live value (component state, not just parameters).
- [ ] Selecting a component shows its parameters; values are formatted, not raw dumps:
  - [ ] `RenderFragment` shows `<fragment>`, `EventCallback` shows `<event>`.
  - [ ] Collections show `Type (n items)`.
  - [ ] Nothing throws when a component has null / fragment / callback parameters.
- [ ] On the **Weather** page, select the `Weather` component and expand the `forecasts` state value:
      it expands to rows `[0]…[4]`, and each row expands to its `Date` / `TemperatureC` / `Summary`
      properties. Dictionaries expand to keyed entries; nested objects expand to their members.
- [ ] **Inline edit:** click a row's `TemperatureC` value, type a number, press **Enter** — the value
      and the computed `TemperatureF` update in the page's table immediately. `TemperatureF` itself is
      computed (no setter) and is not editable. Press **Esc** (or click away) to cancel an edit.

### Phase 2 — tree + live refresh
- [ ] The panel shows a real parent/child hierarchy (e.g. `App` → `Router`/`MainLayout` → `NavMenu`,
      pages), not a flat list.
- [ ] Navigate between pages (Home / Counter / Weather): the tree updates on its own without clicking
      refresh.
- [ ] On the Counter page, click **Click me**, then re-select the `Counter` node — its parameter/state
      reflects the change after the auto-refresh.
- [ ] Expand some nodes, then let it refresh — expansion and the selected node are preserved.

### Phase 3a — element picker (highlight-only on current runtimes)
- [ ] Click the ⌖ picker button, then hover the page: the element under the cursor is highlighted.
- [ ] Press **Esc** to cancel without picking.
- [ ] Click an element: the highlight clears and a one-time console note explains that click-to-select
      is unavailable on this runtime. This is expected — see the picker limitation in the root README:
      on .NET 10 WASM the render batch is an opaque pointer, so DOM↔componentId correlation isn't
      JS-reachable. Select components from the tree instead.

### Phase 3b — jump-to-code
- [ ] A selected node shows a `</> source` link.
- [ ] Clicking it opens the component's `.razor` file in VS Code at roughly the right line.

## Run SampleMaui (Windows)

```bash
dotnet build -t:Run -f net10.0-windows10.0.19041.0 samples/SampleMaui/SampleMaui.csproj
```

`WindowsPackageType` is `None`, so it runs as a plain exe. To inspect the embedded `BlazorWebView`
headlessly (e.g. for automated checks), set `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`
before launching, then attach a CDP client (e.g. Playwright `connectOverCDP`) to `http://localhost:9222`.

## MAUI notes (verified .NET 10, 2026-06-07)

- The JS initializer (`BlazorInspector.lib.module.js`) **does** fire inside the `BlazorWebView` on
  .NET 10 — `afterStarted` runs and `window.blazorInspector` is exposed. No host-page registration
  fallback is needed. (Verified by attaching to the WebView2 over CDP.)
- The overlay renders, the live-component badge populates, and the renderer-reflection tree works in
  the native MAUI runtime — confirmed live.
- Picker is highlight-only here too. Note `correlationKind()` reports `unknown` in `BlazorWebView`
  (vs `pointer` in WASM): BlazorWebView marshals render batches over IPC, so the JS hook never sees a
  decodable batch. The click degrades gracefully with the same console note.
- If you force-rebuild the RCL's `wwwroot` JS, MAUI's incremental build may keep a stale copy — use
  `--no-incremental` on the SampleMaui build to refresh the bundled static asset.
- Re-run `RuntimeInternalsTests` after any TFM bump — the reflected member names are version-specific.
