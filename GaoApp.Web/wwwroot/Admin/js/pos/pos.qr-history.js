window.PosQrHistory = (function () {
    'use strict';
    const labels = { Creating: 'Đang tạo / xác minh', Pending: 'Chờ thanh toán', Received: 'ACB đã nhận tiền',
        Recorded: 'Đã ghi nhận một khoản', Completed: 'Đã chốt đơn', Paid: 'Đã thanh toán', Cancelled: 'Đã hủy', ManualConfirmed: 'Đã xác nhận thủ công',
        ReviewRequired: 'Cần kiểm tra', Failed: 'Chưa thành công', Expired: 'Đã hết hạn' };
    const money = amount => Number(amount || 0).toLocaleString('vi-VN') + 'đ';
    function create({ getOrderId, onOpen }) {
        const root = document.getElementById('paymentQrHistory');
        if (!root) return { refresh() {}, invalidate() {} };
        const latest = document.getElementById('btnReopenLatestPaymentQr');
        const reload = document.getElementById('btnRefreshPaymentQrHistory');
        const summary = document.getElementById('paymentQrHistorySummary');
        const list = document.getElementById('paymentQrHistoryList');
        const status = document.getElementById('paymentQrHistoryStatus');
        let revision = 0, activeOrderId = null, latestQrId = null, opening = false;
        function note(text, error = false) {
            status.textContent = text;
            status.classList.toggle('is-error', error);
        }
        async function get(url) {
            const response = await fetch(url, { method: 'GET', cache: 'no-store' });
            const body = await response.json();
            if (!response.ok) throw new Error(body.message || body.detail || 'Chưa tải được QR đã lưu. Bấm tải lại để thử lại.');
            return body;
        }
        function element(tag, className, text) {
            const node = document.createElement(tag);
            node.className = className;
            if (text != null) node.textContent = text;
            return node;
        }
        function showItems(items) {
            list.replaceChildren();
            summary.textContent = `Lịch sử ${items.length} lần tạo QR`;
            for (const item of items) {
                const row = element('div', 'pos-qr-history__row');
                const info = element('div', 'pos-qr-history__info');
                info.appendChild(element('strong', '', money(item.amount)));
                info.appendChild(element('span', 'pos-qr-history__code', item.requestCode));
                const stamp = item.createdAtUtc;
                const date = new Date(stamp + (/Z$|[+-]\d\d:\d\d$/.test(stamp) ? '' : 'Z'));
                info.appendChild(element('small', '', `${item.bankName} · ${date.toLocaleString('vi-VN')}`));
                if (item.reviewReason) info.appendChild(element('small', 'pos-qr-history__reason', item.reviewReason));
                const actions = element('div', 'pos-qr-history__actions');
                const badge = element('span', 'pos-qr-history__badge', labels[item.status] || item.status);
                badge.dataset.status = item.status;
                actions.appendChild(badge);
                if (item.canReopen) {
                    const button = element('button', 'btn btn-sm btn-outline-primary', 'Mở lại QR');
                    button.type = 'button';
                    button.dataset.reopenQrId = item.qrId;
                    button.addEventListener('click', () => open(item.qrId));
                    actions.appendChild(button);
                } else {
                    const link = element('a', 'pos-qr-history__link', 'Xem giao dịch');
                    link.href = `/admin/acb/payments/orders/${activeOrderId}`;
                    link.target = '_blank'; link.rel = 'noopener';
                    actions.appendChild(link);
                }
                row.appendChild(info); row.appendChild(actions); list.appendChild(row);
            }
        }
        function invalidate() {
            revision++; activeOrderId = null; latestQrId = null;
            list.replaceChildren(); latest.hidden = true; root.hidden = true;
        }
        async function refresh() {
            const orderId = Number(getOrderId());
            if (!orderId) { invalidate(); return; }
            const current = ++revision;
            activeOrderId = orderId; latestQrId = null; latest.hidden = true;
            root.hidden = false; reload.disabled = true;
            note('Đang tải các QR đã tạo…');
            list.replaceChildren(); summary.textContent = 'Lịch sử QR';
            try {
                const data = await get(`/admin/acb/payments/orders/${orderId}/qrs`);
                if (current !== revision || Number(getOrderId()) !== orderId) return;
                if (data.orderId !== orderId) throw new Error('QR trả về không thuộc đơn đang mở. Hãy tải lại.');
                latestQrId = data.latestQrId;
                latest.hidden = !latestQrId;
                showItems(data.items);
                const recent = data.items.find(item => item.qrId === latestQrId);
                note(recent ? `${recent.requestCode} · ${money(recent.amount)} · ${labels[recent.status] || recent.status}`
                    : data.items.length ? 'Các QR trước đã kết thúc. Xem từng lần trong lịch sử.' : 'Đơn này chưa có QR đã tạo.');
                return data;
            } catch (error) {
                if (current === revision) note(error.message, true);
            } finally { if (current === revision) reload.disabled = false; }
        }
        async function open(qrId, warningMessage = '') {
            const orderId = Number(getOrderId());
            if (opening || !qrId || orderId !== activeOrderId) return false;
            const current = revision;
            opening = true; latest.disabled = true;
            note('Đang mở QR đã lưu…');
            try {
                const saved = await get(`/admin/acb/payments/orders/${orderId}/qrs/${qrId}`);
                if (revision !== current || Number(getOrderId()) !== orderId) return false;
                if (saved.qr?.id !== qrId || saved.qr?.orderId !== orderId)
                    throw new Error('QR trả về không khớp lần thanh toán đã chọn.');
                onOpen({ ...saved.qr, canCancel: saved.canCancel, readOnly: saved.readOnly, savedStatus: saved.status,
                    savedMessage: saved.message, pendingWarningMessage: warningMessage });
                note(`Đã mở lại ${saved.qr.requestCode} · ${money(saved.qr.amount)}.`);
                return true;
            } catch (error) {
                if (revision === current) note(error.message, true);
                return false;
            } finally { opening = false; latest.disabled = false; }
        }
        latest.addEventListener('click', () => open(latestQrId));
        reload.addEventListener('click', refresh);
        return { refresh, invalidate, open };
    }
    return { create };
})();
