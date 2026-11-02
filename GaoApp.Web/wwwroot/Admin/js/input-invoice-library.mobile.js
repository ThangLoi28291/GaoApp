/* UI-11f: presentation only. Original invoice fields, permissions and request owner stay in place. */
(() => {
    'use strict';
    const root = document.getElementById('invoiceLibrary');
    if (!root || !window.matchMedia) return;
    const phone = window.matchMedia('(max-width: 767.98px)');
    const filters = document.getElementById('ilFilters');
    const rows = document.getElementById('ilRows');
    const table = rows?.closest('table');
    const original = new Map();
    let toggle = null;

    function attribute(node, name, value) {
        if (!node) return;
        let saved = original.get(node);
        if (!saved) { saved = new Map(); original.set(node, saved); }
        if (!saved.has(name)) saved.set(name, node.getAttribute(name));
        if (node.getAttribute(name) !== value) node.setAttribute(name, value);
    }

    function refreshToggle() {
        if (!toggle) return;
        if (toggle.getAttribute('aria-expanded') === 'true') {
            toggle.textContent = 'Thu gọn bộ lọc';
            return;
        }
        const year = filters.elements.namedItem('year')?.value;
        const month = filters.elements.namedItem('month')?.value;
        const context = year ? (month ? `T${month}/${year}` : `Năm ${year}`) : (month ? `Tháng ${month}` : 'Mọi thời gian');
        const other = ['kind', 'linked', 'review'].filter(name => filters.elements.namedItem(name)?.value).length;
        toggle.textContent = `Bộ lọc · ${context}${other ? ` · +${other}` : ''}`;
    }

    function expand(value) {
        attribute(root, 'data-il-phone-filter-expanded', String(value));
        toggle?.setAttribute('aria-expanded', String(value));
        refreshToggle();
    }

    function decorate() {
        if (!phone.matches) return;
        attribute(root, 'data-il-phone', 'true');
        attribute(document.getElementById('ilCount'), 'aria-live', 'polite');
        if (filters && !toggle) {
            toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'il-mobile-filter-toggle';
            toggle.setAttribute('aria-controls', filters.id);
            toggle.addEventListener('click', () => expand(toggle.getAttribute('aria-expanded') !== 'true'));
            filters.insertBefore(toggle, filters.querySelector('.il-actions'));
            expand(false);
        }
        refreshToggle();
        if (!table || !rows) return;
        attribute(table, 'role', 'table');
        attribute(table.tHead, 'role', 'rowgroup');
        attribute(rows, 'role', 'rowgroup');
        const headings = [...table.querySelectorAll('thead th')];
        table.querySelectorAll('thead tr').forEach(row => attribute(row, 'role', 'row'));
        headings.forEach(heading => attribute(heading, 'role', 'columnheader'));
        rows.querySelectorAll('tr').forEach(row => {
            attribute(row, 'role', 'row');
            [...row.children].forEach((cell, index) => {
                attribute(cell, 'role', 'cell');
                if (cell.colSpan === 1 && headings[index]?.textContent.trim()) {
                    attribute(cell, 'data-il-phone-label', headings[index].textContent.trim());
                }
            });
        });
        // The original list owner replaces rows on search or paging. Release detached presentation state.
        for (const node of original.keys()) if (!node.isConnected) original.delete(node);
    }

    function restore() {
        toggle?.remove(); toggle = null;
        for (const [node, attributes] of original) {
            for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name);
                else node.setAttribute(name, value);
            }
        }
        original.clear();
    }

    // Native validation can focus any original filter, including a currently collapsed one.
    filters?.addEventListener('invalid', () => { if (phone.matches) expand(true); }, true);
    filters?.addEventListener('input', refreshToggle);
    filters?.addEventListener('change', refreshToggle);
    const observer = new MutationObserver(decorate);
    if (rows) observer.observe(rows, { childList: true, subtree: true });
    const update = () => phone.matches ? decorate() : restore();
    if (phone.addEventListener) phone.addEventListener('change', update);
    else phone.addListener(update);
    update();
})();
