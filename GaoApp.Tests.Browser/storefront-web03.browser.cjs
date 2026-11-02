'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
  const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', x => text += x); process.stdin.on('end', () => resolve(text)); }));
  fs.mkdirSync(info.output, { recursive: true });
  const results = [], measurements = [], errors = [], failedRequests = [];
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, storageState: info.phase === 'initial' ? undefined : info.storageState });
  const page = await context.newPage();
  page.on('pageerror', e => errors.push(e.message));
  page.on('requestfailed', r => failedRequests.push({ url: r.url(), reason: r.failure()?.errorText }));
  const test = async (name, run, independent = false) => { try { await run(); results.push({ name, passed: true }); } catch (e) { results.push({ name, passed: false, error: e.stack }); if (!independent) throw e; console.error(name + ': ' + e.stack); process.exitCode = 1; } };
  const settle = async () => { await page.evaluate(async () => { await document.fonts.ready; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }); };
  const goto = async route => { const response = await page.goto(info.baseUrl + route); assert.equal(response.status(), 200, route); await settle(); };
  const amount = text => { const match = text.match(/([\d.,]+)\s*đ/u); assert.ok(match, 'A visible VND amount is required: ' + text); return Number(match[1].replaceAll('.', '').replace(',', '.')); };
  const line = product => page.locator(`.cart-line[data-variant-id="${product.variantId}"][data-unit-id="${product.unitId}"]`);
  const waitCart = async () => { await page.locator('#cart-loading').waitFor({ state: 'hidden' }); await page.locator('#cart-content').waitFor({ state: 'visible' }); };
  const respond = async action => { const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/cart/resolve' && r.request().method() === 'POST'); await action(); assert.equal((await response).status(), 200); await waitCart(); };
  async function add(product, quantity) {
    await goto('/shop/products/' + product.productId);
    await page.locator('#variant-choice').selectOption(String(product.variantId));
    await page.locator('#unit-choice').selectOption(String(product.unitId));
    await page.locator('#product-quantity').fill(String(quantity));
    const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/cart/resolve');
    await page.locator('#product-choice button[type=submit]').click(); assert.equal((await response).status(), 200);
    await page.locator('#toast').waitFor({ state: 'visible' });
  }
  async function review(label, sizes = [320, 360, 390, 430, 1440]) {
    const failures = [];
    for (const width of sizes) {
      await page.setViewportSize({ width, height: width === 1440 ? 1000 : 844 }); await settle();
      const metrics = await page.evaluate(() => {
        const visible = e => { const r = e.getBoundingClientRect(), s = getComputedStyle(e); return r.width > 1 && r.height > 1 && r.bottom > 0 && s.visibility !== 'hidden' && !e.closest('[hidden],.sr-only'); };
        const color = s => { const m = s.match(/[\d.]+/g); return m ? m.map(Number) : [0, 0, 0, 0]; };
        const blend = (fg, bg) => { const a = fg.length > 3 ? fg[3] : 1; return fg.slice(0, 3).map((v, i) => v * a + bg[i] * (1 - a)); };
        const background = e => { let layers = []; for (let n = e; n; n = n.parentElement) { const c = color(getComputedStyle(n).backgroundColor); layers.push(c); if ((c[3] ?? 1) === 1) break; } let bg = [255, 255, 255]; for (const layer of layers.reverse()) bg = blend(layer, bg); return bg; };
        const luminance = c => c.map(v => v / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((v, c, i) => v + c * [.2126, .7152, .0722][i], 0);
        const text = [...document.querySelectorAll('body *')].filter(e => visible(e) && !e.closest('button:disabled,option,script,style,svg') && [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim()));
        const contrast = text.map(e => { const s = getComputedStyle(e), bg = background(e), fg = blend(color(s.color), bg); const a = luminance(fg), b = luminance(bg); const ratio = (Math.max(a, b) + .05) / (Math.min(a, b) + .05); const large = parseFloat(s.fontSize) >= 24 || parseFloat(s.fontSize) >= 18.66 && parseInt(s.fontWeight, 10) >= 700; return { tag: e.tagName, text: e.textContent.trim().slice(0, 70), ratio, threshold: large ? 3 : 4.5, fontSize: s.fontSize, color: s.color, background: bg }; });
        const controls = [...document.querySelectorAll('button,input:not([type=hidden]),select,.button,.icon-button,.bottom-nav a')].filter(visible).map(e => { const r = e.getBoundingClientRect(); return { label: e.getAttribute('aria-label') || e.textContent.trim().slice(0, 60) || e.id, width: r.width, height: r.height }; });
        return { width: innerWidth, scrollWidth: document.documentElement.scrollWidth, bodyFont: parseFloat(getComputedStyle(document.body).fontSize), fontFamily: getComputedStyle(document.body).fontFamily, loadedInter: [...document.fonts].some(f => f.family === 'Inter' && f.status === 'loaded'),
          adminShell: !!document.querySelector('#layout-menu,[data-mobile-shell-owner],.gao-mobile-pins'),
          smallControls: controls.filter(x => x.width < 43.9 || x.height < 43.9), contrastFailures: contrast.filter(x => x.ratio + .02 < x.threshold), contrastSamples: contrast.length,
          minimumContrast: Math.min(...contrast.map(x => x.ratio)), loadedImages: [...document.images].filter(x => x.complete && x.naturalWidth > 0).length };
      });
      measurements.push({ label, ...metrics });
      if (metrics.scrollWidth > width + 1) failures.push(`${label} ${width}: overflow ${metrics.scrollWidth}`);
      if (metrics.bodyFont < 15.9) failures.push(`${label} ${width}: body font ${metrics.bodyFont}`);
      if (!metrics.loadedInter) failures.push(`${label} ${width}: Inter did not load from real local assets`);
      if (metrics.adminShell) failures.push(`${label}: admin layout appeared`);
      if (metrics.smallControls.length) failures.push(`${label} ${width}: controls below44 ${JSON.stringify(metrics.smallControls)}`);
      if (metrics.contrastFailures.length) failures.push(`${label} ${width}: text contrast ${JSON.stringify(metrics.contrastFailures)}`);
      await page.screenshot({ path: path.join(info.output, `${label}-${width}.png`), fullPage: true });
    }
    assert.deepEqual(failures, []);
  }
  try {
    if (info.phase === 'initial') {
      await test('five real destinations at five responsive widths with DOM, contrast and touch measurements', async () => {
        for (const [label, route] of [['home', '/shop'], ['products', '/shop/products'], ['detail', '/shop/products/' + info.primary.productId], ['cart-empty', '/shop/cart'], ['orders', '/shop/orders']]) {
          await goto(route); if (label === 'cart-empty') await page.locator('#cart-empty').waitFor({ state: 'visible' }); await review(label);
        }
      }, true);
      await test('four navigation destinations and keyboard-visible focus', async () => {
        await goto('/shop'); assert.deepEqual(await page.locator('.bottom-nav a').evaluateAll(a => a.map(x => new URL(x.href).pathname)), ['/shop', '/shop/products', '/shop/cart', '/shop/orders']);
        await page.setViewportSize({ width: 320, height: 844 }); await page.keyboard.press('Tab');
        const focus = await page.evaluate(() => { const e = document.activeElement, r = e.getBoundingClientRect(), s = getComputedStyle(e); return { tag: e.tagName, text: e.textContent, outline: parseFloat(s.outlineWidth), x: r.x, y: r.y, right: r.right }; });
        assert.match(focus.text, /nội dung chính/); assert.ok(focus.outline >= 2 && focus.x >= 0 && focus.y >= 0 && focus.right <= 320);
        await page.screenshot({ path: path.join(info.output, 'keyboard-focus-320.png') }); await page.keyboard.press('Enter'); assert.equal(await page.evaluate(() => document.activeElement.id), 'main');
      }, true);
      await test('diacritic-free search, group, name sort and pagination use actual server data', async () => {
        await goto('/shop/products'); await page.locator('#catalog-search').fill('binh giu nhiet'); await Promise.all([page.waitForNavigation(), page.locator('.catalog-filters button').click()]);
        assert.equal(await page.locator('.product-card').count(), 1); assert.equal(await page.locator('.product-card').getAttribute('data-product-id'), String(info.primary.productId));
        await goto('/shop/products'); await page.locator('#catalog-group').selectOption('Điện gia dụng'); await Promise.all([page.waitForNavigation(), page.locator('.catalog-filters button').click()]); assert.equal(await page.locator('.product-card').count(), 1);
        await goto('/shop/products?Search=WEB03%20Z&Sort=NameDesc&PageSize=3'); const first = await page.locator('.product-card').evaluateAll(a => a.map(x => Number(x.dataset.productId))); assert.equal(first.length, 3);
        const names = await page.locator('.product-card h3').allTextContents(); assert.match(names[0], /Z25/); assert.match(names[2], /Z23/);
        await Promise.all([page.waitForNavigation(), page.getByRole('link', { name: 'Trang sau', exact: true }).click()]); const next = await page.locator('.product-card').evaluateAll(a => a.map(x => Number(x.dataset.productId))); assert.equal(next.length, 3); assert.ok(next.every(x => !first.includes(x)));
        const url = new URL(page.url()); assert.equal(url.searchParams.get('Sort'), 'NameDesc'); assert.equal(url.searchParams.get('Search'), 'WEB03 Z');
        await goto('/shop/products?Search=NO_MATCH_WEB03'); assert.equal(await page.locator('.product-card').count(), 0); await review('search-empty', [320, 390]);
      }, true);
      await test('reduced motion is honored by actual CSS', async () => {
        await page.emulateMedia({ reducedMotion: 'reduce' }); await goto('/shop');
        assert.equal(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches), true);
        const moving = await page.evaluate(() => [...document.querySelectorAll('*')].filter(e => { const s = getComputedStyle(e); return s.animationName !== 'none' || s.transitionDuration.split(',').some(v => parseFloat(v) > 0); }).map(e => e.tagName + '.' + e.className)); assert.deepEqual(moving, []);
        await page.screenshot({ path: path.join(info.output, 'reduced-motion-390.png'), fullPage: true }); await page.emulateMedia({ reducedMotion: 'no-preference' });
      }, true);
      await test('keyboard can reach detail purchase with visible focus above fixed navigation', async () => {
        await goto('/shop/products/' + info.primary.productId); await page.setViewportSize({ width: 320, height: 844 });
        let reached = false;
        for (let i = 0; i < 40; i++) { await page.keyboard.press('Tab'); if (await page.locator('#product-choice button[type=submit]').evaluate(e => e === document.activeElement)) { reached = true; break; } }
        assert.ok(reached); const focus = await page.locator('#product-choice button[type=submit]').evaluate(e => { const r = e.getBoundingClientRect(), nav = document.querySelector('.bottom-nav').getBoundingClientRect(); return { top: r.top, bottom: r.bottom, navTop: nav.top, outline: parseFloat(getComputedStyle(e).outlineWidth) }; });
        await page.screenshot({ path: path.join(info.output, 'detail-keyboard-purchase-320.png') }); assert.ok(focus.top >= 0 && focus.bottom <= focus.navTop && focus.outline >= 2, JSON.stringify(focus));
      }, true);
      await test('fractional VND rounds each real line then sums (3 plus3 equals6)', async () => {
        await add(info.rounding[0], .0001); await add(info.rounding[1], .0001); await goto('/shop/cart'); await waitCart();
        assert.equal(amount(await page.locator('#cart-estimate').innerText()), 6); const rows = await page.locator('.line-price').allTextContents(); assert.equal(rows.length, 2); rows.forEach(x => assert.equal(amount(x), 3));
        await page.screenshot({ path: path.join(info.output, 'vnd-per-line-rounding.png'), fullPage: true });
        await respond(() => page.locator('.remove-line').first().click()); await page.locator('.remove-line').first().click(); await page.locator('#cart-empty').waitFor({ state: 'visible' });
      });
      await test('pack-only choice, actual second variant, negative stock and quantity error', async () => {
        await goto('/shop/products/' + info.primary.productId); assert.equal(await page.locator('#unit-choice option:not([disabled])').count(), 1); assert.equal(await page.locator('#unit-choice').inputValue(), String(info.primary.unitId));
        assert.equal(amount(await page.locator('#detail-price').innerText()), 180000); assert.equal(await page.locator('#product-choice button[type=submit]').isEnabled(), true);
        await page.locator('#variant-choice').selectOption(String(info.alternative.variantId)); assert.equal(await page.locator('#unit-choice').inputValue(), String(info.alternative.unitId)); assert.equal(amount(await page.locator('#detail-price').innerText()), 25000);
        await page.locator('#variant-choice').selectOption(String(info.primary.variantId)); await page.locator('#product-quantity').fill('1.23456'); await page.locator('#product-choice button[type=submit]').click();
        assert.equal(await page.locator('#product-quantity').evaluate(e => e.validity.valid), false); assert.ok(await page.locator('#product-quantity').evaluate(e => e.validationMessage.length > 0));
        await page.screenshot({ path: path.join(info.output, 'quantity-error-390.png'), fullPage: true });
      });
      await test('server-formatted money keeps exact digits above2pow53 in detail, line and total', async () => {
        await add(info.precision, 1); assert.equal((await page.locator('#detail-price').innerText()).split('đ')[0].replace(/\D/g, ''), '9007199254740993');
        await goto('/shop/cart'); await waitCart(); assert.equal((await line(info.precision).locator('.line-price').innerText()).replace(/\D/g, ''), '9007199254740993'); assert.equal((await page.locator('#cart-estimate').innerText()).replace(/\D/g, ''), '9007199254740993');
        await page.screenshot({ path: path.join(info.output, 'exact-large-money.png'), fullPage: true }); await line(info.precision).locator('.remove-line').click(); await page.locator('#cart-empty').waitFor({ state: 'visible' });
      });
      await test('guest cart persists exact four-decimal pack selection and repeated add', async () => {
        await add(info.primary, 1.2345); await add(info.primary, .0001); await goto('/shop/cart'); await waitCart();
        assert.equal(await line(info.primary).locator('input').inputValue(), '1.2346'); await page.reload(); await waitCart(); assert.equal(await line(info.primary).locator('input').inputValue(), '1.2346');
        assert.equal(amount(await line(info.primary).locator('.line-price').innerText()), 222228);
      });
      await test('cart plus, minus, direct edit, variant-unit replacement and delete are real UI operations', async () => {
        await respond(() => line(info.primary).getByRole('button', { name: 'Tăng số lượng', exact: true }).click()); assert.equal(await line(info.primary).locator('input').inputValue(), '2.2346');
        await respond(() => line(info.primary).getByRole('button', { name: 'Giảm số lượng', exact: true }).click()); assert.equal(await line(info.primary).locator('input').inputValue(), '1.2346');
        await line(info.primary).locator('input').fill('1.2345'); await respond(() => line(info.primary).locator('input').press('Tab'));
        await Promise.all([page.waitForNavigation(), line(info.primary).getByRole('link', { name: 'Đổi biến thể / đơn vị' }).click()]);
        await page.locator('#variant-choice').selectOption(String(info.alternative.variantId)); await Promise.all([page.waitForNavigation(), page.locator('#product-choice button[type=submit]').click()]); await waitCart(); assert.equal(await line(info.primary).count(), 0); assert.equal(await line(info.alternative).count(), 1);
        await Promise.all([page.waitForNavigation(), line(info.alternative).getByRole('link', { name: 'Đổi biến thể / đơn vị' }).click()]); await page.locator('#variant-choice').selectOption(String(info.primary.variantId));
        await Promise.all([page.waitForNavigation(), page.locator('#product-choice button[type=submit]').click()]); await waitCart(); assert.equal(await line(info.primary).count(), 1); assert.equal(await line(info.alternative).count(), 0);
        await add(info.secondary, 1); await goto('/shop/cart'); await waitCart(); await respond(() => line(info.secondary).locator('.remove-line').click()); assert.equal(await line(info.secondary).count(), 0);
        await add(info.secondary, 2); await goto('/shop/cart'); await waitCart();
      });
      await test('populated cart responsive measurements preserve established functional state', async () => { assert.equal(await page.locator('.cart-line').count(), 2); await review('cart-populated'); }, true);
      await test('malformed localStorage recovers and client labels-prices never override current server truth', async () => {
        const saved = await page.evaluate(() => { const key = Object.keys(localStorage).find(k => k.endsWith(document.body.dataset.cartScope)); return { key, value: localStorage.getItem(key) }; }); assert.ok(saved.key);
        await page.evaluate(key => localStorage.setItem(key, 'not-json'), saved.key); await page.reload(); await page.locator('#cart-empty').waitFor({ state: 'visible' });
        await page.evaluate(saved => { const data = JSON.parse(saved.value); data.items.forEach(x => { x.unitPrice = 0; x.productName = '<script>FORGED_UI_PRICE</script>'; }); localStorage.setItem(saved.key, JSON.stringify(data)); }, saved);
        await page.reload(); await waitCart(); assert.equal(amount(await line(info.primary).locator('.line-price').innerText()), 222210); assert.doesNotMatch(await page.locator('#cart-lines').innerText(), /FORGED_UI_PRICE/);
        fs.writeFileSync(path.join(info.output, 'cart-scope.json'), JSON.stringify({ scope: await page.locator('body').getAttribute('data-cart-scope'), key: saved.key }));
      });
      await test('int32-overflow localStorage IDs do not trap the guest in a400 retry loop', async () => {
        const saved = await page.evaluate(() => { const key = Object.keys(localStorage).find(k => k.endsWith(document.body.dataset.cartScope)); return { key, value: localStorage.getItem(key) }; });
        await page.evaluate(saved => { const data = JSON.parse(saved.value); data.items.push({ productId: 2147483648, variantId: 2147483648, unitId: 2147483648, quantity: 1 }); localStorage.setItem(saved.key, JSON.stringify(data)); }, saved);
        await page.reload(); await waitCart(); assert.equal(await page.locator('#cart-error').isVisible(), false);
        assert.equal(await line(info.primary).count(), 1); assert.equal(await line(info.secondary).count(), 1); assert.equal(await page.locator('.cart-line').count(), 2);
        await page.locator('#toast').waitFor({ state: 'visible' }); assert.match(await page.locator('#toast').innerText(), /giỏ|lựa chọn|bỏ|hợp lệ/i);
        await add(info.rounding[0], 1); await goto('/shop/cart'); await waitCart(); assert.equal(await line(info.rounding[0]).count(), 1);
        await page.evaluate(saved => localStorage.setItem(saved.key, saved.value), saved); await page.reload(); await waitCart();
      });
      await test('pending detail add never overwrites another tab new item or resurrects its removed item', async () => {
        await goto('/shop/products/' + info.primary.productId); await page.locator('#product-quantity').fill('0.5');
        let release, arrived; const hold = new Promise(r => { release = r; }); const intercepted = new Promise(r => { arrived = r; });
        await page.route('**/shop/cart/resolve', async route => { arrived(); await hold; await route.continue(); });
        await page.locator('#product-choice button[type=submit]').click();
        await Promise.race([intercepted, new Promise((_, reject) => setTimeout(() => reject(new Error('Real cart request was not held')), 10000))]);
        const tab = await context.newPage(); tab.on('pageerror', e => errors.push(e.message));
        try {
          await tab.goto(info.baseUrl + '/shop/cart'); await tab.locator('#cart-content').waitFor({ state: 'visible' });
          const deleted = tab.locator(`.cart-line[data-variant-id="${info.secondary.variantId}"][data-unit-id="${info.secondary.unitId}"]`);
          await Promise.all([tab.waitForResponse(r => new URL(r.url()).pathname === '/shop/cart/resolve'), deleted.locator('.remove-line').click()]); await deleted.waitFor({ state: 'detached' });
          await tab.goto(info.baseUrl + '/shop/products/' + info.rounding[0].productId); await tab.locator('#product-quantity').fill('1');
          await Promise.all([tab.waitForResponse(r => new URL(r.url()).pathname === '/shop/cart/resolve'), tab.locator('#product-choice button[type=submit]').click()]); await tab.locator('#toast').waitFor({ state: 'visible' });
          const response = page.waitForResponse(r => new URL(r.url()).pathname === '/shop/cart/resolve'); release(); assert.equal((await response).status(), 200);
          await page.waitForFunction(() => !document.getElementById('product-choice').hasAttribute('aria-busy'));
          const stored = await page.evaluate(() => { const key = Object.keys(localStorage).find(k => k.endsWith(document.body.dataset.cartScope)); return JSON.parse(localStorage.getItem(key)).items; });
          assert.ok(stored.some(x => x.variantId === info.rounding[0].variantId)); assert.ok(!stored.some(x => x.variantId === info.secondary.variantId));
          assert.equal(stored.find(x => x.variantId === info.primary.variantId).quantity, 1.2345);
          await page.locator('#choice-message').waitFor({ state: 'visible' }); assert.match(await page.locator('#choice-message').innerText(), /thay đổi|thử lại|kiểm tra lại/i);
        } finally { release(); await page.unroute('**/shop/cart/resolve'); await tab.close(); }
        await goto('/shop/cart'); await waitCart(); assert.equal(await line(info.primary).locator('input').inputValue(), '1.2345');
        await respond(() => line(info.rounding[0]).locator('.remove-line').click()); await add(info.secondary, 2); await goto('/shop/cart'); await waitCart();
      });
      await test('real network failure, retry and checkout boundary remain usable', async () => {
        await context.setOffline(true); await page.locator('#refresh-cart').click(); await page.locator('#cart-error').waitFor({ state: 'visible' }); await page.screenshot({ path: path.join(info.output, 'cart-network-error.png'), fullPage: true });
        await context.setOffline(false); await respond(() => page.locator('#cart-retry').click());
        await Promise.all([page.waitForNavigation(), page.locator('#continue-cart').click()]); await page.locator('#checkout-form').waitFor({ state: 'visible' });
        assert.equal(await page.locator('#checkout-form [name="RecipientName"]').count(), 1); assert.equal(await page.locator('#checkout-quote').count(), 1);
        // WEB04 now owns authoritative quote/placement. Merely opening its
        // actual form must still create no order, hold, payment or quote.
        await goto('/shop/cart'); await waitCart();
      });
      await context.storageState({ path: info.storageState });
    } else if (info.phase === 'revalidate') {
      await test('persisted old cart immediately uses changed price and rejects newly hidden selection', async () => {
        await goto('/shop/cart'); await waitCart(); assert.equal(amount(await line(info.primary).locator('.line-price').innerText()), 234555);
        assert.equal(await line(info.secondary).count(), 1); assert.match(await line(info.secondary).innerText(), /không còn|đã ẩn/); assert.equal(await page.locator('#continue-cart').getAttribute('aria-disabled'), 'true'); assert.equal(await page.locator('#continue-cart').getAttribute('href'), null);
        await review('cart-revalidated'); await respond(() => page.locator('#refresh-cart').click());
        const direct = await page.goto(info.baseUrl + '/shop/products/' + info.secondary.productId); assert.equal(direct.status(), 404); await page.screenshot({ path: path.join(info.output, 'hidden-detail-404.png'), fullPage: true });
        await goto('/shop/products'); assert.equal(await page.locator(`.product-card[data-product-id="${info.secondary.productId}"]`).count(), 0);
      });
      await context.storageState({ path: info.storageState });
    } else if (info.phase === 'scope') {
      await test('same-origin storefront remapping does not consume another store localStorage cart', async () => {
        await goto('/shop/cart'); await page.locator('#cart-empty').waitFor({ state: 'visible' }); assert.equal(await page.locator('.cart-line').count(), 0);
        const scope = await page.locator('body').getAttribute('data-cart-scope'); assert.notEqual(scope, info.previousScope);
        assert.equal(await page.evaluate(key => localStorage.getItem(key) !== null, info.previousKey), true); await page.screenshot({ path: path.join(info.output, 'new-store-cart-scope.png'), fullPage: true });
      });
    } else throw new Error('Unknown independent browser phase');
    await test('no unhandled browser JavaScript errors', async () => assert.deepEqual(errors, []));
  } finally {
    fs.writeFileSync(path.join(info.output, 'browser-results.json'), JSON.stringify({ phase: info.phase, browser: browser.version(), results, measurements, errors, failedRequests }, null, 2));
    await browser.close();
  }
})().catch(error => { console.error(error.stack); process.exitCode = 1; });
