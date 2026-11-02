// Privacy helper reused from frozen WEB05 V8; business oracle below is independent WEB07.
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');

const staticAssets = new Set(['/assets/storefront.css', '/assets/storefront.js', '/assets/checkout.js', '/assets/pending-order.js', '/assets/orders.js', '/assets/qr.js', '/assets/cancellation.js', '/assets/cancellation.css', '/Admin/css/storefront-refunds.css', '/Admin/js/storefront-refunds.js', '/Admin/css/storefront-fulfillment.css', '/Admin/js/storefront-fulfillment.js', '/lib/bootstrap/dist/css/bootstrap.min.css', '/css/site.css']);
const digest = body => crypto.createHash('sha256').update(body).digest('base64url');
function metadataText(value) {
  let text = String(value); for (let i = 0; i < 2; i++) { try { text = decodeURIComponent(text); } catch { break; } }
  return text.replace(/[A-Za-z0-9_-]{43,}/g, '[REDACTED43]');
}
function urlMetadata(raw) {
  if (!raw) return null;
  try { const u = new URL(raw); return { origin: metadataText(u.origin), path: metadataText(u.pathname), queryKeys: [...u.searchParams.keys()].map(metadataText), fragmentPresent: !!u.hash }; }
  catch { return { invalid: true }; }
}
function privacyVerdict(raw, tokens, origin, proofs) {
  if (!raw) return { forbidden: false, capabilityMatch: false, verifiedAssetVersion: false };
  const forms = [raw]; let decodeFailed = false;
  for (let i = 0; i < 2; i++) { try { forms.push(decodeURIComponent(forms.at(-1))); } catch { decodeFailed = true; break; } }
  const capabilityMatch = tokens.some(token => forms.some(value => value.includes(token) || value.includes(encodeURIComponent(token))));
  const opaque = forms.some(value => /[A-Za-z0-9_-]{43}/.test(value)); let verifiedAssetVersion = false;
  try {
    const u = new URL(raw), keys = [...u.searchParams.keys()], v = u.searchParams.get('v'), proof = proofs.get(u.href);
    verifiedAssetVersion = !capabilityMatch && !u.username && !u.password && u.origin === origin && staticAssets.has(u.pathname) && keys.length === 1 && keys[0] === 'v' && !u.hash &&
      /^[A-Za-z0-9_-]{43}$/.test(v || '') && proof?.status === 200 && proof.method === 'GET' && proof.bodyDigest === v;
  } catch { decodeFailed = true; }
  return { forbidden: decodeFailed || capabilityMatch || opaque && !verifiedAssetVersion, capabilityMatch, verifiedAssetVersion: opaque && verifiedAssetVersion };
}
function privacyOracle() {
  const origin = 'http://127.0.0.1:1', version = digest(Buffer.from('owned static oracle')), good = origin + '/assets/orders.js?v=' + version;
  const token = 'X'.repeat(43), valid = { status: 200, method: 'GET', bodyDigest: version }, rows = [];
  const check = (name, url, proof, tokens, forbidden) => { const actual = privacyVerdict(url, tokens, origin, new Map([[url, proof]])); assert.equal(actual.forbidden, forbidden, name); rows.push({ name, passed: true }); };
  check('verified same-origin sole-v asset', good, valid, [token], false);
  check('capability on otherwise valid static-v remains forbidden', good, valid, [version], true);
  check('wrong response digest', good, { ...valid, bodyDigest: digest(Buffer.from('different')) }, [token], true);
  check('extra query key', good + '&extra=1', valid, [token], true);
  check('duplicate v', good + '&v=' + version, valid, [token], true);
  check('nonstatic path', origin + '/shop/orders?v=' + version, valid, [token], true);
  check('unlisted static path', origin + '/assets/unlisted.js?v=' + version, valid, [token], true);
  check('foreign origin', good.replace(origin, 'http://foreign.invalid'), valid, [token], true);
  check('userinfo cannot share static-origin exception', good.replace(origin, 'http://' + 'U'.repeat(43) + '@127.0.0.1:1'), valid, [token], true);
  check('fragment is not a version exception', good + '#fragment', valid, [token], true);
  check('unverified response status', good, { ...valid, status: 404 }, [token], true);
  check('actual capability in request or referrer', origin + '/shop/orders#' + token, undefined, [token], true);
  check('encoded actual capability', origin + '/shop/orders?x=' + token.split('').map(c => '%' + c.charCodeAt(0).toString(16)).join(''), undefined, [token], true);
  return rows;
}
if (process.argv.includes('--privacy-oracle-only')) { fs.writeFileSync(process.argv[process.argv.indexOf('--privacy-oracle-only') + 1], JSON.stringify(privacyOracle(), null, 2)); process.exit(0); }

