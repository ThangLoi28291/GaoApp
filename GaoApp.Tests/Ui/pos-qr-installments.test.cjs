const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { randomUUID } = require('node:crypto');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.payment.js'), 'utf8');
const commonContext = { window: {} };
vm.runInNewContext(fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.common.js'), 'utf8'), commonContext);
const { parseMoneyInput, setMoneyInput, bindMoneyInput } = commonContext.window.PosCommon;
function harness(post, storage = new Map(), options = {}) {
    const elements = new Map(), listeners = new Map(), documentListeners = [], calls = [], printed = [], printCalls = [], tracked = [], errors = [], modalShows = [];
    const noop = () => {}; const timers = [];
    function el(id) {
        if (!elements.has(id)) {
            const classes = new Set(), handlers = new Map();
            elements.set(id, { id, style: {}, dataset: {}, value: '', textContent: '',
                classList: { contains: name => classes.has(name), toggle: (name, active) => active ? classes.add(name) : classes.delete(name),
                    add: name => classes.add(name), remove: name => classes.delete(name) },
                setAttribute: noop, removeAttribute: noop,
                addEventListener(name, action, flags) { if (!handlers.has(name)) handlers.set(name, []); handlers.get(name).push({ action, once: flags?.once }); },
                removeEventListener(name, action) { handlers.set(name, (handlers.get(name) || []).filter(entry => entry.action !== action)); },
                emit(name, event = {}) { for (const entry of [...handlers.get(name) || []]) {
                    if (entry.once) this.removeEventListener(name, entry.action);
                    entry.action.call(this, { target: this, preventDefault: noop, stopPropagation: noop, ...event });
                } },
                click() { this.emit('click'); }, appendChild: noop, before: noop, replaceChildren: noop, add: noop,
                querySelector: () => null, querySelectorAll: () => [], closest: () => null, contains(target) { return target === this; }, focus: noop, select: noop });
        }
        return elements.get(id);
    }
    const modalInstances = new Map();
    function modal(node) {
        if (!modalInstances.has(node)) modalInstances.set(node, {
            show() {
                modalShows.push(node.id); node.classList.add('show'); node.emit('shown.bs.modal');
                if (node.id === 'paymentQrPendingModal' && options.autoAcknowledge !== false)
                    queueMicrotask(() => this.hide());
            },
            hide() { node.emit('hide.bs.modal'); node.classList.remove('show'); node.emit('hidden.bs.modal'); }
        });
        return modalInstances.get(node);
    }
    let reopen;
    const history = { refresh: noop, invalidate: noop, open: async () => false };
    const state = { business: { currentDraft: { orderId: 15, grandTotal: 9000, paidTotal: 4000, balanceDue: 5000 } }, ui: {} };
    el('payMethod').value = '1'; el('payAmount').value = '5000';
    const context = { console, navigator: {onLine:true}, crypto: { randomUUID }, Option: class { constructor(text,value){this.text=text;this.value=value;} },
        setTimeout: (callback, delay) => timers.push({ callback, delay }), clearTimeout: noop,
        sessionStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) },
        document: { getElementById: el, createElement: el, querySelector: () => null,
            addEventListener: (name, action) => { if (name === 'keydown') documentListeners.push(action); } },
        window: { addEventListener: (name, action) => listeners.set(name, action),
            bootstrap: { Modal: { getOrCreateInstance: modal, getInstance: node => modalInstances.get(node) } },
            PosCommon: { parseMoneyInput, setMoneyInput, bindMoneyInput,
                clearInlineError: noop, setInlineError: (_, message) => errors.push(message), refreshUiLocks: noop, registerUiLock: noop },
            PosQrHistory: { create: options => { reopen = options.onOpen; return history; } },
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
            applyScreenActionSuccess: async ({beforeRefresh}) => { await beforeRefresh?.(); },
            openReceiptPrint: (...args) => { printed.push(args[0]); printCalls.push(args); },
            requestScreenRefresh: async () => { state.business.currentDraft.balanceDue = 3000; }
        } });
    return { payment, el, calls, errors, printed, printCalls, tracked, reopen, history, state, listeners, storage, timers, modalShows, window: context.window,
        async key(key, target, extra = {}) {
            for (const action of documentListeners) await action({ key, target, repeat: false, preventDefault: noop, stopPropagation: noop, ...extra });
        } };
}
const qr = (id, amount) => ({ id, orderId: 15, amount, requestCode: `QR-${id}`, qrDataUrl: `saved-${id}`, bankName: 'Test', content: `Test-${id}` });

