const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.checkout-feedback.js'), 'utf8');
function api() { const window = {}; vm.runInNewContext(source, { window }); return window.PosCheckoutFeedback; }

function reminderHarness() {
    const nodes = new Map();
    const listeners = new Map();
    const windowListeners = new Map();
    const timers = new Map();
    let focused = true;
    let nextTimer = 0;
    function node(id) {
        if (!nodes.has(id)) {
            const classes = new Set();
            nodes.set(id, { hidden: true, textContent: '', offsetWidth: 0,
                classList: { add: name => classes.add(name), remove: name => classes.delete(name), contains: name => classes.has(name) },
                querySelector: selector => node(selector.slice(1)), addEventListener() {}, focus() {} });
        }
        return nodes.get(id);
    }
    const modal = { querySelector: selector => node(selector.slice(1)),
        addEventListener: (name, callback) => listeners.set(name, callback) };
    const window = { addEventListener: (name, callback) => windowListeners.set(name, callback) };
    vm.runInNewContext(source, { window,
        document: { getElementById: node, addEventListener() {}, hasFocus: () => focused },
        setTimeout(callback, delay) { const id = ++nextTimer; timers.set(id, { callback, delay }); return id; },
        clearTimeout: id => timers.delete(id) });
    const feedback = window.PosCheckoutFeedback.create({ modal, submit() {}, choosePrint() {}, isBusy: () => false });
    return { feedback, node, timers, close: () => listeners.get('hidden.bs.modal')(),
        setFocused: value => { focused = value; }, focus: () => windowListeners.get('focus')() };
}

test('cash reminder appears only after invoice popup closes and preserves paid cash through next-cart changes', () => {
    const h = reminderHarness();
    const summary = { received: 60000, change: 31000, total: 29000, other: 0 };
    h.feedback.begin(summary, 1799615);
    assert.equal(h.node('posCashReminder').hidden, true);
    assert.equal(h.timers.size, 0);
    summary.received = 0; summary.change = 0;
    h.close();
    assert.equal(h.node('posCashReminder').hidden, false);
    assert.equal(h.node('posCashReminderReceived').textContent, '60.000');
    assert.equal(h.node('posCashReminderChange').textContent, '31.000');
    assert.equal(h.node('posCashReminderOrder').textContent, 'Đơn #1799615');
    const [timer] = h.timers.values();
    assert.equal(timer.delay, 4000);
    timer.callback();
    assert.equal(h.node('posCashReminder').hidden, true);
    h.close();
    assert.equal(h.node('posCashReminder').hidden, true, 'Closing again must not replay the paid snapshot');
});

test('receipt print preference keeps the reminder pending until the final choice and includes exact cash', () => {
    const h = reminderHarness();
    h.feedback.begin({ received: 29000, change: 0, total: 29000, other: 0 }, 123);
    h.feedback.askPrint();
    assert.equal(h.node('posCashReminder').hidden, true);
    assert.equal(h.timers.size, 0);
    h.close();
    assert.equal(h.node('posCashReminder').hidden, false);
    assert.equal(h.node('posCashReminderChange').textContent, '0');
});
test('default invoice completion shows the cash reminder without opening an invoice-choice dialog', () => {
    const h = reminderHarness();
    h.feedback.begin({ received: 60000, change: 31000, total: 29000, other: 0 }, 123);
    assert.equal(h.node('invoiceIntentChoices').hidden, true);
    assert.equal(h.node('invoiceIntentModalTitle').textContent, 'Hoàn tất đơn hàng');
    h.feedback.complete();
    assert.equal(h.node('posCashReminderReceived').textContent, '60.000');
    assert.equal(h.node('posCashReminderChange').textContent, '31.000');
    assert.equal(h.timers.size, 1);
});

test('printing in a foreground tab does not consume the four-second POS reminder before the cashier returns', () => {
    const h = reminderHarness();
    h.feedback.begin({ received: 60000, change: 31000 }, 123);
    h.setFocused(false); h.close();
    assert.equal(h.node('posCashReminder').hidden, true);
    assert.equal(h.timers.size, 0);
    h.setFocused(true); h.focus();
    assert.equal(h.node('posCashReminder').hidden, false);
    assert.equal(h.node('posCashReminderChange').textContent, '31.000');
    assert.equal([...h.timers.values()][0].delay, 4000);
    h.focus();
    assert.equal(h.timers.size, 1, 'Further focus events must not restart or replay the reminder');
});

test('following checkout cancels the old reminder timer and non-cash/reprint summaries never reuse cash', () => {
    const h = reminderHarness();
    h.feedback.begin({ received: 60000, change: 31000 }, 123); h.close();
    const firstTimer = [...h.timers.keys()][0];
    h.feedback.begin({ received: 100000, change: 20000 }, 124);
    assert.equal(h.node('posCashReminder').hidden, true);
    assert.equal(h.timers.has(firstTimer), false);
    h.close();
    assert.equal(h.timers.size, 1);
    assert.equal(h.node('posCashReminderReceived').textContent, '100.000');
    assert.equal(h.node('posCashReminderOrder').textContent, 'Đơn #124');
    for (const summary of [null, { received: 0, change: 0 }]) {
        h.feedback.begin(summary, 125); h.close();
        assert.equal(h.node('posCashReminder').hidden, true);
        assert.equal(h.timers.size, 0);
    }
});
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
    const env = { window:{PosCheckoutFeedback:api()},getCurrentDraft:()=>draft,Number,posState:{}, finalizationJobs: new Map(),
        prepareReceiptPrint(){},discardReceiptPrint(){},
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
