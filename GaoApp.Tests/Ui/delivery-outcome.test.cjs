const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const api = require('../../GaoApp.Web/wwwroot/Admin/js/delivery.outcomes.js');
const scriptPath = path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/delivery.outcomes.js');
const tripPath = path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/delivery.trip.js');
const clone = value => JSON.parse(JSON.stringify(value));
const version = 'AAAAAAAAAAc=', nextVersion = 'AAAAAAAAAAg=';
const guid = '12345678-1234-1234-1234-123456789abc';
const tick = () => new Promise(resolve => setImmediate(resolve));

function detail() {
    return { storeId: 6, tripId: 11, revision: 3, version, state: 'Departed', courierUserId: 8, courierName: 'Người giao An',
        serverTimeUtc: '2026-10-08T09:00:00Z', departedAtUtc: '2026-10-08T08:45:00Z',
        capabilities: { canEditDraft: false, canAbandonDraft: false, canHandover: false, canDepart: false, canReassignCourier: false, canRequestCancellation: true },
        outcomeCapabilities: { canReportOwn: false, canReportOnBehalf: true, canRecordReturn: true },
        returnToStore: null, allOutcomesComplete: false, readyForReconciliation: false,
        orders: [21, 22].map((id, index) => ({ deliveryOrderId: id, code: 'GH-' + id, version, revision: 5, position: index + 1,
            recipientName: index ? 'Khách Minh' : 'Khách <Lan>', recipientPhone: '0900000000', recipientAddress: 'Địa chỉ thử',
            state: 'Delivering', isActive: true, canReportOutcome: true, sourceWarehouseId: 3 + index, sourceLegalEntityId: 4 + index,
            sourceWarehouseName: 'Kho ' + (index + 1), sourceLegalEntityName: 'Chủ hàng ' + (index + 1),
            custody: { tripId: 11, courierUserId: 8, courierName: 'Người giao An', custodyEventId: 91 + index }, outcome: null,
            lines: [{ lineId: 31 + index, itemName: index ? 'Nước' : 'Gạo <đặc biệt>', baseUnitName: index ? 'Chai' : 'kg',
                approvedBaseQuantityText: index ? '3' : '4', dispatchedBaseQuantityText: index ? '3' : '4',
                acceptedReturnedBaseQuantityText: '0', inCustodyBaseQuantityText: index ? '3' : '4' }] })) };
}
function fields(outcome = 'Partial', delivered = '2.5', returned = '1.5') {
    return { outcome, reason: outcome === 'Full' ? null : 'Khách nhận một phần', reportSource: 'Phone',
        reportedCashText: '15000', reportedTransferText: '12000', customerReceivedAtUtc: null,
        lines: [{ lineId: 31, deliveredBaseQuantityText: delivered, reportedReturnBaseQuantityText: returned }] };
}
function expected(model, operation = 'outcome', orderId = 21) {
    return model.orders.map(row => ({ deliveryOrderId: row.deliveryOrderId, version: row.version, revision: row.revision,
        state: operation === 'outcome' && row.deliveryOrderId === orderId ? row.state === 'CancellationPending' ? row.state : 'AwaitingReconciliation' : row.state }));
}
function ack(model, body, operation = 'outcome', orderId = 21) {
    const appliedVersion = model.revision === 3 ? nextVersion : 'AAAAAAAAAAk=';
    return { clientRequestId: body.clientRequestId, tripId: 11, operation, appliedRevision: model.revision + 1,
        appliedVersion, recordedAtUtc: model.serverTimeUtc, replayed: false,
        orders: expected(model, operation, orderId).map(row => operation === 'outcome' && row.deliveryOrderId === orderId ?
            { ...row, version: appliedVersion, revision: row.revision + 1 } : row) };
}
function storage() { const rows = new Map(); return { rows, getItem: key => rows.get(key) ?? null,
    setItem: (key, value) => rows.set(key, String(value)), removeItem: key => rows.delete(key) }; }

