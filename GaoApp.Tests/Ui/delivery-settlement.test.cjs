const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const api = require('../../GaoApp.Web/wwwroot/Admin/js/delivery.outcomes.js');
const script = path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/delivery.outcomes.js');
const tripScript = path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/delivery.trip.js');
const clone = value => JSON.parse(JSON.stringify(value));
const version = 'AAAAAAAAAAc=', nextVersion = 'AAAAAAAAAAg=';
const guid = '12345678-1234-4234-8234-123456789abc';
function workspace() {
    return { counter: { storeId: 6, terminalId: 7, shiftId: 70, warehouseId: 9, ownerUserId: 9, isOpen: true },
        capabilities: { canFinalize: true, canReceiveReturns: true, canReconcileCash: true, canConfirmBankTransfer: true, canCredit: true },
        bankAccounts: [{ bankAccountId: 4, name: 'Tài khoản đã xác nhận' }],
        orders: [21, 22].map(id => ({ deliveryOrderId: id, sourceCustomerId: 12, customerCreditEligible: true,
            canConfirm: true, savedPreview: null, settlement: null, costQuality: null })),
        trip: { storeId: 6, tripId: 11, revision: 3, version, state: 'Departed', courierUserId: 8, courierName: 'Người giao An',
            serverTimeUtc: '2026-10-08T09:00:00Z', departedAtUtc: '2026-10-08T08:45:00Z',
            capabilities: { canEditDraft: false, canAbandonDraft: false, canHandover: false, canDepart: false, canReassignCourier: false, canRequestCancellation: true },
            outcomeCapabilities: { canReportOwn: false, canReportOnBehalf: true, canRecordReturn: true },
            returnToStore: null, allOutcomesComplete: false, readyForReconciliation: false,
            orders: [21, 22].map((id, index) => ({ deliveryOrderId: id, code: 'GH-' + id, version, revision: 5, position: index + 1,
                recipientName: index ? 'Khách Minh' : 'Khách <Lan>', recipientPhone: '0900000000', recipientAddress: 'Địa chỉ tổng hợp',
                state: 'Delivering', isActive: true, canReportOutcome: true, sourceWarehouseId: 3 + index, sourceLegalEntityId: 4 + index,
                sourceWarehouseName: 'Kho ' + (index + 1), sourceLegalEntityName: 'Chủ hàng ' + (index + 1),
                custody: { tripId: 11, courierUserId: 8, courierName: 'Người giao An', custodyEventId: 91 + index }, outcome: null,
                lines: [{ lineId: 31 + index, itemName: index ? 'Nước' : 'Gạo <đặc biệt>', baseUnitName: index ? 'Chai' : 'kg',
                    approvedBaseQuantityText: index ? '3' : '4', dispatchedBaseQuantityText: index ? '3' : '4',
                    acceptedReturnedBaseQuantityText: '0', inCustodyBaseQuantityText: index ? '3' : '4' }] })) } };
}
function fields() { return { outcome: 'Partial', reason: 'Khách nhận một phần', physicalGoodsConfirmed: true,
    reportedCashText: '45000', actualReceivedCashText: '45000', confirmedTransferText: '0', transferConfirmed: false,
    paymentKind: 'Cash', transferReference: null, creditDueDate: null, creditNote: null, customerReceivedAtUtc: null,
    lines: [{ lineId: 31, deliveredBaseQuantityText: '2.5', actualReturnedBaseQuantityText: '1.5' }] }; }
