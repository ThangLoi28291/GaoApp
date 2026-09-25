'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', x => text += x); process.stdin.on('end', () => resolve(text)); }));
    const output = path.resolve('Logs/media-library-results/browser'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1050 } });
    const page = await context.newPage(), errors = [];
    page.on('pageerror', e => errors.push(e.message));
    page.on('dialog', dialog => dialog.accept());
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('select[name=SelectedTerminalId]').count()) await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
        await page.goto(info.baseUrl + '/admin/media-library');
        await page.waitForFunction(() => document.querySelectorAll('[data-media-image]').length === 8 && [...document.querySelectorAll('[data-media-image]')].every(x => x.complete && x.naturalWidth > 0));
        assert.equal(await page.locator('.media-card').count(), 8);
        assert.equal(await page.locator('.media-badge-used').count(), 2);
        assert.equal(await page.locator('.media-badge-ready').count(), 2);
        for (const [width, height] of [[1440, 1050], [1024, 900], [768, 1024], [390, 844]]) {
            await page.setViewportSize({ width, height });
            await page.screenshot({ path: path.join(output, `library-${width}.png`), fullPage: true });
            const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 2);
            assert.equal(overflow, false, `page overflow at ${width}`);
        }
        await page.setViewportSize({ width: 1440, height: 1050 });
        await page.locator('[data-preview]').first().click();
        assert.equal(await page.locator('dialog[open]').count(), 1);
        await page.keyboard.press('Escape'); assert.equal(await page.locator('dialog[open]').count(), 0);
        await page.locator('#mediaSearch').fill('Sua-tuoi');
        await Promise.all([page.waitForURL('**/*search=Sua-tuoi*'), page.getByRole('button', { name: 'Lọc ảnh' }).click()]);
        assert.equal(await page.locator('.media-card').count(), 1);
        await page.goto(info.baseUrl + '/admin/media-library?statusFilter=used');
        assert.equal(await page.locator('.media-card').count(), 2);
        assert.equal(await page.locator('[data-media-action]').count(), 0);
        await page.goto(info.baseUrl + '/admin/media-library?statusFilter=temp');
        await Promise.all([page.waitForNavigation(), page.locator('[data-media-action="cancel-temp"]').first().click()]);
        assert.equal(await page.locator('.media-card').count(), 1);
        await page.goto(info.baseUrl + '/admin/media-library');
        await Promise.all([page.waitForNavigation(), page.locator('[data-cleanup-all]').click()]);
        assert.equal(await page.locator('.media-card').count(), 5);
        assert.equal(await page.locator('.media-badge-used').count(), 2);
        await page.goto(info.baseUrl + '/admin/media-library?statusFilter=deleted');
        assert.equal(await page.locator('.media-card').count(), 3);
        assert.equal(await page.locator('[data-media-image]').count(), 0);
        await page.goto(info.baseUrl + '/admin/media-library?search=no-such-file');
        assert.equal(await page.locator('.media-empty').count(), 1);
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ passed: true, viewports: [1440, 1024, 768, 390], checks: ['real images', 'preview', 'search', 'filters', 'cancel', 'cleanup', 'retains used', 'deleted history', 'empty state', 'no overflow', 'no JS errors'] }, null, 2));
        console.log('Media library browser checks passed.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
