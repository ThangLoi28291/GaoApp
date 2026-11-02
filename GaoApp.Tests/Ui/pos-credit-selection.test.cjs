const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { randomUUID } = require('node:crypto');

const sourceRoot = path.resolve(__dirname, '../../GaoApp.Web');
const source = fs.readFileSync(path.join(sourceRoot, 'wwwroot/Admin/js/pos/pos.payment.js'), 'utf8');
const markup = fs.readFileSync(path.join(sourceRoot, 'Areas/Admin/Views/POS/Index.cshtml'), 'utf8');
const commonContext = { window: {} };
vm.runInNewContext(fs.readFileSync(path.join(sourceRoot, 'wwwroot/Admin/js/pos/pos.common.js'), 'utf8'), commonContext);
const { parseMoneyInput, setMoneyInput, bindMoneyInput } = commonContext.window.PosCommon;

function harness() {
    const nodes = new Map(), documentListeners = new Map(), calls = [], errors = [], focused = [];
    const storage = new Map(), noop = () => {};
    class Node {
        constructor(id) {
            this.id = id; this.tagName = 'DIV'; this.value = ''; this.textContent = '';
            this.style = {}; this.dataset = {}; this.hidden = false; this.disabled = false;
            this.attributes = new Map(); this.listeners = new Map(); this.classes = new Set();
            this.classList = {
                contains: name => this.classes.has(name),
                add: (...names) => names.forEach(name => this.classes.add(name)),
                remove: (...names) => names.forEach(name => this.classes.delete(name)),
                toggle: (name, force) => {
                    const active = force === undefined ? !this.classes.has(name) : force;
                    if (active) this.classes.add(name); else this.classes.delete(name);
                    return active;
                }
            };
        }
        setAttribute(name, value) { this.attributes.set(name, String(value)); }
        getAttribute(name) { return this.attributes.get(name) ?? null; }
        removeAttribute(name) { this.attributes.delete(name); }
        addEventListener(name, callback) {
            if (!this.listeners.has(name)) this.listeners.set(name, []);
            this.listeners.get(name).push(callback);
        }
        async emit(name, event = {}) {
            for (const callback of this.listeners.get(name) || []) await callback.call(this, event);
        }
        querySelectorAll(selector) { return select(selector); }
        querySelector() { return null; }
        closest(selector) {
            if (selector === '[data-pay-method-value]' && this.getAttribute('data-pay-method-value') !== null) return this;
            return null;
        }
        focus() { focused.push(this.id); document.activeElement = this; }
        select() {}
        replaceChildren() {}
        add() {}
        appendChild() {}
        before() {}
        contains(target) { return target === this; }
    }
    function el(id) { if (!nodes.has(id)) nodes.set(id, new Node(id)); return nodes.get(id); }
    const methods = ['0', '1', '2'].map(method => {
        const node = el('method-' + method); node.tagName = 'BUTTON';
        node.setAttribute('data-pay-method-value', method); return node;
    });
    // Derive collection surfaces from the real Razor markup, so omitting the
    // hide marker from the view cannot silently pass this behavior test.
    const collections = [...markup.matchAll(/<[a-z][^>]*\bdata-payment-collection\b[^>]*>/gi)].map((match, index) => {
        const id = /\bid="([^"]+)"/.exec(match[0])?.[1] || 'collection-' + index;
        const node = el(id); node.setAttribute('data-payment-collection', ''); return node;
    });
    const notes = ['0', '1', '2'].map(method => {
        const node = el('method-note-' + method); node.setAttribute('data-payment-method-note', method); return node;
    });
    function select(selector) {
        if (selector === '[data-pay-method-value]') return methods;
        if (selector === '[data-payment-collection]') return collections;
        if (selector === '[data-payment-method-note]') return notes;
        return [];
    }
    const document = { activeElement: null, body: el('body'), getElementById: el,
        createElement: el, querySelector: () => null, querySelectorAll: select,
        addEventListener(name, callback) {
            if (!documentListeners.has(name)) documentListeners.set(name, []);
            documentListeners.get(name).push(callback);
        } };
    el('payMethod').value = '0'; el('payMethod').tagName = 'SELECT';
    el('payMethod').options = ['0', '1', '2'].map(value => ({ value }));
    for (const id of ['payAmount', 'payReference', 'payProvider', 'creditNote']) el(id).tagName = 'INPUT';
    for (const id of ['btnCreditPayment', 'btnAddPayment', 'btnFinalizeFromPaymentModal']) el(id).tagName = 'BUTTON';
    const state = { business: { currentDraft: { orderId: 15, status: 0, customerId: 7,
        customerName: 'TEST customer', customerCanBuyOnCredit: true, customerDebtBalance: 16500,
        grandTotal: 33500, paidTotal: 10000, balanceDue: 23500, availableDeposits: [],
        payments: [{ method: 0, amount: 10000 }] } }, ui: { busyScopes: {} } };
    const context = { console, document, Element: Node, navigator: { onLine: true }, crypto: { randomUUID },
        Option: class { constructor(text, value) { this.text = text; this.value = value; } },
        setTimeout: noop, clearTimeout: noop,
        sessionStorage: { getItem: key => storage.get(key) ?? null,
            setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) },
        window: { addEventListener: noop, PosCommon: { parseMoneyInput, setMoneyInput, bindMoneyInput,
            clearInlineError: noop, setInlineError: (_, message) => errors.push(message),
            registerUiLock: noop, refreshUiLocks: noop } } };
    vm.runInNewContext(source, context);
    const modal = { show: () => el('paymentModal').classList.add('show'),
        hide: () => el('paymentModal').classList.remove('show') };
    const payment = context.window.PosPayment.create({ posState: state,
        elements: Object.fromEntries(['payMethod', 'payAmount', 'payReference', 'payProvider', 'btnAddPayment',
            'btnFinalizeFromPaymentModal', 'btnPayExact'].map(id => [id, el(id)]).concat([['paymentModalEl', el('paymentModal')]])),
        modals: { paymentModal: modal }, helpers: {
            postJson: async (url, body) => { calls.push({ url, body }); return { ...state.business.currentDraft }; },
            runPosAction: async (_, key, action) => action(),
            renderPaymentModalDraft: noop, renderPaymentPreview: noop, renderPayments: noop,
            showSuccess: noop, showError: message => errors.push(message), focusBarcodeInput: noop,
            applyDraftActionSuccess: noop, applyScreenActionSuccess: noop, requestScreenRefresh: noop,
            openReceiptPrint: noop } });
    payment.bindEvents(); payment.openPaymentModal();
    async function key(key, options = {}) {
        const event = { key, code: key, target: el('paymentModal'), repeat: false,
            ctrlKey: false, altKey: false, shiftKey: false, metaKey: false,
            preventDefault: noop, stopPropagation: noop, ...options };
        for (const callback of documentListeners.get('keydown') || []) await callback(event);
    }
    async function choose(method) {
        await el('paymentModal').emit('click', { target: methods[Number(method)], preventDefault: noop, stopPropagation: noop });
    }
    return { payment, el, state, context, calls, errors, methods, collections, focused, key, choose };
}

