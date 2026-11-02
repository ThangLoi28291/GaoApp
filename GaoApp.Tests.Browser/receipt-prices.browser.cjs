'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let data = ''; process.stdin.on('data', chunk => data += chunk); process.stdin.on('end', () => resolve(data));
    }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
    const errors = [], posts = [];
    page.on('pageerror', e => errors.push(e.message));
    page.on('request', request => {
        if (request.method() === 'POST' && request.url().endsWith('/selling-prices')) posts.push(request.postDataJSON());
    });
    const unit = id => page.locator('#rspUnits .rsp-unit[data-unit-id="' + id + '"]');
    const pack = unit(info.packId), carton = unit(info.cartonId);
    const digits = value => Number(value.replace(/\D/g, ''));
    const assertPrice = async (locator, expected) => assert.equal(digits(await locator.inputValue()), expected);
    const output = path.resolve(process.env.GAO_RECEIPT_BROWSER_OUTPUT || 'TestResults/receipt-prices'); fs.mkdirSync(output, { recursive: true });
    const modalSettled = async () => page.waitForFunction(() => {
        const modal = document.querySelector('#receiptSellingPrices.show');
        if (!modal) return false;
        const transform = getComputedStyle(modal.querySelector('.modal-dialog')).transform;
        return transform === 'none' || transform === 'matrix(1, 0, 0, 1, 0, 0)';
    });
    const saveConfirmed = async () => {
        const before = posts.length;
        await page.locator('#rspSave').click();
        await page.locator('#rspConfirmation').waitFor({ state: 'visible' });
        assert.equal(posts.length, before, 'Reviewing the change list must not write prices.');
        const response = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith('/selling-prices'));
        await page.locator('#rspSave').click();
        const saved = await response;
        assert.equal(saved.ok(), true, await saved.text());
        const json = await saved.json();
        await page.locator('#rspConfirmation').waitFor({ state: 'hidden' });
        return json.data ?? json;
    };
    const closeModal = async () => {
        await page.locator('#receiptSellingPrices .btn-close').click();
        await page.locator('#receiptSellingPrices').waitFor({ state: 'hidden' });
    };
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('[name=SelectedTerminalId]').count())
            await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        await page.goto(info.baseUrl + '/admin/stock-documents/' + info.receiptId);
        const receiptPrice = page.locator('.commercial-unit-price').first();
        await receiptPrice.fill('44000');
        const initialPriceResponse = page.waitForResponse(response => response.request().method() === 'GET' && response.url().endsWith('/selling-prices'));
        await page.locator('.js-receipt-selling-prices').first().click();
        const initialJson = await (await initialPriceResponse).json();
        const initialPrices = initialJson.data ?? initialJson;
        await page.locator('#rspContent').waitFor({ state: 'visible' });
        assert.equal(await page.locator('#rspCost').inputValue(), '11000');
        assert.equal(await page.locator('#rspTarget').inputValue(), '10');
        await assertPrice(pack.locator('[data-field="price"]'), 48000);
        await assertPrice(carton.locator('[data-field="price"]'), 280000);
        assert.equal(await pack.locator('[data-field="wholesale-inherit"]').isChecked(), true);
        assert.equal(await pack.locator('[data-field="wholesale"]').isDisabled(), true);
        assert.equal(await page.locator('#rspUpdateProductPrice').isChecked(), false);
        assert.equal(await page.locator('#rspSave').isDisabled(), true);
        assert.equal(await page.locator('#rspReview').isDisabled(), false);
        assert.equal(await page.locator('#rspUnits [data-field="selected"]').count(), 0);
        await pack.locator('[data-field="price"]').focus();
        await page.keyboard.press('Enter');
        assert.equal(await carton.locator('[data-field="price"]').evaluate(element => element === document.activeElement), true,
            'Enter moves to the next enabled price and skips inherited wholesale.');
        await page.locator('#rspCalculator').evaluate(element => { element.open = true; });
        await page.locator('#rspTarget').fill('20');
        await page.locator('#rspAdvanced').evaluate(element => { element.open = true; });
        await page.locator('#rspCost').fill('12000');
        await assertPrice(pack.locator('[data-field="price"]'), 48000);
        await assertPrice(carton.locator('[data-field="price"]'), 280000);
        assert.equal(posts.length, 0, 'Calculation inputs must never overwrite a price or send a request.');

        await pack.locator('[data-field="wholesale-inherit"]').uncheck();
        await pack.locator('[data-field="wholesale"]').fill('53000');
        assert.equal(await page.locator('#rspReview').isDisabled(), true, 'Keep-prices cannot discard an edited draft.');
        const wholesaleSaved = await saveConfirmed();
        assert.equal(posts.at(-1).updateProductPrice, false);
        assert.equal(posts.at(-1).units.length, 1);
        assert.equal(posts.at(-1).units[0].price, 48000);
        assert.equal(posts.at(-1).units[0].wholesalePrice, 53000);
        assert.equal(wholesaleSaved.productPrice, initialPrices.productPrice,
            'Wholesale-only update must keep the Product base price.');
        assert.equal(wholesaleSaved.units.find(x => x.id === info.cartonId).price, 280000);

        await pack.locator('[data-field="price"]').fill('60000');
        await pack.locator('[data-field="price"]').blur();
        assert.match(await pack.locator('[data-field="price"]').inputValue(), /60[.,]000/);
        await pack.locator('[data-field="wholesale"]').fill('55000');
        await page.locator('#rspUpdateProductPrice').check();
        await page.locator('#rspProductSource').selectOption(String(info.packId));
        await page.locator('#rspSave').click();
        await page.locator('#rspConfirmation').waitFor({ state: 'visible' });
        const summary = await page.locator('#rspConfirmation').textContent();
        assert.match(summary, /60[.,]000/);
        assert.match(summary, /55[.,]000/);
        assert.match(summary, /15[.,]000/);
        assert.equal(posts.length, 1, 'Confirmation remains read-only until the second click.');
        await page.locator('#rspBackToEdit').click();
        await page.locator('#rspConfirmation').waitFor({ state: 'hidden' });
        await assertPrice(pack.locator('[data-field="price"]'), 60000);
        assert.equal(posts.length, 1);
        const saved = await saveConfirmed();
        assert.equal(saved.productPrice, 15000);
        assert.equal(saved.units.find(x => x.id === info.packId).price, 60000);
        assert.equal(saved.units.find(x => x.id === info.packId).wholesalePrice, 55000);
        assert.equal(saved.units.find(x => x.id === info.cartonId).price, 280000);
        assert.equal(posts.at(-1).updateProductPrice, true);
        assert.equal(posts.at(-1).productPriceUnitId, info.packId);
        await page.waitForFunction(() => document.getElementById('priceReviewCounts')?.textContent.includes('Đã cập nhật giá: 1'));
        assert.match(await page.locator('#rspHistory').textContent(), /60[.,]000/);
        assert.equal(await page.locator('#rspSave').isDisabled(), true);
        assert.equal(await page.locator('#rspPrintLabels').isVisible(), true);
        assert.match(await page.locator('#rspPrintLabels').getAttribute('href'), /label-printing\?tab=quick&variantId=\d+&units=/);
        assert.equal(await receiptPrice.inputValue(), '44000', 'Price save preserves unsaved receipt inputs.');
        await page.locator('#rspCalculator,#rspAdvanced').evaluateAll(elements => elements.forEach(element => { element.open = false; }));
        await page.screenshot({ path: path.join(output, 'desktop.png') });

        await pack.locator('[data-field="price"]').fill('');
        assert.equal(await page.locator('#rspSave').isDisabled(), true);
        await pack.locator('[data-field="price"]').fill('61000');
        assert.equal(await page.locator('#rspSave').isDisabled(), false);
        await page.locator('#receiptSellingPrices .btn-close').click();
        await page.locator('#rspDiscardChanges').waitFor({ state: 'visible' });
        await page.locator('#rspKeepEditing').click();
        await assertPrice(pack.locator('[data-field="price"]'), 61000);
        await page.route('**/selling-prices', route => route.request().method() === 'POST' ? route.abort() : route.continue());
        await page.locator('#rspSave').click();
        await page.locator('#rspConfirmation').waitFor({ state: 'visible' });
        await page.locator('#rspSave').click();
        await page.waitForFunction(() => document.getElementById('rspMessage').classList.contains('alert-danger'));
        assert.equal(await page.locator('#rspSave').isDisabled(), true, 'An uncertain write requires reloading current versions.');
        await page.unroute('**/selling-prices');
        await closeModal();
        await page.locator('.js-receipt-selling-prices').first().click();
        await page.locator('#rspContent').waitFor({ state: 'visible' });
        await assertPrice(pack.locator('[data-field="price"]'), 60000);
        assert.equal(await pack.locator('[data-field="wholesale-inherit"]').isChecked(), false);
        await page.locator('#rspReview').click();
        await page.waitForFunction(() => document.getElementById('rspMessage').textContent.startsWith('Đã ghi nhận kiểm tra'));
        await closeModal();
        const firstRow = page.locator('.commercial-line').first();
        await page.waitForFunction(() => document.querySelector('.commercial-line')?.classList.contains('sd-price-reviewed'));
        await receiptPrice.fill('45000');
        assert.ok(await firstRow.evaluate(x => x.classList.contains('sd-price-attention')));
        await receiptPrice.fill('44000');
        assert.ok(await firstRow.evaluate(x => x.classList.contains('sd-price-reviewed')));
        await page.locator('#receiptPriceReviewSummary').scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(output, 'review-summary.png') });
        await page.locator('.js-receipt-selling-prices').first().click();
        await page.locator('#rspContent').waitFor({ state: 'visible' });
        await modalSettled();
        for (const width of [1920, 1440, 1280, 1024, 768, 390, 320]) {
            await page.setViewportSize({ width, height: width < 500 ? 844 : 1100 });
            const overflow = await page.locator('#receiptSellingPrices .modal-content').evaluate(x => ({ width: x.clientWidth, scroll: x.scrollWidth }));
            assert.ok(overflow.scroll <= overflow.width + 1, 'Modal overflow at ' + width + ': ' + JSON.stringify(overflow));
            for (const locator of [pack.locator('[data-field="price"]'), carton.locator('[data-field="wholesale"]')]) {
                const bounds = await locator.boundingBox();
                assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width + 1, 'Price control remains in viewport at ' + width);
                assert.ok(bounds.height >= 40, 'Price control is easy to tap at ' + width);
            }
            if ([1440, 390, 320].includes(width)) {
                await pack.locator('[data-field="price"]').scrollIntoViewIfNeeded();
                await page.screenshot({ path: path.join(output, 'prices-' + width + '.png') });
            }
        }
        assert.deepEqual(errors, []);
        console.log('PASS: actual receipt UI keeps current prices; opt-in calculator; explicit wholesale inheritance; two-step confirmation and cancel preserve drafts; optional Product sync; persistence/history/review; uncertain failures blocked; seven responsive sizes.');
    } catch (error) {
        await page.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {});
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
