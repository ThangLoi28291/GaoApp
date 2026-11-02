const test = require('node:test');
const assert = require('node:assert/strict');
const ui = require('../../GaoApp.Web/wwwroot/Admin/js/delivery.page.js');
function storage() {
    const data = new Map();
    return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key), data };
}
const guid = '12345678-1234-1234-1234-123456789abc';
const body = { clientRequestId: guid, expectedVersion: 'AAABBB==', reason: 'Khách hủy đơn' };
const ack = { clientRequestId: guid, deliveryOrderId: 31, operation: 'cancel', appliedRevision: 5, appliedVersion: 'AAAAAAAAAAc=', recordedAtUtc: '2026-10-07T04:00:00+00:00', replayed: true };
function detail() {
    return { delivery: { id: 31, revision: 4, version: 'AAABBB==', sourceWarehouseId: 7, sourceLegalEntityId: 8 }, capabilities: { canReport: false }, inventory: {
        storeId: 6, deliveryOrderId: 31, revision: 4, version: 'AAABBB==', sourceWarehouseId: 7, sourceLegalEntityId: 8,
        sourceWarehouseName: 'Kho <script>x</script>', sourceLegalEntityName: 'Chủ hàng', serverTimeUtc: '2026-10-07T04:00:00Z', canViewCosts: false, canCancelBeforeHandover: true,
        lines: [{ lineId: 2, itemName: 'Gạo', baseUnitName: 'kg', heldBaseQuantityText: '0', dispatchedBaseQuantityText: '8', acceptedReturnedBaseQuantityText: '2', stockOutsideBaseQuantityText: '6', inCustodyBaseQuantityText: null, dispatchedCostText: null, acceptedReturnedCostText: null }], costFragments: []
    } };
}
test('inventory accepts exact evidence and retains unknown custody separately from six outside source warehouse', () => {
    const d = detail(), inventory = ui.inventoryProjection(d, 6);
    assert.equal(inventory.lines[0].stockOutsideBaseQuantityText, '6'); assert.equal(inventory.lines[0].inCustodyBaseQuantityText, null);
    assert.equal(inventory.sourceWarehouseName, 'Kho <script>x</script>'); assert.equal(ui.inventoryProjection({ inventory: null }, 6), null);
});
test('inventory fails closed on Store, order, aggregate version and immutable source mismatch', () => {
    for (const field of ['storeId', 'deliveryOrderId', 'revision', 'sourceWarehouseId', 'sourceLegalEntityId']) {
        const d = detail(); d.inventory[field]++; assert.throws(() => ui.inventoryProjection(d, 6), /không khớp/);
    }
    const d = detail(); d.inventory.version = 'stale'; assert.throws(() => ui.inventoryProjection(d, 6));
    assert.throws(() => ui.inventoryProjection(detail(), 0));
});
test('inventory checks qty4 with BigInt at maximum precision and rejects fifth-digit and contradictory returns', () => {
    const d = detail(), line = d.inventory.lines[0]; line.dispatchedBaseQuantityText = '99999999999999.9999'; line.acceptedReturnedBaseQuantityText = '0.0001'; line.stockOutsideBaseQuantityText = '99999999999999.9998';
    assert.equal(ui.inventoryProjection(d, 6).lines[0].stockOutsideBaseQuantityText, '99999999999999.9998');
    line.acceptedReturnedBaseQuantityText = '0.00001'; assert.throws(() => ui.inventoryProjection(d, 6));
    line.acceptedReturnedBaseQuantityText = '0.0001'; line.stockOutsideBaseQuantityText = '99999999999999.9999'; assert.throws(() => ui.inventoryProjection(d, 6));
    line.dispatchedBaseQuantityText = '0'; assert.throws(() => ui.inventoryProjection(d, 6));
});
test('quantity viewers receive null costs and no fragments, while genuine final zero costs remain authorized data', () => {
    const d = detail(); d.inventory.lines[0].dispatchedCostText = '0'; assert.throws(() => ui.inventoryProjection(d, 6), /quyền/);
    d.inventory.lines[0].dispatchedCostText = null; d.inventory.costFragments = [{ costAmountText: '80000' }]; assert.throws(() => ui.inventoryProjection(d, 6), /quyền/);
    d.inventory.canViewCosts = true; d.inventory.costFragments = []; d.inventory.lines[0].dispatchedCostText = '0'; assert.equal(ui.inventoryProjection(d, 6).lines[0].dispatchedCostText, '0');
});
test('original provisional estimates and unknown zero estimates remain distinct from final zero and resolved source costs', () => {
    for (const [quality, label, layer, debit, amount] of [
        ['Final', 'Giá vốn đã chốt', 8, 9, '0'],
        ['Provisional', 'Giá vốn tạm tính', null, null, '15000'],
        ['Unknown', 'Chưa xác định giá vốn', null, null, '0'],
        ['Resolved', 'Nguồn đã xử lý; giữ số ghi nhận ban đầu', null, null, '15000']]) {
        const d = detail(); d.inventory.canViewCosts = true;
        const fragment = { fragmentId: 1, lineId: 2, inventoryCostLayerId: layer, inventoryCostBudgetDebitId: debit,
            costQuality: quality, costAmountText: amount, baseQuantityText: '3', unitCostText: quality === 'Unknown' ? '0' : '5000' };
        d.inventory.costFragments = [fragment];
        assert.equal(ui.inventoryProjection(d, 6).costFragments[0].costAmountText, amount);
        assert.equal(ui.costQualityLabel(fragment), label);
    }
});
test('fragment quality cannot manufacture a final layer or relabel incomplete source evidence as final', () => {
    for (const fragment of [
        { costQuality: 'Final', inventoryCostLayerId: null, inventoryCostBudgetDebitId: null },
        { costQuality: 'Final', inventoryCostLayerId: 7, inventoryCostBudgetDebitId: 0 },
        { costQuality: 'Provisional', inventoryCostLayerId: 7, inventoryCostBudgetDebitId: null },
        { costQuality: 'Resolved', inventoryCostLayerId: null, inventoryCostBudgetDebitId: 8 },
        { costQuality: 'Invented', inventoryCostLayerId: null, inventoryCostBudgetDebitId: null }]) {
        const d = detail(); d.inventory.canViewCosts = true; d.inventory.costFragments = [fragment];
        assert.throws(() => ui.inventoryProjection(d, 6));
    }
});
test('same-revision readiness, inventory changes and cost permission changes require rendering; clock-only polling preserves editing focus', () => {
    const current = detail(), next = detail(); next.inventory.serverTimeUtc = '2026-10-07T04:00:08Z';
    assert.equal(ui.projectionChanged(current, next, false), false);
    next.inventory.canCancelBeforeHandover = false; assert.equal(ui.projectionChanged(current, next, false), true);
    next.inventory.canCancelBeforeHandover = true; next.inventory.lines[0].heldBaseQuantityText = '1'; assert.equal(ui.projectionChanged(current, next, false), true);
    assert.equal(ui.projectionChanged(current, current, true), true);
});
test('duplicate evidence lines and malformed nullable custody fail closed rather than deriving custody', () => {
    const d = detail(); d.inventory.lines.push({ ...d.inventory.lines[0] }); assert.throws(() => ui.inventoryProjection(d, 6));
    d.inventory.lines.pop(); delete d.inventory.lines[0].inCustodyBaseQuantityText; assert.throws(() => ui.inventoryProjection(d, 6));
});
test('cancel dirty and pending guards run before a durable request can be created', () => {
    const disk = storage(), registry = ui.createInventoryIntentStore(disk, 'https://shop.test', 6, 'actor');
    for (const [dirty, blocked] of [[true, false], [false, true]]) assert.throws(() => registry.begin(31, 'cancel', ui.cancellationBody(detail(), 6, 'Hủy', guid, dirty, blocked)));
    assert.equal(disk.data.size, 0);
    const d = detail(); d.inventory.canCancelBeforeHandover = false; assert.throws(() => ui.cancellationBody(d, 6, 'Hủy', guid, false, false));
});
test('cancel requires a trimmed reason and exact current version without stock or cost override fields', () => {
    assert.deepEqual(ui.cancellationBody(detail(), 6, '  Khách hủy đơn  ', guid, false, false), body);
    assert.equal(ui.cancellationBody(detail(), 6, 'a'.repeat(500), guid, false, false).reason.length, 500);
    for (const reason of ['', '  ', 'a'.repeat(501)]) assert.throws(() => ui.cancellationBody(detail(), 6, reason, guid, false, false));
});
test('lost cancellation response survives reload with identical GUID/body/version and never automatically sends', () => {
    const disk = storage(), registry = ui.createInventoryIntentStore(disk, 'https://shop.test', 6, 'actor');
    const intent = registry.begin(31, 'cancel', body); registry.sending(intent);
    const reloaded = ui.createInventoryIntentStore(disk, 'https://shop.test', 6, 'actor').read();
    assert.equal(reloaded.body, JSON.stringify(body)); assert.equal(reloaded.uncertain, true); assert.equal(reloaded.storeId, 6); assert.equal(reloaded.actorId, 'actor');
    assert.equal(ui.intentPath(reloaded), '/admin/api/deliveries/31/inventory/cancel');
});
test('new cancellation partitions origin, Store and authenticated actor on a shared computer', () => {
    const disk = storage(); ui.createInventoryIntentStore(disk, 'https://shop.test', 6, 'actor').begin(31, 'cancel', body);
    for (const args of [['https://shop.test', 7, 'actor'], ['https://shop.test', 6, 'other'], ['https://other.test', 6, 'actor']]) assert.equal(ui.createInventoryIntentStore(disk, ...args).read(), null);
});
test('D04 v1 pending report remains byte-compatible alongside a Store-scoped cancellation registry', () => {
    const disk = storage(), picking = ui.createIntentStore(disk, 'https://shop.test:actor'), report = { clientRequestId: guid, expectedVersion: 'old', lines: [{ lineId: 2, pickedQuantityText: '0.0001', shortageReason: 'Thiếu' }] };
    const intent = picking.begin(31, 'report', report); picking.sending(intent);
    assert.equal(ui.createInventoryIntentStore(disk, 'https://shop.test', 6, 'actor').read(), null);
    const legacy = ui.createIntentStore(disk, 'https://shop.test:actor').read(); assert.equal(legacy.body, JSON.stringify(report)); assert.equal(ui.intentPath(legacy), '/admin/api/deliveries/31/picking/report');
});
test('fixed resource route rejects forged operations and arbitrary endpoint fields', () => {
    for (const intent of [{ deliveryId: 31, resource: 'inventory', operation: 'dispatch' }, { deliveryId: 31, resource: 'stock', operation: 'cancel' }, { deliveryId: 31, operation: 'cancel' }, { deliveryId: -1, operation: 'report' }]) assert.throws(() => ui.intentPath(intent));
    const registry = ui.createInventoryIntentStore(storage(), 'https://shop.test', 6, 'actor');
    assert.throws(() => registry.begin(31, 'cancel', { ...body, warehouseId: 7 })); assert.throws(() => registry.begin(31, 'dispatch', body)); assert.equal(registry.read(), null);
});
test('replayed cancellation ACK must match order/GUID/operation and cannot clear a later request', () => {
    const registry = ui.createInventoryIntentStore(storage(), 'https://shop.test', 6, 'actor'); registry.begin(31, 'cancel', body);
    for (const wrong of [{ ...ack, deliveryOrderId: 32 }, { ...ack, operation: 'report' }, { ...ack, clientRequestId: 'wrong' }]) { assert.throws(() => registry.acknowledge(wrong)); assert.ok(registry.read()); }
    registry.acknowledge(ack); assert.equal(registry.read(), null);
    registry.begin(31, 'cancel', { ...body, clientRequestId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', expectedVersion: 'new' }); assert.throws(() => registry.acknowledge(ack)); assert.equal(JSON.parse(registry.read().body).expectedVersion, 'new');
});
test('only a first certain rejection clears a cancellation; later permission/state rejection retains uncertain identity', () => {
    const registry = ui.createInventoryIntentStore(storage(), 'https://shop.test', 6, 'actor'), first = registry.begin(31, 'cancel', body), firstAttempt = registry.sending(first);
    assert.equal(registry.rejectFirstAttempt(firstAttempt), true);
    const next = registry.begin(31, 'cancel', body); registry.sending(next); const retry = registry.sending(registry.read()); assert.equal(registry.rejectFirstAttempt(retry), false); assert.equal(registry.read().body, JSON.stringify(body));
});
test('two tabs with the same stale certain clone cannot erase a preceding lost committed cancellation after rejection', () => {
    const disk = storage(), tabA = ui.createInventoryIntentStore(disk, 'https://shop.test', 6, 'actor'), tabB = ui.createInventoryIntentStore(disk, 'https://shop.test', 6, 'actor');
    const original = tabA.begin(31, 'cancel', body), staleClone = tabB.read(); assert.equal(staleClone.uncertain, false);
    tabA.sending(original); // Server committed, but tab A never received its response.
    const rejectedRetry = tabB.sending(staleClone); assert.equal(tabB.rejectFirstAttempt(staleClone), false); assert.equal(tabB.rejectFirstAttempt(rejectedRetry), false);
    assert.equal(tabA.read().body, JSON.stringify(body)); assert.equal(tabA.read().uncertain, true);
});
test('malformed HTTP200 cancellation ACK retains durable identity until complete typed version/time acknowledgment arrives', () => {
    const registry = ui.createInventoryIntentStore(storage(), 'https://shop.test', 6, 'actor'); const intent = registry.begin(31, 'cancel', body); registry.sending(intent);
    for (const malformed of [{ ...ack, appliedRevision: 0 }, { ...ack, appliedRevision: 1.5 }, { ...ack, appliedVersion: '' }, { ...ack, appliedVersion: 'AAAAAAAAAAcA' }, { ...ack, recordedAtUtc: null }, { ...ack, recordedAtUtc: '2026-10-07T04:00:00' }, { ...ack, recordedAtUtc: '2026-99-07T04:00:00Z' }, { ...ack, replayed: 'true' }]) {
        assert.throws(() => registry.acknowledge(malformed)); assert.equal(registry.read().body, JSON.stringify(body)); assert.equal(registry.read().uncertain, true);
    }
    registry.acknowledge(ack); assert.equal(registry.read(), null);
});
test('storage corruption or failed persistence prevents cancellation rather than replacing the pending request', () => {
    const disk = storage(), registry = ui.createInventoryIntentStore(disk, 'https://shop.test', 6, 'actor'); registry.begin(31, 'cancel', body);
    const key = [...disk.data.keys()][0]; disk.data.set(key, '{invalid'); assert.throws(() => registry.read()); assert.throws(() => registry.begin(32, 'cancel', body)); assert.equal(disk.getItem(key), '{invalid');
    const broken = { getItem: () => null, setItem: () => { throw new Error('disk full'); }, removeItem: () => {} }; assert.throws(() => ui.createInventoryIntentStore(broken, 'https://shop.test', 6, 'actor').begin(31, 'cancel', body), /disk full/);
});
test('cross-tab changed scope or body is rejected before retry; pending cancellation blocks another order', () => {
    const disk = storage(), registry = ui.createInventoryIntentStore(disk, 'https://shop.test', 6, 'actor'), intent = registry.begin(31, 'cancel', body);
    assert.throws(() => registry.begin(32, 'cancel', body)); const key = [...disk.data.keys()][0]; disk.data.set(key, JSON.stringify({ ...intent, body: JSON.stringify({ ...body, expectedVersion: 'changed' }) })); assert.throws(() => registry.sending(intent), /tab khác/);
    disk.data.set(key, JSON.stringify({ ...intent, storeId: 7 })); assert.throws(() => registry.read());
});
