/* UI-12: arrange existing adjustment controls without owning document state or commands. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('[data-inventory-adjustment-index], .adj-page');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map();
        let queued = false, revaluationNotice = null;
        function mark(node, name, value) {
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            revaluationNotice?.remove(); revaluationNotice = null;
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function restoreMark(node, name) {
            const attributes = originals.get(node);
            if (!attributes?.has(name)) return;
            const value = attributes.get(name);
            if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            attributes.delete(name);
            if (!attributes.size) originals.delete(node);
        }
        function decorateQuickView() {
            const body = root.querySelector('#inventoryAdjustmentQuickViewBody');
            const type = body?.querySelector('.adj-index-quick-overview > div:nth-child(2) > strong');
            // Use the original server type label, never a reason or the renderer's signed quantity.
            if (type?.textContent.trim() !== 'Điều chỉnh giá vốn') {
                revaluationNotice?.remove(); revaluationNotice = null;
                if (body) restoreMark(body, 'data-adjustment-phone-revaluation');
                return;
            }
            const lines = body.querySelector('.adj-index-quick-lines');
            if (!lines) return;
            mark(body, 'data-adjustment-phone-revaluation', 'true');
            if (!revaluationNotice?.isConnected) {
                revaluationNotice = document.createElement('p');
                revaluationNotice.className = 'adj-phone-revaluation-note';
                revaluationNotice.setAttribute('role', 'note');
                revaluationNotice.textContent = 'Điều chỉnh giá vốn không làm thay đổi số lượng tồn kho. Mở phiếu để đối chiếu lượng hàng và giá vốn áp dụng.';
                lines.before(revaluationNotice);
            }
        }
        function decorateLines() {
            // Detail already has a native mobile renderer. Only the editable table needs labels.
            const table = root.querySelector('#AdjustmentLineTableBody')?.closest('table');
            if (!table?.tHead?.rows[0]) return;
            const headers = Array.from(table.tHead.rows[0].cells, cell => cell.textContent.trim());
            mark(table, 'data-adjustment-phone-table', 'true');
            mark(table, 'role', 'table');
            mark(table.tHead, 'role', 'rowgroup');
            for (const row of table.tHead.rows) {
                mark(row, 'role', 'row');
                for (const cell of row.cells) mark(cell, 'role', 'columnheader');
            }
            for (const body of table.tBodies) {
                mark(body, 'role', 'rowgroup');
                for (const row of body.rows) {
                    mark(row, 'role', 'row');
                    for (const [index, cell] of Array.from(row.cells).entries()) {
                        const label = headers[index] || 'Thao tác';
                        mark(cell, 'role', 'cell');
                        mark(cell, 'data-adjustment-phone-label', cell.colSpan > 1 ? '' : label);
                        if (cell.colSpan > 1 || index === 1 || index >= 7) mark(cell, 'data-adjustment-phone-wide', 'true');
                        if (index === 0 && cell.colSpan === 1) mark(cell, 'data-adjustment-phone-index', 'true');
                    }
                }
            }
        }
        function refresh() {
            queued = false;
            if (!phone.matches) { restore(); return; }
            for (const node of originals.keys()) if (!node.isConnected) originals.delete(node);
            mark(document.body, 'data-adjustment-phone', 'true');
            mark(root, 'data-adjustment-phone-root', 'true');
            decorateLines();
            decorateQuickView();
        }
        function schedule() {
            if (queued) return;
            queued = true;
            requestAnimationFrame(refresh);
        }
        new MutationObserver(schedule).observe(root, { childList: true, subtree: true });
        phone.addEventListener('change', schedule);
        refresh();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
