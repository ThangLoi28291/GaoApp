(function () {
    'use strict';
    const setup = JSON.parse(document.getElementById('receiptPrintData').textContent), frame = document.getElementById('receiptPaper');
    const feedback = document.getElementById('printFeedback'), button = document.getElementById('printReceipt');
    let rendered, first = true, loading = false, printing = false, helperCompleted = false;
    const drawerPermission = PosPrinting.consumeCashDrawer(setup.receipt);
    let cashDrawer = setup.autoPrint === true;
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
        const openDrawer = cashDrawer; cashDrawer = false;
        try { const result = await PosPrinting.send(rendered, setup, frame.contentWindow, () => GaoPrintLifecycle.close(window), openDrawer && await drawerPermission); helperCompleted = result.mode === 'helper'; feedback.textContent = result.mode === 'helper' ? 'Print Helper đã nhận và ghi lệnh in.' : result.mode === 'qz' ? `Đã gửi lệnh tới ${result.printer}.` : 'Đã mở hộp thoại in.'; }
        catch (error) {
            feedback.textContent = error.message;
            if (window.parent !== window) window.parent.dispatchEvent(new CustomEvent('pos:print-error', {
                detail: { orderId: setup.receipt.orderId, message: error.message } }));
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
    document.getElementById('closeReceipt').addEventListener('click', () => window.close());
    try {
        const selected = PosPrinting.selected(setup, setup.templates), pref = PosPrinting.preferences(setup);
        document.getElementById('printTemplateName').textContent = selected.name;
        feedback.textContent = pref.mode === 'helper' ? 'In qua Linux Print Helper trên máy này' : pref.mode === 'qz' ? `Máy in: ${pref.printer}` : 'In bằng hộp thoại trình duyệt';
        show(selected);
    } catch (error) { button.disabled = true; feedback.textContent = error.message; }
})();
