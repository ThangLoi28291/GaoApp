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
    const model = options.model || { trip: detail(), counter: { storeId: 6, terminalId: 7, shiftId: 12, warehouseId: 3, ownerUserId: 9, isOpen: true },
        capabilities: { canFinalize: true, canReceiveReturns: true, canReconcileCash: true, canConfirmBankTransfer: true, canCredit: true },
        bankAccounts: [{ bankAccountId: 1, name: 'Synthetic bank' }],
        orders: [21, 22].map(deliveryOrderId => ({ deliveryOrderId, sourceCustomerId: null, customerCreditEligible: false, canConfirm: true, savedPreview: null, settlement: null })) };
    const disk = options.storage || storage(), intervals = [], state = { model, posts: [], previews: [], reads: [], networkFail: false, freshFail: false };
    if (options.readonly) { model.capabilities.canFinalize = false; for (const row of model.orders) row.canConfirm = false; }
    const location = { origin: 'https://synthetic.test' }, window = { document, location, localStorage: disk, addEventListener() {} };
    const response = (status, body) => ({ ok: status >= 200 && status < 300, status, text: async () => JSON.stringify(clone(body)) });
    const preview = (orderId, body) => {
        const order = model.trip.orders.find(x => x.deliveryOrderId === orderId);
        const lines = body.lines.map(line => { const source = order.lines.find(x => x.lineId === line.lineId), gross = String(Math.round(Number(line.deliveredBaseQuantityText) * 10000));
            return { ...line, newReturnBaseQuantityText: String(Number(line.actualReturnedBaseQuantityText) - Number(source.acceptedReturnedBaseQuantityText)), grossText: gross, discountText: '0', netText: gross }; });
        return { tripId: 11, deliveryOrderId: orderId, tripVersion: model.trip.version, orderVersion: order.version,
            sourceWarehouseId: order.sourceWarehouseId, sourceLegalEntityId: order.sourceLegalEntityId, sourceHash: 'server-price-proof',
            payableText: String(lines.reduce((n, x) => n + Number(x.netText), 0)), lines };
    };
    const fetch = async (url, request = {}) => {
        if (request.method !== 'POST') { state.reads.push(url); assert.equal(url, '/admin/api/delivery-trips/11/handover');
            return state.freshFail && state.posts.length ? response(503, { message: 'Bản mới chưa tải được' }) : response(200, model); }
        const body = JSON.parse(request.body), orderId = Number(url.split('/')[6]);
        if (url.endsWith('/preview')) { state.previews.push({ url, body }); return response(200, preview(orderId, body)); }
        assert.equal(url, '/admin/api/delivery-trips/11/handover/' + orderId);
        state.posts.push({ url, body, raw: request.body, headers: request.headers });
        if (state.networkFail) throw new Error('Synthetic response lost');
        const amount = preview(orderId, body).payableText, order = model.trip.orders.find(x => x.deliveryOrderId === orderId), info = model.orders.find(x => x.deliveryOrderId === orderId);
        const changedVersion = Buffer.alloc(8); changedVersion.writeBigUInt64BE(BigInt(100 + ++model.trip.revision));
        model.trip.version = order.version = changedVersion.toString('base64'); order.revision++; order.state = 'Settled';
        for (const line of order.lines) {
            line.acceptedReturnedBaseQuantityText = body.lines.find(x => x.lineId === line.lineId).actualReturnedBaseQuantityText;
            line.inCustodyBaseQuantityText = '0';
        }
        info.settlement = { deliveryOrderId: orderId, state: 'Settled', paymentKind: body.paymentKind, terminalId: body.expectedTerminalId, shiftId: body.expectedShiftId,
            sourceWarehouseId: order.sourceWarehouseId, sourceLegalEntityId: order.sourceLegalEntityId, payableText: amount,
            receivedCashText: body.actualReceivedCashText, confirmedTransferText: body.confirmedTransferText, customerDebtText: body.paymentKind === 'CustomerCredit' ? amount : '0',
            saleOrderId: amount === '0' ? null : orderId + 1000, recordedAtUtc: model.trip.serverTimeUtc };
        return response(200, { clientRequestId: body.clientRequestId, tripId: 11, deliveryOrderId: orderId, operation: 'handover',
            appliedTripRevision: model.trip.revision, appliedOrderRevision: order.revision, appliedTripVersion: model.trip.version,
            appliedOrderVersion: order.version, state: 'Settled', replayed: false, recordedAtUtc: model.trip.serverTimeUtc, settlement: info.settlement });
    };
    let sequence = 0;
    const context = { window, document, location, localStorage: disk, fetch, setInterval: callback => { intervals.push(callback); }, setTimeout, clearTimeout,
        crypto: { randomUUID: () => '12345678-1234-1234-1234-' + (++sequence).toString(16).padStart(12, '0') },
        Option: function (text, value) { const option = document.createElement('option'); option.textContent = text; option.value = value; return option; },
        URL, URLSearchParams, Date, Intl, BigInt, console };
    vm.runInNewContext(fs.readFileSync(tripPath, 'utf8'), context, { filename: tripPath });
    vm.runInNewContext(fs.readFileSync(scriptPath, 'utf8'), context, { filename: scriptPath });
    const settle = async () => { await new Promise(resolve => setTimeout(resolve, 220)); for (let i = 0; i < 5; i++) await tick(); }; await settle();
    const get = id => { const element = document.getElementById(id); assert.ok(element, 'Missing #' + id + ': ' + document.getElementById('outcomeMessage').textContent); return element; };
    return { state, document, workspace, get, disk, settle, api: window.DeliveryOutcomeUi,
        click: async id => { await get(id).click(); await settle(); },
        check: async id => { const element = get(id); element.checked = true; await element.emit('change'); await settle(); },
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

test('Current handover shortcuts only preview; failed delivery requires a reason and physical confirmation', async () => {
    const f = await fixture(); assert.match(f.get('outcomeProgress').textContent, /0\/2/);
    assert.match(f.get('outcomeDetail').textContent, /Gạo <đặc biệt>/);
    await f.click('outcomeNone'); assert.equal(f.state.posts.length, 0); assert.equal(f.get('outcomeConfirm').disabled, true);
    await f.input('outcomeReason', 'Khách từ chối'); await f.check('outcomePhysicalConfirmed');
    assert.equal(f.get('outcomeConfirm').disabled, false); await f.click('outcomeConfirm');
    const sent = assertSingle(f.state.posts); assert.equal(sent.body.outcome, 'NotDelivered');
    assert.equal(sent.body.actualReceivedCashText, '0'); assert.equal(sent.body.physicalGoodsConfirmed, true);
    assert.deepEqual(sent.body.lines, [{ lineId: 31, deliveredBaseQuantityText: '0', actualReturnedBaseQuantityText: '4' }]);
    assert.match(f.get('outcomeProgress').textContent, /1\/2/);
});

test('Partial handover sends only the selected order, current versions, CSRF and confirmed transfer', async () => {
    const f = await fixture(); await f.qty(31, '2,5'); await f.input('outcomeReason', 'Khách nhận một phần');
    await f.click('outcomePaymentBankTransfer'); await f.input('outcomeTransfer', '25000');
    assert.equal(f.get('outcomeConfirm').disabled, true);
    await f.check('outcomeTransferConfirmed'); await f.check('outcomePhysicalConfirmed'); await f.click('outcomeConfirm');
    const sent = assertSingle(f.state.posts); assert.equal(sent.url, '/admin/api/delivery-trips/11/handover/21');
    assert.equal(sent.headers.RequestVerificationToken, 'csrf'); assert.equal(sent.body.expectedTripVersion, version); assert.equal(sent.body.expectedOrderVersion, version);
    assert.deepEqual(sent.body.lines, [{ lineId: 31, deliveredBaseQuantityText: '2.5', actualReturnedBaseQuantityText: '1.5' }]);
    assert.equal(sent.body.paymentKind, 'BankTransfer'); assert.equal(sent.body.transferConfirmed, true); assert.equal(sent.body.bankAccountId, 1);
    assert.equal(sent.body.actualReceivedCashText, '0'); assert.equal(sent.body.confirmedTransferText, '25000');
    assert.match(f.get('outcomeSettlement').textContent, /25\.000/); assert.equal(f.state.model.orders[1].settlement, null);
});

test('Handover drafts survive switching and polling; discard clears only the selected order', async () => {
    const f = await fixture(); await f.qty(31, '2.5'); await f.input('outcomeReason', 'Đơn thứ nhất');
    const reads = f.state.reads.length; await f.poll(); assert.equal(f.state.reads.length, reads);
    await f.click('outcomeOrder-22'); await f.qty(32, '1'); await f.input('outcomeReason', 'Đơn thứ hai');
    await f.click('outcomeOrder-21'); assert.equal(f.workspace.querySelector('[data-delivered-line="31"]').value, '2.5');
    await f.click('outcomeDiscard'); assert.equal(f.workspace.querySelector('[data-delivered-line="31"]').value, '');
    await f.click('outcomeOrder-22'); assert.equal(f.workspace.querySelector('[data-delivered-line="32"]').value, '1');
    assert.equal(f.get('outcomeReason').value, 'Đơn thứ hai'); assert.equal(f.state.posts.length, 0);
});

test('Cash discrepancy and stale order versions block confirmation while preserving inputs', async () => {
    const f = await fixture(); await f.click('outcomeFull'); await f.check('outcomePhysicalConfirmed');
    await f.input('outcomeActualCash', '1'); assert.equal(f.get('outcomeConfirm').disabled, true);
    assert.match(f.get('outcomeCashDifference').textContent, /thiếu/); assert.equal(f.state.posts.length, 0);
    await f.input('outcomeActualCash', '40000'); assert.equal(f.get('outcomeConfirm').disabled, false);
    f.state.model.trip.version = nextVersion; f.state.model.trip.revision++;
    f.state.model.trip.orders[0].version = nextVersion; f.state.model.trip.orders[0].revision++;
    await f.click('outcomeReload'); assert.equal(f.get('outcomeActualCash').value, '40000'); assert.equal(f.get('outcomeConfirm').disabled, true);
    assert.equal(f.state.posts.length, 0);
});

test('Handover requires the actual physical checkbox and an open counter owned by this actor', async () => {
    const f = await fixture(); await f.click('outcomeFull'); assert.equal(f.get('outcomeConfirm').disabled, true);
    await f.check('outcomePhysicalConfirmed'); assert.equal(f.get('outcomeConfirm').disabled, false);
    const model = clone(f.state.model); model.counter.ownerUserId = 99;
    const foreign = await fixture({ model }); assert.equal(foreign.get('outcomeFull').disabled, true);
    assert.equal(foreign.get('outcomeConfirm').disabled, true); assert.equal(foreign.state.posts.length, 0);
    assert.match(foreign.get('outcomeReturn').textContent, /Chưa có ca mở của bạn/);
});

test('Lost handover response preserves exact body across reload and only explicit retry posts it', async () => {
    const f = await fixture(); f.state.networkFail = true; await f.click('outcomeFull'); await f.check('outcomePhysicalConfirmed'); await f.click('outcomeConfirm');
    const sent = assertSingle(f.state.posts); assert.equal(f.get('outcomeConfirm').disabled, true); assert.equal(f.get('outcomePending').hidden, false);
    await f.poll(); assert.equal(f.state.posts.length, 1);
    const reloaded = await fixture({ storage: f.disk }); assert.equal(reloaded.state.posts.length, 0);
    await reloaded.click('outcomeRetry'); assert.equal(assertSingle(reloaded.state.posts).raw, sent.raw);
    assert.equal(reloaded.get('outcomePending').hidden, true); assert.match(reloaded.get('outcomeSettlement').textContent, /Đã chốt/);
});

test('Read-only and post-ACK read failures block writes without losing confirmed settlement', async () => {
    const read = await fixture({ readonly: true }); assert.equal(read.get('outcomeConfirm').disabled, true);
    assert.equal(read.get('outcomeFull').disabled, true); await read.click('outcomeConfirm'); assert.equal(read.state.posts.length, 0);
    const f = await fixture(); f.state.freshFail = true; await f.click('outcomeFull'); await f.check('outcomePhysicalConfirmed'); await f.click('outcomeConfirm');
    assert.equal(f.state.posts.length, 1); assert.equal(f.get('outcomeConfirm').disabled, true);
    assert.match(f.get('outcomeMessage').textContent, /cần tải bản mới/i); assert.equal(f.get('outcomePending').hidden, true);
    assert.equal(f.state.model.orders[0].settlement.receivedCashText, '40000');
    f.state.freshFail = false; await f.click('outcomeReload'); assert.match(f.get('outcomeSettlement').textContent, /Đã chốt/);
    assert.match(f.get('outcomeProgress').textContent, /1\/2/); assert.equal(f.state.posts.length, 1);
});

function assertSingle(rows) { assert.equal(rows.length, 1); return rows[0]; }
