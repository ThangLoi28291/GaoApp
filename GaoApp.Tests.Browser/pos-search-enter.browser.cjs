// Exercises the real POS autocomplete/render/keyboard code with controlled HTTP responses.
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');
const scripts = path.resolve(__dirname, '../GaoApp.Web/wwwroot/Admin/js/pos');

(async () => {
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const page = await browser.newPage();
        page.setDefaultTimeout(5000);
        await page.setContent('<div><input id="txtBarcode"><div id="barcodeAutocomplete" style="display:none"></div></div><button id="btnScan">Quét</button>');
        for (const name of ['pos.state.js', 'pos.common.js', 'pos.render.js', 'pos.barcode.js', 'pos.keyboard.js'])
            await page.addScriptTag({ path: path.join(scripts, name) });
        await page.evaluate(() => {
            window.searchCalls = []; window.commands = []; window.errors = [];
            window.searchDelay = 0;
            window.results = [{ variantId: 11, productName: 'Sữa tươi', barcode: '8931234567890', price: 6000,
                isActive: true, productUnitConversionId: 101, unitOptions: [
                    { productUnitConversionId: 101, unitId: 1, unitName: 'hộp', factor: 1, price: 6000, barcode: '8931234567890' },
                    { productUnitConversionId: 102, unitId: 2, unitName: 'lốc', factor: 4, price: 20000, barcode: '8931234567891' }
                ] }, { variantId: 12, productName: 'Sữa chua', barcode: '8939876543210', price: 7000, isActive: true }];
            window.state = PosState.create();
            window.searchState = PosState.createBarcodeSearchState();
            state.business.currentOrderId = 7;
            const input = document.querySelector('#txtBarcode');
            const elements = { txtBarcode: input, barcodeAutocomplete: document.querySelector('#barcodeAutocomplete'), btnScan: document.querySelector('#btnScan') };
            window.barcode = PosBarcode.create({ posState: state, barcodeSearchState: searchState, elements,
                helpers: {
                    formatMoney: value => String(value), escapeHtml: value => String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;'),
                    fetchJson: async (url, options) => {
                        searchCalls.push(url); const response = structuredClone(results);
                        if (searchDelay) await new Promise(resolve => setTimeout(resolve, searchDelay));
                        if (options.signal.aborted) throw new DOMException('Aborted', 'AbortError');
                        return response;
                    },
                    postJson: async (url, body) => { commands.push({ url, body }); return { orderId: 7, lines: [] }; },
                    runPosAction: async (state, key, handler, options) => {
                        try { const draft = await handler(); options.onSuccess(draft); return { ok: true }; }
                        catch (error) { errors.push(error.message); return { ok: false, error }; }
                    },
                    focusBarcodeInput: () => input.focus(), syncDraftToUi: () => {}, showSuccess: () => {},
                    showError: message => errors.push(message),
                    applyDraftActionSuccess: options => options.afterSync?.(), applyScreenActionSuccess: () => {}
                } });
            barcode.bindEvents();
            PosKeyboard.create({ posState: state, elements }).bind();
        });
        const input = page.locator('#txtBarcode');
        async function search(text) {
            await input.fill(text);
            await page.locator('#barcodeAutocomplete [data-autocomplete-index="0"].active').waitFor();
        }
        async function selected(action, expected) {
            const before = await page.evaluate(() => commands.length);
            await action();
            await page.waitForFunction(count => commands.length > count, before);
            const actual = await page.evaluate(() => commands.at(-1));
            assert.equal(actual.url, expected, JSON.stringify(actual));
            assert.equal(await page.evaluate(() => commands.length), before + 1);
        }
        await search('sua');
        await selected(() => input.press('Enter'), '/admin/pos/7/items?variantId=11&qty=1');
        console.log('PASS first result by name');
        await search('sua'); // cached response
        await selected(() => input.press('Enter'), '/admin/pos/7/items?variantId=11&qty=1');
        console.log('PASS first result from cache');
        await search('sua');
        await input.press('ArrowDown');
        await selected(() => input.press('Enter'), '/admin/pos/7/items?variantId=12&qty=1');
        console.log('PASS cached result and arrow navigation');
        await search('893');
        await selected(() => input.press('Enter'), '/admin/pos/7/items?variantId=11&qty=1');
        console.log('PASS first result from partial numeric search');

        await search('4+sua');
        await selected(() => input.press('Enter'), '/admin/pos/7/items?variantId=11&qty=4');
        console.log('PASS quantity prefix with first result');

        await search('sua');
        await input.press('ArrowRight');
        await input.press('ArrowDown');
        await selected(() => input.press('Enter'), '/admin/pos/7/items?variantId=11&productUnitConversionId=102&qty=1');
        console.log('PASS selecting an expanded pack with arrows');

        await search('sua');
        await input.press('ArrowRight');
        await input.press('ArrowDown');
        await input.press('ArrowLeft');
        await selected(() => input.press('Enter'), '/admin/pos/7/items?variantId=11&qty=1');
        console.log('PASS returning from a pack to its parent');

        await search('sua');
        // Reproduce a highlighted first row with an unset legacy keyboard cursor.
        await page.evaluate(() => { searchState.activeIndex = -1; });
        await selected(() => input.press('Enter'), '/admin/pos/7/items?variantId=11&qty=1');
        console.log('PASS visual first selection when keyboard cursor is unset');

        await search('8931234567891');
        await selected(() => input.press('Enter'), '/admin/pos/cart/current/scan');
        assert.equal(await page.evaluate(() => commands.at(-1).body.barcode), '8931234567891');
        console.log('PASS full pack barcode retains exact scan resolution');

        await input.fill('8931234567890');
        await selected(() => input.press('Enter'), '/admin/pos/cart/current/scan');
        assert.equal(await page.evaluate(() => commands.at(-1).body.barcode), '8931234567890');
        console.log('PASS immediate scanner Enter without suggestions');

        await page.evaluate(() => { searchDelay = 500; });
        await input.fill('sua tuoi');
        await selected(async () => { await input.press('Enter'); await input.press('Enter'); }, '/admin/pos/7/items?variantId=11&qty=1');
        console.log('PASS Enter before debounce and repeated Enter adds once');

        await input.fill('sua chua');
        await page.waitForFunction(() => searchState.isLoading);
        await selected(() => input.press('Enter'), '/admin/pos/7/items?variantId=11&qty=1');
        console.log('PASS Enter while name results are loading');

        let before = await page.evaluate(() => commands.length);
        await input.fill('tim cu');
        await input.press('Enter');
        await input.fill('tim moi');
        await page.locator('#barcodeAutocomplete [data-autocomplete-index="0"].active').waitFor();
        assert.equal(await page.evaluate(() => commands.length), before);
        console.log('PASS typing a new query cancels pending selection');

        await input.fill('huy tim');
        await input.press('Enter');
        await input.press('Escape');
        await page.waitForTimeout(650);
        assert.equal(await page.evaluate(() => commands.length), before);
        assert.equal(await page.locator('#barcodeAutocomplete').isVisible(), false);
        console.log('PASS Escape cancels pending selection and stale replies');

        await input.fill('dong tim');
        await input.press('Enter');
        await page.evaluate(() => document.body.click());
        await page.waitForTimeout(650);
        assert.equal(await page.evaluate(() => commands.length), before);
        console.log('PASS dismissing the dropdown cancels pending selection');

        await page.evaluate(() => { searchDelay = 0; });
        await search('sua');
        await input.dispatchEvent('keydown', { key: 'Enter', isComposing: true });
        assert.equal(await page.evaluate(() => commands.length), before);
        await selected(() => page.locator('#barcodeAutocomplete [data-autocomplete-index="1"] .pos-ac-title').click(), '/admin/pos/7/items?variantId=12&qty=1');
        console.log('PASS IME composition and mouse selection');
        assert.deepEqual(await page.evaluate(() => errors), []);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
