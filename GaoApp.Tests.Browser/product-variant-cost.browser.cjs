'use strict';
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const web = path.resolve(__dirname, '../GaoApp.Web');
const partial = fs.readFileSync(path.join(web, 'Areas/Admin/Views/Product/_Variants.cshtml'), 'utf8');
const edit = fs.readFileSync(path.join(web, 'Areas/Admin/Views/Product/Edit.cshtml'), 'utf8');
const saveAll = [...edit.matchAll(/<script>([\s\S]*?)<\/script>/g)].find(x => x[1].includes('async function saveAll()'))[1];
const body = (partial.slice(partial.indexOf('<div'), partial.indexOf('@{')) + partial.slice(partial.indexOf('<style>')))
    .replaceAll('@@media', '@media').replace('@Model.ProductId', '123')
    .replace('@Html.Raw(System.Text.Json.JsonSerializer.Serialize(Model.ProductAlias ?? string.Empty))', '"test-product"');
let saved = [], rows;
const seed = () => [
    { id: 1, sku: 'hop', productVariantName: 'Sữa hộp', costPrice: 5000, isActive: true },
    { id: 2, sku: 'bich', productVariantName: 'Bánh tráng Tây Ninh', costPrice: 0, isActive: true },
    { id: 3, sku: 'chai', productVariantName: 'Nước chai ngưng bán', costPrice: 0, isActive: false }
];
const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, 'http://localhost');
    if (url.pathname === '/core.css') { res.setHeader('Content-Type', 'text/css'); res.end(fs.readFileSync(path.join(web, 'wwwroot/vendor/css/core.css'))); return; }
    if (url.pathname.endsWith('/AttributesData') || url.pathname.endsWith('/ImagesData')) {
        res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify({ ok: true, data: [] })); return;
    }
    if (url.pathname.endsWith('/VariantsData')) {
        res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify({ ok: true, data: rows })); return;
    }
    if (url.pathname.endsWith('/SaveVariants')) {
        let data = ''; for await (const chunk of req) data += chunk;
        const payload = JSON.parse(data); saved.push(payload); rows = payload.variants;
        res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify({ ok: true })); return;
    }
    if (url.pathname !== '/') { res.statusCode = 404; res.end(); return; }
    res.setHeader('Content-Type', 'text/html; charset=utf-8');
    res.end(`<!doctype html><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/core.css">
        <style>body{padding:20px}.vv-attr-card{min-height:20px}</style>
        <form id="productSetupForm"><input id="txtName" value="Tên chưa lưu"><input name="__RequestVerificationToken" value="test-token">
        <button id="btnSaveAll" type="button">Lưu thay đổi</button><section id="variants">${body}</section></form>
        <script>window.warnings=[];window.submitCount=0;
        window.toastr={warning:m=>warnings.push(m),success:()=>{},info:()=>{},error:m=>warnings.push(m)};
        window.hasPendingProductUploads=()=>false;
        document.getElementById('productSetupForm').submit=()=>submitCount++;
        ${saveAll}</script>`);
});
(async () => {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const base = `http://127.0.0.1:${server.address().port}`;
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const page = await browser.newPage(); page.setDefaultTimeout(10000);
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    const output = path.resolve('TestResults/product-variant-cost'); fs.mkdirSync(output, { recursive: true });
    try {
        for (const mobile of [false, true]) {
            saved = []; rows = seed();
            await page.setViewportSize(mobile ? { width: 390, height: 844 } : { width: 1500, height: 1000 });
            await page.goto(base);
            await page.locator('#variantBody .vv-variant-item').nth(2).waitFor();
            const detail = mobile ? '#variantDetailSheetContent' : '#variantDetailPanel';
            await page.locator('#txtName').fill('Tên sản phẩm đang chỉnh');
            await page.locator('#btnSaveVariants').click();
            await page.waitForFunction(detail => document.activeElement === document.querySelector(detail + ' .js-detail-cost'), detail);
            assert.equal(await page.locator(detail + ' .js-detail-cost').getAttribute('data-idx'), '1');
            assert.equal(await page.locator(detail + ' .js-detail-cost').getAttribute('aria-invalid'), 'true');
            assert.equal(saved.length, 0);
            assert.match((await page.evaluate(() => warnings)).at(-1), /Bánh tráng Tây Ninh/);
            assert.equal(await page.locator('#txtName').inputValue(), 'Tên sản phẩm đang chỉnh');
            if (mobile) assert.equal(await page.locator('#variantDetailSheet').getAttribute('aria-hidden'), 'false');
            await page.screenshot({ path: path.join(output, mobile ? 'cost-mobile.png' : 'cost-desktop.png') });
            await page.locator(detail + ' .js-detail-name').fill('Bánh tráng Tây Ninh đã chỉnh');
            await page.keyboard.press('Control+s');
            await page.waitForFunction(detail => document.activeElement === document.querySelector(detail + ' .js-detail-cost'), detail);
            assert.equal(await page.locator(detail + ' .js-detail-name').inputValue(), 'Bánh tráng Tây Ninh đã chỉnh');
            assert.equal(saved.length, 0);
            await page.locator(detail + ' .js-detail-cost').fill('12000');
            if (mobile) await page.locator('#btnCloseVariantDetailSheet').click();
            await page.locator('#btnSaveAll').click();
            await page.waitForFunction(detail => document.activeElement === document.querySelector(detail + ' .js-detail-cost') && document.activeElement.dataset.idx === '2', detail);
            assert.equal(saved.length, 0);
            assert.equal(await page.evaluate(() => submitCount), 0);
            assert.equal((await page.evaluate(() => warnings)).length, 3, 'Save all must not display a second generic error');
            assert.equal(await page.locator(detail + ' .js-detail-cost').isEditable(), true, 'Inactive variants can correct missing cost');
            await page.locator(detail + ' .js-detail-cost').fill('');
            if (mobile) await page.locator('#btnCloseVariantDetailSheet').click();
            await page.locator('#btnSaveVariants').click();
            await page.waitForFunction(detail => document.activeElement === document.querySelector(detail + ' .js-detail-cost'), detail);
            assert.equal(saved.length, 0, 'Blank cost also blocks saving');
            await page.locator(detail + ' .js-detail-cost').fill('2500');
            if (mobile) await page.locator('#btnCloseVariantDetailSheet').click();
            await page.locator('#btnSaveAll').click();
            await page.waitForFunction(() => submitCount === 1);
            assert.equal(saved.length, 1);
            assert.deepEqual(saved[0].variants.map(x => x.costPrice), [5000, 12000, 2500]);
            assert.equal(saved[0].variants[2].isActive, false);
            assert.equal(saved[0].variants[1].productVariantName, 'Bánh tráng Tây Ninh đã chỉnh');
        }
        assert.deepEqual(errors, []);
        console.log('PASS zero/blank cost blocks both save buttons, focuses first invalid variant on desktop/mobile, preserves edits, supports inactive variants and saves only after all costs are corrected.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode = 1; server.close(); });
