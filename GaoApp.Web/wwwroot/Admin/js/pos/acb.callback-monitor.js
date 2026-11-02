(() => {
    'use strict';
    const panel = document.getElementById('acb-callback-health');
    const button = document.getElementById('acb-refresh-status');
    const live = document.getElementById('acb-live-status');
    const message = document.getElementById('acb-monitor-message');
    if (!panel || !button || !live || !message) return;
    let busy = false;
    async function refresh() {
        if (busy || document.hidden) return;
        busy = true;
        button.disabled = true;
        const abort = new AbortController();
        const timeout = setTimeout(() => abort.abort(), 15000);
        const expanded = panel.querySelector('details')?.open;
        message.textContent = 'Đang cập nhật…';
        try {
            const response = await fetch(panel.dataset.statusUrl, {
                credentials: 'same-origin', cache: 'no-store', signal: abort.signal,
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok || response.redirected) throw new Error('Không tải được trạng thái');
            panel.innerHTML = await response.text();
            if (expanded && panel.querySelector('details')) panel.querySelector('details').open = true;
            message.textContent = 'Đã cập nhật biên nhận. Bấm Xem đối soát để cập nhật bảng giao dịch.';
        } catch {
            message.textContent = 'Chưa cập nhật được. Kiểm tra kết nối hoặc phiên đăng nhập rồi thử lại.';
        } finally {
            clearTimeout(timeout);
            busy = false;
            button.disabled = false;
        }
    }
    button.addEventListener('click', refresh);
    live.addEventListener('change', () => { if (live.checked) refresh(); });
    setInterval(() => { if (live.checked) refresh(); }, 10000);
})();
