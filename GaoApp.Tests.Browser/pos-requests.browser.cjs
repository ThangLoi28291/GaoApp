'use strict';
// Actual shared Razor body/partials and production assets; APIs are synthetic and no money is written.
const {chromium}=require('playwright');
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const web=path.resolve(__dirname,'../GaoApp.Web'),origin='https://gao-requests.test',base='/admin/pos-shift/requests';
const output=process.env.GAO_REQUESTS_TEST_OUTPUT,calls=[],errors=[];
let admin=true;
const receipt={id:11,orderId:10,number:'POS-010',shiftId:5,shiftCode:'CA-005',method:'Cash',amount:250000,
    paidAtUtc:'2026-10-04T06:00:00',rowVersion:'receipt-v1',canRequest:true};
const deposit={...receipt,id:31,orderId:0,depositEntryId:31,customerId:3,customerName:'Khách Nguyễn',number:'DC-031',amount:100000};
const paymentRequest={...receipt,id:7,paymentId:11,status:'Pending',requestedBy:'Nguyễn Thu Ngân',oldMethod:'Cash',newMethod:'BankTransfer',
    createdAtUtc:receipt.paidAtUtc,requestReason:'Khách chuyển khoản, ghi nhầm tiền mặt',expectedDelta:-215000,rowVersion:'request-v1'};
const cashRequest={id:7,transactionId:8,status:'Pending',requestedBy:'Nguyễn Thu Ngân',shiftCode:'CA-005',
    before:{type:'CashOut',amount:30000},after:{type:'CashOut',amount:35000}};
