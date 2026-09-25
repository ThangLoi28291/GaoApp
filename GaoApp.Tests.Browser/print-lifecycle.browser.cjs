'use strict';
// Uses the real receipt view and printing scripts. Printer completion is simulated;
// no physical print job, store database or customer order is touched.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const web = path.resolve(__dirname, '../GaoApp.Web');
const scripts = ['receipt.templates', 'print.lifecycle', 'pos.printing']
    .map(name => `<script src="/Admin/js/printing/${name}.js"></script>`).join('');
const receipt = { orderId: 123, orderNumber: 'TEST-PRINT-123', storeName: 'Cửa hàng thử nghiệm',
    lines: [{ itemName: 'Sữa thử nghiệm', quantity: 1, unitPrice: 6000, lineTotal: 6000 }],
    subtotal: 6000, grandTotal: 6000, paidTotal: 6000, payments: [] };
const view = fs.readFileSync(path.join(web, 'Areas/Admin/Views/ReceiptTemplates/Print.cshtml'), 'utf8');
const errors = [], jobs = [];
const server = http.createServer((req, res) => {
    const url = new URL(req.url, 'http://localhost');
    if (url.pathname.startsWith('/Admin/js/printing/')) {
        const name = path.basename(url.pathname);
        res.setHeader('Content-Type', 'text/javascript');
        res.end(fs.readFileSync(path.join(web, 'wwwroot/Admin/js/printing', name))); return;
    }
    res.setHeader('Content-Type', 'text/html; charset=utf-8');
    if (url.pathname === '/receipt') {
        const setup = { receipt, templates: [], storeId: 123, terminalId: 456, autoPrint: url.searchParams.get('auto') === '1' };
        res.end(view.slice(view.indexOf('<!doctype'))
            .replace('@Model.Receipt.OrderNumber', receipt.orderNumber)
            .replace('@Html.Raw(JsonSerializer.Serialize(Model, ReceiptTemplateService.Json))', JSON.stringify(setup))
            .replaceAll('src="~/', 'src="/')); return;
    }
    if (url.pathname === '/simple') {
        res.end('<!doctype html><title>Bill</title><p>Bill</p><script src="/Admin/js/printing/print.lifecycle.js" data-auto-close></script>'); return;
    }
    res.end(`<!doctype html><title>POS giữ nguyên</title><input id="cartNote" value="Đơn đang bán" />${scripts}`);
});

