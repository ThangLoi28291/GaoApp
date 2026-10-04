(() => {
    'use strict';
    const $ = id => document.getElementById(id);
    if (!$('xsBackfillOpen')) return;
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const date = value => new Date(value).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' });
    const endpoint = '/admin/api/receipt-invoice-backfill';
    let rows = [], selected = new Set(), page = 1, totalPages = 1, busy = false, scannedRange = null;

    function selection() {
        const ready = rows.filter(x => x.status === 'Ready');
        $('xsBackfillSelected').textContent = `${selected.size} phiếu được chọn (tối đa 50 mỗi lần).`;
        $('xsBackfillConfirm').disabled = busy || !scannedRange || !selected.size || !$('xsBackfillReason').value.trim();
        $('xsBackfillSelectAll').disabled = busy || !scannedRange || !ready.length;
        $('xsBackfillSelectAll').checked = ready.length > 0 && selected.size === ready.length;
        $('xsBackfillSelectAll').indeterminate = selected.size > 0 && selected.size < ready.length;
    }
    function setBusy(value) {
        busy = value;
        $('xsBackfillDialog').setAttribute('aria-busy', String(value));
        $('xsBackfillFilters').querySelectorAll('input,button').forEach(x => x.disabled = value);
        $('xsBackfillBody').querySelectorAll('input').forEach(x => x.disabled = value);
        $('xsBackfillClose').disabled = value;
        $('xsBackfillPrevious').disabled = value || !scannedRange || page <= 1;
        $('xsBackfillNext').disabled = value || !scannedRange || page >= totalPages;
        selection();
    }
    function notice(message, error = false) {
        $('xsBackfillNotice').hidden = !message;
        $('xsBackfillNotice').className = `alert mt-3 ${error ? 'alert-warning' : 'alert-success'}`;
        $('xsBackfillNotice').textContent = message;
    }
    async function request(url, body) {
        const response = await fetch(url, {
            method: body ? 'POST' : 'GET', credentials: 'same-origin', cache: 'no-store',
            headers: { 'Content-Type': 'application/json', RequestVerificationToken: $('xsBackfillDialog').querySelector('[name="__RequestVerificationToken"]')?.value || '' },
            ...(body ? { body: JSON.stringify(body) } : {})
        });
        const data = await response.json().catch(() => null);
        if (!response.ok) throw new Error(data?.message || (response.status === 403 ? 'Anh không có quyền xác nhận các phiếu này.' : 'Không xử lý được yêu cầu. Vui lòng rà soát lại để kiểm tra kết quả.'));
        if (!data) throw new Error('Máy chủ không trả về kết quả. Vui lòng rà soát lại.');
        return data;
    }
    function render(data) {
        rows = data.rows; page = data.page; totalPages = data.totalPages;
        selected = new Set(rows.filter(x => x.status === 'Ready').map(x => x.receiptId));
        $('xsBackfillSummary').textContent = `${data.totalItems} phiếu trong khoảng ngày gắn XML. Trang này: ${data.readyCount} cần xác nhận · ${data.completeCount} đã đủ · ${data.blockedCount} cần kiểm tra riêng.`;
        $('xsBackfillPage').textContent = `Trang ${page}/${totalPages}`;
        $('xsBackfillBody').innerHTML = rows.length ? rows.map(row => `<tr data-status="${esc(row.status)}">
            <td>${row.status === 'Ready' ? `<input type="checkbox" data-receipt="${Number(row.receiptId)}" checked aria-label="Chọn phiếu ${esc(row.documentNo)}" />` : ''}</td>
            <td><a href="/admin/stock-documents/${Number(row.receiptId)}" target="_blank" rel="noopener">${esc(row.documentNo)}</a><small class="xs-sub">${esc(row.warehouseName)}</small></td>
            <td>${esc(row.xmlNumber)}<small class="xs-sub">${esc(date(row.linkedAtUtc))}</small></td>
            <td>${Number(row.postedTransactionCount)}</td><td>${Number(row.productCountNeedingConfirmation)} / ${Number(row.productCount)}</td>
            <td><span class="xs-badge ${row.status === 'Complete' ? 'increase' : row.status === 'Blocked' ? 'hold' : ''}">${row.status === 'Complete' ? 'Đã đủ' : row.status === 'Blocked' ? 'Cần kiểm tra riêng' : 'Cần xác nhận'}</span><small class="xs-sub">${esc(row.message)}</small></td>
        </tr>`).join('') : '<tr><td colspan="6">Không có phiếu đã duyệt gắn XML trong khoảng ngày này.</td></tr>';
    }
    async function scan(nextPage = 1, preserveNotice = false) {
        if (busy) return;
        const range = { linkedFromDate: $('xsBackfillFrom').value, linkedToDate: $('xsBackfillTo').value || null };
        if (!range.linkedFromDate || (range.linkedToDate && range.linkedToDate < range.linkedFromDate)) {
            invalidate(); notice('Kiểm tra lại khoảng ngày gắn XML.', true); return;
        }
        setBusy(true); scannedRange = null; selected.clear();
        if (!preserveNotice) notice('');
        try {
            const params = new URLSearchParams({ linkedFromDate: range.linkedFromDate, page: String(nextPage), pageSize: '50' });
            if (range.linkedToDate) params.set('linkedToDate', range.linkedToDate);
            const data = await request(endpoint + '?' + params);
            scannedRange = range; render(data);
        } catch (error) { rows = []; selected.clear(); $('xsBackfillBody').innerHTML = '<tr><td colspan="6">Chưa rà soát được. Bấm Rà soát để thử lại.</td></tr>'; notice(error.message, true); }
        finally { setBusy(false); }
    }
    function invalidate() {
        scannedRange = null; selected.clear(); selection();
        $('xsBackfillBody').querySelectorAll('input').forEach(x => { x.checked = false; x.disabled = true; });
        $('xsBackfillPrevious').disabled = true; $('xsBackfillNext').disabled = true;
        $('xsBackfillSummary').textContent = 'Bộ lọc đã thay đổi. Bấm Rà soát để lấy danh sách mới.';
    }
    $('xsBackfillOpen').onclick = () => { $('xsBackfillDialog').showModal(); $('xsBackfillFrom').focus(); };
    $('xsBackfillClose').onclick = () => $('xsBackfillDialog').close();
    $('xsBackfillDialog').addEventListener('cancel', event => { if (busy) event.preventDefault(); });
    $('xsBackfillDialog').addEventListener('close', () => $('xsBackfillOpen').focus());
    $('xsBackfillFilters').onsubmit = event => { event.preventDefault(); scan(); };
    ['xsBackfillFrom', 'xsBackfillTo'].forEach(id => $(id).addEventListener('input', invalidate));
    $('xsBackfillReason').addEventListener('input', selection);
    $('xsBackfillPrevious').onclick = () => scan(page - 1); $('xsBackfillNext').onclick = () => scan(page + 1);
    $('xsBackfillBody').addEventListener('change', event => {
        const id = Number(event.target.dataset.receipt);
        if (!scannedRange || !rows.some(x => x.receiptId === id && x.status === 'Ready')) return;
        if (event.target.checked) selected.add(id); else selected.delete(id); selection();
    });
    $('xsBackfillSelectAll').onchange = event => {
        if (!scannedRange) return;
        selected = new Set(event.target.checked ? rows.filter(x => x.status === 'Ready').map(x => x.receiptId) : []);
        $('xsBackfillBody').querySelectorAll('input').forEach(x => x.checked = selected.has(Number(x.dataset.receipt))); selection();
    };
    $('xsBackfillConfirm').onclick = async () => {
        if (busy || !scannedRange || !selected.size || !$('xsBackfillReason').value.trim()) return;
        const body = { ...scannedRange, reason: $('xsBackfillReason').value.trim(),
            items: rows.filter(x => selected.has(x.receiptId) && x.status === 'Ready').map(x => ({ receiptId: x.receiptId, snapshotHash: x.snapshotHash })) };
        setBusy(true);
        try {
            const result = await request(endpoint + '/confirm', body);
            const blocked = result.filter(x => x.status === 'Blocked');
            notice(`Đã bổ sung ${result.filter(x => x.status === 'Confirmed').length} phiếu · Bỏ qua ${result.filter(x => x.status === 'Skipped').length} phiếu đã đủ · ${blocked.length} phiếu cần kiểm tra lại.`
                + (blocked.length ? ' ' + blocked.map(x => `${rows.find(r => r.receiptId === x.receiptId)?.documentNo || x.receiptId}: ${x.message}`).join(' | ') : ''), !!blocked.length);
            window.dispatchEvent(new Event('invoice-stock-updated'));
        } catch (error) { notice(error.message, true); }
        finally { setBusy(false); await scan(page, true); }
    };
})();
