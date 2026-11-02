'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let text='';process.stdin.on('data',v=>text+=v);process.stdin.on('end',()=>resolve(text));}));
 const browser=await chromium.launch({channel:'chrome',headless:true,args:['--host-resolver-rules=MAP *.phase6.test 127.0.0.1','--no-proxy-server']});
 const context=await browser.newContext({ignoreHTTPSErrors:true,viewport:{width:1440,height:900}}),page=await context.newPage();
 const errors=[],external=[],samples=[],menuChecks=[],assetFailures=[],pending=new Set(),persistent=new Set();let lastImportant=Date.now();page.on('pageerror',e=>errors.push(e.message));
 page.on('request',r=>{const u=new URL(r.url());if(/hub|signalr|presence|store-activity/i.test(u.pathname)||r.resourceType()==='eventsource'||(r.headers().accept||'').includes('text/event-stream')){persistent.add(u.pathname);return;}pending.add(r);lastImportant=Date.now();});
 const finished=r=>{if(pending.delete(r))lastImportant=Date.now();};page.on('requestfinished',finished);page.on('requestfailed',finished);
 page.on('request',r=>{if(!['data:','blob:'].some(p=>r.url().startsWith(p))&&new URL(r.url()).hostname!==new URL(info.baseUrl).hostname)external.push(r.url());});
 page.on('response',r=>{if(r.status()>=400&&['script','stylesheet','font'].includes(r.request().resourceType()))assetFailures.push({path:new URL(r.url()).pathname,status:r.status()});});
 async function settle(){await page.evaluate(async()=>{await document.fonts.ready;await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)));await Promise.allSettled(document.getAnimations().filter(a=>a.effect?.getComputedTiming().iterations!==Infinity).map(a=>a.finished));});}
 async function go(route){pending.clear();lastImportant=Date.now();const response=await page.goto(info.baseUrl+route);assert.equal(response.status(),200,route);
  assert.equal(new URL(page.url()).pathname.toLowerCase(),route.split('?')[0].toLowerCase(),'Unexpected navigation/authorization redirect');
  await settle();
  if(route==='/admin/pos/orders-page')await page.waitForFunction(()=>{const n=document.getElementById('metricCount');return n&&n.textContent!=='—';});
  if(route==='/Admin/Product')await page.waitForFunction(()=>document.getElementById('productTableWrapper')?.getAttribute('aria-busy')==='false');
  if(route==='/admin/reports/sales')await page.waitForFunction(()=>document.querySelector('[data-sales-detail-report]')?.getAttribute('aria-busy')==='false');
  if(['/admin/pos/v3','/admin/pos/legacy'].includes(route))await page.waitForFunction(()=>{const n=document.getElementById('currentDraftBody');return n&&!n.textContent.includes('Đang tải dữ liệu');});
  await settle();
 }
 try{
  await go('/admin/account/login');await page.locator('[name=UserName]').fill(info.user);await page.locator('[name=Password]').fill(info.password);
  await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
  await Promise.all([page.waitForNavigation(),page.locator('button[type=submit]').click()]);
  const cdp=await context.newCDPSession(page);await cdp.send('Network.enable');await cdp.send('Network.setCacheDisabled',{cacheDisabled:true});
  for(const width of [390,1440])for(const [label,route] of [['orders','/admin/pos/orders-page'],['products','/Admin/Product'],['sales','/admin/reports/sales']]){
   await page.setViewportSize({width,height:900});
   for(let repeat=0;repeat<4;repeat++){
    await go(route);
    const sample=await page.evaluate(()=>{const n=performance.getEntriesByType('navigation')[0],r=performance.getEntriesByType('resource');return{duration:n.loadEventEnd,dom:n.domContentLoadedEventEnd,taskReadyMs:performance.now(),htmlBytes:n.encodedBodySize,resources:r.length,resourceBytes:r.reduce((n,v)=>n+v.encodedBodySize,0),transferredBytes:r.reduce((n,v)=>n+v.transferSize,0),scripts:document.scripts.length,forms:document.forms.length,resourcePaths:r.map(v=>new URL(v.name).pathname)}});
    sample.outstandingRequestsAtTaskReady=[...pending].map(r=>new URL(r.url()).pathname);
    if(repeat>0)samples.push({label,width,repeat,...sample});
   }
  }
  if(info.phase==='candidate'){
   for(const entry of ['/admin/pos/v3','/admin/pos/legacy']){
    await page.setViewportSize({width:1440,height:900});await go(entry);
    await page.locator('[aria-label="Mở thêm chức năng POS"]').click();
    const menu=page.locator('[data-configured-pos-menu].show');await menu.waitFor();
    const labels=await menu.locator('a:visible,button:visible').allTextContents();
    assert.ok(labels.some(v=>v.includes('Màn hình khách')));assert.ok(labels.length<=6);
    assert.ok(!labels.some(v=>v.includes('UI24 không đưa')||v.includes('UI24 dashboard khác')));
    await page.screenshot({path:path.join(info.output,'pos-'+(entry.endsWith('v3')?'prime':'legacy')+'-desktop-menu.png')});
    const popupPromise=page.waitForEvent('popup');await menu.locator('[data-pos-native-customer-display]').click();const popup=await popupPromise;
    await popup.waitForLoadState('domcontentloaded');assert.equal(new URL(popup.url()).pathname,'/admin/pos/customer-display');
    await popup.locator('#customerDisplayApp').waitFor();await popup.close();
    menuChecks.push({entry,labels,popup:true});
    for(const width of [320,390,767,768,1024,1440]){await page.setViewportSize({width,height:900});await settle();assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),entry+' overflow '+width);}
   }
   await page.setViewportSize({width:390,height:844});await go('/admin/barcode-manager/'+info.conversionId);
   const csrfTarget=new URL(info.baseUrl+'/admin/api/barcodes/change'),csrfLocal=new URL(csrfTarget);csrfLocal.hostname='127.0.0.1';
   const cookies=(await context.cookies(info.baseUrl)).map(c=>c.name+'='+c.value).join('; ');
   const rejected=await context.request.post(csrfLocal.href,{headers:{host:csrfTarget.host,cookie:cookies,RequestVerificationToken:'ui24-deliberately-invalid'},data:{productUnitConversionId:info.conversionId,newBarcode:'UI24-MISSING-CSRF',setAsPrimary:true}});
   assert.equal(rejected.status(),400,'The native change endpoint must retain CSRF protection; bypass only the client helper that normally supplies a token');
   await page.waitForFunction(()=>document.getElementById('barcodeManagerList').textContent.includes('8934588063053'));
   for(const width of [320,390,767,768,1024,1440]){await page.setViewportSize({width,height:900});await settle();assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'barcode overflow '+width);}
   await page.setViewportSize({width:390,height:844});await page.locator('[data-bs-target="#barcodeChangeDialog"]').click();
   await page.locator('#barcodeManagerNew').fill('8934588063060');await page.locator('#barcodeManagerReason').fill('UI24 đổi mã trong fixture riêng');
   const node=await page.locator('#barcodeManagerNew').elementHandle();
   await page.setViewportSize({width:1440,height:900});await settle();assert.equal(await node.evaluate(e=>e===document.getElementById('barcodeManagerNew')),true);
   assert.equal(await page.locator('#barcodeManagerNew').inputValue(),'8934588063060');await page.setViewportSize({width:390,height:844});
   let writes=0;
   await page.route('**/admin/api/barcodes/change',async route=>{
    writes++;const target=new URL(route.request().url()),local=new URL(target);local.hostname='127.0.0.1';
    const actual=await route.fetch({url:local.href,headers:{...await route.request().allHeaders(),host:target.host}});
    assert.equal(actual.status(),200,await actual.text());await route.abort('connectionreset');
   });
   await page.locator('#barcodeManagerForm [type=submit]').click();await page.waitForFunction(()=>document.getElementById('barcodeManagerError').textContent.includes('Chưa xác định'));
   assert.equal(await page.locator('#barcodeManagerForm [type=submit]').isDisabled(),true);
   await page.evaluate(()=>document.getElementById('barcodeManagerForm').requestSubmit());assert.equal(writes,1);
   await page.screenshot({path:path.join(info.output,'barcode-uncertain-phone.png')});
   await page.locator('#barcodeChangeDialog .btn-close').click();await page.locator('#barcodeManagerReload').click();
   await page.waitForFunction(()=>document.getElementById('barcodeManagerList').textContent.includes('8934588063060'));
   await page.waitForFunction(()=>document.getElementById('barcodeManagerHistory').textContent.includes('UI24 đổi mã trong fixture riêng'));
   assert.match(await page.locator('#barcodeManagerHistory').innerText(),/8934588063053.*8934588063060/s,'Native paged history must display the committed change');
   assert.equal(writes,1);await page.screenshot({path:path.join(info.output,'barcode-updated-phone.png'),fullPage:true});
  }
  if(info.phase==='rollback'){
   await go('/admin/api/barcodes/conversion/'+info.conversionId+'/list');assert.match(await page.locator('body').innerText(),/8934588063060/);
  }
  assert.deepEqual(assetFailures,[]);assert.deepEqual(errors,[]);assert.deepEqual(external,[]);
  const result={passed:true,phase:info.phase,samples,menuChecks,pageErrors:errors,externalRequests:external,assetFailures,persistentEndpoints:[...persistent],readiness:'Actual task DOM: orders metric loaded, products aria-busy false, sales aria-busy false; background requests are recorded separately.',newIsolatedChrome:true,realProductionHttps:true,physicalDeviceTested:false};
  fs.writeFileSync(path.join(info.output,info.phase+'-browser.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({passed:true,phase:info.phase,samples:samples.length,menus:menuChecks.length}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
