const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.acb.js'), 'utf8');

function deferred() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
}
async function until(condition) {
    for (let attempt = 0; attempt < 20; attempt++) {
        if (condition()) return;
        await new Promise(setImmediate);
    }
    assert.ok(condition(), 'Expected immediate processing without advancing the polling timer');
}
function posHarness(fetch, offlineStatus) {
    const listeners = new Map();
    const completed = [];
    const notes = [];
    const elements = new Map();
    const links = [];
    const intervals = [];
    const created = [];
    let now = Date.now();
    class ClockDate extends Date { static now() { return now; } }
    const element = () => ({ dataset: {}, before() {}, replaceChildren(text) { notes.push(text); }, appendChild(link) { links.push(link); }, setAttribute() {} });
    const context = {
        fetch, Date: ClockDate, Map,
        setInterval(callback) { intervals.push(callback); },
        CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
        document: {
            createElement: () => { const value = element(); created.push(value); return value; },
            createTextNode: text => text,
            getElementById: id => { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); },
            querySelector: () => ({ value: 'test-antiforgery' }),
            addEventListener: (name, listener) => listeners.set(name, listener)
        },
        window: {
            navigator: { onLine: true },
            PosOffline: offlineStatus ? { status: () => offlineStatus } : undefined,
            addEventListener: (name, listener) => listeners.set(name, listener),
            dispatchEvent: event => completed.push(event)
        }
    };
    vm.runInNewContext(source, context, { filename: 'pos.acb.js' });
    return { emit: name => listeners.get(name)?.(), completed, notes, links, elements, acb: context.window.PosAcb,
        navigator: context.window.navigator,
        notice: () => created.find(value => value.className === 'alert alert-info'),
        advance(ms) { now += ms; intervals.forEach(callback => callback()); } };
}
const response = value => ({ ok: true, json: async () => value });
const settle = () => new Promise(setImmediate);

test('countdown follows actual 30-second first lookup and 8-second retries without overlapping requests', async () => {
    const firstLookup = deferred();
    let bankLookups = 0;
    const pos = posHarness(async url => {
        if (url.endsWith('/terminal-pending')) return response([{ qrId: 42 }]);
        if (url.includes('refresh=true') && ++bankLookups === 1) return firstLookup.promise;
        return response({ status: 'Pending', orderId: 100 });
    });
    pos.acb.track({ id: 42 }); pos.emit('DOMContentLoaded'); await settle();
    const value = pos.elements.get('acbPaymentCountdownValue');
    assert.equal(value.textContent, '00:30');
    pos.advance(20000); await settle();
    assert.equal(value.textContent, '00:10'); assert.equal(bankLookups, 0);
    pos.acb.track({ id: 42 }); // Reopening must retain the original deadline.
    assert.equal(value.textContent, '00:10');
    pos.advance(10000); await settle();
    assert.equal(bankLookups, 1);
    assert.equal(pos.elements.get('acbPaymentCountdownLabel').textContent, 'Đang kiểm tra ACB…');
    assert.equal(value.textContent, '…');
    pos.advance(2000); await settle(); assert.equal(bankLookups, 1);
    firstLookup.resolve(response({ status: 'Pending', orderId: 100 })); await settle();
    assert.equal(value.textContent, '00:08');
    pos.advance(7000); await settle();
    assert.equal(value.textContent, '00:01'); assert.equal(bankLookups, 1);
    pos.advance(1000); await settle();
    assert.equal(bankLookups, 2); assert.equal(value.textContent, '00:08');
});

test('checking manually skips the countdown and restarts the next 8-second wait', async () => {
    const urls = [];
    const pos = posHarness(async url => {
        urls.push(url);
        return response(url.endsWith('/terminal-pending') ? [] : { status: 'Pending', orderId: 100 });
    });
    pos.acb.track({ id: 42 }); pos.emit('DOMContentLoaded'); await settle();
    pos.advance(12000); await settle();
    assert.equal(pos.elements.get('acbPaymentCountdownValue').textContent, '00:18');
    await pos.acb.check(42);
    assert.ok(urls.at(-1).includes('refresh=true&manual=true'));
    assert.equal(pos.elements.get('acbPaymentCountdownValue').textContent, '00:08');
    pos.advance(1000); await settle();
    assert.equal(pos.elements.get('acbPaymentCountdownValue').textContent, '00:07');
});

