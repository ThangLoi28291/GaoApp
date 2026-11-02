# Store monitor 3D

Browser-only scene for `/admin/store-monitor`. The Razor page and its existing SSE connection remain the data/permission boundary. No React runtime, remote model URLs, demo cash registers, or synthetic business API.

## Build

From the repository root:

```powershell
npm ci --prefix tools/store-monitor-3d
npm run build --prefix tools/store-monitor-3d
npm test --prefix tools/store-monitor-3d
node --test GaoApp.Tests/Ui/store-monitor-3d.test.cjs
```

Commit/publish `GaoApp.Web/wwwroot/Admin/js/store-monitor.3d.bundle.js` with the source. The .NET server needs no Node runtime or npm install. Dependencies are pinned by `package-lock.json`.

## Sources

- `engine.ts`: explicit Babylon.js 9.0.0 imports, bundled locally.
- `scene.ts`: actual geometry, actor models, camera/selection and rendering lifecycle.
- `live-world.mjs`: read-only state driven by committed snapshots. One employee identity, latest work wins, no replay on initialization/restart. Customer transitions and trips never write business data.
- `store-layout.ts`: collision-aware routing adapted from the supplied GaoStore3D-Demo-R05, with the accepted receiving/warehouse/office/label/checkout layout.
- `delivery-road.mjs`: rounded road circuit (~30 seconds/lap), parking, boarding and return routing. No Area B building or automatic delivery confirmation.
- `retail-character.js`, `retail-motorbike.ts`: procedural models from the supplied R05 source; reduced geometry, an office seated pose and interpolated mounting/dismounting poses.
- `GaoApp.Web/wwwroot/Admin/js/store-monitor.3d.js`: lazy loading, responsive composition and allowlisted details.

Babylon.js is Apache-2.0; its license is shipped beside the static assets. R05 simulation/controller/customer-price code is not used.

## Rendering

Target 30 FPS, no dynamic shadow pass, merged static meshes and a capped device pixel ratio. Hidden tabs and 2D stop the render loop. Disconnected/effects-off scenes keep saved facts but stop movement, rendering only updates and camera interaction. WebGL/context failure falls back to 2D.

The 1366×768 reference composition scales uniformly to smaller available windows, regardless of the physical monitor size. Larger windows use the additional area. Live details may scroll inside their own panel.

Browser tests use real Babylon/WebGL via Chrome SwiftShader plus a controlled SSE fixture; they verify functionality and layout, not hardware GPU performance. SQL/HTTP tests run against fixture-owned databases, not the shop database.
