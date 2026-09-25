'use strict';
const assert=require('node:assert/strict'),path=require('node:path');
module.exports=async({page,info,out,errors,idle,close})=>{
 const endpoint=`/admin/api/stock-documents/${info.receiptId}/intake`,url=info.baseUrl+`/admin/warehouse-receiving/${info.receiptId}`;
 const field=()=>page.locator('.select2-container--open .select2-search__field');
 const read=()=>page.evaluate(async url=>{const response=await fetch(url);if(!response.ok)throw new Error(await response.text());return response.json();},endpoint);
 const ready=async()=>{await page.waitForFunction(()=>window.ReceiptIntake&&document.querySelector('#riBaseUnit').options.length>1);await idle();await page.locator('#wrdLastSaved').waitFor({state:'visible'});};
 async function noHistory(){
  assert.equal(await page.locator('#wrdOpenScanHistory,#wrdScanHistory,#wraHistory,[data-wra-tab=history]').count(),0,'Receiving has no history entry point or panel');
  assert.deepEqual(await page.locator('[data-wra-tab]').evaluateAll(nodes=>nodes.map(x=>x.dataset.wraTab)),['items','info']);
 }
 async function checkTabs(){
  await noHistory();
  await page.locator('[data-wra-tab=info]').click();await page.locator('#wraInfo').waitFor({state:'visible'});
  assert.equal(await page.locator('#wraItems').isVisible(),false);
  await page.locator('[data-wra-tab=info]').press('ArrowRight');await page.locator('#wraItems').waitFor({state:'visible'});
  assert.equal(await page.locator('[data-wra-tab=items]').getAttribute('aria-selected'),'true');
  await page.locator('[data-wra-tab=items]').press('ArrowLeft');await page.locator('#wraInfo').waitFor({state:'visible'});
  await page.locator('[data-wra-tab=items]').click();
 }
 const settle=()=>page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
 async function receive(code,qty=1){
  await page.locator('#quickLookupInput + .select2').click();await field().fill('');await field().pressSequentially(code);await field().press('Enter');
  await page.locator('#quickAddProductModal.show').waitFor();await page.locator('#popupQuickQty').fill(String(qty));
  await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();
 }
 async function checkLatest(label){
  const data=await read(),latest=data.recentReceipts[0];assert.ok(latest,label+': persistent journal entry');
  await settle();
  const top=page.locator('#wrdLinesContainer tbody tr[data-receiving-key]').first();
  assert.equal(await top.getAttribute('data-receiving-key'),latest.rowKey,label+': exact saved line must be first');
  assert.equal(await page.locator('#wrdScanName').innerText(),latest.name,label+': confirmation name matches SQL');
  assert.equal(await top.locator('.wrd-line-name,strong').first().innerText(),latest.name,label+': receipt and confirmation agree');
  assert.match(await page.locator('#wrdScanCode').innerText(),new RegExp(latest.unitName));
  await noHistory();
  return latest;
 }
 assert.ok(await page.locator('#wrdLinesContainer tbody tr').count()>=22);
 // Different variant names under the same parent product exposed the old notification mismatch.
 await page.locator('#quickLookupInput + .select2').click();await field().fill('');await field().pressSequentially('sua kiem tra');
 const choice=page.locator('.select2-results__option--selectable').filter({hasText:'Sữa kiểm tra bàn phím 2'});await choice.click();
 assert.equal(await page.locator('#popupProductName').innerText(),'Sữa kiểm tra bàn phím 2');
 await page.locator('#popupQuickQty').fill('3');await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();
 const a=await checkLatest('Typed product');assert.equal(a.quantity,3);
 assert.equal(await page.evaluate(()=>scrollY),0,'Receiving returns to the promoted product');
 await checkTabs();
 await receive('2099900000001',2);const b=await checkLatest('Different barcode');assert.notEqual(a.rowKey,b.rowKey);
 await receive('2099900000002',1);const repeated=await checkLatest('Repeat earlier product');assert.equal(repeated.rowKey,a.rowKey);assert.equal(repeated.quantity,1);assert.equal(repeated.currentQuantity,4);
 // A new declared packing has no catalogue conversion until a manager reviews it.
 await page.locator('#wraMore').click();await page.locator('#wraNew').click();await page.locator('#receiptIntakeModal.show').waitFor();
 await page.locator('#riNewName').fill('Bánh gạo rong biển gói lớn dành cho gia đình');await page.locator('#riBarcode').fill('HISTORY-NEW-01');
 await page.evaluate(()=>{const select=document.querySelector('#riBaseUnit'),option=[...select.options].find(x=>x.value&&!x.value.startsWith('new:'));window.jQuery(select).val(option.value).trigger('change');});
 await page.locator('#riQuantity').fill('5');await page.locator('#riSave').click();await page.locator('#receiptIntakeModal').waitFor({state:'hidden'});await idle();
 const declared=await checkLatest('New provisional item');assert.ok(declared.rowKey.startsWith('intake-'));
 const historyBefore=(await read()).recentReceipts.map(x=>x.commandId);
 await page.screenshot({path:path.join(out,'feedback-receiving-390.png')});
 await page.reload();await ready();
 assert.equal((await checkLatest('Full page reload')).rowKey,declared.rowKey);
 assert.deepEqual((await read()).recentReceipts.map(x=>x.commandId),historyBefore);
 await page.goto(info.baseUrl+'/admin/warehouse-receiving');await page.goto(url);await ready();
 await checkLatest('Reopen receipt');
 // Quantity edits retain position and never become another receiving event.
 const edit=page.locator(`tr[data-receiving-key="${declared.rowKey}"]`);
 await edit.locator('[data-wra-step="1"]').click();await idle();
 assert.equal((await read()).recentReceipts.length,4);assert.equal(await page.locator('#wrdScanTotal').innerText(),'Hiện trong phiếu: 6 Hộp');
 await page.setViewportSize({width:320,height:844});await settle();
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await checkTabs();
 await page.screenshot({path:path.join(out,'feedback-receiving-320.png')});
 await page.setViewportSize({width:1440,height:1000});await settle();
 await noHistory();
 await receive('2099900000003');await checkLatest('Desktop barcode');
 await page.reload();await ready();await checkLatest('Desktop full reload');
 await page.evaluate(()=>window.jQuery('#quickLookupInput').select2('open'));await field().waitFor();
 const notice=await page.locator('#wrdLastSaved').boundingBox(),search=await page.locator('#quickLookupInput + .select2').boundingBox();
 assert.ok(notice.y+notice.height<=search.y,'Autocomplete cannot cover the latest confirmation');
 await page.screenshot({path:path.join(out,'feedback-receiving-desktop.png')});
 await page.locator('#btnSubmitReceiving').click();await page.locator('#btnConfirmSubmitReceiving').click();await page.waitForURL(u=>u.pathname==='/admin/warehouse-receiving');
 await page.goto(url);await ready();await checkLatest('Read-only receipt');
 await page.setViewportSize({width:390,height:844});await settle();await checkTabs();
 assert.deepEqual(errors,[]);assert.equal(await page.evaluate(()=>testAudioCount),0);
 console.log('PASS receiving without history on desktop/mobile: two tabs with click/keyboard navigation, typed/barcode/provisional receiving, exact row/name/unit/quantity, reload/reopen, quantity edit, read-only receipt, 320/390 layouts, no audio or script errors');
};
