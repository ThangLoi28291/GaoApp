'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/stock-document-management.js'), 'utf8');

function fixture() {
    const warnings = [], requests = [], elements = new Map();
    const price = { dataset: { lineVersion: 'line-shown' } };
    const row = { dataset: { lineId: '7', rowVersion: 'physical-line-shown' }, querySelector: () => price, classList: { add() {} } };
    const window = { stockDocumentPage: { documentId: 44, rowVersion: 'document-shown' }, atob: () => '12345678' };
    const context = vm.createContext({ window, console,
        document: { addEventListener() {}, getElementById: id => elements.get(id) || null, querySelectorAll: () => [row] },
        alert: message => warnings.push(message),
        fetch: async (url, options) => { requests.push({ url, ...options, payload: JSON.parse(options.body) });
            return { ok: false, status: 409, text: async () => JSON.stringify({ message: 'Dữ liệu đã thay đổi' }) }; }
    });
    vm.runInContext(source, context);
    return { context, row, price, window, warnings, requests, elements };
}

test('line write tokens come from rendered inputs and never refresh from the server before writing', () => {
    const f = fixture();
    const tokens = vm.runInContext('stockDocumentLineTokens(7)', f.context);
    assert.equal(tokens.rowVersion, 'document-shown');
    assert.equal(tokens.lineRowVersion, 'line-shown');
    f.row.querySelector = () => null;
    assert.equal(vm.runInContext('stockDocumentLineTokens(7)', f.context).lineRowVersion, 'physical-line-shown');
    assert.equal(f.requests.length, 0);
});

test('stale inline quantity sends both original tokens and retains the entered quantity after 409', async () => {
    const f = fixture();
    const input = { value: '12', dataset: { lineId: '7', unitId: '2', note: 'Đếm lại' }, closest: () => f.row };
    f.context.input = input;
    const saved = await vm.runInContext('saveInlineLineQuantity(input)', f.context);
    assert.equal(saved, false);
    assert.equal(f.requests.length, 1);
    assert.equal(f.requests[0].method, 'PUT');
    assert.deepEqual(f.requests[0].payload, { rowVersion: 'document-shown', lineRowVersion: 'line-shown', unitId: 2, quantity: 12, unitCost: null, note: 'Đếm lại' });
    assert.equal(input.value, '12');
    assert.match(f.warnings[0], /Nội dung đang nhập được giữ lại/);
    assert.equal(f.window.stockDocumentPage.rowVersion, 'document-shown');
});

test('a delete confirmation uses its captured versions and cannot adopt a newer page version', async () => {
    const f = fixture();
    const button = { dataset: {}, disabled: false, textContent: '' };
    f.elements.set('btnConfirmDeleteLine', button);
    f.elements.set('deleteLineId', { value: '7', lineTokens: { rowVersion: 'modal-document', lineRowVersion: 'modal-line' } });
    f.window.stockDocumentPage.rowVersion = 'newer-page-version';
    vm.runInContext('bindConfirmDeleteLine()', f.context);
    await button.onclick();
    assert.equal(f.requests.length, 1);
    assert.equal(f.requests[0].method, 'DELETE');
    assert.deepEqual(f.requests[0].payload, { rowVersion: 'modal-document', lineRowVersion: 'modal-line' });
    assert.match(f.warnings[0], /tải lại phiếu/);
    assert.equal(button.disabled, false);
});

test('the approval payload cannot claim payment even if an old or tampered checkbox is checked', () => {
    const approval = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js'), 'utf8');
    const body = approval.slice(approval.indexOf('    function buildCommercialPayload()'), approval.indexOf('    function validateCommercialApproval('));
    const elements = { commercialMerchandisePaid: { checked: true }, commercialFreightPaid: { checked: true }, commercialHasFreight: { checked: true } };
    const context = vm.createContext({ window: { stockDocumentPage: { rowVersion: 'shown' } },
        document: { getElementById: id => elements[id] || null }, getCommercialRows: () => [],
        valueOrNull: () => 'stale payee', nullablePositiveInt: () => 1, roundMoney: n => n, readNumber: () => 123 });
    vm.runInContext(body, context);
    const payload = vm.runInContext('buildCommercialPayload()', context);
    assert.equal(payload.isMerchandisePaid, false);
    assert.equal(payload.isFreightPaid, false);
    assert.equal(payload.merchandisePayeeName, null);
});

test('employee receiving quantity uses its displayed document and line versions, retaining input after a conflict', async () => {
    const receiving = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/warehouse-receiving-detail.js'), 'utf8');
    const requests = [], warnings = [];
    const row = { dataset: { rowVersion: 'employee-line-shown' } };
    const input = { value: '8', dataset: { lineId: '7', unitId: '2' }, closest: () => row };
    const context = vm.createContext({ document: { addEventListener() {} },
        window: { warehouseReceivingDetail: { documentId: 44, rowVersion: 'employee-document-shown' } },
        alert: message => warnings.push(message), input,
        fetch: async (url, options) => { requests.push({ url, payload: JSON.parse(options.body) });
            return { ok: false, status: 409, text: async () => JSON.stringify({ message: 'Stale' }) }; }
    });
    vm.runInContext(receiving, context);
    assert.equal(await vm.runInContext('saveReceivingQtyCore(input)', context), false);
    assert.equal(requests.length, 1);
    assert.equal(requests[0].payload.rowVersion, 'employee-document-shown');
    assert.equal(requests[0].payload.lineRowVersion, 'employee-line-shown');
    assert.equal(requests[0].payload.unitCost, null);
    assert.equal(input.value, '8');
    assert.match(warnings[0], /Giữ nội dung đang nhập/);
});
