let wrCreateModalInstance = null;
let wrAllReceipts = [];
let wrPage = 1;
let wrPageSize = 10;
let wrFilteredReceipts = [];
let wrReceiptFormOptions = null;
let wrStatusFilter = 'all';
document.addEventListener('DOMContentLoaded', function () {
    const modalEl = document.getElementById('wrCreateModal');
    if (modalEl) {
        wrCreateModalInstance = new bootstrap.Modal(modalEl);
    }

    bindWarehouseReceivingEvents();
    loadWarehouseReceivingReceipts();
});

function bindWarehouseReceivingEvents() {
    bindWarehouseReceivingKpiFilters();

    document.getElementById('btnOpenCreateReceiving')?.addEventListener('click', async function () {
        resetCreateReceivingModal();
        await loadWarehouseOptions();
        wrCreateModalInstance?.show();

        setTimeout(focusCreateReceivingTitle, 250);
        setTimeout(focusCreateReceivingTitle, 450);
    });

    document.getElementById('btnCreateReceiving')?.addEventListener('click', createReceivingReceipt);
    document.getElementById('wrBtnReload')?.addEventListener('click', loadWarehouseReceivingReceipts);

    document.getElementById('wrSearch')?.addEventListener('input', function () {
        wrPage = 1;
        renderWarehouseReceivingReceipts();
    });

    document.getElementById('wrCreateModal')?.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter' || e.shiftKey) return;

        const tag = document.activeElement?.tagName?.toLowerCase();
        if (tag === 'textarea') return;

        e.preventDefault();
        document.getElementById('btnCreateReceiving')?.click();
    });
    document.getElementById('wrBtnPrevPage')?.addEventListener('click', function () {
        if (wrPage <= 1) return;

        wrPage--;
        renderWarehouseReceivingReceipts();
    });

    document.getElementById('wrBtnNextPage')?.addEventListener('click', function () {
        const totalPages = getWrTotalPages();

        if (wrPage >= totalPages) return;

        wrPage++;
        renderWarehouseReceivingReceipts();
    });
}

function bindWarehouseReceivingKpiFilters() {
    document.querySelectorAll('[data-warehouse-receiving-index] .wr-kpi-filter[data-status-filter]')
        .forEach(button => {
            if (button.dataset.bound === '1') return;
            button.dataset.bound = '1';

            button.addEventListener('click', function () {
                const requestedStatus = this.dataset.statusFilter || 'all';
                wrStatusFilter = wrStatusFilter === requestedStatus ? 'all' : requestedStatus;
                wrPage = 1;
                syncWarehouseReceivingKpiFilters();
                renderWarehouseReceivingReceipts();
            });
        });

    syncWarehouseReceivingKpiFilters();
}

