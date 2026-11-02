const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/receipt-invoice-follow-up.js'), 'utf8');

async function page() {
    const elements = new Map();
    const element = id => {
        if (!elements.has(id)) elements.set(id, {
            checked: false, disabled: false, hidden: false, textContent: '', listeners: {},
            addEventListener(type, handler) { this.listeners[type] = handler; }
        });
        return elements.get(id);
    };
    let model = { hasLinkedInvoice: true, isConfirmed: false, state: 'NeedsReview', mapId: 41, evidenceFingerprint: 'snapshot-1' };
    let selected = null;
    let fail = false;
    const document = {
        listeners: {}, getElementById: element,
        querySelector(selector) { return selector.includes('approvalInvoiceExpectation') ? selected : null; },
        addEventListener(type, handler) { this.listeners[type] = handler; }
    };
    const window = { stockDocumentPage: { documentId: 4472 } };
    vm.runInNewContext(source, {
        window, document, console,
        fetch: async (_, options) => {
            assert.equal(options.method, 'GET');
            return { ok: !fail, json: async () => fail ? { message: 'Read failed' } : { ...model } };
        }
    });
    document.listeners.DOMContentLoaded();
    await window.GaoReceiptInvoiceFollowUp.refresh();
    return {
        api: window.GaoReceiptInvoiceFollowUp, element,
        setModel(next) { model = { ...model, ...next }; },
        select(value) { selected = { value }; }, failRead() { fail = true; },
        tick() {
            const checkbox = element('approvalReviewInputInvoice');
            checkbox.checked = true;
            checkbox.listeners.change.call(checkbox);
        }
    };
}

test('XML review is explicit and retained across the final approval refresh', async () => {
    const p = await page();
    assert.equal(await p.api.approvalChoice(), true);
    assert.equal(p.api.approvalReview(), null);
    p.tick();
    assert.equal(await p.api.approvalChoice(), true);
    assert.deepEqual(JSON.parse(JSON.stringify(p.api.approvalReview())), { mapId: 41, evidenceFingerprint: 'snapshot-1' });
});

for (const changed of [{ mapId: 42 }, { evidenceFingerprint: 'snapshot-2' }, { hasLinkedInvoice: false }]) {
    test('changed invoice evidence requires a fresh acknowledgement: ' + JSON.stringify(changed), async () => {
        const p = await page();
        p.tick();
        p.setModel(changed);
        await assert.rejects(p.api.approvalChoice(), /đã thay đổi/);
        assert.equal(p.element('approvalReviewInputInvoice').checked, false);
        assert.equal(p.api.approvalReview(), null);
    });
}

test('a receipt without XML still permits waiting or completing without invoice review', async () => {
    const p = await page();
    p.setModel({ hasLinkedInvoice: false, mapId: null, evidenceFingerprint: null });
    await assert.rejects(p.api.approvalChoice(), /Chọn chờ nhà cung cấp/);
    p.select('waiting');
    assert.equal(await p.api.approvalChoice(), true);
    p.select('notExpected');
    assert.equal(await p.api.approvalChoice(), false);
    assert.equal(p.api.approvalReview(), null);
});

test('a failed refresh cannot submit stale XML review evidence', async () => {
    const p = await page();
    p.tick();
    p.failRead();
    await assert.rejects(p.api.approvalChoice(), /Chưa kiểm tra được hóa đơn/);
    assert.throws(() => p.api.approvalReview(), /tải lại/);
});