function html(url){
    const read=file=>fs.readFileSync(path.join(web,'Areas/Admin/Views/POSRequests',file),'utf8');
    const raw=read('Index.cshtml');
    const body=raw.slice(raw.indexOf('<main id="posOperationsRequests"'),raw.indexOf('@section PageScripts'))
        .replace('@ViewBag.ActiveTab',url.searchParams.get('tab')==='payment'?'payment':'cash')
        .replace('@(isAdmin ? "Xem và duyệt yêu cầu theo từng loại nghiệp vụ." : "Chọn loại nghiệp vụ để gửi và theo dõi yêu cầu của bạn.")',
            admin?'Xem và duyệt yêu cầu theo từng loại nghiệp vụ.':'Chọn loại nghiệp vụ để gửi và theo dõi yêu cầu của bạn.')
        .replace(/<partial name="(_CashPanel|_PaymentPanel)" \/>/g,(_,name)=>{
            const partial=read(name+'.cshtml');return partial.slice(partial.indexOf('<section'))
                .replace('@(isAdmin ? "true" : "false")',String(admin))
                .replace('@Html.AntiForgeryToken()','<input type="hidden" name="__RequestVerificationToken" value="fixture-token">');
        });
    return '<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">'+
        '<link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css">'+
        ['cash-adjustments','payment-adjustments','requests'].map(name=>'<link rel="stylesheet" href="/Admin/css/pos/'+name+'.css">').join('')+
        '<style>body{padding:24px;background:#f4f5fa}@media(max-width:640px){body{padding:12px}}</style><body>'+body+
        ['cash-adjustments','payment-adjustments','requests'].map(name=>'<script src="/Admin/js/pos/'+name+'.js"></script>').join('')+'</body></html>';
}
(async()=>{
    const browser=await chromium.launch({channel:'chrome',headless:true});
    try{
        const context=await browser.newContext({viewport:{width:1440,height:900},timezoneId:'Asia/Bangkok'});
        await context.route(origin+'/**',async route=>{
            const req=route.request(),url=new URL(req.url()),p=url.pathname;
            if(p.startsWith('/Admin/')||p.startsWith('/lib/'))return route.fulfill({contentType:p.endsWith('.css')?'text/css':'text/javascript',body:fs.readFileSync(path.join(web,'wwwroot',p),'utf8')});
            if(p===base)return route.fulfill({contentType:'text/html',body:html(url)});
            assert.equal(req.method(),'GET','Tab navigation must never write financial data');calls.push(url);
            const json=body=>route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
            const list=items=>json({items,totalItems:items.length,totalPages:1});
            if(p==='/admin/pos-shift/cash-adjustments/data')return list([cashRequest]);
            if(p==='/admin/pos-shift/cash-adjustments/transactions')return list([{id:8,values:{type:'CashOut',amount:30000,reason:'Chi vật tư',note:'Phiếu gốc tại quầy'},createdAtUtc:receipt.paidAtUtc,shiftCode:'CA-005',terminal:'Quầy 01',rowVersion:'cash-v1'}]);
            if(p==='/admin/pos-shift/payment-adjustments/data')return list([paymentRequest]);
            if(p==='/admin/pos-shift/payment-adjustments/payments')return list([receipt]);
            if(p==='/admin/pos-shift/payment-adjustments/deposits')return list([deposit]);
            if(p==='/admin/pos-shift/payment-adjustments/deposits/31/preview')return json({currentShift:{expected:315000},proposedShift:{expected:315000},expectedDelta:0});
            if(p==='/admin/pos-shift/payment-adjustments/7')return json({request:paymentRequest,
                currentShift:{expected:315000,actual:100000,difference:-215000},proposedShift:{expected:100000,actual:100000,difference:0},canApprove:false,canWithdraw:false});
            throw Error('Unexpected API '+p);
        });
        const page=await context.newPage();page.setDefaultTimeout(10000);page.on('pageerror',e=>errors.push(e.message));
        const cashTab=page.locator('#operationsCashTab'),paymentTab=page.locator('#operationsPaymentTab');
        const loaded=async selector=>{await page.locator(selector+' tr').first().waitFor();await page.waitForFunction(s=>!document.querySelector(s).textContent.includes('Đang tải'),selector);};
        await page.goto(origin+base+'?shiftId=5&status=Approved');await loaded('#caRows');
        assert.equal(await page.locator('h1').count(),1);assert.equal(await cashTab.getAttribute('aria-selected'),'true');
        assert.equal(calls.filter(u=>u.pathname.includes('payment-adjustments')).length,0,'Hidden workflow loads only on first selection');
        const sameDocument=await page.evaluate(()=>window.__documentMarker=crypto.randomUUID());
        await paymentTab.click();await loaded('#paRows');
        assert.equal(await page.evaluate(()=>window.__documentMarker),sameDocument,'Switching tabs uses the same page');
        assert.equal(new URL(page.url()).searchParams.get('tab'),'payment');
        assert.equal(await page.locator('#paStatus').inputValue(),'Approved');
        assert.equal(await page.locator('#operationsReconciliation').getAttribute('href'),'/admin/pos-shift/reconciliation?shiftId=5');
        assert.equal(calls.at(-1).searchParams.get('shiftId'),'5');
        await page.locator('#paymentAdjustments [data-tab="deposits"]').click();await loaded('#paRows');
        await page.locator('#paKeyword').fill('Nguyễn');await page.locator('#paFilters button[type="submit"]').click();await loaded('#paRows');
        await cashTab.click();assert.equal(await page.locator('#caStatus').inputValue(),'Approved');
        await page.locator('#cashAdjustments [data-tab="transactions"]').click();await loaded('#caRows');
        await page.locator('#caKeyword').fill('vật tư');await page.locator('#caFilters button[type="submit"]').click();await loaded('#caRows');
        const reads=calls.length;await paymentTab.click();
        assert.equal(await page.locator('#paKeyword').inputValue(),'Nguyễn');assert.match(await page.locator('#paRows').innerText(),/DC-031/);
        assert.equal(calls.length,reads,'Revisiting preserves the list without duplicate initialization');
        assert.equal(new URL(page.url()).searchParams.get('view'),'deposits');
        const beforeRefresh=calls.length;await page.locator('#paRefresh').click();await loaded('#paRows');
        assert.equal(calls.length,beforeRefresh+1,'Refresh retains a single listener');
        if(output){fs.mkdirSync(output,{recursive:true});await page.screenshot({path:path.join(output,'requests-payment-desktop.png'),fullPage:true});}
        await cashTab.click();assert.equal(await page.locator('#caKeyword').inputValue(),'vật tư');
        if(output)await page.screenshot({path:path.join(output,'requests-cash-desktop.png'),fullPage:true});
        await paymentTab.click();await page.locator('#paClear').click();await loaded('#paRows');
        assert.equal(new URL(page.url()).search,'?tab=payment');assert.equal(await page.locator('#paScope').innerText(),'');
        assert.equal(await page.locator('#operationsReconciliation').getAttribute('href'),'/admin/pos-shift/reconciliation');
        await page.goto(origin+base+'?tab=payment&requestId=7&shiftId=5');
        await page.locator('#paDialog').waitFor();assert.match(await page.locator('#paDialogBody').innerText(),/Khách chuyển khoản/);
        await page.locator('#paClose').click();await cashTab.click();await loaded('#caRows');
        assert.equal(await page.locator('#caDialog').isVisible(),false,'Request IDs cannot cross workflow boundaries');
        assert.equal(calls.filter(u=>u.pathname==='/admin/pos-shift/cash-adjustments/7').length,0);
        admin=false;await page.goto(origin+base+'?tab=payment&entryId=31&shiftId=5');await loaded('#paRows');
        assert.equal(new URL(page.url()).pathname,base);assert.equal(await paymentTab.getAttribute('aria-selected'),'true');
        assert.match(await page.locator('#paScope').innerText(),/Khoản cọc #31/);assert.match(await page.locator('#paRows').innerText(),/DC-031/);
        await page.locator('#paRows [data-create="31"]').click();assert.match(await page.locator('#paDialogTitle').innerText(),/nhận cọc/);
        await page.locator('#paClose').click();
        await page.goto(origin+base+'?tab=cash&transactionId=8&shiftId=5');await loaded('#caRows');
        assert.equal(await cashTab.getAttribute('aria-selected'),'true');assert.match(await page.locator('#caScope').innerText(),/Phiếu #8/);
        await page.locator('#caRows [data-create="8"]').click();assert.match(await page.locator('#caDialogTitle').innerText(),/phiếu #8/);await page.locator('#caClose').click();
        await cashTab.focus();await cashTab.press('ArrowRight');await loaded('#paRows');
        assert.equal(await paymentTab.getAttribute('aria-selected'),'true');assert.equal(await page.evaluate(()=>document.activeElement.id),'operationsPaymentTab');
        await paymentTab.press('Home');assert.equal(await cashTab.getAttribute('aria-selected'),'true');
        await page.setViewportSize({width:390,height:844});
        for(const button of [cashTab,paymentTab]){
            await button.click();assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Mobile page must fit the screen');
            if(output)await page.screenshot({path:path.join(output,'requests-'+(button===cashTab?'cash':'payment')+'-mobile.png'),fullPage:true});
        }
        assert.deepEqual(errors,[]);console.log(JSON.stringify({ok:true,reads:calls.length,scenarios:['shared tabs','lazy loading','retained filters','isolated request IDs','receipt scopes','receipt forms','keyboard','mobile']}));
    }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
