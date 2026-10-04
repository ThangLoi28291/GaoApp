(function () {
    'use strict';
    const watched = new Map();
    const manualPending = new Set();
    const checking = new Set();
    const recording = new Set();
    const retrying = new Set();
    const initialDelay = 30000;
    const retryDelay = 8000;
    let activeRun = null;
    let discoverPending = false;
    let focusedQrId = null;
    let nextDiscoveryAt = 0;
    let banner;
    let paused = false;
    function canPoll() {
        const status = window.PosOffline?.status?.();
        // Automatic bank work must wait until the terminal's offline receipts reach the server.
        return window.navigator?.onLine !== false && status?.connected !== false && !(status?.pending > 0);
    }
    function clearDiscoveryNote() {
        if (banner?.dataset.kind === 'discovery') banner.hidden = true;
    }
    function pause() {
        paused = true;
        clearDiscoveryNote();
        renderCountdown();
    }
    function renderCountdown() {
        const box = document.getElementById('acbPaymentCountdown');
        if (!box) return;
        const next = watched.get(focusedQrId);
        box.hidden = next == null || !canPoll();
        if (box.hidden) return;
        const label = document.getElementById('acbPaymentCountdownLabel');
        const value = document.getElementById('acbPaymentCountdownValue');
        const seconds = Math.max(0, Math.ceil((next - Date.now()) / 1000));
        const busy = checking.has(focusedQrId) || recording.has(focusedQrId);
        box.dataset.phase = busy ? 'checking' : retrying.has(focusedQrId) ? 'retry' : 'waiting';
        const text = recording.has(focusedQrId) ? 'Đang ghi nhận thanh toán…'
            : checking.has(focusedQrId) ? 'Đang kiểm tra ACB…'
            : seconds === 0 ? 'Đang chờ lượt kiểm tra…'
            : retrying.has(focusedQrId) ? 'Thử kiểm tra lại sau' : 'Kiểm tra ACB tiếp theo sau';
        if (label && label.textContent !== text) label.textContent = text;
        if (value) value.textContent = busy ? '…' : `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`;
    }
    function forget(id) {
        watched.delete(id); manualPending.delete(id); retrying.delete(id);
        renderCountdown();
    }
    function note(message, orderId, qrId, kind = 'payment') {
        if (!banner) { banner = document.createElement('div'); banner.id = 'acbPaymentNotice'; banner.className = 'alert alert-info'; banner.setAttribute('role', 'status'); document.getElementById('paymentQrModal')?.before(banner); }
        banner.dataset.kind = kind; banner.hidden = false;
        banner.replaceChildren(document.createTextNode(message + ' '));
        if (orderId) { const link = document.createElement('a'); link.href = `/admin/acb/payments/orders/${orderId}`; link.textContent = 'Xem giao dịch'; link.target = '_blank'; link.rel = 'noopener'; banner.appendChild(link); }
        const popup = document.getElementById('acbPaymentCheckStatus');
        if (popup && qrId === focusedQrId) { popup.hidden = false; popup.textContent = message; }
    }
    async function post(url) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const response = await fetch(url, { method: 'POST', headers: { RequestVerificationToken: token || '' } });
        if (!response.ok) {
            let message = 'Chưa xử lý được thanh toán ACB. Hãy thử kiểm tra lại.';
            try { const error = await response.json(); message = error.message || error.detail || message; } catch (_) { }
            throw new Error(message);
        }
        return response.json();
    }
    async function run(discover) {
        do {
            if (!canPoll()) { pause(); return; }
            const shouldDiscover = discover || discoverPending;
            discover = false; discoverPending = false;
            if (shouldDiscover) {
                try {
                    const response = await fetch('/admin/acb/payments/terminal-pending');
                    if (!response.ok) throw new Error('Chưa tải được các QR đang chờ. Hãy kiểm tra kết nối máy chủ.');
                    const qrs = await response.json();
                    if (!canPoll()) { pause(); return; }
                    qrs.forEach(qr => { if (!watched.has(qr.qrId)) watched.set(qr.qrId, Date.now() + initialDelay); });
                    clearDiscoveryNote();
                } catch (error) {
                    if (canPoll()) note(error.message, undefined, undefined, 'discovery');
                    else { pause(); return; }
                }
            }
            for (const [id, retrieveAfter] of watched) {
                if (!canPoll()) { pause(); return; }
                const manual = manualPending.delete(id);
                const refresh = manual || Date.now() >= retrieveAfter;
                if (refresh) checking.add(id);
                renderCountdown();
                try {
                    const result = await post(`/admin/acb/payments/${id}/status?refresh=${refresh}${manual ? '&manual=true' : ''}`);
                    if (!canPoll()) { pause(); return; }
                    // The countdown and the next outbound lookup use the same deadline.
                    // Local callback-state reads must not push that deadline back.
                    if (refresh) { watched.set(id, Date.now() + retryDelay); retrying.delete(id); }
                    if (result.status === 'Received' || result.status === 'Completed') {
                        recording.add(id); renderCountdown();
                        note('ACB đã xác nhận khoản chuyển. Đang ghi nhận thanh toán…', result.orderId, id);
                        const completed = await post(`/admin/acb/payments/${id}/complete`);
                        // A manual request queued during completion must not recreate or print this payment again.
                        forget(id);
                        note(completed.finalized === false
                            ? `Đã ghi nhận chuyển khoản. Đơn ${completed.orderId} còn thiếu ${Number(completed.remainingAmount).toLocaleString('vi-VN')}đ.`
                            : `Đã nhận chuyển khoản và chốt đơn ${completed.orderId}.`, completed.orderId, id);
                        if (completed.printUrl) {
                            const frame = document.createElement('iframe'); frame.title = 'In bill'; frame.style.cssText = 'position:fixed;width:0;height:0;border:0'; frame.addEventListener('load', () => frame.contentWindow?.addEventListener('afterprint', () => frame.remove(), { once: true })); frame.src = completed.finalized === true
                                ? window.PosPrinting.postPaymentUrl(completed.printUrl, completed.orderId) : completed.printUrl; document.body.appendChild(frame);
                        }
                        window.dispatchEvent(new CustomEvent('acb:completed', { detail: { ...completed, qrId: id } }));
                    } else if (result.status === 'Creating') {
                        note(`QR của đơn ${result.orderId} đang chờ xác minh sau lần tạo trước. Mở giao dịch để tra cứu hoặc hủy lần tạo cũ.`, result.orderId, id);
                    } else if (result.status === 'ReviewRequired' || result.status === 'Cancelled') {
                        forget(id);
                        note(result.message || (result.status === 'Cancelled' ? 'QR đã hủy.' : 'Giao dịch cần kiểm tra, chưa chốt đơn.'), result.orderId, id);
                    } else if (refresh) {
                        const at = result.lastRetrievedAtUtc;
                        const time = at ? new Date(at + (at.endsWith('Z') ? '' : 'Z')).toLocaleTimeString('vi-VN') : null;
                        note(`ACB chưa xác nhận nhận đủ tiền.${time ? ' Kết quả tra cứu lúc ' + time + '.' : ''} Có thể bấm Kiểm tra ngay sau khi chuyển khoản.`, result.orderId, id);
                    }
                } catch (error) {
                    if (watched.has(id)) { watched.set(id, Date.now() + retryDelay); retrying.add(id); }
                    if (canPoll()) note(error.message, null, id);
                    else pause();
                } finally { checking.delete(id); recording.delete(id); renderCountdown(); }
            }
        } while (discoverPending || manualPending.size > 0);
    }
    function tick(discover) {
        if (!canPoll()) { pause(); return Promise.resolve(); }
        if (paused) { paused = false; discover = true; nextDiscoveryAt = Date.now() + retryDelay; }
        if (activeRun) { discoverPending ||= discover; return activeRun; }
        activeRun = run(discover).finally(() => { activeRun = null; });
        return activeRun;
    }
    window.PosAcb = {
        track(qr) {
            focusedQrId = qr.id;
            if (qr.savedStatus === 'ReviewRequired') forget(qr.id);
            else if (!watched.has(qr.id)) watched.set(qr.id, Date.now() + initialDelay);
            renderCountdown();
        },
        blur() { focusedQrId = null; renderCountdown(); },
        forget,
        async check(qrId = focusedQrId) {
            if (!Number.isInteger(qrId) || qrId <= 0) return;
            if (!canPoll()) { pause(); return; }
            focusedQrId = qrId;
            watched.set(qrId, 0); manualPending.add(qrId);
            renderCountdown();
            const button = document.getElementById('btnCheckAcbPayment');
            if (button) button.disabled = true;
            note('Đang tra cứu giao dịch trực tiếp tại ACB…', null, qrId);
            try { await tick(false); }
            finally { if (button) button.disabled = false; }
        }
    };
    window.addEventListener('acb:payment-changed', () => tick(true));
    window.addEventListener('pos:print-error', event => {
        note('Đơn đã được ghi nhận, nhưng chưa gửi được lệnh in. ' + (event.detail?.message || ''));
        const id = Number(event.detail?.orderId);
        if (Number.isInteger(id) && id > 0 && banner) {
            const link = document.createElement('a'); link.href = `/admin/pos/orders/${id}/print?autoPrint=false`;
            link.textContent = 'Mở phiếu để in lại'; link.target = '_blank'; link.rel = 'noopener'; banner.appendChild(link);
        }
    });
    function connectivityChanged() {
        if (!canPoll()) pause();
        else if (paused) tick(true);
    }
    window.addEventListener('pos:offline-status', connectivityChanged);
    window.addEventListener('pos:offline-synced', () => tick(true));
    window.addEventListener('offline', connectivityChanged);
    window.addEventListener('online', connectivityChanged);
    document.addEventListener('DOMContentLoaded', () => {
        nextDiscoveryAt = Date.now() + retryDelay;
        tick(true);
        setInterval(() => {
            renderCountdown();
            if (activeRun) return;
            const discover = Date.now() >= nextDiscoveryAt;
            if (discover) nextDiscoveryAt = Date.now() + retryDelay;
            if (discover || [...watched.values()].some(at => Date.now() >= at)) tick(discover);
        }, 1000);
    });
})();
