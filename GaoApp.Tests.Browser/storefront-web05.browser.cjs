'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');

const staticAssets = new Set(['/assets/storefront.css', '/assets/storefront.js', '/assets/checkout.js', '/assets/pending-order.js', '/assets/orders.js', '/assets/qr.js', '/assets/cancellation.css', '/assets/cancellation.js']);
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
  const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', x => text += x); process.stdin.on('end', () => resolve(text)); }));
  fs.mkdirSync(info.output, { recursive: true });
  const results = [], measurements = [], errors = [], failedRequests = [], requests = [], snapshots = [];
  const privateRequests = [], byRequest = new Map(), responseProofs = new Map(), requestProofs = new Map(), proofTasks = [];
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, timezoneId: 'Asia/Bangkok', permissions: ['clipboard-read', 'clipboard-write'], acceptDownloads: true });
  let page;
  const safe = text => String(text).replace(/\/shop\/orders#[A-Za-z0-9_-]+/g, '/shop/orders#[REDACTED]').replace(/\b[A-Za-z0-9_-]{43}\b/g, '[REDACTED43]');
  const observe = async () => {
    page = await context.newPage(); page.on('pageerror', error => errors.push(safe(error.message)));
    page.on('requestfailed', request => failedRequests.push({ path: metadataText(new URL(request.url()).pathname), reason: request.failure()?.errorText }));
    page.on('request', request => {
      const raw = request.url(), referer = request.headers().referer || '', row = { method: request.method(), ...urlMetadata(raw), resourceType: request.resourceType(), isNavigation: request.isNavigationRequest(),
        referrer: urlMetadata(referer), secretInUrl: /[A-Za-z0-9_-]{43}/.test(raw), secretInReferrer: /[A-Za-z0-9_-]{43}/.test(referer) };
      requests.push(row); privateRequests.push({ raw, referer, row }); byRequest.set(request, row);
    });
    page.on('response', response => {
      const request = response.request(), raw = request.url(), u = new URL(raw), row = byRequest.get(request);
      if (u.origin !== new URL(info.baseUrl).origin || !staticAssets.has(u.pathname)) return;
      proofTasks.push(response.body().then(body => {
        const proof = { status: response.status(), method: request.method(), bodyDigest: digest(body) }; responseProofs.set(u.href, proof); requestProofs.set(row, proof);
        if (row) row.assetResponse = { status: proof.status, bytes: body.length, sha256: crypto.createHash('sha256').update(body).digest('hex'), versionEqualsResponseBodySHA256Base64Url: proof.bodyDigest === u.searchParams.get('v') };
      }).catch(() => { if (row) row.assetResponse = { status: response.status(), bodyUnavailable: true }; }));
    });
  };
  await context.route('**/*', route => { const url = new URL(route.request().url()); return ['data:', 'blob:'].includes(url.protocol) || url.origin === new URL(info.baseUrl).origin ? route.continue() : route.abort('blockedbyclient'); });
  await observe();
  const settle = async () => page.evaluate(async () => { await document.fonts.ready; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); });
  const screenshot = async label => page.screenshot({ path: path.join(info.output, label + '.png'), fullPage: true, mask: [page.locator('#guest-link-input,a[href*="/shop/orders#"]')], maskColor: '#222222' });
  const test = async (name, body, independent = false) => { try { await body(); results.push({ name, passed: true }); } catch (error) { await screenshot('failure-' + results.length).catch(() => {}); results.push({ name, passed: false, error: safe(error.stack) }); process.exitCode = 1; if (!independent) throw error; } };
  const goto = async route => {
    const target = new URL(info.baseUrl + route), previous = new URL(page.url());
    const navigation = { stage: 'navigation proof', path: target.pathname, hashChanged: previous.hash !== target.hash, tokenLookups: [] };
    const targetToken = target.pathname === '/shop/orders' && /^#[A-Za-z0-9_-]{43}$/.test(target.hash) ? target.hash.slice(1) : null;
    if (goto.observer) page.off('request', goto.observer);
    goto.observer = request => {
      const pathname = new URL(request.url()).pathname;
      if (request.method() === 'POST' && ['/shop/orders/access', '/shop/orders/qr'].includes(pathname)) {
        let payload; try { payload = request.postDataJSON(); } catch { payload = null; }
        navigation.tokenLookups.push({ path: pathname, tokenPresent: /^[A-Za-z0-9_-]{43}$/.test(payload?.token || ''), tokenMatchesTarget: !!targetToken && payload?.token === targetToken });
      }
    };
    page.on('request', goto.observer); snapshots.push(navigation);
    const marker = 'web05-' + Date.now() + '-' + Math.random();
    await page.evaluate(value => { window.__web05NavigationProof = value; }, marker);
    const response = await page.goto(target.href);
    if (response) assert.equal(response.status(), 200);
    else {
      assert.equal(previous.origin, target.origin); assert.equal(previous.pathname, target.pathname); assert.equal(previous.search, target.search);
      const actual = await page.evaluate(() => ({ href: location.href, marker: window.__web05NavigationProof, ready: document.readyState, hasMain: !!document.querySelector('main') }));
      assert.equal(actual.href, target.href); assert.equal(actual.marker, marker); assert.ok(['interactive', 'complete'].includes(actual.ready)); assert.equal(actual.hasMain, true);
      navigation.sameDocumentVerified = true;
    }
    await settle();
    navigation.visibleAtReturn = await page.evaluate(() => ({ guestPayment: document.querySelector('#guest-payment')?.textContent || '', qrState: document.querySelector('#qr-state')?.textContent || '', reference: document.querySelector('#qr-content')?.textContent || '' }));
  };
  const checkpoint = async (ready, acknowledged) => { fs.writeFileSync(path.join(info.output, ready), 'Owned fixture checkpoint; no capability.'); const end = Date.now() + 45000;
    while (!fs.existsSync(path.join(info.output, acknowledged))) { if (Date.now() > end) throw new Error('Fixture checkpoint timed out: ' + acknowledged); await new Promise(r => setTimeout(r, 50)); } };
  async function review(label, widths = [320, 360, 390, 430, 1440]) {
    const failures = [];
    for (const width of widths) {
      await page.setViewportSize({ width, height: width === 1440 ? 1000 : 844 }); await settle();
      const measured = await page.evaluate(() => {
        const visible = e => { const r = e.getBoundingClientRect(), s = getComputedStyle(e); return r.width > 1 && r.height > 1 && s.visibility !== 'hidden' && !e.closest('[hidden],.sr-only'); };
        const color = text => { const values = text.match(/[\d.]+/g); return values ? values.map(Number) : [0, 0, 0, 0]; };
        const blend = (fg, bg) => fg.slice(0, 3).map((v, i) => v * (fg[3] ?? 1) + bg[i] * (1 - (fg[3] ?? 1)));
        const background = e => { const layers = []; for (let node = e; node; node = node.parentElement) { const c = color(getComputedStyle(node).backgroundColor); layers.push(c); if ((c[3] ?? 1) === 1) break; } let result = [255, 255, 255]; for (const layer of layers.reverse()) result = blend(layer, result); return result; };
        const luminance = rgb => rgb.map(v => v / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
        const text = [...document.querySelectorAll('body *')].filter(e => visible(e) && !e.closest('button:disabled,option,script,style,svg') && [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim()));
        const contrast = text.map(e => { const s = getComputedStyle(e), bg = background(e), fg = blend(color(s.color), bg), a = luminance(fg), b = luminance(bg); const large = parseFloat(s.fontSize) >= 24 || parseFloat(s.fontSize) >= 18.66 && parseInt(s.fontWeight, 10) >= 700;
          return { tag: e.tagName, text: e.textContent.trim().slice(0, 70), ratio: (Math.max(a, b) + .05) / (Math.min(a, b) + .05), threshold: large ? 3 : 4.5 }; });
        const controls = [...document.querySelectorAll('button,input:not([type=hidden]),select,textarea,.button,.icon-button,.bottom-nav a')].filter(visible).map(e => { const target = e.type === 'checkbox' && [...(e.labels || [])].find(label => label.control === e) || e, r = target.getBoundingClientRect(); return { label: e.id || e.getAttribute('aria-label') || e.textContent.trim().slice(0, 60), width: r.width, height: r.height, checkboxLabel: target !== e }; });
        return { width: innerWidth, scrollWidth: document.documentElement.scrollWidth, bodyFont: parseFloat(getComputedStyle(document.body).fontSize), loadedInter: [...document.fonts].some(f => f.family === 'Inter' && f.status === 'loaded'),
          adminShell: !!document.querySelector('#layout-menu,[data-mobile-shell-owner],.gao-mobile-pins'), smallControls: controls.filter(x => x.width < 43.9 || x.height < 43.9),
          contrastFailures: contrast.filter(x => x.ratio + .02 < x.threshold), contrastSamples: contrast.length, minimumContrast: Math.min(...contrast.map(x => x.ratio)) };
      });
      measurements.push({ label, ...measured }); await screenshot(label + '-' + width);
      if (measured.scrollWidth > width + 1) failures.push(`${label} ${width} overflow=${measured.scrollWidth}`);
      if (measured.bodyFont < 15.9 || !measured.loadedInter || measured.adminShell) failures.push(`${label} ${width} font/shell: ${JSON.stringify(measured)}`);
      if (measured.smallControls.length || measured.contrastFailures.length) failures.push(`${label} ${width}: ${JSON.stringify({ small: measured.smallControls, contrast: measured.contrastFailures })}`);
    }
    assert.deepEqual(failures, []);
  }

  let guestLink, reference;
  try {
    await test('actual product and guest checkout explicitly select QR with authoritative goods plus shipping', async () => {
      await goto('/shop/products/' + info.primary.productId); await page.locator('#variant-choice').selectOption(String(info.primary.variantId)); await page.locator('#unit-choice').selectOption(String(info.primary.unitId)); await page.locator('#product-quantity').fill('1');
      let response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/cart/resolve'); await page.locator('#product-choice button[type=submit]').click(); assert.equal((await response).status(), 200);
      await goto('/shop/checkout'); await page.locator('#checkout-form').waitFor({ state: 'visible' });
      assert.equal(await page.locator('#checkout-confirm').isDisabled(), true); assert.doesNotMatch(await page.locator('#checkout-confirm').innerText(), /\bCOD\b/i);
      await page.locator('[name=RecipientName]').fill('Khách WEB05 fixture'); await page.locator('[name=RecipientPhone]').fill('0901234567'); await page.locator('[name=StreetAddress]').fill('123 Đường fixture, không giao thật');
      await page.locator('[name=ProvinceCode]').selectOption('01'); await page.locator('[name=WardCode] option[value="01001"]').waitFor({ state: 'attached' }); await page.locator('[name=WardCode]').selectOption('01001');
      await page.locator('#checkout-payment-method').selectOption('1'); response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/checkout/quote'); await page.locator('#checkout-quote').click(); const quote = await (await response).json();
      assert.equal(quote.paymentMethod, 1); assert.equal(quote.goodsAmount, 25000); assert.equal(quote.shippingCharge, 20000); assert.equal(quote.totalDue, 45000); assert.equal((await page.locator('#quote-total').innerText()).replace(/\s/g, ''), '45.000đ');
      assert.doesNotMatch(await page.locator('#checkout-message').innerText(), /\bCOD\b/i);
    });
    await test('QR checkout controls remain usable at every mobile and desktop width', async () => review('qr-checkout'), true);
    await test('actual QR placement returns private guest access and initial queued state without bank credentials', async () => {
      const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/checkout/place'); await page.locator('#checkout-confirm').click(); const placed = await (await response).json(); assert.equal(placed.status, 0); guestLink = placed.order.guestLink;
      assert.match(guestLink, /^\/shop\/orders#[A-Za-z0-9_-]{43}$/); await goto(guestLink); await page.locator('#guest-qr').waitFor({ state: 'visible' });
      await checkpoint('checkpoint-queued', 'checkpoint-bank-ready');
      const opened = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/orders/qr'); await page.locator('#qr-reopen').click(); const dto = await (await opened).json(); assert.equal(dto.state, 2); reference = dto.providerReference; assert.match(reference, /^W[0-9]{12}$/);
      assert.equal(dto.amountValue, '45000'); await page.locator('#qr-image:not([hidden])').waitFor(); assert.equal((await page.locator('#qr-amount').innerText()).replace(/\s/g, ''), '45.000đ'); assert.equal(await page.locator('#qr-content').innerText(), reference);
      snapshots.push({ stage: 'pending', reference, amountText: dto.amountText, expiresAtUtc: dto.expiresAtUtc }); assert.match(dto.expiresAtUtc, /Z$/);
    });
    await test('pending QR responsive measurements include real dynamic image and clipboard controls', async () => review('qr-pending'), true);
    await test('QR clipboard content amount and account use exact server values and PNG download is same-origin validated image', async () => {
      for (const [button, expected] of [['#qr-copy-content', reference], ['#qr-copy-amount', '45000'], ['#qr-copy-account', info.account]]) { await page.locator(button).click(); assert.equal(await page.evaluate(() => navigator.clipboard.readText()), expected); }
      assert.match(await page.locator('#qr-image').getAttribute('src'), /^blob:/); const download = page.waitForEvent('download'); await page.locator('#qr-download').click(); const file = await download;
      assert.equal(file.suggestedFilename(), `Thanh-toan-${reference}.png`); await file.saveAs(path.join(info.output, 'synthetic-qr-download.png')); const bytes = fs.readFileSync(path.join(info.output, 'synthetic-qr-download.png')); assert.deepEqual([...bytes.subarray(0, 8)], [137, 80, 78, 71, 13, 10, 26, 10]);
    });
    await test('transient private QR lookup network failure retains order and reopening uses same reference', async () => {
      await page.route('**/shop/orders/qr', route => route.abort('failed'), { times: 1 }); await page.locator('#qr-reopen').click(); await page.waitForFunction(() => document.querySelector('#qr-message').textContent.includes('vẫn giữ nguyên'));
      assert.equal(await page.locator('#qr-content').innerText(), reference); await screenshot('qr-network-error'); const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/orders/qr'); await page.locator('#qr-reopen').click(); const dto = await (await response).json(); assert.equal(dto.providerReference, reference); assert.equal(dto.state, 2);
    });
    await test('keyboard focus on reopen remains visible above mobile navigation and reduced motion is respected', async () => {
      await page.setViewportSize({ width: 320, height: 844 }); await goto(guestLink); await page.locator('#qr-reopen').waitFor(); let reached = false;
      for (let i = 0; i < 60; i++) { await page.keyboard.press('Tab'); if (await page.locator('#qr-reopen').evaluate(e => e === document.activeElement)) { reached = true; break; } }
      const measure = await page.locator('#qr-reopen').evaluate(e => { const r = e.getBoundingClientRect(), nav = document.querySelector('.bottom-nav').getBoundingClientRect(); return { top: r.top, bottom: r.bottom, navTop: nav.top, outline: parseFloat(getComputedStyle(e).outlineWidth) }; }); measurements.push({ label: 'QR keyboard focus', ...measure }); await screenshot('qr-keyboard-320'); assert.ok(reached && measure.top >= 0 && measure.bottom <= measure.navTop && measure.outline >= 2, JSON.stringify(measure));
      await page.emulateMedia({ reducedMotion: 'reduce' }); const motion = await page.evaluate(() => ({ reduced: matchMedia('(prefers-reduced-motion:reduce)').matches, moving: document.getAnimations().filter(a => a.playState === 'running').length })); assert.ok(motion.reduced); assert.equal(motion.moving, 0); await page.emulateMedia({ reducedMotion: 'no-preference' });
    }, true);
    await test('trusted server worker confirms payment while customer page is closed and new page sees paid not delivered', async () => {
      await page.close(); await checkpoint('checkpoint-page-closed', 'checkpoint-paid'); await observe(); await goto(guestLink); await page.waitForFunction(() => document.querySelector('#qr-state').textContent.includes('Đã xác minh'));
      assert.equal(await page.locator('#qr-image').isVisible(), false); assert.equal(await page.locator('#qr-download').isVisible(), false); assert.equal(await page.locator('#qr-copy-amount').isDisabled(), true);
      assert.equal(await page.locator('#qr-message').innerText(), 'Ngân hàng đã xác minh khoản tiền. Xem trạng thái giao hàng và khoản hoàn ở thông tin đơn.');
      assert.equal(await page.locator('#guest-phase').innerText(), 'Chờ cửa hàng chuẩn bị'); assert.equal(await page.locator('#guest-delivery').innerText(), 'Chưa bàn giao giao hàng');
      assert.equal(await page.locator('#qr-content').innerText(), reference); await screenshot('qr-paid-after-page-closed'); snapshots.push({ stage: 'paid', reference });
    });
    await test('paid QR responsive measurements keep money and delivery states distinct', async () => review('qr-paid'), true);
    await test('underpayment produces durable review with no reusable pay image or false paid state', async () => {
      await checkpoint('checkpoint-review-ready', 'checkpoint-reviewed'); await goto(info.reviewGuestLink); await page.waitForFunction(() => document.querySelector('#qr-state').textContent.includes('đối soát'));
      assert.equal(await page.locator('#qr-image').isVisible(), false); assert.equal(await page.locator('#qr-download').isVisible(), false); assert.equal(await page.locator('#qr-copy-amount').isDisabled(), true); assert.ok((await page.locator('#qr-message').innerText()).includes('không chuyển thêm'));
      snapshots.push({ stage: 'review' });
    });
    await test('review QR responsive measurements preserve warnings and disabled payment controls', async () => review('qr-review'), true);
    await test('private capabilities never appear in any HTTP request URL and no uncaught JavaScript errors occur', async () => {
      await Promise.all(proofTasks); const tokens = [guestLink, info.reviewGuestLink].map(link => new URL(link, info.baseUrl).hash.slice(1)); assert.ok(tokens.every(token => /^[A-Za-z0-9_-]{43}$/.test(token)));
      for (const item of privateRequests) {
        const ownProof = requestProofs.get(item.row), ownProofs = new Map(ownProof ? [[new URL(item.raw).href, ownProof]] : []);
        const uri = privacyVerdict(item.raw, tokens, new URL(info.baseUrl).origin, ownProofs), referer = privacyVerdict(item.referer, tokens, new URL(info.baseUrl).origin, responseProofs);
        item.row.secretInUrl = uri.forbidden; item.row.secretInReferrer = referer.forbidden; item.row.actualCapabilityInUrl = uri.capabilityMatch; item.row.actualCapabilityInReferrer = referer.capabilityMatch;
        item.row.verifiedAssetVersion = uri.verifiedAssetVersion; item.row.verifiedReferrerAssetVersion = referer.verifiedAssetVersion;
      }
      assert.ok(requests.every(x => !x.secretInUrl)); assert.ok(requests.every(x => !x.secretInReferrer)); assert.deepEqual(errors, []);
    });
  } catch (error) { console.error(safe(error.stack)); process.exitCode = 1; if (!page.isClosed()) await screenshot('failure-last-state').catch(() => {}); }
  finally {
    fs.writeFileSync(path.join(info.output, 'browser-results.json'), JSON.stringify({ results, measurements, errors, failedRequests, requests, snapshots, privateCapabilitiesRedacted: true, browserVersion: browser.version() }, null, 2));
    await context.close(); await browser.close();
  }
})().catch(error => { console.error(String(error.stack).replace(/\b[A-Za-z0-9_-]{43}\b/g, '[REDACTED43]')); process.exitCode = 1; });
