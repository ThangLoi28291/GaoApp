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
    const session = new Map(), navigator = {};
    const window = {qz,setTimeout:(fn,ms)=>[120000,500].includes(ms)?0:setTimeout(fn,ms===2000?5:ms),clearTimeout,
        dispatchEvent(){},navigator,
        sessionStorage:{getItem:k=>session.get(k)||null,setItem:(k,v)=>session.set(k,v),removeItem:k=>session.delete(k)},
        crypto:{randomUUID},BroadcastChannel:Channel,location:new URL('https://gao.test/pos'),
        history:{replaceState:(_,title,url)=>{window.location=new URL(url,window.location)}}};
    const env = {window,navigator,qz,ReceiptTemplates:templates,AbortController,Uint8Array,URL,Date:ClockDate,CustomEvent:class{},
        localStorage:{getItem:k=>storage.get(k)||null,setItem:(k,v)=>storage.set(k,v),removeItem:k=>storage.delete(k),
            get length(){return storage.size},key:i=>[...storage.keys()][i]},
        btoa:s=>Buffer.from(s,'binary').toString('base64'),
        DOMParser:class{parseFromString(html){parsed.push(html);return {documentElement:{querySelectorAll:()=>[]}}}},
        XMLSerializer:class{serializeToString(){return '<html xmlns="http://www.w3.org/1999/xhtml"/>'}},
        Image:class{async decode(){}},
        fetch:async(url,options)=>{calls.push({url,options});return {ok:true,json:async()=>({ok:true,protocol:'gao-raster-v1',width:576,cashDrawerOnly:true,drawerOpened:true,printed:true,copies:1})}}};
    vm.runInNewContext(source,env);
    return {api:window.PosPrinting,env,storage,calls,completed,browser,parsed,jobs,printWindow,canvas,
        advance:ms=>{now+=ms},now:()=>now,closeChannels:()=>{for(const channel of channels)channel.close()}};
}
const rendered=()=>templates.render({orderNumber:'SYNTHETIC',createdAtUtc:'2026-09-29T00:00:00Z',lines:[]}, templates.builtIns().find(x=>x.key==='itemwide-80').design);
const helperResponse = (data, status = 200) => ({ok:status >= 200 && status < 300,status,json:async()=>data});
const helperBusy = () => helperResponse({ok:false,protocol:'gao-raster-v1',error:'Printer is busy; request was not printed.'},409);
function immediateHelperClock(h) {
    const waits = [];
    h.env.window.setTimeout = (fn, ms) => {
        // Keep request timeouts pending; advance only the bounded busy backoff.
        if ([5000,45000,120000].includes(ms)) return 1;
        if (ms > 0) {
            waits.push(ms);
            if (waits.length > 40) throw Error('Busy retry did not stop');
            h.advance(ms);
        }
        queueMicrotask(fn);
        return 1;
    };
    h.env.window.clearTimeout = () => {};
    return waits;
}

test('offline printing reuses the cashier window instead of opening a blocked second popup', () => {
    const h = harness(), html = [];
    const cashier = {};
    const popup = { closed: false, opener: cashier, setTimeout: () => 1, clearTimeout() {}, document: {
        write: value => html.push(value), close() {}, head: { appendChild() {} }, body: { prepend() {} },
        createElement: () => ({ addEventListener() {}, append() {} })
    } };
    h.env.window.open = () => { throw new Error('Second popup blocked'); };
    const result = h.api.openLocal({ orderNumber: 'OFFLINE-TEST', status: 2, payments: [], lines: [] }, context, [], true, true, popup);
    assert.equal(result, popup);
    assert.equal(popup.opener, cashier, 'Keep the checkout owner so Linux browsers can close this tab after printing');
    assert.equal(html.length, 1);
    assert.match(html[0], /OFFLINE-TEST/);
});

