'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
  const info = JSON.parse(await new Promise(resolve => { let data = ''; process.stdin.on('data', x => data += x); process.stdin.on('end', () => resolve(data)); }));
  fs.mkdirSync(info.output, { recursive: true });
  const results = [], measurements = [], timestamps = [], errors = [], failedRequests = [], requests = [];
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, timezoneId: 'Asia/Bangkok' });
  const page = await context.newPage();
  const safe = text => String(text).replace(/\/shop\/orders#[A-Za-z0-9_-]+/g, '/shop/orders#[REDACTED]').replace(/\b[A-Za-z0-9_-]{43}\b/g, '[REDACTED43]');
  page.on('pageerror', error => errors.push(safe(error.message)));
  page.on('requestfailed', request => failedRequests.push({ path: new URL(request.url()).pathname, reason: request.failure()?.errorText }));
  page.on('request', request => requests.push({ path: new URL(request.url()).pathname, method: request.method() }));
  const settle = async () => page.evaluate(async () => { await document.fonts.ready; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); });
  const test = async (name, body, independent = false) => { try { await body(); results.push({ name, passed: true }); } catch (error) { results.push({ name, passed: false, error: safe(error.stack) }); process.exitCode = 1; if (!independent) throw error; } };
  const goto = async route => { const response = await page.goto(info.baseUrl + route); assert.equal(response.status(), 200, safe(route)); await settle(); };
  const checkpoint = async (ready, acknowledged) => {
    fs.writeFileSync(path.join(info.output, ready), 'Owned browser checkpoint; no guest capability.');
    const end = Date.now() + 25000;
    while (!fs.existsSync(path.join(info.output, acknowledged))) { if (Date.now() > end) throw new Error('Fixture checkpoint timed out: ' + acknowledged); await new Promise(r => setTimeout(r, 50)); }
  };
  const screenshot = async label => page.screenshot({ path: path.join(info.output, label + '.png'), fullPage: true,
    mask: [page.locator('#guest-link-input,input[value*="/shop/orders#"],a[href*="/shop/orders#"]')], maskColor: '#222222' });
  async function add(product, quantity = '0.0001') {
    await goto('/shop/products/' + product.productId); await page.locator('#variant-choice').selectOption(String(product.variantId)); await page.locator('#unit-choice').selectOption(String(product.unitId));
    await page.locator('#product-quantity').fill(quantity); const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/cart/resolve');
    await page.locator('#product-choice button[type=submit]').click(); assert.equal((await response).status(), 200); await page.locator('#toast').waitFor({ state: 'visible' });
  }
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
  let originalQuote, replacement, committed, replayed;
  const placeBodies = [];
  try {
    await test('actual fractional cart estimate remains6 while checkout authority confirms POS5 plus shipping', async () => {
      await add(info.primary); await add(info.secondary); await goto('/shop/cart'); await page.locator('#cart-content').waitFor({ state: 'visible' }); assert.equal((await page.locator('#cart-estimate').innerText()).replace(/\s/g, ''), '6đ');
      await Promise.all([page.waitForNavigation(), page.locator('#continue-cart').click()]); await page.locator('#checkout-form').waitFor({ state: 'visible' });
    });
    await test('empty checkout form has real responsive labels controls and no overflow', async () => { await review('checkout-empty'); }, true);
    await test('keyboard reaches quote action with visible focus above fixed mobile navigation', async () => {
      await page.setViewportSize({ width: 320, height: 844 }); await page.reload(); await page.locator('#checkout-form').waitFor({ state: 'visible' });
      let reached = false; for (let i = 0; i < 50; i++) { await page.keyboard.press('Tab'); if (await page.locator('#checkout-quote').evaluate(e => e === document.activeElement)) { reached = true; break; } }
      const focus = await page.locator('#checkout-quote').evaluate(e => { const r = e.getBoundingClientRect(), nav = document.querySelector('.bottom-nav').getBoundingClientRect(); return { reached: e === document.activeElement, top: r.top, bottom: r.bottom, navTop: nav.top, outline: parseFloat(getComputedStyle(e).outlineWidth) }; });
      await screenshot('checkout-keyboard-focus-320'); measurements.push({ label: 'checkout-keyboard-focus', ...focus }); assert.ok(reached && focus.top >= 0 && focus.bottom <= focus.navTop && focus.outline >= 2, JSON.stringify(focus));
    }, true);
    await test('invalid mobile recipient input stays in form with an accessible error at its field', async () => {
      await page.setViewportSize({ width: 390, height: 844 }); await page.locator('[name="RecipientName"]').fill('Khách WEB04 <script>window.web04Injected=1</script>');
      await page.locator('[name="RecipientPhone"]').fill('abc'); await page.locator('[name="StreetAddress"]').fill('123 Đường fixture, không giao thật');
      await page.route('**/shop/checkout/wards?provinceCode=01', route => route.abort('failed'), { times: 1 }); await page.locator('[name="ProvinceCode"]').selectOption('01');
      await page.locator('#checkout-message[role="alert"]').waitFor({ state: 'visible' }); assert.equal(await page.locator('[name="WardCode"]').isDisabled(), true);
      assert.equal(await page.locator('[name="StreetAddress"]').inputValue(), '123 Đường fixture, không giao thật');
      await screenshot('checkout-ward-network-error'); await page.locator('[name="ProvinceCode"]').selectOption('02'); await page.locator('[name="WardCode"] option[value="02001"]').waitFor({ state: 'attached' });
      await page.locator('[name="ProvinceCode"]').selectOption('01'); await page.locator('[name="WardCode"] option[value="01001"]').waitFor({ state: 'attached' }); await page.locator('[name="WardCode"]').selectOption('01001');
      await page.locator('#checkout-quote').click();
      await page.waitForFunction(() => { const field = document.querySelector('[name="RecipientPhone"]'); return field.getAttribute('aria-invalid') === 'true' || !field.validity.valid; });
      await page.locator('#error-RecipientPhone').waitFor({ state: 'visible' }); assert.equal(await page.locator('[name="RecipientPhone"]').getAttribute('aria-describedby'), 'error-RecipientPhone');
      assert.equal(await page.locator('[name="RecipientPhone"]').evaluate(e => e === document.activeElement), true);
      assert.equal(await page.locator('[name="RecipientName"]').inputValue(), 'Khách WEB04 <script>window.web04Injected=1</script>'); assert.equal(await page.locator('[name="StreetAddress"]').inputValue(), '123 Đường fixture, không giao thật');
      assert.equal(await page.locator('[name="ProvinceCode"]').inputValue(), '01'); assert.equal(await page.locator('[name="WardCode"]').inputValue(), '01001'); await review('checkout-field-error');
    });
    await test('valid address selection shows human labels and authoritative whole-VND quote without losing fields', async () => {
      await page.locator('[name="RecipientPhone"]').fill('0901234567'); await page.locator('[name="Note"]').fill('WEB04 browser synthetic only');
      const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/checkout/quote'); await page.locator('#checkout-quote').click(); const received = await response; assert.equal(received.status(), 200); originalQuote = await received.json();
      assert.equal(originalQuote.goodsAmount, 5); assert.equal(originalQuote.shippingCharge, 20000); assert.equal(originalQuote.totalDue, 20005);
      assert.equal(originalQuote.recipient.provinceName, 'Tỉnh thử nghiệm A'); assert.equal(originalQuote.recipient.wardName, 'Phường thử nghiệm A1');
      assert.equal((await page.locator('#quote-total').innerText()).replace(/\s/g, ''), info.originalTotal.replace(/\s/g, '')); assert.equal(await page.locator('#checkout-confirm').isEnabled(), true); await review('checkout-authoritative-quote');
    });
    await checkpoint('checkpoint-price-ready', 'checkpoint-price-mutated');
    await test('changed server price returns replacement quote and requires a new explicit confirmation before any order or hold', async () => {
      const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/checkout/place'); await page.locator('#checkout-confirm').click(); const received = await response; assert.equal(received.status(), 200);
      placeBodies.push(received.request().postDataJSON()); const result = await received.json(); assert.ok(result.status === 1 || result.status === 'QuoteChanged'); assert.equal(result.order, null); replacement = result.replacementQuote;
      assert.notEqual(replacement.token, originalQuote.token); assert.equal(replacement.goodsAmount, 7); assert.equal(replacement.totalDue, 20007);
      await page.waitForFunction(expected => document.querySelector('#quote-total').textContent.replace(/\s/g, '') === expected.replace(/\s/g, ''), info.changedTotal);
      assert.equal(await page.locator('#checkout-confirm').isEnabled(), true); assert.equal(await page.locator('[name="RecipientPhone"]').inputValue(), '0901234567'); await review('checkout-requote');
      await checkpoint('checkpoint-requote-ready', 'checkpoint-requote-verified');
    });
    await test('lost response after committed COD order retains retry identity and form; retry returns exactly the committed private link', async () => {
      await page.route('**/shop/checkout/place', async route => {
        const response = await route.fetch(); assert.equal(response.status(), 200); const result = await response.json(); assert.ok(result.status === 0 || result.status === 'Placed'); committed = result.order;
        placeBodies.push(route.request().postDataJSON()); await route.abort('failed');
      }, { times: 1 });
      await page.locator('#checkout-confirm').click();
      const committedBy = Date.now() + 20000; while (!committed) { if (Date.now() > committedBy) throw new Error('Expected actual committed response before injected network failure.'); await new Promise(r => setTimeout(r, 50)); }
      await page.waitForFunction(() => document.querySelector('#checkout-confirm') && !document.querySelector('#checkout-confirm').disabled);
      await checkpoint('checkpoint-lost-ready', 'checkpoint-lost-verified'); assert.ok(committed); assert.equal(await page.locator('[name="RecipientPhone"]').inputValue(), '0901234567'); await review('checkout-lost-response');
      const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/checkout/place'); await page.locator('#checkout-confirm').click(); const received = await response; assert.equal(received.status(), 200); replayed = await received.json(); placeBodies.push(received.request().postDataJSON());
      assert.deepEqual(replayed.order, committed); assert.deepEqual(placeBodies[1], placeBodies[2]); assert.notEqual(placeBodies[0].quoteToken, placeBodies[1].quoteToken);
    });
    await test('private fragment link loads actual same-store snapshot without secret URL requests or script execution', async () => {
      assert.match(replayed.order.guestLink, /^\/shop\/orders#[A-Za-z0-9_-]{43}$/); const access = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/orders/access' && r.status() === 200);
      await goto(replayed.order.guestLink); const guestRaw = await (await access).json(); await page.locator('#guest-order').waitFor({ state: 'visible' });
      assert.match(await page.locator('#guest-order').innerText(), new RegExp(replayed.order.code)); assert.match((await page.locator('#guest-order').innerText()).replace(/\s/g, ''), /20\.007đ/);
      assert.match(await page.locator('#guest-order').innerText(), /Tỉnh thử nghiệm A/); assert.match(await page.locator('#guest-order').innerText(), /Phường thử nghiệm A1/);
      assert.equal(await page.evaluate(() => window.web04Injected), undefined); assert.equal(requests.some(r => r.path.includes(replayed.order.guestLink.split('#')[1])), false); await review('guest-order-snapshot');
      const expectedLocal = await page.evaluate(utc => new Date(utc).toLocaleString('vi-VN'), committed.createdAtUtc); const displayed = await page.locator('#guest-created').innerText();
      const stamp = { timezone: 'Asia/Bangkok', rawPlaceOutcomeCreatedAtUtc: committed.createdAtUtc, rawGuestAccessCreatedAtUtc: guestRaw.createdAtUtc, expectedLocalFromPlacedUtc: expectedLocal, displayed,
        utcSuffixPresent: /(?:Z|\+00:00)$/.test(guestRaw.createdAtUtc), displayMatchesPlacedUtc: displayed.includes(expectedLocal) };
      timestamps.push(stamp); assert.ok(stamp.utcSuffixPresent && stamp.displayMatchesPlacedUtc, JSON.stringify(stamp));
      await page.reload(); await page.locator('#guest-order').waitFor({ state: 'visible' }); assert.match(await page.locator('#guest-order').innerText(), new RegExp(replayed.order.code));
    }, true);
    await test('reduced motion and zero browser script errors remain true after placement and tracking', async () => {
      await page.emulateMedia({ reducedMotion: 'reduce' }); await settle(); assert.equal(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches), true);
      const moving = await page.evaluate(() => [...document.querySelectorAll('*')].filter(e => { const s = getComputedStyle(e); return s.animationName !== 'none' || s.transitionDuration.split(',').some(x => parseFloat(x) > 0); }).map(e => e.tagName)); assert.deepEqual(moving, []); assert.deepEqual(errors, []); await screenshot('guest-reduced-motion');
    }, true);
    await checkpoint('checkpoint-precision-ready', 'checkpoint-precision-mutated');
    await test('checkout uses exact server money text above2^53 and received order snapshot survives that live price change', async () => {
      await page.emulateMedia({ reducedMotion: 'no-preference' }); await goto('/shop/cart'); await page.locator('#cart-loading').waitFor({ state: 'hidden' });
      while (await page.locator('.cart-line').count()) { const count = await page.locator('.cart-line').count(); await page.locator('.cart-line .remove-line').first().click(); await page.waitForFunction(previous => document.querySelectorAll('.cart-line').length < previous, count); }
      await add(info.primary, '1'); await goto('/shop/checkout'); await page.locator('#checkout-form').waitFor({ state: 'visible' });
      await page.locator('[name="RecipientName"]').fill('Precision fixture'); await page.locator('[name="RecipientPhone"]').fill('0901234567'); await page.locator('[name="StreetAddress"]').fill('Precision synthetic street');
      await page.locator('[name="ProvinceCode"]').selectOption('01'); await page.locator('[name="WardCode"] option[value="01001"]').waitFor({ state: 'attached' }); await page.locator('[name="WardCode"]').selectOption('01001');
      const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/checkout/quote'); await page.locator('#checkout-quote').click(); const received = await response; assert.equal(received.status(), 200); const quote = await received.json();
      assert.equal(quote.lines[0].unitPriceText, info.preciseUnit); assert.equal(quote.totalDueText, info.preciseTotal);
      await page.waitForFunction(expected => document.querySelector('#quote-total').textContent.replace(/\s/g, '') === expected.replace(/\s/g, ''), info.preciseTotal);
      assert.match((await page.locator('#quote-lines').innerText()).replace(/\s/g, ''), /9\.007\.199\.254\.740\.993đ/); await review('checkout-exact-large-money', [320, 390, 1440]);
      await goto(replayed.order.guestLink); await page.locator('#guest-order').waitFor({ state: 'visible' }); assert.equal((await page.locator('#guest-total').innerText()).replace(/\s/g, ''), '20.007đ');
    });
  } catch (error) { console.error(safe(error.stack)); process.exitCode = 1; await screenshot('failure-last-state').catch(() => {}); }
  finally {
    fs.writeFileSync(path.join(info.output, 'browser-results.json'), JSON.stringify({ results, measurements, timestamps, errors, failedRequests, requests, placeAttempts: placeBodies.length,
      lostResponseIntentionallyInjected: !!committed, privateCapabilitiesRedacted: true, browserVersion: browser.version(), lastPath: new URL(page.url()).pathname }, null, 2));
    await context.close(); await browser.close();
  }
})().catch(error => { console.error(error.stack); process.exitCode = 1; });
