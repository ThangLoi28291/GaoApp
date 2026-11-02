'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const sourcePath = path.resolve(__dirname, '../GaoApp.Web/wwwroot/Admin/js/reports/management-report.page.js');
const source = fs.readFileSync(sourcePath, 'utf8');

class Node {
    constructor(selectors = []) {
        this.selectors = new Set(selectors);
        this.listeners = new Map();
        this.attributes = new Map();
        this.hidden = false;
        this.innerHTML = '';
        this.textContent = '';
        this.value = '';
        this.dataset = {};
        this.label = { hidden: false };
    }
    matches(selector) { return selector.split(',').some(item => this.selectors.has(item.trim())); }
    closest(selector) { return selector === 'label' ? this.label : this.matches(selector) ? this : null; }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    addEventListener(name, listener) {
        const listeners = this.listeners.get(name) || [];
        listeners.push(listener);
        this.listeners.set(name, listeners);
    }
    async emit(name, target = this) {
        for (const listener of this.listeners.get(name) || []) await listener({ target, preventDefault() {} });
    }
}

class NativeOption {
    constructor(text, value) { this.text = String(text); this.value = String(value); this.disabled = false; }
}

// Model the HTMLSelectElement behavior responsible for the regression: assigning an
// option value that does not exist leaves no selected option and reads back as ''.
class NativeSelect extends Node {
    constructor(values) {
        super();
        this.options = values.map(([value, text]) => new NativeOption(text, value));
        this._value = this.options[0]?.value || '';
    }
    get value() { return this._value || ''; }
    set value(value) { this._value = this.options?.some(option => option.value === String(value)) ? String(value) : ''; }
    replaceChildren(...options) { this.options = options; this._value = this.options[0]?.value || ''; }
    add(option) { this.options.push(option); }
}

function report(terminals = [{ id: 41, name: 'Quầy được cấp A' }, { id: 42, name: 'Quầy được cấp B' }]) {
    const metric = value => ({ value, available: true, quality: 1 });
    return {
        period: { fromDate: '2026-10-12', toDate: '2026-10-13' }, comparisonPeriod: null,
        generatedAtUtc: '2026-10-13T02:00:00Z', comparison: null, terminals,
        current: {
            summary: { netSales: metric(252000), cogs: metric(180000), grossProfit: metric(72000), grossMargin: metric(28.57) },
            salesOrders: 2, draftExpenses: 0, inferredCustomerOrders: 0,
            operatingExpenses: 0, operatingProfit: null,
            trend: [], expenseTrend: [], customerGroups: [], products: [], customers: [],
            categories: [], groupTrend: [], expenseCategories: []
        }
    };
}

function boot(terminalQuery = '41') {
    const fields = {
        fromDate: new Node(), toDate: new Node(),
        compare: new NativeSelect([['previous', 'Kỳ trước'], ['none', 'Không so sánh']]),
        terminalId: new NativeSelect([['', 'Tất cả quầy']]),
        customerState: new NativeSelect([['all', 'Tất cả'], ['linked', 'Có khách hàng'], ['guest', 'Khách lẻ']]),
        preset: new NativeSelect([['month', 'Tháng này'], ['custom', 'Tùy chọn']])
    };
    const form = new Node(['[data-report-filters]']);
    form.elements = { namedItem: name => fields[name] };
    const nodes = new Map([['[data-report-filters]', form]]);
    for (const name of ['results', 'error', 'live', 'context', 'quality', 'kpis', 'panels']) nodes.set('[data-report-' + name + ']', new Node());
    nodes.set('[data-report-error] span', new Node());
    const root = new Node();
    root.dataset = { section: 'profit', url: '/admin/reports/management/data', canManage: 'false', canConfirm: 'false', canTreasury: 'false', canExport: 'true' };
    const links = ['overview', 'profit', 'products'].map(section => {
        const link = new Node(); link.dataset.reportNav = section; link.href = 'http://store.test/admin/reports/' + section; return link;
    });
    root.querySelector = selector => nodes.get(selector) || null;
    root.querySelectorAll = selector => selector === '[data-report-nav]' ? links : [];
    const location = new URL('http://store.test/admin/reports/profit?fromDate=2026-10-12&toDate=2026-10-13&compare=none' + (terminalQuery === null ? '' : '&terminalId=' + encodeURIComponent(terminalQuery)));
    const requests = [], histories = [], timers = new Map();
    const context = {
        document: { querySelector: selector => selector === '[data-management-report]' ? root : null, documentElement: { classList: { contains: () => false } } },
        window: {}, location, history: { replaceState(_state, _title, value) { histories.push(String(value)); } },
        URL, URLSearchParams, Intl, Date, AbortController, Option: NativeOption,
        setTimeout(callback) { const id = timers.size + 1; timers.set(id, callback); return id; }, clearTimeout(id) { timers.delete(id); },
        getComputedStyle: () => ({ getPropertyValue: () => '#172d4d' }),
        fetch(url, options) {
            return new Promise(resolve => requests.push({ url: new URL(String(url), location.origin), options, resolve }));
        }
    };
    // Execute the complete production script. No copied params/init functions or mobile adapter are used.
    vm.runInNewContext(source, context, { filename: sourcePath });
    return {
        fields, form, root, nodes, links, requests, histories, timers,
        async reply(payload = report(), status = 200) {
            const request = requests.find(item => !item.answered);
            assert.ok(request, 'Native script must issue a report request'); request.answered = true;
            request.resolve({ ok: status >= 200 && status < 300, status, redirected: false, headers: { get: () => 'application/json' }, json: async () => payload });
            await new Promise(resolve => setImmediate(resolve));
        },
        async submit() { await root.emit('submit', form); },
        async retry() { await root.emit('click', new Node(['[data-report-retry]'])); }
    };
}

