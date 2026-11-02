'use strict';
// Independent actual-browser acceptance. Network interception below only injects
// declared failures/unknown responses; successful business replies come from Kestrel.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');
const hash = x => crypto.createHash('sha256').update(x).digest('hex').toUpperCase();
(async () => {
  const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', x => text += x); process.stdin.on('end', () => resolve(text)); }));
  const boundedDecode = value => { let text = String(value); for (let i = 0; i < 4 && text.length < 1000000; i++) { try { const decoded = decodeURIComponent(text); if (decoded === text) break; text = decoded; } catch { break; } } return text; };
  const safe = text => boundedDecode(text).replace(/\/shop\/orders#[A-Za-z0-9_-]+/g, '/shop/orders#[REDACTED]').replace(/\b[A-Za-z0-9_-]{43}\b/g, '[REDACTED43]');
  const containsCapability = (text, secret) => String(text).includes(secret) || boundedDecode(text).includes(secret);
  const report = { mode: info.mode, results: [], measurements: [], pixels: [], assets: [], requests: [], failures: [], errors: [], states: [], performance: [], physicalPhone: false, linuxRuntime: false };
  const browser = await chromium.launch({ channel: 'chrome', headless: true }); report.browserVersion = browser.version();
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1.5, hasTouch: true, timezoneId: 'Asia/Bangkok', reducedMotion: info.reduced ? 'reduce' : 'no-preference' });
  const rawRequests = [], assetTasks = []; let page;
  const test = async (name, body) => { try { await body(); report.results.push({ name, passed: true }); } catch (error) { report.results.push({ name, passed: false, error: safe(error.stack) }); throw error; } };
  const delay = ms => new Promise(r => setTimeout(r, ms));
  const urlMetadata = raw => { const u = new URL(raw); return { origin: u.origin === info.baseUrl ? 'owned-public' : u.protocol === 'blob:' ? 'owned-blob' : safe(u.origin), path: safe(u.pathname), queryKeys: [...u.searchParams.keys()].map(safe) }; };
  const installNativeProbe = () => {
    const contexts = [], blobs = new Set();
    const records = { contexts: 0, maxActive: 0, draws: 0, contextInfo: [], bufferUploads: [], blobCreated: 0, blobRevoked: 0, states: [] };
    Object.defineProperty(records, 'active', { enumerable: true, get: () => contexts.filter(x => !x.isContextLost()).length });
    Object.defineProperty(records, 'liveBlobs', { enumerable: true, get: () => blobs.size });
    Object.defineProperty(window, '__w8Native', { value: records });
    const original = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function (kind, ...args) {
      const result = original.call(this, kind, ...args);
      if (result && /^webgl/.test(kind) && !this.__w8Context) {
        this.__w8Context = result; records.contexts++; contexts.push(result); records.maxActive = Math.max(records.maxActive, records.active);
        const debug = result.getExtension('WEBGL_debug_renderer_info');
        records.contextInfo.push({ kind, version: result.getParameter(result.VERSION), renderer: result.getParameter(debug?.UNMASKED_RENDERER_WEBGL || result.RENDERER), vendor: result.getParameter(debug?.UNMASKED_VENDOR_WEBGL || result.VENDOR) });
      }
      return result;
    };
    for (const ctor of [window.WebGLRenderingContext, window.WebGL2RenderingContext].filter(Boolean)) {
      for (const name of ['drawArrays', 'drawElements', 'drawArraysInstanced', 'drawElementsInstanced'].filter(n => ctor.prototype[n])) {
        const real = ctor.prototype[name]; ctor.prototype[name] = function (...args) { records.draws++; return real.apply(this, args); };
      }
      const real = ctor.prototype.bufferData;
      ctor.prototype.bufferData = function (target, input, ...args) { records.bufferUploads.push(typeof input === 'number' ? input : input?.byteLength ?? 0); return real.call(this, target, input, ...args); };
    }
    const create = URL.createObjectURL, revoke = URL.revokeObjectURL;
    URL.createObjectURL = function (...args) { records.blobCreated++; const url = create.apply(this, args); blobs.add(url); return url; };
    URL.revokeObjectURL = function (...args) { records.blobRevoked++; blobs.delete(args[0]); return revoke.apply(this, args); };
    document.addEventListener('storefront-viewer-state', e => records.states.push({ ...e.detail, at: performance.now() }));
  };
  await context.addInitScript(installNativeProbe);
  function observe(p) {
    p.on('pageerror', e => report.errors.push(safe(e.message)));
    p.on('request', r => { rawRequests.push({ url: r.url(), method: r.method(), headers: r.headers() }); report.requests.push({ ...urlMetadata(r.url()), method: r.method() }); });
    p.on('requestfailed', r => report.failures.push({ ...urlMetadata(r.url()), reason: r.failure()?.errorText }));
    p.on('response', response => {
      const u = new URL(response.url());
      if (u.origin === info.baseUrl && u.pathname.startsWith('/assets/') && response.status() === 200)
        assetTasks.push((async () => { const bytes = await response.body(); report.assets.push({ path: u.pathname, queryKeys: [...u.searchParams.keys()], status: response.status(), method: response.request().method(), bytes: bytes.length, sha256: hash(bytes) }); })().catch(e => { report.assets.push({ path: u.pathname, captureError: safe(e.message) }); }));
    });
  }
  const settle = async () => page.evaluate(async () => { await document.fonts.ready; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); });
  const goto = async route => { const response = await page.goto(info.baseUrl + route, { timeout: 30000 }); assert.equal(response.status(), 200); await settle(); };
  const shot = async name => page.screenshot({ path: path.join(info.output, name + '.png'), fullPage: true, mask: [page.locator('#guest-link-input,a[href*="/shop/orders#"],input[value*="/shop/orders#"]')], maskColor: '#222222' });
  const state = async value => page.waitForFunction(expected => document.querySelector('[data-product-viewer]')?.dataset.state === expected && document.querySelector('#product-viewer-status')?.dataset.state === expected, value, { timeout: 22000 });
  const current = async () => page.evaluate(() => JSON.parse(JSON.stringify(window.__w8Native)));
  const checkpoint = async (name, reply) => {
    fs.writeFileSync(path.join(info.output, name), 'Owned WEB08 checkpoint; no capability.');
    const end = Date.now() + 25000;
    while (!fs.existsSync(path.join(info.output, reply))) { if (Date.now() > end) throw Error('Actual SQL checkpoint did not complete: ' + reply); await delay(50); }
  };
  async function review(label, widths = [320, 360, 390, 430, 768, 1440]) {
    for (const width of widths) {
      await page.setViewportSize({ width, height: width >= 768 ? 1000 : 844 }); await settle();
      if (label === 'detail-real-3d') {
        await page.locator('.detail-main-image').scrollIntoViewIfNeeded();
        if (await page.locator('[data-product-viewer]').getAttribute('data-state') !== 'ready') await openAndDraw();
        await page.locator('#product-viewer-canvas').waitFor({ state: 'visible' });
        assert.equal(await page.locator('[data-product-viewer]').getAttribute('data-state'), 'ready');
      }
      const metrics = await page.evaluate(() => {
        const visible = e => { const r = e.getBoundingClientRect(); return r.width > 1 && r.height > 1 && getComputedStyle(e).visibility !== 'hidden' && !e.closest('[hidden],.sr-only'); };
        const rgb = text => (text.match(/[\d.]+/g) || []).map(Number);
        const blend = (a, b) => a.slice(0, 3).map((x, i) => x * (a[3] ?? 1) + b[i] * (1 - (a[3] ?? 1)));
        const bg = e => { const layers = []; for (let n = e; n; n = n.parentElement) { const c = rgb(getComputedStyle(n).backgroundColor); layers.push(c); if ((c[3] ?? 1) === 1) break; } return layers.reverse().reduce((b, a) => blend(a, b), [255, 255, 255]); };
        const lum = color => color.map(x => x / 255).map(x => x <= .04045 ? x / 12.92 : ((x + .055) / 1.055) ** 2.4).reduce((s, x, i) => s + x * [.2126, .7152, .0722][i], 0);
        const contrast = [...document.querySelectorAll('body *')].filter(e => visible(e) && !e.closest('button:disabled,option,svg,script,style') && [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim())).map(e => {
          const s = getComputedStyle(e), b = bg(e), a = lum(blend(rgb(s.color), b)), z = lum(b), large = parseFloat(s.fontSize) >= 24 || parseFloat(s.fontSize) >= 18.66 && parseInt(s.fontWeight, 10) >= 700;
          return { text: e.textContent.trim().slice(0, 60), ratio: (Math.max(a, z) + .05) / (Math.min(a, z) + .05), threshold: large ? 3 : 4.5 };
        });
        const targets = [...document.querySelectorAll('button,input:not([type=hidden]),select,textarea,.button,.icon-button,.bottom-nav a')].filter(visible).map(e => { const t = e.type === 'checkbox' && [...(e.labels || [])].find(l => l.control === e) || e, r = t.getBoundingClientRect(); return { id: e.id || e.getAttribute('aria-label') || e.tagName, width: r.width, height: r.height }; });
        return { width: innerWidth, scrollWidth: document.documentElement.scrollWidth, bodyFont: parseFloat(getComputedStyle(document.body).fontSize), inter: [...document.fonts].some(f => f.family === 'Inter' && f.status === 'loaded'),
          smallTargets: targets.filter(x => x.width < 43.9 || x.height < 43.9), contrastFailures: contrast.filter(x => x.ratio + .02 < x.threshold), minimumContrast: Math.min(...contrast.map(x => x.ratio)), contrastSamples: contrast.length };
      });
      report.measurements.push({ label, ...metrics }); await shot(label + '-' + width);
      assert.ok(metrics.scrollWidth <= width + 1, JSON.stringify(metrics)); assert.ok(metrics.bodyFont >= 15.9 && metrics.inter);
      assert.deepEqual(metrics.smallTargets, []); assert.deepEqual(metrics.contrastFailures, []);
    }
  }
  async function buyRemainsUsable() {
    await page.locator('#variant-choice').selectOption(String(info.primaryVariant)); await page.locator('#unit-choice').selectOption(String(info.baseUnit));
    await page.locator('#product-quantity').fill('1');
    const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/cart/resolve' && r.request().method() === 'POST');
    await page.locator('#product-choice button[type=submit]').click(); assert.equal((await response).status(), 200);
    await page.locator('#toast').waitFor({ state: 'visible' }); assert.equal(await page.locator('#product-choice button[type=submit]').isEnabled(), true);
  }
  async function openAndDraw() {
    await page.locator('#product-viewer-open').click(); await state('ready');
    await page.locator('#product-viewer-canvas').waitFor({ state: 'visible' }); const native = await current();
    assert.ok(native.draws > 0 && native.active === 1 && native.maxActive <= 1, JSON.stringify(native));
    const size = await page.locator('#product-viewer-canvas').evaluate(c => ({ width: c.width, height: c.height, cssWidth: c.clientWidth, cssHeight: c.clientHeight, dpr: devicePixelRatio }));
    report.measurements.push({ label: 'actual-render-target', ...size, native }); assert.ok(size.width * size.height <= 1000000);
    if ((await page.viewportSize()).width <= 700) assert.ok(size.width / size.cssWidth <= 1.5 + .01);
  }
  async function verifyActualFontGlyphs() {
    const cdp = await context.newCDPSession(page); await cdp.send('DOM.enable'); await cdp.send('CSS.enable');
    const doc = await cdp.send('DOM.getDocument'), selected = await cdp.send('DOM.querySelector', { nodeId: doc.root.nodeId, selector: 'h1' });
    const fonts = await cdp.send('CSS.getPlatformFontsForNode', { nodeId: selected.nodeId }); await cdp.detach();
    const keys = [...new Set(rawRequests.map(r => r.url).filter(url => new URL(url).pathname === '/assets/fonts/InterVariable.woff2'))];
    report.fontProof = { fonts: fonts.fonts, resourceKeys: keys.map(url => ({ ...urlMetadata(url), keySHA256: hash(url) })), actualText: safe(await page.locator('h1').innerText()) };
    assert.equal(keys.length, 1, 'Preload and used @font-face must share exactly one regular-font resource key');
    assert.ok(fonts.fonts.some(f => /Inter/i.test(f.familyName) && f.glyphCount > 0 && f.isCustomFont), JSON.stringify(fonts));
    assert.equal(await page.evaluate(() => document.fonts.check('16px Inter', 'Tiếng Việt Ắ ồ ư đ')), true);
  }
  async function verifyNativeHiddenLifecycle() {
    // The main profiled context stays intact. Playwright's normal focus capture
    // prevents native tab hiding; only this dedicated default context opts out.
    const { spawn, execFile } = require('node:child_process');
    const execute = require('node:util').promisify(execFile);
    const executable = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
    const profile = path.join(fs.realpathSync(info.output), 'native-visibility-profile-' + crypto.randomUUID().replaceAll('-', ''));
    const nativeSafe = text => safe(text).replace(/ws:\/\/127\.0\.0\.1:\d+\/[^\s]+/g, '[OWNED-CDP]');
    const listenerOpen = port => new Promise(resolve => {
      const socket = require('node:net').createConnection({ host: '127.0.0.1', port });
      const done = open => { socket.destroy(); resolve(open); };
      socket.once('connect', () => done(true)); socket.once('error', () => done(false)); socket.setTimeout(2000, () => done(false));
    });
    const identities = async ids => {
      assert.ok(ids.length > 0 && ids.every(id => Number.isSafeInteger(id) && id > 0));
      const query = 'SELECT ProcessId,ParentProcessId,Name,ExecutablePath,CreationDate FROM Win32_Process WHERE ' + ids.map(id => 'ProcessId=' + id).join(' OR ');
      const { stdout } = await execute('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', "$ErrorActionPreference='Stop'; @(Get-CimInstance -Query '" + query + "') | ConvertTo-Json -Depth 4 -Compress"], { windowsHide: true, timeout: 20000 });
      const parsed = stdout.trim() ? JSON.parse(stdout) : []; return { atUtc: new Date().toISOString(), query, rows: Array.isArray(parsed) ? parsed : [parsed] };
    };
    assert.equal(fs.existsSync(profile), false);
    const proof = report.nativeHiddenLifecycle = { sameActualProduct: info.productId, separateFromProfiledLab: true, noDomPropertyOrEventOverride: true,
      executable, executableSHA256Before: hash(fs.readFileSync(executable)), sourceSHA256: hash(fs.readFileSync(__filename)), nodePid: process.pid,
      profile, profileInitiallyAbsent: true, profileDisposition: 'Retained; no private contents read or deleted', noDefaults: true, defaultContextOnly: true };
    proof.nodeIdentity = await identities([process.pid]);
    fs.writeFileSync(path.join(info.output, 'native-visibility-before-create.json'), JSON.stringify(proof, null, 2));
    fs.mkdirSync(profile);
    const args = ['--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0', '--user-data-dir=' + profile,
      '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-component-update', '--disable-sync', '--disable-extensions', '--disable-features=AutoDeElevate', '--no-startup-window'];
    proof.args = args;
    const child = spawn(executable, args, { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] });
    // Attach before any possible exit, rather than after Browser.close.
    const childExit = new Promise(resolve => child.once('exit', (code, signal) => resolve({ atUtc: new Date().toISOString(), code, signal })));
    let nativeBrowser, control, owner, knownOwners = [];
    try {
      const endpoint = await new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(Error('Owned native Chrome endpoint timeout')), 20000);
        child.once('error', error => { clearTimeout(timer); reject(error); });
        let buffered = '';
        child.stderr.on('data', chunk => { buffered = (buffered + String(chunk)).slice(-16384); const found = buffered.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/[^\s]+)/); if (found) { clearTimeout(timer); resolve(found[1]); } });
      });
      const address = new URL(endpoint); assert.equal(address.hostname, '127.0.0.1');
      proof.endpoint = { loopback: true, port: Number(address.port), sha256: hash(endpoint), rawEndpointPersisted: false };
      proof.loopbackListenerOpenBefore = await listenerOpen(proof.endpoint.port); assert.equal(proof.loopbackListenerOpenBefore, true);
      proof.chromePid = child.pid; proof.ownerBefore = await identities([child.pid]);
      assert.equal(proof.ownerBefore.rows.length, 1); owner = proof.ownerBefore.rows[0];
      assert.equal(owner.ProcessId, child.pid); assert.equal(owner.ExecutablePath.toLowerCase(), executable.toLowerCase());
      fs.writeFileSync(path.join(info.output, 'native-visibility-owner-created.json'), JSON.stringify(proof, null, 2));
      nativeBrowser = await chromium.connectOverCDP(endpoint, { noDefaults: true, isLocal: true, timeout: 10000 });
      proof.browserVersion = nativeBrowser.version(); assert.equal(proof.browserVersion, browser.version());
      const contexts = nativeBrowser.contexts(); assert.equal(contexts.length, 1); const nativeContext = contexts[0];
      await nativeContext.addInitScript(installNativeProbe);
      await nativeContext.addInitScript(() => { window.__w8Visibility = []; document.addEventListener('visibilitychange', event => window.__w8Visibility.push({ trusted: event.isTrusted, hidden: document.hidden, state: document.visibilityState, at: performance.now() })); });
      const nativePage = await nativeContext.newPage(); observe(nativePage); await nativePage.bringToFront();
      await nativePage.setViewportSize({ width: 390, height: 844 });
      const metricsSession = await nativeContext.newCDPSession(nativePage);
      await metricsSession.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1.5, mobile: false });
      await metricsSession.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 1 });
      await metricsSession.send('Emulation.setTimezoneOverride', { timezoneId: 'Asia/Bangkok' });
      await metricsSession.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: info.reduced ? 'reduce' : 'no-preference' }] });
      const response = await nativePage.goto(info.baseUrl + '/shop/products/' + info.productId, { timeout: 30000 }); assert.equal(response.status(), 200);
      proof.actualProfile = await nativePage.evaluate(() => ({ width: innerWidth, height: innerHeight, dpr: devicePixelRatio, maxTouchPoints: navigator.maxTouchPoints, timezone: Intl.DateTimeFormat().resolvedOptions().timeZone, reduced: matchMedia('(prefers-reduced-motion: reduce)').matches }));
      assert.deepEqual(proof.actualProfile, { width: 390, height: 844, dpr: 1.5, maxTouchPoints: 1, timezone: 'Asia/Bangkok', reduced: !!info.reduced });
      const read = () => nativePage.evaluate(() => JSON.parse(JSON.stringify(window.__w8Native)));
      const waitState = value => nativePage.waitForFunction(expected => document.querySelector('[data-product-viewer]')?.dataset.state === expected && document.querySelector('#product-viewer-status')?.dataset.state === expected, value, { timeout: 22000 });
      const draw = async label => {
        const before = await read(); await nativePage.locator('#product-viewer-open').click(); await waitState('ready');
        const canvas = nativePage.locator('#product-viewer-canvas'); await canvas.waitFor({ state: 'visible' }); const native = await read();
        assert.ok(native.draws > before.draws && native.active === 1 && native.maxActive <= 1, JSON.stringify(native));
        const size = await canvas.evaluate(c => ({ width: c.width, height: c.height, cssWidth: c.clientWidth })); assert.ok(size.width * size.height <= 1000000 && size.width / size.cssWidth <= 1.51);
        const canvasSHA256 = hash(await canvas.screenshot({ path: path.join(info.output, label + '.png') })); return { native, size, canvasSHA256 };
      };
      control = await nativeBrowser.newBrowserCDPSession();
      const processInfo = (await control.send('SystemInfo.getProcessInfo')).processInfo;
      assert.ok(processInfo.some(item => item.type === 'browser' && item.id === child.pid));
      proof.initialDraw = await draw('native-visibility-actual-draw');
      const other = await nativeContext.newPage(); await other.goto('about:blank'); await other.bringToFront();
      await nativePage.waitForFunction(() => document.hidden && document.visibilityState === 'hidden' && window.__w8Visibility.some(event => event.trusted && event.hidden && event.state === 'hidden'));
      await waitState('closed'); await nativePage.waitForFunction(() => window.__w8Native.active === 0 && window.__w8Native.liveBlobs === 0);
      proof.hidden = await nativePage.evaluate(() => ({ hidden: document.hidden, visibility: document.visibilityState, events: window.__w8Visibility, native: JSON.parse(JSON.stringify(window.__w8Native)) }));
      assert.equal(proof.hidden.hidden, true); assert.equal(proof.hidden.native.active, 0); assert.equal(proof.hidden.native.liveBlobs, 0);
      const hiddenEventAt = proof.hidden.events.findLast(event => event.trusted && event.hidden).at;
      await nativePage.bringToFront();
      await nativePage.waitForFunction(hiddenAt => !document.hidden && document.visibilityState === 'visible' && window.__w8Visibility.some(event => event.trusted && !event.hidden && event.state === 'visible' && event.at >= hiddenAt), hiddenEventAt);
      await other.close(); await delay(200); await waitState('closed');
      proof.visibleWithoutReopen = await nativePage.evaluate(() => ({ hidden: document.hidden, events: window.__w8Visibility, native: JSON.parse(JSON.stringify(window.__w8Native)) }));
      assert.equal(proof.visibleWithoutReopen.hidden, false); assert.equal(proof.visibleWithoutReopen.native.active, 0); assert.equal(proof.visibleWithoutReopen.native.liveBlobs, 0);
      assert.equal(proof.visibleWithoutReopen.native.contexts, proof.hidden.native.contexts); assert.equal(proof.visibleWithoutReopen.native.draws, proof.hidden.native.draws);
      proof.explicitReopen = await draw('native-visibility-explicit-reopen');
      await nativePage.locator('#product-viewer-close').click(); await waitState('closed');
      await nativePage.waitForFunction(() => window.__w8Native.active === 0 && window.__w8Native.liveBlobs === 0);
      proof.finalNative = await read(); await Promise.all(assetTasks);
    } catch (error) { proof.failure = nativeSafe(error.stack); throw Error(proof.failure); }
    finally {
      if (nativeBrowser) {
        try { const processInfo = (await control.send('SystemInfo.getProcessInfo')).processInfo; proof.ownersBeforeClose = await identities(processInfo.map(item => item.id)); knownOwners = proof.ownersBeforeClose.rows; }
        catch (error) { proof.ownersBeforeCloseError = nativeSafe(error.message); }
        proof.nativeCloseRequestedAtUtc = new Date().toISOString();
        try { await (control || await nativeBrowser.newBrowserCDPSession()).send('Browser.close'); proof.nativeCloseAcknowledged = true; }
        catch (error) { proof.nativeCloseAcknowledged = false; proof.nativeCloseResponse = nativeSafe(error.message); }
        await nativeBrowser.close().catch(error => { proof.disconnectError = nativeSafe(error.message); });
      }
      proof.childExit = await Promise.race([childExit, delay(15000).then(() => null)]);
      if (owner) {
        try {
          proof.ownersAfterClose = await identities([...new Set([owner.ProcessId, ...knownOwners.map(item => item.ProcessId)])]);
          proof.exactOwnersStillLive = [owner, ...knownOwners].filter(before => proof.ownersAfterClose.rows.some(after => after.ProcessId === before.ProcessId && after.CreationDate === before.CreationDate && after.ExecutablePath?.toLowerCase() === before.ExecutablePath?.toLowerCase()));
        } catch (error) { proof.ownerClosureCaptureError = nativeSafe(error.message); }
      }
      proof.loopbackListenerOpenAfter = proof.endpoint ? await listenerOpen(proof.endpoint.port) : null;
      proof.executableSHA256After = hash(fs.readFileSync(executable));
      proof.ownedBrowserClosed = !!proof.childExit && !!owner && proof.exactOwnersStillLive?.length === 0 && proof.loopbackListenerOpenAfter === false;
      fs.writeFileSync(path.join(info.output, 'native-visibility-lifecycle.json'), JSON.stringify(proof, null, 2));
      assert.equal(proof.executableSHA256After, proof.executableSHA256Before); assert.equal(proof.ownedBrowserClosed, true, 'Native Browser.close must yield actual owned child exit and fresh exact-owner absence');
    }
  }
  try {
    page = await context.newPage(); observe(page);
    if (info.mode === 'orientation') {
      await page.goto(info.baseUrl + '/shop/media/' + info.vectors[0].id + '?width=160');
      await test('actual WebP pixels match all eight independently authored EXIF orientation/mirror markers', async () => {
        for (const vector of info.vectors) {
          const actual = await page.evaluate(async id => {
            const response = await fetch('/shop/media/' + id + '?width=160', { cache: 'no-store' }); if (!response.ok) throw Error('Derivative HTTP ' + response.status);
            const bitmap = await createImageBitmap(await response.blob()); const canvas = new OffscreenCanvas(bitmap.width, bitmap.height), draw = canvas.getContext('2d'); draw.drawImage(bitmap, 0, 0);
            const points = [[.2, .2], [.8, .2], [.2, .8], [.8, .8]], corners = points.map(([x, y]) => [...draw.getImageData(Math.floor(x * bitmap.width), Math.floor(y * bitmap.height), 1, 1).data].slice(0, 3));
            const result = { width: bitmap.width, height: bitmap.height, corners, contentType: response.headers.get('Content-Type') }; bitmap.close(); return result;
          }, vector.id);
          report.pixels.push({ orientation: vector.orientation, ...actual }); assert.match(actual.contentType, /^image\/webp/); assert.equal(actual.width, vector.width); assert.equal(actual.height, vector.height);
          actual.corners.forEach((rgb, i) => rgb.forEach((x, channel) => assert.ok(Math.abs(x - vector.corners[i][channel]) <= 25, JSON.stringify({ vector, actual }))));
        }
      });
    } else if (info.mode === 'experience') {
      await test('real home banner hierarchy and explicit catalogue navigation remain usable at all declared widths', async () => {
        await goto('/shop'); assert.equal(await page.locator('h1').count(), 1); assert.ok((await page.locator('h1').innerText()).trim());
        assert.ok(await page.locator('.hero img').count()); await review('home-banner');
        await page.locator('.hero a.button').click(); await page.waitForURL('**/shop/products'); await settle(); await review('catalog-populated');
      });
      await test('real empty search and real missing-product errors have clear recovery without broken images or business writes', async () => {
        await goto('/shop/products?Search=WEB08-ABSENT-' + info.productId); await page.locator('.empty-state').waitFor({ state: 'visible' }); await review('catalog-empty');
        const reply = await page.goto(info.baseUrl + '/shop/products/2147483647'); assert.equal(reply.status(), 404); await settle();
        assert.ok(await page.locator('h1').innerText()); assert.ok(await page.locator('a[href="/shop/products"]').count()); await review('catalog-notfound');
      });
      await test('actual cart request latency shows accessible busy feedback, keeps navigation responsive and commits only one selection', async () => {
        await goto('/shop/products/' + info.productId); let release, entered; const gate = new Promise(r => release = r), pending = new Promise(r => entered = r);
        await page.route('**/shop/cart/resolve', async route => { entered(); await gate; await route.continue(); }, { times: 1 });
        try {
          await page.locator('#product-choice button[type=submit]').click(); await pending;
          assert.equal(await page.locator('#product-choice').getAttribute('aria-busy'), 'true'); assert.equal(await page.locator('#product-choice button[type=submit]').isDisabled(), true);
          assert.equal(await page.locator('.header-cart').isEnabled(), true); await page.locator('.header-cart').click({ trial: true }); assert.equal(await page.locator('#product-choice').getAttribute('aria-busy'), 'true');
          await shot('actual-add-cart-loading'); release(); await page.locator('#toast').waitFor({ state: 'visible' });
          assert.equal(await page.locator('#product-choice').getAttribute('aria-busy'), null);
          const selections = await page.evaluate(() => Object.values(localStorage).map(x => { try { return JSON.parse(x); } catch { return null; } }).filter(Boolean));
          report.measurements.push({ label: 'cart-envelope-shapes', envelopes: selections.map(x => ({ schema: x.schema ?? x.version ?? null, itemCount: x.items?.length ?? x.selections?.length ?? null })) });
          const cart = selections.find(x => x.version === 1 && Array.isArray(x.items)); assert.ok(cart); assert.deepEqual(cart.items, [{ productId: info.productId, variantId: info.primaryVariant, unitId: info.baseUnit, quantity: 1 }]);
        } finally { release(); }
      });
      await test('actual network failure preserves form values and supplies a recoverable accessible error with safe photo fallback', async () => {
        await page.route('**/shop/cart/resolve', route => route.abort('failed'), { times: 1 }); await page.locator('#product-quantity').fill('2'); await page.locator('#product-choice button[type=submit]').click();
        await page.locator('#choice-message[role=alert]').waitFor({ state: 'visible' }); assert.equal(await page.locator('#product-quantity').inputValue(), '2');
        assert.equal(await page.locator('#product-choice button[type=submit]').isEnabled(), true); await review('add-cart-error');
        await page.route('**/shop/media/' + info.imageId + '*', route => route.abort('failed'));
        await page.reload(); await settle(); await page.waitForFunction(() => document.querySelector('#detail-image').src.endsWith('/assets/product-fallback.svg') && document.querySelector('#detail-image').complete);
        assert.equal(await page.locator('#detail-image').evaluate(e => e.naturalWidth > 0), true); assert.ok(await page.locator('#detail-image').getAttribute('alt')); await shot('actual-photo-fallback');
      });
      await test('user effects preference persists while reducing decorative transitions without blocking explicit controls', async () => {
        await page.locator('#effects-toggle').click(); assert.equal(await page.locator('#effects-toggle').getAttribute('aria-pressed'), 'true'); await page.reload(); await settle();
        assert.equal(await page.locator('#effects-toggle').getAttribute('aria-pressed'), 'true');
        const motion = await page.evaluate(() => [...document.querySelectorAll('*')].filter(e => { const s = getComputedStyle(e); return s.animationName !== 'none' || s.transitionDuration.split(',').some(x => parseFloat(x) > .001); }).map(e => e.id || e.tagName)); assert.deepEqual(motion, []);
        await buyRemainsUsable(); assert.deepEqual(report.errors, []);
      });
    } else if (info.mode === 'lifecycle') {
      await goto('/shop/products/' + info.productId);
      await test('initial genuine product keeps image and exact choices without vendor/model/context eager load', async () => {
        assert.equal(await page.locator('#detail-image').evaluate(e => e.complete && e.naturalWidth > 0), true);
        assert.equal((await current()).contexts, 0); assert.equal(await page.locator('#product-viewer-canvas').count(), 0);
        assert.deepEqual(report.requests.filter(r => r.path.includes('/vendor/three-') || r.path === '/shop/media/' + info.glbId || r.path === '/shop/media/' + info.gltfId), []);
        const ids = await page.locator('#product-model-choice option').evaluateAll(xs => xs.map(x => x.value)); assert.deepEqual(ids, [String(info.glbId), String(info.gltfId)]);
        await page.locator('#variant-choice').selectOption(String(info.alternateVariant)); assert.match(await page.locator('#detail-price').innerText(), /35\.000/);
        assert.equal(await page.locator('#product-model-choice').inputValue(), String(info.glbId));
        await page.locator('#variant-choice').selectOption(String(info.primaryVariant)); await page.locator('#unit-choice').selectOption(String(info.packUnit)); assert.match(await page.locator('#detail-price').innerText(), /275\.000/);
        await review('detail-photo'); await verifyActualFontGlyphs();
      });
      await test('explicit 3D draws genuine textured geometry, rotates by touch/keyboard, stops rendering while idle and respects pixel/context budgets', async () => {
        await page.setViewportSize({ width: 390, height: 844 }); await openAndDraw();
        const canvas = page.locator('#product-viewer-canvas'); const before = hash(await canvas.screenshot({ path: path.join(info.output, 'real-canvas-initial.png') }));
        await page.locator('#product-viewer-rotate-left').click(); await settle(); const after = hash(await canvas.screenshot({ path: path.join(info.output, 'real-canvas-button-rotated.png') })); assert.notEqual(after, before);
        await canvas.focus(); await page.keyboard.press('ArrowRight'); await page.keyboard.press('+'); await page.keyboard.press('Home'); await settle();
        const start = (await current()).draws; await delay(650); const end = (await current()).draws; report.measurements.push({ label: 'actual-idle-render-count', windowMs: 650, start, end }); assert.equal(end, start, 'No idle animation/render loop');
        const dragBefore = hash(await canvas.screenshot()); const point = await canvas.evaluate(c => { const r = c.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; });
        const touch = await context.newCDPSession(page);
        await touch.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ ...point, id: 1 }] });
        for (let move = 1; move <= 6; move++) await touch.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: point.x + move * 10, y: point.y + move * 2, id: 1 }] });
        await touch.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] }); await touch.detach(); await settle();
        const dragAfter = hash(await canvas.screenshot({ path: path.join(info.output, 'real-canvas-touch-rotated.png') }));
        assert.notEqual(dragAfter, dragBefore, 'Genuine emulated touch drag must visibly rotate the rendered fixture'); report.measurements.push({ label: 'actual-rendered-rotation', buttonBefore: before, buttonAfter: after, touchBefore: dragBefore, touchAfter: dragAfter, input: 'Chromium trusted CDP touch move; emulation, not physical phone' });
        await review('detail-real-3d'); report.states.push(...(await current()).states);
        await canvas.focus(); await page.keyboard.press('Escape'); await state('closed');
        assert.equal(await page.locator('#product-viewer-canvas').count(), 0); assert.equal(await page.locator('#product-viewer-open').evaluate(e => e === document.activeElement), true);
        await delay(100); const closed = await current(); report.measurements.push({ label: 'actual-closed-native-resources', native: closed }); assert.equal(closed.active, 0); assert.equal(closed.liveBlobs, 0);
      });
      await test('actual model swap and offscreen/hidden/unmount dispose old contexts and require explicit reopening', async () => {
        await page.setViewportSize({ width: 390, height: 600 }); await openAndDraw(); await page.locator('#product-model-choice').selectOption(String(info.gltfId)); await state('ready'); assert.equal((await current()).maxActive, 1);
        await page.evaluate(() => scrollTo(0, document.body.scrollHeight));
        await page.waitForFunction(() => document.querySelector('.detail-main-image').getBoundingClientRect().bottom <= 0); await state('closed');
        const contextsBefore = (await current()).contexts; await page.locator('#product-viewer-open').scrollIntoViewIfNeeded(); await delay(200); assert.equal((await current()).contexts, contextsBefore);
        await openAndDraw(); await page.locator('#product-viewer-close').click(); await state('closed');
        await page.waitForFunction(() => window.__w8Native.active === 0 && window.__w8Native.liveBlobs === 0);
        assert.equal((await current()).active, 0); assert.equal((await current()).liveBlobs, 0);
        const mainBeforeNative = await current(); await verifyNativeHiddenLifecycle(); const mainAfterNative = await current();
        report.measurements.push({ label: 'profiled-main-stays-disposed-during-native-visibility-helper', before: mainBeforeNative, after: mainAfterNative });
        assert.equal(mainAfterNative.active, 0); assert.equal(mainAfterNative.liveBlobs, 0); assert.equal(mainAfterNative.contexts, mainBeforeNative.contexts); assert.equal(mainAfterNative.draws, mainBeforeNative.draws);
        await page.bringToFront(); await openAndDraw();
        await page.locator('#product-viewer-canvas').evaluate(canvas => canvas.__w8Context.getExtension('WEBGL_lose_context').loseContext()); await state('fallback'); assert.equal(await page.locator('#product-viewer-canvas').count(), 0);
        await openAndDraw(); await goto('/shop/cart'); assert.equal(await page.locator('#product-viewer-canvas').count(), 0);
      });
      await test('keyboard focus and reduced-motion preference preserve immediate controls and buying without decorative motion', async () => {
        await goto('/shop/products/' + info.productId); await page.emulateMedia({ reducedMotion: 'reduce' }); await settle();
        const moving = await page.evaluate(() => [...document.querySelectorAll('*')].filter(e => { const s = getComputedStyle(e); return s.animationName !== 'none' || s.transitionDuration.split(',').some(x => parseFloat(x) > .001); }).map(e => e.id || e.tagName)); assert.deepEqual(moving, []);
        await page.locator('#product-viewer-open').scrollIntoViewIfNeeded(); await page.locator('#product-viewer-open').focus(); const focus = await page.locator('#product-viewer-open').evaluate(e => { const r = e.getBoundingClientRect(), nav = document.querySelector('.bottom-nav').getBoundingClientRect(); return { focused: e === document.activeElement, top: r.top, bottom: r.bottom, navTop: nav.top, outline: parseFloat(getComputedStyle(e).outlineWidth) }; });
        report.measurements.push({ label: 'viewer-keyboard-focus', ...focus }); assert.ok(focus.focused && focus.top >= 0 && focus.bottom <= focus.navTop && focus.outline >= 2);
        await openAndDraw(); await page.locator('#product-viewer-close').click(); await state('closed');
        assert.equal(await page.locator('#product-viewer-open').evaluate(e => e === document.activeElement), true, 'Manual close must restore a visible focused control');
      });
      await test('real pack price/cart authority and COD placement retain identical retry command after an actual committed response is lost', async () => {
        await page.locator('#variant-choice').selectOption(String(info.primaryVariant)); await page.locator('#unit-choice').selectOption(String(info.packUnit)); await page.locator('#product-quantity').fill('1');
        const resolved = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/cart/resolve'); await page.locator('#product-choice button[type=submit]').click(); const resolution = await (await resolved).json();
        const line = resolution.items.find(x => x.variantId === info.primaryVariant && x.unitId === info.packUnit); assert.equal(line.unitPrice, 275000); assert.equal(line.quantity, 1);
        await goto('/shop/checkout'); await page.locator('#checkout-form').waitFor({ state: 'visible' });
        await page.locator('[name=RecipientName]').fill('Khách WEB08 fixture'); await page.locator('[name=RecipientPhone]').fill('0901234567'); await page.locator('[name=StreetAddress]').fill('123 Đường fixture, không giao thật');
        await page.locator('[name=ProvinceCode]').selectOption('01'); await page.locator('[name=WardCode] option[value="01001"]').waitFor({ state: 'attached' }); await page.locator('[name=WardCode]').selectOption('01001');
        const quoteResponse = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/checkout/quote'); await page.locator('#checkout-quote').click(); const quote = await (await quoteResponse).json();
        assert.equal(quote.goodsAmount, 275000); assert.equal(quote.shippingCharge, 20000); assert.equal(quote.totalDue, 295000);
        let committed, originalBody; let done; const committedBarrier = new Promise(r => done = r);
        await page.route('**/shop/checkout/place', async route => { const reply = await route.fetch(); assert.equal(reply.status(), 200); const result = await reply.json(); assert.ok(result.order); committed = result.order; originalBody = route.request().postDataJSON(); await route.abort('failed'); done(); }, { times: 1 });
        await page.locator('#checkout-confirm').click(); await committedBarrier; await checkpoint('checkpoint-placement-committed', 'checkpoint-placement-verified');
        await page.waitForFunction(() => !document.querySelector('#checkout-confirm').disabled);
        const retryResponse = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/checkout/place'); await page.locator('#checkout-confirm').click(); const retry = await retryResponse;
        assert.equal(retry.status(), 200); assert.deepEqual(retry.request().postDataJSON(), originalBody); assert.deepEqual((await retry.json()).order, committed);
        const access = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/orders/access'); await goto(committed.guestLink); assert.equal((await access).status(), 200); await page.locator('#guest-order').waitFor({ state: 'visible' });
        assert.match((await page.locator('#guest-total').innerText()).replace(/\s/g, ''), /^295\.000đ$/);
        const token = committed.guestLink.split('#')[1];
        assert.equal(rawRequests.some(r => containsCapability(r.url, token) || Object.values(r.headers).some(value => containsCapability(value, token))), false, 'Raw or percent-encoded actual capability must stay out of URL/referrer/headers');
        assert.equal(await page.evaluate(secret => { const contains = value => { let text = String(value); for (let i = 0; i < 4; i++) { if (text.includes(secret)) return true; try { const decoded = decodeURIComponent(text); if (decoded === text) break; text = decoded; } catch { break; } } return false; }; return Object.keys(localStorage).some(k => contains(localStorage.getItem(k))) || Object.keys(sessionStorage).some(k => contains(sessionStorage.getItem(k))); }, token), false);
        await review('received-private-order');
      });
      await test('no browser script errors or foreign/external asset fetches', async () => { assert.deepEqual(report.errors, []); assert.deepEqual(report.requests.filter(r => !['owned-public', 'owned-blob', 'null'].includes(r.origin)), []); });
    } else if (info.mode === 'failure') {
      const kind = info.failure;
      if (kind === 'no-webgl') await page.addInitScript(() => { const real = HTMLCanvasElement.prototype.getContext; HTMLCanvasElement.prototype.getContext = function (kind, ...args) { return /^webgl/.test(kind) ? null : real.call(this, kind, ...args); }; });
      if (kind === 'font') await page.route('**/assets/fonts/*.woff2*', route => route.abort('failed'));
      await goto('/shop/products/' + info.productId);
      await test('declared ' + kind + ' failure leaves actual image, exact price and cart command usable', async () => {
        if (kind === 'no-model') { assert.equal(await page.locator('#product-viewer-open').count(), 0); assert.equal((await current()).contexts, 0); }
        else if (kind === 'font') {
          assert.equal(await page.evaluate(() => document.fonts.check('16px Inter', 'Tiếng Việt')), false);
          assert.ok((await page.locator('h1').innerText()).includes('WEB08'));
          assert.ok(await page.locator('h1').evaluate(e => e.getBoundingClientRect().height > 0 && getComputedStyle(e).visibility === 'visible'));
          assert.ok(await page.evaluate(() => parseFloat(getComputedStyle(document.body).fontSize) >= 15.9));
        }
        else {
          let release; let fetched; const held = new Promise(r => fetched = r), gate = new Promise(r => release = r);
          if (kind === 'module') await page.route('**/vendor/three-r187/build/three.module.js', r => r.abort('failed'), { times: 1 });
          if (kind === 'model') await page.route('**/shop/media/' + info.glbId, r => r.abort('failed'), { times: 1 });
          if (kind === 'offline') await context.setOffline(true);
          if (['timeout', 'late-close', 'late-swap'].includes(kind)) await page.route('**/shop/media/' + info.glbId, async route => { const response = await route.fetch(); assert.equal(response.status(), 200); fetched(); await gate; await route.fulfill({ response }).catch(() => {}); }, { times: 1 });
          await page.locator('#product-viewer-open').click();
          if (['timeout', 'late-close', 'late-swap'].includes(kind)) {
            await held;
            if (kind === 'timeout') {
              await state('fallback'); const states = (await current()).states, started = states.find(x => x.state === 'loading'), ended = states.find(x => x.state === 'fallback' && x.generation > started.generation);
              const elapsed = ended.at - started.at; report.measurements.push({ label: '15-second-owned-load-deadline', elapsedMs: elapsed });
              assert.ok(elapsed >= 14900 && elapsed <= 15050, 'Actual fallback event must follow the15s deadline, not an indefinite request: ' + elapsed);
            }
            else if (kind === 'late-close') { await page.locator('#product-viewer-close').click(); await state('closed'); }
            else { await page.locator('#product-model-choice').selectOption(String(info.gltfId)); await state('ready'); }
            const beforeRelease = (await current()).states.length; release(); await delay(300);
            const native = await current(); assert.equal(native.maxActive <= 1, true);
            if (kind !== 'late-swap') { assert.equal(await page.locator('#product-viewer-canvas').count(), 0); assert.equal(native.states.slice(beforeRelease).some(x => x.state === 'ready'), false); }
            else { assert.equal(native.states.filter(x => x.state === 'ready').every(x => String(x.mediaId) === String(info.gltfId)), true); await page.locator('#product-viewer-close').click(); }
          } else await state('fallback');
          if (kind === 'module') {
            const pinnedModule = '/assets/vendor/three-r187/build/three.module.js';
            const beforeRetry = { native: await current(), timeOrigin: await page.evaluate(() => performance.timeOrigin), requests: report.requests.filter(r => r.path === pinnedModule).length };
            assert.equal(await page.locator('[data-product-viewer]').getAttribute('data-state'), 'fallback');
            assert.equal(beforeRetry.native.active, 0); assert.equal(beforeRetry.native.liveBlobs, 0);
            assert.ok(report.failures.some(r => r.path === pinnedModule && r.reason === 'net::ERR_FAILED'));
            await openAndDraw(); // Same document, same pinned URL; no reload/cachebuster/automatic retry.
            const afterRetry = await current(), transitions = afterRetry.states.slice(beforeRetry.native.states.length);
            const loading = transitions.find(r => r.state === 'loading'), ready = transitions.find(r => r.state === 'ready');
            assert.ok(loading && ready && ready.generation === loading.generation && loading.generation > beforeRetry.native.states.at(-1).generation);
            assert.ok(afterRetry.draws > beforeRetry.native.draws && afterRetry.active === 1 && afterRetry.maxActive <= 1);
            assert.equal(await page.evaluate(() => performance.timeOrigin), beforeRetry.timeOrigin, 'Manual retry must retain the same actual document');
            const moduleRequests = rawRequests.filter(r => new URL(r.url).pathname === pinnedModule); assert.ok(moduleRequests.length > 0);
            for (const request of moduleRequests) { assert.equal(request.url, info.baseUrl + pinnedModule); assert.equal(request.method, 'GET'); }
            await Promise.all(assetTasks);
            assert.ok(report.assets.some(r => r.path === pinnedModule && r.status === 200 && r.method === 'GET' && r.sha256 && !r.captureError));
            report.moduleRetryProof = { pinnedModule, failedRequestRetained: true, actualDocumentUnchanged: true, loadingGeneration: loading.generation, readyGeneration: ready.generation,
              requestsBeforeManualRetry: beforeRetry.requests, requestsAfterManualRetry: moduleRequests.length, additionalNetworkRequestRequired: false, beforeNative: beforeRetry.native, afterNative: afterRetry };
            await page.locator('#product-viewer-close').click(); await state('closed');
          }
          if (kind === 'offline') await context.setOffline(false);
          assert.equal(await page.locator('#detail-image').isVisible(), true); assert.ok(await page.locator('#product-viewer-status').innerText());
        }
        await buyRemainsUsable(); await shot('failure-' + kind + '-usable-image-and-buy'); assert.deepEqual(report.errors, []);
      });
    } else if (info.mode === 'dependencies') {
      // Both image and purchase field are genuinely visible: focusing the field
      // must not accidentally substitute an offscreen-close for the fault.
      await page.setViewportSize({ width: 1440, height: 1000 });
      await page.addInitScript(() => {
        const real = createImageBitmap; let rejectFirst, releaseSecond;
        const firstGate = new Promise(r => rejectFirst = r), secondGate = new Promise(r => releaseSecond = r);
        const trace = { calls: 0, parseEntries: 0, events: [], rejectFirst, releaseSecond }; window.__w8Decode = trace;
        window.createImageBitmap = async function (...args) {
          const call = ++trace.calls; trace.events.push({ event: 'decode-start', call, at: performance.now() });
          const bitmap = await real.apply(this, args); trace.events.push({ event: 'native-result', call, at: performance.now() });
          if (call === 1) { await firstGate; bitmap.close(); trace.events.push({ event: 'controlled-reject', call, at: performance.now() }); throw Error('WEB08 controlled native decoder dependency rejection'); }
          if (call === 2) await secondGate;
          trace.events.push({ event: 'dependency-completed', call, at: performance.now() }); return bitmap;
        };
      });
      await goto('/shop/products/' + info.productId);
      await test('controlled early OUTER parser callback error drains real native sibling before explicit retry, without late ready or purchase focus theft', async () => {
        try {
          await page.evaluate(async () => {
            const { GLTFLoader } = await import('/assets/vendor/three-r187/loaders/GLTFLoader.js'); const original = GLTFLoader.prototype.parse;
            GLTFLoader.prototype.parse = function (...args) {
              const trace = window.__w8Decode; trace.parseEntries++; trace.events.push({ event: 'parse-start', ordinal: trace.parseEntries, at: performance.now() });
              if (trace.parseEntries === 1) trace.failOuterParser = () => {
                trace.events.push({ event: 'controlled-outer-parser-onError', at: performance.now() });
                args[3](new Error('WEB08 controlled EARLY outer parser callback error; original native work remains running'));
              };
              return original.apply(this, args); // Preserve the actual loader and both real native operations.
            };
          });
          await page.locator('#product-viewer-open').click();
          await page.waitForFunction(() => window.__w8Decode.events.filter(x => x.event === 'native-result').length >= 2);
          await page.locator('#product-quantity').focus();
          const visibility = await page.evaluate(() => { const a = document.querySelector('.detail-main-image').getBoundingClientRect(), b = document.querySelector('#product-quantity').getBoundingClientRect(); return { galleryVisible: a.bottom > 0 && a.top < innerHeight, fieldVisible: b.bottom > 0 && b.top < innerHeight }; });
          assert.ok(visibility.galleryVisible && visibility.fieldVisible, JSON.stringify(visibility));
          await page.evaluate(() => { window.__w8Decode.rejectFirst(); window.__w8Decode.failOuterParser(); }); await state('fallback');
          const injected = await page.evaluate(() => window.__w8Decode.events.find(x => x.event === 'controlled-outer-parser-onError'));
          const events = (await current()).states, loading = events.find(x => x.state === 'loading'), fallback = events.find(x => x.state === 'fallback');
          report.dependencyFault = { injectedOuterParserCallback: true, naturalImageFailurePropagationClaim: false, originalParsePreserved: true, injectedAt: injected.at, fallbackAt: fallback.at, loadStartedAt: loading.at, visibility };
          assert.ok(fallback.at >= injected.at && fallback.at - injected.at < 1000 && fallback.at - loading.at < 15000, JSON.stringify(report.dependencyFault));
          assert.equal(await page.locator('#product-quantity').evaluate(e => e === document.activeElement), true);
          assert.equal(await page.evaluate(() => window.__w8Decode.parseEntries), 1);
          const oldGeneration = loading.generation;
          await buyRemainsUsable(); await page.locator('#product-viewer-open').click(); await state('loading');
          await delay(150); await page.evaluate(() => window.__w8Decode.releaseSecond()); await state('ready');
          const trace = await page.evaluate(() => ({ calls: window.__w8Decode.calls, parseEntries: window.__w8Decode.parseEntries, events: window.__w8Decode.events }));
          report.dependencyTrace = trace;
          const settled = trace.events.find(x => x.event === 'dependency-completed' && x.call === 2), retry = trace.events.find(x => x.event === 'parse-start' && x.ordinal === 2);
          assert.ok(settled && retry && retry.at >= settled.at, JSON.stringify(trace)); assert.equal(trace.parseEntries, 2);
          assert.equal((await current()).states.some(x => x.state === 'ready' && x.generation === oldGeneration), false);
          assert.ok((await current()).draws > 0); assert.equal((await current()).maxActive, 1);
          await page.locator('#product-viewer-close').click(); await state('closed'); assert.equal((await current()).liveBlobs, 0); assert.deepEqual(report.errors, []);
        } finally {
          await page.evaluate(() => { window.__w8Decode.rejectFirst(); window.__w8Decode.releaseSecond(); }).catch(() => {});
        }
      });
    } else if (info.mode === 'client-preflight') {
      report.transportFaultInjected = true;
      const witness = fs.readFileSync(info.payload); assert.equal(hash(witness), info.payloadSHA256); assert.ok(witness.length < 8 * 1024 * 1024);
      await goto('/shop/products/' + info.productId);
      await test('bounded poisoned transport fails preflight before exact same-URL GLTFLoader.parse tripwire', async () => {
        // This imports the exact URL the viewer uses. It is deliberately NOT a
        // lazy-load success measurement. Throw before the original parse entry.
        const loaderPath = '/assets/vendor/three-r187/loaders/GLTFLoader.js';
        await page.evaluate(async url => {
          const { GLTFLoader } = await import(url); window.__w8ParserEntries = 0;
          GLTFLoader.prototype.parse = function () { window.__w8ParserEntries++; throw Error('WEB08 fail-fast parser tripwire: unsafe witness must never enter parser'); };
          window.__w8TripwireInstalled = true;
        }, loaderPath);
        assert.equal(await page.evaluate(() => window.__w8TripwireInstalled), true);
        await page.route('**/shop/media/' + info.glbId, async route => { const original = await route.fetch(); assert.equal(original.status(), 200); await route.fulfill({ status: 200, contentType: 'model/gltf-binary', body: witness }); }, { times: 1 });
        await page.locator('#product-viewer-open').click(); await state('fallback');
        report.parserEntries = await page.evaluate(() => window.__w8ParserEntries); assert.equal(report.parserEntries, 0);
        assert.equal(await page.locator('#detail-image').isVisible(), true); assert.equal(await page.locator('#product-viewer-canvas').count(), 0);
        await buyRemainsUsable(); assert.deepEqual(report.errors, []);
      });
    } else if (info.mode === 'performance') {
      const mobile = info.profile === 'mobile'; await page.close();
      // All runs, including misses, remain in the report. No fastest-run selection.
      for (let index = 0; index < (mobile ? 6 : 3); index++) {
        const cold = !mobile || index < 3;
        const lab = await browser.newContext({ viewport: mobile ? { width: 390, height: 844 } : { width: 1440, height: 900 }, deviceScaleFactor: mobile ? 1.5 : 1, hasTouch: mobile, timezoneId: 'Asia/Bangkok' });
        page = await lab.newPage(); observe(page); const cdp = await lab.newCDPSession(page);
        const sample = { profile: info.profile, index, cold, status: 'started', lcpMs: null, interactionMaxMs: null, cls: null }; report.performance.push(sample);
        await cdp.send('Network.enable'); await cdp.send('Network.setCacheDisabled', { cacheDisabled: cold });
        await cdp.send('Emulation.setCPUThrottlingRate', { rate: mobile ? 4 : 1 });
        if (mobile) await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 150, downloadThroughput: 1600000 / 8, uploadThroughput: 750000 / 8 });
        await page.addInitScript(() => {
          window.__w8Perf = { lcp: [], shifts: [], events: [], firstInputs: [], supported: PerformanceObserver.supportedEntryTypes };
          for (const [type, key] of [['largest-contentful-paint', 'lcp'], ['layout-shift', 'shifts'], ['first-input', 'firstInputs']]) if (PerformanceObserver.supportedEntryTypes.includes(type)) new PerformanceObserver(list => window.__w8Perf[key].push(...list.getEntries().map(x => x.toJSON()))).observe({ type, buffered: true });
          if (PerformanceObserver.supportedEntryTypes.includes('event')) new PerformanceObserver(list => window.__w8Perf.events.push(...list.getEntries().map(x => x.toJSON()))).observe({ type: 'event', buffered: true, durationThreshold: 16 });
        });
        // Warm samples really visit the same route before the measured navigation.
        if (!cold) { await goto('/shop/products/' + info.productId); await page.reload(); await settle(); } else await goto('/shop/products/' + info.productId);
        await page.locator('#variant-choice').selectOption(String(info.alternateVariant)); await page.locator('#product-quantity').click(); await page.keyboard.press('ArrowUp');
        await page.locator('#product-viewer-open').click(); await state('ready'); await page.locator('#product-viewer-rotate-left').click(); await page.locator('#product-viewer-close').click();
        await buyRemainsUsable(); await delay(250); const data = await page.evaluate(() => ({ ...window.__w8Perf, resources: performance.getEntriesByType('resource').map(x => ({ name: new URL(x.name).pathname, duration: x.duration, transferSize: x.transferSize, encodedBodySize: x.encodedBodySize })), navigation: performance.getEntriesByType('navigation').map(x => x.toJSON()), inter: [...document.fonts].filter(x => x.family === 'Inter').map(x => x.status) }));
        const lcp = data.lcp.at(-1)?.startTime, cls = data.shifts.filter(x => !x.hadRecentInput).reduce((s, x) => s + x.value, 0);
        const eventDurations = [...data.events, ...data.firstInputs].map(x => x.duration).filter(Number.isFinite); const inp = eventDurations.length ? Math.max(...eventDurations) : null;
        Object.assign(sample, { status: 'captured', lcpMs: lcp, interactionMaxMs: inp, cls, raw: data });
        await shot('performance-' + info.profile + '-' + index); await lab.close();
      }
      await test('every declared laboratory run records real paint, trusted event timing and layout shifts within accepted budgets', async () => {
        for (const run of report.performance) { assert.ok(Number.isFinite(run.lcpMs), 'No LCP entry'); assert.ok(Number.isFinite(run.interactionMaxMs), 'Unsupported/uncaptured EventTiming is not PASS'); assert.ok(run.raw.supported.includes('event') && run.raw.supported.includes('layout-shift')); assert.ok(run.lcpMs <= 2500 && run.interactionMaxMs <= 200 && run.cls <= .1, JSON.stringify({ index: run.index, lcp: run.lcpMs, inp: run.interactionMaxMs, cls: run.cls })); }
      });
    } else throw Error('Unsupported owned mode');
  } catch (error) {
    process.exitCode = 1; console.error(safe(error.stack));
    const sample = report.performance.at(-1);
    if (sample?.status === 'started') { sample.status = 'failed'; sample.error = safe(error.stack); if (page && !page.isClosed()) sample.raw = await page.evaluate(() => window.__w8Perf || null).catch(() => null); }
    if (page && !page.isClosed()) await shot('failure-last-state').catch(() => {});
  }
  finally {
    if (page && !page.isClosed()) report.states.push(...(await page.evaluate(() => window.__w8Native?.states || []).catch(() => [])));
    await Promise.all(assetTasks); fs.writeFileSync(path.join(info.output, 'browser-results.json'), JSON.stringify(report, null, 2));
    await context.close(); await browser.close();
  }
})();
