'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright'),{addNoncedScript}=require('./browser-contract.cjs');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',x=>s+=x);process.stdin.on('end',()=>resolve(s));}));
 fs.mkdirSync(info.output,{recursive:true});
 const result={completed:false,checks:[],viewports:[],errors:[],csp:[],mutations:[]},pass=text=>{result.checks.push(text);console.log('PASS '+text);};
 const browser=await chromium.launch({channel:'chrome',headless:true});
 // Network fault injection must reach Playwright instead of a service worker's own cache/fetch.
 // The separate original 16-group POS regression runs with the real service worker enabled.
 const context=await browser.newContext({viewport:{width:390,height:844},hasTouch:true,serviceWorkers:'block'});
 result.serviceWorkerBlockedForNetworkFaultInjection=true;
 await context.addInitScript(()=>{
  window.testMediaCode='';window.testMediaError=null;window.testMediaDeferred=false;window.testStreams=[];window.testMediaCalls=0;window.testTorch=false;window.testTorchCapable=true;
  navigator.mediaDevices.getUserMedia=async options=>{
   window.testMediaCalls++;window.testMediaOptions=options;
   if(window.testMediaError)throw new DOMException('Synthetic camera',window.testMediaError);
   const canvas=document.createElement('canvas');canvas.width=1024;canvas.height=600;
   const ctx=canvas.getContext('2d');ctx.fillStyle='white';ctx.fillRect(0,0,1024,600);
   if(window.testMediaCode){const code=document.createElement('canvas');JsBarcode(code,window.testMediaCode,{format:'CODE128',width:3,height:150,fontSize:24,margin:30});ctx.drawImage(code,(1024-code.width)/2,(600-code.height)/2);}
   const stream=canvas.captureStream(12);window.testStreams.push(stream);
   const track=stream.getVideoTracks()[0];track.getCapabilities=()=>({torch:window.testTorchCapable});
   track.applyConstraints=async value=>{if(window.testTorchError)throw Error('Synthetic torch');window.testTorch=value.advanced[0].torch;};
   if(window.testMediaDeferred)await new Promise(resolve=>window.resolveTestMedia=resolve);
   return stream;
  };
 });
 const page=await context.newPage();page.setDefaultTimeout(20000);
 page.on('pageerror',e=>result.errors.push(e.message));
 page.on('console',m=>{if(/violates.*Content Security Policy|Refused to (?:load|execute|apply)/i.test(m.text()))result.csp.push(m.text());});
 await page.route('**/uploads/products/scan*',r=>r.fulfill({status:204,body:''}));
 await page.route('**/admin/pos/cart/current/scan',async route=>{result.mutations.push(route.request().postDataJSON());await route.continue();});
 const modal=page.locator('#posScanCameraModal'),button=page.locator('#posScanCameraOpen'),status=page.locator('#posScanCameraStatus');
 const draft=()=>page.evaluate(()=>posApp.state.business.currentDraft);
 const ended=()=>page.evaluate(()=>testStreams.every(s=>s.getTracks().every(t=>t.readyState==='ended')));
 async function idle(){await page.waitForFunction(()=>!posApp.state.network.busyScopes.size);}
 async function ready(route='/admin/pos'){
  await page.goto(info.manager.baseUrl+route);await page.waitForFunction(()=>window.posApp?.state.business.currentDraft&&window.PosOffline?.status().ready);
  await addNoncedScript(page,{url:info.manager.baseUrl+'/lib/jsbarcode/JsBarcode.all.min.js'});
  assert.equal((await draft()).orderId,info.orderId);await idle();
 }
 async function setup(code='',error=null,deferred=false){await page.evaluate(({code,error,deferred})=>{testMediaCode=code;testMediaError=error;testMediaDeferred=deferred;},{code,error,deferred});}
 async function open(){await button.tap();await page.locator('#posScanCameraModal.show').waitFor();}
 async function close(expectStopped=true){
  const hidden=await modal.evaluateHandle(el=>{const signal={done:false};el.addEventListener('hidden.bs.modal',()=>signal.done=true,{once:true});return signal;});
  await modal.locator('[data-bs-dismiss=modal]').tap();await page.waitForFunction(s=>s.done,hidden);await hidden.dispose();
  assert.equal(await page.locator('#posScanCameraVideo').evaluate(el=>el.srcObject),null);
  if(expectStopped)assert.ok(await ended());
 }
 async function quantity(id,unit,expected){await page.waitForFunction(({id,unit,expected})=>posApp.state.business.currentDraft.lines.filter(x=>x.variantId===id&&(!unit||x.sellingUnitName===unit)).reduce((s,x)=>s+x.quantity,0)===expected,{id,unit,expected});await idle();}
 async function cameraScan(code,id,unit,expected){
  const count=result.mutations.length;await setup(code);await open();await quantity(id,unit,expected);await modal.waitFor({state:'hidden'});
  assert.ok(await ended());assert.equal(result.mutations.length,count+1);await page.waitForTimeout(650);assert.equal(result.mutations.length,count+1,'Repeated video frames cannot add twice');
 }
 try{
  await page.goto(info.manager.baseUrl+'/admin/account/login');await page.locator('[name=UserName]').fill(info.manager.user);await page.locator('[name=Password]').fill(info.manager.password);
  if(await page.locator('[name=SelectedTerminalId]').count())await page.locator('[name=SelectedTerminalId]').selectOption(String(info.manager.terminalId));
  await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('#loginForm button[type=submit]').click()]);
  await ready();assert.equal(await page.evaluate(()=>testMediaCalls),0);assert.ok(await page.evaluate(()=>isSecureContext));
  pass('No camera permission or stream until user taps the phone scan button');
  await cameraScan('2099900000001',info.variantId,'Lon',1);
  assert.deepEqual(result.mutations.at(-1),{barcode:'2099900000001',quantity:1});
  assert.equal(await page.evaluate(()=>testMediaOptions.video.facingMode.ideal),'environment');assert.equal(await page.evaluate(()=>testMediaOptions.audio),false);
  assert.equal(await page.locator('#posScanName').innerText(),'Bia Ken bạc 330 ml');
  pass('Real local ZXing decodes CODE128 video, rear camera requested, stream stopped, original POS scan acknowledged once');
  await cameraScan('2099900000001',info.variantId,'Lon',2);
  pass('Explicitly reopening camera permits the same barcode once again without replaying old frames');
  await page.locator('#posCockpitCommandHints [data-pos-command-prefix="4+"]').tap();
  const badge=await page.locator('#posQtyPrefixBadge').boundingBox(),trigger=await button.boundingBox();assert.ok(badge.x+badge.width<=trigger.x,'Quantity badge must not cover camera control');
  await page.screenshot({path:path.join(info.output,'phone-390-camera-entry-quantity.png')});
  await cameraScan('2099900000002',info.variantId,'Lốc',4);
  assert.deepEqual(result.mutations.at(-1),{barcode:'2099900000002',quantity:4});
  pass('Camera uses original quantity badge and exact pack barcode: four packs, not four base units');
  const count=result.mutations.length,history=await page.locator('#posScanHistoryList li').count();await setup('2099900099999');await open();await page.locator('#posScanGuard[open]').waitFor();
  assert.equal(await modal.isVisible(),false);assert.ok(await ended());assert.equal(result.mutations.length,count+1);
  assert.equal(await page.locator('#posScanHistoryList li').count(),history);
  await page.keyboard.press('F2');await page.keyboard.press('F3');assert.equal(await page.locator('.modal.show').count(),0);
  await page.locator('#posScanGuardConfirm').click();await page.locator('#posScanGuard[open]').waitFor({state:'hidden'});
  pass('Unknown barcode closes camera before the original native guard; no item or history fabricated');
  await page.locator('#txtBarcode').fill('bia');
  for(const error of ['NotAllowedError','NotFoundError','NotReadableError']){
   await setup('',error);const before=await page.locator('#txtBarcode').inputValue();await open();await status.locator('xpath=self::*[@data-error="true"]').waitFor();
   assert.equal(await page.locator('#posScanCameraRetry').isEnabled(),true);assert.ok(await ended());await close();assert.equal(await page.locator('#txtBarcode').inputValue(),before);
  }
  pass('Denied permission, missing camera and busy camera show recovery text and leave typed input/cart intact');
  await setup();await open();await page.locator('#posScanCameraTorch:not([hidden])').waitFor();
  await page.locator('#posScanCameraTorch').tap();assert.equal(await page.evaluate(()=>testTorch),true);assert.equal(await page.locator('#posScanCameraTorch').getAttribute('aria-pressed'),'true');
  await page.evaluate(()=>testTorchError=true);await page.locator('#posScanCameraTorch').tap();await page.waitForFunction(()=>document.querySelector('#posScanCameraStatus').dataset.error==='true');
  await page.evaluate(()=>testTorchError=false);await close();
  await page.evaluate(()=>testTorchCapable=false);await setup();await open();await page.waitForFunction(()=>!!document.querySelector('#posScanCameraVideo').srcObject&&!document.querySelector('#posScanCameraRetry').disabled);
  assert.equal(await page.locator('#posScanCameraTorch').isVisible(),false);await close();await page.evaluate(()=>testTorchCapable=true);
  pass('Torch is capability gated, toggles track constraint and reports unsupported torch without losing the cart');
  await setup();await open();await page.locator('#posScanCameraTorch:not([hidden])').waitFor();
  const noMutation=result.mutations.length;
  for(const key of ['F2','F3','F4','F6','F8','Control+Delete','Enter'])await page.keyboard.press(key);
  assert.equal(await page.locator('.modal.show').count(),1);assert.equal(await modal.isVisible(),true);assert.equal(result.mutations.length,noMutation);
  await page.keyboard.press('Escape');await modal.waitFor({state:'hidden'});assert.ok(await ended());
  pass('Camera owns POS shortcuts during capture; Escape closes and stops capture instead of opening a business action');
  await setup('2099900000001',null,true);await open();await page.waitForFunction(()=>!!window.resolveTestMedia);
  await close(false);await page.evaluate(()=>resolveTestMedia());await page.waitForFunction(()=>testStreams.every(s=>s.getTracks().every(t=>t.readyState==='ended')));
  assert.equal(result.mutations.length,noMutation);
  pass('Closing while permission/getUserMedia is pending stops late tracks and never adds a stale code');
  await setup();await open();await page.locator('#posScanCameraTorch:not([hidden])').waitFor();
  await page.evaluate(()=>{Object.defineProperty(document,'hidden',{configurable:true,value:true});document.dispatchEvent(new Event('visibilitychange'));});
  assert.ok(await ended());const calls=await page.evaluate(()=>testMediaCalls);
  await page.evaluate(()=>{delete document.hidden;document.dispatchEvent(new Event('visibilitychange'));});
  assert.equal(await page.evaluate(()=>testMediaCalls),calls);await close();
  pass('Background transition stops media and returning to the page does not reopen the camera automatically');
  await setup();await open();await page.locator('#posScanCameraTorch:not([hidden])').waitFor();
  for(const [width,height] of [[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]]){
   await page.setViewportSize({width,height});
   const g=await modal.evaluate(el=>{const b=el.querySelector('.modal-content').getBoundingClientRect(),body=el.querySelector('.modal-body');return {right:b.right,width:innerWidth,height:b.height,viewport:innerHeight,bodyScroll:body.scrollHeight>=body.clientHeight};});
   assert.ok(g.right<=width+1&&g.height<=height+1,JSON.stringify(g));result.viewports.push({width,height,...g});
  }
  await page.screenshot({path:path.join(info.output,'phone-390-440-camera.png')});
  await page.setViewportSize({width:768,height:1024});await modal.waitFor({state:'hidden'});assert.ok(await ended());assert.equal(await button.isVisible(),false);
  for(const width of [1024,1440]){await page.setViewportSize({width,height:1000});assert.equal(await button.isVisible(),false);assert.equal(await page.locator('[data-pos-camera]').count(),0);}
  pass('Camera modal fits seven phone viewports; resizing to tablet/desktop stops capture and hides the added control');
  await page.setViewportSize({width:390,height:844});
  await page.reload();await page.waitForFunction(()=>!!window.posApp?.state.business.currentDraft);await addNoncedScript(page,{url:info.manager.baseUrl+'/lib/jsbarcode/JsBarcode.all.min.js'});
  // Fresh page/decoder namespace with a deliberate local script network failure.
  result.decoderBeforeFault=await page.evaluate(()=>({present:!!window.ZXingBrowser,url:document.querySelector('#posScanCameraModal').dataset.decoderUrl,scripts:Array.from(document.scripts).filter(s=>s.src.includes('zxing')).map(s=>s.src)}));
  assert.equal(result.decoderBeforeFault.present,false,JSON.stringify(result.decoderBeforeFault));
  const decoderFault=url=>url.pathname.endsWith('/lib/zxing-browser/0.1.5/zxing-browser.min.js');
  await page.route(decoderFault,r=>{result.decoderFaultIntercepted=true;return r.abort();});await setup();await open();await page.waitForFunction(()=>document.querySelector('#posScanCameraStatus').textContent.includes('Không tải được bộ quét'));
  assert.ok(await ended());await page.unroute(decoderFault);await page.locator('#posScanCameraRetry').tap();await page.locator('#posScanCameraTorch:not([hidden])').waitFor();await close();
  pass('Decoder load failure stops media; Quét lại reloads the same local CSP-nonced decoder successfully');
  await context.setOffline(true);await page.waitForFunction(()=>!navigator.onLine);
  const offlineAllowed=await page.evaluate(()=>PosCommon.isPosOnline(posApp.state));
  assert.equal(await button.isDisabled(),!offlineAllowed,'Follow original prepared-session offline policy');
  const offlineCalls=await page.evaluate(()=>testMediaCalls);
  if(offlineAllowed){await setup();await open();await page.locator('#posScanCameraTorch:not([hidden])').waitFor();await close();assert.equal(await page.evaluate(()=>testMediaCalls),offlineCalls+1);}
  assert.equal(await page.evaluate(()=>PosOffline.status().pending),0,'No camera-specific journal entries when no code was received');
  await context.setOffline(false);await page.waitForFunction(()=>navigator.onLine&&!document.querySelector('#posScanCameraOpen').disabled);
  pass('Offline camera entry follows the original prepared-session policy without creating a separate queue');
  await setup('2099900000001',null,true);await open();await page.waitForFunction(()=>!!window.resolveTestMedia);
  await page.evaluate(()=>{window.testOriginalOrder=posApp.state.business.currentDraft.orderId;posApp.state.business.currentDraft.orderId+=100000;resolveTestMedia();});
  await page.waitForFunction(()=>Array.from(document.querySelectorAll('#toast-container .toast-message')).some(el=>el.textContent.includes('Phiên giỏ đã thay đổi')));
  await page.evaluate(()=>{posApp.state.business.currentDraft.orderId=testOriginalOrder;});assert.equal(result.mutations.length,noMutation);
  pass('A code from an old camera session cannot be handed to a different current draft');
  await ready('/admin/pos/legacy');await cameraScan('2099900000003',info.waterId,null,3);
  await page.screenshot({path:path.join(info.output,'legacy-phone-after-camera.png')});
  pass('Legacy POS camera uses the same original exact scan and retains the seeded draft');
  await ready('/admin/pos/v3');assert.equal(await button.isVisible(),true);assert.equal(await page.evaluate(()=>testMediaCalls),0);
  pass('Prime v3 reload preserves cart and requires a new user action to reopen camera');
  assert.deepEqual(result.errors,[]);assert.deepEqual(result.csp,[]);result.completed=true;
 }finally{
  if(!result.completed)await page.screenshot({path:path.join(info.output,'failure.png'),fullPage:true}).catch(()=>{});
  fs.writeFileSync(path.join(info.output,'result.json'),JSON.stringify(result,null,2));await browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
