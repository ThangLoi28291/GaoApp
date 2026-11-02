(function (root) {
    'use strict';
    const operations = new Set(['claim', 'report', 'submit', 'plan', 'approve', 'reopen', 'reassign']);
    const states = { Created: 'Chờ soạn', Picking: 'Đang soạn', AwaitingApproval: 'Chờ quản lý duyệt', ReadyForHandover: 'Đã chốt soạn', HandedOver: 'Đã bàn giao', Delivering: 'Đang giao', AwaitingReconciliation: 'Chờ đối soát', ReadyToSettle: 'Chờ chốt thanh toán', CancellationPending: 'Chờ xử lý hủy', Settled: 'Đã chốt', Cancelled: 'Đã hủy' };
    function scaledQuantity(value) {
        if (typeof value !== 'string' || value.length > 19 || !/^[0-9]+(?:\.[0-9]{1,4})?$/.test(value)) throw new Error('Số lượng dùng dấu chấm và tối đa 4 số lẻ.');
        const [whole, fraction = ''] = value.split('.');
        const scaled = BigInt(whole) * 10000n + BigInt(fraction.padEnd(4, '0'));
        if (scaled >= 1000000000000000000n) throw new Error('Số lượng vượt giới hạn.');
        return scaled;
    }
    function displayExact(value, minimumFraction = 0) {
        if (typeof value !== 'string' || !/^[0-9]+(?:\.[0-9]{1,6})?$/.test(value)) throw new Error('Dữ liệu số không hợp lệ.');
        let [whole, fraction = ''] = value.split('.');
        whole = BigInt(whole).toString().replace(/\B(?=(\d{3})+(?!\d))/g, '.');
        fraction = fraction.replace(/0+$/, '').padEnd(minimumFraction, '0');
        return whole + (fraction ? ',' + fraction : '');
    }
    function money(value) { return displayExact(value) + ' đ'; }
    function costQualityLabel(fragment) {
        const labels = { Final: 'Giá vốn đã chốt', Provisional: 'Giá vốn tạm tính', Unknown: 'Chưa xác định giá vốn', Resolved: 'Nguồn đã xử lý; giữ số ghi nhận ban đầu' };
        if (!Object.hasOwn(labels, fragment.costQuality)) throw new Error('Trạng thái giá vốn chưa hợp lệ.');
        const finalId = value => Number.isSafeInteger(value) && value > 0;
        if (fragment.costQuality === 'Final' ? !finalId(fragment.inventoryCostLayerId) || !finalId(fragment.inventoryCostBudgetDebitId) :
            fragment.inventoryCostLayerId !== null || fragment.inventoryCostBudgetDebitId !== null) throw new Error('Chứng cứ giá vốn không khớp trạng thái nguồn.');
        return labels[fragment.costQuality];
    }
    function parseUtcDate(value) {
        const text = typeof value === 'string' && !/(?:Z|[+-][0-9]{2}:[0-9]{2})$/i.test(value) ? value + 'Z' : value;
        const date = new Date(text); if (!Number.isFinite(date.getTime())) throw new Error('Thời gian máy chủ không hợp lệ.'); return date;
    }
    function canApplyProjection(current, incoming, requestEpoch, latestEpoch) {
        return requestEpoch === latestEpoch && current?.delivery.id === incoming?.delivery.id &&
            Number.isSafeInteger(incoming?.delivery.revision) && incoming.delivery.revision >= current.delivery.revision;
    }
    function validNote(value, label) { const text = value.trim(); if (!text || text.length > 1000) throw new Error(label + ' cần có nội dung, tối đa 1.000 ký tự.'); return text; }
    function reportLine(line, value, reason) {
        const actual = scaledQuantity(value), planned = scaledQuantity(line.plannedQuantityText);
        if (actual > planned) throw new Error(line.itemName + ': lượng soạn vượt kế hoạch.');
        const note = reason.trim();
        if ((actual === 0n || actual < planned) && !note) throw new Error(line.itemName + ': cần ghi lý do thiếu hàng.');
        if (note.length > 1000) throw new Error('Lý do tối đa 1.000 ký tự.');
        return { lineId: line.lineId, pickedQuantityText: value, shortageReason: note || null };
    }
    function validRevision(value) { return value && /^[1-9][0-9]{0,8}$/.test(String(value)) ? String(value) : null; }
    function parseLookup(raw, origin) {
        const text = raw.trim();
        if (/^https?:\/\//i.test(text)) {
            const url = new URL(text);
            if (url.origin !== origin || url.pathname !== '/admin/deliveries') throw new Error('QR không thuộc cửa hàng này.');
            return { key: url.searchParams.get('key') || '', revision: validRevision(url.searchParams.get('revision')) };
        }
        return { key: text, revision: null };
    }
    function createIntentStore(storage, scope) {
        if (!scope) throw new Error('Không xác định được phiên nhân viên. Hãy đăng nhập lại.');
        const key = 'gao.delivery.picking.v1:' + scope;
        function read() {
            const raw = storage.getItem(key); if (!raw) return null;
            let intent; try { intent = JSON.parse(raw); } catch { throw new Error('Yêu cầu đang lưu không đọc được. Cần kiểm tra trước khi thao tác tiếp.'); }
            if (intent.schema !== 1 || !Number.isSafeInteger(intent.deliveryId) || intent.deliveryId <= 0 || !operations.has(intent.operation) || typeof intent.body !== 'string') throw new Error('Yêu cầu đang lưu không hợp lệ.');
            const body = JSON.parse(intent.body);
            if (typeof body.expectedVersion !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(body.clientRequestId)) throw new Error('Yêu cầu đang lưu không hợp lệ.');
            return intent;
        }
        function write(intent) {
            const raw = JSON.stringify(intent); storage.setItem(key, raw);
            if (storage.getItem(key) !== raw) throw new Error('Không lưu được yêu cầu an toàn. Chưa gửi thao tác.');
            return intent;
        }
        return {
            read,
            begin(deliveryId, operation, body) {
                if (read()) throw new Error('Còn một yêu cầu chưa xác nhận. Cần thử lại yêu cầu cũ trước.');
                if (!operations.has(operation)) throw new Error('Thao tác không hợp lệ.');
                return write({ schema: 1, deliveryId, operation, body: JSON.stringify(body), uncertain: false });
            },
            sending(intent) { const current = read(); if (!current || current.body !== intent.body || current.operation !== intent.operation || current.deliveryId !== intent.deliveryId) throw new Error('Yêu cầu đã thay đổi ở tab khác.'); return write({ ...current, uncertain: true }); },
            acknowledge(ack) {
                const intent = read(); if (!intent) throw new Error('Không còn yêu cầu đang xác nhận.');
                const body = JSON.parse(intent.body);
                if (ack.clientRequestId !== body.clientRequestId || ack.deliveryOrderId !== intent.deliveryId || ack.operation !== intent.operation) throw new Error('Phản hồi không khớp yêu cầu. Giữ yêu cầu cũ để kiểm tra.');
                storage.removeItem(key); if (storage.getItem(key)) throw new Error('Không xóa được yêu cầu đã xác nhận.');
            },
            rejectFirstAttempt(intent) { const current = read(); if (intent.uncertain || !current || current.body !== intent.body) return false; storage.removeItem(key); return storage.getItem(key) === null; }
        };
    }
    function inventoryProjection(detail, storeId) {
        const inventory = detail.inventory;
        if (inventory == null) return null;
        const d = detail.delivery;
        if (!Number.isSafeInteger(storeId) || storeId <= 0 || inventory.storeId !== storeId ||
            inventory.deliveryOrderId !== d.id || inventory.revision !== d.revision || inventory.version !== d.version ||
            inventory.sourceWarehouseId !== d.sourceWarehouseId || inventory.sourceLegalEntityId !== d.sourceLegalEntityId)
            throw new Error('Dữ liệu kho không khớp cửa hàng hoặc bản đơn. Tải bản mới trước khi thao tác.');
        if (!Array.isArray(inventory.lines) || !Array.isArray(inventory.costFragments) || typeof inventory.canViewCosts !== 'boolean' || typeof inventory.canCancelBeforeHandover !== 'boolean')
            throw new Error('Dữ liệu kho chưa đầy đủ.');
        parseUtcDate(inventory.serverTimeUtc);
        if (inventory.custody != null) {
            const c = inventory.custody;
            if (!Number.isSafeInteger(c.tripId) || c.tripId <= 0 || !Number.isSafeInteger(c.custodyEventId) || c.custodyEventId <= 0 ||
                !Number.isSafeInteger(c.courierUserId) || c.courierUserId <= 0 || !Number.isSafeInteger(c.confirmedByUserId) || c.confirmedByUserId <= 0 ||
                !['HandedOver', 'Departed'].includes(c.tripState) || !['Handover', 'Transfer'].includes(c.custodyKind)) throw new Error('Chứng cứ người giữ hàng chưa hợp lệ.');
            parseUtcDate(c.confirmedAtUtc); if (c.departedAtUtc != null) parseUtcDate(c.departedAtUtc);
        }
        const ids = new Set();
        for (const line of inventory.lines) {
            if (!Number.isSafeInteger(line.lineId) || line.lineId <= 0 || ids.has(line.lineId)) throw new Error('Dòng kho không hợp lệ.');
            ids.add(line.lineId);
            const held = scaledQuantity(line.heldBaseQuantityText), dispatched = scaledQuantity(line.dispatchedBaseQuantityText), returned = scaledQuantity(line.acceptedReturnedBaseQuantityText), outside = scaledQuantity(line.stockOutsideBaseQuantityText);
            if (held < 0n || returned > dispatched || outside !== dispatched - returned) throw new Error('Lượng kho chưa khớp chứng từ.');
            if (line.inCustodyBaseQuantityText !== null && (!inventory.custody || scaledQuantity(line.inCustodyBaseQuantityText) !== outside)) throw new Error('Lượng trách nhiệm không khớp chứng cứ bàn giao.');
            if (!inventory.canViewCosts && (line.dispatchedCostText !== null || line.acceptedReturnedCostText !== null)) throw new Error('Dữ liệu giá vốn không đúng quyền xem.');
        }
        if (!inventory.canViewCosts && inventory.costFragments.length) throw new Error('Dữ liệu giá vốn không đúng quyền xem.');
        for (const fragment of inventory.costFragments) costQualityLabel(fragment);
        return inventory;
    }
    function projectionChanged(current, incoming, needsFresh) {
        const facts = inventory => inventory == null ? null : { ...inventory, serverTimeUtc: null };
        return needsFresh || incoming.delivery.version !== current.delivery.version ||
            JSON.stringify(incoming.capabilities) !== JSON.stringify(current.capabilities) ||
            JSON.stringify(facts(incoming.inventory)) !== JSON.stringify(facts(current.inventory));
    }
    function intentPath(intent) {
        if (!Number.isSafeInteger(intent.deliveryId) || intent.deliveryId <= 0) throw new Error('Đơn trong yêu cầu không hợp lệ.');
        if (intent.resource === 'inventory' && intent.operation === 'cancel') return '/admin/api/deliveries/' + intent.deliveryId + '/inventory/cancel';
        if (intent.resource == null && operations.has(intent.operation)) return '/admin/api/deliveries/' + intent.deliveryId + '/picking/' + intent.operation;
        throw new Error('Thao tác đang lưu không hợp lệ.');
    }
    function createInventoryIntentStore(storage, origin, storeId, actorId) {
        if (!origin || !Number.isSafeInteger(storeId) || storeId <= 0 || !actorId) throw new Error('Không xác định được cửa hàng và nhân viên. Hãy đăng nhập lại.');
        const key = 'gao.delivery.inventory.v1:' + JSON.stringify([origin, storeId, actorId]);
        const attempts = new WeakMap();
        function read() {
            const raw = storage.getItem(key); if (!raw) return null;
            let intent, body; try { intent = JSON.parse(raw); body = JSON.parse(intent.body); } catch { throw new Error('Yêu cầu hủy đang lưu không đọc được.'); }
            if (intent.schema !== 1 || intent.resource !== 'inventory' || intent.storeId !== storeId || intent.actorId !== actorId || intent.operation !== 'cancel' || typeof intent.body !== 'string' ||
                typeof body.expectedVersion !== 'string' || !body.expectedVersion || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(body.clientRequestId) ||
                typeof body.reason !== 'string' || validNote(body.reason, 'Lý do hủy') !== body.reason ||
                Object.keys(body).sort().join(',') !== 'clientRequestId,expectedVersion,reason') throw new Error('Yêu cầu hủy đang lưu không hợp lệ.');
            intentPath(intent); return intent;
        }
        function write(intent) { const raw = JSON.stringify(intent); storage.setItem(key, raw); if (storage.getItem(key) !== raw) throw new Error('Không lưu được yêu cầu hủy an toàn. Chưa gửi thao tác.'); return intent; }
        return {
            read,
            begin(deliveryId, operation, body) {
                if (read()) throw new Error('Còn một yêu cầu chưa xác nhận. Cần thử lại yêu cầu cũ trước.');
                const intent = { schema: 1, resource: 'inventory', storeId, actorId, deliveryId, operation, body: JSON.stringify(body), uncertain: false };
                intentPath(intent);
                // Validate the body before writing; a failed validation must not strand an unsendable request.
                if (operation !== 'cancel' || typeof body.expectedVersion !== 'string' || !body.expectedVersion || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(body.clientRequestId) ||
                    typeof body.reason !== 'string' || validNote(body.reason, 'Lý do hủy') !== body.reason || Object.keys(body).sort().join(',') !== 'clientRequestId,expectedVersion,reason') throw new Error('Yêu cầu hủy không hợp lệ.');
                return write(intent);
            },
            sending(intent) {
                const current = read(); if (!current || current.body !== intent.body || current.deliveryId !== intent.deliveryId || current.resource !== intent.resource || current.storeId !== intent.storeId || current.actorId !== intent.actorId || current.operation !== intent.operation) throw new Error('Yêu cầu đã thay đổi ở tab khác.');
                const attempt = write({ ...current, uncertain: true }); attempts.set(attempt, current.uncertain === true); return attempt;
            },
            acknowledge(ack) {
                const intent = read(); if (!intent) throw new Error('Không còn yêu cầu đang xác nhận.'); const body = JSON.parse(intent.body);
                if (ack.clientRequestId !== body.clientRequestId || ack.deliveryOrderId !== intent.deliveryId || ack.operation !== 'cancel' ||
                    !Number.isSafeInteger(ack.appliedRevision) || ack.appliedRevision <= 0 || ack.appliedRevision > 2147483647 ||
                    typeof ack.appliedVersion !== 'string' || !/^[A-Za-z0-9+/]{10}[AEIMQUYcgkosw048]=$/.test(ack.appliedVersion) ||
                    typeof ack.recordedAtUtc !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(ack.recordedAtUtc) ||
                    typeof ack.replayed !== 'boolean') throw new Error('Phản hồi không khớp yêu cầu hủy hoặc thiếu bản xác nhận.');
                parseUtcDate(ack.recordedAtUtc);
                storage.removeItem(key); if (storage.getItem(key)) throw new Error('Không xóa được yêu cầu đã xác nhận.');
            },
            rejectFirstAttempt(attempt) { const current = read(); if (!attempts.has(attempt) || attempts.get(attempt) || !current || current.body !== attempt.body || current.deliveryId !== attempt.deliveryId) return false; storage.removeItem(key); return storage.getItem(key) === null; }
        };
    }
    function cancellationBody(detail, storeId, reason, clientRequestId, dirty, blocked) {
        if (dirty) throw new Error('Có chỉnh sửa chưa lưu. Lưu kết quả soạn hoặc tải lại để bỏ chỉnh sửa trước khi hủy.');
        if (blocked) throw new Error('Xác nhận yêu cầu cũ và tải bản mới trước khi hủy.');
        const inventory = inventoryProjection(detail, storeId);
        if (!inventory?.canCancelBeforeHandover) throw new Error('Đơn hiện tại chưa được phép hủy trước bàn giao.');
        const cancellationReason = reason.trim();
        if (!cancellationReason || cancellationReason.length > 500) throw new Error('Lý do hủy cần có nội dung, tối đa 500 ký tự.');
        return { clientRequestId, expectedVersion: detail.delivery.version, reason: cancellationReason };
    }
    const api = { scaledQuantity, displayExact, money, costQualityLabel, parseUtcDate, canApplyProjection, reportLine, parseLookup, validRevision, createIntentStore, inventoryProjection, projectionChanged, intentPath, createInventoryIntentStore, cancellationBody };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    if (!root.document) return;
    root.DeliveryPickingUi = api;
    const $ = id => document.getElementById(id);
    if (!$('deliverySearch')) return;
    const workspace = document.querySelector('.delivery-workspace');
    const storeId = Number(workspace.dataset.store);
    let intents, inventoryIntents, cancelReason = '', current = null, currentKey = '', printedRevision = null, busy = false, dirty = false, needsFresh = false, lookingUp = false, sequence = 0, readEpoch = 0;
    let pickingFilter = 'all';
    const pickingDrafts = new Map(); let pickingDraftOrderId = null;
    try {
        intents = createIntentStore(localStorage, workspace.dataset.actor ? location.origin + ':' + workspace.dataset.actor : '');
        inventoryIntents = createInventoryIntentStore(localStorage, location.origin, storeId, workspace.dataset.actor);
    } catch (e) { message(e.message, true); }
    function node(tag, text, cls) { const element = document.createElement(tag); if (text != null) element.textContent = text; if (cls) element.className = cls; return element; }
    function button(text, action, cls = 'btn btn-outline-primary') { const element = node('button', text, cls); element.type = 'button'; element.addEventListener('click', action); return element; }
    function message(text, error = false) { const element = $('deliveryPageMessage'); element.textContent = text; element.className = 'delivery-page-message' + (error ? ' is-error' : ''); }
    function time(value) { return value ? parseUtcDate(value).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour12: false }) : 'Chưa ghi nhận'; }
    function note(text, cls = 'delivery-muted') { return node('p', text, cls); }
    function inputField(label, value, id, numeric = false) {
        const wrapper = node('label', null, 'delivery-input-label'); const title = node('span', label);
        const input = node('input'); input.type = 'text'; input.id = id; input.className = 'form-control'; input.value = value ?? '';
        input.maxLength = numeric ? 19 : 1000; if (numeric) { input.inputMode = 'decimal'; input.placeholder = 'Chưa ghi nhận'; }
        input.addEventListener('input', () => { dirty = true; updatePickingDraft(); }); wrapper.append(title, input); return { wrapper, input };
    }
    async function get(path) {
        const response = await fetch('/admin/api/deliveries' + path, { cache: 'no-store', credentials: 'same-origin' });
        const data = await response.json(); if (!response.ok) throw new Error(data.message || 'Không có quyền xem hoặc không tìm thấy đơn.'); return data;
    }
    function pendingRequests() { try { return [intents?.read(), inventoryIntents?.read()].filter(Boolean); } catch (e) { message(e.message, true); return [{ broken: true }]; } }
    function pending() { return pendingRequests()[0] ?? null; }
    function pendingPanel(panel) {
        for (const intent of pendingRequests()) {
            const box = node('section', null, 'delivery-pending'); box.append(node('strong', intent.resource === 'inventory' ? 'Có yêu cầu hủy chưa xác nhận' : 'Có thao tác chưa xác nhận'), note('Giữ nguyên mã yêu cầu và nội dung. Thử lại để xác nhận kết quả đã lưu, sau đó tải bản mới nhất.'));
            if (!intent.broken) box.append(button('Thử lại yêu cầu cũ', () => send(intent), 'btn btn-warning'), note('Đơn #' + intent.deliveryId + ' · ' + intent.operation));
            panel.append(box);
        }
    }
    async function send(intent) {
        const registry = intent.resource === 'inventory' ? inventoryIntents : intents;
        if (busy || lookingUp || !registry) return; readEpoch++; busy = true; render(); message('Đang xác nhận thao tác…');
        let acknowledged = false;
        try {
            const attempt = registry.sending(intent);
            const token = document.querySelector('meta[name="request-verification-token"]')?.content || document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!token) throw new Error('Thiếu mã bảo vệ phiên. Hãy tải lại trang và thử lại yêu cầu cũ.');
            const response = await fetch(intentPath(intent), {
                method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', RequestVerificationToken: token }, body: intent.body
            });
            let data; try { data = await response.json(); } catch { throw new Error('Chưa xác nhận được kết quả. Thử lại yêu cầu cũ.'); }
            if (!response.ok) {
                // A first received rejection has no preceding uncertain attempt. Retries retain identity after permission or state changes.
                if ([400, 403, 404, 409, 422].includes(response.status)) registry.rejectFirstAttempt(intent.resource === 'inventory' ? attempt : intent);
                if (intent.resource === 'inventory') needsFresh = true;
                throw new Error(data.message || 'Yêu cầu bị từ chối.');
            }
            registry.acknowledge(data);
            acknowledged = true; needsFresh = true; dirty = false;
            if (intent.resource === 'inventory') cancelReason = '';
            const fresh = current?.delivery.id === intent.deliveryId ? await loadCurrent(intent.deliveryId, true) : await lookup(currentKey, true, intent.deliveryId);
            if (!fresh) throw new Error('Chưa tải được bản hiện hành.');
            message(data.replayed ? 'Đã xác nhận yêu cầu cũ. Đang hiển thị bản mới nhất.' : 'Đã lưu thao tác. Đang hiển thị bản mới nhất.');
            await recent();
        } catch (e) { message(acknowledged && needsFresh ? 'Đã xác nhận thao tác nhưng chưa tải được bản mới. Tải bản mới trước khi thao tác tiếp.' : (pending() ? 'Chưa xác nhận. ' : '') + e.message, true); }
        finally { busy = false; render(); }
    }
    async function command(operation, fields = {}) {
        if (!current || busy || lookingUp || !intents) return;
        if (needsFresh) { message('Tải bản mới trước khi thao tác tiếp.', true); return; }
        if (pending()) { message('Còn một yêu cầu chưa xác nhận. Cần thử lại yêu cầu cũ trước.', true); return; }
        if (operation === 'submit' && dirty) { message('Có chỉnh sửa chưa lưu. Lưu kết quả soạn trước khi gửi chốt.', true); return; }
        try {
            const body = { clientRequestId: crypto.randomUUID(), expectedVersion: current.delivery.version, ...fields };
            const intent = intents.begin(current.delivery.id, operation, body); await send(intent);
        } catch (e) { message(e.message, true); }
    }
    function printedKey(id) { return 'gao.delivery.printed.v1:' + workspace.dataset.actor + ':' + id; }
    async function lookup(raw, force = true, knownId = null) {
        if (busy && !knownId) return false;
        const ticket = ++sequence; readEpoch++; lookingUp = true; render(); message('Đang tra đơn…');
        try {
            const parsed = parseLookup(raw, location.origin);
            const detail = knownId ? { id: knownId, code: currentKey } : await get('/lookup?key=' + encodeURIComponent(parsed.key));
            const data = await get('/' + detail.id + '/picking'); if (ticket !== sequence) return false;
            inventoryProjection(data, storeId);
            current = data; currentKey = knownId ? data.delivery.lookupToken : parsed.key || detail.code; cancelReason = '';
            printedRevision = parsed.revision;
            if (!printedRevision) try { printedRevision = validRevision(sessionStorage.getItem(printedKey(detail.id))); } catch { /* URL remains authoritative. */ }
            if (printedRevision) try { sessionStorage.setItem(printedKey(detail.id), printedRevision); } catch { /* Also retained in URL. */ }
            dirty = false; needsFresh = false; lookingUp = false; render(); $('deliveryKey').value = data.delivery.code;
            const params = new URLSearchParams({ key: currentKey }); if (printedRevision) params.set('revision', printedRevision);
            history.replaceState(null, '', '/admin/deliveries?' + params.toString()); message(''); return true;
        } catch (e) { if (ticket === sequence) message(e.message, true); return false; }
        finally { if (ticket === sequence && lookingUp) { lookingUp = false; render(); } }
    }
    async function loadCurrent(id, force = false) {
        if (lookingUp || current?.delivery.id !== id) return false;
        const ticket = sequence, epoch = ++readEpoch; let data;
        try { data = await get('/' + id + '/picking'); }
        catch (e) { if (ticket !== sequence || epoch !== readEpoch || current?.delivery.id !== id) return false; throw e; }
        if (ticket !== sequence || !canApplyProjection(current, data, epoch, readEpoch)) return false;
        inventoryProjection(data, storeId);
        if (dirty && !force) {
            if (data.delivery.version !== current.delivery.version) message('Đơn đã có thay đổi. Tải bản mới để tiếp tục; nội dung đang nhập vẫn được giữ.'); return false;
        }
        const changed = projectionChanged(current, data, needsFresh);
        current = data; needsFresh = false; if (force) dirty = false; if (changed || force) render(); return true;
    }
    function updatePickingDraft() {
        if (!current) return;
        let edited = 0, shortageCount = 0;
        for (const line of current.lines.filter(item => item.isActive)) {
            const quantity = $('deliveryQty-' + line.lineId), reason = $('deliveryReason-' + line.lineId);
            const card = $('deliveryLine-' + line.lineId), reasonBox = $('deliveryReasonBox-' + line.lineId), draftLabel = $('deliveryDraftLine-' + line.lineId);
            let short = false, localState = '';
            const value = quantity ? quantity.value : line.reportedQuantityText;
            try {
                if (value != null && value !== '') {
                    const actual = scaledQuantity(value), needed = scaledQuantity(line.plannedQuantityText);
                    short = actual < needed;
                    localState = actual === 0n && needed > 0n ? 'Không có' : short ? 'Thiếu' : actual > needed ? 'Vượt lượng cần soạn' : 'Đủ';
                }
            } catch { localState = 'Kiểm tra số lượng'; }
            if (short) shortageCount++;
            const changed = quantity && (quantity.value !== (line.reportedQuantityText ?? '') || reason.value !== (line.shortageReason ?? ''));
            if (quantity && reason && dirty) pickingDrafts.set(line.lineId, { quantity: quantity.value, reason: reason.value, reasonOpen: reasonBox?.dataset.open === 'true' });
            if (changed) edited++;
            if (card) { card.classList.toggle('has-shortage', short); card.classList.toggle('has-unsaved', !!changed); card.hidden = pickingFilter === 'short' && !short; }
            if (draftLabel) { draftLabel.textContent = changed ? 'Chưa lưu · ' + (localState || 'Chưa nhập lượng') : ''; draftLabel.hidden = !changed; }
            if (reasonBox) reasonBox.hidden = !short && !reason.value.trim() && reasonBox.dataset.open !== 'true';
            for (const [kind, label] of [['Full', 'Đủ'], ['Short', 'Thiếu'], ['Missing', 'Không có']]) {
                const control = $('delivery' + kind + '-' + line.lineId); if (control) control.setAttribute('aria-pressed', String(localState === label));
            }
        }
        for (const line of current.lines.filter(item => !item.isActive)) { const card = $('deliveryLine-' + line.lineId); if (card) card.hidden = pickingFilter === 'short'; }
        const emptyShortages = $('deliveryShortagesEmpty'); if (emptyShortages) emptyShortages.hidden = pickingFilter !== 'short' || shortageCount > 0;
        const progress = $('deliveryDraftProgress'); if (progress) { progress.textContent = dirty ? (edited ? edited + ' dòng đang chỉnh · chưa lưu' : 'Có chỉnh sửa chưa lưu') : 'Không có chỉnh sửa chưa lưu'; progress.classList.toggle('is-dirty', dirty); }
        const submit = $('deliverySubmitPicking'); if (submit) submit.disabled = submit.dataset.blocked === 'true' || dirty || current.lines.some(line => line.isActive && line.reportedQuantityText === null);
        for (const [id, mode] of [['deliveryShowAll', 'all'], ['deliveryShowShortages', 'short']]) { const control = $(id); if (control) control.setAttribute('aria-pressed', String(pickingFilter === mode)); }
    }
    function foldSection(panel, label, cls = 'delivery-picking-secondary') { const section = node('details', null, cls); section.append(node('summary', label)); panel.append(section); return section; }
    function render() {
        $('deliveryKey').disabled = busy || lookingUp; $('deliverySearch').querySelector('button').disabled = busy || lookingUp;
        const panel = $('deliveryDetail'); if (!current) { panel.hidden = true; return; }
        const sameDraftOrder = pickingDraftOrderId === current.delivery.id;
        if (!sameDraftOrder) pickingFilter = 'all';
        if (!dirty || !sameDraftOrder) pickingDrafts.clear();
        pickingDraftOrderId = current.delivery.id;
        if (dirty && sameDraftOrder) for (const line of current.lines) { const quantity = $('deliveryQty-' + line.lineId), reason = $('deliveryReason-' + line.lineId); if (quantity && reason) pickingDrafts.set(line.lineId, { quantity: quantity.value, reason: reason.value, reasonOpen: $('deliveryReasonBox-' + line.lineId)?.dataset.open === 'true' }); }
        panel.replaceChildren(); panel.hidden = false;
        const d = current.delivery, capabilities = current.capabilities, blocked = busy || lookingUp || needsFresh || !!pending() || !intents || !inventoryIntents;
        panel.dataset.revision = String(d.revision); panel.dataset.version = d.version; panel.dataset.state = d.state;
        panel.dataset.needsFresh = String(needsFresh);
        if (needsFresh) panel.append(note('Cần tải bản mới trước khi thao tác. Nội dung dưới đây là bản đã xem trước đó; kiểm tra kết quả hiện hành trên máy chủ.', 'delivery-revision-warning'));
        const header = node('div', null, 'delivery-detail-heading'); const identity = node('div'); identity.append(node('h3', d.recipientName || 'Khách nhận hàng'), note(d.code + ' · ' + (current.pickerName || 'Chưa nhận soạn'))); header.append(identity, node('span', states[d.state] || d.state, 'delivery-badge state-' + d.state)); panel.append(header);
        if (printedRevision && BigInt(printedRevision) < BigInt(d.revision)) panel.append(note('Phiếu đang quét là bản ' + printedRevision + '. Đơn hiện tại là bản ' + d.revision + ' — kiểm tra lượng mới trước khi soạn hoặc giao.', 'delivery-revision-warning'));
        const recipient = node('section', null, 'delivery-recipient'); recipient.append(node('span', d.recipientPhone), node('span', d.recipientAddress)); if (d.note) recipient.append(node('span', d.note)); panel.append(recipient);
        const effective = current.lines.filter(line => line.isActive);
        const reported = effective.filter(line => line.reportedQuantityText !== null).length;
        const progress = node('div', null, 'delivery-picking-progress'); progress.setAttribute('role', 'progressbar'); progress.setAttribute('aria-valuemin', '0'); progress.setAttribute('aria-valuemax', String(effective.length)); progress.setAttribute('aria-valuenow', String(reported));
        const bar = node('span'); bar.style.width = effective.length ? (reported * 100 / effective.length) + '%' : '0%'; progress.append(bar); const savedProgress = note(reported + '/' + effective.length + ' dòng đã lưu trên máy chủ', 'delivery-saved-progress'); savedProgress.id = 'deliverySavedProgress'; panel.append(savedProgress, progress);
        pendingPanel(panel);
        const actions = node('div', null, 'delivery-actions');
        const refresh = button('Tải bản mới', () => loadCurrent(d.id, true).catch(e => message(e.message, true))); refresh.disabled = busy || lookingUp; actions.append(refresh);
        if (capabilities.canClaim) { const claim = button('Nhận soạn đơn này', () => command('claim'), 'btn btn-primary'); claim.id = 'deliveryClaim'; claim.disabled = blocked; actions.append(claim); }
        const bill = node('a', 'Mở / in phiếu A5', 'btn btn-outline-primary'); bill.href = '/admin/deliveries/' + d.id + '/bill'; bill.target = '_blank'; bill.rel = 'noopener'; actions.append(bill); panel.append(actions);
        const lines = node('section', null, 'delivery-picking-lines'); const lineHeading = node('div', null, 'delivery-line-heading'); lineHeading.append(node('h4', 'Hàng cần soạn'));
        const filters = node('div', null, 'delivery-line-filters'); const showAll = button('Tất cả', () => { pickingFilter = 'all'; updatePickingDraft(); }); showAll.id = 'deliveryShowAll'; const showShort = button('Thiếu hàng', () => { pickingFilter = 'short'; updatePickingDraft(); }); showShort.id = 'deliveryShowShortages'; filters.append(showAll, showShort); lineHeading.append(filters); lines.append(lineHeading);
        for (const line of current.lines) {
            if (!line.isActive && line.isReplacement) continue;
            const card = node('article', null, 'delivery-picking-line' + (!line.isActive ? ' is-removed' : line.reportedQuantityText === null ? '' : scaledQuantity(line.reportedQuantityText) < scaledQuantity(line.plannedQuantityText) ? ' has-shortage' : ' is-reported'));
            card.dataset.lineId = String(line.lineId);
            card.id = 'deliveryLine-' + line.lineId;
            const original = current.lines.find(item => item.lineId === line.originalRootLineId);
            const title = node('div', null, 'delivery-line-title'); title.append(node('strong', line.itemName), node('span', line.isReplacement ? 'Thay cho ' + (original?.itemName || 'hàng gốc') : 'Hàng gốc', 'delivery-small-badge')); card.append(title);
            const needed = node('div', null, 'delivery-needed'); needed.append(node('span', 'Cần soạn'), node('strong', displayExact(line.plannedQuantityText) + ' ' + line.unitName));
            const row = node('div', null, 'delivery-quantity-row'); row.append(needed);
            const status = !line.isActive ? 'Đã bỏ khỏi bản soạn' : line.reportedQuantityText === null ? 'Chưa lưu kết quả' : 'Đã lưu: ' + displayExact(line.reportedQuantityText) + ' ' + line.unitName;
            const savedState = node('div', status, 'delivery-line-status'); const draftLabel = node('span', '', 'delivery-line-draft'); draftLabel.id = 'deliveryDraftLine-' + line.lineId; draftLabel.hidden = true;
            if (capabilities.canReport && line.isActive) {
                const draft = dirty ? pickingDrafts.get(line.lineId) : null;
                const quantity = inputField('Thực có (' + line.unitName + ')', draft ? draft.quantity : line.reportedQuantityText, 'deliveryQty-' + line.lineId, true);
                const reason = inputField('Lý do thiếu / không có hàng', draft ? draft.reason : line.shortageReason, 'deliveryReason-' + line.lineId);
                reason.wrapper.id = 'deliveryReasonBox-' + line.lineId; reason.wrapper.classList.add('delivery-shortage-input'); reason.wrapper.dataset.open = String(draft?.reasonOpen === true); reason.wrapper.hidden = true;
                quantity.input.disabled = reason.input.disabled = blocked;
                const choices = node('div', null, 'delivery-quantity-choices');
                const full = button('Đủ', () => { quantity.input.value = line.plannedQuantityText; reason.wrapper.dataset.open = 'false'; dirty = true; updatePickingDraft(); }); full.id = 'deliveryFull-' + line.lineId; full.disabled = blocked;
                const short = button('Thiếu', () => { reason.wrapper.dataset.open = 'true'; reason.wrapper.hidden = false; quantity.input.focus(); }); short.id = 'deliveryShort-' + line.lineId; short.disabled = blocked;
                const missing = button('Không có', () => { quantity.input.value = '0'; dirty = true; updatePickingDraft(); reason.input.focus(); }); missing.id = 'deliveryMissing-' + line.lineId; missing.disabled = blocked;
                choices.append(full, short, missing); row.append(quantity.wrapper, choices); card.append(row, reason.wrapper);
            }
            else { const actual = node('div', null, 'delivery-actual-readonly'); actual.append(node('span', 'Thực có'), node('strong', line.reportedQuantityText === null ? 'Chưa ghi nhận' : displayExact(line.reportedQuantityText) + ' ' + line.unitName)); row.append(actual); card.append(row); if (line.shortageReason) card.append(note('Lý do: ' + line.shortageReason, 'delivery-shortage-reason')); }
            card.append(savedState, draftLabel);
            if (line.approvedQuantityText !== null) card.append(note('Được giao: ' + displayExact(line.approvedQuantityText) + ' ' + line.unitName, 'delivery-approved-fact'));
            const lineDetails = foldSection(card, 'Thông tin dòng hàng', 'delivery-line-details'); lineDetails.append(note('Đặt gốc: ' + displayExact(line.orderedQuantityText) + ' ' + line.unitName + ' · Giá gốc: ' + displayExact(line.unitPriceText, 2) + ' đ'), note(line.reportedAtUtc ? (line.reporterName || 'Nhân viên #' + line.reporterUserId) + ' · ' + time(line.reportedAtUtc) : 'Chưa có người ghi nhận'));
            lines.append(card);
        }
        const emptyShortages = note('Chưa có mặt hàng thiếu trong kết quả đang xem.', 'delivery-muted'); emptyShortages.id = 'deliveryShortagesEmpty'; emptyShortages.hidden = true; lines.append(emptyShortages);
        panel.append(lines);
        const primaryActions = node('div', null, 'delivery-picking-primary'); const draftProgress = note('', 'delivery-draft-progress'); draftProgress.id = 'deliveryDraftProgress'; primaryActions.append(draftProgress); const primaryButtons = node('div', null, 'delivery-primary-buttons'); primaryActions.append(primaryButtons);
        if (capabilities.canReport) {
            const save = button('Lưu kết quả soạn', () => {
                try {
                    const reports = effective.filter(line => $('deliveryQty-' + line.lineId).value !== '').map(line => reportLine(line, $('deliveryQty-' + line.lineId).value, $('deliveryReason-' + line.lineId).value));
                    if (!reports.length) throw new Error('Nhập ít nhất một kết quả soạn.'); command('report', { lines: reports });
                } catch (e) { message(e.message, true); }
            }, 'btn btn-primary'); save.id = 'deliverySaveReport'; save.disabled = blocked; primaryButtons.append(save);
        }
        if (capabilities.canSubmit) { const submit = button('Gửi chốt soạn', () => command('submit'), 'btn btn-success'); submit.id = 'deliverySubmitPicking'; submit.dataset.blocked = String(blocked); submit.disabled = blocked || dirty || effective.some(line => line.reportedQuantityText === null); primaryButtons.append(submit); }
        if (capabilities.canReport || capabilities.canSubmit) { primaryActions.append(note(effective.some(line => line.reportedQuantityText === null) ? 'Lưu đủ kết quả các mặt hàng trước khi gửi chốt. Dòng không có hàng cần ghi 0 và lý do.' : 'Lưu kết quả rồi gửi chốt. Dòng không có hàng vẫn cần ghi 0 và lý do.')); panel.append(primaryActions); }
        const orderInfo = foldSection(panel, 'Thông tin đơn và giá trị'); orderInfo.append(note(d.code + ' · Bản ' + d.revision + ' · Tạo ' + time(d.createdAtUtc) + ' · Quầy #' + d.createdTerminalId + ' · Giỏ #' + d.sourceCartId), note('Người phụ trách: ' + (current.pickerName || 'Chưa nhận soạn') + ' · Nhận lúc: ' + time(current.assignedAtUtc) + ' · Gửi chốt: ' + time(current.submittedAtUtc)));
        const totals = node('div', null, 'delivery-picking-totals'); totals.append(note('Giá trị gốc: ' + money(current.quotedTotalText)), note('Bản soạn dự kiến: ' + money(current.draftTotalText)));
        if (current.approvedTotalText !== null) totals.append(node('strong', 'Lượng được duyệt: ' + money(current.approvedTotalText))); else totals.append(note('Chưa có bản duyệt hiện hành.')); orderInfo.append(totals);
        const inventoryDetails = foldSection(panel, 'Tồn kho và hủy đơn'); renderInventory(inventoryDetails, blocked);
        const coverage = node('details', null, 'delivery-coverage'); coverage.append(node('summary', 'Lượng còn thiếu theo yêu cầu gốc'));
        for (const rootLine of current.rootCoverage) { const original = current.lines.find(line => line.lineId === rootLine.originalRootLineId); coverage.append(note((original?.itemName || 'Dòng #' + rootLine.originalRootLineId) + ': thiếu dự kiến ' + displayExact(rootLine.draftMissingQuantityText) + ' ' + (original?.unitName || '') + (rootLine.approvedMissingQuantityText !== null ? ' · thiếu sau duyệt ' + displayExact(rootLine.approvedMissingQuantityText) : ''))); } panel.append(coverage);
        if (capabilities.canPlan) renderPlan(panel, effective, blocked);
        if (capabilities.canApprove) renderApproval(panel, effective, blocked);
        if (capabilities.canReopen || capabilities.canReassign) renderAdministration(panel, blocked);
        const historySection = node('details', null, 'delivery-history'); historySection.append(node('summary', 'Lịch sử xử lý · thời gian máy chủ'));
        const historyBody = node('div'); historySection.append(historyBody); let loaded = false;
        historySection.addEventListener('toggle', async () => {
            if (!historySection.open || loaded) return;
            try { const historyRows = await get('/' + d.id + '/history'); loaded = true; for (const row of historyRows) historyBody.append(note('Bản ' + row.revision + ' · ' + row.action + ' · Nhân viên #' + row.actorUserId + ' · ' + time(row.recordedAtUtc))); }
            catch (e) { historyBody.textContent = e.message; }
        }); panel.append(historySection);
        panel.append(note('Cập nhật máy chủ: ' + time(current.serverTimeUtc) + '. Kiểm tra lại bản hiện hành trước khi thao tác.', 'delivery-server-time'));
        updatePickingDraft();
    }
    function renderInventory(panel, blocked) {
        const box = node('section', null, 'delivery-inventory'); box.id = 'deliveryInventory'; box.append(node('h4', 'Hàng tại kho và ngoài kho'));
        const inventory = inventoryProjection(current, storeId);
        if (!inventory) { box.append(note('Chưa có dữ liệu chứng từ kho của đơn.')); panel.append(box); return; }
        box.dataset.version = inventory.version; box.dataset.canCancel = String(inventory.canCancelBeforeHandover);
        box.append(note('Kho nguồn: ' + inventory.sourceWarehouseName + ' · Chủ hàng: ' + inventory.sourceLegalEntityName, 'delivery-inventory-origin'));
        for (const line of inventory.lines) {
            const row = node('article', null, 'delivery-inventory-line'); row.dataset.lineId = String(line.lineId); row.append(node('strong', line.itemName));
            const facts = node('dl', null, 'delivery-inventory-quantities');
            for (const [key, label] of [['heldBaseQuantityText', 'Đang giữ cho đơn'], ['dispatchedBaseQuantityText', 'Đã xuất khỏi kho'], ['acceptedReturnedBaseQuantityText', 'Đã nhận trả vào kho'], ['stockOutsideBaseQuantityText', 'Còn ngoài kho nguồn']]) {
                const value = node('dd', displayExact(line[key]) + ' ' + line.baseUnitName); value.dataset.quantity = key; facts.append(node('dt', label), value);
            }
            row.append(facts, note(line.inCustodyBaseQuantityText === null ? 'Trách nhiệm giao: chưa có xác nhận.' : 'Lượng đã xác nhận trách nhiệm giao: ' + displayExact(line.inCustodyBaseQuantityText) + ' ' + line.baseUnitName, 'delivery-inventory-custody')); box.append(row);
            if (inventory.canViewCosts) for (const fragment of inventory.costFragments.filter(x => x.lineId === line.lineId)) {
                const cost = node('div', null, 'delivery-inventory-cost'); cost.dataset.costQuality = fragment.costQuality;
                cost.append(note(costQualityLabel(fragment)), note('Chứng từ xuất ' + displayExact(fragment.baseQuantityText) + ' ' + line.baseUnitName + ' · Giá vốn ghi nhận ban đầu: ' + money(fragment.costAmountText)));
                if (fragment.costQuality !== 'Final') cost.append(note('Số ghi nhận ban đầu được giữ để đối chiếu; giá vốn thực tế dựa trên chứng cứ bù sau đó.'));
                row.append(cost);
            }
        }
        if (inventory.custody) {
            const c = inventory.custody, context = node('div', null, 'delivery-inventory-custody'); context.id = 'deliveryCustodyContext';
            context.append(note('Người giữ hàng: ' + c.courierName + ' · ' + c.tripCode), note('Nhân viên xác nhận: ' + c.confirmedByName + ' · ' + time(c.confirmedAtUtc)));
            if (c.departedAtUtc) context.append(note('Xuất phát: ' + time(c.departedAtUtc) + ' · Nhân viên: ' + c.departedByName));
            const link = node('a', 'Mở chuyến giao'); link.href = '/admin/deliveries/trips?trip=' + c.tripId; context.append(link); box.append(context);
        }
        box.append(note('Lượng ngoài kho dựa trên chứng từ xuất và nhận trả. Người giữ hàng chỉ hiện khi có xác nhận bàn giao; kết quả giao sẽ được cập nhật ở bước sau.'), note('Chứng từ kho cập nhật: ' + time(inventory.serverTimeUtc), 'delivery-server-time'));
        if (inventory.canCancelBeforeHandover) {
            const cancellation = node('details', null, 'delivery-cancel-section'); cancellation.append(node('summary', 'Hủy đơn trước bàn giao'));
            cancellation.append(note('Hủy đơn sẽ giải phóng lượng đang giữ. Hàng đã xuất kho cần quy trình nhận trả.'));
            const label = node('label', null, 'delivery-input-label'); label.append(node('span', 'Lý do hủy')); const input = node('input'); input.id = 'deliveryCancelReason'; input.className = 'form-control'; input.type = 'text'; input.maxLength = 500; input.value = cancelReason; input.disabled = blocked;
            input.addEventListener('input', () => { cancelReason = input.value; }); label.append(input); cancellation.append(label);
            const cancel = button('Xác nhận hủy đơn', async () => {
                try { const body = cancellationBody(current, storeId, cancelReason, crypto.randomUUID(), dirty, busy || lookingUp || needsFresh || !!pending() || !inventoryIntents); await send(inventoryIntents.begin(current.delivery.id, 'cancel', body)); }
                catch (e) { message(e.message, true); }
            }, 'btn btn-outline-danger'); cancel.id = 'deliveryCancel'; cancel.disabled = blocked; cancellation.append(cancel); box.append(cancellation);
        }
        panel.append(box);
    }
    function managerSection(panel, title, id) { const details = node('details', null, 'delivery-manager-section'); details.id = id; details.append(node('summary', title)); const body = node('div', null, 'delivery-manager-body'); details.append(body); panel.append(details); return body; }
    function managerNotes(body, prefix, blocked) {
        const reason = inputField('Lý do xử lý', '', prefix + 'Reason'); const confirmation = inputField('Nội dung đã thống nhất với khách', '', prefix + 'Confirmation'); reason.input.disabled = confirmation.input.disabled = blocked; body.append(reason.wrapper, confirmation.wrapper); return { reason, confirmation };
    }
    function renderPlan(panel, effective, blocked) {
        const body = managerSection(panel, 'Quản lý: giảm, bỏ hoặc thay hàng', 'deliveryPlanSection');
        body.append(note('Giá gốc được giữ. Hàng thay thế được báo giá lại trên máy chủ khi lưu, rồi người soạn phải ghi nhận lượng thực có.'));
        const fields = new Map();
        for (const line of effective) {
            const row = node('div', null, 'delivery-plan-row'); row.append(node('strong', line.itemName + ' · ' + line.unitName));
            const quantity = inputField('Lượng giữ trong kế hoạch', line.plannedQuantityText, 'deliveryPlanQty-' + line.lineId, true);
            const reason = inputField('Lý do bỏ / giảm', line.shortageReason || '', 'deliveryPlanReason-' + line.lineId);
            const coverage = line.isReplacement ? inputField('Bù bao nhiêu lượng hàng gốc', line.draftOriginalCoverageText, 'deliveryPlanCoverage-' + line.lineId, true) : null;
            quantity.input.disabled = reason.input.disabled = blocked; if (coverage) coverage.input.disabled = blocked;
            row.append(quantity.wrapper, reason.wrapper); if (coverage) row.append(coverage.wrapper);
            const remove = button('Bỏ dòng này', () => { quantity.input.value = '0'; if (coverage) coverage.input.value = '0'; dirty = true; reason.input.focus(); }); remove.disabled = blocked; row.append(remove); body.append(row); fields.set(line.lineId, { quantity, reason, coverage });
        }
        const search = inputField('Tìm sản phẩm thay thế', '', 'deliveryReplacementSearch'); search.input.maxLength = 200; search.input.disabled = blocked;
        const options = node('select'); options.id = 'deliveryReplacementOptions'; options.className = 'form-select'; options.setAttribute('aria-label', 'Sản phẩm và đơn vị thay thế'); options.disabled = blocked;
        const rootSelect = node('select'); rootSelect.id = 'deliveryReplacementRoot'; rootSelect.className = 'form-select'; rootSelect.setAttribute('aria-label', 'Dòng hàng gốc cần thay'); rootSelect.disabled = blocked;
        for (const line of current.lines.filter(line => !line.isReplacement)) { const option = node('option', line.itemName + ' · ' + line.unitName); option.value = String(line.lineId); rootSelect.append(option); }
        const replacementQty = inputField('Lượng sản phẩm thay thế', '', 'deliveryReplacementQty', true), replacementCoverage = inputField('Lượng được bù của hàng gốc', '', 'deliveryReplacementCoverage', true);
        replacementQty.input.disabled = replacementCoverage.input.disabled = blocked;
        let replacementOptions = []; const proposals = []; const proposalsBody = node('div', null, 'delivery-replacement-proposals');
        const find = button('Tìm hàng / báo giá', async () => {
            try { replacementOptions = await get('/' + current.delivery.id + '/picking/replacement-options?query=' + encodeURIComponent(search.input.value) + '&take=25'); options.replaceChildren();
                for (const [index, item] of replacementOptions.entries()) { const option = node('option', item.itemName + ' · ' + item.unitName + ' · ' + money(item.unitPriceText) + ' · ' + item.priceTier); option.value = String(index); options.append(option); }
                if (!replacementOptions.length) message('Không tìm thấy sản phẩm được phép thay.');
            } catch (e) { message(e.message, true); }
        }); find.id = 'deliveryReplacementFind'; find.disabled = blocked;
        const add = button('Thêm vào đề xuất thay hàng', () => {
            try {
                const option = replacementOptions[options.selectedIndex]; if (!option) throw new Error('Tìm và chọn sản phẩm trước.');
                if (scaledQuantity(replacementQty.input.value) === 0n) throw new Error('Lượng thay thế phải lớn hơn 0.'); scaledQuantity(replacementCoverage.input.value);
                const proposal = { originalRootLineId: Number(rootSelect.value), variantId: option.variantId, productUnitConversionId: option.productUnitConversionId, plannedQuantityText: replacementQty.input.value, originalCoverageText: replacementCoverage.input.value };
                proposals.push(proposal); const row = node('div', null, 'delivery-proposal'); row.append(note(option.itemName + ': ' + displayExact(proposal.plannedQuantityText) + ' ' + option.unitName + ' · bù ' + displayExact(proposal.originalCoverageText) + ' lượng gốc'));
                row.append(button('Bỏ đề xuất', () => { proposals.splice(proposals.indexOf(proposal), 1); row.remove(); dirty = true; })); proposalsBody.append(row); dirty = true;
            } catch (e) { message(e.message, true); }
        }); add.id = 'deliveryReplacementAdd'; add.disabled = blocked;
        body.append(search.wrapper, find, options, rootSelect, replacementQty.wrapper, replacementCoverage.wrapper, add, proposalsBody);
        const notes = managerNotes(body, 'deliveryPlan', blocked);
        const save = button('Lưu phương án · yêu cầu soạn lại', () => {
            try {
                const lines = effective.map(line => { const f = fields.get(line.lineId); const q = scaledQuantity(f.quantity.input.value); if (q > scaledQuantity(line.orderedQuantityText)) throw new Error(line.itemName + ': vượt lượng chụp.'); if (f.coverage) scaledQuantity(f.coverage.input.value); const reason = f.reason.input.value.trim(); if (q === 0n && !reason) throw new Error(line.itemName + ': cần lý do bỏ hàng.'); return { lineId: line.lineId, plannedQuantityText: f.quantity.input.value, originalCoverageText: f.coverage ? f.coverage.input.value : null, reason: reason || null }; });
                command('plan', { lines, newReplacements: proposals, reason: validNote(notes.reason.input.value, 'Lý do'), customerConfirmationNote: validNote(notes.confirmation.input.value, 'Xác nhận với khách') });
            } catch (e) { message(e.message, true); }
        }, 'btn btn-primary'); save.id = 'deliveryPlanSave'; save.disabled = blocked; body.append(save);
    }
    function renderApproval(panel, effective, blocked) {
        const body = managerSection(panel, 'Quản lý: duyệt lượng đã soạn', 'deliveryApprovalSection'); body.append(note('Chỉ duyệt lượng nhân viên đã ghi nhận. Hàng thay thế cần xác nhận riêng lượng bù cho yêu cầu gốc.'));
        const fields = new Map();
        for (const line of effective) { const quantity = inputField(line.itemName + ' · lượng được giao (' + line.unitName + ')', line.reportedQuantityText, 'deliveryAllow-' + line.lineId, true); quantity.input.disabled = blocked; body.append(quantity.wrapper);
            const coverage = line.isReplacement ? inputField('Lượng bù hàng gốc được duyệt', line.draftOriginalCoverageText, 'deliveryAllowCoverage-' + line.lineId, true) : null; if (coverage) { coverage.input.disabled = blocked; body.append(coverage.wrapper); } fields.set(line.lineId, { quantity, coverage }); }
        const notes = managerNotes(body, 'deliveryApprove', blocked);
        const approve = button('Duyệt và chốt soạn', () => {
            try { const lines = effective.map(line => { const f = fields.get(line.lineId); const allowed = scaledQuantity(f.quantity.input.value); if (line.reportedQuantityText === null || allowed > scaledQuantity(line.reportedQuantityText)) throw new Error(line.itemName + ': lượng duyệt vượt lượng đã soạn.'); if (f.coverage) scaledQuantity(f.coverage.input.value); return { lineId: line.lineId, allowedQuantityText: f.quantity.input.value, confirmedOriginalCoverageText: f.coverage ? f.coverage.input.value : null }; });
                command('approve', { lines, reason: validNote(notes.reason.input.value, 'Lý do'), customerConfirmationNote: validNote(notes.confirmation.input.value, 'Xác nhận với khách') });
            } catch (e) { message(e.message, true); }
        }, 'btn btn-success'); approve.id = 'deliveryApprove'; approve.disabled = blocked; body.append(approve);
    }
    function renderAdministration(panel, blocked) {
        const body = managerSection(panel, 'Quản lý: mở soạn lại / đổi người phụ trách', 'deliveryAdministration');
        const reason = inputField('Lý do', '', 'deliveryAdminReason'); reason.input.disabled = blocked; body.append(reason.wrapper);
        if (current.capabilities.canReopen) { const reopen = button('Mở soạn lại', () => { try { command('reopen', { reason: validNote(reason.input.value, 'Lý do') }); } catch (e) { message(e.message, true); } }); reopen.id = 'deliveryReopen'; reopen.disabled = blocked; body.append(reopen); }
        if (current.capabilities.canReassign) { const label = node('label', null, 'delivery-input-label'); label.append(node('span', 'Nhân viên nhận phụ trách')); const select = node('select'); select.id = 'deliveryPickerSelect'; select.className = 'form-select'; select.disabled = blocked;
            for (const picker of current.pickerOptions) { const option = node('option', picker.displayName); option.value = String(picker.userId); option.selected = picker.userId === current.pickerUserId; select.append(option); } label.append(select); body.append(label);
            const reassign = button('Đổi người soạn', () => { try { const pickerUserId = Number(select.value); if (!Number.isSafeInteger(pickerUserId) || pickerUserId <= 0) throw new Error('Chọn nhân viên hợp lệ.'); command('reassign', { pickerUserId, reason: validNote(reason.input.value, 'Lý do') }); } catch (e) { message(e.message, true); } }); reassign.id = 'deliveryReassign'; reassign.disabled = blocked; body.append(reassign);
        }
    }
    async function recent() {
        try { const list = await get(''); const panel = $('deliveryRecent'); panel.replaceChildren(); if (!list.length) panel.append(note('Chưa có đơn giao.'));
            for (const d of list) { const item = button('', () => lookup(d.code), 'delivery-recent'); item.append(node('strong', d.recipientName || 'Khách nhận hàng'), node('span', d.code + ' · ' + (states[d.state] || d.state))); panel.append(item); }
        } catch (e) { message(e.message, true); }
    }
    $('deliverySearch').addEventListener('submit', e => { e.preventDefault(); lookup($('deliveryKey').value); });
    $('deliveryRefreshList')?.addEventListener('click', recent);
    root.addEventListener('storage', () => { if (!dirty) render(); else message('Yêu cầu lưu ở tab khác đã thay đổi. Kiểm tra trước khi thao tác.'); });
    root.setInterval(() => { if (current && !busy && !lookingUp && document.visibilityState === 'visible') loadCurrent(current.delivery.id).catch(() => message('Chưa tải được cập nhật mới. Kiểm tra kết nối và tải bản mới trước khi thao tác.', true)); }, 8000);
    recent(); const initial = new URLSearchParams(location.search); if (initial.get('key')) lookup(location.origin + '/admin/deliveries?' + initial.toString());
})(typeof window === 'undefined' ? globalThis : window);
