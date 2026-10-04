'use strict';
// Real view body and production JS/CSS; all APIs and financial mutations are synthetic.
const {chromium}=require('playwright');
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const web=path.resolve(__dirname,'../GaoApp.Web'),origin='https://gao-payment-adjustment.test';
const output=process.env.GAO_PAYMENT_ADJUST_TEST_OUTPUT,base='/admin/pos-shift/payment-adjustments';
const date='2026-10-04T06:00:00',calls=[],errors=[]; // SQL UTC DateTime can serialize without the Z suffix.
let isAdmin=false,request,depositRequest,failCreateOnce=true,deny=false,shiftVersion='shift-v1';
const current={cashSales:215000,nonCashSales:0,expected:315000,actual:100000,received:100000,difference:-215000,needsReconciliation:false};
const candidate={id:11,orderId:10,number:'POS-010',shiftId:5,shiftCode:'CA-005',shiftStatus:'Closed',method:'Cash',amount:250000,
    reference:null,paidAtUtc:date,rowVersion:'pay-v1',canRequest:true,unavailableReason:null,pendingRequestId:null};
const protectedReceipt={...candidate,id:12,orderId:20,number:'POS-020',method:'BankTransfer',amount:10000,reference:'ACB-20',canRequest:false,
    unavailableReason:'Khoản này có chứng từ ngân hàng tự động liên kết.'};
const depositCandidate={...candidate,id:31,orderId:0,depositEntryId:31,customerId:3,customerName:'Khách Nguyễn',number:'DC-031',method:'BankTransfer',amount:100000,reference:'COC-GOC',rowVersion:'deposit-v1'};
const depositProposed=()=>({...current,expected:current.expected+100000,difference:current.difference-100000});
function proposed(method){return method===0?{...current}:{...current,cashSales:0,nonCashSales:215000,expected:100000,difference:0};}
function detail(){return {request,currentShift:current,proposedShift:request.status==='Pending'?proposed(request.newMethod==='Cash'?0:1):current,
    shiftRowVersion:shiftVersion,canApprove:isAdmin&&request.status==='Pending',canWithdraw:!isAdmin&&request.status==='Pending',unavailableReason:null};}
