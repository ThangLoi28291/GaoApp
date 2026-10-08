const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const scriptPath = process.env.GAO_DELIVERY_PICKING_SCRIPT ||
    path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/delivery.page.js');
const source = fs.readFileSync(scriptPath, 'utf8');
const clone = value => JSON.parse(JSON.stringify(value));
const tick = () => new Promise(resolve => setImmediate(resolve));

class Element {
    constructor(tag, document) {
        this.tagName = tag.toUpperCase(); this.document = document; this.children = []; this.parentNode = null;
        this.attributes = new Map(); this.listeners = new Map(); this.dataset = {}; this.style = {};
        this.className = ''; this.id = ''; this.value = ''; this.disabled = false; this.hidden = false; this.open = false; this._text = '';
        this.classList = {
            contains: name => this.className.split(/\s+/).includes(name),
            add: (...names) => { this.className = [...new Set([...this.className.split(/\s+/).filter(Boolean), ...names])].join(' '); },
            remove: (...names) => { this.className = this.className.split(/\s+/).filter(name => !names.includes(name)).join(' '); },
            toggle: (name, force) => { const enabled = force === undefined ? !this.classList.contains(name) : force;
                if (enabled) this.classList.add(name); else this.classList.remove(name); return enabled; }
        };
    }
    get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
    set textContent(value) { this.replaceChildren(); this._text = String(value ?? ''); }
    append(...children) { for (const child of children) { if (child.parentNode) child.remove(); child.parentNode = this; this.children.push(child); } }
    replaceChildren(...children) { for (const child of this.children) child.parentNode = null; this.children = []; this._text = ''; this.append(...children); }
    remove() { if (this.parentNode) { this.parentNode.children = this.parentNode.children.filter(child => child !== this); this.parentNode = null; } }
    contains(child) { return this === child || this.children.some(item => item.contains(child)); }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    removeAttribute(name) { this.attributes.delete(name); }
    addEventListener(name, listener) { const listeners = this.listeners.get(name) || []; listeners.push(listener); this.listeners.set(name, listeners); }
    async emit(name) { for (const listener of this.listeners.get(name) || []) await listener({ target: this, currentTarget: this, preventDefault() {}, stopPropagation() {} }); }
    async click() { if (!this.disabled) await this.emit('click'); }
    focus() { this.document.activeElement = this; }
    matches(selector) {
        if (selector.startsWith('#')) return this.id === selector.slice(1);
        if (selector.startsWith('.')) return selector.slice(1).split('.').every(name => this.classList.contains(name));
        const attr = selector.match(/^\[data-([\w-]+)(?:="([^"]+)")?\]$/);
        if (attr) { const key = attr[1].replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
            return attr[2] === undefined ? this.dataset[key] !== undefined : this.dataset[key] === attr[2]; }
        return this.tagName === selector.toUpperCase();
    }
    querySelectorAll(selector) { return this.children.flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]); }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
    closest(selector) { return this.matches(selector) ? this : this.parentNode?.closest(selector) || null; }
}

