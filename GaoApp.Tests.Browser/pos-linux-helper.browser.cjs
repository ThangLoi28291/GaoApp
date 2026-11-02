'use strict';
// Real supplied Python HTTP handler and production receipt scripts. Only USB is
// stubbed; there are no business writes, real printer calls or protocol mocks.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {spawn} = require('node:child_process');
const {chromium} = require('playwright');
const root = path.resolve(__dirname,'..');
const helperSource = process.env.GAO_LINUX_HELPER_SOURCE;
const lostTabStorage = process.env.GAO_TEST_LOST_PRINT_STORAGE === '1';
if (!helperSource) throw Error('Set GAO_LINUX_HELPER_SOURCE to the supplied server.py');
const origin = 'https://gao-linux-helper.test';
const read = name => fs.readFileSync(path.join(root,name),'utf8');
const python = String.raw`
import importlib.util, json, sys, time
spec = importlib.util.spec_from_file_location('supplied_helper', sys.argv[1])
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
helper.device_ready = lambda: True
def memory_device(chunks):
    commands = list(chunks)
    print(json.dumps({'event':'device-write', 'drawerPulses':commands.count(b'\x1b\x70\x00\x19\xfa'), 'copies':commands.count(b'\x1b@\x1ba\x00\x1dL\x00\x00\x1dW\x40\x02')}), flush=True)
    time.sleep(0.25)
helper.write_commands = memory_device
original_reply = helper.Handler.reply
def traced_reply(self, status, **value):
    original_reply(self, status, **value)
    if self.path in ('/print', '/drawer'):
        print(json.dumps({'event':'helper-ack', 'endpoint':self.path, 'status':status, 'value':{'ok':status == 200, 'protocol':helper.PROTOCOL, **value}}), flush=True)
helper.Handler.reply = traced_reply
server = helper.ThreadingHTTPServer(('127.0.0.1', 17880), helper.Handler)
server.origins = helper.allowed_origins('${origin}')
print(json.dumps({'event':'ready'}), flush=True)
server.serve_forever()
`;
const receipt = {orderId:123,orderNumber:'TEST-123',storeName:'CỬA HÀNG THỬ',createdAtUtc:'2026-10-15T00:00:00Z',status:'Completed',
    subtotal:10000,grandTotal:10000,paidTotal:20000,changeDue:10000,lines:[{itemName:'Gạo thử nghiệm',quantity:1,unitPrice:10000,lineTotal:10000}],
    payments:[{method:'Cash',amount:20000}]};