test('waiting QR only offers waiting and cancellation; rejecting the bank suggestion enables the saved manual flow', async () => {
    const h = harness(async () => ({ qrId: 1, orderId: 15, finalized: false, remainingAmount: 3000 }));
    h.reopen(qr(1, 2000));
    assert.equal(h.el('paymentQrPopupTitle').textContent, 'Đợi xác nhận chuyển khoản');
    assert.equal(h.el('btnConfirmPaymentQrPaid').style.display, 'none');
    assert.equal(h.el('btnCancelPaymentQr').hidden, false);
    await h.payment.addPayment(); assert.equal(h.calls.length, 0, 'Enter/add payment while waiting only reopens the QR');
    await h.listeners.get('bank-notification:manual-fallback')({ detail: { orderId: 15, qrId: 1 } });
    assert.equal(h.el('paymentQrPopupTitle').textContent, 'Kiểm tra chuyển khoản thủ công');
    assert.equal(h.el('btnConfirmPaymentQrPaid').style.display, '');
    assert.equal(h.calls.length, 0);
    await h.payment.confirmPaymentQrPaid(); assert.match(h.calls[0][0], /\/1\/manual-confirm$/);
});

test('F9 only checks notifications while waiting and confirms manually only after rejecting a bank suggestion', async () => {
    const start = source.indexOf('        async function handlePaymentQrModalHotkeys(');
    const end = source.indexOf('\n        /*', start);
    let manual = false, confirmed = 0, checked = 0;
    const env = { currentPaymentQr: { id: 5 }, document: { getElementById: () => ({ classList: { contains: () => true } }) },
        window: { PosBankNotifications: { refresh: async () => { checked++; } } },
        isManualQrMode: () => manual, confirmPaymentQrPaid: async () => { confirmed++; } };
    vm.runInNewContext(source.slice(start, end), env);
    const key = repeat => ({ key: 'F9', repeat, preventDefault() {}, stopPropagation() {} });
    await env.handlePaymentQrModalHotkeys(key(false)); assert.equal(manual, false); assert.equal(checked, 1); assert.equal(confirmed, 0);
    await env.handlePaymentQrModalHotkeys(key(true)); assert.equal(confirmed, 0);
    assert.equal(checked, 1); manual = true;
    await env.handlePaymentQrModalHotkeys(key(false)); assert.equal(confirmed, 1);
});

test('offline mode and an unconnected notification account cannot silently enable manual confirmation', async () => {
    const h = harness(async () => { throw Error('unexpected confirmation'); });
    h.window.PosOffline = { localMode: () => true, manualQr: qr => ({ ...qr, manualRequested: true, savedMessage: 'Mất kết nối: kiểm tra tiền rồi bấm Đã nhận thủ công.' }) };
    h.window.PosBankNotifications = { enabled: () => false, isManual: () => false, track() {} };
    h.reopen(qr(1, 2000));
    assert.equal(h.el('paymentQrPopupTitle').textContent, 'Đợi xác nhận chuyển khoản');
    assert.equal(h.el('btnConfirmPaymentQrPaid').style.display, 'none');
    assert.match(h.el('acbPaymentCheckStatus').textContent, /đang chờ kết quả/);
    assert.ok(!h.el('acbPaymentCheckStatus').textContent.includes('thủ công'));
    await h.payment.addPayment(); assert.equal(h.calls.length, 0);
    h.window.PosOffline.localMode = () => false;
    h.listeners.get('pos:offline-status')();
    assert.equal(h.el('paymentQrWaitTitle').textContent, 'Đang đợi tiền chuyển khoản');
    assert.equal(h.el('btnConfirmPaymentQrPaid').style.display, 'none');
    const view = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml'), 'utf8');
    assert.match(view, /id="btnManualPaymentQr"[^>]*hidden/, 'manual fallback is initially hidden until the server waiting deadline');
});

