import { build } from 'esbuild';
await build({ entryPoints: ['src/scene.ts'], bundle: true, format: 'esm', minify: true,
  target: ['es2022'], outfile: '../../GaoApp.Web/wwwroot/Admin/js/store-monitor.3d.bundle.js',
  legalComments: 'eof', sourcemap: false });