test('the initial terminal scope reaches native fetch and navigation before server options exist', async () => {
    const app = boot('41');
    assert.deepEqual(app.fields.terminalId.options.map(option => option.value), [''], 'Do not invent a valid terminal option from an untrusted URL');
    assert.equal(app.requests.length, 1);
    assert.equal(app.requests[0].url.searchParams.get('terminalId'), '41');
    assert.equal(app.requests[0].options.credentials, 'same-origin');
    assert.equal(app.requests[0].options.method, undefined, 'Initial report stays a native GET');
    assert.ok(app.links.every(link => new URL(link.href).searchParams.get('terminalId') === '41'));
    assert.equal(new URL(app.histories.at(-1), 'http://store.test').searchParams.get('terminalId'), '41');
    await app.reply();
    assert.equal(app.root.getAttribute('aria-busy'), 'false');
    assert.equal(app.nodes.get('[data-report-error]').hidden, true);
    assert.equal(app.nodes.get('[data-report-results]').hidden, false);
    assert.equal(app.fields.terminalId.value, '41');
    assert.deepEqual(app.fields.terminalId.options.map(option => [option.value, option.text]), [['', 'Tất cả quầy'], ['41', 'Quầy được cấp A'], ['42', 'Quầy được cấp B']]);
    assert.match(app.nodes.get('[data-report-quality]').innerHTML, /Chi phí thuộc toàn cửa hàng/);
    assert.match(app.nodes.get('[data-report-panels]').innerHTML, /management\/export\?[^"<]*terminalId=41/);
    assert.match(app.nodes.get('[data-report-panels]').innerHTML, /profit\/detail\?[^"<]*terminalId=41/);
    assert.match(app.nodes.get('[data-report-panels]').innerHTML, /cost-adjustments\?[^"<]*terminalId=41/);
    assert.equal(app.timers.size, 0);
});

test('choosing all after options load removes the initial terminal from fetch, navigation and rendered links', async () => {
    const app = boot('41'); await app.reply();
    app.fields.terminalId.value = ''; await app.fields.terminalId.emit('change'); await app.submit();
    assert.equal(app.requests.at(-1).url.searchParams.has('terminalId'), false);
    assert.ok(app.links.every(link => !new URL(link.href).searchParams.has('terminalId')));
    await app.reply();
    assert.equal(app.fields.terminalId.value, '');
    assert.doesNotMatch(app.nodes.get('[data-report-quality]').innerHTML, /Chi phí thuộc toàn cửa hàng/);
    assert.doesNotMatch(app.nodes.get('[data-report-panels]').innerHTML, /terminalId=/);
});

test('applying dates while terminal options are still loading retains the requested terminal and ignores the stale response', async () => {
    const app = boot('41');
    app.fields.fromDate.value = '2026-10-01';
    app.fields.preset.value = 'custom';
    await app.submit();
    assert.equal(app.requests.length, 2);
    assert.equal(app.requests[0].options.signal.aborted, true);
    assert.equal(app.requests[1].url.searchParams.get('fromDate'), '2026-10-01');
    assert.equal(app.requests[1].url.searchParams.get('terminalId'), '41', 'Changing a date must not imply choosing all terminals');
    assert.ok(app.links.every(link => new URL(link.href).searchParams.get('terminalId') === '41'));
    await app.reply(report([{ id: 999, name: 'Phản hồi cũ không được áp dụng' }]));
    assert.deepEqual(app.fields.terminalId.options.map(option => option.value), ['']);
    assert.equal(app.nodes.get('[data-report-results]').hidden, true);
    await app.reply();
    assert.equal(app.fields.terminalId.value, '41');
    assert.deepEqual(app.fields.terminalId.options.map(option => option.value), ['', '41', '42']);
    assert.equal(app.nodes.get('[data-report-results]').hidden, false);
});

test('a later explicit authorized terminal choice replaces the initial URL scope', async () => {
    const app = boot('41'); await app.reply();
    app.fields.terminalId.value = '42'; await app.fields.terminalId.emit('change'); await app.submit();
    assert.equal(app.requests.at(-1).url.searchParams.get('terminalId'), '42');
    await app.reply();
    assert.equal(app.fields.terminalId.value, '42');
    assert.ok(app.links.every(link => new URL(link.href).searchParams.get('terminalId') === '42'));
    assert.match(app.nodes.get('[data-report-panels]').innerHTML, /terminalId=42/);
});

for (const [requested, status] of [['foreign-999', 403], ['not-a-terminal', 400]]) {
    test('the backend rejection of ' + requested + ' is preserved without fabricating an authorized terminal', async () => {
        const app = boot(requested);
        assert.equal(app.requests[0].url.searchParams.get('terminalId'), requested);
        await app.reply({ message: 'Terminal không thuộc cửa hàng hiện tại.' }, status);
        assert.equal(app.nodes.get('[data-report-results]').hidden, true);
        assert.equal(app.nodes.get('[data-report-error]').hidden, false);
        assert.match(app.nodes.get('[data-report-error] span').textContent, new RegExp('HTTP ' + status));
        assert.deepEqual(app.fields.terminalId.options.map(option => option.value), ['']);
        await app.retry();
        assert.equal(app.requests.at(-1).url.searchParams.get('terminalId'), requested, 'Retry must retain the requested scope instead of falling back to all');
        await app.reply({ message: 'Terminal không thuộc cửa hàng hiện tại.' }, status);
        app.fields.terminalId.value = ''; await app.fields.terminalId.emit('change'); await app.submit();
        assert.equal(app.requests.at(-1).url.searchParams.has('terminalId'), false, 'Explicit terminal change to all clears pending URL scope');
        await app.reply();
        assert.equal(app.nodes.get('[data-report-error]').hidden, true);
        assert.equal(app.fields.terminalId.value, '');
    });
}

test('a successful payload that omits the requested terminal cannot silently render as all terminals', async () => {
    const app = boot('999'); await app.reply(report([{ id: 41, name: 'Quầy được cấp A' }]));
    assert.equal(app.nodes.get('[data-report-results]').hidden, true);
    assert.equal(app.nodes.get('[data-report-error]').hidden, false);
    assert.match(app.nodes.get('[data-report-error] span').textContent, /không nằm trong danh sách quầy/);
    assert.deepEqual(app.fields.terminalId.options.map(option => option.value), ['', '41']);
    assert.equal(app.nodes.get('[data-report-kpis]').innerHTML, '', 'Do not render a financial result under a mismatched visible scope');
    app.fields.terminalId.value = '41'; await app.fields.terminalId.emit('change'); await app.submit();
    assert.equal(app.requests.at(-1).url.searchParams.get('terminalId'), '41');
    await app.reply();
    assert.equal(app.nodes.get('[data-report-results]').hidden, false);
});

test('an initial all-terminal URL stays all while native authoritative options remain available', async () => {
    const app = boot(null);
    assert.equal(app.requests[0].url.searchParams.has('terminalId'), false);
    await app.reply();
    assert.equal(app.fields.terminalId.value, '');
    assert.deepEqual(app.fields.terminalId.options.map(option => option.value), ['', '41', '42']);
    assert.ok(app.links.every(link => !new URL(link.href).searchParams.has('terminalId')));
});
