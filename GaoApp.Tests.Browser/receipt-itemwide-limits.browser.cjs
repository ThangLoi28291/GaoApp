'use strict';
const assert = require('node:assert/strict');
const {chromium} = require('playwright');
const fs = require('node:fs'), path = require('node:path');
const templates = require('../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
const {assertBlackInk, assertMoneyFits, assertWideItems} = require('./receipt-templates.browser.cjs');
const cases = [
    ['normal', 2, 85000],
    ['thousands', 9999, 9999999],
    ['tens-of-thousands', 99999, 99999999],
    ['million', 1000000, 1000000],
    ['fractional', 1234567.89, 1234567],
    ['large-price', 1, 999999999999],
    ['large-total', 999999, 999999999],
    ['mixed-cart', 1234567.89, 1234567]
];
(async () => {
    const output = path.resolve('TestResults/receipt-templates/itemwide-limits'); fs.mkdirSync(output, {recursive:true});
    const browser = await chromium.launch({channel:'chrome',headless:true});
    const page = await browser.newPage({viewport:{width:303,height:1400}}), errors = [];
    try {
        for (const [name,quantity,unitPrice] of cases) {
            const total = Math.round(quantity * unitPrice * 100) / 100;
            const receipt = {storeName:'GẠO · MARKET',orderNumber:'KIỂM TRA SỐ LỚN',createdAtUtc:'2026-09-12T08:00:00Z',
                cashierName:'Nhân viên kiểm tra',subtotal:total,grandTotal:total,paidTotal:total,
                lines:[{itemName:'Sản phẩm kiểm tra số lượng và giá tiền lớn',quantity,unitPrice,lineTotal:total,sellingUnitName:'Thùng 24 chai'}],
                payments:[{method:0,amount:total}]};
            if (name === 'mixed-cart') {
                receipt.lines.unshift({itemName:'Hàng thường',quantity:2,unitPrice:85000,lineTotal:170000,sellingUnitName:'Gói'});
                receipt.lines.push({itemName:'Hàng số lượng nhiều',quantity:99999,unitPrice:15000,lineTotal:1499985000,sellingUnitName:'Hộp'});
                receipt.subtotal = receipt.grandTotal = receipt.paidTotal = Math.round((total + 170000 + 1499985000) * 100) / 100;
                receipt.payments[0].amount = receipt.paidTotal;
            }
            const rendered = templates.render(receipt,{layout:'itemwide'});
            fs.writeFileSync(path.join(output,name+'.html'),rendered.html);
            await page.setContent(rendered.html);
            try {
                for (const media of ['screen','print']) {
                    await page.emulateMedia({media}); await assertBlackInk(page); await assertMoneyFits(page); await assertWideItems(page, receipt.lines);
                    assert.equal(await page.locator('.grand .num').textContent(), templates.money(receipt.grandTotal));
                    if (name === 'mixed-cart') assert.equal(await page.locator('.item-expanded').count(), 1, 'Only the oversized line should expand; other products keep four columns');
                    const bounds = await page.locator('.receipt').evaluate(receipt => {
                        const rect = receipt.getBoundingClientRect(), failures = [];
                        for (const table of receipt.querySelectorAll('table')) {
                            const b = table.getBoundingClientRect();
                            if (b.left < rect.left || b.right > rect.right + 1) failures.push(table.className);
                        }
                        return failures;
                    });
                    assert.deepEqual(bounds, [], 'Tables must stay inside 80 mm paper');
                }
                console.log(`PASS: ${name}: SL ${templates.money(quantity)}, đơn giá ${templates.money(unitPrice)}, thành tiền ${templates.money(total)}`);
            } catch (error) {
                errors.push(name+': '+error.message); console.log('FAIL: '+name+': '+error.message);
            }
            await page.locator('.receipt').screenshot({path:path.join(output,name+'.png')});
        }
    } finally { await browser.close(); }
    assert.deepEqual(errors, [], 'Large quantities and prices must remain readable on the 80 mm receipt');
})().catch(error => {console.error(error);process.exitCode=1;});