test('network failure shows retry countdown and preserves the diagnostic while waiting', async () => {
    let lookups = 0;
    const pos = posHarness(async url => {
        if (url.endsWith('/terminal-pending')) return response([]);
        if (url.includes('refresh=true')) {
            lookups++;
            return { ok: false, json: async () => ({ message: 'Không kết nối được ACB.' }) };
        }
        return response({ status: 'Pending', orderId: 100 });
    });
    pos.acb.track({ id: 42 }); pos.emit('DOMContentLoaded'); await settle();
    pos.advance(30000); await settle();
    assert.equal(pos.elements.get('acbPaymentCountdown').dataset.phase, 'retry');
    assert.equal(pos.elements.get('acbPaymentCountdownValue').textContent, '00:08');
    assert.equal(pos.elements.get('acbPaymentCheckStatus').textContent, 'Không kết nối được ACB.');
    pos.advance(7000); await settle(); assert.equal(lookups, 1);
    assert.equal(pos.elements.get('acbPaymentCheckStatus').textContent, 'Không kết nối được ACB.');
    pos.advance(1000); await settle(); assert.equal(lookups, 2);
});

test('changing the displayed QR keeps separate deadlines and hides the timer on manual/read-only views', async () => {
    const pos = posHarness(async url => response(url.endsWith('/terminal-pending') ? [] : { status: 'Pending', orderId: 100 }));
    pos.acb.track({ id: 41 }); pos.emit('DOMContentLoaded'); await settle();
    pos.advance(10000); await settle(); pos.acb.track({ id: 42 });
    assert.equal(pos.elements.get('acbPaymentCountdownValue').textContent, '00:30');
    pos.acb.track({ id: 41 }); assert.equal(pos.elements.get('acbPaymentCountdownValue').textContent, '00:20');
    pos.acb.blur(); assert.equal(pos.elements.get('acbPaymentCountdown').hidden, true);
    pos.advance(5000); await settle(); assert.equal(pos.elements.get('acbPaymentCountdown').hidden, true);
    pos.acb.track({ id: 42 }); assert.equal(pos.elements.get('acbPaymentCountdownValue').textContent, '00:25');
});

for (const status of ['ReviewRequired', 'Cancelled', 'Received']) {
    test(`countdown stops after ${status} without another bank check`, async () => {
        let statuses = 0;
        const pos = posHarness(async url => {
            if (url.endsWith('/terminal-pending')) return response([]);
            if (url.endsWith('/complete')) return response({ orderId: 100, finalized: false, remainingAmount: 3000, printUrl: null });
            statuses++; return response({ status, orderId: 100 });
        });
        pos.emit('DOMContentLoaded'); await settle();
        await pos.acb.check(42);
        assert.equal(pos.elements.get('acbPaymentCountdown').hidden, true);
        pos.advance(60000); await settle(); assert.equal(statuses, 1);
    });
}

test('returning from a throttled background tab checks immediately instead of extending or going negative', async () => {
    let lookups = 0;
    const pos = posHarness(async url => {
        if (url.endsWith('/terminal-pending')) return response([]);
        if (url.includes('refresh=true')) lookups++;
        return response({ status: 'Pending', orderId: 100 });
    });
    pos.acb.track({ id: 42 }); pos.emit('DOMContentLoaded'); await settle();
    pos.advance(90000); await settle();
    assert.equal(lookups, 1);
    assert.equal(pos.elements.get('acbPaymentCountdownValue').textContent, '00:08');
});

test('hub notifications during a pending status request resume immediately and finalize once', async () => {
    const pendingStatus = deferred();
    const calls = [];
    let statusCalls = 0;
    let completeCalls = 0;
    const pos = posHarness(async url => {
        calls.push(url);
        if (url.endsWith('/terminal-pending')) return response([{ qrId: 42 }]);
        if (url.includes('/status?')) {
            if (++statusCalls === 1) return pendingStatus.promise;
            return response({ status: 'Received', orderId: 100 });
        }
        if (url.endsWith('/complete')) {
            completeCalls++;
            return response({ orderId: 100, printUrl: null });
        }
        throw new Error(`Unexpected URL ${url}`);
    });
    pos.emit('DOMContentLoaded');
    await until(() => statusCalls === 1);
    pos.emit('acb:payment-changed');
    pos.emit('acb:payment-changed');
    pendingStatus.resolve(response({ status: 'Pending', orderId: 100 }));
    await until(() => pos.completed.length === 1);
    assert.equal(completeCalls, 1);
    assert.equal(calls.filter(url => url.endsWith('/terminal-pending')).length, 2);
    assert.equal(pos.completed[0].detail.orderId, 100);
    assert.equal(pos.completed[0].detail.qrId, 42);
    assert.ok(calls.filter(url => url.includes('/status?')).every(url => url.endsWith('refresh=false')),
        'Hub handling reads callback state before the bank-polling deadline');
});