// The real renderer runs against a small DOM; no browser, shop API or business command is contacted.
class Element {
    constructor(tag, document) {
        this.tagName = tag.toUpperCase(); this.document = document; this.children = []; this.parentNode = null;
        this.attributes = new Map(); this.listeners = new Map(); this.dataset = {}; this.style = {};
        this.className = ''; this.id = ''; this.value = ''; this.disabled = false; this.hidden = false; this.checked = false; this._text = '';
    }
    get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
    set textContent(value) { this.replaceChildren(); this._text = String(value ?? ''); }
    append(...children) { for (const child of children) { if (child.parentNode) child.remove(); child.parentNode = this; this.children.push(child); } }
    replaceChildren(...children) { for (const child of this.children) child.parentNode = null; this.children = []; this._text = ''; this.append(...children); }
    remove() { if (this.parentNode) { this.parentNode.children = this.parentNode.children.filter(child => child !== this); this.parentNode = null; } }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    removeAttribute(name) { this.attributes.delete(name); }
    addEventListener(name, listener) { const rows = this.listeners.get(name) || []; rows.push(listener); this.listeners.set(name, rows); }
    async emit(name) { for (const listener of this.listeners.get(name) || []) await listener({ target: this, preventDefault() {} }); }
    async click() { if (!this.disabled) await this.emit('click'); }
    focus() { this.document.activeElement = this; }
    matches(selector) {
        if (selector.startsWith('#')) return this.id === selector.slice(1);
        if (selector.startsWith('.')) return selector.slice(1).split('.').every(name => this.className.split(/\s+/).includes(name));
        const attr = selector.match(/^(\w+)?\[([\w-]+)(?:="([^"]+)")?\]$/);
        if (attr) { const value = attr[2].startsWith('data-') ? this.dataset[attr[2].slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase())] : this.getAttribute(attr[2]);
            return (!attr[1] || this.tagName === attr[1].toUpperCase()) && (attr[3] === undefined ? value != null : value === attr[3]); }
        return this.tagName === selector.toUpperCase();
    }
    querySelectorAll(selector) { return this.children.flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]); }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
    closest(selector) { return this.matches(selector) ? this : this.parentNode?.closest(selector) || null; }
}

