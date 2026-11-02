const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const app = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js'), 'utf8');
const offlineSource = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.js'), 'utf8');
const offlineCore = require('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.core.js');
const tick = () => new Promise(setImmediate);
function fixture(post = async id => ({ orderId: id })) {
    const calls = [], prints = [], events = new Map(); let shows = 0, visible = false;
    const feedback = { begin() {}, complete() {}, askPrint() {}, showRetry() {} };
    const modalElement = { classList: { contains: () => visible }, addEventListener: (name, fn) => events.set(name, fn) };
    const env = { Number, encodeURIComponent, setTimeout, console, window: {
        open: url => { prints.push({ url, frame: false }); return { closed: false, location: { replace: url => prints.push({ url, frame: false }) } }; },
        PosOffline: { resolveOrderId: id => id, pendingInvoiceIntentOrderId: () => null, invoiceIntentStatus: () => null, print: () => false },
        PosPrinting: { openLocal: (receipt, setup, templates, offline, postPayment) => prints.push({orderId:receipt.orderId,postPayment}) } },
        document: { getElementById: () => null, createElement: () => ({ style: {}, dataset: {}, setAttribute() {}, addEventListener() {} }), body: { appendChild: frame => prints.push({ url: frame.src, frame: true }) } },
        invoiceIntentModalEl: modalElement, invoiceIntentErrorBox: { classList: { add() {}, remove() {} } }, invoiceIntentOrderText: {},
        invoiceIntentModal: { show() { visible = true; shows++; }, hide() { visible = false; events.get('hidden.bs.modal')?.(); } },
        btnInvoiceIntentAutomatic: null, checkoutFeedback: feedback,
        pendingReceiptPrint: null, pendingAskBeforePrintingReceipt: false, pendingInvoiceIntentOrderId: null, pendingPostPaymentPrintOrderId: null,
        invoiceIntentBusy: false, receiptPrintActive: false, receiptPrintClosing: false, receiptPrintQueue: [], postPaymentPrintOrders: new Set(), preparedReceiptPrintWindows: new Map(), posState: { ui: {} },
        getJson: async url => ({receipt:{orderId:Number(/receipt\/(\d+)/.exec(url)[1])},templates:[]}),
        showError: message => { throw Error(message); }, focusBarcodeInput() {},
        postJson: async (url, body) => { const id = Number(url.split('/')[3]); calls.push({ id, route: body.route }); return await post(id); } };
    vm.runInNewContext(app.slice(app.indexOf('function clearInvoiceIntentError()'), app.indexOf('        function nowIso()')), env);
    vm.runInNewContext(app.slice(app.indexOf('        function bindInvoiceIntentEvents()'), app.indexOf('function bindModuleEvents()')), env);
    env.bindInvoiceIntentEvents();
    return { env, calls, prints, shows: () => shows };
}
function journalCheckoutFixture(payments, changeDue=0) {
    let request, operationId=0;
    const h=fixture(async id=>(await request(`/admin/pos/${id}/invoice-route`,{route:1})).json());
    const context={storeId:1,terminalId:2,userId:3,shiftId:4,permissions:['pos.order.finalize','pos.payment.create'],screen:{}};
    const state=offlineCore.initial(context);
    state.currentId=123;
    state.orders[123]={orderId:123,status:0,grandTotal:100,subtotal:100,paidTotal:100+changeDue,balanceDue:0,changeDue,
        lines:[{lineId:1,variantId:1,sellingUnitId:1,quantity:1,unitPrice:100,lineTotal:100}],payments};
    const env={core:offlineCore,state,catalog:{products:[]},writer:true,authBlocked:false,connected:true,base:'/admin/pos',
        canWork:()=>true,localMode:()=>false,json:(value,status=200)=>new Response(JSON.stringify(value),{status}),
        fail:message=>new Response(JSON.stringify({message}),{status:409}),isUnavailable:response=>response.status>=500,
        invoiceIntentOperation:url=>url.endsWith('/invoice-route'),renumber(){},
        operation:(url,method,body)=>({id:`journal-${++operationId}`,url:url.pathname,method,body,occurredAt:'2026-10-15T09:00:00Z'}),
        save:async next=>{env.state=next;},
        // This is the actual journal's order: save(next); notify(); return response.
        notify:()=>h.env.restorePendingInvoiceIntent(),
        send:async op=>new Response(JSON.stringify(op.url.endsWith('/invoice-route')
            ?{success:true,orderId:123,route:'Automatic'}:{success:true,orderId:123}),{status:200})};
    vm.runInNewContext(offlineSource.slice(offlineSource.indexOf('    async function transport('),offlineSource.indexOf('    async function intercepted(')),env);
    let chain=Promise.resolve();
    request=(url,body={})=>{const next=chain.then(()=>env.transport(new URL(url,'https://gao.test'),{method:'POST',body:JSON.stringify(body)}));chain=next.catch(()=>{});return next;};
    h.env.window.PosOffline.pendingInvoiceIntentOrderId=()=>offlineCore.pendingInvoiceIntent(env.state);
    h.env.window.PosOffline.invoiceIntentStatus=id=>offlineCore.invoiceIntentOrder(env.state,id)?.invoiceIntent||null;
    h.env.posState.network={busyScopes:new Set()};
    return {...h,journal:env,request};
}
for(const [name,payments,changeDue]of [
    ['cash',[{paymentId:1,method:'Cash',amount:100}],0],
    ['overpaid transfer',[{paymentId:1,method:'BankTransfer',amount:120}],20]
]) test(`real journal notify before ${name} finalize response cannot print a second bill without its drawer intent`,async()=>{
    const h=journalCheckoutFixture(payments,changeDue);
    h.env.posState.network.busyScopes.add('checkout');
    await h.request('/admin/pos/123/finalize');
    h.env.openReceiptPrint(123,'80',true,true);
    h.env.posState.network.busyScopes.delete('checkout');
    await tick();await new Promise(resolve=>setTimeout(resolve,15));
    assert.equal(h.journal.state.orders[123].status,2,'Use the real offline finalize implementation');
    assert.equal(h.journal.state.orders[123].invoiceIntent.route,1);
    assert.equal(h.calls.length,1,'One invoice-route operation for one finalized sale');
    assert.deepEqual(h.prints,[{orderId:123,postPayment:true}],
        'One checkout must keep its one print-and-drawer intent; an early journal notification is not a history reprint');
});
test('a locally confirmed QR transfer keeps one immediate print through the real journal notification',async()=>{
    const h=journalCheckoutFixture([]);
    h.journal.state.orders[123].paidTotal=0;h.journal.state.orders[123].balanceDue=100;
    h.env.posState.network.busyScopes.add('checkout');
    await h.request('/admin/pos/offline/manual-transfer',{orderId:123,clientRequestId:'manual-transfer-123',amount:120,referenceCode:'QR-123'});
    h.env.openReceiptPrint(123,'80',true,true);
    h.env.posState.network.busyScopes.delete('checkout');
    await tick();await new Promise(resolve=>setTimeout(resolve,15));
    const order=h.journal.state.orders[123];
    assert.equal(order.status,2);assert.equal(order.payments[0].method,'BankTransfer');assert.equal(order.changeDue,20);
    assert.equal(h.calls.length,1);assert.deepEqual(h.prints,[{orderId:123,postPayment:true}]);
});
test('a reload still restores a genuinely unfinished durable invoice once without pretending it is a new cash collection',async()=>{
    const h=journalCheckoutFixture([{paymentId:1,method:'Cash',amount:100}]);
    offlineCore.apply(h.journal.state,h.journal.catalog,{url:'/admin/pos/123/finalize',method:'POST',body:{},occurredAt:'2026-10-15T09:00:00Z'});
    assert.equal(offlineCore.pendingInvoiceIntent(h.journal.state),123);
    h.env.restorePendingInvoiceIntent();h.env.restorePendingInvoiceIntent();
    await tick();await new Promise(resolve=>setTimeout(resolve,15));
    assert.equal(h.calls.length,1);assert.deepEqual(h.prints,[{url:'/admin/pos/receipt/123',frame:false}]);
    assert.equal(offlineCore.pendingInvoiceIntent(h.journal.state),null);
    h.env.restorePendingInvoiceIntent();await tick();assert.equal(h.prints.length,1);
});
test('a completed checkout does not turn a later pending journal status into another automatic print',async()=>{
    const h=journalCheckoutFixture([{paymentId:1,method:'Cash',amount:100}]);
    offlineCore.apply(h.journal.state,h.journal.catalog,{url:'/admin/pos/123/finalize',method:'POST',body:{},occurredAt:'2026-10-15T09:00:00Z'});
    h.env.postPaymentPrintOrders.add(123);
    h.env.restorePendingInvoiceIntent();await tick();
    assert.equal(h.calls.length,0);assert.equal(h.prints.length,0,'Recovery must not create a new owner for an already dispatched sale');
});
test('background transfers select Automatic and print without asking about electronic invoices', async () => {
    const h = fixture();
    h.env.openReceiptPrint(123, '80', true, true); await tick();
    assert.deepEqual(h.calls, [{ id: 123, route: 1 }]); assert.equal(h.shows(), 0);
    assert.deepEqual(h.prints, [{ orderId:123,postPayment:true }]);
    h.env.openReceiptPrint(123, '80', true, true); await tick(); assert.equal(h.prints.length, 1);
});

