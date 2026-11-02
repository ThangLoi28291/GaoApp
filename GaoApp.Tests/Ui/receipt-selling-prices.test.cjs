const { test } = require('node:test');
const assert = require('node:assert/strict');
const math = require('../../GaoApp.Web/wwwroot/Admin/js/receipt-selling-prices.js');
test('10% margin and markup are different, rounded upward for every unit', () => {
    assert.equal(math.suggest(11000, 10, 'margin', 100), 12300);
    assert.equal(math.suggest(66000, 10, 'margin', 100), 73400);
    assert.equal(math.suggest(264000, 10, 'margin', 100), 293400);
    assert.equal(math.suggest(11000, 10, 'markup', 100), 12100);
    assert.equal(math.suggest(9000, 10, 'margin', 100), 10000);
    assert.equal(math.suggest(11000, 20, 'margin', 100), 13800);
    assert.ok(math.profit(11000, 12300, 'margin') >= 10);
    assert.equal(math.profit(11000, 10000, 'margin'), -10);
});
test('cost uses receipt conversion, optional capitalized tax and per-line freight', () => {
    assert.equal(math.baseCost(44000, 4, 2, 8, false, 0), 11000);
    assert.equal(math.baseCost(44000, 4, 2, 8, true, 8000), 12880);
    assert.ok(Number.isNaN(math.baseCost(0, 4, 2, 8, true, 8000)));
    assert.ok(Number.isNaN(math.suggest(11000, NaN, 'margin', 100)));
    assert.ok(Number.isNaN(math.suggest(11000, 100, 'margin', 100)));
});

test('Vietnamese money entry accepts grouped currency and decimal cents without ambiguous partial parsing', () => {
    for (const [input, expected] of [['60000', 60000], ['60.000', 60000], ['60.000 đ', 60000],
        ['1.234.567,89', 1234567.89], ['60000,50', 60000.5], ['123.45', 123.45], [' 21 000 ', 21000]])
        assert.equal(math.parseMoney(input), expected, input);
    for (const input of ['', '-1000', '60,000', '12,345', '1.23.456', '60.000 abc', '1e6', 'NaN'])
        assert.ok(Number.isNaN(math.parseMoney(input)), input);
    assert.equal(math.validMoney(0), false);
    assert.equal(math.validMoney(-1), false);
    assert.equal(math.validMoney(12.345), false);
    assert.equal(math.validMoney(60000.5), true);
    assert.equal(math.validMoney(1000000000000), false);
});

test('default price preview rounds decimal division like server AwayFromZero', () => {
    assert.equal(math.unitPriceToBase(403, 40), 10.08);
    assert.equal(math.unitPriceToBase(402.99, 40), 10.07);
    assert.equal(math.unitPriceToBase(60000, 4), 15000);
    assert.equal(math.unitPriceToBase(1.005, 1), 1.01);
    assert.equal(math.unitPriceToBase(0.01, 100), 0);
    assert.equal(math.unitPriceToBase(0.01, 1e-3), 10);
    assert.ok(Number.isNaN(math.unitPriceToBase(5, 0)));
});