async function fixture(options = {}) {
    const document = { activeElement: null }; document.createElement = tag => new Element(tag, document); document.body = document.createElement('body');
    const workspace = document.createElement('div'); workspace.className = 'delivery-outcomes-workspace'; workspace.dataset = { store: '6', actor: '9', trip: '11' }; document.body.append(workspace);
    for (const id of ['outcomeMessage', 'outcomePending', 'outcomeFacts', 'outcomeReturn', 'outcomeProgress', 'outcomeList', 'outcomeDetail', 'outcomeReload']) {
        const element = document.createElement(id === 'outcomeReload' ? 'button' : 'div'); element.id = id; workspace.append(element);
    }
    document.getElementById = id => document.body.querySelector('#' + id);
    document.querySelector = selector => selector === 'meta[name=request-verification-token]' ? { content: 'csrf' } : document.body.querySelector(selector);
    const disk = options.storage || storage(), intervals = [], state = { model: options.model || detail(), posts: [], reads: [], networkFail: false, freshFail: false };
    if (options.readonly) { state.model.outcomeCapabilities = { canReportOwn: false, canReportOnBehalf: false, canRecordReturn: false }; for (const row of state.model.orders) row.canReportOutcome = false; }
    const location = { origin: 'https://synthetic.test' }, window = { document, location, localStorage: disk, addEventListener() {} };
    const response = (status, body) => ({ ok: status >= 200 && status < 300, status, text: async () => JSON.stringify(clone(body)) });
    const fetch = async (url, request = {}) => {
        if (request.method !== 'POST') { state.reads.push(url); assert.equal(url, '/admin/api/delivery-trips/11'); return state.freshFail && state.posts.length ? response(503, { message: 'Bản mới chưa tải được' }) : response(200, state.model); }
        const body = JSON.parse(request.body), operation = url.endsWith('/outcome') ? 'outcome' : 'return-to-store', orderId = operation === 'outcome' ? Number(url.split('/').at(-2)) : null;
        state.posts.push({ url, body, raw: request.body, headers: request.headers });
        if (state.networkFail) throw new Error('Synthetic response lost');
        const result = ack(state.model, body, operation, orderId);
        state.model.revision = result.appliedRevision; state.model.version = result.appliedVersion;
        if (operation === 'outcome') {
            const row = state.model.orders.find(x => x.deliveryOrderId === orderId), changed = result.orders.find(x => x.deliveryOrderId === orderId);
            Object.assign(row, { revision: changed.revision, version: changed.version, state: changed.state });
            row.outcome = { orderRevision: row.revision, orderVersion: row.version, tripId: 11, deliveryOrderId: orderId,
                custodyEventId: row.custody.custodyEventId, actorUserId: 9, actorName: 'Nhân viên Bình', courierUserId: 8, courierName: state.model.courierName,
                reportSource: body.reportSource, outcome: body.outcome, reason: body.reason, reportedCashText: body.reportedCashText,
                reportedTransferText: body.reportedTransferText, recordedAtUtc: result.recordedAtUtc, customerReceivedAtUtc: body.customerReceivedAtUtc,
                isCurrentValid: true, lines: body.lines.map(line => ({ ...line, acceptedReturnedAtReportBaseQuantityText: '0', pendingReturnBaseQuantityText: line.reportedReturnBaseQuantityText })) };
        } else { state.model.returnToStore = { tripRevision: result.appliedRevision, actorUserId: 9, actorName: 'Nhân viên Bình', courierUserId: 8,
            courierName: state.model.courierName, recordedAtUtc: result.recordedAtUtc, confirmationNote: body.confirmationNote }; state.model.outcomeCapabilities.canRecordReturn = false; }
        state.model.allOutcomesComplete = state.model.orders.every(row => row.outcome?.isCurrentValid);
        state.model.readyForReconciliation = Boolean(state.model.returnToStore) && state.model.allOutcomesComplete;
        return response(200, result);
    };
    let sequence = 0;
    const context = { window, document, location, localStorage: disk, fetch, setInterval: callback => { intervals.push(callback); },
        crypto: { randomUUID: () => '12345678-1234-1234-1234-' + (++sequence).toString(16).padStart(12, '0') },
        Option: function (text, value) { const option = document.createElement('option'); option.textContent = text; option.value = value; return option; },
        URL, URLSearchParams, Date, Intl, BigInt, console };
    vm.runInNewContext(fs.readFileSync(tripPath, 'utf8'), context, { filename: tripPath });
    vm.runInNewContext(fs.readFileSync(scriptPath, 'utf8'), context, { filename: scriptPath });
    const settle = async () => { for (let i = 0; i < 7; i++) await tick(); }; await settle();
    const get = id => { const element = document.getElementById(id); assert.ok(element, 'Missing #' + id + ': ' + document.getElementById('outcomeMessage').textContent); return element; };
    return { state, document, workspace, get, disk, settle, api: window.DeliveryOutcomeUi,
        click: async id => { await get(id).click(); await settle(); },
        input: async (id, value) => { const element = get(id); element.value = value; await element.emit('input'); await settle(); },
        qty: async (id, value) => { const element = workspace.querySelector('[data-delivered-line="' + id + '"]'); assert.ok(element); element.value = value; await element.emit('input'); await settle(); },
        poll: async () => { for (const callback of intervals) await callback(); await settle(); } };
}

test('Canonical report quantities conserve dispatch and accepted returns; money stays a whole-dong declaration', () => {
    const model = detail(), body = api.reportBody(model, 21, fields(), guid);
    assert.equal(body.reportedCourierUserId, 8); assert.equal(body.expectedOrderVersion, version);
    assert.equal(body.expectedTripVersion, version); assert.deepEqual(body.lines, fields().lines);
    assert.equal(api.textQuantity(api.parseQuantity('0.0001')), '0.0001');
    assert.equal(api.money('9999999999999999'), 9999999999999999n);
    for (const amount of ['-1', '1.5', '10000000000000000']) assert.throws(() => api.money(amount));
    for (const changed of [{ ...fields(), reason: '' }, { ...fields(), reportedCashText: '1.5' },
        { ...fields(), customerReceivedAtUtc: '2026-10-08T08:00:00Z' }, fields('Full', '2.5', '1.5'), fields('Partial', '2.5', '1')])
        assert.throws(() => api.reportBody(model, 21, changed, guid));
    // A stale GET clock must not reject a later real customer time; the server owns the upper bound.
    assert.equal(api.reportBody(model, 21, { ...fields(), customerReceivedAtUtc: '2026-10-08T09:01:00Z' }, guid).customerReceivedAtUtc, '2026-10-08T09:01:00Z');
    model.orders[0].lines[0].acceptedReturnedBaseQuantityText = '2'; model.orders[0].lines[0].inCustodyBaseQuantityText = '2';
    assert.throws(() => api.reportBody(model, 21, fields(), guid));
    assert.equal(api.reportBody(model, 21, fields('NotDelivered', '0', '4'), guid).reportedCashText, '15000');
});

