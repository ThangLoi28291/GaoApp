'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const vm = require('node:vm');
const review = require(path.resolve(__dirname, '../GaoApp.Web/wwwroot/Admin/js/receipt-approval-workspace.js'));

function goods(overrides = {}) {
    return {
        id: '11', lineNo: '1', name: 'Sữa hộp', quantity: 2, factor: 1,
        price: 100, priceText: '100', exactPrice: 100, exactAmount: 200,
        taxId: 1, taxRate: 8, allocation: 0, previousPrice: null,
        allowZero: false, catalog: false, loss: false, attention: false,
        ...overrides
    };
}

function receipt(overrides = {}) {
    return {
        readOnly: false, lines: [goods()], supplierId: 1,
        supplierState: {}, draftState: {}, pendingOutside: false,
        unresolved: 0, hasCatalog: false, overdelivery: false,
        hasVat: false, includeVat: false,
        hasFreight: false, freight: 0, freightPayee: '', capitalizeFreight: false,
        merchandisePaid: false, freightPaid: false, priceReviewUnavailable: false,
        ...overrides
    };
}

const issueKeys = result => result.issues.map(issue => issue.key);

test('an ordinary complete receipt has no required work and keeps goods separate from debt', () => {
    const result = review.evaluate(receipt());
    assert.deepEqual(result.counts, { blocking: 0, acknowledgements: 0, warnings: 0 });
    assert.deepEqual(result.financial, {
        subtotal: 200, vat: 0, merchandise: 200, freight: 0,
        landed: 200, outstandingMerchandise: 200, outstandingFreight: 0
    });
});

test('exact saved goods allocation survives rounded displayed unit prices', () => {
    const result = review.financial(receipt({
        hasVat: true, includeVat: true,
        lines: [goods({ quantity: 3, price: 3333.33, priceText: '3333.33', exactPrice: 3333.33, exactAmount: 10000 })]
    }));
    assert.equal(result.subtotal, 10000, 'The saved allocation is authoritative; multiplying the displayed unit price would lose one cent.');
    assert.equal(result.vat, 800);
    assert.equal(result.merchandise, 10800);
    assert.equal(result.landed, 10800);
});

test('changing the unit price stops reusing an old exact amount', () => {
    const result = review.financial(receipt({
        hasVat: true,
        lines: [goods({ quantity: 3, price: 3333.34, priceText: '3333.34', exactPrice: 3333.33, exactAmount: 10000 })]
    }));
    assert.equal(result.subtotal, 10000.02);
    assert.equal(result.vat, 800);
    assert.equal(result.merchandise, 10800.02);
});

test('fractional quantity rounds half cents like the decimal purchase policy', () => {
    const result = review.financial(receipt({
        lines: [goods({ quantity: 2.5, price: 4.03, priceText: '4.03', exactPrice: NaN, exactAmount: NaN })]
    }));
    assert.equal(result.subtotal, 10.08, '2.5 × 4.03 = 10.075, which must round away from zero to 10.08.');
});

test('VAT is rounded per goods line before the receipt total is summed', () => {
    const result = review.financial(receipt({
        hasVat: true,
        lines: [goods({ quantity: 1, price: 0.06, priceText: '0.06', exactPrice: 0.06, exactAmount: 0.06 }),
            goods({ id: '12', lineNo: '2', quantity: 1, price: 0.06, priceText: '0.06', exactPrice: 0.06, exactAmount: 0.06 })]
    }));
    assert.equal(result.subtotal, 0.12);
    assert.equal(result.vat, 0, 'Each 0.0048 VAT line rounds to zero, unlike applying VAT to the summed subtotal.');
    assert.equal(result.merchandise, 0.12);
});

