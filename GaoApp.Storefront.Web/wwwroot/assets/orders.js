(() => {
  'use strict';
  if (!document.querySelector('[data-guest-page]') || !window.GaoStorefrontApi) return;
  const api = window.GaoStorefrontApi; const pendingKey = `gao.storefront.pending.v1:${document.body.dataset.cartScope}`;
  const form = document.getElementById('guest-link-form'); const input = document.getElementById('guest-link-input');
  const loading = document.getElementById('guest-loading'); const error = document.getElementById('guest-error'); const orderBox = document.getElementById('guest-order');
  const copyLink = document.getElementById('copy-order-link');
  let currentToken = '', selectedToken = '', generation = 0, lookup = null, loaded = false, paymentMethod = null;
  let working = false, pendingWorking = false, pending;
  function message(text) { error.textContent = text; error.hidden = false; }
  function phaseLabel(phase) {
    if (!Number.isInteger(phase)) return 'Đang cập nhật';
    return ['Chờ thanh toán', 'Chờ cửa hàng chuẩn bị', 'Đang chuẩn bị', 'Đang giao hàng', 'Đã giao hàng', 'Đã hoàn tất', 'Đã hủy'][phase] || 'Đang cập nhật';
  }
  function refundLabel(state) {
    if (!Number.isInteger(state)) return 'Đang cập nhật';
    return ['Chưa có yêu cầu hoàn', 'Đang chờ xử lý hoàn tiền', 'Đã duyệt hoàn tiền', 'Đã hoàn tiền có chứng cứ', 'Cần đối soát lại khoản hoàn'][state] || 'Đang cập nhật';
  }
  function tokenFrom(value) {
    try {
      const url = new URL(value, location.origin);
      return url.origin === location.origin && url.pathname === '/shop/orders' && /^#[A-Za-z0-9_-]{43}$/.test(url.hash) ? url.hash.slice(1) : null;
    } catch { return null; }
  }
  function element(tag, cls, text) { const node = document.createElement(tag); if (cls) node.className = cls; if (text !== undefined) node.textContent = text; return node; }
  function invalidate(token) {
    lookup?.abort(); lookup = null; generation++; selectedToken = token || ''; currentToken = ''; loaded = false; paymentMethod = null; working = false;
    orderBox.hidden = true; copyLink.disabled = true; loading.hidden = true; error.hidden = true; error.textContent = ''; input.value = '';
    for (const id of ['guest-code', 'guest-created', 'guest-phase', 'guest-payment', 'guest-payment-method-note', 'guest-delivery', 'guest-refund', 'guest-recipient', 'guest-address', 'guest-goods', 'guest-shipping', 'guest-total']) document.getElementById(id).textContent = '';
    document.getElementById('guest-lines').replaceChildren();
    document.getElementById('guest-shipments').replaceChildren(); document.getElementById('guest-incident').hidden = true; document.getElementById('guest-incident').textContent = '';
    window.dispatchEvent(new CustomEvent('storefront-guest-changing', { detail: { token: selectedToken, generation } }));
    return generation;
  }
  function latest(at, token, controller) { return at === generation && token === selectedToken && tokenFrom(location.href) === token && !controller.signal.aborted; }
  window.GaoStorefrontGuest = Object.freeze({ phaseLabel, refundLabel, snapshot: () => ({ token: selectedToken, generation, loaded: loaded && tokenFrom(location.href) === selectedToken, paymentMethod }) });
  async function open(token, updateUrl = true, invalidMessage = '') {
    if (updateUrl) history.replaceState(null, '', token ? '/shop/orders#' + token : '/shop/orders');
    const at = invalidate(token);
    document.getElementById('pending-order').hidden = Boolean(token) || !pending;
    if (!token) { if (invalidMessage) message(invalidMessage); return; }
    working = true; loading.hidden = false;
    const controller = new AbortController(); lookup = controller;
    try {
      const order = await api.post('/shop/orders/access', { token }, controller.signal);
      if (!latest(at, token, controller)) return;
      document.getElementById('guest-code').textContent = 'Đơn ' + order.code;
      document.getElementById('guest-created').textContent = 'Tiếp nhận lúc ' + new Date(order.createdAtUtc).toLocaleString('vi-VN');
      document.getElementById('guest-phase').textContent = phaseLabel(order.phase);
      document.getElementById('guest-payment').textContent = [order.paymentMethod === 1 ? 'Chưa thanh toán QR' : 'Chưa thanh toán — COD khi nhận hàng', 'Đang kiểm tra thanh toán', 'Đã xác nhận thanh toán', 'Cần đối soát thanh toán'][order.paymentState] || 'Đang cập nhật';
      document.getElementById('guest-payment-method-note').textContent = order.paymentMethod === 1 ? 'QR được ngân hàng xác minh; chưa coi là đã giao hàng hoặc đã chốt bán.' : 'COD: thanh toán khi nhận hàng. Trạng thái tiền và giao hàng được theo dõi riêng.';
      document.getElementById('guest-delivery').textContent = ['Chưa bàn giao giao hàng', 'Sẵn sàng chuẩn bị giao', 'Đang soạn hàng', 'Đã bàn giao người vận chuyển', 'Đã giao tới người nhận', 'Đang xử lý hàng trả', 'Đã nhận lại hàng'][order.deliveryState] || 'Đang cập nhật';
      document.getElementById('guest-refund').textContent = refundLabel(order.refundState);
      const shipments = document.getElementById('guest-shipments');
      for (const shipment of order.shipments || []) {
        const item = element('section', 'checkout-line'); item.append(element('h4', '', shipment.label)); item.append(element('p', '', shipment.status));
        if (shipment.carrierName) item.append(element('p', '', shipment.carrierName));
        if (shipment.trackingCode) item.append(element('p', '', 'Mã vận đơn: ' + shipment.trackingCode));
        shipments.append(item);
      }
      if (!shipments.childElementCount) shipments.append(element('p', '', 'Cửa hàng sẽ cập nhật khi bắt đầu chuẩn bị giao.'));
      const incident = document.getElementById('guest-incident'); incident.hidden = !order.hasCustodyIncident;
      incident.textContent = order.hasCustodyIncident ? 'Cửa hàng đang kiểm tra sự cố giao hàng. Vui lòng liên hệ cửa hàng để biết thêm.' : '';
      const recipient = order.recipient; document.getElementById('guest-recipient').textContent = `${recipient.name} · ${recipient.phone}`;
      document.getElementById('guest-address').textContent = `${recipient.streetAddress}, ${recipient.wardName}, ${recipient.provinceName}`;
      const lines = document.getElementById('guest-lines'); lines.replaceChildren();
      for (const line of order.lines) {
        const block = element('section', 'checkout-line'); block.append(element('h3', '', line.name));
        block.append(element('p', '', `${line.variantName || ''} · ${line.unitName} · SL ${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 4 }).format(line.quantity)}`));
        block.append(element('p', '', `${line.unitPriceText} / ${line.unitName}`));
        if (line.isGift) block.append(element('p', 'gift-note', 'Quà tặng'));
        if (line.promotionName) block.append(element('p', 'promotion-note', line.promotionName));
        if (line.discount > 0) block.append(element('p', '', `Giảm giá đã phân bổ: ${line.discountText}`));
        if (line.roundingAdjustment !== 0) block.append(element('p', 'muted', `Điều chỉnh làm tròn POS: ${line.roundingAdjustmentText}`));
        block.append(element('strong', '', line.netAmountText)); lines.append(block);
      }
      document.getElementById('guest-goods').textContent = order.goodsAmountText; document.getElementById('guest-shipping').textContent = order.shippingChargeText; document.getElementById('guest-total').textContent = order.totalDueText;
      currentToken = token; loaded = true; paymentMethod = order.paymentMethod; copyLink.disabled = false;
      orderBox.hidden = false; document.getElementById('pending-order').hidden = true;
      window.dispatchEvent(new CustomEvent('storefront-guest-loaded', { detail: { token, generation: at, paymentMethod } }));
    } catch (problem) { if (latest(at, token, controller) && problem.name !== 'AbortError') message(problem.message || 'Chưa mở được đơn hàng. Hãy thử lại với liên kết riêng.'); }
    finally { if (at === generation && lookup === controller) { loading.hidden = true; working = false; lookup = null; } }
  }
  const invalidLink = 'Hãy dán đúng liên kết riêng của đơn tại cửa hàng này.';
  form.addEventListener('submit', event => { event.preventDefault(); const token = tokenFrom(input.value.trim()); open(token, true, token ? '' : invalidLink); });
  window.addEventListener('hashchange', () => open(tokenFrom(location.href), false, location.hash ? invalidLink : ''));
  document.getElementById('guest-refresh').addEventListener('click', () => { if (currentToken && loaded && tokenFrom(location.href) === currentToken) open(currentToken, false); });
  copyLink.addEventListener('click', async () => {
    if (!currentToken || !loaded || tokenFrom(location.href) !== currentToken) return;
    const at = generation, token = currentToken, link = location.origin + '/shop/orders#' + token;
    try { await navigator.clipboard.writeText(link); if (at === generation && token === currentToken && tokenFrom(location.href) === token) message('Đã sao chép liên kết riêng. Chỉ chia sẻ với người được bạn cho phép xem đơn.'); }
    catch { if (at !== generation || token !== currentToken || tokenFrom(location.href) !== token) return; input.value = link; input.focus(); input.select(); message('Liên kết đã được chọn trong ô nhập. Bạn có thể sao chép để giữ lại.'); }
  });
  try { const raw = sessionStorage.getItem(pendingKey); if (raw && raw.length < 65536) { const value = JSON.parse(raw); if (value?.request?.clientRequestId && value?.request?.quoteToken && /^[0-9a-f]{64}$/i.test(value.request.fingerprint)) pending = value; } }
  catch { /* No lookup by phone or code is substituted for a missing private link. */ }
  document.getElementById('pending-order-retry').addEventListener('click', async () => {
    if (!pending || working || pendingWorking || tokenFrom(location.href)) return;
    const at = generation, selectedUrl = location.href, proof = pending, button = document.getElementById('pending-order-retry'); pendingWorking = true; button.disabled = true;
    try {
      // A placement replay remains durable if navigation changes while it is in flight.
      // Do not abort it or let its result replace a newer selected capability.
      const result = await api.post('/shop/checkout/place', proof.request);
      if (at !== generation || pending !== proof || location.href !== selectedUrl) return;
      if (result.status === 1 || result.status === 'QuoteChanged') {
        try { sessionStorage.removeItem(pendingKey); } catch { }
        pending = null; document.getElementById('pending-order').hidden = true;
        message(result.message + ' Chưa có đơn được tạo. Hãy về giỏ và lấy báo giá mới.'); return;
      }
      const token = result.order ? tokenFrom(new URL(result.order.guestLink, location.origin).href) : null;
      if (!token) throw new Error('Chưa nhận được liên kết riêng; hãy kiểm tra lại cùng yêu cầu.');
      window.GaoStorefrontCart?.clearIfUnchanged(proof.cart); try { sessionStorage.removeItem(pendingKey); } catch { }
      pending = null; await open(token);
    } catch (problem) { if (at === generation && pending === proof && location.href === selectedUrl) message(problem.message + ' Giữ nguyên mã yêu cầu trước đó để tránh đặt trùng.'); }
    finally { pendingWorking = false; button.disabled = false; }
  });
  window.addEventListener('pagehide', () => invalidate(''));
  window.addEventListener('pageshow', event => { if (event.persisted) open(tokenFrom(location.href), false, location.hash ? invalidLink : ''); });
  open(tokenFrom(location.href), false, location.hash ? invalidLink : '');
})();
