const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { randomUUID } = require('node:crypto');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.payment.js'), 'utf8');
function harness(post, storage = new Map()) {
    const elements = new Map(), listeners = new Map(), calls = [], printed = [], tracked = [], errors = [];
    const noop = () => {}; const timers = [];
    function el(id) {
        if (!elements.has(id)) elements.set(id, { id, style: {}, dataset: {}, value: '', textContent: '',
            classList: { contains: () => false, toggle: noop, add: noop, remove: noop },
            setAttribute: noop, removeAttribute: noop, addEventListener: noop, appendChild: noop, before: noop,
            querySelector: () => null, querySelectorAll: () => [], closest: () => null, focus: noop, select: noop });
        return elements.get(id);
    }
    let reopen;
    const state = { business: { currentDraft: { orderId: 15, grandTotal: 9000, paidTotal: 4000, balanceDue: 5000 } }, ui: {} };
    el('payMethod').value = '1'; el('payAmount').value = '5000';
    const context = { console, crypto: { randomUUID }, setTimeout: (callback, delay) => timers.push({ callback, delay }), clearTimeout: noop,
        sessionStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) },
        document: { getElementById: el, createElement: el, querySelector: () => null },
        window: { addEventListener: (name, action) => listeners.set(name, action),
            PosCommon: { clearInlineError: noop, setInlineError: (_, message) => errors.push(message), refreshUiLocks: noop },
            PosQrHistory: { create: options => { reopen = options.onOpen; return { refresh: noop, invalidate: noop }; } },
            PosAcb: { track: qr => tracked.push(qr.id), check: id => calls.push(['check', id]) } }
    };
    vm.runInNewContext(source, context);
    const payment = context.window.PosPayment.create({ posState: state,
        elements: Object.fromEntries(['payMethod', 'payAmount', 'payReference', 'payProvider', 'btnAddPayment', 'paymentModalEl'].map(id => [id, el(id)])),
        modals: {}, helpers: {
            postJson: async (url, body) => { calls.push([url, body]); return await post(url, body); },
            runPosAction: async (_, key, action, options) => { const result = await action(); await options.onSuccess?.(result); options.onFinally?.(); return result; },
            renderPaymentModalDraft: noop, renderPaymentPreview: noop, showSuccess: noop,
            applyDraftActionSuccess: ({ draft, afterSync }) => { state.business.currentDraft = draft; afterSync?.(draft); },
            applyScreenActionSuccess: async () => {},
            openReceiptPrint: id => printed.push(id),
            requestScreenRefresh: async () => { state.business.currentDraft.balanceDue = 3000; }
        } });
    return { payment, el, calls, errors, printed, tracked, reopen, state, listeners, storage, timers };
}
const qr = (id, amount) => ({ id, orderId: 15, amount, requestCode: `QR-${id}`, qrDataUrl: `saved-${id}`, bankName: 'Test', content: `Test-${id}` });

test('POS creates distinct installments and reopening does not call create or change entered amount', async () => {
    let id = 0;
    const h = harness(async (_, body) => qr(++id, body.amount));
    h.el('payAmount').value = '2000';
    await h.payment.createPaymentQr();
    h.el('payAmount').value = '3000';
    await h.payment.createPaymentQr();
    assert.notEqual(h.calls[0][1].clientRequestId, h.calls[1][1].clientRequestId);
    assert.deepEqual(h.calls.map(c => c[1].amount), [2000, 3000]);
    h.reopen(qr(1, 2000)); h.payment.openPaymentQrPopup();
    assert.equal(h.calls.length, 2);
    assert.equal(h.el('paymentQrImage').src, 'saved-1');
    assert.equal(h.el('payAmount').value, '3000');
});

test('lost create response keeps the creation key through reload and retry', async () => {
    const storage = new Map();
    const h = harness(async () => { throw new Error('network'); }, storage);
    await assert.rejects(h.payment.createPaymentQr(), /network/);
    const next = harness(async (_, body) => qr(8, body.amount), storage);
    await next.payment.createPaymentQr();
    assert.equal(next.calls[0][1].clientRequestId, h.calls[0][1].clientRequestId);
    assert.equal(storage.size, 0);
});

