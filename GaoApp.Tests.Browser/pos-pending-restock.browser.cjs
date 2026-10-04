'use strict';
// HTML rendered by the real controller in POSReturnsSqlServerTests; synthetic writes only.
const {chromium}=require('playwright');
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const web=path.resolve(__dirname,'../GaoApp.Web'),output=path.resolve(__dirname,'../TestResults/pos-returns'),origin='https://gao-pending.test';
(async()=>{
    const browser=await chromium.launch({channel:'chrome',headless:true});
    try {
        const context=await browser.newContext({viewport:{width:1360,height:900}}),page=await context.newPage();
        const errors=[],posts=[];page.on('pageerror',e=>errors.push(e.message));
        const html=async name=>{
            const source=fs.readFileSync(path.join(output,name),'utf8');
            const fragment=await page.evaluate(source=>{
                const doc=new DOMParser().parseFromString(source,'text/html');
                doc.querySelector('input[name="__RequestVerificationToken"]').value='synthetic-token';
                return ['#pendingRestockCount','input[name="__RequestVerificationToken"]','#pendingRestockMessage','.card[data-return-id]']
                    .flatMap(selector=>Array.from(doc.querySelectorAll(selector)).map(node=>selector==='#pendingRestockCount'?node.closest('h3').outerHTML:node.outerHTML)).join('');
            },source);
            return `<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><body style="padding:24px;background:#f4f5fa">${fragment}<script src="/Admin/js/pos/pos.pending.restock.page.js"></script></body></html>`;
        };
        let document=await html('pending-restock-blocked.html'),reject=false;
        await context.route(origin+'/**',async route=>{
            const req=route.request(),p=new URL(req.url()).pathname;
            if(p.startsWith('/lib/')||p.startsWith('/Admin/')) return route.fulfill({contentType:p.endsWith('.css')?'text/css':'text/javascript',body:fs.readFileSync(path.join(web,'wwwroot',p),'utf8')});
            if(req.method()==='POST'){
                assert.match(p,/^\/admin\/pos\/returns\/\d+\/complete-restock$/);assert.equal(req.headers().requestverificationtoken,'synthetic-token');posts.push(p);
                await new Promise(resolve=>setTimeout(resolve,150));
                return route.fulfill({status:reject?400:200,contentType:'application/json',body:JSON.stringify(reject?
                    {success:false,message:'Giá vốn còn tạm tính.',actionHint:'Quản lý cần xử lý giá vốn rồi tải lại.'}:
                    {success:true,message:'Đã hoàn tất nhập kho. Không phát sinh thêm tiền hoàn.'})});
            }
            return route.fulfill({contentType:'text/html',body:document});
        });
        await page.goto(origin+'/admin/pos/returns/pending-restock');
        assert.equal(await page.locator('.js-complete-restock').isDisabled(),true);assert.equal(posts.length,0);
        assert.match(await page.locator('.card').innerText(),/tạm tính/);
        await page.screenshot({path:path.join(output,'pending-restock-blocked.png')});
        document=await html('pending-restock-ready.html');await page.reload();
        assert.equal(await page.locator('.js-complete-restock').isDisabled(),false);
        await page.screenshot({path:path.join(output,'pending-restock-ready.png')});
        await page.locator('.js-complete-restock').evaluate(button=>{button.click();button.click();});
        await page.locator('.card[data-return-id]').waitFor({state:'detached'});assert.equal(posts.length,1);
        assert.match(await page.locator('#pendingRestockMessage').innerText(),/Không phát sinh thêm tiền hoàn/);
        assert.equal(await page.locator('#pendingRestockCount').innerText(),'0 phiếu');
        await page.reload();reject=true;await page.locator('.js-complete-restock').click();
        await page.waitForFunction(()=>document.querySelector('#pendingRestockMessage').textContent.includes('tạm tính'));
        assert.equal(await page.locator('.card[data-return-id]').count(),1);assert.equal(await page.locator('.js-complete-restock').isDisabled(),false);
        assert.match(await page.locator('#pendingRestockMessage').innerText(),/Quản lý/);
        await page.setViewportSize({width:390,height:844});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
        await page.screenshot({path:path.join(output,'pending-restock-mobile.png')});
        reject=false;await page.locator('.js-complete-restock').click();await page.locator('.card[data-return-id]').waitFor({state:'detached'});
        assert.equal(posts.length,3);assert.deepEqual(errors,[]);
        console.log('PASS: real rendered queue, readiness, antiforgery, one submit, whole card removal, accurate count, state conflict retry and mobile layout.');
    } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
