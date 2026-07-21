let barcodeResolveModalInstance = null;
let barcodeRequests = [];

document.addEventListener('DOMContentLoaded', function () {
    const modalEl = document.getElementById('barcodeResolveModal');
    if (modalEl) {
        barcodeResolveModalInstance = new bootstrap.Modal(modalEl);
    }

    bindBarcodeNormalizationEvents();
    loadBarcodeRequests();
});

function bindBarcodeNormalizationEvents() {
    document.getElementById('btnReloadBarcodeRequests')?.addEventListener('click', loadBarcodeRequests);
    document.getElementById('btnSearchBarcodeRequests')?.addEventListener('click', loadBarcodeRequests);

    document.getElementById('barcodeRequestKeyword')?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            loadBarcodeRequests();
        }
    });

    document.getElementById('barcodeRequestStatus')?.addEventListener('change', loadBarcodeRequests);

    document.getElementById('btnConfirmBarcodeResolve')?.addEventListener('click', confirmBarcodeResolve);
}

async function readApiResponse(response) {
    const text = await response.text();

    try {
        return {
            ok: response.ok,
            data: JSON.parse(text)
        };
    } catch {
        return {
            ok: response.ok,
            data: { message: text }
        };
    }
}

async function loadBarcodeRequests() {
    const body = document.getElementById('barcodeRequestTableBody');
    if (!body) return;

    body.innerHTML = `
        <tr>
            <td colspan="9" class="text-center text-muted py-4">
                Đang tải dữ liệu...
            </td>
        </tr>
    `;

    const status = document.getElementById('barcodeRequestStatus')?.value || '';
    const keyword = document.getElementById('barcodeRequestKeyword')?.value || '';

    const url = `/admin/barcode-normalization/list?status=${encodeURIComponent(status)}&keyword=${encodeURIComponent(keyword)}`;

    try {
        const response = await fetch(url, {
            method: 'GET',
            cache: 'no-store'
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            body.innerHTML = renderMessageRow(api.data?.message || 'Không tải được dữ liệu.', true);
            return;
        }

        barcodeRequests = api.data || [];
        renderBarcodeRequests();
    } catch (error) {
        console.error(error);
        body.innerHTML = renderMessageRow('Có lỗi khi tải dữ liệu.', true);
    }
}

function renderBarcodeRequests() {
    const body = document.getElementById('barcodeRequestTableBody');
    if (!body) return;

    if (!barcodeRequests.length) {
        body.innerHTML = renderMessageRow('Không có yêu cầu nào.', false);
        return;
    }

    body.innerHTML = barcodeRequests.map(x => `
        <tr>
            <td>#${x.id}</td>

            <td>
                <div class="fw-bold">${escapeHtml(x.productNameSnapshot || '')}</div>
                <div class="text-muted small">
                    Phiếu: ${escapeHtml(x.stockDocumentNo || '-')}
                    ${x.warehouseName ? ' · Kho: ' + escapeHtml(x.warehouseName) : ''}
                </div>
            </td>

            <td class="fw-bold">${escapeHtml(x.unitNameSnapshot || '')}</td>

            <td>x${escapeHtml(formatDecimal(x.factorSnapshot || 1))}</td>

            <td>
                ${x.suggestedBarcode
            ? `<span class="fw-bold text-primary">${escapeHtml(x.suggestedBarcode)}</span>`
            : `<span class="text-muted">Không có mã</span>`}
            </td>

            <td>${escapeHtml(x.requestTypeText || '')}</td>

            <td>${renderStatusBadge(x.status, x.statusText)}</td>

            <td>${formatDateTime(x.requestedAtUtc)}</td>

            <td class="text-end">
                ${renderActionButtons(x)}
            </td>
        </tr>
    `).join('');

    bindActionButtons();
}

function renderActionButtons(x) {
    if (Number(x.status) !== 1) {
        return `<span class="text-muted small">Đã xử lý</span>`;
    }

    if (Number(x.requestType) === 1) {
        return `
            <button type="button"
                    class="btn btn-sm btn-success js-approve-barcode"
                    data-id="${x.id}">
                Duyệt
            </button>

            <button type="button"
                    class="btn btn-sm btn-outline-danger js-reject-barcode"
                    data-id="${x.id}">
                Từ chối
            </button>
        `;
    }

    if (Number(x.requestType) === 2) {
        return `
            <button type="button"
                    class="btn btn-sm btn-warning js-confirm-no-barcode"
                    data-id="${x.id}">
                Xác nhận
            </button>

            <button type="button"
                    class="btn btn-sm btn-outline-danger js-reject-barcode"
                    data-id="${x.id}">
                Từ chối
            </button>
        `;
    }

    return '';
}

function bindActionButtons() {
    document.querySelectorAll('.js-approve-barcode').forEach(btn => {
        btn.addEventListener('click', function () {
            openResolveModal(Number(this.dataset.id), 'approve');
        });
    });

    document.querySelectorAll('.js-reject-barcode').forEach(btn => {
        btn.addEventListener('click', function () {
            openResolveModal(Number(this.dataset.id), 'reject');
        });
    });

    document.querySelectorAll('.js-confirm-no-barcode').forEach(btn => {
        btn.addEventListener('click', function () {
            openResolveModal(Number(this.dataset.id), 'confirm-no-barcode');
        });
    });
}

function openResolveModal(id, action) {
    const item = barcodeRequests.find(x => Number(x.id) === Number(id));
    if (!item) return;

    setValue('barcodeResolveId', id);
    setValue('barcodeResolveAction', action);
    setValue('barcodeResolveManagerNote', '');
    setText('barcodeResolveMessage', '');

    setText('barcodeResolveProductName', item.productNameSnapshot || '-');
    setText('barcodeResolveUnitName', `${item.unitNameSnapshot || '-'} · x${formatDecimal(item.factorSnapshot || 1)}`);
    setText('barcodeResolveSuggestedBarcode', item.suggestedBarcode || 'Không có mã');

    let title = 'Xử lý barcode';
    let btnText = 'Xác nhận';
    let btnClass = 'btn btn-primary';

    if (action === 'approve') {
        title = 'Duyệt barcode mới?';
        btnText = 'Duyệt barcode';
        btnClass = 'btn btn-success';
    } else if (action === 'reject') {
        title = 'Từ chối yêu cầu?';
        btnText = 'Từ chối';
        btnClass = 'btn btn-danger';
    } else if (action === 'confirm-no-barcode') {
        title = 'Xác nhận không có mã NSX?';
        btnText = 'Xác nhận';
        btnClass = 'btn btn-warning';
    }

    setText('barcodeResolveTitle', title);

    const btn = document.getElementById('btnConfirmBarcodeResolve');
    if (btn) {
        btn.textContent = btnText;
        btn.className = btnClass;
    }

    barcodeResolveModalInstance?.show();
}

async function confirmBarcodeResolve() {
    const id = Number(document.getElementById('barcodeResolveId')?.value || 0);
    const action = document.getElementById('barcodeResolveAction')?.value || '';
    const note = document.getElementById('barcodeResolveManagerNote')?.value || '';
    const msg = document.getElementById('barcodeResolveMessage');
    const btn = document.getElementById('btnConfirmBarcodeResolve');

    if (!id || !action) return;

    let url = '';

    if (action === 'approve') {
        url = `/admin/barcode-normalization/${id}/approve`;
    } else if (action === 'reject') {
        url = `/admin/barcode-normalization/${id}/reject`;
    } else if (action === 'confirm-no-barcode') {
        url = `/admin/barcode-normalization/${id}/confirm-no-barcode`;
    }

    if (!url) return;

    try {
        btn.disabled = true;
        btn.textContent = 'Đang xử lý...';

        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                managerNote: note
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Xử lý thất bại.';
            return;
        }

        barcodeResolveModalInstance?.hide();
        await loadBarcodeRequests();
    } catch (error) {
        console.error(error);
        if (msg) msg.textContent = 'Có lỗi khi xử lý.';
    } finally {
        btn.disabled = false;
        btn.textContent = 'Xác nhận';
    }
}

