'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
const mobile=require('./inventory-lookup-mobile-review.cjs');

(async()=>{
    const info=JSON.parse(await new Promise(resolve=>{let input='';process.stdin.on('data',chunk=>input+=chunk);process.stdin.on('end',()=>resolve(input));}));
    const browser=await chromium.launch({channel:'chrome',headless:true});
    const errors=[],observations=[];let page;
    fs.mkdirSync(info.output,{recursive:true});
    try{
        const login=async account=>{
            const context=await browser.newContext({viewport:{width:390,height:844},serviceWorkers:'block'});
            await mobile.protect(context,info.baseUrl);
            const current=await context.newPage();
            current.on('pageerror',error=>errors.push(error.message));
            current.on('dialog',async dialog=>{errors.push('Unexpected native dialog: '+dialog.message());await dialog.dismiss();});
            await current.goto(info.baseUrl+'/admin/account/login');
            await current.locator('[name=UserName]').fill(account.user);await current.locator('[name=Password]').fill(account.password);
            if(await current.locator('[name=SelectedTerminalId]').count())await current.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
            await Promise.all([current.waitForURL(url=>!url.pathname.endsWith('/login')),current.locator('button[type=submit]').click()]);
            return{context,page:current};
        };
        const manager=await login(info.manager);page=manager.page;
        const dataRequest=(pathname,predicate=()=>true)=>page.waitForResponse(response=>{
            const url=new URL(response.url());return response.request().method()==='GET'&&url.pathname===pathname&&predicate(url);
        });
        const ready=async kind=>{await page.locator('#'+kind+'ResultsPanel[aria-busy=false]').waitFor();await mobile.settle(page);};
        const gotoData=async(kind,route)=>{
            const received=dataRequest('/admin/'+route+'/data');await page.goto(info.baseUrl+'/admin/'+route);
            const response=await received;assert.equal(response.status(),200);const result=await response.json();await ready(kind);return result;
        };
        const close=async selector=>{await page.locator(selector+' .btn-close').click();await page.locator(selector).waitFor({state:'hidden'});await mobile.settle(page);};
        const searchInquiry=async keyword=>{
            const response=dataRequest('/admin/inventory-inquiry/data',url=>url.searchParams.get('keyword')===keyword);
            await page.locator('#inventoryKeyword').fill('');await page.locator('#inventoryKeyword').pressSequentially(keyword,{delay:5});
            const result=await response;assert.equal(result.status(),200);const payload=await result.json();await ready('inventory');return payload;
        };
        const inquiryQuick=async(label,canViewCost)=>{
            const response=dataRequest('/admin/inventory-inquiry/quick-view',url=>url.searchParams.get('warehouseId')===String(info.sourceWarehouseId)&&url.searchParams.get('productVariantId')===String(info.variantId));
            await page.locator(`#inventoryMobileList [data-warehouse-id="${info.sourceWarehouseId}"][data-product-variant-id="${info.variantId}"] .js-inventory-quick-view`).click();
            const result=await response;assert.equal(result.status(),200);const payload=await result.json();
            assert.equal(payload.onHandQty,105);assert.equal(payload.reservedQty,12);assert.equal(payload.availableQty,93);
            assert.equal(Object.hasOwn(payload,'cost'),canViewCost);
            await page.locator('#inventoryQuickBody .inventory-unit-table').waitFor();
            assert.equal(await page.locator('#inventoryQuickBody .inventory-cost-details').count(),canViewCost?1:0);
            const units=await page.locator('#inventoryQuickBody .inventory-unit-table tbody tr').allTextContents();
            assert.ok(units.some(text=>text.includes(info.barcode)&&/4\s+Hộp/.test(text)));
            assert.ok(units.some(text=>text.includes(info.cartonBarcode)&&/24\s+Hộp/.test(text)));
            await mobile.dialog(page,'#inventoryQuickViewModal',label,info.output);
            await close('#inventoryQuickViewModal');
        };
        let result=await gotoData('inventory','inventory-inquiry');
        assert.equal(result.canViewCost,true);assert.equal(result.totalItems,1);
        assert.match(await page.locator('#inventoryMobileList').innerText(),/105[\s\S]*12[\s\S]*93/);
        await mobile.review(page,'inquiry-admin-stock',info.output,{root:'[data-inventory-inquiry-index]'});
        for(const keyword of[info.barcode,info.cartonBarcode,info.sku,'sua tuoi khong duong']){
            result=await searchInquiry(keyword);assert.equal(result.items.length,1);assert.equal(result.items[0].productVariantId,info.variantId);
        }
        await mobile.review(page,'inquiry-native-name-search',info.output,{root:'[data-inventory-inquiry-index]'});
        await page.locator('#inventoryOpenMobileFilters').click();
        await page.locator('#inventoryMobileWarehouse').selectOption(String(info.sourceWarehouseId));
        await page.locator('#inventoryMobileState').selectOption('positive');
        await page.locator('#inventoryMobileSortBy').selectOption('availableqty');
        await mobile.dialog(page,'#inventoryMobileFilterSheet','inquiry-native-filters',info.output);
        const filtered=dataRequest('/admin/inventory-inquiry/data',url=>url.searchParams.get('warehouseId')===String(info.sourceWarehouseId)&&url.searchParams.get('state')==='positive');
        await page.locator('#inventoryMobileApplyFilters').click();assert.equal((await filtered).status(),200);
        await page.locator('#inventoryMobileFilterSheet').waitFor({state:'hidden'});await ready('inventory');
        assert.equal(await page.locator('#inventoryWarehouseFilter').inputValue(),String(info.sourceWarehouseId));
        await mobile.review(page,'inquiry-scoped-positive',info.output,{root:'[data-inventory-inquiry-index]'});
        await inquiryQuick('inquiry-admin-cost-and-unit-sheet',true);
        result=await searchInquiry('UI13-no-such-product');assert.equal(result.items.length,0);
        assert.match(await page.locator('#inventoryMobileList').innerText(),/Không/);
        await mobile.review(page,'inquiry-native-empty',info.output,{root:'[data-inventory-inquiry-index]'});
        observations.push('Native inquiry numeric barcodes, SKU and accent-insensitive name search; scoped positive filter; factor4/factor24 original units; physical105/reserved12/available93; admin cost and empty state.');

        result=await gotoData('ledger','inventory-ledger');assert.equal(result.canViewCost,true);assert.equal(result.totalItems,3);
        const receipt=result.items.find(item=>item.transactionId===info.transactionId);
        assert.ok(receipt);assert.deepEqual([receipt.beforeQty,receipt.quantityChange,receipt.afterQty],[100,8,108]);
        assert.equal(receipt.receiptDocumentNo,'NK-UI13-SOURCE');assert.ok(receipt.inboundCost);
        await mobile.review(page,'ledger-admin-movements',info.output,{root:'[data-inventory-ledger-index]'});
        await page.locator('#ledgerSearchScope').selectOption('reference');
        await ready('ledger');
        const sourceSearch=dataRequest('/admin/inventory-ledger/data',url=>url.searchParams.get('keyword')==='NK-UI13-SOURCE'&&url.searchParams.get('searchScope')==='reference');
        await page.locator('#ledgerKeyword').fill('NK-UI13-SOURCE');
        const sourceResponse=await sourceSearch;assert.equal(sourceResponse.status(),200);result=await sourceResponse.json();assert.equal(result.items.length,1);await ready('ledger');
        await page.locator('#ledgerOpenMobileFilters').click();
        await page.locator('#ledgerMobileWarehouse').selectOption(String(info.sourceWarehouseId));
        await page.locator('#ledgerMobileFromDate').fill(info.fromDate);await page.locator('#ledgerMobileToDate').fill(info.toDate);
        await page.locator('#ledgerMobileReferenceCode').fill('NK-UI13-SOURCE');
        await mobile.dialog(page,'#ledgerMobileFilterSheet','ledger-native-dates-source-filters',info.output);
        const ledgerFiltered=dataRequest('/admin/inventory-ledger/data',url=>url.searchParams.get('fromDate')===info.fromDate&&url.searchParams.get('toDate')===info.toDate&&url.searchParams.get('referenceCode')==='NK-UI13-SOURCE');
        await page.locator('#ledgerMobileApplyFilters').click();assert.equal((await ledgerFiltered).status(),200);
        await page.locator('#ledgerMobileFilterSheet').waitFor({state:'hidden'});await ready('ledger');
        await mobile.review(page,'ledger-native-reference-date-scope',info.output,{root:'[data-inventory-ledger-index]'});
        const ledgerQuick=dataRequest('/admin/inventory-ledger/quick-view',url=>url.searchParams.get('transactionId')===String(info.transactionId));
        await page.locator(`#ledgerMobileList .js-ledger-quick[data-transaction-id="${info.transactionId}"]`).click();
        assert.equal((await ledgerQuick).status(),200);await page.locator('#ledgerQuickBody .ledger-quick-metrics').waitFor();
        assert.match(await page.locator('#ledgerQuickBody').innerText(),/100[\s\S]*\+8[\s\S]*108/);
        assert.match(await page.locator('#ledgerQuickBody').innerText(),/NK-UI13-SOURCE/);
        assert.equal(await page.locator('#ledgerQuickBody .ledger-inbound-cost').count(),1);
        await mobile.dialog(page,'#ledgerQuickViewModal','ledger-admin-source-cost-sheet',info.output);await close('#ledgerQuickViewModal');
        observations.push('Native ledger exact receipt100→+8→108, decrease final105, source identity, dates and reference search preserved; original source facts remain original text.');

        const warehouseReady=async()=>{await page.locator(`#warehouseMobileList [data-csp-action=warehouse-edit][data-csp-id="${info.warehouseId}"]`).waitFor();await mobile.settle(page);};
        await page.goto(info.baseUrl+'/Admin/WarehouseManagement/Index');await warehouseReady();
        await mobile.review(page,'warehouse-native-list',info.output,{root:'.warehouse-page'});
        await page.locator('#warehouseSearch').fill('Kho kiểm thử UI-13');
        assert.equal(await page.locator('#warehouseMobileList .warehouse-mobile-card').count(),1);
        await page.locator('.warehouse-phone-filters > summary').click();
        await page.locator('#warehouseLegalEntityFilter').selectOption(String(info.legalEntityId));
        await page.locator('#warehouseLifecycleFilter').selectOption('active');
        await page.locator('#warehousePageSize').selectOption('20');
        await mobile.review(page,'warehouse-native-filtered',info.output,{root:'.warehouse-page'});
        await page.locator('#btnCreateWarehouse').click();await page.waitForTimeout(400);
        assert.equal(await page.locator('#warehouseCode').isEditable(),false);
        await mobile.dialog(page,'#warehouseModal','warehouse-create-cancel',info.output);await close('#warehouseModal');
        await page.locator(`#warehouseMobileList [data-csp-action=warehouse-edit][data-csp-id="${info.warehouseId}"]`).click();await page.waitForTimeout(400);
        assert.equal(await page.locator('#warehouseId').inputValue(),String(info.warehouseId));
        assert.equal(await page.locator('#warehouseLegalEntityId').inputValue(),String(info.legalEntityId));
        assert.equal(await page.locator('#warehouseLegalEntityId').isDisabled(),true);
        assert.equal(await page.locator('#warehouseAllowNegativeInventory').isChecked(),false);
        await mobile.dialog(page,'#warehouseModal','warehouse-native-edit-note',info.output);
        await page.locator('#warehouseNote').fill('UI13 native warehouse note');
        const warehouseSaved=page.waitForResponse(response=>response.request().method()==='PUT'&&new URL(response.url()).pathname==='/admin/api/warehouses');
        await page.locator('#btnSaveWarehouse').click();assert.equal((await warehouseSaved).status(),200);
        await page.locator('#warehouseModal').waitFor({state:'hidden'});await warehouseReady();
        assert.match(await page.locator('#warehouseMobileList').innerText(),/UI13 native warehouse note/);
        observations.push('Native warehouse keyword/HKD/lifecycle/page-size filters and create cancel; original form edit changes isolated note only, read-only code/owner and stock flags preserved.');

        const legalReady=async(canPreflight=true)=>{
            await page.locator(`#legalEntityTable [data-csp-action=legal-entity-edit][data-csp-id="${info.legalEntityId}"]`).waitFor();
            if(canPreflight){await page.waitForFunction(()=>document.getElementById('canaryHealthBadge')?.textContent.trim()!=='Đang tải'&&!document.getElementById('preflightSummary')?.textContent.includes('Đang kiểm tra'));}
            await mobile.settle(page);
        };
        await page.goto(info.baseUrl+'/Admin/LegalEntityManagement/Index');await legalReady();
        assert.equal(await page.locator('#multiLegalEntityEnabled').isChecked(),false);
        await mobile.review(page,'legal-native-gates-and-health',info.output,{root:'#legalEntityPage'});
        await page.locator('#btnCreateLegalEntity').click();
        assert.equal(await page.locator('#legalEntityDefaultWarehouse').isDisabled(),true);
        await mobile.dialog(page,'#legalEntityModal','legal-create-cancel',info.output);await close('#legalEntityModal');
        await page.locator(`#legalEntityTable [data-csp-action=legal-entity-edit][data-csp-id="${info.legalEntityId}"]`).click();
        assert.equal(await page.locator('#legalEntityDefaultWarehouse').inputValue(),String(info.warehouseId));
        const ownerOptions=await page.locator('#legalEntityDefaultWarehouse option').evaluateAll(options=>options.filter(option=>option.value).map(option=>Number(option.value)));
        assert.deepEqual(ownerOptions,[info.warehouseId]);
        assert.equal(await page.locator('#legalEntitySalePriority').inputValue(),'2');
        await mobile.dialog(page,'#legalEntityModal','legal-native-edit-note',info.output);
        await page.locator('#legalEntityNote').fill('UI13 native legal note');
        const legalSaved=page.waitForResponse(response=>response.request().method()==='PUT'&&new URL(response.url()).pathname===`/admin/api/legal-entities/${info.legalEntityId}`);
        await page.locator('#btnSaveLegalEntity').click();assert.equal((await legalSaved).status(),200);
        await page.locator('#legalEntityModal').waitFor({state:'hidden'});await legalReady();
        assert.equal(await page.locator('#multiLegalEntityEnabled').isChecked(),false);
        await mobile.review(page,'legal-after-native-note-edit',info.output,{root:'#legalEntityPage'});
        observations.push('Native legal entity health/preflight, create cancel and note edit; owner options contain only own warehouse, priority2/default warehouse/invoice assignment and multi feature remain unchanged.');

        const reconciliationReady=async()=>{await page.locator('.le-page').waitFor();assert.match(await page.locator('.le-page').innerText(),/UI13-RECON-ORDER/);await mobile.settle(page);};
        await page.goto(info.baseUrl+'/admin/legal-entities/reconciliation');await reconciliationReady();
        await mobile.review(page,'reconciliation-native-allocation-summary',info.output,{root:'.le-page'});
        await page.locator('form.le-filter [name=FromDate]').fill(info.fromDate);await page.locator('form.le-filter [name=ToDate]').fill(info.toDate);
        await page.locator('form.le-filter [name=LegalEntityId]').selectOption(String(info.legalEntityId));
        await page.locator('form.le-filter [name=Keyword]').fill('UI13-RECON-ORDER');
        await Promise.all([page.waitForURL(url=>url.pathname==='/admin/legal-entities/reconciliation'&&url.searchParams.get('LegalEntityId')===String(info.legalEntityId)),page.locator('form.le-filter button[type=submit]').click()]);
        await reconciliationReady();
        assert.match(await page.locator('.le-page').innerText(),/UI13-LEGAL/);
        await mobile.review(page,'reconciliation-native-filtered',info.output,{root:'.le-page'});
        const sourceLink=page.locator(`.le-page a.le-link[href="/admin/pos/order-detail/${info.orderId}"]`);
        assert.equal(await sourceLink.count(),1);
        await Promise.all([page.waitForURL(url=>url.pathname===`/admin/pos/order-detail/${info.orderId}`),sourceLink.click()]);
        assert.match(await page.locator('body').innerText(),/UI13-RECON-ORDER/);
        observations.push('Native reconciliation GET form scopes date, HKD and order; original order drilldown link reaches correct isolated order. Allocation remains120; no invoice fabrication or feature activation.');
        await manager.context.close();

        const reader=await login(info.viewer);page=reader.page;
        result=await gotoData('inventory','inventory-inquiry');assert.equal(result.canViewCost,false);
        assert.equal(await page.locator('#inventoryMobileList .inventory-mobile-cost').count(),0);
        await mobile.review(page,'inquiry-viewer-no-cost',info.output,{root:'[data-inventory-inquiry-index]'});
        await inquiryQuick('inquiry-viewer-units-no-cost',false);
        result=await gotoData('ledger','inventory-ledger');assert.equal(result.canViewCost,false);
        assert.equal(await page.locator('#ledgerMobileList .ledger-mobile-cost').count(),0);
        await mobile.review(page,'ledger-viewer-no-cost',info.output,{root:'[data-inventory-ledger-index]'});
        const readerQuick=dataRequest('/admin/inventory-ledger/quick-view',url=>url.searchParams.get('transactionId')===String(info.transactionId));
        await page.locator(`#ledgerMobileList .js-ledger-quick[data-transaction-id="${info.transactionId}"]`).click();
        const readerResponse=await readerQuick;assert.equal(readerResponse.status(),200);
        assert.equal(Object.hasOwn((await readerResponse.json()).item,'inboundCost'),false);
        await page.locator('#ledgerQuickBody .ledger-quick-metrics').waitFor();
        assert.equal(await page.locator('#ledgerQuickBody .ledger-inbound-cost').count(),0);
        await mobile.dialog(page,'#ledgerQuickViewModal','ledger-viewer-no-cost-sheet',info.output);await close('#ledgerQuickViewModal');
        await page.goto(info.baseUrl+'/Admin/WarehouseManagement/Index');
        await page.waitForFunction(()=>document.querySelector('#warehouseMobileList .warehouse-mobile-card'));
        assert.equal(await page.locator('#btnCreateWarehouse').count(),0);
        assert.equal(await page.locator('[data-csp-action=warehouse-edit]').count(),0);
        assert.equal(await page.locator('#warehouseMobileList input:not([disabled])').count(),0);
        await mobile.review(page,'warehouse-viewer-native-disabled-controls',info.output,{root:'.warehouse-page'});
        await page.goto(info.baseUrl+'/Admin/LegalEntityManagement/Index');
        await page.waitForFunction(()=>document.getElementById('legalEntityTotal')?.textContent.trim()==='2');
        assert.equal(await page.locator('#btnCreateLegalEntity').count(),0);assert.equal(await page.locator('[data-csp-action=legal-entity-edit]').count(),0);
        assert.equal(await page.locator('#multiLegalEntityEnabled').count(),0);
        assert.equal(await page.locator('#multiLegalEntityReadOnlyState').count(),1);
        await mobile.review(page,'legal-viewer-native-read-only-gate',info.output,{root:'#legalEntityPage'});
        await page.goto(info.baseUrl+'/admin/legal-entities/reconciliation');await reconciliationReady();
        assert.equal(await page.locator('.le-actions a[href*="/export"]').count(),0);
        await mobile.review(page,'reconciliation-viewer-no-export',info.output,{root:'.le-page'});
        await reader.context.close();
        assert.deepEqual(errors,[],'No original owner JavaScript errors or unexpected native dialog');
        fs.writeFileSync(path.join(info.output,'browser-checks.json'),JSON.stringify({passed:true,observations,errors,existingChromeWindowsUntouched:true,freshHeadlessContexts:true},null,2)+'\n');
        console.log(JSON.stringify({passed:true,observations}));
    }catch(error){
        console.error(error.stack||error);
        if(page&&!page.isClosed()){
            await page.screenshot({path:path.join(info.output,'browser-failure.png'),fullPage:true,animations:'disabled'}).catch(()=>{});
            fs.writeFileSync(path.join(info.output,'browser-failure.json'),JSON.stringify({error:String(error),url:page.url(),errors,observations},null,2)+'\n');
        }
        process.exitCode=1;
    }finally{await browser.close();}
})();
