/* Phone presentation only. Command, draft, revision, source and financial owners stay in their original modules. */
(() => {
    'use strict';
    const root = document.querySelector('.delivery-workspace');
    if (!root) return;
    const picking = root.classList.contains('delivery-picking-workspace');
    const outcomes = root.classList.contains('delivery-outcomes-workspace');
    const list = root.querySelector(picking ? '.delivery-list-panel' : outcomes ? '.outcome-sidebar' : '.delivery-trip-sidebar');
    const detail = root.querySelector(picking ? '#deliveryDetail' : outcomes ? '#outcomeDetail' : '#deliveryTripDetail');
    if (!list || !detail) return;
    const phone = matchMedia('(max-width: 767.98px)');
    const saved = new Map();
    let navigation, listButton, detailButton, lastIdentity = '', screen = 'list', frame, sourcePlacement, pendingSelection;
    function attr(node, name, value) {
        if (!saved.has(node)) saved.set(node, new Map());
        if (!saved.get(node).has(name)) saved.get(node).set(name, node.getAttribute(name));
        if (node.getAttribute(name) !== value) node.setAttribute(name, value);
    }
    function identity() {
        if (picking) return detail.hidden ? '' : detail.querySelector('.delivery-detail-heading .delivery-muted')?.textContent.split(' · ')[0] || '';
        if (outcomes) return root.querySelector('.outcome-stop[aria-current="true"]')?.dataset.orderId || '';
        return detail.dataset.editing === 'true' ? 'edit:' + (detail.dataset.tripId || 'new') : detail.dataset.tripId || '';
    }
    function choose(value) {
        screen = value;
        root.setAttribute('data-delivery-phone-screen', screen);
        listButton.setAttribute('aria-pressed', String(screen === 'list'));
        detailButton.setAttribute('aria-pressed', String(screen === 'detail'));
    }
    function sync() {
        frame = null;
        if (!phone.matches) {
            if (sourcePlacement) {
                if (sourcePlacement.marker.isConnected) sourcePlacement.marker.replaceWith(sourcePlacement.node);
                else sourcePlacement.node.remove();
                sourcePlacement = null;
            }
            navigation?.remove(); navigation = null;
            pendingSelection = null;
            root.removeAttribute('data-delivery-phone-screen');
            for (const [node, attrs] of saved) for (const [name, value] of attrs) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            saved.clear(); return;
        }
        for (const node of saved.keys()) if (!root.contains(node)) saved.delete(node);
        if (picking) {
            if (sourcePlacement && !sourcePlacement.marker.isConnected) { sourcePlacement.node.remove(); sourcePlacement = null; }
            const source = detail.querySelector('#deliveryInventory .delivery-inventory-origin');
            const recipient = detail.querySelector('.delivery-recipient');
            if (source && recipient) {
                const marker = document.createComment('phone source position');
                source.before(marker); recipient.after(source); sourcePlacement = {node: source, marker};
            }
        }
        if (!navigation) {
            navigation = document.createElement('div');
            navigation.className = 'delivery-phone-navigation';
            navigation.setAttribute('role', 'group'); navigation.setAttribute('aria-label', 'Chọn phần đang xem');
            const control = (label, value) => {
                const button = document.createElement('button'); button.type = 'button'; button.textContent = label;
                button.dataset.deliveryPhoneView = value;
                button.addEventListener('click', () => { pendingSelection = null; choose(value); }); navigation.append(button); return button;
            };
            listButton = control(outcomes ? 'Các điểm giao' : picking ? 'Danh sách đơn' : 'Danh sách chuyến', 'list');
            detailButton = control(outcomes ? 'Bàn giao từng đơn' : picking ? 'Đang soạn' : 'Chi tiết chuyến', 'detail');
            (root.querySelector('.delivery-search') || root.querySelector('.delivery-heading')).after(navigation);
        }
        const current = identity();
        if (pendingSelection && current && detail.firstElementChild !== pendingSelection.firstChild) { screen = 'detail'; pendingSelection = null; }
        if (current && current !== lastIdentity) screen = 'detail';
        if (!current) screen = 'list';
        lastIdentity = current; detailButton.disabled = !current; choose(screen);
        for (const table of root.querySelectorAll('.delivery-trip-quantity-table,.outcome-table')) {
            attr(table, 'role', 'table');
            for (const group of table.querySelectorAll('thead,tbody')) attr(group, 'role', 'rowgroup');
            const headers = [...table.querySelectorAll('thead th')];
            for (const header of headers) attr(header, 'role', 'columnheader');
            for (const row of table.querySelectorAll('tr')) attr(row, 'role', 'row');
            for (const row of table.querySelectorAll('tbody tr')) for (const [index, cell] of [...row.cells].entries()) {
                attr(cell, 'role', cell.tagName === 'TH' ? 'rowheader' : 'cell');
                if (index && headers[index]) attr(cell, 'data-delivery-phone-label', headers[index].textContent.trim());
            }
        }
    }
    function schedule() { if (!frame) frame = requestAnimationFrame(sync); }
    root.addEventListener('click', event => {
        const item = event.target.closest('.delivery-recent,.delivery-trip-list-item,.outcome-stop');
        if (phone.matches && item && !item.disabled) { pendingSelection = {firstChild: detail.firstElementChild}; schedule(); }
    });
    root.addEventListener('submit', event => {
        if (phone.matches && event.target.id === 'deliverySearch') { pendingSelection = {firstChild: detail.firstElementChild}; schedule(); }
    });
    new MutationObserver(schedule).observe(root, {childList: true, subtree: true, attributes: true, attributeFilter: ['hidden', 'data-trip-id', 'data-editing', 'aria-current']});
    phone.addEventListener('change', schedule);
    sync();
})();
