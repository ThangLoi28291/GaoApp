(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.PosShiftManagerDetail = api;
})(typeof window === 'undefined' ? globalThis : window, function () {
    'use strict';
    const labels = { Cash: 'Tiền mặt', BankManual: 'CK thủ công', BankAutomatic: 'CK tự động', Card: 'Thẻ', EWallet: 'Ví điện tử', Other: 'Khác', BankUnverified: 'CK cần kiểm tra' };
    const esc = x => String(x ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const money = x => Number(x || 0).toLocaleString('vi-VN') + ' đ';
    const date = x => x ? new Date(typeof x === 'string' && !/(Z|[+-]\d\d:\d\d)$/i.test(x) ? x + 'Z' : x).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' }) : '—';
    function breakdownHtml(data, interactive = false) {
        if (!data) return '<p class="text-muted">Chưa có chi tiết phương thức thanh toán.</p>';
        return '<div class="manager-payment-grid">' + (data.channels || []).map(x => {
            const tag = interactive ? 'button' : 'div';
            return `<${tag} class="manager-payment-card ${x.channel === 'BankUnverified' && x.salesAmount ? 'is-warning' : ''}" ${interactive ? 'type="button" data-payment-channel="' + esc(x.channel) + '"' : ''}>
                <span>${esc(x.label || labels[x.channel])}</span><strong>${money(x.salesAmount)}</strong>
                <small>${Number(x.paymentCount || 0)} giao dịch · Nhận ${money(x.recordedAmount)}</small></${tag}>`;
        }).join('') + '</div>' + (Number(data.difference) !== 0 ? `<div class="manager-detail-warning">Chi tiết thanh toán lệch tổng không tiền mặt của ca ${money(data.difference)}. Cần kiểm tra dữ liệu cũ hoặc điều chỉnh; số tổng ca là ${money(data.shiftNonCashSalesTotal)}.</div>` : '');
    }
    function paymentRows(data) {
        const rows = [];
        for (const order of data.orders || []) {
            let remaining = Math.max(0, Number(order.grandTotal || 0) - Number(order.deposit || 0));
            const active = (order.payments || []).filter(p => !p.cancelled && !p.isDebtCollection && p.amount > 0)
                .slice().sort((a, b) => (a.method === 'Cash' ? 2 : 1) - (b.method === 'Cash' ? 2 : 1) || a.id - b.id);
            const applied = new Map();
            if (!order.cancelled && ['Completed', 'Refunded'].includes(order.status)) for (const p of active) {
                const amount = Math.min(Number(p.amount), remaining); applied.set(p.id, amount); remaining -= amount;
            }
            for (const p of order.payments || []) rows.push({ ...p, orderId: order.id, number: order.number, customer: order.customer,
                orderStatus: order.status, orderCancelled: order.cancelled, applied: applied.get(p.id) || 0,
                channel: p.channel || (p.method === 'BankTransfer' ? p.bankVerified ? 'BankAutomatic' : 'BankUnverified' : p.method) });
        }
        return rows.sort((a, b) => b.id - a.id);
    }
    function paymentHtml(p) {
        return `<div class="manager-payment-row"><div><strong>${esc(p.number)} · ${esc(labels[p.channel] || p.method)}</strong>
            <small>${esc(p.customer)} · ${esc(date(p.paidAtUtc))} · ${esc(p.actor)}</small>
            <small>${esc(p.reference || 'Không có mã tham chiếu')} · ${esc(p.confirmation || '')}</small>
            ${p.cancelled || p.orderCancelled ? '<span class="badge bg-label-danger">Đã hủy · không tính doanh thu</span>' : p.isDebtCollection ? '<span class="badge bg-label-info">Thu công nợ · theo dõi riêng</span>' : !['Completed', 'Refunded'].includes(p.orderStatus) ? '<span class="badge bg-label-warning">Đơn chưa chốt · không tính doanh thu</span>' : ''}</div>
            <div class="text-end"><strong>${money(p.amount)}</strong><small>Tính doanh thu ${money(p.applied)}</small>
            <a href="/admin/pos/order-detail/${Number(p.orderId)}" target="_blank" rel="noopener">Xem đơn</a></div></div>`;
    }
    function render(data, target, recon) {
        let tab = 'orders', channel = '', page = 1;
        const tabs = { orders: 'Đơn hàng', payments: 'Thanh toán', cash: 'Thu / chi', refunds: 'Hoàn tiền', other: 'Cọc / công nợ', qrs: 'QR', adjustments: 'Điều chỉnh' };
        const gap = data.actualCash == null ? null : Number(data.actualCash) - Number(data.expectedCash);
        const formula = [ ['Tiền đầu ca', data.openingCash, 'opening'], ['+ Tiền mặt bán hàng', data.cashSales, 'orders'],
            ['+ Thu thêm vào két', data.cashIn, 'cash'], ['− Chi / rút khỏi két', data.cashOut, 'cash'], ['− Hoàn tiền mặt', data.cashRefunds, 'refunds'] ];
        const flagged = (data.orders || []).filter(o => recon.orderFlags(o).length > 0).length;
        const delta = Number(data.detailExpectedCash) - Number(data.expectedCash);
        target.innerHTML = `<div class="recon manager-reconciliation">
            <div class="d-flex flex-wrap justify-content-between gap-2 mb-3"><div><h5 class="fw-bold mb-1">Đối soát tiền trong ca</h5><small class="text-muted">Cập nhật ${esc(date(data.asOfUtc))}</small></div>
            <a href="/admin/pos-shift/reconciliation?shiftId=${Number(data.shiftId)}" target="_blank" rel="noopener">Mở đối soát đầy đủ ↗</a></div>
            <div class="manager-cash-check"><div><span>Quỹ dự kiến</span><strong>${money(data.expectedCash)}</strong></div>
            <div><span>Nhân viên thực đếm</span><strong>${data.actualCash == null ? 'Chưa chốt' : money(data.actualCash)}</strong></div>
            <div class="${gap == null || gap === 0 ? 'is-match' : gap < 0 ? 'is-short' : 'is-over'}"><span>Lệch quỹ</span><strong>${gap == null ? 'Chưa chốt' : gap === 0 ? 'Khớp' : (gap < 0 ? 'Thiếu ' : 'Thừa ') + money(Math.abs(gap))}</strong></div></div>
            <div class="manager-detail-columns"><div class="recon-card"><h6 class="fw-bold">Tiền dự kiến được tính thế nào?</h6><div class="recon-formula">
            ${formula.map(x => `<button type="button" data-evidence-tab="${x[2]}"><span>${esc(x[0])}</span><strong>${money(x[1])}</strong></button>`).join('')}
            <div class="d-flex justify-content-between pt-3 fw-bold"><span>= Quỹ dự kiến</span><span>${money(data.expectedCash)}</span></div></div>
            <p class="recon-help">Bấm từng khoản để xem chứng từ. Chuyển khoản, thẻ và ví không cộng vào két tiền mặt.</p></div>
            <div class="recon-card"><h6 class="fw-bold">Các mục cần kiểm tra</h6><p>${flagged} đơn có dấu hiệu cần đối chiếu: nhận dư, nhiều lần thu, chuyển khoản thủ công, khoản đã hủy hoặc chưa chốt.</p>
            <button type="button" class="btn btn-outline-primary btn-sm" data-evidence-attention> Xem các đơn cần kiểm tra</button>
            <div class="${delta || data.needsReconciliation ? 'manager-detail-warning' : 'recon-data-check'}">${delta ? `Tính từ giao dịch: ${money(data.detailExpectedCash)}; lệch số tổng ca ${money(delta)}.` : 'Tiền dự kiến tính từ giao dịch khớp số tổng ca.'}
            ${data.needsReconciliation ? '<br>Ca có điều chỉnh sau khi đóng, cần đối soát lại.' : ''}</div>
            <p class="recon-help">Các dấu hiệu giúp tra cứu; cần đối chiếu tiền thực tế và chứng từ để kết luận nguyên nhân.</p></div></div>
            <h6 class="fw-bold mt-4">Chi tiết không tiền mặt · ${money(data.nonCashSales)}</h6>${breakdownHtml(data.paymentBreakdown, true)}
            <p class="recon-help mb-3">Số lớn là tiền tính doanh thu, đã loại tiền nhận dư, công nợ thu riêng và khoản đã hủy.</p>
            <details class="recon-card mb-3" data-manager-denominations><summary class="fw-bold">Mệnh giá kiểm đếm</summary>${['Opening', 'Closing'].map(type => `<p class="mt-3 mb-1"><strong>${type === 'Opening' ? 'Đầu ca' : 'Chốt ca'}</strong></p><div class="recon-denoms">${(data.denominations || []).filter(x => x.type === type).map(x => `<span>${money(x.value)} × ${Number(x.quantity)} = ${money(x.amount)}</span>`).join('') || '<span>Chưa lưu bảng mệnh giá.</span>'}</div>`).join('')}</details>
            <div class="manager-evidence-tabs" role="tablist">${Object.entries(tabs).map(([key, text]) => `<button type="button" role="tab" data-manager-tab="${key}">${text}</button>`).join('')}</div>
            <div class="manager-evidence-filters"><input type="search" class="form-control" aria-label="Tìm trong chi tiết ca" placeholder="Tìm mã đơn, khách, ghi chú, mã giao dịch..." data-manager-search>
            <select class="form-select" aria-label="Lọc chi tiết ca" data-manager-scope><option value="all">Tất cả giao dịch</option><option value="attention">Cần kiểm tra</option><option value="cash">Có tiền mặt</option><option value="bank">Có chuyển khoản</option></select>
            <select class="form-select" aria-label="Phương thức thanh toán" data-manager-channel hidden><option value="">Tất cả phương thức</option>${Object.entries(labels).map(([key, text]) => `<option value="${key}">${text}</option>`).join('')}</select></div>
            <div data-manager-rows></div><div class="d-flex justify-content-between gap-2 align-items-center mt-3"><small data-manager-count></small><div class="d-flex gap-2"><button type="button" class="btn btn-outline-secondary btn-sm" data-manager-prev>Trước</button><button type="button" class="btn btn-outline-secondary btn-sm" data-manager-next>Sau</button></div></div></div>`;
        const $ = s => target.querySelector(s), payments = paymentRows(data);
        function list() {
            const query = $('[data-manager-search]').value.toLowerCase().trim(), scope = $('[data-manager-scope]').value;
            let rows;
            if (tab === 'payments') rows = payments.filter(p => (!channel || p.channel === channel)
                && (!query || `${p.number} ${p.customer} ${p.reference || ''} ${p.actor} ${p.amount}`.toLowerCase().includes(query))
                && (scope === 'all' || scope === 'cash' && p.method === 'Cash' || scope === 'bank' && p.method === 'BankTransfer'
                    || scope === 'attention' && (p.cancelled || p.orderCancelled || p.channel === 'BankManual' || p.channel === 'BankUnverified' || !['Completed', 'Refunded'].includes(p.orderStatus))));
            else rows = recon.filterRows(recon.rowsFor(data, tab), { query, scope });
            const pages = Math.max(1, Math.ceil(rows.length / 20)); page = Math.min(Math.max(page, 1), pages);
            $('[data-manager-rows]').innerHTML = rows.slice((page - 1) * 20, page * 20).map(r => tab === 'payments' ? paymentHtml(r) : recon.renderRow(data, tab, r)).join('') || '<div class="empty-box">Không có giao dịch phù hợp.</div>';
            $('[data-manager-count]').textContent = `${rows.length} mục · Trang ${page}/${pages}`;
            $('[data-manager-prev]').disabled = page <= 1; $('[data-manager-next]').disabled = page >= pages;
            $('[data-manager-channel]').hidden = tab !== 'payments'; $('[data-manager-channel]').value = channel;
            target.querySelectorAll('[data-manager-tab]').forEach(b => { b.classList.toggle('active', b.dataset.managerTab === tab); b.setAttribute('aria-selected', String(b.dataset.managerTab === tab)); });
        }
        const change = (next, nextChannel = '') => { tab = next; channel = nextChannel; page = 1; list(); };
        target.onclick = e => {
            const b = e.target.closest('button'); if (!b) return;
            if (b.hasAttribute('data-manager-tab')) change(b.dataset.managerTab);
            if (b.hasAttribute('data-payment-channel')) { $('[data-manager-search]').value = ''; $('[data-manager-scope]').value = 'all'; change('payments', b.dataset.paymentChannel); $('[data-manager-rows]').scrollIntoView({ block: 'nearest', behavior: 'smooth' }); }
            if (b.hasAttribute('data-evidence-tab')) { if (b.dataset.evidenceTab === 'opening') $('[data-manager-denominations]').open = true; else { $('[data-manager-scope]').value = 'all'; $('[data-manager-search]').value = ''; change(b.dataset.evidenceTab); } }
            if (b.hasAttribute('data-evidence-attention')) { $('[data-manager-scope]').value = 'attention'; $('[data-manager-search]').value = ''; change('orders'); }
            if (b.hasAttribute('data-manager-prev')) { page--; list(); } if (b.hasAttribute('data-manager-next')) { page++; list(); }
        };
        $('[data-manager-search]').oninput = () => { page = 1; list(); };
        $('[data-manager-scope]').onchange = () => { page = 1; list(); };
        $('[data-manager-channel]').onchange = e => change('payments', e.target.value);
        list();
    }
    let sequence = 0, controller;
    async function load(shiftId, target) {
        const request = ++sequence; controller?.abort(); controller = new AbortController();
        const currentController = controller;
        target.innerHTML = '<div class="recon-loading" role="status">Đang tải chứng từ đối soát...</div>';
        const timeout = setTimeout(() => currentController.abort(), 20000);
        try {
            const response = await fetch('/admin/pos-shift/reconciliation/data?shiftId=' + Number(shiftId), { signal: currentController.signal, cache: 'no-store' });
            const data = await response.json();
            if (!response.ok) throw Error(data.message || data.title || 'Không tải được chi tiết ca.');
            if (request !== sequence) return;
            render(data, target, window.PosShiftReconciliation);
        } catch (error) {
            if (request === sequence) { target.innerHTML = `<div class="manager-detail-warning" role="alert">${esc(error.name === 'AbortError' ? 'Chưa tải được chứng từ trong 20 giây.' : error.message)} <button class="btn btn-outline-primary btn-sm" type="button" data-manager-retry>Thử lại</button></div>`; target.querySelector('[data-manager-retry]').onclick = () => load(shiftId, target); }
        } finally { clearTimeout(timeout); }
    }
    function cancel() { sequence++; controller?.abort(); }
    return { load, cancel, render, breakdownHtml, paymentRows, paymentHtml };
});
