(() => {
    'use strict';
    const root = document.getElementById('menuVisibility');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const subjects = [...root.querySelectorAll('.mv-subject')];
    const phone = matchMedia('(max-width: 767.98px)'), review = document.querySelector('[data-menu-review]');
    const originalSaveHtml = $('mvSave').innerHTML, originalReloadHtml = $('mvReload').innerHTML, collapsed = new Set();
    function stage(name, focus = false) {
        root.dataset.mobileStage = name;
        root.querySelectorAll('.mv-mobile-steps [data-mv-stage]').forEach(button => {
            if (button.dataset.mvStage === name) button.setAttribute('aria-current', 'step'); else button.removeAttribute('aria-current');
        });
        if (!phone.matches || !focus) return;
        const target = name === 'subjects' ? $('mvSubjectSearch') : name === 'editor' ? $('mvTargetName') : root.querySelector('.mv-preview h2');
        if (target) { if (!target.matches('input')) target.tabIndex = -1; target.focus({ preventScroll: true }); target.scrollIntoView({ block: 'start', behavior: 'auto' }); }
    }
    let mode = 'role', state = null, hidden = new Set(), reset = false, dirty = false, busy = false, sequence = 0;
    const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const normalize = value => String(value).normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').toLowerCase();
    function message(text, error = false) {
        $('mvMessage').textContent = text; $('mvMessage').hidden = !text; $('mvMessage').classList.toggle('error', error);
        if (phone.matches && text) $('mvMessage').scrollIntoView({ block: 'center', behavior: 'auto' });
    }
    function controls() {
        ['mvShowAll', 'mvHideAll', 'mvReset', 'mvReload'].forEach(id => $(id).disabled = busy || !state);
        $('mvSave').disabled = busy || !state || !dirty;
        root.querySelector('.mv-editor').setAttribute('aria-busy', String(busy));
        root.querySelectorAll('.mv-tree input').forEach(input => input.disabled = busy || input.dataset.available !== 'true');
        $('mvSaveStatus').textContent = busy ? 'Đang xử lý…' : dirty ? 'Có thay đổi chưa lưu' : state ? 'Đã đồng bộ cấu hình hiển thị' : 'Các thay đổi chỉ áp dụng sau khi lưu.';
        root.querySelectorAll('[data-mv-stage]').forEach(button => button.disabled = busy || (button.dataset.mvStage !== 'subjects' && !state));
        $('mvSave').innerHTML = phone.matches ? '<i class="bx bx-check" aria-hidden="true"></i> Xem lại và lưu' : originalSaveHtml;
        $('mvReload').innerHTML = phone.matches ? '<i class="bx bx-refresh" aria-hidden="true"></i>' : originalReloadHtml;
        $('mvReload').setAttribute('aria-label', 'Tải lại cấu hình');
        if (state) $('mvReset').textContent = phone.matches ? (state.type === 'employee' ? 'Dùng nhóm' : 'Mặc định')
            : (state.type === 'role' ? 'Khôi phục mặc định' : 'Dùng menu của nhóm');
        if (phone.matches) $('mvSummary').title = $('mvSummary').textContent; else $('mvSummary').removeAttribute('title');
        $('mvMobileTarget').textContent = state?.name || 'Chưa chọn đối tượng';
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
    function visibleIds(selection = hidden) {
        const ids = new Set(), visited = new Set();
        function visit(node) {
            if (visited.has(node.id) || !node.available || selection.has(node.id)) return false;
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
        const focusedMenuId = phone.matches && document.activeElement?.closest('#mvTree') ? document.activeElement.dataset.menuId : null;
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
            const closed = !q && collapsed.has(node.id);
            const mobileReason = shown.has(node.id) ? 'Đang hiển thị' : hidden.has(node.id) ? 'Đang ẩn' : 'Ẩn theo menu cha hoặc nhóm chưa có mục khả dụng';
            return `<div class="mv-branch" data-mobile-collapsed="${closed}"><label class="mv-row ${children.length ? 'mv-parent' : ''} ${!allowed ? 'mv-unavailable' : ''}" style="--depth:${Math.min(depth, 5)};--mobile-depth:${Math.min(depth, 2)}"><input type="checkbox" data-menu-id="${node.id}" data-available="${allowed}" aria-label="${escape(node.title)}" ${checked ? 'checked' : ''} ${!allowed || busy ? 'disabled' : ''}><span class="mv-menu-icon"><i class="${escape(node.icon || 'bx bx-folder')}"></i></span><span class="mv-row-name"><strong>${escape(node.title)}</strong>${!allowed ? `<small>${escape(reason)}</small>` : `<small class="gao-menu-mobile-only mv-row-state">${escape(mobileReason)}</small>`}</span>${children.length ? `<span class="mv-count">${children.length} mục</span>` : ''}<span class="mv-dot ${shown.has(node.id) ? 'on' : ''}" title="${escape(reason)}"></span></label>${children.length ? `<button type="button" class="gao-menu-mobile-only mv-branch-toggle" data-mv-toggle="${node.id}" aria-label="Thu gọn hoặc mở nhóm ${escape(node.title)}" aria-expanded="${!closed}" aria-controls="mv-children-${node.id}"><i class="bx bx-chevron-down" aria-hidden="true"></i></button><div class="mv-children" id="mv-children-${node.id}">${children.map(n => branch(n, depth + 1)).join('')}</div>` : ''}</div>`;
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
        if (focusedMenuId) {
            const next = $('mvTree').querySelector('input[data-menu-id="' + focusedMenuId + '"]');
            if (next && !next.disabled) next.focus({ preventScroll: true });
        }
    }
    async function request(url, options = {}) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', ...options });
        if (response.redirected) throw new Error('Phiên đăng nhập đã thay đổi. Vui lòng đăng nhập lại.');
        const data = await response.json().catch(() => null);
        if (!response.ok || !data) throw new Error(data?.message || `Không thể tải cấu hình (${response.status}). Vui lòng thử lại.`);
        return data;
    }
    function accept(data) {
        if (!state || state.type !== data.type || state.id !== data.id) collapsed.clear();
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
            if (phone.matches) stage('editor', true);
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
    async function saveVisibility() {
        if (busy || !dirty || !state) return;
        busy = true; controls(); message('');
        try {
            const data = await request(root.dataset.saveUrl, { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': root.querySelector('input[name="__RequestVerificationToken"]').value }, body: JSON.stringify({ type: state.type, id: state.id, version: state.version, hiddenIds: [...hidden], reset }) });
            accept(data); message('Đã lưu hiển thị menu. Nhân viên tải lại trang để nhận cấu hình mới.');
        } catch (error) { message(error.message, true); }
        finally { busy = false; render(); }
    }
    function openReview() {
        if (!state || !dirty || busy || !review || typeof review.showModal !== 'function') return false;
        const list = review.querySelector('[data-menu-review-summary]'); list.replaceChildren();
        const add = (label, text) => {
            const row = document.createElement('div'), term = document.createElement('dt'), value = document.createElement('dd');
            term.textContent = label; value.textContent = text; row.append(term, value); list.append(row);
        };
        const before = visibleIds(new Set(state.hiddenIds)), after = visibleIds();
        const changedMenus = state.menus.filter(node => before.has(node.id) !== after.has(node.id));
        review.querySelector('[data-menu-review-intro]').textContent = 'Kiểm tra đúng đối tượng và phạm vi áp dụng. Chỉ cấu hình hiển thị được lưu.';
        add('Đối tượng', state.name);
        add('Phạm vi', $('mvImpact').textContent);
        add('Cách áp dụng', state.type === 'employee' && reset ? 'Dùng lại menu của nhóm' : state.type === 'employee' ? 'Cấu hình riêng cho nhân viên này' : reset ? 'Khôi phục mặc định của nhóm' : 'Cập nhật menu của nhóm');
        add('Mục hiển thị', before.size + ' → ' + after.size);
        add('Thay đổi thực tế', changedMenus.length ? changedMenus.length + ' mục đổi trạng thái hiển thị' : 'Giữ số mục hiện tại; cập nhật cách kế thừa hoặc lựa chọn ẩn/hiện');
        changedMenus.slice(0, 12).forEach(node => add(node.title, after.has(node.id) ? 'Sẽ hiển thị' : 'Sẽ ẩn'));
        if (changedMenus.length > 12) add('Các mục còn lại', (changedMenus.length - 12) + ' mục; xem đầy đủ ở bước Xem trước');
        review.showModal(); return true;
    }
    $('mvSave').addEventListener('click', () => {
        if (phone.matches && openReview()) return;
        // A phone browser lacking modal review must not silently skip the review step.
        if (phone.matches) { if (state && dirty && !busy) message('Trình duyệt chưa hỗ trợ xem lại trước lưu. Hãy dùng trình duyệt mới hơn.', true); return; }
        saveVisibility();
    });
    review?.querySelector('[data-menu-review-cancel]').addEventListener('click', () => review.close());
    review?.querySelector('[data-menu-review-confirm]').addEventListener('click', () => {
        if (!phone.matches || busy || !dirty || !state) return;
        review.close(); saveVisibility();
    });
    root.querySelectorAll('[data-mv-stage]').forEach(button => button.addEventListener('click', () => {
        if (!phone.matches || busy || (button.dataset.mvStage !== 'subjects' && !state)) return;
        stage(button.dataset.mvStage, true);
    }));
    $('mvTree').addEventListener('click', event => {
        const button = event.target.closest('[data-mv-toggle]');
        if (!phone.matches || !button || busy || $('mvMenuSearch').value.trim()) return;
        const id = Number(button.dataset.mvToggle);
        if (collapsed.has(id)) collapsed.delete(id); else collapsed.add(id);
        const branch = button.closest('.mv-branch'); branch.dataset.mobileCollapsed = String(collapsed.has(id));
        button.setAttribute('aria-expanded', String(!collapsed.has(id)));
    });
    phone.addEventListener('change', () => {
        if (review?.open) review.close();
        controls();
        if (phone.matches) stage(state ? 'editor' : 'subjects');
        else if (!state && !busy) { const first = subjects.find(s => !s.hidden); if (first) load(first.dataset.type, Number(first.dataset.id)); }
    });
    window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
    filterSubjects();
    controls();
    const first = subjects.find(s => !s.hidden); if (first && !phone.matches) load(first.dataset.type, Number(first.dataset.id));
})();
