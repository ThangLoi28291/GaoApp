(() => {
    'use strict';
    const root = document.querySelector('.pos-orders-page');
    if (!root) return;
    root.querySelectorAll('[data-order-lookup]').forEach(wrapper => {
        const kind = wrapper.dataset.orderLookup, input = wrapper.querySelector('#' + kind);
        const selectedId = wrapper.querySelector('input[type=hidden]');
        const panel = wrapper.querySelector('.po-lookup-panel'), list = panel.querySelector('[role=listbox]');
        const status = panel.querySelector('[role=status]');
        let timer, controller, sequence = 0, options = [], active = -1;
        function cancel() { clearTimeout(timer); controller?.abort(); controller = null; sequence++; }
        function close() {
            cancel(); panel.hidden = true; input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant'); active = -1;
        }
        function highlight(index) {
            active = index;
            [...list.children].forEach((node, i) => node.setAttribute('aria-selected', String(i === index)));
            const node = list.children[index];
            if (node) { input.setAttribute('aria-activedescendant', node.id); node.scrollIntoView({ block: 'nearest' }); }
        }
        function choose(index) {
            const item = options[index]; if (!item) return;
            input.value = item.name + (item.code && item.code !== item.name ? ' · ' + item.code : '');
            selectedId.value = String(item.id); close(); input.dispatchEvent(new Event('change', { bubbles: true }));
        }
        async function search() {
            cancel(); const seq = sequence, term = input.value.trim();
            const request = controller = new AbortController(); const signal = request.signal;
            const timeout = setTimeout(() => request.abort(), 10000);
            options = []; active = -1; list.replaceChildren(); input.removeAttribute('aria-activedescendant');
            panel.hidden = false; input.setAttribute('aria-expanded', 'true'); status.textContent = 'Đang tìm…';
            try {
                const response = await fetch('/admin/pos/orders/filter-options?' + new URLSearchParams({ kind, term: selectedId.value ? '' : term }),
                    { headers: { Accept: 'application/json' }, cache: 'no-store', signal });
                if (response.redirected || !response.ok) throw new Error('lookup');
                const data = await response.json();
                if (seq !== sequence || document.activeElement !== input) return;
                if (!Array.isArray(data)) throw new Error('lookup');
                options = data.filter(item => Number.isSafeInteger(item.id) && item.id > 0 && typeof item.name === 'string');
                list.replaceChildren(...options.map((item, index) => {
                    const button = document.createElement('button');
                    button.type = 'button'; button.tabIndex = -1; button.id = kind + 'Option' + index;
                    button.setAttribute('role', 'option'); button.setAttribute('aria-selected', 'false');
                    const name = document.createElement('strong'), code = document.createElement('span');
                    name.textContent = item.name; code.textContent = item.code;
                    button.append(name, code); button.addEventListener('pointerdown', event => event.preventDefault());
                    button.addEventListener('click', () => choose(index)); return button;
                }));
                status.textContent = options.length ? (options.length === 20 ? 'Hiện 20 kết quả. Gõ thêm để thu hẹp.' : 'Chỉ hiển thị dữ liệu đang kích hoạt.') : 'Không có kết quả đang kích hoạt.';
                if (options.length) highlight(0);
            } catch (error) {
                if (seq !== sequence) return;
                status.textContent = 'Chưa tải được gợi ý. Gõ lại hoặc bấm ↓ để thử lại.';
            } finally { clearTimeout(timeout); }
        }
        input.addEventListener('focus', () => void search());
        input.addEventListener('input', () => {
            selectedId.value = ''; close(); options = [];
            timer = setTimeout(() => void search(), 200);
        });
        input.addEventListener('blur', close);
        input.addEventListener('keydown', event => {
            if (event.key === 'Escape' && !panel.hidden) { event.preventDefault(); event.stopPropagation(); close(); }
            else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                if (panel.hidden || !options.length) void search();
                else highlight((active + (event.key === 'ArrowDown' ? 1 : options.length - 1)) % options.length);
            } else if (event.key === 'Enter' && !panel.hidden && active >= 0) {
                event.preventDefault(); choose(active);
            } else if (event.key === 'Tab') close();
        });
        root.addEventListener('orders:filters-reset', () => { close(); options = []; });
    });
})();
