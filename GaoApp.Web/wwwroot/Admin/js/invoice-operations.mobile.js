/* UI-16: presentation only. Native invoice, stock, queue and log owners retain every command. */
(function () {
    'use strict';
    function start() {
        let root = document.querySelector('[data-xml-stock], .log-page, .log-detail-page');
        let kind = root?.matches('[data-xml-stock]') ? 'stock' : root?.matches('.log-detail-page') ? 'log-detail' : root ? 'logs' : '';
        if (!root) {
            const correction = document.querySelector('form input[name="OriginalInvoiceHeadId"]');
            const auto = document.querySelector('.auto-card');
            const cleanup = document.querySelector('.cleanup-card');
            root = (correction || auto || cleanup)?.closest('.container-fluid');
            kind = correction ? 'correction' : auto ? 'auto' : cleanup ? 'cleanup' : '';
        }
        if (!root || !kind) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        let queued = false, printing = false;
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
            root.querySelectorAll('table').forEach(table => {
                mark(table, 'data-invoice-ops-table', ''); mark(table, 'role', 'table');
                if (table.tHead?.querySelector('input,button,select')) mark(table.tHead, 'data-invoice-ops-interactive-head', '');
                const matrix = [];
                [...table.tHead?.rows || []].forEach((row, rowIndex) => {
                    matrix[rowIndex] ||= [];
                    let column = 0;
                    for (const cell of row.cells) {
                        while (matrix[rowIndex][column] !== undefined) column++;
                        const title = cell.textContent.trim() || cell.querySelector('[aria-label]')?.getAttribute('aria-label') || 'Chọn';
                        for (let y = 0; y < cell.rowSpan; y++) {
                            matrix[rowIndex + y] ||= [];
                            for (let x = 0; x < cell.colSpan; x++) matrix[rowIndex + y][column + x] = title;
                        }
                        mark(cell, 'role', 'columnheader');
                        if (cell.querySelector('input,button,select')) mark(cell, 'data-invoice-ops-label', title);
                        column += cell.colSpan;
                    }
                });
                const labels = matrix.at(-1) || [];
                table.querySelectorAll('thead,tbody').forEach(node => mark(node, 'role', 'rowgroup'));
                table.querySelectorAll('tr').forEach(node => mark(node, 'role', 'row'));
                for (const body of table.tBodies) {
                    const spans = [];
                    for (const row of body.rows) {
                        let column = 0;
                        for (const cell of row.cells) {
                            while (spans[column] > 0) column++;
                            mark(cell, 'role', 'cell');
                            if (cell.colSpan > 1) mark(cell, 'data-invoice-ops-wide', '');
                            else {
                                mark(cell, 'data-invoice-ops-label', cell.getAttribute('data-label') || labels[column] || 'Thông tin');
                                if (cell.querySelector('button,a.btn,form')) mark(cell, 'data-invoice-ops-actions', '');
                            }
                            for (let x = 0; x < cell.colSpan; x++) if (cell.rowSpan > 1) spans[column + x] = cell.rowSpan;
                            column += cell.colSpan;
                        }
                        for (let x = 0; x < spans.length; x++) if (spans[x] > 0) spans[x]--;
                    }
                }
            });
        }
        function decorate() {
            queued = false;
            if (printing || !phone.matches) { restore(); return; }
            mark(document.body, 'data-invoice-operations-phone', kind);
            mark(root, 'data-invoice-ops-root', kind);
            const header = kind === 'stock' ? document.querySelector('.gds-page-header') : root.querySelector(':scope > .d-flex');
            mark(header, 'data-invoice-ops-header', '');
            tables();
        }
        new MutationObserver(() => {
            if (printing || !phone.matches || queued) return;
            queued = true; requestAnimationFrame(decorate);
        }).observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', decorate);
        window.addEventListener('beforeprint', () => { printing = true; restore(); });
        window.addEventListener('afterprint', () => { printing = false; decorate(); });
        decorate();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
