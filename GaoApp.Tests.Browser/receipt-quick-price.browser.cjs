'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let input = ''; process.stdin.on('data', value => input += value); process.stdin.on('end', () => resolve(input)); }));
    const seed = info.seed;
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    let page;
    const errors = [], writes = [];
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
        page = await context.newPage();
        page.on('pageerror', error => errors.push(error.message));
        page.on('request', request => {
            if (request.url().includes(`/stock-documents/${seed.receiptId}/price-draft`) && request.method() === 'POST')
                writes.push({ url: request.url(), body: request.postDataJSON(), token: request.headers().requestverificationtoken });
            if (request.url().includes('/pricing-allocation') && ['PUT', 'POST'].includes(request.method()))
                writes.push({ forbiddenPlanWrite: request.url(), method: request.method() });
        });
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name="UserName"]').fill(info.user); await page.locator('[name="Password"]').fill(info.password);
        await page.locator('[name="SelectedTerminalId"]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type="submit"]').click()]);
        const receiptUrl = info.baseUrl + `/admin/stock-documents/${seed.receiptId}`;
        await page.goto(receiptUrl);
        const row = id => page.locator(`.commercial-line[data-line-id="${id}"]`);
        const panel = id => row(id).locator('[data-qprice-panel]');
        const input = id => row(id).locator('.commercial-unit-price');
        const control = (id, name) => panel(id).locator(`[data-qprice-${name}]`);
        const prices = () => page.locator('.commercial-unit-price').evaluateAll(inputs => inputs.map(input => Number(input.value)));
        const waitForEditable = id => page.waitForFunction(lineId => {
            const price = document.querySelector(`.commercial-line[data-line-id="${lineId}"] .commercial-unit-price`);
            return price && !price.disabled && !price.readOnly && window.stockDocumentPage?.canApproveCommercial === true &&
                document.getElementById('pricingBillConversion')?.textContent.includes('Chỉ tìm trong') &&
                window.GaoReceiptPriceDrafts && !window.GaoReceiptPriceDrafts.hasUnsaved();
        }, id);
        await row(seed.setLineId).locator('[data-qprice-open]').waitFor();
        await waitForEditable(seed.setLineId);
        const originalPrices = await prices();
        assert.deepEqual(originalPrices, [10, 10, 10]);
        assert.equal(await page.locator('[data-qprice-open]').count(), 3);
        assert.equal(await page.locator('[data-qprice-panel]:not([hidden])').count(), 0);

        async function open(id) {
            // Pricing workspace initialization temporarily holds/disables manual price inputs.
            // hasUnsaved() alone does not signal that the Goods row can accept the helper.
            await waitForEditable(id);
            await row(id).locator('[data-qprice-open]').click();
            assert.equal(await panel(id).isVisible(), true, `Calculator must open on editable Goods row ${id}.`);
        }
        async function fill(id, buy, buyUnit, money, gift = null, giftUnit = null) {
            await control(id, 'buy-unit').selectOption(String(buyUnit));
            await control(id, 'buy').fill(String(buy));
            await control(id, 'money').fill(String(money));
            await control(id, 'has-gift').setChecked(gift !== null);
            if (gift !== null) {
                await control(id, 'gift-unit').selectOption(String(giftUnit));
                await control(id, 'gift').fill(String(gift));
            }
        }
        function resultText(id) { return control(id, 'result').textContent(); }
        function observeNoAutosave() { return page.waitForTimeout(1150); }
        async function assertMobileLayout(expectedFooterPosition) {
            const layout = await page.evaluate(() => {
                const footer = document.querySelector('#commercialApprovalWorkbench > .sd-commercial-sticky-actions');
                return { footerPosition: footer && getComputedStyle(footer).position,
                    viewportWidth: window.innerWidth, documentWidth: document.documentElement.scrollWidth,
                    bodyWidth: document.body.scrollWidth };
            });
            assert.equal(layout.viewportWidth, 390);
            assert.equal(layout.footerPosition, expectedFooterPosition, `Mobile footer state: ${JSON.stringify(layout)}`);
            assert.ok(Math.max(layout.documentWidth, layout.bodyWidth) <= layout.viewportWidth + 1,
                `Mobile calculator must not cause horizontal overflow: ${JSON.stringify(layout)}`);
        }
        async function assertMobileActionReachable(id, action) {
            const button = control(id, action);
            await button.scrollIntoViewIfNeeded();
            await page.screenshot({ path: path.join(info.evidenceRoot, `receipt-quick-price-mobile-${action}-action.png`) });
            await button.click({ trial: true, timeout: 5000 });
            const hit = await button.evaluate(element => {
                const rect = element.getBoundingClientRect();
                const x = rect.left + rect.width / 2, y = rect.top + rect.height / 2;
                const target = document.elementFromPoint(x, y);
                return { reachable: target === element || element.contains(target), x, y,
                    viewportHeight: window.innerHeight, targetTag: target?.tagName,
                    targetId: target?.id, targetClass: target?.className };
            });
            assert.equal(hit.reachable, true, `Mobile ${action} must be reachable without a sticky overlay: ${JSON.stringify(hit)}`);
        }

        await open(seed.setLineId);
        await fill(seed.setLineId, 18, seed.setUnitId, 1580000, 2, seed.setUnitId);
        assert.match(await resultText(seed.setLineId), /79\.000/);
        assert.match(await control(seed.setLineId, 'base').textContent(), /39\.500/);
        assert.match(await control(seed.setLineId, 'total').textContent(), /40/);
        assert.equal(await control(seed.setLineId, 'approve').isEnabled(), true);
        await observeNoAutosave();
        assert.deepEqual(writes, [], 'Typing in the calculator must not autosave prices or a pricing plan.');
        assert.deepEqual(await prices(), originalPrices, 'Preview must not change any goods price.');
        await control(seed.setLineId, 'cancel').click();
        assert.equal(await panel(seed.setLineId).isHidden(), true);
        assert.deepEqual(await prices(), originalPrices);

        await open(seed.setLineId);
        await fill(seed.setLineId, 18, seed.setUnitId, 1580000, 4, seed.baseUnitId);
        assert.match(await resultText(seed.setLineId), /79\.000/);
        assert.match(await control(seed.setLineId, 'base').textContent(), /39\.500/);
        await control(seed.setLineId, 'money').press('Escape');
        assert.equal(await panel(seed.setLineId).isHidden(), true);
        assert.deepEqual(await prices(), originalPrices);
        assert.deepEqual(writes, []);

        // Enter on Hủy must perform Hủy, rather than approving the computed price.
        await open(seed.setLineId);
        await fill(seed.setLineId, 18, seed.setUnitId, 1580000, 2, seed.setUnitId);
        await control(seed.setLineId, 'cancel').press('Enter');
        assert.equal(await panel(seed.setLineId).isHidden(), true);
        assert.deepEqual(await prices(), originalPrices);
        assert.deepEqual(writes, []);

        await open(seed.setLineId);
        for (const invalid of [{ buy: '0', money: '1000' }, { buy: '-1', money: '1000' }, { buy: '20', money: '0' },
            { buy: '20', money: '-1' }, { buy: '20', money: '' }, { buy: '20', money: '1000.001' }]) {
            await fill(seed.setLineId, invalid.buy, seed.setUnitId, invalid.money);
            assert.equal(await control(seed.setLineId, 'approve').isDisabled(), true, JSON.stringify(invalid));
            assert.ok((await control(seed.setLineId, 'error').textContent()).trim(), 'Invalid input must explain the error.');
            await control(seed.setLineId, 'money').press('Enter');
            assert.equal(await panel(seed.setLineId).isVisible(), true, 'Invalid Enter must not close/apply.');
            assert.deepEqual(await prices(), originalPrices);
        }
        await control(seed.setLineId, 'money').press('Control+Enter');
        assert.equal(await panel(seed.setLineId).isVisible(), true);
        assert.equal(await page.locator('#approveModal').isVisible(), false, 'Calculator shortcuts must not open receipt approval.');
        assert.deepEqual(await prices(), originalPrices);
        assert.deepEqual(writes, []);
        await fill(seed.setLineId, 20, seed.setUnitId, 1000, -1, seed.baseUnitId);
        assert.equal(await control(seed.setLineId, 'approve').isDisabled(), true);
        assert.ok((await control(seed.setLineId, 'error').textContent()).trim());
        await control(seed.setLineId, 'has-gift').uncheck();
        assert.equal(await control(seed.setLineId, 'gift-fields').isHidden(), true);
        assert.equal(await control(seed.setLineId, 'approve').isEnabled(), true, 'Unselected gift input must not affect the calculation.');
        // This calculator is a helper; it does not invent a quantity-matching approval gate.
        await fill(seed.setLineId, 1, seed.setUnitId, 1000);
        assert.equal(await control(seed.setLineId, 'approve').isEnabled(), true);
        assert.match(await resultText(seed.setLineId), /1\.000/);
        await control(seed.setLineId, 'cancel').click();
        await observeNoAutosave(); assert.deepEqual(writes, []);

        await open(seed.setLineId);
        await fill(seed.setLineId, 18, seed.setUnitId, 1580000, 4, seed.baseUnitId);
        fs.mkdirSync(info.evidenceRoot, { recursive: true });
        await panel(seed.setLineId).screenshot({ path: path.join(info.evidenceRoot, 'receipt-quick-price-desktop.png') });
        await page.setViewportSize({ width: 390, height: 844 });
        await assertMobileLayout('static');
        await panel(seed.setLineId).screenshot({ path: path.join(info.evidenceRoot, 'receipt-quick-price-mobile.png') });
        await assertMobileActionReachable(seed.setLineId, 'cancel');
        await control(seed.setLineId, 'cancel').click();
        assert.equal(await panel(seed.setLineId).isHidden(), true);
        await assertMobileLayout('sticky');
        await observeNoAutosave();
        assert.deepEqual(await prices(), originalPrices, 'Mobile Hủy must retain every Goods price.');
        assert.deepEqual(writes, [], 'Mobile Hủy must not write a price draft or pricing plan.');
        await open(seed.setLineId);
        await fill(seed.setLineId, 18, seed.setUnitId, 1580000, 4, seed.baseUnitId);
        await assertMobileLayout('static');
        await assertMobileActionReachable(seed.setLineId, 'approve');
        const confirmed = page.waitForResponse(response => response.url().endsWith(`/stock-documents/${seed.receiptId}/price-draft`) &&
            response.request().method() === 'POST' && response.request().postDataJSON().lines.some(line => line.stockDocumentLineId === seed.setLineId && line.unitPriceBeforeVat === 79000));
        await control(seed.setLineId, 'approve').click();
        assert.equal(await panel(seed.setLineId).isHidden(), true);
        await assertMobileLayout('sticky');
        assert.equal(Number(await input(seed.setLineId).inputValue()), 79000);
        assert.equal(Number(await input(seed.packLineId).inputValue()), 10);
        assert.equal(Number(await input(seed.cartonLineId).inputValue()), 10);
        const savedSet = await confirmed; assert.equal(savedSet.status(), 200, await savedSet.text());
        await page.waitForFunction(() => !window.GaoReceiptPriceDrafts.hasUnsaved());
        assert.equal(writes.length, 1); assert.ok(writes[0].token);
        assert.deepEqual(writes[0].body.lines.map(line => line.stockDocumentLineId), [seed.setLineId]);
        await page.setViewportSize({ width: 1440, height: 1000 });
        await page.reload(); await row(seed.setLineId).locator('[data-qprice-open]').waitFor();
        assert.equal(Number(await input(seed.setLineId).inputValue()), 79000);
        assert.equal(Number(await input(seed.packLineId).inputValue()), 10);

        // One gift unit differs from the bought unit; cent rounding matches the manual field precision.
        await open(seed.packLineId);
        await fill(seed.packLineId, 2, seed.packUnitId, 100000, 4, seed.baseUnitId);
        assert.match(await resultText(seed.packLineId), /33\.333,33/);
        assert.match(await control(seed.packLineId, 'total').textContent(), /12/);
        assert.equal(await control(seed.packLineId, 'approve').isEnabled(), true);
        assert.equal(Number(await input(seed.packLineId).inputValue()), 10);
        const rounded = page.waitForResponse(response => response.url().endsWith(`/stock-documents/${seed.receiptId}/price-draft`) &&
            response.request().method() === 'POST' && response.request().postDataJSON().lines.some(line => line.stockDocumentLineId === seed.packLineId && line.unitPriceBeforeVat === 33333.33));
        await control(seed.packLineId, 'money').press('Enter');
        assert.equal(await panel(seed.packLineId).isHidden(), true);
        assert.equal(Number(await input(seed.packLineId).inputValue()), 33333.33);
        const savedRounded = await rounded; assert.equal(savedRounded.status(), 200, await savedRounded.text());
        await page.waitForFunction(() => !window.GaoReceiptPriceDrafts.hasUnsaved());
        assert.equal(writes.length, 2); assert.ok(writes[1].token);
        assert.deepEqual(writes[1].body.lines.map(line => line.stockDocumentLineId), [seed.packLineId]);
        await page.reload(); await row(seed.packLineId).locator('[data-qprice-open]').waitFor();
        assert.equal(Number(await input(seed.setLineId).inputValue()), 79000);
        assert.equal(Number(await input(seed.packLineId).inputValue()), 33333.33);
        assert.equal(Number(await input(seed.cartonLineId).inputValue()), 10);
        assert.equal(await page.locator('#pricingAllocationLines .sd-bill-row').count(), 0);
        assert.deepEqual(errors, []);
        console.log('PASS: local preview, same-SKU unit conversion, cancel/Escape/native cancel Enter, invalid inputs, reachable mobile Hủy/Duyệt clicks, explicit desktop Enter, exact row targeting, antiforgery and real autosave persistence, no pricing plan writes.');
    } catch (error) {
        if (page && !page.isClosed()) {
            fs.mkdirSync(info.evidenceRoot, { recursive: true });
            await page.screenshot({ path: path.join(info.evidenceRoot, 'receipt-quick-price-failure.png'), fullPage: true }).catch(() => {});
            console.error('Quick price failure state:', JSON.stringify(await page.evaluate(() => ({
                canApprove: window.stockDocumentPage?.canApproveCommercial,
                readOnly: document.getElementById('commercialApprovalWorkbench')?.dataset.readonly,
                workspaceStatus: document.getElementById('pricingBillConversion')?.textContent,
                priceDraftStatus: document.getElementById('receiptPriceDraftStatus')?.textContent,
                unsaved: window.GaoReceiptPriceDrafts?.hasUnsaved(),
                calculatorScriptLoaded: [...document.scripts].some(script => script.src.includes('/receipt-quick-price.js')),
                rows: [...document.querySelectorAll('.commercial-line[data-line-id]')].map(row => ({
                    id: row.dataset.lineId,
                    launcherDisabled: row.querySelector('[data-qprice-open]')?.disabled,
                    priceDisabled: row.querySelector('.commercial-unit-price')?.disabled,
                    priceReadOnly: row.querySelector('.commercial-unit-price')?.readOnly,
                    price: row.querySelector('.commercial-unit-price')?.value,
                    panelHidden: row.querySelector('[data-qprice-panel]')?.hidden,
                    error: row.querySelector('[data-qprice-error]')?.textContent
                }))
            })).catch(() => ({ stateUnavailable: true }))));
            console.error('Quick price browser errors:', JSON.stringify(errors));
            console.error('Quick price write count:', writes.length);
        }
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
