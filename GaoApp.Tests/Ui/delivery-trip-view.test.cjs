const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const scriptPath = path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/delivery.trip.js');
const source = fs.readFileSync(scriptPath, 'utf8');
const clone = value => JSON.parse(JSON.stringify(value));
const version = 'AAAAAAAAAAc=';
const tick = () => new Promise(resolve => setImmediate(resolve));

class Element {
    constructor(tag, document) {
        this.tagName = tag.toUpperCase(); this.document = document; this.children = []; this.parentNode = null;
        this.attributes = new Map(); this.listeners = new Map(); this.dataset = {}; this.style = {};
        this.className = ''; this.id = ''; this.value = ''; this.disabled = false; this.hidden = false; this.open = false; this.checked = false; this._text = '';
        this.classList = { contains: name => this.className.split(/\s+/).includes(name),
            add: (...names) => { this.className = [...new Set([...this.className.split(/\s+/).filter(Boolean), ...names])].join(' '); },
            remove: (...names) => { this.className = this.className.split(/\s+/).filter(name => !names.includes(name)).join(' '); },
            toggle: (name, force) => { const enabled = force ?? !this.classList.contains(name); if (enabled) this.classList.add(name); else this.classList.remove(name); return enabled; } };
    }
    get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
    set textContent(value) { this.replaceChildren(); this._text = String(value ?? ''); }
    append(...children) { for (const child of children) { if (child.parentNode) child.remove(); child.parentNode = this; this.children.push(child); } }
    prepend(...children) { for (const child of children.reverse()) { if (child.parentNode) child.remove(); child.parentNode = this; this.children.unshift(child); } }
    replaceChildren(...children) { for (const child of this.children) child.parentNode = null; this.children = []; this._text = ''; this.append(...children); }
    remove() { if (this.parentNode) { this.parentNode.children = this.parentNode.children.filter(child => child !== this); this.parentNode = null; } }
    contains(child) { return this === child || this.children.some(item => item.contains(child)); }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    removeAttribute(name) { this.attributes.delete(name); }
    addEventListener(name, listener) { const rows = this.listeners.get(name) || []; rows.push(listener); this.listeners.set(name, rows); }
    async emit(name, target = this) {
        for (const listener of this.listeners.get(name) || []) await listener({ target, currentTarget: this, preventDefault() {}, stopPropagation() {} });
        if (['input', 'change'].includes(name) && this.parentNode) await this.parentNode.emit(name, target);
    }
    async click() { if (!this.disabled) await this.emit('click'); }
    focus() { this.document.activeElement = this; }
    matches(selector) {
        if (selector.startsWith('#')) return this.id === selector.slice(1);
        if (selector.startsWith('.')) return selector.slice(1).split('.').every(name => this.classList.contains(name));
        const attr = selector.match(/^(\w+)?\[([\w-]+)(?:="([^"]+)")?\]$/);
        if (attr) { const value = attr[2].startsWith('data-') ? this.dataset[attr[2].slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase())] : this.getAttribute(attr[2]);
            return (!attr[1] || this.tagName === attr[1].toUpperCase()) && (attr[3] === undefined ? value != null : value === attr[3]); }
        return this.tagName === selector.toUpperCase();
    }
    querySelectorAll(selector) { return this.children.flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]); }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
    closest(selector) { return this.matches(selector) ? this : this.parentNode?.closest(selector) || null; }
}

