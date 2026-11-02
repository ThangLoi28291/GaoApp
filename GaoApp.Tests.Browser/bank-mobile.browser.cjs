'use strict';
// UI-17 native bank views, local drafts and cancel paths in an owned headless browser.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium } = require('playwright');
const mobile = require('./bank-mobile-review.cjs');

(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', value => text += value); process.stdin.on('end', () => resolve(text)); }));
    fs.mkdirSync(info.output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const origin = new URL(info.baseUrl).origin;
    const errors = [], unexpectedWrites = [], forbiddenReads = [], bankRequests = [], observations = [], reviewedStates = [], stateProvenance = [], permissionChecks = [];
    const expectedLabels = [
        'bank-native-order-lookup', 'bank-native-order-review-lookup', 'bank-native-order-lookup-evidence', 'bank-native-embedded-order-lookup',
        'bank-controlled-lookup-empty', 'bank-controlled-lookup-error', 'bank-controlled-lookup-slow',
        'bank-native-reconciliation-all', 'bank-native-reconciliation-history', 'bank-captured-bank-results', 'bank-captured-bank-empty',
        'bank-native-notifications-live', 'bank-native-store-bank-accounts', 'bank-native-store-bank-accounts-filtered',
        'bank-native-store-bank-create-draft', 'bank-native-store-bank-edit-draft', 'bank-native-acb-settings-draft',
        'bank-native-routing-current-empty', 'bank-captured-routing-populated', 'bank-captured-sandbox-result',
        'bank-viewer-native-order-lookup', 'bank-viewer-native-reconciliation', 'bank-viewer-native-notifications', 'bank-viewer-native-accounts'
    ];
    let page, credentialLeak = false, liveChecks = null;
    try {
        async function login(account) {
            const context = await browser.newContext({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block' });
            await mobile.protect(context, info.baseUrl);
            const current = await context.newPage(); current.setDefaultTimeout(30000);
            current.on('pageerror', error => errors.push(error.message));
            current.on('dialog', async dialog => { errors.push('Unexpected native dialog: ' + dialog.type()); await dialog.dismiss(); });
            current.on('request', request => {
                const address = new URL(request.url());
                if (address.origin !== origin && /acb|bank|oauth/i.test(address.hostname)) bankRequests.push({ method: request.method(), hostname: address.hostname });
            });
            await current.goto(info.baseUrl + '/admin/account/login?returnUrl=%2FAdmin%2FStoreBankAccounts');
            await current.locator('[name=UserName]').fill(account.user); await current.locator('[name=Password]').fill(account.password);
            if (await current.locator('[name=SelectedTerminalId]').count()) await current.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
            await Promise.all([current.waitForURL(url => !url.pathname.toLowerCase().endsWith('/login')), current.locator('#loginForm button[type=submit]').click()]);
            await context.route('**/*', async route => {
                const request = route.request(), address = new URL(request.url());
                if (address.origin !== origin) return route.fallback();
                if (['GET', 'HEAD', 'OPTIONS'].includes(request.method())) {
                    if (/^\/admin\/acb\/payments\/orders\/\d+\/qrs\/\d+$/i.test(address.pathname)) {
                        forbiddenReads.push({ method: request.method(), pathname: address.pathname }); return route.abort('blockedbyclient');
                    }
                    return route.fallback();
                }
                if (/^\/admin\/api\/store-activity\/(?:online|presence)$/.test(address.pathname)) return route.fallback();
                unexpectedWrites.push({ method: request.method(), pathname: address.pathname }); return route.abort('blockedbyclient');
            });
            return { context, page: current };
        }
        const noCanary = async label => {
            assert.ok(info.canary && typeof info.canary === 'string');
            credentialLeak = (await page.content()).includes(info.canary);
            assert.equal(credentialLeak, false, label + ' does not return a stored credential canary');
        };
        const visit = async (route, root) => {
            const response = await page.goto(info.baseUrl + route); assert.equal(response.status(), 200, route);
            await page.locator(root).first().waitFor(); await mobile.settle(page); await noCanary(route);
        };
        const review = async (label, root, provenance = 'Native authenticated Razor and owned SQL read models') => {
            await noCanary(label); await mobile.review(page, label, info.output, { root });
            reviewedStates.push(label); stateProvenance.push({ label, provenance }); await noCanary(label);
        };
        const nativeRoot = async (locator, ancestor = '.container-fluid,.container-xxl') => locator.first().evaluate((node, ancestor) => {
            const root = node.closest(ancestor); if (!root) throw new Error('Native root not found: ' + ancestor);
            const parts = [];
            for (let current = root; current && current !== document.documentElement; current = current.parentElement) {
                const siblings = [...current.parentElement.children].filter(sibling => sibling.tagName === current.tagName);
                parts.unshift(current.tagName.toLowerCase() + ':nth-of-type(' + (siblings.indexOf(current) + 1) + ')');
            }
            return 'html > ' + parts.join(' > ');
        }, ancestor);
        const lookupReady = async () => {
            await page.waitForFunction(() => !document.getElementById('acbRefresh')?.disabled && !document.getElementById('acbLookupStatus')?.textContent.includes('Đang tải'));
            await mobile.settle(page);
        };
        const banksReady = async () => {
            await page.waitForFunction(() => document.getElementById('storeBankResultsPanel')?.getAttribute('aria-busy') === 'false' && !document.getElementById('storeBankMobileList')?.textContent.includes('Đang tải dữ liệu'));
            await mobile.settle(page);
        };
        const closeModal = async selector => {
            await page.locator(selector + ' [data-bs-dismiss="modal"]').first().click();
            await page.locator(selector).waitFor({ state: 'hidden' }); await mobile.settle(page);
        };
        async function captured(label, frameRoute, capturedBody, target, rootLocator) {
            assert.ok(typeof capturedBody === 'string' && capturedBody.length > 20, label + ' compiled native captured body');
            const response = await page.context().request.get(info.baseUrl + frameRoute); assert.equal(response.status(), 200);
            const frame = await response.text();
            const html = await page.evaluate(({ frame, capturedBody, target }) => {
                const doc = new DOMParser().parseFromString(frame, 'text/html');
                const host = doc.querySelector(target || '[data-mobile-shell-content]'); if (!host) throw new Error('Native captured frame target missing');
                host.innerHTML = /<html[\s>]/i.test(capturedBody) ? new DOMParser().parseFromString(capturedBody, 'text/html').body.innerHTML : capturedBody;
                return '<!doctype html>\n' + doc.documentElement.outerHTML;
            }, { frame, capturedBody, target });
            const address = info.baseUrl + '/ui17/captured/' + label;
            await page.route(address, route => route.fulfill({ contentType: 'text/html; charset=utf-8', body: html }));
            const loaded = await page.goto(address); assert.equal(loaded.status(), 200); await mobile.settle(page);
            const root = await rootLocator(); await review(label, root, 'Compiled original Razor captured by the owned test host; injected into a native authenticated frame; no bank POST or external provider call');
            await page.unroute(address);
        }

        const manager = await login(info.manager); page = manager.page;
        const orderRoute = '/admin/acb/payments/orders/' + info.orderId;
        const firstData = await manager.context.request.get(info.baseUrl + orderRoute + '/data'); assert.equal(firstData.status(), 200);
        const originalData = await firstData.json();
        assert.equal(originalData.order.id, info.orderId); assert.equal(Number(originalData.order.grandTotal), 10000);
        assert.ok(originalData.sessions.length > 0 && originalData.sessions.some(session => session.transactions.length > 0), 'Owned matched ACB order has native transaction evidence');
        await visit(orderRoute, '#acbLookup'); await lookupReady();
        await review('bank-native-order-lookup', '#acbLookup');
        await visit('/admin/acb/payments/orders/' + info.reviewOrderId, '#acbLookup'); await lookupReady();
        assert.ok(await page.locator('.acb-qr-row').count() > 0, 'Review order retains native QR history');
        await review('bank-native-order-review-lookup', '#acbLookup');
        await visit(orderRoute, '#acbLookup'); await lookupReady();
        await page.locator('.acb-qr-row').first().evaluate(node => { node.open = true; });
        await review('bank-native-order-lookup-evidence', '#acbLookup');
        await visit(orderRoute + '?embedded=true', '#acbLookup'); await lookupReady();
        assert.equal(await page.locator('body.acb-embedded').count(), 1); await review('bank-native-embedded-order-lookup', '#acbLookup');
        observations.push('Full and embedded lookup use the original saved-data GET owner. A matched order and a review order retain their QR/transaction evidence; no refresh, cancel or complete command is clicked.');

        const controlledRoute = '/admin/acb/payments/orders/' + info.waitingOrderId;
        const controlledUrl = info.baseUrl + controlledRoute + '/data';
        const emptyData = { order: { id: info.waitingOrderId, orderNumber: 'UI17-CONTROLLED-EMPTY', grandTotal: 10000, balanceDue: 10000 }, sessions: [], payments: [], otherQrs: [] };
        await page.route(controlledUrl, route => route.fulfill({ contentType: 'application/json', body: JSON.stringify(emptyData) }));
        await visit(controlledRoute, '#acbLookup'); await lookupReady();
        assert.match(await page.locator('#acbLookupResults').innerText(), /Chưa có giao dịch chuyển khoản/);
        await review('bank-controlled-lookup-empty', '#acbLookup', 'Synthetic response to the original lookup GET, with the original renderer and no command'); await page.unroute(controlledUrl);
        await page.route(controlledUrl, route => route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'UI17 controlled read temporarily unavailable' }) }));
        await visit(controlledRoute, '#acbLookup'); await lookupReady();
        assert.match(await page.locator('#acbLookupStatus.acb-error').innerText(), /UI17 controlled read temporarily unavailable/);
        await review('bank-controlled-lookup-error', '#acbLookup', 'Synthetic 503 response to the original lookup GET; original error owner, no bank call'); await page.unroute(controlledUrl);
        let release, readStarted;
        const gate = new Promise(resolve => { release = resolve; }), requested = new Promise(resolve => { readStarted = resolve; });
        await page.route(controlledUrl, async route => { readStarted(); await gate; await route.fulfill({ contentType: 'application/json', body: JSON.stringify(emptyData) }); });
        try {
            await visit(controlledRoute, '#acbLookup'); await requested;
            assert.equal(await page.locator('#acbRefresh').isDisabled(), true);
            await review('bank-controlled-lookup-slow', '#acbLookup', 'Held synthetic lookup GET response; original loading and disabled control owner, no bank call');
        } finally { release(); }
        await lookupReady(); await page.unroute(controlledUrl);

        const dates = '?from=' + encodeURIComponent(info.fromDate) + '&to=' + encodeURIComponent(info.fromDate);
        await visit('/admin/acb/reconciliation' + dates + '&source=ALL', '.acb-reconcile');
        assert.ok(await page.locator('.acb-reconcile .acb-ledger tbody tr').count() > 0);
        await review('bank-native-reconciliation-all', '.acb-reconcile');
        const orderLink = page.locator(`.acb-reconcile a[data-acb-order-id="${info.orderId}"]`).first();
        await orderLink.click(); await page.locator('.acb-dialog[open]').waitFor();
        await page.frameLocator('.acb-dialog iframe').locator('#acbLookupResults .acb-panel').first().waitFor();
        await mobile.dialog(page, '.acb-dialog[open]', 'bank-native-order-embedded-dialog', info.output);
        await page.locator('.acb-dialog-close').click(); await page.locator('.acb-dialog[open]').waitFor({ state: 'hidden' });
        await visit('/admin/acb/reconciliation' + dates + '&source=TRANSACTION_HISTORY', '.acb-reconcile');
        assert.equal(await page.locator('#acbSource').inputValue(), 'TRANSACTION_HISTORY');
        await review('bank-native-reconciliation-history', '.acb-reconcile');
        await captured('bank-captured-bank-results', '/admin/acb/reconciliation' + dates + '&source=ALL', info.captures.bankLookupPopulated, '#acb-bank-results', async () => {
            assert.ok(await page.locator('#acb-bank-results .acb-bank-ledger tbody tr').count() > 1);
            assert.ok(await page.locator('#acb-bank-results td[rowspan]').count() > 0); return '.acb-reconcile';
        });
        await captured('bank-captured-bank-empty', '/admin/acb/reconciliation' + dates + '&source=ALL', info.captures.bankLookupEmpty, '#acb-bank-results', async () => {
            assert.match(await page.locator('#acb-bank-results').innerText(), /ACB chưa trả giao dịch/); return '.acb-reconcile';
        });
        observations.push('Native callback reconciliation is read for all sources and history; local order links open the existing embedded dialog. Direct-bank populated/empty partials are compiled captures only. Bank lookup POST, reprocess and refresh remain unsubmitted.');

        await visit('/admin/bank-notifications', '#bankNotificationLiveState');
        const liveRoot = await nativeRoot(page.locator('#bankNotificationLiveStatus'));
        assert.equal(await page.locator('#bankNotificationWaitingRows [data-live-key^="qr-"]').count(), 2);
        const receipt = page.locator(`#bankNotificationHistoryRows [data-live-key="receipt-${info.receiptId}"]`);
        const liveSelect = () => receipt.locator('select[name=qrId]');
        assert.equal(await liveSelect().locator('option').filter({ hasText: 'Đơn #' + info.secondWaitingOrderId }).count(), 1);
        let liveReadCount = 0;
        const countLive = request => { if (new URL(request.url()).pathname === '/admin/bank-notifications/live-state') liveReadCount++; };
        page.on('request', countLive);
        await page.evaluate(() => { window.__ui17LiveRows = [...document.querySelectorAll('[data-bank-live-region] > [data-live-key]')].map(node => ({ node, original: node.cloneNode(true), key: node.dataset.liveKey })); });
        const snapshot = page.waitForResponse(response => new URL(response.url()).pathname === '/admin/bank-notifications/live-state');
        await page.evaluate(() => window.dispatchEvent(new Event('online'))); const snapshotResponse = await snapshot; assert.equal(snapshotResponse.status(), 200);
        const snapshotHtml = await snapshotResponse.text(); await mobile.settle(page);
        const keyedRowChecks = await page.evaluate(html => {
            const incoming = new DOMParser().parseFromString(html, 'text/html');
            return window.__ui17LiveRows.map(({ node, original, key }) => {
                const selector = `[data-bank-live-region] > [data-live-key="${key}"]`, equalToIncoming = original.isEqualNode(incoming.querySelector(selector));
                return { key, equalToIncoming, retained: node.isConnected && document.querySelector(selector) === node };
            });
        }, snapshotHtml);
        assert.ok(keyedRowChecks.every(row => !row.equalToIncoming || row.retained), 'Rows equal to the real incoming snapshot retain native identity; fresh antiforgery tokens may legitimately replace other rows');
        await page.locator('#bridgeName').fill('UI17 unsaved phone name');
        await receipt.locator('details').evaluate(node => { node.open = true; }); await liveSelect().selectOption(String(info.secondQrId));
        await page.locator('#bridgeName').focus();
        const changedSnapshot = page.waitForResponse(response => new URL(response.url()).pathname === '/admin/bank-notifications/live-state');
        await page.evaluate(() => window.dispatchEvent(new Event('online'))); assert.equal((await changedSnapshot).status(), 200); await mobile.settle(page);
        assert.equal(await page.locator('#bridgeName').inputValue(), 'UI17 unsaved phone name');
        assert.equal(await receipt.locator('details').evaluate(node => node.open), true);
        assert.equal(await liveSelect().inputValue(), String(info.secondQrId));
        await liveSelect().focus();
        const readsBeforePause = liveReadCount;
        await page.evaluate(() => window.dispatchEvent(new Event('online'))); await page.waitForTimeout(250);
        assert.equal(liveReadCount, readsBeforePause, 'Focused target select pauses original live replacement');
        assert.equal(await liveSelect().evaluate(node => node === document.activeElement), true);
        await review('bank-native-notifications-live', liveRoot);
        await receipt.locator('form[data-bank-approval] button[type=submit]').click(); await page.locator('#adminBankApprovalModal.show').waitFor();
        assert.equal((await page.locator('#adminApprovalTarget').innerText()).endsWith('#' + info.secondWaitingOrderId), true);
        assert.match(await page.locator('#adminApprovalAmount').innerText(), /10[.,]000/);
        const reviewedTarget = await page.locator('#adminApprovalTarget').innerText(), readsDuringModal = liveReadCount;
        await page.evaluate(() => window.dispatchEvent(new Event('online'))); await page.waitForTimeout(250);
        assert.equal(await page.locator('#adminApprovalTarget').innerText(), reviewedTarget); assert.equal(liveReadCount, readsDuringModal);
        await mobile.dialog(page, '#adminBankApprovalModal', 'bank-native-notification-approval-cancel', info.output);
        await page.locator('#adminApprovalCancel').click(); await page.locator('#adminBankApprovalModal').waitFor({ state: 'hidden' });
        liveChecks = { unchangedKeyedRowsRetained: true, managerKeyedRowChecks: keyedRowChecks, managerEqualRows: keyedRowChecks.filter(row => row.equalToIncoming).length, configurationDraftPreserved: true, expandedEvidencePreserved: true,
            selectedQrId: info.secondQrId, selectedOrderId: info.secondWaitingOrderId, focusedSelectPausesRefresh: true, modalTargetFrozen: true, approvalSubmitted: false, nativeGetSnapshots: liveReadCount };
        page.off('request', countLive);
        observations.push('Bank notifications retain original keyed rows, draft device controls, expanded evidence and chosen second QR through native GET snapshots. Focused selection and the native approval modal pause replacement; approval is opened for review and cancelled without confirm.');

        await visit('/Admin/StoreBankAccounts', '[data-store-bank-account-index]'); await banksReady();
        assert.ok(await page.locator(`#storeBankMobileList [data-bank-quick="${info.bankId}"]`).count() > 0);
        await review('bank-native-store-bank-accounts', '[data-store-bank-account-index]');
        await page.locator('#storeBankOpenMobileFilters').click(); await page.locator('#storeBankMobileFilterSheet.show').waitFor();
        await page.locator('#storeBankMobileLifecycle').selectOption('active');
        await mobile.dialog(page, '#storeBankMobileFilterSheet', 'bank-native-account-mobile-filters', info.output);
        const filtered = page.waitForResponse(response => new URL(response.url()).pathname === '/admin/store-bank-accounts/data');
        await page.locator('#storeBankMobileApplyFilters').click(); assert.equal((await filtered).status(), 200);
        await page.locator('#storeBankMobileFilterSheet').waitFor({ state: 'hidden' }); await banksReady();
        assert.equal(await page.locator('#storeBankLifecycle').inputValue(), 'active');
        await review('bank-native-store-bank-accounts-filtered', '[data-store-bank-account-index]');
        await page.locator(`#storeBankMobileList [data-bank-quick="${info.bankId}"]`).first().click(); await page.locator('#storeBankQuickView.show').waitFor();
        await mobile.dialog(page, '#storeBankQuickView', 'bank-native-account-quick-view', info.output); await closeModal('#storeBankQuickView');
        const legacy = await manager.context.request.get(info.baseUrl + '/Admin/StoreBankAccounts/Search?search=ACB&page=1&pageSize=20');
        assert.equal(legacy.status(), 200); assert.match(await legacy.text(), /<table/); assert.ok(!(await legacy.text()).includes(info.canary));
        await visit('/Admin/StoreBankAccounts/Edit', 'form[action*="/storebankaccounts/edit" i]');
        await page.locator('#BankCode').fill('ACB'); await page.locator('#BankName').fill('UI17 unsaved draft');
        await page.locator('#AccountNumber').fill('UI17-NOT-SAVED'); await page.locator('#AccountName').fill('UI17 DRAFT ONLY');
        await review('bank-native-store-bank-create-draft', '.row.justify-content-center');
        await page.locator('form[action*="/storebankaccounts/edit" i] a').filter({ hasText: 'Hủy bỏ' }).click(); await banksReady();
        await visit('/Admin/StoreBankAccounts/Edit?id=' + info.bankId, 'form[action*="/storebankaccounts/edit" i]');
        assert.equal(await page.locator('#Id').inputValue(), String(info.bankId)); await page.locator('#BankName').fill('UI17 local edit draft only');
        await review('bank-native-store-bank-edit-draft', '.row.justify-content-center');
        await page.locator('form[action*="/storebankaccounts/edit" i] a').filter({ hasText: 'Hủy bỏ' }).click(); await banksReady();
        observations.push('Native account filters, quick view, compatibility Search partial and Create/Edit drafts preserve original owners. Both drafts are cancelled via the original link; no status/default/save command is clicked.');

        await visit('/admin/acb/settings', '#ClientSecret');
        assert.equal(await page.locator('#ClientSecret').inputValue(), ''); assert.equal(await page.locator('#CallbackApiKey').inputValue(), '');
        assert.equal(await page.locator('#ClientSecret').getAttribute('type'), 'password'); assert.equal(await page.locator('#CallbackApiKey').getAttribute('type'), 'password');
        await page.locator('#MerchantId').fill('UI17-UNSAVED-DRAFT'); await page.locator('details').first().evaluate(node => { node.open = true; });
        await review('bank-native-acb-settings-draft', await nativeRoot(page.locator('#ClientSecret'), '.card'));
        await visit('/admin/acb/callback-routing', 'h4');
        const routingRoot = () => nativeRoot(page.locator('h4').filter({ hasText: 'Cửa hàng nhận callback từ URL cũ' }));
        assert.equal(await page.locator('select[name=TargetStoreId]').count(), 0, 'Owned host has no configured legacy host route');
        await review('bank-native-routing-current-empty', await routingRoot());
        await captured('bank-captured-routing-populated', '/admin/acb/callback-routing', info.captures.routingPopulated, null, async () => {
            assert.ok(await page.locator('select[name=TargetStoreId]').count() > 0); return routingRoot();
        });
        await captured('bank-captured-sandbox-result', '/admin/acb/settings', info.captures.sandboxResult, null, async () => {
            const heading = page.locator('h3').filter({ hasText: 'Kết quả kiểm thử ACB' }); assert.ok(await heading.count() > 0);
            const root = await nativeRoot(heading, '.card'); assert.ok(await page.locator(root).locator('tbody tr').count() > 0); return root;
        });
        observations.push('ACB settings keep stored secret and callback key inputs empty; only an unsaved MerchantId draft is inspected. Routing is empty in the real owned host; populated routing and SandboxResult are compiled native captures, with no save/test/QR/bank request.');
        await manager.context.close();

        const viewer = await login(info.viewer); page = viewer.page;
        await visit(orderRoute, '#acbLookup'); await lookupReady(); await review('bank-viewer-native-order-lookup', '#acbLookup');
        await visit('/admin/acb/reconciliation' + dates + '&source=ALL', '.acb-reconcile');
        assert.equal(await page.locator('.acb-reconcile form[action$="/retry"]').count(), 0); await review('bank-viewer-native-reconciliation', '.acb-reconcile');
        await visit('/admin/bank-notifications', '#bankNotificationLiveState');
        assert.equal(await page.locator('form[data-bank-approval]').count(), 0); assert.equal(await page.locator('form[action*="/revoke"],form[action*="/renew"]').count(), 0);
        await page.evaluate(() => { window.__ui17ViewerRows = [...document.querySelectorAll('[data-bank-live-region] > [data-live-key]')]; });
        const viewerSnapshot = page.waitForResponse(response => new URL(response.url()).pathname === '/admin/bank-notifications/live-state');
        await page.evaluate(() => window.dispatchEvent(new Event('online'))); assert.equal((await viewerSnapshot).status(), 200); await mobile.settle(page);
        const viewerRowIdentity = await page.evaluate(() => ({ count: window.__ui17ViewerRows.length, retained: window.__ui17ViewerRows.every(node => node.isConnected && document.querySelector(`[data-bank-live-region] > [data-live-key="${node.dataset.liveKey}"]`) === node) }));
        assert.ok(viewerRowIdentity.count > 0); assert.equal(viewerRowIdentity.retained, true, 'Actual read-only live rows retain identity without CSRF-bearing management forms');
        liveChecks.viewerUnchangedKeyedRows = viewerRowIdentity;
        await review('bank-viewer-native-notifications', await nativeRoot(page.locator('#bankNotificationLiveStatus')));
        await visit('/Admin/StoreBankAccounts', '[data-store-bank-account-index]'); await banksReady(); await review('bank-viewer-native-accounts', '[data-store-bank-account-index]');
        for (const route of ['/admin/acb/settings', '/admin/acb/callback-routing', '/Admin/StoreBankAccounts/Edit', '/Admin/StoreBankAccounts/Edit?id=' + info.bankId]) {
            const response = await viewer.context.request.get(info.baseUrl + route, { maxRedirects: 0 });
            assert.ok(response.status() === 403 || response.status() === 302 && /access-?denied/i.test(response.headers().location || ''), 'Server denies ungranted management route ' + route);
            permissionChecks.push({ route, status: response.status(), denied: true });
        }
        observations.push('BankAccount.View and Pos.Payment.View retain original lookup, reconciliation, notification and account reads. Native partials hide admin approval/revoke/renew; server gates reject all tested manage/configuration routes.');
        await viewer.context.close();

        assert.deepEqual(unexpectedWrites, []); assert.deepEqual(forbiddenReads, []); assert.deepEqual(bankRequests, []); assert.deepEqual(errors, []);
        assert.deepEqual(reviewedStates.slice().sort(), expectedLabels.slice().sort()); assert.equal(new Set(reviewedStates).size, expectedLabels.length);
        const matrix = JSON.parse(fs.readFileSync(path.join(info.output, 'mobile-review.json'), 'utf8'));
        assert.equal(matrix.cases.length, expectedLabels.length); assert.equal(matrix.viewports.length, expectedLabels.length * 7);
        assert.equal(matrix.desktopProofs.length, expectedLabels.length * 2); assert.equal(matrix.printProofs.length, expectedLabels.length);
        assert.equal(matrix.dialogs.length, 12); assert.equal(matrix.failures?.length || 0, 0); assert.equal(matrix.fieldChanges?.length || 0, 0);
        const result = { passed: true, reviewedStates, stateProvenance, expectedLabels, observations, liveChecks, permissionChecks, errors, unexpectedWrites, forbiddenReads, bankRequests,
            existingChromeWindowsUntouched: true, freshHeadlessContexts: true, standardAssetNegotiation: !process.env.GAO_UI17_RAW_ASSETS,
            noBankPaymentConfigurationOrRoutingWrites: true, noBankNetwork: true, credentialCanaryAbsent: true, capturedNativeViewStates: stateProvenance.filter(state => state.provenance.startsWith('Compiled')),
            controlledReadStates: stateProvenance.filter(state => /^(?:Synthetic|Held)/.test(state.provenance)) };
        fs.writeFileSync(path.join(info.output, 'browser-checks.json'), JSON.stringify(result, null, 2) + '\n');
        console.log(JSON.stringify({ passed: true, reviewedStates: reviewedStates.length, phoneCases: matrix.viewports.length, desktopProofs: matrix.desktopProofs.length, printProofs: matrix.printProofs.length, dialogs: matrix.dialogs.length, liveChecks }));
    } catch (error) {
        console.error(error.stack || error);
        if (!credentialLeak && page && !page.isClosed()) await page.screenshot({ path: path.join(info.output, 'browser-failure.png'), fullPage: true, animations: 'disabled' }).catch(() => {});
        fs.writeFileSync(path.join(info.output, 'browser-failure.json'), JSON.stringify({ error: String(error), url: page && !page.isClosed() ? page.url() : null, errors, unexpectedWrites, forbiddenReads, bankRequests, reviewedStates, stateProvenance, observations, liveChecks }, null, 2) + '\n'); process.exitCode = 1;
    } finally { await browser.close(); }
})();
