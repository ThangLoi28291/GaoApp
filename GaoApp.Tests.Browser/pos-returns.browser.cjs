'use strict';
// Real order-detail modal markup and production assets, with synthetic APIs only.
const {chromium}=require('playwright');
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const web=path.resolve(__dirname,'../GaoApp.Web'),origin='https://gao-returns.test',output=process.env.GAO_RETURNS_TEST_OUTPUT;
let pending=true,returned=false,credit=false,delay=0,visual=false;const posts=[],errors=[];
function eligibility(){if(visual)return {orderId:10,orderNumber:'POS-20261009-00313',customerName:'Khách lẻ',grandTotal:688820,paidTotal:688820,refundableRemaining:688820,lines:[
    {orderLineId:11,itemName:'Sữa dinh dưỡng ít đường DHA 110ml',sku:'8936123456789',unitName:'Hộp',soldQuantity:48,returnableQuantity:48,multiplier:1,suggestedRefundUnitAmount:5729.1667,canRestock:false,
        restockBlockReason:'Dòng hàng #1768157 chưa thể nhập lại kho vì giá vốn xuất bán còn tạm tính.',restockActionHint:'Quản lý cần kiểm tra phiếu nhập, tồn đầu kỳ và đối soát giá vốn của dòng hàng này. Sau khi xử lý, tải lại đơn để nhập lại kho. Có thể chọn Chờ nhập kho để nhận hàng trả và hoàn tiền ngay.'},
    {orderLineId:12,itemName:'Sữa tươi TH nguyên chất hộp 1L',sku:'8936123456790',unitName:'Hộp',soldQuantity:12,returnableQuantity:10,multiplier:1,unitPrice:39172,canRestock:true},
    {orderLineId:13,itemName:'Sữa chua uống hương dâu ít đường với tên sản phẩm dài để kiểm tra hiển thị',sku:'8936123456791',unitName:'Lốc',soldQuantity:6,returnableQuantity:6,multiplier:4,unitPrice:26500,canRestock:true}]};
    return {orderId:10,grandTotal:200000,paidTotal:200000,refundableRemaining:200000,refundedTotal:returned?20000:0,
    isCreditSale:credit,balanceDue:credit?30000:0,depositAmount:0,lines:[{orderLineId:11,variantId:1,itemName:'100g Đông trùng sấy khô',soldQuantity:2,
    returnedQuantity:returned?1:0,returnableQuantity:returned?1:2,multiplier:1,unitPrice:100000,suggestedRefundUnitAmount:90000,
    canRestock:!pending,restockBlockReason:pending?'Dòng hàng #11 chưa thể nhập lại kho vì giá vốn xuất bán còn tạm tính.':null,
    restockActionHint:'Quản lý cần kiểm tra phiếu nhập, tồn đầu kỳ và đối soát giá vốn.'}]};}
