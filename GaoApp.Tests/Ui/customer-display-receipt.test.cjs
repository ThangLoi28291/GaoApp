'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.customer-display-receipt.js'), 'utf8');
const customerSource = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.customer-display-customer.js'), 'utf8');
function fixture() {
    const elements = new Map(), requests = [];
    const element = id => {
        if (!elements.has(id)) elements.set(id, { hidden: true, dataset: {}, textContent: '', innerHTML: '',
            classList: { add() {}, remove() {} }, addEventListener() {}, matches: () => false,
            scrollHeight: 900, clientHeight: 500, offsetWidth: 320, scrollTop: 0 });
        return elements.get(id);
    };
    const window = { matchMedia: () => ({ matches: true }) };
    const context = { window, document: { getElementById: element }, AbortController, console: { warn() {} },
        setTimeout: () => 1, clearTimeout() {}, requestAnimationFrame: () => 1, cancelAnimationFrame() {}, performance: { now: () => 0 },
        fetch(url, options) { return new Promise(resolve => { requests.push({ url, options, resolve }); }); } };
    vm.runInNewContext(customerSource, context);
    vm.runInNewContext(source, context);
    const player = window.CustomerDisplayReceipt.create({ app: { dataset: { storeId: '1', terminalId: '2', storeName: 'Tiệm' } }, onShow() {}, onClose() {} });
    const complete = (index, receipt) => requests[index].resolve({ ok: true, json: async () => receipt });
    const receipt = id => ({ orderId: id, status: 'Completed', storeName: '<img src=x>', customerName: 'Khách <script>',
        orderNumber: 'BILL-' + id, finalizedAtUtc: '2026-10-09T13:00:00Z', subtotal: 100, discountTotal: 15,
        orderDiscount: 5, grandTotal: 85, paidTotal: 100, changeDue: 15, lines: [{ lineId: 1, itemName: '<script>alert(1)</script>', quantity: 2, unitPrice: 50, lineTotal: 100 }] });
    return { player, requests, complete, receipt, element };
}
test('only this store/counter server event starts a bill; repeats do not restart it', async () => {
    const f = fixture();
    f.player.onFinalized({ storeId: 9, terminalId: '2', orderId: 10 });
    f.player.onFinalized({ storeId: 1, terminalId: '3', orderId: 10 });
    f.player.onFinalized({ storeId: 1, orderId: 10 });
    assert.equal(f.requests.length, 0);
    const pending = f.player.show(10);
    f.player.onFinalized({ storeId: 1, terminalId: '2', orderId: 10 });
    assert.equal(f.requests.length, 1);
    f.complete(0, f.receipt(10)); await pending;
    f.player.close(); f.player.onFinalized({ storeId: 1, terminalId: '2', orderId: 10 });
    assert.equal(f.requests.length, 1);
    assert.equal(f.requests[0].options.cache, 'no-store');
});
test('the next purchase cancels an in-flight bill and stale responses cannot show it', async () => {
    const f = fixture();
    const old = f.player.show(10);
    f.player.onDraft({ orderId: 11, lines: [] }); assert.equal(f.player.isActive(), true);
    f.player.onDraft({ orderId: 11, lines: [{ lineId: 2 }] });
    assert.equal(f.requests[0].options.signal.aborted, true);
    const next = f.player.show(11);
    f.complete(1, f.receipt(11)); await next;
    f.complete(0, f.receipt(10)); await old;
    assert.equal(f.element('cdReceiptPaper').dataset.orderId, '11');
    assert.equal(f.element('cdReceiptOverlay').hidden, false);
});
test('rejects draft/mismatched receipts; displays committed totals and escapes customer/product data', async () => {
    const f = fixture();
    let pending = f.player.show(10); f.complete(0, { ...f.receipt(10), status: 'Draft' }); await pending;
    assert.equal(f.element('cdReceiptOverlay').hidden, true);
    pending = f.player.show(10); f.complete(1, f.receipt(11)); await pending;
    assert.equal(f.element('cdReceiptOverlay').hidden, true);
    pending = f.player.show(10); f.complete(2, f.receipt(10)); await pending;
    assert.equal(f.element('cdReceiptTotal').textContent, '85 ₫');
    assert.equal(f.element('cdReceiptChange').textContent, '15 ₫');
    assert.ok(f.element('cdReceiptPaper').innerHTML.includes('&lt;script&gt;alert(1)&lt;/script&gt;'));
    assert.ok(f.element('cdReceiptPaper').innerHTML.includes('&lt;img src=x&gt;'));
    assert.ok(!f.element('cdReceiptPaper').innerHTML.includes('<script>'));
    assert.ok(f.element('cdReceiptPaper').innerHTML.includes('15 ₫'), 'Do not add orderDiscount to the aggregate discount again.');
    assert.ok(f.element('cdReceiptPaper').innerHTML.includes('20:00:00'), 'UTC timestamp must display in Vietnamese time.');
});

test('the finalized bill uses the current customer points and clears them for wholesale customers', async () => {
    const f = fixture();
    const retail = { ...f.receipt(10), customerId: 25, customerPhone: '0901234567', customerCurrentPoints: 1250 };
    let pending = f.player.show(10); f.complete(0, retail); await pending;
    assert.equal(f.element('cdReceiptCustomerName').textContent, retail.customerName);
    assert.equal(f.element('cdReceiptCustomerPhone').textContent, '0901234567');
    assert.equal(f.element('cdReceiptCustomerPoints').textContent, '1.250');
    assert.equal(f.element('cdReceiptCustomerPointsBox').hidden, false);
    assert.match(f.element('cdReceiptPaper').innerHTML, /Điểm hiện tại<\/span><strong>1\.250 điểm/);
    assert.ok(f.element('cdReceiptPaper').innerHTML.includes('Khách &lt;script&gt;'));
    f.player.close();
    pending = f.player.show(11);
    f.complete(1, { ...f.receipt(11), customerId: 26, customerPriceTier: 'WHOLESALE', customerCurrentPoints: 999 }); await pending;
    assert.equal(f.element('cdReceiptCustomerPointsBox').hidden, true);
    assert.equal(f.element('cdReceiptCustomerPoints').textContent, '');
    assert.ok(!f.element('cdReceiptPaper').innerHTML.includes('Điểm hiện tại'));
});
