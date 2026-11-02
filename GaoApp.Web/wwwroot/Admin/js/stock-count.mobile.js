/* UI-12: phone presentation of the original stock-count controls and quantities. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('[data-stock-count-index], .scd-page');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map();
        let navigation = null;

        function mark(node, name, value) {
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            navigation?.remove(); navigation = null;
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function sections() {
            return [
                { key: 'header', label: 'Thông tin', node: root.querySelector('#DocumentNo')?.closest('.scd-card') },
                { key: 'lookup', label: 'Nhập kiểm kê', node: root.querySelector('#QuickLookupInput')?.closest('.scd-card') },
                { key: 'lines', label: 'Dòng kiểm kê', node: root.querySelector('#stockCountLineTableBody')?.closest('.scd-card') },
                { key: 'grouped', label: 'Tổng hợp', node: root.querySelector('#stockCountGroupedBody')?.closest('.scd-card') }
            ].filter(item => item.node && !item.node.closest('[hidden], .d-none'));
        }
        function decorateSections() {
            if (!root.classList.contains('scd-page')) return;
            const items = sections();
            for (const item of items) mark(item.node, 'data-count-phone-section', item.key);
            if (items.length < 2 || navigation) return;
            navigation = document.createElement('nav');
            navigation.className = 'count-phone-sections';
            navigation.setAttribute('data-count-phone-navigation', 'true');
            navigation.setAttribute('aria-label', 'Đi đến nội dung phiếu kiểm kê');
            for (const item of items) {
                const button = document.createElement('button');
                button.type = 'button'; button.textContent = item.label;
                button.dataset.countPhoneJump = item.key;
                button.addEventListener('click', () => {
                    const target = sections().find(section => section.key === item.key)?.node;
                    if (!target) return;
                    target.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' });
                });
                navigation.append(button);
            }
            items[0].node.before(navigation);
        }
        function decorateTables() {
            root.querySelectorAll('.scd-table').forEach(table => {
                const headerRow = table.tHead?.rows[0];
                if (!headerRow) return;
                const headers = Array.from(headerRow.cells, cell => cell.textContent.trim());
                const lineTable = table.querySelector('#stockCountLineTableBody') !== null;
                const kinds = lineTable
                    ? ['sequence', 'product', 'unit', 'factor', 'system', 'counted', 'base', 'difference', 'conclusion', 'note', 'actions']
                    : ['product', 'system', 'base', 'difference', 'conclusion', 'units'];
                mark(table, 'data-count-phone-table', 'true'); mark(table, 'role', 'table');
                mark(table.tHead, 'role', 'rowgroup');
                for (const row of table.tHead.rows) {
                    mark(row, 'role', 'row');
                    for (const cell of row.cells) mark(cell, 'role', 'columnheader');
                }
                for (const body of table.tBodies) {
                    mark(body, 'role', 'rowgroup');
                    for (const row of body.rows) {
                        mark(row, 'role', 'row');
                        let column = 0;
                        for (const cell of row.cells) {
                            mark(cell, 'role', 'cell');
                            mark(cell, 'data-count-phone-label', cell.colSpan > 1 ? '' : headers[column] || 'Thao tác');
                            mark(cell, 'data-count-phone-kind', cell.colSpan > 1 ? 'empty' : kinds[column] || 'value');
                            column += Math.max(1, cell.colSpan);
                        }
                    }
                }
            });
        }
        function decorate() {
            if (!phone.matches) { restore(); return; }
            mark(document.body, 'data-stock-count-phone', 'true');
            mark(root, 'data-count-phone-root', 'true');
            decorateSections(); decorateTables();
            for (const node of originals.keys()) if (!node.isConnected) originals.delete(node);
        }
        let queued = false;
        const observer = new MutationObserver(() => {
            if (!phone.matches || queued) return;
            queued = true;
            requestAnimationFrame(() => { queued = false; decorate(); });
        });
        observer.observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', decorate); decorate();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
}());
