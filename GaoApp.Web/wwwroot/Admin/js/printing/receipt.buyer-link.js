(function () {
    'use strict';
    const form = document.getElementById('replaceInvoiceBuyerLink');
    if (!form) return;
    const button = form.querySelector('button');
    const status = document.getElementById('replaceInvoiceBuyerLinkStatus');
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (!window.confirm('Thu hồi tất cả QR nhập thông tin hóa đơn cũ của đơn này và cấp QR mới? Hạn nhập thông tin giữ nguyên.')) return;
        button.disabled = true;
        try {
            if (typeof window.GaoReceiptPrint?.setInvoiceBuyerLink !== 'function')
                throw new Error('Trang in chưa sẵn sàng. Vui lòng chờ tải xong rồi thử lại.');
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin' });
            if (!response.ok) throw new Error('Không thể cấp lại QR. Vui lòng tải lại trang để kiểm tra trạng thái đơn hàng.');
            window.GaoReceiptPrint.setInvoiceBuyerLink(await response.json());
            status.textContent = 'Đã thu hồi QR cũ. Bấm “In hóa đơn” để in QR mới; hạn nhập thông tin giữ nguyên.';
        } catch (error) {
            status.textContent = error.message;
        } finally {
            button.disabled = false;
        }
    });
})();
