(() => {
  'use strict';
  const page = document.getElementById('sf-control-page');
  if (!page) return;
  const status = document.getElementById('sf-control-status');
  const pendingBox = document.getElementById('sf-control-pending');
  const csrf = page.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const orderId = Number(page.dataset.orderId), actorId = Number(page.dataset.actorId);
  const initialPath = location.pathname;
  const prefix = `gao.storefront.control.v1:${page.dataset.storeId}:${actorId}:${orderId}:`;
  const routes = Object.freeze({ 'cancellation-approve': '/cancellation/approve', 'cancellation-reject': '/cancellation/reject',
    'cancellation-acknowledge': '/cancellation/acknowledge-delivery', 'refund-approve': '/approve', 'refund-reject': '/reject', 'refund-confirm': '/confirm-execution' });
  const names = Object.freeze({ 'cancellation-approve': 'Duyệt hủy / thu hồi', 'cancellation-reject': 'Quyết định yêu cầu hủy',
    'cancellation-acknowledge': 'Xử lý xung đột giao / hủy', 'refund-approve': 'Duyệt khoản hoàn', 'refund-reject': 'Đối soát khoản hoàn', 'refund-confirm': 'Xác nhận thực hoàn' });
  let busy = false;
  function integer(raw) {
    const value = Number(raw);
    if (!Number.isInteger(value) || value < 1 || value > 2147483647) throw new Error('Chọn đúng hồ sơ hoặc chứng từ trong danh sách.');
    return value;
  }
  function value(form, name) { return form.elements.namedItem(name)?.value?.trim() || ''; }
  function current() { return document.getElementById('sf-control-page') === page && location.pathname === initialPath; }
  function show(text, failed = false) {
    status.textContent = text; status.hidden = !text;
    status.className = 'sf-control-notice' + (failed ? ' sf-control-error' : '');
  }
  function endpoint(operation) {
    if (!Object.hasOwn(routes, operation)) throw new Error('Thao tác không hợp lệ.');
    return '/admin/storefront/refunds/' + integer(orderId) + routes[operation];
  }
  function lockButtons() {
    page.querySelectorAll('button[type="submit"], [data-retry-command]').forEach(button => { button.disabled = busy; });
    document.getElementById('sf-control-refresh').disabled = busy;
  }
  function recordFor(form, operation) {
    const request = { clientRequestId: crypto.randomUUID(), expectedRevision: integer(page.dataset.revision),
      expectedVersion: page.dataset.version, note: 'Nhân viên xử lý hồ sơ hủy / hoàn website.' };
    let target;
    if (operation.startsWith('cancellation-')) {
      target = integer(form.dataset.requestId); request.cancellationRequestId = target; request.reason = value(form, 'reason');
    } else {
      target = integer(form.dataset.refundId); request.refundId = target;
      if (operation !== 'refund-confirm') request.reason = value(form, 'reason');
      else {
        const selected = form.elements.namedItem('source')?.selectedOptions?.[0];
        if (!selected?.value) throw new Error('Tải và chọn chứng từ đã thực chi.');
        request.sourceKind = integer(selected.dataset.kind); request.sourceId = integer(selected.dataset.id);
        request.sourceVersion = selected.dataset.version; request.payerLegalEntityId = integer(value(form, 'payerLegalEntityId'));
        request.payerBankAccountId = selected.dataset.fund === 'bank' ? integer(value(form, 'payerBankAccountId')) : null;
        request.payerBankAccountVersion = selected.dataset.fund === 'bank' ? form.elements.namedItem('payerBankAccountId')?.selectedOptions?.[0]?.dataset.version : null;
        if (selected.dataset.fund === 'bank' && !/^[A-Za-z0-9+/]{11}=$/.test(request.payerBankAccountVersion || '')) throw new Error('Tải lại hồ sơ để chọn đúng phiên bản tài khoản đã thực chuyển.');
        request.externalTransactionId = value(form, 'externalTransactionId'); request.evidenceReference = value(form, 'evidenceReference');
        request.executedAt = null;
        if (request.sourceKind === 2) {
          const raw = value(form, 'executedAt'), time = new Date(raw);
          if (!raw || Number.isNaN(time.getTime())) throw new Error('Nhập thời điểm thực chi đã kiểm tra.');
          request.executedAt = time.toISOString();
        }
      }
    }
    return { operation, target, orderId, actorId, request };
  }
  function keyFor(record) { return prefix + record.operation + ':' + record.target; }
  function valid(record) {
    return record && record.orderId === orderId && record.actorId === actorId && Object.hasOwn(routes, record.operation) &&
      Number.isInteger(record.target) && record.target > 0 && record.request &&
      /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(record.request.clientRequestId || '') &&
      Number.isInteger(record.request.expectedRevision) && record.request.expectedRevision > 0 &&
      /^[A-Za-z0-9+/]{11}=$/.test(record.request.expectedVersion || '');
  }
  function stored(key) {
    try { const raw = sessionStorage.getItem(key); if (!raw || raw.length > 20000) return null; const record = JSON.parse(raw); return valid(record) && keyFor(record) === key ? record : null; }
    catch { return null; }
  }
  function refreshPending() {
    pendingBox.replaceChildren(); let found = 0;
    try {
      // Independent of currently rendered forms: a committed command may have removed its form.
      const keys = Array.from({ length: sessionStorage.length }, (_, index) => sessionStorage.key(index)).filter(key => key?.startsWith(prefix));
      for (const key of keys) {
        const record = stored(key); if (!record) continue;
        if (++found > 200) break;
        const row = document.createElement('section'); row.className = 'sf-control-pending-item';
        const title = document.createElement('h3'); title.textContent = names[record.operation] + ' · Lần gửi chưa rõ kết quả';
        const text = document.createElement('p'); text.textContent = 'Kiểm tra lại đúng nội dung và mã yêu cầu cũ. Không lập khoản chi hoặc thao tác hủy lần nữa.';
        const button = document.createElement('button'); button.type = 'button'; button.className = 'sf-control-button'; button.dataset.retryCommand = 'true';
        button.textContent = 'Kiểm tra lại thao tác'; button.disabled = busy; button.addEventListener('click', () => execute(record, key));
        row.append(title, text, button); pendingBox.append(row);
      }
    } catch { /* No alternate financial command is generated when storage is unavailable. */ }
    pendingBox.hidden = !pendingBox.childElementCount;
    return found;
  }
  async function responseData(response) {
    const data = await response.json().catch(() => null);
    if (!response.ok) {
      const error = new Error(data?.message || data?.detail || data?.title || 'Chưa xử lý được thao tác. Hãy kiểm tra lại hồ sơ.');
      error.status = response.status; error.retryable = data?.retryable === true; throw error;
    }
    if (!data) throw new Error('Chưa đọc được kết quả; giữ mã yêu cầu cũ để kiểm tra lại.');
    return data;
  }
  async function execute(record, key) {
    if (busy || !current() || !valid(record) || keyFor(record) !== key) return;
    if (!csrf) { show('Phiên làm việc đã thay đổi. Tải lại hồ sơ trước khi thao tác.', true); return; }
    busy = true; lockButtons(); show('Đang kiểm tra nguồn và ghi nhận thao tác…');
    const controller = new AbortController(); const deadline = setTimeout(() => controller.abort(), 20000);
    try {
      const response = await fetch(endpoint(record.operation), { method: 'POST', credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf }, body: JSON.stringify(record.request) });
      const ack = await responseData(response);
      if (ack.orderId !== orderId || !Number.isInteger(ack.revision) || ack.revision < 1 || typeof ack.version !== 'string')
        throw new Error('Chưa nhận được xác nhận đầy đủ. Kiểm tra lại đúng mã yêu cầu cũ.');
      sessionStorage.removeItem(key);
      if (current()) { show('Đã ghi nhận. Đang tải lại hồ sơ thực tế…'); location.reload(); }
    } catch (error) {
      if ([400, 401, 403, 404, 409, 422].includes(error.status) && !error.retryable) { try { sessionStorage.removeItem(key); } catch { } }
      if (current()) {
        show((error.name === 'AbortError' ? 'Chưa nhận rõ kết quả.' : error.message || 'Kết quả chưa rõ.') +
          (error.status && !error.retryable && error.status < 500 ? ' Cập nhật hồ sơ trước khi thử thao tác mới.' : ' Dùng Kiểm tra lại thao tác với đúng mã cũ.'), true);
        refreshPending();
      }
    } finally { clearTimeout(deadline); busy = false; if (current()) lockButtons(); }
  }
  function sourceChanged(form) {
    const selected = form.elements.namedItem('source').selectedOptions[0];
    const bank = selected?.dataset.fund === 'bank', manual = selected?.dataset.kind === '2';
    const bankInput = form.elements.namedItem('payerBankAccountId'), timeInput = form.elements.namedItem('executedAt');
    form.querySelector('[data-bank-field]').hidden = !bank; bankInput.disabled = !bank; bankInput.required = bank;
    if (!bank) bankInput.value = '';
    form.querySelector('[data-bank-time]').hidden = !manual; timeInput.disabled = !manual; timeInput.required = manual;
    if (!manual) timeInput.value = '';
  }
  for (const form of page.querySelectorAll('form[data-operation]')) {
    form.addEventListener('submit', event => {
      event.preventDefault(); if (busy || !form.reportValidity()) return;
      if (refreshPending() > 0) { show('Còn thao tác chưa rõ kết quả. Kiểm tra lại yêu cầu cũ trước khi gửi thao tác khác.', true); return; }
      const operation = event.submitter?.dataset.operation || form.dataset.operation;
      try {
        const record = recordFor(form, operation), key = keyFor(record);
        if (stored(key)) { show('Lần gửi trước chưa rõ kết quả. Dùng Kiểm tra lại thao tác trước khi tạo yêu cầu khác.', true); refreshPending(); return; }
        const raw = JSON.stringify(record); sessionStorage.setItem(key, raw);
        if (sessionStorage.getItem(key) !== raw) throw new Error('Chưa giữ được mã yêu cầu; chưa gửi thao tác.');
        execute(record, key); refreshPending();
      } catch (error) { show(error.message || 'Trình duyệt chưa giữ được mã yêu cầu; chưa gửi thao tác.', true); }
    });
    const sourceButton = form.querySelector('[data-load-payouts]');
    if (!sourceButton) continue;
    const select = form.elements.namedItem('source'); select.addEventListener('change', () => sourceChanged(form));
    sourceButton.addEventListener('click', async () => {
      if (busy || sourceButton.disabled) return;
      sourceButton.disabled = true;
      const message = form.querySelector('[data-payout-message]'); message.textContent = 'Đang kiểm tra các chứng từ thực chi…';
      const controller = new AbortController(); const deadline = setTimeout(() => controller.abort(), 20000);
      try {
        const response = await fetch('/admin/storefront/refunds/' + integer(orderId) + '/payout-sources?refundId=' + integer(form.dataset.refundId),
          { credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
        const sources = await responseData(response);
        if (!current() || !document.contains(form)) return;
        if (!Array.isArray(sources)) throw new Error('Chưa đọc được danh sách chứng từ.');
        select.replaceChildren(new Option('Chọn đúng chứng từ đã thực trả khách', ''));
        for (const source of sources) {
          const option = new Option(source.label + ' · ' + source.amountText, source.kind + ':' + source.id);
          option.dataset.kind = String(source.kind); option.dataset.id = String(source.id); option.dataset.version = source.version; option.dataset.fund = source.fund;
          select.append(option);
        }
        select.disabled = !sources.length; sourceChanged(form);
        message.textContent = sources.length ? 'Chọn cả chứng từ thực chi. Số tiền được đối chiếu lại khi xác nhận.' : 'Chưa có chứng từ thực chi phù hợp còn có thể chọn. Ghi nhận khoản đã trả tại chức năng thu / chi trước.';
      } catch (error) { if (current()) message.textContent = error.name === 'AbortError' ? 'Chưa tải được chứng từ. Bạn có thể thử lại.' : error.message; }
      finally { clearTimeout(deadline); sourceButton.disabled = false; }
    });
  }
  document.getElementById('sf-control-refresh').addEventListener('click', () => { if (!busy) location.reload(); });
  window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
  refreshPending();
})();