test('rejecting a suggestion for another order does not enable confirmation on the current QR', async () => {
    const h = harness(async () => { throw Error('unexpected confirmation'); });
    h.reopen(qr(1, 2000));
    await h.listeners.get('bank-notification:manual-fallback')({ detail: { orderId: 16, qrId: 2 } });
    assert.equal(h.el('btnConfirmPaymentQrPaid').style.display, 'none'); assert.equal(h.calls.length, 0);
});

test('POS creates the next installment after confirmation and reopening retains the entered amount', async () => {
    let id = 0;
    const h = harness(async (url, body) => url.endsWith('/manual-confirm')
        ? { qrId: 1, orderId: 15, finalized: false, remainingAmount: 3000 }
        : qr(++id, body.amount));
    h.el('payAmount').value = '2000';
    await h.payment.createPaymentQr();
    await h.payment.confirmPaymentQrPaid();
    h.el('payAmount').value = '3000';
    await h.payment.createPaymentQr();
    assert.notEqual(h.calls[0][1].clientRequestId, h.calls[2][1].clientRequestId);
    assert.deepEqual(h.calls.filter(c => c[0].endsWith('/payment-qr')).map(c => c[1].amount), [2000, 3000]);
    h.reopen(qr(1, 2000)); h.payment.openPaymentQrPopup();
    assert.equal(h.calls.length, 3);
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
    assert.equal(h.el('payAmount').value, '3.000');
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
    for (const value of ['10000000000000000', '1000.5']) { h.el('payAmount').value = value; await h.payment.createPaymentQr(); }
    assert.equal(h.calls.length, 0); assert.equal(h.errors.length, 2);
});

test('grouped payment amounts reach cash and QR APIs as complete numbers', async () => {
    const cash=harness(async()=>({orderId:15,status:0,grandTotal:9000,paidTotal:9000,balanceDue:0,payments:[]}));
    cash.el('payMethod').value='0';cash.el('payAmount').value='30.000.000';
    await cash.payment.addPayment();assert.equal(cash.calls[0][1].amount,30000000);
    const transfer=harness(async(_,body)=>qr(1,body.amount));
    transfer.el('payAmount').value='5.000';await transfer.payment.createPaymentQr();
    assert.equal(transfer.calls[0][1].amount,5000);
    await transfer.payment.cancelPaymentQr();
    transfer.el('payAmount').value='5.001';await transfer.payment.createPaymentQr();
    assert.equal(transfer.calls.length,3);
    assert.equal(transfer.calls[2][1].amount,5001,'Overpaid QR retains the full requested transfer');
});

function pendingConflict(savedQr, orderId = 15) {
    return { errorCode: 'POS_QR_PENDING', message: 'QR trước chưa được xử lý. Xác nhận đã nhận tiền hoặc hủy QR trước khi tạo QR mới.',
        metadata: { orderId, qrId: savedQr?.qr.id || 1, savedQr } };
}

