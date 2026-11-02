const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js'), 'utf8');
const section = (start, end) => source.slice(source.indexOf(start), source.indexOf(end, source.indexOf(start)));
const clone = value => JSON.parse(JSON.stringify(value));

function page({ imported = false, autoApplicable = false, failure = null, remembered = false, refreshFailure = false, rereadReason = null, remoteSaved = false } = {}) {
    const saved = { mappingId: 73, productVariantId: 51, productUnitConversionId: 61, confirmedUnitId: 41,
        confirmedFactor: 1, mappingRowVersion: 'mapping-v1', isAutoApplicable: autoApplicable,
        reasonCode: autoApplicable ? null : 'NameUnitSuggestionRequiresConfirmation' };
    const invoice = { id: 19, details: [{ id: 11, lineNo: 4, itemName: 'Tương ớt Chin-su 500gr', unitName: 'Chai',
        quantity: 10, unitPrice: 100, lineAmount: 1000, itemCatalogMapping: remembered ? clone(saved) : null }] };
    const choice = { productVariantId: 51, unitId: 41, kind: 'buy', touched: true };
    const importedBill = imported ? { billLineKey: 'xml-19-11', productVariantId: 51, billUnitId: 41, billQuantity: 10 } : null;
    const requests = [], messages = [];
    const xmlMessage = { textContent: '' };
    let stored = remembered || remoteSaved;
    const context = {
        console, host: {}, workspace: { receiptRowVersion: 'receipt-v1' }, receiptId: 4464,
        busy: false, writing: false, xmlLoading: false, xmlLoaded: false, xmlGeneration: 1,
        xmlInvoices: [clone(invoice)], xmlChoices: new Map([['xml-19-11', choice]]),
        editable: () => true, normalize: value => value.toLowerCase(),
        products: () => [{ productVariantId: 51, units: [{ unitId: 41, conversionId: 61, name: 'Chai', factor: 1 }] }],
        bill: () => importedBill, updateActions() {}, renderXmlRows() {},
        byId: () => xmlMessage, document: { querySelector: () => ({ value: 'antiforgery' }) },
        window: {}, message: (text, error) => messages.push({ text, error }),
        fetch: async (url, options = {}) => {
            requests.push({ url, ...options });
            if (options.method === 'POST') {
                if (failure) return { ok: false, json: async () => ({ message: failure }) };
                stored = true;
                return { ok: true, json: async () => ({ ...clone(saved), isAutoApplicable: true, reasonCode: null }) };
            }
            if (stored && refreshFailure) return { ok: false, json: async () => ({ message: 'Không tải được XML.' }) };
            const fresh = clone(invoice);
            fresh.details[0].itemCatalogMapping = stored ? clone(saved) : null;
            if (stored && rereadReason) fresh.details[0].itemCatalogMapping.reasonCode = rereadReason;
            return { ok: true, json: async () => [fresh] };
        }
    };
    vm.createContext(context);
    vm.runInContext(
        section('    const xmlKey =', '    function xmlAmount(') +
        section('    async function action(', '    function importLegacy(') +
        section('    async function loadXmlRows(', '    function appendXmlRow(') +
        section('    function xmlDetailSignature(', '    function importXmlRow(') +
        '\nthis.rememberedMatches = typeof xmlMappingRemembered === "function" ? xmlMappingRemembered : xmlMappingMatches; this.autoMatches = xmlMappingMatches;',
        context);
    return {
        context, choice, requests, messages, importedBill, xmlMessage,
        async remember() { const head = context.xmlInvoices[0]; await context.rememberXmlRow(head, head.details[0]); },
        isRemembered() { return context.rememberedMatches(context.xmlInvoices[0].details[0], choice); },
        isAutoApplicable() { return context.autoMatches(context.xmlInvoices[0].details[0], choice); },
        posts() { return requests.filter(request => request.method === 'POST'); }
    };
}

