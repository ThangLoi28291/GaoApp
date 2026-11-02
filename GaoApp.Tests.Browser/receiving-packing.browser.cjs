'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
module.exports=async({page,info,context,out,errors,idle,close,login,browser})=>{
 const checks=[],writes=[],csp=[];let completed=false;
 const observe=p=>{p.on('request',r=>{if(!['GET','HEAD'].includes(r.method())&&/\/admin\/(?:warehouse-receiving|api\/stock-documents)/.test(new URL(r.url()).pathname))writes.push({url:r.url(),method:r.method(),body:r.postDataJSON()});});p.on('console',m=>{if(/Content Security Policy/i.test(m.text()))csp.push(m.text());});};observe(page);
 const url=info.baseUrl+'/admin/warehouse-receiving/'+info.receiptId;
 const settle=p=>p.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
 async function ready(p=page){await p.waitForFunction(()=>window.WarehouseReceivingApp&&window.ReceiptIntake&&document.querySelector('#riBaseUnit').options.length>1);await settle(p);await p.evaluate(()=>document.fonts.ready);}
 async function size(width,height){await page.setViewportSize({width,height});await page.waitForFunction(w=>!!document.querySelector('[data-receiving-packing]')===(w<768),width);await settle(page);}
 async function overflow(p=page){const b=await p.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth}));assert.ok(b.scroll<=b.width+1,JSON.stringify(b));}
 const shot=name=>page.screenshot({path:path.join(out,name+'.png'),animations:'disabled'});
 // Existing library measurements can leave only an inert empty style on input/riNote.
 const canonical=html=>html.replace(/(<input[^>]*?) style=""/g,'$1').replace(/(<textarea[^>]*\bid="riNote"[^>]*?) style=""/g,'$1');
 async function closeFor(p,id){const done=await p.locator(id).evaluateHandle(el=>{const s={done:false};el.addEventListener('hidden.bs.modal',()=>s.done=true,{once:true});return s;});await p.locator(id+' .modal-header [data-bs-dismiss=modal]').click();await p.waitForFunction(s=>s.done,done);await done.dispose();}
 try{
  await ready();await size(1440,900);await page.evaluate(()=>scrollTo(0,0));
  const selectors=['.wrd-topbar','.wrd-layout','#receiptDocumentActions','#receiptIntakeModal'];
  const baseline=(await page.evaluate(selectors=>{window.__packingOriginal=selectors.flatMap(s=>[...document.querySelectorAll(s+', '+s+' *')]);return selectors.map(s=>document.querySelector(s).outerHTML);},selectors)).map(canonical);
  for(const [width,height]of[[1600,1000],[1440,900],[1366,768],[1024,768],[768,1024],[767,900],[430,932],[390,844],[360,800],[320,740]]){
   await size(width,height);await overflow();
   if(width<768){assert.equal(await page.locator('.wrc-quantity-unit').count(),3);assert.equal(await page.locator('.wrc-workload strong').innerText(),'3 dòng trên phiếu');assert.equal(await page.locator('.wrc-task-actions button:visible').count(),0);await shot('packing-phone-'+width);}
   else assert.equal(await page.locator('.wrc-workload,.wrc-quantity-unit,.wrc-evidence').count(),0);
   checks.push('viewport-'+width+'x'+height);
  }
  await size(1440,900);
  assert.deepEqual((await page.evaluate(selectors=>selectors.map(s=>document.querySelector(s).outerHTML),selectors)).map(canonical),baseline);
  assert.equal(await page.evaluate(()=>window.__packingOriginal.every(n=>n.isConnected)),true);
  const current=await page.screenshot({animations:'disabled'});fs.writeFileSync(path.join(out,'packing-desktop-current.png'),current);
  await page.locator('link[href*="warehouse-receiving-packing.mobile.css"]').evaluate(n=>n.disabled=true);
  const original=await page.screenshot({animations:'disabled'});fs.writeFileSync(path.join(out,'packing-desktop-baseline.png'),original);assert.ok(current.equals(original));
  await page.locator('link[href*="warehouse-receiving-packing.mobile.css"]').evaluate(n=>n.disabled=false);
  checks.push('desktop-exact-DOM-original-fields-and-pixel-equality');assert.deepEqual(writes,[]);
  await size(390,844);
  const row=id=>page.locator('tr[data-line-id="'+id+'"]');
  assert.match(await row(info.ui05c.packLineId).locator('.wrc-quantity-unit').innerText(),/Lốc/);
  assert.match(await row(info.ui05c.otherLineId).locator('.wrc-quantity-unit').innerText(),/Lốc/);
  assert.match(await row(info.ui05c.packLineId).locator('.wrd-line-meta').innerText(),/4/);
  assert.match(await row(info.ui05c.otherLineId).locator('.wrd-line-meta').innerText(),/6/);
  await row(info.ui05c.packLineId).locator('[data-wra-edit]').click();await page.locator('#wraQuantityModal.show').waitFor();
  assert.match(await page.locator('#wraQuantityUnit').innerText(),/Lốc/);assert.match(await page.locator('#wraQuantityMeta').innerText(),/4/);
  await page.locator('#wraQuantity').fill('2.5');await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();
  assert.equal(Number(await row(info.ui05c.packLineId).getAttribute('data-receiving-quantity')),2.5);
  assert.equal(Number(await row(info.ui05c.cartonLineId).getAttribute('data-receiving-quantity')),2);
  assert.equal(Number(await row(info.ui05c.otherLineId).getAttribute('data-receiving-quantity')),3);
  checks.push('same-unit-different-factors-and-fractional-save-keeps-other-quantities');
  await page.locator('#wraMore').click();await page.locator('#wraNew').click();await page.locator('#receiptIntakeModal.show').waitFor();
  await page.waitForFunction(()=>!bootstrap.Modal.getInstance(document.getElementById('receiptIntakeModal'))._isTransitioning);
  assert.equal(await page.locator('#riNewTab').getAttribute('aria-selected'),'true');
  await page.locator('#riNewName').fill('Hàng mới UI05c');
  await page.evaluate(({base,receive})=>{window.jQuery('#riBaseUnit').val(String(base)).trigger('change');window.jQuery('#riReceiveUnit').val(String(receive)).trigger('change');},{base:info.ui05c.baseUnitId,receive:info.ui05c.receiveUnitId});
  await page.locator('#riFactor').fill('12');await page.locator('#riQuantity').fill('2.5');
  assert.equal(await page.locator('#riQuantity').evaluate(n=>n.validity.valid),true);
  assert.match(await page.locator('#riEquivalent').innerText(),/30 Hộp/);
  assert.equal(await page.locator('#riApproveWrap').isVisible(),false);
  await shot('packing-unknown-390');checks.push('unknown-packing-equivalent-per-base-unit-and-employee-no-approval');
  await page.locator('.wrc-evidence summary').click();await page.locator('#riNote').fill('Đối chiếu nhãn và quy cách 12 hộp.');
  await page.locator('#riPhoto').setInputFiles({name:'bad.svg',mimeType:'image/svg+xml',buffer:Buffer.from('<svg/>')});
  await page.waitForFunction(()=>window.ReceiptIntakePhoto.blocked()&&document.getElementById('riPhotoStatus').textContent.includes('JPEG'));
  const priorWrites=writes.length;await page.locator('#riSave').click();assert.equal(writes.length,priorWrites);assert.match(await page.locator('#riError').innerText(),/Ảnh/);
  await page.locator('#riPhotoRemove').click();
  const png=await page.evaluate(()=>{const c=document.createElement('canvas');c.width=900;c.height=650;const x=c.getContext('2d');x.fillStyle='#e8f1ff';x.fillRect(0,0,900,650);x.fillStyle='#203554';x.font='bold 58px sans-serif';x.fillText('BAO BI 12 HOP',110,330);return c.toDataURL('image/png').split(',')[1];});
  const choosePhoto=page.waitForEvent('filechooser');await page.locator('.wrc-photo-choose').click();
  await (await choosePhoto).setFiles({name:'bao-bi.png',mimeType:'image/png',buffer:Buffer.from(png,'base64')});
  await page.waitForFunction(()=>!!window.ReceiptIntakePhoto.current()&&!window.ReceiptIntakePhoto.blocked());
  const jpeg=await page.evaluate(()=>{window.__packingPhotoNode=document.getElementById('riPhoto');return window.ReceiptIntakePhoto.current();});
  assert.match(jpeg,/^data:image\/jpeg;base64,/);await shot('packing-photo-390');
  for(const [width,height]of[[320,568],[390,440],[1440,900],[390,844]]){
   await size(width,height);assert.equal(await page.locator('#riNote').inputValue(),'Đối chiếu nhãn và quy cách 12 hộp.');
   assert.equal(await page.locator('#riQuantity').inputValue(),'2.5');assert.equal(await page.locator('#riFactor').inputValue(),'12');
   assert.equal(await page.evaluate(()=>document.getElementById('riPhoto')===window.__packingPhotoNode),true);assert.equal(await page.evaluate(()=>window.ReceiptIntakePhoto.current()),jpeg);
   if(width<768)await page.waitForFunction(h=>Math.abs(parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--gao-mobile-visible-height'))-h)<1,height);
   await settle(page);const box=await page.locator('#receiptIntakeModal .modal-content').boundingBox();assert.ok(box.y>=-1&&box.y+box.height<=height+1,JSON.stringify(box));
  }
  checks.push('invalid-photo-blocks-submit-valid-photo-and-original-fields-survive-short-screen-resize');
  await page.locator('#riSave').click();await page.locator('#receiptIntakeModal').waitFor({state:'hidden'});await idle();
  await page.waitForFunction(()=>document.querySelector('.wrc-task-actions [aria-controls=receiptIntakePending]')?.textContent.startsWith('1 '));
  assert.equal(await page.locator('.wrc-workload strong').innerText(),'4 dòng trên phiếu');
  await page.locator('.wrc-task-actions [aria-controls=receiptIntakePending]').click();
  await page.waitForFunction(()=>document.querySelector('#receiptIntakePending').open&&document.querySelector('[data-wra-tab=info]').getAttribute('aria-selected')==='true');
  assert.match(await page.locator('#riPendingItems').innerText(),/2,5 Thùng/);assert.match(await page.locator('#riPendingItems').innerText(),/12 Hộp/);
  assert.equal(await page.locator('#riPendingItems [data-ri-review]').count(),0);await shot('packing-employee-pending-390');
  await page.locator('[data-wra-tab=items]').click();await page.locator('.wrc-task-actions [aria-controls=riPhotoEvidence]').click();
  await page.waitForFunction(()=>document.querySelector('#riPhotoEvidence').open);
  const photo=page.locator('#riPhotoItems a').first();assert.match(await photo.innerText(),/Hàng mới UI05c/);
  const photoResponse=await context.request.get(info.baseUrl+await photo.getAttribute('href'));assert.equal(photoResponse.status(),200);assert.match(photoResponse.headers()['content-type'],/image\/jpeg/);
  await shot('packing-photo-evidence-390');checks.push('acknowledged-line-count-pending-and-photo-shortcuts-open-original-tasks-with-correct-permissions');
  const managerContext=await browser.newContext({viewport:{width:390,height:844},isMobile:true,hasTouch:true});const manager=await managerContext.newPage();observe(manager);manager.on('pageerror',e=>errors.push(e.message));
  await login(manager,info);await manager.goto(url);await ready(manager);
  await manager.waitForFunction(()=>document.querySelector('.wrc-task-actions [aria-controls=receiptIntakePending]')?.textContent.startsWith('1 '));
  await manager.locator('.wrc-task-actions [aria-controls=receiptIntakePending]').click();await manager.locator('[data-ri-review]').first().click();await manager.locator('#receiptIntakeReviewModal.show').waitFor();
  await manager.locator('.ri-evidence-photo').waitFor();assert.equal(await manager.locator('#riReviewQuantity').inputValue(),'2.5');assert.equal(await manager.locator('#riReviewFactor').inputValue(),'12');
  await manager.locator('#riReviewName').fill('Hàng mới đã kiểm tra UI05c');
  await manager.screenshot({path:path.join(out,'packing-manager-review-390.png'),animations:'disabled'});
  const reviewResponse=manager.waitForResponse(r=>r.request().method()==='POST'&&r.url().includes('/intake/')&&r.url().endsWith('/review'));
  await manager.locator('#riReviewSaveDraft').click();assert.equal((await reviewResponse).status(),200);
  await manager.waitForFunction(()=>!window.ReceiptIntake.isSaving()&&!window.ReceiptIntakeReview.dirty());
  await closeFor(manager,'#receiptIntakeReviewModal');await manager.reload();await ready(manager);
  await manager.locator('.wrc-task-actions [aria-controls=receiptIntakePending]').click();await manager.locator('[data-ri-review]').first().click();await manager.locator('#receiptIntakeReviewModal.show').waitFor();
  await manager.waitForFunction(()=>document.getElementById('riReviewName').value==='Hàng mới đã kiểm tra UI05c');assert.match(await manager.locator('#riReviewFacts').innerText(),/Hàng mới UI05c/);
  await managerContext.close();checks.push('manager-original-review-opens-packaging-and-saves-completion-draft-without-approval');
  await page.locator('[data-wra-tab=items]').click();await page.emulateMedia({forcedColors:'active'});await overflow();await shot('packing-forced-colors-390');await page.emulateMedia({forcedColors:'none'});
  await page.evaluate(()=>document.documentElement.style.fontSize='24px');await overflow();await page.evaluate(()=>document.documentElement.style.fontSize='');checks.push('forced-colors-and-150-percent-text-no-page-overflow');
  assert.equal(writes.length,3,JSON.stringify(writes));assert.equal(writes.filter(x=>x.method==='PUT').length,1);assert.equal(writes.filter(x=>x.url.endsWith('/review')&&x.body.saveDraftOnly===true&&x.body.approve===false).length,1);
  assert.deepEqual(errors,[]);assert.deepEqual(csp,[]);checks.push('only-quantity-provisional-and-review-draft-commands-no-JS-or-CSP-error');completed=true;
 }finally{if(!completed)await shot('ui05c-failure').catch(()=>{});fs.writeFileSync(path.join(out,'result.json'),JSON.stringify({completed,checks,writes,errors,csp},null,2));}
 console.log('UI-05C BROWSER PASS: '+checks.length+' groups, own headless Chrome only.');
};