function preview(model, orderId = 21, rows = fields().lines) {
    const order = model.trip.orders.find(row => row.deliveryOrderId === orderId);
    const lines = rows.map(row => {
        const source = order.lines.find(line => line.lineId === row.lineId);
        const gross = api.parseQuantity(row.deliveredBaseQuantityText) * 20000n / 10000n;
        const discount = gross / 10n;
        return { ...row, newReturnBaseQuantityText: api.textQuantity(api.parseQuantity(row.actualReturnedBaseQuantityText) - api.parseQuantity(source.acceptedReturnedBaseQuantityText)),
            grossText: gross.toString(), discountText: discount.toString(), netText: (gross - discount).toString() };
    });
    return { tripId: 11, deliveryOrderId: orderId, tripVersion: model.trip.version, orderVersion: order.version,
        sourceWarehouseId: order.sourceWarehouseId, sourceLegalEntityId: order.sourceLegalEntityId, sourceHash: 'immutable-saved-source',
        payableText: lines.reduce((sum, line) => sum + BigInt(line.netText), 0n).toString(), lines };
}
function ack(model, body, orderId = 21, payable = '45000') {
    const order = model.trip.orders.find(row => row.deliveryOrderId === orderId);
    const state = payable === '0' && order.state === 'CancellationPending' ? 'Cancelled' : 'Settled';
    return { clientRequestId: body.clientRequestId, tripId: 11, deliveryOrderId: orderId, operation: 'handover',
        appliedTripRevision: model.trip.revision + 1, appliedTripVersion: nextVersion,
        appliedOrderRevision: order.revision + 1, appliedOrderVersion: nextVersion, state,
        recordedAtUtc: '2026-10-08T09:01:00Z', replayed: false,
        settlement: { deliveryOrderId: orderId, saleOrderId: payable === '0' ? null : 100 + orderId, state, paymentKind: body.paymentKind,
            payableText: payable, receivedCashText: body.actualReceivedCashText, confirmedTransferText: body.confirmedTransferText,
            customerDebtText: body.paymentKind === 'CustomerCredit' ? payable : '0', terminalId: body.expectedTerminalId,
            shiftId: body.expectedShiftId, sourceWarehouseId: order.sourceWarehouseId, sourceLegalEntityId: order.sourceLegalEntityId,
            recordedAtUtc: '2026-10-08T09:01:00Z' } };
}
function storage() { const rows = new Map(); return { rows, getItem: key => rows.get(key) ?? null,
    setItem: (key, value) => rows.set(key, String(value)), removeItem: key => rows.delete(key) }; }
function savedReport(model, kind) {
    const order = model.trip.orders[0], full = kind === 'Full', delivered = full ? '4' : '2.5', returned = full ? '0' : '1.5';
    order.state = 'AwaitingReconciliation';
    order.outcome = { orderRevision: order.revision, orderVersion: order.version, tripId: 11, deliveryOrderId: 21,
        custodyEventId: order.custody.custodyEventId, actorUserId: 9, actorName: 'Người nhập', courierUserId: 8,
        courierName: model.trip.courierName, reportSource: 'AtStore', outcome: kind, reason: full ? null : 'Khách nhận một phần',
        reportedCashText: full ? '72000' : '45000', reportedTransferText: '0', recordedAtUtc: '2026-10-08T08:55:00Z',
        customerReceivedAtUtc: null, isCurrentValid: true, lines: [{ lineId: 31, deliveredBaseQuantityText: delivered,
            reportedReturnBaseQuantityText: returned, acceptedReturnedAtReportBaseQuantityText: '0', pendingReturnBaseQuantityText: returned }] };
    model.orders[0].savedPreview = preview(model, 21, [{ lineId: 31, deliveredBaseQuantityText: delivered, actualReturnedBaseQuantityText: returned }]);
}
function assertClosedReport(model, kind, delivered, returned) {
    api.validateWorkspace(model, 6, 11); api.validateDetail(model.trip, 6, 11);
    const row = model.trip.orders[0]; assert.equal(row.state, 'Settled'); assert.equal(row.lines[0].inCustodyBaseQuantityText, '0');
    assert.equal(row.lines[0].acceptedReturnedBaseQuantityText, returned); assert.equal(row.outcome.outcome, kind);
    assert.equal(row.outcome.lines[0].deliveredBaseQuantityText, delivered); assert.equal(row.outcome.lines[0].pendingReturnBaseQuantityText, '0');
    assert.equal(row.outcome.lines[0].acceptedReturnedAtReportBaseQuantityText, '0'); assert.equal(row.outcome.isCurrentValid, true);
    const nonzero = clone(model); nonzero.trip.orders[0].lines[0].inCustodyBaseQuantityText = '1';
    assert.throws(() => api.validateWorkspace(nonzero, 6, 11));
    const noProof = clone(model); noProof.trip.orders[0].custody = null; assert.throws(() => api.validateWorkspace(noProof, 6, 11));
    assert.throws(() => api.reportBody(model.trip, 21, { ...fields(), reportSource: 'AtStore' }, guid));
}

