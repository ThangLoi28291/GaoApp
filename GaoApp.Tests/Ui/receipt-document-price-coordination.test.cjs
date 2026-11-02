const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/receipt-document-actions.js'), 'utf8');
const settle = () => new Promise(resolve => setImmediate(resolve));

function fixture(hold) {
    const elements = new Map(), events = [], requests = [], listeners = [];
    const element = id => {
        if (!elements.has(id)) elements.set(id, { dataset: {}, value: '', textContent: '', disabled: false,
            classList: { toggle() {} }, querySelectorAll: () => [],
            querySelector: () => ({ value: 'csrf' }), addEventListener(name, handler) { this[name] = handler; } });
        return elements.get(id);
    };
    element('receiptDocumentPermissions').textContent = '{}';
    const window = { GaoReceiptPriceDrafts: { hold: () => hold(events) }, location: { reload: () => events.push('reload') } };
    const context = { window, document: { getElementById: element, addEventListener: (name, handler) => { if (name === 'click') listeners.push(handler); } },
        bootstrap: { Modal: class { show() {} hide() { element('receiptDocumentModal')['hidden.bs.modal'](); } } },
        fetch: async (url, options) => {
            events.push(options.method); requests.push({ url, options });
            return { ok: true, json: async () => ({ document: { id: 7, rowVersion: 'after-price-save', documentNo: 'PN7', documentTitle: 'Tên cũ' }, canRename: true }) };
        } };
    vm.runInNewContext(source, context);
    return { events, requests, element, open: () => listeners.forEach(handler => handler({
        target: { closest: selector => selector.includes('[data-receipt-title]') ? { dataset: { receiptTitle: '7' } } : null },
        preventDefault() {}, stopPropagation() {} })) };
}

test('receipt title reads and posts only after price drafts finish, using the resulting version', async () => {
    let finish;
    const gate = new Promise(resolve => finish = resolve);
    const f = fixture(async events => { events.push('hold'); await gate; events.push('prices-saved'); return () => events.push('released'); });
    f.open(); await settle(); assert.deepEqual(f.events, ['hold']); assert.equal(f.requests.length, 0);
    finish(); await settle(); assert.deepEqual(f.events, ['hold', 'prices-saved', 'GET']);
    f.element('receiptNewTitle').value = 'Tên mới'; f.element('receiptTitleSave').click(); await settle();
    const body = JSON.parse(f.requests[1].options.body);
    assert.equal(body.rowVersion, 'after-price-save'); assert.equal(body.title, 'Tên mới');
    assert.equal(f.requests[1].options.headers.RequestVerificationToken, 'csrf');
});

test('closing the title dialog releases price editing exactly once', async () => {
    const f = fixture(async events => () => events.push('released'));
    f.open(); await settle();
    f.element('receiptDocumentModal')['hidden.bs.modal']();
    f.element('receiptDocumentModal')['hidden.bs.modal']();
    assert.deepEqual(f.events, ['GET', 'released']);
});

test('a failed price save prevents reading or changing receipt titles and exposes the error', async () => {
    const f = fixture(async () => { throw new Error('Giá nhập chưa được lưu'); });
    f.open(); await settle(); assert.equal(f.requests.length, 0);
    assert.equal(f.element('receiptDocumentMessage').textContent, 'Giá nhập chưa được lưu');
    assert.equal(f.element('receiptTitleSave').disabled, true);
});

test('closing while price drafts are pending releases their eventual hold and cannot revive the cancelled dialog', async () => {
    let finish;
    const gate = new Promise(resolve => finish = resolve);
    const f = fixture(async events => { events.push('hold'); await gate; return () => events.push('released'); });
    f.open(); await settle();
    f.element('receiptDocumentModal')['hidden.bs.modal']();
    finish(); await settle();
    assert.deepEqual(f.events, ['hold', 'released']); assert.equal(f.requests.length, 0);
    f.element('receiptTitleSave').click(); await settle(); assert.equal(f.requests.length, 0);
});
