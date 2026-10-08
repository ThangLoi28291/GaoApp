const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.delivery.js'), 'utf8');
const intentKey = 'gao.delivery.intent.v1:6:4:17';
const guid = '21a00000-0000-0000-0000-000000000001';
const tick = () => new Promise(resolve => setImmediate(resolve));
const pending = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
const response = (status, body) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
const snapshot = overrides => ({ sourceCartId: 31, version: 'AAAAAAAAAAE=', fingerprint: 'fresh-fingerprint',
    warehouseName: 'Kho chính', quotedTotal: 20000, lines: [], recipientName: 'Khách từ máy chủ',
    recipientPhone: '0900000002', recipientAddress: 'Địa chỉ từ máy chủ', ...overrides });
const unsupported = () => response(409, { code: 'PRICE_FEATURE_UNSUPPORTED', message: 'Giỏ có khuyến mãi chưa hỗ trợ giao hàng.' });

function fixture(draft, replies, savedIntent) {
    class Element {
        constructor() { this.value = ''; this.textContent = ''; this.disabled = false; this.hidden = false;
            this.dataset = {}; this.children = []; this.listeners = new Map(); this.classList = { contains: () => false }; }
        addEventListener(name, listener) { this.listeners.set(name, listener); }
        emit(name, event = {}) { return this.listeners.get(name)?.({ preventDefault() {}, ...event }); }
        append(...children) { this.children.push(...children); }
        replaceChildren(...children) { this.children = children; }
        focus() {}
    }
    const ids = ['deliveryModal', 'btnOpenDelivery', 'posShell', 'deliveryName', 'deliveryPhone', 'deliveryAddress',
        'deliveryNote', 'deliveryMessage', 'deliverySubmit', 'deliveryRefresh', 'deliveryFields', 'deliveryCart',
        'deliveryResult', 'deliveryForm', 'deliveryCreatedCode', 'deliveryBillLink', 'deliveryLookupLink'];
    const elements = Object.fromEntries(ids.map(id => [id, new Element()]));
    elements.posShell.dataset = { storeId: '6', terminalId: '4' }; elements.deliveryModal.dataset.actorId = '17';
    const documentListeners = new Map(), disk = new Map(), calls = [];
    if (savedIntent) disk.set(intentKey, savedIntent);
    const state = { business: { currentDraft: draft } };
    const context = { document: { getElementById: id => elements[id] || null,
        addEventListener: (name, listener) => documentListeners.set(name, listener),
        createElement: () => new Element(), querySelector: () => ({ content: 'test-csrf' }) },
        window: { posApp: { state, loadScreen: async () => {} }, addEventListener() {} }, navigator: { onLine: true },
        bootstrap: { Modal: { getInstance: () => null, getOrCreateInstance: () => ({ show() {} }) } },
        sessionStorage: { getItem: key => disk.get(key) ?? null, setItem: (key, value) => disk.set(key, value), removeItem: key => disk.delete(key) },
        crypto: { randomUUID: () => guid }, fetch: async (url, options) => {
            calls.push({ url, ...options }); const reply = replies.shift();
            assert.ok(reply, 'Unexpected request'); return reply;
        } };
    vm.runInNewContext(source, context, { filename: 'pos.delivery.js' }); documentListeners.get('DOMContentLoaded')();
    return { elements, state, disk, calls, open: () => elements.btnOpenDelivery.emit('click'),
        input: (id, value) => { elements[id].value = value; elements[id].emit('input'); },
        submit: () => elements.deliveryForm.emit('submit') };
}

test('selected POS name and phone appear immediately and survive promotion409 without enabling creation', async () => {
    const request = pending(), app = fixture({ customerId: 7, customerName: 'A Nghĩa', customerPhone: '0901234567' }, [request.promise]);
    app.open();
    assert.equal(app.elements.deliveryName.value, 'A Nghĩa'); assert.equal(app.elements.deliveryPhone.value, '0901234567');
    assert.equal(app.elements.deliverySubmit.disabled, true);
    request.resolve(unsupported()); await tick();
    assert.equal(app.elements.deliveryName.value, 'A Nghĩa'); assert.equal(app.elements.deliveryPhone.value, '0901234567');
    assert.match(app.elements.deliveryMessage.textContent, /khuyến mãi/); assert.equal(app.elements.deliverySubmit.disabled, true);
    await app.submit(); assert.equal(app.calls.length, 1); assert.equal(app.disk.size, 0);
});

