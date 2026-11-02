'use strict';
// Real notification owner and Bootstrap review; synthetic API only, no money,
// phone, database or printer is touched by these browser regressions.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..'), origin = 'https://gao-bank-recovery.test';
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const view = read('GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml');
const start = view.indexOf('<div class="modal fade pos-bank-review-modal"');
const end = view.indexOf('<div class="modal fade"', start + 1);
assert.ok(start >= 0 && end > start, 'Use the actual POS review markup');
const markup = view.slice(start, end);
const qr = { id: 301, orderId: 1838418, readOnly: false, automaticConfirmation: false };
const item = { receiptId: 101, waitId: 201, qrId: qr.id, orderId: qr.orderId, amount: 170000,
    senderName: 'KHACH CHUYEN KHOAN', transactionCode: 'GD:RECOVERY12345', automatic: true,
    postedAtUtc: '2026-10-15T09:00:00Z', content: '+170.000 VND từ KHACH CHUYEN KHOAN tại ACB POS-1838418 MGD:41045' };
const html = '<!doctype html><html lang="vi"><meta charset="utf-8"><body><main><h1>POS thử phục hồi</h1>' +
    '<input name="__RequestVerificationToken" value="SYNTHETIC-CSRF" type="hidden"></main>' +
    '<div class="modal fade" id="paymentQrModal"><div class="modal-dialog"><div class="modal-content">' +
    '<div class="modal-body">Đang đợi chuyển khoản</div></div></div></div>' + markup + '</body></html>';
const output = path.join(root, '.artifacts/bank-phone-background-20261015/browser-recovery');

