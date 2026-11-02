'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {chromium} = require('playwright');
const ui11 = require('./receipt-mobile-review.cjs'); // UI-11 review hook
async function ui11HistoryFailure(page, error) { // UI-11 review hook
    if(!ui11.enabled())return; const folder=ui11.output('history'); // UI-11 review hook
    const reportPath=path.join(folder,'mobile-review.json'), report=fs.existsSync(reportPath)?JSON.parse(fs.readFileSync(reportPath,'utf8')):{}; // UI-11 review hook
    const viewport=report.failures?.at(-1)?.viewport||{width:320,height:568}; await page.setViewportSize(viewport);await ui11.settle(page); // UI-11 review hook
    const diagnostic=await page.evaluate(()=>{ // UI-11 review hook
        const rect=node=>{const r=node.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom};}; // UI-11 review hook
        const describe=node=>{const s=getComputedStyle(node);return{tag:node.tagName,id:node.id,class:typeof node.className==='string'?node.className:'',rect:rect(node),clientWidth:node.clientWidth,scrollWidth:node.scrollWidth,display:s.display,position:s.position,minWidth:s.minWidth,maxWidth:s.maxWidth,overflowX:s.overflowX,whiteSpace:s.whiteSpace,flex:s.flex,gridTemplateColumns:s.gridTemplateColumns,text:node.textContent?.trim().slice(0,140)};}; // UI-11 review hook
        const closedDetails=node=>{for(let ancestor=node.parentElement;ancestor;ancestor=ancestor.parentElement){if(ancestor.tagName!=='DETAILS'||ancestor.open)continue;const summary=[...ancestor.children].find(child=>child.tagName==='SUMMARY');if(!summary?.contains(node))return true;}return false;}; // UI-11 review hook
        const scroller=node=>{for(let ancestor=node.parentElement;ancestor&&ancestor!==document.body;ancestor=ancestor.parentElement){const s=getComputedStyle(ancestor),r=rect(ancestor);if(/auto|scroll|hidden|clip/.test(s.overflowX)&&r.x>=-1&&r.right<=innerWidth+1)return ancestor;}return null;}; // UI-11 review hook
        const candidates=[...document.querySelectorAll('body *')].filter(node=>{const r=rect(node),s=getComputedStyle(node);return r.width&&r.height&&(r.right>innerWidth+1||r.x< -1)&&s.display!=='none'&&s.visibility==='visible'&&s.position!=='fixed'&&!closedDetails(node);}); // UI-11 review hook
        return{viewport:{width:innerWidth,height:innerHeight},document:{clientWidth:document.documentElement.clientWidth,scrollWidth:document.documentElement.scrollWidth,scrollX,scrollY},visibleOverflow:candidates.filter(node=>!scroller(node)).slice(0,80).map(node=>({element:describe(node),ancestors:[node.parentElement,node.parentElement?.parentElement,node.parentElement?.parentElement?.parentElement,node.parentElement?.parentElement?.parentElement?.parentElement].filter(Boolean).map(describe)})),containedHorizontalScroll:candidates.filter(node=>scroller(node)).slice(0,30).map(node=>({element:describe(node),scroller:describe(scroller(node))})),excludedNativeClosedDetails:[...document.querySelectorAll('details:not([open])')].length}; // UI-11 review hook
    }); // UI-11 review hook
    fs.writeFileSync(path.join(folder,'history-overflow-diagnostic.json'),JSON.stringify({error:String(error),...diagnostic},null,2)+'\n'); // UI-11 review hook
    await page.screenshot({path:path.join(folder,`history-overflow-${viewport.width}-viewport.png`),fullPage:false,animations:'disabled'}); // UI-11 review hook
} // UI-11 review hook
(async () => {
    const info = JSON.parse(await new Promise(resolve => {let data='';process.stdin.on('data', x=>data+=x);process.stdin.on('end', ()=>resolve(data));}));
    const output = path.resolve('TestResults/receipt-history/browser'); fs.mkdirSync(output, {recursive:true});
    const browser = await chromium.launch({channel:'chrome', headless:true});
    const context = await browser.newContext({viewport:{width:1440,height:1050},timezoneId:'Asia/Bangkok'});
    await ui11.protect(context, info.baseUrl); // UI-11 review hook
    const page = await context.newPage(), errors=[];
    page.on('pageerror', error=>errors.push(error.message));
    const url = info.baseUrl+`/admin/stock-documents/${info.receiptId}`;
    const auditUrl = `**/admin/api/stock-documents/${info.receiptId}/audit-events`;
    async function login(target, credentials) {
        await target.goto(info.baseUrl+'/admin/account/login');
        await target.locator('[name=UserName]').fill(credentials.user);
        await target.locator('[name=Password]').fill(credentials.password);
        const terminal=target.locator('[name=SelectedTerminalId]'); if(await terminal.count()) await terminal.selectOption(String(info.terminalId));
        await Promise.all([target.waitForURL(url=>!url.pathname.endsWith('/login')),target.locator('button[type=submit]').click()]);
    }
    try {
        await login(page, info); await page.goto(url);
        const card=page.locator('#purchaseReceiptAuditCard'), entries=card.locator('.rh-event');
        await require('./browser-contract.cjs').reveal(page, '#purchaseReceiptAuditCard');
        await card.locator('#receiptHistoryStats').filter({hasText:'53 thao tác'}).waitFor();
        assert.equal(await entries.count(),12);
        assert.equal(await entries.first().locator('.rh-title').innerText(),'Sửa nhà cung cấp sau duyệt');
        assert.equal(await entries.first().locator('time').innerText(),'13:53:00','Unzoned SQL UTC must be rendered in local time.');
        assert.equal(await entries.first().locator('.rh-product').innerText(),'Sữa tươi ít đường 180 ml');
        assert.equal(await card.locator('pre:visible').count(),0,'Raw audit evidence must be collapsed by default.');
        await ui11.review(page,'receipt-history-collapsed',ui11.output('history'),{root:'#purchaseReceiptAuditCard'}).catch(async error=>{await ui11HistoryFailure(page,error);throw error;}); // UI-11 review hook
        await card.locator('#receiptHistoryMore').click(); assert.equal(await entries.count(),24);
        await card.locator('#receiptHistorySort').selectOption('oldest');
        assert.equal(await entries.first().locator('.rh-title').innerText(),'Tạo phiếu nhập');
        const changed=entries.filter({hasText:'Điều chỉnh hàng nhập'});
        assert.match(await changed.locator('.rh-changes').innerText(),/Số lượng nhập\s+2\s+→\s+5/);
        await changed.locator('.rh-details > summary').click();
        const qtyRow=changed.locator('tbody tr').filter({hasText:'Số lượng nhập'});
        assert.deepEqual(await qtyRow.locator('td').allTextContents(),['2','5']);
        await changed.screenshot({path:path.join(output,'before-after.png')});
        await ui11.review(page,'receipt-history-before-after-expanded',ui11.output('history'),{root:'#purchaseReceiptAuditCard .rh-event:has(.rh-details[open])'}).catch(async error=>{await ui11HistoryFailure(page,error);throw error;}); // UI-11 review hook
        await card.locator('#receiptHistorySearch').fill('cap nhat hang cho duyet');
        assert.equal(await entries.count(),1,'Vietnamese search must work without accents.');
        await entries.first().locator('.rh-details > summary').click();
        assert.match(await entries.first().locator('.rh-evidence-note').innerText(),/chỉ lưu thông tin sau/);
        assert.equal(await entries.first().locator('tbody tr').filter({hasText:'Số lượng nhập'}).locator('td').first().innerText(),'Không được lưu');
        await card.locator('#receiptHistorySearch').fill('');
        await card.locator('#receiptHistoryFilter').selectOption('invoice');
        assert.equal(await entries.count(),12);
        assert.ok((await entries.locator('.rh-badge').allTextContents()).every(x=>x==='Hóa đơn'));
        await card.locator('#receiptHistorySearch').fill('tran dung');
        assert.ok((await entries.locator('.rh-actor').allTextContents()).every(x=>x==='Trần Dũng'));
        await card.locator('#receiptHistorySearch').fill('khong co ket qua 123');
        assert.equal(await entries.count(),0);
        assert.match(await card.locator('#purchaseReceiptAuditStatus').innerText(),/Không tìm thấy/);
        await card.locator('#receiptHistorySearch').fill('');await card.locator('#receiptHistoryFilter').selectOption('');
        while(await card.locator('#receiptHistoryMore').isVisible()) await card.locator('#receiptHistoryMore').click();
        assert.equal(await entries.count(),53);
        assert.ok((await entries.locator('.rh-title').allTextContents()).every(x=>!x.includes('Sự kiện ')&&!x.includes('Thao tác khác')));
        await card.locator('#receiptHistorySort').selectOption('newest');
        await card.evaluate(node=>node.scrollIntoView({block:'start',behavior:'instant'}));
        await page.waitForFunction(()=>{const node=document.getElementById('purchaseReceiptAuditCard'),expected=(parseFloat(getComputedStyle(node).scrollMarginTop)||0)+(parseFloat(getComputedStyle(document.scrollingElement).scrollPaddingTop)||0);if(Math.abs(node.getBoundingClientRect().top-expected)<3)return true;node.scrollIntoView({block:'start',behavior:'instant'});return false;}); // UI-11 history respects header scroll margin
        await page.screenshot({path:path.join(output,'timeline-desktop.png')});
        // Legacy/future evidence must remain readable, safely escaped and recoverable.
        const fixture = {id:999,eventType:999,actorUserName:'<img src=x onerror="window.auditXss=1">',occurredAtUtc:'invalid',
            changedFieldsJson:'[broken',oldValuesJson:'{malformed',newValuesJson:'{"Note":"<script>window.auditXss=2</script>"}',isSuccess:false};
        await page.route(auditUrl, route=>route.fulfill({json:{events:[fixture]}}));
        await card.locator('#receiptHistoryRefresh').click();await card.locator('[data-event-id="999"]').waitFor();
        assert.equal(await entries.count(),1); assert.equal(await entries.locator('img,script').count(),0);
        assert.equal(await page.evaluate(()=>window.auditXss),undefined);
        await entries.locator('.rh-details > summary').click();await entries.locator('.rh-technical > summary').click();
        assert.match(await entries.locator('.rh-technical').innerText(),/\{malformed/);
        assert.equal(await entries.locator('.rh-failed').innerText(),'Không thành công');
        await page.unroute(auditUrl);
        await page.route(auditUrl, route=>route.fulfill({status:500,json:{message:'test failure'}}));
        await card.locator('#receiptHistoryRefresh').click();
        await card.locator('#purchaseReceiptAuditStatus').filter({hasText:'Không thể tải lịch sử'}).waitFor();
        assert.equal(await card.locator('#receiptHistoryRefresh').isEnabled(),true);
        await page.unroute(auditUrl);
        await card.locator('#receiptHistoryRefresh').click();await card.locator('#receiptHistoryStats').filter({hasText:'53 thao tác'}).waitFor();
        await card.locator('#receiptHistorySort').selectOption('newest');
        await page.setViewportSize({width:390,height:844});await card.evaluate(node=>node.scrollIntoView({block:'start',behavior:'instant'}));
        await page.waitForFunction(()=>{const node=document.getElementById('purchaseReceiptAuditCard'),expected=(parseFloat(getComputedStyle(node).scrollMarginTop)||0)+(parseFloat(getComputedStyle(document.scrollingElement).scrollPaddingTop)||0);if(Math.abs(node.getBoundingClientRect().top-expected)<3)return true;node.scrollIntoView({block:'start',behavior:'instant'});return false;}); // UI-11 history respects header scroll margin
        assert.equal(await card.evaluate(node=>node.scrollWidth<=node.clientWidth+1),true,'History must not overflow at phone width.');
        assert.equal(await card.evaluate(node=>node.getBoundingClientRect().right<=window.innerWidth),true);
        await page.screenshot({path:path.join(output,'timeline-mobile.png')});
        const employeeContext=await browser.newContext(),employee=await employeeContext.newPage();
        await ui11.protect(employeeContext,info.baseUrl); // UI-11 review hook
        await login(employee,info.employee);await employee.goto(url);
        assert.equal(await employee.locator('#purchaseReceiptAuditCard').count(),0,'Employee without audit permission must not see history.');
        const auditStatus=await employee.evaluate(async id=>(await fetch(`/admin/api/stock-documents/${id}/audit-events`)).status,info.receiptId);
        assert.equal(auditStatus,403);
        assert.deepEqual(errors,[]);
        console.log('PASS: 53 event labels, UTC time, before/after evidence, missing prior values, search/filter/sort/load-more, safe legacy JSON, retry, mobile layout and audit access control.');
    } catch(error) {await page.screenshot({path:path.join(output,'failure.png')}).catch(()=>{});throw error;}
    finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