function detail(state = 'Draft') {
    const physical = state === 'HandedOver' || state === 'Departed';
    return { storeId: 6, tripId: 11, code: 'GH-20261008-SYNTHETIC', revision: 3, version, state, courierUserId: 8, courierName: 'Người giao An',
        serverTimeUtc: '2026-10-08T09:00:00Z', createdAtUtc: '2026-10-08T08:00:00Z', note: 'Chuyến thử giao diện',
        handedOverAtUtc: physical ? '2026-10-08T08:30:00Z' : null, handedOverByName: physical ? 'Nhân viên Bình' : null,
        departedAtUtc: state === 'Departed' ? '2026-10-08T08:45:00Z' : null, departedByName: state === 'Departed' ? 'Nhân viên Bình' : null,
        capabilities: { canEditDraft: !physical, canAbandonDraft: !physical, canHandover: !physical, canDepart: state === 'HandedOver',
            canReassignCourier: state !== 'Departed', canRequestCancellation: physical },
        orders: [{ deliveryOrderId: 21, code: 'GH-ORDER-021', version, position: 1, recipientName: 'Khách Lan', recipientAddress: '12 Đường thử nghiệm',
            state: physical ? state === 'Departed' ? 'Delivering' : 'HandedOver' : 'ReadyForHandover', isActive: true, isStale: false,
            canRequestCancellation: physical, sourceWarehouseId: 3, sourceLegalEntityId: 4, sourceWarehouseName: 'Kho chính', sourceLegalEntityName: 'Chủ hàng A',
            custody: physical ? { tripId: 11, courierUserId: 8, courierName: 'Người giao An', custodyEventId: 91,
                confirmedByName: 'Nhân viên Bình', confirmedAtUtc: '2026-10-08T08:30:00Z' } : null,
            lines: [{ lineId: 31, itemName: 'Gạo <đặc biệt>', baseUnitName: 'kg', approvedBaseQuantityText: '1.2345',
                dispatchedBaseQuantityText: physical ? '1.2345' : '0', acceptedReturnedBaseQuantityText: physical ? '0.2345' : '0', inCustodyBaseQuantityText: physical ? '1' : null },
            { lineId: 32, itemName: 'Nước', baseUnitName: 'Chai', approvedBaseQuantityText: '2.0001',
                dispatchedBaseQuantityText: physical ? '2.0001' : '0', acceptedReturnedBaseQuantityText: physical ? '0.0001' : '0', inCustodyBaseQuantityText: physical ? '2' : null }] }],
        history: [{ recordedAtUtc: '2026-10-08T08:00:00Z', actorName: 'Nhân viên Bình', action: 'create', revision: 1 }] };
}

