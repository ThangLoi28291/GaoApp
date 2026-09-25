'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => { let value = ''; process.stdin.on('data', x => value += x); process.stdin.on('end', () => resolve(value)); }));
    const output = path.resolve('TestResults/customer-display/browser'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage(), errors = [];
    page.on('pageerror', e => errors.push(e.message));
    let draft = null, media = [], screenUnavailable = false, mediaUnavailable = false;
    await page.route('**/admin/pos/screen', route => route.fulfill({ status: screenUnavailable ? 503 : 200, contentType: 'application/json', body: JSON.stringify({ currentDraft: draft }) }));
    await page.route('**/admin/displaypromotion/activeforcustomerdisplay', route => route.fulfill({ status: mediaUnavailable ? 503 : 200, contentType: 'application/json', body: JSON.stringify({ data: media }) }));
    // Product artwork belongs only to this browser fixture; production uses catalog images.
    await page.route('**/display-fixture/item-*', route => {
        const index = Number(route.request().url().split('item-')[1]);
        const tones = ['#8aa9ba', '#daae68', '#9dab61', '#b9a3b2', '#8caa8d', '#d89578'];
        const color = tones[index % tones.length];
        const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="240" viewBox="0 0 240 240"><rect width="240" height="240" fill="#f5f5eb"/><ellipse cx="120" cy="214" rx="55" ry="9" fill="#dce0d4"/><path d="m78 68 19-27h49l18 27v138H78Z" fill="${color}"/><path d="m97 41 13 27v138H78V68Z" fill="#ffffff" opacity=".3"/><path d="M97 41h49v13H97Z" fill="#f5f6ea"/><rect x="80" y="111" width="82" height="58" fill="#fffdf1"/><path d="M115 146c-8-15 2-28 21-26 1 17-8 28-21 26Z" fill="${color}"/><path d="m114 151 16-24" stroke="#45644e" stroke-width="2"/></svg>`;
        return route.fulfill({ contentType: 'image/svg+xml', body: svg });
    });
    const money = n => n.toLocaleString('vi-VN');
    const products = [ ['Sữa tươi không đường 180 ml', 3, 8500, 'hộp'], ['Bánh mì nguyên cám', 1, 35000, 'gói'], ['Trà xanh hương nhài', 2, 12000, 'chai'], ['Sữa chua vị việt quất', 2, 28000, 'lốc'], ['Ngũ cốc dinh dưỡng', 1, 68000, 'hộp'], ['Nước ép cam nguyên chất', 1, 42000, 'chai'] ];
    const lines = products.map(([itemName, quantity, unitPrice, sellingUnitName], i) => ({ lineId: i + 1, itemName, quantity, unitPrice, sellingUnitName, lineTotal: quantity * unitPrice, imageThumbUrl: '/display-fixture/item-' + i }));
    const subtotal = lines.reduce((sum, x) => sum + x.lineTotal, 0);
    const sampleDraft = { orderId: 9001, cashierName: 'Người tạo đơn cũ', customerName: 'Nguyễn Minh Anh', customerPhone: '', lines, subtotal, discountTotal: 15000, orderDiscount: 0, grandTotal: subtotal - 15000, paidTotal: 0, balanceDue: subtotal - 15000, changeDue: 0 };
    async function connectBridge() {
        await page.waitForFunction(() => window.signalR && document.querySelector('.cd-status').textContent.includes('Sẵn sàng'));
        await page.evaluate(async ({ storeId, terminalId }) => {
            window.displayProbeBridge = new signalR.HubConnectionBuilder().withUrl('/hubs/pos').build();
            await window.displayProbeBridge.start();
            await window.displayProbeBridge.invoke('JoinStoreGroup', storeId, String(terminalId));
        }, info);
    }
    const send = (eventType, payload = {}) => page.evaluate(async ({ eventType, payload }) => {
        if (eventType === 'customer_display_promotion_changed') {
            // Promotion broadcasts are server-only. Use a real authorized admin action on fixture data.
            if (!window.probePromotionToken) {
                const html = await (await fetch('/admin/label-printing')).text();
                window.probePromotionToken = new DOMParser().parseFromString(html, 'text/html').querySelector('[name="__RequestVerificationToken"]').value;
            }
            const form = new URLSearchParams({ __RequestVerificationToken: window.probePromotionToken, IsActive: 'false' });
            if (window.probePromotionId) form.set('id', String(window.probePromotionId));
            else { form.set('Title', 'Customer display browser fixture'); form.set('MediaType', 'text'); }
            const response = await fetch('/admin/displaypromotion/' + (window.probePromotionId ? 'toggleactive' : 'create'), { method: 'POST', body: form });
            if (!response.ok) throw new Error('Fixture promotion update failed: ' + response.status);
            const result = await response.json();
            if (!result.success) throw new Error('Fixture promotion update was rejected.');
            if (result.id) window.probePromotionId = result.id;
            return;
        }
        await window.displayProbeBridge.invoke('BroadcastTerminalEvent', { eventType, payload });
    }, { eventType, payload });
    async function screenshot(name) {
        await page.evaluate(() => Promise.all(document.getAnimations().map(animation => animation.finished.catch(() => {}))));
        await inViewport('#cdCashierName');
        await inViewport('#cdTerminalName');
        assert.equal(await page.locator('#cdCashierName').innerText(), 'Nguyễn Minh An', 'The display must retain this device operator instead of the old order creator.');
        if (await page.locator('#cdWifi').isVisible()) {
            await inViewport('#cdWifiName'); await inViewport('#cdWifiPassword');
        }
        await page.screenshot({ path: path.join(output, name + '.png') });
    }
    async function inViewport(selector) {
        const rect = await page.locator(selector).boundingBox();
        assert.ok(rect, `${selector} should be visible`);
        const size = page.viewportSize();
        assert.ok(rect.x >= -1 && rect.y >= -1 && rect.x + rect.width <= size.width + 1 && rect.y + rect.height <= size.height + 1,
            `${selector} is clipped at ${size.width}x${size.height}: ${JSON.stringify(rect)}`);
    }
    function separate(a, b) {
        return a.x + a.width <= b.x + 1 || b.x + b.width <= a.x + 1 ||
            a.y + a.height <= b.y + 1 || b.y + b.height <= a.y + 1;
    }
    async function productDetailsVisible() {
        await inViewport('.cd-feature-panel');
        const panel = await page.locator('.cd-feature-panel').boundingBox();
        for (const selector of ['#cdHeroImage', '#cdHeroName', '#cdHeroQty', '#cdHeroPrice', '#cdHeroTotal']) {
            await inViewport(selector);
            const rect = await page.locator(selector).boundingBox();
            assert.ok(rect.x >= panel.x && rect.y >= panel.y && rect.x + rect.width <= panel.x + panel.width + 1 && rect.y + rect.height <= panel.y + panel.height + 1,
                `${selector} must fit inside the visible product card at ${JSON.stringify(page.viewportSize())}`);
        }
    }
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('select[name=SelectedTerminalId]').count()) await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
        await page.goto(info.baseUrl + '/admin/pos/customer-display');
        await connectBridge();
        assert.equal(await page.locator('.cd-brand-name').innerText(), 'GẠO · MARKET');
        assert.equal(await page.locator('#cdWifi').isVisible(), false, 'Unconfigured guest Wi-Fi stays hidden.');
        const settings = await context.newPage();
        await settings.goto(info.baseUrl + '/admin/displaypromotion');
        await settings.locator('#guestWifiName').fill('GẠO MARKET · Guest');
        await settings.locator('#guestWifiPassword').fill('Gao@2026');
        await settings.locator('#guestWifiForm button[type=submit]').click();
        await settings.locator('#guestWifiStatus.text-success').waitFor();
        await page.waitForFunction(() => document.querySelector('#cdWifiName').textContent === 'GẠO MARKET · Guest');
        assert.equal(await page.locator('#cdWifiPassword').innerText(), 'Gao@2026');
        await settings.close();
        await page.locator('.cd-idle-media-fallback img').waitFor();
        await screenshot('idle-1440');
        for (const size of [{ width: 800, height: 600 }, { width: 768, height: 1024 }, { width: 390, height: 844 }]) {
            await page.setViewportSize(size); await inViewport('#cdIdleMediaStage'); await inViewport('.cd-welcome h1');
            await screenshot(`idle-${size.width}x${size.height}`);
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        draft = structuredClone(sampleDraft);
        await send('customer_payment_changed');
        await page.waitForFunction(total => document.querySelector('#cdGrandTotal').textContent === total, money(draft.grandTotal));
        await page.waitForFunction(() => document.querySelector('#cdHeroImage').complete);
        assert.equal(await page.locator('.cd-line').count(), 6);
        await screenshot('cart-1440');
        for (const size of [{ width: 1920, height: 1080 }, { width: 1366, height: 768 }, { width: 1024, height: 768 }, { width: 800, height: 600 }, { width: 768, height: 1024 }, { width: 390, height: 844 }]) {
            await page.setViewportSize(size);
            await inViewport('.cd-total-box'); await inViewport('.cd-footer');
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
            await screenshot(`cart-${size.width}x${size.height}`);
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        // The product card, cart/totals and promotions must all remain visible together.
        media = [{ mediaType: 'text', title: 'Tươi ngon mỗi ngày', description: 'Khám phá những lựa chọn yêu thích tại tiệm.', backgroundColor: '#173c32', textColor: '#f7f7ef', durationSeconds: 120, isFullscreen: true }];
        await send('customer_display_promotion_changed');
        await page.locator('.cd-idle-promo-title').waitFor();
        await page.evaluate(() => { window.preservedPromotion = document.querySelector('.cd-idle-media-text'); });
        for (const size of [{ width: 1440, height: 900 }, { width: 1920, height: 1080 }, { width: 1366, height: 768 }, { width: 1024, height: 768 }, { width: 800, height: 600 }, { width: 768, height: 1024 }, { width: 390, height: 844 }]) {
            await page.setViewportSize(size);
            await inViewport('#cdIdleMediaStage'); await inViewport('.cd-total-box');
            await productDetailsVisible();
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
            const ad = await page.locator('#cdIdleScreen').boundingBox(), cart = await page.locator('.cd-order-panel').boundingBox(), product = await page.locator('.cd-feature-panel').boundingBox();
            assert.ok(separate(ad, cart) && separate(product, cart) && separate(ad, product), 'Product, promotion and cart must not overlap.');
            assert.equal(await page.locator('#cdGrandTotal').innerText(), money(draft.grandTotal));
            await screenshot(`cart-promotion-${size.width}x${size.height}`);
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        draft.lines[1].quantity = 2; draft.lines[1].lineTotal = 70000;
        draft.subtotal += 35000; draft.grandTotal += 35000; draft.balanceDue += 35000;
        await send('customer_payment_changed');
        await page.waitForFunction(() => document.querySelector('#cdHeroName').textContent === 'Bánh mì nguyên cám');
        assert.match(await page.locator('#cdHeroQty').innerText(), /2 gói/);
        await productDetailsVisible();
        assert.match(await page.locator('#cdHeroTotal').innerText(), /70\.000/);
        await screenshot('cart-promotion-product-updated');
        assert.equal(await page.evaluate(() => window.preservedPromotion === document.querySelector('.cd-idle-media-text')), true, 'Cart updates must not restart the promotion.');
        mediaUnavailable = true;
        const failedMediaResponse = page.waitForResponse(response => response.url().endsWith('/admin/displaypromotion/activeforcustomerdisplay') && response.status() === 503);
        await send('customer_display_promotion_changed'); await failedMediaResponse;
        assert.equal(await page.evaluate(() => window.preservedPromotion === document.querySelector('.cd-idle-media-text')), true, 'An unavailable feed must retain the current promotion.');
        mediaUnavailable = false;
        screenUnavailable = true;
        await send('customer_payment_changed');
        await page.waitForFunction(() => document.querySelector('.cd-status').classList.contains('is-offline'));
        assert.equal(await page.locator('#cdGrandTotal').innerText(), money(draft.grandTotal));
        await screenshot('connection-interrupted');
        screenUnavailable = false;
        await send('customer_payment_changed');
        await page.waitForFunction(() => !document.querySelector('.cd-status').classList.contains('is-offline'));
        await send('customer_payment_preview', { method: 0, amount: 300000, expectedBalance: 0, expectedChange: 4500 });
        await page.locator('#cdPaymentOverlay:not(.d-none)').waitFor();
        await screenshot('payment-cash');
        assert.match(await page.locator('#cdPaymentMessage').innerText(), /4\.500 ₫/);
        const qrContext = {}; vm.runInNewContext(fs.readFileSync('GaoApp.Web/wwwroot/Admin/js/pos/qrcodegen.js', 'utf8'), qrContext);
        const qr = qrContext.qrcodegen.QrCode.encodeText('DISPLAY-TEST-NOT-A-PAYMENT', qrContext.qrcodegen.QrCode.Ecc.MEDIUM);
        let cells = ''; for (let y = 0; y < qr.size; y++) for (let x = 0; x < qr.size; x++) if (qr.getModule(x, y)) cells += `<rect x="${x + 4}" y="${y + 4}" width="1" height="1"/>`;
        const qrDataUrl = 'data:image/svg+xml;base64,' + Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${qr.size + 8} ${qr.size + 8}"><rect width="100%" height="100%" fill="white"/><g fill="black">${cells}</g></svg>`).toString('base64');
        await send('customer_payment_qr_created', { bankName: 'NGÂN HÀNG KIỂM THỬ', accountNumber: '0123456789', accountName: 'GẠO MARKET · KIỂM THỬ', qrDataUrl, amount: draft.grandTotal, content: 'TEST POS 9001' });
        await page.locator('#cdQrBox:not(.d-none)').waitFor();
        await page.waitForFunction(() => document.querySelector('#cdQrImage').complete);
        for (const size of [{ width: 1920, height: 1080 }, { width: 1440, height: 900 }, { width: 1366, height: 768 }, { width: 1200, height: 700 }, { width: 1024, height: 768 }, { width: 800, height: 600 }, { width: 768, height: 1024 }, { width: 390, height: 844 }]) {
            await page.setViewportSize(size); await inViewport('#cdQrImage'); await inViewport('#cdQrAmount'); await inViewport('#cdQrContent');
            assert.equal(await page.locator('.cd-payment-visual').evaluate(el => el.scrollHeight <= el.clientHeight + 2), true, 'QR panel must fit without scrolling.');
            if (size.width >= 1200) {
                const ad = await page.locator('#cdIdleScreen').boundingBox(), payment = await page.locator('#cdPaymentOverlay').boundingBox();
                assert.ok(separate(ad, payment), 'Wide payment layout must leave the promotion visible.');
            }
            await screenshot(`payment-qr-${size.width}x${size.height}`);
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        await send('customer_payment_preview', { method: 0, amount: 300000, expectedChange: 4500 });
        await page.waitForFunction(() => document.querySelector('#cdQrBox').classList.contains('d-none'));
        assert.equal(await page.locator('#cdCashVisual').isVisible(), true, 'Switching from QR back to cash clears QR presentation.');
        await send('customer_payment_success', { finalized: false, paidAmount: 100000, remainingAmount: 195500, durationMs: 30000 });
        await page.locator('#cdSuccessOverlay.is-info:not(.d-none)').waitFor();
        assert.match(await page.locator('#cdSuccessTitle').innerText(), /Đã nhận/);
        await screenshot('payment-partial');
        draft = null;
        await send('customer_payment_success', { finalized: true, changeAmount: 4500, durationMs: 30000 });
        await page.locator('#cdSuccessOverlay.is-success:not(.d-none)').waitFor();
        await screenshot('payment-success');
        await send('customer_display_reset');
        await page.locator('#cdSuccessOverlay').waitFor({ state: 'hidden' });
        await page.locator('#cdIdleScreen:not(.d-none)').waitFor();
        assert.equal(await page.locator('#cdCustomerName').innerText(), 'Quý khách');
        // Large carts retain every row, long names and safe text rendering.
        draft = structuredClone(sampleDraft);
        draft.lines = Array.from({ length: 22 }, (_, i) => ({ ...lines[i % lines.length], lineId: i + 1, itemName: i === 0 ? '<img src=x onerror=alert(1)> Sản phẩm có tên rất dài cần hiển thị an toàn' : lines[i % lines.length].itemName }));
        await send('customer_payment_changed');
        await page.waitForFunction(() => document.querySelectorAll('.cd-line').length === 22);
        assert.equal(await page.locator('.cd-line-name img').count(), 0);
        await page.mouse.move(0, 0);
        await page.waitForFunction(() => document.querySelector('#cdCartLines').scrollTop > 0, null, { timeout: 12000 });
        await screenshot('cart-many-items');
        // The existing promotion endpoint still controls text/image/fullscreen media.
        draft = null; media = [{ mediaType: 'text', title: 'Một ngày thật vui', description: 'Cảm ơn bạn đã ghé tiệm.', backgroundColor: '#173c32', textColor: '#f7f7ef', durationSeconds: 30, isFullscreen: false }];
        await send('customer_display_promotion_changed');
        await send('customer_display_reset');
        await page.waitForFunction(() => document.querySelector('.cd-idle-promo-title')?.textContent.includes('Một ngày thật vui'));
        await screenshot('idle-promotion-text');
        media = [{ mediaType: 'image', mediaUrl: '/Admin/img/customer-display/grocery-bag.svg', title: 'Hình ảnh tại tiệm', durationSeconds: 30, isFullscreen: true }];
        await page.reload(); await connectBridge();
        await page.locator('#cdIdleScreen.has-fullscreen-media').waitFor();
        assert.equal(await page.locator('.cd-welcome').isVisible(), false);
        await page.waitForFunction(() => document.querySelector('.cd-idle-media-image')?.complete);
        await screenshot('idle-promotion-fullscreen');
        await page.evaluate(() => { window.preservedPromotion = document.querySelector('.cd-idle-media-image'); });
        draft = structuredClone(sampleDraft); await send('customer_payment_changed');
        await page.waitForFunction(() => document.querySelector('#customerDisplayApp').dataset.view === 'cart');
        assert.equal(await page.evaluate(() => window.preservedPromotion === document.querySelector('.cd-idle-media-image')), true);
        await screenshot('cart-promotion-image');
        draft = null; await send('customer_display_reset');
        await page.waitForFunction(() => document.querySelector('#customerDisplayApp').dataset.view === 'idle');
        assert.equal(await page.evaluate(() => window.preservedPromotion === document.querySelector('.cd-idle-media-image')), true, 'Reset expands the same image without restarting.');
        assert.equal(await page.locator('.cd-welcome').isVisible(), false, 'Fullscreen setting applies again in idle.');
        // Duration and countdown continue while the cashier scans; full-screen items stay in the rail.
        draft = structuredClone(sampleDraft); await send('customer_payment_changed');
        media = [
            { mediaType: 'text', title: 'Ưu đãi hôm nay', description: 'Mời bạn khám phá tại tiệm.', durationSeconds: 3, isFlashSale: true, countdownToUtc: new Date(Date.now() + 3600000).toISOString() },
            { mediaType: 'text', title: 'Lựa chọn tiếp theo', description: 'Tươi ngon mỗi ngày.', durationSeconds: 30, isFullscreen: true }
        ];
        await send('customer_display_promotion_changed');
        await page.locator('.cd-countdown').waitFor();
        const firstCountdown = await page.locator('.cd-countdown').innerText();
        await page.waitForFunction(value => document.querySelector('.cd-countdown')?.textContent !== value, firstCountdown);
        await page.waitForFunction(() => document.querySelector('.cd-idle-promo-title')?.textContent.includes('Lựa chọn tiếp theo'), null, { timeout: 6000 });
        assert.equal(await page.locator('#cdGrandTotal').innerText(), money(draft.grandTotal));
        // Use a real, fixture-generated muted video to check playback across idle/cart transitions.
        const videoBytes = await page.evaluate(async () => {
            const canvas = document.createElement('canvas'); canvas.width = 480; canvas.height = 320;
            const ctx = canvas.getContext('2d'), stream = canvas.captureStream(5), chunks = [];
            const recorder = new MediaRecorder(stream, { mimeType: 'video/webm;codecs=vp8' });
            recorder.ondataavailable = event => chunks.push(event.data);
            const finished = new Promise(resolve => { recorder.onstop = resolve; });
            recorder.start();
            const timer = setInterval(() => {
                ctx.fillStyle = '#173c32'; ctx.fillRect(0, 0, 480, 320);
                ctx.fillStyle = '#d6ed8b'; ctx.font = 'bold 32px sans-serif'; ctx.fillText('TƯƠI NGON MỖI NGÀY', 36, 140);
                ctx.fillRect(36, 190, (performance.now() / 20) % 400, 5);
            }, 200);
            await new Promise(resolve => setTimeout(resolve, 8000)); recorder.stop(); await finished;
            clearInterval(timer); stream.getTracks().forEach(track => track.stop());
            return Array.from(new Uint8Array(await new Blob(chunks).arrayBuffer()));
        });
        await page.route('**/display-fixture/promotion.webm', route => route.fulfill({ contentType: 'video/webm', body: Buffer.from(videoBytes) }));
        media = [{ mediaType: 'video', mediaUrl: '/display-fixture/promotion.webm', title: 'Video tại tiệm', durationSeconds: 120, isFullscreen: true }];
        draft = null; await send('customer_display_reset'); await send('customer_display_promotion_changed');
        await page.waitForFunction(() => document.querySelector('video.cd-idle-media-video')?.currentTime > .2);
        await page.evaluate(() => { window.preservedVideo = document.querySelector('video.cd-idle-media-video'); window.videoTime = window.preservedVideo.currentTime; });
        draft = structuredClone(sampleDraft); await send('customer_payment_changed');
        await page.waitForFunction(() => document.querySelector('#customerDisplayApp').dataset.view === 'cart');
        await page.waitForFunction(() => document.querySelector('video.cd-idle-media-video') === window.preservedVideo && !window.preservedVideo.paused && window.preservedVideo.currentTime > window.videoTime + .2);
        await screenshot('cart-promotion-video');
        draft = null; await send('customer_display_reset');
        await page.waitForFunction(() => document.querySelector('#customerDisplayApp').dataset.view === 'idle');
        assert.equal(await page.evaluate(() => document.querySelector('video.cd-idle-media-video') === window.preservedVideo && !window.preservedVideo.paused), true);
        draft = structuredClone(sampleDraft); await send('customer_payment_changed');
        await page.waitForFunction(() => document.querySelector('#customerDisplayApp').dataset.view === 'cart');
        media = [{ mediaType: 'image', mediaUrl: '/display-fixture/missing-promotion.png', title: 'Broken image', durationSeconds: 30 }];
        await page.route('**/display-fixture/missing-promotion.png', route => route.fulfill({ status: 404, body: '' }));
        await send('customer_display_promotion_changed');
        await page.waitForFunction(() => !document.querySelector('#customerDisplayApp').classList.contains('has-promotions'));
        assert.equal(await page.locator('.cd-feature-panel').isVisible(), true);
        assert.equal(await page.locator('#cdGrandTotal').innerText(), money(draft.grandTotal));
        await screenshot('cart-missing-promotion-fallback');
        draft = null; await send('customer_display_reset');
        await page.locator('.cd-idle-media-fallback').waitFor();
        assert.equal(await page.locator('#cdIdleScreen.has-fullscreen-media').count(), 0);
        await screenshot('idle-missing-promotion-fallback');
        // Clearing the active playlist from admin restores the normal cart without a page reload.
        draft = structuredClone(sampleDraft);
        media = [{ mediaType: 'text', title: 'Nội dung tạm thời', durationSeconds: 120 }];
        await send('customer_payment_changed'); await send('customer_display_promotion_changed');
        await page.waitForFunction(() => document.querySelector('#customerDisplayApp').dataset.view === 'cart' && document.querySelector('#customerDisplayApp').classList.contains('has-promotions'));
        media = []; await send('customer_display_promotion_changed');
        await page.waitForFunction(() => !document.querySelector('#customerDisplayApp').classList.contains('has-promotions'));
        assert.equal(await page.locator('.cd-feature-panel').isVisible(), true);
        assert.equal(await page.locator('#cdGrandTotal').innerText(), money(draft.grandTotal));
        assert.deepEqual(errors, []);
        // Updating credentials preserves exact characters and uses text, not HTML; clearing hides the block live.
        const wifiEditor = await context.newPage();
        await wifiEditor.goto(info.baseUrl + '/admin/displaypromotion');
        await wifiEditor.locator('#guestWifiPassword').fill(' <img src=x onerror=alert(1)> ');
        await wifiEditor.locator('#guestWifiForm button[type=submit]').click();
        await page.waitForFunction(() => document.querySelector('#cdWifiPassword').textContent === ' <img src=x onerror=alert(1)> ');
        assert.equal(await page.locator('#cdWifiPassword img').count(), 0);
        await wifiEditor.locator('#guestWifiStatus.text-success').waitFor();
        await wifiEditor.locator('#guestWifiName').fill(''); await wifiEditor.locator('#guestWifiPassword').fill('');
        await wifiEditor.locator('#guestWifiForm button[type=submit]').click();
        await page.locator('#cdWifi').waitFor({ state: 'hidden' });
        await wifiEditor.close();
        console.log('PASS: configured store identity; idle/cart/cash/QR/partial/success/reset via real SignalR; product details, promotions and totals visible together at 7 sizes; QR at 8 sizes; live promotion updates, rotation, countdown and actual video playback across idle/cart; full-width idle and missing-media fallback; large cart scrolling and safe text.');
    } catch (error) { await screenshot('failure').catch(() => {}); throw error; }
    finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