(async () => {
  const info = JSON.parse(await new Promise(resolve => { let data = ''; process.stdin.on('data', x => data += x); process.stdin.on('end', () => resolve(data)); }));
  fs.mkdirSync(info.output, { recursive: true });
  const results = [], measurements = [], errors = [], requests = [], failedRequests = [], privateRequests = [], proofTasks = [], responseProofs = new Map();
  const timingStart = process.hrtime.bigint(), requestTimings = [], pageTimings = [], timingRequests = new WeakMap();
  const elapsedMs = () => Number(process.hrtime.bigint() - timingStart) / 1e6;
  const tokens = [info.aLink, info.bLink].map(link => new URL(link, info.publicUrl).hash.slice(1));
  const origins = new Set([new URL(info.adminUrl).origin, new URL(info.publicUrl).origin]);
  const safe = value => tokens.reduce((text, token) => text.split(token).join('[CAPABILITY]'), metadataText(value));
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const adminContext = await browser.newContext({ viewport: { width: 390, height: 844 }, timezoneId: 'Asia/Bangkok', extraHTTPHeaders: { 'X-Web07-User': String(info.userId) } });
  const guestContext = await browser.newContext({ viewport: { width: 390, height: 844 }, timezoneId: 'Asia/Bangkok' });
  // Delay completion of the ORIGINAL digest promise, rather than substituting a
  // hash or setting live DOM state. This tests the actual asynchronous boundary.
  await guestContext.addInitScript(() => {
    window.__web07Document = crypto.randomUUID();
    const original = crypto.subtle.digest.bind(crypto.subtle); let release;
    const gate = new Promise(resolve => { release = resolve; });
    window.__web07ReleaseDigest = release;
    crypto.subtle.digest = async (algorithm, data) => {
      const result = original(algorithm, data);
      if (data.byteLength === 43 && !window.__web07DigestCaptured) {
        window.__web07DigestCaptured = true; await gate;
      }
      return result;
    };
  });
  const admin = await adminContext.newPage(), guest = await guestContext.newPage(); let active = guest;
  for (const context of [adminContext, guestContext]) await context.route('**/*', route => {
    const u = new URL(route.request().url()); return origins.has(u.origin) || ['blob:', 'data:'].includes(u.protocol) ? route.continue() : route.abort('blockedbyclient');
  });
  for (const page of [admin, guest]) {
    const pageName = page === guest ? 'guest' : 'admin';
    pageTimings.push({ page: pageName, event: 'observer-attached', elapsedMs: elapsedMs() });
    for (const event of ['domcontentloaded', 'load']) page.on(event, () => pageTimings.push({ page: pageName, event, elapsedMs: elapsedMs() }));
    page.on('request', request => {
      const row = { page: pageName, ...urlMetadata(request.url()), method: request.method(), resourceType: request.resourceType(), navigation: request.isNavigationRequest(),
        startMs: elapsedMs(), responseMs: null, status: null, finishedMs: null, failedMs: null };
      requestTimings.push(row); timingRequests.set(request, row);
    });
    page.on('response', response => { const row = timingRequests.get(response.request()); if (row) { row.responseMs = elapsedMs(); row.status = response.status(); } });
    page.on('requestfinished', request => { const row = timingRequests.get(request); if (row) row.finishedMs = elapsedMs(); });
    page.on('requestfailed', request => { const row = timingRequests.get(request); if (row) { row.failedMs = elapsedMs(); row.failure = safe(request.failure()?.errorText || ''); } });
    page.on('pageerror', error => errors.push(safe(error.message)));
    page.on('requestfailed', request => failedRequests.push({ ...urlMetadata(request.url()), reason: safe(request.failure()?.errorText || '') }));
    page.on('request', request => {
      const raw = request.url(), referrer = request.headers().referer || '';
      const row = { method: request.method(), ...urlMetadata(raw), referrer: urlMetadata(referrer), navigation: request.isNavigationRequest() };
      requests.push(row); privateRequests.push({ raw, referrer, row });
    });
    page.on('response', response => {
      const request = response.request(), u = new URL(request.url()); if (!origins.has(u.origin) || !staticAssets.has(u.pathname)) return;
      proofTasks.push(response.body().then(body => responseProofs.set(u.href, { status: response.status(), method: request.method(), bodyDigest: digest(body), sha256: crypto.createHash('sha256').update(body).digest('hex'), bytes: body.length })).catch(() => {}));
    });
  }
  const screenshot = async label => active.screenshot({ path: path.join(info.output, label + '.png'), fullPage: true,
    mask: [active.locator('#guest-link-input,a[href*="/shop/orders#"]')], maskColor: '#222' });
  const test = async (name, body) => { try { await body(); results.push({ name, passed: true }); } catch (error) { await screenshot('failure-' + results.length).catch(() => {}); results.push({ name, passed: false, error: safe(error.stack) }); process.exitCode = 1; throw error; } };
  const checkpoint = async (ready, ack) => {
    fs.writeFileSync(path.join(info.output, ready), 'Owned actual fixture checkpoint; no secrets.'); const until = Date.now() + 45000;
    while (!fs.existsSync(path.join(info.output, ack))) { if (Date.now() > until) throw new Error('Owned checkpoint timeout: ' + ack); await new Promise(resolve => setTimeout(resolve, 50)); }
  };
  const adminRoute = operation => '/admin/storefront/refunds/' + info.orderId + '/' + operation;
  let renderedPayerVersion = null;
  const isPost = (response, route) => response.request().method() === 'POST' && new URL(response.url()).pathname === route;
  const sameDocument = async link => {
    const before = new URL(guest.url()), marker = await guest.evaluate(() => window.__web07Document);
    const response = await guest.goto(info.publicUrl + link);
    const after = new URL(guest.url()); assert.equal(after.origin + after.pathname + after.search, before.origin + before.pathname + before.search);
    assert.equal(after.hash, new URL(link, info.publicUrl).hash); assert.equal(response, null); assert.equal(await guest.evaluate(() => window.__web07Document), marker);
  };
  const guestRefresh = async () => { const wait = guest.waitForResponse(r => isPost(r, '/shop/orders/cancellation/access')); await guest.locator('#guest-cancellation-refresh').click(); assert.equal((await wait).status(), 200); };
  const submitGuest = async reason => { await guest.locator('#guest-cancellation-reason').fill(reason); await guest.locator('#guest-cancellation-confirm').check(); await guest.locator('#guest-cancellation-submit').click(); };
  const submitAdmin = async (selector, operation) => {
    const response = admin.waitForResponse(r => isPost(r, adminRoute(operation))), navigation = admin.waitForNavigation({ waitUntil: 'domcontentloaded' }); void navigation.catch(() => {});
    await admin.locator(selector).click(); const actual = await response;
    if (actual.status() !== 200) { const body = await actual.json().catch(() => null); fs.writeFileSync(path.join(info.output, 'unexpected-admin-' + results.length + '.json'), JSON.stringify({ status: actual.status(), code: safe(body?.code || ''), message: safe(body?.message || '') })); }
    assert.equal(actual.status(), 200); await navigation;
  };
  const canonicalLocal = value => value.replace(/(\.\d*?[1-9])0+$/, '$1').replace(/\.0+$/, '').replace(/(T\d{2}:\d{2}):00$/, '$1');
  async function measure(label, page) {
    active = page;
    for (const width of [320, 360, 390, 430, 1440]) {
      await page.setViewportSize({ width, height: width === 1440 ? 1000 : 844 });
      await page.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); });
      const row = await page.evaluate(() => {
        const shown = e => { const r = e.getBoundingClientRect(); return r.width > 1 && r.height > 1 && getComputedStyle(e).visibility !== 'hidden' && !e.closest('[hidden]'); };
        const color = s => s.match(/[\d.]+/g)?.map(Number) || [0, 0, 0, 0];
        const blend = (a, b) => a.slice(0, 3).map((v, i) => v * (a[3] ?? 1) + b[i] * (1 - (a[3] ?? 1)));
        const background = e => { const layers = []; for (; e; e = e.parentElement) { const c = color(getComputedStyle(e).backgroundColor); layers.push(c); if ((c[3] ?? 1) === 1) break; } return layers.reverse().reduce((a, b) => blend(b, a), [255, 255, 255]); };
        const luminance = rgb => rgb.map(v => v / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((s, v, i) => s + v * [.2126, .7152, .0722][i], 0);
        const text = [...document.querySelectorAll('body *')].filter(e => shown(e) && !e.closest('button:disabled,option,script,style,svg') && [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim()));
        const contrast = text.map(e => { const s = getComputedStyle(e), bg = background(e), a = luminance(bg), b = luminance(blend(color(s.color), bg)); const large = parseFloat(s.fontSize) >= 24 || parseFloat(s.fontSize) >= 18.66 && parseInt(s.fontWeight, 10) >= 700; return { tag: e.tagName, ratio: (Math.max(a, b) + .05) / (Math.min(a, b) + .05), threshold: large ? 3 : 4.5 }; });
        const controls = [...document.querySelectorAll('button,input:not([type=hidden]),select,textarea,.bottom-nav a')].filter(shown).map(e => { const target = e.type === 'checkbox' && e.closest('label') || e, r = target.getBoundingClientRect(); return { id: e.id || e.name || e.tagName, width: r.width, height: r.height, checkboxLabel: e.type === 'checkbox' }; });
        return { width: innerWidth, scrollWidth: document.documentElement.scrollWidth, font: parseFloat(getComputedStyle(document.querySelector('#sf-control-page') || document.body).fontSize), contrastSamples: contrast.length, minContrast: Math.min(...contrast.map(x => x.ratio)), contrastFailures: contrast.filter(x => x.ratio < x.threshold), controls, smallControls: controls.filter(x => x.width < 43.9 || x.height < 43.9) };
      });
      measurements.push({ label, ...row }); await screenshot(label + '-' + width);
      assert.ok(row.scrollWidth <= width + 1, label + ' overflow'); assert.ok(row.font >= 15.9, label + ' font16'); assert.deepEqual(row.smallControls, [], label + ' effective44px targets'); assert.deepEqual(row.contrastFailures, [], label + ' contrast');
      const buttons = page.locator('button.sf-control-button:visible:not(:disabled),button.button:visible:not(:disabled)');
      for (let index = 0; index < Math.min(await buttons.count(), 2); index++) {
        const button = buttons.nth(index);
        const stateContrast = async state => {
          const transition = await button.evaluate(e => Math.max(0, ...getComputedStyle(e).transitionDuration.split(',').map(value => value.trim().endsWith('ms') ? parseFloat(value) : parseFloat(value) * 1000)));
          if (transition > 0) await page.waitForTimeout(Math.min(1000, transition + 30));
          const value = await button.evaluate(e => {
            const colors = s => s.match(/[\d.]+/g)?.map(Number) || [0, 0, 0, 0];
            const blend = (a, b) => a.slice(0, 3).map((v, i) => v * (a[3] ?? 1) + b[i] * (1 - (a[3] ?? 1)));
            const layers = []; for (let parent = e; parent; parent = parent.parentElement) { const color = colors(getComputedStyle(parent).backgroundColor); layers.push(color); if ((color[3] ?? 1) === 1) break; }
            const background = layers.reverse().reduce((a, b) => blend(b, a), [255, 255, 255]);
            const lum = rgb => rgb.map(v => v / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
            const style = getComputedStyle(e), a = lum(blend(colors(style.color), background)), b = lum(background);
            return { ratio: (Math.max(a, b) + .05) / (Math.min(a, b) + .05), transition: style.transitionDuration };
          });
          measurements.push({ label, width, buttonIndex: index, state, ...value }); assert.ok(value.ratio >= 4.5, label + ' ' + state + ' enabled button contrast');
        };
        await button.hover(); await stateContrast('hover');
        await page.mouse.down();
        try { await stateContrast('active'); }
        finally { await page.mouse.move(1, 1); await page.mouse.up(); }
      }
    }
  }
  try {
    await test('genuine capability digest must finish before cancellation can be submitted', async () => {
      assert.equal((await guest.goto(info.publicUrl + info.aLink)).status(), 200); await guest.locator('#guest-order').waitFor({ state: 'visible' });
      await guest.waitForFunction(() => window.__web07DigestCaptured === true); assert.equal(await guest.locator('#guest-cancellation-form').isVisible(), false);
      assert.equal(requests.filter(x => x.method === 'POST' && x.path === '/shop/orders/cancellation').length, 0);
      await guest.evaluate(() => window.__web07ReleaseDigest()); await guest.locator('#guest-cancellation-form').waitFor({ state: 'visible' });
      assert.equal(await guest.locator('#guest-code').innerText(), 'Đơn ' + info.aCode);
      assert.equal(await guest.locator('#guest-phase').innerText(), info.recall ? 'Đang giao hàng' : 'Chờ cửa hàng chuẩn bị');
      await measure('guest-before-cancel', guest);
    });
    await test('keyboard and real associated checkbox-label target work at320 with visible safe focus', async () => {
      active = guest; await guest.setViewportSize({ width: 320, height: 844 });
      const box = guest.locator('#guest-cancellation-confirm'), label = box.locator('..'); await label.click({ position: { x: 36, y: 8 } }); assert.equal(await box.isChecked(), true); await box.uncheck();
      await guest.locator('body').click({ position: { x: 1, y: 1 } }); let found = false;
      for (let i = 0; i < 100; i++) { await guest.keyboard.press('Tab'); found = await guest.locator('#guest-cancellation-submit').evaluate(e => document.activeElement === e); if (found) break; }
      const row = await guest.locator('#guest-cancellation-submit').evaluate(e => { const r = e.getBoundingClientRect(), nav = document.querySelector('.bottom-nav')?.getBoundingClientRect(); return { top: r.top, bottom: r.bottom, visibleBottom: nav?.top ?? innerHeight, outline: parseFloat(getComputedStyle(e).outlineWidth) }; });
      measurements.push({ label: 'guest-keyboard', ...row }); assert.ok(found && row.top >= 0 && row.bottom <= row.visibleBottom && row.outline >= 2); await screenshot('keyboard-320');
    });
    let aAttempts = 0;
    await test('delayed actual A cancellation completion resumes B state after faithful same-document capability navigation', async () => {
      let release, accepted; const held = new Promise(resolve => { release = resolve; }), committed = new Promise(resolve => { accepted = resolve; });
      await guest.route('**/shop/orders/cancellation', async intercepted => {
        const body = intercepted.request().postDataJSON(); assert.equal(body.token, tokens[0]); aAttempts++; const actual = await intercepted.fetch(); assert.equal(actual.status(), 200); accepted(); await held; await intercepted.fulfill({ response: actual });
      });
      await submitGuest('WEB07 khách A xác nhận yêu cầu qua giao diện thật'); await committed; await checkpoint('checkpoint-a-committed', 'checkpoint-a-verified');
      await sameDocument(info.bLink); await guest.locator('#guest-code', { hasText: info.bCode }).waitFor({ state: 'visible' }); release();
      await guest.locator('#guest-cancellation-form').waitFor({ state: 'visible' }); assert.equal(await guest.locator('#guest-code').innerText(), 'Đơn ' + info.bCode);
      assert.equal(await guest.locator('#guest-phase').innerText(), 'Chờ cửa hàng chuẩn bị');
      assert.equal(await guest.locator('#guest-refund').innerText(), 'Chưa có yêu cầu hoàn');
      assert.equal(await guest.locator('#guest-cancellation-reason').inputValue(), ''); assert.equal(await guest.locator('#guest-cancellation-confirm').isChecked(), false);
      assert.doesNotMatch(await guest.locator('#guest-cancellation').innerText(), new RegExp(info.aCode)); assert.equal(aAttempts, 1);
      await guest.unroute('**/shop/orders/cancellation'); await measure('guest-B-after-A', guest);
    });
    await test('guest committed response loss survives reload after form disappearance and retries the identical original command', async () => {
      const bodies = []; let dropped = false, committed; const actualCommitted = new Promise(resolve => { committed = resolve; });
      await guest.route('**/shop/orders/cancellation', async intercepted => {
        bodies.push(intercepted.request().postDataJSON());
        if (!dropped) { dropped = true; const actual = await intercepted.fetch(); assert.equal(actual.status(), 200); await intercepted.abort('failed'); committed(); } else await intercepted.continue();
      });
      await submitGuest('WEB07 khách B yêu cầu hủy trước chuẩn bị'); await actualCommitted; await guest.locator('#guest-cancellation-pending').waitFor({ state: 'visible' }); await checkpoint('checkpoint-b-lost', 'checkpoint-b-verified');
      await guest.reload({ waitUntil: 'domcontentloaded' }); await guest.evaluate(() => window.__web07ReleaseDigest());
      await guest.locator('#guest-cancellation-pending').waitFor({ state: 'visible' }); assert.equal(await guest.locator('#guest-cancellation-form').isVisible(), false);
      const response = guest.waitForResponse(r => isPost(r, '/shop/orders/cancellation')); await guest.locator('#guest-cancellation-retry').click(); assert.equal((await response).status(), 200);
      await guest.locator('#guest-cancellation-pending').waitFor({ state: 'hidden' }); assert.equal(bodies.length, 2); assert.deepEqual(bodies[0], bodies[1]); await guest.unroute('**/shop/orders/cancellation');
      await guestRefresh(); await guest.locator('#guest-phase', { hasText: 'Đã hủy' }).waitFor({ state: 'visible' });
      assert.equal(await guest.locator('#guest-phase').innerText(), 'Đã hủy');
      assert.equal(await guest.locator('#guest-refund').innerText(), 'Chưa có yêu cầu hoàn');
      fs.writeFileSync(path.join(info.output, 'guest-replay-proof.json'), JSON.stringify({ attempts: 2, identicalPayload: true, originalKey: true, formAbsentAfterReload: true, aAttempts }));
    });
    await test('staff sees actual request or paid cancellation and physical recall produces refund only after real returned slices', async () => {
      active = admin; assert.equal((await admin.goto(info.adminUrl + '/admin/storefront/refunds?Filter=All')).status(), 200);
      await admin.locator('.sf-control-queue-row[data-order-id="' + info.orderId + '"]').getByText('Đơn ' + info.aCode, { exact: true }).waitFor(); await measure('admin-queue', admin);
      await admin.goto(info.adminUrl + '/admin/storefront/refunds/' + info.orderId);
      if (info.recall) {
        const form = admin.locator('form[data-operation=cancellation-decision]'); await form.locator('[name=reason]').fill('WEB07 xác minh thu hồi đủ mọi nguồn đang giao');
        await submitAdmin('button[data-operation=cancellation-approve]', 'cancellation/approve');
        assert.equal(await admin.locator('form[data-operation=cancellation-decision]').count(), 0); await checkpoint('checkpoint-return-needed', 'checkpoint-return-verified');
        await admin.reload({ waitUntil: 'domcontentloaded' });
      }
      await sameDocument(info.aLink); await guest.locator('#guest-refund-detail', { hasText: '230.000' }).waitFor();
      assert.equal(await guest.locator('#guest-phase').innerText(), 'Đã hủy');
      assert.equal(await guest.locator('#guest-refund').innerText(), 'Đang chờ xử lý hoàn tiền');
      await guest.locator('#qr-state', { hasText: 'QR đã đóng vì đơn hủy' }).waitFor(); assert.equal(await guest.locator('#qr-image').isVisible(), false); assert.equal(await guest.locator('#qr-download').isVisible(), false);
      for (const id of ['qr-copy-account', 'qr-copy-content', 'qr-copy-amount']) assert.equal(await guest.locator('#' + id).isDisabled(), true);
      assert.equal((await guest.locator('#qr-account').innerText()).trim(), ''); assert.equal((await guest.locator('#qr-content').innerText()).trim(), '');
      await measure('guest-closedQR-refund', guest); await checkpoint('checkpoint-debit-needed', 'checkpoint-debit-ready');
    });
    await test('real SQL order lock timeout409 keeps original approval key for exact retry', async () => {
      active = admin; await admin.reload({ waitUntil: 'domcontentloaded' }); const form = admin.locator('form[data-operation=refund-decision]'); await form.locator('[name=reason]').fill('WEB07 xác minh nghĩa vụ hoàn đủ tiền hàng và phí giao');
      await form.locator('[name=reason]').focus(); await admin.keyboard.press('Tab');
      const focus = await admin.locator('button[data-operation=refund-approve]').evaluate(e => { const r = e.getBoundingClientRect(); return { focused: document.activeElement === e, top: r.top, bottom: r.bottom, viewport: innerHeight, outline: parseFloat(getComputedStyle(e).outlineWidth) }; });
      measurements.push({ label: 'admin-keyboard', ...focus }); assert.ok(focus.focused && focus.top >= 0 && focus.bottom <= focus.viewport && focus.outline >= 2);
      const bodies = [], observe = request => { if (request.method() === 'POST' && new URL(request.url()).pathname === adminRoute('approve')) bodies.push(request.postDataJSON()); }; admin.on('request', observe);
      await checkpoint('checkpoint-busy-lock-needed', 'checkpoint-busy-lock-acquired');
      const response = admin.waitForResponse(r => isPost(r, adminRoute('approve'))); await admin.locator('button[data-operation=refund-approve]').click();
      const actual = await response; assert.equal(actual.status(), 409); const body = await actual.json(); assert.equal(body.code, 'RETRY_SAME_COMMAND'); assert.equal(body.retryable, true);
      await admin.locator('#sf-control-pending [data-retry-command]').waitFor({ state: 'visible' }); await checkpoint('checkpoint-busy-response', 'checkpoint-busy-lock-released');
      const navigation = admin.waitForNavigation({ waitUntil: 'domcontentloaded' }); void navigation.catch(() => {}); await admin.locator('#sf-control-pending [data-retry-command]').click(); await navigation;
      admin.off('request', observe); assert.equal(bodies.length, 2); assert.deepEqual(bodies[0], bodies[1]);
      fs.writeFileSync(path.join(info.output, 'busy-replay-proof.json'), JSON.stringify({ actualStatus: 409, actualCode: body.code, identicalPayload: true, attempts: 2 })); await checkpoint('checkpoint-approved', 'checkpoint-approved-verified');
      await guestRefresh(); await guest.locator('#guest-refund', { hasText: 'Đã duyệt hoàn tiền' }).waitFor({ state: 'visible' });
      assert.equal(await guest.locator('#guest-phase').innerText(), 'Đã hủy');
      assert.equal(await guest.locator('#guest-refund').innerText(), 'Đã duyệt hoàn tiền');
    });
    await test('approved means pending actual payout and the selectable full debit comes from real prior cash or bank records', async () => {
      active = admin; const debit = JSON.parse(fs.readFileSync(path.join(info.output, 'checkpoint-debit.json'), 'utf8'));
      const form = admin.locator('form[data-operation=refund-confirm]'); await form.waitFor(); await form.locator('[data-load-payouts]').click();
      const select = form.locator('[name=source]'), expectedSource = debit.kind + ':' + debit.id;
      await select.waitFor({ state: 'visible' });
      const option = select.locator('option[value="' + expectedSource + '"]'); await option.waitFor({ state: 'attached' }); assert.equal(await select.isEnabled(), true);
      await select.selectOption(expectedSource); assert.equal(await select.inputValue(), expectedSource);
      assert.equal(await option.getAttribute('data-id'), String(debit.id)); assert.equal(await option.getAttribute('data-kind'), String(debit.kind));
      await form.locator('[name=payerLegalEntityId]').selectOption(String(info.ownerId));
      if (debit.kind === 2) {
        await form.locator('[name=payerBankAccountId]').selectOption(String(info.bankAccountId));
        renderedPayerVersion = await form.locator('[name=payerBankAccountId]').evaluate(e => e.selectedOptions[0].dataset.version);
        assert.match(renderedPayerVersion, /^[A-Za-z0-9+/]{11}=$/); assert.equal(Buffer.from(renderedPayerVersion, 'base64').length, 8);
        const instant = new Date(debit.executedAt), local = canonicalLocal(new Date(instant.getTime() + 7 * 3600000).toISOString().slice(0, 23));
        await form.locator('[name=executedAt]').fill(local);
      }
      await form.locator('[name=externalTransactionId]').fill(debit.externalId); await form.locator('[name=evidenceReference]').fill('WEB07 đã xác minh thực trả theo chứng từ thực chi có trước'); await form.locator('input[type=checkbox]').check();
      assert.match(await admin.locator('[data-refund-id]').first().innerText(), /chờ thực hoàn/); await measure('admin-approved-before-payout', admin);
    });
    await test('actual payout response loss survives reload with removed form and exact retry cannot create a second outflow', async () => {
      active = admin; const bodies = []; let lost = false, payoutCommitted; const actualPayoutCommitted = new Promise(resolve => { payoutCommitted = resolve; });
      await admin.route('**' + adminRoute('confirm-execution'), async intercepted => {
        bodies.push(intercepted.request().postDataJSON());
        if (!lost) { lost = true; const actual = await intercepted.fetch(); assert.equal(actual.status(), 200); await intercepted.abort('failed'); payoutCommitted(); } else await intercepted.continue();
      });
      await admin.locator('form[data-operation=refund-confirm] button[type=submit]').click(); await actualPayoutCommitted; await admin.locator('#sf-control-pending [data-retry-command]').waitFor({ state: 'visible' }); await checkpoint('checkpoint-payout-lost', 'checkpoint-payout-verified');
      await admin.reload({ waitUntil: 'domcontentloaded' }); assert.equal(await admin.locator('form[data-operation=refund-confirm]').count(), 0);
      await admin.locator('#sf-control-pending [data-retry-command]').waitFor({ state: 'visible' });
      const navigation = admin.waitForNavigation({ waitUntil: 'domcontentloaded' }); void navigation.catch(() => {}); await admin.locator('#sf-control-pending [data-retry-command]').click(); await navigation;
      assert.equal(bodies.length, 2); assert.deepEqual(bodies[0], bodies[1]); assert.equal(await admin.locator('#sf-control-pending').isVisible(), false);
      assert.equal(bodies[0].payerBankAccountVersion, renderedPayerVersion);
      fs.writeFileSync(path.join(info.output, 'payout-replay-proof.json'), JSON.stringify({ attempts: 2, identicalPayload: true, originalKey: true, formAbsentAfterReload: true })); await admin.unroute('**' + adminRoute('confirm-execution')); await measure('admin-executed', admin);
    });
    await test('guest refund progress remains truthful in maintenance and invalid or empty capability clears all previous private state', async () => {
      await checkpoint('checkpoint-maintenance', 'checkpoint-maintenance-ready'); active = guest; await guestRefresh();
      await guest.locator('#guest-refund-detail', { hasText: 'Đã xác nhận thực hoàn' }).waitFor();
      assert.equal(await guest.locator('#guest-phase').innerText(), 'Đã hủy');
      assert.equal(await guest.locator('#guest-refund').innerText(), 'Đã hoàn tiền có chứng cứ');
      await measure('guest-executed-maintenance', guest);
      await sameDocument('/shop/orders#invalid-token'); await guest.locator('#guest-order').waitFor({ state: 'hidden' }); assert.equal(await guest.locator('#guest-cancellation').isVisible(), false); assert.equal(await guest.locator('#guest-refund-detail').innerText(), '');
      await sameDocument('/shop/orders#'); assert.equal(await guest.locator('#guest-cancellation').isVisible(), false); assert.equal(await guest.locator('#guest-cancellation-history').innerText(), ''); await screenshot('guest-private-cleared');
    });
    await test('actual response digests token URL referrer and storage privacy remain valid with only the two deliberate response losses', async () => {
      await Promise.all(proofTasks);
      for (const row of privateRequests) { const origin = new URL(row.raw).origin, a = privacyVerdict(row.raw, tokens, origin, responseProofs), b = privacyVerdict(row.referrer, tokens, origin, responseProofs); row.row.secretInUrl = a.forbidden; row.row.secretInReferrer = b.forbidden; row.row.versionEqualsResponseBodySHA256Base64Url = a.verifiedAssetVersion; }
      assert.ok(requests.every(row => !row.secretInUrl && !row.secretInReferrer)); assert.deepEqual(errors, []);
      assert.equal(failedRequests.length, 2); assert.ok(failedRequests.every(row => ['/shop/orders/cancellation', adminRoute('confirm-execution')].includes(row.path) && /ERR_FAILED/.test(row.reason)));
      for (const page of [guest, admin]) {
        const storage = await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }));
        for (const token of tokens) { assert.ok(!storage.includes(token)); assert.ok(!storage.includes(encodeURIComponent(token))); }
      }
      for (const required of ['/assets/cancellation.js', '/assets/cancellation.css', '/Admin/js/storefront-refunds.js', '/Admin/css/storefront-refunds.css']) assert.ok([...responseProofs.keys()].some(url => new URL(url).pathname === required), required + ' actual served proof');
    });
  } catch { process.exitCode = 1; }
  finally {
    await Promise.all(proofTasks);
    const served = [...responseProofs.entries()].map(([url, proof]) => ({ ...urlMetadata(url), status: proof.status, method: proof.method, sha256: proof.sha256, bytes: proof.bytes }));
    const timing = { clock: 'owned-node-monotonic-relative-ms', capturedAtMs: elapsedMs(), requests: requestTimings, pages: pageTimings };
    fs.writeFileSync(path.join(info.output, 'browser-results.json'), JSON.stringify({ results, measurements, errors, requests, failedRequests, served, privacyOracle: privacyOracle(), recall: info.recall, adminBodyOnly: true, timing }, null, 2));
    await browser.close();
  }
})().catch(error => { console.error(metadataText(error.stack)); process.exitCode = 1; });

