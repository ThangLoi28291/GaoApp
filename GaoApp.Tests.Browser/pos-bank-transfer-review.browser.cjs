'use strict';
// Actual POS review markup, Bootstrap and bank notification owners. All bank
// responses are synthetic HTTPS routes: no database, phone or printer is used.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const output = path.join(root, 'TestResults/pos-bank-transfer-review');
const view = read('GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml');
const start = view.indexOf('<div class="modal fade pos-bank-review-modal"');
const end = view.indexOf('<div class="modal fade"', start + 1);
assert.ok(start >= 0 && end > start, 'Actual POS bank review markup must be present');
const reviewMarkup = view.slice(start, end);
const sourceContent = 'Cửa hàng nhận tiền thành công\n+ 170.000 VND từ HO SU QUANG tại TMCP Nam A - HO SU QUANG chuyen tien GD 6288NAMAP2M4EBGL 151026-09:35:59. MGD: 31045';
const item = { receiptId: 101, waitId: 201, qrId: 301, orderId: 1838418, amount: 170000,
    senderName: 'HO SU QUANG', transactionCode: 'GD:6288NAMAP2M4EBGL',
    postedAtUtc: '2026-10-15T02:36:01Z', content: sourceContent, automatic: false };
const html = '<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>' +
    '<body class="pos-prime-layout"><main><h1>Quầy thử nghiệm</h1><p>Khách tích điểm: <strong id="loyaltyCustomerName">OTHERNAME</strong></p>' +
    '<input name="__RequestVerificationToken" value="SYNTHETIC-CSRF" type="hidden"><div id="fallbackState">Đang chờ xác nhận tự động</div></main>' +
    '<div class="modal fade" id="paymentQrModal" tabindex="-1" aria-hidden="true"><div class="modal-dialog"><div class="modal-content">' +
    '<div class="modal-body">Đang chờ xác nhận chuyển khoản</div></div></div></div>' + reviewMarkup + '</body></html>';

async function waitUntil(check, label) {
    const deadline = Date.now() + 5000;
    while (!check()) {
        assert.ok(Date.now() < deadline, 'Timed out: ' + label);
        await new Promise(resolve => setTimeout(resolve, 20));
    }
}

