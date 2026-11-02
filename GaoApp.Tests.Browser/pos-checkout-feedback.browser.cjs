'use strict';
// Current Razor/receipt owner functions. HTTP and print transport are synthetic; no real printer.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'..'),read=p=>fs.readFileSync(path.join(root,p),'utf8');
(async()=>{
 const view=read('GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml');
 const start=view.lastIndexOf('<div class="modal fade"',view.indexOf('id="invoiceIntentModal"'));
 const markup=view.slice(start,view.indexOf('<!--',start));
 const app=read('GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js');
 const declarations=app.slice(app.indexOf('let pendingInvoiceIntentOrderId = null;'),app.indexOf('const checkoutFeedback ='));
 const logic=app.slice(app.indexOf('function clearInvoiceIntentError()'),app.indexOf('        function nowIso()'));
 const bindings=app.slice(app.indexOf('function bindInvoiceIntentEvents()'),app.indexOf('function bindModuleEvents()'));
 const output=process.env.GAO_CHECKOUT_FEEDBACK_OUTPUT||path.join(root,'TestResults/pos-checkout-feedback');
 fs.mkdirSync(output,{recursive:true});
 const result={completed:false,checks:[],errors:[]},pass=t=>{result.checks.push(t);console.log('PASS '+t);};
 const browser=await chromium.launch({channel:'chrome',headless:true});let page;
 try{
  page=await browser.newPage({viewport:{width:1366,height:900},reducedMotion:'reduce'});
  page.setDefaultTimeout(15000);page.on('pageerror',e=>result.errors.push(e.message));
  await page.setContent('<html lang="vi"><body style="background:#eff1f7"><input id="barcode"><button id="nextCartButton" style="position:fixed;top:190px;left:50%">Giỏ mới</button>'+markup+'</body></html>');
  for(const file of ['vendor/css/core.css','Admin/css/pos/pos.checkout-feedback.css'])await page.addStyleTag({content:read('GaoApp.Web/wwwroot/'+file)});
  for(const file of ['vendor/js/bootstrap.js','Admin/js/pos/pos.checkout-feedback.js'])await page.addScriptTag({content:read('GaoApp.Web/wwwroot/'+file)});
  await page.addScriptTag({content:[
   "const invoiceIntentModalEl=document.getElementById('invoiceIntentModal');",
   "const invoiceIntentOrderText=document.getElementById('invoiceIntentOrderText');",
   "const invoiceIntentErrorBox=document.getElementById('invoiceIntentErrorBox');",
   "const btnInvoiceIntentAutomatic=document.getElementById('btnInvoiceIntentAutomatic');",
   "const invoiceIntentModal=new bootstrap.Modal(invoiceIntentModalEl,{backdrop:'static',keyboard:false});",
   "const posState={ui:{modals:{}},network:{busyScopes:new Set()}};",
   "const focusBarcodeInput=()=>document.getElementById('barcode').focus();",
   "const showError=message=>{throw new Error(message);};",
   declarations,
   "window.calls=[];window.prints=[];window.rejectRoute=false;window.slow=false;window.askPreference=false;window.openCount=0;window.blockPrint=false;",
   "const postJson=async(url,body)=>{calls.push({url,body});if(window.slow)await new Promise(r=>window.release=r);if(window.rejectRoute)throw new Error('Lỗi thử nghiệm');return {orderId:Number(url.split('/')[3]),askBeforePrintingReceipt:window.askPreference};};",
   "window.open=()=>{window.openCount++;return window.blockPrint?null:{closed:false,close(){this.closed=true;},location:{replace(){}}};};",
   "window.PosPrinting={postPaymentUrl:(url,id)=>{prints.push({url,orderId:id});return 'data:text/html,<title>Print suppressed</title>';}};",
   "const checkoutFeedback=PosCheckoutFeedback.create({modal:invoiceIntentModalEl,submit:submitInvoiceIntent,choosePrint:chooseReceiptPrint,isBusy:()=>invoiceIntentBusy});",
   logic,bindings,"bindInvoiceIntentEvents();",
   "window.showCheckout=(id,cash=true)=>openReceiptPrint(id,'80',true,true,cash?{received:200000,total:158000,change:42000,other:0}:null,window.askPreference);",
   "window.ownerIdle=()=>!invoiceIntentBusy;",
   "window.repeatInvoiceRoute=()=>submitInvoiceIntent(2);",
   "document.getElementById('nextCartButton').addEventListener('click',function(){this.textContent='Đơn mới';});"
  ].join('\n')});
  async function show(id,cash=true){await page.evaluate(({id,cash})=>showCheckout(id,cash),{id,cash});}
  async function idle(){await page.waitForFunction(()=>ownerIdle());}
  await page.evaluate(()=>{window.slow=true;});await show(123);
  await page.waitForFunction(()=>calls.length===1);await page.evaluate(()=>repeatInvoiceRoute());
  assert.equal(await page.evaluate(()=>calls.length),1);assert.equal(await page.evaluate(()=>calls[0].body.route),1);
  await page.evaluate(()=>{window.slow=false;release();});await idle();await page.locator('#posCashReminder').waitFor({state:'visible'});
  assert.match(await page.locator('#posCashReminderChange').innerText(),/42[.,]000/);
  assert.equal(await page.evaluate(()=>prints.length),1);assert.equal(await page.locator('.modal-backdrop').count(),0);
  pass('Current automatic invoice default and busy guard produce one print handoff and cash reminder');
  await page.locator('#nextCartButton').click();await page.locator('#barcode').fill('8936235440101');
  assert.equal(await page.locator('#nextCartButton').innerText(),'Đơn mới');
  assert.equal(await page.locator('#barcode').inputValue(),'8936235440101');
  assert.equal(await page.locator('#posCashReminder').evaluate(el=>getComputedStyle(el).pointerEvents),'none');
  assert.match(await page.locator('#posCashReminderOrder').innerText(),/123/);
  await page.screenshot({path:path.join(output,'cash-reminder-desktop.png')});
  await page.locator('#posCashReminder').waitFor({state:'hidden',timeout:6000});
  pass('Reminder leaves the next cart/barcode usable and keeps the completed order snapshot');
  await page.evaluate(()=>{window.rejectRoute=true;});await show(124);
  await page.locator('#invoiceIntentErrorBox').waitFor({state:'visible'});
  assert.match(await page.locator('#cashReceiptChange').innerText(),/42[.,]000/);
  const failedCalls=await page.evaluate(()=>calls.length);
  await page.setViewportSize({width:390,height:844});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  await page.screenshot({path:path.join(output,'cash-mobile-retry.png')});
  await page.evaluate(()=>{window.rejectRoute=false;});await page.locator('#btnInvoiceIntentAutomatic').click();
  await page.locator('#invoiceIntentModal').waitFor({state:'hidden'});await idle();
  assert.equal(await page.evaluate(()=>calls.length),failedCalls+1);assert.equal(await page.evaluate(()=>prints.length),2);
  assert.equal(await page.evaluate(()=>calls.at(-1).url),'/admin/pos/124/invoice-route');
  pass('Invoice failure keeps the original dialog and retries the same order on mobile');
  await page.evaluate(()=>{window.askPreference=true;});const beforeOpen=await page.evaluate(()=>openCount);
  await show(125);await page.locator('#receiptPrintChoices').waitFor({state:'visible'});await idle();
  await page.waitForFunction(()=>document.activeElement.id==='btnReceiptDoNotPrint');
  assert.equal(await page.evaluate(()=>openCount),beforeOpen);
  await page.screenshot({path:path.join(output,'receipt-choice-mobile.png')});
  await page.keyboard.press('K');await page.locator('#invoiceIntentModal').waitFor({state:'hidden'});
  assert.equal(await page.evaluate(()=>prints.length),2);
  pass('Customer print preference defaults to No print and opens no blank print tab');
  await page.evaluate(()=>{window.blockPrint=true;});await show(126);
  await page.locator('#receiptPrintChoices').waitFor({state:'visible'});await idle();
  await page.keyboard.press('C');await page.locator('#invoiceIntentErrorBox').waitFor({state:'visible'});
  assert.equal(await page.locator('#receiptPrintChoices').isVisible(),true);
  const beforeRetry=await page.evaluate(()=>calls.length);
  await page.evaluate(()=>{window.blockPrint=false;});await page.locator('#btnReceiptPrint').click();
  await page.locator('#invoiceIntentModal').waitFor({state:'hidden'});
  assert.equal(await page.evaluate(()=>prints.length),3);assert.equal(await page.evaluate(()=>calls.length),beforeRetry);
  pass('Blocked popup preserves print choice and retries without resubmitting invoice or payment');
  await show(126);await idle();
  assert.equal(await page.evaluate(()=>prints.length),3);assert.equal(await page.evaluate(()=>calls.length),beforeRetry);
  pass('Repeated completion of the same order is deduplicated by the original owner');
  await page.locator('#posCashReminder').waitFor({state:'hidden',timeout:6000});
  await page.evaluate(()=>{window.askPreference=false;});await show(127,false);await idle();
  assert.equal(await page.evaluate(()=>prints.length),4);assert.equal(await page.locator('#posCashReminder').isVisible(),false);
  assert.deepEqual(result.errors,[]);pass('Non-cash completion retains the default print flow without a cash reminder');
  result.completed=true;
 }finally{
  if(!result.completed&&page)await page.screenshot({path:path.join(output,'failure.png')}).catch(()=>{});
  fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(result,null,2));await browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
