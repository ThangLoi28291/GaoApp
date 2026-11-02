'use strict';
// UI-18 uses only owned, authenticated native reads and local dialog drafts/cancel paths.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');
const mobile = require('./reports-mobile-review.cjs');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let input = ''; process.stdin.on('data', value => input += value); process.stdin.on('end', () => resolve(input));
    }));
    assert.ok(/^\d{4}-\d{2}-\d{2}$/.test(info.day), 'Owned fixture day required');
    assert.ok(info.baseUrl && info.output && info.manager && info.reader && info.salesViewer, 'Owned fixture accounts required');
    fs.mkdirSync(info.output, { recursive: true });
    const origin = new URL(info.baseUrl).origin;
    const preflight = process.argv.includes('--diagnostic-native-preflight');
    const salesFallbackOnly = process.argv.includes('--sales-fallback-contrast-only');
    assert.equal(preflight && salesFallbackOnly, false, 'Diagnostic and focused acceptance modes are mutually exclusive');
    const mode = salesFallbackOnly ? 'sales_fallback_contrast_acceptance' : preflight ? 'diagnostic_native_preflight' : 'acceptance';
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const errors = [], unexpectedWrites = [], externalRequests = [], requests = [], exports = [], permissionChecks = [];
    const observations = [], reviewedStates = [], probedStates = [], stateProvenance = [], responseReads = [], pendingResponses = [], scopeProofs = [], dateTimeProofs = [];
    const managerRoutes = ['overview', 'profit', 'products', 'categories', 'groups', 'customers', 'expenses'];
    const salesSegments = ['overview', 'orders', 'products', 'discounts', 'returns', 'void'];
    const operations = ['cashflow', 'inventory', 'debts'];
    const expectedLabels = salesFallbackOnly ? ['sales-viewer-native-overview'] : [
        ...managerRoutes.map(section => 'manager-' + section),
        'manager-products-empty-filter',
        ...salesSegments.map(segment => 'manager-sales-' + segment),
        'manager-profit-detail', 'manager-cost-activity',
        ...operations.map(section => 'manager-' + section),
        'manager-cashflow-cash-fund', 'manager-inventory-threshold', 'manager-debts-overdue',
        'reader-profit', 'reader-profit-detail', ...operations.map(section => 'reader-' + section),
        'sales-viewer-native-overview',
        'expense-new-draft-cancel', 'expense-edit-draft-cancel', 'expense-confirm-draft-cancel', 'expense-void-posted-cancel',
        'treasury-out-draft-cancel', 'treasury-transfer-draft-cancel', 'treasury-expense-draft-cancel',
        'treasury-payable-draft-cancel', 'treasury-opening-draft-cancel', 'treasury-reverse-draft-cancel', 'treasury-due-draft-cancel'
    ];
    const roots = {
        management: '[data-management-report]:not([data-operations-report])',
        sales: '[data-sales-detail-report]',
        profit: '[data-profit-report]',
        operations: '[data-operations-report]',
        salesOverview: '[data-sales-executive-report]'
    };
    const period = new URLSearchParams({ fromDate: info.day, toDate: info.day, compare: 'none', customerState: 'all' });
    let page, failed = null;

    const ownAddress = address => address.origin === origin;
    const isHeartbeat = (method, pathname) => method === 'POST' && /^\/admin\/api\/store-activity\/(?:online|presence)$/.test(pathname);
    const isRead = method => ['GET', 'HEAD', 'OPTIONS'].includes(method);
    const dataRequest = request => /\/admin\/reports\/(?:management\/data|expenses\/data|operations\/(?:cashflow|inventory|debts)\/data|profit\/data|cost-adjustments\/data|overview\/data|sales\/(?:context|orders|products|discounts|returns|voids))$/.test(request.pathname);
    function reportRoute(section, extra = {}) {
        const query = new URLSearchParams(period);
        Object.entries(extra).forEach(([key, value]) => query.set(key, String(value)));
        return '/admin/reports/' + section + '?' + query;
    }
    async function noCanary(label) {
        if (info.canary) assert.equal((await page.content()).includes(info.canary), false, label + ' stored credential/configuration canary');
    }
    async function loaded(root) {
        await page.locator(root).first().waitFor();
        await page.waitForFunction(selector => document.querySelector(selector)?.getAttribute('aria-busy') === 'false', root);
        // A native Sales segment-tab load does not toggle its page-level aria-busy.
        // Observe the original loading flag rather than treating its in-flight table as empty.
        await page.waitForFunction(selector => {
            const loading = document.querySelector(selector)?.querySelector('#salesDetailLoading');
            return !loading || loading.hidden;
        }, root);
        for (const selector of ['[data-report-error]', '[data-profit-error]', '[data-error]']) {
            if (await page.locator(root).locator(selector).count()) {
                assert.equal(await page.locator(root).locator(selector).isVisible(), false, await page.locator(root).locator(selector).textContent());
            }
        }
        for (const selector of ['#salesDetailStateTitle', '#salesReportStateTitle']) {
            const title = page.locator(root).locator(selector);
            if (await title.count() && await title.isVisible()) {
                assert.doesNotMatch(await title.textContent(), /^Không tải/, 'Native sales error must not count as valid empty data');
            }
        }
        await mobile.settle(page);
    }
    async function visit(route, root) {
        await Promise.all(pendingResponses);
        const response = await page.goto(info.baseUrl + route);
        assert.equal(response.status(), 200, route);
        await loaded(root);
        await noCanary(route);
    }
    async function review(label, root, category, provenance = 'Native authenticated Razor, original report JS and real isolated SQL fixture reads') {
        await noCanary(label);
        const before = requests.length;
        if (preflight) await mobile.settle(page);
        else await mobile.review(page, label, info.output, { root });
        const matrixDataRequests = requests.slice(before).filter(dataRequest);
        assert.deepEqual(matrixDataRequests, [], label + ' viewport/print presentation must not fetch report data');
        probedStates.push(label);
        if (!preflight) reviewedStates.push(label);
        stateProvenance.push({ label, category, provenance, controlledTransport: false, syntheticMetrics: false, mandatoryMatrixReviewed: !preflight });
        observations.push(await page.locator(root).evaluate((node, label) => ({
            label, url: location.pathname + location.search, kind: node.getAttribute('data-section') || node.getAttribute('data-mode') || '',
            nativeTables: node.querySelectorAll('table').length,
            nativeRows: node.querySelectorAll('tbody tr').length,
            actualApexCharts: node.querySelectorAll('.apexcharts-svg').length,
            nativeDialogsOpen: [...node.querySelectorAll('dialog[open]')].map(dialog => dialog.getAttribute('aria-labelledby')),
            quality: [...node.querySelectorAll('[data-report-quality],[data-quality],[data-profit-quality]')].map(item => item.textContent.trim()),
            nativeHeaders: [...node.querySelectorAll('table')].map(table => [...table.querySelectorAll('thead th')].map(cell => cell.textContent.trim()))
        }), label));
        await noCanary(label);
    }
    async function reviewDialog(selector, label, root, category) {
        await waitNativeDialog(selector);
        const controls = await page.locator(selector).evaluate(node => [...node.querySelectorAll('input,select,textarea')].map(field => ({
            name: field.name, type: field.type, value: field.value, required: field.required, readOnly: field.readOnly, disabled: field.disabled,
            nativeForm: field.form === node.querySelector('form'), hidden: !!field.closest('[hidden],.d-none')
        })));
        await review(label, root, category, 'Original native dialog opened by its real native button; local fields filled, original cancel closes; no command submitted');
        if (!preflight) await mobile.dialog(page, selector, label, info.output);
        const currentControls = await page.locator(selector).evaluate(node => [...node.querySelectorAll('input,select,textarea')].map(field => ({
            name: field.name, type: field.type, value: field.value, required: field.required, readOnly: field.readOnly, disabled: field.disabled,
            nativeForm: field.form === node.querySelector('form'), hidden: !!field.closest('[hidden],.d-none')
        })));
        assert.deepEqual(currentControls, controls, label + ' original native dialog fields preserved');
        observations.push({ label, nativeDialogControls: controls, cancelOnly: true });
    }
    async function closeDialog(selector, button) {
        await page.locator(selector).locator(button).first().click();
        await page.waitForFunction(selector => !document.querySelector(selector).open, selector);
    }
    async function waitNativeDialog(selector) {
        // Treasury opens only after its original sources GET and form setup finish.
        // Reading a field from the still-closed dialog can observe the previous mode.
        await page.locator(selector).waitFor({ state: 'visible' });
        assert.equal(await page.locator(selector).evaluate(dialog => dialog.open), true, selector + ' native dialog is open before inspecting fields');
    }
    function quotedCsv(text) {
        const rows = []; let index = text.charCodeAt(0) === 0xfeff ? 1 : 0;
        while (index < text.length) {
            const row = [];
            while (index < text.length) {
                assert.equal(text[index++], '"', 'Native CSV cells remain quoted');
                let cell = '', closed = false;
                while (index < text.length) {
                    if (text[index] !== '"') { cell += text[index++]; continue; }
                    if (text[index + 1] === '"') { cell += '"'; index += 2; continue; }
                    index++; closed = true; break;
                }
                assert.ok(closed, 'Native CSV quoted cell terminates'); row.push(cell);
                if (text[index] === ',') { index++; continue; }
                if (text[index] === '\r') { assert.equal(text[index + 1], '\n'); index += 2; }
                else if (text[index] === '\n') index++;
                else assert.equal(index, text.length, 'Native CSV row delimiter');
                break;
            }
            rows.push(row);
        }
        return rows;
    }
    const csvNumber = value => ({ sourceNumber: value });
    function csvText(value) {
        const text = String(value ?? '');
        return /^[=+\-@]/.test(text.trimStart()) && !/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(text) ? "'" + text : text;
    }
    function csvSourceRows(address, source) {
        const periodRows = address.pathname.includes('/management/')
            ? [['Kỳ báo cáo', info.day, info.day]] : [['Từ ngày', info.day, 'Đến ngày', info.day]];
        if (address.pathname.includes('/management/')) {
            const current = source.current, dimension = address.searchParams.get('dimension');
            if (dimension === 'profit') {
                const summary = current.summary, quality = ['Empty', 'Finalized', 'Provisional', 'Unavailable', 'DataIntegrityConflict'];
                return [...periodRows, ['Chỉ tiêu', 'Số tiền VND', 'Trạng thái'],
                    ['Doanh thu thuần', csvNumber(summary.netSales.value), typeof summary.netSales.quality === 'number' ? quality[summary.netSales.quality] : summary.netSales.quality],
                    ['Giá vốn', csvNumber(summary.cogs.value), summary.costState],
                    ['Lợi nhuận gộp', csvNumber(summary.grossProfit.value), summary.costState],
                    ['Chi phí vận hành toàn cửa hàng', csvNumber(current.operatingExpenses), 'Đã ghi nhận'],
                    ['LN sau chi phí ghi nhận', csvNumber(current.operatingProfit), summary.costState]];
            }
            const values = { products: current.products, categories: current.categories, customers: current.customers, groups: current.customerGroups }[dimension];
            assert.ok(values, 'Known native management CSV dimension');
            return [...periodRows, ['Tên', 'Nhóm', 'SKU', 'Đơn bán', ...(dimension === 'products' ? ['SL đơn vị gốc'] : []), 'Doanh thu thuần', 'Giá vốn', 'LN gộp', 'Biên LN %', 'Trạng thái giá vốn'],
                ...values.map(value => [csvText(value.name), csvText(value.group), csvText(value.sku), csvNumber(value.salesOrders), ...(dimension === 'products' ? [csvNumber(value.baseQuantity)] : []),
                    csvNumber(value.summary.netSales.value), csvNumber(value.summary.cogs.value), csvNumber(value.summary.grossProfit.value), csvNumber(value.summary.grossMargin.value), csvText(value.summary.costState)])];
        }
        const warnings = source.warnings.map(warning => ['Lưu ý', csvText(warning)]);
        if (address.pathname.includes('/cashflow/')) {
            const localMinute = at => {
                const parts = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
                    .formatToParts(new Date(/(?:Z|[+-]\d{2}:?\d{2})$/i.test(at) ? at : at + 'Z'));
                const value = key => parts.find(part => part.type === key).value;
                return [value('year'), value('month'), value('day')].join('-') + ' ' + [value('hour'), value('minute')].join(':');
            };
            return [...periodRows, ...warnings, ['Quỹ', 'Đầu kỳ', 'Thu', 'Chi', 'Thuần', 'Cuối kỳ'],
                ...source.summary.map(value => [csvText(value.name), csvNumber(value.opening), csvNumber(value.inflow), csvNumber(value.outflow), csvNumber(value.net), csvNumber(value.closing)]),
                ['Mã', 'Thời điểm UTC+7', 'Quỹ', 'Nội dung', 'Nguồn', 'Số tiền có dấu', 'Tham chiếu', 'Chuyển quỹ'],
                ...source.items.map(value => [csvText(value.id), localMinute(value.atUtc), csvText(value.fund), csvText(value.name), csvText(value.source), csvNumber(value.amount), csvText(value.reference), value.isTransfer ? 'Có' : ''])];
        }
        if (address.pathname.includes('/inventory/')) {
            return [...periodRows, ...warnings, ['Kho', 'Sản phẩm', 'SKU', 'Đơn vị gốc', 'Đầu kỳ', 'Nhập', 'Xuất', 'Cuối kỳ', 'Giá trị cuối kỳ', 'Xuất bán', 'Số ngày đủ bán', 'Trạng thái', 'Giá vốn tạm', 'Thiếu dữ liệu'],
                ...source.items.map(value => [csvText(value.warehouse), csvText(value.name), csvText(value.sku), csvText(value.unit), csvNumber(value.opening), csvNumber(value.inbound), csvNumber(value.outbound), csvNumber(value.closing),
                    csvNumber(value.closingValue), csvNumber(value.sold), csvNumber(value.daysCover), csvText(value.state), value.isProvisional ? 'Có' : '', value.isIncomplete ? 'Có' : ''])];
        }
        assert.ok(address.pathname.includes('/debts/'), 'Known native operations CSV route');
        return [...periodRows, ...warnings, ['Loại', 'Đối tượng', 'Chứng từ', 'Hạn thanh toán', 'Đầu kỳ', 'Tăng nợ', 'Giảm nợ', 'Cuối kỳ', 'Tuổi nợ'],
            ...source.items.map(value => [value.kind === 'receivable' ? 'Phải thu' : 'Phải trả', csvText(value.party), csvText(value.document), value.dueDate?.slice(0, 10) || '',
                csvNumber(value.opening), csvNumber(value.increase), csvNumber(value.decrease), csvNumber(value.closing), csvText(value.aging)])];
    }
    async function getExport(href, label, expected) {
        const address = new URL(href, info.baseUrl);
        assert.equal(address.origin, origin);
        assert.equal(address.searchParams.get('fromDate'), info.day, label + ' export from');
        assert.equal(address.searchParams.get('toDate'), info.day, label + ' export to');
        for (const [key, value] of Object.entries(expected || {})) assert.equal(address.searchParams.get(key), value, label + ' export scope ' + key);
        const response = await page.request.get(address.href, { headers: { Accept: 'text/csv' } });
        assert.equal(response.status(), 200, label + ' actual native CSV export');
        const body = await response.body();
        assert.match(response.headers()['content-type'], /text\/csv/i);
        const rows = quotedCsv(body.toString('utf8'));
        const sourceAddress = new URL(address); sourceAddress.pathname = sourceAddress.pathname.replace(/\/export$/, '/data');
        sourceAddress.searchParams.delete('dimension');
        const sourceResponse = await page.request.get(sourceAddress.href, { headers: { Accept: 'application/json' } });
        assert.equal(sourceResponse.status(), 200, label + ' same-scope original CSV source');
        const source = await sourceResponse.json(), expectedRows = csvSourceRows(address, source);
        assert.deepEqual(rows[0], expectedRows[0], label + ' native CSV quoted from/to period');
        assert.equal(rows.length, expectedRows.length, label + ' CSV retains all native source rows');
        let numericCells = 0, nullCells = 0;
        rows.forEach((row, rowIndex) => {
            assert.equal(row.length, expectedRows[rowIndex].length, label + ' native CSV columns, row ' + rowIndex);
            row.forEach((cell, column) => {
                const expectedCell = expectedRows[rowIndex][column];
                if (expectedCell && typeof expectedCell === 'object') {
                    if (expectedCell.sourceNumber == null) { assert.equal(cell, ''); nullCells++; }
                    else { assert.match(cell, /^-?\d+(?:\.\d+)?$/); assert.equal(Number(cell), expectedCell.sourceNumber, label + ' unmodified native numeric source at ' + rowIndex + ':' + column); numericCells++; }
                } else assert.equal(cell, expectedCell, label + ' unmodified native text source at ' + rowIndex + ':' + column);
            });
        });
        const file = label + '-' + (address.searchParams.get('dimension') || 'operations') + '.csv';
        fs.writeFileSync(path.join(info.output, file), body);
        exports.push({ label, pathname: address.pathname, query: Object.fromEntries(address.searchParams), status: response.status(), bytes: body.length, sha256: crypto.createHash('sha256').update(body).digest('hex'),
            file, sourcePath: sourceAddress.pathname, sourceQuery: Object.fromEntries(sourceAddress.searchParams), rows: rows.length, numericCellsComparedToNativeSource: numericCells, nullCellsComparedToNativeSource: nullCells, allColumnsComparedToNativeSource: true });
    }
    async function exportVisible(label, expected) {
        const anchors = await page.locator('[data-export]').evaluateAll(nodes => nodes.map(node => node.href));
        assert.ok(anchors.length > 0, label + ' native export action');
        for (const href of anchors) await getExport(href, label, expected);
    }
    async function forbiddenGet(pathname, label) {
        const response = await page.request.get(info.baseUrl + pathname, { headers: { Accept: 'application/json' }, maxRedirects: 0 });
        assert.equal(response.status(), 403, label + ' server enforces permission');
        permissionChecks.push({ label, pathname, status: response.status() });
    }
    async function login(account, role) {
        const context = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce', serviceWorkers: 'block' });
        await mobile.protect(context, info.baseUrl);
        const current = await context.newPage(); current.setDefaultTimeout(30000);
        current.on('pageerror', error => errors.push({ role, message: error.message }));
        current.on('dialog', async dialog => { errors.push({ role, message: 'Unexpected native browser dialog: ' + dialog.type() }); await dialog.dismiss(); });
        current.on('request', request => {
            const address = new URL(request.url());
            if (!ownAddress(address) && !['data:', 'blob:', 'about:'].includes(address.protocol)) externalRequests.push({ role, method: request.method(), origin: address.origin });
            if (ownAddress(address)) requests.push({ role, method: request.method(), pathname: address.pathname, query: Object.fromEntries(address.searchParams) });
        });
        current.on('response', response => {
            const address = new URL(response.url());
            if (!ownAddress(address) || response.status() !== 200 || !/application\/json/i.test(response.headers()['content-type'] || '') || !dataRequest({ pathname: address.pathname })) return;
            pendingResponses.push(response.json().then(body => {
                responseReads.push({ role, pathname: address.pathname, query: Object.fromEntries(address.searchParams), body });
            }).catch(error => { if (!/Target.*closed|No resource|No data|cancel|aborted/i.test(String(error))) errors.push({ role, message: 'Native JSON capture: ' + error }); }));
        });
        await current.goto(info.baseUrl + '/admin/account/login?returnUrl=%2Fadmin%2Freports%2Fprofit');
        await current.locator('[name=UserName]').fill(account.user);
        await current.locator('[name=Password]').fill(account.password);
        if (await current.locator('[name=SelectedTerminalId]').count()) await current.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([current.waitForURL(url => !url.pathname.toLowerCase().endsWith('/login')), current.locator('#loginForm button[type=submit]').click()]);
        // Authentication is complete. No business POST is permitted for any later probe or native event.
        await context.route('**/*', async route => {
            const request = route.request(), address = new URL(request.url());
            if (!ownAddress(address)) return route.fallback();
            if (isRead(request.method()) || isHeartbeat(request.method(), address.pathname)) return route.fallback();
            unexpectedWrites.push({ role, method: request.method(), pathname: address.pathname });
            return route.abort('blockedbyclient');
        });
        return { context, page: current };
    }
    async function proveSalesLocalDateTimes(segment) {
        await Promise.all(pendingResponses);
        const endpoint = { orders: '/admin/reports/sales/orders', discounts: '/admin/reports/sales/discounts', returns: '/admin/reports/sales/returns', void: '/admin/reports/sales/voids' }[segment];
        const source = responseReads.findLast(read => read.role === 'manager' && read.pathname === endpoint && read.query.fromDate === info.day && read.query.toDate === info.day);
        assert.ok(source, segment + ' original native SQL response captured before datetime inspection');
        const items = source.body.items;
        assert.ok(Array.isArray(items), segment + ' native response items');
        const cells = await page.locator('#salesDetailTableBody > tr').evaluateAll(rows => rows.map(row => [...row.cells].map(cell => cell.textContent.trim())));
        assert.equal(cells.length, items.length, segment + ' original rendered rows correspond to native source');
        const clock = new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', dateStyle: 'short', timeStyle: 'short' });
        const fromSource = (value, local) => {
            if (!value) return '—';
            const offset = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value);
            const instant = new Date(offset ? value : value + (local ? '+07:00' : 'Z'));
            assert.ok(Number.isFinite(instant.getTime()), segment + ' valid native datetime source');
            return clock.format(instant);
        };
        const comparisons = items.map((item, index) => ({
            index, record: item.orderNumber || item.returnNumber || String(item.orderId || ''),
            completedAtUtc: item.completedAtUtc, completedAtLocal: item.completedAtLocal,
            expectedLocalText: fromSource(item.completedAtLocal, true),
            corroboratingUtcText: fromSource(item.completedAtUtc, false), actualNativeCellText: cells[index][1]
        }));
        const mismatches = comparisons.filter(row => row.actualNativeCellText !== row.expectedLocalText || row.corroboratingUtcText !== row.expectedLocalText);
        dateTimeProofs.push({ segment, endpoint, query: source.query, sourceRows: items.length, domRows: cells.length,
            nativeEmpty: items.length === 0, provenance: 'Original authenticated SQL response and original rendered completion cell; no timestamps or financial fixtures synthesized',
            emptyCoverage: items.length === 0 ? 'Native endpoint has no rows in this real fixture; actual formatter/renderer branches are also covered by the source-executed Node unit suite' : null,
            comparisons, errors: mismatches });
        assert.deepEqual(mismatches, [], segment + ' local wall time displayed once and corroborated by original UTC instant');
    }
    function nativeManagementRead(overview) {
        // Login can leave the previous page's management read in flight. Match the
        // native view/period, then capture bytes now, before any later navigation.
        const capture = page.waitForResponse(response => {
            const address = new URL(response.url());
            const query = address.searchParams;
            return address.origin === origin && address.pathname === '/admin/reports/management/data' && response.request().method() === 'GET'
                && query.get('view') === (overview ? 'overview' : null)
                && query.get('fromDate') === info.day && query.get('toDate') === info.day
                && query.get('compare') === 'none' && query.get('customerState') === 'all';
        }).then(async response => {
            assert.equal(response.status(), 200, 'Native scoped management read succeeds');
            const bytes = await response.body();
            return { address: new URL(response.url()), body: JSON.parse(bytes.toString('utf8')) };
        }).then(read => ({ read }), error => ({ error }));
        // Attach the rejection handler immediately. The owning step explicitly
        // consumes and rethrows a failed capture; financial checks are never skipped.
        return { async take() { const result = await capture; if (result.error) throw result.error; return result.read; } };
    }
    async function assertNativeTerminalScope(label, read, overview) {
        const expected = String(info.terminalId), query = read.address.searchParams;
        assert.equal(query.get('terminalId'), expected, label + ' original data request retains terminal');
        assert.equal(query.get('fromDate'), info.day); assert.equal(query.get('toDate'), info.day);
        assert.equal(query.get('compare'), 'none'); assert.equal(query.get('customerState'), 'all');
        if (overview) assert.equal(query.get('view'), 'overview', label + ' native overview keeps light source');
        else assert.equal(query.get('view'), null, label + ' native dimension uses its complete source');
        assert.equal(read.body.current.summary.netSales.value, info.scopedTerminalNet, label + ' exact native terminal revenue');
        assert.notEqual(read.body.current.summary.netSales.value, info.allTerminalNet, label + ' terminal scope is distinguishable from all terminals');
        assert.equal(read.body.current.operatingProfit, null, label + ' store-wide expenses are not allocated to a terminal');
        assert.equal(await page.locator('[data-report-filters] [name=terminalId]').inputValue(), expected, label + ' original selected terminal control');
        assert.equal(new URL(page.url()).searchParams.get('terminalId'), expected, label + ' browser URL scope');
        scopeProofs.push({ label, request: read.address.pathname + read.address.search, actualNativeNetSales: read.body.current.summary.netSales.value, expectedNativeNetSales: info.scopedTerminalNet,
            allTerminalNetSales: info.allTerminalNet, selectedTerminal: expected, operatingProfit: read.body.current.operatingProfit });
    }

    try {
        if (!salesFallbackOnly) {
        const manager = await login(info.manager, 'manager'); page = manager.page;
        assert.ok(Number.isFinite(info.scopedTerminalNet) && Number.isFinite(info.allTerminalNet) && info.scopedTerminalNet !== info.allTerminalNet, 'Owned fixture supplies distinguishable native terminal/all-terminal totals');
        for (const section of managerRoutes) {
            const allTerminalRead = section === 'overview' ? nativeManagementRead(true) : null;
            await visit(reportRoute(section), roots.management);
            assert.equal(await page.locator(roots.management).getAttribute('data-section'), section);
            if (section === 'overview') {
                const nativeOverviewReads = requests.filter(request => request.role === 'manager' && request.pathname === '/admin/reports/management/data' && request.query.view === 'overview');
                assert.ok(nativeOverviewReads.length > 0, 'Native management overview uses view=overview');
                const native = await allTerminalRead.take();
                assert.equal(native.address.searchParams.get('terminalId'), null, 'All-terminal native overview has no terminal filter');
                assert.equal(native.body.current.summary.netSales.value, info.allTerminalNet, 'Exact native all-terminal total before scoped navigation');
            }
            await review('manager-' + section, roots.management, section === 'overview' ? 'D01' : section === 'expenses' ? 'D03' : 'D02');
            if (section === 'overview') await mobile.reportMenu(page, 'manager-overview-native-menu', info.output);
            if (section !== 'overview' && section !== 'expenses') await exportVisible('manager-' + section, { dimension: section });
        }

        await visit(reportRoute('products'), roots.management);
        await page.locator('[data-dimension-filter] [name=search]').fill('UI18_NO_MATCH_FIXED_QUERY');
        await page.locator('[data-dimension-filter] [type=submit]').click();
        await mobile.settle(page);
        assert.match(await page.locator('[data-report-panels]').textContent(), /Không có/);
        await review('manager-products-empty-filter', roots.management, 'D02');

        // Use the original report nav, then its original drill/back links to prove period scope survives.
        const scopedOverviewRead = nativeManagementRead(true);
        await visit(reportRoute('overview', { terminalId: info.terminalId }), roots.management);
        await assertNativeTerminalScope('before-native-products-navigation', await scopedOverviewRead.take(), true);
        assert.match(await page.locator('[data-report-quality]').textContent(), /Chi phí thuộc toàn cửa hàng/, 'Native terminal scope keeps whole-store expense warning');
        const originalNav = page.locator('[data-report-nav=products]');
        const productsHref = await originalNav.getAttribute('href');
        assert.equal(new URL(productsHref, info.baseUrl).searchParams.get('terminalId'), String(info.terminalId));
        await Promise.all(pendingResponses);
        const scopedProductsRead = nativeManagementRead(false);
        await Promise.all([page.waitForURL(url => url.pathname === '/admin/reports/products'), originalNav.click()]);
        await loaded(roots.management);
        await assertNativeTerminalScope('after-native-products-navigation', await scopedProductsRead.take(), false);
        assert.equal(new URL(page.url()).searchParams.get('fromDate'), info.day);
        const beforeBack = requests.length;
        await Promise.all(pendingResponses);
        await page.goBack(); await loaded(roots.management);
        assert.equal(new URL(page.url()).searchParams.get('terminalId'), String(info.terminalId));
        assert.equal(await page.locator('[data-report-filters] [name=terminalId]').inputValue(), String(info.terminalId), 'Back navigation restores native selected terminal');
        const nativeRevenueTitle = await page.locator('[data-report-kpis] .mr-kpi').first().locator('strong').getAttribute('title');
        assert.equal(nativeRevenueTitle, new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(info.scopedTerminalNet) + ' ₫', 'Back navigation retains the scoped native revenue display');
        const backReads = requests.slice(beforeBack).filter(dataRequest);
        assert.ok(backReads.every(request => request.query.terminalId === String(info.terminalId)), 'Any native back reload preserves terminal scope');
        scopeProofs.push({ label: 'after-native-back-navigation', selectedTerminal: String(info.terminalId), nativeRevenueTitle, nativeBackReloads: backReads });

        for (const segment of salesSegments) {
            await visit(reportRoute('sales', { segment }), roots.sales);
            assert.equal(new URL(page.url()).searchParams.get('segment'), segment);
            if (['orders', 'discounts', 'returns', 'void'].includes(segment)) await proveSalesLocalDateTimes(segment);
            await review('manager-sales-' + segment, roots.sales, 'D04');
        }
        // Click the native segment tab as well as reading routes directly.
        await page.locator('[data-sales-detail-segment=orders]').click();
        await loaded(roots.sales);
        assert.equal(new URL(page.url()).searchParams.get('segment'), 'orders');
        assert.equal(new URL(page.url()).searchParams.get('fromDate'), info.day);

        await visit(reportRoute('profit/detail'), roots.profit);
        await review('manager-profit-detail', roots.profit, 'D05');
        const activity = page.locator('[data-profit-activity]');
        const activityHref = new URL(await activity.getAttribute('href'), info.baseUrl);
        assert.equal(activityHref.searchParams.get('origin_fromDate'), info.day);
        await Promise.all(pendingResponses);
        await Promise.all([page.waitForURL(url => url.pathname === '/admin/reports/cost-adjustments'), activity.click()]);
        await loaded(roots.profit);
        await review('manager-cost-activity', roots.profit, 'D06');
        const backHref = new URL(await page.locator('[data-profit-back]').getAttribute('href'), info.baseUrl);
        assert.equal(backHref.searchParams.get('fromDate'), info.day);
        assert.equal(backHref.searchParams.get('toDate'), info.day);
        await Promise.all(pendingResponses);
        await Promise.all([page.waitForURL(url => url.pathname === '/admin/reports/profit'), page.locator('[data-profit-back]').click()]);
        await loaded(roots.management);

        for (const section of operations) {
            await visit(reportRoute(section), roots.operations);
            await review('manager-' + section, roots.operations, { cashflow: 'D07', inventory: 'D08', debts: 'D09' }[section]);
            await exportVisible('manager-' + section);
        }
        await visit(reportRoute('cashflow'), roots.operations);
        await page.locator('[data-filters] [name=fund]').selectOption('cash');
        await page.locator('[data-filters] [type=submit]').click(); await loaded(roots.operations);
        assert.equal(new URL(page.url()).searchParams.get('fund'), 'cash');
        await review('manager-cashflow-cash-fund', roots.operations, 'D07');
        await exportVisible('manager-cashflow-cash-fund', { fund: 'cash' });
        await visit(reportRoute('inventory'), roots.operations);
        const warehouse = await page.locator('[data-filters] [name=warehouseId] option').evaluateAll(options => options.find(option => option.value)?.value || '');
        assert.ok(warehouse, 'Owned stock fixture includes a native warehouse option');
        await page.locator('[data-filters] [name=warehouseId]').selectOption(warehouse);
        await page.locator('[data-filters] [name=lowStockThreshold]').fill('2');
        await page.locator('[data-filters] [type=submit]').click(); await loaded(roots.operations);
        assert.equal(new URL(page.url()).searchParams.get('lowStockThreshold'), '2');
        assert.equal(new URL(page.url()).searchParams.get('warehouseId'), warehouse);
        await review('manager-inventory-threshold', roots.operations, 'D08');
        await exportVisible('manager-inventory-threshold', { warehouseId: warehouse, lowStockThreshold: '2' });
        await visit(reportRoute('debts'), roots.operations);
        await page.locator('[data-state]').selectOption('overdue'); await mobile.settle(page);
        await review('manager-debts-overdue', roots.operations, 'D09');

        await visit(reportRoute('expenses'), roots.management);
        await page.locator('[data-expense-new]').click();
        await waitNativeDialog('[data-expense-dialog]');
        await page.locator('[data-expense-form] [name=name]').fill('Bản nháp cục bộ UI-18, sẽ đóng');
        await page.locator('[data-expense-form] [name=amount]').fill('123');
        await page.locator('[data-expense-form] [name=note]').fill('Chỉ kiểm tra giao diện và đóng, không lưu');
        await reviewDialog('[data-expense-dialog]', 'expense-new-draft-cancel', roots.management, 'D03');
        await closeDialog('[data-expense-dialog]', '[data-expense-close]');
        await page.locator('[data-expense-edit="' + info.expenseDraftId + '"]').click();
        await waitNativeDialog('[data-expense-dialog]');
        assert.equal(await page.locator('[data-expense-form] [name=id]').inputValue(), String(info.expenseDraftId));
        await reviewDialog('[data-expense-dialog]', 'expense-edit-draft-cancel', roots.management, 'D03');
        await closeDialog('[data-expense-dialog]', '[data-expense-close]');
        await page.locator('[data-expense-confirm="' + info.expenseDraftId + '"]').click();
        await waitNativeDialog('[data-expense-action-dialog]');
        assert.equal(await page.locator('[data-void-reason]').isVisible(), false);
        await reviewDialog('[data-expense-action-dialog]', 'expense-confirm-draft-cancel', roots.management, 'D03');
        await closeDialog('[data-expense-action-dialog]', '[data-action-close]');
        await page.locator('[data-expense-void="' + info.expensePostedId + '"]').click();
        await waitNativeDialog('[data-expense-action-dialog]');
        assert.equal(await page.locator('[data-expense-action-form] [name=reason]').getAttribute('required'), '');
        await page.locator('[data-expense-action-form] [name=reason]').fill('Lý do cục bộ, đóng mà không hủy');
        await reviewDialog('[data-expense-action-dialog]', 'expense-void-posted-cancel', roots.management, 'D03');
        await closeDialog('[data-expense-action-dialog]', '[data-action-close]');
        assert.equal(await page.locator('[data-expense-edit="' + info.expenseVoidedId + '"],[data-expense-void="' + info.expenseVoidedId + '"]').count(), 0, 'Voided expense has no edit/void controls');

        await visit(reportRoute('cashflow'), roots.operations);
        await page.locator('[data-money-new]').click();
        await waitNativeDialog('[data-money-dialog]');
        const money = page.locator('[data-money-form]');
        await money.locator('[name=name]').fill('Phiếu cục bộ UI-18, sẽ đóng');
        await money.locator('[name=amount]').fill('123');
        await reviewDialog('[data-money-dialog]', 'treasury-out-draft-cancel', roots.operations, 'D07');
        await closeDialog('[data-money-dialog]', '[data-close]');
        await page.locator('[data-money-new]').click();
        await waitNativeDialog('[data-money-dialog]');
        await money.locator('[name=mode]').selectOption('transfer');
        await money.locator('[name=name]').fill('Chuyển quỹ cục bộ, không lưu');
        await money.locator('[name=amount]').fill('123');
        assert.equal(await page.locator('[data-target-label]').isVisible(), true);
        assert.equal(await page.locator('[data-source-label]').isVisible(), false);
        await reviewDialog('[data-money-dialog]', 'treasury-transfer-draft-cancel', roots.operations, 'D07');
        await closeDialog('[data-money-dialog]', '[data-close]');
        await page.locator('[data-money-new]').click();
        await waitNativeDialog('[data-money-dialog]');
        await money.locator('[name=mode]').selectOption('expense');
        await money.locator('[name=source]').selectOption(String(info.expensePostedId));
        assert.equal(await money.locator('[name=amount]').evaluate(node => node.readOnly), true, 'Native expense source locks amount');
        await reviewDialog('[data-money-dialog]', 'treasury-expense-draft-cancel', roots.operations, 'D07');
        await closeDialog('[data-money-dialog]', '[data-close]');
        await page.locator('[data-opening]').click();
        await waitNativeDialog('[data-opening-dialog]');
        await page.locator('[data-opening-form] [name=amount]').fill('123');
        await page.locator('[data-opening-form] [name=note]').fill('Nháp cục bộ, không thay đổi số dư');
        await reviewDialog('[data-opening-dialog]', 'treasury-opening-draft-cancel', roots.operations, 'D07');
        await closeDialog('[data-opening-dialog]', '[data-close]');
        await page.locator('[data-reverse="' + info.cashEntryId + '"]').click();
        await waitNativeDialog('[data-action-dialog]');
        assert.equal(await page.locator('[data-action-form] [name=reason]').evaluate(node => node.required), true);
        await page.locator('[data-action-form] [name=reason]').fill('Nháp lý do cục bộ, không đảo phiếu');
        await reviewDialog('[data-action-dialog]', 'treasury-reverse-draft-cancel', roots.operations, 'D07');
        await closeDialog('[data-action-dialog]', '[data-close]');
        await visit(reportRoute('debts'), roots.operations);
        await page.locator('[data-pay="' + info.payableId + '"]').click();
        await waitNativeDialog('[data-money-dialog]');
        assert.equal(await money.locator('[name=mode]').inputValue(), 'payable');
        assert.equal(await money.locator('[name=source]').inputValue(), String(info.payableId));
        await reviewDialog('[data-money-dialog]', 'treasury-payable-draft-cancel', roots.operations, 'D09');
        await closeDialog('[data-money-dialog]', '[data-close]');
        await page.locator('[data-due="' + info.payableId + '"]').click();
        await waitNativeDialog('[data-action-dialog]');
        assert.equal(await page.locator('[data-action-reason-label]').isVisible(), false);
        assert.equal(await page.locator('[data-action-form] [name=date]').evaluate(node => node.required), false);
        await reviewDialog('[data-action-dialog]', 'treasury-due-draft-cancel', roots.operations, 'D09');
        await closeDialog('[data-action-dialog]', '[data-close]');
        await Promise.all(pendingResponses); await manager.context.close();

        const reader = await login(info.reader, 'reader'); page = reader.page;
        await visit(reportRoute('profit'), roots.management);
        assert.equal(await page.locator('[data-export],[data-expense-new],[data-report-nav=overview]').count(), 0, 'Profit-only reader has no export/manage/sales-only nav');
        await review('reader-profit', roots.management, 'D02');
        await forbiddenGet('/admin/reports/management/export?' + period + '&dimension=profit', 'reader-management-export');
        await visit(reportRoute('profit/detail'), roots.profit);
        assert.equal(await page.locator(roots.profit).getAttribute('data-can-sales'), 'false');
        await review('reader-profit-detail', roots.profit, 'D05');
        for (const section of operations) {
            await visit(reportRoute(section), roots.operations);
            assert.equal(await page.locator('[data-export],[data-money-new],[data-money-dialog],[data-opening],[data-due],[data-pay],[data-reverse]').count(), 0, 'Reader cannot get treasury controls or exports');
            assert.equal(await page.locator('.mr-nav a[href*="/admin/reports/overview"]').count(), 0, 'Profit reader has no Sales.View overview link');
            await review('reader-' + section, roots.operations, { cashflow: 'D07', inventory: 'D08', debts: 'D09' }[section]);
            await forbiddenGet('/admin/reports/operations/' + section + '/export?' + period, 'reader-' + section + '-export');
        }
        await forbiddenGet('/admin/reports/treasury/sources', 'reader-treasury-sources');
        await Promise.all(pendingResponses); await reader.context.close();
        }

        const salesViewer = await login(info.salesViewer, 'sales-viewer'); page = salesViewer.page;
        await visit(reportRoute('overview'), roots.salesOverview);
        assert.equal(await page.locator(roots.management).count(), 0, 'Sales-only overview retains native executive fallback');
        assert.equal(await page.locator('.sales-report-page-header [data-csp-action="report-profit"]').count(), 0, 'Sales-only header has no profit action');
        await review('sales-viewer-native-overview', roots.salesOverview, 'D01');
        await forbiddenGet('/admin/reports/management/data?' + period, 'sales-only-management-data');
        await forbiddenGet('/admin/reports/management/export?' + period + '&dimension=profit', 'sales-only-management-export');
        await Promise.all(pendingResponses); await salesViewer.context.close();

        await Promise.all(pendingResponses);
        const completed = preflight ? probedStates : reviewedStates;
        assert.deepEqual([...completed].sort(), [...expectedLabels].sort(), 'Every required UI-18 native flow completed exactly once');
        assert.equal(new Set(completed).size, completed.length);
        if (salesFallbackOnly) assert.deepEqual(dateTimeProofs, [], 'Focused fallback acceptance does not claim unrelated datetime cases');
        else {
            assert.deepEqual(dateTimeProofs.map(proof => proof.segment).sort(), ['orders', 'discounts', 'returns', 'void'].sort(), 'All four native datetime segments inspected');
            assert.ok(dateTimeProofs.find(proof => proof.segment === 'orders').sourceRows > 0, 'Real SQL orders exercise nonempty native local-time cells');
            assert.ok(dateTimeProofs.every(proof => proof.errors.length === 0), 'No native local/UTC datetime display mismatch');
        }
        assert.deepEqual(unexpectedWrites, [], 'Read-only browser guard saw no business writes');
        assert.deepEqual(errors, [], 'No native JS/page errors');
    } catch (error) {
        failed = String(error);
        if (page && !page.isClosed()) {
            await page.screenshot({ path: path.join(info.output, 'driver-failure.png'), fullPage: true, animations: 'disabled' }).catch(() => {});
        }
        throw error;
    } finally {
        await Promise.all(pendingResponses);
        const report = {
            success: !failed, failure: failed, mode, mandatoryMatrixExecuted: !preflight && !failed && reviewedStates.length === expectedLabels.length, mandatoryMatrixComplete: !preflight && !failed && reviewedStates.length === expectedLabels.length,
            coverageScope: salesFallbackOnly ? 'Focused acceptance of the one Sales.View fallback affected by the scoped primary-KPI contrast correction; separate full 39-case evidence covers the other views' : 'All 39 required native report states',
            browser: 'Owned headless Chrome instance; existing user windows never connected, enumerated or closed',
            transport: { nativeResponsesOnly: true, syntheticReportMetrics: false, businessWritesAllowed: false, allowedWriteException: 'Exact same-origin POST /admin/api/store-activity/(online|presence) after login; authentication occurs before business guard', externalOriginsBlocked: true, printAndNewWindowStubbed: true },
            period: Object.fromEntries(period), mandatoryExpectedStates: expectedLabels, reviewedStates, probedStates, nativeStateCount: probedStates.length, matrixReviewedStateCount: reviewedStates.length, nativeFlowCount: probedStates.length, scopeProofs, dateTimeProofs,
            stateProvenance, observations, exports, permissionChecks, unexpectedWrites, errors, externalRequests,
            requestObservations: requests.filter(request => request.pathname.startsWith('/admin/reports/')),
            responseReads, baselineProvided: !!info.baseline,
            baselinePolicy: 'Raw native JSON evidence retained for caller-owned financial/stock/expense comparison; no metrics recomputed in presentation or browser helper'
        };
        fs.writeFileSync(path.join(info.output, 'reports-mobile-result.json'), JSON.stringify(report, null, 2) + '\n');
        if (preflight) fs.writeFileSync(path.join(info.output, 'reports-mobile-diagnostic-native-preflight-result.json'), JSON.stringify(report, null, 2) + '\n');
        await browser.close();
        console.log(JSON.stringify({ success: !failed, mode, mandatoryMatrixExecuted: !preflight && !failed && reviewedStates.length === expectedLabels.length, nativeFlowStates: probedStates.length, reviewedStates: reviewedStates.length, mandatoryStates: expectedLabels.length, csvExports: exports.length, serverPermissionChecks: permissionChecks.length, unexpectedWrites: unexpectedWrites.length, nativePageErrors: errors.length }));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
