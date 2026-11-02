const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const inventoryMobile = require('./inventory-mobile-review.cjs'); // UI-12 review hook

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let input = ''; process.stdin.on('data', chunk => input += chunk); process.stdin.on('end', () => resolve(input));
    }));
    fs.mkdirSync(info.output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
        await inventoryMobile.protect(context, info.baseUrl); // UI-12 review hook
        const page = await context.newPage(), errors = [], observations = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('[name=SelectedTerminalId]').count())
            await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        if(inventoryMobile.enabled()){await page.goto(info.baseUrl+'/admin/stock-counts');await page.waitForFunction(()=>document.querySelector('#stockCountResultsPanel[aria-busy="false"]')&&document.querySelector('#stockCountMobileList [data-document-id]')&&document.getElementById('scIndexWarehouse')?.options.length>1);await inventoryMobile.review(page,'count-list',inventoryMobile.output('stock-count'),{root:'[data-stock-count-index]'});} // UI-12 review hook
        if(inventoryMobile.enabled()){await page.setViewportSize({width:390,height:844});await page.locator('#scIndexOpenMobileFilters').click();await inventoryMobile.dialog(page,'#stockCountMobileFilterSheet','count-filters',inventoryMobile.output('stock-count'));await page.locator('#stockCountMobileFilterSheet [data-bs-dismiss=offcanvas]').click();await page.locator('#stockCountMobileFilterSheet').waitFor({state:'hidden'});await page.locator(`#stockCountMobileList [data-document-id="${info.locked.id}"] .sc-index-quick-button`).click();await page.waitForFunction(()=>document.getElementById('quickViewBody')?.textContent.includes('B6 original'));await inventoryMobile.dialog(page,'#stockCountQuickViewModal','count-quick-view',inventoryMobile.output('stock-count'));await page.locator('#stockCountQuickViewModal .btn-close').click();await page.setViewportSize({width:1440,height:1000});} // UI-12 review hook
        // Delay the options response: the persisted selection must survive initial load.
        await page.route('**/admin/api/warehouses/select2?term=*', async route => {
            await new Promise(resolve => setTimeout(resolve, 250));
            await route.continue();
        });
        const open = async count => {
            await page.goto(info.baseUrl + '/admin/stock-counts/' + count.id);
            await page.waitForFunction(id => document.getElementById('WarehouseId')?.value === String(id), count.source);
            await page.keyboard.press('Escape');
        };
        const api = (method, url, data) => page.evaluate(async ({ method, url, data }) => {
            const response = await fetch(url, { method, headers: { 'Content-Type': 'application/json' },
                body: data === undefined ? undefined : JSON.stringify(data) });
            return { status: response.status, body: await response.json() };
        }, { method, url, data });
        const add = id => api('POST', `/admin/api/stock-counts/${id}/lines`,
            { productVariantId: info.variantId, unitId: info.unitId, countedQty: 12 });
        const reload = () => page.evaluate(() => loadStockCountDetail());
        const waitWarehouse = (id, disabled) => page.waitForFunction(({ id, disabled }) => {
            const warehouse = document.getElementById('WarehouseId');
            return warehouse?.value === String(id) && warehouse.disabled === disabled;
        }, { id, disabled });

        await open(info.locked);
        assert.equal(await page.locator('#WarehouseId').isDisabled(), true);
        assert.equal(await page.locator('#DocumentDate').isEnabled(), true);
        assert.equal(await page.locator('#DocumentNote').isEnabled(), true);
        assert.equal(await page.locator('#StockCountWarehouseLockHint').isVisible(), true);
        assert.match(await page.locator('#StockCountWarehouseLockHint').innerText(), /không thể đổi kho/);
        await inventoryMobile.review(page,'count-warehouse-locked',inventoryMobile.output('stock-count'),{root:'.scd-page'}); // UI-12 review hook
        if(inventoryMobile.enabled()){await page.setViewportSize({width:390,height:844});await page.locator('.btn-edit-stock-count-line').click();await inventoryMobile.dialog(page,'#stockCountLineModal','count-edit-line',inventoryMobile.output('stock-count'));await page.locator('#stockCountLineModal .btn-close').click();await page.keyboard.press('Escape');await page.locator('#ToggleOnlyDifferentLines').check();await inventoryMobile.review(page,'count-native-difference-filter',inventoryMobile.output('stock-count'),{root:'.scd-page'});await page.locator('#ToggleOnlyDifferentLines').uncheck();await page.setViewportSize({width:1440,height:1000});} // UI-12 review hook
        await page.screenshot({ path: path.join(info.output, 'locked-desktop.png'), fullPage: true });
        await page.setViewportSize({ width: 390, height: 844 });
        assert.equal(await page.locator('#WarehouseId').isDisabled(), true);
        assert.equal(await page.locator('#StockCountWarehouseLockHint').isVisible(), true);
        await page.screenshot({ path: path.join(info.output, 'locked-mobile.png'), fullPage: true });
        await page.setViewportSize({ width: 1440, height: 1000 });
        observations.push('Existing lines lock only the warehouse in editable documents, desktop and mobile.');

        await open(info.empty);
        await waitWarehouse(info.empty.source, false);
        await inventoryMobile.review(page,'count-empty-editable',inventoryMobile.output('stock-count'),{root:'.scd-page'}); // UI-12 review hook
        const changeEmpty = page.waitForResponse(response => response.url().endsWith('/admin/api/stock-counts/update-header') && response.request().method() === 'POST');
        await page.locator('#WarehouseId').selectOption(String(info.empty.destination));
        assert.equal((await changeEmpty).status(), 200);
        await waitWarehouse(info.empty.destination, false);
        await page.waitForFunction(id => stockCountUiState.currentDetail?.warehouseId === id, info.empty.destination);
        observations.push('Empty document changes warehouse and reloads the saved selection.');

        await open(info.stale);
        assert.equal(await page.locator('#WarehouseId').isEnabled(), true);
        // This represents an add from a different tab: no local UI refresh occurs.
        const added = await add(info.stale.id);
        assert.equal(added.status, 200);
        await page.locator('#DocumentNote').fill('B6 unsaved note preserved');
        const rejection = page.waitForResponse(response => response.url().endsWith('/admin/api/stock-counts/update-header') && response.request().method() === 'POST');
        await page.locator('#WarehouseId').selectOption(String(info.stale.destination));
        assert.equal((await rejection).status(), 400);
        await waitWarehouse(info.stale.source, true);
        assert.equal(await page.locator('#DocumentNote').inputValue(), 'B6 unsaved note preserved');
        assert.equal(await page.locator('#StockCountWarehouseLockHint').isVisible(), true);
        const persisted = await api('GET', `/admin/api/stock-counts/${info.stale.id}`);
        assert.equal(persisted.body.warehouseId, info.stale.source);
        assert.equal(persisted.body.note, 'B6 original');
        // Saving other inputs remains possible with the server's warehouse restored.
        await page.evaluate(() => saveStockCountHeader(false));
        assert.equal((await api('GET', `/admin/api/stock-counts/${info.stale.id}`)).body.note, 'B6 unsaved note preserved');
        observations.push('Stale warehouse change is rejected, saved warehouse is restored, and other input edits remain available.');

        assert.equal((await api('DELETE', `/admin/api/stock-counts/lines/${added.body.id}`)).status, 200);
        await reload();
        await waitWarehouse(info.stale.source, false);
        assert.equal(await page.locator('#StockCountWarehouseLockHint').isVisible(), false);
        const changed = page.waitForResponse(response => response.url().endsWith('/admin/api/stock-counts/update-header') && response.request().method() === 'POST');
        await page.locator('#WarehouseId').selectOption(String(info.stale.destination));
        assert.equal((await changed).status(), 200);
        await page.waitForFunction(id => stockCountUiState.currentDetail?.warehouseId === id, info.stale.destination);
        assert.equal((await add(info.stale.id)).status, 200);
        await reload();
        await waitWarehouse(info.stale.destination, true);
        const detail = await api('GET', `/admin/api/stock-counts/${info.stale.id}`);
        assert.equal(detail.body.lines.length, 1);
        assert.equal(detail.body.lines[0].systemQtyBase, 0);
        assert.equal(detail.body.lines[0].differenceQtyBase, 12);
        await inventoryMobile.review(page,'count-new-warehouse-difference',inventoryMobile.output('stock-count'),{root:'.scd-page'}); // UI-12 review hook
        observations.push('Deleting all lines reenables warehouse selection; a new line snapshots the new warehouse.');

        await open(info.moved);
        assert.equal((await api('POST', '/admin/api/stock-counts/update-header', {
            stockCountDocumentId: info.moved.id, warehouseId: info.moved.destination,
            note: 'B6 other tab changed warehouse'
        })).status, 200);
        assert.equal((await add(info.moved.id)).status, 200);
        await page.locator('#DocumentNote').fill('B6 note from an older tab');
        const staleNote = await page.waitForResponse(response => response.url().endsWith('/admin/api/stock-counts/update-header') && response.request().method() === 'POST');
        assert.equal(staleNote.status(), 400);
        await waitWarehouse(info.moved.destination, true);
        assert.equal(await page.locator('#DocumentNote').inputValue(), 'B6 note from an older tab');
        await page.evaluate(() => saveStockCountHeader(false));
        const moved = await api('GET', `/admin/api/stock-counts/${info.moved.id}`);
        assert.equal(moved.body.warehouseId, info.moved.destination);
        assert.equal(moved.body.note, 'B6 note from an older tab');
        assert.equal(moved.body.lines[0].systemQtyBase, 0);
        observations.push('An older tab cannot silently move a document back while saving its note after another tab has changed warehouse and added a line.');

        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(info.output, 'observations.json'), JSON.stringify({ observations, errors }, null, 2));
        await context.close();
        console.log('B6 Chrome: 5 scenarios passed; no JavaScript errors.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error.stack || error.message); process.exit(1); });
