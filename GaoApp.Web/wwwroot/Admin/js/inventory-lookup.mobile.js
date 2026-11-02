/* UI-13: phone presentation of the existing inquiry and ledger renderers. */
(function () {
    'use strict';

    function start() {
        const root = document.querySelector('[data-inventory-inquiry-index], [data-inventory-ledger-index]');
        if (!root) return;

        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map();
        const kind = root.hasAttribute('data-inventory-inquiry-index') ? 'inquiry' : 'ledger';
        const header = document.getElementById(kind === 'inquiry' ? 'inventoryReloadButton' : 'ledgerReloadButton')?.closest('.gds-page-header');

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
            mark(document.body, 'data-inventory-lookup-phone', kind);
            mark(root, 'data-lookup-phone-root', kind);
            mark(header, 'data-lookup-phone-header', kind);
        }

        phone.addEventListener('change', apply);
        apply();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();
