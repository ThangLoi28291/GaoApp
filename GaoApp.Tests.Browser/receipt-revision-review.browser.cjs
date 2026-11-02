'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const root = path.resolve(__dirname, '..');
    const read = file => fs.readFileSync(path.join(root, 'GaoApp.Web', file), 'utf8');
    const css = ['vendor/css/core.css', 'Admin/css/purchase-receipt-approval.css', 'Admin/css/receipt-selling-prices.css']
        .map(file => read('wwwroot/' + file)).join('\n');
    const source = read('wwwroot/Admin/js/stock-document-management.js');
    const filters = source.slice(source.indexOf('function initLegacyCatalogReview()'), source.indexOf('async function saveLegacyCatalogProduct()'));
    const output = path.join(root, 'TestResults/receipt-revision-review');
    fs.mkdirSync(output, { recursive: true });
    const row = (id, kind, priceClass) => `<tr class="commercial-line ${kind ? 'sd-revision-line' : ''} ${priceClass}" data-line-id="${id}" data-revision-changed="${Boolean(kind)}" data-catalog-review="false" data-price-missing="false" data-exception="false">
        <td data-label="Ảnh"><span>Ảnh SP</span></td>
        <td data-label="Sản phẩm"><div class="sd-product-identity"><strong>Sữa chua lên men ít đường chai 85ml · Dòng ${id}</strong>${kind ? `<div class="sd-revision-change"><span class="sd-revision-badge">${kind === 'added' ? 'Bổ sung' : 'Đã chỉnh sửa'}</span><small>${kind === 'added' ? 'Dòng hàng bổ sung sau lần trả về gần nhất.' : 'Số lượng: 12 → 24 Chai · Ghi chú đã chỉnh sửa.'}</small></div>` : ''}</div></td>
        <td data-label="Giá nhập"><input class="form-control commercial-unit-price" value="${1000 + id}" aria-label="Giá dòng ${id}"></td>
        <td data-label="Trạng thái">${priceClass === 'sd-price-loss' ? 'Có giá bán dưới vốn' : 'Đã kiểm tra, giữ giá'}</td></tr>`;
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        const errors = []; page.on('pageerror', error => errors.push(error.message));
        await page.setContent(`<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style></head><body><main style="padding:16px">
            <div class="sd-table-toolbar"><h5>Hàng hóa thực nhận</h5><div class="sd-filter-chips"><button type="button" class="active" data-commercial-filter="all">Tất cả <span>3</span></button><button type="button" data-commercial-filter="revision">Đã sửa / bổ sung <span>2</span></button></div></div>
            <div class="sd-revision-summary" role="note"><div><strong>Thay đổi sau lần trả về gần nhất</strong><div>1 dòng bổ sung · 1 dòng chỉnh sửa · 1 dòng đã xóa</div><small>Trả về lúc 16/10/2026 10:00 · Gửi duyệt lại lúc 16/10/2026 10:30. Các dòng bổ sung hoặc chỉnh sửa được đưa lên đầu để đối chiếu; giá nháp đã lưu được giữ lại.</small></div></div>
            <div class="table-responsive sd-commercial-table-wrap"><table class="table sd-commercial-table"><thead><tr><th>Ảnh</th><th>Sản phẩm</th><th>Giá nhập</th><th>Trạng thái</th></tr></thead><tbody>${row(3, 'added', 'sd-price-reviewed')}${row(2, 'changed', 'sd-price-loss')}${row(1, null, 'sd-price-reviewed')}</tbody></table></div>
            </main></body></html>`);
        await page.addScriptTag({ content: filters + '\ninitLegacyCatalogReview();' });
        const ids = () => page.locator('.commercial-line:visible').evaluateAll(rows => rows.map(row => row.dataset.lineId));
        assert.deepEqual(await ids(), ['3', '2', '1']);
        await page.locator('[data-commercial-filter="revision"]').click();
        assert.deepEqual(await ids(), ['3', '2']);
        await page.locator('[data-commercial-filter="all"]').click();
        assert.deepEqual(await ids(), ['3', '2', '1']);
        const input = page.locator('[data-line-id="2"] .commercial-unit-price');
        await input.fill('24000');
        assert.equal(await input.inputValue(), '24000');
        assert.deepEqual(await ids(), ['3', '2', '1'], 'Entering a price never moves a row.');
        const markers = await page.locator('.sd-revision-line > td:first-child').evaluateAll(cells => cells.map(cell => getComputedStyle(cell).borderLeftWidth));
        assert.deepEqual(markers, ['4px', '4px'], 'Revision borders coexist with reviewed and loss warning markers.');
        assert.notEqual(await page.locator('[data-line-id="2"] > td').first().evaluate(cell => getComputedStyle(cell).backgroundColor), await page.locator('[data-line-id="3"] > td').first().evaluate(cell => getComputedStyle(cell).backgroundColor), 'Price loss highlighting remains distinct.');
        for (const width of [1440, 390, 320]) {
            await page.setViewportSize({ width, height: 1000 });
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `No page overflow at ${width}.`);
            assert.equal(await page.locator('.sd-revision-badge:visible').count(), 2);
            await page.screenshot({ path: path.join(output, `review-${width}.png`), fullPage: true });
        }
        assert.deepEqual(errors, []);
        console.log('PASS: revision filtering restores unchanged goods, pricing inputs stay in place, changed markers coexist with price warnings, desktop and mobile layouts fit.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
