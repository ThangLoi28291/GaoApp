(function () {
    'use strict';
    const root = document.getElementById('gao-store-wall');
    if (!root) return;
    const q = selector => root.querySelector(selector);
    const esc = value => String(value ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
    const labels = { pos: 'Quầy tính tiền', receipt: 'Nhập hàng', warehouse: 'Kiểm kê · Chuyển kho', label: 'In tem sản phẩm', review: 'Duyệt phiếu', delivery: 'Giao hàng' };
    const initials = { pos: '01', receipt: '02', warehouse: '03', label: '04', review: '05' };
    const captions = { receipt: 'Đang nhập dữ liệu phiếu nhập', pos: 'Đang thao tác tại quầy', warehouse: 'Đang thao tác chứng từ kho', label: 'Đang thao tác phiếu in tem', review: 'Đang thao tác duyệt phiếu' };
    const models = window.GaoStoreScenes;
    const observed = new Set(), effectSignatures = new Map(), flashTimers = new Map();
    const sceneResize = new ResizeObserver(entries => entries.forEach(({ target, contentRect }) => {
        target.style.setProperty('--wm-art-scale', String(Math.min(1, contentRect.width / 252, contentRect.height / 116)));
    }));
    let data = { people: [], events: [] }, source, retryTimer, retryDelay = 2000, connected = false, stopped = false, lastSignal = 0, clockOffset = 0, seenEvents = null, nextRender = Infinity;
    let motion = true, motionOverride = false, theme = 'light', selectedUser = null;
    // v2 starts light even when the former wallboard saved its dark default.
    try { motion = localStorage.getItem('gao.monitor.motion') !== 'off'; theme = localStorage.getItem('gao.monitor.theme.v2') === 'dark' ? 'dark' : 'light'; } catch (_) { }
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const screenFits = window.matchMedia('(min-width:1100px) and (min-height:620px)');
    const pages = new Map();
    let fitLayout = true, cycle = !reduced.matches, cycleOverride = false, nextCycle = Date.now() + 10000, detailRef = null, detailTrigger = null;
    try {
        fitLayout = localStorage.getItem('gao.monitor.layout') !== 'detail';
        const savedCycle = localStorage.getItem('gao.monitor.cycle');
        if (savedCycle !== null) { cycle = savedCycle === 'on'; cycleOverride = cycle; }
    } catch (_) { }
    const wallLayout = () => fitLayout && screenFits.matches;
    const cycling = () => cycle && (!reduced.matches || cycleOverride);
    const effectsEnabled = () => motion && (!reduced.matches || motionOverride);
    const now = () => Date.now() + clockOffset;
    const time = value => new Date(typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value) && !/(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value + 'Z' : value).toLocaleTimeString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit', second: '2-digit' });
    const seconds = value => Math.max(0, Math.floor((now() - new Date(typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value) && !/(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value + 'Z' : value).getTime()) / 1000));
    const isEditing = person => connected && person.state === 'editing' && (!person.editedAtUtc || seconds(person.editedAtUtc) < 15);
    function duration(value) {
        const s = seconds(value);
        return s < 60 ? `${s} giây` : s < 3600 ? `${Math.floor(s / 60)} phút ${s % 60} giây` : `${Math.floor(s / 3600)} giờ ${Math.floor(s % 3600 / 60)} phút`;
    }
    function stamp(value) { return `<time datetime="${esc(value)}">${time(value)}</time><span class="wm-age" data-at="${esc(value)}">${duration(value)} trước</span>`; }
    function design() {
        const activeMotion = effectsEnabled();
        root.dataset.theme = theme; root.dataset.paused = String(!connected || !activeMotion || document.hidden); root.dataset.motion = String(activeMotion); root.dataset.forceMotion = String(motionOverride);
        document.documentElement.style.colorScheme = theme;
        q('#wm-theme').textContent = theme === 'dark' ? 'Nền sáng' : 'Nền tối';
        q('#wm-motion').setAttribute('aria-pressed', String(activeMotion));
        q('#wm-motion').textContent = activeMotion ? 'Hiệu ứng: bật' : 'Hiệu ứng: tắt';
        q('#wm-layout').textContent = wallLayout() ? 'Vừa màn hình' : 'Xem đầy đủ';
        q('#wm-layout').setAttribute('aria-pressed', String(wallLayout()));
        q('#wm-layout').disabled = !screenFits.matches;
        q('#wm-layout').title = screenFits.matches ? 'Đổi giữa tổng quan vừa màn hình và danh sách đầy đủ' : 'Chế độ vừa màn hình dùng từ 1100 × 620 px; màn hình nhỏ xem dạng cuộn';
        q('#wm-cycle').hidden = !wallLayout();
        q('#wm-cycle').textContent = 'Luân phiên: ' + (cycling() ? 'bật' : 'tắt');
        q('#wm-cycle').setAttribute('aria-pressed', String(cycling()));
        q('#wm-layout-hint').textContent = wallLayout() ? 'Bấm thẻ xem chi tiết · ' + (cycling() ? 'Luân phiên mỗi 10 giây' : 'Dùng ‹ › để chuyển trang') : 'Trạng thái thao tác trên GaoApp · Giờ Việt Nam';
        window.dispatchEvent(new CustomEvent('gao:monitor-state', { detail: { connected, motion: activeMotion, theme } }));
    }
    const nodeKey = node => node?.nodeType === 1 ? node.getAttribute('data-key') : null;
    function children(element, template) {
        [...template.childNodes].forEach((node, i) => {
            const key = nodeKey(node);
            if (key && nodeKey(element.childNodes[i]) !== key) {
                const found = [...element.childNodes].find(child => nodeKey(child) === key);
                element.insertBefore(found || node.cloneNode(true), element.childNodes[i] || null);
            }
            if (element.childNodes[i]) patch(element.childNodes[i], node); else element.appendChild(node.cloneNode(true));
        });
        while (element.childNodes.length > template.childNodes.length) element.lastChild.remove();
    }
    function patch(oldNode, newNode) {
        if (oldNode.isEqualNode(newNode)) return;
        if (oldNode.nodeType !== newNode.nodeType || oldNode.nodeName !== newNode.nodeName) { oldNode.replaceWith(newNode.cloneNode(true)); return; }
        if (oldNode.nodeType === 3) { oldNode.nodeValue = newNode.nodeValue; return; }
        if (oldNode.nodeType !== 1) return;
        [...oldNode.attributes].forEach(a => { if (!newNode.hasAttribute(a.name) && !(a.name === 'style' && oldNode.hasAttribute('data-fit'))) oldNode.removeAttribute(a.name); });
        [...newNode.attributes].forEach(a => { if (oldNode.getAttribute(a.name) !== a.value) oldNode.setAttribute(a.name, a.value); });
        children(oldNode, newNode);
    }
    function markup(element, html) { const template = document.createElement('div'); template.innerHTML = html; children(element, template); }
    function pageOf(key, items) {
        const area = q('#wm-' + key);
        const size = wallLayout() ? Math.max(1, Math.floor((area.clientWidth - 18 + 8) / ((key === 'delivery' ? 210 : key === 'pos' ? 280 : 260) + 8))) : Math.max(1, items.length);
        const count = Math.max(1, Math.ceil(items.length / size));
        const page = pages.get(key) || { index: 0 };
        page.index = Math.min(page.index, count - 1); page.count = count; page.size = size;
        pages.set(key, page);
        area.style.setProperty('--wm-columns', String(size));
        return { rows: wallLayout() ? items.slice(page.index * size, (page.index + 1) * size) : items, page };
    }
    function pager(key, page, total) {
        return wallLayout() && page.count > 1 ? `<div class="wm-pager" aria-label="Trang ${esc(labels[key])}" title="${total} công việc · ${page.size} ô mỗi trang"><button type="button" data-page-area="${key}" data-page-step="-1" aria-label="Trang trước ${esc(labels[key])}">‹</button><span>${page.index + 1}/${page.count}</span><button type="button" data-page-area="${key}" data-page-step="1" aria-label="Trang sau ${esc(labels[key])}">›</button></div>` : '';
    }
    function badge(state, text) { return `<span class="wm-badge" data-status="${state}"><i class="wm-dot"></i>${esc(text)}</span>`; }
    function scene(key, mode, caption, workKey = '', event = null, document = '', documentTitle = '') {
        const effect = models.effect(key, workKey, event, mode === 'working');
        const activeEffect = mode === 'idle' ? '' : effect;
        return `<div class="wm-scene${activeEffect ? ' fx-' + activeEffect : ''}" data-mode="${mode}" data-effect="${activeEffect}" data-approved="${key === 'review' && effect === 'approved'}" data-bill="${key === 'pos' && effect === 'print'}" data-active="${mode === 'working' || mode === 'saved'}"><div class="wm-visual" data-fit aria-hidden="true">${models.render(key, workKey, event, document, mode === 'working', documentTitle)}<span class="wm-save-confirm">✓</span></div><div class="wm-scene-caption"><span>${esc(caption)}</span><small>${mode === 'working' ? 'Tín hiệu thao tác trực tiếp' : mode === 'saved' ? 'Thao tác đã lưu thành công' : 'Chờ thao tác tiếp theo'}</small></div></div>`;
    }
    function laneKey(key, item) { return key === 'pos' && item.terminal ? item.terminal : `${item.userId}|${item.terminal || ''}|${item.workKey || ''}`; }
    function lanes(key) {
        const map = new Map();
        data.people.filter(p => p.module === key).forEach(p => {
            const id = laneKey(key, p); let lane = map.get(id);
            if (!lane) { lane = { id, people: [], event: null }; map.set(id, lane); }
            lane.people.push(p);
        });
        data.events.filter(e => e.module === key).forEach(e => {
            const id = laneKey(key, e); let lane = map.get(id);
            if (!lane) {
                lane = { id, people: [], event: null }; map.set(id, lane);
            }
            if (!lane.event) lane.event = e;
        });
        const ordered = [...map.values()].sort((a, b) => (key === 'pos' ? 0 : Number(b.people.length > 0) - Number(a.people.length > 0)) || a.id.localeCompare(b.id, 'vi', { numeric: true }));
        if (key !== 'pos') return ordered;
        const terminals = data.checkouts?.map(checkout => checkout.terminal);
        return ordered.filter(lane => !terminals || terminals.includes(lane.id)).slice(0, 3);
    }
    function station(key, lane) {
        const p = lane.people.find(person => person.state === 'editing') || lane.people[0], e = lane.event;
        const currentEvent = !p?.workKey || !e?.workKey || p.workKey === e.workKey ? e : null;
        const hasEditing = lane.people.some(isEditing);
        const recent = connected && currentEvent && seconds(e.occurredAtUtc) < 12;
        const editedAfterSave = e && lane.people.some(person => person.state === 'editing' && person.editedAtUtc && new Date(typeof person.editedAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(person.editedAtUtc) ? person.editedAtUtc + 'Z' : person.editedAtUtc) > new Date(typeof e.occurredAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(e.occurredAtUtc) ? e.occurredAtUtc + 'Z' : e.occurredAtUtc));
        // A completed save supersedes earlier typing; only a newer interaction resumes the work animation.
        const editing = hasEditing && (!currentEvent || editedAfterSave);
        const saved = recent && !editing;
        const state = !connected ? 'stale' : saved ? 'saved' : editing ? 'working' : p ? 'viewing' : 'saved';
        if (recent) nextRender = Math.min(nextRender, new Date(typeof e.occurredAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(e.occurredAtUtc) ? e.occurredAtUtc + 'Z' : e.occurredAtUtc).getTime() + 12000);
        if (editing) lane.people.filter(person => person.state === 'editing' && person.editedAtUtc).forEach(person => {
            nextRender = Math.min(nextRender, new Date(typeof person.editedAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(person.editedAtUtc) ? person.editedAtUtc + 'Z' : person.editedAtUtc).getTime() + 15000);
        });
        const person = p ? [...new Set(lane.people.map(item => item.person))].join(' · ') : e?.person || 'Nhân viên';
        const terminal = p?.terminal || e?.terminal || 'Tại cửa hàng';
        const title = editing ? captions[key] : currentEvent ? e.text.replace(/^vừa /, '').replace(/^./, c => c.toLocaleUpperCase('vi')) : 'Đang mở màn hình';
        const document = p?.document || e?.document;
        // A saved rename may arrive from the other receipt area while this page's signed title is older.
        const titleEvent = data.events.find(item => item.workKey && item.workKey === (p?.workKey || e?.workKey) &&
            Object.hasOwn(item, 'documentTitle') && (!p?.openedAtUtc || new Date(typeof item.occurredAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(item.occurredAtUtc) ? item.occurredAtUtc + 'Z' : item.occurredAtUtc) >= new Date(typeof p.openedAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(p.openedAtUtc) ? p.openedAtUtc + 'Z' : p.openedAtUtc)));
        const documentTitle = titleEvent ? titleEvent.documentTitle : p?.documentTitle ?? currentEvent?.documentTitle;
        const activity = `<div class="wm-task-title">${esc(title)}</div>${document ? `<div class="wm-task-document">${esc(document)}</div>` : ''}${documentTitle ? `<div class="wm-task-document-title">${esc(documentTitle)}</div>` : ''}${currentEvent?.detail ? `<div class="wm-task-detail">${esc(currentEvent.detail)}</div>` : ''}${p?.document && p.document !== currentEvent?.document && currentEvent?.document?.startsWith('Lệnh in') ? `<div class="wm-task-detail">${esc(currentEvent.document)}</div>` : ''}${!currentEvent ? '<div class="wm-task-detail">Chưa có lần lưu mới cho công việc này.</div>' : ''}${e && !currentEvent ? `<div class="wm-task-detail">Lần lưu trước: ${esc(e.text)}${e.document ? ' · ' + esc(e.document) : ''}</div>` : ''}`;
        const clock = e ? `<div class="wm-task-time">${stamp(e.occurredAtUtc)}</div>${p && e.userId !== p.userId ? `<small class="wm-event-author">Lần lưu của ${esc(e.person)}</small>` : ''}` : p?.openedAtUtc ? `<div class="wm-task-time wm-open-time">Mở lúc ${stamp(p.openedAtUtc)}</div>` : '';
        const opened = p ? `<div class="wm-presence-time">${p.openedAtUtc ? `Mở màn hình lúc ${time(p.openedAtUtc)} · <span class="wm-opened" data-at="${esc(p.openedAtUtc)}">${duration(p.openedAtUtc)}</span>` : 'Màn hình đang mở'}</div>` : '<div class="wm-presence-time">Cập nhật đã lưu · không có tín hiệu đang thao tác</div>';
        const status = !connected ? 'Tín hiệu cũ' : saved ? (wallLayout() ? 'Vừa lưu' : 'Vừa lưu xong') : editing ? (wallLayout() ? 'Đang làm' : 'Đang thao tác') : p ? 'Đang mở' : 'Đã lưu';
        const caption = saved ? 'Đã lưu thành công' : editing ? captions[key] : p ? 'Chờ thao tác tiếp theo' : 'Lần lưu gần nhất';
        const heading = key === 'pos' ? `<strong>${esc(terminal)}</strong><span class="wm-terminal">${esc(person)}</span>` : `<strong>${esc(person)}</strong><span class="wm-terminal">${esc(terminal)}</span>`;
        const actorId = p?.userId || e?.userId;
        const signature = saved ? 'event:' + e.id : editing ? 'edit:' + lane.people.map(person => person.editedAtUtc || person.openedAtUtc).join('|') : '';
        return `<section class="wm-station${p ? ' wm-live-person' : ''}" role="button" tabindex="0" aria-pressed="${selectedUser === actorId}" aria-label="Xem hoạt động của ${esc(person)}" data-user-id="${actorId}" data-key="${esc(key + '|' + lane.id)}" data-terminal="${esc(terminal)}" data-work-key="${esc(p?.workKey || e?.workKey || '')}" data-status="${state}" data-event-id="${e?.id || ''}" data-effect-signature="${esc(signature)}"><div class="wm-station-heading"><div class="wm-person"><span class="wm-avatar">${esc(person.slice(0, 1))}</span><div>${heading}</div></div>${badge(state, status)}</div><div class="wm-station-work">${scene(key, saved ? 'saved' : editing ? 'working' : 'idle', caption, p?.workKey || e?.workKey, currentEvent, document, documentTitle)}<div class="wm-station-content">${activity}</div></div>${clock}${opened}</section>`;
    }
    function renderCard(key) {
        const items = lanes(key), people = items.flatMap(lane => lane.people);
        const { rows, page } = pageOf(key, items);
        const working = people.filter(isEditing).length;
        const recent = data.events.find(e => e.module === key && (key !== 'pos' || items.some(lane => lane.id === laneKey(key, e)))), justSaved = connected && recent && seconds(recent.occurredAtUtc) < 12;
        const state = !connected ? 'stale' : working ? 'working' : justSaved ? 'saved' : people.length ? 'viewing' : 'idle';
        const stateLabel = wallLayout() ? key === 'pos' ? `${items.length} việc${!connected ? ' · cũ' : working ? ' · ' + working + ' đang làm' : ''}` : `${items.length}` : !connected ? 'Tín hiệu cũ' : working ? `${working} đang thao tác · ${items.length} việc` : people.length ? `${people.length} đang mở · ${items.length} việc` : recent ? `${items.length} việc đã lưu` : 'Sẵn sàng';
        const title = `<div class="wm-area-heading"><div class="wm-area-title"><span class="wm-area-number">${initials[key]}</span><h2 title="${labels[key]}">${wallLayout() && key === 'warehouse' ? 'Kho · Kiểm kê' : wallLayout() && key === 'label' ? 'In tem' : labels[key]}</h2></div><div class="wm-area-tools" title="${items.length} công việc · ${working} đang thao tác">${badge(state, stateLabel)}${pager(key, page, items.length)}</div></div>`;
        const area = q('#wm-' + key);
        const empty = `<div class="wm-area-empty">${scene(key, 'idle', 'Chưa có thao tác mới')}<p>Nhân viên và công việc sẽ xuất hiện tại đây.</p></div>`;
        const placeholders = key === 'pos' ? Array.from({ length: Math.max(0, (wallLayout() ? Math.min(3, page.size) : 3) - items.length) }, (_, index) => `<section class="wm-station wm-empty-station" data-key="pos-empty-${index}" data-status="${connected ? 'idle' : 'stale'}"><div class="wm-station-heading"><div class="wm-person"><span class="wm-avatar">—</span><div><strong>Vị trí quầy ${items.length + index + 1}</strong><span class="wm-terminal">Chưa có nhân viên</span></div></div>${badge(connected ? 'idle' : 'stale', connected ? 'Quầy trống' : 'Tín hiệu cũ')}</div><div class="wm-station-work">${scene('pos', 'idle', 'Chưa có hóa đơn mở')}<div class="wm-station-content"><div class="wm-task-title">Chờ nhân viên sử dụng quầy</div><div class="wm-task-detail">Mã quầy thực tế sẽ xuất hiện khi có tín hiệu.</div></div></div></section>`).join('') : '';
        markup(area, title + (items.length || key === 'pos' ? `<div class="wm-stations">${rows.map(l => station(key, l)).join('')}${placeholders}</div>` : empty));
        area.dataset.state = state;
        area.dataset.multiple = String(items.length > 1);
    }
    function renderDetail() {
        if (!detailRef) return;
        let html = '';
        if (detailRef.module === 'delivery') {
            const order = window.GaoDeliveryWall.visibleOrders(data.delivery, now()).find(item => item.orderId === detailRef.id);
            if (!order) { detailRef = null; q('#wm-work-detail').close(); markup(q('#wm-detail-body'), ''); return; }
            html = window.GaoDeliveryWall.render({ ...data.delivery, orders: [order] }, connected, now());
        } else {
            const lane = lanes(detailRef.module).find(item => item.id === detailRef.id);
            if (lane) html = station(detailRef.module, lane);
        }
        markup(q('#wm-detail-body'), html || '<p class="wm-empty">Công việc không còn trong dữ liệu giám sát hiện tại.</p>');
        q('#wm-detail-title').textContent = labels[detailRef.module];
        q('#wm-detail-body').querySelectorAll('[role=button]').forEach(card => { card.removeAttribute('role'); card.removeAttribute('tabindex'); card.removeAttribute('aria-pressed'); });
    }
    function openDetail(card) {
        const module = card.closest('.wm-area').id.replace('wm-', '');
        detailRef = { module, id: module === 'delivery' ? Number(card.dataset.deliveryId) : card.dataset.key.slice(module.length + 1) };
        detailTrigger = card;
        renderDetail(); if (!detailRef) return;
        q('#wm-work-detail').showModal(); syncScenes(); nextCycle = Date.now() + 10000;
    }
    function render() {
        nextRender = Infinity;
        root.dataset.layout = wallLayout() ? 'wall' : 'detail';
        root.dataset.deliveryActive = String(Number(data.delivery?.activeCount || 0) > 0);
        const counts = Object.keys(labels).map(key => ({ key, count: lanes(key).length }));
        root.dataset.compactIdle = String(root.dataset.deliveryActive === 'true' && counts.filter(x => !['pos', 'delivery'].includes(x.key)).every(x => x.count === 0));
        root.dataset.density = counts.reduce((total, item) => total + item.count, 0) > 8 || counts.some(item => item.key !== 'pos' && item.count > 1) ? 'compact' : 'comfortable';
        Object.keys(labels).filter(key => key !== 'delivery').forEach(renderCard);
        const deliveryRows = window.GaoDeliveryWall.visibleOrders(data.delivery, now());
        const deliveryPage = pageOf('delivery', deliveryRows);
        markup(q('#wm-delivery'), window.GaoDeliveryWall.render(data.delivery, connected, now(), { rows: deliveryPage.rows, pager: pager('delivery', deliveryPage.page, deliveryRows.length), compact: wallLayout() }));
        for (const card of data.delivery?.orders || []) {
            const end = new Date(typeof card.updatedAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(card.updatedAtUtc) ? card.updatedAtUtc + 'Z' : card.updatedAtUtc).getTime() + 12000;
            if (end > now()) nextRender = Math.min(nextRender, end);
            const expiry = window.GaoDeliveryWall.expiresAt(card, data.delivery);
            if (expiry > now()) nextRender = Math.min(nextRender, expiry);
        }
        const users = new Set(data.people.map(p => p.userId));
        const workers = new Set(data.people.filter(isEditing).map(p => p.userId));
        const counters = new Set(data.people.filter(p => p.module === 'pos').map(p => p.terminal || p.userId));
        markup(q('#wm-summary'), `<div class="wm-summary-item"><span class="wm-summary-icon">◉</span><strong>${connected ? workers.size : '—'}</strong><div>Nhân viên đang thao tác<small>${connected ? users.size : '—'} nhân viên đang kết nối</small></div></div><div class="wm-summary-item"><span class="wm-summary-icon">▥</span><strong>${connected ? data.people.length : '—'}</strong><div>Công việc đang mở<small>${connected ? counters.size : '—'} quầy POS · các phiếu hiện riêng</small></div></div><div class="wm-summary-item"><span class="wm-summary-icon">↗</span><strong>${data.events.length}</strong><div>Cập nhật đã lưu<small>Phiên từ ${data.startedAtUtc ? time(data.startedAtUtc) : '—'} · tối đa 120</small></div></div>`);
        const filteredEvents = selectedUser === null ? data.events : data.events.filter(e => e.userId === selectedUser);
        markup(q('#wm-feed'), filteredEvents.slice(0, 24).map(e => `<li class="wm-event" data-key="event-${e.id}" data-event-id="${e.id}"><div class="wm-event-category">${esc(labels[e.module])}<span>${esc(e.terminal || '')}</span></div><strong>${esc(e.person)}</strong><p>${esc(e.text)}${e.document ? ' · <b>' + esc(e.document) + '</b>' : ''}</p>${e.documentTitle ? `<div class="wm-event-document-title">${esc(e.documentTitle)}</div>` : ''}${e.detail ? `<div class="wm-event-detail">${esc(e.detail)}</div>` : ''}<div class="wm-task-time">${stamp(e.occurredAtUtc)}</div></li>`).join('') || '<li class="wm-empty">Chưa có cập nhật đã lưu trong phiên này.</li>');
        q('#wm-selection').hidden = selectedUser === null;
        q('#wm-selection-name').textContent = data.people.find(p => p.userId === selectedUser)?.person || data.events.find(e => e.userId === selectedUser)?.person || '';
        const teams = new Map();
        data.people.forEach(p => { const team = teams.get(p.userId) || { person: p.person, places: new Set() }; team.places.add((p.terminal ? p.terminal + ' · ' : '') + labels[p.module]); teams.set(p.userId, team); });
        markup(q('#wm-team'), [...teams].map(([id, p]) => `<div class="wm-team-person" data-key="person-${id}"><span class="wm-avatar">${esc(p.person.slice(0, 1))}</span><div><strong>${esc(p.person)}</strong><small>${esc([...p.places].join(' / '))}</small></div></div>`).join('') || '<p class="wm-empty">Chờ nhân viên mở màn hình làm việc.</p>');
        q('#wm-attention').textContent = connected ? 'Mốc giờ và nội dung lấy từ thao tác đã lưu. Tương tác màn hình được hiển thị riêng.' : 'Mất tín hiệu. Nội dung là lần cập nhật cuối; hoạt động hiện tại có thể đã thay đổi.';
        const event = data.events[0];
        q('#wm-arrival-text').textContent = event ? `${event.terminal ? event.terminal + ' · ' : ''}${event.person} ${event.text}${event.document ? ' · ' + event.document : ''}${event.documentTitle ? ' · ' + event.documentTitle : ''}` : 'Đang nhận tín hiệu · chờ nhân viên cập nhật công việc.';
        q('#wm-arrival-time').textContent = event ? time(event.occurredAtUtc) : 'TRỰC TIẾP'; renderDetail(); design(); syncScenes();
    }
    function syncScenes() {
        for (const visual of observed) if (!root.contains(visual)) { sceneResize.unobserve(visual); observed.delete(visual); }
        root.querySelectorAll('.wm-visual[data-fit]').forEach(visual => {
            if (!observed.has(visual)) { sceneResize.observe(visual); observed.add(visual); }
        });
        const keys = new Set(Object.keys(initials).flatMap(key => lanes(key).map(lane => key + '|' + lane.id)).concat(window.GaoDeliveryWall.visibleOrders(data.delivery, now()).map(order => 'delivery-' + order.orderId)));
        q('.wm-operations').querySelectorAll('.wm-station[data-effect-signature], .wd-card[data-effect-signature]').forEach(card => {
            const key = card.dataset.key, signature = card.dataset.effectSignature;
            keys.add(key);
            if (signature && signature !== effectSignatures.get(key) && root.dataset.paused !== 'true') {
                card.querySelector('.wm-scene, .wd-picture')?.getAnimations({ subtree: true }).forEach(animation => { animation.currentTime = 0; animation.play(); });
            }
            effectSignatures.set(key, signature);
        });
        for (const key of effectSignatures.keys()) if (!keys.has(key)) effectSignatures.delete(key);
    }
    function flash(event) {
        if (!effectsEnabled() || !connected || document.hidden || seconds(event.occurredAtUtc) > 15) return;
        root.querySelectorAll(`[data-event-id="${event.id}"]`).forEach(item => {
            item.classList.remove('wm-new'); void item.offsetWidth; item.classList.add('wm-new');
            // Replay the brief check only for this newly saved work; keep other animation nodes running.
            item.querySelector('.wm-scene[data-mode=saved] .wm-save-confirm')?.getAnimations().forEach(animation => { animation.currentTime = 0; animation.play(); });
            clearTimeout(flashTimers.get(item));
            flashTimers.set(item, setTimeout(() => { item.classList.remove('wm-new'); flashTimers.delete(item); }, 2500));
        });
        const banner = q('#wm-arrival'); banner.classList.remove('wm-new'); void banner.offsetWidth; banner.classList.add('wm-new');
    }
    function receive(payload, snapshot) {
        lastSignal = Date.now(); clockOffset = new Date(typeof payload.serverTimeUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(payload.serverTimeUtc) ? payload.serverTimeUtc + 'Z' : payload.serverTimeUtc).getTime() - Date.now(); connected = true; retryDelay = 2000;
        q('#wm-clock').textContent = time(now());
        q('#wm-connection').textContent = 'ĐANG NHẬN TÍN HIỆU';
        if (snapshot) {
            const previous = seenEvents; data = payload;
            seenEvents = new Set(data.events.map(e => String(e.id))); render();
            window.dispatchEvent(new CustomEvent('gao:monitor-snapshot', { detail: data }));
            if (previous !== null) data.events.filter(e => !previous.has(String(e.id))).slice(0, 8).forEach(flash);
        } else design();
    }
    function disconnected(message) { connected = false; q('#wm-connection').textContent = message; render(); }
    function revoked() {
        stopped = true; source?.close(); clearTimeout(retryTimer); retryTimer = null;
        data = { people: [], events: [], delivery: null }; selectedUser = null; seenEvents = null;
        detailRef = null; detailTrigger = null; q('#wm-work-detail').close(); markup(q('#wm-detail-body'), ''); pages.clear();
        disconnected('PHIÊN ĐĂNG NHẬP ĐÃ THAY ĐỔI');
        window.dispatchEvent(new Event('gao:monitor-revoked'));
    }
    async function retry() {
        if (stopped || retryTimer || !navigator.onLine) return;
        try {
            const response = await fetch(root.dataset.streamUrl.replace(/\/stream$/, '/snapshot'), { credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if ([401, 403].includes(response.status)) { revoked(); return; }
        } catch (_) { }
        if (stopped) return;
        retryTimer = setTimeout(() => { retryTimer = null; connect(); }, retryDelay); retryDelay = Math.min(15000, retryDelay * 2);
    }
    function connect() {
        if (stopped) return;
        source?.close(); source = new EventSource(root.dataset.streamUrl);
        source.addEventListener('snapshot', event => { try { receive(JSON.parse(event.data), true); } catch (_) { disconnected('KHÔNG ĐỌC ĐƯỢC TÍN HIỆU'); } });
        source.addEventListener('heartbeat', event => { try { receive(JSON.parse(event.data), false); } catch (_) { } });
        source.addEventListener('revoked', revoked);
        source.onerror = () => { source.close(); disconnected('MẤT KẾT NỐI · ĐANG THỬ LẠI'); retry(); };
    }
    q('#wm-theme').addEventListener('click', () => { theme = theme === 'dark' ? 'light' : 'dark'; design(); try { localStorage.setItem('gao.monitor.theme.v2', theme); } catch (_) { } });
    q('#wm-motion').addEventListener('click', () => { motion = !effectsEnabled(); motionOverride = motion && reduced.matches; design(); try { localStorage.setItem('gao.monitor.motion', motion ? 'on' : 'off'); } catch (_) { } });
    q('#wm-layout').addEventListener('click', () => { fitLayout = !fitLayout; nextCycle = Date.now() + 10000; render(); try { localStorage.setItem('gao.monitor.layout', fitLayout ? 'wall' : 'detail'); } catch (_) { } });
    q('#wm-cycle').addEventListener('click', () => { cycle = !cycling(); cycleOverride = cycle; nextCycle = Date.now() + 10000; design(); try { localStorage.setItem('gao.monitor.cycle', cycle ? 'on' : 'off'); } catch (_) { } });
    q('#wm-detail-close').addEventListener('click', () => q('#wm-work-detail').close());
    q('#wm-work-detail').addEventListener('close', () => { detailRef = null; markup(q('#wm-detail-body'), ''); detailTrigger?.focus(); detailTrigger = null; nextCycle = Date.now() + 10000; });
    q('.wm-operations').addEventListener('click', event => {
        const pageButton = event.target.closest('[data-page-area]');
        if (pageButton) {
            const page = pages.get(pageButton.dataset.pageArea);
            if (page) { page.index = (page.index + Number(pageButton.dataset.pageStep) + page.count) % page.count; nextCycle = Date.now() + 10000; render(); }
            return;
        }
        const card = event.target.closest('.wm-station[data-user-id], .wd-card[data-delivery-id]');
        if (card) { if (wallLayout() || card.dataset.deliveryId) openDetail(card); else { selectedUser = Number(card.dataset.userId); render(); } }
    });
    q('.wm-operations').addEventListener('keydown', event => {
        if (event.key === 'Enter' || event.key === ' ') {
            const card = event.target.closest('.wm-station[data-user-id], .wd-card[data-delivery-id]');
            if (card) { event.preventDefault(); if (wallLayout() || card.dataset.deliveryId) openDetail(card); else { selectedUser = Number(card.dataset.userId); render(); } }
        }
    });
    q('#wm-show-all').addEventListener('click', () => { selectedUser = null; render(); });
    q('#wm-fullscreen').hidden = !document.fullscreenEnabled;
    q('#wm-fullscreen').addEventListener('click', async () => { try { if (document.fullscreenElement) await document.exitFullscreen(); else await root.requestFullscreen(); } catch (_) { q('#wm-sync').textContent = 'Có thể dùng F11 để mở toàn màn hình.'; } });
    document.addEventListener('fullscreenchange', () => { q('#wm-fullscreen').textContent = document.fullscreenElement ? 'Thoát toàn màn hình' : 'Toàn màn hình'; });
    reduced.addEventListener('change', design);
    let resizeFrame;
    window.addEventListener('resize', () => { cancelAnimationFrame(resizeFrame); resizeFrame = requestAnimationFrame(() => { nextCycle = Date.now() + 10000; render(); }); });
    document.addEventListener('visibilitychange', () => { render(); });
    window.addEventListener('gao:monitor-view', render);
    window.addEventListener('pagehide', () => { source?.close(); clearTimeout(retryTimer); retryTimer = null; sceneResize.disconnect(); observed.clear(); flashTimers.forEach(clearTimeout); flashTimers.clear(); });
    window.addEventListener('pageshow', event => { if (event.persisted) { root.querySelectorAll('.wm-visual[data-fit]').forEach(visual => sceneResize.observe(visual)); connect(); } });
    window.addEventListener('offline', () => { source?.close(); clearTimeout(retryTimer); retryTimer = null; disconnected('MẤT KẾT NỐI · CHỜ MẠNG TRỞ LẠI'); });
    window.addEventListener('online', () => { if (!stopped) connect(); });
    setInterval(() => {
        q('#wm-clock').textContent = time(now());
        root.querySelectorAll('.wm-age[data-at]').forEach(el => { el.textContent = duration(el.dataset.at) + ' trước'; });
        root.querySelectorAll('.wm-opened[data-at]').forEach(el => { el.textContent = duration(el.dataset.at); });
        root.querySelectorAll('.wm-trip-elapsed[data-at]').forEach(el => { el.textContent = 'Đã đi ' + window.GaoDeliveryWall.elapsed(el.dataset.at, now()); });
        if (now() >= nextRender) render();
        if (Date.now() >= nextCycle) {
            nextCycle = Date.now() + 10000;
            if (wallLayout() && cycling() && connected && !document.hidden && !q('#wm-work-detail').open) {
                let changed = false;
                pages.forEach((page, key) => {
                    if (page.count > 1 && !q('#wm-' + key).matches(':hover, :focus-within')) { page.index = (page.index + 1) % page.count; changed = true; }
                });
                if (changed) render();
            }
        }
        if (connected && Date.now() - lastSignal > 15000) { source?.close(); disconnected('TÍN HIỆU ĐÃ GIÁN ĐOẠN'); retry(); }
        q('#wm-sync').textContent = connected ? 'Tín hiệu cập nhật ' + Math.floor((Date.now() - lastSignal) / 1000) + ' giây trước' : lastSignal ? 'Lần cuối nhận tín hiệu: ' + time(lastSignal + clockOffset) : 'Chưa nhận được tín hiệu cửa hàng';
    }, 1000);
    render(); connect();
})();