const templates = require('../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
const setup = {receipt,storeId:123,terminalId:456,templates:[],autoPrint:true,
    receiptDefault:{template:templates.builtIns().find(x=>x.key==='itemwide-80')}};
const transferReceipt = {...receipt,orderId:124,orderNumber:'TEST-QR-124',paidTotal:10000,changeDue:0,
    payments:[{method:'BankTransfer',amount:10000,reference:'POS-124-QR-TEST'}]};
const overpaidReceipt = {...transferReceipt,orderId:125,orderNumber:'TEST-OVERPAID-125',paidTotal:12000,changeDue:2000,payments:[{method:'BankTransfer',amount:12000,reference:'POS-125-QR-TEST'}]};
const bankItem = {receiptId:10,waitId:20,qrId:30,orderId:124,amount:10000,automatic:true,
    senderName:'KHACH CHUYEN KHOAN THU',transactionCode:'GD:TEST-124',postedAtUtc:'2026-10-15T00:00:00Z'};
let transferAvailable=false;
const bankConfirmations=[],invoiceRoutes=[];
const app = read('GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js');
const printing = app.slice(app.indexOf('function clearInvoiceIntentError()'),app.indexOf('        function nowIso()'));
const payment = read('GaoApp.Web/wwwroot/Admin/js/pos/pos.payment.js');
const bankCompletion = payment.slice(payment.indexOf("        window.addEventListener('bank-notification:completed'"),
    payment.indexOf("        window.addEventListener('bank-notification:tracking'"));
const parentHtml = `<!doctype html><meta charset="utf-8"><main id="posShell" data-store-id="123" data-terminal-id="456"><button id="checkout">Thanh toán thử</button><button id="overpaid">Chuyển khoản dư</button><button id="reprint">In lại</button></main>
<script src="/Admin/js/printing/receipt.templates.js"></script><script src="/Admin/js/printing/print.lifecycle.js"></script><script src="/Admin/js/printing/pos.printing.js"></script>
<script>
const preparedReceiptPrintWindows=new Map(),receiptPrintQueue=[],postPaymentPrintOrders=new Set();
let pendingReceiptPrint=null,pendingAskBeforePrintingReceipt=false,pendingInvoiceIntentOrderId=null,pendingPostPaymentPrintOrderId=null,
    invoiceIntentBusy=false,receiptPrintActive=false,receiptPrintClosing=false;
const invoiceIntentErrorBox=null,invoiceIntentOrderText={},btnInvoiceIntentAutomatic=null,
    invoiceIntentModalEl={classList:{contains:()=>false}},invoiceIntentModal={show:()=>window.invoicePrompts++,hide(){}},
    checkoutFeedback={begin(){},complete(){},askPrint(){},showRetry(){}};
window.printErrors=[];window.invoicePrompts=0;window.qrResults=[];
window.PosOffline={print:()=>false,resolveOrderId:id=>id,pendingInvoiceIntentOrderId:()=>null,invoiceIntentStatus:()=>null};
const showError=message=>printErrors.push(message);const getJson=async url=>(await fetch(url)).json();
const postJson=async(url,body)=>(await fetch(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)})).json();
const getCurrentDraft=()=>({orderId:124,askBeforePrintingReceipt:false});
const applyQrPaymentResult=async result=>qrResults.push(result);
${printing}
${bankCompletion}
document.getElementById('checkout').onclick=()=>{prepareReceiptPrint(123);Promise.resolve().then(()=>printReceiptAfterInvoiceIntent(123,null,true));};
document.getElementById('overpaid').onclick=()=>printReceiptAfterInvoiceIntent(125,null,true);
document.getElementById('reprint').onclick=()=>window.open('/admin/pos/receipt/123','_blank');
</script><script src="/Admin/js/pos/pos.bank-notifications.js"></script>`;
async function until(predicate) {
    const deadline=Date.now()+10000;
    while(!predicate()) {if(Date.now()>deadline)throw Error('Test helper did not become ready');await new Promise(resolve=>setTimeout(resolve,25));}
}
(async()=>{
    const messages=[],stderr=[],acknowledgements=[];
    const helper=spawn(process.env.GAO_TEST_PYTHON || 'python',['-B','-u','-c',python,helperSource],{windowsHide:true,stdio:['ignore','pipe','pipe']});
    let buffer='';
    helper.stdout.on('data',chunk=>{buffer+=chunk;let boundary;while((boundary=buffer.indexOf('\n'))>=0){const line=buffer.slice(0,boundary).trim();buffer=buffer.slice(boundary+1);if(line){const value=JSON.parse(line);messages.push(value);if(value.event==='helper-ack')acknowledgements.push(value.value);}}});
    helper.stderr.on('data',chunk=>stderr.push(String(chunk)));
    let browser;
    try {
        await until(()=>{if(helper.exitCode!==null)throw Error(stderr.join(''));return messages.some(x=>x.event==='ready');});
        browser=await chromium.launch(process.env.GAO_PLAYWRIGHT_CHROME?{executablePath:process.env.GAO_PLAYWRIGHT_CHROME}:{channel:'chrome'});
        const context=await browser.newContext(),errors=[],requests=[],httpStatuses=[];
        await context.grantPermissions(['local-network-access'],{origin});
        await context.addInitScript(()=>{try{localStorage.setItem('gao-pos-print-v1:123:456',JSON.stringify({mode:'helper',copies:2}));}catch{/* Initial opaque about:blank has no storage. */}});
        if(lostTabStorage)await context.addInitScript(()=>{
            if(!location.pathname.startsWith('/admin/pos/receipt/'))return;
            const getItem=Storage.prototype.getItem;
            Storage.prototype.getItem=function(key){return String(key).startsWith('gao-pos-payment-print-v1:')?null:getItem.call(this,key);};
            window.BroadcastChannel=class{constructor(){throw Error('Synthetic unavailable background channel');}};
        });
        context.on('page',tab=>tab.on('pageerror',error=>errors.push(error.message)));
        context.on('request',request=>{if(['http://127.0.0.1:17880/print','http://127.0.0.1:17880/drawer'].includes(request.url()) && request.method()==='POST')requests.push({endpoint:new URL(request.url()).pathname,owner:request.frame().url(),payload:request.postDataJSON()});});
        context.on('response',response=>{if(['http://127.0.0.1:17880/print','http://127.0.0.1:17880/drawer'].includes(response.url()) && response.request().method()==='POST')httpStatuses.push(response.status());});
        await context.route(origin+'/**',route=>{
            const url=new URL(route.request().url());
            if(url.pathname.startsWith('/Admin/'))return route.fulfill({contentType:'text/javascript',body:read('GaoApp.Web/wwwroot'+url.pathname)});
            const json=value=>route.fulfill({contentType:'application/json',body:JSON.stringify(value)});
            if(url.pathname==='/admin/pos/bank-notifications/admin-approvals')return json([]);
            if(url.pathname==='/admin/pos/bank-notifications/pending')return json(transferAvailable?[bankItem]:[]);
            if(url.pathname==='/admin/pos/bank-notifications/confirm') {
                bankConfirmations.push(route.request().postDataJSON());transferAvailable=false;
                return json({orderId:124,qrId:30,finalized:true,paidAmount:10000,remainingAmount:0,printUrl:'/admin/pos/receipt/124'});
            }
            if(url.pathname==='/admin/pos/124/invoice-route') {
                invoiceRoutes.push(route.request().postDataJSON());return json({orderId:124,askBeforePrintingReceipt:false});
            }
            if(url.pathname.startsWith('/admin/pos/receipt/')) {
                const printSetup=url.pathname.endsWith('/125')?{...setup,receipt:overpaidReceipt}:url.pathname.endsWith('/124')?{...setup,receipt:transferReceipt}:setup;
                if(url.searchParams.has('data'))return json(printSetup);
                const body='<!doctype'+read('GaoApp.Web/Areas/Admin/Views/ReceiptTemplates/Print.cshtml').split('<!doctype')[1]
                    .replace('@Model.Receipt.OrderNumber',printSetup.receipt.orderNumber)
                    .replace('@Html.Raw(JsonSerializer.Serialize(Model, ReceiptTemplateService.Json))',JSON.stringify(printSetup))
                    .replaceAll('src="~/','src="/');
                return route.fulfill({contentType:'text/html',body});
            }
            return route.fulfill({contentType:'text/html',body:parentHtml});
        });
        const page=await context.newPage();await page.goto(origin+'/admin/pos');
        // Linux checkout stays in POS and uses one combined print-and-drawer
        // request, exactly like the known-working POS Shift cash slip.
        await page.locator('#checkout').click();
        await until(()=>acknowledgements.length===1);
        await page.waitForFunction(()=>!document.querySelector('iframe[data-pos-local-print]'));
        assert.equal(context.pages().length,1);
        assert.deepEqual(requests.map(x=>x.endpoint),['/print']);
        assert.equal(requests[0].owner,origin+'/admin/pos');
        assert.equal(requests[0].payload.cashDrawer,true);
        assert.deepEqual(acknowledgements[0],{ok:true,protocol:'gao-raster-v1',printed:true,copies:2});
        // History reprint never opens the drawer.
        const opened=context.waitForEvent('page');await page.locator('#reprint').click();const paper=await opened;
        await paper.waitForEvent('close',{timeout:10000});await until(()=>acknowledgements.length===2);
        assert.equal(requests.at(-1).endpoint,'/print');assert.equal(requests.at(-1).payload.cashDrawer,false);
        // Popup denial cannot affect Linux cash printing or its drawer request.
        await page.evaluate(()=>window.open=()=>null);await page.locator('#checkout').click();
        await until(()=>acknowledgements.length===3);
        await page.waitForFunction(()=>!document.querySelector('iframe[data-pos-local-print]'));
        // Overpaid transfer also prints then opens the drawer for giving change.
        await page.locator('#overpaid').click();await until(()=>acknowledgements.length===4);
        await page.waitForFunction(()=>!document.querySelector('iframe[data-pos-local-print]'));
        // Real bank poll -> automatic confirm -> production payment event handler
        // -> default consumer invoice -> print exact QR without a drawer pulse.
        transferAvailable=true;await page.evaluate(()=>PosBankNotifications.refresh());
        await until(()=>acknowledgements.length===5);
        await page.waitForFunction(()=>!document.querySelector('iframe[data-pos-local-print]'));
        assert.equal(requests.length,5);assert.equal(requests.at(-1).owner,origin+'/admin/pos');
        assert.equal(requests.at(-1).payload.cashDrawer,false);
        assert.deepEqual(bankConfirmations,[{receiptId:10,waitId:20,automatic:true}]);
        assert.deepEqual(invoiceRoutes,[{route:1}]);assert.equal(await page.evaluate(()=>invoicePrompts),0);
        assert.equal(await page.evaluate(()=>qrResults.length),1);
        await page.evaluate(()=>PosBankNotifications.refresh());assert.equal(bankConfirmations.length,1);
        assert.equal(context.pages().length,1);assert.deepEqual(await page.evaluate(()=>printErrors),[]);assert.deepEqual(errors,[]);
        assert.deepEqual(httpStatuses,Array(5).fill(200));
        assert.deepEqual(requests.map(x=>x.endpoint),Array(5).fill('/print'));
        const writes=messages.filter(x=>x.event==='device-write');
        assert.deepEqual(writes.map(x=>x.drawerPulses),[1,0,1,1,0]);assert.deepEqual(writes.map(x=>x.copies),[2,2,2,2,2]);
        console.log(JSON.stringify({status:'PASS',actualSuppliedPythonHandler:true,physicalDeviceStubbed:true,lostTabStorage,helperRequests:requests.length,
            printedCopies:writes.map(x=>x.copies),drawerPulses:writes.map(x=>x.drawerPulses),
            automaticQrConfirmed:bankConfirmations.length,invoiceChoicePrompts:await page.evaluate(()=>invoicePrompts),
            scenarios:'Linux hidden cash/overpaid-transfer uses one POS Shift-compatible print-and-drawer job then cleanup; history and exact QR without drawer; blocked popups; automatic QR confirmation and no invoice choice'}));
    } finally {if(browser)await browser.close();helper.kill();}
})().catch(error=>{console.error(error);process.exitCode=1;});
