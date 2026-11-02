/* UI-11: presentation of the existing receipt workspace; financial owners stay in place. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('[data-stock-document-index], .sd-receipt-page, #splitReceiptWorkspace');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map();
        let navigation = null, filtersButton = null;
        const links = new Map();
        function mark(node, name, value) {
            let attrs = originals.get(node);
            if (!attrs) { attrs = new Map(); originals.set(node, attrs); }
            if (!attrs.has(name)) attrs.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            navigation?.remove(); navigation = null; links.clear(); filtersButton?.remove(); filtersButton = null;
            for (const [node, attrs] of originals) for (const [name, value] of attrs) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function targets() {
            const goods = root.querySelector('.sd-manager-goods, #stockDocumentLinesContainer, .sd-line-table');
            const issue = root.querySelector('#receiptRevisionRequest:not([hidden]), #receiptIntakePending:not([hidden]), #managerExceptionQueue:not([hidden]), #receiptApprovalIssuesDisclosure');
            const pricing = root.querySelector('#receiptPricingAllocation');
            const documents = root.querySelector('#inputInvoicePickerSection, #receiptInvoiceFollowUp') || document.getElementById('purchaseReceiptAuditCard');
            return [
                { key: 'goods', label: 'Hàng nhận', node: goods, tab: 'goods' },
                { key: 'issues', label: 'Cần xử lý', node: issue, tab: issue?.closest('[data-workbench-panel]')?.dataset.workbenchPanel },
                { key: 'pricing', label: 'Thương mại', node: pricing, tab: 'pricing' },
                { key: 'documents', label: 'Chứng từ', node: documents }
            ].filter(item => item.node && !item.node.hidden && !item.node.classList.contains('d-none'));
        }
        function jump(key) {
            const target = targets().find(item => item.key === key);
            if (!target) return;
            // Use the original native tab owner, without storing a new tab/draft state.
            if (target.tab && typeof window.activateStockDocumentWorkbenchTab === 'function') window.activateStockDocumentWorkbenchTab(target.tab, false);
            for (let node = target.node; node && node !== document.body; node = node.parentElement) {
                if (node instanceof HTMLDetailsElement) node.open = true;
            }
            if (target.node.closest('[hidden], .d-none')) return;
            target.node.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' });
        }
        function decorateNavigation() {
            if (!root.classList.contains('sd-receipt-page')) return;
            const items = targets();
            if (items.length < 2) { navigation?.remove(); navigation = null; links.clear(); return; }
            if (!navigation) {
                navigation = document.createElement('nav');
                navigation.className = 'receipt-phone-sections';
                navigation.setAttribute('data-receipt-phone-navigation', 'true');
                navigation.setAttribute('aria-label', 'Đi đến nội dung phiếu nhập');
                const header = root.querySelector('.sd-review-header') || root.querySelector('.sd-card');
                if (header) header.after(navigation); else root.prepend(navigation);
            }
            for (const [key, button] of links) if (!items.some(item => item.key === key)) { button.remove(); links.delete(key); }
            for (const item of items) if (!links.has(item.key)) {
                const button = document.createElement('button'); button.type = 'button';
                button.textContent = item.label; button.dataset.receiptPhoneJump = item.key;
                button.addEventListener('click', () => jump(item.key));
                navigation.append(button); links.set(item.key, button);
            }
        }
        function decorateTables() {
            // Financial XML, selling-price and bill/program tables retain their own layouts.
            root.querySelectorAll('.sd-line-table, #splitReceiptWorkspace table').forEach(table => {
                if (!table.tHead?.rows[0]) return;
                mark(table, 'data-receipt-phone-table', 'true'); mark(table, 'role', 'table');
                const headers = Array.from(table.tHead.rows[0].cells, cell => cell.textContent.trim().replace(/BaseQuantity/g, 'số lượng gốc'));
                mark(table.tHead, 'role', 'rowgroup');
                for (const row of table.tHead.rows) { mark(row, 'role', 'row'); for (const cell of row.cells) mark(cell, 'role', 'columnheader'); }
                for (const body of table.tBodies) {
                    mark(body, 'role', 'rowgroup');
                    for (const row of body.rows) {
                        mark(row, 'role', 'row');
                        for (const [index, cell] of Array.from(row.cells).entries()) {
                            mark(cell, 'role', 'cell');
                            mark(cell, 'data-receipt-phone-label', cell.colSpan > 1 ? '' : headers[index] || '');
                            if (cell.colSpan > 1 || /Sản phẩm|Hàng hóa|Dòng nguồn|Thao tác/.test(headers[index] || '')) mark(cell, 'data-receipt-phone-wide', 'true');
                        }
                    }
                }
            });
        }
        function decorateFilters() {
            if (!root.hasAttribute('data-stock-document-index')) return;
            const bar = root.querySelector('.gds-filter-bar');
            if (!bar) return;
            if (!filtersButton) {
                filtersButton = document.createElement('button'); filtersButton.type = 'button';
                filtersButton.className = 'receipt-phone-filter-toggle';
                filtersButton.addEventListener('click', () => {
                    mark(root, 'data-receipt-phone-filters-open', root.getAttribute('data-receipt-phone-filters-open') === 'true' ? 'false' : 'true');
                    decorateFilters();
                });
                const search = bar.querySelector('.gds-filter-bar__search');
                if (search) search.after(filtersButton); else bar.prepend(filtersButton);
                mark(root, 'data-receipt-phone-filters-open', 'false');
            }
            const expanded = root.getAttribute('data-receipt-phone-filters-open') === 'true';
            filtersButton.setAttribute('aria-expanded', String(expanded));
            const labels = ['sdStatusFilter','sdInvoiceFilter'].map(id => document.getElementById(id)?.selectedOptions[0]?.textContent.trim()).filter(Boolean);
            const label = expanded ? 'Thu gọn bộ lọc' : 'Bộ lọc · ' + labels.join(' · ');
            if (filtersButton.textContent !== label) filtersButton.textContent = label;
        }
        function decorate() {
            if (!phone.matches) { restore(); return; }
            mark(document.body, 'data-receipt-phone', 'true'); mark(root, 'data-receipt-phone-root', 'true');
            decorateTables(); decorateNavigation(); decorateFilters();
            for (const node of originals.keys()) if (!node.isConnected) originals.delete(node);
        }
        let queued = false;
        const observer = new MutationObserver(() => {
            if (!phone.matches || queued) return;
            queued = true; requestAnimationFrame(() => { queued = false; decorate(); });
        });
        observer.observe(root, { childList: true, subtree: true });
        root.addEventListener('change', event => { if (phone.matches && ['sdStatusFilter','sdInvoiceFilter','sdPageSize'].includes(event.target.id)) decorateFilters(); });
        phone.addEventListener('change', decorate); decorate();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
}());
