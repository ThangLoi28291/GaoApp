const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => resolve(s)); }));
    fs.mkdirSync('TestResults/receipt-entry', { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
        const errors = []; page.on('pageerror', e => errors.push(e.message));
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
        await page.goto(info.baseUrl + '/admin/stock-documents');
        const row = page.locator(`tr.sd-index-row[data-id="${info.receiptId}"]`);
        await row.waitFor();
        assert.match(await row.locator('.sd-index-entry').textContent(), /E2E test user/);
        assert.ok((await row.locator('.sd-index-entry').textContent()).includes(info.terminalName));
        assert.equal(await row.locator('td').count(), 7);
        assert.match(await page.locator('.sd-index-row').filter({ hasText: 'Phiếu cũ' }).textContent(), /Chưa ghi nhận/);
        const cssUrl = await page.locator('link[href*="stock-document-management.css"]').getAttribute('href');
        assert.ok(new URL(cssUrl, info.baseUrl).searchParams.get('v'), 'CSS must have a content version so old seven-column widths cannot stay cached');
        const names = await row.locator('.sd-index-entry-line > span').allTextContents();
        await row.locator('.sd-index-entry-line > span').first().evaluate(el => el.textContent = 'Quản trị Demo Nguyễn Thị Minh Anh');
        await row.locator('.sd-index-entry-line > span').last().evaluate(el => el.textContent = 'Quầy thu ngân 01 · Máy nhập hàng');
        for (const width of [1600, 1366, 1024]) {
            await page.setViewportSize({ width, height: 1000 });
            const geometry = await row.evaluate(el => {
                const employee = el.querySelector('.sd-index-entry');
                const bounds = employee.getBoundingClientRect();
                return { width: bounds.width,
                    nextLeft: employee.nextElementSibling.getBoundingClientRect().left,
                    right: bounds.right,
                    fits: [...employee.querySelectorAll('.sd-index-entry-line > span')].every(n => n.scrollWidth <= n.clientWidth + 1) };
            });
            assert.ok(geometry.width >= 170, `Employee column collapsed at ${width}px`);
            assert.ok(geometry.right <= geometry.nextLeft + 1 && geometry.fits, `Employee text overlaps progress at ${width}px`);
        }
        await page.setViewportSize({ width: 1600, height: 1000 });
        await page.screenshot({ path: 'TestResults/receipt-entry/desktop.png', fullPage: true });
        for (let i = 0; i < names.length; i++) await row.locator('.sd-index-entry-line > span').nth(i).evaluate((el, name) => el.textContent = name, names[i]);
        await row.locator('.js-stock-document-quick-view').click();
        await page.locator('#stockDocumentInfoModal.show').waitFor();
        assert.equal(await page.locator('#sdInfoCreatedBy').textContent(), 'E2E test user');
        assert.ok((await page.locator('#sdInfoEntryTerminal').textContent()).includes(info.terminalName));
        await page.locator('#stockDocumentInfoModal [data-bs-dismiss=modal]').first().click();
        await page.locator('#stockDocumentInfoModal').waitFor({ state: 'hidden' });
        await page.locator('.modal-backdrop').waitFor({ state: 'hidden' });
        await page.setViewportSize({ width: 390, height: 844 });
        const card = page.locator(`.sd-index-mobile-card[data-id="${info.receiptId}"]`);
        await card.waitFor();
        assert.match(await card.textContent(), /E2E test user/);
        assert.ok((await card.textContent()).includes(info.terminalName));
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1));
        await page.screenshot({ path: 'TestResults/receipt-entry/mobile.png', fullPage: true });
        assert.deepEqual(errors, []);
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exit(1); });
