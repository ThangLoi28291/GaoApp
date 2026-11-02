'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');const {review,settle,evidence,save}=require('./printing-screens-mobile-review.cjs');
(async()=>{
 const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',v=>s+=v);process.stdin.on('end',()=>resolve(s));}));fs.mkdirSync(info.output,{recursive:true});
 const browser=await chromium.launch({channel:'chrome',headless:true}),context=await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce'}),cashier=await context.newPage(),display=await context.newPage(),errors=[],external=[];
 display.on('pageerror',e=>errors.push(e.message));
 await context.route('**/*',r=>{const u=new URL(r.request().url());if(u.origin!==new URL(info.baseUrl).origin&&!['data:','blob:'].includes(u.protocol)){external.push(u.origin+u.pathname);return r.abort();}return r.continue();});
 const state=async label=>review(display,'#customerDisplayApp',label,info.output,{dynamic:true});
 try{
  await cashier.goto(info.baseUrl+'/admin/account/login');await cashier.locator('[name=UserName]').fill(info.user);await cashier.locator('[name=Password]').fill(info.password);await cashier.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));await Promise.all([cashier.waitForURL(u=>!u.pathname.endsWith('/login')),cashier.locator('#loginForm button[type=submit]').click()]);
  await cashier.goto(info.baseUrl+'/admin/label-printing');const token=await cashier.locator('[name=__RequestVerificationToken]').first().inputValue();
  const api=async(url,data={})=>{const r=await context.request.post(info.baseUrl+url,{data,headers:{RequestVerificationToken:token}});assert.equal(r.ok(),true,url+' '+await r.text());return r.json();};
  const draft=await api('/admin/pos/draft');const orderId=draft.orderId;await api('/admin/pos/'+orderId+'/items?variantId='+info.variantId+'&qty=3');
  const screen=await (await context.request.get(info.baseUrl+'/admin/pos/screen')).json(),total=screen.currentDraft.grandTotal;assert.ok(total>10);
  await display.goto(info.baseUrl+'/admin/pos/customer-display');await display.waitForFunction(()=>document.querySelector('.cd-status')?.textContent.includes('Sẵn sàng'));await state('customer-cart');
  await display.evaluate(async info=>{window.ui22Bridge=new signalR.HubConnectionBuilder().withUrl('/hubs/pos').build();await window.ui22Bridge.start();await window.ui22Bridge.invoke('JoinStoreGroup',info.storeId,String(info.terminalId));},info);
  const send=(eventType,payload={})=>display.evaluate(({eventType,payload})=>ui22Bridge.invoke('BroadcastTerminalEvent',{eventType,payload}),{eventType,payload});
  const qr=await api('/admin/pos/cart/current/payment-qr',{amount:10,clientRequestId:crypto.randomUUID()});await send('customer_payment_qr_created',{orderId,qrId:qr.id});
  await display.locator('#cdQrBox:not(.d-none)').waitFor();assert.equal(await display.locator('#cdQrImage').getAttribute('src'),qr.qrDataUrl);await state('customer-local-QR');
  await context.setOffline(true);await display.waitForFunction(()=>document.querySelector('.cd-status')?.textContent.includes('Mất kết nối'));assert.equal(await display.locator('#cdPaymentOverlay').isVisible(),false);await state('customer-disconnected');
  await context.setOffline(false);await display.locator('#cdQrBox:not(.d-none)').waitFor();await state('customer-QR-reconnected');
  await api('/admin/pos/payment-qr/'+qr.id+'/manual-confirm');await display.locator('#cdSuccessOverlay:not(.d-none)').waitFor();await state('customer-partial-confirmation');
  await api('/admin/pos/'+orderId+'/payments',{method:0,amount:total-10,clientRequestId:crypto.randomUUID()});await api('/admin/pos/'+orderId+'/finalize');
  await display.locator('#cdReceiptPaper[data-order-id="'+orderId+'"]').waitFor();await state('customer-committed-receipt');
  const receipt=await (await context.request.get(info.baseUrl+'/admin/pos/orders/'+orderId+'/receipt')).json();const printed=await (await context.request.get(info.baseUrl+'/admin/pos/orders/'+orderId+'/print?autoPrint=false')).text();const model=JSON.parse(printed.match(/<script\b[^>]*\bid="receiptPrintData"[^>]*>([\s\S]*?)<\/script>/)[1]);assert.deepEqual(model.receipt,receipt);assert.equal(receipt.grandTotal,total);assert.equal(receipt.paidTotal,total);
  assert.deepEqual(errors,[]);assert.deepEqual(external,[]);save(info.output);fs.writeFileSync(path.join(info.output,'result.json'),JSON.stringify({passed:true,nativeStates:evidence.cases.length,phoneChecks:evidence.phoneChecks.length,desktopProofs:evidence.desktopProofs.length,printProofs:evidence.printProofs.length,localQrMatchesStoredData:true,printedReceiptMatchesCommittedData:true,physicalPrinterUsed:false,external,pageErrors:errors},null,2)+'\n');console.log('PASS native cart, local QR, disconnect/reconnect, partial confirmation and committed receipt.');
 }catch(error){save(info.output);await display.screenshot({path:path.join(info.output,'failure.png'),fullPage:true}).catch(()=>{});throw error;}
 finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
