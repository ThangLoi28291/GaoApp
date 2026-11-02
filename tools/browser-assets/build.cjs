/* Rebuild with npm ci --ignore-scripts && npm run build. No CDN runtime fallback. */
'use strict';
const fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const acorn = require('acorn');
const root = path.resolve(__dirname, '../..'), web = path.join(root, 'GaoApp.Web/wwwroot');
const manifest = [], packages = require('./package.json').dependencies;
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
function save(relative, bytes) {
  const target = path.join(web, relative); fs.mkdirSync(path.dirname(target), { recursive: true });
  if (fs.existsSync(target) && fs.readFileSync(target).equals(Buffer.from(bytes))) return;
  fs.writeFileSync(target, bytes);
}
function copy(name, source, destinations) {
  const directory = path.dirname(require.resolve(name + '/package.json'));
  const bytes = fs.readFileSync(path.join(directory, source));
  for (const destination of destinations) {
    save(destination, bytes);
    manifest.push({ file: destination, package: name, version: packages[name], source,
      registry: 'https://registry.npmjs.org/' + name, sha256: hash(bytes) });
  }
  const license = fs.readdirSync(directory).find(f => /(?:^|[-_])licen[cs]e(?:\.|$)/i.test(f));
  const licensePath = license ? path.join(directory, license) : path.join(__dirname, 'licenses', name === '@microsoft/signalr' ? 'signalr.txt' : name.replace(/[@/]/g, '_') + '.txt');
  if (!fs.existsSync(licensePath)) throw new Error('License not found: ' + name);
  save('lib/licenses/' + name.replace(/[@/]/g, '_') + '.txt', fs.readFileSync(licensePath));
}
copy('jquery', 'dist/jquery.min.js', ['lib/jquery/dist/jquery.min.js', 'vendor/libs/jquery/jquery.js']);
copy('bootstrap', 'dist/js/bootstrap.bundle.min.js', ['lib/bootstrap/dist/js/bootstrap.bundle.min.js', 'vendor/js/bootstrap.js']);
copy('bootstrap', 'dist/css/bootstrap.min.css', ['lib/bootstrap/dist/css/bootstrap.min.css']);
copy('@popperjs/core', 'dist/umd/popper.min.js', ['vendor/libs/popper/popper.js']);
copy('@microsoft/signalr', 'dist/browser/signalr.min.js', ['lib/signalr/signalr.min.js']);
copy('toastr', 'build/toastr.min.js', ['lib/toastr/toastr.min.js']);
copy('toastr', 'build/toastr.min.css', ['lib/toastr/toastr.min.css']);
copy('select2', 'dist/js/select2.min.js', ['lib/select2/select2.min.js']);
copy('select2', 'dist/css/select2.min.css', ['lib/select2/select2.min.css']);
copy('select2-bootstrap-5-theme', 'dist/select2-bootstrap-5-theme.min.css', ['lib/select2/select2-bootstrap-5-theme.min.css']);
copy('filepond', 'dist/filepond.min.js', ['lib/filepond/filepond.min.js']);
copy('filepond', 'dist/filepond.min.css', ['lib/filepond/filepond.min.css']);
copy('filepond-plugin-image-preview', 'dist/filepond-plugin-image-preview.min.js', ['lib/filepond/filepond-plugin-image-preview.min.js']);
copy('filepond-plugin-image-preview', 'dist/filepond-plugin-image-preview.min.css', ['lib/filepond/filepond-plugin-image-preview.min.css']);
copy('jquery-validation', 'dist/jquery.validate.min.js', ['lib/jquery-validation/dist/jquery.validate.min.js']);
copy('jquery-validation-unobtrusive', 'dist/jquery.validate.unobtrusive.min.js', ['lib/jquery-validation-unobtrusive/jquery.validate.unobtrusive.min.js']);
copy('sweetalert2', 'dist/sweetalert2.all.min.js', ['lib/sweetalert2/sweetalert2.all.min.js']);
copy('jsbarcode', 'dist/JsBarcode.all.min.js', ['lib/jsbarcode/JsBarcode.all.min.js']);
copy('qz-tray', 'qz-tray.js', ['Admin/js/printing/qz-tray.js']);
copy('@zxing/browser', 'umd/zxing-browser.min.js', ['lib/zxing-browser/0.1.5/zxing-browser.min.js']);