function model() {
    return {
        delivery: { id: 31, code: 'GH-031', lookupToken: 'persisted-key', state: 'Picking', revision: 4, version: 'AAAAAAAAAAY=',
            recipientName: 'Khách Lan', recipientPhone: '0900000001', recipientAddress: '12 Đường thử nghiệm', createdAtUtc: '2026-10-08T08:00:00Z',
            createdTerminalId: 3, sourceCartId: 51, sourceWarehouseId: 7, sourceLegalEntityId: 8 },
        capabilities: { canReport: true, canSubmit: true, canClaim: false, canPlan: false, canApprove: false, canReopen: false, canReassign: false },
        pickerName: 'Nhân viên soạn', pickerUserId: 4, assignedAtUtc: '2026-10-08T08:01:00Z', submittedAtUtc: null,
        quotedTotalText: '32345', draftTotalText: '22345', approvedTotalText: null,
        serverTimeUtc: '2026-10-08T08:02:00Z', rootCoverage: [], pickerOptions: [],
        lines: [
            { lineId: 17, itemName: 'Gạo', unitName: 'kg', orderedQuantityText: '1.2345', plannedQuantityText: '1.2345',
                unitPriceText: '10000', reportedQuantityText: null, shortageReason: null, reportedAtUtc: null,
                approvedQuantityText: null, approvedNetText: null, isActive: true, isReplacement: false },
            { lineId: 18, itemName: 'Nước', unitName: 'Chai', orderedQuantityText: '2', plannedQuantityText: '2',
                unitPriceText: '10000', reportedQuantityText: '1', shortageReason: 'Còn một chai', reportedAtUtc: '2026-10-08T08:01:30Z',
                reporterUserId: 4, reporterName: 'Nhân viên soạn', approvedQuantityText: null, approvedNetText: null, isActive: true, isReplacement: false }
        ],
        inventory: { storeId: 6, deliveryOrderId: 31, revision: 4, version: 'AAAAAAAAAAY=', sourceWarehouseId: 7, sourceLegalEntityId: 8,
            sourceWarehouseName: 'Kho chính', sourceLegalEntityName: 'Chủ hàng', serverTimeUtc: '2026-10-08T08:02:00Z',
            canViewCosts: false, canCancelBeforeHandover: false, costFragments: [],
            lines: [{ lineId: 17, itemName: 'Gạo', baseUnitName: 'kg', heldBaseQuantityText: '0', dispatchedBaseQuantityText: '0',
                acceptedReturnedBaseQuantityText: '0', stockOutsideBaseQuantityText: '0', inCustodyBaseQuantityText: null,
                dispatchedCostText: null, acceptedReturnedCostText: null }] }
    };
}

async function fixture(options = {}) {
    const document = { visibilityState: 'visible', activeElement: null };
    document.createElement = tag => new Element(tag, document);
    document.body = document.createElement('body');
    document.getElementById = id => document.body.querySelector('#' + id);
    const workspace = document.createElement('div'); workspace.className = 'delivery-workspace'; workspace.dataset = { store: '6', actor: 'actor' };
    document.body.append(workspace);
    for (const [id, tag] of [['deliverySearch', 'form'], ['deliveryKey', 'input'], ['deliveryPageMessage', 'div'],
        ['deliveryRecent', 'div'], ['deliveryRefreshList', 'button'], ['deliveryDetail', 'section']]) {
        const element = document.createElement(tag); element.id = id; workspace.append(element);
    }
    document.getElementById('deliverySearch').append(document.createElement('button'));
    document.querySelector = selector => selector === 'meta[name="request-verification-token"]' ? { content: 'csrf' } : document.body.querySelector(selector);
    const data = new Map();
    const storage = { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, String(value)), removeItem: key => data.delete(key) };
    const state = { model: model(), posts: [], reads: [], failFresh: false }; Object.assign(state.model.capabilities, options.capabilities);
    const intervals = [];
    const location = { origin: 'https://shop.test', search: '?key=GH-031' };
    const window = { document, location, localStorage: storage, sessionStorage: storage,
        addEventListener() {}, setInterval: callback => { intervals.push(callback); return intervals.length; } };
    let identity = 0;
    const response = (status, body) => ({ ok: status >= 200 && status < 300, status, json: async () => clone(body) });
    const fetch = async (url, request = {}) => {
        if (request.method === 'POST') {
            const body = JSON.parse(request.body); state.posts.push({ url, request: { ...request }, body });
            if (url.endsWith('/report')) {
                for (const report of body.lines) { const line = state.model.lines.find(item => item.lineId === report.lineId);
                    line.reportedQuantityText = report.pickedQuantityText; line.shortageReason = report.shortageReason;
                    line.reportedAtUtc = '2026-10-08T08:03:00Z'; }
            }
            state.model.delivery.revision++; state.model.delivery.version = 'AAAAAAAAAAc=';
            state.model.inventory.revision = state.model.delivery.revision; state.model.inventory.version = state.model.delivery.version;
            return response(200, { clientRequestId: body.clientRequestId, deliveryOrderId: 31, operation: url.split('/').pop(),
                appliedRevision: state.model.delivery.revision, appliedVersion: state.model.delivery.version, replayed: false });
        }
        state.reads.push(url);
        if (url.endsWith('/picking')) return state.failFresh && state.posts.length ? response(503, { message: 'Mất kết nối cập nhật' }) : response(200, state.model);
        if (url.includes('/lookup?')) return response(200, { id: state.model.delivery.id, code: state.model.delivery.code });
        if (url.endsWith('/history')) return response(200, []);
        if (url === '/admin/api/deliveries') return response(200, [{ id: 31, code: 'GH-031', recipientName: 'Khách Lan', state: 'Picking' }]);
        throw new Error('Unexpected route: ' + url);
    };
    const context = { window, document, location, localStorage: storage, sessionStorage: storage, fetch,
        history: { replaceState() {} }, crypto: { randomUUID: () => '12345678-1234-1234-1234-' + (++identity).toString(16).padStart(12, '0') },
        URL, URLSearchParams, Date, Intl, BigInt, console };
    vm.runInNewContext(source, context, { filename: scriptPath });
    if (options.pending) {
        const registry = window.DeliveryPickingUi.createIntentStore(storage, location.origin + ':actor');
        registry.begin(31, 'report', { clientRequestId: '12345678-1234-1234-1234-123456789abc', expectedVersion: 'AAAAAAAAAAY=',
            lines: [{ lineId: 17, pickedQuantityText: '0', shortageReason: 'Hết hàng' }] });
    }
    const settle = async () => { for (let i = 0; i < 6; i++) await tick(); };
    await settle();
    const get = id => { const element = document.getElementById(id); assert.ok(element, 'Missing element #' + id); return element; };
    assert.equal(get('deliveryDetail').dataset.version, 'AAAAAAAAAAY=', get('deliveryPageMessage').textContent);
    return { state, document, workspace, get, settle, storage, intervals,
        input: async (id, value) => { const input = get(id); input.value = value; input.focus(); await input.emit('input'); },
        click: async id => { await get(id).click(); await settle(); },
        poll: async () => { for (const interval of intervals) await interval(); await settle(); }
    };
}

