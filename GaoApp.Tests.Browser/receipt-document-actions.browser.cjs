'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
(async()=>{
    const info=JSON.parse(await new Promise(resolve=>{let data='';process.stdin.on('data',chunk=>data+=chunk);process.stdin.on('end',()=>resolve(data));}));
    const browser=await chromium.launch({channel:'chrome',headless:true});
    const errors=[], output=process.env.GAO_RECEIPT_ACTIONS_OUTPUT;
    if(output)fs.mkdirSync(output,{recursive:true});
    try{
        async function login(user,password){
            const context=await browser.newContext({viewport:{width:1440,height:1050}}), page=await context.newPage();
            page.setDefaultTimeout(15000);page.on('pageerror',error=>errors.push(error.message));
            await page.goto(info.baseUrl+'/admin/account/login');
            await page.locator('[name=UserName]').fill(user);await page.locator('[name=Password]').fill(password);
            if(await page.locator('[name=SelectedTerminalId]').count())await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
            await Promise.all([page.waitForURL(url=>!url.pathname.endsWith('/login')),page.locator('button[type=submit]').click()]);
            return page;
        }
        const staff=await login(info.employee,info.employeePassword), manager=await login(info.user,info.password);
        const staffUrl=info.baseUrl+'/admin/warehouse-receiving/'+info.receiptId;
        const managerUrl=info.baseUrl+'/admin/stock-documents/'+info.receiptId;
        async function openTitle(page){
            await page.locator(`[data-receipt-title="${info.receiptId}"]`).first().click();
            await page.waitForFunction(()=>document.getElementById('receiptDocumentMessage').textContent==='');
            await page.locator('#receiptNewTitle').waitFor();
        }
        async function saveTitle(page,title){
            await openTitle(page);await page.locator('#receiptNewTitle').fill(title);await page.locator('#receiptTitleSave').click();
            await page.locator('#receiptDocumentModal').waitFor({state:'hidden'});
        }
        await staff.goto(info.baseUrl+'/admin/warehouse-receiving');
        await staff.locator(`[data-receipt-title="${info.receiptId}"]`).waitFor();
        await staff.setViewportSize({width:390,height:844});
        assert.ok(await staff.locator('.wr-card-actions').first().evaluate(node=>node.clientWidth>280),'Mobile receipt actions span the card');
        if(output)await staff.screenshot({path:path.join(output,'staff-draft-list-mobile.png'),fullPage:true});
        await openTitle(staff);await staff.locator('#receiptNewTitle').fill('  Phiếu nhập buổi sáng  ');
        await Promise.all([staff.waitForEvent('domcontentloaded'),staff.locator('#receiptTitleSave').click()]);
        await staff.locator('.wr-title').filter({hasText:'Phiếu nhập buổi sáng'}).waitFor();
        // Delete has a concrete confirmation; cancel must preserve the receipt.
        await staff.locator(`[data-receipt-delete="${info.deleteId}"]`).click();
        await staff.locator('#receiptDeleteWarning').waitFor();
        await staff.locator('#receiptDocumentModal [data-bs-dismiss="modal"]').last().click();
        assert.equal(await staff.locator(`[data-receipt-delete="${info.deleteId}"]`).count(),1);
        await staff.locator(`[data-receipt-delete="${info.deleteId}"]`).click();
        await staff.waitForFunction(()=>!document.getElementById('receiptTitleSave').disabled);
        await Promise.all([staff.waitForEvent('domcontentloaded'),staff.locator('#receiptTitleSave').click()]);
        await staff.locator(`[data-receipt-title="${info.receiptId}"]`).waitFor();
        assert.equal(await staff.locator(`[data-receipt-delete="${info.deleteId}"]`).count(),0);

        await staff.setViewportSize({width:1440,height:1050});await staff.goto(staffUrl);
        await staff.locator('#btnSubmitReceiving').click();
        await Promise.all([staff.waitForEvent('domcontentloaded'),staff.locator('#btnConfirmSubmitReceiving').click()]);
        // The existing submit flow returns to the receiving list; reopen the submitted receipt.
        await staff.goto(staffUrl);
        await staff.locator(`[data-receipt-title="${info.receiptId}"]`).filter({hasText:'Yêu cầu đổi tên'}).waitFor();
        assert.equal(await staff.locator('[data-receipt-delete]').count(),0);
        await saveTitle(staff,'Tên đề nghị sau khi gửi duyệt');
        await staff.locator('#receiptTitleProposalNotice').filter({hasText:'Tên đề nghị sau khi gửi duyệt'}).waitFor();
        assert.match(await staff.locator('.wrd-document-title').textContent(),/Phiếu nhập buổi sáng/);

        await manager.goto(managerUrl);
        await manager.locator('#receiptTitleProposalNotice').filter({hasText:'Tên đề nghị sau khi gửi duyệt'}).waitFor();
        assert.match(await manager.locator('[data-retail-unit]').first().textContent(),/48[.,]000 đ\/Lốc/);
        assert.match(await manager.locator('[data-retail-unit]').nth(1).textContent(),/280[.,]000 đ\/Thùng/);
        await manager.locator('.commercial-unit-price').first().fill('12345');
        assert.ok(await manager.locator('.commercial-unit-price').first().evaluate(node=>node.clientWidth>=110),'Retail column leaves price inputs readable');
        if(output)await manager.locator('#commercialApprovalWorkbench').screenshot({path:path.join(output,'retail-comparison.png')});
        await openTitle(manager);await manager.locator('#receiptTitleApprove').waitFor();
        if(output)await manager.screenshot({path:path.join(output,'manager-title-approval.png')});
        await manager.locator('#receiptTitleApprove').click();await manager.locator('#receiptDocumentModal').waitFor({state:'hidden'});
        assert.equal(await manager.locator('[data-receipt-document-title]').textContent(),'Tên đề nghị sau khi gửi duyệt');
        assert.equal(await manager.locator('.commercial-unit-price').first().inputValue(),'12345','Title decisions preserve unsaved commercial inputs');
        await staff.reload();await saveTitle(staff,'Tên đề nghị bị từ chối');
        await openTitle(manager);await manager.locator('#receiptTitleDecline').click();await manager.locator('#receiptDocumentModal').waitFor({state:'hidden'});
        assert.equal(await manager.locator('[data-receipt-document-title]').textContent(),'Tên đề nghị sau khi gửi duyệt');
        // A competing request invalidates an already-open manager dialog.
        await openTitle(manager);await manager.locator('#receiptNewTitle').fill('Tên từ cửa sổ cũ');
        await staff.reload();await saveTitle(staff,'Đề nghị mới trong lúc quản lý đang mở');
        await manager.locator('#receiptTitleSave').click();await manager.locator('#receiptDocumentMessage').filter({hasText:'Phiếu đã thay đổi'}).waitFor();
        await manager.locator('#receiptDocumentModal [data-bs-dismiss="modal"]').last().click();
        await saveTitle(manager,'Tên quản lý quyết định');
        assert.equal(await manager.locator('[data-receipt-document-title]').textContent(),'Tên quản lý quyết định');
        assert.equal(await manager.locator('#receiptTitleProposalNotice').isHidden(),true);
        await staff.reload();assert.equal(await staff.locator('.wrd-document-title').textContent().then(t=>t.trim()),'Tên quản lý quyết định');
        // Saving catalog prices refreshes the comparison column without replacing unsaved receipt inputs.
        const firstRetail=manager.locator('[data-retail-unit]').first(), unitId=await firstRetail.getAttribute('data-retail-unit');
        await manager.locator('.js-receipt-selling-prices').first().click();
        await manager.locator('#rspContent').waitFor({state:'visible'});
        for(const checkbox of await manager.locator('#rspUnits [data-field="selected"]').all())await checkbox.uncheck();
        const pricedUnit=manager.locator(`#rspUnits [data-unit-id="${unitId}"]`);
        await pricedUnit.locator('[data-field="price"]').fill('60000');
        await pricedUnit.locator('[data-field="selected"]').check();
        await manager.locator('#rspSave').click();
        await manager.waitForFunction(()=>document.getElementById('rspMessage').textContent.startsWith('Đã cập nhật 1'));
        assert.match(await firstRetail.textContent(),/60[.,]000 đ\/Lốc/);
        assert.match(await manager.locator('[data-retail-unit]').nth(1).textContent(),/280[.,]000 đ\/Thùng/);
        assert.equal(await manager.locator('.commercial-unit-price').first().inputValue(),'12345');
        await manager.goto(info.baseUrl+'/admin/stock-documents');
        await manager.locator('.sd-index-title').filter({hasText:'Tên quản lý quyết định'}).waitFor();
        await manager.setViewportSize({width:390,height:844});
        if(output)await manager.screenshot({path:path.join(output,'manager-list-mobile.png'),fullPage:true});
        assert.deepEqual(errors,[]);
        console.log('PASS browser: draft rename/delete confirmation, employee request, manager approve/reject/direct rename, stale dialog, actual retail columns, mobile lists, unsaved price preservation; no page errors.');
    }catch(error){
        for(const [index,context] of browser.contexts().entries()){
            const page=context.pages()[0];
            if(output && page)await page.screenshot({path:path.join(output,`failure-${index}.png`),fullPage:true});
            if(page)console.error('UI diagnostic',index,await page.evaluate(()=>({url:location.pathname,
                actions:document.getElementById('receiptDocumentActions')?.outerHTML,
                message:document.getElementById('receiptDocumentMessage')?.textContent,
                heading:document.querySelector('h1')?.textContent})),errors);
        }
        throw error;
    }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
