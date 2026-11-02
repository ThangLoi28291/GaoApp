const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const policy = require('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.privacy.js');
const memoryDatabase = require('./helpers/pos-offline-indexeddb.cjs');

function fixture(changes = {}, controls = {}) {
    const context = { storeId: 1, terminalId: 2, userId: 3, shiftId: 4, deviceBinding: 'device-A',
        expiresAtUtc: new Date(Date.now() + 3600000).toISOString(), antiForgeryToken: 'do-not-retain' };
    const state = { key: '1:2:3:4', context, privacyVersion: 1, privacyRevision: 'proof-A',
        queue: [], quarantined: [], orders: {}, maps: { order: {}, customer: {} }, ...changes };
    const stores = new Map([
        ['sessions', new Map([[state.key, structuredClone(state)]])],
        ['catalogs', new Map([[state.key, { products: [{ id: 10 }], customers: [
            { customerId: 9, name: 'Attached', phone: '0900000001', address: 'unnecessary address', priceTier: 'WHOLESALE' },
            { customerId: 8, name: 'Unrelated', phone: '0900000002', address: 'other address' }], promotions: [] }]])],
        ['meta', new Map([['auth', { key: state.key, revision: state.privacyRevision, deviceBinding: context.deviceBinding, locked: false }],
            ['active', { key: state.key, revision: state.privacyRevision, privacyVersion: 1, expiresAt: state.context.expiresAtUtc }]])]
    ]);
    const db = memoryDatabase(stores, controls);
    const runtime = vm.createContext({ structuredClone, Date, crypto: controls.crypto || require('node:crypto').webcrypto });
    vm.runInContext(fs.readFileSync(require.resolve('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.privacy.js'), 'utf8'), runtime);
    return { api: runtime.PosOfflinePrivacy, state, stores, db, controls, saved: () => stores.get('sessions').get(state.key),
        catalog: () => stores.get('catalogs').get(state.key) };
}

test('only attached or pending customers remain; cached addresses and unrelated customers are removed', async () => {
    const f = fixture({ orders: { 5: { orderId: 5, status: 0, customerId: 9, customerName: 'Attached', customerPhone: '0900000001', customerPriceTier: 'WHOLESALE' } } });
    await f.api.sweep(f.db);
    assert.deepEqual(f.catalog().customers.map(x => x.customerId), [9]);
    assert.equal(f.catalog().customers[0].priceTier, 'WHOLESALE');
    assert.equal(f.catalog().customers[0].address, undefined);
    assert.deepEqual(f.catalog().products, [{ id: 10 }]);
});

for (const reason of ['logout', 'unauthorized']) test(`${reason} removes clean sessions and orphan catalogs`, async () => {
    const f = fixture(); f.stores.get('catalogs').set('orphan', { customers: [{ name: 'orphan pii' }] });
    await f.api.sweep(f.db, { reason });
    assert.equal(f.stores.get('sessions').size, 0); assert.equal(f.stores.get('catalogs').size, 0);
    assert.equal(f.stores.get('meta').has('active'), false); assert.equal((await f.api.auth(f.db)).locked, true);
});

for (const expiresAt of ['2000-01-01T00:00:00', 'invalid', undefined]) test(`expiry ${expiresAt} fails closed and removes a synchronized cache`, async () => {
    const f = fixture(); f.stores.get('sessions').get(f.state.key).context.expiresAtUtc = expiresAt;
    await f.api.sweep(f.db);
    assert.equal(f.saved(), undefined); assert.equal(f.catalog(), undefined);
});

for (const changes of [
    { queue: [{ id: 'durable-payment', url: '/admin/pos/5/payments', body: { amount: 50 }, localResult: { orderId: 5, customerId: 9 } }] },
    { quarantined: [{ id: 'review-payment', body: { amount: 50 }, url: '/admin/pos/5/payments' }] },
    { conflict: { operationId: 'review' } },
    { qrs: { 7: { id: 7, orderId: 5, offline: true, status: 0 } } },
    { qrs: { 8: { id: 8, orderId: 5, manualRequested: true, status: 0 } } },
    { orders: { 5: { orderId: 5, status: 2, offline: true, invoiceIntentRequired: true } } },
    { orders: { 1500000001: { orderId: 1500000001, status: 0, lines: [{ quantity: 2 }] } } }
]) test(`cleanup retains unresolved work: ${Object.keys(changes)[0]}`, async () => {
    const f = fixture(changes), before = structuredClone(changes);
    await f.api.sweep(f.db, { reason: 'logout' });
    assert.ok(f.saved()); assert.equal(f.saved().privacyLocked, true);
    for (const [field, value] of Object.entries(before)) assert.deepEqual(f.saved()[field], value);
    assert.equal(f.saved().context.antiForgeryToken, undefined);
    assert.equal(f.stores.get('meta').has('active'), false);
});

test('changing context removes clean previous sessions and locks pending previous sessions', async () => {
    const f = fixture({ queue: [{ id: 'original', url: '/admin/pos/5/note' }] });
    f.stores.get('sessions').set('other', { ...structuredClone(f.state), key: 'other', queue: [] });
    await f.api.sweep(f.db, { keepKey: 'new-shift' });
    assert.equal(f.stores.get('sessions').has('other'), false); assert.equal(f.saved().privacyLocked, true);
    assert.equal(f.saved().queue[0].id, 'original');
});

