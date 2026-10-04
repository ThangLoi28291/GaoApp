'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let data = ''; process.stdin.on('data', chunk => data += chunk); process.stdin.on('end', () => resolve(data));
    }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage();
    const errors = [], dialogs = [], deletes = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('dialog', async dialog => { dialogs.push(dialog.message()); await dialog.dismiss(); });
    page.on('request', request => { if (request.method() === 'DELETE') deletes.push(request.url()); });
    const output = process.env.GAO_RECEIPT_REMOVAL_OUTPUT || path.resolve('TestResults/receipt-line-removal');
    fs.mkdirSync(output, { recursive: true });
    const modal = page.locator('#deleteReceivingLineModal');
    const confirm = page.locator('#btnConfirmDeleteReceivingLine');
    const row = id => page.locator(`#wrdLinesContainer tr[data-line-id="${id}"]`);
    const deleteUrl = id => `**/admin/api/stock-documents/${info.receiptId}/lines/${id}`;
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        await page.goto(info.baseUrl + `/admin/warehouse-receiving/${info.receiptId}`);
        await page.waitForFunction(() => window.ReceiptIntake && !window.ReceiptIntake.isSaving());
        await row(info.knownLineId).locator('.btn-delete-receiving-line').click(); await modal.waitFor({state:'visible'});
        assert.match(await page.locator('#deleteReceivingLineName').textContent(), /Sữa/);
        assert.match(await page.locator('#deleteReceivingLineQuantity').textContent(), /4 Lốc/);
        assert.equal(await modal.locator('.wrd-delete-product').evaluate(element=>getComputedStyle(element).display),'flex');
        await modal.locator('.modal-content').screenshot({path:path.join(output,'delete-desktop.png'),animations:'disabled'});
        await modal.getByRole('button',{name:'Giữ lại',exact:true}).click(); await modal.waitFor({state:'hidden'});
        assert.equal(deletes.length,0); assert.equal(await row(info.knownLineId).count(),1);

        await row(info.knownLineId).locator('.btn-delete-receiving-line').click(); await modal.waitFor({state:'visible'});
        await page.route(deleteUrl(info.knownLineId), route => route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({message:'Không thể xóa thử nghiệm'})}),{times:1});
        await confirm.click(); await page.waitForFunction(() => document.getElementById('deleteReceivingLineError').textContent === 'Không thể xóa thử nghiệm');
        assert.equal(await row(info.knownLineId).count(),1); assert.equal(await confirm.isEnabled(),true);
        let release; const gate = new Promise(resolve => release = resolve);
        await page.route(deleteUrl(info.knownLineId),async route => {await gate;await route.continue();},{times:1});
        await confirm.click();
        await page.waitForFunction(() => document.getElementById('btnConfirmDeleteReceivingLine').disabled);
        await page.keyboard.press('Escape'); assert.equal(await modal.isVisible(),true);
        assert.equal(await modal.getByRole('button',{name:'Giữ lại',exact:true}).isDisabled(),true);
        release(); await modal.waitFor({state:'hidden'});
        await row(info.knownLineId).waitFor({state:'detached'});
        assert.equal(await page.locator('#wrdTotalLines').textContent(),'1');
        assert.equal(deletes.length,2,'One failed attempt, one actual deletion');

        // Use the actual mobile row editor to reach the same confirmation.
        await page.setViewportSize({width:390,height:844});
        await page.locator('[data-wra-tab="items"]').click();
        await row(info.resolvedLineId).locator('[data-wra-edit]').click();
        await page.locator('#wraQuantityModal').waitFor({state:'visible'});
        await page.locator('#wraRemove').click(); await modal.waitFor({state:'visible'});
        assert.match(await page.locator('#deleteReceivingLineName').textContent(), /Bánh tráng/);
        const box = await modal.locator('.modal-content').boundingBox();
        assert.ok(box.x>=0 && box.x+box.width<=390);
        await page.screenshot({path:path.join(output,'delete-mobile.png'),animations:'disabled'});
        // A failed refresh after deletion must retry GET only, not submit another DELETE.
        await page.route(`**/admin/warehouse-receiving/${info.receiptId}/lines`,route=>route.fulfill({status:503,body:'Unavailable'}),{times:1});
        await confirm.click();
        await page.waitForFunction(() => document.getElementById('deleteReceivingLineAction').textContent === 'Tải lại danh sách');
        assert.match(await page.locator('#deleteReceivingLineError').textContent(), /Đã xóa dòng hàng/);
        assert.equal(deletes.length,3);
        await page.locator('#btnCancelDeleteReceivingLine').click(); await modal.waitFor({state:'hidden'});
        await page.locator('#wraRetry').click(); await modal.waitFor({state:'visible'});
        assert.equal(await page.locator('#deleteReceivingLineAction').textContent(),'Tải lại danh sách');
        await confirm.click(); await modal.waitFor({state:'hidden'});
        await row(info.resolvedLineId).waitFor({state:'detached'});
        assert.equal(deletes.length,3,'Refreshing after confirmed deletion does not delete twice');
        assert.equal((await page.locator('#wrdTotalLines').textContent()).trim(),'0');
        await page.reload(); assert.equal(await page.locator('#wrdLinesContainer tr[data-line-id]').count(),0);
        assert.deepEqual(dialogs,[]); assert.deepEqual(errors,[]);
        console.log('PASS: desktop/mobile confirmation, cancel, inline errors, pending lock, known/resolved removal, preserved history and GET-only refresh retry; no native dialogs.');
    } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
