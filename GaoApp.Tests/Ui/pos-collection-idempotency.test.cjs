const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const context = { window: {} };
vm.runInNewContext(fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.payment.js'), 'utf8'), context);
const factory = context.window.PosPayment.createCollectionIntents;
function storage() { const map = new Map(); return { getItem: k => map.get(k), setItem: (k,v) => map.set(k,v), removeItem: k => map.delete(k) }; }
const input = { method: 0, amount: 20, referenceCode: null, provider: null };
test('lost response and reload retry exactly the original collection even when UI amount changed', () => {
    const disk = storage(), first = factory(disk, randomUUID).prepare(10, input);
    const afterReload = factory(disk, randomUUID).prepare(10, { ...input, amount: 40 });
    assert.equal(JSON.stringify(first.body), JSON.stringify(afterReload.body));
    assert.equal(afterReload.recovered, true);
});
test('acknowledged identical installment receives a new identity; a duplicate acknowledgment cannot clear it', () => {
    const intents = factory(storage(), randomUUID), first = intents.prepare(10, input);
    intents.complete(first.body);
    const second = intents.prepare(10, input);
    assert.notEqual(first.body.clientRequestId, second.body.clientRequestId);
    intents.complete(first.body);
    assert.equal(intents.prepare(10, input).body.clientRequestId, second.body.clientRequestId);
});
test('cart switch preserves each pending intent and never retargets the original request', () => {
    const intents = factory(storage(), randomUUID), first = intents.prepare(10, input), second = intents.prepare(11, input);
    assert.notEqual(first.body.clientRequestId, second.body.clientRequestId);
    assert.equal(intents.prepare(10, input).body.orderId, 10);
    assert.equal(intents.prepare(10, input).body.clientRequestId, first.body.clientRequestId);
});
test('unavailable or corrupt persistence blocks issuing a new collection', () => {
    assert.throws(() => factory({ getItem: () => null, setItem: () => { throw new Error('storage blocked'); } }, randomUUID).prepare(10, input), /storage blocked/);
    assert.throws(() => factory({ getItem: () => '{broken' }, randomUUID).prepare(10, input));
});
