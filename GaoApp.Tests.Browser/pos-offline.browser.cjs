'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const receiptProbe = process.argv.includes('--receipt-templates') ? require('./receipt-templates.browser.cjs') : null;
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let value = ''; process.stdin.on('data', x => value += x); process.stdin.on('end', () => resolve(value)); }));
    const output = path.resolve('TestResults/pos-offline/browser'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    let page = await context.newPage(); const errors = [];
    const observe = p => p.on('pageerror', e => errors.push(e.message)); observe(page);
    const request = (url, method = 'GET', body) => page.evaluate(async ({ url, method, body }) => {
        const response = await fetch(url, { method, headers: { 'Content-Type': 'application/json',
            'RequestVerificationToken': window.PosCommon?.getAntiForgeryToken?.() || '' }, body: body == null ? undefined : JSON.stringify(body) });
        const data = await response.json(); if (!response.ok) throw new Error(`${response.status}: ${JSON.stringify(data)}`); return data;
    }, { url, method, body });
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('select[name=SelectedTerminalId]').count()) await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
        let releaseCatalog;
        const catalogGate = new Promise(resolve => { releaseCatalog = resolve; });
        await page.route('**/admin/pos/offline/catalog?**', async route => { await catalogGate; await route.continue(); });
        await page.goto(info.baseUrl + '/admin/pos');
        await page.waitForFunction(() => window.posApp?.state?.business?.currentDraft && PosOffline.status().preparing);
        assert.equal(await page.evaluate(() => PosOffline.status().ready), false);
        await request('/admin/pos/cart/current/note', 'POST', { note: 'Changed while catalog loads' });
        console.log('PASS: first POS screen and online edits work before the catalog download completes');
        releaseCatalog();
        await page.waitForFunction(() => window.PosOffline?.status().ready, { timeout: 45000 });
        await page.unroute('**/admin/pos/offline/catalog?**');
        assert.equal(await page.evaluate(() => PosOffline.canWork()), true);
        await page.waitForFunction(() => PosOffline.status().shellReady, { timeout: 45000 });
        await context.setOffline(true);
        assert.equal((await request('/admin/pos/screen')).currentDraft.note, 'Changed while catalog loads');
        await context.setOffline(false); await page.evaluate(() => PosOffline.sync());
        console.log('PASS: actual POS assets, IndexedDB and service worker prepared');
        await page.waitForFunction(() => !!navigator.serviceWorker.controller);
        const newVersion = await page.evaluate(async () => {
            const response = await fetch('/Admin/js/pos/pos.offline.core.js?v=cache-miss-' + crypto.randomUUID());
            return { status: response.status, text: await response.text() };
        });
        assert.equal(newVersion.status, 200); assert.ok(newVersion.text.includes('PosOfflineCore'));
        // Reproduce the reported startup failure: a controlling worker with four missing scripts.
        await page.evaluate(async () => {
            const cache = await caches.open('gao-pos-assets-v1');
            for (const request of await cache.keys()) {
                if (/\/(pos\.offline\.core|pos\.offline|pos\.render|pos\.payment)\.js$/.test(new URL(request.url).pathname))
                    await cache.delete(request);
            }
        });
        let repeatedCatalogRequests = 0;
        const countCatalog = request => { if (/\/offline\/(catalog|customers|promotions)/.test(request.url())) repeatedCatalogRequests++; };
        page.on('request', countCatalog);
        await page.reload();
        await page.waitForFunction(() => window.PosOffline?.canWork() && typeof window.PosRender?.renderPayments === 'function');
        await page.waitForFunction(() => PosOffline.status().shellReady);
        page.off('request', countCatalog);
        assert.equal(repeatedCatalogRequests, 0, 'A fresh catalog should be reused on reload');
        console.log('PASS: new asset versions and POS reload with missing cached scripts load from the server');
        // A transient QR-list error must disappear after an empty successful response too.
        await page.route('**/admin/acb/payments/terminal-pending', route => route.fulfill({ status: 503,
            contentType: 'application/json', body: '{}' }));
        await page.evaluate(() => window.dispatchEvent(new CustomEvent('acb:payment-changed')));
        await page.locator('#acbPaymentNotice').filter({ hasText: 'Chưa tải được các QR đang chờ' }).waitFor({ state: 'visible' });
        await page.unroute('**/admin/acb/payments/terminal-pending');
        await page.evaluate(() => window.dispatchEvent(new CustomEvent('acb:payment-changed')));
        await page.locator('#acbPaymentNotice').waitFor({ state: 'hidden' });
        console.log('PASS: pending-QR warning clears after server recovery with no pending QR');
        if (receiptProbe) await receiptProbe.prepare(page, context, info, output);
        let screen = await request('/admin/pos/screen'); const firstId = screen.currentDraft.orderId;
        const afterAdd = await page.evaluate(async variantId => {
            const result = await posApp.modules.posBarcode.addVariantToCurrentCart(variantId, 1);
            const button = document.getElementById('btnOpenPayment');
            const snapshot = { ok: result.ok, disabled: button.disabled, uiLocked: button.dataset.uiLocked,
                busyScopes: [...posApp.state.network.busyScopes], paymentState: document.getElementById('sumPaymentStateText').textContent };
            // Same turn as completion: a timer/heartbeat cannot be needed to open checkout.
            const modal = document.getElementById('paymentModal');
            snapshot.modalOpened = false;
            modal.addEventListener('show.bs.modal', () => { snapshot.modalOpened = true; }, { once: true });
            window.__checkoutShown = new Promise(resolve => modal.addEventListener('shown.bs.modal', () => resolve(), { once: true }));
            button.click();
            return snapshot;
        }, info.variantId);
        assert.equal(afterAdd.ok, true);
        assert.equal(afterAdd.disabled, false);
        assert.equal(afterAdd.uiLocked, 'false');
        assert.deepEqual(afterAdd.busyScopes, []);
        assert.equal(afterAdd.paymentState, 'Chưa thanh toán');
        assert.equal(afterAdd.modalOpened, true);
        await page.evaluate(async () => { await window.__checkoutShown; bootstrap.Modal.getInstance(document.getElementById('paymentModal')).hide(); });
        await page.locator('#paymentModal').waitFor({ state: 'hidden' });
        console.log('PASS: checkout opens immediately after adding a product without waiting for the offline heartbeat');
        await page.route('**/admin/pos/cart/current/hold', route => route.fulfill({ status: 403, contentType: 'application/json', body: JSON.stringify({ message: 'Permission denied' }) }));
        await assert.rejects(request('/admin/pos/cart/current/hold', 'POST', { holdNote: 'Denied test' }), /403/);
        assert.equal(await page.evaluate(() => PosOffline.canWork()), true);
        assert.equal(await page.evaluate(() => PosOffline.status().pending), 0);
        await request('/admin/pos/products/search?keyword=test');
        await page.unroute('**/admin/pos/cart/current/hold');
        const held = await request('/admin/pos/cart/current/hold', 'POST', { holdNote: 'Hold regression' });
        assert.equal(held.heldOrderId, firstId);
        await request(`/admin/pos/orders/${firstId}/resume`, 'POST', {});
        assert.equal((await request('/admin/pos/screen')).currentDraft.orderId, firstId);
        console.log('PASS: permission denial does not invalidate login; permitted hold and resume work');
        const other = await context.newPage(); await other.goto(info.baseUrl + '/admin/pos');
        await other.waitForFunction(() => window.PosOffline?.status().ready);
        assert.equal(await other.evaluate(() => PosOffline.status().writer), false); await other.close();
        console.log('PASS: second tab cannot write to the same terminal');
        // The server commits this addition, but its response is lost before the cashier receives it.
        let dropped = false;
        await page.route('**/admin/pos/**/items?**', async route => {
            if (!dropped) { dropped = true; await route.fetch(); await context.setOffline(true); await route.abort(); }
            else await route.continue();
        });
        const afterLostReply = await request(`/admin/pos/${firstId}/items?variantId=${info.variantId}&qty=1`, 'POST', {});
        assert.equal(afterLostReply.lines[0].quantity, 2);
        await page.unroute('**/admin/pos/**/items?**');
        await request('/admin/pos/cart/current/scan', 'POST', { barcode: afterLostReply.lines[0].barcode, qty: 1 }).catch(async () =>
            request(`/admin/pos/${firstId}/items?variantId=${info.variantId}&qty=1`, 'POST', {}));
        const cash = await request('/admin/pos/cart/current/payment-and-finalize', 'POST', { orderId: firstId, clientRequestId: require('node:crypto').randomUUID(), method: 0, amount: 60 });
        assert.equal(cash.finalized, true); assert.equal(cash.draft.grandTotal, 60);
        if (receiptProbe) await receiptProbe.offlinePrint(page, firstId, output);
        screen = await request('/admin/pos/screen');
        const secondId = screen.currentDraft.orderId;
        await request(`/admin/pos/${secondId}/items?variantId=${info.variantId}&qty=2`, 'POST', {});
        const qr = await request('/admin/pos/cart/current/payment-qr', 'POST', { amount: 40, bankAccountId: info.bankId, clientRequestId: require('node:crypto').randomUUID() });
        assert.equal(qr.automaticConfirmation, false); assert.ok(qr.qrDataUrl.startsWith('data:image/png;'));
        await page.screenshot({ path: path.join(output, 'offline-before-restart.png'), fullPage: true });
        const pending = await page.evaluate(() => PosOffline.status().pending); assert.ok(pending >= 4);
        await page.close(); page = await context.newPage(); observe(page);
        await page.goto(info.baseUrl + '/admin/pos');
        await page.waitForFunction(() => window.PosOffline?.canWork());
        if (receiptProbe) await receiptProbe.mockPrinters(page);
        assert.equal(await page.evaluate(() => PosOffline.status().pending), pending);
        await page.evaluate(() => {
            const original = window.fetch;
            window.__acbPollRequests = [];
            window.fetch = function (input, options) {
                const path = new URL(typeof input === 'string' || input instanceof URL ? input : input.url, location.href).pathname;
                if (/^\/admin\/acb\/payments\/(terminal-pending|\d+\/(status|complete))$/.test(path))
                    window.__acbPollRequests.push(path);
                return original.call(window, input, options);
            };
            window.dispatchEvent(new CustomEvent('acb:payment-changed'));
        });
        assert.deepEqual(await page.evaluate(() => window.__acbPollRequests), []);
        await page.locator('#acbPaymentNotice').waitFor({ state: 'hidden' });
        screen = await request('/admin/pos/screen'); assert.equal(screen.currentDraft.orderId, secondId); assert.equal(screen.currentDraft.grandTotal, 40);
        const savedQr = await request(`/admin/acb/payments/orders/${secondId}/qrs/${qr.id}`); assert.equal(savedQr.qr.qrRawText, qr.qrRawText);
        console.log('PASS: offline page restart restores carts, pending writes and QR from disk');
        await page.waitForFunction(() => document.getElementById('sumGrandTotal')?.textContent.trim() === '40');
        await page.locator('#btnOpenPayment').click();
        await page.locator('[data-pay-method-value="1"]').click();
        await page.locator('#btnReopenLatestPaymentQr').click();
        await page.locator('#paymentQrModal.show').waitFor();
        assert.equal((await page.locator('#btnConfirmPaymentQrPaid').textContent()).trim(), 'Đã nhận thủ công');
        await page.screenshot({ path: path.join(output, 'offline-qr-restored.png'), fullPage: true });
        await page.locator('#btnConfirmPaymentQrPaid').click();
        await page.waitForFunction(async id => { const r = await fetch(`/admin/pos/${id}`); return r.ok && (await r.json()).status === 2; }, secondId);
        const transfer = await request(`/admin/pos/${secondId}`);
        assert.equal(transfer.paidTotal, 40); assert.equal(transfer.grandTotal, 40);
        console.log('PASS: cashier reopens QR and confirms manual receipt through the existing offline UI');
        // Keep the first replay in flight to exercise LAN recovery before the queue is drained.
        let releaseReplay, heldReplay = false;
        const replayGate = new Promise(resolve => { releaseReplay = resolve; });
        const holdFirstReplay = async route => {
            if (!heldReplay && route.request().headers()['x-pos-operation-id']) {
                heldReplay = true;
                await replayGate;
            }
            await route.continue();
        };
        await page.route('**/admin/pos/**', holdFirstReplay);
        try {
            await context.setOffline(false);
            await page.evaluate(() => { PosOffline.sync(); });
            await page.waitForFunction(() => PosOffline.status().connected && PosOffline.status().pending > 0);
            await page.evaluate(() => window.dispatchEvent(new CustomEvent('acb:payment-changed')));
            assert.deepEqual(await page.evaluate(() => window.__acbPollRequests), [], 'Bank polling must wait for the pending journal');
        } finally { releaseReplay(); }
        await page.evaluate(() => PosOffline.sync());
        await page.waitForFunction(() => PosOffline.status().pending === 0, { timeout: 45000 });
        await page.unroute('**/admin/pos/**', holdFirstReplay);
        assert.equal(await page.evaluate(() => PosOffline.status().message), '');
        await page.waitForFunction(() => window.__acbPollRequests.includes('/admin/acb/payments/terminal-pending'));
        await page.locator('#acbPaymentNotice').waitFor({ state: 'hidden' });
        console.log('PASS: QR polling pauses offline and during replay, resumes after sync, with no stale warning');
        console.log('PASS: ordered replay after a lost committed response, cash receipt and manual QR');
        if (receiptProbe) await receiptProbe.onlinePrint(context, info, firstId, output);
        await page.screenshot({ path: path.join(output, 'after-sync.png'), fullPage: true });
        // Once the recovery notice hides, successful heartbeats must not show it again.
        const networkBanner = page.locator('#posNetworkStateBanner');
        await networkBanner.waitFor({ state: 'hidden' });
        await page.evaluate(() => {
            window.__networkBannerReshown = false;
            const banner = document.getElementById('posNetworkStateBanner');
            new MutationObserver(() => {
                if (banner.style.display !== 'none') window.__networkBannerReshown = true;
            }).observe(banner, { attributes: true, childList: true, subtree: true });
        });
        for (let heartbeat = 0; heartbeat < 3; heartbeat++) await page.evaluate(() => PosOffline.sync());
        assert.equal(await networkBanner.isVisible(), false);
        assert.equal(await page.evaluate(() => window.__networkBannerReshown), false);
        console.log('PASS: stable connection heartbeats do not reopen the hidden recovery banner');
        await context.setOffline(true);
        await networkBanner.waitFor({ state: 'visible' });
        assert.match(await networkBanner.textContent(), /Mất kết nối/);
        const beforeQuota = await request('/admin/pos/screen');
        await page.evaluate(() => { IDBObjectStore.prototype.put = function () { throw new DOMException('Simulated quota', 'QuotaExceededError'); }; });
        await assert.rejects(request(`/admin/pos/${beforeQuota.currentDraft.orderId}/items?variantId=${info.variantId}&qty=1`, 'POST', {}), /Không ghi được dữ liệu/);
        assert.equal(await page.evaluate(() => PosOffline.canWork()), false);
        console.log('PASS: disk failure rejects the sale before claiming it was saved');
        assert.deepEqual(errors, []);
        await page.screenshot({ path: path.join(output, 'disk-error.png'), fullPage: true });
    } catch (error) {
        console.error('STATE', await page.evaluate(() => { const s = window.PosOffline?.status(); if (s?.context) delete s.context.antiForgeryToken; return s; }).catch(() => null));
        console.error('JS ERRORS', errors); await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {});
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