test('One canonical counter command combines actual goods and money without report or arrival prerequisites', () => {
    const model = workspace(); api.validateWorkspace(model, 6, 11);
    const p = preview(model), body = api.handoverBody(model, 21, fields(), guid, p, 9);
    assert.equal(model.trip.returnToStore, null); assert.equal(model.trip.orders[0].outcome, null);
    assert.equal(body.expectedTerminalId, 7); assert.equal(body.expectedShiftId, 70); assert.equal(body.courierUserId, 8);
    assert.equal(body.actualReceivedCashText, '45000'); assert.deepEqual(body.lines, fields().lines);
    assert.equal(body.physicalGoodsConfirmed, true); assert.equal(body.expectedOrderVersion, version);
    for (const field of ['prices', 'sourceWarehouseId', 'actorUserId', 'creditCustomerId', 'saleOrderId']) assert.equal(field in body, false);
    model.trip.orders[0].lines[0].acceptedReturnedBaseQuantityText = '.5';
    assert.throws(() => api.validatePreview(p, model, 21)); // Noncanonical source is rejected too.
    model.trip.orders[0].lines[0].acceptedReturnedBaseQuantityText = '0.5';
    model.trip.orders[0].lines[0].inCustodyBaseQuantityText = '3.5';
    assert.equal(preview(model).lines[0].newReturnBaseQuantityText, '1');
    const invalidCustody = workspace(); invalidCustody.trip.orders[0].lines[0].inCustodyBaseQuantityText = '3';
    assert.throws(() => api.validateWorkspace(invalidCustody, 6, 11));
    assert.throws(() => api.handoverBody(invalidCustody, 21, { ...fields(), outcome: 'Full', reason: null,
        reportedCashText: '72000', actualReceivedCashText: '72000',
        lines: [{ lineId: 31, deliveredBaseQuantityText: '4', actualReturnedBaseQuantityText: '0' }] }, guid,
        preview(invalidCustody, 21, [{ lineId: 31, deliveredBaseQuantityText: '4', actualReturnedBaseQuantityText: '0' }]), 9));
});

test('Preview binds the exact source/version/line net sum; zero delivery has no fabricated sale receipt', () => {
    const model = workspace(), p = preview(model);
    for (const changed of [{ ...p, payableText: '45001' }, { ...p, sourceWarehouseId: 9 }, { ...p, orderVersion: nextVersion },
        { ...p, lines: [{ ...p.lines[0], netText: '45001' }] }, { ...p, lines: [{ ...p.lines[0], newReturnBaseQuantityText: '0' }] }])
        assert.throws(() => api.validatePreview(changed, model, 21, fields().lines));
    const empty = { ...fields(), outcome: 'NotDelivered', reportedCashText: '0', actualReceivedCashText: '0',
        lines: [{ lineId: 31, deliveredBaseQuantityText: '0', actualReturnedBaseQuantityText: '4' }] };
    const zero = preview(model, 21, empty.lines), body = api.handoverBody(model, 21, empty, guid, zero, 9);
    const disk = storage(), intent = api.createHandoverIntentStore(disk, 'https://synthetic.test', 6, 9, 11);
    intent.begin(model, 21, body, zero); intent.sending(intent.read()); const result = ack(model, body, 21, '0');
    assert.equal(intent.acknowledge(result).settlement.saleOrderId, null); assert.equal(intent.read(), null);
});

test('Physical receipt, actor-owned open counter and payment-specific rights refuse discrepancy or unsupported credit', () => {
    const model = workspace(), p = preview(model);
    for (const changed of [{ ...fields(), physicalGoodsConfirmed: false }, { ...fields(), actualReceivedCashText: '44000' },
        { ...fields(), reportedCashText: '1.5' }, { ...fields(), paymentKind: 'Mixed' }])
        assert.throws(() => api.handoverBody(model, 21, changed, guid, p, 9));
    for (const mutate of [m => m.counter.isOpen = false, m => m.counter.ownerUserId = 77, m => m.capabilities.canReceiveReturns = false,
        m => m.capabilities.canReconcileCash = false, m => m.orders[0].canConfirm = false]) {
        const altered = clone(model); mutate(altered); assert.throws(() => api.handoverBody(altered, 21, fields(), guid, p, 9));
    }
    const bank = { ...fields(), paymentKind: 'BankTransfer', reportedCashText: '0', actualReceivedCashText: '0',
        confirmedTransferText: '45000', transferConfirmed: true };
    const bankBody = api.handoverBody(model, 21, bank, guid, p, 9);
    assert.equal(bankBody.transferReference, null); assert.equal(bankBody.bankAccountId, 4);
    assert.throws(() => api.handoverBody(model, 21, { ...bank, bankAccountId: 99 }, guid, p, 9));
    const noAccount = clone(model); noAccount.bankAccounts = [];
    assert.throws(() => api.handoverBody(noAccount, 21, bank, guid, p, 9));
    assert.throws(() => api.handoverBody(model, 21, { ...bank, transferConfirmed: false }, guid, p, 9));
    const credit = { ...fields(), paymentKind: 'CustomerCredit', reportedCashText: '0', actualReceivedCashText: '0' };
    assert.equal(api.handoverBody(model, 21, credit, guid, p, 9).paymentKind, 'CustomerCredit');
    model.orders[0].customerCreditEligible = false; assert.throws(() => api.handoverBody(model, 21, credit, guid, p, 9));
});

