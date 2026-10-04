'use strict';
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
async function assertBlackInk(page) {
    const result = await page.locator('.receipt').evaluate(receipt => {
        const nodes = [receipt, ...receipt.querySelectorAll('*')];
        const text = nodes.filter(node => [...node.childNodes].some(child => child.nodeType === 3 && child.textContent.trim()));
        return {
            colored: text.filter(node => getComputedStyle(node).color !== 'rgb(0, 0, 0)').map(node => node.className || node.tagName),
            thin: text.filter(node => Number(getComputedStyle(node).fontWeight) < 600).map(node => node.className || node.tagName),
            filled: nodes.filter(node => !['rgba(0, 0, 0, 0)', 'rgb(255, 255, 255)'].includes(getComputedStyle(node).backgroundColor)).map(node => node.className || node.tagName),
            important: [...receipt.querySelectorAll('.store,.title,.number,.grand td,.settlement td')].every(node => Number(getComputedStyle(node).fontWeight) >= 800)
        };
    });
    assert.deepEqual(result.colored, [], 'Receipt text must be pure black');
    assert.deepEqual(result.thin, [], 'Receipt text must not use light/regular strokes');
    assert.deepEqual(result.filled, [], 'Printed content must use white/transparent backgrounds');
    assert.equal(result.important, true, 'Important receipt fields must be bold');
}
async function assertMoneyFits(page) {
    const failures = await page.locator('.receipt').evaluate(receipt => {
        const errors = [];
        for (const cell of receipt.querySelectorAll('td.num,.item-values .num,.totals div.num,.amount,.payment-line span:last-child')) {
            for (const node of cell.childNodes) {
                if (node.nodeType !== 3 && node.nodeName !== 'STRONG') continue;
                if (!node.textContent.trim()) continue;
                const range = document.createRange(); range.selectNodeContents(node);
                const rects = [...range.getClientRects()], bounds = cell.getBoundingClientRect();
                if (rects.length !== 1 || rects.some(r => r.left < bounds.left - 1 || r.right > bounds.right + 1)) errors.push(node.textContent.trim());
            }
        }
        return errors;
    });
    assert.deepEqual(failures, [], 'Money and quantities must stay on one line inside their columns');
}
async function assertWideItems(page, lines) {
    const groups = await page.locator('.itemwide .item-group').evaluateAll(groups => groups.map(group => {
        const heading = group.querySelector('.item-heading td'), values = group.querySelector('.item-values');
        const name = heading.querySelector('.item-name'), table = group.closest('table');
        return { name: name.textContent, values: [...values.querySelectorAll('[data-item-value]')].map(cell => cell.textContent.trim()),
            fullWidth: Math.abs(heading.getBoundingClientRect().width - table.getBoundingClientRect().width) < 1,
            belowName: values.getBoundingClientRect().top >= name.getBoundingClientRect().bottom,
            keepTogether: getComputedStyle(group).breakInside === 'avoid' };
    }));
    assert.equal(groups.length, lines.length);
    const money = value => Number(value || 0).toLocaleString('vi-VN', {maximumFractionDigits:2});
    groups.forEach((group, index) => {
        const line = lines[index];
        assert.equal(group.name, line.productVariantName || line.itemName || line.productName);
        assert.deepEqual(group.values, [money(line.quantity), line.sellingUnitName || line.unitName || '—', money(line.unitPrice), money(line.lineTotal)]);
        assert.equal(group.fullWidth, true, 'Product name must span all four columns');
        assert.equal(group.belowName, true, 'Quantity, unit and prices must appear below the name');
        assert.equal(group.keepTogether, true, 'Printing must keep the name with its values');
    });
}
async function mockPrinters(page) {
    await page.evaluate(() => {
        window.__printJobs = [];
        window.qz = { websocket: { isActive: () => true, setUsingSurf() {} },
            printers: { find: async () => ['Windows EPSON TM-T82', 'Linux_CUPS_HP'] },
            configs: { create: (printer, options) => ({ printer, options }) },
            print: async (config, data) => { window.__printJobs.push({ config, data }); } };
    });
}
async function prepare(page, context, info, output) {
    output = path.join(output, 'receipts'); fs.mkdirSync(output, { recursive: true });
    const studio = await context.newPage();
    await studio.goto(info.baseUrl + '/admin/receipt-templates');
    await studio.locator('.template-card').first().waitFor();
    assert.equal(await studio.locator('.template-card').count(), 16);
    await verifyStoreIdentity(studio, page, context, info, output);
    await verifyWidePreview(studio, output);
    await verifyClassicPreview(studio, output);
    await mockPrinters(studio);
    await studio.locator('[data-size="45"]').click();
    assert.equal(await studio.locator('.template-card').count(), 3);
    await studio.locator('[data-template-key="modern-45"]').click();
    await studio.locator('#designName').fill('Gạo · Bill quầy 45 mm');
    await studio.locator('#designFooter').fill('Cảm ơn quý khách!\nĐổi hàng trong 7 ngày với hóa đơn.');
    await studio.locator('#saveTemplate').click();
    await studio.locator('#studioMessage').filter({ hasText: 'Đã lưu mẫu.' }).waitFor();
    await studio.locator('#printMode').selectOption('qz');
    await studio.locator('#connectPrinters').click();
    await studio.locator('#studioMessage').filter({ hasText: 'Đã tìm thấy 2 máy in' }).waitFor();
    await studio.locator('#clientPrinter').selectOption('Linux_CUPS_HP');
    await studio.locator('#printCopies').fill('2');
    await studio.locator('#applyTemplate').click();
    await studio.locator('#setStoreDefault').click();
    await studio.locator('#studioMessage').filter({hasText:'Đã đặt mẫu mặc định'}).waitFor();
    await page.waitForFunction(() => PosOffline.status().context.receiptDefault?.template?.design.paperSize === '45');
    await studio.locator('#activePrintProfile').filter({ hasText: 'Linux_CUPS_HP' }).waitFor();
    await studio.screenshot({ path: path.join(output, 'studio-45.png'), fullPage: true });
    const receipt = { storeName: 'GẠO · MARKET', storeAddress: '128 Nguyễn Văn Cừ, TP. Hồ Chí Minh', storePhone: '0901 234 567',
        orderNumber: 'POS-001286', createdAtUtc: '2026-09-10T03:25:00Z', cashierName: 'Nguyễn Minh Anh', customerName: 'Nguyễn Hoàng An',
        subtotal: 285000, discountTotal: 15000, grandTotal: 270000, paidTotal: 300000, changeDue: 30000,
        lines: [{ itemName: 'Cà phê Arabica rang mộc', quantity: 2, sellingUnitName: 'Gói', unitPrice: 85000, lineTotal: 170000 },
            { itemName: 'Trà ô long túi lọc', quantity: 1, sellingUnitName: 'Hộp', unitPrice: 75000, lineTotal: 75000 },
            { itemName: 'Bánh hạnh nhân nguyên hạt', quantity: 1, sellingUnitName: 'Gói', unitPrice: 40000, lineTotal: 40000 }], payments: [{ method: 0, amount: 300000 }] };
    const paper = await context.newPage();
    for (const size of ['45', '80', 'A6', 'A5', 'A4']) {
        for (const layout of ['modern', 'classic', 'compact']) {
            const html = await studio.evaluate(({ receipt, size, layout }) => ReceiptTemplates.render(receipt, { paperSize: size, layout, accentColor:'#aa4488' }).html, { receipt, size, layout });
            const width = { '45': 45, '80': 80, A6: 105, A5: 148, A4: 210 }[size];
            await paper.setViewportSize({ width: Math.ceil(width * 96 / 25.4), height: 1123 });
            await paper.setContent(html);
            await assertBlackInk(paper);
            await assertMoneyFits(paper);
            await paper.emulateMedia({media:'print'}); await assertBlackInk(paper);
            await paper.emulateMedia({media:'screen'});
            const bounds = await paper.evaluate(() => {
                const receipt = document.querySelector('.receipt').getBoundingClientRect();
                return { width: receipt.width, overflows: [...document.querySelectorAll('table')].some(t => t.getBoundingClientRect().right > receipt.right + 1) };
            });
            assert.ok(Math.abs(bounds.width - width * 96 / 25.4) < 1);
            assert.equal(bounds.overflows, false, `Table overflow on ${layout}/${size}`);
            if (layout === 'modern') {
                await paper.locator('.receipt').screenshot({ path: path.join(output, 'receipt-' + size + '.png') });
                await paper.pdf({ path: path.join(output, 'receipt-' + size + '.pdf'), preferCSSPageSize: true, printBackground: false });
            }
        }
    }
    await paper.close();
    await studio.locator('[data-size="A4"]').click(); await studio.locator('[data-template-key="classic-A4"]').click();
    await studio.screenshot({ path: path.join(output, 'studio-a4.png'), fullPage: true });
    await studio.close();
    await mockPrinters(page);
    console.log('PASS: template library, store customization, 15 layout/size previews and client printer choice');
}
async function verifyWidePreview(studio, output) {
    await studio.locator('[data-size="80"]').click();
    assert.equal(await studio.locator('.template-card').count(), 4);
    await studio.locator('[data-size="all"]').click();
    await studio.locator('[data-template-key="classic-A4"]').click();
    await studio.locator('#designLayout').selectOption('itemwide');
    assert.equal(await studio.locator('#designSize').inputValue(), '80');
    assert.equal(await studio.locator('#designSize option:disabled').count(), 4);
    await studio.locator('#designLayout').selectOption('classic');
    assert.equal(await studio.locator('#designSize option:disabled').count(), 0);
    studio.once('dialog', dialog => dialog.accept());
    await studio.locator('[data-template-key="itemwide-80"]').click();
    const preview = studio.frameLocator('#receiptPreview');
    await preview.locator('.receipt.itemwide[data-paper-size="80"]').waitFor();
    assert.equal(await preview.locator('.item-heading td[colspan="4"]').count(), 3);
    assert.deepEqual(await preview.locator('.item-values').first().locator('td').allTextContents(), ['2','Gói','85.000','170.000']);
    await assertBlackInk(preview); await assertMoneyFits(preview);
    await studio.locator('.studio-preview-panel').screenshot({path:path.join(output,'itemwide-preview-80.png')});
    await studio.locator('#designName').fill('Tên hàng rộng · Mẫu quầy');
    await Promise.all([studio.waitForResponse(r => r.url().endsWith('/receipt-templates/data') && r.request().method() === 'POST' && r.ok()), studio.locator('#saveTemplate').click()]);
    await studio.locator('#studioMessage').filter({hasText:'Đã lưu mẫu.'}).waitFor();
    await studio.reload();
    await studio.locator('.template-card').filter({hasText:'Tên hàng rộng · Mẫu quầy'}).click();
    await preview.locator('.receipt.itemwide[data-paper-size="80"]').waitFor();
    assert.equal(await studio.locator('#designLayout').inputValue(), 'itemwide');
    assert.equal(await studio.locator('#designSize option:disabled').count(), 4);
    console.log('PASS: new 80 mm preset, full-width product names, four values below each name and saved custom layout survive reload');
}
async function verifyStoreIdentity(studio, pos, context, info, output) {
    const name = 'Tiệm Gạo An Bình', address = '12 Nguyễn Trãi, Phường An Bình', phone = '0909 123 456';
    assert.equal(await studio.locator('#receiptStoreName').inputValue(), 'R2 Inventory Posting Store');
    assert.equal(await studio.locator('#receiptStoreAddress').inputValue(), '');
    assert.equal(await studio.locator('#receiptStorePhone').inputValue(), '');
    await studio.frameLocator('#receiptPreview').locator('.store').filter({ hasText: 'R2 Inventory Posting Store' }).waitFor();
    await studio.locator('#receiptStoreName').fill(name);
    await studio.locator('#receiptStoreAddress').fill(address);
    await studio.locator('#receiptStorePhone').fill(phone);
    await studio.frameLocator('#receiptPreview').locator('.brand').filter({ hasText: phone }).waitFor();
    await studio.locator('#saveStoreIdentity').click();
    await studio.locator('#storeIdentityStatus').filter({ hasText: 'Đã lưu cho tất cả mẫu' }).waitFor();
    await pos.waitForFunction(name => PosOffline.status().context.receiptStoreInfo?.storeName === name, name);
    const another = await context.newPage();
    await another.goto(info.baseUrl + '/admin/receipt-templates');
    assert.equal(await another.locator('#receiptStoreName').inputValue(), name);
    assert.equal(await another.locator('#receiptStoreAddress').inputValue(), address);
    assert.equal(await another.locator('#receiptStorePhone').inputValue(), phone);
    await another.close();
    await studio.locator('[data-template-key="classic-A4"]').click();
    await studio.frameLocator('#receiptPreview').locator('.receipt.classic .brand').filter({ hasText: phone }).waitFor();
    await studio.locator('[aria-labelledby=storeIdentityTitle]').screenshot({ path: path.join(output, 'store-identity-settings.png') });
    console.log('PASS: shared store identity replaces demo branding, survives reload/template changes and refreshes the open POS offline cache');
}
async function verifyClassicPreview(studio, output) {
    const fits = async size => studio.waitForFunction(size => {
        const frame = document.getElementById('receiptPreview'), stage = frame.parentElement, doc = frame.contentDocument;
        const receipt = doc?.querySelector('.receipt.classic'), style = getComputedStyle(stage);
        return receipt?.dataset.paperSize === size
            && frame.getBoundingClientRect().width <= stage.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight) + 1
            && frame.clientHeight >= receipt.getBoundingClientRect().height
            && frame.clientHeight <= receipt.getBoundingClientRect().height + 3
            && doc.documentElement.scrollHeight <= doc.documentElement.clientHeight + 2
            && doc.documentElement.scrollWidth <= doc.documentElement.clientWidth + 2;
    }, size).catch(async error => {
        console.log('Preview failure:', await studio.evaluate(() => {
            const frame = document.getElementById('receiptPreview'), stage = frame.parentElement, doc = frame.contentDocument;
            return { viewport: innerWidth, paper: doc.querySelector('.receipt')?.dataset.paperSize, zoom: frame.style.zoom,
                paperWidth: frame.getBoundingClientRect().width, stageWidth: stage.clientWidth,
                frameHeight: frame.clientHeight, receiptHeight: doc.querySelector('.receipt')?.getBoundingClientRect().height,
                rootHeight: doc.documentElement.clientHeight, rootScrollHeight: doc.documentElement.scrollHeight,
                rootWidth: doc.documentElement.clientWidth, rootScrollWidth: doc.documentElement.scrollWidth };
        }));
        throw error;
    });
    for (const size of ['45', '80', 'A6', 'A5', 'A4']) {
        await studio.locator(`[data-template-key="classic-${size}"]`).click();
        for (const width of [1440, 1000, 390, 1440]) {
            await studio.setViewportSize({ width, height: 1000 });
            await fits(size);
            const physicalWidth = await studio.frameLocator('#receiptPreview').locator('.receipt').evaluate(el => el.getBoundingClientRect().width);
            const expected = { '45': 45, '80': 80, A6: 105, A5: 148, A4: 210 }[size] * 96 / 25.4;
            assert.ok(Math.abs(physicalWidth - expected) < 1, 'Preview scaling must preserve the physical receipt width');
            if (width === 1000) await studio.locator('.studio-preview-panel').screenshot({ path: path.join(output, 'classic-preview-' + size + '.png') });
        }
    }
    const footer = studio.frameLocator('#receiptPreview').locator('.footer');
    const longFooter = ('Thông tin đổi hàng và bảo hành của cửa hàng.\n').repeat(9).slice(0, 400);
    await studio.locator('#designFooter').fill(longFooter);
    await footer.filter({ hasText: 'Thông tin đổi hàng' }).waitFor(); await fits('A4');
    const longHeight = await studio.locator('#receiptPreview').evaluate(el => el.clientHeight);
    await studio.locator('#designFooter').fill('Cảm ơn quý khách.');
    await footer.filter({ hasText: 'Cảm ơn quý khách.' }).waitFor(); await fits('A4');
    assert.ok(await studio.locator('#receiptPreview').evaluate(el => el.clientHeight) < longHeight, 'Shorter configuration must shrink the preview');
    assert.equal(await studio.locator('#designAccent,#printColor').count(), 0, 'Color controls are removed from bill settings');
    await assertBlackInk(studio.frameLocator('#receiptPreview'));
    await fits('A4');
    await studio.locator('.studio-preview-panel').screenshot({ path: path.join(output, 'classic-monochrome.png') });
    studio.once('dialog', dialog => dialog.accept());
    await studio.locator('[data-template-key="modern-80"]').click();
    await studio.setViewportSize({ width: 1440, height: 1000 });
    console.log('PASS: classic preview fits all 5 sizes after desktop/mobile resizing, shrinks after edits and uses black, heavy text');
}
async function offlinePrint(page, orderId, output) {
    // Retain the popup for visual assertions while checking the requested auto-close.
    await page.context().addInitScript(() => { window.__closeRequests = 0; window.close = () => window.__closeRequests++; });
    const popupReady = page.waitForEvent('popup');
    assert.equal(await page.evaluate(id => PosOffline.print(id), orderId), true);
    const popup = await popupReady;
    await popup.locator('.receipt[data-paper-size="45"]').waitFor();
    await popup.locator('.local-print-tools').filter({ hasText: 'Đã gửi tới Linux_CUPS_HP' }).waitFor();
    await popup.waitForFunction(() => window.__closeRequests === 1);
    const jobs = await page.evaluate(() => window.__printJobs);
    assert.equal(jobs.length, 1); assert.equal(jobs[0].config.printer, 'Linux_CUPS_HP'); assert.equal(jobs[0].config.options.copies, 2);
    assert.equal(jobs[0].config.options.colorType, 'grayscale'); await assertBlackInk(popup);
    assert.match(jobs[0].data[0].data, /Chờ đồng bộ máy chủ/);
    assert.match(jobs[0].data[0].data, /Tiệm Gạo An Bình/); assert.match(jobs[0].data[0].data, /12 Nguyễn Trãi, Phường An Bình/); assert.match(jobs[0].data[0].data, /0909 123 456/);
    await popup.screenshot({ path: path.join(output, 'receipts', 'offline-selected-template.png'), fullPage: true });
    await popup.close();
    console.log('PASS: offline sale uses the saved custom 45 mm template and selected local printer');
}
async function onlinePrint(context, info, orderId, output) {
    const page = await context.newPage();
    await page.addInitScript(() => { window.__closeRequests = 0; window.close = () => window.__closeRequests++; });
    await page.goto(info.baseUrl + `/admin/pos/orders/${orderId}/print?autoPrint=false`);
    await page.frameLocator('#receiptPaper').locator('.receipt[data-paper-size="45"]').waitFor();
    await mockPrinters(page); await page.locator('#printReceipt').click();
    await page.locator('#printFeedback').filter({ hasText: 'Đã gửi lệnh tới Linux_CUPS_HP' }).waitFor();
    await page.waitForFunction(() => window.__closeRequests === 1);
    const jobs = await page.evaluate(() => window.__printJobs);
    assert.equal(jobs.length, 1); assert.equal(jobs[0].config.options.size.width, 45);
    assert.equal(jobs[0].config.options.colorType, 'grayscale'); await assertBlackInk(page.frameLocator('#receiptPaper'));
    assert.doesNotMatch(jobs[0].data[0].data, /Chờ đồng bộ máy chủ/);
    assert.match(jobs[0].data[0].data, /Đổi hàng trong 7 ngày/);
    assert.match(jobs[0].data[0].data, /Tiệm Gạo An Bình/); assert.match(jobs[0].data[0].data, /12 Nguyễn Trãi, Phường An Bình/); assert.match(jobs[0].data[0].data, /0909 123 456/);
    await page.screenshot({ path: path.join(output, 'receipts', 'online-selected-template.png'), fullPage: true });
    await page.close();
    console.log('PASS: server receipt and offline receipt use the same store default and selected local printer');
    const staffContext = await context.browser().newContext();
    const staff = await staffContext.newPage();
    await staff.goto(info.baseUrl + '/admin/account/login');
    await staff.locator('[name=UserName]').fill(info.cashierUser); await staff.locator('[name=Password]').fill(info.cashierPassword);
    if (await staff.locator('[name=SelectedTerminalId]').count()) await staff.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
    await Promise.all([staff.waitForURL(url => !url.pathname.endsWith('/login')), staff.locator('#loginForm button[type=submit]').click()]);
    await staff.goto(info.baseUrl + '/admin/receipt-templates');
    assert.equal(new URL(staff.url()).pathname, '/admin/account/access-denied');
    assert.equal(await staff.locator('#receiptStudio').count(), 0);
    // A fresh employee browser with an old local template must still receive the shared 45 mm design.
    await staff.goto(info.baseUrl + `/admin/pos/orders/${orderId}/print?autoPrint=false&size=A4`);
    await staff.evaluate(() => {
        const setup = JSON.parse(document.getElementById('receiptPrintData').textContent);
        localStorage.setItem(`gao-pos-print-v1:${setup.storeId}:${setup.terminalId || 'admin'}`, JSON.stringify({template:{paperSize:'A4',title:'LOCAL OVERRIDE'}}));
    });
    await staff.reload();
    await staff.frameLocator('#receiptPaper').locator('.receipt[data-paper-size="45"]').waitFor();
    assert.equal(await staff.locator('#printTemplateChoice').count(), 0);
    assert.equal(await staff.locator('a[href="/admin/receipt-templates"]').count(), 0);
    assert.match(await staff.locator('#printTemplateName').innerText(), /Bill quầy 45 mm/);
    await staff.evaluate(() => { window.__staffPrints = 0; document.getElementById('receiptPaper').contentWindow.print = () => window.__staffPrints++; });
    await staff.locator('#printReceipt').click();
    assert.equal(await staff.evaluate(() => window.__staffPrints), 1);
    await staff.screenshot({path:path.join(output, 'receipts', 'employee-store-default.png'), fullPage:true});
    await staffContext.close();
    console.log('PASS: employee cannot configure templates, receives shared default in a fresh browser, ignores size override and prints without choosing a template');
}
module.exports = { prepare, mockPrinters, offlinePrint, onlinePrint, assertBlackInk, assertMoneyFits, assertWideItems };
