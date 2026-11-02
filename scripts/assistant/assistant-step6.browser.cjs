// Real GaoApp markup, CSS and JS. All API calls use isolated HTTP fixtures.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/wind/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/assistant-test.cjs')('playwright');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'Logs/assistant-upgrade-step6');fs.mkdirSync(out,{recursive:true});
const id1='11111111-1111-4111-a111-111111111111',id2='22222222-2222-4222-a222-222222222222',id3='33333333-3333-4333-a333-333333333333';
let who,conversations,active,posts,fault,mode,oneUnit,chatDelay,chatPending,readAt,feedbackKind,feedbackPending,feedbackResult;const cases=[];
function reset(){who={contractVersion:'1.0',identity:'1',displayName:'Minh An',storeName:'GaoMart Chơn Thành',scope:{storeId:'1',warehouseId:null,legalEntityId:null},permissions:['assistant.use','assistant.chat','catalog.read','quotes.retail.read','quotes.wholesale.read']};conversations=[{id:id1,title:'Tra giá sữa',store:'1'},{id:id2,title:'Hỏi về nhập hàng',store:'1'},{id:id3,title:'Hội thoại cửa hàng 2',store:'2'}];active={'1':id1,'2':id3};posts=[];fault=null;feedbackKind=null;feedbackPending=false;feedbackResult=null;mode='quote';oneUnit=false;chatDelay=0;chatPending=false;readAt=new Date(Date.now()-600000).toISOString();}
reset();
const scope=()=>({...who.scope}),expires=()=>new Date(Date.now()+60000).toISOString();
const view=fs.readFileSync(path.join(root,'GaoApp.Web/Areas/Admin/Views/Shared/Components/Assistant/Default.cshtml'),'utf8').replace(/@Url.Content\("([^"]+)"\)/g,(_,value)=>value.replace('~','')).replace('@Html.AntiForgeryToken()','<input name="__RequestVerificationToken" value="TEST-CSRF">').replace(/ asp-append-version="true"/g,'').replace(/(src|href)="~\//g,'$1="/').replace(/<script[^>]+assistant-forms.js[^>]+><\/script>/,'');
const html='<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>GaoApp — kiểm tra bước 3</title><style>body{margin:0;font:14px system-ui;background:#eef3ef;color:#284839}.background{padding:32px}.background h1{font-size:24px}.background p{color:#71877a}.background button{padding:12px;border-radius:8px;border:1px solid #bfd0c3;background:white}*{box-sizing:border-box}</style></head><body><div class="background"><h1>GaoApp</h1><p>Dữ liệu minh họa để kiểm tra giao diện trợ lý.</p><button id="outside">Tiếp tục công việc</button></div>'+view+'</body></html>';
const server=http.createServer(async(req,res)=>{
 const url=new URL(req.url,'http://localhost');const json=(body,status=200)=>{res.statusCode=status;res.setHeader('Content-Type','application/json');res.end(JSON.stringify(body));};
 if(url.pathname==='/quality-preview'){res.setHeader('Content-Type','text/html;charset=utf-8');return res.end(fs.readFileSync(path.join(out,'quality-page.html'),'utf8').replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'').replace(/<link[^>]+(?:https:)?\/\/[^>]+>/gi,''));}
 if(url.pathname==='/'){res.setHeader('Content-Type','text/html; charset=utf-8');return res.end(html);}
 const assetRoot=path.join(root,'GaoApp.Web/wwwroot'),asset=path.resolve(assetRoot,url.pathname.slice(1));if(!url.pathname.startsWith('/uploads/')&&asset.startsWith(assetRoot+path.sep)&&fs.existsSync(asset)&&fs.statSync(asset).isFile()){res.setHeader('Content-Type',asset.endsWith('.css')?'text/css':asset.endsWith('.js')?'text/javascript':asset.endsWith('.svg')?'image/svg+xml':asset.endsWith('.woff2')?'font/woff2':'application/octet-stream');return res.end(fs.readFileSync(asset));}
 if(url.pathname.startsWith('/Admin/')){const file=path.resolve(root,'GaoApp.Web/wwwroot',url.pathname.slice(1));if(!file.startsWith(path.join(root,'GaoApp.Web/wwwroot')+path.sep)||!fs.existsSync(file)){res.statusCode=404;return res.end();}res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':'text/css');return res.end(fs.readFileSync(file));}
 if(url.pathname==='/uploads/products/valid.png'){res.setHeader('Content-Type','image/png');return res.end(Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRusAAAAASUVORK5CYII=','base64'));}
 if(url.pathname.startsWith('/uploads/')){res.statusCode=404;return res.end();}
 if(url.pathname.endsWith('/context'))return json({...who,scope:scope()});
 let raw='';for await(const chunk of req)raw+=chunk;const input=raw?JSON.parse(raw):{},op=url.pathname.split('/').pop();posts.push({op,input});
 const envelope={contractVersion:'1.0',requestId:input.requestId||crypto.randomUUID(),scope:scope()};
 const body=c=>({...envelope,conversationId:c.id,title:c.title,expiresAt:expires()});
 if(fault===op)return json({...envelope,code:'authorization_source_unavailable',message:'Chưa xác minh được quyền truy cập hiện tại.'},503);
 if(op==='Feedback'){feedbackKind=input.body.kind;feedbackResult={...envelope,feedback:{messageId:input.body.messageId,kind:feedbackKind}};return json(feedbackPending?{...envelope,status:'processing'}:feedbackResult,feedbackPending?202:200);}
 if(op==='RequestStatus')return json({...envelope,requestId:input.resourceId,status:'completed',result:feedbackResult});
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
   if(input.body.message==='đổi mặt hàng khác')mode='none';
   else if(input.body.message==='tất cả đơn vị')oneUnit=false;
   else if(input.body.message==='giá sỉ')mode='wholesale';
   else if(input.body.message.startsWith('chọn '))mode='quote';
   readAt=new Date().toISOString();return json(body(c));
 }
 if(op==='History'){
   const product={productId:'101',variantId:'1001',name:'Sữa tươi TH có đường hộp 180ml'};
   const item=(p,choice=null)=>({product:p,choice,sku:who.scope.storeId==='1'?'TH180-001':'TH180-STORE2',imageUrl:null,
      attributes:{brand:'TH',sugar:'sweetened',capacity:'180ml'},saleUnits:['Hộp','Lốc','Thùng']});
   let contents='[TEST] '+product.name+' — Hộp: giá '+(mode==='wholesale'?'sỉ 8.500':'lẻ 9.000')+' đ/Hộp';
   if(!oneUnit)contents+='\n'+product.name+' — Lốc: giá lẻ 36.000 đ/Lốc\n'+product.name+' — Thùng: giá lẻ 432.000 đ/Thùng';
   let display={messageId:'12',scope:scope(),kind:'quote',items:[item(product)],hasMore:false,
       priceReadAt:readAt,priceTiers:[mode==='wholesale'?'wholesale':'retail'],expiresAt:expires(),canReadWholesale:who.permissions.includes('quotes.wholesale.read')};
   if(mode==='choices'){
      display={...display,kind:'choices',hasMore:true,priceReadAt:null,priceTiers:null,items:[item({productId:'901',variantId:'9001',name:'Pepsi lon 320ml'},1),item({productId:'902',variantId:'9002',name:'Pepsi chai 390ml'},2)]};
      contents='Mình chọn sản phẩm bên dưới nhé:\n1. Pepsi lon 320ml\n2. Pepsi chai 390ml\nXem thêm sản phẩm.';
   }
   if(mode==='product'){display.kind='product';display.priceReadAt=null;display.priceTiers=null;contents='[TEST] '+product.name+'. Đơn vị bán: Hộp, Lốc, Thùng.';}
   if(mode==='none'){display=null;contents='Mình muốn tìm mặt hàng nào?';}
   if(mode==='expired')display.expiresAt=new Date(Date.now()-1000).toISOString();
   if(mode==='twoExpiries')display.expiresAt=new Date(Date.now()+1000).toISOString();
   if(mode==='short')display.expiresAt=new Date(Date.now()+750).toISOString();
   if(mode==='scope')display.scope={...scope(),storeId:'2'};
   if(mode==='old')display.messageId='11';
   if(mode==='unsafe')display.items[0].imageUrl='/uploads/products/%2e%2e/secrets.png';
   if(mode==='external')display.items[0].imageUrl='https://other.invalid/product.png';
   if(mode==='photo')display.items[0].imageUrl='/uploads/products/valid.png';
   if(mode==='broken')display.items[0].imageUrl='/uploads/products/missing.png';
   if(mode==='html'){display.items[0].sku='<img src=x onerror=alert(1)>';display.items[0].attributes.brand='<script>alert(1)</script>';}
   if(mode==='noSku')display.items[0].sku=null;
   if(mode==='revoked')who.permissions=who.permissions.filter(p=>!p.startsWith('quotes.'));
   if(input.cursor){display=null;contents=product.name+' — Hộp: giá lẻ 8.000 đ/Hộp';}
   const items=[{messageId:'11',role:'user',content:'Giá sữa tươi TH có đường 180ml',scope:scope()},
       {messageId:input.cursor?'8':'12',role:'assistant',content:contents,scope:scope(),expiresAt:mode==='expiredFeedback'?new Date(Date.now()-1000).toISOString():mode==='twoExpiries'?new Date(Date.now()+400).toISOString():expires()}];
   return json({...envelope,conversationId:input.resourceId,items,nextCursor:input.cursor?null:'older',
      ...(mode==='legacy'?{}:{productDisplay:display}),clarification:null,feedback:feedbackKind?[{messageId:'12',kind:feedbackKind,updatedAt:new Date().toISOString()}]:[]});
 }
 return json(envelope);
});
let browser,origin;const ready=page=>page.waitForFunction(()=>!document.querySelector('#gaSend').disabled);const open=async page=>{await page.locator('#gaOpen').click();await ready(page);};
async function test(name,fn,options={}){reset();let page;try{page=await browser.newPage({viewport:{width:1280,height:900},...options});const errors=[];page.on('pageerror',e=>errors.push(e.message));await fn(page,()=>page.goto(origin));assert.deepEqual(errors,[]);cases.push({name,passed:true});console.log('PASS '+name);}catch(error){cases.push({name,passed:false,error:error.message});console.error('FAIL '+name+' '+error.message);}finally{await page?.close();}}
const count=op=>posts.filter(p=>p.op===op).length;

(async()=>{await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));origin='http://127.0.0.1:'+server.address().port;browser=await chromium.launch({headless:true,channel:'msedge'});
try{

 await test('feedback is offered only for assistant messages and opts into history state',async(p,go)=>{await go();await open(p);assert.equal(await p.locator('.ga-feedback-choice').count(),3);assert.equal(await p.locator('[data-role="user"] .ga-feedback-choice').count(),0);assert.equal(posts.find(x=>x.op==='History').input.includeFeedback,true);await p.screenshot({path:path.join(out,'feedback-desktop.png')});});
 await test('wrong-product report sends one referenced mutation without resending the question',async(p,go)=>{await go();await open(p);await p.locator('#gaInput').fill('Nội dung đang gõ');await p.getByRole('button',{name:'Sai hàng',exact:true}).click();await ready(p);const sent=posts.filter(x=>x.op==='Feedback');assert.equal(sent.length,1);assert.deepEqual(sent[0].input.body,{conversationId:id1,messageId:'12',kind:'wrong_product'});assert.match(sent[0].input.requestId,/^[a-f0-9-]{36}$/);assert.equal(posts.filter(x=>x.op==='Chat').length,0);assert.equal(await p.locator('#gaInput').inputValue(),'Nội dung đang gõ');assert.equal(await p.getByRole('button',{name:'Sai hàng',exact:true}).getAttribute('aria-pressed'),'true');});
 await test('feedback survives reopening and a second click removes the selection',async(p,go)=>{await go();await open(p);await p.getByRole('button',{name:'Sai giá',exact:true}).click();await ready(p);await p.locator('#gaClose').click();await open(p);assert.equal(await p.getByRole('button',{name:'Sai giá',exact:true}).getAttribute('aria-pressed'),'true');await p.getByRole('button',{name:'Sai giá',exact:true}).click();await ready(p);assert.equal(posts.filter(x=>x.op==='Feedback').at(-1).input.body.kind,null);});
 await test('feedback error preserves the price card and composer and does not retry automatically',async(p,go)=>{fault='Feedback';await go();await open(p);await p.getByRole('button',{name:'Chưa hiểu',exact:true}).click();await ready(p);assert.match(await p.locator('#gaStatus').textContent(),/xác minh/);assert.equal(await p.locator('.ga-quote tbody tr').count(),3);assert.equal(posts.filter(x=>x.op==='Feedback').length,1);assert.equal(await p.locator('#gaSend').isDisabled(),false);});
 await test('pending feedback checks the original request and keeps price actions locked',async(p,go)=>{feedbackPending=true;await go();await open(p);await p.getByRole('button',{name:'Sai hàng',exact:true}).click();await p.locator('#gaRecovery').waitFor();await p.waitForFunction(()=>!document.querySelector('#gaCheck').disabled);assert.equal(await p.getByRole('button',{name:'Sai giá',exact:true}).isDisabled(),true);const original=posts.find(x=>x.op==='Feedback').input.requestId;await p.locator('#gaCheck').click();await ready(p);assert.equal(posts.find(x=>x.op==='RequestStatus').input.resourceId,original);assert.equal(posts.filter(x=>x.op==='Feedback').length,1);});
 await test('expired messages do not accept feedback',async(p,go)=>{mode='expiredFeedback';await go();await open(p);assert.equal(await p.getByRole('button',{name:'Sai hàng',exact:true}).isDisabled(),true);});
 await test('detached feedback from another store cannot send',async(p,go)=>{await go();await open(p);await p.evaluate(()=>window.detached=document.querySelector('.ga-feedback-choice'));await p.locator('#gaClose').click();who={...who,scope:{...who.scope,storeId:'2'}};await open(p);await p.evaluate(()=>detached.click());assert.equal(posts.filter(x=>x.op==='Feedback').length,0);});
 await test('feedback expiry does not stop the later price-card expiry timer',async(p,go)=>{mode='twoExpiries';await go();await open(p);await p.waitForTimeout(500);assert.equal(await p.getByRole('button',{name:'Sai hàng',exact:true}).isDisabled(),true);await p.waitForTimeout(650);assert.equal(await p.getByRole('button',{name:'Giá sỉ',exact:true}).isDisabled(),true);});
 await test('mobile feedback buttons keep 44-pixel touch targets and fit the panel',async(p,go)=>{await p.setViewportSize({width:320,height:844});await go();await open(p);await p.locator('.ga-feedback').scrollIntoViewIfNeeded();for(const b of await p.locator('.ga-feedback-choice').all()){const r=await b.boundingBox();assert.ok(r.height>=44&&r.x>=0&&r.x+r.width<=320);}await p.screenshot({path:path.join(out,'feedback-mobile.png')});});
 await test('actual server-rendered management page is readable on desktop and mobile',async(p,go)=>{const page=path.join(out,'quality-page.html');assert.ok(fs.existsSync(page),'Run BFF SQL test before management preview');await p.goto(origin+'/quality-preview');await p.locator('.aq-page').waitFor();assert.match(await p.locator('.aq-page').textContent(),/TEST-model/);await p.locator('.aq-page').screenshot({path:path.join(out,'quality-desktop.png')});await p.setViewportSize({width:390,height:844});await p.locator('.aq-page').screenshot({path:path.join(out,'quality-mobile.png')});});
 fs.writeFileSync(path.join(out,'step6-results.json'),JSON.stringify({transport:'mock',geminiCalls:0,photoFixture:'1-pixel PNG for load test, placeholders in screenshots',cases},null,2));process.exitCode=cases.some(c=>!c.passed)?1:0;
}finally{await browser.close();server.close();}})().catch(e=>{console.error(e);server.close();process.exitCode=1;});
