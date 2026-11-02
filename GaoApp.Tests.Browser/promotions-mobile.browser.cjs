'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const{chromium}=require('playwright'),mobile=require('./promotions-mobile-review.cjs');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let input='';process.stdin.on('data',v=>input+=v);process.stdin.on('end',()=>resolve(input));}));
 assert.ok(info.baseUrl&&info.manager&&info.reader&&info.ids&&info.output,'Owned SQL fixture required');fs.mkdirSync(info.output,{recursive:true});
 const origin=new URL(info.baseUrl).origin,browser=await chromium.launch({channel:'chrome',headless:true}),errors=[],writes=[],external=[],nativeResponses=[],validationProofs=[],contexts=[];
 const previews=new Set(['/admin/label-printing/preview','/admin/promotion-labels/preview-a4']);
 const read=request=>['GET','HEAD','OPTIONS'].includes(request.method());
 async function login(account,role){
  const context=await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce',serviceWorkers:'block'});contexts.push(context);
  await context.addInitScript(()=>{window.print=()=>{throw Error('Unexpected physical browser printing');};window.open=()=>null;});
  await context.route('**/*',route=>{const url=new URL(route.request().url());if(url.origin===origin||['blob:','data:','about:'].includes(url.protocol))return route.continue();external.push({role,path:url.origin+url.pathname});return route.abort('blockedbyclient');});
  const page=await context.newPage();page.setDefaultTimeout(30000);page.on('pageerror',error=>errors.push({role,message:error.message}));
  page.on('dialog',async dialog=>{errors.push({role,message:'Unexpected browser dialog '+dialog.type()+': '+dialog.message()});await dialog.dismiss();});
  page.on('response',response=>{const url=new URL(response.url());if(url.origin===origin&&/\/admin\/(?:promotion|displaypromotion|promotion-labels|label-printing)/i.test(url.pathname)&&response.request().resourceType()!=='document'&&['fetch','xhr'].includes(response.request().resourceType()))nativeResponses.push({role,path:url.pathname,query:Object.fromEntries(url.searchParams),method:response.request().method(),status:response.status(),contentType:response.headers()['content-type']});});
  await page.goto(info.baseUrl+'/admin/account/login?returnUrl=%2Fadmin%2Fpromotion');await page.locator('[name=UserName]').fill(account.user);await page.locator('[name=Password]').fill(account.password);
  if(await page.locator('[name=SelectedTerminalId]').count())await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
  await Promise.all([page.waitForURL(url=>!url.pathname.toLowerCase().endsWith('/login')),page.locator('#loginForm button[type=submit]').click()]);
  await context.route('**/*',route=>{const request=route.request(),url=new URL(request.url());if(url.origin!==origin)return route.fallback();if(read(request)||/^\/admin\/api\/store-activity\/(online|presence)$/.test(url.pathname)||(request.method()==='POST'&&previews.has(url.pathname)))return route.fallback();writes.push({role,method:request.method(),path:url.pathname});return route.abort('blockedbyclient');});
  return page;
 }
 async function inspect(page,selector,label,options){await mobile.review(page,selector,label,info.output,options);}
 async function closeModal(page,selector){await page.locator(selector+' .btn-close').click();await page.locator(selector).waitFor({state:'hidden'});await mobile.settle(page);}
 async function promotionEdit(page,key){
  const row=page.locator('.promo-table tbody tr').filter({has:page.locator('[data-promotion-edit="'+info.ids[key]+'"]')});await row.locator('[data-bs-toggle="dropdown"]').click();await row.locator('[data-promotion-edit]').click();await page.locator('#promotionEditModal').waitFor({state:'visible'});await page.waitForFunction(id=>document.querySelector('#promoId').value===String(id),info.ids[key]);await mobile.settle(page);
  if(['combo','mixed'].includes(key))await page.waitForFunction(id=>[...document.querySelectorAll('#promoRuleBody [data-field="unitSelect"]')].every(select=>[...select.options].some(option=>option.value===String(id))),info.conversionId);
 }
 async function previewChange(page,action){const previous=await page.locator('#plPreview').getAttribute('src');const[response]=await Promise.all([page.waitForResponse(response=>previews.has(new URL(response.url()).pathname)&&response.status()===200),action()]);await response.finished();await page.waitForFunction(previous=>{const image=document.querySelector('#plPreview');return!image.hidden&&image.getAttribute('src')!==previous&&image.complete&&image.naturalWidth>0;},previous);await mobile.settle(page);}
 try{
  const page=await login(info.manager,'manager');await page.goto(info.baseUrl+'/admin/promotion');await page.locator('.promo-table tbody tr').first().waitFor();
  const listText=await page.locator('#promotionTableWrapper').innerText();assert.ok(!listText.includes('UI19_FOREIGN_PRIVATE'));for(const status of['Đang chạy','Hết hạn','Sắp chạy','Đã khóa'])assert.ok(listText.includes(status),'Native promotion status '+status);
  assert.equal(await page.locator('.promo-phone-filter-toggle').getAttribute('aria-expanded'),'false','Phone opens with compact filters');
  await inspect(page,'.promo-shell','promotion-list');
  await page.locator('.promo-phone-filter-toggle').click();assert.equal(await page.locator('.promo-phone-filter-toggle').getAttribute('aria-expanded'),'true');await inspect(page,'.promo-shell','promotion-filters-expanded');
  for(const[key,value]of[['current','1'],['combo','2'],['gift','3']]){
   await Promise.all([page.waitForResponse(response=>new URL(response.url()).pathname.toLowerCase()==='/admin/promotion/search'),page.locator('#ddlType').selectOption(value)]);await page.waitForFunction(()=>!document.querySelector('#promotionTableWrapper').classList.contains('opacity-50'));await inspect(page,'.promo-shell','promotion-filter-'+key);
  }
  await page.goto(info.baseUrl+'/admin/promotion?search=UI19_NO_SUCH_CAMPAIGN');await page.locator('.promo-empty').waitFor();await inspect(page,'.promo-shell','promotion-empty');
  await page.goto(info.baseUrl+'/admin/promotion');
  for(const key of['current','combo','mixed','gift','expired','inactive']){
   await promotionEdit(page,key);assert.equal(await page.locator('#promoId').inputValue(),String(info.ids[key]));
   if(key==='mixed'){assert.equal(await page.locator('#promoComboQuantity').inputValue(),'6');assert.equal(await page.locator('#promoComboFixedPrice').inputValue(),'180000');}
   if(key==='gift'){assert.equal(await page.locator('#promoBuyQuantity').inputValue(),'10');assert.equal(await page.locator('#promoGetQuantity').inputValue(),'1');}
   await inspect(page,'.promo-shell,#promotionEditModal','promotion-edit-'+key,{dialog:true});await closeModal(page,'#promotionEditModal');
  }
  await page.locator('#btnOpenCreatePromotion').click();await page.locator('#promotionEditModal').waitFor({state:'visible'});
  await page.locator('#promoName').fill('Bản nháp chưa lưu · điều kiện và thời gian được giữ nguyên qua các kích thước');await page.locator('#promoDescription').fill('Không lưu bản nháp này.');await page.locator('#promoDiscountValue').fill('999');
  await page.locator('#btnSavePromotion').click();await page.locator('.swal2-popup').waitFor({state:'visible'});assert.ok((await page.locator('.swal2-html-container').innerText()).includes('ít nhất 1 sản phẩm'));assert.deepEqual(writes,[],'Native invalid draft must be rejected before any business POST');await mobile.settle(page);await page.screenshot({path:path.join(info.output,'promotion-validation-error.png'),animations:'disabled'});validationProofs.push({case:'promotion-create-missing-product',nativeError:await page.locator('.swal2-html-container').innerText(),businessPosts:0});await page.locator('.swal2-confirm').click();await page.locator('.swal2-popup').waitFor({state:'hidden'});await mobile.settle(page);
  await inspect(page,'.promo-shell,#promotionEditModal','promotion-create-invalid-draft',{dialog:true});await closeModal(page,'#promotionEditModal');
  await page.goto(info.baseUrl+'/admin/displaypromotion');await page.locator('.dp-table tbody tr').first().waitFor();assert.ok(!(await page.locator('.display-promotion-page').innerText()).includes('UI19_FOREIGN_PRIVATE'));await inspect(page,'.display-promotion-page','display-list');
  for(const key of['text','image','hidden','expired']){
   await page.locator('[data-csp-action="promotion-edit"][data-csp-id="'+info.ids['display-'+key]+'"]').click();await page.locator('#promotionModal').waitFor({state:'visible'});assert.equal(await page.locator('#promotionId').inputValue(),String(info.ids['display-'+key]));
   await inspect(page,'.display-promotion-page,#promotionModal','display-edit-'+key,{dialog:true});await closeModal(page,'#promotionModal');
  }
  await page.locator('[data-csp-action="promotion-create"]').first().click();await page.locator('#promotionModal').waitFor({state:'visible'});await page.locator('#title').fill('Lời chào chưa lưu · điện thoại không làm mất nội dung');await page.locator('#description').fill('Bản nháp cục bộ giữ giá trị và quyền sở hữu form gốc.');
  await inspect(page,'.display-promotion-page,#promotionModal','display-create-draft',{dialog:true});await closeModal(page,'#promotionModal');
  await page.goto(info.baseUrl+'/admin/promotion-labels?promotionId='+info.ids.current);await page.locator('[data-add="'+info.conversionId+'"]').waitFor();await page.locator('#plPreview').waitFor({state:'visible'});
  assert.equal(await page.locator('#plCampaign').inputValue(),String(info.ids.current));await inspect(page,'#promotionLabels','labels-products');
  await previewChange(page,()=>page.locator('[data-add="'+info.conversionId+'"]').click());await page.locator('.pl-quantity').fill('3');await page.locator('.pl-quantity').dispatchEvent('input');
  assert.ok((await page.locator('#plLines').innerText()).includes('34.000'));assert.ok((await page.locator('#plLines').innerText()).includes('Mua từ 2'));
  assert.equal(await page.locator('#plPrint').isDisabled(),true,'Disconnected physical printer remains unavailable');await inspect(page,'#promotionLabels','labels-selected');
  for(const[preset,label]of[['80x50','large'],['a4','a4']]){await previewChange(page,()=>page.locator('#plPaperPreset').selectOption(preset));await inspect(page,'#promotionLabels','labels-preview-'+label);}
  assert.ok((await page.locator('#plPaperSize').innerText()).includes('A4'));assert.ok(await page.locator('#plPreview').evaluate(image=>image.naturalWidth>0),'Actual native PNG preview decoded');
  const[emptyResponse]=await Promise.all([page.waitForResponse(response=>{const url=new URL(response.url());return url.pathname==='/admin/promotion-labels/products'&&url.searchParams.get('q')==='UI19_NO_SUCH_PRODUCT';}),page.locator('#plSearch').fill('UI19_NO_SUCH_PRODUCT')]);await emptyResponse.finished();await page.waitForFunction(()=>document.querySelectorAll('#plProducts [data-add]').length===0);await mobile.settle(page);await inspect(page,'#promotionLabels','labels-empty-search');
  const reader=await login(info.reader,'reader');await reader.goto(info.baseUrl+'/admin/promotion');await reader.locator('.promo-table').waitFor();await inspect(reader,'.promo-shell','promotion-view-only');
  // Original view-only markup exposes edit/create; controllers continue enforcing all Manage commands.
  await promotionEdit(reader,'current');await inspect(reader,'.promo-shell,#promotionEditModal','promotion-view-only-native-draft',{dialog:true});await closeModal(reader,'#promotionEditModal');
  await reader.goto(info.baseUrl+'/admin/displaypromotion');assert.equal(await reader.locator('#guestWifiName').evaluate(input=>input.readOnly),true);assert.ok(await reader.locator('.dp-edit').first().isDisabled());assert.equal(await reader.locator('[data-csp-action="promotion-create"]').count(),0);await inspect(reader,'.display-promotion-page','display-view-only');
  assert.deepEqual(writes,[]);assert.deepEqual(errors,[]);assert.deepEqual(external,[],'Never contacts physical client, printer, provider or another app');
  const result={passed:true,mode:'native_sql_acceptance',mandatoryMatrixExecuted:true,nativeStateCount:mobile.evidence.cases.length,
   phoneChecks:mobile.evidence.phoneChecks.length,desktopProofs:mobile.evidence.desktopProofs.length,printProofs:mobile.evidence.printProofs.length,dialogChecks:mobile.evidence.dialogChecks.length,dialogScrollChecks:mobile.evidence.dialogScrollChecks.length,
   unexpectedBusinessWrites:writes,pageErrors:errors,externalRequests:external,nativeResponses,validationProofs,
   nativePhysicalPrinterCommands:0,renderingOnlyPreviewPostsAllowed:[...previews],browser:'Fresh owned headless Chrome; no existing user process or tab enumerated, connected or closed'};
  fs.writeFileSync(path.join(info.output,'promotions-mobile-result.json'),JSON.stringify(result,null,2)+'\n');console.log(JSON.stringify({passed:true,nativeStates:result.nativeStateCount,phoneChecks:result.phoneChecks,desktopProofs:result.desktopProofs,printProofs:result.printProofs,dialogChecks:result.dialogChecks}));
 }finally{mobile.save(info.output);for(const context of contexts)await context.close();await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
