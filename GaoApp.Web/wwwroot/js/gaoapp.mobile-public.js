/* Viewport sizing only. Native customer-support handlers own every command and draft. */
(() => {
    'use strict';
    const root = document.getElementById('customerSupport');
    if (!root) return;
    const phone = matchMedia('(max-width: 767.98px)');
    function resize() {
        if (phone.matches) root.style.setProperty('--gp-viewport-height', `${Math.round(window.visualViewport?.height || innerHeight)}px`);
        else root.style.removeProperty('--gp-viewport-height');
    }
    phone.addEventListener('change', resize);
    window.addEventListener('resize', resize, { passive: true });
    window.visualViewport?.addEventListener('resize', resize, { passive: true });
    window.addEventListener('pageshow', resize);
    resize();
})();
