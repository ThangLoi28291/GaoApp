(() => {
    'use strict';
    const root = document.getElementById('menuVisibility');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const subjects = [...root.querySelectorAll('.mv-subject')];
    let mode = 'role', state = null, hidden = new Set(), reset = false, dirty = false, busy = false, sequence = 0;
    const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const normalize = value => String(value).normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').toLowerCase();
    function message(text, error = false) { $('mvMessage').textContent = text; $('mvMessage').hidden = !text; $('mvMessage').classList.toggle('error', error); }
    function controls() {
        ['mvShowAll', 'mvHideAll', 'mvReset', 'mvReload'].forEach(id => $(id).disabled = busy || !state);
        $('mvSave').disabled = busy || !state || !dirty;
        root.querySelector('.mv-editor').setAttribute('aria-busy', String(busy));
        root.querySelectorAll('.mv-tree input').forEach(input => input.disabled = busy || input.dataset.available !== 'true');
        $('mvSaveStatus').textContent = busy ? 'Đang xử lý…' : dirty ? 'Có thay đổi chưa lưu' : state ? 'Đã đồng bộ cấu hình hiển thị' : 'Các thay đổi chỉ áp dụng sau khi lưu.';
    }
    function changed() {
        dirty = !!state && (reset || JSON.stringify([...hidden].sort((a, b) => a - b)) !== JSON.stringify([...state.hiddenIds].sort((a, b) => a - b)));
        controls();
    }
    function filterSubjects() {
        const q = normalize($('mvSubjectSearch').value);
        subjects.forEach(s => s.hidden = s.dataset.type !== mode || !normalize(s.dataset.search).includes(q));
        $('mvNoSubjects').hidden = subjects.some(s => !s.hidden);
    }
    function descendants(id, seen = new Set()) {
        if (seen.has(id)) return [];
        seen.add(id);
        return [id, ...state.menus.filter(n => n.parentId === id).flatMap(n => descendants(n.id, seen))];
    }
    function available(node, seen = new Set()) {
        if (!node || !node.available || seen.has(node.id)) return false;
        seen.add(node.id);
        return node.parentId == null || available(state.menus.find(n => n.id === node.parentId), seen);
    }
    function visibleIds() {
        const ids = new Set(), visited = new Set();
        function visit(node) {
            if (visited.has(node.id) || !node.available || hidden.has(node.id)) return false;
            visited.add(node.id);
            let children = false;
            state.menus.filter(n => n.parentId === node.id).forEach(n => { children = visit(n) || children; });
            if (node.isFolder && !children) return false;
            ids.add(node.id); return true;
        }
        state.menus.filter(n => n.parentId == null).forEach(visit);
        return ids;
    }
    function render() {
        if (!state) return;
        const q = normalize($('mvMenuSearch').value), shown = visibleIds(), byId = new Map(state.menus.map(n => [n.id, n]));
        $('mvTargetName').textContent = state.name;
        $('mvTargetDetail').textContent = state.type === 'role' ? `${state.memberCount} nhân viên trong nhóm · ${state.personalCount} người có menu riêng` : `Thuộc nhóm ${state.detail}`;
        $('mvMode').hidden = false;
        $('mvMode').textContent = state.type === 'role' ? 'Theo nhóm' : (reset || (state.inherited && !dirty)) ? 'Theo nhóm' : 'Cá nhân';
        $('mvImpact').textContent = state.type === 'role' ? `Áp dụng cho ${state.memberCount - state.personalCount} nhân viên đang dùng menu nhóm. ${state.personalCount} cấu hình riêng được giữ nguyên.` : reset || (state.inherited && !dirty) ? 'Nhân viên này dùng menu của nhóm. Chỉnh các mục bên dưới để tạo cấu hình riêng.' : 'Chỉ áp dụng cho nhân viên này. Các thành viên khác giữ nguyên menu.';
        $('mvReset').textContent = state.type === 'role' ? 'Khôi phục mặc định' : 'Dùng menu của nhóm';
        $('mvPreviewName').textContent = state.name;
        const visited = new Set();
        function branch(node, depth) {
            if (visited.has(node.id)) return '';
            visited.add(node.id);
            const children = state.menus.filter(n => n.parentId === node.id);
            if (q && !descendants(node.id).some(id => normalize(byId.get(id)?.title || '').includes(q))) return '';
            const allowed = available(node), checked = allowed && !hidden.has(node.id);
            const reason = !allowed ? node.unavailableReason || 'Menu cha không khả dụng' : hidden.has(node.id) ? 'Đang ẩn' : 'Cho phép hiển thị';
            return `<div class="mv-branch"><label class="mv-row ${children.length ? 'mv-parent' : ''} ${!allowed ? 'mv-unavailable' : ''}" style="--depth:${Math.min(depth, 5)}"><input type="checkbox" data-menu-id="${node.id}" data-available="${allowed}" ${checked ? 'checked' : ''} ${!allowed || busy ? 'disabled' : ''}><span class="mv-menu-icon"><i class="${escape(node.icon || 'bx bx-folder')}"></i></span><span class="mv-row-name"><strong>${escape(node.title)}</strong>${!allowed ? `<small>${escape(reason)}</small>` : ''}</span>${children.length ? `<span class="mv-count">${children.length} mục</span>` : ''}<span class="mv-dot ${shown.has(node.id) ? 'on' : ''}" title="${escape(reason)}"></span></label>${children.map(n => branch(n, depth + 1)).join('')}</div>`;
        }
        $('mvTree').innerHTML = state.menus.filter(n => n.parentId == null).map(n => branch(n, 0)).join('') || '<p class="mv-empty">Không tìm thấy mục menu.</p>';
        $('mvTree').querySelectorAll('input[data-menu-id]').forEach(input => {
            const ids = descendants(Number(input.dataset.menuId)).filter(id => available(byId.get(id)));
            const count = ids.filter(id => !hidden.has(id)).length;
            input.indeterminate = count > 0 && count < ids.length;
        });
        function preview(node, depth = 0) {
            if (!shown.has(node.id)) return '';
            const children = state.menus.filter(n => n.parentId === node.id && shown.has(n.id));
            return `<div class="mv-preview-row ${depth ? 'child' : ''}" style="--depth:${depth}"><i class="${escape(depth ? 'bx bx-circle' : node.icon || 'bx bx-folder')}"></i><span>${escape(node.title)}</span>${children.length ? '<i class="bx bx-chevron-down"></i>' : ''}</div>${children.map(n => preview(n, depth + 1)).join('')}`;
        }
        $('mvPreview').innerHTML = state.menus.filter(n => n.parentId == null).map(n => preview(n)).join('') || '<div class="mv-empty"><i class="bx bx-hide"></i><p>Không có menu hiển thị.</p></div>';
        $('mvSummary').textContent = `${shown.size} mục hiển thị · ${state.menus.length - shown.size} mục ẩn hoặc chưa có quyền`;
        controls();
    }
    async function request(url, options = {}) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', ...options });
        if (response.redirected) throw new Error('Phiên đăng nhập đã thay đổi. Vui lòng đăng nhập lại.');
        const data = await response.json().catch(() => null);
        if (!response.ok || !data) throw new Error(data?.message || `Không thể tải cấu hình (${response.status}). Vui lòng thử lại.`);
        return data;
    }
    function accept(data) {
        state = data;
        const valid = new Set(data.menus.map(n => n.id));
        state.hiddenIds = data.hiddenIds.filter(id => valid.has(id));
        hidden = new Set(state.hiddenIds); reset = false; dirty = false;
        subjects.forEach(s => { const selected = s.dataset.type === data.type && Number(s.dataset.id) === data.id; s.classList.toggle('selected', selected); s.setAttribute('aria-pressed', String(selected)); });
        const subject = subjects.find(s => s.dataset.type === data.type && Number(s.dataset.id) === data.id);
        if (data.type === 'employee' && subject) subject.querySelector('em').textContent = data.inherited ? 'Theo nhóm' : 'Menu riêng';
        const roleSubject = subjects.find(s => s.dataset.type === 'role' && Number(s.dataset.id) === data.roleId);
        if (roleSubject) roleSubject.querySelector('em').textContent = `${data.memberCount} nhân viên · ${data.personalCount} chỉnh riêng`;
    }
    async function load(type, id) {
        if (busy || (dirty && !confirm('Có thay đổi chưa lưu. Bỏ các thay đổi để chuyển cấu hình?'))) return;
        const seq = ++sequence; busy = true; controls(); message('');
        try {
            const url = new URL(root.dataset.detailUrl, location.origin); url.searchParams.set('type', type); url.searchParams.set('id', id);
            const data = await request(url);
            if (seq !== sequence) return;
            accept(data);
        } catch (error) { message(error.message, true); }
        finally { busy = false; render(); controls(); }
    }
    subjects.forEach(s => s.addEventListener('click', () => load(s.dataset.type, Number(s.dataset.id))));
    root.querySelectorAll('.mv-tabs button').forEach(button => button.addEventListener('click', () => {
        mode = button.dataset.type;
        root.querySelectorAll('.mv-tabs button').forEach(b => { b.classList.toggle('active', b === button); b.setAttribute('aria-pressed', String(b === button)); });
        filterSubjects();
    }));
    $('mvSubjectSearch').addEventListener('input', filterSubjects);
    $('mvMenuSearch').addEventListener('input', render);
    $('mvTree').addEventListener('change', event => {
        const input = event.target.closest('input[data-menu-id]'); if (!input || busy) return;
        const id = Number(input.dataset.menuId);
        descendants(id).forEach(key => { if (input.checked) hidden.delete(key); else hidden.add(key); });
        if (input.checked) {
            let node = state.menus.find(n => n.id === id); const seen = new Set();
            while (node?.parentId != null && !seen.has(node.parentId)) { seen.add(node.parentId); hidden.delete(node.parentId); node = state.menus.find(n => n.id === node.parentId); }
        }
        reset = false; changed(); render();
    });
    $('mvShowAll').addEventListener('click', () => { hidden.clear(); reset = false; changed(); render(); });
    $('mvHideAll').addEventListener('click', () => { hidden = new Set(state.menus.map(n => n.id)); reset = false; changed(); render(); });
    $('mvReset').addEventListener('click', () => { hidden = new Set(state.type === 'employee' ? state.inheritedHiddenIds : []); reset = true; changed(); render(); });
    $('mvReload').addEventListener('click', () => load(state.type, state.id));
    $('mvSave').addEventListener('click', async () => {
        if (busy || !dirty || !state) return;
        busy = true; controls(); message('');
        try {
            const data = await request(root.dataset.saveUrl, { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': root.querySelector('input[name="__RequestVerificationToken"]').value }, body: JSON.stringify({ type: state.type, id: state.id, version: state.version, hiddenIds: [...hidden], reset }) });
            accept(data); message('Đã lưu hiển thị menu. Nhân viên tải lại trang để nhận cấu hình mới.');
        } catch (error) { message(error.message, true); }
        finally { busy = false; render(); }
    });
    window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
    filterSubjects();
    const first = subjects.find(s => !s.hidden); if (first) load(first.dataset.type, Number(first.dataset.id));
})();