test('Durable intent keeps exact uncertain retry and refuses an ACK for altered source, till, money or versions', () => {
    const model = workspace(), p = preview(model), body = api.handoverBody(model, 21, fields(), guid, p, 9), disk = storage();
    const intent = api.createHandoverIntentStore(disk, 'https://synthetic.test', 6, 9, 11), first = intent.begin(model, 21, body, p);
    const attempt = intent.sending(first), retry = api.createHandoverIntentStore(disk, 'https://synthetic.test', 6, 9, 11);
    assert.equal(retry.read().body, first.body); assert.equal(retry.read().uncertain, true);
    assert.throws(() => retry.begin(model, 21, { ...body, actualReceivedCashText: '1' }, p));
    assert.equal(retry.rejectFirstAttempt(retry.sending(retry.read())), false);
    const result = ack(model, body);
    for (const altered of [{ ...result, operation: 'outcome' }, { ...result, appliedOrderVersion: version },
        { ...result, settlement: { ...result.settlement, sourceWarehouseId: 99 } },
        { ...result, settlement: { ...result.settlement, terminalId: 99 } },
        { ...result, settlement: { ...result.settlement, receivedCashText: '44999' } }]) {
        assert.throws(() => retry.acknowledge(altered)); assert.equal(retry.read().body, first.body);
    }
    assert.equal(retry.acknowledge({ ...result, replayed: true }).settlement.saleOrderId, 121); assert.equal(intent.read(), null);
    assert.equal(attempt.body, first.body);
});

