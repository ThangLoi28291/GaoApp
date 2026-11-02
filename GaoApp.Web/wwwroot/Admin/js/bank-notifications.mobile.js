/* UI-17: phone presentation only; native live snapshots and approval owners retain their controls. */
(function () {
    'use strict';
    function start() {
        const root = document.getElementById('bankNotificationLiveStatus')?.closest('.container-fluid');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        let printing = false, scrollBox = null, modalNodes = null, modalBody = null;
        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            if (scrollBox && modalBody && modalNodes) {
                for (const node of modalNodes) modalBody.appendChild(node);
                scrollBox.remove(); scrollBox = null; modalNodes = null;
            }
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function approvalSheet() {
            if (scrollBox) return;
            modalBody = document.querySelector('#adminBankApprovalModal .modal-body');
            const title = modalBody?.querySelector(':scope > h2');
            const footer = modalBody?.querySelector('#adminApprovalCancel')?.closest('.d-flex');
            if (!modalBody || !title || !footer || footer.parentElement !== modalBody) return;
            modalNodes = [...modalBody.childNodes];
            scrollBox = document.createElement('div'); scrollBox.className = 'bank-notifications-phone-modal-scroll';
            modalBody.insertBefore(scrollBox, title.nextSibling);
            for (const node of modalNodes) if (node !== title && node !== footer) scrollBox.appendChild(node);
            mark(title, 'data-bank-notifications-phone-modal-title', '');
            mark(footer, 'data-bank-notifications-phone-footer', '');
        }
        function decorate() {
            if (printing || !phone.matches) { restore(); return; }
            mark(document.body, 'data-bank-notifications-phone', '');
            mark(root, 'data-bank-notifications-phone-root', '');
            approvalSheet();
            // Keyed live rows intentionally receive no attributes or wrappers.
            // Native isEqualNode comparisons must still retain unchanged rows.
        }
        phone.addEventListener('change', decorate);
        window.addEventListener('beforeprint', () => { printing = true; restore(); });
        window.addEventListener('afterprint', () => { printing = false; decorate(); });
        decorate();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
