'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const{chromium}=require('playwright');
(async()=>{
    const info=JSON.parse(await new Promise(resolve=>{let input='';process.stdin.on('data',v=>input+=v);process.stdin.on('end',()=>resolve(input));}));
    const browser=await chromium.launch({channel:'chrome',headless:true});
    const errors=[],external=[],alerts=[],posts=[];const contexts=[];
    try{
        const context=await browser.newContext({viewport:{width:1440,height:1000},reducedMotion:'reduce',timezoneId:'America/Los_Angeles',serviceWorkers:'block'});contexts.push(context);
        const origin=new URL(info.baseUrl).origin;
        await context.route('**/*',route=>{const u=new URL(route.request().url());if(u.origin===origin||['data:','blob:','about:'].includes(u.protocol))return route.continue();external.push(u.origin);return route.abort();});
        const page=await context.newPage();page.setDefaultTimeout(30000);page.on('pageerror',e=>errors.push(e.message));
        page.on('dialog',async dialog=>{alerts.push(dialog.message());await dialog.dismiss();});
        await page.goto(info.baseUrl+'/admin/account/login');await page.locator('[name=UserName]').fill(info.user);await page.locator('[name=Password]').fill(info.password);
        await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('button[type=submit]').click()]);
        page.on('request',r=>{if(r.method()==='POST'&&!/\/admin\/api\/store-activity\//.test(new URL(r.url()).pathname))posts.push(new URL(r.url()).pathname);});
        const modal=page.locator('#manualDetailModal'),input=page.locator('#manualProductSearch'),menu=page.locator('#manualProductAutocomplete');
        async function visit(){await page.goto(info.baseUrl+'/Admin/Invoice/Detail/'+info.invoiceId);await page.locator('#invoiceDetailPage').waitFor();}
        async function open(){await page.keyboard.press('F2');await modal.waitFor({state:'visible'});await input.waitFor({state:'visible'});}
        async function close(){await modal.locator('.btn-close').first().click();await modal.waitFor({state:'hidden'});}
        async function search(term='hoa'){
            const reply=page.waitForResponse(r=>new URL(r.url()).pathname.toLowerCase().endsWith('/invoice/searchproductvariants')&&new URL(r.url()).searchParams.get('keyword')===term);
            await input.fill(term);const response=await reply;await menu.locator('[data-invoice-product-index]').first().waitFor();return response.json();
        }
        async function choose(){await input.press('Enter');await page.waitForFunction(id=>document.getElementById('manualProductVariantId')?.value===String(id),info.positiveId);}
        const settle=async()=>{await page.evaluate(()=>document.fonts.ready);await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));};
        await visit();await open();let result=await search();assert.equal(result.stockFilterApplied,true);assert.equal(result.items.length,2);
        assert.equal(result.items[0].productVariantId,info.positiveId);assert.equal(result.items[0].availableQuantity,12);assert.equal(result.items[0].maximumAdditionalQuantity,10);
        assert.ok(result.items.some(x=>x.productVariantId===info.fractionalId&&x.availableQuantity===.5));
        assert.doesNotMatch(await menu.innerText(),/Hoa hết tồn/);assert.match(await menu.innerText(),/Còn khả dụng: 12 Cái/);assert.match(await menu.innerText(),/Có thể thêm:\s*10/);
        const widths=[];
        for(const width of [1440,320,390,430]){
            await page.setViewportSize({width,height:900});await settle();
            assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false,`Page overflow ${width}`);
            const box=await menu.boundingBox();assert.ok(box.x>=-1&&box.x+box.width<=width+1,`Picker overflow ${width}`);
            await page.screenshot({path:path.join(info.output,`picker-${width}.png`),animations:'disabled'});widths.push(width);
        }
        await page.setViewportSize({width:1440,height:1000});await choose();assert.match(await page.locator('#manualProductStock').innerText(),/Còn khả dụng: 12 Cái/);
        assert.equal(await page.locator('#manualUnitName').inputValue(),'Cái');
        await page.locator('#manualQuantity').fill('11');await page.locator('#manualDetailForm button[type=submit]').click();
        assert.match(alerts.at(-1),/tối đa 10/);assert.equal(posts.length,0);
        // Direct, authenticated rejected command verifies that this rule is not a UI-only filter.
        const rejected=await page.evaluate(async()=>{const form=document.getElementById('manualDetailForm'),body=new FormData(form);body.set('Quantity','11');const r=await fetch(form.action,{method:'POST',body,headers:{'X-Requested-With':'XMLHttpRequest'}});return{status:r.status,body:await r.json()};});
        assert.equal(rejected.status,400);assert.match(rejected.body.message,/khả dụng|tối đa/);
        await page.locator('#manualQuantity').fill('1');
        await page.locator('#manualUnitPrice').fill('28005');
        await context.route('**/Invoice/CreateManualDetail',route=>route.fulfill({status:400,json:{success:false,message:'Tồn khả dụng đã thay đổi.'}}));
        await context.route('**/Invoice/ProductStock?**',route=>route.fulfill({json:{...result.items[0],availableQuantity:4,maximumAdditionalQuantity:2,unitPrice:99999}}));
        await page.locator('#manualDetailForm button[type=submit]').click();
        await page.waitForFunction(()=>document.getElementById('manualProductStock')?.textContent.includes('Còn khả dụng: 4'));
        assert.equal(await page.locator('#manualUnitPrice').inputValue(),'28005');
        assert.equal(await page.locator('#manualQuantity').inputValue(),'1');
        await context.unroute('**/Invoice/CreateManualDetail');await context.unroute('**/Invoice/ProductStock?**');
        await search();await choose();
        const saved=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname.toLowerCase().endsWith('/invoice/createmanualdetail'));
        await page.locator('#manualDetailForm button[type=submit]').click();assert.equal((await saved).status(),200);await modal.waitFor({state:'hidden'});
        await visit();await open();result=await search();assert.equal(result.items[0].alreadyOnInvoiceQuantity,3);assert.equal(result.items[0].maximumAdditionalQuantity,9);
        await choose();await input.fill('hoa');assert.equal(await page.locator('#manualProductVariantId').inputValue(),'');assert.equal(await page.locator('#manualItemName').inputValue(),'');
        await menu.locator('[data-invoice-product-index]').first().waitFor();
        // A superseded, delayed search must neither reopen old results nor restore an old selected ID.
        let release,seen;const paused=new Promise(r=>release=r),seenPromise=new Promise(r=>seen=r);
        await context.route('**/Invoice/SearchProductVariants?**',async route=>{if(new URL(route.request().url()).searchParams.get('keyword')!=='ho')return route.continue();seen();await paused;try{await route.fulfill({json:{stockFilterApplied:true,items:[{productVariantId:info.positiveId,displayName:'STALE_<script>',availableQuantity:999,maximumAdditionalQuantity:999}]}});}catch{}});
        await input.fill('ho');await seenPromise;result=await search();release();await settle();assert.doesNotMatch(await menu.innerText(),/STALE_/);
        await context.unroute('**/Invoice/SearchProductVariants?**');
        await context.route('**/Invoice/SearchProductVariants?**',route=>route.fulfill({status:503,contentType:'application/json',body:'{}'}));
        await input.fill('hoa lỗi');await menu.locator('[role=status]').waitFor();assert.match(await menu.innerText(),/Không tải được/);assert.equal(await menu.locator('[data-invoice-product-index]').count(),0);
        await context.unroute('**/Invoice/SearchProductVariants?**');await search();
        // A selected product that is no longer available is not silently accepted.
        await context.route('**/Invoice/ProductStock?**',route=>route.fulfill({json:{productVariantId:info.positiveId,availableQuantity:0,maximumAdditionalQuantity:0}}));
        await input.press('Enter');await page.waitForFunction(()=>document.getElementById('manualProductStock')?.dataset.kind==='error');assert.equal(await page.locator('#manualProductVariantId').inputValue(),'');
        await context.unroute('**/Invoice/ProductStock?**');await search();await choose();
        await page.setViewportSize({width:390,height:844});await page.locator('#manualProductStock').scrollIntoViewIfNeeded();await settle();
        await page.screenshot({path:path.join(info.output,'selected-phone.png'),animations:'disabled'});
        await close();await open();
        await context.route('**/Invoice/SearchProductVariants?**',route=>route.fulfill({status:403,contentType:'application/json',body:'{}'}));
        await input.fill('hoa');await menu.locator('[role=status]').waitFor();assert.match(await menu.innerText(),/quyền đã thay đổi/);assert.equal(await menu.locator('[data-invoice-product-index]').count(),0);
        await context.unroute('**/Invoice/SearchProductVariants?**');await close();
        // Correction invoices retain the catalog, including a zero-stock item.
        const correction=await page.evaluate(async id=>{const r=await fetch('/Admin/Invoice/SearchProductVariants?invoiceHeadId='+id+'&keyword=hoa');return{status:r.status,data:await r.json()};},info.correctionId);
        assert.equal(correction.status,200);assert.equal(correction.data.stockFilterApplied,false);assert.ok(correction.data.items.some(x=>x.productVariantId===info.emptyId&&x.availableQuantity===null));
        assert.deepEqual(errors,[]);assert.deepEqual(external,[]);assert.ok(posts.every(p=>p.toLowerCase()==='/admin/invoice/createmanualdetail'));
        const proof={passed:true,widths,positiveOnly:true,currentQuantityShown:true,baseUnitAndWarehouseShown:true,existingInvoiceQuantityDeducted:true,serverRejectsOverQuantity:true,
            successfulManualAdds:1,typedSearchClearsSelection:true,staleSearchIgnored:true,outOfStockSelectionRejected:true,serverFailureShown:true,permissionChangeClearsResults:true,
            failedSaveRefreshesStockWithoutOverwritingInput:true,correctionsKeepCatalog:true,pageErrors:errors,externalRequests:external,posts,alerts,existingChromeWindowsPreserved:true};
        fs.writeFileSync(path.join(info.output,'browser.json'),JSON.stringify(proof,null,2));console.log(JSON.stringify(proof));
    }finally{for(const context of contexts)await context.close();await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
