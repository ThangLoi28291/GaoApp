// Run the InventoryInquiryCostSqlServerTests and InventoryLedgerInboundCostSqlServerTests
// first to create real HTML/API fixtures in Logs, then: node GaoApp.Tests.Browser/inventory-cost.browser.cjs
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '..');
const assets = path.join(root, 'GaoApp.Web', 'wwwroot');
const output = path.join(root, 'Logs', 'inventory-cost-browser');
const mimeTypes = { '.css': 'text/css', '.js': 'application/javascript', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2', '.woff': 'font/woff', '.ttf': 'font/ttf' };
const modules = [
    { route: 'inventory-inquiry', fixtures: 'inventory-admin-cost-ui', prefix: 'inventory', row: '.inventory-row', action: '.js-inventory-quick-view', details: '.inventory-cost-details', costColumn: '[data-inventory-cost-column]', cell: '.inventory-cost-cell', mobileCost: '.inventory-mobile-cost' },
    { route: 'inventory-ledger', fixtures: 'inventory-ledger-cost-ui', prefix: 'ledger', row: '.ledger-row', action: '.js-ledger-quick', details: '.ledger-inbound-cost', costColumn: '[data-ledger-cost-column]', cell: '.ledger-cost-cell', mobileCost: '.ledger-mobile-cost' }
];

(async () => {
    fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const results = [];
    try {
        for (const module of modules) {
            const evidence = path.join(root, 'Logs', module.fixtures);
            for (const profile of ['admin', 'staff']) {
                for (const width of [1440, 390]) {
                    const isAdmin = profile === 'admin';
                    const context = await browser.newContext({ viewport: { width, height: width > 1000 ? 1080 : 844 }, timezoneId: 'Asia/Bangkok' });
                    const page = await context.newPage();
                    const errors = [];
                    const list = JSON.parse(fs.readFileSync(path.join(evidence, `${profile}-page.json`), 'utf8'));
                    const quick = JSON.parse(fs.readFileSync(path.join(evidence, `${profile}-quick.json`), 'utf8'));
                    const item = quick.item || quick;
                    page.on('pageerror', error => errors.push(error.message));
                    await page.route('**/*', async route => {
                        const url = new URL(route.request().url());
                        if (url.hostname !== 'inventory-cost.test') return route.fulfill({ contentType: route.request().resourceType() === 'stylesheet' ? 'text/css' : 'application/javascript', body: '' });
                        if (url.pathname === `/admin/${module.route}`) return route.fulfill({ contentType: 'text/html', path: path.join(evidence, `${profile}.html`) });
                        if (url.pathname === `/admin/${module.route}/data`) return route.fulfill({ contentType: 'application/json', body: JSON.stringify(list) });
                        if (url.pathname === `/admin/${module.route}/quick-view`) return route.fulfill({ contentType: 'application/json', body: JSON.stringify(quick) });
                        if (url.pathname === '/admin/api/warehouses/select2') return route.fulfill({ contentType: 'application/json', body: '{"results":[{"id":1,"text":"R2 Inventory Warehouse"}]}' });
                        const file = path.resolve(assets, '.' + decodeURIComponent(url.pathname));
                        if (file.startsWith(assets + path.sep) && fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({ contentType: mimeTypes[path.extname(file)] || 'application/octet-stream', path: file });
                        return route.fulfill({ status: 404, body: '' });
                    });
                    await page.goto(`http://inventory-cost.test/admin/${module.route}`);
                    await page.locator(`#${module.prefix}DesktopBody ${module.row}`).first().waitFor({ state: 'attached' });
                    assert.equal(await page.locator(`th${module.costColumn}`).getAttribute('hidden') === null, isAdmin);
                    assert.equal((await page.locator(module.cell).count()) > 0, isAdmin);
                    assert.equal((await page.locator(module.mobileCost).count()) > 0, isAdmin);
                    const overflow = await page.evaluate(() => ({ width: innerWidth, document: document.documentElement.scrollWidth,
                        elements: [...document.querySelectorAll('body *')].filter(el => el.getBoundingClientRect().right > innerWidth + 1 && getComputedStyle(el).position !== 'fixed')
                            .slice(0, 12).map(el => ({ tag: el.tagName, id: el.id, className: el.className, right: el.getBoundingClientRect().right })) }));
                    if (overflow.document > overflow.width) await page.screenshot({ path: path.join(output, `${module.route}-${profile}-${width}-overflow.png`), fullPage: true });
                    assert.ok(overflow.document <= overflow.width, `${module.route} ${profile} ${width}: ${JSON.stringify(overflow)}`);
                    const rowFilter = module.route === 'inventory-ledger' ? `[data-transaction-id="${item.transactionId}"]` : '';
                    if (isAdmin && module.route === 'inventory-ledger') {
                        const cells = page.locator(`#ledgerDesktopBody ${module.row}${rowFilter} ${module.cell}`);
                        assert.match(await cells.innerText(), /4\.583 ₫/);
                        for (const outbound of list.items.filter(row => row.quantityChange <= 0)) {
                            assert.equal(await page.locator(`#ledgerDesktopBody [data-transaction-id="${outbound.transactionId}"] ${module.cell}`).innerText(), '—');
                        }
                    }
                    await page.screenshot({ path: path.join(output, `${module.route}-${profile}-${width}-list.png`), fullPage: true });
                    const action = module.route === 'inventory-ledger' ? `${module.action}${rowFilter}:visible` : `${module.action}:visible`;
                    await page.locator(action).first().click();
                    await page.locator(`#${module.prefix}QuickBody .${module.prefix}-quick-grid`).waitFor();
                    assert.equal(await page.locator(module.details).count(), isAdmin ? 1 : 0);
                    if (isAdmin) {
                        if (module.route === 'inventory-inquiry') {
                            assert.match(await page.locator('.inventory-cost-grid > div').nth(1).innerText(), /12 ₫/);
                            const expectedDate = new Date(item.cost.lastInboundAtUtc + 'Z').toLocaleString('vi-VN', { timeZone: 'Asia/Bangkok' });
                            assert.ok((await page.locator(module.details).innerText()).includes(expectedDate));
                        } else {
                            assert.match(await page.locator('.ledger-cost-grid > div').nth(0).innerText(), /4\.583 ₫/);
                            assert.match(await page.locator('.ledger-cost-grid > div').nth(1).innerText(), /1\.100\.000 ₫/);
                        }
                        await page.locator(module.details).scrollIntoViewIfNeeded();
                    }
                    assert.equal(await page.locator(`#${module.prefix}QuickBody`).evaluate(el => el.scrollWidth <= el.clientWidth), true, 'Quick view must not overflow horizontally');
                    await page.screenshot({ path: path.join(output, `${module.route}-${profile}-${width}-quick.png`) });

                    if (isAdmin && module.route === 'inventory-inquiry') {
                        // Verify the user's fractional-cost example and fractional selling price through the real formatter.
                        await page.locator('#inventoryQuickViewModal [data-bs-dismiss="modal"]').first().click();
                        await page.locator('#inventoryQuickViewModal').waitFor({ state: 'hidden' });
                        Object.assign(quick.cost, { nextFifoUnitCost: 4583.3333, averageUnitCost: 4583.3333, lastInboundUnitCost: 4583.3333, inventoryValue: 1099999.992 });
                        quick.units = [{ unitName: 'hộp', factor: 1, sellPrice: 5500.5, isBaseUnit: true }];
                        await page.locator(action).first().click();
                        await page.waitForFunction(() => document.querySelector('.inventory-cost-grid')?.textContent.includes('1.100.000 ₫'));
                        assert.equal(await page.locator('.inventory-cost-grid strong').allTextContents().then(values => values.slice(0, 3).every(value => value === '4.583 ₫')), true);
                        assert.equal(await page.locator('.inventory-unit-price').innerText(), '5.501 ₫');
                    }
                    assert.deepEqual(errors, []);
                    results.push({ module: module.route, profile, width, passed: true });
                    await context.close();
                }
            }
        }
    } finally { await browser.close(); }
    fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(results, null, 2));
    console.log(JSON.stringify(results, null, 2));
})().catch(error => { console.error(error); process.exitCode = 1; });
