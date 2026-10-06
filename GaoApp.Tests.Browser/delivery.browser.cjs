const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text=''; process.stdin.on('data', c=>text+=c); process.stdin.on('end',()=>resolve(text)); }));
    const browser = await chromium.launch({channel:'chrome',headless:true});
    const evidence = info.evidenceDirectory || path.join(process.cwd(),'TestResults','delivery'); fs.mkdirSync(evidence,{recursive:true});
    const context = await browser.newContext({viewport:{width:1440,height:1000}}); const page=await context.newPage();
    const errors=[],checks=[],requests=[]; page.on('pageerror',e=>errors.push(e.message));
    async function post(url,body) { const r=await page.evaluate(async ({url,body})=>{const r=await fetch(url,{method:'POST',headers:{'Content-Type':'application/json',RequestVerificationToken:document.querySelector('meta[name="request-verification-token"]').content},body:body==null?null:JSON.stringify(body)});return {status:r.status,text:await r.text()};},{url,body});assert.equal(r.status,200,url+': '+r.text);return JSON.parse(r.text); }
    async function pos() { await page.goto(info.baseUrl+'/admin/pos'); await page.waitForFunction(()=>!!window.posApp); }
    async function openDelivery() { await page.evaluate(()=>window.posApp.modules.posPayment.openPaymentModal()); await page.locator('#btnOpenDelivery').click(); await page.locator('#deliveryModal.show').waitFor(); }
    async function fill() { await page.locator('#deliverySubmit:not([disabled])').waitFor(); await page.locator('#deliveryName').fill('Nguyễn An <script>alert(1)</script>');await page.locator('#deliveryPhone').fill('0901234567');await page.locator('#deliveryAddress').fill('123 Nguyễn Văn Cừ, phường thử nghiệm, Việt Nam');await page.locator('#deliveryNote').fill('Gọi trước khi giao. <img src=x onerror=alert(1)>'); }
    try {
        await page.goto(info.baseUrl+'/admin/account/login'); await page.locator('[name=UserName]').fill(info.user);await page.locator('[name=Password]').fill(info.password);await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('#loginForm button[type=submit]').click()]);
        await post('/admin/pos/shift/open',{openingCash:0,warehouseId:info.warehouseId});const draft=await post('/admin/pos/draft');await post(`/admin/pos/${draft.orderId}/items?variantId=${info.variantId}&qty=2`);
        await pos();
        // Real HTTP failure before commit must not produce a printable success.
        await page.route('**/admin/api/deliveries/from-current-cart', async route=>{const body=route.request().postDataJSON();body.expectedFingerprint='A'.repeat(64);const response=await route.fetch({postData:JSON.stringify(body)});assert.equal(response.status(),409);await route.fulfill({response});});
        await openDelivery(); await fill();await page.locator('#deliverySubmit').click();await page.waitForFunction(()=>document.querySelector('#deliveryMessage').textContent.includes('Giỏ đã đổi'));
        assert.equal(await page.locator('#deliveryResult').isVisible(),false);checks.push('Server conflict never exposes a success bill');await page.unroute('**/admin/api/deliveries/from-current-cart');
        await page.locator('#deliveryRefresh').click();await fill();await page.keyboard.press('F4');assert.equal(await page.locator('#deliveryModal.show').count(),1);checks.push('POS finalize hotkey isolated while recipient form is open');
        // Server commits, response gets lost. Reload must retain the exact body and clientRequestId.
        await page.route('**/admin/api/deliveries/from-current-cart',async route=>{requests.push(route.request().postDataJSON());const response=await route.fetch();assert.equal(response.status(),200);await route.abort('failed');});
        await page.locator('#deliverySubmit').click();await page.waitForFunction(()=>document.querySelector('#deliveryMessage').textContent.includes('Chưa xác nhận'));
        assert.equal(await page.locator('#deliveryResult').isVisible(),false);await page.unroute('**/admin/api/deliveries/from-current-cart');await pos();await openDelivery();
        await page.waitForFunction(()=>document.querySelector('#deliverySubmit').textContent.includes('yêu cầu cũ'));assert.equal(await page.locator('#deliveryName').isDisabled(),true);
        await page.route('**/admin/api/deliveries/from-current-cart',async route=>{requests.push(route.request().postDataJSON());await route.continue();});await page.locator('#deliverySubmit').click();await page.locator('#deliveryResult').waitFor({state:'visible'});assert.deepEqual(requests[0],requests[1]);checks.push('Committed response lost then reload replays exact intent once');
        await page.screenshot({path:path.join(evidence,'pos-delivery-success.png'),fullPage:true});const billPath=await page.locator('#deliveryBillLink').getAttribute('href');const lookupPath=await page.locator('#deliveryLookupLink').getAttribute('href');
        const bill=await context.newPage();bill.on('pageerror',e=>errors.push(e.message));await bill.goto(info.baseUrl+billPath);await bill.locator('#deliveryQr').waitFor();
        assert.match(await bill.locator('.delivery-recipient').innerText(),/<script>alert\(1\)<\/script>/);assert.equal(await bill.locator('.delivery-recipient script').count(),0);
        assert.equal(await bill.locator('script[src*="pos.printing"],script[src*="qz"]').count(),0);assert.equal(await bill.locator('#deliveryPrint').innerText(),'In phiếu A5');
        await bill.evaluate(()=>{window.__printCalls=0;window.print=()=>window.__printCalls++;});await bill.locator('#deliveryPrint').click();assert.equal(await bill.evaluate(()=>window.__printCalls),1);
        await bill.screenshot({path:path.join(evidence,'bill-a5.png'),fullPage:true});await bill.pdf({path:path.join(evidence,'bill-a5.pdf'),preferCSSPageSize:true,printBackground:true});checks.push('Real A5 bill escapes user text and browser print has no POS printer or drawer hooks');
        await bill.goto(info.baseUrl+lookupPath);await bill.locator('#deliveryDetail:not([hidden])').waitFor();assert.match(await bill.locator('#deliveryDetail').innerText(),/Chờ soạn/);const code=await bill.locator('#deliveryDetail h3').innerText();await bill.locator('#deliveryKey').fill(code);await bill.locator('#deliverySearch button').click();await bill.locator('#deliveryDetail:not([hidden])').waitFor();
        await bill.setViewportSize({width:390,height:844});await bill.screenshot({path:path.join(evidence,'lookup-mobile.png'),fullPage:true});assert.equal(await bill.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);checks.push('Authenticated token and manual code lookup; mobile layout');
        const secondary=await context.newPage();await secondary.goto(info.baseUrl+'/admin/pos');await secondary.waitForFunction(()=>window.PosOffline?.status()?.context && !window.PosOffline.status().writer && window.PosOffline.status().message.includes('tab khác'));
        await secondary.locator('#btnOpenDelivery').evaluate(e=>e.click());await secondary.waitForFunction(()=>document.querySelector('#deliveryMessage').textContent.includes('tab khác'));
        assert.equal(await secondary.locator('#deliverySubmit').isDisabled(),true);await secondary.close();checks.push('Second POS tab cannot create delivery while another tab owns the terminal');
        // Long order uses real source lines, not a forged bill fixture.
        await page.locator('#deliveryModal [data-bs-dismiss]').click();const second=await post('/admin/pos/draft');for(const id of info.longVariantIds) await post(`/admin/pos/${second.orderId}/items?variantId=${id}&qty=1`);
        await pos();await openDelivery();await fill();await context.setOffline(true);await page.locator('#deliverySubmit').click();await page.waitForFunction(()=>document.querySelector('#deliveryMessage').textContent.includes('Cần có mạng'));assert.equal(await page.locator('#deliveryResult').isVisible(),false);await context.setOffline(false);checks.push('Offline create blocked, no delivery queue');
        await page.locator('#deliverySubmit').click();await page.locator('#deliveryResult').waitFor({state:'visible'});const longBillPath=await page.locator('#deliveryBillLink').getAttribute('href');await bill.goto(info.baseUrl+longBillPath);assert.equal(await bill.locator('tbody tr').count(),28);
        await bill.emulateMedia({media:'print'});await bill.pdf({path:path.join(evidence,'bill-a5-long.pdf'),preferCSSPageSize:true,printBackground:true});await bill.screenshot({path:path.join(evidence,'bill-a5-long.png'),fullPage:true});checks.push('28 real source lines render and flow on A5 pages');
        assert.deepEqual(errors,[]);fs.writeFileSync(path.join(evidence,'browser-checks.json'),JSON.stringify({checks,errors,replayedSameRequest:true},null,2));console.log('DELIVERY BROWSER PASS: '+checks.join('; '));
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
