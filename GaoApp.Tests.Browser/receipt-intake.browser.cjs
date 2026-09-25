'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {chromium} = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => {let data='';process.stdin.on('data',x=>data+=x);process.stdin.on('end',()=>resolve(data));}));
    const output=path.resolve('TestResults/receipt-intake/browser');fs.mkdirSync(output,{recursive:true});
    const browser=await chromium.launch({channel:'chrome',headless:true});
    const employeeContext=await browser.newContext({viewport:{width:1440,height:1000}});
    const page=await employeeContext.newPage(),errors=[];page.on('pageerror',error=>errors.push(error.message));
    const base=`/admin/api/stock-documents/${info.receiptId}`;
    async function login(target,credentials) {
        await target.goto(info.baseUrl+'/admin/account/login');
        await target.locator('[name=UserName]').fill(credentials.user);
        await target.locator('[name=Password]').fill(credentials.password);
        const terminal=target.locator('[name=SelectedTerminalId]');if(await terminal.count())await terminal.selectOption(String(info.terminalId));
        await Promise.all([target.waitForURL(url=>!url.pathname.endsWith('/login')),target.locator('button[type=submit]').click()]);
    }
    async function get(target,url) {return target.evaluate(async url=>{const response=await fetch(url);if(!response.ok)throw new Error(await response.text());return response.json();},url);}
    async function scan(code) {
        await page.evaluate(()=>window.jQuery('#quickLookupInput').select2('open'));
        const field=page.locator('.select2-container--open .select2-search__field');
        await field.fill('');await field.pressSequentially(code);await field.press('Enter');
        await page.locator('#receiptIntakeModal.show').waitFor();
    }
    async function product(target) {
        await target.locator('#riExistingTab').click();
        await target.locator('#riProduct + .select2 .select2-selection').click();
        const field=target.locator('#receiptIntakeModal .select2-search__field');await field.fill('Sữa');
        await target.locator('#receiptIntakeModal .select2-results__option--selectable').first().click();
        await target.locator('#riProductCard:not([hidden])').waitFor();
    }
    async function save(target) {
        await target.locator('#riSave').click();
        await target.locator('#receiptIntakeModal').waitFor({state:'hidden'});
        await target.waitForFunction(()=>!window.ReceiptIntake.isSaving());
    }
    async function autocomplete(target,id,term) {
        if (!await target.locator('.select2-container--open .select2-search__field').isVisible())
            await target.locator(`#${id} + .select2 .select2-selection`).click();
        await target.locator('.select2-container--open .select2-search__field').fill(term);
        await target.locator('.select2-container--open .select2-results__option--selectable').first().click();
    }
    async function quantityControls(target,container) {
        await target.waitForFunction(()=>window.ReceiptQuantityControls && !window.ReceiptIntake.isSaving());
        const fields=target.locator(`${container} .js-receiving-qty,${container} .js-inline-line-qty,${container} [data-intake-quantity]`);
        const count=await fields.count();
        for(const index of [0,count-2,count-1]) {
            const field=fields.nth(index), original=Number(await field.inputValue());
            assert.equal(await field.getAttribute('step'),'1');
            await field.focus();await field.press('ArrowUp');assert.equal(Number(await field.inputValue()),original+1);
            await field.press('ArrowDown');assert.equal(Number(await field.inputValue()),original);
            await field.locator('xpath=ancestor::tr').locator('td').nth(1).click();
            await target.keyboard.press('+');
            await target.waitForFunction(()=>!window.ReceiptQuantityControls.isBusy());
            assert.equal(Number(await field.inputValue()),original+1);
            await target.keyboard.press('+');await target.keyboard.press('+');
            await target.waitForFunction(()=>!window.ReceiptQuantityControls.isBusy());
            assert.equal(Number(await field.inputValue()),original+3,`Rapid presses must not lose a quantity increment (${container}, index ${index}).`);
            await target.keyboard.press('-');await target.waitForFunction(()=>!window.ReceiptQuantityControls.isBusy());
            await target.getByRole('button',{name:'Giảm 1 đơn vị',exact:true}).click();
            await target.waitForFunction(()=>!window.ReceiptQuantityControls.isBusy());
            await target.getByRole('button',{name:'Giảm 1 đơn vị',exact:true}).click();
            await target.waitForFunction(()=>!window.ReceiptQuantityControls.isBusy());
            assert.equal(Number(await field.inputValue()),original);
            assert.equal(await target.locator(`${container} tr.rq-selected`).count(),1);
        }
        const last=fields.last(), beforeSearch=await last.inputValue();
        await target.evaluate(()=>window.jQuery('#quickLookupInput').select2('open'));
        const search=target.locator('.select2-container--open .select2-search__field');
        await search.fill('Sữa');await search.press('+');await search.press('-');
        assert.equal(await search.inputValue(),'Sữa+-');
        assert.equal(await last.inputValue(),beforeSearch,'Typing in search must not change the selected quantity.');
        await target.evaluate(()=>window.jQuery('#quickLookupInput').select2('close'));
        await last.locator('xpath=ancestor::tr').locator('td').nth(1).click();
        await target.getByRole('button',{name:'Tăng 1 đơn vị',exact:true}).click();
        await target.waitForFunction(()=>!window.ReceiptQuantityControls.isBusy());
        await target.keyboard.press('-');await target.waitForFunction(()=>!window.ReceiptQuantityControls.isBusy());
        assert.equal(await target.locator('.rq-message').innerText(),'');
        await target.locator(container).locator('xpath=..').screenshot({path:path.join(output,container.includes('wrd')?'quantity-step-warehouse.png':'quantity-step-management.png')});
    }
    try {
        await login(page,info.employee);
        await page.goto(info.baseUrl+`/admin/warehouse-receiving/${info.receiptId}`);
        await page.waitForFunction(()=>window.ReceiptIntake && document.querySelector('#riBaseUnit').options.length>1);
        assert.equal(await page.locator('#btnReceivingQuickCreateProduct').count(),0);
        assert.equal(await page.locator('#receiptIntakePending').isVisible(),false);
        await page.screenshot({path:path.join(output,'compact-receiving.png')});
        await scan('NEW-PACK-0001');
        await product(page);await page.locator('[data-ri-unit="new"]').click();
        await autocomplete(page,'riReceiveUnit','Loc');
        assert.equal(await page.locator(`[data-ri-unit="${info.packId}"]`).getAttribute('aria-pressed'),'true');
        await page.locator('#riQuantity').fill('1');await save(page);
        const known=await get(page,base+'/barcode-proposals');assert.equal(known.items.length,1);
        await scan('NEW-CASE-0001');await product(page);await page.locator('[data-ri-unit="new"]').click();
        await autocomplete(page,'riReceiveUnit','Kiện');
        await page.locator('#riFactor').fill('48');await page.locator('#riQuantity').fill('2');
        assert.match(await page.locator('#riEquivalent').innerText(),/96/);
        await page.screenshot({path:path.join(output,'new-packing-desktop.png')});
        await page.setViewportSize({width:390,height:844});
        await page.screenshot({path:path.join(output,'new-packing-mobile.png')});
        assert.equal(await page.locator('#receiptIntakeModal .modal-body').evaluate(el=>el.scrollWidth<=el.clientWidth+1),true);
        assert.equal(await page.locator('#riApproveWrap').isVisible(),false);
        await save(page);await page.setViewportSize({width:1440,height:1000});
        const received=page.locator('#wrdLinesContainer .ri-received-row');
        await received.waitFor();
        assert.match(await received.innerText(),/Kiện/);
        assert.equal(await received.locator('input').inputValue(),'2');
        await received.locator('input').fill('3');await received.locator('input').press('Tab');
        await page.waitForFunction(()=>!window.ReceiptIntake.isSaving());
        assert.equal(await received.locator('input').inputValue(),'3');
        assert.equal(await page.locator('#wrdTotalLines').innerText(),'3');
        await page.screenshot({path:path.join(output,'new-unit-received-immediately.png')});
        await scan('NEW-CASE-0001');
        await page.waitForFunction(()=>document.querySelector('#riFactor').value==='48');
        assert.equal(await page.locator('#riReceiveUnit option:checked').innerText(),'Kiện');
        await page.locator('#receiptIntakeModal [data-bs-dismiss=modal]').first().click();
        await page.locator('#receiptIntakeModal').waitFor({state:'hidden'});
        await page.keyboard.press('F4');await page.locator('#receiptIntakeModal.show').waitFor();
        assert.equal(await page.locator('#riNewTab').getAttribute('aria-selected'),'true');
        await page.locator('#riBarcode').fill('NEW-PRODUCT-0001');await page.locator('#riNewName').fill('Hàng mới kiểm thử popup');
        await autocomplete(page,'riBaseUnit','Hop');
        await autocomplete(page,'riReceiveUnit','Túi');
        await page.locator('#riFactor').fill('10');await page.locator('#riQuantity').fill('2');
        const category=await page.locator('#riCategory option').evaluateAll(items=>items.find(x=>/^\d+$/.test(x.value)).textContent);
        await autocomplete(page,'riCategory',category);
        await page.locator('#riExistingTab').click();await page.locator('#riNewTab').click();
        assert.equal(await page.locator('#riNewName').inputValue(),'Hàng mới kiểm thử popup');
        assert.equal(await page.locator('#riBarcode').inputValue(),'NEW-PRODUCT-0001');
        await page.screenshot({path:path.join(output,'new-product-tab.png')});await save(page);
        let state=(await get(page,base+'/intake')).state;assert.equal(state.unresolvedCount,2);
        assert.equal(await page.locator('#wrdLinesContainer .ri-received-row').count(),2);
        await quantityControls(page,'#wrdLinesContainer');
        const managerContext=await browser.newContext({viewport:{width:1440,height:1000}});
        const manager=await managerContext.newPage();manager.on('pageerror',error=>errors.push(error.message));
        await login(manager,info);await manager.goto(info.baseUrl+`/admin/stock-documents/${info.receiptId}`);
        assert.equal(await manager.locator('#stockDocumentLinesContainer .ri-received-row').count(),2);
        assert.equal(await manager.locator('#stockDocumentLinesContainer .ri-received-quantity[readonly]').count(),0);
        await quantityControls(manager,'#stockDocumentLinesContainer');
        await page.reload();await page.waitForFunction(()=>window.ReceiptIntake && document.querySelector('#riBaseUnit').options.length>1);
        await page.locator('#btnSubmitReceiving').click();await page.locator('#submitReceivingModal.show').waitFor();
        await page.locator('#btnConfirmSubmitReceiving').click();await page.waitForURL(url=>url.pathname==='/admin/warehouse-receiving');
        await manager.goto(info.baseUrl+`/admin/stock-documents/${info.receiptId}`);
        await manager.locator('#receiptIntakePending:not([hidden])').waitFor();await manager.locator('#receiptIntakePending > summary').click();
        await manager.locator('#riPendingItems .ri-pending-item').nth(1).waitFor();
        assert.equal(await manager.locator('#riPendingItems .ri-pending-item').count(),2);
        assert.equal(await manager.locator('.rq-toolbar:visible').count(),0);
        await manager.screenshot({path:path.join(output,'manager-pending-review.png'),fullPage:true});
        await manager.locator('.commercial-unit-price').first().fill('11');
        for(let n=0;n<2;n++) {
            await manager.locator('[data-ri-review]').first().click();
            await manager.locator('#receiptIntakeReviewModal.show').waitFor();
            await manager.locator('#riApprove').click();await manager.locator('#receiptIntakeReviewModal').waitFor({state:'hidden'});
            await manager.waitForFunction(()=>!window.ReceiptIntake.isSaving());
        }
        state=(await get(manager,base+'/intake')).state;assert.equal(state.unresolvedCount,0);
        assert.equal(await manager.locator('#commercialApprovalWorkbench').getAttribute('data-has-unresolved-provisional'),'false');
        assert.equal(await manager.locator('.commercial-unit-price').first().inputValue(),'11');
        assert.equal(await manager.locator('.commercial-unit-price').count(),4);
        assert.equal(await manager.locator('#stockDocumentLinesContainer .ri-received-row').count(),0);
        await manager.locator('#rbpItems [data-review=approve]').click();
        await manager.waitForFunction(()=>document.querySelector('#receiptIntakePending').hidden);
        const lookup=await get(manager,base+'/barcode-proposals/lookup?term=NEW-CASE-0001');
        assert.equal(lookup.results[0].factor,48);assert.equal(lookup.results[0].unitName,'Kiện');
        assert.deepEqual(errors,[]);console.log('PASS: both receiving views use step 1, selected-row +/- and buttons, repeated presses persist, search typing is isolated, quantities remain consistent through receipt submission and manager approval.');
    } catch(error) {await page.screenshot({path:path.join(output,'failure.png'),fullPage:true}).catch(()=>{});throw error;}
    finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
