const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/printing/receipt.print-page.js'), 'utf8');
const tick = () => new Promise(setImmediate);

function fixture(mode, { reportFails = false, fail = false, immediate = true, errorDiagnostic, receiptPrinted = false, failCount = Infinity } = {}) {
    const receipt = { orderId: 123, status: 'Completed', payments: [{ method: 'Cash', amount: 100 }] };
    const setup = { receipt, autoPrint: true };
    const elements = new Map(), sent = [], sentModes = [], closes = [], reports = [], savedPreferences = [];
    let preferences = { mode, printer: 'TEST', copies: 2, color: false };
    const node = id => {
        if (!elements.has(id)) {
            const handlers = new Map();
            elements.set(id, { textContent: '', style: {}, hidden: true, handlers,
                addEventListener(type, callback) {
                    if (!handlers.has(type)) handlers.set(type, []);
                    handlers.get(type).push(callback);
                } });
        }
        return elements.get(id);
    };
    node('receiptPrintData').textContent = JSON.stringify(setup);
    Object.assign(node('receiptPaper'), { contentWindow: {}, contentDocument: { body: { scrollHeight: 100 } } });
    const window = { location: { hash: immediate ? '#pos-payment-print=' + 'a'.repeat(36) : '' }, close: () => closes.push(true) };
    window.parent = window;
    window.GaoPrintLifecycle = { close: () => closes.push(true) };
    const PosPrinting = {
        consumeCashDrawer: async () => true, selected: () => ({ name: 'TEST' }), preferences: () => ({ ...preferences }), pageSize() {},
        savePreferences: (context, value) => {
            savedPreferences.push({ context, value: { ...value } });
            preferences = { ...value };
            return { ...preferences };
        },
        send: async (...args) => {
            sent.push(args); sentModes.push(preferences.mode);
            if (fail && sent.length <= failCount) {
                const error = Error('TEST-FAILED');
                if (errorDiagnostic) error.printDiagnostic = { ...(Array.isArray(errorDiagnostic)
                    ? errorDiagnostic[Math.min(sent.length - 1, errorDiagnostic.length - 1)] : errorDiagnostic) };
                if (receiptPrinted) error.receiptPrinted = true;
                throw error;
            }
            return { mode: preferences.mode, printer: 'TEST' };
        },
        reportPaymentPrint: (...args) => { reports.push(args); if (reportFails) throw Error('Report unavailable'); }
    };
    vm.runInNewContext(source, { window, document: { getElementById: node, addEventListener() {} }, PosPrinting,
        ReceiptTemplates: { render: () => ({ size: { width: 80 }, html: '<main>TEST</main>' }) },
        GaoPrintLifecycle: window.GaoPrintLifecycle, navigator: { userAgent: 'Receipt close unit test' } });
    node('receiptPaper').onload();
    const click = id => {
        const element = node(id);
        for (const handler of element.handlers.get('click') || []) handler({ currentTarget: element });
    };
    return { sent, sentModes, closes, reports, savedPreferences, node, click };
}

for (const mode of ['helper', 'qz']) test(`${mode} acknowledgement closes the tab without waiting for a background completion timer`, async () => {
    const h = fixture(mode); await tick();
    assert.equal(h.sent.length, 1); assert.equal(h.sent[0][4], true);
    assert.equal(h.closes.length, 1);
    h.sent[0][3]();
    assert.equal(h.closes.length, 1, 'A late completion callback must not close a second time');
});

test('failed POS result reporting cannot prevent a successfully printed receipt from closing', async () => {
    const h = fixture('helper', { reportFails: true }); await tick();
    assert.equal(h.closes.length, 1); assert.equal(h.sent.length, 1);
});

test('browser printing keeps the tab open until the print dialog signals completion', async () => {
    const h = fixture('browser'); await tick();
    assert.equal(h.closes.length, 0);
    h.sent[0][3](); assert.equal(h.closes.length, 1);
});

test('automatic checkout failure closes once while a manual history print failure remains available', async () => {
    const automatic = fixture('helper', { fail: true }); await tick();
    assert.equal(automatic.closes.length, 1); assert.equal(automatic.sent.length, 1);
    const history = fixture('helper', { fail: true, immediate: false }); await tick();
    assert.equal(history.closes.length, 0); assert.match(history.node('printFeedback').textContent, /TEST-FAILED/);
});