(async () => {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const base = `http://127.0.0.1:${server.address().port}`;
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext();
    await context.exposeFunction('recordPrintJob', job => jobs.push(job));
    await context.addInitScript(() => {
        window.print = () => { window.__printStarted = true; window.dispatchEvent(new Event('beforeprint')); };
    });
    context.on('page', p => p.on('pageerror', e => errors.push(e.message)));
    const parent = await context.newPage(); parent.setDefaultTimeout(10000);
    await parent.goto(base);
    async function popup(url, noopener = false) {
        const ready = context.waitForEvent('page');
        await parent.evaluate(({ url, noopener }) => window.open(url, '_blank', noopener ? 'noopener,noreferrer' : ''), { url: base + url, noopener });
        const page = await ready; page.setDefaultTimeout(10000);
        await page.waitForLoadState(); return page;
    }
    async function finish(page, frame) {
        const closed = page.waitForEvent('close');
        await frame.locator('html').evaluate(html => html.ownerDocument.defaultView.dispatchEvent(new Event('afterprint')));
        await closed;
        assert.equal(parent.isClosed(), false);
        assert.equal(await parent.locator('#cartNote').inputValue(), 'Đơn đang bán');
    }
    async function mockQz(page, fail = false) {
        await page.evaluate(fail => {
            localStorage.setItem('gao-pos-print-v1:123:456', JSON.stringify({ mode: 'qz', printer: 'TEST', copies: 1 }));
            window.qz = { websocket: { isActive: () => true }, printers: { find: async () => ['TEST'] },
                configs: { create: (printer, options) => ({ printer, options }) },
                print: async (config, data) => { if (fail) throw new Error('Máy in thử nghiệm chưa sẵn sàng'); await recordPrintJob({ config, data }); } };
        }, fail);
    }
    try {
        // A noopener tab still closes its outer page when the inner paper finishes.
        let page = await popup('/receipt?auto=1', true);
        let paper = await page.locator('#receiptPaper').contentFrame();
        await paper.locator('.receipt').waitFor();
        await page.waitForFunction(() => document.querySelector('#receiptPaper').contentWindow.__printStarted);
        assert.equal(page.isClosed(), false, 'A non-blocking print() return must not close the dialog/tab');
        await finish(page, paper);

        // Preview remains open until the cashier actually prints (including Cancel).
        page = await popup('/receipt?auto=0');
        paper = page.frameLocator('#receiptPaper');
        await paper.locator('.receipt').waitFor();
        assert.equal(await page.evaluate(() => !!document.querySelector('#receiptPaper').contentWindow.__printStarted), false);
        await page.locator('#printReceipt').click();
        await finish(page, await page.locator('#receiptPaper').contentFrame());

        // QZ errors remain visible and retryable; a successful retry closes once.
        page = await popup('/receipt?auto=0');
        await page.frameLocator('#receiptPaper').locator('.receipt').waitFor();
        await mockQz(page, true);
        await page.locator('#printReceipt').click();
        await page.locator('#printFeedback').filter({ hasText: 'chưa sẵn sàng' }).waitFor();
        assert.equal(page.isClosed(), false);
        assert.equal(await page.locator('#printReceipt').isEnabled(), true);
        await mockQz(page);
        let closed = page.waitForEvent('close');
        await page.locator('#printReceipt').click(); await closed;
        assert.equal(jobs.length, 1);

        // Offline/template-test popups use the same lifecycle without closing POS.
        await parent.evaluate(() => localStorage.removeItem('gao-pos-print-v1:123:456'));
        let ready = context.waitForEvent('page');
        await parent.evaluate(receipt => { PosPrinting.openLocal(receipt, { storeId: 123, terminalId: 456 }, [], true); }, receipt);
        page = await ready;
        await page.waitForFunction(() => window.__printStarted);
        await finish(page, page);
        await mockQz(parent);
        ready = context.waitForEvent('page');
        await parent.evaluate(receipt => { PosPrinting.openLocal(receipt, { storeId: 123, terminalId: 456 }, [], true); }, receipt);
        page = await ready;
        if (!page.isClosed()) await page.waitForEvent('close');
        assert.equal(jobs.length, 2);
        assert.equal(parent.isClosed(), false);

        // Embedded ACB bill cleanup removes only its temporary print frame.
        await parent.evaluate(() => {
            localStorage.removeItem('gao-pos-print-v1:123:456');
            const frame = document.createElement('iframe'); frame.id = 'acbPrint'; frame.src = '/receipt?auto=1'; document.body.appendChild(frame);
        });
        const bill = parent.frameLocator('#acbPrint');
        await bill.frameLocator('#receiptPaper').locator('.receipt').waitFor();
        await parent.waitForFunction(() => document.querySelector('#acbPrint').contentDocument.querySelector('#receiptPaper').contentWindow.__printStarted);
        await bill.locator('#receiptPaper').evaluate(frame => frame.contentWindow.dispatchEvent(new Event('afterprint')));
        await parent.locator('#acbPrint').waitFor({ state: 'detached' });
        assert.equal(await parent.locator('#cartNote').inputValue(), 'Đơn đang bán');

        // Fallback browsers may signal leaving print media without afterprint.
        page = await popup('/simple');
        await finish(page, page);
        page = await popup('/simple');
        await page.evaluate(() => {
            const listeners = [];
            const fakeWindow = { addEventListener() {}, removeEventListener() {},
                matchMedia: () => ({ addEventListener: (_, fn) => listeners.push(fn), removeEventListener() {} }) };
            GaoPrintLifecycle.watch(fakeWindow, () => GaoPrintLifecycle.close(window));
            window.__media = matches => listeners.forEach(fn => fn({ matches }));
            __media(false); // Initial screen mode is not a completed print.
        });
        assert.equal(page.isClosed(), false);
        closed = page.waitForEvent('close');
        await page.evaluate(() => { __media(true); __media(false); }); await closed;
        assert.deepEqual(errors, []);
        console.log('PASS print lifecycle: browser completion/cancel, noopener/inner frame, manual preview, QZ success/error/retry, offline popup, embedded cleanup and print-media fallback; POS stays open.');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode = 1; server.close(); });
