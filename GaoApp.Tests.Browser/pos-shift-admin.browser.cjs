const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => resolve(s)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const output = path.resolve('TestResults/pos-shift-admin'); fs.mkdirSync(output, { recursive: true });
    const errors = [];
    const contexts = [];
    async function login(user, password) {
        const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } }); contexts.push(context);
        await context.addInitScript(() => { window.print = () => { window.__printStarted = true; }; });
        context.on('page', p => p.on('pageerror', e => errors.push(e.message)));
        const page = await context.newPage(); page.setDefaultTimeout(25000);
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(user); await page.locator('[name=Password]').fill(password);
        if (await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        return page;
    }
    let admin, employee;
    try {
        admin = await login(info.user, info.password);
        await admin.goto(info.baseUrl + '/admin/pos-shift/handover-slips');
        const nav = admin.locator('.pos-workspace-nav');
        assert.equal(await nav.locator(':scope > a, :scope > button, :scope > .pos-nav-group').count(), 4);
        await admin.locator('#posShiftNavToggle').click();
        await admin.locator('#posShiftNavMenu.show').waitFor();
        assert.equal(await admin.locator('#posShiftNavMenu [aria-current="page"]').innerText(), 'Phiếu nhận ca');
        await admin.keyboard.press('ArrowDown');
        assert.equal(await admin.evaluate(() => document.activeElement.getAttribute('href')), '/admin/pos-shift');
        await admin.keyboard.press('Escape');
        assert.equal(await admin.locator('#posShiftNavToggle').getAttribute('aria-expanded'), 'false');
        await admin.locator('h1').click();
        await admin.screenshot({ path: path.join(output, 'navigation-desktop.png'), animations: 'disabled' });
        for (const width of [390, 320]) {
            await admin.setViewportSize({ width, height: 844 });
            assert.ok((await nav.boundingBox()).height <= 114, 'Mobile navigation stays within two rows');
            assert.ok(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2));
            if (width === 390) await admin.screenshot({ path: path.join(output, 'navigation-mobile.png'), animations: 'disabled' });
            for (const group of ['Shift', 'Management']) {
                await admin.locator(`#pos${group}NavToggle`).click();
                const menu = admin.locator(`#pos${group}NavMenu.show`);
                await menu.waitFor();
                const bounds = await menu.boundingBox();
                assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width + 1, 'Dropdown stays inside the phone viewport');
                if (width === 390 && group === 'Shift') await admin.screenshot({ path: path.join(output, 'navigation-mobile-menu.png'), animations: 'disabled' });
                await admin.keyboard.press('Escape');
            }
        }
        await admin.setViewportSize({ width: 1440, height: 1000 });
        if (process.env.POS_NAVIGATION_ONLY === '1') {
            employee = await login(info.employeeUser, info.employeePassword);
            await employee.goto(info.baseUrl + '/admin/pos-shift');
            assert.equal(await employee.locator('a[href="/admin/pos-shift/handover-slips"]').count(), 0);
            assert.equal(await employee.locator('a[href="/admin/pos-shift/manager-dashboard"]').count(), 0);
            await employee.locator('#posManagementNavToggle').click();
            assert.equal(await employee.locator('#posManagementNavMenu a[href="/admin/pos-shift/requests"]').isVisible(), true);
            await admin.setViewportSize({ width: 390, height: 844 });
            for (const [url, group] of [['/admin/pos-shift/history', 'Shift'], ['/admin/pos-shift/reconciliation', 'Shift'],
                ['/admin/pos/orders-page', 'Management'], ['/admin/pos/dashboard', 'Management'], ['/admin/pos-shift/requests', 'Management'],
                ['/admin/pos-shift/manager-dashboard', 'Management']]) {
                await admin.goto(info.baseUrl + url);
                const toggle = admin.locator(`#pos${group}NavToggle`);
                assert.match(await toggle.getAttribute('class'), /active/);
                await toggle.click();
                const selected = admin.locator(`#pos${group}NavMenu [aria-current="page"]`);
                assert.equal(await selected.getAttribute('href'), url);
                assert.equal(await selected.isVisible(), true);
                assert.ok(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2));
            }
            assert.deepEqual(errors, []);
            console.log('PASS: compact POS navigation on desktop and 320/390px phones; keyboard, active pages, all shared workspaces and admin-only links verified.');
            return;
        }
        await admin.locator('#btnShowCreateSlipModal').click();
        await admin.locator('#createSlipModal.show').waitFor();
        assert.equal(await admin.locator('#ddlCreateSlipEmployee').count(), 0, 'Receiver is recorded when the shift opens');
        await admin.locator('#ddlCreateSlipTerminal').selectOption(String(info.terminalId));
        await admin.locator('#ddlCreateSlipWarehouse').selectOption(String(info.warehouseId));
        await admin.locator('#slipQty_50000').fill('2');
        await admin.locator('#txtCreateSlipNote').fill('Tiền lẻ đầu ca');
        await admin.screenshot({ path: path.join(output, 'create-desktop.png'), fullPage: false, animations: 'disabled' });
        await admin.setViewportSize({ width: 390, height: 844 });
        await admin.screenshot({ path: path.join(output, 'create-mobile.png'), fullPage: false, animations: 'disabled' });
        assert.ok(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2), 'Mobile page should not overflow horizontally');
        const mobileConfirm = await admin.locator('#btnCreateSlipConfirm').boundingBox();
        assert.ok(mobileConfirm.y >= 0 && mobileConfirm.y + mobileConfirm.height <= 844, 'Create confirmation stays visible on mobile');
        await admin.setViewportSize({ width: 1440, height: 1000 });
        const createdPromise = admin.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/admin/pos/shift-handover-slips'));
        const popupPromise = admin.waitForEvent('popup');
        await admin.locator('#btnCreateSlipConfirm').click();
        const createdResponse = await createdPromise; assert.equal(createdResponse.status(), 200);
        let slip = await createdResponse.json(); const print = await popupPromise;
        assert.equal(slip.assignedToUserId, null);
        await print.locator('#assignedTo').filter({ hasText: 'Ghi nhận khi nhận ca' }).waitFor();
        await print.waitForFunction(() => document.querySelector('#barcode')?.children.length > 0);
        await print.screenshot({ path: path.join(output, 'handover-print.png'), fullPage: false, animations: 'disabled' });
        await admin.waitForFunction(async id => (await (await fetch('/admin/pos/shift-handover-slips/' + id)).json()).status === 2, slip.id);
        await print.waitForFunction(() => window.__printStarted);
        const printedClosed = print.waitForEvent('close');
        await print.evaluate(() => window.dispatchEvent(new Event('afterprint')));
        await printedClosed;
        assert.equal(admin.isClosed(), false, 'Finishing print keeps the handover manager open');

        employee = await login(info.employeeUser, info.employeePassword);
        await employee.goto(info.baseUrl + '/admin/pos-shift');
        assert.equal(await employee.locator('a[href="/admin/pos-shift/handover-slips"]').count(), 0);
        await employee.locator('#btnShowOpenModal').click();
        await employee.locator('#openShiftModal.show').waitFor();
        await employee.locator('#txtHandoverSlipBarcode').fill(slip.barcodeValue);
        await employee.locator('#txtHandoverSlipBarcode').press('Enter');
        await employee.waitForFunction(id => document.querySelector('#selectedHandoverSlipId')?.value === String(id), slip.id);
        // The cashier has loaded the old amount. Admin edits a previously printed slip.
        const oldBarcode = slip.barcodeValue;
        await admin.locator(`[data-edit-slip="${slip.id}"]`).click();
        await admin.locator('#createSlipModal.show').waitFor();
        assert.equal(await admin.locator('#slipQty_50000').inputValue(), '2');
        assert.equal(await admin.locator('#txtCreateSlipNote').inputValue(), 'Tiền lẻ đầu ca');
        await admin.locator('#slipQty_50000').fill('3');
        await admin.locator('#txtCreateSlipNote').fill('Sửa thành 150.000');
        await admin.setViewportSize({ width: 390, height: 844 });
        await admin.screenshot({ path: path.join(output, 'edit-mobile.png'), fullPage: false, animations: 'disabled' });
        assert.ok(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2));
        const editedResponse = admin.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/update'));
        const newPrintPromise = admin.waitForEvent('popup');
        await admin.locator('#btnCreateSlipConfirm').click();
        const saved = await editedResponse; assert.equal(saved.status(), 200);
        slip = await saved.json(); assert.notEqual(slip.barcodeValue, oldBarcode);
        assert.equal(slip.openingCashTotal, 150000);
        const newPrint = await newPrintPromise;
        await newPrint.locator('#barcodeText').filter({ hasText: slip.barcodeValue }).waitFor();
        await newPrint.screenshot({ path: path.join(output, 'edited-print.png'), fullPage: false, animations: 'disabled' });
        await admin.waitForFunction(async id => (await (await fetch('/admin/pos/shift-handover-slips/' + id)).json()).status === 2, slip.id);
        await newPrint.waitForFunction(() => window.__printStarted);
        const editedPrintClosed = newPrint.waitForEvent('close');
        await newPrint.evaluate(() => window.dispatchEvent(new Event('afterprint')));
        await editedPrintClosed;
        const stale = await employee.evaluate(async ({ id, barcode }) => {
            const res = await fetch('/admin/pos/shift/open', { method: 'POST', headers: {
                'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
            }, body: JSON.stringify({ handoverSlipId: id, handoverBarcodeValue: barcode }) });
            return res.status;
        }, { id: slip.id, barcode: oldBarcode });
        assert.equal(stale, 409, 'Already loaded old barcode cannot silently receive the edited amount');
        const reloadBarcode = employee.waitForResponse(r => r.url().includes('/shift-handover-slips/barcode?barcodeValue=' + slip.barcodeValue));
        await employee.locator('#txtHandoverSlipBarcode').fill(slip.barcodeValue);
        await employee.locator('#txtHandoverSlipBarcode').press('Enter');
        assert.equal((await reloadBarcode).status(), 200);
        await employee.waitForFunction(() => document.querySelector('#openQty_50000').value === '3');
        const openResponse = employee.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/admin/pos/shift/open'));
        await employee.locator('#btnOpenShiftConfirm').click();
        assert.equal((await openResponse).status(), 200);
        await admin.goto(info.baseUrl + '/admin/pos-shift/handover-slips');
        await admin.locator(`[data-view-slip="${slip.id}"]`).waitFor();
        assert.equal(await admin.locator(`[data-edit-slip="${slip.id}"]`).count(), 0, 'Used slip is locked');
        const used = await admin.evaluate(async id => (await fetch('/admin/pos/shift-handover-slips/' + id)).json(), slip.id);
        assert.equal(used.usedByUserId, info.employeeId);
        assert.ok(await admin.locator('tbody').innerText().then(x => x.includes(used.usedByUserName)));
        await admin.setViewportSize({ width: 1440, height: 1000 });

        const close = await employee.evaluate(async () => {
            const res = await fetch('/admin/pos/shift/close', { method: 'POST', headers: {
                'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
            }, body: JSON.stringify({ closingCashActual: 99000, note: 'Nhân viên khai thiếu 1.000' }) });
            return { status: res.status, body: await res.json() };
        });
        assert.equal(close.status, 200, JSON.stringify(close.body));

        const closingPopupPromise = employee.waitForEvent('popup');
        await employee.evaluate(id => window.open('/admin/pos-shift/closing-slip-print?shiftId=' + id, '_blank'), close.body.id);
        const closingPrint = await closingPopupPromise;
        await closingPrint.waitForFunction(() => window.__printStarted);
        assert.match(await closingPrint.locator('#actualCash').innerText(), /99.000/);
        const closingPrinted = closingPrint.waitForEvent('close');
        await closingPrint.evaluate(() => window.dispatchEvent(new Event('afterprint')));
        await closingPrinted;
        assert.equal(employee.isClosed(), false, 'Closing a printed closing slip keeps the employee page open');

        await admin.goto(info.baseUrl + '/admin/pos-shift/manager-dashboard');
        await admin.locator('#shiftBody [data-detail]').first().waitFor();
        await admin.locator('#filterCashReceipt').selectOption('pending');
        await admin.locator('#shiftBody [data-detail]').first().click();
        await admin.locator('#cashReceiptForm').waitFor();
        await admin.locator('#cashReceivedAmount').fill('98000');
        assert.equal(await admin.locator('#cashReceiptNote').getAttribute('required'), '');
        await admin.locator('#cashReceiptNote').fill('Admin kiểm lại thiếu thêm 1.000');
        await admin.screenshot({ path: path.join(output, 'receipt-desktop.png'), fullPage: false, animations: 'disabled' });
        await admin.setViewportSize({ width: 390, height: 844 });
        await admin.screenshot({ path: path.join(output, 'receipt-mobile.png'), fullPage: false, animations: 'disabled' });
        assert.ok(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2));
        const receiptResponse = admin.waitForResponse(r => r.request().method() === 'POST' && r.url().includes('/cash-receipt'));
        await admin.locator('#btnConfirmCashReceipt').click();
        assert.equal((await receiptResponse).status(), 200);
        await admin.waitForFunction(() => document.querySelector('#cashReceiptForm').hidden && document.querySelector('#detailContent').textContent.includes('98.000'));
        assert.ok((await admin.locator('#detailContent').innerText()).includes('Đã nhận tiền / Đã duyệt'));
        await admin.reload();
        await admin.locator('#filterCashReceipt').selectOption('received');
        await admin.locator('#shiftBody [data-detail]').first().click();
        assert.equal(await admin.locator('#cashReceiptForm').isVisible(), false);
        assert.ok((await admin.locator('#detailContent').innerText()).includes('Admin kiểm lại thiếu thêm 1.000'));
        assert.deepEqual(errors, []);
        console.log('PASS terminal-only slip creates/prints, edits on mobile, rejects loaded old barcode, receives with actual employee and locks editing; closing cash is reviewed, confirmed and persists after reload; desktop/mobile forms and print checked.');
    } catch (e) {
        for (const [name, page] of [['admin', admin], ['employee', employee]]) if (page) await page.screenshot({ path: path.join(output, name + '-failure.png'), fullPage: false, animations: 'disabled' }).catch(() => {});
        throw e;
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
