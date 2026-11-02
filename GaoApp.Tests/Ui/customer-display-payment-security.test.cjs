const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.customer-display-payment.js'), 'utf8');
function harness() {
    const pending = [], calls = [], timers = new Map();
    let timerId = 0;
    const context = { window: {}, Date, AbortController,
        fetch: (url, init) => { assert.equal(url, '/admin/pos/customer-display/payment-state'); assert.equal(init.cache, 'no-store');
            return new Promise((resolve, reject) => pending.push({ resolve, reject })); },
        setTimeout: (fn, ms) => { const id = ++timerId; timers.set(id, { fn, ms }); return id; }, clearTimeout: id => timers.delete(id) };
    vm.runInNewContext(source, context);
    const client = context.window.CustomerDisplayPayment.create({ storeId: () => 1, terminalId: () => 3,
        ...Object.fromEntries(['qr', 'preview', 'recorded', 'hide', 'clear', 'unavailable'].map(name => [name, payload => calls.push({ name, payload })])) });
    const snapshot = (mode, key = mode, more = {}) => ({ storeId: 1, terminalId: 3, orderId: 10, mode, key,
        validUntilUtc: new Date(Date.now() + 30000).toISOString(),
        payload: { amount: 100, qrDataUrl: 'data:image/png;base64,dGVzdA==' }, ...more });
    function complete(index, data, status = 200) { pending[index].resolve({ ok: status === 200, json: async () => data }); }
    return { client, calls, pending, timers, snapshot, complete };
}
test('a delayed response cannot replace the current cart QR or a newer confirmed state', async () => {
    const f = harness(); const old = f.client.refresh(); const current = f.client.invalidate();
    f.complete(1, f.snapshot('qr', 'new-qr', { orderId: 11 })); await current;
    f.complete(0, f.snapshot('qr', 'old-qr')); await old;
    assert.equal(f.calls.filter(c => c.name === 'qr').length, 1);
    assert.equal(f.client.isAvailable(), true);
});
test('each committed receipt is shown once despite duplicate events and polling', async () => {
    const f = harness();
    for (let i = 0; i < 3; i++) {
        const job = i ? f.client.invalidate() : f.client.refresh();
        f.complete(i, f.snapshot('recorded', 'payment:10:23')); await job;
    }
    assert.equal(f.calls.filter(c => c.name === 'recorded').length, 1);
});
test('a committed partial receipt survives preview and hide transitions after collection', async () => {
    const f = harness();
    const receipt = { key: 'payment:10:23', payload: { finalized: false, remainingAmount: 50 }, validUntilUtc: new Date(Date.now() + 30000).toISOString() };
    let job = f.client.refresh(); f.complete(0, f.snapshot('preview', 'preview-1', { receipt })); await job;
    job = f.client.invalidate(); f.complete(1, f.snapshot('idle', 'idle', { receipt })); await job;
    assert.equal(f.calls.filter(c => c.name === 'recorded').length, 1);
    assert.equal(f.calls.find(c => c.name === 'recorded').payload.remainingAmount, 50);
});
test('slow polling is coalesced while a new event can invalidate an in-flight read', async () => {
    const f = harness(), first = f.client.refresh(); await f.client.refresh();
    assert.equal(f.pending.length, 1);
    const next = f.client.invalidate();
    assert.equal(f.pending.length, 2);
    f.complete(1, f.snapshot('idle')); await next;
    f.complete(0, f.snapshot('qr')); await first;
    assert.equal(f.calls.filter(c => c.name === 'qr').length, 0);
});
test('foreign terminal, expired QR, external image and non-image data cannot be displayed', async () => {
    const invalid = [ { storeId: 2 }, { terminalId: 4 }, { validUntilUtc: new Date(0).toISOString() },
        { payload: { qrDataUrl: 'https://attacker.invalid/qr' } }, { payload: { qrDataUrl: 'data:image/svg+xml;base64,dGVzdA==' } } ];
    for (const patch of invalid) {
        const f = harness(), job = f.client.refresh(); f.complete(0, f.snapshot('qr', 'invalid', patch)); await job;
        assert.equal(f.calls.filter(c => c.name === 'qr').length, 0);
        assert.equal(f.calls.at(-1).name, 'unavailable');
    }
});
test('loss of server connectivity clears financial overlays and reconnect reloads the server QR', async () => {
    const f = harness(); let job = f.client.refresh(); f.complete(0, f.snapshot('qr')); await job;
    job = f.client.refresh(); f.pending[1].reject(new Error('offline')); await job;
    assert.equal(f.client.isAvailable(), false);
    assert.equal(f.calls.at(-2).name, 'clear');
    job = f.client.refresh(); f.complete(2, f.snapshot('qr')); await job;
    assert.equal(f.calls.filter(c => c.name === 'qr').length, 2);
    const deadline = [...f.timers.values()].find(t => t.ms <= 5000);
    deadline.fn(); assert.equal(f.client.isAvailable(), false);
});
