'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
module.exports=async({page,info,context,out,errors,idle,close,state,browser})=>{
 const checks=[],writes=[],events=[];let completed=false;
 const endpoint=`/admin/api/stock-documents/${info.receiptId}/intake`,lines=`/admin/warehouse-receiving/${info.receiptId}/lines`;
 const snapshot=()=>page.locator('#wrdLastSaved').evaluate(el=>({hidden:el.hidden,fields:[...el.querySelectorAll('strong,small,time,[id^=wrdScan]')].map(x=>[x.id,x.textContent,x.getAttribute('datetime')])}));
 // Replay keeps the command and business payload; concurrency tokens can
 // advance after an acknowledged write, as the existing service permits.
 const stable=body=>Object.fromEntries(Object.entries(body).filter(([key])=>!['documentRowVersion','itemRowVersion','leaseToken'].includes(key)));
 const shot=name=>page.screenshot({path:path.join(out,name+'.png'),animations:'disabled'});
 const failed=()=>page.waitForFunction(()=>document.querySelector('#wrdSaveStatus').dataset.state==='error');
 const cameraClosed=async()=>assert.equal(await page.locator('#wrdCaptureModal.show').count(),0);
 const openKnown=async quantity=>{
  await page.locator('#quickLookupInput + .select2').click();const input=page.locator('.select2-container--open .select2-search__field');
  await input.fill('INTERNAL-BOX');await input.press('Enter');await page.locator('#quickAddProductModal.show').waitFor();await page.locator('#popupQuickQty').fill(String(quantity));
 };
 const retryKnown=async(scan=false)=>{await close('#quickAddProductModal');await page.locator('#wraRetry').click();await page.locator('#quickAddProductModal.show').waitFor();await page.locator(scan?'#wrdAddAndScan':'#btnPopupQuickAddLine').click();};
 page.on('request',r=>{if(['POST','PUT'].includes(r.method())&&r.url().includes(`/admin/api/stock-documents/${info.receiptId}`))writes.push({url:new URL(r.url()).pathname,method:r.method(),body:r.postDataJSON()});});
 try{
  // Compare the real desktop page with the three prior owner scripts, using
  // a second disposable context and the same authenticated fixture account.
  await page.setViewportSize({width:1440,height:900});await page.waitForFunction(()=>!document.body.classList.contains('wrd-app-mode'));
  await page.waitForFunction(()=>!document.querySelector('[data-receiving-phone]'));
  await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  const desktop=await page.screenshot({animations:'disabled'});fs.writeFileSync(path.join(out,'desktop-current.png'),desktop);
  const baselineContext=await browser.newContext({viewport:{width:1440,height:900},isMobile:true,hasTouch:true,storageState:await context.storageState()});
  try{
   const baseline=await baselineContext.newPage();
   for(const name of ['warehouse-receiving-detail','receipt-intake','warehouse-receiving-feedback','warehouse-receiving-app'])await baseline.route('**/'+name+'.js*',route=>route.fulfill({contentType:'application/javascript',body:fs.readFileSync(path.join('.artifacts/ui-05d/before/GaoApp.Web/wwwroot/Admin/js',name+'.js'),'utf8')}));
   await baseline.goto(page.url());await baseline.waitForFunction(()=>window.ReceiptIntake&&document.querySelector('#riBaseUnit').options.length>1);
   await baseline.locator('.select2-container--open').first().waitFor();
   await baseline.evaluate(()=>{window.jQuery('#quickLookupInput').select2('close');document.activeElement?.blur();});
   const original=await baseline.screenshot({animations:'disabled'});fs.writeFileSync(path.join(out,'desktop-before.png'),original);assert.ok(desktop.equals(original),'Desktop pixels equal to UI-05c');
  }finally{await baselineContext.close();}
  checks.push('desktop-pixels-equal-to-before-command-fixes');
  await page.setViewportSize({width:390,height:844});await page.waitForFunction(()=>document.body.classList.contains('wrd-app-mode'));
  await page.evaluate(()=>{window.ackEvents=[];document.addEventListener('receiving:save',e=>ackEvents.push({state:e.detail.state,commandId:e.detail.feedback?.commandId}));});
  let reads=0,posts=0;const before=await snapshot(),commands=[];
  await page.route('**'+endpoint,async route=>{
   if(route.request().method()==='GET'){
    reads++;if(reads===1){await route.fulfill({status:403,json:{message:'Owned fixture: read acknowledgement denied'}});return;}
    if(reads===2){await route.fulfill({status:200,contentType:'text/html',body:'<html>Owned fixture: incomplete response</html>'});return;}
   }
   await route.continue();
  });
  await page.route('**'+endpoint+'/known',async route=>{
   commands.push(stable(route.request().postDataJSON()));if(++posts===2)await route.fulfill({status:403,json:{message:'Owned fixture: retry denied'}});else await route.continue();
  });
  await openKnown(1.25);await page.locator('#wrdAddAndScan').click();await failed();await cameraClosed();
  assert.equal(await page.locator('#quickAddProductModal.show').count(),1);assert.equal(await page.locator('#popupQuickQty').isDisabled(),true);assert.deepEqual(await snapshot(),before);
  await shot('known-read-failure');checks.push('known-POST-success-read-403-keeps-dialog-fields-and-previous-confirmation');
  await retryKnown();await failed();assert.equal(await page.locator('#popupQuickQty').isDisabled(),true);assert.deepEqual(await snapshot(),before);await cameraClosed();
  checks.push('later-403-does-not-unlock-an-unacknowledged-known-command');
  // Direct invocation is an existing integration entry point. It must not
  // replace the pending command with another product selection.
  await page.evaluate(()=>window.openQtyPopup({productVariantId:999999,unitId:999999,productName:'Không được thay thế',quantity:99}));
  assert.equal(await page.locator('#popupQuickQty').inputValue(),'1.25');assert.doesNotMatch(await page.locator('#popupProductName').innerText(),/Không được thay thế/);
  await page.evaluate(()=>window.openGroupedProductPopup({productName:'Không được thay thế',units:[{productVariantId:999999,unitId:999999}]}));
  assert.equal(await page.locator('#popupQuickQty').inputValue(),'1.25');checks.push('pending-known-command-survives-existing-selection-entry-points');
  await retryKnown(true);await failed();assert.equal(await page.locator('#popupQuickQty').isDisabled(),true);assert.equal(await page.locator('#btnPopupQuickAddLine').isEnabled(),true);assert.deepEqual(await snapshot(),before);await cameraClosed();
  checks.push('malformed-200-confirmation-preserves-last-valid-permissions-state-and-allows-safe-retry');
  await page.evaluate(()=>testMediaCode='');await retryKnown(true);await page.locator('#wrdCaptureModal.show').waitFor();await page.waitForFunction(()=>testStreams.length>0);await close('#wrdCaptureModal');await idle();
  assert.equal(commands.length,4);commands.forEach(command=>assert.deepEqual(command,commands[0]));assert.match(await page.locator('#wrdScanAdded').innerText(),/1,25/);
  assert.equal((await state()).items.filter(x=>x.rawBarcode==='INTERNAL-BOX').length,1);await page.unroute('**'+endpoint);await page.unroute('**'+endpoint+'/known');
  checks.push('same-command-payload-four-replays-one-physical-intake-and-camera-only-after-confirmation');
  // A valid rowversion alone is insufficient: the visible row quantity must
  // match the acknowledged server journal for this exact command.
  const oldLines=await page.evaluate(async url=>(await fetch(url)).text(),lines),prior=await snapshot();let stale=true;
  let releaseList,arriveList;const listGate=new Promise(resolve=>releaseList=resolve),listArrived=new Promise(resolve=>arriveList=resolve);
  await page.route('**'+lines,async route=>{if(stale){arriveList();await listGate;await route.fulfill({status:200,contentType:'text/html',body:oldLines});}else await route.continue();});
  const start=writes.length;await openKnown(.75);await page.locator('#btnPopupQuickAddLine').click();await listArrived;
  await page.locator('#quickAddProductModal [data-bs-dismiss=modal]').first().click();assert.equal(await page.locator('#quickAddProductModal.show').count(),1);await cameraClosed();assert.deepEqual(await snapshot(),prior);
  checks.push('inflight-list-confirmation-blocks-dialog-close-and-scan');releaseList();await failed();await cameraClosed();
  assert.equal(await page.locator('#quickAddProductModal.show').count(),1);assert.deepEqual(await snapshot(),prior);assert.equal(await page.locator('#popupQuickQty').isDisabled(),true);
  await shot('known-snapshot-mismatch');stale=false;await retryKnown();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();
  assert.deepEqual(stable(writes[start].body),stable(writes[start+1].body));await page.unroute('**'+lines);checks.push('stale-valid-list-snapshot-cannot-close-or-confirm-until-exact-row-matches');
  // Lose a response after the real SQL write; allow the same command through
  // on retry. The existing feedback and quantities must remain stable.
  let lost=true;const knownLost=[];await page.route('**'+endpoint+'/known',async route=>{knownLost.push(stable(route.request().postDataJSON()));if(lost){lost=false;await route.fetch();await route.abort('failed');}else await route.continue();});
  const old=await snapshot();await openKnown(.5);await page.locator('#btnPopupQuickAddLine').click();await failed();assert.deepEqual(await snapshot(),old);
  await retryKnown();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();assert.deepEqual(knownLost[0],knownLost[1]);await page.unroute('**'+endpoint+'/known');
  checks.push('known-network-loss-after-real-write-replays-identical-command-once');
  let reject=true;const rejected=[];await page.route('**'+endpoint+'/known',async route=>{rejected.push(stable(route.request().postDataJSON()));if(reject){reject=false;await route.fulfill({status:400,json:{message:'Owned fixture: invalid quantity'}});}else await route.continue();});
  await openKnown(.1);await page.locator('#btnPopupQuickAddLine').click();await failed();assert.equal(await page.locator('#popupQuickQty').isEnabled(),true);
  await page.locator('#popupQuickQty').fill('.25');await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();
  assert.notEqual(rejected[0].commandId,rejected[1].commandId);await page.unroute('**'+endpoint+'/known');checks.push('definite-initial-400-allows-correction-with-new-command');
  const lineId=(await state()).items.find(x=>x.rawBarcode==='INTERNAL-BOX').resolvedStockDocumentLineId;
  const knownRow=()=>page.locator(`tr[data-line-id="${lineId}"]`),quantityUrl=`**/api/stock-documents/${info.receiptId}/lines/${lineId}`;
  let quantityWrites=0;await page.route(quantityUrl,async route=>{if(route.request().method()!=='PUT'){await route.continue();return;}quantityWrites++;await route.fetch();await route.abort('failed');});
  await knownRow().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('3.25');await page.locator('#wraSaveQuantity').click();
  await page.waitForFunction(()=>document.querySelector('#wraQuantityError').textContent.includes('Chưa xác nhận'));assert.equal(await page.locator('#wraQuantity').isDisabled(),true);
  await close('#wraQuantityModal');await page.locator('#wraRetry').click();await page.locator('#wraQuantityModal.show').waitFor();await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();
  assert.equal(quantityWrites,1,'Lost quantity response is confirmed by a matching server snapshot without another PUT');assert.equal(Number(await knownRow().getAttribute('data-receiving-quantity')),3.25);await page.unroute(quantityUrl);
  checks.push('known-quantity-lost-after-write-freezes-target-and-confirms-by-server-read-without-second-PUT');
  const quantityBodies=[];await page.route(quantityUrl,async route=>{if(route.request().method()!=='PUT'){await route.continue();return;}quantityBodies.push(route.request().postDataJSON());if(quantityBodies.length===1)await route.abort('failed');else await route.continue();});
  await knownRow().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('2.75');await page.locator('#wraSaveQuantity').click();await page.waitForFunction(()=>document.querySelector('#wraQuantityError').textContent.includes('Chưa xác nhận'));
  await close('#wraQuantityModal');await page.locator('#wraRetry').click();await page.locator('#wraQuantityModal.show').waitFor();await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();
  assert.equal(quantityBodies.length,2);assert.deepEqual(quantityBodies[0],quantityBodies[1],'Before-write loss retains original document/line concurrency tokens and quantity');assert.equal(Number(await knownRow().getAttribute('data-receiving-quantity')),2.75);await page.unroute(quantityUrl);
  checks.push('known-quantity-before-write-loss-retries-exact-payload-only-when-original-versions-still-match');
  let conflictWrites=0;await page.route(quantityUrl,async route=>{if(route.request().method()==='PUT'){conflictWrites++;await route.abort('failed');}else await route.continue();});
  await knownRow().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('3');await page.locator('#wraSaveQuantity').click();await page.waitForFunction(()=>document.querySelector('#wraQuantityError').textContent.includes('Chưa xác nhận'));
  const currentHtml=await page.evaluate(async url=>(await fetch(url)).text(),lines);
  const conflictingHtml=await page.evaluate(({html,lineId})=>{const t=document.createElement('template');t.innerHTML=html;t.content.querySelector('[data-receiving-row-version]').dataset.receivingRowVersion='AAAAAAAAAAE=';t.content.querySelector(`tr[data-line-id="${lineId}"]`).dataset.receivingQuantity='4.5';return t.innerHTML;},{html:currentHtml,lineId});
  await page.route('**'+lines,route=>route.fulfill({status:200,contentType:'text/html',body:conflictingHtml}));
  await page.locator('#wraSaveQuantity').click();await page.waitForFunction(()=>document.querySelector('#wraQuantityError').textContent.includes('Chưa xác nhận'));assert.equal(conflictWrites,1,'A conflicting version must not trigger another PUT');assert.equal(await page.locator('#wraQuantity').isDisabled(),true);
  await page.unroute('**'+lines);await page.unroute(quantityUrl);await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();
  await knownRow().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('2.75');await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();
  checks.push('conflicting-quantity-version-keeps-target-locked-and-never-overwrites-unseen-edits');
  await page.locator('#wraMore').click();await page.locator('#wraNew').click();await page.locator('#receiptIntakeModal.show').waitFor();
  await page.locator('#riNewName').fill('Hàng kiểm tra xác nhận UI05d');await page.locator('#riBarcode').fill('UI05D-UNKNOWN');
  await page.evaluate(()=>{const base=[...document.querySelector('#riBaseUnit').options].find(x=>x.value&&!x.value.startsWith('new:'));window.jQuery('#riBaseUnit').val(base.value).trigger('change');const receive=document.querySelector('#riReceiveUnit');receive.add(new Option('Thùng','new:Thùng'));window.jQuery(receive).val('new:Thùng').trigger('change');});
  await page.locator('#riFactor').fill('12');await page.locator('#riQuantity').fill('2.5');await page.locator('.wrc-evidence summary').click();await page.locator('#riNote').fill('Ghi chú giữ nguyên khi thử lại');
  const photo=await page.evaluate(()=>{const canvas=document.createElement('canvas');canvas.width=500;canvas.height=300;const c=canvas.getContext('2d');c.fillStyle='#deeafa';c.fillRect(0,0,500,300);c.fillStyle='#123457';c.font='32px sans-serif';c.fillText('UI-05d · THÙNG 12 HỘP',20,130);return canvas.toDataURL('image/png').split(',')[1];});
  await page.locator('#riPhoto').setInputFiles({name:'ui05d-packaging.png',mimeType:'image/png',buffer:Buffer.from(photo,'base64')});await page.waitForFunction(()=>ReceiptIntakePhoto.current()&&!ReceiptIntakePhoto.blocked());
  const preserved=await page.evaluate(()=>({name:riNewName.value,barcode:riBarcode.value,unit:riReceiveUnit.value,factor:riFactor.value,quantity:riQuantity.value,note:riNote.value,photo:ReceiptIntakePhoto.current()}));
  let unknownPosts=0,unknownReads=0;const unknownCommands=[],previous=await snapshot();
  await page.route('**'+endpoint,async route=>{
   if(route.request().method()==='GET'){if(++unknownReads===1)await route.fulfill({status:403,json:{message:'Owned fixture: unknown read denied'}});else await route.continue();return;}
   unknownCommands.push(stable(route.request().postDataJSON()));if(++unknownPosts===1){await route.fetch();await route.abort('failed');}else await route.continue();
  });
  await page.locator('#riSave').click();await failed();assert.equal(await page.locator('#receiptIntakeModal.show').count(),1);assert.equal(await page.locator('#riQuantity').isDisabled(),true);assert.deepEqual(await snapshot(),previous);
  await close('#receiptIntakeModal');await page.locator('#wraRetry').click();await page.locator('#receiptIntakeModal.show').waitFor();await page.locator('#riSave').click();await failed();
  assert.equal(await page.locator('#receiptIntakeModal.show').count(),1);assert.equal(await page.locator('#riNewName').isDisabled(),true);assert.equal(await page.locator('#riFactor').isDisabled(),true);assert.deepEqual(await snapshot(),previous);await cameraClosed();
  const after=await page.evaluate(()=>({name:riNewName.value,barcode:riBarcode.value,unit:riReceiveUnit.value,factor:riFactor.value,quantity:riQuantity.value,note:riNote.value,photo:ReceiptIntakePhoto.current()}));assert.deepEqual(after,preserved);
  await shot('unknown-awaiting-confirmation');checks.push('unknown-lost-POST-and-read-403-preserve-name-unit-fraction-factor-note-photo-and-feedback');
  await close('#receiptIntakeModal');await page.locator('#wraRetry').click();await page.locator('#receiptIntakeModal.show').waitFor();await page.locator('#riSave').click();await page.locator('#receiptIntakeModal').waitFor({state:'hidden'});await idle();
  assert.equal(unknownCommands.length,3);unknownCommands.forEach(command=>assert.deepEqual(command,unknownCommands[0]));await page.unroute('**'+endpoint);
  const final=await state();assert.equal(final.items.filter(x=>x.rawBarcode==='UI05D-UNKNOWN').length,1);assert.equal(final.items.find(x=>x.rawBarcode==='UI05D-UNKNOWN').quantity,2.5);
  assert.equal(final.items.filter(x=>x.rawBarcode==='INTERNAL-BOX').reduce((n,x)=>n+x.quantity,0),2.75);assert.equal(await page.locator('[data-wra-tab]').count(),2);
  await shot('confirmed-mobile');checks.push('unknown-three-identical-command-replays-one-intake-two-tabs-and-exact-server-totals');
  const unknownId=final.items.find(x=>x.rawBarcode==='UI05D-UNKNOWN').id,unknownRow=()=>page.locator(`tr[data-intake-item-id="${unknownId}"]`),unknownQuantityCommands=[];let denyRead=true;
  await page.route('**'+endpoint,async route=>{if(route.request().method()==='GET'&&denyRead){denyRead=false;await route.fulfill({status:403,json:{message:'Owned fixture: quantity confirmation read denied'}});}else await route.continue();});
  await page.route('**'+endpoint+'/'+unknownId+'/quantity',async route=>{unknownQuantityCommands.push(route.request().postDataJSON());await route.continue();});
  await unknownRow().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('3.5');await page.locator('#wraSaveQuantity').click();await page.waitForFunction(()=>document.querySelector('#wraQuantityError').textContent.includes('Chưa xác nhận'));
  assert.equal(await page.locator('#wraQuantity').isDisabled(),true);await close('#wraQuantityModal');await page.locator('#wraRetry').click();await page.locator('#wraQuantityModal.show').waitFor();assert.equal(await page.locator('#wraQuantity').inputValue(),'3.5');
  await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();assert.deepEqual(stable(unknownQuantityCommands[0]),stable(unknownQuantityCommands[1]));await page.unroute('**'+endpoint);await page.unroute('**'+endpoint+'/'+unknownId+'/quantity');
  await unknownRow().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('2.5');await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();
  checks.push('provisional-quantity-read-403-retains-command-on-replaced-row-and-retry-opens-the-correct-sheet');
  events.push(...await page.evaluate(()=>ackEvents));assert.equal(events.filter(e=>e.commandId).length,5,'Only fully acknowledged capture commands emit receipt feedback');
  assert.deepEqual(errors,[]);checks.push('five-confirmed-commands-only-no-JavaScript-errors');completed=true;
 }finally{if(!completed)await shot('failure').catch(()=>{});fs.writeFileSync(path.join(out,'result.json'),JSON.stringify({completed,checks,writes:writes.map(x=>({...x,body:{...x.body,photoDataUrl:x.body.photoDataUrl?'[packaging JPEG retained]':null}})),events,errors},null,2));}
 console.log('UI-05D BROWSER PASS: '+checks.length+' groups; exact command replay, confirmation and SQL fixture only.');
};
