(function () {
    'use strict';
    function initialize() {
        const root = document.querySelector('[data-warehouse-receiving-index]');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const cards = new Map(), moved = [];
        let active = false, top, filters, warehouseSelect, sourceSelect, warehouse = '', source = '', optionsSignature = '';
        function element(tag, className, text) {
            const node = document.createElement(tag); node.className = className;
            if (text) node.textContent = text;
            return node;
        }
        function move(node, target, entries) {
            if (!node) return;
            const anchor = document.createComment('receiving-phone-position');
            node.before(anchor); entries.push({ node, anchor }); target.append(node);
        }
        function restore(entries) {
            entries.reverse().forEach(({ node, anchor }) => { if (anchor.parentNode) anchor.replaceWith(node); });
            entries.length = 0;
        }
        const warehouseKey = row => String(row.legalEntityId ?? '') + '\u001f' + String(row.warehouseName || '');
        function receipts(rows) {
            if (!active) return;
            const choices = new Map();
            for (const row of rows) choices.set(warehouseKey(row), (row.warehouseName || 'Chưa có kho') + (row.legalEntityName ? ' · ' + row.legalEntityName : ''));
            const sorted = [...choices].sort((a, b) => a[1].localeCompare(b[1], 'vi'));
            const signature = JSON.stringify(sorted);
            if (signature === optionsSignature) return;
            optionsSignature = signature;
            warehouseSelect.replaceChildren();
            for (const [value, text] of [['', 'Tất cả kho / HKD'], ...sorted]) {
                const option = element('option', '', text); option.value = value; warehouseSelect.append(option);
            }
            if (warehouse && !choices.has(warehouse)) warehouse = '';
            warehouseSelect.value = warehouse;
        }
        function matches(row) {
            if (!active) return true;
            return (!warehouse || warehouseKey(row) === warehouse)
                && (!source || (Number(row.purchaseOrderId) > 0 ? 'purchase-order' : 'unlinked') === source);
        }
        function rendered(rows) {
            for (const card of cards.keys()) if (!card.isConnected) cards.delete(card);
            if (!active) return;
            root.querySelectorAll('#wrReceiptList > .wr-card').forEach((card, index) => {
                if (cards.has(card)) return;
                const row = rows[index]; if (!row) return;
                const saved = { moved: [], created: [] }; cards.set(card, saved);
                const heading = element('div', 'wr-mobile-card-heading'); card.prepend(heading); saved.created.push(heading);
                move(card.querySelector('.wr-code'), heading, saved.moved);
                move(card.querySelector('.wr-badge'), heading, saved.moved);
                const context = element('div', 'wr-mobile-card-context');
                context.append(element('span', 'wr-mobile-source', Number(row.purchaseOrderId) > 0 ? 'Theo đơn mua' : 'Không gắn đơn mua'));
                if (row.supplierName) context.append(element('span', '', 'Nhà cung cấp: ' + row.supplierName));
                const track = element('ol', 'wr-mobile-workflow'); track.setAttribute('aria-label', 'Tiến trình xử lý phiếu');
                const pending = Number(row.status) === 2;
                ['Nhập hàng', 'Chờ duyệt'].forEach((label, i) => {
                    const step = element('li', '', i === 0 && Number(row.status) === 4 ? 'Cần sửa' : label);
                    if ((pending && i === 1) || (!pending && i === 0)) step.setAttribute('aria-current', 'step');
                    if (pending && i === 0) step.classList.add('is-done');
                    track.append(step);
                });
                context.append(track);
                card.querySelector('.wr-card-actions').before(context); saved.created.push(context);
            });
        }
        function refresh() { wrPage = 1; renderWarehouseReceivingReceipts(); }
        function enter() {
            if (active) return;
            active = true; root.dataset.wrPhone = ''; optionsSignature = '';
            top = element('div', 'wr-mobile-top'); root.prepend(top);
            move(document.getElementById('btnOpenCreateReceiving'), top, moved);
            filters = element('details', 'wr-mobile-filters');
            filters.append(element('summary', '', 'Lọc theo kho và nguồn phiếu'));
            const fields = element('div', 'wr-mobile-filter-fields'); filters.append(fields);
            function select(label, id, options) {
                const holder = element('div', '');
                const caption = element('label', '', label); caption.htmlFor = id;
                const control = element('select', 'form-select'); control.id = id;
                for (const [value, text] of options) { const option = element('option', '', text); option.value = value; control.append(option); }
                holder.append(caption, control); fields.append(holder); return control;
            }
            warehouseSelect = select('Kho / HKD', 'wrMobileWarehouse', []);
            sourceSelect = select('Nguồn phiếu', 'wrMobileSource', [['', 'Tất cả nguồn'], ['purchase-order', 'Theo đơn mua'], ['unlinked', 'Không gắn đơn mua']]);
            sourceSelect.value = source;
            warehouseSelect.addEventListener('change', () => { warehouse = warehouseSelect.value; refresh(); });
            sourceSelect.addEventListener('change', () => { source = sourceSelect.value; refresh(); });
            filters.append(element('p', 'wr-mobile-filter-note', 'Các số thống kê phía trên tính theo toàn bộ phiếu cần xử lý.'));
            root.querySelector('.wr-toolbar').after(filters);
            receipts(wrAllReceipts);
            if (warehouse || source) refresh();
            else rendered(wrFilteredReceipts.slice((wrPage - 1) * wrPageSize, wrPage * wrPageSize));
        }
        function leave() {
            if (!active) return;
            active = false;
            for (const [card, saved] of cards) {
                if (!card.isConnected) continue;
                restore(saved.moved); saved.created.forEach(node => node.remove());
            }
            cards.clear(); restore(moved); top.remove(); filters.remove(); delete root.dataset.wrPhone;
            if (warehouse || source) renderWarehouseReceivingReceipts();
        }
        window.GaoReceivingListMobile = { receipts, matches, rendered };
        function update() { if (phone.matches) enter(); else leave(); }
        phone.addEventListener('change', update); update();
        window.addEventListener('pageshow', update);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize, { once: true });
    else initialize();
}());
