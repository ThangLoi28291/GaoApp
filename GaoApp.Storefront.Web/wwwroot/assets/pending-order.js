(() => {
  'use strict';
  const box = document.getElementById('checkout-resume'); if (!box || !window.GaoStorefrontApi) return;
  const key = `gao.storefront.pending.v1:${document.body.dataset.cartScope}`; let pending;
  try { const raw = sessionStorage.getItem(key); if (raw && raw.length < 65536) { const value = JSON.parse(raw); if (value?.request?.clientRequestId && value?.request?.quoteToken && /^[0-9a-f]{64}$/i.test(value.request.fingerprint)) pending = value; } }
  catch { return; }
  if (!pending) return; box.hidden = false;
  const button = document.getElementById('checkout-resume-retry'); const message = document.getElementById('checkout-resume-message');
  button.addEventListener('click', async () => {
    if (button.disabled) return; button.disabled = true;
    try {
      const result = await window.GaoStorefrontApi.post('/shop/checkout/place', pending.request);
      if (result.status === 1 || result.status === 'QuoteChanged') { message.textContent = result.message + ' Chưa tạo đơn; chờ cửa hàng mở nhận đơn mới rồi lấy lại báo giá.'; return; }
      if (!result.order || !/^\/shop\/orders#[A-Za-z0-9_-]{43}$/.test(result.order.guestLink)) throw new Error('Chưa nhận được liên kết riêng.');
      const link = document.getElementById('checkout-resume-link'); link.href = result.order.guestLink; link.hidden = false;
      message.textContent = 'Đã khôi phục đúng đơn ' + result.order.code + ' · Tổng ' + result.order.totalDueText;
      window.GaoStorefrontCart?.clearIfUnchanged(pending.cart); try { sessionStorage.removeItem(key); } catch { }
      pending = null; button.hidden = true; link.focus();
    } catch (error) { message.textContent = error.message + ' Giữ nguyên mã yêu cầu đang lưu; có thể tải lại trang rồi thử lại khi cửa hàng khắc phục.'; }
    finally { button.disabled = false; }
  });
})();
