const test = require('node:test');
const assert = require('node:assert/strict');
const ui = require('../../GaoApp.Web/wwwroot/Admin/js/delivery.page.js');

function storage() {
    const data = new Map();
    return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key), data };
}
const identity = '12345678-1234-1234-1234-123456789abc';
const body = { clientRequestId: identity, expectedVersion: 'AAAAAAAAAAY=', lines: [{ lineId: 17, pickedQuantityText: '99999999999999.9999', shortageReason: 'Còn đúng lượng này' }] };
const ack = { clientRequestId: identity, deliveryOrderId: 31, operation: 'report', appliedRevision: 4, appliedVersion: 'AAAAAAAAAAc=', replayed: false };

test('UTC foundation timestamps without an offset retain the same instant as DateTimeOffset picking timestamps', () => {
    assert.equal(ui.parseUtcDate('2026-10-07T02:24:49.123').toISOString(), '2026-10-07T02:24:49.123Z');
    assert.equal(ui.parseUtcDate('2026-10-07T02:24:49.123Z').toISOString(), '2026-10-07T02:24:49.123Z');
    assert.equal(ui.parseUtcDate('2026-10-07T09:24:49.123+07:00').toISOString(), '2026-10-07T02:24:49.123Z');
    assert.throws(() => ui.parseUtcDate('not a timestamp'));
});

test('a delayed earlier GET cannot replace a newer acknowledged revision even when its old capability allows picking', () => {
    const current = { delivery: { id: 31, revision: 7, version: 'newer' }, capabilities: { canReport: false } };
    const stale = { delivery: { id: 31, revision: 6, version: 'older' }, capabilities: { canReport: true } };
    assert.equal(ui.canApplyProjection(current, stale, 9, 9), false);
    assert.equal(ui.canApplyProjection(current, { ...stale, delivery: { ...stale.delivery, revision: 8 } }, 9, 9), true);
});
test('read epochs invalidate precommand, previous lookup and older same-revision responses', () => {
    const current = { delivery: { id: 31, revision: 7 } }, incoming = { delivery: { id: 31, revision: 7 } };
    assert.equal(ui.canApplyProjection(current, incoming, 8, 9), false);
    assert.equal(ui.canApplyProjection(current, incoming, 9, 9), true);
    assert.equal(ui.canApplyProjection(current, { delivery: { id: 32, revision: 100 } }, 9, 9), false);
});

