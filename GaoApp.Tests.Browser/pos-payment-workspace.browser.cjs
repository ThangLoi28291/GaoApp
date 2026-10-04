'use strict';
// Real payment markup, styles and owners; synthetic draft/APIs, no sale or bank request.
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
        const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, reducedMotion: 'reduce' });
        const errors = []; page.on('pageerror', error => errors.push(error.message));
        await page.route('https://gao-payment.test/**', route => route.fulfill({ contentType: 'text/html', body:
            '<!doctype html><html lang="vi" data-bs-theme="light" data-skin="default"><head><meta charset="utf-8">'+
            '<meta name="viewport" content="width=device-width, initial-scale=1"></head><body class="pos-prime-layout" data-prime-ui="ready">'+markup+'</body></html>' }));
        await page.goto('https://gao-payment.test/');
        for (const file of ['vendor/css/core.css','vendor/fonts/iconify-icons.css','Admin/css/pos/pos.css',
            'Admin/css/pos/pos-cockpit-v2.css','Admin/css/pos/pos-prime.css','Admin/css/pos/pos.qr-history.css']) {
            await page.addStyleTag({ content: read('GaoApp.Web/wwwroot/'+file) });
        }
        for (const file of ['vendor/js/bootstrap.js','Admin/js/pos/pos.common.js','Admin/js/pos/pos.render.js','Admin/js/pos/pos.payment.js']) {
            await page.addScriptTag({ content: read('GaoApp.Web/wwwroot/'+file) });
        }
        await page.evaluate(() => {
            const noop=()=>{};
            PosCommon.registerUiLock=noop;PosCommon.refreshUiLocks=noop;
            window.calls=[];window.notices=[];
            window.posState={business:{currentDraft:{orderId:15,status:0,subtotal:215000,grandTotal:215000,paidTotal:0,balanceDue:215000,payments:[]}},ui:{}};
            const modalEl=document.getElementById('paymentModal');
            window.payment=PosPayment.create({posState,
                elements:Object.fromEntries(['payMethod','payAmount','payReference','payProvider','btnAddPayment','btnFinalizeFromPaymentModal','btnPayExact'].map(id=>[id,document.getElementById(id)]).concat([['paymentModalEl',modalEl]])),
                modals:{paymentModal:new bootstrap.Modal(modalEl)},helpers:{
                    renderPaymentModalDraft:PosRender.renderPaymentModalDraft,renderPaymentPreview:PosRender.renderPaymentPreview,
                    postJson:async(url,body)=>{calls.push({url,body});return url.includes('/acb/')
                        ? {id:1,orderId:15,amount:body.amount,requestCode:'QR-TEST',qrDataUrl:''}
                        : {...posState.business.currentDraft,paidTotal:body.amount,balanceDue:215000-body.amount};},
                    runPosAction:async(_,key,action,options)=>{const result=await action();await options.onSuccess?.(result);options.onFinally?.();return result;},
                    applyDraftActionSuccess:({draft,afterSync})=>{posState.business.currentDraft=draft;afterSync?.(draft);},
                    requestScreenRefresh:async()=>{},showSuccess:noop,showError:message=>notices.push(message),
                    renderPayments:noop,syncDraftToUi:noop,focusBarcodeInput:noop,openReceiptPrint:noop
                }});
            payment.bindEvents();payment.openPaymentModal();
        });
        const input=page.locator('#payAmount');
        await page.waitForFunction(()=>document.activeElement?.id==='payAmount');
        assert.equal(await input.inputValue(),'215.000');
        const output=path.join(root,'TestResults/pos-payment-workspace');fs.mkdirSync(output,{recursive:true});
        for (const presentation of ['prime','legacy']) {
            await page.evaluate(prime=>document.body.classList.toggle('pos-prime-layout',prime),presentation==='prime');
            for (const viewport of [{width:1920,height:1080},{width:1776,height:1000},{width:1366,height:900}]) {
                await page.setViewportSize(viewport);
                const box=await page.locator('.pos-payment-workspace-dialog').boundingBox();
                assert.ok(Math.abs(box.width-Math.max(520,viewport.width/3))<2,JSON.stringify({presentation,viewport,box}));
                const amountBox=await input.boundingBox();assert.ok(amountBox.height>=68);
                assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
                if(presentation==='prime' && viewport.width===1776) {
                    await page.screenshot({path:path.join(output,'checkout-desktop.png'),animations:'disabled'});
                    await page.locator('.pos-payment-workspace').screenshot({path:path.join(output,'checkout-panel.png'),animations:'disabled'});
                }
            }
        }
        await input.fill('');await input.pressSequentially('30000000');
        assert.equal(await input.inputValue(),'30.000.000');
        assert.equal(await page.locator('#payPreviewChange').innerText(),'29.785.000');
        await input.press('Backspace');assert.equal(await input.inputValue(),'3.000.000');
        await input.fill('1234');await input.evaluate(el=>el.setSelectionRange(2,2));
        await input.press('Backspace');assert.equal(await input.inputValue(),'234');
        await input.fill('215000');await input.evaluate(el=>el.setSelectionRange(1,1));await input.press('9');
        assert.equal(await input.inputValue(),'2.915.000');assert.equal(await input.evaluate(el=>el.selectionStart),3);
        await input.evaluate(el=>{el.select();const clipboardData=new DataTransfer();clipboardData.setData('text','250,000');
            el.dispatchEvent(new ClipboardEvent('paste',{clipboardData,bubbles:true,cancelable:true}));});
        assert.equal(await input.inputValue(),'250.000');assert.equal(await page.locator('#payPreviewChange').innerText(),'35.000');
        await page.evaluate(()=>payment.setPayAmountExact({focus:false}));assert.equal(await input.inputValue(),'215.000');
        await page.evaluate(()=>payment.setPayAmountQuick(300000));assert.equal(await input.inputValue(),'300.000');
        await page.evaluate(()=>payment.increasePayAmount(10000));assert.equal(await input.inputValue(),'310.000');
        await input.fill('');await input.pressSequentially('20.5');
        assert.equal(await input.inputValue(),'20,5');
        await page.evaluate(()=>payment.addPayment());assert.equal(await page.evaluate(()=>calls.length),0,'Fractional VND cannot be collected');
        await input.fill('100000');await page.evaluate(()=>payment.addPayment());
        assert.equal(await page.evaluate(()=>calls[0].body.amount),100000);
        await page.evaluate(()=>{
            posState.business.currentDraft={orderId:15,status:0,subtotal:215000,grandTotal:215000,paidTotal:0,balanceDue:215000,payments:[]};
            payment.openPaymentModal();
        });
        await page.locator('[data-pay-method-value="1"]').click();await input.fill('250000');
        assert.equal(await input.inputValue(),'250.000');
        assert.equal(await page.locator('#payPreviewChange').innerText(),'35.000');
        assert.match(await page.locator('#payPreviewStateText').innerText(),/Chuyển khoản dư 35\.000 đ\. Vẫn ghi nhận đủ 250\.000 đ\./);
        assert.match(await page.locator('#payPreviewBox').getAttribute('class'),/pay-preview-box--warning/);
        await page.waitForFunction(()=>getComputedStyle(document.getElementById('payPreviewBox')).backgroundColor==='rgb(255, 251, 235)');
        assert.equal(await page.locator('#payPreviewBox').evaluate(el=>getComputedStyle(el).backgroundColor),'rgb(255, 251, 235)');
        await page.locator('[data-pay-method-value="0"]').click();
        assert.doesNotMatch(await page.locator('#payPreviewBox').getAttribute('class'),/pay-preview-box--warning/);
        await page.locator('[data-pay-method-value="1"]').click();
        await input.fill('250000');
        await page.waitForFunction(()=>getComputedStyle(document.getElementById('payPreviewBox')).backgroundColor==='rgb(255, 251, 235)');
        await page.locator('.pos-payment-workspace').screenshot({path:path.join(output,'checkout-transfer-overpay.png'),animations:'disabled'});
        await page.evaluate(()=>payment.createPaymentQr());
        assert.equal(await page.evaluate(()=>calls.at(-1).body.amount),250000);
        await page.locator('#paymentQrModal').waitFor({state:'visible'});
        await page.locator('#paymentQrModal .modal-dialog').evaluate(async el=>{
            await Promise.all(el.getAnimations().map(animation=>animation.finished));
        });
        await page.evaluate(()=>bootstrap.Modal.getInstance(document.getElementById('paymentQrModal'))?.hide());
        await page.locator('#paymentQrModal').waitFor({state:'hidden'});
        await page.evaluate(()=>{document.body.classList.add('pos-prime-layout');
            posState.business.currentDraft={orderId:15,status:0,subtotal:215000,grandTotal:215000,paidTotal:0,balanceDue:215000,payments:[]};
            payment.openPaymentModal();});
        await page.setViewportSize({width:390,height:844});
        await page.locator('#paymentModal').waitFor({state:'visible'});
        await page.locator('.pos-payment-workspace__body').evaluate(el=>el.scrollTop=0);
        await page.screenshot({path:path.join(output,'checkout-mobile.png'),animations:'disabled'});
        assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
        const footer=await page.locator('.pos-payment-workspace__footer').boundingBox();assert.ok(footer.y+footer.height<=844);
        assert.deepEqual(errors,[]);
        console.log('PASS: desktop/mobile sizing, amount formatting, transfer surplus warning and full QR amount, cash/QR payloads and fractional VND rejection.');
    } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