const fontDirectory = path.dirname(require.resolve('@fontsource/public-sans/package.json'));
let css = '/* Public Sans; SIL Open Font License 1.1. Self-hosted; no Google Fonts request. */\n';
for (const subset of ['vietnamese', 'latin-ext', 'latin']) for (const weight of [300, 400, 500, 600, 700]) for (const style of ['normal', 'italic']) {
  const file = `public-sans-${subset}-${weight}-${style}.woff2`;
  const upstream = fs.readFileSync(path.join(fontDirectory, `${weight}${style === 'italic' ? '-italic' : ''}.css`), 'utf8');
  const face = upstream.match(new RegExp('@font-face\\s*\\{[^}]*' + file.replaceAll('.', '\\.') + '[^}]*\\}', 's'));
  if (!face) throw new Error('Font face not found: ' + file);
  css += face[0].replaceAll('./files/', './') + '\n';
  copy('@fontsource/public-sans', 'files/' + file, ['lib/fonts/public-sans/' + file]);
}
save('lib/fonts/public-sans/public-sans.css', css);
manifest.push({ file: 'lib/fonts/public-sans/public-sans.css', package: '@fontsource/public-sans', version: packages['@fontsource/public-sans'], sha256: hash(css) });

// The existing template bundles contain webpack's eval-based development wrapper.
// Parse literals as data; never execute downloaded/source strings to unwrap them.
for (const template of require('./template-bundles.json')) {
  const relative = template.file;
  const target = path.join(web, relative); let source = fs.readFileSync(target, 'utf8');
  if (![template.originalSha256, template.sha256].includes(hash(source))) throw new Error('Unreviewed template change: ' + relative);
  if (template.package && packages[template.package] !== template.version) throw new Error('Template upgrade requires rebuilding/reviewing its actual bundled code: ' + relative);
  const replacements = [];
  function walk(node, parent) {
    if (!node || typeof node !== 'object') return;
    if (node.type === 'CallExpression' && node.callee.type === 'Identifier' && node.callee.name === 'eval') {
      if (parent.type !== 'ExpressionStatement' || node.arguments.length !== 1 || typeof node.arguments[0].value !== 'string')
        throw new Error('Unexpected eval; manual review required: ' + relative);
      const literal = node.arguments[0].value.replace(/\/\/[#@] source(?:Mapping)?URL=.*$/gm, '');
      acorn.parse(literal, { ecmaVersion: 'latest', allowReturnOutsideFunction: true });
      replacements.push({ start: parent.start, end: parent.end, text: literal });
      return;
    }
    for (const value of Object.values(node)) if (Array.isArray(value)) value.forEach(v => walk(v, node)); else if (value && typeof value === 'object') walk(value, node);
  }
  walk(acorn.parse(source, { ecmaVersion: 'latest' }), null);
  for (const change of replacements.sort((a, b) => b.start - a.start)) source = source.slice(0, change.start) + change.text + source.slice(change.end);
  // Ensure the generated file is valid JS and no executable eval survived.
  let evalCount = 0;
  function check(node) { if (!node || typeof node !== 'object') return; if (node.type === 'CallExpression' && node.callee.name === 'eval') evalCount++; for (const v of Object.values(node)) if (Array.isArray(v)) v.forEach(check); else if (v && typeof v === 'object') check(v); }
  check(acorn.parse(source, { ecmaVersion: 'latest' })); if (evalCount) throw new Error('Nested eval: ' + relative);
  if (hash(source) !== template.sha256) throw new Error('Unexpected transformed template: ' + relative);
  save(relative, source);
  manifest.push({ file: relative, ...(template.package ? { package: template.package, version: template.version } : {}),
    originalSha256: template.originalSha256,
    source: 'Existing repository template bundle; webpack eval literals parsed into ordinary module code', sha256: hash(source) });
}
fs.mkdirSync(path.join(root, 'eng/security'), { recursive: true });
fs.writeFileSync(path.join(root, 'eng/security/browser-assets.manifest.json'), JSON.stringify({ format: 1, assets: manifest }, null, 2) + '\n');
console.log('Wrote ' + manifest.length + ' pinned assets; no scripts were evaluated during transformation.');
