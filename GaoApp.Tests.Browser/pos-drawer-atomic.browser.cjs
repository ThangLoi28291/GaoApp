'use strict';
// Reproduce the installed Helper acknowledging paper before releasing its lock.
// Production browser code + supplied Python Handler; USB writes remain in memory.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {spawn} = require('node:child_process');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '..');
const source = process.env.GAO_LINUX_HELPER_SOURCE;
if (!source) throw Error('Set GAO_LINUX_HELPER_SOURCE to the supplied server.py');
const origin = 'https://gao-drawer-atomic.test';
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const python = String.raw`
import importlib.util, json, sys, time
spec = importlib.util.spec_from_file_location('supplied_helper', sys.argv[1])
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
helper.device_ready = lambda: True
def memory_device(chunks):
    commands = list(chunks)
    kinds = ['drawer' if chunk == b'\x1b\x70\x00\x19\xfa' else 'copy' if chunk == b'\x1b@\x1ba\x00\x1dL\x00\x00\x1dW\x40\x02' else 'cut' if chunk == b'\x1dV\x00' else 'data' for chunk in commands]
    print(json.dumps({'event':'device-write', 'commands':kinds}), flush=True)
helper.write_commands = memory_device
original_reply = helper.Handler.reply
def delayed_reply(self, status, **value):
    original_reply(self, status, **value)
    if self.path in ('/print', '/drawer'):
        print(json.dumps({'event':'ack', 'endpoint':self.path, 'status':status}), flush=True)
    if self.path == '/print' and status == 200:
        time.sleep(0.5) # Keep the real PRINT_LOCK after the browser receives ACK.
        print(json.dumps({'event':'lock-releasing'}), flush=True)
helper.Handler.reply = delayed_reply
server = helper.ThreadingHTTPServer(('127.0.0.1',17880), helper.Handler)
server.origins = helper.allowed_origins('${origin}')
print(json.dumps({'event':'ready'}), flush=True)
server.serve_forever()
`;
const receipt = {orderId:123,orderNumber:'TEST-123',storeName:'THỬ NGHIỆM',createdAtUtc:'2026-10-15T00:00:00Z',status:'Completed',
    subtotal:10000,grandTotal:10000,paidTotal:10000,changeDue:0,
    lines:[{itemName:'Hàng thử',quantity:1,unitPrice:10000,lineTotal:10000}],payments:[{method:'Cash',amount:10000}]};
