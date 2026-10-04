'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', data => text += data); process.stdin.on('end', () => resolve(text)); }));
    const output = path.resolve('TestResults/pos-orders-page/browser'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1600, height: 1000 }, reducedMotion: 'reduce' });
    const page = await context.newPage(), errors = [], commands = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => { if (request.method() !== 'GET' && new URL(request.url()).pathname.startsWith('/admin/pos/')) commands.push(request.url()); });
    let fixtures = false, listError = false, receiptError = false, empty = false, race = false, receiptDelay = false, listDelay = false;
    const requests = [], receiptRequests = [];
    const now = new Date();
    const rows = [
        { orderId: 105, orderNumber: 'POS-20260910-00003', status: 'Completed', paymentStatus: 'Paid', grandTotal: 158000, paidTotal: 158000, balanceDue: 0, hasBankTransfer: true },
        { orderId: 106, orderNumber: null, status: 'Draft', paymentStatus: 'Unpaid', grandTotal: 215000, paidTotal: 0, balanceDue: 215000 },
        { orderId: 104, orderNumber: 'POS-20260910-00002', status: 'Completed', paymentStatus: 'Paid', grandTotal: 482500, paidTotal: 482500, balanceDue: 0, hasBankTransfer: true, voucherDiscountTotal: 25000, rewardVouchers: [{ voucherCode: 'GAO30K', value: 30000 }] },
        { orderId: 103, orderNumber: 'POS-20260910-00001', status: 'Completed', paymentStatus: 'Paid', grandTotal: 685000, paidTotal: 685000, balanceDue: 0, refundedTotal: 50000, returnCount: 1 },
        { orderId: 102, orderNumber: 'POS-20260909-00002', status: 'OnHold', paymentStatus: 'Partial', grandTotal: 320000, paidTotal: 100000, balanceDue: 220000 },
        { orderId: 101, orderNumber: 'POS-20260909-00001', status: 'Cancelled', paymentStatus: 'Unpaid', grandTotal: 125000, paidTotal: 0, balanceDue: 125000 },
        { orderId: 100, orderNumber: 'POS-20260908-00005', status: 'Refunded', paymentStatus: 'Refunded', grandTotal: 95000, paidTotal: 95000, balanceDue: 0, refundedTotal: 95000, returnCount: 1 },
        { orderId: 99, orderNumber: 'POS-20260908-00004', status: 'Voided', paymentStatus: 'Unpaid', grandTotal: 85000, paidTotal: 0, balanceDue: 85000 }
    ].map((row, i) => ({ ...row, createdAtUtc: new Date(now.getTime() - (i + 1) * 3600000).toISOString(), completedAtUtc: row.status === 'Completed' ? new Date(now.getTime() - (i === 0 ? 60000 : 3600000)).toISOString() : null }));
    rows.forEach((row, i) => Object.assign(row, { customerName: 'Nguyễn Minh Anh', customerPhone: '0901234567',
        cashierName: 'Trần Ngọc Hà', terminalName: 'Quầy thu ngân 01', terminalCode: 'POS01',
        paymentMethods: i === 0 ? ['Cash', 'BankTransfer'] : i === 1 ? ['BankTransfer'] : i === 2 ? ['Cash'] : [],
        isCreditSale: i === 0 || i === 3 }));
    const receipt = id => ({ ...rows.find(row => row.orderId === id), orderId: id, customerName: 'Nguyễn Minh Anh', cashierName: 'Trần Ngọc Hà', shiftCode: 'SHIFT-20260910-01', customerPhone: '0901 234 567', finalizedAtUtc: now.toISOString(),
        subtotal: 168000, discountTotal: 10000, grandTotal: 158000, paidTotal: 158000, balanceDue: 0,
        lines: [{ itemName: 'Sữa tươi TH true MILK không đường 180 ml', productVariantName: 'Sữa tươi TH true MILK không đường 180 ml', scannedBarcode: '2000105515438', barcode: 'TH-PACK', sku: 'TH180', quantity: 2, unitPrice: 32000, lineTotal: 64000, sellingUnitName: 'lốc' },
            { itemName: 'Ngũ cốc dinh dưỡng nguyên hạt', barcode: '0893850597419', sku: 'NC-001', quantity: 1, unitPrice: 68000, lineTotal: 68000, sellingUnitName: 'hộp' },
            { itemName: 'Bánh mì nguyên cám', sku: 'BMC-001', quantity: 1, unitPrice: 36000, lineTotal: 36000, sellingUnitName: 'gói' }],
        payments: [{ method: 'BankTransfer', amount: 158000, createdAt: now.toISOString(), reference: 'POS105-ACB' }], note: 'Khách lấy hóa đơn.\nGiao hàng tại quầy.' });
    await page.route('**/admin/pos/orders?*', async route => {
        if (!fixtures) return route.continue();
        const query = new URL(route.request().url()).searchParams;
        requests.push(Object.fromEntries(query));
        if (listDelay) await new Promise(resolve => setTimeout(resolve, 600));
        if (listError) return route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Máy chủ tạm thời không phản hồi.' }) });
        const keyword = query.get('keyword') || '';
        if (race && keyword === 'slow') await new Promise(resolve => setTimeout(resolve, 700));
        let items = empty ? [] : rows.filter(row => !query.get('status') || row.status === query.get('status'));
        if (keyword === 'safe') items = [{ ...rows[0], orderNumber: '<img src=x onerror=alert(1)>', rewardVouchers: [{ voucherCode: '"><script>bad()</script>' }] }];
        if (race) items = [{ ...rows[0], orderNumber: keyword }];
        const pageNumber = Number(query.get('page') || 1), pageSize = Number(query.get('pageSize') || 20);
        const totalItems = items.length && !query.get('status') && !keyword ? 28 : items.length;
        if (pageNumber > 1) items = [{ ...rows[0], orderId: 200, orderNumber: 'POS-TRANG-2' }];
        return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ items, totalItems, page: pageNumber, pageSize }) }).catch(() => {});
    });
    await page.route(/\/admin\/pos\/orders\/\d+$/, async route => {
        if (!fixtures) return route.continue();
        receiptRequests.push(route.request().url());
        if (receiptError) return route.fulfill({ status: 403, contentType: 'application/json', body: '{}' });
        const id = Number(new URL(route.request().url()).pathname.split('/').pop());
        if (receiptDelay && id === 105) await new Promise(resolve => setTimeout(resolve, 650));
        return route.fulfill({ contentType: 'application/json', body: JSON.stringify(receipt(id)) }).catch(() => {});
    });
    const loaded = () => page.waitForFunction(() => document.querySelector('.po-table')?.getAttribute('aria-busy') === 'false');
    await page.route('**/admin/pos/orders/filter-options?*', async route => {
        if (!fixtures) return route.continue();
        const query = new URL(route.request().url()).searchParams, kind = query.get('kind'), term = query.get('term');
        if (term === 'slow') await new Promise(resolve => setTimeout(resolve, 700));
        const data = term === 'zzz' ? [] : kind === 'employee'
            ? [{ id: 501, name: term === 'slow' ? 'Old response' : 'Nguyễn Thu Hà', code: 'ha01' }, { id: 502, name: 'Nguyễn Thu Hà', code: 'ha02' }]
            : [{ id: 601, name: 'Quầy chính', code: 'POS01' }, { id: 602, name: 'Quầy chính', code: 'POS02' }];
        await route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) }).catch(() => {});
    });
    async function refresh() { await page.locator('#btnRefresh').click(); await loaded(); }
    async function screenshot(name) { await page.screenshot({ path: path.join(output, name + '.png'), fullPage: false }); }
    async function noOverflow() {
        const overflow = await page.evaluate(() => ({ width: innerWidth, document: document.documentElement.scrollWidth, root: document.querySelector('.pos-orders-page').getBoundingClientRect().right }));
        if (overflow.document > overflow.width + 1 || overflow.root > overflow.width + 1) {
            await screenshot('overflow-' + overflow.width);
            console.log('Overflow elements', await page.evaluate(() => [...document.querySelectorAll('body *')]
                .filter(node => node.getBoundingClientRect().right > innerWidth + 1)
                .map(node => ({ tag: node.tagName, id: node.id, class: node.className, right: node.getBoundingClientRect().right })).slice(-20)));
        }
        assert.ok(overflow.document <= overflow.width + 1 && overflow.root <= overflow.width + 1, JSON.stringify(overflow));
        const overlap = await page.locator('.po-order-row').evaluateAll(rows => rows.some(row => {
            const cells = [...row.children].filter(cell => getComputedStyle(cell).display !== 'none');
            return cells.some(cell => cell.scrollWidth > cell.clientWidth + 2);
        }));
        assert.equal(overlap, false, 'Order cells must not overflow');
    }
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('select[name=SelectedTerminalId]').count()) await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
        await page.goto(info.baseUrl + '/admin/pos/orders-page'); await loaded();
        assert.match(await page.locator('#ordersBody').innerText(), /Không tìm thấy đơn hàng/);
        assert.equal(await page.locator('#metricCount').innerText(), '0');
        console.log('PASS real authenticated Razor and empty list API on disposable SQL.');
        fixtures = true; await refresh();
        assert.equal(await page.locator('.po-order-row').count(), 8);
        assert.equal(await page.locator('#metricCount').innerText(), '28');
        assert.equal((await page.locator('#metricTotal').innerText()).replace(/\s/g, ''), rows.reduce((sum, row) => sum + row.grandTotal, 0).toLocaleString('vi-VN') + 'đ');
        assert.match(await page.locator('#filterSummary').innerText(), /Tất cả thời gian/);
        const firstText = await page.locator('[data-order-id="105"]').innerText();
        assert.match(firstText, /Nguyễn Minh Anh/); assert.match(firstText, /Trần Ngọc Hà/);
        assert.match(firstText, /Quầy thu ngân 01/); assert.match(firstText, /Tiền mặt/); assert.match(firstText, /Chuyển khoản/);
        assert.match(firstText, /Công nợ/);
        for (const [width, height] of [[1600, 1000], [1440, 900], [1366, 768], [1024, 768], [768, 1024], [390, 844], [360, 800]]) {
            await page.setViewportSize({ width, height }); await noOverflow();
            if (width >= 768) assert.equal(await page.locator('#btnToggleFilters').isVisible(), false, 'Mobile filter toggle stays hidden on desktop/tablet');
            await page.evaluate(() => scrollTo(0, 0)); await screenshot('orders-' + width);
            if (width === 390) {
                await page.locator('#btnToggleFilters').click();
                assert.equal(await page.locator('#fromDate').isVisible(), true);
                await page.locator('#btnToggleFilters').click();
                assert.equal(await page.locator('#fromDate').isVisible(), false);
                await page.locator('.po-order-row').first().scrollIntoViewIfNeeded(); await screenshot('orders-cards-390');
            }
        }
        console.log('PASS list totals, all status variants and seven responsive widths.');
        await page.setViewportSize({ width: 1440, height: 900 });
        for (const [id, value] of [['employee', 'Ngọc Hà'], ['customer', '0901234567'], ['terminal', 'POS01']]) await page.locator('#' + id).fill(value);
        await page.locator('#settlement').selectOption('Mixed');
        await page.locator('#btnSearch').click(); await loaded();
        assert.equal(requests.at(-1).employee, 'Ngọc Hà'); assert.equal(requests.at(-1).customer, '0901234567');
        assert.equal(requests.at(-1).terminal, 'POS01'); assert.equal(requests.at(-1).settlement, 'Mixed');
        assert.match(await page.locator('#filterSummary').innerText(), /Nhân viên: Ngọc Hà/);
        await page.locator('#btnNextPage').click(); await loaded();
        assert.equal(requests.at(-1).employee, 'Ngọc Hà'); assert.equal(requests.at(-1).settlement, 'Mixed');
        await page.reload(); await loaded();
        assert.equal(await page.locator('#employee').inputValue(), 'Ngọc Hà');
        assert.equal(await page.locator('#customer').inputValue(), '0901234567');
        assert.equal(await page.locator('#terminal').inputValue(), 'POS01');
        assert.equal(await page.locator('#settlement').inputValue(), 'Mixed');
        await page.locator('#btnResetFilters').click(); await loaded();
        for (const id of ['employee', 'customer', 'terminal', 'settlement']) assert.equal(await page.locator('#' + id).inputValue(), '');
        console.log('PASS new filter submission, page retention, URL restore and reset; identities and mixed/credit payment badges.');
        await page.locator('#employee').fill('nguyen');
        await page.locator('#employeeOptions [role=option]').first().waitFor();
        await screenshot('employee-autocomplete');
        const beforeSelection = requests.length;
        await page.locator('#employee').press('ArrowDown'); await page.locator('#employee').press('Enter');
        assert.equal(await page.locator('#employeeId').inputValue(), '502');
        assert.equal(requests.length, beforeSelection, 'Enter selects a suggestion without submitting the form');
        assert.match(await page.locator('#employee').inputValue(), /ha02/);
        await page.locator('#terminal').fill('quay');
        await page.locator('#terminalOptions [role=option]').nth(1).click();
        assert.equal(await page.locator('#terminalId').inputValue(), '602');
        await page.locator('#btnSearch').click(); await loaded();
        assert.equal(requests.at(-1).employeeId, '502'); assert.equal(requests.at(-1).terminalId, '602');
        await page.reload(); await loaded();
        assert.equal(await page.locator('#employeeId').inputValue(), '502');
        assert.equal(await page.locator('#terminalId').inputValue(), '602');
        await page.locator('#employee').fill('zzz');
        assert.equal(await page.locator('#employeeId').inputValue(), '');
        await page.waitForFunction(() => document.querySelector('[data-order-lookup=employee] [role=status]').textContent.includes('Không có kết quả'));
        await page.locator('#employee').press('Escape');
        assert.equal(await page.locator('#employee').getAttribute('aria-expanded'), 'false');
        const slowRequest = page.waitForRequest(r => r.url().includes('filter-options') && r.url().includes('term=slow'));
        await page.locator('#employee').fill('slow'); await slowRequest;
        await page.locator('#employee').fill('nguyen');
        await page.locator('#employeeOptions [role=option]').first().waitFor(); await page.waitForTimeout(750);
        assert.doesNotMatch(await page.locator('#employeeOptions').innerText(), /Old response/);
        await page.locator('#employee').press('Enter');
        await page.locator('#btnResetFilters').click(); await loaded();
        assert.equal(await page.locator('#employeeId').inputValue(), ''); assert.equal(await page.locator('#terminalId').inputValue(), '');
        assert.equal(new URL(page.url()).searchParams.has('employeeId'), false);
        console.log('PASS autocomplete mouse/keyboard selection, duplicate names by ID, URL restore, clearing, empty result, Escape and stale lookup cancellation.');
        const beforeRowPreview = receiptRequests.length;
        await page.locator('[data-order-id="105"] td:nth-child(3)').dblclick();
        await page.locator('#previewBody .po-preview-lines').first().waitFor();
        assert.equal(receiptRequests.length, beforeRowPreview + 1, 'Double-clicking row content requests its receipt once');
        assert.match(await page.locator('#previewBody').innerText(), /Nguyễn Minh Anh/);
        assert.match(await page.locator('#previewBody').innerText(), /Sữa tươi TH/);
        assert.equal(await page.locator('#previewDetailLink').getAttribute('href'), '/admin/pos/order-detail/105');
        await screenshot('preview-1440');
        await page.setViewportSize({ width: 390, height: 844 });
        await screenshot('preview-390');
        const footer = await page.locator('.po-drawer-footer').boundingBox();
        assert.ok(footer.x >= 0 && footer.x + footer.width <= 391 && footer.y + footer.height <= 845);
        await page.keyboard.press('Escape');
        assert.equal(await page.locator('#orderPreview').evaluate(node => node.open), false);
        await page.waitForFunction(() => document.activeElement.dataset.previewId === '105');
        assert.equal(await page.evaluate(() => document.activeElement.dataset.previewId), '105');
        await page.setViewportSize({ width: 1440, height: 900 });
        const beforeCodePreview = receiptRequests.length;
        await page.locator('#ordersBody [data-preview-id="105"]').dblclick();
        await page.locator('#previewBody .po-preview-lines').first().waitFor();
        assert.equal(await page.locator('#orderPreview').evaluate(node => node.open), true, 'Double-click on the code must not immediately dismiss the preview');
        assert.equal(receiptRequests.length, beforeCodePreview + 1, 'The existing code button still requests one receipt');
        await page.keyboard.press('Escape');
        receiptError = true; await page.locator('[data-preview-id="105"]').click();
        await page.locator('[data-retry-preview]').waitFor();
        assert.match(await page.locator('#previewBody').innerText(), /chưa có quyền/);
        receiptError = false; await page.locator('[data-retry-preview]').click();
        await page.locator('#previewBody .po-preview-lines').first().waitFor(); await page.locator('#btnClosePreview').click();
        receiptDelay = true;
        await page.locator('#ordersBody [data-preview-id="105"]').click();
        await page.keyboard.press('Escape');
        await page.locator('#ordersBody [data-preview-id="104"]').click();
        await page.locator('#previewBody .po-preview-lines').first().waitFor();
        await page.waitForTimeout(750);
        assert.equal(await page.locator('#previewTitle').innerText(), 'POS-20260910-00002', 'Closed preview must not replace a newer order');
        await page.locator('#btnClosePreview').click(); receiptDelay = false;
        await page.locator('[data-menu-id="105"]').click();
        assert.equal(await page.evaluate(() => document.activeElement.dataset.previewId), '105');
        await page.keyboard.press('ArrowDown');
        assert.equal(await page.evaluate(() => document.activeElement.getAttribute('href')), '/admin/pos/order-detail/105');
        assert.equal(await page.locator('[data-refund-order-id="105"]').getAttribute('href'), '/admin/pos/order-detail/105');
        assert.equal(await page.locator('[data-void-order-id="105"]').getAttribute('href'), '/admin/pos/order-detail/105');
        await screenshot('actions-1440');
        await page.route('**/admin/acb/payments/orders/105?embedded=true', route => route.fulfill({ contentType: 'text/html', body: '<h1>ACB fixture</h1>' }));
        await page.locator('[data-acb-order-id="105"]').click();
        await page.locator('.acb-dialog[open]').waitFor();
        assert.match(await page.locator('.acb-dialog iframe').getAttribute('src'), /105\?embedded=true/);
        await page.keyboard.press('Escape');
        assert.equal(await page.evaluate(() => document.activeElement.dataset.menuId), '105');
        await page.locator('[data-menu-id="104"]').click();
        assert.equal(await page.locator('[data-void-order-id]').count(), 0, 'Expired void action stays unavailable');
        await page.keyboard.press('Escape');
        await page.evaluate(() => { window.probePrintCalls = []; window.open = (...args) => { window.probePrintCalls.push(args); return null; }; });
        const beforePrint = receiptRequests.length;
        await page.locator('#ordersBody [data-print-order-id="105"]').dblclick();
        const calls = await page.evaluate(() => window.probePrintCalls);
        assert.deepEqual(calls, [['/admin/pos/orders/105/print?autoPrint=false', '_blank', 'noopener']]);
        assert.equal(receiptRequests.length, beforePrint, 'Double-clicking print must not open a row preview');
        assert.equal(await page.locator('#orderPreview').evaluate(node => node.open), false);
        await page.locator('#ordersBody [data-menu-id="105"]').dispatchEvent('dblclick', { button: 0 });
        assert.equal(await page.locator('#orderPreview').evaluate(node => node.open), false, 'Double-click on an action control is excluded');
        console.log('PASS row double-click, one receipt request, code-button double-click, print/action exclusion and restored focus.');
        console.log('PASS preview fields, responsive drawer, Escape/focus, denied/retry, original ACB dialog and print/refund/void destinations.');
        listDelay = true;
        await page.locator('#btnNextPage').click();
        assert.equal(await page.locator('.po-table').getAttribute('aria-busy'), 'true');
        assert.equal(await page.locator('.po-order-row').count(), 8, 'Keep the last page readable while fetching the next');
        assert.equal(await page.locator('.po-skeleton-row').count(), 0);
        assert.match(await page.locator('#pageIndicator').innerText(), /Trang 1/);
        assert.equal(await page.locator('#metricCount').innerText(), '28');
        await loaded(); listDelay = false;
        assert.match(await page.locator('#ordersBody').innerText(), /POS-TRANG-2/);
        assert.match(page.url(), /page=2/);
        await page.locator('#btnPrevPage').click(); await loaded();
        await page.locator('#pageSize').selectOption('10'); await loaded();
        assert.equal(requests.at(-1).pageSize, '10');
        await page.locator('#status').selectOption('Completed'); await page.locator('#btnSearch').click(); await loaded();
        assert.equal(await page.locator('.po-order-row').count(), 3);
        assert.equal(requests.at(-1).status, 'Completed');
        await page.locator('[data-range="7days"]').click(); await loaded();
        const dateValues = requests.at(-1);
        assert.equal(dateValues.status, 'Completed');
        assert.equal((new Date(dateValues.toDate) - new Date(dateValues.fromDate)) / 86400000, 6);
        assert.equal(await page.locator('[data-range="7days"]').getAttribute('aria-pressed'), 'true');
        await page.reload(); await loaded();
        assert.equal(await page.locator('#status').inputValue(), 'Completed');
        assert.equal(await page.locator('#fromDate').inputValue(), dateValues.fromDate);
        const beforeInvalid = requests.length;
        await page.locator('#fromDate').fill('2026-09-20'); await page.locator('#toDate').fill('2026-09-01');
        await page.locator('#btnSearch').click();
        assert.match(await page.locator('#toDate').evaluate(node => node.validationMessage), /Đến ngày/);
        assert.equal(requests.length, beforeInvalid);
        await page.locator('#btnResetFilters').click(); await loaded();
        assert.equal(await page.locator('#status').inputValue(), '');
        assert.equal(await page.locator('#fromDate').inputValue(), '');
        await page.locator('#keyword').fill('safe'); await page.locator('#keyword').press('Enter'); await loaded();
        assert.match(await page.locator('#ordersBody').innerText(), /<img src=x/);
        assert.equal(await page.locator('#ordersBody img, #ordersBody script').count(), 0);
        race = true;
        await page.locator('#keyword').fill('slow'); await page.locator('#keyword').press('Enter');
        await page.waitForFunction(() => document.querySelector('.po-table').getAttribute('aria-busy') === 'true');
        await page.locator('#keyword').fill('newest'); await page.locator('#keyword').press('Enter'); await loaded();
        await page.waitForTimeout(850);
        assert.match(await page.locator('#ordersBody').innerText(), /newest/);
        assert.doesNotMatch(await page.locator('#ordersBody').innerText(), /slow/);
        race = false;
        await page.locator('#btnResetFilters').click(); await loaded();
        empty = true; await refresh();
        assert.match(await page.locator('#ordersBody').innerText(), /Không tìm thấy/);
        await screenshot('empty-1440');
        empty = false; listError = true; await refresh();
        assert.match(await page.locator('#ordersBody').innerText(), /Máy chủ tạm thời/);
        assert.equal(await page.locator('#metricTotal').innerText(), '—');
        assert.equal(await page.locator('#btnNextPage').isDisabled(), true);
        await screenshot('error-1440');
        listError = false; await page.locator('[data-retry-orders]').click(); await loaded();
        assert.equal(await page.locator('.po-order-row').count(), 8);
        await page.goto(info.baseUrl + '/admin/pos/orders-page?page=999&pageSize=10'); await loaded();
        assert.match(page.url(), /page=3&/);
        assert.equal(requests.at(-1).page, '3', 'Out-of-range bookmark recovers to last page');
        assert.deepEqual(commands, [], 'List/preview must not send financial commands');
        assert.deepEqual(errors, []);
        console.log('PASS pagination/filter/date validation/reset/URL restore, escaped content, stale response cancellation, empty/error/retry; zero financial commands or page errors.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
