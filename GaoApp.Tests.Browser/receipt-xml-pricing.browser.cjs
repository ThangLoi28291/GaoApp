'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const ui11 = require('./receipt-mobile-review.cjs'); // UI-11 review hook
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let input = ''; process.stdin.on('data', x => input += x); process.stdin.on('end', () => resolve(input)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    let page;
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 1050 } });
        await ui11.protect(context, info.baseUrl); // UI-11 review hook
        page = await context.newPage();
        const errors = []; page.on('pageerror', error => errors.push(error.message));
        const mappingRequests = [];
        page.on('request', request => {
            if (request.url().endsWith(`/stock-documents/${info.receiptId}/pricing/xml-mappings`) && request.method() === 'POST')
                mappingRequests.push(request.postDataJSON());
        });
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name="UserName"]').fill(info.user); await page.locator('[name="Password"]').fill(info.password);
        await page.locator('[name="SelectedTerminalId"]').selectOption(String(info.terminalId));
        await page.locator('button[type="submit"]').click();
        await page.goto(info.baseUrl + `/admin/stock-documents/${info.receiptId}`);
        await page.waitForFunction(() => document.getElementById('pricingBillConversion')?.textContent.includes('Chỉ tìm trong'));
        await page.locator('[data-workbench-tab="pricing"]').click();
        const bills = page.locator('#pricingAllocationLines .sd-bill-row');
        assert.equal(await bills.count(), 0, 'Linking XML must not automatically insert bill rows.');
        const initialGoods = await page.locator('.commercial-unit-price').evaluateAll(rows => rows.map(row => row.value));
        await page.locator('#pricingBillSearch').click();
        await page.locator('#pricingBillSuggestions button').first().click();
        const baseUnit = await page.locator('#pricingBillUnit').evaluate(select => [...select.options].find(option => /×1$/.test(option.textContent)).value);
        await page.locator('#pricingBillUnit').selectOption(baseUnit);
        await page.locator('#pricingBillQuantity').fill('1'); await page.locator('#pricingBillPrice').fill('777');
        await page.locator('#pricingBillAdd').click();
        const manualKey = await bills.first().getAttribute('data-bill-line-key');
        // Existing manual selections/programs must survive XML import and draft restoration.
        await page.locator('.sd-pricing-steps [data-pricing-step="program"]').click();
        await page.locator('#pricingNewProgram').click(); await page.locator('#pricingRuleType').selectOption('2');
        await page.locator('#pricingRuleSearch').click();
        await page.locator(`#pricingRuleSuggestions [data-program-bill-key="${manualKey}"]`).click();
        await page.locator('#pricingSaveRule').click();
        assert.equal(await page.locator('#pricingRuleList .sd-pricing-rule').count(), 1);
        await page.locator('.sd-pricing-steps [data-pricing-step="bill"]').click();
        await page.locator('#pricingXmlImport').click();
        await page.waitForFunction(() => document.querySelectorAll('#pricingXmlRows [data-xml-detail-id]').length === 5);
        const xmlRow = index => page.locator(`#pricingXmlRows [data-xml-detail-id="${info.detailIds[index]}"]`);
        assert.equal(await page.locator('#pricingXmlPanel').isVisible(), true);
        await ui11.review(page, 'xml-source-mapping', ui11.output('xml-pricing')); // UI-11 review hook
        assert.equal(await xmlRow(0).locator('[data-xml-product-search]').inputValue(), '');
        assert.equal(await xmlRow(1).locator('[data-xml-product-search]').inputValue(), 'Sữa tươi không đường');
        assert.equal(await xmlRow(2).locator('[data-xml-product-search]').inputValue(), 'Sữa tươi không đường');
        assert.equal(await xmlRow(3).locator('[data-xml-kind]').inputValue(), '', 'Zero-value XML rows require an explicit line type.');
        assert.equal(await xmlRow(4).locator('[data-xml-add]').isDisabled(), true, 'Negative/discount XML evidence must not be imported as merchandise.');

        await xmlRow(0).locator('[data-xml-product-search]').fill('sua tuoi');
        await xmlRow(0).locator(`[data-xml-product-option="${info.variantId}"]`).click();
        await xmlRow(0).locator('[data-xml-unit]').selectOption(String(info.cartonUnitId));
        const mappingResponse = page.waitForResponse(response => response.url().endsWith(`/stock-documents/${info.receiptId}/pricing/xml-mappings`) && response.request().method() === 'POST');
        await xmlRow(0).locator('[data-xml-remember]').click();
        const mapping = await mappingResponse; assert.equal(mapping.status(), 200);
        assert.ok(mapping.request().headers().requestverificationtoken, 'Mapping is protected by antiforgery.');
        assert.equal(mapping.request().postDataJSON().inputInvoiceHeadId, info.headId);
        await page.waitForFunction(() => document.getElementById('pricingXmlMessage')?.textContent.includes('Đã nhớ ghép'));
        assert.equal(await bills.count(), 1, 'Remembering product/unit alone must not add quantities to the current bill.');
        assert.equal(await xmlRow(0).locator('[data-xml-remember]').isDisabled(), true);
        assert.equal(await xmlRow(0).locator('[data-xml-add]').isEnabled(), true);
        assert.equal(mappingRequests.length, 1);
        await xmlRow(0).locator('[data-xml-add]').click();
        await page.waitForFunction(() => document.querySelectorAll('#pricingAllocationLines .sd-bill-row').length === 2);
        assert.equal(mappingRequests.length, 1, 'Adding a row must not POST catalog memory.');
        assert.equal(await bills.first().getAttribute('data-bill-line-key'), manualKey);
        const xmlKey = await bills.nth(1).getAttribute('data-bill-line-key');
        assert.equal(xmlKey, `xml-${info.headId}-${info.detailIds[0]}`);
        assert.match(await xmlRow(0).textContent(), /VAT 8%/);
        assert.match(await xmlRow(0).textContent(), /108\.000 đ/);
        assert.match(await bills.nth(1).textContent(), /108\.000 đ/, 'Import the XML line amount plus its own 8% VAT.');

        // An unremembered zero-value row can be added after the user explicitly
        // chooses merchandise/unit and Gift. Memory remains an independent action.
        await xmlRow(3).locator('[data-xml-product-search]').fill('sua tuoi');
        await xmlRow(3).locator(`[data-xml-product-option="${info.variantId}"]`).click();
        await xmlRow(3).locator('[data-xml-unit]').selectOption(baseUnit);
        assert.equal(await xmlRow(3).locator('[data-xml-remember]').isEnabled(), true, 'Memory must not require choosing Buy/Gift.');
        assert.equal(await xmlRow(3).locator('[data-xml-add]').isDisabled(), true);
        await xmlRow(3).locator('[data-xml-kind]').selectOption('gift');
        await xmlRow(3).locator('[data-xml-add]').click();
        await page.waitForFunction(() => document.querySelectorAll('#pricingAllocationLines .sd-bill-row').length === 3);
        assert.equal(mappingRequests.length, 1, 'Add-only must not silently remember a new product/unit match.');
        assert.equal(await xmlRow(3).locator('[data-xml-remember]').isEnabled(), true, 'Memory can be saved independently after adding.');
        assert.equal(await xmlRow(3).locator('[data-xml-add]').isDisabled(), true);
        assert.match(await bills.nth(2).textContent(), /Hàng tặng/);
        // The existing manual row already accounts for this quantity. Remove the
        // deliberate extra row before testing the original complete calculation.
        await bills.nth(2).locator('[data-bill-action="Xóa"]').click();
        assert.equal(await bills.count(), 2);
        await page.locator('#pricingXmlImport').click();
        await page.waitForFunction(() => !document.getElementById('pricingXmlImport').disabled);
        assert.equal(await xmlRow(3).locator('[data-xml-add]').isEnabled(), true, 'Explicit deletion permits re-adding the source row.');
        await page.locator('#pricingXmlAddAll').click();
        await page.waitForFunction(() => document.querySelectorAll('#pricingAllocationLines .sd-bill-row').length === 4);
        assert.equal(await bills.count(), 4, 'Repeated SKU XML rows remain separate.');
        assert.match(await page.locator('#pricingBillTotal').textContent(), /189\.777 đ/);
        assert.equal(await page.locator('#pricingRuleList .sd-pricing-rule').count(), 1);
        assert.deepEqual(await page.locator('.commercial-unit-price').evaluateAll(rows => rows.map(row => row.value)), initialGoods);
        assert.equal(await page.locator('#pricingApply').isDisabled(), true);
        await page.locator('#pricingXmlImport').click();
        await page.waitForFunction(() => !document.getElementById('pricingXmlImport').disabled);
        assert.equal(await xmlRow(0).locator('[data-xml-add]').isDisabled(), true);
        assert.equal(await page.locator('#pricingXmlAddAll').isDisabled(), true);
        assert.equal(await bills.count(), 4, 'Reopening XML must not duplicate existing imported rows.');

        fs.mkdirSync(info.evidenceRoot, { recursive: true });
        await page.locator('#pricingXmlPanel').screenshot({ path: path.join(info.evidenceRoot, 'xml-pricing-import-desktop.png') });
        await page.setViewportSize({ width: 390, height: 844 });
        await page.locator('#pricingXmlPanel').screenshot({ path: path.join(info.evidenceRoot, 'xml-pricing-import-mobile.png') });
        assert.ok(await page.locator('#pricingXmlAddAll').isVisible());
        await page.setViewportSize({ width: 1440, height: 1050 });
        await page.reload();
        await page.waitForFunction(() => document.getElementById('pricingAllocationMessage')?.textContent.includes('Đã khôi phục'));
        await page.locator('[data-workbench-tab="pricing"]').click();
        assert.equal(await bills.count(), 4); assert.equal(await bills.first().getAttribute('data-bill-line-key'), manualKey);
        assert.equal(await page.locator('#pricingRuleList .sd-pricing-rule').count(), 1);
        assert.equal(await bills.nth(1).getAttribute('data-bill-line-key'), xmlKey);
        await bills.nth(1).locator('[data-bill-action="Sửa"]').click();
        assert.equal(await page.locator('#pricingBillPriceMode').inputValue(), 'amount');
        assert.equal(await page.locator('#pricingBillAmount').inputValue(), '108000');
        assert.equal(await page.locator('#pricingBillPrice').inputValue(), '36000');
        await page.locator('#pricingBillCancelEdit').click();
        await page.locator('#pricingXmlImport').click();
        await page.waitForFunction(() => document.querySelectorAll('#pricingXmlRows [data-xml-detail-id]').length === 5);
        assert.equal(await xmlRow(0).locator('[data-xml-product-search]').inputValue(), 'Sữa tươi không đường');
        assert.equal(await xmlRow(0).locator('[data-xml-unit]').inputValue(), String(info.cartonUnitId));
        assert.equal(await page.locator('#pricingXmlAddAll').isDisabled(), true);
        assert.equal(await page.locator('#pricingQuantityOverview').getAttribute('data-state'), 'matched');
        assert.deepEqual(await page.locator('.commercial-unit-price').evaluateAll(rows => rows.map(row => row.value)), initialGoods);

        // Saving a plan remains separate from explicitly applying its computed prices.
        await page.locator('.sd-pricing-steps [data-pricing-step="price"]').click();
        // Entering the price step starts an automatic preview with the previous
        // bill total. Wait for it, then bind this assertion to the updated input.
        await page.waitForFunction(() => !document.getElementById('pricingPreview').disabled);
        const previewResponse = page.waitForResponse(response => response.url().endsWith('/pricing-allocation/preview') &&
            response.request().method() === 'POST' && Number(response.request().postDataJSON().actualBillTotal) === 189699.30);
        await page.locator('#pricingActualBillTotal').fill('189699.30');
        await page.locator('#pricingPreview').click();
        const preview = await previewResponse; assert.equal(preview.status(), 200);
        const previewBody = await preview.json(); assert.equal(previewBody.systemTotal, 189699.30);
        assert.deepEqual(previewBody.errors, [], JSON.stringify(previewBody));
        assert.equal(previewBody.actualBillTotal, 189699.30); assert.equal(previewBody.canApply, true);
        await page.waitForFunction(() => !document.getElementById('pricingSave').disabled);
        const saveResponse = page.waitForResponse(response => response.url().endsWith('/pricing-allocation') && response.request().method() === 'PUT');
        await page.locator('#pricingSave').click(); assert.equal((await saveResponse).status(), 200);
        await page.waitForFunction(() => !document.getElementById('pricingApply').disabled);
        assert.deepEqual(await page.locator('.commercial-unit-price').evaluateAll(rows => rows.map(row => row.value)), initialGoods);

        // A saved plan retains XML row identity after the browser draft is cleared.
        await page.reload();
        await page.waitForFunction(() => document.querySelectorAll('#pricingAllocationLines .sd-bill-row').length === 4);
        await page.locator('[data-workbench-tab="pricing"]').click();
        await page.locator('#pricingXmlImport').click();
        await page.waitForFunction(() => !document.getElementById('pricingXmlImport').disabled);
        assert.equal(await xmlRow(0).locator('[data-xml-add]').isDisabled(), true);
        assert.equal(await page.locator('#pricingXmlAddAll').isDisabled(), true);
        assert.equal(await bills.count(), 4, 'Saved plan reload must not make XML sources addable again.');
        assert.equal(mappingRequests.length, 1);
        await page.locator('.sd-pricing-steps [data-pricing-step="price"]').click();
        await page.waitForFunction(() => !document.getElementById('pricingApply').disabled);
        const applyResponse = page.waitForResponse(response => response.url().endsWith('/pricing-allocation/apply') && response.request().method() === 'POST');
        await page.locator('#pricingApply').click(); assert.equal((await applyResponse).status(), 200);
        await page.waitForFunction(() => document.querySelector('#commercialApprovalWorkbench')?.dataset.allocationApplied === 'true');
        await page.locator('[data-workbench-tab="goods"]').click();
        assert.notDeepEqual(await page.locator('.commercial-unit-price').evaluateAll(rows => rows.map(row => row.value)), initialGoods);
        assert.equal(await page.locator('.commercial-unit-price').first().isEnabled(), true);
        assert.equal(await page.locator('.commercial-unit-price').first().evaluate(input => input.readOnly), false);
        await page.locator('.commercial-unit-price').first().fill('123.45');
        assert.equal(await page.locator('.commercial-unit-price').first().inputValue(), '123.45');
        await page.locator('#commercialApprovalWorkbench').screenshot({ path: path.join(info.evidenceRoot, 'xml-pricing-applied-editable.png') });
        assert.deepEqual(errors, []);
        console.log('PASS: separate Remember/Add, memory without quantity import, add without memory, explicit gift type, canonical unit alias memory, exact XML amount, repeated SKU lines, dedupe after local/server reload, manual/program preservation, Save/Apply separation, editable final goods prices.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
