const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function fixture() {
    const requests = [], responses = [], contexts = [], identity = {contractVersion:'1.0',identity:'1',scope:{storeId:'1',warehouseId:null,legalEntityId:null},permissions:[]};
    const window = {addEventListener(){}};
    const sandbox = vm.createContext({window,document:{addEventListener(){}},location:{origin:'https://pos.test'},URL,AbortController,
        crypto:require('node:crypto').webcrypto,setTimeout,clearTimeout,Date,
        fetch:async (url,options)=>{ requests.push({url:String(url),body:options?.body&&JSON.parse(options.body)});
            if(String(url).includes('/context'))return contexts.length?contexts.shift()():Response.json(identity);
            return responses.shift()(requests.at(-1)); }});
    vm.runInContext(fs.readFileSync(require.resolve('../../GaoApp.Web/wwwroot/Admin/js/assistant-core.js'),'utf8'),sandbox);
    return {session:new window.GaoAssistant.Session({dataset:{contextUrl:'/context',callUrl:'/call/'},querySelector(){return {value:'csrf'};}}),requests,responses,contexts,identity};
}
const busy = () => Response.json({contractVersion:'1.0',code:'resource_busy',message:'busy',retryAfterSeconds:1},{status:429,headers:{'Retry-After':'1'}});
test('AI 429 preserves the job and same request ID; status and cancellation have independent budgets',async()=>{
    const f=fixture();f.responses.push(busy);
    await assert.rejects(f.session.begin('Chat',{message:'test'}),e=>e.code==='resource_busy'&&e.retryAfterSeconds===1);
    const job=f.session.job;assert.ok(job);
    f.responses.push(()=>Response.json({contractVersion:'1.0',code:'request_not_found'},{status:404}));
    await f.session.check();assert.equal(job.retry,true);
    await assert.rejects(f.session.retry(),e=>e.code==='resource_busy');assert.equal(f.session.job,job);
    await new Promise(resolve=>setTimeout(resolve,1100));
    // Status must be checked again after a failed retry before resubmitting.
    f.responses.push(()=>Response.json({contractVersion:'1.0',code:'request_not_found'},{status:404}));await f.session.check();
    f.responses.push(req=>Response.json({contractVersion:'1.0',requestId:req.body.requestId,scope:f.identity.scope}));
    await f.session.retry();assert.equal(f.session.job,null);
    const calls=f.requests.filter(x=>x.url.endsWith('/Chat'));assert.equal(calls.length,2);assert.equal(calls[0].body.requestId,calls[1].body.requestId);
    f.responses.push(busy);await assert.rejects(f.session.begin('Chat',{}));
    f.responses.push(()=>Response.json({contractVersion:'1.0'}));await f.session.cancel();
    assert.ok(f.requests.at(-1).url.endsWith('/CancelRequest'));assert.equal(f.session.job,null);
});
test('status 429 retains a previously accepted AI job',async()=>{
    const f=fixture();f.responses.push(req=>Response.json({contractVersion:'1.0',requestId:req.body.requestId,scope:f.identity.scope},{status:202}));
    assert.equal(await f.session.begin('Chat',{}),null);const job=f.session.job;
    f.responses.push(busy);await assert.rejects(f.session.check(),e=>e.status===429);assert.equal(f.session.job,job);
});
test('context 429 after an accepted AI request retains its original job for status verification',async()=>{
    const f=fixture();f.contexts.push(()=>Response.json(f.identity),busy);
    f.responses.push(req=>Response.json({contractVersion:'1.0',requestId:req.body.requestId,scope:f.identity.scope},{status:202}));
    await assert.rejects(f.session.begin('Chat',{}),e=>e.code==='resource_busy');
    assert.equal(f.session.job.payload.requestId,f.requests.find(x=>x.url.endsWith('/Chat')).body.requestId);
});
