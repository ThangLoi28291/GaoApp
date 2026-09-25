'use strict';
const assert=require('node:assert/strict'),path=require('node:path');
module.exports=async({page,info,out,errors,idle,close,state})=>{
 const rowSelector='#wrdLinesContainer tr[data-receiving-key]';
 const settled=()=>page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
 const position=key=>page.evaluate(key=>{
  const row=[...document.querySelectorAll('#wrdLinesContainer tr[data-receiving-key]')].find(x=>x.dataset.receivingKey===key);
  return {scroll:scrollY,top:row.getBoundingClientRect().top,index:[...row.parentElement.children].indexOf(row),quantity:Number(row.dataset.receivingQuantity)};
 },key);
 const target=key=>page.locator(`${rowSelector}[data-receiving-key="${key}"]`);
 async function watch(key){
  await page.evaluate(key=>{
   window.scrollSamples=[];window.recordScroll=true;
   const tick=()=>{if(!window.recordScroll)return;const row=[...document.querySelectorAll('#wrdLinesContainer tr[data-receiving-key]')].find(x=>x.dataset.receivingKey===key);if(row)window.scrollSamples.push({scroll:scrollY,top:row.getBoundingClientRect().top});requestAnimationFrame(tick);};tick();
  },key);
 }
 async function unchanged(key,before,label){
  await idle();await settled();const after=await position(key);
  const samples=await page.evaluate(()=>{window.recordScroll=false;return window.scrollSamples||[];});
  const jump=Math.max(...samples.map(x=>Math.abs(x.top-before.top)),Math.abs(after.top-before.top));
  console.log(`${label}: scroll ${before.scroll} -> ${after.scroll}, row ${before.top} -> ${after.top}, max frame shift ${jump}`);
  assert.equal(after.index,before.index,`${label}: editing must not reorder the row`);
  assert.ok(jump<=4,`${label}: row jumped ${jump}px while saving`);
  assert.ok(Math.abs(after.scroll-before.scroll)<=4,`${label}: scroll position changed`);
  return after;
 }
 assert.ok(await page.locator(rowSelector).count()>=22,'Fixture must extend well below the phone viewport');
 const knownKey=await page.locator(rowSelector).last().getAttribute('data-receiving-key');
 for(const width of [390,320]){
  await page.setViewportSize({width,height:844});
  await target(knownKey).locator('[data-wra-edit]').scrollIntoViewIfNeeded();await settled();
  for(const delta of [1,1,-1,-1]){
   const before=await position(knownKey);assert.ok(before.scroll>1500,'Exercise a row near the bottom of a long receipt');
   await watch(knownKey);await target(knownKey).locator(`[data-wra-step="${delta}"]`).click();
   const after=await unchanged(knownKey,before,`known ${width}px ${delta>0?'+':'−'}`);
   assert.equal(after.quantity,before.quantity+delta);
  }
  await page.screenshot({path:path.join(out,`scroll-stable-${width}.png`)});
 }
 await page.setViewportSize({width:390,height:844});
 await target(knownKey).locator('[data-wra-edit]').scrollIntoViewIfNeeded();await settled();
 const beforeSheet=await position(knownKey);
 await target(knownKey).locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill(String(beforeSheet.quantity+2.5));
 await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();await settled();
 const afterSheet=await position(knownKey);assert.ok(Math.abs(afterSheet.top-beforeSheet.top)<=4,'Closing quantity sheet keeps its row in place');
 assert.equal(afterSheet.quantity,beforeSheet.quantity+2.5);
 // If the employee scrolls during a slow response, keep the viewport they moved to.
 let release,requested=false;const gate=new Promise(resolve=>release=resolve);
 await page.route(`**/warehouse-receiving/${info.receiptId}/lines`,async route=>{requested=true;await gate;await route.continue();});
 await target(knownKey).locator('[data-wra-step="1"]').click();
 await new Promise((resolve,reject)=>{const deadline=Date.now()+20000;const poll=()=>requested?resolve():Date.now()>deadline?reject(new Error('Quantity save did not request refreshed rows')):setTimeout(poll,10);poll();});
 await page.evaluate(()=>scrollBy(0,-650));await settled();
 const visibleKey=await page.evaluate(()=>[...document.querySelectorAll('#wrdLinesContainer tr[data-receiving-key]')].find(row=>row.getBoundingClientRect().top>110)?.dataset.receivingKey);
 const moved=await position(visibleKey);await watch(visibleKey);release();
 await unchanged(visibleKey,moved,'scrolling during slow save');await page.unroute(`**/warehouse-receiving/${info.receiptId}/lines`);
 // Create a real provisional item, then reopen the receipt so it appears after the known rows.
 await page.locator('#wraMore').click();await page.locator('#wraNew').click();await page.locator('#receiptIntakeModal.show').waitFor();
 await page.locator('#riNewName').fill('Hàng kiểm tra cuộn cuối phiếu');await page.locator('#riBarcode').fill('SCROLL-PROVISIONAL');
 await page.evaluate(()=>{const select=document.querySelector('#riBaseUnit'),option=[...select.options].find(x=>x.value&&!x.value.startsWith('new:'));window.jQuery(select).val(option.value).trigger('change');});
 await page.locator('#riQuantity').fill('3');await page.locator('#riSave').click();await page.locator('#receiptIntakeModal').waitFor({state:'hidden'});await idle();
 const provisional=(await state()).items.find(x=>x.rawBarcode==='SCROLL-PROVISIONAL');assert.ok(provisional);
 await page.reload();await page.waitForFunction(()=>window.ReceiptIntake&&document.querySelector('#riBaseUnit').options.length>1);await settled();
 const provisionalKey=`intake-${provisional.id}`;
 await target(provisionalKey).locator('[data-wra-edit]').scrollIntoViewIfNeeded();await settled();
 for(const delta of [1,-1]){
  const before=await position(provisionalKey);await watch(provisionalKey);await target(provisionalKey).locator(`[data-wra-step="${delta}"]`).click();
  const after=await unchanged(provisionalKey,before,`provisional ${delta>0?'+':'−'}`);assert.equal(after.quantity,before.quantity+delta);
 }
 const beforeProvisionalSheet=await position(provisionalKey);
 await target(provisionalKey).locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('7.5');await page.locator('#wraSaveQuantity').click();await page.locator('#wraQuantityModal').waitFor({state:'hidden'});await idle();await settled();
 assert.ok(Math.abs((await position(provisionalKey)).top-beforeProvisionalSheet.top)<=4,'Provisional quantity sheet preserves row position');
 // A newly received scan should still move to the top and show its confirmation.
 await page.locator('#wraMore').click();await page.locator('#wraShowInfo').click();await page.locator('[data-wra-tab="items"]').click();
 await page.evaluate(()=>window.scrollTo(0,0));await page.locator('#quickLookupInput + .select2').click();
 await page.locator('.select2-container--open .select2-search__field').fill('INTERNAL-BOX');await page.locator('.select2-container--open .select2-search__field').press('Enter');
 await page.locator('#quickAddProductModal.show').waitFor();await page.locator('#btnPopupQuickAddLine').click();await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();await settled();
 assert.equal(await page.evaluate(()=>scrollY),0,'New scans still reveal the latest received product');
 assert.equal(await page.locator(rowSelector).first().locator('.wrd-line-meta').innerText().then(x=>x.includes('INTERNAL-BOX')),true);
 assert.deepEqual(errors,[]);
 console.log('PASS mobile lower-row +/−, fractional quantity sheets, provisional items, slow-response user scrolling and scan-to-top behavior on a long SQL receipt');
};