async function until(predicate) {
    const deadline=Date.now()+10000;
    while(!predicate()) {if(Date.now()>deadline) throw Error('Timed out waiting for Helper');await new Promise(resolve=>setTimeout(resolve,20));}
}
(async()=>{
    const messages=[],stderr=[];
    const helper=spawn(process.env.GAO_TEST_PYTHON || 'python',['-B','-u','-c',python,source],{windowsHide:true,stdio:['ignore','pipe','pipe']});
    let buffer='',browser;
    helper.stdout.on('data',chunk=>{buffer+=chunk;let boundary;while((boundary=buffer.indexOf('\n'))>=0){const line=buffer.slice(0,boundary).trim();buffer=buffer.slice(boundary+1);if(line)messages.push(JSON.parse(line));}});
    helper.stderr.on('data',chunk=>stderr.push(String(chunk)));
    try {
        await until(()=>{if(helper.exitCode!==null)throw Error(stderr.join(''));return messages.some(x=>x.event==='ready');});
        browser=await chromium.launch(process.env.GAO_PLAYWRIGHT_CHROME?{executablePath:process.env.GAO_PLAYWRIGHT_CHROME}:{channel:'chrome'});
        const context=await browser.newContext(),requests=[],errors=[];
        await context.grantPermissions(['local-network-access'],{origin});
        await context.addInitScript(()=>{try{localStorage.setItem('gao-pos-print-v1:123:456',JSON.stringify({mode:'helper',copies:2}));}catch{}});
        context.on('request',request=>{if(request.url().startsWith('http://127.0.0.1:17880/')&&request.method()==='POST')requests.push({endpoint:new URL(request.url()).pathname,payload:request.postDataJSON()});});
        const html=`<!doctype html><meta charset="utf-8"><input id="barcode"><script src="/Admin/js/printing/receipt.templates.js"></script><script src="/Admin/js/printing/print.lifecycle.js"></script><script src="/Admin/js/printing/pos.printing.js"></script><script>window.printErrors=[];window.addEventListener('pos:print-error',event=>printErrors.push(event.detail.message));</script>`;
        await context.route(origin+'/**',route=>{const url=new URL(route.request().url());return route.fulfill(url.pathname.startsWith('/Admin/')?{contentType:'text/javascript',body:read('GaoApp.Web/wwwroot'+url.pathname)}:{contentType:'text/html',body:html});});
        const page=await context.newPage();page.on('pageerror',error=>errors.push(error.message));await page.goto(origin+'/admin/pos');
        const scenarios=[
            {name:'cash',receipt,postPayment:true,drawer:true},
            {name:'overpaid-transfer',receipt:{...receipt,orderId:124,paidTotal:12000,changeDue:2000,payments:[{method:'BankTransfer',amount:12000}]},postPayment:true,drawer:true},
            {name:'exact-transfer',receipt:{...receipt,orderId:125,payments:[{method:'BankTransfer',amount:10000}]},postPayment:true,drawer:false},
            {name:'history',receipt,postPayment:false,drawer:false}
        ];
        for(const scenario of scenarios){
            const start=requests.length,writeCount=messages.filter(x=>x.event==='device-write').length;
            await page.evaluate(({receipt,postPayment})=>{
                // A local frame for history avoids popup policy while exercising
                // the same production openLocal transport and cleanup.
                const frame=document.createElement('iframe');frame.dataset.testPaper='true';document.body.appendChild(frame);
                PosPrinting.openLocal(receipt,{storeId:123,terminalId:456},[],false,postPayment,frame.contentWindow);
            },scenario);
            await page.waitForFunction(()=>!document.querySelector('iframe[data-test-paper]'));
            await until(()=>messages.filter(x=>x.event==='lock-releasing').length>=scenarios.indexOf(scenario)+1);
            const actual=requests.slice(start),writes=messages.filter(x=>x.event==='device-write').slice(writeCount);
            const outcome={requests:actual.map(x=>x.endpoint),acks:messages.filter(x=>x.event==='ack').slice(start),writes:writes.map(x=>({copies:x.commands.filter(c=>c==='copy').length,pulses:x.commands.filter(c=>c==='drawer').length}))};
            assert.deepEqual(actual.map(x=>x.endpoint),['/print'],`${scenario.name}: paper and pulse must share one HTTP job; observed ${JSON.stringify(outcome)}`);
            assert.equal(actual[0].payload.cashDrawer,scenario.drawer,`${scenario.name}: explicit drawer policy`);
            assert.equal(writes.length,1);
            const commands=writes[0].commands;
            assert.equal(commands.filter(x=>x==='copy').length,2);
            assert.equal(commands.filter(x=>x==='drawer').length,scenario.drawer?1:0);
            if(scenario.drawer)assert.equal(commands.at(-1),'drawer','drawer pulse follows all printed copies and cuts');
            await page.locator('#barcode').fill(scenario.name);assert.equal(await page.locator('#barcode').inputValue(),scenario.name);
        }
        assert.deepEqual(messages.filter(x=>x.event==='ack').map(x=>x.status),[200,200,200,200]);
        assert.deepEqual(await page.evaluate(()=>printErrors),[]);assert.deepEqual(errors,[]);assert.equal(context.pages().length,1);
        console.log(JSON.stringify({status:'PASS',actualSuppliedPythonHandler:true,physicalDeviceStubbed:true,delayedLockReleaseMs:500,scenarios:scenarios.map(x=>x.name),printJobs:requests.length,standaloneDrawerPosts:requests.filter(x=>x.endpoint==='/drawer').length}));
    } finally {if(browser)await browser.close();helper.kill();await new Promise(resolve=>helper.once('exit',resolve));}
})().catch(error=>{console.error(error);process.exitCode=1;});