test('a tab with an old authorization cannot recreate data after logout', async () => {
    const f = fixture(); const stale = structuredClone(f.state);
    await f.api.sweep(f.db, { reason: 'logout' });
    await assert.rejects(f.api.commit(f.db, stale, { customers: [] }), /khóa/);
    assert.equal(f.stores.get('sessions').size, 0);
});

test('a bootstrap started before logout cannot unlock the new authentication fence', async () => {
    const f = fixture(), beforeRequest = await f.api.auth(f.db);
    await f.api.sweep(f.db, { reason: 'logout' });
    await assert.rejects(f.api.authorize(f.db, beforeRequest, f.state.context), /thay đổi/);
    assert.equal((await f.api.auth(f.db)).locked, true);
});

test('reauthentication binds the same journal to a fresh verified device generation without changing its operations', async () => {
    const f = fixture({ queue: [{ id: 'retain-id', url: '/admin/pos/5/payments', body: { amount: 50 } }] });
    await f.api.sweep(f.db, { reason: 'logout' });
    const proof = await f.api.authorize(f.db, await f.api.auth(f.db), { ...f.state.context, deviceBinding: 'device-B' });
    const restored = structuredClone(f.saved()); restored.context.deviceBinding = 'device-B'; restored.privacyRevision = proof.revision; restored.privacyLocked = false;
    await f.api.commit(f.db, restored, f.catalog());
    assert.equal(f.saved().queue[0].id, 'retain-id'); assert.equal(f.saved().context.antiForgeryToken, undefined);
    assert.equal(f.stores.get('meta').get('active').revision, proof.revision);
});

test('storage failure rolls back deletion and preserves the original payment journal', async () => {
    const f = fixture({ queue: [{ id: 'important', url: '/admin/pos/5/payments', body: { amount: 50 } }] });
    const before = structuredClone(f.stores); f.controls.failSave = true;
    await assert.rejects(f.api.sweep(f.db, { reason: 'logout' }));
    assert.deepEqual(f.stores, before);
});

test('malformed legacy journals and their catalog are retained but cannot provide an offline shell', async () => {
    const f = fixture(); f.stores.get('sessions').set(f.state.key, { unexpectedLegacyPayment: 50 });
    await f.api.sweep(f.db, { reason: 'logout' });
    assert.deepEqual(f.saved(), { unexpectedLegacyPayment: 50 }); assert.ok(f.catalog());
    assert.equal(f.stores.get('meta').has('active'), false);
});

test('an address is removed from a confirmed customer but retained in an unacknowledged create command', async () => {
    const f = fixture({ customers: [{ customerId: 9, name: 'Attached', address: 'discard' }],
        orders: { 5: { orderId: 5, status: 0, customerId: 9, customerName: 'Attached' } } });
    await f.api.sweep(f.db);
    assert.equal(f.saved().customers[0].address, undefined);
});

test('a commit cannot replace another cashier or device lease even with a matching journal key', async () => {
    const f = fixture();
    f.stores.get('meta').get('auth').deviceBinding = 'different-device';
    await assert.rejects(f.api.commit(f.db, f.state, f.catalog()), error => error.privacyLock === true);
});

test('new offline customer addresses remain in the unsynced command needed to create the customer', async () => {
    const op = { id: 'new-customer', url: '/admin/pos/customers/quick-create', body: { name: 'New', address: 'delivery address' } };
    const f = fixture({ queue: [op], customers: [{ customerId: 1500000001, name: 'New', address: 'delivery address' }] });
    await f.api.sweep(f.db, { reason: 'logout' });
    assert.deepEqual(f.saved().queue[0], op); assert.equal(f.catalog().customers[0].address, undefined);
});

test('UTC timestamps with and without an explicit zone have the same deadline', () => {
    assert.equal(policy.utc('2026-10-22T10:00:00'), policy.utc('2026-10-22T10:00:00Z'));
});

test('cleanup also works on HTTP admin pages where crypto.randomUUID is unavailable', async () => {
    const webcrypto = require('node:crypto').webcrypto;
    const f = fixture({}, { crypto: { getRandomValues: values => webcrypto.getRandomValues(values) } });
    await f.api.sweep(f.db, { reason: 'logout' });
    const authentication = await f.api.auth(f.db);
    assert.equal(authentication.locked, true); assert.match(authentication.revision, /^[a-f0-9]{32}$/);
});

for (const logout of [false, true]) test(`submit cleanup tolerates named method/action controls (${logout ? 'logout' : 'payment'})`, async () => {
    const listeners = {};
    let prevented = false, submitted = 0;
    const window = { location: { href: 'https://store.example/admin/pos', origin: 'https://store.example', pathname: '/admin/pos' },
        navigator: {}, document: { addEventListener: (name, handler) => { listeners[name] = handler; } },
        addEventListener() {}, dispatchEvent() {}, localStorage: { setItem() {} } };
    const runtime = vm.createContext({ window, URL, Date, crypto: require('node:crypto').webcrypto,
        CustomEvent: function () {}, HTMLFormElement: { prototype: { submit() { submitted++; } } } });
    vm.runInContext(fs.readFileSync(require.resolve('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.privacy.js'), 'utf8'), runtime);
    const form = { tagName: 'FORM', method: {}, action: {}, getAttribute: name =>
        name === 'method' ? 'POST' : logout ? '/Account/Logout' : '/admin/customer-deposits/collect' };
    assert.doesNotThrow(() => listeners.submit({ target: form, preventDefault() { prevented = true; } }));
    await new Promise(setImmediate);
    assert.equal(prevented, logout);
    assert.equal(submitted, logout ? 1 : 0);
});
