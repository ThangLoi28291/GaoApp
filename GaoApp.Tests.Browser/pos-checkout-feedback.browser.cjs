// Uses the real Razor modal, Bootstrap, feedback module and invoice functions.
// Only HTTP and printing are stubbed; no sale or physical print is performed.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
(async () => {
    const view = read('GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml');
    const start = view.lastIndexOf('<div class="modal fade"', view.indexOf('id="invoiceIntentModal"'));
    const markup = view.slice(start, view.indexOf('<!--', start));
    const app = read('GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js');
    const logic = app.slice(app.indexOf('function clearInvoiceIntentError()'), app.indexOf('        function nowIso()'));
    const bindings = app.slice(app.indexOf('function bindInvoiceIntentEvents()'), app.indexOf('function bindModuleEvents()'));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1366, height: 900 }, reducedMotion: 'reduce' });
        const errors = []; page.on('pageerror', e => errors.push(e.message));
        await page.setContent('<html lang="vi"><body style="background:#eff1f7"><input id="barcode">' + markup + '</body></html>');
        await page.addStyleTag({ content: read('GaoApp.Web/wwwroot/vendor/css/core.css') });
        await page.addStyleTag({ content: read('GaoApp.Web/wwwroot/Admin/css/pos/pos.checkout-feedback.css') });
        await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/vendor/js/bootstrap.js') });
        await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/Admin/js/pos/pos.checkout-feedback.js') });
        await page.addScriptTag({ content: `
            const invoiceIntentModalEl = document.getElementById('invoiceIntentModal');
            const invoiceIntentOrderText = document.getElementById('invoiceIntentOrderText');
            const invoiceIntentErrorBox = document.getElementById('invoiceIntentErrorBox');
            const btnInvoiceIntentAutomatic = document.getElementById('btnInvoiceIntentAutomatic');
            const btnInvoiceIntentManual = document.getElementById('btnInvoiceIntentManual');
            const invoiceIntentModal = new bootstrap.Modal(invoiceIntentModalEl, {backdrop:'static',keyboard:false});
            const posState = {ui:{modals:{}}};
            let invoiceIntentBusy=false, pendingInvoiceIntentOrderId=null, pendingPostPaymentPrintOrderId=null, pendingReceiptPrint=null, pendingAskBeforePrintingReceipt=false;
            const focusBarcodeInput = () => document.getElementById('barcode').focus();
            const showError = message => { throw new Error(message); };
            window.calls=[]; window.prints=[]; window.rejectRoute=false; window.slow=false; window.askPreference=false; window.openCount=0; window.blockPrint=false;
            const postJson=async(url,body)=>{ window.calls.push({url,body}); if(window.slow)await new Promise(r=>window.release=r); if(window.rejectRoute)throw new Error('Lỗi thử nghiệm'); return {orderId:123, askBeforePrintingReceipt: window.askPreference}; };
            window.open=()=>{window.openCount++;return window.blockPrint?null:{closed:false,close(){},location:{replace:url=>window.prints.push(url)}};};
            window.PosPrinting={postPaymentUrl:url=>url+'?postPayment=1'};
            const checkoutFeedback=PosCheckoutFeedback.create({modal:invoiceIntentModalEl,submit:submitInvoiceIntent,choosePrint:chooseReceiptPrint,isBusy:()=>invoiceIntentBusy});
            ${logic}
            ${bindings}
            bindInvoiceIntentEvents();
            window.showCheckout=(cash=true)=>openReceiptPrint(123,'80',true,true,cash?{received:200000,total:158000,change:42000,other:0}:null,window.askPreference);
        ` });
        const show = async cash => { await page.evaluate(cash => window.showCheckout(cash), cash); await page.waitForFunction(() => document.activeElement?.id === 'btnInvoiceIntentAutomatic'); };
        await show(true);
        assert.match(await page.locator('#cashReceiptChange').innerText(), /42[.,]000/);
        const output = path.join(root, 'TestResults/pos-checkout-feedback'); fs.mkdirSync(output, { recursive: true });
        await page.screenshot({ path: path.join(output, 'cash-desktop.png') });
        await page.evaluate(() => window.slow = true);
        await page.keyboard.press('Enter');
        await page.keyboard.press('c'); // In-flight request must not change the selected route or print twice.
        assert.equal(await page.evaluate(() => calls.length), 1);
        assert.equal(await page.evaluate(() => calls[0].body.route), 1);
        await page.evaluate(() => { window.slow=false; window.release(); });
        await page.locator('#invoiceIntentModal').waitFor({ state: 'hidden' });
        assert.equal(await page.evaluate(() => prints.length), 1);
        assert.equal(await page.locator('#btnCashReceiptDone').count(), 0);
        assert.equal(await page.evaluate(() => document.activeElement.id), 'barcode');
        await show(false); assert.equal(await page.locator('#cashReceiptSummary').isVisible(), false);
        await page.keyboard.press('C'); await page.locator('#invoiceIntentModal').waitFor({ state: 'hidden' });
        assert.equal(await page.evaluate(() => calls.at(-1).body.route), 2);
        await show(true); await page.evaluate(() => window.rejectRoute=true);
        await page.keyboard.press('k'); await page.locator('#invoiceIntentErrorBox').waitFor({ state: 'visible' });
        assert.equal(await page.evaluate(() => calls.at(-1).body.route), 1);
        assert.equal(await page.evaluate(() => prints.length), 2);
        assert.equal(await page.locator('#invoiceIntentChoices').isVisible(), true);
        await page.keyboard.press('Escape'); assert.equal(await page.locator('#invoiceIntentModal').isVisible(), true);
        await page.setViewportSize({ width: 390, height: 844 });
        await page.screenshot({ path: path.join(output, 'cash-mobile.png') });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
        await page.evaluate(() => window.rejectRoute=false);
        await page.keyboard.press('K'); await page.locator('#invoiceIntentModal').waitFor({ state: 'hidden' });
        const before=await page.evaluate(() => calls.length);
        await page.keyboard.press('k'); assert.equal(await page.evaluate(() => calls.length), before);
        await page.setViewportSize({width:1366,height:900});
        await page.evaluate(()=>window.askPreference=true);
        const beforePrint=await page.evaluate(()=>prints.length), beforeOpen=await page.evaluate(()=>openCount);
        await show(true); await page.keyboard.press('Enter');
        await page.locator('#receiptPrintChoices').waitFor({state:'visible'});
        await page.waitForFunction(()=>document.activeElement.id==='btnReceiptDoNotPrint');
        assert.equal(await page.evaluate(()=>openCount),beforeOpen, 'Opt-in customer must not open a blank print tab');
        await page.screenshot({path:path.join(output,'receipt-choice.png')});
        await page.keyboard.press('Enter'); await page.locator('#invoiceIntentModal').waitFor({state:'hidden'});
        assert.equal(await page.evaluate(()=>prints.length),beforePrint);
        await show(false); await page.keyboard.press('c'); await page.locator('#receiptPrintChoices').waitFor({state:'visible'});
        const beforeChoiceCalls=await page.evaluate(()=>calls.length);
        await page.keyboard.press('K'); await page.locator('#invoiceIntentModal').waitFor({state:'hidden'});
        assert.equal(await page.evaluate(()=>prints.length),beforePrint); assert.equal(await page.evaluate(()=>calls.length),beforeChoiceCalls);
        await show(true); await page.keyboard.press('k'); await page.locator('#receiptPrintChoices').waitFor({state:'visible'});
        await page.evaluate(()=>window.blockPrint=true); await page.keyboard.press('C'); await page.locator('#invoiceIntentErrorBox').waitFor({state:'visible'});
        assert.equal(await page.locator('#receiptPrintChoices').isVisible(),true);
        await page.evaluate(()=>window.blockPrint=false); await page.locator('#btnReceiptPrint').click(); await page.locator('#invoiceIntentModal').waitFor({state:'hidden'});
        assert.equal(await page.evaluate(()=>prints.length),beforePrint+1);
        await show(false); await page.keyboard.press('Enter'); await page.locator('#receiptPrintChoices').waitFor({state:'visible'});
        await page.locator('#btnReceiptDoNotPrint').click(); await page.locator('#invoiceIntentModal').waitFor({state:'hidden'});
        assert.equal(await page.evaluate(()=>prints.length),beforePrint+1);
        // A following default customer resumes the existing automatic print flow.
        await page.evaluate(()=>window.askPreference=false); await show(false); await page.keyboard.press('Enter');
        await page.locator('#invoiceIntentModal').waitFor({state:'hidden'}); assert.equal(await page.evaluate(()=>prints.length),beforePrint+2);
        assert.deepEqual(errors, []);
        console.log('PASS: cash snapshot, Enter/K/C, busy/error guards, immediate close after invoice choice, transfer-only reset, mobile, focus restoration.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode=1; });
