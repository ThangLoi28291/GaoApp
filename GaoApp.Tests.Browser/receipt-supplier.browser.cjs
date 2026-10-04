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
    await context.route('**/uploads/products/receipt-preview.png', route => route.fulfill({
        contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="300" height="300"><rect width="300" height="300" fill="white"/><path d="M100 40h100v220H100z" fill="#d8eddf"/><path d="M100 40l25-20h50l25 20" fill="#4b8661"/><text x="150" y="155" text-anchor="middle" font-size="32" fill="#245339">SỮA</text></svg>'
    }));
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const receiptUrl = info.baseUrl + `/admin/stock-documents/${info.receiptId}`;
    const headerUrl = '**/admin/stock-documents/update-header';
    const supplierName = 'Nhà cung cấp thử lưu XML';
    async function choose(target, name) {
        await target.locator('[data-workbench-tab="settlement"]').click();
        await target.locator('#commercialSupplierId + .select2 .select2-selection').click();
        await target.locator('.select2-container--open .select2-search__field').fill(name);
        await target.locator('.select2-results__option--selectable').filter({ hasText: name }).first().click();
    }
    async function saved(target) {
        await target.waitForFunction(() => document.getElementById('commercialSupplierSaveState')?.textContent === 'Đã lưu nhà cung cấp.');
    }
    async function value(target, id) { return target.locator('#' + id).inputValue(); }
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('[name=SelectedTerminalId]').count())
            await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        await page.goto(receiptUrl);
        await page.waitForFunction(() => window.jQuery && jQuery('#commercialSupplierId').data('select2'));
        assert.equal(await value(page, 'CurrentSupplierId'), '');
        assert.equal(await page.locator('#btnOpenInputInvoicePicker').isDisabled(), true);
        const initialVersion = await page.evaluate(() => window.stockDocumentPage.rowVersion);
        await page.locator('[data-workbench-tab="goods"]').click();
        const vatCell = page.locator('td[data-label="VAT"]').first();
        assert.equal(await vatCell.isVisible(), false);
        const imageButton = page.locator('.sd-commercial-image-button').first();
        await imageButton.scrollIntoViewIfNeeded();
        await page.waitForFunction(() => document.querySelector('.sd-commercial-thumbnail').naturalWidth > 0);
        // Scrolling closes the image preview; let layout/scroll settle before hovering.
        await page.waitForTimeout(250);
        await imageButton.hover();
        await page.locator('#commercialImagePreview').waitFor({ state: 'visible' });
        const visualOutput = path.resolve('TestResults/receipt-supplier'); fs.mkdirSync(visualOutput, { recursive: true });
        await page.screenshot({ path: path.join(visualOutput, 'goods-image-preview.png') });
        await page.keyboard.press('Escape');
        assert.equal(await page.locator('#commercialImagePreview').isVisible(), false);
        await page.setViewportSize({ width: 390, height: 844 });
        assert.equal(await vatCell.isVisible(), false, 'Responsive grid must respect hidden VAT');
        // The preview closes on scroll/resize. Finish the viewport transition before opening it.
        await imageButton.scrollIntoViewIfNeeded();
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        await imageButton.click();
        await page.locator('#commercialImagePreview').waitFor({ state: 'visible' });
        const previewBox = await page.locator('#commercialImagePreview').boundingBox();
        assert.ok(previewBox.x >= 0 && previewBox.x + previewBox.width <= 390);
        await page.keyboard.press('Escape');
        await page.setViewportSize({ width: 1440, height: 1000 });
        const price = page.locator('.commercial-unit-price').first();
        await price.fill('12345');
        await page.locator('[data-workbench-tab="settlement"]').click();
        await page.locator('#commercialHasVat').check();
        await page.locator('[data-workbench-tab="goods"]').click();
        assert.equal(await vatCell.isVisible(), true);
        const tax = page.locator('.commercial-tax').first();
        const taxValue = await tax.locator('option').nth(1).getAttribute('value');
        if (taxValue) await tax.selectOption(taxValue);
        await page.locator('[data-workbench-tab="settlement"]').click();
        await page.locator('#commercialHasVat').uncheck();
        await page.locator('[data-workbench-tab="goods"]').click();
        assert.equal(await vatCell.isVisible(), false);
        await page.locator('[data-workbench-tab="settlement"]').click();
        await page.locator('#commercialHasVat').check();
        if (taxValue) assert.equal(await tax.inputValue(), taxValue, 'VAT selection is retained when toggled back on');

        // A delayed save must block invoice picking until SQL acknowledges the supplier.
        let release;
        const gate = new Promise(resolve => release = resolve);
        await page.route(headerUrl, async route => { await gate; await route.continue(); }, { times: 1 });
        await choose(page, supplierName);
        await page.waitForFunction(() => document.getElementById('commercialSupplierId').disabled);
        assert.equal(await page.locator('#btnOpenInputInvoicePicker').isDisabled(), true);
        release(); await saved(page);
        assert.equal(await value(page, 'CurrentSupplierId'), String(info.supplierId));
        assert.equal(await page.locator('#inputInvoiceSupplierName').textContent(), supplierName);
        assert.equal(await page.locator('#inputInvoiceSupplierTaxCode').textContent(), '0123456789');
        assert.equal(await page.locator('#inputInvoiceSupplierMissing').getAttribute('hidden'), '');
        assert.equal(await page.locator('#btnOpenInputInvoicePicker').isEnabled(), true);
        assert.equal(await price.inputValue(), '12345');
        assert.equal(await page.locator('#commercialHasVat').isChecked(), true);
        assert.notEqual(await page.evaluate(() => window.stockDocumentPage.rowVersion), initialVersion);
        await page.locator('[data-workbench-tab="xml"]').click();
        await page.locator('.sd-optional-xml > summary').click();
        const candidates = page.waitForResponse(response => response.url().includes('/input-invoices/picker/candidates'));
        await page.locator('#btnOpenInputInvoicePicker').click();
        const libraryResponse = await candidates;
        assert.equal(libraryResponse.status(), 200, 'Picker must read the newly persisted supplier');
        const libraryData = await libraryResponse.json();
        assert.equal(libraryData.context.supplierId, info.supplierId);
        assert.equal(libraryData.candidates[0].invoiceNumber, '1001');
        assert.equal(libraryData.candidates[0].hasXml, true);
        await page.locator('#inputInvoicePickerModal.show').waitFor();
        await page.locator('#inputInvoicePickerSupplierContext').filter({ hasText: supplierName }).waitFor();
        await page.locator('.input-invoice-candidate').first().click();
        await page.locator('#inputInvoicePickerPreview').getByText('Hàng kiểm thử XML', { exact: false }).waitFor();
        const originalViewport = page.viewportSize();
        const output = path.resolve('TestResults/receipt-supplier'); fs.mkdirSync(output, { recursive: true });
        const assertPickerActions = async () => {
            await page.waitForTimeout(350);
            await page.screenshot({ path: path.join(output, 'invoice-picker-layout.png') });
            assert.ok(await page.locator('#btnSelectInputInvoice').evaluate(el => {
                const r = el.getBoundingClientRect();
                return r.top >= 0 && r.bottom <= innerHeight && r.left >= 0 && r.right <= innerWidth &&
                    (el.disabled ? el.parentElement : el).contains(document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2));
            }), 'Invoice selection stays visible and clickable without browser zoom');
        };
        for (const viewport of [{ width: 1366, height: 650 }, { width: 1024, height: 600 }, { width: 800, height: 600 }, { width: 390, height: 700 }]) {
            await page.setViewportSize(viewport); await assertPickerActions();
        }
        // PDF content may scroll independently; its intrinsic size must never displace the footer.
        await page.setViewportSize({ width: 1024, height: 600 });
        const previewHtml = await page.locator('#inputInvoicePickerPreview').innerHTML();
        await page.locator('#inputInvoicePickerPreview').evaluate(el => {
            el.innerHTML = '<iframe title="PDF layout fixture" srcdoc="<body style=\'height:1800px\'>Bản xem trước hóa đơn dài</body>"></iframe>';
        });
        await assertPickerActions();
        await page.screenshot({ path: path.join(output, 'invoice-picker-small-screen.png') });
        await page.locator('#inputInvoiceRelinkReasonBox').evaluate(el => el.classList.remove('d-none'));
        await assertPickerActions();
        await page.locator('#inputInvoiceRelinkReasonBox').evaluate(el => el.classList.add('d-none'));
        await page.locator('#inputInvoicePickerPreview').evaluate((el, html) => { el.innerHTML = html; }, previewHtml);
        await page.setViewportSize(originalViewport);
        // A slower previous filter must not replace the latest supplier's candidate list.
        let releaseOldFilter;
        const oldFilterGate = new Promise(resolve => releaseOldFilter = resolve);
        await page.route('**/input-invoices/picker/candidates?**', async route => {
            await oldFilterGate;
            await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ message: 'STALE FILTER ERROR' }) }).catch(() => {});
        }, { times: 1 });
        await page.locator('#inputInvoicePickerSearch').fill('delayed');
        await Promise.all([page.waitForRequest(r => r.url().includes('search=delayed')),
            page.locator('#inputInvoicePickerFilters button[type=submit]').click()]);
        await page.locator('#inputInvoicePickerSearch').fill('1001');
        await page.locator('#inputInvoicePickerFilters button[type=submit]').click();
        await page.locator('.input-invoice-candidate').filter({ hasText: '1001' }).waitFor();
        releaseOldFilter();
        await page.waitForTimeout(150);
        assert.equal(await page.locator('#inputInvoicePickerCandidates').getByText('STALE FILTER ERROR').count(), 0);
        await page.locator('#inputInvoicePickerModal [data-bs-dismiss="modal"]').first().click();
        await page.locator('#inputInvoicePickerModal').waitFor({ state: 'hidden' });
        await page.reload();
        assert.equal(await value(page, 'commercialSupplierId'), String(info.supplierId));
        assert.equal(await page.locator('#inputInvoiceSupplierName').textContent(), supplierName);

        // Missing tax code is distinct from missing supplier.
        await choose(page, 'Nhà cung cấp thiếu MST'); await saved(page);
        assert.equal(await value(page, 'CurrentSupplierId'), String(info.noTaxSupplierId));
        assert.equal(await page.locator('#inputInvoiceSupplierMissing').getAttribute('hidden'), '');
        assert.equal(await page.locator('#inputInvoiceSupplierTaxMissing').getAttribute('hidden'), null);
        assert.equal(await page.locator('#btnOpenInputInvoicePicker').isDisabled(), true);

        // A failed save restores the persisted selection and does not pretend success.
        await page.route(headerUrl, route => route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ success: false, message: 'Lỗi lưu thử nghiệm' }) }), { times: 1 });
        await choose(page, supplierName);
        await page.waitForFunction(() => document.getElementById('commercialSupplierSaveState').textContent === 'Lỗi lưu thử nghiệm');
        assert.equal(await value(page, 'commercialSupplierId'), String(info.noTaxSupplierId));
        assert.equal(await page.locator('#btnOpenInputInvoicePicker').isDisabled(), true);
        await choose(page, supplierName); await saved(page);

        // A stale tab cannot overwrite the supplier saved from another tab.
        const stale = await context.newPage(); await stale.goto(receiptUrl);
        await choose(page, 'Nhà cung cấp thiếu MST'); await saved(page);
        await choose(stale, 'Nhà cung cấp thiếu MST');
        await stale.waitForFunction(() => document.getElementById('commercialSupplierSaveState').textContent.includes('người khác cập nhật'));
        assert.equal(await value(stale, 'commercialSupplierId'), String(info.supplierId));
        await stale.close();
        await choose(page, supplierName); await saved(page);
        await page.locator('[data-workbench-tab="xml"]').click();
        if (await page.locator('.sd-optional-xml').getAttribute('open') === null)
            await page.locator('.sd-optional-xml > summary').click();
        await page.locator('#inputInvoicePickerSection').screenshot({ path: path.join(output, 'supplier-saved-xml.png') });
        assert.deepEqual(errors, []);
        console.log('PASS: supplier persists before approval, XML picker works, prices/VAT survive, missing tax, failed save and stale tab handled.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
