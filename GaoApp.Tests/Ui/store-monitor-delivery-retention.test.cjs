const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const sandbox = { window: {} };
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/store-monitor.delivery.js'), 'utf8'), sandbox);
const wall = sandbox.window.GaoDeliveryWall;
const completed = Date.parse('2026-10-09T01:00:00Z');
const iso = n => new Date(n).toISOString();
const order = state => ({ orderId: 1, code: 'GH-CLOSED', state, completedAtUtc: iso(completed), updatedAtUtc: iso(completed), recipient: 'Khách đã hoàn tất', lines: [] });

test('settled and cancelled cards disappear exactly at 60 seconds from committed completion', () => {
    for (const state of ['Settled', 'Cancelled']) {
        const snapshot = { orders: [order(state)], completedCardLifetimeSeconds: 60 };
        assert.equal(wall.visibleOrders(snapshot, completed + 59999).length, 1);
        assert.equal(wall.visibleOrders(snapshot, completed + 60000).length, 0);
        assert.equal(wall.visibleOrders(snapshot, completed + 3600000).length, 0);
        assert.doesNotMatch(wall.render(snapshot, true, completed + 60000, { rows: snapshot.orders }), /data-key="delivery-1"|Khách đã hoàn tất/);
    }
});

test('unfinished delivery states have no time limit', () => {
    for (const state of Object.keys(wall.states).filter(s => !['Settled', 'Cancelled'].includes(s)))
        assert.equal(wall.visibleOrders({ orders: [order(state)] }, completed + 86400000).length, 1, state);
});

test('later trip updates, duplicates and reconnects do not extend a completed card lifetime', () => {
    const closed = { ...order('Settled'), updatedAtUtc: iso(completed + 120000) };
    const snapshot = { orders: [closed], activeCount: 0 };
    for (let i = 0; i < 3; i++) assert.equal(wall.visibleOrders(snapshot, completed + 60000).length, 0);
    assert.equal(wall.expiresAt(closed, snapshot), completed + 60000);
    assert.equal(closed.state, 'Settled');
    assert.equal(closed.completedAtUtc, iso(completed));
});

test('older snapshots use their saved timestamp; invalid finished timestamps cannot persist forever', () => {
    const legacy = { ...order('Settled'), completedAtUtc: null };
    assert.equal(wall.visibleOrders({ orders: [legacy] }, completed + 60000).length, 0);
    assert.equal(wall.visibleOrders({ orders: [{ ...legacy, updatedAtUtc: 'invalid' }] }, completed).length, 0);
    assert.equal(wall.visibleOrders(null, completed).length, 0);
});
