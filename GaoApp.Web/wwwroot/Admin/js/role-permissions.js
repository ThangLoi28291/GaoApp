(() => {
    'use strict';
    const root = document.getElementById('rolePermissionsPage');
    if (!root) return;
    const $ = id => root.querySelector('#' + id);
    const form = $('rolePermissionForm'), picker = $('rolePicker');
    const boxes = [...root.querySelectorAll('.permission-checkbox')];
    const items = boxes.map(box => ({ box, card: box.closest('.rp-permission'), initial: box.dataset.initial === 'true' }));
    const groups = [...root.querySelectorAll('.rp-group')];
    const features = [...root.querySelectorAll('.rp-feature')];
    const normalize = text => text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase().trim();
    for (const item of items) item.search = normalize(item.card.dataset.search);
    let state = 'all', group = 'all', submitting = false, navigating = false;
    const changes = () => items.filter(item => item.box.checked !== item.initial);
    const visible = () => items.filter(item => !item.card.hidden);
    picker?.addEventListener('change', () => {
        if (changes().length && !window.confirm('Bạn có thay đổi chưa lưu. Rời trang và bỏ các thay đổi này?')) {
            picker.value = form?.dataset.roleId || '0'; return;
        }
        navigating = true;
        const url = new URL(picker.dataset.url, location.origin);
        if (picker.value !== '0') url.searchParams.set('roleId', picker.value);
        location.assign(url);
    });
    if (!form) return;
    function refresh() {
        const selected = items.filter(item => item.box.checked).length;
        const changed = changes(), added = changed.filter(item => item.box.checked).length, removed = changed.length - added;
        $('selectedPermissionCount').textContent = selected;
        $('unselectedPermissionCount').textContent = items.length - selected;
        $('changedPermissionCount').textContent = changed.length;
        $('changeSummary').textContent = changed.length ? `Thêm ${added} · Bỏ ${removed}` : 'Chưa có thay đổi';
        $('saveTitle').textContent = changed.length ? `${changed.length} quyền đang thay đổi` : 'Chưa có thay đổi';
        $('saveSummary').textContent = changed.length ? `Thêm ${added} quyền · Bỏ ${removed} quyền · Chưa được lưu` : 'Chọn quyền để thêm hoặc bỏ quyền của vai trò này.';
        $('undoChanges').disabled = $('reviewChanges').disabled = changed.length === 0;
        const term = normalize($('permissionSearch').value).replace(/\s+/g, ' ');
        for (const item of items) {
            const modified = item.box.checked !== item.initial;
            item.card.hidden = !(item.search.includes(term) &&
                (group === 'all' || item.card.closest('.rp-group').dataset.group === group) &&
                (state === 'all' || state === 'selected' && item.box.checked || state === 'unselected' && !item.box.checked || state === 'changed' && modified));
            item.card.classList.toggle('rp-modified', modified);
            item.card.querySelector('.rp-change-mark').textContent = modified ? (item.box.checked ? 'Thêm' : 'Bỏ') : '';
        }
        for (const feature of features) {
            const showing = [...feature.querySelectorAll('.rp-permission')].filter(card => !card.hidden).map(card => card.querySelector('input'));
            feature.hidden = !showing.length;
            const check = feature.querySelector('.rp-feature-check');
            check.checked = showing.length > 0 && showing.every(box => box.checked);
            check.indeterminate = showing.some(box => box.checked) && !check.checked;
        }
        for (const section of groups) {
            section.hidden = ![...section.querySelectorAll('.rp-feature')].some(feature => !feature.hidden);
            const groupBoxes = [...section.querySelectorAll('.permission-checkbox')];
            root.querySelectorAll(`[data-group-count="${section.dataset.group}"]`).forEach(node => {
                node.textContent = `${groupBoxes.filter(box => box.checked).length} / ${groupBoxes.length}`;
            });
        }
        const count = visible().length;
        $('resultCount').textContent = `Đang hiển thị ${count} / ${items.length} quyền`;
        $('noResults').hidden = count !== 0;
        $('selectVisible').disabled = !count || visible().every(item => item.box.checked);
        $('clearVisible').disabled = !count || visible().every(item => !item.box.checked);
        root.querySelectorAll('[data-state]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.state === state)));
        root.querySelectorAll('[data-group-filter]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.groupFilter === group)));
    }
    function clearFilters() { state = group = 'all'; $('permissionSearch').value = ''; refresh(); }
    $('permissionSearch').addEventListener('input', refresh);
    $('permissionSearch').addEventListener('keydown', event => { if (event.key === 'Enter') event.preventDefault(); });
    $('clearFilters').addEventListener('click', clearFilters);
    $('resetSearch').addEventListener('click', clearFilters);
    $('showCodes').addEventListener('change', event => root.classList.toggle('rp-show-codes', event.target.checked));
    root.querySelectorAll('[data-state]').forEach(button => button.addEventListener('click', () => { state = button.dataset.state; refresh(); }));
    root.querySelectorAll('[data-group-filter]').forEach(button => button.addEventListener('click', () => { group = button.dataset.groupFilter; refresh(); }));
    for (const item of items) item.box.addEventListener('change', refresh);
    for (const feature of features) feature.querySelector('.rp-feature-check').addEventListener('change', event => {
        [...feature.querySelectorAll('.rp-permission')].filter(card => !card.hidden).forEach(card => card.querySelector('input').checked = event.target.checked); refresh();
    });
    $('selectVisible').addEventListener('click', () => { visible().forEach(item => item.box.checked = true); refresh(); });
    $('clearVisible').addEventListener('click', () => { visible().forEach(item => item.box.checked = false); refresh(); });
    $('undoChanges').addEventListener('click', () => { items.forEach(item => item.box.checked = item.initial); refresh(); });
    const dialog = $('changesDialog');
    form.addEventListener('submit', event => {
        if (submitting) return;
        event.preventDefault();
        const changed = changes();
        if (!changed.length) return;
        const list = $('changesList'); list.replaceChildren();
        for (const enabled of [true, false]) {
            const subset = changed.filter(item => item.box.checked === enabled);
            if (!subset.length) continue;
            const section = document.createElement('section'), heading = document.createElement('h3'), ul = document.createElement('ul');
            heading.textContent = `${enabled ? 'Cấp thêm' : 'Bỏ'} ${subset.length} quyền`;
            for (const item of subset) {
                const li = document.createElement('li'), title = document.createElement('strong'), detail = document.createElement('span');
                title.textContent = item.card.dataset.title; detail.textContent = item.card.dataset.groupLabel;
                li.append(title, detail); ul.append(li);
            }
            section.append(heading, ul); list.append(section);
        }
        dialog.showModal();
    });
    root.querySelectorAll('[data-close-dialog]').forEach(button => button.addEventListener('click', () => dialog.close()));
    $('confirmSave').addEventListener('click', () => {
        if (submitting) return;
        submitting = true;
        $('confirmSave').disabled = true; $('confirmSave').textContent = 'Đang lưu…';
        // Hidden filtered checkboxes stay enabled and are all submitted with the form.
        form.requestSubmit();
    });
    window.addEventListener('beforeunload', event => {
        if (!submitting && !navigating && changes().length) { event.preventDefault(); event.returnValue = ''; }
    });
    refresh();
})();
