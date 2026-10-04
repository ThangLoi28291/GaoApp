(() => {
    'use strict';
    const notice = document.getElementById('kioskMonitorState');
    const date = x => x ? new Date(x.endsWith('Z') ? x : x + 'Z').toLocaleString('vi-VN') : 'Chưa kết nối';
    let loading = false;
    async function refresh() {
        if (loading || document.hidden) return; loading = true;
        try {
            const response = await fetch('/admin/kiosks/status', { cache: 'no-store', headers: { Accept: 'application/json' } });
            if (!response.ok) throw new Error();
            for (const s of await response.json()) {
                const card = document.querySelector(`[data-kiosk-id="${s.id}"]`); if (!card) continue;
                const status = card.querySelector('[data-status]');
                status.textContent = !s.isActive ? 'Đã thu hồi' : s.isPaused ? 'Tạm dừng' : !s.activated ? 'Chờ kích hoạt' : s.lastSeenAtUtc && Date.now() - Date.parse(s.lastSeenAtUtc.endsWith('Z') ? s.lastSeenAtUtc : s.lastSeenAtUtc + 'Z') < 30000 ? 'Đang kết nối' : 'Mất kết nối';
                status.className = 'badge ' + (status.textContent === 'Đang kết nối' ? 'bg-label-success' : 'bg-label-warning');
                card.querySelector('[data-seen]').textContent = 'Truy cập gần nhất: ' + date(s.lastSeenAtUtc);
                const help = card.querySelector('[data-help]'); help.hidden = !s.helpRequestedAtUtc;
                help.textContent = 'Khách cần hỗ trợ · ' + date(s.helpRequestedAtUtc);
                const pause = card.querySelector('form[action$="/pause"] button'); if (pause) pause.textContent = s.isPaused ? 'Tiếp tục phục vụ' : 'Tạm dừng';
                const order = card.querySelector('[data-order]'); order.hidden = !s.orderId;
                if (s.orderId) {
                    order.replaceChildren(document.createTextNode(`Đơn #${s.orderId} · ${Number(s.total || 0).toLocaleString('vi-VN')}đ · ${['Đang tạo QR', 'Chờ thanh toán', 'Đã nhận tiền', 'Đã hoàn tất', 'QR đã hủy', 'Cần kiểm tra'][s.paymentStatus] || 'Đang chọn hàng'} `));
                    for (const [label, url] of [['Xem đơn', '/admin/pos/order-detail/' + s.orderId], ['Tra cứu ACB', '/admin/acb/payments/orders/' + s.orderId]]) {
                        const link = document.createElement('a'); link.href = url; link.textContent = label; link.className = 'ms-2'; order.append(link);
                    }
                }
            }
            notice.textContent = 'Đã cập nhật ' + new Date().toLocaleTimeString('vi-VN') + ' · Tự cập nhật mỗi 5 giây';
        } catch { notice.textContent = 'Chưa cập nhật được trạng thái. Đang thử kết nối lại…'; }
        finally { loading = false; }
    }
    document.querySelectorAll('form[action^="/admin/kiosks/"]').forEach(form => form.addEventListener('submit', async ev => {
        const action = form.action.split('/').pop();
        const message = { revoke: 'Thu hồi quyền máy này? Máy sẽ phải kích hoạt lại. Giao dịch đang chờ được giữ để đối soát.', 'cancel-payment': 'Kiểm tra ngân hàng rồi hủy phiên chưa thanh toán? Hệ thống sẽ chặn hủy nếu đã nhận tiền.', 'close-shift': 'Chốt phiên của quầy và tạm dừng phục vụ?' }[action];
        if (message && !window.confirm(message)) { ev.preventDefault(); return; }
        if (['key', 'create'].includes(action)) return;
        ev.preventDefault(); const button = form.querySelector('button'), result = document.getElementById('kioskActionMessage');
        button.disabled = true; result.hidden = true;
        try {
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' } });
            if (!response.ok) { const text = await response.text(); let data; try { data = JSON.parse(text); } catch { data = {}; } throw new Error(data.message || (text.startsWith('<') ? 'Chưa thể thực hiện thao tác. Vui lòng tải lại rồi thử lại.' : text)); }
            if (response.redirected && new URL(response.url).pathname !== '/admin/kiosks') throw new Error('Phiên quản trị đã kết thúc. Vui lòng đăng nhập lại.');
            result.className = 'alert alert-success'; result.textContent = 'Đã cập nhật quầy tự phục vụ.'; await refresh();
        } catch (error) { result.className = 'alert alert-danger'; result.textContent = error.message; }
        finally { result.hidden = false; button.disabled = false; }
    }));
    refresh(); setInterval(refresh, 5000);
})();
