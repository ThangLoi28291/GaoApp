'use strict';
// Production admin JavaScript and actual Bootstrap/modal markup. Bank APIs are
// synthetic HTTPS routes; this test never writes a store database or prints.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const artifact = path.join(root, 'TestResults/bank-notifications-live');
const view = read('GaoApp.Web/Areas/Admin/Views/BankNotifications/Index.cshtml');
const modalStart = view.indexOf('<div class="modal fade"');
const modalEnd = view.indexOf('@section Scripts');
assert.ok(modalStart >= 0 && modalEnd > modalStart, 'Actual bank approval modal must exist');
const modal = view.slice(modalStart, modalEnd);
const script = read('GaoApp.Web/wwwroot/Admin/js/pos/bank-notifications.admin.js');
const bootstrap = read('GaoApp.Web/wwwroot/lib/bootstrap/dist/js/bootstrap.bundle.min.js');
const css = read('GaoApp.Web/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css');
const escape = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
const money = value => Number(value).toLocaleString('vi-VN') + ' đ';
const qr = (id, order) => ({ id, order, terminal: 'Quầy thu ngân ' + (id - 50), amount: 170000 });
const receipt = { id: 17, sender: 'HO SU QUANG', amount: 170000, reference: 'GD:SYNTHETIC-17', content: 'Cửa hàng nhận tiền thành công + 170.000 VND từ HO SU QUANG', state: 'unmatched' };

function liveHtml(state) {
    const waiting = state.waiting.map(item => `<tr data-live-key="qr-${item.id}"><td>${escape(item.terminal)}</td><td>#${item.order}</td><td>${money(item.amount)}</td><td>QR-${item.id}</td><td><form data-bank-approval data-order="${item.order}" data-terminal="${escape(item.terminal)}" data-amount="${item.amount}" data-sender="Chưa có thông báo từ điện thoại"><input type="hidden" name="__RequestVerificationToken" value="SYNTHETIC-CSRF"><input type="hidden" name="qrId" value="${item.id}"><input type="hidden" name="amount" value="${item.amount}"><button type="submit">Đã nhận tiền</button></form></td></tr>`).join('');
    const history = state.receipts.map(item => {
        const outcome = { unmatched: 'Chưa ghép / đang chờ xử lý', pending: 'Admin đã gửi xác nhận — chờ quầy ghi nhận', admin: 'Admin xác nhận — đã ghi nhận tiền', automatic: 'Tự xác nhận theo POS' }[item.state];
        const targets = item.state === 'unmatched' ? state.waiting.filter(wait => wait.amount === item.amount) : [];
        const approval = targets.length ? `<form data-bank-approval data-sender="${escape(item.sender)}" data-reference="${escape(item.reference)}" data-amount="${item.amount}"><input type="hidden" name="__RequestVerificationToken" value="SYNTHETIC-CSRF"><input type="hidden" name="receiptId" value="${item.id}"><input type="hidden" name="amount" value="${item.amount}"><select name="qrId" aria-label="Chọn khoản chờ" required><option value="">Chọn đúng quầy / đơn</option>${targets.map(target => `<option value="${target.id}" data-order="${target.order}" data-terminal="${escape(target.terminal)}">${escape(target.terminal)} · Đơn #${target.order}</option>`).join('')}</select><button type="submit">Đã nhận tiền</button></form>` : '';
        return `<tr data-live-key="receipt-${item.id}"><td>2026-10-15 12:36:01</td><td>${money(item.amount)}</td><td>${escape(item.sender)}</td><td>${item.order ? 'POS-' + item.order : ''}</td><td>${escape(item.reference)}</td><td>${outcome}<details><summary>Nội dung gốc</summary><pre>${escape(item.content)}</pre></details>${approval}</td></tr>`;
    }).join('');
    return `<div id="bankNotificationLiveState"><h2>Điện thoại đã đăng ký</h2><table class="table"><tbody id="bankNotificationDeviceRows" data-bank-live-region><tr data-live-key="device-6"><td>Điện thoại cửa hàng #6</td><td>ACCOUNT-TEST</td><td>${escape(state.phone)}</td><td>${state.version}</td></tr></tbody></table><h2>Chuyển khoản đang chờ tại quầy</h2><table class="table"><tbody id="bankNotificationWaitingRows" data-bank-live-region>${waiting || '<tr data-live-key="empty"><td colspan="5">Không có khoản chuyển khoản đang chờ.</td></tr>'}</tbody></table><div id="bankNotificationHistory" data-pending-qr-ids="${state.receipts.filter(item => item.state === 'pending').map(item => item.qrId).join(',')}"><h2>Lịch sử nhận và xác nhận chuyển khoản</h2><table class="table"><tbody id="bankNotificationHistoryRows" data-bank-live-region>${history || '<tr data-live-key="empty"><td colspan="6">Chưa có thông báo hoặc xác nhận chuyển khoản.</td></tr>'}</tbody></table></div></div>`;
}

