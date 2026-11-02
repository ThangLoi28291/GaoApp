'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',x=>s+=x);process.stdin.on('end',()=>resolve(s));}));
 fs.mkdirSync(info.output,{recursive:true});
 const result={completed:false,checks:[],viewports:[],desktopProofs:[],errors:[],csp:[],requests:[],bankEvidenceCallbacksSynthetic:true};
 const pass=t=>{result.checks.push(t);console.log('PASS '+t);};
 const browser=await chromium.launch({channel:'chrome',headless:true});let page;
 async function login(account){
  const context=await browser.newContext({viewport:{width:390,height:844},hasTouch:true,serviceWorkers:'block'});
  await context.addInitScript(()=>{window.print=()=>{};});
  await context.route('**/*',async route=>{
   const u=new URL(route.request().url());
   if((u.hostname==='127.0.0.1'||u.hostname==='localhost')&&u.port!==new URL(account.baseUrl).port)
    return route.fulfill({status:503,body:'Physical printing suppressed in test'});
   if(!['localhost','127.0.0.1'].includes(u.hostname)&&!u.hostname.endsWith('.localhost'))return route.abort();
   return route.continue();
  });
  const tab=await context.newPage();tab.setDefaultTimeout(20000);
  tab.on('pageerror',e=>result.errors.push(e.message));
  tab.on('console',m=>{if(/violates.*Content Security Policy|Refused to (?:load|execute|apply)/i.test(m.text()))result.csp.push(m.text());});
  await tab.goto(account.baseUrl+'/admin/account/login');await tab.locator('[name=UserName]').fill(account.user);await tab.locator('[name=Password]').fill(account.password);
  await tab.locator('[name=SelectedTerminalId]').selectOption(String(account.terminalId));
  await Promise.all([tab.waitForURL(u=>!u.pathname.endsWith('/login')),tab.locator('#loginForm button[type=submit]').click()]);
  return {context,tab};
 }
 const state=()=>page.evaluate(()=>posApp.state.business.currentDraft);
 async function settled(){await page.waitForFunction(()=>document.getAnimations().every(a=>a.playState!=='running'||a.effect?.getTiming().iterations===Infinity));await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));}
 async function idle(){await page.waitForFunction(()=>!posApp.state.network.busyScopes.size);await settled();}
 async function ready(route='/admin/pos'){
  await page.goto(info.manager.baseUrl+route);await page.waitForFunction(()=>window.posApp?.state.business.currentDraft&&window.PosOffline?.status().ready);await page.evaluate(()=>document.fonts.ready);await idle();
  assert.equal((await state()).orderId,info.orderId);
 }
 async function payment(){await page.locator('#btnOpenPayment').click();await page.locator('#paymentModal.show').waitFor();await settled();await page.locator('#btnReopenLatestPaymentQr:not([hidden])').waitFor();}
 async function qr(){await page.locator('#btnReopenLatestPaymentQr').click();await page.locator('#paymentQrModal.show').waitFor();await settled();}
 async function closeQr(){await page.locator('#paymentQrModal .btn-close').click();await page.locator('#paymentQrModal').waitFor({state:'hidden'});await settled();}
 async function closePayment(){await page.locator(page.viewportSize().width<768?'#paymentModal .btn-close':'#paymentModal .pos-payment-footer__actions [data-bs-dismiss=modal]').click();await page.locator('#paymentModal').waitFor({state:'hidden'});await settled();}
 async function geometry(id,width,height,route){
  await page.setViewportSize({width,height});await settled();await page.waitForFunction(id=>Math.abs(document.getElementById(id).getBoundingClientRect().height-innerHeight)<2,id);
  const g=await page.locator('#'+id).evaluate(el=>{
   const rect=n=>{const b=n.getBoundingClientRect();return {x:b.x,y:b.y,right:b.right,bottom:b.bottom,width:b.width,height:b.height};};
   const f=el.querySelector('.pos-transfer-mobile-footer'),b=el.querySelector('.modal-body');
   const targets=[...el.querySelectorAll('.pos-transfer-mobile-footer button, .modal-header button')].filter(n=>n.getClientRects().length&&getComputedStyle(n).visibility!=='hidden').map(n=>({id:n.id,...rect(n)}));
   return {document:document.documentElement.scrollWidth,modal:rect(el.querySelector('.modal-content')),body:rect(b),footer:rect(f),targets};
  });
  assert.ok(g.document<=width+1&&Math.abs(g.modal.x)<1&&Math.abs(g.modal.right-width)<1&&g.modal.bottom<=height+1,JSON.stringify(g));
  assert.ok(g.body.height>=44&&g.body.bottom<=g.footer.y+1&&g.footer.bottom<=height+1,JSON.stringify(g));
  for(const t of g.targets)assert.ok(t.width>=44&&t.height>=44&&t.x>=0&&t.right<=width+1,JSON.stringify(t));
  result.viewports.push({id,width,height,route,...g});
 }
 async function visibleViewport(id,route){
  await page.evaluate(()=>{window.__transferRealViewport=window.visualViewport;Object.defineProperty(window,'visualViewport',{configurable:true,value:{height:360,width:390,offsetTop:0,offsetLeft:0,scale:1}});__transferRealViewport.dispatchEvent(new Event('resize'));});
  await page.waitForFunction(id=>Math.abs(document.getElementById(id).getBoundingClientRect().height-360)<2,id);
  const box=await page.locator('#'+id).evaluate(el=>{const f=el.querySelector('.pos-transfer-mobile-footer').getBoundingClientRect(),b=el.querySelector('.modal-body').getBoundingClientRect();return {footerBottom:f.bottom,bodyHeight:b.height};});
  assert.ok(box.footerBottom<=361&&box.bodyHeight>=44,JSON.stringify(box));result.viewportResizeSimulations??=[];result.viewportResizeSimulations.push({id,route,...box});
  await page.evaluate(()=>{Object.defineProperty(window,'visualViewport',{configurable:true,value:__transferRealViewport});__transferRealViewport.dispatchEvent(new Event('resize'));});await settled();
 }
 async function desktop(route,width){
  await page.setViewportSize({width,height:1000});await ready(route);await payment();await qr();
  const proof=await page.evaluate(()=>{
   const snap=()=>[...document.querySelectorAll('#paymentModal, #paymentModal *, #paymentQrModal, #paymentQrModal *, #bankTransferReviewModal, #bankTransferReviewModal *')].map(el=>{const s=getComputedStyle(el),b=el.getBoundingClientRect();return {id:el.id,tag:el.tagName,css:[...s].map(p=>[p,s.getPropertyValue(p)]),box:[b.x,b.y,b.width,b.height]};});
   const link=document.querySelector('link[href*="pos.transfer.mobile.css"]'),before=snap();link.disabled=true;const after=snap();link.disabled=false;
   return {count:before.length,equal:JSON.stringify(before)===JSON.stringify(after)};
  });assert.ok(proof.equal,JSON.stringify(proof));assert.equal(await page.locator('.pos-transfer-mobile-footer').count(),0);result.desktopProofs.push({route,width,...proof});
  if(width===1440)await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-desktop.png':'prime-desktop.png')});
  await closeQr();await closePayment();
 }
 async function bankReview(route){
  // Real review owner and markup, deterministic presentation callbacks only.
  // The standalone bank-review and recovery probes separately exercise its HTTP owner.
  await page.evaluate(()=>{
   window.__reviewCalls=0;window.__reviewRejects=0;window.__reviewFail=true;
   window.__reviewItem={receiptId:9001,waitId:9002,qrId:9003,orderId:123456,amount:170000,
    senderName:'NGUYEN THI KHACH CHUYEN KHOAN',transactionCode:'GD:6288NAMAP2M4EBGL-LONG-REFERENCE-1234567890',postedAtUtc:'2026-10-26T02:36:01Z',
    content:'Cửa hàng nhận tiền thành công\n+170.000 VND từ NGUYEN THI KHACH CHUYEN KHOAN tại ACB\n'+('Nội dung đối chiếu bằng chứng chuyển khoản. '.repeat(35))};
   window.__showReview=()=>PosBankTransferReview.show(__reviewItem,{
    confirm:async()=>{__reviewCalls++;if(__reviewFail)throw new Error('Chưa kết nối được máy chủ. Kiểm tra rồi thử lại.');await PosBankTransferReview.close(false);},
    reject:async()=>{__reviewRejects++;await PosBankTransferReview.close(false);}
   });return __showReview();
  });await page.locator('#bankTransferReviewModal.show').waitFor();await settled();
  assert.match(await page.locator('#bankReviewAmount').innerText(),/170.000/);assert.match(await page.locator('#bankReviewSender').innerText(),/NGUYEN THI/);
  await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-bank-review-summary.png':'phone-bank-review-summary.png')});
  await page.locator('.pos-bank-review__source summary').tap();
  for(const [w,h] of [[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]])await geometry('bankTransferReviewModal',w,h,route);
  await page.setViewportSize({width:390,height:844});await settled();
  await visibleViewport('bankTransferReviewModal',route);
  await page.locator('#bankReviewContent').scrollIntoViewIfNeeded();
  assert.ok(await page.locator('#btnBankReviewConfirm').isVisible());
  await page.locator('#btnBankReviewConfirm').tap();await page.locator('#bankReviewError:not([hidden])').waitFor();
  await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-bank-review-error.png':'phone-bank-review-evidence.png')});
  assert.equal(await page.locator('#bankTransferReviewModal .pos-transfer-mobile-footer #bankReviewError').count(),1);
  await page.evaluate(()=>{__reviewFail=false;});await page.locator('#btnBankReviewConfirm').tap();await page.locator('#bankTransferReviewModal').waitFor({state:'hidden'});
  assert.equal(await page.evaluate(()=>__reviewCalls),2);assert.equal((await state()).paidTotal,0);
  await page.evaluate(()=>__showReview());await settled();await page.locator('#btnBankReviewReject').tap();await page.locator('#bankTransferReviewModal').waitFor({state:'hidden'});
  assert.equal(await page.evaluate(()=>__reviewRejects),1);assert.equal((await state()).paidTotal,0);
  await page.setViewportSize({width:1440,height:1000});await settled();
  assert.equal(await page.locator('#bankTransferReviewModal .modal-body > .pos-bank-review__actions').count(),1);
  assert.equal(await page.locator('#bankTransferReviewModal .modal-body > #bankReviewError').count(),1);
 }
 try{
  page=(await login(info.manager)).tab;await ready();
  for(const [route,width] of [['/admin/pos',1440],['/admin/pos',768],['/admin/pos',1024],['/admin/pos/legacy',1440],['/admin/pos/legacy',768]])await desktop(route,width);
  pass('Five computed-style/box comparisons retain all QR/history/review desktop and tablet markup styles at 768/1024/1440');
  let originalImage;
  for(const route of ['/admin/pos','/admin/pos/legacy']){
   await page.setViewportSize({width:390,height:844});await ready(route);await payment();await qr();
   await page.evaluate(()=>{window.__qrConfirm=document.getElementById('btnConfirmPaymentQrPaid');window.__paymentError=document.getElementById('paymentInlineErrorBox');});
   assert.match(await page.locator('#paymentQrAmount').innerText(),/31.000/);assert.match(await page.locator('#paymentQrRequestCode').innerText(),/QR/);
   const src=await page.locator('#paymentQrImage').getAttribute('src');assert.ok(src.startsWith('data:image/'));if(originalImage)assert.equal(src,originalImage);else originalImage=src;
   for(const [w,h] of [[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]])await geometry('paymentQrModal',w,h,route);
   await page.setViewportSize({width:390,height:844});await settled();await page.locator('#paymentQrModal .modal-body').evaluate(el=>{el.scrollTop=0;});
   await visibleViewport('paymentQrModal',route);
   await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-phone-qr.png':'phone-qr.png')});
   await page.locator('#paymentQrAccountName').scrollIntoViewIfNeeded();assert.match(await page.locator('#paymentQrAccountName').innerText(),/CUA HANG THU NGHIEM/);
   await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-phone-qr-details.png':'phone-qr-details.png')});
   await closeQr();assert.equal((await state()).paidTotal,0);assert.equal(await page.locator('#paymentModal #paymentInlineErrorBox').count(),1);await qr();assert.equal(await page.locator('#paymentQrImage').getAttribute('src'),src);
   await page.setViewportSize({width:1440,height:1000});await settled();assert.equal(await page.locator('.pos-transfer-mobile-footer').count(),0);
   assert.equal(await page.evaluate(()=>document.getElementById('btnConfirmPaymentQrPaid')===__qrConfirm&&document.getElementById('paymentInlineErrorBox')===__paymentError),true);
   assert.equal(await page.locator('#paymentQrModal .modal-body > .d-grid #btnConfirmPaymentQrPaid').count(),1);
   await closeQr();await closePayment();await page.setViewportSize({width:390,height:844});await settled();await bankReview(route);
  }
  pass('Prime and legacy: 28 phone QR/review viewport checks, original nodes restored on desktop; QR amount/image/account remain identical across close/reopen');
  pass('Long bank sender/reference/evidence scroll without covering fixed actions; synthetic review error/retry/reject retain the original owner and never collect');
  await page.setViewportSize({width:390,height:844});await ready('/admin/pos/v3');await payment();await qr();assert.equal(await page.locator('#paymentQrImage').getAttribute('src'),originalImage);await closeQr();await closePayment();
  await page.reload();await page.waitForFunction(()=>window.posApp?.state.business.currentDraft&&PosOffline.status().ready);await payment();await qr();assert.equal(await page.locator('#paymentQrImage').getAttribute('src'),originalImage);
  assert.equal((await state()).paidTotal,0);await closeQr();
  await page.locator('[data-pay-method-value="1"]').click();await page.locator('#payAmount').fill('50000');await page.locator('#btnCreatePaymentQr').click();
  await page.locator('#paymentQrModal.show').waitFor();await settled();await page.locator('#paymentQrPendingWarning:not([hidden])').waitFor();
  assert.match(await page.locator('#paymentQrAmount').innerText(),/31.000/);assert.equal(await page.locator('#paymentQrImage').getAttribute('src'),originalImage);assert.equal((await state()).paidTotal,0);
  pass('Reload and repeated creation restore the saved 31,000 QR, never replace it with the entered 50,000 or mark it paid');
  await page.locator('#btnManualPaymentQr:not([hidden])').waitFor();await page.locator('#btnManualPaymentQr').tap();await page.locator('#btnConfirmPaymentQrPaid:visible').waitFor();await idle();
  let failOnce=true,release,started;
  await page.route('**/admin/pos/payment-qr/*/manual-confirm',async route=>{
   result.requests.push({url:route.request().url(),body:route.request().postDataJSON()});
   if(failOnce){failOnce=false;const response=await route.fetch();assert.equal(response.status(),200);await route.fulfill({status:409,contentType:'application/json',body:'{"message":"Chưa nhận được kết quả. Kiểm tra rồi thử lại cùng QR."}'});return;}
   started?.();await new Promise(resolve=>{release=resolve;});await route.continue();
  });
  await page.locator('#btnConfirmPaymentQrPaid').tap();await page.locator('#paymentQrModal #paymentInlineErrorBox:visible').filter({hasText:'Chưa nhận được kết quả'}).waitFor();await idle();
  assert.equal(await page.locator('[id="paymentInlineErrorBox"]').count(),1);
  await page.screenshot({path:path.join(info.output,'phone-qr-uncertain-confirmation.png')});assert.equal(await page.locator('#paymentQrModal .pos-transfer-mobile-footer #paymentInlineErrorBox').count(),1);
  const reached=new Promise(resolve=>{started=resolve;});await page.locator('#btnConfirmPaymentQrPaid').tap();await reached;
  await page.waitForFunction(()=>document.getElementById('btnConfirmPaymentQrPaid').disabled);await page.keyboard.press('F9');await page.keyboard.press('F9');assert.equal(result.requests.length,2);release();
  await page.locator('#paymentQrModal').waitFor({state:'hidden'});await page.waitForFunction(()=>posApp.state.business.currentDraft.paidTotal===31000);await idle();
  assert.equal((await state()).payments.length,1);assert.equal((await state()).balanceDue,100000);
  assert.equal(await page.locator('[id="paymentInlineErrorBox"]').count(),1);assert.equal(await page.locator('#paymentInlineErrorBox').isVisible(),false);
  pass('Actual SQL commit followed by retryable 409 exposes the original error in the QR footer; retry uses the same QR and repeated F9 cannot collect twice');
  await page.locator('#payAmount').fill('20000');await page.locator('#btnCreatePaymentQr').tap();await page.locator('#paymentQrModal.show').waitFor();await settled();
  await page.locator('#btnCancelPaymentQr').tap();await page.locator('#paymentQrModal').waitFor({state:'hidden'});await idle();assert.equal((await state()).paidTotal,31000);
  await page.locator('#btnRefreshPaymentQrHistory').tap();await page.locator('#paymentQrHistory details').evaluate(el=>{el.open=true;});
  await page.locator('.pos-qr-history__row').filter({has:page.locator('[data-status="Cancelled"]')}).locator('button').tap();await page.locator('#paymentQrModal.show').waitFor();await settled();
  assert.equal(await page.locator('#btnConfirmPaymentQrPaid').isVisible(),false);assert.equal(await page.locator('#btnCancelPaymentQr').isVisible(),false);assert.equal(await page.locator('#btnCheckAcbPayment').isVisible(),false);
  assert.equal(await page.locator('[id="paymentInlineErrorBox"]').count(),1);assert.equal(await page.locator('#paymentInlineErrorBox').isVisible(),false);
  await page.screenshot({path:path.join(info.output,'phone-cancelled-readonly.png')});await closeQr();
  await page.locator('#payAmount').fill('100000');await page.locator('#btnCreatePaymentQr').tap();await page.locator('#paymentQrModal.show').waitFor();await settled();
  assert.match(await page.locator('#paymentQrAmount').innerText(),/100.000/);assert.equal(await page.locator('#btnManualPaymentQr').isVisible(),false);assert.equal(await page.locator('#btnConfirmPaymentQrPaid').isVisible(),false);assert.equal((await state()).paidTotal,31000);
  await closeQr();await closePayment();pass('Cancel changes no money; a saved cancelled QR is read-only; a new QR cannot grant manual confirmation before server timing');
  const denied=await login(info.denied);const forbidden=await denied.tab.request.get(info.denied.baseUrl+'/admin/pos',{maxRedirects:0});assert.ok([302,403].includes(forbidden.status()));await denied.context.close();
  const other=await login(info.other);const foreign=await other.tab.request.get(info.other.baseUrl+`/admin/acb/payments/orders/${info.orderId}/qrs/${info.qrId}`,{maxRedirects:0});
  const foreignText=await foreign.text();assert.ok([400,403,404,500].includes(foreign.status()),foreign.status()+' '+foreignText.slice(0,300));
  assert.doesNotMatch(foreignText,/"qrDataUrl"|"accountNumber"|"grandTotal"|CUA HANG THU NGHIEM/);result.foreignStoreRejection={status:foreign.status(),qrAndAccountDataNotDisclosed:true};await other.context.close();
  pass('Server POS denial and foreign-store saved QR access remain enforced');
  assert.deepEqual(result.errors,[]);assert.deepEqual(result.csp,[]);result.completed=true;
 }finally{
  await page?.screenshot({path:path.join(info.output,'last-state.png')}).catch(()=>{});fs.writeFileSync(path.join(info.output,'result.json'),JSON.stringify(result,null,2));await browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
