/* UI-14: presentation only; product, variant, price and barcode owners stay native. */
(function () {
    'use strict';

    function start() {
        const root = document.querySelector('[data-product-page], .product-setup-workspace, .p-main');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map();
        const kind = root.matches('[data-product-page]') ? 'list' : root.matches('.product-setup-workspace') ? 'setup' : 'detail';
        let filterGroup = null;
        let filterAnchor = null;
        let filterNode = null;
        let sectionNav = null;
        let scheduled = false;

        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }

        function restore() {
            if (filterAnchor && filterNode) filterAnchor.replaceWith(filterNode);
            filterGroup?.remove();
            filterGroup = null;
            filterAnchor = null;
            filterNode = null;
            sectionNav?.remove();
            sectionNav = null;
            for (const [node, attributes] of originals) {
                for (const [name, value] of attributes) {
                    if (value === null) node.removeAttribute(name);
                    else node.setAttribute(name, value);
                }
            }
            originals.clear();
        }

        function filters() {
            if (kind !== 'list' || filterGroup) return;
            filterNode = root.querySelector('.product-extra-filters');
            if (!filterNode) return;
            filterAnchor = document.createComment('Original product filters position');
            filterNode.before(filterAnchor);
            filterGroup = document.createElement('details');
            filterGroup.setAttribute('data-product-phone-filters', '');
            const summary = document.createElement('summary');
            summary.textContent = 'Bộ lọc khác';
            filterGroup.append(summary);
            filterNode.before(filterGroup);
            filterGroup.append(filterNode);
        }

        function sections() {
            if (kind !== 'setup' || sectionNav) return;
            const titles = root.querySelectorAll('.gm-section-title');
            const items = [
                [titles[0], 'Thông tin'],
                [titles[1], 'Giá bán'],
                [root.querySelector('.gm-pond-wrap')?.closest('.gm-card'), 'Hình ảnh'],
                [root.querySelector('#variants'), 'Biến thể']
            ].filter(item => item[0]);
            if (!items.length) return;
            sectionNav = document.createElement('nav');
            sectionNav.setAttribute('data-product-phone-sections', '');
            sectionNav.setAttribute('aria-label', 'Các phần thiết lập sản phẩm');
            items.forEach(([node, label], index) => {
                if (!node.id) mark(node, 'id', 'product-phone-section-' + index);
                mark(node, 'data-product-phone-section', '');
                const link = document.createElement('a');
                link.setAttribute('href', '#' + node.id);
                link.textContent = label;
                sectionNav.append(link);
            });
            root.querySelector('#productSetupForm')?.before(sectionNav);
        }

        function tables() {
            document.querySelectorAll('.uc-table, .bc-table, #productQuickViewModal .product-unit-table').forEach(table => {
                mark(table, 'data-product-phone-table', '');
                mark(table, 'role', 'table');
                const headings = Array.from(table.querySelectorAll('thead th'), cell => cell.textContent.trim());
                table.querySelectorAll('thead, tbody').forEach(group => mark(group, 'role', 'rowgroup'));
                table.querySelectorAll('tr').forEach(row => mark(row, 'role', 'row'));
                table.querySelectorAll('th').forEach(cell => mark(cell, 'role', 'columnheader'));
                table.querySelectorAll('tbody tr').forEach(row => {
                    Array.from(row.cells).forEach((cell, index) => {
                        mark(cell, 'role', 'cell');
                        if (cell.colSpan <= 1) mark(cell, 'data-product-phone-label', headings[index] || 'Thông tin');
                    });
                });
            });
        }

        function apply() {
            scheduled = false;
            if (!phone.matches) { restore(); return; }
            mark(document.body, 'data-products-phone', kind);
            mark(root, 'data-product-phone-root', kind);
            const header = kind === 'list'
                ? document.querySelector('.gds-page-header')
                : document.querySelector('.content-wrapper .content-header, .content-wrapper h4');
            if (header && !root.contains(header)) mark(header, 'data-product-phone-header', '');
            if (kind === 'detail') mark(root.closest('.row'), 'data-product-phone-gallery', '');
            filters();
            sections();
            tables();
        }

        new MutationObserver(() => {
            if (!phone.matches || scheduled) return;
            scheduled = true;
            requestAnimationFrame(apply);
        }).observe(document.querySelector('.content-wrapper') || root, { childList: true, subtree: true });
        phone.addEventListener('change', apply);
        apply();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();
