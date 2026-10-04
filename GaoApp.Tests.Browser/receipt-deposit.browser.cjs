const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
const templates = require('../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
const { assertMoneyFits, assertBlackInk } = require('./receipt-templates.browser.cjs');
(async () => {
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 800, height: 1200 } });
        fs.mkdirSync('TestResults/receipt-deposit', { recursive: true });
        for (const design of templates.builtIns().map(x => x.design)) {
            for (const deposit of [0, 200000, 228000]) {
                const receipt = { storeName: 'Gao Mart', orderNumber: 'POS-TEST', customerName: 'Khách kiểm tra',
                    subtotal: 228000, grandTotal: 228000, depositAmount: deposit, paidTotal: 228000,
                    lines: [{ itemName: 'Khoai môn sấy chấm sữa', quantity: 4, unitPrice: 57000, lineTotal: 228000, unitName: 'Hũ' }],
                    payments: deposit === 228000 ? [] : [{ method: 0, amount: 228000 - deposit }] };
                await page.setContent(templates.render(receipt, design).html);
                for (const media of ['screen', 'print']) {
                    await page.emulateMedia({ media });
                    await assertMoneyFits(page); await assertBlackInk(page);
                    assert.equal(await page.locator('.deposit-applied').count(), deposit ? 1 : 0);
                    assert.match(await page.locator('.grand').innerText(), /228\.000/);
                    if (deposit) {
                        assert.match(await page.locator('.deposit-applied').innerText(), new RegExp(templates.money(deposit).replaceAll('.', '\\.')));
                        assert.match(await page.locator('.totals tr').filter({ hasText: 'Cần trả sau cọc' }).innerText(), new RegExp(templates.money(228000 - deposit).replaceAll('.', '\\.')));
                        assert.equal(await page.locator('.payment-line').filter({ hasText: 'Tiền cọc đã dùng' }).count(), 1);
                    }
                }
                if (design.layout === 'modern' && design.paperSize === '80' && deposit === 200000)
                    await page.locator('.receipt').screenshot({ path: 'TestResults/receipt-deposit/receipt-80.png' });
            }
        }
        console.log('PASS: 16 receipt designs, no/partial/full deposit; amounts fit in screen and print.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