test('Saved report validation keeps stale signed pending facts and rejects invented custody or mismatched source', () => {
    const model = detail(), row = model.orders[0];
    row.outcome = { orderRevision: 5, orderVersion: version, tripId: 11, deliveryOrderId: 21, custodyEventId: 91,
        actorUserId: 9, courierUserId: 8, reportSource: 'Phone', outcome: 'Partial', reason: 'Thiếu',
        reportedCashText: '0', reportedTransferText: '0', recordedAtUtc: model.serverTimeUtc, customerReceivedAtUtc: null,
        isCurrentValid: false, lines: [{ ...fields().lines[0], acceptedReturnedAtReportBaseQuantityText: '0', pendingReturnBaseQuantityText: '-0.5' }] };
    row.lines[0].acceptedReturnedBaseQuantityText = '2'; row.lines[0].inCustodyBaseQuantityText = '2';
    assert.equal(api.validateDetail(model, 6, 11).orders[0].outcome.isCurrentValid, false);
    const wrong = clone(model); wrong.orders[0].outcome.isCurrentValid = true; assert.throws(() => api.validateDetail(wrong, 6, 11));
    wrong.orders[0].outcome.courierUserId = 10; assert.throws(() => api.validateDetail(wrong, 6, 11));
    const noCustody = detail(); noCustody.orders[0].custody = null; noCustody.orders[0].lines[0].inCustodyBaseQuantityText = null;
    assert.throws(() => api.reportBody(noCustody, 21, fields(), guid));
});

test('Intent ACK joins every active order and requires only the outcome target to advance', () => {
    const model = detail(), body = api.reportBody(model, 21, fields(), guid), disk = storage();
    const store = api.createIntentStore(disk, 'https://test', 6, '9', 11), intent = store.begin('outcome', body, 21, expected(model));
    store.sending(intent); const good = ack(model, body);
    for (const invalid of [{ ...good, orders: good.orders.slice(0, 1) }, { ...good, clientRequestId: 'different' },
        { ...good, orders: [good.orders[0], good.orders[0]] }, { ...good, orders: [{ ...good.orders[0], version }, good.orders[1]] },
        { ...good, orders: [good.orders[0], { ...good.orders[1], revision: 6 }] }]) {
        assert.throws(() => store.acknowledge(invalid)); assert.equal(store.read().body, JSON.stringify(body));
    }
    store.acknowledge(good); assert.equal(store.read(), null);
    const arrival = api.returnBody(model, { physicalReturnConfirmed: true, confirmationNote: '' }, guid);
    store.begin('return-to-store', arrival, null, expected(model, 'return-to-store'));
    const returnAck = ack(model, arrival, 'return-to-store', null);
    assert.throws(() => store.acknowledge({ ...returnAck, orders: [{ ...returnAck.orders[0], revision: 6 }, returnAck.orders[1]] }));
    store.acknowledge(returnAck); assert.equal(store.read(), null);
});

test('Uncertain intent stays byte-exact across reload and tab scope; older GET epochs cannot replace a newer revision', () => {
    const model = detail(), disk = storage(), body = api.reportBody(model, 21, fields(), guid);
    const first = api.createIntentStore(disk, 'https://test', 6, '9', 11); first.begin('outcome', body, 21, expected(model));
    const attempt = first.sending(first.read()), reloaded = api.createIntentStore(disk, 'https://test', 6, '9', 11);
    assert.equal(reloaded.read().body, JSON.stringify(body)); assert.equal(reloaded.read().uncertain, true);
    assert.equal(reloaded.rejectFirstAttempt(attempt), false); assert.throws(() => reloaded.begin('outcome', body, 21, expected(model)));
    for (const scoped of [api.createIntentStore(disk, 'https://test', 7, '9', 11), api.createIntentStore(disk, 'https://test', 6, '10', 11), api.createIntentStore(disk, 'https://test', 6, '9', 12)]) assert.equal(scoped.read(), null);
    assert.equal(api.canApply(model, { ...model, revision: 2 }, 1, 1), false);
    assert.equal(api.canApply(model, model, 1, 2), false); assert.equal(api.canApply(model, model, 2, 2, 4), false);
});

