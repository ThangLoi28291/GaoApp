'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', x => text += x); process.stdin.on('end', () => resolve(text)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage(), errors = [];
    page.on('pageerror', e => errors.push(e.message));
    const url = `${info.baseUrl}/admin/stock-documents/${info.receiptId}`, endpoint = '**/price-draft';
    const prices = () => page.locator('.commercial-unit-price');
    async function complete(target = page) { await target.waitForFunction(() => !window.GaoReceiptPriceDrafts.hasUnsaved()); }
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        await page.goto(url); await prices().first().waitFor(); assert.equal(await prices().count(), 10);
        for (let i = 0; i < 9; i++) await prices().nth(i).fill(String(10001 + i));
        // Last edited row remains focused: the debounce itself must persist it.
        await complete();
        await page.goto(info.baseUrl + '/admin/stock-documents'); await page.goto(url);
        for (let i = 0; i < 9; i++) assert.equal(await prices().nth(i).inputValue(), String(10001 + i));
        assert.equal(await prices().nth(9).inputValue(), '10');

        // Typing a second value while the first save is in flight must persist the latest value.
        let release, arrived;
        const gate = new Promise(r => release = r), started = new Promise(r => arrived = r);
        await page.route(endpoint, async route => { arrived(); await gate; await route.continue(); }, { times: 1 });
        await prices().first().fill('21000'); await started;
        await prices().first().fill('22000'); release(); await complete();
        await page.reload(); assert.equal(await prices().first().inputValue(), '22000');

        // A network failure keeps edits and warns before leaving; manual retry persists them.
        await page.route(endpoint, route => route.abort('failed'));
        await prices().first().fill('23000');
        await page.waitForFunction(() => document.getElementById('receiptPriceDraftStatus').classList.contains('text-danger'));
        assert.equal(await prices().first().inputValue(), '23000');
        assert.equal(await page.evaluate(() => { const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented; }), true);
        const blocked = await page.evaluate(async () => { try { await window.GaoReceiptPriceDrafts.hold(); return false; } catch { return true; } });
        assert.equal(blocked, true, 'Approval hold must reject a failed draft save');
        await page.unroute(endpoint); await page.locator('#receiptPriceDraftSave').click(); await complete();
        await page.reload(); assert.equal(await prices().first().inputValue(), '23000');

        // A second page reads server drafts and cannot overwrite a more recent first-page edit.
        const other = await context.newPage(); await other.goto(url); await other.locator('.commercial-unit-price').first().waitFor();
        assert.equal(await other.locator('.commercial-unit-price').first().inputValue(), '23000');
        await prices().first().fill('24000'); await complete();
        await other.locator('.commercial-unit-price').first().fill('25000');
        await other.waitForFunction(() => document.getElementById('receiptPriceDraftStatus').classList.contains('text-danger'));
        assert.equal(await other.locator('.commercial-unit-price').first().inputValue(), '25000');
        await page.reload(); assert.equal(await prices().first().inputValue(), '24000');
        await other.close({ runBeforeUnload: false });
        await prices().nth(1).fill('26000'); await complete();
        fs.mkdirSync('TestResults/receipt-price-drafts', { recursive: true });
        await page.locator('#receiptPriceDraftBar').scrollIntoViewIfNeeded();
        await page.screenshot({ path: 'TestResults/receipt-price-drafts/saved.png' });
        assert.deepEqual(errors, []);
        console.log('PASS: 9/10 prices survive navigation, latest edit wins, failed saves remain visible, retry works, stale tabs cannot overwrite, approval waits for drafts.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
