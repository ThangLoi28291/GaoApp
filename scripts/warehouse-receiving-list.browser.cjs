// Real receiving-list script with simulated read responses; no business data is written.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),assert=require('node:assert/strict');
const {createRequire}=require('node:module');
const {chromium}=createRequire(path.resolve(__dirname,'../GaoApp.Tests.Browser/package.json'))('playwright');
const root=path.resolve(__dirname,'..'),out=path.join(root,'Logs/receiving-list-20261012');fs.mkdirSync(out,{recursive:true});
let mode='ok',delay=150,reads=0,writes=0;const results=[];
const rows=[{id:1,documentNo:'NK-TEST-001',documentTitle:'Phiếu thử tải danh sách',status:1,warehouseName:'Kho thử',legalEntityName:'HKD thử',totalLines:2,totalProductTypes:1,documentDate:'2026-10-12T03:00:00Z'}];
const html='<!doctype html><html lang="vi"><head><meta charset="utf-8"><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><link rel="stylesheet" href="/Admin/css/warehouse-receiving.css"></head><body><div data-warehouse-receiving-index><button id="wrBtnReload">Tải lại</button><input id="wrSearch"><div id="wrLoadStatus" role="status" hidden></div><div id="wrReceiptList"></div><div id="wrPageInfo"></div><button id="wrBtnPrevPage">Trước</button><span id="wrCurrentPageText"></span><button id="wrBtnNextPage">Sau</button><span id="wrDraftCount"></span><span id="wrPendingCount"></span><span id="wrRejectedCount"></span></div><script src="/Admin/js/warehouse-receiving.js"></script></body></html>';
const server=http.createServer(async(req,res)=>{
 const url=new URL(req.url,'http://localhost');if(req.method!=='GET'){writes++;res.statusCode=405;return res.end();}
 if(url.pathname==='/'){res.setHeader('Content-Type','text/html; charset=utf-8');return res.end(html);}
 if(url.pathname==='/admin/warehouse-receiving/receipts'){
  reads++;const reply=mode,wait=delay;await new Promise(resolve=>setTimeout(resolve,wait));
  if(reply==='html'){res.setHeader('Content-Type','text/html');return res.end('<html>login page</html>');}
  res.setHeader('Content-Type','application/json');res.statusCode=reply==='error'?503:200;return res.end(JSON.stringify(reply==='error'?{message:'database diagnostic should not be displayed'}:rows));
 }
 const file=path.resolve(root,'GaoApp.Web/wwwroot',url.pathname.slice(1));if(!file.startsWith(path.join(root,'GaoApp.Web/wwwroot')+path.sep)||!fs.existsSync(file)){res.statusCode=404;return res.end();}
 res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':'text/css');res.end(fs.readFileSync(file));
});
async function test(name,fn){try{await fn();results.push({name,passed:true});console.log('PASS '+name);}catch(error){results.push({name,passed:false,error:error.message});console.error('FAIL '+name+' '+error.message);}}
(async()=>{
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
  const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{const native=setTimeout;window.setTimeout=(fn,ms,...args)=>native(fn,ms===30000&&window.testFastDeadline?25:ms,...args);});
  const origin='http://127.0.0.1:'+server.address().port;
  await test('initial load and repeated reloads share one pending read',async()=>{delay=600;await page.goto(origin);assert.equal(await page.locator('#wrBtnReload').isDisabled(),true);await page.evaluate(()=>{void loadWarehouseReceivingReceipts();void loadWarehouseReceivingReceipts();});await page.locator('.wr-card').waitFor();assert.equal(reads,1);assert.equal(await page.locator('#wrBtnReload').isEnabled(),true);assert.equal(await page.locator('#wrReceiptList').getAttribute('aria-busy'),'false');delay=0;});
  await test('failed refresh keeps existing receipts and provides retry',async()=>{mode='error';await page.locator('#wrBtnReload').click();await page.getByRole('button',{name:'Thử lại',exact:true}).waitFor();assert.equal(await page.locator('.wr-card').count(),1);assert.match(await page.locator('#wrLoadStatus').textContent(),/đã tải trước đó/);assert.doesNotMatch(await page.locator('#wrLoadStatus').textContent(),/database diagnostic/);mode='ok';await page.getByRole('button',{name:'Thử lại',exact:true}).click();await page.locator('#wrLoadStatus').waitFor({state:'hidden'});assert.equal(await page.locator('.wr-card').count(),1);});
  await test('a non-JSON success response never replaces the receipt list',async()=>{mode='html';await page.locator('#wrBtnReload').click();await page.getByRole('button',{name:'Thử lại',exact:true}).waitFor();assert.equal(await page.locator('.wr-card').count(),1);mode='ok';await page.getByRole('button',{name:'Thử lại',exact:true}).click();await page.locator('#wrLoadStatus').waitFor({state:'hidden'});});
  await test('network failure keeps receipts and allows recovery',async()=>{await page.route('**/admin/warehouse-receiving/receipts',route=>route.abort('failed'));await page.locator('#wrBtnReload').click();await page.getByRole('button',{name:'Thử lại',exact:true}).waitFor();assert.equal(await page.locator('.wr-card').count(),1);await page.unroute('**/admin/warehouse-receiving/receipts');await page.getByRole('button',{name:'Thử lại',exact:true}).click();await page.locator('#wrLoadStatus').waitFor({state:'hidden'});});
  await test('slow read is cancelled once and can be retried without clearing receipts',async()=>{delay=300;await page.evaluate(()=>window.testFastDeadline=true);const before=reads;await page.locator('#wrBtnReload').click();await page.getByRole('button',{name:'Thử lại',exact:true}).waitFor();assert.match(await page.locator('#wrLoadStatus').textContent(),/mất quá lâu/);assert.equal(await page.locator('.wr-card').count(),1);assert.equal(reads,before+1);delay=0;await page.evaluate(()=>window.testFastDeadline=false);await page.getByRole('button',{name:'Thử lại',exact:true}).click();await page.locator('#wrLoadStatus').waitFor({state:'hidden'});});
  await test('first-load failure shows recovery instead of a broken or empty data result',async()=>{mode='error';await page.reload();await page.getByRole('button',{name:'Thử lại',exact:true}).waitFor();assert.equal(await page.locator('.wr-card').count(),0);assert.match(await page.locator('#wrReceiptList').textContent(),/Chưa tải được/);mode='ok';await page.getByRole('button',{name:'Thử lại',exact:true}).click();await page.locator('.wr-card').waitFor();});
  assert.equal(writes,0);assert.deepEqual(errors,[]);fs.writeFileSync(path.join(out,'browser-results.json'),JSON.stringify({businessWrites:writes,cases:results},null,2));process.exitCode=results.some(r=>!r.passed)?1:0;
 }finally{await browser.close();server.close();}
})().catch(error=>{console.error(error);server.close();process.exitCode=1;});
