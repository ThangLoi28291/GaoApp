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
        let pendingCashReminder = null;
        let awaitingCashReminder = null;
        let reminderTimer = null;
        const reminder = document.getElementById('posCashReminder');
        const el = id => modal.querySelector('#' + id);
        const money = value => amount(value).toLocaleString('vi-VN') + ' đ';
        function hideReminder() {
            clearTimeout(reminderTimer);
            reminderTimer = null;
            if (reminder) {
                reminder.hidden = true;
                reminder.classList.remove('is-visible');
            }
        }
        function showReminder(snapshot) {
            if (!reminder || !snapshot) return;
            hideReminder();
            reminder.querySelector('#posCashReminderReceived').textContent = snapshot.received.toLocaleString('vi-VN');
            reminder.querySelector('#posCashReminderChange').textContent = snapshot.change.toLocaleString('vi-VN');
            const order = reminder.querySelector('#posCashReminderOrder');
            order.hidden = !snapshot.orderId;
            order.textContent = snapshot.orderId ? `Đơn #${snapshot.orderId}` : '';
            reminder.hidden = false;
            void reminder.offsetWidth;
            reminder.classList.add('is-visible');
            reminderTimer = setTimeout(hideReminder, 4000);
        }
        function showReminderWhenPosReturns() {
            if (!awaitingCashReminder || document.hidden || document.hasFocus?.() === false) return;
            const snapshot = awaitingCashReminder;
            awaitingCashReminder = null;
            showReminder(snapshot);
        }
        function begin(summary, orderId) {
            hideReminder();
            awaitingCashReminder = null;
            pendingCashReminder = summary && amount(summary.received) > 0
                ? { received: amount(summary.received), change: amount(summary.change),
                    orderId: Number.isInteger(Number(orderId)) && Number(orderId) > 0 ? Number(orderId) : null }
                : null;
            printChoice = false;
            el('receiptPrintChoices').hidden = true;
            el('cashReceiptSummary').hidden = !summary;
            el('invoiceIntentChoices').hidden = true;
            el('invoiceIntentModalTitle').textContent = 'Hoàn tất đơn hàng';
            el('invoiceIntentDescription').textContent = 'Hóa đơn được xử lý tự động theo cấu hình.';
            el('invoiceIntentHelp').textContent = 'Enter: Thử lại hoàn tất';
            if (summary) {
                el('cashReceiptTotal').textContent = money(summary.total);
                el('cashReceiptReceived').textContent = money(summary.received);
                el('cashReceiptChange').textContent = money(summary.change);
                el('cashReceiptOtherRow').hidden = !summary.other;
                el('cashReceiptOther').textContent = money(summary.other);
            }
        }
        function complete() {
            awaitingCashReminder = pendingCashReminder;
            pendingCashReminder = null;
            showReminderWhenPosReturns();
        }
        function showRetry() {
            printChoice = false;
            el('invoiceIntentChoices').hidden = false;
            el('receiptPrintChoices').hidden = true;
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
        modal.addEventListener('hidden.bs.modal', complete);
        window.addEventListener?.('focus', showReminderWhenPosReturns);
        document.addEventListener('visibilitychange', showReminderWhenPosReturns);
        document.addEventListener('keydown', event => {
            if (!modal.classList.contains('show') || event.isComposing || event.ctrlKey || event.altKey || event.metaKey) return;
            if (event.target?.matches?.('input, textarea, select, [contenteditable="true"]')) return;
            const key = event.key.toLowerCase();
            if (!(printChoice ? ['enter', 'k', 'c'].includes(key) : key === 'enter')) return;
            event.preventDefault(); event.stopImmediatePropagation();
            if (event.repeat || isBusy()) return;
            if (printChoice) choosePrint(key === 'c');
            else submit(1);
        }, true);
        return { begin, askPrint, showRetry, complete };
    }
    return { cashSummary, create };
})();