(async () => {
    fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ ...(process.env.GAO_PLAYWRIGHT_CHROME
        ? { executablePath: process.env.GAO_PLAYWRIGHT_CHROME } : { channel: 'chrome' }), headless: true });
    const errors = [];
    try {
        async function scenario(candidate, { holdConfirmation = false } = {}) {
            const context = await browser.newContext({ viewport: { width: 1366, height: 900 }, reducedMotion: 'reduce' });
            const page = await context.newPage();
            page.setDefaultTimeout(10000);
            page.on('pageerror', error => errors.push(error.message));
            const calls = [];
            let pending = [candidate], releaseConfirm;
            await page.route('https://gao-bank-review.test/**', async route => {
                const request = route.request(), url = new URL(request.url());
                if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
                const endpoint = url.pathname.replace('/admin/pos/bank-notifications', '');
                let result;
                if (endpoint === '/admin-approvals') result = [];
                else if (endpoint === '/pending') result = pending;
                else if (endpoint === '/confirm' || endpoint === '/dismiss') {
                    calls.push({ endpoint, method: request.method(), body: request.postDataJSON() });
                    if (endpoint === '/confirm' && holdConfirmation) await new Promise(resolve => { releaseConfirm = resolve; });
                    pending = [];
                    result = endpoint === '/confirm' ? { receiptId: candidate.receiptId, qrId: candidate.qrId,
                        orderId: candidate.orderId, finalized: false, paidAmount: candidate.amount, remainingAmount: 0 } : { success: true };
                } else return route.fulfill({ status: 404, body: 'Unexpected synthetic endpoint: ' + url.pathname });
                return route.fulfill({ contentType: 'application/json', body: JSON.stringify(result) });
            });
            await page.goto('https://gao-bank-review.test/');
            for (const file of ['vendor/css/core.css', 'Admin/css/pos/pos.css', 'Admin/css/pos/pos-cockpit-v2.css',
                'Admin/css/pos/pos-prime.css', 'Admin/css/pos/pos.bank-notifications.css'])
                await page.addStyleTag({ content: read('GaoApp.Web/wwwroot/' + file) });
            await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/vendor/js/bootstrap.js') });
            await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/Admin/js/pos/pos.bank-transfer-review.js') });
            await page.evaluate(() => {
                window.posState = { business: { currentDraft: { orderId: 1838418, customerName: 'OTHERNAME', customerId: 999 } } };
                window.completed = []; window.manualFallbacks = []; window.reviewShown = 0;
                document.getElementById('bankTransferReviewModal').addEventListener('shown.bs.modal', () => window.reviewShown++);
                window.addEventListener('bank-notification:completed', event => window.completed.push(event.detail));
                window.addEventListener('bank-notification:manual-fallback', event => {
                    window.manualFallbacks.push(event.detail);
                    document.getElementById('fallbackState').textContent = 'Kiểm tra chuyển khoản thủ công';
                });
            });
            await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/Admin/js/pos/pos.bank-notifications.js') });
            return { context, page, calls, releaseConfirmation: () => releaseConfirm?.() };
        }
        async function ready(h) {
            await h.page.locator('#bankTransferReviewModal').waitFor({ state: 'visible' });
            await h.page.waitForFunction(() => !PosBankTransferReview.busy());
            assert.equal(await h.page.locator('#bankReviewSender').isVisible(), true);
            assert.equal(await h.page.locator('#bankReviewAmount').isVisible(), true);
            assert.equal(await h.page.locator('#bankReviewSender').innerText(), 'HO SU QUANG');
            assert.match(await h.page.locator('#bankReviewAmount').innerText(), /170[.,]000\s*đ/);
            assert.match(await h.page.locator('#btnBankReviewConfirm').innerText(), /Đúng khách — Xác nhận/);
            assert.equal(await h.page.locator('#btnBankReviewReject span').innerText(), 'Bỏ qua');
            assert.match(await h.page.locator('.pos-bank-review__footer').innerText(), /Bỏ qua: chuyển sang kiểm tra thủ công/);
            assert.equal(await h.page.locator('#loyaltyCustomerName').innerText(), 'OTHERNAME');
            assert.doesNotMatch(await h.page.locator('#bankTransferReviewModal').innerText(), /OTHERNAME/);
            assert.equal(await h.page.locator('#bankReviewContent').textContent(), sourceContent);
            assert.equal(await h.page.locator('#bankReviewSender > *').count(), 0);
            assert.equal(await h.page.locator('#bankReviewContent > *').count(), 0);
        }

        const correct = await scenario({ ...item }, { holdConfirmation: true });
        await ready(correct);
        await correct.page.screenshot({ path: path.join(output, 'bank-sender-170000-desktop.png') });
        await correct.page.keyboard.down('Enter');
        await waitUntil(() => correct.calls.length === 1, 'cashier confirmation request');
        await correct.page.keyboard.down('Enter'); // Held Enter must not confirm twice.
        await correct.page.keyboard.up('Enter');
        assert.equal(correct.calls.length, 1);
        assert.deepEqual(correct.calls[0], { endpoint: '/confirm', method: 'POST',
            body: { receiptId: 101, waitId: 201, automatic: false } });
        correct.releaseConfirmation();
        await correct.page.waitForFunction(() => completed.length === 1);
        await correct.page.locator('#bankTransferReviewModal').waitFor({ state: 'hidden' });
        await correct.page.evaluate(() => PosBankNotifications.refresh());
        assert.equal(correct.calls.length, 1);
        assert.equal(await correct.page.evaluate(() => manualFallbacks.length), 0);
        await correct.context.close();

        const wrong = await scenario({ ...item, receiptId: 102, waitId: 202 });
        await ready(wrong);
        await wrong.page.keyboard.down('Escape');
        await wrong.page.keyboard.down('Escape');
        await wrong.page.keyboard.up('Escape');
        await wrong.page.waitForFunction(() => manualFallbacks.length === 1);
        await wrong.page.locator('#bankTransferReviewModal').waitFor({ state: 'hidden' });
        assert.deepEqual(wrong.calls, [{ endpoint: '/dismiss', method: 'POST', body: { receiptId: 102, waitId: 202 } }]);
        assert.equal(await wrong.page.evaluate(() => completed.length), 0);
        assert.equal(await wrong.page.evaluate(() => PosBankNotifications.isManual(301)), true);
        assert.match(await wrong.page.locator('#fallbackState').innerText(), /thủ công/);
        await wrong.page.evaluate(() => PosBankNotifications.refresh());
        assert.equal(wrong.calls.length, 1, 'Rejected transfer cannot become a financial confirmation on refresh');
        await wrong.context.close();

        const automatic = await scenario({ ...item, receiptId: 103, waitId: 203, automatic: true,
            content: sourceContent.replace('HO SU QUANG chuyen tien', 'POS-1838418-QR-1838418-TEST') });
        await automatic.page.waitForFunction(() => completed.length === 1);
        assert.deepEqual(automatic.calls, [{ endpoint: '/confirm', method: 'POST',
            body: { receiptId: 103, waitId: 203, automatic: true } }]);
        assert.equal(await automatic.page.evaluate(() => reviewShown), 0);
        assert.equal(await automatic.page.locator('#bankTransferReviewModal').isVisible(), false);
        await automatic.context.close();

        const textOnly = await scenario({ ...item, receiptId: 104, waitId: 204,
            senderName: '<b>HO SU QUANG</b>', content: '<img src=x onerror="window.bankSourceExecuted=true">' });
        await textOnly.page.locator('#bankTransferReviewModal').waitFor({ state: 'visible' });
        await textOnly.page.waitForFunction(() => !PosBankTransferReview.busy());
        assert.equal(await textOnly.page.locator('#bankReviewSender').textContent(), '<b>HO SU QUANG</b>');
        assert.equal(await textOnly.page.locator('#bankReviewSender b').count(), 0);
        assert.equal(await textOnly.page.locator('#bankReviewContent img').count(), 0);
        assert.equal(await textOnly.page.evaluate(() => window.bankSourceExecuted), undefined);
        await textOnly.context.close();

        assert.deepEqual(errors, []);
        console.log('PASS bank transfer review: actual POS modal shows 170.000đ and bank sender HO SU QUANG instead of the loyalty customer; Enter confirms once with automatic:false, Esc dismisses once to manual without financial confirmation, known POS auto-confirms without review, and bank text remains inert textContent.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
