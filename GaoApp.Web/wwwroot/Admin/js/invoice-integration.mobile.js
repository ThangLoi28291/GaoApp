/* UI-16: presentation only; provider settings, sanitized payloads and Viettel commands retain their native owners. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('.viettel-dashboard, .payload-page, .invoice-provider-page, .invoice-provider-form') || document.querySelector('form[action*="ViettelInvoiceSync" i]')?.closest('.container-fluid');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        const kind = root.matches('.viettel-dashboard') ? 'dashboard' : root.matches('.payload-page') ? 'payload' : root.matches('.invoice-provider-form') ? 'form' : root.matches('.invoice-provider-page') ? 'providers' : 'sync';
        let sections = null, printing = false, queued = false;
        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            sections?.remove(); sections = null;
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function formSections() {
            if (kind !== 'form' || sections) return;
            const cards = root.querySelectorAll('form > .row > .col-lg-8 > .setting-card');
            const targets = [[cards[0], 'Kết nối'], [cards[1], 'Hóa đơn'], [root.querySelector('form > .row > .col-lg-4 > .setting-card'), 'Đối chiếu']].filter(([node]) => node);
            if (!targets.length) return;
            sections = document.createElement('nav'); sections.className = 'invoice-integration-phone-sections';
            sections.setAttribute('aria-label', 'Các phần cấu hình nhà cung cấp hóa đơn');
            targets.forEach(([node, title], index) => {
                if (!node.id) mark(node, 'id', 'invoice-integration-phone-section-' + index);
                mark(node, 'data-invoice-integration-phone-section', '');
                const link = document.createElement('a'); link.href = '#' + node.id; link.textContent = title; sections.append(link);
            });
            root.querySelector('form')?.before(sections);
        }
        function tables() {
            root.querySelectorAll('table').forEach(table => {
                mark(table, 'data-invoice-integration-phone-table', ''); mark(table, 'role', 'table');
                const grid = [];
                [...table.querySelectorAll('thead tr')].forEach((row, rowIndex) => {
                    grid[rowIndex] ||= []; let column = 0;
                    [...row.cells].forEach(cell => {
                        while (grid[rowIndex][column] !== undefined) column++;
                        const label = cell.textContent.trim();
                        for (let r = rowIndex; r < rowIndex + cell.rowSpan; r++) {
                            grid[r] ||= [];
                            for (let c = column; c < column + cell.colSpan; c++) grid[r][c] = label;
                        }
                        column += cell.colSpan;
                    });
                });
                const labels = grid[grid.length - 1] || [];
                table.querySelectorAll('thead,tbody,tfoot').forEach(node => mark(node, 'role', 'rowgroup'));
                table.querySelectorAll('tr').forEach(node => mark(node, 'role', 'row'));
                table.querySelectorAll('thead th').forEach(node => mark(node, 'role', 'columnheader'));
                const occupied = [];
                table.querySelectorAll('tbody tr').forEach(row => {
                    let column = 0;
                    [...row.cells].forEach(cell => {
                        while (occupied[column] > 0) column++;
                        mark(cell, 'role', cell.tagName === 'TH' ? 'rowheader' : 'cell');
                        if (cell.colSpan === 1) mark(cell, 'data-invoice-integration-phone-label', labels[column] || 'Thông tin');
                        if (column === 0 || cell.colSpan > 1) mark(cell, 'data-invoice-integration-phone-wide', '');
                        if (cell.querySelector('form, .action-stack, .provider-actions') || cell.matches('.provider-actions')) mark(cell, 'data-invoice-integration-phone-actions', '');
                        for (let c = column; c < column + cell.colSpan; c++) occupied[c] = Math.max(occupied[c] || 0, cell.rowSpan);
                        column += cell.colSpan;
                    });
                    for (let c = 0; c < occupied.length; c++) occupied[c] = Math.max(0, (occupied[c] || 0) - 1);
                });
            });
        }
        function apply() {
            queued = false;
            if (printing || !phone.matches) { restore(); return; }
            mark(document.body, 'data-invoice-integration-phone', kind);
            mark(root, 'data-invoice-integration-phone-root', kind);
            mark(root.querySelector(':scope > .d-flex'), 'data-invoice-integration-phone-header', '');
            if (kind === 'payload') mark(root.parentElement?.querySelector('a[href="/admin/invoice-input-stock"]'), 'data-invoice-integration-phone-leading', '');
            formSections(); tables();
        }
        new MutationObserver(() => {
            if (printing || !phone.matches || queued) return;
            queued = true; requestAnimationFrame(apply);
        }).observe(root, {childList:true,subtree:true});
        phone.addEventListener('change', apply);
        window.addEventListener('beforeprint', () => { printing = true; restore(); });
        window.addEventListener('afterprint', () => { printing = false; apply(); });
        apply();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, {once:true}); else start();
})();
