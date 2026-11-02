'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let data = ''; process.stdin.on('data', chunk => data += chunk); process.stdin.on('end', () => resolve(data));
    }));
    const output = path.resolve(process.env.GAO_RECEIVING_LIST_OUTPUT || 'TestResults/mobile-ui-05a-20261025/browser');
    fs.mkdirSync(output, { recursive: true });
    // Own headless process/profile only; never attaches to or closes a user's Chrome.
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
    const page = await context.newPage(), errors = [], csp = [], writes = [], checks = [];
    let reads = 0, mode = '', completed = false;
    const endpoint = info.baseUrl + '/admin/warehouse-receiving/receipts';
    function observe(p) {
        p.on('pageerror', e => errors.push(e.message));
        p.on('console', m => { if (/Content Security Policy/i.test(m.text())) csp.push(m.text()); });
        p.on('request', r => {
            const pathname = new URL(r.url()).pathname;
            if (pathname === '/admin/warehouse-receiving/receipts' && r.method() === 'GET') reads++;
            if (r.method() !== 'GET' && /\/admin\/(?:warehouse-receiving|api\/stock-documents)/.test(pathname)) writes.push(pathname);
        });
    }
    observe(page);
    await context.addInitScript(() => {
        const original = window.setTimeout;
        window.setTimeout = function (callback, ms, ...args) {
            return original(callback, window.__receivingFastTimeout && ms === 30000 ? 250 : ms, ...args);
        };
    });
    await page.route(endpoint, async route => {
        if (!mode) return route.continue();
        if (mode === 'network') return route.abort('failed');
        if (mode === 'slow') {
            await new Promise(resolve => setTimeout(resolve, 600));
            return route.fulfill({ status: 500, body: '{}' }).catch(() => {});
        }
        if (mode === 'html') return route.fulfill({ status: 200, contentType: 'text/html', body: '<h1>Login</h1>' });
        if (mode === 'empty') return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
        return route.fulfill({ status: Number(mode), contentType: 'application/json', body: '{"message":"test failure"}' });
    });
    async function login(p, account) {
        await p.goto(info.baseUrl + '/admin/account/login');
        await p.locator('[name=UserName]').fill(account.user); await p.locator('[name=Password]').fill(account.password);
        if (await p.locator('select[name=SelectedTerminalId]').count()) await p.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([p.waitForURL(url => !url.pathname.endsWith('/login')), p.locator('#loginForm button[type=submit]').click()]);
    }
    async function load(p = page, expectRows = true) {
        const response = await p.goto(info.baseUrl + '/admin/warehouse-receiving');
        assert.equal(response.status(), 200);
        await p.waitForFunction(() => document.querySelector('#wrReceiptList')?.getAttribute('aria-busy') === 'false');
        if (expectRows) await p.locator('.wr-card').first().waitFor();
        await p.evaluate(() => document.fonts.ready);
    }
    async function shot(name) { await page.screenshot({ path: path.join(output, name + '.png'), animations: 'disabled' }); }
    async function overflow(p = page) {
        const box = await p.evaluate(() => ({ width: innerWidth, document: document.documentElement.scrollWidth,
            root: document.querySelector('[data-warehouse-receiving-index]').getBoundingClientRect().right }));
        assert.ok(box.document <= box.width + 1 && box.root <= box.width + 1, JSON.stringify(box));
    }
    async function ids(p = page) {
        return p.locator('.wr-card-action').evaluateAll(nodes => nodes.map(n => Number(n.getAttribute('href').split('/').pop())));
    }
    async function closeModal() {
        await page.locator('#wrCreateModal .modal-header [data-bs-dismiss=modal]').click();
        await page.locator('#wrCreateModal').waitFor({ state: 'hidden' });
    }
    try {
        await login(page, info.manager); await load();
        const response = await context.request.get(endpoint);
        assert.equal(response.status(), 200);
        const rows = await response.json();
        assert.deepEqual(rows.map(x => x.id).sort((a,b) => a-b), [...info.activeIds].sort((a,b) => a-b));
        assert.ok(rows.every(x => [1,2,4].includes(x.status) && !x.documentNo.startsWith('EXCLUDED') && !x.documentNo.startsWith('FOREIGN')));
        assert.ok(rows.every(x => !('unitCost' in x) && !('lines' in x) && !('totalAmount' in x)));
        checks.push('real-API-exact-24-active-receipts-tenant-scope-and-safe-summary');
        for (const id of ['wrDraftCount','wrPendingCount','wrRejectedCount']) assert.equal(await page.locator('#' + id).innerText(), '8');
        assert.equal(await page.locator('.wr-card').count(), 10);
        // Existing jQuery measurement can leave only an inert style="" on the search input.
        const canonical = html => html.replace(/(<input[^>]*\bid="wrSearch"[^>]*?) style=""/g,'$1');
        const baseline = (await page.evaluate(() => {
            window.__receivingNodes = [...document.querySelectorAll('[data-warehouse-receiving-index], [data-warehouse-receiving-index] *, [data-wr-index-header], [data-wr-index-header] *')];
            window.__createButton = document.getElementById('btnOpenCreateReceiving');
            return ['[data-warehouse-receiving-index]', '[data-wr-index-header]'].map(s => document.querySelector(s).outerHTML);
        })).map(canonical);
        await shot('receiving-desktop-1440');
        const readsBeforeResize = reads;
        for (const [width,height] of [[1600,1000],[1440,900],[1366,768],[1024,768],[768,1024],[767,900],[430,932],[390,844],[360,800],[320,740]]) {
            await page.setViewportSize({width,height});
            await page.waitForFunction(isPhone => document.querySelector('[data-warehouse-receiving-index]').hasAttribute('data-wr-phone') === isPhone, width < 768);
            await overflow();
            if (width < 768) {
                assert.equal(await page.locator('.wr-mobile-workflow').count(), 10);
                assert.equal(await page.locator('.wr-mobile-workflow [aria-current=step]').count(), 10);
                assert.equal(await page.locator('.gds-kpi-card__icon').first().isVisible(),false);
                assert.ok((await page.locator('.wr-kpi-filter').first().boundingBox()).height <= 90);
                assert.equal(await page.locator('#btnOpenCreateReceiving').count(), 1);
                assert.equal(await page.evaluate(() => document.getElementById('btnOpenCreateReceiving') === window.__createButton), true);
                for (const box of await page.locator('.wr-card-action, .wr-kpi-filter, #wrBtnReload').evaluateAll(nodes => nodes.map(n => n.getBoundingClientRect().height))) assert.ok(box >= 43.9);
                await page.evaluate(() => scrollTo(0,0)); await shot('receiving-phone-' + width);
            } else assert.equal(await page.locator('.wr-mobile-filters').count(), 0);
            checks.push('viewport-' + width + 'x' + height);
        }
        await page.setViewportSize({width:1440,height:900});
        await page.waitForFunction(() => !document.querySelector('[data-wr-phone]'));
        assert.deepEqual((await page.evaluate(() => ['[data-warehouse-receiving-index]', '[data-wr-index-header]'].map(s => document.querySelector(s).outerHTML))).map(canonical), baseline);
        assert.equal(await page.evaluate(() => window.__receivingNodes.every(n => n.isConnected)), true);
        assert.equal(reads, readsBeforeResize);
        checks.push('desktop-DOM-exact-restoration-original-nodes-no-refetch-on-resize');
        // Finish the existing sidebar's resize transition and freeze the online indicator's pulse.
        const desktopPixels = await page.screenshot({animations:'disabled'});
        fs.writeFileSync(path.join(output,'receiving-desktop-current.png'),desktopPixels);
        await page.locator('link[href*="warehouse-receiving-index.mobile.css"]').evaluate(n => { n.disabled = true; });
        const baselinePixels = await page.screenshot({animations:'disabled'});
        fs.writeFileSync(path.join(output,'receiving-desktop-baseline.png'),baselinePixels);
        assert.ok(desktopPixels.equals(baselinePixels), 'New mobile CSS changes no desktop pixels');
        checks.push('desktop-pixel-equality');
        await page.locator('link[href*="warehouse-receiving-index.mobile.css"]').evaluate(n => { n.disabled = false; });
        await page.setViewportSize({width:390,height:844});
        await page.locator('.wr-mobile-filters summary').click();
        assert.equal(await page.locator('#wrMobileWarehouse option').count(), 3);
        const localReads = reads;
        const warehouseKey = String(info.otherLegalEntityId) + '\u001fKho chi nhánh phía Đông';
        await page.locator('#wrMobileWarehouse').selectOption(warehouseKey);
        assert.match(await page.locator('#wrPageInfo').innerText(), /12 phiếu/);
        await page.locator('#wrMobileSource').selectOption('unlinked');
        await page.locator('[data-status-filter="4"]').click();
        const expected = rows.filter(x => x.legalEntityId === info.otherLegalEntityId && !x.purchaseOrderId && x.status === 4);
        assert.deepEqual(await ids(), expected.map(x => x.id));
        assert.ok((await page.locator('.wr-card-action').allTextContents()).every(s => s.trim() === 'Sửa phiếu'));
        assert.ok((await page.locator('.wr-mobile-workflow [aria-current=step]').allTextContents()).every(s => s === 'Cần sửa'));
        for (const id of ['wrDraftCount','wrPendingCount','wrRejectedCount']) assert.equal(await page.locator('#' + id).innerText(), '8');
        checks.push('warehouse-HKD-source-status-intersection-global-KPI-counts');
        await page.setViewportSize({width:1024,height:768});
        await page.waitForFunction(() => !document.querySelector('[data-wr-phone]'));
        assert.deepEqual(await ids(), rows.filter(x => x.status === 4).map(x => x.id));
        await page.setViewportSize({width:390,height:844});
        await page.waitForFunction(() => document.querySelector('[data-wr-phone]'));
        assert.deepEqual(await ids(), expected.map(x => x.id));
        assert.equal(await page.locator('#wrMobileWarehouse').inputValue(), warehouseKey);
        checks.push('phone-filters-bypass-desktop-and-restore-on-return');
        await page.locator('[data-status-filter="4"]').click();
        await page.locator('.wr-mobile-filters summary').click();
        await page.locator('#wrMobileWarehouse').selectOption('');
        await page.locator('#wrMobileSource').selectOption('purchase-order');
        assert.deepEqual(await ids(), rows.filter(x => x.purchaseOrderId).map(x => x.id));
        await page.locator('#wrMobileSource').selectOption('');
        await page.locator('#wrSearch').fill('PHONE-000');
        assert.equal(await page.locator('.wr-card').count(),1);
        assert.match(await page.locator('.wr-card-facts').innerText(), /0 dòng hàng/);
        assert.match(await page.locator('.wr-card-action').innerText(), /Tiếp tục nhập/);
        await page.locator('#wrSearch').fill('does-not-exist');
        assert.equal(await page.locator('.wr-card').count(),0);
        assert.match(await page.locator('.wr-empty').innerText(), /Không có phiếu/);
        await page.locator('#wrSearch').fill('');
        await page.locator('#wrBtnNextPage').click();
        assert.deepEqual(await ids(),rows.slice(10,20).map(x => x.id));
        await page.locator('#wrBtnNextPage').click();
        assert.deepEqual(await ids(),rows.slice(20).map(x => x.id));
        assert.equal(await page.locator('#wrBtnNextPage').isDisabled(),true);
        await page.locator('#wrBtnPrevPage').click();
        assert.deepEqual(await ids(),rows.slice(10,20).map(x => x.id));
        assert.equal(reads,localReads);
        checks.push('purchase-order-filter-empty-receipt-search-no-result-pagination-local-only');
        await page.locator('[data-status-filter="2"]').click();
        assert.ok((await page.locator('.wr-card-action').allTextContents()).every(s => s.trim() === 'Xem phiếu'));
        assert.ok((await page.locator('.wr-mobile-workflow [aria-current=step]').allTextContents()).every(s => s === 'Chờ duyệt'));
        await page.locator('[data-status-filter="2"]').click();
        assert.equal(await page.locator('.wr-title img, .wr-title script').count(),0);
        assert.match(await page.locator('.wr-title').first().innerText(), /<kiểm tra> & đối chiếu/);
        checks.push('status-workflow-and-original-CTA-semantics-escaped-long-title');
        await page.locator('.wr-mobile-filters summary').click();
        await page.locator('#btnOpenCreateReceiving').click();
        await page.locator('#wrCreateModal').waitFor({state:'visible'});
        await page.waitForFunction(() => !bootstrap.Modal.getInstance(document.getElementById('wrCreateModal'))._isTransitioning);
        await page.waitForFunction(() => Number(document.getElementById('wrWarehouseId').value) > 0);
        const formOptions = await (await context.request.get(info.baseUrl + '/admin/api/stock-documents/receipt-form-options')).json();
        assert.equal(await page.locator('#wrLegalEntityId').inputValue(),String(formOptions.defaultLegalEntityId));
        await page.locator('#wrLegalEntityId').selectOption(String(info.otherLegalEntityId));
        assert.equal(await page.locator('#wrWarehouseId').inputValue(),String(info.otherWarehouseId));
        await page.locator('#wrDocumentTitle').fill('Giữ nháp tên đang nhập');
        await page.locator('#wrNote').fill('Ghi chú đang nhập');
        await page.locator('#wrDirectReceiptSource').selectOption('Mua gấp');
        for (const [width,height] of [[320,568],[390,440],[1440,900],[390,844]]) {
            await page.setViewportSize({width,height});
            await page.waitForFunction(height => innerWidth >= 768 || Math.abs(parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--gao-mobile-visible-height')) - height) < 1,height);
            await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
            assert.equal(await page.locator('#wrDocumentTitle').inputValue(),'Giữ nháp tên đang nhập');
            assert.equal(await page.locator('#wrWarehouseId').inputValue(),String(info.otherWarehouseId));
            const box = await page.locator('#wrCreateModal .modal-content').boundingBox();
            assert.ok(box.y >= -1 && box.y + box.height <= height + 1,JSON.stringify(box));
            if (width < 768) {
                const close = await page.locator('#wrCreateModal .modal-header .btn-close').boundingBox();
                assert.ok(close.height >= 44 && close.width >= 44 && close.y >= box.y && close.y + close.height <= box.y + box.height);
            }
        }
        assert.equal(await page.locator('#wrDirectReceiptSource').inputValue(),'Mua gấp');
        assert.equal(await page.locator('#wrNote').inputValue(),'Ghi chú đang nhập');
        await shot('receiving-create-phone-390'); await closeModal();
        checks.push('create-modal-original-fields-HKD-default-warehouse-preserved-on-resize-short-viewport');
        await page.evaluate(() => scrollTo(0,0));
        const stale = await ids();
        for (const failure of ['500','403','html','network','slow']) {
            mode = failure;
            await page.evaluate(() => { window.__receivingFastTimeout = true; });
            await page.locator('#wrBtnReload').click();
            await page.locator('[data-wr-retry]').waitFor();
            assert.deepEqual(await ids(),stale);
            assert.match(await page.locator('#wrLoadStatus').innerText(), /danh sách đã tải trước đó/);
            assert.equal(await page.locator('#wrBtnReload').isDisabled(),false);
            if (failure === 'slow') assert.match(await page.locator('#wrLoadStatus').innerText(),/quá lâu/);
            checks.push('refresh-failure-' + failure + '-keeps-stale-rows-and-retry');
        }
        mode = ''; await page.locator('[data-wr-retry]').click();
        await page.locator('#wrLoadStatus').waitFor({state:'hidden'});
        mode = 'slow';
        const beforeBusy = reads;
        await page.evaluate(() => { window.__receivingFastTimeout = false; loadWarehouseReceivingReceipts(); loadWarehouseReceivingReceipts(); loadWarehouseReceivingReceipts(); });
        await page.waitForFunction(() => document.getElementById('wrBtnReload').disabled);
        assert.equal(await page.locator('#wrReceiptList').getAttribute('aria-busy'),'true');
        await page.locator('[data-wr-retry]').waitFor();
        assert.equal(reads,beforeBusy+1);
        checks.push('single-refresh-flight-disabled-button-aria-busy');
        mode = '500'; await load(page,false);
        assert.equal(await page.locator('.wr-card').count(),0);
        assert.match(await page.locator('.wr-empty').innerText(), /Chưa tải được/);
        await page.locator('[data-wr-retry]').waitFor();
        mode = ''; await page.locator('[data-wr-retry]').click();
        await page.locator('.wr-card').first().waitFor();
        mode = 'empty'; await load(page,false);
        assert.match(await page.locator('.wr-empty').innerText(), /Không có phiếu/);
        assert.equal(await page.locator('#wrDraftCount').innerText(),'0');
        assert.equal(await page.locator('#wrMobileWarehouse option').count(),1);
        checks.push('first-load-error-recovery-and-successful-empty-state');
        mode = ''; await load();
        await page.emulateMedia({forcedColors:'active'}); await overflow(); await shot('receiving-forced-colors-390');
        await page.emulateMedia({forcedColors:'none'});
        await page.evaluate(() => { document.documentElement.style.fontSize = '24px'; }); await overflow();
        await shot('receiving-text-150-percent-390');
        await page.evaluate(() => { document.documentElement.style.fontSize = ''; });
        checks.push('forced-colors-and-150-percent-text-no-horizontal-overflow');
        const employeeContext = await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce'});
        const employeePage = await employeeContext.newPage(); observe(employeePage);
        await login(employeePage,info.employee); await load(employeePage);
        assert.equal(await employeePage.locator('[data-receipt-title], [data-receipt-delete]').count(),0);
        assert.equal(await employeePage.locator('.wr-card-action').count(),10);
        assert.deepEqual((await (await employeeContext.request.get(endpoint)).json()).map(x => x.id),rows.map(x => x.id));
        await employeeContext.close();
        const deniedContext = await browser.newContext(); const deniedPage = await deniedContext.newPage();
        await login(deniedPage,info.denied);
        assert.equal((await deniedContext.request.get(endpoint,{headers:{Accept:'application/json'},maxRedirects:0})).status(),403);
        const forbiddenPage = await deniedContext.request.get(info.baseUrl+'/admin/warehouse-receiving',{maxRedirects:0});
        assert.equal(forbiddenPage.status(),302);
        assert.match(forbiddenPage.headers().location,/\/admin\/account\/access-denied/);
        await deniedContext.close();
        checks.push('create-only-employee-no-rename-delete-and-view-only-role-denied-by-original-policy');
        assert.deepEqual(writes,[]); assert.deepEqual(errors,[]); assert.deepEqual(csp,[]);
        checks.push('no-business-POST-no-pageerror-no-CSP-violation');
        completed = true;
    } finally {
        if (!completed) await shot('failure').catch(() => {});
        fs.writeFileSync(path.join(output,'browser-results.json'),JSON.stringify({completed,checks,reads,writes,errors,csp},null,2));
        await browser.close();
    }
    console.log('RECEIVING LIST BROWSER PASS: ' + checks.length + ' checks; original user Chrome untouched.');
})().catch(error => { console.error(error); process.exitCode = 1; });
