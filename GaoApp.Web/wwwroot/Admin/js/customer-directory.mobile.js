/* UI-15: presentation only; original customer/supplier forms, filters and profile owners retain their data. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('#customerProfile, [data-customer-shell], [data-supplier-shell], .supplier-editor, [data-layout-consistency]:has(form input[name="PriceTier"]), [data-layout-consistency]:has(form select[name="PriceTier"])');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        const kind = root.id === 'customerProfile' ? 'profile' : root.matches('[data-customer-shell],[data-supplier-shell]') ? 'list' : 'editor';
        let scheduled = false;
        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function profileTables() {
            if (kind !== 'profile') return;
            root.querySelectorAll('.cp-table').forEach(table => {
                mark(table, 'data-directory-phone-table', ''); mark(table, 'role', 'table');
                const headings = [...table.querySelectorAll('thead th')].map(cell => cell.textContent.trim());
                table.querySelectorAll('thead,tbody').forEach(node => mark(node, 'role', 'rowgroup'));
                table.querySelectorAll('tr').forEach(node => mark(node, 'role', 'row'));
                table.querySelectorAll('th').forEach(node => mark(node, 'role', 'columnheader'));
                table.querySelectorAll('tbody tr').forEach(row => [...row.cells].forEach((cell, index) => {
                    mark(cell, 'role', 'cell');
                    if (cell.colSpan === 1) mark(cell, 'data-directory-phone-label', headings[index] || 'Thông tin');
                }));
            });
        }
        function apply() {
            scheduled = false;
            if (!phone.matches) { restore(); return; }
            mark(document.body, 'data-customer-directory-phone', kind);
            mark(root, 'data-directory-phone-root', kind);
            const header = document.querySelector('.gds-page-header');
            if (header && !root.contains(header)) mark(header, 'data-directory-phone-header', '');
            profileTables();
        }
        new MutationObserver(() => {
            if (!phone.matches || scheduled) return;
            scheduled = true; requestAnimationFrame(apply);
        }).observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', apply); apply();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
