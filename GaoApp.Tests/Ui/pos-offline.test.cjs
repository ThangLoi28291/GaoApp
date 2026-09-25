const test = require('node:test');
const assert = require('node:assert/strict');
const core = require('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.core.js');
const { randomUUID } = require('node:crypto');
const fs = require('node:fs');
const vm = require('node:vm');
function fixture() {
    const context = { storeId: 1, terminalId: 2, userId: 3, shiftId: 4, permissions: ['pos.order.create', 'pos.order.finalize', 'pos.order.hold', 'pos.order.discount', 'pos.payment.create'], screen: {} };
    const state = core.initial(context);
    const catalog = { customers: [{ customerId: 9, name: 'Khách sỉ', priceTier: 'WHOLESALE' }], products: [{ id: 10, productId: 11,
        productName: 'Sữa', productVariantName: 'Sữa tươi', sku: 'MILK', price: 10000, onHandQty: 12,
        units: [{ id: 12, unitId: 13, unitName: 'Hộp', factor: 1, isBaseUnit: true, isDefaultForSale: true, price: 10000, wholesalePrice: 9000, barcodes: ['123456'] },
            { id: 14, unitId: 15, unitName: 'Lốc', factor: 4, price: 36000, wholesalePrice: 32000, barcodes: ['654321'] }] }] };
    const act = (url, body = {}, method = 'POST') => core.clone(core.apply(state, catalog, { url, body, method, occurredAt: '2026-09-09T03:00:00.000Z' }));
    act('/admin/pos/cart/ensure');
    return { state, catalog, act };
}
test('scan, change quantity, discount, cash and a new cart preserve completed sale through a reload', () => {
    const { state, act } = fixture();
    const draft = act('/admin/pos/cart/current/scan', { barcode: '123456', qty: 2 });
    assert.equal(draft.grandTotal, 20000);
    act(`/admin/pos/lines/${draft.lines[0].lineId}?qty=3`, {}, 'PATCH');
    act('/admin/pos/cart/current/discount', { discountAmount: 1000 });
    const completed = act('/admin/pos/cart/current/payment-and-finalize', { orderId: draft.orderId, clientRequestId: randomUUID(), method: 0, amount: 50000 });
    assert.equal(completed.draft.grandTotal, 29000); assert.equal(completed.draft.changeDue, 21000);
    const next = act('/admin/pos/cart/current/new'); assert.notEqual(next.orderId, draft.orderId);
    const reloaded = JSON.parse(JSON.stringify(state));
    assert.equal(reloaded.orders[draft.orderId].status, 2); assert.equal(core.screen(reloaded).currentDraft.orderId, next.orderId);
});
test('cached product, gift and combo promotions preserve server calculation order and stable gift identities', () => {
    const { state, catalog, act } = fixture();
    catalog.products[0].units = catalog.products[0].units.slice(0, 1);
    catalog.promotions = [
        { id: 1, type: 1, name: '10%', priority: 1, discountType: 1, discountValue: 10, items: [{ productId: 11, minQuantity: 2 }] },
        { id: 2, type: 3, name: 'Mua 2 tặng 1', priority: 1, buyQuantity: 2, getQuantity: 1, items: [{ productId: 11 }] },
        { id: 3, type: 2, name: 'Combo', priority: 1, comboFixedPrice: 15000, comboRules: [{ productId: 11, requiredQuantity: 2 }] }
    ];
    const draft = act('/admin/pos/cart/current/scan', { barcode: '123456', qty: 2 });
    assert.equal(draft.promotionDiscountTotal, 2000); assert.equal(draft.comboDiscountTotal, 3000); assert.equal(draft.grandTotal, 15000);
    const gift = draft.lines.find(l => l.isPromotionGift); assert.equal(gift.quantity, 1); assert.equal(gift.unitPrice, 0);
    const changed = act(`/admin/pos/lines/${draft.lines[0].lineId}?qty=4`, {}, 'PATCH');
    assert.equal(changed.grandTotal, 30000); assert.equal(changed.lines.find(l => l.isPromotionGift).lineId, gift.lineId);
    assert.equal(changed.lines.find(l => l.isPromotionGift).quantity, 2);
    assert.throws(() => act(`/admin/pos/lines/${gift.lineId}?qty=9`, {}, 'PATCH'), /Hàng tặng/);
    assert.notEqual(core.linesSignature(changed), core.linesSignature({ ...changed, lines: changed.lines.filter(l => !l.isPromotionGift) }));
    assert.equal(state.orders[state.currentId].lines.length, 2);
});
test('new offline customer is durable and later references use the server customer identity', () => {
    const { state, catalog, act } = fixture();
    const draft = act('/admin/pos/customers/quick-create', { name: 'Khách mới', phone: '0901234567', priceTier: 'WHOLESALE' });
    const reloaded = core.clone(state);
    assert.equal(core.read(reloaded, catalog, new URL('https://pos/admin/pos/customers/search?keyword=0901234567'))[0].name, 'Khách mới');
    core.learn(reloaded, draft, { ...draft, customerId: 100 });
    assert.equal(core.translate(reloaded, { url: `/admin/pos/cart/current/customer/${draft.customerId}`, body: {} }).url, '/admin/pos/cart/current/customer/100');
    assert.throws(() => act('/admin/pos/customers/quick-create', { name: 'Trùng', phone: '0901234567' }), /đã tồn tại/);
});
test('pack and wholesale pricing matches the existing POS thresholds', () => {
    const { act } = fixture();
    const draft = act('/admin/pos/cart/current/scan', { barcode: '123456', qty: 4 });
    assert.equal(draft.grandTotal, 36000);
    const wholesale = act('/admin/pos/cart/current/customer/9', { repriceExistingLines: true });
    assert.equal(wholesale.grandTotal, 32000);
});
test('repeated payment identity does not count cash twice', () => {
    const { act } = fixture();
    act('/admin/pos/cart/current/scan', { barcode: '123456' });
    const body = { clientRequestId: randomUUID(), method: 0, amount: 10000 };
    act('/admin/pos/cart/current/payments', body);
    const again = act('/admin/pos/cart/current/payments', body);
    assert.equal(again.paidTotal, 10000); assert.equal(again.payments.length, 1);
    assert.throws(() => act('/admin/pos/cart/current/payments', { ...body, amount: 20000 }), /Mã lần thu/);
});
test('manual QR collection is a bank transfer with an explicit local confirmation source', () => {
    const { state, act } = fixture();
    act('/admin/pos/cart/current/scan', { barcode: '123456' });
    const result = act('/admin/pos/offline/manual-transfer', { orderId: state.currentId, clientRequestId: randomUUID(), bankAccountId: 1, amount: 10000, referenceCode: 'GAOABC123' });
    assert.equal(result.finalized, true); assert.equal(result.confirmationSource, 'offline-manual');
    assert.equal(result.draft.payments[0].method, 'BankTransfer'); assert.equal(result.draft.payments[0].reference, 'GAOABC123');
});
test('holding a cart creates a separate cart and resumes only stored orders', () => {
    const { state, act } = fixture();
    act('/admin/pos/cart/current/scan', { barcode: '123456' });
    const held = act('/admin/pos/cart/current/hold', { holdNote: 'Chờ khách' });
    assert.notEqual(held.heldOrderId, held.newDraftOrderId); assert.equal(core.screen(state).heldOrders.length, 1);
    act(`/admin/pos/orders/${held.heldOrderId}/resume`); assert.equal(state.currentId, held.heldOrderId);
    assert.throws(() => act('/admin/pos/orders/999/resume'), /chưa có/);
});

