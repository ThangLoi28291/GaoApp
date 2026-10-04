
    let dashboardData = null;
    let selectedShiftId = null;

    const shiftPager = { page: 1, pageSize: 20 };
    const diffPager = { page: 1, pageSize: 5 };

    const filterFromDate = document.getElementById('filterFromDate');
    const filterToDate = document.getElementById('filterToDate');
    const filterStatus = document.getElementById('filterStatus');
    const filterUserId = document.getElementById('filterUserId');

    const detailModalEl = document.getElementById('shiftDetailModal');
    const detailModal = detailModalEl ? new bootstrap.Modal(detailModalEl) : null;

    const receiptForm = document.getElementById('cashReceiptForm');
    const receiptAmount = document.getElementById('cashReceivedAmount');
    const receiptNote = document.getElementById('cashReceiptNote');
    const receiptError = document.getElementById('cashReceiptError');
    const receiptButton = document.getElementById('btnConfirmCashReceipt');
    const receiptFilter = document.getElementById('filterCashReceipt');

    function escapeHtml(value) {
        const node = document.createElement('span');
        node.textContent = value == null ? '' : String(value);
        return node.innerHTML;
    }
    function isClosed(item) { return String(item.status).toLowerCase() === 'closed' || Number(item.status) === 2; }
    function receiptBadge(item) {
        const negative = Number(item.closingCashExpected) < 0 ? `<a class="badge bg-label-danger d-block mb-1" href="/admin/pos-shift/requests?tab=cash&shiftId=${Number(item.id)}&status=Approved">Tiền dự kiến âm ${money(item.closingCashExpected)} · Kiểm tra đối soát</a>` : '';
        if (item.needsCashReconciliation) return negative + `<a class="badge bg-label-warning" href="/admin/pos-shift/requests?tab=cash&shiftId=${Number(item.id)}&status=NeedsReconciliation">Cần đối soát lại</a>`;
        if (!isClosed(item)) return negative + '<span class="text-muted">Chưa chốt ca</span>';
        return negative + (item.cashReceivedAtUtc
            ? '<span class="badge bg-label-success">Đã nhận tiền / Đã duyệt</span>'
            : '<span class="badge bg-label-warning">Chờ admin nhận tiền</span>');
    }

    function money(value) {
        return Number(value || 0).toLocaleString('vi-VN', { maximumFractionDigits: 0 }) + ' đ';
    }

    function numberText(value) {
        return Number(value || 0).toLocaleString('vi-VN');
    }

    function dt(value) {
        if (!value) return '-';
        return new Date(value).toLocaleString('vi-VN');
    }

    function dateInput(date) {
        const y = date.getFullYear();
        const m = String(date.getMonth() + 1).padStart(2, '0');
        const d = String(date.getDate()).padStart(2, '0');
        return y + '-' + m + '-' + d;
    }

    function fromDateUtc(value) {
        if (!value) return null;
        return new Date(value + 'T00:00:00').toISOString();
    }

    function toDateUtcExclusive(value) {
        if (!value) return null;
        const d = new Date(value + 'T00:00:00');
        d.setDate(d.getDate() + 1);
        return d.toISOString();
    }

    function userNameText(x) {
        if (!x) return '-';
        return x.fullName || x.userName || ('User #' + x.userId);
    }

    function shiftUserNameText(x) {
        if (!x) return '-';
        return x.openedByUserName || ('User #' + x.openedByUserId);
    }

    function showSuccess(message) {
        if (window.Swal) {
            Swal.fire({
                icon: 'success',
                title: 'Thành công',
                text: message,
                timer: 1400,
                showConfirmButton: false
            });
            return;
        }

        if (window.toastr) {
            toastr.success(message);
            return;
        }

        alert(message);
    }

    function showError(message) {
        if (window.Swal) {
            Swal.fire({
                icon: 'error',
                title: 'Có lỗi',
                text: message || 'Không xử lý được yêu cầu.'
            });
            return;
        }

        if (window.toastr) {
            toastr.error(message || 'Không xử lý được yêu cầu.');
            return;
        }

        alert(message || 'Không xử lý được yêu cầu.');
    }

    async function fetchJson(url) {
        const res = await fetch(url);

        if (!res.ok) {
            let msg = 'Yêu cầu thất bại (' + res.status + ')';

            try {
                const j = await res.json();
                msg = j.message || j.title || j.error || msg;
            } catch {
            }

            throw new Error(msg);
        }

        return await res.json();
    }

    function csvEscape(value) {
        if (value === null || value === undefined) return '';

        const text = value.toString().replace(/"/g, '""');
        return '"' + text + '"';
    }

    function downloadCsv(filename, rows) {
        const csvContent = rows
            .map(function (row) {
                return row.map(csvEscape).join(',');
            })
            .join('\r\n');

        const blob = new Blob(['\ufeff' + csvContent], {
            type: 'text/csv;charset=utf-8;'
        });

        const link = document.createElement('a');
        const url = URL.createObjectURL(blob);

        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);

        URL.revokeObjectURL(url);
    }

    function statusBadge(status) {
        const raw = (status || '').toString().toLowerCase();

        if (raw === '1' || raw.includes('open')) {
            return '<span class="badge-shift badge-open">Đang mở</span>';
        }

        return '<span class="badge-shift badge-closed">Đã đóng</span>';
    }

    function statusText(status) {
        const raw = (status || '').toString().toLowerCase();

        if (raw === '1' || raw.includes('open')) {
            return 'Đang mở';
        }

        return 'Đã đóng';
    }

    function diffPill(diff) {
        diff = Number(diff || 0);

        if (diff === 0) {
            return '<span class="diff-pill diff-zero">Khớp</span>';
        }

        if (diff > 0) {
            return '<span class="diff-pill diff-over">Thừa ' + money(diff) + '</span>';
        }

        return '<span class="diff-pill diff-short">Thiếu ' + money(Math.abs(diff)) + '</span>';
    }

    function paginate(items, page, pageSize) {
        items = items || [];

        const total = items.length;
        const totalPages = Math.max(1, Math.ceil(total / pageSize));
        const currentPage = Math.min(Math.max(1, page), totalPages);
        const start = (currentPage - 1) * pageSize;

        return {
            total: total,
            totalPages: totalPages,
            currentPage: currentPage,
            pageItems: items.slice(start, start + pageSize),
            from: total === 0 ? 0 : start + 1,
            to: Math.min(start + pageSize, total)
        };
    }

    function buildUrl() {
        const p = new URLSearchParams();

        const from = fromDateUtc(filterFromDate.value);
        const to = toDateUtcExclusive(filterToDate.value);

        if (from) p.set('fromUtc', from);
        if (to) p.set('toUtcExclusive', to);
        if (filterStatus.value) p.set('status', filterStatus.value);
        if (filterUserId.value) p.set('userId', filterUserId.value);

        return '/admin/pos/shift/manager-dashboard?' + p.toString();
    }

    function renderUserFilter(items) {
        const current = filterUserId.value;

        const map = new Map();

        (items || []).forEach(function (x) {
            map.set(x.userId, {
                id: x.userId,
                name: userNameText(x)
            });
        });

        const users = Array.from(map.values());

        filterUserId.innerHTML =
            '<option value="">Tất cả nhân viên</option>' +
            users.map(function (x) {
                return '<option value="' + x.id + '">' + x.name + '</option>';
            }).join('');

        filterUserId.value = current;
    }

    function renderKpis(overview) {
        overview = overview || {};

        document.getElementById('kpiTotalSales').textContent = money(overview.totalSales);
        document.getElementById('kpiCashSales').textContent = money(overview.cashSalesTotal);
        document.getElementById('kpiNonCashSales').textContent = money(overview.nonCashSalesTotal);

        const diff = Number(overview.cashDifferenceTotal || 0);
        const diffEl = document.getElementById('kpiCashDiff');

        if (diff === 0) {
            diffEl.textContent = 'Khớp';
        } else if (diff > 0) {
            diffEl.textContent = 'Thừa ' + money(diff);
        } else {
            diffEl.textContent = 'Thiếu ' + money(Math.abs(diff));
        }

        diffEl.className = 'kpi-value ' + (diff < 0 ? 'text-money-bad' : 'text-money-good');

        document.getElementById('kpiCashDiffNote').textContent =
            (overview.differenceShiftCount || 0) + ' ca lệch | ' +
            (overview.shortageShiftCount || 0) + ' thiếu | ' +
            (overview.overShiftCount || 0) + ' thừa';

        document.getElementById('kpiShiftCount').textContent = numberText(overview.totalShifts);
        document.getElementById('kpiShiftCountNote').textContent =
            (overview.openShifts || 0) + ' đang mở / ' +
            (overview.closedShifts || 0) + ' đã đóng';

        document.getElementById('kpiCashIn').textContent = money(overview.cashInTotal);
        document.getElementById('kpiCashOut').textContent = money(overview.cashOutTotal);

        document.getElementById('kpiRefundVoid').textContent = money(overview.refundTotal);
        document.getElementById('kpiRefundVoidNote').textContent =
            (overview.refundCount || 0) + ' refund / ' +
            (overview.voidCount || 0) + ' void';
    }

    function renderEmployees(items) {
        const body = document.getElementById('employeeBody');

        if (!items || !items.length) {
            body.innerHTML =
                '<tr><td colspan="4"><div class="empty-box">Không có dữ liệu nhân viên.</div></td></tr>';
            return;
        }

        body.innerHTML = items.map(function (x) {
            return ''
                + '<tr>'
                + '  <td>'
                + '      <div class="row-main">' + userNameText(x) + '</div>'
                + '      <div class="row-sub">' + (x.closedShifts || 0) + ' ca đóng / ' + (x.openShifts || 0) + ' ca mở</div>'
                + '  </td>'
                + '  <td class="text-end fw-bold">' + (x.totalShifts || 0) + '</td>'
                + '  <td class="text-end fw-bold">' + money(x.totalSales) + '</td>'
                + '  <td class="text-end">' + diffPill(x.cashDifferenceTotal) + '</td>'
                + '</tr>';
        }).join('');
    }

    function renderDifferenceShifts(items) {
        const box = document.getElementById('differenceBody');
        const pagerInfo = document.getElementById('differencePagerInfo');
        const btnPrev = document.getElementById('btnDiffPrev');
        const btnNext = document.getElementById('btnDiffNext');

        const p = paginate(items, diffPager.page, diffPager.pageSize);
        diffPager.page = p.currentPage;

        pagerInfo.textContent = p.total === 0
            ? '0 ca'
            : p.from + '-' + p.to + ' / ' + p.total + ' ca';

        btnPrev.disabled = p.currentPage <= 1;
        btnNext.disabled = p.currentPage >= p.totalPages;

        if (!p.pageItems.length) {
            box.innerHTML = '<div class="empty-box">Không có ca lệch quỹ.</div>';
            return;
        }

        box.innerHTML = p.pageItems.map(function (x) {
            return ''
                + '<div class="d-flex justify-content-between align-items-center gap-2 mb-2 p-3 rounded-3 border">'
                + '  <div>'
                + '      <div class="row-main">' + (x.shiftCode || ('Ca #' + x.id)) + '</div>'
                + '      <div class="row-sub">' + shiftUserNameText(x) + ' - ' + dt(x.openedAtUtc) + '</div>'
                + '  </div>'
                + '  <div class="text-end">'
                +        diffPill(x.cashDifference)
                + '      <div class="mt-2">'
                + '          <button class="btn btn-sm btn-outline-primary table-action-btn" data-detail="' + x.id + '">Chi tiết</button>'
                + '      </div>'
                + '  </div>'
                + '</div>';
        }).join('');
    }

    function renderShifts(items) {
        const all = items || [];
        document.getElementById('cashReceiptPendingCount').textContent =
            all.filter(x => isClosed(x) && !x.cashReceivedAtUtc).length + ' ca chờ nhận';
        items = all.filter(x => !receiptFilter.value || (isClosed(x)
            && (receiptFilter.value === 'received' ? !!x.cashReceivedAtUtc : !x.cashReceivedAtUtc)));
        const body = document.getElementById('shiftBody');
        const pagerInfo = document.getElementById('shiftPagerInfo');
        const btnPrev = document.getElementById('btnShiftPrev');
        const btnNext = document.getElementById('btnShiftNext');

        const p = paginate(items, shiftPager.page, shiftPager.pageSize);
        shiftPager.page = p.currentPage;

        document.getElementById('shiftTotalText').textContent = p.total + ' ca';

        pagerInfo.textContent = p.total === 0
            ? '0 ca'
            : p.from + '-' + p.to + ' / ' + p.total + ' ca';

        btnPrev.disabled = p.currentPage <= 1;
        btnNext.disabled = p.currentPage >= p.totalPages;

        if (!p.pageItems.length) {
            body.innerHTML =
                '<tr><td colspan="8"><div class="empty-box">Không có ca nào theo bộ lọc.</div></td></tr>';
            return;
        }

        body.innerHTML = p.pageItems.map(function (x) {
            return ''
                + '<tr>'
                + '  <td>'
                + '      <div class="row-main">' + (x.shiftCode || ('Ca #' + x.id)) + '</div>'
                + '      <div class="row-sub">' + dt(x.openedAtUtc) + '</div>'
                + '  </td>'
                + '  <td>'
                + '      <div class="row-main">' + shiftUserNameText(x) + '</div>'
                + '      <div class="row-sub">' + (x.terminalCode || '') + ' ' + (x.terminalName || '') + '</div>'
                + '  </td>'
                + '  <td>' + statusBadge(x.status) + '</td>'
                + '  <td class="text-end fw-bold">' + money(x.totalSales) + '</td>'
                + '  <td class="text-end">' + money(x.cashSalesTotal) + '</td>'
                + '  <td>' + receiptBadge(x) + '</td>'
                + '  <td class="text-end">' + diffPill(x.cashDifference) + '</td>'
                + '  <td class="text-end">'
                + '      <button class="btn btn-outline-primary btn-sm table-action-btn" data-detail="' + x.id + '">' + (isClosed(x) && !x.cashReceivedAtUtc ? 'Nhận tiền / Duyệt' : 'Chi tiết') + '</button>'
                + '  </td>'
                + '</tr>';
        }).join('');
    }

    function renderAll(data) {
        dashboardData = data || {};

        renderUserFilter(dashboardData.employeeStats || []);
        renderKpis(dashboardData.overview || {});
        renderEmployees(dashboardData.employeeStats || []);
        renderDifferenceShifts(dashboardData.differenceShifts || []);
        renderShifts(dashboardData.shifts || []);
    }

    async function loadDashboard(showOk) {
        try {
            shiftPager.page = 1;
            diffPager.page = 1;

            const data = await fetchJson(buildUrl());
            renderAll(data);

            if (showOk) showSuccess('Đã tải báo cáo.');
        } catch (err) {
            showError(err.message);
        }
    }

    function openDetail(id) {
        const items = dashboardData && dashboardData.shifts ? dashboardData.shifts : [];
        const item = items.find(function (x) { return x.id === id; });

        if (!item) {
            showError('Không tìm thấy ca cần xem.');
            return;
        }

        selectedShiftId = id;

        document.getElementById('detailTitle').textContent = item.shiftCode || ('Ca #' + item.id);
        document.getElementById('detailSub').textContent = shiftUserNameText(item) + ' - ' + dt(item.openedAtUtc);

        const html = ''
            + '<div class="manager-popup-grid">'
            + popupItem('Trạng thái', statusBadge(item.status))
            + popupItem('Nhân viên mở ca', escapeHtml(shiftUserNameText(item)))
            + popupItem('Mở lúc', dt(item.openedAtUtc))
            + popupItem('Đóng lúc', dt(item.closedAtUtc))
            + popupItem('Tiền đầu ca', money(item.openingCash))
            + popupItem('Doanh thu', money(item.totalSales))
            + popupItem('Tiền mặt', money(item.cashSalesTotal))
            + popupItem('Không tiền mặt', money(item.nonCashSalesTotal))
            + popupItem('Thu thêm', money(item.cashInTotal))
            + popupItem('Chi ra', money(item.cashOutTotal))
            + popupItem('Refund', money(item.refundTotal) + ' / ' + (item.refundCount || 0) + ' lần')
            + popupItem('Void', item.voidCount || 0)
            + popupItem('Tiền dự kiến', money(item.closingCashExpected))
            + popupItem('Nhân viên thực đếm', item.closingCashActual == null ? '-' : money(item.closingCashActual))
            + popupItem('Bàn giao tiền', receiptBadge(item))
            + popupItem('Admin thực nhận', item.cashReceivedAmount == null ? '-' : money(item.cashReceivedAmount))
            + popupItem('Lệch so với nhân viên', item.cashReceivedAmount == null ? '-' : diffPill(item.cashReceivedAmount - item.closingCashActual))
            + popupItem('Admin xác nhận', escapeHtml(item.cashReceivedByUserName || '-'))
            + popupItem('Xác nhận lúc', dt(item.cashReceivedAtUtc))
            + popupItem('Ghi chú bàn giao', escapeHtml(item.cashReceiptNote || '-'))
            + '<div class="manager-popup-item" style="grid-column:1/-1;">'
            + '    <div class="manager-popup-label">Lệch quỹ</div>'
            + '    <div class="manager-popup-value">' + diffPill(item.cashDifference) + '</div>'
            + '</div>'
            + '</div>';

        document.getElementById('detailContent').innerHTML = html;

        receiptForm.hidden = !isClosed(item) || item.closingCashActual == null || !!item.cashReceivedAtUtc;
        document.getElementById('cashReceiptSummary').textContent = 'Nhân viên khai: ' + money(item.closingCashActual) + ' · Sổ quỹ dự kiến: ' + money(item.closingCashExpected);
        receiptAmount.value = '';
        receiptNote.value = '';
        receiptError.textContent = '';
        document.getElementById('cashReceiptDifference').textContent = '';
        receiptButton.disabled = false;
        document.getElementById('btnPrintClosingSlip').hidden = !isClosed(item);
        if (detailModal) detailModal.show();
    }

    function popupItem(label, value) {
        return ''
            + '<div class="manager-popup-item">'
            + '  <div class="manager-popup-label">' + label + '</div>'
            + '  <div class="manager-popup-value">' + value + '</div>'
            + '</div>';
    }

