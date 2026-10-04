const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.checkout-feedback.js'), 'utf8');
function api() { const window = {}; vm.runInNewContext(source, { window }); return window.PosCheckoutFeedback; }
test('cash feedback uses cash tender separately from transfers and keeps confirmed change', () => {
    const draft = { grandTotal: 150000, changeDue: 50000, payments: [{ method: 'Cash', amount: 120000 }, { method: 0, amount: 30000 }, { method: 'BankTransfer', amount: 50000 }] };
    const result = api().cashSummary(draft);
    assert.equal(result.received, 150000); assert.equal(result.other, 50000);
    assert.equal(result.change, 50000); assert.equal(result.total, 150000);
    draft.payments[0].amount = 0; draft.changeDue = 0;
    assert.equal(result.received, 150000); assert.equal(result.change, 50000);
});
test('exact cash shows zero change, non-cash and empty drafts do not show cash feedback', () => {
    assert.equal(api().cashSummary({ grandTotal: 100, changeDue: 0, payments: [{ method: 'Cash', amount: 100 }] }).change, 0);
    for (const draft of [null, {}, { payments: [] }, { payments: [{ method: 'BankTransfer', amount: 100 }] }])
        assert.equal(api().cashSummary(draft), null);
});
test('actual cash finalize passes the paid order snapshot only after successful finalize', async () => {
    const payment = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.payment.js'), 'utf8');
    const start = payment.indexOf('        async function finalizeRecordedPayment(');
    const end = payment.indexOf('        async function finalizeFromPaymentModal(', start);
    let draft = { askBeforePrintingReceipt: true, orderId: 123, grandTotal: 158000, changeDue: 42000, payments: [{ method: 'Cash', amount: 200000 }] };
    const printed=[];
    const env = { window:{PosCheckoutFeedback:api()},getCurrentDraft:()=>draft,Number,posState:{},
        clearPaymentInlineError(){},ensurePaymentErrorBox(){},btnFinalizeFromPaymentModal:{},
        pendingFinalizeCustomerPayload:null,SUCCESS_HOLD_MS:4500,RESET_AFTER_SUCCESS_MS:4500,
        paymentModal:{hide(){}},showCustomerPaymentSuccess(){},setTimeout(){},refreshLocksSafe(){},
        postJson:async()=>({orderId:123}),runPosAction:async(_,key,action,options)=>options.onSuccess(await action()),
        applyScreenSuccess:async options=>{draft={orderId:124,payments:[],grandTotal:0,changeDue:0};await options.beforeRefresh();},
        openReceiptPrint:(...args)=>printed.push(args) };
    vm.runInNewContext(payment.slice(start,end),env);
    await env.finalizeRecordedPayment(123);
    assert.equal(printed.length,1); assert.equal(printed[0][0],123);
    assert.equal(printed[0][5],true); assert.equal(printed[0][4].received,200000); assert.equal(printed[0][4].change,42000);
    env.postJson=async()=>{throw Error('failed')};
    await assert.rejects(env.finalizeRecordedPayment(124),/failed/); assert.equal(printed.length,1);
});
