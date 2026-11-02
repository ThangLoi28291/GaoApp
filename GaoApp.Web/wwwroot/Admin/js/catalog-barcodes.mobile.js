/* UI-14: presentation only. Existing barcode filters, pagination and resolution commands own their data. */
(function () {
    'use strict';
    function start() {
        const root = document.getElementById('barcodeHistoryPage') || document.getElementById('barcodeRequestKeyword')?.closest('.container-xxl');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        let filters = null, positions = [];
        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            for (const { node, anchor } of positions) if (anchor.isConnected) anchor.replaceWith(node);
            positions = []; filters?.remove(); filters = null;
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function decorateFilters() {
            if (filters?.isConnected) return;
            const form = root.querySelector('#barcodeHistoryFilterForm');
            const fields = form ? [...form.children].filter(node => node.classList.contains('mb-3')).slice(1) : [];
            if (!fields.length) return;
            filters = document.createElement('details'); filters.className = 'barcode-phone-filters';
            const summary = document.createElement('summary'); summary.textContent = 'Loại thao tác và thời gian';
            const content = document.createElement('div'); content.className = 'barcode-phone-filter-fields';
            filters.append(summary, content); fields[0].before(filters);
            for (const node of fields) {
                const anchor = document.createComment('UI-14 original barcode filter position');
                node.before(anchor); positions.push({ node, anchor }); content.append(node);
            }
        }
        function decorateTable() {
            const table = root.querySelector('table');
            if (!table) return;
            mark(table, 'data-barcode-phone-table', 'true'); mark(table, 'role', 'table');
            const headers = [...table.querySelectorAll('thead th')].map(th => th.textContent.trim());
            table.querySelectorAll('thead, tbody').forEach(node => mark(node, 'role', 'rowgroup'));
            table.querySelectorAll('tr').forEach(row => mark(row, 'role', 'row'));
            table.querySelectorAll('thead th').forEach(node => mark(node, 'role', 'columnheader'));
            table.querySelectorAll('tbody tr').forEach(row => {
                const cells = [...row.children];
                cells.forEach((cell, index) => {
                    mark(cell, 'role', 'cell');
                    if (cell.colSpan > 1) { mark(cell, 'data-barcode-phone-wide', 'true'); return; }
                    mark(cell, 'data-barcode-phone-label', headers[index] === 'Factor' ? 'Hệ số quy đổi' : (headers[index] || ''));
                    if ([1, 4, 8].includes(index)) mark(cell, 'data-barcode-phone-wide', 'true');
                    if (index === 8) mark(cell, 'data-barcode-phone-actions', 'true');
                });
            });
        }
        function decorate() {
            if (!phone.matches) { restore(); return; }
            mark(document.body, 'data-catalog-barcodes-phone', 'true');
            mark(root, 'data-catalog-barcodes-phone-root', root.id === 'barcodeHistoryPage' ? 'history' : 'normalization');
            root.querySelectorAll('input:not([type="hidden"]), select, textarea').forEach(field => {
                if (field.hasAttribute('aria-label') || field.labels?.length) return;
                const label = field.closest('.mb-3')?.querySelector('label')?.textContent.trim();
                mark(field, 'aria-label', label || (field.id === 'barcodeRequestStatus' ? 'Trạng thái yêu cầu barcode' : 'Tìm sản phẩm, đơn vị, barcode'));
            });
            const modal = document.getElementById('barcodeResolveModal');
            if (modal) {
                mark(modal, 'aria-labelledby', 'barcodeResolveTitle');
                mark(modal.querySelector('.btn-close'), 'aria-label', 'Đóng');
                mark(modal.querySelector('#barcodeResolveManagerNote'), 'aria-label', 'Ghi chú quản lý');
            }
            decorateFilters(); decorateTable();
        }
        let queued = false;
        new MutationObserver(() => {
            if (!phone.matches || queued) return;
            queued = true; requestAnimationFrame(() => { queued = false; decorate(); });
        }).observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', decorate); decorate();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
}());
