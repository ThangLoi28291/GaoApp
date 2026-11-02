const {chromium}=require('playwright');
const assert=require('node:assert/strict'),path=require('node:path'),fs=require('node:fs');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let value='';process.stdin.on('data',x=>value+=x);process.stdin.on('end',()=>resolve(value));}));
 const url=info.baseUrl+'/admin/warehouse-receiving/'+info.receiptId;
 let browser,context,page;const errors=[];
 const phase=value=>fs.writeFileSync(path.join(info.output,'progress.txt'),value);
 async function open(storageState){
  browser=await chromium.launch({channel:'chrome',headless:true});context=await browser.newContext({viewport:{width:390,height:844},storageState});
  page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));page.on('dialog',d=>d.accept());
 }
 async function login(){
  await page.locator('[name=UserName]').fill(info.user);await page.locator('[name=Password]').fill(info.password);
  if(await page.locator('select[name=SelectedTerminalId]').count())await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminal));
  await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('#loginForm button[type=submit]').click()]);
 }
 async function ready(){await page.waitForFunction(()=>window.ReceivingDraftRecovery&&window.WarehouseReceivingApp&&window.ReceiptIntake);await page.locator('#wrdLinesContainer').waitFor();}
 async function idle(){await page.waitForFunction(()=>!window.receivingIsActivelySaving()&&!window.ReceiptIntake.isActivelySaving());}
 async function add(index){
  await page.evaluate(choice=>window.openQtyPopup(choice),info.choices[index]);await page.locator('#quickAddProductModal.show').waitFor();
  await page.locator('#popupQuickQty').fill('1');await page.locator('#btnPopupQuickAddLine').click();
  await page.locator('#quickAddProductModal').waitFor({state:'hidden'});await idle();
  await page.waitForFunction(()=>!window.ReceivingDraftRecovery.hasPending());
 }
 async function recover(){
  await page.locator('#receivingRecovery button').filter({hasText:'Khôi phục phần chưa lưu'}).click();
  await page.waitForFunction(()=>!window.ReceivingDraftRecovery.isRecovering());await idle();
 }
 try{
  await open();await page.goto(info.baseUrl+'/admin/account/login?returnUrl='+encodeURIComponent('/admin/warehouse-receiving/'+info.receiptId));await login();await ready();
  const auth=(await context.cookies()).find(x=>x.name==='.AspNetCore.Cookies');assert.ok(auth&&auth.expires>Date.now()/1000,'Login survives browser process closure within its existing lifetime');
  for(let i=0;i<4;i++)await add(i);phase('First four items saved');
  const endpoint='**/admin/api/stock-documents/'+info.receiptId+'/intake/known';let lostId;
  await page.route(endpoint,async route=>{lostId=route.request().postDataJSON().commandId;const response=await route.fetch();assert.equal(response.status(),200);await route.abort('failed');});
  await page.evaluate(choice=>window.openQtyPopup(choice),info.choices[4]);await page.locator('#btnPopupQuickAddLine').click();await idle();
  await page.waitForFunction(()=>window.ReceivingDraftRecovery.hasPending());
  const saved=await context.storageState();saved.cookies=saved.cookies.filter(x=>x.expires>0);await browser.close();
  await open(saved);await page.goto(url);await ready();assert.ok(!page.url().includes('/login'),'No login needed after browser restart');
  assert.equal(await page.locator('#wrdLinesContainer tr[data-line-id]').count(),5);
  await page.screenshot({path:path.join(info.output,'phone-pending.png')});
  const retries=[];page.on('request',request=>{if(request.url().endsWith('/intake/known'))retries.push(request.postDataJSON().commandId);});
  await recover();await page.waitForFunction(()=>!window.ReceivingDraftRecovery.hasPending());
  phase('Browser restart and lost acknowledgement recovered');
  assert.ok(!retries.includes(lostId),'Already committed addition is verified without being added again');
  for(let i=5;i<10;i++)await add(i);
  assert.equal(await page.locator('#wrdLinesContainer tr[data-line-id]').count(),10);
  phase('Ten items saved');
  // Lose authentication while an addition is in progress; the journal remains through login.
  await context.clearCookies({name:'.AspNetCore.Cookies'});
  await page.evaluate(choice=>window.openQtyPopup(choice),info.choices[0]);await page.locator('#popupQuickQty').fill('3');await page.locator('#btnPopupQuickAddLine').click();await idle();
  await page.locator('#quickAddProductModal [data-receiving-login]').waitFor({state:'visible'});await page.locator('#quickAddProductModal [data-receiving-login]').click();await login();await ready();
  await recover();await page.waitForFunction(()=>!window.ReceivingDraftRecovery.hasPending());
  phase('Expired session reauthenticated and addition recovered');
  const lastRow=page.locator('#wrdLinesContainer tr[data-line-id]').filter({hasText:info.choices[9].productName});
  await lastRow.locator('[data-wra-edit]').click();await page.locator('#wraQuantity').fill('7');
  await page.reload();await ready();await recover();await page.waitForFunction(()=>!window.ReceivingDraftRecovery.hasPending());
  assert.equal(await lastRow.locator('.js-receiving-qty').inputValue(),'7');
  phase('Unsent phone quantity recovered');
  await page.evaluate(()=>window.ReceiptIntake.open('',{tab:'new'}));await page.locator('#receiptIntakeModal.show').waitFor();
  await page.locator('#riNewName').fill('Món mới chưa bấm lưu');await page.locator('#riBaseUnit').selectOption(String(info.unitId));await page.locator('#riReceiveUnit').selectOption(String(info.unitId));
  await page.locator('#riFactor').fill('1');await page.locator('#riQuantity').fill('2');
  await page.locator('#receiptIntakeModal .wrc-evidence>summary').click();await page.locator('#riNote').fill('Giữ nội dung khi khóa điện thoại');
  await page.evaluate(()=>{window.ReceiptIntakePhoto.restore('data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aJ1cAAAAASUVORK5CYII=');window.ReceivingDraftRecovery.captureForms();});
  await page.reload();await ready();await recover();await page.locator('#receiptIntakeModal.show').waitFor();
  assert.equal(await page.locator('#riNewName').inputValue(),'Món mới chưa bấm lưu');assert.equal(await page.locator('#riQuantity').inputValue(),'2');
  assert.equal(await page.locator('#riNote').inputValue(),'Giữ nội dung khi khóa điện thoại');assert.ok(await page.locator('#riPhotoPreview').isVisible());
  assert.equal(await page.locator('#riApproveNow').isChecked(),false);
  await page.waitForFunction(()=>getComputedStyle(document.querySelector('#receiptIntakeModal')).opacity==='1');
  await page.locator('#riNote').scrollIntoViewIfNeeded();
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  await page.screenshot({path:path.join(info.output,'phone-restored.png')});
  await page.locator('#receiptIntakeModal .btn-close').click();await page.locator('#receiptIntakeModal').waitFor({state:'hidden'});
  assert.deepEqual(errors,[]);console.log('Ten items preserved; persistent login; lost acknowledgement; reauthentication; unsent quantity, product form and photo recovery passed.');
 }catch(error){if(page&&!page.isClosed())await page.screenshot({path:path.join(info.output,'failure.png'),fullPage:true}).catch(()=>{});throw error;}
 finally{await browser?.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
