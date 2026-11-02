'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const ui11 = require('./receipt-mobile-review.cjs'); // UI-11 review hook

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let input = ''; process.stdin.on('data', chunk => input += chunk); process.stdin.on('end', () => resolve(input));
    }));
    fs.mkdirSync(info.evidenceDirectory, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    await ui11.protect(context, info.baseUrl); // UI-11 review hook
    const page = await context.newPage(), errors = [], checks = [], layouts = [], consoleErrors = [], serverFailures = [], snapshots = [];
    page.setDefaultTimeout(20000);
    page.on('pageerror', error => errors.push(error.message));
    const localOrigin = new URL(info.baseUrl).origin;
    page.on('console', message => {
        if (message.type() !== 'error') return;
        // HTTP errors are inspected by status below; deliberate validation failures are not JavaScript failures.
        if (/Failed to load resource:|net::ERR_ABORTED/.test(message.text())) return;
        const source = message.location().url;
        if (!source || source.startsWith(localOrigin)) consoleErrors.push({ text: message.text(), source });
    });
    page.on('response', response => {
        if (response.status() >= 500 && new URL(response.url()).origin === localOrigin)
            serverFailures.push({ status: response.status(), method: response.request().method(), url: response.url() });
    });
    const acceptDialog = dialog => dialog.accept();
    page.on('dialog', acceptDialog);
    const url = `${info.baseUrl}/admin/stock-documents/${info.receiptId}`;
    const apiUrl = `${info.baseUrl}/admin/api/stock-documents/${info.receiptId}`;
    const rows = () => page.locator('.commercial-line');
    const row = id => page.locator(`.commercial-line[data-line-id="${id}"]`);
    const filter = value => page.locator(`[data-commercial-filter="${value}"]`).first();
    const visibleRows = () => page.locator('.commercial-line:not(.d-none)');
    const round = value => Math.round((value + Number.EPSILON) * 100) / 100;
    async function saved() { await page.waitForFunction(() => !window.GaoReceiptPriceDrafts.hasUnsaved()); }
    async function refresh() { await page.evaluate(() => window.GaoReceiptApprovalWorkspace.refresh()); }
    async function count(kind) { return Number(await page.locator(`#receiptApproval${kind}Count`).textContent()); }
    async function shot(name, fullPage = false) {
        await page.screenshot({ path: path.join(info.evidenceDirectory, `${name}.png`), fullPage, animations: 'disabled' });
        snapshots.push({ name, viewport: page.viewportSize(), url: page.url() });
    }
    async function tab(name) { await page.locator(`[data-workbench-tab="${name}"]`).click(); }
    async function captureSection(name, selector) {
        await page.locator(selector).first().evaluate(el => el.scrollIntoView({ block: 'start', behavior: 'instant' }));
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${name} has no horizontal page overflow.`);
        const switches = await page.locator('.sd-paid-switch .form-check-input').evaluateAll(inputs => inputs.map(input => {
            const box = input.getBoundingClientRect(), wrapper = input.closest('.sd-paid-switch').getBoundingClientRect();
            return { id: input.id, visible: box.width > 0 && box.height > 0, contained: box.left >= wrapper.left && box.right <= wrapper.right, height: wrapper.height };
        }));
        assert.ok(switches.filter(input => input.visible).every(input => input.contained && input.height >= 44), `${name} payment and freight switches stay inside their full touch targets: ${JSON.stringify(switches)}.`);
        await shot(name);
    }
    async function footerIsOperable(width) {
        await page.locator('.sd-commercial-sticky-actions').scrollIntoViewIfNeeded();
        const results = await page.locator('#commercialApproveButton, #commercialRejectButton').evaluateAll(buttons => buttons.map(button => {
            const r = button.getBoundingClientRect(), hit = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2);
            return { id: button.id, x: r.x, y: r.y, right: r.right, bottom: r.bottom, coveredBy: hit?.className || hit?.id || '', operable: !!hit && (hit === button || button.contains(hit)) };
        }));
        assert.ok(results.every(button => button.operable), `Primary footer remains clickable, including beside floating navigation at ${width}: ${JSON.stringify(results)}`);
        await shot(`footer-navigation-${width}`);
    }
    async function setSupplier(value) {
        await page.evaluate(value => {
            const el = document.getElementById('commercialSupplierId');
            el.value = value;
            if (window.jQuery) jQuery(el).trigger('change'); else el.dispatchEvent(new Event('change', { bubbles: true }));
        }, value);
        await page.waitForFunction(() => !document.getElementById('commercialSupplierId').disabled);
        await saved(); await refresh();
    }
    async function issue(pattern) {
        await page.locator('#receiptApprovalIssuesDisclosure').evaluate(el => el.open = true);
        return page.locator('#receiptApprovalIssues button').filter({ hasText: pattern }).first();
    }
    async function money(id) {
        return page.locator('#' + id).evaluate(el => Number(el.textContent.replace(/[^\d,-]/g, '').replace(',', '.')) || 0);
    }
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        await page.goto(url);
        await page.waitForFunction(() => !!window.GaoReceiptApprovalWorkspace && !!window.GaoReceiptPriceDrafts);
        await page.locator('#priceReviewCounts .badge').first().waitFor({ state: 'attached' });
        await saved(); await refresh();
        const initialOrder = await rows().evaluateAll(elements => elements.map(el => Number(el.dataset.lineId)));
        await ui11.review(page, 'approval-goods', ui11.output('approval')); // UI-11 review hook
        await ui11.navigation(page, 'approval-native-sections', ui11.output('approval')); // UI-11 review hook
        assert.equal(initialOrder.length, 14);
        assert.equal(await page.locator('.sd-revision-line').count(), 3);
        assert.equal(await row(info.unchangedLineId).locator('.commercial-unit-price').inputValue(), String(info.savedPrice));
        assert.equal(await page.locator('#btnOpenApproveModal:visible, #btnOpenRejectModal:visible').count(), 0, 'One visible pair of main actions.');
        assert.equal(await page.locator('#commercialApproveButton:visible, #commercialRejectButton:visible').count(), 2);
        assert.equal(await page.locator('#commercialApprovalWorkbench').getAttribute('data-source'), 'direct');
        assert.equal(await page.locator('.commercial-line').getByText(/^(Trong PO|Ngoài PO)$/).count(), 0, 'Direct receipts never imply a PO allocation.');
        assert.equal(await count('Blocking'), 0);
        assert.ok(await count('Acknowledgement') > 0, 'Actual historical purchase triggers acknowledgement.');
        assert.ok(await count('Warning') > 0, 'Below-sale-margin loss is informational.');
        assert.equal(await page.locator('#commercialApproveButton').isEnabled(), true, 'Informational margin warnings do not block approval.');
        assert.equal(await page.locator('#commercialFreightTabTotal').isVisible(), false, 'No-freight receipt does not display an obsolete fee badge.');
        assert.match(await page.locator('#commercialFreightHeading').textContent(), /Không phát sinh/);
        checks.push('Real revised receipt has stable correction-first rows, one main action pair, correct direct-receipt labels and three explicit rule categories.');

        const requested = await page.evaluate(async endpoint => {
            const response = await fetch(endpoint + '/request-revision', {
                method: 'POST', headers: { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ rowVersion: window.stockDocumentPage.rowVersion, note: 'Đề nghị kiểm tra lại số lượng trước khi duyệt (kiểm thử)' })
            });
            return { status: response.status, body: await response.text() };
        }, apiUrl);
        assert.equal(requested.status, 200, requested.body);
        await page.reload(); await page.waitForFunction(() => !!window.GaoReceiptApprovalWorkspace && !!window.GaoReceiptPriceDrafts);
        await saved(); await refresh();
        assert.equal(await page.locator('#commercialApproveButton').isDisabled(), true, 'An unresolved request blocks approval.');
        await (await issue(/Giải quyết yêu cầu xin sửa/i)).click();
        assert.equal(await page.locator('#receiptRevisionRequest').evaluate(el => el === document.activeElement), true);
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 }); await captureSection(`revision-request-blocked-${width}`, '#receiptRevisionRequest');
        }
        await page.locator('#btnIgnoreRevisionRequest').click();
        await page.locator('#returnToEditModal').waitFor({ state: 'visible' });
        await page.locator('#btnConfirmReturnToEdit').click();
        assert.match(await page.locator('#returnToEditMessage').textContent(), /Nhập lý do/);
        assert.equal(await page.locator('#returnToEditNote').evaluate(el => el === document.activeElement), true);
        await shot('revision-request-reason-required-390');
        await page.locator('#returnToEditNote').fill('Đã kiểm đếm cùng nhân viên; số lượng hiện tại đúng, tiếp tục duyệt.');
        await Promise.all([page.waitForNavigation({ waitUntil: 'domcontentloaded' }), page.locator('#btnConfirmReturnToEdit').click()]);
        await page.waitForFunction(() => !!window.GaoReceiptApprovalWorkspace && !!window.GaoReceiptPriceDrafts);
        await saved(); await refresh();
        assert.equal(await page.locator('#receiptRevisionRequest').count(), 0);
        assert.equal(await count('Blocking'), 0);
        assert.equal(await page.locator('#commercialApproveButton').isEnabled(), true);
        await page.setViewportSize({ width: 1440, height: 900 });
        checks.push('A real staff revision request blocks approval and focuses its resolution controls on desktop/mobile; rejecting it requires an explicit reason, persists through HTTP, and restores readiness only after resolution.');

        for (const [width, height] of [[1920, 1080], [1440, 900], [1366, 768], [1024, 900], [768, 1000], [390, 900], [320, 900]]) {
            await page.setViewportSize({ width, height }); await page.evaluate(() => scrollTo(0, 0));
            await page.waitForTimeout(120);
            const result = await page.evaluate(() => {
                const bounds = selector => { const el = document.querySelector(selector), r = el?.getBoundingClientRect(); return r && { x: r.x, y: r.y, width: r.width, height: r.height, right: r.right, bottom: r.bottom }; };
                const box = el => { const r = el.getBoundingClientRect(); return { width: r.width, height: r.height, right: r.right, left: r.left }; };
                return { width: innerWidth, height: innerHeight, pageWidth: document.documentElement.scrollWidth,
                    goods: bounds('.sd-review-goods-toolbar'), firstRow: bounds('.commercial-line'), footer: bounds('.sd-commercial-sticky-actions'),
                    inputs: [...document.querySelectorAll('.commercial-unit-price')].map(box),
                    actions: [...document.querySelectorAll('#commercialApproveButton, #commercialRejectButton, #commercialNextIssue, #commercialLineSearch')].map(box),
                    overflow: [...document.querySelectorAll('.sd-review-workspace *')].filter(el => { const r = el.getBoundingClientRect(); return r.width && r.right > innerWidth + 2 && getComputedStyle(el).position !== 'fixed'; }).slice(0, 8).map(el => el.className)
                };
            });
            layouts.push(result);
            assert.ok(result.pageWidth <= width + 1, `No horizontal page overflow at ${width}: ${JSON.stringify(result)}`);
            if (width === 1440 || width === 1366) assert.ok(result.goods.y < result.footer.y, `Goods toolbar is visible upon opening at ${width}×${height}.`);
            if (width < 768) {
                assert.ok(result.inputs.every(b => b.left >= 0 && b.right <= width + 1 && b.width >= 80 && b.height >= 44), `Readable mobile prices at ${width}.`);
                assert.ok(result.actions.every(b => b.height >= 44), `Main mobile touch targets at least 44px at ${width}.`);
            }
            await shot(`workspace-${width}`);
            if (width < 768) { await rows().first().scrollIntoViewIfNeeded(); await shot(`goods-${width}`); }
        }
        checks.push('Compact real Razor layout fits seven widths from 320 to 1920; goods appear immediately on common desktops and mobile controls meet 44px targets.');

        // Walk the whole rendered workspace without saving a pricing plan or changing the receipt.
        const snapshotsStart = snapshots.length;
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            for (const name of ['goods', 'pricing', 'settlement', 'freight']) {
                await tab(name);
                assert.equal(await page.locator(`[data-workbench-tab="${name}"]`).getAttribute('aria-selected'), 'true');
                assert.equal(await page.locator(`[data-workbench-tab="${name}"]`).getAttribute('tabindex'), '0');
                assert.equal(await page.locator(`.sd-workbench-tab-panel:not([hidden]):not([data-workbench-panel="${name}"])`).count(), 0, 'Only the selected tab exposes its panels.');
                await captureSection(`tab-${name}-${width}`, `[data-workbench-panel="${name}"]`);
                if (name === 'pricing') {
                    if(width===1440)await ui11.review(page, 'approval-pricing', ui11.output('approval')); // UI-11 review hook
                    await page.locator('#receiptPricingAllocation').waitFor({ state: 'visible' });
                    for (const step of ['bill', 'program', 'price']) {
                        await page.locator(`[data-pricing-step="${step}"]`).first().click();
                        await page.locator(`[data-pricing-page="${step}"]`).waitFor({ state: 'visible' });
                        await captureSection(`pricing-step-${step}-${width}`, `[data-pricing-page="${step}"]`);
                    }
                    await page.locator('[data-pricing-step="bill"]').first().click();
                    assert.match(await page.locator('#receiptPricingAllocation').textContent(), /tùy chọn|thủ công/i);
                }
            }
            const financial = page.locator('.sd-review-financial-disclosure');
            await financial.evaluate(el => { el.open = false; });
            await financial.locator('summary').first().focus(); await page.keyboard.press('Enter');
            assert.equal(await financial.evaluate(el => el.open), true, 'Keyboard expands the financial summary.');
            await page.locator('.sd-review-money-breakdown').evaluate(el => { el.open = true; });
            await captureSection(`financial-expanded-${width}`, '.sd-review-financial-summary');
            assert.match(await financial.textContent(), /Tiền hàng|Giá trị nhập kho/i);
            assert.equal(await page.locator('#commercialOutstandingMerchandise').isVisible(), true);
            assert.equal(await page.locator('#commercialOutstandingFreight').isVisible(), true);
            await financial.locator('summary').first().focus(); await page.keyboard.press('Enter');
            assert.equal(await financial.evaluate(el => el.open), false, 'Keyboard collapses the financial summary.');
            await captureSection(`financial-collapsed-${width}`, '.sd-review-financial-summary');
            await financial.evaluate(el => { el.open = true; });
            await page.locator('.sd-review-money-breakdown').evaluate(el => { el.open = false; });
            await footerIsOperable(width);
            for (const selector of ['#receiptPriceReviewSummary', '#receiptInvoiceFollowUp', '#inputInvoicePickerSection']) {
                const disclosure = page.locator(selector).locator('xpath=ancestor-or-self::details[1]');
                await disclosure.locator('summary').first().click();
                assert.equal(await disclosure.evaluate(el => el.open), true);
                await captureSection(`support-${selector.slice(1)}-${width}`, selector);
                assert.ok(await disclosure.locator('summary').first().evaluate(el => el.getBoundingClientRect().width <= innerWidth + 1));
                await disclosure.locator('summary').first().click();
                assert.equal(await disclosure.evaluate(el => el.open), false);
            }
            await tab('goods');
            await page.locator('[data-workbench-tab="goods"]').focus();
            for (const name of ['pricing', 'settlement', 'freight', 'goods']) {
                await page.keyboard.press('ArrowRight');
                assert.equal(await page.locator(`[data-workbench-tab="${name}"]`).getAttribute('aria-selected'), 'true');
                assert.equal(await page.locator(`[data-workbench-tab="${name}"]`).evaluate(el => el === document.activeElement), true);
            }
            await page.keyboard.press('ArrowLeft');
            assert.equal(await page.locator('[data-workbench-tab="freight"]').getAttribute('aria-selected'), 'true', 'Left arrow wraps to the previous tab.');
            await tab('goods');
        }
        assert.ok(snapshots.length - snapshotsStart >= 24, 'The complete desktop/mobile walkthrough records all tab, pricing, money and support states.');
        checks.push('Complete 1440px/390px UI simulation covers all four tabs, three optional pricing steps, expanded/collapsed money and calculation details, selling-price/invoice/XML support panels, keyboard tab cycling and unobscured footer controls without applying a pricing plan.');
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            await page.locator('#commercialRejectButton').click();
            await page.locator('#rejectModal').waitFor({ state: 'visible' });
            await page.waitForFunction(() => document.activeElement?.id === 'rejectNote');
            if (width===390) await ui11.dialog(page, '#rejectModal', 'return-receipt', ui11.output('approval')); // UI-11 review hook
            assert.equal(await page.locator('#rejectNote').getAttribute('maxlength'), '1000');
            assert.match(await page.locator('#rejectModal').textContent(), /không tự hoàn tác giá bán/);
            await shot(`return-dialog-${width}`);
            const bounds = await page.locator('#rejectModal .modal-content').boundingBox();
            assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width + 1 && bounds.y >= 0 && bounds.y + bounds.height <= 901, `Return dialog fits ${width}.`);
            await page.keyboard.press('Escape'); await page.locator('#rejectModal').waitFor({ state: 'hidden' });
        }
        checks.push('Return dialog is readable on desktop/mobile, focuses the required reason, states the selling-price rule and cancels safely without returning the fixture receipt.');

        const more = page.locator('.sd-review-more'), menuSummary = more.locator('summary').first();
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            await menuSummary.click();
            assert.equal(await more.evaluate(el => el.open), true);
            await page.locator('#receiptReviewOtherActions [data-receipt-title]').waitFor({ state: 'visible' });
            const menu = await page.locator('#receiptReviewOtherActions').boundingBox();
            assert.ok(menu.x >= 0 && menu.x + menu.width <= width + 1, `Other actions menu fits ${width}.`);
            await shot(`other-actions-${width}`);
            await menuSummary.focus(); await page.keyboard.press('Escape');
            assert.equal(await more.evaluate(el => el.open), false, 'Escape closes the action menu.');
            await menuSummary.click(); await page.locator('#commercialLineSearch').click();
            assert.equal(await more.evaluate(el => el.open), false, 'Clicking outside closes the action menu.');
            await menuSummary.click(); await page.locator('#receiptReviewOtherActions [data-receipt-title]').click();
            await page.locator('#receiptDocumentModal').waitFor({ state: 'visible' });
            await page.locator('#receiptTitleSave').waitFor({ state: 'visible' });
            assert.equal(await more.evaluate(el => el.open), false, 'Choosing an action closes the action menu.');
            assert.equal(await page.locator('#receiptDocumentModal').evaluate(el => !!el.closest('.sd-review-header')), false, 'Dialogs are outside the header stacking/overflow context.');
            const modal = await page.locator('#receiptDocumentModal .modal-content').boundingBox();
            assert.ok(modal.x >= 0 && modal.x + modal.width <= width + 1 && modal.y >= 0 && modal.y + modal.height <= 901, `Rename dialog fits ${width}.`);
            await shot(`rename-dialog-${width}`);
            await page.keyboard.press('Escape'); await page.locator('#receiptDocumentModal').waitFor({ state: 'hidden' });
        }
        checks.push('Relocated action menu opens/closes by mouse and Escape, fits desktop/mobile and opens an unclipped rename dialog; cancellation changes no data.');

        await page.setViewportSize({ width: 1440, height: 900 }); await tab('goods');
        const search = page.locator('#commercialLineSearch');
        await search.fill('nuoc mam'); assert.equal(await visibleRows().count(), 1, 'Vietnamese search ignores accents.');
        await search.fill('UX-03'); assert.equal(await visibleRows().count(), 1, 'SKU search.');
        await search.fill('8930000000002'); assert.equal(await visibleRows().count(), 1, 'Barcode search.');
        await search.fill('khong-co-san-pham-nay'); assert.equal(await visibleRows().count(), 0);
        await page.locator('#commercialFilterEmpty').waitFor({ state: 'visible' });
        await page.locator('#commercialFilterEmpty [data-commercial-filter="all"]').click();
        assert.equal(await search.inputValue(), ''); assert.equal(await visibleRows().count(), 14);
        await filter('revision').focus(); await page.keyboard.press('Space'); assert.equal(await visibleRows().count(), 3);
        assert.equal(await filter('revision').getAttribute('aria-pressed'), 'true');
        await search.fill('UX-03'); assert.equal(await visibleRows().count(), 0, 'Search intersects the selected issue filter.');
        await filter('all').click(); await search.fill('');
        assert.deepEqual(await rows().evaluateAll(elements => elements.map(el => Number(el.dataset.lineId))), initialOrder);
        checks.push('Keyboard filters and accent-insensitive name/SKU/barcode search compose correctly; empty results offer reset without reordering rows.');

        await filter('revision').click(); await search.fill('sua tuoi');
        const beforeIntakeRefresh = await rows().evaluateAll(elements => elements.map(el => ({ id: el.dataset.lineId, price: el.querySelector('.commercial-unit-price').value })));
        const visibleBeforeIntakeRefresh = await visibleRows().evaluateAll(elements => elements.map(el => el.dataset.lineId));
        assert.equal(visibleBeforeIntakeRefresh.length, 1);
        await page.evaluate(() => window.GaoAppPurchaseReceiptApproval.refreshAfterIntake());
        await page.waitForFunction(() => window.GaoReceiptApprovalWorkspace.getState().filter === 'revision' && window.GaoReceiptApprovalWorkspace.getState().search === 'sua tuoi');
        assert.equal(await filter('revision').getAttribute('aria-pressed'), 'true');
        assert.equal(await search.inputValue(), 'sua tuoi');
        assert.deepEqual(await visibleRows().evaluateAll(elements => elements.map(el => el.dataset.lineId)), visibleBeforeIntakeRefresh);
        assert.deepEqual(await rows().evaluateAll(elements => elements.map(el => ({ id: el.dataset.lineId, price: el.querySelector('.commercial-unit-price').value }))), beforeIntakeRefresh);
        await filter('all').click(); await search.fill('');
        checks.push('The real HTTP refresh used after receipt intake preserves search, the selected revision filter, visible rows, all entered prices and stable row order.');

        const price = row(info.changedLineId).locator('.commercial-unit-price');
        const original = await price.inputValue();
        await price.fill(''); await refresh(); assert.ok(await count('Blocking') > 0);
        await search.fill('UX-03'); await page.locator('#commercialNextIssue').click();
        await page.waitForFunction(() => document.activeElement?.classList.contains('commercial-unit-price'));
        assert.equal(await row(info.changedLineId).isVisible(), true, 'Next issue reveals the blocked row even when search hid it.');
        const priceIssue = await issue(/giá nhập|thiếu giá|giá phải|chưa có giá/i);
        await priceIssue.click();
        await page.waitForFunction(() => document.activeElement?.classList.contains('commercial-unit-price'));
        assert.equal(await page.locator('[data-workbench-tab="goods"]').getAttribute('aria-selected'), 'true');
        assert.equal(await page.locator('#commercialApproveButton').isDisabled(), true);
        await price.fill('0'); await refresh(); assert.ok(await count('Blocking') > 0, 'Unplanned zero price remains blocked.');
        await page.locator('#receiptApprovalIssuesDisclosure').evaluate(el => { el.open = true; });
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            await captureSection(`blocking-readiness-${width}`, '#receiptApprovalReadiness');
            assert.ok(await page.locator('#receiptApprovalIssues .sd-review-issue.is-blocking').count() > 0, 'Expanded readiness explains a blocking condition.');
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        await price.fill(original); await saved(); await refresh(); assert.equal(await count('Blocking'), 0);
        await price.fill('76543'); await saved();
        assert.equal(await price.evaluate(el => document.activeElement === el), true, 'Autosave preserves input focus.');
        assert.deepEqual(await rows().evaluateAll(elements => elements.map(el => Number(el.dataset.lineId))), initialOrder);
        assert.match(await page.locator('#receiptPriceDraftStatus').textContent(), /đã lưu|lưu.*lúc/i);
        await page.reload(); await page.waitForFunction(() => !!window.GaoReceiptApprovalWorkspace); await saved(); await refresh();
        assert.equal(await row(info.changedLineId).locator('.commercial-unit-price').inputValue(), '76543');
        assert.equal(await row(info.unchangedLineId).locator('.commercial-unit-price').inputValue(), String(info.savedPrice));
        checks.push('Blank and zero prices report blocking reasons and jump to the exact input; saving preserves focus/order and both old/new review prices survive reload.');

        await (await issue(/giá bán thấp hơn giá vốn/i)).click();
        await page.waitForFunction(() => document.activeElement?.classList.contains('js-receipt-selling-prices'));
        await page.keyboard.press('Enter'); await page.locator('#receiptSellingPrices').waitFor({ state: 'visible' });
        assert.equal(await page.locator('#receiptSellingPrices').evaluate(el => !!el.closest('.sd-review-header')), false);
        await page.locator('#rspUnits .rsp-unit [data-field="price"]').first().waitFor({ state: 'visible' });
        await ui11.dialog(page, '#receiptSellingPrices', 'selling-prices', ui11.output('approval')); // UI-11 review hook
        assert.equal(await page.locator('#rspSave').isDisabled(), true, 'Opening the popup preserves current selling prices.');
        const sellingPosts = [];
        const captureSellingPost = request => { if (request.method() === 'POST' && /\/selling-prices(?:\?|$)/.test(request.url())) sellingPosts.push(request.url()); };
        page.on('request', captureSellingPost);
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            await page.locator('#receiptSellingPrices .modal-body').evaluate(el => { el.scrollTop = 0; });
            await shot(`selling-prices-current-${width}`);
            const content = await page.locator('#receiptSellingPrices .modal-content').evaluate(el => ({ width: el.clientWidth, scroll: el.scrollWidth }));
            assert.ok(content.scroll <= content.width + 1, `Selling-price popup fits ${width}.`);
        }
        const sellingPrice = page.locator('#rspUnits .rsp-unit [data-field="price"]').first();
        const draftSellingPrice = Number((await sellingPrice.inputValue()).replace(/\D/g, '')) + 1000;
        await sellingPrice.fill(String(draftSellingPrice));
        await page.locator('#rspSave').click();
        await page.locator('#rspConfirmation').waitFor({ state: 'visible' });
        assert.equal(await sellingPrice.isDisabled(), true, 'Confirmation freezes price editing until Back.');
        assert.match(await page.locator('#rspConfirmation').textContent(), /hiệu lực ngay/);
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            await page.locator('#rspConfirmation').scrollIntoViewIfNeeded();
            await shot(`selling-prices-confirmation-${width}`);
        }
        assert.equal(sellingPosts.length, 0, 'Reviewing proposed selling-price changes never writes prices.');
        await page.locator('#rspBackToEdit').click(); assert.equal(await sellingPrice.isDisabled(), false);
        await page.locator('#receiptSellingPrices .btn-close').click();
        await page.locator('#rspDiscardChanges').waitFor({ state: 'visible' }); await shot('selling-prices-unsaved-guard-390');
        await page.locator('#rspKeepEditing').click();
        assert.equal(Number((await sellingPrice.inputValue()).replace(/\D/g, '')), draftSellingPrice, 'Keep editing retains a proposed price.');
        await page.locator('#receiptSellingPrices .btn-close').click(); await page.locator('#rspDiscardAndClose').click();
        await page.locator('#receiptSellingPrices').waitFor({ state: 'hidden' });
        assert.equal(sellingPosts.length, 0, 'Discarding the simulation leaves actual selling prices untouched.');
        page.off('request', captureSellingPost);
        await page.setViewportSize({ width: 1440, height: 900 });
        await saved(); await refresh();
        assert.equal(await count('Blocking'), 0); assert.equal(await page.locator('#commercialApproveButton').isEnabled(), true);
        checks.push('Informational loss warning opens selling prices by keyboard; desktop/mobile simulation covers retained prices, two-step frozen confirmation, Back and unsaved-price discard guard, with no selling-price POST and no receipt blocker.');

        await tab('settlement');
        const supplierId = await page.locator('#commercialSupplierId').inputValue();
        await setSupplier('');
        assert.ok(await count('Blocking') > 0, 'Missing supplier is mandatory.');
        await (await issue(/nhà cung cấp/i)).click();
        assert.equal(await page.locator('[data-workbench-tab="settlement"]').getAttribute('aria-selected'), 'true');
        await page.keyboard.press('Escape'); await setSupplier(supplierId);
        assert.match(await page.locator('#commercialSupplierSaveState').textContent(), /Đã lưu nhà cung cấp/);
        assert.equal(await count('Blocking'), 0);
        await page.locator('#commercialHasVat').check(); await refresh();
        assert.ok(await count('Blocking') >= 14, 'Every missing tax rate is visible when VAT is enabled.');
        await page.locator('#receiptApprovalIssuesDisclosure').evaluate(el => { el.open = true; });
        await captureSection('vat-missing-rules-1440', '#receiptApprovalReadiness');
        await (await issue(/thuế suất|VAT/i)).click();
        assert.equal(await page.locator('[data-workbench-tab="goods"]').getAttribute('aria-selected'), 'true');
        await page.waitForFunction(() => document.activeElement?.classList.contains('commercial-tax'));
        await filter('all').click(); await search.fill('');
        for (const tax of await page.locator('.commercial-tax').all()) await tax.selectOption(String(info.taxId));
        await refresh(); assert.equal(await count('Blocking'), 0);
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            assert.equal(await rows().first().locator('.commercial-tax').isVisible(), true, 'VAT controls remain visible in the goods tab when enabled.');
            await captureSection(`goods-vat-fields-${width}`, '.commercial-line');
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        await tab('settlement'); await page.locator('#includeVatInInventoryCost').check();
        assert.match(await page.locator('#receiptPriceDraftBar').textContent(), /VAT.*áp dụng khi duyệt/s, 'Saved-price text does not imply unsaved settings were persisted.');
        const merchandise = await money('commercialMerchandiseTotal');
        const expectedMerchandise = await rows().evaluateAll(elements => Math.round(elements.reduce((total, el) => {
            const before = Math.round(Number(el.dataset.quantity) * Number(el.querySelector('.commercial-unit-price').value) * 100) / 100;
            return total + before + Math.round(before * 8) / 100;
        }, 0) * 100) / 100);
        assert.equal(merchandise, expectedMerchandise, 'Merchandise includes each line VAT exactly once.');
        assert.equal(await page.evaluate(() => { const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented; }), true, 'Unapplied VAT/settlement choices warn before leaving.');
        page.off('dialog', acceptDialog);
        const cancelledNavigation = new Promise((resolve, reject) => {
            page.once('dialog', async dialog => {
                if (dialog.type() !== 'beforeunload') { await dialog.dismiss(); reject(new Error('Expected the unsaved-settings beforeunload dialog.')); return; }
                await dialog.dismiss(); resolve();
            });
        });
        await page.locator('.sd-review-header-actions a').first().click({ noWaitAfter: true });
        await Promise.race([cancelledNavigation, new Promise((_, reject) => setTimeout(() => reject(new Error('Leaving unapplied settings did not warn.')), 8000))]);
        page.on('dialog', acceptDialog);
        assert.equal(new URL(page.url()).pathname, `/admin/stock-documents/${info.receiptId}`);
        assert.equal(await page.locator('#commercialHasVat').isChecked(), true, 'Cancelling navigation retains the selected VAT choice.');
        assert.equal(await money('commercialOutstandingMerchandise'), merchandise);
        assert.equal(await page.locator('#commercialMerchandisePaid').count(), 0, 'Approval cannot declare supplier payment.');
        assert.match(await page.locator('[data-workbench-panel="settlement"] .sd-receipt-payment-rule').textContent(), /phiếu chi tại Thu\/chi sau khi duyệt/);
        assert.equal(await money('commercialOutstandingMerchandise'), merchandise);
        checks.push('Supplier saves independently; VAT requires configured tax; payment is read-only and requires a separate treasury voucher after approval; cancelling navigation retains unapplied choices.');

        await tab('freight'); await page.locator('#commercialHasFreight').check(); await refresh();
        assert.ok(await count('Blocking') >= 2, 'Enabled freight requires amount and recipient.');
        await page.locator('#commercialFreightTotal').fill('12345');
        await page.locator('#commercialFreightPayee').fill('Đơn vị vận chuyển kiểm thử');
        await page.locator('#capitalizeFreightInInventoryCost').check();
        await refresh();
        assert.equal(await count('Blocking'), 0);
        assert.match(await page.locator('#commercialFreightHeading').textContent(), /12[.,]345/);
        assert.equal(await page.locator('#commercialFreightTabTotal').isVisible(), true);
        assert.match(await page.locator('#commercialFreightTabTotal').textContent(), /12[.,]345/);
        assert.match(await page.locator('#commercialFreightDescription').textContent(), /Đưa vào giá vốn.*Tạo khoản phải trả/);
        assert.equal(await money('commercialOutstandingFreight'), 12345);
        assert.equal(await page.locator('#commercialFreightPaid').count(), 0, 'Approval cannot declare freight payment.');
        await page.locator('#capitalizeFreightInInventoryCost').uncheck();
        assert.match(await page.locator('#commercialFreightDescription').textContent(), /Ghi nhận riêng.*Tạo khoản phải trả/);
        assert.equal(await money('commercialLandedTotal'), merchandise);
        await page.locator('#capitalizeFreightInInventoryCost').check();
        assert.equal(await money('commercialLandedTotal'), round(merchandise + 12345));
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            await captureSection(`freight-enabled-${width}`, '#commercialFreightDisclosure');
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        await tab('goods');
        const allocation = rows().first().locator('.commercial-freight-allocation');
        const allocated = Number(await allocation.inputValue()); await allocation.fill(String(round(allocated + 1))); await refresh();
        assert.ok(await count('Blocking') > 0, 'Freight allocation imbalance blocks approval.');
        await (await issue(/phân bổ/i)).click();
        assert.equal(await page.locator('[data-workbench-tab="freight"]').getAttribute('aria-selected'), 'true');
        await page.locator('#commercialAutoAllocate').click(); await refresh(); assert.equal(await count('Blocking'), 0);
        const sumAllocated = await page.locator('.commercial-freight-allocation').evaluateAll(elements => Math.round(elements.reduce((sum, el) => sum + Number(el.value), 0) * 100) / 100);
        assert.equal(sumAllocated, 12345);
        await shot('freight-reviewed');
        checks.push('Freight amount/recipient/allocation rules are explicit; automatic rounding balances to the cent; inventory value and two independent unpaid totals match the selected VAT/capitalization rules.');

        await tab('goods'); await filter('revision').click();
        assert.equal(await visibleRows().count(), 3, 'Final approval is deliberately opened with most goods filtered out.');
        await row(info.changedLineId).locator('.commercial-unit-price').fill('76544');
        const operations = []; let submitted;
        page.on('request', request => {
            if (request.method() !== 'POST') return;
            if (/\/(price-draft|approve-commercial)$/.test(request.url())) operations.push(request.url().split('/').pop());
            if (request.url().endsWith('/approve-commercial')) submitted = request.postDataJSON();
        });
        await page.locator('#commercialApproveButton').click();
        await page.locator('#approveModal').waitFor({ state: 'visible' });
        await page.locator('#priceVarianceAcceptancePanel').waitFor({ state: 'visible' });
        assert.equal(await page.locator('#acceptPriceVariance').isChecked(), false);
        assert.equal(operations.includes('approve-commercial'), false, 'Opening the review dialog must never approve.');
        const finalMerchandise = await money('commercialMerchandiseTotal');
        assert.equal(await money('commercialApprovalModalMerchandiseTotal'), finalMerchandise);
        assert.equal(await money('approvalModalLanded'), round(finalMerchandise + 12345));
        assert.equal(await money('approvalModalOutstandingMerchandise'), finalMerchandise);
        assert.equal(await money('approvalModalOutstandingFreight'), 12345);
        assert.match(await page.locator('#approveModal').textContent(), /kho|công nợ/i);
        await page.locator('input[name="approvalInvoiceExpectation"][value="notExpected"]').check();
        await page.locator('#btnApprove').click(); await refresh();
        assert.equal(operations.includes('approve-commercial'), false, 'Price differences require explicit acceptance.');
        // Validation may return the user to the relevant field; reopen the review to accept.
        if (!await page.locator('#approveModal').isVisible()) {
            await page.locator('#commercialApproveButton').click(); await page.locator('#approveModal').waitFor({ state: 'visible' });
        }
        await ui11.dialog(page, '#approveModal', 'final-receipt-approval', ui11.output('approval')); // UI-11 review hook
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            const bounds = await page.locator('#approveModal .modal-content').boundingBox();
            assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width + 1 && bounds.y >= 0 && bounds.y + bounds.height <= 901, `Approval dialog fits ${width}.`);
            const heading = await page.locator('#approveModalTitle').boundingBox();
            assert.ok(heading.y >= bounds.y && heading.y + heading.height < bounds.y + bounds.height, `Approval title remains visible after validation and scrolling at ${width}.`);
            if (width < 480) assert.ok(await page.locator('#approveModal .sd-approval-final-summary strong').evaluateAll(elements => elements.every(el => el.scrollWidth <= el.clientWidth + 1)), 'Mobile confirmation amounts fit without clipped currency.');
            await shot(`approval-modal-${width}`);
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        await page.locator('#acceptPriceVariance').check();
        await page.locator('input[name="approvalInvoiceExpectation"][value="notExpected"]').check();
        await page.locator('#approveNote').fill('Đã kiểm tra hàng sửa, thuế, cước và công nợ trên dữ liệu thử.');
        const approvalResponse = page.waitForResponse(response => response.url().endsWith('/approve-commercial') && response.request().method() === 'POST');
        await page.locator('#btnApprove').dblclick({ delay: 20 });
        const approval = await approvalResponse;
        assert.equal(approval.ok(), true, await approval.text());
        await page.waitForTimeout(150);
        assert.equal(operations.filter(x => x === 'approve-commercial').length, 1, 'Repeated click submits approval only once.');
        assert.equal(operations.at(-1), 'approve-commercial', 'Pending prices save before approval.');
        assert.equal(submitted.lines.length, 14, 'All lines are sent even when filters were used.');
        assert.equal(submitted.hasVat, true); assert.equal(submitted.includeVatInInventoryCost, true);
        assert.equal(submitted.freightTotal, 12345); assert.equal(submitted.capitalizeFreightInInventoryCost, true);
        assert.equal(submitted.isMerchandisePaid, false); assert.equal(submitted.isFreightPaid, false);
        const approvedState = await (await context.request.get(apiUrl)).json();
        assert.equal(approvedState.lines.find(line => line.id === info.changedLineId).unitPriceBeforeVat, 76544);
        assert.equal(approvedState.totalAmount, finalMerchandise);
        await page.waitForURL(location => location.pathname === '/admin/stock-documents');
        await page.goto(url); await page.waitForFunction(() => document.getElementById('commercialApprovalWorkbench')?.dataset.readonly === 'true');
        assert.equal(await page.locator('#commercialApproveButton, #commercialRejectButton').count(), 0);
        assert.equal(await page.locator('.commercial-unit-price:not([disabled])').count(), 0);
        await shot('approved-readonly');
        assert.deepEqual(errors, []);
        assert.deepEqual(consoleErrors, [], 'No local JavaScript console errors during the complete walkthrough.');
        assert.deepEqual(serverFailures, [], 'No local HTTP 5xx responses during the complete walkthrough.');
        checks.push('Approval preview does not post, requires price-variance acknowledgement, fits desktop/mobile and flushes prices before one real approval; reload is read-only.');
        fs.writeFileSync(path.join(info.evidenceDirectory, 'browser-checks.json'), JSON.stringify({ fixtureOnly: true, checks, layouts, snapshots, errors, consoleErrors, serverFailures, submitted }, null, 2));
        console.log('PASS receipt approval workspace: ' + checks.join('\n'));
    } catch (error) {
        await shot('failure').catch(() => {});
        const diagnostic = await page.evaluate(() => ({ state: window.GaoReceiptApprovalWorkspace?.getState?.(),
            focus: { tag: document.activeElement?.tagName, id: document.activeElement?.id, className: document.activeElement?.className },
            ready: document.getElementById('receiptApprovalReadiness')?.innerText })).catch(() => null);
        fs.writeFileSync(path.join(info.evidenceDirectory, 'browser-checks.json'), JSON.stringify({ fixtureOnly: true, checks, layouts, snapshots, errors, consoleErrors, serverFailures, failure: error.message, diagnostic }, null, 2));
        console.error('UI diagnostic', { url: page.url(), errors, consoleErrors, serverFailures, checks, diagnostic });
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
