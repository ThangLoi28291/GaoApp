/* UI-04a: rearrange the existing rendered nodes on phones; the orders loader owns all data/actions. */
(() => {
    'use strict';
    const root = document.querySelector('[data-mobile-orders]');
    const body = root?.querySelector('#ordersBody');
    if (!root || !body) return;
    const phone = window.matchMedia('(max-width: 767.98px)');
    const rows = new Map();
    let shortcutPlacement, metricPlacement;
    const element = (tag, className, text) => {
        const node = document.createElement(tag);
        node.className = className;
        if (text) node.textContent = text;
        return node;
    };
    function group(parent, title, nodes) {
        if (!nodes.length) return;
        const section = element('div', 'po-mobile-order-group');
        section.append(element('p', 'po-mobile-order-group-label', title), ...nodes);
        parent.append(section);
    }
    function preserveFocus(active, row, fallback) {
        if (!active || !row.contains(active) || root.querySelector('dialog[open]')) return;
        (active.isConnected ? active : fallback)?.focus({ preventScroll: true });
    }
    function enhanceRow(row) {
        if (rows.has(row)) return;
        const cells = Array.from(row.cells);
        if (cells.length !== 6) return;
        const orderButton = cells[0].querySelector('.po-order-link');
        const time = cells[0].querySelector('.po-order-meta');
        const customer = cells[0].querySelector('.po-customer');
        const statuses = cells[1].querySelector('.po-statuses');
        const actions = cells[5].querySelector('.po-actions');
        if (!orderButton || !time || !customer || !statuses || !actions) return;
        const active = row.contains(document.activeElement) ? document.activeElement : null;
        const originals = cells.map(cell => Array.from(cell.childNodes));
        const colspan = cells[0].getAttribute('colspan');
        rows.set(row, { cells, originals, colspan, orderButton });
        const card = element('article', 'po-mobile-order-card');
        card.setAttribute('aria-label', 'Đơn hàng ' + orderButton.textContent.trim());
        const heading = element('div', 'po-mobile-order-heading');
        heading.append(orderButton, actions);
        const identity = element('div', 'po-mobile-order-identity');
        identity.append(customer, time);
        const amount = element('div', 'po-mobile-order-amount');
        amount.append(...originals[2]);
        const brief = element('div', 'po-mobile-order-brief');
        brief.append(identity, amount);
        const payments = element('div', 'po-mobile-order-payments');
        const methods = cells[3].querySelector('.po-settlement-tags');
        payments.append(...originals[3].filter(node => node !== methods));
        const details = element('details', 'po-mobile-order-details');
        const summary = element('summary', 'po-mobile-order-summary', 'Thông tin bổ sung');
        // Also lets the existing row double-click guard recognize this native control.
        summary.setAttribute('role', 'button');
        const secondary = element('div', 'po-mobile-order-secondary');
        group(secondary, 'Thông tin đơn', originals[0].filter(node => ![orderButton, time, customer].includes(node)));
        group(secondary, 'Nhân viên, máy & giao hàng', originals[1].filter(node => node !== statuses));
        if (methods) group(secondary, 'Hình thức thanh toán', [methods]);
        group(secondary, 'Cọc, voucher & sau bán', originals[4]);
        details.append(summary, secondary);
        card.append(heading, brief, statuses, payments, details);
        cells[0].replaceChildren(card);
        cells[0].setAttribute('colspan', '6');
        cells[0].classList.add('po-mobile-order-cell');
        cells.slice(1).forEach(cell => cell.classList.add('po-mobile-order-unused'));
        preserveFocus(active, row, orderButton);
    }
    function restoreRow(row, saved) {
        const active = row.contains(document.activeElement) ? document.activeElement : null;
        saved.cells.forEach((cell, index) => {
            cell.replaceChildren(...saved.originals[index]);
            cell.classList.remove('po-mobile-order-cell', 'po-mobile-order-unused');
        });
        if (saved.colspan === null) saved.cells[0].removeAttribute('colspan');
        else saved.cells[0].setAttribute('colspan', saved.colspan);
        // The native details summary is phone-only; return focus to the original order button.
        if (active && !active.isConnected && !root.querySelector('dialog[open]')) saved.orderButton.focus({ preventScroll: true });
        else preserveFocus(active, row, saved.orderButton);
    }
    function arrangeShortcuts() {
        if (shortcutPlacement) return;
        const nav = root.querySelector('[data-configured-pos-nav]');
        const heading = root.querySelector('.po-header');
        if (!nav || !heading) return;
        const active = nav.contains(document.activeElement) ? document.activeElement : null;
        const details = element('details', 'po-mobile-order-shortcuts');
        const summary = element('summary', 'po-mobile-order-summary', 'Lối tắt POS của cửa hàng');
        shortcutPlacement = { nav, parent: nav.parentNode, next: nav.nextSibling, details };
        details.open = !!active;
        details.append(summary, nav);
        heading.after(details);
        if (active && !root.querySelector('dialog[open]')) active.focus({ preventScroll: true });
    }
    function arrangeMetrics() {
        if (metricPlacement) return;
        const metrics = root.querySelector('.po-metrics');
        if (!metrics) return;
        const details = element('details', 'po-mobile-order-overview');
        const summary = element('summary', 'po-mobile-order-summary', 'Thống kê theo bộ lọc');
        metricPlacement = { metrics, parent: metrics.parentNode, next: metrics.nextSibling, details };
        metrics.before(details);
        details.append(summary, metrics);
    }
    function scan() {
        if (!phone.matches) return;
        for (const [row] of rows) if (!body.contains(row)) rows.delete(row);
        body.querySelectorAll('.po-order-row').forEach(enhanceRow);
    }
    const observer = new MutationObserver(scan);
    function sync() {
        observer.disconnect();
        if (phone.matches) {
            root.setAttribute('data-mobile-orders-mode', 'cards');
            arrangeShortcuts();
            arrangeMetrics();
            scan();
            // Only tbody replacements matter; our changes inside cells cannot trigger this observer.
            observer.observe(body, { childList: true });
        } else {
            for (const [row, saved] of rows) if (body.contains(row)) restoreRow(row, saved);
            rows.clear();
            if (metricPlacement) {
                const { metrics, parent, next, details } = metricPlacement;
                const active = details.contains(document.activeElement);
                parent.insertBefore(metrics, next?.parentNode === parent ? next : null);
                details.remove(); metricPlacement = null;
                if (active && !root.querySelector('dialog[open]')) root.querySelector('#keyword')?.focus({ preventScroll: true });
            }
            if (shortcutPlacement) {
                const { nav, parent, next, details } = shortcutPlacement;
                const active = details.contains(document.activeElement) ? document.activeElement : null;
                parent.insertBefore(nav, next?.parentNode === parent ? next : null);
                details.remove(); shortcutPlacement = null;
                if (active && !root.querySelector('dialog[open]')) {
                    (active.isConnected ? active : nav.querySelector('button,a'))?.focus({ preventScroll: true });
                }
            }
            root.removeAttribute('data-mobile-orders-mode');
        }
    }
    phone.addEventListener('change', sync);
    window.addEventListener('pagehide', () => observer.disconnect());
    window.addEventListener('pageshow', sync);
    sync();
})();
