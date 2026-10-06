(async function () {
    'use strict';
    const $ = id => document.getElementById(id), money = n => Number(n).toLocaleString('vi-VN', { maximumFractionDigits: 0 }) + ' đ';
    const quantity = n => Number(n).toLocaleString('vi-VN', { maximumFractionDigits: 4 });
    const price = n => Number(n).toLocaleString('vi-VN', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' đ';
    const states = { Created: 'Chờ soạn', Picking: 'Đang soạn', Ready: 'Chờ giao', Delivering: 'Đang giao', Returned: 'Chờ bàn giao', Completed: 'Hoàn thành', Cancelled: 'Đã hủy' };
    function node(tag, text, cls) { const e = document.createElement(tag); e.textContent = text; if (cls) e.className = cls; return e; }
    async function get(path) { const r = await fetch('/admin/api/deliveries' + path, { cache: 'no-store', credentials: 'same-origin' }); const d = await r.json(); if (!r.ok) throw new Error(d.message || 'Không có quyền xem hoặc không tìm thấy đơn.'); return d; }
    function key(raw) { raw = raw.trim(); if (/^https?:\/\//i.test(raw)) { const u = new URL(raw); if (u.origin !== location.origin || u.pathname !== '/admin/deliveries') throw new Error('QR không thuộc cửa hàng này.'); return u.searchParams.get('key') || ''; } return raw; }
    async function lookup(raw) {
        $('deliveryDetail').hidden = true; $('deliveryPageMessage').textContent = 'Đang tra đơn…';
        try {
            const k = key(raw), d = await get('/lookup?key=' + encodeURIComponent(k));
            const panel = $('deliveryDetail'); panel.replaceChildren(node('h3', d.code), node('span', states[d.state] || d.state, 'delivery-badge'),
                node('p', 'Tạo ' + new Date(d.createdAtUtc).toLocaleString('vi-VN') + ' · Quầy #' + d.createdTerminalId + ' · Giỏ #' + d.sourceCartId),
                node('h5', d.recipientName + ' · ' + d.recipientPhone), node('p', d.recipientAddress));
            if (d.note) panel.append(node('p', 'Ghi chú: ' + d.note));
            const table = node('table', '', 'delivery-lines'); const head = document.createElement('thead'), row = document.createElement('tr');
            ['Hàng', 'SL', 'Đơn giá', 'Thành tiền'].forEach(x => row.append(node('th', x))); head.append(row); table.append(head);
            const body = document.createElement('tbody'); for (const line of d.lines) { const tr = document.createElement('tr'); [line.itemName, quantity(line.orderedQuantity) + ' ' + line.unitName, price(line.unitPrice), money(line.net)].forEach(x => tr.append(node('td', x))); body.append(tr); } table.append(body);
            const bill = node('a', 'Mở / in phiếu A5', 'btn btn-primary'); bill.href = '/admin/deliveries/' + d.id + '/bill'; bill.target = '_blank'; bill.rel = 'noopener';
            panel.append(table, node('p', 'Giá trị dự kiến: ' + money(d.quotedTotal), 'delivery-total'), node('p', 'Thanh toán theo lượng thực giao. Đơn mới chưa thu tiền.'), bill); panel.hidden = false;
            $('deliveryPageMessage').textContent = ''; $('deliveryKey').value = d.code;
            history.replaceState(null, '', '/admin/deliveries?key=' + encodeURIComponent(k));
        } catch (e) { $('deliveryPageMessage').textContent = e.message; }
    }
    $('deliverySearch').addEventListener('submit', e => { e.preventDefault(); lookup($('deliveryKey').value); });
    try { const list = await get(''); if (!list.length) $('deliveryRecent').append(node('p', 'Chưa có đơn giao.')); for (const d of list) { const b = node('button', d.code + ' · ' + d.recipientName + ' · ' + (states[d.state] || d.state), 'delivery-recent'); b.type = 'button'; b.addEventListener('click', () => lookup(d.code)); $('deliveryRecent').append(b); } }
    catch (e) { $('deliveryPageMessage').textContent = e.message; }
    const initial = new URLSearchParams(location.search).get('key'); if (initial) await lookup(initial);
})();
