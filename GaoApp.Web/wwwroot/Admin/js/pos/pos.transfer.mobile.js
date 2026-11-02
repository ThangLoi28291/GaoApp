/* UI-06d: place original transfer actions in a phone footer. Bootstrap and the
   existing payment/bank modules retain every decision, state and event handler. */
(function () {
    'use strict';
    function mount() {
        const root = document.getElementById('posShell');
        if (!root || root.__transferMobileMounted) return;
        root.__transferMobileMounted = true;
        const phone = window.matchMedia('(max-width: 767.98px)');
        const dialogs = [
            { id: 'paymentQrModal', selectors: ['.modal-body > .d-grid'] },
            { id: 'bankTransferReviewModal', selectors: ['#bankReviewError', '.pos-bank-review__actions', '.pos-bank-review__footer'] }
        ].map(item => {
            const modal = document.getElementById(item.id);
            const content = modal?.querySelector('.modal-content');
            const nodes = item.selectors.map(selector => modal?.querySelector(selector));
            return content && nodes.every(Boolean) ? { modal, content, nodes, markers: [], footer: null } : null;
        }).filter(Boolean);
        const qrDialog = dialogs.find(dialog => dialog.modal.id === 'paymentQrModal');
        const paymentError = document.getElementById('paymentInlineErrorBox');
        let errorMarker = null;
        let frame = 0;
        function geometry() {
            frame = 0;
            const viewport = window.visualViewport;
            const active = phone.matches && root.hasAttribute('data-pos-phone') && viewport && Math.abs(viewport.scale - 1) < .02;
            for (const dialog of dialogs) {
                if (active) {
                    dialog.modal.style.setProperty('--pos-transfer-visible-height', Math.floor(viewport.height) + 'px');
                    dialog.modal.style.setProperty('--pos-transfer-visible-top', Math.max(0, viewport.offsetTop) + 'px');
                } else {
                    dialog.modal.style.removeProperty('--pos-transfer-visible-height');
                    dialog.modal.style.removeProperty('--pos-transfer-visible-top');
                }
            }
        }
        function scheduleGeometry() { if (!frame) frame = requestAnimationFrame(geometry); }
        function apply() {
            const active = phone.matches && root.hasAttribute('data-pos-phone');
            const focused = document.activeElement;
            // Return the shared original error before restoring its footer host.
            if (errorMarker && (!active || !qrDialog?.modal.classList.contains('show'))) {
                errorMarker.replaceWith(paymentError);
                errorMarker = null;
            }
            for (const dialog of dialogs) {
                if (active && !dialog.footer) {
                    dialog.footer = document.createElement('div');
                    dialog.footer.className = 'pos-transfer-mobile-footer';
                    for (const node of dialog.nodes) {
                        const marker = document.createComment('transfer action original position');
                        node.before(marker);
                        dialog.markers.push(marker);
                        dialog.footer.append(node);
                    }
                    dialog.content.append(dialog.footer);
                } else if (!active && dialog.footer) {
                    dialog.nodes.forEach((node, index) => dialog.markers[index].replaceWith(node));
                    dialog.footer.remove();
                    dialog.footer = null;
                    dialog.markers = [];
                }
            }
            if (active && qrDialog?.modal.classList.contains('show') && paymentError && !errorMarker) {
                errorMarker = document.createComment('payment error original position');
                paymentError.before(errorMarker);
                qrDialog.footer.prepend(paymentError);
            }
            if (focused && focused !== document.activeElement && focused.isConnected) focused.focus({ preventScroll: true });
            scheduleGeometry();
        }
        phone.addEventListener('change', apply);
        window.addEventListener('pageshow', apply);
        window.addEventListener('resize', scheduleGeometry);
        window.visualViewport?.addEventListener('resize', scheduleGeometry);
        window.visualViewport?.addEventListener('scroll', scheduleGeometry);
        qrDialog?.modal.addEventListener('shown.bs.modal', apply);
        qrDialog?.modal.addEventListener('hidden.bs.modal', apply);
        apply();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
    else mount();
})();
