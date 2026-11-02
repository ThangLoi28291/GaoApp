const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('../../GaoApp.Tests.Browser/node_modules/playwright');

test('product filter submits, persists through pagination and reload, resets and works on mobile', async () => {
    const root = path.resolve(__dirname, '../..');
    const view = fs.readFileSync(path.join(root, 'GaoApp.Web/Areas/Admin/Views/POSOrderPage/Index.cshtml'), 'utf8');
    const markupStart = view.indexOf('<div class="pos-orders-page"');
    const markupEnd = view.indexOf('@section PageScripts');
    assert.ok(markupStart >= 0 && markupEnd > markupStart, 'Orders view must expose its root and script section');
    const markup = view.slice(markupStart, markupEnd);
    const css = fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot/Admin/css/pos/pos-orders-page.css'), 'utf8');
    const mobileCss = fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot/Admin/css/pos/pos-orders-page.mobile.css'), 'utf8');
    const js = fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot/Admin/js/pos/pos.orders.page.js'), 'utf8');
    const mobileJs = fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot/Admin/js/pos/pos.orders.mobile.js'), 'utf8');
    const output = path.join(root, 'Logs/pos-product-search-20261010');
    fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage();
    const requests = [], errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/*', async route => {
        const url = new URL(route.request().url());
        assert.equal(url.hostname, 'product-search.test');
        assert.equal(route.request().method(), 'GET');
        if (url.pathname === '/orders.js') return route.fulfill({ contentType: 'text/javascript', body: js });
        if (url.pathname === '/orders.mobile.js') return route.fulfill({ contentType: 'text/javascript', body: mobileJs });
        if (url.pathname === '/admin/pos/orders') {
            const params = Object.fromEntries(url.searchParams);
            requests.push(params);
            const currentPage = Number(params.page || 1);
            const size = Number(params.pageSize || 20);
            return route.fulfill({ json: { items: [{ orderId: currentPage, orderNumber: 'PRODUCT-' + currentPage,
                status: 'Completed', paymentStatus: 'Paid', grandTotal: 100, paidTotal: 100, balanceDue: 0 }],
                totalItems: 25, page: currentPage, pageSize: size } });
        }
        return route.fulfill({ contentType: 'text/html', body: '<!doctype html><html lang="vi"><meta charset="utf-8">' +
            '<meta name="viewport" content="width=device-width, initial-scale=1"><style>body{margin:24px;background:#f4f6fa;font-family:Arial,sans-serif}' +
            css + mobileCss + '</style>' + markup + '<script src="/orders.js"></script><script src="/orders.mobile.js"></script></html>' });
    });
    async function loaded() {
        await page.waitForFunction(() => document.querySelector('.po-table').getAttribute('aria-busy') === 'false');
    }
    async function action(callback) {
        const response = page.waitForResponse(r => new URL(r.url()).pathname === '/admin/pos/orders');
        await callback(); await response; await loaded();
    }
    try {
        await page.goto('http://product-search.test/admin/pos/orders-page?product=sua%20dau%20nanh&page=2&pageSize=20');
        await loaded();
        assert.equal(await page.locator('#product').inputValue(), 'sua dau nanh');
        assert.equal(requests.at(-1).product, 'sua dau nanh');
        assert.equal(requests.at(-1).page, '2');
        assert.match(await page.locator('#filterSummary').innerText(), /Sản phẩm: sua dau nanh/);
        assert.equal(await page.locator('#btnToggleFilters').getAttribute('aria-expanded'), 'true');
        await page.locator('#product').fill('  Sữa Đậu Nành  ');
        await page.locator('#customer').fill('KH-PRODUCT');
        await action(() => page.locator('#product').press('Enter'));
        assert.equal(requests.at(-1).product, 'Sữa Đậu Nành');
        assert.equal(requests.at(-1).customer, 'KH-PRODUCT');
        assert.equal(requests.at(-1).page, '1');
        assert.equal(new URL(page.url()).searchParams.get('product'), 'Sữa Đậu Nành');
        await page.screenshot({ path: path.join(output, 'product-filter-desktop.png') });
        await page.locator('#product').fill('Chưa áp dụng');
        await action(() => page.locator('#btnNextPage').click());
        assert.equal(requests.at(-1).product, 'Sữa Đậu Nành');
        assert.equal(requests.at(-1).page, '2');
        await action(() => page.locator('#btnRefresh').click());
        assert.equal(requests.at(-1).product, 'Sữa Đậu Nành');
        await page.reload(); await loaded();
        assert.equal(await page.locator('#product').inputValue(), 'Sữa Đậu Nành');
        assert.equal(await page.locator('#customer').inputValue(), 'KH-PRODUCT');
        await page.setViewportSize({ width: 390, height: 844 });
        assert.equal(await page.locator('#product').isVisible(), true);
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        await page.screenshot({ path: path.join(output, 'product-filter-mobile.png') });
        if (await page.locator('#btnResetFilters').isHidden()) await page.locator('#btnToggleFilters').click();
        await action(() => page.locator('#btnResetFilters').click());
        assert.equal(await page.locator('#product').inputValue(), '');
        assert.equal(requests.at(-1).product, undefined);
        assert.equal(requests.at(-1).customer, undefined);
        assert.equal(requests.at(-1).page, '1');
        assert.equal(new URL(page.url()).searchParams.has('product'), false);
        assert.ok(!(await page.locator('#filterSummary').innerText()).includes('Sản phẩm:'));
        assert.deepEqual(errors, []);
    } finally {
        await context.close(); await browser.close();
    }
});