test('VAT and freight capitalization affect inventory value independently of supplier payment', () => {
    for (const includeVat of [false, true]) {
        for (const capitalizeFreight of [false, true]) {
            const result = review.financial(receipt({
                hasVat: true, includeVat, hasFreight: true, freight: 30,
                freightPayee: 'Đơn vị vận chuyển', capitalizeFreight
            }));
            assert.equal(result.merchandise, 216);
            assert.equal(result.landed, 200 + (includeVat ? 16 : 0) + (capitalizeFreight ? 30 : 0));
            assert.equal(result.outstandingMerchandise, 216);
            assert.equal(result.outstandingFreight, 30);
        }
    }
});

test('approval cannot clear either payable using a stale client paid flag', () => {
    for (const merchandisePaid of [false, true]) {
        for (const freightPaid of [false, true]) {
            const result = review.financial(receipt({
                hasVat: true, includeVat: true, hasFreight: true, freight: 30,
                capitalizeFreight: true, merchandisePaid, freightPaid
            }));
            assert.equal(result.landed, 246);
            assert.equal(result.merchandise, 216);
            assert.equal(result.freight, 30);
            assert.equal(result.outstandingMerchandise, 216);
            assert.equal(result.outstandingFreight, 30);
        }
    }
});

test('disabled VAT and freight ignore stale tax, capitalization, fee and paid checkbox values', () => {
    const result = review.financial(receipt({
        hasVat: false, includeVat: true, hasFreight: false, freight: 99999,
        capitalizeFreight: true, merchandisePaid: false, freightPaid: false
    }));
    assert.deepEqual(result, { subtotal: 200, vat: 0, merchandise: 200, freight: 0, landed: 200, outstandingMerchandise: 200, outstandingFreight: 0 });
});

test('clearing a price does not display a stale saved amount or NaN totals', () => {
    const result = review.evaluate(receipt({ lines: [goods({ price: NaN, priceText: '' })] }));
    assert.deepEqual(issueKeys(result), ['price-11']);
    assert.equal(result.financial.merchandise, 0);
    assert.equal(result.financial.landed, 0);
    assert.equal(Number.isFinite(result.financial.outstandingMerchandise), true);
});

test('zero goods prices are always blocked under the existing positive-price approval rule', () => {
    const zero = { price: 0, priceText: '0', exactPrice: 0, exactAmount: 0 };
    assert.deepEqual(issueKeys(review.evaluate(receipt({ lines: [goods(zero)] }))), ['price-11']);
    const allocated = review.evaluate(receipt({ lines: [goods({ ...zero, allowZero: true })] }));
    assert.deepEqual(allocated.blocking.map(issue => issue.key), ['price-11'],
        'A stale client allocation flag must not bypass the authoritative positive-price rule.');
    assert.equal(allocated.financial.subtotal, 0);
});

test('nonfinite, negative and out-of-storage-range prices are blocked at the affected row', () => {
    for (const price of [NaN, Infinity, -Infinity, -0.01, 100000000000000]) {
        const result = review.evaluate(receipt({ lines: [goods({ price, priceText: String(price) })] }));
        const issue = result.blocking.find(item => item.key === 'price-11');
        assert.ok(issue, `Price ${price} must be blocked.`);
        assert.equal(issue.lineId, '11');
        assert.equal(issue.selector, '.commercial-unit-price');
    }
});

test('a zero price needs correction before it can become a historical-price acknowledgement', () => {
    const invalid = goods({
        price: 0, priceText: '0', exactPrice: 0, exactAmount: 0,
        allowZero: true, previousPrice: 100
    });
    const result = review.evaluate(receipt({ lines: [invalid] }));
    assert.deepEqual(result.blocking.map(issue => issue.key), ['price-11']);
    assert.equal(result.acknowledgements.length, 0, 'An invalid zero cannot be approved by accepting a variance.');
    const corrected = review.evaluate(receipt({ lines: [{ ...invalid, price: 80, priceText: '80' }] }));
    assert.equal(corrected.blocking.length, 0);
    assert.deepEqual(corrected.acknowledgements.map(issue => issue.key), ['variance-11']);
});

