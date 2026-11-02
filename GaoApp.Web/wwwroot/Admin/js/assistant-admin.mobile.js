/* UI-20: presentation only. Native assistant admin forms, data and review handlers retain ownership. */
(function () {
    'use strict';
    function start() {
        const roots = [
            [document.getElementById('assistantKnowledgeAdmin'), 'knowledge'],
            [document.getElementById('assistantSearchAdmin'), 'search'],
            [document.querySelector('.aq-page'), 'quality'],
            [document.getElementById('aiEnabled')?.closest('form')?.parentElement, 'settings']
        ].filter(([root]) => root);
        if (!roots.length) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        let printing = false, queued = false;
        function mark(node, name, value) {
            if (!node) return;
            let attrs = originals.get(node);
            if (!attrs) { attrs = new Map(); originals.set(node, attrs); }
            if (!attrs.has(name)) attrs.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restoreNode(node, attrs) {
            for (const [name, value] of attrs) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
        }
        function restore() {
            for (const [node, attrs] of originals) restoreNode(node, attrs);
            originals.clear();
        }
        function tableCards(table) {
            mark(table, 'data-assistant-admin-table', ''); mark(table, 'role', 'table');
            const labels = [...(table.tHead?.rows[0]?.cells || [])].map(cell => cell.textContent.trim().replace(/\s+/g, ' '));
            if (table.tHead) mark(table.tHead, 'role', 'rowgroup');
            [...(table.tHead?.rows || [])].forEach(row => mark(row, 'role', 'row'));
            [...table.tBodies].forEach(body => {
                mark(body, 'role', 'rowgroup');
                [...body.rows].forEach(row => {
                    mark(row, 'role', 'row');
                    [...row.cells].forEach((cell, index) => {
                        mark(cell, 'role', 'cell');
                        if (cell.colSpan === 1 && labels[index]) mark(cell, 'data-assistant-admin-cell-label', labels[index]);
                    });
                });
            });
        }
        function paint() {
            if (printing || !phone.matches) { restore(); return; }
            for (const [node, attrs] of originals) if (!node.isConnected) { restoreNode(node, attrs); originals.delete(node); }
            for (const [root, kind] of roots) {
                mark(root, 'data-assistant-admin-phone', kind);
                mark(root.firstElementChild, 'data-assistant-admin-heading', '');
                root.querySelectorAll('table').forEach(tableCards);
            }
        }
        function queue() {
            if (queued || printing) return;
            queued = true;
            requestAnimationFrame(() => { queued = false; paint(); });
        }
        const observer = new MutationObserver(queue);
        for (const [root] of roots) observer.observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', paint);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; paint(); });
        paint();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
