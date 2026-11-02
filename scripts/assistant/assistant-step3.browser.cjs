// Real GaoApp markup, CSS and JS. All API calls use isolated HTTP fixtures.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/wind/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/assistant-test.cjs')('playwright');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'Logs/assistant-upgrade-step3');fs.mkdirSync(out,{recursive:true});
const id1='11111111-1111-4111-a111-111111111111',id2='22222222-2222-4222-a222-222222222222',id3='33333333-3333-4333-a333-333333333333';
let who,conversations,active,posts,fault,mode,oneUnit,chatDelay,chatPending,readAt;const cases=[];
function reset(){who={contractVersion:'1.0',identity:'1',displayName:'Minh An',storeName:'GaoMart Chơn Thành',scope:{storeId:'1',warehouseId:null,legalEntityId:null},permissions:['assistant.use','assistant.chat','catalog.read','quotes.retail.read','quotes.wholesale.read']};conversations=[{id:id1,title:'Tra giá sữa',store:'1'},{id:id2,title:'Hỏi về nhập hàng',store:'1'},{id:id3,title:'Hội thoại cửa hàng 2',store:'2'}];active={'1':id1,'2':id3};posts=[];fault=null;mode='quote';oneUnit=false;chatDelay=0;chatPending=false;readAt=new Date(Date.now()-600000).toISOString();}
reset();
const scope=()=>({...who.scope}),expires=()=>new Date(Date.now()+60000).toISOString();
const view=fs.readFileSync(path.join(root,'GaoApp.Web/Areas/Admin/Views/Shared/Components/Assistant/Default.cshtml'),'utf8').replace(/@Url.Content\("([^"]+)"\)/g,(_,value)=>value.replace('~','')).replace('@Html.AntiForgeryToken()','<input name="__RequestVerificationToken" value="TEST-CSRF">').replace(/ asp-append-version="true"/g,'').replace(/(src|href)="~\//g,'$1="/').replace(/<script[^>]+assistant-forms.js[^>]+><\/script>/,'');
const html='<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>GaoApp — kiểm tra bước 3</title><style>body{margin:0;font:14px system-ui;background:#eef3ef;color:#284839}.background{padding:32px}.background h1{font-size:24px}.background p{color:#71877a}.background button{padding:12px;border-radius:8px;border:1px solid #bfd0c3;background:white}*{box-sizing:border-box}</style></head><body><div class="background"><h1>GaoApp</h1><p>Dữ liệu minh họa để kiểm tra giao diện trợ lý.</p><button id="outside">Tiếp tục công việc</button></div>'+view+'</body></html>';
const server=http.createServer(async(req,res)=>{
 const url=new URL(req.url,'http://localhost');const json=(body,status=200)=>{res.statusCode=status;res.setHeader('Content-Type','application/json');res.end(JSON.stringify(body));};
 if(url.pathname==='/'){res.setHeader('Content-Type','text/html; charset=utf-8');return res.end(html);}
 if(url.pathname.startsWith('/Admin/')){const file=path.resolve(root,'GaoApp.Web/wwwroot',url.pathname.slice(1));if(!file.startsWith(path.join(root,'GaoApp.Web/wwwroot')+path.sep)||!fs.existsSync(file)){res.statusCode=404;return res.end();}res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':'text/css');return res.end(fs.readFileSync(file));}
 if(url.pathname==='/uploads/products/valid.png'){res.setHeader('Content-Type','image/png');return res.end(Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRusAAAAASUVORK5CYII=','base64'));}
 if(url.pathname.startsWith('/uploads/')){res.statusCode=404;return res.end();}
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
       {messageId:input.cursor?'8':'12',role:'assistant',content:contents,scope:scope()}];
   return json({...envelope,conversationId:input.resourceId,items,nextCursor:input.cursor?null:'older',
      ...(mode==='legacy'?{}:{productDisplay:display}),clarification:null});
 }
 return json(envelope);
});
let browser,origin;const ready=page=>page.waitForFunction(()=>!document.querySelector('#gaSend').disabled);const open=async page=>{await page.locator('#gaOpen').click();await ready(page);};
async function test(name,fn,options={}){reset();let page;try{page=await browser.newPage({viewport:{width:1280,height:900},...options});const errors=[];page.on('pageerror',e=>errors.push(e.message));await fn(page,()=>page.goto(origin));assert.deepEqual(errors,[]);cases.push({name,passed:true});console.log('PASS '+name);}catch(error){cases.push({name,passed:false,error:error.message});console.error('FAIL '+name+' '+error.message);}finally{await page?.close();}}
const count=op=>posts.filter(p=>p.op===op).length;

(async()=>{await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));origin='http://127.0.0.1:'+server.address().port;browser=await chromium.launch({headless:true,channel:'msedge'});
try{
 await test('quote card shows verified SKU attributes every unit and original source timestamp',async(page,go)=>{
   await go();await open(page);assert.equal(posts.find(p=>p.op==='History').input.includeProducts,true);
   assert.match(await page.locator('.ga-product-sku').textContent(),/TH180-001/);
   assert.deepEqual(await page.locator('.ga-product-tags span').allTextContents(),['TH','Có đường','180ml']);
   assert.equal(await page.locator('.ga-quote tbody tr').count(),3);assert.equal(await page.locator('.ga-price-time').getAttribute('datetime'),readAt);
   assert.deepEqual(await page.locator('.ga-card-action').allTextContents(),['Giá sỉ','Tất cả đơn vị','Chọn hàng khác']);
   await page.locator('#gaPanel').screenshot({path:path.join(out,'product-desktop.png')});
 });
 await test('wholesale button sends one natural follow-up and reads a new result',async(page,go)=>{
   oneUnit=true;await go();await open(page);const before=readAt;await page.getByRole('button',{name:'Giá sỉ',exact:true}).click();await ready(page);
   assert.equal(count('Chat'),1);assert.equal(posts.find(p=>p.op==='Chat').input.body.message,'giá sỉ');
   assert.match(await page.locator('.ga-quote').textContent(),/8.500 đ\/Hộp/);assert.notEqual(readAt,before);
 });
 await test('all-unit action refetches instead of calculating missing units in the browser',async(page,go)=>{
   oneUnit=true;await go();await open(page);assert.equal(await page.locator('.ga-quote tbody tr').count(),1);
   await page.getByRole('button',{name:'Tất cả đơn vị',exact:true}).click();await ready(page);
   assert.equal(posts.find(p=>p.op==='Chat').input.body.message,'tất cả đơn vị');assert.equal(await page.locator('.ga-quote tbody tr').count(),3);
 });
 await test('other-product action removes the old card and asks for new product',async(page,go)=>{
   await go();await open(page);await page.getByRole('button',{name:'Chọn hàng khác',exact:true}).click();await ready(page);
   assert.equal(posts.find(p=>p.op==='Chat').input.body.message,'đổi mặt hàng khác');assert.equal(await page.locator('.ga-quote').count(),0);
   assert.match(await page.locator('#gaMessages').textContent(),/Mình muốn tìm mặt hàng nào/);
 });
 await test('card buttons disable pending work and cannot double-send',async(page,go)=>{
   chatDelay=350;await go();await open(page);const b=page.getByRole('button',{name:'Tất cả đơn vị',exact:true});await b.click();
   await page.waitForFunction(()=>document.querySelector('.ga-card-action').disabled);
   await b.evaluate(n=>n.dispatchEvent(new MouseEvent('click',{bubbles:true})));await ready(page);assert.equal(count('Chat'),1);
 });
 await test('detached old buttons cannot send after a new response in the same conversation',async(page,go)=>{
   await go();await open(page);await page.locator('.ga-card-action').first().evaluate(n=>window.oldProductButton=n);
   await page.getByRole('button',{name:'Tất cả đơn vị',exact:true}).click();await ready(page);
   await page.evaluate(()=>{oldProductButton.disabled=false;oldProductButton.dispatchEvent(new MouseEvent('click',{bubbles:true}));});assert.equal(count('Chat'),1);
 });
 await test('pending 202 keeps original recovery and disables product actions',async(page,go)=>{
   chatPending=true;await go();await open(page);await page.getByRole('button',{name:'Giá sỉ',exact:true}).click();await page.locator('#gaRecovery').waitFor();
   assert.equal(await page.locator('.ga-card-action:disabled').count(),3);assert.equal(count('Chat'),1);
 });
 await test('missing wholesale permission hides the action and price tier',async(page,go)=>{
   who.permissions=who.permissions.filter(p=>p!=='quotes.wholesale.read');await go();await open(page);
   assert.equal(await page.getByRole('button',{name:'Giá sỉ',exact:true}).count(),0);assert.doesNotMatch(await page.locator('.ga-quote table').textContent(),/Giá sỉ/);
 });
 await test('rights revoked before final context recheck clear card and transcript',async(page,go)=>{
   mode='revoked';await go();await page.locator('#gaOpen').click();await page.locator('#gaOpenRetry').waitFor();
   assert.equal(await page.locator('.ga-message').count(),0);assert.equal(await page.locator('.ga-card-action').count(),0);assert.match(await page.locator('#gaStatus').textContent(),/Quyền đọc kết quả/);
 });
 await test('expired card keeps original read time and visibly disables actions',async(page,go)=>{
   mode='expired';await go();await open(page);assert.equal(await page.locator('.ga-card-action:disabled').count(),3);
   assert.equal(await page.locator('.ga-price-time').getAttribute('datetime'),readAt);assert.equal(await page.locator('.ga-card-expired').isVisible(),true);
   await page.locator('.ga-card-action').first().evaluate(n=>n.dispatchEvent(new MouseEvent('click',{bubbles:true})));assert.equal(count('Chat'),0);
 });
 await test('card expiry disables actions while panel stays open',async(page,go)=>{
   mode='short';await go();await open(page);await page.waitForFunction(()=>Array.from(document.querySelectorAll('.ga-card-action')).every(b=>b.disabled));assert.equal(count('Chat'),0);
 });
 await test('older history keeps current actions once and never decorates earlier prices',async(page,go)=>{
   await go();await open(page);await page.locator('#gaHistoryMore').click();await ready(page);
   assert.equal(await page.locator('.ga-quote').count(),2);assert.equal(await page.locator('.ga-card-action').count(),3);assert.equal(await page.locator('.ga-price-time').count(),1);
 });
 await test('rich candidate cards keep verified choice number and name',async(page,go)=>{
   mode='choices';await go();await open(page);assert.equal(await page.locator('.ga-product-result').count(),2);
   await page.locator('.ga-rich-choice').nth(1).click();await ready(page);
   assert.equal(posts.find(p=>p.op==='Chat').input.body.message,'chọn 2. Pepsi chai 390ml');
 });
 await test('catalog-only product shows metadata and units without price actions',async(page,go)=>{
   mode='product';who.permissions=who.permissions.filter(p=>!p.startsWith('quotes.'));await go();await open(page);
   assert.equal(await page.locator('.ga-quote').count(),0);assert.equal(await page.locator('.ga-card-action').count(),0);assert.match(await page.locator('.ga-sale-units').textContent(),/Hộp, Lốc, Thùng/);
   assert.equal(await page.locator('.ga-source-note').textContent(),'DỮ LIỆU TEST');
 });
 await test('same-origin source image loads and broken image falls back without losing prices',async(page,go)=>{
   mode='photo';await go();await open(page);await page.waitForFunction(()=>document.querySelector('.ga-product-photo img')?.naturalWidth>0);
   await page.locator('#gaClose').click();mode='broken';await open(page);await page.waitForFunction(()=>!document.querySelector('.ga-product-photo img'));
   assert.equal(await page.locator('.ga-product-photo span').isVisible(),true);assert.equal(await page.locator('.ga-quote tbody tr').count(),3);
 });
 await test('HTML in SKU and attributes remains plain text',async(page,go)=>{
   mode='html';await go();await open(page);assert.equal(await page.locator('.ga-product-sku img,.ga-product-tags script').count(),0);
   assert.match(await page.locator('.ga-product-sku').textContent(),/<img/);
 });
 await test('missing SKU shows a clear label and does not hide verified prices',async(page,go)=>{
   mode='noSku';await go();await open(page);assert.equal(await page.locator('.ga-product-sku').textContent(),'Chưa có mã hàng');assert.equal(await page.locator('.ga-quote tbody tr').count(),3);
 });
 await test('external and traversal image URLs are rejected before network reads',async(page,go)=>{
   const requests=[];page.on('request',r=>requests.push(r.url()));await go();for(const value of ['unsafe','external']){
      mode=value;await page.locator('#gaOpen').click();await page.locator('#gaOpenRetry').waitFor();assert.equal(await page.locator('.ga-card-action').count(),0);await page.locator('#gaClose').click();}
   assert.equal(requests.some(r=>r.includes('other.invalid')||r.includes('secrets.png')),false);
 });
 await test('foreign store and wrong message ID never receive interactive card',async(page,go)=>{
   mode='scope';await go();await page.locator('#gaOpen').click();await page.locator('#gaOpenRetry').waitFor();assert.equal(await page.locator('.ga-card-action').count(),0);
   await page.locator('#gaClose').click();mode='old';await open(page);assert.equal(await page.locator('.ga-card-action').count(),0);
 });
 await test('store change discards old SKU and attaches actions to correct conversation',async(page,go)=>{
   await go();await open(page);await page.locator('#gaClose').click();who={...who,storeName:'Cửa hàng 2',scope:{...who.scope,storeId:'2'}};await open(page);
   assert.match(await page.locator('.ga-product-sku').textContent(),/STORE2/);await page.getByRole('button',{name:'Giá sỉ',exact:true}).click();await ready(page);
   assert.equal(posts.find(p=>p.op==='Chat').input.body.conversationId,id3);
 });
 await test('mobile product, price table and touch actions fit 320-pixel viewport',async(page,go)=>{
   await page.setViewportSize({width:320,height:844});await go();await open(page);const panel=await page.locator('#gaPanel').boundingBox();
   for(const button of await page.locator('.ga-card-action').all()){const b=await button.boundingBox();assert.ok(b.height>=44&&b.x>=panel.x&&b.x+b.width<=panel.x+panel.width);}
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:path.join(out,'product-mobile.png')});
 });
 fs.writeFileSync(path.join(out,'step3-results.json'),JSON.stringify({transport:'mock',geminiCalls:0,photoFixture:'1-pixel PNG for load test, placeholders in screenshots',cases},null,2));process.exitCode=cases.some(c=>!c.passed)?1:0;
}finally{await browser.close();server.close();}})().catch(e=>{console.error(e);server.close();process.exitCode=1;});
