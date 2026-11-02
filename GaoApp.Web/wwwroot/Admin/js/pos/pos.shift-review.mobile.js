/* UI-07c: presentation only. Original reconciliation/approval owners retain all state and commands. */
(() => {
    'use strict';
    function init() {
        const dashboard = document.querySelector('.manager-dashboard');
        const reconciliation = document.getElementById('shiftReconciliation');
        if (!dashboard && !reconciliation) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const modal = document.getElementById('shiftDetailModal');
        const form = document.getElementById('cashReceiptForm');
        const confirm = document.getElementById('btnConfirmCashReceipt');
        const moves = [], wrappers = [], attributes = new Map();
        let mounted = false, frame = 0;
        function attribute(node, name, value) {
            if (!node) return;
            let saved = attributes.get(node);
            if (!saved) attributes.set(node, saved = new Map());
            if (!saved.has(name)) saved.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function move(node, parent, before = null) {
            if (!node || !parent) return;
            const marker = document.createComment('UI-07c original position');
            node.before(marker); moves.push({node, marker}); parent.insertBefore(node, before);
        }
        function disclosure(title, before) {
            const details = document.createElement('details'), summary = document.createElement('summary');
            details.className = 'shift-review-disclosure'; summary.textContent = title;
            details.append(summary); before.before(details); wrappers.push(details); return details;
        }
        function mount() {
            if (mounted) return;
            mounted = true;
            if (dashboard) {
                const filter = dashboard.querySelector('.manager-filter-card');
                const queue = document.getElementById('shiftBody')?.closest('.manager-card');
                const difference = document.getElementById('differenceBody')?.closest('.manager-card');
                const employee = document.getElementById('employeeBody')?.closest('.manager-card');
                const payments = document.getElementById('dashboardPaymentBreakdown')?.closest('.manager-card');
                const kpis = dashboard.querySelector('.kpi-grid');
                const grid = dashboard.querySelector('.manager-grid');
                const filters = disclosure('Bộ lọc báo cáo', filter);
                move(filter, filters);
                move(queue, dashboard, filters.nextSibling);
                move(difference, dashboard, queue.nextSibling);
                attribute(queue, 'data-shift-phone-queue', '');
                const overview = disclosure('Tổng hợp theo bộ lọc', difference.nextElementSibling || grid);
                move(kpis, overview); move(payments, overview); move(employee, overview);
                attribute(grid, 'hidden', '');
                const body = modal.querySelector('.modal-body');
                move(form, body, body.firstChild);
                const evidence = document.getElementById('detailReconciliation');
                move(evidence, disclosure('Đối soát giao dịch trong ca', evidence));
                move(confirm, modal.querySelector('.modal-footer'));
                attribute(confirm, 'form', form.id);
                // The form's original owner controls visibility. The same submit button remains associated with it.
                attribute(confirm, 'hidden', '');
            }
        }
        function restore() {
            if (!mounted) return;
            mounted = false;
            for (const {node, marker} of moves.reverse()) { marker.replaceWith(node); }
            moves.length = 0;
            for (const wrapper of wrappers) wrapper.remove();
            wrappers.length = 0;
            for (const [node, saved] of attributes) for (const [name, value] of saved)
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            attributes.clear();
            modal?.style.removeProperty('--shift-review-height');
            modal?.style.removeProperty('--shift-review-top');
        }
        function tables() {
            const roots = [dashboard, reconciliation, modal].filter(Boolean);
            for (const root of roots) for (const table of root.querySelectorAll('.manager-table,.recon-table')) {
                attribute(table, 'role', 'table');
                const labels = [...table.querySelectorAll('thead th')].map(th => th.textContent.trim());
                for (const group of table.querySelectorAll('thead,tbody')) attribute(group, 'role', 'rowgroup');
                for (const row of table.querySelectorAll('tr')) {
                    attribute(row, 'role', 'row');
                    for (const cell of row.querySelectorAll('th')) attribute(cell, 'role', 'columnheader');
                    for (const [index, cell] of [...row.querySelectorAll('td')].entries()) {
                        attribute(cell, 'role', 'cell');
                        if (cell.colSpan === 1 && labels[index]) attribute(cell, 'data-phone-label', labels[index]);
                    }
                }
            }
            // Do not retain removed evidence/page rows for the lifetime of the page.
            for (const node of attributes.keys()) if (!node.isConnected) attributes.delete(node);
        }
        function viewport() {
            if (!modal || !phone.matches) return;
            const visible = window.visualViewport;
            const unzoomed = visible && Math.abs(visible.scale - 1) < .01;
            modal.style.setProperty('--shift-review-height', (unzoomed ? visible.height : innerHeight) + 'px');
            modal.style.setProperty('--shift-review-top', (unzoomed ? visible.offsetTop : 0) + 'px');
        }
        function sync() {
            frame = 0;
            if (!phone.matches) { restore(); return; }
            mount(); tables(); viewport();
            if (confirm && confirm.hidden !== form.hidden) confirm.hidden = form.hidden;
        }
        function schedule() { if (!frame) frame = requestAnimationFrame(sync); }
        const observer = new MutationObserver(schedule);
        for (const root of [dashboard, reconciliation, modal].filter(Boolean))
            observer.observe(root, {childList: true, subtree: true, attributes: true, attributeFilter: ['hidden']});
        phone.addEventListener('change', schedule);
        addEventListener('resize', schedule);
        window.visualViewport?.addEventListener('resize', schedule);
        window.visualViewport?.addEventListener('scroll', schedule);
        modal?.addEventListener('focusin', event => {
            if (phone.matches && event.target.matches('input,textarea,select')) requestAnimationFrame(() => {
                viewport(); event.target.scrollIntoView({block: 'nearest'});
            });
        });
        sync();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init, {once: true});
    else init();
})();
