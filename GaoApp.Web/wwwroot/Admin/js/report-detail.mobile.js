/* UI-18: presentation only. Native report queries, quality, amounts and commands retain ownership. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('[data-sales-detail-report], [data-profit-report][data-mode="detail"], [data-profit-report][data-mode="activity"]');
        if (!root) return;
        const kind = root.hasAttribute('data-sales-detail-report') ? 'sales' : root.dataset.mode === 'activity' ? 'cost-activity' : 'profit-detail';
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map();
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
            for (const [node, attributes] of originals) restoreNode(node, attributes);
            originals.clear();
        }
        function decorateTable(table) {
            mark(table, 'data-report-detail-phone-table', '');
            mark(table, 'role', 'table');
            const headers = [];
            const headSlots = [];
            [...(table.tHead?.rows || [])].forEach((row, rowIndex) => {
                let column = 0;
                [...row.cells].forEach(cell => {
                    while (headSlots[rowIndex]?.[column]) column++;
                    const label = cell.textContent.trim().replace(/\s+/g, ' ');
                    for (let offset = 0; offset < Math.max(1, cell.colSpan); offset++) {
                        if (label) headers[column + offset] = label;
                        for (let down = 0; down < Math.max(1, cell.rowSpan); down++) {
                            const slots = headSlots[rowIndex + down] || (headSlots[rowIndex + down] = []);
                            slots[column + offset] = true;
                        }
                    }
                    column += Math.max(1, cell.colSpan);
                });
            });
            [...table.tBodies].forEach(body => {
                const occupied = [];
                [...body.rows].forEach(row => {
                    mark(row, 'role', 'row');
                    let column = 0;
                    [...row.cells].forEach(cell => {
                        while (occupied[column] > 0) column++;
                        mark(cell, 'role', cell.tagName === 'TH' ? 'rowheader' : 'cell');
                        const action = cell.querySelector('a, button, input, select, textarea');
                        const label = headers[column] || (action ? 'Thao tác' : '');
                        if (label) mark(cell, 'data-report-detail-phone-label', label);
                        if (column === 0) mark(cell, 'data-report-detail-phone-lead', '');
                        if (column === 0 || cell.colSpan > 1 || cell.rowSpan > 1 || action || column === headers.length - 1 && headers.length > 6) mark(cell, 'data-report-detail-phone-wide', '');
                        for (let offset = 0; offset < Math.max(1, cell.colSpan); offset++) occupied[column + offset] = Math.max(occupied[column + offset] || 0, cell.rowSpan);
                        column += Math.max(1, cell.colSpan);
                    });
                    for (let columnIndex = 0; columnIndex < occupied.length; columnIndex++) occupied[columnIndex] = Math.max(0, (occupied[columnIndex] || 0) - 1);
                });
            });
        }
        function paint() {
            if (printing || !phone.matches) { restore(); return; }
            for (const [node, attributes] of originals) {
                if (!node.isConnected) { restoreNode(node, attributes); originals.delete(node); }
            }
            mark(document.body, 'data-report-detail-phone', kind);
            mark(root, 'data-report-detail-phone-root', kind);
            if (kind === 'sales') {
                mark(document.querySelector('.sales-report-page-header'), 'data-report-detail-phone-heading', '');
                [['#salesDetailSearch', 'Tìm trong báo cáo bán hàng'], ['#salesDetailSort', 'Thứ tự chi tiết bán hàng']].forEach(([selector, label]) => {
                    const control = root.querySelector(selector);
                    if (control && !control.hasAttribute('aria-label')) mark(control, 'aria-label', label);
                });
            }
            root.querySelectorAll('#salesDetailTableWrap > table, [data-profit-trend-table], [data-profit-detail-table]').forEach(decorateTable);
        }
        function queue() {
            if (queued || printing) return;
            queued = true;
            requestAnimationFrame(() => { queued = false; paint(); });
        }
        new MutationObserver(queue).observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', paint);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; paint(); });
        paint();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
