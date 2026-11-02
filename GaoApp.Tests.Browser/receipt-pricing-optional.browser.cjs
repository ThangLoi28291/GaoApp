'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let input = ''; process.stdin.on('data', value => input += value); process.stdin.on('end', () => resolve(input));
    }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        const errors = []; page.on('pageerror', error => errors.push(error.message));
        fs.mkdirSync(info.evidenceRoot, { recursive: true });
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name="UserName"]').fill(info.user);
        await page.locator('[name="Password"]').fill(info.password);
        await page.locator('[name="SelectedTerminalId"]').selectOption(String(info.terminalId));
        await page.locator('button[type="submit"]').click();
        await page.goto(info.baseUrl + `/admin/stock-documents/${info.receiptId}`);
        await page.waitForFunction(() => document.getElementById('pricingBillConversion')?.textContent.includes('Chỉ tìm trong'));
        const pack = info.lines.find(x => x.factor === 4), normalTotal = info.lines.reduce((sum, x) => sum + x.quantity * x.factor * 10, 0);
        const goodsPrice = page.locator(`.commercial-line[data-line-id="${pack.id}"] .commercial-unit-price`);
        assert.equal(await goodsPrice.evaluate(input => input.readOnly), false);
        await page.locator('[data-workbench-tab="pricing"]').click();
        for (const line of info.lines) {
            await page.locator('#pricingBillSearch').click();
            await page.locator('#pricingBillSuggestions button').first().click();
            await page.locator('#pricingBillUnit').selectOption(String(line.unitId));
            await page.locator('#pricingBillQuantity').fill(String(line.quantity - (line.id === pack.id ? 1 : 0)));
            await page.locator('#pricingBillPrice').fill(String(line.factor * 10));
            await page.locator('#pricingBillAdd').click();
        }
        await page.locator('.sd-pricing-steps [data-pricing-step="program"]').click();
        assert.equal(await page.locator('#pricingRuleList .sd-pricing-rule').count(), 0);
        assert.match(await page.locator('#pricingRuleList').textContent(), /tính giá theo bill như thông thường/);
        await page.locator('.sd-pricing-steps [data-pricing-step="price"]').click();
        await page.waitForFunction(() => !document.getElementById('pricingPreview').disabled);
        await page.locator('#pricingActualBillTotal').fill(String(normalTotal - pack.factor * 10));
        const mismatchedResponse = page.waitForResponse(response => response.url().endsWith('/pricing-allocation/preview'));
        await page.locator('#pricingPreview').click();
        const mismatched = await (await mismatchedResponse).json();
        assert.equal(mismatched.canApply, false);
        assert.ok(mismatched.errors.some(error => error.includes('thực nhận nhiều hơn bill')));
        assert.equal(await page.locator('#pricingManualHelp').isVisible(), true);
        assert.equal(await page.locator('#pricingApply').isDisabled(), true);
        assert.equal(await page.locator('#pricingSystemTotal').textContent(), '—', 'A quantity error must not present an uncalculated zero total.');
        assert.equal(await page.locator('#pricingDifference').textContent(), '—', 'An uncalculated bill must not appear balanced.');
        await page.locator('#receiptPricingAllocation').screenshot({ path: path.join(info.evidenceRoot, 'quantity-mismatch-optional-pricing.png') });

        async function saveManualGoodsPrice() {
            await page.locator('#pricingManualGoods').click();
            assert.equal(await goodsPrice.isVisible(), true);
            assert.equal(await goodsPrice.isEnabled(), true);
            assert.equal(await goodsPrice.evaluate(input => input.readOnly), false);
            const saved = page.waitForResponse(response => response.url().endsWith('/price-draft') && response.request().method() === 'POST');
            await goodsPrice.fill('55');
            assert.equal((await saved).status(), 200);
            await page.waitForFunction(() => !window.GaoReceiptPriceDrafts.hasUnsaved());
            assert.equal(await page.evaluate(() => window.GaoReceiptPricingAllocation.blocksConfirm()), false);
            await page.locator('#commercialApproveButton').click();
            await page.locator('#approveModal').waitFor({ state: 'visible' });
            assert.equal(await page.locator('#btnApprove').isEnabled(), true);
            await page.locator('#approveModal [data-bs-dismiss="modal"]').first().click();
            await page.locator('#approveModal').waitFor({ state: 'hidden' });
        }
        await saveManualGoodsPrice(); // An incomplete bill must not require finishing the helper to enter Goods prices.
        await page.locator('[data-workbench-tab="pricing"]').click();
        await page.locator('.sd-pricing-steps [data-pricing-step="bill"]').click();
        const packBill = page.locator('#pricingAllocationLines .sd-bill-row').filter({ hasText: `× ${new Intl.NumberFormat('vi-VN').format(pack.factor * 10)} đ` });
        await packBill.locator('[data-bill-action="Sửa"]').click();
        await page.locator('#pricingBillQuantity').fill(String(pack.quantity));
        await page.locator('#pricingBillAdd').click();
        await page.locator('.sd-pricing-steps [data-pricing-step="price"]').click();
        await page.waitForFunction(() => !document.getElementById('pricingPreview').disabled);
        await page.locator('#pricingActualBillTotal').fill(String(normalTotal));
        const normalResponse = page.waitForResponse(response => response.url().endsWith('/pricing-allocation/preview') &&
            Number(response.request().postDataJSON().actualBillTotal) === normalTotal);
        await page.locator('#pricingPreview').click();
        const normal = await (await normalResponse).json();
        assert.equal(normal.canApply, true, JSON.stringify(normal));
        assert.equal(normal.systemTotal, normalTotal);
        assert.equal(normal.rules.length, 0);
        assert.equal(await page.locator('#pricingFinalTitle').textContent(), 'Giá nhập tính theo bill');
        for (const line of info.lines) assert.equal(normal.lines.find(x => x.stockDocumentLineId === line.id).effectiveUnitPriceBeforeVat, line.factor * 10);
        const save = page.waitForResponse(response => response.url().endsWith('/pricing-allocation') && response.request().method() === 'PUT');
        await page.locator('#pricingSave').click(); assert.equal((await save).status(), 200);
        await page.waitForFunction(() => !document.getElementById('pricingApply').disabled);
        const apply = page.waitForResponse(response => response.url().endsWith('/pricing-allocation/apply'));
        await page.locator('#pricingApply').click(); assert.equal((await apply).status(), 200);
        await page.waitForFunction(() => !document.getElementById('pricingApply').disabled);
        assert.equal(await goodsPrice.inputValue(), String(pack.factor * 10));
        await page.locator('#receiptPricingAllocation').screenshot({ path: path.join(info.evidenceRoot, 'ordinary-price-without-program.png') });
        await saveManualGoodsPrice(); // Apply fills Goods; it does not lock them against later manual corrections.
        assert.equal(await goodsPrice.inputValue(), '55');
        assert.deepEqual(errors, []);
        console.log('PASS: no-program bill pricing; quantity mismatch blocks Apply only; manual Goods prices autosave before/after Apply and the approval modal remains available.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
