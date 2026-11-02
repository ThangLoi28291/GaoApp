'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let text = ''; process.stdin.on('data', value => text += value); process.stdin.on('end', () => resolve(text));
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
        await page.goto(info.baseUrl + '/Admin/Invoice');
        await page.locator('[data-bs-target="#invoiceOrderDraftModal"]').click();
        const term = page.locator('#invoiceOrderDraftTerm');
        await term.fill(String(info.orderId));
        const option = page.locator(`#invoiceOrderDraftResults [data-order-id="${info.orderId}"]`);
        await option.waitFor();
        await term.press('ArrowDown');
        assert.equal(await option.evaluate(button => button === document.activeElement), true);
        await option.press('Enter');
        await page.waitForFunction(() => !document.getElementById('invoiceOrderDraftCreate').disabled);
        await page.screenshot({ path: path.join(info.evidenceRoot, 'create-empty-invoice.png') });
        await page.locator('#invoiceOrderDraftCreate').click();
        await page.waitForURL(url => url.pathname.includes('/Invoice/Detail'));
        assert.match(await page.locator('#manualLinesBox').textContent(), /Tổng:\s*0/);
        await page.locator('#btnOpenManualDetailModal').click();
        await page.locator('#manualItemName').fill('Sản phẩm thêm thủ công sau tạo hóa đơn');
        await page.locator('#manualUnitName').fill('Hộp');
        await page.locator('#manualQuantity').fill('2');
        await page.locator('#manualUnitPrice').fill('5000');
        const saved = page.waitForResponse(response => response.url().endsWith('/Invoice/CreateManualDetail') && response.request().method() === 'POST');
        await page.locator('#manualDetailModal button[type="submit"]').click();
        assert.equal((await saved).ok(), true);
        await page.locator('#manualLinesBox').getByText('Sản phẩm thêm thủ công sau tạo hóa đơn', { exact: true }).waitFor();
        await page.screenshot({ path: path.join(info.evidenceRoot, 'manual-line-added.png') });
        await page.setViewportSize({ width: 390, height: 844 });
        await page.goto(info.baseUrl + '/Admin/Invoice');
        await page.locator('[data-bs-target="#invoiceOrderDraftModal"]').click();
        await term.fill(String(info.duplicateOrderId));
        await page.locator(`#invoiceOrderDraftResults [data-order-id="${info.duplicateOrderId}"]`).click();
        await page.waitForFunction(() => document.getElementById('invoiceOrderDraftMessage').textContent.includes('Báo trùng'));
        assert.equal(await page.locator('#invoiceOrderDraftCreate').isDisabled(), true);
        assert.equal(await page.locator('#invoiceOrderDraftSelection a').count(), 1);
        await page.screenshot({ path: path.join(info.evidenceRoot, 'duplicate-invoice-mobile.png') });
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({ passed: true, createdEmpty: true, manualAdded: true, duplicateBlocked: true }));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
