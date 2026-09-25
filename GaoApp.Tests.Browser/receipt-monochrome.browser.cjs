'use strict';
const {chromium} = require('playwright');
const fs = require('node:fs'), path = require('node:path');
const templates = require('../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
const {assertBlackInk, assertMoneyFits, assertWideItems} = require('./receipt-templates.browser.cjs');
(async () => {
    const output = path.resolve('TestResults/receipt-templates/monochrome'); fs.mkdirSync(output, {recursive:true});
    const browser = await chromium.launch({channel:'chrome',headless:true});
    const page = await browser.newPage();
    const receipt = {storeName:'GẠO · MARKET',storeAddress:'128 Nguyễn Văn Cừ, TP. Hồ Chí Minh',storePhone:'0901 234 567',
        orderNumber:'POS-001286',createdAtUtc:'2026-09-10T03:25:00Z',cashierName:'Nguyễn Minh Anh',customerName:'Nguyễn Hoàng An',
        subtotal:285000,discountTotal:15000,grandTotal:270000,paidTotal:300000,changeDue:30000,
        lines:[{itemName:'Cà phê Arabica rang mộc',quantity:2,sellingUnitName:'Gói',unitPrice:85000,lineTotal:170000},
            {itemName:'Trà ô long túi lọc',quantity:1,sellingUnitName:'Hộp',unitPrice:75000,lineTotal:75000},
            {itemName:'Bánh hạnh nhân nguyên hạt',quantity:1,sellingUnitName:'Gói',unitPrice:40000,lineTotal:40000}],
        payments:[{method:0,amount:300000}]};
    try {
        for (const option of templates.builtIns()) {
            for (const large of [false,true]) {
                const data = large ? {...receipt,grandTotal:123456789,paidTotal:200000000,changeDue:76543211,
                    lines:[{itemName:'Sản phẩm giá trị lớn',quantity:100,unitPrice:1234567,lineTotal:123456789}]} : receipt;
                const result = templates.render(data, {...option.design,accentColor:'#ff2200'});
                await page.setViewportSize({width:Math.ceil(result.size.width*96/25.4),height:1200});
                await page.setContent(result.html);
                for (const media of ['screen','print']) {
                    await page.emulateMedia({media}); await assertBlackInk(page); await assertMoneyFits(page);
                    if (option.design.layout === 'itemwide') await assertWideItems(page, data.lines);
                }
                if (!large) {
                    await page.locator('.receipt').screenshot({path:path.join(output,option.key+'.png')});
                    if (['modern','itemwide'].includes(option.design.layout)) await page.pdf({path:path.join(output,option.key+'.pdf'),preferCSSPageSize:true,printBackground:false});
                }
            }
        }
        const longLines = [{itemName:'Sữa tắm gội xả 3 trong 1 dành cho bé Suave Kids hương trái cây chai lớn 1180 ml',quantity:1.25,sellingUnitName:'Thùng 24 chai',unitPrice:60000,lineTotal:73000,lineDiscount:2000,sku:'SKU-001'},
            {productVariantName:'Gạo <đặc biệt> & thơm dài hạt',quantity:2,unitName:'Kg',unitPrice:35000,lineTotal:70000}];
        await page.setViewportSize({width:303,height:1200});
        await page.setContent(templates.render({...receipt,lines:longLines},{layout:'itemwide',showSku:true}).html);
        for (const media of ['screen','print']) {
            await page.emulateMedia({media}); await assertWideItems(page,longLines); await assertMoneyFits(page); await assertBlackInk(page);
        }
        await page.locator('.receipt').screenshot({path:path.join(output,'itemwide-80-long-names.png')});
        await page.setContent(templates.render({...receipt,lines:[]},{layout:'itemwide'}).html);
        await page.locator('.items td[colspan="4"]').filter({hasText:'Chưa có sản phẩm'}).waitFor();
        console.log('PASS: 16 receipt designs, normal and large amounts, screen/print black ink, heavy text and unbroken numeric columns; 80 mm full-width names, fractional quantities, long units and empty receipts.');
    } catch(error) {
        await page.screenshot({path:path.join(output,'failure.png'),fullPage:true}); throw error;
    } finally { await browser.close(); }
})().catch(error => {console.error(error);process.exitCode=1;});