test('offline printing reports blocked windows so the cashier can retry the saved invoice', () => {
    const h = harness(); h.env.window.open = () => null;
    assert.throws(() => h.api.openLocal({ lines: [] }, context, [], true, false, { closed: true }), /Cho phép mở cửa sổ/);
});
test('automatic cash checkout sends bill and drawer together in one hidden print job', async () => {
    const h = harness(), frames = [], timer = [];
    h.api.savePreferences(context, { mode: 'helper' });
    const popup = { ...h.printWindow, closed: false, parent: {}, setTimeout: fn => timer.push(fn), clearTimeout() {},
        document: { ...h.printWindow.document, write() {}, close() {}, body: { prepend() {} },
            createElement: type => type === 'canvas' ? h.canvas : { addEventListener() {}, append() {} } } };
    h.env.document = { createElement: () => ({ style: {}, dataset: {}, setAttribute() {}, contentWindow: popup }), body: { appendChild: frame => frames.push(frame) } };
    h.env.window.open = () => { throw Error('Popup blocked without user gesture'); };
    h.env.GaoPrintLifecycle = { close() {} };
    const receipt = { orderId: 123, orderNumber: 'AUTO-CASH', status: 2, payments: [{ method: 'Cash', amount: 100 }], lines: [] };
    assert.equal(h.api.openLocal(receipt, context, [], true, true), popup);
    assert.equal(frames.length, 1); assert.match(frames[0].style.cssText, /width:850px/);
    timer[0](); await new Promise(resolve => setTimeout(resolve, 10));
    const posts = h.calls.filter(x => x.options.method === 'POST');
    assert.deepEqual(posts.map(x=>x.url), ['http://127.0.0.1:17880/print']);
    assert.equal(JSON.parse(posts[0].options.body).cashDrawer, true);
});

test('cash receipt coalesces duplicate submissions and waits for its single print-and-drawer ACK',async()=>{
    const h=harness();h.api.savePreferences(context,{mode:'helper'});
    const originalFetch=h.env.fetch, releases={};
    h.env.fetch=async(url,options)=>{
        const response=await originalFetch(url,options);
        if(options.method==='POST')await new Promise(resolve=>{releases[url.split('/').at(-1)]=resolve});
        return response;
    };
    const done=()=>h.completed.push(true);
    const first=h.api.sendReceipt(rendered(),context,h.printWindow,done,true);
    const second=h.api.sendReceipt(rendered(),context,h.printWindow,done,true);
    await new Promise(resolve=>setImmediate(resolve));
    assert.equal(h.calls.filter(x=>x.options.method==='POST').length,1);assert.equal(h.completed.length,0);
    assert.equal(JSON.parse(h.calls.find(x=>x.options.method==='POST').options.body).cashDrawer,true);
    releases.print();await Promise.all([first,second]);await new Promise(resolve=>setTimeout(resolve,5));
    assert.equal(h.completed.length,1);
    await h.api.sendReceipt(rendered(),context,h.printWindow,done,true);
    assert.equal(h.calls.filter(x=>x.options.method==='POST').length,1);
});

test('combined receipt failure never sends a second drawer job, retries or changes transport automatically',async()=>{
    for(const failure of ['network','http']){
        const h=harness();h.api.savePreferences(context,{mode:'helper'});const originalFetch=h.env.fetch;
        h.env.fetch=async(url,options)=>{
            const response=await originalFetch(url,options);
            if(!url.endsWith('/print'))return response;
            if(failure==='network')throw new TypeError('lost response after device write');
            return {ok:false,status:503,json:async()=>({ok:false})};
        };
        await assert.rejects(h.api.sendReceipt(rendered(),context,h.printWindow,()=>h.completed.push(true),true),/Chưa xác nhận kết quả in/);
        await new Promise(resolve=>setTimeout(resolve,5));
        const posts=h.calls.filter(x=>x.options.method==='POST');
        assert.equal(posts.length,1);assert.equal(posts[0].url,'http://127.0.0.1:17880/print');
        assert.equal(JSON.parse(posts[0].options.body).cashDrawer,true);
        assert.equal(h.completed.length,0);assert.equal(h.browser.length,0);
    }
});

