'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const sha = data => crypto.createHash('sha256').update(data).digest('hex').toUpperCase();
(async () => {
  const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', x => text += x); process.stdin.on('end', () => resolve(text)); }));
  const decode = input => { let value = String(input); for (let n = 0; n < 4 && value.length < 1000000; n++) { try { const next = decodeURIComponent(value); if (next === value) break; value = next; } catch { break; } } return value; };
  const secrets = [];
  const safe = input => { let value = decode(input); for (const secret of secrets) value = value.split(secret).join('[CAPABILITY]'); return value.replace(/\/shop\/orders#[A-Za-z0-9_-]+/g, '/shop/orders#[CAPABILITY]').replace(/(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])/g, '[OPAQUE43]'); };
  const report = { results: [], assets: [], requests: [], failedRequests: [], errors: [], layouts: [], textScale: [], skip: [], screenshots: [],
    transport: { actualHttps: true, certificateSpkiPin: info.spkiPin, ignoreHTTPSErrors: false }, physicalPhone: false, nativeOSZoom: false, userTextOverride: true,
    adminScope: 'Real settings controller/view body plus unchanged Bootstrap/site assets; no AdminMain/sidebar/authentication certification' };
  const pending = [], captureErrors = [], raw = [];
  const allowedPublic = new Set(['/assets/storefront.css', '/assets/storefront.js', '/assets/cart.js', '/assets/checkout.css', '/assets/checkout.js', '/assets/pending-order.js', '/assets/orders.js', '/assets/qr.js', '/assets/product-fallback.svg',
    '/assets/cancellation.css', '/assets/cancellation.js', '/assets/product-viewer.css', '/assets/product-viewer.js', '/assets/fonts/InterVariable.woff2']);
  const allowedAdmin = new Set(['/lib/bootstrap/dist/css/bootstrap.min.css', '/css/site.css']);
  let browser, context, publicPage, adminPage, css, adminNetwork, loss;
  const step = async (name, body) => { try { await body(); report.results.push({ name, passed: true }); } catch (error) { report.results.push({ name, passed: false, error: safe(error.stack) }); throw error; } };
  const metadata = url => { const parsed = new URL(url); return { origin: parsed.origin === info.publicBase ? 'owned-public' : parsed.origin === info.adminBase ? 'owned-admin' : 'unexpected', path: safe(parsed.pathname), queryKeys: [...parsed.searchParams.keys()] }; };
  const drain = async () => { await Promise.all(pending); assert.equal(captureErrors.length, 0, 'Actual static response body capture failed'); };
  const screenshot = async (page, name) => { const file = path.join(info.output, name + '.png'); await page.screenshot({ path: file, fullPage: true }); report.screenshots.push({ file, sha256: sha(await fs.readFile(file)) }); };
  const go = async (page, url) => { await drain(); return await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 }); };
  const requestPatterns = [{ urlPattern: '*', requestStage: 'Request' }];
  const nativeNetwork = async page => {
    const session = await context.newCDPSession(page);
    await session.send('Network.enable');
    await session.send('Network.setCacheDisabled', { cacheDisabled: true });
    session.on('Fetch.requestPaused', event => {
      const task = (async () => {
        const url = new URL(event.request.url);
        if (event.responseStatusCode !== undefined || event.responseErrorReason !== undefined) {
          assert.ok(loss && page === publicPage && url.origin === info.publicBase && url.pathname === '/shop/checkout/place' && event.request.method === 'POST', 'Unexpected native response-stage pause');
          await loss.onResponse(event, session); return;
        }
        if (![info.publicBase, info.adminBase].includes(url.origin) && !['blob:', 'data:'].includes(url.protocol)) { await session.send('Fetch.failRequest', { requestId: event.requestId, errorReason: 'BlockedByClient' }); return; }
        if (loss && page === publicPage && url.pathname === '/shop/checkout/place' && event.request.method === 'POST') loss.onRequest(event.request.postData);
        await session.send('Fetch.continueRequest', { requestId: event.requestId });
      })().catch(async error => { report.errors.push(safe(error.stack)); loss?.reject(error); await session.send('Fetch.failRequest', { requestId: event.requestId, errorReason: 'Aborted' }).catch(() => {}); });
      pending.push(task);
    });
    await session.send('Fetch.enable', { patterns: requestPatterns }); return session;
  };
  const layout = async (page, kind) => {
    const row = await page.evaluate(() => ({ width: innerWidth, scroll: document.documentElement.scrollWidth, fonts: [...document.querySelectorAll('input,button,select,textarea')].filter(x => x.getClientRects().length).map(x => ({ tag: x.tagName, font: parseFloat(getComputedStyle(x).fontSize) })) }));
    report.layouts.push({ kind, ...row }); assert.ok(row.scroll <= row.width + 1, 'Horizontal overflow: ' + kind); if (kind.startsWith('public')) assert.ok(row.fonts.every(x => x.font >= 16), 'Public control font must remain at least16px');
  };
  // Persistent inspector agent. Clear the exact current inspector sheet before
  // baseline capture; never compound inherited font sizes or alter layout CSS.
  const scale = async (kind) => {
    const frame = (await css.send('Page.getFrameTree')).frameTree.frame.id;
    const sheet = (await css.send('CSS.createStyleSheet', { frameId: frame, force: false })).styleSheetId;
    await css.send('CSS.setStyleSheetText', { styleSheetId: sheet, text: '' });
    const before = await publicPage.evaluate(() => {
      const nodes = [...document.querySelectorAll('a,button,input,select,textarea,label,p,h1,h2,h3,span,li,dt,dd')].filter(x => x.getClientRects().length && getComputedStyle(x).visibility !== 'hidden');
      return nodes.map((node, index) => { node.dataset.w09Text = String(index); const rect = node.getBoundingClientRect(); const range = document.createRange(); range.selectNodeContents(node); const r = range.getBoundingClientRect(); return { id: String(index), font: parseFloat(getComputedStyle(node).fontSize), width: rect.width, height: rect.height, textWidth: r.width, textHeight: r.height }; });
    });
    assert.ok(before.length > 0); await css.send('CSS.setStyleSheetText', { styleSheetId: sheet, text: before.map(x => '[data-w09-text="' + x.id + '"]{font-size:' + x.font * 2 + 'px!important}').join('\n') });
    const after = await publicPage.evaluate(() => [...document.querySelectorAll('[data-w09-text]')].map(node => { const range = document.createRange(); range.selectNodeContents(node); const rect = range.getBoundingClientRect(); return { id: node.dataset.w09Text, font: parseFloat(getComputedStyle(node).fontSize), textWidth: rect.width, textHeight: rect.height }; }));
    for (const original of before) assert.ok(Math.abs(after.find(x => x.id === original.id).font / original.font - 2) < .01, 'Actual font must grow2x');
    assert.ok(before.some(x => x.textWidth > 0 && (after.find(y => y.id === x.id).textWidth > x.textWidth * 1.4 || after.find(y => y.id === x.id).textHeight > x.textHeight * 1.4)), 'Actual Range text geometry must grow');
    await layout(publicPage, 'public-200%-' + kind); report.textScale.push({ kind, width: await publicPage.evaluate(() => innerWidth), before, after, actualOccurrences: before.length });
    if (kind === 'home') {
      const skip = publicPage.locator('.skip-link');
      const initial = await skip.boundingBox(); assert.ok(initial && initial.y + initial.height <= 0, 'Unfocused enlarged skip link must be fully offscreen');
      await publicPage.keyboard.press('Tab');
      assert.ok(await skip.evaluate(x => x === document.activeElement), 'Native Tab must focus skip link');
      const focused = await skip.boundingBox(), viewport = publicPage.viewportSize(); assert.ok(focused && focused.x >= 0 && focused.y >= 0 && focused.x + focused.width <= viewport.width + 1 && focused.y + focused.height <= viewport.height);
      await publicPage.keyboard.press('Enter'); assert.equal(await publicPage.evaluate(() => document.activeElement?.id), 'main'); assert.equal(new URL(publicPage.url()).hash, '#main');
      const hidden = await skip.boundingBox(); assert.ok(hidden && hidden.y + hidden.height <= 0); report.skip.push({ width: viewport.width, initial, focused, hidden, actualTab: true, actualMainFocus: true });
    }
    await screenshot(publicPage, 'text-' + kind + '-' + publicPage.viewportSize().width);
    await css.send('CSS.setStyleSheetText', { styleSheetId: sheet, text: '' });
    const restored = await publicPage.evaluate(() => [...document.querySelectorAll('[data-w09-text]')].map(x => ({ id: x.dataset.w09Text, font: parseFloat(getComputedStyle(x).fontSize) })));
    for (const original of before) assert.ok(Math.abs(restored.find(x => x.id === original.id).font - original.font) < .01, 'Original unscaled baseline must be restored');
  };
  try {
    assert.match(info.spkiPin, /^[A-Za-z0-9+/]{43}=$/);
    report.chrome = { executable: info.chromeExecutable, sha256Before: sha(await fs.readFile(info.chromeExecutable)) };
    browser = await chromium.launch({ executablePath: info.chromeExecutable, headless: true, args: ['--ignore-certificate-errors-spki-list=' + info.spkiPin] }); report.browserVersion = browser.version();
    context = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1.5, hasTouch: true, timezoneId: 'Asia/Bangkok', ignoreHTTPSErrors: false });
    context.on('request', request => { const item = { url: request.url(), method: request.method(), headers: null }; raw.push(item); pending.push(request.allHeaders().then(headers => { item.headers = headers; }).catch(error => { captureErrors.push(safe(error.message)); })); });
    context.on('response', response => {
      const u = new URL(response.url()); const allowed = u.origin === info.publicBase ? allowedPublic : u.origin === info.adminBase ? allowedAdmin : null;
      if (!allowed?.has(u.pathname)) return;
      pending.push((async () => { assert.equal(response.request().method(), 'GET'); assert.equal(response.status(), 200); const bytes = await response.body(); report.assets.push({ ...metadata(response.url()), method: 'GET', status: 200, bytes: bytes.length, sha256: sha(bytes) }); })().catch(error => { captureErrors.push(safe(error.message)); }));
    });
    context.on('requestfailed', request => report.failedRequests.push({ ...metadata(request.url()), method: request.method(), error: safe(request.failure()?.errorText || '') }));
    publicPage = await context.newPage(); adminPage = await context.newPage(); await adminPage.setExtraHTTPHeaders({ 'X-Web09-User': String(info.userId) });
    for (const page of [publicPage, adminPage]) page.on('pageerror', error => report.errors.push(safe(error.stack)));
    css = await nativeNetwork(publicPage); adminNetwork = await nativeNetwork(adminPage); await css.send('DOM.enable'); await css.send('CSS.enable');
    await step('Real operator activates closed COD-only fixture through current form, permission and CSRF', async () => {
      await go(adminPage, info.adminBase + '/admin/storefront/settings');
      for (const width of [320, 360, 390, 430, 768, 1440]) { await adminPage.setViewportSize({ width, height: 1000 }); await layout(adminPage, 'operator-closed'); }
      await adminPage.locator('#sales-control-reason').fill('WEB09 actual browser authorized prepublication fixture activation');
      const oldVersion = await adminPage.locator('#storefront-sales-control input[name=Version]').inputValue();
      const posted = adminPage.waitForResponse(r => new URL(r.url()).pathname.endsWith('/activate') && r.request().method() === 'POST');
      await drain(); await adminPage.locator('#storefront-sales-control button').click(); assert.equal((await posted).status(), 302);
      await adminPage.waitForFunction(version => document.querySelector('#storefront-sales-control input[name=Version]')?.value !== version, oldVersion);
      await adminPage.waitForLoadState('networkidle'); assert.match(await adminPage.locator('#storefront-intake-status').innerText(), /cho phép nhận đơn mới/i);
      const ready = await go(publicPage, info.publicBase + '/health/ready'); assert.equal(ready.status(), 200); assert.equal((await ready.json()).acceptingNewOrders, true);
    });
    await step('Actual release pages are responsive and keyboard/200% user-text skip remain usable', async () => {
      for (const width of [320, 360, 390, 430, 768, 1440]) {
        await publicPage.setViewportSize({ width, height: 1000 }); await go(publicPage, info.publicBase + '/'); await layout(publicPage, 'public-home');
        assert.equal(await publicPage.locator('.preview-banner').count(), 0); await screenshot(publicPage, 'home-' + width);
      }
      for (const width of [320, 390, 1440]) {
        await publicPage.setViewportSize({ width, height: 1000 });
        for (const [kind, route] of [['home', '/'], ['detail', '/shop/products/' + info.productId], ['cart', '/shop/cart']]) { await go(publicPage, info.publicBase + route); await scale(kind); }
      }
    });
    await step('Actual COD commit loses response then operator stop prevents new sales before exact replay', async () => {
      await publicPage.setViewportSize({ width: 390, height: 844 }); await go(publicPage, info.publicBase + '/shop/products/' + info.productId);
      const resolved = publicPage.waitForResponse(r => new URL(r.url()).pathname === '/shop/cart/resolve' && r.request().method() === 'POST');
      await publicPage.locator('#product-choice button[type=submit]').click(); assert.equal((await resolved).status(), 200);
      await publicPage.locator('#toast').waitFor({ state: 'visible' }); await go(publicPage, info.publicBase + '/shop/cart'); await scale('dynamic-cart');
      await go(publicPage, info.publicBase + '/shop/checkout'); await publicPage.locator('#recipient-name').fill('WEB09 khách fixture'); await publicPage.locator('#recipient-phone').fill('0901234567');
      await publicPage.locator('#recipient-province').selectOption('01'); await publicPage.locator('#recipient-ward option[value="01001"]').waitFor({ state: 'attached' });
      await publicPage.locator('#recipient-ward').selectOption('01001'); await publicPage.locator('#recipient-street').fill('WEB09 địa chỉ fixture không giao thật');
      await publicPage.locator('#checkout-quote').click(); await publicPage.waitForFunction(() => !document.querySelector('#checkout-confirm')?.disabled);
      let first = true, firstBody, secondBody, original;
      let commitResolve, commitReject; const committed = new Promise((resolve, reject) => { commitResolve = resolve; commitReject = reject; }); committed.catch(() => {});
      loss = { reject: commitReject, onRequest: body => { if (first) firstBody = body; else secondBody = body; }, onResponse: async (event, session) => {
        assert.equal(first, true); assert.equal(event.responseStatusCode, 200);
        firstBody ??= event.request.postData; assert.equal(typeof firstBody, 'string');
        const body = await session.send('Fetch.getResponseBody', { requestId: event.requestId });
        const bytes = Buffer.from(body.body, body.base64Encoded ? 'base64' : 'utf8'); original = JSON.parse(bytes.toString('utf8'));
        assert.equal(original.order.totalDue, 45000); secrets.push(original.order.guestLink.split('#')[1]); first = false;
        await session.send('Fetch.failRequest', { requestId: event.requestId, errorReason: 'Aborted' });
        await session.send('Fetch.enable', { patterns: requestPatterns });
        report.nativeResponseLoss = { transport: 'Same pinned Chromium response-stage interception', actualStatus: event.responseStatusCode,
          actualBodySHA256: sha(bytes), requestBodySHA256: sha(firstBody), failureAfterActualResponse: true, nodeAPIRequestUsed: false, fabricatedResponse: false };
        commitResolve();
      } };
      await css.send('Fetch.enable', { patterns: [{ urlPattern: info.publicBase + '/shop/checkout/place', requestStage: 'Response' }, ...requestPatterns] });
      await publicPage.locator('#checkout-confirm').click(); await committed;
      await fs.writeFile(path.join(info.output, 'checkpoint-order-committed'), 'Actual response200 fetched then deliberately aborted; no capability.');
      const deadline = Date.now() + 25000; while (Date.now() < deadline) { try { await fs.access(path.join(info.output, 'checkpoint-order-verified')); break; } catch { await new Promise(r => setTimeout(r, 100)); } }
      await fs.access(path.join(info.output, 'checkpoint-order-verified'));
      await adminPage.locator('#sales-control-reason').fill('WEB09 actual browser emergency stop after committed lost response');
      const beforeStopVersion = await adminPage.locator('#storefront-sales-control input[name=Version]').inputValue();
      await drain(); const stopped = adminPage.waitForResponse(r => new URL(r.url()).pathname.endsWith('/stop-new-orders') && r.request().method() === 'POST');
      await adminPage.locator('#storefront-sales-control button').click(); assert.equal((await stopped).status(), 302); await adminPage.waitForLoadState('networkidle');
      await adminPage.waitForFunction(version => document.querySelector('#storefront-sales-control input[name=Version]')?.value !== version, beforeStopVersion);
      assert.match(await adminPage.locator('#storefront-intake-status').innerText(), /dừng nhận đơn/i);
      const ready = await publicPage.evaluate(async () => { const response = await fetch('/health/ready', { cache: 'no-store', credentials: 'same-origin' }); return { status: response.status, value: await response.json() }; });
      assert.equal(ready.status, 200); assert.equal(ready.value.acceptingNewOrders, false);
      await drain(); await publicPage.reload({ waitUntil: 'networkidle', timeout: 30000 }); await publicPage.locator('#checkout-resume-retry').waitFor({ state: 'visible' });
      await publicPage.locator('#checkout-resume-retry').click(); await publicPage.locator('#checkout-resume-link').waitFor({ state: 'visible' });
      assert.equal(firstBody, secondBody); loss = null; await drain(); await publicPage.locator('#checkout-resume-link').click(); await publicPage.waitForURL('**/shop/orders#*'); assert.equal(new URL(publicPage.url()).hash.substring(1), secrets[0]);
      report.placement = { code: original.order.code, totalDue: original.order.totalDue, requestBodySHA256: sha(firstBody), identicalReplay: true, actualResponseAbortedAfterCommit: true };
    });
    await step('Paused existing customer reads/cancels actual order and no guest capability leaks through request metadata', async () => {
      await publicPage.locator('#guest-phase').waitFor({ state: 'visible' }); await publicPage.locator('#guest-cancellation-reason').fill('WEB09 actual cancellation while new intake remains paused');
      await publicPage.locator('#guest-cancellation-confirm').check(); await publicPage.locator('#guest-cancellation-submit').click();
      await publicPage.waitForFunction(() => document.querySelector('#guest-phase')?.textContent.includes('Đã hủy'));
      await drain(); assert.equal(report.errors.length, 0); assert.equal(report.failedRequests.length, 1, 'Only deliberate lost-response abort is expected');
      for (const request of raw) {
        const u = new URL(request.url); assert.ok([info.publicBase, info.adminBase].includes(u.origin), 'Unexpected network origin');
        for (const token of secrets) { assert.ok(!decode(request.url).includes(token)); for (const [name, value] of Object.entries(request.headers || {})) if (name.toLowerCase() !== 'cookie') assert.ok(!decode(value).includes(token), 'Capability in non-cookie header'); }
        const opaque = [...u.searchParams.entries()].filter(([, value]) => /^[A-Za-z0-9_-]{43}$/.test(value));
        if (opaque.length) { assert.equal(request.method, 'GET'); assert.ok(allowedPublic.has(u.pathname) && u.origin === info.publicBase); assert.deepEqual(opaque.map(x => x[0]), ['v']); assert.ok(report.assets.some(x => x.path === u.pathname && x.status === 200 && x.sha256)); }
        report.requests.push({ ...metadata(request.url), method: request.method });
      }
      await screenshot(publicPage, 'paused-cancelled-order'); await screenshot(adminPage, 'operator-stopped');
    });
  } catch (error) { report.passed = false; report.error = safe(error.stack); process.exitCode = 1; }
  finally {
    try { await drain(); } catch (error) { report.captureFailure = safe(error.stack); report.passed = false; process.exitCode = 1; }
    try { for (const session of [css, adminNetwork].filter(Boolean)) { await session.send('Fetch.disable'); await session.detach(); } if (context) await context.close(); if (browser) await browser.close(); } catch (error) { report.closeFailure = safe(error.stack); report.passed = false; process.exitCode = 1; }
    try { report.chrome.sha256After = sha(await fs.readFile(info.chromeExecutable)); assert.equal(report.chrome.sha256After, report.chrome.sha256Before); } catch (error) { report.chromeFailure = safe(error.stack); report.passed = false; process.exitCode = 1; }
    report.captureErrors = captureErrors; if (report.passed !== false) report.passed = report.results.length === 4 && report.results.every(x => x.passed);
    await fs.writeFile(path.join(info.output, 'browser-results.json'), JSON.stringify(report, null, 2));
  }
})().catch(error => { process.stderr.write(String(error.message).replace(/(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])/g, '[OPAQUE43]')); process.exitCode = 1; });
