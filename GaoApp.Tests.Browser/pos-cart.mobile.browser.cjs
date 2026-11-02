'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',x=>s+=x);process.stdin.on('end',()=>resolve(s));}));
 fs.mkdirSync(info.output,{recursive:true});
 const result={completed:false,checks:[],viewports:[],desktopProofs:[],errors:[],csp:[]};
 const pass=t=>{result.checks.push(t);console.log('PASS '+t);};
 const browser=await chromium.launch({channel:'chrome',headless:true});let page;
 async function login(account,size={width:390,height:844}){
  const context=await browser.newContext({viewport:size,hasTouch:true});const tab=await context.newPage();tab.setDefaultTimeout(20000);
  tab.on('pageerror',e=>result.errors.push(e.message));
  tab.on('console',m=>{if(/violates.*Content Security Policy|Refused to (?:load|execute|apply)/i.test(m.text()))result.csp.push(m.text());});
  await tab.route('**/uploads/products/scan*',r=>r.fulfill({contentType:'image/svg+xml',body:'<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80"><rect width="80" height="80" rx="12" fill="#e6efff"/></svg>'}));
  await tab.goto(account.baseUrl+'/admin/account/login');await tab.locator('[name=UserName]').fill(account.user);await tab.locator('[name=Password]').fill(account.password);
  if(await tab.locator('[name=SelectedTerminalId]').count())await tab.locator('[name=SelectedTerminalId]').selectOption(String(account.terminalId));
  await Promise.all([tab.waitForURL(u=>!u.pathname.endsWith('/login')),tab.locator('#loginForm button[type=submit]').click()]);return {context,tab};
 }
 const state=()=>page.evaluate(()=>posApp.state.business.currentDraft);
 async function idle(){await page.waitForFunction(()=>!posApp.state.network.busyScopes.size);await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));}
 async function ready(route='/admin/pos'){
  await page.goto(info.manager.baseUrl+route);await page.waitForFunction(()=>window.posApp?.state.business.currentDraft&&window.PosOffline?.status().ready);
  await page.evaluate(()=>document.fonts.ready);await idle();assert.equal((await state()).orderId,info.orderId);
 }
 async function saved(predicate,arg){await page.waitForFunction(predicate,arg);await idle();}
 const row=id=>page.locator(`tr[data-line-id="${id}"]`);
 async function action(id,selector){await row(id).locator('.pos-line-actions-toggle').tap();await row(id).locator('.dropdown-menu.show').waitFor();await row(id).locator(selector).tap();}
 async function closeModal(id){
  const selector=page.viewportSize().width>=768?' .modal-footer [data-bs-dismiss=modal]':' .btn-close';
  await page.locator(id+selector).tap();await page.locator(id).waitFor({state:'hidden'});
 }
 async function contextOpen(){
  if(await page.locator('#posPrimeContextSheet.show').count())return;
  await page.locator('#posPrimeContextLauncher').tap();await page.locator('#posPrimeContextSheet.show').waitFor();
 }
 async function contextClose(){if(await page.locator('#posPrimeContextSheet.show').count()){await page.locator('#posPrimeContextSheet .btn-close').tap();await page.locator('#posPrimeContextSheet').waitFor({state:'hidden'});}}
 async function modalGeometry(id){
  await page.waitForFunction(selector=>document.querySelector(selector).getAnimations({subtree:true}).every(a=>a.playState!=='running'||a.effect?.getTiming().iterations===Infinity),id);
  const value=await page.locator(id).evaluate(el=>{const b=el.querySelector('.modal-content').getBoundingClientRect();return {left:b.left,right:b.right,top:b.top,bottom:b.bottom,w:innerWidth,h:innerHeight,bodyOverflow:el.querySelector('.modal-body').scrollHeight>el.querySelector('.modal-body').clientHeight};});
  assert.ok(value.left>=0&&value.right<=value.w+1&&value.top>=0&&value.bottom<=value.h+1,JSON.stringify({id,...value}));
  const targets=await page.locator(id+' button:visible').evaluateAll(nodes=>nodes.map(el=>{const b=el.getBoundingClientRect();return {id:el.id,width:b.width,height:b.height};}));
  for(const t of targets)assert.ok(t.width>=44&&t.height>=44,JSON.stringify(t));return value;
 }
 async function desktopProof(route,width){
  await contextClose();await page.setViewportSize({width,height:1000});await ready(route);
  const proof=await page.evaluate(()=>{
   const snap=()=>Array.from(document.querySelectorAll('#posShell, #posShell *, #customerRewardModal *, #useRewardVoucherModal *, #lineDiscountModal *, #qtyEditModal *')).map(el=>{const s=getComputedStyle(el),b=el.getBoundingClientRect();return {tag:el.tagName,id:el.id,css:Array.from(s).map(p=>[p,s.getPropertyValue(p)]),box:[b.x,b.y,b.width,b.height]};});
   const link=document.querySelector('link[href*="pos.cart.mobile.css"]'),before=snap();link.disabled=true;const after=snap();link.disabled=false;
   return {count:before.length,equal:JSON.stringify(before)===JSON.stringify(after),differences:before.flatMap((x,i)=>JSON.stringify(x)===JSON.stringify(after[i])?[]:[x.id||x.tag]).slice(0,8)};
  });assert.ok(proof.equal,JSON.stringify(proof));result.desktopProofs.push({route,width,...proof});
  if(width===1440)await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-desktop.png':'prime-desktop.png'),animations:'disabled',caret:'hide'});
 }
 try{
  const main=await login(info.manager);page=main.tab;await ready();
  const initial=await state();assert.equal(initial.lines.length,3);assert.equal(initial.customerId,info.cartSeed.RetailId);
  const water=initial.lines.find(x=>x.variantId===info.waterId),pack=initial.lines.find(x=>x.sellingUnitName==='Lốc');
  assert.equal(pack.multiplier,6);assert.equal(pack.baseQuantity,6);
  pass('Phone loads the existing retail customer, three original cart rows and pack conversion from the server');
  await row(water.lineId).locator('[data-inc-line-id]').tap();
  await saved(id=>posApp.state.business.currentDraft.lines.find(x=>x.lineId===id).quantity===3,water.lineId);
  await row(water.lineId).locator('[data-dec-line-id]').tap();
  await saved(id=>posApp.state.business.currentDraft.lines.find(x=>x.lineId===id).quantity===2,water.lineId);
  await row(water.lineId).locator('.qty-input').fill('3');await row(water.lineId).locator('.qty-input').press('Tab');
  await saved(id=>posApp.state.business.currentDraft.lines.find(x=>x.lineId===id).quantity===3,water.lineId);
  pass('Original plus, minus and direct quantity input persist correctly through touch and blur');
  await action(pack.lineId,'[data-open-qty-line-id]');await page.locator('#qtyEditModal.show').waitFor();
  await modalGeometry('#qtyEditModal');await page.locator('#qtyEditValue').fill('2');await page.locator('#btnSaveQtyEdit').tap();
  await saved(id=>posApp.state.business.currentDraft.lines.find(x=>x.lineId===id).quantity===2,pack.lineId);
  await page.locator('#qtyEditModal').waitFor({state:'hidden'});
  assert.match(await row(pack.lineId).locator('.pos-qty-summary').innerText(),/12 Lon/);
  await action(pack.lineId,'[data-price-line-id]');await page.locator('#linePriceTableModal.show').waitFor();
  await modalGeometry('#linePriceTableModal');assert.match(await page.locator('#linePriceTableContent').innerText(),/Lốc[\s\S]*x6/);
  await page.screenshot({path:path.join(info.output,'phone-pack-price.png')});await closeModal('#linePriceTableModal');
  pass('In-card Bootstrap actions remain reachable; original quantity editor and price/conversion table preserve 2 Lốc = 12 Lon');
  await action(water.lineId,'[data-line-discount-id]');await page.locator('#lineDiscountModal.show').waitFor();await modalGeometry('#lineDiscountModal');
  await page.locator('#lineDiscountAmount').fill('1000');await page.locator('#btnSaveLineDiscount').tap();
  await saved(id=>posApp.state.business.currentDraft.lines.find(x=>x.lineId===id).lineDiscount===1000,water.lineId);
  await page.locator('#lineDiscountModal').waitFor({state:'hidden'});assert.match(await row(water.lineId).innerText(),/Giảm dòng/);
  pass('Original line discount editor applies exactly 1,000 and renders the row adjustment');
  await contextOpen();await page.locator('[data-toggle-meta=note]').tap();
  await page.locator('#txtOrderNote').fill('Giao sau 17 giờ — gọi trước khi đến.');await page.locator('#btnSaveOrderNote').tap();
  await saved(()=>posApp.state.business.currentDraft.note==='Giao sau 17 giờ — gọi trước khi đến.');
  await page.locator('[data-meta-section=note].is-collapsed').waitFor();
  await page.locator('[data-toggle-meta=discount]').tap();await page.locator('#txtOrderDiscount').fill('3000');await page.locator('#btnSaveOrderDiscount').tap();
  await saved(()=>posApp.state.business.currentDraft.orderDiscount===3000);
  await page.locator('[data-meta-section=discount].is-collapsed').waitFor();await page.locator('[data-toggle-meta=discount]').tap();
  await page.locator('#btnClearOrderDiscount').tap();await saved(()=>posApp.state.business.currentDraft.orderDiscount===0);
  await page.locator('[data-meta-section=discount].is-collapsed').waitFor();await page.locator('[data-toggle-meta=discount]').tap();
  await page.locator('#txtOrderDiscount').fill('3000');await page.locator('#btnSaveOrderDiscount').tap();await saved(()=>posApp.state.business.currentDraft.orderDiscount===3000);
  await page.locator('[data-meta-section=discount].is-collapsed').waitFor();
  assert.match(await page.locator('#metaOrderNoteSummary').innerText(),/Đã có ghi chú/);
  pass('Existing task editors save note, apply/remove/reapply order discount without duplicate inputs or handlers');
  await page.locator('[data-customer-action=reward]').tap();await page.locator('#customerRewardModal.show').waitFor();
  await modalGeometry('#customerRewardModal');assert.match(await page.locator('#rewardModalSummary').innerText(),/200/);
  await page.screenshot({path:path.join(info.output,'phone-points.png')});await closeModal('#customerRewardModal');
  await contextOpen();await page.locator('[data-customer-action=use-voucher]').tap();await page.locator('#useRewardVoucherModal.show').waitFor();
  await page.locator('.use-voucher-check').waitFor();await modalGeometry('#useRewardVoucherModal');
  await page.locator('.pos-use-voucher-item').tap();assert.match(await page.locator('#useVoucherTotal').innerText(),/10.000/);
  await page.screenshot({path:path.join(info.output,'phone-voucher.png')});await page.locator('#btnApplyRewardVouchers').tap();
  await saved(()=>posApp.state.business.currentDraft.voucherDiscountTotal===10000);await page.locator('#useRewardVoucherModal').waitFor({state:'hidden'});
  pass('Point summary and whole-row voucher selection use original customer owners; the server applies 10,000');
  await contextOpen();await page.screenshot({path:path.join(info.output,'phone-customer-tools.png')});await contextClose();
  // Use the original customer-search UI, including its existing reprice behavior.
  async function chooseCustomer(id){
   await page.locator('#txtBarcode').fill('@ Minh Anh');await page.locator(`#barcodeAutocomplete [data-customer-id="${id}"]`).waitFor();
   await page.locator(`#barcodeAutocomplete [data-customer-id="${id}"]`).tap();await saved(value=>posApp.state.business.currentDraft.customerId===value,id);
  }
  await chooseCustomer(info.cartSeed.WholesaleId);await contextOpen();assert.equal(await page.locator('[data-customer-action=reward]').count(),0);
  assert.equal(await page.locator('[data-customer-action=use-voucher]').count(),1);assert.match(await page.locator('#customerInfoBox').innerText(),/KHÁCH SỈ/);
  await contextClose();await chooseCustomer(info.cartSeed.RetailId);await contextOpen();assert.equal(await page.locator('[data-customer-action=reward]').count(),1);
  await page.locator('[data-customer-action=use-voucher]').tap();await page.locator('#useRewardVoucherModal.show').waitFor();await page.locator('.use-voucher-check').waitFor();
  await page.locator('.use-voucher-check').check();await page.locator('#btnApplyRewardVouchers').tap();await saved(()=>posApp.state.business.currentDraft.voucherDiscountTotal===10000);
  await page.locator('#useRewardVoucherModal').waitFor({state:'hidden'});await contextClose();
  pass('Search selects wholesale/retail with existing repricing; wholesale hides points and keeps voucher action, retail restores eligibility');
  const final=await state();result.finalDraft=final;
  const server=await page.request.get(info.manager.baseUrl+'/admin/pos/'+info.orderId);assert.equal(server.status(),200);const actual=await server.json();
  for(const key of ['orderId','customerId','note','orderDiscount','voucherDiscountTotal','grandTotal','balanceDue','paidTotal'])assert.equal(final[key],actual[key],key);
  assert.deepEqual(final.lines.map(x=>[x.lineId,x.quantity,x.baseQuantity,x.lineTotal]),actual.lines.map(x=>[x.lineId,x.quantity,x.baseQuantity,x.lineTotal]));
  pass('Displayed draft quantities, discounts, customer, note and totals match a fresh authenticated HTTP read');
  for(const [width,height] of [[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]]){
   await page.setViewportSize({width,height});await idle();await row(water.lineId).scrollIntoViewIfNeeded();
   const g=await row(water.lineId).evaluate(el=>{const b=el.getBoundingClientRect(),q=el.querySelector('.pos-qty-inline').getBoundingClientRect(),t=el.querySelector('.pos-line-total').getBoundingClientRect();return {width:innerWidth,document:document.documentElement.scrollWidth,row:{x:b.x,right:b.right,width:b.width},qty:{right:q.right,width:q.width},total:{x:t.x,right:t.right},qtyFont:getComputedStyle(el.querySelector('.qty-input')).fontSize};});
   assert.ok(g.document<=width+1&&g.row.x>=0&&g.row.right<=width+1&&g.qty.right<=g.total.x+1,JSON.stringify(g));assert.equal(g.qtyFont,'16px');
   for(const s of ['.qty-btn','.qty-input','.pos-line-actions-toggle'])for(const b of await row(water.lineId).locator(s).evaluateAll(nodes=>nodes.map(el=>{const b=el.getBoundingClientRect();return [b.width,b.height];})))assert.ok(b[0]>=44&&b[1]>=44,JSON.stringify(b));
   result.viewports.push({width,height,...g});await page.screenshot({path:path.join(info.output,`phone-${width}-${height}.png`),animations:'disabled'});
  }
  pass('Seven phone/short-height viewports keep cart cards in bounds with 44px targets and 16px quantity inputs');
  await page.setViewportSize({width:320,height:568});await contextOpen();
  await page.locator('[data-customer-action=use-voucher]').tap();await page.locator('#useRewardVoucherModal.show').waitFor();await modalGeometry('#useRewardVoucherModal');await closeModal('#useRewardVoucherModal');
  await contextOpen();await page.locator('[data-customer-action=reward]').tap();await page.locator('#customerRewardModal.show').waitFor();await modalGeometry('#customerRewardModal');await closeModal('#customerRewardModal');await contextClose();
  pass('Customer reward/voucher modal headers, scroll bodies and footer actions fit 320px phone');
  for(const width of [768,1024,1440])await desktopProof('/admin/pos',width);
  for(const width of [768,1440])await desktopProof('/admin/pos/legacy',width);
  await action(pack.lineId,'[data-price-line-id]');await page.locator('#linePriceTableModal.show').waitFor();
  assert.match(await page.locator('#linePriceTableContent').innerText(),/Lốc[\s\S]*x6/);await closeModal('#linePriceTableModal');
  pass('Tablet/desktop at 768/1024/1440 keep every computed property and element box identical with the new CSS enabled or disabled');
  await page.setViewportSize({width:390,height:844});await ready('/admin/pos/legacy');
  await action(water.lineId,'[data-open-qty-line-id]');await page.locator('#qtyEditModal.show').waitFor();await modalGeometry('#qtyEditModal');await closeModal('#qtyEditModal');
  const active=await row(water.lineId).evaluate(el=>{const style=getComputedStyle(el),wrap=el.querySelector('.pos-line-name-wrap'),name=el.querySelector('.pos-line-name-main');return {display:style.display,position:style.position,rowWidth:el.getBoundingClientRect().width,wrapWidth:wrap.getBoundingClientRect().width,nameWidth:name.getBoundingClientRect().width};});
  assert.equal(active.display,'grid');assert.equal(active.position,'relative');assert.ok(active.wrapWidth>=active.rowWidth-30&&active.nameWidth>=120,JSON.stringify(active));result.legacyActiveRow=active;
  await row(water.lineId).scrollIntoViewIfNeeded();await page.screenshot({path:path.join(info.output,'legacy-phone-active.png')});
  await row(pack.lineId).scrollIntoViewIfNeeded();await page.screenshot({path:path.join(info.output,'legacy-phone.png')});
  assert.deepEqual((await state()).lines,final.lines);
  pass('Legacy phone uses the same cart cards and quantity editor, and keeps the server draft unchanged');
  await ready('/admin/pos/v3');assert.deepEqual((await state()).lines,final.lines);
  await page.emulateMedia({reducedMotion:'reduce',forcedColors:'active'});await page.screenshot({path:path.join(info.output,'phone-forced-colors.png')});
  await page.emulateMedia({reducedMotion:'no-preference',forcedColors:'none'});
  assert.equal(await page.locator('#txtOrderNote').count(),1);assert.equal(await page.locator('#txtOrderDiscount').count(),1);assert.equal(await page.locator('#customerInfoBox').count(),1);
  await contextOpen();await page.screenshot({path:path.join(info.output,'phone-customer-tools-final.png')});
  await page.locator('[data-customer-action=reward]').tap();await page.locator('#customerRewardModal.show').waitFor();await modalGeometry('#customerRewardModal');
  assert.equal(await page.locator('#btnRedeemRewardVoucher').evaluate(el=>getComputedStyle(el).backgroundColor),'rgb(37, 99, 235)');
  const stats=await page.locator('#rewardModalSummary strong').evaluateAll(nodes=>nodes.map(el=>{const b=el.getBoundingClientRect();return {text:el.textContent.trim(),width:b.width,height:b.height,lineHeight:parseFloat(getComputedStyle(el).lineHeight),scrollWidth:el.scrollWidth,clientWidth:el.clientWidth};}));
  for(const s of stats)assert.ok(s.height<=s.lineHeight+1&&s.scrollWidth<=s.clientWidth,JSON.stringify(s));result.rewardStats=stats;
  await page.screenshot({path:path.join(info.output,'phone-points-final.png')});await closeModal('#customerRewardModal');
  await main.context.close();pass('V3 reload, forced colors and reduced motion keep one original customer/note/discount control');
  const denied=await login(info.denied);page=denied.tab;
  for(const route of ['/admin/pos','/admin/pos/v3','/admin/pos/legacy','/admin/pos/cart/current',`/admin/api/customers/${info.cartSeed.RetailId}/reward-summary`]){
   const response=await page.request.get(info.denied.baseUrl+route,{maxRedirects:0});assert.ok([302,403].includes(response.status()),route+' '+response.status());
   if(response.status()===302)assert.match(response.headers().location,/access.?denied|forbidden/i);
  }
  await denied.context.close();pass('Server still denies POS/cart/reward summary to an account without the required permissions');
  assert.deepEqual(result.errors,[]);assert.deepEqual(result.csp,[]);result.completed=true;
 }finally{
  if(!result.completed&&page&&!page.isClosed())await page.screenshot({path:path.join(info.output,'failure.png'),fullPage:true}).catch(()=>{});
  fs.writeFileSync(path.join(info.output,'result.json'),JSON.stringify(result,null,2));await browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
