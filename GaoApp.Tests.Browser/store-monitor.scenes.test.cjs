const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const sandbox = { window: {} };
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../GaoApp.Web/wwwroot/Admin/js/store-monitor.scenes.js'), 'utf8'), sandbox);
const models = sandbox.window.GaoStoreScenes;
const event = (action, text = '') => ({ action, text });

test('Partial POS payment does not print a completed bill', () => {
    for (const action of ['AddPayment', 'AddPaymentToCurrentCart', 'AddPaymentAndMaybeFinalizeCurrentCart'])
        assert.equal(models.effect('pos', 'order:1', event('POS.' + action, 'vừa ghi nhận thanh toán'), false), 'input');
    assert.equal(models.effect('pos', 'order:1', event('POS.AddPaymentAndMaybeFinalizeCurrentCart', 'vừa thanh toán và chốt đơn'), false), 'print');
    for (const action of ['Finalize', 'FinalizeCurrentCart', 'FinalizeCredit'])
        assert.equal(models.effect('pos', 'order:1', event('POS.' + action), false), 'print');
});

test('Only added or scanned products move across the POS scanner', () => {
    for (const action of ['AddItem', 'AddByBarcode', 'ScanToCurrentCart'])
        assert.equal(models.effect('pos', 'order:1', event('POS.' + action), false), 'scan');
    for (const action of ['RemoveLine', 'Cancel', 'RefundOrder', 'HoldOrder'])
        assert.equal(models.effect('pos', 'order:1', event('POS.' + action), false), 'input');
});

test('New interaction after a save resumes generic input instead of replaying printing/approval', () => {
    assert.equal(models.effect('pos', 'order:1', event('POS.Finalize'), true), 'input');
    assert.equal(models.effect('review', 'receipt:1', event('StockDocuments.Approve'), true), 'input');
});

test('Count and transfer with the same numeric ID use different physical models and effects', () => {
    const saved = event('StockTransfers.UpdateLine');
    assert.equal(models.kind('warehouse', 'count:42', saved), 'count');
    assert.equal(models.kind('warehouse', 'transfer:42', saved), 'transfer');
    assert.equal(models.effect('warehouse', 'transfer:42', saved, false), 'transfer');
    assert.equal(models.effect('warehouse', 'transfer:42', event('StockTransfers.DeleteLine'), false), 'input');
});

test('Label printing runs for a sent print job, not plans, confirmations or cancellations', () => {
    assert.equal(models.effect('label', 'label:1', event('LabelPrinting.Print'), false), 'print');
    for (const action of ['Plan', 'Confirm', 'Cancel', 'Complete'])
        assert.equal(models.effect('label', 'label:1', event('LabelPrinting.' + action), false), 'input');
    assert.equal(models.kind('label', 'label:1', event('LabelPrinting.Plan')), 'prepare');
});

test('Only approval stamps a document; submission, pricing, rejection and intake drafts do not', () => {
    for (const action of ['Approve', 'ApproveCommercial', 'Confirm'])
        assert.equal(models.effect('review', 'receipt:1', event('StockDocuments.' + action), false), 'approved');
    for (const action of ['SubmitApproval', 'Reject', 'SavePriceDraft', 'SavePricingAllocation'])
        assert.equal(models.effect('review', 'receipt:1', event('StockDocuments.' + action), false), 'input');
    assert.equal(models.effect('review', 'receipt:1', event('ReceiptIntake.Review', 'vừa lưu thông tin chờ duyệt'), false), 'input');
    assert.equal(models.effect('review', 'receipt:1', event('ReceiptIntake.Review', 'vừa duyệt mặt hàng Gạo'), false), 'approved');
});

test('Models escape real document labels and contain neither progress bars nor invented counts', () => {
    for (const module of ['pos', 'receipt', 'warehouse', 'label', 'review']) {
        const html = models.render(module, 'count:1', null, '<img src=x onerror="attack()">', false);
        assert.doesNotMatch(html, /<img|onerror="attack|progress|0\/1|Kho A|Kho B/);
        if (['pos', 'receipt', 'review'].includes(module)) assert.match(html, /&lt;img/);
    }
});

test('Receiving scanner is used for actual captures; document edits keep the sheet and keyboard', () => {
    for (const action of ['StockDocuments.AddLineByBarcode', 'ReceiptIntake.Capture', 'ReceiptIntake.Known', 'ReceiptIntake.Quantity'])
        assert.equal(models.kind('receipt', 'receipt:1', event(action)), 'check');
    assert.equal(models.kind('receipt', 'receipt:1', event('StockDocumentManagement.UpdateHeader')), 'input');
});

test('The POS screen uses the persisted product detail and escapes it', () => {
    assert.match(models.render('pos', 'order:1', {detail:'Gạo ST25 · Tổng 408.000 ₫'}, '#1', false), /Gạo ST25/);
    const html=models.render('pos','order:1',{detail:'<img src=x onerror="attack()">'},'#1',false);
    assert.doesNotMatch(html,/<img|onerror="attack/);
    assert.match(html,/&lt;img/);
});

test('Receiving and review devices display the receipt title while retaining its ID in the tooltip', () => {
    for (const module of ['receipt', 'review']) {
        const html = models.render(module, 'receipt:42', null, 'Phiếu nhập #42', false, 'Nhập gạo tháng 10');
        assert.match(html, /title="Phiếu nhập #42 · Nhập gạo tháng 10">Nhập gạo tháng 10<\/span>/);
        assert.match(models.render(module, 'receipt:42', null, 'Phiếu nhập #42', false), />Phiếu nhập #42<\/span>/);
        const hostile = models.render(module, 'receipt:42', null, 'Phiếu nhập #42', false, '<img src=x onerror="attack()">');
        assert.doesNotMatch(hostile, /<img|onerror="attack/);
        assert.match(hostile, /&lt;img/);
    }
});
