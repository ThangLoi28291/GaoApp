(function () {
    'use strict';
    const setup = JSON.parse(document.getElementById('receiptPrintData').textContent), frame = document.getElementById('receiptPaper');
    const feedback = document.getElementById('printFeedback'), button = document.getElementById('printReceipt');
    const browserPrintButton = document.getElementById('useBrowserPrint');
    let rendered, first = true, loading = false, printing = false, helperCompleted = false, printClosed = false;
    let printDiagnostic = null;
    let helperPostAttempted = false, browserRecoveryAvailable = false;
    const postPayment = /^#pos-payment-print=[0-9a-f-]{36}$/i.test(window.location.hash);
    const drawerPermission = PosPrinting.consumeCashDrawer(setup.receipt);
    let cashDrawer = setup.autoPrint === true;
    function completePrint(message = '') {
        if (printClosed) return;
        printClosed = true;
        try { PosPrinting.reportPaymentPrint?.(setup.receipt, message); }
        catch { /* Reporting to POS must never prevent closing the receipt. */ }
        finally {
            if (window.GaoPrintLifecycle) window.GaoPrintLifecycle.close(window);
            else window.close();
        }
    }
    function buildQrDataUrl(value) {
    if (!value) return null;

    if (!window.qrcodegen?.QrCode) {
        throw new Error(
            'Chưa tải được bộ tạo QR hóa đơn.'
        );
    }

    const matrix =
        qrcodegen.QrCode.encodeText(
            String(value),
            qrcodegen.QrCode.Ecc.QUARTILE
        );

    const canvas =
        document.createElement('canvas');

    const scale = 6;
    const border = 4;

    canvas.width =
        canvas.height =
            (matrix.size + border * 2) *
            scale;

    const drawing =
        canvas.getContext('2d');

    drawing.fillStyle = '#fff';

    drawing.fillRect(
        0,
        0,
        canvas.width,
        canvas.height
    );

    drawing.fillStyle = '#000';

    for (
        let y = 0;
        y < matrix.size;
        y++
    ) {
        for (
            let x = 0;
            x < matrix.size;
            x++
        ) {
            if (!matrix.getModule(x, y)) {
                continue;
            }

            drawing.fillRect(
                (x + border) * scale,
                (y + border) * scale,
                scale,
                scale
            );
        }
    }

    return canvas.toDataURL(
        'image/png'
    );
}
    async function print() {
        if (loading || printing || helperCompleted) return;
        printing = true; button.disabled = true; feedback.textContent = 'Đang gửi lệnh in…';
        browserRecoveryAvailable = false;
        if (browserPrintButton) browserPrintButton.hidden = true;
        const openDrawer = cashDrawer; cashDrawer = false;
        try {
            const result = await (PosPrinting.sendReceipt || PosPrinting.send)(rendered, setup, frame.contentWindow, completePrint,
                openDrawer && await drawerPermission);
            helperCompleted = result.mode === 'helper';
            feedback.textContent = result.mode === 'helper' ? 'Print Helper đã nhận và ghi lệnh in.' : result.mode === 'qz' ? `Đã gửi lệnh tới ${result.printer}.` : 'Đã mở hộp thoại in.';
            // Helper/QZ already acknowledged completion. Close in this task;
            // do not wait for a timer in a background Chrome print tab.
            if (result.mode === 'helper' || result.mode === 'qz') completePrint();
        }
        catch (error) {
            feedback.textContent = error.message;
            const diagnostic = error.printDiagnostic;
            // Never offer a second transport after an ambiguous print or drawer
            // request, including a later failed health check in this document.
            if (diagnostic?.method === 'POST' || error.receiptPrinted === true) helperPostAttempted = true;
            browserRecoveryAvailable = !helperPostAttempted && diagnostic?.endpoint === '/health' && diagnostic.method === 'GET';
            if (browserPrintButton) browserPrintButton.hidden = !browserRecoveryAvailable;
            if (browserRecoveryAvailable)
                feedback.textContent += '\nChưa gửi lệnh in. Nếu máy này dùng hộp thoại Chrome, bấm “Dùng hộp thoại in Chrome” để lưu cách in cho quầy.';
            if (error.printDiagnostic) {
                printDiagnostic = { version: 1, timeUtc: new Date().toISOString(), browser: navigator.userAgent,
                    orderId: setup.receipt.orderId, storeId: setup.storeId, terminalId: setup.terminalId,
                    automaticReceipt: setup.autoPrint === true, checkoutMarker: postPayment,
                    embedded: window.parent !== window, hasOpener: !!window.opener,
                    transport: error.printDiagnostic };
                const download = document.getElementById('downloadPrintDiagnostic');
                if (download) download.hidden = false;
            }
            if (window.parent !== window) window.parent.dispatchEvent(new CustomEvent('pos:print-error', {
                detail: { orderId: setup.receipt.orderId, message: error.message } }));
            if (postPayment) {
                completePrint(error.message);
            }
        }
        finally { printing = false; button.disabled = helperCompleted; }
    }
    function show(design) {
        const invoiceBuyerQrDataUrl =
    setup.invoiceBuyerSelfServiceUrl
        ? buildQrDataUrl(
            setup.invoiceBuyerSelfServiceUrl
        )
        : null;

rendered =
    ReceiptTemplates.render(
        setup.receipt,
        design,
        {
            invoiceBuyerQrDataUrl,
            invoiceBuyerQrExpiresAtUtc:
                setup.invoiceBuyerSelfServiceExpiresAtUtc
        }
    ); loading = true; button.disabled = true;
        frame.style.width = rendered.size.width + 'mm';
        frame.onload = () => {
            frame.style.height = Math.ceil(frame.contentDocument.body.scrollHeight) + 'px';
            PosPrinting.pageSize(frame.contentDocument, rendered);
            loading = false; button.disabled = helperCompleted || printing;
            const autoPrint = first && setup.autoPrint;
            first = false;
            if (autoPrint) print();
        };
        frame.srcdoc = rendered.html;
    }
    button.addEventListener('click', print);
    window.GaoReceiptPrint = {
        setInvoiceBuyerLink(link) {
            setup.invoiceBuyerSelfServiceUrl = link.url;
            setup.invoiceBuyerSelfServiceExpiresAtUtc = link.expiresAtUtc;
            first = false;
            cashDrawer = false;
            show(PosPrinting.selected(setup, setup.templates));
        }
    };
    browserPrintButton?.addEventListener('click', async () => {
        if (!browserRecoveryAvailable || loading || printing || helperCompleted) return;
        browserRecoveryAvailable = false; browserPrintButton.hidden = true;
        try {
            PosPrinting.savePreferences(setup, { ...PosPrinting.preferences(setup), mode: 'browser' });
            await print();
        } catch (error) { feedback.textContent = error.message; }
    });
    document.getElementById('downloadPrintDiagnostic')?.addEventListener('click', () => {
        if (!printDiagnostic) return;
        const url = URL.createObjectURL(new Blob([JSON.stringify(printDiagnostic, null, 2)], { type: 'application/json' }));
        const link = document.createElement('a'); link.href = url;
        link.download = `gao-print-error-${setup.receipt.orderId}.json`; link.click();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
    document.getElementById('closeReceipt').addEventListener('click', () => GaoPrintLifecycle.close(window));
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') { event.preventDefault(); GaoPrintLifecycle.close(window); }
        else if (event.key === 'Enter' && event.repeat) { event.preventDefault(); event.stopPropagation(); }
    });
    try {
        const selected = PosPrinting.selected(setup, setup.templates), pref = PosPrinting.preferences(setup);
        document.getElementById('printTemplateName').textContent = selected.name;
        feedback.textContent = pref.mode === 'helper' ? 'In qua Linux Print Helper trên máy này' : pref.mode === 'qz' ? `Máy in: ${pref.printer}` : 'In bằng hộp thoại trình duyệt';
        show(selected);
    } catch (error) { button.disabled = true; feedback.textContent = error.message; }
})();
