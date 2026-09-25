const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.qr-history.js'), 'utf8');
function element() {
    const listeners = new Map();
    return { children: [], dataset: {}, hidden: false, disabled: false, textContent: '',
        classList: { toggle() {} }, appendChild(node) { this.children.push(node); },
        replaceChildren(...nodes) { this.children = nodes; },
        addEventListener(name, action) { listeners.set(name, action); },
        click() { return listeners.get('click')?.(); } };
}
function harness(fetch) {
    const elements = new Map();
    const opened = [];
    let orderId = 15;
    const context = { window: {}, fetch, document: {
        getElementById(id) { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); },
        createElement: element
    } };
    vm.runInNewContext(source, context);
    const history = context.window.PosQrHistory.create({ getOrderId: () => orderId, onOpen: qr => opened.push(qr) });
    return { history, opened, elements, setOrder(id) { orderId = id; },
        latest: () => elements.get('btnReopenLatestPaymentQr').click(),
        rows: () => elements.get('paymentQrHistoryList').children };
}
const response = data => ({ ok: true, json: async () => data });
const item = (id, status = 'Pending') => ({ qrId: id, requestCode: `QR-${id}`, amount: id * 1000,
    createdAtUtc: '2026-09-09T01:00:00', status, bankName: 'ACB', canReopen: status === 'Pending' });
const saved = id => ({ qr: { id, orderId: 15, amount: id * 1000, requestCode: `QR-${id}`, qrDataUrl: `stored-image-${id}` },
    status: 'Pending', canCancel: true, message: 'QR đã lưu' });

test('loading and reopening latest QR uses only GET and retains its saved id, image and amount', async () => {
    const calls = [];
    const h = harness(async (url, options) => {
        calls.push([url, options.method]);
        return response(url.endsWith('/qrs') ? { orderId: 15, latestQrId: 6, items: [item(6), item(5, 'Cancelled')] } : saved(6));
    });
    await h.history.refresh();
    assert.equal(h.elements.get('btnReopenLatestPaymentQr').hidden, false);
    await h.latest();
    assert.deepEqual(calls, [['/admin/acb/payments/orders/15/qrs', 'GET'], ['/admin/acb/payments/orders/15/qrs/6', 'GET']]);
    assert.equal(h.opened[0].id, 6);
    assert.equal(h.opened[0].amount, 6000);
    assert.equal(h.opened[0].qrDataUrl, 'stored-image-6');
    assert.equal(h.rows()[1].children[1].children[1].textContent, 'Xem giao dịch');
});

test('choosing an earlier active QR opens that exact attempt, not the latest one', async () => {
    const h = harness(async url => response(url.endsWith('/qrs')
        ? { orderId: 15, latestQrId: 6, items: [item(6), item(4)] } : saved(4)));
    await h.history.refresh();
    await h.rows()[1].children[1].children[1].click();
    assert.equal(h.opened[0].id, 4);
    assert.equal(h.opened[0].amount, 4000);
});

test('reload restores the history without creating a QR or requiring an amount entry', async () => {
    let reads = 0;
    const fetch = async url => {
        assert.equal(url, '/admin/acb/payments/orders/15/qrs'); reads++;
        return response({ orderId: 15, latestQrId: 6, items: [item(6)] });
    };
    const original = harness(fetch); await original.history.refresh();
    const reloaded = harness(fetch); await reloaded.history.refresh();
    assert.equal(reads, 2);
    assert.equal(reloaded.elements.get('btnReopenLatestPaymentQr').hidden, false);
    assert.equal(reloaded.opened.length, 0);
});

test('a response arriving after the cashier changes order cannot open the previous orders QR', async () => {
    let resolve;
    const delayed = new Promise(done => { resolve = done; });
    const h = harness(async url => url.endsWith('/qrs')
        ? response({ orderId: 15, latestQrId: 6, items: [item(6)] }) : delayed);
    await h.history.refresh();
    const opening = h.latest();
    h.setOrder(16); h.history.invalidate();
    resolve(response(saved(6))); await opening;
    assert.equal(h.opened.length, 0);
    assert.equal(h.elements.get('paymentQrHistory').hidden, true);
});

test('an older history response cannot replace the next orders history', async () => {
    let resolve;
    const delayed = new Promise(done => { resolve = done; });
    const h = harness(async url => url.includes('/15/') ? delayed : response({ orderId: 16, latestQrId: null, items: [] }));
    const first = h.history.refresh();
    h.setOrder(16); await h.history.refresh();
    resolve(response({ orderId: 15, latestQrId: 6, items: [item(6)] })); await first;
    assert.equal(h.rows().length, 0);
    assert.equal(h.elements.get('btnReopenLatestPaymentQr').hidden, true);
});

test('failed reopening displays the error and allows a retry without calling create', async () => {
    let attempts = 0;
    const h = harness(async url => {
        if (url.endsWith('/qrs')) return response({ orderId: 15, latestQrId: 6, items: [item(6)] });
        assert.equal(url, '/admin/acb/payments/orders/15/qrs/6');
        if (++attempts === 1) return { ok: false, json: async () => ({ message: 'Mất kết nối local' }) };
        return response(saved(6));
    });
    await h.history.refresh(); await h.latest();
    assert.match(h.elements.get('paymentQrHistoryStatus').textContent, /Mất kết nối/);
    assert.equal(h.elements.get('btnReopenLatestPaymentQr').disabled, false);
    await h.latest(); assert.equal(h.opened.length, 1);
});

test('server returning a different QR is rejected without showing an image', async () => {
    const h = harness(async url => response(url.endsWith('/qrs')
        ? { orderId: 15, latestQrId: 6, items: [item(6)] } : saved(7)));
    await h.history.refresh(); await h.latest();
    assert.equal(h.opened.length, 0);
    assert.match(h.elements.get('paymentQrHistoryStatus').textContent, /không khớp/);
});

test('repeated clicks during loading reuse one read and render once', async () => {
    let resolve, calls = 0;
    const pending = new Promise(done => { resolve = done; });
    const h = harness(async url => {
        if (url.endsWith('/qrs')) return response({ orderId: 15, latestQrId: 6, items: [item(6)] });
        calls++; return pending;
    });
    await h.history.refresh();
    const first = h.latest(); await h.latest();
    resolve(response(saved(6))); await first;
    assert.equal(calls, 1); assert.equal(h.opened.length, 1);
});


test('completed QR reopens the saved image in read-only mode', async () => {
    const h = harness(async (url, options) => {
        assert.equal(options.method, 'GET');
        return response(url.endsWith('/qrs')
            ? { orderId: 15, latestQrId: 6, items: [{ ...item(6, 'Completed'), canReopen: true }] }
            : { ...saved(6), status: 'Completed', canCancel: false, readOnly: true });
    });
    await h.history.refresh(); await h.latest();
    assert.equal(h.opened[0].qrDataUrl, 'stored-image-6');
    assert.equal(h.opened[0].readOnly, true);
    assert.equal(h.opened[0].canCancel, false);
});
