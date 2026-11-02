/* UI-20: presentation only. The native widget owns jobs, fields, focus, viewport and overlays. */
(function () {
    'use strict';
    function start() {
        const root = document.getElementById('gaoAssistant');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const original = root.getAttribute('data-assistant-widget-phone');
        let printing = false;
        function restore() {
            if (original === null) root.removeAttribute('data-assistant-widget-phone');
            else root.setAttribute('data-assistant-widget-phone', original);
        }
        function paint() {
            if (printing || !phone.matches) { restore(); return; }
            root.setAttribute('data-assistant-widget-phone', '');
        }
        phone.addEventListener('change', paint);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; paint(); });
        paint();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
