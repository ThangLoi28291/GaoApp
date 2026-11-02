/* UI-19: phone presentation. Native forms, permissions, preview and playback retain ownership. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('.display-promotion-page');
        const modal = document.getElementById('promotionModal');
        if (!root || !modal) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map(), added = new Set();
        let printing = false;
        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            for (const node of added) node.remove();
            added.clear();
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function heading(before, text) {
            if (!before) return;
            const node = document.createElement('h2');
            node.className = 'dpm-section-title'; node.textContent = text;
            before.before(node); added.add(node);
        }
        function paint() {
            restore();
            if (printing || !phone.matches) return;
            mark(root, 'data-display-promotion-phone', '');
            const table = root.querySelector('.dp-table');
            if (table) {
                mark(table, 'role', 'table');
                if (table.tHead) mark(table.tHead, 'role', 'rowgroup');
                [...(table.tHead?.rows || [])].forEach(row => {
                    mark(row, 'role', 'row'); [...row.cells].forEach(cell => mark(cell, 'role', 'columnheader'));
                });
                [...table.tBodies].forEach(body => {
                    mark(body, 'role', 'rowgroup');
                    [...body.rows].forEach(row => {
                        mark(row, 'role', 'row'); [...row.cells].forEach(cell => mark(cell, 'role', 'cell'));
                    });
                });
            }
            modal.querySelectorAll('input:not([type="hidden"]),select,textarea').forEach(control => {
                if (control.hasAttribute('aria-label') || control.hasAttribute('aria-labelledby') || control.labels?.length) return;
                const label = control.parentElement?.querySelector(':scope > .form-label');
                if (label) mark(control, 'aria-label', label.textContent.trim());
            });
            const close = modal.querySelector('.btn-close');
            if (close && !close.hasAttribute('aria-label')) mark(close, 'aria-label', 'Đóng nội dung màn hình khách');
            const firstColumn = modal.querySelector('.modal-body > .row > .col-lg-7');
            if (firstColumn) {
                heading(firstColumn.firstElementChild, 'Nội dung');
                heading(firstColumn.querySelector('#mediaType')?.closest('.row'), 'Cách trình chiếu');
                heading(firstColumn.querySelector('#mediaFile')?.parentElement, 'Hình ảnh và màu sắc');
                heading(firstColumn.querySelector('#startAt')?.closest('.row'), 'Lịch hiển thị');
            }
            const preview = document.getElementById('livePreview');
            if (preview) {
                const note = document.createElement('p');
                note.className = 'dpm-preview-note';
                note.textContent = 'Xem trước nội dung cho màn hình khách tại quầy. Hình ảnh và video giữ nguyên tỷ lệ.';
                preview.after(note); added.add(note);
            }
        }
        phone.addEventListener('change', paint);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; paint(); });
        paint();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
