/* UI-19: presentation only; the native promotion editor retains all commands and calculations. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('.promo-shell');
        if (!root) return;
        const editor = document.getElementById('promotionEditModal');
        const removal = document.getElementById('promotionDeleteModal');
        const heading = document.querySelector('.promo-page-header');
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map(), groups = [];
        let printing = false, queued = false, filterToggle = null, filtersOpen = false;

        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restoreAttributes(node, attributes) {
            for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
        }
        function groupFields(title, ids, before) {
            const nodes = ids.map(id => document.getElementById(id)?.closest('.row > div'));
            if (nodes.some(node => !node) || !before?.parentNode) return;
            const section = document.createElement('section');
            section.className = 'promo-section promo-phone-group';
            const label = document.createElement('h6');
            label.className = 'promo-section-title'; label.textContent = title;
            const row = document.createElement('div'); row.className = 'row g-3';
            section.append(label, row);
            const moved = nodes.map(node => {
                const anchor = document.createComment('UI-19 original promotion field');
                node.parentNode.insertBefore(anchor, node); row.appendChild(node);
                return { node, anchor };
            });
            before.parentNode.insertBefore(section, before);
            groups.push({ section, moved });
        }
        function restore() {
            filterToggle?.remove();
            filterToggle = null;
            for (const { section, moved } of groups) {
                for (const { node, anchor } of moved) {
                    if (anchor.parentNode) { anchor.parentNode.insertBefore(node, anchor); anchor.remove(); }
                }
                section.remove();
            }
            groups.length = 0;
            for (const [node, attributes] of originals) restoreAttributes(node, attributes);
            originals.clear();
        }
        function filters() {
            const search = document.getElementById('txtSearch')?.closest('.row > div');
            const row = search?.parentElement;
            if (!row) return;
            [...row.children].filter(node => node !== search && node !== filterToggle).forEach(node => {
                mark(node, 'data-promotion-phone-filter', '');
            });
            if (!filterToggle) {
                filterToggle = document.createElement('button');
                filterToggle.type = 'button';
                filterToggle.className = 'btn btn-outline-secondary promo-phone-filter-toggle';
                filterToggle.addEventListener('click', () => { filtersOpen = !filtersOpen; filters(); });
                row.appendChild(filterToggle);
            }
            const selected = ['ddlType', 'ddlIsActive', 'ddlCustomerTier'].map(id => document.getElementById(id))
                .filter(select => select?.value).map(select => select.selectedOptions[0]?.textContent.trim()).filter(Boolean);
            mark(root, 'data-promotion-phone-filters-open', String(filtersOpen));
            filterToggle.setAttribute('aria-expanded', String(filtersOpen));
            const label = [filtersOpen ? 'Ẩn bộ lọc' : 'Bộ lọc', ...selected].join(' · ');
            if (filterToggle.textContent !== label) filterToggle.textContent = label;
        }
        function tableCards(table) {
            mark(table, 'data-promotion-phone-table', ''); mark(table, 'role', 'table');
            const labels = [...(table.tHead?.rows[0]?.cells || [])].map(cell => cell.textContent.trim().replace(/\s+/g, ' '));
            if (table.tHead) mark(table.tHead, 'role', 'rowgroup');
            [...(table.tHead?.rows || [])].forEach(row => mark(row, 'role', 'row'));
            [...table.tBodies].forEach(body => {
                mark(body, 'role', 'rowgroup');
                [...body.rows].forEach(row => {
                    mark(row, 'role', 'row');
                    [...row.cells].forEach((cell, index) => {
                        mark(cell, 'role', 'cell');
                        if (labels[index]) mark(cell, 'data-promotion-phone-label', labels[index]);
                    });
                });
            });
        }
        function nameFields(scope) {
            scope?.querySelectorAll('input:not([type="hidden"]), select, textarea').forEach(control => {
                if (control.hasAttribute('aria-label') || control.hasAttribute('aria-labelledby')) return;
                const label = control.closest('.promo-rule-product-area, .promo-rule-unit-area, .promo-rule-qty-area, .row > div')?.querySelector('label');
                if (label && !label.contains(control)) mark(control, 'aria-label', label.textContent.trim().replace(/\s+/g, ' '));
            });
        }
        function paint() {
            if (printing || !phone.matches) { restore(); return; }
            for (const [node, attributes] of originals) {
                if (!node.isConnected) { restoreAttributes(node, attributes); originals.delete(node); }
            }
            [root, heading, editor, removal].forEach(node => mark(node, 'data-promotion-phone', ''));
            filters();
            if (!groups.length) {
                const before = editor?.querySelector('.promo-type-panel');
                groupFields('Điều kiện áp dụng', ['promoCustomerTier', 'promoPriority'], before);
                groupFields('Thời gian và trạng thái', ['promoStartAt', 'promoEndAt', 'promoIsActive'], before);
            }
            root.querySelectorAll('.promo-table').forEach(tableCards);
            nameFields(root); nameFields(editor);
            [[root.querySelector('#btnReload'), 'Tải lại danh sách khuyến mãi'],
             [root.querySelector('#btnClearSearch'), 'Xóa từ khóa tìm kiếm'],
             [editor?.querySelector('.btn-close'), 'Đóng biểu mẫu khuyến mãi']].forEach(([node, label]) => {
                if (node && !node.hasAttribute('aria-label')) mark(node, 'aria-label', label);
            });
            editor?.querySelectorAll('[data-remove-rule]').forEach(node => {
                if (!node.hasAttribute('aria-label')) mark(node, 'aria-label', 'Xóa dòng sản phẩm');
            });
        }
        function queue() {
            if (queued || printing) return;
            queued = true;
            requestAnimationFrame(() => { queued = false; paint(); });
        }
        const observer = new MutationObserver(queue);
        [root, editor].forEach(node => { if (node) observer.observe(node, { childList: true, subtree: true }); });
        phone.addEventListener('change', paint);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; paint(); });
        paint();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