test('Actual shortcuts are local, require a failure reason and do not count drafts as saved reports', async () => {
    const f = await fixture(); assert.match(f.get('outcomeProgress').textContent, /0\/2/);
    assert.match(f.get('outcomeDetail').textContent, /Gạo <đặc biệt>/);
    await f.click('outcomeNone'); await f.click('outcomeSave'); assert.equal(f.state.posts.length, 0);
    assert.match(f.get('outcomeMessage').textContent, /lý do/); assert.match(f.get('outcomeProgress').textContent, /0\/2/);
    await f.input('outcomeReason', 'Khách từ chối'); await f.input('outcomeCash', '15000');
    await f.click('outcomeSave'); assert.equal(f.state.posts.length, 1);
    assert.equal(f.state.posts[0].body.outcome, 'NotDelivered'); assert.equal(f.state.posts[0].body.reportedCashText, '15000');
    assert.deepEqual(f.state.posts[0].body.lines, [{ lineId: 31, deliveredBaseQuantityText: '0', reportedReturnBaseQuantityText: '4' }]);
    assert.match(f.get('outcomeProgress').textContent, /1\/2/); assert.equal(f.state.model.orders[1].outcome, null);
});

test('Actual partial form sends selected order only, exact source version and CSRF, then renders genuine saved facts', async () => {
    const f = await fixture(); await f.qty(31, '2,5'); await f.input('outcomeKind', 'Partial');
    await f.input('outcomeReason', 'Khách nhận một phần'); await f.input('outcomeSource', 'Phone');
    await f.input('outcomeTransfer', '12000'); await f.click('outcomeSave');
    const sent = assertSingle(f.state.posts); assert.equal(sent.url, '/admin/api/delivery-trips/11/orders/21/outcome');
    assert.equal(sent.headers.RequestVerificationToken, 'csrf'); assert.equal(sent.body.expectedTripVersion, version);
    assert.deepEqual(sent.body.lines, [{ lineId: 31, deliveredBaseQuantityText: '2.5', reportedReturnBaseQuantityText: '1.5' }]);
    assert.deepEqual(Object.keys(sent.body).sort(), ['clientRequestId', 'customerReceivedAtUtc', 'expectedOrderVersion', 'expectedTripVersion', 'lines', 'outcome', 'reason', 'reportSource', 'reportedCashText', 'reportedCourierUserId', 'reportedTransferText'].sort());
    assert.match(f.get('outcomeDetail').textContent, /chưa xác nhận/); assert.match(f.get('outcomeDetail').textContent, /Nhân viên Bình/);
    assert.match(f.get('outcomeProgress').textContent, /1\/2/);
});

test('Drafts survive switching and polling; discard removes only the selected order draft', async () => {
    const f = await fixture(); await f.qty(31, '2.5'); await f.input('outcomeReason', 'Đơn thứ nhất');
    const reads = f.state.reads.length; await f.poll(); assert.equal(f.state.reads.length, reads);
    await f.click('outcomeOrder-22'); await f.qty(32, '1'); await f.input('outcomeReason', 'Đơn thứ hai');
    await f.click('outcomeOrder-21'); assert.equal(f.workspace.querySelector('[data-delivered-line="31"]').value, '2.5');
    assert.equal(f.get('outcomeReason').value, 'Đơn thứ nhất'); await f.click('outcomeDiscard');
    assert.equal(f.workspace.querySelector('[data-delivered-line="31"]').value, '');
    await f.click('outcomeOrder-22'); assert.equal(f.workspace.querySelector('[data-delivered-line="32"]').value, '1');
    assert.equal(f.get('outcomeReason').value, 'Đơn thứ hai'); assert.equal(f.state.posts.length, 0);
});

