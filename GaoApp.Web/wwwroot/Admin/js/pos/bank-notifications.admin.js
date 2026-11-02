(function () {
    'use strict';
    const live = document.getElementById('bankNotificationLiveState');
    const status = document.getElementById('bankNotificationLiveStatus');
    const element = document.getElementById('adminBankApprovalModal');
    const modal = element && window.bootstrap ? new bootstrap.Modal(element, { backdrop: 'static' }) : null;
    const confirm = document.getElementById('adminApprovalConfirm');
    const cancel = document.getElementById('adminApprovalCancel');
    const error = document.getElementById('adminApprovalError');
    const regionIds = ['bankNotificationDeviceRows', 'bankNotificationWaitingRows', 'bankNotificationHistoryRows'];
    let selected = null, busy = false, refreshing = false, stopped = false, timer, refreshAbort, generation = 0, refreshRequested = false;
    const setStatus = message => { if (status) status.textContent = message; };
    const editingLiveRow = () => document.activeElement?.matches('input,select,textarea') &&
        document.activeElement.closest('[data-bank-live-region]');
    const paused = () => document.hidden || busy || element?.classList.contains('show') || editingLiveRow();
    function schedule() {
        clearTimeout(timer);
        if (live && !stopped) timer = setTimeout(refresh, refreshRequested && !paused() ? 0 : 3000);
        refreshRequested = false;
    }
    function stopForAuthorization() {
        stopped = true; clearTimeout(timer);
        setStatus('Phiên đăng nhập hoặc quyền truy cập đã thay đổi. Đăng nhập lại để tiếp tục cập nhật.');
    }
    function preserveRowState(current, next) {
        const oldDetails = current.querySelectorAll('details');
        next.querySelectorAll('details').forEach((details, index) => { details.open = oldDetails[index]?.open === true; });
        const oldSelects = current.querySelectorAll('select');
        next.querySelectorAll('select').forEach((select, index) => {
            const previous = oldSelects[index];
            if (!previous || previous.name !== select.name) return;
            if (!Array.from(select.options).some(option => option.value === previous.value)) {
                const empty = document.createElement('option');
                empty.value = ''; empty.textContent = 'Khoản chờ đã thay đổi — chọn lại';
                select.prepend(empty);
                select.value = '';
            } else select.value = previous.value;
        });
    }
    function applySnapshot(html) {
        const snapshot = new DOMParser().parseFromString(html, 'text/html');
        if (!snapshot.getElementById('bankNotificationLiveState')) throw new Error('Invalid live state');
        // Validate every region before touching the current screen. An expired login
        // or partial response must not erase the last known transaction history.
        const regions = regionIds.map(id => {
            const current = document.getElementById(id), next = snapshot.getElementById(id);
            if (!current || !next) throw new Error('Incomplete live state');
            const keys = Array.from(next.children, row => row.dataset.liveKey);
            if (keys.some(key => !key) || new Set(keys).size !== keys.length) throw new Error('Invalid live rows');
            return { current, next };
        });
        const focusSelector = 'a,button,input,select,textarea,summary,[tabindex]';
        let restoreFocus;
        for (const { current, next } of regions) {
            const oldRows = new Map(Array.from(current.children, row => [row.dataset.liveKey, row]));
            const fragment = document.createDocumentFragment();
            for (const incoming of Array.from(next.children)) {
                const old = oldRows.get(incoming.dataset.liveKey);
                const focusIndex = old ? Array.from(old.querySelectorAll(focusSelector)).indexOf(document.activeElement) : -1;
                let row = incoming;
                if (old && old.isEqualNode(incoming)) row = old;
                else if (old) {
                    preserveRowState(old, incoming);
                }
                if (focusIndex >= 0) restoreFocus = row.querySelectorAll(focusSelector)[focusIndex];
                fragment.appendChild(row);
            }
            current.replaceChildren(fragment);
        }
        const history = document.getElementById('bankNotificationHistory');
        if (history) history.dataset.pendingQrIds = snapshot.getElementById('bankNotificationHistory')?.dataset.pendingQrIds || '';
        restoreFocus?.focus({ preventScroll: true });
    }
    async function refresh() {
        if (!live || stopped) return;
        if (refreshing) { refreshRequested = true; return; }
        clearTimeout(timer);
        if (paused()) { schedule(); return; }
        if (navigator.onLine === false) { setStatus('Mất kết nối mạng. Danh sách sẽ tự cập nhật khi có mạng lại.'); schedule(); return; }
        refreshing = true;
        const abort = new AbortController();
        refreshAbort = abort;
        const requestGeneration = generation;
        const timeout = setTimeout(() => abort.abort(), 15000);
        try {
            const response = await fetch(status?.dataset.liveUrl || '/admin/bank-notifications/live-state', {
                cache: 'no-store', signal: abort.signal, headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (response.status === 401 || response.status === 403 || response.redirected) { stopForAuthorization(); return; }
            if (!response.ok) throw new Error('Live state unavailable');
            const html = await response.text();
            // A modal or selection may have opened while the request was in flight.
            if (paused() || requestGeneration !== generation) return;
            applySnapshot(html);
            setStatus('Tự cập nhật mỗi 3 giây.');
        } catch (_) {
            if (requestGeneration === generation) setStatus('Chưa cập nhật được. Giữ danh sách hiện tại và tự thử lại.');
        } finally {
            clearTimeout(timeout); refreshAbort = null; refreshing = false; schedule();
        }
    }
    // Delegation also handles forms received in later snapshots. Opening a review
    // freezes its target, amount and token until the administrator confirms it.
    document.addEventListener('submit', event => {
        const form = event.target.closest('form[data-bank-approval]');
        if (!form) return;
        event.preventDefault();
        if (!modal || busy || stopped || !form.reportValidity()) return;
        generation++; refreshAbort?.abort();
        const data = new FormData(form);
        const option = form.querySelector('select[name="qrId"]')?.selectedOptions[0];
        const target = option?.dataset || form.dataset;
        selected = Object.freeze({
            token: data.get('__RequestVerificationToken'),
            decision: Object.freeze({ qrId: Number(data.get('qrId')), amount: Number(data.get('amount')),
                receiptId: data.has('receiptId') ? Number(data.get('receiptId')) : null })
        });
        document.getElementById('adminApprovalSender').textContent = form.dataset.sender || '';
        document.getElementById('adminApprovalAmount').textContent = Number(form.dataset.amount).toLocaleString('vi-VN') + ' đ';
        document.getElementById('adminApprovalTarget').textContent = `${target.terminal} · Đơn #${target.order}`;
        document.getElementById('adminApprovalReference').textContent = form.dataset.reference || '';
        error.textContent = ''; modal.show();
    });
    confirm?.addEventListener('click', async () => {
        if (busy || !selected || stopped) return;
        busy = true; confirm.disabled = cancel.disabled = true;
        const reviewed = selected;
        const abort = new AbortController();
        const timeout = setTimeout(() => abort.abort(), 15000);
        let succeeded = false;
        try {
            const response = await fetch('/admin/bank-notifications/approve', { method: 'POST', cache: 'no-store', signal: abort.signal,
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: reviewed.token },
                body: JSON.stringify(reviewed.decision) });
            const result = await response.json().catch(() => ({}));
            if (response.status === 401 || response.redirected) stopForAuthorization();
            if (!response.ok || response.redirected) throw new Error(result.message || (response.status === 403 ? 'Bạn chưa có quyền xác nhận thanh toán.' : 'Chưa gửi được xác nhận. Kiểm tra lại khoản chờ.'));
            succeeded = true;
        } catch (failure) {
            error.textContent = failure.name === 'AbortError' ? 'Chưa nhận được kết quả. Có thể thử lại; cùng khoản chờ sẽ chỉ ghi nhận một lần.' : failure.message;
        } finally {
            clearTimeout(timeout); busy = false; confirm.disabled = cancel.disabled = false;
            if (succeeded) { generation++; refreshAbort?.abort(); selected = null; modal.hide(); }
        }
    });
    element?.addEventListener('hide.bs.modal', event => { if (busy) event.preventDefault(); });
    element?.addEventListener('hidden.bs.modal', () => { selected = null; refresh(); });
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
    document.addEventListener('focusout', event => { if (event.target.closest('[data-bank-live-region]')) schedule(); });
    window.addEventListener('online', refresh);
    window.addEventListener('offline', () => setStatus('Mất kết nối mạng. Danh sách sẽ tự cập nhật khi có mạng lại.'));
    window.addEventListener('pagehide', () => clearTimeout(timer));
    window.addEventListener('pageshow', event => { if (event.persisted) refresh(); });
    schedule();
}());