test('seven repeated attempts reopen the pending QR and show its warning without changing the saved or entered amounts', async () => {
    let created = 0;
    const original = qr(1, 2000);
    const h = harness(async () => {
        if (created) throw pendingConflict({ qr: original, status: 'Pending', canCancel: true, readOnly: false });
        created++; return original;
    });
    await h.payment.createPaymentQr();
    h.el('payAmount').value = '3000';
    for (let i = 0; i < 7; i++) await h.payment.createPaymentQr();
    assert.equal(created, 1);
    assert.equal(h.el('paymentQrImage').src, 'saved-1');
    assert.equal(h.el('paymentQrAmount').textContent, '2.000');
    assert.equal(h.el('payAmount').value, '3000');
    assert.equal(h.el('paymentQrPendingWarning').hidden, false);
    assert.match(h.el('paymentQrPendingWarning').textContent, /Xác nhận đã nhận tiền hoặc hủy/);
    assert.equal(h.el('paymentQrPopupTitle').textContent, 'QR trước chưa được xử lý');
    assert.equal(h.modalShows.filter(id => id === 'paymentQrModal').length, 8);
    assert.equal(h.modalShows.includes('paymentQrPendingModal'), false);
    assert.equal(h.el('btnConfirmPaymentQrPaid').style.display, 'none');
    assert.equal(h.el('btnCancelPaymentQr').hidden, false);
    assert.equal(h.storage.size, 0);
});

test('closing QR and scanning more items reopens the exact unpaid QR as a warning popup without changing its amount', async () => {
    let created = 0;
    const original = qr(1, 2000);
    const h = harness(async () => {
        if (created) throw pendingConflict({ qr: original, status: 'Pending', canCancel: true, readOnly: false });
        created++; return original;
    });
    h.el('payAmount').value = '2.000';
    await h.payment.createPaymentQr();
    h.window.bootstrap.Modal.getInstance(h.el('paymentQrModal')).hide();
    // The order identity stays the same while the persisted cart gains more items.
    h.state.business.currentDraft = { ...h.state.business.currentDraft, grandTotal: 12000, balanceDue: 8000 };
    h.el('payAmount').value = '8.000';
    await h.payment.createPaymentQr();
    assert.equal(created, 1);
    assert.equal(h.calls.length, 2);
    assert.ok(h.calls.every(([url]) => url.endsWith('/cart/current/payment-qr')), 'Closing never cancels or records a transfer');
    assert.equal(h.modalShows.at(-1), 'paymentQrModal');
    assert.equal(h.el('paymentQrModal').classList.contains('show'), true);
    assert.equal(h.el('paymentQrPopupTitle').textContent, 'QR trước chưa được xử lý');
    assert.equal(h.el('paymentQrPendingWarning').hidden, false);
    assert.equal(h.el('paymentQrImage').src, 'saved-1');
    assert.equal(h.el('paymentQrAmount').textContent, '2.000');
    assert.match(h.el('paymentQrPendingBalance').textContent, /2\.000/);
    assert.match(h.el('paymentQrPendingBalance').textContent, /8\.000/);
    assert.equal(h.el('payAmount').value, '8.000');
});

test('server conflict restores the unresolved QR even when a different archived QR is currently open', async () => {
    const h = harness(async () => { throw pendingConflict({ qr: qr(2, 3000), status: 'Pending', canCancel: true, readOnly: false }); });
    h.reopen({ ...qr(1, 2000), readOnly: true, canCancel: false });
    await h.payment.createPaymentQr();
    assert.equal(h.el('paymentQrImage').src, 'saved-2');
    assert.equal(h.el('btnConfirmPaymentQrPaid').style.display, 'none');
});

test('ACB received conflict offers bank checking and does not enable manual confirmation or cancellation', async () => {
    const h = harness(async () => { throw pendingConflict({ qr: { ...qr(1, 2000), automaticConfirmation: true },
        status: 'Received', canCancel: false, readOnly: false, message: 'ACB đã xác nhận tiền.' }); });
    await h.payment.createPaymentQr();
    assert.equal(h.el('btnConfirmPaymentQrPaid').style.display, 'none');
    assert.equal(h.el('btnCancelPaymentQr').hidden, true);
    assert.equal(h.el('btnCheckAcbPayment').hidden, false);
    assert.equal(h.el('paymentQrPendingWarning').hidden, false);
});

