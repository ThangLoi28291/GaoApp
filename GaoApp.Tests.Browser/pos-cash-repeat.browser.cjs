'use strict';
// Real payment owners, invoice defaults and receipt transport; synthetic checkout
// responses and a loopback Print Helper. No database, bank or physical drawer.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..'), origin = 'https://gao-cash.test';
const channelAvailable = process.env.GAO_CASH_NO_CHANNEL !== '1';
const delayedDrawerGrant = process.env.GAO_CASH_DELAYED_GRANT === '1';
const lostChannelMessages = process.env.GAO_CASH_LOST_MESSAGES === '1';
const childCloseIgnored = process.env.GAO_CASH_CHILD_CLOSE_IGNORED === '1';
const closeRequiresOpener = process.env.GAO_CASH_CLOSE_REQUIRES_OPENER === '1';
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const templates = require('../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
const receipt = { orderId: 123, orderNumber: 'TEST-CASH-123', storeName: 'CỬA HÀNG THỬ NGHIỆM',
    createdAtUtc: '2026-10-15T02:20:00Z', subtotal: 131000, grandTotal: 131000, paidTotal: 200000, changeDue: 69000, status: 'Completed',
    lines: [{ itemName: 'Sản phẩm thử nghiệm', quantity: 1, unitPrice: 131000, lineTotal: 131000 }], payments: [{ method: 'Cash', amount: 200000 }] };
const posts = [], drawers = [];
let finishPrint, failPrint = false;
const server = http.createServer((req, res) => {
    if (req.headers.origin !== origin) { res.writeHead(403); res.end(); return; }
    res.setHeader('Access-Control-Allow-Origin', origin); res.setHeader('Vary', 'Origin');
    if (req.method === 'OPTIONS') {
        res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS'); res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
        res.setHeader('Access-Control-Allow-Private-Network', 'true'); res.writeHead(204); res.end(); return;
    }
    res.setHeader('Content-Type', 'application/json');
    if (req.url === '/health') { res.end(JSON.stringify({ ok: true, protocol: 'gao-raster-v1', width: 576, cashDrawerOnly: true })); return; }
    if(req.url==='/drawer'){let body='';req.on('data',chunk=>body+=chunk);req.on('end',()=>{drawers.push(JSON.parse(body));res.end(JSON.stringify({ok:true,protocol:'gao-raster-v1',drawerOpened:true}));});return;}
    assert.equal(req.url, '/print');
    let body = ''; req.on('data', chunk => body += chunk); req.on('end', () => {
        const payload = JSON.parse(body); posts.push(payload);
        finishPrint = () => { res.statusCode = failPrint ? 503 : 200; res.end(JSON.stringify({ ok: !failPrint, protocol: 'gao-raster-v1', printed: !failPrint, copies: payload.copies })); };
    });
});
const view = read('GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml');
const paymentStart = view.indexOf('<div class="modal fade pos-payment-workspace-modal"');
const paymentEnd = view.lastIndexOf('<div class="modal fade"', view.indexOf('id="confirmModal"'));
const paymentMarkup = view.slice(paymentStart, paymentEnd).replace(/@if\s*\([^\n]*\)\s*\{/g, '').replace(/^\s*}\s*$/gm, '');
const invoiceStart = view.lastIndexOf('<div class="modal fade"', view.indexOf('id="invoiceIntentModal"'));
const invoiceMarkup = view.slice(invoiceStart, view.indexOf('<!--', invoiceStart));
const app = read('GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js');
const invoiceLogic = app.slice(app.indexOf('function clearInvoiceIntentError()'), app.indexOf('        function nowIso()'));
const invoiceBindings = app.slice(app.indexOf('        function bindInvoiceIntentEvents()'), app.indexOf('function bindModuleEvents()'));
const printBindings = app.slice(app.indexOf('function bindModuleEvents()'), app.indexOf('    posCustomer.bindEvents();')) + '}';
const scripts = ['vendor/js/bootstrap.js', 'Admin/js/pos/pos.common.js', 'Admin/js/pos/pos.checkout-feedback.js',
    'Admin/js/pos/pos.payment.js', 'Admin/js/printing/receipt.templates.js', 'Admin/js/printing/print.lifecycle.js', 'Admin/js/printing/pos.printing.js',
    'Admin/js/printing/shift.cash-printing.js']
    .map(file => `<script src="/${file}"></script>`).join('');
const html = `<!doctype html><html lang="vi"><meta charset="utf-8"><style>${read('GaoApp.Web/wwwroot/vendor/css/core.css')}</style>
<body><main id="posShell" data-store-id="123" data-terminal-id="456"><input id="barcode" placeholder="Barcode"><h1>POS thử nghiệm</h1></main>${paymentMarkup}${invoiceMarkup}${scripts}<script>
const noop = () => {};
window.calls = []; window.printErrors = []; window.windowOpenCount = 0;
const actualOpen=window.open.bind(window);
window.open = (...args) => { window.windowOpenCount++; return new URLSearchParams(location.search).has('blocked')?null:actualOpen(...args); };
const focusBarcodeInput = () => document.getElementById('barcode').focus();
const hasAnyModalOpen = () => !!document.querySelector('.modal.show');
const showError = message => printErrors.push(message);
const posState = window.posState = { business: { currentDraft: { orderId:123,status:0,subtotal:131000,grandTotal:131000,paidTotal:0,balanceDue:131000,payments:[] } },
    ui: {modals:{}}, network: {isOnline:true,pendingActions:new Set(),busyScopes:new Set()} };
const postJson = async (url,body) => {
    calls.push({url,body});
    if (url.endsWith('/payments')) { await new Promise(resolve => window.receiveCash = resolve); return {orderId:123,status:0,grandTotal:131000,paidTotal:200000,balanceDue:0,changeDue:69000,payments:[{method:'Cash',amount:200000}]}; }
    if (url.endsWith('/finalize')) await new Promise(resolve => window.finishSale = resolve);
    return {orderId:123,askBeforePrintingReceipt:false};
};
const getJson = async url => (await fetch(url)).json();
const invoiceIntentModalEl = document.getElementById('invoiceIntentModal');
const invoiceIntentOrderText = document.getElementById('invoiceIntentOrderText');
const invoiceIntentErrorBox = document.getElementById('invoiceIntentErrorBox');
const btnInvoiceIntentAutomatic = document.getElementById('btnInvoiceIntentAutomatic');
const invoiceIntentModal = new bootstrap.Modal(invoiceIntentModalEl,{backdrop:'static',keyboard:false});
let pendingReceiptPrint=null,pendingAskBeforePrintingReceipt=false,pendingInvoiceIntentOrderId=null,pendingPostPaymentPrintOrderId=null,
    invoiceIntentBusy=false,receiptPrintActive=false,receiptPrintClosing=false;
const receiptPrintQueue=[],postPaymentPrintOrders=new Set(),preparedReceiptPrintWindows=new Map();
window.PosOffline={print:(id,postPayment,preopenedWindow)=>{
    if(!new URLSearchParams(location.search).has('offline'))return false;
    PosPrinting.openLocal(${JSON.stringify({...receipt,status:2})},{storeId:123,terminalId:456},[],true,postPayment,preopenedWindow);return true;
},resolveOrderId:id=>id,pendingInvoiceIntentOrderId:()=>null,invoiceIntentStatus:()=>null};
const checkoutFeedback=PosCheckoutFeedback.create({modal:invoiceIntentModalEl,submit:submitInvoiceIntent,choosePrint:chooseReceiptPrint,isBusy:()=>invoiceIntentBusy});
${invoiceLogic}
${invoiceBindings}
${printBindings}
bindInvoiceIntentEvents();bindModuleEvents();
const modalElement = document.getElementById('paymentModal');
window.payment=PosPayment.create({posState,
    elements:Object.fromEntries(['payMethod','payAmount','payReference','payProvider','btnAddPayment','btnFinalizeFromPaymentModal','btnPayExact'].map(id=>[id,document.getElementById(id)]).concat([['paymentModalEl',modalElement]])),
    modals:{paymentModal:new bootstrap.Modal(modalElement)},helpers:{postJson,runPosAction:PosCommon.runPosAction,
        renderPaymentModalDraft:noop,renderPaymentPreview:noop,showSuccess:noop,showError,
        applyDraftActionSuccess:({draft,afterSync})=>{posState.business.currentDraft=draft;afterSync?.(draft);},
        applyScreenActionSuccess:async({beforeRefresh})=>{await beforeRefresh?.();posState.business.currentDraft={orderId:124,status:0,grandTotal:0,balanceDue:0,payments:[]};focusBarcodeInput();},
        requestScreenRefresh:async()=>{},renderPayments:noop,syncDraftToUi:noop,focusBarcodeInput,openReceiptPrint,prepareReceiptPrint,discardReceiptPrint}});
payment.bindEvents();payment.openPaymentModal();
</script></body></html>`;
(async () => {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(17880, '127.0.0.1', resolve); });
    const browser = await chromium.launch(process.env.GAO_PLAYWRIGHT_CHROME ? { executablePath: process.env.GAO_PLAYWRIGHT_CHROME } : { channel:'chrome' });
    try {
        const context = await browser.newContext({ viewport: {width:1366,height:900}, reducedMotion:'reduce' });
        await context.grantPermissions(['local-network-access'], {origin});
        await context.addInitScript(useChannel => { if (!useChannel) window.BroadcastChannel=undefined; localStorage.setItem('gao-pos-print-v1:123:456',JSON.stringify({mode:'helper',copies:1,templateKey:'itemwide-80'})); }, channelAvailable);
        if (closeRequiresOpener) await context.addInitScript(() => {
            // Emulate Linux browser versions which require an opener to close
            // a script-created top-level print tab after navigation.
            if (window.parent !== window) return;
            const nativeClose=window.close.bind(window);
            window.close=()=>{if(window.opener)nativeClose();};
        });
        if (delayedDrawerGrant || lostChannelMessages) await context.addInitScript(drop => {
            const NativeChannel = window.BroadcastChannel;
            window.BroadcastChannel = class extends NativeChannel {
                postMessage(data) {
                    if (drop) return;
                    if (data.token && data.requestId && !data.kind) setTimeout(() => super.postMessage(data), 2300);
                    else super.postMessage(data);
                }
            };
        }, lostChannelMessages);
        const page = await context.newPage(), errors = [], helperRequestOwners = [];
        context.on('request', request => {
            if (request.method() === 'POST' && request.url() === 'http://127.0.0.1:17880/print')
                helperRequestOwners.push(request.frame().url());
        });
        context.on('page', tab => tab.on('pageerror', error => errors.push(error.message)));
        page.on('pageerror', error => errors.push(error.message));
        await context.route(origin+'/**', async route => {
            const url = new URL(route.request().url());
            if (url.pathname.startsWith('/Admin/') || url.pathname.startsWith('/vendor/')) {
                let body=read('GaoApp.Web/wwwroot'+url.pathname);
                if (childCloseIgnored && url.pathname.endsWith('/print.lifecycle.js'))
                    body += '\nif (window.parent===window && location.pathname.startsWith("/admin/pos/receipt/")) GaoPrintLifecycle.close=()=>{};';
                return route.fulfill({contentType:'text/javascript',body});
            }
            if (url.pathname.startsWith('/admin/pos/receipt/')) {
                const setup={receipt,storeId:123,terminalId:456,templates:[],autoPrint:true,canManageTemplates:false,
                    receiptDefault:{template:templates.builtIns().find(x=>x.key==='itemwide-80')}};
                if(url.searchParams.get('data')==='true')return route.fulfill({contentType:'application/json',body:JSON.stringify(setup)});
                const body='<!doctype'+read('GaoApp.Web/Areas/Admin/Views/ReceiptTemplates/Print.cshtml').split('<!doctype')[1]
                    .replace('@Model.Receipt.OrderNumber',receipt.orderNumber)
                    .replace('@Html.Raw(JsonSerializer.Serialize(Model, ReceiptTemplateService.Json))',JSON.stringify(setup))
                    .replaceAll('src="~/','src="/').replace(/@if[^\n]+\{[^\n]+\}/,'');
                return route.fulfill({contentType:'text/html',body});
            }
            return route.fulfill({contentType:'text/html',body:html});
        });
        const output=path.join(root,'.artifacts/pos-recovery-drawer/cash-browser');fs.mkdirSync(output,{recursive:true});
        // Establish the known-working reference using the actual POS Shift module.
        await page.goto(origin+'/admin/pos');
        await page.waitForFunction(()=>!!window.PosShiftCashPrinting);
        await page.evaluate(()=>{
            window.shiftPrinted = false;
            PosShiftCashPrinting.print({id:1,type:'CashIn',amount:10000,createdAtUtc:'2026-10-15T01:00:00Z',posShiftId:1,reason:'Test'},
                {storeId:123,terminalId:456},true).then(()=>window.shiftPrinted=true);
        });
        for(let i=0;i<150&&!finishPrint;i++)await new Promise(resolve=>setTimeout(resolve,50));
        assert.equal(posts.length,1);assert.equal(posts[0].cashDrawer,true);
        finishPrint();await page.waitForFunction(()=>shiftPrinted);
        assert.equal(helperRequestOwners[0],origin+'/admin/pos');
        posts.length=0;helperRequestOwners.length=0;
        for (const [index,failure] of [false,true,true,false].entries()) {
            failPrint=failure;finishPrint=null;
            await page.goto(origin+'/admin/pos'+(index===2?'?offline=1':index===3?'?blocked=1':''));
            await page.waitForFunction(()=>document.activeElement?.id==='payAmount');
            await page.locator('#payAmount').fill('200000');
            await page.keyboard.down('Enter'); await page.keyboard.down('Enter'); await page.keyboard.up('Enter');
            for(let i=0;i<8;i++)await page.keyboard.press('Enter');
            assert.equal(context.pages().length,1,'Linux Helper must not open a receipt tab');
            assert.equal(await page.evaluate(()=>calls.length),1,JSON.stringify(await page.evaluate(()=>({errors:printErrors,method:document.getElementById('payMethod').value,amount:document.getElementById('payAmount').value,inline:document.getElementById('paymentInlineErrorBox')?.textContent,modal:document.getElementById('paymentModal').className,busy:[...posState.network.busyScopes],pending:[...posState.network.pendingActions]})))+' pageErrors='+JSON.stringify(errors));
            await page.evaluate(()=>receiveCash());
            await page.waitForFunction(()=>typeof finishSale==='function');
            for(let i=0;i<8;i++)await page.keyboard.press(i%2?'Enter':'Control+Enter');
            await page.evaluate(()=>document.getElementById('btnFinalizeFromPaymentModal').dispatchEvent(new Event('click')));
            assert.equal(await page.evaluate(()=>calls.filter(c=>c.url.endsWith('/finalize')).length),1);
            await page.evaluate(()=>finishSale());
            await page.waitForFunction(()=>posState.business.currentDraft.orderId===124);
            const expected=index+1;
            for(let i=0;i<150&&posts.length<expected;i++)await new Promise(resolve=>setTimeout(resolve,50));
            assert.equal(posts.length,expected);assert.equal(posts.at(-1).cashDrawer,true);
            assert.equal(drawers.length,0,'Drawer pulse belongs to the same /print payload as the bill');
            assert.equal(helperRequestOwners.at(-1),page.url(),'Linux checkout requests originate in POS, like POS Shift');
            await page.evaluate(()=>{const input=document.getElementById('payAmount');for(let i=0;i<8;i++)input.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true,cancelable:true}));});
            assert.equal(await page.evaluate(()=>calls.filter(c=>c.url.endsWith('/payments')).length),1);
            assert.equal(await page.evaluate(()=>calls.filter(c=>c.body?.route===1).length),1);
            finishPrint();
            if(failure){
                await page.waitForFunction(()=>printErrors.length===1);
                await page.waitForFunction(()=>!document.querySelector('iframe[data-pos-payment-order],iframe[data-pos-local-print]'));
                assert.equal(await page.evaluate(()=>document.activeElement.id),'barcode');
                await page.locator('#barcode').fill('SAN-PHAM-DON-MOI');
                assert.equal(await page.locator('#barcode').inputValue(),'SAN-PHAM-DON-MOI');
                await page.screenshot({path:path.join(output,index===2?'offline-print-warning.png':'online-print-warning.png'),animations:'disabled'});
            }else{
                await page.waitForFunction(()=>!document.querySelector('iframe[data-pos-payment-order],iframe[data-pos-local-print]'));
                assert.equal(await page.evaluate(()=>printErrors.length),0);
            }
            assert.equal(await page.evaluate(()=>windowOpenCount),0);
            assert.equal(drawers.length,0);
            assert.equal(context.pages().length,1,'Helper completion cleans the hidden paper and leaves POS open');
            assert.equal(posts.length,expected,'No automatic retry after ambiguous helper failure');
        }
        assert.deepEqual(errors,[]);
        console.log(JSON.stringify({status:'PASS',channelAvailable,scenarios:'Linux hidden printing and drawer in the same /print job as actual POS Shift; repeated/held Enter and Ctrl+Enter; one collection/finalize/invoice/print per sale; uncertain results never retry or send a second drawer job; POS remains ready.',helperPosts:posts.length,drawerPosts:drawers.length,physicalHardware:false}));
    }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(error=>{console.error(error);server.close();process.exitCode=1;});
