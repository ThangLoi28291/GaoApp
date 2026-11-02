(() => {
  'use strict';
  const page = document.getElementById('sf-operation-page'); const settings = document.getElementById('sf-service-setting-page');
  if (!page && !settings) return;
  const status = document.getElementById('sf-operation-status'); const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const pendingBox = document.getElementById('sf-operation-pending'); let busy = false;
  const value = (form, name) => form.elements.namedItem(name)?.value?.trim() || '';
  function integer(raw, optional = false) { if (optional && !raw) return null; const n = Number(raw); if (!Number.isInteger(n) || n < 1 || n > 2147483647) throw new Error('Chọn đúng chứng từ, người hoặc nguồn trong danh sách.'); return n; }
  function time(raw) { const date = new Date(raw); if (!raw || Number.isNaN(date.getTime())) throw new Error('Nhập thời điểm thực tế hợp lệ.'); return date.toISOString(); }
  function show(text, error = false) { status.textContent = text; status.className = error ? 'alert alert-danger' : 'alert alert-info'; }
  function lines(form, selectedOnly = false) { return [...form.querySelectorAll('[data-line-row]')].filter(row => !selectedOnly || row.querySelector('[name="receive"]')?.checked).map(row => ({ fulfillmentSourceId: integer(row.dataset.sourceId), orderLineId: integer(row.dataset.orderLineId), baseQuantity: row.querySelector('[name="baseQuantity"]').value.trim() })); }
  function carriers(form) { return [...form.querySelectorAll('[data-carrier-row]')].map(row => ({ fulfillmentSourceId: integer(row.dataset.sourceId), courierUserId: page.dataset.deliveryMethod === '0' ? integer(row.querySelector('[name="courierUserId"]').value, true) : null, carrierName: row.querySelector('[name="carrierName"]')?.value.trim() || null, trackingCode: row.querySelector('[name="trackingCode"]')?.value.trim() || null })); }
  function requestFor(form) {
    const operation = form.dataset.operation;
    if (operation === 'import-notification') return { notificationReceiptId: integer(value(form, 'notificationReceiptId')) };
    const request = { clientRequestId: crypto.randomUUID(), expectedRevision: integer(page.dataset.revision), expectedVersion: page.dataset.version, note: value(form, 'note') };
    switch (operation) {
      case 'pick': request.lines = lines(form); break;
      case 'dispatch': request.carriers = carriers(form); break;
      case 'outcome': Object.assign(request, { fulfillmentSourceId: integer(value(form, 'fulfillmentSourceId')), delivered: value(form, 'delivered') === 'true', resultOccurredAt: time(value(form, 'resultOccurredAt')), evidenceReference: value(form, 'evidenceReference') }); break;
      case 'return': request.lines = lines(form, true); if (!request.lines.length) throw new Error('Chọn dòng vừa nhận về và nhập lượng thực nhận.'); break;
      case 'incident': request.custodySourceIds = [...form.querySelectorAll('[name="custodySourceId"]:checked')].map(x => integer(x.value)); request.evidenceReference = value(form, 'evidenceReference'); break;
      case 'reconcile-incident': Object.assign(request, { incidentId: integer(form.dataset.incidentId), resolution: Number(value(form, 'resolution')), countedLines: lines(form), carriers: carriers(form), evidenceReference: value(form, 'evidenceReference') }); break;
      case 'customer-paid': Object.assign(request, { fulfillmentSourceId: integer(value(form, 'fulfillmentSourceId')), amount: value(form, 'amount'), customerPaidAt: time(value(form, 'customerPaidAt')), evidenceReference: value(form, 'evidenceReference') }); break;
      case 'bank-remittance': Object.assign(request, { globalBankReceiptId: integer(value(form, 'globalBankReceiptId')), receivingLegalEntityId: integer(value(form, 'receivingLegalEntityId')), evidenceReference: value(form, 'evidenceReference') }); break;
      case 'cash-remittance': request.posShiftCashTransactionId = integer(value(form, 'posShiftCashTransactionId')); break;
      case 'withholding': Object.assign(request, { operatingExpenseId: integer(value(form, 'operatingExpenseId')), legalEntityId: integer(value(form, 'legalEntityId')), carrierEvidenceReference: value(form, 'carrierEvidenceReference') }); break;
      case 'prepare': case 'post-sale': break;
      default: throw new Error('Thao tác chưa được hỗ trợ.');
    }
    return request;
  }
  async function post(url, request) {
    const response = await fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf }, body: JSON.stringify(request) });
    const content = await response.json().catch(() => null);
    if (!response.ok) { const error = new Error(content?.message || content?.detail || content?.title || 'Chưa ghi được thao tác; kiểm tra dữ liệu và tải lại trạng thái.'); error.status = response.status; error.retryable = content?.retryable === true; throw error; }
    return content;
  }
  function key(form) { return 'gao.storefront.staff-command.v1:' + location.pathname + ':' + form.dataset.operation; }
  function pending(form) {
    try { const raw = sessionStorage.getItem(key(form)); if (!raw) return null; if (raw.length > 2500000) return null; const stored = JSON.parse(raw); return stored?.url === endpoint(form) && stored?.request && typeof stored.request === 'object' ? stored : null; } catch { return null; }
  }
  function endpoint(form) { return form.dataset.operation === 'import-notification' ? '/admin/storefront/fulfillment/import-notification' : '/admin/storefront/fulfillment/' + integer(page.dataset.orderId) + '/' + form.dataset.operation; }
  async function execute(form, stored) {
    if (busy) return; busy = true; const button = form.querySelector('button[type="submit"]'); button.disabled = true; show('Đang ghi và kiểm tra nguồn chứng từ…');
    try {
      await post(stored.url, stored.request); try { sessionStorage.removeItem(key(form)); } catch { }
      show('Đã ghi nhận. Đang cập nhật trạng thái thực tế…'); location.reload();
    } catch (problem) {
      if ([400, 401, 403, 404, 409, 422].includes(problem.status) && !problem.retryable) { try { sessionStorage.removeItem(key(form)); } catch { } }
      show(problem.message + (problem.status ? '' : ' Kết quả chưa rõ; dùng Kiểm tra lại thao tác để gửi đúng mã cũ.'), true);
      if (['dispatch', 'reconcile-incident'].includes(form.dataset.operation))
        status.textContent += ' Nếu hàng đã rời kho, dừng bàn giao và kiểm tra trạng thái/chứng từ; ghi sự cố để đối soát nếu lượng thực tế chưa khớp. Không chuyển hàng lần nữa khi kiểm tra lại yêu cầu cũ.';
      refreshPending();
    } finally { busy = false; button.disabled = false; }
  }
  function refreshPending() {
    if (!page) return; pendingBox.replaceChildren();
    for (const form of page.querySelectorAll('form[data-operation]')) {
      const stored = pending(form); if (!stored) continue;
      const note = document.createElement('p'); note.textContent = 'Có thao tác đã gửi nhưng chưa nhận rõ kết quả. Kiểm tra lại đúng yêu cầu cũ trước khi nhập thao tác mới.';
      const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-warning'; button.textContent = 'Kiểm tra lại thao tác'; button.addEventListener('click', () => execute(form, stored));
      pendingBox.append(note, button);
    }
    pendingBox.hidden = !pendingBox.childElementCount;
  }
  if (page) {
    for (const input of page.querySelectorAll('input[type="datetime-local"]')) {
      input.step = '0.001';
      const now = new Date();
      input.value = new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, -1);
      const update = document.createElement('button'); update.type = 'button'; update.className = 'btn btn-outline-secondary';
      update.textContent = 'Lấy thời điểm hiện tại'; update.addEventListener('click', () => {
        const current = new Date(); input.value = new Date(current.getTime() - current.getTimezoneOffset() * 60000).toISOString().slice(0, -1);
      });
      input.after(update);
    }
    for (const form of page.querySelectorAll('form[data-operation]')) form.addEventListener('submit', event => {
      event.preventDefault(); if (busy || !form.reportValidity()) return;
      if (pending(form)) { show('Thao tác trước chưa rõ kết quả. Dùng nút Kiểm tra lại thao tác với cùng nội dung và mã yêu cầu.', true); refreshPending(); return; }
      try { const stored = { url: endpoint(form), request: requestFor(form) }; sessionStorage.setItem(key(form), JSON.stringify(stored)); execute(form, stored); }
      catch (problem) { show(problem.message || 'Trình duyệt chưa giữ được mã yêu cầu; chưa gửi thao tác.', true); }
    });
    refreshPending();
  }
  if (settings) {
    const form = document.getElementById('sf-service-setting-form'); form.addEventListener('submit', async event => {
      event.preventDefault(); if (busy || !form.reportValidity()) return; busy = true;
      try { await post('/admin/storefront/service-revenue', { version: form.dataset.settingVersion || null, legalEntityId: integer(value(form, 'legalEntityId')), taxId: integer(value(form, 'taxId')), name: value(form, 'name'), invoiceRoute: Number(value(form, 'invoiceRoute')) }); show('Đã lưu cấu hình nguồn dịch vụ.'); location.reload(); }
      catch (problem) { show(problem.message, true); } finally { busy = false; }
    });
  }
})();
