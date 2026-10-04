const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => resolve(s)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1280, height: 800 } }), page = await context.newPage(), errors = [];
    page.on('pageerror', e => errors.push(e.message));
    const output = path.resolve('TestResults/kiosk'); fs.mkdirSync(output, { recursive: true });
    const action = name => page.locator(`[data-action="${name}"]`).first();
    const ready = () => page.waitForFunction(() => document.getElementById('gao-kiosk').getAttribute('aria-busy') !== 'true');
    const shot = async name => { await page.screenshot({ path: path.join(output, name + '.png'), fullPage: true }); assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), name + ' must fit viewport'); };
    try {
        await page.goto(info.baseUrl + '/kiosk'); await page.locator('#g-key').fill(info.key);
        await action('activate').click(); await page.locator('.g-idle').waitFor();
        assert.match(await page.locator('.g-idle').innerText(), /Gao Mart|GAO MART/); await shot('idle');
        await action('menu').click(); await ready(); assert.equal(await page.locator('.g-tile').count(), 3); await shot('menu-1280');
        await page.setViewportSize({ width: 1024, height: 768 }); await shot('menu-1024'); assert.ok(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight + 1), 'Menu must fit the screen without scrolling');
        await action('product').click(); await ready(); await page.locator('#g-query').fill('nuoc suoi');
        await action('choose-0').click(); await ready(); assert.equal(await page.locator('.g-unit-price').count(), 2);
        assert.match(await page.locator('.g-unit-prices').innerText(), /100\.000/); await shot('product');
        assert.ok(await page.locator('.g-product-no-image').isVisible(), 'Missing photos have a clear fallback');
        // Fixture covers the three-unit, portrait-image layout; no catalog data is changed.
        const sample = { variantId: 1, displayName: 'Sữa tươi TH chocolate hộp 110ml', barcode: '8935217400461', brand: 'TH true MILK', baseUnit: 'Hộp', price: 6500, description: '', imageUrl: '/kiosk/test-product.svg', units: [
            { id: 1, name: 'Hộp', factor: 1, price: 6500, barcodes: ['8935217400461'] },
            { id: 2, name: 'Lốc', factor: 4, price: 25000, barcodes: [] },
            { id: 3, name: 'Thùng', factor: 48, price: 292000, barcodes: [] }
        ] };
        const sampleRoute = route => route.fulfill({ json: [sample] });
        await page.route('**/kiosk/api/products?*', sampleRoute);
        await page.route('**/kiosk/test-product.svg', route => route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="360" height="480" viewBox="0 0 360 480"><defs><linearGradient id="paper"><stop stop-color="#f7f5ed"/><stop offset="1" stop-color="#d6d6cf"/></linearGradient></defs><ellipse cx="181" cy="450" rx="109" ry="15" fill="#23364b" opacity=".09"/><path d="M82 80 135 35h155v370l-58 43H82Z" fill="url(#paper)" stroke="#dadbd9"/><path d="m82 80 150 1 58-46H135Z" fill="#fdfcf7"/><path d="m232 81 58-46v370l-58 43Z" fill="#dadbd4"/><path d="M82 262h150v186H82Z" fill="#604634"/><text x="157" y="155" font-family="Georgia" text-anchor="middle" fill="#223e67" font-size="65">TH</text><text x="157" y="191" font-family="Georgia" text-anchor="middle" fill="#223e67" font-size="25">true MILK</text><text x="157" y="228" font-family="Arial" text-anchor="middle" fill="#625b4e" font-size="13">SỮA TƯƠI</text><text x="157" y="310" font-family="Arial" text-anchor="middle" fill="#fff" font-size="15">CHOCOLATE</text><text x="157" y="402" font-family="Arial" text-anchor="middle" fill="#eee1cc" font-size="17">110 ml</text></svg>' }));
        await page.locator('#g-query').fill('sua tuoi'); await action('choose-0').click(); await ready();
        await page.locator('.g-product-photo img').evaluate(img => img.decode());
        assert.equal(await page.locator('.g-unit-price').count(), 3);
        assert.match(await page.locator('.g-unit-prices').innerText(), /1 Lốc = 4 Hộp/);
        assert.match(await page.locator('.g-unit-prices').innerText(), /1 Thùng = 48 Hộp/);
        const fits = () => page.evaluate(() => document.documentElement.scrollHeight <= innerHeight + 1);
        await shot('product-1024'); assert.ok(await fits(), 'Three unit prices and product photo fit 1024×768');
        await action('product-image').click(); await page.locator('#g-dialog[open]').waitFor(); await shot('product-image-zoom');
        assert.ok(await page.locator('.g-product-enlarged').isVisible());
        await page.keyboard.press('Escape'); assert.equal(await page.locator('#g-dialog[open]').count(), 0);
        await action('product-image').click(); await action('close-dialog').click();
        await page.setViewportSize({ width: 1280, height: 800 }); await shot('product-1280'); assert.ok(await fits());
        await page.setViewportSize({ width: 800, height: 1280 }); await shot('product-portrait');
        await page.setViewportSize({ width: 390, height: 844 }); await shot('product-narrow');
        await page.setViewportSize({ width: 1024, height: 768 });
        // Scanner still replaces the product after closing the photo dialog.
        await page.keyboard.type(sample.barcode + '\n', { delay: 3 });
        await page.waitForFunction(() => document.querySelector('#g-query')?.value === ''); await ready();
        sample.displayName = 'Sản phẩm có tên rất dài để kiểm tra bố cục khi nhiều đơn vị bán cùng hiển thị';
        sample.description = 'Thông tin sản phẩm dài vẫn được đọc đầy đủ, không bị cắt mất nội dung.';
        sample.imageUrl = '/kiosk/missing-test-product.png';
        sample.units.push({ id: 4, name: 'Kiện đóng gói lớn', factor: 96, price: 584000, barcodes: [] });
        await page.route('**/kiosk/missing-test-product.png', route => route.fulfill({ status: 404, body: '' }));
        await page.locator('#g-query').fill('ten dai'); await action('choose-0').click(); await ready();
        await page.locator('.g-product-no-image:not([hidden])').waitFor(); await shot('product-many-units');
        assert.equal(await page.locator('.g-unit-price').count(), 4);
        assert.ok(await page.locator('.g-unit-price').last().isVisible());
        await page.unroute('**/kiosk/api/products?*', sampleRoute);
        await action('home').click(); await ready(); await action('login').click(); await ready();
        for (const digit of info.customerPhone) await action('key-' + digit).click();
        await action('lookup').click(); await page.locator('.g-stats').waitFor(); await ready();
        assert.match(await page.locator('.g-stat').first().innerText(), /11/); await shot('customer');
        await action('tab-points').click(); await ready(); assert.equal(await page.locator('.g-history').count(), 20);
        await action('next').click(); await ready(); assert.equal(await page.locator('.g-history').count(), 3);
        await action('tab-vouchers').click(); await ready(); assert.match(await page.locator('main').innerText(), /Đã sử dụng/);
        await page.locator('[data-action^="voucher-"]').first().click(); await page.locator('#g-dialog[open]').waitFor(); await ready();
        assert.equal(await page.locator('.g-product-image-dialog').count(), 0, 'Photo dialog styles do not leak into other dialogs');
        await action('close-dialog').click();
        await action('end-customer').click(); await page.locator('.g-idle').waitFor(); await ready();
        const privateRead = await page.evaluate(async () => (await fetch('/kiosk/api/customer/history', { method: 'POST', headers: { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('[name=__RequestVerificationToken]').value }, body: JSON.stringify({ tab: 'points' }) })).status);
        assert.equal(privateRead, 403);
        await action('menu').click(); await ready(); await action('shop').click(); await ready(); await page.locator('.g-shop').waitFor();
        for (let i = 0; i < 4; i++) await page.keyboard.type(info.barcode + '\n', { delay: 3 });
        await page.waitForFunction(() => document.querySelector('.g-cart-row .g-qty-value')?.textContent === '4'); await ready();
        await page.locator('#g-query').focus();
        for (let i = 0; i < 3; i++) await page.keyboard.type(info.barcode + '\n', { delay: 5 });
        await page.waitForFunction(() => document.querySelector('.g-qty-value')?.textContent === '7'); await ready();
        await page.locator('.g-qty-value').click(); await action('qtykey-Xóa').click(); await action('qtykey-2').click(); await action('qtykey-4').click();
        await page.locator('[data-action^="saveqty-"]').click(); await ready();
        await page.locator('.g-cart-remove').click(); await ready();
        assert.equal(await page.locator('.g-cart-row').count(), 0); assert.ok(await action('checkout').isDisabled());
        await page.reload(); await page.locator('.g-cart-empty').waitFor();
        await page.keyboard.type(info.barcode + '\n', { delay: 3 });
        await page.locator('.g-qty-value').waitFor(); await ready();
        await page.locator('.g-qty-value').click(); await action('qtykey-Xóa').click(); await action('qtykey-2').click(); await action('qtykey-4').click();
        await page.locator('[data-action^="saveqty-"]').click(); await ready();
        const adjusted = { body: await page.evaluate(async () => (await fetch('/kiosk/api/state')).json()) };
        assert.equal(adjusted.body.order.grandTotal, 100000);
        // UI image fixture; cart prices/quantities still come from the real disposable server.
        const cartImageRoute = async route => {
            const response = await route.fetch(), data = await response.json();
            if (data.order?.lines) data.order.lines.forEach(line => { line.imageUrl = '/kiosk/test-water.svg'; });
            return route.fulfill({ response, json: data });
        };
        await page.route('**/kiosk/test-water.svg', route => route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="300" viewBox="0 0 200 300"><rect x="78" y="12" width="44" height="24" rx="6" fill="#2560ac"/><path d="M77 36h46v22l25 33v179q0 17-17 17H69q-17 0-17-17V91l25-33Z" fill="#d9ecf5" stroke="#9cc1d8" stroke-width="3"/><rect x="54" y="126" width="92" height="91" fill="#255fa5"/><text x="100" y="173" text-anchor="middle" fill="white" font-size="15" font-family="Arial">AQUAFINA</text><text x="100" y="200" text-anchor="middle" fill="white" font-size="12" font-family="Arial">500 ml</text></svg>' }));
        await page.route('**/kiosk/api/state', cartImageRoute);
        await page.route('**/kiosk/api/poll', cartImageRoute);
        await page.reload(); await page.locator('.g-shop').waitFor(); assert.match(await page.locator('.g-total').innerText(), /100.000/); await shot('cart');
        assert.ok(await page.locator('.g-cart-thumb img').isVisible());
        await page.locator('[data-action^="cart-image-"]').first().click(); await page.locator('.g-product-enlarged').waitFor(); await action('close-dialog').click();
        assert.match(await page.locator('.g-checkout-summary').innerText(), /Khách không lấy hóa đơn/);
        await page.setViewportSize({ width: 1280, height: 800 }); await shot('cart-1280');
        await page.setViewportSize({ width: 800, height: 1280 }); await shot('cart-portrait');
        await page.setViewportSize({ width: 1024, height: 768 });
        await page.unroute('**/kiosk/api/state', cartImageRoute);
        await page.unroute('**/kiosk/api/poll', cartImageRoute);
        await action('help').click(); await page.locator('#g-dialog[open]').waitFor(); await ready(); await action('close-dialog').click();
        const adminContext = await browser.newContext({ viewport: { width: 1366, height: 900 } }), admin = await adminContext.newPage();
        admin.on('pageerror', e => errors.push(e.message));
        await admin.goto(info.baseUrl + '/admin/account/login'); await admin.locator('[name=UserName]').fill(info.user); await admin.locator('[name=Password]').fill(info.password);
        await admin.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([admin.waitForURL(u => !u.pathname.endsWith('/login')), admin.locator('button[type=submit]').click()]);
        await admin.goto(info.baseUrl + '/admin/kiosks');
        await admin.locator('[data-help]:not([hidden])').waitFor();
        await admin.screenshot({ path: path.join(output, 'admin-monitor.png'), fullPage: true });
        await admin.locator('form[action$="/help-done"] button').click(); await admin.locator('#kioskActionMessage.alert-success').waitFor();
        await admin.waitForFunction(() => document.querySelector('[data-help]').hidden);
        await adminContext.close();
        // Only the browser payment presentation is simulated; server bank evidence/idempotency has separate tests.
        await page.addScriptTag({ path: path.resolve('GaoApp.Web/wwwroot/Admin/js/pos/qrcodegen.js') });
        const testQr = await page.evaluate(() => {
            const code = qrcodegen.QrCode.encodeText('KIOSK UI TEST - NOT A PAYMENT', qrcodegen.QrCode.Ecc.MEDIUM);
            const canvas = document.createElement('canvas'), scale = 7, border = 4;
            canvas.width = canvas.height = (code.size + border * 2) * scale;
            const ctx = canvas.getContext('2d'); ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, canvas.width, canvas.height); ctx.fillStyle = '#111';
            for (let y = 0; y < code.size; y++) for (let x = 0; x < code.size; x++) if (code.getModule(x, y)) ctx.fillRect((x + border) * scale, (y + border) * scale, scale, scale);
            return canvas.toDataURL('image/png');
        });
        let cancellationAllowed = false, repeatOrder = false, failCreation = false; const paymentPolls = [];
        let paymentState = { ...adjusted.body, paymentCheckAfterUtc: new Date(Date.now() + 30000).toISOString(), mode: 'payment', paymentStatus: 'Pending', qr: { id: 9001, qrDataUrl: testQr, amount: 100000, content: 'KIEM THU GIAO DIEN - KHONG CHUYEN TIEN', expireAtUtc: new Date(Date.now() + 600000).toISOString() } };
        await page.route('**/kiosk/api/command', async route => {
            const body = route.request().postDataJSON();
            if (body.action === 'start' && repeatOrder) return route.fulfill({ json: paymentState });
            if (body.action === 'checkout') {
                if (failCreation) {
                    paymentState = { ...paymentState, mode: 'payment', paymentStatus: 'Creating', qr: { ...paymentState.qr, qrDataUrl: '' } };
                    return route.fulfill({ status: 409, json: { message: 'ACB chưa xác nhận yêu cầu thành công (mã ACB 30020500). Hãy kiểm tra trạng thái giao dịch.' } });
                }
                return route.fulfill({ json: paymentState });
            }
            if (body.action === 'retry-payment') {
                paymentState = { ...paymentState, mode: 'payment', paymentStatus: 'Pending', qr: { ...paymentState.qr, id: paymentState.qr.id + 1, qrDataUrl: testQr } };
                return route.fulfill({ json: paymentState });
            }
            if (body.action === 'cancel-payment') {
                if (!cancellationAllowed) return route.fulfill({ status: 409, json: { message: 'Giao dịch cần kiểm tra với ngân hàng. Vui lòng chờ nhân viên hỗ trợ.' } });
                paymentState = { ...paymentState, mode: 'idle', order: null, qr: null }; return route.fulfill({ json: paymentState });
            }
            if (body.action === 'finish') { paymentState = { ...paymentState, mode: 'idle', order: null, qr: null, completedAtUtc: null }; return route.fulfill({ json: paymentState }); }
            return route.continue();
        });
        await page.route('**/kiosk/api/poll', route => { paymentPolls.push(route.request().postDataJSON()); return route.fulfill({ json: paymentState }); });
        await page.route('**/kiosk/api/state', route => route.fulfill({ json: paymentState }));
        await action('checkout').click(); await action('pay').click(); await page.locator('.g-payment').waitFor(); await ready(); await shot('payment-ui-only');
        assert.ok(await fits(), 'QR, countdown and cancellation controls fit 1024×768');
        assert.equal(await action('finish').count(), 0);
        await action('cancel-payment').click(); assert.match(await page.locator('#g-dialog').innerText(), /ngân hàng xác nhận chưa nhận tiền/);
        await action('confirm-cancel-payment').click(); await ready();
        await page.locator('.g-error').waitFor(); assert.equal(await page.locator('.g-payment').count(), 1, 'Blocked cancellation preserves payment view');
        const originalPayment = structuredClone(paymentState);
        cancellationAllowed = true; await action('cancel-payment').click(); await action('confirm-cancel-payment').click(); await ready();
        await page.getByRole('heading', { name: 'Đã hủy thanh toán', exact: true }).waitFor();
        assert.equal(await page.locator('.g-payment').count(), 0); await action('close-dialog').click();
        paymentState = originalPayment;
        await page.clock.install();
        paymentState.paymentCheckAfterUtc = await page.evaluate(() => new Date(Date.now() + 30000).toISOString());
        await page.reload(); await page.locator('.g-payment').waitFor();
        paymentPolls.length = 0;
        let polled = page.waitForResponse(r => r.url().endsWith('/kiosk/api/poll'));
        await page.clock.fastForward(10000); await polled;
        assert.ok(paymentPolls.length > 0); assert.ok(paymentPolls.every(p => !p.checkPayment), 'Callback state is checked without waiting for the bank countdown');
        paymentState = { ...paymentState, mode: 'success', paymentStatus: 'Completed', completedAtUtc: await page.evaluate(() => new Date().toISOString()) };
        await page.clock.fastForward(3000); await page.locator('.g-success').waitFor();
        assert.ok(paymentPolls.every(p => !p.checkPayment), 'Callback confirmation completes before the 30-second deadline');
        paymentState = { ...originalPayment, paymentCheckAfterUtc: await page.evaluate(() => new Date(Date.now() + 30000).toISOString()) };
        await page.reload(); await page.locator('.g-payment').waitFor(); paymentPolls.length = 0;
        polled = page.waitForResponse(r => r.url().endsWith('/kiosk/api/poll'));
        await page.clock.fastForward(31000); await polled;
        await page.waitForFunction(() => document.querySelector('#g-bank-countdown')?.textContent === '00:08');
        assert.ok(paymentPolls.some(p => p.checkPayment), 'Countdown triggers a direct bank lookup');
        await page.clock.fastForward(100000); await page.locator('.g-payment').waitFor(); assert.equal(await page.locator('.g-idle').count(), 0, 'Pending QR must not reset on idle timeout');
        paymentState = { ...paymentState, mode: 'success', paymentStatus: 'Completed', completedAtUtc: await page.evaluate(() => new Date().toISOString()) };
        await page.clock.fastForward(4000); await page.locator('.g-success').waitFor(); await shot('success'); assert.ok(await action('finish').isVisible()); assert.ok(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight + 1), 'Success action must fit the screen');
        await page.clock.fastForward(16000); await page.locator('.g-idle').waitFor();
        // A new order must not inherit the prior success, QR or error. Simulate an uncertain create response.
        repeatOrder = true; failCreation = true;
        paymentState = { ...originalPayment, sessionKey: '22222222-2222-4222-8222-222222222222', mode: 'shop', completedAtUtc: null,
            order: { ...originalPayment.order, id: originalPayment.order.id + 1, orderNumber: null, paidTotal: 0 },
            qr: { ...originalPayment.qr, id: 9100 }, paymentCheckAfterUtc: await page.evaluate(() => new Date(Date.now() + 30000).toISOString()) };
        await action('menu').click(); await ready(); await action('shop').click(); await ready();
        await action('checkout').click(); await action('pay').click(); await ready();
        await page.locator('.g-error').waitFor(); assert.match(await page.locator('.g-error').innerText(), /30020500/);
        assert.match(await page.locator('.g-payment h1').innerText(), /Đang xác minh/);
        assert.equal(await page.locator('.g-qr-image').count(), 0); assert.ok(await action('retry-payment').isVisible());
        await shot('payment-create-recovery');
        await action('retry-payment').click(); await action('confirm-retry-payment').click(); await ready();
        assert.ok(await page.locator('.g-qr-image').isVisible()); assert.equal(await page.locator('.g-error').count(), 0);
        // A callback after an error must replace the error with authoritative success, even without a button click.
        paymentState = { ...paymentState, paymentStatus: 'Creating', qr: { ...paymentState.qr, id: 9200, qrDataUrl: '' } };
        await page.reload(); await page.locator('.g-payment').waitFor();
        await action('retry-payment').click();
        await page.route('**/kiosk/api/command', route => route.fulfill({ status: 409, json: { message: 'ACB chưa xác nhận yêu cầu (mã ACB 30020500).' } }));
        await action('confirm-retry-payment').click(); await ready(); await page.locator('.g-error').waitFor();
        paymentState = { ...paymentState, mode: 'success', paymentStatus: 'Completed', order: { ...paymentState.order, paidTotal: paymentState.order.grandTotal }, completedAtUtc: await page.evaluate(() => new Date().toISOString()) };
        await page.clock.fastForward(4000); await page.locator('.g-success').waitFor();
        assert.equal(await page.locator('.g-error').count(), 0, 'Confirmed second order clears the earlier ACB error');
        await page.unroute('**/kiosk/api/command');
        // End the presentation fixture locally; the disposable real cart is unaffected.
        paymentState = { ...paymentState, mode: 'idle', order: null, qr: null, completedAtUtc: null, sessionKey: '33333333-3333-4333-8333-333333333333' };
        await page.reload(); await page.locator('.g-idle').waitFor();
        await page.setViewportSize({ width: 800, height: 1280 }); await action('menu').click(); await ready(); await shot('portrait');
        assert.deepEqual(errors, []);
        console.log('PASS: activation, promotions, three touch actions, accentless product search, unit prices, read-only customer history, private session end, burst scanner, pack price, help request, pending QR reload/idle guard, success reset after 15 seconds, bank countdown/callback priority, safe cancellation UI, cart photos, 1024/1280/portrait layouts.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
