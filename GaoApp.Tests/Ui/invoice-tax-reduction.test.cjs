const test = require('node:test');
const assert = require('node:assert/strict');
const { calculate } = require('../../GaoApp.Web/wwwroot/Admin/js/invoice-tax-reduction.js');
for (const rate of [1, 2, 3, 5]) {
    test(`Revenue tax ${rate}% reduces only 20% of that rate`, () => {
        assert.deepEqual(calculate('1000000', rate), { reduction: BigInt(rate * 2000), total: BigInt(1000000 - rate * 2000) });
    });
}
test('Disabled restores gross; rounds half dong upward; large amounts remain exact', () => {
    assert.deepEqual(calculate('1000000', 0), { reduction: 0n, total: 1000000n });
    assert.deepEqual(calculate('250', 1), { reduction: 1n, total: 249n });
    assert.deepEqual(calculate('249', 1), { reduction: 0n, total: 249n });
    assert.deepEqual(calculate('9999999999999999', 1), { reduction: 20000000000000n, total: 9979999999999999n });
});
test('Invalid rates and negative bases cannot produce a preview', () => {
    for (const rate of [-1, 4, 20, 100]) assert.throws(() => calculate('1000000', rate));
    assert.throws(() => calculate('-1', 1));
});
