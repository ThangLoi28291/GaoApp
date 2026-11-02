'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const {protect}=require('./delivery-mobile-review.cjs');
(async()=>{
    const info=JSON.parse(await new Promise(resolve=>{let raw='';process.stdin.on('data',x=>raw+=x);process.stdin.on('end',()=>resolve(raw));}));
    const result={passed:false,checks:[],viewports:[],errors:[],csp:[]};let page;
    const write=()=>fs.writeFileSync(path.join(info.output,'result.json'),JSON.stringify(result,null,2));
    const pass=text=>{result.checks.push(text);write();console.log('PASS '+text);};
    const browser=await chromium.launch({channel:'chrome',headless:true});
    async function review(label,width,height=900){
        await page.setViewportSize({width,height});await page.evaluate(()=>document.fonts.ready);await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
        const state=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,duplicateIds:[...document.querySelectorAll('[id]')].map(x=>x.id).filter((x,i,a)=>a.indexOf(x)!==i)}));
        assert.ok(state.scroll<=width+1,`${label}: overflow ${state.scroll}/${width}`);assert.deepEqual(state.duplicateIds,[]);
        await page.evaluate(()=>scrollTo(0,0));
        await page.screenshot({path:path.join(info.output,`${label}-${width}.png`),fullPage:true,animations:'disabled'});
        result.viewports.push({label,...state});write();
    }
    async function run(account,isPhone){
        const context=await browser.newContext({viewport:isPhone?{width:390,height:844}:{width:1440,height:1000},serviceWorkers:'block',reducedMotion:'reduce'});
        page=await context.newPage();page.setDefaultTimeout(20000);await protect(page,account.baseUrl);
        page.on('pageerror',e=>result.errors.push(e.message));page.on('console',m=>{if(/violates.*Content Security Policy|Refused to (?:load|execute|apply)/i.test(m.text()))result.csp.push(m.text());});
        await page.goto(account.baseUrl+'/admin/account/login');await page.locator('[name=UserName]').fill(account.name);await page.locator('[name=Password]').fill(account.password);
        if(await page.locator('[name=SelectedTerminalId]').count())await page.locator('[name=SelectedTerminalId]').selectOption(String(account.terminalId));
        await Promise.all([page.waitForURL(u=>!u.pathname.endsWith('/login')),page.locator('#loginForm button[type=submit]').click()]);
        await page.goto(account.baseUrl+'/admin/deliveries?key='+encodeURIComponent(account.code));
        await page.getByRole('button',{name:'Bắt đầu soạn',exact:true}).waitFor();
        await page.getByRole('button',{name:'Bắt đầu soạn',exact:true}).click();
        await page.getByRole('button',{name:'Đủ theo bill',exact:true}).waitFor();
        assert.equal(await page.getByLabel('Lý do nếu thiếu',{exact:true}).isVisible(),false);
        await page.getByLabel(/^Thực có/).fill('8');
        assert.equal(await page.getByLabel('Lý do nếu thiếu',{exact:true}).isVisible(),true);
        await review(isPhone?'phone-picking':'desktop-picking',isPhone?390:1440,isPhone?844:1000);
        await page.getByRole('button',{name:'Đủ theo bill',exact:true}).click();assert.equal(await page.getByLabel('Lý do nếu thiếu',{exact:true}).isVisible(),false);await page.getByLabel('Tôi đã kiểm đủ các dòng',{exact:false}).check();
        const complete=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname===`/admin/api/deliveries/${account.id}/picking/complete`);
        await page.getByRole('button',{name:'Soạn xong',exact:true}).click();assert.equal((await complete).status(),200);
        await page.getByRole('button',{name:'Giao hàng',exact:true}).waitFor();pass((isPhone?'Phone':'Desktop')+' confirms picking in one command');
        if(isPhone){await page.getByRole('button',{name:'← Danh sách đơn',exact:true}).click();await page.locator('[data-dd-stage=ready]').click();await page.locator('.dd-card[aria-current=true]').click();}
        await page.getByRole('button',{name:'Giao hàng',exact:true}).click();await page.locator('#ddCourier').selectOption(String(info.courierId));
        await page.locator('#ddDispatchConfirmed').check();await review(isPhone?'phone-dispatch':'desktop-dispatch',isPhone?390:1440,isPhone?844:1000);
        const dispatch=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname==='/admin/api/delivery-trips/dispatch');
        await page.locator('#ddDispatchSubmit').click();assert.equal((await dispatch).status(),200);
        await page.getByRole('button',{name:'Nhận bàn giao',exact:true}).waitFor();pass((isPhone?'Phone':'Desktop')+' assigns courier, exports and departs in one command');
        await page.getByRole('button',{name:'Nhận bàn giao',exact:true}).click();await page.getByRole('button',{name:'Giao đủ',exact:true}).click();
        await page.getByLabel('Khách nhận',{exact:true}).fill('8');await page.getByLabel('Lý do nếu giao thiếu / không giao được',{exact:true}).fill('Khách nhận 8, quầy kiểm đếm nhận lại 2');
        await page.waitForFunction(()=>document.querySelector('.dd-total strong')?.textContent.includes('160.000'));
        await page.getByRole('button',{name:'Khách trả đủ · quầy nhận đủ',exact:true}).click();
        const finish=page.getByRole('button',{name:'Xác nhận & hoàn tất',exact:true});assert.equal(await finish.isDisabled(),true);
        await page.getByLabel('Người giao đã về quầy.',{exact:false}).check();await finish.waitFor();assert.equal(await finish.isEnabled(),true);
        await page.getByLabel('Tiền mặt quầy thực nhận (đồng)',{exact:true}).fill('150000');
        assert.equal(await page.getByLabel('Người giao đã về quầy.',{exact:false}).isChecked(),false);assert.equal(await finish.isDisabled(),true);
        await page.getByRole('button',{name:'Khách trả đủ · quầy nhận đủ',exact:true}).click();await page.getByLabel('Người giao đã về quầy.',{exact:false}).check();assert.equal(await finish.isEnabled(),true);
        for(const width of isPhone?[320,360,390,430,600,767]:[1024,1440,1920])await review(isPhone?'phone-counter':'desktop-counter',width,isPhone?844:1000);
        await page.setViewportSize(isPhone?{width:390,height:844}:{width:1440,height:1000});
        assert.equal(await page.getByLabel('Khách nhận',{exact:true}).inputValue(),'8');assert.equal(await page.getByLabel('Người giao đã về quầy.',{exact:false}).isChecked(),true);
        await finish.scrollIntoViewIfNeeded();
        const uncovered=await finish.evaluate(el=>{const r=el.getBoundingClientRect(),top=document.elementFromPoint(r.left+r.width/2,r.top+r.height/2);return top===el||el.contains(top);});assert.equal(uncovered,true,'Main action is not covered by the mobile dock');
        let cut=isPhone;const bodies=[];
        await page.route('**/admin/api/delivery-trips/*/handover/'+account.id,async route=>{bodies.push(route.request().postData());const response=await route.fetch();if(cut){cut=false;return route.abort('failed');}return route.fulfill({response});});
        await finish.click();
        if(isPhone){await page.locator('#ddPending').waitFor({state:'visible'});await page.reload();await page.getByRole('button',{name:'Thử lại yêu cầu cũ',exact:true}).waitFor();await page.getByRole('button',{name:'Thử lại yêu cầu cũ',exact:true}).click();}
        await page.locator('.dd-success').waitFor();assert.match(await page.locator('.dd-success').innerText(),/160\.000/);
        assert.match(await page.locator('#ddDetail').innerText(),/khách nhận 8 · nhận về 2/);
        if(isPhone){assert.equal(bodies.length,2);assert.equal(bodies[0],bodies[1]);pass('Phone recovers an already committed counter handover using the exact same request');}
        await review(isPhone?'phone-completed':'desktop-completed',isPhone?390:1440,isPhone?844:1000);
        pass((isPhone?'Phone':'Desktop')+' sells 8, accepts 2 returns and displays the actual final amount');
        await context.close();
    }
    try{await run(info.desktop,false);await run(info.phone,true);assert.deepEqual(result.errors,[]);assert.deepEqual(result.csp,[]);result.passed=true;}
    catch(e){result.failure=e.stack;if(page)await page.screenshot({path:path.join(info.output,'failure.png'),fullPage:true}).catch(()=>{});throw e;}
    finally{write();await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
