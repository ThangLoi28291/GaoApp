'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => resolve(s)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    const output = 'TestResults/receipt-invoice-follow-up'; fs.mkdirSync(output, { recursive: true });
    try {
        await page.route('**/uploads/products/receipt-follow-up.png', route => route.fulfill({ contentType: 'image/svg+xml',
            body: '<svg xmlns="http://www.w3.org/2000/svg" width="160" height="160"><rect width="160" height="160" fill="#cde9d9"/><text x="30" y="85" font-size="25">Sữa tươi</text></svg>' }));
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        const read = path => page.evaluate(async path => { const r = await fetch(path); if (!r.ok) throw new Error(await r.text()); return r.json(); }, path);
        for (const [id, wait] of [[info.receiptId, false], [info.secondId, true]]) {
            await page.goto(info.baseUrl + '/admin/stock-documents/' + id);
            await page.waitForFunction(() => document.getElementById('receiptInvoiceFollowUpBadge').textContent.includes('Chưa phân loại'));
            await page.waitForLoadState('networkidle');
            await page.locator('.sd-commercial-image-button').first().scrollIntoViewIfNeeded();
            await page.waitForFunction(() => document.querySelector('.sd-commercial-image-button img')?.naturalWidth > 0);
            await page.locator('.sd-commercial-image-button').first().hover();
            await page.locator('#commercialImagePreview').waitFor({ state: 'visible' });
            await page.screenshot({ path: `${output}/product-image.png` });
            await page.keyboard.press('Escape');
            await page.locator('#commercialImagePreview').waitFor({ state: 'hidden' });
            for (const price of await page.locator('.commercial-unit-price').all()) await price.fill('10000');
            await page.locator('#btnOpenApproveModal').click();
            await page.locator('#approveModal').waitFor({ state: 'visible' });
            assert.equal(await page.locator('#approvalQueueLabels').isChecked(), false);
            assert.equal(await page.locator('[name=approvalInvoiceExpectation]:checked').count(), 0);
            if (await page.locator('#acceptPriceVariance').isVisible()) await page.locator('#acceptPriceVariance').check();
            await page.locator('#btnApprove').click();
            await page.waitForFunction(() => document.getElementById('approveMessage').textContent.includes('Chọn chờ'));
            await page.locator(`[name=approvalInvoiceExpectation][value=${wait ? 'waiting' : 'notExpected'}]`).check();
            if (wait) await page.locator('#approvalQueueLabels').check();
            assert.equal(await page.locator('#approveMessage').textContent(), '');
            await page.screenshot({ path: `${output}/approval-${wait}.png` });
            const response = page.waitForResponse(r => r.url().endsWith('/approve-commercial') && r.request().method() === 'POST');
            await page.locator('#btnApprove').click();
            const result = await response; assert.equal(result.status(), 200, await result.text());
            await page.waitForURL(url => url.pathname === '/admin/stock-documents');
            const tasks = await read('/admin/label-printing/tasks');
            assert.equal(tasks.filter(x => x.stockDocumentId === id).length, wait ? 1 : 0);
        }
        await page.locator('#sdWaitingInvoiceKpi').click();
        await page.waitForFunction(id => document.querySelectorAll('#sdReceiptTableBody tr[data-id]').length === 1 && document.querySelector('#sdReceiptTableBody tr').dataset.id === String(id), info.secondId);
        await page.screenshot({ path: `${output}/waiting-list.png` });
        await page.locator(`#sdReceiptTableBody tr[data-id="${info.secondId}"] a[href$="?tab=xml"]`).click();
        await page.waitForFunction(() => document.querySelector('.sd-optional-xml')?.open === true);
        await page.locator('#btnEndInvoiceWaiting').waitFor({ state: 'visible' });
        assert.equal(await page.locator('#btnOpenApproveModal').count(), 0);
        await page.locator('#btnEndInvoiceWaiting').click();
        await page.locator('#endInvoiceWaitingReason').fill('Nhà cung cấp xác nhận không gửi hóa đơn');
        await Promise.all([page.waitForNavigation(), page.locator('#btnConfirmEndInvoiceWaiting').click()]);
        await page.waitForFunction(() => document.getElementById('receiptInvoiceFollowUpBadge').textContent === 'Không chờ hóa đơn');
        await page.setViewportSize({ width: 390, height: 844 });
        await page.screenshot({ path: `${output}/completed-mobile.png`, fullPage: true });
        // Present a late invoice needing review. Backend authorization, evidence freshness
        // and no-reposting are exercised against SQL in ReceiptInvoiceFollowUpSqlServerTests.
        let followUp = { state: 'NeedsReview', hasLinkedInvoice: true, isConfirmed: true, rowVersion: 'test-version',
            mapId: 42, evidenceFingerprint: 'evidence', receiptGoodsTotal: 1688899, xmlPaymentAmount: 4580891, unmatchedDetailCount: 16 };
        let reviewRequests = 0;
        const followUpUrl = `**/admin/api/stock-documents/${info.secondId}/invoice-follow-up`;
        await page.route(followUpUrl, route => route.fulfill({ contentType: 'application/json', body: JSON.stringify(followUp) }));
        await page.route(followUpUrl + '/review', async route => {
            const body = route.request().postDataJSON();
            assert.equal(body.mapId, 42); assert.equal(body.evidenceFingerprint, 'evidence');
            assert.equal(body.rowVersion, 'test-version'); assert.equal(body.reason, 'Hóa đơn chung nhiều phiếu, đã kiểm tra');
            reviewRequests++;
            followUp = { ...followUp, state: 'Reviewed', reviewReason: body.reason, reviewedBy: 'Quản lý', reviewedAtUtc: '2026-10-01T08:00:00Z' };
            await route.fulfill({ contentType: 'application/json', body: JSON.stringify(followUp) });
        });
        await page.reload();
        await page.locator('#btnReviewInvoice').click();
        await page.locator('#reviewInvoiceModal.show').waitFor();
        assert.match(await page.locator('#reviewInvoiceSummary').textContent(), /2\.891\.992/);
        await page.locator('#btnConfirmReviewInvoice').click();
        assert.match(await page.locator('#reviewInvoiceMessage').textContent(), /Vui lòng/);
        assert.equal(reviewRequests, 0);
        await page.locator('#reviewInvoiceReason').fill('Hóa đơn chung nhiều phiếu, đã kiểm tra');
        await page.screenshot({ path: `${output}/review-mobile.png` });
        await page.locator('#btnConfirmReviewInvoice').click();
        await page.waitForFunction(() => document.getElementById('receiptInvoiceFollowUpBadge').textContent === 'Đã kiểm tra hóa đơn');
        assert.equal(reviewRequests, 1);
        assert.equal(await page.locator('#btnReviewInvoice').isVisible(), false);
        assert.match(await page.locator('#receiptInvoiceReviewDetails').textContent(), /Quản lý.*Hóa đơn chung/);
        await page.reload();
        await page.locator('#receiptInvoiceReviewDetails').waitFor({ state: 'visible' });
        assert.deepEqual(errors, []);
        console.log('PASS: explicit invoice choice, opt-in queue, immediate approval, wait filter, metadata-only finish and image hover.');
    } catch (error) {
        await page.screenshot({ path: `${output}/failure.png`, fullPage: true });
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
