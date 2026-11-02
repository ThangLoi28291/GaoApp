'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'..'),web=path.join(root,'GaoApp.Web'),output=path.join(root,'TestResults/promotion-labels/browser');
fs.mkdirSync(output,{recursive:true});
const asset=file=>fs.readFileSync(path.join(web,'wwwroot',file),'utf8');
let main=fs.readFileSync(path.join(web,'Areas/Admin/Views/PromotionLabels/Index.cshtml'),'utf8').match(/<main[\s\S]*?<\/main>/)[0]
 .replace('@ViewBag.StoreId','1').replace('@Url.Content("~/admin/promotion-labels/")','/admin/promotion-labels/')
 .replace('@Url.Content("~/admin/label-printing/")','/admin/label-printing/').replace(/@Url.Content\("[^"\n]+"\)/g,'/admin/promotion-labels/client-download')
 .replace(/@Url.Action\([^\n]+?\)/g,'/Admin/Promotion').replace('@Html.AntiForgeryToken()','<input type="hidden" name="__RequestVerificationToken" value="test-csrf" />');
const html='<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>'+asset('lib/bootstrap/dist/css/bootstrap.css')+asset('Admin/css/promotion-labels.css')+'body{background:#f5f6fa;padding:16px}</style></head><body>'+main+'<script>'+asset('Admin/js/printing/promotion-labels.js')+'</script></body></html>';
const product={conversionId:1,name:'Bộ bàn chải dây 3 chiếc',sku:'BRUSH',barcode:'4902430679640',unit:'Bộ',retailPrice:39000,salePrice:32100,minimumQuantity:1,
 promotionId:5,promotionName:'Giảm giá tháng 10',startAtUtc:'2026-10-10T17:00:00Z',endAtUtc:'2026-10-31T16:59:00Z',fingerprint:'fresh',problem:null};
const token='test-token-32-characters-for-client-connection';
const png=fs.readFileSync(process.env.LABEL_NATIVE_PREVIEW || path.join(root,'TestResults/promotion-labels/native/preview-203-1.png'));
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try{
  const context=await browser.newContext({viewport:{width:1440,height:1100},reducedMotion:'reduce'});
  await context.addInitScript(()=>{window.print=()=>{throw Error('Unexpected browser print dialog');};window.open=()=>{throw Error('Unexpected print window');};});
  const requests=[],jobs=[];let stale=false,lost=false,resultKnown=false;
  await context.route('http://127.0.0.1:17881/**',async route=>{
   const req=route.request(),url=new URL(req.url());
   assert.equal(req.headers().authorization,'Bearer '+token);
   const headers={'access-control-allow-origin':'http://localhost:19181','access-control-allow-headers':'authorization,content-type'};
   const data={ok:true,protocol:'gao-label-client-v1'};
   if(url.pathname==='/preview'){assert.equal(req.postDataJSON().design.barcodeFormat,'AUTO');return route.fulfill({headers,contentType:'image/png',body:png});}
   if(url.pathname==='/printers')data.printers=['Label USB','Bill EPSON'];
   else if(url.pathname==='/print'){
    const body=req.postDataJSON();jobs.push(body);assert.equal(body.payload.design.layout,'promotion-shelf');assert.equal(body.payload.design.barcodeFormat,'AUTO');assert.equal(body.payload.printer.windowsPrinterName,'Label USB');
    if(lost)return route.fulfill({headers,status:500,contentType:'application/json',body:JSON.stringify({...data,ok:false,message:'Mất phản hồi khi gửi in'})});
    Object.assign(data,{submitted:true,requestId:body.requestId,quantity:body.payload.items.reduce((s,x)=>s+x.quantity,0)});
   }else if(url.pathname.startsWith('/results/'))data.submitted=resultKnown;
   else assert.equal(url.pathname,'/health');
   return route.fulfill({headers,contentType:'application/json',body:JSON.stringify(data)});
  });
  await context.route('http://localhost:19181/**',async route=>{
   const req=route.request(),url=new URL(req.url());
   if(url.pathname==='/admin/promotion-labels/')return route.fulfill({contentType:'text/html',body:html});
   requests.push({path:url.pathname,body:req.postDataJSON()});
   assert.ok(!req.headers().authorization,'Never send pairing token to Web');
   if(url.pathname==='/admin/label-printing/preview'){assert.equal(req.postDataJSON().design.barcodeFormat,'AUTO');return route.fulfill({contentType:'image/png',body:png});}
   let data;
   if(url.pathname==='/admin/label-printing/templates')data=[{id:9,design:{name:'Mẫu quản lý 50 × 30',widthMm:50,heightMm:30,columns:2,columnGapMm:2,rowGapMm:3,leftMarginMm:1,rightMarginMm:1,fontSize:9,printerId:7,barcodeFormat:'CODE128'}}];
   else if(url.pathname==='/admin/label-printing/printers')data=[{id:7,name:'Client tem',windowsPrinterName:'Label USB',dpi:300,printableWidthMm:108,offsetXmm:.5,offsetYmm:-.5,enabled:true}];
   else if(url.pathname.endsWith('/campaigns'))data=[{id:5,name:'Giảm giá tháng 10'}];
   else if(url.pathname.endsWith('/products')){
    const page=Number(url.searchParams.get('page')||1),q=url.searchParams.get('q');
    data={items:q==='missing'?[]:page===2?[{...product,conversionId:25}]:Array.from({length:24},(_,i)=>({...product,conversionId:i+1})),total:q==='missing'?0:25,page,pageSize:24};
   }else if(url.pathname.endsWith('/refresh'))data=req.postDataJSON().conversionIds.map(id=>({...product,conversionId:id,fingerprint:'updated'}));
   else if(url.pathname.endsWith('/prepare')){
    assert.equal(req.headers().requestverificationtoken,'test-csrf');
    if(stale)return route.fulfill({status:409,contentType:'application/json',body:JSON.stringify({message:'Giá đã đổi. Cập nhật danh sách trước khi in.'})});
    data=req.postDataJSON().lines.map(line=>({product:{...product,conversionId:line.conversionId},quantity:line.quantity}));
   }else throw Error('Unexpected server printing route '+url.pathname);
   return route.fulfill({contentType:'application/json',body:JSON.stringify(data)});
  });
  const page=await context.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto('http://localhost:19181/admin/promotion-labels/');await page.locator('[data-add="1"]').waitFor();
  await page.locator('#plToken').fill(token);await page.locator('#plConnect').click();
  await page.locator('#plPrinter').selectOption('Label USB');
  await page.locator('#plImportTemplate').selectOption('9');
  assert.equal(await page.locator('#plDesign [name="columns"]').inputValue(),'2');
  assert.equal(await page.locator('#plPrinterForm [name="dpi"]').inputValue(),'300');
  await page.locator('#plSavePrinter').click();
  await page.locator('[data-add="1"]').click();await page.locator('.pl-quantity').fill('3');
  await page.locator('#plPrint').click();await page.waitForFunction(()=>document.getElementById('plMessage').textContent.startsWith('Đã gửi 3 tem'));
  assert.equal(jobs.length,1);const sent=jobs[0].payload;
  assert.equal(sent.design.columns,2);assert.equal(sent.design.rowGapMm,3);assert.equal(sent.printer.dpi,300);assert.equal(sent.printer.offsetXmm,.5);
  assert.equal(sent.items[0].product.promotion.retailPrice,39000);assert.equal(sent.items[0].product.price,32100);
  await page.locator('#plTestPrint').click();await page.waitForFunction(()=>document.getElementById('plMessage').textContent.startsWith('Đã gửi 2 tem'));assert.equal(jobs.length,2);
  await page.locator('[data-add="2"]').click();await page.locator('#plPrint').click();await page.waitForFunction(()=>document.getElementById('plMessage').textContent.startsWith('Đã gửi 4 tem'));assert.deepEqual(jobs[2].payload.items.map(x=>x.quantity),[3,1]);
  await page.locator('[data-remove="2"]').click();stale=true;await page.locator('#plPrint').click();
  await page.waitForFunction(()=>document.getElementById('plMessage').textContent.includes('Giá đã đổi'));assert.equal(jobs.length,3);stale=false;
  await page.locator('#plRefresh').click();await page.waitForFunction(()=>document.getElementById('plMessage').textContent.includes('Đã cập nhật'));
  await page.locator('#plNext').click();await page.locator('[data-add="25"]').waitFor();assert.equal(await page.locator('#plPageInfo').textContent(),'Trang 2/2');
  await page.locator('#plSearch').fill('missing');await page.locator('#plSearchForm').evaluate(f=>f.requestSubmit());await page.waitForFunction(()=>document.getElementById('plResultSummary').textContent.startsWith('0'));
  assert.equal(await page.locator('.pl-quantity').inputValue(),'3');
  await page.locator('#plTemplateSettings > summary').click();await page.locator('#plPrinterSettings > summary').click();
  await page.locator('#plDesign [name="columns"]').fill('3');await page.locator('#plSavePrinter').click();await page.waitForFunction(()=>document.getElementById('plMessage').textContent.includes('vượt vùng in'));assert.equal(await page.locator('#plPrint').isDisabled(),true);
  await page.locator('#plDesign [name="columns"]').fill('2');await page.locator('#plSavePrinter').click();
  await page.locator('#plTemplateSettings > summary').click();await page.locator('#plPrinterSettings > summary').click();
  await page.locator('#plPaperPreset').selectOption('80x50');assert.equal(await page.locator('#plDesign [name="widthMm"]').inputValue(),'80');assert.equal(await page.locator('#plDesign [name="heightMm"]').inputValue(),'50');assert.equal(await page.locator('#plDesign [name="columns"]').inputValue(),'1');
  await page.locator('#plCopyTemplate').click();assert.equal(await page.locator('#plTemplate option').count(),3);
  await page.locator('#plTestPrint').click();await page.waitForFunction(()=>document.getElementById('plMessage').textContent.startsWith('Đã gửi 1 tem'));
  assert.equal(jobs.at(-1).payload.design.widthMm,80);assert.equal(jobs.at(-1).payload.design.heightMm,50);
  await page.locator('#plTemplateSettings > summary').click();const original=await page.locator('#plTemplate option').nth(1).getAttribute('value');await page.locator('#plTemplate').selectOption(original);
  assert.equal(await page.locator('#plPaperSize').textContent(),'50 × 30 mm');
  lost=true;await page.locator('#plPrint').click();await page.locator('#plUncertain').waitFor();assert.equal(await page.locator('#plPrint').isDisabled(),true);
  const count=jobs.length;await page.reload();await page.locator('[data-add="1"]').waitFor();await page.locator('#plUncertain').waitFor();assert.equal(jobs.length,count,'No automatic replay on reload');
  await page.locator('#plCheckResult').click();assert.equal(await page.locator('#plUncertain').isVisible(),true);
  resultKnown=true;await page.locator('#plCheckResult').click();await page.locator('#plUncertain').waitFor({state:'hidden'});lost=false;
  for(const width of [1440,736,390,320]){await page.setViewportSize({width,height:1100});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'Overflow '+width);await page.screenshot({path:path.join(output,'page-'+width+'.png'),fullPage:true});}
  assert.equal(requests.some(r=>r.path.endsWith('/jobs')),false);assert.deepEqual(errors,[]);
  console.log('PASS: native client printing, template import/save, preview, two-column calibration, test row, no print dialog/QZ/server jobs, stale price guard, uncertain result/reload guard and responsive layouts.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
