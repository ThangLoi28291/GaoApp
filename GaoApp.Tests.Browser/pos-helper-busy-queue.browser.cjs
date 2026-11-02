'use strict';
// Production printing and the supplied Python Handler run unchanged. Only USB
// output is replaced; a delayed return after the real 200 response exposes the
// Handler's ACK-before-lock-release window. No sale/payment API is called.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {spawn} = require('node:child_process');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '..');
const helperSource = process.env.GAO_LINUX_HELPER_SOURCE;
if (!helperSource) throw Error('Set GAO_LINUX_HELPER_SOURCE to the supplied server.py');
const origin = 'https://gao-helper-busy.test';
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const python = String.raw`
import importlib.util, json, sys, threading, time
spec = importlib.util.spec_from_file_location('supplied_helper', sys.argv[1])
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
helper.device_ready = lambda: True
state = {'failNext': False}
def controls():
    for line in sys.stdin:
        command = json.loads(line)
        state['failNext'] = command.get('failNext') is True
        print(json.dumps({'event': 'control-ready'}), flush=True)
threading.Thread(target=controls, daemon=True).start()
def memory_device(chunks):
    if state['failNext']:
        state['failNext'] = False
        print(json.dumps({'event': 'device-failure'}), flush=True)
        raise OSError('Synthetic device failure; do not retry this uncertain job.')
    commands = list(chunks)
    print(json.dumps({'event': 'device-write',
        'drawerPulses': commands.count(b'\x1b\x70\x00\x19\xfa'),
        'copies': commands.count(b'\x1b@\x1ba\x00\x1dL\x00\x00\x1dW\x40\x02')}), flush=True)
    time.sleep(0.05)
helper.write_commands = memory_device
original_reply = helper.Handler.reply
def traced_reply(self, status, **value):
    original_reply(self, status, **value)
    if self.path in ('/print', '/drawer'):
        print(json.dumps({'event': 'helper-ack', 'endpoint': self.path,
            'status': status, 'value': {'ok': status == 200, 'protocol': helper.PROTOCOL, **value}}), flush=True)
    if self.path == '/print' and status == 200:
        # This sleep is after bytes have reached Chrome, while PRINT_LOCK is
        # still held by the unchanged do_POST implementation.
        time.sleep(0.5)
helper.Handler.reply = traced_reply
original_post = helper.Handler.do_POST
def traced_post(self):
    original_post(self)
    print(json.dumps({'event': 'handler-complete'}), flush=True)
helper.Handler.do_POST = traced_post
server = helper.ThreadingHTTPServer(('127.0.0.1', 17880), helper.Handler)
server.origins = helper.allowed_origins('${origin}')
print(json.dumps({'event': 'ready'}), flush=True)
server.serve_forever()
`;

const html = `<!doctype html><meta charset="utf-8"><title>Helper queue regression</title>
<input id="nextSale" aria-label="Next sale" value="Ready">
<script src="/Admin/js/printing/receipt.templates.js"></script>
<script src="/Admin/js/printing/print.lifecycle.js"></script>
<script src="/Admin/js/printing/pos.printing.js"></script>
<script>
const context={storeId:123,terminalId:456};
PosPrinting.savePreferences(context,{mode:'helper',copies:2});
window.completed=[]; window.paperResults=[];
window.preparePaper=async orderId=>{
  const receipt={orderId,orderNumber:'QUEUE-'+orderId,storeName:'TEST',status:'Completed',
    createdAtUtc:'2026-10-15T00:00:00Z',subtotal:10000,grandTotal:10000,paidTotal:20000,changeDue:10000,
    lines:[{itemName:'Synthetic cash sale '+orderId,quantity:1,unitPrice:10000,lineTotal:10000}],
    payments:[{method:'Cash',amount:20000}]};
  const rendered=ReceiptTemplates.render(receipt,ReceiptTemplates.builtIns().find(x=>x.key==='itemwide-80').design);
  const frame=document.createElement('iframe');frame.dataset.testPaper=String(orderId);
  frame.style.cssText='position:fixed;left:-10000px;top:0;width:850px;height:850px;border:0';
  await new Promise(resolve=>{frame.onload=resolve;frame.srcdoc=rendered.html;document.body.appendChild(frame);});
  const done=()=>{completed.push(orderId);frame.remove();};
  return {rendered,frame,done};
};
window.sendPaper=paper=>PosPrinting.sendReceipt(paper.rendered,context,paper.frame.contentWindow,paper.done,true);
window.runPair=async base=>{
  const first=await preparePaper(base),second=await preparePaper(base+1);
  const jobs=[sendPaper(first),sendPaper(first),sendPaper(second),sendPaper(second)];
  const results=await Promise.allSettled(jobs);
  paperResults=results.map(x=>x.status==='fulfilled'?{status:x.status}:({status:x.status,
    message:x.reason.message,diagnostic:x.reason.printDiagnostic}));
  return paperResults;
};
</script>`;