test('pending warning clears when cancellation succeeds and a fresh QR is created', async () => {
    let pending = true;
    const h = harness(async url => {
        if (url.endsWith('/cancel')) { pending = false; return { success: true }; }
        if (pending) throw pendingConflict({ qr: qr(1, 2000), status: 'Pending', canCancel: true });
        return qr(2, 3000);
    });
    await h.payment.createPaymentQr();
    await h.payment.cancelPaymentQr();
    h.el('payAmount').value = '3000';
    await h.payment.createPaymentQr();
    assert.equal(h.el('paymentQrImage').src, 'saved-2');
    assert.equal(h.el('paymentQrPendingWarning').hidden, true);
    assert.equal(h.calls[1][0], '/admin/pos/payment-qr/1/cancel');
    assert.equal(h.calls[2][1].amount, 3000);
    assert.equal(h.modalShows.at(-1), 'paymentQrModal');
});

test('pending conflict arriving after a cart change cannot reopen the previous order QR', async () => {
    let release;
    const h = harness(() => new Promise((_, reject) => { release = reject; }));
    const creating = h.payment.createPaymentQr();
    h.state.business.currentDraft = { orderId: 16, balanceDue: 5000 };
    release(pendingConflict({ qr: qr(1, 2000), status: 'Pending', canCancel: true }));
    await creating;
    assert.equal(h.el('paymentQrImage').src, undefined);
    assert.equal(h.errors.length, 0);
});

test('pending QR without an image displays the recovery warning and cannot display a fabricated QR', async () => {
    const h = harness(async () => { throw pendingConflict(null); });
    await h.payment.createPaymentQr();
    assert.equal(h.el('paymentQrImage').src, undefined);
    assert.match(h.errors[0], /chưa được xử lý/);
    assert.deepEqual(h.modalShows, ['paymentQrPendingModal']);
    assert.match(h.el('paymentQrPendingMessage').textContent, /chưa được xử lý/);
    assert.equal(h.el('paymentQrPendingHistoryLink').href, '/admin/acb/payments/orders/15');
});

test('missing image on the pending QR cannot reuse an archived QR image as the recovery popup', async () => {
    const h = harness(async () => { throw pendingConflict({ qr: { ...qr(1, 2000), qrDataUrl: '' }, status: 'Pending', canCancel: true }); });
    h.reopen({ ...qr(7, 7000), readOnly: true, canCancel: false });
    h.window.bootstrap.Modal.getInstance(h.el('paymentQrModal')).hide();
    const previousQrShows = h.modalShows.filter(id => id === 'paymentQrModal').length;
    await h.payment.createPaymentQr();
    assert.equal(h.modalShows.filter(id => id === 'paymentQrModal').length, previousQrShows);
    assert.equal(h.modalShows.at(-1), 'paymentQrPendingModal');
    assert.equal(h.el('paymentQrModal').classList.contains('show'), false);
    assert.equal(h.calls.length, 1);
});

test('the missing-image warning owns Enter and Ctrl+Enter instead of mutating the payment form underneath', async () => {
    const h = harness(async url => {
        if (url.endsWith('/payment-qr')) throw pendingConflict(null);
        throw new Error('A payment action escaped the warning dialog: ' + url);
    }, new Map(), { autoAcknowledge: false });
    h.payment.bindEvents();
    h.el('paymentModalEl').classList.add('show');
    await h.payment.createPaymentQr();
    assert.equal(h.el('paymentQrPendingModal').classList.contains('show'), true);
    h.el('payReference').value = 'TEST-STILL-UNPAID';
    await h.key('Enter', h.el('paymentQrPendingModal'));
    await h.key('Enter', h.el('paymentQrPendingModal'), { ctrlKey: true });
    assert.equal(h.calls.length, 1, 'Only the initial rejected QR request is allowed');
});

