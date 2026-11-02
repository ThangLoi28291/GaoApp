/* UI-17: presentation only; native ACB lookup, callbacks, reconciliation and payment commands retain ownership. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('#acbLookup, .acb-reconcile');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        const kind = root.matches('.acb-reconcile') ? 'reconcile' : document.body.classList.contains('acb-embedded') ? 'embedded' : 'lookup';
        let hostPhone;
        if (kind === 'embedded' && parent !== window) {
            try { hostPhone = parent.matchMedia('(max-width: 767.98px)'); } catch (_) { /* Standalone viewport scope remains sufficient for a different-origin host. */ }
        }
        let printing = false, queued = false;
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
        function tables() {
            root.querySelectorAll('table.acb-ledger').forEach(table => {
                mark(table, 'data-bank-workspace-phone-table', ''); mark(table, 'role', 'table');
                const headers = [];
                [...(table.tHead?.rows || [])].forEach(row => [...row.cells].forEach(cell => {
                    const label = cell.textContent.trim().replace(/\s+/g, ' ');
                    for (let index = 0; index < Math.max(1, cell.colSpan); index++) headers.push(label);
                }));
                [...table.tBodies].forEach(body => {
                    const occupied = [];
                    [...body.rows].forEach(row => {
                        mark(row, 'role', 'row');
                        const continuation = occupied.some(span => span > 0);
                        if (continuation) mark(row, 'data-bank-workspace-phone-continuation', '');
                        let column = 0;
                        [...row.cells].forEach(cell => {
                            while (occupied[column] > 0) column++;
                            mark(cell, 'role', 'cell');
                            if (headers[column]) mark(cell, 'data-bank-workspace-phone-label', headers[column]);
                            if (column === 0 || cell.colSpan > 1 || cell.rowSpan > 1 || cell.matches('.acb-tx-confirmation,.acb-report-result,.acb-bank-callback') || cell.querySelector('form,a.acb-button,button')) mark(cell, 'data-bank-workspace-phone-wide', '');
                            for (let index = 0; index < Math.max(1, cell.colSpan); index++) occupied[column + index] = Math.max(occupied[column + index] || 0, cell.rowSpan);
                            column += Math.max(1, cell.colSpan);
                        });
                        for (let index = 0; index < occupied.length; index++) occupied[index] = Math.max(0, (occupied[index] || 0) - 1);
                    });
                });
            });
        }
        function paint() {
            if (printing || !phone.matches || (hostPhone && !hostPhone.matches)) { restore(); return; }
            mark(document.body, 'data-bank-workspace-phone', kind);
            mark(root, 'data-bank-workspace-phone-root', kind);
            if (kind === 'lookup' && root.previousElementSibling?.matches('a.btn[href]')) mark(root.previousElementSibling, 'data-bank-workspace-phone-leading', '');
            tables();
        }
        function queue() {
            if (queued || printing) return;
            queued = true;
            requestAnimationFrame(() => { queued = false; paint(); });
        }
        new MutationObserver(queue).observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', paint);
        hostPhone?.addEventListener('change', paint);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; paint(); });
        paint();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
