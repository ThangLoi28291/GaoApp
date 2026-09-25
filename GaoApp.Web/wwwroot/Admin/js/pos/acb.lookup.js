(function () {
    'use strict';
    const root = document.getElementById('acbLookup');
    if (!root) return;
    const button = document.getElementById('acbRefresh');
    const status = document.getElementById('acbLookupStatus');
    const results = document.getElementById('acbLookupResults');
    const overview = document.getElementById('acbLookupSummary');
    const labels = { Creating: 'Đang xác minh', Pending: 'Chờ chuyển khoản', Received: 'Đã nhận · chờ chốt', Completed: 'Đã chốt đơn', Cancelled: 'Đã hủy', ReviewRequired: 'Cần đối chiếu', COMPLETED: 'ACB xác nhận', SUCCESS: 'ACB xác nhận', PAID: 'ACB xác nhận', APPROVED: 'ACB xác nhận', ERRORCORRECTED: 'Đã điều chỉnh / hủy', Manual: 'Thu ngân ghi nhận', ManualConfirmed: 'Xác nhận thủ công', Paid: 'Đã ghi nhận', Expired: 'Hết hạn', Failed: 'Không thành công' };
    const tones = { Completed: 'success', Received: 'success', COMPLETED: 'success', SUCCESS: 'success', PAID: 'success', APPROVED: 'success', ReviewRequired: 'danger', ERRORCORRECTED: 'danger', Creating: 'warning', Pending: 'warning', Cancelled: 'muted', Manual: 'info', ManualConfirmed: 'info', Paid: 'info', Failed: 'danger' };
    const money = value => Number(value || 0).toLocaleString('vi-VN') + ' ₫';
    const date = value => {
        if (!value) return 'Chưa tra cứu';
        const parsed = new Date(/(?:Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : value + 'Z');
        return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('vi-VN');
    };
    function el(tag, text, className) { const node = document.createElement(tag); if (text != null) node.textContent = text; if (className) node.className = className; return node; }
    function badge(value) { return el('span', labels[value] || value, 'acb-badge acb-' + (tones[value] || 'muted')); }
    function metric(label, value, accent) { const card = el('div', null, 'acb-metric' + (accent ? ' acb-metric-accent' : '')); card.append(el('span', label), el('strong', value)); return card; }
    function setStatus(text, error) { status.textContent = text; status.className = 'acb-status' + (error ? ' acb-error' : ''); }
    function panel(title, count, subtitle) {
        const section = el('section', null, 'acb-panel');
        const head = el('div', null, 'acb-panel-head'); const name = el('h2', title); name.append(el('span', String(count), 'acb-count'));
        head.append(name); if (subtitle) head.append(el('span', subtitle, 'acb-panel-note')); section.append(head); return section;
    }
    async function responseError(response) {
        try { const body = await response.json(); return new Error((body.message || body.detail || 'Chưa tra cứu được ACB.') + (body.traceId ? ' Mã kiểm tra: ' + body.traceId : '')); }
        catch (_) { return new Error('Chưa kết nối được máy chủ. Hãy thử lại.'); }
    }
    function confirmationInfo(info) {
        const block = el('div', null, 'acb-confirmation');
        block.dataset.source = info?.code || 'Unknown';
        block.append(el('strong', info?.label || 'Chưa lưu nguồn xác nhận', 'acb-confirmation-label'));
        if (info?.confirmedAtUtc) block.append(el('small', 'Xác nhận: ' + date(info.confirmedAtUtc)));
        if (info?.userName || info?.userId) block.append(el('small', 'Người thao tác: ' + (info.userName || '#' + info.userId)));
        if (info?.callbackReceiptId) block.append(el('small', 'Biên nhận callback #' + info.callbackReceiptId));
        if (info && !info.recorded && info.code !== 'Pending' && info.code !== 'Unknown')
            block.append(el('small', 'Đã xác minh · Chưa ghi khoản thu', 'acb-confirmation-pending'));
        return block;
    }
    function renderLedger(sessions, payments) {
        const bankRows = sessions.flatMap(s => s.transactions.map(t => ({ ...t, source: 'ACB · ' + s.providerOrderId, confirmation: s.confirmation })));
        const manualRows = payments.filter(p => !p.automatic).map(p => ({ transactionNumber: p.referenceCode || 'Khoản thu #' + p.id, amount: p.amount, status: 'Manual', content: p.provider || 'Chuyển khoản thủ công', postedAt: date(p.paidAtUtc), source: 'Ghi nhận trên hóa đơn', confirmation: p.confirmation }));
        const rows = [...bankRows, ...manualRows];
        const section = panel('Giao dịch chuyển khoản', rows.length, 'ACB xác nhận và khoản thu thủ công');
        if (!rows.length) {
            const empty = el('div', null, 'acb-empty'); empty.append(el('span', '↔', 'acb-empty-icon'), el('strong', 'Chưa có giao dịch chuyển khoản'), el('p', 'Sau khi khách chuyển tiền, bấm Tra cứu ACB để cập nhật kết quả.'));
            section.append(empty);
        } else {
            const wrap = el('div', null, 'acb-table-scroll'); const table = el('table', null, 'acb-ledger');
            const head = table.createTHead().insertRow(); ['Giao dịch / Nội dung', 'Ngày ghi nhận', 'Phương án xác nhận', 'Số tiền', 'Trạng thái'].forEach((label, i) => head.append(el('th', label, i === 3 ? 'acb-number' : '')));
            const body = table.createTBody();
            rows.forEach(t => {
                const row = body.insertRow(); const detail = el('td', null, 'acb-tx-content');
                detail.append(el('strong', t.transactionNumber || '—'), el('span', t.content || 'Không có nội dung', 'acb-tx-description'), el('small', t.source));
                const state = el('td'); state.append(badge(t.status));
                const confirmation = el('td', null, 'acb-tx-confirmation'); confirmation.append(confirmationInfo(t.confirmation));
                row.append(detail, el('td', t.postedAt || '—', 'acb-tx-date'), confirmation, el('td', money(t.amount), 'acb-number acb-tx-amount'), state);
            }); wrap.append(table); section.append(wrap);
        }
        results.append(section);
    }
    function cancelButton(s) {
        const cancel = el('button', 'Hủy QR đang chờ', 'acb-button acb-danger-button'); cancel.type = 'button';
        cancel.addEventListener('click', async () => {
            if (!window.confirm('Hủy QR thanh toán này? Tiền mặt đã thu vẫn được giữ nguyên.')) return;
            cancel.disabled = true; button.disabled = true; setStatus('Đang tra cứu và yêu cầu ACB hủy QR…');
            try {
                const response = await fetch(`/admin/acb/payments/${s.qrId}/cancel`, { method: 'POST', headers: headers() });
                if (!response.ok) throw await responseError(response);
                await load(false);
            } catch (error) { setStatus(error.message, true); cancel.disabled = false; }
            finally { button.disabled = false; }
        }); return cancel;
    }
    function renderHistory(sessions, otherQrs) {
        if (!sessions.length && !otherQrs.length) return;
        const section = panel('Lịch sử mã QR', sessions.length + otherQrs.length, 'Bấm từng dòng để xem chi tiết');
        const attempts = [
            ...sessions.map(s => ({ ...s, reference: s.providerOrderId, automatic: true })),
            ...otherQrs.map(s => ({ ...s, reference: s.requestCode, automatic: false, transactions: [] }))
        ].sort((a, b) => b.qrId - a.qrId);
        attempts.forEach((s, index) => {
            const detail = el('details', null, 'acb-qr-row'); detail.open = s.status === 'ReviewRequired';
            const summary = el('summary'); const identity = el('div', null, 'acb-qr-identity');
            identity.append(el('strong', s.reference), el('small', (s.automatic ? 'ACB tự động' : 'QR thủ công') + (index === 0 ? ' · Gần nhất' : '')));
            summary.append(el('span', '›', 'acb-chevron'), identity, el('span', date(s.createdAtUtc), 'acb-qr-date'), el('strong', money(s.amount), 'acb-number'), badge(s.status)); detail.append(summary);
            const content = el('div', null, 'acb-qr-detail');
            const audit = el('div', null, 'acb-qr-confirmation');
            audit.append(el('span', 'Phương án xác nhận', 'acb-confirmation-heading'), confirmationInfo(s.confirmation));
            content.append(audit);
            if (s.automatic) {
                content.append(el('p', `Ca #${s.shiftId} · Máy #${s.terminalId} · Tra cứu gần nhất: ${date(s.lastRetrievedAtUtc)}`));
                content.append(el('p', s.transactions.length ? 'Mã giao dịch: ' + s.transactions.map(t => t.transactionNumber).join(', ') : 'Chưa có giao dịch ACB cho mã QR này.'));
            } else {
                content.append(el('p', (s.bankName || 'Ngân hàng nhận tiền') + ' · ' + (s.content || 'Không có nội dung')));
                if (s.manualConfirmedAtUtc) content.append(el('p', 'Xác nhận thủ công lúc ' + date(s.manualConfirmedAtUtc)));
            }
            if (s.reviewReason) content.append(el('p', s.reviewReason, 'acb-callout'));
            if (s.canCancel && (s.status === 'Pending' || s.status === 'Creating')) content.append(cancelButton(s));
            detail.append(content); section.append(detail);
        }); results.append(section);
    }
    function render(data) {
        results.replaceChildren(); overview.replaceChildren();
        const { order, sessions, payments, otherQrs } = data;
        document.getElementById('acbLookupTitle').textContent = 'Chuyển khoản · ' + (order.orderNumber || 'Đơn #' + order.id);
        overview.append(metric('Tổng hóa đơn', money(order.grandTotal)), metric('Đã thu chuyển khoản', money(payments.reduce((sum, p) => sum + p.amount, 0)), true), metric('Còn phải thanh toán', money(order.balanceDue)));
        button.hidden = sessions.length === 0;
        renderLedger(sessions, payments); renderHistory(sessions, otherQrs);
    }
    function headers() { return { RequestVerificationToken: root.querySelector('input[name="__RequestVerificationToken"]').value }; }
    async function load(refresh) {
        button.disabled = true; setStatus(refresh ? 'Đang tra cứu trực tiếp tại ACB…' : 'Đang tải lịch sử chuyển khoản…');
        try {
            const response = await fetch(`/admin/acb/payments/orders/${encodeURIComponent(root.dataset.orderId)}/${refresh ? 'refresh' : 'data'}`, { method: refresh ? 'POST' : 'GET', headers: headers() });
            if (!response.ok) throw await responseError(response);
            const data = await response.json(); render(data);
            setStatus(refresh ? 'Đã cập nhật từ ACB lúc ' + new Date().toLocaleTimeString('vi-VN') + '.' : data.sessions.length ? 'Dữ liệu đã lưu · Bấm Tra cứu ACB để cập nhật trực tiếp từ ngân hàng.' : 'Dữ liệu khoản thu do thu ngân ghi nhận trên hóa đơn.');
        } catch (error) { setStatus(error.message, true); }
        finally { button.disabled = false; }
    }
    button.addEventListener('click', () => load(true)); load(false);
})();