test('an id-only conflict automatically loads the exact pending QR from saved history', async () => {
    const h = harness(async () => { throw pendingConflict(null); });
    const opened = [];
    h.history.refresh = async () => ({ orderId: 15, latestQrId: 2,
        items: [{ qrId: 2, canReopen: true }, { qrId: 1, canReopen: true }] });
    h.history.open = async (id, warning) => {
        opened.push(id); h.reopen({ ...qr(id, 2000), pendingWarningMessage: warning }); return true;
    };
    await h.payment.createPaymentQr();
    assert.deepEqual(opened, [1]);
    assert.equal(h.el('paymentQrImage').src, 'saved-1');
    assert.equal(h.el('paymentQrPendingWarning').hidden, false);
    assert.equal(h.calls.length, 1);
});

test('the exact pending QR still reopens when its history list fails but the saved attempt can be read', async () => {
    const h = harness(async () => { throw pendingConflict(null); });
    const opened = [];
    h.history.refresh = async () => undefined;
    h.history.open = async (id, warning) => {
        opened.push(id); h.reopen({ ...qr(id, 2000), pendingWarningMessage: warning }); return true;
    };
    await h.payment.createPaymentQr();
    assert.deepEqual(opened, [1]);
    assert.equal(h.modalShows.at(-1), 'paymentQrModal');
    assert.equal(h.el('paymentQrImage').src, 'saved-1');
    assert.equal(h.el('paymentQrAmount').textContent, '2.000');
    assert.equal(h.modalShows.includes('paymentQrPendingModal'), false);
});

test('changing carts while saved QR is loading cannot leave a warning on the new order', async () => {
    let release;
    const h = harness(async () => { throw pendingConflict(null); });
    h.history.refresh = async () => ({ orderId: 15, items: [{ qrId: 1, canReopen: true }] });
    const loading = new Promise(done => { release = done; });
    h.history.open = async () => {
        h.state.business.currentDraft = { orderId: 16, balanceDue: 5000 };
        await loading; return false;
    };
    const creating = h.payment.createPaymentQr();
    release(); await creating;
    assert.equal(h.el('paymentQrImage').src, undefined);
    assert.equal(h.errors.length, 0);
});

test('bank transfer above the order total is recorded in full while card overpayment is blocked', async()=>{
    const h=harness(async(_,body)=>({orderId:15,status:0,grandTotal:9000,paidTotal:4000+body.amount,balanceDue:0,payments:[]}));
    h.el('payMethod').value='2';h.el('payAmount').value='12.000';h.el('payReference').value='BANK-RECEIPT';
    await h.payment.addPayment();assert.equal(h.calls.length,0);assert.match(h.errors[0],/không cho phép thanh toán dư/);
    h.el('payMethod').value='1';
    await h.payment.addPayment();
    assert.equal(h.calls.length,1);assert.equal(h.calls[0][1].amount,12000);
    assert.equal(h.state.business.currentDraft.paidTotal,16000);
});

