'use strict';
const test = require('node:test'), assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.customer-display-customer.js'), 'utf8');
function fixture() {
    const elements = new Map();
    const getElementById = id => {
        if (!elements.has(id)) elements.set(id, { textContent: '', hidden: true });
        return elements.get(id);
    };
    const window = {};
    vm.runInNewContext(source, { window, document: { getElementById } });
    return { ...window.CustomerDisplayCustomer, element: getElementById };
}

test('current points come from the selected customer reward summary, including a confirmed zero balance', () => {
    const f = fixture();
    const order = Object.freeze({ customerId: 5, customerName: 'Nguyễn Minh Anh', customerPhone: '0901234567',
        rewardSummary: Object.freeze({ customerId: 5, availablePoints: 1250, balanceAmount: 1250000 }) });
    f.render(order, ['cdCustomer', 'cdPaymentCustomer']);
    assert.equal(f.element('cdCustomerPoints').textContent, '1.250');
    assert.equal(f.element('cdPaymentCustomerPoints').textContent, '1.250');
    assert.equal(f.element('cdCustomerPhone').hidden, false);
    assert.equal(f.element('cdCustomerPointsBox').hidden, false);
    assert.equal(order.rewardSummary.availablePoints, 1250);
    f.render({ customerId: 6, customerName: 'Khách mới', rewardSummary: { customerId: 6, availablePoints: 0 } }, ['cdCustomer']);
    assert.equal(f.element('cdCustomerPoints').textContent, '0');
    assert.equal(f.element('cdCustomerPointsBox').hidden, false);
    assert.equal(f.element('cdCustomerPhone').hidden, true);
});

test('anonymous, missing, invalid and mismatched balances never inherit the last customer points', () => {
    const f = fixture();
    for (const order of [null, { customerId: 5 }, { customerId: 5, rewardSummary: { customerId: 6, availablePoints: 100 } },
        ...[null, '', NaN, Infinity, -1].map(customerCurrentPoints => ({ customerId: 5, customerCurrentPoints }))]) {
        f.render({ customerId: 5, customerCurrentPoints: 100 }, ['cdCustomer']);
        f.render(order, ['cdCustomer']);
        assert.equal(f.element('cdCustomerPointsBox').hidden, true);
        assert.equal(f.element('cdCustomerPoints').textContent, '');
    }
    f.render(null, ['cdCustomer']);
    assert.equal(f.element('cdCustomerName').textContent, 'Khách lẻ');
    assert.equal(f.element('cdCustomerPhone').textContent, '');
});

test('wholesale and ineligible customers cannot show points from stale cached data', () => {
    const f = fixture();
    for (const order of [
        { customerPriceTier: 'WHOLESALE' }, { customerPriceTier: ' wholesale ' },
        { customer: { priceTier: 'WHOLESALE' } }, { rewardSummary: { availablePoints: 500, isPointEligible: false } }
    ]) {
        const data = { customerId: 5, customerCurrentPoints: 999, ...order };
        assert.equal(f.info(data).points, null);
        f.render(data, ['cdCustomer', 'cdPaymentCustomer', 'cdReceiptCustomer']);
        for (const prefix of ['cdCustomer', 'cdPaymentCustomer', 'cdReceiptCustomer']) {
            assert.equal(f.element(prefix + 'PointsBox').hidden, true);
            assert.equal(f.element(prefix + 'Points').textContent, '');
        }
    }
});

test('receipt balance takes priority after checkout and customer text is written as text', () => {
    const f = fixture();
    f.render({ customerId: 5, customerName: '<img src=x>', customerPhone: '<script>', customerCurrentPoints: 1450,
        rewardSummary: { customerId: 5, availablePoints: 1250 } }, ['cdReceiptCustomer']);
    assert.equal(f.element('cdReceiptCustomerPoints').textContent, '1.450');
    assert.equal(f.element('cdReceiptCustomerName').textContent, '<img src=x>');
    assert.equal(f.element('cdReceiptCustomerPhone').textContent, '<script>');
});
