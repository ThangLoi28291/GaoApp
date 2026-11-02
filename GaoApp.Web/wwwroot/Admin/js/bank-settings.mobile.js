/* UI-17: phone presentation; native bank configuration, permissions and commands retain ownership. */
(function () {
    'use strict';
    function start() {
        const accountIndex = document.querySelector('[data-store-bank-account-index]');
        const accountForm = document.querySelector('form[action*="/storebankaccounts/edit" i]');
        const acbInput = document.getElementById('ClientSecret');
        const routingHeading = [...document.querySelectorAll('h4')].find(node => node.textContent.trim() === 'Cửa hàng nhận callback từ URL cũ');
        const resultHeading = [...document.querySelectorAll('h3')].find(node => node.textContent.trim().startsWith('Kết quả kiểm thử ACB'));
        const root = accountIndex || accountForm?.closest('.row.justify-content-center') || acbInput?.closest('.card') || routingHeading?.closest('.container-fluid, .container-xxl') || resultHeading?.closest('.card');
        if (!root) return;
        const kind = accountIndex ? 'accounts' : accountForm ? 'account-form' : acbInput ? 'acb' : routingHeading ? 'routing' : 'result';
        const header = document.querySelector('.gao-admin-page-heading-content');
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        let navigation = null, printing = false, queued = false;
        function mark(node, name, value) {
            if (!node) return;
            let saved = originals.get(node);
            if (!saved) { saved = new Map(); originals.set(node, saved); }
            if (!saved.has(name)) saved.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            navigation?.remove(); navigation = null;
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function sections() {
            if (kind !== 'acb' || navigation) return;
            const targets = [[root.querySelector('form'), 'Kết nối'], [root.querySelector('fieldset'), 'Callback'], [root.querySelector('form[action*="test-connection" i]'), 'Kiểm tra']].filter(([node]) => node);
            if (targets.length < 2) return;
            navigation = document.createElement('nav'); navigation.className = 'bank-settings-phone-sections';
            navigation.setAttribute('aria-label', 'Các phần cấu hình ACB');
            targets.forEach(([node, label], index) => {
                if (!node.id) mark(node, 'id', 'bank-settings-phone-section-' + index);
                mark(node, 'data-bank-settings-phone-section', '');
                const link = document.createElement('a'); link.href = '#' + node.id; link.textContent = label; navigation.append(link);
            });
            root.querySelector('form')?.before(navigation);
        }
        function tables() {
            root.querySelectorAll('table').forEach(table => {
                mark(table, 'data-bank-settings-phone-table', ''); mark(table, 'role', 'table');
                const grid = [];
                [...table.querySelectorAll('thead tr')].forEach((row, rowIndex) => {
                    grid[rowIndex] ||= []; let column = 0;
                    [...row.cells].forEach(cell => {
                        while (grid[rowIndex][column] !== undefined) column++;
                        const text = cell.textContent.trim().replace(/\s+/g, ' ');
                        for (let r = rowIndex; r < rowIndex + cell.rowSpan; r++) {
                            grid[r] ||= [];
                            for (let c = column; c < column + cell.colSpan; c++) grid[r][c] = text;
                        }
                        column += cell.colSpan;
                    });
                });
                const labels = grid.at(-1) || [];
                table.querySelectorAll('thead th').forEach(cell => mark(cell, 'role', 'columnheader'));
                table.querySelectorAll('tbody tr').forEach(row => {
                    mark(row, 'role', 'row'); let column = 0;
                    [...row.cells].forEach(cell => {
                        mark(cell, 'role', 'cell');
                        if (cell.colSpan > 1) mark(cell, 'data-bank-settings-phone-wide', '');
                        else if (labels[column]) mark(cell, 'data-bank-settings-phone-label', labels[column]);
                        column += cell.colSpan;
                    });
                });
            });
        }
        function refresh() {
            queued = false;
            if (printing || !phone.matches) { restore(); return; }
            mark(document.body, 'data-bank-settings-phone', kind);
            mark(root, 'data-bank-settings-phone-root', kind); mark(header, 'data-bank-settings-phone-header', '');
            sections(); tables();
        }
        const queue = () => { if (queued || printing || !phone.matches) return; queued = true; requestAnimationFrame(refresh); };
        new MutationObserver(queue).observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', refresh);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; refresh(); });
        refresh();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
