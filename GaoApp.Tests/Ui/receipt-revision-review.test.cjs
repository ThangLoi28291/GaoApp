const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const read = name => fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js', name), 'utf8');

function classList() {
    const values = new Set();
    return {
        toggle(value, enabled) { if (enabled) values.add(value); else values.delete(value); },
        contains(value) { return values.has(value); }
    };
}

test('revision filter shows only corrected and added goods, while existing filters and all restore unchanged goods', () => {
    const source = read('stock-document-management.js');
    const init = source.slice(source.indexOf('function initLegacyCatalogReview()'), source.indexOf('async function saveLegacyCatalogProduct()'));
    const rows = [
        { dataset: { lineId: '3', revisionChanged: 'true', priceMissing: 'true' } },
        { dataset: { lineId: '2', revisionChanged: 'true', catalogReview: 'true', exception: 'true' } },
        { dataset: { lineId: '1', revisionChanged: 'false', priceMissing: 'true' } }
    ].map(row => ({ ...row, classList: classList() }));
    const buttons = ['all', 'revision', 'catalog', 'price', 'exception'].map(value => ({
        dataset: { commercialFilter: value }, classList: classList()
    }));
    let click;
    const document = {
        getElementById: () => null,
        addEventListener(type, handler) { if (type === 'click') click = handler; },
        querySelectorAll(selector) { return selector === '.commercial-line' ? rows : buttons; }
    };
    vm.runInNewContext(init + '\ninitLegacyCatalogReview();', { document });
    const visible = () => rows.filter(row => !row.classList.contains('d-none')).map(row => row.dataset.lineId);
    const filter = value => click({ target: { closest: () => buttons.find(button => button.dataset.commercialFilter === value) } });
    assert.deepEqual(visible(), ['3', '2', '1'], 'Initialization keeps the server review order.');
    filter('revision'); assert.deepEqual(visible(), ['3', '2']);
    filter('catalog'); assert.deepEqual(visible(), ['2']);
    filter('price'); assert.deepEqual(visible(), ['3', '1']);
    filter('exception'); assert.deepEqual(visible(), ['2']);
    filter('all'); assert.deepEqual(visible(), ['3', '2', '1']);
    assert.equal(buttons.find(button => button.dataset.commercialFilter === 'all').classList.contains('active'), true);
});

test('price filter updates counts and accessible selection without hiding the row being edited, and an empty result can reset to all', () => {
    const source = read('stock-document-management.js');
    const init = source.slice(source.indexOf('function initLegacyCatalogReview()'), source.indexOf('async function saveLegacyCatalogProduct()'));
    const inputs = ['', '0', '-1'].map(value => ({ value, matches: selector => selector === '.commercial-unit-price' }));
    const rows = inputs.map((input, index) => ({
        dataset: { lineId: String(index + 1), revisionChanged: 'false', priceMissing: 'false' },
        classList: classList(), querySelector: () => input
    }));
    const buttons = ['all', 'revision', 'price'].map(value => ({
        dataset: { commercialFilter: value }, classList: classList(), attributes: {},
        setAttribute(name, value) { this.attributes[name] = value; }
    }));
    const reset = { dataset: { commercialFilter: 'all' } };
    const empty = { hidden: true }, message = { textContent: '' }, count = { textContent: '' };
    const handlers = {};
    const document = {
        getElementById: id => id === 'commercialFilterEmpty' ? empty : id === 'commercialFilterEmptyMessage' ? message : null,
        addEventListener(type, handler) { handlers[type] = handler; },
        querySelectorAll: selector => selector === '.commercial-line' ? rows : buttons,
        querySelector: () => count
    };
    vm.runInNewContext(init + '\ninitLegacyCatalogReview();', { document });
    const visible = () => rows.filter(row => !row.classList.contains('d-none')).map(row => row.dataset.lineId);
    const click = button => handlers.click({ target: { closest: () => button } });
    const choose = value => click(buttons.find(button => button.dataset.commercialFilter === value));

    assert.equal(count.textContent, '1', 'Only blank values are missing; zero and invalid negative prices are separate validation concerns.');
    choose('price');
    assert.deepEqual(visible(), ['1']);
    assert.equal(buttons[2].attributes['aria-pressed'], 'true');
    assert.equal(buttons[0].attributes['aria-pressed'], 'false');

    inputs[0].value = '12000';
    handlers.input({ target: inputs[0] });
    assert.equal(count.textContent, '0');
    assert.equal(rows[0].dataset.priceMissing, 'false');
    assert.deepEqual(visible(), ['1'], 'Typing keeps the edited row in place until another filter selection.');
    choose('price');
    assert.deepEqual(visible(), []);
    assert.equal(empty.hidden, false);
    assert.match(message.textContent, /Không còn dòng hàng để trống giá nhập/);
    click(reset);
    assert.deepEqual(visible(), ['1', '2', '3']);
    assert.equal(empty.hidden, true);
    assert.equal(buttons[0].attributes['aria-pressed'], 'true');

    inputs[1].value = '';
    handlers['receipt-pricing-inputs-changed']();
    assert.equal(count.textContent, '1', 'Programmatic pricing changes also refresh the count.');
    choose('price');
    assert.deepEqual(visible(), ['2']);
    choose('revision');
    assert.equal(empty.hidden, false);
    assert.match(message.textContent, /chỉnh sửa hoặc bổ sung/);
});

test('changed-first visual order does not change which original line receives freight rounding', () => {
    const source = read('purchase-receipt-approval.js');
    const allocate = source.slice(source.indexOf('function autoAllocateFreight()'), source.indexOf('function renderAllocationStatus('));
    const rows = [1, 2, 3].map(id => ({
        dataset: { lineId: String(id), lineNo: String(id), lineAfterVat: '1' },
        input: { value: '' },
        querySelector() { return this.input; }
    }));
    let visualOrder = [rows[2], rows[0], rows[1]];
    const document = { getElementById: id => id === 'commercialFreightTotal' ? { value: '.01' } : { checked: true } };
    const context = vm.createContext({
        document, getCommercialRows: () => [...visualOrder],
        readNumber: input => Number(input.value),
        roundMoney: value => Math.round((Number(value) + Number.EPSILON) * 100) / 100,
        decimalInput: String
    });
    vm.runInContext(allocate, context);
    vm.runInContext('autoAllocateFreight();', context);
    assert.deepEqual(rows.map(row => Number(row.input.value)), [0, 0, .01]);
    assert.deepEqual(visualOrder.map(row => row.dataset.lineId), ['3', '1', '2'], 'Allocation never moves the visible rows.');
    visualOrder = [rows[1], rows[2], rows[0]];
    vm.runInContext('autoAllocateFreight();', context);
    assert.deepEqual(rows.map(row => Number(row.input.value)), [0, 0, .01]);
});
