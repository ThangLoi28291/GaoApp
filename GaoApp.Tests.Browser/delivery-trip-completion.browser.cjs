'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
(async()=> {
    const info=JSON.parse(await new Promise(resolve=> {let input='';process.stdin.on('data',value=>input+=value);process.stdin.on('end',()=>resolve(input));}));
    const browser=await chromium.launch({channel:'chrome',headless:true});
    try {
        const page=await browser.newPage({viewport:{width:1440,height:1000}});
        await require('./delivery-mobile-review.cjs').protect(page, info.baseUrl);
        const errors=[]; page.on('pageerror',error=>errors.push(error.message));
        let posts=0; page.on('request',request=> {if(request.url().includes('/admin/api/delivery-trips')&&request.method()==='POST')posts++;});
        await page.goto(info.baseUrl+'/admin/account/login');
        await page.locator('[name="UserName"]').fill(info.user); await page.locator('[name="Password"]').fill(info.password);
        await page.locator('[name="SelectedTerminalId"]').selectOption(String(info.terminalId));
        await page.locator('#loginForm button[type="submit"]').click();
        await page.waitForURL(url=>!url.pathname.toLowerCase().includes('/account/login'));
        await page.goto(info.baseUrl+'/admin/deliveries/trips?trip='+info.tripId);
        const selected=page.locator(`[data-trip-id="${info.tripId}"].delivery-trip-list-item`);
        const panel=page.locator('#deliveryTripDetail');
        await page.waitForFunction(()=>document.querySelector('#deliveryTripDetail .state-Completed')?.textContent==='Đã hoàn thành');
        assert.match(await selected.textContent(),/Đã hoàn thành/);
        assert.doesNotMatch(await panel.textContent(),/Hàng và tiền còn chờ đối chiếu/);
        assert.match(await panel.textContent(),/Đã chốt hàng và tiền của toàn bộ chuyến/);
        assert.equal(await page.locator('#deliveryTripElapsed').count(),0);
        assert.equal(await panel.locator('.delivery-trip-order .delivery-trip-state').first().textContent(),'Đã chốt');
        const filtered=page.waitForResponse(response=>response.url().includes('state=Completed')&&response.status()===200);
        await page.locator('#deliveryTripState').selectOption('Completed');
        await filtered;
        assert.match(await selected.textContent(),/Đã hoàn thành/);
        fs.mkdirSync(info.evidenceRoot,{recursive:true});
        await page.screenshot({path:path.join(info.evidenceRoot,'completed-trip-desktop.png'),fullPage:true});
        await page.setViewportSize({width:390,height:844});
        await page.screenshot({path:path.join(info.evidenceRoot,'completed-trip-mobile.png'),fullPage:true});
        assert.deepEqual(errors,[]); assert.equal(posts,0);
        console.log('PASS: completed status in list/detail/filter on desktop/mobile, no pending reconciliation or business writes.');
    } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
