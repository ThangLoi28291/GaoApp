'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const mobile = require('./customer-relations-mobile-review.cjs');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let input = ''; process.stdin.on('data', chunk => input += chunk); process.stdin.on('end', () => resolve(input));
    }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const errors = [], observations = [], notes = [], unexpectedWrites = [];
    let page;
    fs.mkdirSync(info.output, { recursive: true });
    try {
        async function login(account, allowNotes) {
            const context = await browser.newContext({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block' });
            await mobile.protect(context, info.baseUrl);
            const current = await context.newPage(); current.setDefaultTimeout(30000);
            current.on('pageerror', error => errors.push(error.message));
            current.on('dialog', async dialog => { errors.push('Unexpected native dialog: ' + dialog.message()); await dialog.dismiss(); });
            await current.goto(info.baseUrl + '/admin/account/login?returnUrl=%2FAdmin%2FCustomer');
            await current.locator('[name=UserName]').fill(account.user); await current.locator('[name=Password]').fill(account.password);
            if (await current.locator('[name=SelectedTerminalId]').count()) await current.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
            await Promise.all([current.waitForURL(url => !url.pathname.endsWith('/login')), current.locator('#loginForm button[type=submit]').click()]);
            await context.route('**/*', async route => {
                const request = route.request(), url = new URL(request.url());
                if (['GET', 'HEAD', 'OPTIONS'].includes(request.method()) || /^\/admin\/api\/store-activity\/(?:online|presence)$/.test(url.pathname)) return route.fallback();
                const match = url.pathname.match(/^\/Admin\/(Customer|Supplier)\/Edit(?:\/(\d+))?$/i);
                if (allowNotes && request.method() === 'POST' && match) {
                    const supplier = match[1].toLowerCase() === 'supplier', id = String(supplier ? info.supplierId : info.customerId);
                    const data = new URLSearchParams(request.postData() || ''), note = supplier ? info.supplierNote : info.customerNote;
                    assert.equal(data.get('Id'), id, 'Native note form must target the owned fixture row');
                    assert.equal(data.get('Note'), note, 'Only the planned native note may be submitted');
                    if (match[2]) assert.equal(match[2], id);
                    notes.push({ kind: supplier ? 'supplier' : 'customer', id: Number(id), pathname: url.pathname });
                    return route.fallback();
                }
                unexpectedWrites.push({ method: request.method(), pathname: url.pathname });
                return route.abort('blockedbyclient');
            });
            return { context, page: current };
        }
        const responseFor = (pathname, predicate = () => true, method = 'GET') => page.waitForResponse(response => {
            const url = new URL(response.url());
            return response.request().method() === method && url.pathname.toLowerCase() === pathname.toLowerCase() && predicate(url);
        });
        const visit = async (route, root) => {
            const response = await page.goto(info.baseUrl + route); assert.equal(response.status(), 200, route);
            await page.locator(root).first().waitFor(); await mobile.settle(page);
        };
        const review = (label, root) => mobile.review(page, label, info.output, { root });
        const closeModal = async selector => {
            await page.locator(selector + ' .btn-close').first().click();
            await page.locator(selector).waitFor({ state: 'hidden' }); await mobile.settle(page);
        };
        const profileReady = async () => {
            await page.waitForFunction(() => document.getElementById('cpResults')?.getAttribute('aria-busy') === 'false');
            assert.equal(await page.locator('#cpResults [data-retry]').count(), 0, await page.locator('#cpResults').innerText());
            await mobile.settle(page);
        };
        const profileTab = async tab => { await page.locator(`[data-tab="${tab}"]`).click(); await profileReady(); };
        const manager = await login(info.manager, true); page = manager.page;

        await visit('/Admin/Customer/Index', '[data-customer-shell]');
        await review('customer-native-index', '[data-customer-shell]');
        await page.locator('#customerFilterOpen').click();
        await page.locator('#customerFilterSheet.is-open').waitFor();
        const customerSearch = responseFor('/Admin/Customer/Search');
        await page.locator('#txtSearch').fill('KH-PROFILE'); assert.equal((await customerSearch).status(), 200);
        await page.waitForFunction(() => document.getElementById('customerTableWrapper')?.getAttribute('aria-busy') === 'false');
        await mobile.dialog(page, '#customerFilterSheet', 'customer-native-filter-sheet', info.output);
        await page.locator('#customerFilterClose').click();
        const customerCard = page.locator(`.customer-mobile-card[data-id="${info.customerId}"]`);
        await customerCard.waitFor();
        await customerCard.locator('.js-customer-quick-view').click();
        await page.locator('#customerQuickViewModal.show').waitFor();
        assert.match(await page.locator('#customerQuickViewName').innerText(), /Nguyễn Minh An/);
        await mobile.dialog(page, '#customerQuickViewModal', 'customer-native-quick-view', info.output);
        await closeModal('#customerQuickViewModal');

        const customerEditor = '[data-layout-consistency]:has(form select[name="PriceTier"])';
        await visit('/Admin/Customer/Create', customerEditor);
        await review('customer-native-create', customerEditor);
        await page.locator('#Name').fill(''); await page.locator(customerEditor).locator('form button[type=submit]').click();
        await page.locator('[data-valmsg-for=Name].field-validation-error').waitFor();
        await visit('/Admin/Customer/Edit/' + info.customerId, customerEditor);
        const customerVersion = await page.locator('#RowVersion').inputValue(); assert.ok(customerVersion.length > 0);
        await page.locator('#Note').fill(info.customerNote);
        await review('customer-native-edit-note-draft', customerEditor);
        const customerSaved = page.waitForResponse(response => response.request().method() === 'POST' && /\/Customer\/Edit(?:\/\d+)?$/i.test(new URL(response.url()).pathname));
        await page.locator(customerEditor).locator('form button[type=submit]').click();
        assert.equal((await customerSaved).status(), 302);
        await page.waitForURL(url => /^\/Admin\/Customer(?:\/Index)?$/i.test(url.pathname));
        await visit('/Admin/Customer/Edit/' + info.customerId, customerEditor);
        assert.equal(await page.locator('#Note').inputValue(), info.customerNote);
        assert.notEqual(await page.locator('#RowVersion').inputValue(), customerVersion);
        observations.push('Native customer create validation and existing edit form retained; only the owned customer note was saved with its original RowVersion and form controls.');

        await visit(`/admin/customers/${info.customerId}/profile`, '#customerProfile'); await profileReady();
        assert.equal((await page.locator('#cpPoints').innerText()).trim(), '11');
        assert.match(await page.locator('.cp-stat').filter({ hasText: 'Giá trị mua sau hoàn tiền' }).locator('strong').innerText(), /200[.,]000/);
        await review('profile-native-overview', '#customerProfile');
        await profileTab('orders'); await review('profile-native-orders', '#customerProfile');
        await page.locator(`#cpResults [data-order="${info.orderId}"]`).first().click();
        await page.waitForFunction(() => document.getElementById('cpDetailTitle')?.textContent === 'PROFILE-SALE');
        assert.match(await page.locator('#cpDetailBody').innerText(), /100[.,]000/);
        await mobile.dialog(page, '#cpDetail', 'profile-native-order-preview', info.output);
        await page.locator('#cpCloseDetail').click();
        await profileTab('points'); await review('profile-native-points', '#customerProfile');
        await page.locator('#cpNext').click(); await profileReady();
        assert.equal(await page.locator('#cpResults .cp-table tbody tr').count(), 3);
        await review('profile-native-points-last-page', '#customerProfile');
        await profileTab('vouchers'); await review('profile-native-vouchers', '#customerProfile');
        await page.locator(`#cpResults [data-voucher="${info.voucherId}"]`).first().click();
        await page.waitForFunction(() => document.getElementById('cpDetailTitle')?.textContent === 'VC-PROFILE-USED');
        assert.match(await page.locator('#cpDetailBody').innerText(), /Đã dùng/);
        await mobile.dialog(page, '#cpDetail', 'profile-native-voucher-preview', info.output);
        await page.locator('#cpCloseDetail').click();
        observations.push('Native profile overview, completed orders, 23 point entries with pagination and vouchers preserve source totals; original order and used-voucher detail dialogs open the selected records.');

        await visit('/Admin/Supplier/Index', '[data-supplier-shell]');
        await review('supplier-native-index', '[data-supplier-shell]');
        const supplierName = (await page.locator('.gds-mobile-list .gds-row-card__title').first().innerText()).trim();
        const supplierSearch = responseFor('/Admin/Supplier/Search');
        await page.locator('#txtSearch').fill(supplierName); assert.equal((await supplierSearch).status(), 200);
        await page.waitForFunction(() => document.getElementById('supplierTableWrapper')?.getAttribute('aria-busy') === 'false');
        assert.ok((await page.locator('.gds-mobile-list').innerText()).includes(supplierName));
        await visit('/Admin/Supplier/Create', '.supplier-editor');
        await review('supplier-native-create-through-edit-view', '.supplier-editor');
        await page.locator('#Name').fill(''); await page.locator('.supplier-editor').locator('form .se-save').click();
        await page.locator('[data-valmsg-for=Name].field-validation-error').waitFor();
        await visit('/Admin/Supplier/Edit/' + info.supplierId, '.supplier-editor');
        assert.equal(await page.locator('#Code').isEditable(), false);
        const supplierVersion = await page.locator('#RowVersion').inputValue(); assert.ok(supplierVersion.length > 0);
        await page.locator('#Note').fill(info.supplierNote);
        await review('supplier-native-edit-note-draft', '.supplier-editor');
        const supplierSaved = page.waitForResponse(response => response.request().method() === 'POST' && /\/Supplier\/Edit(?:\/\d+)?$/i.test(new URL(response.url()).pathname));
        await page.locator('.supplier-editor').locator('form .se-save').click(); assert.equal((await supplierSaved).status(), 302);
        await page.waitForURL(url => /^\/Admin\/Supplier(?:\/Index)?$/i.test(url.pathname));
        await visit('/Admin/Supplier/Edit/' + info.supplierId, '.supplier-editor');
        assert.equal(await page.locator('#Note').inputValue(), info.supplierNote);
        assert.notEqual(await page.locator('#RowVersion').inputValue(), supplierVersion);
        observations.push('Supplier Create uses its existing Edit view. Native required-name validation, immutable edit Code, bank fields and original note form survive resize; only the owned supplier note was saved.');

        await visit('/admin/customer-debt', '#customerDebtPage');
        await review('debt-native-customer-list', '#customerDebtPage');
        await visit('/admin/customer-debt?customerId=' + info.customerId, '#customerDebtPage');
        const debtOrder = page.locator('[data-debt-panel="orders"] tr').filter({ has: page.locator(`a[href="/admin/pos/order-detail/${info.debtOrderId}"]`) });
        assert.match(await debtOrder.innerText(), /50[.,]000/);
        await review('debt-native-selected-orders', '#customerDebtPage');
        for (const tab of ['journal', 'receipts', 'refunds']) {
            await page.locator(`[data-debt-tab="${tab}"]`).click();
            await page.locator(`[data-debt-panel="${tab}"]`).waitFor();
            await review('debt-native-selected-' + tab, '#customerDebtPage');
        }
        assert.equal(await page.locator('#debtBank').isDisabled(), true);
        await page.locator('#debtOrder').selectOption(String(info.debtOrderId));
        await page.locator('#debtAmount').fill('0'); await page.locator('#debtSubmit').click();
        assert.equal(await page.locator('#debtAmount').evaluate(node => node.validity.valid), false);
        await page.locator('#debtAmount').fill('50000'); await page.locator('#debtNote').fill('UI15 debt draft only');
        await page.locator('#debtMethod').selectOption('1');
        assert.equal(await page.locator('#debtBank').isDisabled(), false);
        assert.equal(await page.locator('#debtBank').getAttribute('required'), '');
        assert.equal(await page.locator('#debtReference').getAttribute('required'), '');
        await page.locator('#debtReference').fill('UI15-DRAFT-NO-PAYMENT');
        await review('debt-native-collection-bank-draft', '#customerDebtPage');
        await visit('/admin/customer-debt/collections?customerId=' + info.customerId, '#customerDebtPage');
        await review('debt-native-collections-report', '#customerDebtPage');
        await page.locator('#debtReportFilters [name=from]').fill('2026-10-26');
        await page.locator('#debtReportFilters [name=to]').fill('2026-10-25');
        await page.locator('#debtReportFilters button[type=submit]').click();
        await page.locator('#debtReportError').filter({ hasText: 'Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.' }).waitFor();
        await page.locator('#debtReportFilters [name=from]').fill('2026-01-01');
        await page.locator('#debtReportFilters [name=to]').fill('2026-12-31');
        await page.locator('#debtReportFilters [name=search]').fill('');
        const reportNavigation = page.waitForURL(url => url.pathname === '/admin/customer-debt/collections' && url.searchParams.get('from') === '2026-01-01');
        await page.locator('#debtReportFilters button[type=submit]').click(); await reportNavigation;
        await page.locator('#debtReportTotal').waitFor(); assert.match(await page.locator('#debtReportTotal').innerText(), /10[.,]000/);
        observations.push('Native debt customer/order/receipt/journal/refund tabs retain the 50,000 outstanding order and 10,000 collection. Zero collection and reversed report dates validate locally; bank draft fields retain disabled/required semantics without submitting money.');

        await visit('/admin/customer-deposit', '#customerDepositPage');
        await review('deposit-native-all-tickets', '#customerDepositPage');
        const depositRow = page.locator(`.deposit-list-table tr[data-deposit-id="${info.depositId}"]`);
        assert.match(await depositRow.innerText(), /160[.,]000/);
        const depositQuery = page.waitForURL(url => url.pathname === '/admin/customer-deposit' && url.searchParams.get('search') === 'PCOC-' + String(info.depositId).padStart(6, '0'));
        await page.locator('.deposit-list-filters [name=search]').fill('PCOC-' + String(info.depositId).padStart(6, '0'));
        await page.locator('.deposit-list-filters button[type=submit]').click(); await depositQuery;
        assert.equal(await page.locator('.deposit-list-table tr[data-deposit-id]').count(), 1);
        await visit('/admin/customer-deposit?customerId=' + info.customerId, '#customerDepositPage');
        await page.locator('[data-deposit-tab="receive"]').click();
        await page.locator('#depositReceive [name=purpose]').fill('UI15 draft only, do not receive money');
        await page.locator('#depositReceive [name=amount]').fill('120000');
        await page.locator('#depositReceive [name=note]').fill('Retain this draft during resize');
        await review('deposit-native-receive-cash-draft', '#customerDepositPage');
        await page.locator('#depositReceive .deposit-methods label').filter({ has: page.locator('[name=method][value="1"]') }).click();
        assert.equal(await page.locator('#depositCreateQr').isVisible(), true);
        assert.equal(await page.locator('#depositQrPreview').isVisible(), false);
        await review('deposit-native-receive-bank-draft', '#customerDepositPage');
        for (const tab of ['list', 'balances', 'journal']) {
            await page.locator(`[data-deposit-tab="${tab}"]`).click();
            await page.locator(`[data-deposit-panel="${tab}"]`).waitFor();
            await review('deposit-native-selected-' + tab, '#customerDepositPage');
        }
        const linkedDepositOrder = page.locator(`[data-deposit-panel="journal"] a[href="/admin/pos/order-detail/${info.depositOrderId}"]`);
        assert.ok(await linkedDepositOrder.count() > 0);
        await page.locator('[data-deposit-tab="refund"]').click();
        await page.locator('#depositRefund [name=depositId]').selectOption(String(info.depositId));
        await page.locator('#depositRefund [name=amount]').fill('10000');
        await page.locator('#depositRefund [name=note]').fill('UI15 refund draft only');
        await review('deposit-native-refund-draft', '#customerDepositPage');
        observations.push('Native deposit all/filtered lists, receive cash/bank drafts, balance tickets, journal with the source order link and refund draft preserve the 250,000 receive -40,000 refund -50,000 use =160,000 balance. No QR, receive, refund, apply or print command was submitted.');

        await visit('/admin/reward-vouchers/settings', '[data-reward-settings-page]');
        const rateSerialization = await page.locator('[data-reward-settings-page]').evaluate(node => ({
            moneyPerPoint: Number(node.dataset.originalMoneyPerPoint),
            pointsPerVoucher: Number(node.dataset.originalPointsPerVoucher),
            voucherValue: Number(node.dataset.originalVoucherValue)
        }));
        assert.deepEqual(rateSerialization, { moneyPerPoint: 1000, pointsPerVoucher: 30, voucherValue: 30000 }, 'Native policy data attributes must retain invariant numeric values');
        await review('rewards-native-settings', '[data-reward-settings-page]');
        const categoryBoxes = page.locator('[name=ExcludedCategoryIds]');
        assert.ok(await categoryBoxes.count() > 0);
        const originallyChecked = await categoryBoxes.first().isChecked();
        await categoryBoxes.first().setChecked(!originallyChecked);
        await page.locator('#rewardCategorySearch').fill('UI15-NO-CATEGORY-MATCH');
        assert.equal(await page.locator('[data-reward-category]:visible').count(), 0);
        assert.equal(await categoryBoxes.first().isChecked(), !originallyChecked);
        await page.locator('#rewardCategorySearch').fill('');
        await categoryBoxes.first().setChecked(originallyChecked);
        const oldMoneyPerPoint = await page.locator('#moneyPerPoint').inputValue();
        await page.locator('#moneyPerPoint').fill(String(Number(oldMoneyPerPoint) + 1));
        await page.locator('#btnSaveRewardSettings').click();
        await page.locator('#rewardSettingsConfirmModal.show').waitFor();
        assert.equal(await page.locator('#confirmRateChange').inputValue(), 'false');
        assert.match(await page.locator('#oldPolicySummary').innerText(), /1[.,]000\s*đ\s*\/\s*điểm/);
        assert.match(await page.locator('#oldPolicySummary').innerText(), /voucher\s*30[.,]000\s*đ/);
        assert.match(await page.locator('#newPolicySummary').innerText(), /1[.,]001\s*đ\s*\/\s*điểm/);
        assert.match(await page.locator('#newPolicySummary').innerText(), /voucher\s*30[.,]000\s*đ/);
        await mobile.dialog(page, '#rewardSettingsConfirmModal', 'rewards-native-rate-change-cancel', info.output);
        await closeModal('#rewardSettingsConfirmModal');
        observations.push('The three native policy data attributes retain invariant numeric values 1000/30/30000; old/new confirmation summaries preserve the 1,000 to 1,001 money-per-point change and 30,000 voucher value. Reward category search keeps hidden selected checkboxes in the native form. Rate change opens the original confirmation, cancelled with ConfirmRateChange=false; no policy settings were saved.');

        await visit('/admin/reward-vouchers', '[data-reward-voucher-index]');
        await page.waitForFunction(() => document.getElementById('rewardVoucherResultsPanel')?.getAttribute('aria-busy') === 'false');
        await page.locator(`[data-voucher-card="${info.voucherId}"]`).waitFor();
        await review('vouchers-native-index', '[data-reward-voucher-index]');
        await page.locator('#rewardVoucherOpenMobileFilters').click();
        await page.locator('#rewardVoucherMobileFilterSheet.show').waitFor();
        await mobile.dialog(page, '#rewardVoucherMobileFilterSheet', 'vouchers-native-mobile-filters', info.output);
        await page.locator('#rewardVoucherMobileStatus').selectOption('used');
        const filteredVouchers = responseFor('/admin/reward-vouchers/data', url => url.searchParams.get('status') === 'used');
        await page.locator('#rewardVoucherMobileApplyFilters').click(); assert.equal((await filteredVouchers).status(), 200);
        await page.locator('#rewardVoucherMobileFilterSheet').waitFor({ state: 'hidden' });
        await page.locator(`[data-voucher-card="${info.voucherId}"] [data-voucher-view]`).click();
        await page.locator('#rewardVoucherQuickView.show').waitFor();
        await page.waitForFunction(() => document.getElementById('rewardVoucherQuickCode')?.textContent === 'VC-PROFILE-USED');
        await mobile.dialog(page, '#rewardVoucherQuickView', 'vouchers-native-used-quick-view', info.output);
        assert.equal(await page.locator('#rewardVoucherCancel').isVisible(), false);
        assert.equal(await page.locator('#rewardVoucherLock').isVisible(), false);
        await page.locator('#rewardVoucherOpenHistory').click();
        await page.locator('#rewardVoucherHistorySheet.show').waitFor();
        await page.waitForFunction(() => !document.getElementById('rewardVoucherHistoryList').textContent.includes('Đang tải'));
        await mobile.dialog(page, '#rewardVoucherHistorySheet', 'vouchers-native-history', info.output);
        await closeModal('#rewardVoucherHistorySheet');
        observations.push('Native voucher index, used-status mobile filters, selected used voucher quick view and existing history are read-only; status-dependent unavailable lock/cancel actions remain hidden.');
        await manager.context.close();

        const viewer = await login(info.viewer, false); page = viewer.page;
        await visit(`/admin/customers/${info.customerId}/profile`, '#customerProfile'); await profileReady();
        assert.equal(await page.locator('[data-tab=orders]').count(), 0);
        assert.equal(await page.locator('#cpResults [data-order]').count(), 0);
        assert.match(await page.locator('#customerProfile').innerText(), /Cần quyền xem đơn hàng/);
        await review('profile-viewer-native-overview-without-orders', '#customerProfile');
        await profileTab('points');
        assert.equal(await page.locator('#cpResults [data-order]').count(), 0);
        await review('profile-viewer-native-points-without-orders', '#customerProfile');
        await profileTab('vouchers');
        await page.locator(`#cpResults [data-voucher="${info.voucherId}"]`).first().click();
        await page.waitForFunction(() => document.getElementById('cpDetailTitle')?.textContent === 'VC-PROFILE-USED');
        assert.equal(await page.locator('#cpDetailBody a[href*="/admin/pos/order-detail/"]').count(), 0);
        await mobile.dialog(page, '#cpDetail', 'profile-viewer-native-voucher-without-order-links', info.output);
        await page.locator('#cpCloseDetail').click();
        await viewer.context.close();
        assert.deepEqual(unexpectedWrites, [], 'No financial, reward or configuration command may be issued');
        assert.deepEqual(notes.map(note => note.kind).sort(), ['customer', 'supplier'], 'Only two owned native notes were saved');
        assert.deepEqual(errors, [], 'Original owners must produce no JavaScript errors');
        fs.writeFileSync(path.join(info.output, 'browser-checks.json'), JSON.stringify({ passed: true, observations, errors, unexpectedWrites,
            nativeNoteWrites: notes, rateSerialization, existingChromeWindowsUntouched: true, freshHeadlessContexts: true,
            standardAssetNegotiation: !process.env.GAO_UI15_RAW_ASSETS, noFinancialOrRewardWrites: true }, null, 2) + '\n');
        console.log(JSON.stringify({ passed: true, observations, nativeNoteWrites: notes }));
    } catch (error) {
        console.error(error.stack || error);
        if (page && !page.isClosed()) {
            await page.screenshot({ path: path.join(info.output, 'browser-failure.png'), fullPage: true, animations: 'disabled' }).catch(() => {});
            fs.writeFileSync(path.join(info.output, 'browser-failure.json'), JSON.stringify({ error: String(error), url: page.url(), errors, unexpectedWrites, nativeNoteWrites: notes, observations }, null, 2) + '\n');
        }
        process.exitCode = 1;
    } finally { await browser.close(); }
})();
