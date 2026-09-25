'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { chromium } = require('playwright');

function checkRedirectGuards() {
    const source=fs.readFileSync(path.resolve(__dirname,'../GaoApp.Web/wwwroot/Admin/js/pos/pos.error.js'),'utf8');
    for(const item of [
        {pathname:'/admin/pos',pending:0,connected:true,expected:true},
        {pathname:'/admin/pos/v3',pending:0,connected:true,expected:true},
        {pathname:'/admin/pos/legacy',pending:0,connected:true,expected:true},
        {pathname:'/admin/pos',pending:2,connected:true,expected:false},
        {pathname:'/admin/pos',pending:0,connected:false,expected:false},
        {pathname:'/admin/pos-shift',pending:0,connected:true,expected:false}
    ]) {
        const destinations=[];
        const window={ location:{pathname:item.pathname,search:'?debug=1',replace:url=>destinations.push(url)},
            PosOffline:{status:()=>({pending:item.pending,connected:item.connected})} };
        vm.runInNewContext(source,{window,URLSearchParams});
        assert.equal(window.PosError.redirectToShiftIfNeeded('POS_AUTH_FORBIDDEN'),false);
        assert.equal(window.PosError.redirectToShiftIfNeeded('POS_SHIFT_NOT_OPEN'),item.expected);
        if(item.expected) {
            window.PosError.redirectToShiftIfNeeded('POS_SHIFT_NOT_OPEN');
            assert.equal(destinations.length,1,'Only one redirect is scheduled');
            const destination=new URL(destinations[0],'https://localhost');
            assert.equal(destination.pathname,'/admin/pos-shift');
            assert.equal(destination.searchParams.get('returnUrl'),item.pathname+'?debug=1');
        } else assert.equal(destinations.length,0);
    }
    console.log('PASS redirect guards for POS pages, repeated events, pending journal, network loss and no shift-page loop');
}

(async()=> {
    checkRedirectGuards();
    const info=JSON.parse(await new Promise(resolve=>{let s='';process.stdin.on('data',x=>s+=x);process.stdin.on('end',()=>resolve(s));}));
    const browser=await chromium.launch({channel:'chrome',headless:true});
    const context=await browser.newContext({viewport:{width:1366,height:900}});
    const page=await context.newPage(),errors=[];
    page.on('pageerror',e=>errors.push(e.message)); page.setDefaultTimeout(20000);
    const output=path.resolve('TestResults/pos-receive-shift');fs.mkdirSync(output,{recursive:true});
    let openRequests=0;
    page.on('request',r=>{if(r.method()==='POST'&&new URL(r.url()).pathname==='/admin/pos/shift/open')openRequests++;});
    try {
        await page.goto(info.baseUrl+'/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        if(await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url=>!url.pathname.endsWith('/login')),page.locator('button[type=submit]').click()]);
        await page.goto(info.baseUrl+'/admin/pos/v3');
        await page.waitForURL(url=>url.pathname==='/admin/pos-shift'&&url.searchParams.get('start')==='1');
        await page.locator('#openShiftModal.show').waitFor();
        assert.equal(openRequests,0,'Navigating to receive shift never opens a shift without confirmation');
        assert.equal(new URL(page.url()).searchParams.get('returnUrl'),'/admin/pos/v3');
        await page.locator('#ddlWarehouse').selectOption(String(info.warehouseId));
        await page.route('**/admin/pos/shift/open',route=>route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({errorCode:'POS_VALIDATION_FAILED',message:'Kiểm tra thông tin nhận ca.'})}));
        await page.locator('#btnOpenShiftConfirm').click();
        await page.waitForFunction(()=>!document.querySelector('#btnOpenShiftConfirm').disabled);
        assert.equal(new URL(page.url()).pathname,'/admin/pos-shift');
        assert.equal(await page.locator('#openShiftModal.show').isVisible(),true,'Failed receipt keeps the form open');
        await page.unroute('**/admin/pos/shift/open');
        await page.locator('#btnOpenShiftConfirm').click();
        await page.waitForURL(url=>url.pathname==='/admin/pos/v3');
        await page.waitForFunction(()=>!!window.posApp?.state.business.screen);
        const shift=await page.evaluate(async()=>await(await fetch('/admin/pos/shift/current')).json());
        assert.ok(shift.id>0); assert.equal(shift.openingCash,0);
        await page.goto(info.baseUrl+'/admin/pos-shift');
        await page.locator('#openShiftSection').waitFor();
        assert.equal(await page.locator('#openShiftModal').isVisible(),false,'Normal shift-page navigation does not open the receive form');
        assert.deepEqual(errors,[]);
        console.log('PASS no shift -> receive form -> validation failure stays -> successful confirmation returns to original POS; normal shift page remains unchanged');
    } catch(error) { await page.screenshot({path:path.join(output,'failure.png'),fullPage:true}).catch(()=>{});throw error; }
    finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
