const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = name => fs.readFileSync(require.resolve(`../../GaoApp.Web/wwwroot/Admin/js/pos/${name}.js`), 'utf8');
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; };
test('PosError HTTP rejections retain their status instead of becoming unknown network failures', () => {
    const { common } = fixture();
    for (const [statusCode, type] of [[400, 'validation'], [403, 'auth'], [409, 'conflict'], [503, 'system']]) {
        const error = common.normalizeApiError({ statusCode, message: 'Rejected' });
        assert.equal(error.status, statusCode);
        assert.equal(error.type, type);
    }
    assert.equal(common.normalizeApiError(new Error('Connection closed')).status, 0);
});
function element() {
    const attributes = new Map(), classes = new Set();
    return { nodeType: 1, tagName: 'BUTTON', disabled: false, dataset: {}, textContent: '',
        getAttribute: name => attributes.get(name) || null, setAttribute: (name, value) => attributes.set(name, value), removeAttribute: name => attributes.delete(name),
        classList: { add: (...names) => names.forEach(x => classes.add(x)), remove: (...names) => names.forEach(x => classes.delete(x)), contains: name => classes.has(name) } };
}
function fixture(useStateHelpers = true) {
    const context = { window: {}, navigator: { onLine: true }, console, AbortController,
        document: { querySelector: () => null, getElementById: () => null }, requestAnimationFrame() {} };
    vm.createContext(context);
    vm.runInContext(source('pos.state'), context);
    const state = context.window.PosState.create();
    if (!useStateHelpers) delete context.window.PosState;
    vm.runInContext(source('pos.common'), context);
    const common = context.window.PosCommon, button = element();
    common.registerUiLock(state, { target: button, requireOnline: true, busyScopes: ['cartMutate', 'checkout'] });
    return { context, common, state, button };
}

for (const helpers of [true, false]) {
    test(`checkout locks immediately while a scan is in flight (PosState helpers: ${helpers})`, async () => {
        const f = fixture(helpers), reply = deferred();
        const action = f.common.runPosAction(f.state, 'barcode:scan:123', () => reply.promise, { scopes: ['cartMutate'] });
        assert.equal(f.button.disabled, true);
        reply.resolve({ orderId: 1 }); await action;
        assert.equal(f.button.disabled, false);
    });
}

for (const outcome of ['success', 'failure', 'abort']) {
    test(`checkout unlocks after scan ${outcome} without waiting for an offline heartbeat`, async () => {
        const f = fixture(), reply = deferred();
        const action = f.common.runPosAction(f.state, 'barcode:scan:123', () => reply.promise, { scopes: ['cartMutate'], displayMode: 'silent' });
        // Offline persistence emits a status event while the cart mutation still owns its lock.
        f.common.refreshUiLocks(f.state);
        assert.equal(f.button.disabled, true);
        if (outcome === 'success') reply.resolve({ orderId: 1 });
        else reply.reject(Object.assign(new Error('scan failed'), { name: outcome === 'abort' ? 'AbortError' : 'Error', status: 500 }));
        await action;
        assert.equal(f.state.network.busyScopes.size, 0);
        assert.equal(f.button.disabled, false);
        assert.equal(f.button.getAttribute('aria-disabled'), 'false');
        assert.equal(f.button.classList.contains('is-ui-locked'), false);
    });
}

test('pending-action-only controls update without a heartbeat', async () => {
    const f = fixture(), button = element(), reply = deferred();
    f.common.registerUiLock(f.state, { target: button, pendingActions: ['test:request'] });
    const action = f.common.runPosAction(f.state, 'test:request', () => reply.promise, {});
    assert.equal(button.disabled, true);
    reply.resolve({}); await action;
    assert.equal(button.disabled, false);
});

test('finishing a scan preserves an independent checkout lock', async () => {
    const f = fixture(), reply = deferred();
    const action = f.common.runPosAction(f.state, 'barcode:scan', () => reply.promise, { scopes: ['cartMutate'] });
    f.state.network.busyScopes.add('checkout');
    reply.resolve({}); await action;
    assert.equal(f.button.disabled, true);
    assert.equal(f.state.network.busyScopes.has('checkout'), true);
});

test('finishing a scan does not enable online-only checkout while disconnected', async () => {
    const f = fixture(), reply = deferred();
    const action = f.common.runPosAction(f.state, 'barcode:scan', () => reply.promise, { scopes: ['cartMutate'] });
    f.state.offline.isOnline = false;
    reply.resolve({}); await action;
    assert.equal(f.button.disabled, true);
});

test('patching a scanned cart updates payment status alongside its new balance', () => {
    const f = fixture(), hero = element(), label = element(), actions = element(), balance = element();
    const nodes = { posSummaryHero: hero, sumPaymentStateText: label, posSummaryActionGrid: actions, sumBalance: balance };
    f.context.document.getElementById = id => nodes[id] || null;
    vm.runInContext(source('pos.render'), f.context);
    const render = f.context.window.PosRender;
    const empty = { orderId: 1, subtotal: 0, grandTotal: 0, paidTotal: 0, balanceDue: 0, changeDue: 0, lines: [], payments: [] };
    f.state.business.currentDraft = empty;
    render.renderSummaryState(empty); render.renderSummaryActionState(empty);
    const scanned = { ...empty, subtotal: 358000, grandTotal: 358000, balanceDue: 358000,
        lines: [{ lineId: 1, variantId: 1, quantity: 1, unitPrice: 358000, lineTotal: 358000 }] };
    render.syncDraftToUi(scanned, f.state);
    assert.equal(label.textContent, 'Chưa thanh toán');
    assert.equal(hero.classList.contains('pos-summary-hero--paid'), false);
    assert.equal(actions.classList.contains('is-paid'), false);
    assert.equal(balance.textContent, '358.000');
});
