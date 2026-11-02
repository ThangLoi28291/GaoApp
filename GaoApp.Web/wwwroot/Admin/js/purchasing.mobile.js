/* UI-10: presentation of the original purchase forms, at phone widths only. */
(function () {
    'use strict';
    const selector = '[data-purchase-request-index], [data-purchase-order-index], #purchaseRequestPage, #purchaseRequestDetailsPage, #purchaseRequestPreparePage, #purchaseOrderPage, #purchaseOrderDetailsPage, #purchaseOrderCommercialPage, #receivingWorkbench';
    const tables = '#purchaseRequestPage .pr-lines-table, #purchaseRequestDetailsPage .pr-review-table, #purchaseRequestPreparePage .prc-lines-table, #purchaseOrderPage .po-line-table, #purchaseOrderCommercialPage table, #purchaseOrderDetailsPage table';
    function start() {
        const root = document.querySelector(selector);
        if (!root) return;
        const phone = window.matchMedia('(max-width: 767.98px)');
        const originals = new Map();
        function mark(node, name, value) {
            let attrs = originals.get(node);
            if (!attrs) { attrs = new Map(); originals.set(node, attrs); }
            if (!attrs.has(name)) attrs.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            for (const [node, attrs] of originals) {
                for (const [name, value] of attrs) {
                    if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
                }
            }
            originals.clear();
        }
        function decorate() {
            if (!phone.matches) { restore(); return; }
            mark(document.body, 'data-purchase-phone', 'true');
            mark(root, 'data-purchase-phone-root', 'true');
            document.querySelectorAll(tables).forEach(table => {
                mark(table, 'data-purchase-phone-table', 'true');
                mark(table, 'role', 'table');
                const headings = Array.from(table.tHead?.rows[0]?.cells || [], cell => cell.textContent.trim());
                if (table.tHead) mark(table.tHead, 'role', 'rowgroup');
                table.querySelectorAll('thead tr').forEach(row => mark(row, 'role', 'row'));
                table.querySelectorAll('thead th').forEach(cell => mark(cell, 'role', 'columnheader'));
                for (const body of table.tBodies) {
                    mark(body, 'role', 'rowgroup');
                    const occupied = [];
                    let product = '';
                    for (const row of body.rows) {
                        mark(row, 'role', 'row');
                        const identity = row.querySelector('.pod-product strong');
                        if (identity) product = identity.textContent.trim();
                        if (row.classList.contains('pod-product-group-related')) mark(row, 'data-purchase-product', product);
                        let column = 0;
                        for (const cell of row.cells) {
                            while (occupied[column] > 0) column++;
                            mark(cell, 'role', 'cell');
                            mark(cell, 'data-purchase-label', cell.getAttribute('data-label') || headings[column] || '');
                            if (cell.colSpan > 1 || /Sản phẩm|Mặt hàng|Dòng hàng/.test(headings[column] || '') || cell.querySelector('.pr-product-cell, .prc-product, .pr-product-name, .po-product-name, .pod-product') || cell.classList.contains('pod-product-cell')) mark(cell, 'data-purchase-wide', 'true');
                            if (cell.querySelector('button, .remove-line')) mark(cell, 'data-purchase-action', 'true');
                            for (let span = 0; span < cell.colSpan; span++) occupied[column + span] = cell.rowSpan;
                            column += cell.colSpan;
                        }
                        for (let i = 0; i < occupied.length; i++) occupied[i] = Math.max(0, (occupied[i] || 0) - 1);
                    }
                }
            });
            // Forget removed owner-rendered rows; never retain a second editor or draft.
            for (const node of originals.keys()) if (!node.isConnected) originals.delete(node);
        }
        let queued = false;
        const observer = new MutationObserver(function () {
            if (!phone.matches || queued) return;
            queued = true;
            queueMicrotask(function () { queued = false; decorate(); });
        });
        observer.observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', decorate);
        decorate();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
}());
