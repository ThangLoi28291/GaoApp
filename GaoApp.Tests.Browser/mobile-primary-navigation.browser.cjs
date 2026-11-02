'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',v=>s+=v);process.stdin.on('end',()=>resolve(s));}));
 fs.mkdirSync(info.output,{recursive:true});const browser=await chromium.launch({channel:'chrome',headless:true});
 const context=await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce'}),page=await context.newPage(),errors=[],checks=[];
 page.on('pageerror',e=>errors.push(e.message));
 const bar=page.locator('[data-mobile-pins]');
 const settle=async()=>{await page.evaluate(()=>document.fonts.ready);await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));};
 const login=async account=>{await context.clearCookies();await page.goto(account.baseUrl+'/admin/account/login');await page.locator('[name=UserName]').fill(account.user);await page.locator('[name=Password]').fill(account.password);await page.locator('[name=SelectedTerminalId]').selectOption(String(account.terminalId));await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('#loginForm button[type=submit]').click()]);};
 async function checkTabs(ids,labels,label){
  await bar.waitFor();await settle();assert.deepEqual(await bar.locator('[data-mobile-pin-id]').evaluateAll(nodes=>nodes.map(n=>Number(n.dataset.mobilePinId))),ids,label);
  assert.deepEqual(await bar.locator('.gao-mobile-pin > span').allTextContents(),[...labels,'Menu'],label);
  assert.equal(await bar.locator('[data-pins-menu]').count(),1);
  const verified=await page.evaluate(()=>{const bar=document.querySelector('[data-mobile-pins]'),menu=document.querySelector('[data-admin-navigation]');return[...bar.querySelectorAll('[data-mobile-pin-id]')].every(node=>{const source=menu.querySelector('[data-nav-menu-id="'+node.dataset.mobilePinId+'"] [data-nav-destination]');return source&&source.getAttribute('href')===node.getAttribute('href');});});assert.equal(verified,true,label+' every URL references the filtered store menu');
  checks.push({label,ids,labels});
 }
 async function phoneSizes(label){
  for(const[width,height]of[[320,568],[390,844],[600,900],[767,900],[390,440]]){
   await page.setViewportSize({width,height});await settle();const metrics=await bar.evaluate(node=>{const box=node.getBoundingClientRect();return{pageWidth:document.documentElement.scrollWidth,width:innerWidth,bottom:box.bottom,height:innerHeight,items:[...node.querySelectorAll('.gao-mobile-pin')].map(n=>{const r=n.getBoundingClientRect();return{width:r.width,height:r.height};})};});
   assert.ok(metrics.pageWidth<=width+1&&metrics.bottom<=height+1,JSON.stringify(metrics));assert.ok(metrics.items.every(x=>x.width>=44&&x.height>=44),JSON.stringify(metrics));
  }
  await page.setViewportSize({width:390,height:844});await settle();await page.screenshot({path:path.join(info.output,label+'-phone.png')});
 }
 async function desktopParity(){
  const beforeCss=fs.readFileSync('.artifacts/mobile-primary-nav/before/GaoApp.Web/wwwroot/Admin/css/gaoapp.mobile-pins.css','utf8');
  for(const width of[768,1440]){
   await page.setViewportSize({width,height:1000});await settle();assert.equal(await bar.isVisible(),false);
   const result=await page.evaluate(beforeCss=>{
    const link=document.querySelector('link[href*="gaoapp.mobile-pins.css"]'),nodes=[...document.querySelectorAll('[data-admin-navigation], [data-admin-navigation] *, .layout-page, .layout-page *')];
    const snap=n=>{const s=getComputedStyle(n),r=n.getBoundingClientRect();return{css:JSON.stringify([...s].map(k=>[k,s.getPropertyValue(k)])),box:[r.x,r.y,r.width,r.height]};};
    const before=nodes.map(snap),style=document.createElement('style');style.textContent=beforeCss;link.disabled=true;document.head.append(style);
    const differences=nodes.flatMap((node,i)=>{const after=snap(node);return before[i].css!==after.css||before[i].box.some((v,j)=>v!==after.box[j])?[{tag:node.tagName,id:node.id}]:[];});
    style.remove();link.disabled=false;return{elements:nodes.length,differences};
   },beforeCss);assert.deepEqual(result.differences,[],'Desktop/tablet CSS and geometry unchanged '+width);checks.push({label:'desktop-parity',width,...result});
  }
  await page.screenshot({path:path.join(info.output,'desktop-1440.png')});await page.setViewportSize({width:390,height:844});
 }
 try{
  await login(info.accounts.full);
  if(info.mode==='custom'){
   await page.goto(info.accounts.full.baseUrl+'/admin/label-printing');await checkTabs([info.customId],['In tem riêng của tiệm'],'explicit custom pins retained');await phoneSizes('custom-pins');
  }else{
   // A blocked pin API must not erase the server-rendered preferred tabs.
   await page.route('**/admin/menu-pins/configured',r=>r.fulfill({status:503,contentType:'application/json',body:'{}'}));
   await page.goto(info.accounts.full.baseUrl+'/admin/home');
   await checkTabs([info.homeId,info.ordersId,info.receivingId,info.salesId],['Tổng quan','Đơn hàng','Nhập hàng','Bán hàng'],'default tabs without configured pins/API');
   await phoneSizes('default-home');await desktopParity();
   await bar.locator('[data-pins-menu]').click();await page.locator('html.layout-menu-expanded').waitFor({state:'attached'});await page.locator('[data-nav-close]').click();await page.waitForFunction(()=>!document.documentElement.classList.contains('layout-menu-expanded'));
   await page.unroute('**/admin/menu-pins/configured');
   await page.goto(info.accounts.full.baseUrl+'/admin/pos/orders-page');
   await checkTabs([info.homeId,info.ordersId,info.receivingId,info.salesId],['Tổng quan','Đơn hàng','Nhập hàng','Bán hàng'],'order page default navigation');
   assert.equal(await bar.locator('[aria-current=page]').getAttribute('data-mobile-pin-id'),String(info.ordersId));await phoneSizes('default-orders');
   const data=await(await context.request.get(info.accounts.full.baseUrl+'/admin/menu-pins/configured')).json();assert.deepEqual(data.menuIds,[]);assert.deepEqual(data.defaultTabs.map(t=>t.id),[info.homeId,info.ordersId,info.receivingId,info.salesId]);
   await login(info.accounts.editor);await page.goto(info.accounts.editor.baseUrl+'/admin/menu-pins');
   assert.deepEqual(await page.locator('[data-pin-preview] small').allTextContents(),['Tổng quan','Đơn hàng','Nhập hàng','Bán hàng','Menu']);assert.equal(await page.locator('[data-pin-save]').isDisabled(),true);
   await page.locator('[data-pin-subject]').selectOption('role:'+info.limitedRoleId);await page.waitForFunction(()=>document.querySelector('[data-pin-preview-status]').textContent.startsWith('2/4'));
   assert.deepEqual(await page.locator('[data-pin-preview] small').allTextContents(),['Đơn hàng','Bán hàng','Menu']);await page.locator('[data-pin-preview]').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(info.output,'editor-role-preview.png')});checks.push({label:'phone editor preview uses defaults and role permissions without selecting or saving pins'});
   await login(info.accounts.limited);await page.goto(info.accounts.limited.baseUrl+'/admin/pos/orders-page');await checkTabs([info.ordersId,info.salesId],['Đơn hàng','Bán hàng'],'permission filtered defaults');await phoneSizes('limited');
   await login(info.accounts.hidden);await page.goto(info.accounts.hidden.baseUrl+'/admin/home');await checkTabs([info.homeId,info.receivingId],['Tổng quan','Nhập hàng'],'hidden parent removes default descendants');await phoneSizes('hidden-branch');
   await login(info.accounts.foreign);await page.goto(info.accounts.foreign.baseUrl+'/admin/home');await checkTabs([],[],'other store cannot inherit default IDs');await phoneSizes('other-store');
  }
  assert.deepEqual(errors,[]);fs.writeFileSync(path.join(info.output,info.mode+'-result.json'),JSON.stringify({passed:true,checks,pageErrors:errors,existingChromeWindowsPreserved:true},null,2)+'\n');console.log('PASS '+info.mode+' primary navigation, store permissions and desktop parity.');
 }catch(error){await page.screenshot({path:path.join(info.output,info.mode+'-failure.png'),fullPage:true}).catch(()=>{});throw error;}
 finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
