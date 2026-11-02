// UI-05b: move original receipt metadata into Info on phones; business controls keep their owners.
(function () {
    'use strict';
    function initialize() {
        const root = document.querySelector('[data-receiving-detail]');
        const heading = root?.querySelector('.wrd-topbar'), info = root?.querySelector('#wraInfo');
        if (!heading || !info) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const nodes = [heading, root.querySelector('#receiptDocumentActions')].filter(Boolean);
        const placements = [];
        let readonlyNotice;
        function update() {
            if (phone.matches && !placements.length) {
                for (const node of nodes) {
                    const anchor = document.createComment('receiving-metadata-desktop-position');
                    node.before(anchor); placements.push({ node, anchor });
                }
                info.prepend(...nodes); root.dataset.receivingPhone = '';
                if (root.querySelector('#CanEditReceiving')?.value === 'false') {
                    root.dataset.receivingReadonly = '';
                    readonlyNotice = document.createElement('span'); readonlyNotice.className = 'wrb-readonly-status';
                    readonlyNotice.setAttribute('role', 'status');
                    readonlyNotice.textContent = 'Phiếu chỉ xem · ' + (root.querySelector('.wra-badge')?.textContent.trim() || '');
                    root.querySelector('.wra-dock-status')?.append(readonlyNotice);
                }
            } else if (!phone.matches && placements.length) {
                for (const { node, anchor } of placements) anchor.replaceWith(node);
                placements.length = 0; delete root.dataset.receivingPhone;
                readonlyNotice?.remove(); readonlyNotice = null; delete root.dataset.receivingReadonly;
            }
        }
        phone.addEventListener('change', update); window.addEventListener('pageshow', update); update();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize, { once: true });
    else initialize();
}());
