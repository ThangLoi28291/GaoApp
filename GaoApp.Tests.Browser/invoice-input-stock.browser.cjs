'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', x => text += x); process.stdin.on('end', () => resolve(text)); }));
    const output = path.resolve('TestResults/invoice-input-stock/browser'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
    const errors = [], requests = []; page.on('pageerror', x => errors.push(x.message));
    let fixtures = false, fail = false;
    const common = { warehouseId: 1, warehouseName: 'Kho chính', legalEntityName: 'Hộ kinh doanh Gạo Mart', baseUnit: 'hộp' };
    const balances = ['Sữa tươi TH có đường hộp 110ml', 'Sữa tươi TH chocolate hộp 180ml', 'Bánh tai heo sấy giòn Bình Minh', 'Bánh tam giác Mayeef'].map((name, i) => ({ ...common, productVariantId: i + 1, productName: name, code: '20001055154' + (38 + i), received: 120, issued: 24 + i, held: i === 1 ? 12 : 0, remaining: 96 - i, available: 96 - i - (i === 1 ? 12 : 0) }));
    const movements = ['increase', 'decrease', 'hold'].map((kind, i) => ({ ...common, key: 'row-' + i, productVariantId: 1, productName: balances[0].productName, code: balances[0].code, dateUtc: '2026-09-10T05:15:00Z', kind, change: i === 0 ? 120 : i === 1 ? -24 : 0, held: i === 2 ? 12 : 0, before: i === 0 ? 0 : 120, after: i === 0 ? 120 : i === 1 ? 96 : 120, sourceCode: i === 0 ? 'NK-20260910-0001' : 'HĐ 00001238', xmlNumber: i === 0 ? '00003561' : null, stockDocumentId: i === 0 ? 1 : null, invoiceHeadId: i ? 2 : null, note: i === 0 ? 'XML 30 lốc × 4 hộp. Ghi nhận 120 hộp.' : 'Chứng từ đã được đối chiếu.' }));
    await page.route('**/admin/invoice-input-stock/data?**', async route => {
        requests.push(route.request().url());
        if (!fixtures) return route.continue();
        if (fail) return route.fulfill({ status: 503, body: '' });
        const q = new URL(route.request().url()).searchParams;
        const rows = q.get('view') === 'ledger' ? movements.filter(x => !q.get('kind') || x.kind === q.get('kind')) : balances;
        return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ page: 1, pageSize: 20, totalPages: 1, totalItems: rows.length, productCount: 4, increaseCount: 1, decreaseCount: 1, holdCount: 1, negativeCount: 0, balances: rows, movements: rows, warehouses: [{ id: 1, name: 'Kho chính' }] }) });
    });
    const loaded = () => page.waitForFunction(() => document.querySelector('#xsResults')?.getAttribute('aria-busy') === 'false');
    const shot = name => page.screenshot({ path: path.join(output, name + '.png') });
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('select[name=SelectedTerminalId]').count()) await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
        await page.goto(info.baseUrl + '/admin/invoice-input-stock'); await loaded();
        assert.match(await page.locator('#xsBody').innerText(), /Chưa có dữ liệu/);
        assert.equal(await page.locator('#xsProducts').innerText(), '0');
        console.log('PASS real authenticated Razor and SQL projection API (empty fixture).');
        fixtures = true; await page.locator('#xmlReload').click(); await loaded();
        for (const [width, height] of [[1440, 1000], [1366, 768], [1024, 900], [768, 1024], [390, 844], [360, 800]]) {
            await page.setViewportSize({ width, height });
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'No page overflow at ' + width);
            await shot('balances-' + width);
        }
        await page.setViewportSize({ width: 1440, height: 1000 });
        await page.locator('#xsBody tr[data-index]').first().dblclick();
        assert.ok(await page.locator('#xsDialog').evaluate(x => x.open));
        await shot('quick-view'); await page.keyboard.press('Escape');
        await page.locator('[data-view=ledger]').click(); await loaded();
        assert.equal(await page.locator('#xsBody tr[data-index]').count(), 3);
        assert.match(await page.locator('#xsBody').innerText(), /Nhập từ XML/); await shot('ledger-1440');
        await page.locator('[data-preview="0"]').click();
        assert.equal(await page.locator('#xsDialogActions a').getAttribute('href'), '/admin/stock-documents/1');
        await page.locator('#xsDialogActions button').click(); await loaded();
        assert.match(requests.at(-1), /productVariantId=1/);
        await page.locator('#xsFrom').fill('2026-09-11'); await page.locator('#xsTo').fill('2026-09-10');
        const count = requests.length; await page.locator('#xsFilters button[type=submit]').click();
        assert.ok(await page.locator('#xsDateError').isVisible()); assert.equal(requests.length, count);
        await page.locator('#xsReset').click(); await loaded();
        await page.locator('[data-metric=hold]').click(); await loaded(); assert.equal(await page.locator('#xsBody tr[data-index]').count(), 1);
        await page.setViewportSize({ width: 390, height: 844 }); await shot('ledger-390');
        fail = true; await page.locator('#xmlReload').click(); await loaded();
        assert.equal(await page.locator('#xsProducts').innerText(), '—'); assert.ok(await page.locator('[data-retry]').isVisible());
        fail = false; await page.locator('[data-retry]').click(); await loaded();
        await page.locator('[data-preview="0"]').click(); await shot('quick-view-390');
        await page.keyboard.press('Escape');
        assert.deepEqual(errors, []); console.log('PASS six responsive widths, quick view, source link, product history, date validation, holds and error/retry.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
