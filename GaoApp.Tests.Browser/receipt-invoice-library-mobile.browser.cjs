'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let value = ''; process.stdin.on('data', chunk => value += chunk); process.stdin.on('end', () => resolve(value));
    }));
    fs.mkdirSync(info.output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const results = { permissionCases: [], resizes: [], filterValidation: [], screenshots: [], foreignStore: null };
    async function guardedContext(baseUrl) {
        const context = await browser.newContext({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block' });
        const origin = new URL(baseUrl).origin;
        await context.addInitScript(() => { window.print = () => {}; window.open = () => null; });
        await context.route('**/*', route => {
            const url = new URL(route.request().url());
            return url.origin === origin || ['data:', 'blob:', 'about:'].includes(url.protocol)
                ? route.continue() : route.abort('blockedbyclient');
        });
        return context;
    }
    async function login(page, baseUrl, account, terminalId) {
        await page.goto(baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(account.user);
        await page.locator('[name=Password]').fill(account.password);
        if (await page.locator('[name=SelectedTerminalId]').count())
            await page.locator('[name=SelectedTerminalId]').selectOption(String(terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm').evaluate(form => form.requestSubmit())]);
        await page.goto(baseUrl + '/admin/input-invoices');
    }
    async function settle(page) {
        await page.evaluate(() => document.fonts.ready);
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    }
    async function filters(page, role) {
        const toggle = page.locator('.il-mobile-filter-toggle');
        await toggle.waitFor();
        await toggle.click();
        await page.locator('#ilFilters [name=year]').fill('1999');
        await toggle.click();
        assert.equal(await toggle.getAttribute('aria-expanded'), 'false');
        const valid = await page.locator('#ilFilters [name=year]').evaluate(field => field.reportValidity());
        assert.equal(valid, false, role + ' existing native year boundary');
        assert.equal(await toggle.getAttribute('aria-expanded'), 'true', role + ' invalid original filter becomes visible');
        await page.locator('#ilFilters [name=year]').fill('2026');
        await page.locator('#ilFilters [name=month]').selectOption('10');
        await toggle.click();
        await page.evaluate(() => {
            window.__ui11LibraryFilterFields = [...document.getElementById('ilFilters').elements]
                .filter(field => field.name).map(node => ({ node, value: node.value, form: node.form, disabled: node.disabled }));
        });
        for (const width of [320, 767, 768, 1440, 390]) {
            await page.setViewportSize({ width, height: 844 }); await settle(page);
            assert.ok(await page.evaluate(() => window.__ui11LibraryFilterFields.every(field => field.node.isConnected &&
                field.node.value === field.value && field.node.form === field.form && field.node.disabled === field.disabled)),
            role + ' filter value/native form owner survives collapsed filters and resizing');
            if (width >= 768) assert.equal(await toggle.count(), 0, role + ' desktop filter presentation restored');
        }
        results.filterValidation.push({ role, invalidYearRevealed: true, originalFieldsAndValuesRetained: true });
    }
    function mayView(role, kind) {
        return role === 'cost' || role === 'both' || role === 'direct' && ['unlinked', 'direct'].includes(kind) ||
            role === 'purchase' && ['purchase', 'foreign-key'].includes(kind);
    }
    const scenarios = {
        viewer: ['unlinked', 'purchase', 'foreign-key', 'mixed'],
        cost: ['unlinked', 'purchase', 'mixed'], both: ['unlinked', 'purchase', 'mixed'],
        direct: ['direct', 'purchase'], purchase: ['foreign-key', 'direct']
    };
    try {
        for (const [role, kinds] of Object.entries(scenarios)) {
            const context = await guardedContext(info.baseUrl);
            const page = await context.newPage(), errors = [], files = [], writes = [];
            page.setDefaultTimeout(25000);
            page.on('pageerror', error => errors.push(error.message));
            await login(page, info.baseUrl, info.accounts[role], info.terminalId);
            await page.waitForFunction(() => document.querySelectorAll('#ilRows [data-open]').length === 6);
            page.on('request', request => {
                const pathname = new URL(request.url()).pathname;
                if (/\/admin\/input-invoices\/\d+\/(pdf|xml|preview)$/.test(pathname)) files.push(pathname);
                if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method()) && !/^\/admin\/api\/store-activity\/(online|presence)$/.test(pathname))
                    writes.push({ method: request.method(), pathname });
            });
            assert.equal(await page.locator('#ilSync, #ilUpload').count(), 0, role + ' no import capability from view/approve/cost permissions');
            await filters(page, role);
            if (role === 'viewer') {
                assert.match(await page.locator('#ilTypes').innerText(), /Không có quyền xem giá/);
                assert.doesNotMatch(await page.locator('#ilRows').innerText(), /108[.,]000|8[.,]000|B2_PRIVATE_PRICE_NOTE/);
            }
            for (const kind of kinds) {
                const allowed = mayView(role, kind), review = allowed && role !== 'cost';
                const beforeFiles = files.length;
                await page.locator(`#ilRows [data-open="${info.ids[kind]}"]`).click();
                await page.waitForFunction(() => document.getElementById('ilDetailMeta').textContent.includes('MST'));
                if (allowed) {
                    await page.locator('#ilShowXml').click();
                    await page.locator('#ilPreviewContent article').waitFor();
                }
                await page.evaluate(() => {
                    window.__ui11LibraryDialogFields = [...document.querySelectorAll('#ilDetail input,#ilDetail select,#ilDetail textarea')]
                        .map(node => ({ node, value: node.value, disabled: node.disabled, readonly: node.readOnly, form: node.form }));
                });
                for (const [width, height] of [[320, 568], [390, 440], [767, 900], [768, 900], [1440, 900], [390, 844]]) {
                    await page.setViewportSize({ width, height }); await settle(page);
                    const state = await page.evaluate(() => {
                        const dialog = document.getElementById('ilDetail'), rect = dialog.getBoundingClientRect();
                        const visible = id => {
                            const element = document.getElementById(id), box = element.getBoundingClientRect();
                            return !element.hidden && box.width > 0 && box.height > 0 && getComputedStyle(element).visibility !== 'hidden';
                        };
                        return { width: innerWidth, height: innerHeight, dialog: { x: rect.x, y: rect.y, right: rect.right, bottom: rect.bottom },
                            saveVisible: visible('ilSave'), saveDisabled: document.getElementById('ilSave').disabled,
                            statusDisabled: document.getElementById('ilReviewStatus').disabled, noteDisabled: document.getElementById('ilReviewNote').disabled,
                            xml: visible('ilShowXml'), pdf: visible('ilShowPdf'), download: visible('ilDownload'),
                            downloadHref: document.getElementById('ilDownload').getAttribute('href'), iframeCount: dialog.querySelectorAll('iframe').length,
                            nativeFieldsRetained: window.__ui11LibraryDialogFields.every(field => field.node.isConnected && field.node.value === field.value &&
                                field.node.disabled === field.disabled && field.node.readOnly === field.readonly && field.node.form === field.form),
                            phoneAttributes: [...document.querySelectorAll('*')].flatMap(node => [...node.attributes].filter(attribute => attribute.name.startsWith('data-il-phone'))).length };
                    });
                    assert.equal(state.saveVisible, review, `${role}/${kind}/${width} save gate`);
                    assert.equal(state.saveDisabled, !review); assert.equal(state.statusDisabled, !review); assert.equal(state.noteDisabled, !review);
                    assert.equal(state.xml, allowed); assert.equal(state.pdf, allowed); assert.equal(state.download, allowed);
                    assert.equal(state.downloadHref, allowed ? `/admin/input-invoices/${info.ids[kind]}/xml` : null);
                    assert.ok(state.nativeFieldsRetained, `${role}/${kind} native field identity and restrictions`);
                    assert.ok(state.dialog.x >= -1 && state.dialog.y >= -1 && state.dialog.right <= width + 1 && state.dialog.bottom <= height + 1,
                        JSON.stringify({ role, kind, ...state }));
                    if (width >= 768) assert.equal(state.phoneAttributes, 0, role + ' no mobile attributes on desktop');
                    if (!allowed) {
                        assert.equal(files.length, beforeFiles, `${role}/${kind} no preview/original network fetch on resize`);
                        assert.equal(state.iframeCount, 0);
                        assert.doesNotMatch(await page.locator('#ilDetail').innerText(), /100[.,]000|108[.,]000|B2_PRIVATE_PRICE_NOTE/);
                    }
                    results.resizes.push({ role, kind, allowed, review, ...state });
                }
                if (kind === kinds[0]) {
                    const name = `${role}-${kind}-phone.png`;
                    await page.screenshot({ path: path.join(info.output, name), animations: 'disabled' });
                    results.screenshots.push(name);
                }
                await page.locator('#ilDetail footer [data-close]').click();
                await page.waitForFunction(() => !document.getElementById('ilDownload').hasAttribute('href'));
                results.permissionCases.push({ role, kind, costAllowed: allowed, reviewAllowed: review, filesNeverFetchedWhenRestricted: !allowed });
            }
            assert.deepEqual(writes, [], role + ' presentation produced no catalog/receipt/store writes');
            assert.deepEqual(errors, [], role + ' JavaScript errors');
            await context.close();
        }
        const context = await guardedContext(info.foreignBaseUrl), page = await context.newPage();
        await login(page, info.foreignBaseUrl, info.foreignAccount, info.foreignTerminalId);
        await page.waitForFunction(() => document.getElementById('ilTotal').textContent === '0');
        assert.equal(await page.locator('#ilRows [data-open]').count(), 0);
        const foreign = await page.request.get(info.foreignBaseUrl + `/admin/input-invoices/${info.ids.unlinked}`,
            { headers: { Accept: 'application/json' }, maxRedirects: 0 });
        assert.equal(foreign.status(), 404);
        results.foreignStore = { renderedRows: 0, directDetailStatus: 404 };
        await context.close();
        console.log(`PASS UI-11 library: ${results.permissionCases.length} real permission/source cases, ${results.resizes.length} dialog resize checks; native fields/read-only/cost/file gates and filter validation retained; zero presentation writes; foreign tenant denied.`);
    } finally {
        fs.writeFileSync(path.join(info.output, 'mobile-library-permissions.json'), JSON.stringify(results, null, 2) + '\n');
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
