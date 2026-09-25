'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', x => text += x); process.stdin.on('end', () => resolve(text)); }));
    const output = path.resolve('TestResults/receipt-barcode-proposals/browser'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage(), errors = []; page.on('pageerror', e => errors.push(e.message));
    async function login(target, credentials) {
        await target.goto(info.baseUrl + '/admin/account/login');
        await target.locator('[name=UserName]').fill(credentials.user); await target.locator('[name=Password]').fill(credentials.password);
        if (await target.locator('select[name=SelectedTerminalId]').count()) await target.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([target.waitForURL(url => !url.pathname.endsWith('/login')), target.locator('#loginForm button[type=submit]').click()]);
    }
    async function request(target, url, method = 'GET', body) {
        return target.evaluate(async ({ url, method, body }) => {
            const response = await fetch(url, { method, headers: { 'Content-Type': 'application/json',
                RequestVerificationToken: document.querySelector('[name="__RequestVerificationToken"]').value }, body: body === undefined ? undefined : JSON.stringify(body) });
            const text = await response.text(); if (!response.ok) throw new Error(response.status + ': ' + text); return text ? JSON.parse(text) : null;
        }, { url, method, body });
    }
    try {
        await login(page, info.employee);
        await page.goto(info.baseUrl + `/admin/warehouse-receiving/${info.receiptId}`);
        await page.waitForFunction(() => window.ReceiptIntake && document.querySelector('#riBaseUnit').options.length > 1);
        const initialDetail = await request(page, `/admin/api/stock-documents/${info.receiptId}`);
        assert.ok(initialDetail.rowVersion.includes('+'), 'Fixture must exercise HTML-sensitive Base64.');
        assert.equal(await page.evaluate(() => window.warehouseReceivingDetail.rowVersion), initialDetail.rowVersion,
            'Razor must emit the exact SQL rowversion, without HTML entities inside JavaScript.');
        const managerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
        const manager = await managerContext.newPage(); manager.on('pageerror', e => errors.push(e.message));
        await login(manager, info);
        await manager.goto(info.baseUrl + `/admin/stock-documents/${info.receiptId}`);
        assert.equal(await manager.evaluate(() => window.stockDocumentPage.rowVersion), initialDetail.rowVersion,
            'Manager view must also preserve HTML-sensitive Base64.');
        await page.evaluate(() => window.jQuery('#quickLookupInput').select2('open'));
        const scan = page.locator('.select2-container--open .select2-search__field');
        await Promise.all([page.waitForResponse(r => r.url().includes('/barcode-proposals/lookup?') && r.ok()), scan.fill('0001234567890')]);
        await scan.press('Enter');
        await page.locator('#receiptIntakeModal.show').waitFor();
        assert.equal(await page.locator('#riBarcode').inputValue(), '0001234567890');
        await page.waitForFunction(() => document.activeElement?.matches('#receiptIntakeModal .select2-search__field'));
        await page.keyboard.type('Sữa tươi');
        const products = page.locator('#receiptIntakeModal .select2-results__option--selectable');
        await products.filter({ hasText: 'Sữa tươi không đường' }).waitFor();
        assert.equal(await products.count(), 1, 'Each product should appear once, independent of unit count.');
        await products.first().click();
        await page.locator('#riUnitCards button').first().waitFor();
        assert.equal(await page.locator('#riUnitCards button').count(), 4);
        assert.equal(await page.locator('#riSave').isDisabled(), true, 'Explicitly choose the unit for a new barcode.');
        await page.locator('#riUnitCards button:not(.is-add)').filter({ hasText: 'Thùng' }).click();
        assert.match(await page.locator('#riEquivalent').innerText(), /24 Hộp/);
        assert.match(await page.locator('#riUnitCards [aria-pressed=true]').innerText(), /INTERNAL-CARTON/);
        await page.locator('#riUnitCards button').filter({ hasText: 'Lốc' }).click();
        assert.equal(await page.locator('#riUnitCards [aria-pressed=true]').count(), 1);
        assert.match(await page.locator('#riUnitCards [aria-pressed=true]').innerText(), /1 Lốc = 4 Hộp/);
        assert.match(await page.locator('#riEquivalent').innerText(), /4 Hộp/);
        await page.locator('#riNote').fill('Mã trên bao bì lốc 4 hộp');
        await page.screenshot({ path: path.join(output, 'employee-select-pack.png') });
        await page.setViewportSize({ width: 390, height: 844 });
        await page.locator('#riUnitCards').scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(output, 'unit-cards-mobile.png') });
        assert.equal(await page.locator('#receiptIntakeModal .modal-body').evaluate(x => x.scrollWidth <= x.clientWidth + 1), true);
        assert.ok((await page.locator('#riSave').boundingBox()).width >= 140, 'Mobile action remains usable.');
        await page.setViewportSize({ width: 1440, height: 1000 });
        await page.locator('#riSave').click();
        await page.locator('#receiptIntakeModal').waitFor({state:'hidden'});
        await page.waitForFunction(() => !window.ReceiptIntake.isSaving());
        await page.locator('#receiptIntakePending > summary').click();
        await page.locator('#rbpItems').getByText('Mã mới – chờ duyệt', { exact: true }).waitFor();
        // Repeating the scan in the same receipt goes directly to quantity entry.
        await page.evaluate(() => window.jQuery('#quickLookupInput').select2('open'));
        await scan.fill('');
        // Send scanner-like key events: Select2 retains its Enter suppression flag
        // between openings, so filling the reused input alone can skip its query.
        await Promise.all([page.waitForResponse(r => r.url().includes('/barcode-proposals/lookup?') && r.ok()), scan.pressSequentially('0001234567890')]);
        await scan.press('Enter');
        await page.locator('#quickAddProductModal.show').waitFor();
        assert.equal(await page.locator('#popupFactor').innerText(), '4');
        assert.equal(await page.locator('#rbpModal.show').count(), 0);
        await page.locator('#popupQuickQty').fill('2');
        await Promise.all([page.waitForResponse(r => r.url().endsWith(`/stock-documents/${info.receiptId}/lines`) && r.request().method() === 'POST' && r.ok()),
            page.locator('#btnPopupQuickAddLine').click()]);
        await page.waitForFunction(() => document.querySelector('#quickAddProductModal').style.display === 'none');
        await page.setViewportSize({ width: 390, height: 844 });
        await page.locator('#receiptBarcodeProposals').screenshot({ path: path.join(output, 'employee-mobile.png') });
        let detail = await request(page, `/admin/api/stock-documents/${info.receiptId}`);
        const receivedPack = detail.lines.filter(x => x.unitNameSnapshot === 'Lốc' || x.unitName === 'Lốc');
        assert.equal(receivedPack.reduce((sum, line) => sum + line.quantity, 0), 5);
        assert.equal(receivedPack.reduce((sum, line) => sum + line.baseQuantity, 0), 20);
        await page.waitForFunction(version => window.warehouseReceivingDetail.rowVersion === version, detail.rowVersion);
        await page.setViewportSize({ width: 1440, height: 1000 });

        // Add/delete a separate base-unit line using the actual receiving controls.
        await page.evaluate(() => window.jQuery('#quickLookupInput').select2('open'));
        await scan.fill('');
        await Promise.all([page.waitForResponse(r => r.url().includes('/barcode-proposals/lookup?') && r.ok()), scan.pressSequentially('INTERNAL-BOX')]);
        await scan.press('Enter');
        await page.locator('#quickAddProductModal.show').waitFor();
        await page.locator('#popupQuickQty').fill('1');
        await page.locator('#btnPopupQuickAddLine').click();
        await page.waitForFunction(() => document.querySelector('#quickAddProductModal').style.display === 'none');
        detail = await request(page, `/admin/api/stock-documents/${info.receiptId}`);
        const baseLine = detail.lines.find(x => x.isBaseUnit);
        assert.ok(baseLine);
        await page.locator(`.btn-delete-receiving-line[data-line-id="${baseLine.id}"]`).waitFor();
        page.once('dialog', dialog => dialog.accept());
        await page.locator(`.btn-delete-receiving-line[data-line-id="${baseLine.id}"]`).click();
        await page.locator(`.js-receiving-qty[data-line-id="${baseLine.id}"]`).waitFor({ state: 'detached' });
        detail = await request(page, `/admin/api/stock-documents/${info.receiptId}`);
        assert.equal(detail.lines.some(x => x.id === baseLine.id), false);
        assert.equal(await page.evaluate(() => window.warehouseReceivingDetail.rowVersion), detail.rowVersion);

        // Hold a real quantity autosave: opening Submit must wait for its response AND refreshed lines.
        const packLine = detail.lines.find(x => x.unitName === 'Lốc');
        const qtyInput = page.locator(`.js-receiving-qty[data-line-id="${packLine.id}"]`);
        const lineUrl = `**/admin/api/stock-documents/${info.receiptId}/lines/${packLine.id}`;
        let releaseSave, markSaveStarted;
        const saveGate = new Promise(resolve => releaseSave = resolve);
        const saveStarted = new Promise(resolve => markSaveStarted = resolve);
        await page.route(lineUrl, async route => { markSaveStarted(); await saveGate; await route.continue(); }, { times: 1 });
        await qtyInput.fill('6');
        await page.locator('#btnSubmitReceiving').click();
        await saveStarted;
        await page.locator('#submitReceivingModal.show').waitFor();
        assert.equal(await page.locator('#btnConfirmSubmitReceiving').isDisabled(), true);
        assert.match(await page.locator('#submitReceivingMessage').innerText(), /Đang lưu/);
        releaseSave();
        await page.waitForFunction(() => !document.querySelector('#btnConfirmSubmitReceiving').disabled);
        detail = await request(page, `/admin/api/stock-documents/${info.receiptId}`);
        assert.equal(detail.lines.find(x => x.id === packLine.id).quantity, 6);
        assert.equal(await page.evaluate(() => window.warehouseReceivingDetail.rowVersion), detail.rowVersion);
        await page.locator('#submitReceivingModal [data-bs-dismiss=modal]').first().click();
        await page.locator('#submitReceivingModal.show').waitFor({ state: 'hidden' });

        // Failed saves must not submit the old quantity; a successful retry recovers.
        await page.route(lineUrl, route => route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Test: lưu số lượng thất bại.' }) }), { times: 1 });
        page.once('dialog', dialog => dialog.accept());
        await qtyInput.fill('7');
        await page.locator('#btnSubmitReceiving').click();
        await page.waitForFunction(() => document.querySelector('#submitReceivingMessage').textContent.includes('Chưa lưu được'));
        assert.equal(await page.locator('#btnConfirmSubmitReceiving').isDisabled(), true);
        await page.locator('#submitReceivingModal [data-bs-dismiss=modal]').first().click();
        await page.locator('#submitReceivingModal.show').waitFor({ state: 'hidden' });
        await qtyInput.fill('7');
        await page.locator('#btnSubmitReceiving').click();
        await page.waitForFunction(() => !document.querySelector('#btnConfirmSubmitReceiving').disabled);
        detail = await request(page, `/admin/api/stock-documents/${info.receiptId}`);
        assert.equal(detail.lines.find(x => x.id === packLine.id).quantity, 7);

        // A second client's unseen edit must still be rejected by the existing server concurrency check.
        const visibleVersion = await page.evaluate(() => window.warehouseReceivingDetail.rowVersion);
        await request(manager, `/admin/api/stock-documents/${info.receiptId}/lines/${packLine.id}`, 'PUT', {
            unitId: packLine.unitId, quantity: 7, unitCost: 12345, note: 'Concurrent manager edit' });
        const concurrentDetail = await request(manager, `/admin/api/stock-documents/${info.receiptId}`);
        assert.notEqual(concurrentDetail.rowVersion, visibleVersion);
        const submitUrl = `/stock-documents/${info.receiptId}/submit-approval`;
        const [staleResponse] = await Promise.all([page.waitForResponse(r => r.url().endsWith(submitUrl)), page.locator('#btnConfirmSubmitReceiving').click()]);
        assert.equal(staleResponse.ok(), false);
        assert.equal(staleResponse.request().postDataJSON().rowVersion, visibleVersion);
        await page.waitForFunction(() => document.querySelector('#submitReceivingMessage').textContent.includes('người khác cập nhật'));
        await page.screenshot({ path: path.join(output, 'receiving-concurrent-edit.png') });
        await page.locator('#submitReceivingModal [data-bs-dismiss=modal]').first().click();
        await page.locator('#submitReceivingModal.show').waitFor({ state: 'hidden' });

        // A subsequent saved edit refreshes the visible snapshot; submit through the real modal, without reloading.
        await qtyInput.fill('8');
        await page.locator('#btnSubmitReceiving').click();
        await page.waitForFunction(() => !document.querySelector('#btnConfirmSubmitReceiving').disabled);
        detail = await request(page, `/admin/api/stock-documents/${info.receiptId}`);
        assert.equal(detail.lines.find(x => x.id === packLine.id).quantity, 8);
        assert.equal(await page.evaluate(() => window.warehouseReceivingDetail.rowVersion), detail.rowVersion);
        const [submitted] = await Promise.all([page.waitForResponse(r => r.url().endsWith(submitUrl)), page.locator('#btnConfirmSubmitReceiving').click()]);
        assert.equal(submitted.ok(), true, await submitted.text());
        assert.equal(submitted.request().postDataJSON().rowVersion, detail.rowVersion);
        await page.waitForURL(url => url.pathname === '/admin/warehouse-receiving');
        await page.screenshot({ path: path.join(output, 'receiving-submitted.png') });
        await manager.goto(info.baseUrl + `/admin/stock-documents/${info.receiptId}`);
        await manager.locator('#receiptIntakePending:not([hidden])').waitFor();
        await manager.locator('#receiptIntakePending > summary').click();
        await manager.locator('#rbpItems [data-review=approve]').waitFor();
        await manager.locator('#rbpItems input').fill('Đã kiểm tra đúng mã lốc');
        await manager.screenshot({ path: path.join(output, 'manager-review.png'), fullPage: true });
        await manager.locator('#rbpItems [data-review=approve]').click();
        await manager.waitForFunction(() => document.querySelector('#receiptIntakePending').hidden);
        const proposals = await request(manager, `/admin/api/stock-documents/${info.receiptId}/barcode-proposals`);
        assert.equal(proposals.items.length, 1); assert.equal(proposals.items[0].status, 2);
        const lookup = await request(manager, `/admin/api/stock-documents/${info.receiptId}/barcode-proposals/lookup?catalogOnly=true&term=0001234567890`);
        assert.equal(lookup.results[0].productUnitConversionId, info.packId);
        assert.equal(lookup.results[0].factor, 4);
        assert.deepEqual(errors, []);
        console.log('PASS: exact Base64 in both Razor views; add/edit/delete refresh version; submit waits for autosave; failed save blocks; unseen manager edit conflicts; real submit button succeeds without reload.');
        console.log('PASS: employee unknown scan → select existing pack → proposal; repeated scan → quantity; manager approval → global alias.');
    } catch (error) {
        await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {});
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