function renderStatusBadge(status, text) {
    switch (Number(status)) {
        case 1:
            return `<span class="badge bg-warning text-dark">${escapeHtml(text || 'Chờ xử lý')}</span>`;
        case 2:
            return `<span class="badge bg-success">${escapeHtml(text || 'Đã duyệt')}</span>`;
        case 3:
            return `<span class="badge bg-danger">${escapeHtml(text || 'Từ chối')}</span>`;
        case 4:
            return `<span class="badge bg-info text-dark">${escapeHtml(text || 'Không có mã')}</span>`;
        default:
            return `<span class="badge bg-secondary">${escapeHtml(text || 'Không rõ')}</span>`;
    }
}
function renderMessageRow(message, isError) {
    return '<tr>' +
        '<td colspan="9" class="text-center ' + (isError ? 'text-danger' : 'text-muted') + ' py-4">' +
        escapeHtml(message || '') +
        '</td>' +
        '</tr>';
}

function formatDateTime(value) {
    if (!value) return '-';

    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return '-';

    return d.toLocaleString('vi-VN');
}

function formatDecimal(value) {
    return Number(value || 0).toFixed(3).replace(/\.?0+$/, '');
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function setText(id, value) {
    const el = document.getElementById(id);
    if (el) el.textContent = value ?? '';
}

function setValue(id, value) {
    const el = document.getElementById(id);
    if (el) el.value = value ?? '';
}