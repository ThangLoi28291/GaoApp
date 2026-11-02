// Real autocomplete, action helpers and scan dialog; product HTTP responses are fixtures.
// This probe does not connect to a store database or create a sale.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

(async () => {
    const view = read('GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml');
    const start = view.indexOf('<dialog id="posScanGuard"');
    const guard = view.slice(start, view.indexOf('</dialog>', start) + 9);
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const page = await browser.newPage({ viewport: { width: 1366, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    page.setDefaultTimeout(10000);
    try {
        await page.setContent('<html lang="vi"><body><main style="max-width:960px;margin:32px auto">' +
            '<div><input id="txtBarcode" aria-label="Tìm sản phẩm" style="width:100%;padding:16px;font-size:22px"></div>' +
            '<button id="btnScan">Quét mã</button><div id="barcodeAutocomplete" style="display:none"></div>' +
            '</main>' + guard + '</body></html>');
        for (const file of ['vendor/css/core.css', 'Admin/css/pos/pos.css', 'Admin/css/pos/pos.scan-feedback.css']) {
            await page.addStyleTag({ content: read('GaoApp.Web/wwwroot/' + file) });
        }
        for (const file of ['pos.common.js', 'pos.render.js', 'pos.scan-guard.js', 'pos.barcode.js']) {
            await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/Admin/js/pos/' + file) });
        }
        await page.addScriptTag({ content: `
            window.requests = []; window.notices = []; window.alert = message => notices.push(message);
            window.searchDelay = 0;
            const products = [
                {variantId:11,productName:'Bàn chải micro siêu mềm',displayName:'Bàn chải micro siêu mềm',barcode:'4902430679640',price:12000,availableQty:15,isActive:true,unitOptions:[{productUnitConversionId:61,unitName:'Lốc',factor:6,barcode:'4902430679641',price:60000,availableQty:2}]},
                {variantId:12,productName:'Bàn chải OralB trà đen',displayName:'Bàn chải OralB trà đen',barcode:'4902430678193',price:31000,availableQty:15,isActive:true,unitOptions:[]}
            ];
            window.state = {business:{currentOrderId:1,currentDraft:{orderId:1,lines:[]}},network:{isOnline:true,busyScopes:new Set(),pendingActions:new Set()}};
            window.searchState = {};
            const input = document.getElementById('txtBarcode');
            window.barcode = PosBarcode.create({posState:state,barcodeSearchState:searchState,
                elements:{txtBarcode:input,btnScan:document.getElementById('btnScan'),barcodeAutocomplete:document.getElementById('barcodeAutocomplete')},
                helpers:{...PosCommon,
                    fetchJson:async url=>{const keyword=new URL(url,'http://test').searchParams.get('keyword'); if(searchDelay)await new Promise(r=>setTimeout(r,searchDelay)); return keyword==='888'?[]:products;},
                    postJson:async(url,body)=>{requests.push({url,body}); if(url.endsWith('/scan')&&body.barcode==='9999999999999')throw {status:400,message:'Không tìm thấy sản phẩm theo barcode.'};return {orderId:1,lines:[]};},
                    focusBarcodeInput:()=>input.focus(),syncDraftToUi:()=>{},showSuccess:()=>{},showError:message=>notices.push(message)
                }});
            barcode.bindEvents();
        ` });
        const input = page.locator('#txtBarcode');
        const rows = page.locator('#barcodeAutocomplete [data-autocomplete-kind]');
        const requests = () => page.evaluate(() => window.requests);
        const waitRequest = n => page.waitForFunction(n => requests.length === n && !searchState.isSubmitting, n);
        const search = async keyword => {
            await input.fill(keyword);
            await rows.first().waitFor({ state: 'visible' });
        };

        await search('430');
        assert.equal(await rows.first().getAttribute('class').then(x => x.includes('active')), true);
        await input.press('Enter'); await waitRequest(1);
        assert.equal((await requests())[0].url, '/admin/pos/1/items?variantId=11&qty=1');
        assert.equal(await page.locator('#posScanGuard').isVisible(), false);
        assert.equal(await input.inputValue(), '');

        await search('430'); await input.press('ArrowDown'); await input.press('Enter'); await waitRequest(2);
        assert.equal((await requests())[1].url, '/admin/pos/1/items?variantId=12&qty=1');

        await search('4+430'); await input.press('Enter'); await waitRequest(3);
        assert.equal((await requests())[2].url, '/admin/pos/1/items?variantId=11&qty=4');

        await search('430'); await input.press('ArrowRight'); await input.press('ArrowDown');
        await input.press('Enter'); await waitRequest(4);
        assert.equal((await requests())[3].url, '/admin/pos/1/items?variantId=11&productUnitConversionId=61&qty=1');

        await search('Bàn chải'); await input.press('Enter'); await waitRequest(5);
        assert.equal((await requests())[4].url, '/admin/pos/1/items?variantId=11&qty=1');

        await page.evaluate(() => searchDelay = 450);
        await input.fill('431'); await page.locator('.pos-autocomplete-loading').waitFor();
        await input.press('Enter'); await input.press('Enter'); await waitRequest(6);
        assert.equal((await requests())[5].url, '/admin/pos/1/items?variantId=11&qty=1');
        await page.evaluate(() => searchDelay = 0);

        await input.fill('888'); await page.locator('.pos-autocomplete-empty').waitFor();
        await input.press('Enter'); await input.press('Enter');
        assert.equal((await requests()).length, 6);
        assert.equal(await input.inputValue(), '888');
        assert.equal(await page.locator('#posScanGuard').isVisible(), false);
        assert.equal(await page.locator('.pos-autocomplete-empty').isVisible(), true);

        // A keyboard wedge scanner sends characters and Enter before the 300 ms search debounce.
        await input.fill(''); await input.pressSequentially('4902430679641'); await input.press('Enter'); await waitRequest(7);
        assert.equal((await requests())[6].url, '/admin/pos/cart/current/scan');
        assert.equal((await requests())[6].body.barcode, '4902430679641');

        await search('430');
        await input.fill(''); await input.pressSequentially('9999999999999'); await input.press('Enter');
        await page.locator('#posScanGuard[open]').waitFor();
        assert.equal((await requests()).length, 8);
        assert.equal((await requests())[7].url, '/admin/pos/cart/current/scan');
        assert.equal(await page.locator('#posScanGuardCode').innerText(), '9999999999999');
        await page.keyboard.type('4902430679640'); await page.keyboard.press('Enter');
        assert.equal(await page.locator('#posScanGuard').isVisible(), true);
        assert.equal((await requests()).length, 8);
        await page.locator('#posScanGuardConfirm').click();
        assert.equal(await input.evaluate(el => document.activeElement === el), true);

        assert.deepEqual(errors, []);
        console.log('PASS: numeric partial search, highlighted row, quantity prefix, pack unit, name search, pending search, empty search, exact scanner barcode and missing-code guard.');
    } catch (error) {
        const output = path.join(root, 'TestResults', 'pos-barcode-search'); fs.mkdirSync(output, { recursive: true });
        await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {});
        console.error(error); console.error('BROWSER ERRORS', errors); process.exitCode = 1;
    } finally {
        await browser.close();
    }
})();
