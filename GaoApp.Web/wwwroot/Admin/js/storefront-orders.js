(() => {
  'use strict';
  const page = document.querySelector('[data-storefront-inbox]'); if (!page) return;
  const afterId = Number(page.dataset.afterId); let reading = false;
  async function refresh() {
    if (reading || document.hidden) return; reading = true;
    try {
      const response = await fetch(`/admin/storefront/orders/notifications?afterId=${afterId}`, { credentials: 'same-origin', cache: 'no-store' });
      if (!response.ok) throw new Error(); const result = await response.json();
      document.getElementById('storefront-awaiting-count').textContent = String(result.awaitingPreparationCount);
      document.getElementById('storefront-new-count').textContent = String(result.newOrders);
      document.getElementById('storefront-new-orders').hidden = result.newOrders <= 0;
      document.getElementById('storefront-poll-state').textContent = 'Vừa kiểm tra đơn mới. Tự kiểm tra lại sau 30 giây.';
    } catch { document.getElementById('storefront-poll-state').textContent = 'Chưa kiểm tra được đơn mới. Trang sẽ thử lại; bạn có thể tải lại danh sách.'; }
    finally { reading = false; }
  }
  setInterval(refresh, 30000); document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
})();
