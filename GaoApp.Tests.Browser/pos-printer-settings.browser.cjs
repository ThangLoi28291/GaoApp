const {chromium}=require('playwright');
const assert=require('node:assert/strict'),path=require('node:path');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let value='';process.stdin.on('data',x=>value+=x);process.stdin.on('end',()=>resolve(value));}));
 const browser=await chromium.launch({channel:'chrome',headless:true});
 const context=await browser.newContext({viewport:{width:1280,height:960}}),page=await context.newPage(),errors=[],writes=[];
 page.on('pageerror',error=>errors.push(error.message));
 page.on('request',request=>{if(request.url().includes('/receipt-templates')&&!['GET','HEAD'].includes(request.method()))writes.push(request.url());});
 const key=`gao-pos-print-v1:${info.storeId}:${info.terminalId}`,otherKey=`gao-pos-print-v1:${info.storeId}:${info.terminalId+1000}`;
 await page.addInitScript(({key,otherKey})=>{
  if(!localStorage.getItem(key))localStorage.setItem(key,JSON.stringify({mode:'qz',printer:'EPSON cũ',copies:2,color:false,templateKey:'classic-A4'}));
  if(!localStorage.getItem(otherKey))localStorage.setItem(otherKey,JSON.stringify({mode:'qz',printer:'Quầy khác',copies:1,color:false}));
 },{key,otherKey});
 try{
  await page.goto(info.baseUrl+'/admin/account/login?returnUrl='+encodeURIComponent('/admin/pos/printer-settings'));
  await page.locator('[name=UserName]').fill(info.user);await page.locator('[name=Password]').fill(info.password);
  if(await page.locator('select[name=SelectedTerminalId]').count())await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
  await Promise.all([page.waitForURL(u=>u.pathname.endsWith('/admin/pos/printer-settings')),page.locator('#loginForm button[type=submit]').click()]);
  await page.locator('#applyTemplate').waitFor();
  assert.equal(await page.locator('#printMode').inputValue(),'qz');assert.equal(await page.locator('#clientPrinter').inputValue(),'EPSON cũ');assert.equal(await page.locator('#printCopies').inputValue(),'2');
  assert.equal(await page.locator('#designFields,#saveStoreIdentity,#setStoreDefault').count(),0);
  await page.evaluate(()=>{PosPrinting.printers=async()=>['EPSON cũ','EPSON mới'];});
  await page.locator('#connectPrinters').click();await page.locator('#clientPrinter option').filter({hasText:'EPSON mới'}).waitFor({state:'attached'});
  await page.locator('#clientPrinter').selectOption('EPSON mới');await page.locator('#printCopies').fill('3');await page.locator('#applyTemplate').click();
  await page.locator('#printerSettingsMessage').filter({hasText:'Đã lưu máy in'}).waitFor();
  let prefs=await page.evaluate(key=>JSON.parse(localStorage.getItem(key)),key);assert.equal(prefs.printer,'EPSON mới');assert.equal(prefs.copies,3);assert.equal(prefs.templateKey,'classic-A5');
  await page.reload();await page.locator('#clientPrinter').waitFor({state:'visible'});assert.equal(await page.locator('#clientPrinter').inputValue(),'EPSON mới');assert.equal(await page.locator('#printCopies').inputValue(),'3');
  await page.screenshot({path:path.join(info.output,'desktop.png'),fullPage:true});
  await page.setViewportSize({width:390,height:844});
  assert.ok((await page.locator('#applyTemplate').boundingBox()).width>=270,'The phone save action has a full-width touch target');
  await page.screenshot({path:path.join(info.output,'phone.png'),fullPage:true});
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  await page.evaluate(()=>{PosPrinting.health=async()=>({ready:true});PosPrinting.openLocal=async(receipt,setup,catalog)=>{window.testPrint={receipt,setup,catalog};};});
  await page.locator('#printMode').selectOption('helper');await page.locator('#checkHelper').click();await page.locator('#printerSettingsMessage').filter({hasText:'Print Helper sẵn sàng'}).waitFor();
  await page.locator('#applyTemplate').click();prefs=await page.evaluate(key=>JSON.parse(localStorage.getItem(key)),key);assert.equal(prefs.mode,'helper');
  await page.locator('#printMode').selectOption('browser');assert.equal(await page.locator('#printCopySettings').isHidden(),true);
  await page.locator('#testPrint').click();await page.waitForFunction(()=>window.testPrint);
  const printed=await page.evaluate(()=>window.testPrint);assert.equal(printed.receipt.orderNumber,'IN-THU');assert.equal(printed.setup.receiptDefault.template.key,'classic-A5');
  assert.equal((await page.evaluate(key=>JSON.parse(localStorage.getItem(key)),otherKey)).printer,'Quầy khác');
  assert.deepEqual(writes,[]);assert.deepEqual(errors,[]);
  console.log('Employee printer setup: existing preferences preserved, QZ/helper/browser modes, default template, desktop/phone, no shared-setting writes or physical printing.');
 }catch(error){await page.screenshot({path:path.join(info.output,'failure.png'),fullPage:true}).catch(()=>{});throw error;}
 finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