test('hub notification during failed discovery is retained for another immediate attempt', async () => {
    const pendingDiscovery = deferred();
    let discoveryCalls = 0;
    const pos = posHarness(async url => {
        assert.ok(url.endsWith('/terminal-pending'));
        if (++discoveryCalls === 1) return pendingDiscovery.promise;
        return response([]);
    });
    pos.emit('DOMContentLoaded');
    pos.emit('acb:payment-changed');
    pendingDiscovery.resolve({ ok: false });
    await until(() => discoveryCalls === 2);
    assert.equal(pos.completed.length, 0);
});


test('a recovered Creating attempt shows the original order lookup without recording payment', async () => {
    const pos = posHarness(async url => {
        if (url.endsWith('/terminal-pending')) return response([{ qrId: 42 }]);
        if (url.includes('/status?')) return response({ status: 'Creating', orderId: 100 });
        throw new Error(`Unexpected mutation ${url}`);
    });
    pos.emit('DOMContentLoaded');
    await until(() => pos.notes.length > 0);
    assert.match(pos.notes[0], /100/);
    assert.equal(pos.links[0].href, '/admin/acb/payments/orders/100');
    assert.equal(pos.completed.length, 0);
});


test('manual check queued behind a status call waits for fresh bank evidence and completes once', async () => {
    const pending = deferred();
    let statusCalls = 0; let completeCalls = 0; const urls = [];
    const pos = posHarness(async url => {
        urls.push(url);
        if (url.includes('/status?')) {
            if (++statusCalls === 1) return pending.promise;
            return response({ status: 'Received', orderId: 100 });
        }
        if (url.endsWith('/complete')) { completeCalls++; return response({ orderId: 100, printUrl: null }); }
        if (url.endsWith('/terminal-pending')) return response([{ qrId: 42 }]);
        throw new Error(url);
    });
    pos.acb.track({ id: 42 }); pos.emit('DOMContentLoaded');
    await until(() => statusCalls === 1);
    let finished = false;
    const checking = pos.acb.check(42).then(() => { finished = true; });
    assert.equal(pos.elements.get('btnCheckAcbPayment').disabled, true);
    assert.equal(finished, false);
    pending.resolve(response({ status: 'Pending', orderId: 100 }));
    await checking;
    assert.ok(urls.some(url => url.includes('refresh=true&manual=true')));
    assert.equal(completeCalls, 1);
    assert.equal(pos.completed.length, 1);
    assert.equal(pos.elements.get('btnCheckAcbPayment').disabled, false);
});

test('manual unpaid check reports the result in the QR popup without completing', async () => {
    const pos = posHarness(async url => {
        assert.ok(url.includes('/42/status?refresh=true&manual=true'));
        return response({ status: 'Pending', orderId: 100, lastRetrievedAtUtc: '2026-09-09T01:02:03Z' });
    });
    pos.acb.track({ id: 42 }); await pos.acb.check(42);
    assert.match(pos.elements.get('acbPaymentCheckStatus').textContent, /ACB chưa xác nhận/);
    assert.equal(pos.completed.length, 0);
});

test('manual bank outage shows safe server error and keeps the button available to retry', async () => {
    const pos = posHarness(async () => ({ ok: false, json: async () => ({ message: 'Không kết nối được ACB.' }) }));
    pos.acb.track({ id: 42 }); await pos.acb.check(42);
    assert.equal(pos.elements.get('acbPaymentCheckStatus').textContent, 'Không kết nối được ACB.');
    assert.equal(pos.elements.get('btnCheckAcbPayment').disabled, false);
    assert.equal(pos.completed.length, 0);
});


test('partial ACB receipt reports remaining balance without claiming finalize or printing', async () => {
    let completions = 0;
    const pos = posHarness(async url => {
        if (url.includes('/status?')) return response({ status: 'Received', orderId: 100 });
        if (url.endsWith('/complete')) { completions++; return response({ orderId: 100, finalized: false, paidAmount: 20000, remainingAmount: 50000, printUrl: null }); }
        throw new Error(`Unexpected URL ${url}`);
    });
    await pos.acb.check(42);
    assert.equal(completions, 1);
    assert.equal(pos.completed[0].detail.finalized, false);
    assert.equal(pos.completed[0].detail.remainingAmount, 50000);
    assert.match(pos.notes.at(-1), /còn thiếu 50[.,]000đ/);
    assert.ok(pos.notes.every(note => !note.includes('chốt đơn')));
});