test('offline scanning accepts the quantity field sent by the barcode input', () => {
    const { act } = fixture();
    const draft = act('/admin/pos/cart/current/scan', { barcode: '123456', quantity: 4 });
    assert.equal(draft.lines[0].quantity, 4);
});
test('mapped request identities survive restart without changing unrelated product identifiers', () => {
    const { state, act } = fixture();
    const local = act('/admin/pos/cart/current/scan', { barcode: '123456' });
    const remote = structuredClone(local); remote.orderId = 101; remote.lines[0].lineId = 201;
    core.learn(state, local, remote);
    const replay = core.translate(JSON.parse(JSON.stringify(state)), { url: `/admin/pos/lines/${local.lines[0].lineId}?qty=2`,
        body: { orderId: local.orderId, variantId: 10 }, expectedOrderId: local.orderId });
    assert.equal(replay.url, '/admin/pos/lines/201?qty=2'); assert.equal(replay.body.orderId, 101); assert.equal(replay.body.variantId, 10);
    assert.equal(replay.expectedOrderId, 101);
});
test('missing catalog entries and unsupported server actions fail without accepting a sale', () => {
    const { state, act } = fixture();
    assert.throws(() => act('/admin/pos/cart/current/scan', { barcode: 'missing' }), /chưa có/);
    assert.equal(core.current(state).lines.length, 0);
    assert.throws(() => act('/admin/pos/cart/current/reward-vouchers', { voucherIds: [1] }), /cần kết nối/);
});
test('permission restrictions and incomplete payments cannot finalize a sale', () => {
    const { state, act } = fixture();
    act('/admin/pos/cart/current/scan', { barcode: '123456' });
    assert.throws(() => act('/admin/pos/cart/current/finalize'), /chưa nhận đủ/);
    state.context.permissions = [];
    assert.throws(() => act('/admin/pos/cart/current/payments', { clientRequestId: randomUUID(), method: 0, amount: 10000 }), /quyền/);
});
test('VietQR payload has a verified CRC, correct account and variable amount', () => {
    const value = core.vietQr('970416', '123456789', 123000, 'GAO2TEST');
    assert.match(value, /0010?6?970416|0006970416/); assert.ok(value.includes('0109123456789'));
    assert.ok(value.includes('5406123000')); assert.ok(value.includes('0808GAO2TEST'));
    let crc = 65535;
    for (const byte of Buffer.from(value.slice(0, -4))) { crc ^= byte << 8; for (let i = 0; i < 8; i++) crc = (crc & 32768) ? ((crc << 1) ^ 4129) & 65535 : (crc << 1) & 65535; }
    assert.equal(value.slice(-4), crc.toString(16).toUpperCase().padStart(4, '0'));
    assert.throws(() => core.vietQr('bad', '123', 1, 'GAO'), /không hợp lệ/);
});
test('bundled QR encoder creates the entire payment symbol without a network dependency', () => {
    const context = {}; vm.runInNewContext(fs.readFileSync(require.resolve('../../GaoApp.Web/wwwroot/Admin/js/pos/qrcodegen.js'), 'utf8'), context);
    const payload = core.vietQr('970416', '123456789', 50000, 'GAOTEST');
    const matrix = context.qrcodegen.QrCode.encodeText(payload, context.qrcodegen.QrCode.Ecc.QUARTILE);
    assert.ok(matrix.size >= 21); assert.equal(matrix.getModule(0, 0), true); assert.equal(matrix.getModule(6, 6), true);
});
