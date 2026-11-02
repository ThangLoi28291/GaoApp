const test = require('node:test');
const assert = require('node:assert/strict');
const detail = require('../../GaoApp.Web/wwwroot/Admin/js/pos/pos-shift-manager-detail.js');
const payment = (id, method, amount, extra = {}) => ({ id, method, amount, ...extra });
test('mixed receipts cap sale revenue and apply non-cash before cash, excluding deposits', () => {
    const rows = detail.paymentRows({ orders: [{ id: 1, number: 'POS-1', status: 'Completed', grandTotal: 100, deposit: 10,
        payments: [payment(1, 'Cash', 50), payment(2, 'BankTransfer', 40, { channel: 'BankAutomatic' }), payment(3, 'Card', 70)] }] });
    const byId = Object.fromEntries(rows.map(x => [x.id, x]));
    assert.equal(byId[1].applied, 0); assert.equal(byId[2].applied, 40); assert.equal(byId[3].applied, 50);
});
test('deleted, draft and debt collections never enter applied sale totals', () => {
    const rows = detail.paymentRows({ orders: [{ id: 1, status: 'Completed', grandTotal: 30, payments: [payment(1, 'Cash', 60, { cancelled: true }), payment(2, 'BankTransfer', 40, { isDebtCollection: true })] },
        { id: 2, status: 'Draft', grandTotal: 20, payments: [payment(3, 'Cash', 20)] }, { id: 3, status: 'Completed', cancelled: true, grandTotal: 10, payments: [payment(4, 'Cash', 10)] }] });
    assert.equal(rows.reduce((sum, p) => sum + p.applied, 0), 0); assert.equal(rows.length, 4);
});
test('missing bank proof remains explicitly unverified in old responses', () => {
    const rows = detail.paymentRows({ orders: [{ status: 'Completed', grandTotal: 40, payments: [payment(1, 'BankTransfer', 40)] }] });
    assert.equal(rows[0].channel, 'BankUnverified');
});
test('breakdown distinguishes recorded receipts from capped revenue and exposes unmatched totals', () => {
    const html = detail.breakdownHtml({ difference: 5, shiftNonCashSalesTotal: 105, channels: [{ channel: 'Card', label: 'Thẻ', salesAmount: 100, recordedAmount: 120, paymentCount: 2 }] }, true);
    assert.match(html, /data-payment-channel="Card"/); assert.match(html, /100 đ/); assert.match(html, /Nhận 120 đ/); assert.match(html, /lệch tổng/);
});
test('names, references, labels and actor text cannot inject HTML', () => {
    const html = detail.paymentHtml({ number: '<img src=x>', reference: '<script>bad()</script>', actor: '<b>user</b>', amount: 10, applied: 0, orderStatus: 'Draft', orderId: 1 });
    assert.ok(!html.includes('<img src=x>')); assert.ok(!html.includes('<script>')); assert.match(html, /&lt;script&gt;/);
    assert.match(html, /chưa chốt/); assert.ok(!detail.breakdownHtml({ channels: [{ label: '<img src=x>', channel: 'Other' }] }).includes('<img src=x>'));
});
