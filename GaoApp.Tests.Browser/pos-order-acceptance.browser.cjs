'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
(async()=>{
    const info=JSON.parse(await new Promise(resolve=>{let data='';process.stdin.on('data',s=>data+=s);process.stdin.on('end',()=>resolve(data));}));
    const output=path.resolve(process.env.GAO_POS_ACCEPTANCE_OUTPUT||'TestResults/mobile-ui-04d-20261025/acceptance');fs.mkdirSync(output,{recursive:true});
    const browser=await chromium.launch({channel:'chrome',headless:true});
    const context=await browser.newContext({viewport:{width:1440,height:900},reducedMotion:'reduce'});
    const page=await context.newPage(),checks=[],errors=[],cspErrors=[],commands=[];let completed=false;
    function observe(p){p.on('pageerror',e=>errors.push(e.message));p.on('console',m=>{if(m.type()==='error'&&/Content Security Policy/i.test(m.text()))cspErrors.push(m.text());});}
    observe(page);
    page.on('request',r=>{if(r.method()!=='GET'&&/\/admin\/(pos|pos-shift)\//.test(new URL(r.url()).pathname))commands.push({path:new URL(r.url()).pathname,method:r.method(),body:r.postDataJSON(),hasCsrf:!!r.headers()['requestverificationtoken']});});
    const money=n=>new Intl.NumberFormat('vi-VN',{maximumFractionDigits:0}).format(n);
    const compact=s=>s.replace(/\s/g,'');
    const canonical=s=>s.replace(/(<input name="__RequestVerificationToken"[^>]*?) style=""/g,'$1');
    async function login(p,user,password){
        await p.goto(info.baseUrl+'/admin/account/login');await p.locator('[name=UserName]').fill(user);await p.locator('[name=Password]').fill(password);
        if(await p.locator('select[name=SelectedTerminalId]').count())await p.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([p.waitForURL(u=>!u.pathname.endsWith('/login')),p.locator('#loginForm button[type=submit]').click()]);
    }
    async function load(id){assert.equal((await page.goto(info.baseUrl+'/admin/pos/order-detail/'+id)).status(),200);await page.locator('[data-mobile-order-detail]').waitFor();await page.evaluate(()=>document.fonts.ready);}
    async function phone(width=390){await page.setViewportSize({width,height:width===320?740:844});await page.locator('.od-mobile-shell').waitFor();}
    async function tab(i){await page.locator('.od-mobile-tab').nth(i).click();}
    async function shot(name){await page.screenshot({path:path.join(output,name+'.png')});}
    async function overflow(label){
        const geometry=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth}));
        assert.ok(geometry.scroll<=geometry.width+1,label+': '+JSON.stringify(geometry));
        for(const selector of ['[data-order-field="voucher"]','[data-order-field="customer"]','.od-mobile-hero .ord-stat-value']){
            const problems=await page.locator(selector).evaluateAll(nodes=>nodes.filter(n=>n.getClientRects().length&&n.scrollWidth>n.clientWidth+2).map(n=>({tag:n.tagName,width:n.clientWidth,scroll:n.scrollWidth})));
            assert.deepEqual(problems,[],label+' clipped data: '+selector);
        }
    }
    async function modal(id){await page.locator('#'+id+'.show').waitFor();}
    async function close(id){await page.locator('#'+id+' .modal-header [data-bs-dismiss=modal]').click();await page.locator('#'+id).waitFor({state:'hidden'});}
    const byKind=k=>info.cases.find(c=>c.Kind===k);
    try{
        await login(page,info.user,info.password);
        for(const item of info.cases){
            await page.setViewportSize({width:1440,height:900});await load(item.OrderId);
            const response=await page.request.get(info.baseUrl+'/admin/pos/orders/'+item.OrderId);assert.equal(response.status(),200);const receipt=await response.json();
            const original=canonical(await page.locator('[data-mobile-order-detail]').evaluate(n=>n.outerHTML));
            for(const width of [390,320]){
                await phone(width);
                assert.ok(compact(await page.locator('.od-mobile-hero .ord-stat.primary .ord-stat-value').innerText()).includes(compact(money(receipt.grandTotal)+'đ')),item.Kind+' total');
                assert.equal(await page.locator('[data-order-lines] tbody tr').count(),Math.max(1,receipt.lines.length));
                for(let i=0;i<3;i++){await tab(i);await overflow(item.Kind+' '+width+' tab '+i);}
                assert.equal(await page.locator('[data-order-field="customer"] img, [data-order-information] script').count(),0,'Razor content must stay escaped');
                await tab(1);
                assert.ok(compact(await page.locator('.ord-price-net dd').innerText()).includes(compact(money(receipt.grandTotal)+'đ')));
                if(item.Kind==='vouchers'){
                    assert.equal(receipt.rewardVouchers.length,3);
                    for(const v of receipt.rewardVouchers)assert.ok((await page.locator('[data-order-field="voucher"]').innerText()).includes(v.voucherCode));
                    await page.locator('[data-order-field="voucher"]').scrollIntoViewIfNeeded();await shot('vouchers-expanded-'+width);
                }
                if(item.Kind==='hkd'){
                    const details=page.locator('.od-mobile-disclosure').filter({has:page.locator('[data-order-legal]')});
                    if(await details.getAttribute('open')===null)await details.locator('summary').click();
                    assert.equal(await page.locator('[data-order-legal] tbody tr').count(),2);
                    assert.equal(await page.locator('[data-order-legal] a[href*="/Admin/Invoice/Detail"]').count(),2);
                    await overflow('expanded HKD '+width);await page.locator('[data-order-legal] .ord-card-head').scrollIntoViewIfNeeded();await shot('hkd-expanded-'+width);
                }
                if(item.Kind==='partial-credit'||item.Kind==='large'){
                    await page.locator('#od-mobile-panel-1 [data-csp-action="payment-history"]').click();await modal('paymentHistoryModal');
                    const text=compact(await page.locator('#paymentHistoryModal').innerText());
                    assert.ok(text.includes(compact(money(receipt.paidTotal)+'đ')));assert.ok(text.includes(compact(money(receipt.balanceDue)+'đ')));
                    await overflow('payments '+item.Kind+' '+width);await shot(item.Kind+'-payments-'+width);await close('paymentHistoryModal');
                }
                if(item.Kind.startsWith('delivery-')){
                    assert.equal(receipt.delivery.isSettlementSale,false);assert.equal(await page.locator('.ord-payment-section').count(),0);
                    assert.equal(await page.locator('[data-csp-action="return-refund"]').count(),0);
                }
                await tab(0);await page.evaluate(()=>scrollTo(0,0));await shot(item.Kind+'-'+width);
            }
            await page.setViewportSize({width:1440,height:900});await page.waitForFunction(()=>!document.querySelector('.od-mobile-shell'));
            assert.equal(canonical(await page.locator('[data-mobile-order-detail]').evaluate(n=>n.outerHTML))===original,true,item.Kind+' desktop DOM restoration');
            checks.push('real-archive-'+item.Kind+'-390-320-desktop');
        }
        await phone();await load(byKind('repeated-refunds').OrderId);await tab(2);
        await page.locator('.od-mobile-history-actions [data-csp-action="return-history"]').click();await modal('returnHistoryModal');
        await page.waitForFunction(()=>document.querySelector('#returnHistoryModalBody').getAttribute('aria-busy')==='false');
        assert.equal(await page.locator('#returnHistoryModalBody .ret-history-item').count(),2);
        assert.ok((await page.locator('#returnHistoryModalBody .ret-history-item').first().boundingBox()).y<(await page.locator('#returnHistoryModal .modal-footer').boundingBox()).y,'First history record visible above modal footer');
        await overflow('two completed refunds');await shot('repeated-refund-history-390');await close('returnHistoryModal');
        await tab(0);await page.locator('.od-mobile-after-sale [data-csp-action="return-refund"]').click();await modal('returnRefundModal');
        await page.waitForFunction(()=>document.querySelector('[data-return-refunded-order]')?.textContent.trim()==='15 đ');
        await shot('refunded-summary-390');await page.setViewportSize({width:1440,height:900});
        await page.waitForFunction(()=>!document.querySelector('.od-mobile-shell'));
        assert.equal((await page.locator('[data-return-refunded-order]').innerText()).trim(),'0 đ','Legacy desktop display retained');
        await phone();await page.waitForFunction(()=>document.querySelector('[data-return-refunded-order]')?.textContent.trim()==='15 đ');
        await close('returnRefundModal');
        checks.push('real-repeated-refund-history');
        for(const [label,loginInfo,hasHkd] of [['view-only',info.viewOnly,false],['reconcile-only',info.reconcileOnly,true]]){
            const c=await browser.newContext({viewport:{width:320,height:740},reducedMotion:'reduce'}),p=await c.newPage();observe(p);
            await login(p,loginInfo.Name,loginInfo.Password);assert.equal((await p.goto(info.baseUrl+'/admin/pos/order-detail/'+byKind('hkd').OrderId)).status(),200);
            await p.locator('.od-mobile-shell').waitFor();assert.equal(await p.locator('[data-order-legal]').count(),Number(hasHkd));
            if(hasHkd){await p.locator('.od-mobile-tab').nth(1).click();await p.locator('.od-mobile-disclosure:has([data-order-legal]) > summary').click();
                assert.equal(await p.locator('[data-order-legal] a[href*="/Admin/Invoice/Detail"]').count(),0);assert.match(await p.locator('[data-order-legal]').innerText(),/Chỉ xem/);}
            if(label==='view-only')assert.equal(await p.locator('.od-mobile-dock a').count(),1);
            await p.screenshot({path:path.join(output,'hkd-'+label+'-320.png')});await c.close();checks.push('populated-hkd-'+label);
        }
        await load(byKind('partial-credit').OrderId);await phone(320);await page.evaluate(()=>document.documentElement.style.fontSize='200%');
        for(let i=0;i<3;i++){await tab(i);await overflow('font 200% tab '+i);}
        await shot('font-200-percent-320');await page.evaluate(()=>document.documentElement.style.removeProperty('font-size'));
        await page.emulateMedia({forcedColors:'active',reducedMotion:'reduce'});await tab(0);await overflow('forced colors');await shot('forced-colors-320');
        assert.equal(await page.locator('.od-mobile-panel:not([hidden])').evaluate(n=>getComputedStyle(n).animationName),'none');
        await page.emulateMedia({forcedColors:'none',reducedMotion:'reduce'});checks.push('text-zoom-forced-colors-reduced-motion-emulation');
        await phone();await page.goto(info.baseUrl+'/admin/pos/orders-page?keyword=UI04D');
        await page.waitForFunction(()=>document.querySelector('.po-table')?.getAttribute('aria-busy')==='false');
        assert.equal(await page.locator('#keyword').inputValue(),'UI04D');
        const listUrl=page.url();await load(byKind('retail').OrderId);await page.goBack();
        await page.locator('#keyword').waitFor();assert.equal(page.url(),listUrl);assert.equal(await page.locator('#keyword').inputValue(),'UI04D');
        checks.push('real-list-filter-back-navigation');
        await load(byKind('repeated-refunds').OrderId);await tab(2);
        const historyUrl='**/admin/pos/returns/order/'+byKind('repeated-refunds').OrderId+'/history';
        await page.route(historyUrl,r=>r.fulfill({status:403,contentType:'application/json',body:JSON.stringify({message:'Không còn quyền xem lịch sử.'})}));
        await page.locator('.od-mobile-history-actions [data-csp-action="return-history"]').click();await modal('returnHistoryModal');
        await page.waitForFunction(()=>document.querySelector('#returnHistoryModalBody').getAttribute('aria-busy')==='false');
        assert.match(await page.locator('#returnHistoryModalBody').innerText(),/Không còn quyền/);await shot('history-forbidden-390');await close('returnHistoryModal');await page.unroute(historyUrl);
        checks.push('history-permission-error-readable-and-closeable');
        // A genuine browser command, against this fixture's dedicated real POS sale only.
        await load(info.writeOrderId);await page.locator('.od-mobile-after-sale [data-csp-action="return-refund"]').click();await modal('returnRefundModal');
        await page.waitForFunction(()=>document.querySelector('#returnRefundModalBody').getAttribute('aria-busy')==='false');
        await page.locator('#returnType').selectOption('1');await page.locator('#returnReason').fill('UI04D browser refund exactly once');
        await page.locator('#refundAmount').fill('10');await page.locator('#refundMethod').selectOption('0');
        const saved=page.waitForResponse(r=>new URL(r.url()).pathname==='/admin/pos/returns'&&r.request().method()==='POST');
        const refreshed=page.waitForNavigation({waitUntil:'load'});
        await page.locator('#btnSubmitReturnRefund').dblclick();assert.equal((await saved).status(),200);await refreshed;
        await page.locator('.od-mobile-shell').waitFor();
        const after=await (await page.request.get(info.baseUrl+'/admin/pos/orders/'+info.writeOrderId)).json();
        assert.equal(after.grandTotal,40);assert.equal(after.paidTotal,40);assert.equal(after.refundedTotal,10);assert.equal(after.refundableRemaining,30);
        assert.equal(commands.length,1);assert.equal(commands[0].path,'/admin/pos/returns');assert.equal(commands[0].body.orderId,info.writeOrderId);
        assert.equal(commands[0].body.type,1);assert.equal(commands[0].body.payments[0].amount,10);assert.equal(commands[0].hasCsrf,true);
        await tab(2);await page.locator('.od-mobile-history-actions [data-csp-action="return-history"]').click();await modal('returnHistoryModal');
        await page.waitForFunction(()=>document.querySelector('#returnHistoryModalBody').getAttribute('aria-busy')==='false');
        assert.equal(await page.locator('#returnHistoryModalBody .ret-history-item').count(),1);await shot('successful-browser-refund-390');await close('returnHistoryModal');
        checks.push('real-browser-refund-posted-once-with-CSRF-and-refreshed-history');
        assert.deepEqual(errors,[]);assert.deepEqual(cspErrors,[]);completed=true;
        console.log('PASS UI-04d acceptance: '+checks.length+' groups; nine real SQL cases; HKD/zoom/history/back navigation; one genuine browser refund, CSRF and refreshed state.');
    }finally{
        if(!completed)await shot('failure').catch(()=>{});
        fs.writeFileSync(path.join(output,'result.json'),JSON.stringify({completed,checks,errors,cspErrors,commands,realRazorSql:true,isolatedHeadlessChrome:true,createdAt:new Date().toISOString()},null,2));
        await browser.close();
    }
})().catch(error=>{console.error(error);process.exitCode=1;});