test('a failed helper health check offers browser printing without automatically sending another print', async () => {
    const h = fixture('helper', { fail: true, failCount: 1, immediate: false,
        errorDiagnostic: { endpoint: '/health', method: 'GET', stage: 'network', httpStatus: null } });
    await tick();
    assert.equal(h.sent.length, 1);
    assert.equal(h.savedPreferences.length, 0);
    assert.equal(h.node('useBrowserPrint').hidden, false);
    assert.match(h.node('printFeedback').textContent, /trình duyệt|Chrome/i);
    assert.equal(h.closes.length, 0, 'A history receipt must remain available for an explicit recovery choice');

    h.click('useBrowserPrint');
    h.click('useBrowserPrint');
    await tick();
    assert.deepEqual(h.sentModes, ['helper', 'browser']);
    assert.equal(h.savedPreferences.length, 1, 'Repeated clicks cannot save or submit the native recovery twice');
    assert.equal(h.savedPreferences[0].context.receipt.orderId, 123);
    assert.equal(h.savedPreferences[0].value.mode, 'browser');
    assert.equal(h.savedPreferences[0].value.copies, 2, 'Switching transport preserves the local print preferences');
    assert.equal(h.savedPreferences[0].value.printer, 'TEST');
    assert.equal(h.node('useBrowserPrint').hidden, true);
    assert.equal(h.closes.length, 0, 'Opening the native dialog does not itself complete printing');
    h.sent[1][3]();
    assert.equal(h.closes.length, 1);
    h.sent[1][3]();
    assert.equal(h.closes.length, 1, 'Native print completion remains single-use');
});

test('a helper GET health failure still closes an automatic checkout receipt once', async () => {
    const h = fixture('helper', { fail: true,
        errorDiagnostic: { endpoint: '/health', method: 'GET', stage: 'network', httpStatus: null } });
    await tick();
    assert.equal(h.sent.length, 1);
    assert.equal(h.savedPreferences.length, 0);
    assert.equal(h.closes.length, 1);
});

for (const diagnostic of [
    { endpoint: '/print', method: 'POST', stage: 'network' },
    { endpoint: '/drawer', method: 'POST', stage: 'drawer-ack' },
    { endpoint: '/health', method: 'POST', stage: 'network' },
    { endpoint: '/print', method: 'GET', stage: 'network' }
]) test(`${diagnostic.method} ${diagnostic.endpoint} failure cannot offer browser recovery`, async () => {
    const h = fixture('helper', { fail: true, immediate: false, errorDiagnostic: diagnostic });
    await tick();
    assert.equal(h.node('useBrowserPrint').hidden, true);
    h.click('useBrowserPrint');
    await tick();
    assert.equal(h.sent.length, 1, 'An ineligible action cannot submit another print');
    assert.equal(h.savedPreferences.length, 0, 'An uncertain device write must keep the selected transport');
    assert.equal(h.closes.length, 0);
});

test('a later failed health check cannot offer browser printing after an uncertain POST in the same receipt', async () => {
    const h = fixture('helper', { fail: true, immediate: false, errorDiagnostic: [
        { endpoint: '/print', method: 'POST', stage: 'network' },
        { endpoint: '/health', method: 'GET', stage: 'network' }
    ] });
    await tick();
    h.click('printReceipt');
    await tick();
    assert.equal(h.sent.length, 2);
    assert.equal(h.node('useBrowserPrint').hidden, true);
    h.click('useBrowserPrint');
    await tick();
    assert.deepEqual(h.sentModes, ['helper', 'helper']);
    assert.equal(h.savedPreferences.length, 0);
});

test('a drawer health failure after the bill was printed cannot offer another paper print', async () => {
    const h = fixture('helper', { fail: true, immediate: false, receiptPrinted: true,
        errorDiagnostic: { endpoint: '/health', method: 'GET', stage: 'network' } });
    await tick();
    assert.equal(h.node('useBrowserPrint').hidden, true);
    h.click('useBrowserPrint');
    await tick();
    assert.equal(h.sent.length, 1);
    assert.equal(h.savedPreferences.length, 0);
});