test('zero historical price has no comparable purchase history and creates no acknowledgement', () => {
    const result = review.evaluate(receipt({ lines: [goods({ previousPrice: 0 })] }));
    assert.equal(result.acknowledgements.length, 0, 'Backend price-variance policy ignores missing or nonpositive history.');
});

test('missing VAT selection blocks even with a zero rate; a configured zero VAT option is accepted', () => {
    const missing = review.evaluate(receipt({ hasVat: true, lines: [goods({ taxId: 0, taxRate: 0 })] }));
    assert.deepEqual(issueKeys(missing), ['tax-11']);
    const configuredZero = review.evaluate(receipt({ hasVat: true, lines: [goods({ taxId: 5, taxRate: 0 })] }));
    assert.equal(configuredZero.blocking.length, 0);
    assert.equal(configuredZero.financial.vat, 0);
    const vatDisabled = review.evaluate(receipt({ hasVat: false, lines: [goods({ taxId: NaN, taxRate: 8 })] }));
    assert.equal(vatDisabled.blocking.length, 0);
});

test('freight requires a positive total and a payee even if it has already been paid', () => {
    const result = review.evaluate(receipt({ hasFreight: true, freight: 0, freightPayee: '', freightPaid: true }));
    assert.deepEqual(result.blocking.map(issue => issue.key), ['freight-total', 'freight-payee']);
});

test('capitalized freight must have nonnegative finite allocations and balance to the cent', () => {
    const configured = { hasFreight: true, freight: 20, freightPayee: 'Đơn vị vận chuyển', capitalizeFreight: true };
    const balanced = review.evaluate(receipt({ ...configured, lines: [goods({ allocation: 20 })] }));
    assert.equal(balanced.blocking.length, 0);
    const missingCent = review.evaluate(receipt({ ...configured, lines: [goods({ allocation: 19.99 })] }));
    assert.deepEqual(issueKeys(missingCent), ['freight-allocation']);
    const invalid = review.evaluate(receipt({ ...configured, lines: [goods({ allocation: NaN })] }));
    assert.deepEqual(issueKeys(invalid), ['allocation-11', 'freight-allocation']);
    const negative = review.evaluate(receipt({ ...configured, lines: [goods({ allocation: -1 })] }));
    assert.deepEqual(issueKeys(negative), ['allocation-11', 'freight-allocation']);
});

test('freight balance sums each rounded line exactly as the approval payload does', () => {
    const configured = {
        hasFreight: true, freightPayee: 'Đơn vị vận chuyển', capitalizeFreight: true,
        lines: [goods({ allocation: 0.005 }), goods({ id: '12', lineNo: '2', allocation: 0.005 })]
    };
    const unbalanced = review.evaluate(receipt({ ...configured, freight: 0.01 }));
    assert.deepEqual(unbalanced.blocking.map(issue => issue.key), ['freight-allocation'],
        'Two half-cent inputs become two one-cent payload allocations, so a one-cent total is not balanced.');
    const balanced = review.evaluate(receipt({ ...configured, freight: 0.02 }));
    assert.equal(balanced.blocking.length, 0, 'The two rounded allocations balance a two-cent freight total.');
    assert.equal(balanced.financial.freight, 0.02);
});

test('freight totals and capitalized allocations respect the stored money maximum', () => {
    const maximum = 99999999999999.99;
    const configured = { hasFreight: true, freightPayee: 'Đơn vị vận chuyển', capitalizeFreight: false };
    const boundary = review.evaluate(receipt({ ...configured, freight: maximum }));
    assert.equal(boundary.blocking.length, 0, 'The configured storage boundary itself remains valid.');
    for (const freight of [100000000000000, Infinity, NaN]) {
        const result = review.evaluate(receipt({ ...configured, freight }));
        assert.ok(result.blocking.some(issue => issue.key === 'freight-total'), `Freight ${freight} must be blocked before approval.`);
    }
    const allocation = review.evaluate(receipt({
        ...configured, capitalizeFreight: true, freight: maximum,
        lines: [goods({ allocation: 100000000000000 })]
    }));
    assert.ok(allocation.blocking.some(issue => issue.key === 'allocation-11'), 'An out-of-range allocation must identify its exact row.');
});

