'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => resolve(s)); }));
    const output = path.resolve('TestResults/customer-display/receipt'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const cashier = await context.newPage(), display = await context.newPage(), errors = [];
    display.on('pageerror', error => errors.push(error.message));
    const money = value => Number(value || 0).toLocaleString('vi-VN', { maximumFractionDigits: 2 });
    try {
        await cashier.goto(info.baseUrl + '/admin/account/login');
        await cashier.locator('[name=UserName]').fill(info.user); await cashier.locator('[name=Password]').fill(info.password);
        await cashier.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([cashier.waitForURL(url => !url.pathname.endsWith('/login')), cashier.locator('#loginForm button[type=submit]').click()]);
        await cashier.goto(info.baseUrl + '/admin/label-printing');
        const token = await cashier.locator('[name=__RequestVerificationToken]').first().inputValue();
        async function api(url, data) {
            const response = await context.request.post(info.baseUrl + url, { data: data || {}, headers: { RequestVerificationToken: token } });
            assert.equal(response.ok(), true, url + ': ' + await response.text());
            return response.json();
        }
        // Prepare the first cart before the display starts its screen polling.
        const first = await api('/admin/pos/draft');
        await api(`/admin/pos/${first.orderId}/items?variantId=${info.variantId}&qty=3`);
        await api('/admin/pos/cart/current/discount', { discountAmount: 5 });
        await display.goto(info.baseUrl + '/admin/pos/customer-display');
        await display.waitForFunction(() => document.querySelector('.cd-status').textContent.includes('Sẵn sàng'));
        await display.evaluate(async info => {
            window.receiptBridge = new signalR.HubConnectionBuilder().withUrl('/hubs/pos').build();
            await window.receiptBridge.start(); await window.receiptBridge.invoke('JoinStoreGroup', info.storeId, String(info.terminalId));
        }, info);
        const send = (eventType, payload = {}) => display.evaluate(({ eventType, payload }) => window.receiptBridge.invoke('BroadcastTerminalEvent', { eventType, payload }), { eventType, payload });
        async function assertReceipt(id) {
            await display.locator(`#cdReceiptPaper[data-order-id="${id}"]`).waitFor();
            assert.equal(await display.locator('#cdReceiptOverlay').isVisible(), true);
            const receipt = await (await context.request.get(info.baseUrl + `/admin/pos/orders/${id}/receipt`)).json();
            const printed = await (await context.request.get(info.baseUrl + `/admin/pos/orders/${id}/print?autoPrint=false`)).text();
            const printData = JSON.parse(printed.match(/<script\b[^>]*\bid="receiptPrintData"[^>]*>([\s\S]*?)<\/script>/)[1]);
            assert.deepEqual(printData.receipt, receipt, 'Animation and physical bill use the same committed receipt data.');
            assert.equal(await display.locator('#cdReceiptTotal').innerText(), money(receipt.grandTotal) + ' ₫');
            assert.match(await display.locator('.cd-bill-meta').innerText(), new RegExp(receipt.orderNumber));
            const rows = display.locator('.cd-bill-items tbody tr'); assert.equal(await rows.count(), receipt.lines.length);
            for (let i = 0; i < receipt.lines.length; i++) {
                assert.ok((await rows.nth(i).innerText()).includes(receipt.lines[i].productVariantName || receipt.lines[i].itemName));
                assert.equal(await rows.nth(i).locator('td').last().innerText(), money(receipt.lines[i].lineTotal) + ' ₫');
            }
            assert.equal(await display.locator('#cdReceiptPaper img').count(), 0, 'Product names render as safe text.');
            return receipt;
        }
        // No bill on a partial payment. A real server finalize drives the animation even without a POS UI broadcast.
        await api(`/admin/pos/${first.orderId}/payments`, { method: 0, amount: 10, clientRequestId: crypto.randomUUID() });
        await send('customer_payment_refresh');
        await display.locator('#cdSuccessOverlay.is-info:not(.d-none)').waitFor();
        assert.equal(await display.locator('#cdReceiptOverlay').isVisible(), false);
        await api(`/admin/pos/${first.orderId}/payments`, { method: 0, amount: 50, clientRequestId: crypto.randomUUID() });
        await api(`/admin/pos/${first.orderId}/finalize`);
        const receipt = await assertReceipt(first.orderId);
        assert.equal(receipt.discountTotal, 5); assert.equal(receipt.grandTotal, 55); assert.equal(receipt.changeDue, 5);
        assert.equal(await display.locator('#cdReceiptChange').innerText(), '5 ₫');
        await display.waitForFunction(() => document.querySelector('#cdReceiptPaper').getAnimations().every(a => a.playState === 'finished'));
        for (const size of [{ width: 1440, height: 900 }, { width: 800, height: 600 }, { width: 390, height: 844 }, { width: 1920, height: 1080 }]) {
            await display.setViewportSize(size);
            const rect = await display.locator('#cdReceiptOverlay').boundingBox();
            assert.ok(rect.x >= 0 && rect.y >= 0 && rect.x + rect.width <= size.width + 1 && rect.y + rect.height <= size.height + 1);
            assert.equal(await display.locator('#cdReceiptViewport').evaluate(el => el.scrollWidth <= el.clientWidth + 1), true);
            assert.equal(await display.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
            await display.screenshot({ path: path.join(output, `bill-${size.width}x${size.height}.png`) });
        }
        await assert.rejects(() => send('customer_payment_success', { finalized: true, changeAmount: 999999, durationMs: 10 }));
        await assert.rejects(() => send('customer_display_reset', { orderId: first.orderId }));
        assert.equal(await display.locator('#cdReceiptOverlay').isVisible(), true, 'Legacy resets must not cut off the receipt or replace its totals.');
        assert.equal(await display.locator('#cdReceiptChange').innerText(), '5 ₫');
        // New cart has no effect until the next customer actually has products.
        const second = await api('/admin/pos/draft');
        assert.equal(await display.locator('#cdReceiptOverlay').isVisible(), true);
        await api(`/admin/pos/${second.orderId}/items?variantId=${info.receiptVariantIds[0]}&qty=2`);
        await display.locator('#cdReceiptOverlay').waitFor({ state: 'hidden' });
        for (const variantId of info.receiptVariantIds.slice(1)) await api(`/admin/pos/${second.orderId}/items?variantId=${variantId}&qty=1`);
        const draft = (await (await context.request.get(info.baseUrl + '/admin/pos/screen')).json()).currentDraft;
        const payment = await api('/admin/pos/cart/current/payment-and-finalize', { orderId: second.orderId, method: 0, amount: draft.grandTotal + 43, clientRequestId: crypto.randomUUID() });
        assert.equal(payment.finalized, true);
        await assertReceipt(second.orderId);
        assert.equal(await display.locator('#cdReceiptChange').innerText(), '43 ₫');
        assert.equal(await display.locator('.cd-bill-items tbody tr').count(), 20);
        await display.setViewportSize({ width: 1440, height: 900 }); await display.mouse.move(0, 0);
        await display.waitForFunction(() => document.querySelector('#cdReceiptViewport').scrollTop > 30, null, { timeout: 10000 });
        await display.screenshot({ path: path.join(output, 'bill-long-feeding.png') });
        await display.locator('#cdReceiptViewport').evaluate(el => { el.scrollTop = el.scrollHeight; });
        await display.screenshot({ path: path.join(output, 'bill-long-totals.png') });
        await display.setViewportSize({ width: 390, height: 844 });
        await display.screenshot({ path: path.join(output, 'bill-long-mobile.png') });
        assert.equal(await display.locator('#cdReceiptViewport').evaluate(el => el.scrollWidth <= el.clientWidth + 1), true);
        // Exercise the actual cashier UI and its delayed success/reset messages. Prevent any printer call.
        await context.route('**/admin/pos/orders/*/print*', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><title>Fixture print disabled</title>' }));
        await display.emulateMedia({ reducedMotion: 'reduce' });
        const third = await api('/admin/pos/draft');
        await api(`/admin/pos/${third.orderId}/items?variantId=${info.variantId}&qty=2`);
        await cashier.goto(info.baseUrl + '/admin/pos');
        await cashier.waitForFunction(id => window.posApp?.state.business.currentDraft?.orderId === id && window.PosOffline?.status().ready, third.orderId);
        await cashier.locator('#btnOpenPayment').click();
        await cashier.locator('#payAmount').fill('50'); await cashier.locator('#payAmount').dispatchEvent('input');
        await cashier.locator('#btnAddPayment').click();
        await display.locator(`#cdReceiptPaper[data-order-id="${third.orderId}"]`).waitFor();
        await display.waitForTimeout(5500);
        assert.equal(await display.locator('#cdReceiptOverlay').isVisible(), true, 'Real cash auto-finalize/printing and its reset retain the bill.');
        assert.equal(await display.locator('#cdReceiptChange').innerText(), '10 ₫');
        assert.equal(await display.locator('#cdReceiptPaper').evaluate(el => el.getAnimations().length), 0);
        assert.equal(await display.locator('#cdReceiptViewport').evaluate(el => el.scrollTop), 0, 'Reduced motion disables automatic scrolling.');
        await display.setViewportSize({ width: 1440, height: 900 });
        await display.screenshot({ path: path.join(output, 'bill-cash-ui.png') });
        // Real POS customer selection must appear immediately, even with no products and a previous bill still showing.
        const empty = await api('/admin/pos/draft');
        await cashier.waitForFunction(id => window.posApp?.state.business.currentDraft?.orderId === id, empty.orderId);
        const selectCustomer = customerId => cashier.evaluate(id => window.posApp.modules.posCustomer.setCustomerToCurrentCart(id), customerId);
        await selectCustomer(info.customerIds[0]);
        await display.waitForFunction(() => document.querySelector('#cdIdleCustomerName').textContent === 'Khách điểm màn hình' && !document.querySelector('#cdIdleCustomerSection').hidden);
        assert.equal(await display.locator('#cdReceiptOverlay').isVisible(), false, 'Customer selection dismisses the previous bill without requiring a product scan.');
        assert.equal(await display.locator('#cdIdleCustomerSection').isVisible(), true);
        assert.equal(await display.locator('#cdIdleCustomerPhone').innerText(), '0901234567');
        assert.equal(await display.locator('#cdIdleCustomerPoints').innerText(), '1.250');
        assert.equal(await display.locator('#cdIdleCustomerPointsBox').isVisible(), true);
        const emptyDraft = (await (await context.request.get(info.baseUrl + '/admin/pos/screen')).json()).currentDraft;
        assert.equal(emptyDraft.lines.length, 0);
        await display.screenshot({ path: path.join(output, 'customer-selected-empty-cart.png') });
        await selectCustomer(info.customerIds[1]);
        await display.waitForFunction(() => document.querySelector('#cdIdleCustomerName').textContent === 'Khách mới chưa tích điểm');
        assert.equal(await display.locator('#cdIdleCustomerPoints').innerText(), '0');
        assert.equal(await display.locator('#cdIdleCustomerPointsBox').isVisible(), true);
        await selectCustomer(info.customerIds[2]);
        await display.waitForFunction(() => document.querySelector('#cdIdleCustomerName').textContent === 'Khách sỉ màn hình');
        assert.equal(await display.locator('#cdIdleCustomerPointsBox').isVisible(), false);
        assert.equal(await display.locator('#cdIdleCustomerPoints').textContent(), '');
        console.log('PASS real POS customer selection instantly shows identity/current SQL reward points with zero products; prior bill dismissed; zero and wholesale balances handled correctly.');
        await api('/admin/customer-deposit/receive', { clientRequestId: crypto.randomUUID(), customerId: info.customerIds[2],
            amount: 25000, method: 0, purpose: 'Browser test deposit' });
        await display.locator('#cdReceiptOverlay').waitFor({ state: 'hidden' });
        await display.locator('#cdSuccessOverlay:not(.d-none)').waitFor();
        assert.equal(await display.locator('#cdSuccessTitle').innerText(), 'Đã nhận tiền đặt cọc');
        assert.match(await display.locator('#cdSuccessAmount').innerText(), /25\.000/);
        assert.deepEqual(errors, []);
        console.log('PASS: real SQL/SignalR finalize; two distinct receipts match printed DTOs; partial payment; aggregate discount/change; legacy reset; next customer; 20 safe rows; automatic feed/scroll and four viewport sizes; real cashier cash auto-finalize/print flow and reduced motion.');
    } catch (error) { await display.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {}); throw error; }
    finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
