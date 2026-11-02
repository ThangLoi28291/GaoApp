'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let input = ''; process.stdin.on('data', value => input += value); process.stdin.on('end', () => resolve(input));
    }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage();
    const checks = [], errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const check = text => {
        checks.push(text); console.log('PASS:', text);
        fs.writeFileSync(path.join(info.output, 'checks.json'), JSON.stringify({ checks, errors }, null, 2));
    };
    async function fill(title) {
        await page.locator('[name=Title]').fill(title);
        await page.locator('[name=Controller]').fill('Product');
        await page.locator('[name=Action]').fill('Index');
        await page.locator('[name=PermissionCode]').selectOption(info.permission);
        await page.locator('[name=AdministratorPassword]').fill(info.password);
        await page.locator('[name=Reason]').fill('Browser verification of menu display configuration');
    }
    async function assertForm() {
        for (const name of ['AssignRoleIds', 'CreateNewPermission', 'NewPermissionCode', 'NewPermissionName', 'NewPermissionGroupName'])
            assert.equal(await page.locator(`[name="${name}"]`).count(), 0);
        assert.equal(await page.locator('a[href="/Admin/RolePermissions"]').count() > 0, true);
        assert.equal(await page.locator('[name=PermissionCode]').count(), 1);
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
    }
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('select[name=SelectedTerminalId]').count())
            await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);

        await page.goto(info.baseUrl + '/Admin/AdminMenus/Create');
        await assertForm();
        await fill('Browser menu');
        await page.screenshot({ path: path.join(info.output, 'create-desktop.png'), fullPage: true });
        const created = page.waitForResponse(response => response.request().method() === 'POST' && /AdminMenus\/Create/i.test(response.url()));
        await page.locator('form[action*="AdminMenus"] button[type=submit]').click();
        assert.equal((await created).status(), 302);
        await page.waitForURL(url => /\/AdminMenus(?:\/Index)?$/i.test(url.pathname));
        check('new create form saves an existing visibility condition without role-grant fields');

        const editUrl = await page.locator('[data-menu-search]').filter({ hasText: 'Browser menu' })
            .first().locator('a[href*="/AdminMenus/Edit"]').first().getAttribute('href');
        assert.ok(editUrl);
        await page.goto(info.baseUrl + editUrl);
        await assertForm();
        assert.equal(await page.locator('[name=PermissionCode]').inputValue(), info.permission);
        await page.setViewportSize({ width: 390, height: 844 });
        await assertForm();
        await page.screenshot({ path: path.join(info.output, 'edit-mobile.png'), fullPage: true });
        await fill('Browser menu renamed');
        const edited = page.waitForResponse(response => response.request().method() === 'POST' && /AdminMenus\/Edit/i.test(response.url()));
        await page.locator('form[action*="AdminMenus"] button[type=submit]').click();
        // UI-02d requires the existing phone review before the native form may commit.
        await page.locator('[data-menu-review][open]').waitFor();
        assert.match(await page.locator('[data-menu-review-summary]').innerText(), /Browser menu renamed/);
        await page.locator('[data-menu-review-confirm]').click();
        assert.equal((await edited).status(), 302);
        await page.waitForURL(url => /\/AdminMenus(?:\/Index)?$/i.test(url.pathname));
        check('edit form retains its visibility condition and submits correctly on a narrow screen');

        await page.goto(info.baseUrl + '/Admin/AdminMenus/Create');
        await fill('Browser forged menu');
        const rejected = await page.evaluate(async () => {
            const form = document.querySelector('form[action*="AdminMenus"]');
            const fields = new FormData(form);
            fields.set('CreateNewPermission', 'false');
            const response = await fetch(form.action, { method: 'POST', body: fields });
            return { status: response.status, html: await response.text() };
        });
        assert.equal(rejected.status, 400);
        const decoded = await page.evaluate(html => { const node = document.createElement('textarea'); node.innerHTML = html; return node.value; }, rejected.html);
        assert.match(decoded, /Biểu mẫu menu đã thay đổi/);
        check('old form fields receive a reload error and cannot create a menu');
        assert.deepEqual(errors, []);
        check('browser completes all menu checks without uncaught JavaScript errors');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