async function fixture(options = {}) {
    const document = { activeElement: null }; document.createElement = tag => new Element(tag, document); document.body = document.createElement('body');
    const workspace = document.createElement('div'); workspace.className = 'delivery-trip-workspace'; workspace.dataset = { store: '6', actor: 'actor' }; document.body.append(workspace);
    for (const [id, tag] of [['deliveryTripMessage', 'div'], ['deliveryTripPending', 'div'], ['deliveryTripRefresh', 'button'], ['deliveryTripState', 'select'],
        ['deliveryTripList', 'div'], ['deliveryTripMore', 'button'], ['deliveryTripNew', 'button'], ['deliveryTripDetail', 'main']]) {
        const element = document.createElement(tag); element.id = id; workspace.append(element);
    }
    document.getElementById = id => document.body.querySelector('#' + id);
    document.querySelector = selector => selector === 'meta[name=request-verification-token]' ? { content: 'csrf' } : document.body.querySelector(selector);
    const disk = new Map(), storage = { getItem: key => disk.get(key) ?? null, setItem: (key, value) => disk.set(key, String(value)), removeItem: key => disk.delete(key) };
    const state = { model: detail(options.state), posts: [], reads: [], failFresh: false, monotonic: 0 };
    if (options.completed) {
        state.model.isCompleted = true;
        state.model.orders[0].state = 'Settled'; state.model.orders[0].canRequestCancellation = false;
        state.model.capabilities.canRequestCancellation = false;
        for (const line of state.model.orders[0].lines) line.inCustodyBaseQuantityText = '0';
    }
    if (options.returned || options.completed) state.model.returnToStore = {
        actorName:'Nhân viên Bình', recordedAtUtc:'2026-10-08T08:59:00Z'
    };
    if (options.readonly) for (const key of Object.keys(state.model.capabilities)) state.model.capabilities[key] = false;
    if (options.stale) { state.model.orders[0].isStale = true; state.model.capabilities.canHandover = false; }
    const intervals = [], location = { origin: 'https://synthetic.test', href: 'https://synthetic.test/trips?trip=11' };
    const window = { document, location, localStorage: storage, addEventListener() {} }; let sequence = 0;
    const response = (status, body) => ({ ok: status >= 200 && status < 300, status, text: async () => JSON.stringify(clone(body)) });
    const fetch = async (url, request = {}) => {
        if (request.method === 'POST') {
            const body = JSON.parse(request.body), operation = url.split('/').pop(); state.posts.push({ url, body, headers: request.headers });
            let next = operation === 'handover' ? 'HandedOver' : operation === 'depart' ? 'Departed' : state.model.state;
            const updated = detail(next); updated.revision = state.model.revision + 1; updated.version = 'AAAAAAAAAAg=';
            if (operation === 'cancel-request') { updated.orders[0].state = 'CancellationPending'; updated.orders[0].canRequestCancellation = false; updated.capabilities.canDepart = false; }
            if (operation === 'draft') { updated.courierUserId = body.courierUserId; updated.orders = body.orders.map(row => state.model.orders.find(order => order.deliveryOrderId === row.deliveryOrderId)); }
            state.model = updated;
            return response(200, { clientRequestId: body.clientRequestId, tripId: 11, operation, appliedRevision: updated.revision,
                appliedVersion: updated.version, recordedAtUtc: updated.serverTimeUtc, replayed: false,
                orders: updated.orders.map(row => ({ deliveryOrderId: row.deliveryOrderId, revision: 4, version: row.version, state: row.state })) });
        }
        state.reads.push(url);
        if (url === '/admin/api/delivery-trips/11') return state.failFresh && state.posts.length ? response(503, { message: 'Bản mới chưa tải được' }) : response(200, state.model);
        if (url === '/admin/api/delivery-trips/options') return response(200, { storeId: 6, couriers: [{ userId: 8, name: 'Người giao An' }, { userId: 9, name: 'Người giao Chi' }], orders: [] });
        if (url.startsWith('/admin/api/delivery-trips?')) return response(200, { storeId: 6, items: [{ ...state.model, activeOrderCount: 1 }], nextBeforeId: null });
        throw new Error('Unexpected synthetic route: ' + url);
    };
    const context = { window, document, location, localStorage: storage, fetch, history: { replaceState() {} },
        setInterval: callback => { intervals.push(callback); return intervals.length; }, performance: { now: () => state.monotonic },
        crypto: { randomUUID: () => '12345678-1234-1234-1234-' + (++sequence).toString(16).padStart(12, '0') },
        Option: function (text, value) { const option = document.createElement('option'); option.textContent = text; option.value = value; return option; },
        URL, URLSearchParams, Date, Intl, BigInt, console };
    vm.runInNewContext(source, context, { filename: scriptPath });
    if (options.pending) { const registry = window.DeliveryTripUi.createIntentStore(storage, location.origin, 6, 'actor');
        const body = window.DeliveryTripUi.commandBody('depart', state.model, {}, '12345678-1234-1234-1234-123456789abc', false, false);
        registry.sending(registry.begin(11, 'depart', body)); }
    const settle = async () => { for (let i = 0; i < 8; i++) await tick(); }; await settle();
    const get = id => { const element = document.getElementById(id); assert.ok(element, 'Missing #' + id); return element; };
    assert.equal(get('deliveryTripDetail').dataset.tripId, '11', get('deliveryTripMessage').textContent);
    return { state, document, get, settle, storage, intervals, api: window.DeliveryTripUi,
        click: async id => { await get(id).click(); await settle(); },
        input: async (id, value) => { const input = get(id); input.value = value; input.focus(); await input.emit('input'); await settle(); },
        open: async id => { get(id).open = true; await get(id).emit('toggle'); },
        poll: async () => { state.monotonic += 1000; for (const callback of intervals) await callback(); await settle(); } };
}

test('Draft shows ordered customer facts and exact approved quantities without manufacturing custody', async () => {
    const f = await fixture(); const panel = f.get('deliveryTripDetail'), card = panel.querySelector('[data-order-id="21"]');
    assert.ok(card.textContent.indexOf('Khách Lan') < card.textContent.indexOf('GH-ORDER-021'));
    assert.match(card.textContent, /12 Đường thử nghiệm/); assert.match(card.textContent, /Kho chính/);
    assert.deepEqual(panel.querySelectorAll('[data-step]').map(step => [step.dataset.step, step.dataset.status]),
        [['create', 'done'], ['handover', 'current'], ['depart', 'pending']]);
    const first = card.querySelector('[data-line-id="31"]');
    assert.equal(first.querySelector('[data-quantity="approved"]').textContent, '1,2345');
    assert.equal(first.querySelector('[data-quantity="dispatched"]').textContent, '0');
    assert.equal(first.querySelector('[data-quantity="returned"]').textContent, '0');
    assert.match(first.querySelector('[data-quantity="custody"]').textContent, /chưa xác nhận/i);
    assert.match(first.textContent, /kg/); assert.equal(first.querySelector('th').textContent, 'Gạo <đặc biệt>');
    assert.ok(f.get('deliveryTripHandover')); assert.equal(f.document.getElementById('deliveryTripDepart'), null);
    const selected = f.get('deliveryTripList').querySelector('[data-trip-id="11"]'); assert.equal(selected.getAttribute('aria-current'), 'true');
});

