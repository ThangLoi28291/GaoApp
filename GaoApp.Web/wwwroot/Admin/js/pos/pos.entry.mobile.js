/* UI-06a: phone presentation only. Original POS controls retain every business handler. */
(function () {
    'use strict';
    function mount() {
        const root = document.getElementById('posShell');
        if (!root || root.dataset.posEntryMounted) return;
        const header = root.querySelector('[data-pos-prime-header], [data-pos-cockpit-header]');
        const context = header?.querySelector('.pos-prime-header__context, .pos-cockpit-header__context');
        const readiness = header?.querySelector('.pos-prime-readiness, .pos-cockpit-readiness');
        const shortcuts = header?.querySelector('[data-pos-shortcuts-toggle]');
        if (!context || !readiness || !shortcuts) return;
        root.dataset.posEntryMounted = 'true';
        const media = window.matchMedia('(max-width: 767.98px)');
        const placements = [context, readiness, shortcuts].map(node => ({ node, marker: null }));
        let details = null, observer = null, wasOpen = false;
        function restore() {
            if (!details) return;
            wasOpen = details.open;
            const needsFocus = details.contains(document.activeElement);
            observer?.disconnect(); observer = null;
            for (const item of placements) {
                item.marker.replaceWith(item.node); item.marker = null;
            }
            details.remove(); details = null;
            root.removeAttribute('data-pos-phone');
            if (needsFocus) header.querySelector('#btnOpenHeldOrders, button')?.focus({ preventScroll: true });
        }
        function activate() {
            if (details) return;
            details = document.createElement('details');
            details.className = 'pos-entry-context';
            details.open = wasOpen;
            const summary = document.createElement('summary');
            const caption = document.createElement('span');
            caption.className = 'pos-entry-context__caption';
            caption.textContent = 'Ca và quầy';
            const value = document.createElement('span');
            value.className = 'pos-entry-context__value';
            const chevron = document.createElement('i');
            chevron.className = 'bx bx-chevron-down'; chevron.setAttribute('aria-hidden', 'true');
            summary.append(caption, value, chevron);
            const body = document.createElement('div');
            body.className = 'pos-entry-context__body';
            const footer = document.createElement('div');
            footer.className = 'pos-entry-context__footer';
            for (const item of placements) {
                item.marker = document.createComment('pos-entry-original-position');
                item.node.before(item.marker);
            }
            body.append(context);
            // Legacy header omits terminal; display the same server-resolved terminal from posShell.
            if (!root.classList.contains('pos-prime')) {
                const terminal = document.createElement('div');
                terminal.className = 'pos-entry-context__terminal';
                terminal.textContent = 'Quầy: ' + (root.dataset.terminalName || root.dataset.terminalCode || 'Chưa xác định máy POS');
                body.append(terminal);
            }
            footer.append(readiness, shortcuts); body.append(footer);
            details.append(summary, body); header.append(details);
            function sync() {
                const shift = context.querySelector('strong');
                const shiftText = root.classList.contains('pos-prime') ? shift?.textContent : shift?.parentElement.title;
                const text = [shiftText?.trim(), root.dataset.terminalName || root.dataset.terminalCode].filter(Boolean).join(' · ');
                if (value.textContent !== text) value.textContent = text;
                summary.title = text;
                summary.setAttribute('aria-label', 'Thông tin ca và quầy: ' + text);
                details.classList.toggle('is-warning', readiness.classList.contains('is-warning'));
            }
            sync();
            observer = new MutationObserver(sync);
            observer.observe(context, { childList: true, characterData: true, subtree: true });
            observer.observe(readiness, { attributes: true, attributeFilter: ['class'] });
            root.setAttribute('data-pos-phone', '');
        }
        function refresh() { if (media.matches) activate(); else restore(); }
        media.addEventListener('change', refresh);
        window.addEventListener('pageshow', refresh);
        refresh();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
    else mount();
})();