test('credit selection is exclusive and hides the ordinary collection controls from the real view', async () => {
    const h = harness();
    assert.equal(h.methods[0].classList.contains('is-active'), true);
    await h.el('btnCreditPayment').emit('click');
    assert.equal(h.el('btnCreditPayment').getAttribute('aria-pressed'), 'true');
    assert.equal(h.el('paymentModal').getAttribute('data-payment-method'), 'credit');
    for (const method of h.methods) {
        assert.equal(method.classList.contains('is-active'), false);
        assert.equal(method.getAttribute('aria-pressed'), 'false');
    }
    assert.ok(h.collections.length >= 5, 'The real amount, preview, QR and collection action surfaces must be marked');
    assert.ok(h.collections.every(node => node.hidden));
    assert.equal(h.el('posCreditPanel').hidden, false);
    assert.equal(h.focused.at(-1), 'btnFinalizeFromPaymentModal');
});

for (const previousMethod of ['0', '1', '2']) {
    test(`Enter after choosing credit from method ${previousMethod} finalizes only the persisted remaining debt`, async () => {
        const h = harness(); await h.choose(previousMethod);
        h.el('payAmount').value = '999999'; h.el('payReference').value = '';
        h.el('creditNote').value = '  TEST debt note  ';
        await h.el('btnCreditPayment').emit('click');
        await h.key('Enter', { target: h.el('creditNote') });
        assert.equal(h.calls.length, 1);
        assert.equal(h.calls[0].url, '/admin/pos/15/finalize-credit');
        assert.equal(h.calls[0].body.expectedBalance, 23500);
        assert.equal(h.calls[0].body.expectedCustomerId, 7);
        assert.equal(h.calls[0].body.note, 'TEST debt note');
        assert.equal(h.state.business.currentDraft.payments.length, 1, 'Previously received cash stays recorded');
        assert.equal(h.state.business.currentDraft.paidTotal, 10000);
    });
}

