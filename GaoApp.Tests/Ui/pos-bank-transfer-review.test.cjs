const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.bank-transfer-review.js'), 'utf8');
const tick = () => new Promise(setImmediate);
function fixture() {
    const nodes = new Map(), keys = [];
    function el(id) {
        if (!nodes.has(id)) {
            const events = new Map(), classes = new Set();
            nodes.set(id, { id, textContent: '', hidden: true,
                classList: { contains: x => classes.has(x), add: x => classes.add(x), remove: x => classes.delete(x) },
                addEventListener(name, fn) { const handlers = events.get(name) || []; handlers.push(fn); events.set(name, handlers); },
                removeEventListener(name, fn) { events.set(name, (events.get(name) || []).filter(x => x !== fn)); },
                emit(name) { for (const fn of [...events.get(name) || []]) fn(); }, focus() {} });
        }
        return nodes.get(id);
    }
    const window = { bootstrap: { Modal: { getOrCreateInstance: node => ({
        hide() { node.classList.remove('show'); node.emit('hidden.bs.modal'); },
        show() { node.classList.add('show'); node.emit('shown.bs.modal'); }
    }) } } };
    vm.runInNewContext(source, { window, document: { getElementById: el,
        querySelectorAll: () => [...nodes.values()].filter(node => node.classList.contains('show')),
        addEventListener: (name, fn) => { if (name === 'keydown') keys.push(fn); } }, setTimeout, clearTimeout });
    function press(key, repeat = false) { const event = { key, repeat, preventDefault() {}, stopImmediatePropagation() {} }; keys.forEach(fn => fn(event)); }
    return { api: window.PosBankTransferReview, el, press };
}
const sample = { receiptId: 11, waitId: 2, qrId: 5, orderId: 100, amount: 196000,
    senderName: 'DOAN THI CAM DUA', transactionCode: 'GD:12345678', postedAtUtc: '2026-10-14T12:05:38Z', content: 'Thông báo ACB gốc' };

test('review uses the bank sender, safe literal content, and Enter once despite repeated keys or a later suggestion', async () => {
    const h = fixture(); let count = 0, resolve, received;
    const item = { ...sample, senderName: '<img src=x onerror=alert(1)>' };
    await h.api.show(item, { confirm: selected => { count++; received = selected; return new Promise(r => { resolve = r; }); } });
    assert.equal(h.el('bankReviewSender').textContent, item.senderName);
    assert.equal(h.el('bankReviewContent').textContent, item.content);
    assert.equal(h.el('bankReviewAmount').textContent, '196.000 đ');
    h.press('Enter'); h.press('Enter'); h.press('Enter', true);
    await h.api.show({ ...sample, receiptId: 12, senderName: 'Tên khác' }, {});
    assert.equal(count, 1); assert.equal(received.receiptId, 11);
    assert.equal(h.api.active().receiptId, 11);
    resolve(); await tick(); await h.api.close();
});

test('Esc dismisses the bank suggestion and restores the QR only when explicitly requested', async () => {
    const h = fixture(), qr = h.el('paymentQrModal'); qr.classList.add('show');
    let rejected = 0;
    await h.api.show(sample, { reject: async () => { rejected++; await h.api.close(true); } });
    assert.equal(qr.classList.contains('show'), false);
    h.press('Escape'); h.press('Escape'); await tick();
    assert.equal(rejected, 1); assert.equal(qr.classList.contains('show'), true); assert.equal(h.api.active(), null);
});

test('server rejection keeps the selected payment visible and enables an explicit retry', async () => {
    const h = fixture(); let calls = 0;
    await h.api.show(sample, { confirm: async () => { calls++; throw Error('Khoản chờ đã thay đổi'); } });
    h.press('Enter'); await tick();
    assert.equal(h.el('bankReviewError').hidden, false); assert.equal(h.el('bankReviewError').textContent, 'Khoản chờ đã thay đổi');
    assert.equal(h.api.active().receiptId, 11); assert.equal(h.el('btnBankReviewConfirm').disabled, false);
    h.press('Enter'); await tick(); assert.equal(calls, 2); await h.api.close();
});

test('review waits while the cashier is using another modal', async () => {
    const h = fixture(); h.el('invoiceIntentModal').classList.add('show');
    await h.api.show(sample, {}); assert.equal(h.api.active(), null);
    h.el('invoiceIntentModal').classList.remove('show');
    await h.api.show({ ...sample, senderName: null }, {});
    assert.equal(h.el('bankReviewSender').textContent, 'Chưa đọc được tên người chuyển'); await h.api.close();
});
