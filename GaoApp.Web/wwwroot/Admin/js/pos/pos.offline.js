/* Durable terminal journal and offline transport for the existing POS UI. */
window.PosOffline = (function () {
    'use strict';
    const core = window.PosOfflineCore;
    const nativeFetch = window.fetch.bind(window);
    const databaseName = 'gao-pos-offline-v1';
    const catalogMaxAgeMs = 15 * 60 * 1000;
    let database, state, catalog, ready = false, writer = false, connected = true, authBlocked = false, storageError = '', blockedPending = false;
    let preparing = false, blockedWriter = false, sessionIssue = null;
    let initPromise, writerRetryTimer, pageActive = true;
    let chain = Promise.resolve(), syncRunning = false, token = '', refreshTimer, unlock, shellReady = false;
    const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
    const fail = (message, status = 409) => json({ message, errorCode: 'POS_OFFLINE_REVIEW', errorType: 'business' }, status);
    const serial = work => { const next = chain.then(work, work); chain = next.catch(() => {}); return next; };
    const uuid = () => crypto.randomUUID();
    const isUnavailable = response => response.status >= 500 || response.status === 408;
    const expired = () => !state || new Date(state.context.expiresAtUtc).getTime() <= Date.now();
    function canWork() { return ready && writer && !authBlocked && !storageError && !sessionIssue && !expired(); }
    function localMode() { return canWork() && (!connected || state.queue.length > 0); }
    function notify() { window.dispatchEvent(new CustomEvent('pos:offline-status')); }
    function status() {
        return { ready, preparing, writer, connected, shellReady, sessionIssue, offline: !connected, pending: state?.queue.length || 0,
            message: storageError || (authBlocked ? 'Cần đăng nhập lại đúng nhân viên để đồng bộ.' : sessionIssue?.message || (!writer && ready ? 'POS đang mở ở tab khác của quầy.' : expired() && ready ? 'Phiên offline đã hết hạn. Kết nối server để chuẩn bị lại quầy.' : state?.conflict?.message || '')),
            expiresAt: state?.context.expiresAtUtc, context: state?.context };
    }
    async function sessionRejected(response) {
        // A business rejection proves the server is reachable. Never turn a
        // closed/missing shift into permission to sell using an old offline cache.
        connected = true;
        const problem = await response.clone().json().catch(() => ({}));
        sessionIssue = {
            code: problem.errorCode || 'POS_SESSION_UNAVAILABLE',
            title: problem.errorCode === 'POS_SHIFT_NOT_OPEN' ? 'Chưa mở ca POS' : 'Cần kiểm tra phiên POS',
            message: [problem.message || 'Phiên POS hiện chưa sẵn sàng.', problem.actionHint].filter(Boolean).join(' ')
        };
    }
    async function openDatabase() {
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
    async function get(store, key) {
        return new Promise((resolve, reject) => {
            const request = database.transaction(store).objectStore(store).get(key);
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }
    async function save(next, nextCatalog) {
        try {
            await new Promise((resolve, reject) => {
                const tx = database.transaction(nextCatalog ? ['sessions', 'catalogs', 'meta'] : ['sessions', 'meta'], 'readwrite');
                tx.objectStore('sessions').put(next, next.key);
                tx.objectStore('meta').put({ key: next.key, expiresAt: next.context.expiresAtUtc }, 'active');
                if (nextCatalog) tx.objectStore('catalogs').put(nextCatalog, next.key);
                tx.oncomplete = resolve;
                tx.onerror = () => reject(tx.error);
                tx.onabort = () => reject(tx.error || new Error('Không lưu được dữ liệu POS.'));
            });
            state = next;
            if (nextCatalog) catalog = nextCatalog;
        } catch (e) {
            storageError = 'Không ghi được dữ liệu trên máy. Chưa xác nhận thao tác; giữ nguyên dữ liệu để kiểm tra bộ nhớ.';
            notify(); throw e;
        }
    }
    async function raw(url, options = {}, timeoutMs = 4000) {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), timeoutMs);
        const abort = () => controller.abort();
        options.signal?.addEventListener('abort', abort, { once: true });
        try {
            const response = await nativeFetch(url, { ...options, credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
            if (response.redirected && !response.headers.get('content-type')?.includes('application/json')) return fail('Cần đăng nhập lại.', 401);
            return response;
        } finally { clearTimeout(timer); options.signal?.removeEventListener('abort', abort); }
    }
    const base = '/admin/pos';
    function handled(url) {
        if (url.origin !== location.origin) return false;
        return /^\/admin\/pos\/(screen|cart\/current(?:\/(?:new|scan|payments|payment-and-finalize|hold|cancel|finalize|note|discount|customer(?:\/\d+)?))?|cart\/ensure|draft|orders\/(?:drafts|held|\d+(?:\/(?:resume|receipt))?)|products\/search|customers\/(?:search|quick-create)|lines\/\d+(?:\/discount)?|payments\/\d+|\d+(?:\/(?:items|payments|finalize|cancel))?|offline\/manual-transfer)$/.test(url.pathname);
    }
    function paymentOperation(url) { return /\/(payments|payment-and-finalize|finalize|manual-transfer)$/.test(new URL(url, location.origin).pathname); }
    function renumber(next) {
        const mapped = {};
        for (const order of Object.values(next.orders)) {
            order.orderId = Number(core.mapId(next, 'order', order.orderId));
            if (order.customerId) order.customerId = Number(core.mapId(next, 'customer', order.customerId));
            for (const line of order.lines) { line.lineId = Number(core.mapId(next, 'line', line.lineId));
                if (line.giftSourceLineId) line.giftSourceLineId = Number(core.mapId(next, 'line', line.giftSourceLineId)); }
            for (const payment of order.payments) payment.paymentId = Number(core.mapId(next, 'payment', payment.paymentId));
            mapped[order.orderId] = order;
        }
        next.orders = mapped;
        for (const customer of next.customers || []) customer.customerId = Number(core.mapId(next, 'customer', customer.customerId));
        for (const qr of Object.values(next.qrs)) qr.orderId = Number(core.mapId(next, 'order', qr.orderId));
        if (next.currentId) next.currentId = Number(core.mapId(next, 'order', next.currentId));
    }
    function operation(url, method, body) {
        const current = state.orders[body?.orderId || state.currentId];
        return { id: uuid(), url: url.pathname + url.search, method, body, occurredAt: new Date().toISOString(), offline: localMode(),
            shiftId: state.context.shiftId, expectedOrderId: (url.pathname.startsWith(base + '/cart/current/') && !url.pathname.endsWith('/new') || url.pathname.endsWith('/customers/quick-create')) ? state.currentId : null,
            expectedTotal: paymentOperation(url.href) && method !== 'DELETE' ? current?.grandTotal : null,
            expectedLines: paymentOperation(url.href) && current ? core.linesSignature(current) : null, localResult: null, serverRequest: null };
    }
    async function send(op, signal) {
        const headers = { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': token || window.PosCommon?.getAntiForgeryToken?.() || '', 'X-POS-Operation-Id': op.id,
            'X-POS-Shift-Id': String(op.shiftId) };
        if (op.serverRequest.expectedOrderId) headers['X-POS-Expected-Order-Id'] = String(op.serverRequest.expectedOrderId);
        if (op.offline) {
            headers['X-POS-Offline'] = '1'; headers['X-POS-Occurred-At'] = op.occurredAt;
            if (op.expectedTotal != null) headers['X-POS-Expected-Total'] = String(op.expectedTotal);
            if (op.expectedLines != null) headers['X-POS-Expected-Lines-Hash'] = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(op.expectedLines))), b => b.toString(16).padStart(2, '0')).join('');
        }
        return raw(op.serverRequest.url, { method: op.method, headers, signal,
            body: op.method === 'DELETE' && op.serverRequest.body == null ? undefined : JSON.stringify(op.serverRequest.body || {}) });
    }
    async function enqueueLocal(op) {
        if (!canWork()) throw new Error(status().message || 'Quầy chưa sẵn sàng bán offline.');
        const next = core.clone(state);
        op.offline = true;
        op.localResult = core.clone(core.apply(next, catalog, op));
        next.queue.push(op);
        await save(next); notify();
        return json(op.localResult);
    }
    async function ensureLocalCart() {
        if (state.currentId) return;
        const op = operation(new URL(base + '/cart/ensure', location.origin), 'POST', {});
        await enqueueLocal(op);
    }
    async function transport(url, options) {
        const method = (options.method || 'GET').toUpperCase();
        if (options.signal?.aborted) throw new DOMException('Aborted', 'AbortError');
        if (!writer) return fail('POS đang mở ở tab khác của quầy. Hãy dùng một tab để tránh ghi đè giỏ.');
        if (authBlocked) return fail('Cần đăng nhập lại đúng nhân viên.', 401);
        let body = null;
        try { if (options.body) body = JSON.parse(options.body); } catch { return nativeFetch(url, options); }
        if (method === 'GET') {
            if (!localMode()) {
                let response;
                try { response = await raw(url.href, options); }
                catch (e) { if (options.signal?.aborted) throw e; connected = false; }
                if (response && !isUnavailable(response)) {
                    if (response.status === 401) { authBlocked = true; notify(); return response; }
                    // An authoritative screen may be shown while the session is
                    // blocked, but must not overwrite unsynced local order data.
                    if (response.ok && !sessionIssue && !state.queue.length) {
                        const data = await response.clone().json();
                        const next = core.clone(state); core.absorb(next, data); await save(next); connected = true;
                    }
                    return response;
                }
                connected = false; notify();
            }
            if (!canWork()) return fail('Dữ liệu offline hết hạn hoặc không lưu được trên máy.');
            if (url.pathname === base + '/screen') await ensureLocalCart();
            return json(core.read(state, catalog, url));
        }
        if (!canWork()) return fail(status().message || 'Quầy chưa sẵn sàng lưu giao dịch.');
        const op = operation(url, method, body);
        if (localMode()) return enqueueLocal(op);
        let predicted = core.clone(state);
        try { op.localResult = core.clone(core.apply(predicted, catalog, op)); }
        catch { predicted = null; }
        op.serverRequest = core.translate(state, op);
        const pending = core.clone(state); pending.queue.push(op);
        await save(pending); // Write intent before making a request with side effects.
        let response;
        try { response = await send(op, options.signal); }
        catch { connected = false; }
        if (!response || isUnavailable(response)) {
            connected = false;
            if (predicted) {
                op.offline = true; predicted.queue = core.clone(state.queue); predicted.queue[predicted.queue.length - 1] = op;
                await save(predicted); notify(); return json(op.localResult);
            }
            const next = core.clone(state); next.conflict = { message: 'Chưa rõ kết quả thao tác. Đang giữ mã yêu cầu để kiểm tra khi server trở lại.', operationId: op.id, recoverable: true };
            await save(next); notify(); return fail(next.conflict.message);
        }
        if (response.status === 401) authBlocked = true;
        if (!response.ok) {
            const next = core.clone(state); next.queue = next.queue.filter(x => x.id !== op.id); await save(next); notify(); return response;
        }
        const result = await response.clone().json();
        const next = predicted || core.clone(state);
        next.queue = state.queue.filter(x => x.id !== op.id);
        core.learn(next, op.localResult, result); renumber(next); core.absorb(next, result);
        await save(next); notify(); return response;
    }
    async function intercepted(input, options = {}) {
        const url = new URL(typeof input === 'string' || input instanceof URL ? input : input.url, location.href);
        if (url.origin !== location.origin) return nativeFetch(input, options);
        if (!ready) {
            const posRequest = url.pathname.startsWith(base + '/') || url.pathname.startsWith('/admin/acb/');
            if (posRequest && blockedWriter) return fail('POS đang mở ở tab khác của quầy.');
            if (posRequest && (blockedPending || state?.queue.length))
                return fail(storageError || 'Còn giao dịch tại quầy cần phục hồi trước khi bán tiếp.');
            if (posRequest && preparing) {
                if (!writer) return fail('POS đang mở ở tab khác của quầy.');
                // Let online sales proceed during preparation, and drain them before taking the final snapshot.
                return serial(() => ready && handled(url) ? transport(url, options) : nativeFetch(input, options));
            }
            return nativeFetch(input, options);
        }
        const history = url.pathname.match(/^\/admin\/acb\/payments\/orders\/(\d+)\/qrs(?:\/(\d+))?$/);
        if (history) return serial(async () => {
            if (!localMode()) {
                try {
                    const response = await raw(url.href, options);
                    if (response.ok) {
                        const data = await response.clone().json();
                        if (data.qr) await rememberQr(data.qr, { readOnly: data.readOnly, canCancel: data.canCancel, savedStatus: data.status });
                        if (!history[2]) {
                            const next = core.clone(state);
                            for (const item of data.items) if (next.qrs[item.qrId]) {
                                next.qrs[item.qrId].savedStatus = item.status;
                                next.qrs[item.qrId].readOnly = ['Recorded', 'Completed', 'Paid', 'ManualConfirmed', 'Cancelled', 'ReviewRequired', 'Failed'].includes(item.status);
                            }
                            await save(next); return json(mergeQrHistory(Number(history[1]), data));
                        }
                        return response;
                    }
                    if (!isUnavailable(response)) return response;
                } catch { /* Use the journal's QR history. */ }
                connected = false; notify();
            }
            if (history[2]) {
                const saved = state.qrs[history[2]];
                if (!saved || Number(core.mapId(state, 'order', saved.orderId)) !== Number(core.mapId(state, 'order', history[1]))) return fail('QR này chưa được lưu trên máy.');
                const qr = manualQr(saved);
                return json({ qr, readOnly: qr.readOnly, canCancel: qr.canCancel, status: qr.savedStatus || (qr.status === 1 ? 'ManualConfirmed' : 'Pending'), message: qr.savedMessage });
            }
            return json(mergeQrHistory(Number(history[1])));
        });
        // QR prepared locally is a transfer instruction, never a bank confirmation.
        if (url.pathname === base + '/cart/current/payment-qr') {
            return serial(async () => {
                try {
                    if (localMode()) return json(await createQr(JSON.parse(options.body || '{}')));
                    const response = await raw(url.href, options, 15000);
                    if (response.ok) await rememberQr(await response.clone().json());
                    return response;
                }
                catch (e) { return fail(e.message); }
            });
        }
        const qrConfirm = url.pathname.match(/^\/admin\/pos\/payment-qr\/(\d+)\/manual-confirm$/);
        if (qrConfirm && state.qrs[qrConfirm[1]] && (state.qrs[qrConfirm[1]].offline || localMode() || state.qrs[qrConfirm[1]].manualRequested)) {
            return serial(async () => {
                const qr = state.qrs[qrConfirm[1]];
                if (qr.result) return json(qr.result);
                if (qr.readOnly) return fail('QR đã kết thúc.');
                try {
                    if (!qr.clientRequestId) { const next = core.clone(state); next.qrs[qr.id].clientRequestId = uuid(); next.qrs[qr.id].manualRequested = true; await save(next); }
                    const response = await transport(new URL(base + '/offline/manual-transfer', location.origin), { method: 'POST', body: JSON.stringify({
                        orderId: Number(core.mapId(state, 'order', qr.orderId)), clientRequestId: state.qrs[qr.id].clientRequestId, bankAccountId: qr.bankAccountId, amount: qr.amount,
                        referenceCode: qr.requestCode || qr.content, existingQrId: qr.offline ? null : qr.id }) });
                    if (response.ok) { const next = core.clone(state); next.qrs[qr.id].status = 1; next.qrs[qr.id].result = await response.clone().json(); await save(next); }
                    return response;
                } catch (e) { return fail(e.message); }
            });
        }
        const qrCancel = url.pathname.match(/^\/admin\/pos\/payment-qr\/(\d+)\/cancel$/);
        if (qrCancel && state.qrs[qrCancel[1]]?.offline) return serial(async () => {
            const next = core.clone(state); if (next.qrs[qrCancel[1]].status === 1) return fail('QR đã nhận tiền.');
            next.qrs[qrCancel[1]].status = 2; await save(next); return json({ success: true });
        });
        if (!handled(url)) {
            if (localMode() && (url.pathname.startsWith('/admin/acb/') || url.pathname.startsWith(base + '/')) && !url.pathname.startsWith(base + '/offline/'))
                return fail('Chức năng này cần kết nối server; các đơn offline vẫn được lưu tại quầy.');
            return nativeFetch(input, options);
        }
        return serial(async () => {
            try { return await transport(url, options); }
            catch (e) { if (e.name === 'AbortError') throw e; return fail(storageError || e.message); }
        });
    }
    async function pages(path) {
        const result = []; let afterId = 0;
        do {
            const response = await raw(path + '?afterId=' + afterId, {}, 15000);
            if (!response.ok) throw new Error('Chưa tải đủ danh mục để bán offline.');
            const page = await response.json(); result.push(...page.items); afterId = page.nextAfterId;
        } while (afterId != null);
        return result;
    }
    // A document owns one initialization and one writer lock, even when the UI boots twice.
    function init() {
        if (!initPromise) initPromise = initialize();
        return initPromise;
    }
    window.addEventListener('pagehide', () => {
        pageActive = false; writer = false; unlock?.();
        clearInterval(refreshTimer); clearInterval(writerRetryTimer);
    });
    function watchWriterRelease(name) {
        clearInterval(writerRetryTimer);
        writerRetryTimer = setInterval(async () => {
            if (!pageActive || !blockedWriter) return;
            try {
                const locks = await navigator.locks.query();
                if (!locks.held.some(lock => lock.name === name) && !locks.pending.some(lock => lock.name === name)) {
                    // Reload the authoritative shift/cart and journal before accepting writes.
                    clearInterval(writerRetryTimer);
                    location.reload();
                }
            } catch { /* Keep the writer guard when lock ownership cannot be verified. */ }
        }, 1500);
    }
    async function takeWriterLock(key) {
        if (!navigator.locks) throw new Error('POS offline cần HTTPS và trình duyệt hỗ trợ khóa quầy (Chrome/Edge).');
        const name = 'gao-pos-terminal:' + key.split(':').slice(0, 2).join(':');
        await new Promise((resolve, reject) => {
            navigator.locks.request(name, { ifAvailable: true }, async lock => {
                if (!pageActive) { resolve(); return; }
                writer = !!lock; blockedWriter = !lock;
                if (blockedWriter) { storageError = 'POS đang mở ở tab khác của quầy. Sẽ tự kiểm tra lại khi tab đó đóng.'; window.fetch = intercepted; watchWriterRelease(name); }
                resolve();
                if (lock) await new Promise(r => { unlock = r; });
            }).catch(reject);
        });
    }
    async function prepareWorker() {
        if (!window.isSecureContext || !navigator.serviceWorker || !writer) return;
        const registration = await navigator.serviceWorker.register('/pos-offline-worker.js', { scope: '/' });
        await navigator.serviceWorker.ready;
        navigator.serviceWorker.addEventListener('message', event => {
            if (event.data?.type === 'pos-offline-prepared' && event.data.key === state.key) { shellReady = true; notify(); }
        });
        const assets = Array.from(document.querySelectorAll('script[src], link[rel="stylesheet"][href]'))
            .map(x => new URL(x.src || x.href, location.href)).filter(x => x.origin === location.origin).map(x => x.href);
        (registration.active || registration.waiting)?.postMessage({ type: 'prepare', key: state.key,
            expiresAt: state.context.expiresAtUtc, page: location.href, assets });
    }
    function startTransport() {
        ready = writer && !!state && !!catalog;
        if (!ready) return;
        window.fetch = intercepted;
        navigator.storage?.persist?.().catch(() => {});
        prepareWorker().catch(() => {});
        refreshTimer = setInterval(() => sync().catch(() => {}), 6000);
        sync().catch(() => {});
    }
    async function prepareCatalog(context) {
        try {
            const [products, customers, promotionsResponse] = await Promise.all([
                pages(base + '/offline/catalog'), pages(base + '/offline/customers'), raw(base + '/offline/promotions')
            ]);
            if (!promotionsResponse.ok) throw new Error('Chưa tải đủ khuyến mãi để bán offline.');
            const nextCatalog = { products, customers, promotions: await promotionsResponse.json(), fetchedAtUtc: new Date().toISOString() };
            for (const product of products) if (!product.units.length && product.baseUnitId)
                product.units.push({ id: null, unitId: product.baseUnitId, unitName: product.baseUnitName, factor: 1,
                    price: product.price, wholesalePrice: null, isBaseUnit: true, isDefaultForSale: true, barcodes: [] });
            await serial(async () => {
                if (!writer) return;
                const statusResponse = await raw(base + '/offline/status');
                if (!statusResponse.ok || core.contextKey(await statusResponse.json()) !== core.contextKey(context))
                    throw new Error('Ca hoặc phiên đăng nhập đã thay đổi. Mở lại POS để chuẩn bị đúng quầy.');
                // The cashier may have sold/held/switched carts while the catalog was downloading.
                const screenResponse = await raw(base + '/screen');
                if (!screenResponse.ok) throw new Error('Chưa lấy được giỏ mới nhất để chuẩn bị offline.');
                const latestScreen = await screenResponse.json();
                const next = core.clone(state); next.context = { ...context, screen: latestScreen };
                core.absorb(next, latestScreen);
                for (const item of [...latestScreen.draftOrders, ...latestScreen.heldOrders.filter(x => x.isCurrentTerminal && x.isCurrentShift)]) {
                    if (next.orders[item.orderId]) continue;
                    const draftResponse = await raw(base + '/' + item.orderId);
                    if (draftResponse.ok) next.orders[item.orderId] = await draftResponse.json();
                }
                await save(next, nextCatalog);
                preparing = false;
                startTransport();
            });
        } catch (e) {
            storageError = e.message || 'Chưa chuẩn bị được POS offline.';
        } finally { preparing = false; notify(); }
    }
    async function initialize() {
        try {
            database = await openDatabase();
            const shell = document.getElementById('posShell');
            const prefix = `${shell?.dataset.storeId}:${shell?.dataset.terminalId}:${shell?.dataset.userId}:`;
            const active = await get('meta', 'active');
            if (active?.key?.startsWith(prefix)) { state = await get('sessions', active.key); catalog = await get('catalogs', active.key); }
            let response;
            try { response = await raw(base + '/offline/bootstrap', {}, 6000); }
            catch { connected = false; }
            if (response?.ok) {
                const context = await response.json(); token = context.antiForgeryToken;
                const key = core.contextKey(context);
                const sessions = await new Promise((resolve, reject) => {
                    const query = database.transaction('sessions').objectStore('sessions').getAll();
                    query.onsuccess = () => resolve(query.result); query.onerror = () => reject(query.error);
                });
                const old = sessions.find(s => s.queue.length && s.context.storeId === context.storeId && s.context.terminalId === context.terminalId && s.key !== key);
                if (old) {
                    blockedPending = true; window.fetch = intercepted;
                    throw new Error('Quầy còn giao dịch của ca hoặc nhân viên trước. Đăng nhập đúng ca gốc để đồng bộ/đối soát trước khi bán tiếp.');
                }
                const stored = await get('sessions', key);
                state = stored || core.initial(context); catalog = await get('catalogs', key);
                await takeWriterLock(key);
                window.addEventListener('online', () => sync().catch(() => {}));
                window.addEventListener('offline', () => { connected = false; notify(); });
                window.addEventListener('pagehide', () => { writer = false; unlock?.(); clearInterval(refreshTimer); });
                window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
                if (writer) {
                    if (state.queue.length && !catalog) throw new Error('Thiếu danh mục gốc của giao dịch chờ đồng bộ. Cần phục hồi dữ liệu tại quầy.');
                    const cacheAge = Date.now() - new Date(catalog?.fetchedAtUtc).getTime();
                    if (catalog && (state.queue.length || cacheAge >= 0 && cacheAge < catalogMaxAgeMs && !expired())) {
                        const next = core.clone(state); next.context = context;
                        if (!next.queue.length) core.absorb(next, context.screen);
                        await save(next);
                    } else {
                        preparing = true;
                        window.fetch = intercepted;
                        // Only context, pending-journal and writer checks block POS startup.
                        prepareCatalog(context);
                    }
                }
                connected = true;
            } else {
                if (response && [401, 403].includes(response.status)) { authBlocked = true; state = null; }
                else if (response && !isUnavailable(response)) await sessionRejected(response);
                else connected = false;
                if (state && catalog && !expired()) await takeWriterLock(state.key);
                window.addEventListener('online', () => sync().catch(() => {}));
                window.addEventListener('offline', () => { connected = false; notify(); });
                window.addEventListener('pagehide', () => { writer = false; unlock?.(); clearInterval(refreshTimer); });
                window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
            }
            if (!preparing) startTransport();
        } catch (e) {
            storageError = e.message || 'Chưa chuẩn bị được POS offline.';
            // Never replace a pending journal after a failed setup.
            if (state?.queue.length) window.fetch = intercepted;
        }
        notify();
    }
    async function sync(force = false) {
        if (syncRunning || !ready || !writer || storageError || authBlocked || (state.conflict && !state.conflict.recoverable && !force)) return;
        syncRunning = true;
        const hadPending = state.queue.length > 0, wasConnected = connected, hadSessionIssue = !!sessionIssue;
        try {
            const response = await raw(base + '/offline/status');
            if ([401, 403].includes(response.status)) { connected = true; sessionIssue = null; authBlocked = true; notify(); return; }
            if (!response.ok) {
                if (isUnavailable(response)) connected = false;
                else await sessionRejected(response);
                notify(); return;
            }
            const info = await response.json();
            connected = true;
            if (core.contextKey(info) !== state.key) {
                sessionIssue = { code: 'POS_SESSION_CHANGED', title: 'Ca hoặc nhân viên đã thay đổi',
                    message: state.queue.length ? 'Quầy còn giao dịch của phiên trước. Cần đồng bộ/đối soát đúng ca gốc trước khi bán tiếp.' : 'Tải lại POS để sử dụng ca và nhân viên hiện tại.' };
                notify(); return;
            }
            sessionIssue = null;
            token = info.antiForgeryToken;
            if (info.receiptStoreInfo && info.receiptStoreInfo.rowVersion !== state.context.receiptStoreInfo?.rowVersion)
                await serial(async () => { const next = core.clone(state); next.context.receiptStoreInfo = info.receiptStoreInfo; await save(next); });
            if (force || state.conflict?.recoverable) await serial(async () => { const next = core.clone(state); next.conflict = null; await save(next); });
            while (state.queue.length && !state.conflict) {
                await serial(async () => {
                    const next = core.clone(state), op = next.queue[0];
                    if (!op) return;
                    if (!op.serverRequest) { op.serverRequest = core.translate(next, op); await save(next); }
                    let replay;
                    try { replay = await send(op); }
                    catch { connected = false; return; }
                    if (isUnavailable(replay)) { connected = false; return; }
                    if (!replay.ok) {
                        const data = await replay.json().catch(() => ({}));
                        const failed = core.clone(state); failed.conflict = { operationId: op.id, message: data.message || 'Một giao dịch cần đối soát trước khi gửi tiếp.' };
                        if (replay.status === 401) authBlocked = true;
                        await save(failed); return;
                    }
                    const result = await replay.json(), accepted = core.clone(state);
                    if (op.offline && op.expectedTotal != null && (core.draftOf(result)?.grandTotal !== op.expectedTotal ||
                        op.expectedLines != null && core.linesSignature(core.draftOf(result)) !== op.expectedLines)) {
                        accepted.conflict = { operationId: op.id, message: 'Server đã ghi nhận thao tác nhưng tổng tiền khác phiếu tại quầy. Cần đối soát trước khi gửi tiếp.' };
                        await save(accepted); return;
                    }
                    core.learn(accepted, op.localResult, result); accepted.queue.shift();
                    if (!accepted.queue.length) { renumber(accepted); core.absorb(accepted, result); }
                    await save(accepted);
                });
                notify();
                if (!connected || authBlocked) break;
            }
            if (!state.queue.length && connected && (hadPending || !wasConnected || hadSessionIssue)) window.dispatchEvent(new CustomEvent('pos:offline-synced'));
        } catch { connected = false; }
        finally { syncRunning = false; notify(); }
    }
    async function rememberQr(qr, extra = {}) {
        if (!qr?.id || !qr.orderId) return;
        const next = core.clone(state);
        next.qrs[qr.id] = { ...next.qrs[qr.id], ...core.clone(qr), ...extra };
        await save(next);
    }
    function manualQr(qr) {
        if (!qr) return qr;
        if (localMode() && !qr.readOnly && !qr.result) return { ...qr, automaticConfirmation: false, manualRequested: true,
            canCancel: !!qr.offline, savedMessage: 'Mất kết nối: kiểm tra tiền thực nhận rồi bấm Đã nhận thủ công. Khoản thu sẽ gửi về server sau.' };
        return qr;
    }
    async function prepareManualConfirmation(qr) {
        if (!qr?.manualRequested) return;
        if (!canWork()) throw new Error('Quầy chưa sẵn sàng ghi khoản thu thủ công.');
        await serial(() => rememberQr(qr, { manualRequested: true }));
    }
    function mergeQrHistory(orderId, remote = { items: [] }) {
        const items = [...remote.items];
        for (const qr of Object.values(state.qrs).filter(q => Number(core.mapId(state, 'order', q.orderId)) === Number(core.mapId(state, 'order', orderId)))) {
            if (items.some(x => x.qrId === qr.id)) continue;
            items.push({ qrId: qr.id, amount: qr.amount, requestCode: qr.requestCode, bankName: qr.bankName,
                createdAtUtc: qr.createdAtUtc || state.context.preparedAtUtc,
                status: qr.result ? 'ManualConfirmed' : qr.status === 2 && qr.offline ? 'Cancelled' : qr.savedStatus || 'Pending', canReopen: true });
        }
        items.sort((a, b) => b.qrId - a.qrId);
        return { ...remote, orderId, items, latestQrId: items.find(x => ['Pending', 'Creating'].includes(x.status))?.qrId || null };
    }
    async function createQr(body) {
        if (!canWork()) throw new Error('Quầy chưa sẵn sàng tạo QR offline.');
        core.permission(state, 'pos.payment.create');
        const draft = core.current(state), amount = Number(body.amount || draft.balanceDue);
        if (amount > draft.balanceDue) throw new Error('Số tiền QR vượt số còn thiếu.');
        const account = state.context.accounts.find(x => body.bankAccountId ? x.id === body.bankAccountId : /^\d{6}$/.test(x.vietQrBankBin || ''));
        if (!account) throw new Error('Cần cấu hình BIN VietQR và tài khoản nhận tiền trước khi bán offline.');
        const previous = Object.values(state.qrs).find(x => x.orderId === draft.orderId && x.clientRequestId === body.clientRequestId && x.status === 0);
        if (previous) return previous;
        const next = core.clone(state), qrId = ++next.nextId, clientRequestId = body.clientRequestId || uuid();
        const content = 'GAO' + state.context.terminalId + clientRequestId.replace(/-/g, '').slice(0, 24).toUpperCase();
        const payload = core.vietQr(account.vietQrBankBin, account.accountNumber, amount, content);
        const matrix = qrcodegen.QrCode.encodeText(payload, qrcodegen.QrCode.Ecc.QUARTILE);
        const canvas = document.createElement('canvas'), scale = 6, border = 4;
        canvas.width = canvas.height = (matrix.size + border * 2) * scale;
        const drawing = canvas.getContext('2d'); drawing.fillStyle = '#fff'; drawing.fillRect(0, 0, canvas.width, canvas.height);
        drawing.fillStyle = '#000';
        for (let y = 0; y < matrix.size; y++) for (let x = 0; x < matrix.size; x++) if (matrix.getModule(x, y)) drawing.fillRect((x + border) * scale, (y + border) * scale, scale, scale);
        const qr = { id: qrId, orderId: draft.orderId, bankAccountId: account.id, bankCode: account.bankCode, bankName: account.bankName,
            accountNumber: account.accountNumber, accountName: account.accountName, amount, content, requestCode: content, status: 0,
            automaticConfirmation: false, offline: true, clientRequestId, qrRawText: payload, qrDataUrl: canvas.toDataURL('image/png'),
            createdAtUtc: new Date().toISOString(), expireAtUtc: state.context.expiresAtUtc };
        next.qrs[qrId] = qr; await save(next); return qr;
    }
    function print(orderId) {
        const order = state?.orders[orderId] || state?.orders[core.mapId(state, 'order', orderId)];
        if (!order || !localMode()) return false;
        core.permission(state, 'pos.order.reprint');
        if (!window.PosPrinting) throw new Error('Chưa tải được bộ mẫu in offline.');
        const storeInfo = state.context.receiptStoreInfo || {};
        window.PosPrinting.openLocal({ ...order, storeName: storeInfo.storeName || state.context.storeName,
            storeAddress: storeInfo.storeAddress, storePhone: storeInfo.storePhone,
            terminalName: state.context.terminalName, cashierName: state.context.userName },
            state.context, state.context.receiptTemplates, true);
        return true;
    }
    function exportPending() {
        if (!state) return;
        const exported = core.clone(state); delete exported.context.antiForgeryToken;
        const blob = new Blob([JSON.stringify({ exportedAtUtc: new Date().toISOString(), state: exported }, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob), anchor = document.createElement('a'); anchor.href = url;
        anchor.download = `gao-pos-pending-${state.context.terminalId}-${Date.now()}.json`; anchor.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    return { init, canWork, localMode, status, sync, print, exportPending, createQr, manualQr, prepareManualConfirmation,
        resolveOrderId: id => state ? Number(core.mapId(state, 'order', id)) : id };
})();
