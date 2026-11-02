'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto'),cp=require('node:child_process');
const {chromium}=require('playwright');
const {review,settle,evidence,save}=require('./printing-screens-mobile-review.cjs');
const receiptAssertions=require('./receipt-templates.browser.cjs');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',v=>s+=v);process.stdin.on('end',()=>resolve(s));}));
 fs.mkdirSync(info.output,{recursive:true});const pdfDir=path.join(info.output,'pdf');fs.mkdirSync(pdfDir,{recursive:true});
 const browser=await chromium.launch({channel:'chrome',headless:true,args:['--use-angle=swiftshader','--enable-unsafe-swiftshader']}),errors=[],external=[],pdfs=[];
 const context=await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce'});
 await context.addInitScript(()=>{window.__ui22PrintCalls=0;window.__ui22CloseCalls=0;window.print=()=>{window.__ui22PrintCalls++;};window.close=()=>{window.__ui22CloseCalls++;};});
 // A separate headless profile owns every test page. Hardware/payment endpoints are never submitted.
 await context.route('**/*',route=>{const u=new URL(route.request().url());if(u.origin!==new URL(info.baseUrl).origin&&!['data:','blob:'].includes(u.protocol)){external.push(u.origin+u.pathname);return route.abort();}return route.continue();});
 const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));
 const login=async account=>{await page.goto(info.baseUrl+'/admin/account/login');await page.locator('[name=UserName]').fill(account.user);await page.locator('[name=Password]').fill(account.password);await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('#loginForm button[type=submit]').click()]);};
 const go=async route=>{const response=await page.goto(info.baseUrl+route);assert.ok(response.ok(),route+' '+response.status());await settle(page);};
 const state=async(selector,label,options={})=>{await review(page,selector,label,info.output,options);};
 const assetSelector='link[href*="print-workspace.mobile.css"],link[href*="print-page.mobile.css"],link[href*="special-displays.mobile.css"]';
 const hash=s=>crypto.createHash('sha256').update(s).digest('hex');
 async function pdfPair(label,html,format){
  const snapshot=await context.newPage();const route=info.baseUrl+'/__ui22_print_snapshot_'+label;
  // Snapshot of native, already rendered DOM. Removing scripts prevents repeat mark-printed/API/print commands.
  await snapshot.route(route,r=>r.fulfill({contentType:'text/html; charset=utf-8',body:html.replace(/<script\b[^>]*>[\s\S]*?<\/script\s*>/gi,'')}));
  await snapshot.goto(route);await snapshot.emulateMedia({media:'print'});await settle(snapshot);
  const paper=await snapshot.evaluate(()=>{const node=document.querySelector('.receipt,.deposit-receipt,.delivery-sheet,main.paper,.debt-receipt')||document.body;const r=node.getBoundingClientRect();return{width:r.width,height:r.height,text:node.innerText,images:[...node.querySelectorAll('img,svg')].map(n=>n.outerHTML)};});
  const options={printBackground:true,displayHeaderFooter:false,margin:{top:0,bottom:0,left:0,right:0},preferCSSPageSize:true};
  const nativePageRule=html.match(/@page[^{}]*\{[^}]*?size\s*:\s*([^;}]+)/i)?.[1].trim()||null;
  const fixedNativePaper=/^\d+(?:\.\d+)?mm\s+\d+(?:\.\d+)?mm$/.test(nativePageRule||'');
  if(format.startsWith('roll')){
   if(!fixedNativePaper){options.width=format.slice(4)+'mm';options.height=Math.ceil(paper.height+4)+'px';options.preferCSSPageSize=false;}
  }else options.format=format;
  const files=[];
  for(const enabled of[false,true]){
   await snapshot.locator(assetSelector).evaluateAll((links,enabled)=>links.forEach(n=>n.disabled=!enabled),enabled);await settle(snapshot);
   const filename=path.join(pdfDir,label+(enabled?'-after':'-before')+'.pdf');
   const marker=path.resolve('.artifacts/ui-22/pdf-operation-started.json');
   if(!fs.existsSync(marker)){
    cp.execFileSync(process.execPath,['C:/Users/wind/.codex/plugins/cache/openai-primary-runtime/pdf/26.1026.10807/skills/pdf/container_tools/mark_artifact_operation_started.mjs','--operation-kind','create','--expected-output-count','24','--output-format','pdf'],{stdio:'pipe'});
    fs.writeFileSync(marker,JSON.stringify({operation:'create',expected:24,at:new Date().toISOString()})+'\n');
   }
   await snapshot.pdf({path:filename,...options});files.push(filename);
  }
  pdfs.push({label,format,nativePageRule,paperSizeSource:fixedNativePaper?'unchanged fixed native @page rule':format.startsWith('roll')?'explicit roll width/content height because Chrome cannot use native auto roll height':'native @page or explicit format fallback',nativeTextSha256:hash(paper.text),nativeImagesSha256:hash(JSON.stringify(paper.images)),nativeImageCount:paper.images.length,files,source:'native rendered DOM; scripts removed only to avoid repeat commands'});await snapshot.close();
  fs.writeFileSync(path.join(info.output,'pdf-sources.json'),JSON.stringify(pdfs,null,2)+'\n');
 }
 async function printCase(route,selector,label,format,ready){
  await go(route);if(ready)await page.waitForFunction(ready);await settle(page);
  if(label==='receipt')await page.frameLocator('#receiptPaper').locator('.receipt').waitFor();
  await state('body',label);
  await page.evaluate(()=>dispatchEvent(new Event('beforeprint')));await settle(page);
  assert.equal(await page.locator('.ui22-paper-viewport').count(),0,'Original paper owner restored before print');
  let html;
  if(label==='receipt'){
   const frame=await (await page.locator('#receiptPaper').elementHandle()).contentFrame();await receiptAssertions.assertBlackInk(frame);await receiptAssertions.assertMoneyFits(frame);html=await frame.content();
   const model=await page.locator('#receiptPrintData').textContent();const data=JSON.parse(model);const receipt=data.receipt;
   for(const size of['45','80','A6','A5','A4']){
    const rendered=await page.evaluate(({receipt,size})=>ReceiptTemplates.render(receipt,{layout:'classic',paperSize:size}),{receipt,size});
    await pdfPair('renderer-'+size,rendered.html,size.startsWith('A')?size:'roll'+size);
   }
  }else html=await page.content();
  await pdfPair(label,html,format);await page.evaluate(()=>dispatchEvent(new Event('afterprint')));await settle(page);
  assert.ok(await page.locator(selector).count(),label+' native paper remains');
 }
 try{
  await login(info.admin);
  await go('/admin/receipt-templates');await page.locator('.template-card').first().waitFor();assert.equal(await page.locator('.template-card').count(),16);
  await state('#receiptStudio','receipt-studio');
  await page.locator('[data-template-key="classic-A4"]').click();await page.frameLocator('#receiptPreview').locator('.receipt').waitFor();
  await page.waitForFunction(()=>getComputedStyle(document.getElementById('receiptPreview')).zoom==='1');await state('#receiptStudio','receipt-studio-A4-actual');
  await page.locator('.ui22-preview-tools [data-zoom-mode=fit]').click();assert.ok(await page.locator('#receiptPreview').evaluate(n=>Number(getComputedStyle(n).zoom))<1);await state('#receiptStudio','receipt-studio-A4-fit');
  await page.locator('.ui22-preview-tools [data-zoom-mode=actual]').click();await page.locator('#designName').fill('UI22 bản nháp chưa lưu');await page.locator('#designFooter').fill('Nội dung tiếng Việt chưa lưu · kiểm tra đổi khổ và xoay máy.');await state('#receiptStudio','receipt-studio-unsaved');
  await go('/admin/pos/printer-settings');await page.locator('#printMode').selectOption('helper');await state('#posPrinterSettings','printer-settings-helper');await page.locator('#printMode').selectOption('browser');await state('#posPrinterSettings','printer-settings-browser');
  await go('/admin/label-printing');await page.waitForFunction(()=>!document.querySelector('#quickFields').disabled);await state('#labelPrinting','label-quick');
  await page.locator('#quickSearch').fill(info.kioskBarcode);await page.locator('#quickSearchForm button').click();await page.locator('#quickResults button').first().waitFor();await state('#labelPrinting','label-search-results');
  await page.locator('#quickResults button').first().click();await page.locator('#quickLines tr').first().waitFor();await state('#labelPrinting','label-quick-lines');
  await page.locator('[data-tab=tasks]').click();await state('#labelPrinting','label-receipt-tasks');await page.locator('[data-tab=history]').click();await state('#labelPrinting','label-print-history');
  await go('/admin/label-printing-settings');await page.waitForFunction(()=>Number(document.querySelector('#templateFields [name=widthMm]').value)>0);await page.locator('#labelPreview:not([hidden])').waitFor();await state('#labelPrinting','label-template-studio');
  await page.locator('#templateFields [name=name]').fill('UI22 tem chưa lưu');await page.waitForResponse(r=>r.url().includes('/admin/label-printing/preview')&&r.request().method()==='POST');await page.locator('#labelPreview').evaluate(n=>n.decode());await state('#labelPrinting','label-template-preview');
  await page.locator('.ui22-preview-tools [data-zoom-mode=fit]').click();await state('#labelPrinting','label-template-fit');
  await page.locator('[data-tab=printers]').click();await page.locator('#printerForm [name=name]').fill('UI22 máy in chưa lưu');await state('#labelPrinting','label-server-printer-draft');
  await printCase('/admin/pos/orders/'+info.orderId+'/print?autoPrint=false','#receiptPaper','receipt','roll80');
  await printCase('/admin/customer-deposit/'+info.depositId+'/print','.deposit-receipt','deposit','roll80');
  await printCase('/admin/customer-debt/receipts/'+info.debtReceiptId,'.debt-receipt','debt','A4');
  await printCase(info.deliveryUrl,'.delivery-sheet','delivery','A5');
  await printCase('/admin/purchase-orders/'+info.purchaseId+'/print?autoPrint=false','main.paper','purchase-order','A4');
  await printCase('/admin/pos-shift/closing-slip-print?id='+info.closingId,'.receipt','closing-slip','roll80',()=>document.querySelector('#barcodeText')?.textContent==='UI22-CLOSE-80');
  await printCase('/admin/pos-shift/handover-slip-print?id='+info.handoverId,'.receipt','handover-slip','roll80',()=>document.querySelector('#barcodeText')?.textContent==='UI22-HANDOVER-80');
  await go('/admin/pos/customer-display');await page.waitForFunction(()=>document.querySelector('.cd-status')?.textContent.includes('Sẵn sàng'));await state('#customerDisplayApp','customer-display-idle',{dynamic:true});
  await context.clearCookies();await login(info.monitor);
  await go('/admin/store-monitor');await page.locator('#wm3-canvas').waitFor();await state('#gao-store-wall','store-monitor-3d',{dynamic:true});await page.locator('#wm-view').click();await state('#gao-store-wall','store-monitor-2d',{dynamic:true});await page.locator('#wm-theme').click();await state('#gao-store-wall','store-monitor-dark',{dynamic:true});
  await context.clearCookies();await login(info.admin);
  await go('/kiosk');await page.locator('#g-key').waitFor();await state('#gao-kiosk','kiosk-activation',{dynamic:true});await page.locator('#g-key').fill(info.kioskKey);await Promise.all([page.waitForNavigation(),page.locator('[data-action=activate]').click()]);
  await page.locator('[data-action=menu]').first().waitFor();await state('#gao-kiosk','kiosk-idle',{dynamic:true});await page.locator('[data-action=menu]').first().click();await page.locator('[data-action=product]').waitFor();await state('#gao-kiosk','kiosk-menu',{dynamic:true});await page.locator('[data-action=product]').click();await page.locator('#g-query').fill('nuoc suoi');await page.locator('[data-action^="choose-"]').first().waitFor();await state('#gao-kiosk','kiosk-product-search',{dynamic:true});await page.locator('[data-action^="choose-"]').first().click();await state('#gao-kiosk','kiosk-product-details',{dynamic:true});
  await page.locator('[data-action=menu]').click();await page.locator('[data-action=shop]').click();await page.locator('#g-query').fill(info.kioskBarcode);await page.locator('#g-query').press('Enter');await page.locator('[data-action^="setqty-"]').first().waitFor();await state('#gao-kiosk','kiosk-cart',{dynamic:true});
  await page.locator('[data-action^="setqty-"]').first().click();await page.locator('#g-qty').fill('3');await state('#gao-kiosk','kiosk-quantity-draft',{dynamic:true,dialog:true,nativeDialog:'#g-dialog'});await page.locator('#g-dialog [data-ui22-dialog-close]').click();
  await page.locator('[data-action=checkout]').click();await state('#gao-kiosk','kiosk-checkout-confirmation',{dynamic:true,dialog:true,nativeDialog:'#g-dialog'});await page.locator('#g-dialog [data-action=close-dialog]').first().click();
  await context.setOffline(true);await page.waitForFunction(()=>!document.querySelector('#g-network').hidden);await state('#gao-kiosk','kiosk-network-disconnected',{dynamic:true});await context.setOffline(false);await page.waitForFunction(()=>document.querySelector('#g-network').hidden,null,{timeout:30000});
  await context.clearCookies();await login(info.reader);await go('/admin/pos/printer-settings');await page.locator('#applyTemplate').waitFor();await settle(page);assert.equal(await page.locator('#saveStoreIdentity,#designFields,#designName').count(),0);await state('#posPrinterSettings','printer-settings-employee');
  assert.deepEqual(errors,[]);assert.deepEqual(external,[]);save(info.output);
  fs.writeFileSync(path.join(info.output,'printing-screens-result.json'),JSON.stringify({passed:true,nativeStateCount:evidence.cases.length,phoneChecks:evidence.phoneChecks.length,desktopProofs:evidence.desktopProofs.length,printProofs:evidence.printProofs.length,pdfPairs:pdfs.length,pageErrors:errors,externalRequests:external,hardwarePrintJobs:0,bankRequests:0},null,2)+'\n');
  console.log('PASS UI-22 native phone/desktop/print matrix and dedicated screens.');
 }catch(error){await page.screenshot({path:path.join(info.output,'native-failure.png'),fullPage:true}).catch(()=>{});save(info.output);throw error;}
 finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
