const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/stock-document-management.js'), 'utf8');
function fixture(fetch) {
    const ids = ['sdReceiptTableBody', 'sdReceiptMobileList', 'sdPaginationInfo', 'sdCurrentPageText',
        'sdCountAll', 'sdCountWorking', 'sdCountPending', 'sdCountConfirmed', 'sdBtnPrevPage', 'sdBtnNextPage'];
    const elements = Object.fromEntries(ids.map(id => [id, { innerHTML: 'Đang tải...', textContent: '0', disabled: false }]));
    const context = { window: {}, document: { addEventListener() {}, getElementById: id => elements[id] || null },
        fetch, console: { error() {} } };
    vm.runInNewContext(source, context);
    return { elements, load: context.loadReceiptList };
}

for (const networkFailure of [false, true]) {
    test(`receipt list clears desktop/mobile loading and unknown KPI totals after ${networkFailure ? 'network' : 'HTTP'} failure`, async () => {
        const f = fixture(async () => {
            if (networkFailure) throw new Error('disconnected');
            return { ok: false, status: 500, text: async () => JSON.stringify({ message: '<timeout>' }) };
        });
        await f.load();
        for (const id of ['sdReceiptTableBody', 'sdReceiptMobileList'])
            assert.doesNotMatch(f.elements[id].innerHTML, /Đang tải/);
        assert.doesNotMatch(f.elements.sdPaginationInfo.textContent, /Đang tải/);
        assert.equal(f.elements.sdBtnPrevPage.disabled, true);
        assert.equal(f.elements.sdBtnNextPage.disabled, true);
        assert.equal(f.elements.sdCountAll.textContent, '—');
        if (!networkFailure) {
            assert.match(f.elements.sdReceiptMobileList.innerHTML, /&lt;timeout&gt;/);
            assert.doesNotMatch(f.elements.sdReceiptTableBody.innerHTML, /<timeout>/);
        }
    });
}
