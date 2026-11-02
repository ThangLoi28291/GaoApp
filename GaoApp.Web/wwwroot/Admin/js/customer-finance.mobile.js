/* UI-15: presentation only. Original debt/deposit owners keep commands and money. */
(function () {
    'use strict';
    function start() {
        const root = document.getElementById('customerDebtPage') || document.getElementById('customerDepositPage');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), originals = new Map();
        const kind = root.id === 'customerDepositPage' ? 'deposit' : root.querySelector('#debtReportFilters') ? 'collections' : 'debt';
        let advanced = null, positions = [], sectionNav = null, queued = false, printing = false;

        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }

        function restore() {
            for (const { node, anchor } of positions) if (anchor.isConnected) anchor.replaceWith(node);
            positions = []; advanced?.remove(); advanced = null; sectionNav?.remove(); sectionNav = null;
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }

        function filters() {
            if (advanced?.isConnected) return;
            const form = root.querySelector('#debtReportFilters');
            if (!form) return;
            const fields = ['method', 'userId', 'shiftId'].map(name => form.querySelector('[name="' + name + '"]')?.closest('label')).filter(Boolean);
            if (!fields.length) return;
            advanced = document.createElement('details'); advanced.className = 'finance-phone-filters';
            const summary = document.createElement('summary'); summary.textContent = 'Phương thức, người thu và ca';
            const content = document.createElement('div'); content.className = 'finance-phone-filter-fields';
            advanced.append(summary, content); fields[0].before(advanced);
            for (const node of fields) {
                const anchor = document.createComment('UI-15 original debt report filter position');
                node.before(anchor); positions.push({ node, anchor }); content.append(node);
            }
        }

        function sections() {
            if (sectionNav?.isConnected) return;
            const records = root.querySelector('.debt-records'), collect = root.querySelector('#debtCollectForm');
            if (!records || !collect) return;
            if (!records.id) mark(records, 'id', 'finance-phone-records');
            sectionNav = document.createElement('nav'); sectionNav.className = 'finance-phone-sections d-print-none';
            sectionNav.setAttribute('aria-label', 'Các phần công nợ của khách');
            for (const [node, label] of [[records, 'Đối chiếu công nợ'], [collect, 'Thu nợ']]) {
                const link = document.createElement('a'); link.setAttribute('href', '#' + node.id); link.textContent = label;
                sectionNav.append(link);
            }
            root.querySelector('.debt-workspace')?.before(sectionNav);
        }

        function headers(table) {
            const grid = [];
            [...(table.tHead?.rows || [])].forEach((row, rowIndex) => {
                grid[rowIndex] ||= [];
                let column = 0;
                for (const cell of row.cells) {
                    while (grid[rowIndex][column] !== undefined) column++;
                    for (let y = rowIndex; y < rowIndex + cell.rowSpan; y++) {
                        grid[y] ||= [];
                        for (let x = column; x < column + cell.colSpan; x++) grid[y][x] = cell.textContent.trim();
                    }
                    column += cell.colSpan;
                }
            });
            const columns = Math.max(0, ...grid.map(row => row.length));
            return Array.from({ length: columns }, (_, column) => [...new Set(grid.map(row => row[column]).filter(Boolean))].join(' · '));
        }

        function tables() {
            root.querySelectorAll('.debt-table, .deposit-table').forEach(table => {
                const labels = headers(table);
                mark(table, 'data-finance-phone-table', 'true'); mark(table, 'role', 'table');
                table.querySelectorAll('thead, tbody, tfoot').forEach(node => mark(node, 'role', 'rowgroup'));
                table.querySelectorAll('tr').forEach(node => mark(node, 'role', 'row'));
                table.querySelectorAll('thead th').forEach(node => mark(node, 'role', 'columnheader'));
                for (const group of [...table.tBodies, ...(table.tFoot ? [table.tFoot] : [])]) {
                    const occupied = [];
                    [...group.rows].forEach((row, rowIndex) => {
                        occupied[rowIndex] ||= [];
                        let column = 0;
                        for (const cell of row.cells) {
                            while (occupied[rowIndex][column]) column++;
                            mark(cell, 'role', cell.tagName === 'TH' ? 'rowheader' : 'cell');
                            if (cell.colSpan > 1) mark(cell, 'data-finance-phone-wide', 'true');
                            else {
                                mark(cell, 'data-finance-phone-label', labels[column] || 'Thông tin');
                                const actions = !!cell.querySelector('button, a.btn');
                                const money = cell.matches('.is-number, .debt-balance') || (cell.classList.contains('text-end') && !actions);
                                if (money) mark(cell, 'data-finance-phone-money', 'true');
                                if (actions) mark(cell, 'data-finance-phone-actions', 'true');
                                if (column === 0 || (table.classList.contains('deposit-list-table') && column === 2) || (cell === row.lastElementChild && !money))
                                    mark(cell, 'data-finance-phone-wide', 'true');
                            }
                            for (let y = rowIndex; y < rowIndex + cell.rowSpan; y++) {
                                occupied[y] ||= [];
                                for (let x = column; x < column + cell.colSpan; x++) occupied[y][x] = true;
                            }
                            column += cell.colSpan;
                        }
                    });
                }
            });
        }

        function decorate() {
            if (printing || !phone.matches) { restore(); return; }
            mark(document.body, 'data-customer-finance-phone', kind);
            mark(root, 'data-finance-phone-root', kind);
            mark(document.querySelector('.gds-page-header'), 'data-finance-phone-header', 'true');
            filters(); sections(); tables();
        }

        new MutationObserver(() => {
            if (printing || !phone.matches || queued) return;
            queued = true; requestAnimationFrame(() => { queued = false; decorate(); });
        }).observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', decorate);
        window.addEventListener('beforeprint', () => { printing = true; restore(); });
        window.addEventListener('afterprint', () => { printing = false; decorate(); });
        decorate();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
}());
