(() => {
    'use strict';
    function init() {
    const root = document.getElementById('cashAdjustments');
    if (!root || root.dataset.initialized === 'true') return;
    root.dataset.initialized = 'true';
    const $ = id => document.getElementById(id), base = '/admin/pos-shift/cash-adjustments';
    const isAdmin = root.dataset.admin === 'true', dialog = $('caDialog');
    const url = new URL(location.href);
    let transactionId = Number(url.searchParams.get('transactionId')) || null;
    let shiftId = Number(url.searchParams.get('shiftId')) || null;
    let tab = transactionId || !isAdmin ? 'transactions' : 'requests';
    if (['requests','transactions'].includes(url.searchParams.get('view'))) tab = url.searchParams.get('view');
    let page = 1, pages = 1, rows = [], busy = false, listController, sequence = 0, detailSequence = 0;
    const names = { Pending: 'Chờ duyệt', Approved: 'Đã duyệt', Rejected: 'Từ chối', Withdrawn: 'Đã rút' };
    const money = n => n == null ? 'Chưa ghi nhận' : Number(n).toLocaleString('vi-VN', { maximumFractionDigits: 2 }) + ' đ';
    const signed = n => (n > 0 ? '+' : '') + money(n);
    const date = value => value ? new Date(/Z$|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z').toLocaleString('vi-VN') : '—';
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const type = v => v === 'CashIn' ? 'Thu' : 'Chi';
    const badge = r => `<span class="badge bg-label-${r.status === 'Approved' ? 'success' : r.status === 'Pending' ? 'warning' : 'secondary'}">${names[r.status] || '—'}</span>${r.appliedToClosedShift && !r.reconciledAtUtc ? '<small class="text-warning">Cần đối soát lại</small>' : ''}`;
    const valueBox = (v, title, cancel = false) => `<div class="ca-box ${title === 'Đề nghị' ? 'proposed' : ''}"><h3>${title}</h3><b>${cancel ? 'Hủy phiếu' : type(v.type)}</b><div class="ca-amount">${cancel ? '0 đ' : money(v.amount)}</div><div class="ca-detail-note">${esc(v.reason)}</div><small class="ca-detail-note">${esc(v.note)}</small></div>`;
    function message(text, error = false) { const e = $('caMessage'); e.hidden = !text; e.className = `alert alert-${error ? 'danger' : 'success'}`; e.textContent = text; }
    function modalError(text) { $('caDialogError').hidden = !text; $('caDialogError').textContent = text; }
    async function api(path, body, signal) {
        const c = new AbortController(), timer = setTimeout(() => c.abort(), 20000);
        const abort = () => c.abort(); signal?.addEventListener('abort', abort, { once: true });
        try {
            const response = await fetch(base + path, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', signal: c.signal,
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: root.querySelector('[name="__RequestVerificationToken"]').value },
                body: body === undefined ? undefined : JSON.stringify(body) });
            const json = await response.json().catch(() => ({}));
            if (!response.ok || response.redirected) throw new Error(json.message || json.detail || 'Không thể xử lý. Hãy kiểm tra đăng nhập và tải lại dữ liệu.');
            return json;
        } finally { clearTimeout(timer); signal?.removeEventListener('abort', abort); }
    }
    function show(title, html) {
        $('caDialogTitle').textContent = title; $('caDialogBody').innerHTML = html; modalError('');
        if (!dialog.open) dialog.showModal();
        dialog.scrollTop = 0;
    }
    function setBusy(value) { busy = value; dialog.querySelectorAll('button,input,select,textarea').forEach(el => el.disabled = value); }
    dialog.addEventListener('cancel', e => { if (busy) e.preventDefault(); });
    dialog.addEventListener('close', () => { detailSequence++; });
    $('caClose').onclick = () => { if (!busy) dialog.close(); };
    async function load() {
        const seq = ++sequence; listController?.abort(); listController = new AbortController();
        root.querySelectorAll('[data-tab]').forEach(b => { b.classList.toggle('active', b.dataset.tab === tab); b.setAttribute('aria-pressed', String(b.dataset.tab === tab)); });
        $('caStatusLabel').hidden = tab !== 'requests'; $('caKeywordLabel').hidden = tab !== 'transactions';
        $('caScope').textContent = [shiftId ? `Ca #${shiftId}` : '', transactionId && tab === 'transactions' ? `Phiếu #${transactionId}` : ''].filter(Boolean).join(' · ');
        const params = new URLSearchParams({ page });
        if (shiftId) params.set('shiftId', shiftId);
        if (tab === 'requests') params.set('status', $('caStatus').value);
        else { params.set('keyword', $('caKeyword').value); if (transactionId) params.set('transactionId', transactionId); }
        $('caPrev').disabled = $('caNext').disabled = true;
        $('caRows').innerHTML = '<tr><td colspan="6">Đang tải…</td></tr>';
        $('caHead').innerHTML = tab === 'requests' ? '<tr><th>Phiếu / nhân viên</th><th>Thay đổi</th><th>Trạng thái</th><th></th></tr>' : '<tr><th>Nội dung</th><th>Thu / chi</th><th>Trạng thái</th><th></th></tr>';
        try {
            const data = await api(`/${tab === 'requests' ? 'data' : 'transactions'}?${params}`, undefined, listController.signal);
            if (seq !== sequence) return;
            rows = data.items; pages = Math.max(1, data.totalPages);
            $('caRows').innerHTML = rows.map(r => tab === 'requests'
                ? `<tr><td><b>Phiếu #${r.transactionId}</b><small>${esc(r.requestedBy)} · ${esc(r.shiftCode)}</small></td><td>${type(r.before.type)} ${money(r.before.amount)} → <b>${r.isCancellation ? 'Hủy phiếu' : type(r.after.type) + ' ' + money(r.after.amount)}</b></td><td>${badge(r)}</td><td><button class="btn btn-outline-primary btn-sm" data-detail="${r.id}">Xem yêu cầu</button></td></tr>`
                : `<tr><td><b>${esc(r.values.reason)}</b><small>#${r.id} · ${date(r.createdAtUtc)}</small></td><td><b class="text-${r.values.type === 'CashIn' ? 'success' : 'danger'}">${type(r.values.type)} ${money(r.values.amount)}</b></td><td>${r.cancelled ? 'Đã hủy' : r.pendingRequestId ? '<span class="badge bg-label-warning">Chờ duyệt điều chỉnh</span>' : 'Đã ghi nhận'}</td><td>${r.cancelled ? '—' : r.pendingRequestId ? `<button class="btn btn-outline-primary btn-sm" data-detail="${r.pendingRequestId}">Xem yêu cầu</button>` : `<button class="btn btn-outline-primary btn-sm" data-create="${r.id}">Sửa / hủy phiếu</button>`}</td></tr>`).join('') || '<tr><td colspan="4" class="text-center p-5">Không có dữ liệu phù hợp.</td></tr>';
            $('caCount').textContent = `${data.totalItems.toLocaleString('vi-VN')} ${tab === 'requests' ? 'yêu cầu' : 'phiếu'}`;
            $('caPage').textContent = `${page} / ${pages}`;
            $('caPrev').disabled = page <= 1; $('caNext').disabled = page >= pages;
        } catch (e) { if (seq === sequence) { $('caRows').innerHTML = '<tr><td colspan="6">Không tải được dữ liệu. Bấm Tải lại để thử lại.</td></tr>'; message(e.name === 'AbortError' ? 'Kết nối quá thời gian chờ.' : e.message, true); } }
    }
    function guid() { return '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, c => (Number(c) ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> Number(c) / 4).toString(16)); }
    function create(r) {
        const clientRequestId = guid();
        show(`Điều chỉnh phiếu #${r.id}`, `<p>${esc(r.terminal)} · ${esc(r.shiftCode)}</p>${valueBox(r.values, 'Đang ghi nhận')}
            <form id="caCreateForm"><label>Yêu cầu<select id="caKind" class="form-select"><option value="edit">Sửa thu/chi</option><option value="cancel">Hủy phiếu thu/chi</option></select></label>
            <div id="caEditFields"><div class="ca-compare"><label>Loại nghiệp vụ<select id="caType" class="form-select"><option value="1" ${r.values.type === 'CashIn' ? 'selected' : ''}>Thu tiền</option><option value="2" ${r.values.type === 'CashOut' ? 'selected' : ''}>Chi tiền</option></select></label>
            <label>Số tiền đề nghị (đ)<input id="caAmount" class="form-control" type="number" min="0.01" max="9999999999999.99" step="0.01" required value="${r.values.amount}"></label></div>
            <label>Nội dung thu/chi<input id="caReason" class="form-control" maxlength="300" required value="${esc(r.values.reason)}"></label>
            <label>Ghi chú<textarea id="caNote" class="form-control" maxlength="500">${esc(r.values.note)}</textarea></label></div>
            <div id="caDelta" class="ca-delta"></div><label>Lý do đề nghị <span class="text-danger">Bắt buộc</span><textarea id="caRequestReason" class="form-control" required maxlength="500" placeholder="Ví dụ: nhập nhầm thu tiền thành chi tiền…"></textarea></label>
            <div class="alert alert-info">Số liệu hiện tại được giữ nguyên cho đến khi Admin duyệt. Phiếu hủy vẫn được lưu trong lịch sử.</div>
            <div class="ca-actions"><button type="submit" class="btn btn-primary">Gửi yêu cầu</button></div></form>`);
        function preview() {
            const cancel = $('caKind').value === 'cancel'; $('caEditFields').hidden = cancel;
            ['caAmount', 'caReason'].forEach(id => $(id).required = !cancel);
            const before = r.values.amount * (r.values.type === 'CashIn' ? 1 : -1);
            const after = cancel ? 0 : Number($('caAmount').value) * ($('caType').value === '1' ? 1 : -1);
            $('caDelta').textContent = `Tiền dự kiến cuối ca sẽ thay đổi: ${signed(after - before)}`;
        }
        ['caKind', 'caType', 'caAmount'].forEach(id => $(id).addEventListener('input', preview)); preview();
        $('caCreateForm').onsubmit = async e => {
            e.preventDefault(); if (busy) return;
            const input = { clientRequestId, rowVersion: r.rowVersion, isCancellation: $('caKind').value === 'cancel', type: Number($('caType').value), amount: Number($('caAmount').value), reason: $('caReason').value, note: $('caNote').value, requestReason: $('caRequestReason').value };
            setBusy(true); modalError('');
            let result;
            try { result = await api(`/transactions/${r.id}/requests`, input); } catch (err) { modalError(err.message); } finally { setBusy(false); }
            if (result) { message('Đã gửi yêu cầu. Số liệu thu/chi chưa thay đổi.'); tab = 'requests'; $('caStatus').value = 'Pending'; page = 1; await load(); await detail(result.id); }
        };
    }
    function position(p) {
        return `<div class="ca-position">${[['Tổng thu thêm', p.cashInTotal], ['Tổng chi', p.cashOutTotal], ['Tiền dự kiến', p.expected], ['Tiền thực đếm', p.actual], ['Tiền Admin đã nhận', p.received], ['Thực đếm − dự kiến', p.difference], ['Đã nhận − dự kiến', p.receivedDifference]].map(([name, n]) => `<div>${name}<strong>${money(n)}</strong></div>`).join('')}</div>`;
    }
    function negativeWarning(d) {
        const pending = d.request.status === 'Pending', expected = pending ? d.proposedShift.expected : d.currentShift.expected;
        if (expected >= 0) return '';
        return `<section id="caNegativeExpectedWarning" class="ca-negative-warning" role="alert">
            <h3>${pending ? 'Lưu ý trước khi duyệt: tiền dự kiến sẽ âm' : 'Cần đối soát: tiền dự kiến đang âm'}</h3>
            ${pending ? `<div class="ca-position"><div>Trước điều chỉnh<strong>${money(d.currentShift.expected)}</strong></div><div>Thay đổi do sửa phiếu<strong>${signed(d.request.expectedDelta)}</strong></div><div>Sau điều chỉnh<strong>${money(expected)}</strong></div></div>` : `<div class="ca-amount">${money(expected)}</div>`}
            <p>Sổ ca đang thể hiện tổng chi và hoàn tiền vượt tiền đầu ca, doanh thu tiền mặt và thu thêm. Đây là số dư theo dữ liệu, chưa phải kết luận về tiền thực tế trong két.</p>
            <p><b>Admin cần kiểm tra:</b> tiền đầu ca đã nhập đủ chưa, các phiếu thu/chi có sai hoặc thiếu không, và tiền thực đếm trong két.</p>
            <p class="mb-0">${pending ? 'Duyệt vẫn được phép và sẽ lưu đúng số âm để đối soát.' : 'Số âm được giữ nguyên để đối soát.'} Tiền thực đếm và tiền đã nhận không đổi; hệ thống không tự bù khoản thiếu.</p>
        </section>`;
    }
    async function detail(id) {
        const seq = ++detailSequence;
        show(`Yêu cầu #${id}`, '<p>Đang tải chi tiết…</p>');
        try {
            const d = await api(`/${id}`); if (seq !== detailSequence || !dialog.open) return;
            const r = d.request;
            let snapshots = '';
            if (r.beforeShiftJson && r.afterShiftJson) {
                try { snapshots = `<details class="ca-audit"><summary>Số liệu ca trước / sau khi duyệt</summary><h3 class="mt-3">Trước khi duyệt</h3>${position(JSON.parse(r.beforeShiftJson))}<h3>Sau khi duyệt</h3>${position(JSON.parse(r.afterShiftJson))}</details>`; } catch (_) { /* Current values remain visible if historical snapshot is unavailable. */ }
            }
            show(`Yêu cầu #${r.id} · ${r.isCancellation ? 'Hủy phiếu' : 'Sửa thu/chi'}`, `<p>${badge(r)}</p><p><b>${esc(r.requestedBy)}</b> · ${esc(r.terminal)}<br><small>${esc(r.shiftCode)} · Phiếu #${r.transactionId} · ${date(r.createdAtUtc)}</small></p>
                <div class="ca-compare">${valueBox(r.before, 'Trước điều chỉnh')}${valueBox(r.after, 'Đề nghị', r.isCancellation)}</div>
                <h3>Lý do đề nghị</h3><p class="ca-detail-note">${esc(r.requestReason)}</p><div class="ca-delta">Ảnh hưởng tiền dự kiến cuối ca: <b>${signed(r.expectedDelta)}</b></div>
                ${negativeWarning(d)}
                <div class="ca-position ca-position-simple"><div>Quỹ hiện tại<strong>${money(d.currentShift.expected)}</strong></div><div>Quỹ sau khi duyệt<strong>${money(r.status==='Pending'?d.proposedShift.expected:d.currentShift.expected)}</strong></div></div>
                <details class="ca-audit"><summary>Số liệu đầy đủ</summary><h3>Số liệu ca hiện tại</h3>${position(d.currentShift)}
                ${r.status === 'Pending' ? `<h3>Sau khi duyệt</h3>${position(d.proposedShift)}` : ''}
                <p class="text-muted small">Tiền thực đếm, tiền đã nhận và phiếu chốt ca ban đầu được giữ nguyên.</p></details>
                ${r.reviewedAtUtc ? `<div class="ca-audit">${names[r.status]} bởi <b>${esc(r.reviewedBy)}</b> · ${date(r.reviewedAtUtc)}<div class="ca-detail-note">${esc(r.reviewNote)}</div></div>` : ''}
                ${r.reconciledAtUtc ? `<div class="ca-audit">Đã đối soát lại bởi <b>${esc(r.reconciledBy)}</b> · ${date(r.reconciledAtUtc)}<div class="ca-detail-note">${esc(r.reconciliationNote)}</div></div>` : ''}${snapshots}
                ${d.currentShift.needsReconciliation ? '<div class="alert alert-warning">Ca này cần đối soát lại sau điều chỉnh. Xác nhận đối soát áp dụng cho tất cả điều chỉnh đã duyệt, chưa đối soát của ca.</div>' : ''}
                ${isAdmin && r.status === 'Pending' && !d.canApprove ? '<div class="alert alert-warning">Phiếu gốc đã thay đổi hoặc đã hủy. Hãy từ chối yêu cầu và yêu cầu nhân viên lập lại.</div>' : ''}
                ${(isAdmin && (r.status === 'Pending' || d.currentShift.needsReconciliation)) || d.canWithdraw ? '<label>Ghi chú xử lý<textarea id="caDecisionNote" class="form-control" maxlength="500" placeholder="Bắt buộc khi từ chối hoặc xác nhận đối soát lại"></textarea></label>' : ''}
                ${d.canApprove && d.proposedShift.expected < 0 ? `<p class="alert alert-warning mb-0"><b>Duyệt sẽ ghi nhận tiền dự kiến ${money(d.proposedShift.expected)}.</b> Cần kiểm tra chứng từ và tiền thực đếm sau điều chỉnh.</p>` : ''}
                <div class="ca-actions">${d.canWithdraw ? '<button class="btn btn-outline-secondary" data-decision="withdraw">Rút yêu cầu</button>' : ''}${isAdmin && r.status === 'Pending' ? '<button class="btn btn-outline-danger" data-decision="reject">Từ chối</button>' : ''}${d.canApprove ? '<button class="btn btn-primary" data-decision="approve">Duyệt điều chỉnh</button>' : ''}${isAdmin && d.currentShift.needsReconciliation ? '<button class="btn btn-success" data-decision="reconcile">Xác nhận đã đối soát lại ca</button>' : ''}</div>`);
            dialog.querySelectorAll('[data-decision]').forEach(button => button.onclick = async () => {
                if (busy) return; const action = button.dataset.decision, note = $('caDecisionNote')?.value.trim();
                if ((action === 'reject' || action === 'reconcile') && !note) { modalError('Vui lòng nhập ghi chú xử lý.'); $('caDecisionNote').focus(); return; }
                setBusy(true); modalError(''); let ok = false;
                try { await api(action === 'reconcile' ? `/shifts/${r.shiftId}/reconcile` : `/${r.id}/${action}`, { rowVersion: action === 'reconcile' ? d.shiftRowVersion : r.rowVersion, note }); ok = true; }
                catch (err) { modalError(err.message); } finally { setBusy(false); }
                if (ok) { message(action === 'approve' ? 'Đã duyệt và cập nhật số liệu thu/chi.' : action === 'reconcile' ? 'Đã xác nhận đối soát lại ca.' : 'Đã cập nhật yêu cầu.'); await load(); await detail(id); }
            });
        } catch (err) { if (seq === detailSequence) modalError(err.message); }
    }
    $('caRows').onclick = e => { const b = e.target.closest('button'); if (!b || busy) return; if (b.dataset.detail) detail(Number(b.dataset.detail)); else if (b.dataset.create) create(rows.find(r => r.id === Number(b.dataset.create))); };
    root.querySelectorAll('[data-tab]').forEach(b => b.onclick = () => { tab = b.dataset.tab; page = 1; load(); });
    $('caFilters').onsubmit = e => { e.preventDefault(); page = 1; load(); };
    $('caStatus').onchange = () => { page = 1; load(); };
    $('caRefresh').onclick = () => { message(''); load(); };
    $('caClear').onclick = () => { shiftId = transactionId = null; $('caKeyword').value = ''; $('caStatus').value = 'Pending'; page = 1; if(window.POSRequestTabs)window.POSRequestTabs.clearFilterUrl('cash');else history.replaceState(null, '', base); load(); };
    $('caPrev').onclick = () => { if (page > 1) { page--; load(); } }; $('caNext').onclick = () => { if (page < pages) { page++; load(); } };
    if (url.searchParams.has('status')) $('caStatus').value = url.searchParams.get('status');
    if (url.searchParams.has('keyword')) $('caKeyword').value = url.searchParams.get('keyword');
    window.POSCashAdjustmentPage.getQuery = () => {
        const query = new URLSearchParams({tab:'cash',view:tab});
        if(shiftId)query.set('shiftId',shiftId);if(transactionId)query.set('transactionId',transactionId);
        if(tab==='requests')query.set('status',$('caStatus').value);else if($('caKeyword').value)query.set('keyword',$('caKeyword').value);
        return query;
    };
    load();
    if(Number(url.searchParams.get('requestId'))>0)detail(Number(url.searchParams.get('requestId')));
    }
    window.POSCashAdjustmentPage = {init};
    if(!document.getElementById('posOperationsRequests'))init();
})();
