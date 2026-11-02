/* UI-08a presentation only: original request owners retain forms, permissions, busy state and commands. */
(() => {
    'use strict';
    function init() {
        const root = document.getElementById('posOperationsRequests');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const dialogs = ['ca', 'pa'].map(prefix => ({
            dialog: document.getElementById(prefix + 'Dialog'), body: document.getElementById(prefix + 'DialogBody'),
            action: null, marker: null, footer: null
        }));
        const attributes = new Map();
        let frame = 0;
        function attribute(node, name, value) {
            let saved = attributes.get(node);
            if (!saved) attributes.set(node, saved = new Map());
            if (!saved.has(name)) saved.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function release(record) {
            if (record.action) {
                // A new owner render removed the old body/marker: discard its detached actions, never revive them.
                if (record.marker?.isConnected && record.body.contains(record.marker)) record.marker.replaceWith(record.action);
                else record.action.remove();
            }
            record.action = record.marker = null;
        }
        function actions(record) {
            if (record.action && !record.body.contains(record.marker)) release(record);
            const action = record.body.querySelector('.ca-actions');
            if (action) {
                release(record);
                if (!record.footer) {
                    record.footer = document.createElement('div');
                    record.footer.className = 'requests-phone-footer'; record.dialog.append(record.footer);
                }
                // Native form association preserves validation and the original onsubmit listener after reparenting.
                for (const button of action.querySelectorAll('button')) {
                    if (button.type === 'submit' && button.form?.id) attribute(button, 'form', button.form.id);
                }
                record.marker = document.createComment('UI-08a original action position');
                action.before(record.marker); record.action = action; record.footer.append(action);
            }
            if (record.footer) record.footer.hidden = !record.dialog.open || !record.action?.children.length;
        }
        function tables() {
            for (const table of root.querySelectorAll('.ca-table-wrap table')) {
                attribute(table, 'role', 'table');
                const labels = [...table.querySelectorAll('thead th')].map(n => n.textContent.trim());
                for (const group of table.querySelectorAll('thead,tbody')) attribute(group, 'role', 'rowgroup');
                for (const row of table.querySelectorAll('tr')) {
                    attribute(row, 'role', 'row');
                    for (const cell of row.querySelectorAll('th')) attribute(cell, 'role', 'columnheader');
                    for (const [index, cell] of [...row.querySelectorAll('td')].entries()) {
                        attribute(cell, 'role', 'cell');
                        if (cell.colSpan === 1 && labels[index]) attribute(cell, 'data-request-phone-label', labels[index]);
                    }
                }
            }
        }
        function viewport() {
            const visible = window.visualViewport, unzoomed = visible && Math.abs(visible.scale - 1) < .01;
            for (const {dialog} of dialogs) {
                dialog.style.setProperty('--requests-visible-height', (unzoomed ? visible.height : innerHeight) + 'px');
                dialog.style.setProperty('--requests-visible-top', (unzoomed ? visible.offsetTop : 0) + 'px');
            }
        }
        function restore() {
            for (const record of dialogs) {
                release(record); record.footer?.remove(); record.footer = null;
                record.dialog.style.removeProperty('--requests-visible-height');
                record.dialog.style.removeProperty('--requests-visible-top');
            }
            for (const [node, saved] of attributes) for (const [name, value] of saved)
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            attributes.clear();
        }
        function sync() {
            frame = 0;
            if (!phone.matches) { restore(); return; }
            for (const record of dialogs) actions(record);
            tables(); viewport();
            for (const node of attributes.keys()) if (!node.isConnected) attributes.delete(node);
        }
        function schedule() { if (!frame) frame = requestAnimationFrame(sync); }
        // Reconcile action nodes in the mutation microtask, before a new owner render can paint obsolete controls.
        const observer = new MutationObserver(() => {
            if (phone.matches) for (const record of dialogs) actions(record);
            schedule();
        });
        observer.observe(root, {childList: true, subtree: true, attributes: true, attributeFilter: ['open']});
        phone.addEventListener('change', sync);
        addEventListener('resize', schedule);
        window.visualViewport?.addEventListener('resize', schedule);
        window.visualViewport?.addEventListener('scroll', schedule);
        root.addEventListener('focusin', event => {
            if (phone.matches && event.target.closest('dialog[open]') && event.target.matches('input,select,textarea'))
                requestAnimationFrame(() => { viewport(); event.target.scrollIntoView({block: 'nearest'}); });
        });
        sync();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init, {once: true}); else init();
})();