test('recent cards lead with recipient, saved progress stays persisted and untouched lines remain unreported', async () => {
    const f = await fixture();
    const recent = f.get('deliveryRecent'); assert.ok(recent.textContent.indexOf('Khách Lan') < recent.textContent.indexOf('GH-031'));
    assert.equal(f.get('deliveryQty-17').value, ''); assert.equal(f.get('deliveryQty-18').value, '1');
    assert.match(f.get('deliverySavedProgress').textContent, /1\s*\/\s*2/);
    assert.equal(f.get('deliverySubmitPicking').disabled, true);
    await f.click('deliverySaveReport');
    assert.equal(f.state.posts.length, 1); assert.deepEqual(f.state.posts[0].body.lines,
        [{ lineId: 18, pickedQuantityText: '1', shortageReason: 'Còn một chai' }]);
    assert.equal(f.get('deliveryDraftProgress').classList.contains('is-dirty'), false);
    assert.equal(f.get('deliveryQty-17').value, '');
    assert.match(f.get('deliverySavedProgress').textContent, /1\s*\/\s*2/);
    assert.equal(f.get('deliverySubmitPicking').disabled, true);
    await f.click('deliverySubmitPicking'); assert.equal(f.state.posts.length, 1);
    await f.click('deliveryFull-17');
    assert.equal(f.get('deliveryQty-17').value, '1.2345'); assert.equal(f.get('deliveryQty-18').value, '1');
    assert.match(f.get('deliverySavedProgress').textContent, /1\s*\/\s*2/);
    assert.match(f.get('deliveryDraftProgress').textContent, /chưa lưu/i);
    assert.equal(f.get('deliverySubmitPicking').disabled, true); assert.equal(f.state.posts.length, 1);
});

