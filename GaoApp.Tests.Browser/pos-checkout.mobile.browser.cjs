'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',x=>s+=x);process.stdin.on('end',()=>resolve(s));}));
 fs.mkdirSync(info.output,{recursive:true});
 const result={completed:false,checks:[],viewports:[],desktopProofs:[],mutations:[],errors:[],csp:[],printingSuppressed:true,serviceWorkerBlockedForFaultInjection:true};
 const pass=t=>{result.checks.push(t);console.log('PASS '+t);};
 const browser=await chromium.launch({channel:'chrome',headless:true});let page,releaseCollection=null,slowReady=null,networkMode='normal';
 async function login(account,size={width:390,height:844}){
  const context=await browser.newContext({viewport:size,hasTouch:true,serviceWorkers:'block'});
  await context.addInitScript(()=>{window.print=()=>{};});
  // Never contact a physical Print Helper/QZ or run a receipt document in this test.
  await context.route('**/*',async route=>{
   const url=new URL(route.request().url());
   if((url.hostname==='127.0.0.1'||url.hostname==='localhost')&&url.port!==new URL(account.baseUrl).port)
    return route.fulfill({status:503,contentType:'application/json',body:'{"message":"Physical printing disabled in test"}'});
   if(/\/admin\/pos\/orders\/\d+\/print/.test(url.pathname))return route.fulfill({contentType:'text/html',body:'<html><body>Receipt transport suppressed in test.</body></html>'});
   return route.continue();
  });
  const tab=await context.newPage();tab.setDefaultTimeout(20000);
  tab.on('pageerror',e=>result.errors.push(e.message));
  tab.on('console',m=>{if(/violates.*Content Security Policy|Refused to (?:load|execute|apply)/i.test(m.text()))result.csp.push(m.text());});
  await tab.goto(account.baseUrl+'/admin/account/login');await tab.locator('[name=UserName]').fill(account.user);await tab.locator('[name=Password]').fill(account.password);
  if(await tab.locator('[name=SelectedTerminalId]').count())await tab.locator('[name=SelectedTerminalId]').selectOption(String(account.terminalId));
  await Promise.all([tab.waitForURL(u=>!u.pathname.endsWith('/login')),tab.locator('#loginForm button[type=submit]').click()]);
  return {context,tab};
 }
 const state=()=>page.evaluate(()=>posApp.state.business.currentDraft);
 const modal=()=>page.locator('#paymentModal');
 async function idle(){await page.waitForFunction(()=>!posApp.state.network.busyScopes.size);await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));}
 async function settled(){await page.waitForFunction(()=>document.getAnimations().every(a=>a.playState!=='running'||a.effect?.getTiming().iterations===Infinity));}
 async function ready(route='/admin/pos'){
  await page.goto(info.manager.baseUrl+route);await page.waitForFunction(()=>window.posApp?.state.business.currentDraft&&window.PosOffline?.status().ready);await page.evaluate(()=>document.fonts.ready);await idle();
  assert.equal((await state()).orderId,info.orderId);assert.equal((await state()).grandTotal,131000);
 }
 async function open(){await page.locator('#btnOpenPayment').click();await page.locator('#paymentModal.show').waitFor();await settled();await page.waitForFunction(()=>document.activeElement?.id==='payAmount');}
 async function close(){await page.locator(page.viewportSize().width<768?'#paymentModal .btn-close':'#paymentModal .pos-payment-footer__actions [data-bs-dismiss=modal]').tap();await modal().waitFor({state:'hidden'});await page.locator('.modal-backdrop').waitFor({state:'hidden'});}
 async function geometry(width,height,route){
  await page.setViewportSize({width,height});await idle();await settled();
  await page.waitForFunction(()=>Math.abs(document.querySelector('#paymentModal').getBoundingClientRect().height-innerHeight)<2);
  const g=await modal().evaluate(el=>{const rect=node=>{const b=node.getBoundingClientRect();return {x:b.x,y:b.y,right:b.right,bottom:b.bottom,width:b.width,height:b.height};};return {w:innerWidth,h:innerHeight,document:document.documentElement.scrollWidth,modal:rect(el.querySelector('.modal-content')),body:rect(el.querySelector('.modal-body')),footer:rect(el.querySelector('.modal-footer')),submit:rect(el.querySelector('#btnAddPayment')),inputFont:getComputedStyle(el.querySelector('#payAmount')).fontSize,host:el.querySelector('#btnAddPayment').parentElement.className};});
  assert.ok(g.document<=width+1&&g.modal.x>=0&&g.modal.right<=width+1&&g.modal.bottom<=height+1,JSON.stringify(g));
  assert.ok(g.body.height>=44&&g.footer.bottom<=height+1&&g.body.bottom<=g.footer.y+1,JSON.stringify(g));assert.ok(g.submit.height>=44&&g.submit.width>=44&&g.submit.y>=g.footer.y,JSON.stringify(g));
  const targets=await page.locator('#paymentModal .modal-header button, #paymentModal .modal-footer button:visible, #paymentModal .pos-payment-method-button:visible, #paymentModal .pos-payment-quick-button:visible').evaluateAll(nodes=>nodes.map(el=>{const b=el.getBoundingClientRect();return {id:el.id,width:b.width,height:b.height};}));
  for(const t of targets)assert.ok(t.width>=44&&t.height>=44,JSON.stringify(t));
  assert.ok(parseFloat(g.inputFont)>=16);assert.equal(await page.locator('#btnAddPayment').count(),1);result.viewports.push({route,width,height,...g});
 }
 async function desktop(route,width){
  await page.setViewportSize({width,height:1000});await ready(route);await open();
  const p=await page.evaluate(()=>{
   const snap=()=>Array.from(document.querySelectorAll('#paymentModal, #paymentModal *, #invoiceIntentModal, #invoiceIntentModal *')).map(el=>{const s=getComputedStyle(el),b=el.getBoundingClientRect();return {id:el.id,tag:el.tagName,css:Array.from(s).map(p=>[p,s.getPropertyValue(p)]),box:[b.x,b.y,b.width,b.height]};});
   const link=document.querySelector('link[href*="pos.checkout.mobile.css"]'),before=snap();link.disabled=true;const after=snap();link.disabled=false;
   return {count:before.length,equal:JSON.stringify(before)===JSON.stringify(after),diff:before.flatMap((x,i)=>JSON.stringify(x)===JSON.stringify(after[i])?[]:[x.id||x.tag]).slice(0,4)};
  });assert.ok(p.equal,JSON.stringify(p));assert.equal(await page.locator('.pos-checkout-mobile-submit').count(),0);result.desktopProofs.push({route,width,...p});
  if(width===1440)await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-desktop.png':'prime-desktop.png'),animations:'disabled'});await close();
 }
 async function keyboardViewport(route){
  await page.locator('#payAmount').focus();
  await page.evaluate(()=>{window.__checkoutRealViewport=window.visualViewport;Object.defineProperty(window,'visualViewport',{configurable:true,value:{height:360,width:390,offsetTop:0,offsetLeft:0,scale:1}});__checkoutRealViewport.dispatchEvent(new Event('resize'));});
  await page.waitForFunction(()=>Math.abs(document.querySelector('#paymentModal').getBoundingClientRect().height-360)<2);
  const g=await modal().evaluate(el=>{const f=el.querySelector('.modal-footer').getBoundingClientRect(),b=el.querySelector('.modal-body').getBoundingClientRect();return {footerBottom:f.bottom,bodyHeight:b.height};});assert.ok(g.footerBottom<=361&&g.bodyHeight>=44,JSON.stringify(g));
  result.keyboardViewports??=[];result.keyboardViewports.push({route,...g});
  await page.evaluate(()=>{Object.defineProperty(window,'visualViewport',{configurable:true,value:__checkoutRealViewport});__checkoutRealViewport.dispatchEvent(new Event('resize'));});
  await page.waitForFunction(()=>document.querySelector('#paymentModal').getBoundingClientRect().height>800);
 }
 try{
  const main=await login(info.manager,{width:1440,height:1000});page=main.tab;await ready();
  assert.equal((await state()).paidTotal,0);assert.equal((await state()).askBeforePrintingReceipt,true);
  await desktop('/admin/pos',1440);await desktop('/admin/pos',768);await desktop('/admin/pos',1024);
  await desktop('/admin/pos/legacy',1440);await desktop('/admin/pos/legacy',768);
  pass('Five tablet/desktop comparisons preserve every payment/feedback computed property and box at 768/1024/1440');
  for(const route of ['/admin/pos','/admin/pos/legacy']){
   await page.setViewportSize({width:390,height:844});await ready(route);
   await page.evaluate(()=>{window.__checkoutButton=document.getElementById('btnAddPayment');});await open();
   assert.equal(await page.evaluate(()=>document.getElementById('btnAddPayment')===window.__checkoutButton),true);
   if(route.includes('legacy'))assert.equal(await page.locator('.pos-checkout-mobile-submit #btnAddPayment').count(),1);
   else assert.equal(await page.locator('.pos-prime-payment-submit-host #btnAddPayment').count(),1);
   for(const [w,h] of [[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]])await geometry(w,h,route);
   await page.setViewportSize({width:390,height:844});await settled();await keyboardViewport(route);
   await page.locator('#paymentModal .modal-body').evaluate(el=>{el.scrollTop=0;});
   await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-phone.png':'phone-summary-methods.png')});
   await page.locator('#payAmount').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-phone-amount.png':'phone-amount.png')});
   await page.locator('#payAmount').fill('150000');await page.locator('#payAmount').press('Tab');
   assert.match(await page.locator('#payPreviewChange').innerText(),/19.000/);await close();assert.equal((await state()).paidTotal,0);
   await open();assert.equal(await page.locator('#payAmount').inputValue(),'131.000');assert.equal((await state()).orderId,info.orderId);await close();
   await page.setViewportSize({width:1440,height:1000});await idle();await settled();assert.equal(await page.locator('.pos-checkout-mobile-submit').count(),0);
   assert.equal(await page.evaluate(()=>document.getElementById('btnAddPayment')===__checkoutButton),true);
   if(route.includes('legacy'))assert.equal(await page.locator('#paymentForm #btnAddPayment').count(),1);
  }
  pass('Prime/legacy: seven phone sizes each, simulated keyboard, one original footer submit, cancel/reopen and exact node restoration');
  await page.setViewportSize({width:390,height:844});await ready('/admin/pos/v3');await open();
  await page.locator('[data-pay-method-value="1"]').tap();assert.equal(await page.locator('#payMethod').inputValue(),'1');assert.equal(await page.locator('#paymentQrBox').isVisible(),true);
  await page.locator('[data-pay-method-value="2"]').tap();await page.locator('#payAmount').fill('31000');await page.locator('#payReference').fill('');
  await page.locator('#btnAddPayment').tap();await page.locator('#paymentInlineErrorBox:visible').waitFor();assert.equal(await page.locator('#payAmount').inputValue(),'31.000');assert.equal((await state()).paidTotal,0);
  assert.equal(await page.evaluate(()=>document.activeElement.id),'payReference');
  await page.screenshot({path:path.join(info.output,'phone-card-validation.png')});pass('Method selection and missing card reference retain validation, amount, focus and unpaid cart without collecting');
  await page.route('**/admin/pos/cart/current/payments',async route=>{
   const body=route.request().postDataJSON();result.mutations.push(body);const mode=networkMode;
   if(mode==='slow')await new Promise(resolve=>{releaseCollection=resolve;slowReady?.();});
   if(mode==='lost'){
    const response=await route.fetch();assert.equal(response.status(),200);
    result.ackFailure={status:409,serverCommittedBeforeResponse:true};
    await route.fulfill({status:409,contentType:'application/json',body:'{"message":"Lần thu đang được xử lý. Vui lòng thử lại cùng mã."}'});return;
   }
   await route.continue();
  });
  await page.locator('#payReference').fill('MOBILE-CARD');await page.locator('#payProvider').fill('POS Test');networkMode='slow';
  const requestStarted=new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('Slow collection did not reach its route')),20000);slowReady=()=>{clearTimeout(timer);resolve();};});
  await page.locator('#btnAddPayment').tap();await requestStarted;await page.waitForFunction(()=>document.getElementById('btnAddPayment').disabled);
  await page.locator('#payAmount').press('Enter');await page.locator('#payAmount').press('Enter');assert.equal(result.mutations.length,1);
  assert.ok(releaseCollection);networkMode='normal';releaseCollection();
  await page.waitForFunction(()=>posApp.state.business.currentDraft.paidTotal===31000);await idle();assert.match(await page.locator('#paySumBalance').innerText(),/100.000/);
  await close();await open();assert.match(await page.locator('#paymentModalListBox').innerText(),/31.000/);assert.equal(await page.locator('#payAmount').inputValue(),'100.000');
  pass('Slow card collection blocks repeated Enter; one real 31,000 installment persists in the ledger across close/reopen');
  await page.locator('#payAmount').fill('20000');networkMode='lost';await page.locator('#btnAddPayment').tap();
  await page.locator('#paymentInlineErrorBox:visible').waitFor();await idle();
  const first=result.mutations.at(-1);assert.equal(first.amount,20000);await close();await open();
  assert.equal(await page.locator('#payAmount').inputValue(),'20.000');assert.match(await page.locator('#paymentInlineErrorBox').innerText(),/chưa xác định kết quả/);
  const restored=await state();assert.equal(await page.locator('#payPreviewBalance').innerText(),Math.max(0,restored.balanceDue-20000).toLocaleString('vi-VN'));
  assert.equal(await page.locator('#payPreviewChange').innerText(),'0');
  await page.screenshot({path:path.join(info.output,'phone-recover-collection.png')});networkMode='normal';await page.locator('#btnAddPayment').tap();
  await page.waitForFunction(()=>posApp.state.business.currentDraft.paidTotal===51000);await idle();
  assert.equal(result.mutations.at(-1).clientRequestId,first.clientRequestId);assert.equal((await state()).payments.length,2);
  pass('A committed collection followed by retryable 409 restores its pending amount and retries the same identity without collecting twice');
  await page.locator('#payAmount').fill('100000');await page.locator('#payAmount').press('Tab');assert.match(await page.locator('#payPreviewChange').innerText(),/20.000/);
  await page.screenshot({path:path.join(info.output,'phone-cash-change.png')});await page.locator('#btnAddPayment').tap();
  await page.locator('#receiptPrintChoices:not([hidden])').waitFor();assert.match(await page.locator('#cashReceiptChange').innerText(),/20.000/);
  await page.screenshot({path:path.join(info.output,'phone-completed-change.png')});await page.locator('#btnReceiptDoNotPrint').tap();
  await page.locator('#invoiceIntentModal').waitFor({state:'hidden'});
  const server=await page.request.get(info.manager.baseUrl+'/admin/pos/orders/'+info.orderId);assert.equal(server.status(),200);const completed=await server.json();
  assert.equal(completed.status,'Completed');assert.equal(completed.grandTotal,131000);assert.equal(completed.paidTotal,151000);assert.equal(completed.changeDue,20000);assert.equal(completed.payments.length,3);result.completedReceipt=completed;
  assert.ok(result.mutations.length>=4);assert.equal(new Set(result.mutations.map(x=>x.clientRequestId)).size,3);
  assert.equal(result.mutations.filter(x=>x.amount===31000).length,1);assert.equal(result.mutations.filter(x=>x.amount===100000).length,1);
  pass('Cash 100,000 finishes the remaining 80,000 once; original receipt preference/No print and 20,000 change agree with server');
  await main.context.close();
  const limited=await login(info.limited);page=limited.tab;const html=await (await page.request.get(info.limited.baseUrl+'/admin/pos')).text();
  for(const id of ['posCreditPanel','posDepositPanel','btnOpenDelivery'])assert.ok(!html.includes('id="'+id+'"'),id+' must obey server permission');await limited.context.close();
  const denied=await login(info.denied);page=denied.tab;
  for(const route of ['/admin/pos','/admin/pos/v3','/admin/pos/legacy','/admin/pos/orders/'+info.orderId]){const r=await page.request.get(info.denied.baseUrl+route,{maxRedirects:0});assert.ok([302,403].includes(r.status()),route+' '+r.status());}
  await denied.context.close();
  const other=await login(info.other);page=other.tab;const forbidden=await page.request.get(info.other.baseUrl+'/admin/pos/orders/'+info.orderId,{maxRedirects:0});
  assert.ok([400,403,404].includes(forbidden.status()),'Other store '+forbidden.status());
  const foreignBody=await forbidden.text();assert.doesNotMatch(foreignBody,/"orderNumber"|"grandTotal"|"customerName"/i);
  if(forbidden.status()===400){const problem=JSON.parse(foreignBody);assert.match(problem.message||problem.detail||JSON.stringify(problem),/Không tìm thấy đơn hàng/);}
  result.otherStoreRejection={status:forbidden.status(),orderDataNotDisclosed:true};await other.context.close();
  pass('Server omits credit/deposit/delivery without permissions, rejects denied POS access and isolates the order from another store');
  assert.deepEqual(result.errors,[]);assert.deepEqual(result.csp,[]);result.completed=true;
 }finally{
  if(!result.completed&&page&&!page.isClosed()){
   result.failureState=await page.evaluate(()=>({draft:window.posApp?.state.business.currentDraft,error:document.getElementById('paymentInlineErrorBox')?.textContent,amount:document.getElementById('payAmount')?.value})).catch(()=>null);
   await page.screenshot({path:path.join(info.output,'failure.png'),fullPage:true}).catch(()=>{});
  }
  fs.writeFileSync(path.join(info.output,'result.json'),JSON.stringify(result,null,2));await browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
