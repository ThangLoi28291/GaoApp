'use strict';
const assert=require('node:assert/strict'),{chromium}=require('playwright');
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try{
  const context=await browser.newContext();
  const page=await context.newPage();
  page.on('console',message=>{if(message.type()==='error')console.error(message.text());});
  page.on('requestfailed',request=>console.error(request.url(),request.failure()?.errorText));
  const cdp=await context.newCDPSession(page);
  const {targetInfo}=await cdp.send('Target.getTargetInfo');
  await cdp.send('Browser.setPermission',{permission:{name:'loopback-network'},setting:'granted',origin:'https://gao.test',browserContextId:targetInfo.browserContextId});
  await context.route('https://gao.test/**',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><html><button id="connect">Kết nối</button></html>'}));
  await page.goto('https://gao.test/');
  const result=await page.evaluate(async()=>{
   const headers={Authorization:'Bearer smoke-test-pairing-token-32-characters'};
   const request=async(path,body)=>{
    const response=await fetch('http://127.0.0.1:17881'+path,{method:body?'POST':'GET',targetAddressSpace:'loopback',credentials:'omit',headers:{...headers,...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined});
    if(!response.ok)throw Error('Client HTTP '+response.status);
    return path==='/preview'?{type:response.headers.get('content-type'),bytes:(await response.arrayBuffer()).byteLength}:await response.json();
   };
   const health=await request('/health'),printers=await request('/printers');
   const preview=await request('/preview',{design:{name:'Tem thử',layout:'promotion-shelf',widthMm:50,heightMm:30,columns:2,fontSize:8},
    printer:{name:'Chỉ xem mẫu',windowsPrinterName:'preview-only',dpi:203,printableWidthMm:108,offsetXmm:0,offsetYmm:0},
    product:{variantId:0,name:'Bộ bàn chải dây 3 chiếc',barcode:'4902430679640',unit:'Bộ',price:32100,receivedQuantity:0,promotion:{retailPrice:39000,minimumQuantity:1,startAtUtc:'2026-10-11T00:00:00Z',endAtUtc:'2026-10-31T00:00:00Z'}}});
   return {health,printers,preview};
  });
  assert.equal(result.health.protocol,'gao-label-client-v1');assert.ok(Array.isArray(result.printers.printers));assert.equal(result.preview.type,'image/png');assert.ok(result.preview.bytes>100);
  console.log('PASS: HTTPS Chrome connects to packaged native client over real loopback CORS and renders two-column preview; no print call.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
