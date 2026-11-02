/* UI-14: presentation only; native catalog cards, forms and commands remain owners. */
(function () {
    'use strict';

    function start() {
        const root = document.querySelector('.catalog-editor, [data-layout-consistency]');
        if (!root) return;

        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map();
        const kind = root.matches('.catalog-editor') ? 'editor' : 'list';
        const header = kind === 'list' ? document.querySelector('.gds-page-header') : null;

        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            node.setAttribute(name, value);
        }

        function restore() {
            for (const [node, attributes] of originals) {
                for (const [name, value] of attributes) {
                    if (value === null) node.removeAttribute(name);
                    else node.setAttribute(name, value);
                }
            }
            originals.clear();
        }

        function apply() {
            if (!phone.matches) { restore(); return; }
            mark(document.body, 'data-catalog-foundation-phone', kind);
            mark(root, 'data-catalog-phone-root', kind);
            mark(header, 'data-catalog-phone-header', '');
        }

        phone.addEventListener('change', apply);
        apply();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();
