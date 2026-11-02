/* Presentation only: keep the server rows, source values and original command nodes. */
(() => {
    'use strict';
    const roots = [...document.querySelectorAll('[data-after-sales-phone]')];
    if (!roots.length) return;
    const phone = matchMedia('(max-width: 767.98px)');
    const saved = new Map();
    function attr(node, name, value) {
        if (!saved.has(node)) saved.set(node, new Map());
        const original = saved.get(node);
        if (!original.has(name)) original.set(name, node.getAttribute(name));
        node.setAttribute(name, value);
    }
    function sync() {
        if (!phone.matches) {
            for (const [node, attrs] of saved) for (const [name, value] of attrs) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            saved.clear();
            return;
        }
        for (const root of roots) for (const table of root.querySelectorAll('table')) {
            attr(table, 'role', 'table');
            for (const group of table.querySelectorAll('thead,tbody')) attr(group, 'role', 'rowgroup');
            const headers = [...table.querySelectorAll('thead th')];
            for (const header of headers) attr(header, 'role', 'columnheader');
            for (const row of table.querySelectorAll('tr')) attr(row, 'role', 'row');
            for (const row of table.querySelectorAll('tbody tr')) for (const [index, cell] of [...row.cells].entries()) {
                attr(cell, 'role', 'cell');
                if (cell.colSpan > 1) continue;
                const label = root.id === 'posPendingRestock' && index === 2
                    ? 'Chờ nhập · đơn vị gốc' : headers[index]?.textContent.trim();
                if (label) attr(cell, 'data-after-sales-label', label);
            }
        }
    }
    phone.addEventListener('change', sync);
    sync();
})();
