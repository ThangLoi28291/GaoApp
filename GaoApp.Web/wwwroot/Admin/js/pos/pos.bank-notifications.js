(function () {
    'use strict';
    const api = '/admin/pos/bank-notifications';
    const automaticAttempts = new Map();
    const attemptedWaits = new Map();
    const confirmations = new Map();
    const consumedReceipts = new Set();
    const armed = new Map();
    const armAttempts = new Map();
    const armCooldown = 15000, maximumAutoAttempts = 3;
    const manualQrs = new Set();
    const states = new Map();
    let activeQrId = null;
    let running = false;
    let actionBusy = false;
    let activeQr;
    let pane;
    function online() {
        const state = window.PosOffline?.status?.();
        return navigator.onLine !== false && state?.connected !== false && !(state?.pending > 0);
    }
    async function request(path, body) {
        const abort = new AbortController();
        const timeout = setTimeout(() => abort.abort(), 12000);
        try {
            const options = { signal: abort.signal, cache: 'no-store' };
            if (body !== undefined) {
                options.method = 'POST';
                options.headers = { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]')?.value || '' };
                options.body = JSON.stringify(body);
            }
            const response = await fetch(api + path, options);
            if (!response.ok) {
                let message = response.status === 403 ? 'Bạn chưa có quyền xác nhận thanh toán.' : 'Khoản chờ đã thay đổi. Kiểm tra thủ công hoặc thử lại.';
                try { const result = await response.json(); message = result.message || result.detail || message; } catch (_) { }
                const error = new Error(message); error.status = response.status;
                throw error;
            }
            return response.json();
        } finally { clearTimeout(timeout); }
    }
    function ensurePane() {
        if (!pane) {
            pane = document.createElement('div'); pane.id = 'bankNotificationSuggestions';
            pane.setAttribute('aria-live', 'polite');
        }
        const modal = document.getElementById('paymentQrModal');
        const body = modal?.classList?.contains('show') ? modal.querySelector('.modal-body') : null;
        if (body) body.appendChild(pane); else modal?.before(pane);
        return pane;
    }
    function button(label, handler) {
        const b = document.createElement('button'); b.type = 'button'; b.className = 'btn btn-sm btn-outline-primary me-2'; b.textContent = label;
        b.addEventListener('click', async () => {
            if (actionBusy) return;
            actionBusy = true; b.disabled = true;
            try { await handler(); } catch (error) { const p = document.createElement('p'); p.className = 'text-danger mb-0'; p.textContent = error.message; b.parentElement?.appendChild(p); }
            finally { actionBusy = false; b.disabled = false; await poll(); }
        });
        return b;
    }
    const itemKey = item => `${item.receiptId}:${item.waitId}`;
    function transient(error) {
        const status = Number(error?.status) || 0;
        return status === 0 || status === 408 || status === 429 || status >= 500 && status <= 599;
    }
    function onlineWarning() {
        if (!activeQr || activeQr.readOnly || activeQr.automaticConfirmation || manualQrs.has(activeQr.id)) {
            if (pane) pane.hidden = true;
            return;
        }
        const root = ensurePane(); root.hidden = false; root.replaceChildren();
        const message = document.createElement('p'); message.className = 'alert alert-warning mb-0';
        message.textContent = 'Quầy đang chờ kết nối/đồng bộ, chưa tự xác nhận chuyển khoản.';
        root.appendChild(message);
    }
    async function confirm(item, automatic) {
        if (consumedReceipts.has(Number(item.receiptId))) return;
        if (!online()) throw new Error('Chờ kết nối và đồng bộ thao tác tại quầy trước khi xác nhận.');
        const key = itemKey(item);
        if (confirmations.has(key)) return confirmations.get(key);
        const operation = (async () => {
            const result = await request('/confirm', { receiptId: item.receiptId, waitId: item.waitId, automatic });
            consumedReceipts.add(Number(item.receiptId));
            automaticAttempts.delete(key);
            try { await window.PosBankTransferReview?.close(false); }
            catch (_) { /* A display error cannot suppress an acknowledged payment result. */ }
            window.dispatchEvent(new CustomEvent('bank-notification:completed', { detail: result }));
            return result;
        })();
        confirmations.set(key, operation);
        try { return await operation; }
        finally { if (confirmations.get(key) === operation) confirmations.delete(key); }
    }
    function display(items) {
        items = items.filter(item => !manualQrs.has(item.qrId) && !consumedReceipts.has(Number(item.receiptId)));
        const root = ensurePane(); root.replaceChildren();
        for (const item of items.filter(i => i.automatic)) {
            const card = document.createElement('div'); card.className = 'alert alert-info';
            const message = document.createElement('p'); message.className = 'mb-2';
            const amount = Number(item.amount).toLocaleString('vi-VN');
            const time = new Date(item.postedAtUtc).toLocaleTimeString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit', second: '2-digit' });
            message.textContent = `Nhận ${amount}đ từ ${item.senderName || 'người chuyển chưa đọc được tên'} lúc ${time} · Đơn ${item.orderId} · ${item.transactionCode}`;
            card.appendChild(message);
            const attempt = automaticAttempts.get(itemKey(item));
            const priorWait = attemptedWaits.get(Number(item.receiptId));
            const state = document.createElement('p'); state.className = 'mb-2';
            state.textContent = attempt?.error
                ? `Chưa xác nhận được: ${attempt.error}` + (attempt.retryable && attempt.attempts < maximumAutoAttempts ? ' Quầy sẽ tự thử lại.' : ' Kiểm tra thông tin rồi bấm Thử xác nhận lại.')
                : priorWait !== undefined && priorWait !== Number(item.waitId) ? 'Khoản này đã được thử xác nhận cho một yêu cầu khác. Cần kiểm tra thủ công trước khi thử lại.'
                : 'Khớp POS và số tiền. Đang chờ ghi nhận hoặc thử lại.';
            card.appendChild(state);
            card.appendChild(button('Thử xác nhận lại', async () => { await confirm(item, true); }));
            root.appendChild(card);
        }
        const candidates = items.filter(i => !i.automatic);
        const selected = candidates.find(i => Number(i.qrId) === Number(activeQrId)) || candidates[0];
        if (selected) void window.PosBankTransferReview?.show(selected, {
            confirm: async item => {
                actionBusy = true;
                try { await confirm(item, false); } finally { actionBusy = false; await poll(); }
            },
            reject: async item => {
                actionBusy = true;
                try {
                    await request('/dismiss', { receiptId: item.receiptId, waitId: item.waitId });
                    manualQrs.add(item.qrId);
                    await window.PosBankTransferReview.close(false);
                    window.dispatchEvent(new CustomEvent('bank-notification:manual-fallback', { detail: item }));
                } finally { actionBusy = false; await poll(); }
            }
        });
    }
    async function poll() {
        if (!online()) { onlineWarning(); return; }
        if (running || actionBusy || window.PosBankTransferReview?.busy()) return;
        running = true;
        try {
            const approvals = await request('/admin-approvals');
            const adminErrors = [];
            for (const qrId of approvals) {
                if (!online() || actionBusy || window.PosBankTransferReview?.busy()) return;
                try {
                    const result = await request(`/complete-admin/${qrId}`, {});
                    await window.PosBankTransferReview?.close(false);
                    window.dispatchEvent(new CustomEvent('bank-notification:completed', { detail: result }));
                } catch (error) { adminErrors.push(error.message); }
            }
            if (window.PosBankTransferReview?.active()) {
                const reviewModal = document.getElementById('bankTransferReviewModal');
                // Bootstrap may finish hiding this modal outside its review
                // owner. Clear only that stale UI state, never a visible or
                // transitioning decision, then fetch fresh server candidates.
                if (window.PosBankTransferReview.busy() || !reviewModal || reviewModal.classList.contains('show') ||
                    reviewModal.getAttribute('aria-hidden') !== 'true') return;
                await window.PosBankTransferReview.close(false);
                if (window.PosBankTransferReview.active() || window.PosBankTransferReview.busy() || actionBusy || !online()) return;
            }
            if (activeQr && !activeQr.readOnly && !activeQr.automaticConfirmation) await updateStatus(activeQr);
            await armActive(activeQr);
            const items = await request('/pending');
            if (!online() || actionBusy || window.PosBankTransferReview?.busy()) return;
            if (pane) pane.hidden = false;
            const remaining = [];
            for (const item of items) {
                if (consumedReceipts.has(Number(item.receiptId)) || manualQrs.has(item.qrId) || states.get(item.qrId)?.ended) continue;
                const key = itemKey(item), attempt = automaticAttempts.get(key), priorWait = attemptedWaits.get(Number(item.receiptId));
                const sameWait = priorWait === undefined || priorWait === Number(item.waitId);
                const retryDue = !attempt || attempt.retryable && attempt.attempts < maximumAutoAttempts && Date.now() >= attempt.nextAt;
                if (item.automatic && sameWait && retryDue) {
                    const attempts = (attempt?.attempts || 0) + 1;
                    attemptedWaits.set(Number(item.receiptId), Number(item.waitId));
                    // A confirmation can change the order and all waiting QR
                    // candidates. Never display/confirm the old poll snapshot.
                    try { await confirm(item, true); display([]); return; }
                    catch (error) {
                        automaticAttempts.set(key, { attempts, retryable: transient(error),
                            nextAt: Date.now() + Math.min(12000, 3000 * Math.pow(2, attempts - 1)),
                            error: error.message || 'Chưa nhận được phản hồi xác nhận từ máy chủ.' });
                        remaining.push(item);
                    }
                } else remaining.push(item);
            }
            display(remaining);
            const armError = activeQr && armAttempts.get(activeQr.id)?.error;
            if (armError && trackingEligible(activeQr) && armed.get(activeQr.id) !== true) {
                const warning = document.createElement('p'); warning.className = 'alert alert-warning';
                warning.textContent = 'Chưa đăng ký được khoản chờ tự động: ' + armError + ' Quầy sẽ kiểm tra lại.';
                ensurePane().appendChild(warning);
            }
            for (const message of adminErrors) {
                const warning = document.createElement('p'); warning.className = 'alert alert-warning';
                warning.textContent = 'Chưa áp dụng được xác nhận của admin: ' + message;
                ensurePane().appendChild(warning);
            }
        } catch (error) {
            if (trackingEligible(activeQr)) {
                const root = ensurePane(); root.hidden = false;
                const warning = document.createElement('p'); warning.className = 'alert alert-warning mb-0';
                warning.textContent = 'Chưa kiểm tra được khoản chuyển: ' + (error.message || 'Kiểm tra kết nối của quầy.');
                root.replaceChildren(warning);
            }
        }
        finally { running = false; }
    }
    async function track(qr) {
        activeQr = qr;
        activeQrId = qr?.id;
        if (!qr || qr.readOnly || qr.automaticConfirmation || !online()) return;
        if (armed.get(qr.id) === true) {
            window.dispatchEvent(new CustomEvent('bank-notification:tracking', { detail: { qrId: qr.id, enabled: armed.get(qr.id) } }));
            await updateStatus(qr); return;
        }
        await armActive(qr);
        await poll();
    }
    function trackingEligible(qr) {
        return qr && Number(activeQr?.id) === Number(qr.id) && !qr.readOnly && !qr.automaticConfirmation &&
            !qr.manualFallback && !manualQrs.has(qr.id) && !states.get(qr.id)?.ended && !states.get(qr.id)?.manual && online();
    }
    async function armActive(qr) {
        if (!trackingEligible(qr) || armed.get(qr.id) === true) return;
        const previous = armAttempts.get(qr.id);
        if (previous && (previous.inFlight || Date.now() < previous.nextAt)) return;
        const attempt = { inFlight: true, nextAt: Date.now() + armCooldown };
        armAttempts.set(qr.id, attempt);
        try {
            const result = await request(`/arm/${qr.id}`, {});
            if (!trackingEligible(qr)) return;
            armed.set(qr.id, result.enabled === true);
            window.dispatchEvent(new CustomEvent('bank-notification:tracking', { detail: { qrId: qr.id, enabled: result.enabled === true } }));
        } catch (error) {
            // A missing registration must not disable future polls forever.
            // Preserve server rejection text without changing the current QR.
            if (trackingEligible(qr)) attempt.error = error.message;
        } finally { attempt.inFlight = false; }
    }
    function waitingState(id) {
        const sample = states.get(id);
        if (!sample) return null;
        const elapsed = Math.max(0, (Date.parse(sample.serverNowUtc) + Date.now() - sample.sampledAt - Date.parse(sample.startedAtUtc)) / 1000);
        return { ...sample, elapsed, warning: elapsed >= sample.warningAfterSeconds,
            canManual: !sample.ended && elapsed >= sample.manualAfterSeconds };
    }
    async function updateStatus(qr) {
        try {
            const value = await request(`/status/${qr.id}`);
            if (Number(value.qrId) !== Number(qr.id)) return;
            states.set(qr.id, { ...value, sampledAt: Date.now() });
            if (value.manual) manualQrs.add(qr.id);
            window.dispatchEvent(new CustomEvent('bank-notification:tracking', { detail: { qrId: qr.id, ended: value.ended } }));
        } catch (_) { /* Missing server state never grants manual takeover. */ }
    }
    async function beginManual(qr) {
        if (actionBusy || !online()) throw new Error('Cần kết nối máy chủ để chuyển khoản chờ sang kiểm tra thủ công.');
        actionBusy = true;
        try {
            const value = await request(`/manual-review/${qr.id}`, {});
            if (!value.manual) { await updateStatus(qr); throw new Error('Khoản chuyển đã được xử lý. Đang tải lại kết quả.'); }
            manualQrs.add(qr.id);
            window.dispatchEvent(new CustomEvent('bank-notification:manual-fallback', { detail: { qrId: qr.id, orderId: qr.orderId } }));
        } finally { actionBusy = false; }
    }
    window.PosBankNotifications = { track, refresh: poll, beginManual, waitingState, isManual: id => manualQrs.has(id), enabled: id => armed.get(id) };
    setInterval(() => {
        if (activeQr && states.has(activeQr.id)) window.dispatchEvent(new CustomEvent('bank-notification:tracking', { detail: { qrId: activeQr.id } }));
    }, 1000);
    window.addEventListener('online', poll);
    window.addEventListener('pos:offline-status', poll);
    setInterval(poll, 3000);
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', poll); else poll();
}());
