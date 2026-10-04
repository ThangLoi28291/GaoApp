'use strict';
// Actual Razor bodies and production JS/CSS with synthetic APIs. No business DB or drawer calls.
const {chromium} = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const web = path.resolve(__dirname, '../GaoApp.Web');
const origin = 'https://gao-reconciliation.test';
const output = process.env.GAO_RECON_TEST_OUTPUT;
const date = '2026-10-04T03:00:00'; // SQL UTC DateTime without a timezone suffix.
const payment = (id,method,amount,bankVerified=false) => ({id,method,amount,bankVerified,
    reference:method==='BankTransfer'?'CK-'+id:null,provider:method==='BankTransfer'?'ACB':null,
    paidAtUtc:date,actor:'Nguyễn Thu Ngân',cancelled:false,isDebtCollection:false,
    confirmation:bankVerified?'ACB đã xác nhận':method==='BankTransfer'?'Nhân viên ghi nhận chuyển khoản':'Đã ghi nhận'});
const order = (id,grandTotal,cash,bank,when=date) => ({id,number:'POS-00'+id,status:'Completed',customer:'Khách lẻ',
    actor:'Nguyễn Thu Ngân',createdAtUtc:when,completedAtUtc:when,cancelled:false,grandTotal,deposit:0,
    cashReceived:cash,bankReceived:bank,otherReceived:0,remaining:0,surplus:Math.max(0,cash+bank-grandTotal),
    cashApplied:cash,nonCashApplied:Math.min(bank,grandTotal),isCreditSale:false,note:'',
    payments:[...(cash?[payment(id*10,'Cash',cash)]:[]),...(bank?[payment(id*10+1,'BankTransfer',bank,id===2)]:[])],
    lines:[{name:'Sản phẩm kiểm thử',quantity:1,unitPrice:grandTotal,total:grandTotal}]});
const data = {storeId:1,ownerId:3,shiftId:5,terminalId:2,shiftCode:'CA-TEST-005',ownerName:'Nguyễn Thu Ngân',
    terminalName:'Quầy thu ngân 02',status:'Open',canReturnToClose:true,openedAtUtc:'2026-10-01T00:00:00Z',closedAtUtc:null,asOfUtc:date,
    openingCash:100000,cashSales:1541630,nonCashSales:315000,cashIn:240000,cashOut:20000,cashRefunds:15000,nonCashRefunds:0,
    expectedCash:1846630,detailExpectedCash:1846630,actualCash:null,needsReconciliation:false,
    orders:[order(1,215000,0,250000),order(2,180000,80000,100000,'2026-10-03T03:00:00Z'),order(3,1461630,1461630,0,'2026-10-01T03:00:00Z')],
    cashTransactions:[{id:8,type:'CashIn',amount:240000,reason:'Bổ sung tiền lẻ',note:'Phiếu gốc tại quầy',createdAtUtc:date,
        actor:'Nguyễn Thu Ngân',cancelled:false,canRequest:true,pendingRequestId:9},
        {id:7,type:'CashOut',amount:20000,reason:'Chi mua vật tư <kiểm tra>',note:'Đối chiếu hóa đơn',createdAtUtc:date,
            actor:'Nguyễn Thu Ngân',cancelled:false,canRequest:true}],
    refunds:[{id:4,orderId:99,number:'HOAN-004',status:'Completed',reason:'Khách trả hàng của ca trước',note:'',
        createdAtUtc:date,refundTotal:15000,depositRestored:0,payments:[payment(50,'Cash',15000)]}],
    otherMovements:[{id:4,kind:'Deposit:Receive',amount:100000,method:'Cash',reference:'DC-004',note:'Nhận cọc',createdAtUtc:date,actor:'Nguyễn Thu Ngân',customer:'Khách Nguyễn',allocations:[],canRequest:true},
        {id:5,kind:'DebtCollection',amount:50000,method:'BankTransfer',reference:'CN-005',note:'Thu nợ đơn của ca trước',createdAtUtc:date,actor:'Nguyễn Thu Ngân',customer:'Khách Nguyễn',allocations:[{orderId:99,number:'POS-099',amount:50000}]}],
    qrs:[{id:6,orderId:2,amount:100000,code:'QR-006',status:'Completed',confirmation:'QR ACB',reviewReason:null,createdAtUtc:date,paymentId:21},
        {id:7,orderId:1,amount:250000,code:'QR-007',status:'Pending',confirmation:'QR ACB',reviewReason:null,createdAtUtc:date,paymentId:null}],
    adjustments:[{id:9,transactionId:8,status:'Pending',isCancellation:false,beforeType:'CashIn',beforeAmount:240000,
        afterType:'CashIn',afterAmount:220000,expectedDelta:-20000,reason:'Kiểm tra lại phiếu gốc',actor:'Nguyễn Thu Ngân',createdAtUtc:date}],
    denominations:[{type:'Opening',value:100000,quantity:1,amount:100000}]};
