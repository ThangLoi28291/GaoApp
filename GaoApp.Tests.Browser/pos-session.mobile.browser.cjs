'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',x=>s+=x);process.stdin.on('end',()=>resolve(s));}));
 fs.mkdirSync(info.output,{recursive:true});
 const result={completed:false,checks:[],viewports:[],desktopProofs:[],errors:[],csp:[],deliveryRequests:[],printingSuppressed:true};
 const pass=t=>{result.checks.push(t);console.log('PASS '+t);};
 const browser=await chromium.launch({channel:'chrome',headless:true});let page,context;
 async function makeContext(blockWorkers=false){
  const c=await browser.newContext({viewport:{width:390,height:844},hasTouch:true,serviceWorkers:blockWorkers?'block':'allow',acceptDownloads:true});
  await c.addInitScript(()=>{window.print=()=>{};});
  await c.route('**/*',async route=>{
   const u=new URL(route.request().url());
   if((u.hostname==='127.0.0.1'||u.hostname==='localhost')&&u.port!==new URL(info.manager.baseUrl).port)return route.fulfill({status:503,body:'Physical printing suppressed in test'});
   if(!['localhost','127.0.0.1'].includes(u.hostname)&&!u.hostname.endsWith('.localhost'))return route.abort();
   return route.continue();
  });return c;
 }
 function observe(p){p.setDefaultTimeout(20000);p.on('pageerror',e=>result.errors.push(e.message));p.on('console',m=>{if(/violates.*Content Security Policy|Refused to (?:load|execute|apply)/i.test(m.text()))result.csp.push(m.text());});}
 async function login(p,account=info.manager){
  await p.goto(account.baseUrl+'/admin/account/login');await p.locator('[name=UserName]').fill(account.user);await p.locator('[name=Password]').fill(account.password);if(await p.locator('[name=SelectedTerminalId]').count())await p.locator('[name=SelectedTerminalId]').selectOption(String(account.terminalId));
  await Promise.all([p.waitForURL(u=>!u.pathname.endsWith('/login')),p.locator('#loginForm button[type=submit]').click()]);
 }
 async function idle(){await page.waitForFunction(()=>!posApp.state.network.busyScopes.size);await settled();}
 async function settled(){await page.waitForFunction(()=>document.getAnimations().every(a=>a.playState!=='running'||a.effect?.getTiming().iterations===Infinity));await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));}
 async function ready(route='/admin/pos'){
  await page.goto(info.manager.baseUrl+route);await page.waitForFunction(()=>window.posApp?.state.business.currentDraft&&window.PosOffline?.canWork());await page.evaluate(()=>document.fonts.ready);await idle();
 }
 const draft=()=>page.evaluate(()=>posApp.state.business.currentDraft);
 async function api(url,method='GET',body){return page.evaluate(async({url,method,body})=>{const r=await fetch(url,{method,headers:{'Content-Type':'application/json',RequestVerificationToken:PosCommon.getAntiForgeryToken()},body:body===undefined?undefined:JSON.stringify(body)});const data=await r.json();if(!r.ok)throw Error(r.status+' '+JSON.stringify(data));return data;},{url,method,body});}
 async function journal(p=page){return p.evaluate(async()=>{const db=await PosOfflinePrivacy.open();try{return await new Promise((resolve,reject)=>{const r=db.transaction('sessions').objectStore('sessions').getAll();r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);});}finally{db.close();}});}
 async function openDelivery(){await page.locator('#btnOpenPayment').click();await page.locator('#paymentModal.show').waitFor();await settled();await page.locator('#btnOpenDelivery').tap();await page.locator('#deliveryModal.show').waitFor();await settled();}
 async function closeDelivery(){await page.locator('#deliveryModal .btn-close').click();await page.locator('#deliveryModal').waitFor({state:'hidden'});await settled();}
 const sizes=[[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]];
 async function deliveryGeometry(route,w,h){
  await page.setViewportSize({width:w,height:h});await settled();await page.waitForFunction(()=>Math.abs(document.getElementById('deliveryModal').getBoundingClientRect().height-innerHeight)<2);
  const g=await page.locator('#deliveryModal').evaluate(el=>{const rect=n=>{const b=n.getBoundingClientRect();return {x:b.x,y:b.y,right:b.right,bottom:b.bottom,width:b.width,height:b.height};};return {document:document.documentElement.scrollWidth,modal:rect(el.querySelector('.modal-content')),body:rect(el.querySelector('.modal-body')),footer:rect(el.querySelector('.modal-footer')),font:getComputedStyle(el.querySelector('#deliveryPhone')).fontSize,targets:[...el.querySelectorAll('.modal-header button,.modal-footer button,.modal-footer a')].filter(n=>n.getClientRects().length).map(n=>({id:n.id,...rect(n)}))};});
  assert.ok(g.document<=w+1&&Math.abs(g.modal.x)<1&&Math.abs(g.modal.right-w)<1&&g.modal.bottom<=h+1,JSON.stringify(g));assert.ok(g.body.height>=44&&g.body.bottom<=g.footer.y+1&&g.footer.bottom<=h+1,JSON.stringify(g));assert.ok(parseFloat(g.font)>=16);
  for(const t of g.targets)assert.ok(t.width>=44&&t.height>=44&&t.x>=0&&t.right<=w+1&&t.bottom<=h+1,JSON.stringify(t));assert.equal(await page.locator('[id="deliveryMessage"]').count(),1);assert.equal(await page.locator('#deliveryModal .modal-footer #deliveryMessage').count(),1);result.viewports.push({surface:'delivery',route,w,h,...g});
 }
 async function bannerGeometry(route,w,h){
  await page.setViewportSize({width:w,height:h});await settled();const g=await page.locator('#posNetworkStateBanner').evaluate(el=>{const b=el.getBoundingClientRect();return {x:b.x,right:b.right,width:b.width,document:document.documentElement.scrollWidth,text:el.textContent,targets:[...el.querySelectorAll('button')].map(n=>({text:n.textContent,w:n.getBoundingClientRect().width,h:n.getBoundingClientRect().height}))};});assert.ok(g.document<=w+1&&g.x>=0&&g.right<=w+1,JSON.stringify(g));for(const t of g.targets)assert.ok(t.w>=44&&t.h>=44);result.viewports.push({surface:'network',route,w,h,...g});
 }
 async function desktop(route,w){
  await page.setViewportSize({width:w,height:1000});await ready(route);await openDelivery();await page.locator('#deliverySubmit:not([disabled])').waitFor();
  const proof=await page.evaluate(()=>{
   const snap=()=>[...document.querySelectorAll('#deliveryModal,#deliveryModal *,#posNetworkStateBanner,#posNetworkStateBanner *')].map(el=>{const s=getComputedStyle(el),b=el.getBoundingClientRect();return {css:[...s].map(p=>[p,s.getPropertyValue(p)]),box:[b.x,b.y,b.width,b.height]};});
   const link=document.querySelector('link[href*="pos.session.mobile.css"]'),before=snap();link.disabled=true;const after=snap();link.disabled=false;return {equal:JSON.stringify(before)===JSON.stringify(after),count:before.length};
  });assert.ok(proof.equal);assert.equal(await page.locator('#deliveryModal .modal-body > #deliveryMessage').count(),1);result.desktopProofs.push({route,w,...proof});if(w===1440)await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-desktop.png':'prime-desktop.png')});await closeDelivery();
 }
 async function viewportSimulation(route){
  await page.locator('#deliveryAddress').focus();await page.evaluate(()=>{window.__sessionRealViewport=visualViewport;Object.defineProperty(window,'visualViewport',{configurable:true,value:{height:360,width:390,offsetTop:0,scale:1}});__sessionRealViewport.dispatchEvent(new Event('resize'));});
  await page.waitForFunction(()=>Math.abs(document.getElementById('deliveryModal').getBoundingClientRect().height-360)<2);
  const g=await page.locator('#deliveryModal').evaluate(el=>({footer:el.querySelector('.modal-footer').getBoundingClientRect().bottom,body:el.querySelector('.modal-body').getBoundingClientRect().height}));assert.ok(g.footer<=361&&g.body>=44);result.viewportSimulations??=[];result.viewportSimulations.push({route,...g});
  await page.evaluate(()=>{Object.defineProperty(window,'visualViewport',{configurable:true,value:__sessionRealViewport});__sessionRealViewport.dispatchEvent(new Event('resize'));});await settled();
 }
 async function configuredDisplayProbe(){
  // Isolated DOM projection, never a menu record or control injected into the application.
  const c=await browser.newContext({viewport:{width:390,height:844}}),p=await c.newPage();
  await p.route('https://configured-display.test/',r=>r.fulfill({contentType:'text/html',body:'<button data-configured-customer-display data-menu-id="123" data-url="/admin/pos/customer-display">Màn khách được cấu hình</button><button id="unconfigured">Không phải menu</button>'}));
  await p.goto('https://configured-display.test/');await p.addScriptTag({path:path.resolve(__dirname,'../GaoApp.Web/wwwroot/Admin/js/gaoapp.configured-shortcuts.js')});
  await p.evaluate(()=>{window.__opens=[];window.__focus=0;window.open=(...a)=>{__opens.push(a);return {focus:()=>__focus++};};});
  for(const w of [390,1440]){await p.setViewportSize({width:w,height:844});await p.locator('[data-configured-customer-display]').click();}
  const calls=await p.evaluate(()=>__opens);assert.equal(calls.length,2);assert.deepEqual(calls[0],calls[1]);assert.equal(calls[0][0],'https://configured-display.test/admin/pos/customer-display');assert.equal(calls[0][1],'GaoMartCustomerDisplay');assert.equal(await p.evaluate(()=>__focus),2);
  await p.locator('#unconfigured').click();assert.equal(await p.evaluate(()=>__opens.length),2);await p.locator('[data-configured-customer-display]').evaluate(el=>{el.dataset.url='https://untrusted.test/';});await p.locator('[data-configured-customer-display]').click();assert.equal(await p.evaluate(()=>__opens.length),2);
  await p.locator('[data-configured-customer-display]').evaluate(el=>{el.dataset.url='/admin/pos/customer-display';window.open=()=>null;});
  const warning=new Promise(resolve=>p.once('dialog',async d=>{const t=d.message();await d.accept();resolve(t);}));await p.locator('[data-configured-customer-display]').click();assert.match(await warning,/chặn popup/);await c.close();
 }
 try{
  context=await makeContext();page=await context.newPage();observe(page);await login(page);await ready();
  for(const [route,w] of [['/admin/pos',1440],['/admin/pos',768],['/admin/pos',1024],['/admin/pos/legacy',1440],['/admin/pos/legacy',768]])await desktop(route,w);
  pass('Five tablet/desktop computed-style and box comparisons preserve original delivery/network UI at 768/1024/1440');
  for(const route of ['/admin/pos','/admin/pos/legacy']){
   await page.setViewportSize({width:390,height:844});await ready(route);await page.waitForFunction(()=>PosOffline.status().shellReady);await openDelivery();await page.locator('#deliverySubmit:not([disabled])').waitFor();
   await page.locator('#deliveryName').fill('Người nhận trên điện thoại');await page.locator('#deliveryPhone').fill('0901234567');await page.locator('#deliveryAddress').fill('Địa chỉ nhận hàng được giữ khi đổi kích thước');
   await page.evaluate(()=>{window.__deliveryMessage=document.getElementById('deliveryMessage');});for(const [w,h] of sizes)await deliveryGeometry(route,w,h);
   await page.setViewportSize({width:390,height:844});await settled();await viewportSimulation(route);await page.locator('#deliveryPhone').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(info.output,route.includes('legacy')?'legacy-delivery-phone.png':'delivery-phone.png')});
   await page.setViewportSize({width:1440,height:1000});await settled();assert.equal(await page.locator('#deliveryName').inputValue(),'Người nhận trên điện thoại');assert.equal(await page.evaluate(()=>document.getElementById('deliveryMessage')===__deliveryMessage),true);assert.equal(await page.locator('#deliveryModal .modal-body > #deliveryMessage').count(),1);await closeDelivery();
   await page.setViewportSize({width:390,height:844});await context.setOffline(true);await page.locator('#posNetworkStateBanner:visible').waitFor();await page.waitForFunction(()=>!PosOffline.status().connected);for(const [w,h] of sizes)await bannerGeometry(route,w,h);await context.setOffline(false);await page.evaluate(()=>PosOffline.sync(true));await page.waitForFunction(()=>PosOffline.status().connected);
  }
  pass('Prime/legacy: 14 delivery + 14 offline banner phone sizes, two reduced visualViewport simulations, same form values/status node and desktop restoration');
  await page.setViewportSize({width:390,height:844});await ready();await page.waitForFunction(()=>PosOffline.status().shellReady&&navigator.serviceWorker.controller);
  const accountPage=await context.newPage();observe(accountPage);await accountPage.goto(info.manager.baseUrl+'/admin');
  const second=await context.newPage();observe(second);await second.goto(info.manager.baseUrl+'/admin/pos');await second.waitForFunction(()=>window.PosOffline?.status().message.includes('tab khác'));
  const secondWrite=await second.evaluate(async()=>{const r=await fetch('/admin/pos/cart/current/note',{method:'POST',headers:{'Content-Type':'application/json'},body:'{"note":"Blocked second tab"}'});return r.status;});assert.equal(secondWrite,409);assert.equal(await second.evaluate(()=>PosOffline.canWork()),false);await second.screenshot({path:path.join(info.output,'second-tab-blocked.png')});
  pass('Actual second phone tab is read-only under the original terminal writer lock and rejects a mutation with 409');
  await context.setOffline(true);await page.waitForFunction(()=>!PosOffline.status().connected);
  await api('/admin/pos/cart/current/note','POST',{note:'UI06e offline journal preserved'});await page.evaluate(()=>posApp.loadScreen());await page.locator('#posNetworkStateBanner button').filter({hasText:'Lưu bản đối soát'}).waitFor();
  const sessions=await journal(),saved=sessions.find(x=>x.queue.some(op=>op.body?.note==='UI06e offline journal preserved'));assert.ok(saved);
  await page.reload();await page.waitForFunction(()=>window.posApp?.state.business.currentDraft&&PosOffline.canWork());await idle();
  const originalQueue=(await journal()).find(x=>x.key===saved.key).queue;assert.deepEqual(originalQueue.slice(0,saved.queue.length),saved.queue);assert.ok(originalQueue.slice(saved.queue.length).every(op=>op.url==='/admin/pos/cart/ensure'));
  assert.equal(await page.evaluate(()=>document.getElementById('posShell').__sessionMobileMounted),true);result.pendingOperationIds=originalQueue.map(x=>x.id);
  pass('Actual service worker reloads the phone shell with the new presentation assets offline and preserves every original queued command');
  for(const [w,h] of sizes)await bannerGeometry('/admin/pos-pending',w,h);await page.setViewportSize({width:390,height:844});await settled();await page.screenshot({path:path.join(info.output,'offline-pending-phone.png')});
  const downloadEvent=page.waitForEvent('download');await page.locator('#posNetworkStateBanner button').filter({hasText:'Lưu bản đối soát'}).tap();const download=await downloadEvent;const exportPath=path.join(info.output,'disposable-journal-export.json');await download.saveAs(exportPath);const exported=JSON.parse(fs.readFileSync(exportPath,'utf8'));assert.equal(exported.state.context.antiForgeryToken,undefined);assert.deepEqual(exported.state.queue,originalQueue);
  await page.locator('#posNetworkStateBanner button').filter({hasText:'Thử đồng bộ'}).tap();assert.deepEqual((await journal()).find(x=>x.key===saved.key).queue,originalQueue);
  await openDelivery();await page.waitForFunction(()=>document.getElementById('deliveryMessage').textContent.includes('đồng bộ'));assert.equal(await page.locator('#deliverySubmit').isDisabled(),true);await page.screenshot({path:path.join(info.output,'delivery-offline-blocked.png')});await closeDelivery();assert.deepEqual((await journal()).find(x=>x.key===saved.key).queue,originalQueue);
  pass('Prepared offline journal saves the original command; phone sync/export keep its identity and omit CSRF; offline delivery is blocked without a delivery queue');
  // Submit the real logout form while offline. The privacy owner locks immediately,
  // before the unreachable server, so reconnect cannot race the pending journal.
  await accountPage.locator('form[action="/admin/account/logout"]').evaluate(form=>form.requestSubmit());
  await page.waitForFunction(()=>PosOffline.status().locked);await second.waitForFunction(()=>PosOffline.status().locked);
  const lock=page.locator('dialog[aria-label="Phiên POS đã khóa"]');await lock.waitFor({state:'visible'});
  for(const [w,h] of sizes){await page.setViewportSize({width:w,height:h});await settled();const g=await lock.evaluate(el=>{const b=el.getBoundingClientRect(),a=el.querySelector('a').getBoundingClientRect();return {x:b.x,right:b.right,bottom:b.bottom,link:{width:a.width,height:a.height,top:a.top,bottom:a.bottom}};});assert.ok(g.x===0&&g.right<=w+1&&g.bottom<=h+1&&g.link.width>=44&&g.link.height>=44&&g.link.top>=0&&g.link.bottom<=h+1,JSON.stringify(g));result.viewports.push({surface:'privacy-lock',w,h,...g});}
  await page.setViewportSize({width:390,height:844});await page.keyboard.press('Escape');assert.equal(await lock.isVisible(),true);await page.screenshot({path:path.join(info.output,'privacy-lock-phone.png')});assert.deepEqual((await journal()).find(x=>x.key===saved.key).queue,originalQueue);assert.equal(await page.evaluate(()=>PosOffline.canWork()),false);
  await page.setViewportSize({width:1440,height:1000});await settled();const lockDesktop=await page.evaluate(()=>{const el=document.querySelector('dialog[aria-label="Phiên POS đã khóa"]'),link=document.querySelector('link[href*="pos.session.mobile.css"]'),snap=()=>[el,...el.querySelectorAll('*')].map(n=>{const c=getComputedStyle(n),b=n.getBoundingClientRect();return {css:[...c].map(p=>[p,c.getPropertyValue(p)]),box:[b.x,b.y,b.width,b.height]};});const before=snap();link.disabled=true;const after=snap();link.disabled=false;return JSON.stringify(before)===JSON.stringify(after);});assert.ok(lockDesktop);result.desktopProofs.push({surface:'privacy-lock',w:1440,equal:true});
  await context.setOffline(false);await accountPage.goto(info.manager.baseUrl+'/admin');await accountPage.locator('form[action="/admin/account/logout"]').evaluate(form=>form.requestSubmit());await accountPage.waitForURL(u=>u.pathname.endsWith('/login'));assert.deepEqual((await journal(accountPage)).find(x=>x.key===saved.key).queue,originalQueue);
  await page.close();await second.close();page=accountPage;await login(page);await ready();await page.waitForFunction(()=>PosOffline.status().pending===0);assert.equal((await draft()).note,'UI06e offline journal preserved');
  pass('Offline logout locks both phone tabs, preserves pending operations, blocks Escape/writes and verified reauthentication replays the original journal; desktop lock styles match');
  await context.close();context=await makeContext(true);page=await context.newPage();observe(page);await login(page);await ready();await openDelivery();await page.locator('#deliverySubmit:not([disabled])').waitFor();
  const recipient={name:'Nguyễn An nhận hàng',phone:'0901234567',address:'123 Nguyễn Văn Cừ, phường thử nghiệm',note:'Gọi trước khi giao'};
  async function fill(){for(const [id,value] of [['deliveryName',recipient.name],['deliveryPhone',recipient.phone],['deliveryAddress',recipient.address],['deliveryNote',recipient.note]])await page.locator('#'+id).fill(value);}
  await fill();await page.locator('#deliveryPhone').fill('');await page.locator('#deliverySubmit').tap();assert.equal(await page.locator('#deliveryPhone').evaluate(el=>el.validity.valid),false);assert.equal(result.deliveryRequests.length,0);await fill();
  let mode='conflict',release,started;
  await page.route('**/admin/api/deliveries/from-current-cart',async route=>{
   const body=route.request().postDataJSON();result.deliveryRequests.push({mode,body});
   if(mode==='conflict'){const response=await route.fetch({postData:JSON.stringify({...body,expectedFingerprint:'A'.repeat(64)})});assert.equal(response.status(),409);return route.fulfill({response});}
   if(mode==='lost'){started?.();await new Promise(resolve=>{release=resolve;});const response=await route.fetch();assert.equal(response.status(),200);return route.fulfill({status:503,contentType:'application/json',body:'{"message":"Mất phản hồi xác nhận thử nghiệm"}'});}
   return route.continue();
  });
  await page.locator('#deliverySubmit').tap();await page.waitForFunction(()=>document.getElementById('deliveryMessage').textContent.includes('Giỏ đã đổi'));assert.equal(await page.locator('#deliveryResult').isVisible(),false);await page.screenshot({path:path.join(info.output,'delivery-conflict-phone.png')});
  await page.locator('#deliveryRefresh').tap();await page.locator('#deliverySubmit:not([disabled])').waitFor();await fill();mode='lost';const reached=new Promise(resolve=>{started=resolve;});await page.locator('#deliverySubmit').tap();await reached;await page.evaluate(()=>{document.getElementById('deliveryForm').requestSubmit();document.getElementById('deliveryForm').requestSubmit();});assert.equal(result.deliveryRequests.length,2);release();
  await page.waitForFunction(()=>document.getElementById('deliveryMessage').textContent.includes('Chưa xác nhận'));assert.equal(await page.locator('#deliveryResult').isVisible(),false);await page.screenshot({path:path.join(info.output,'delivery-uncertain-phone.png')});
  await ready();await openDelivery();await page.waitForFunction(()=>document.getElementById('deliverySubmit').textContent.includes('yêu cầu cũ'));assert.equal(await page.locator('#deliveryName').isDisabled(),true);assert.equal(await page.locator('#deliveryName').inputValue(),recipient.name);mode='retry';await page.locator('#deliverySubmit').tap();await page.locator('#deliveryResult:not([hidden])').waitFor();await idle();
  assert.deepEqual(result.deliveryRequests[1].body,result.deliveryRequests[2].body);assert.equal(await page.locator('#deliveryModal .modal-footer #deliveryMessage').count(),1);assert.match(await page.locator('#deliveryMessage').innerText(),/thành công/);assert.notEqual((await draft()).orderId,info.orderId);
  await page.locator('#deliveryResult').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(info.output,'delivery-success-phone.png')});await closeDelivery();
  pass('Recipient validation and real 409 expose no success; slow submit blocks repetition; commit plus 503/reload retries the identical delivery intent once');
  const display=await context.newPage();observe(display);await display.goto(info.manager.baseUrl+'/admin/pos/customer-display');await display.locator('#customerDisplayApp').waitFor();assert.equal(await display.locator('#customerDisplayApp').getAttribute('data-store-id'),String(info.storeId));assert.equal(await display.locator('#customerDisplayApp').getAttribute('data-terminal-id'),String(info.manager.terminalId));await display.close();await configuredDisplayProbe();
  pass('Original configured-display opener keeps the same named popup on phone/desktop, ignores unconfigured/cross-origin targets, reports popup blocking; real display uses this store/terminal');
  await context.close();context=await makeContext(true);page=await context.newPage();observe(page);await login(page,info.limited);const html=await(await page.request.get(info.limited.baseUrl+'/admin/pos')).text();assert.ok(!html.includes('id="deliveryModal"')&&!html.includes('id="btnOpenDelivery"'));const denied=await page.request.get(info.limited.baseUrl+'/admin/api/deliveries/current-cart',{maxRedirects:0});assert.ok([302,403].includes(denied.status()));
  pass('Server omits the delivery form/entry without store permissions and rejects its API; no menu record or shortcut is created');
  assert.deepEqual(result.errors,[]);assert.deepEqual(result.csp,[]);result.completed=true;
 }finally{
  if(page&&!page.isClosed())await page.screenshot({path:path.join(info.output,'last-state.png')}).catch(()=>{});fs.writeFileSync(path.join(info.output,'result.json'),JSON.stringify(result,null,2));await browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
