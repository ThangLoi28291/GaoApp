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
