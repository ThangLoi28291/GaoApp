(function () {
    'use strict';
    const setup = JSON.parse(document.getElementById('receiptPrintData').textContent), frame = document.getElementById('receiptPaper');
    const select = document.getElementById('printTemplateChoice'), feedback = document.getElementById('printFeedback'), button = document.getElementById('printReceipt');
    let rendered, first = true, loading = false, printing = false;
    async function print() {
        if (loading || printing) return;
        printing = true; button.disabled = true; feedback.textContent = 'Đang gửi lệnh in…';
        try { const result = await PosPrinting.send(rendered, setup, frame.contentWindow, () => GaoPrintLifecycle.close(window)); feedback.textContent = result.mode === 'qz' ? `Đã gửi lệnh tới ${result.printer}.` : 'Đã mở hộp thoại in.'; }
        catch (error) {
            feedback.textContent = error.message;
            if (window.parent !== window) window.parent.dispatchEvent(new CustomEvent('pos:print-error', {
                detail: { orderId: setup.receipt.orderId, message: error.message } }));
        }
        finally { printing = false; button.disabled = false; }
    }
    function show(design) {
        rendered = ReceiptTemplates.render(setup.receipt, design); loading = true; button.disabled = true;
        frame.style.width = rendered.size.width + 'mm';
        frame.onload = () => {
            frame.style.height = Math.ceil(frame.contentDocument.body.scrollHeight) + 'px';
            PosPrinting.pageSize(frame.contentDocument, rendered);
            loading = false; button.disabled = false;
            if (first && setup.autoPrint) print();
            first = false;
        };
        frame.srcdoc = rendered.html;
    }
    button.addEventListener('click', print);
    document.getElementById('closeReceipt').addEventListener('click', () => window.close());
    select.addEventListener('change', () => {
        const choice = setup.templates.find(x => x.key === select.value);
        if (choice) { first = false; show(choice.design); feedback.textContent = 'Đổi mẫu cho lần in này. Để lưu mặc định, mở Mẫu hóa đơn & máy in.'; }
    });
    try {
        const selected = PosPrinting.selected(setup, setup.templates), pref = PosPrinting.preferences(setup);
        select.replaceChildren(new Option('Mẫu đã chọn trên client', ''), ...setup.templates.map(t => new Option(t.design.name, t.key)));
        if (setup.size && ReceiptTemplates.sizes[setup.size]) selected.paperSize = setup.size;
        feedback.textContent = pref.mode === 'qz' ? `Máy in: ${pref.printer}` : 'In bằng hộp thoại trình duyệt';
        show(selected);
    } catch (error) { button.disabled = true; feedback.textContent = error.message; }
})();
