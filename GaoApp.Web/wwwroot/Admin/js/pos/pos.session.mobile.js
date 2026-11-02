/* UI-06e: phone delivery status placement/viewport only. The original delivery
   form, journal, authentication and terminal lock keep their own authority. */
(function () {
    'use strict';
    function mount() {
        const root = document.getElementById('posShell');
        if (!root || root.__sessionMobileMounted) return;
        root.__sessionMobileMounted = true;
        const modal = document.getElementById('deliveryModal');
        const message = document.getElementById('deliveryMessage');
        const footer = modal?.querySelector('.modal-footer');
        if (!modal || !message || !footer) return;
        const body = modal.querySelector('.modal-body');
        const lookup = footer.querySelector('a[href="/admin/deliveries"]');
        const phone = window.matchMedia('(max-width: 767.98px)');
        let marker = null, lookupMarker = null, frame = 0;
        function geometry() {
            frame = 0;
            const viewport = window.visualViewport;
            if (!phone.matches || !root.hasAttribute('data-pos-phone') || !viewport || Math.abs(viewport.scale - 1) > .02) {
                modal.style.removeProperty('--pos-session-visible-height');
                modal.style.removeProperty('--pos-session-visible-top');
                return;
            }
            modal.style.setProperty('--pos-session-visible-height', Math.floor(viewport.height) + 'px');
            modal.style.setProperty('--pos-session-visible-top', Math.max(0, viewport.offsetTop) + 'px');
            const focused = document.activeElement;
            const body = focused?.closest('#deliveryModal .modal-body');
            if (body && modal.classList.contains('show')) {
                const field = focused.getBoundingClientRect(), visible = body.getBoundingClientRect();
                if (field.bottom > visible.bottom - 12) body.scrollTop += field.bottom - visible.bottom + 12;
                else if (field.top < visible.top + 12) body.scrollTop -= visible.top - field.top + 12;
            }
        }
        function scheduleGeometry() { if (!frame) frame = requestAnimationFrame(geometry); }
        function apply() {
            const active = phone.matches && root.hasAttribute('data-pos-phone');
            if (active && !marker) {
                marker = document.createComment('delivery status original position');
                message.before(marker);
                footer.prepend(message);
                if (lookup && body) {
                    lookupMarker = document.createComment('delivery lookup original position');
                    lookup.before(lookupMarker);
                    body.append(lookup);
                }
            } else if (!active && marker) {
                marker.replaceWith(message);
                marker = null;
                if (lookupMarker) { lookupMarker.replaceWith(lookup); lookupMarker = null; }
            }
            scheduleGeometry();
        }
        phone.addEventListener('change', apply);
        window.addEventListener('pageshow', apply);
        window.addEventListener('resize', scheduleGeometry);
        window.visualViewport?.addEventListener('resize', scheduleGeometry);
        window.visualViewport?.addEventListener('scroll', scheduleGeometry);
        modal.addEventListener('shown.bs.modal', scheduleGeometry);
        apply();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
    else mount();
})();
