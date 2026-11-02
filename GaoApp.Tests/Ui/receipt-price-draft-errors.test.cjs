'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/receipt-price-drafts.js'), 'utf8');

function fixture(responses) {
    const requests = [], events = [], inputEvents = new Map(), timers = new Map();
    let timerId = 0;
    const status = { textContent: '', className: '' }, lineStatus = { textContent: '', className: '' }, footer = {};
    const input = {
        value: '100', disabled: false, dataset: { lineVersion: 'line-before' },
        addEventListener(name, handler) { inputEvents.set(name, handler); },
        closest: () => row
    };
    const row = { dataset: { lineId: '11', exactUnitPrice: '100', exactBeforeVat: '200' }, querySelector: () => lineStatus };
    const save = { disabled: false, dataset: {}, addEventListener() {} };
    const elements = new Map([
        ['receiptPriceDraftBar', {}], ['receiptPriceDraftStatus', status], ['receiptPriceDraftSave', save],
        ['commercialHasVat', { dispatchEvent: event => events.push(event.type) }]
    ]);
    const document = {
        querySelectorAll: () => [input], getElementById: id => elements.get(id) || null,
        querySelector: selector => selector === '.sd-manager-save-state span' ? footer : { value: 'antiforgery-test' },
        dispatchEvent: event => events.push(event.type)
    };
    const window = {
        stockDocumentPage: { documentId: 4460, rowVersion: 'document-before', canApproveCommercial: true },
        addEventListener() {},
        stockDocumentRowVersion: { current: () => window.stockDocumentPage.rowVersion, update: value => { window.stockDocumentPage.rowVersion = value; } }
    };
    const context = vm.createContext({
        window, document, Event, AbortController, TypeError,
        setTimeout(handler) { const id = ++timerId; timers.set(id, handler); return id; },
        clearTimeout(id) { timers.delete(id); },
        fetch: async (url, options) => {
            requests.push({ url, payload: JSON.parse(options.body) });
            const response = responses.shift();
            assert.ok(response, 'Every attempted save must have an explicit test response.');
            if (response instanceof Error) throw response;
            return response;
        }
    });
    vm.runInContext(source, context);
    window.GaoReceiptPriceDrafts.init();
    return {
        api: window.GaoReceiptPriceDrafts, input, row, status, footer, save, window, requests, events,
        change(value) { input.value = value; inputEvents.get('input')(); }
    };
}

const response = (ok, data) => ({ ok, async json() { return data; } });
const confirmed = () => response(true, {
    rowVersion: 'document-after', lineVersions: { 11: 'line-after' },
    lineAmountsBeforeVat: { 11: 240 }, savedAtUtc: '2026-10-18T10:00:00Z'
});

test('known HTTP failure clears when all values return to their last confirmed saved values', async () => {
    const f = fixture([response(false, { message: 'Phiếu đã được người khác thay đổi.' })]);
    f.change('120');
    assert.equal(await f.api.flush(), false);
    assert.equal(f.api.getState().uncertain, false);
    assert.equal(f.api.getState().dirtyCount, 1);
    assert.match(f.api.getState().error, /người khác/);

    f.change('100');
    assert.equal(f.api.getState().dirtyCount, 0);
    assert.equal(f.api.getState().error, '');
    assert.equal(await f.api.flush(), true);
    assert.equal(f.requests.length, 1, 'Reverting a rejected edit does not create a redundant save.');
    const release = await f.api.hold(); release();
    assert.equal(f.input.disabled, false);
    assert.equal(f.row.dataset.exactBeforeVat, '200');
});

for (const [kind, failure] of [
    ['network failure', () => new TypeError('Failed to fetch')],
    ['aborted request', () => Object.assign(new Error('Timeout'), { name: 'AbortError' })],
    ['malformed successful response', () => response(true, {})],
    ['unreadable successful response', () => ({ ok: true, async json() { throw new Error('Truncated JSON'); } })]
]) {
    test(`${kind} remains unresolved after reverting all prices and cannot authorize approval or return`, async () => {
        const f = fixture([failure()]);
        f.change('120');
        assert.equal(await f.api.flush(), false);
        assert.equal(f.api.getState().uncertain, true);
        assert.match(f.status.textContent, /tải lại phiếu.*đối chiếu/);

        f.change('100');
        assert.equal(f.api.getState().dirtyCount, 0);
        assert.equal(f.api.getState().uncertain, true);
        assert.notEqual(f.api.getState().error, '');
        assert.equal(await f.api.flush(), false, 'An empty save batch cannot prove the result of an earlier uncertain request.');
        await assert.rejects(f.api.hold(), /Chưa xác minh.*tải lại phiếu/);
        assert.equal(f.input.disabled, false, 'A failed hold releases inputs for recovery.');
        assert.equal(f.requests.length, 1);
        assert.equal(f.window.stockDocumentPage.rowVersion, 'document-before');
        assert.equal(f.row.dataset.exactUnitPrice, '100');
        assert.match(f.footer.textContent, /Cần kiểm tra/);
    });
}

test('a confirmed successful retry clears uncertainty and uses the returned document, line version and exact amount', async () => {
    const f = fixture([new TypeError('Failed to fetch'), confirmed()]);
    f.change('120');
    assert.equal(await f.api.flush(), false);
    assert.equal(f.api.getState().uncertain, true);

    assert.equal(await f.api.flush(), true);
    assert.equal(f.api.getState().uncertain, false);
    assert.equal(f.api.getState().error, '');
    assert.equal(f.api.getState().dirtyCount, 0);
    assert.equal(f.requests.length, 2);
    assert.equal(f.window.stockDocumentPage.rowVersion, 'document-after');
    assert.equal(f.input.dataset.lineVersion, 'line-after');
    assert.equal(f.row.dataset.exactUnitPrice, '120');
    assert.equal(f.row.dataset.exactBeforeVat, '240');
    assert.match(f.status.textContent, /Đã lưu giá nhập/);
    assert.ok(f.events.includes('receipt-price-drafts-state-changed'));
    assert.ok(f.events.includes('change'));
});

test('a later known rejection does not erase uncertainty about an earlier unconfirmed save', async () => {
    const f = fixture([new TypeError('Failed to fetch'), response(false, { message: 'Dữ liệu vừa thay đổi.' })]);
    f.change('120');
    assert.equal(await f.api.flush(), false);
    assert.equal(await f.api.flush(), false);
    assert.equal(f.api.getState().uncertain, true);
    f.change('100');
    assert.equal(f.api.getState().dirtyCount, 0);
    assert.equal(await f.api.flush(), false);
    assert.match(f.api.getState().error, /Chưa xác minh/);
});

test('invalid unsaved input is retained after an HTTP failure and cannot be mistaken for a reverted saved value', async () => {
    const f = fixture([response(false, { message: 'Chưa thể lưu giá.' })]);
    f.change('120');
    assert.equal(await f.api.flush(), false);
    f.change('');
    assert.equal(f.api.getState().dirtyCount, 1);
    assert.equal(f.api.getState().invalidCount, 1);
    assert.notEqual(f.api.getState().error, '');
    assert.equal(await f.api.flush(), false);
    assert.equal(f.input.value, '');
    assert.equal(f.requests.length, 1);
});
