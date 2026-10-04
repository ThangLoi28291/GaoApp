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

test('customer reprice retains 24-bottle carton price for both tiers and respects keep-price offline', () => {
    const { catalog, act } = fixture();
    catalog.customers.push({ customerId: 8, name: 'Khách lẻ', priceTier: 'RETAIL' });
    catalog.products[0].units.push({ id: 16, unitId: 17, unitName: 'Thùng', factor: 24,
        price: 100000, wholesalePrice: 96000, barcodes: ['CARTON24'] });
    const draft = act('/admin/pos/cart/current/scan', { barcode: '123456', qty: 24 });
    assert.equal(draft.grandTotal, 100000);
    assert.equal(act('/admin/pos/cart/current/customer/8', { repriceExistingLines: true }).grandTotal, 100000);
    assert.equal(act('/admin/pos/cart/current/customer/9', { repriceExistingLines: true }).grandTotal, 96000);
    assert.equal(act('/admin/pos/cart/current/customer/8', { repriceExistingLines: true }).grandTotal, 100000);
    assert.equal(act('/admin/pos/cart/current/customer/9', { repriceExistingLines: false }).grandTotal, 100000);
    assert.equal(act(`/admin/pos/lines/${draft.lines[0].lineId}?qty=48`, {}, 'PATCH').grandTotal, 192000);
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

test('offline transfer records full surplus through retries and reload, while card remains capped', () => {
    const { state, act } = fixture();
    const draft = act('/admin/pos/cart/current/scan', { barcode: '123456' });
    assert.throws(() => act('/admin/pos/cart/current/payments', { clientRequestId: randomUUID(), method: 2, amount: 12500 }), /vượt số còn thiếu/);
    const body = { orderId: draft.orderId, clientRequestId: randomUUID(), bankAccountId: 1, amount: 12500, referenceCode: 'GAOOVERPAID' };
    const collection = { ...body, method: 1, provider: 'OFFLINE-MANUAL' };
    act('/admin/pos/cart/current/payments', collection);
    act('/admin/pos/cart/current/payments', collection);
    act('/admin/pos/offline/manual-transfer', body);
    const saved = JSON.parse(JSON.stringify(state)).orders[draft.orderId];
    assert.equal(saved.paidTotal, 12500); assert.equal(saved.balanceDue, 0); assert.equal(saved.changeDue, 2500);
    assert.equal(saved.payments.length, 1); assert.equal(saved.payments[0].amount, 12500);
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
test('offline quantity updates reject values that can overflow server money columns', () => {
    const { act } = fixture();
    const draft = act('/admin/pos/cart/current/scan', { barcode: '123456' });
    assert.throws(() => act(`/admin/pos/lines/${draft.lines[0].lineId}?qty=138935001002581`, {}, 'PATCH'), /tối đa 1\.000\.000/);
    assert.equal(draft.lines[0].quantity, 1);
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


function completedIntentFixture(method = 'cash') {
    const f = fixture();
    f.act('/admin/pos/cart/current/scan', { barcode: '123456' });
    const id = f.state.currentId;
    f.act(method === 'cash' ? '/admin/pos/cart/current/payment-and-finalize' : '/admin/pos/offline/manual-transfer',
        { orderId: id, clientRequestId: randomUUID(), method: 0, amount: 10000, bankAccountId: 1, referenceCode: 'OFF' });
    return { ...f, id };
}
for (const method of ['cash', 'transfer']) for (const route of [1, 2])
test(`invoice intent ${method} persists choice ${route} after finalize and reload without resetting completion time`, () => {
    const { state, catalog, id } = completedIntentFixture(method);
    const completed = state.orders[id].completedAtUtc;
    assert.equal(core.pendingInvoiceIntent(state), id);
    const op = { id: randomUUID(), url: `/admin/pos/${id}/invoice-route`, method: 'POST', body: { route }, occurredAt: '2026-09-09T04:00:00Z' };
    const result = core.apply(state, catalog, op);
    assert.equal(result.success, true); assert.equal(result.pendingSync, true);
    const reloaded = JSON.parse(JSON.stringify(state));
    assert.equal(core.pendingInvoiceIntent(reloaded), null);
    assert.equal(reloaded.orders[id].invoiceIntent.route, route);
    assert.equal(reloaded.orders[id].completedAtUtc, completed);
    core.apply(reloaded, catalog, { ...op, id: randomUUID() });
    assert.equal(reloaded.orders[id].invoiceIntent.operationId, op.id);
    assert.throws(() => core.apply(reloaded, catalog, { ...op, body: { route: route === 1 ? 2 : 1 } }), /quản lý/);
});

test('invoice intent blocks incomplete, invalid, foreign-context and unauthorized selections', () => {
    const f = fixture(), id = f.state.currentId;
    const op = { url: `/admin/pos/${id}/invoice-route`, method: 'POST', body: { route: 1 } };
    assert.throws(() => core.apply(f.state, f.catalog, op), /hoàn tất/);
    const ready = completedIntentFixture(); op.url = `/admin/pos/${ready.id}/invoice-route`;
    assert.throws(() => core.apply(ready.state, ready.catalog, { ...op, body: { route: 0 } }), /không hợp lệ/);
    for (const field of ['storeId', 'terminalId', 'userId', 'shiftId']) {
        const foreign = core.clone(ready.state); foreign.context[field]++;
        assert.throws(() => core.apply(foreign, ready.catalog, op), /phiên POS/);
    }
    ready.state.context.permissions = [];
    assert.throws(() => core.apply(ready.state, ready.catalog, op), /quyền/);
});

test('invoice intent restores old completed journals and blocks another sale until the missing choice is saved', () => {
    const { state, catalog, id, act } = completedIntentFixture();
    delete state.orders[id].invoiceIntentRequired; // Old journal schema; retain its completed offline sale.
    assert.equal(core.pendingInvoiceIntent(JSON.parse(JSON.stringify(state))), id);
    act('/admin/pos/cart/current/new');
    assert.throws(() => act('/admin/pos/cart/current/scan', { barcode: '123456' }), /lựa chọn hóa đơn/);
    act(`/admin/pos/${id}/invoice-route`, { route: 2 });
    assert.equal(act('/admin/pos/cart/current/scan', { barcode: '123456' }).grandTotal, 10000);
    assert.equal(core.pendingInvoiceIntent(core.initial(state.context)), null);
});

test('invoice intent translates the completed order instead of the new cart and rejects unmapped IDs or mismatched acknowledgments', () => {
    const { state, catalog, id, act } = completedIntentFixture();
    const op = { id: randomUUID(), url: `/admin/pos/${id}/invoice-route`, method: 'POST', body: { route: 2 } };
    core.apply(state, catalog, op); act('/admin/pos/cart/current/new');
    assert.throws(() => core.translate(state, op), /ánh xạ/);
    state.maps.order[id] = 101; state.maps.order[state.currentId] = 102;
    assert.equal(core.translate(state, op).url, '/admin/pos/101/invoice-route');
    assert.throws(() => core.confirmInvoiceIntent(state, op, { success: true, orderId: 102, route: 'Manual' }), /không khớp/);
    assert.throws(() => core.confirmInvoiceIntent(state, op, { success: true, orderId: 101, route: 'Automatic' }), /không khớp/);
    core.confirmInvoiceIntent(state, op, { success: true, orderId: 101, route: 'Manual' });
    assert.equal(state.orders[id].invoiceIntent.pendingSync, false);
    assert.equal(state.orders[state.currentId].invoiceIntent, undefined);
    core.absorb(state, { orderId: 101, status: 2, lines: [], payments: [] });
    assert.equal(state.orders[101].invoiceIntent.route, 2);
});

async function intentTransportHarness(saved, controls = {}) {
    const f = fixture(), key = f.state.key;
    f.state.context.expiresAtUtc = new Date(Date.now() + 3600000).toISOString();
    f.state.context.permissions.push('pos.order.reprint');
    f.state.context.screen = { currentDraft: core.clone(f.state.orders[f.state.currentId]), currentCart: { currentOrderId: f.state.currentId }, draftOrders: [], heldOrders: [] };
    const stores = saved || new Map([
        ['sessions', new Map([[key, core.clone(f.state)]])],
        ['catalogs', new Map([[key, { ...f.catalog, fetchedAtUtc: new Date().toISOString() }]])],
        ['meta', new Map([['active', { key, expiresAt: f.state.context.expiresAtUtc }]])]
    ]);
    const server = controls.backend?.server || fixture();
    if (!controls.backend) { server.state.orders = {}; server.state.currentId = null; server.state.nextId = 100; }
    const requests = [], receipts = controls.backend?.receipts || new Map(), prints = [], events = [], intervals = [];
    controls.backend = { server, receipts };
    controls.online ||= false;
    const database = { close() {}, transaction(names, mode) {
        const tx = {}, writes = [];
        tx.objectStore = name => ({
            get(k) { const q = {}; queueMicrotask(() => { q.result = structuredClone(stores.get(name)?.get(k)); q.onsuccess?.(); }); return q; },
            getAll() { const q = {}; queueMicrotask(() => { q.result = structuredClone([...stores.get(name).values()]); q.onsuccess?.(); }); return q; },
            put(value, k) { writes.push([name, k, structuredClone(value)]); }
        });
        if (mode === 'readwrite') queueMicrotask(() => {
            if (controls.failSave) { tx.error = new Error('Disk full'); tx.onabort?.(); }
            else { for (const [name, k, value] of writes) stores.get(name).set(k, value); tx.oncomplete?.(); }
        });
        return tx;
    } };
    const window = {
        PosOfflineCore: core, isSecureContext: false, PosPrinting: { openLocal: order => prints.push(structuredClone(order)) },
        addEventListener() {}, dispatchEvent: event => events.push(event.type),
        fetch: async (input, options = {}) => {
            if (!controls.online) throw new TypeError('Offline');
            const url = new URL(input, 'https://pos.local');
            if (url.pathname.endsWith('/offline/status') || url.pathname.endsWith('/offline/bootstrap')) {
                if (controls.stallStatusBody) return new Response(new ReadableStream({ start(c) { c.enqueue(new TextEncoder().encode('{')); } }), { headers: { 'Content-Type': 'application/json' } });
                if (controls.authStatus) return Response.json({ message: 'Session rejected' }, { status: controls.authStatus });
                return Response.json({ ...f.state.context, userId: controls.changedUserId || f.state.context.userId, antiForgeryToken: 'test' });
            }
            const id = options.headers?.['X-POS-Operation-Id'];
            requests.push({ path: url.pathname, id, body: JSON.parse(options.body || '{}') });
            if (controls.replayAuth) return Response.json({ message: 'Login expired' }, { status: 401 });
            if (controls.rejectRoute && url.pathname.endsWith('/invoice-route')) return Response.json({ message: 'Route conflict' }, { status: 409 });
            let result = receipts.get(id);
            if (!result) {
                result = core.clone(core.apply(server.state, server.catalog, { id, url: url.pathname + url.search,
                    method: options.method, body: JSON.parse(options.body || '{}'), occurredAt: options.headers?.['X-POS-Occurred-At'] }));
                receipts.set(id, result);
            }
            if (controls.loseRouteResponse && url.pathname.endsWith('/invoice-route')) {
                controls.loseRouteResponse = false; controls.online = false; throw new TypeError('Response lost after commit');
            }
            if (controls.failAfterRouteCommit && url.pathname.endsWith('/invoice-route')) {
                controls.failAfterRouteCommit = false; controls.failSave = true;
            }
            if (controls.stallReplayBody) return new Response(new ReadableStream({ start(c) { c.enqueue(new TextEncoder().encode('{')); } }), { headers: { 'Content-Type': 'application/json' } });
            if (controls.slowReplay) await new Promise(resolve => setTimeout(resolve, 60));
            return Response.json(result);
        }
    };
    const location = { origin: 'https://pos.local', href: 'https://pos.local/admin/pos', reload() {} };
    const qrContext = {};
    vm.runInNewContext(fs.readFileSync(require.resolve('../../GaoApp.Web/wwwroot/Admin/js/pos/qrcodegen.js'), 'utf8'), qrContext);
    vm.runInNewContext(fs.readFileSync(require.resolve('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.js'), 'utf8'), {
        window, location, URL, Response, AbortController, DOMException, structuredClone, Date,
        crypto: require('node:crypto').webcrypto, TextEncoder,
        setTimeout: (fn, ms) => setTimeout(fn, controls.fastTimeout ? ms / 100 : ms), clearTimeout,
        setInterval: fn => { intervals.push(fn); return intervals.length; }, clearInterval() {}, navigator: { locks: { request: (_, __, callback) => Promise.resolve(callback({})) } },
        qrcodegen: qrContext.qrcodegen,
        document: { getElementById: () => ({ dataset: { storeId: '1', terminalId: '2', userId: '3' } }),
            createElement: () => ({ getContext: () => ({ fillRect() {} }), toDataURL: () => 'data:image/png;base64,offline' }) },
        CustomEvent: class { constructor(type) { this.type = type; } },
        indexedDB: { open() { const q = {}; queueMicrotask(() => { q.result = database; q.onsuccess?.(); }); return q; } }
    });
    await window.PosOffline.init();
    await new Promise(resolve => setImmediate(resolve));
    return { api: window.PosOffline, stores, controls, server, requests, receipts, prints, tick: () => intervals.at(-1)(),
        state: () => stores.get('sessions').get(key),
        post: async (url, body = {}) => { const response = await window.fetch(url, { method: 'POST', body: JSON.stringify(body) }); return { status: response.status, body: await response.json() }; }
    };
}
async function sellOffline(h) {
    const created = await h.post('/admin/pos/cart/current/new');
    await h.post('/admin/pos/cart/current/scan', { barcode: '123456' });
    const completed = await h.post('/admin/pos/cart/current/payment-and-finalize',
        { orderId: created.body.orderId, clientRequestId: randomUUID(), method: 0, amount: 10000 });
    assert.equal(completed.status, 200);
    return created.body.orderId;
}
test('offline repeated create clicks and reload return the original pending QR conflict, and cancellation releases creation', async () => {
    const seed = await intentTransportHarness();
    seed.state().context.accounts = [{ id: 1, vietQrBankBin: '970416', accountNumber: '123456789', accountName: 'Test' }];
    const h = await intentTransportHarness(seed.stores);
    const original = await h.post('/admin/pos/cart/current/payment-qr', { clientRequestId: randomUUID(), amount: 2000 });
    assert.equal(original.status, 200);
    const repeated = await Promise.all(Array.from({ length: 7 }, (_, i) =>
        h.post('/admin/pos/cart/current/payment-qr', { clientRequestId: randomUUID(), amount: 3000 + i })));
    for (const result of repeated) {
        assert.equal(result.status, 409);
        assert.equal(result.body.errorCode, 'POS_QR_PENDING');
        assert.equal(result.body.metadata.savedQr.qr.id, original.body.id);
        assert.equal(result.body.metadata.savedQr.qr.amount, 2000);
        assert.equal(result.body.metadata.savedQr.canCancel, true);
    }
    assert.equal(Object.keys(h.state().qrs).length, 1);
    const reloaded = await intentTransportHarness(h.stores);
    const blocked = await reloaded.post('/admin/pos/cart/current/payment-qr', { clientRequestId: randomUUID(), amount: 4000 });
    assert.equal(blocked.body.metadata.qrId, original.body.id);
    await reloaded.post(`/admin/pos/payment-qr/${original.body.id}/cancel`);
    const next = await reloaded.post('/admin/pos/cart/current/payment-qr', { clientRequestId: randomUUID(), amount: 4000 });
    assert.equal(next.status, 200);
    assert.notEqual(next.body.id, original.body.id);
    assert.equal(next.body.amount, 4000);
});

test('offline review-only bank QR cannot be bypassed with a new QR', async () => {
    const seed = await intentTransportHarness();
    const orderId = seed.state().currentId;
    seed.state().qrs[30] = { id: 30, orderId, status: 0, savedStatus: 'ReviewRequired', readOnly: true,
        automaticConfirmation: true, requestCode: 'BANK-REVIEW', amount: 2000, qrDataUrl: 'saved-review' };
    const h = await intentTransportHarness(seed.stores);
    const result = await h.post('/admin/pos/cart/current/payment-qr', { clientRequestId: randomUUID(), amount: 3000 });
    assert.equal(result.status, 409);
    assert.equal(result.body.errorCode, 'POS_QR_PENDING');
    assert.equal(result.body.metadata.qrId, 30);
    assert.equal(Object.keys(h.state().qrs).length, 1);
});

test('automatic retry resumes after authentication returns for the original cashier and shift', async () => {
    const h = await intentTransportHarness(); await sellOffline(h);
    const before = JSON.stringify(h.state().queue);
    h.controls.online = true; h.controls.authStatus = 401;
    await h.tick(); assert.equal(h.api.canWork(), false);
    assert.equal(JSON.stringify(h.state().queue), before);
    h.controls.authStatus = 0; h.controls.changedUserId = 44;
    await h.tick(); assert.equal(h.requests.length, 0);
    h.controls.changedUserId = 0;
    await h.tick(); assert.equal(h.state().queue.length, 0);
    assert.equal(h.api.canWork(), true);
});

test('401 during replay preserves the command and retries automatically after reauthentication', async () => {
    const h = await intentTransportHarness(); await sellOffline(h);
    h.controls.online = true; h.controls.replayAuth = true;
    await h.tick(); const operationId = h.state().queue[0].id;
    assert.ok(h.state().conflict.recoverable);
    h.controls.replayAuth = false;
    await h.tick(); assert.equal(h.state().queue.length, 0);
    assert.equal(h.requests[1].id, operationId);
});

for (const stage of ['status', 'replay'])
test(`a stalled ${stage} response body releases synchronization and retries with original operation IDs`, async () => {
    const h = await intentTransportHarness(); await sellOffline(h);
    const originalIds = h.state().queue.map(x => x.id);
    h.controls.online = true; h.controls.fastTimeout = true;
    h.controls[stage === 'status' ? 'stallStatusBody' : 'stallReplayBody'] = true;
    await h.tick();
    assert.equal(h.api.status().syncing, false);
    assert.match(h.api.status().syncError, /quá lâu/);
    assert.deepEqual(h.state().queue.map(x => x.id), originalIds);
    h.controls.stallStatusBody = h.controls.stallReplayBody = false;
    await h.tick();
    assert.equal(h.state().queue.length, 0);
    assert.equal(h.receipts.size, originalIds.length, 'No duplicated sale after an acknowledgement is lost');
    assert.equal(h.api.status().syncError, '');
    assert.ok(h.api.status().lastSyncAt);
});

test('replay allows a slow server more time than foreground sale without parallel synchronization', async () => {
    const h = await intentTransportHarness(); await sellOffline(h);
    h.controls.online = true; h.controls.fastTimeout = true; h.controls.slowReplay = true;
    await Promise.all([h.tick(), h.tick(), h.api.sync(true)]);
    assert.equal(h.state().queue.length, 0);
    assert.equal(h.requests.length, h.receipts.size);
});

test('automatic retry drains an 84-operation backlog in order without losing invoices or duplicating sales', async () => {
    const h = await intentTransportHarness();
    for (let i = 0; i < 21; i++) {
        const id = await sellOffline(h);
        await h.post(`/admin/pos/${id}/invoice-route`, { route: 2 });
    }
    const ids = h.state().queue.map(x => x.id);
    assert.equal(ids.length, 84);
    h.controls.online = true;
    await h.tick();
    assert.equal(h.state().queue.length, 0);
    assert.deepEqual(h.requests.map(x => x.id), ids);
    assert.equal(h.receipts.size, 84);
    assert.equal(Object.values(h.server.state.orders).filter(x => x.status === 2).length, 21);
});

test('invoice intent journal acknowledges only durable storage, replays FIFO, and deduplicates a lost server response', async () => {
    const h = await intentTransportHarness(); const id = await sellOffline(h);
    assert.equal(h.api.pendingInvoiceIntentOrderId(), id);
    assert.throws(() => h.api.print(id), /lựa chọn/);
    const chosen = await h.post(`/admin/pos/${id}/invoice-route`, { route: 2 });
    assert.equal(chosen.status, 200); assert.equal(chosen.body.pendingSync, true);
    const count = h.state().queue.length;
    assert.equal((await h.post(`/admin/pos/${id}/invoice-route`, { route: 2 })).status, 200);
    assert.equal(h.state().queue.length, count);
    assert.equal((await h.post(`/admin/pos/${id}/invoice-route`, { route: 1 })).status, 409);
    assert.equal(h.api.print(id), true); assert.equal(h.prints[0].invoiceIntent.route, 2);
    h.controls.online = true; h.controls.loseRouteResponse = true;
    await h.api.sync();
    assert.equal(h.state().queue.length, 1);
    const request = h.requests.at(-1); assert.equal(request.path, '/admin/pos/101/invoice-route');
    assert.ok(request.id);
    h.controls.online = true; await h.api.sync();
    assert.equal(h.state().queue.length, 0);
    assert.equal(h.requests.at(-1).id, request.id);
    assert.equal(h.state().orders[101].invoiceIntent.pendingSync, false);
    assert.equal(h.receipts.size, count);
});

test('invoice intent restores the missing modal after reload and storage failure never confirms the choice', async () => {
    const h = await intentTransportHarness(); const id = await sellOffline(h);
    const reloaded = await intentTransportHarness(h.stores);
    assert.equal(reloaded.api.pendingInvoiceIntentOrderId(), id);
    reloaded.controls.failSave = true;
    const result = await reloaded.post(`/admin/pos/${id}/invoice-route`, { route: 1 });
    assert.equal(result.status, 409);
    assert.equal(reloaded.state().orders[id].invoiceIntent, undefined);
    assert.equal(reloaded.prints.length, 0);
    assert.equal(reloaded.state().queue.filter(x => x.url.endsWith('/invoice-route')).length, 0);
});

test('invoice intent preserves a rejected replay for reconciliation and blocks receipt printing', async () => {
    const h = await intentTransportHarness(); const id = await sellOffline(h);
    await h.post(`/admin/pos/${id}/invoice-route`, { route: 1 });
    h.controls.online = true; h.controls.rejectRoute = true; await h.api.sync();
    assert.equal(h.state().queue.length, 1); assert.match(h.state().conflict.message, /Route conflict/);
    assert.equal(h.state().orders[id].invoiceIntent.route, 1);
    assert.throws(() => h.api.print(id), /Route conflict/);
    assert.equal((await h.post(`/admin/pos/${id}/invoice-route`, { route: 2 })).status, 409);
});

test('invoice intent UI restores the pending order without opening another modal during submission', () => {
    const app = fs.readFileSync(require.resolve('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js'), 'utf8');
    const source = app.slice(app.indexOf('function restorePendingInvoiceIntent()'), app.indexOf('function openReceiptPrint('));
    const calls = [], context = { window: { PosOffline: { pendingInvoiceIntentOrderId: () => 123 } },
        invoiceIntentBusy: false, pendingInvoiceIntentOrderId: null, openReceiptPrint: id => calls.push(id) };
    vm.runInNewContext(source, context); context.restorePendingInvoiceIntent();
    assert.deepEqual(calls, [123]); context.invoiceIntentBusy = true; context.restorePendingInvoiceIntent();
    assert.deepEqual(calls, [123]);
    assert.match(app, /resolveOrderId\(pendingInvoiceIntentOrderId\)/);
});


for (const failure of ['401', '403', 'changed-user'])
test(`invoice intent pending replay survives ${failure} without submitting the route under another identity`, async () => {
    const h = await intentTransportHarness(); const id = await sellOffline(h);
    await h.post(`/admin/pos/${id}/invoice-route`, { route: 2 });
    const before = JSON.stringify(h.state().queue);
    h.controls.online = true;
    if (failure === 'changed-user') h.controls.changedUserId = 44; else h.controls.authStatus = Number(failure);
    await h.api.sync();
    assert.equal(JSON.stringify(h.state().queue), before);
    assert.equal(h.requests.length, 0);
    assert.equal(h.state().orders[id].invoiceIntent.route, 2);
});


test('invoice intent online commit survives a failed response save and browser reload using the same operation key', async () => {
    const h = await intentTransportHarness(); h.controls.online = true;
    await h.api.sync();
    const id = await sellOffline(h);
    assert.equal(h.state().queue.length, 0);
    h.controls.failAfterRouteCommit = true;
    const response = await h.post(`/admin/pos/${id}/invoice-route`, { route: 2 });
    assert.equal(response.status, 409);
    assert.equal(h.state().queue.length, 1);
    const operationId = h.state().queue[0].id;
    assert.ok(h.receipts.has(operationId));
    assert.equal(h.state().orders[id].invoiceIntent.route, 2);
    h.controls.failSave = false;
    const reloaded = await intentTransportHarness(h.stores, h.controls);
    for (let i = 0; i < 30 && reloaded.state().queue.length; i++) await new Promise(resolve => setImmediate(resolve));
    assert.equal(reloaded.state().queue.length, 0);
    assert.equal(reloaded.requests.at(-1).id, operationId);
    assert.equal(reloaded.state().orders[id].invoiceIntent.pendingSync, false);
    assert.equal(reloaded.api.pendingInvoiceIntentOrderId(), null);
});
test('receipt print preference belongs to the completed offline order and resets on customer removal', () => {
    const { state, catalog, act } = fixture();
    catalog.customers[0].askBeforePrintingReceipt = true;
    const selected = act('/admin/pos/cart/current/customer/9', {});
    assert.equal(selected.askBeforePrintingReceipt, true);
    act('/admin/pos/cart/current/customer', {}, 'DELETE');
    assert.equal(core.current(state).askBeforePrintingReceipt, false);
    act('/admin/pos/cart/current/customer/9', {});
    const order = act('/admin/pos/cart/current/scan', { barcode: '123456', qty: 1 });
    act('/admin/pos/cart/current/payment-and-finalize', { orderId: order.orderId, clientRequestId: randomUUID(), method: 0, amount: 10000 });
    act('/admin/pos/cart/current/new');
    assert.notEqual(core.current(state).askBeforePrintingReceipt, true);
    const intent = act(`/admin/pos/${order.orderId}/invoice-route`, { route: 1 });
    assert.equal(intent.askBeforePrintingReceipt, true);
    assert.equal(intent.orderId, order.orderId);
    assert.equal(core.invoiceIntentOrder(JSON.parse(JSON.stringify(state)), order.orderId).askBeforePrintingReceipt, true);
});
