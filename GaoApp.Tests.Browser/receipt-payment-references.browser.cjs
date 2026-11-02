'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const { chromium } = require('playwright');
const templates = require('../GaoApp.Web/wwwroot/Admin/js/printing/receipt.templates.js');
const { assertMoneyFits } = require('./receipt-templates.browser.cjs');

(async () => {
    const output = path.resolve('TestResults/receipt-payment-references/browser');
    fs.mkdirSync(output, { recursive: true });
    const qr1 = 'QR-1799607-9a700d6ab6c0402099b673909da876fe';
    const qr2 = 'QR-1799607-b6fbd01dee4e44e669f6a2acf971eede';
    const receipt = { storeName: 'GẠO MART', orderId: 1799607, orderNumber: 'POS-20261010-00001',
        createdAtUtc: '2026-10-10T11:00:00Z', customerName: 'Khách lẻ', cashierName: 'Quản trị Demo',
        subtotal: 400000, grandTotal: 400000, paidTotal: 400000,
        lines: [{ itemName: 'Bánh men tuổi thơ', quantity: 7, unitPrice: 40000, lineTotal: 280000 },
            { itemName: 'Sữa tươi TH có đường bịch 220ml', quantity: 3, unitPrice: 10000, lineTotal: 30000 },
            { itemName: 'Bánh tai heo nhí 350g', quantity: 2, unitPrice: 45000, lineTotal: 90000 }] };
    const cases = [
        { name: 'multiple', references: ['QR-1799607-1', 'QR-1799607-2'], amounts: ['30.000', '50.000', '320.000'],
            payments: [{ method: 'Cash', amount: 30000 }, { method: 'BankTransfer', amount: 50000, reference: qr1 },
                { method: 'BankTransfer', amount: 320000, reference: qr2 }] },
        { name: 'single', references: ['QR-1799607'], amounts: ['30.000', '370.000'],
            payments: [{ method: 'Cash', amount: 30000 }, { method: 'BankTransfer', amount: 370000, reference: qr1 }] }
    ];
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const page = await browser.newPage();
    try {
        for (const option of templates.builtIns()) {
            for (const sample of cases) {
                const data = { ...receipt, payments: sample.payments };
                const original = JSON.stringify(data);
                const result = templates.render(data, option.design);
                assert.equal(JSON.stringify(data), original);
                await page.setViewportSize({ width: Math.ceil(result.size.width * 96 / 25.4), height: 1200 });
                await page.setContent(result.html);
                for (const media of ['screen', 'print']) {
                    await page.emulateMedia({ media });
                    assert.deepEqual(await page.locator('.payment-line small').allTextContents(), sample.references);
                    assert.deepEqual(await page.locator('.payment-line span:last-child').allTextContents(), sample.amounts);
                    const text = await page.locator('.receipt').innerText();
                    assert.ok(!text.includes(qr1) && !text.includes(qr2));
                    await assertMoneyFits(page);
                    assert.ok(await page.locator('.receipt').evaluate(node => node.scrollWidth <= node.clientWidth + 1));
                }
                if (['modern-45', 'modern-80', 'itemwide-80'].includes(option.key)) {
                    await page.locator('.receipt').screenshot({ path: path.join(output, option.key + '-' + sample.name + '.png') });
                }
            }
        }
        console.log('PASS: single/multiple QR transfer references and exact amounts in all 16 bill designs, screen and print, including 45 mm; raw data unchanged.');
    } catch (error) {
        await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true });
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