test('successful pending-QR discovery clears its previous connection warning, including an empty list', async () => {
    let failDiscovery = true;
    const pos = posHarness(async () => failDiscovery ? { ok: false } : response([]));
    pos.emit('DOMContentLoaded'); await settle();
    assert.match(pos.notes.at(-1), /Chưa tải được các QR đang chờ/);
    assert.notEqual(pos.notice().hidden, true);
    failDiscovery = false;
    pos.advance(8000); await settle();
    assert.equal(pos.notice().hidden, true);
    failDiscovery = true;
    pos.advance(8000); await settle();
    assert.equal(pos.notice().hidden, false, 'A new server failure must remain visible');
});

test('successful discovery preserves a payment review warning', async () => {
    const pos = posHarness(async url => response(url.endsWith('/terminal-pending')
        ? [] : { status: 'ReviewRequired', orderId: 100, message: 'Khoản chuyển cần đối soát.' }));
    await pos.acb.check(42);
    pos.emit('acb:payment-changed'); await settle();
    assert.match(pos.notes.at(-1), /cần đối soát/);
    assert.notEqual(pos.notice().hidden, true);
});

test('offline POS pauses discovery and bank checks until all pending sales have synced', async () => {
    const status = { connected: false, pending: 3 };
    const urls = [];
    const pos = posHarness(async url => {
        urls.push(url);
        return response(url.endsWith('/terminal-pending') ? [] : { status: 'Pending', orderId: 100 });
    }, status);
    pos.acb.track({ id: 42 }); pos.emit('DOMContentLoaded');
    pos.advance(30000); pos.emit('acb:payment-changed'); await pos.acb.check(42); await settle();
    assert.deepEqual(urls, []);
    assert.equal(pos.notes.length, 0);
    assert.equal(pos.elements.get('acbPaymentCountdown').hidden, true);
    status.connected = true;
    pos.emit('pos:offline-status'); pos.advance(8000); await settle();
    assert.deepEqual(urls, [], 'A reachable server is not enough while the journal is pending');
    status.pending = 0;
    pos.emit('pos:offline-synced'); await settle();
    assert.equal(urls.filter(url => url.endsWith('/terminal-pending')).length, 1);
    assert.equal(urls.filter(url => url.includes('/status?')).length, 1);
    assert.ok(urls.every(url => !url.includes('manual=true')), 'An offline click must not queue a bank request');
});

test('server recovery with no offline sale restarts discovery immediately', async () => {
    const status = { connected: false, pending: 0 };
    let calls = 0;
    const pos = posHarness(async () => { calls++; return response([]); }, status);
    pos.emit('DOMContentLoaded'); await settle();
    assert.equal(calls, 0);
    status.connected = true; pos.emit('pos:offline-status'); await settle();
    assert.equal(calls, 1);
});

test('an in-flight discovery failure after going offline does not leave a stale warning', async () => {
    const pending = deferred();
    const status = { connected: true, pending: 0 };
    let calls = 0;
    const pos = posHarness(async () => ++calls === 1 ? pending.promise : response([]), status);
    pos.emit('DOMContentLoaded');
    status.connected = false; pos.emit('pos:offline-status');
    pending.resolve({ ok: false }); await settle();
    assert.equal(pos.notes.length, 0);
    status.connected = true; pos.emit('pos:offline-synced'); await settle();
    assert.equal(calls, 2);
});

test('offline startup pauses ACB even before the offline runtime is ready', async () => {
    let calls = 0;
    const pos = posHarness(async () => { calls++; return response([]); });
    pos.navigator.onLine = false;
    pos.emit('DOMContentLoaded'); pos.advance(8000); await settle();
    assert.equal(calls, 0);
    pos.navigator.onLine = true; pos.emit('online'); await settle();
    assert.equal(calls, 1);
});

test('going offline during a status check does not start automatic completion', async () => {
    const pending = deferred();
    const status = { connected: true, pending: 0 };
    const urls = [];
    const pos = posHarness(async url => {
        urls.push(url);
        if (url.endsWith('/terminal-pending')) return response([]);
        if (url.includes('/status?')) return pending.promise;
        return response({ orderId: 100, printUrl: null });
    }, status);
    pos.acb.track({ id: 42 }); pos.emit('DOMContentLoaded');
    await until(() => urls.some(url => url.includes('/status?')));
    status.connected = false; status.pending = 1; pos.emit('pos:offline-status');
    pending.resolve(response({ status: 'Received', orderId: 100 })); await settle();
    assert.equal(urls.filter(url => url.endsWith('/complete')).length, 0);
    assert.equal(pos.completed.length, 0);
    status.connected = true; status.pending = 0; pos.emit('pos:offline-synced'); await settle();
    assert.equal(urls.filter(url => url.endsWith('/complete')).length, 1);
    assert.equal(pos.completed.length, 1);
});
