(() => {
  'use strict';
  const panel = document.getElementById('guest-cancellation');
  if (!panel || !window.GaoStorefrontGuest) return;
  const form = document.getElementById('guest-cancellation-form');
  const reason = document.getElementById('guest-cancellation-reason');
  const confirmation = document.getElementById('guest-cancellation-confirm');
  const status = document.getElementById('guest-cancellation-status');
  const pendingBox = document.getElementById('guest-cancellation-pending');
  const retry = document.getElementById('guest-cancellation-retry');
  const refreshButton = document.getElementById('guest-cancellation-refresh');
  const submit = document.getElementById('guest-cancellation-submit');
  const prefix = 'gao.storefront.cancel.v1:' + document.body.dataset.cartScope + ':';
  let token = '', generation = -1, capabilityHash = '', view = null, read = null, pending = null, busy = false;

  function latest(at, secret) {
    const current = window.GaoStorefrontGuest.snapshot();
    return current.loaded && current.generation === at && current.token === secret && generation === at && token === secret;
  }
  function node(tag, text, cls) {
    const item = document.createElement(tag);
    if (text !== undefined) item.textContent = text;
    if (cls) item.className = cls;
    return item;
  }
  function show(text, failed = false) {
    status.textContent = text; status.hidden = !text;
    status.className = 'sf-control-notice' + (failed ? ' is-error' : '');
  }
  function reset(nextGeneration) {
    read?.abort(); read = null; token = ''; capabilityHash = ''; view = null; pending = null;
    generation = Number.isSafeInteger(nextGeneration) ? nextGeneration : generation + 1;
    panel.hidden = true; panel.setAttribute('aria-busy', 'false'); form.hidden = true; pendingBox.hidden = true;
    reason.value = ''; confirmation.checked = false; show('');
    document.getElementById('guest-cancellation-message').textContent = '';
    document.getElementById('guest-cancellation-history').replaceChildren();
    document.getElementById('guest-refund-detail').replaceChildren();
  }
  function controls() {
    form.hidden = !view || !capabilityHash || !!pending || !(view.canSelfCancel || view.canRequestCancellation);
    submit.textContent = view?.canSelfCancel ? 'Hủy đơn hàng' : 'Gửi yêu cầu hủy';
    submit.disabled = busy || !!read || !capabilityHash;
    reason.disabled = confirmation.disabled = busy;
    retry.disabled = busy || !pending;
    refreshButton.disabled = busy || !!read;
    pendingBox.hidden = !pending;
    document.getElementById('guest-cancellation-explanation').textContent = view?.canSelfCancel
      ? 'Đơn sẽ được hủy nếu vẫn chưa bắt đầu chuẩn bị. Tiền đã trả được cửa hàng xử lý hoàn riêng.'
      : 'Cửa hàng kiểm tra hàng thực tế trước khi quyết định. Gửi yêu cầu chưa có nghĩa là đơn đã hủy.';
  }
  function render(state) {
    if (view && view.canSelfCancel !== state.canSelfCancel) confirmation.checked = false;
    view = state; panel.hidden = false;
    document.getElementById('guest-phase').textContent = window.GaoStorefrontGuest.phaseLabel(state.phase);
    document.getElementById('guest-refund').textContent = window.GaoStorefrontGuest.refundLabel(state.refundState);
    document.getElementById('guest-cancellation-message').textContent = state.message;
    const history = document.getElementById('guest-cancellation-history'); history.replaceChildren();
    for (const item of state.history || []) {
      const row = node('li'); row.append(node('strong', item.status));
      const time = node('time', new Date(item.atUtc).toLocaleString('vi-VN')); time.dateTime = item.atUtc;
      row.append(time, node('p', item.reason)); history.append(row);
    }
    if (!history.childElementCount) history.append(node('li', 'Chưa có yêu cầu hủy.'));
    const refunds = document.getElementById('guest-refund-detail'); refunds.replaceChildren();
    for (const item of state.refunds || []) {
      const card = node('article', undefined, 'sf-control-refund'); card.append(node('strong', item.status));
      const amounts = node('dl');
      amounts.append(node('dt', 'Khoản cần hoàn'), node('dd', item.amountText), node('dt', 'Đã xác nhận thực hoàn'), node('dd', item.executedAmountText));
      card.append(amounts, node('p', item.message)); refunds.append(card);
    }
    if (!refunds.childElementCount) refunds.append(node('p', 'Chưa có khoản hoàn được xác định. Nếu tiền về sau khi hủy, cửa hàng tiếp tục đối soát.'));
    controls();
  }
  async function post(path, payload, signal) {
    const csrf = document.querySelector('#cart-csrf input[name="__RequestVerificationToken"]')?.value;
    if (!csrf) throw new Error('Phiên xem đã thay đổi. Hãy tải lại trang bằng liên kết riêng.');
    const response = await fetch(path, { method: 'POST', credentials: 'same-origin', cache: 'no-store', signal,
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf }, body: JSON.stringify(payload) });
    const data = await response.json().catch(() => null);
    if (!response.ok) {
      const problem = new Error(data?.message || data?.detail || data?.title || (response.status === 429 ? 'Bạn thao tác hơi nhanh. Hãy thử lại sau một phút.' : 'Chưa kiểm tra được yêu cầu hủy.'));
      problem.status = response.status; problem.retryable = data?.retryable === true; throw problem;
    }
    if (!data) throw new Error('Chưa đọc được kết quả. Hãy kiểm tra lại đúng yêu cầu cũ.');
    return data;
  }
  function restorePending() {
    pending = null;
    try {
      const raw = sessionStorage.getItem(prefix + capabilityHash);
      if (!raw || raw.length > 12000) return;
      const item = JSON.parse(raw);
      if (item?.capabilityHash !== capabilityHash || !/^[a-f0-9-]{36}$/i.test(item.clientRequestId || '') ||
          !Number.isSafeInteger(item.expectedRevision) || item.expectedRevision < 1 ||
          typeof item.expectedVersion !== 'string' || !/^[A-Za-z0-9+/]{11}=$/.test(item.expectedVersion) ||
          typeof item.reason !== 'string' || !item.reason.trim() || item.reason.length > 1000) return;
      pending = { capabilityHash, clientRequestId: item.clientRequestId, expectedRevision: item.expectedRevision,
        expectedVersion: item.expectedVersion, reason: item.reason, note: 'Khách xác nhận yêu cầu hủy trên website.' };
    } catch { /* Storage failure never creates a substitute cancellation command. */ }
  }
  async function refresh() {
    if (!token || !latest(generation, token) || read || busy) return;
    const at = generation, secret = token, controller = new AbortController(); read = controller;
    panel.hidden = false; panel.setAttribute('aria-busy', 'true'); controls();
    try {
      const state = await post('/shop/orders/cancellation/access', { token: secret }, controller.signal);
      if (controller.signal.aborted || !latest(at, secret)) return;
      render(state); show('');
    } catch (error) {
      if (!controller.signal.aborted && latest(at, secret)) show(error.message || 'Chưa tải được trạng thái hủy / hoàn tiền.', true);
    } finally {
      if (read === controller && latest(at, secret)) { read = null; panel.setAttribute('aria-busy', 'false'); controls(); }
    }
  }
  async function start() {
    const current = window.GaoStorefrontGuest.snapshot();
    if (!current.loaded || !/^[A-Za-z0-9_-]{43}$/.test(current.token)) { reset(current.generation); return; }
    reset(current.generation); token = current.token;
    const at = generation, secret = token;
    try {
      // Only a capability digest indexes the pending command; the token stays in memory/fragment.
      const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(secret));
      if (!latest(at, secret)) return;
      capabilityHash = [...new Uint8Array(bytes)].map(value => value.toString(16).padStart(2, '0')).join('');
      restorePending(); await refresh();
    } catch {
      if (latest(at, secret)) { panel.hidden = false; show('Trình duyệt chưa sẵn sàng bảo vệ mã yêu cầu. Hãy tải lại trang trong kết nối an toàn.', true); }
    }
  }
  async function execute(record) {
    if (busy || !record || record.capabilityHash !== capabilityHash || !latest(generation, token)) return;
    const at = generation, secret = token, key = prefix + capabilityHash;
    const controller = new AbortController(); const deadline = setTimeout(() => controller.abort(), 20000);
    busy = true; controls(); show('Đang kiểm tra và ghi nhận yêu cầu…');
    try {
      // Navigation never replaces this already dispatched command with a fresh one.
      const ack = await post('/shop/orders/cancellation', { token: secret, clientRequestId: record.clientRequestId,
        expectedRevision: record.expectedRevision, expectedVersion: record.expectedVersion, reason: record.reason, note: record.note }, controller.signal);
      if (!Number.isSafeInteger(ack.revision) || ack.revision < 1 || typeof ack.version !== 'string')
        throw new Error('Chưa đọc được xác nhận. Hãy kiểm tra lại đúng yêu cầu cũ.');
      sessionStorage.removeItem(key);
      if (!latest(at, secret)) return;
      pending = null; reason.value = ''; confirmation.checked = false;
      show('Đã ghi nhận. Đang cập nhật trạng thái đơn…');
      document.getElementById('guest-refresh').click();
    } catch (error) {
      const definitive = [400, 401, 403, 404, 409, 422].includes(error.status) && !error.retryable;
      if (definitive) { try { sessionStorage.removeItem(key); } catch { } }
      if (!latest(at, secret)) return;
      if (definitive) pending = null;
      show((error.name === 'AbortError' ? 'Chưa nhận rõ kết quả.' : error.message || 'Kết quả chưa rõ.') +
        (definitive ? ' Cập nhật trạng thái rồi kiểm tra lại trước khi gửi yêu cầu mới.' : ' Dùng Kiểm tra lại yêu cầu hủy để gửi đúng mã cũ.'), true);
    } finally {
      clearTimeout(deadline); busy = false;
      // A different selected order may have finished loading while this command
      // was pending. Its read was deferred; resume that read without reissuing a command.
      if (token && capabilityHash && latest(generation, token) && !view && !read) refresh();
      else controls();
    }
  }
  form.addEventListener('submit', event => {
    event.preventDefault();
    if (busy || !view || !latest(generation, token) || !form.reportValidity()) return;
    if (pending) { show('Hãy kiểm tra lại yêu cầu cũ trước khi gửi lần khác.', true); return; }
    try {
      const record = { capabilityHash, clientRequestId: crypto.randomUUID(), expectedRevision: view.revision,
        expectedVersion: view.version, reason: reason.value.trim(), note: 'Khách xác nhận yêu cầu hủy trên website.' };
      if (!record.reason) { reason.focus(); return; }
      const raw = JSON.stringify(record); sessionStorage.setItem(prefix + capabilityHash, raw);
      if (sessionStorage.getItem(prefix + capabilityHash) !== raw) throw new Error('Không giữ được mã yêu cầu.');
      pending = record; execute(record);
    } catch { show('Trình duyệt chưa lưu được mã yêu cầu; chưa gửi hủy đơn. Hãy kiểm tra quyền lưu trữ hoặc tải lại trang.', true); }
  });
  retry.addEventListener('click', () => execute(pending));
  refreshButton.addEventListener('click', refresh);
  window.addEventListener('storefront-guest-changing', event => reset(event.detail?.generation));
  window.addEventListener('storefront-guest-loaded', start);
  window.addEventListener('pagehide', () => reset());
  start();
})();