test('freight recorded outside inventory does not require stale per-line allocations', () => {
    const result = review.evaluate(receipt({
        hasFreight: true, freight: 20, freightPayee: 'Đơn vị vận chuyển', capitalizeFreight: false,
        lines: [goods({ allocation: NaN })]
    }));
    assert.equal(result.blocking.length, 0);
    assert.equal(result.financial.landed, 200);
    assert.equal(result.financial.outstandingFreight, 20);
});

test('an already paid receipt still needs a supplier', () => {
    const result = review.evaluate(receipt({ supplierId: 0, merchandisePaid: true }));
    assert.deepEqual(issueKeys(result), ['supplier']);
});

test('an open staff revision request blocks approval and points to explicit resolution controls', () => {
    const result = review.evaluate(receipt({ hasRevisionRequest: true }));
    assert.equal(result.blocking.length, 1);
    assert.equal(result.blocking[0].key, 'revision-request');
    assert.equal(result.blocking[0].targetId, 'receiptRevisionRequest');
    assert.match(result.blocking[0].detail, /có lý do/);
    assert.equal(review.evaluate(receipt({ readOnly: true, hasRevisionRequest: true })).blocking.length, 0);
});

test('supplier save and price draft failures remain required work with recoverable field targets', () => {
    const result = review.evaluate(receipt({ supplierState: { supplierSaveFailed: true }, draftState: { error: 'Mất kết nối' } }));
    assert.deepEqual(result.blocking.map(issue => [issue.key, issue.targetId]), [
        ['supplier-save', 'commercialSupplierId'], ['price-save', 'receiptPriceDraftSave']
    ]);
    assert.equal(result.blocking[1].detail, 'Mất kết nối');
    const saving = review.evaluate(receipt({ supplierState: { supplierSaving: true } }));
    assert.match(saving.blocking[0].title, /Đang lưu/);
});

test('optional selling price warnings never become required receipt approval steps', () => {
    const result = review.evaluate(receipt({
        priceReviewUnavailable: true,
        lines: [goods({ loss: true }), goods({ id: '12', lineNo: '2', attention: true })]
    }));
    assert.equal(result.blocking.length, 0);
    assert.equal(result.acknowledgements.length, 0);
    assert.deepEqual(result.warnings.map(issue => issue.key), ['loss-11', 'selling-12', 'selling-unavailable']);
});

test('changed historical price and overdelivery are acknowledgements, ordered after hard blockers and before optional warnings', () => {
    const result = review.evaluate(receipt({
        supplierId: 0, overdelivery: true,
        lines: [goods({ previousPrice: 90, loss: true })]
    }));
    assert.deepEqual(issueKeys(result), ['supplier', 'variance-11', 'overdelivery', 'loss-11']);
    assert.deepEqual(result.counts, { blocking: 1, acknowledgements: 2, warnings: 1 });
});

test('numeric-equivalent historical prices do not trigger acknowledgement and row identity stays stable', () => {
    const input = receipt({ lines: [goods({ previousPrice: 100.000 })] });
    const before = JSON.stringify(input);
    const result = review.evaluate(input);
    assert.equal(result.acknowledgements.length, 0);
    assert.equal(JSON.stringify(input), before, 'Readiness must not change prices, quantities or row order.');
});

test('missing goods, unresolved catalog and undecided outside-PO goods remain distinct requirements', () => {
    const result = review.evaluate(receipt({ lines: [], pendingOutside: true, unresolved: 2, hasCatalog: true }));
    assert.deepEqual(issueKeys(result), ['pending-outside', 'provisional', 'catalog', 'no-lines']);
    assert.match(result.blocking[1].title, /^2 /);
});

