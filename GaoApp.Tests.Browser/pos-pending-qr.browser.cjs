// Real POS markup, Bootstrap, HTTP error parser and action pipeline; synthetic payments only.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
(async () => {
    const view = read('GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml');
    const start = view.indexOf('<div class="modal fade pos-payment-workspace-modal"');
    const end = view.lastIndexOf('<div class="modal fade"', view.indexOf('id="confirmModal"'));
    const markup = view.slice(start, end).replace(/@if\s*\([^\n]*\)\s*\{/g, '').replace(/^\s*}\s*$/gm, '');
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1366, height: 900 }, reducedMotion: 'reduce' });
        const errors = []; page.on('pageerror', error => errors.push(error.message));
        const qrs = []; let pending = null, paidTotal = 0, createAttempts = 0, confirmCalls = 0, cancelCalls = 0;
        const saved = qr => ({ qr, status: qr.status === 4 ? 'Cancelled' : qr.status === 5 ? 'ManualConfirmed' : 'Pending',
            canCancel: qr.status === 0, readOnly: qr.status !== 0, message: 'QR đã lưu.' });
        await page.route('https://gao-qr.test/**', async route => {
            const request = route.request(), url = new URL(request.url());
            let body = {}, status = 200;
            if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body:
                '<!doctype html><html lang="vi"><head><meta charset="utf-8"></head><body class="pos-prime-layout">'+markup+'</body></html>' });
            if (url.pathname.endsWith('/cart/current/payment-qr')) {
                createAttempts++;
                const input = request.postDataJSON();
                if (pending) { status = 409; body = { errorCode: 'POS_QR_PENDING', errorType: 'business',
                    message: `QR ${pending.requestCode} của đơn này chưa được xử lý. Xác nhận đã nhận tiền hoặc hủy QR trước khi tạo QR mới.`,
                    metadata: { orderId: 15, qrId: pending.id, savedQr: saved(pending) } }; }
                else {
                    pending = { id: qrs.length + 1, orderId: 15, bankAccountId: 1, amount: input.amount,
                        requestCode: `QR-${qrs.length + 1}`, status: 0, content: 'TEST PAYMENT', bankName: 'Ngân hàng thử nghiệm',
                        accountNumber: '123456789', accountName: 'POS TEST', createdAtUtc: new Date().toISOString(),
                        qrDataUrl: 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1kAAAAASUVORK5CYII=' };
                    qrs.push(pending); body = pending;
                }
            } else if (url.pathname.endsWith('/manual-confirm')) {
                confirmCalls++; paidTotal += pending.amount; pending.status = 5;
                body = { qrId: pending.id, orderId: 15, finalized: false, paidAmount: pending.amount, remainingAmount: 100000 - paidTotal };
                pending = null;
            } else if (url.pathname.endsWith('/cancel')) {
                cancelCalls++; pending.status = 4; pending = null; body = { success: true };
            } else if (url.pathname.endsWith('/qrs')) {
                body = { orderId: 15, latestQrId: pending?.id || qrs.at(-1)?.id || null,
                    items: qrs.slice().reverse().map(qr => ({ ...qr, qrId: qr.id, status: saved(qr).status, canReopen: true })) };
            } else if (/\/qrs\/\d+$/.test(url.pathname)) body = saved(qrs.find(qr => qr.id === Number(url.pathname.split('/').at(-1))));
            else if (url.pathname === '/draft') body = { orderId: 15, status: 0, grandTotal: 100000, subtotal: 100000,
                paidTotal, balanceDue: 100000 - paidTotal, payments: [] };
            else return route.fulfill({ status: 404, body: '' });
            return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
        });
        await page.goto('https://gao-qr.test/');
        for (const file of ['vendor/css/core.css','Admin/css/pos/pos.css','Admin/css/pos/pos-cockpit-v2.css',
            'Admin/css/pos/pos-prime.css','Admin/css/pos/pos.qr-history.css'])
            await page.addStyleTag({ content: read('GaoApp.Web/wwwroot/'+file) });
        for (const file of ['vendor/js/bootstrap.js','Admin/js/pos/pos.state.js','Admin/js/pos/pos.common.js',
            'Admin/js/pos/pos.error.js','Admin/js/pos/pos.render.js','Admin/js/pos/pos.qr-history.js','Admin/js/pos/pos.payment.js'])
            await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/'+file) });
        await page.evaluate(async () => {
            const noop = () => {};
            window.posState = PosState.create(); posState.business.currentDraft = await (await fetch('/draft')).json();
            const modalEl = document.getElementById('paymentModal');
            window.payment = PosPayment.create({ posState,
                elements: Object.fromEntries(['payMethod','payAmount','payReference','payProvider','btnAddPayment','btnFinalizeFromPaymentModal','btnPayExact']
                    .map(id => [id,document.getElementById(id)]).concat([['paymentModalEl',modalEl]])),
                modals: { paymentModal: new bootstrap.Modal(modalEl) }, helpers: {
                    postJson: PosCommon.postJson, runPosAction: PosCommon.runPosAction,
                    renderPaymentModalDraft: PosRender.renderPaymentModalDraft, renderPaymentPreview: PosRender.renderPaymentPreview,
                    requestScreenRefresh: async () => { posState.business.currentDraft = await (await fetch('/draft')).json(); },
                    showSuccess: noop, showError: noop, renderPayments: noop, syncDraftToUi: noop, focusBarcodeInput: noop,
                    openReceiptPrint: noop, applyDraftActionSuccess: noop, applyScreenActionSuccess: noop
                } });
            payment.bindEvents(); payment.openPaymentModal();
        });
        const readyQr = async () => {
            await page.locator('#paymentQrModal').waitFor({ state: 'visible' });
            await page.waitForFunction(() => bootstrap.Modal.getInstance(document.getElementById('paymentQrModal'))?._isTransitioning === false);
        };
        await page.locator('#paymentModal').waitFor({ state: 'visible' });
        await page.locator('[data-pay-method-value="1"]').click();
        await page.locator('#payAmount').fill('20000');
        await page.locator('#btnCreatePaymentQr').click();
        await readyQr();
        assert.equal(qrs.length, 1);
        for (let i = 0; i < 7; i++) {
            await page.keyboard.press('Escape');
            await page.locator('#paymentQrModal').waitFor({ state: 'hidden' });
            assert.equal(await page.locator('#paymentModal').isVisible(), true, 'Escape closes only the QR popup');
            await page.locator('#payAmount').fill('50000');
            await page.locator('#btnCreatePaymentQr').click();
            await page.locator('#paymentQrPendingWarning').waitFor({ state: 'visible' });
            await readyQr();
            assert.equal(await page.locator('#paymentQrAmount').innerText(), '20.000');
            assert.equal(await page.locator('#payAmount').inputValue(), '50.000');
        }
        assert.equal(qrs.length, 1); assert.equal(createAttempts, 8);
        assert.equal(confirmCalls, 0); assert.equal(cancelCalls, 0);
        assert.match(await page.locator('#paymentQrPendingWarning').innerText(), /Xác nhận đã nhận tiền hoặc hủy/);
        const output = path.join(root,'TestResults/pos-pending-qr'); fs.mkdirSync(output, { recursive: true });
        await page.screenshot({ path: path.join(output,'pending-warning-desktop.png'), animations: 'disabled' });
        await page.locator('#btnConfirmPaymentQrPaid').click();
        await page.locator('#paymentQrModal').waitFor({ state: 'hidden' });
        await page.waitForFunction(() => document.getElementById('payAmount').value === '80.000');
        assert.equal(confirmCalls, 1);
        await page.locator('#btnCreatePaymentQr').click();
        await readyQr();
        assert.equal(qrs.length, 2); assert.equal(qrs[1].amount, 80000);
        assert.equal(await page.locator('#paymentQrPendingWarning').isVisible(), false);
        await page.locator('#btnCancelPaymentQr').click();
        await page.locator('#paymentQrModal').waitFor({ state: 'hidden' });
        await page.locator('#btnCreatePaymentQr').click();
        await readyQr();
        assert.equal(qrs.length, 3); assert.equal(cancelCalls, 1);
        await page.keyboard.press('Escape');
        await page.locator('#paymentQrModal').waitFor({ state: 'hidden' });
        await page.locator('#btnCreatePaymentQr').click();
        await page.locator('#paymentQrPendingWarning').waitFor({ state: 'visible' });
        await page.setViewportSize({ width: 390, height: 844 });
        await page.screenshot({ path: path.join(output,'pending-warning-mobile.png'), animations: 'disabled' });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
        assert.deepEqual(errors, []);
        console.log('PASS: repeated creation reopens one saved QR; confirmation and cancellation release creation; desktop/mobile warnings visible.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
