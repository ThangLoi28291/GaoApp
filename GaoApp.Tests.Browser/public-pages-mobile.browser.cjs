'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', v => text += v); process.stdin.on('end', () => resolve(text)); }));
    fs.mkdirSync(info.output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const errors = [], states = [], desktop = [];
    const sizes = [[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[390,440]];
    const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
    const page = await context.newPage();
    const watch = p => p.on('pageerror', e => errors.push(e.message)); watch(page);
    const settle = async p => { await p.evaluate(async () => {
        await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
        await document.fonts.ready;
        await Promise.allSettled(document.getAnimations().filter(a => a.effect?.getComputedTiming().iterations !== Infinity).map(a => a.finished));
    }); };
    const image = async (p, label) => { await p.setViewportSize({width:390,height:844}); await settle(p); await p.screenshot({path:path.join(info.output,label+'.png'),fullPage:true}); };
    async function review(p, label) {
        for (const [width,height] of sizes) {
            await p.setViewportSize({width,height}); await settle(p);
            const checks = await p.evaluate(() => {
                const visible = e => !!e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden';
                return { width: innerWidth, scroll: document.documentElement.scrollWidth,
                    adminMenu: !!document.querySelector('#layout-menu,.gao-mobile-pins,[data-mobile-shell-owner]'),
                    small: [...document.querySelectorAll('button,a,input:not([type=hidden]):not([type=radio]):not([type=checkbox]),textarea,select,summary')].filter(visible)
                        .filter(e => e.getBoundingClientRect().height < 40).map(e => e.id || e.textContent.trim().slice(0,40)) };
            });
            assert.ok(checks.scroll <= width + 1, `${label} ${width} overflow ${checks.scroll}`);
            assert.equal(checks.adminMenu,false,`${label} must stay public`);
            assert.deepEqual(checks.small,[],`${label} ${width} touch controls`);
            states.push({label,width,height,...checks});
        }
        for (const width of [768,1440]) {
            await p.setViewportSize({width,height:900}); await settle(p);
            const snapshot = () => p.evaluate(() => [...document.querySelectorAll('body,body *')].filter(e => !e.closest('.gp-phone') && e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden').map(e => {
                const r=e.getBoundingClientRect(),s=getComputedStyle(e);return {tag:e.tagName,id:e.id,box:[r.x,r.y,r.width,r.height].map(n=>Math.round(n*100)/100),style:[s.display,s.fontSize,s.lineHeight,s.padding,s.margin,s.backgroundColor,s.color,s.borderRadius,s.position]};
            }));
            const enabled=await snapshot();
            await p.evaluate(() => { document.querySelector('link[href*="gaoapp.mobile-public.css"]').disabled=true; const s=document.createElement('style');s.id='ui23-baseline-hide';s.textContent='.gp-phone{display:none!important}';document.head.append(s); });
            await settle(p);const disabled=await snapshot();
            assert.deepEqual(enabled,disabled,`${label} desktop ${width}`);
            await p.evaluate(() => { document.querySelector('link[href*="gaoapp.mobile-public.css"]').disabled=false;document.getElementById('ui23-baseline-hide').remove(); });
            desktop.push({label,width,unchanged:true});
            if(width===1440) await p.screenshot({path:path.join(info.output,label+'-desktop.png'),fullPage:true});
        }
        await image(p,label);
    }
    try {
        // Real public Razor and native server validation, no mocked application routes.
        assert.equal((await page.goto(info.baseUrl+info.activeLink)).status(),200);
        assert.equal(await page.locator('form').count(),1);
        const input=await page.locator('#BuyerName').elementHandle();
        await page.locator('#BuyerName').fill('UI23 bản nháp vẫn còn sau khi đổi kích thước');
        await review(page,'invoice-draft');
        assert.equal(await input.evaluate(e=>e===document.getElementById('BuyerName')),true);
        assert.equal(await page.locator('#BuyerName').inputValue(),'UI23 bản nháp vẫn còn sau khi đổi kích thước');
        await page.locator('label[for=buyerBusiness]').click();
        assert.equal(await page.locator('#BuyerTaxCode').evaluate(e=>e.required),true);
        await page.locator('#BuyerLegalName').fill('UI23 Công ty mẫu tên dài để kiểm tra hiển thị');
        await review(page,'invoice-business');
        await page.locator('label[for=buyerIndividual]').click();
        const csrf=await page.locator('[name=__RequestVerificationToken]').inputValue();
        const invalid=await context.request.post(info.baseUrl+info.activeLink,{form:{__RequestVerificationToken:csrf,BuyerType:'Individual',BuyerName:'UI23',BuyerAddress:''}});
        assert.equal(invalid.status(),400);
        // Navigate by the actual form to retain the real error response and status.
        await page.locator('#BuyerAddress').fill('');
        await page.locator('#BuyerName').fill('UI23');
        await page.locator('#BuyerAddress').evaluate(e=>e.required=false);
        await Promise.all([page.waitForNavigation(),page.locator('button[type=submit]').click()]);
        assert.equal(await page.locator('[role=alert]').count(),1);
        await review(page,'invoice-server-error');
        await page.goto(info.baseUrl+info.activeLink);
        await page.locator('#BuyerName').fill('UI23 khách đã cập nhật');
        await page.locator('#BuyerAddress').fill('UI23 địa chỉ thử nghiệm');
        await Promise.all([page.waitForNavigation(),page.locator('button[type=submit]').click()]);
        assert.match(await page.locator('[role=status]').innerText(),/Đã tiếp nhận/);
        await review(page,'invoice-accepted');
        for (const [label,link,status] of [['invoice-expired',info.expiredLink,410],['invoice-locked',info.lockedLink,409]]) {
            assert.equal((await page.goto(info.baseUrl+link)).status(),status);
            assert.doesNotMatch(await page.content(),/PRIVATE-BUYER|private-issue4|012345678901/);
            assert.equal(await page.locator('input').count(),0); await review(page,label);
        }
        assert.equal((await context.request.get(info.baseUrl+'/invoice-info/invalid-ui23')).status(),404);

        assert.equal((await page.goto(info.baseUrl+'/Home/Index')).status(),200);
        await review(page,'public-home');
        assert.equal((await context.request.get(info.baseUrl+'/Home/Privacy')).status(),405);
        await Promise.all([page.waitForNavigation(),page.locator('.gp-header button').click()]);
        assert.match(new URL(page.url()).pathname,/\/Home\/Privacy/i);
        await review(page,'public-privacy-post');
        for(const route of ['/error/403','/error/404','/error/500','/Home/Error']) {
            await page.goto(info.baseUrl+route);await review(page,'public-error-'+route.replaceAll('/','-'));
            const mobileText=await page.locator('body').innerText();assert.doesNotMatch(mobileText,/Development Mode|ASPNETCORE_ENVIRONMENT/);
        }
        const disabled=await context.newPage();watch(disabled);
        await disabled.goto(info.disabledUrl+'/support');assert.equal(await disabled.locator('#csStartForm').count(),0);await review(disabled,'chat-disabled');
        await Promise.all([page.waitForResponse(r=>r.url().endsWith('/support/session')),page.goto(info.baseUrl+'/support')]);
        await review(page,'chat-welcome');
        await page.locator('#csName').fill('UI23 Khách kiểm thử');await page.locator('#csConsent').check();
        await page.locator('#csStartForm button').click();await page.locator('#csChat').waitFor({state:'visible'});
        assert.equal(await page.locator('input[type=file]').count(),0,'Public API is text-only; do not invent an upload command');
        await page.locator('#csMessage').fill('UI23 nháp không bị mất khi xoay màn hình');
        const composer=await page.locator('#csMessage').elementHandle();
        await review(page,'chat-bot-draft');
        assert.equal(await composer.evaluate(e=>e===document.getElementById('csMessage')),true);
        assert.equal(await page.locator('#csMessage').inputValue(),'UI23 nháp không bị mất khi xoay màn hình');
        await page.setViewportSize({width:390,height:440});
        await page.evaluate(()=>document.activeElement?.blur());await page.locator('#csMessage').focus();await settle(page);
        const focused=await page.locator('#csMessage').boundingBox(),send=await page.locator('#csSend').boundingBox();
        assert.ok(focused.y>=0&&focused.y+focused.height<=440,'Short viewport keeps the focused native composer reachable');
        assert.ok(send.y>=0&&send.y+send.height<=440,'Short viewport keeps send reachable');
        await page.screenshot({path:path.join(info.output,'chat-short-viewport-focus.png')});
        await page.setViewportSize({width:390,height:844});await settle(page);
        await page.locator('#csHuman').click();await page.waitForFunction(()=>document.getElementById('csMode').textContent.includes('Đang chờ'));
        await page.locator('#csMessage').fill('UI23 tin gửi đúng một lần');
        await page.locator('#csSend').click();await page.waitForFunction(()=>document.getElementById('csMessage').value==='');
        assert.equal(await page.locator('.cs-message[data-role=customer] p').filter({hasText:'UI23 tin gửi đúng một lần'}).count(),1);
        await review(page,'chat-waiting');
        await page.locator('#csPrivate summary').click();await page.locator('#csVerifyCode').fill('UI23-CODE-INVALID');
        await Promise.all([page.waitForResponse(r=>r.url().endsWith('/private/verify')),page.locator('#csVerifyForm button').click()]);
        await review(page,'chat-private-denied');
        await page.locator('#csPrivate summary').click();
        // Native staff command changes only this customer's fixture conversation.
        const employee=await browser.newContext();const admin=await employee.newPage();watch(admin);
        await admin.goto(info.baseUrl+'/admin/account/login');await admin.locator('[name=UserName]').fill(info.Name);
        await admin.locator('[name=Password]').fill(info.Password);await admin.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([admin.waitForNavigation(),admin.locator('button[type=submit]').click()]);
        await admin.goto(info.baseUrl+'/admin/customer-service');
        const id=await page.locator('#csConversations').inputValue();
        async function command(action){const response=await admin.evaluate(async({id,action})=>{
            const token=document.querySelector('[name=__RequestVerificationToken]').value;
            const url='/admin/customer-service/conversations/'+id;
            const get=await fetch(url);const row=await get.json();
            const post=await fetch(url+'/commands',{method:'POST',headers:{'Content-Type':'application/json',RequestVerificationToken:token},body:JSON.stringify({requestId:crypto.randomUUID(),action,version:row.version})});return {status:post.status,body:await post.text()};
        },{id,action});assert.equal(response.status,200,response.body);await page.reload();await page.locator('#csChat').waitFor({state:'visible'});}
        await command('claim');await review(page,'chat-human');
        await command('close');assert.equal(await page.locator('#csMessage').isDisabled(),true);await review(page,'chat-closed');
        await page.locator('#csNew').click();await page.locator('#csWelcome').waitFor({state:'visible'});await image(page,'chat-new');
        // Reduced-motion and virtual viewport changes use the same controls, no clone or submit.
        await page.emulateMedia({reducedMotion:'reduce'});
        assert.equal(await page.locator('.cs-welcome').evaluate(e=>getComputedStyle(e).animationName),'none');
        await page.setViewportSize({width:390,height:440});await settle(page);
        assert.match(await page.locator('#customerSupport').getAttribute('style'),/--gp-viewport-height: 440px/);
        await page.setViewportSize({width:768,height:900});await settle(page);
        assert.equal(await page.locator('#customerSupport').evaluate(e=>e.style.getPropertyValue('--gp-viewport-height')),'');
        assert.deepEqual(errors,[]);
        const result={passed:true,states,desktop,pageErrors:errors,checks:['native individual/business form','server 400 validation','accepted notice','410 expired and 409 locked without PII','invalid token 404','GET privacy 405 and native POST navigation','public error pages','chat disabled/welcome/bot/waiting/human/closed','single send','private verification denied','draft and node identity through resize','reduced motion','viewport sizing and desktop cleanup','no admin menu','no public attachment endpoint invented'],browser:'new isolated headless Chrome',physicalDeviceTested:false};
        fs.writeFileSync(path.join(info.output,'runtime-review.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({passed:true,phoneStates:states.length,desktopChecks:desktop.length,checks:result.checks}));
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