function pageHtml(state) {
    return `<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}</style></head><body class="p-3"><h1 tabindex="-1">Nhận thông báo chuyển khoản ACB</h1><form id="deviceConfig"><label for="bridgeName">Tên điện thoại</label><input id="bridgeName" name="name"><select id="bridgeBank"><option value="1">ACCOUNT-TEST</option><option value="2">SECOND-TEST</option></select></form><p id="bankNotificationLiveStatus" role="status" aria-live="polite" data-live-url="/admin/bank-notifications/live-state">Tự cập nhật mỗi 3 giây.</p>${liveHtml(state)}${modal}<script>${bootstrap}</script><script>${script}</script></body></html>`;
}

async function waitUntil(check, label) {
    const deadline = Date.now() + 10000;
    while (!check()) {
        assert.ok(Date.now() < deadline, 'Timed out: ' + label);
        await new Promise(resolve => setTimeout(resolve, 20));
    }
}

(async () => {
    fs.mkdirSync(artifact, { recursive: true });
    const browser = await chromium.launch({ ...(process.env.GAO_PLAYWRIGHT_CHROME ? { executablePath: process.env.GAO_PLAYWRIGHT_CHROME } : { channel: 'chrome' }), headless: true });
    const results = [];
    try {
        const context = await browser.newContext({ viewport: { width: 1200, height: 1000 }, reducedMotion: 'reduce' });
        const page = await context.newPage();
        page.setDefaultTimeout(10000);
        const errors = [], posts = [];
        let state = { phone: 'Đang kết nối', version: 1, waiting: [], receipts: [] };
        let navigations = 0, liveRequests = 0, failures = 0, failNext = false;
        let holdNext = false, heldLive, holdApproval = false, releaseApproval;
        page.on('pageerror', error => errors.push(error.message));
        await page.route('https://gao-bank-live.test/**', async route => {
            const request = route.request(), url = new URL(request.url());
            if (url.pathname.endsWith('/live-state')) {
                liveRequests++;
                if (failNext) { failNext = false; failures++; return route.fulfill({ status: 503, body: 'Synthetic temporary outage' }); }
                const body = liveHtml(state);
                if (holdNext) { holdNext = false; await new Promise(resolve => { heldLive = resolve; }); }
                return route.fulfill({ contentType: 'text/html', body });
            }
            if (url.pathname.endsWith('/approve')) {
                posts.push({ body: request.postDataJSON(), headers: request.headers() });
                if (holdApproval) await new Promise(resolve => { releaseApproval = resolve; });
                state = { ...state, version: state.version + 1,
                    waiting: state.waiting.filter(item => item.id !== 52),
                    receipts: state.receipts.map(item => item.id === 17 ? { ...item, state: 'pending', qrId: 52, order: 101 } : item) };
                return route.fulfill({ contentType: 'application/json', body: '{"success":true}' });
            }
            if (url.pathname === '/admin/bank-notifications') {
                navigations++;
                return route.fulfill({ contentType: 'text/html', body: pageHtml(state) });
            }
            return route.fulfill({ status: 404, body: 'Unexpected endpoint: ' + url.pathname });
        });
        const online = () => page.evaluate(() => window.dispatchEvent(new Event('online')));
        await page.goto('https://gao-bank-live.test/admin/bank-notifications');
        await page.waitForLoadState('load');
        state = { ...state, version: 2, waiting: [qr(51, 100), qr(52, 101)], receipts: [{ ...receipt }] };
        // No navigation/event: the ordinary interval must discover a QR created
        // after an initially empty page, including the phone's sender and amount.
        await page.locator('#bankNotificationWaitingRows [data-live-key="qr-52"]').waitFor();
        assert.equal(navigations, 1);
        assert.equal(await page.locator('#bankNotificationWaitingRows [data-live-key^="qr-"]').count(), 2);
        assert.match(await page.locator('#bankNotificationHistoryRows').innerText(), /HO SU QUANG/);
        assert.match(await page.locator('#bankNotificationHistoryRows').innerText(), /170[.,]000/);
        assert.equal(posts.length, 0, 'A live refresh cannot approve a transfer');
        results.push('initially empty page discovers new QR and bank sender/amount by interval');

        await page.locator('#bridgeName').fill('Điện thoại đang nhập');
        await page.locator('#bridgeBank').selectOption('2');
        await page.locator('[data-live-key="receipt-17"] details summary').click();
        await page.locator('[data-live-key="receipt-17"] select').selectOption('52');
        await page.locator('h1').focus();
        state = { ...state, version: 3, phone: 'Mất liên lạc', receipts: [{ ...receipt, content: receipt.content + ' · chi tiết mới' }] };
        await online();
        await page.waitForFunction(() => document.getElementById('bankNotificationDeviceRows').textContent.includes('Mất liên lạc'));
        assert.equal(await page.locator('#bridgeName').inputValue(), 'Điện thoại đang nhập');
        assert.equal(await page.locator('#bridgeBank').inputValue(), '2');
        assert.equal(await page.locator('[data-live-key="receipt-17"] details').evaluate(node => node.open), true);
        assert.equal(await page.locator('[data-live-key="receipt-17"] select').inputValue(), '52');
        results.push('device form, expanded source content and chosen QR survive live refresh');

        await page.locator('[data-live-key="receipt-17"] select').focus();
        state = { ...state, version: 4, phone: 'Có tin chờ gửi' };
        await online();
        await page.waitForTimeout(150);
        assert.match(await page.locator('#bankNotificationDeviceRows').innerText(), /Mất liên lạc/, 'Do not interrupt an active target selection');
        await page.locator('h1').focus();
        await online();
        await page.waitForFunction(() => document.getElementById('bankNotificationDeviceRows').textContent.includes('Có tin chờ gửi'));
        results.push('active cashier target selection defers replacement');

        // A snapshot already in flight must not replace the form after the
        // cashier starts reviewing it. This covers the post-fetch safety check.
        holdNext = true;
        state = { ...state, version: 5, phone: 'Đang kết nối' };
        await online();
        await waitUntil(() => !!heldLive, 'held live response');
        await page.locator('[data-live-key="receipt-17"] form button').click();
        await page.locator('#adminBankApprovalModal.show').waitFor();
        await page.waitForFunction(() => getComputedStyle(document.getElementById('adminBankApprovalModal')).opacity === '1');
        const reviewedHtml = await page.locator('#bankNotificationHistoryRows').innerHTML();
        heldLive(); heldLive = null;
        await page.waitForTimeout(150);
        assert.equal(await page.locator('#bankNotificationHistoryRows').innerHTML(), reviewedHtml);
        assert.equal(await page.locator('#adminApprovalSender').innerText(), 'HO SU QUANG');
        assert.match(await page.locator('#adminApprovalAmount').innerText(), /170[.,]000/);
        assert.match(await page.locator('#adminApprovalTarget').innerText(), /Quầy thu ngân 2.*#101/);
        assert.equal(posts.length, 0);
        results.push('in-flight snapshot cannot alter an open financial review');

        holdApproval = true;
        await page.locator('#adminApprovalConfirm').click();
        await waitUntil(() => posts.length === 1 && !!releaseApproval, 'held approval');
        await online();
        assert.equal(await page.locator('#adminApprovalConfirm').isDisabled(), true);
        assert.equal(await page.locator('#adminApprovalCancel').isDisabled(), true);
        await page.evaluate(() => bootstrap.Modal.getInstance(document.getElementById('adminBankApprovalModal')).hide());
        assert.equal(await page.locator('#adminBankApprovalModal').isVisible(), true, 'Cannot dismiss an unfinished financial request');
        assert.equal(await page.locator('#bankNotificationHistoryRows').innerHTML(), reviewedHtml);
        releaseApproval(); releaseApproval = null;
        await page.locator('#adminBankApprovalModal').waitFor({ state: 'hidden' });
        await page.waitForFunction(() => document.getElementById('bankNotificationHistoryRows').textContent.includes('Admin đã gửi xác nhận — chờ quầy ghi nhận'));
        assert.equal(await page.locator('#bankNotificationWaitingRows [data-live-key="qr-52"]').count(), 0);
        assert.equal(await page.locator('#bankNotificationWaitingRows [data-live-key="qr-51"]').count(), 1);
        assert.deepEqual(posts[0].body, { qrId: 52, amount: 170000, receiptId: 17 });
        assert.equal(posts[0].headers.requestverificationtoken, 'SYNTHETIC-CSRF');
        assert.equal(navigations, 1, 'Successful admin approval updates history without navigating');
        results.push('one explicit admin approval moves QR to truthful pending history without reload');

        state = { ...state, version: 6, receipts: state.receipts.map(item => ({ ...item, state: 'admin' })) };
        await online();
        await page.waitForFunction(() => document.getElementById('bankNotificationHistoryRows').textContent.includes('Admin xác nhận — đã ghi nhận tiền'));
        assert.doesNotMatch(await page.locator('#bankNotificationHistoryRows').innerText(), /chờ quầy ghi nhận/);
        state = { ...state, version: 7, waiting: [], receipts: [...state.receipts, { ...receipt, id: 18, sender: 'NGUYEN THI TEST', state: 'automatic', qrId: 51, order: 100 }] };
        await online();
        await page.waitForFunction(() => document.getElementById('bankNotificationHistoryRows').textContent.includes('Tự xác nhận theo POS'));
        assert.match(await page.locator('#bankNotificationWaitingRows').innerText(), /Không có khoản chuyển khoản đang chờ/);
        assert.equal(await page.locator('#bankNotificationHistoryRows [data-live-key^="receipt-"]').count(), 2);
        assert.equal(posts.length, 1);
        results.push('POS-recorded admin and automatic transfers update waiting/history without extra approval');

        const beforeFailure = await page.locator('#bankNotificationHistoryRows').innerHTML();
        failNext = true;
        await online();
        await waitUntil(() => failures === 1, 'temporary HTTP outage');
        await page.waitForTimeout(150);
        assert.equal(await page.locator('#bankNotificationHistoryRows').innerHTML(), beforeFailure);
        state = { ...state, version: 8, phone: 'Lỗi gửi tin' };
        await online();
        await page.waitForFunction(() => document.getElementById('bankNotificationDeviceRows').textContent.includes('Lỗi gửi tin'));
        state = { ...state, version: 9, phone: 'Đã thu hồi' };
        await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
        await page.waitForFunction(() => document.getElementById('bankNotificationDeviceRows').textContent.includes('Đã thu hồi'));
        assert.equal(navigations, 1);
        assert.equal(posts.length, 1);
        assert.ok(liveRequests >= 8);
        assert.deepEqual(errors, []);
        results.push('temporary outage preserves data and online/visible events recover in place');
        await page.screenshot({ path: path.join(artifact, 'live-history.png'), fullPage: true });
        fs.writeFileSync(path.join(artifact, 'verification.json'), JSON.stringify({ passed: results, syntheticApisOnly: true, navigations, approvalRequests: posts.length, liveRequests, errors }, null, 2));
        console.log(`PASS: ${results.length} bank-notification live Chromium regressions; ${liveRequests} snapshots, one explicit approval, no page reload. Synthetic APIs only.`);
        await context.close();
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