for (const [method, paymentMethods, credit] of [
    ['cash', [0], false], ['bank transfer', [1], false], ['card', [2], false],
    ['wallet', [3], false], ['other', [99], false], ['credit', [], true], ['mixed', [0, 1], false]
]) test(`actual ${method} finalize continues to the default invoice flow without a choice dialog`, async () => {
    const h = fixture();
    const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.payment.js'), 'utf8');
    const feedback = { window: {} };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/pos/pos.checkout-feedback.js'), 'utf8'), feedback);
    const finalized = [];
    const draft = { orderId: 123, grandTotal: 100, changeDue: 0, payments: paymentMethods.map(method => ({ method, amount: 100 / paymentMethods.length })) };
    const env = { window: { PosCheckoutFeedback: feedback.window.PosCheckoutFeedback }, getCurrentDraft: () => draft, Number, posState: {}, finalizationJobs: new Map(),
        prepareReceiptPrint(){},discardReceiptPrint(){},
        clearPaymentInlineError() {}, ensurePaymentErrorBox() {}, btnFinalizeFromPaymentModal: {},
        pendingFinalizeCustomerPayload: null, SUCCESS_HOLD_MS: 4500, RESET_AFTER_SUCCESS_MS: 4500,
        paymentModal: { hide() {} }, showCustomerPaymentSuccess() {}, setTimeout() {}, refreshLocksSafe() {}, sessionStorage: { removeItem() {} },
        postJson: async url => { finalized.push(url); return { orderId: 123 }; },
        runPosAction: async (_, key, action, options) => options.onSuccess(await action()),
        applyScreenSuccess: async options => { await options.beforeRefresh(); },
        openReceiptPrint: (...args) => h.env.openReceiptPrint(...args) };
    vm.runInNewContext(source.slice(source.indexOf('        async function finalizeRecordedPayment('), source.indexOf('        async function finalizeFromPaymentModal(')), env);
    await env.finalizeRecordedPayment(123, credit ? { expectedBalance: 100, expectedCustomerId: 1 } : null);
    await tick();
    assert.deepEqual(finalized, [credit ? '/admin/pos/123/finalize-credit' : '/admin/pos/123/finalize']);
    assert.deepEqual(h.calls, [{ id: 123, route: 1 }]); assert.equal(h.shows(), 0); assert.equal(h.prints.length, 1);
});
test('two background completions are serialized and retain their own order and print intent', async () => {
    let resolve;
    const h = fixture(id => id === 123 ? new Promise(r => { resolve = r; }) : Promise.resolve({ orderId: id }));
    h.env.openReceiptPrint(123, '80', true, true); h.env.openReceiptPrint(124, '80', true, true);
    assert.equal(h.calls.length, 1); resolve({ orderId: 123 });
    await tick(); await new Promise(r => setTimeout(r, 10));
    assert.deepEqual(h.calls.map(x => x.id), [123, 124]);
    assert.deepEqual(h.prints.map(x => x.orderId), [123,124]);
});
test('a network error retains the order for retry; paper bill preference still requires its separate choice', async () => {
    let fail = true;
    const h = fixture(async id => { if (fail) throw Error('Mất kết nối'); return { orderId: id, askBeforePrintingReceipt: true }; });
    h.env.openReceiptPrint(123, '80', true, true); await tick();
    assert.equal(h.shows(), 1); assert.equal(h.env.pendingInvoiceIntentOrderId, 123); assert.equal(h.prints.length, 0);
    fail = false; await h.env.submitInvoiceIntent(1);
    assert.equal(h.env.pendingReceiptPrint.orderId, 123); assert.equal(h.prints.length, 0);
    h.env.chooseReceiptPrint(false); assert.equal(h.env.pendingReceiptPrint, null); assert.equal(h.env.receiptPrintActive, false);
});
test('history reprint preserves invoice choice; an unfinished offline invoice defaults to Automatic', async () => {
    const h = fixture();
    h.env.openReceiptPrint(123); await tick(); assert.equal(h.calls.length, 0); assert.equal(h.prints[0].frame, false);
    h.env.window.PosOffline.pendingInvoiceIntentOrderId = () => 124;
    const post = h.env.postJson;
    h.env.postJson = async (...args) => { const result = await post(...args); h.env.window.PosOffline.pendingInvoiceIntentOrderId = () => null; return result; };
    h.env.restorePendingInvoiceIntent(); await tick();
    assert.deepEqual(h.calls, [{ id: 124, route: 1 }]); assert.equal(h.prints[1].url, '/admin/pos/receipt/124');
});
test('history reprint with paper preference asks only about printing and never changes an existing invoice route', async () => {
    const h = fixture(); h.env.openReceiptPrint(123, '80', true, false, null, true);
    await tick(); assert.equal(h.calls.length, 0); assert.equal(h.env.pendingReceiptPrint.orderId, 123);
    h.env.chooseReceiptPrint(false); assert.equal(h.env.receiptPrintActive, false);
});
test('the next receipt waits for the previous modal to finish closing before replacing its snapshot', async () => {
    const h = fixture(async id => ({ orderId: id, askBeforePrintingReceipt: true }));
    h.env.openReceiptPrint(123, '80', true, true); h.env.openReceiptPrint(124, '80', true, true); await tick();
    const hide = h.env.invoiceIntentModal.hide;
    h.env.invoiceIntentModal.hide = () => {};
    h.env.chooseReceiptPrint(false); await tick(); assert.equal(h.calls.length, 1); assert.equal(h.env.receiptPrintClosing, true);
    hide(); await new Promise(r => setTimeout(r, 10)); assert.deepEqual(h.calls.map(x => x.id), [123, 124]);
});