test('quantity controls affect only the chosen line, reveal required reason and never silently save', async () => {
    const f = await fixture();
    assert.equal(f.get('deliveryReason-17').closest('label').hidden, true);
    await f.click('deliveryShort-17');
    assert.equal(f.get('deliveryQty-17').value, ''); assert.equal(f.document.activeElement, f.get('deliveryQty-17'));
    assert.equal(f.get('deliveryReason-17').closest('label').hidden, false);
    await f.click('deliveryMissing-17');
    assert.equal(f.get('deliveryQty-17').value, '0'); assert.equal(f.document.activeElement, f.get('deliveryReason-17'));
    await f.click('deliverySaveReport'); assert.match(f.get('deliveryPageMessage').textContent, /lý do thiếu/i);
    assert.equal(f.state.posts.length, 0);
    await f.click('deliveryFull-17'); assert.equal(f.get('deliveryQty-17').value, '1.2345');
    assert.equal(f.get('deliveryReason-17').closest('label').hidden, true); assert.equal(f.get('deliveryQty-18').value, '1');
    assert.equal(f.state.posts.length, 0);
});

test('dirty submit is refused and explicit save sends exact qty4, trimmed reasons and current version', async () => {
    const f = await fixture();
    await f.input('deliveryQty-17', '0.2345'); await f.input('deliveryReason-17', '  Thiếu một kg  ');
    assert.equal(f.get('deliverySubmitPicking').disabled, true);
    await f.click('deliverySubmitPicking'); assert.equal(f.state.posts.length, 0);
    await f.click('deliverySaveReport'); assert.equal(f.state.posts.length, 1);
    const post = f.state.posts[0]; assert.equal(post.url, '/admin/api/deliveries/31/picking/report');
    assert.equal(post.request.headers.RequestVerificationToken, 'csrf');
    assert.deepEqual(post.body, { clientRequestId: '12345678-1234-1234-1234-000000000001', expectedVersion: 'AAAAAAAAAAY=',
        lines: [{ lineId: 17, pickedQuantityText: '0.2345', shortageReason: 'Thiếu một kg' },
            { lineId: 18, pickedQuantityText: '1', shortageReason: 'Còn một chai' }] });
    assert.match(f.get('deliverySavedProgress').textContent, /2\s*\/\s*2/);
    assert.equal(f.get('deliveryDraftProgress').classList.contains('is-dirty'), false);
    assert.equal(f.get('deliveryDraftProgress').textContent, 'Không có chỉnh sửa chưa lưu');
    assert.equal(f.get('deliveryDetail').dataset.version, 'AAAAAAAAAAc=');
    assert.equal(f.get('deliverySubmitPicking').disabled, false);
    await f.input('deliveryQty-17', '0.2344');
    assert.equal(f.get('deliverySubmitPicking').disabled, true);
    await f.click('deliverySubmitPicking'); assert.equal(f.state.posts.length, 1);
});