function html(){
    const view=fs.readFileSync(path.join(web,'Areas/Admin/Views/POSOrderDetail/Index.cshtml'),'utf8');
    const modals=view.slice(view.indexOf('<div class="modal fade modal-clean" id="orderActionModal"'),view.indexOf('@section PageScripts'));
    return '<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">'+
        '<link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><link rel="stylesheet" href="/Admin/css/pos/pos-order-detail.css">'+
        '<style>body{padding:24px;background:#f4f5fa}</style><body><h1>Chi tiết đơn POS</h1>'+modals+
        '<input name="__RequestVerificationToken" type="hidden" value="synthetic-token">'+
        '<script>window.__messages=[];window.toastr={error:m=>__messages.push(m),success:()=>{}};window.loadOrderDetail=async()=>{};</script>'+
        '<script src="/lib/bootstrap/dist/js/bootstrap.bundle.min.js"></script><script id="posOrderDetailPageScript" data-grand-total="200000" data-paid-total="200000" data-refundable-remaining="200000" src="/Admin/js/pos/pos.order.detail.page.js"></script></body></html>';
}
(async()=>{
    const browser=await chromium.launch({channel:'chrome',headless:true});
    try{
        const context=await browser.newContext({viewport:{width:1440,height:1000}});
        await context.route(origin+'/**',async route=>{
            const req=route.request(),url=new URL(req.url()),p=url.pathname;
            if(p.startsWith('/Admin/')||p.startsWith('/lib/'))return route.fulfill({contentType:p.endsWith('.css')?'text/css':'text/javascript',body:fs.readFileSync(path.join(web,'wwwroot',p),'utf8')});
            if(p==='/admin/pos/order-detail/10')return route.fulfill({contentType:'text/html',body:html()});
            if(req.method()==='POST'){
                assert.equal(req.headers().requestverificationtoken,'synthetic-token');posts.push({path:p,data:req.postDataJSON()});
                return route.fulfill({contentType:'application/json',body:JSON.stringify({success:true,message:'Đã ghi nhận'})});
            }
            if(p==='/admin/pos/returns/order/10/eligibility'){
                const data=eligibility();if(delay)await new Promise(r=>setTimeout(r,delay));
                return route.fulfill({contentType:'application/json',body:JSON.stringify(data)});
            }
            throw Error('Unexpected read '+p);
        });
        const page=await context.newPage();page.setDefaultTimeout(10000);page.on('pageerror',e=>errors.push(e.message));
        const full=page.locator('#orderActionModal'),partial=page.locator('#returnRefundModal');
        const afterSubmit=async modal=>{
            await modal.waitFor({state:'hidden'});
            await page.waitForFunction(id=>!bootstrap.Modal.getInstance(document.getElementById(id))._isTransitioning&&
                !document.querySelector('#'+id+' .modal-footer [aria-busy="true"]'),await modal.getAttribute('id'));
        };
        await page.addInitScript(()=>{window.__events=[];for(const name of ['show','shown','hide','hidden'])document.addEventListener(name+'.bs.modal',e=>window.__events.push([e.target.id,name,Date.now()]));});
        const close=async modal=>{
            const id=await modal.getAttribute('id');
            await page.waitForFunction(id=>!bootstrap.Modal.getInstance(document.getElementById(id))._isTransitioning,id);
            await page.evaluate(id=>{window.__hidden=new Promise(resolve=>document.getElementById(id).addEventListener('hidden.bs.modal',()=>resolve(true),{once:true}));},id);
            await modal.locator('.modal-header .btn-close').click();await modal.waitFor({state:'hidden'});
            await page.evaluate(()=>window.__hidden);
        };
        await page.goto(origin+'/admin/pos/order-detail/10');await page.evaluate(()=>openOrderActionModal('refund',10));
        await full.waitFor();assert.equal(await page.locator('#btnConfirmOrderAction').isDisabled(),true);
        assert.match(await page.locator('#orderActionCostWarning').innerText(),/Đông trùng.*tạm tính/);
        assert.match(await page.locator('#orderActionCostWarning').innerText(),/Quản lý/);assert.equal(posts.length,0);
        if(output){fs.mkdirSync(output,{recursive:true});await full.screenshot({path:path.join(output,'full-return-cost-warning.png')});}
        await close(full);await page.evaluate(()=>openReturnRefundModal(10));await page.locator('.js-return-qty').waitFor();
        assert.equal(await page.locator('.js-refund-unit').inputValue(),'90000','Suggested refund includes the original line discount');
        assert.equal(await page.locator('.js-restock').inputValue(),'2','Unresolved cost explicitly selects awaiting restock, never NoRestock');
        assert.match(await page.locator('#returnRefundModalBody').innerText(),/tạm tính/);
        await page.locator('#returnReason').fill('Khách trả một món');await page.locator('.js-return-qty').fill('1');
        await page.locator('.js-restock').selectOption('1');
        await page.locator('#refundAmount').fill('90000');await page.locator('#btnSubmitReturnRefund').click();
        await page.waitForFunction(()=>window.__messages.some(m=>m.includes('tạm tính')));assert.equal(posts.length,0,'Blocked restock never submits money or stock');
        if(output)await partial.screenshot({path:path.join(output,'partial-return-cost-warning.png')});
        await page.locator('.js-restock').selectOption('2');
        assert.equal(await page.evaluate(()=>buildReturnRefundRequest().lines[0].action),2);
        await page.locator('#returnType').selectOption('1');assert.equal(await page.locator('.js-return-qty').inputValue(),'0');
        await page.locator('#refundAmount').fill('10000');await page.locator('#btnSubmitReturnRefund').click();await afterSubmit(partial);
        assert.equal(posts.at(-1).data.type,1);assert.deepEqual(posts.at(-1).data.lines,[]);assert.equal(posts.at(-1).data.payments[0].amount,10000);
        pending=false;await page.evaluate(()=>openOrderActionModal('refund',10));await full.waitFor();
        assert.equal(await page.locator('#btnConfirmOrderAction').isDisabled(),false);assert.equal(await page.locator('#orderActionCostWarning').isVisible(),false);
        await page.locator('#orderActionRefundMethod').selectOption('1');await page.locator('#orderActionReason').fill('Trả toàn bộ hàng, hoàn chuyển khoản');
        await page.locator('#btnConfirmOrderAction').click();await afterSubmit(full);assert.equal(posts.at(-1).data.refundMethod,1);
        returned=true;await page.evaluate(()=>openOrderActionModal('refund',10));await full.waitFor();
        assert.equal(await page.locator('#btnConfirmOrderAction').isDisabled(),true);assert.match(await page.locator('#orderActionCostWarning').innerText(),/phần còn lại/);await close(full);
        returned=false;pending=true;credit=true;await page.evaluate(()=>openReturnRefundModal(10));await page.locator('#returnType').waitFor();
        assert.equal(await page.locator('#returnType option[value="1"]').evaluate(el=>el.disabled),true);
        await page.locator('.js-return-qty').fill('1');await page.locator('#returnType').selectOption('2');
        await page.locator('.js-return-qty').fill('1');assert.equal(await page.locator('#refundAmount').inputValue(),'0','Return-only credit must not auto-fill a payment');await close(partial);
        credit=false;pending=false;await page.evaluate(()=>openReturnRefundModal(10));await page.locator('.js-return-qty').waitFor();
        await page.locator('#returnReason').fill('Nhận lại hàng không nhập kho do hỏng');await page.locator('.js-return-qty').fill('1');
        await page.locator('.js-restock').selectOption('0');await page.locator('#refundAmount').fill('90000');
        await page.waitForFunction(()=>!bootstrap.Modal.getInstance(document.getElementById('returnRefundModal'))._isTransitioning);
        assert.equal(await page.evaluate(()=>buildReturnRefundRequest().orderId),10,JSON.stringify(await page.evaluate(()=>window.__events)));
        await page.locator('#btnSubmitReturnRefund').click();await afterSubmit(partial).catch(async error=>{console.error(await page.evaluate(()=>window.__messages),posts);throw error;});assert.equal(posts.at(-1).data.lines[0].action,0);
        pending=true;await page.setViewportSize({width:390,height:844});await page.evaluate(()=>openOrderActionModal('refund',10));await full.waitFor();
        await page.waitForFunction(()=>!bootstrap.Modal.getInstance(document.getElementById('orderActionModal'))._isTransitioning);
        assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
        if(output)await full.screenshot({path:path.join(output,'return-cost-warning-mobile.png')});
        await page.locator('#orderActionPendingRestock').check();
        assert.equal(await page.locator('#btnConfirmOrderAction').isDisabled(),false);
        await page.locator('#orderActionReason').fill('Nhận hàng trả và hoàn tiền, chờ nhập kho');
        await page.locator('#btnConfirmOrderAction').click();await afterSubmit(full);
        assert.equal(posts.at(-1).data.allowPendingRestock,true);
        await page.evaluate(()=>openOrderActionModal('refund',10));await full.waitFor();
        await close(full);delay=600;await page.evaluate(()=>{void openOrderActionModal('refund',10);});await full.waitFor();await close(full);
        delay=0;pending=false;await page.evaluate(()=>openOrderActionModal('refund',10));await full.waitFor();
        await page.waitForTimeout(650);assert.equal(await page.locator('#btnConfirmOrderAction').isDisabled(),false,'Old cost check cannot override a newer popup');
        await close(full);visual=true;await page.setViewportSize({width:1253,height:900});
        const countBeforeVisual=posts.length;
        await page.evaluate(()=>openReturnRefundModal(10));await partial.waitFor();
        assert.equal(await partial.locator('.ret-line-card').count(),3);
        assert.ok(await partial.locator('.ret-line-product h6').first().evaluate(el=>el.getBoundingClientRect().width>450),'Product names have room at the screenshot viewport');
        await partial.locator('.js-return-qty').first().fill('2');
        assert.match(await page.locator('#returnRefundFooterTotal').innerText(),/11\.458/);
        await partial.locator('.ret-line-warning summary').click();
        assert.match(await partial.locator('.ret-line-warning').innerText(),/tồn đầu kỳ/);
        const checkBounds=async()=>assert.ok(await partial.evaluate(el=>{
            const body=el.querySelector('.modal-body'),footer=el.querySelector('.modal-footer');
            return body.scrollWidth<=body.clientWidth+1&&el.scrollWidth<=innerWidth&&
                footer.getBoundingClientRect().bottom<=innerHeight&&
                [...el.querySelectorAll('.js-return-qty,.js-refund-unit,.js-restock,.js-line-reason')].every(input=>input.getBoundingClientRect().width>=90);
        }),'Inputs fit, with no horizontal overflow and the save footer visible');
        await checkBounds();
        await partial.locator('.modal-body').evaluate(el=>el.scrollTop=0);
        if(output)await partial.screenshot({path:path.join(output,'return-popup-desktop.png')});
        await page.setViewportSize({width:390,height:844});await checkBounds();
        await partial.locator('.modal-body').evaluate(el=>el.scrollTop=0);
        if(output)await partial.screenshot({path:path.join(output,'return-popup-mobile.png')});
        await partial.locator('.js-restock').first().scrollIntoViewIfNeeded();
        await partial.locator('.js-line-reason').first().fill('Hộp bị móp');
        assert.equal(await page.evaluate(()=>buildReturnRefundRequest().lines[0].returnQuantity),2);
        assert.equal(await page.evaluate(()=>buildReturnRefundRequest().lines[0].action),2);
        if(output)await partial.screenshot({path:path.join(output,'return-popup-mobile-inputs.png')});
        assert.equal(posts.length,countBeforeVisual,'Visual review and editing never save a return');
        assert.deepEqual(errors,[]);console.log('PASS: upfront cost warning, blocked submission, discounted refund price, refund-only, actual tender, previous returns, credit return-only, explicit NoRestock, mobile and stale response.');
    }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
