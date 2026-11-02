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
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    await ui11.protect(context,info.baseUrl); // UI-11 review hook
    const page = await context.newPage(), errors = [], checks = [], layouts = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('dialog', dialog => dialog.accept());
    page.setDefaultTimeout(20000);
    const url = `${info.baseUrl}/admin/stock-documents/${info.receiptId}`;
    const rows = () => page.locator('.commercial-line');
    const row = id => page.locator(`.commercial-line[data-line-id="${id}"]`);
    const filter = value => page.locator(`[data-commercial-filter="${value}"]`).first();
    async function saved() { await page.waitForFunction(() => !window.GaoReceiptPriceDrafts.hasUnsaved()); }
    async function shot(name) { await page.screenshot({ path: path.join(info.evidenceDirectory, name + '.png'), animations: 'disabled' }); }
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        await page.goto(url);
        await page.locator('.sd-revision-summary').waitFor();
        await page.waitForFunction(() => !!window.GaoReceiptPriceDrafts && document.querySelectorAll('[data-price-review-line-id]').length > 0);
        await page.locator('#priceReviewCounts .badge').first().waitFor({ state: 'attached' });
        const initialOrder = await rows().evaluateAll(elements => elements.map(el => Number(el.dataset.lineId)));
        assert.equal(initialOrder.length, 14);
        assert.equal(await page.locator('.commercial-line.sd-revision-line').count(), 3);
        assert.ok(await page.locator('.sd-revision-line.sd-price-loss').count() > 0, 'Revision markings coexist with an actual below-cost price warning.');
        assert.ok(initialOrder.slice(0, 3).includes(info.changedLineId));
        assert.ok(initialOrder.slice(0, 3).includes(info.addedLineId));
        assert.equal(await row(info.unchangedLineId).locator('.commercial-unit-price').inputValue(), String(info.savedPrice));
        assert.match(await row(info.changedLineId).locator('.sd-revision-change').textContent(), /Số lượng: 2 → 3/);
        assert.match(await row(info.addedLineId).locator('.sd-revision-badge').textContent(), /Bổ sung/);
        checks.push('Actual HTTP return/edit/resubmit puts only 3 changed/added lines first and retains previous review prices.');
        await saved(); await ui11.review(page,'receipt-revision-received',ui11.output('revision')); // UI-11 review hook
        if(ui11.enabled()){const history=page.locator('.sd-revision-history').first(),open=await history.evaluate(node=>node.open);await history.evaluate(node=>node.open=true);await ui11.review(page,'receipt-revision-history-expanded',ui11.output('revision'),{root:'.sd-revision-summary'});await history.evaluate((node,value)=>node.open=value,open);} // UI-11 review hook

        for (const width of [1920, 1440, 1366, 1024, 768, 390, 320]) {
            await page.setViewportSize({ width, height: width < 768 ? 900 : 1000 });
            await page.locator('.sd-revision-summary').scrollIntoViewIfNeeded();
            await page.waitForTimeout(150);
            const metrics = await page.evaluate(() => {
                const bounds = element => { const r = element.getBoundingClientRect(); return { left: r.left, right: r.right, width: r.width, height: r.height }; };
                return {
                    width: innerWidth, pageWidth: document.documentElement.scrollWidth,
                    table: bounds(document.querySelector('.sd-commercial-table-wrap')),
                    prices: [...document.querySelectorAll('.commercial-unit-price')].map(bounds),
                    chips: [...document.querySelectorAll('.sd-filter-chips button')].map(bounds),
                    product: bounds(document.querySelector('.sd-product-identity')),
                    firstRowCells: [...document.querySelector('.commercial-line').cells].filter(cell => !cell.hidden).map(cell => ({
                        label: cell.dataset.label,
                        children: [...cell.children].filter(el => !el.hidden).map(el => ({ cls: el.className, ...bounds(el) }))
                    })),
                    overflowing: [...document.querySelectorAll('.sd-commercial-workbench *')].filter(el => {
                        const r = el.getBoundingClientRect(); return r.width && r.right > innerWidth + 1 && getComputedStyle(el).position !== 'fixed';
                    }).slice(0, 6).map(el => ({ tag: el.tagName, cls: el.className }))
                };
            });
            layouts.push(metrics);
            await shot('goods-' + width);
            if (width < 768) await rows().first().screenshot({ path: path.join(info.evidenceDirectory, `first-card-${width}.png`) });
        }
        assert.ok(layouts.every(m => m.pageWidth <= m.width + 1), 'No page overflow: ' + JSON.stringify(layouts.filter(m => m.pageWidth > m.width + 1)));
        assert.ok(layouts.filter(m => m.width < 768).every(m => m.prices.every(p => p.left >= 0 && p.right <= m.width + 1 && p.width >= 80)), 'Readable mobile price inputs.');
        assert.ok(layouts.filter(m => m.width < 768).every(m => m.prices.every(p => p.height >= 44) && m.chips.every(p => p.height >= 44)), 'Mobile price inputs and filter touch targets are at least 44px tall.');
        checks.push('Real page fits 1920/1440/1366/1024/768/390/320 widths; mobile price fields remain readable.');

        await page.setViewportSize({ width: 390, height: 900 });
        const mobilePrice = row(info.changedLineId).locator('.commercial-unit-price');
        await mobilePrice.fill('76000');
        await saved();
        const mobileBounds = await mobilePrice.boundingBox();
        const footerBounds = await page.locator('.sd-commercial-sticky-actions').boundingBox();
        assert.ok(mobileBounds.y >= 0 && mobileBounds.y + mobileBounds.height <= footerBounds.y, 'Mobile focused price is visible above the action bar.');
        assert.deepEqual(await rows().evaluateAll(elements => elements.map(el => Number(el.dataset.lineId))), initialOrder);
        await page.locator('.sd-revision-line.sd-price-loss [data-price-review-line-id]').first().scrollIntoViewIfNeeded();
        await shot('warning-row-390');
        checks.push('Mobile editing saves without moving rows or covering the focused input; changed-row markings remain visible with price-loss warnings.');

        await page.setViewportSize({ width: 1440, height: 1000 });
        await filter('revision').focus();
        await page.keyboard.press('Space');
        assert.equal(await page.locator('.commercial-line:not(.d-none)').count(), 3);
        assert.equal(await filter('revision').getAttribute('aria-pressed'), 'true');
        assert.equal(await filter('all').getAttribute('aria-pressed'), 'false');
        const activePrice = row(info.changedLineId).locator('.commercial-unit-price');
        await activePrice.fill('76543');
        await saved();
        assert.equal(await activePrice.evaluate(el => document.activeElement === el), true);
        assert.deepEqual(await rows().evaluateAll(elements => elements.map(el => Number(el.dataset.lineId))), initialOrder, 'Price editing must not reorder rows.');
        await filter('all').click();
        assert.equal(await page.locator('.commercial-line:not(.d-none)').count(), 14);
        await page.reload();
        assert.equal(await row(info.changedLineId).locator('.commercial-unit-price').inputValue(), '76543');
        assert.equal(await row(info.unchangedLineId).locator('.commercial-unit-price').inputValue(), String(info.savedPrice));
        checks.push('Revision filter is accessible; price edits preserve focus/order and both changed/unchanged saved prices survive reload.');

        await filter('catalog').click();
        assert.equal(await page.locator('.commercial-line:not(.d-none)').count(), 0);
        await page.locator('#commercialFilterEmpty').waitFor({ state: 'visible' });
        await shot('empty-filter');
        await page.locator('#commercialFilterEmpty [data-commercial-filter="all"]').click();
        assert.equal(await page.locator('.commercial-line:not(.d-none)').count(), 14);
        // Live filter counts follow edits, while typing never hides the focused row.
        const missingInput = row(info.changedLineId).locator('.commercial-unit-price');
        await missingInput.fill('');
        assert.equal(await filter('price').locator('span').textContent(), '1');
        await filter('price').click();
        assert.equal(await page.locator('.commercial-line:not(.d-none)').count(), 1);
        await missingInput.fill('76543');
        assert.equal(await filter('price').locator('span').textContent(), '0');
        assert.equal(await row(info.changedLineId).isVisible(), true, 'Do not hide the focused row while typing.');
        await saved();
        await filter('price').click();
        await page.locator('#commercialFilterEmpty').waitFor({ state: 'visible' });
        await filter('all').click();
        checks.push('Empty filters explain the result and offer reset; missing-price counts update without interrupting typing.');

        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            await page.locator('#commercialRejectButton').click();
            await page.locator('#rejectModal').waitFor({ state: 'visible' });
            await page.locator('#rejectNote').fill('Kiểm tra bổ sung số lượng thực nhận');
            if(width===1440)await ui11.dialog(page,'#rejectModal','receipt-revision-return',ui11.output('revision')); // UI-11 review hook
            await shot('return-modal-' + width);
            const modal = await page.locator('#rejectModal .modal-content').boundingBox();
            assert.ok(modal.x >= 0 && modal.x + modal.width <= width && modal.y >= 0 && modal.y + modal.height <= 900, `Return dialog fits at ${width}: ${JSON.stringify(modal)}`);
            await page.locator('#rejectModal [data-bs-dismiss="modal"]').last().click();
            await page.locator('#rejectModal').waitFor({ state: 'hidden' });
            assert.equal(await rows().count(), 14);
        }
        checks.push('Return dialog is usable on desktop/mobile; cancelling preserves review and current values.');

        // Verify the real click path waits for unsaved prices before returning the fixture receipt.
        await page.setViewportSize({ width: 1440, height: 1000 });
        await row(info.unchangedLineId).locator('.commercial-unit-price').fill('13579');
        const operations = [];
        page.on('request', request => { if (request.method() === 'POST' && /\/(price-draft|reject)$/.test(request.url())) operations.push(request.url().split('/').pop()); });
        await page.locator('#commercialRejectButton').click();
        await page.locator('#rejectNote').fill('Kiểm thử trả phiếu sau khi lưu giá');
        await Promise.all([page.waitForURL(u => u.pathname === '/admin/stock-documents'), page.locator('#btnReject').click()]);
        const state = await (await context.request.get(`${info.baseUrl}/admin/api/stock-documents/${info.receiptId}`)).json();
        assert.equal(state.lines.find(line => line.id === info.unchangedLineId).unitPriceBeforeVat, 13579);
        assert.equal(operations.at(-1), 'reject');
        assert.deepEqual(errors, []);
        checks.push('Actual return button saves pending prices before returning the receipt; no browser JavaScript errors.');
        fs.writeFileSync(path.join(info.evidenceDirectory, 'browser-checks.json'), JSON.stringify({ fixtureOnly: true, checks, layouts, errors }, null, 2));
        console.log('PASS receipt revision UX: ' + checks.join('\n'));
    } catch (error) {
        await shot('failure').catch(() => {});
        fs.writeFileSync(path.join(info.evidenceDirectory, 'browser-checks.json'), JSON.stringify({ fixtureOnly: true, checks, layouts, errors, failure: error.message }, null, 2));
        console.error('UI diagnostic', { url: page.url(), errors, checks });
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