test('exact, quick and increased payment values stay grouped without changing their amounts',()=>{
    const h=harness(async()=>{});h.payment.setPayAmountExact({focus:false});
    assert.equal(h.el('payAmount').value,'5.000');
    h.payment.setPayAmountQuick(200000);assert.equal(h.el('payAmount').value,'200.000');
    h.payment.increasePayAmount(50000);assert.equal(h.el('payAmount').value,'250.000');
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

test('cash finalization follows collection with its paid order and no timer gap', async () => {
    const h = harness(async () => ({ orderId: 15, status: 0, grandTotal: 9000, paidTotal: 9000, balanceDue: 0, payments: [] }));
    h.el('payMethod').value = '0'; h.el('payAmount').value = '5000';
    await h.payment.addPayment();
    assert.equal(h.timers.some(t => t.delay === 150), false);
    assert.equal(h.calls[1][0], '/admin/pos/15/finalize');
    assert.equal(h.calls.length, 2);
});
test('repeated collection and finalize requests are ignored through the whole cash checkout and print once', async () => {
    let receive, finalize;
    const h = harness(async url => url.endsWith('/payments') ? new Promise(resolve => { receive = resolve; }) : new Promise(resolve => { finalize = resolve; }));
    h.el('payMethod').value = '0'; h.el('payAmount').value = '10000';
    const checkout = h.payment.addPayment();
    await Promise.all(Array.from({ length: 8 }, () => h.payment.addPayment()));
    await h.payment.finalizeFromPaymentModal();
    assert.equal(h.calls.length, 1);
    receive({ orderId: 15, status: 0, grandTotal: 9000, paidTotal: 14000, balanceDue: 0, changeDue: 5000, payments: [{ method: 'Cash', amount: 10000 }] });
    await new Promise(setImmediate);
    assert.equal(h.calls.length, 2); assert.equal(h.calls[1][0], '/admin/pos/15/finalize');
    await Promise.all(Array.from({ length: 8 }, () => h.payment.addPayment()));
    await h.payment.finalizeFromPaymentModal();
    assert.equal(h.calls.length, 2, 'No extra collection or finalize while closing the paid order');
    finalize({ orderId: 15 }); await checkout;
    await h.payment.finalizeFromPaymentModal();
    assert.equal(h.calls.length, 2); assert.equal(h.printCalls.length, 1);
});
test('a failed cash finalize unlocks a retry without collecting or printing the cash twice', async () => {
    let fail = true;
    const h = harness(async url => {
        if (url.endsWith('/payments')) return { orderId: 15, status: 0, grandTotal: 9000, paidTotal: 9000, balanceDue: 0, payments: [] };
        if (fail) throw Error('finalize temporarily failed');
        return { orderId: 15 };
    });
    h.el('payMethod').value = '0'; h.el('payAmount').value = '5000';
    await assert.rejects(h.payment.addPayment(), /temporarily failed/);
    assert.equal(h.printCalls.length, 0);
    fail = false; await h.payment.finalizeFromPaymentModal();
    assert.equal(h.calls.filter(([url]) => url.endsWith('/payments')).length, 1);
    assert.equal(h.calls.filter(([url]) => url.endsWith('/finalize')).length, 2);
    assert.equal(h.printCalls.length, 1);
});
test('manual VND collection rejects fractions and out-of-range values before storing or posting', async () => {
    const h = harness(async () => { throw new Error('unexpected collection'); });
    h.el('payMethod').value = '0';
    for (const value of ['-1', '0', '20.5', '10000000000000000']) {
        h.el('payAmount').value = value; await h.payment.addPayment();
    }
    assert.equal(h.calls.length, 0); assert.equal(h.storage.size, 0);
});

test('successful finalize marks only the resulting order for immediate post-payment print', async () => {
    const h=harness(async()=>({orderId:15,status:2,changeDue:0,payments:[{method:'Cash',amount:9000}]}));
    h.state.business.currentDraft.balanceDue=0;h.el('payAmount').value='0';
    await h.payment.finalizeFromPaymentModal();
    assert.deepEqual(h.printCalls,[[15,'80',true,true,undefined,false]]);
    assert.equal(h.calls[0][0],'/admin/pos/15/finalize');
});
test('failed finalize never grants post-payment print intent',async()=>{
    const h=harness(async()=>{throw Error('finalize failed')});
    h.state.business.currentDraft.balanceDue=0;h.el('payAmount').value='0';
    await assert.rejects(h.payment.finalizeFromPaymentModal(),/finalize failed/);
    assert.deepEqual(h.printCalls,[]);
});
test('manual QR marks immediate print only when the server finalized and claimed a print',async()=>{
    for(const [finalized,printUrl,expected] of [[true,'/print',true],[false,'/print',false],[true,null,null]]){
        const h=harness(async()=>({qrId:1,orderId:15,finalized,paidAmount:5000,remainingAmount:0,printUrl}));
        h.reopen(qr(1,5000));await h.payment.confirmPaymentQrPaid();
        assert.deepEqual(h.printCalls,expected===null?[]:[[15,'80',true,expected,null,false]]);
    }
});
