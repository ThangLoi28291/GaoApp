'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', x => text += x); process.stdin.on('end', () => resolve(text)); }));
    const output = path.resolve('TestResults/product-filters'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const errors = [];
    try {
        async function login(user, password) {
            const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
            const page = await context.newPage(); page.on('pageerror', e => errors.push(e.message));
            await page.route('**/uploads/test-product-filter.svg', route => route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80"><rect width="80" height="80" fill="#eff9f4"/><text x="13" y="45" fill="#266e50">MILK</text></svg>' }));
            await page.goto(info.baseUrl + '/admin/account/login');
            await page.locator('[name=UserName]').fill(user); await page.locator('[name=Password]').fill(password);
            if (await page.locator('select[name=SelectedTerminalId]').count()) await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
            await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
            await page.goto(info.baseUrl + '/Admin/Product'); return page;
        }
        const page = await login(info.user, info.password);
        async function apply(action) {
            await Promise.all([page.waitForResponse(r => r.url().includes('/Product/Search') && r.ok()), action()]);
            await page.waitForFunction(() => document.querySelector('#productTableWrapper').getAttribute('aria-busy') === 'false');
        }
        async function choose(kind, term) {
            await page.locator('#filter-' + kind).fill(term);
            await page.locator('#options-' + kind + ' [role=option]').first().waitFor();
            await apply(() => page.locator('#filter-' + kind).press('Enter'));
        }
        await choose('supplier', 'nguyen');
        assert.match(await page.locator('#filter-supplier').inputValue(), /Nguyễn Phát.*ngừng hoạt động/);
        assert.equal(await page.locator('.gds-desktop-list [data-product-row]').count(), 1);
        await choose('brand', 'anh duong');
        await choose('unit', 'chai');
        assert.equal(await page.locator('.gds-desktop-list [data-product-row]').count(), 1);
        await apply(() => page.locator('#ddlLifecycle').selectOption('all'));
        assert.equal(await page.locator('.gds-desktop-list [data-product-row]').count(), 2);
        const filteredUrl = page.url();
        for (const field of ['supplierId', 'brandId', 'baseUnitId']) assert.ok(new URL(filteredUrl).searchParams.has(field));
        await page.screenshot({ path: path.join(output, 'desktop.png'), fullPage: true });
        await page.reload();
        assert.match(await page.locator('#filter-supplier').inputValue(), /Nguyễn Phát/);
        assert.match(await page.locator('#filter-brand').inputValue(), /Ánh Dương/);
        assert.equal(await page.locator('.gds-desktop-list [data-product-row]').count(), 2);
        await apply(() => page.locator('#ddlDataIssue').selectOption('no-image'));
        assert.equal(await page.locator('.gds-desktop-list [data-product-row]').count(), 1);
        assert.match(await page.locator('.gds-desktop-list').textContent(), /Sữa tươi ngừng bán/);
        const edit = page.locator('.gds-desktop-list a[href*="/Edit/"]').first();
        assert.ok(new URL(await edit.getAttribute('href'), info.baseUrl).searchParams.get('returnUrl').includes('dataIssue=no-image'));
        await apply(() => page.locator('#btnResetProductFilters').click());
        assert.equal(await page.locator('.gds-desktop-list [data-product-row]').count(), 3);
        assert.equal(await page.locator('#ddlLifecycle').inputValue(), 'all');
        await apply(() => page.goBack());
        assert.equal(await page.locator('#ddlDataIssue').inputValue(), 'no-image');
        assert.equal(await page.locator('.gds-desktop-list [data-product-row]').count(), 1);
        await apply(() => page.locator('#btnResetProductFilters').click());
        await apply(() => page.locator('#ddlDataIssue').selectOption('no-brand'));
        assert.match(await page.locator('.gds-desktop-list').textContent(), /Bánh gạo rong biển/);
        await apply(() => page.getByRole('button', { name: 'Bỏ lọc Chưa có thương hiệu', exact: true }).click());
        await apply(() => page.locator('#txtSearch').fill('sua tuoi'));
        assert.equal(await page.locator('.gds-desktop-list [data-product-row]').count(), 2);
        await page.setViewportSize({ width: 390, height: 844 });
        await page.locator('#filter-brand').fill('anh');
        await page.locator('#options-brand [role=option]').first().waitFor();
        await page.screenshot({ path: path.join(output, 'mobile.png'), fullPage: true });
        assert.equal(await page.locator('.product-extra-filters').evaluate(x => x.scrollWidth > x.clientWidth + 2), false);
        const bounds = await page.locator('[data-product-lookup=brand] .product-lookup-panel').boundingBox();
        assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 391);
        await page.keyboard.press('Escape');
        assert.equal(await page.locator('[data-product-lookup=brand] .product-lookup-panel').isVisible(), false);
        const staff = await login(info.employee, info.employeePassword);
        assert.equal(await staff.locator('[data-product-lookup=supplier]').count(), 0);
        assert.equal(await staff.locator('[data-product-lookup=brand]').count(), 1);
        assert.deepEqual(errors, []);
        console.log('PASS: supplier/brand/unit autocomplete, unaccented terms, inactive labels, combined filters, missing data, chips/reset, URL/reload/back/edit-return persistence, supplier permission and responsive layout.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