test('invalid or in-flight physical quantity cannot be represented as ready', () => {
    for (const quantity of [0, -1, NaN, Infinity]) {
        const result = review.evaluate(receipt({ lines: [goods({ quantity })] }));
        assert.ok(issueKeys(result).includes('quantity-11'), `Quantity ${quantity} must be blocked.`);
    }
    for (const factor of [0, -1, NaN, Infinity]) {
        const result = review.evaluate(receipt({ lines: [goods({ factor })] }));
        assert.ok(issueKeys(result).includes('quantity-11'), `Factor ${factor} must be blocked.`);
    }
    const saving = review.evaluate(receipt({ lines: [goods({ savingQuantity: true })] }));
    assert.match(saving.blocking[0].title, /Đang lưu số lượng/);
});

test('confirmed receipts retain financial facts without displaying obsolete approval tasks', () => {
    const result = review.evaluate(receipt({
        readOnly: true, supplierId: 0, pendingOutside: true, unresolved: 2, hasCatalog: true,
        overdelivery: true, priceReviewUnavailable: true, hasVat: true,
        lines: [goods({ previousPrice: 90, loss: true })]
    }));
    assert.equal(result.issues.length, 0);
    assert.equal(result.financial.merchandise, 216);
});

test('search normalization matches Vietnamese names and barcodes without losing meaningful tokens', () => {
    assert.equal(review.normalize('  ĐƯỜNG Trắng — Sữa TƯƠI  '), 'duong trang — sua tuoi');
    assert.equal(review.normalize('Đvt: HỘP 8938505974190'), 'dvt: hop 8938505974190');
    assert.equal(review.normalize(null), '');
    assert.equal(review.normalize(undefined), '');
});

