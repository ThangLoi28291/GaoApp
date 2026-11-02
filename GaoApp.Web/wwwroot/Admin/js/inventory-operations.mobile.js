/* UI-12: presentation only; existing transfer and issue commands keep their original owners. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('[data-stock-transfer-index], .st-page, .inventory-issue-index-page, .inventory-issue-detail-page');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
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
                { key: 'header', label: 'Thông tin', node: root.querySelector('#TransferDocumentNo')?.closest('.st-card') },
                { key: 'lookup', label: 'Thêm hàng', node: root.querySelector('#QuickTransferLookupInput')?.closest('.st-card') },
                { key: 'lines', label: 'Dòng chuyển', node: root.querySelector('#stockTransferLineTableBody')?.closest('.st-card') }
            ].filter(section => section.node && !section.node.closest('[hidden], .d-none'));
        }
        function decorateNavigation() {
            if (!root.classList.contains('st-page') || navigation) return;
            const items = sections();
            if (items.length < 2) return;
            navigation = document.createElement('nav'); navigation.className = 'inventory-phone-sections';
            navigation.setAttribute('data-inventory-phone-navigation', 'true');
            navigation.setAttribute('aria-label', 'Đi đến nội dung phiếu chuyển kho');
            for (const item of items) {
                const button = document.createElement('button'); button.type = 'button'; button.textContent = item.label;
                button.dataset.inventoryPhoneJump = item.key;
                button.addEventListener('click', () => sections().find(section => section.key === item.key)?.node.scrollIntoView({
                    block: 'start', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth'
                }));
                navigation.append(button);
            }
            items[0].node.before(navigation);
        }
        function decorateTables() {
            const transfer = root.querySelector('#stockTransferLineTableBody')?.closest('table');
            const tables = [...root.querySelectorAll('.issue-lines-table')];
            if (transfer) tables.push(transfer);
            for (const table of tables) {
                if (!table.tHead?.rows[0]) continue;
                const headers = Array.from(table.tHead.rows[0].cells, cell => cell.textContent.trim().replace(/^Factor$/, 'Hệ số quy đổi'));
                mark(table, 'data-inventory-phone-table', 'true'); mark(table, 'role', 'table');
                mark(table.tHead, 'role', 'rowgroup');
                for (const row of table.tHead.rows) { mark(row, 'role', 'row'); for (const cell of row.cells) mark(cell, 'role', 'columnheader'); }
                for (const body of table.tBodies) {
                    mark(body, 'role', 'rowgroup');
                    for (const row of body.rows) {
                        mark(row, 'role', 'row'); let column = 0;
                        for (const cell of row.cells) {
                            const label = cell.colSpan > 1 ? '' : headers[column] || 'Thao tác';
                            mark(cell, 'role', 'cell'); mark(cell, 'data-inventory-phone-label', label);
                            if (cell.colSpan > 1 || /Sản phẩm|Ghi chú|Tác vụ|Thao tác/.test(label)) mark(cell, 'data-inventory-phone-wide', 'true');
                            column += Math.max(1, cell.colSpan);
                        }
                    }
                }
            }
        }
        function decorate() {
            if (!phone.matches) { restore(); return; }
            mark(document.body, 'data-inventory-phone', 'true'); mark(root, 'data-inventory-phone-root', 'true');
            decorateTables(); decorateNavigation();
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
