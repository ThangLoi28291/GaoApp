// Run PosRewardEligibilitySqlServerTests first to capture the actual Razor page.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..');
const assets = path.join(root, 'GaoApp.Web', 'wwwroot');
const output = path.join(root, 'Logs', 'reward-eligibility-tests');
const types = { '.css': 'text/css', '.js': 'application/javascript', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2', '.woff': 'font/woff', '.ttf': 'font/ttf' };

(async () => {
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        for (const width of [320, 390, 1440]) {
            const page = await browser.newPage({ viewport: { width, height: width > 1000 ? 1080 : 844 } });
            const errors = [];
            page.on('pageerror', e => errors.push(e.message));
            await page.route('**/*', async route => {
                const url = new URL(route.request().url());
                if (url.hostname !== 'rewards.test') return route.fulfill({ body: '' });
                if (url.pathname === '/admin/reward-vouchers/settings')
                    return route.fulfill({ contentType: 'text/html', path: path.join(output, 'settings.html') });
                const file = path.resolve(assets, '.' + decodeURIComponent(url.pathname));
                if (file.startsWith(assets + path.sep) && fs.existsSync(file) && fs.statSync(file).isFile())
                    return route.fulfill({ contentType: types[path.extname(file)] || 'application/octet-stream', path: file });
                return route.fulfill({ status: 404, body: '' });
            });
            await page.goto('http://rewards.test/admin/reward-vouchers/settings');
            const boxes = page.locator('[name="ExcludedCategoryIds"]');
            assert.equal(await boxes.count(), 2);
            assert.equal(await page.locator('[name="ExcludedCategoryIds"]:checked').count(), 0);
            await boxes.first().check();
            await page.locator('#rewardCategorySearch').fill('sua tuoi');
            assert.equal(await page.locator('[data-reward-category]:visible').count(), 1);
            // Hiding a selected parent during search must not drop it from the submitted form.
            assert.equal(await page.evaluate(() => new FormData(document.querySelector('#rewardSettingsForm')).getAll('ExcludedCategoryIds').length), 1);
            await page.locator('#rewardCategorySearch').fill('khong tim thay');
            assert.equal(await page.locator('[data-reward-category]:visible').count(), 0);
            await page.locator('#rewardCategorySearch').fill('');
            assert.equal(await page.locator('[data-reward-category]:visible').count(), 2);
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Overflow at ${width}px`);
            await page.locator('#rewardCategoryList').scrollIntoViewIfNeeded();
            await page.screenshot({ path: path.join(output, `settings-${width}.png`), fullPage: width === 1440 });
            assert.deepEqual(errors, []);
            console.log(`PASS reward category settings at ${width}px`);
            await page.close();
        }
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
