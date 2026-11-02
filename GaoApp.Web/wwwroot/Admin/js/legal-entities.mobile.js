/* UI-13: phone presentation for existing legal entity and reconciliation owners. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('#legalEntityPage, .le-page');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
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
            for (const table of root.querySelectorAll('#legalEntityTable, .le-table')) {
                const head = table.tHead;
                if (!head?.rows[0]) continue;
                const labels = Array.from(head.rows[0].cells, cell => cell.textContent.trim());
                mark(table, 'data-legal-phone-table', 'true'); mark(table, 'role', 'table');
                mark(head, 'role', 'rowgroup');
                for (const row of head.rows) {
                    mark(row, 'role', 'row');
                    for (const cell of row.cells) mark(cell, 'role', 'columnheader');
                }
                for (const body of table.tBodies) {
                    mark(body, 'role', 'rowgroup');
                    for (const row of body.rows) {
                        mark(row, 'role', 'row'); let column = 0;
                        for (const cell of row.cells) {
                            const label = cell.colSpan > 1 ? '' : labels[column] || '';
                            mark(cell, 'role', 'cell'); mark(cell, 'data-legal-phone-label', label);
                            if (cell.colSpan > 1 || column === 0 || (table.id === 'legalEntityTable'
                                ? /^(HKD|Thông tin pháp lý|Kho bán mặc định|Hóa đơn điện tử|Thao tác)$/.test(label)
                                : /^(HKD|Ngày chốt|Kết quả)$/.test(label))) mark(cell, 'data-legal-phone-wide', 'true');
                            column += Math.max(1, cell.colSpan);
                        }
                    }
                }
            }
        }
        function sections() {
            const fields = [
                ['legalEntityCode', 'Nhận diện hộ kinh doanh'],
                ['legalEntityLegalName', 'Thông tin pháp lý'],
                ['legalEntityDefaultWarehouse', 'Kho và hóa đơn'],
                ['legalEntityDefaultPurchase', 'Thiết lập nhập hàng']
            ];
            for (const [id, label] of fields) {
                const field = document.getElementById(id);
                const column = field?.closest('#legalEntityForm .row > div');
                mark(column, 'data-legal-phone-section', label);
            }
        }
        function decorate() {
            if (!phone.matches) { restore(); return; }
            mark(document.body, 'data-legal-phone', 'true'); mark(root, 'data-legal-phone-root', 'true');
            tables(); sections();
            for (const node of originals.keys()) if (!node.isConnected) originals.delete(node);
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