test('remembered name-only mapping stays remembered after the XML list is reloaded', async () => {
    const p = page();
    await p.remember();
    assert.equal(p.posts().length, 1);
    assert.equal(p.isRemembered(), true);
    assert.equal(p.isAutoApplicable(), false, 'A saved name-only suggestion still requires review before automatic use.');
    assert.match(p.xmlMessage.textContent, /Các XML sau vẫn cần kiểm tra/);
    assert.equal(p.posts()[0].headers.RequestVerificationToken, 'antiforgery');
    assert.deepEqual(JSON.parse(p.posts()[0].body), { receiptRowVersion: 'receipt-v1', inputInvoiceHeadId: 19,
        inputInvoiceDetailId: 11, productVariantId: 51, productUnitConversionId: 61, mappingRowVersion: null });
    await p.remember();
    assert.equal(p.posts().length, 1, 'A second click must not save an unchanged remembered match again.');
});

test('remembering after a row was added preserves the existing bill', async () => {
    const p = page({ imported: true });
    const before = clone(p.importedBill);
    await p.remember();
    assert.equal(p.isRemembered(), true);
    assert.deepEqual(p.importedBill, before);
});

test('saved code mappings remain eligible for automatic recognition', () => {
    const p = page({ remembered: true, autoApplicable: true });
    assert.equal(p.isRemembered(), true);
    assert.equal(p.isAutoApplicable(), true);
});

test('a changed conversion or selection does not count as a remembered match', () => {
    const p = page({ remembered: true });
    assert.equal(p.isRemembered(), true);
    p.context.xmlInvoices[0].details[0].itemCatalogMapping.mappingId = null;
    assert.equal(p.isRemembered(), false, 'An unpersisted suggestion must not look saved.');
    p.context.xmlInvoices[0].details[0].itemCatalogMapping.mappingId = 73;
    p.context.xmlInvoices[0].details[0].itemCatalogMapping.reasonCode = 'ConversionSignatureDrift';
    assert.equal(p.isRemembered(), false);
    p.context.xmlInvoices[0].details[0].itemCatalogMapping.reasonCode = 'NameUnitSuggestionRequiresConfirmation';
    p.choice.unitId = 99;
    assert.equal(p.isRemembered(), false);
});

test('a failed save exposes its message on the row and does not claim success', async () => {
    const p = page({ failure: 'Phiếu vừa thay đổi. Vui lòng tải lại.' });
    await p.remember();
    assert.equal(p.choice.rememberError, 'Phiếu vừa thay đổi. Vui lòng tải lại.');
    assert.equal(p.isRemembered(), false);
    assert.equal(p.choice.remembering, false);
    assert.equal(p.messages.some(message => message.error), true);
});

test('the same mapping saved in another tab is recognized without a duplicate POST', async () => {
    const p = page({ remoteSaved: true });
    await p.remember();
    assert.equal(p.isRemembered(), true);
    assert.equal(p.isAutoApplicable(), false);
    assert.equal(p.posts().length, 0);
    assert.equal(p.messages.some(message => message.error), false);
});

test('a failed reload keeps the saved result and exposes the refresh failure on the row', async () => {
    const p = page({ refreshFailure: true });
    await p.remember();
    assert.equal(p.posts().length, 1);
    assert.equal(p.isRemembered(), true, 'The successful POST must not be lost when the follow-up GET fails.');
    assert.match(p.choice.rememberError, /Đã lưu ghép, nhưng chưa tải lại/);
    assert.equal(p.xmlMessage.textContent, 'Không tải được XML.');
});

test('a conflicting mapping on reload cannot display an unchanged success message', async () => {
    const p = page({ rereadReason: 'ConversionSignatureDrift' });
    await p.remember();
    assert.equal(p.isRemembered(), false);
    assert.match(p.choice.rememberError, /Ghép đã thay đổi/);
    assert.equal(p.messages.some(message => message.error), true);
});
