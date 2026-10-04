const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { randomUUID } = require('node:crypto');
const templates = require('../../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
const source = fs.readFileSync(path.join(__dirname, '../../GaoApp.Web/wwwroot/Admin/js/printing/pos.printing.js'), 'utf8');
const context = {storeId:1,terminalId:2};
const storageKey = 'gao-pos-print-v1:1:2';
function harness() {
    const storage = new Map(), calls = [], completed = [], browser = [], parsed = [];
    const jobs = [], qz = {websocket:{isActive:()=>true},printers:{find:async()=>['TEST']},configs:{create:(printer,options)=>({printer,options})},print:async(config,data)=>jobs.push({config,data})};
    const pixels = new Uint8ClampedArray(576 * 4); pixels.fill(255); pixels[0]=0;
    const canvas = {getContext:()=>({fillRect(){},drawImage(){},getImageData:()=>({data:pixels})})};
    const paper = {querySelectorAll:()=>[], getBoundingClientRect:()=>({width:576,height:1})};
    const printWindow = {document:{fonts:{ready:Promise.resolve()},querySelector:()=>paper,getElementById:()=>null,
        createElement:type=>type==='canvas'?canvas:{}, head:{appendChild(){}}},focus(){},print:()=>browser.push(true)};
    const channels=new Set();let now=Date.now();
    class ClockDate extends Date { static now(){return now;} }
    class Channel {
        constructor(name){this.name=name;channels.add(this);}
        postMessage(data){for(const peer of channels)if(peer!==this&&peer.name===this.name)queueMicrotask(()=>{if(channels.has(peer))peer.onmessage?.({data});});}
        close(){channels.delete(this);}
    }
    const window = {qz,setTimeout:(fn,ms)=>ms===120000?0:setTimeout(fn,ms===2000?5:ms),clearTimeout,
        crypto:{randomUUID},BroadcastChannel:Channel,location:new URL('https://gao.test/pos'),
        history:{replaceState:(_,title,url)=>{window.location=new URL(url,window.location)}}};
    const env = {window,qz,ReceiptTemplates:templates,AbortController,Uint8Array,URL,Date:ClockDate,
        localStorage:{getItem:k=>storage.get(k)||null,setItem:(k,v)=>storage.set(k,v),removeItem:k=>storage.delete(k),
            get length(){return storage.size},key:i=>[...storage.keys()][i]},
        btoa:s=>Buffer.from(s,'binary').toString('base64'),
        DOMParser:class{parseFromString(html){parsed.push(html);return {documentElement:{querySelectorAll:()=>[]}}}},
        XMLSerializer:class{serializeToString(){return '<html xmlns="http://www.w3.org/1999/xhtml"/>'}},
        Image:class{async decode(){}},
        fetch:async(url,options)=>{calls.push({url,options});return {ok:true,json:async()=>({ok:true,protocol:'gao-raster-v1',width:576,printed:true,copies:1})}}};
    vm.runInNewContext(source,env);
    return {api:window.PosPrinting,env,storage,calls,completed,browser,parsed,jobs,printWindow,canvas,
        advance:ms=>{now+=ms},closeChannels:()=>{for(const channel of channels)channel.close()}};
}
const rendered=()=>templates.render({orderNumber:'SYNTHETIC',createdAtUtc:'2026-09-29T00:00:00Z',lines:[]}, templates.builtIns().find(x=>x.key==='itemwide-80').design);

test('offline printing reuses the cashier window instead of opening a blocked second popup', () => {
    const h = harness(), html = [];
    const popup = { closed: false, setTimeout: () => 1, clearTimeout() {}, document: {
        write: value => html.push(value), close() {}, head: { appendChild() {} }, body: { prepend() {} },
        createElement: () => ({ addEventListener() {}, append() {} })
    } };
    h.env.window.open = () => { throw new Error('Second popup blocked'); };
    const result = h.api.openLocal({ orderNumber: 'OFFLINE-TEST', status: 2, payments: [], lines: [] }, context, [], true, true, popup);
    assert.equal(result, popup);
    assert.equal(popup.opener, null);
    assert.equal(html.length, 1);
    assert.match(html[0], /OFFLINE-TEST/);
});

test('offline printing reports blocked windows so the cashier can retry the saved invoice', () => {
    const h = harness(); h.env.window.open = () => null;
    assert.throws(() => h.api.openLocal({ lines: [] }, context, [], true, false, { closed: true }), /Cho phép mở cửa sổ/);
});

test('legacy browser and qz preferences retain their mode; helper persists without a printer; qz requires one',()=>{
    const h=harness();
    assert.equal(h.api.preferences(context).mode,'browser');
    for(const mode of ['browser','qz']){h.storage.set(storageKey,JSON.stringify({mode,printer:'TEST'}));assert.equal(h.api.preferences(context).mode,mode);}
    h.api.savePreferences(context,{mode:'helper',templateKey:'itemwide-80'});
    assert.equal(h.api.preferences(context).mode,'helper');assert.equal(h.api.preferences(context).printer,'');
    assert.equal(h.api.preferences(context).templateKey,'itemwide-80');
    assert.throws(()=>h.api.savePreferences(context,{mode:'qz'}),/Chọn máy in/);
});
test('browser and qz keep their paths and never call localhost',async()=>{
    const h=harness();await h.api.send(rendered(),context,h.printWindow);assert.equal(h.browser.length,1);
    h.api.savePreferences(context,{mode:'qz',printer:'TEST'});const result=await h.api.send(rendered(),context,h.printWindow);
    assert.equal(result.mode,'qz');assert.equal(h.jobs.length,1);assert.equal(h.calls.length,0);
    assert.ok(h.jobs[0].data[0].data.includes(rendered().body));
});
test('helper uses canonical HTML and exact endpoint, coalesces concurrent calls and completes once after ACK',async()=>{
    const h=harness(), html=rendered();h.api.savePreferences(context,{mode:'helper'});
    let acknowledge;
    h.env.fetch=async(url,options)=>{h.calls.push({url,options});if(url.endsWith('/health'))return {ok:true,json:async()=>({ok:true,protocol:'gao-raster-v1',width:576})};
        return new Promise(resolve=>{acknowledge=()=>resolve({ok:true,json:async()=>({ok:true,protocol:'gao-raster-v1',printed:true,copies:1})})});};
    const done=()=>h.completed.push(true), a=h.api.send(html,context,h.printWindow,done), b=h.api.send(html,context,h.printWindow,done);
    await new Promise(resolve=>setImmediate(resolve));assert.equal(h.completed.length,0);assert.equal(h.calls.length,2);
    assert.equal(h.calls[0].url,'http://127.0.0.1:17880/health');assert.equal(h.calls[1].url,'http://127.0.0.1:17880/print');
    const payload=JSON.parse(h.calls[1].options.body);assert.equal(payload.protocol,'gao-raster-v1');assert.equal(payload.cut,true);assert.equal(payload.cashDrawer,false);
    assert.equal(Buffer.from(payload.rasterBase64,'base64')[0],128);assert.equal(h.parsed[0],html.html);
    acknowledge();assert.equal((await a).mode,'helper');await b;await new Promise(resolve=>setTimeout(resolve,5));
    assert.equal(h.completed.length,1);assert.equal(h.browser.length,0);
    await h.api.send(html,context,h.printWindow,done);assert.equal(h.calls.length,2);
});
test('health failure and legacy helper contract fail before print without fallback',async()=>{
    for(const failure of ['offline','old-protocol']){const h=harness();h.api.savePreferences(context,{mode:'helper'});
        h.env.fetch=async()=>{if(failure==='offline')throw Error('offline');return {ok:true,json:async()=>({ok:true})}};
        await assert.rejects(h.api.send(rendered(),context,h.printWindow,()=>h.completed.push(true)),/Không kết nối được Print Helper/);
        assert.equal(h.browser.length,0);assert.equal(h.completed.length,0);assert.equal(h.parsed.length,0);}
});
test('POST failure or invalid acknowledgement never retries, falls back, or closes; manual retry is possible',async()=>{
    for(const failure of ['network','http','invalid-json','missing-ack']){const h=harness();h.api.savePreferences(context,{mode:'helper'});let posts=0;
        h.env.fetch=async url=>{if(url.endsWith('/health'))return {ok:true,json:async()=>({ok:true,protocol:'gao-raster-v1',width:576})};posts++;
            if(failure==='network')throw Error('lost');return {ok:failure!=='http',json:async()=>{if(failure==='invalid-json')throw Error('json');return {ok:true,protocol:'gao-raster-v1'}}};};
        await assert.rejects(h.api.send(rendered(),context,h.printWindow,()=>h.completed.push(true)),/Kiểm tra giấy đã ra/);
        assert.equal(posts,1);assert.equal(h.browser.length,0);assert.equal(h.completed.length,0);
        await assert.rejects(h.api.send(rendered(),context,h.printWindow),/Kiểm tra giấy đã ra/);assert.equal(posts,2);}
});
test('unsupported paper and oversized or blank raster are rejected before POST',async()=>{
    for(const kind of ['paper','long','blank']){const h=harness();h.api.savePreferences(context,{mode:'helper'});const html=rendered();
        if(kind==='paper')html.size={width:105,height:148};
        if(kind==='long')h.printWindow.document.querySelector=()=>({querySelectorAll:()=>[],getBoundingClientRect:()=>({width:300,height:10000})});
        if(kind==='blank')h.canvas.getContext=()=>({fillRect(){},drawImage(){},getImageData:()=>({data:new Uint8ClampedArray(576*4).fill(255)})});
        await assert.rejects(h.api.send(html,context,h.printWindow));assert.equal(h.calls.filter(x=>x.options.method==='POST').length,0);}
});
test('helper copies are bounded, and timeout is ambiguous without automatic retry or close',async()=>{
    const h=harness();h.api.savePreferences(context,{mode:'helper',copies:99});assert.equal(h.api.preferences(context).copies,5);
    h.api.savePreferences(context,{mode:'helper',copies:1});let posts=0;
    h.env.window.setTimeout=(fn,ms)=>setTimeout(fn,ms===45000?1:ms);
    h.env.fetch=async(url,options)=>{
        if(url.endsWith('/health'))return {ok:true,json:async()=>({ok:true,protocol:'gao-raster-v1',width:576})};
        posts++;return new Promise((resolve,reject)=>options.signal.addEventListener('abort',()=>reject(Error('timeout'))));
    };
    await assert.rejects(h.api.send(rendered(),context,h.printWindow,()=>h.completed.push(true)),/Chưa xác nhận kết quả in/);
    assert.equal(posts,1);assert.equal(h.browser.length,0);assert.equal(h.completed.length,0);
});

const finalized = (payments, changeDue=0) => ({orderId:123,status:'Completed',payments,changeDue});
const cash = amount => ({method:'Cash',amount});
const transfer = (amount, reference='MANUAL') => ({method:'BankTransfer',amount,reference});
const drawerCases = [
    ['cash only',finalized([cash(100)]),true],
    ['cash plus transfer',finalized([cash(40),transfer(60)]),true],
    ['transfer exact',finalized([transfer(100)]),false],
    ['QR exact',finalized([transfer(100,'QR-123')]),false],
    ['transfer overpay and positive change',finalized([transfer(120)],20),true],
    ['QR overpay and positive change',finalized([transfer(120,'QR-123')],20),true],
    ['no money and no change',finalized([]),false],
    ['deposit is not cash received now', {...finalized([]),depositAmount:100},false],
    ['card is not cash or transfer',finalized([{method:'Card',amount:120}],20),false],
    ['zero cash does not open drawer',finalized([cash(0),transfer(100)]),false]
];
for(const [name,receipt,expected] of drawerCases){
    test(`${name}: finalized numeric state controls only the helper cashDrawer boolean`,async()=>{
        const h=harness();h.api.savePreferences(context,{mode:'helper'});
        h.env.window.location=new URL(h.api.postPaymentUrl('/admin/pos/receipt/123',123),'https://gao.test');
        const drawer=await h.api.consumeCashDrawer(receipt);
        assert.equal(drawer,expected);
        await h.api.send(rendered(),context,h.printWindow,null,drawer);
        const payload=JSON.parse(h.calls.find(x=>x.options.method==='POST').options.body);
        assert.equal(payload.cashDrawer,expected);assert.equal(typeof payload.cashDrawer,'boolean');
        assert.deepEqual(Object.keys(payload).sort(),['cashDrawer','copies','cut','height','protocol','rasterBase64','width']);
        assert.equal(await h.api.consumeCashDrawer(receipt),false,'No second use after receipt entry');
    });
}
test('reprint cash, reprint overpaid transfer, preview and test have no drawer intent',async()=>{
    for(const receipt of [finalized([cash(100)]),finalized([transfer(120)],20)]){
        const h=harness();h.api.savePreferences(context,{mode:'helper'});
        assert.equal(await h.api.consumeCashDrawer(receipt),false);
        assert.equal(h.api.cashDrawerFor(receipt),false);
        await h.api.send(rendered(),{...context,receipt,autoPrint:true,cashDrawer:true},h.printWindow);
        assert.equal(JSON.parse(h.calls.at(-1).options.body).cashDrawer,false,'Receipt contents/context cannot auto-enable drawer');
    }
});
test('handoff is bound to one order, expires, and cannot be recovered from history or a copied URL',async()=>{
    for(const kind of ['consumed','wrong-order','expired','draft','refunded','missing']){
        const h=harness(),url=h.api.postPaymentUrl('/admin/pos/orders/123/print?autoPrint=true',123);
        h.env.window.location=new URL(url,'https://gao.test');
        const receipt=finalized([cash(100)]);
        if(kind==='consumed')assert.equal(await h.api.consumeCashDrawer(receipt),true);
        if(kind==='wrong-order')receipt.orderId=124;
        if(kind==='expired')h.advance(120001);
        if(kind==='draft')receipt.status='Draft';
        if(kind==='refunded')receipt.status='Refunded';
        if(kind==='missing')h.closeChannels();
        h.env.window.location=new URL(url,'https://gao.test');
        assert.equal(await h.api.consumeCashDrawer(receipt),false,kind);
        assert.equal(h.env.window.location.hash,'');assert.equal(h.storage.size,0);
    }
});
test('noncanonical numeric inputs and nonboolean intent cannot open the drawer',async()=>{
    const h=harness();
    for(const amount of ['100',true,null,NaN,Infinity,-1]){
        assert.equal(h.api.cashDrawerFor(finalized([cash(amount)]),true),false);
        assert.equal(h.api.cashDrawerFor(finalized([transfer(120)],amount),true),false);
    }
    for(const flag of [undefined,null,0,1,'true',{},[]]){
        const run=harness();run.api.savePreferences(context,{mode:'helper'});
        assert.equal(run.api.cashDrawerFor(finalized([cash(100)]),flag),false);
        await run.api.send(rendered(),context,run.printWindow,null,flag);
        assert.equal(JSON.parse(run.calls.at(-1).options.body).cashDrawer,false);
    }
});
test('true and false helper jobs differ only in cashDrawer; no raw command from context',async()=>{
    const payloads=[];
    for(const flag of [false,true]){
        const h=harness();h.api.savePreferences(context,{mode:'helper'});
        await h.api.send(rendered(),{...context,rawCommand:'ESC p',cashDrawerCommand:'anything'},h.printWindow,null,flag);
        payloads.push(JSON.parse(h.calls.at(-1).options.body));
    }
    assert.deepEqual(payloads[1],{...payloads[0],cashDrawer:true});
});
test('handoff never redirects to external or unrelated URLs; unavailable channel keeps printing safe',async()=>{
    const h=harness();
    for(const url of ['https://evil.test/admin/pos/receipt/123','/admin/pos/receipt/124','/admin/receipt-templates'])
        assert.equal(h.api.postPaymentUrl(url,123),url);
    h.env.window.BroadcastChannel=class{constructor(){throw Error('blocked')}};
    assert.equal(h.api.postPaymentUrl('/admin/pos/receipt/123',123),'/admin/pos/receipt/123');
    assert.equal(await h.api.consumeCashDrawer(finalized([cash(100)])),false);
});

test('actual invoice-intent popup preserves post-payment intent once; restored/history entry stays false',async()=>{
    const h=harness(), navigations=[];
    const app=fs.readFileSync(path.join(__dirname,'../../GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js'),'utf8');
    const popup=()=>({closed:false,opener:{},location:{replace:url=>navigations.push(url)},close(){this.closed=true}});
    h.env.window.open=()=>popup();
    h.env.window.PosOffline={print:()=>false,resolveOrderId:id=>id,pendingInvoiceIntentOrderId:()=>null,invoiceIntentStatus:()=>null};
    const env={window:h.env.window,Number,encodeURIComponent,checkoutFeedback:null,invoiceIntentErrorBox:null,invoiceIntentOrderText:{},
        invoiceIntentModal:{show(){},hide(){}},btnInvoiceIntentManual:null,btnInvoiceIntentAutomatic:null,
        pendingReceiptPrint:null,pendingAskBeforePrintingReceipt:false,document:{getElementById:()=>null},pendingInvoiceIntentOrderId:null,pendingPostPaymentPrintOrderId:null,invoiceIntentBusy:false,posState:{ui:{}},
        showError:message=>{throw Error(message)},postJson:async()=>({orderId:123})};
    vm.runInNewContext(app.slice(app.indexOf('function clearInvoiceIntentError()'),app.indexOf('        function nowIso()')),env);
    env.openReceiptPrint(123,'80',true,true);
    await env.submitInvoiceIntent(1);
    h.env.window.location=new URL(navigations[0],'https://gao.test');
    assert.equal(await h.api.consumeCashDrawer(finalized([cash(100)])),true);
    env.openReceiptPrint(123);await env.submitInvoiceIntent(1);
    assert.equal(navigations[1],'/admin/pos/receipt/123');
    h.env.window.PosOffline.pendingInvoiceIntentOrderId=()=>123;
    env.restorePendingInvoiceIntent();await env.submitInvoiceIntent(1);
    assert.equal(navigations[2],'/admin/pos/receipt/123');
});

test('current-cart finalize passes an immediate print intent only after success',async()=>{
    const source=fs.readFileSync(path.join(__dirname,'../../GaoApp.Web/wwwroot/Admin/js/pos/pos.order.js'),'utf8');
    const start=source.indexOf('        async function finalizeCurrentCart()');
    const end=source.indexOf('\n        async function ',start+1);
    const printed=[];let confirmation;
    const env={window:{},posState:{},btnConfirmAction:{},confirmModal:{hide(){}},refreshLocksSafe(){},
        openConfirmModal:options=>{confirmation=options},postJson:async()=>({orderId:123}),
        runPosAction:async(_,key,action,options)=>options.onSuccess(await action()),
        applyScreenSuccess:async options=>options.beforeRefresh(),openReceiptPrint:(...args)=>printed.push(args)};
    vm.runInNewContext(source.slice(start,end),env);
    await env.finalizeCurrentCart();assert.equal(printed.length,0);
    await confirmation.onConfirm();assert.deepEqual(printed,[[123,'80',true,true,undefined,false]]);
    env.postJson=async()=>{throw Error('not finalized')};
    await assert.rejects(confirmation.onConfirm(),/not finalized/);assert.equal(printed.length,1);
});
test('actual offline print forwards explicit immediate intent, while history defaults to false',()=>{
    const source=fs.readFileSync(path.join(__dirname,'../../GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.js'),'utf8');
    const opened=[];
    const env={state:{orders:{123:{...finalized([cash(100)]),status:2,invoiceIntent:{route:1}}},context:{}},
        localMode:()=>true,core:{permission(){}},window:{PosPrinting:{openLocal:(...args)=>opened.push(args)}}};
    vm.runInNewContext(source.slice(source.indexOf('    function print('),source.indexOf('    function exportPending()')),env);
    assert.equal(env.print(123,true),true);assert.equal(opened[0][4],true);
    assert.equal(env.print(123),true);assert.equal(opened[1][4],false);
    assert.equal(env.print(123,'true'),true);assert.equal(opened[2][4],false);
});
test('no channel to the live POS means fail closed and remove the receipt URL marker',async()=>{
    const h=harness();h.env.window.location=new URL(h.api.postPaymentUrl('/admin/pos/receipt/123',123),'https://gao.test');
    delete h.env.window.BroadcastChannel;
    assert.equal(await h.api.consumeCashDrawer(finalized([cash(100)])),false);assert.equal(h.env.window.location.hash,'');
});

test('standalone drawer requires helper mode and capability, and posts only a boolean contract',async()=>{
    const h=harness();await assert.rejects(h.api.openDrawer(context),/Chọn Linux Print Helper/);
    h.api.savePreferences(context,{mode:'helper'});await assert.rejects(h.api.openDrawer(context),/cập nhật Print Helper/);
    h.env.fetch=async(url,options)=>{h.calls.push({url,options});return {ok:true,json:async()=>({ok:true,protocol:'gao-raster-v1',width:576,cashDrawerOnly:true,drawerOpened:true})}};
    await h.api.openDrawer(context);
    const post=h.calls.find(x=>x.options.method==='POST');assert.equal(post.url,'http://127.0.0.1:17880/drawer');
    assert.deepEqual(JSON.parse(post.options.body),{protocol:'gao-raster-v1',cashDrawer:true});
    assert.equal(h.browser.length,0);assert.equal(h.parsed.length,0);
});
test('standalone drawer failure or missing ACK never retries or falls back to printing',async()=>{
    for(const kind of ['network','http','ack']){
        const h=harness();h.api.savePreferences(context,{mode:'helper'});let posts=0;
        h.env.fetch=async url=>{if(url.endsWith('/health'))return {ok:true,json:async()=>({ok:true,protocol:'gao-raster-v1',width:576,cashDrawerOnly:true})};
            posts++;if(kind==='network')throw Error('lost');return {ok:kind!=='http',json:async()=>({ok:true,protocol:'gao-raster-v1'})}};
        await assert.rejects(h.api.openDrawer(context),/Kiểm tra két/);assert.equal(posts,1);assert.equal(h.browser.length,0);
    }
});

