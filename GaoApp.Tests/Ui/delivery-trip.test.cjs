const test = require('node:test');
const assert = require('node:assert/strict');
const ui = require('../../GaoApp.Web/wwwroot/Admin/js/delivery.trip.js');
const inventory = require('../../GaoApp.Web/wwwroot/Admin/js/delivery.page.js');
const guid = '12345678-1234-1234-1234-123456789abc', version = 'AAAAAAAAAAc=';
function storage() { const data = new Map(); return { data, getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) }; }
function detail(state = 'Draft') { return { storeId: 6, tripId: 11, revision: 3, version, state, courierUserId: 8, serverTimeUtc: '2026-10-08T01:00:00Z',
    capabilities: { canEditDraft: true, canAbandonDraft: true, canHandover: true, canDepart: true, canReassignCourier: true, canRequestCancellation: true },
    orders: [{ deliveryOrderId: 21, version, state: state === 'Draft' ? 'ReadyForHandover' : state === 'Departed' ? 'Delivering' : 'HandedOver', isActive: true, canRequestCancellation: true, sourceWarehouseId: 3, sourceLegalEntityId: 4,
        lines: [{ lineId: 31, approvedBaseQuantityText: '2', dispatchedBaseQuantityText: '0', acceptedReturnedBaseQuantityText: '0', inCustodyBaseQuantityText: null }], custody: null }] }; }
