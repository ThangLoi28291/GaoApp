const assert=require('node:assert/strict');
const fs=require('node:fs');const path=require('node:path');const {chromium}=require('playwright');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',x=>s+=x);process.stdin.on('end',()=>resolve(s));}));
 const browser=await chromium.launch({channel:'chrome',headless:true});
 const context=await browser.newContext({viewport:{width:1440,height:1000}});const page=await context.newPage();
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 const output=path.resolve('TestResults/intake-completion');fs.mkdirSync(output,{recursive:true});
 async function open(id){const section=page.locator('#receiptIntakePending');if(await section.getAttribute('open')===null)await section.locator('summary').click();await page.locator(`[data-ri-review="${id}"]`).click();await page.locator('#receiptIntakeReviewModal').waitFor({state:'visible'});}
 async function supplier(){await page.locator('#riReviewSupplier + .select2 .select2-selection').click();await page.locator('.select2-container--open .select2-search__field').fill(info.supplierName);await page.locator('.select2-results__option--selectable').filter({hasText:info.supplierName}).click();}
 try {
 await page.goto(info.baseUrl+'/admin/account/login');await page.locator('[name=UserName]').fill(info.user);await page.locator('[name=Password]').fill(info.password);
 if(await page.locator('[name=SelectedTerminalId]').count())await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
 await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('button[type=submit]').click()]);
 const url=info.baseUrl+`/admin/stock-documents/${info.receiptId}`;await page.goto(url);await open(info.itemIds[0]);
 await page.locator('#riReviewName').fill('Bánh kiểm tra giao diện');await page.locator('#riReviewBarcode').fill('0009876543210');
 await page.locator('#riReviewCost').fill('12000');await page.locator('#riReviewRetail').fill('15000');await page.locator('#riReviewWholesale').fill('14000');await page.locator('#riReviewSellable').check();await supplier();
 const photoBytes=Buffer.from(await page.evaluate(()=>{const c=document.createElement('canvas');c.width=240;c.height=240;const x=c.getContext('2d');x.fillStyle='#f2ce74';x.fillRect(0,0,240,240);x.fillStyle='#704c26';x.font='bold 30px sans-serif';x.fillText('BANH MOI',24,130);return c.toDataURL('image/png').split(',')[1];}),'base64');
 await page.locator('#riReviewPhotoFile').setInputFiles({name:'product.png',mimeType:'image/png',buffer:photoBytes});
 await page.waitForFunction(()=>document.getElementById('riReviewPhotoStatus').textContent.includes('sẵn sàng'));
 assert.match(await page.locator('#riReviewCostHint').innerText(),/12\.000/);
 await page.locator('#riReviewSaveDraft').click();await page.waitForFunction(()=>document.getElementById('riReviewSaveStatus').textContent==='Đã lưu nháp vào phiếu'&&!window.ReceiptIntake.isSaving());
 await page.locator('#receiptIntakeReviewModal .btn-close').click();await page.locator('#receiptIntakeReviewModal').waitFor({state:'hidden'});await page.reload();await open(info.itemIds[0]);
 await page.waitForFunction(()=>document.getElementById('riReviewPhotoPreview').naturalWidth>0);
 assert.equal(await page.locator('#riReviewPhotoPreview').isVisible(),true);
 assert.ok((await page.locator('#riReviewPhotoPreview').getAttribute('src')).includes('/review-photo'));
 assert.equal(await page.locator('#riReviewName').inputValue(),'Bánh kiểm tra giao diện');assert.equal(await page.locator('#riReviewBarcode').inputValue(),'0009876543210');assert.equal(await page.locator('#riReviewRetail').inputValue(),'15000');assert.equal(await page.locator('#riReviewSupplier').inputValue(),String(info.supplierId));assert.equal(await page.locator('#riReviewSellable').isChecked(),true);
 await page.locator('#receiptIntakeReviewModal .modal-body').evaluate(x=>x.scrollTop=0);await page.locator('#receiptIntakeReviewModal').screenshot({path:path.join(output,'desktop.png'),animations:'disabled'});
 await page.setViewportSize({width:390,height:844});await page.screenshot({path:path.join(output,'mobile.png'),animations:'disabled'});
 const size=await page.locator('#receiptIntakeReviewModal .modal-content').boundingBox();assert.ok(size.width<=390);
 await page.setViewportSize({width:1440,height:1000});
 // Invalid images are visible errors; removal persists, then a replacement can be approved.
 await page.locator('#riReviewPhotoFile').setInputFiles({name:'bad.png',mimeType:'image/png',buffer:Buffer.from('invalid')});
 await page.waitForFunction(()=>window.ReceiptIntakeReviewPhoto.blocked()&&document.getElementById('riReviewPhotoDrop').getAttribute('aria-busy')==='false');
 await page.locator('#riReviewPhotoRemove').click();await page.locator('#riReviewSaveDraft').click();
 await page.waitForFunction(()=>document.getElementById('riReviewSaveStatus').textContent==='Đã lưu nháp vào phiếu'&&!window.ReceiptIntake.isSaving());
 assert.equal(await page.locator('#riReviewPhotoPreview').isVisible(),false);
 await page.locator('#riReviewPhotoFile').setInputFiles({name:'replacement.png',mimeType:'image/png',buffer:photoBytes});
 await page.waitForFunction(()=>document.getElementById('riReviewPhotoStatus').textContent.includes('sẵn sàng'));
 // Unsaved edits cannot disappear on Close.
 await page.locator('#riReviewName').fill('Bánh kiểm tra giao diện đã sửa');await page.locator('#receiptIntakeReviewModal .btn-close').click();await page.locator('#riReviewDiscard').waitFor({state:'visible'});assert.equal(await page.locator('#receiptIntakeReviewModal').isVisible(),true);
 await page.locator('#riApproveNext').click();await page.waitForFunction(()=>document.getElementById('riReviewName').value==='Hàng mới dùng nhà cung cấp đã lưu'&&!window.ReceiptIntake.isSaving());
 await page.locator('#receiptIntakeReviewModal').waitFor({state:'visible'});
 await page.locator('#riReviewBarcode').fill('0009876543210');await page.locator('#riApprove').click();await page.waitForFunction(()=>document.getElementById('riReviewError').textContent.includes('đã thuộc'));
 assert.equal(await page.locator('#riReviewBarcode').inputValue(),'0009876543210');
 await page.locator('#riReviewBarcode').fill('');await page.locator('#riApprove').click();await page.locator('#receiptIntakeReviewModal').waitFor({state:'hidden'});await page.waitForFunction(()=>!window.ReceiptIntake.isSaving());
 assert.equal(await page.locator('[data-ri-review]').count(),0);assert.deepEqual(errors,[]);
 console.log('PASS: photo upload, preview, reload, invalid image, removal, replacement, approval, draft reload, supplier, leading-zero barcode, prices, unsaved warning, approve-next, duplicate-code rejection and responsive review UI.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
