/* UI-18: presentation only. Native report loading, calculations, charts, permissions and financial commands retain ownership. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('[data-management-report]:not([data-operations-report]), [data-sales-executive-report]');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map(), dialogMoves = new Map();
        const kind = root.matches('[data-sales-executive-report]') ? 'sales' : 'management';
        let printing = false, queued = false;
        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            for (const [form, move] of dialogMoves) {
                // Put the same native nodes back in their original order and original form.
                for (const node of [...move.scroll.childNodes]) form.insertBefore(node, move.scroll);
                move.scroll.remove();
            }
            dialogMoves.clear();
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function dialogs() {
            root.querySelectorAll('[data-expense-form], [data-expense-action-form]').forEach(form => {
                if (dialogMoves.has(form)) return;
                const heading = form.querySelector(':scope > .mr-dialog-heading'), actions = form.querySelector(':scope > .mr-dialog-actions');
                if (!heading || !actions) return;
                const scroll = document.createElement('div');
                scroll.className = 'rwm-dialog-scroll';
                scroll.setAttribute('data-reports-workspace-phone-scroll', '');
                const middle = [];
                for (let node = heading.nextSibling; node && node !== actions; node = node.nextSibling) middle.push(node);
                form.insertBefore(scroll, actions);
                middle.forEach(node => scroll.append(node));
                dialogMoves.set(form, { scroll });
            });
        }
        function tables() {
            root.querySelectorAll('.mr-table, .sales-report-chart-data table').forEach(table => {
                // Keep native interactive headers as a scrollable table rather than hiding their controls.
                if (table.tHead?.querySelector('input,select,button,a,textarea')) return;
                const headers = [];
                [...(table.tHead?.rows || [])].forEach(row => [...row.cells].forEach(cell => {
                    for (let index = 0; index < Math.max(1, cell.colSpan); index++) headers.push(cell.textContent.trim().replace(/\s+/g, ' '));
                }));
                if (!headers.length) return;
                mark(table, 'data-reports-workspace-phone-table', ''); mark(table, 'role', 'table');
                if (table.tHead) mark(table.tHead, 'role', 'rowgroup');
                [...table.tBodies].forEach(body => {
                    mark(body, 'role', 'rowgroup');
                    const occupied = [];
                    [...body.rows].forEach(row => {
                        mark(row, 'role', 'row');
                        let column = 0;
                        [...row.cells].forEach(cell => {
                            while (occupied[column] > 0) column++;
                            mark(cell, 'role', cell.tagName === 'TH' ? 'rowheader' : 'cell');
                            if (headers[column]) mark(cell, 'data-reports-workspace-phone-label', headers[column]);
                            if (column === 0) mark(cell, 'data-reports-workspace-phone-first', '');
                            if (column === 0 || cell.colSpan > 1 || cell.rowSpan > 1 || cell.querySelector('form,button,a.mr-button')) mark(cell, 'data-reports-workspace-phone-wide', '');
                            for (let index = 0; index < Math.max(1, cell.colSpan); index++) occupied[column + index] = Math.max(occupied[column + index] || 0, cell.rowSpan);
                            column += Math.max(1, cell.colSpan);
                        });
                        for (let index = 0; index < occupied.length; index++) occupied[index] = Math.max(0, (occupied[index] || 0) - 1);
                    });
                });
            });
        }
        function paint() {
            if (printing || !phone.matches) { restore(); return; }
            mark(document.body, 'data-reports-workspace-phone', kind);
            mark(root, 'data-reports-workspace-phone-root', kind);
            if (kind === 'sales') mark(document.querySelector('.sales-report-page-header'), 'data-reports-workspace-phone-heading', '');
            tables();
            dialogs();
        }
        function queue() {
            if (queued || printing) return;
            queued = true;
            requestAnimationFrame(() => { queued = false; paint(); });
        }
        // Native rendering may replace report panels. No attributes, field values, requests or commands are observed or changed.
        new MutationObserver(queue).observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', paint);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; paint(); });
        paint();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
