'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', value => text += value); process.stdin.on('end', () => resolve(text)); }));
    const output = path.resolve('TestResults/label-barcodes'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1050 } });
    const page = await context.newPage(); const errors = []; page.on('pageerror', e => errors.push(e.message));
    await page.route('**/uploads/test-label-product.svg', route => route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80"><rect width="80" height="80" fill="#eef6f1"/><text x="12" y="45" fill="#236b48">MILK</text></svg>' }));
    const request = (suffix, method = 'GET', body) => page.evaluate(async ({ suffix, method, body }) => {
        const response = await fetch('/admin/label-printing/' + suffix, { method, headers: { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('[name="__RequestVerificationToken"]').value }, body: body === undefined ? undefined : JSON.stringify(body) });
        const text = await response.text(); if (!response.ok) throw new Error(response.status + ': ' + text); return text ? JSON.parse(text) : null;
    }, { suffix, method, body });
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('select[name=SelectedTerminalId]').count()) await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
        await page.goto(info.baseUrl + '/admin/label-printing');
        const printer = await request('printers', 'POST', { name: 'Máy tem nhỏ · Thu ngân', windowsPrinterName: 'FAKE TRANSPORT ONLY', dpi: 203, printableWidthMm: 108, enabled: true });
        const template = await request('templates', 'POST', { design: { name: 'Tem nhỏ 35 × 22', printerId: printer.id, widthMm: 35, heightMm: 22, columns: 2, quantityMode: 'one' } });
        const added = await request('receipts/' + info.receiptId, 'POST');
        await page.goto(info.baseUrl + '/admin/label-printing?task=' + added.id);
        await page.locator('#taskLines tr').first().waitFor();
        const original = await request('tasks/' + added.id);
        const button = page.locator('#taskPrintEditor [data-print-template]').first();
        await page.locator('[data-select=all]').click();
        await page.locator('#taskLines [data-quantity]').first().fill('7');
        await button.click();
        await page.getByRole('dialog', { name: 'Chuẩn bị mã vạch phù hợp để in tem' }).waitFor();
        assert.equal(await page.locator('.label-barcode-issues li').count(), 2);
        assert.match(await page.locator('.label-barcode-notice').textContent(), /Tạo 1 mã nội bộ mới/);
        assert.match(await page.locator('.label-barcode-notice').textContent(), /Dùng lại 1 mã đã có/);
        await page.screenshot({ path: path.join(output, 'barcode-confirmation.png'), fullPage: false });
        await page.keyboard.press('Escape');
        assert.deepEqual((await request('tasks/' + added.id)).lines, original.lines);
        assert.equal((await request('jobs')).length, 0);
        // Switch to quick printing to exercise the exact-unit path for the first product.
        const first = original.lines[0].product;
        await page.goto(info.baseUrl + '/admin/label-printing?variantId=' + first.variantId);
        await page.locator('#quickLines [data-quick-line]').first().waitFor();
        await page.locator('#quickLines input[type=number]').fill('4');
        await page.locator('#quickPrintChoices [data-print-template]').first().click();
        await page.getByRole('dialog', { name: 'Chuẩn bị mã vạch phù hợp để in tem' }).waitFor();
        await page.locator('.label-confirm-popup button[type=submit]').click();
        await page.locator('.label-confirm-printer').waitFor();
        assert.match(await page.locator('#quickLines').textContent(), /8938505974194/);
        assert.equal(await page.locator('#quickLines input[type=number]').inputValue(), '4');
        await page.keyboard.press('Escape'); // Saving the alias does not send a print job.
        assert.equal((await request('jobs')).length, 0);
        // Explicitly refresh this receipt after the separate quick/catalog change.
        const changed = await request('tasks/' + added.id);
        assert.equal(changed.sourceChanged, true);
        await request('tasks/' + added.id + '/refresh', 'POST', { rowVersion: changed.rowVersion });
        await page.goto(info.baseUrl + '/admin/label-printing?task=' + added.id);
        await page.locator('#taskLines tr').first().waitFor();
        await page.locator('[data-select=all]').click();
        await page.locator('#taskLines [data-quantity]').first().fill('7');
        await button.click();
        await page.getByRole('dialog', { name: 'Chuẩn bị mã vạch phù hợp để in tem' }).waitFor();
        assert.equal(await page.locator('.label-barcode-issues li').count(), 1);
        await page.locator('.label-confirm-popup button[type=submit]').click();
        await page.locator('.label-confirm-printer').waitFor();
        const prepared = await request('tasks/' + added.id);
        assert.equal(prepared.sourceChanged, false); assert.deepEqual(prepared.progress, original.progress);
        assert.equal(await page.locator('#taskLines [data-selected]:checked').count(), 2);
        assert.equal(await page.locator('#taskLines [data-quantity]').first().inputValue(), '7');
        assert.equal((await request('jobs')).length, 0);
        await page.keyboard.press('Escape');
        await button.click(); // Already repaired: directly confirm printing, no barcode prompt again.
        await page.locator('.label-confirm-printer').waitFor();
        assert.equal(await page.locator('.label-barcode-issues').count(), 0);
        await page.locator('.label-confirm-popup button[type=submit]').click();
        await page.waitForFunction(() => document.querySelector('#taskTotals').textContent.includes('2 / 2'));
        const done = await request('tasks/' + added.id);
        assert.equal(done.progress.handled, 2);
        assert.equal(done.jobs[0].quantity, 8);
        for (const old of ['0984712540111', '0984712539663']) assert.equal((await request('products?q=' + old)).length, 1);
        assert.deepEqual(errors, []);
        console.log('PASS: barcode prompt cancel, reuse/create, quick exact-unit update, unchanged quantities/progress, cancel print after saving, no duplicate alias prompt, successful print with old aliases retained.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exit(1); });