function html(){
    const raw=fs.readFileSync(path.join(web,'Areas/Admin/Views/POSRequests/_PaymentPanel.cshtml'),'utf8');
    const body=raw.slice(raw.indexOf('<section id="paymentAdjustments"'))
        .replace('@(isAdmin ? "true" : "false")',String(isAdmin))
        .replace('@Html.AntiForgeryToken()','<input type="hidden" name="__RequestVerificationToken" value="synthetic-token">');
    return '<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">'+
        '<link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><link rel="stylesheet" href="/Admin/css/pos/cash-adjustments.css">'+
        '<link rel="stylesheet" href="/Admin/css/pos/payment-adjustments.css"><style>body{padding:24px;background:#f4f5fa}@media(max-width:640px){body{padding:12px}}</style><body>'+body+
        '<script src="/Admin/js/pos/payment-adjustments.js"></script></body></html>';
}
(async()=>{
    const browser=await chromium.launch({channel:'chrome',headless:true});
    try{
        const context=await browser.newContext({viewport:{width:1440,height:1080},timezoneId:'Asia/Bangkok'});
        await context.route(origin+'/**',async route=>{
            const req=route.request(),url=new URL(req.url()),json=(data,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
            if(url.pathname.startsWith('/Admin/')||url.pathname.startsWith('/lib/'))return route.fulfill({contentType:url.pathname.endsWith('.css')?'text/css':'text/javascript',body:fs.readFileSync(path.join(web,'wwwroot',url.pathname),'utf8')});
            if(url.pathname===base)return route.fulfill({contentType:'text/html',body:html()});
            if(deny)return json({message:'Không có quyền xem ca này.'},403);
            if(req.method()==='POST'){
                assert.equal(req.headers().requestverificationtoken,'synthetic-token');
                const input=req.postDataJSON();calls.push({path:url.pathname,input});
                if(url.pathname===base+'/payments/11/requests'){
                    assert.equal(input.amount,undefined,'UI must never propose a replacement received amount');
                    assert.equal(input.method,1);assert.equal(input.reference,'');assert.equal(input.rowVersion,'pay-v1');
                    if(!request)request={id:7,paymentId:11,orderId:10,number:'POS-010',shiftId:5,shiftCode:'CA-005',requestedBy:'Nguyễn Thu Ngân',createdAtUtc:date,
                        status:'Pending',requestReason:input.requestReason,amount:250000,oldMethod:'Cash',newMethod:'BankTransfer',oldReference:null,newReference:null,
                        expectedDelta:-215000,rowVersion:'request-v1',appliedToClosedShift:false,clientRequestId:input.clientRequestId};
                    else assert.equal(input.clientRequestId,request.clientRequestId,'Retries keep the original identity');
                    if(failCreateOnce){failCreateOnce=false;return json({message:'Phản hồi bị gián đoạn. Kiểm tra hoặc gửi lại cùng yêu cầu.'},503);}return json({id:7});
                }
                if(url.pathname===base+'/7/approve'){
                    assert.ok(isAdmin);assert.equal(input.shiftRowVersion,'shift-v1');assert.equal(input.rowVersion,'request-v1');
                    request.beforeShiftJson=JSON.stringify(current);Object.assign(current,proposed(1));current.needsReconciliation=true;
                    request.afterShiftJson=JSON.stringify(current);request.status='Approved';request.reviewedBy='Quản lý';request.reviewedAtUtc=date;
                    request.reviewNote=input.note;request.appliedToClosedShift=true;request.rowVersion='request-v2';shiftVersion='shift-v2';
                    candidate.method='BankTransfer';candidate.reference=null;candidate.rowVersion='pay-v2';return json({success:true});
                }
                if(url.pathname===base+'/shifts/5/reconcile'){
                    assert.ok(isAdmin);assert.equal(input.rowVersion,'shift-v2');assert.ok(input.note.trim());
                    current.needsReconciliation=false;request.reconciledAtUtc=date;request.reconciledBy='Quản lý';request.reconciliationNote=input.note;shiftVersion='shift-v3';return json({success:true});
                }
                if(url.pathname===base+'/deposits/31/requests'){
                    assert.equal(input.amount,undefined);assert.equal(input.method,0);assert.equal(input.reference,null);assert.ok(input.requestReason);
                    depositRequest={...depositCandidate,id:8,paymentId:0,requestedBy:'Nguyễn Thu Ngân',createdAtUtc:date,status:'Pending',requestReason:input.requestReason,
                        oldMethod:'BankTransfer',newMethod:'Cash',oldReference:'COC-GOC',newReference:null,expectedDelta:100000,rowVersion:'deposit-request-v1'};
                    return json({id:8});
                }
                if(url.pathname===base+'/8/approve'){
                    assert.ok(isAdmin);assert.equal(input.shiftRowVersion,shiftVersion);Object.assign(current,depositProposed());
                    depositRequest.status='Approved';depositRequest.reviewedBy='Quản lý';depositRequest.reviewedAtUtc=date;depositCandidate.method='Cash';return json({success:true});
                }
                throw Error('Unexpected mutation '+url.pathname);
            }
            if(url.pathname===base+'/payments'){
                const row={...candidate,canRequest:!request||request.status!=='Pending',pendingRequestId:request?.status==='Pending'?7:null};
                const items=url.searchParams.has('paymentId')?[row]:[row,protectedReceipt];return json({items,totalPages:1,totalItems:items.length});
            }
            if(url.pathname===base+'/data'){
                const status=url.searchParams.get('status'),items=[request,depositRequest].filter(r=>r&&(!status||r.status===status));
                return json({items,totalPages:1,totalItems:items.length});
            }
            if(url.pathname===base+'/7')return json(detail());
            if(url.pathname===base+'/deposits')return json({items:[depositCandidate],totalPages:1,totalItems:1});
            if(url.pathname===base+'/deposits/31/preview')return json({currentShift:current,proposedShift:Number(url.searchParams.get('method'))===0?depositProposed():current,expectedDelta:Number(url.searchParams.get('method'))===0?100000:0});
            if(url.pathname===base+'/8')return json({request:depositRequest,currentShift:current,proposedShift:depositRequest.status==='Pending'?depositProposed():current,
                shiftRowVersion:shiftVersion,canApprove:isAdmin&&depositRequest.status==='Pending',canWithdraw:!isAdmin&&depositRequest.status==='Pending',unavailableReason:null});
            if(url.pathname===base+'/payments/11/preview')return json({currentShift:current,proposedShift:proposed(Number(url.searchParams.get('method'))),expectedDelta:Number(url.searchParams.get('method'))===0?0:-215000});
            throw Error('Unexpected read '+url.pathname);
        });
        const page=await context.newPage();page.setDefaultTimeout(10000);page.on('pageerror',e=>errors.push(e.message));
        await page.goto(origin+base+'?paymentId=11&shiftId=5');await page.locator('[data-create="11"]').waitFor();
        assert.match(await page.locator('#paRows').innerText(),/13:00:00/,'UTC receipt time is displayed in the cashier timezone');await page.locator('[data-create="11"]').click();
        await page.locator('#paMethod').selectOption('1');assert.equal(await page.locator('#paReference').getAttribute('required'),null);
        await page.locator('#paReason').fill('Nhập nhầm tiền mặt; khách đã chuyển khoản, đã đối chiếu mã BANK-250.');
        await page.waitForFunction(()=>!document.getElementById('paSend').disabled);
        assert.match(await page.locator('#paPreview').innerText(),/-215.000 đ/);
        assert.equal(await page.locator('#paDialog input[type="number"]').count(),0,'The received amount stays read-only');
        assert.equal(await page.locator('#paReference').isVisible(),false,'Optional bank reference stays in the collapsed extra information');
        if(output){fs.mkdirSync(output,{recursive:true});await page.locator('#paDialog').screenshot({path:path.join(output,'cashier-payment-request.png'),animations:'disabled'});}
        await page.locator('#paSend').click();await page.locator('#paDialogError').waitFor({state:'visible'});
        assert.equal(current.expected,315000,'Creating a request cannot alter money');
        await page.evaluate(()=>{const f=document.getElementById('paCreateForm');f.requestSubmit();f.requestSubmit();});
        await page.waitForFunction(()=>document.getElementById('paDialogTitle').textContent.includes('Yêu cầu #7'));
        assert.equal(calls.filter(x=>x.path.endsWith('/requests')).length,2,'One failed-response retry, no double submission');
        assert.equal(await page.locator('[data-decision="approve"]').count(),0,'Cashier cannot approve');
        assert.ok(await page.locator('[data-decision="withdraw"]').isVisible());
        await page.locator('#paClose').click();await page.locator('#paClear').click();await page.locator('[data-tab="payments"]').click();
        await page.waitForFunction(()=>document.getElementById('paRows').textContent.includes('chứng từ ngân hàng'));
        assert.equal(await page.locator('[data-create="12"]').count(),0,'Automated bank evidence cannot be relabelled');
        await page.setViewportSize({width:390,height:844});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
        if(output)await page.screenshot({path:path.join(output,'cashier-payments-mobile.png'),fullPage:true,animations:'disabled'});
        isAdmin=true;await page.setViewportSize({width:1440,height:1080});await page.goto(origin+base+'?requestId=7&shiftId=5');
        await page.locator('[data-decision="approve"]').waitFor({state:'visible'});
        assert.match(await page.locator('#paDialog').innerText(),/315.000 đ/);assert.match(await page.locator('#paDialog').innerText(),/100.000 đ/);
        if(output)await page.locator('#paDialog').screenshot({path:path.join(output,'manager-before-approval.png'),animations:'disabled'});
        await page.locator('[data-decision="reject"]').click();assert.match(await page.locator('#paDialogError').innerText(),/nhập ghi chú/);
        assert.equal(calls.length,2,'A rejection without a reason must not be sent');
        await page.locator('#paDecisionNote').fill('Đã kiểm tra chứng từ ngân hàng và tiền thực đếm');
        await page.locator('[data-decision="approve"]').click();await page.waitForFunction(()=>document.getElementById('paDialogBody').textContent.includes('Đã duyệt'));
        assert.equal(current.expected,100000);assert.equal(current.actual,100000);assert.equal(current.received,100000);assert.equal(candidate.amount,250000);
        assert.equal(current.needsReconciliation,true);
        await page.locator('#paDecisionNote').fill('Đã dò đủ các giao dịch, quỹ khớp sau điều chỉnh');
        await page.locator('[data-decision="reconcile"]').click();await page.waitForFunction(()=>document.getElementById('paDialogBody').textContent.includes('Đối soát lại bởi'));
        assert.equal(current.needsReconciliation,false);assert.equal(calls.length,4);
        if(output)await page.locator('#paDialog').screenshot({path:path.join(output,'approved-payment-history.png'),animations:'disabled'});
        request.requestReason='<img src=x onerror=alert(1)> nội dung đối chiếu';request.reviewNote='<script>alert(1)</script>';
        await page.reload();await page.waitForFunction(()=>document.getElementById('paDialogBody').textContent.includes('nội dung đối chiếu'));
        assert.equal(await page.locator('#paDialog img, #paDialog script').count(),0,'Request reasons and reviewer notes are rendered as text');
        isAdmin=false;await page.goto(origin+base+'?entryId=31&shiftId=5');await page.locator('[data-create="31"]').click();
        assert.equal(await page.locator('#paMethod option').count(),2,'Deposits support cash and transfer only');
        assert.match(await page.locator('#paDialog').innerText(),/Khách Nguyễn/);await page.locator('#paMethod').selectOption('0');
        await page.locator('#paReason').fill('Cọc nhận bằng tiền mặt, chọn nhầm chuyển khoản');await page.waitForFunction(()=>!document.getElementById('paSend').disabled);
        assert.match(await page.locator('#paPreview').innerText(),/\+100.000 đ/);
        if(output)await page.locator('#paDialog').screenshot({path:path.join(output,'deposit-request-simple.png'),animations:'disabled'});
        await page.locator('#paSend').click();await page.waitForFunction(()=>document.getElementById('paDialogTitle').textContent.includes('Yêu cầu #8'));
        assert.equal(await page.locator('[data-decision="approve"]').count(),0);
        isAdmin=true;await page.goto(origin+base+'?requestId=8');await page.locator('[data-decision="approve"]').waitFor({state:'visible'});
        assert.equal(await page.locator('.pa-extra[open]').count(),0,'Deep shift information is collapsed initially');
        await page.locator('[data-decision="approve"]').click();await page.waitForFunction(()=>document.getElementById('paDialogBody').textContent.includes('Đã duyệt'));
        assert.equal(current.expected,200000);assert.equal(current.nonCashSales,215000);assert.equal(depositCandidate.amount,100000);
        deny=true;await page.goto(origin+base);await page.locator('#paMessage').waitFor({state:'visible'});
        assert.match(await page.locator('#paMessage').innerText(),/Không có quyền/);assert.deepEqual(errors,[]);
        console.log('PASS: optional transfer reference, deposit request/approval, compact UI, capped preview, immutable amount, retry, double-submit guard, permissions, bank protection, reconciliation, escaping and mobile layout.');
    }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
