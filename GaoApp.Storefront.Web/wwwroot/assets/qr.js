(() => {
  'use strict';
  const panel = document.getElementById('guest-qr');
  if (!panel || !window.GaoStorefrontApi) return;
  const reopen = document.getElementById('qr-reopen');
  const copyIds = ['qr-copy-account', 'qr-copy-content', 'qr-copy-amount'];
  const states = ['Đang chuẩn bị QR', 'Đang xác minh tạo QR', 'Chờ ngân hàng xác minh thanh toán', 'Đã xác minh thanh toán', 'Cần đối soát', 'QR đã quá hạn', 'QR đã đóng vì đơn hủy'];
  let token = '', generation = -1, current = null, knownClosed = false, request = null, timer, imageUrl = '', imageReference = '';
  function latest(at, secret) {
    const capability = window.GaoStorefrontGuest?.snapshot();
    return at === generation && secret === token && capability?.loaded && capability.paymentMethod === 1 && capability.generation === at && capability.token === secret;
  }
  function clearImage() {
    if (imageUrl) URL.revokeObjectURL(imageUrl);
    imageUrl = ''; imageReference = '';
    const image = document.getElementById('qr-image'); image.hidden = true; image.removeAttribute('src');
    const download = document.getElementById('qr-download'); download.hidden = true; download.removeAttribute('href'); download.removeAttribute('download');
  }
  function clearCopy() {
    const input = document.getElementById('qr-copy-fallback'); input.value = ''; input.hidden = true;
    document.getElementById('qr-copy-label').hidden = true; document.getElementById('qr-copy-message').textContent = '';
  }
  function reset(nextGeneration) {
    request?.abort(); request = null; clearTimeout(timer); timer = undefined;
    generation = Number.isSafeInteger(nextGeneration) ? nextGeneration : generation + 1;
    token = ''; current = null; knownClosed = false; panel.hidden = true; clearImage(); clearCopy(); reopen.disabled = true;
    document.getElementById('qr-payment-details').hidden = false;
    document.getElementById('qr-heading').textContent = 'Thanh toán QR'; reopen.textContent = 'Mở lại / kiểm tra QR';
    for (const id of copyIds) { document.getElementById(id).disabled = true; document.getElementById(id).hidden = false; }
    for (const id of ['qr-state', 'qr-message', 'qr-amount', 'qr-beneficiary', 'qr-account', 'qr-content', 'qr-expiry']) document.getElementById(id).textContent = '';
  }
  function start() {
    const capability = window.GaoStorefrontGuest?.snapshot();
    if (!capability?.loaded || capability.paymentMethod !== 1 || !/^[A-Za-z0-9_-]{43}$/.test(capability.token)) { reset(capability?.generation); return; }
    if (generation !== capability.generation || token !== capability.token) {
      reset(capability.generation); token = capability.token;
    }
    reopen.disabled = false; clearTimeout(timer); refresh();
  }
  async function refresh() {
    if (!token || request || document.hidden || !latest(generation, token)) return;
    const at = generation, secret = token, controller = new AbortController(); request = controller; reopen.disabled = true;
    try {
      const result = await window.GaoStorefrontApi.post('/shop/orders/qr', { token: secret }, controller.signal);
      if (controller.signal.aborted || !latest(at, secret)) return;
      current = result; panel.hidden = false;
      const closed = result.state === 6; knownClosed = closed;
      document.getElementById('qr-heading').textContent = closed ? 'Trạng thái QR' : 'Thanh toán QR';
      document.getElementById('qr-payment-details').hidden = closed;
      reopen.textContent = closed ? 'Kiểm tra trạng thái' : 'Mở lại / kiểm tra QR';
      for (const id of copyIds) document.getElementById(id).hidden = closed;
      document.getElementById('qr-state').textContent = states[result.state] || 'Đang kiểm tra';
      document.getElementById('qr-message').textContent = result.message; document.getElementById('qr-amount').textContent = closed ? '' : result.amountText;
      document.getElementById('qr-beneficiary').textContent = closed ? '' : `${result.bankName} · Người nhận: ${result.beneficiaryName}`;
      document.getElementById('qr-account').textContent = closed ? '' : result.virtualAccount ? `Tài khoản nhận: ${result.virtualAccount}` : 'Tài khoản nhận sẽ hiển thị sau khi ngân hàng trả QR.';
      document.getElementById('qr-content').textContent = closed ? '' : result.content;
      document.getElementById('qr-expiry').textContent = closed ? '' : `Hạn QR: ${new Date(result.expiresAtUtc).toLocaleString('vi-VN')}. Mở lại giữ nguyên mã và thời hạn.`;
      document.getElementById('qr-copy-account').disabled = !result.virtualAccount || result.state !== 2;
      document.getElementById('qr-copy-content').disabled = result.state !== 2; document.getElementById('qr-copy-amount').disabled = result.state !== 2;
      if (result.state !== 2) clearCopy();
      if (!result.qrImageReady || result.state !== 2) clearImage();
      else if (imageReference !== result.providerReference) {
        const csrf = document.querySelector('#cart-csrf input[name="__RequestVerificationToken"]')?.value;
        const response = await fetch('/shop/orders/qr/image', { method: 'POST', credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
          headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf || '' }, body: JSON.stringify({ token: secret }) });
        if (controller.signal.aborted || !latest(at, secret)) return;
        if (!response.ok || !response.headers.get('content-type')?.startsWith('image/png')) throw new Error('Chưa mở được ảnh QR; hãy kiểm tra lại cùng yêu cầu.');
        const blob = await response.blob();
        if (controller.signal.aborted || !latest(at, secret)) return;
        clearImage(); imageUrl = URL.createObjectURL(blob); imageReference = result.providerReference;
        const image = document.getElementById('qr-image'); image.src = imageUrl; image.hidden = false;
        const download = document.getElementById('qr-download'); download.href = imageUrl; download.download = `Thanh-toan-${result.providerReference}.png`; download.hidden = false;
      }
      if (result.state === 3) {
        document.getElementById('guest-payment').textContent = 'Ngân hàng đã xác minh thanh toán';
        const phase = document.getElementById('guest-phase'); if (phase.textContent === 'Chờ thanh toán') phase.textContent = 'Chờ cửa hàng chuẩn bị';
      }
      if (result.state === 4) document.getElementById('guest-payment').textContent = 'Cần đối soát thanh toán — không chuyển thêm tiền';
    } catch (error) {
      if (controller.signal.aborted || !latest(at, secret) || error.name === 'AbortError') return;
      const wasClosed = knownClosed;
      clearImage(); clearCopy(); current = null;
      for (const id of copyIds) document.getElementById(id).disabled = true;
      if (error.status === 404) reset(at);
      else { panel.hidden = false; document.getElementById('qr-message').textContent = error.message + (wasClosed ? ' QR đã đóng; cửa hàng vẫn kiểm tra tiền về muộn.' : ' Mã đơn và yêu cầu thanh toán vẫn giữ nguyên.'); }
    } finally {
      // A cancelled request from an older capability must not reset a newer request or timer.
      if (request === controller && at === generation && secret === token) {
        request = null; reopen.disabled = false; clearTimeout(timer);
        if (latest(at, secret)) timer = setTimeout(refresh, current?.state >= 3 ? 20000 : 5000);
      }
    }
  }
  async function copy(value) {
    if (!value || current?.state !== 2 || !latest(generation, token)) return;
    const at = generation, secret = token, status = document.getElementById('qr-copy-message');
    try {
      await navigator.clipboard.writeText(value);
      if (latest(at, secret) && current?.state === 2) status.textContent = 'Đã sao chép. Hãy kiểm tra lại trong ứng dụng ngân hàng.';
    } catch {
      if (!latest(at, secret) || current?.state !== 2) return;
      const input = document.getElementById('qr-copy-fallback'); input.value = value; input.hidden = false;
      document.getElementById('qr-copy-label').hidden = false; input.focus(); input.select(); status.textContent = 'Chọn và sao chép nội dung trong ô bên dưới.';
    }
  }
  reopen.addEventListener('click', refresh);
  document.getElementById('qr-copy-account').addEventListener('click', () => copy(current?.virtualAccount));
  document.getElementById('qr-copy-content').addEventListener('click', () => copy(current?.content));
  document.getElementById('qr-copy-amount').addEventListener('click', () => copy(current?.amountValue));
  window.addEventListener('storefront-guest-changing', event => reset(event.detail?.generation));
  window.addEventListener('storefront-guest-loaded', start);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
  window.addEventListener('pagehide', () => reset(window.GaoStorefrontGuest?.snapshot().generation));
  // The central loader may already have completed before this deferred asset loads.
  // Read its current capability; never start an independent request directly from a hash.
  start();
})();
