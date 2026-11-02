/* UI-13: presentation of existing warehouse controls; native permissions and commands remain owners. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('[data-warehouse-management-index]');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        let filters = null, positions = [];
        function mark(node, name, value) {
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
            const bar = root.querySelector('.warehouse-filter-bar');
            const controls = bar ? [...bar.children].filter(node => node.classList.contains('gds-filter-control')) : [];
            if (!controls.length) return;
            filters = document.createElement('details'); filters.className = 'warehouse-phone-filters';
            filters.setAttribute('data-warehouse-phone-filters', 'true');
            const summary = document.createElement('summary'); summary.textContent = 'Bộ lọc kho';
            const fields = document.createElement('div'); fields.className = 'warehouse-phone-filter-fields';
            filters.append(summary, fields); controls[0].before(filters);
            for (const node of controls) {
                const anchor = document.createComment('UI-13 original filter position');
                node.before(anchor); positions.push({ node, anchor }); fields.append(node);
            }
        }
        function decorate() {
            if (!phone.matches) { restore(); return; }
            mark(document.body, 'data-warehouse-phone', 'true'); mark(root, 'data-warehouse-phone-root', 'true');
            decorateFilters();
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
