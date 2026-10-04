/* Cash feedback is a snapshot of the paid order, never the next cart. */
window.PosCheckoutFeedback = (() => {
    'use strict';
    const amount = value => Number.isFinite(Number(value)) ? Math.max(0, Number(value)) : 0;
    function cashSummary(draft) {
        if (!draft || !Array.isArray(draft.payments)) return null;
        const cash = draft.payments.filter(p => p.method === 0 || String(p.method).toLowerCase() === 'cash');
        const received = cash.reduce((sum, p) => sum + amount(p.amount), 0);
        if (!received) return null;
        return { received, total: amount(draft.grandTotal), change: amount(draft.changeDue),
            other: draft.payments.filter(p => !cash.includes(p)).reduce((sum, p) => sum + amount(p.amount), 0) };
    }
    function create({ modal, submit, isBusy, choosePrint }) {
        if (!modal) return null;
        let printChoice = false;
        const el = id => modal.querySelector('#' + id);
        const money = value => amount(value).toLocaleString('vi-VN') + ' đ';
        function begin(summary) {
            printChoice = false;
            el('receiptPrintChoices').hidden = true;
            el('cashReceiptSummary').hidden = !summary;
            el('invoiceIntentChoices').hidden = false;
            el('invoiceIntentModalTitle').textContent = 'Khách có lấy hóa đơn điện tử không?';
            el('invoiceIntentDescription').textContent = 'Chọn phương thức hóa đơn trước khi in bill.';
            el('invoiceIntentHelp').textContent = 'Enter / K: Không lấy hóa đơn · C: Có lấy hóa đơn';
            if (summary) {
                el('cashReceiptTotal').textContent = money(summary.total);
                el('cashReceiptReceived').textContent = money(summary.received);
                el('cashReceiptChange').textContent = money(summary.change);
                el('cashReceiptOtherRow').hidden = !summary.other;
                el('cashReceiptOther').textContent = money(summary.other);
            }
        }
        function askPrint() {
            printChoice = true;
            el('invoiceIntentChoices').hidden = true;
            el('receiptPrintChoices').hidden = false;
            el('invoiceIntentModalTitle').textContent = 'Có in bill cho khách không?';
            el('invoiceIntentDescription').textContent = 'Khách đã bật mặc định không lấy bill giấy.';
            el('invoiceIntentHelp').textContent = 'Enter / K: Không in bill · C: In bill';
            el('btnReceiptDoNotPrint').focus();
        }
        el('btnReceiptDoNotPrint')?.addEventListener('click', () => { if (printChoice && !isBusy()) choosePrint(false); });
        el('btnReceiptPrint')?.addEventListener('click', () => { if (printChoice && !isBusy()) choosePrint(true); });
        modal.addEventListener('shown.bs.modal', () => el(printChoice ? 'btnReceiptDoNotPrint' : 'btnInvoiceIntentAutomatic').focus());
        document.addEventListener('keydown', event => {
            if (!modal.classList.contains('show') || event.isComposing || event.ctrlKey || event.altKey || event.metaKey) return;
            if (event.target?.matches?.('input, textarea, select, [contenteditable="true"]')) return;
            const key = event.key.toLowerCase();
            if (!['enter', 'k', 'c'].includes(key)) return;
            event.preventDefault(); event.stopImmediatePropagation();
            if (event.repeat || isBusy()) return;
            if (printChoice) choosePrint(key === 'c');
            else submit(key === 'c' ? 2 : 1);
        }, true);
        return { begin, askPrint };
    }
    return { cashSummary, create };
})();
