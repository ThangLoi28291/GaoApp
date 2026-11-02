'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let input = ''; process.stdin.on('data', value => input += value); process.stdin.on('end', () => resolve(input));
    }));
    const browser = await chromium.launch({ channel:'chrome', headless:true });
    try {
        fs.mkdirSync(info.evidenceRoot, {recursive:true});
        const page = await browser.newPage({viewport:{width:1440,height:1000}});
        const errors = []; page.on('pageerror', error => errors.push(error.message));
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name="UserName"]').fill(info.user);
        await page.locator('[name="Password"]').fill(info.password);
        await page.locator('[name="SelectedTerminalId"]').selectOption(String(info.terminalId));
        await page.locator('#loginForm button[type="submit"]').click();
        await page.waitForURL(url => !url.pathname.toLowerCase().includes('/account/login'));
        await page.goto(info.baseUrl + `/admin/stock-documents/${info.receiptId}`);
        await page.locator('[data-workbench-tab="settlement"]').click();
        const field = page.locator('#commercialSupplierId');
        assert.equal(await field.isDisabled(), true);
        const original = await field.inputValue();
        let writes = 0; page.on('request', request => { if(request.url().endsWith('/correct-supplier') && request.method()==='POST') writes++; });
        const button = page.locator('#receiptReviewOtherActions [data-receipt-supplier]');
        async function openCorrection() {
            const more = page.locator('.sd-review-more');
            if (!await more.evaluate(element => element.open)) await more.locator('summary').click();
            await button.waitFor({state:'visible'});
            await button.click();
        }
        await openCorrection();
        const modal = page.locator('#receiptSupplierModal');
        await page.waitForFunction(() => !document.getElementById('receiptSupplierSave').disabled);
        assert.match(await page.locator('#receiptSupplierCurrent').textContent(), /Nhà cung cấp đúng/);
        await modal.getByRole('button', {name:'Hủy', exact:true}).click();
        await modal.waitFor({state:'hidden'});
        assert.equal(writes,0); assert.equal(await field.inputValue(),original);
        await openCorrection();
        await page.waitForFunction(() => !document.getElementById('receiptSupplierSave').disabled);
        if (await page.locator('#receiptSupplierSearch').isVisible()) {
            await page.locator('#receiptSupplierSearch').fill('sửa trên giao diện');
            await page.locator(`#receiptSupplierNewId option[value="${info.supplierId}"]`).waitFor({state:'attached'});
            await page.locator('#receiptSupplierNewId').selectOption(String(info.supplierId));
        } else {
            await modal.locator('.select2-selection').click();
            await page.locator('.select2-search__field').fill('sửa trên giao diện');
            await page.getByRole('option').filter({hasText:'Nhà cung cấp sửa trên giao diện'}).click();
        }
        await page.locator('#receiptSupplierSave').click();
        assert.equal(writes,0); assert.match(await page.locator('#receiptSupplierMessage').textContent(),/Nhập lý do/);
        await page.locator('#receiptSupplierReason').fill('Chọn nhầm NCC, sửa theo xác nhận của nhà cung cấp');
        await modal.screenshot({path:path.join(info.evidenceRoot,'supplier-correction-modal.png')});
        const response = page.waitForResponse(response => response.url().endsWith('/correct-supplier') && response.request().method()==='POST');
        await page.locator('#receiptSupplierSave').click();
        assert.equal((await response).status(),200);
        await page.waitForFunction(supplierId => document.getElementById('commercialSupplierId')?.value===String(supplierId) &&
            !document.getElementById('receiptSupplierModal')?.classList.contains('show'),info.supplierId);
        assert.equal(writes,1); assert.equal(await field.isDisabled(),true); assert.deepEqual(errors,[]);
        await page.setViewportSize({width:390,height:844});
        await page.locator('[data-workbench-tab="settlement"]').click(); await openCorrection();
        await page.waitForFunction(() => !document.getElementById('receiptSupplierSave').disabled);
        await modal.screenshot({path:path.join(info.evidenceRoot,'supplier-correction-mobile.png')});
        await page.keyboard.press('Escape'); await modal.waitFor({state:'hidden'});
        console.log('PASS: approved supplier correction, cancel, reason, one POST, reload and mobile modal.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode=1; });
