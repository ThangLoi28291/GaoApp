/* UI-06c: phone copy and legacy presentation. Prime already places this original button in its footer.
   Keep the same node/listeners/disabled state; never handle a collection or submit here. */
(function () {
    'use strict';
    function mount() {
        const root = document.getElementById('posShell');
        if (!root || root.__checkoutMobileMounted) return;
        const legacy = root.dataset.posPresentation === 'legacy';
        const button = document.getElementById('btnAddPayment');
        const modal = document.getElementById('paymentModal');
        const footer = modal?.querySelector('.pos-payment-workspace__footer');
        if (!button || !footer) return;
        root.__checkoutMobileMounted = true;
        const phone = window.matchMedia('(max-width: 767.98px)');
        const notes = [
            ['0', 'Tiền mặt đủ hoặc dư sẽ chốt đơn. Kiểm tra tiền thừa trước khi ghi nhận.'],
            ['2', 'Nhập mã tham chiếu của giao dịch thẻ trước khi ghi nhận.']
        ].map(([method, text]) => {
            const node = modal.querySelector('[data-payment-method-note="' + method + '"]');
            return node ? { node, text, original: node.textContent } : null;
        }).filter(Boolean);
        let marker = null, host = null;
        let frame = 0;
        function geometry() {
            frame = 0;
            const viewport = window.visualViewport;
            if (!phone.matches || !root.hasAttribute('data-pos-phone') || !viewport || Math.abs(viewport.scale - 1) > .02) {
                modal.style.removeProperty('--pos-checkout-visible-height');
                modal.style.removeProperty('--pos-checkout-visible-top');
                return;
            }
            modal.style.setProperty('--pos-checkout-visible-height', Math.floor(viewport.height) + 'px');
            modal.style.setProperty('--pos-checkout-visible-top', Math.max(0, viewport.offsetTop) + 'px');
            const focused = document.activeElement;
            const body = focused?.closest('#paymentModal .modal-body');
            if (body && modal.classList.contains('show')) {
                const fieldBox = focused.getBoundingClientRect(), bodyBox = body.getBoundingClientRect();
                if (fieldBox.bottom > bodyBox.bottom - 12) body.scrollTop += fieldBox.bottom - bodyBox.bottom + 12;
                else if (fieldBox.top < bodyBox.top + 12) body.scrollTop -= bodyBox.top - fieldBox.top + 12;
            }
        }
        function scheduleGeometry() { if (!frame) frame = window.requestAnimationFrame(geometry); }
        function apply() {
            const focused = document.activeElement === button;
            const active = phone.matches && root.hasAttribute('data-pos-phone');
            for (const note of notes) {
                const text = active ? note.text : note.original;
                if (note.node.textContent !== text) note.node.textContent = text;
            }
            if (legacy && active && !host) {
                marker = document.createComment('checkout submit original position');
                button.before(marker);
                host = document.createElement('div');
                host.className = 'pos-checkout-mobile-submit';
                footer.prepend(host);
                host.append(button);
            } else if (!active && host) {
                marker.replaceWith(button);
                host.remove();
                host = marker = null;
            }
            if (focused && document.activeElement !== button) button.focus({ preventScroll: true });
            if (legacy) scheduleGeometry();
        }
        phone.addEventListener('change', apply);
        window.addEventListener('pageshow', apply);
        if (legacy) {
            window.addEventListener('resize', scheduleGeometry);
            window.visualViewport?.addEventListener('resize', scheduleGeometry);
            window.visualViewport?.addEventListener('scroll', scheduleGeometry);
        }
        apply();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
    else mount();
})();
