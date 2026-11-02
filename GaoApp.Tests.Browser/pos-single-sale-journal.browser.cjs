'use strict';
// One cash sale through the real journal (IndexedDB + offline transport), payment
// owner, invoice owner and printing. APIs are synthetic; Python/HTTP is the
// supplied Handler with USB output replaced. No database or physical device.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {spawn}=require('node:child_process');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'..'),origin='https://gao-single-sale.test';
const read=file=>fs.readFileSync(path.join(root,file),'utf8');
const helperSource=process.env.GAO_LINUX_HELPER_SOURCE;
if(!helperSource)throw Error('Set GAO_LINUX_HELPER_SOURCE to the supplied server.py');
const legacyRestore=process.env.GAO_TEST_LEGACY_JOURNAL_RESTORE==='1';
const python=String.raw`
import importlib.util, json, sys, time
spec=importlib.util.spec_from_file_location('supplied_helper',sys.argv[1])
helper=importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
helper.device_ready=lambda: True
def memory_device(chunks):
    commands=list(chunks)
    print(json.dumps({'event':'device-write','copies':commands.count(b'\x1b@\x1ba\x00\x1dL\x00\x00\x1dW\x40\x02'),
        'pulses':commands.count(b'\x1b\x70\x00\x19\xfa')}),flush=True)
    time.sleep(0.05)
helper.write_commands=memory_device
original_reply=helper.Handler.reply
def reply(self,status,**value):
    original_reply(self,status,**value)
    if self.path in ('/print','/drawer'):
        print(json.dumps({'event':'ack','status':status,'endpoint':self.path}),flush=True)
helper.Handler.reply=reply
server=helper.ThreadingHTTPServer(('127.0.0.1',17880),helper.Handler)
server.origins=helper.allowed_origins('${origin}')
print(json.dumps({'event':'ready'}),flush=True)
server.serve_forever()
`;
const initialDraft={orderId:123,status:0,orderNumber:null,subtotal:131000,grandTotal:131000,paidTotal:0,balanceDue:131000,
    changeDue:0,discountTotal:0,orderDiscount:0,appliedRewardVouchers:[],createdAtUtc:'2026-10-15T00:00:00Z',
    lines:[{lineId:10,variantId:11,itemName:'Synthetic product',quantity:1,unitPrice:131000,lineTotal:131000,
        lineDiscount:0,promotionDiscount:0,multiplier:1,sellingUnitId:12}],payments:[]};
const paidDraft={...initialDraft,paidTotal:200000,balanceDue:0,changeDue:69000,
    payments:[{paymentId:99,method:'Cash',amount:200000,createdAt:'2026-10-15T00:00:00Z'}]};
