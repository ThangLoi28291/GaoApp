'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let value = ''; process.stdin.on('data', x => value += x); process.stdin.on('end', () => resolve(value)); }));
    const output = path.resolve('.artifacts/security-plan-a/browser-issue2'); fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const cashier = await context.newPage(), display = await context.newPage(), errors = [], checks = [];
    display.on('pageerror', error => errors.push(error.message));
    try {
        await cashier.goto(info.baseUrl + '/admin/account/login');
        await cashier.locator('[name=UserName]').fill(info.user); await cashier.locator('[name=Password]').fill(info.password);
        if (await cashier.locator('select[name=SelectedTerminalId]').count()) await cashier.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([cashier.waitForURL(url => !url.pathname.endsWith('/login')), cashier.locator('#loginForm button[type=submit]').click()]);
        await cashier.goto(info.baseUrl + '/admin/label-printing');
        const token = await cashier.locator('[name=__RequestVerificationToken]').first().inputValue();
        async function api(url, data = {}) {
            const response = await context.request.post(info.baseUrl + url, { data, headers: { RequestVerificationToken: token } });
            assert.equal(response.ok(), true, url + ': ' + await response.text()); return response.json();
        }
        const order = (await api('/admin/pos/draft')).orderId;
        await api(`/admin/pos/${order}/items?variantId=${info.variantId}&qty=3`);
        await display.goto(info.baseUrl + '/admin/pos/customer-display');
        await display.waitForFunction(() => !!window.signalR && document.querySelector('.cd-status').textContent.includes('Sẵn sàng'));
        await display.evaluate(async info => {
            window.paymentProbe = new signalR.HubConnectionBuilder().withUrl('/hubs/pos').build();
            await window.paymentProbe.start(); await window.paymentProbe.invoke('JoinStoreGroup', info.storeId, String(info.terminalId));
        }, info);
        const send = (eventType, payload = {}) => display.evaluate(({ eventType, payload }) => window.paymentProbe.invoke('BroadcastTerminalEvent', { eventType, payload }), { eventType, payload });
        for (const eventType of ['customer_payment_success', 'customer_display_reset']) {
            await assert.rejects(() => send(eventType, { finalized: true, paidAmount: 999999 }));
        }
        assert.equal(await display.locator('#cdSuccessOverlay').isVisible(), false); checks.push('forged-success-rejected');
        const qr = await api('/admin/pos/cart/current/payment-qr', { amount: 10 });
        await send('customer_payment_qr_created', { orderId: order, qrId: qr.id, qrDataUrl: 'https://attacker.invalid/qr', accountNumber: 'ATTACKER' });
        await display.waitForFunction(qr => document.getElementById('cdQrImage').src === qr.qrDataUrl && !document.getElementById('cdPaymentOverlay').classList.contains('d-none'), qr);
        assert.doesNotMatch(await display.locator('#cdPaymentMessage').innerText(), /ATTACKER/);
        await display.screenshot({ path: path.join(output, 'server-qr.png') }); checks.push('stored-qr-and-recipient');
        // Close/reopen the page without resending a browser QR payload.
        await display.reload();
        await display.waitForFunction(qr => document.getElementById('cdQrImage').src === qr.qrDataUrl, qr); checks.push('qr-recovered-after-reload');
        await api(`/admin/pos/payment-qr/${qr.id}/manual-confirm`);
        await display.locator('#cdSuccessOverlay.is-info').waitFor();
        assert.match(await display.locator('#cdSuccessMessage').innerText(), /còn lại/);
        assert.match(await display.locator('#cdSuccessMessage').innerText(), /thủ công/);
        assert.equal(await display.locator('#cdQrImage').getAttribute('src'), ''); checks.push('partial-receipt-and-manual-source');
        await display.screenshot({ path: path.join(output, 'partial-payment.png') });
        // Confirm that loss of the state API cannot leave a payable QR or a success overlay visible.
        await display.route('**/admin/pos/customer-display/payment-state', route => route.fulfill({ status: 503, body: '{}' }));
        await display.waitForFunction(() => document.querySelector('.cd-status').textContent.includes('Mất kết nối'), { timeout: 10000 });
        assert.equal(await display.locator('#cdSuccessOverlay').isVisible(), false);
        assert.equal(await display.locator('#cdPaymentOverlay').isVisible(), false); checks.push('disconnect-clears-financial-overlays');
        await display.unroute('**/admin/pos/customer-display/payment-state');
        const next = await api('/admin/pos/cart/current/payment-qr', { amount: 10 });
        await display.evaluate(async info => {
            window.paymentProbe = new signalR.HubConnectionBuilder().withUrl('/hubs/pos').build();
            await window.paymentProbe.start(); await window.paymentProbe.invoke('JoinStoreGroup', info.storeId, String(info.terminalId));
        }, info);
        await send('customer_payment_qr_created', { orderId: order, qrId: next.id });
        await display.waitForFunction(qr => document.getElementById('cdQrImage').src === qr.qrDataUrl, next); checks.push('reconnect-loads-server-qr');
        await api(`/admin/pos/payment-qr/${next.id}/cancel`);
        await display.waitForFunction(() => document.getElementById('cdQrImage').getAttribute('src') === ''); checks.push('cancelled-qr-cleared');
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(output, 'checks.json'), JSON.stringify({ passed: checks, errors }, null, 2));
        console.log('PASS customer payment display browser: ' + checks.join(', '));
    } catch (error) {
        await display.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {});
        const debug = await display.evaluate(async baseUrl => ({ status: document.querySelector('.cd-status')?.textContent,
            scripts: [...document.scripts].map(s => s.src).filter(s => s.includes('customer-display')),
            qrPrefix: document.getElementById('cdQrImage')?.getAttribute('src')?.slice(0, 60),
            paymentClass: document.getElementById('cdPaymentOverlay')?.className,
            module: typeof window.CustomerDisplayPayment, dataset: { ...document.getElementById('customerDisplayApp')?.dataset },
            browserState: await (async () => {
                const res = await fetch(new URL('/admin/pos/customer-display/payment-state', baseUrl).href, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
                const state = await res.json();
                return { status: res.status, redirected: res.redirected, mode: state.mode, storeId: state.storeId,
                    terminalId: state.terminalId, validMs: Date.parse(state.validUntilUtc) - Date.now(),
                    safeImage: /^data:image\/(png|jpeg|webp);base64,[A-Za-z0-9+/=\r\n]+$/.test(state.payload?.qrDataUrl) };
            })() }), info.baseUrl).catch(diagnosticError => ({ diagnosticError: diagnosticError.message }));
        const response = await context.request.get(info.baseUrl + '/admin/pos/customer-display/payment-state');
        const state = await response.json();
        const diagnostic = { ...debug, errors, apiStatus: response.status(), mode: state.mode, orderId: state.orderId,
            key: state.key, apiQrPrefix: state.payload?.qrDataUrl?.slice(0, 60) };
        fs.writeFileSync(path.join(output, 'failure.json'), JSON.stringify(diagnostic, null, 2));
        console.error(diagnostic);
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
