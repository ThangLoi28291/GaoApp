'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',x=>s+=x);process.stdin.on('end',()=>resolve(s));}));
 const out=path.resolve('TestResults/receiving-mobile/browser');fs.mkdirSync(out,{recursive:true});
 const browser=await chromium.launch({channel:'chrome',headless:true});
 const context=await browser.newContext({viewport:{width:390,height:844},isMobile:true,hasTouch:true});
 const page=await context.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(20000);
 const endpoint=`/admin/api/stock-documents/${info.receiptId}/intake`;
 async function login(p,user){await p.goto(info.baseUrl+'/admin/account/login');await p.locator('[name=UserName]').fill(user.user);await p.locator('[name=Password]').fill(user.password);const terminal=p.locator('[name=SelectedTerminalId]');if(await terminal.count())await terminal.selectOption(String(info.terminalId));await Promise.all([p.waitForURL(u=>!u.pathname.endsWith('/login')),p.locator('button[type=submit]').click()]);}
 async function state(){return page.evaluate(async url=>(await(await fetch(url)).json()).state,endpoint);}
 async function idle(){await page.waitForFunction(()=>!window.ReceiptIntake.isSaving()&&!window.receivingHasPendingChanges());}
 async function close(id){
  // display:none precedes backdrop cleanup and hidden.bs.modal. Register the
  // completion signal before clicking so the next action sees settled locks.
  const lifecycle=await page.locator(id).evaluateHandle(modal=>{
   const state={hidden:false};modal.addEventListener('hidden.bs.modal',()=>state.hidden=true,{once:true});return state;
  });
  try{
   await page.locator(`${id} [data-bs-dismiss=modal]`).first().click();
   await page.waitForFunction(state=>state.hidden,lifecycle);
   await page.locator(id).waitFor({state:'hidden'});
  }finally{await lifecycle.dispose();}
 }
 // innerText changes when the Info tab hides this card. Compare each complete
 // field independently so layout whitespace cannot merge quantity/unit fields.
 async function confirmation(){return page.locator('#wrdLastSaved').evaluate(card=>Object.fromEntries(
  ['wrdScanName','wrdScanCode','wrdScanAdded','wrdScanTotal','wrdScanTime'].map(id=>{
   const node=card.querySelector('#'+id);
   return [id,{text:node.textContent.replace(/\s+/g,' ').trim(),dateTime:node.getAttribute('datetime')}];
  })));}
 try{
  await context.addInitScript(()=>{
   window.testMediaCode='INTERNAL-BOX';window.testStreams=[];window.testMediaError=null;
   window.testAudioCount=0;window.Audio=class{constructor(){window.testAudioCount++;}};
   navigator.mediaDevices.getUserMedia=async options=>{
    window.testMediaOptions=options;
    if(window.testMediaError)throw new DOMException('Camera test',window.testMediaError);
    const canvas=document.createElement('canvas');canvas.width=1024;canvas.height=600;
    const ctx=canvas.getContext('2d');ctx.fillStyle='white';ctx.fillRect(0,0,1024,600);
    if(window.testMediaCode){const code=document.createElement('canvas');window.JsBarcode(code,window.testMediaCode,{format:'CODE128',width:3,height:150,fontSize:24,margin:30});ctx.drawImage(code,(1024-code.width)/2,(600-code.height)/2);}
    const stream=canvas.captureStream(12);window.testStreams.push(stream);
    const track=stream.getVideoTracks()[0];track.getCapabilities=()=>({torch:true});track.applyConstraints=async value=>{window.testTorch=value.advanced[0].torch;};
    if(window.testMediaDeferred)await new Promise(resolve=>window.resolveTestMedia=resolve);
    return stream;
   };
   window.SpeechRecognition=class{constructor(){window.testSpeech=this;}start(){}stop(){this.onend?.();}abort(){window.testSpeechAborted=true;}};
  });
  await login(page,info.employee);await page.goto(info.baseUrl+`/admin/warehouse-receiving/${info.receiptId}`);
  await page.waitForFunction(()=>window.ReceiptIntake&&document.querySelector('#riBaseUnit').options.length>1);
  await page.addScriptTag({url:info.baseUrl+'/lib/jsbarcode/JsBarcode.all.min.js'});
  if(process.env.RECEIVING_FEEDBACK_RELOAD_CHECK==='1'){
   await require('./receiving-feedback-reload.browser.cjs')({page,info,context,out,errors,idle,close,state});
   return;
  }
  if(process.env.RECEIVING_INTERACTION_CHECK==='1'){
   await require('./receiving-interaction.browser.cjs')({page,info,out,errors,idle,close,state});
   return;
  }
  assert.equal(await page.evaluate(()=>document.activeElement.tagName==='INPUT'),false,'Mobile must not auto-open the keyboard');
  assert.equal(await page.locator('#wrdOpenTyping').count(),0,'The redundant typing button must be removed');
  assert.equal(await page.locator('#quickLookupInput + .select2').isVisible(),true,'Inline search stays visible on mobile');
  await page.locator('#quickLookupInput + .select2').click();
  await page.locator('.select2-container--open .select2-search__field').fill('sua tuoi');
  await page.locator('.select2-results__option--selectable').first().waitFor();
  assert.match(await page.locator('.select2-results__option--selectable').first().innerText(),/Sữa tươi/);
  await page.evaluate(()=>window.jQuery('#quickLookupInput').select2('close'));
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'No horizontal page overflow');
  await page.screenshot({path:path.join(out,'mobile-receiving.png'),fullPage:true});
  if(process.env.RECEIVING_MOBILE_PREVIEW==='1'){console.log('PASS mobile receiving visual preview');return;}
  if(process.env.RECEIVING_SCROLL_CHECK==='1'){
   await require('./receiving-scroll.browser.cjs')({page,info,context,out,errors,idle,close,state});
   return;
  }
  if(process.env.RECEIVING_SEARCH_CHECK==='1'){
   await page.setViewportSize({width:1440,height:1000});
   await page.locator('#quickLookupInput + .select2').click();
   await page.locator('.select2-container--open .select2-search__field').fill('KHONG DUONG');
   await page.locator('.select2-results__option--selectable').first().waitFor();
   assert.match(await page.locator('.select2-results__option--selectable').first().innerText(),/không đường/);
   await page.locator('.select2-results__option--selectable').first().click();
   await page.locator('#quickAddProductModal.show').waitFor();
   assert.match(await page.locator('#popupProductName').innerText(),/Sữa tươi/);
   await close('#quickAddProductModal');
   await page.locator('.modal-backdrop').waitFor({state:'detached'});
   await page.screenshot({path:path.join(out,'desktop-inline-search.png'),fullPage:true});
   assert.deepEqual(errors,[]);console.log('PASS inline unaccented search on mobile and desktop, product selection, no redundant typing button');return;
  }
  await page.locator('#wrdOpenVoice').click();await page.locator('#wrdCaptureModal.show').waitFor();
  await page.evaluate(()=>testSpeech.onresult({results:[Object.assign([{transcript:'Sữa'}],{isFinal:true})]}));
  await page.locator('.wrd-capture-result').first().waitFor();assert.equal(await page.locator('#quickAddProductModal.show').count(),0,'Voice never adds or selects automatically');
  await page.screenshot({path:path.join(out,'voice-results.png')});
  await page.locator('.wrd-capture-result').first().click();await page.locator('#quickAddProductModal.show').waitFor();
  await page.locator('#popupUnitChooserList button').first().click();await page.locator('#popupQuickQty').fill('1');
  await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();
  assert.equal(await page.locator('#wrdOpenScanHistory,#wrdScanHistory,[data-wra-tab=history]').count(),0);
  assert.match(await page.locator('#wrdScanName').innerText(),/Sữa tươi/);
  assert.equal(await page.locator('#wrdLastSaved').isVisible(),true);
  assert.equal(await page.locator('#wrdScanPhotoModal.show').count(),0,'Success never auto-opens a feedback overlay');
  console.log('PASS voice search, explicit selection, unit selection and real SQL save');

  const firstConfirmation=await confirmation();
  if(process.env.RECEIVING_APP_CHECK==='1')await page.locator('[data-wra-tab="info"]').click();
  await page.locator('#wrdOpenCamera').click();await page.locator('#quickAddProductModal.show').waitFor();
  assert.equal(await page.locator('#popupBarcode').innerText(),'INTERNAL-BOX');
  assert.equal(await page.evaluate(()=>testStreams.every(s=>s.getTracks().every(t=>t.readyState==='ended'))),true,'Camera stops immediately after recognition');
  assert.equal(await page.evaluate(()=>testMediaOptions.video.facingMode.ideal),'environment');
  await page.locator('#popupQuickQty').fill('2.5');await page.evaluate(()=>Promise.all(document.getAnimations().map(a=>a.finished.catch(()=>{}))));await page.screenshot({path:path.join(out,'quantity-bottom-sheet.png')});
  let lostReply=true;const commands=[];
  await page.route('**/intake/known',async route=>{commands.push(route.request().postDataJSON().commandId);if(lostReply){lostReply=false;await route.fetch();await route.abort('failed');}else await route.continue();});
  await page.locator('#btnPopupQuickAddLine').click();await page.waitForFunction(()=>document.querySelector('#wrdSaveStatus').dataset.state==='error');
  assert.equal(await page.locator('#quickAddProductModal.show').count(),1,'Failed reply preserves quantity dialog');
  assert.deepEqual(await confirmation(),firstConfirmation,'Lost acknowledgement must preserve the exact product, barcode/unit, quantities and receipt time');
  assert.equal(await page.locator('#popupQuickQty').isDisabled(),true,'Uncertain save freezes its quantity until replay is acknowledged');
  await close('#quickAddProductModal');await page.locator('#wraRetry').click();await page.locator('#quickAddProductModal.show').waitFor();
  await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();
  assert.equal(commands.length,2);assert.equal(commands[0],commands[1],'Retry must reuse command ID after lost acknowledgement');
  if(process.env.RECEIVING_APP_CHECK==='1')assert.equal(await page.locator('[data-wra-tab="items"]').getAttribute('aria-selected'),'true','Successful scan returns to the received list');
  assert.match(await page.locator('#wrdScanAdded').innerText(),/2,5/);
  const lastKnown=(await state()).items.filter(x=>x.rawBarcode==='INTERNAL-BOX').sort((a,b)=>b.id-a.id)[0];
  assert.equal(await page.locator('#wrdLinesContainer tbody tr').first().getAttribute('data-line-id'),String(lastKnown.resolvedStockDocumentLineId),'Most recently received line is first');
  const afterRetry=await state();assert.equal(afterRetry.items.filter(x=>x.rawBarcode==='INTERNAL-BOX'&&x.quantity===2.5).length,1,'Only one physical intake for retry');
  await page.unroute('**/intake/known');console.log('PASS real ZXing CODE128 camera decode, stop tracks, fractional quantity, lost-reply retry idempotency');

  await page.locator('#wrdOpenCamera').click();await page.locator('#quickAddProductModal.show').waitFor();
  await page.evaluate(()=>testMediaCode='');await page.locator('#wrdAddAndScan').click();await page.locator('#wrdCaptureModal.show').waitFor();
  await page.locator('#wrdTorch:not([hidden])').waitFor();await page.locator('#wrdTorch').click();assert.equal(await page.evaluate(()=>testTorch),true);
  await page.screenshot({path:path.join(out,'camera-scanner.png')});await close('#wrdCaptureModal');
  assert.equal(await page.evaluate(()=>testStreams.every(s=>s.getTracks().every(t=>t.readyState==='ended'))),true);
  await page.evaluate(()=>testMediaError='NotAllowedError');await page.locator('#wrdOpenCamera').click();
  await page.waitForFunction(()=>document.querySelector('#wrdCaptureMessage').textContent.includes('Chưa được cấp quyền'));
  await close('#wrdCaptureModal');
  await page.evaluate(()=>{testMediaError=null;testMediaDeferred=true;});await page.locator('#wrdOpenCamera').click();await page.waitForFunction(()=>!!window.resolveTestMedia);
  await close('#wrdCaptureModal');await page.evaluate(()=>{resolveTestMedia();testMediaDeferred=false;});
  await page.waitForFunction(()=>testStreams.every(s=>s.getTracks().every(t=>t.readyState==='ended')));
  console.log('PASS scan next, torch, permission denial, cancel while permission is pending, media cleanup');

  await page.locator('#quickLookupInput + .select2').click();await page.locator('.select2-container--open .select2-search__field').fill('MOBILE-UNKNOWN-01');await page.locator('.select2-container--open .select2-search__field').press('Enter');
  await page.locator('#receiptIntakeModal.show').waitFor();await page.locator('#riNewTab').click();
  await page.locator('#receiptIntakeModal.show').waitFor();assert.equal(await page.locator('#riBarcode').inputValue(),'MOBILE-UNKNOWN-01');await page.locator('#riNewName').fill('Bánh gạo mới · ảnh bao bì');
  await page.evaluate(()=>{for(const id of ['riBaseUnit','riReceiveUnit','riCategory']){const el=document.getElementById(id),option=[...el.options].find(x=>/^\d+$/.test(x.value));window.jQuery(el).val(option.value).trigger('change');}});
  await page.locator('#riFactor').fill('1');await page.locator('#riQuantity').fill('3');
  const photo=await page.evaluate(()=>{const c=document.createElement('canvas');c.width=600;c.height=380;const x=c.getContext('2d');x.fillStyle='#f8ecca';x.fillRect(0,0,600,380);x.fillStyle='#176451';x.font='bold 40px sans-serif';x.fillText('BÁNH GẠO · 10 GÓI',65,180);return c.toDataURL('image/jpeg',.8).split(',')[1];});
  await page.locator('#riPhoto').setInputFiles({name:'packaging.jpg',mimeType:'image/jpeg',buffer:Buffer.from(photo,'base64')});
  await page.waitForFunction(()=>window.ReceiptIntakePhoto.current()&&!window.ReceiptIntakePhoto.blocked());
  await page.locator('#riPhotoPreview').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(out,'new-product-photo.png')});
  const beforePhotoConfirmation=await page.locator('#wrdLastSaved').innerText();
  let losePhotoReply=true;const photoCommands=[];
  await page.route('**/intake',async route=>{if(route.request().method()!=='POST'){await route.continue();return;}photoCommands.push(route.request().postDataJSON().commandId);if(losePhotoReply){losePhotoReply=false;await route.fetch();await route.abort('failed');}else await route.continue();});
  await page.locator('#riSave').click();await page.waitForFunction(()=>document.querySelector('#wrdSaveStatus').dataset.state==='error');
  assert.equal(await page.locator('#wrdLastSaved').innerText(),beforePhotoConfirmation,'Unknown intake with a lost reply is not confirmed prematurely');
  assert.equal(await page.locator('#riQuantity').isDisabled(),true);
  await close('#receiptIntakeModal');await page.locator('#wraRetry').click();await page.locator('#receiptIntakeModal.show').waitFor();
  await page.locator('#riSave').click();await page.locator('#receiptIntakeModal').waitFor({state:'hidden'});await idle();
  assert.equal(photoCommands.length,2);assert.equal(photoCommands[0],photoCommands[1]);await page.unroute('**/intake');
  const captured=(await state()).items.find(x=>x.rawBarcode==='MOBILE-UNKNOWN-01');assert.equal(captured.hasPhoto,true);assert.equal(captured.quantity,3);
  assert.equal(await page.locator('#wrdScanName').innerText(),'Bánh gạo mới · ảnh bao bì');
  assert.equal(await page.locator('#wrdLinesContainer tbody tr').first().getAttribute('data-intake-item-id'),String(captured.id),'New provisional product moves ahead of known lines');
  assert.match(await page.locator('#wrdScanTotal').innerText(),/Hiện trong phiếu: 3/);
  await page.waitForFunction(()=>!document.querySelector('#wrdScanImageButton').disabled);
  await page.locator('#wrdScanImageButton').click();await page.locator('#wrdScanPhotoModal.show').waitFor();
  await page.waitForFunction(()=>document.querySelector('#wrdScanPhotoLarge').naturalWidth>0);await close('#wrdScanPhotoModal');
  await page.locator('[data-wra-tab="info"]').click();await page.locator('#wraInfo').waitFor();
  assert.equal(await page.locator('#wrdOpenCamera').isVisible(),true,'Camera stays reachable from information');
  await page.locator('[data-wra-tab="items"]').click();
  await page.locator('#wrdLastSaved').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(out,'scan-confirmation-mobile.png')});
  const photoResult=await page.evaluate(async url=>{const r=await fetch(url);return {status:r.status,type:r.headers.get('content-type'),cache:r.headers.get('cache-control'),size:(await r.arrayBuffer()).byteLength};},`${endpoint}/${captured.id}/photo`);
  assert.equal(photoResult.status,200);assert.equal(photoResult.type,'image/jpeg');assert.ok(photoResult.size<=262144);assert.match(photoResult.cache,/no-store/);
  const anonymous=await browser.newContext();const noAuth=await anonymous.request.get(info.baseUrl+`${endpoint}/${captured.id}/photo`,{maxRedirects:0});assert.ok([302,401,403].includes(noAuth.status()));await anonymous.close();
  console.log('PASS unknown product, JPEG resizing, transactional photo persistence and authenticated private read');
  if(process.env.RECEIVING_APP_CHECK==='1'){
   await page.locator('[data-wra-tab="info"]').click();await page.locator('.wrd-side').waitFor();
   await page.locator('#receiptIntakePending').evaluate(el=>el.open=true);
   assert.equal(await page.locator('#riPendingItems').isVisible(),true,'Pending products remain accessible in information');
   await page.screenshot({path:path.join(out,'app-information.png')});
   const knownId=lastKnown.resolvedStockDocumentLineId;
   const known=()=>page.locator(`tr[data-line-id="${knownId}"]`);
   const increaseSelector=`tr[data-line-id="${knownId}"] [data-wra-step="1"]`;
   await page.locator('#wraMore').click();await page.locator('#wraNew').click();await page.locator('#receiptIntakeModal.show').waitFor();
   // A real data refresh can complete while the dialog is open. Make that
   // interleaving deterministic without forcing any lock or disabled property.
   await page.evaluate(()=>window.ReceiptIntake.reload());
   await page.waitForFunction(selector=>document.querySelector(selector)?.disabled,increaseSelector);
   assert.equal(await known().locator('[data-wra-step="1"]').isDisabled(),true,'Open add-item dialog blocks quantity steps');
   await close('#receiptIntakeModal');await page.locator('[data-wra-tab="items"]').click();
   await page.waitForFunction(selector=>document.querySelector(selector)?.disabled===false,increaseSelector);
   assert.equal(await page.evaluate(()=>!!window.receivingHasPendingChanges() || !!window.ReceiptIntake.isSaving() || window.WarehouseReceivingQuantity.hasFailed()),false,'Cancelled add-item flow leaves no pending business operation');
   const before=Number(await known().getAttribute('data-receiving-quantity'));
   await known().locator('[data-wra-step="1"]').click();await idle();
   assert.equal(Number(await known().getAttribute('data-receiving-quantity')),before+1);
   await known().locator('[data-wra-step="-1"]').click();await idle();
   assert.equal(Number(await known().getAttribute('data-receiving-quantity')),before);
   console.log('PASS B3 cancelled add-item flow restores ordinary +/- clicks and exact acknowledged quantities after a modal-time data refresh');
   await known().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('9,125');
   await page.evaluate(()=>Promise.all(document.getAnimations().map(a=>a.finished.catch(()=>{}))));
   await page.screenshot({path:path.join(out,'app-edit-quantity.png')});
   await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();
   assert.equal(Number(await known().getAttribute('data-receiving-quantity')),9.125,'Comma decimal input saves exact quantity');
   await known().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('0');await page.locator('#wraSaveQuantity').click();
   assert.match(await page.locator('#wraQuantityError').innerText(),/lớn hơn 0/);await close('#wraQuantityModal');
   assert.equal(Number(await known().getAttribute('data-receiving-quantity')),9.125,'Invalid and canceled edits never change persisted quantity');
   await known().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('12.5');
   let loseQuantity=true;
   await page.route(`**/lines/${knownId}`,async route=>{if(route.request().method()==='PUT'&&loseQuantity){loseQuantity=false;await route.fetch();await route.abort();}else await route.continue();});
   await page.locator('#wraSaveQuantity').click();await page.waitForFunction(()=>document.querySelector('#wraQuantityError').textContent.includes('Chưa xác nhận'));
   assert.equal(await page.locator('#wraQuantity').inputValue(),'12.5');await close('#wraQuantityModal');
   assert.equal(await known().locator('[data-wra-step="1"]').isDisabled(),true,'Closing a dialog must not unlock an unacknowledged quantity operation');
   await page.locator('#btnSubmitReceiving').click();assert.equal(await page.locator('#btnConfirmSubmitReceiving').isDisabled(),true,'Unacknowledged quantity blocks approval');await close('#submitReceivingModal');
   await page.locator('#wraRetry').click();assert.equal(await page.locator('#wraQuantity').inputValue(),'12.5');
   await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();await page.unroute(`**/lines/${knownId}`);
   assert.equal(Number(await known().getAttribute('data-receiving-quantity')),12.5,'Lost response retry sets total once');
   const provisional=()=>page.locator(`tr[data-intake-item-id="${captured.id}"]`);
   await provisional().locator('[data-wra-step="1"]').click();await idle();
   assert.equal((await state()).items.find(x=>x.id===captured.id).quantity,4);
   await provisional().locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('6');
   let loseProvisional=true;const quantityCommands=[];
   await page.route(`**/intake/${captured.id}/quantity`,async route=>{quantityCommands.push(route.request().postDataJSON().commandId);if(loseProvisional){loseProvisional=false;await route.fetch();await route.abort();}else await route.continue();});
   await page.locator('#wraSaveQuantity').click();await page.waitForFunction(()=>document.querySelector('#wraQuantityError').textContent.includes('Chưa xác nhận'));
   assert.equal(await page.locator('#wraQuantity').isDisabled(),true,'Unacknowledged provisional quantity remains frozen for exact replay');
   assert.equal(await page.locator('[data-wra-quantity-step="1"]').isDisabled(),true);
   await close('#wraQuantityModal');await page.locator('#wraRetry').click();await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();
   assert.equal(quantityCommands.length,2);assert.equal(quantityCommands[0],quantityCommands[1]);assert.equal((await state()).items.find(x=>x.id===captured.id).quantity,6);
   await page.unroute(`**/intake/${captured.id}/quantity`);
   await provisional().locator('[data-wra-edit]').click();await page.locator('#wraRemove').click();await page.locator('#receiptIntakeReviewModal.show').waitFor();await close('#receiptIntakeReviewModal');
   await context.setOffline(true);await page.waitForFunction(()=>document.querySelector('#wraStatus').dataset.state==='error');await context.setOffline(false);
   for(const width of [320,390,768]){
    await page.setViewportSize({width,height:844});await page.locator('[data-wra-tab="items"]').click();await page.evaluate(()=>window.scrollTo(0,0));
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,`No overflow at ${width}`);
    const dock=await page.locator('.wra-dock').boundingBox();assert.ok(dock.y+dock.height<=845&&dock.y>500);
    await page.screenshot({path:path.join(out,`app-${width}.png`)});
    await page.locator('[data-wra-tab="info"]').click();assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,`Information has no overflow at ${width}`);await page.locator('[data-wra-tab="items"]').click();
   }
   await page.setViewportSize({width:1440,height:1000});
   // matchMedia change reparents the same controls asynchronously. Observe
   // that completed move before checking the resulting desktop/mobile layout.
   await page.locator('.wrd-capture-actions #wrdOpenCamera').waitFor({state:'visible'});
   assert.equal(await page.locator('.wra-dock').isVisible(),false);
   assert.equal(await page.locator('.wrd-capture-actions #wrdOpenCamera').isVisible(),true,'Original controls restored when moving to desktop');
   await page.setViewportSize({width:390,height:844});
   await page.locator('#wraDockActions #wrdOpenCamera').waitFor({state:'visible'});
   assert.equal(await page.locator('#wraDockActions #wrdOpenCamera').isVisible(),true);
   const removed=page.locator(`tr[data-receiving-conversion="${info.packId}"]`),removedId=await removed.getAttribute('data-line-id');
   await removed.locator('[data-wra-edit]').click();page.once('dialog',dialog=>dialog.accept());await page.locator('#wraRemove').click();await page.locator(`tr[data-line-id="${removedId}"]`).waitFor({state:'detached'});await idle();
   console.log('PASS app tabs, pending items, more menu, row steppers, decimal sheet, validation, known/provisional lost-response retries, offline indicator, 320/390/768/1440 layouts');
  }
  if(process.env.RECEIVING_FEEDBACK_CHECK==='1'){
   async function receiveBarcode(code,quantity){
    await page.locator('#quickLookupInput + .select2').click();
    await page.locator('.select2-container--open .select2-search__field').fill(code);
    await page.locator('.select2-container--open .select2-search__field').press('Enter');
    if(code==='MOBILE-UNKNOWN-01'){
     await page.locator('#receiptIntakeModal.show').waitFor();await page.locator('#riQuantity').fill(String(quantity));await page.locator('#riSave').click();await page.locator('#receiptIntakeModal').waitFor({state:'hidden'});
    }else{
     await page.locator('#quickAddProductModal.show').waitFor();await page.locator('#popupQuickQty').fill(String(quantity));await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});
    }
    await idle();
   }
   await receiveBarcode('MOBILE-UNKNOWN-01',2);
   assert.match(await page.locator('#wrdScanTotal').innerText(),/Hiện trong phiếu: 5/);
   assert.equal((await state()).items.find(x=>x.id===captured.id).quantity,5,'Repeated unknown scan accumulates on the same item');
   await page.emulateMedia({reducedMotion:'reduce'});
   for(let i=0;i<7;i++)await receiveBarcode('INTERNAL-BOX',1);
   const packBefore=Number(await page.locator(`[data-receiving-conversion="${info.packId}"]`).getAttribute('data-receiving-quantity'));
   await page.locator('#quickLookupInput + .select2').click();
   await page.locator('.select2-container--open .select2-search__field').fill('sua tuoi');
   await page.locator('.select2-results__option--selectable').first().click();await page.locator('#quickAddProductModal.show').waitFor();
   await page.locator('#popupUnitChooserList button').filter({hasText:'Lốc'}).click();
   await page.locator('#popupQuickQty').fill('2');await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();
   assert.equal(await page.locator('#wrdLinesContainer tbody tr').first().getAttribute('data-receiving-conversion'),String(info.packId),'Same product in another unit must promote the correct conversion');
   assert.match(await page.locator('#wrdScanAdded').innerText(),/2 Lốc/);
   assert.equal(await page.locator('#wrdScanTotal').innerText(),`Hiện trong phiếu: ${packBefore+2} Lốc`);
   assert.equal(await page.locator('#wrdScanHistory,#wraHistory').count(),0,'Repeated receiving does not create a history panel');
   const firstKey=await page.locator('#wrdLinesContainer tbody tr').first().getAttribute('data-receiving-key');
   await page.evaluate(()=>window.refreshReceivingLines());
   assert.equal(await page.locator('#wrdLinesContainer tbody tr').first().getAttribute('data-receiving-key'),firstKey,'Refreshing receipt rows preserves recency');
   assert.equal(await page.locator('#wrdLastSaved').evaluate(el=>getComputedStyle(el).animationName),'none','Reduced motion disables confirmation animation');
   assert.equal(await page.evaluate(()=>testAudioCount),0,'Feedback is silent');
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
   await page.setViewportSize({width:1440,height:1000});
   assert.equal(await page.locator('#wrdOpenScanHistory,#wrdScanHistory').count(),0,'Desktop has no receiving history');
   await page.screenshot({path:path.join(out,'scan-confirmation-desktop.png')});
   assert.deepEqual(errors,[]);console.log('PASS confirmation card, silent feedback, exact quantities, retry deduplication, row recency, photo on demand, no receiving history on mobile/desktop and reduced motion');return;
  }
  const managerContext=await browser.newContext({viewport:{width:1440,height:1000}}),manager=await managerContext.newPage();manager.on('pageerror',e=>errors.push(e.message));
  await login(manager,info);await manager.goto(info.baseUrl+`/admin/warehouse-receiving/${info.receiptId}`);await manager.waitForFunction(()=>window.ReceiptIntake&&document.querySelector('#riPendingCount').textContent!=='0');
  await manager.locator('#receiptIntakePending').evaluate(el=>el.open=true);await manager.locator(`[data-ri-review="${captured.id}"]`).click();await manager.locator('.ri-evidence-photo').waitFor();await manager.waitForFunction(()=>document.querySelector('.ri-evidence-photo').naturalWidth>0);
  await manager.screenshot({path:path.join(out,'manager-photo-review.png')});await managerContext.close();
  await page.setViewportSize({width:1440,height:1000});await page.screenshot({path:path.join(out,'desktop-receiving.png'),fullPage:true});
  if(process.env.RECEIVING_APP_CHECK==='1'){
   await page.setViewportSize({width:390,height:844});await page.locator('#btnSubmitReceiving').click();
   await page.locator('#btnConfirmSubmitReceiving').click();await page.waitForURL(u=>u.pathname==='/admin/warehouse-receiving');
   await page.goto(info.baseUrl+`/admin/warehouse-receiving/${info.receiptId}`);await page.locator('.wra-header').waitFor();
   assert.equal(await page.locator('#wraDockActions').count(),0,'Pending approval is read-only');
   assert.equal(await page.locator('[data-wra-edit]').count(),0);
   assert.ok(Number(await page.locator('#wraCount').innerText())>0,'Read-only receipt retains summary');
   await page.locator('[data-wra-tab="info"]').click();await page.locator('#btnRequestRevision').click();await page.locator('#requestRevisionModal.show').waitFor();
   await page.locator('#txtRevisionNote').fill('Kiểm tra lại số lượng thực nhận');await page.locator('#btnConfirmRevision').click();
   await page.waitForFunction(()=>!!document.querySelector('.wrd-revision-info'));
   await page.locator('[data-wra-tab="info"]').click();await page.screenshot({path:path.join(out,'app-pending-revision.png')});
   console.log('PASS real approval submission, read-only mobile receipt and revision request');
  }
  assert.deepEqual(errors,[]);console.log('PASS manager review photo, mobile/desktop layouts, no JavaScript errors');
 }catch(error){await page.screenshot({path:path.join(out,'failure.png'),fullPage:true}).catch(()=>{});console.error(await page.locator('body').innerText().catch(()=>''));throw error;}
 finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
