const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js'), 'utf8');
const applySource = source.slice(source.indexOf('        function applyScreenData('), source.indexOf('        function applyDraftData('));
const initStart = source.indexOf('        async function init() {');
const initSource = source.slice(initStart, source.indexOf('        return {', initStart));
const screen = id => ({ currentCart: { currentOrderId: id }, currentDraft: { orderId: id, balanceDue: 23500 }, heldOrders: [], draftOrders: [] });
const deferred = () => { let resolve; return { promise: new Promise(done => { resolve = done; }), resolve: () => resolve() }; };

function fixture(options = {}) {
    const events = [], reads = [], renders = [], bindings = [], listeners = new Map(), errors = [];
    let initialized = false, snapshotReads = 0;
    const snapshot = options.snapshot === undefined ? screen(15) : options.snapshot;
    const status = { connected: options.connected !== false, ready: true, ...options.status };
    const env = {
        window: {
            PosOffline: {
                async init() { await options.gate?.promise; initialized = true; events.push('offline-ready'); },
                status: () => status,
                initialScreen() {
                    assert.equal(initialized, true, 'No snapshot access before offline safety checks finish');
                    snapshotReads++; return snapshot;
                }
            },
            PosError: {
                redirectToShiftIfNeeded: code => code === 'POS_SHIFT_NOT_OPEN',
                handle: error => errors.push(error), getUiBehavior: () => 'modal'
            },
            addEventListener: (name, callback) => listeners.set(name, callback)
        },
        posState: { business: {}, offline: { isOnline: false, lastSyncAt: 'previous-sync' }, runtime: {} },
        keyboard: { bind: () => bindings.push('keyboard') },
        renderScreen: (data, state) => { renders.push({ data, isOnline: state.offline.isOnline }); events.push('render'); },
        renderNetworkBanner() {}, refreshUiLocks() {}, clearAllRetryUi() {},
        focusBarcodeInput: () => events.push('focus'), nowIso: () => '2026-10-27T08:00:00Z',
        markScreenRefreshCompleted: (reason, scope) => events.push('complete:' + reason + ':' + scope),
        readBootstrapError: () => options.bootstrapError || null,
        restorePendingInvoiceIntent: () => events.push('restore-invoice'),
        initSignalR: () => events.push('signalr'),
        async requestScreenRefresh(request) {
            reads.push(request); events.push('screen-read');
            if (!options.rejectRefresh) env.applyScreenData(screen(99), { focusBarcode: request.focusBarcode });
        }
    };
    for (const name of ['registerRetryScopes', 'bindModuleEvents', 'bindModalEvents', 'bindRuntimeEvents', 'bindMetaPanelToggle'])
        env[name] = () => bindings.push(name);
    vm.createContext(env); vm.runInContext(applySource + '\n' + initSource, env);
    return { env, events, reads, renders, bindings, listeners, errors, snapshot, status, snapshotReads: () => snapshotReads };
}

test('bootstrap screen renders only after offline safety checks and avoids a duplicate full screen request', async () => {
    const gate = deferred(), f = fixture({ gate });
    const startup = f.env.init();
    assert.equal(f.snapshotReads(), 0); assert.equal(f.renders.length, 0);
    assert.deepEqual(f.bindings, []); assert.deepEqual(f.reads, []);
    gate.resolve(); await startup;
    assert.equal(f.renders.length, 1); assert.equal(f.renders[0].data, f.snapshot);
    assert.equal(f.env.posState.business.currentOrderId, 15);
    assert.equal(f.snapshotReads(), 1); assert.deepEqual(f.reads, []);
    assert.equal(f.bindings.length, 6);
    assert.ok(f.events.indexOf('offline-ready') < f.events.indexOf('render'));
    assert.ok(f.events.includes('complete:init-bootstrap:full'));
    assert.ok(f.events.indexOf('render') < f.events.indexOf('signalr'));
});

test('an authorized pending journal snapshot is used directly without overwriting it from the server', async () => {
    const local = { ...screen(1500000015), pendingCount: 3 };
    const f = fixture({ snapshot: local, status: { pending: 3 } });
    await f.env.init();
    assert.equal(f.env.posState.business.currentDraft.orderId, 1500000015);
    assert.equal(f.renders[0].data.pendingCount, 3);
    assert.deepEqual(f.reads, []);
});

test('a validated offline snapshot stays offline and does not claim a new server synchronization', async () => {
    const f = fixture({ connected: false });
    await f.env.init();
    assert.equal(f.env.posState.offline.isOnline, false);
    assert.equal(f.env.posState.offline.lastSyncAt, 'previous-sync');
    assert.equal(f.renders[0].isOnline, false);
    assert.deepEqual(f.reads, []);
});

test('when no safe initial snapshot is exposed startup keeps the existing full refresh path', async () => {
    const f = fixture({ snapshot: null }); await f.env.init();
    assert.equal(f.reads.length, 1);
    assert.equal(f.reads[0].reason, 'init'); assert.equal(f.reads[0].scope, 'full');
    assert.equal(f.reads[0].force, true);
    assert.equal(f.env.posState.business.currentOrderId, 99);
});

test('older offline API without an initial snapshot accessor still loads the screen normally', async () => {
    const f = fixture(); delete f.env.window.PosOffline.initialScreen;
    await f.env.init();
    assert.equal(f.reads.length, 1); assert.equal(f.renders.length, 1);
    assert.equal(f.snapshotReads(), 0);
});

test('missing shift redirect finishes before bindings, snapshot use or a screen request', async () => {
    const f = fixture({ status: { sessionIssue: { code: 'POS_SHIFT_NOT_OPEN' } } });
    await f.env.init();
    assert.equal(f.snapshotReads(), 0); assert.deepEqual(f.reads, []);
    assert.deepEqual(f.bindings, []); assert.deepEqual(f.renders, []);
});

test('an existing server bootstrap error is handled without displaying an initial snapshot', async () => {
    const error = { errorCode: 'POS_AUTH_FORBIDDEN', message: 'Denied' };
    const f = fixture({ bootstrapError: error }); await f.env.init();
    assert.equal(f.snapshotReads(), 0); assert.deepEqual(f.renders, []);
    assert.deepEqual(f.reads, []); assert.deepEqual(f.errors, [error]);
});

test('a rejected session with no authorized snapshot cannot render a stale server screen', async () => {
    const f = fixture({ snapshot: null, status: { locked: true }, rejectRefresh: true });
    await f.env.init();
    assert.equal(f.snapshotReads(), 1); assert.equal(f.reads.length, 1);
    assert.deepEqual(f.renders, []); assert.equal(f.env.posState.business.currentDraft, undefined);
});

test('normal full screen refresh retains its existing online bookkeeping outside the initial snapshot path', () => {
    const f = fixture({ connected: false });
    f.env.applyScreenData(screen(16), { focusBarcode: false });
    assert.equal(f.env.posState.offline.isOnline, true);
    assert.equal(f.env.posState.offline.lastSyncAt, '2026-10-27T08:00:00Z');
    assert.equal(f.events.includes('focus'), false);
});

test('later journal synchronization still refreshes the screen after the initial bootstrap render', async () => {
    const f = fixture(); await f.env.init();
    assert.equal(f.reads.length, 0);
    await f.listeners.get('pos:offline-synced')();
    assert.equal(f.reads.length, 1); assert.equal(f.reads[0].reason, 'offline-synced');
    assert.equal(f.reads[0].focusBarcode, false);
});