test('HandedOver and Departed render confirmed per-line quantities and only capable next operations', async () => {
    for (const state of ['HandedOver', 'Departed']) {
        const f = await fixture({ state }), panel = f.get('deliveryTripDetail'), first = panel.querySelector('[data-line-id="31"]');
        assert.deepEqual(panel.querySelectorAll('[data-step]').map(step => step.dataset.status),
            state === 'HandedOver' ? ['done', 'done', 'current'] : ['done', 'done', 'done']);
        assert.equal(first.querySelector('[data-quantity="dispatched"]').textContent, '1,2345');
        assert.equal(first.querySelector('[data-quantity="returned"]').textContent, '0,2345');
        assert.equal(first.querySelector('[data-quantity="custody"]').textContent, '1');
        assert.equal(panel.querySelector('[data-line-id="32"]').querySelector('[data-quantity="custody"]').textContent, '2');
        assert.match(panel.textContent, /Người giao An/); assert.match(panel.textContent, /Nhân viên Bình/);
        assert.equal(f.document.getElementById('deliveryTripHandover'), null);
        assert.equal(Boolean(f.document.getElementById('deliveryTripDepart')), state === 'HandedOver');
        assert.equal(Boolean(f.document.getElementById('deliveryTripTransferPanel')), state === 'HandedOver');
        if (state === 'Departed') assert.match(f.get('deliveryTripElapsed').textContent, /0 giờ 15 phút/);
    }
});

test('Closed trips show completion in list and detail, with settled orders and no pending reconciliation text', async () => {
    const f = await fixture({state:'Departed', completed:true}), panel=f.get('deliveryTripDetail');
    assert.match(panel.querySelector('.delivery-trip-title').textContent,/Đã hoàn thành/);
    assert.match(f.get('deliveryTripList').textContent,/Đã hoàn thành/);
    assert.doesNotMatch(panel.textContent,/Hàng và tiền còn chờ đối chiếu/);
    assert.match(panel.textContent,/Đã chốt hàng và tiền của toàn bộ chuyến/);
    assert.match(panel.querySelector('[data-order-id="21"]').textContent,/Đã chốt/);
    assert.equal(f.document.getElementById('deliveryTripElapsed'),null);
    assert.match(f.get('deliveryTripOutcomes').textContent,/Xem bàn giao/);
});

test('Returned and partly settled trips remain unfinished, and polling updates the cached list when closure arrives', async () => {
    const f = await fixture({state:'Departed',returned:true});
    assert.match(f.get('deliveryTripDetail').textContent,/Đã trở về/);
    assert.doesNotMatch(f.get('deliveryTripList').textContent,/Đã hoàn thành/);
    assert.match(f.get('deliveryTripDetail').textContent,/Hàng và tiền còn chờ đối chiếu/);
    const reads = f.state.reads.filter(url=>url.startsWith('/admin/api/delivery-trips?')).length;
    f.state.model.isCompleted=true; f.state.model.orders[0].state='Cancelled';
    f.state.model.orders[0].canRequestCancellation=false; f.state.model.capabilities.canRequestCancellation=false;
    for(const line of f.state.model.orders[0].lines) line.inCustodyBaseQuantityText='0';
    await f.poll();
    assert.match(f.get('deliveryTripList').textContent,/Đã hoàn thành/);
    assert.match(f.get('deliveryTripDetail').textContent,/Đã hoàn thành/);
    assert.doesNotMatch(f.get('deliveryTripDetail').textContent,/Hàng và tiền còn chờ đối chiếu/);
    assert.ok(f.state.reads.filter(url=>url.startsWith('/admin/api/delivery-trips?')).length>reads);
});

