// Privacy helper reused from frozen WEB05 V8; business oracle below is independent WEB06.
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');

const staticAssets = new Set(['/assets/storefront.css', '/assets/storefront.js', '/assets/checkout.js', '/assets/pending-order.js', '/assets/orders.js', '/assets/qr.js', '/assets/cancellation.css', '/assets/cancellation.js', '/Admin/css/storefront-fulfillment.css', '/Admin/js/storefront-fulfillment.js', '/lib/bootstrap/dist/css/bootstrap.min.css', '/css/site.css']);
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
  const info = JSON.parse(await new Promise(resolve => { let data = ''; process.stdin.on('data', part => data += part); process.stdin.on('end', () => resolve(data)); }));
  fs.mkdirSync(info.output, { recursive: true });
  const results = [], measurements = [], errors = [], requests = [], failedRequests = [], privateRequests = [], proofTasks = [], responseProofs = new Map();
  const tokens = [new URL(info.guestLink, info.publicUrl).hash.slice(1)], origins = new Set([new URL(info.adminUrl).origin, new URL(info.publicUrl).origin]);
  const safe = text => tokens.reduce((value, token) => value.split(token).join('[CAPABILITY]'), metadataText(text));
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const adminContext = await browser.newContext({ viewport: { width: 390, height: 844 }, timezoneId: 'Asia/Bangkok', extraHTTPHeaders: { 'X-Web06-User': String(info.userId), 'X-Web06-Terminal': String(info.terminalId) } });
  const guestContext = await browser.newContext({ viewport: { width: 390, height: 844 }, timezoneId: 'Asia/Bangkok' });
  const admin = await adminContext.newPage(), guest = await guestContext.newPage(); let active = admin;
  for (const context of [adminContext, guestContext]) await context.route('**/*', route => { const u = new URL(route.request().url()); return origins.has(u.origin) || ['blob:', 'data:'].includes(u.protocol) ? route.continue() : route.abort('blockedbyclient'); });
  for (const page of [admin, guest]) {
    page.on('pageerror', error => errors.push(safe(error.message)));
    page.on('requestfailed', request => failedRequests.push({ ...urlMetadata(request.url()), reason: safe(request.failure()?.errorText || '') }));
    page.on('request', request => { const raw = request.url(), referrer = request.headers().referer || '', row = { method: request.method(), ...urlMetadata(raw), referrer: urlMetadata(referrer), navigation: request.isNavigationRequest() }; requests.push(row); privateRequests.push({ raw, referrer, row }); });
    page.on('response', response => {
      const request = response.request(), u = new URL(request.url()); if (!origins.has(u.origin) || !staticAssets.has(u.pathname)) return;
      proofTasks.push(response.body().then(body => responseProofs.set(u.href, { status: response.status(), method: request.method(), bodyDigest: digest(body), sha256: crypto.createHash('sha256').update(body).digest('hex'), bytes: body.length })).catch(() => {}));
    });
  }
  const screenshot = async label => active.screenshot({ path: path.join(info.output, label + '.png'), fullPage: true, mask: [active.locator('#guest-link-input,a[href*="/shop/orders#"]')], maskColor: '#222' });
  const test = async (name, body) => { try { await body(); results.push({ name, passed: true }); } catch (error) { await screenshot('failure-' + results.length).catch(() => {}); results.push({ name, passed: false, error: safe(error.stack) }); process.exitCode = 1; throw error; } };
  const checkpoint = async (ready, ack) => { fs.writeFileSync(path.join(info.output, ready), 'Owned fixture checkpoint; no secret.'); const until = Date.now() + 45000; while (!fs.existsSync(path.join(info.output, ack))) { if (Date.now() > until) throw new Error('Owned checkpoint timed out: ' + ack); await new Promise(resolve => setTimeout(resolve, 50)); } };
  const route = operation => '/admin/storefront/fulfillment/' + info.orderId + '/' + operation;
  const submit = async operation => {
    const response = active.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === route(operation));
    const navigation = active.waitForNavigation({ waitUntil: 'domcontentloaded' });
    // Observe early rejection while preserving the ORIGINAL success-path await.
    void navigation.catch(() => {});
    await active.locator('#sf-' + operation + '-form button[type=submit]').click(); const result = await response;
    if (result.status() !== 200) {
      const body = await result.json().catch(() => null);
      fs.writeFileSync(path.join(info.output, 'unexpected-' + operation + '-' + results.length + '.json'), JSON.stringify({ operation, actualStatus: result.status(),
        code: metadataText(safe(String(body?.code ?? ''))), message: metadataText(safe(String(body?.message ?? ''))) }));
    }
    assert.equal(result.status(), 200, operation); await navigation;
  };
  const canonicalLocal = value => value.replace(/(\.\d*?[1-9])0+$/, '$1').replace(/\.0+$/, '').replace(/(T\d{2}:\d{2}):00$/, '$1');
  const currentLocal = async input => { const step = Number(await input.getAttribute('step') || 60); return canonicalLocal(new Date(Date.now() + 7 * 3600000).toISOString().slice(0, step > 0 && step < 1 ? 23 : step > 0 && step < 60 ? 19 : 16)); };
  const fillCarrier = async () => {
    const form = admin.locator('#sf-dispatch-form');
    if (info.national) { await form.locator('[name=carrierName]').fill('Hãng giao fixture WEB06'); await form.locator('[name=trackingCode]').fill('W06-TRACK-OWNED'); }
    else await form.locator('[name=courierUserId]').selectOption(String(info.courierId));
    await form.locator('[name=note]').fill('WEB06 chứng cứ bàn giao thực tế đủ hàng theo từng nguồn');
    await form.locator('input[type=checkbox]').check();
  };
  async function measure(label, page, widths = [320, 360, 390, 430, 1440]) {
    active = page;
    for (const width of widths) {
      await page.setViewportSize({ width, height: width === 1440 ? 1000 : 844 }); await page.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); });
      const value = await page.evaluate(() => {
        const shown = element => { const r = element.getBoundingClientRect(); return r.width > 1 && r.height > 1 && getComputedStyle(element).visibility !== 'hidden' && !element.closest('[hidden]'); };
        const color = text => text.match(/[\d.]+/g)?.map(Number) || [0, 0, 0, 0];
        const blend = (fg, bg) => fg.slice(0, 3).map((v, i) => v * (fg[3] ?? 1) + bg[i] * (1 - (fg[3] ?? 1)));
        const bg = element => { const layers = []; for (let current = element; current; current = current.parentElement) { const c = color(getComputedStyle(current).backgroundColor); layers.push(c); if ((c[3] ?? 1) === 1) break; } return layers.reverse().reduce((value, layer) => blend(layer, value), [255, 255, 255]); };
        const lum = rgb => rgb.map(v => v / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
        const nodes = [...document.querySelectorAll('body *')].filter(e => shown(e) && !e.closest('button:disabled,option,script,style,svg') && [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim()));
        const contrast = nodes.map(e => { const s = getComputedStyle(e), back = bg(e), a = lum(back), b = lum(blend(color(s.color), back)), large = parseFloat(s.fontSize) >= 24 || parseFloat(s.fontSize) >= 18.66 && parseInt(s.fontWeight, 10) >= 700; return { tag: e.tagName, ratio: (Math.max(a, b) + .05) / (Math.min(a, b) + .05), threshold: large ? 3 : 4.5 }; });
        const controls = [...document.querySelectorAll('button,input:not([type=hidden]),select,textarea,.bottom-nav a')].filter(shown).map(e => { const target = e.type === 'checkbox' && e.closest('label') || e, r = target.getBoundingClientRect(); return { id: e.id || e.name || e.tagName, width: r.width, height: r.height }; });
        const root = document.querySelector('.sf-operations') || document.body;
        return { width: innerWidth, scrollWidth: document.documentElement.scrollWidth, font: parseFloat(getComputedStyle(root).fontSize), adminBodyOnly: !!document.querySelector('.sf-operations'), contrastSamples: contrast.length, minContrast: Math.min(...contrast.map(x => x.ratio)), contrastFailures: contrast.filter(x => x.ratio + .02 < x.threshold), smallControls: controls.filter(x => x.width < 43.9 || x.height < 43.9) };
      });
      measurements.push({ label, ...value }); await screenshot(label + '-' + width); assert.ok(value.scrollWidth <= width + 1, label + ' document overflow ' + JSON.stringify(value)); assert.ok(value.font >= 15.9, label + ' font16'); assert.deepEqual(value.smallControls, [], label + ' touch targets'); assert.deepEqual(value.contrastFailures, [], label + ' contrast');
    }
  }
  try {
    await test('actual queue and initial guest tracking render with typed filters and five responsive widths', async () => {
      assert.equal((await admin.goto(info.adminUrl + '/admin/storefront/fulfillment')).status(), 200); await admin.locator('text=' + info.code).first().waitFor(); await measure('queue', admin);
      assert.equal((await guest.goto(info.publicUrl + info.guestLink)).status(), 200); await guest.locator('#guest-order').waitFor({ state: 'visible' }); assert.equal(await guest.locator('#guest-code').innerText(), 'Đơn ' + info.code);
    });
    const attempts = [];
    await test('real prepare response loss retains exact command and retry commits once', async () => {
      active = admin; await admin.goto(info.adminUrl + '/admin/storefront/fulfillment/' + info.orderId); let dropped = false;
      await admin.route('**' + route('prepare'), async intercepted => { attempts.push(intercepted.request().postDataJSON()); if (!dropped) { dropped = true; const response = await intercepted.fetch(); assert.equal(response.status(), 200); await intercepted.abort('failed'); } else await intercepted.continue(); });
      await admin.locator('#sf-prepare-form button[type=submit]').click(); await admin.locator('#sf-operation-pending button').waitFor({ state: 'visible' }); await checkpoint('checkpoint-lost-prepare', 'checkpoint-lost-verified');
      const navigation = admin.waitForNavigation({ waitUntil: 'domcontentloaded' }); await admin.locator('#sf-operation-pending button').click(); await navigation; await admin.locator('#sf-pick-form').waitFor();
      assert.equal(attempts.length, 2); assert.deepEqual(attempts[0], attempts[1]); fs.writeFileSync(path.join(info.output, 'prepare-replay-proof.json'), JSON.stringify({ attempts: attempts.length, identicalPayload: true, sameOperationId: true })); await admin.unroute('**' + route('prepare'));
    });
    await test('actual SQL order-lock timeout409 preserves original command for identical successful retry', async () => {
      active = admin; const bodies = [];
      const observe = request => { if (request.method() === 'POST' && new URL(request.url()).pathname === route('pick')) bodies.push(request.postDataJSON()); };
      admin.on('request', observe); await admin.locator('#sf-pick-form [name=baseQuantity]').fill('4'); await checkpoint('checkpoint-busy-lock-needed', 'checkpoint-busy-lock-acquired');
      const response = admin.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === route('pick')); await admin.locator('#sf-pick-form button[type=submit]').click();
      const busy = await response; assert.equal(busy.status(), 409); const body = await busy.json(); assert.equal(body.code, 'RETRY_SAME_COMMAND'); assert.equal(body.retryable, true); await admin.locator('#sf-operation-status.alert-danger').waitFor(); await checkpoint('checkpoint-busy-response', 'checkpoint-busy-lock-released');
      await admin.locator('#sf-operation-pending button').waitFor({ state: 'visible' }); const navigation = admin.waitForNavigation({ waitUntil: 'domcontentloaded' }); await admin.locator('#sf-operation-pending button').click(); await navigation;
      admin.off('request', observe); assert.equal(bodies.length, 2); assert.deepEqual(bodies[0], bodies[1]); fs.writeFileSync(path.join(info.output, 'busy-retry-proof.json'), JSON.stringify({ actualHttpStatus: 409, attempts: 2, identicalPayload: true, sameOperationId: true }));
      await checkpoint('checkpoint-busy-retried', 'checkpoint-busy-once-verified');
    });
    await test('field invalid input retains value without a network mutation and partial picking remains visibly short', async () => {
      active = admin; await admin.setViewportSize({ width: 390, height: 844 }); const input = admin.locator('#sf-pick-form [name=baseQuantity]');
      const count = requests.filter(x => x.path === route('pick')).length; await input.fill('1,5'); await admin.locator('#sf-pick-form button[type=submit]').click(); assert.equal(await input.inputValue(), '1,5'); assert.equal(await input.evaluate(e => e.validity.patternMismatch), true); assert.equal(requests.filter(x => x.path === route('pick')).length, count);
      await screenshot('invalid-pick-field'); await input.fill('3'); await admin.locator('#sf-pick-form [name=note]').fill('WEB06 thực tế thiếu một đơn vị khi kiểm đếm'); await submit('pick'); assert.match(await admin.locator('.sf-source').innerText(), /Thiếu\s+1/);
      await fillCarrier(); const response = admin.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === route('dispatch')); await admin.locator('#sf-dispatch-form button[type=submit]').click(); assert.equal((await response).status(), 409); await admin.locator('#sf-operation-status.alert-danger').waitFor(); assert.ok((await admin.locator('#sf-operation-status').innerText()).trim()); await checkpoint('checkpoint-partial-rejected', 'checkpoint-partial-verified');
      await measure('partial-picking', admin);
    });
    await test('keyboard focus is visible and scrolls the real picking control safely at 320', async () => {
      active = admin; await admin.setViewportSize({ width: 320, height: 844 }); await admin.locator('body').click({ position: { x: 1, y: 1 } }); let found = false;
      for (let i = 0; i < 100; i++) { await admin.keyboard.press('Tab'); found = await admin.locator('#sf-pick-form button[type=submit]').evaluate(e => e === document.activeElement); if (found) break; }
      const focus = await admin.locator('#sf-pick-form button[type=submit]').evaluate(e => { const r = e.getBoundingClientRect(); return { top: r.top, bottom: r.bottom, height: innerHeight, outline: parseFloat(getComputedStyle(e).outlineWidth) }; }); measurements.push({ label: 'admin-keyboard', ...focus }); await screenshot('keyboard-320'); assert.ok(found && focus.top >= 0 && focus.bottom <= focus.height && focus.outline >= 2, JSON.stringify(focus));
    });
    await test('full original picked quantity enables real local or nationwide dispatch and guest manual tracking', async () => {
      active = admin; await admin.locator('#sf-pick-form [name=baseQuantity]').fill('4'); await submit('pick'); await fillCarrier(); await submit('dispatch'); await admin.locator('#sf-outcome-form').waitFor(); await measure('shipping', admin);
      const response = guest.waitForResponse(r => new URL(r.url()).pathname === '/shop/orders/access'); await guest.locator('#guest-refresh').click(); assert.equal((await response).status(), 200); await guest.locator('#guest-shipments').waitFor();
      if (info.national) await guest.locator('#guest-shipments', { hasText: 'W06-TRACK-OWNED' }).waitFor(); await measure('guest-shipping', guest); await checkpoint('checkpoint-dispatched', 'checkpoint-dispatch-verified');
    });
    await test('actual successful delivery and customer-paid evidence keep remittance distinct', async () => {
      active = admin; const form = admin.locator('#sf-outcome-form'); await form.locator('[name=delivered]').selectOption('true'); const time = form.locator('[name=resultOccurredAt]'); await time.fill(await currentLocal(time)); await form.locator('[name=evidenceReference]').fill('WEB06 actual controlled browser delivery evidence');
      await form.locator('[name=note]').fill('WEB06 chứng cứ khách đã thực nhận đủ hàng của nguồn giao'); await submit('outcome');
      const paid = admin.locator('#sf-customer-paid-form'); await paid.locator('[name=amount]').fill('200000'); const paidTime = paid.locator('[name=customerPaidAt]'); await paidTime.fill(await currentLocal(paidTime)); await paid.locator('[name=evidenceReference]').fill('WEB06 actual controlled browser customer-paid evidence'); await submit('customer-paid');
      assert.equal(await admin.locator('#sf-post-sale-form button[type=submit]').isDisabled(), true); await measure('delivered-unremitted', admin); await checkpoint('checkpoint-cash-needed', 'checkpoint-cash-written');
    });
    await test('real cash-in identity is reconciled then sale uses existing stock and money once', async () => {
      active = admin; await admin.reload({ waitUntil: 'domcontentloaded' }); const cash = JSON.parse(fs.readFileSync(path.join(info.output, 'checkpoint-cash.json'), 'utf8'));
      await admin.locator('#sf-cash-remittance-form [name=posShiftCashTransactionId]').selectOption(String(cash.id)); await submit('cash-remittance'); await admin.locator('#sf-post-sale-form button[type=submit]').waitFor(); assert.equal(await admin.locator('#sf-post-sale-form button[type=submit]').isEnabled(), true); await submit('post-sale');
      assert.equal(await admin.locator('#sf-post-sale-form').count(), 0); await measure('completed', admin); await checkpoint('checkpoint-completed', 'checkpoint-sale-verified');
    });
    await test('readonly guest delivery remains accessible in maintenance and token change clears shipment data', async () => {
      await checkpoint('checkpoint-maintenance', 'checkpoint-maintenance-closed'); active = guest;
      const response = guest.waitForResponse(r => new URL(r.url()).pathname === '/shop/orders/access'); await guest.locator('#guest-refresh').click(); assert.equal((await response).status(), 200); await guest.locator('#guest-phase').filter({ hasText: 'Hoàn tất' }).waitFor(); await measure('guest-completed-maintenance', guest);
      const previous = guest.url(); const navigation = await guest.goto(info.publicUrl + '/shop/orders#invalid-token'); if (navigation === null) { const old = new URL(previous), current = new URL(guest.url()); assert.equal(old.origin + old.pathname + old.search, current.origin + current.pathname + current.search); assert.equal(current.hash, '#invalid-token'); }
      await guest.locator('#guest-order').waitFor({ state: 'hidden' }); assert.equal(await guest.locator('#guest-shipments').innerText(), ''); assert.equal(await guest.locator('#guest-incident').innerText(), ''); await screenshot('guest-token-cleared');
    });
    await test('actual response-backed static digests and every token URL/referrer privacy proof remain valid', async () => {
      await Promise.all(proofTasks); for (const row of privateRequests) { const origin = new URL(row.raw).origin; const verdict = privacyVerdict(row.raw, tokens, origin, responseProofs), referrer = privacyVerdict(row.referrer, tokens, origin, responseProofs); row.row.secretInUrl = verdict.forbidden; row.row.secretInReferrer = referrer.forbidden; if (verdict.verifiedAssetVersion) row.row.versionEqualsResponseBodySHA256Base64Url = true; }
      assert.ok(requests.every(x => !x.secretInUrl && !x.secretInReferrer)); assert.equal(errors.length, 0); assert.ok(failedRequests.every(x => x.path === route('prepare') && /ERR_FAILED/.test(x.reason)));
    });
  } catch { process.exitCode = 1; }
  finally {
    await Promise.all(proofTasks); const served = [...responseProofs.entries()].map(([url, proof]) => ({ ...urlMetadata(url), status: proof.status, method: proof.method, sha256: proof.sha256, bytes: proof.bytes }));
    fs.writeFileSync(path.join(info.output, 'browser-results.json'), JSON.stringify({ results, measurements, errors, failedRequests, requests, served, privacyOracle: privacyOracle(), national: info.national, adminBodyOnly: true }, null, 2));
    await browser.close();
  }
})().catch(error => { console.error(metadataText(error.stack)); process.exitCode = 1; });

