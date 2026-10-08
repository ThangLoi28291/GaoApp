(function (root) {
    'use strict';
    const ops = new Set(['create', 'draft', 'abandon', 'handover', 'depart', 'reassign', 'cancel-request']);
    const tripStates = { Draft: 'Đang xếp chuyến', HandedOver: 'Đã nhận hàng', Departed: 'Đã xuất phát', Abandoned: 'Đã bỏ chuyến nháp' };
    const orderStates = new Set(['Created', 'Picking', 'AwaitingApproval', 'ReadyForHandover', 'HandedOver', 'Delivering', 'AwaitingReconciliation', 'ReadyToSettle', 'CancellationPending', 'Settled', 'Cancelled']);
    const orderLabels = { ReadyForHandover: 'Đã chốt soạn', HandedOver: 'Đã bàn giao', Delivering: 'Đang giao', CancellationPending: 'Chờ xử lý thu hồi' };
    const positive = value => Number.isInteger(value) && value > 0 && value <= 2147483647;
    const guid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
    const version = value => typeof value === 'string' && /^[A-Za-z0-9+/]{10}[AEIMQUYcgkosw048]=$/.test(value);
    function utc(value) {
        if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value) || !Number.isFinite(Date.parse(value))) throw new Error('Giờ máy chủ không hợp lệ.');
        return new Date(value);
    }
    function quantity(value) {
        if (typeof value !== 'string' || value.length > 19 || !/^\d+(?:\.\d{1,4})?$/.test(value)) throw new Error('Lượng hàng không hợp lệ.');
        const [whole, fraction = ''] = value.split('.'), scaled = BigInt(whole) * 10000n + BigInt(fraction.padEnd(4, '0'));
        if (scaled >= 1000000000000000000n) throw new Error('Lượng hàng vượt giới hạn.'); return scaled;
    }
    function displayQuantity(value) {
        quantity(value); let [whole, fraction = ''] = value.split('.'); whole = BigInt(whole).toString().replace(/\B(?=(\d{3})+(?!\d))/g, '.'); fraction = fraction.replace(/0+$/, ''); return whole + (fraction ? ',' + fraction : '');
    }
    function note(value, required = false) {
        if (typeof value !== 'string') throw new Error('Nội dung không hợp lệ.'); const text = value.trim();
        if (text.length > 500 || required && !text) throw new Error('Cần ghi nội dung, tối đa 500 ký tự.'); return text || null;
    }
    function selections(rows) {
        if (!Array.isArray(rows) || !rows.length || rows.length > 50) throw new Error('Chọn từ 1 đến 50 đơn.'); const ids = new Set();
        return rows.map(row => { if (!positive(row.deliveryOrderId) || ids.has(row.deliveryOrderId) || !version(row.version)) throw new Error('Đơn hoặc bản đơn chưa hợp lệ.'); ids.add(row.deliveryOrderId); return { deliveryOrderId: row.deliveryOrderId, expectedVersion: row.version }; });
    }
    function commandBody(operation, detail, fields, clientRequestId, dirty, blocked) {
        if (!ops.has(operation) || !guid(clientRequestId)) throw new Error('Mã thao tác không hợp lệ.');
        if (blocked) throw new Error('Xác nhận yêu cầu cũ và tải bản mới trước khi thao tác.');
        if (dirty && operation !== 'draft' && operation !== 'create') throw new Error('Có chỉnh sửa chuyến chưa lưu. Lưu chuyến hoặc tải lại để bỏ chỉnh sửa trước.');
        if (operation === 'create') { if (!positive(fields.courierUserId)) throw new Error('Chọn người giao.'); return { clientRequestId, courierUserId: fields.courierUserId, orders: selections(fields.orders), note: note(fields.note || '') }; }
        if (!detail || !positive(detail.tripId) || !version(detail.version)) throw new Error('Tải chuyến trước khi thao tác.');
        const body = { clientRequestId, expectedTripVersion: detail.version }, caps = detail.capabilities;
        if (operation === 'draft') {
            if (!caps.canEditDraft || detail.state !== 'Draft') throw new Error('Chuyến không cho phép sửa bản nháp.');
            if (!positive(fields.courierUserId) || fields.courierUserId !== detail.courierUserId && !caps.canReassignCourier) throw new Error('Chưa có quyền đổi người giao.');
            return { ...body, courierUserId: fields.courierUserId, orders: selections(fields.orders), reason: note(fields.reason || '', true) };
        }
        if (operation === 'abandon') { if (!caps.canAbandonDraft || detail.state !== 'Draft') throw new Error('Chưa được phép bỏ chuyến nháp.'); return { ...body, reason: note(fields.reason || '', true) }; }
        if (operation === 'cancel-request') {
            const order = detail.orders.find(row => row.isActive && row.deliveryOrderId === fields.orderId);
            if (!caps.canRequestCancellation || !order?.canRequestCancellation || !['HandedOver', 'Delivering'].includes(order.state)) throw new Error('Đơn không cho phép yêu cầu thu hồi.');
            return { ...body, expectedOrderVersion: order.version, reason: note(fields.reason || '', true) };
        }
        body.orders = selections(detail.orders.filter(row => row.isActive));
        if (operation === 'depart') { if (!caps.canDepart || detail.state !== 'HandedOver') throw new Error('Chuyến chưa được phép xuất phát.'); return body; }
        const courier = operation === 'reassign' ? fields.newCourierUserId : detail.courierUserId;
        if (!positive(courier) || fields.confirmedCourierUserId !== courier || fields.physicalReceiptConfirmed !== true) throw new Error('Xác nhận đúng người giao đã nhận hàng thực tế.');
        if (operation === 'handover' && (!caps.canHandover || detail.state !== 'Draft')) throw new Error('Chuyến chưa sẵn sàng bàn giao.');
        if (operation === 'reassign' && (!caps.canReassignCourier || detail.state !== 'HandedOver' || courier === detail.courierUserId)) throw new Error('Chọn người nhận bàn giao khác trước xuất phát.');
        return { ...body, ...(operation === 'reassign' ? { newCourierUserId: courier, reason: note(fields.reason || '', true) } : {}), confirmedCourierUserId: courier, physicalReceiptConfirmed: true, confirmationNote: note(fields.confirmationNote || '') };
    }
    function intentPath(intent) {
        if (!ops.has(intent.operation) || intent.operation !== 'create' && !positive(intent.tripId)) throw new Error('Chuyến đang lưu không hợp lệ.');
        const base = '/admin/api/delivery-trips';
        if (intent.operation === 'create') { if (intent.tripId !== null) throw new Error('Bản nháp mới không có mã chuyến trước tạo.'); return base; }
        if (intent.operation === 'cancel-request') { if (!positive(intent.orderId)) throw new Error('Đơn đang lưu không hợp lệ.'); return base + '/' + intent.tripId + '/orders/' + intent.orderId + '/cancel-request'; }
        return base + '/' + intent.tripId + '/' + (intent.operation === 'reassign' ? 'courier/reassign' : intent.operation);
    }
    function validateBody(operation, body) {
        const keys = {
            create: 'clientRequestId,courierUserId,note,orders', draft: 'clientRequestId,courierUserId,expectedTripVersion,orders,reason',
            abandon: 'clientRequestId,expectedTripVersion,reason', handover: 'clientRequestId,confirmationNote,confirmedCourierUserId,expectedTripVersion,orders,physicalReceiptConfirmed',
            depart: 'clientRequestId,expectedTripVersion,orders', reassign: 'clientRequestId,confirmationNote,confirmedCourierUserId,expectedTripVersion,newCourierUserId,orders,physicalReceiptConfirmed,reason',
            'cancel-request': 'clientRequestId,expectedOrderVersion,expectedTripVersion,reason'
        };
        if (!body || Object.keys(body).sort().join(',') !== keys[operation] || !guid(body.clientRequestId) || operation !== 'create' && !version(body.expectedTripVersion)) throw new Error('Nội dung yêu cầu đang lưu không hợp lệ.');
        if (body.orders) { const rows = body.orders.map(row => { if (Object.keys(row).sort().join(',') !== 'deliveryOrderId,expectedVersion') throw new Error('Dòng yêu cầu chứa dữ liệu ngoài phạm vi.'); return { deliveryOrderId: row.deliveryOrderId, version: row.expectedVersion }; }); selections(rows); }
        for (const key of ['courierUserId', 'newCourierUserId', 'confirmedCourierUserId']) if (key in body && !positive(body[key])) throw new Error('Người giao đang lưu không hợp lệ.');
        if ('expectedOrderVersion' in body && !version(body.expectedOrderVersion)) throw new Error('Bản đơn đang lưu không hợp lệ.');
        if ('physicalReceiptConfirmed' in body && body.physicalReceiptConfirmed !== true) throw new Error('Yêu cầu chưa xác nhận nhận hàng.');
        if ('newCourierUserId' in body && body.newCourierUserId !== body.confirmedCourierUserId) throw new Error('Người nhận xác nhận không khớp.');
        for (const key of ['note', 'confirmationNote', 'reason']) if (key in body && (key === 'reason' ? note(body[key], true) !== body[key] : body[key] !== null && note(body[key]) !== body[key])) throw new Error('Nội dung ghi chú đang lưu không hợp lệ.');
    }
    function createIntentStore(storage, origin, storeId, actorId) {
        if (!origin || !positive(storeId) || !actorId) throw new Error('Không xác định được cửa hàng và nhân viên. Đăng nhập lại.');
        const key = 'gao.delivery.trip.v1:' + JSON.stringify([origin, storeId, actorId]), witnesses = new WeakMap();
        function validate(intent) {
            let body; try { body = JSON.parse(intent.body); } catch { throw new Error('Yêu cầu chuyến đang lưu không đọc được.'); }
            if (intent.schema !== 1 || intent.storeId !== storeId || intent.actorId !== actorId || typeof intent.body !== 'string' || typeof intent.uncertain !== 'boolean') throw new Error('Yêu cầu chuyến đang lưu không hợp lệ.');
            validateBody(intent.operation, body);
            intentPath(intent); return intent;
        }
        function read() { const raw = storage.getItem(key); if (!raw) return null; try { return validate(JSON.parse(raw)); } catch (error) { throw new Error(error.message || 'Yêu cầu chuyến đang lưu hỏng.'); } }
        function write(intent) { validate(intent); const raw = JSON.stringify(intent); storage.setItem(key, raw); if (storage.getItem(key) !== raw) throw new Error('Không lưu được yêu cầu an toàn. Chưa gửi thao tác.'); return intent; }
        function same(a, b) { return a.body === b.body && a.operation === b.operation && a.tripId === b.tripId && a.orderId === b.orderId; }
        return { read,
            begin(tripId, operation, body, orderId = null) { if (read()) throw new Error('Còn yêu cầu chưa được xác nhận.'); return write({ schema: 1, storeId, actorId, tripId, orderId, operation, body: JSON.stringify(body), uncertain: false }); },
            sending(intent) { const current = read(); if (!current || !same(current, intent)) throw new Error('Yêu cầu đã đổi ở tab khác.'); const attempt = write({ ...current, uncertain: true }); witnesses.set(attempt, current.uncertain); return attempt; },
            acknowledge(ack) {
                const current = read(); if (!current) throw new Error('Không còn yêu cầu cần xác nhận.'); const body = JSON.parse(current.body);
                if (!ack || ack.clientRequestId !== body.clientRequestId || ack.operation !== current.operation || !positive(ack.tripId) || current.tripId !== null && ack.tripId !== current.tripId || !positive(ack.appliedRevision) || !version(ack.appliedVersion) || typeof ack.replayed !== 'boolean' || !Array.isArray(ack.orders) || ack.orders.length > 50) throw new Error('Phản hồi không khớp yêu cầu hoặc thiếu bản xác nhận.');
                utc(ack.recordedAtUtc); const ids = new Set();
                for (const row of ack.orders) { if (!positive(row.deliveryOrderId) || ids.has(row.deliveryOrderId) || !positive(row.revision) || !version(row.version) || !orderStates.has(row.state)) throw new Error('Phản hồi dòng đơn chưa hợp lệ.'); ids.add(row.deliveryOrderId); }
                if (body.orders && (ids.size !== body.orders.length || body.orders.some(row => !ids.has(row.deliveryOrderId))) || current.operation === 'cancel-request' && !ids.has(current.orderId)) throw new Error('Phản hồi không khớp các đơn của yêu cầu.');
                const expectedState = { create: 'ReadyForHandover', draft: 'ReadyForHandover', handover: 'HandedOver', reassign: 'HandedOver', depart: 'Delivering' }[current.operation];
                if (expectedState && ack.orders.some(row => row.state !== expectedState) || current.operation === 'abandon' && ack.orders.length || current.operation === 'cancel-request' && ack.orders.find(row => row.deliveryOrderId === current.orderId)?.state !== 'CancellationPending') throw new Error('Trạng thái xác nhận không khớp thao tác đã gửi.');
                storage.removeItem(key); if (storage.getItem(key) !== null) throw new Error('Không xóa được yêu cầu đã xác nhận.'); return ack.tripId;
            },
            rejectFirstAttempt(attempt) { const current = read(); if (!witnesses.has(attempt) || witnesses.get(attempt) || !current || !same(current, attempt)) return false; storage.removeItem(key); return storage.getItem(key) === null; }
        };
    }
    function validateDetail(detail, storeId) {
        if (!detail || detail.storeId !== storeId || !positive(detail.tripId) || !positive(detail.revision) || !version(detail.version) || !tripStates[detail.state] || !Array.isArray(detail.orders) || !detail.capabilities || ['canEditDraft', 'canAbandonDraft', 'canHandover', 'canDepart', 'canReassignCourier', 'canRequestCancellation'].some(key => typeof detail.capabilities[key] !== 'boolean')) throw new Error('Bản chuyến không khớp cửa hàng.'); utc(detail.serverTimeUtc);
        const ids = new Set(); for (const row of detail.orders) {
            if (!positive(row.deliveryOrderId) || ids.has(row.deliveryOrderId) || !orderStates.has(row.state) || !version(row.version) || !positive(row.sourceWarehouseId) || !positive(row.sourceLegalEntityId) || !Array.isArray(row.lines)) throw new Error('Nguồn đơn trong chuyến không hợp lệ.'); ids.add(row.deliveryOrderId);
            const lines = new Set(); for (const line of row.lines) { if (!positive(line.lineId) || lines.has(line.lineId)) throw new Error('Dòng trách nhiệm bị trùng.'); lines.add(line.lineId); const dispatched = quantity(line.dispatchedBaseQuantityText), returned = quantity(line.acceptedReturnedBaseQuantityText); quantity(line.approvedBaseQuantityText); if (returned > dispatched) throw new Error('Lượng nhận trả vượt chứng từ xuất.'); if (line.inCustodyBaseQuantityText !== null && (quantity(line.inCustodyBaseQuantityText) !== dispatched - returned || !row.custody)) throw new Error('Lượng trách nhiệm chưa có chứng cứ phù hợp.'); }
            if (row.custody && (row.custody.tripId !== detail.tripId || row.custody.courierUserId !== detail.courierUserId || !positive(row.custody.custodyEventId))) throw new Error('Người giữ hàng không khớp chuyến.');
        } return detail;
    }
    function canApply(current, incoming, requestEpoch, latestEpoch, minimum = null) { return requestEpoch === latestEpoch && (!minimum || minimum.tripId !== incoming.tripId || incoming.revision >= minimum.revision) && (!current || current.tripId === incoming.tripId && incoming.revision >= current.revision); }
    function elapsed(serverTime, departure, milliseconds) { const value = Math.max(0, utc(serverTime).getTime() + Math.max(0, milliseconds) - utc(departure).getTime()); const minutes = Math.floor(value / 60000); return Math.floor(minutes / 60) + ' giờ ' + minutes % 60 + ' phút'; }
    const api = { positive, guid, version, utc, quantity, displayQuantity, note, selections, commandBody, intentPath, createIntentStore, validateDetail, canApply, elapsed };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    if (!root.document) return; root.DeliveryTripUi = api;
    const $ = id => document.getElementById(id), workspace = document.querySelector('.delivery-trip-workspace'); if (!workspace) return;
    const storeId = Number(workspace.dataset.store); let registry;
    try { registry = createIntentStore(localStorage, location.origin, storeId, workspace.dataset.actor); } catch (error) { $('deliveryTripMessage').textContent = error.message; return; }
    let detail = null, knownTripId = null, minimumApplied = null, options = null, epoch = 0, listEpoch = 0, busy = false, needsFresh = false, dirty = false, editing = false, inputEditing = false, nextBefore = null, list = [], serverBaseline = null, monotonicBaseline = 0;
    const node = (tag, text, cls) => { const n = document.createElement(tag); if (text != null) n.textContent = text; if (cls) n.className = cls; return n; };
    const time = value => value ? utc(value).toLocaleString('vi-VN') : 'Chưa ghi nhận';
    function message(text, error = false) { $('deliveryTripMessage').textContent = text; $('deliveryTripMessage').className = error ? 'alert alert-warning' : text ? 'alert alert-info' : ''; }
    function blocked() { return busy || needsFresh || Boolean(registry.read()); }
    function button(text, id, action, disabled = false, cls = 'btn btn-primary') { const b = node('button', text, cls); b.type = 'button'; if (id) b.id = id; b.disabled = disabled; b.addEventListener('click', () => Promise.resolve().then(action).catch(error => message(error.message, true))); return b; }
    function field(label, id, kind = 'input', value = '') { const box = node('label', null, 'delivery-input-label'), input = node(kind); input.id = id; input.className = 'form-control'; input.value = value; if (kind === 'input' || kind === 'textarea') input.maxLength = 500; box.append(node('span', label), input); return { box, input }; }
    async function request(url, body) { const headers = {}; if (body != null) { headers['Content-Type'] = 'application/json'; headers.RequestVerificationToken = document.querySelector('[name=__RequestVerificationToken]')?.value || document.querySelector('meta[name=request-verification-token]')?.content || ''; } const response = await fetch(url, { method: body == null ? 'GET' : 'POST', cache: 'no-store', credentials: 'same-origin', headers, body }); const text = await response.text(); let data; try { data = JSON.parse(text); } catch { data = null; } if (!response.ok) { const error = new Error(data?.message || 'Không tải được chuyến (' + response.status + ').'); error.status = response.status; throw error; } return data; }
    async function loadOptions() { const value = await request('/admin/api/delivery-trips/options'); if (value.storeId !== storeId || !Array.isArray(value.couriers) || !Array.isArray(value.orders)) throw new Error('Danh sách lựa chọn không khớp cửa hàng.'); options = value; $('deliveryTripNew').hidden = false; if (detail && !dirty && !inputEditing && !editing) render(); return value; }
    function pendingPanel() { const panel = $('deliveryTripPending'), intent = registry.read(); panel.replaceChildren(); panel.hidden = !intent; if (!intent) return; panel.className = 'delivery-trip-pending'; panel.append(node('strong', 'Còn một yêu cầu chưa xác nhận'), node('p', 'Đã giữ nguyên mã, nội dung và phiên bản của thao tác ' + intent.operation + '. Không tự gửi khi mở lại màn hình.')); panel.append(button('Thử lại đúng yêu cầu', 'deliveryTripRetry', () => send(intent), busy)); }
    const disclosures = new Map();
    function rememberPresentation(panel) {
        for (const section of panel.querySelectorAll('[data-disclosure]')) disclosures.set(section.dataset.disclosure, section.open);
        const fields = [];
        if (inputEditing || dirty || registry.read()) for (const tag of ['input', 'textarea', 'select']) for (const input of panel.querySelectorAll(tag)) if (input.id) fields.push({ id: input.id, value: input.value, checked: input.checked });
        return { identity: panel.dataset.tripId + ':' + panel.dataset.state + ':' + panel.dataset.editing, fields };
    }
    function restorePresentation(panel, saved) {
        if (saved.identity === panel.dataset.tripId + ':' + panel.dataset.state + ':' + panel.dataset.editing) for (const old of saved.fields) { const input = $(old.id); if (input && panel.contains(input)) { input.value = old.value; if (input.type === 'checkbox') input.checked = old.checked; } }
        for (const item of $('deliveryTripList').querySelectorAll('[data-trip-id]')) { const selected = item.dataset.tripId === String(detail?.tripId || ''); item.setAttribute('aria-current', selected ? 'true' : 'false'); item.classList.toggle('is-selected', selected); }
    }
    function disclosure(key, title, id) {
        const section = node('details', null, 'delivery-trip-disclosure'); section.id = id; section.dataset.disclosure = String(detail?.tripId || 'new') + ':' + key; section.open = disclosures.get(section.dataset.disclosure) === true; section.append(node('summary', title)); section.addEventListener('toggle', () => disclosures.set(section.dataset.disclosure, section.open)); return section;
    }
    function progress(state) {
        const steps = node('ol', null, 'delivery-trip-steps'); steps.setAttribute('aria-label', 'Tiến độ chuyến giao');
        const done = state === 'Departed' ? 3 : state === 'HandedOver' ? 2 : state === 'Draft' || state === 'Abandoned' ? 1 : 0;
        const labels = ['Lập chuyến', 'Bàn giao', 'Xuất phát'], keys = ['create', 'handover', 'depart'];
        for (let i = 0; i < labels.length; i++) { const status = i < done ? 'done' : i === done && state !== 'Abandoned' ? 'current' : 'pending', step = node('li', null, 'delivery-trip-step is-' + status); step.dataset.step = keys[i]; step.dataset.status = status; const icon = node('span', status === 'done' ? '✓' : String(i + 1), 'delivery-trip-step-icon'); icon.setAttribute('aria-hidden', 'true'); step.append(icon, node('span', labels[i])); if (status === 'current') step.setAttribute('aria-current', 'step'); steps.append(step); } return steps;
    }
    function orderCard(row, includeRecall) {
        const card = node('article', null, 'delivery-trip-order' + (row.isActive === false ? ' is-inactive' : '')); card.dataset.orderId = String(row.deliveryOrderId); card.dataset.eligible = String(!row.isStale); const heading = node('div', null, 'delivery-trip-order-header'), customer = node('div', null, 'delivery-trip-customer'); customer.append(node('span', Number.isInteger(row.position) && row.isActive !== false ? 'Điểm giao ' + row.position : 'Đơn trong chuyến', 'delivery-trip-stop'), node('h4', row.recipientName || 'Chưa có tên người nhận'), node('p', row.code, 'delivery-trip-order-code')); heading.append(customer, node('span', row.isActive === false ? 'Đã bỏ khỏi chuyến' : orderLabels[row.state] || row.state || 'Đã chốt soạn', 'delivery-trip-state')); card.append(heading, node('p', row.recipientAddress || 'Chưa có địa chỉ giao', 'delivery-trip-address')); if (row.recipientPhone) card.append(node('p', row.recipientPhone, 'delivery-trip-muted')); card.append(node('p', 'Kho: ' + row.sourceWarehouseName + ' · Chủ hàng: ' + row.sourceLegalEntityName, 'delivery-trip-source'));
        if (row.isStale) card.append(node('p', 'Đơn đã thay đổi. Làm mới bản nháp hoặc bỏ đơn này trước bàn giao.', 'delivery-trip-eligibility-error'));
        const scroll = node('div', null, 'delivery-trip-table-scroll'), table = node('table', null, 'delivery-trip-quantity-table'), caption = node('caption', 'Lượng hàng theo từng sản phẩm và đơn vị'), head = node('thead'), headers = node('tr'), body = node('tbody'); table.append(caption); for (const label of ['Sản phẩm', 'ĐVT', 'Chốt soạn', 'Đã xuất', 'Đã nhận trả', 'Đang giữ']) { const th = node('th', label); th.setAttribute('scope', 'col'); headers.append(th); } head.append(headers); table.append(head);
        for (const line of row.lines) { const tr = node('tr', null, 'delivery-trip-line'); tr.dataset.lineId = String(line.lineId); const product = node('th', line.itemName); product.setAttribute('scope', 'row'); tr.append(product, node('td', line.baseUnitName)); for (const [key, value] of [['approved', line.approvedBaseQuantityText], ['dispatched', line.dispatchedBaseQuantityText], ['returned', line.acceptedReturnedBaseQuantityText], ['custody', line.inCustodyBaseQuantityText]]) { const cell = node('td', value === null ? 'Chưa xác nhận' : displayQuantity(value), value === null ? 'is-unknown' : key === 'custody' ? 'is-custody' : ''); cell.dataset.quantity = key; tr.append(cell); } body.append(tr); } table.append(body); scroll.append(table); card.append(scroll);
        if (row.custody) card.append(node('p', row.custody.courierName + ' giữ hàng · ' + row.custody.confirmedByName + ' xác nhận · ' + time(row.custody.confirmedAtUtc), 'delivery-trip-custody-proof')); else card.append(node('p', 'Chưa có xác nhận người giữ hàng thực tế.', 'delivery-trip-muted'));
        const link = node('a', 'Xem đơn / phiếu giao →', 'delivery-trip-order-link'); link.href = '/admin/deliveries?key=' + encodeURIComponent(row.code); card.append(link);
        if (includeRecall && row.isActive && row.canRequestCancellation) { const section = disclosure('recall-' + row.deliveryOrderId, 'Yêu cầu thu hồi đơn', 'deliveryTripRecallPanel-' + row.deliveryOrderId), content = node('div', null, 'delivery-trip-disclosure-body'), reason = field('Lý do thu hồi', 'deliveryTripRecallReason-' + row.deliveryOrderId, 'textarea'), action = button('Gửi yêu cầu thu hồi', 'deliveryTripRecall-' + row.deliveryOrderId, () => command('cancel-request', { orderId: row.deliveryOrderId, reason: reason.input.value }), blocked(), 'btn btn-outline-danger'); content.append(node('p', 'Chỉ gửi yêu cầu xử lý. Hàng và người đang giữ hàng chưa thay đổi.', 'delivery-trip-muted'), reason.box, action); section.append(content); card.append(section); } return card;
    }
    function courierSelect(id, value, includeCurrent = true) { const control = field('Người giao', id, 'select'); const rows = [...(options?.couriers || [])]; if (includeCurrent && detail && !rows.some(x => x.userId === detail.courierUserId)) rows.push({ userId: detail.courierUserId, name: detail.courierName + ' (không còn khả dụng)' }); control.input.append(new Option('Chọn người giao', '')); for (const row of rows) control.input.append(new Option(row.name, String(row.userId))); control.input.value = String(value || ''); return control; }
    function renderDraft() {
        const panel = $('deliveryTripDetail'), saved = rememberPresentation(panel); panel.replaceChildren(); panel.dataset.editing = 'true'; panel.dataset.state = detail?.state || 'New'; panel.dataset.tripId = String(detail?.tripId || ''); panel.dataset.needsFresh = String(needsFresh); const title = node('div', null, 'delivery-trip-title'), titleText = node('div'); titleText.append(node('h3', detail ? 'Sửa chuyến #' + detail.tripId : 'Xếp chuyến mới')); if (detail) titleText.append(node('p', detail.code, 'delivery-trip-code')); title.append(titleText, node('span', 'Chưa bàn giao', 'delivery-trip-state')); panel.append(title, progress(detail?.state || 'New'));
        const courier = courierSelect('deliveryTripCourier', detail?.courierUserId), controls = node('div', null, 'delivery-trip-controls'); courier.input.disabled = Boolean(detail && !detail.capabilities.canReassignCourier); controls.append(courier.box);
        const memo = field(detail ? 'Lý do sửa / đổi thứ tự / làm mới chuyến' : 'Ghi chú chuyến (tùy chọn)', 'deliveryTripDraftReason', 'textarea'); controls.append(memo.box); panel.append(controls, node('p', 'Thứ tự dưới đây là thứ tự ghé điểm. Mỗi đơn giữ nguyên khách, kho nguồn và chủ hàng.', 'delivery-trip-muted'));
        const existing = detail?.orders.filter(x => x.isActive) || [], rows = [...existing, ...(options?.orders || []).filter(x => !existing.some(y => y.deliveryOrderId === x.deliveryOrderId))], selected = existing.map(x => x.deliveryOrderId), cards = node('div');
        function renderSelections() { cards.replaceChildren(); for (const row of rows) { const card = orderCard(row, false), label = node('label', null, 'delivery-trip-selection'), check = node('input'); check.type = 'checkbox'; check.dataset.selectOrder = String(row.deliveryOrderId); check.checked = selected.includes(row.deliveryOrderId); check.disabled = !check.checked && row.state && row.state !== 'ReadyForHandover'; label.append(check, node('span', 'Chọn đơn · ' + row.code)); card.prepend(label); check.addEventListener('change', () => { if (check.checked) selected.push(row.deliveryOrderId); else selected.splice(selected.indexOf(row.deliveryOrderId), 1); dirty = true; renderSelections(); }); const at = selected.indexOf(row.deliveryOrderId); if (at >= 0) { card.append(node('p', 'Điểm ghé ' + (at + 1), 'delivery-trip-option-status'), button('Lên trước', null, () => { if (at > 0) { [selected[at - 1], selected[at]] = [selected[at], selected[at - 1]]; dirty = true; renderSelections(); } }, at === 0 || blocked(), 'btn btn-sm btn-outline-primary')); } cards.append(card); } }
        renderSelections(); panel.append(cards); if (!rows.length) panel.append(node('p', 'Chưa có đơn đủ điều kiện ghép chuyến.', 'delivery-trip-empty'));
        for (const input of [courier.input, memo.input]) input.addEventListener('input', () => { dirty = true; }); const actions = node('div', null, 'delivery-trip-actions delivery-trip-draft-actions'); actions.append(button(detail ? 'Lưu thay đổi chuyến' : 'Tạo chuyến nháp', 'deliveryTripSaveDraft', () => command(detail ? 'draft' : 'create', { courierUserId: Number(courier.input.value), orders: selected.map(id => rows.find(row => row.deliveryOrderId === id)), reason: memo.input.value, note: memo.input.value }), blocked()), button('Bỏ chỉnh sửa / tải bản mới', 'deliveryTripDiscard', async () => { dirty = false; editing = false; if (detail) await loadDetail(detail.tripId, true); else { detail = null; panel.replaceChildren(node('p', 'Chọn một chuyến hoặc tạo chuyến mới.', 'delivery-trip-empty')); } }, busy, 'btn btn-outline-secondary')); panel.append(actions); restorePresentation(panel, saved); pendingPanel();
    }
    function physicalControls(operation) {
        const controls = node('div', null, 'delivery-trip-controls' + (operation === 'handover' ? ' delivery-trip-primary' : '')); if (operation === 'handover') controls.append(node('h4', 'Bàn giao hàng cho ' + detail.courierName), node('p', 'Kiểm tra hàng rồi xác nhận người giao đã nhận đủ các đơn bên dưới.', 'delivery-trip-muted')); let selected = null; if (operation === 'reassign') { selected = courierSelect('deliveryTripNewCourier', '', false); controls.append(selected.box); }
        const confirm = node('div', null, 'delivery-trip-confirm'), label = node('label'), check = node('input'); check.type = 'checkbox'; check.id = 'deliveryTripPhysicalReceipt'; label.append(check, node('span', operation === 'handover' ? 'Tôi xác nhận ' + detail.courierName + ' đã nhận thực tế toàn bộ hàng trong chuyến.' : 'Tôi xác nhận người giao mới đã nhận thực tế toàn bộ hàng đang chịu trách nhiệm.')); confirm.append(label); controls.append(confirm);
        const memo = field('Ghi chú xác nhận (tùy chọn)', 'deliveryTripConfirmationNote', 'textarea'); controls.append(memo.box); const reason = operation === 'reassign' ? field('Lý do đổi người giao', 'deliveryTripReassignReason', 'textarea') : null; if (reason) controls.append(reason.box);
        controls.append(button(operation === 'handover' ? 'Xác nhận đã bàn giao hàng' : 'Xác nhận chuyển người giữ hàng', 'deliveryTrip' + (operation === 'handover' ? 'Handover' : 'Reassign'), () => { const courier = selected ? Number(selected.input.value) : detail.courierUserId; return command(operation, { newCourierUserId: courier, confirmedCourierUserId: courier, physicalReceiptConfirmed: check.checked, confirmationNote: memo.input.value, reason: reason?.input.value }); }, blocked())); return controls;
    }
    function render() {
        if (editing) { renderDraft(); return; }
        const panel = $('deliveryTripDetail'), saved = rememberPresentation(panel), changed = panel.dataset.tripId !== String(detail?.tripId || '') || panel.dataset.state !== detail?.state;
        panel.replaceChildren(); panel.dataset.editing = 'false'; panel.dataset.needsFresh = String(needsFresh);
        if (!detail) { if (needsFresh && knownTripId) panel.append(node('p', 'Đã xác nhận thao tác, cần tải bản chuyến mới.', 'delivery-trip-eligibility-error'), button('Tải bản mới', 'deliveryTripReload', () => loadDetail(knownTripId, true), busy)); return; }
        panel.dataset.tripId = String(detail.tripId); panel.dataset.state = detail.state; panel.classList.toggle('is-updated', changed);
        const activeCount = detail.orders.filter(row => row.isActive).length, title = node('div', null, 'delivery-trip-title'), titleText = node('div'); titleText.append(node('h3', 'Chuyến #' + detail.tripId), node('p', detail.code, 'delivery-trip-code')); title.append(titleText, node('span', tripStates[detail.state], 'delivery-trip-state state-' + detail.state)); panel.append(title, progress(detail.state));
        if (detail.note) panel.append(node('p', detail.note, 'delivery-trip-muted'));
        const facts = node('div', null, 'delivery-trip-facts'); function fact(label, value, annotation, confirmed) { const n = node('div', null, 'delivery-trip-fact' + (confirmed ? ' is-confirmed' : '')); n.append(node('span', label), node('strong', value), node('span', annotation)); facts.append(n); }
        fact('Người giao', detail.courierName, detail.state === 'Draft' ? 'Dự kiến · chưa nhận hàng' : detail.state === 'Abandoned' ? 'Phân công đã bỏ' : 'Đã xác nhận nhận hàng', detail.state === 'HandedOver' || detail.state === 'Departed'); fact('Đơn trong chuyến', String(activeCount) + ' đơn', 'Giữ riêng khách và nguồn hàng', false); fact('Bàn giao', time(detail.handedOverAtUtc), detail.handedOverByName ? detail.handedOverByName + ' xác nhận' : 'Chưa xác nhận nhận hàng', Boolean(detail.handedOverAtUtc)); fact('Xuất phát', time(detail.departedAtUtc), detail.departedByName ? detail.departedByName + ' ghi nhận' : 'Chưa ghi nhận', Boolean(detail.departedAtUtc)); panel.append(facts);
        const actions = node('div', null, 'delivery-trip-actions delivery-trip-toolbar'); actions.append(button('Tải bản mới', 'deliveryTripReload', () => loadDetail(detail.tripId, true), busy, 'btn btn-sm btn-outline-secondary'));
        if (detail.capabilities.canEditDraft) actions.append(button('Sửa đơn / thứ tự giao', 'deliveryTripEdit', async () => { await loadOptions(); editing = true; render(); }, blocked(), 'btn btn-sm btn-outline-primary')); panel.append(actions);
        if (needsFresh) panel.append(node('p', 'Đã xác nhận thao tác nhưng chưa tải được bản mới. Tải bản mới trước khi tiếp tục.', 'delivery-trip-eligibility-error'));
        if (detail.state === 'Draft' && detail.capabilities.canHandover) panel.append(physicalControls('handover'));
        if (detail.capabilities.canDepart) { const next = node('section', null, 'delivery-trip-primary'); next.append(node('h4', 'Sẵn sàng xuất phát'), node('p', 'Hàng đã bàn giao. Ghi nhận khi người giao bắt đầu đi.', 'delivery-trip-muted'), button('Ghi nhận xuất phát', 'deliveryTripDepart', () => command('depart', {}), blocked())); panel.append(next); }
        if (detail.departedAtUtc) { const status = node('section', null, 'delivery-trip-departure'), clock = node('p', '', 'delivery-trip-clock'); clock.id = 'deliveryTripElapsed'; status.append(node('span', 'CHUYẾN ĐÃ XUẤT PHÁT', 'delivery-trip-stop'), clock, node('p', 'Thời gian để theo dõi. Chưa có vị trí hoặc kết quả khách nhận hàng.', 'delivery-trip-muted')); panel.append(status); tick(); }
        const orderHeading = node('div', null, 'delivery-trip-section-heading delivery-trip-orders-heading'); orderHeading.append(node('h4', 'Đơn giao · ' + activeCount), node('span', 'Lượng theo đơn vị của từng sản phẩm', 'delivery-trip-muted')); panel.append(orderHeading);
        for (const row of detail.orders) panel.append(orderCard(row, detail.capabilities.canRequestCancellation));
        if (detail.state === 'Draft' && !detail.capabilities.canHandover) panel.append(node('p', 'Chưa xác nhận nhận hàng. Kiểm tra bản đơn và quyền bàn giao trước khi tiếp tục.', 'delivery-trip-eligibility-error'));
        if (detail.state === 'HandedOver' && detail.capabilities.canReassignCourier) { const section = disclosure('transfer', 'Đổi người giữ hàng trước xuất phát', 'deliveryTripTransferPanel'); section.append(physicalControls('reassign')); panel.append(section); }
        if (detail.capabilities.canAbandonDraft) { const section = disclosure('abandon', 'Bỏ chuyến nháp', 'deliveryTripAbandonPanel'), content = node('div', null, 'delivery-trip-disclosure-body'), reason = field('Lý do bỏ chuyến nháp', 'deliveryTripAbandonReason', 'textarea'); content.append(node('p', 'Chỉ bỏ phân công trong chuyến; đơn và hàng đã soạn giữ nguyên.', 'delivery-trip-muted'), reason.box, button('Bỏ chuyến nháp', 'deliveryTripAbandon', () => command('abandon', { reason: reason.input.value }), blocked(), 'btn btn-outline-danger')); section.append(content); panel.append(section); }
        const history = disclosure('history', 'Lịch sử chuyến · ' + detail.history.length + ' cập nhật', 'deliveryTripHistoryPanel'), ol = node('ol', null, 'delivery-trip-timeline'), labels = { create: 'Tạo chuyến', draft: 'Sửa bản nháp', abandon: 'Bỏ chuyến nháp', handover: 'Xác nhận nhận hàng', depart: 'Ghi xuất phát', reassign: 'Chuyển người giữ hàng', 'cancel-request': 'Yêu cầu thu hồi đơn' };
        for (const row of detail.history) { const item = node('li'); item.append(node('strong', labels[row.action] || row.action), node('span', row.actorName + ' · ' + time(row.recordedAtUtc)), node('small', 'Bản ' + row.revision)); ol.append(item); } history.append(ol); panel.append(history, node('p', 'Cập nhật: ' + time(detail.serverTimeUtc) + ' · Bản ' + detail.revision, 'delivery-trip-sync')); restorePresentation(panel, saved); pendingPanel();
    }
    function tick() { const clock = $('deliveryTripElapsed'); if (clock && detail?.departedAtUtc && serverBaseline) clock.textContent = 'Đã xuất phát ' + elapsed(serverBaseline, detail.departedAtUtc, performance.now() - monotonicBaseline); }
    async function loadDetail(id, discard = false) { if (dirty && !discard) return; const ticket = ++epoch; knownTripId = id; try { const value = validateDetail(await request('/admin/api/delivery-trips/' + id), storeId); if (!canApply(detail?.tripId === id ? detail : null, value, ticket, epoch, minimumApplied)) { if (ticket === epoch && needsFresh) message('Bản đọc còn cũ; cần tải bản mới sau thao tác đã xác nhận.', true); return; } detail = value; needsFresh = false; if (discard) { dirty = false; editing = false; inputEditing = false; } serverBaseline = value.serverTimeUtc; monotonicBaseline = performance.now(); if (!editing && !dirty && !inputEditing) render(); message(''); const url = new URL(location.href); url.searchParams.set('trip', String(id)); history.replaceState(null, '', url); } catch (error) { if (ticket === epoch) { message((needsFresh ? 'Đã xác nhận thao tác, cần tải bản mới. ' : '') + error.message, true); if (!dirty && !inputEditing) render(); } } }
    function renderList() {
        const box = $('deliveryTripList'); box.replaceChildren();
        for (const row of list) { const selected = row.tripId === detail?.tripId, b = button('', null, () => { if (dirty) throw new Error('Có chỉnh sửa chưa lưu. Bỏ chỉnh sửa trước khi mở chuyến khác.'); detail = null; editing = false; return loadDetail(row.tripId, true); }, busy, 'delivery-trip-list-item' + (selected ? ' is-selected' : '')); b.dataset.tripId = String(row.tripId); b.setAttribute('aria-current', selected ? 'true' : 'false'); const top = node('div', null, 'delivery-trip-list-top'); top.append(node('strong', 'Chuyến #' + row.tripId), node('span', tripStates[row.state], 'delivery-trip-list-state state-' + row.state)); b.append(top, node('span', row.courierName + ' · ' + row.activeOrderCount + ' đơn', 'delivery-trip-list-courier'), node('span', row.code, 'delivery-trip-list-code'), node('span', time(row.departedAtUtc || row.handedOverAtUtc || row.createdAtUtc), 'delivery-trip-list-time')); box.append(b); }
        if (!list.length) box.append(node('p', 'Chưa có chuyến theo bộ lọc.', 'delivery-trip-muted')); $('deliveryTripMore').hidden = !nextBefore;
    }
    async function loadList(more = false) { const ticket = ++listEpoch, query = new URLSearchParams({ take: '30' }); if ($('deliveryTripState').value) query.set('state', $('deliveryTripState').value); if (more && nextBefore) query.set('beforeId', String(nextBefore)); const value = await request('/admin/api/delivery-trips?' + query); if (ticket !== listEpoch) return; if (value.storeId !== storeId || !Array.isArray(value.items)) throw new Error('Danh sách chuyến không khớp cửa hàng.'); list = more ? [...list, ...value.items.filter(x => !list.some(y => y.tripId === x.tripId))] : value.items; nextBefore = value.nextBeforeId; renderList(); }
    async function send(intent) {
        if (busy) return; ++epoch; busy = true; let attempt, acknowledged = false; pendingPanel(); try { attempt = registry.sending(intent); const ack = await request(intentPath(intent), attempt.body); const id = registry.acknowledge(ack); acknowledged = true; minimumApplied = { tripId: id, revision: ack.appliedRevision }; knownTripId = id; needsFresh = true; dirty = false; editing = false; inputEditing = false; if (detail?.tripId !== id) detail = null; message('Đã xác nhận thao tác. Đang tải bản mới.'); await loadDetail(id, true); await loadList(); }
        catch (error) { if (!acknowledged && attempt && [400, 403, 404, 409].includes(error.status) && registry.rejectFirstAttempt(attempt)) message(error.message + ' Tải bản mới trước khi thử nội dung mới.', true); else message((acknowledged ? 'Đã xác nhận thao tác; cần tải bản mới. ' : 'Chưa xác nhận kết quả. Giữ nguyên yêu cầu để thử lại. ') + error.message, true); }
        finally { busy = false; if (!dirty) render(); pendingPanel(); }
    }
    async function command(operation, fields) { const body = commandBody(operation, detail, fields, crypto.randomUUID(), dirty, blocked()); const intent = registry.begin(operation === 'create' ? null : detail.tripId, operation, body, fields.orderId || null); return send(intent); }
    $('deliveryTripRefresh').addEventListener('click', () => loadList().catch(error => message(error.message, true))); $('deliveryTripState').addEventListener('change', () => loadList().catch(error => message(error.message, true))); $('deliveryTripMore').addEventListener('click', () => loadList(true).catch(error => message(error.message, true)));
    $('deliveryTripNew').hidden = true; $('deliveryTripNew').addEventListener('click', () => Promise.resolve().then(async () => { if (blocked() || dirty) throw new Error('Xác nhận yêu cầu hoặc bỏ chỉnh sửa trước khi tạo chuyến.'); await loadOptions(); ++epoch; detail = null; dirty = false; editing = true; renderDraft(); }).catch(error => message(error.message, true)));
    pendingPanel(); loadList().catch(error => message(error.message, true)); loadOptions().catch(error => { if (error.status !== 403) message(error.message, true); }); const initial = Number(new URL(location.href).searchParams.get('trip')); if (positive(initial)) loadDetail(initial, true);
    $('deliveryTripDetail').addEventListener('input', () => { inputEditing = true; });
    root.addEventListener('storage', event => { if (event.key?.startsWith('gao.delivery.trip.v1:')) { pendingPanel(); if (!dirty && !inputEditing) render(); } }); setInterval(() => { tick(); if (detail && !busy && !dirty && !editing && !inputEditing) loadDetail(detail.tripId); }, 10000); setInterval(tick, 1000);
})(typeof window === 'undefined' ? globalThis : window);