const finalized={...paidDraft,status:2,orderNumber:'TEST-CASH-123',completedAtUtc:'2026-10-15T00:00:01Z'};
const receipt={...finalized,status:'Completed',storeName:'TEST STORE'};
const emptyDraft={...initialDraft,orderId:124,subtotal:0,grandTotal:0,balanceDue:0,lines:[]};
const screen=draft=>({currentCart:{currentOrderId:draft.orderId,hasCurrentOrder:true},currentDraft:draft,draftOrders:[],heldOrders:[]});
const setup={receipt,storeId:123,terminalId:456,templates:[],autoPrint:true};
const templates=require('../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
setup.receiptDefault={template:templates.builtIns().find(x=>x.key==='itemwide-80')};
const view=read('GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml');
const paymentStart=view.indexOf('<div class="modal fade pos-payment-workspace-modal"');
const paymentEnd=view.lastIndexOf('<div class="modal fade"',view.indexOf('id="confirmModal"'));
const paymentMarkup=view.slice(paymentStart,paymentEnd).replace(/@if\s*\([^\n]*\)\s*\{/g,'').replace(/^\s*}\s*$/gm,'');
const invoiceStart=view.lastIndexOf('<div class="modal fade"',view.indexOf('id="invoiceIntentModal"'));
const invoiceMarkup=view.slice(invoiceStart,view.indexOf('<!--',invoiceStart));
const app=read('GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js');
let invoiceLogic=app.slice(app.indexOf('function clearInvoiceIntentError()'),app.indexOf('        function nowIso()'));
if(legacyRestore)invoiceLogic=invoiceLogic.replace(/function restorePendingInvoiceIntent\(\) \{[\s\S]*?\n\}/,`function restorePendingInvoiceIntent() {
    const orderId=window.PosOffline?.pendingInvoiceIntentOrderId();
    if(orderId&&!receiptPrintActive&&!invoiceIntentBusy&&Number(pendingInvoiceIntentOrderId)!==Number(orderId))
        openReceiptPrint(orderId,'80',true,false,null,window.PosOffline?.receiptPrintPreference?.(orderId));
}`);
const invoiceBindings=app.slice(app.indexOf('        function bindInvoiceIntentEvents()'),app.indexOf('function bindModuleEvents()'));
const printBindings=app.slice(app.indexOf('function bindModuleEvents()'),app.indexOf('    posCustomer.bindEvents();'))+'}';
const scripts=['vendor/js/bootstrap.js','Admin/js/pos/pos.common.js','Admin/js/pos/pos.checkout-feedback.js',
    'Admin/js/pos/pos.payment.js','Admin/js/printing/receipt.templates.js','Admin/js/printing/print.lifecycle.js',
    'Admin/js/printing/pos.printing.js','Admin/js/printing/shift.cash-printing.js',
    'Admin/js/pos/pos.offline.core.js','Admin/js/pos/pos.offline.js'].map(file=>'<script src="/'+file+'"></script>').join('');
const html=`<!doctype html><meta charset="utf-8"><style>${read('GaoApp.Web/wwwroot/vendor/css/core.css')}</style>
<main id="posShell" data-store-id="123" data-terminal-id="456" data-user-id="789"><input id="barcode"></main>
${paymentMarkup}${invoiceMarkup}${scripts}<script>
const noop=()=>{};
window.calls=[];window.printErrors=[];window.windowOpenCount=0;window.invoicePrompts=0;window.journalEvents=[];
const actualOpen=window.open.bind(window);window.open=(...args)=>{windowOpenCount++;return actualOpen(...args);};
const focusBarcodeInput=()=>document.getElementById('barcode').focus();
const hasAnyModalOpen=()=>!!document.querySelector('.modal.show');
const showError=message=>printErrors.push(message);
const posState=window.posState={business:{currentDraft:${JSON.stringify(initialDraft)}},ui:{modals:{}},
    network:{isOnline:true,pendingActions:new Set(),busyScopes:new Set()}};
const postJson=async(url,body)=>{calls.push({url,body});const response=await fetch(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body||{})});
    const result=await response.json();if(!response.ok)throw Error(result.message);return result;};
const getJson=async url=>(await fetch(url)).json();
const invoiceIntentModalEl=document.getElementById('invoiceIntentModal'),invoiceIntentOrderText=document.getElementById('invoiceIntentOrderText'),
    invoiceIntentErrorBox=document.getElementById('invoiceIntentErrorBox'),btnInvoiceIntentAutomatic=document.getElementById('btnInvoiceIntentAutomatic');
const invoiceIntentModal=new bootstrap.Modal(invoiceIntentModalEl,{backdrop:'static',keyboard:false});
const actualInvoiceShow=invoiceIntentModal.show.bind(invoiceIntentModal);invoiceIntentModal.show=(...args)=>{invoicePrompts++;return actualInvoiceShow(...args);};
let pendingReceiptPrint=null,pendingAskBeforePrintingReceipt=false,pendingInvoiceIntentOrderId=null,pendingPostPaymentPrintOrderId=null,
    invoiceIntentBusy=false,receiptPrintActive=false,receiptPrintClosing=false;
const receiptPrintQueue=[],postPaymentPrintOrders=new Set(),preparedReceiptPrintWindows=new Map();
const checkoutFeedback=PosCheckoutFeedback.create({modal:invoiceIntentModalEl,submit:submitInvoiceIntent,choosePrint:chooseReceiptPrint,isBusy:()=>invoiceIntentBusy});
${invoiceLogic}
${invoiceBindings}
${printBindings}
bindInvoiceIntentEvents();bindModuleEvents();
window.addEventListener('pos:offline-status',()=>{
    journalEvents.push({pending:PosOffline.pendingInvoiceIntentOrderId(),checkoutBusy:posState.network.busyScopes.has('checkout')});
    restorePendingInvoiceIntent();
});
const modalElement=document.getElementById('paymentModal');
window.payment=PosPayment.create({posState,
    elements:Object.fromEntries(['payMethod','payAmount','payReference','payProvider','btnAddPayment','btnFinalizeFromPaymentModal','btnPayExact'].map(id=>[id,document.getElementById(id)]).concat([['paymentModalEl',modalElement]])),
    modals:{paymentModal:new bootstrap.Modal(modalElement)},helpers:{postJson,runPosAction:PosCommon.runPosAction,
        renderPaymentModalDraft:noop,renderPaymentPreview:noop,showSuccess:noop,showError,
        applyDraftActionSuccess:({draft,afterSync})=>{posState.business.currentDraft=draft;afterSync?.(draft);},
        applyScreenActionSuccess:async({beforeRefresh})=>{await beforeRefresh?.();posState.business.currentDraft=${JSON.stringify(emptyDraft)};focusBarcodeInput();},
        requestScreenRefresh:async()=>{},renderPayments:noop,syncDraftToUi:noop,focusBarcodeInput,openReceiptPrint,prepareReceiptPrint,discardReceiptPrint}});
(async()=>{await PosOffline.init();while(!PosOffline.status().ready||PosOffline.status().syncing)await new Promise(resolve=>setTimeout(resolve,10));
    payment.bindEvents();payment.openPaymentModal();window.testReady=true;})();
</script>`;
async function until(predicate,description){const end=Date.now()+15000;while(!predicate()){if(Date.now()>end)throw Error('Timed out: '+description);await new Promise(resolve=>setTimeout(resolve,20));}}
(async()=>{
    const messages=[],stderr=[],results=[];
    const helper=spawn(process.env.GAO_TEST_PYTHON||'python',['-B','-u','-c',python,helperSource],{windowsHide:true,stdio:['ignore','pipe','pipe']});
    let buffer='';helper.stdout.on('data',chunk=>{buffer+=chunk;let boundary;while((boundary=buffer.indexOf('\n'))>=0){const line=buffer.slice(0,boundary).trim();buffer=buffer.slice(boundary+1);if(line)messages.push(JSON.parse(line));}});
    helper.stderr.on('data',chunk=>stderr.push(String(chunk)));
    let browser;
    try{
        await until(()=>{if(helper.exitCode!==null)throw Error(stderr.join(''));return messages.some(x=>x.event==='ready');},'Helper startup');
        browser=await chromium.launch(process.env.GAO_PLAYWRIGHT_CHROME?{executablePath:process.env.GAO_PLAYWRIGHT_CHROME}:{channel:'chrome'});
        for(const copies of [1,2]){
            const context=await browser.newContext({viewport:{width:1366,height:900},reducedMotion:'reduce'}),errors=[],posts=[],apiCalls=[];
            await context.grantPermissions(['local-network-access'],{origin});
            await context.addInitScript(copies=>{Object.defineProperty(navigator,'serviceWorker',{value:undefined});
                try{localStorage.setItem('gao-pos-print-v1:123:456',JSON.stringify({mode:'helper',copies,templateKey:'itemwide-80'}));}
                catch{/* POS Shift's sandboxed paper has no localStorage; its owner holds the preferences. */}},copies);
            context.on('page',page=>page.on('pageerror',error=>errors.push(error.message)));
            context.on('request',request=>{if(request.url().startsWith('http://127.0.0.1:17880/')&&request.method()==='POST')posts.push({endpoint:new URL(request.url()).pathname,payload:request.postDataJSON()});});
            let releaseCash,releaseFinalize,currentScreen=screen(initialDraft);
            const offlineContext={storeId:123,terminalId:456,userId:789,shiftId:1,antiForgeryToken:'SYNTHETIC',
                expiresAtUtc:new Date(Date.now()+3600000).toISOString(),permissions:['pos.order.create','pos.order.finalize','pos.payment.create','pos.order.reprint'],
                screen:currentScreen,receiptDefault:setup.receiptDefault,receiptTemplates:[],receiptStoreInfo:{storeName:'TEST STORE'}};
            await context.route(origin+'/**',async route=>{
                const url=new URL(route.request().url()),pathname=url.pathname;
                const json=value=>route.fulfill({contentType:'application/json',body:JSON.stringify(value)});
                if(pathname.startsWith('/Admin/')||pathname.startsWith('/vendor/'))return route.fulfill({contentType:'text/javascript',body:read('GaoApp.Web/wwwroot'+pathname)});
                if(pathname==='/admin/pos/offline/bootstrap'||pathname==='/admin/pos/offline/status')return json(offlineContext);
                if(pathname==='/admin/pos/offline/catalog')return json({items:[],nextAfterId:null});
                if(pathname==='/admin/pos/offline/customers')return json({items:[],nextAfterId:null});
                if(pathname==='/admin/pos/offline/promotions')return json([]);
                if(pathname==='/admin/pos/screen')return json(currentScreen);
                if(pathname==='/admin/pos/cart/current/payments'){
                    apiCalls.push({path:pathname,body:route.request().postDataJSON()});
                    await new Promise(resolve=>releaseCash=resolve);currentScreen=screen(paidDraft);return json(paidDraft);
                }
                if(pathname==='/admin/pos/123/finalize'){
                    apiCalls.push({path:pathname,body:route.request().postDataJSON()});
                    await new Promise(resolve=>releaseFinalize=resolve);currentScreen=screen(emptyDraft);return json({success:true,orderId:123,data:finalized});
                }
                if(pathname==='/admin/pos/123/invoice-route'){
                    apiCalls.push({path:pathname,body:route.request().postDataJSON()});return json({success:true,orderId:123,route:'Automatic',askBeforePrintingReceipt:false});
                }
                if(pathname.startsWith('/admin/pos/receipt/')){
                    if(url.searchParams.get('data')==='true')return json(setup);
                    const body='<!doctype'+read('GaoApp.Web/Areas/Admin/Views/ReceiptTemplates/Print.cshtml').split('<!doctype')[1]
                        .replace('@Model.Receipt.OrderNumber',receipt.orderNumber)
                        .replace('@Html.Raw(JsonSerializer.Serialize(Model, ReceiptTemplateService.Json))',JSON.stringify(setup))
                        .replaceAll('src="~/','src="/');
                    return route.fulfill({contentType:'text/html',body});
                }
                return route.fulfill({contentType:'text/html',body:html});
            });
            const page=await context.newPage();await page.goto(origin+'/admin/pos');
            await page.waitForFunction(()=>window.testReady===true);
            await page.waitForFunction(()=>document.activeElement?.id==='payAmount');
            // Known-working Shift reference uses the same actual Helper, copies,
            // renderer transport and device before the single cash sale.
            const beforeShift=messages.filter(x=>x.event==='device-write').length;
            await page.evaluate(()=>PosShiftCashPrinting.print({id:1,type:'CashIn',amount:10000,createdAtUtc:'2026-10-15T00:00:00Z',posShiftId:1,reason:'Test'},
                {storeId:123,terminalId:456},true));
            await until(()=>messages.filter(x=>x.event==='ack'&&x.status===200).length>=beforeShift+1,'Shift acknowledgement');
            assert.equal(posts.length,1);assert.equal(posts[0].payload.cashDrawer,true);
            posts.length=0;const beforeSale=messages.filter(x=>x.event==='device-write').length;
            await page.locator('#payAmount').fill('200000');
            await page.keyboard.down('Enter');await page.keyboard.down('Enter');await page.keyboard.up('Enter');
            for(let i=0;i<6;i++)await page.keyboard.press('Enter');
            await until(()=>!!releaseCash,'cash collection request');assert.equal(apiCalls.length,1);
            releaseCash();await until(()=>!!releaseFinalize,'cash finalize request');
            for(let i=0;i<6;i++)await page.keyboard.press(i%2?'Enter':'Control+Enter');
            assert.equal(apiCalls.filter(x=>x.path.endsWith('/finalize')).length,1);
            releaseFinalize();
            await page.waitForFunction(()=>posState.business.currentDraft.orderId===124);
            await until(()=>posts.some(x=>x.endpoint==='/print'),'cash print');
            await page.waitForFunction(()=>!document.querySelector('iframe[data-pos-local-print],iframe[data-pos-payment-order]')&&!receiptPrintActive&&!invoiceIntentBusy);
            // Let journal notifications and modal transitions settle; a second
            // transport arising from recovery must be visible in this same sale.
            await page.waitForTimeout(750);
            const journalEvents=await page.evaluate(()=>window.journalEvents);
            assert.ok(journalEvents.some(x=>x.pending===123&&x.checkoutBusy),'Exercise the real journal notify while finalize owns checkout');
            const saleWrites=messages.filter(x=>x.event==='device-write').slice(beforeSale);
            assert.deepEqual(posts.map(x=>[x.endpoint,x.payload.cashDrawer,x.payload.copies]),[['/print',true,copies]],
                'A single sale must not recovery-print before its normal checkout print');
            assert.deepEqual(saleWrites.map(x=>[x.copies,x.pulses]),[[copies,1]]);
            assert.equal(apiCalls.filter(x=>x.path.endsWith('/payments')).length,1);
            assert.equal(apiCalls.filter(x=>x.path.endsWith('/finalize')).length,1);
            assert.deepEqual(apiCalls.filter(x=>x.path.endsWith('/invoice-route')).map(x=>x.body),[{route:1}]);
            assert.equal(await page.evaluate(()=>invoicePrompts),0);
            assert.equal(await page.evaluate(()=>windowOpenCount),0);
            assert.equal(context.pages().length,1);
            assert.deepEqual(await page.evaluate(()=>printErrors),[]);assert.deepEqual(errors,[]);
            await page.locator('#barcode').fill('NEXT-SALE-READY');
            results.push({copies,journalPendingDuringFinalize:true,salePrintRequests:posts.length,drawerPulses:saleWrites.reduce((sum,x)=>sum+x.pulses,0),invoicePrompts:0,popups:0});
            await context.close();
        }
        console.log(JSON.stringify({status:'PASS',legacyRestore,actualSuppliedPythonHandler:true,physicalDeviceStubbed:true,
            realIndexedDbJournal:true,realOfflineTransport:true,results}));
    }finally{if(browser)await browser.close();helper.kill();await new Promise(resolve=>helper.exitCode!==null?resolve():helper.once('exit',resolve));}
})().catch(error=>{console.error(error);process.exitCode=1;});
