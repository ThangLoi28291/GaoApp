(function () {
    'use strict';
    const root = document.getElementById('gao-store-wall');
    if (!root) return;
    const q = selector => root.querySelector(selector);
    const esc = value => String(value ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
    const labels = { pos: 'Quầy tính tiền', receipt: 'Nhập hàng', warehouse: 'Kiểm kê · Chuyển kho', label: 'In tem sản phẩm', review: 'Duyệt phiếu' };
    const initials = { pos: '01', receipt: '02', warehouse: '03', label: '04', review: '05' };
    const captions = { receipt: 'Đang nhập dữ liệu phiếu nhập', pos: 'Đang thao tác tại quầy', warehouse: 'Đang thao tác chứng từ kho', label: 'Đang thao tác phiếu in tem', review: 'Đang thao tác duyệt phiếu' };
    const art = {
        receipt: '<div class="wm-data-packets"><i></i><i></i><i></i></div><div class="wm-paper-art"><i></i><i></i><i></i></div><i class="wm-pen-art"></i>',
        pos: '<div class="wm-scan-corners"><i class="wm-barcode-art"></i><i class="wm-scanner-beam"></i></div>',
        warehouse: '<div class="wm-conveyor"></div><div class="wm-cargo"><i class="wm-package-art"></i><i class="wm-package-art"></i><i class="wm-package-art"></i></div>',
        label: '<div class="wm-printer-art"><i class="wm-printer-light"></i></div><div class="wm-label-art"></div>',
        review: '<div class="wm-paper-art wm-review-paper"><i></i><i></i><i></i></div><span class="wm-review-stamp">✓</span>'
    };
    let data = { people: [], events: [] }, source, retryTimer, retryDelay = 2000, connected = false, stopped = false, lastSignal = 0, clockOffset = 0, highestEvent = null, nextRender = Infinity;
    let motion = true, theme = 'light';
    // v2 starts light even when the former wallboard saved its dark default.
    try { motion = localStorage.getItem('gao.monitor.motion') !== 'off'; theme = localStorage.getItem('gao.monitor.theme.v2') === 'dark' ? 'dark' : 'light'; } catch (_) { }
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const now = () => Date.now() + clockOffset;
    const time = value => new Date(value).toLocaleTimeString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit', second: '2-digit' });
    const seconds = value => Math.max(0, Math.floor((now() - new Date(value).getTime()) / 1000));
    function duration(value) {
        const s = seconds(value);
        return s < 60 ? `${s} giây` : s < 3600 ? `${Math.floor(s / 60)} phút ${s % 60} giây` : `${Math.floor(s / 3600)} giờ ${Math.floor(s % 3600 / 60)} phút`;
    }
    function stamp(value) { return `<time datetime="${esc(value)}">${time(value)}</time><span class="wm-age" data-at="${esc(value)}">${duration(value)} trước</span>`; }
    function design() {
        root.dataset.theme = theme; root.dataset.paused = String(!connected || !motion || reduced.matches); root.dataset.motion = String(motion);
        document.documentElement.style.colorScheme = theme;
        q('#wm-theme').textContent = theme === 'dark' ? 'Nền sáng' : 'Nền tối';
        q('#wm-motion').setAttribute('aria-pressed', String(motion));
        q('#wm-motion').textContent = motion ? 'Hiệu ứng: bật' : 'Hiệu ứng: tắt';
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
        [...oldNode.attributes].forEach(a => { if (!newNode.hasAttribute(a.name)) oldNode.removeAttribute(a.name); });
        [...newNode.attributes].forEach(a => { if (oldNode.getAttribute(a.name) !== a.value) oldNode.setAttribute(a.name, a.value); });
        children(oldNode, newNode);
    }
    function markup(element, html) { const template = document.createElement('div'); template.innerHTML = html; children(element, template); }
    function badge(state, text) { return `<span class="wm-badge" data-status="${state}"><i class="wm-dot"></i>${esc(text)}</span>`; }
    function scene(key, mode, caption) {
        return `<div class="wm-scene" data-mode="${mode}" data-active="${mode === 'working' || mode === 'saved'}"><div class="wm-visual" aria-hidden="true"><div class="wm-art" data-art="${key}">${art[key]}<span class="wm-save-confirm">✓</span></div></div><div class="wm-scene-caption"><span>${esc(caption)}</span><small>${mode === 'working' ? 'Tín hiệu thao tác trực tiếp' : mode === 'saved' ? 'Thao tác đã lưu thành công' : 'Chờ thao tác tiếp theo'}</small></div></div>`;
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
        return [...map.values()].sort((a, b) => (key === 'pos' ? 0 : Number(b.people.length > 0) - Number(a.people.length > 0)) || a.id.localeCompare(b.id, 'vi', { numeric: true }));
    }
    function station(key, lane) {
        const p = lane.people.find(person => person.state === 'editing') || lane.people[0], e = lane.event;
        const hasEditing = connected && lane.people.some(person => person.state === 'editing');
        const recent = connected && e && seconds(e.occurredAtUtc) < 12;
        const editedAfterSave = e && lane.people.some(person => person.state === 'editing' && person.editedAtUtc && new Date(person.editedAtUtc) > new Date(e.occurredAtUtc));
        // A completed save supersedes earlier typing; only a newer interaction resumes the work animation.
        const editing = hasEditing && (!e || editedAfterSave);
        const saved = recent && !editing;
        const state = !connected ? 'stale' : saved ? 'saved' : editing ? 'working' : p ? 'viewing' : 'saved';
        if (recent) nextRender = Math.min(nextRender, new Date(e.occurredAtUtc).getTime() + 12000);
        const person = p ? [...new Set(lane.people.map(item => item.person))].join(' · ') : e?.person || 'Nhân viên';
        const terminal = p?.terminal || e?.terminal || 'Tại cửa hàng';
        const title = e ? e.text.replace(/^vừa /, '').replace(/^./, c => c.toLocaleUpperCase('vi')) : editing ? captions[key] : 'Đang mở màn hình';
        const document = p?.document || e?.document;
        const activity = `<div class="wm-task-title">${esc(title)}</div>${document ? `<div class="wm-task-document">${esc(document)}</div>` : ''}${e?.detail ? `<div class="wm-task-detail">${esc(e.detail)}</div>` : ''}${p?.document && p.document !== e?.document && e?.document?.startsWith('Lệnh in') ? `<div class="wm-task-detail">${esc(e.document)}</div>` : ''}${!e ? '<div class="wm-task-detail">Chưa có lần lưu mới trong phiên giám sát.</div>' : ''}`;
        const clock = e ? `<div class="wm-task-time">${stamp(e.occurredAtUtc)}</div>${p && e.userId !== p.userId ? `<small class="wm-event-author">Lần lưu của ${esc(e.person)}</small>` : ''}` : '';
        const opened = p ? `<div class="wm-presence-time">${p.openedAtUtc ? `Mở màn hình lúc ${time(p.openedAtUtc)} · <span class="wm-opened" data-at="${esc(p.openedAtUtc)}">${duration(p.openedAtUtc)}</span>` : 'Màn hình đang mở'}</div>` : '<div class="wm-presence-time">Cập nhật đã lưu · không có tín hiệu đang thao tác</div>';
        const status = !connected ? 'Tín hiệu cũ' : saved ? 'Vừa lưu xong' : editing ? 'Đang thao tác' : p ? 'Đang mở' : 'Đã lưu';
        const caption = saved ? 'Đã lưu thành công' : editing ? captions[key] : p ? 'Chờ thao tác tiếp theo' : 'Lần lưu gần nhất';
        const heading = key === 'pos' ? `<strong>${esc(terminal)}</strong><span class="wm-terminal">${esc(person)}</span>` : `<strong>${esc(person)}</strong><span class="wm-terminal">${esc(terminal)}</span>`;
        return `<section class="wm-station${p ? ' wm-live-person' : ''}" data-key="${esc(key + '|' + lane.id)}" data-terminal="${esc(terminal)}" data-work-key="${esc(p?.workKey || e?.workKey || '')}" data-status="${state}" data-event-id="${e?.id || ''}"><div class="wm-station-heading"><div class="wm-person"><span class="wm-avatar">${esc(person.slice(0, 1))}</span><div>${heading}</div></div>${badge(state, status)}</div><div class="wm-station-work">${scene(key, saved ? 'saved' : editing ? 'working' : 'idle', caption)}<div class="wm-station-content">${activity}</div></div>${clock}${opened}</section>`;
    }
    function renderCard(key) {
        const items = lanes(key), people = data.people.filter(p => p.module === key);
        const working = connected ? people.filter(p => p.state === 'editing').length : 0;
        const recent = data.events.find(e => e.module === key), justSaved = connected && recent && seconds(recent.occurredAtUtc) < 12;
        const state = !connected ? 'stale' : working ? 'working' : justSaved ? 'saved' : people.length ? 'viewing' : 'idle';
        const title = `<div class="wm-area-heading"><div class="wm-area-title"><span class="wm-area-number">${initials[key]}</span><h2>${labels[key]}</h2></div>${badge(state, !connected ? 'Tín hiệu cũ' : working ? `${working} đang thao tác · ${items.length} việc` : people.length ? `${people.length} đang mở · ${items.length} việc` : recent ? `${items.length} việc đã lưu` : 'Sẵn sàng')}</div>`;
        const area = q('#wm-' + key);
        const empty = `<div class="wm-area-empty">${scene(key, 'idle', 'Chưa có thao tác mới')}<p>Nhân viên và công việc sẽ xuất hiện tại đây.</p></div>`;
        markup(area, title + (items.length ? `<div class="wm-stations">${items.map(l => station(key, l)).join('')}</div>` : empty));
        area.dataset.state = state;
        area.dataset.multiple = String(items.length > 1);
    }
    function render() {
        nextRender = Infinity;
        const counts = Object.keys(labels).map(key => ({ key, count: lanes(key).length }));
        root.dataset.density = counts.reduce((total, item) => total + item.count, 0) > 8 || counts.some(item => item.key !== 'pos' && item.count > 1) ? 'compact' : 'comfortable';
        Object.keys(labels).forEach(renderCard);
        markup(q('#wm-delivery'), `<div class="wm-area-heading"><div class="wm-area-title"><span class="wm-area-number">06</span><h2>Giao hàng</h2></div>${badge('idle', 'Chưa kết nối')}</div><div class="wm-delivery-placeholder" data-active="false"><span class="wm-delivery-icon" aria-hidden="true">↗</span><div><strong>Giao hàng sẽ được kết nối ở bước sau</strong><p>Chờ nguồn trạng thái đơn giao của nhân viên.</p></div></div>`);
        const users = new Set(data.people.map(p => p.userId));
        const workers = new Set(data.people.filter(p => p.state === 'editing').map(p => p.userId));
        const counters = new Set(data.people.filter(p => p.module === 'pos').map(p => p.terminal || p.userId));
        markup(q('#wm-summary'), `<div class="wm-summary-item"><span class="wm-summary-icon">◉</span><strong>${connected ? workers.size : '—'}</strong><div>Nhân viên đang thao tác<small>${connected ? users.size : '—'} nhân viên đang kết nối</small></div></div><div class="wm-summary-item"><span class="wm-summary-icon">▥</span><strong>${connected ? data.people.length : '—'}</strong><div>Công việc đang mở<small>${connected ? counters.size : '—'} quầy POS · các phiếu hiện riêng</small></div></div><div class="wm-summary-item"><span class="wm-summary-icon">↗</span><strong>${data.events.length}</strong><div>Cập nhật đã lưu<small>Phiên từ ${data.startedAtUtc ? time(data.startedAtUtc) : '—'} · tối đa 120</small></div></div>`);
        markup(q('#wm-feed'), data.events.slice(0, 24).map(e => `<li class="wm-event" data-key="event-${e.id}" data-event-id="${e.id}"><div class="wm-event-category">${esc(labels[e.module])}<span>${esc(e.terminal || '')}</span></div><strong>${esc(e.person)}</strong><p>${esc(e.text)}${e.document ? ' · <b>' + esc(e.document) + '</b>' : ''}</p>${e.detail ? `<div class="wm-event-detail">${esc(e.detail)}</div>` : ''}<div class="wm-task-time">${stamp(e.occurredAtUtc)}</div></li>`).join('') || '<li class="wm-empty">Chưa có cập nhật đã lưu trong phiên này.</li>');
        const teams = new Map();
        data.people.forEach(p => { const team = teams.get(p.userId) || { person: p.person, places: new Set() }; team.places.add((p.terminal ? p.terminal + ' · ' : '') + labels[p.module]); teams.set(p.userId, team); });
        markup(q('#wm-team'), [...teams].map(([id, p]) => `<div class="wm-team-person" data-key="person-${id}"><span class="wm-avatar">${esc(p.person.slice(0, 1))}</span><div><strong>${esc(p.person)}</strong><small>${esc([...p.places].join(' / '))}</small></div></div>`).join('') || '<p class="wm-empty">Chờ nhân viên mở màn hình làm việc.</p>');
        q('#wm-attention').textContent = connected ? 'Mốc giờ và nội dung lấy từ thao tác đã lưu. Tương tác màn hình được hiển thị riêng.' : 'Mất tín hiệu. Nội dung là lần cập nhật cuối; hoạt động hiện tại có thể đã thay đổi.';
        const event = data.events[0];
        q('#wm-arrival-text').textContent = event ? `${event.terminal ? event.terminal + ' · ' : ''}${event.person} ${event.text}${event.document ? ' · ' + event.document : ''}` : 'Đang nhận tín hiệu · chờ nhân viên cập nhật công việc.';
        q('#wm-arrival-time').textContent = event ? time(event.occurredAtUtc) : 'TRỰC TIẾP'; design();
    }
    function flash(event) {
        if (!motion || reduced.matches || !connected || seconds(event.occurredAtUtc) > 15) return;
        root.querySelectorAll(`[data-event-id="${event.id}"]`).forEach(item => {
            item.classList.remove('wm-new'); void item.offsetWidth; item.classList.add('wm-new');
            // Replay the brief check only for this newly saved work; keep other animation nodes running.
            item.querySelector('.wm-scene[data-mode=saved] .wm-save-confirm')?.getAnimations().forEach(animation => { animation.currentTime = 0; animation.play(); });
            setTimeout(() => item.classList.remove('wm-new'), 2500);
        });
        const banner = q('#wm-arrival'); banner.classList.remove('wm-new'); void banner.offsetWidth; banner.classList.add('wm-new');
    }
    function receive(payload, snapshot) {
        lastSignal = Date.now(); clockOffset = new Date(payload.serverTimeUtc).getTime() - Date.now(); connected = true; retryDelay = 2000;
        q('#wm-clock').textContent = time(now());
        q('#wm-connection').textContent = 'ĐANG NHẬN TÍN HIỆU';
        if (snapshot) {
            const previous = highestEvent; data = payload; highestEvent = data.events[0]?.id || 0; render();
            if (previous !== null) data.events.filter(e => e.id > previous).slice(0, 8).forEach(flash);
        } else design();
    }
    function disconnected(message) { connected = false; q('#wm-connection').textContent = message; render(); }
    async function retry() {
        if (stopped || retryTimer || !navigator.onLine) return;
        try {
            const response = await fetch(root.dataset.streamUrl.replace(/\/stream$/, '/snapshot'), { credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if ([401, 403].includes(response.status)) { stopped = true; disconnected('PHIÊN ĐĂNG NHẬP ĐÃ THAY ĐỔI'); return; }
        } catch (_) { }
        if (stopped) return;
        retryTimer = setTimeout(() => { retryTimer = null; connect(); }, retryDelay); retryDelay = Math.min(15000, retryDelay * 2);
    }
    function connect() {
        if (stopped) return;
        source?.close(); source = new EventSource(root.dataset.streamUrl);
        source.addEventListener('snapshot', event => { try { receive(JSON.parse(event.data), true); } catch (_) { disconnected('KHÔNG ĐỌC ĐƯỢC TÍN HIỆU'); } });
        source.addEventListener('heartbeat', event => { try { receive(JSON.parse(event.data), false); } catch (_) { } });
        source.addEventListener('revoked', () => { stopped = true; source.close(); disconnected('PHIÊN ĐĂNG NHẬP ĐÃ THAY ĐỔI'); });
        source.onerror = () => { source.close(); disconnected('MẤT KẾT NỐI · ĐANG THỬ LẠI'); retry(); };
    }
    q('#wm-theme').addEventListener('click', () => { theme = theme === 'dark' ? 'light' : 'dark'; design(); try { localStorage.setItem('gao.monitor.theme.v2', theme); } catch (_) { } });
    q('#wm-motion').addEventListener('click', () => { motion = !motion; design(); try { localStorage.setItem('gao.monitor.motion', motion ? 'on' : 'off'); } catch (_) { } });
    q('#wm-fullscreen').hidden = !document.fullscreenEnabled;
    q('#wm-fullscreen').addEventListener('click', async () => { try { if (document.fullscreenElement) await document.exitFullscreen(); else await root.requestFullscreen(); } catch (_) { q('#wm-sync').textContent = 'Có thể dùng F11 để mở toàn màn hình.'; } });
    document.addEventListener('fullscreenchange', () => { q('#wm-fullscreen').textContent = document.fullscreenElement ? 'Thoát toàn màn hình' : 'Toàn màn hình'; });
    reduced.addEventListener('change', design);
    window.addEventListener('pagehide', () => { source?.close(); clearTimeout(retryTimer); retryTimer = null; });
    window.addEventListener('pageshow', event => { if (event.persisted) connect(); });
    window.addEventListener('offline', () => { source?.close(); clearTimeout(retryTimer); retryTimer = null; disconnected('MẤT KẾT NỐI · CHỜ MẠNG TRỞ LẠI'); });
    window.addEventListener('online', () => { if (!stopped) connect(); });
    setInterval(() => {
        q('#wm-clock').textContent = time(now());
        root.querySelectorAll('.wm-age[data-at]').forEach(el => { el.textContent = duration(el.dataset.at) + ' trước'; });
        root.querySelectorAll('.wm-opened[data-at]').forEach(el => { el.textContent = duration(el.dataset.at); });
        if (now() >= nextRender) render();
        if (connected && Date.now() - lastSignal > 15000) { source?.close(); disconnected('TÍN HIỆU ĐÃ GIÁN ĐOẠN'); retry(); }
        q('#wm-sync').textContent = connected ? 'Tín hiệu cập nhật ' + Math.floor((Date.now() - lastSignal) / 1000) + ' giây trước' : lastSignal ? 'Lần cuối nhận tín hiệu: ' + time(lastSignal + clockOffset) : 'Chưa nhận được tín hiệu cửa hàng';
    }, 1000);
    render(); connect();
})();