function syncWarehouseReceivingKpiFilters() {
    document.querySelectorAll('[data-warehouse-receiving-index] .wr-kpi-filter[data-status-filter]')
        .forEach(button => {
            button.setAttribute('aria-pressed', String(button.dataset.statusFilter === wrStatusFilter));
        });
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

async function loadWarehouseReceivingReceipts() {
    const list = document.getElementById('wrReceiptList');
    if (list) {
        list.innerHTML = `<div class="wr-empty">Đang tải danh sách phiếu...</div>`;
    }

    try {
        const response = await fetch('/admin/warehouse-receiving/receipts', {
            method: 'GET',
            cache: 'no-store'
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            list.innerHTML = `<div class="wr-empty text-danger">${escapeHtml(api.data?.message || 'Không tải được danh sách.')}</div>`;
            return;
        }

        wrAllReceipts = api.data || [];
        wrPage = 1;
        renderWarehouseReceivingReceipts();
    } catch (error) {
        console.error(error);
        list.innerHTML = `<div class="wr-empty text-danger">Có lỗi khi tải danh sách.</div>`;
    }
}

function renderWarehouseReceivingReceipts() {
    const list = document.getElementById('wrReceiptList');
    if (!list) return;

    const keyword = (document.getElementById('wrSearch')?.value || '').trim().toLowerCase();

    wrFilteredReceipts = wrAllReceipts.filter(x => {
        const matchesKeyword = !keyword || [
            x.documentNo,
            x.documentTitle,
            statusText(x.status)
        ].some(v => String(v || '').toLowerCase().includes(keyword));

        const matchesStatus = wrStatusFilter === 'all'
            || String(Number(x.status)) === wrStatusFilter;

        return matchesKeyword && matchesStatus;
    });

    updateStats();

    const totalItems = wrFilteredReceipts.length;
    const totalPages = getWrTotalPages();

    if (wrPage > totalPages) {
        wrPage = totalPages;
    }

    const startIndex = (wrPage - 1) * wrPageSize;
    const pageItems = wrFilteredReceipts.slice(startIndex, startIndex + wrPageSize);

    updateWrPagination(totalItems, startIndex, pageItems.length, totalPages);

    if (pageItems.length === 0) {
        list.innerHTML = `<div class="wr-empty">Không có phiếu nào cần xử lý.</div>`;
        return;
    }

    list.innerHTML = pageItems.map(x => {
        const status = Number(x.status);
        const actionText = status === 2
            ? 'Xem phiếu'
            : (status === 4 ? 'Sửa phiếu' : 'Tiếp tục nhập');
        const actionClass = status === 2
            ? 'is-view'
            : (status === 4 ? 'is-edit' : 'btn-primary');

        return `
            <div class="wr-card wr-status-${status}">
                <div class="wr-card-icon">
                    <i class="bx bx-file"></i>
                </div>

                <div class="wr-card-main">
                    <div class="wr-title">${escapeHtml(x.documentTitle || 'Chưa đặt tên phiếu')}</div>
                    <div class="wr-code">${escapeHtml(x.documentNo || '-')}</div>
                    <div class="wr-date">Ngày tạo: ${formatDateTime(x.createdAtUtc || x.documentDate)}</div>
                </div>

                <div class="wr-card-meta">
                    ${renderStatusBadge(x.status)}
                    <div class="wr-card-facts">
                        <div>Kho: <strong>${escapeHtml(x.warehouseName || '-')}</strong></div>
                        <div>HKD: <strong>${escapeHtml(x.legalEntityName || '-')}</strong></div>
                        <div><strong>${formatNumber(x.totalLines || 0)}</strong> dòng hàng · <strong>${formatNumber(x.totalProductTypes || 0)}</strong> loại sản phẩm</div>
                    </div>
                </div>

                <div class="wr-card-actions d-flex flex-wrap gap-2 align-items-center">
                ${window.GaoReceiptDocumentActions?.buttons(x) || ''}
                <a class="wr-card-action ${actionClass}"
                   href="/admin/warehouse-receiving/${x.id}">
                    ${actionText}
                </a>
                </div>
            </div>
        `;
    }).join('');
}

function updateStats() {
    const all = wrAllReceipts || [];

    setText('wrDraftCount', formatNumber(all.filter(x => Number(x.status) === 1).length));
    setText('wrPendingCount', formatNumber(all.filter(x => Number(x.status) === 2).length));
    setText('wrRejectedCount', formatNumber(all.filter(x => Number(x.status) === 4).length));
}

async function loadWarehouseOptions() {
    const legalEntitySelect = document.getElementById('wrLegalEntityId');
    const select = document.getElementById('wrWarehouseId');
    if (!legalEntitySelect || !select) return;

    legalEntitySelect.innerHTML = `<option value="">Đang tải HKD...</option>`;
    select.innerHTML = `<option value="">Đang tải kho...</option>`;

    try {
        const response = await fetch('/admin/api/stock-documents/receipt-form-options', {
            method: 'GET',
            cache: 'no-store'
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            legalEntitySelect.innerHTML = `<option value="">Không tải được HKD</option>`;
            select.innerHTML = `<option value="">Không tải được kho</option>`;
            return;
        }

        wrReceiptFormOptions = api.data || { legalEntities: [], warehouses: [] };
        const legalEntities = wrReceiptFormOptions.legalEntities || [];

        legalEntitySelect.innerHTML = `<option value="">-- Chọn HKD --</option>`;
        legalEntities.forEach(x => {
            const option = document.createElement('option');
            option.value = x.id;
            option.textContent = `${x.code} - ${x.name}`;
            legalEntitySelect.appendChild(option);
        });

        const defaultLegalEntityId = Number(wrReceiptFormOptions.defaultLegalEntityId || 0);
        if (defaultLegalEntityId) {
            legalEntitySelect.value = String(defaultLegalEntityId);
        } else if (legalEntities.length === 1) {
            legalEntitySelect.value = String(legalEntities[0].id);
        }

        renderWarehouseOptionsForLegalEntity();

        if (!legalEntitySelect.dataset.receiptChangeBound) {
            legalEntitySelect.dataset.receiptChangeBound = '1';
            legalEntitySelect.addEventListener('change', renderWarehouseOptionsForLegalEntity);
        }
    } catch (error) {
        console.error(error);
        legalEntitySelect.innerHTML = `<option value="">Không tải được HKD</option>`;
        select.innerHTML = `<option value="">Không tải được kho</option>`;
    }
}

function renderWarehouseOptionsForLegalEntity() {
    const legalEntitySelect = document.getElementById('wrLegalEntityId');
    const warehouseSelect = document.getElementById('wrWarehouseId');
    if (!legalEntitySelect || !warehouseSelect) return;

    const legalEntityId = Number(legalEntitySelect.value || 0);
    const legalEntities = wrReceiptFormOptions?.legalEntities || [];
    const warehouses = (wrReceiptFormOptions?.warehouses || [])
        .filter(x => Number(x.legalEntityId) === legalEntityId);
    const legalEntity = legalEntities.find(x => Number(x.id) === legalEntityId);

    warehouseSelect.innerHTML = `<option value="">-- Chọn kho --</option>`;
    warehouses.forEach(x => {
        const option = document.createElement('option');
        option.value = x.id;
        option.textContent = `${x.code} - ${x.name}`;
        warehouseSelect.appendChild(option);
    });

    const defaultWarehouseId = Number(legalEntity?.defaultWarehouseId || 0);
    if (defaultWarehouseId && warehouses.some(x => Number(x.id) === defaultWarehouseId)) {
        warehouseSelect.value = String(defaultWarehouseId);
    } else if (warehouses.length === 1) {
        warehouseSelect.value = String(warehouses[0].id);
    }
}

async function createReceivingReceipt() {
    const btn = document.getElementById('btnCreateReceiving');
    const msg = document.getElementById('wrCreateMessage');

    const legalEntityId = Number(document.getElementById('wrLegalEntityId')?.value || 0);
    const warehouseId = Number(document.getElementById('wrWarehouseId')?.value || 0);
    const documentTitle = document.getElementById('wrDocumentTitle')?.value || '';
    const directReceiptReason = document.getElementById('wrDirectReceiptSource')?.value || '';
    const note = document.getElementById('wrNote')?.value || '';

    if (msg) msg.textContent = '';

    if (!legalEntityId) {
        if (msg) msg.textContent = 'Vui lòng chọn HKD nhập hàng.';
        return;
    }

    if (!warehouseId) {
        if (msg) msg.textContent = 'Vui lòng chọn kho.';
        return;
    }

    if (!directReceiptReason) {
        if (msg) msg.textContent = 'Vui lòng chọn nguồn nhập.';
        document.getElementById('wrDirectReceiptSource')?.focus();
        return;
    }

    try {
        btn.disabled = true;
        btn.textContent = 'Đang tạo...';

        const response = await fetch('/admin/warehouse-receiving/receipts', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                legalEntityId: legalEntityId,
                warehouseId: warehouseId,
                supplierId: null,
                documentTitle: documentTitle,
                directReceiptReason: directReceiptReason,
                note: note,
                documentDate: new Date().toISOString()
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Tạo phiếu thất bại.';
            return;
        }

        window.location.href = api.data?.redirectUrl || `/admin/warehouse-receiving/${api.data?.id}`;
    } catch (error) {
        console.error(error);

        if (msg) {
            msg.textContent = 'Có lỗi khi tạo phiếu.';
        }
    } finally {
        btn.disabled = false;
        btn.textContent = 'Tạo phiếu';
    }
}

function resetCreateReceivingModal() {
    setValue('wrDocumentTitle', '');
    setValue('wrDirectReceiptSource', 'Nhà phân phối giao');
    setValue('wrNote', '');
    setText('wrCreateMessage', '');
    const legalEntity = document.getElementById('wrLegalEntityId');
    const warehouse = document.getElementById('wrWarehouseId');
    if (legalEntity) legalEntity.value = '';
    if (warehouse) warehouse.innerHTML = '<option value="">-- Chọn HKD trước --</option>';
}

function renderStatusBadge(status) {
    switch (Number(status)) {
        case 1:
            return `<span class="wr-badge wr-badge-draft">Đang làm</span>`;
        case 2:
            return `<span class="wr-badge wr-badge-pending">Chờ duyệt</span>`;
        case 4:
            return `<span class="wr-badge wr-badge-rejected">Cần sửa</span>`;
        default:
            return `<span class="wr-badge">Không rõ</span>`;
    }
}

function statusText(status) {
    switch (Number(status)) {
        case 1: return 'Đang làm Nháp';
        case 2: return 'Chờ duyệt';
        case 4: return 'Cần sửa Bị trả về';
        default: return '';
    }
}

function formatNumber(value) {
    return new Intl.NumberFormat('vi-VN').format(value || 0);
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
function getWrTotalPages() {
    const total = wrFilteredReceipts.length || 0;
    return Math.max(1, Math.ceil(total / wrPageSize));
}

function updateWrPagination(totalItems, startIndex, currentCount, totalPages) {
    const info = document.getElementById('wrPageInfo');
    const pageText = document.getElementById('wrCurrentPageText');
    const prevBtn = document.getElementById('wrBtnPrevPage');
    const nextBtn = document.getElementById('wrBtnNextPage');

    const from = totalItems === 0 ? 0 : startIndex + 1;
    const to = totalItems === 0 ? 0 : startIndex + currentCount;

    if (info) {
        info.textContent = `Hiển thị ${from} - ${to} / ${totalItems} phiếu`;
    }

    if (pageText) {
        pageText.textContent = `${wrPage} / ${totalPages}`;
    }

    if (prevBtn) {
        prevBtn.disabled = wrPage <= 1;
    }

    if (nextBtn) {
        nextBtn.disabled = wrPage >= totalPages;
    }
}
function formatDateTime(value) {
    if (!value) return '-';

    const d = new Date(value);
    if (isNaN(d.getTime())) return '-';

    return d.toLocaleString('vi-VN', {
        hour: '2-digit',
        minute: '2-digit',
        day: '2-digit',
        month: '2-digit',
        year: 'numeric'
    });
}
function focusCreateReceivingTitle() {
    const input = document.getElementById('wrDocumentTitle');
    if (!input) return;

    input.focus({ preventScroll: true });
    input.select();
}