test('Recall starts collapsed, stays open across polling, preserves typed reason and sends only genuine recall fields', async () => {
    const f = await fixture({ state: 'Departed' }); assert.equal(f.get('deliveryTripRecallPanel-21').open, false);
    await f.open('deliveryTripRecallPanel-21'); await f.poll(); assert.equal(f.get('deliveryTripRecallPanel-21').open, true);
    await f.input('deliveryTripRecallReason-21', 'Khách yêu cầu thu hồi'); const input = f.get('deliveryTripRecallReason-21');
    await f.poll(); assert.equal(f.get('deliveryTripRecallReason-21'), input); assert.equal(input.value, 'Khách yêu cầu thu hồi');
    await f.click('deliveryTripRecall-21'); assert.equal(f.state.posts.length, 1);
    const request = f.state.posts[0]; assert.equal(request.url, '/admin/api/delivery-trips/11/orders/21/cancel-request');
    assert.deepEqual(Object.keys(request.body).sort(), ['clientRequestId', 'expectedOrderVersion', 'expectedTripVersion', 'reason']);
    assert.equal(request.body.expectedOrderVersion, version); assert.equal(request.body.expectedTripVersion, version);
    assert.equal(request.body.reason, 'Khách yêu cầu thu hồi'); assert.equal(request.headers.RequestVerificationToken, 'csrf');
});

test('Draft editing retains unsaved input during polls and the real save hook keeps exact order versions', async () => {
    const f = await fixture(); await f.click('deliveryTripEdit'); await f.input('deliveryTripDraftReason', 'Làm mới bản đơn');
    const input = f.get('deliveryTripDraftReason'); await f.poll(); assert.equal(f.get('deliveryTripDraftReason'), input);
    assert.equal(input.value, 'Làm mới bản đơn'); assert.equal(f.state.posts.length, 0);
    assert.equal(f.document.getElementById('deliveryTripHandover'), null);
    await f.click('deliveryTripSaveDraft'); assert.equal(f.state.posts.length, 1);
    assert.equal(f.state.posts[0].url, '/admin/api/delivery-trips/11/draft');
    assert.deepEqual(f.state.posts[0].body.orders, [{ deliveryOrderId: 21, expectedVersion: version }]);
    assert.equal(f.state.posts[0].body.reason, 'Làm mới bản đơn'); assert.equal(f.state.posts[0].body.expectedTripVersion, version);
});

test('Handover requires the physical checkbox, then departure uses the unchanged actual command hooks', async () => {
    const f = await fixture(); await f.click('deliveryTripHandover'); assert.equal(f.state.posts.length, 0);
    assert.match(f.get('deliveryTripMessage').textContent, /nhận hàng thực tế/);
    f.get('deliveryTripPhysicalReceipt').checked = true; await f.click('deliveryTripHandover'); assert.equal(f.state.posts.length, 1);
    const body = f.state.posts[0].body; assert.equal(body.confirmedCourierUserId, 8); assert.equal(body.physicalReceiptConfirmed, true);
    assert.equal(body.confirmationNote, null); assert.deepEqual(body.orders, [{ deliveryOrderId: 21, expectedVersion: version }]);
    assert.equal('actorUserId' in body, false); assert.equal('confirmedAtUtc' in body, false);
    await f.click('deliveryTripDepart'); assert.equal(f.state.posts.length, 2);
    assert.equal(f.state.posts[1].url, '/admin/api/delivery-trips/11/depart');
    assert.deepEqual(Object.keys(f.state.posts[1].body).sort(), ['clientRequestId', 'expectedTripVersion', 'orders']);
});

test('Readonly, stale, uncertain pending and acknowledged-but-unfresh models cannot authorize a command', async () => {
    const read = await fixture({ state: 'Departed', readonly: true });
    for (const id of ['deliveryTripEdit', 'deliveryTripHandover', 'deliveryTripDepart', 'deliveryTripRecall-21']) assert.equal(read.document.getElementById(id), null);
    const stale = await fixture({ stale: true }); assert.equal(stale.document.getElementById('deliveryTripHandover'), null);
    assert.match(stale.get('deliveryTripDetail').textContent, /đơn đã thay đổi/i);
    const pending = await fixture({ state: 'HandedOver', pending: true }); assert.equal(pending.state.posts.length, 0);
    assert.equal(pending.get('deliveryTripPending').hidden, false); assert.equal(pending.get('deliveryTripDepart').disabled, true);
    await pending.click('deliveryTripDepart'); assert.equal(pending.state.posts.length, 0);
    const fresh = await fixture(); fresh.state.failFresh = true; fresh.get('deliveryTripPhysicalReceipt').checked = true;
    await fresh.click('deliveryTripHandover'); assert.equal(fresh.state.posts.length, 1);
    assert.equal(fresh.get('deliveryTripDetail').dataset.needsFresh, 'true'); assert.equal(fresh.get('deliveryTripHandover').disabled, true);
    await fresh.click('deliveryTripHandover'); assert.equal(fresh.state.posts.length, 1);
});