let shift = {id:5,shiftCode:data.shiftCode,openedByUserName:data.ownerName,terminalName:data.terminalName,
    openedAtUtc:data.openedAtUtc,openingCash:data.openingCash,cashSalesTotal:data.cashSales,nonCashSalesTotal:data.nonCashSales,
    cashInTotal:data.cashIn,cashOutTotal:data.cashOut,cashRefundTotal:data.cashRefunds,closingCashExpected:data.expectedCash,status:0};
let deny = false;
const errors = [], writes = [];
function html(kind) {
    let body, scripts, css;
    if(kind==='reconciliation') {
        const raw=fs.readFileSync(path.join(web,'Areas/Admin/Views/POSShiftReconciliation/Index.cshtml'),'utf8');
        body=raw.slice(raw.indexOf('<div class="recon-heading">'),raw.indexOf('\n}\n<partial'))+
            raw.slice(raw.indexOf('<main id="shiftReconciliation"'),raw.indexOf('@section PageScripts'));
        body=body.replace('@CurrentUser.UserId','3');
        scripts=['/Admin/js/pos/pos.shift.close-draft.js','/Admin/js/pos/pos.shift.reconciliation.js'];
        css='/Admin/css/pos/pos-shift-reconciliation.css';
    } else {
        const raw=fs.readFileSync(path.join(web,'Areas/Admin/Views/POSShiftPage/Index.cshtml'),'utf8');
        body=raw.slice(raw.indexOf('<div class="shift-shell">'),raw.indexOf('@section PageScripts'))
            .replace('@Html.AntiForgeryToken()','<input type="hidden" name="__RequestVerificationToken" value="synthetic">')
            .replace('@Html.Raw(JsonSerializer.Serialize(printContext))',JSON.stringify({storeId:1,userId:3,terminalId:2,
                storeName:'Cửa hàng thử nghiệm',terminalName:data.terminalName,userName:data.ownerName,canReconcile:true}));
        scripts=['/lib/bootstrap/dist/js/bootstrap.bundle.min.js','/Admin/js/printing/shift.cash-printing.js',
            '/Admin/js/pos/pos.shift.close-draft.js','/Admin/js/pos/pos.shift.page.js'];
        css='/Admin/css/pos/pos-shift.css';
    }
    return '<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">'+
        '<link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><link rel="stylesheet" href="'+css+'">'+
        '<style>body{padding:24px;background:#f4f5fa} @media(max-width:700px){body{padding:12px}}</style><body>'+body+
        scripts.map(src=>'<script src="'+src+'"></script>').join('')+'</body></html>';
}
(async()=>{
    const browser=await chromium.launch({channel:'chrome',headless:true});
    try {
        const context=await browser.newContext({viewport:{width:1440,height:1080},timezoneId:'Asia/Bangkok'});
        await context.addInitScript(()=>{
            window.__handled=[];
            window.toastr={error:message=>window.__handled.push(message),success:()=>{}};
            const fetchJson=async(url,options)=>{const r=await fetch(url,options),d=await r.json();if(!r.ok)throw Error(d.message);return d;};
            window.PosError={fetchJson,postJson:(url,data)=>fetchJson(url,{method:'POST',body:JSON.stringify(data)}),
                clearBanner:()=>{},handle:e=>window.__handled.push(e.message)};
        });
        await context.route(origin+'/**',async route=>{
            const request=route.request(),url=new URL(request.url());
            const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
            if(request.method()!=='GET'){writes.push(url.pathname);return json({message:'Unexpected mutation'},400);}
            if(url.pathname==='/admin/api/warehouses')return json([]);
            if(url.pathname==='/admin/pos/shift/current')return json(shift);
            if(url.pathname==='/admin/pos/shift/cash-transactions')return json([]);
            if(url.pathname==='/admin/pos-shift/reconciliation/data')return deny?json({message:'Bạn chỉ được đối soát ca của mình.'},403):json(data);
            if(url.pathname.startsWith('/Admin/')||url.pathname.startsWith('/lib/'))return route.fulfill({
                contentType:url.pathname.endsWith('.css')?'text/css':'text/javascript',body:fs.readFileSync(path.join(web,'wwwroot',url.pathname),'utf8')});
            return route.fulfill({contentType:'text/html',body:html(url.pathname==='/admin/pos-shift/reconciliation'?'reconciliation':'shift')});
        });
        const page=await context.newPage();page.setDefaultTimeout(10000);
        page.on('pageerror',e=>errors.push(e.message));
        await page.goto(origin+'/admin/pos-shift?resumeClose=1&shiftId=5');
        await page.locator('#closeShiftModal').waitFor({state:'visible'});
        const denominations=page.locator('input[data-prefix="close"][data-denom]');
        assert.equal(await denominations.count(),9);
        for(const input of await denominations.all())await input.fill('1');
        assert.equal(await page.locator('#txtClosingCashActual').inputValue(),'888.000');
        await page.locator('#txtCloseNote').fill('Giữ nguyên bảng kiểm đếm để dò lệch');
        await page.locator('#btnReviewShiftDifference').click();
        await page.waitForURL('**/reconciliation?shiftId=5');
        await page.locator('#reconContent').waitFor({state:'visible'});
        assert.equal(await page.locator('#reconExpected').innerText(),'1.846.630 đ');
        assert.equal(await page.locator('#reconActual').innerText(),'888.000 đ');
        assert.equal(await page.locator('#reconDifference').innerText(),'Thiếu 958.630 đ');
        assert.match(await page.locator('#reconUpdated').innerText(),/10:00:00/,'UTC database timestamps retain local cashier time');
        assert.equal(await page.locator('#reconList .recon-item').count(),3);
        assert.equal(await page.locator('#reconDataCheck').isVisible(),false,'Routine matching totals do not add an extra panel');
        assert.equal(await page.locator('.recon-breakdown').getAttribute('open'),null,'Full calculation starts collapsed');
        if(output){fs.mkdirSync(output,{recursive:true});await page.screenshot({path:path.join(output,'reconciliation-desktop.png'),fullPage:true,animations:'disabled'});}
        await page.locator('#reconSearch').fill('250.000');
        assert.equal(await page.locator('#reconList .recon-item').count(),1);
        await page.locator('#reconList > .recon-item > summary').click();
        const details=await page.locator('#reconList').innerText();
        assert.match(details,/250.000 đ/);assert.match(details,/Nhận dư 35.000 đ/);assert.match(details,/Nhân viên ghi nhận chuyển khoản/);
        if(output)await page.locator('#reconList').screenshot({path:path.join(output,'reconciliation-payment-detail.png'),animations:'disabled'});
        await page.locator('#reconSearch').fill('');
        await page.locator('.recon-extra-filters > summary').click();await page.locator('#reconDay').selectOption('2026-10-04');
        assert.equal(await page.locator('#reconList .recon-item').count(),1);
        assert.equal(await page.locator('#reconExpected').innerText(),'1.846.630 đ','Day filters never change whole-shift totals');
        await page.locator('.recon-breakdown > summary').click();await page.locator('[data-formula-tab="cash"]').first().click();
        assert.equal(await page.locator('#reconDay').inputValue(),'');
        assert.equal(await page.locator('#reconList .recon-item').count(),2);
        await page.locator('#reconList > .recon-item > summary').first().click();
        assert.equal(await page.locator('#reconList a').first().getAttribute('href'),'/admin/pos-shift/requests?tab=cash&shiftId=5&transactionId=8');
        await page.locator('.recon-more-tabs > summary').click();await page.locator('[data-tab="adjustments"]').click();await page.locator('#reconList > .recon-item > summary').click();
        assert.match(await page.locator('#reconList').innerText(),/-20.000 đ/);
        assert.equal(await page.locator('#reconExpected').innerText(),'1.846.630 đ','Pending requests never change expected cash');
        for(const [tab,count] of [['refunds',1],['other',2],['qrs',2]]){
            await page.locator('[data-tab="'+tab+'"]').click();assert.equal(await page.locator('#reconList .recon-item').count(),count);
            if(tab==='other'){
                await page.locator('#reconList > .recon-item > summary').last().click();
                assert.match(await page.locator('#reconList').innerText(),/Khách Nguyễn/);
                assert.match(await page.locator('#reconList').innerText(),/POS-099/);
                assert.equal(await page.locator('#reconList a[href="/admin/pos/order-detail/99"]').first().getAttribute('href'),'/admin/pos/order-detail/99');
                await page.locator('#reconList > .recon-item > summary').first().click();
                assert.equal(await page.getByRole('link',{name:'Đổi phương thức nhận cọc'}).getAttribute('href'),'/admin/pos-shift/requests?tab=payment&shiftId=5&entryId=4');
            }
        }
        await page.locator('[data-tab="orders"]').click();await page.locator('#reconDay').selectOption('');
        await page.setViewportSize({width:390,height:844});
        await page.locator('#reconList summary').first().click();
        assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Mobile page must not overflow horizontally');
        if(output)await page.screenshot({path:path.join(output,'reconciliation-mobile.png'),fullPage:true,animations:'disabled'});
        await page.setViewportSize({width:1440,height:1080});
        await page.locator('#reconReturn').click();await page.waitForURL('**/pos-shift?resumeClose=1&shiftId=5');
        await page.locator('#closeShiftModal').waitFor({state:'visible'});
        assert.equal(await page.locator('#txtClosingCashActual').inputValue(),'888.000');
        assert.equal(await page.locator('#txtCloseNote').inputValue(),'Giữ nguyên bảng kiểm đếm để dò lệch');
        assert.deepEqual(await denominations.evaluateAll(inputs=>inputs.map(x=>x.value)),Array(9).fill('1'));
        if(output)await page.locator('#closeShiftModal .modal-content').screenshot({path:path.join(output,'closing-with-review.png'),animations:'disabled'});
        await page.locator('#txtClosingCashActual').fill('1846630');assert.equal(await page.locator('#btnReviewShiftDifference').isVisible(),false);
        await page.locator('#txtClosingCashActual').fill('888000');
        await page.locator('#btnCloseShift').click();
        await page.locator('#btnReviewConfirmedShiftDifference').waitFor({state:'visible'});
        await page.locator('#btnReviewConfirmedShiftDifference').click();
        await page.waitForURL('**/reconciliation?shiftId=5');
        await page.locator('#reconReturn').click();await page.locator('#closeShiftModal').waitFor({state:'visible'});
        await page.evaluate(()=>{Storage.prototype.setItem=function(){throw Error('Không lưu được bảng kiểm đếm');};});
        await page.locator('#btnReviewShiftDifference').click();
        assert.match(await page.evaluate(()=>window.__handled.join(' ')),/Không lưu được/);
        assert.equal(await page.locator('#txtClosingCashActual').inputValue(),'888.000');
        assert.match(page.url(),/\/pos-shift\?/,'Failed draft save keeps the closing popup open');
        shift={...shift,id:6};
        await page.goto(origin+'/admin/pos-shift?resumeClose=1&shiftId=5');
        await page.waitForFunction(()=>window.__handled.length>0);
        assert.equal(await page.locator('#closeShiftModal').isVisible(),false,'Do not apply the draft to a different shift');
        data.status='Closed';data.canReturnToClose=false;data.actualCash=2000000;
        await page.goto(origin+'/admin/pos-shift/reconciliation?shiftId=5');await page.locator('#reconContent').waitFor({state:'visible'});
        assert.equal(await page.locator('#reconActual').innerText(),'2.000.000 đ','Closed-shift snapshot ignores the old browser count');
        deny=true;await page.goto(origin+'/admin/pos-shift/reconciliation?shiftId=5');
        await page.locator('#reconError').waitFor({state:'visible'});assert.equal(await page.locator('#reconContent').isVisible(),false);
        assert.equal(await page.locator('#shiftReconciliation').getAttribute('aria-busy'),'false');
        assert.deepEqual(writes,[]);assert.deepEqual(errors,[]);
        console.log('PASS: whole-shift details, full transfer surplus, filters, request links, mobile layout, preserved counts, mismatch CTA, closed snapshot and access errors; zero financial writes.');
    } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
