const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js'), 'utf8');

function pricing(hasVat) {
    const math = source.slice(source.indexOf('    function decimalParts('), source.indexOf('    function thumbnail('));
    const xml = source.slice(source.indexOf('    function xmlAmount('), source.indexOf('    function xmlProblem('));
    const context = { byId: () => ({ checked: hasVat }) };
    vm.runInNewContext(math + xml, context);
    return context.xmlAmount;
}

test('a bill using VAT-inclusive prices imports the actual discounted XML line plus its tax', () => {
    const amount = pricing(false)({ quantity: 3, unitPrice: 40, lineAmount: 100,
        vatRate: '8%', effectiveVatAmount: 8, lineAmountAfterVat: 108 }, 'buy');
    assert.equal(amount.amount, '108');
    assert.equal(amount.price, '36');
});

test('a receipt separating VAT imports the net amount without adding tax twice', () => {
    const amount = pricing(true)({ quantity: 3, unitPrice: 40, lineAmount: 100,
        vatRate: '8%', effectiveVatAmount: 8, lineAmountAfterVat: 108 }, 'buy');
    assert.equal(amount.amount, '100');
    assert.equal(amount.price, '33.333333333333');
});

test('unknown tax cannot silently import a taxable purchase at its net price', () => {
    assert.throws(() => pricing(false)({ quantity: 1, lineAmount: 100, lineAmountAfterVat: null }, 'buy'), /Chưa xác định được VAT/);
});

test('a gift with a positive tax charge requires review instead of dropping the charge', () => {
    const amount = pricing(false)({ quantity: 1, lineAmount: 0, effectiveVatAmount: 0 }, 'gift');
    assert.equal(amount.amount, '0');
    assert.throws(() => pricing(false)({ quantity: 1, lineAmount: 0, effectiveVatAmount: 8 }, 'gift'), /tiền VAT bằng 0/);
});
