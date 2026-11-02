(function (root) {
    'use strict';
    root.CustomerDisplayPayment = {
        create(options) {
            let sequence = 0, timer = null, available = false, lastKey = null, binding = null, receiptUntil = 0;
            let inFlight = false, activeAbort = null;
            const seen = new Set();
            const safeImage = value => typeof value === 'string' && value.length < 3000000 &&
                /^data:image\/(png|jpeg|webp);base64,[A-Za-z0-9+/=\r\n]+$/.test(value);
            function unavailable() {
                available = false;
                lastKey = null;
                receiptUntil = 0;
                clearTimeout(timer);
                options.clear();
                options.unavailable();
            }
            async function refresh(force = false) {
                if (inFlight && !force) return;
                if (activeAbort) activeAbort.abort();
                const request = ++sequence;
                const abort = new AbortController();
                activeAbort = abort;
                inFlight = true;
                const deadline = setTimeout(() => abort.abort(), 5000);
                try {
                    const response = await fetch('/admin/pos/customer-display/payment-state', {
                        credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }, signal: abort.signal
                    });
                    if (request !== sequence) return;
                    if (!response.ok || response.redirected) throw new Error('Payment state unavailable');
                    const state = await response.json();
                    if (request !== sequence) return;
                    const until = Date.parse(state.validUntilUtc);
                    if (String(state.storeId) !== String(options.storeId()) ||
                        String(state.terminalId) !== String(options.terminalId()) || !Number.isFinite(until) || until <= Date.now() ||
                        !['idle', 'preview', 'qr', 'recorded'].includes(state.mode) ||
                        (state.mode === 'qr' && !safeImage(state.payload?.qrDataUrl)))
                        throw new Error('Invalid payment state');
                    if (!available) options.available?.();
                    available = true;
                    clearTimeout(timer);
                    timer = setTimeout(unavailable, Math.min(5000, until - Date.now()));
                    const nextBinding = state.payload?.kind === 'deposit' ? state.key : String(state.orderId ?? '');
                    if (nextBinding !== binding) { options.clear(); binding = nextBinding; lastKey = null; receiptUntil = 0; }
                    const receipt = state.receipt || (state.mode === 'recorded'
                        ? { key: state.key, payload: state.payload, validUntilUtc: state.validUntilUtc } : null);
                    if (receipt && Date.parse(receipt.validUntilUtc) > Date.now() && !seen.has(receipt.key)) {
                        seen.add(receipt.key);
                        if (seen.size > 32) seen.delete(seen.values().next().value);
                        options.recorded(receipt.payload);
                        receiptUntil = Date.now() + Math.min(4500, Date.parse(receipt.validUntilUtc) - Date.now());
                        lastKey = null;
                        return;
                    }
                    if (receiptUntil > Date.now()) return;
                    if (receiptUntil) { options.clear(); receiptUntil = 0; }
                    if (state.mode !== 'recorded' && lastKey !== state.key) {
                        if (state.mode === 'qr') options.qr(state.payload);
                        else if (state.mode === 'preview') options.preview(state.payload);
                        else options.hide();
                    }
                    lastKey = state.key;
                } catch {
                    if (request === sequence) unavailable();
                } finally {
                    clearTimeout(deadline);
                    if (request === sequence) { inFlight = false; activeAbort = null; }
                }
            }
            function invalidate() {
                ++sequence;
                lastKey = null;
                options.hide();
                return refresh(true);
            }
            return { refresh, invalidate, isAvailable: () => available };
        }
    };
})(window);
