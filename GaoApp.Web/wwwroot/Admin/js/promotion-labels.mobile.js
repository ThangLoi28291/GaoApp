/* UI-19: labels phone presentation. Native data, quantities, details, preview and printing stay native. */
(function () {
    'use strict';
    function start() {
        const root = document.getElementById('promotionLabels');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map(), added = new Set();
        let printing = false, queued = false;
        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restoreNode(node, attributes) {
            for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
        }
        function restore() {
            for (const node of added) node.remove();
            added.clear();
            for (const [node, attributes] of originals) restoreNode(node, attributes);
            originals.clear();
        }
        function decorateLines() {
            const table = root.querySelector('.pl-table-scroll > table');
            if (!table) return;
            mark(table, 'role', 'table');
            const headers = [...(table.tHead?.rows[0]?.cells || [])].map(cell => cell.textContent.trim());
            if (table.tHead) mark(table.tHead, 'role', 'rowgroup');
            [...(table.tHead?.rows || [])].forEach(row => {
                mark(row, 'role', 'row'); [...row.cells].forEach(cell => mark(cell, 'role', 'columnheader'));
            });
            [...table.tBodies].forEach(body => {
                mark(body, 'role', 'rowgroup');
                [...body.rows].forEach(row => {
                    mark(row, 'role', 'row');
                    [...row.cells].forEach((cell, index) => {
                        mark(cell, 'role', 'cell');
                        if (cell.colSpan === 1) mark(cell, 'data-plm-label', headers[index] || 'Thao tác');
                    });
                });
            });
        }
        function paint() {
            if (printing || !phone.matches) { restore(); return; }
            for (const [node, attributes] of originals) if (!node.isConnected) {
                restoreNode(node, attributes); originals.delete(node);
            }
            if (!added.size) {
                const panels = [...root.querySelectorAll('.pl-main > .pl-panel'), root.querySelector('.pl-print-panel')];
                const first = panels[0];
                if (first) {
                    const title = document.createElement('h2');
                    title.className = 'plm-selection-title'; title.textContent = 'Chọn chương trình và sản phẩm';
                    title.setAttribute('data-plm-number', '01'); first.prepend(title); added.add(title);
                }
                panels.forEach((panel, index) => {
                    mark(panel, 'data-plm-step', String(index + 1));
                    const title = panel?.querySelector(':scope > .pl-toolbar > h2');
                    if (title) mark(title, 'data-plm-number', String(index + 1).padStart(2, '0'));
                });
                const footer = root.querySelector('.pl-print-footer');
                if (footer) {
                    const note = document.createElement('p');
                    note.className = 'pl-note plm-device-note';
                    note.textContent = 'Kiểm tra mẫu và số tem trước khi in. In trực tiếp cần GaoApp Label Print Client trên thiết bị đang dùng.';
                    footer.before(note); added.add(note);
                }
            }
            decorateLines();
        }
        function queue() {
            if (queued || printing) return;
            queued = true;
            requestAnimationFrame(() => { queued = false; paint(); });
        }
        const lines = document.getElementById('plLines');
        if (lines) new MutationObserver(queue).observe(lines, { childList: true, subtree: true });
        phone.addEventListener('change', paint);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; paint(); });
        paint();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