async function until(predicate, description = 'helper event') {
    const deadline = Date.now() + 20000;
    while (!predicate()) {
        if (Date.now() > deadline) throw Error('Timed out waiting for ' + description);
        await new Promise(resolve => setTimeout(resolve, 20));
    }
}

(async () => {
    const messages = [], stderr = [], results = [];
    const helper = spawn(process.env.GAO_TEST_PYTHON || 'python', ['-B', '-u', '-c', python, helperSource],
        {windowsHide:true, stdio:['pipe','pipe','pipe']});
    let buffer = '';
    helper.stdout.on('data', chunk => {
        buffer += chunk;
        let boundary;
        while ((boundary = buffer.indexOf('\n')) >= 0) {
            const line = buffer.slice(0,boundary).trim(); buffer = buffer.slice(boundary+1);
            if (line) messages.push(JSON.parse(line));
        }
    });
    helper.stderr.on('data', chunk => stderr.push(String(chunk)));
    let browser;
    const writes = () => messages.filter(x => x.event === 'device-write');
    const acks = () => messages.filter(x => x.event === 'helper-ack');
    const handlers = () => messages.filter(x => x.event === 'handler-complete');
    async function settled() {
        await until(() => handlers().length === acks().length, 'all Helper locks released');
    }
    try {
        await until(() => {
            if (helper.exitCode !== null) throw Error(stderr.join(''));
            return messages.some(x => x.event === 'ready');
        }, 'Python Handler startup');
        browser = await chromium.launch(process.env.GAO_PLAYWRIGHT_CHROME ?
            {executablePath:process.env.GAO_PLAYWRIGHT_CHROME} : {channel:'chrome'});
        for (const locks of [true,false]) {
            const context = await browser.newContext(), errors = [], posts = [];
            await context.grantPermissions(['local-network-access'], {origin});
            if (!locks) await context.addInitScript(() => Object.defineProperty(navigator, 'locks', {value:undefined}));
            context.on('page', page => page.on('pageerror', error => errors.push(error.message)));
            context.on('request', request => {
                if (request.url().startsWith('http://127.0.0.1:17880/') && request.method() === 'POST')
                    posts.push({endpoint:new URL(request.url()).pathname, payload:request.postDataJSON()});
            });
            await context.route(origin+'/**', route => {
                const url = new URL(route.request().url());
                if (url.pathname.startsWith('/Admin/'))
                    return route.fulfill({contentType:'text/javascript', body:read('GaoApp.Web/wwwroot'+url.pathname)});
                return route.fulfill({contentType:'text/html', body:html});
            });
            const page = await context.newPage(); await page.goto(origin+'/pos');
            if (locks) assert.equal(await page.evaluate(() => typeof navigator.locks?.request), 'function');
            const beforeWrites = writes().length, beforeAcks = acks().length;
            const outcome = await page.evaluate(() => runPair(100));
            assert.deepEqual(outcome.map(x=>x.status), Array(4).fill('fulfilled'),
                'Two real cash jobs and same-paper duplicate submissions must all finish: '+JSON.stringify(outcome));
            await page.waitForFunction(() => completed.length === 2 && !document.querySelector('iframe[data-test-paper]'));
            await settled();
            const pairWrites = writes().slice(beforeWrites), pairAcks = acks().slice(beforeAcks);
            assert.deepEqual(pairWrites.map(x=>x.copies), [2,2]);
            assert.deepEqual(pairWrites.map(x=>x.drawerPulses), [1,1]);
            assert.ok(pairAcks.some(x=>x.status === 409), 'Exercise real ACK-before-lock-release busy response');
            assert.deepEqual(pairAcks.filter(x=>x.status === 200).map(x=>x.value.copies), [2,2]);
            assert.ok(posts.every(x=>x.endpoint === '/print' && x.payload.cashDrawer === true && x.payload.copies === 2));
            const pairPosts = posts.length;
            // Separate module realms/tabs share one Helper. Each distinct job
            // must print once; same-paper duplicate calls must still coalesce.
            const second = await context.newPage(); await second.goto(origin+'/pos-two');
            const beforeTabsWrites = writes().length;
            await Promise.all([page,second].map((tab,index) => tab.evaluate(async id => {
                const paper = await preparePaper(id);
                await Promise.all([sendPaper(paper),sendPaper(paper)]);
            },200+index)));
            await Promise.all([page,second].map(tab => tab.waitForFunction(() => !document.querySelector('iframe[data-test-paper]'))));
            await settled();
            assert.deepEqual(writes().slice(beforeTabsWrites).map(x=>[x.copies,x.drawerPulses]), [[2,1],[2,1]]);
            assert.ok(posts.every(x=>x.endpoint === '/print'));
            // Real Handler 503 must not be retried. A following queued job
            // nevertheless prints, proving one failure does not poison the queue.
            const beforeControl = messages.filter(x=>x.event === 'control-ready').length;
            helper.stdin.write(JSON.stringify({failNext:true})+'\n');
            await until(() => messages.filter(x=>x.event === 'control-ready').length === beforeControl+1, 'USB failure fixture');
            const beforeFailureWrites = writes().length, beforeFailureAcks = acks().length;
            const failed = await second.evaluate(() => runPair(300));
            assert.equal(failed.filter(x=>x.status === 'rejected').length,2);
            assert.equal(failed.filter(x=>x.status === 'fulfilled').length,2);
            assert.equal(failed[0].status,failed[1].status,'Duplicate callers share the first result');
            assert.equal(failed[2].status,failed[3].status,'Duplicate callers share the second result');
            assert.equal(failed.find(x=>x.status === 'rejected').diagnostic.httpStatus,503);
            await settled();
            assert.deepEqual(writes().slice(beforeFailureWrites).map(x=>[x.copies,x.drawerPulses]), [[2,1]]);
            assert.deepEqual(acks().slice(beforeFailureAcks).filter(x=>x.status !== 409).map(x=>x.status), [503,200]);
            assert.deepEqual(errors,[]);
            assert.equal(await page.locator('#nextSale').inputValue(),'Ready');
            results.push({locks, samePageJobs:2, samePagePostAttempts:pairPosts,
                acrossTabsJobs:2, queuedFailureRecovered:true, physicalPulsePerSuccessfulJob:1});
            await context.close();
        }
        console.log(JSON.stringify({status:'PASS', actualSuppliedPythonHandler:true,physicalDeviceStubbed:true,
            delayedLockReleaseMs:500,results,realPrintJobs:writes().length,
            drawerPulses:writes().reduce((sum,x)=>sum+x.drawerPulses,0),
            actualBusyResponses:acks().filter(x=>x.status===409).length}));
    } finally {
        if (browser) await browser.close();
        helper.kill();
        await new Promise(resolve => helper.exitCode !== null ? resolve() : helper.once('exit', resolve));
    }
})().catch(error => { console.error(error); process.exitCode=1; });
