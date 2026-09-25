(function () {
    'use strict';
    let dialog;
    let frame;
    let previousFocus;
    document.addEventListener('click', event => {
        const link = event.target.closest('a[data-acb-order-id]');
        if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return;
        if (typeof HTMLDialogElement === 'undefined') return;
        const id = Number(link.dataset.acbOrderId);
        if (!Number.isInteger(id) || id <= 0) return;
        event.preventDefault(); previousFocus = link;
        if (!dialog) {
            dialog = document.createElement('dialog'); dialog.className = 'acb-dialog'; dialog.setAttribute('aria-label', 'Thông tin chuyển khoản');
            const header = document.createElement('div'); header.className = 'acb-dialog-header';
            const title = document.createElement('span'); title.textContent = 'HÓA ĐƠN / THÔNG TIN CHUYỂN KHOẢN';
            const close = document.createElement('button'); close.type = 'button'; close.className = 'acb-dialog-close'; close.textContent = '×'; close.setAttribute('aria-label', 'Đóng thông tin chuyển khoản'); close.addEventListener('click', () => dialog.close());
            header.append(title, close); frame = document.createElement('iframe'); frame.title = 'Lịch sử chuyển khoản của hóa đơn';
            dialog.append(header, frame); document.body.append(dialog);
            dialog.addEventListener('close', () => { frame.removeAttribute('src'); previousFocus?.focus(); });
        }
        frame.src = `/admin/acb/payments/orders/${id}?embedded=true`;
        dialog.showModal();
    });
})();
