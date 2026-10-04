(() => {
    'use strict';
    window.initProductFilters = ({ shell, onChange }) => {
        const box = shell.querySelector('.product-extra-filters');
        const chips = document.getElementById('productFilterChips');
        const issue = document.getElementById('ddlDataIssue');
        const search = document.getElementById('txtSearch'), category = document.getElementById('ddlCategory'), lifecycle = document.getElementById('ddlLifecycle');
        const lookups = [];
        function changed() { render(); onChange(); }
        shell.querySelectorAll('[data-product-lookup]').forEach(wrapper => {
            const kind = wrapper.dataset.productLookup, key = wrapper.dataset.filterKey;
            const input = wrapper.querySelector('[role=combobox]'), hidden = wrapper.querySelector('input[type=hidden]');
            const panel = wrapper.querySelector('.product-lookup-panel'), list = panel.querySelector('[role=listbox]'), status = panel.querySelector('[role=status]');
            let timer, controller, sequence = 0, options = [], active = -1;
            const cache = new Map(); if (hidden.value) cache.set(hidden.value, input.value);
            const label = item => item.name + (item.isActive ? '' : ' (ngừng hoạt động)');
            function cancel() { clearTimeout(timer); controller?.abort(); sequence++; }
            function close() { cancel(); panel.hidden = true; input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant'); active = -1; }
            function highlight(index) {
                active = index; [...list.children].forEach((n, i) => n.setAttribute('aria-selected', String(i === index)));
                if (list.children[index]) { input.setAttribute('aria-activedescendant', list.children[index].id); list.children[index].scrollIntoView({ block: 'nearest' }); }
            }
            function choose(index) {
                const item = options[index]; if (!item) return;
                hidden.value = String(item.id); input.value = label(item); cache.set(hidden.value, input.value); close(); changed();
            }
            async function fetchOptions(params, signal) {
                const response = await fetch(box.dataset.filterOptionsUrl + '?' + new URLSearchParams({ kind, ...params }), { signal, headers: { Accept: 'application/json' } });
                if (!response.ok || response.redirected) throw new Error('lookup');
                const data = await response.json(); if (!Array.isArray(data)) throw new Error('lookup'); return data;
            }
            async function find() {
                cancel(); const seq = sequence; const currentRequest = controller = new AbortController();
                options = []; active = -1; list.replaceChildren(); panel.hidden = false; input.setAttribute('aria-expanded', 'true');
                input.removeAttribute('aria-activedescendant'); status.textContent = 'Đang tìm…';
                const timeout = setTimeout(() => currentRequest.abort(), 10000);
                try {
                    const data = await fetchOptions({ term: hidden.value ? '' : input.value.trim() }, currentRequest.signal);
                    if (seq !== sequence || document.activeElement !== input) return;
                    options = data;
                    list.replaceChildren(...options.map((item, index) => {
                        const button = document.createElement('button'); button.type = 'button'; button.tabIndex = -1; button.id = 'product-' + kind + '-' + index;
                        button.setAttribute('role', 'option'); button.setAttribute('aria-selected', 'false');
                        const name = document.createElement('strong'), detail = document.createElement('small'); name.textContent = label(item); detail.textContent = item.code;
                        button.append(name, detail); button.addEventListener('pointerdown', e => e.preventDefault()); button.addEventListener('click', () => choose(index)); return button;
                    }));
                    status.textContent = options.length ? 'Chọn một kết quả để lọc' + (options.length === 20 ? ' · Gõ thêm để thu hẹp' : '') : 'Không có kết quả phù hợp.';
                    if (options.length) highlight(0);
                } catch { if (seq === sequence) status.textContent = 'Chưa tải được gợi ý. Bấm ↓ để thử lại.'; }
                finally { clearTimeout(timeout); }
            }
            input.addEventListener('focus', () => void find());
            input.addEventListener('blur', () => { close(); if (!hidden.value) input.value = ''; });
            input.addEventListener('input', () => {
                const had = !!hidden.value; hidden.value = ''; close(); wrapper.querySelector('[data-clear-lookup]').hidden = !input.value; if (had) changed();
                timer = setTimeout(() => void find(), 250);
            });
            input.addEventListener('keydown', event => {
                if (event.key === 'Escape') { event.preventDefault(); close(); }
                else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                    event.preventDefault(); if (panel.hidden || !options.length) void find();
                    else highlight((active + (event.key === 'ArrowDown' ? 1 : options.length - 1)) % options.length);
                } else if (event.key === 'Enter' && !panel.hidden && active >= 0) { event.preventDefault(); choose(active); }
            });
            const state = { key, wrapper, input, hidden, clear() { close(); hidden.value = ''; input.value = ''; },
                async restore(id) {
                    close(); hidden.value = id || ''; input.value = id ? cache.get(id) || 'Đang tải lựa chọn…' : '';
                    if (!id || cache.has(id)) return;
                    const seq = sequence;
                    try { const values = await fetchOptions({ selectedId: id }); if (seq !== sequence || hidden.value !== id) return; input.value = values.length ? label(values[0]) : 'Không còn khả dụng'; cache.set(id, input.value); }
                    catch { if (seq === sequence) input.value = 'Không tải được tên lựa chọn'; }
                    render();
                }
            };
            wrapper.querySelector('[data-clear-lookup]').addEventListener('click', () => { state.clear(); changed(); });
            lookups.push(state);
        });
        function render() {
            if (!chips) return; chips.replaceChildren();
            function chip(text, remove) {
                const b = document.createElement('button'); b.type = 'button'; b.className = 'product-filter-chip'; b.textContent = text + ' ×'; b.setAttribute('aria-label', 'Bỏ lọc ' + text);
                b.onclick = () => { remove(); changed(); }; chips.append(b);
            }
            if (search.value.trim()) chip('Từ khóa: ' + search.value.trim(), () => { search.value = ''; });
            if (category.value) chip('Danh mục: ' + category.selectedOptions[0].text, () => { category.value = ''; });
            if (lifecycle.value !== 'all') chip(lifecycle.selectedOptions[0].text, () => { lifecycle.value = 'all'; });
            for (const x of lookups) {
                x.wrapper.querySelector('[data-clear-lookup]').hidden = !x.hidden.value && !x.input.value;
                if (x.hidden.value) chip(x.wrapper.dataset.filterLabel + ': ' + x.input.value, () => x.clear());
            }
            if (issue.value) chip(issue.selectedOptions[0].text, () => { issue.value = ''; });
            if (!chips.childElementCount) { const note = document.createElement('span'); note.textContent = 'Đang xem tất cả sản phẩm chưa xóa'; chips.append(note); }
        }
        issue?.addEventListener('change', changed);
        document.getElementById('btnResetProductFilters')?.addEventListener('click', () => {
            lookups.forEach(x => x.clear()); issue.value = ''; search.value = ''; category.value = ''; lifecycle.value = 'all'; changed();
        });
        render();
        return { render,
            append(parameters) { lookups.forEach(x => { if (x.hidden.value) parameters.set(x.key, x.hidden.value); }); if (issue.value) parameters.set('dataIssue', issue.value); },
            restore(parameters) { lookups.forEach(x => void x.restore(parameters.get(x.key))); issue.value = parameters.get('dataIssue') || ''; render(); }
        };
    };
})();
