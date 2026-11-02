(() => {
  'use strict';
  const form = document.getElementById('checkout-form');
  if (!form || !window.GaoStorefrontCart || !window.GaoStorefrontApi) return;
  const cart = window.GaoStorefrontCart; const api = window.GaoStorefrontApi;
  const pendingKey = `gao.storefront.pending.v1:${document.body.dataset.cartScope}`;
  const fields = document.getElementById('recipient-fields'); const province = form.elements.namedItem('ProvinceCode'); const ward = form.elements.namedItem('WardCode');
  const quoteButton = document.getElementById('checkout-quote'); const confirm = document.getElementById('checkout-confirm'); const message = document.getElementById('checkout-message');
  let currentQuote = null; let quotedCart = null; let pending = null; let formRevision = 0; let quoting = false; let placing = false; let wardRequest = 0;
  function note(text, error = false) { message.textContent = text; message.hidden = false; message.setAttribute('role', error ? 'alert' : 'status'); }
  function showError(text) { const box = document.getElementById('placement-error'); box.textContent = text; box.hidden = false; }
  function element(tag, cls, text) { const node = document.createElement(tag); if (cls) node.className = cls; if (text !== undefined) node.textContent = text; return node; }
  function forgetPending() { try { sessionStorage.removeItem(pendingKey); } catch { /* A retained proof only replays the same order on reload. */ } pending = null; }
  function savePending(value) { sessionStorage.setItem(pendingKey, JSON.stringify(value)); pending = value; }
  try {
    const raw = sessionStorage.getItem(pendingKey);
    if (raw && raw.length < 65536) {
      const value = JSON.parse(raw);
      const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
      if (value?.request && guid.test(value.request.clientRequestId) && guid.test(value.request.quoteToken) && /^[0-9a-f]{64}$/i.test(value.request.fingerprint) && typeof value.cart?.signature === 'string' && typeof value.cart?.revision === 'string') pending = value;
      else note('Không đọc được mã yêu cầu trước đó. Nếu đã gửi đơn, hãy kiểm tra liên kết riêng hoặc liên hệ cửa hàng trước khi đặt tiếp.', true);
    }
  } catch { note('Trình duyệt chưa cho đọc phiên yêu cầu trước đó. Hãy kiểm tra đơn đã gửi trước khi đặt lại.', true); }
  function invalidate() {
    formRevision++;
    if (pending || quoting || placing) return;
    currentQuote = null; confirm.disabled = true; confirm.textContent = 'Xác nhận đặt đơn'; document.getElementById('quote-content').hidden = true;
    document.getElementById('quote-placeholder').hidden = false;
  }
  form.addEventListener('input', event => {
    invalidate(); const name = event.target.name;
    if (name) { const error = document.querySelector(`[data-field-error="${name}"]`); if (error) error.hidden = true; event.target.removeAttribute('aria-invalid'); }
  });
  form.addEventListener('change', invalidate);
  province.addEventListener('change', async () => {
    const request = ++wardRequest; const code = province.value; ward.replaceChildren(new Option(code ? 'Đang tải phường / xã…' : 'Chọn tỉnh / thành phố trước', '')); ward.disabled = true;
    if (!code) return;
    try {
      const response = await fetch(`/shop/checkout/wards?provinceCode=${encodeURIComponent(code)}`, { credentials: 'same-origin', cache: 'no-store' });
      const data = await response.json().catch(() => null); if (request !== wardRequest || code !== province.value) return;
      if (!response.ok || !Array.isArray(data?.wards)) throw new Error(data?.message || 'Chưa tải được phường / xã. Hãy chọn lại tỉnh / thành phố.');
      ward.replaceChildren(new Option('Chọn phường / xã', '')); data.wards.forEach(value => ward.add(new Option(value.name, value.code))); ward.disabled = false;
    } catch (error) { if (request === wardRequest) { ward.replaceChildren(new Option('Chọn lại tỉnh / thành phố để thử lại', '')); note(error.message, true); } }
  });
  function renderQuote(quote) {
    const lines = document.getElementById('quote-lines'); lines.replaceChildren();
    for (const line of quote.lines) {
      const block = element('article', 'checkout-line'); block.dataset.quoteLine = String(line.index);
      block.append(element('h3', '', line.name), element('p', '', `${line.variantName} · ${line.unitName} · SL ${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 4 }).format(line.quantity)}`));
      block.append(element('p', '', `${line.unitPriceText} / ${line.unitName}`));
      if (line.isGift) block.append(element('p', 'gift-note', 'Quà tặng theo chương trình'));
      if (line.promotionName) block.append(element('p', 'promotion-note', line.promotionName));
      if (line.discount > 0) block.append(element('p', '', `Giảm giá đã phân bổ: ${line.discountText}`));
      if (line.roundingAdjustment !== 0) block.append(element('p', 'muted', `Điều chỉnh làm tròn POS: ${line.roundingAdjustmentText}`));
      block.append(element('strong', '', line.netAmountText)); lines.append(block);
    }
    const recipient = quote.recipient;
    document.getElementById('quote-address').textContent = `${recipient.name} · ${recipient.phone} · ${recipient.streetAddress}, ${recipient.wardName}, ${recipient.provinceName}`;
    document.getElementById('quote-delivery').textContent = `${quote.deliveryMethod === 0 || quote.deliveryMethod === 'Local' ? 'Giao gần' : 'Giao toàn quốc'} · ${quote.shippingZoneName} · Dự kiến ${quote.minDeliveryDays}–${quote.maxDeliveryDays} ngày`;
    document.getElementById('quote-weight').textContent = quote.totalWeightKg === null ? 'Phí giao theo bảng cố định; không tự thay kg thiếu bằng 0.' : `Tổng trọng lượng giao: ${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 6 }).format(quote.totalWeightKg)} kg`;
    document.getElementById('quote-goods').textContent = quote.goodsAmountText; document.getElementById('quote-shipping').textContent = quote.shippingChargeText; document.getElementById('quote-total').textContent = quote.totalDueText;
    document.getElementById('quote-shortage').hidden = !quote.hasInventoryShortage;
    document.getElementById('quote-expiry').textContent = `Báo giá có hiệu lực đến ${new Date(quote.expiresAtUtc).toLocaleTimeString('vi-VN')}. Máy chủ kiểm tra lại khi xác nhận.`;
    document.getElementById('quote-content').hidden = false; document.getElementById('quote-placeholder').hidden = true;
    confirm.disabled = false; confirm.textContent = quote.paymentMethod === 1 ? 'Đặt đơn và mở QR thanh toán' : 'Xác nhận đặt COD';
  }
  function restoreRecipient(recipient) {
    // A replacement after a reload still belongs to the server's original address, not empty form defaults.
    form.elements.namedItem('RecipientName').value = recipient.name;
    form.elements.namedItem('RecipientPhone').value = recipient.phone;
    form.elements.namedItem('StreetAddress').value = recipient.streetAddress;
    form.elements.namedItem('Note').value = recipient.note || '';
    if(currentQuote) form.elements.namedItem('PaymentMethod').value=String(currentQuote.paymentMethod ?? 0);
    province.value = recipient.provinceCode;
    wardRequest++;
    ward.replaceChildren(new Option(recipient.wardName, recipient.wardCode)); ward.disabled = false;
  }
  form.addEventListener('submit', async event => {
    event.preventDefault(); if (pending || quoting || placing) return;
    document.querySelectorAll('[data-field-error]').forEach(node => { node.hidden = true; });
    if (!form.reportValidity()) return;
    const snapshot = cart.snapshot(); if (snapshot.items.length === 0) { document.getElementById('checkout-empty').hidden = false; return; }
    const revision = formRevision; const input = Object.fromEntries(new FormData(form)); input.items = snapshot.items;
    input.PaymentMethod=Number(input.PaymentMethod);
    quoting = true; quoteButton.disabled = true; confirm.disabled = true; note('Đang kiểm tra giá, ưu đãi và phí giao…');
    try {
      const quote = await api.post('/shop/checkout/quote', input); const latest = cart.snapshot();
      if (revision !== formRevision || latest.signature !== snapshot.signature || latest.revision !== snapshot.revision) { currentQuote = null; note('Thông tin hoặc giỏ vừa thay đổi. Hãy lấy lại báo giá trước khi đặt.', true); return; }
      currentQuote = quote; quotedCart = snapshot; renderQuote(quote); note('Hãy xem báo giá chốt và địa chỉ. Chỉ tạo đơn khi bạn bấm xác nhận đặt hàng.');
    } catch (error) {
      currentQuote = null; note(error.message, true);
      if (error.fields) {
        for (const [key, errors] of Object.entries(error.fields)) {
          const clean = key.replace(/^request\./i, '');
          const field = [...form.elements].find(node => node.name?.toLowerCase() === clean.toLowerCase());
          const output = [...document.querySelectorAll('[data-field-error]')].find(node => node.dataset.fieldError.toLowerCase() === clean.toLowerCase());
          if (output) { output.textContent = errors.join(' '); output.hidden = false; } if (field) field.setAttribute('aria-invalid', 'true');
        }
        form.querySelector('[aria-invalid=true]')?.focus();
      }
    } finally { quoting = false; quoteButton.disabled = false; }
  });
  async function place() {
    if (placing) return;
    if (!pending) {
      if (!currentQuote || !quotedCart) return;
      const latest = cart.snapshot();
      if (latest.signature !== quotedCart.signature || latest.revision !== quotedCart.revision) { invalidate(); note('Giỏ vừa thay đổi. Hãy xem lại báo giá mới.', true); return; }
      try {
        const id = globalThis.crypto?.randomUUID?.(); if (!id) throw new Error('Trình duyệt cần kết nối an toàn để tạo mã yêu cầu.');
        savePending({ request: { clientRequestId: id, quoteToken: currentQuote.token, fingerprint: currentQuote.fingerprint }, cart: { signature: quotedCart.signature, revision: quotedCart.revision } });
      } catch { showError('Trình duyệt chưa lưu được mã yêu cầu an toàn. Chưa gửi đơn; hãy cho phép lưu phiên hoặc dùng trình duyệt khác.'); return; }
    }
    placing = true; fields.disabled = true; confirm.disabled = true; document.getElementById('placement-error').hidden = true; note('Đang xác nhận cùng một yêu cầu đặt hàng…');
    try {
      const result = await api.post('/shop/checkout/place', pending.request);
      if (result.status === 1 || result.status === 'QuoteChanged') {
        const previousCart = pending.cart; forgetPending(); currentQuote = result.replacementQuote; renderQuote(currentQuote); restoreRecipient(currentQuote.recipient); fields.disabled = false;
        const latest = cart.snapshot(); quotedCart = latest.signature === previousCart.signature && latest.revision === previousCart.revision ? latest : null;
        if (!quotedCart) { confirm.disabled = true; note(result.message + ' Giỏ cũng đã đổi; hãy lấy lại báo giá theo giỏ hiện tại.', true); }
        else { confirm.textContent = 'Xác nhận báo giá mới và đặt đơn'; note(result.message, true); }
        return;
      }
      if (!result.order || !/^\/shop\/orders#[A-Za-z0-9_-]{43}$/.test(result.order.guestLink)) throw new Error('Chưa nhận được liên kết riêng. Hãy kiểm tra lại cùng yêu cầu.');
      const oldCart = pending.cart; forgetPending(); cart.clearIfUnchanged(oldCart);
      document.getElementById('placed-code').textContent = result.order.code; document.getElementById('placed-total').textContent = result.order.totalDueText; document.getElementById('placed-link').href = result.order.guestLink;
      document.getElementById('placed-payment-message').textContent = result.order.paymentMethod === 1 ? 'Đơn đang chờ thanh toán QR. Mở liên kết riêng bên dưới; chưa tự coi là đã thu tiền.' : 'Chưa thanh toán. Cửa hàng đang chờ chuẩn bị, thanh toán COD khi nhận hàng.';
      document.getElementById('placed-link').textContent = result.order.paymentMethod === 1 ? 'Mở QR thanh toán và theo dõi đơn' : 'Xem trạng thái đơn bằng liên kết riêng';
      document.getElementById('checkout-content').hidden = true; document.getElementById('placement-success').hidden = false; message.hidden = true;
      document.getElementById('placed-link').focus();
    } catch (error) {
      if (error.safeToRequote) { forgetPending(); currentQuote = null; fields.disabled = false; confirm.disabled = true; showError(error.message + ' Hãy kiểm tra thông tin và lấy lại báo giá.'); }
      else { showError(error.message + ' Yêu cầu có thể đã được nhận. Hãy thử lại cùng mã đang lưu để tránh đặt trùng.'); confirm.disabled = false; confirm.textContent = 'Kiểm tra lại đúng yêu cầu'; }
    } finally { placing = false; }
  }
  confirm.addEventListener('click', place);
  window.addEventListener('storefront-cart-changed', () => { if (!pending && !placing) { invalidate(); note('Giỏ đã thay đổi. Hãy lấy lại báo giá trước khi xác nhận.'); } });
  if (pending) { fields.disabled = true; confirm.disabled = false; confirm.textContent = 'Kiểm tra lại đúng yêu cầu'; note('Đã tìm thấy yêu cầu trước đó. Kiểm tra lại cùng mã để nhận đúng đơn; không gửi một đơn mới.'); }
  else {
    const snapshot = cart.snapshot();
    if (!snapshot.items.length) { document.getElementById('checkout-content').hidden = true; document.getElementById('checkout-empty').hidden = false; }
    else if (snapshot.recovered) note('Đã bỏ qua lựa chọn lưu không hợp lệ, giữ các dòng hợp lệ. Hãy kiểm tra giỏ trước khi đặt.');
  }
})();
