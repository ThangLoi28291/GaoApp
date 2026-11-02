'use strict';
// UI-16 observes native, read-only invoice workflows in an owned headless context.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const mobile = require('./invoice-mobile-review.cjs');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let input = ''; process.stdin.on('data', chunk => input += chunk); process.stdin.on('end', () => resolve(input));
    }));
    fs.mkdirSync(info.output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const errors = [], observations = [], unexpectedWrites = [], forbiddenReads = [], providerRequests = [], reviewedStates = [], permissionChecks = [], hiddenTableChecks = [];
    const origin = new URL(info.baseUrl).origin;
    let page, inlineNumericValues, stockFixtureValues, credentialLeak = false;
    const forbiddenCommands = /^\/Admin\/(?:Invoice\/(?:ViettelDraftPreview|ViettelIssue|ViewOfficialPdf|DownloadOfficial|DownloadSaved|IssueViettel|PreviewViettel|DownloadViettel|SendViettel|RetryViettel|SyncViettel|RenderViettel)|InvoiceProviderSetting\/TestLogin)/i;
    try {
        async function login(account) {
            const context = await browser.newContext({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block' });
            await mobile.protect(context, info.baseUrl);
            const current = await context.newPage(); current.setDefaultTimeout(30000);
            current.on('pageerror', error => errors.push(error.message));
            current.on('dialog', async dialog => { errors.push('Unexpected native dialog: ' + dialog.type()); await dialog.dismiss(); });
            current.on('request', request => {
                const address = new URL(request.url());
                if (address.origin !== origin && /viettel|sinvoice/i.test(address.hostname)) providerRequests.push({ method: request.method(), hostname: address.hostname });
            });
            await current.goto(info.baseUrl + '/admin/account/login?returnUrl=%2FAdmin%2FInvoiceDashboard');
            await current.locator('[name=UserName]').fill(account.user); await current.locator('[name=Password]').fill(account.password);
            if (await current.locator('[name=SelectedTerminalId]').count()) await current.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
            await Promise.all([current.waitForURL(url => !url.pathname.toLowerCase().endsWith('/login')), current.locator('#loginForm button[type=submit]').click()]);
            await context.route('**/*', async route => {
                const request = route.request(), address = new URL(request.url());
                if (address.origin !== origin) return route.fallback();
                if (['GET', 'HEAD', 'OPTIONS'].includes(request.method())) {
                    if (forbiddenCommands.test(address.pathname)) {
                        forbiddenReads.push({ method: request.method(), pathname: address.pathname });
                        return route.abort('blockedbyclient');
                    }
                    return route.fallback();
                }
                if (/^\/admin\/api\/store-activity\/(?:online|presence)$/.test(address.pathname)) return route.fallback();
                unexpectedWrites.push({ method: request.method(), pathname: address.pathname });
                return route.abort('blockedbyclient');
            });
            return { context, page: current };
        }
        const noCanary = async label => {
            assert.ok(info.canary && typeof info.canary === 'string', 'A credential canary is required');
            credentialLeak = (await page.content()).includes(info.canary);
            assert.equal(credentialLeak, false, label + ' must not expose a credential canary');
        };
        const visit = async (route, root) => {
            const response = await page.goto(info.baseUrl + route); assert.equal(response.status(), 200, route);
            await page.locator(root).first().waitFor(); await mobile.settle(page); await noCanary(route);
        };
        const review = async (label, root) => {
            await mobile.review(page, label, info.output, { root }); reviewedStates.push(label);
            // Shallow, field-free probes exercise native hidden semantics without cloning controls or changing their owners.
            const hiddenChecks = await page.evaluate(async selector => {
                const table = [...document.querySelector(selector).querySelectorAll('table')].find(node => node.getClientRects().length && node.tBodies[0]?.rows[0]?.cells[0]);
                if (!table) return [];
                const body = table.tBodies[0], row = body.rows[0], cell = row.cells[0], probes = [];
                const shallow = node => {
                    const clone = node.cloneNode(false);
                    for (const attribute of [...clone.attributes]) {
                        if (attribute.name === 'class' || attribute.name === 'data-label' || /^data-invoice-(?:workspace-phone|ops|integration-phone)/.test(attribute.name)) continue;
                        clone.removeAttribute(attribute.name);
                    }
                    return clone;
                };
                try {
                    for (const tag of ['tbody', 'tr', 'td']) for (const mechanism of ['hidden', 'd-none']) {
                        const probeRow = shallow(row), probeCell = shallow(cell); probeCell.textContent = 'UI16 temporary hidden probe'; probeRow.append(probeCell);
                        let target, removal;
                        if (tag === 'tbody') { target = shallow(body); target.append(probeRow); removal = target; }
                        else { target = tag === 'tr' ? probeRow : probeCell; removal = probeRow; }
                        if (mechanism === 'hidden') target.hidden = true; else target.classList.add('d-none');
                        (tag === 'tbody' ? table : body).append(removal); probes.push({ tag, mechanism, target, removal });
                    }
                    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                    return probes.map(({ tag, mechanism, target }) => ({ tag, mechanism, hidden: getComputedStyle(target).display === 'none' && target.getClientRects().length === 0 }));
                } finally { for (const probe of probes) probe.removal.remove(); }
            }, root);
            assert.ok(hiddenChecks.every(check => check.hidden), JSON.stringify({ label, hiddenChecks }));
            if (hiddenChecks.length) hiddenTableChecks.push({ label, checks: hiddenChecks });
            await mobile.settle(page);
            await noCanary(label);
        };
        // Native containers lack IDs; derive their original CSS path without adding an attribute.
        const nativeContainer = anchor => page.locator(anchor).first().evaluate(node => {
            const root = node.closest('.container-fluid'); if (!root) throw new Error('Native container not found');
            const parts = [];
            for (let current = root; current && current !== document.documentElement; current = current.parentElement) {
                const tag = current.tagName.toLowerCase(), siblings = [...(current.parentElement?.children || [])].filter(sibling => sibling.tagName === current.tagName);
                parts.unshift(tag + ':nth-of-type(' + (siblings.indexOf(current) + 1) + ')');
            }
            return 'html > ' + parts.join(' > ');
        });
        const closeModal = async selector => {
            await page.locator(selector + ' [data-bs-dismiss="modal"]').first().click();
            await page.locator(selector).waitFor({ state: 'hidden' }); await mobile.settle(page);
        };
        const stockReady = async () => {
            await page.waitForFunction(() => document.getElementById('xsResults')?.getAttribute('aria-busy') === 'false');
            assert.equal(await page.locator('#xsResults [data-retry]').count(), 0, 'Native stock GET must succeed');
            await mobile.settle(page);
        };
        const manager = await login(info.manager); page = manager.page;

        await visit('/Admin/Invoice/Index', '.invoice-index');
        assert.ok(await page.locator(`.invoice-reference[href$="/${info.invoiceId}"]`).count() > 0, 'Owned local invoice is present');
        await review('invoice-native-index', '.invoice-index');
        await page.locator('[data-bs-target="#invoiceOrderDraftModal"]').click();
        await page.locator('#invoiceOrderDraftModal.show').waitFor();
        const orderSearch = page.waitForResponse(response => new URL(response.url()).pathname.toLowerCase() === '/admin/invoice/searchordersfordraft' && response.request().method() === 'GET');
        await page.locator('#invoiceOrderDraftTerm').fill(String(info.draftOrderId));
        await page.locator('#invoiceOrderDraftSearch button[type=submit]').click();
        assert.equal((await orderSearch).status(), 200);
        const draftOrder = page.locator(`#invoiceOrderDraftResults [data-order-id="${info.draftOrderId}"]`); await draftOrder.waitFor();
        const preview = page.waitForResponse(response => new URL(response.url()).pathname.toLowerCase() === '/admin/invoice/previeworderdraft' && response.request().method() === 'GET');
        await draftOrder.click(); assert.equal((await preview).status(), 200);
        await page.locator('#invoiceOrderDraftSelection').waitFor();
        await page.waitForFunction(() => document.getElementById('invoiceOrderDraftCreate')?.disabled === false);
        assert.match(await page.locator('#invoiceOrderDraftSelection').innerText(), new RegExp(String(info.draftOrderId)));
        await mobile.dialog(page, '#invoiceOrderDraftModal', 'invoice-native-pos-order-preview-cancel', info.output);
        await closeModal('#invoiceOrderDraftModal');
        observations.push('Native POS order search and GET draft preview select the owned completed order; the create button is never clicked and the modal is cancelled.');

        await visit('/Admin/Invoice/Detail/' + info.invoiceId, '#invoiceDetailPage');
        await page.waitForFunction(() => !document.getElementById('invoiceStockStatus')?.textContent.includes('Đang kiểm tra'));
        await review('invoice-native-local-detail', '#invoiceDetailPage');
        const manualRow = page.locator(`#manualLinesBox tr[data-manual-detail-id="${info.manualLineId}"]`);
        inlineNumericValues = await manualRow.evaluate(node => ({ quantity: Number(node.dataset.manualQuantity), unitPrice: Number(node.dataset.manualUnitPrice), vatRate: Number(node.dataset.manualVatRate), rawFinite: [node.dataset.manualQuantity, node.dataset.manualUnitPrice, node.dataset.manualVatRate].every(value => value !== '' && Number.isFinite(Number(value))) }));
        assert.deepEqual(inlineNumericValues, { quantity: 1.25, unitPrice: 8000, vatRate: 8, rawFinite: true });
        await manualRow.locator('[data-manual-display="quantity"]').dblclick();
        await manualRow.locator('[data-manual-edit-field="quantity"]').waitFor();
        for (const [field, expected] of Object.entries({ quantity: 1.25, unitPrice: 8000, vatRate: 8 })) {
            assert.equal(Number(await manualRow.locator(`[data-manual-edit-field="${field}"]`).inputValue()), expected, 'Native inline ' + field + ' is invariant and finite');
        }
        await review('invoice-native-fractional-inline-edit-cancel', '#invoiceDetailPage');
        await manualRow.locator('[data-cancel-manual-edit-id]').click();
        assert.equal(await manualRow.locator('[data-manual-edit-field]').count(), 0);
        assert.match(await manualRow.innerText(), /10[.,]800/);
        await page.locator('#btnOpenManualDetailModal').click(); await page.locator('#manualDetailModal.show #manualDetailForm').waitFor();
        await page.locator('#manualItemName').fill('UI16 draft only'); await page.locator('#manualUnitName').fill('kg');
        await page.locator('#manualQuantity').fill('1.25'); await page.locator('#manualUnitPrice').fill('8000'); await page.locator('#manualVatRate').fill('8');
        for (const [selector, value] of [['#manualQuantity', '1.25'], ['#manualUnitPrice', '8000'], ['#manualVatRate', '8']]) {
            assert.equal(await page.locator(selector).inputValue(), value); assert.equal(await page.locator(selector).evaluate(node => node.validity.valid), true);
        }
        await mobile.dialog(page, '#manualDetailModal', 'invoice-native-manual-fractional-draft-cancel', info.output); await closeModal('#manualDetailModal');
        await page.locator('#btnOpenBuyerInfoModal').click(); await page.locator('#buyerInfoModal.show').waitFor();
        await page.locator('#buyerInfoForm [name=BuyerType][value=Business]').check();
        await page.locator('#buyerLegalName').fill('UI16 buyer draft only'); await page.locator('#buyerAddress').fill('Native form retained during resize');
        await mobile.dialog(page, '#buyerInfoModal', 'invoice-native-buyer-business-draft-cancel', info.output); await closeModal('#buyerInfoModal');
        observations.push('Native manual row raw attributes and numeric editor preserve quantity 1.25, unit price 8,000 and VAT 8; total remains 10,800 after cancel. New manual and buyer forms are inspected as local drafts, then cancelled without save or lookup.');

        await visit('/Admin/Invoice/Detail/' + info.issuedInvoiceId, '#invoiceDetailPage');
        assert.equal(await page.locator('#btnOpenManualDetailModal,#btnOpenBuyerInfoModal,#manualDetailModal,#buyerInfoModal').count(), 0, 'Issued invoice retains native hard lock');
        assert.ok(await page.locator('a[href*="InvoiceCorrection/Create"]').count() >= 1, 'ADMIN retains native correction routes');
        await review('invoice-native-issued-locked-detail', '#invoiceDetailPage');
        observations.push('Issued invoice keeps original lock semantics: edit/manual modal controls remain absent and existing correction links remain available only to the manager. Correction history uses the original inline table when present.');

        await visit('/Admin/InvoiceDashboard/Index', 'form[action*="InvoiceDashboard" i]');
        const dashboardRoot = await nativeContainer('form[action*="InvoiceDashboard" i]');
        assert.equal((await page.locator(dashboardRoot).innerText()).includes(':dd/MM/yyyy'), false, 'Native metric period is formatted');
        await review('invoice-native-dashboard', dashboardRoot);

        await visit(`/Admin/InvoiceCorrection/Create?originalInvoiceHeadId=${info.issuedInvoiceId}&type=1`, 'form:has(input[name="OriginalInvoiceHeadId"])');
        assert.equal(await page.locator('[name=OriginalInvoiceHeadId]').inputValue(), String(info.issuedInvoiceId));
        await page.locator('[name=Reason]').fill('UI16 correction draft only');
        await page.locator('[name=AgreementDocumentNo]').fill('UI16-NOT-SUBMITTED');
        await page.locator('[name=Note]').fill('Original issued invoice retained; no correction is created.');
        await review('invoice-native-replacement-correction-draft', await nativeContainer('form:has(input[name="OriginalInvoiceHeadId"])'));

        await visit('/admin/invoice-input-stock', '[data-xml-stock]'); await stockReady();
        const balanceResponse = await manager.context.request.get(info.baseUrl + '/admin/invoice-input-stock/data?view=balance&page=1&pageSize=20');
        assert.equal(balanceResponse.status(), 200); const balancePayload = await balanceResponse.json();
        const fixtureBalanceIndex = (balancePayload.balances || []).findIndex(row => Number(row.opening) === 20 && Number(row.issued) === 2 && Number(row.remaining) === 18);
        assert.ok(fixtureBalanceIndex >= 0, 'Seeded documentary opening 20 minus issued 2 must yield native remaining 18');
        const fixtureBalance = balancePayload.balances[fixtureBalanceIndex];
        stockFixtureValues = { opening: fixtureBalance.opening, issued: fixtureBalance.issued, remaining: fixtureBalance.remaining, held: fixtureBalance.held, available: fixtureBalance.available };
        assert.equal(Number(fixtureBalance.available), 18 - Number(fixtureBalance.held), 'Native availability retains its original held-quantity rule');
        assert.equal(await page.locator('#xsBody tr[data-index]').count(), balancePayload.balances.length, 'Native balance render uses the original GET rows');
        await review('invoice-native-input-stock-balances', '[data-xml-stock]');
        let stockPreviewCount = 0;
        const balancePreview = page.locator(`#xsBody [data-preview="${fixtureBalanceIndex}"]`);
        await balancePreview.waitFor();
        assert.equal(await balancePreview.evaluate(node => { const box = node.getBoundingClientRect(); return box.width >= 44 && box.height >= 44; }), true, 'Native stock preview touch target is at least 44 by 44');
        {
            await balancePreview.click(); await page.locator('#xsDialog[open]').waitFor();
            assert.equal(await page.locator('#xsDialogTitle').innerText(), fixtureBalance.productName, 'Original native preview shows the seeded product');
            await mobile.dialog(page, '#xsDialog', 'invoice-native-input-stock-balance-preview', info.output);
            await page.locator('#xsClose').click(); stockPreviewCount++;
        }
        await page.locator('button[data-view=ledger]').click(); await stockReady();
        const ledgerResponse = await manager.context.request.get(info.baseUrl + '/admin/invoice-input-stock/data?view=ledger&page=1&pageSize=20');
        assert.equal(ledgerResponse.status(), 200); const ledgerPayload = await ledgerResponse.json();
        assert.ok((ledgerPayload.movements || []).some(row => row.legacySourceKey === 'UI16-DOCUMENTARY-OPENING' && Number(row.change) === 20 && row.isOpening === true), 'Native ledger retains the seeded documentary opening');
        assert.ok(ledgerPayload.movements.some(row => Number(row.invoiceHeadId) === Number(info.issuedInvoiceId) && Number(row.change) === -2), 'Native ledger retains the issued-invoice decrease');
        assert.equal(await page.locator('#xsBody tr[data-index]').count(), ledgerPayload.movements.length, 'Native ledger render uses the original GET rows');
        await review('invoice-native-input-stock-ledger', '[data-xml-stock]');
        assert.ok(await page.locator('#xsBody [data-preview]').count() > 0, 'Seeded ledger must offer a native preview');
        assert.equal(await page.locator('#xsBody [data-preview]').first().evaluate(node => { const box = node.getBoundingClientRect(); return box.width >= 44 && box.height >= 44; }), true, 'Native ledger preview touch target is at least 44 by 44');
        {
            await page.locator('#xsBody [data-preview]').first().click(); await page.locator('#xsDialog[open]').waitFor();
            await mobile.dialog(page, '#xsDialog', 'invoice-native-input-stock-ledger-preview', info.output);
            await page.locator('#xsClose').click(); stockPreviewCount++;
        }
        if (await page.locator('#xsBackfillOpen').count()) {
            await page.locator('#xsBackfillOpen').click(); await page.locator('#xsBackfillDialog[open]').waitFor();
            assert.equal(await page.locator('#xsBackfillConfirm').isDisabled(), true);
            await mobile.dialog(page, '#xsBackfillDialog', 'invoice-native-input-stock-backfill-draft-cancel', info.output);
            await page.locator('#xsBackfillClose').click();
        }
        assert.equal(stockPreviewCount, 2);
        observations.push('Native stock balance and ledger GETs preserve documentary opening 20 minus issued 2 = remaining 18 and original held-quantity availability. Both seeded native row previews open and close, with 44 by 44 touch targets. Backfill confirmation is never submitted.');

        for (const tab of ['today', 'waiting', 'single', 'groups', 'errors', 'history', 'old', 'settings']) {
            await visit('/Admin/AutoInvoice/Index?tab=' + tab, '.auto-card');
            const autoRoot = await nativeContainer('.auto-card');
            assert.match(await page.locator('.auto-health').innerText(), /Tạm ngưng/, 'Owned fixture auto issuance remains paused');
            if (tab === 'settings') {
                const settings = page.locator('form[action*="SaveSettings" i]');
                assert.equal(await settings.locator('[name=IsEnabled][type=checkbox]').isChecked(), false);
                for (const name of ['MinimumAgeMinutes', 'SeparateAmountThreshold', 'GroupTargetAmount', 'SendIntervalSeconds', 'ClosingTimeLocal', 'TimeZoneId', 'ScopeMode', 'ScopeStartDateLocal', 'ScopeEndDateLocal', 'IssueOldDayRemainder']) {
                    assert.ok(await settings.locator(`[name="${name}"]`).count() > 0, 'Native auto setting ' + name);
                }
            }
            await review('invoice-native-auto-' + tab, autoRoot);
        }
        observations.push('All eight original AutoInvoice tab routes retain native queues, history, incidents and settings. The owned setting stays paused; no toggle, run, recheck or save is issued.');

        await visit('/Admin/InvoiceIntegrationLog/Index?InvoiceHeadId=' + info.invoiceId, '.log-page');
        assert.ok(await page.locator(`.log-page a[href$="/Detail/${info.logId}"]`).count() > 0, 'Owned integration log is in native invoice filter');
        await review('invoice-native-integration-logs-filtered', '.log-page');
        await visit('/Admin/InvoiceIntegrationLog/Detail/' + info.logId, '.log-detail-page');
        assert.equal(JSON.parse(await page.locator('#requestBody').innerText()).redacted, true, 'Secret fields are redacted in original log read model');
        assert.ok(await page.locator('#responseBody').count() === 1);
        await review('invoice-native-integration-log-masked-detail', '.log-detail-page');
        await visit('/Admin/InvoiceIntegrationLog/Cleanup', '.cleanup-card');
        const cleanupRoot = await nativeContainer('.cleanup-card');
        assert.equal(await page.locator('button[name=submitMode][value=preview]').getAttribute('type'), 'submit');
        assert.equal(await page.locator('button[name=submitMode][value=execute]').getAttribute('type'), 'submit');
        await page.locator('[name=MaxRowsPerRun]').fill('25');
        await review('invoice-native-integration-cleanup-get-dry-run', cleanupRoot);
        observations.push('Native log filter/detail and Cleanup GET dry run preserve their original owners. Credential canary is absent from full HTML, request body is REDACTED, and neither POST preview nor cleanup execute is clicked.');

        await visit('/Admin/Invoice/ViettelDashboard', '.viettel-dashboard');
        await review('invoice-native-viettel-dashboard', '.viettel-dashboard');
        await visit('/Admin/Invoice/ViettelPayload/' + info.invoiceId, '.payload-page');
        assert.ok((await page.locator('#payloadJson').innerText()).trim().startsWith('{'), 'Native pure payload builder yields JSON');
        assert.equal(await page.locator('#previewPdfFrame').isVisible(), false, 'Remote PDF stays lazy and hidden');
        await review('invoice-native-viettel-local-payload', '.payload-page');
        await visit('/Admin/Invoice/ViettelPayload/' + info.issuedInvoiceId, '.payload-page');
        await review('invoice-native-viettel-issued-payload', '.payload-page');
        if (await page.locator('[data-bs-target="#sendViettelEmailModal"]').count()) {
            await page.locator('[data-bs-target="#sendViettelEmailModal"]').click();
            await page.locator('#sendViettelEmailModal.show').waitFor();
            await page.locator('#sendViettelEmailModal [name=BuyerEmail]').fill('ui16-draft@example.invalid');
            await mobile.dialog(page, '#sendViettelEmailModal', 'invoice-native-viettel-email-draft-cancel', info.output);
            await closeModal('#sendViettelEmailModal');
        }
        observations.push('Viettel dashboard and both local/issued payloads use pure GET builders only. Original lazy PDF remains hidden; no issuance, preview rendering, download, retry, sync or email request is made.');

        await visit('/Admin/InvoiceProviderSetting/Index', '.invoice-provider-page');
        assert.ok(await page.locator(`.invoice-provider-page a[href$="/Edit/${info.providerId}"]`).count() > 0);
        await review('invoice-native-provider-settings', '.invoice-provider-page');
        await visit('/Admin/InvoiceProviderSetting/Create', '.invoice-provider-form');
        assert.equal(await page.locator('#txtInvoiceProviderPassword').inputValue(), '');
        assert.equal(await page.locator('#txtInvoiceProviderPassword').getAttribute('type'), 'password');
        await review('invoice-native-provider-create-draft', '.invoice-provider-form');
        await visit('/Admin/InvoiceProviderSetting/Edit/' + info.providerId, '.invoice-provider-form');
        assert.equal(await page.locator('#Id').inputValue(), String(info.providerId));
        assert.equal(await page.locator('#txtInvoiceProviderPassword').inputValue(), '', 'Edit never returns existing credential');
        assert.equal(await page.locator('#txtInvoiceProviderPassword').getAttribute('type'), 'password');
        await review('invoice-native-provider-edit-empty-password', '.invoice-provider-form');
        await visit('/Admin/ViettelInvoiceSync/Index', 'form[action*="ViettelInvoiceSync" i]');
        assert.equal(await page.locator('#UpdateLocalInvoices').isChecked(), true, 'Native sync default is preserved without submission');
        assert.equal(await page.locator('[name=PageSize]').inputValue(), '100');
        await review('invoice-native-viettel-sync-get-draft', await nativeContainer('form[action*="ViettelInvoiceSync" i]'));
        observations.push('Provider list/Create/Edit retain native masked credential handling; edit password is empty. Sync retains original defaults, including UpdateLocalInvoices=true, while no configuration or sync form is submitted.');
        await manager.context.close();

        const viewer = await login(info.viewer); page = viewer.page;
        await visit('/Admin/InvoiceDashboard/Index', 'form[action*="InvoiceDashboard" i]');
        await review('invoice-viewer-native-dashboard', await nativeContainer('form[action*="InvoiceDashboard" i]'));
        await visit('/Admin/InvoiceProviderSetting/Index', '.invoice-provider-page');
        await review('invoice-viewer-native-provider-index', '.invoice-provider-page');
        await visit('/Admin/InvoiceIntegrationLog/Index?InvoiceHeadId=' + info.invoiceId, '.log-page');
        await review('invoice-viewer-native-log-index', '.log-page');
        await visit('/Admin/InvoiceIntegrationLog/Detail/' + info.logId, '.log-detail-page');
        assert.equal(JSON.parse(await page.locator('#requestBody').innerText()).redacted, true);
        await review('invoice-viewer-native-masked-log-detail', '.log-detail-page');
        await visit('/admin/invoice-input-stock?view=ledger', '[data-xml-stock]'); await stockReady();
        const stockResponse = await viewer.context.request.get(info.baseUrl + '/admin/invoice-input-stock/data?view=ledger&page=1&pageSize=20');
        assert.equal(stockResponse.status(), 200);
        const stockData = await stockResponse.json();
        assert.ok(Array.isArray(stockData.movements), 'Native inventory permission retains the original ledger read model');
        assert.equal(await page.locator('#xsBackfillOpen').count(), 0, 'Read-only viewer cannot backfill stock');
        await review('invoice-viewer-native-stock-read-permission', '[data-xml-stock]');
        for (const route of ['/Admin/Invoice/Index', '/Admin/Invoice/Detail/' + info.invoiceId, '/Admin/Invoice/ViettelDashboard', '/Admin/Invoice/ViettelPayload/' + info.invoiceId, '/Admin/InvoiceProviderSetting/Create', '/Admin/InvoiceProviderSetting/Edit/' + info.providerId, '/Admin/ViettelInvoiceSync/Index', '/Admin/InvoiceIntegrationLog/Cleanup', '/Admin/AutoInvoice/Index', `/Admin/InvoiceCorrection/Create?originalInvoiceHeadId=${info.issuedInvoiceId}&type=1`]) {
            const response = await viewer.context.request.get(info.baseUrl + route, { maxRedirects: 0 });
            const location = response.headers().location || '';
            assert.ok(response.status() === 403 || (response.status() === 302 && /access-?denied/i.test(location)), 'Server denies ungranted invoice action: ' + route);
            permissionChecks.push({ route, status: response.status(), denied: true });
        }
        observations.push('Read-only Integration.View and Inventory.Transaction.View user retains the original dashboard, log, provider list and stock read models. Server authorization denies all tested manage, settings and correction routes.');
        await viewer.context.close();
        assert.deepEqual(unexpectedWrites, [], 'No invoice, stock, log, configuration or provider command may be submitted');
        assert.deepEqual(forbiddenReads, [], 'No provider command GET may be requested');
        assert.deepEqual(providerRequests, [], 'No remote provider may be contacted');
        assert.deepEqual(errors, [], 'Original native owners must produce no JavaScript errors');
        const matrix = JSON.parse(fs.readFileSync(path.join(info.output, 'mobile-review.json'), 'utf8'));
        assert.equal(reviewedStates.length, 31, 'All UI-16 native review states are mandatory');
        assert.equal(matrix.cases.length, reviewedStates.length, 'Every state must execute its presentation checks');
        assert.equal(matrix.viewports.length, reviewedStates.length * 7, 'Every state must execute seven phone viewports');
        assert.equal(matrix.desktopProofs.length, reviewedStates.length * 2, 'Every state must execute both desktop comparisons');
        assert.equal(matrix.printProofs.length, reviewedStates.length, 'Every state must execute its print comparison');
        assert.equal(matrix.failures?.length || 0, 0);
        const result = { passed: true, observations, reviewedStates, inlineNumericValues, stockFixtureValues, hiddenTableChecks, permissionChecks, errors, unexpectedWrites, forbiddenReads, providerRequests,
            existingChromeWindowsUntouched: true, freshHeadlessContexts: true, standardAssetNegotiation: !process.env.GAO_UI16_RAW_ASSETS,
            noInvoiceOrFinancialWrites: true, noProviderOrSettingsWrites: true, noProviderNetwork: true, credentialCanaryAbsent: true };
        fs.writeFileSync(path.join(info.output, 'browser-checks.json'), JSON.stringify(result, null, 2) + '\n');
        console.log(JSON.stringify({ passed: true, reviewedStates: reviewedStates.length, observations, inlineNumericValues }));
    } catch (error) {
        console.error(error.stack || error);
        if (!credentialLeak && page && !page.isClosed()) await page.screenshot({ path: path.join(info.output, 'browser-failure.png'), fullPage: true, animations: 'disabled' }).catch(() => {});
        fs.writeFileSync(path.join(info.output, 'browser-failure.json'), JSON.stringify({ error: String(error), url: page && !page.isClosed() ? page.url() : null, errors, unexpectedWrites, forbiddenReads, providerRequests, observations, reviewedStates, inlineNumericValues, stockFixtureValues, hiddenTableChecks }, null, 2) + '\n');
        process.exitCode = 1;
    } finally { await browser.close(); }
})();