test('healthy fresh snapshot replaces initial POS defaults and supplies the server customer address', async () => {
    const request = pending(), app = fixture({ customer: { name: 'Tên nested', phone: '0901111111', address: 'Địa chỉ local' },
        customerName: 'Tên flat cũ', customerPhone: '0902222222' }, [request.promise]);
    app.open(); assert.equal(app.elements.deliveryName.value, 'Tên nested'); assert.equal(app.elements.deliveryAddress.value, 'Địa chỉ local');
    request.resolve(response(200, snapshot())); await tick();
    assert.equal(app.elements.deliveryName.value, 'Khách từ máy chủ'); assert.equal(app.elements.deliveryPhone.value, '0900000002');
    assert.equal(app.elements.deliveryAddress.value, 'Địa chỉ từ máy chủ'); assert.equal(app.elements.deliverySubmit.disabled, false);
    assert.match(app.elements.deliveryCart.children[0].textContent, /Giỏ #31/);
});

test('recipient edits including an explicit clear survive a late snapshot and creation uses its authoritative cart version', async () => {
    const request = pending(), app = fixture({ customerName: 'A Nghĩa', customerPhone: '0901234567' }, [request.promise,
        response(200, { delivery: { id: 22, code: 'GH-22' }, billUrl: '/22/bill', lookupUrl: '/22' })]);
    app.open(); app.input('deliveryName', 'Người nhận khác'); app.input('deliveryPhone', '');
    app.input('deliveryAddress', 'Địa chỉ tự nhập'); app.input('deliveryNote', 'Gọi trước khi giao');
    request.resolve(response(200, snapshot())); await tick();
    assert.equal(app.elements.deliveryName.value, 'Người nhận khác'); assert.equal(app.elements.deliveryPhone.value, '');
    assert.equal(app.elements.deliveryAddress.value, 'Địa chỉ tự nhập'); assert.equal(app.elements.deliveryNote.value, 'Gọi trước khi giao');
    await app.submit();
    const body = JSON.parse(app.calls[1].body);
    assert.deepEqual(body, { clientRequestId: guid, sourceCartId: 31, expectedVersion: 'AAAAAAAAAAE=', expectedFingerprint: 'fresh-fingerprint',
        recipientName: 'Người nhận khác', recipientPhone: '', recipientAddress: 'Địa chỉ tự nhập', note: 'Gọi trước khi giao' });
});

test('reopening seeds the current changed customer and clearing the POS customer removes old recipient defaults', async () => {
    const app = fixture({ customerName: 'Khách A', customerPhone: '0900000001', customerAddress: 'Địa chỉ A' },
        [unsupported(), unsupported(), unsupported()]);
    app.open(); await tick(); app.input('deliveryName', 'Nhập ở lần trước');
    app.state.business.currentDraft = { customer: { name: 'Khách B', phone: '0900000002', address: 'Địa chỉ B' } };
    app.open(); assert.equal(app.elements.deliveryName.value, 'Khách B'); assert.equal(app.elements.deliveryAddress.value, 'Địa chỉ B'); await tick();
    app.state.business.currentDraft = { customerId: null, customerName: null, customerPhone: null };
    app.open(); await tick();
    for (const id of ['deliveryName', 'deliveryPhone', 'deliveryAddress', 'deliveryNote']) assert.equal(app.elements[id].value, '');
    assert.equal(app.elements.deliverySubmit.disabled, true);
});

test('an older modal snapshot cannot overwrite a reopened cleared customer or remove the latest eligibility warning', async () => {
    const oldRequest = pending(), app = fixture({ customerName: 'Khách A' }, [oldRequest.promise, unsupported()]);
    app.open(); app.state.business.currentDraft = null; app.open(); await tick();
    oldRequest.resolve(response(200, snapshot({ recipientName: 'Khách A trả về muộn' }))); await tick();
    assert.equal(app.elements.deliveryName.value, ''); assert.equal(app.elements.deliveryCart.children.length, 0);
    assert.match(app.elements.deliveryMessage.textContent, /khuyến mãi/); assert.equal(app.elements.deliverySubmit.disabled, true);
});

test('stored uncertain intent takes priority over customer defaults and retries the exact body without a new snapshot or GUID', async () => {
    const saved = JSON.stringify({ clientRequestId: '99000000-0000-0000-0000-000000000099', sourceCartId: 18,
        expectedVersion: 'AAAAAAAAABg=', expectedFingerprint: 'saved-fingerprint', recipientName: 'Người nhận đã gửi',
        recipientPhone: '0909999999', recipientAddress: 'Địa chỉ đã gửi', note: null });
    const app = fixture({ customerName: 'Khách vừa đổi', customerPhone: '0901111111' }, [response(503, { message: 'Chưa xác nhận' })], saved);
    app.open(); await tick();
    assert.equal(app.calls.length, 0); assert.equal(app.elements.deliveryName.value, 'Người nhận đã gửi');
    assert.equal(app.elements.deliveryFields.disabled, true); assert.equal(app.elements.deliverySubmit.disabled, false);
    await app.submit();
    assert.equal(app.calls.length, 1); assert.equal(app.calls[0].url, '/admin/api/deliveries/from-current-cart');
    assert.equal(app.calls[0].body, saved); assert.equal(app.disk.get(intentKey), saved);
    assert.equal(app.elements.deliveryName.value, 'Người nhận đã gửi');
});
