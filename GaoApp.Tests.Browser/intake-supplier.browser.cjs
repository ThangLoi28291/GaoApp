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
    const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
    const page = await context.newPage();
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    const receiptUrl = info.baseUrl + `/admin/stock-documents/${info.receiptId}`;
    const headerUrl = '**/admin/stock-documents/update-header';
    const output = process.env.GAO_INTAKE_SUPPLIER_OUTPUT || path.resolve('TestResults/intake-supplier');
    fs.mkdirSync(output, { recursive: true });
    let reviews = 0, headers = 0;
    const reviewVersions = [], savedVersions = [];
    page.on('request', request => {
        if (request.method() === 'POST' && request.url().includes('/intake/') && request.url().endsWith('/review')) {
            reviews++; reviewVersions.push(request.postDataJSON().documentRowVersion);
        }
        if (request.url().endsWith('/update-header')) headers++;
    });
    page.on('response', async response => {
        if (response.url().endsWith('/update-header') && response.ok()) savedVersions.push((await response.json()).rowVersion);
    });
    async function open(target, itemId) {
        const pending = target.locator('#receiptIntakePending');
        if (await pending.getAttribute('open') === null) await pending.locator('summary').click();
        await target.locator(`[data-ri-review="${itemId}"]`).click();
        await target.locator('#receiptIntakeReviewModal').waitFor({ state: 'visible' });
        await target.locator('#riReviewSupplierWrap').waitFor({ state: 'visible' });
    }
    async function choose(target) {
        await target.locator('#riReviewSupplier + .select2 .select2-selection').click();
        await target.locator('.select2-container--open .select2-search__field').fill(info.supplierName);
        await target.locator('.select2-results__option--selectable').filter({ hasText: info.supplierName }).click();
    }
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('[name=SelectedTerminalId]').count())
            await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        await page.goto(receiptUrl);
        await page.waitForFunction(() => window.jQuery && jQuery('#riReviewSupplier').data('select2'));
        const initialVersion = await page.evaluate(() => window.stockDocumentRowVersion.current());
        await page.locator('[data-workbench-tab="goods"]').click();
        await page.locator('.commercial-unit-price').first().fill('12345');
        // Keep a genuinely stale page to exercise the real header concurrency guard later.
        const stale = await context.newPage(); await stale.goto(receiptUrl);
        await open(page, info.itemIds[0]);
        await page.locator('#riApprove').click();
        await page.waitForFunction(() => document.getElementById('riReviewError').textContent.includes('Vui lòng chọn nhà cung cấp'));
        assert.equal(reviews, 0); assert.equal(headers, 0);
        await choose(page);
        assert.equal(await page.locator('#riReviewError').textContent(), '', 'Clear the missing-supplier message after selection');
        await page.locator('#receiptIntakeReviewModal .modal-content').screenshot({ path: path.join(output, 'supplier-in-review.png'), animations: 'disabled' });

        await page.route(headerUrl, route => route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ success: false, message: 'Lỗi lưu thử nghiệm' }) }), { times: 1 });
        await page.locator('#riApprove').click();
        await page.waitForFunction(() => document.getElementById('riReviewError').textContent === 'Lỗi lưu thử nghiệm');
        assert.equal(reviews, 0, 'Failed header save must never approve the product');
        assert.equal(await page.locator('#CurrentSupplierId').inputValue(), '');
        assert.equal(await page.locator('#riApprove').isEnabled(), true);

        let release; const gate = new Promise(resolve => release = resolve);
        await page.route(headerUrl, async route => { await gate; await route.continue(); }, { times: 1 });
        await page.locator('#riApprove').click();
        await page.waitForFunction(() => document.getElementById('commercialSupplierId').disabled);
        assert.equal(reviews, 0, 'Await header persistence before approving');
        assert.equal(await page.locator('#riApprove').isDisabled(), true);
        assert.equal(await page.locator('#riReviewSupplier').isDisabled(), true);
        release();
        await page.locator('#receiptIntakeReviewModal').waitFor({ state: 'hidden' });
        await page.waitForFunction(id => !document.querySelector(`[data-ri-review="${id}"]`), info.itemIds[0]);
        assert.equal(reviews, 1);
        assert.equal(await page.locator('#CurrentSupplierId').inputValue(), String(info.supplierId));
        assert.notEqual(reviewVersions[0], initialVersion);
        assert.equal(reviewVersions[0], savedVersions[0]);
        assert.equal(await page.locator('.commercial-unit-price').first().inputValue(), '12345', 'Preserve unsaved purchase price');

        let staleReviews = 0;
        stale.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/review')) staleReviews++; });
        await open(stale, info.itemIds[1]); await choose(stale);
        await stale.locator('#riApprove').click();
        await stale.waitForFunction(() => document.getElementById('riReviewError').textContent.includes('người khác cập nhật'));
        assert.equal(staleReviews, 0, 'Stale header must stop approval');
        await stale.close();

        const headersBefore = headers;
        await open(page, info.itemIds[1]);
        assert.equal(await page.locator('#riReviewSupplier').inputValue(), String(info.supplierId));
        await page.locator('#riApprove').click();
        await page.locator('#receiptIntakeReviewModal').waitFor({ state: 'hidden' });
        await page.waitForFunction(() => !window.ReceiptIntake.isSaving());
        assert.equal(reviews, 2); assert.equal(headers, headersBefore, 'Reuse the saved supplier without an extra header write');
        assert.equal(await page.locator('.commercial-unit-price').first().inputValue(), '12345');
        await page.screenshot({ path: path.join(output, 'products-reviewed.png'), animations: 'disabled' });
        assert.deepEqual(errors, []);
        console.log('PASS: inline supplier search; missing, failed, delayed and stale saves block review; fresh version; saved supplier reused; unsaved prices preserved.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