function workspaceDom(options = {}) {
    const byId = new Map(), handlers = new Map();
    let focused, activeTab;
    function element(tag = 'div', properties = {}) {
        const classes = new Set((properties.className || '').split(/\s+/).filter(Boolean));
        return {
            tagName: tag.toUpperCase(), dataset: {}, children: [], attributes: {},
            value: '', checked: false, hidden: false, isConnected: true,
            textContent: '', ...properties,
            classList: {
                contains: name => classes.has(name),
                toggle(name, force) { if (force ?? !classes.has(name)) classes.add(name); else classes.delete(name); }
            },
            append(...nodes) {
                for (const node of nodes) {
                    if (node.parentElement) node.parentElement.children = node.parentElement.children.filter(child => child !== node);
                    node.parentElement = this; this.children.push(node);
                }
            },
            setAttribute(name, value) { this.attributes[name] = String(value); },
            querySelector() { return null; }, querySelectorAll() { return []; },
            closest(selector) {
                if (selector === '.commercial-line' && classes.has('commercial-line') ||
                    selector === '.modal' && classes.has('modal') ||
                    selector === '[data-workbench-panel]' && this.dataset.workbenchPanel ||
                    selector === '[data-line-id]' && this.dataset.lineId) return this;
                return this.parentElement?.closest(selector) || null;
            },
            contains(node) { return node === this || this.children.some(child => child.contains(node)); },
            matches(selector) {
                if (selector === 'input[type="number"]') return tag === 'input' && this.type === 'number';
                return ['input', 'select', 'textarea', 'button', 'a'].includes(tag) || this.attributes.tabindex != null;
            },
            scrollIntoView(value) { this.scrolled = value; },
            focus() { focused = this; }, select() { this.selected = true; }
        };
    }
    function identified(id, tag = 'div', properties = {}) {
        const node = element(tag, { id, ...properties }); byId.set(id, node); return node;
    }
    const workbench = identified('commercialApprovalWorkbench', 'section', {
        className: 'sd-review-workspace', dataset: { readonly: 'false' }
    });
    const panel = element('div', { dataset: { workbenchPanel: 'goods' } }); workbench.append(panel);
    const rowModels = options.lines || [goods()];
    const rows = rowModels.map(line => {
        const row = element('tr', {
            className: 'commercial-line',
            dataset: {
                lineId: String(line.id), lineNo: String(line.lineNo), productName: line.name,
                search: line.name, quantity: String(line.quantity), factor: String(line.factor),
                exactUnitPrice: String(line.exactPrice), exactBeforeVat: String(line.exactAmount),
                allowZeroPrice: String(line.allowZero), revisionChanged: 'false', catalogReview: 'false'
            }
        });
        const price = element('input', { type: 'number', value: line.priceText });
        const quantity = element('input', { type: 'number', value: String(line.quantity) });
        const tax = element('select', { value: String(line.taxId), selectedOptions: [{ dataset: { rate: String(line.taxRate) } }] });
        const allocation = element('input', { type: 'number', value: String(line.allocation) });
        const old = element('span', { dataset: { value: line.previousPrice == null ? '' : String(line.previousPrice) } });
        row.fields = { price, quantity, tax, allocation };
        row.querySelector = selector => ({ '.commercial-unit-price': price, '.commercial-quantity': quantity,
            '.commercial-tax': tax, '.commercial-freight-allocation': allocation, '.commercial-last-price': old })[selector] || null;
        row.append(price, quantity, tax, allocation, old); panel.append(row); return row;
    });
    const supplier = identified('commercialSupplierId', 'select', { value: '1' });
    const search = identified('commercialLineSearch', 'input', { value: '' });
    const vat = identified('commercialHasVat', 'input', { type: 'checkbox', checked: true });
    const includeVat = identified('includeVatInInventoryCost', 'input', { type: 'checkbox', checked: true });
    const paid = identified('commercialMerchandisePaid', 'input', { type: 'checkbox', checked: true });
    const settings = [vat, includeVat, paid, ...rows.flatMap(row => [row.fields.tax, row.fields.allocation])];
    workbench.append(supplier, search, vat, includeVat, paid);
    identified('commercialApproveButton', 'button'); identified('commercialVisibleCount');
    identified('commercialFilterEmpty'); identified('commercialFilterEmptyMessage');
    identified('receiptApprovalReadinessTitle');
    workbench.querySelectorAll = selector => selector === '[data-commercial-filter]' ? [] : settings;
    const target = identified('receiptReviewOtherActions');
    const source = element('div'), toolbar = identified('receiptDocumentActions'), labels = element('div', { dataset: { labelReceipt: '1' } }), modal = element('div', { className: 'modal' });
    source.append(toolbar, labels, modal);
    const document = {
        readyState: 'loading', getElementById: id => byId.get(id) || null,
        querySelectorAll: selector => selector === '#commercialApprovalWorkbench .commercial-line' ? rows :
            selector.startsWith('[data-receipt-review-actions-source]') ? source.children.filter(child => child === toolbar || child === labels) : [],
        querySelector: () => null,
        addEventListener(name, handler) { if (!handlers.has(name)) handlers.set(name, []); handlers.get(name).push(handler); },
        createElement: tag => element(tag), createDocumentFragment: () => element('fragment')
    };
    const window = {
        document, addEventListener() {}, matchMedia: () => ({ matches: false, addEventListener() {} }),
        activateStockDocumentWorkbenchTab: value => { activeTab = value; },
        GaoReceiptPriceDrafts: { getState: () => ({}) }
    };
    const context = vm.createContext({ window, document });
    vm.runInContext(fs.readFileSync(path.resolve(__dirname, '../GaoApp.Web/wwwroot/Admin/js/receipt-approval-workspace.js'), 'utf8'), context);
    window.GaoReceiptApprovalWorkspace.init();
    return {
        api: window.GaoReceiptApprovalWorkspace, byId, rows, workbench, source, target, toolbar, labels, modal,
        element, identified, window, settings, search,
        replaceWorkbench() {
            // refreshAfterIntake receives a new Razor section, restores the current field
            // values and initializes it; the new server-rendered rows start unfiltered.
            const fresh = identified('commercialApprovalWorkbench', 'section', {
                className: 'sd-review-workspace', dataset: { ...workbench.dataset }
            });
            fresh.querySelectorAll = workbench.querySelectorAll;
            fresh.append(...[...workbench.children]);
            workbench.isConnected = false;
            for (const row of rows) { row.hidden = false; row.classList.toggle('d-none', false); }
            window.GaoReceiptApprovalWorkspace.init();
            return fresh;
        },
        emit(name, target) { for (const handler of handlers.get(name) || []) handler({ target }); },
        focused: () => focused, activeTab: () => activeTab
    };
}