function exportManagerDashboardExcel() {
    const p = new URLSearchParams();

    const from = fromDateUtc(filterFromDate.value);
    const to = toDateUtcExclusive(filterToDate.value);

    if (from) p.set('fromUtc', from);
    if (to) p.set('toUtcExclusive', to);
    if (filterStatus.value) p.set('status', filterStatus.value);
    if (filterUserId.value) p.set('userId', filterUserId.value);

    const url = '/admin/pos/shift/manager-dashboard/export-excel?' + p.toString();

    window.open(url, '_blank');
}

    function printManagerDashboardReport() {
        if (!dashboardData || !dashboardData.overview) {
            showError('Chưa có dữ liệu để in báo cáo.');
            return;
        }

        const o = dashboardData.overview;

        const shiftRows = (dashboardData.shifts || []).map(function (x) {
            return ''
                + '<tr>'
                + '<td>' + (x.shiftCode || ('Ca #' + x.id)) + '</td>'
                + '<td>' + shiftUserNameText(x) + '</td>'
                + '<td>' + statusText(x.status) + '</td>'
                + '<td>' + dt(x.openedAtUtc) + '</td>'
                + '<td>' + dt(x.closedAtUtc) + '</td>'
                + '<td class="right">' + money(x.totalSales) + '</td>'
                + '<td class="right">' + money(x.cashSalesTotal) + '</td>'
                + '<td class="right">' + money(x.refundTotal) + '</td>'
                + '<td class="right">' + money(x.cashDifference) + '</td>'
                + '</tr>';
        }).join('');

        const html = ''
            + '<!DOCTYPE html>'
            + '<html lang="vi">'
            + '<head>'
            + '<meta charset="utf-8" />'
            + '<title>Báo cáo ca POS</title>'
            + '<style>'
            + 'body{font-family:Arial,sans-serif;padding:24px;color:#111827;}'
            + 'h1{text-align:center;margin-bottom:4px;font-size:22px;}'
            + '.sub{text-align:center;color:#667085;margin-bottom:20px;}'
            + '.summary{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:18px;}'
            + '.box{border:1px solid #ddd;border-radius:10px;padding:10px;}'
            + '.label{color:#667085;font-size:12px;font-weight:bold;}'
            + '.value{font-size:18px;font-weight:900;margin-top:6px;}'
            + 'table{width:100%;border-collapse:collapse;margin-top:12px;}'
            + 'th,td{border:1px solid #ddd;padding:7px;font-size:12px;}'
            + 'th{background:#f3f4f6;}'
            + '.right{text-align:right;}'
            + '</style>'
            + '</head>'
            + '<body>'
            + '<h1>BÁO CÁO QUẢN LÝ CA POS</h1>'
            + '<div class="sub">Từ ' + (filterFromDate.value || '...') + ' đến ' + (filterToDate.value || '...') + '</div>'
            + '<div class="summary">'
            + '<div class="box"><div class="label">Tổng doanh thu</div><div class="value">' + money(o.totalSales) + '</div></div>'
            + '<div class="box"><div class="label">Tiền mặt</div><div class="value">' + money(o.cashSalesTotal) + '</div></div>'
            + '<div class="box"><div class="label">Không tiền mặt</div><div class="value">' + money(o.nonCashSalesTotal) + '</div></div>'
            + '<div class="box"><div class="label">Lệch quỹ</div><div class="value">' + money(o.cashDifferenceTotal) + '</div></div>'
            + '<div class="box"><div class="label">Tổng ca</div><div class="value">' + (o.totalShifts || 0) + '</div></div>'
            + '<div class="box"><div class="label">Ca đang mở</div><div class="value">' + (o.openShifts || 0) + '</div></div>'
            + '<div class="box"><div class="label">Ca đã đóng</div><div class="value">' + (o.closedShifts || 0) + '</div></div>'
            + '<div class="box"><div class="label">Refund / Void</div><div class="value">' + (o.refundCount || 0) + ' / ' + (o.voidCount || 0) + '</div></div>'
            + '</div>'
            + '<h3>Danh sách ca</h3>'
            + '<table>'
            + '<thead>'
            + '<tr>'
            + '<th>Ca</th><th>Nhân viên</th><th>Trạng thái</th><th>Mở lúc</th><th>Đóng lúc</th>'
            + '<th class="right">Doanh thu</th><th class="right">Tiền mặt</th><th class="right">Refund</th><th class="right">Lệch quỹ</th>'
            + '</tr>'
            + '</thead>'
            + '<tbody>' + shiftRows + '</tbody>'
            + '</table>'
            + '</body>'
            + '</html>';

        const w = window.open('', '_blank');

        if (!w) {
            showError('Trình duyệt đang chặn popup. Vui lòng cho phép popup để in báo cáo.');
            return;
        }

        w.document.open();
        w.document.write(html);
        w.document.close();
        GaoPrintLifecycle.autoClose(w);

        setTimeout(function () {
            w.print();
        }, 500);
    }

    function updateReceiptDifference() {
        const item = (dashboardData?.shifts || []).find(x => x.id === selectedShiftId);
        const valid = item && receiptAmount.value !== '' && Number.isFinite(Number(receiptAmount.value));
        const amount = Number(receiptAmount.value);
        receiptNote.required = !!valid && (amount !== Number(item.closingCashActual) || amount !== Number(item.closingCashExpected));
        document.getElementById('cashReceiptDifference').textContent = valid
            ? 'Lệch so với nhân viên: ' + money(amount - item.closingCashActual)
                + ' · Lệch so với sổ quỹ: ' + money(amount - item.closingCashExpected)
                + (receiptNote.required ? '. Cần ghi lý do chênh lệch.' : '') : '';
    }

    async function confirmCashReceipt(event) {
        event.preventDefault();
        updateReceiptDifference();
        if (!receiptForm.reportValidity() || receiptButton.disabled) return;
        const id = selectedShiftId;
        receiptButton.disabled = true;
        receiptError.textContent = '';
        try {
            const response = await fetch('/admin/pos/shift/' + id + '/cash-receipt', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ receivedAmount: Number(receiptAmount.value), note: receiptNote.value.trim() || null })
            });
            const result = await response.json();
            if (!response.ok) throw new Error(result.message || 'Không xác nhận được tiền bàn giao.');
            // Lock the local form immediately; a failed refresh must not invite a second edit.
            const item = (dashboardData?.shifts || []).find(x => x.id === id);
            if (item) item.cashReceivedAtUtc = new Date().toISOString();
            receiptForm.hidden = true;
            renderShifts(dashboardData.shifts);
            const data = await fetchJson(buildUrl());
            renderAll(data);
            if (selectedShiftId === id) openDetail(id);
            showSuccess('Đã xác nhận nhận tiền và duyệt chốt ca.');
        } catch (err) { receiptError.textContent = err.message; }
        finally { receiptButton.disabled = false; }
    }

    function bindEvents() {
        receiptForm.addEventListener('submit', confirmCashReceipt);
        receiptAmount.addEventListener('input', updateReceiptDifference);
        receiptFilter.addEventListener('change', function () {
            shiftPager.page = 1;
            renderShifts(dashboardData?.shifts || []);
        });
        document.addEventListener('click', function (e) {
            const detailBtn = e.target.closest('[data-detail]');

            if (detailBtn) {
                openDetail(Number(detailBtn.dataset.detail));
            }
        });

        document.getElementById('btnApplyFilter').addEventListener('click', function () {
            loadDashboard(true);
        });

        document.getElementById('btnReloadDashboard').addEventListener('click', function () {
            loadDashboard(true);
        });

        document.getElementById('btnExportExcel').addEventListener('click', exportManagerDashboardExcel);
        document.getElementById('btnPrintReport').addEventListener('click', printManagerDashboardReport);

        document.getElementById('btnToday').addEventListener('click', function () {
            const now = new Date();
            filterFromDate.value = dateInput(now);
            filterToDate.value = dateInput(now);
            loadDashboard(true);
        });

        document.getElementById('btnLast7Days').addEventListener('click', function () {
            const now = new Date();
            const from = new Date();
            from.setDate(now.getDate() - 6);

            filterFromDate.value = dateInput(from);
            filterToDate.value = dateInput(now);
            loadDashboard(true);
        });

        document.getElementById('btnLast30Days').addEventListener('click', function () {
            const now = new Date();
            const from = new Date();
            from.setDate(now.getDate() - 29);

            filterFromDate.value = dateInput(from);
            filterToDate.value = dateInput(now);
            loadDashboard(true);
        });

        document.getElementById('btnClearFilter').addEventListener('click', function () {
            filterFromDate.value = '';
            filterToDate.value = '';
            filterStatus.value = '';
            filterUserId.value = '';
            loadDashboard(true);
        });

        document.getElementById('btnPrintClosingSlip').addEventListener('click', function () {
            if (!selectedShiftId) {
                showError('Chưa chọn ca.');
                return;
            }

            window.open('/admin/pos-shift/closing-slip-print?shiftId=' + selectedShiftId, '_blank');
        });

        document.getElementById('btnShiftPrev').addEventListener('click', function () {
            if (shiftPager.page <= 1) return;

            shiftPager.page--;
            renderShifts(dashboardData && dashboardData.shifts ? dashboardData.shifts : []);
        });

        document.getElementById('btnShiftNext').addEventListener('click', function () {
            shiftPager.page++;
            renderShifts(dashboardData && dashboardData.shifts ? dashboardData.shifts : []);
        });

        document.getElementById('shiftPageSize').addEventListener('change', function () {
            shiftPager.pageSize = Number(this.value || 20);
            shiftPager.page = 1;
            renderShifts(dashboardData && dashboardData.shifts ? dashboardData.shifts : []);
        });

        document.getElementById('btnDiffPrev').addEventListener('click', function () {
            if (diffPager.page <= 1) return;

            diffPager.page--;
            renderDifferenceShifts(dashboardData && dashboardData.differenceShifts ? dashboardData.differenceShifts : []);
        });

        document.getElementById('btnDiffNext').addEventListener('click', function () {
            diffPager.page++;
            renderDifferenceShifts(dashboardData && dashboardData.differenceShifts ? dashboardData.differenceShifts : []);
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        const now = new Date();

        filterFromDate.value = dateInput(now);
        filterToDate.value = dateInput(now);

        const pageSizeSelect = document.getElementById('shiftPageSize');
        if (pageSizeSelect) {
            shiftPager.pageSize = Number(pageSizeSelect.value || 20);
        }

        bindEvents();
        loadDashboard(false);
    });
