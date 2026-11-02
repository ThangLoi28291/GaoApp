const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const code = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/bank-notifications.admin.js'), 'utf8');
const ids = ['bankNotificationDeviceRows', 'bankNotificationWaitingRows', 'bankNotificationHistoryRows'];
function fixture(replies = []) {
    let nextTimer = 0, replacements = 0;
    const timers = new Map(), events = {}, requests = [];
    const elements = new Map([
        ['bankNotificationLiveState', {}],
        ['bankNotificationLiveStatus', { textContent: '', dataset: { liveUrl: '/admin/bank-notifications/live-state' } }],
        ['adminBankApprovalModal', { classList: { contains: () => false }, addEventListener() {} }],
        ['bankNotificationHistory', { dataset: { pendingQrIds: '' } }]
    ]);
    for (const id of ids) elements.set(id, { children: [], replaceChildren() { replacements++; } });
    const document = {
        hidden: false, activeElement: null,
        getElementById: id => elements.get(id),
        addEventListener: (name, callback) => { events[name] = callback; },
        createDocumentFragment: () => ({ appendChild() {} })
    };
    const window = { addEventListener: (name, callback) => { events[name] = callback; } };
    const navigator = { onLine: true };
    const context = {
        window, document, navigator, AbortController,
        setTimeout: (callback, delay) => { const id = ++nextTimer; timers.set(id, { callback, delay }); return id; },
        clearTimeout: id => timers.delete(id),
        DOMParser: class {
            parseFromString(html) {
                return { getElementById: id => {
                    if (id === 'bankNotificationLiveState') return html === 'login' ? null : {};
                    if (id === 'bankNotificationHistory') return { dataset: { pendingQrIds: '' } };
                    if (html === 'incomplete' && id === ids[2]) return null;
                    return ids.includes(id) ? { children: [] } : null;
                } };
            }
        },
        fetch: async (url, options) => {
            requests.push({ url, options });
            const reply = replies.shift() || {};
            if (reply instanceof Error) throw reply;
            if (reply.gate) await reply.gate;
            return { ok: (reply.status || 200) < 400, status: reply.status || 200, redirected: reply.redirected || false, text: async () => reply.html || 'valid' };
        }
    };
    vm.runInNewContext(code, context);
    const run = async (delay = 3000) => {
        const entry = Array.from(timers).find(([, timer]) => timer.delay === delay);
        assert.ok(entry, `Scheduled timer ${delay} exists`);
        timers.delete(entry[0]);
        return entry[1].callback();
    };
    return { document, navigator, events, elements, requests, run, timers, replacements: () => replacements,
        status: () => elements.get('bankNotificationLiveStatus').textContent };
}
test('empty admin page starts read-only live polling without Bootstrap', async () => {
    const f = fixture();
    await f.run();
    assert.equal(f.requests.length, 1);
    assert.equal(f.requests[0].url, '/admin/bank-notifications/live-state');
    assert.equal(f.requests[0].options.cache, 'no-store');
    assert.equal(f.requests[0].options.method, undefined, 'refresh cannot submit financial approval');
    assert.equal(f.replacements(), 3);
    assert.match(f.status(), /3 giây/);
});
test('failed refresh keeps rows and recovers on the next poll', async () => {
    const f = fixture([{ status: 503 }, {}]);
    await f.run(); assert.equal(f.replacements(), 0); assert.match(f.status(), /Giữ danh sách/);
    await f.run(); assert.equal(f.replacements(), 3); assert.equal(f.requests.length, 2);
});
test('incomplete and login HTML are rejected before any table changes', async () => {
    for (const html of ['incomplete', 'login']) {
        const f = fixture([{ html }]);
        await f.run(); assert.equal(f.replacements(), 0); assert.match(f.status(), /Chưa cập nhật/);
    }
});
test('expired authorization stops polling while preserving the last rows', async () => {
    for (const reply of [{ status: 401 }, { status: 403 }, { redirected: true }]) {
        const f = fixture([reply]);
        await f.run();
        assert.equal(f.replacements(), 0); assert.match(f.status(), /Đăng nhập lại/);
        assert.equal(f.timers.size, 0);
        await f.events.online(); assert.equal(f.requests.length, 1);
    }
});
test('online events cannot overlap an in-flight request and coalesce a follow-up', async () => {
    let release; const gate = new Promise(resolve => { release = resolve; });
    const f = fixture([{ gate }, {}]);
    const pending = f.run();
    await f.events.online(); await f.events.online();
    assert.equal(f.requests.length, 1);
    release(); await pending;
    await f.run(0); assert.equal(f.requests.length, 2); assert.equal(f.replacements(), 6);
});
test('hidden page and edited live select pause polling; config input does not', async () => {
    const f = fixture();
    f.document.hidden = true;
    await f.run(); assert.equal(f.requests.length, 0);
    f.document.hidden = false;
    f.document.activeElement = { matches: () => true, closest: () => ({}) };
    await f.events.visibilitychange(); assert.equal(f.requests.length, 0);
    f.document.activeElement = { matches: () => true, closest: () => null };
    await f.events.visibilitychange(); assert.equal(f.requests.length, 1);
});
test('network return and a cached-page return resume without reloading', async () => {
    const f = fixture();
    f.navigator.onLine = false;
    await f.run(); assert.equal(f.requests.length, 0); assert.match(f.status(), /Mất kết nối/);
    f.navigator.onLine = true;
    await f.events.online(); assert.equal(f.requests.length, 1);
    f.events.pagehide(); assert.equal(f.timers.size, 0);
    await f.events.pageshow({ persisted: true }); assert.equal(f.requests.length, 2);
});
test('opening a modal during an in-flight read discards the snapshot', async () => {
    let release; const gate = new Promise(resolve => { release = resolve; });
    const f = fixture([{ gate }]);
    const pending = f.run();
    f.elements.get('adminBankApprovalModal').classList.contains = () => true;
    release(); await pending;
    assert.equal(f.replacements(), 0);
    await f.run(); assert.equal(f.requests.length, 1);
});