test('workspace initialization moves only toolbars into the collapsed menu and preserves price, VAT and payment choices', () => {
    const dom = workspaceDom();
    const before = dom.settings.map(input => [input.value, input.checked]);
    dom.api.init(); dom.api.refresh();
    assert.deepEqual(dom.target.children, [dom.toolbar, dom.labels]);
    assert.deepEqual(dom.source.children, [dom.modal], 'Bootstrap dialogs stay outside the collapsed action panel.');
    assert.equal(dom.rows[0].fields.price.value, '100');
    assert.deepEqual(dom.settings.map(input => [input.value, input.checked]), before);
    assert.equal(dom.byId.get('commercialApproveButton').disabled, false);
});

test('workspace filters and Vietnamese search preserve row order and the input being edited', () => {
    const dom = workspaceDom({ lines: [goods({ id: '11', name: 'Sữa tươi', price: NaN, priceText: '' }), goods({ id: '12', name: 'Bánh gạo' })] });
    const visible = () => dom.rows.filter(row => !row.hidden).map(row => row.dataset.lineId);
    dom.api.setFilter('price'); assert.deepEqual(visible(), ['11']);
    dom.rows[0].fields.price.value = '120'; dom.emit('input', dom.rows[0].fields.price);
    assert.deepEqual(visible(), ['11'], 'Validation updates keep the field under the keyboard visible.');
    dom.api.setFilter('price'); assert.deepEqual(visible(), []);
    dom.api.setFilter('all'); dom.search.value = 'sua TUOI'; dom.emit('input', dom.search);
    assert.deepEqual(visible(), ['11']);
    dom.api.setFilter('all', true);
    assert.deepEqual(visible(), ['11', '12']);
    assert.equal(dom.search.value, '');
    assert.equal(dom.rows[0].fields.price.value, '120');
    assert.deepEqual(dom.rows.map(row => row.dataset.lineId), ['11', '12']);
});

test('replacing the workbench after intake reapplies the selected filter and preserved search', () => {
    const dom = workspaceDom({ lines: [goods({ id: '11', name: 'Sữa tươi có sửa' }),
        goods({ id: '12', name: 'Sữa tươi giữ nguyên' }), goods({ id: '13', name: 'Bánh gạo có sửa' })] });
    dom.rows[0].dataset.revisionChanged = 'true'; dom.rows[2].dataset.revisionChanged = 'true';
    const visible = () => dom.rows.filter(row => !row.hidden).map(row => row.dataset.lineId);
    dom.api.setFilter('revision'); dom.search.value = 'sua tuoi'; dom.emit('input', dom.search);
    assert.deepEqual(visible(), ['11'], 'Search intersects the selected revised-goods filter.');
    const prices = dom.rows.map(row => row.fields.price.value);
    dom.replaceWorkbench();
    const state = dom.api.getState();
    assert.equal(state.filter, 'revision'); assert.equal(state.search, 'sua tuoi');
    assert.deepEqual(visible(), ['11'], 'Fresh server rows must not silently bypass the still-visible search/filter.');
    assert.equal(state.visibleCount, 1);
    assert.equal(dom.byId.get('commercialVisibleCount').textContent, '1 / 3 dòng');
    assert.deepEqual(dom.rows.map(row => row.fields.price.value), prices);
    assert.deepEqual(dom.rows.map(row => row.dataset.lineId), ['11', '12', '13']);
});