test('Saving one dirty order rebases another only when its own version stays unchanged; conflict reload preserves stale inputs', async () => {
    const f = await fixture(); await f.qty(31, '2.5'); await f.input('outcomeKind', 'Partial'); await f.input('outcomeReason', 'Đơn thứ nhất');
    await f.click('outcomeOrder-22'); await f.qty(32, '1'); await f.input('outcomeKind', 'Partial'); await f.input('outcomeReason', 'Đơn thứ hai');
    await f.click('outcomeOrder-21'); await f.click('outcomeSave'); assert.equal(f.state.posts.length, 1);
    await f.click('outcomeOrder-22'); assert.equal(f.workspace.querySelector('[data-delivered-line="32"]').value, '1');
    assert.equal(f.get('outcomeReason').value, 'Đơn thứ hai'); assert.equal(f.get('outcomeSave').disabled, false);
    await f.click('outcomeSave'); assert.equal(f.state.posts.length, 2);
    assert.equal(f.state.posts[1].body.expectedTripVersion, nextVersion); assert.equal(f.state.posts[1].body.expectedOrderVersion, version);
    const stale = await fixture(); await stale.qty(31, '2'); await stale.input('outcomeReason', 'Giữ bản đang nhập');
    stale.state.model.revision++; stale.state.model.version = nextVersion;
    stale.state.model.orders[0].version = nextVersion; stale.state.model.orders[0].revision++;
    await stale.click('outcomeReload'); assert.equal(stale.workspace.querySelector('[data-delivered-line="31"]').value, '2');
    assert.equal(stale.get('outcomeReason').value, 'Giữ bản đang nhập'); assert.equal(stale.get('outcomeSave').disabled, true);
    assert.equal(stale.state.posts.length, 0); await stale.click('outcomeDiscard'); assert.equal(stale.get('outcomeSave').disabled, false);
});

test('Arrival-first requires a checkbox and actual courier; all order versions and missing-report progress stay unchanged', async () => {
    const f = await fixture(), original = clone(f.state.model.orders); await f.click('outcomeRecordReturn'); assert.equal(f.state.posts.length, 0);
    const check = f.get('outcomeReturnConfirmed'); check.checked = true; await check.emit('change');
    await f.input('outcomeReturnNote', 'Đã về tiệm'); await f.click('outcomeRecordReturn');
    const sent = assertSingle(f.state.posts); assert.equal(sent.url, '/admin/api/delivery-trips/11/return-to-store');
    assert.equal(sent.body.confirmedCourierUserId, 8); assert.equal(sent.body.physicalReturnConfirmed, true);
    assert.deepEqual(f.state.model.orders, original); assert.equal(f.state.model.readyForReconciliation, false);
    assert.match(f.get('outcomeReturn').textContent, /Còn 2 đơn/); assert.match(f.get('outcomeProgress').textContent, /0\/2/);
});

test('Lost response keeps pending body, blocks edits, and reload never POSTs until explicit exact retry', async () => {
    const f = await fixture(); f.state.networkFail = true; await f.click('outcomeFull'); await f.click('outcomeSave');
    const sent = assertSingle(f.state.posts); assert.equal(f.get('outcomeSave').disabled, true); assert.equal(f.get('outcomePending').hidden, false);
    await f.poll(); assert.equal(f.state.posts.length, 1);
    const reloaded = await fixture({ storage: f.disk }); assert.equal(reloaded.state.posts.length, 0);
    assert.equal(reloaded.get('outcomeSave').disabled, true); await reloaded.click('outcomeRetry');
    assert.equal(assertSingle(reloaded.state.posts).raw, sent.raw); assert.equal(reloaded.get('outcomePending').hidden, true);
});

test('Read-only and post-ACK stale reads block writes without losing a confirmed report', async () => {
    const read = await fixture({ readonly: true }); assert.equal(read.get('outcomeSave').disabled, true);
    assert.equal(read.get('outcomeFull').disabled, true); assert.equal(read.document.getElementById('outcomeRecordReturn'), null);
    await read.click('outcomeSave'); assert.equal(read.state.posts.length, 0);
    const f = await fixture(); f.state.freshFail = true; await f.click('outcomeFull'); await f.click('outcomeSave');
    assert.equal(f.state.posts.length, 1); assert.equal(f.get('outcomeSave').disabled, true);
    assert.match(f.get('outcomeMessage').textContent, /Cần tải bản mới/); assert.equal(f.get('outcomePending').hidden, true);
    assert.equal(f.state.model.orders[0].outcome.outcome, 'Full');
    f.state.freshFail = false; await f.click('outcomeReload'); assert.equal(f.get('outcomeSave').disabled, false);
    assert.match(f.get('outcomeProgress').textContent, /1\/2/);
});

function assertSingle(rows) { assert.equal(rows.length, 1); return rows[0]; }
