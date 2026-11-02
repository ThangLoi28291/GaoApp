'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
module.exports=async({page,info,context,out,errors,idle,close,state,login,browser})=>{
 const checks=[],writes=[],csp=[];let completed=false;
 const endpoint=`/admin/api/stock-documents/${info.receiptId}/intake`,receiptUrl=id=>info.baseUrl+`/admin/warehouse-receiving/${id}`;
 const row=id=>page.locator(`tr[data-line-id="${id}"]`),pack=()=>row(info.ui05e.packLineId),other=()=>row(info.ui05e.otherLineId);
 const settle=()=>page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
 const shot=name=>page.screenshot({path:path.join(out,name+'.png'),animations:'disabled'});
 const size=async(w,h)=>{await page.setViewportSize({width:w,height:h});await page.waitForFunction(w=>!!document.querySelector('[data-receiving-phone]')===(w<768),w);await settle();};
 const overflow=async()=>assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true,'No horizontal page overflow');
 const business=body=>Object.fromEntries(Object.entries(body).filter(([key])=>!['documentRowVersion','itemRowVersion','leaseToken'].includes(key)));
 const captureState=()=>page.locator('#wrdLastSaved').evaluate(el=>({hidden:el.hidden,name:el.querySelector('#wrdScanName').textContent,added:el.querySelector('#wrdScanAdded').textContent,time:el.querySelector('time').getAttribute('datetime')}));
 const step=async(target,delta)=>{await target().locator(`[data-wra-step="${delta}"]`).click();await idle();};
 const closeSheet=async()=>{await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();};
 page.on('console',m=>{if(/Content Security Policy/i.test(m.text()))csp.push(m.text());});
 page.on('request',r=>{if(['POST','PUT','DELETE'].includes(r.method())&&/\/admin\/(?:warehouse-receiving|api\/stock-documents)/.test(new URL(r.url()).pathname))writes.push({url:new URL(r.url()).pathname,method:r.method(),body:r.postDataJSON()});});
 try{
  await size(1440,900);await page.evaluate(()=>scrollTo(0,0));
  const selectors=['.wrd-topbar','.wrd-layout','#receiptDocumentActions'],canonical=html=>html.replace(/(<(?:input|textarea)[^>]*?) style=""/g,'$1');
  const desktopNodes=(await page.evaluate(selectors=>{window.acceptanceNodes=selectors.flatMap(s=>[...document.querySelectorAll(s+', '+s+' *')]);return selectors.map(s=>document.querySelector(s).outerHTML);},selectors)).map(canonical);
  const desktop=await page.screenshot({animations:'disabled'});fs.writeFileSync(path.join(out,'desktop-current.png'),desktop);
  const baselineContext=await browser.newContext({viewport:{width:1440,height:900},isMobile:true,hasTouch:true,storageState:await context.storageState()});
  try{
   const baseline=await baselineContext.newPage();
   for(const name of ['warehouse-receiving-quantity','warehouse-receiving-mobile','warehouse-receiving-app'])await baseline.route('**/'+name+'.js*',route=>route.fulfill({contentType:'application/javascript',body:fs.readFileSync(path.join('.artifacts/ui-05e/before/GaoApp.Web/wwwroot/Admin/js',name+'.js'),'utf8')}));
   await baseline.route('**/warehouse-receiving-detail.mobile-v2.css*',route=>route.fulfill({contentType:'text/css',body:fs.readFileSync('.artifacts/ui-05e/before/GaoApp.Web/wwwroot/Admin/css/warehouse-receiving-detail.mobile-v2.css','utf8')}));
   await baseline.goto(receiptUrl(info.receiptId));await baseline.waitForFunction(()=>window.ReceiptIntake&&document.querySelector('#riBaseUnit').options.length>1);
   await baseline.locator('.select2-container--open').first().waitFor();await baseline.evaluate(()=>{window.jQuery('#quickLookupInput').select2('close');document.activeElement?.blur();});
   const before=await baseline.screenshot({animations:'disabled'});fs.writeFileSync(path.join(out,'desktop-before.png'),before);assert.ok(desktop.equals(before),'Desktop pixels remain identical');
  }finally{await baselineContext.close();}
  checks.push('desktop-pixels-equal-to-UI05d');
  for(const [w,h] of [[1600,1000],[1440,900],[1366,768],[1024,768],[768,1024],[767,900],[430,932],[390,844],[360,800],[320,740]]){
   await size(w,h);await overflow();
   if(w<768){assert.equal(await page.locator('[data-wra-tab]').count(),2);assert.equal(await page.locator('#wraSummary').isVisible(),false);for(const box of await page.locator('.wra-tabs button,.wra-dock-actions button').evaluateAll(nodes=>nodes.map(x=>x.getBoundingClientRect())))assert.ok(box.width>=44&&box.height>=44);}
   else assert.equal(await page.locator('.wra-dock').isVisible(),false);
   checks.push(`viewport-${w}x${h}`);
  }
  await size(1440,900);
  assert.deepEqual((await page.evaluate(selectors=>selectors.map(s=>document.querySelector(s).outerHTML),selectors)).map(canonical),desktopNodes);
  assert.equal(await page.evaluate(()=>acceptanceNodes.every(x=>x.isConnected)),true);checks.push('desktop-original-DOM-nodes-and-fields-restored-after-phone-viewports');
  assert.equal(writes.length,0);
  await page.locator('#quickLookupInput + .select2').click();const desktopSearch=page.locator('.select2-container--open .select2-search__field');await desktopSearch.fill('INTERNAL-BOX');await desktopSearch.press('Enter');await page.locator('#quickAddProductModal.show').waitFor();
  await page.locator('#popupQuickQty').fill('1.125');await page.locator('[data-popup-step="-1"]').click();assert.equal(await page.locator('#popupQuickQty').inputValue(),'1');
  await page.locator('#popupQuickQty').fill('.12345');await page.locator('[data-popup-step="1"]').click();assert.equal(await page.locator('#popupQuickQty').inputValue(),'1.12345');await close('#quickAddProductModal');assert.equal(writes.length,0);checks.push('desktop-quick-popup-keeps-original-minimum-and-arithmetic-without-saving');
  await size(390,844);
  assert.match(await pack().locator('.wrc-quantity-unit').innerText(),/Lốc/);assert.match(await other().locator('.wrc-quantity-unit').innerText(),/Lốc/);
  assert.match(await pack().locator('.wrd-line-meta').innerText(),/4/);assert.match(await other().locator('.wrd-line-meta').innerText(),/6/);
  await step(pack,-1);assert.equal(Number(await pack().getAttribute('data-receiving-quantity')),.125,'1.125 minus one must preserve .125');
  assert.equal(Number(await other().getAttribute('data-receiving-quantity')),1.125,'Same unit name must not select another conversion');
  await step(pack,1);await step(pack,-1);assert.equal(Number(await pack().getAttribute('data-receiving-quantity')),.125);
  checks.push('known-card-step-crosses-one-with-exact-fraction-and-separate-same-name-conversion');
  await other().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('1,125');await page.locator('[data-wra-quantity-step="-1"]').click();assert.equal(await page.locator('#wraQuantity').inputValue(),'0.125');
  await page.locator('[data-wra-quantity-step="1"]').click();await page.locator('[data-wra-quantity-step="-1"]').click();await closeSheet();
  assert.equal(Number(await other().getAttribute('data-receiving-quantity')),.125);assert.equal(Number(await pack().getAttribute('data-receiving-quantity')),.125);
  await step(other,1);await step(other,-1);checks.push('fractional-sheet-comma-and-card-steps-keep-Loc-times4-and-times6-separate');
  await pack().locator('[data-wra-edit]').click();const priorWrites=writes.length;
  for(const invalid of ['0','-1','0,0005']){await page.locator('#wraQuantity').fill(invalid);await page.locator('#wraSaveQuantity').click();assert.equal(await page.locator('#wraQuantityModal.show').count(),1);assert.ok((await page.locator('#wraQuantityError').innerText()).length>0);}
  assert.equal(writes.length,priorWrites,'Invalid quantities never send a write');await close('#wraQuantityModal');checks.push('zero-negative-and-more-than-three-decimals-rejected-without-write');
  await shot('fractional-same-unit-390');
  // Real CODE128 decoder, synthetic video frames. A result opens review only;
  // one deliberate save is one new command, even when frames repeat.
  const knownCommands=[];let release,arrived;const gate=new Promise(resolve=>release=resolve),arrival=new Promise(resolve=>arrived=resolve);
  await page.route('**'+endpoint+'/known',async route=>{knownCommands.push(business(route.request().postDataJSON()));if(knownCommands.length===1){await route.fetch();arrived();await gate;await route.abort('failed');}else await route.continue();});
  const beforeKnown=writes.length,priorConfirmation=await captureState();await page.locator('#wrdOpenCamera').click();await page.locator('#quickAddProductModal.show').waitFor();
  await page.waitForFunction(()=>testStreams.length>0&&testStreams.every(s=>s.getTracks().every(t=>t.readyState==='ended')));
  assert.equal(writes.length,beforeKnown,'Camera decode never adds automatically');await page.locator('#popupQuickQty').fill('1.125');await page.locator('[data-popup-step="-1"]').click();assert.equal(await page.locator('#popupQuickQty').inputValue(),'0.125');
  await page.locator('#btnPopupQuickAddLine').click();await arrival;await page.evaluate(()=>document.querySelector('#btnPopupQuickAddLine').click());assert.equal(knownCommands.length,1,'A second tap during a pending request is ignored');
  release();await page.waitForFunction(()=>document.querySelector('#wrdSaveStatus').dataset.state==='error');assert.deepEqual(await captureState(),priorConfirmation);assert.equal(await page.locator('#popupQuickQty').isDisabled(),true);
  await close('#quickAddProductModal');await page.locator('#wraRetry').click();await page.locator('#quickAddProductModal.show').waitFor();await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();
  assert.deepEqual(knownCommands[0],knownCommands[1]);checks.push('camera-repeated-frames-and-double-tap-do-not-add-lost-response-retry-reuses-one-command');
  await page.locator('#wrdOpenCamera').click();await page.locator('#quickAddProductModal.show').waitFor();await page.locator('#popupQuickQty').fill('.125');await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();
  assert.notEqual(knownCommands[1].commandId,knownCommands[2].commandId,'A deliberate repeated scan is a new receipt command');
  assert.equal((await state()).items.filter(x=>x.rawBarcode==='INTERNAL-BOX').length,2);assert.match(await page.locator('#wrdScanTotal').innerText(),/0,25 Hộp/);checks.push('same-barcode-deliberate-rescan-adds-one-new-command-and-exact-fractional-total');
  await page.emulateMedia({reducedMotion:'reduce'});await page.locator('#quickLookupInput + .select2').click();const search=page.locator('.select2-container--open .select2-search__field');await search.fill('INTERNAL-BOX');await search.press('Enter');await page.locator('#quickAddProductModal.show').waitFor();
  await page.locator('#popupQuickQty').fill('1.001');await page.locator('[data-popup-step="-1"]').click();assert.equal(await page.locator('#popupQuickQty').inputValue(),'0.001');await page.locator('[data-popup-step="-1"]').click();assert.equal(await page.locator('#popupQuickQty').inputValue(),'0.001');
  await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();assert.match(await page.locator('#wrdScanAdded').innerText(),/0,001 Hộp/);assert.match(await page.locator('#wrdScanTotal').innerText(),/0,251 Hộp/);
  assert.equal(knownCommands.length,4);assert.equal(new Set(knownCommands.map(x=>x.commandId)).size,3);await page.unroute('**'+endpoint+'/known');await page.emulateMedia({reducedMotion:'no-preference'});checks.push('minimum-0001-clamp-and-three-decimal-total-survive-reduced-motion-receiving');
  await page.locator('#wraMore').click();await page.locator('#wraNew').click();await page.locator('#receiptIntakeModal.show').waitFor();
  await page.locator('#riNewName').fill('Hàng mới kiểm tra số lẻ UI05e');await page.locator('#riBarcode').fill('UI05E-UNKNOWN');
  await page.evaluate(()=>{const unit=[...document.querySelector('#riBaseUnit').options].find(x=>x.value&&!x.value.startsWith('new:'));window.jQuery('#riBaseUnit').val(unit.value).trigger('change');const receive=document.querySelector('#riReceiveUnit');receive.add(new Option('Thùng','new:Thùng'));window.jQuery(receive).val('new:Thùng').trigger('change');});
  await page.locator('#riFactor').fill('12');await page.locator('#riQuantity').fill('1.125');
  await size(390,440);await page.locator('#riQuantity').focus();await overflow();await page.locator('#riSave').scrollIntoViewIfNeeded();assert.equal(await page.locator('#riSave').isVisible(),true);await shot('keyboard-height-390x440');
  await size(320,568);await overflow();assert.equal(await page.locator('#riQuantity').inputValue(),'1.125');await size(390,844);checks.push('reduced-height-and-320px-input-layout-preserves-packing-and-fractional-values');
  const unknownCommands=[];const capture=r=>{if(r.method()==='POST'&&new URL(r.url()).pathname===endpoint)unknownCommands.push(business(r.postDataJSON()));};page.on('request',capture);
  const beforeOffline=await captureState();await context.setOffline(true);await page.locator('#riSave').click();await page.waitForFunction(()=>document.querySelector('#wrdSaveStatus').dataset.state==='error');assert.equal(await page.locator('#riQuantity').isDisabled(),true);assert.deepEqual(await captureState(),beforeOffline);
  const offlineCount=unknownCommands.length;await context.setOffline(false);await settle();assert.equal(unknownCommands.length,offlineCount,'Reconnection does not automatically send an intake');
  await close('#receiptIntakeModal');await page.locator('#wraRetry').click();await page.locator('#receiptIntakeModal.show').waitFor();assert.equal(await page.locator('#riQuantity').inputValue(),'1.125');await page.locator('#riSave').click();await page.locator('#receiptIntakeModal').waitFor({state:'hidden'});await idle();
  assert.equal(unknownCommands.length,2);assert.deepEqual(unknownCommands[0],unknownCommands[1]);page.off('request',capture);checks.push('offline-before-write-keeps-draft-no-auto-send-and-explicit-retry-uses-same-command');
  const unknown=(await state()).items.find(x=>x.rawBarcode==='UI05E-UNKNOWN'),unknownRow=()=>page.locator(`tr[data-intake-item-id="${unknown.id}"]`);
  await unknownRow().locator('[data-wra-edit]').click();await page.locator('[data-wra-quantity-step="-1"]').click();assert.equal(await page.locator('#wraQuantity').inputValue(),'0.125');await closeSheet();
  await step(unknownRow,1);await step(unknownRow,-1);assert.equal((await state()).items.find(x=>x.id===unknown.id).quantity,.125);checks.push('provisional-sheet-and-card-steps-support-below-one-with-original-factor');
  assert.equal(await page.evaluate(()=>testAudioCount),0);const beforeReload=(await state()).items.map(x=>[x.id,x.quantity,x.rawBarcode]);await page.reload();await page.waitForFunction(()=>window.ReceiptIntake&&document.querySelector('#riBaseUnit').options.length>1);await idle();
  assert.deepEqual((await state()).items.map(x=>[x.id,x.quantity,x.rawBarcode]),beforeReload);assert.equal(await page.locator('[data-wra-tab]').count(),2);assert.equal(await page.locator('#wrdScanHistory,[data-wra-tab=history]').count(),0);await shot('confirmed-fractions-390');checks.push('reload-preserves-server-receipts-quantities-and-only-two-tabs');
  // Cancellation and late permission responses must release every owned track.
  await require('./browser-contract.cjs').addNoncedScript(page,{url:info.baseUrl+'/lib/jsbarcode/JsBarcode.all.min.js'});await page.evaluate(()=>{testMediaCode='';testMediaDeferred=true;});
  await page.locator('#wrdOpenCamera').click();await page.waitForFunction(()=>!!window.resolveTestMedia);await close('#wrdCaptureModal');await page.evaluate(()=>{resolveTestMedia();testMediaDeferred=false;});await page.waitForFunction(()=>testStreams.every(s=>s.getTracks().every(t=>t.readyState==='ended')));
  await page.evaluate(()=>testMediaError='NotAllowedError');await page.locator('#wrdOpenCamera').click();await page.waitForFunction(()=>document.querySelector('#wrdCaptureMessage').textContent.includes('Chưa được cấp quyền'));await close('#wrdCaptureModal');await page.evaluate(()=>testMediaError=null);checks.push('camera-denial-and-close-before-permission-release-all-tracks');
  await page.locator('[data-wra-tab=info]').click();await page.locator('[data-wra-tab=info]').press('ArrowRight');assert.equal(await page.locator('[data-wra-tab=items]').getAttribute('aria-selected'),'true');
  await page.emulateMedia({forcedColors:'active'});await overflow();await shot('forced-colors-390');await page.emulateMedia({forcedColors:'none'});await page.evaluate(()=>document.documentElement.style.fontSize='24px');await overflow();await page.evaluate(()=>document.documentElement.style.fontSize='');checks.push('two-tab-keyboard-forced-colors-and-150-percent-text');
  const readOnly=await context.newPage();readOnly.on('pageerror',e=>errors.push(e.message));await readOnly.goto(receiptUrl(info.ui05e.readOnlyId));await readOnly.waitForFunction(()=>window.ReceiptIntake&&document.querySelector('#CanEditReceiving').value==='false');
  assert.equal(await readOnly.locator('#wraDockActions,#wrdOpenCamera,[data-wra-edit],[data-wra-step]').count(),0);assert.equal(await readOnly.locator('.wrd-scan-panel').isVisible(),false,'No empty scan card on a read-only receipt without a recent capture');await readOnly.keyboard.press('F4');assert.equal(await readOnly.locator('.modal.show').count(),0);assert.match(await readOnly.locator('.wrb-readonly-status').innerText(),/Chờ duyệt/);await readOnly.screenshot({path:path.join(out,'readonly-390.png'),animations:'disabled'});
  const readOnlyState=await readOnly.evaluate(async id=>(await(await fetch(`/admin/api/stock-documents/${id}/intake`)).json()).state,info.ui05e.readOnlyId);
  const token=await readOnly.locator('#receiptIntake [name=__RequestVerificationToken]').first().inputValue();
  const blocked=await context.request.post(info.baseUrl+`/admin/api/stock-documents/${info.ui05e.readOnlyId}/intake/known`,{headers:{RequestVerificationToken:token},data:{productUnitConversionId:info.packId,factor:4,quantity:1,commandId:crypto.randomUUID(),documentRowVersion:readOnlyState.documentRowVersion}});assert.equal(blocked.status(),409,await blocked.text());await readOnly.close();checks.push('readonly-phone-has-no-edit-or-capture-entry-F4-blocked-and-server-rejects-valid-CSRF-write');
  const foreign=await context.request.get(info.baseUrl+`/admin/api/stock-documents/${info.ui05e.foreignId}/intake`);assert.equal(foreign.status(),409);const foreignBody=await foreign.json();assert.match(foreignBody.message,/Không tìm thấy phiếu nhập của cửa hàng/);assert.equal(foreignBody.state,undefined);
  const deniedContext=await browser.newContext({viewport:{width:390,height:844}});try{const denied=await deniedContext.newPage();await login(denied,info.ui05e.denied);const r=await deniedContext.request.get(receiptUrl(info.receiptId),{maxRedirects:0,headers:{Accept:'application/json'}});assert.equal(r.status(),403);}finally{await deniedContext.close();}
  checks.push('original-store-isolation-and-create-permission-gates-still-enforced');
  assert.equal(await page.evaluate(()=>testAudioCount),0);assert.deepEqual(errors,[]);assert.deepEqual(csp,[]);checks.push('no-audio-JavaScript-or-CSP-errors');completed=true;
 }finally{await context.setOffline(false).catch(()=>{});if(!completed)await shot('failure').catch(()=>{});fs.writeFileSync(path.join(out,'result.json'),JSON.stringify({completed,checks,writes,errors,csp,media:'Synthetic camera frames and microphone; real decoder, Razor, HTTP and disposable SQL. Reduced viewport height does not certify physical IME.'},null,2));}
 console.log('UI-05E BROWSER PASS: '+checks.length+' groups; fractional quantity, repeat scan, permissions and layout acceptance.');
};