// Small DOM for the actual renderer/handlers, with synthetic GET/preview/ACK only.
class Element {
    constructor(tag, document) { this.tagName = tag.toUpperCase(); this.document = document; this.children = []; this.parentNode = null;
        this.attributes = new Map(); this.listeners = new Map(); this.dataset = {}; this.style = {}; this.className = ''; this.id = '';
        this.value = ''; this.disabled = false; this.hidden = false; this.checked = false; this._text = ''; }
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
async function renderFixture(reportKind) {
    const document = { activeElement: null }; document.createElement = tag => new Element(tag, document); document.body = document.createElement('body');
    const root = document.createElement('div'); root.className = 'delivery-outcomes-workspace'; root.dataset = { store: '6', actor: '9', trip: '11' }; document.body.append(root);
    for (const id of ['outcomeMessage', 'outcomePending', 'outcomeFacts', 'outcomeProgress', 'outcomeList', 'outcomeDetail', 'outcomeReturn', 'outcomeReload']) {
        const element = document.createElement(id === 'outcomeReload' ? 'button' : 'div'); element.id = id; root.append(element);
    }
    document.getElementById = id => document.body.querySelector('#' + id);
    document.querySelector = selector => selector === 'meta[name=request-verification-token]' ? { content: 'csrf' } : document.body.querySelector(selector);
    const disk = storage(), timers = new Map(), intervals = [], state = { model: workspace(), commands: [], previews: [], loseReply: false };
    if (reportKind) savedReport(state.model, reportKind);
    let sequence = 0, timerId = 0;
    const response = (status, body) => ({ ok: status >= 200 && status < 300, status, text: async () => JSON.stringify(clone(body)) });
    const fetch = async (url, request = {}) => {
        if (request.method !== 'POST') { assert.equal(url, '/admin/api/delivery-trips/11/handover'); return response(200, state.model); }
        const body = JSON.parse(request.body), parts = url.split('/'), id = Number(parts.at(-1) === 'preview' ? parts.at(-2) : parts.at(-1));
        if (url.endsWith('/preview')) { state.previews.push({ url, body }); return response(200, preview(state.model, id, body.lines)); }
        assert.equal(url, '/admin/api/delivery-trips/11/handover/' + id); state.commands.push({ url, body, raw: request.body, headers: request.headers });
        if (state.loseReply) throw new Error('Synthetic response lost');
        const p = preview(state.model, id, body.lines), result = ack(state.model, body, id, p.payableText);
        state.model.trip.version = result.appliedTripVersion; state.model.trip.revision = result.appliedTripRevision;
        const order = state.model.trip.orders.find(row => row.deliveryOrderId === id), info = state.model.orders.find(row => row.deliveryOrderId === id);
        Object.assign(order, { version: result.appliedOrderVersion, revision: result.appliedOrderRevision, state: result.state });
        info.settlement = result.settlement; info.canConfirm = false; info.savedPreview = null; order.canReportOutcome = false;
        for (const line of body.lines) { const row = order.lines.find(x => x.lineId === line.lineId); row.acceptedReturnedBaseQuantityText = line.actualReturnedBaseQuantityText;
            row.inCustodyBaseQuantityText = '0'; }
        // Core preserves a genuine prior D07 report; closing custody is zero,
        // while its immutable delivered facts remain report history.
        if (order.outcome) for (const line of order.outcome.lines) {
            const current = order.lines.find(row => row.lineId === line.lineId);
            line.pendingReturnBaseQuantityText = api.textQuantity(api.parseQuantity(line.reportedReturnBaseQuantityText) - api.parseQuantity(current.acceptedReturnedBaseQuantityText));
        }
        return response(200, result);
    };
    const location = { origin: 'https://synthetic.test' }, window = { document, location, localStorage: disk, addEventListener() {} };
    const context = { window, document, location, localStorage: disk, fetch, setInterval: callback => intervals.push(callback),
        setTimeout: callback => { const id = ++timerId; timers.set(id, callback); return id; }, clearTimeout: id => timers.delete(id),
        crypto: { randomUUID: () => '12345678-1234-4234-8234-' + (++sequence).toString().padStart(12, '0') },
        Option: function (text, value) { const element = document.createElement('option'); element.textContent = text; element.value = value; return element; },
        URL, URLSearchParams, Date, Intl, BigInt, console };
    vm.runInNewContext(fs.readFileSync(tripScript, 'utf8'), context, { filename: tripScript });
    vm.runInNewContext(fs.readFileSync(script, 'utf8'), context, { filename: script });
    async function settle() { for (let i = 0; i < 12; i++) { await new Promise(resolve => setImmediate(resolve)); const work = [...timers.values()]; timers.clear(); for (const fn of work) await fn(); } }
    await settle();
    const get = id => { const element = document.getElementById(id); assert.ok(element, '#' + id + ': ' + document.getElementById('outcomeMessage').textContent); return element; };
    return { state, root, get, disk, settle,
        click: async id => { await get(id).click(); await settle(); },
        input: async (id, value) => { const element = get(id); element.value = value; await element.emit('input'); await settle(); },
        check: async id => { const element = get(id); element.checked = true; await element.emit('change'); await settle(); },
        qty: async (line, value) => { const element = root.querySelector('[data-delivered-line="' + line + '"]'); assert.ok(element); element.value = value; await element.emit('input'); await settle(); },
        returned: async (line, value) => { const element = root.querySelector('[data-returned-line="' + line + '"]'); assert.ok(element); element.value = value; await element.emit('input'); await settle(); },
        poll: async () => { for (const fn of intervals) await fn(); await settle(); } };
}

test('Actual form confirms once; an uncertain response blocks edits and only retries the identical full command', async () => {
    const f = await renderFixture('Full'); assert.equal(f.get('outcomeConfirm').disabled, true);
    assert.equal(f.root.querySelector('#outcomeSave'), null); assert.equal(f.root.querySelector('#outcomeRecordReturn'), null);
    await f.click('outcomeFull'); await f.input('outcomeCash', '72000'); await f.input('outcomeActualCash', '72000'); await f.check('outcomePhysicalConfirmed');
    assert.equal(f.get('outcomeConfirm').disabled, false); f.state.loseReply = true; await f.click('outcomeConfirm');
    assert.equal(f.state.commands.length, 1); const original = f.state.commands[0]; assert.equal(original.headers.RequestVerificationToken, 'csrf');
    assert.equal(f.get('outcomeConfirm').disabled, true); assert.match(f.get('outcomePending').textContent, /Chưa|chưa|yêu cầu/);
    await f.click('outcomeReload'); assert.equal(f.state.commands.length, 1);
    f.state.loseReply = false; await f.click('outcomeRetry'); assert.equal(f.state.commands.length, 2);
    assert.equal(f.state.commands[1].raw, original.raw); assert.equal(f.root.querySelector('#outcomeConfirm'), null);
    assert.match(f.get('outcomeSettlement').textContent, /Đã chốt bàn giao/);
    assertClosedReport(f.state.model, 'Full', '4', '0');
    assert.equal(f.state.model.trip.orders[0].outcome.recordedAtUtc, '2026-10-08T08:55:00Z');
    await f.poll(); assert.match(f.get('outcomeSettlement').textContent, /Đã chốt bàn giao/);
});

test('Two order drafts survive switching/poll and one settlement; changed own version remains blocked after reload', async () => {
    const f = await renderFixture('Partial');
    await f.qty(31, '2.5'); await f.returned(31, '1.5'); await f.input('outcomeKind', 'Partial'); await f.input('outcomeReason', 'Đơn một');
    await f.input('outcomeCash', '45000'); await f.input('outcomeActualCash', '45000'); await f.check('outcomePhysicalConfirmed');
    await f.click('outcomeOrder-22'); await f.qty(32, '1'); await f.returned(32, '2'); await f.input('outcomeKind', 'Partial'); await f.input('outcomeReason', 'Đơn hai');
    await f.input('outcomeCash', '18000'); await f.input('outcomeActualCash', '18000'); await f.check('outcomePhysicalConfirmed');
    await f.poll(); await f.click('outcomeOrder-21'); await f.click('outcomeConfirm'); assert.equal(f.state.commands.length, 1);
    assertClosedReport(f.state.model, 'Partial', '2.5', '1.5');
    assert.match(f.get('outcomeSettlement').textContent, /Đã chốt bàn giao/);
    await f.click('outcomeOrder-22'); assert.equal(f.root.querySelector('[data-delivered-line="32"]').value, '1');
    assert.equal(f.get('outcomeReason').value, 'Đơn hai'); assert.equal(f.get('outcomeCash').value, '18000');
    assert.equal(f.get('outcomePhysicalConfirmed').checked, false); assert.equal(f.get('outcomeConfirm').disabled, true);
    // A fresh preview after the other order changed the trip retains the draft
    // but requires the clerk to confirm the physical handover again.
    await f.check('outcomePhysicalConfirmed'); assert.equal(f.get('outcomeConfirm').disabled, false);
    f.state.model.trip.orders[1].version = 'AAAAAAAAAAk='; f.state.model.trip.orders[1].revision++;
    f.state.model.trip.version = 'AAAAAAAAAAk='; f.state.model.trip.revision++;
    await f.click('outcomeReload'); assert.equal(f.get('outcomeReason').value, 'Đơn hai'); assert.equal(f.get('outcomeConfirm').disabled, true);
    assert.equal(f.state.commands.length, 1);
});

test('Actual renderer uses server code and recipientAddress as escaped identifying text', async () => {
    const f = await renderFixture(), order = f.state.model.trip.orders[0];
    assert.ok(f.get('outcomeOrder-21').textContent.includes(order.code));
    assert.ok(f.get('outcomeDetail').querySelector('header').textContent.includes(order.code));
    assert.ok(f.get('outcomeDetail').textContent.includes(order.recipientAddress));
    assert.equal('deliveryCode' in order, false); assert.equal('address' in order, false);
    order.code = 'GH-<svg onload="alert(1)">&21';
    order.recipientAddress = '12 <img src=x onerror="alert(2)"> & "Tổ 1"';
    await f.poll();
    assert.ok(f.get('outcomeOrder-21').textContent.includes(order.code));
    const header = f.get('outcomeDetail').querySelector('header');
    assert.ok(header.textContent.includes(order.code)); assert.ok(header.textContent.includes(order.recipientAddress));
    for (const tag of ['svg', 'img', 'script']) assert.equal(f.root.querySelector(tag), null);
    await f.click('outcomeOrder-22');
    assert.ok(f.get('outcomeDetail').querySelector('header').textContent.includes('GH-22'));
    assert.equal(f.get('outcomeDetail').textContent.includes(order.recipientAddress), false);
    await f.click('outcomeOrder-21');
    assert.ok(f.get('outcomeDetail').textContent.includes(order.code));
    assert.ok(f.get('outcomeDetail').textContent.includes(order.recipientAddress));
    assert.equal(f.state.commands.length, 0);
});
