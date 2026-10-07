const { test } = require('node:test');
const assert = require('node:assert/strict');
const core = require('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.core.js');
const fs = require('node:fs');
const vm = require('node:vm');

function fixture() {
    const state = core.initial({ storeId: 1, terminalId: 2, userId: 3, shiftId: 4,
        permissions: ['pos.order.create', 'pos.order.discount'], screen: {} });
    const catalog = { customers: [], products: [1, 2, 3, 4].map(variant => ({
        id: variant, productId: 1, productName: `TH vị ${variant}`, price: 10000,
        units: [{ id: variant * 10, unitId: 1, unitName: 'Hộp', factor: 1,
            price: 10000, isBaseUnit: true, isDefaultForSale: true, barcodes: [`BOX${variant}`] },
            { id: variant * 10 + 1, unitId: 2, unitName: 'Lốc', factor: 6,
                price: 60000, barcodes: [`PACK${variant}`] }]
    })), promotions: [mixed()] };
    const act = (url, body = {}, method = 'POST') => core.clone(core.apply(state, catalog, {
        url, body, method, occurredAt: '2026-10-07T03:00:00Z'
    }));
    act('/admin/pos/cart/ensure');
    const scan = (barcode, qty) => act('/admin/pos/cart/current/scan', { barcode, qty });
    return { catalog, state, act, scan };
}
function mixed() {
    return { id: 1, name: 'TH ghép vị', type: 2, priority: 0, comboPricingMode: 2,
        comboQuantity: 48, comboBaseUnitId: 1, comboFixedPrice: 400000,
        comboRules: [{ productId: 1, variantId: 1 }, { productId: 1, variantId: 2 }] };
}

for (const [first, second, expected] of [[24, 24, 400000], [30, 18, 400000], [25, 25, 416667],
    [49, 1, 416667], [24, 25, 408333], [48, 48, 800000], [24, 23, 470000]]) {
    test(`${first} + ${second} base units use whole-quantity tier online-equivalent total ${expected}`, () => {
        const { scan } = fixture();
        scan('BOX1', first);
        const draft = scan('BOX2', second);
        assert.equal(draft.grandTotal, expected);
        assert.equal(draft.lines.reduce((sum, line) => sum + line.lineTotal, 0), expected);
        assert.equal(draft.comboDiscountTotal, 0);
    });
}
test('mixed selling units, quantity edit and restart restore eligibility and normal price', () => {
    const { scan, act, state, catalog } = fixture();
    scan('BOX1', 24);
    const draft = scan('PACK2', 4);
    assert.equal(draft.grandTotal, 400000);
    const reloaded = JSON.parse(JSON.stringify(state));
    const line = draft.lines.find(x => x.variantId === 1);
    const changed = core.apply(reloaded, catalog, { url: `/admin/pos/lines/${line.lineId}?qty=23`, method: 'PATCH', body: {} });
    assert.equal(changed.grandTotal, 470000);
    assert.ok(changed.lines.every(x => x.promotionId === null));
    assert.equal(act(`/admin/pos/lines/${line.lineId}?qty=26`, {}, 'PATCH').grandTotal, 416667);
});
test('fractional pack unit prices reconcile rounded line totals with the order total', () => {
    const { scan, catalog } = fixture();
    for (const product of catalog.products) product.units[1].price = 55000;
    scan('BOX1', 25);
    const draft = scan('BOX2', 25);
    assert.equal(draft.grandTotal, 416667);
    assert.equal(draft.lines.reduce((sum, line) => sum + line.lineTotal, 0), draft.grandTotal);
});
test('independent groups combine and overlapping group does not reuse quantities', () => {
    const { scan, catalog } = fixture();
    catalog.promotions[0].priority = 5;
    catalog.promotions.push({ ...mixed(), id: 2, comboFixedPrice: 350000 }, { ...mixed(), id: 3,
        comboRules: [{ productId: 1, variantId: 3 }, { productId: 1, variantId: 4 }] });
    scan('BOX1', 24); scan('BOX2', 24); scan('BOX3', 24);
    const draft = scan('BOX4', 24);
    assert.equal(draft.grandTotal, 800000);
    assert.deepEqual(draft.lines.map(x => x.promotionId), [1, 1, 3, 3]);
});
test('group competes with better product discount and higher-priority fixed-item combo', () => {
    const { scan, catalog, act } = fixture();
    const product = { id: 9, name: 'Giảm 20%', type: 1, priority: 0, discountType: 1,
        discountValue: 20, items: [{ productId: 1 }] };
    catalog.promotions.push(product);
    scan('BOX1', 24);
    let draft = scan('BOX2', 24);
    assert.equal(draft.grandTotal, 384000);
    product.discountValue = 10;
    draft = act(`/admin/pos/lines/${draft.lines[0].lineId}?qty=24`, {}, 'PATCH');
    assert.equal(draft.grandTotal, 400000);
    catalog.promotions.push({ ...mixed(), id: 10, priority: 5, comboPricingMode: 1,
        comboFixedPrice: 350000, comboRules: [{ productId: 1, variantId: 1, requiredQuantity: 24 },
            { productId: 1, variantId: 2, requiredQuantity: 24 }] });
    draft = act(`/admin/pos/lines/${draft.lines[0].lineId}?qty=24`, {}, 'PATCH');
    assert.equal(draft.grandTotal, 350000);
});
test('base-unit mismatch and unlisted variant do not reach group threshold', () => {
    const { scan, catalog } = fixture();
    catalog.products[1].units[0].unitId = 99;
    scan('BOX1', 24);
    assert.equal(scan('BOX2', 24).grandTotal, 480000);
    assert.equal(scan('BOX3', 48).grandTotal, 960000);
});
test('POS row shows effective group price and one promotion label with the configured note', () => {
    const { scan, catalog } = fixture();
    catalog.promotions[0].comboNote = 'Ghép vị từ 48 hộp';
    scan('BOX1', 24);
    const draft = scan('BOX2', 24);
    const window = { PosCommon: { formatMoney: value => Number(value).toLocaleString('vi-VN'),
        escapeHtml: value => String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;') } };
    vm.runInNewContext(fs.readFileSync('GaoApp.Web/wwwroot/Admin/js/pos/pos.render.js', 'utf8'), { window });
    const html = window.PosRender.buildDraftLineRowHtml(draft.lines[0], 0);
    assert.match(html, /pos-line-promo-price[\s\S]*8\.333/);
    assert.match(html, /title="Ghép vị từ 48 hộp"/);
    assert.equal((html.match(/<span>\s*TH ghép vị\s*<\/span>/g) || []).length, 1);
    assert.doesNotMatch(html, /pos-line-combo-badge/);
});
