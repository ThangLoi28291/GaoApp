'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/stock-document-management.js'), 'utf8');
const functionSource = source.slice(source.indexOf('async function loadInputInvoiceAssociation()'), source.indexOf('function renderInputInvoiceList('));
async function load({ message, warning, ok = false, data } = {}) {
    const state = { textContent: 'Đang kiểm tra', className: '' };
    const elements = { StockDocumentId: { value: '7' }, inputInvoiceAssociationState: state, inputInvoiceSupplierTaxMissing: warning };
    const context = vm.createContext({
        inputInvoiceAssociationGeneration: 0, cachedInputInvoiceAssociation: {},
        document: { getElementById: id => elements[id] || null },
        fetch: async () => ({}), readApiResponse: async () => ({ ok, data: data || { message } })
    });
    vm.runInContext(functionSource, context);
    await vm.runInContext('loadInputInvoiceAssociation()', context);
    return { state, cached: context.cachedInputInvoiceAssociation };
}
test('supplier prerequisites already visible are not repeated in XML association status', async () => {
    const warning = { hidden: false, textContent: '\n Nhà cung cấp chưa có mã số thuế.\n Vui lòng cập nhật danh mục nhà cung cấp trước khi chọn hóa đơn.\n', children: [] };
    const result = await load({ warning, message: 'Nhà cung cấp chưa có mã số thuế. Vui lòng cập nhật danh mục nhà cung cấp trước khi chọn hóa đơn.' });
    assert.equal(result.state.textContent, '');
    assert.equal(result.cached, null);
});
test('hidden supplier prerequisites do not suppress association failures', async () => {
    const message = 'Nhà cung cấp chưa có mã số thuế.';
    const result = await load({ warning: { hidden: true, textContent: message }, message });
    assert.equal(result.state.textContent, message);
});
test('other association errors stay visible even when a supplier warning is present', async () => {
    const result = await load({ warning: { hidden: false, textContent: 'Nhà cung cấp chưa có mã số thuế.' }, message: 'Phiếu vừa thay đổi. Vui lòng tải lại.' });
    assert.equal(result.state.textContent, 'Phiếu vừa thay đổi. Vui lòng tải lại.');
});
test('successful association status replaces an earlier prerequisite message', async () => {
    const result = await load({ ok: true, data: { lifecycleState: 'Linked', currentInvoiceSeries: 'C26T', currentInvoiceNumber: '18' } });
    assert.match(result.state.textContent, /Đã liên kết XML C26T · 18/);
    assert.match(result.state.className, /text-success/);
});