test('manual QR confirms the saved attempt at one endpoint then updates the remaining amount', async () => {
    const h = harness(async () => ({ qrId: 1, orderId: 15, finalized: false, paidAmount: 2000, remainingAmount: 3000, printUrl: null }));
    h.reopen(qr(1, 2000));
    h.el('payAmount').value = '99999';
    await h.payment.confirmPaymentQrPaid();
    assert.equal(h.calls.length, 1);
    assert.equal(h.calls[0][0], '/admin/pos/payment-qr/1/manual-confirm');
    assert.equal(h.el('payAmount').value, 3000);
    assert.deepEqual(h.printed, []);
});

test('archived QR is visible but cannot confirm, cancel or start automatic watching', async () => {
    const h = harness(async () => { throw new Error('unexpected mutation'); });
    h.reopen({ ...qr(3, 5000), readOnly: true, canCancel: false, automaticConfirmation: true });
    await h.payment.confirmPaymentQrPaid(); await h.payment.cancelPaymentQr();
    assert.equal(h.el('paymentQrImage').src, 'saved-3');
    assert.equal(h.el('btnCheckAcbPayment').hidden, true);
    assert.deepEqual(h.calls, []); assert.deepEqual(h.tracked, []);
});

test('invalid amount cannot create a QR before reaching the server', async () => {
    const h = harness(async () => { throw new Error('unexpected create'); });
    for (const value of ['5001', '1000.5']) { h.el('payAmount').value = value; await h.payment.createPaymentQr(); }
    assert.equal(h.calls.length, 0); assert.equal(h.errors.length, 2);
});

test('manual partial collection keeps its identity after a lost response and restores original inputs on reload', async () => {
    const storage = new Map();
    const first = harness(async () => { throw new Error('lost response'); }, storage);
    first.el('payMethod').value = '0'; first.el('payAmount').value = '2000';
    await assert.rejects(first.payment.addPayment(), /lost response/);
    const next = harness(async () => ({ orderId: 15, status: 0, grandTotal: 9000, paidTotal: 6000, balanceDue: 3000, payments: [] }), storage);
    next.el('payMethod').value = '0'; next.el('payAmount').value = '3000';
    await next.payment.addPayment();
    assert.equal(next.calls[0][1].orderId, 15);
    assert.equal(next.calls[0][1].amount, 2000);
    assert.equal(next.calls[0][1].clientRequestId, first.calls[0][1].clientRequestId);
    assert.equal(storage.size, 0);
    next.el('payAmount').value = '2000'; await next.payment.addPayment();
    assert.notEqual(next.calls[0][1].clientRequestId, next.calls[1][1].clientRequestId);
});

test('delayed cash finalization uses the paid order even when the screen changes carts', async () => {
    const h = harness(async () => ({ orderId: 15, status: 0, grandTotal: 9000, paidTotal: 9000, balanceDue: 0, payments: [] }));
    h.el('payMethod').value = '0'; h.el('payAmount').value = '5000';
    await h.payment.addPayment();
    h.state.business.currentDraft = { orderId: 16, status: 0, grandTotal: 10000, balanceDue: 10000 };
    await h.timers.find(t => t.delay === 150).callback();
    assert.equal(h.calls[1][0], '/admin/pos/15/finalize');
    assert.equal(h.calls.length, 2);
});
test('manual VND collection rejects fractions and out-of-range values before storing or posting', async () => {
    const h = harness(async () => { throw new Error('unexpected collection'); });
    h.el('payMethod').value = '0';
    for (const value of ['-1', '0', '20.5', '10000000000000000']) {
        h.el('payAmount').value = value; await h.payment.addPayment();
    }
    assert.equal(h.calls.length, 0); assert.equal(h.storage.size, 0);
});