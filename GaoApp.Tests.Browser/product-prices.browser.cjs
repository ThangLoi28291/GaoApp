'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
(async()=>{
    const info=JSON.parse(await new Promise(resolve=>{let data='';process.stdin.on('data',chunk=>data+=chunk);process.stdin.on('end',()=>resolve(data));}));
    const browser=await chromium.launch({channel:'chrome',headless:true});
    const errors=[], output=process.env.GAO_PRODUCT_PRICES_OUTPUT;
    if(output)fs.mkdirSync(output,{recursive:true});
    try{
        async function login(user,password){
            const context=await browser.newContext({viewport:{width:1440,height:1050}}), page=await context.newPage();
            page.setDefaultTimeout(15000);page.on('pageerror',error=>errors.push(error.message));
            await page.goto(info.baseUrl+'/admin/account/login');
            await page.locator('[name=UserName]').fill(user);await page.locator('[name=Password]').fill(password);
            if(await page.locator('[name=SelectedTerminalId]').count())await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
            await Promise.all([page.waitForURL(url=>!url.pathname.endsWith('/login')),page.locator('button[type=submit]').click()]);
            await page.goto(info.baseUrl+'/Admin/Product?lifecycle=all');return page;
        }
        const staff=await login(info.employee,info.employeePassword),manager=await login(info.user,info.password);
        const supplier='NCC-PRIVATE-ONLY';
        assert.equal((await staff.content()).includes(supplier),false);
        assert.match(await staff.locator('.product-wholesale-price').first().textContent(),/9[.,]500/);
        await staff.locator('.gds-desktop-list [data-product-row] .js-quick-view').first().click();
        await staff.locator('#productQuickViewModal').waitFor({state:'visible'});
        assert.equal(await staff.locator('#productQuickViewSupplier').count(),0);
        assert.equal(await staff.locator('#productQuickViewUnits tbody tr').count(),3);
        const box=staff.locator('#productQuickViewUnits [data-unit-name="Hộp"]');
        const pack=staff.locator('#productQuickViewUnits [data-unit-name="Lốc"]');
        const carton=staff.locator('#productQuickViewUnits [data-unit-name="Thùng"]');
        assert.match(await box.textContent(),/12[.,]000.*9[.,]500/);
        assert.match(await pack.textContent(),/48[.,]000.*45[.,]000/);
        assert.match(await carton.textContent(),/280[.,]000.*280[.,]000.*Theo giá lẻ/);
        if(output)await staff.screenshot({path:path.join(output,'staff-popup-desktop.png'),animations:'disabled'});
        await staff.setViewportSize({width:390,height:844});
        assert.ok(await staff.locator('#productQuickViewModal .modal-content').evaluate(node=>node.scrollWidth<=node.clientWidth+1));
        await staff.locator('#productQuickViewUnits').scrollIntoViewIfNeeded();
        if(output)await staff.screenshot({path:path.join(output,'staff-popup-mobile.png'),animations:'disabled'});
        await staff.locator('#productQuickViewModal [data-bs-dismiss="modal"]').last().click();
        await staff.locator('#productQuickViewModal').waitFor({state:'hidden'});
        await staff.locator('#txtSearch').fill('Sữa tươi');
        await staff.waitForResponse(response=>response.url().includes('/Product/Search') && response.ok());
        assert.equal((await staff.content()).includes(supplier),false,'AJAX results must not embed supplier');
        await staff.locator('.gds-mobile-list .js-quick-view').first().click();
        await staff.locator('#productQuickViewModal').waitFor({state:'visible'});
        assert.equal(await staff.locator('#productQuickViewUnits tbody tr').count(),3,'Reopening replaces previous price rows');
        await manager.locator('.gds-desktop-list [data-product-row] .js-quick-view').first().click();
        await manager.locator('#productQuickViewModal').waitFor({state:'visible'});
        assert.equal(await manager.locator('#productQuickViewSupplier').textContent(),supplier);
        assert.equal(await manager.locator('#productQuickViewUnits tbody tr').count(),3);
        if(output)await manager.screenshot({path:path.join(output,'manager-popup-desktop.png'),animations:'disabled'});
        assert.deepEqual(errors,[]);
        console.log('PASS: supplier absent for employee (initial/AJAX HTML and popup), visible for manager; box/pack/carton retail-wholesale prices and fallback; reopen/search/mobile, no JS errors.');
    }catch(error){
        for(const [i,context] of browser.contexts().entries())if(output && context.pages()[0])await context.pages()[0].screenshot({path:path.join(output,`failure-${i}.png`)});
        throw error;
    }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
