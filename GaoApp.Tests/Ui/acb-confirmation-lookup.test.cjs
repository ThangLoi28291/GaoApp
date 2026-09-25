const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/acb.lookup.js'), 'utf8');

class Element {
    constructor(tag = 'div') { this.tag = tag; this.children = []; this.dataset = {}; this.listeners = {}; this.textContent = ''; }
    append(...nodes) { this.children.push(...nodes); }
    replaceChildren(...nodes) { this.children = nodes; }
    createTHead() { const node = new Element('thead'); this.append(node); return node; }
    createTBody() { const node = new Element('tbody'); this.append(node); return node; }
    insertRow() { const node = new Element('tr'); this.append(node); return node; }
    addEventListener(name, action) { this.listeners[name] = action; }
    querySelector() { return { value: 'test-antiforgery' }; }
    set innerHTML(_) { throw new Error('Untrusted audit data must be rendered as text'); }
}
const descendants = node => [node, ...node.children.flatMap(descendants)];
const text = node => descendants(node).map(x => x.textContent).join(' ');
const confirmation = (code, label, extra = {}) => ({ code, label, confirmedAtUtc: '2026-09-09T03:00:00Z', recorded: true, ...extra });
const session = (qrId, audit) => ({ qrId, providerOrderId: `QR-${qrId}`, amount: 1000, status: 'Completed',
    createdAtUtc: '2026-09-09T02:59:00Z', confirmation: audit,
    transactions: [{ transactionNumber: `BANK-${qrId}`, amount: 1000, status: 'COMPLETED', content: 'Thanh toan', postedAt: '09/09/2026' }] });
const payload = (sessions = [], payments = [], otherQrs = []) => ({
    order: { id: 15, grandTotal: 9000, balanceDue: 0 }, sessions, payments, otherQrs
});
async function harness(data) {
    const elements = new Map(['acbLookup', 'acbRefresh', 'acbLookupStatus', 'acbLookupResults', 'acbLookupSummary', 'acbLookupTitle'].map(id => [id, new Element()]));
    elements.get('acbLookup').dataset.orderId = '15';
    const calls = [];
    const context = { document: { getElementById: id => elements.get(id), createElement: tag => new Element(tag) }, window: {},
        fetch: async (url, options) => { calls.push([url, options.method]); return { ok: true, json: async () => data }; } };
    vm.runInNewContext(source, context);
    await new Promise(setImmediate);
    assert.doesNotMatch(elements.get('acbLookupStatus').className, /acb-error/);
    return { elements, calls, all: () => descendants(elements.get('acbLookupResults')),
        refresh: () => elements.get('acbRefresh').listeners.click() };
}

test('invoice ledger and saved QR details show each winning source, time, cashier or callback receipt', async () => {
    const audits = [
        confirmation('ScheduledCheck', 'Tự kiểm tra theo thời gian'),
        confirmation('ManualCheck', 'Thu ngân bấm Kiểm tra ngay', { userId: 7, userName: 'Thu ngân An' }),
        confirmation('Callback', 'Callback ACB · Tức thời', { callbackReceiptId: 42 }),
        confirmation('DailyCallback', 'Callback ACB · Danh sách cuối ngày', { callbackReceiptId: 43 })
    ];
    const h = await harness(payload(audits.map((audit, index) => session(index + 1, audit))));
    assert.ok(h.all().some(x => x.tag === 'th' && x.textContent === 'Phương án xác nhận'));
    for (const audit of audits) {
        const blocks = h.all().filter(x => x.dataset.source === audit.code);
        assert.equal(blocks.length, 2, 'source appears on bank row and QR history');
        for (const block of blocks) {
            assert.ok(text(block).includes(audit.label));
            assert.match(text(block), /Xác nhận:/);
            if (audit.userName) assert.ok(text(block).includes('Người thao tác: ' + audit.userName));
            if (audit.callbackReceiptId) assert.ok(text(block).includes('Biên nhận callback #' + audit.callbackReceiptId));
        }
    }
});

test('refreshing an invoice retains saved confirmation provenance and distinguishes unposted evidence', async () => {
    const data = payload([session(1, confirmation('Callback', 'Callback ACB · Tức thời', { callbackReceiptId: 55, recorded: false }))]);
    const h = await harness(data);
    const before = text(h.elements.get('acbLookupResults'));
    assert.match(before, /Đã xác minh · Chưa ghi khoản thu/);
    data.sessions[0].lastRetrievedAtUtc = '2026-09-09T04:00:00Z';
    await h.refresh();
    const blocks = h.all().filter(x => x.dataset.source === 'Callback');
    assert.equal(blocks.length, 2);
    assert.ok(blocks.every(x => text(x).includes('Biên nhận callback #55')));
    assert.deepEqual(h.calls, [['/admin/acb/payments/orders/15/data', 'GET'], ['/admin/acb/payments/orders/15/refresh', 'POST']]);
});

test('manual confirmation and old unknown source remain distinct; actor names render safely as text', async () => {
    const actor = '<img src=x onerror=alert(1)>';
    const manual = confirmation('ManualQr', 'Thu ngân xác nhận QR thủ công', { userId: 7, userName: actor });
    const h = await harness(payload([session(1, { code: 'Unknown', label: 'Chưa lưu nguồn xác nhận', recorded: true })],
        [{ id: 26, amount: 2000, automatic: false, paidAtUtc: '2026-09-09T03:00:00Z', confirmation: manual }],
        [{ qrId: 2, requestCode: 'MANUAL-2', amount: 2000, status: 'ManualConfirmed', confirmation: manual }]));
    assert.equal(h.all().filter(x => x.dataset.source === 'ManualQr').length, 2);
    const old = h.all().filter(x => x.dataset.source === 'Unknown');
    assert.equal(old.length, 2);
    assert.ok(old.every(x => !text(x).includes('Xác nhận:') && text(x).includes('Chưa lưu nguồn xác nhận')));
    assert.ok(h.all().some(x => x.textContent === 'Người thao tác: ' + actor));
    assert.ok(!h.all().some(x => x.tag === 'img'));
});