test('same-order filter retains drafts and empty hint while another order resets visibility without leaked edits', async () => {
    const f = await fixture();
    await f.input('deliveryQty-17', '1.2345'); await f.input('deliveryReason-17', 'Giữ nội dung đang nhập');
    await f.click('deliveryShowShortages');
    assert.equal(f.get('deliveryLine-17').hidden, true); assert.equal(f.get('deliveryLine-18').hidden, false);
    assert.equal(f.get('deliveryShortagesEmpty').hidden, true);
    await f.input('deliveryQty-18', '2');
    assert.equal(f.get('deliveryLine-18').hidden, true);
    assert.equal(f.get('deliveryShortagesEmpty').hidden, false);
    assert.match(f.get('deliveryShortagesEmpty').textContent, /Chưa có mặt hàng thiếu/);
    const details = f.get('deliveryDetail').querySelector('details'); assert.ok(details);
    details.open = true; await details.emit('toggle'); await f.settle();
    f.get('deliveryQty-17').focus();
    f.state.model.serverTimeUtc = '2026-10-08T08:02:08Z'; f.state.model.inventory.serverTimeUtc = '2026-10-08T08:02:08Z';
    await f.poll();
    assert.equal(f.get('deliveryQty-17').value, '1.2345'); assert.equal(f.get('deliveryReason-17').value, 'Giữ nội dung đang nhập');
    assert.equal(f.get('deliveryQty-18').value, '2');
    assert.equal(f.get('deliveryShowShortages').getAttribute('aria-pressed'), 'true');
    assert.equal(f.get('deliveryLine-17').hidden, true); assert.equal(f.get('deliveryLine-18').hidden, true);
    assert.equal(f.get('deliveryShortagesEmpty').hidden, false);
    assert.equal(f.document.activeElement, f.get('deliveryQty-17')); assert.match(f.get('deliveryDraftProgress').textContent, /chưa lưu/i);
    assert.match(f.get('deliverySavedProgress').textContent, /1\s*\/\s*2/); assert.equal(f.state.posts.length, 0);
    const other = model(); other.delivery.id = 32; other.delivery.code = 'GH-032'; other.delivery.lookupToken = 'other-key';
    other.inventory.deliveryOrderId = 32; other.inventory.lines[0].lineId = 27;
    for (const [index, line] of other.lines.entries()) {
        line.lineId = 27 + index; line.reportedQuantityText = null; line.shortageReason = null; line.reportedAtUtc = null;
    }
    f.state.model = other; f.get('deliveryKey').value = 'GH-032';
    await f.get('deliverySearch').emit('submit'); await f.settle();
    assert.equal(f.get('deliveryShowAll').getAttribute('aria-pressed'), 'true');
    assert.equal(f.get('deliveryShowShortages').getAttribute('aria-pressed'), 'false');
    for (const id of [27, 28]) {
        assert.equal(f.get('deliveryLine-' + id).hidden, false);
        assert.equal(f.get('deliveryQty-' + id).value, ''); assert.equal(f.get('deliveryReason-' + id).value, '');
    }
    assert.equal(f.get('deliveryShortagesEmpty').hidden, true);
    assert.equal(f.get('deliveryDraftProgress').classList.contains('is-dirty'), false);
    assert.match(f.get('deliverySavedProgress').textContent, /0\s*\/\s*2/); assert.equal(f.state.posts.length, 0);
});

test('readonly capability removes edits and a durable pending request disables quantity and save actions', async () => {
    const readonly = await fixture({ capabilities: { canReport: false, canSubmit: false } });
    assert.equal(readonly.document.getElementById('deliveryQty-17'), null);
    assert.equal(readonly.document.getElementById('deliverySaveReport'), null);
    assert.equal(readonly.document.getElementById('deliverySubmitPicking'), null);
    assert.match(readonly.get('deliverySavedProgress').textContent, /1\s*\/\s*2/);
    const pending = await fixture({ pending: true });
    for (const id of ['deliveryQty-17', 'deliveryReason-17', 'deliveryFull-17', 'deliverySaveReport', 'deliverySubmitPicking'])
        assert.equal(pending.get(id).disabled, true, id);
    await pending.click('deliveryFull-17'); await pending.click('deliverySaveReport');
    assert.equal(pending.get('deliveryQty-17').value, ''); assert.equal(pending.state.posts.length, 0);
    assert.match(pending.get('deliveryDetail').textContent, /chưa xác nhận/i);
});

test('acknowledged save with failed fresh read keeps stale controls blocked until deliberate successful refresh', async () => {
    const f = await fixture(); await f.click('deliveryFull-17'); f.state.failFresh = true;
    await f.click('deliverySaveReport'); assert.equal(f.state.posts.length, 1);
    assert.equal(f.get('deliveryDetail').dataset.needsFresh, 'true');
    for (const id of ['deliveryQty-17', 'deliverySaveReport', 'deliverySubmitPicking']) assert.equal(f.get(id).disabled, true, id);
    await f.click('deliverySubmitPicking'); assert.equal(f.state.posts.length, 1);
    assert.match(f.get('deliveryPageMessage').textContent, /chưa tải được bản mới/i);
    f.state.failFresh = false;
    const refresh = f.get('deliveryDetail').querySelectorAll('button').find(button => button.textContent === 'Tải bản mới');
    assert.ok(refresh); await refresh.click(); await f.settle();
    assert.equal(f.get('deliveryDetail').dataset.needsFresh, 'false'); assert.equal(f.get('deliverySaveReport').disabled, false);
    assert.equal(f.get('deliverySubmitPicking').disabled, false);
    assert.match(f.get('deliverySavedProgress').textContent, /2\s*\/\s*2/); assert.equal(f.state.posts.length, 1);
});
