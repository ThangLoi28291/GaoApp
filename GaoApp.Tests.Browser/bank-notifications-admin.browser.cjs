const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..');
const artifact = path.join(root, '.artifacts/pos-recovery-drawer/admin-browser');
const view = fs.readFileSync(path.join(root, 'GaoApp.Web/Areas/Admin/Views/BankNotifications/Index.cshtml'), 'utf8');
const modal = view.slice(view.indexOf('<div class="modal fade"'), view.indexOf('@section Scripts'));
const script = fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot/Admin/js/pos/bank-notifications.admin.js'), 'utf8');
const bootstrap = fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot/lib/bootstrap/dist/js/bootstrap.bundle.min.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css'), 'utf8');
const html = `<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style><body class="bg-light p-4">
<h1 class="h4">Nhận thông báo chuyển khoản ACB</h1><input id="bridgeName" placeholder="Tên điện thoại"><div class="card"><div class="card-body">
<form data-bank-approval data-sender="KHACH CHUYEN TIEN" data-reference="MGD: TEST-17" data-amount="28000">
<input name="__RequestVerificationToken" value="test-csrf" type="hidden"><input name="receiptId" value="17" type="hidden"><input name="amount" value="28000" type="hidden">
<select name="qrId" class="form-select" required><option value="">Chọn đúng quầy / đơn</option><option value="51" data-order="100" data-terminal="Quầy thu ngân 01">Quầy thu ngân 01 · Đơn #100 · 28.000 đ</option><option value="52" data-order="101" data-terminal="Quầy thu ngân 02">Quầy thu ngân 02 · Đơn #101 · 28.000 đ</option></select>
<button type="submit" class="btn btn-primary mt-2">Đã nhận tiền</button></form></div></div>${modal}<script>${bootstrap}</script><script>${script}</script></body></html>`;
(async () => {
    fs.mkdirSync(artifact, { recursive: true });
    const browser = await chromium.launch(process.env.GAO_PLAYWRIGHT_CHROME ? { executablePath: process.env.GAO_PLAYWRIGHT_CHROME } : { channel: 'chrome' });
    try {
        const page = await browser.newPage({ viewport: { width: 1100, height: 760 } });
        const errors = [], sent = [];
        let recorded=false,historyLoads=0,historyChecks=0;
        const historyState=()=>`<div id="bankNotificationLiveState"><table><tbody id="bankNotificationDeviceRows" data-bank-live-region><tr data-live-key="device-6"><td>Đang kết nối</td></tr></tbody></table><table><tbody id="bankNotificationWaitingRows" data-bank-live-region><tr data-live-key="empty"><td>Không có khoản chuyển khoản đang chờ.</td></tr></tbody></table><div id="bankNotificationHistory" data-pending-qr-ids="${recorded?'':'52'}"><table><tbody id="bankNotificationHistoryRows" data-bank-live-region><tr data-live-key="qr-52"><td>${recorded?'Admin xác nhận — đã ghi nhận tiền':'Admin đã gửi xác nhận — chờ quầy ghi nhận'}</td></tr></tbody></table></div></div>`;
        page.on('pageerror', error => errors.push(error.message));
        await page.route('https://gao-admin.test/**', async route => {
            const url=new URL(route.request().url());
            if(url.pathname.endsWith('/live-state')){
                historyChecks++;
                await route.fulfill({contentType:'text/html',body:historyState()});
            }else if (url.pathname.endsWith('/approve')) {
                sent.push({ body: route.request().postDataJSON(), headers: route.request().headers() });
                // A synthetic conflict keeps the reviewed modal visible and exercises error recovery.
                await route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ message: 'Khoản chờ đã thay đổi. Tải lại để kiểm tra.' }) });
            } else {
                if(url.searchParams.has('history'))historyLoads++;
                const history=url.searchParams.has('history')?`<p id="bankNotificationLiveStatus" role="status" data-live-url="/admin/bank-notifications/live-state">Tự cập nhật mỗi 3 giây.</p>${historyState()}`:'';
                await route.fulfill({ contentType: 'text/html', body:html.replace('<h1',history+'<h1') });
            }
        });
        await page.goto('https://gao-admin.test/admin/bank-notifications');
        await page.getByRole('button', { name: 'Đã nhận tiền', exact: true }).click();
        assert.equal(sent.length, 0);
        assert.equal(await page.locator('#adminBankApprovalModal').isVisible(), false, 'equal amounts require an explicit target');
        await page.locator('select[name="qrId"]').selectOption('52');
        await page.getByRole('button', { name: 'Đã nhận tiền', exact: true }).click();
        await page.locator('#adminBankApprovalModal.show').waitFor();
        await page.waitForFunction(() => getComputedStyle(document.getElementById('adminBankApprovalModal')).opacity === '1');
        assert.equal(sent.length, 0, 'opening review does not send financial approval');
        assert.match(await page.locator('#adminApprovalTarget').textContent(), /Quầy thu ngân 02.*#101/);
        assert.equal(await page.locator('#adminApprovalSender').textContent(), 'KHACH CHUYEN TIEN');
        assert.match(await page.locator('#adminApprovalAmount').textContent(), /28.000/);
        await page.screenshot({ path: path.join(artifact, 'approval-review.png') });
        await page.getByRole('button', { name: 'Đúng — Đã nhận tiền', exact: true }).click();
        await page.getByRole('alert').filter({ hasText: 'Khoản chờ đã thay đổi' }).waitFor();
        assert.deepEqual(sent[0].body, { qrId: 52, amount: 28000, receiptId: 17 });
        assert.equal(sent[0].headers.requestverificationtoken, 'test-csrf');
        assert.equal(await page.locator('#adminApprovalConfirm').isEnabled(), true);
        assert.deepEqual(errors, []);
        await page.setViewportSize({ width: 390, height: 844 });
        await page.screenshot({ path: path.join(artifact, 'approval-mobile.png') });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
        await page.goto('https://gao-admin.test/admin/bank-notifications?history=1');
        for(let attempt=0;historyChecks<1&&attempt<80;attempt++)await new Promise(resolve=>setTimeout(resolve,50));
        assert.ok(historyChecks>0);assert.equal(historyLoads,1,'Pending command is not a recorded payment');
        await page.locator('#bridgeName').fill('Điện thoại đang nhập');await page.locator('h1').click();
        recorded=true;
        await page.waitForFunction(()=>document.getElementById('bankNotificationHistory')?.dataset.pendingQrIds==='');
        assert.equal(historyLoads,1,'Refresh history in place when POS has recorded the pending admin approval');
        assert.equal(await page.locator('#bridgeName').inputValue(),'Điện thoại đang nhập','Keep unfinished phone settings while history changes');
        assert.match(await page.locator('#bankNotificationHistoryRows').innerText(),/Admin xác nhận — đã ghi nhận tiền/);
        assert.deepEqual(errors,[]);assert.equal(sent.length,1,'Refreshing history cannot submit another approval');
        console.log('PASS: Admin review requires a target and explicit confirmation; bank sender/amount/terminal, CSRF, conflict recovery, mobile layout; pending history updates in place after POS recording and preserves unfinished device settings. Synthetic APIs only.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
