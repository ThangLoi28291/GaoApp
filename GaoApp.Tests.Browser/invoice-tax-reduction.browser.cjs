'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s=''; process.stdin.on('data',v=>s+=v); process.stdin.on('end',()=>resolve(s)); }));
    const browser = await chromium.launch({channel:'chrome',headless:true}); const errors=[], checked=[];
    const context = await browser.newContext({ viewport:{width:1440,height:1000}, reducedMotion:'reduce', serviceWorkers:'block' });
    try {
        const origin = new URL(info.baseUrl).origin;
        await context.route('**/*', route => new URL(route.request().url()).origin === origin || /^(data|blob|about):/.test(route.request().url()) ? route.continue() : route.abort());
        const page = await context.newPage(); page.setDefaultTimeout(30000); page.on('pageerror',e=>errors.push(e.message));
        await page.goto(info.baseUrl+'/admin/account/login'); await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('button[type=submit]').click()]);
        const panel = page.locator('[data-invoice-tax-relief]');
        const enabled = panel.locator('[data-tax-enabled]'), rate=panel.locator('[data-tax-percentage]');
        const save = async () => { await Promise.all([page.waitForNavigation(),panel.locator('button[type=submit]').click()]); await panel.waitFor(); };
        const money = async selector => (await panel.locator(selector).innerText()).replace(/\D/g,'');
        const shot = async (name,width,height=1000) => {
            await page.setViewportSize({width,height}); await panel.scrollIntoViewIfNeeded();
            await page.screenshot({path:path.join(info.output,name+'.png'),animations:'disabled'});
            const metrics=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,ids:[...document.querySelectorAll('[id]')].map(n=>n.id)}));
            assert.ok(metrics.scroll<=metrics.width+1,JSON.stringify(metrics));
            assert.equal(metrics.ids.length,new Set(metrics.ids).size); checked.push({name,width});
        };
        await page.goto(info.baseUrl+'/Admin/Invoice/Detail/'+info.id); await panel.waitFor();
        assert.equal(await enabled.isChecked(),false); await enabled.check(); await rate.selectOption('1');
        assert.equal(await money('[data-tax-reduction]'),'2000'); assert.equal(await money('[data-tax-total]'),'998000');
        assert.equal(await panel.locator('[data-tax-unsaved]').isVisible(),true);
        await shot('desktop-preview',1440); await save(); assert.equal(await enabled.isChecked(),true);
        await page.goto(info.baseUrl+'/Admin/Invoice/ViettelPayload/'+info.id); await panel.waitFor();
        let payload=JSON.parse(await page.locator('#payloadJson').innerText());
        assert.equal(payload.generalInvoiceInfo.adjustAmount20,1); assert.equal(payload.summarizeInfo.totalAmountWithTax,998000);
        await rate.selectOption('2');
        const preview=page.locator('a[data-tax-dependent]'); if(await preview.count()) assert.equal(await preview.first().getAttribute('aria-disabled'),'true');
        await save(); payload=JSON.parse(await page.locator('#payloadJson').innerText());
        assert.equal(payload.generalInvoiceInfo.adjustAmount20,2); assert.equal(payload.summarizeInfo.totalAmountWithTax,996000);
        await shot('desktop-saved',1440);
        await page.setViewportSize({width:390,height:844}); await rate.selectOption('5');
        assert.equal(await money('[data-tax-total]'),'990000'); await shot('phone-preview',390,844); await save();
        await shot('phone-saved',390,844); await shot('phone-small',320,740); await shot('tablet',768,1024);
        await enabled.uncheck(); await save(); payload=JSON.parse(await page.locator('#payloadJson').innerText());
        assert.equal(Object.hasOwn(payload.generalInvoiceInfo,'adjustAmount20'),false); assert.equal(payload.summarizeInfo.totalAmountWithTax,1000000);
        await enabled.check(); await rate.selectOption('1'); await save();
        assert.deepEqual(errors,[]); fs.writeFileSync(path.join(info.output,'result.json'),JSON.stringify({passed:true,errors,checked},null,2));
        console.log('PASS Native desktop/phone tax choice, exact amounts, persistence, unsaved guard, removal and SInvoice API flag; '+checked.length+' viewport checks');
    } finally { await context.close(); await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