test('revealing a row issue clears obstructing filters, activates the correct tab and focuses its exact input', () => {
    const dom = workspaceDom({ lines: [goods({ price: NaN, priceText: '' })] });
    dom.search.value = 'không tồn tại'; dom.api.setFilter('revision');
    assert.equal(dom.rows[0].hidden, true);
    assert.equal(dom.api.revealIssue('price-11'), true);
    assert.equal(dom.search.value, '');
    assert.equal(dom.rows[0].hidden, false);
    assert.equal(dom.activeTab(), 'goods');
    assert.equal(dom.focused(), dom.rows[0].fields.price);
    assert.equal(dom.rows[0].fields.price.selected, true);
    assert.equal(dom.rows[0].fields.price.attributes['aria-invalid'], 'true');
});

test('revealing optional support opens its collapsed ancestor without changing goods values', () => {
    const dom = workspaceDom();
    const details = dom.element('details', { open: false });
    const retry = dom.identified('refreshPriceReviews', 'button'); details.append(retry);
    dom.window.GaoReceiptPriceReviews = { getState: () => ({ unavailable: true }) };
    assert.equal(dom.api.revealIssue('selling-unavailable'), true);
    assert.equal(details.open, true);
    assert.equal(dom.focused(), retry);
    assert.equal(dom.rows[0].fields.price.value, '100');
});

test('revealing unresolved catalog work opens a details target itself and focuses it', () => {
    const dom = workspaceDom();
    dom.workbench.dataset.unresolvedCount = '1';
    const pending = dom.identified('receiptIntakePending', 'details', { open: false });
    dom.api.refresh();
    assert.ok(dom.api.getState().blocking.some(issue => issue.key === 'provisional'));
    assert.equal(dom.api.revealIssue('provisional'), true);
    assert.equal(pending.open, true, 'The target itself, as well as any enclosing disclosures, must open.');
    assert.equal(dom.focused(), pending);
    assert.equal(pending.attributes.tabindex, '-1');
    assert.equal(dom.rows[0].fields.price.value, '100');
});

function approvalSubmitHarness({ blocking = false, overdeliveryAccepted = true } = {}) {
    const source = fs.readFileSync(path.resolve(__dirname, '../GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js'), 'utf8');
    const submit = source.slice(source.indexOf('async function submitCommercialApproval()'), source.indexOf('function buildCommercialPayload()'));
    let hidden = 0, focused = false, requests = 0;
    const checkbox = { checked: false, focus() { focused = true; } }, message = { textContent: '' };
    const values = { btnApprove: { disabled: false }, acceptPriceVariance: checkbox,
        acceptOverdelivery: { checked: overdeliveryAccepted }, approveMessage: message };
    const context = vm.createContext({
        document: { getElementById: id => values[id] || null },
        window: { GaoReceiptApprovalWorkspace: { getState: () => ({ blocking: blocking ? [{}] : [] }) } },
        validateCommercialApproval: () => false, getPriceVariances: () => [{}],
        hideApproveModal: () => hidden++, fetch: async () => requests++
    });
    vm.runInContext(submit, context);
    return { run: () => vm.runInContext('submitCommercialApproval()', context),
        hidden: () => hidden, focused: () => focused, requests: () => requests, message };
}

test('missing price-variance acknowledgement keeps the approval dialog open and focuses the required checkbox', async () => {
    const h = approvalSubmitHarness(); await h.run();
    assert.equal(h.hidden(), 0);
    assert.equal(h.focused(), true);
    assert.match(h.message.textContent, /xác nhận.*chênh lệch giá nhập/);
    assert.equal(h.requests(), 0);
});

test('a new hard blocker returns from approval confirmation to the workbench without sending approval', async () => {
    const h = approvalSubmitHarness({ blocking: true }); await h.run();
    assert.equal(h.hidden(), 1);
    assert.equal(h.requests(), 0);
});
