(function () {
    'use strict';

    let currentPage = 1;
    const pageSize = 20;
    let totalPages = 1;
    let selectedVoucherId = 0;
    let selectedVoucherStatus = '';

    const detailModalEl = document.getElementById('voucherDetailModal');
    const cancelModalEl = document.getElementById('cancelVoucherModal');
    const logsModalEl = document.getElementById('voucherLogsModal');

    const detailModal = detailModalEl && window.bootstrap
        ? bootstrap.Modal.getOrCreateInstance(detailModalEl)
        : null;

    const cancelModal = cancelModalEl && window.bootstrap
        ? bootstrap.Modal.getOrCreateInstance(cancelModalEl)
        : null;

    const logsModal = logsModalEl && window.bootstrap
        ? bootstrap.Modal.getOrCreateInstance(logsModalEl)
        : null;

    function money(value) {
        return new Intl.NumberFormat('vi-VN').format(Number(value || 0));
    }

    function dateText(value) {
        if (!value) return '--';
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return '--';
        return d.toLocaleString('vi-VN');
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function statusText(status) {
        if (status === 'Available') return 'Chưa sử dụng';
        if (status === 'Used') return 'Đã sử dụng';
        if (status === 'Cancelled') return 'Đã hủy';
        if (status === 'Expired') return 'Hết hạn';
        if (status === 'Locked') return 'Đã khóa';
        return status || '--';
    }

    function statusClass(status) {
        if (status === 'Available') return 'rv-status-badge rv-status-available';
        if (status === 'Used') return 'rv-status-badge rv-status-used';
        if (status === 'Cancelled') return 'rv-status-badge rv-status-cancelled';
        if (status === 'Expired') return 'rv-status-badge rv-status-expired';
        if (status === 'Locked') return 'rv-status-badge rv-status-expired';
        return 'rv-status-badge';
    }

    async function fetchJson(url, options) {
        const response = await fetch(url, {
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json'
            },
            ...options
        });

        if (!response.ok) {
            const text = await response.text();
            throw new Error(text || 'Không thể xử lý yêu cầu.');
        }

        return await response.json();
    }

    function buildQuery() {
        const keyword = (document.getElementById('txtKeyword')?.value || '').trim();
        const status = document.getElementById('ddlStatus')?.value || '';
        const fromDate = document.getElementById('fromDate')?.value || '';
        const toDate = document.getElementById('toDate')?.value || '';

        const qs = new URLSearchParams();
        qs.set('page', String(currentPage));
        qs.set('pageSize', String(pageSize));

        if (keyword) qs.set('keyword', keyword);
        if (status) qs.set('status', status);
        if (fromDate) qs.set('fromDate', fromDate);
        if (toDate) qs.set('toDate', toDate);

        return qs.toString();
    }

    async function loadCounts() {
        const data = await fetchJson('/admin/api/customers/reward-vouchers/status-counts');

        const available = Number(data.Available ?? data.available ?? 0);
        const used = Number(data.Used ?? data.used ?? 0);
        const cancelled = Number(data.Cancelled ?? data.cancelled ?? 0);
        const expired = Number(data.Expired ?? data.expired ?? 0);
        const locked = Number(data.Locked ?? data.locked ?? 0);

        document.getElementById('totalVoucherCount').textContent =
            money(available + used + cancelled + expired + locked);

        document.getElementById('availableVoucherCount').textContent = money(available);
        document.getElementById('usedVoucherCount').textContent = money(used);
        document.getElementById('cancelledVoucherCount').textContent = money(cancelled + expired + locked);
    }

    async function loadList() {
        const tbody = document.getElementById('voucherTableBody');

        tbody.innerHTML = `
            <tr>
                <td colspan="8" class="text-center text-muted py-5">
                    Đang tải dữ liệu...
                </td>
            </tr>
        `;

        const data = await fetchJson('/admin/api/customers/reward-vouchers?' + buildQuery());

        const items = data.items || data.Items || [];
        const totalItems = Number(data.totalItems || data.TotalItems || 0);
        totalPages = Number(data.totalPages || data.TotalPages || 1);

        if (!items.length) {
            tbody.innerHTML = `
                <tr>
                    <td colspan="8" class="text-center text-muted py-5">
                        Không có phiếu thưởng phù hợp.
                    </td>
                </tr>
            `;
        } else {
            tbody.innerHTML = items.map(function (x) {
                const status = x.status || x.Status || '';

                return `
                    <tr>
                        <td><div class="rv-code">${escapeHtml(x.voucherCode || '')}</div></td>
                        <td><div class="rv-customer-name">${escapeHtml(x.customerName || '')}</div></td>
                        <td>${escapeHtml(x.customerPhone || '')}</td>
                        <td class="text-end fw-bold">${money(x.value)} đ</td>
                        <td>${dateText(x.issuedAtUtc)}</td>
                        <td><span class="${statusClass(status)}">${statusText(status)}</span></td>
                        <td class="text-end">
                            <button type="button"
                                    class="btn btn-sm btn-outline-primary"
                                    data-view-voucher-id="${Number(x.id || 0)}">
                                Xem
                            </button>
                        </td>
                    </tr>
                `;
            }).join('');
        }

        document.getElementById('pagingInfo').textContent =
            `Trang ${currentPage}/${totalPages} · ${money(totalItems)} phiếu`;

        document.getElementById('btnPrev').disabled = currentPage <= 1;
        document.getElementById('btnNext').disabled = currentPage >= totalPages;
    }

    async function loadAll() {
        await Promise.all([
            loadCounts(),
            loadList()
        ]);
    }

    function setText(id, value) {
        const el = document.getElementById(id);
        if (el) el.textContent = value ?? '--';
    }

    function updateDetailButtons(status) {
        selectedVoucherStatus = status;

        document.getElementById('btnOpenCancelVoucher')?.classList.add('d-none');
        document.getElementById('btnLockVoucher')?.classList.add('d-none');
        document.getElementById('btnUnlockVoucher')?.classList.add('d-none');

        if (status === 'Available') {
            document.getElementById('btnOpenCancelVoucher')?.classList.remove('d-none');
            document.getElementById('btnLockVoucher')?.classList.remove('d-none');
        }

        if (status === 'Locked') {
            document.getElementById('btnUnlockVoucher')?.classList.remove('d-none');
        }
    }

    async function openDetail(id) {
        selectedVoucherId = Number(id || 0);
        if (!selectedVoucherId) return;

        setText('detailVoucherCodeHeader', 'Đang tải...');
        updateDetailButtons('');

        detailModal?.show();

        const x = await fetchJson(`/admin/api/customers/reward-vouchers/${selectedVoucherId}`);
        const status = x.status || x.Status || '';

        setText('detailVoucherCodeHeader', x.voucherCode || '');
        setText('detailVoucherCode', x.voucherCode || '');
        setText('detailCustomerName', x.customerName || '');
        setText('detailCustomerPhone', x.customerPhone || '');
        setText('detailVoucherValue', money(x.value) + ' đ');
        setText('detailRequiredAmount', money(x.requiredAmount) + ' đ');
        setText('detailIssuedAt', dateText(x.issuedAtUtc));
        setText('detailUsedAt', dateText(x.usedAtUtc));
        setText('detailStatus', statusText(status));
        setText('detailUsedOrderId', x.usedOrderId ? '#' + x.usedOrderId : '--');
        setText('detailReferenceCode', x.referenceCode || '--');
        setText('detailDescription', x.description || '--');

        updateDetailButtons(status);
    }

    async function cancelVoucher() {
        if (!selectedVoucherId) return;

        const reason = document.getElementById('cancelReason')?.value || '';

        await fetchJson(`/admin/api/customers/reward-vouchers/${selectedVoucherId}/cancel`, {
            method: 'POST',
            body: JSON.stringify({ reason: reason })
        });

        cancelModal?.hide();
        detailModal?.hide();

        await loadAll();
    }

    async function lockVoucher() {
        if (!selectedVoucherId) return;

        const reason = prompt('Nhập lý do khóa voucher:', '') || '';

        await fetchJson(`/admin/api/customers/reward-vouchers/${selectedVoucherId}/lock`, {
            method: 'POST',
            body: JSON.stringify({ reason: reason })
        });

        await openDetail(selectedVoucherId);
        await loadAll();
    }

    async function unlockVoucher() {
        if (!selectedVoucherId) return;

        const reason = prompt('Nhập lý do mở khóa voucher:', '') || '';

        await fetchJson(`/admin/api/customers/reward-vouchers/${selectedVoucherId}/unlock`, {
            method: 'POST',
            body: JSON.stringify({ reason: reason })
        });

        await openDetail(selectedVoucherId);
        await loadAll();
    }

    async function viewVoucherLogs() {
        if (!selectedVoucherId) return;

        const box = document.getElementById('voucherLogsBox');
        if (box) {
            box.innerHTML = `<div class="text-muted">Đang tải nhật ký...</div>`;
        }

        logsModal?.show();

        const logs = await fetchJson(`/admin/api/customers/reward-vouchers/${selectedVoucherId}/logs`);
        const items = logs.items || logs.Items || logs;

        if (!Array.isArray(items) || !items.length) {
            box.innerHTML = `<div class="text-muted">Chưa có nhật ký.</div>`;
            return;
        }

        box.innerHTML = items.map(function (x) {
            return `
                <div class="border rounded-3 p-3">
                    <div class="d-flex justify-content-between gap-3">
                        <div>
                            <div class="fw-bold">${escapeHtml(x.action || x.Action || '')}</div>
                            <div class="text-muted small">${escapeHtml(x.note || x.Note || '')}</div>
                            ${(x.orderId || x.OrderId) ? `
                                <div class="small mt-1">Đơn liên quan: #${x.orderId || x.OrderId}</div>
                            ` : ''}
                        </div>
                        <div class="text-muted small text-end">
                            ${dateText(x.createdAtUtc || x.CreatedAtUtc)}
                        </div>
                    </div>
                </div>
            `;
        }).join('');
    }

    async function printVoucher() {
        if (!selectedVoucherId) return;

        const x = await fetchJson(`/admin/api/customers/reward-vouchers/${selectedVoucherId}/print`);

        const voucherCode = x.voucherCode || x.VoucherCode || '';
        const customerName = x.customerName || x.CustomerName || '';
        const customerPhone = x.customerPhone || x.CustomerPhone || '';
        const value = x.value || x.Value || 0;
        const issuedAt = x.issuedAtUtc || x.IssuedAtUtc;
        const status = x.status || x.Status || '';

        const html = `
<!doctype html>
<html>
<head>
    <meta charset="utf-8">
    <title>In phiếu giảm giá</title>
<script src="/lib/jsbarcode/JsBarcode.all.min.js"><\/script>

    <style>
    @page {
    size: 80mm auto;
    margin: 0;
}

html,
body {
    width: 80mm;
    margin: 0;
    padding: 0;
    font-family: Arial, sans-serif;
}

      .voucher {
    width: 72mm;
    box-sizing: border-box;
    border: 1px dashed #333;
    border-radius: 6px;
    padding: 4mm;
    margin: 0 auto;
}

        .title {
            text-align: center;
          font-size: 18px;
            font-weight: 800;
            margin-bottom: 8px;
        }

        .code {
            text-align: center;
    font-size: 16px;
    font-weight: 800;
    margin: 6px 0;
        }

        .value {
    text-align: center;
    font-size: 24px;
    font-weight: 900;
    margin: 8px 0;
}

        .barcode-box {
            margin: 10px 0 12px;
            text-align: center;
        }

        #voucherBarcode {
            max-width: 100%;
        }

        .row {
    display: flex;
    justify-content: space-between;
    margin: 4px 0;
    font-size: 12px;
}
        .row strong {
            text-align: right;
        }

        .note {
            margin-top: 14px;
            font-size: 12px;
            color: #555;
            border-top: 1px solid #ddd;
            padding-top: 10px;
            line-height: 1.45;
        }

        @media print {
            body { padding: 0; }
            .voucher { page-break-inside: avoid; }
        }
    </style>
</head>

<body>
    <div class="voucher">
        <div class="title">PHIẾU GIẢM GIÁ</div>

      

      <div class="barcode-box" id="voucherBarcodeBox">
    <svg id="voucherBarcode"></svg>
</div>

        <div class="value">${money(value)} đ</div>

        <div class="row">
            <span>Khách hàng</span>
            <strong>${escapeHtml(customerName)}</strong>
        </div>

        <div class="row">
            <span>SĐT</span>
            <strong>${escapeHtml(customerPhone)}</strong>
        </div>

        <div class="row">
            <span>Ngày phát hành</span>
            <strong>${dateText(issuedAt)}</strong>
        </div>

        <div class="row">
            <span>Trạng thái</span>
            <strong>${statusText(status)}</strong>
        </div>

        <div class="note">
            Khi dùng phiếu, nhân viên quét mã vạch này tại ô bán hàng.
            Hệ thống sẽ tự nhận dạng theo mã voucher.
        </div>
    </div>

    <script>
      window.addEventListener('load', function () {
    if (!window.JsBarcode) {
        document.getElementById('voucherBarcodeBox').innerHTML =
            '<div style="font-size:12px;color:red;text-align:center;">Không tải được thư viện mã vạch</div>';
        return;
    }

    JsBarcode("#voucherBarcode", "${escapeHtml(voucherCode)}", {
    format: "CODE128",
    width: 1.6,
    height: 45,
    displayValue: true,
    fontSize: 11,
    margin: 0
});

            setTimeout(function () {
                window.print();
            }, 300);
        });
    <\/script>
</body>
</html>
`;

        const win = window.open('', '_blank', 'width=480,height=720');
        if (!win) {
            alert('Trình duyệt đã chặn popup in phiếu.');
            return;
        }

        win.document.open();
        win.document.write(html);
        win.document.close();
    }

    function resetFilters() {
        document.getElementById('txtKeyword').value = '';
        document.getElementById('ddlStatus').value = '';
        document.getElementById('fromDate').value = '';
        document.getElementById('toDate').value = '';
        currentPage = 1;
    }

    function bindEvents() {
        document.getElementById('btnSearch')?.addEventListener('click', async function () {
            currentPage = 1;
            await loadAll();
        });

        document.getElementById('btnReset')?.addEventListener('click', async function () {
            resetFilters();
            await loadAll();
        });

        document.getElementById('txtKeyword')?.addEventListener('keydown', async function (e) {
            if (e.key === 'Enter') {
                currentPage = 1;
                await loadAll();
            }
        });

        document.getElementById('btnPrev')?.addEventListener('click', async function () {
            if (currentPage <= 1) return;
            currentPage--;
            await loadList();
        });

        document.getElementById('btnNext')?.addEventListener('click', async function () {
            if (currentPage >= totalPages) return;
            currentPage++;
            await loadList();
        });

        document.getElementById('voucherTableBody')?.addEventListener('click', async function (e) {
            const btn = e.target.closest('[data-view-voucher-id]');
            if (!btn) return;

            await openDetail(btn.getAttribute('data-view-voucher-id'));
        });

        document.getElementById('btnOpenCancelVoucher')?.addEventListener('click', function () {
            document.getElementById('cancelReason').value = '';
            cancelModal?.show();
        });

        document.getElementById('btnConfirmCancelVoucher')?.addEventListener('click', async function () {
            try {
                await cancelVoucher();
            } catch (err) {
                alert(err.message || 'Không thể hủy phiếu.');
            }
        });

        document.getElementById('btnLockVoucher')?.addEventListener('click', async function () {
            try {
                await lockVoucher();
            } catch (err) {
                alert(err.message || 'Không thể khóa phiếu.');
            }
        });

        document.getElementById('btnUnlockVoucher')?.addEventListener('click', async function () {
            try {
                await unlockVoucher();
            } catch (err) {
                alert(err.message || 'Không thể mở khóa phiếu.');
            }
        });

        document.getElementById('btnViewVoucherLogs')?.addEventListener('click', async function () {
            try {
                await viewVoucherLogs();
            } catch (err) {
                alert(err.message || 'Không thể xem nhật ký voucher.');
            }
        });

        document.getElementById('btnPrintVoucher')?.addEventListener('click', async function () {
            try {
                await printVoucher();
            } catch (err) {
                alert(err.message || 'Không thể in voucher.');
            }
        });
    }

    document.addEventListener('DOMContentLoaded', async function () {
        bindEvents();

        try {
            await loadAll();
        } catch (err) {
            alert(err.message || 'Không tải được danh sách phiếu thưởng.');
        }
    });
})();