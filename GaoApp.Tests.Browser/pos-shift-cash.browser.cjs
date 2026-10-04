'use strict';
// Actual shift view/scripts with synthetic web APIs and a real loopback helper transport.
// Never connects to the business database, printer, or physical drawer.
const {chromium} = require('playwright');
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const web = path.resolve(__dirname, '../GaoApp.Web');
const origin = 'https://gao-shift.test';
const output = process.env.GAO_SHIFT_TEST_OUTPUT;
const posts = [], events = [], saved = [], audits = [], errors = [];
let pending, behavior = 'hold', capability = true, denyAudit = false, failSave = false, canReconcile = true;
let shift = {id:5,shiftCode:'CA-TEST-005',openedByUserName:'Nguyễn Thu Ngân',terminalName:'Máy tính tiền 02',openedAtUtc:'2026-09-29T00:00:00Z',openingCash:100000,
    cashInTotal:0,cashOutTotal:0,closingCashExpected:100000,status:0};
const server = http.createServer((req,res) => {
    if(req.headers.origin !== origin) {res.writeHead(403);res.end();return;}
    res.setHeader('Access-Control-Allow-Origin',origin);
    if(req.method === 'OPTIONS') {
        res.setHeader('Access-Control-Allow-Methods','GET, POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers','Content-Type');
        res.setHeader('Access-Control-Allow-Private-Network','true');res.writeHead(204);res.end();return;
    }
    res.setHeader('Content-Type','application/json');
    if(req.url === '/health') {res.end(JSON.stringify({ok:true,protocol:'gao-raster-v1',width:576,cashDrawerOnly:capability}));return;}
    assert.ok(['/print','/drawer'].includes(req.url));assert.equal(req.method,'POST');
    let body='';req.on('data',chunk => body+=chunk);req.on('end',() => {
        const payload = JSON.parse(body);posts.push({url:req.url,payload});events.push(req.url);
        const finish = () => {
            res.statusCode = behavior === 'fail' ? 503 : 200;
            res.end(JSON.stringify({ok:behavior !== 'fail',protocol:'gao-raster-v1',
                ...(req.url === '/drawer' ? {drawerOpened:true} : {printed:true,copies:payload.copies})}));
        };
        if(behavior === 'hold') pending=finish;else finish();
    });
});
async function waitFor(predicate) {
    const deadline=Date.now()+15000;
    while(!predicate()) {if(Date.now()>deadline) throw new Error('Condition timed out');await new Promise(resolve=>setTimeout(resolve,25));}
}
(async() => {
    await new Promise((resolve,reject) => {server.once('error',reject);server.listen(17880,'127.0.0.1',resolve);});
    const browser=await chromium.launch({channel:'chrome',headless:true});
    try {
        const context=await browser.newContext({viewport:{width:1360,height:1000}});
        await context.grantPermissions(['local-network-access'],{origin});
        await context.addInitScript(() => {
            localStorage.setItem('gao-pos-print-v1:1:2',JSON.stringify({mode:'helper',copies:1}));
            window.__handled=[];
            const fetchJson=async(url,options) => {
                const response=await fetch(url,options);const data=await response.json();
                if(!response.ok) throw new Error(data.message || 'Request failed');return data;
            };
            window.PosError={fetchJson,postJson:(url,data,getToken) => fetchJson(url,{method:'POST',
                headers:{'Content-Type':'application/json',RequestVerificationToken:getToken()},body:JSON.stringify(data)}),
                handle:error=>window.__handled.push(error.message),clearBanner:()=>{}};
        });
        await context.route(origin+'/**',async route => {
            const request=route.request(), url=new URL(request.url());
            const json=(data,status=200) => route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
            if(url.pathname==='/admin/api/warehouses') return json([]);
            if(url.pathname==='/admin/pos/shift/current') return json(shift);
            if(url.pathname==='/admin/pos/shift/cash-transactions') return json([...saved].reverse());
            if(url.pathname==='/admin/pos/shift/cash-transaction') {
                if(failSave) return json({message:'Không lưu được phiếu'},400);
                const data=request.postDataJSON();
                const transaction={...data,id:saved.length+1,posShiftId:5,type:data.type===1?'CashIn':'CashOut',createdAtUtc:'2026-09-29T03:15:00Z'};
                saved.push(transaction);events.push('saved');return json(transaction);
            }
            if(url.pathname==='/admin/pos/shift/cash-drawer') {
                if(denyAudit) return json({message:'Không có quyền mở két'},403);
                audits.push(request.postDataJSON());events.push('audit');return json({auditId:audits.length,shiftId:5});
            }
            if(['/admin/pos/shift/cash-drawer/receive','/admin/pos/shift/cash-drawer/close'].includes(url.pathname)) {
                if(denyAudit) return json({message:'Không có quyền thao tác ca'},403);
                const purpose=url.pathname.endsWith('/receive')?'Receive':'Close';
                const body=request.postDataJSON();
                assert.equal(body.shiftId,purpose==='Receive'?null:5);
                assert.equal(body.reason,undefined,'Reason is fixed by the server');
                audits.push({purpose,...body});events.push('audit');
                return json({auditId:audits.length,shiftId:body.shiftId,purpose});
            }
            if(['/admin/pos/shift/open','/admin/pos/shift/close'].includes(url.pathname))
                throw new Error('Drawer confirmation must not open or close the shift');
            if(url.pathname.startsWith('/Admin/') || url.pathname.startsWith('/lib/')) {
                return route.fulfill({contentType:url.pathname.endsWith('.css')?'text/css':'text/javascript',
                    body:fs.readFileSync(path.join(web,'wwwroot',url.pathname),'utf8')});
            }
            const raw=fs.readFileSync(path.join(web,'Areas/Admin/Views/POSShiftPage/Index.cshtml'),'utf8');
            const body=raw.slice(raw.indexOf('<div class="shift-shell">'),raw.indexOf('@section PageScripts'))
                .replace('@Html.AntiForgeryToken()','<input type="hidden" name="__RequestVerificationToken" value="synthetic">')
                .replace('@Html.Raw(JsonSerializer.Serialize(printContext))',JSON.stringify({storeId:1,terminalId:2,
                    storeName:'CỬA HÀNG THỬ NGHIỆM',terminalName:'Quầy 02',userName:'Thu ngân thử',canReconcile}));
            const scripts=['/lib/bootstrap/dist/js/bootstrap.bundle.min.js','/Admin/js/printing/print.lifecycle.js',
                '/Admin/js/printing/pos.printing.js','/Admin/js/printing/shift.cash-printing.js','/Admin/js/pos/pos.shift.page.js'];
            return route.fulfill({contentType:'text/html',body:'<!doctype html><meta charset="utf-8">'+
                '<link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css"><link rel="stylesheet" href="/Admin/css/pos/pos-shift.css">'+
                body+scripts.map(src=>`<script src="${src}"></script>`).join('')});
        });
        const page=await context.newPage();page.setDefaultTimeout(10000);
        page.on('pageerror',error=>errors.push(error.message));
        await page.goto(origin+'/admin/pos-shift');
        await page.locator('#btnShowDrawerModal').waitFor();
        assert.equal(await page.locator('#shiftDisplayName').innerText(),'Nguyễn Thu Ngân · Máy tính tiền 02');
        assert.equal(await page.locator('#shiftCode').isVisible(),false,'Technical shift code stays in collapsed details');
        assert.equal(await page.evaluate(()=>isSecureContext),true);
        // Money grouping must remain editable and must never change the submitted amount.
        await page.locator('#btnShowCashTxnModal').click();
        const amountInput=page.locator('#cashTxnAmount');
        await amountInput.fill('');await amountInput.pressSequentially('30000000');
        assert.equal(await amountInput.inputValue(),'30.000.000');
        await amountInput.press('Backspace');assert.equal(await amountInput.inputValue(),'3.000.000');
        await amountInput.press('0');assert.equal(await amountInput.inputValue(),'30.000.000');
        await amountInput.evaluate(el=>el.setSelectionRange(1,1));await amountInput.press('9');
        assert.equal(await amountInput.inputValue(),'390.000.000');
        assert.equal(await amountInput.evaluate(el=>el.selectionStart),2,'Caret stays next to the inserted digit');
        await amountInput.fill('1234');await amountInput.evaluate(el=>el.setSelectionRange(2,2));
        await amountInput.press('Backspace');assert.equal(await amountInput.inputValue(),'234');
        await amountInput.fill('1234');await amountInput.evaluate(el=>el.setSelectionRange(1,1));
        await amountInput.press('Delete');assert.equal(await amountInput.inputValue(),'134');
        for (const [pasted,formatted] of [['30,000,000','30.000.000'],['30.000.000 đ','30.000.000'],
            ['100,000.50','100.000,50'],['100000.50','100.000,50']]) {
            await amountInput.evaluate((el,text)=>{
                el.select();const clipboardData=new DataTransfer();clipboardData.setData('text',text);
                el.dispatchEvent(new ClipboardEvent('paste',{clipboardData,bubbles:true,cancelable:true}));
            },pasted);
            assert.equal(await amountInput.inputValue(),formatted);
        }
        await amountInput.fill('30000000');
        await page.locator('[data-cash-txn-type="1"]').click();
        assert.equal(await amountInput.inputValue(),'30.000.000','Changing type preserves the entered amount');
        if(output) {
            fs.mkdirSync(output,{recursive:true});
            await page.locator('#cashTxnReason').fill('Chi mua vật tư tại quầy');
            await page.locator('[data-cash-txn-type="2"]').click();
            await page.locator('#cashTxnModal .modal-content').screenshot({path:path.join(output,'cash-grouped-desktop.png'),animations:'disabled'});
            await page.setViewportSize({width:390,height:844});
            await page.locator('#cashTxnModal .modal-content').screenshot({path:path.join(output,'cash-grouped-mobile.png'),animations:'disabled'});
            await page.setViewportSize({width:1360,height:1000});
        }
        await page.locator('#cashTxnModal [data-bs-dismiss="modal"]').first().click();
        await page.locator('#cashTxnModal').waitFor({state:'hidden'});
        async function create(type,amount,reason) {
            await page.locator('#btnShowCashTxnModal').click();
            // The fixture responds instantly; wait for Bootstrap's opening transition before submitting.
            await page.locator('#cashTxnModal .modal-dialog').evaluate(async el=>{
                await Promise.all(el.getAnimations().map(animation=>animation.finished));
            });
            await page.locator(`[data-cash-txn-type="${type}"]`).click();
            await page.locator('#cashTxnAmount').fill(String(amount));
            assert.equal(await amountInput.inputValue(),Number(amount).toLocaleString('vi-VN'));
            await page.locator('#cashTxnReason').fill(reason);
            // Two immediate clicks must still create only one saved transaction and one print job.
            await page.evaluate(()=>{const b=document.getElementById('btnAddCashTxn');b.click();b.click();});
        }
        await create(1,100000,'Bổ sung tiền lẻ tại quầy <kiểm tra>');
        await waitFor(()=>posts.length===1);
        assert.deepEqual(events,['saved','/print']);assert.equal(saved.length,1);
        assert.equal(saved[0].amount,100000,'Grouped display must submit the complete numeric amount');
        assert.equal(posts[0].payload.cashDrawer,true);assert.equal(posts[0].payload.width,576);
        assert.equal(posts[0].payload.cut,true);assert.ok(Buffer.from(posts[0].payload.rasterBase64,'base64').some(byte=>byte));
        assert.equal(await page.locator('#btnAddCashTxn').isDisabled(),true);
        const frame=page.frameLocator('iframe[title="Phiếu thu/chi tiền mặt"]');
        assert.match(await frame.locator('.receipt').innerText(),/PHIẾU THU TIỀN MẶT/);
        assert.match(await frame.locator('.receipt').innerText(),/Bổ sung tiền lẻ tại quầy <kiểm tra>/);
        if(output) {
            fs.mkdirSync(output,{recursive:true});
            // Make the test-only offscreen iframe visible while its helper ACK is held for visual QA.
            await page.locator('#cashTxnModal').waitFor({state:'hidden'});
            await page.locator('iframe[title="Phiếu thu/chi tiền mặt"]').evaluate(el=>{el.style.left='0';el.style.zIndex='2147483647';});
            await frame.locator('.receipt').screenshot({path:path.join(output,'cash-in-voucher.png')});
            const bitmap=await page.evaluate(payload=>{
                const canvas=document.createElement('canvas');canvas.width=payload.width;canvas.height=payload.height;
                const ctx=canvas.getContext('2d'),data=ctx.createImageData(canvas.width,canvas.height),bytes=atob(payload.rasterBase64);
                for(let i=0;i<canvas.width*canvas.height;i++) {
                    data.data[i*4]=data.data[i*4+1]=data.data[i*4+2]=(bytes.charCodeAt(i>>3)&(128>>(i&7)))?0:255;
                    data.data[i*4+3]=255;
                }
                ctx.putImageData(data,0,0);return canvas.toDataURL();
            },posts[0].payload);
            fs.writeFileSync(path.join(output,'cash-in-bitmap.png'),Buffer.from(bitmap.split(',')[1],'base64'));
        }
        behavior='ok';pending();
        await page.locator('#shiftCashFeedback').filter({hasText:'Đã gửi phiếu #1'}).waitFor();
        await page.locator('[data-print-cash="1"]').click();await waitFor(()=>posts.length===2);
        assert.equal(posts[1].payload.cashDrawer,false);assert.equal(saved.length,1);
        await create(2,40000,'Chi mua vật tư');await waitFor(()=>posts.length===3);
        assert.equal(posts[2].payload.cashDrawer,true);assert.equal(saved[1].type,'CashOut');
        assert.equal(saved[1].amount,40000);
        await page.locator('[data-print-cash="2"]').click();await waitFor(()=>posts.length===4);
        assert.equal(posts[3].payload.cashDrawer,false);
        // Save failure cannot print. Print failure keeps the saved voucher, and retry is a reprint.
        failSave=true;await create(1,10000,'Không lưu');await page.waitForFunction(()=>__handled.includes('Không lưu được phiếu'));
        assert.equal(posts.length,4);assert.equal(saved.length,2);failSave=false;
        await page.locator('#cashTxnModal [data-bs-dismiss="modal"]').first().click();
        behavior='fail';await create(1,20000,'Thử lỗi in');
        await page.locator('#shiftCashFeedback').filter({hasText:'Phiếu #3 đã được lưu'}).waitFor();
        assert.equal(saved.length,3);assert.equal(posts.length,5);
        behavior='ok';await page.locator('[data-print-cash="3"]').click();await waitFor(()=>posts.length===6);
        assert.equal(posts[5].payload.cashDrawer,false);assert.equal(saved.length,3);
        // Standalone drawer requires reason and a successful saved audit before its single pulse request.
        await page.locator('#btnShowDrawerModal').click();await page.locator('#btnOpenCashDrawer').click();
        await page.locator('#cashDrawerError').filter({hasText:'Nhập lý do'}).waitFor();
        assert.equal(audits.length,0);assert.equal(posts.length,6);
        await page.locator('#cashDrawerModal [data-bs-dismiss="modal"]').first().click();
        await page.locator('#cashDrawerModal').waitFor({state:'hidden'});
        await page.locator('#btnShowDrawerModal').click();
        await page.locator('#cashDrawerReason').fill('  Kiểm đếm tiền trong két  ');
        if(output) await page.screenshot({path:path.join(output,'drawer-reason-dialog.png')});
        behavior='hold';const before=events.length;
        await page.waitForFunction(()=>!document.getElementById('btnOpenCashDrawer').disabled);
        await page.evaluate(()=>{const b=document.getElementById('btnOpenCashDrawer');b.click();b.click();});
        await waitFor(()=>posts.length===7);
        assert.deepEqual(events.slice(before),['audit','/drawer']);
        assert.deepEqual(audits,[{shiftId:5,reason:'Kiểm đếm tiền trong két'}]);
        assert.deepEqual(posts[6],{url:'/drawer',payload:{protocol:'gao-raster-v1',cashDrawer:true}});
        assert.equal(saved.length,3);assert.equal(await page.locator('#btnOpenCashDrawer').isDisabled(),true);
        // Dismissing/reopening the dialog cannot bypass the pending command guard.
        await page.locator('#cashDrawerModal [data-bs-dismiss="modal"]').first().click();
        await page.locator('#cashDrawerModal').waitFor({state:'hidden'});
        assert.equal(await page.locator('#btnShowDrawerModal').isDisabled(),true);
        behavior='ok';pending();pending=null;await page.locator('#shiftCashFeedback').filter({hasText:'Đã lưu lý do'}).waitFor();
        denyAudit=true;await page.locator('#btnShowDrawerModal').click();await page.locator('#cashDrawerReason').fill('Thử thiếu quyền');
        await page.locator('#btnOpenCashDrawer').click();await page.locator('#cashDrawerError').filter({hasText:'Không có quyền'}).waitFor();
        assert.equal(posts.length,7);assert.equal(audits.length,1);denyAudit=false;
        capability=false;await page.locator('#btnOpenCashDrawer').click();
        await page.locator('#cashDrawerError').filter({hasText:'Cập nhật'}).waitFor();
        assert.equal(posts.length,7);assert.equal(audits.length,1);capability=true;
        // Unknown helper outcome must never cause an automatic retry.
        behavior='fail';await page.locator('#btnOpenCashDrawer').click();
        await page.locator('#cashDrawerError').filter({hasText:'Kiểm tra két'}).waitFor();
        assert.equal(posts.length,8);assert.equal(audits.length,2);behavior='ok';
        await page.locator('#cashDrawerModal [data-bs-dismiss="modal"]').first().click();
        await page.locator('#cashDrawerModal').waitFor({state:'hidden'});
        await page.locator('.modal-backdrop').waitFor({state:'detached'});
        if(output) await page.screenshot({path:path.join(output,'cash-voucher-history.png')});
        const count=posts.length;await page.reload();await page.locator('#btnShowDrawerModal').waitFor();
        assert.equal(posts.length,count,'Reload/history must not print or open drawer');
        canReconcile=false;await page.reload();await page.locator('#openShiftSection').waitFor();
        assert.equal(await page.locator('#btnShowDrawerModal').isHidden(),true);
        assert.equal(await page.locator('#btnShowCashTxnModal').isHidden(),true);
        // Closing uses shift permission, not the manual drawer reason dialog.
        await page.locator('#btnShowCloseModal').click();
        await page.locator('#shiftDrawerConfirmModal').waitFor();
        assert.equal(await page.locator('#shiftDrawerAutoReason').innerText(),'Mở két chốt ca');
        assert.equal(await page.locator('#closeShiftModal').isVisible(),false);
        await page.locator('#btnCancelShiftDrawer').click();
        await page.locator('#shiftDrawerConfirmModal').waitFor({state:'hidden'});
        assert.equal(posts.length,count,'Cancel never opens the drawer');
        await page.locator('#btnShowCloseModal').click();
        await page.waitForFunction(()=>!document.getElementById('btnConfirmShiftDrawer').disabled);
        if(output) await page.screenshot({path:path.join(output,'close-shift-drawer.png')});
        behavior='hold';const closeBefore=events.length;
        await page.locator('#btnConfirmShiftDrawer').evaluate(button=>{button.click();button.click();});
        await waitFor(()=>events.length===closeBefore+2 && !!pending);
        assert.deepEqual(events.slice(closeBefore),['audit','/drawer']);
        assert.equal(await page.locator('#btnCancelShiftDrawer').isDisabled(),true);
        assert.equal(await page.locator('#closeShiftModal').isVisible(),false);
        pending();pending=null;behavior='ok';
        await page.locator('#closeShiftModal').waitFor();
        await page.locator('#closeShiftModal [data-bs-dismiss="modal"]').first().click();
        await page.locator('#closeShiftModal').waitFor({state:'hidden'});
        shift=null;await page.reload();await page.locator('#noOpenShiftSection').waitFor();
        assert.equal(await page.locator('#btnShowDrawerModal').isHidden(),true);
        const receiveBefore=posts.length;
        await page.locator('#btnShowOpenModal').click();
        await page.waitForFunction(()=>!document.getElementById('btnConfirmShiftDrawer').disabled);
        assert.equal(await page.locator('#shiftDrawerAutoReason').innerText(),'Mở két nhận ca');
        if(output) await page.screenshot({path:path.join(output,'receive-shift-drawer.png')});
        denyAudit=true;await page.locator('#btnConfirmShiftDrawer').click();
        await page.locator('#shiftDrawerConfirmError').filter({hasText:'Không có quyền'}).waitFor();
        assert.equal(posts.length,receiveBefore);denyAudit=false;
        capability=false;await page.locator('#btnConfirmShiftDrawer').click();
        await page.locator('#shiftDrawerConfirmError').filter({hasText:'Cập nhật'}).waitFor();
        assert.equal(posts.length,receiveBefore);capability=true;
        behavior='fail';await page.locator('#btnConfirmShiftDrawer').click();
        await page.locator('#shiftDrawerConfirmError').filter({hasText:'Kiểm tra két'}).waitFor();
        assert.equal(posts.length,receiveBefore+1);
        // Continue after an uncertain helper response must never send another pulse.
        await page.locator('#btnContinueShiftCount').click();
        await page.locator('#openShiftModal').waitFor();
        assert.equal(posts.length,receiveBefore+1);
        await page.locator('#openShiftModal [data-bs-dismiss="modal"]').first().click();
        await page.locator('#openShiftModal').waitFor({state:'hidden'});
        behavior='ok';await page.locator('#btnShowOpenModal').click();
        await page.locator('#btnConfirmShiftDrawer').click();
        await page.locator('#openShiftModal').waitFor();
        assert.equal(posts.length,receiveBefore+2);
        assert.deepEqual(posts.at(-1),{url:'/drawer',payload:{protocol:'gao-raster-v1',cashDrawer:true}});
        assert.deepEqual(errors,[]);
        console.log(JSON.stringify({result:'PASS',savedVouchers:saved.length,printJobs:posts.filter(p=>p.url==='/print').length,
            drawerRequests:posts.filter(p=>p.url==='/drawer').length,auditRequests:audits.length,
            cases:['live money grouping','caret editing','localized paste','cash in/out','reprint never opens','double clicks','failed save/print','required reason','audit before pulse',
                'denied audit','old helper','unknown outcome no retry','reload','permission visibility','closed shift'],pageErrors:errors},null,2));
    } finally {await browser.close();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}
})().catch(error=>{console.error(error);process.exitCode=1;server.closeAllConnections();server.close();});
