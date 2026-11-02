'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const mobile = require('./product-catalog-mobile-review.cjs');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let input = ''; process.stdin.on('data', chunk => input += chunk); process.stdin.on('end', () => resolve(input));
    }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const errors = [], observations = [], writes = [];
    let page;
    fs.mkdirSync(info.output, { recursive: true });
    try {
        const login = async account => {
            const context = await browser.newContext({ viewport: { width: 390, height: 844 }, serviceWorkers: 'block' });
            await mobile.protect(context, info.baseUrl);
            const current = await context.newPage();
            current.setDefaultTimeout(30000);
            current.on('pageerror', error => errors.push(error.message));
            current.on('dialog', async dialog => { errors.push('Unexpected native dialog: ' + dialog.message()); await dialog.dismiss(); });
            await current.goto(info.baseUrl + '/admin/account/login');
            await current.locator('[name=UserName]').fill(account.user);
            await current.locator('[name=Password]').fill(account.password);
            if (await current.locator('[name=SelectedTerminalId]').count()) await current.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
            await Promise.all([current.waitForURL(url => !url.pathname.endsWith('/login')), current.locator('button[type=submit]').click()]);
            current.on('request', request => {
                const pathname = new URL(request.url()).pathname;
                if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method()) && !/^\/admin\/api\/store-activity\/(?:online|presence)$/.test(pathname))
                    writes.push({ method: request.method(), pathname });
            });
            return { context, page: current };
        };
        const received = (pathname, predicate = () => true) => page.waitForResponse(response => {
            const url = new URL(response.url());
            return response.request().method() === 'GET' && url.pathname.toLowerCase() === pathname.toLowerCase() && predicate(url);
        });
        const visit = async (route, selector) => {
            const response = await page.goto(info.baseUrl + route);
            assert.equal(response.status(), 200, route);
            await page.locator(selector).first().waitFor();
            await mobile.settle(page);
        };
        const closeModal = async selector => {
            await page.locator(selector + ' .btn-close').first().click();
            await page.locator(selector).waitFor({ state: 'hidden' });
            await mobile.settle(page);
        };
        const productCard = () => page.locator('.gds-mobile-list [data-product-row]').filter({
            has: page.locator(`a[href*="/Edit/${info.ids.Product}"],a[href*="/Edit?id=${info.ids.Product}"],button[data-id="${info.ids.Product}"]`)
        }).first();
        const productListReady = async () => {
            await page.locator('.gds-mobile-list [data-product-row]').first().waitFor();
            await page.waitForFunction(() => document.getElementById('productTableWrapper')?.getAttribute('aria-busy') !== 'true');
            await mobile.settle(page);
        };
        const quickView = async (label, supplierAllowed) => {
            const card = productCard();
            assert.equal(await card.count(), 1, 'Own seeded product card');
            const units = JSON.parse(await card.getAttribute('data-quick-units'));
            assert.equal(units.length, 3);
            assert.deepEqual(units.map(unit => unit.factor).sort((a, b) => a - b), [1, 4, 24]);
            const pack = units.find(unit => unit.unitName === 'Lốc'), carton = units.find(unit => unit.unitName === 'Thùng');
            assert.deepEqual([pack.retailPrice, pack.wholesalePrice, carton.retailPrice, carton.wholesalePrice], [48000, 45000, 280000, null]);
            for (const unit of units) {
                assert.equal(Object.hasOwn(unit, 'costPrice'), false);
                assert.equal(Object.hasOwn(unit, 'supplierName'), false);
            }
            assert.equal(await page.locator('#productQuickViewSupplier').count(), supplierAllowed ? 1 : 0);
            await card.locator('.js-quick-view').last().click();
            await page.locator('#productQuickViewModal.show').waitFor();
            assert.equal(await page.locator('#productQuickViewUnits tbody tr').count(), 3);
            const packText = await page.locator('#productQuickViewUnits tr[data-unit-name="Lốc"]').innerText();
            assert.match(packText, /48\.000/); assert.match(packText, /45\.000/);
            assert.match(await page.locator('#productQuickViewUnits tr[data-unit-name="Thùng"]').innerText(), /Theo giá lẻ/);
            if (supplierAllowed) assert.match(await page.locator('#productQuickViewSupplier').innerText(), /NCC-PRIVATE-ONLY/);
            else assert.equal((await page.content()).includes('NCC-PRIVATE-ONLY'), false);
            await mobile.dialog(page, '#productQuickViewModal', label, info.output);
            await closeModal('#productQuickViewModal');
            return units;
        };

        const manager = await login(info.manager); page = manager.page;
        await visit('/Admin/Product/Index?lifecycle=all', '[data-product-page]');
        await productListReady();
        await mobile.review(page, 'product-native-index', info.output, { root: '[data-product-page]' });
        const saleUnits = await quickView('product-admin-unit-prices', true);
        const nativeSearch = received('/Admin/Product/Search', url => url.searchParams.get('search') === saleUnits[0].sku);
        await page.locator('#txtSearch').fill(saleUnits[0].sku);
        assert.equal((await nativeSearch).status(), 200); await productListReady();
        assert.equal(await productCard().count(), 1);
        await page.locator('[data-product-phone-filters] > summary').click();
        const brandOptions = received('/Admin/Product/FilterOptions', url => url.searchParams.get('kind') === 'brand' && url.searchParams.get('term') === 'UI14-BRAND');
        await page.locator('#filter-brand').fill('UI14-BRAND');
        assert.equal((await brandOptions).status(), 200);
        const brandSearch = received('/Admin/Product/Search', url => url.searchParams.get('brandId') === String(info.ids.Brand));
        await page.locator('#options-brand [role=option]').filter({ hasText: 'UI14-BRAND' }).click();
        assert.equal((await brandSearch).status(), 200); await productListReady();
        assert.equal(await page.locator('[data-product-lookup=brand] input[type=hidden]').inputValue(), String(info.ids.Brand));
        await page.locator('[data-product-phone-filters] > summary').click();
        assert.equal(await page.locator('[data-product-phone-filters]').evaluate(node => node.open), false);
        assert.equal(new URL(page.url()).searchParams.get('brandId'), String(info.ids.Brand));
        assert.equal(await page.locator('#txtSearch').inputValue(), saleUnits[0].sku);
        const barcodeSearch = received('/Admin/Product/Search', url => url.searchParams.get('search') === 'INTERNAL-PACK');
        await page.locator('#txtSearch').fill('INTERNAL-PACK');
        assert.equal((await barcodeSearch).status(), 200); await productListReady();
        assert.equal(await productCard().count(), 1);
        await mobile.review(page, 'product-native-brand-and-barcode-search', info.output, { root: '[data-product-page]' });
        observations.push('Original product SKU search and quick view show factor1/4/24 units, retail/wholesale prices and carton retail fallback; supplier remains permission controlled.');

        await visit('/Admin/Product/Create', '.product-setup-workspace');
        await mobile.review(page, 'product-native-create', info.output, { root: '.product-setup-workspace' });
        await visit('/Admin/Product/Edit/' + info.ids.Product, '.product-setup-workspace');
        await page.locator('#variantBody .vv-variant-item').first().waitFor();
        await mobile.settle(page);
        await mobile.review(page, 'product-native-edit', info.output, { root: '.product-setup-workspace' });
        const sectionLinks = page.locator('[data-product-phone-sections] a');
        assert.ok(await sectionLinks.count() >= 4);
        await sectionLinks.last().click();
        assert.match(page.url(), /#variants$/);
        const conversionResponse = received('/Admin/ProductUnitConversion/GetByVariantId', url => url.searchParams.get('productVariantId') === String(info.variantId));
        await page.locator('#variantBody .vv-variant-item .js-unit-conv').first().click();
        assert.equal((await conversionResponse).status(), 200);
        await page.locator('#mdlVariantUnitConversions.show').waitFor();
        await page.locator('#conversionListWrap tr.uc-click-row').first().waitFor();
        assert.equal(await page.locator('#uc_ProductVariantId').inputValue(), String(info.variantId));
        assert.equal(await page.locator('#conversionListWrap tr.uc-click-row').count(), 3);
        await mobile.dialog(page, '#mdlVariantUnitConversions', 'product-native-units-and-barcodes', info.output);
        const packRow = page.locator(`#conversionListWrap tr.uc-click-row[data-id="${info.packId}"]`);
        await packRow.click();
        await page.locator('#barcodeListWrap .bc-click-row').first().waitFor();
        await packRow.locator('.js-edit-conversion').click();
        await page.locator('#ucxConversionEditor.is-open').waitFor();
        assert.equal(await page.locator('#uc_Factor').inputValue(), '4');
        assert.equal(await page.locator('#uc_Price').inputValue(), '48000');
        assert.equal(await page.locator('#uc_WholesalePrice').inputValue(), '45000');
        await mobile.dialog(page, '#ucxConversionEditor', 'product-native-conversion-editor-cancel', info.output);
        await page.locator('#btnCloseConversionEditor').click();
        await page.waitForFunction(() => !document.getElementById('ucxConversionEditor').classList.contains('is-open'));
        await page.locator('#barcodeListWrap .js-edit-barcode').first().click();
        await page.locator('#ucxBarcodeEditor.is-open').waitFor();
        assert.ok((await page.locator('#bc_Barcode').inputValue()).length > 0);
        await mobile.dialog(page, '#ucxBarcodeEditor', 'product-native-barcode-editor-cancel', info.output);
        await page.locator('#btnCloseBarcodeEditor').click();
        await page.waitForFunction(() => !document.getElementById('ucxBarcodeEditor').classList.contains('is-open'));
        const historyResponse = page.waitForResponse(response => response.request().method() === 'GET' && /history/i.test(new URL(response.url()).pathname));
        await page.locator('#btnReloadBarcodeHistory').click(); assert.equal((await historyResponse).status(), 200);
        await page.waitForFunction(() => !document.getElementById('barcodeHistoryWrap').textContent.includes('Đang tải'));
        assert.match(await page.locator('#barcodeHistoryWrap').innerText(), /UI-14|2600140000048/);
        await closeModal('#mdlVariantUnitConversions');
        await page.locator('#btnOpenMobileVariantDetail').click();
        await page.locator('#variantDetailSheet.is-open').waitFor();
        assert.ok((await page.locator('#variantDetailSheetContent .js-detail-sku').inputValue()).length > 0);
        await mobile.dialog(page, '#variantDetailSheet', 'product-native-variant-detail-sheet', info.output);
        const imageListResponse = received('/Admin/Product/ImagesData', url => url.searchParams.get('productId') === String(info.ids.Product));
        await page.locator('#variantDetailSheetContent .js-detail-pick-img').click();
        assert.equal((await imageListResponse).status(), 200);
        await page.locator('#mdlPickVariantImage.show').waitFor();
        assert.match(await page.locator('#pickImgGrid').innerText(), /Sản phẩm chưa có ảnh/);
        await mobile.dialog(page, '#mdlPickVariantImage', 'product-native-variant-image-picker-cancel', info.output);
        await closeModal('#mdlPickVariantImage');
        assert.equal(await page.locator('#variantDetailSheet').evaluate(node => node.classList.contains('is-open')), true);
        await page.locator('#btnCloseVariantDetailSheet').click();
        await page.waitForFunction(() => !document.getElementById('variantDetailSheet').classList.contains('is-open'));
        await page.locator('#variantBody .vv-variant-item .js-open-quick-view').first().click();
        await page.locator('#mdlVariantQuickView.show').waitFor();
        assert.ok((await page.locator('#variantQuickViewBody').innerText()).includes(saleUnits[0].sku));
        await mobile.dialog(page, '#mdlVariantQuickView', 'product-native-variant-quick-view', info.output);
        await closeModal('#mdlVariantQuickView');
        observations.push('Original variant selection opens its own conversion modal; pack4/48000/45000 and selected barcode forms survive all resize checks, editors close without saving, contextual history reload uses native GET. Native variant image picker opens from the original clickable empty image target, loads its own product images and cancels without choosing or clearing an image; original variant detail sheet remains open.');
        await visit('/Admin/Product/Detail/' + info.ids.Product, '.p-main');
        await mobile.review(page, 'product-native-detail', info.output, { root: '.row.g-4:has(.p-main)' });

        const catalogKinds = ['Brand', 'Category', 'Unit', 'Tax', 'ProductAttribute', 'AttributeValue'];
        for (const kind of catalogKinds) {
            console.log('[UI-14 native catalog]', kind);
            await visit(`/Admin/${kind}/Index`, '[data-layout-consistency]');
            await page.locator('.gds-mobile-list .gds-row-card').first().waitFor();
            await mobile.review(page, kind.toLowerCase() + '-native-index', info.output, { root: '[data-layout-consistency]' });
            const firstCard = page.locator('.gds-mobile-list .gds-row-card').first();
            const keyword = (await firstCard.locator('.gds-row-card__title').innerText()).trim();
            const searchResponse = received(`/Admin/${kind}/Search`);
            await page.locator('#txtSearch').fill(keyword);
            assert.equal((await searchResponse).status(), 200);
            await page.waitForFunction(() => [...document.querySelectorAll('.gds-results-region')].every(node => node.getAttribute('aria-busy') !== 'true'));
            await mobile.settle(page);
            assert.ok((await page.locator('.gds-mobile-list').innerText()).includes(keyword));
            await page.locator('.gds-mobile-list .js-delete').first().click();
            await page.locator('.swal2-popup').waitFor();
            await mobile.dialog(page, '.swal2-popup', kind.toLowerCase() + '-native-delete-cancel', info.output);
            await page.locator('.swal2-cancel').click();
            await page.locator('.swal2-popup').waitFor({ state: 'hidden' });

            await visit(`/Admin/${kind}/Create`, '.catalog-editor');
            await mobile.review(page, kind.toLowerCase() + '-native-create', info.output, { root: '.catalog-editor' });
            const codeBefore = await page.locator('#Code').inputValue();
            const versionBefore = await page.locator('[name=RowVersion]').count() ? await page.locator('[name=RowVersion]').inputValue() : null;
            assert.ok(await page.locator('#Name').getAttribute('data-val-required'), kind + ' required native Name rule');
            await page.locator('#Name').fill('');
            await page.locator('.ce-save').click();
            await page.locator('[data-valmsg-for=Name].field-validation-error').waitFor();
            assert.equal(await page.locator('#Code').inputValue(), codeBefore);
            if (versionBefore !== null) assert.equal(await page.locator('[name=RowVersion]').inputValue(), versionBefore);

            await visit(`/Admin/${kind}/Edit/${info.ids[kind]}`, '.catalog-editor');
            assert.equal(await page.locator('#Code').isEditable(), false);
            await mobile.review(page, kind.toLowerCase() + '-native-edit', info.output, { root: '.catalog-editor' });
        }
        observations.push('All six catalog Index/Create/Edit pages reviewed; original native search refresh, delete confirmation cancellation, required Name validation and immutable edit Code/RowVersion behavior retained.');

        await visit('/admin/barcode-history', '#barcodeHistoryPage');
        assert.match(await page.locator('#barcodeHistoryList').innerText(), /2600140000048/);
        await mobile.review(page, 'barcode-history-native-index', info.output, { root: '#barcodeHistoryPage' });
        const historyFilter = received('/admin/api/barcode-history', url => url.searchParams.get('Keyword') === '2600140000048');
        await page.locator('#barcodeHistoryFilterForm [name=Keyword]').fill('2600140000048');
        await page.locator('#barcodeHistoryFilterForm button[type=submit]').click();
        assert.equal((await historyFilter).status(), 200);
        await page.locator('#barcodeHistoryLoading').waitFor({ state: 'hidden' });
        assert.match(await page.locator('#barcodeHistoryList').innerText(), /2600140000048/);

        const normalization = received('/admin/barcode-normalization/list');
        await visit('/admin/barcode-normalization', '#barcodeRequestTableBody');
        assert.equal((await normalization).status(), 200);
        await page.locator(`#barcodeRequestTableBody .js-approve-barcode[data-id="${info.requestId}"]`).waitFor();
        await mobile.review(page, 'barcode-normalization-native-index', info.output, { root: '.container-xxl:has(#barcodeRequestKeyword)' });
        await page.locator(`#barcodeRequestTableBody .js-approve-barcode[data-id="${info.requestId}"]`).click();
        await page.locator('#barcodeResolveModal.show').waitFor();
        assert.equal(await page.locator('#barcodeResolveId').inputValue(), String(info.requestId));
        assert.equal(await page.locator('#barcodeResolveAction').inputValue(), 'approve');
        await mobile.dialog(page, '#barcodeResolveModal', 'barcode-normalization-native-approval-cancel', info.output);
        await closeModal('#barcodeResolveModal');
        const normalizationAll = received('/admin/barcode-normalization/list', url => url.searchParams.get('status') === '');
        await page.locator('#barcodeRequestStatus').selectOption('');
        assert.equal((await normalizationAll).status(), 200);
        await page.locator(`#barcodeRequestTableBody .js-approve-barcode[data-id="${info.requestId}"]`).waitFor();
        observations.push('Native barcode history filter and normalization pending/all status read paths reviewed; approval opens exact request modal and is cancelled without command.');
        await manager.context.close();

        const viewer = await login(info.viewer); page = viewer.page;
        await visit('/Admin/Product/Index?lifecycle=all', '[data-product-page]'); await productListReady();
        await mobile.review(page, 'product-viewer-native-supplier-private', info.output, { root: '[data-product-page]' });
        await quickView('product-viewer-unit-prices-without-supplier', false);
        for (const kind of catalogKinds) {
            await visit(`/Admin/${kind}/Index`, '[data-layout-consistency]');
            await page.locator('.gds-mobile-list .gds-row-card').first().waitFor();
            if (['Category', 'ProductAttribute', 'AttributeValue'].includes(kind)) {
                assert.equal(await page.locator(`a[href="/Admin/${kind}/Create"]`).count(), 0);
                assert.equal(await page.locator('.gds-mobile-list .js-delete,.gds-mobile-list .js-toggle-status,.gds-mobile-list a[href*="/Edit/"]').count(), 0);
            }
        }
        observations.push('Read-only catalog viewer retains original permission-gated actions; product viewer receives the same selling prices and no supplier/cost data. Controller denied/cross-tenant checks and complete persistent-state fingerprint verified by paired .NET fixture.');
        await viewer.context.close();
        assert.deepEqual(writes, [], 'No native validation/cancel/read workflow issued a business write');
        assert.deepEqual(errors, [], 'No original owner JavaScript errors or unexpected native dialog');
        fs.writeFileSync(path.join(info.output, 'browser-checks.json'), JSON.stringify({ passed: true, observations, errors, writes,
            existingChromeWindowsUntouched: true, freshHeadlessContexts: true, standardAssetNegotiation: !process.env.GAO_UI14_RAW_ASSETS }, null, 2) + '\n');
        console.log(JSON.stringify({ passed: true, observations }));
    } catch (error) {
        console.error(error.stack || error);
        if (page && !page.isClosed()) {
            await page.screenshot({ path: path.join(info.output, 'browser-failure.png'), fullPage: true, animations: 'disabled' }).catch(() => {});
            fs.writeFileSync(path.join(info.output, 'browser-failure.json'), JSON.stringify({ error: String(error), url: page.url(), errors, writes, observations }, null, 2) + '\n');
        }
        process.exitCode = 1;
    } finally { await browser.close(); }
})();
