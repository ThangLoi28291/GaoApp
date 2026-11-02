'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const root = path.resolve(__dirname, '..');
    const read = file => fs.readFileSync(path.join(root, 'GaoApp.Web', file), 'utf8');
    const css = ['vendor/css/core.css', 'css/demo.css', 'css/site.css', 'Admin/css/receipt-selling-prices.css'].map(file => read('wwwroot/' + file)).join('\n');
    const source = read('Areas/Admin/Views/StockDocumentManagement/_ReceiptSellingPrices.cshtml');
    const markup = source.slice(source.indexOf('<div')).replace(/^\s*@if[^\n]*\r?\n/gm, '').replace(/^\s*[{}]\s*$/gm, '');
    const output = path.join(root, 'TestResults/receipt-price-popup'); fs.mkdirSync(output, { recursive: true });
    const units = [
        { id: 1, unitName: 'Chai', factor: 1, price: 6000, wholesalePrice: null },
        { id: 2, unitName: 'Lốc', factor: 4, price: 24000, wholesalePrice: 20000 },
        { id: 3, unitName: 'Thùng', factor: 48, price: 250000, wholesalePrice: 200000 }
    ];
    const prices = { productName: 'Sữa chua lên men TH ít đường chai 85ml', baseUnitName: 'Chai', productPrice: 6000,
        productVersion: 'AAAAAAAB', variantVersion: 'AAAAAAAC', lineVersion: 'AAAAAAAD', catalogVersion: 'catalog-v1',
        productPriceUnitId: 1, units: units.map(unit => ({ ...unit, rowVersion: 'AAAAAAAE' })), history: [] };
    const reviews = { lines: [{ lineId: 101, belowCost: false, needsAttention: false, reviewed: false,
        currentBaseCost: 5310, reviewedBaseCost: null, catalogCurrent: true, units }] };
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
        const errors = []; page.on('pageerror', error => errors.push(error.message));
        await page.setContent(`<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style></head><body>
            <main style="padding:24px"><div id="priceReviewCounts"></div><table><tr class="commercial-line" data-factor="1" data-quantity="24"><td>
            <input class="commercial-unit-price" type="number" value="5421.44"><div data-price-review-line-id="101"></div>
            <button class="js-receipt-selling-prices" data-line-id="101" data-variant-id="1">Giá bán & lợi nhuận</button></td></tr></table></main>${markup}</body></html>`);
        await page.evaluate(({ prices, reviews }) => {
            window.stockDocumentPage = { documentId: 999 };
            window.fetch = async url => {
                const data = String(url).endsWith('/selling-price-reviews') ? reviews : prices;
                return { ok: true, json: async () => data };
            };
        }, { prices, reviews });
        await page.addScriptTag({ content: read('wwwroot/lib/bootstrap/dist/js/bootstrap.bundle.min.js') });
        await page.addScriptTag({ content: read('wwwroot/Admin/js/receipt-price-reviews.js') });
        await page.waitForFunction(() => document.querySelector('.sd-price-loss-detail')?.textContent.includes('Thùng'));
        const warning = page.locator('.sd-price-loss-detail');
        assert.match(await warning.textContent(), /Lốc: giá sỉ dưới vốn/);
        assert.match(await warning.textContent(), /Thùng: giá lẻ và sỉ dưới vốn/);
        assert.doesNotMatch(await warning.textContent(), /Chai:/, 'The default unit is profitable.');
        await page.locator('.commercial-unit-price').fill('4000');
        await page.evaluate(() => document.dispatchEvent(new Event('receipt-pricing-inputs-changed')));
        assert.equal(await page.locator('.sd-price-loss-detail').count(), 0, 'Recompute all unit warnings when receipt cost changes.');
        await page.locator('.commercial-unit-price').fill('5421.44');
        await page.evaluate(() => document.dispatchEvent(new Event('receipt-pricing-inputs-changed')));
        await page.addScriptTag({ content: read('wwwroot/Admin/js/receipt-selling-prices.js') });
        await page.locator('.js-receipt-selling-prices').click();
        await page.waitForFunction(() => document.querySelectorAll('#rspUnits .rsp-unit').length === 3);
        await page.waitForFunction(() => {
            const modal = document.querySelector('#receiptSellingPrices.show');
            if (!modal) return false;
            const transform = getComputedStyle(modal.querySelector('.modal-dialog')).transform;
            return transform === 'none' || transform === 'matrix(1, 0, 0, 1, 0, 0)';
        });
        assert.equal(await page.locator('#rspUnits .rsp-current-loss').count(), 2);
        assert.match(await page.locator('[data-unit-id="3"] [data-out="current-profit"]').textContent(), /Lỗ/);
        assert.match(await page.locator('[data-unit-id="2"] [data-out="current-wholesale-profit"]').textContent(), /Lỗ/);
        assert.equal(await page.locator('#rspSave').isDisabled(), true, 'Opening a price popup retains the current prices.');
        assert.equal(await page.locator('#rspReview').isDisabled(), false);
        await page.locator('[data-unit-id="1"] [data-field="price"]').focus();
        await page.keyboard.press('Enter');
        assert.equal(await page.locator('[data-unit-id="2"] [data-field="price"]').evaluate(element => element === document.activeElement), true,
            'Enter skips inherited wholesale and moves to the next editable price.');
        for (const width of [1920, 1440, 1366, 1024, 768, 390, 320]) {
            await page.setViewportSize({ width, height: 1100 });
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `No overflow at ${width}.`);
            const bounds = await page.locator('#rspUnits [data-field="price"],#rspUnits [data-field="wholesale"]').evaluateAll(elements => elements.map(el => ({ left: el.getBoundingClientRect().left, right: el.getBoundingClientRect().right, height: el.getBoundingClientRect().height })));
            assert.ok(bounds.every(x => x.left >= 0 && x.right <= width), `All prices fit at ${width}.`);
            assert.ok(bounds.every(x => x.height >= 40), `Prices are easy to tap at ${width}: ${JSON.stringify(bounds)}.`);
            const dialog = await page.locator('#receiptSellingPrices .modal-content').evaluate(el => ({ width: el.clientWidth, scrollWidth: el.scrollWidth }));
            assert.ok(dialog.scrollWidth <= dialog.width + 1, `Popup content does not overflow at ${width}.`);
        }
        for (const width of [1440, 390, 320]) {
            await page.setViewportSize({ width, height: 1100 });
            await page.locator('#receiptSellingPrices .modal-body').evaluate(element => { element.scrollTop = 0; });
            await page.screenshot({ path: path.join(output, `popup-top-${width}.png`) });
            await page.locator('#rspUnits').scrollIntoViewIfNeeded();
            await page.screenshot({ path: path.join(output, `prices-${width}.png`) });
        }
        await page.setViewportSize({ width: 1440, height: 1100 });
        const pack = page.locator('#rspUnits .rsp-unit[data-unit-id="2"]');
        await page.locator('#rspCalculator').evaluate(element => { element.open = true; });
        await page.locator('#rspTarget').fill('20');
        assert.equal(Number((await pack.locator('[data-field="price"]').inputValue()).replace(/\D/g, '')), 24000,
            'A changed target only recalculates suggestions.');
        await pack.locator('.rsp-unit-calculator').evaluate(element => { element.open = true; });
        const suggested = pack.locator('[data-action="suggest"]');
        assert.match(await suggested.textContent(), /27[.,]200/);
        await suggested.click();
        assert.equal(Number((await pack.locator('[data-field="price"]').inputValue()).replace(/\D/g, '')), 27200,
            'A suggestion changes its channel only after an explicit click.');
        assert.equal(Number((await pack.locator('[data-field="wholesale"]').inputValue()).replace(/\D/g, '')), 20000);
        assert.equal(await page.locator('#rspReview').isDisabled(), true);
        await page.locator('#rspSave').click();
        await page.locator('#rspConfirmation').waitFor({ state: 'visible' });
        await page.waitForFunction(() => {
            const section = document.querySelector('#rspConfirmation').getBoundingClientRect();
            const body = document.querySelector('#receiptSellingPrices .modal-body').getBoundingClientRect();
            return section.top >= body.top && section.bottom <= body.bottom + 1;
        });
        assert.match(await page.locator('#rspConfirmation').textContent(), /27[.,]200/);
        assert.equal(await pack.locator('[data-field="price"]').isDisabled(), true);
        await page.screenshot({ path: path.join(output, 'confirmation-1440.png') });
        await page.setViewportSize({ width: 390, height: 844 });
        await page.locator('#rspConfirmation').scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(output, 'confirmation-390.png') });
        await page.setViewportSize({ width: 1440, height: 1100 });
        await page.locator('#rspBackToEdit').click();
        assert.equal(await pack.locator('[data-field="price"]').isDisabled(), false);
        await page.locator('#receiptSellingPrices .btn-close').click();
        await page.locator('#rspDiscardChanges').waitFor({ state: 'visible' });
        await page.locator('#rspKeepEditing').click();
        assert.equal(Number((await pack.locator('[data-field="price"]').inputValue()).replace(/\D/g, '')), 27200);
        await page.locator('#receiptSellingPrices .btn-close').click();
        await page.locator('#rspDiscardAndClose').click();
        await page.locator('#receiptSellingPrices').waitFor({ state: 'hidden' });
        assert.deepEqual(errors, []);
        console.log('PASS: converted retail/wholesale loss warnings; current prices kept on open; explicit per-channel suggestions; frozen confirmation and Back; discard guard; accessible price inputs and no overflow at seven desktop/mobile widths.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
