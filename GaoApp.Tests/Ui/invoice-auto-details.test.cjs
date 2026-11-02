const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/invoice-detail.js'), 'utf8');
const flush = async () => { await new Promise(setImmediate); await new Promise(setImmediate); };
const decode = value => value.replace(/&quot;/g, '"').replace(/&#0?39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
const encode = value => String(value).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

// Only the DOM operations used by the production script are implemented here.
// Generated edit markup is parsed, so assertions inspect the inputs/buttons the script creates.
class Element {
    constructor(tag = 'div', attrs = {}) {
        this.tag = tag; this.attrs = {}; this.dataset = {}; this.childNodes = []; this.listeners = {};
        this.classes = new Set(); this.style = {}; this.disabled = false; this.value = '';
        this.classList = {
            add: (...names) => names.forEach(name => this.classes.add(name)),
            remove: (...names) => names.forEach(name => this.classes.delete(name)),
            contains: name => this.classes.has(name)
        };
        for (const [name, value] of Object.entries(attrs)) this.setAttribute(name, value);
    }
    get children() { return this.childNodes.filter(node => node instanceof Element); }
    setAttribute(name, value) {
        this.attrs[name] = String(value);
        if (name === 'class') this.classes = new Set(String(value).split(/\s+/).filter(Boolean));
        if (name === 'value') this.value = String(value);
        if (name.startsWith('data-')) this.dataset[name.slice(5).replace(/-([a-z])/g, (_, char) => char.toUpperCase())] = String(value);
    }
    getAttribute(name) { return name === 'class' ? [...this.classes].join(' ') : this.attrs[name] ?? null; }
    removeAttribute(name) { delete this.attrs[name]; }
    get action() { return this.getAttribute('action'); }
    get innerHTML() { return this.childNodes.map(node => typeof node === 'string' ? node : node.outerHTML).join(''); }
    set innerHTML(html) { this.childNodes = parse(html, this); }
    get outerHTML() {
        const attrs = { ...this.attrs };
        if (this.classes.size) attrs.class = [...this.classes].join(' ');
        return `<${this.tag}${Object.entries(attrs).map(([name, value]) => ` ${name}="${encode(value)}"`).join('')}>${this.innerHTML}</${this.tag}>`;
    }
    get textContent() { return this.childNodes.map(node => typeof node === 'string' ? decode(node) : node.textContent).join(''); }
    set textContent(text) { this.childNodes = [encode(text)]; }
    querySelectorAll(selector) { return descendants(this).filter(node => matches(node, selector)); }
    querySelector(selector) { return this.querySelectorAll(selector)[0] ?? null; }
    closest(selector) { for (let node = this; node; node = node.parent) if (matches(node, selector)) return node; return null; }
    contains(node) { return node === this || descendants(this).includes(node); }
    addEventListener(name, callback) { (this.listeners[name] ??= []).push(callback); }
    removeEventListener(name, callback) { this.listeners[name] = (this.listeners[name] ?? []).filter(item => item !== callback); }
    async fire(name, event = {}) {
        event = { target: this, preventDefault() {}, stopPropagation() {}, ...event };
        await this[`on${name}`]?.(event);
        for (const callback of [...(this.listeners[name] ?? [])]) await callback(event);
    }
    focus() { this.focused = true; }
    select() { this.selected = true; }
}
function parse(html, parent) {
    const root = { childNodes: [] }, stack = [root];
    for (const token of String(html).match(/<[^>]*>|[^<]+/g) ?? []) {
        if (token.startsWith('</')) { stack.pop(); continue; }
        if (!token.startsWith('<')) { stack.at(-1).childNodes.push(token); continue; }
        const tag = token.match(/^<([\w-]+)/)?.[1];
        if (!tag) continue;
        const attrs = {};
        for (const match of token.slice(tag.length + 1, -1).matchAll(/([\w:-]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'))?/g))
            attrs[match[1]] = decode(match[2] ?? match[3] ?? '');
        const node = new Element(tag, attrs);
        node.parent = stack.length === 1 ? parent : stack.at(-1);
        stack.at(-1).childNodes.push(node);
        if (!['input', 'br', 'hr', 'img'].includes(tag) && !token.endsWith('/>')) stack.push(node);
    }
    return root.childNodes;
}
function descendants(node) { return node.children.flatMap(child => [child, ...descendants(child)]); }
function matches(node, selector) {
    if (!(node instanceof Element)) return false;
    if (selector.includes(' ')) {
        const parts = selector.split(/\s+/), last = parts.pop();
        return matches(node, last) && !!node.parent?.closest(parts.join(' '));
    }
    if (selector.endsWith(':last-child')) return matches(node, selector.replace(':last-child', '')) && node.parent?.children.at(-1) === node;
    const tag = selector.match(/^[\w-]+/)?.[0];
    if (tag && node.tag !== tag) return false;
    const id = selector.match(/#([\w-]+)/)?.[1];
    if (id && node.getAttribute('id') !== id) return false;
    for (const item of selector.matchAll(/\.([\w-]+)/g)) if (!node.classes.has(item[1])) return false;
    for (const item of selector.matchAll(/\[([\w-]+)(?:=['"]?([^'"\]]+)['"]?)?\]/g))
        if (node.getAttribute(item[1]) === null || item[2] !== undefined && node.getAttribute(item[1]) !== item[2]) return false;
    return true;
}
class FormDataFixture {
    constructor(form) {
        this.values = new Map();
        for (const input of form?.querySelectorAll('input[name]') ?? []) this.append(input.getAttribute('name'), input.value);
    }
    append(name, value) { this.values.set(name, String(value)); }
    get(name) { return this.values.get(name) ?? null; }
    has(name) { return this.values.has(name); }
}
const row = (id, auto = true) => `<tr class="manual-detail-row" data-manual-detail-id="${id}" data-manual-quantity="2" data-manual-unit-price="10000" data-manual-vat-rate="8" data-manual-note="Original note">
    <td>#${id}</td><td>Milk</td><td data-manual-display="quantity">2</td><td data-manual-display="unitPrice">10,000</td>
    <td data-manual-display="vatRate">8</td><td data-manual-display="note">Original note</td>
    ${auto ? '<td data-provenance="orderLine">#73</td><td data-provenance="allocation">#19</td>' : ''}
    <td data-manual-actions><button data-edit-manual-detail-id="${id}">Sửa</button><button data-delete-manual-detail-id="${id}" data-delete-manual-detail-name="Milk">Xóa</button></td></tr>`;
const createForm = `<form id="manualDetailForm" action="/Admin/Invoice/CreateManualDetail">
    <input name="__RequestVerificationToken" value="fixture-csrf"><input name="InvoiceHeadId" value="41">
    <input id="manualQuantity" name="Quantity" value="3"><input id="manualUnitPrice" name="UnitPrice" value="12000">
    <input id="manualVatRate" name="VatRate" value="8"><input name="Note" value="Added product">
    <button type="submit">Lưu sản phẩm</button></form>`;
const response = (value, ok = true) => ({ ok, headers: { get: () => typeof value === 'string' ? 'text/html' : 'application/json' },
    json: async () => value, text: async () => value });
const mutationSuccess = () => response({ success: true, invoiceHeadId: 41 });
function fixture({ locked = false, request } = {}) {
    const root = new Element('document');
    root.innerHTML = `<div id="invoiceDetailPage" data-invoice-head-id="41" data-is-locked="${locked}" data-is-adjustment-amount-invoice="false">
        <form id="invoiceAntiForgeryForm"><input name="__RequestVerificationToken" value="fixture-csrf"></form>
        <button id="btnOpenManualDetailModal" data-invoice-head-id="41">F2</button>
        <div id="invoiceSummaryBox">Original summary</div>
        <div id="autoLinesBox"><button data-add-invoice-detail>Thêm sản phẩm</button><table><tbody>${row(51)}</tbody></table></div>
        <div id="manualLinesBox"><table><tbody>${row(52, false)}</tbody></table></div></div>
        <div id="manualDetailModal"><div id="manualDetailModalContent"></div></div>
        <div id="deleteManualDetailModal"><span id="deleteManualDetailName"></span><input id="deleteManualDetailId">
        <button id="btnConfirmDeleteManualDetail">Xóa dòng</button></div>`;
    const requests = [], alerts = [], stockEvents = [], modals = new Map();
    class Modal {
        constructor(element) { this.element = element; this.shown = 0; this.hidden = 0; modals.set(element.getAttribute('id'), this); }
        show() { this.shown++; this.element.classList.add('show'); this.element.fire('shown.bs.modal'); }
        hide() { this.hidden++; this.element.classList.remove('show'); }
    }
    const document = {
        getElementById: id => root.querySelector(`#${id}`), querySelector: selector => root.querySelector(selector),
        querySelectorAll: selector => root.querySelectorAll(selector), addEventListener: (name, callback) => root.addEventListener(name, callback),
        dispatchEvent: event => {
            if (event.type === 'invoice:lines-changed') stockEvents.push({
                summary: document.getElementById('invoiceSummaryBox').innerHTML,
                auto: document.getElementById('autoLinesBox').innerHTML,
                manual: document.getElementById('manualLinesBox').innerHTML
            });
        }
    };
    const fetch = async (url, options) => {
        requests.push({ url, options });
        if (request) { const custom = await request(url, options); if (custom !== undefined) return custom; }
        if (options.method === 'POST') return mutationSuccess();
        if (url.includes('CreateManualDetail?')) return response(createForm);
        if (url.includes('InvoiceSummaryPartial')) return response('Updated summary');
        if (url.includes('AutoLinesPartial')) return response(`<table><tbody>${row(51)}</tbody></table>Updated Auto`);
        if (url.includes('ManualLinesPartial')) return response(`<table><tbody>${row(52, false)}</tbody></table>Updated Manual`);
        throw new Error('Unexpected request: ' + url);
    };
    vm.runInNewContext(source, { document, window: { bootstrap: { Modal }, addEventListener() {} }, bootstrap: { Modal }, fetch,
        FormData: FormDataFixture, CustomEvent: class { constructor(type) { this.type = type; } },
        alert: message => alerts.push(message), console: { error() {} }, setTimeout, clearTimeout });
    return { document, requests, alerts, stockEvents, modals, root,
        el: id => document.getElementById(id), autoRow: () => root.querySelector('[data-manual-detail-id="51"]'),
        manualRow: () => root.querySelector('[data-manual-detail-id="52"]'),
        posts: () => requests.filter(item => item.options.method === 'POST') };
}
const edit = async row => row.querySelector('[data-edit-manual-detail-id]').fire('click');
const save = async row => row.querySelector('[data-save-manual-edit-id]').fire('click');

test('Auto edits use generic endpoint and CSRF while provenance stays intact', async () => {
    const view = fixture();
    const item = view.autoRow();
    const orderLine = item.querySelector('[data-provenance="orderLine"]').outerHTML;
    const allocation = item.querySelector('[data-provenance="allocation"]').outerHTML;
    await edit(item);
    assert.equal(item.classList.contains('is-editing'), true);
    item.querySelector('[data-manual-edit-field="quantity"]').value = '4';
    item.querySelector('[data-manual-edit-field="unitPrice"]').value = '15000';
    item.querySelector('[data-manual-edit-field="vatRate"]').value = '10';
    item.querySelector('[data-manual-edit-field="note"]').value = '<new note>';
    assert.equal(item.querySelector('[data-provenance="orderLine"]').outerHTML, orderLine);
    assert.equal(item.querySelector('[data-provenance="allocation"]').outerHTML, allocation);
    await save(item);
    const [{ url, options }] = view.posts();
    assert.equal(url, '/Admin/Invoice/UpdateDetail');
    assert.equal(options.body.get('InvoiceDetailId'), '51');
    assert.equal(options.body.get('Quantity'), '4');
    assert.equal(options.body.get('UnitPrice'), '15000');
    assert.equal(options.body.get('VatRate'), '10');
    assert.equal(options.body.get('Note'), '<new note>');
    assert.equal(options.body.get('__RequestVerificationToken'), 'fixture-csrf');
    assert.equal(view.stockEvents.length, 1);
    assert.match(view.stockEvents[0].auto, /Updated Auto/);
    assert.match(view.stockEvents[0].manual, /Updated Manual/);
});

test('Auto double-click editing can be cancelled without writes and restores its provenance', async () => {
    const view = fixture(), item = view.autoRow(), before = item.innerHTML;
    await item.fire('dblclick');
    item.querySelector('[data-manual-edit-field="quantity"]').value = '99';
    await item.querySelector('[data-cancel-manual-edit-id]').fire('click');
    assert.equal(item.classList.contains('is-editing'), false);
    assert.equal(item.innerHTML, before);
    assert.equal(view.posts().length, 0);
    await edit(item);
    assert.equal(item.classList.contains('is-editing'), true, 'restored explicit edit button is rebound');
});

test('Manual edits also use the generic endpoint', async () => {
    const view = fixture(), item = view.manualRow();
    await edit(item); await save(item);
    assert.equal(view.posts()[0].url, '/Admin/Invoice/UpdateDetail');
    assert.equal(view.posts()[0].options.body.get('InvoiceDetailId'), '52');
});

test('Auto deletion requires confirmation; dismissing does not write, confirming uses generic endpoint', async () => {
    const view = fixture();
    await view.autoRow().querySelector('[data-delete-manual-detail-id]').fire('click');
    assert.equal(view.el('deleteManualDetailId').value, '51');
    assert.equal(view.el('deleteManualDetailName').textContent, 'Milk');
    assert.equal(view.modals.get('deleteManualDetailModal').shown, 1);
    assert.equal(view.posts().length, 0);
    view.modals.get('deleteManualDetailModal').hide();
    assert.equal(view.posts().length, 0);
    await view.autoRow().querySelector('[data-delete-manual-detail-id]').fire('click');
    await view.el('btnConfirmDeleteManualDetail').fire('click');
    const [{ url, options }] = view.posts();
    assert.equal(url, '/Admin/Invoice/DeleteDetail');
    assert.equal(options.body.get('invoiceDetailId'), '51');
    assert.equal(options.body.get('__RequestVerificationToken'), 'fixture-csrf');
    assert.equal(view.stockEvents.length, 1);
});

test('Auto add opens existing Manual form and creates through Manual endpoint, with F2 retained', async () => {
    const view = fixture();
    const button = view.root.querySelector('[data-add-invoice-detail]');
    await view.root.fire('click', { target: button }); await flush();
    assert.match(view.requests[0].url, /CreateManualDetail\?invoiceHeadId=41$/);
    assert.equal(view.modals.get('manualDetailModal').shown, 1);
    await view.el('manualDetailForm').fire('submit');
    const [{ url, options }] = view.posts();
    assert.equal(url, '/Admin/Invoice/CreateManualDetail');
    assert.equal(options.body.get('InvoiceHeadId'), '41');
    assert.equal(options.body.get('Quantity'), '3');
    assert.equal(options.body.get('__RequestVerificationToken'), 'fixture-csrf');
    assert.equal(options.body.has('SourceType'), false, 'the browser cannot fabricate an Auto source');
    assert.equal(view.stockEvents.length, 1);
    await view.root.fire('keydown', { key: 'F2' }); await flush();
    assert.equal(view.modals.get('manualDetailModal').shown, 2);
});

test('summary and both tables wait for all responses before replacement and one stock event', async () => {
    let release;
    const view = fixture({ request: url => url.includes('ManualLinesPartial')
        ? new Promise(resolve => { release = () => resolve(response('Delayed Manual')); }) : undefined });
    const initial = ['invoiceSummaryBox', 'autoLinesBox', 'manualLinesBox'].map(id => view.el(id).innerHTML);
    await edit(view.autoRow());
    const pending = save(view.autoRow()); await flush();
    assert.equal(typeof release, 'function');
    for (const [index, id] of ['invoiceSummaryBox', 'autoLinesBox', 'manualLinesBox'].entries())
        if (id !== 'autoLinesBox') assert.equal(view.el(id).innerHTML, initial[index]);
    assert.equal(view.autoRow().classList.contains('is-editing'), true);
    assert.equal(view.stockEvents.length, 0);
    release(); await pending;
    assert.equal(view.stockEvents.length, 1);
    assert.equal(view.stockEvents[0].summary, 'Updated summary');
    assert.match(view.stockEvents[0].auto, /Updated Auto/);
    assert.equal(view.stockEvents[0].manual, 'Delayed Manual');
});

for (const failure of ['update', 'partial']) {
    test(`${failure} failure retains the Auto editor and both existing tables`, async () => {
        const view = fixture({ request: (url, options) => failure === 'update' && options.method === 'POST'
            ? response({ success: false, message: 'Synthetic rejected update' }, false)
            : failure === 'partial' && url.includes('AutoLinesPartial') ? response('Synthetic failed partial', false) : undefined });
        const manual = view.el('manualLinesBox').innerHTML, summary = view.el('invoiceSummaryBox').innerHTML;
        await edit(view.autoRow());
        view.autoRow().querySelector('[data-manual-edit-field="note"]').value = 'Retain my edit';
        await save(view.autoRow());
        assert.equal(view.autoRow().classList.contains('is-editing'), true);
        assert.equal(view.autoRow().classList.contains('is-saving'), false);
        assert.equal(view.autoRow().querySelector('[data-manual-edit-field="note"]').value, 'Retain my edit');
        assert.equal(view.el('manualLinesBox').innerHTML, manual);
        assert.equal(view.el('invoiceSummaryBox').innerHTML, summary);
        assert.equal(view.stockEvents.length, 0);
        assert.ok(view.alerts.length);
        if (failure === 'partial') {
            assert.match(view.alerts.at(-1), /Đã lưu thay đổi nhưng chưa tải lại/);
            assert.doesNotMatch(view.alerts.at(-1), /Lỗi kết nối/);
        } else assert.equal(view.alerts.at(-1), 'Synthetic rejected update');
    });
}

test('nonfinite numbers and VAT above 100 retain the editor without submitting', async () => {
    for (const [field, value] of [['quantity', 'not-a-number'], ['quantity', '1e309'], ['unitPrice', '1e309'], ['vatRate', '101']]) {
        const view = fixture();
        await edit(view.autoRow());
        view.autoRow().querySelector(`[data-manual-edit-field="${field}"]`).value = value;
        await save(view.autoRow());
        assert.equal(view.posts().length, 0, `${field}=${value} must not reach the server`);
        assert.equal(view.autoRow().classList.contains('is-editing'), true);
        assert.equal(view.autoRow().classList.contains('is-saving'), false);
        assert.equal(view.stockEvents.length, 0);
        assert.match(view.alerts.at(-1), field === 'vatRate' ? /VAT.*0.*100/ : /phải là số hợp lệ/);
    }
});

test('pressing Enter twice while an edit is saving issues only one mutation', async () => {
    let release;
    const view = fixture({ request: (_, options) => options.method === 'POST'
        ? new Promise(resolve => { release = resolve; }) : undefined });
    await edit(view.autoRow());
    const input = view.autoRow().querySelector('[data-manual-edit-field="quantity"]');
    const firstEnter = input.fire('keydown', { key: 'Enter' });
    await flush();
    assert.equal(view.autoRow().classList.contains('is-saving'), true);
    await input.fire('keydown', { key: 'Enter' });
    assert.equal(view.posts().length, 1);
    assert.equal(view.stockEvents.length, 0);
    release(mutationSuccess());
    await firstEnter;
    assert.equal(view.posts().length, 1);
    assert.equal(view.stockEvents.length, 1);
});

test('delete rejection retains confirmation and row; locked invoices reject all mutation entry points', async () => {
    const failed = fixture({ request: (_, options) => options.method === 'POST' ? response({ success: false, message: 'Cannot delete' }, false) : undefined });
    await failed.autoRow().querySelector('[data-delete-manual-detail-id]').fire('click');
    await failed.el('btnConfirmDeleteManualDetail').fire('click');
    assert.equal(failed.modals.get('deleteManualDetailModal').hidden, 0);
    assert.ok(failed.autoRow()); assert.equal(failed.stockEvents.length, 0);

    const locked = fixture({ locked: true });
    await edit(locked.autoRow()); await locked.autoRow().fire('dblclick');
    assert.equal(locked.autoRow().classList.contains('is-editing'), false);
    await locked.autoRow().querySelector('[data-delete-manual-detail-id]').fire('click');
    locked.el('deleteManualDetailId').value = '51';
    await locked.el('btnConfirmDeleteManualDetail').fire('click');
    await locked.root.fire('click', { target: locked.root.querySelector('[data-add-invoice-detail]') });
    await locked.root.fire('keydown', { key: 'F2' }); await flush();
    assert.equal(locked.requests.length, 0);
    assert.equal(locked.modals.get('deleteManualDetailModal').shown, 0);

    const transitioning = fixture();
    await edit(transitioning.autoRow());
    transitioning.el('invoiceDetailPage').setAttribute('data-is-locked', 'true');
    await save(transitioning.autoRow());
    assert.equal(transitioning.posts().length, 0);
});