(async () => {
    fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ headless: true, ...(process.env.GAO_PLAYWRIGHT_CHROME
        ? { executablePath: process.env.GAO_PLAYWRIGHT_CHROME } : { channel: 'chrome' }) });
    const errors = [], outcomes = [];
    try {
        async function scenario({ arms = [200], confirmations = [200] } = {}) {
            const context = await browser.newContext({ viewport: { width: 1366, height: 900 }, reducedMotion: 'reduce' });
            const page = await context.newPage(), calls = [];
            page.setDefaultTimeout(5000);
            page.on('pageerror', error => errors.push(error.message));
            let now = new Date('2026-10-15T09:00:00Z').getTime(), armEnabled = false, pending = [item];
            let armCount = 0, confirmationCount = 0;
            await page.clock.setFixedTime(now);
            await page.route(origin + '/**', async route => {
                const request = route.request(), url = new URL(request.url());
                if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
                const endpoint = url.pathname.replace('/admin/pos/bank-notifications', '');
                calls.push({ endpoint, method: request.method(), body: request.method() === 'POST' ? request.postDataJSON() : undefined });
                let status = 200, result;
                if (endpoint === '/admin-approvals') result = [];
                else if (endpoint === '/pending') result = armEnabled ? pending : [];
                else if (endpoint === '/arm/301') {
                    const response = arms[Math.min(armCount++, arms.length - 1)];
                    status = typeof response === 'number' ? response : 200;
                    armEnabled = typeof response === 'object' ? response.enabled === true : status === 200;
                    result = status === 200 ? { enabled: armEnabled } : { message: 'Tạm thời chưa kết nối được điện thoại' };
                } else if (endpoint === '/status/301') result = { qrId: 301, ended: false, manual: false,
                    startedAtUtc: '2026-10-15T09:00:00Z', serverNowUtc: new Date(now).toISOString(),
                    warningAfterSeconds: 60, manualAfterSeconds: 120, phones: [] };
                else if (endpoint === '/confirm') {
                    const code = confirmations[Math.min(confirmationCount++, confirmations.length - 1)];
                    if (code === 'network') return route.abort('failed');
                    status = code;
                    if (status === 200) {
                        pending = [];
                        result = { receiptId: 101, qrId: 301, orderId: 1838418, finalized: false, paidAmount: 170000, remainingAmount: 0 };
                    } else result = { message: status === 409 ? 'Khoản chờ đã thay đổi; cần kiểm tra trước khi xác nhận.' : 'Máy chủ tạm bận, giữ nguyên khoản chuyển.' };
                } else return route.fulfill({ status: 404, body: 'Unexpected endpoint: ' + url.pathname });
                return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(result) });
            });
            await page.goto(origin + '/');
            for (const file of ['vendor/css/core.css', 'Admin/css/pos/pos.bank-notifications.css'])
                await page.addStyleTag({ content: read('GaoApp.Web/wwwroot/' + file) });
            await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/vendor/js/bootstrap.js') });
            await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/Admin/js/pos/pos.bank-transfer-review.js') });
            await page.evaluate(() => {
                window.completed = []; window.reviewShown = 0; window.pendingReads = 0;
                window.PosOffline = { status: () => ({ connected: true, pending: 0 }) };
                document.getElementById('bankTransferReviewModal').addEventListener('shown.bs.modal', () => window.reviewShown++);
                window.addEventListener('bank-notification:completed', event => completed.push(event.detail));
                const originalFetch = window.fetch.bind(window);
                window.fetch = async (...args) => {
                    const response = await originalFetch(...args);
                    if (String(args[0]).endsWith('/pending')) pendingReads++;
                    return response;
                };
            });
            await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/Admin/js/pos/pos.bank-notifications.js') });
            await page.waitForFunction(() => pendingReads > 0);
            await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 0)));
            return { context, page, calls, armCount: () => armCount, confirmationCount: () => confirmationCount,
                setPending: value => { pending = value; },
                track: () => page.evaluate(qr => PosBankNotifications.track(qr), qr),
                refresh: () => page.evaluate(() => PosBankNotifications.refresh()),
                advance: async milliseconds => { now += milliseconds; await page.clock.setFixedTime(now); },
                close: () => context.close() };
        }
        const confirms = h => h.calls.filter(call => call.endpoint === '/confirm');
        async function assertCompletedOnce(h) {
            await h.page.waitForFunction(() => completed.length === 1);
            assert.equal(await h.page.evaluate(() => reviewShown), 0, 'Known POS never opens the cashier comparison popup');
            assert.equal(await h.page.locator('#bankTransferReviewModal').isVisible(), false);
            await h.advance(30000); await h.refresh(); await h.refresh();
            assert.equal(await h.page.evaluate(() => completed.length), 1);
            assert.ok(confirms(h).every(call => call.body.receiptId === 101 && call.body.waitId === 201 && call.body.automatic === true));
        }

        for (const [name, first] of [['disabled', { enabled: false }], ['server-error', 503]]) {
            const h = await scenario({ arms: [first, { enabled: true }] });
            await h.track(); assert.equal(h.armCount(), 1); assert.equal(h.confirmationCount(), 0);
            await h.advance(14999); await h.refresh(); assert.equal(h.armCount(), 1, 'No arm storm before fifteen seconds');
            await h.advance(1); await h.refresh();
            assert.equal(h.armCount(), 2, 'An active QR retries arming once the phone/configuration recovers');
            await assertCompletedOnce(h); assert.equal(h.confirmationCount(), 1);
            outcomes.push('arm-' + name); await h.close();
        }

        for (const failure of [500, 429, 'network']) {
            const h = await scenario({ confirmations: [failure, 200] });
            await h.track(); assert.equal(h.confirmationCount(), 1);
            await h.advance(2999); await h.refresh(); assert.equal(h.confirmationCount(), 1);
            await h.advance(1); await h.refresh();
            await assertCompletedOnce(h); assert.equal(h.confirmationCount(), 2);
            const trace = h.calls.map(call => call.endpoint), second = trace.lastIndexOf('/confirm');
            assert.equal(trace[second - 1], '/pending', 'Retry must use a new server pending snapshot');
            outcomes.push('confirm-recovered-' + failure); await h.close();
        }

        const bounded = await scenario({ confirmations: [500] });
        await bounded.track(); assert.equal(bounded.confirmationCount(), 1);
        await bounded.advance(3000); await bounded.refresh(); assert.equal(bounded.confirmationCount(), 2);
        await bounded.advance(5999); await bounded.refresh(); assert.equal(bounded.confirmationCount(), 2);
        await bounded.advance(1); await bounded.refresh(); assert.equal(bounded.confirmationCount(), 3);
        await bounded.advance(60000); await bounded.refresh(); await bounded.refresh();
        assert.equal(bounded.confirmationCount(), 3, 'Stop after three total automatic attempts');
        assert.equal(await bounded.page.evaluate(() => completed.length), 0);
        assert.equal(await bounded.page.getByRole('button', { name: 'Thử xác nhận lại' }).isVisible(), true);
        outcomes.push('confirm-bounded-three'); await bounded.close();

        const changed = await scenario({ confirmations: [500, 200] });
        await changed.track(); changed.setPending([]); await changed.advance(3000); await changed.refresh();
        assert.equal(changed.confirmationCount(), 1, 'A stale candidate cannot be retried after it leaves the fresh pending list');
        assert.equal(await changed.page.evaluate(() => completed.length), 0);
        outcomes.push('changed-pending-no-retry'); await changed.close();

        const externalHide = await scenario();
        externalHide.setPending([{ ...item, automatic: false }]);
        await externalHide.track();
        await externalHide.page.locator('#bankTransferReviewModal').waitFor({ state: 'visible' });
        await externalHide.page.waitForFunction(() => !PosBankTransferReview.busy());
        assert.equal(externalHide.confirmationCount(), 0);
        externalHide.setPending([item]);
        await externalHide.refresh();
        assert.equal(externalHide.confirmationCount(), 0, 'A visible cashier review must continue to own the decision');
        assert.equal(await externalHide.page.locator('#bankTransferReviewModal').isVisible(), true);
        await externalHide.page.evaluate(() => bootstrap.Modal.getOrCreateInstance(document.getElementById('bankTransferReviewModal')).hide());
        await externalHide.page.locator('#bankTransferReviewModal').waitFor({ state: 'hidden' });
        await externalHide.page.waitForFunction(() => !PosBankTransferReview.busy());
        await externalHide.refresh();
        await externalHide.page.waitForFunction(() => completed.length === 1);
        assert.equal(externalHide.confirmationCount(), 1, 'An externally hidden review cannot freeze fresh automatic candidates');
        assert.equal(await externalHide.page.evaluate(() => reviewShown), 1);
        assert.equal(externalHide.calls.filter(call => call.endpoint === '/dismiss').length, 0, 'UI cleanup must not dismiss or confirm money by itself');
        assert.deepEqual(confirms(externalHide).map(call => call.body), [{ receiptId: 101, waitId: 201, automatic: true }]);
        await externalHide.advance(30000); await externalHide.refresh();
        assert.equal(externalHide.confirmationCount(), 1);assert.equal(await externalHide.page.evaluate(() => completed.length), 1);
        outcomes.push('external-review-hide-releases-fresh-auto'); await externalHide.close();

        for (const failure of [403, 409]) {
            const h = await scenario({ confirmations: [failure, 200] });
            await h.track(); assert.equal(h.confirmationCount(), 1);
            await h.advance(60000); await h.refresh(); await h.refresh();
            assert.equal(h.confirmationCount(), 1, 'Business/auth rejection must never trigger automatic retries');
            assert.equal(await h.page.evaluate(() => completed.length), 0);
            const retry = h.page.getByRole('button', { name: 'Thử xác nhận lại' });
            assert.equal(await retry.isVisible(), true);
            if (failure === 409) {
                assert.match(await h.page.locator('#bankNotificationSuggestions').innerText(), /Khoản chờ đã thay đổi/);
                await h.page.screenshot({ path: path.join(output, 'conflict-retry-card.png') });
                await retry.click(); await assertCompletedOnce(h); assert.equal(h.confirmationCount(), 2);
            }
            outcomes.push('confirm-no-auto-retry-' + failure); await h.close();
        }
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ status: 'PASS', scenarios: outcomes,
            source: 'Actual notification JavaScript, POS review markup and Bootstrap; synthetic APIs', physicalHardware: false }, null, 2));
        console.log(JSON.stringify({ status: 'PASS', scenarios: outcomes.length, cases: outcomes }));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