test('qty4 retains the maximum persisted quantity beyond JavaScript safe integer precision', () => {
    assert.equal(ui.scaledQuantity('99999999999999.9999'), 999999999999999999n);
    assert.equal(ui.displayExact('99999999999999.9999'), '99.999.999.999.999,9999');
    assert.equal(ui.scaledQuantity('0.0001'), 1n);
});
test('quantity input rejects fifth digits, exponent, negative, whitespace and localized separators', () => {
    for (const value of ['0.00001', '1e2', '-1', ' 1', '1 ', '1,25', '1.000.000', '', '100000000000000']) assert.throws(() => ui.scaledQuantity(value));
    assert.throws(() => ui.scaledQuantity(1));
});
test('unit price and whole dong display preserve exact strings without binary rounding', () => {
    assert.equal(ui.displayExact('1000.25', 2), '1.000,25');
    assert.equal(ui.displayExact('0.26', 2), '0,26');
    assert.equal(ui.displayExact('12.00', 2), '12,00');
    assert.equal(ui.money('9999999999999999'), '9.999.999.999.999.999 đ');
});
test('blank result is unreported and explicit zero requires a shortage reason', () => {
    const line = { lineId: 3, itemName: 'Gạo', plannedQuantityText: '2' };
    assert.throws(() => ui.reportLine(line, '', ''));
    assert.throws(() => ui.reportLine(line, '0', ''));
    assert.deepEqual(ui.reportLine(line, '0', '  Hết hàng  '), { lineId: 3, pickedQuantityText: '0', shortageReason: 'Hết hàng' });
});
test('partial picking needs reason and cannot exceed planned quantity at exact qty4 bounds', () => {
    const line = { lineId: 3, itemName: 'Gạo', plannedQuantityText: '99999999999999.9998' };
    assert.throws(() => ui.reportLine(line, '99999999999999.9999', 'Đủ'));
    assert.throws(() => ui.reportLine(line, '99999999999999.9997', ''));
    assert.equal(ui.reportLine(line, '99999999999999.9997', 'Thiếu 0.0001').pickedQuantityText, '99999999999999.9997');
});
test('full picking retains exact quantity input and omits an empty reason', () => {
    assert.deepEqual(ui.reportLine({ lineId: 2, itemName: 'Nước', plannedQuantityText: '1.2345' }, '1.2345', ''), { lineId: 2, pickedQuantityText: '1.2345', shortageReason: null });
});
test('QR parsing keeps printed revision and rejects another origin or unrelated route', () => {
    assert.deepEqual(ui.parseLookup('https://shop.test/admin/deliveries?key=stable&revision=2', 'https://shop.test'), { key: 'stable', revision: '2' });
    assert.deepEqual(ui.parseLookup(' GH-123 ', 'https://shop.test'), { key: 'GH-123', revision: null });
    assert.throws(() => ui.parseLookup('https://other.test/admin/deliveries?key=stable', 'https://shop.test'));
    assert.throws(() => ui.parseLookup('https://shop.test/admin/pos?key=stable', 'https://shop.test'));
});
test('printed revisions reject invalid values rather than approximating them', () => {
    for (const value of ['0', '-2', '1.5', '1e2', '1000000000']) assert.equal(ui.validRevision(value), null);
    assert.equal(ui.validRevision('42'), '42');
});
test('reload retries the identical request string, GUID and rowversion after response uncertainty', () => {
    const disk = storage(), first = ui.createIntentStore(disk, 'https://shop.test:employee-1');
    const intent = first.begin(31, 'report', body); first.sending(intent);
    const reloaded = ui.createIntentStore(disk, 'https://shop.test:employee-1').read();
    assert.equal(reloaded.body, JSON.stringify(body)); assert.equal(reloaded.uncertain, true);
    assert.equal(JSON.parse(reloaded.body).lines[0].pickedQuantityText, '99999999999999.9999');
    assert.equal(JSON.parse(reloaded.body).expectedVersion, 'AAAAAAAAAAY=');
});
test('pending request blocks a new intent, including another order', () => {
    const store = ui.createIntentStore(storage(), 'shop:actor'); store.begin(31, 'report', body);
    assert.throws(() => store.begin(32, 'claim', { clientRequestId: identity, expectedVersion: 'other' }), /yêu cầu chưa xác nhận/);
});
test('acknowledgment must match all order, actor-scoped GUID and operation identities', () => {
    const store = ui.createIntentStore(storage(), 'shop:actor'); store.begin(31, 'report', body);
    for (const wrong of [{ ...ack, clientRequestId: 'wrong' }, { ...ack, deliveryOrderId: 32 }, { ...ack, operation: 'submit' }]) {
        assert.throws(() => store.acknowledge(wrong)); assert.ok(store.read());
    }
    store.acknowledge(ack); assert.equal(store.read(), null);
});
test('late duplicate ack cannot clear a newer intent after the original request was acknowledged', () => {
    const store = ui.createIntentStore(storage(), 'shop:actor'); store.begin(31, 'report', body);
    store.acknowledge({ ...ack, replayed: true });
    const newerBody = { clientRequestId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', expectedVersion: 'newer-version' };
    store.begin(31, 'submit', newerBody); assert.throws(() => store.acknowledge(ack));
    assert.equal(JSON.parse(store.read().body).clientRequestId, newerBody.clientRequestId);
});
test('fresh rejection releases first unconfirmed intent; uncertain retry never abandons its identity', () => {
    const store = ui.createIntentStore(storage(), 'shop:actor'); const first = store.begin(31, 'report', body); store.sending(first);
    assert.equal(store.rejectFirstAttempt(first), true); assert.equal(store.read(), null);
    const second = store.begin(31, 'report', body); const uncertain = store.sending(second);
    assert.equal(store.rejectFirstAttempt(uncertain), false); assert.equal(store.read().body, JSON.stringify(body));
});
test('Store origin and actor partition pending requests on a shared browser', () => {
    const disk = storage(); ui.createIntentStore(disk, 'https://one.test:actor-1').begin(31, 'report', body);
    assert.equal(ui.createIntentStore(disk, 'https://one.test:actor-2').read(), null);
    assert.equal(ui.createIntentStore(disk, 'https://two.test:actor-1').read(), null);
    assert.ok(ui.createIntentStore(disk, 'https://one.test:actor-1').read());
});
test('storage failure prevents creating an intent that could be sent without persistence', () => {
    const disk = { getItem: () => null, setItem: () => { throw new Error('disk full'); }, removeItem: () => {} };
    assert.throws(() => ui.createIntentStore(disk, 'shop:actor').begin(31, 'report', body), /disk full/);
    assert.throws(() => ui.createIntentStore(storage(), ''));
});
test('corrupt durable intent fails closed and is not overwritten', () => {
    const disk = storage(); disk.data.set('gao.delivery.picking.v1:shop:actor', '{invalid');
    const store = ui.createIntentStore(disk, 'shop:actor'); assert.throws(() => store.read());
    assert.throws(() => store.begin(31, 'report', body)); assert.equal(disk.getItem('gao.delivery.picking.v1:shop:actor'), '{invalid');
});
test('cross-tab changed pending body cannot be sent as the older request', () => {
    const disk = storage(), store = ui.createIntentStore(disk, 'shop:actor'), intent = store.begin(31, 'report', body);
    disk.data.set('gao.delivery.picking.v1:shop:actor', JSON.stringify({ ...intent, body: JSON.stringify({ ...body, expectedVersion: 'changed' }) }));
    assert.throws(() => store.sending(intent), /tab khác/);
});
