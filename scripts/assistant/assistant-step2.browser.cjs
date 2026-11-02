// Real GaoApp markup, CSS and JS. All API calls use isolated HTTP fixtures.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/wind/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/assistant-test.cjs')('playwright');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'Logs/assistant-upgrade-step2');fs.mkdirSync(out,{recursive:true});
const id1='11111111-1111-4111-a111-111111111111',id2='22222222-2222-4222-a222-222222222222',id3='33333333-3333-4333-a333-333333333333';
let who,conversations,active,posts,fault,hintMode,chatDelay,chatPending,historyExtras,attributeOptions;const cases=[];
function reset(){who={contractVersion:'1.0',identity:'1',displayName:'Minh An',storeName:'GaoMart Chơn Thành',scope:{storeId:'1',warehouseId:null,legalEntityId:null},permissions:['assistant.use','assistant.chat']};conversations=[{id:id1,title:'Tra giá sữa',store:'1'},{id:id2,title:'Hỏi về nhập hàng',store:'1'},{id:id3,title:'Hội thoại cửa hàng 2',store:'2'}];active={'1':id1,'2':id3};posts=[];fault=null;hintMode='valid';chatDelay=0;chatPending=false;historyExtras=false;attributeOptions=['Có đường','Ít đường','Không đường'];}
reset();
const scope=()=>({...who.scope}),expires=()=>new Date(Date.now()+60000).toISOString();
const view=fs.readFileSync(path.join(root,'GaoApp.Web/Areas/Admin/Views/Shared/Components/Assistant/Default.cshtml'),'utf8').replace(/@Url.Content\("([^"]+)"\)/g,(_,value)=>value.replace('~','')).replace('@Html.AntiForgeryToken()','<input name="__RequestVerificationToken" value="TEST-CSRF">').replace(/ asp-append-version="true"/g,'').replace(/(src|href)="~\//g,'$1="/').replace(/<script[^>]+assistant-forms.js[^>]+><\/script>/,'');
const html='<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>GaoApp — kiểm tra bước 2</title><style>body{margin:0;font:14px system-ui;background:#eef3ef;color:#284839}.background{padding:32px}.background h1{font-size:24px}.background p{color:#71877a}.background button{padding:12px;border-radius:8px;border:1px solid #bfd0c3;background:white}*{box-sizing:border-box}</style></head><body><div class="background"><h1>GaoApp</h1><p>Dữ liệu minh họa để kiểm tra giao diện trợ lý.</p><button id="outside">Tiếp tục công việc</button></div>'+view+'</body></html>';
const server=http.createServer(async(req,res)=>{
 const url=new URL(req.url,'http://localhost');const json=(body,status=200)=>{res.statusCode=status;res.setHeader('Content-Type','application/json');res.end(JSON.stringify(body));};
 if(url.pathname==='/'){res.setHeader('Content-Type','text/html; charset=utf-8');return res.end(html);}
 if(url.pathname.startsWith('/Admin/')){const file=path.resolve(root,'GaoApp.Web/wwwroot',url.pathname.slice(1));if(!file.startsWith(path.join(root,'GaoApp.Web/wwwroot')+path.sep)||!fs.existsSync(file)){res.statusCode=404;return res.end();}res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':'text/css');return res.end(fs.readFileSync(file));}
 if(url.pathname.endsWith('/context'))return json({...who,scope:scope()});
 let raw='';for await(const chunk of req)raw+=chunk;const input=raw?JSON.parse(raw):{},op=url.pathname.split('/').pop();posts.push({op,input});
 const envelope={contractVersion:'1.0',requestId:input.requestId||crypto.randomUUID(),scope:scope()};
 const body=c=>({...envelope,conversationId:c.id,title:c.title,expiresAt:expires()});
 if(fault===op)return json({...envelope,code:'authorization_source_unavailable',message:'Chưa xác minh được quyền truy cập hiện tại.'},503);
 if(op==='Capabilities')return json({...envelope,chat:{available:true}});
 if(op==='ResumeConversation'||op==='CreateConversation'){
   let c=conversations.find(c=>c.id===active[who.scope.storeId]);
   if(op==='CreateConversation'||!c){c={id:crypto.randomUUID(),title:'Cuộc chat mới',store:who.scope.storeId};conversations.push(c);active[c.store]=c.id;}return json(body(c));
 }
 if(op==='ListConversations')return json({...envelope,items:conversations.filter(c=>c.store===who.scope.storeId).map(body),nextCursor:null});
 const c=conversations.find(c=>c.id===(input.resourceId||input.body?.conversationId));
 if(op==='GetConversation')return json(body(c));
 if(op==='RenameConversation'){c.title=input.body.title;return json(body(c));}
 if(op==='DeleteConversation'){conversations=conversations.filter(x=>x!==c);delete active[who.scope.storeId];return json(envelope);}

 if(op==='Chat'){
   if(chatDelay)await new Promise(r=>setTimeout(r,chatDelay));
   if(chatPending)return json({...envelope,status:'processing'},202);
   hintMode='none';return json(body(c));
 }
 if(op==='History'){
   const latest={messageId:'12',role:'assistant',content:hintMode==='none'?'Sữa tươi TH có đường hộp 180ml — Hộp: giá lẻ 9.000 đ/Hộp':'Mình cần loại đường nào? '+attributeOptions.map(v=>v.toLowerCase()).join(' / ')+'. Mình chọn hoặc gõ thêm nhé.',scope:scope()};
   let hint={messageId:'12',field:'sugar',options:attributeOptions,scope:scope(),expiresAt:expires()};
   if(hintMode==='none'||input.cursor)hint=null;
   if(hintMode==='expired')hint.expiresAt=new Date(Date.now()-1000).toISOString();
   if(hintMode==='short')hint.expiresAt=new Date(Date.now()+700).toISOString();
   if(hintMode==='old')hint.messageId='10';
   if(hintMode==='scope')hint.scope={...scope(),storeId:'2'};
   if(hintMode==='invalid')hint.options=['Có đường','Có đường'];
   const items=input.cursor?[{messageId:'10',role:'assistant',content:'Câu hỏi cũ',scope:scope()}]:[
      ...(historyExtras?[{messageId:'10',role:'assistant',content:'Loại đường nào?',scope:scope()}]:[]),
      {messageId:'11',role:'user',content:'Giá sữa tươi TH 180ml',scope:scope()},latest];
   return json({...envelope,conversationId:input.resourceId,items,nextCursor:input.cursor?null:'older',...(input.includeClarification?{clarification:hint}:{})});
 }
 return json(envelope);
});
let browser,origin;const ready=page=>page.waitForFunction(()=>!document.querySelector('#gaSend').disabled);const open=async page=>{await page.locator('#gaOpen').click();await ready(page);};
async function test(name,fn,options={}){reset();let page;try{page=await browser.newPage({viewport:{width:1280,height:900},...options});const errors=[];page.on('pageerror',e=>errors.push(e.message));await fn(page,()=>page.goto(origin));assert.deepEqual(errors,[]);cases.push({name,passed:true});console.log('PASS '+name);}catch(error){cases.push({name,passed:false,error:error.message});console.error('FAIL '+name+' '+error.message);}finally{await page?.close();}}
const count=op=>posts.filter(p=>p.op===op).length;

(async()=>{await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));origin='http://127.0.0.1:'+server.address().port;browser=await chromium.launch({headless:true,channel:'msedge'});
try{
 await test('verified attributes render as accessible buttons and opt in to History',async(page,go)=>{
   await go();await open(page);assert.deepEqual(await page.locator('.ga-attribute-choice').allTextContents(),attributeOptions);
   assert.equal(posts.find(p=>p.op==='History').input.includeClarification,true);
   assert.equal(await page.locator('.ga-attribute-choices').getAttribute('role'),'group');
   assert.equal(await page.locator('.ga-message').last().evaluate(n=>n.firstChild.textContent),'Mình cần loại đường nào?');
   await page.locator('#gaPanel').screenshot({path:path.join(out,'attribute-desktop.png')});
 });
 await test('attribute click sends literal label once then disables while waiting',async(page,go)=>{
   chatDelay=300;await go();await open(page);const button=page.locator('.ga-attribute-choice').first();await button.click();
   await page.waitForFunction(()=>document.querySelector('.ga-attribute-choice').disabled);
   await button.evaluate(n=>n.dispatchEvent(new MouseEvent('click',{bubbles:true})));
   await ready(page);assert.equal(count('Chat'),1);assert.equal(posts.find(p=>p.op==='Chat').input.body.message,'Có đường');
   assert.equal(posts.find(p=>p.op==='Chat').input.body.conversationId,id1);assert.equal(await page.locator('.ga-attribute-choice').count(),0);
 });
 await test('typed corrections still send unchanged instead of selecting a button',async(page,go)=>{
   await go();await open(page);await page.locator('#gaInput').fill('không phải có đường mà ít đường');await page.locator('#gaInput').press('Enter');await ready(page);
   assert.equal(posts.find(p=>p.op==='Chat').input.body.message,'không phải có đường mà ít đường');assert.equal(count('Chat'),1);
 });
 await test('expired or older-message metadata does not create active buttons',async(page,go)=>{
   await go();for(const mode of ['expired','old']){hintMode=mode;await open(page);assert.equal(await page.locator('.ga-attribute-choice').count(),0);await page.locator('#gaClose').click();}
 });
 await test('expiry disables buttons while the panel remains open',async(page,go)=>{
   hintMode='short';await go();await open(page);await page.waitForFunction(()=>Array.from(document.querySelectorAll('.ga-attribute-choice')).every(b=>b.disabled));
   await page.locator('.ga-attribute-choice').first().evaluate(n=>n.dispatchEvent(new MouseEvent('click',{bubbles:true})));assert.equal(count('Chat'),0);
 });
 await test('history paging does not duplicate latest active buttons',async(page,go)=>{
   historyExtras=true;await go();await open(page);assert.equal(await page.locator('.ga-attribute-choice').count(),3);
   await page.locator('#gaHistoryMore').click();await ready(page);assert.equal(await page.locator('.ga-attribute-choice').count(),3);
   assert.equal(await page.locator('.ga-message').count(),4);
 });
 await test('different-store metadata is rejected and old transcript is cleared',async(page,go)=>{
   hintMode='scope';await go();await page.locator('#gaOpen').click();await page.locator('#gaOpenRetry').waitFor();
   assert.equal(await page.locator('.ga-attribute-choice').count(),0);assert.equal(await page.locator('.ga-message').count(),0);assert.match(await page.locator('#gaStatus').textContent(),/sai cửa hàng/);
 });
 await test('malformed duplicate options are rejected before rendering',async(page,go)=>{
   hintMode='invalid';await go();await page.locator('#gaOpen').click();await page.locator('#gaOpenRetry').waitFor();assert.equal(await page.locator('.ga-attribute-choice').count(),0);
 });
 await test('source labels remain text, including HTML characters',async(page,go)=>{
   attributeOptions=['<img src=x onerror=alert(1)>','Cam'];await go();await open(page);assert.equal(await page.locator('.ga-attribute-choices img').count(),0);
   assert.equal(await page.locator('.ga-attribute-choice').first().textContent(),attributeOptions[0]);
 });
 await test('pending requests prevent old suggestions from resending',async(page,go)=>{
   chatPending=true;await go();await open(page);await page.locator('.ga-attribute-choice').first().click();await page.locator('#gaRecovery').waitFor();
   assert.equal(await page.locator('.ga-attribute-choice:disabled').count(),3);
   await page.locator('.ga-attribute-choice').nth(1).evaluate(n=>n.dispatchEvent(new MouseEvent('click',{bubbles:true})));assert.equal(count('Chat'),1);
 });
 await test('reopening after store change uses only new-store suggestions',async(page,go)=>{
   await go();await open(page);await page.locator('#gaClose').click();who={...who,storeName:'Cửa hàng 2',scope:{...who.scope,storeId:'2'}};
   attributeOptions=['1L','2L'];await open(page);assert.deepEqual(await page.locator('.ga-attribute-choice').allTextContents(),attributeOptions);
   await page.locator('.ga-attribute-choice').first().click();await ready(page);assert.equal(posts.find(p=>p.op==='Chat').input.body.conversationId,id3);
 });
 await test('mobile attribute buttons wrap inside panel and keep touch size',async(page,go)=>{
   await page.setViewportSize({width:320,height:844});await go();await open(page);
   const panel=await page.locator('#gaPanel').boundingBox();for(const button of await page.locator('.ga-attribute-choice').all()){const box=await button.boundingBox();assert.ok(box.height>=44&&box.x>=panel.x&&box.x+box.width<=panel.x+panel.width);}
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
   await page.screenshot({path:path.join(out,'attribute-mobile.png')});
 });
 fs.writeFileSync(path.join(out,'step2-results.json'),JSON.stringify({transport:'mock',geminiCalls:0,cases},null,2));process.exitCode=cases.some(c=>!c.passed)?1:0;
}finally{await browser.close();server.close();}})().catch(e=>{console.error(e);server.close();process.exitCode=1;});
