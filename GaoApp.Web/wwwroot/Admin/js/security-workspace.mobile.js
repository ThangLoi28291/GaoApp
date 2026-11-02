/* UI-21 presentation adapter. Original rows, inputs, grants, tokens and handlers stay in place. */
(() => {
    'use strict';
    if (!document.querySelector('[data-ui21-view]')) return;
    const phone = matchMedia('screen and (max-width: 767.98px)');
    const changed = new Map();
    const remember = (node, name, value) => {
        if (!changed.has(node)) changed.set(node, new Map());
        const attributes = changed.get(node);
        if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
        if (node.getAttribute(name) !== value) node.setAttribute(name, value);
    };
    const restore = () => {
        for (const [node, attributes] of changed) for (const [name, value] of attributes) {
            if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
        }
        changed.clear();
    };
    const apply = () => {
        if (!phone.matches) { restore(); return; }
        document.querySelectorAll('#auditLogTable, body:has([data-ui21-view="POSTerminals/Index"]) [data-mobile-shell-content] table').forEach(table => {
            remember(table, 'data-ui21-cards', '');
            const labels = [...table.querySelectorAll('thead th')].map(th => th.textContent.trim());
            table.querySelectorAll('tbody tr').forEach(row => [...row.cells].forEach((cell, index) => {
                if (cell.colSpan === 1 && labels[index]) remember(cell, 'data-ui21-label', labels[index]);
            }));
        });
    };
    let scheduled = false;
    const refresh = () => { if (!scheduled) { scheduled = true; requestAnimationFrame(() => { scheduled = false; apply(); }); } };
    const content = document.querySelector('[data-mobile-shell-content]');
    if (content) new MutationObserver(refresh).observe(content, {childList: true, subtree: true});
    phone.addEventListener('change', apply);
    addEventListener('beforeprint', restore);
    addEventListener('afterprint', apply);
    apply();
})();
