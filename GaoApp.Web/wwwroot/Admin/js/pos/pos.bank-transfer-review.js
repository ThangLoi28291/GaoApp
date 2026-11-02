(function () {
    'use strict';
    let current = null, handlers, suspended, opening = false, busy = false, bound = false;
    const el = id => document.getElementById(id);
    const key = item => `${item.receiptId}:${item.waitId}`;
    function transition(element, event, action) {
        return new Promise(resolve => {
            const done = () => { clearTimeout(timeout); element.removeEventListener(event, done); resolve(); };
            element.addEventListener(event, done, { once: true });
            const timeout = setTimeout(done, 1200);
            action();
        });
    }
    function controls(disabled) {
        for (const id of ['btnBankReviewConfirm', 'btnBankReviewReject']) if (el(id)) el(id).disabled = disabled;
    }
    async function decide(correct) {
        if (!current || busy || opening) return;
        busy = true; controls(true); el('bankReviewError').hidden = true;
        const selected = current;
        try { await (correct ? handlers.confirm(selected) : handlers.reject(selected)); }
        catch (error) { el('bankReviewError').textContent = error.message || 'Chưa xử lý được khoản chuyển. Thử lại sau khi kiểm tra kết nối.'; el('bankReviewError').hidden = false; }
        finally { busy = false; controls(false); }
    }
    function bind() {
        if (bound) return; bound = true;
        el('btnBankReviewConfirm').addEventListener('click', () => decide(true));
        el('btnBankReviewReject').addEventListener('click', () => decide(false));
        el('bankTransferReviewModal').addEventListener('shown.bs.modal', () => el('btnBankReviewConfirm').focus());
        document.addEventListener('keydown', event => {
            if (!current || !opening && !el('bankTransferReviewModal').classList.contains('show') || event.isComposing || event.ctrlKey || event.altKey || event.metaKey) return;
            if (!['Enter', 'Escape'].includes(event.key)) return;
            event.preventDefault(); event.stopImmediatePropagation();
            if (!event.repeat) void decide(event.key === 'Enter');
        }, true);
    }
    async function show(item, callbacks) {
        if (current || opening || !window.bootstrap?.Modal || !el('bankTransferReviewModal')) return;
        // Let the cashier finish other dialogs before opening this payment review.
        const visible = [...document.querySelectorAll('.modal.show')];
        if (visible.some(node => !['paymentQrModal', 'paymentModal'].includes(node.id))) return;
        bind(); opening = true; current = { ...item }; handlers = callbacks; controls(true);
        el('bankReviewAmount').textContent = Number(item.amount).toLocaleString('vi-VN') + ' đ';
        el('bankReviewSender').textContent = item.senderName || 'Chưa đọc được tên người chuyển';
        el('bankReviewOrder').textContent = `Đơn #${item.orderId}`;
        el('bankReviewTime').textContent = new Date(item.postedAtUtc).toLocaleTimeString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour:'2-digit', minute:'2-digit' });
        el('bankReviewReference').textContent = item.transactionCode;
        el('bankReviewContent').textContent = item.content || 'Thông báo không có thêm nội dung.';
        el('bankReviewError').hidden = true;
        const modal = el('bankTransferReviewModal');
        suspended = visible.find(node => node.id === 'paymentQrModal');
        if (suspended && suspended !== modal) {
            await transition(suspended, 'hidden.bs.modal', () => window.bootstrap.Modal.getOrCreateInstance(suspended).hide());
        }
        if (current && key(current) === key(item))
            await transition(modal, 'shown.bs.modal', () => window.bootstrap.Modal.getOrCreateInstance(modal, { backdrop: 'static', keyboard: false }).show());
        opening = false; controls(false);
    }
    async function close(restore = false) {
        if (!current) return;
        const modal = el('bankTransferReviewModal'), previous = suspended;
        current = null; suspended = null; controls(true);
        if (modal.classList.contains('show'))
            await transition(modal, 'hidden.bs.modal', () => window.bootstrap.Modal.getOrCreateInstance(modal).hide());
        if (restore && previous?.id === 'paymentQrModal') window.bootstrap.Modal.getOrCreateInstance(previous).show();
    }
    window.PosBankTransferReview = { show, close, active: () => current, busy: () => busy || opening };
}());
