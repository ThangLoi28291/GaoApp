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
    const api = { scaledQuantity, displayExact, money, parseUtcDate, canApplyProjection, reportLine, parseLookup, validRevision, createIntentStore };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    if (!root.document) return;
    root.DeliveryPickingUi = api;
    const $ = id => document.getElementById(id);
    if (!$('deliverySearch')) return;
    const workspace = document.querySelector('.delivery-workspace');
    let intents, current = null, currentKey = '', printedRevision = null, busy = false, dirty = false, needsFresh = false, lookingUp = false, sequence = 0, readEpoch = 0;
    try { intents = createIntentStore(localStorage, workspace.dataset.actor ? location.origin + ':' + workspace.dataset.actor : ''); } catch (e) { message(e.message, true); }
    function node(tag, text, cls) { const element = document.createElement(tag); if (text != null) element.textContent = text; if (cls) element.className = cls; return element; }
    function button(text, action, cls = 'btn btn-outline-primary') { const element = node('button', text, cls); element.type = 'button'; element.addEventListener('click', action); return element; }
    function message(text, error = false) { const element = $('deliveryPageMessage'); element.textContent = text; element.className = 'delivery-page-message' + (error ? ' is-error' : ''); }
    function time(value) { return value ? parseUtcDate(value).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour12: false }) : 'Chưa ghi nhận'; }
    function note(text, cls = 'delivery-muted') { return node('p', text, cls); }
    function inputField(label, value, id, numeric = false) {
        const wrapper = node('label', null, 'delivery-input-label'); const title = node('span', label);
        const input = node('input'); input.type = 'text'; input.id = id; input.className = 'form-control'; input.value = value ?? '';
        input.maxLength = numeric ? 19 : 1000; if (numeric) { input.inputMode = 'decimal'; input.placeholder = 'Chưa ghi nhận'; }
        input.addEventListener('input', () => { dirty = true; }); wrapper.append(title, input); return { wrapper, input };
    }
    async function get(path) {
        const response = await fetch('/admin/api/deliveries' + path, { cache: 'no-store', credentials: 'same-origin' });
        const data = await response.json(); if (!response.ok) throw new Error(data.message || 'Không có quyền xem hoặc không tìm thấy đơn.'); return data;
    }
    function pending() { try { return intents?.read() ?? null; } catch (e) { message(e.message, true); return { broken: true }; } }
    function pendingPanel(panel) {
        const intent = pending(); if (!intent) return;
        const box = node('section', null, 'delivery-pending'); box.append(node('strong', 'Có thao tác chưa xác nhận'), note('Giữ nguyên mã yêu cầu và nội dung. Thử lại để xác nhận kết quả đã lưu, sau đó tải bản mới nhất.'));
        if (!intent.broken) box.append(button('Thử lại yêu cầu cũ', () => send(intent), 'btn btn-warning'), note('Đơn #' + intent.deliveryId + ' · ' + intent.operation));
        panel.append(box);
    }
    async function send(intent) {
        if (busy || lookingUp || !intents) return; readEpoch++; busy = true; render(); message('Đang xác nhận thao tác…');
        let acknowledged = false;
        try {
            intents.sending(intent);
            const token = document.querySelector('meta[name="request-verification-token"]')?.content || document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!token) throw new Error('Thiếu mã bảo vệ phiên. Hãy tải lại trang và thử lại yêu cầu cũ.');
            const response = await fetch('/admin/api/deliveries/' + intent.deliveryId + '/picking/' + intent.operation, {
                method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', RequestVerificationToken: token }, body: intent.body
            });
            let data; try { data = await response.json(); } catch { throw new Error('Chưa xác nhận được kết quả. Thử lại yêu cầu cũ.'); }
            if (!response.ok) {
                // A first received rejection has no preceding uncertain attempt. Retries retain identity after permission or state changes.
                if ([400, 403, 404, 409, 422].includes(response.status)) intents.rejectFirstAttempt(intent);
                throw new Error(data.message || 'Yêu cầu bị từ chối.');
            }
            intents.acknowledge(data);
            acknowledged = true; needsFresh = true; dirty = false;
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
            current = data; currentKey = knownId ? data.delivery.lookupToken : parsed.key || detail.code;
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
        if (dirty && !force) {
            if (data.delivery.version !== current.delivery.version) message('Đơn đã có thay đổi. Tải bản mới để tiếp tục; nội dung đang nhập vẫn được giữ.'); return false;
        }
        const changed = needsFresh || data.delivery.version !== current.delivery.version || JSON.stringify(data.capabilities) !== JSON.stringify(current.capabilities);
        current = data; needsFresh = false; if (force) dirty = false; if (changed || force) render(); return true;
    }
    function render() {
        $('deliveryKey').disabled = busy || lookingUp; $('deliverySearch').querySelector('button').disabled = busy || lookingUp;
        const panel = $('deliveryDetail'); if (!current) { panel.hidden = true; return; }
        panel.replaceChildren(); panel.hidden = false;
        const d = current.delivery, capabilities = current.capabilities, blocked = busy || lookingUp || needsFresh || !!pending() || !intents;
        panel.dataset.revision = String(d.revision); panel.dataset.version = d.version; panel.dataset.state = d.state;
        panel.dataset.needsFresh = String(needsFresh);
        if (needsFresh) panel.append(note('Thao tác đã được xác nhận. Nội dung dưới đây là bản đã xem trước đó; tải bản mới để xem kết quả hiện hành.', 'delivery-revision-warning'));
        const header = node('div', null, 'delivery-detail-heading'); header.append(node('h3', d.code), node('span', states[d.state] || d.state, 'delivery-badge state-' + d.state)); panel.append(header);
        panel.append(note('Bản ' + d.revision + ' · Tạo ' + time(d.createdAtUtc) + ' · Quầy #' + d.createdTerminalId + ' · Giỏ #' + d.sourceCartId));
        if (printedRevision && BigInt(printedRevision) < BigInt(d.revision)) panel.append(note('Phiếu đang quét là bản ' + printedRevision + '. Đơn hiện tại là bản ' + d.revision + ' — kiểm tra lượng mới trước khi soạn hoặc giao.', 'delivery-revision-warning'));
        const recipient = node('section', null, 'delivery-recipient'); recipient.append(node('strong', d.recipientName), node('span', d.recipientPhone), node('span', d.recipientAddress)); if (d.note) recipient.append(node('span', d.note)); panel.append(recipient);
        const facts = node('div', null, 'delivery-picking-facts'); facts.append(note('Người đang phụ trách: ' + (current.pickerName || 'Chưa nhận soạn')), note('Nhận lúc: ' + time(current.assignedAtUtc)), note('Gửi chốt: ' + time(current.submittedAtUtc))); panel.append(facts);
        const effective = current.lines.filter(line => line.isActive);
        const reported = effective.filter(line => line.reportedQuantityText !== null).length;
        const progress = node('div', null, 'delivery-picking-progress'); progress.setAttribute('role', 'progressbar'); progress.setAttribute('aria-valuemin', '0'); progress.setAttribute('aria-valuemax', String(effective.length)); progress.setAttribute('aria-valuenow', String(reported));
        const bar = node('span'); bar.style.width = effective.length ? (reported * 100 / effective.length) + '%' : '0%'; progress.append(bar); panel.append(note(reported + '/' + effective.length + ' dòng đã ghi nhận kết quả'), progress);
        pendingPanel(panel);
        const actions = node('div', null, 'delivery-actions');
        const refresh = button('Tải bản mới', () => loadCurrent(d.id, true).catch(e => message(e.message, true))); refresh.disabled = busy || lookingUp; actions.append(refresh);
        if (capabilities.canClaim) { const claim = button('Nhận soạn đơn này', () => command('claim'), 'btn btn-primary'); claim.id = 'deliveryClaim'; claim.disabled = blocked; actions.append(claim); }
        const bill = node('a', 'Mở / in phiếu A5', 'btn btn-outline-primary'); bill.href = '/admin/deliveries/' + d.id + '/bill'; bill.target = '_blank'; bill.rel = 'noopener'; actions.append(bill); panel.append(actions);
        const lines = node('section', null, 'delivery-picking-lines'); lines.append(node('h4', 'Hàng trong đơn'));
        for (const line of current.lines) {
            if (!line.isActive && line.isReplacement) continue;
            const card = node('article', null, 'delivery-picking-line' + (line.reportedQuantityText === null ? '' : scaledQuantity(line.reportedQuantityText) < scaledQuantity(line.plannedQuantityText) ? ' has-shortage' : ' is-reported'));
            card.dataset.lineId = String(line.lineId);
            const title = node('div', null, 'delivery-line-title'); title.append(node('strong', line.itemName), node('span', line.isReplacement ? 'Thay cho dòng #' + line.originalRootLineId : 'Hàng gốc', 'delivery-small-badge')); card.append(title);
            card.append(note('Đặt gốc: ' + displayExact(line.orderedQuantityText) + ' ' + line.unitName + ' · Kế hoạch: ' + displayExact(line.plannedQuantityText) + ' · Đơn giá: ' + displayExact(line.unitPriceText, 2) + ' đ'));
            const status = line.reportedQuantityText === null ? 'Chưa ghi nhận lượng thực có' : 'Đã soạn: ' + displayExact(line.reportedQuantityText) + ' ' + line.unitName;
            card.append(node('div', status, 'delivery-line-status'), note(line.reportedAtUtc ? (line.reporterName || 'Nhân viên #' + line.reporterUserId) + ' · ' + time(line.reportedAtUtc) : 'Chờ người soạn kiểm tra'));
            if (line.shortageReason) card.append(note('Lý do: ' + line.shortageReason, 'delivery-shortage-reason'));
            if (line.approvedQuantityText !== null) card.append(note('Đã duyệt: ' + displayExact(line.approvedQuantityText) + ' ' + line.unitName + ' · ' + money(line.approvedNetText), 'delivery-approved-fact'));
            if (capabilities.canReport && line.isActive) {
                const fields = node('div', null, 'delivery-report-fields');
                const quantity = inputField('Lượng đã soạn (' + line.unitName + ')', line.reportedQuantityText, 'deliveryQty-' + line.lineId, true);
                const reason = inputField('Lý do thiếu / không có hàng', line.shortageReason, 'deliveryReason-' + line.lineId);
                quantity.input.disabled = reason.input.disabled = blocked;
                const full = button('Đủ theo kế hoạch', () => { quantity.input.value = line.plannedQuantityText; dirty = true; }); full.disabled = blocked;
                const missing = button('Không có hàng', () => { quantity.input.value = '0'; dirty = true; reason.input.focus(); }); missing.disabled = blocked;
                fields.append(quantity.wrapper, reason.wrapper); card.append(fields, full, missing);
            }
            lines.append(card);
        }
        panel.append(lines);
        if (capabilities.canReport) {
            const save = button('Lưu kết quả soạn', () => {
                try {
                    const reports = effective.filter(line => $('deliveryQty-' + line.lineId).value !== '').map(line => reportLine(line, $('deliveryQty-' + line.lineId).value, $('deliveryReason-' + line.lineId).value));
                    if (!reports.length) throw new Error('Nhập ít nhất một kết quả soạn.'); command('report', { lines: reports });
                } catch (e) { message(e.message, true); }
            }, 'btn btn-primary'); save.id = 'deliverySaveReport'; save.disabled = blocked; panel.append(save);
        }
        if (capabilities.canSubmit) { const submit = button('Gửi chốt soạn', () => command('submit'), 'btn btn-success'); submit.id = 'deliverySubmitPicking'; submit.disabled = blocked; panel.append(submit, note('Lưu kết quả trước khi gửi. Tất cả dòng cần có kết quả, kể cả lượng bằng 0.')); }
        const totals = node('div', null, 'delivery-picking-totals'); totals.append(note('Giá trị gốc: ' + money(current.quotedTotalText)), note('Bản soạn dự kiến: ' + money(current.draftTotalText)));
        if (current.approvedTotalText !== null) totals.append(node('strong', 'Lượng được duyệt: ' + money(current.approvedTotalText))); else totals.append(note('Chưa có bản duyệt hiện hành.')); panel.append(totals);
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
            for (const d of list) { const item = button(d.code + ' · ' + d.recipientName + ' · ' + (states[d.state] || d.state), () => lookup(d.code), 'delivery-recent'); panel.append(item); }
        } catch (e) { message(e.message, true); }
    }
    $('deliverySearch').addEventListener('submit', e => { e.preventDefault(); lookup($('deliveryKey').value); });
    $('deliveryRefreshList')?.addEventListener('click', recent);
    root.addEventListener('storage', () => { if (!dirty) render(); else message('Yêu cầu lưu ở tab khác đã thay đổi. Kiểm tra trước khi thao tác.'); });
    root.setInterval(() => { if (current && !busy && !lookingUp && document.visibilityState === 'visible') loadCurrent(current.delivery.id).catch(() => message('Chưa tải được cập nhật mới. Kiểm tra kết nối và tải bản mới trước khi thao tác.', true)); }, 8000);
    recent(); const initial = new URLSearchParams(location.search); if (initial.get('key')) lookup(location.origin + '/admin/deliveries?' + initial.toString());
})(typeof window === 'undefined' ? globalThis : window);
