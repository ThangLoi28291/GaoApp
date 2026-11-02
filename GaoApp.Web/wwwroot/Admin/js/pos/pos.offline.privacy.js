/* Retention and authentication fences shared by the POS page and its worker. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.PosOfflinePrivacy = api;
    if (typeof window !== 'undefined' && window.document) api.listen();
})(typeof window !== 'undefined' ? window : globalThis, function () {
    'use strict';
    const version = 1, databaseName = 'gao-pos-offline-v1', shells = 'gao-pos-shells-v1';
    const marker = 'gao-pos-privacy-lock-v1';
    let channel, startup;
    const nonce = () => typeof crypto.randomUUID === 'function' ? crypto.randomUUID() :
        Array.from(crypto.getRandomValues(new Uint32Array(4)), value => value.toString(16).padStart(8, '0')).join('');
    function utc(value) {
        if (typeof value !== 'string' || !value) return NaN;
        return Date.parse(/(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : value + 'Z');
    }
    function pending(state) {
        // Unknown journals are retained for recovery, never guessed to be disposable.
        if (!state?.context || !Array.isArray(state.queue)) return true;
        return !!(state.queue.length || state.quarantined?.length || state.conflict ||
            state.customers?.some(value => Number(value.customerId) >= 1500000000 && !state.maps?.customer?.[value.customerId]) ||
            Object.values(state.qrs || {}).some(qr => !qr.result && (qr.manualRequested || qr.offline && qr.status === 0)) ||
            Object.values(state.orders || {}).some(order =>
                order.invoiceIntent?.pendingSync || order.status === 2 &&
                (order.invoiceIntentRequired || order.offline) && ![1, 2].includes(order.invoiceIntent?.route) ||
                Number(order.orderId) >= 1500000000 && !state.maps?.order?.[order.orderId] &&
                (order.lines?.length || order.payments?.length)));
    }
    function customer(value) {
        return { customerId: value.customerId, name: value.name, phone: value.phone || null,
            priceTier: value.priceTier || 'RETAIL', askBeforePrintingReceipt: value.askBeforePrintingReceipt === true };
    }
    function minimize(state, catalog) {
        if (!state?.context || !Array.isArray(state.queue) || !catalog) return { state, catalog };
        const customers = new Map(), keep = new Set();
        const remember = order => {
            if (!order?.customerId) return;
            keep.add(Number(order.customerId));
            if (order.customerName) customers.set(Number(order.customerId), customer({ customerId: order.customerId,
                name: order.customerName, phone: order.customerPhone, priceTier: order.customerPriceTier,
                askBeforePrintingReceipt: order.askBeforePrintingReceipt }));
        };
        for (const order of Object.values(state.orders || {}))
            if ([0, 1].includes(order.status)) remember(order);
        for (const op of [...state.queue, ...(state.quarantined || [])]) {
            const result = op.localResult;
            remember(result?.draft || result?.data || result?.currentDraft || result);
            if (op.body?.customerId) keep.add(Number(op.body.customerId));
            const match = op.url?.match(/\/customer\/(\d+)(?:\?|$)/);
            if (match) keep.add(Number(match[1]));
        }
        state.customers = (state.customers || []).filter(value => keep.has(Number(value.customerId)) ||
            Number(value.customerId) >= 1500000000 && !state.maps?.customer?.[value.customerId])
            .map(value => Number(value.customerId) >= 1500000000 && !state.maps?.customer?.[value.customerId] ? value : customer(value));
        for (const value of state.customers) {
            keep.add(Number(value.customerId));
            customers.set(Number(value.customerId), customer(value));
        }
        for (const value of catalog.customers || [])
            if (keep.has(Number(value.customerId)) && !customers.has(Number(value.customerId)))
                customers.set(Number(value.customerId), customer(value));
        return { state, catalog: { ...catalog, privacyVersion: version, customers: [...customers.values()] } };
    }
    function open() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, 1);
            request.onupgradeneeded = () => {
                for (const name of ['sessions', 'catalogs', 'meta']) request.result.createObjectStore(name);
            };
            request.onsuccess = () => { request.result.onversionchange = () => request.result.close(); resolve(request.result); };
            request.onerror = () => reject(request.error);
            request.onblocked = () => reject(new Error('Đóng tab POS cũ để cập nhật dữ liệu offline.'));
        });
    }
    function auth(db) {
        return new Promise((resolve, reject) => {
            const request = db.transaction('meta').objectStore('meta').get('auth');
            request.onsuccess = () => resolve(request.result || null);
            request.onerror = () => reject(request.error);
        });
    }
    function authorize(db, expected, context) {
        return new Promise((resolve, reject) => {
            let result, error;
            const tx = db.transaction('meta', 'readwrite'), store = tx.objectStore('meta'), request = store.get('auth');
            request.onsuccess = () => {
                const current = request.result;
                if ((current?.revision || null) !== (expected?.revision || null) || !context.deviceBinding) {
                    error = Object.assign(new Error('Phiên đã thay đổi. Đăng nhập lại để mở POS.'), { privacyLock: true }); tx.abort(); return;
                }
                const key = `${context.storeId}:${context.terminalId}:${context.userId}:${context.shiftId}`;
                result = { key, deviceBinding: context.deviceBinding, locked: false,
                    revision: current && !current.locked && current.key === key && current.deviceBinding === context.deviceBinding
                        ? current.revision : nonce() };
                store.put(result, 'auth');
            };
            tx.oncomplete = () => resolve(result);
            tx.onabort = tx.onerror = () => reject(error || tx.error || new Error('Không xác thực được bộ nhớ POS.'));
        });
    }
    function commit(db, state, catalog) {
        return new Promise((resolve, reject) => {
            let error;
            const tx = db.transaction(['sessions', 'catalogs', 'meta'], 'readwrite'), meta = tx.objectStore('meta');
            const request = meta.get('auth');
            request.onsuccess = () => {
                const current = request.result;
                if (!current || current.locked || current.revision !== state.privacyRevision || current.key !== state.key ||
                    current.deviceBinding !== state.context.deviceBinding) {
                    error = Object.assign(new Error('Phiên POS đã bị khóa. Đăng nhập lại đúng nhân viên để tiếp tục.'), { privacyLock: true }); tx.abort(); return;
                }
                const stored = structuredClone(state);
                delete stored.context.antiForgeryToken;
                tx.objectStore('sessions').put(stored, state.key);
                if (catalog) tx.objectStore('catalogs').put(catalog, state.key);
                meta.put({ key: state.key, expiresAt: state.context.expiresAtUtc, privacyVersion: version,
                    revision: current.revision }, 'active');
            };
            tx.oncomplete = resolve;
            tx.onabort = tx.onerror = () => reject(error || tx.error || new Error('Không lưu được dữ liệu POS.'));
        });
    }
    function sweep(db, { reason = 'startup', keepKey = null, now = Date.now() } = {}) {
        return new Promise((resolve, reject) => {
            const tx = db.transaction(['sessions', 'catalogs', 'meta'], 'readwrite');
            const sessions = tx.objectStore('sessions'), catalogs = tx.objectStore('catalogs'), meta = tx.objectStore('meta');
            const queries = [sessions.getAll(), sessions.getAllKeys(), catalogs.getAllKeys(), meta.get('active'), meta.get('auth')];
            const removed = [], retained = [];
            let remaining = queries.length;
            for (const query of queries) query.onsuccess = () => {
                if (--remaining) return;
                const [values, sessionKeys, catalogKeys, active, authentication] = queries.map(q => q.result);
                const existing = new Set(); let activeLocked = false;
                for (let index = 0; index < values.length; index++) {
                    const state = values[index], key = state?.key || sessionKeys[index];
                    if (!state?.key) {
                        existing.add(key); retained.push(key);
                        if (active?.key === key) activeLocked = true;
                        continue;
                    }
                    if (state.context) delete state.context.antiForgeryToken;
                    const expiresAt = utc(state.context?.expiresAtUtc);
                    const lock = ['logout', 'unauthorized'].includes(reason) || !(expiresAt > now) || keepKey && key !== keepKey || state.privacyLocked;
                    if (lock && !pending(state)) {
                        sessions.delete(key); catalogs.delete(key); removed.push(key);
                    } else {
                        existing.add(key);
                        if (lock) {
                            state.privacyLocked = true;
                            if (state.context) { delete state.context.antiForgeryToken; delete state.context.screen; }
                            retained.push(key);
                        }
                        const query = catalogs.get(key);
                        query.onsuccess = () => {
                            const reduced = minimize(state, query.result);
                            sessions.put(reduced.state, key);
                            if (reduced.catalog) catalogs.put(reduced.catalog, key);
                        };
                    }
                    if (active?.key === key && lock) activeLocked = true;
                }
                for (const key of catalogKeys) if (!existing.has(key)) catalogs.delete(key);
                if (active && (activeLocked || !existing.has(active.key) || active.privacyVersion !== version || !(utc(active.expiresAt) > now)))
                    meta.delete('active');
                if (['logout', 'unauthorized'].includes(reason) || activeLocked)
                    meta.put({ ...authentication, revision: nonce(), locked: true, reason }, 'auth');
            };
            tx.oncomplete = () => resolve({ removed, retained });
            tx.onabort = tx.onerror = () => reject(tx.error || new Error('Chưa dọn được dữ liệu POS trên máy.'));
        });
    }
    async function pruneShells(db) {
        if (typeof caches === 'undefined') return;
        const active = await new Promise((resolve, reject) => {
            const request = db.transaction('meta').objectStore('meta').get('active');
            request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
        });
        const authentication = await auth(db), cache = await caches.open(shells);
        const keep = active?.privacyVersion === version && !authentication?.locked &&
            authentication?.revision === active.revision && utc(active.expiresAt) > Date.now()
            ? '/__pos-shell__/' + encodeURIComponent(active.key) : null;
        for (const request of await cache.keys()) if (new URL(request.url).pathname !== keep) await cache.delete(request);
    }
    function announce(reason, publish = true, local = true) {
        const data = { type: 'pos-privacy-lock', reason, id: nonce() };
        try { window.localStorage.setItem(marker, JSON.stringify(data)); } catch { /* BroadcastChannel and worker remain available. */ }
        if (local) window.dispatchEvent(new CustomEvent('pos:privacy-lock', { detail: data }));
        if (publish) channel?.postMessage(data);
    }
    async function clean(reason, keepKey = null) {
        const db = await open();
        try { await sweep(db, { reason, keepKey }); await pruneShells(db); }
        finally { db.close(); }
    }
    function listen() {
        if (typeof BroadcastChannel !== 'undefined') {
            channel = new BroadcastChannel(marker);
            channel.onmessage = event => { if (event.data?.type === 'pos-privacy-lock') announce(event.data.reason, false); };
        }
        window.addEventListener('storage', event => {
            if (event.key === marker && event.newValue) {
                try { const data = JSON.parse(event.newValue); window.dispatchEvent(new CustomEvent('pos:privacy-lock', { detail: data })); }
                catch { /* Ignore malformed cross-tab messages. */ }
            }
        });
        window.navigator.serviceWorker?.addEventListener('message', event => {
            if (event.data?.type === 'pos-privacy-lock') announce(event.data.reason, false);
        });
        const submitted = new WeakSet();
        window.document.addEventListener('submit', event => {
            const form = event.target;
            if (form?.tagName !== 'FORM' || form.getAttribute('method')?.toLowerCase() !== 'post' || submitted.has(form)) return;
            const url = new URL(form.getAttribute('action') || window.location.href, window.location.href);
            if (url.origin !== window.location.origin || !/\/(?:account|auth)\/logout$/i.test(url.pathname)) return;
            event.preventDefault(); submitted.add(form); announce('logout');
            clean('logout').catch(() => {}).finally(() => HTMLFormElement.prototype.submit.call(form));
        }, true);
        // Also removes expired caches when the user returns to a non-POS admin page.
        const enteringLogin = /\/account\/login$/i.test(window.location.pathname);
        if (enteringLogin) announce('logout');
        startup = clean(enteringLogin ? 'logout' : 'startup').catch(() => {});
    }
    return { version, utc, pending, minimize, open, auth, authorize, commit, sweep, pruneShells, announce, clean, listen,
        settled: () => startup || Promise.resolve() };
});
