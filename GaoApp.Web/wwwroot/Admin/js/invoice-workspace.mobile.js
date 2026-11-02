/* UI-16: presentation only; the existing invoice owners retain every field and command. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('#invoiceDetailPage,.invoice-index,.container-fluid:has(> .dashboard-card)');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        const kind = root.id === 'invoiceDetailPage' ? 'detail' : root.classList.contains('invoice-index') ? 'index' : 'dashboard';
        let filters = null, positions = [], sections = null, queued = false, printing = false;
        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            for (const { node, anchor } of positions) if (anchor.isConnected) anchor.replaceWith(node);
            positions = []; filters?.remove(); filters = null; sections?.remove(); sections = null;
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function indexFilters() {
            if (kind !== 'index' || filters?.isConnected) return;
            const main = root.querySelector('.invoice-filter__main'), options = root.querySelector('.invoice-filter__options');
            if (!main || !options) return;
            const fields = [...main.children].slice(1).concat([...options.children].filter(node => !node.classList.contains('invoice-filter__actions')));
            filters = document.createElement('details'); filters.className = 'invoice-phone-filters';
            const summary = document.createElement('summary'); summary.textContent = 'Ngày, trạng thái và sắp xếp';
            const body = document.createElement('div'); body.className = 'invoice-phone-filter-fields';
            filters.append(summary, body); main.after(filters);
            for (const node of fields) {
                const anchor = document.createComment('UI-16 original invoice filter position');
                node.before(anchor); positions.push({ node, anchor }); body.append(node);
            }
        }
        function detailSections() {
            if (kind !== 'detail' || sections?.isConnected) return;
            const summary = root.querySelector('#invoiceSummaryBox');
            const buyer = summary?.nextElementSibling;
            const auto = root.querySelector('table:has(.invoice-stock-heading)')?.closest('.invoice-card');
            const targets = [[summary, 'Tổng quan'], [buyer, 'Người mua'], [auto, 'Dòng từ đơn'], [root.querySelector('#manualLinesBox'), 'Thêm tay']].filter(([node]) => node);
            if (!targets.length) return;
            sections = document.createElement('nav'); sections.className = 'invoice-phone-sections';
            sections.setAttribute('aria-label', 'Các phần chi tiết hóa đơn');
            targets.forEach(([node, title], index) => {
                if (!node.id) mark(node, 'id', 'invoice-workspace-phone-section-' + index);
                mark(node, 'data-invoice-workspace-phone-section', '');
                const link = document.createElement('a'); link.setAttribute('href', '#' + node.id); link.textContent = title; sections.append(link);
            });
            root.querySelector('.page-header')?.after(sections);
        }
        function decorateTables() {
            const tables = new Set([...root.querySelectorAll('table'), ...document.querySelectorAll('#invoiceOrderDraftModal table,#manualDetailModal table')]);
            for (const table of tables) {
                if (!table.querySelector('thead')) continue;
                mark(table, 'data-invoice-workspace-phone-table', ''); mark(table, 'role', 'table');
                const headings = [], occupied = [];
                [...table.tHead.rows].forEach((row, rowIndex) => {
                    occupied[rowIndex] ||= [];
                    let column = 0;
                    for (const cell of row.cells) {
                        while (occupied[rowIndex][column]) column++;
                        for (let offset = 0; offset < cell.colSpan; offset++) {
                            headings[column + offset] = cell.textContent.trim() || headings[column + offset] || 'Thao tác';
                            for (let span = 0; span < cell.rowSpan; span++) { occupied[rowIndex + span] ||= []; occupied[rowIndex + span][column + offset] = true; }
                        }
                        column += cell.colSpan;
                    }
                });
                table.querySelectorAll('thead,tbody,tfoot').forEach(node => mark(node, 'role', 'rowgroup'));
                table.querySelectorAll('tr').forEach(node => mark(node, 'role', 'row'));
                table.querySelectorAll('th').forEach(node => mark(node, 'role', 'columnheader'));
                for (const body of table.tBodies) {
                    const spans = [];
                    for (const row of body.rows) {
                        let column = 0;
                        for (const cell of row.cells) {
                            while (spans[column] > 0) column++;
                            mark(cell, 'role', 'cell');
                            if (cell.colSpan > 1) mark(cell, 'data-invoice-workspace-phone-wide', '');
                            else mark(cell, 'data-invoice-workspace-phone-label', headings[column] || 'Thông tin');
                            if (/SL|số lượng|đơn giá|thành tiền|tổng tiền|giá trị|VAT/i.test(headings[column] || '')) mark(cell, 'data-invoice-workspace-phone-money', '');
                            if (cell.querySelector('button,.btn') && cell === row.cells[row.cells.length - 1]) mark(cell, 'data-invoice-workspace-phone-actions', '');
                            for (let offset = 0; offset < cell.colSpan; offset++) if (cell.rowSpan > 1) spans[column + offset] = cell.rowSpan;
                            column += cell.colSpan;
                        }
                        for (let index = 0; index < spans.length; index++) if (spans[index] > 0) spans[index]--;
                    }
                }
            }
        }
        function apply() {
            queued = false;
            if (printing || !phone.matches) { restore(); return; }
            mark(document.body, 'data-invoice-workspace-phone', kind); mark(root, 'data-invoice-workspace-phone-root', kind);
            const header = kind === 'index' ? document.querySelector('.gds-page-header') : kind === 'detail' ? root.querySelector('.page-header') : root.firstElementChild;
            mark(header, 'data-invoice-workspace-phone-header', '');
            if (kind === 'dashboard') root.querySelectorAll(':scope > .row:has(.metric-card)').forEach(node => mark(node, 'data-invoice-workspace-phone-metrics', ''));
            indexFilters(); detailSections(); decorateTables();
        }
        new MutationObserver(() => {
            if (printing || !phone.matches || queued) return;
            queued = true; requestAnimationFrame(apply);
        }).observe(document.querySelector('.content-wrapper') || root, { childList: true, subtree: true });
        phone.addEventListener('change', apply);
        window.addEventListener('beforeprint', () => { printing = true; restore(); });
        window.addEventListener('afterprint', () => { printing = false; apply(); });
        apply();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
