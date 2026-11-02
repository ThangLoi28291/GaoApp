'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
const baseUrl=process.env.CUSTOMER_DISPLAY_PREVIEW_URL||'http://127.0.0.1:5199';
const output=path.resolve(__dirname,'../Logs/customer-display-halloween');
(async()=>{
 fs.mkdirSync(output,{recursive:true});
 const browser=await chromium.launch({channel:'chrome',headless:true});
 const context=await browser.newContext({viewport:{width:1440,height:900}}),page=await context.newPage();
 const errors=[],checks=[];page.on('pageerror',e=>errors.push(e.message));
 let draft=null,media=[];
 const lines=[['Sữa tươi 1L',2,32000],['Bánh quy nguyên cám',1,45000],['Táo đỏ',1,76000]].map(([itemName,quantity,unitPrice],i)=>({lineId:i+1,itemName,quantity,unitPrice,lineTotal:quantity*unitPrice,sellingUnitName:'gói'}));
 const receipt={orderId:9001,status:'Completed',storeName:'GẠO MART',storeAddress:'Bình Dương',terminalName:'Quầy 01',cashierName:'Nguyễn Minh An',customerName:'Nguyễn Minh Anh',orderNumber:'GAO-000123',finalizedAtUtc:'2026-10-10T12:42:00Z',lines,subtotal:185000,discountTotal:10000,grandTotal:175000,paidTotal:200000,changeDue:25000,balanceDue:0,payments:[{method:0,amount:200000}]};
 await context.route('**/admin/pos/screen',r=>r.fulfill({json:{currentDraft:draft}}));
 await context.route('**/admin/displaypromotion/activeforcustomerdisplay',r=>r.fulfill({json:{data:media}}));
 await context.route('**/admin/pos/customer-display/info',r=>r.fulfill({json:{storeId:1,terminalId:2,terminalName:'Quầy 01',cashierName:'Nguyễn Minh An',wifiName:'GaoMart Guest',wifiPassword:'Gao@2026'}}));
 await context.route('**/admin/customer-deposit/active-qr',r=>r.fulfill({status:204}));
 await context.route('**/admin/pos/orders/9001/receipt',r=>r.fulfill({json:receipt}));
 await context.route('https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/**',r=>r.fulfill({contentType:'application/javascript',body:'window.signalR={HubConnectionBuilder:class{withUrl(){return this}withAutomaticReconnect(){return this}build(){return window.fixtureHub={handlers:{},on(n,f){this.handlers[n]=f},onreconnecting(){},onreconnected(){},onclose(){},async start(){},async invoke(){}}}}};'}));
 const send=(eventType,payload={})=>page.evaluate(({eventType,payload})=>window.fixtureHub.handlers['pos:event']({eventType,payload,storeId:1,terminalId:2,orderId:9001}),{eventType,payload});
 const visible=async selector=>{
  const rect=await page.locator(selector).boundingBox(),size=page.viewportSize();
  assert(rect,selector+' visible');assert(rect.x>=-1&&rect.y>=-1&&rect.x+rect.width<=size.width+1&&rect.y+rect.height<=size.height+1,selector+' clipped: '+JSON.stringify({rect,size}));
 };
 const layout=async(state)=>{
  for(const size of [{width:1920,height:1080},{width:1440,height:900},{width:1024,height:768},{width:800,height:600},{width:768,height:1024},{width:390,height:844},{width:320,height:740}]){
   await page.setViewportSize(size);await page.waitForTimeout(80);
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'horizontal overflow');
   await visible('#cdCashierName');await visible('#cdTerminalName');await visible('#cdWifiName');await visible('#cdWifiPassword');
   if(state==='idle'){await visible('.cd-welcome h1');await visible('#cdIdleMediaStage');}
   if(state==='cart'){await visible('.cd-total-box');assert.equal(await page.locator('#cdGrandTotal').innerText(),'175.000');}
   if(state==='qr'){
    await visible('.cd-qr-frame');await visible('#cdQrAmount');await visible('#cdQrContent');
    assert(await page.locator('.cd-payment-visual').evaluate(e=>e.scrollHeight<=e.clientHeight+2),'QR card needs scrolling at '+JSON.stringify(size));
   }
   if(state==='receipt'){await visible('#cdReceiptTotal');await visible('#cdReceiptViewport');assert(await page.locator('#cdReceiptViewport').evaluate(e=>e.scrollWidth<=e.clientWidth+1));}
   if([1440,390].includes(size.width))await page.screenshot({path:path.join(output,state+'-'+size.width+'.png')});
  }
 };
 try{
  await page.goto(baseUrl+'/admin/pos/customer-display');
  await page.waitForFunction(()=>document.getElementById('customerDisplayApp').classList.contains('has-festival-idle')&&window.fixtureHub);
  await page.evaluate(()=>document.fonts.ready);
  assert(await page.evaluate(()=>document.fonts.check('700 32px "Baloo 2"','Ghé tiệm, đón Halloween.')));
  assert.match(await page.locator('.cd-welcome h1').evaluate(e=>getComputedStyle(e).fontFamily),/Baloo 2/);
  for(const url of ['/Admin/img/customer-display-halloween/festival-props-v1.webp','/Admin/img/customer-display-halloween/market-sprites-v1.webp','/Admin/fonts/baloo2/Baloo2-Variable.ttf'])assert((await context.request.get(baseUrl+url)).ok(),url+' loads locally');
  const motion=async()=>page.locator('.cd-hw-scene').evaluate(e=>Object.fromEntries(['cd-hw-flight','cd-hw-lantern','cd-hw-candy','cd-hw-pumpkin'].map(c=>{const s=getComputedStyle(e.querySelector('.'+c));return[c,s.transform+'|'+s.filter]})));
  const a=await motion();await page.waitForTimeout(750);const b=await motion();for(const key of Object.keys(a))assert.notEqual(a[key],b[key]);
  const wings=new Set();for(let i=0;i<9;i++){wings.add(await page.locator('.cd-hw-bat').first().evaluate(e=>getComputedStyle(e).backgroundPosition));await page.waitForTimeout(80);}assert.equal(wings.size,2);
  checks.push('Vietnamese font and local artwork load; pumpkin, candy, lanterns, flight and wing poses animate');
  await layout('idle');
  draft={...receipt,orderId:9001,paidTotal:0,balanceDue:175000,changeDue:0};
  await send('customer_payment_changed');await page.waitForFunction(()=>document.getElementById('cdGrandTotal').textContent==='175.000');
  assert.equal(await page.locator('.cd-line').count(),3);await layout('cart');
  const qr='data:image/svg+xml;base64,'+Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><rect width="100" height="100" fill="white"/><path d="M10 10h25v25H10zM65 10h25v25H65zM10 65h25v25H10zM45 45h10v10H45zM65 65h25v25H65z" fill="black"/></svg>').toString('base64');
  await send('customer_payment_qr_created',{bankName:'Ngân hàng kiểm thử',accountNumber:'0123456789',accountName:'GẠO MART',qrDataUrl:qr,amount:175000,content:'GAO-000123'});
  await page.locator('#cdQrBox:not(.d-none)').waitFor();await layout('qr');
  assert.equal(await page.locator('#cdQrImage').getAttribute('src'),qr);assert.equal(await page.locator('.cd-qr-frame .cd-hw-prop').count(),0);
  checks.push('idle/cart/QR and staff/Wi-Fi fit at seven viewport sizes; real POS numbers and QR image are retained');
  await page.setViewportSize({width:1440,height:900});
  media=[{id:1,mediaType:'text',title:'Ưu đãi tại tiệm',description:'Nội dung theo cấu hình cửa hàng',durationSeconds:60}];
  await send('customer_display_promotion_changed');await page.waitForFunction(()=>document.getElementById('customerDisplayApp').classList.contains('has-promotions'));
  assert(!await page.locator('#customerDisplayApp').evaluate(e=>e.classList.contains('has-festival-idle')));
  await page.evaluate(()=>{window.keptPromotion=document.querySelector('.cd-idle-media-text')});
  await send('customer_payment_changed');await page.locator('#cdPaymentOverlay').waitFor({state:'hidden'});
  draft={...draft,customerName:'Khách tiếp theo'};await send('cart_updated');await page.waitForFunction(()=>document.getElementById('cdCustomerName').textContent==='Khách tiếp theo');
  assert(await page.evaluate(()=>window.keptPromotion===document.querySelector('.cd-idle-media-text')));
  for(const size of [{width:1440,height:900},{width:800,height:600},{width:390,height:844}]){
   await page.setViewportSize(size);await visible('.cd-feature-panel');await visible('.cd-total-box');await visible('#cdIdleMediaStage');
   const selectors=['.cd-feature-panel','.cd-order-panel','#cdIdleScreen'];
   const boxes=await Promise.all(selectors.map(s=>page.locator(s).boundingBox()));
   for(let i=0;i<3;i++)for(let j=i+1;j<3;j++){const a=boxes[i],b=boxes[j];assert(a.x+a.width<=b.x+1||b.x+b.width<=a.x+1||a.y+a.height<=b.y+1||b.y+b.height<=a.y+1,'promotion overlaps checkout');}
  }
  checks.push('configured promotions stay mounted and separate from product/order during checkout');
  media=[];await send('customer_display_promotion_changed');await page.waitForFunction(()=>document.getElementById('customerDisplayApp').classList.contains('has-festival-idle'));
  await page.setViewportSize({width:1440,height:900});await send('order_finalized');await page.locator('#cdReceiptPaper[data-order-id="9001"]').waitFor();
  await page.waitForFunction(()=>document.getElementById('customerDisplayApp').classList.contains('is-halloween-celebrating'));
  assert.equal(await page.locator('#cdReceiptTotal').innerText(),'175.000 ₫');assert.equal(await page.locator('#cdReceiptChange').innerText(),'25.000 ₫');
  assert.equal(await page.locator('.cd-hw-confetti i').count(),14);await page.waitForTimeout(2800);await layout('receipt');
  await send('customer_display_reset');assert(await page.locator('#cdReceiptOverlay').isVisible());
  checks.push('committed receipt totals/change remain correct, confetti runs once, bill feed/scroll and reset retention survive');
  await page.emulateMedia({reducedMotion:'reduce'});
  assert.equal(await page.locator('.cd-hw-pumpkin').first().evaluate(e=>getComputedStyle(e).animationName),'none');
  assert(!await page.locator('#customerDisplayApp').evaluate(e=>e.classList.contains('is-halloween-celebrating')));
  checks.push('reduced motion disables decorative animations');
  assert.deepEqual(errors,[]);fs.writeFileSync(path.join(output,'browser-report.json'),JSON.stringify({checks,errors},null,2));
  console.log(JSON.stringify({passed:true,checks,errors},null,2));
 }catch(e){await page.screenshot({path:path.join(output,'failure.png')}).catch(()=>{});throw e;}
 finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
