const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

test('POS hides wholesale point actions and stale balances while preserving customer identity and existing vouchers', () => {
    const box = { innerHTML: '' };
    const document = { getElementById: id => id === 'customerInfoBox' ? box : id === 'posCustomerProfileAccess' ? { dataset: { allowed: 'true' } } : null };
    const window = { PosCommon: { formatMoney: value => Number(value || 0).toLocaleString('vi-VN'), escapeHtml: value => String(value || '') } };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.render.js'), 'utf8'), { window, document });
    const draft = { customerId: 7, customerName: 'Khách sỉ', customerPhone: '0901234567', customerPriceTier: 'WHOLESALE',
        rewardSummary: { availablePoints: 1234, redeemableVoucherCount: 12, availableVoucherCount: 2, availableVoucherValue: 20000 } };
    window.PosRender.renderCustomerInfo(draft);
    assert.match(box.innerHTML, /KHÁCH SỈ|0901234567|data-customer-action="use-voucher"/);
    assert.match(box.innerHTML, /2 phiếu khả dụng/);
    assert.match(box.innerHTML, /20\.000/);
    assert.doesNotMatch(box.innerHTML, /data-customer-action="reward"|1\.234|Lịch sử &amp; điểm|Chưa đủ điểm/);
    window.PosRender.renderCustomerInfo({ ...draft, customerPriceTier: 'RETAIL' });
    assert.match(box.innerHTML, /data-customer-action="reward"|1\.234/);
    window.PosRender.renderCustomerInfo({ ...draft, customerPriceTier: 'RETAIL', rewardSummary: { ...draft.rewardSummary, isPointEligible: false } });
    assert.doesNotMatch(box.innerHTML, /data-customer-action="reward"|1\.234/);
    const before = { orderId: 1, customerId: 7, customerPriceTier: 'RETAIL', rewardSummary: { availablePoints: 0, isPointEligible: true } };
    const after = { ...before, rewardSummary: { ...before.rewardSummary, isPointEligible: false } };
    assert.equal(window.PosRender.getDraftPatch(before, after).customerChanged, true);
});
