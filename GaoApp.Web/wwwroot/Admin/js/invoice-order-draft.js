(function () {
    'use strict';
    const modal = document.getElementById('invoiceOrderDraftModal');
    if (!modal) return;
    const form = document.getElementById('invoiceOrderDraftSearch');
    const term = document.getElementById('invoiceOrderDraftTerm');
    const results = document.getElementById('invoiceOrderDraftResults');
    const message = document.getElementById('invoiceOrderDraftMessage');
    const selection = document.getElementById('invoiceOrderDraftSelection');
    const create = document.getElementById('invoiceOrderDraftCreate');
    const money = new Intl.NumberFormat('vi-VN');
    let selectedId = null, version = 0, timer = null, busy = false;

    function node(tag, className, text) {
        const item = document.createElement(tag);
        item.className = className || '';
        if (text != null) item.textContent = text;
        return item;
    }
    function status(text, error) {
        message.className = 'mt-3 ' + (error ? 'text-danger' : 'text-muted');
        message.textContent = text;
    }
    function clearSelection() {
        selectedId = null;
        create.disabled = true;
        selection.replaceChildren();
        selection.hidden = true;
    }
    function detailUrl(id) {
        const url = new URL(modal.dataset.detailUrl, location.origin);
        url.searchParams.set('id', id);
        return url.toString();
    }
    async function request(url, options) {
        const response = await fetch(url, { credentials: 'same-origin', ...options });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data) throw new Error(data?.message || 'Không xử lý được yêu cầu. Vui lòng thử lại.');
        return data;
    }
    function orderLabel(order) {
        return `#${order.orderId} · ${order.orderNumber || 'Đơn POS'}`;
    }
    function showSelection(data) {
        selection.replaceChildren(node('div', 'fw-semibold', orderLabel(data.order)));
        selection.hidden = false;
        results.replaceChildren();
        const refs = data.invoices || [];
        if (refs.length) {
            status(data.created ? 'Đã tạo hóa đơn trống. Chọn hóa đơn để thêm sản phẩm.' : 'Báo trùng: đơn POS này đã có hóa đơn. Không tạo thêm.', !data.created);
            for (const item of refs) {
                const link = node('a', 'btn btn-outline-primary mt-2 me-2', `Mở hóa đơn #${item.id}${item.legalEntityName ? ' · ' + item.legalEntityName : ''}`);
                link.href = detailUrl(item.id);
                selection.append(link);
            }
            selectedId = null;
            create.disabled = true;
        } else {
            selectedId = data.order.orderId;
            status('Chưa có hóa đơn cho đơn này.');
            selection.append(node('p', 'mt-2 mb-0', 'Hóa đơn mới có 0 sản phẩm, ở chế độ phát hành thủ công. Nếu đơn có phân bổ nhiều HKD, mỗi HKD có một hóa đơn trống.'));
            create.disabled = false;
        }
    }
    async function choose(orderId) {
        if (busy) return;
        const current = ++version;
        clearSelection();
        status('Đang kiểm tra hóa đơn của đơn POS…');
        try {
            const url = new URL(modal.dataset.previewUrl, location.origin);
            url.searchParams.set('orderId', orderId);
            const data = await request(url);
            if (current !== version) return;
            showSelection(data);
        } catch (error) { if (current === version) status(error.message, true); }
    }
    async function search() {
        clearTimeout(timer);
        if (busy) return;
        const current = ++version;
        clearSelection();
        results.replaceChildren();
        const query = term.value.trim();
        if (!query) { status('Nhập mã hoặc số thứ tự đơn hàng để tìm.'); return; }
        status('Đang tìm đơn POS…');
        try {
            const url = new URL(modal.dataset.searchUrl, location.origin);
            url.searchParams.set('term', query);
            const items = await request(url);
            if (current !== version) return;
            status(items.length ? 'Chọn đơn POS bên dưới (tối đa 20 kết quả).' : 'Không tìm thấy đơn POS đã hoàn tất.');
            for (const order of items) {
                const button = node('button', 'list-group-item list-group-item-action');
                button.type = 'button';
                button.dataset.orderId = order.orderId;
                button.append(node('div', 'fw-semibold', orderLabel(order)));
                const date = order.completedAtUtc ? new Date(order.completedAtUtc + (/(Z|[+-]\d{2}:?\d{2})$/i.test(order.completedAtUtc) ? '' : 'Z'))
                    .toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' }) : '';
                button.append(node('div', 'small text-muted', `${date} · Thanh toán ${money.format(order.grandTotal)} đ`));
                button.append(node('span', `badge mt-1 ${order.hasInvoice ? 'bg-label-warning' : 'bg-label-success'}`,
                    order.hasInvoice ? 'Đã có hóa đơn' : 'Chưa có hóa đơn'));
                button.addEventListener('click', () => choose(order.orderId));
                results.append(button);
            }
        } catch (error) { if (current === version) status(error.message, true); }
    }
    form.addEventListener('submit', event => { event.preventDefault(); search(); });
    term.addEventListener('input', () => {
        if (busy) return;
        ++version;
        clearSelection();
        results.replaceChildren();
        clearTimeout(timer);
        timer = setTimeout(search, 300);
    });
    term.addEventListener('keydown', event => {
        if (event.key === 'ArrowDown' && results.firstElementChild) {
            event.preventDefault(); results.firstElementChild.focus();
        }
    });
    results.addEventListener('keydown', event => {
        if (!['ArrowDown', 'ArrowUp'].includes(event.key)) return;
        event.preventDefault();
        const next = event.key === 'ArrowDown' ? event.target.nextElementSibling : event.target.previousElementSibling;
        if (next) next.focus(); else if (event.key === 'ArrowUp') term.focus();
    });
    create.addEventListener('click', async () => {
        if (!selectedId || busy) return;
        busy = true;
        create.disabled = true;
        term.disabled = true;
        form.querySelector('button').disabled = true;
        status('Đang tạo hóa đơn trống…');
        try {
            const body = new URLSearchParams({ orderId: selectedId,
                __RequestVerificationToken: form.querySelector('[name="__RequestVerificationToken"]').value });
            const data = await request(modal.dataset.createUrl, { method: 'POST', body });
            showSelection(data);
            if (data.created && data.invoices.length === 1) location.assign(detailUrl(data.invoices[0].id));
        } catch (error) { status(error.message, true); create.disabled = false; }
        finally { busy = false; term.disabled = false; form.querySelector('button').disabled = false; }
    });
    modal.addEventListener('shown.bs.modal', () => term.focus());
    modal.addEventListener('hidden.bs.modal', () => { ++version; clearTimeout(timer); });
})();