function departure() { return ui.commandBody('depart', detail('HandedOver'), {}, guid, false, false); }
function ack(operation = 'depart') { return { clientRequestId: guid, tripId: 11, operation, appliedRevision: 4, appliedVersion: version, recordedAtUtc: '2026-10-08T01:00:00Z', replayed: true, orders: [{ deliveryOrderId: 21, revision: 6, version, state: 'Delivering' }] }; }
test('qty4 strings preserve maximum base quantities and no fifth digit or exponent is accepted', () => {
    assert.equal(ui.quantity('99999999999999.9999'), 999999999999999999n); assert.equal(ui.displayQuantity('99999999999999.9999'), '99.999.999.999.999,9999');
    for (const value of ['0.00001', '-1', '1e3', 2, '100000000000000']) assert.throws(() => ui.quantity(value));
});
test('semantic stop order stays ordered in create and draft bodies; duplicate and stale-version selection fails', () => {
    const rows = [{ deliveryOrderId: 2, version }, { deliveryOrderId: 1, version }];
    const body = ui.commandBody('create', null, { courierUserId: 8, orders: rows, note: '' }, guid, false, false);
    assert.deepEqual(body.orders.map(x => x.deliveryOrderId), [2, 1]); assert.equal(body.note, null);
    assert.throws(() => ui.selections([rows[0], rows[0]])); assert.throws(() => ui.selections([{ deliveryOrderId: 1, version: 'AA==' }])); assert.throws(() => ui.selections([]));
});
test('draft courier changes need explicit capability while same-courier refresh remains permitted', () => {
    const d = detail(); d.capabilities.canReassignCourier = false;
    assert.throws(() => ui.commandBody('draft', d, { courierUserId: 9, orders: d.orders, reason: 'Đổi người' }, guid, true, false), /quyền/);
    assert.equal(ui.commandBody('draft', d, { courierUserId: 8, orders: d.orders, reason: 'Làm mới' }, guid, true, false).courierUserId, 8);
});
test('planned assignment cannot claim physical custody; checkbox and exact named courier are mandatory', () => {
    for (const fields of [{ confirmedCourierUserId: 8, physicalReceiptConfirmed: false }, { confirmedCourierUserId: 9, physicalReceiptConfirmed: true }]) assert.throws(() => ui.commandBody('handover', detail(), fields, guid, false, false), /nhận hàng/);
    const body = ui.commandBody('handover', detail(), { confirmedCourierUserId: 8, physicalReceiptConfirmed: true }, guid, false, false);
    assert.equal(body.confirmationNote, null); assert.equal(body.physicalReceiptConfirmed, true); assert.equal('actorUserId' in body, false); assert.equal('confirmedAtUtc' in body, false);
});
test('dirty and pending guards precede intent creation for handover, departure and recall', () => {
    for (const operation of ['handover', 'depart', 'cancel-request']) { assert.throws(() => ui.commandBody(operation, detail(), {}, guid, true, false), /chưa lưu/); assert.throws(() => ui.commandBody(operation, detail(), {}, guid, false, true), /yêu cầu cũ/); }
});
test('physical wholetrip transfer cannot rename departed responsibility or use existing courier', () => {
    const fields = { newCourierUserId: 9, confirmedCourierUserId: 9, physicalReceiptConfirmed: true, reason: 'Đổi người giao', confirmationNote: '' };
    assert.equal(ui.commandBody('reassign', detail('HandedOver'), fields, guid, false, false).orders.length, 1);
    assert.throws(() => ui.commandBody('reassign', detail('Departed'), fields, guid, false, false));
    assert.throws(() => ui.commandBody('reassign', detail('HandedOver'), { ...fields, newCourierUserId: 8, confirmedCourierUserId: 8 }, guid, false, false));
});
test('recall after departure targets exactly one active order version and carries no stock or outcome facts', () => {
    const body = ui.commandBody('cancel-request', detail('Departed'), { orderId: 21, reason: 'Khách hủy' }, guid, false, false);
    assert.deepEqual(Object.keys(body).sort(), ['clientRequestId', 'expectedOrderVersion', 'expectedTripVersion', 'reason']);
    assert.throws(() => ui.commandBody('cancel-request', detail('Departed'), { orderId: 22, reason: 'Hủy' }, guid, false, false));
});
test('durable intent is Store/actor/origin scoped and reload never creates a new GUID/body/version', () => {
    const disk = storage(), first = ui.createIntentStore(disk, 'https://a.test', 6, 'actor'); const stored = first.begin(11, 'depart', departure()); first.sending(stored);
    const reloaded = ui.createIntentStore(disk, 'https://a.test', 6, 'actor'); assert.equal(reloaded.read().body, JSON.stringify(departure())); assert.equal(reloaded.read().uncertain, true);
    for (const args of [['https://b.test', 6, 'actor'], ['https://a.test', 7, 'actor'], ['https://a.test', 6, 'other']]) assert.equal(ui.createIntentStore(disk, ...args).read(), null);
    assert.throws(() => first.begin(12, 'depart', departure()));
});
test('lost-response samebody stale second-tab rejection retains durable uncertainty', () => {
    const disk = storage(), a = ui.createIntentStore(disk, 'https://a.test', 6, 'actor'), b = ui.createIntentStore(disk, 'https://a.test', 6, 'actor'); const original = a.begin(11, 'depart', departure()), stale = b.read();
    a.sending(original); const attempt = b.sending(stale); assert.equal(b.rejectFirstAttempt(attempt), false); assert.equal(a.read().body, original.body);
});
test('first definitive rejection can release only its owned attempt, never an arbitrary clone', () => {
    const disk = storage(), registry = ui.createIntentStore(disk, 'https://a.test', 6, 'actor'), original = registry.begin(11, 'depart', departure());
    assert.equal(registry.rejectFirstAttempt(original), false); assert.equal(registry.rejectFirstAttempt(registry.sending(original)), true); assert.equal(registry.read(), null);
});
test('malformed successful ACK keeps exact pending identity and requires typed per-order facts', () => {
    for (const replacement of [{ appliedRevision: 0 }, { appliedVersion: 'AA==' }, { recordedAtUtc: 'yesterday' }, { replayed: 'true' }, { tripId: 12 }, { orders: [{ deliveryOrderId: 21, revision: 0, version, state: 'Delivering' }] }, { orders: [...ack().orders, ...ack().orders] }]) {
        const disk = storage(), registry = ui.createIntentStore(disk, 'https://a.test', 6, 'actor'); const original = registry.begin(11, 'depart', departure()); assert.throws(() => registry.acknowledge({ ...ack(), ...replacement })); assert.equal(registry.read().body, original.body);
    }
});
test('create ACK assigns genuine new trip ID while operation/command mismatch cannot clear pending', () => {
    const disk = storage(), registry = ui.createIntentStore(disk, 'https://a.test', 6, 'actor'); registry.begin(null, 'create', ui.commandBody('create', null, { courierUserId: 8, orders: detail().orders }, guid, false, false));
    assert.throws(() => registry.acknowledge(ack())); assert.throws(() => registry.acknowledge({ ...ack('create'), tripId: 42 })); assert.equal(registry.acknowledge({ ...ack('create'), tripId: 42, orders: ack().orders.map(row => ({ ...row, state: 'ReadyForHandover' })) }), 42); assert.equal(registry.read(), null);
});
test('lost-response ACKs enforce the actual handover/recall states and abandonment has no active order result', () => {
    const disk = storage(), registry = ui.createIntentStore(disk, 'https://a.test', 6, 'actor'); const body = ui.commandBody('handover', detail(), { confirmedCourierUserId: 8, physicalReceiptConfirmed: true }, guid, false, false); registry.sending(registry.begin(11, 'handover', body));
    assert.throws(() => registry.acknowledge({ ...ack('handover'), orders: ack().orders.map(row => ({ ...row, state: 'ReadyForHandover' })) })); assert.equal(registry.read().uncertain, true); registry.acknowledge({ ...ack('handover'), orders: ack().orders.map(row => ({ ...row, state: 'HandedOver' })) });
    registry.begin(11, 'cancel-request', ui.commandBody('cancel-request', detail('Departed'), { orderId: 21, reason: 'Thu hồi' }, guid, false, false), 21); assert.throws(() => registry.acknowledge(ack('cancel-request'))); registry.acknowledge({ ...ack('cancel-request'), orders: ack().orders.map(row => ({ ...row, state: 'CancellationPending' })) });
    registry.begin(11, 'abandon', ui.commandBody('abandon', detail(), { reason: 'Bỏ chuyến' }, guid, false, false)); assert.throws(() => registry.acknowledge(ack('abandon'))); registry.acknowledge({ ...ack('abandon'), orders: [] }); assert.equal(registry.read(), null);
});
test('stored unknown fields, invalid confirmation and failed durable storage never send a request', () => {
    const disk = storage(), registry = ui.createIntentStore(disk, 'https://a.test', 6, 'actor'); assert.throws(() => registry.begin(11, 'depart', { ...departure(), actorUserId: 8 })); assert.equal(disk.data.size, 0);
    const broken = { getItem: () => null, setItem: () => {}, removeItem: () => {} }; assert.throws(() => ui.createIntentStore(broken, 'https://a.test', 6, 'actor').begin(11, 'depart', departure()));
});
test('resource paths distinguish create, trip transfer and perorder recall; invalid routes fail closed', () => {
    assert.equal(ui.intentPath({ tripId: null, operation: 'create' }), '/admin/api/delivery-trips');
    assert.equal(ui.intentPath({ tripId: 11, operation: 'reassign' }), '/admin/api/delivery-trips/11/courier/reassign');
    assert.equal(ui.intentPath({ tripId: 11, orderId: 21, operation: 'cancel-request' }), '/admin/api/delivery-trips/11/orders/21/cancel-request'); assert.throws(() => ui.intentPath({ tripId: 11, operation: 'dispatch' }));
});
test('projection separates unknown custody from dispatched quantity and checks scoped evidence', () => {
    const d = detail(); d.orders[0].lines[0].dispatchedBaseQuantityText = '2'; assert.equal(ui.validateDetail(d, 6).orders[0].lines[0].inCustodyBaseQuantityText, null);
    assert.throws(() => ui.validateDetail(d, 7)); d.orders[0].lines[0].inCustodyBaseQuantityText = '2'; assert.throws(() => ui.validateDetail(d, 6));
    d.orders[0].custody = { tripId: 11, courierUserId: 8, custodyEventId: 13 }; assert.equal(ui.validateDetail(d, 6).orders[0].lines[0].inCustodyBaseQuantityText, '2');
    d.orders[0].custody.courierUserId = 9; assert.throws(() => ui.validateDetail(d, 6));
});
test('delayed precommand GET and lower revisions cannot restore stale capabilities; same-version freshness can apply', () => {
    const current = detail(), newer = detail(); newer.revision++; assert.equal(ui.canApply(current, newer, 1, 2), false); assert.equal(ui.canApply(newer, current, 2, 2), false);
    current.capabilities.canDepart = false; assert.equal(ui.canApply(newer, { ...newer, capabilities: current.capabilities }, 3, 3), true);
    assert.equal(ui.canApply(null, current, 4, 4, { tripId: current.tripId, revision: newer.revision }), false);
});
test('elapsed is based on server clock plus monotonic time and never claims a location or outcome', () => {
    assert.equal(ui.elapsed('2026-10-08T02:00:00Z', '2026-10-08T01:00:00Z', 120000), '1 giờ 2 phút'); assert.equal(ui.elapsed('2026-10-08T00:00:00Z', '2026-10-08T01:00:00Z', 0), '0 giờ 0 phút'); assert.throws(() => ui.utc('2026-10-08T01:00:00+07:00'));
});
test('reason and optional attestation remain <=500 and cannot use whitespace-only required content', () => { assert.equal(ui.note('  ghi chú  '), 'ghi chú'); assert.equal(ui.note(' '), null); assert.throws(() => ui.note(' ', true)); assert.throws(() => ui.note('x'.repeat(501))); });
test('D05 quantity projection adds genuine custody context without authorizing costs or guessing unknown legacy responsibility', () => {
    const d = { delivery: { id: 21, revision: 3, version, sourceWarehouseId: 3, sourceLegalEntityId: 4 }, inventory: { storeId: 6, deliveryOrderId: 21, revision: 3, version, sourceWarehouseId: 3, sourceLegalEntityId: 4, serverTimeUtc: '2026-10-08T01:00:00Z', canViewCosts: false, canCancelBeforeHandover: false, costFragments: [],
        custody: { tripId: 11, tripState: 'Departed', custodyEventId: 13, custodyKind: 'Handover', courierUserId: 8, confirmedByUserId: 9, confirmedAtUtc: '2026-10-08T01:00:00Z', departedAtUtc: '2026-10-08T01:01:00Z' },
        lines: [{ lineId: 31, heldBaseQuantityText: '0', dispatchedBaseQuantityText: '2', acceptedReturnedBaseQuantityText: '0.5', stockOutsideBaseQuantityText: '1.5', inCustodyBaseQuantityText: '1.5', dispatchedCostText: null, acceptedReturnedCostText: null }] } };
    assert.equal(inventory.inventoryProjection(d, 6).custody.courierUserId, 8); d.inventory.custody = null; assert.throws(() => inventory.inventoryProjection(d, 6)); d.inventory.lines[0].inCustodyBaseQuantityText = null; assert.equal(inventory.inventoryProjection(d, 6).lines[0].inCustodyBaseQuantityText, null);
});