test('cash receipt uses the same combined helper contract as POS Shift without requiring standalone drawer capability',async()=>{
    const h=harness();h.api.savePreferences(context,{mode:'helper'});const originalFetch=h.env.fetch;
    h.env.fetch=async(url,options)=>url.endsWith('/health')?{ok:true,json:async()=>({ok:true,protocol:'gao-raster-v1',width:576})}:originalFetch(url,options);
    await h.api.sendReceipt(rendered(),context,h.printWindow,null,true);
    assert.equal(h.calls.length,1);assert.equal(h.calls[0].url,'http://127.0.0.1:17880/print');
    assert.equal(JSON.parse(h.calls[0].options.body).cashDrawer,true);
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
test('only a verified not-printed busy response is retried, with the same combined payload and one completion',async()=>{
    const h=harness();h.api.savePreferences(context,{mode:'helper',copies:2});const waits=immediateHelperClock(h);
    const posts=[];
    h.env.fetch=async(url,options)=>{
        if(url.endsWith('/health'))return helperResponse({ok:true,protocol:'gao-raster-v1',width:576});
        posts.push({url,body:options.body});
        return posts.length<=2?helperBusy():helperResponse({ok:true,protocol:'gao-raster-v1',printed:true,copies:2});
    };
    const first=h.api.sendReceipt(rendered(),context,h.printWindow,()=>h.completed.push(true),true);
    const duplicate=h.api.sendReceipt(rendered(),context,h.printWindow,()=>h.completed.push(true),true);
    await Promise.all([first,duplicate]);await new Promise(setImmediate);
    assert.equal(posts.length,3);
    assert.ok(posts.every(post=>post.url==='http://127.0.0.1:17880/print'&&post.body===posts[0].body));
    const payload=JSON.parse(posts[0].body);assert.equal(payload.cashDrawer,true);assert.equal(payload.copies,2);
    assert.deepEqual(waits,[250,500]);assert.equal(h.completed.length,1);assert.equal(h.browser.length,0);
    await h.api.sendReceipt(rendered(),context,h.printWindow,null,true);assert.equal(posts.length,3,'ACKed paper cannot be printed again');
});
test('persistent verified busy responses stop within the retry and elapsed-time limits',async()=>{
    const h=harness();h.api.savePreferences(context,{mode:'helper'});const waits=immediateHelperClock(h),started=h.now();let posts=0;
    h.env.fetch=async url=>{
        if(url.endsWith('/health'))return helperResponse({ok:true,protocol:'gao-raster-v1',width:576});
        posts++;return helperBusy();
    };
    await assert.rejects(h.api.sendReceipt(rendered(),context,h.printWindow,()=>h.completed.push(true),true),error=>{
        assert.equal(error.printDiagnostic.httpStatus,409);return true;
    });
    assert.ok(posts>1&&posts<=32,`bounded attempts: ${posts}`);
    assert.equal(waits.length,posts-1);assert.ok(waits.every(ms=>ms>0&&ms<=1000));
    assert.ok(h.now()-started>0&&h.now()-started<=30000,`bounded elapsed: ${h.now()-started}`);
    assert.equal(h.completed.length,0);assert.equal(h.browser.length,0);
});
test('ambiguous or contradictory responses never retry or switch transport',async()=>{
    const cases=[
        ['generic-409',()=>helperResponse({ok:false,protocol:'gao-raster-v1',error:'Printer is busy'},409)],
        ['unknown-outcome',()=>helperResponse({ok:false,protocol:'gao-raster-v1',error:'Printer is busy; request was not printed.',outcome:'unknown'},409)],
        ['already-printed',()=>helperResponse({ok:false,protocol:'gao-raster-v1',error:'Printer is busy; request was not printed.',printed:true},409)],
        ['printed-string',()=>helperResponse({ok:false,protocol:'gao-raster-v1',error:'Printer is busy; request was not printed.',printed:'true'},409)],
        ['printed-number',()=>helperResponse({ok:false,protocol:'gao-raster-v1',error:'Printer is busy; request was not printed.',printed:1},409)],
        ['already-opened-drawer',()=>helperResponse({ok:false,protocol:'gao-raster-v1',error:'Printer is busy; request was not printed.',drawerOpened:true},409)],
        ['drawer-opened-string',()=>helperResponse({ok:false,protocol:'gao-raster-v1',error:'Printer is busy; request was not printed.',drawerOpened:'true'},409)],
        ['copies-present',()=>helperResponse({ok:false,protocol:'gao-raster-v1',error:'Printer is busy; request was not printed.',copies:1},409)],
        ['wrong-protocol',()=>helperResponse({ok:false,protocol:'other',error:'Printer is busy; request was not printed.'},409)],
        ['ok-true',()=>helperResponse({ok:true,protocol:'gao-raster-v1',error:'Printer is busy; request was not printed.'},409)],
        ['malformed-json',()=>({ok:false,status:409,json:async()=>{throw new SyntaxError('JSON')}})],
        ['device-write-failure',()=>helperResponse({ok:false,protocol:'gao-raster-v1',error:'Printer is busy; request was not printed.'},503)],
        ['network',()=>{throw new TypeError('Lost response after write')}],
        ['invalid-ack',()=>helperResponse({ok:true,protocol:'gao-raster-v1',copies:1})]
    ];
    for(const [name,response]of cases){
        const h=harness();h.api.savePreferences(context,{mode:'helper'});const waits=immediateHelperClock(h);let posts=0;
        h.env.fetch=async url=>{
            if(url.endsWith('/health'))return helperResponse({ok:true,protocol:'gao-raster-v1',width:576});
            posts++;return response();
        };
        await assert.rejects(h.api.sendReceipt(rendered(),context,h.printWindow,()=>h.completed.push(true),true),undefined,name);
        assert.equal(posts,1,name);assert.equal(waits.length,0,name);assert.equal(h.completed.length,0,name);assert.equal(h.browser.length,0,name);
    }
});
test('distinct paper jobs are serialized locally and the queue continues after a failed print',async()=>{
    const h=harness();h.api.savePreferences(context,{mode:'helper'});const posts=[],release=[];
    h.env.fetch=async(url,options)=>{
        if(url.endsWith('/health'))return helperResponse({ok:true,protocol:'gao-raster-v1',width:576});
        posts.push({url,body:options.body});
        return new Promise(resolve=>release.push(resolve));
    };
    const secondWindow={...h.printWindow};
    const first=h.api.sendReceipt(rendered(),context,h.printWindow,null,true);
    const firstFailed=assert.rejects(first,/Chưa xác nhận kết quả in/);
    const second=h.api.sendReceipt(rendered(),context,secondWindow,()=>h.completed.push(true),false);
    await new Promise(setImmediate);assert.equal(posts.length,1,'Second paper must not reach a busy Helper');
    assert.equal(JSON.parse(posts[0].body).cashDrawer,true);
    release[0](helperResponse({ok:false},503));await firstFailed;await new Promise(setImmediate);
    assert.equal(posts.length,2,'A failed job must release the queue');assert.equal(JSON.parse(posts[1].body).cashDrawer,false);
    release[1](helperResponse({ok:true,protocol:'gao-raster-v1',printed:true,copies:1}));await second;await new Promise(resolve=>setTimeout(resolve,5));
    assert.equal(h.completed.length,1);assert.equal(h.browser.length,0);
});
test('standalone drawer and receipt printing share the same exclusive Helper queue and Web Lock',async()=>{
    const h=harness();h.api.savePreferences(context,{mode:'helper'});const posts=[],release=[],locks=[];
    h.env.navigator.locks={request:async(name,...args)=>{
        const callback=args.at(-1);locks.push({name,options:typeof args[0]==='object'?args[0]:{}});return callback();
    }};
    h.env.fetch=async(url,options)=>{
        if(url.endsWith('/health'))return helperResponse({ok:true,protocol:'gao-raster-v1',width:576,cashDrawerOnly:true});
        posts.push({url,body:options.body});return new Promise(resolve=>release.push(resolve));
    };
    const first=h.api.sendReceipt(rendered(),context,h.printWindow,null,true);
    await new Promise(setImmediate);
    const drawer=h.api.openDrawer(context);await new Promise(setImmediate);
    assert.equal(posts.length,1,'No drawer POST may overlap an in-flight paper POST');
    release[0](helperResponse({ok:true,protocol:'gao-raster-v1',printed:true,copies:1}));await first;await new Promise(setImmediate);
    assert.equal(posts.length,2);assert.equal(posts[1].url,'http://127.0.0.1:17880/drawer');
    release[1](helperResponse({ok:true,protocol:'gao-raster-v1',drawerOpened:true}));await drawer;
    assert.equal(locks.length,2);assert.equal(typeof locks[0].name,'string');assert.ok(locks[0].name.length>0);
    assert.equal(locks[1].name,locks[0].name,'All Helper writes need the same cross-tab lock');
    assert.ok(locks.every(lock=>lock.options.mode===undefined||lock.options.mode==='exclusive'));
});
test('standalone drawer retries the same boolean contract only after a verified no-write busy response',async()=>{
    const h=harness();h.api.savePreferences(context,{mode:'helper'});const posts=[],waits=immediateHelperClock(h);
    h.env.fetch=async(url,options)=>{
        if(url.endsWith('/health'))return helperResponse({ok:true,protocol:'gao-raster-v1',width:576,cashDrawerOnly:true});
        posts.push({url,body:options.body});
        return posts.length===1?helperBusy():helperResponse({ok:true,protocol:'gao-raster-v1',drawerOpened:true});
    };
    await h.api.openDrawer(context);
    assert.equal(posts.length,2);assert.deepEqual(waits,[250]);
    assert.ok(posts.every(post=>post.url==='http://127.0.0.1:17880/drawer'&&post.body===posts[0].body));
    assert.deepEqual(JSON.parse(posts[0].body),{protocol:'gao-raster-v1',cashDrawer:true});
    assert.equal(h.browser.length,0);assert.equal(h.parsed.length,0);
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
test('helper rejection diagnostics distinguish invalid JSON, acknowledgement fields and missing HTTP response without retrying',async()=>{
    for(const kind of ['json','printed','copies','network']){
        const h=harness();h.api.savePreferences(context,{mode:'helper'});let posts=0;
        h.env.fetch=async url=>{
            if(url.endsWith('/health'))return {ok:true,status:200,json:async()=>({ok:true,protocol:'gao-raster-v1',width:576})};
            posts++;
            if(kind==='network')throw new TypeError('Failed to fetch');
            return {ok:true,status:200,json:async()=>{
                if(kind==='json')throw new SyntaxError('Invalid JSON');
                return {ok:true,protocol:'gao-raster-v1',printed:kind!=='printed',...(kind==='copies'?{copies:'1'}:{copies:1})};
            }};
        };
        await assert.rejects(h.api.send(rendered(),context,h.printWindow,()=>h.completed.push(true)),error=>{
            assert.equal(error.printDiagnostic.stage,{json:'json',printed:'print-ack',copies:'copies-ack',network:'network'}[kind]);
            assert.equal(error.printDiagnostic.endpoint,'/print');
            assert.equal(error.printDiagnostic.httpStatus,kind==='network'?null:200);
            assert.equal(error.printDiagnostic.expected.copies,1);
            if(kind==='copies')assert.equal(error.printDiagnostic.received.copies,'1');
            assert.match(error.message,/Chưa xác nhận kết quả in/);
            return true;
        });
        assert.equal(posts,1);assert.equal(h.completed.length,0);assert.equal(h.browser.length,0);
    }
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
    ['cash overpay and positive change',finalized([cash(120)],20),true],
    ['no money and no change',finalized([]),false],
    ['deposit is not cash received now', {...finalized([]),depositAmount:100},false],
    ['card is not cash or transfer',finalized([{method:'Card',amount:120}],20),false],
    ['wallet is not cash received now',finalized([{method:'Wallet',amount:120}],20),false],
    ['other payment is not cash received now',finalized([{method:'Other',amount:120}],20),false],
    ['credit does not open drawer',finalized([]),false],
    ['zero cash does not open drawer',finalized([cash(0),transfer(100)]),false],
    ['zero cash with transfer overpay preserves the existing change rule',finalized([cash(0),transfer(120)],20),true],
    ['draft with cash does not open drawer',{...finalized([cash(100)]),status:'Draft'},false],
    ['cancelled with cash does not open drawer',{...finalized([cash(100)]),status:'Cancelled'},false],
    ['refunded with cash does not open drawer',{...finalized([cash(100)]),status:'Refunded'},false]
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
test('the dedicated tab claims its live POS permission without waiting for a background channel',async()=>{
    const h=harness();
    h.env.window.closed=false;
    const url=h.api.postPaymentUrl('/admin/pos/receipt/123',123,h.env.window);
    h.env.window.location=new URL(url,'https://gao.test');
    h.env.window.opener={PosPrinting:h.api};
    h.closeChannels();
    h.env.window.BroadcastChannel=class{constructor(){throw Error('Channel unavailable')}};
    assert.equal(await h.api.consumeCashDrawer(finalized([cash(100)])),true);
    h.env.window.location=new URL(url,'https://gao.test');
    assert.equal(await h.api.consumeCashDrawer(finalized([cash(100)])),false,'A copied/reloaded URL cannot reuse a consumed ticket');
});

test('receipt can claim its own live POS intent when tab storage was lost and the background channel cannot answer',async()=>{
    const h=harness();h.env.window.closed=false;
    const url=h.api.postPaymentUrl('/admin/pos/receipt/123',123,h.env.window);
    h.env.window.location=new URL(url,'https://gao.test');
    h.env.window.sessionStorage.getItem=()=>null;
    h.env.window.opener={PosPrinting:h.api};
    h.closeChannels();h.env.window.BroadcastChannel=class{constructor(){throw Error('Background channel unavailable')}};
    assert.equal(await h.api.consumeCashDrawer(finalized([cash(100)])),true);
    h.env.window.location=new URL(url,'https://gao.test');
    assert.equal(await h.api.consumeCashDrawer(finalized([cash(100)])),false,'Reload/copy cannot use the live permission again');
});

test('a different tab cannot consume the live intent reserved for the cashier receipt',()=>{
    const h=harness();h.env.window.closed=false;
    const url=h.api.postPaymentUrl('/admin/pos/receipt/123',123,h.env.window),token=url.split('=')[1];
    assert.equal(h.api.claimPaymentPrint(token,123,{}),false);
    assert.equal(h.api.claimPaymentPrint(token,124,h.env.window),false);
    assert.equal(h.api.claimPaymentPrint(token,123,h.env.window),true);
    assert.equal(h.api.claimPaymentPrint(token,123,h.env.window),false);
});

test('a consumed live grant cannot be reused through a copied storage snapshot and channel fallback',async()=>{
    const h=harness();h.env.window.closed=false;
    const url=h.api.postPaymentUrl('/admin/pos/receipt/123',123,h.env.window),token=url.split('=')[1];
    const key='gao-pos-payment-print-v1:'+token,copy=h.env.window.sessionStorage.getItem(key);
    h.env.window.location=new URL(url,'https://gao.test');h.env.window.opener={PosPrinting:h.api};
    assert.equal(await h.api.consumeCashDrawer(finalized([cash(100)])),true);
    h.env.window.opener=null;h.env.window.sessionStorage.setItem(key,copy);
    h.env.window.location=new URL(url,'https://gao.test');
    assert.equal(await h.api.consumeCashDrawer(finalized([cash(100)])),false);
});

test('staged drawer tickets reject a different order or an expired handoff',async()=>{
    for(const kind of ['wrong-order','expired']){
        const h=harness();h.env.window.closed=false;
        const url=h.api.postPaymentUrl('/admin/pos/receipt/123',123,h.env.window);
        h.env.window.location=new URL(url,'https://gao.test');
        if(kind==='expired')h.advance(120001);
        assert.equal(await h.api.consumeCashDrawer({...finalized([cash(100)]),orderId:kind==='wrong-order'?124:123}),false);
    }
});

test('blocked print-tab storage returns control to direct POS printing instead of a timed drawer handoff',()=>{
    const h=harness();h.env.window.closed=false;
    h.env.window.sessionStorage.setItem=()=>{throw Error('Storage blocked')};
    assert.equal(h.api.postPaymentUrl('/admin/pos/receipt/123',123,h.env.window),'/admin/pos/receipt/123');
});

test('the POS closes its own print tab after acknowledgement even if the receipt does not close itself',async()=>{
    const h=harness(),closed=[];
    h.env.window.closed=false;h.env.window.close=()=>closed.push(true);
    h.env.window.location=new URL(h.api.postPaymentUrl('/admin/pos/receipt/123',123,h.env.window),'https://gao.test');
    const receipt=finalized([cash(100)]);
    assert.equal(await h.api.consumeCashDrawer(receipt),true);
    h.api.reportPaymentPrint(receipt);
    await new Promise(setImmediate);
    assert.equal(closed.length,1);
    h.api.reportPaymentPrint(receipt);await new Promise(setImmediate);
    assert.equal(closed.length,1);
});

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

for (const channelAvailable of [false,true]) test(`checkout restores the receipt-owned route with BroadcastChannel=${channelAvailable}; history keeps its existing route`,async()=>{
    const h=harness(), navigations=[];
    if (!channelAvailable) h.env.window.BroadcastChannel = undefined;
    const app=fs.readFileSync(path.join(__dirname,'../../GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js'),'utf8');
    const popup=()=>({closed:false,opener:{},sessionStorage:h.env.window.sessionStorage,location:{replace:url=>navigations.push(url)},close(){this.closed=true}});
    h.env.window.open=url=>{if(url!=='about:blank')navigations.push(url);return popup();};
    h.env.window.PosOffline={print:()=>false,resolveOrderId:id=>id,pendingInvoiceIntentOrderId:()=>null,invoiceIntentStatus:()=>null};
    const direct=[];
    h.env.window.PosPrinting.openLocal=(...args)=>direct.push(args);
    const env={window:h.env.window,Number,encodeURIComponent,setTimeout,checkoutFeedback:{begin(){},complete(){},showRetry(){}},invoiceIntentErrorBox:null,invoiceIntentOrderText:{},
        invoiceIntentModal:{show(){},hide(){}},btnInvoiceIntentManual:null,btnInvoiceIntentAutomatic:null,
        invoiceIntentModalEl:{classList:{contains:()=>false}},receiptPrintQueue:[],postPaymentPrintOrders:new Set(),preparedReceiptPrintWindows:new Map(),receiptPrintActive:false,receiptPrintClosing:false,
        getJson:async()=>({receipt:{...finalized([cash(100)]),orderId:123},storeId:1,terminalId:2,templates:[]}),
        pendingReceiptPrint:null,pendingAskBeforePrintingReceipt:false,document:{getElementById:()=>null,createElement:()=>({style:{},dataset:{},contentWindow:popup(),remove(){},set src(url){navigations.push(url)},setAttribute(){},addEventListener(){}}),body:{appendChild(){}}},pendingInvoiceIntentOrderId:null,pendingPostPaymentPrintOrderId:null,invoiceIntentBusy:false,posState:{ui:{}},
        showError:message=>{throw Error(message)},postJson:async()=>({orderId:123})};
    vm.runInNewContext(app.slice(app.indexOf('function clearInvoiceIntentError()'),app.indexOf('        function nowIso()')),env);
    env.openReceiptPrint(123,'80',true,true);
    await new Promise(setImmediate);
    if(channelAvailable){assert.equal(direct.length,0);assert.match(navigations.shift(),/^\/admin\/pos\/receipt\/123#pos-payment-print=/);}
    else{assert.equal(direct.length,1);assert.equal(direct[0][4],true);}
    env.openReceiptPrint(123,'80',true,true);await new Promise(setImmediate);assert.equal(direct.length,channelAvailable?0:1);assert.equal(navigations.length,0);
    env.openReceiptPrint(123);await env.submitInvoiceIntent(1);
    assert.equal(navigations[0],'/admin/pos/receipt/123');
    h.env.window.PosOffline.pendingInvoiceIntentOrderId=()=>123;
    env.restorePendingInvoiceIntent();await new Promise(setImmediate);
    assert.equal(navigations.length,1,'Already printed checkout 123 must not start a recovery reprint');
    h.env.window.PosOffline.pendingInvoiceIntentOrderId=()=>124;
    env.postJson=async()=>{h.env.window.PosOffline.pendingInvoiceIntentOrderId=()=>null;return {orderId:124};};
    env.restorePendingInvoiceIntent();await new Promise(setImmediate);
    assert.equal(navigations[1],'/admin/pos/receipt/124','A separate unfinished order still recovers normally');
});

test('current-cart finalize passes an immediate print intent only after success',async()=>{
    const source=fs.readFileSync(path.join(__dirname,'../../GaoApp.Web/wwwroot/Admin/js/pos/pos.order.js'),'utf8');
    const start=source.indexOf('        async function finalizeCurrentCart()');
    const end=source.indexOf('\n        async function ',start+1);
    const printed=[];let confirmation;
    const env={window:{},posState:{},btnConfirmAction:{},confirmModal:{hide(){}},refreshLocksSafe(){},prepareReceiptPrint(){},discardReceiptPrint(){},
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
        privacyLocked:false,hardLocked:false,expired:()=>false,
        localMode:()=>true,core:{permission(){}},window:{PosPrinting:{openLocal:(...args)=>opened.push(args)}}};
    vm.runInNewContext(source.slice(source.indexOf('    function print('),source.indexOf('    function exportPending()')),env);
    assert.equal(env.print(123,true),true);assert.equal(opened[0][4],true);
    assert.equal(env.print(123),true);assert.equal(opened[1][4],false);
    assert.equal(env.print(123,'true'),true);assert.equal(opened[2][4],false);
    env.privacyLocked=true;assert.equal(env.print(123,true),false);assert.equal(opened.length,3);
});
test('no channel to the live POS means fail closed and remove the receipt URL marker',async()=>{
    const h=harness();h.env.window.location=new URL(h.api.postPaymentUrl('/admin/pos/receipt/123',123),'https://gao.test');
    delete h.env.window.BroadcastChannel;
    assert.equal(await h.api.consumeCashDrawer(finalized([cash(100)])),false);assert.equal(h.env.window.location.hash,'');
});

test('standalone drawer requires helper mode and capability, and posts only a boolean contract',async()=>{
    const h=harness();await assert.rejects(h.api.openDrawer(context),/Chọn Linux Print Helper/);
    h.api.savePreferences(context,{mode:'helper'});
    h.env.fetch=async()=>({ok:true,json:async()=>({ok:true,protocol:'gao-raster-v1',width:576})});
    await assert.rejects(h.api.openDrawer(context),/cập nhật Print Helper/);
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

