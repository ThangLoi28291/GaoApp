const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('../../GaoApp.Tests.Browser/node_modules/playwright');

test('actual orders renderer shows settled financials, pending quote, linked documents and credit on desktop/mobile', async () => {
    const root = path.resolve(__dirname, '../..');
    const view = fs.readFileSync(path.join(root, 'GaoApp.Web/Areas/Admin/Views/POSOrderPage/Index.cshtml'), 'utf8');
    const markup = view.slice(view.indexOf('<div class="pos-orders-page"'), view.indexOf('@section PageScripts'));
    const css = fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot/Admin/css/pos/pos-orders-page.css'), 'utf8');
    const js = fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot/Admin/js/pos/pos.orders.page.js'), 'utf8');
    const templates = fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js'), 'utf8');
    const now = '2026-10-08T15:25:00Z';
    const paid = { orderId: 22, orderNumber: 'POS-20261008-00004', status: 'Completed', paymentStatus: 'Paid',
        grandTotal: 23500, paidTotal: 23500, balanceDue: 0, paymentMethods: ['Cash'],
        createdAtUtc: now, completedAtUtc: now, customerName: 'Baby Nhật Linh', cashierName: 'Thu ngân', terminalName: 'Quầy B',
        delivery: { deliveryOrderId: 7, code: 'GH-20261008-A3D906AAC5EF64CE', state: 'Settled', settlementStatus: 'settled',
            isSettlementSale: true, sourceOrderId: 11, saleOrderId: 22, saleOrderNumber: 'POS-20261008-00004' } };
    const pending = { orderId: 33, status: 'Cancelled', paymentStatus: 'Unpaid', grandTotal: 37000, paidTotal: 0, balanceDue: 37000,
        delivery: { deliveryOrderId: 8, code: 'GH-PENDING', state: 'Picking', settlementStatus: 'pending', isPendingSettlement: true, isSettlementSale: false } };
    const ordinary = { orderId: 44, status: 'Completed', paymentStatus: 'Paid', grandTotal: 1000, paidTotal: 1000, balanceDue: 0 };
    let current = paid;
    const output = path.join(root, 'Logs/pos-delivery-unified-20261008'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
    const page = await context.newPage(), errors = [], commands = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/*', async route => {
        const request = route.request(), url = new URL(request.url());
        assert.equal(url.hostname, 'unified.test');
        if (request.method() !== 'GET') commands.push(request.method());
        if (url.pathname === '/orders.js') return route.fulfill({ contentType: 'text/javascript', body: js });
        if (url.pathname === '/templates.js') return route.fulfill({ contentType: 'text/javascript', body: templates });
        if (url.pathname === '/admin/pos/orders') return route.fulfill({ json: { items: [current, pending, ordinary], totalItems: 3, page: 1, pageSize: 20 } });
        if (url.pathname === '/admin/pos/orders/22') return route.fulfill({ json: { ...current, finalizedAtUtc: now,
            subtotal: 30000, discountTotal: 6500, promotionDiscountTotal: 6500,
            lines: [{ itemName: 'Sản phẩm thực giao', quantity: 1, unitPrice: 30000, grossAmount: 30000,
                promotionDiscount: 6500, promotionName: 'Khuyến mãi', lineTotal: 23500 }],
            payments: current.isCreditSale ? [] : [{ method: 'Cash', amount: 23500, createdAt: now }] } });
        return route.fulfill({ contentType: 'text/html', body: '<!doctype html><html lang="vi"><meta charset="utf-8"><style>body{margin:24px;background:#f4f6fa;font-family:Arial,sans-serif}' + css + '</style>' + markup + '<script src="/templates.js"></script><script src="/orders.js"></script></html>' });
    });
    try {
        await page.goto('http://unified.test/admin/pos/orders-page');
        await page.waitForFunction(() => document.querySelector('.po-table').getAttribute('aria-busy') === 'false');
        assert.equal(await page.locator('.po-order-row').count(), 3);
        const row = page.locator('[data-order-id="22"]');
        assert.match(await row.innerText(), /Đơn giao · Đã chốt/);
        assert.match(await row.innerText(), /Theo hàng thực giao/);
        assert.match(await row.innerText(), /Đã trả\s+23\.500 đ/);
        assert.match(await row.innerText(), /Tiền mặt/);
        assert.ok(!(await row.innerText()).includes('Giá trị dự kiến'));
        assert.match(await page.locator('[data-order-id="33"]').innerText(), /Chưa thu tiền/);
        assert.equal(await page.locator('#metricPaid').innerText(), '24.500đ');
        assert.equal(await page.locator('#metricDue').innerText(), '0đ');
        await page.screenshot({ path: path.join(output, 'orders-desktop.png') });
        await row.locator('[data-preview-id]').click();
        await page.waitForFunction(() => document.querySelector('#previewBody').getAttribute('aria-busy') === 'false');
        assert.match(await page.locator('#previewBody').innerText(), /Thanh toán theo hàng thực giao/);
        assert.match(await page.locator('#previewBody').innerText(), /Các khoản thanh toán/);
        assert.equal(await page.locator('.po-delivery-documents a[href="/admin/pos/order-detail/11"]').count(), 1);
        assert.equal(await page.locator('.po-delivery-documents a[href="/admin/deliveries/7/bill"]').count(), 1);
        assert.equal(await page.locator('.po-delivery-documents a[href="/admin/pos/orders/22/print?autoPrint=false"]').count(), 1);
        await page.screenshot({ path: path.join(output, 'preview-desktop.png') });
        await page.setViewportSize({ width: 390, height: 844 });
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        await page.screenshot({ path: path.join(output, 'preview-mobile.png') });
        await page.locator('#btnClosePreview').click();
        current = { ...paid, isCreditSale: true, paidTotal: 0, balanceDue: 23500, paymentStatus: 'Unpaid', paymentMethods: [] };
        await page.locator('#btnRefresh').click();
        await page.waitForFunction(() => document.querySelector('.po-table').getAttribute('aria-busy') === 'false');
        assert.match(await row.innerText(), /Công nợ · còn nợ/);
        assert.match(await row.innerText(), /Còn thiếu\s+23\.500 đ/);
        assert.equal(await page.locator('#metricDue').innerText(), '23.500đ');
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        assert.deepEqual(errors, []); assert.deepEqual(commands, []);
    } finally { await browser.close(); }
});