test('Alt+4 follows the same credit selection and Enter path even with a stale empty card reference', async () => {
    const h = harness(); await h.choose('2');
    await h.key('4', { altKey: true });
    assert.equal(h.el('btnCreditPayment').getAttribute('aria-pressed'), 'true');
    assert.ok(h.collections.every(node => node.hidden));
    await h.key('Enter');
    assert.deepEqual(h.calls.map(call => call.url), ['/admin/pos/15/finalize-credit']);
});

test('stale add-payment clicks and direct callers cannot collect cash while credit is selected', async () => {
    const h = harness(); await h.el('btnCreditPayment').emit('click');
    h.el('payAmount').value = '23500';
    await h.el('btnAddPayment').emit('click');
    await h.payment.addPayment();
    await h.payment.createPaymentQr();
    assert.deepEqual(h.calls, []);
});

test('two quick Enter presses keep one credit finalization request and never collect cash', async () => {
    const h = harness(); await h.el('btnCreditPayment').emit('click');
    await Promise.all([h.key('Enter'), h.key('Enter')]);
    assert.deepEqual(h.calls.map(call => call.url), ['/admin/pos/15/finalize-credit']);
});

for (const method of ['0', '1', '2']) {
    test(`choosing ordinary method ${method} exits credit mode and restores its collection fields`, async () => {
        const h = harness(); await h.el('btnCreditPayment').emit('click');
        await h.choose(method);
        assert.equal(h.el('btnCreditPayment').getAttribute('aria-pressed'), 'false');
        assert.equal(h.el('paymentModal').getAttribute('data-payment-method'), method);
        assert.equal(h.el('posCreditPanel').hidden, true);
        assert.equal(h.methods[Number(method)].getAttribute('aria-pressed'), 'true');
        assert.ok(h.collections.every(node => !node.hidden));
        assert.equal(h.el('btnFinalizeFromPaymentModal').textContent, 'Chốt đơn');
    });
}

test('Alt+1 after credit restores normal cash Enter instead of finalizing debt', async () => {
    const h = harness(); await h.key('4', { altKey: true });
    await h.key('1', { altKey: true });
    h.el('payAmount').value = '10000';
    await h.key('Enter');
    assert.equal(h.calls.length, 1);
    assert.equal(h.calls[0].url, '/admin/pos/cart/current/payments');
    assert.equal(h.calls[0].body.method, 0);
    assert.equal(h.calls[0].body.amount, 10000);
});

test('repeat Enter, checkout busy and offline states cannot finalize or collect while credit is selected', async () => {
    const h = harness(); await h.el('btnCreditPayment').emit('click');
    await h.key('Enter', { repeat: true });
    h.state.ui.busyScopes.checkout = true;
    await h.key('Enter'); await h.key('Enter', { ctrlKey: true });
    h.state.ui.busyScopes.checkout = false; h.context.navigator.onLine = false;
    await h.key('Enter');
    assert.deepEqual(h.calls, []);
});

test('closing and reopening checkout clears credit selection and returns to cash', async () => {
    const h = harness(); await h.el('btnCreditPayment').emit('click');
    await h.el('paymentModal').emit('hidden.bs.modal');
    h.payment.openPaymentModal();
    assert.equal(h.el('btnCreditPayment').getAttribute('aria-pressed'), 'false');
    assert.equal(h.methods[0].getAttribute('aria-pressed'), 'true');
    assert.equal(h.el('posCreditPanel').hidden, true);
    assert.ok(h.collections.every(node => !node.hidden));
});
