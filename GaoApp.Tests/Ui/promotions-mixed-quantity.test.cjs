const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function form() {
    const nodes = new Map();
    const make = (value = '') => ({ value, textContent: '', checked: false,
        classList: { values: new Set(), toggle(name, enabled) { enabled ? this.values.add(name) : this.values.delete(name); } } });
    const get = id => { if (!nodes.has(id)) nodes.set(id, make()); return nodes.get(id); };
    const cards = [1, 2].map(variant => {
        const fields = { productId: make('1'), variantId: make(String(variant)),
            productUnitConversionId: make(String(variant + 10)), qty: make('24') };
        return { classList: make().classList,
            querySelector(selector) { return fields[selector.match(/data-field="([^"]+)"/)[1]]; } };
    });
    const areas = [make(), make(), make(), make()];
    const document = {
        querySelector(selector) { return selector === '.promo-shell' ? { dataset: {} } : null; },
        getElementById: get, addEventListener() {},
        querySelectorAll(selector) {
            if (selector === '#promoRuleBody .promo-rule-card') return cards;
            if (selector.includes('.promo-rule-unit-area')) return areas;
            return [];
        }
    };
    const source = fs.readFileSync('GaoApp.Web/wwwroot/Admin/js/promotions.page.js', 'utf8')
        .replace(/\}\)\(\);\s*$/, 'globalThis.testForm = { buildRequest, validateRequest, refreshTypePanels, clearForm, normalizeProductData }; })();');
    const context = { document, bootstrap: { Modal: function () {} }, Date, Number, Set };
    vm.runInNewContext(source, context);
    for (const [id, value] of Object.entries({ promoType: '2', promoName: 'TH ghép vị',
        promoComboFixedPrice: '400000', promoComboPricingMode: '2', promoComboQuantity: '48',
        promoStartAt: '2026-10-07T00:00', promoEndAt: '2026-11-07T00:00' })) get(id).value = value;
    return { api: context.testForm, get, cards, areas };
}
test('mixed mode submits one common threshold and whitelist without per-flavour quantities or selling-unit filters', () => {
    const { api, areas, get } = form();
    api.refreshTypePanels();
    const request = api.buildRequest();
    assert.equal(request.comboPricingMode, 2);
    assert.equal(request.comboQuantity, 48);
    assert.equal(request.comboFixedPrice, 400000);
    assert.ok(request.comboRules.every(x => x.requiredQuantity === 1 && x.productUnitConversionId === null));
    assert.equal(api.validateRequest(request), '');
    assert.ok(areas.every(x => x.classList.values.has('d-none')));
    assert.equal(get('promoComboPriceLabel').textContent, 'Giá một thùng');
    assert.match(get('promoPreviewType').textContent, /48.*400\.000/);
});
test('switching to legacy mode retains fixed components and hides the threshold', () => {
    const { api, get, areas } = form();
    get('promoComboPricingMode').value = '1';
    api.refreshTypePanels();
    const request = api.buildRequest();
    assert.equal(request.comboQuantity, null);
    assert.ok(request.comboRules.every(x => x.requiredQuantity === 24 && x.productUnitConversionId > 0));
    assert.ok(areas.every(x => !x.classList.values.has('d-none')));
    assert.ok(get('promoComboQuantityPanel').classList.values.has('d-none'));
});
test('invalid threshold and duplicate members cannot be submitted', () => {
    const { api } = form();
    const request = api.buildRequest();
    request.comboQuantity = 0;
    assert.match(api.validateRequest(request), /lớn hơn 0/);
    request.comboQuantity = 48;
    request.comboRules[1].variantId = 1;
    assert.match(api.validateRequest(request), /khác nhau/);
});
test('flavour picker distinguishes variants under the same parent product', () => {
    const { api } = form();
    const product = api.normalizeProductData({ productId: 1, variantId: 2, productName: 'Sữa TH',
        variantName: 'TH ít đường', sku: 'TH-IT', text: 'Sữa TH · TH ít đường | TH-IT | #2' });
    assert.equal(product.productName, 'TH ít đường');
    assert.match(product.text, /TH ít đường/);
    assert.equal(product.productId, 1);
    assert.equal(product.variantId, 2);
});
