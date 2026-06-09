/* =========================================================
   STOCK COUNT UI SCRIPT
   File: wwwroot/Admin/js/stock-count.js

   Mục tiêu:
   - Dùng chung Index + Detail
   - Không dùng alert/confirm mặc định
   - Có search/filter/pagination Index
   - Có quick view khi double click
   - Có Select2 lookup barcode/sản phẩm
   - Keyboard flow:
     + Ctrl+S: lưu header
     + Ctrl+Enter: gửi duyệt / xác nhận
     + Alt+A: focus ô thêm sản phẩm
========================================================= */

let createStockCountModalInstance = null;
let editStockCountLineModalInstance = null;
let stockCountQuickViewModalInstance = null;
let stockCountHeaderSaveTimer = null;
let stockCountLineModalInstance = null;

const stockCountUiState = {
    allItems: [],
    filteredItems: [],
    currentPage: 1,
    pageSize: 10,
    currentDetail: null,
    keyboardBound: false
};

document.addEventListener('DOMContentLoaded', function () {
    if (!window.stockCountPage) return;

    initStockCountModalInstances();

    if (window.stockCountPage.mode === 'index') {
        bindStockCountIndexEvents();
        loadStockCountList();
    }

    if (window.stockCountPage.mode === 'detail') {
        bindStockCountDetailEvents();
        initStockCountQuickLookupSelect2();
        initStockCountWarehouseSelect();
        loadStockCountDetail();
    }
});

/* =========================================================
   COMMON
========================================================= */

async function readStockCountApiResponse(response) {
    const text = await response.text();

    try {
        return {
            ok: response.ok,
            status: response.status,
            data: text ? JSON.parse(text) : {}
        };
    } catch {
        return {
            ok: response.ok,
            status: response.status,
            data: { message: text }
        };
    }
}

function stockCountEscapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function stockCountFormatDateTime(value) {
    if (!value) return '';
    const d = new Date(value);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleString('vi-VN');
}

function stockCountFormatNumber(value) {
    return Number(value ?? 0).toLocaleString('vi-VN', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 3
    });
}

function stockCountToLocalInputValue(value) {
    if (!value) return '';

    const d = new Date(value);
    if (isNaN(d.getTime())) return '';

    return new Date(d.getTime() - d.getTimezoneOffset() * 60000)
        .toISOString()
        .slice(0, 16);
}

function initStockCountModalInstances() {
    const createModal = document.getElementById('createStockCountModal');
    const editModal = document.getElementById('editStockCountLineModal');
    const quickViewModal = document.getElementById('stockCountQuickViewModal');
    const lineModal = document.getElementById('stockCountLineModal');
    if (lineModal) stockCountLineModalInstance = new bootstrap.Modal(lineModal);

    if (createModal) createStockCountModalInstance = new bootstrap.Modal(createModal);
    if (editModal) editStockCountLineModalInstance = new bootstrap.Modal(editModal);
    if (quickViewModal) stockCountQuickViewModalInstance = new bootstrap.Modal(quickViewModal);
}

function stockCountStatusText(status) {
    switch (Number(status)) {
        case 1: return 'Nháp';
        case 2: return 'Chờ duyệt';
        case 3: return 'Đã xác nhận';
        case 4: return 'Bị từ chối';
        case 5: return 'Đã hủy';
        default: return 'Không rõ';
    }
}

function stockCountStatusBadge(status, isDetail = false) {
    const prefix = isDetail ? 'scd' : 'sc';

    switch (Number(status)) {
        case 1:
            return `<span class="${prefix}-status-draft"><i class="bx bx-edit"></i> Nháp</span>`;
        case 2:
            return `<span class="${prefix}-status-pending"><i class="bx bx-time-five"></i> Chờ duyệt</span>`;
        case 3:
            return `<span class="${prefix}-status-confirmed"><i class="bx bx-check-circle"></i> Đã xác nhận</span>`;
        case 4:
            return `<span class="${prefix}-status-rejected"><i class="bx bx-x-circle"></i> Bị từ chối</span>`;
        case 5:
            return `<span class="${prefix}-status-cancelled"><i class="bx bx-block"></i> Đã hủy</span>`;
        default:
            return `<span class="badge bg-secondary">Không rõ</span>`;
    }
}

function stockCountDiffClass(value) {
    const n = Number(value || 0);
    if (n > 0) return 'scd-diff-plus';
    if (n < 0) return 'scd-diff-minus';
    return 'scd-diff-zero';
}

function stockCountDiffRowClass(value) {
    const n = Number(value || 0);
    if (n > 0) return 'scd-diff-row-plus';
    if (n < 0) return 'scd-diff-row-minus';
    return 'scd-diff-row-zero';
}

function stockCountDiffConclusion(value) {
    const n = Number(value || 0);

    if (n > 0) {
        return '<span class="badge bg-label-success">Tăng tồn</span>';
    }

    if (n < 0) {
        return '<span class="badge bg-label-danger">Giảm tồn</span>';
    }

    return '<span class="badge bg-label-secondary">Khớp</span>';
}

function getStockCountPermissions() {
    return window.stockCountPage?.permissions || {};
}

function focusStockCountQuickLookup() {
    focusStockCountProductInput(250);
}

/* =========================================================
   TOAST / CONFIRM
========================================================= */

function scToast(type, title, message) {
    const containerId = 'stockCountToastContainer';
    let container = document.getElementById(containerId);

    if (!container) {
        container = document.createElement('div');
        container.id = containerId;
        container.className = 'toast-container position-fixed top-0 end-0 p-3';
        container.style.zIndex = '1090';
        document.body.appendChild(container);
    }

    const toastId = `sc-toast-${Date.now()}`;
    const icon = type === 'success'
        ? 'bx-check-circle'
        : type === 'danger'
            ? 'bx-error-circle'
            : type === 'warning'
                ? 'bx-error'
                : 'bx-info-circle';

    container.insertAdjacentHTML('beforeend', `
        <div id="${toastId}" class="toast sc-toast border-0 shadow-lg" role="alert">
            <div class="toast-body d-flex gap-3">
                <div class="sc-toast-icon sc-toast-${stockCountEscapeHtml(type)}">
                    <i class="bx ${icon}"></i>
                </div>

                <div class="flex-grow-1">
                    <div class="fw-bold">${stockCountEscapeHtml(title)}</div>
                    <div class="small text-muted">${stockCountEscapeHtml(message || '')}</div>
                </div>

                <button type="button" class="btn-close" data-bs-dismiss="toast"></button>
            </div>
        </div>
    `);

    const el = document.getElementById(toastId);
    const toast = new bootstrap.Toast(el, { delay: 2800 });

    toast.show();
    el.addEventListener('hidden.bs.toast', () => el.remove());
}

function scConfirm(options) {
    return new Promise(resolve => {
        const modalId = 'stockCountConfirmModal';
        document.getElementById(modalId)?.remove();

        document.body.insertAdjacentHTML('beforeend', `
            <div class="modal fade" id="${modalId}" tabindex="-1">
                <div class="modal-dialog modal-dialog-centered">
                    <div class="modal-content sc-confirm-modal">
                        <div class="modal-header border-0 pb-0">
                            <div>
                                <h5 class="modal-title fw-bold">${stockCountEscapeHtml(options.title || 'Xác nhận')}</h5>
                                <div class="text-muted small mt-1">${stockCountEscapeHtml(options.subtitle || '')}</div>
                            </div>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>

                        <div class="modal-body">
                            <div class="sc-confirm-box">
                                ${options.html || stockCountEscapeHtml(options.message || '')}
                            </div>
                        </div>

                        <div class="modal-footer border-0">
                            <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">
                                Đóng
                            </button>

                            <button type="button" class="btn ${options.okClass || 'btn-primary'}" id="btnScConfirmOk">
                                ${stockCountEscapeHtml(options.okText || 'Xác nhận')}
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `);

        const el = document.getElementById(modalId);
        const modal = new bootstrap.Modal(el);
        let settled = false;

        document.getElementById('btnScConfirmOk').onclick = () => {
            settled = true;
            resolve(true);
            modal.hide();
        };

        el.addEventListener('hidden.bs.modal', () => {
            if (!settled) resolve(false);
            el.remove();
        }, { once: true });

        modal.show();
    });
}

/* =========================================================
   INDEX
========================================================= */

function bindStockCountIndexEvents() {
    document.getElementById('btnReloadStockCountList')?.addEventListener('click', loadStockCountList);

    document.getElementById('btnOpenCreateStockCountModal')?.addEventListener('click', async function () {
        await loadStockCountWarehouseOptionsForCreate();
        resetCreateStockCountForm();
        createStockCountModalInstance?.show();

        setTimeout(() => {
            document.getElementById('createStockCountDocumentName')?.focus();
        }, 220);
    });

    document.getElementById('btnCreateStockCount')?.addEventListener('click', createStockCountDocument);

    document.getElementById('stockCountSearchInput')?.addEventListener('input', function () {
        stockCountUiState.currentPage = 1;
        applyStockCountIndexFilter();
    });

    document.getElementById('stockCountStatusFilter')?.addEventListener('change', function () {
        stockCountUiState.currentPage = 1;
        applyStockCountIndexFilter();
    });

    document.getElementById('stockCountPageSize')?.addEventListener('change', function () {
        stockCountUiState.pageSize = Number(this.value || 10);
        stockCountUiState.currentPage = 1;
        applyStockCountIndexFilter();
    });

    document.getElementById('btnResetStockCountFilter')?.addEventListener('click', function () {
        document.getElementById('stockCountSearchInput').value = '';
        document.getElementById('stockCountStatusFilter').value = 'working';
        document.getElementById('stockCountPageSize').value = '10';

        stockCountUiState.pageSize = 10;
        stockCountUiState.currentPage = 1;

        applyStockCountIndexFilter();
    });
}

async function loadStockCountList() {
    setIndexTableLoading();

    const response = await fetch('/admin/api/stock-counts');
    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        setIndexTableError(api.data?.message || 'Không tải được danh sách phiếu kiểm kê.');
        scToast('danger', 'Không tải được dữ liệu', api.data?.message || 'Vui lòng thử lại.');
        return;
    }

    stockCountUiState.allItems = Array.isArray(api.data) ? api.data : [];

    renderStockCountIndexSummary(stockCountUiState.allItems);
    applyStockCountIndexFilter();
}

function setIndexTableLoading() {
    const body = document.getElementById('stockCountIndexBody');

    if (body) {
        body.innerHTML = `<tr><td colspan="7" class="sc-empty">Đang tải dữ liệu...</td></tr>`;
    }

    const info = document.getElementById('stockCountListInfo');
    if (info) info.textContent = 'Đang tải...';
}

function setIndexTableError(message) {
    const body = document.getElementById('stockCountIndexBody');

    if (body) {
        body.innerHTML = `<tr><td colspan="7" class="sc-empty text-danger">${stockCountEscapeHtml(message)}</td></tr>`;
    }

    const info = document.getElementById('stockCountListInfo');
    if (info) info.textContent = 'Lỗi tải dữ liệu';
}

function renderStockCountIndexSummary(items) {
    const total = items.length;
    const working = items.filter(x => Number(x.status) === 1 || Number(x.status) === 4).length;
    const pending = items.filter(x => Number(x.status) === 2).length;
    const confirmed = items.filter(x => Number(x.status) === 3).length;

    document.getElementById('sumTotalDocuments').textContent = total.toString();
    document.getElementById('sumWorkingDocuments').textContent = working.toString();
    document.getElementById('sumPendingDocuments').textContent = pending.toString();
    document.getElementById('sumConfirmedDocuments').textContent = confirmed.toString();
}

function applyStockCountIndexFilter() {
    const keyword = (document.getElementById('stockCountSearchInput')?.value || '')
        .trim()
        .toLowerCase();

    const statusFilter = document.getElementById('stockCountStatusFilter')?.value || 'working';

    let items = [...stockCountUiState.allItems];

    if (statusFilter === 'working') {
        // Mặc định theo yêu cầu: hiển thị phiếu nháp/chờ duyệt.
        items = items.filter(x => Number(x.status) === 1 || Number(x.status) === 2);
    } else if (statusFilter !== 'all') {
        items = items.filter(x => Number(x.status) === Number(statusFilter));
    }

    if (keyword) {
        items = items.filter(x => {
            const haystack = [
                x.documentNo,
                x.documentName,
                x.warehouseName,
                x.note,
                stockCountStatusText(x.status)
            ].join(' ').toLowerCase();

            return haystack.includes(keyword);
        });
    }

    stockCountUiState.filteredItems = items;

    renderStockCountIndexRows();
    renderStockCountPagination();
}

function renderStockCountIndexRows() {
    const body = document.getElementById('stockCountIndexBody');
    if (!body) return;

    const items = stockCountUiState.filteredItems;
    const total = items.length;
    const pageSize = stockCountUiState.pageSize;
    const totalPages = Math.max(1, Math.ceil(total / pageSize));

    if (stockCountUiState.currentPage > totalPages) {
        stockCountUiState.currentPage = totalPages;
    }

    const start = (stockCountUiState.currentPage - 1) * pageSize;
    const pageItems = items.slice(start, start + pageSize);

    const info = document.getElementById('stockCountListInfo');
    if (info) {
        info.textContent = `${stockCountFormatNumber(total)} phiếu phù hợp`;
    }

    if (!pageItems.length) {
        body.innerHTML = `<tr><td colspan="8" class="sc-empty">Không có phiếu kiểm kê phù hợp.</td></tr>`;
        return;
    }

    body.innerHTML = pageItems.map(x => `
        <tr class="sc-index-row" data-document-id="${x.id}">
           <td>
    <div class="sc-document-cell">

        <div class="sc-document-main">
            ${stockCountEscapeHtml(x.documentName || 'Phiếu chưa đặt tên')}
        </div>

        <div class="sc-document-sub">
            <span class="sc-document-no">
                ${stockCountEscapeHtml(x.documentNo || '')}
            </span>

            <span class="sc-document-dot"></span>

            <span>ID: ${x.id}</span>

            <span class="sc-document-dot"></span>

           
        </div>
    </div>
</td>

            <td>${stockCountFormatDateTime(x.documentDate)}</td>
            <td>${stockCountEscapeHtml(x.warehouseName || '')}</td>
            <td>${stockCountStatusBadge(x.status)}</td>
            <td class="text-end fw-bold">${stockCountFormatNumber(x.totalLines || 0)}</td>
            <td>${stockCountEscapeHtml(x.note || '')}</td>

            <td class="text-end">
                <a href="/admin/stock-counts/${x.id}"
                   class="btn btn-sm btn-primary"
                   onclick="event.stopPropagation();">
                    Mở
                </a>
            </td>
        </tr>
    `).join('');

    bindStockCountIndexRowEvents();

    const paginationInfo = document.getElementById('stockCountPaginationInfo');
    if (paginationInfo) {
        const from = total === 0 ? 0 : start + 1;
        const to = Math.min(start + pageSize, total);
        paginationInfo.textContent = `Hiển thị ${from} - ${to} / ${total}`;
    }
}

function renderStockCountPagination() {
    const pagination = document.getElementById('stockCountPagination');
    if (!pagination) return;

    const total = stockCountUiState.filteredItems.length;
    const pageSize = stockCountUiState.pageSize;
    const totalPages = Math.max(1, Math.ceil(total / pageSize));
    const current = stockCountUiState.currentPage;

    const pages = [];

    pages.push(`
        <li class="page-item ${current <= 1 ? 'disabled' : ''}">
            <button class="page-link" data-page="${current - 1}">‹</button>
        </li>
    `);

    const from = Math.max(1, current - 2);
    const to = Math.min(totalPages, current + 2);

    for (let i = from; i <= to; i++) {
        pages.push(`
            <li class="page-item ${i === current ? 'active' : ''}">
                <button class="page-link" data-page="${i}">${i}</button>
            </li>
        `);
    }

    pages.push(`
        <li class="page-item ${current >= totalPages ? 'disabled' : ''}">
            <button class="page-link" data-page="${current + 1}">›</button>
        </li>
    `);

    pagination.innerHTML = pages.join('');

    pagination.querySelectorAll('button[data-page]').forEach(btn => {
        btn.onclick = function () {
            const page = Number(this.dataset.page || 1);
            if (page < 1 || page > totalPages) return;

            stockCountUiState.currentPage = page;
            renderStockCountIndexRows();
            renderStockCountPagination();
        };
    });
}

function bindStockCountIndexRowEvents() {
    document.querySelectorAll('.sc-index-row').forEach(row => {
        row.ondblclick = function () {
            const id = Number(this.dataset.documentId || 0);
            if (id > 0) openStockCountQuickView(id);
        };
    });
}

async function openStockCountQuickView(id) {
    const title = document.getElementById('quickViewTitle');
    const subtitle = document.getElementById('quickViewSubtitle');
    const body = document.getElementById('quickViewBody');
    const openBtn = document.getElementById('btnOpenQuickViewDetail');

    if (title) title.textContent = 'Xem nhanh phiếu kiểm kê';
    if (subtitle) subtitle.textContent = `Đang tải phiếu #${id}`;
    if (body) body.innerHTML = `<div class="text-muted">Đang tải dữ liệu...</div>`;
    if (openBtn) openBtn.href = `/admin/stock-counts/${id}`;

    stockCountQuickViewModalInstance?.show();

    const response = await fetch(`/admin/api/stock-counts/${id}`);
    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        if (body) {
            body.innerHTML = `<div class="text-danger">${stockCountEscapeHtml(api.data?.message || 'Không tải được chi tiết phiếu.')}</div>`;
        }

        return;
    }

    const detail = api.data;
    const lines = detail.lines || [];
    const grouped = buildStockCountGroupedItems(lines);
    const diffItems = grouped.filter(x => Number(x.difference || 0) !== 0);

    if (title) title.textContent = detail.documentNo || 'Phiếu kiểm kê';
    if (subtitle) subtitle.textContent = detail.documentName || detail.warehouseName || '';

    if (body) {
        body.innerHTML = `
            <div class="sc-quickview-grid mb-3">
                <div class="sc-quickview-item">
                    <div class="sc-quickview-label">Trạng thái</div>
                    <div class="sc-quickview-value">${stockCountStatusBadge(detail.status)}</div>
                </div>

                <div class="sc-quickview-item">
                    <div class="sc-quickview-label">Kho</div>
                    <div class="sc-quickview-value">${stockCountEscapeHtml(detail.warehouseName || '')}</div>
                </div>

                <div class="sc-quickview-item">
                    <div class="sc-quickview-label">Ngày phiếu</div>
                    <div class="sc-quickview-value">${stockCountFormatDateTime(detail.documentDate)}</div>
                </div>

                <div class="sc-quickview-item">
                    <div class="sc-quickview-label">Số dòng / loại SP lệch</div>
                    <div class="sc-quickview-value">
                        ${stockCountFormatNumber(lines.length)} dòng / ${stockCountFormatNumber(diffItems.length)} lệch
                    </div>
                </div>
            </div>

            <div class="text-muted small mb-2">Ghi chú</div>
            <div class="sc-confirm-box">${stockCountEscapeHtml(detail.note || 'Không có ghi chú')}</div>
        `;
    }
}

async function loadStockCountWarehouseOptionsForCreate() {
    const select = document.getElementById('createStockCountWarehouseId');
    if (!select) return;

    select.innerHTML = `<option value="">Đang tải kho...</option>`;

    const response = await fetch('/admin/api/warehouses/select2?term=');
    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        select.innerHTML = `<option value="">Không tải được kho</option>`;
        return;
    }

    const items = api.data?.results || [];
    select.innerHTML = `<option value="">-- Chọn kho --</option>` +
        items.map(x => `<option value="${x.id}">${stockCountEscapeHtml(x.text)}</option>`).join('');
}

function resetCreateStockCountForm() {
    const name = document.getElementById('createStockCountDocumentName');
    const warehouse = document.getElementById('createStockCountWarehouseId');
    const dateInput = document.getElementById('createStockCountDocumentDate');
    const note = document.getElementById('createStockCountNote');
    const msg = document.getElementById('createStockCountMessage');

    if (name) name.value = '';
    if (warehouse) warehouse.value = '';
    if (note) note.value = '';
    if (msg) msg.textContent = '';

    if (dateInput) {
        const now = new Date();
        dateInput.value = new Date(now.getTime() - now.getTimezoneOffset() * 60000)
            .toISOString()
            .slice(0, 16);
    }
}

async function createStockCountDocument() {
    const documentName = document.getElementById('createStockCountDocumentName')?.value?.trim() || '';
    const warehouseId = document.getElementById('createStockCountWarehouseId')?.value || '';
    const documentDate = document.getElementById('createStockCountDocumentDate')?.value || '';
    const note = document.getElementById('createStockCountNote')?.value || '';
    const msg = document.getElementById('createStockCountMessage');

    if (msg) msg.textContent = '';

    if (!warehouseId) {
        if (msg) msg.textContent = 'Vui lòng chọn kho kiểm kê.';
        document.getElementById('createStockCountWarehouseId')?.focus();
        return;
    }

    const btn = document.getElementById('btnCreateStockCount');
    if (btn) btn.disabled = true;

    const response = await fetch('/admin/api/stock-counts', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            documentName,
            warehouseId: Number(warehouseId),
            documentDate: documentDate ? new Date(documentDate).toISOString() : null,
            note
        })
    });

    const api = await readStockCountApiResponse(response);

    if (btn) btn.disabled = false;

    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Tạo phiếu kiểm kê thất bại.';
        scToast('danger', 'Tạo phiếu thất bại', api.data?.message || 'Vui lòng kiểm tra lại thông tin.');
        return;
    }

    createStockCountModalInstance?.hide();
    scToast('success', 'Tạo phiếu thành công', api.data?.message || 'Đang mở chi tiết phiếu.');

    window.location.href = `/admin/stock-counts/${api.data.id}`;
}

/* =========================================================
   DETAIL
========================================================= */

function bindStockCountDetailEvents() {
    document.getElementById('btnSubmitStockCountApproval')?.addEventListener('click', submitStockCountApproval);
    document.getElementById('btnConfirmStockCount')?.addEventListener('click', confirmStockCountDocument);
    document.getElementById('btnRejectStockCount')?.addEventListener('click', rejectStockCountDocument);
    document.getElementById('btnRefreshSystemQty')?.addEventListener('click', refreshStockCountSystemQty);
    
    

    document.getElementById('DocumentName')?.addEventListener('input', queueStockCountHeaderSave);
    document.getElementById('WarehouseId')?.addEventListener('change', queueStockCountHeaderSave);
    document.getElementById('DocumentDate')?.addEventListener('change', queueStockCountHeaderSave);
    document.getElementById('DocumentNote')?.addEventListener('input', queueStockCountHeaderSave);
    document.getElementById('LineModalCountedQty')
        ?.addEventListener('input', updateStockCountLineModalPreview);

    document.getElementById('btnSaveStockCountLineModal')
        ?.addEventListener('click', saveStockCountLineModal);

    document.getElementById('LineModalCountedQty')
        ?.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                saveStockCountLineModal();
            }
        });

    document.getElementById('ToggleOnlyDifferentGrouped')?.addEventListener('change', rerenderStockCountDetailTables);
    document.getElementById('ToggleOnlyDifferentLines')?.addEventListener('change', rerenderStockCountDetailTables);

    bindStockCountKeyboardEvents();
}
async function saveStockCountLineModal() {
    const mode = document.getElementById('LineModalMode')?.value || 'add';
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    const lineId = Number(document.getElementById('LineModalLineId')?.value || 0);
    const productVariantId = Number(document.getElementById('LineModalVariantId')?.value || 0);
    const unitId = Number(document.getElementById('LineModalUnitSelect')?.value || 0);
    const countedQty = Number(document.getElementById('LineModalCountedQty')?.value || 0);
    const note = document.getElementById('LineModalNote')?.value || '';
    const msg = document.getElementById('LineModalMessage');

    if (msg) msg.textContent = '';

    if (!unitId) {
        if (msg) msg.textContent = 'Vui lòng chọn đơn vị.';
        return;
    }

    if (countedQty < 0) {
        if (msg) msg.textContent = 'Số lượng kiểm kê không hợp lệ.';
        return;
    }

    let url = '';
    let method = '';
    let body = {};

    if (mode === 'add') {
        url = `/admin/api/stock-counts/${stockCountDocumentId}/lines`;
        method = 'POST';
        body = {
            productVariantId,
            unitId,
            countedQty,
            note
        };
    } else {
        url = `/admin/api/stock-counts/lines/${lineId}`;
        method = 'PUT';
        body = {
            unitId,
            countedQty,
            note
        };
    }

    const response = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
    });

    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Không thể lưu dòng kiểm kê.';
        scToast('danger', 'Lưu dòng thất bại', api.data?.message || 'Không thể lưu dòng kiểm kê.');
        return;
    }

    const updatedLineId = api.data?.id || lineId;
    if (updatedLineId) {
        sessionStorage.setItem('stockCountLastUpdatedLineId', updatedLineId);
    }

    stockCountLineModalInstance?.hide();

    $('#QuickLookupInput').val(null).trigger('change');
    $('#QuickLookupInput').removeData('selected-item');

    scToast('success', 'Đã lưu dòng kiểm kê', api.data?.message || 'Dòng kiểm kê đã được cập nhật.');

    await loadStockCountDetail();

    // Đợi modal đóng xong rồi mới mở lại ô quét sản phẩm.
    focusStockCountProductInput(650);
    focusStockCountQuickLookup();
}
function focusLineModalQty() {
    setTimeout(function () {
        const qty = document.getElementById('LineModalCountedQty');

        if (!qty) return;

        qty.focus();
        qty.select();
    }, 350);
}
function bindStockCountKeyboardEvents() {
    if (stockCountUiState.keyboardBound) return;
    stockCountUiState.keyboardBound = true;

    document.addEventListener('keydown', function (e) {
        if (!window.stockCountPage || window.stockCountPage.mode !== 'detail') return;

        const activeEl = document.activeElement;
        const isTyping = activeEl &&
            ['INPUT', 'TEXTAREA'].includes(activeEl.tagName) &&
            !e.ctrlKey &&
            !e.altKey;

        if (isTyping) return;

        if (e.ctrlKey && e.key.toLowerCase() === 's') {
            e.preventDefault();
            saveStockCountHeader(true);
        }

        if (e.ctrlKey && e.key === 'Enter') {
            e.preventDefault();

            const status = Number(stockCountUiState.currentDetail?.status || 0);

            if (status === 1 || status === 4) {
                submitStockCountApproval();
            } else if (status === 2) {
                confirmStockCountDocument();
            }
        }

        if (e.altKey && e.key.toLowerCase() === 'a') {
            e.preventDefault();
            focusStockCountQuickLookup();
        }
    });
}

async function initStockCountWarehouseSelect() {
    const select = document.getElementById('WarehouseId');
    if (!select) return;

    const response = await fetch('/admin/api/warehouses/select2?term=');
    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        select.innerHTML = `<option value="">Không tải được kho</option>`;
        return;
    }

    const items = api.data?.results || [];
    select.innerHTML = `<option value="">-- Chọn kho --</option>` +
        items.map(x => `<option value="${x.id}">${stockCountEscapeHtml(x.text)}</option>`).join('');
}

async function loadStockCountDetail() {
    const documentId = window.stockCountPage.stockCountDocumentId;

    const response = await fetch(`/admin/api/stock-counts/${documentId}`);
    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        scToast('danger', 'Không tải được chi tiết', api.data?.message || 'Vui lòng quay lại danh sách.');
        return;
    }

    const detail = api.data;
    stockCountUiState.currentDetail = detail;

    renderStockCountDetailHeader(detail);
    rerenderStockCountDetailTables();
    renderStockCountDetailSummary(detail.lines || []);
    applyStockCountReadonlyState(detail.status);

    if (Number(detail.status) === 1 || Number(detail.status) === 4) {
        focusStockCountProductInput(450);
    }
}

function renderStockCountDetailHeader(detail) {
    document.getElementById('DocumentNo').value = detail.documentNo || '';

    const documentName = document.getElementById('DocumentName');
    if (documentName) documentName.value = detail.documentName || '';

    document.getElementById('WarehouseId').value = detail.warehouseId || '';
    document.getElementById('DocumentNote').value = detail.note || '';
    document.getElementById('DocumentStatusBadge').innerHTML = stockCountStatusBadge(detail.status, true);

    const documentDate = document.getElementById('DocumentDate');
    if (documentDate) {
        documentDate.value = stockCountToLocalInputValue(detail.documentDate);
    }
}

function rerenderStockCountDetailTables() {
    const detail = stockCountUiState.currentDetail;
    if (!detail) return;

    const lines = detail.lines || [];
    const onlyDifferentGrouped = document.getElementById('ToggleOnlyDifferentGrouped')?.checked === true;
    const onlyDifferentLines = document.getElementById('ToggleOnlyDifferentLines')?.checked === true;

    renderStockCountGroupedSummary(lines, onlyDifferentGrouped);
    renderStockCountLines(lines, onlyDifferentLines);
}

function buildStockCountGroupedItems(lines) {
    const groups = new Map();

    (lines || []).forEach(line => {
        const key = String(line.productVariantId);

        if (!groups.has(key)) {
            groups.set(key, {
                productVariantId: line.productVariantId,
                productNameSnapshot: line.productNameSnapshot,
                skuSnapshot: line.skuSnapshot,
                barcodeSnapshot: line.barcodeSnapshot,
                imageUrl: line.imageUrl || null,
                systemQtyBase: Number(line.systemQtyBase || 0),
                countedQtyBaseSum: 0,
                units: []
            });
        }

        const item = groups.get(key);
        item.countedQtyBaseSum += Number(line.countedQtyBase || 0);

        const unitText = `${line.unitName || ''}: ${stockCountFormatNumber(line.countedQty || 0)}`;
        item.units.push(unitText);
    });

    return Array.from(groups.values()).map(item => ({
        ...item,
        difference: item.countedQtyBaseSum - item.systemQtyBase
    }));
}

function renderStockCountDetailSummary(lines) {
    const groupedItems = buildStockCountGroupedItems(lines);

    const totalLines = lines.length;
    const totalProductTypes = groupedItems.length;
    const totalSystemQtyBase = groupedItems.reduce((sum, x) => sum + Number(x.systemQtyBase || 0), 0);
    const totalCountedQtyBase = groupedItems.reduce((sum, x) => sum + Number(x.countedQtyBaseSum || 0), 0);
    const sumDifference = totalCountedQtyBase - totalSystemQtyBase;
    const differentLines = groupedItems.filter(x => Number(x.difference || 0) !== 0).length;

    document.getElementById('sumTotalLines').textContent = stockCountFormatNumber(totalLines);
    document.getElementById('sumProductTypes').textContent = stockCountFormatNumber(totalProductTypes);
    document.getElementById('sumSystemQtyBase').textContent = stockCountFormatNumber(totalSystemQtyBase);
    document.getElementById('sumCountedQtyBase').textContent = stockCountFormatNumber(totalCountedQtyBase);
    document.getElementById('sumDifferentLines').textContent = stockCountFormatNumber(differentLines);
    document.getElementById('sumDifferenceQtyBase').textContent =
        `${sumDifference > 0 ? '+' : ''}${stockCountFormatNumber(sumDifference)}`;
}

function renderStockCountLines(lines, onlyDifferent = false) {
    const body = document.getElementById('stockCountLineTableBody');
    if (!body) return;

    let renderLines = [...(lines || [])];

    if (onlyDifferent) {
        renderLines = renderLines.filter(line => Number(line.differenceQtyBase || 0) !== 0);
    }

    if (!renderLines.length) {
        body.innerHTML = `<tr><td colspan="11" class="text-center text-muted py-4">Không có dòng kiểm kê phù hợp.</td></tr>`;
        return;
    }

    const permissions = getStockCountPermissions();
    const canUpdate = permissions.canUpdate === true;
    const canDelete = permissions.canDelete === true;
    const status = Number(stockCountUiState.currentDetail?.status || 0);
    const isEditable = status === 1 || status === 4;

    const lastUpdatedLineId = Number(sessionStorage.getItem('stockCountLastUpdatedLineId') || 0);

    body.innerHTML = renderLines.map(line => {
        const diff = Number(line.differenceQtyBase || 0);
        const rowClass = `${stockCountDiffRowClass(diff)} ${lastUpdatedLineId === Number(line.id) ? 'scd-last-updated' : ''}`.trim();

        const imageHtml = line.imageUrl
            ? `<img src="${stockCountEscapeHtml(line.imageUrl)}" class="sc-product-img" alt="">`
            : `<div class="sc-product-img-placeholder"><i class="bx bx-package"></i></div>`;

        const actionHtml = isEditable
            ? `
                ${canUpdate ? `
                    <button type="button"
                            class="btn btn-sm btn-warning btn-edit-stock-count-line"
                            data-line-id="${line.id}"
                            data-variant-id="${line.productVariantId}"
                            data-unit-id="${line.unitId}"
                            data-system-qty="${line.systemQtyBase}"
                            data-counted-qty="${line.countedQty}"
                            data-product-name="${stockCountEscapeHtml(line.productNameSnapshot || '')}"
                            data-note="${stockCountEscapeHtml(line.note || '')}">
                        Sửa
                    </button>
                ` : ''}

                ${canDelete ? `
                    <button type="button"
                            class="btn btn-sm btn-danger btn-delete-stock-count-line"
                            data-line-id="${line.id}">
                        Xóa
                    </button>
                ` : ''}
            `
            : `<span class="text-muted small">Đã khóa</span>`;

        return `
            <tr class="${rowClass}" data-line-id="${line.id}">
                <td class="fw-bold">${line.lineNo}</td>

                <td>
                    <div class="sc-product-cell">
                        ${imageHtml}

                        <div>
                            <div class="sc-product-name">${stockCountEscapeHtml(line.productNameSnapshot || '')}</div>
                            <div class="sc-product-sku">SKU: ${stockCountEscapeHtml(line.skuSnapshot || '-')}</div>
                            <div class="sc-product-barcode">Barcode: ${stockCountEscapeHtml(line.barcodeSnapshot || '-')}</div>
                        </div>
                    </div>
                </td>

                <td>${stockCountEscapeHtml(line.unitName || '')}</td>
                <td class="text-end">${stockCountFormatNumber(line.factor)}</td>
                <td class="text-end">${stockCountFormatNumber(line.systemQtyBase)}</td>
                <td class="text-end fw-bold">${stockCountFormatNumber(line.countedQty)}</td>
                <td class="text-end">${stockCountFormatNumber(line.countedQtyBase)}</td>

                <td class="text-end ${stockCountDiffClass(diff)}">
                    ${diff > 0 ? '+' : ''}${stockCountFormatNumber(diff)}
                </td>

                <td>${stockCountDiffConclusion(diff)}</td>
                <td>${stockCountEscapeHtml(line.note || '')}</td>

                <td class="text-end">
                    <div class="d-inline-flex gap-1">
                        ${actionHtml}
                    </div>
                </td>
            </tr>
        `;
    }).join('');

    bindEditStockCountLineButtons();
    bindDeleteStockCountLineButtons();

    if (lastUpdatedLineId > 0) {
        setTimeout(() => {
            sessionStorage.removeItem('stockCountLastUpdatedLineId');
        }, 1200);
    }
}

function renderStockCountGroupedSummary(lines, onlyDifferent = false) {
    const body = document.getElementById('stockCountGroupedBody');
    if (!body) return;

    let groupedItems = buildStockCountGroupedItems(lines);

    if (onlyDifferent) {
        groupedItems = groupedItems.filter(x => Number(x.difference || 0) !== 0);
    }

    if (!groupedItems.length) {
        body.innerHTML = `<tr><td colspan="6" class="text-center text-muted py-4">Không có dữ liệu tổng hợp phù hợp.</td></tr>`;
        return;
    }

    body.innerHTML = groupedItems.map(item => {
        const diff = Number(item.difference || 0);

        return `
            <tr>
              <td>
    <div class="sc-product-cell">
        ${item.imageUrl
                ? `<img src="${stockCountEscapeHtml(item.imageUrl)}" class="sc-product-img" alt="">`
                : `<div class="sc-product-img-placeholder"><i class="bx bx-package"></i></div>`
            }

        <div>
            <div class="sc-product-name">${stockCountEscapeHtml(item.productNameSnapshot || '')}</div>
            <div class="sc-product-barcode">Barcode: ${stockCountEscapeHtml(item.barcodeSnapshot || '-')}</div>
            <div class="sc-product-sku">SKU: ${stockCountEscapeHtml(item.skuSnapshot || '-')}</div>
        </div>
    </div>
</td>

                <td class="text-end">${stockCountFormatNumber(item.systemQtyBase)}</td>
                <td class="text-end fw-bold">${stockCountFormatNumber(item.countedQtyBaseSum)}</td>

                <td class="text-end ${stockCountDiffClass(diff)}">
                    ${diff > 0 ? '+' : ''}${stockCountFormatNumber(diff)}
                </td>

                <td>${stockCountDiffConclusion(diff)}</td>
                <td>${stockCountEscapeHtml(item.units.join(' | '))}</td>
            </tr>
        `;
    }).join('');
}

function applyStockCountReadonlyState(status) {
    const permissions = getStockCountPermissions();

    const canUpdate = permissions.canUpdate === true;
    const canApprove = permissions.canApprove === true;

    const isEditableStatus = Number(status) === 1 || Number(status) === 4;
    const isPending = Number(status) === 2;
    const canEdit = canUpdate && isEditableStatus;

    const documentName = document.getElementById('DocumentName');
    if (documentName) documentName.disabled = !canEdit;

    const warehouse = document.getElementById('WarehouseId');
    if (warehouse) warehouse.disabled = !canEdit;

    const documentDate = document.getElementById('DocumentDate');
    if (documentDate) documentDate.disabled = !canEdit;

    const documentNote = document.getElementById('DocumentNote');
    if (documentNote) documentNote.disabled = !canEdit;

    const quickLookup = $('#QuickLookupInput');
    if (quickLookup.length) quickLookup.prop('disabled', !canEdit);

    const quickQty = document.getElementById('QuickCountedQty');
    if (quickQty) quickQty.disabled = !canEdit;

    const quickAddBtn = document.getElementById('btnQuickAddStockCountLine');
    if (quickAddBtn) quickAddBtn.disabled = !canEdit;

    const submitBtn = document.getElementById('btnSubmitStockCountApproval');
    if (submitBtn) submitBtn.disabled = !canEdit;

    const refreshBtn = document.getElementById('btnRefreshSystemQty');
    if (refreshBtn) refreshBtn.disabled = !canEdit;

    const confirmBtn = document.getElementById('btnConfirmStockCount');
    if (confirmBtn) confirmBtn.disabled = !(canApprove && isPending);

    const rejectBtn = document.getElementById('btnRejectStockCount');
    if (rejectBtn) rejectBtn.disabled = !(canApprove && isPending);

    document.querySelectorAll('.btn-edit-stock-count-line').forEach(btn => btn.disabled = !canEdit);
    document.querySelectorAll('.btn-delete-stock-count-line').forEach(btn => btn.disabled = !canEdit);
}
function focusStockCountProductInput(delay = 250) {
    const select = $('#QuickLookupInput');

    if (!select.length || !window.jQuery || !$.fn.select2) {
        return;
    }

    setTimeout(function () {
        try {
            select.select2('open');
        } catch {
            return;
        }

        // Select2 cần thêm thời gian render input search thật.
        setTimeout(function () {
            const searchInput = document.querySelector(
                '.select2-container--open .select2-search__field'
            );

            if (!searchInput) {
                // Retry nhẹ nếu dropdown vừa bị Bootstrap/modal giành focus.
                setTimeout(function () {
                    select.select2('open');

                    setTimeout(function () {
                        const retryInput = document.querySelector(
                            '.select2-container--open .select2-search__field'
                        );

                        retryInput?.focus();
                        retryInput?.select();
                    }, 120);
                }, 180);

                return;
            }

            searchInput.focus();
            searchInput.select();
        }, 140);
    }, delay);
}
function queueStockCountHeaderSave() {
    if (stockCountHeaderSaveTimer) clearTimeout(stockCountHeaderSaveTimer);
    stockCountHeaderSaveTimer = setTimeout(() => saveStockCountHeader(false), 650);
}

async function saveStockCountHeader(showToast) {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    const warehouseId = Number(document.getElementById('WarehouseId')?.value || 0);
    const documentName = document.getElementById('DocumentName')?.value?.trim() || '';
    const documentDate = document.getElementById('DocumentDate')?.value || '';
    const note = document.getElementById('DocumentNote')?.value || '';

    if (!stockCountDocumentId || !warehouseId) return;

    const response = await fetch('/admin/api/stock-counts/update-header', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            stockCountDocumentId,
            documentName,
            warehouseId,
            documentDate: documentDate ? new Date(documentDate).toISOString() : null,
            note
        })
    });

    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        scToast('danger', 'Lưu phiếu thất bại', api.data?.message || 'Không thể lưu thông tin phiếu.');
        return;
    }

    if (showToast) {
        scToast('success', 'Đã lưu phiếu', api.data?.message || 'Thông tin phiếu kiểm kê đã được lưu.');
    }
}

/* =========================================================
   SELECT2 LOOKUP
========================================================= */

function initStockCountQuickLookupSelect2() {
    const el = $('#QuickLookupInput');
    if (!el.length) return;

    el.select2({
        theme: 'bootstrap-5',
        width: '100%',
        placeholder: 'Quét barcode / nhập SKU / gõ tên sản phẩm...',
        minimumInputLength: 1,
        ajax: {
            url: '/admin/api/inventory-adjustments/product-lookup-select2',
            dataType: 'json',
            delay: 120,
            data: function (params) {
                return { term: params.term || '' };
            },
            processResults: function (data) {
                return data;
            }
        },
        templateResult: formatStockCountLookupResult,
        templateSelection: formatStockCountLookupSelection,
        escapeMarkup: function (markup) {
            return markup;
        }
    });

    el.on('select2:select', function (e) {
        const item = e.params.data;
        el.data('selected-item', item);

        openStockCountLineModalForAdd(item);
    });

    el.on('select2:clear', function () {
        el.removeData('selected-item');
    });

    bindStockCountLookupEnterAutoSelect();
}
async function openStockCountLineModalForAdd(item) {
    $('#QuickLookupInput').select2('close');
  

    document.getElementById('LineModalMode').value = 'add';
    document.getElementById('LineModalLineId').value = '';
    document.getElementById('LineModalVariantId').value = item.productVariantId || '';
    document.getElementById('LineModalCountedQty').value = '1';
    document.getElementById('LineModalNote').value = '';
    document.getElementById('LineModalMessage').textContent = '';

    setLineModalProductInfo({
        productName: item.productName || item.text || '-',
        sku: item.sku || '-',
        barcode: item.barcode || '-',
        unitName: item.unitName || '-',
        factor: item.factor || 1,
        imageUrl: item.imageUrl || '',
        systemQty: 0
    });

    await loadStockCountLineModalUnits(item.productVariantId, item.unitId);

    updateStockCountLineModalPreview();

    stockCountLineModalInstance?.show();
    focusLineModalQty();

    setTimeout(() => {
        const qty = document.getElementById('LineModalCountedQty');
        qty?.focus();
        qty?.select();
    }, 250);
}

function formatStockCountLookupResult(item) {
    if (!item.id) return item.text || '';

    const title = stockCountEscapeHtml(item.productName || '');
    const img = item.imageUrl
        ? `<img src="${stockCountEscapeHtml(item.imageUrl)}" class="select2-result-img" alt="">`
        : `<div class="select2-result-img d-flex align-items-center justify-content-center"><i class="bx bx-package"></i></div>`;

    const meta = [
        item.sku ? `SKU: ${stockCountEscapeHtml(item.sku)}` : '',
        item.unitName ? `Đơn vị: ${stockCountEscapeHtml(item.unitName)}` : '',
        item.factor ? `x${item.factor}` : '',
        item.barcode ? `BC: ${stockCountEscapeHtml(item.barcode)}` : '',
        item.sourceType ? stockCountEscapeHtml(item.sourceType) : ''
    ].filter(Boolean).join(' | ');

    return `
        <div class="select2-result-row">
            ${img}
            <div>
                <div class="select2-result-title">${title}</div>
                <div class="select2-result-meta">${meta}</div>
            </div>
        </div>
    `;
}

function formatStockCountLookupSelection(item) {
    if (!item || !item.id) return item.text || 'Chọn sản phẩm';

    const parts = [];

    if (item.productName) parts.push(item.productName);
    if (item.unitName) parts.push(item.unitName);
    if (item.factor) parts.push('x' + item.factor);
    if (item.barcode) parts.push(item.barcode);

    return stockCountEscapeHtml(parts.join(' | '));
}

function bindStockCountLookupEnterAutoSelect() {
    $(document).off('keydown.stockCountSelect2Auto');
    $(document).on('keydown.stockCountSelect2Auto', '.select2-container--open .select2-search__field', async function (e) {
        if (e.key !== 'Enter') return;

        const term = $(this).val();
        if (!term) return;

        const response = await fetch(`/admin/api/inventory-adjustments/product-lookup-select2?term=${encodeURIComponent(term)}`);
        const api = await readStockCountApiResponse(response);

        if (!api.ok || !api.data?.results || api.data.results.length !== 1) return;

        e.preventDefault();

        const item = api.data.results[0];
        const el = $('#QuickLookupInput');

        const option = new Option(item.text, item.id, true, true);
        el.append(option).trigger('change');
        el.trigger({
            type: 'select2:select',
            params: { data: item }
        });

        el.select2('close');
    });
}

/* =========================================================
   LINE ACTIONS
========================================================= */

async function quickAddStockCountLine() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    const productVariantId = Number(document.getElementById('SelectedProductVariantId')?.value || 0);
    const unitId = Number(document.getElementById('SelectedUnitId')?.value || 0);
    const countedQty = Number(document.getElementById('QuickCountedQty')?.value || 0);
    const selectedItem = $('#QuickLookupInput').data('selected-item');

    if (!productVariantId || !unitId) {
        scToast('warning', 'Chưa chọn sản phẩm', 'Vui lòng quét barcode hoặc chọn sản phẩm trước.');
        focusStockCountQuickLookup();
        return;
    }

    if (countedQty < 0) {
        scToast('warning', 'Số lượng không hợp lệ', 'Số lượng kiểm kê không được nhỏ hơn 0.');
        document.getElementById('QuickCountedQty')?.focus();
        return;
    }

    let note = null;

    if (selectedItem && selectedItem.barcode &&
        (
            selectedItem.sourceType === 'UnitBarcode' ||
            selectedItem.sourceType === 'VariantBarcode' ||
            selectedItem.sourceType === 'BarcodeHistory'
        )) {
        note = `Quét mã: ${selectedItem.barcode}`;
    }

    const btn = document.getElementById('btnQuickAddStockCountLine');
    if (btn) btn.disabled = true;

    const response = await fetch(`/admin/api/stock-counts/${stockCountDocumentId}/lines`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            productVariantId,
            unitId,
            countedQty,
            note
        })
    });

    const api = await readStockCountApiResponse(response);

    if (btn) btn.disabled = false;

    if (!api.ok) {
        scToast('danger', 'Thêm dòng thất bại', api.data?.message || 'Không thể thêm dòng kiểm kê.');
        focusStockCountQuickLookup();
        return;
    }

    if (api.data?.id) {
        sessionStorage.setItem('stockCountLastUpdatedLineId', api.data.id);
    }

    $('#QuickLookupInput').val(null).trigger('change');
    $('#QuickLookupInput').removeData('selected-item');

    document.getElementById('SelectedProductVariantId').value = '';
    document.getElementById('SelectedUnitId').value = '';
    document.getElementById('SelectedFactor').value = '';
    document.getElementById('QuickCountedQty').value = '1';

    scToast('success', 'Đã thêm dòng', api.data?.message || 'Dòng kiểm kê đã được cập nhật.');
    await loadStockCountDetail();
    focusStockCountQuickLookup();
}
function bindEditStockCountLineButtons() {
    document.querySelectorAll('.btn-edit-stock-count-line').forEach(btn => {
        btn.onclick = async function () {
            const lineId = this.dataset.lineId || '';
            const variantId = this.dataset.variantId || '';
            const unitId = this.dataset.unitId || '';
            const countedQty = this.dataset.countedQty || '0';
            const productName = this.dataset.productName || '-';
            const note = this.dataset.note || '';

            const row = this.closest('tr');

            const barcode = row?.querySelector('.sc-product-barcode')?.textContent?.replace('Barcode:', '').trim() || '-';
            const sku = row?.querySelector('.sc-product-sku')?.textContent?.replace('SKU:', '').trim() || '-';
            const img = row?.querySelector('.sc-product-img')?.getAttribute('src') || '';
            const systemQty = this.dataset.systemQty || '0';

            document.getElementById('LineModalMode').value = 'edit';
            document.getElementById('LineModalLineId').value = lineId;
            document.getElementById('LineModalVariantId').value = variantId;
            document.getElementById('LineModalCountedQty').value = countedQty;
            document.getElementById('LineModalNote').value = note;
            document.getElementById('LineModalMessage').textContent = '';

            setLineModalProductInfo({
                productName,
                sku,
                barcode,
                unitName: '-',
                factor: 1,
                imageUrl: img,
                systemQty
            });

            document.getElementById('LineModalTitle').textContent = 'Sửa số lượng kiểm kê';
            document.getElementById('LineModalSubtitle').textContent = 'Nhập số lượng rồi nhấn Enter để lưu.';

            await loadStockCountLineModalUnits(variantId, unitId);

            updateStockCountLineModalPreview();

            stockCountLineModalInstance?.show();
            focusLineModalQty();

            setTimeout(() => {
                const qty = document.getElementById('LineModalCountedQty');
                qty?.focus();
                qty?.select();
            }, 250);
        };
    });
}
function setLineModalProductInfo(item) {
    document.getElementById('LineModalProductName').textContent = item.productName || '-';
    document.getElementById('LineModalBarcode').textContent = item.barcode || '-';
    document.getElementById('LineModalUnitName').textContent = item.unitName || '-';
    document.getElementById('LineModalFactor').textContent = item.factor || 1;
    document.getElementById('LineModalSystemQty').textContent = stockCountFormatNumber(item.systemQty || 0);

    const img = document.getElementById('LineModalProductImage');
    const noImg = document.getElementById('LineModalNoImage');

    if (item.imageUrl) {
        img.src = item.imageUrl;
        img.classList.remove('d-none');
        noImg.classList.add('d-none');
    } else {
        img.src = '';
        img.classList.add('d-none');
        noImg.classList.remove('d-none');
    }
}

function updateStockCountLineModalPreview() {
    const qty = Number(document.getElementById('LineModalCountedQty')?.value || 0);
    const select = document.getElementById('LineModalUnitSelect');
    const option = select?.options[select.selectedIndex];

    const factor = Number(option?.getAttribute('data-factor') || 1);
    const systemQty = Number(
        String(document.getElementById('LineModalSystemQty')?.textContent || '0')
            .replaceAll('.', '')
            .replace(',', '.')
    ) || 0;

    const baseQty = qty * factor;
    const diff = baseQty - systemQty;

    document.getElementById('LineModalFactor').textContent = stockCountFormatNumber(factor);
    document.getElementById('LineModalBaseQtyPreview').textContent = stockCountFormatNumber(baseQty);
    document.getElementById('LineModalDiffPreview').textContent =
        `${diff > 0 ? '+' : ''}${stockCountFormatNumber(diff)}`;

    const diffEl = document.getElementById('LineModalDiffPreview');
    diffEl.classList.remove('text-success', 'text-danger', 'text-muted');

    if (diff > 0) diffEl.classList.add('text-success');
    else if (diff < 0) diffEl.classList.add('text-danger');
    else diffEl.classList.add('text-muted');
}

async function loadStockCountLineModalUnits(variantId, selectedUnitId) {
    const select = document.getElementById('LineModalUnitSelect');
    const info = document.getElementById('LineModalUnitInfo');

    select.innerHTML = `<option value="">Đang tải...</option>`;
    if (info) info.textContent = '';

    const response = await fetch(`/admin/api/stock-documents/product-variants/${variantId}/units`);
    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        select.innerHTML = `<option value="">Không tải được đơn vị</option>`;
        if (info) info.textContent = api.data?.message || '';
        return;
    }

    const items = api.data || [];

    select.innerHTML = items.map(x => {
        const selected = Number(selectedUnitId) === Number(x.unitId) ? 'selected' : '';
        return `<option value="${x.unitId}" data-factor="${x.factor}" data-unit-name="${stockCountEscapeHtml(x.unitName)}" ${selected}>
            ${stockCountEscapeHtml(x.unitName)} (x${x.factor})
        </option>`;
    }).join('');

    updateStockCountLineModalUnitInfo();

    select.onchange = function () {
        updateStockCountLineModalUnitInfo();
        updateStockCountLineModalPreview();
    };
}

function updateStockCountLineModalUnitInfo() {
    const select = document.getElementById('LineModalUnitSelect');
    const info = document.getElementById('LineModalUnitInfo');

    const option = select?.options[select.selectedIndex];

    if (!option || !option.value) {
        if (info) info.textContent = '';
        return;
    }

    const factor = option.getAttribute('data-factor') || '1';
    const unitName = option.getAttribute('data-unit-name') || option.textContent || '-';

    document.getElementById('LineModalUnitName').textContent = unitName.replace(/\(x.*?\)/, '').trim();
    document.getElementById('LineModalFactor').textContent = factor;

    if (info) {
        info.textContent = `Hệ số quy đổi về đơn vị gốc: x${factor}`;
    }
}

async function loadStockCountLineUnits(variantId, selectedUnitId) {
    const select = document.getElementById('EditLineUnitId');
    const info = document.getElementById('EditLineUnitInfo');

    if (!select) return;

    select.innerHTML = `<option value="">Đang tải...</option>`;
    if (info) info.textContent = '';

    const response = await fetch(`/admin/api/stock-documents/product-variants/${variantId}/units`);
    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        select.innerHTML = `<option value="">Không tải được đơn vị</option>`;
        if (info) info.textContent = api.data?.message || 'Không tải được cấu hình đơn vị.';
        return;
    }

    const items = api.data || [];

    if (!items.length) {
        select.innerHTML = `<option value="">Không có đơn vị</option>`;
        return;
    }

    select.innerHTML = items.map(x => {
        const selected = Number(selectedUnitId) === Number(x.unitId) ? 'selected' : '';
        return `<option value="${x.unitId}" data-factor="${x.factor}" ${selected}>
            ${stockCountEscapeHtml(x.unitName)} (x${x.factor})
        </option>`;
    }).join('');

    updateStockCountEditUnitInfo();
    select.onchange = updateStockCountEditUnitInfo;
}

function updateStockCountEditUnitInfo() {
    const select = document.getElementById('EditLineUnitId');
    const info = document.getElementById('EditLineUnitInfo');

    if (!select || !info) return;

    const option = select.options[select.selectedIndex];

    if (!option || !option.value) {
        info.textContent = '';
        return;
    }

    const factor = option.getAttribute('data-factor') || '1';
    info.textContent = `Hệ số quy đổi về đơn vị gốc: x${factor}`;
}

async function saveEditStockCountLine() {
    const lineId = Number(document.getElementById('EditLineId')?.value || 0);
    const unitId = Number(document.getElementById('EditLineUnitId')?.value || 0);
    const countedQty = Number(document.getElementById('EditCountedQty')?.value || 0);
    const note = document.getElementById('EditLineNote')?.value || '';
    const msg = document.getElementById('EditLineMessage');

    if (msg) msg.textContent = '';

    if (!lineId || !unitId) {
        if (msg) msg.textContent = 'Thông tin dòng không hợp lệ.';
        return;
    }

    if (countedQty < 0) {
        if (msg) msg.textContent = 'Số lượng đếm không hợp lệ.';
        return;
    }

    const response = await fetch(`/admin/api/stock-counts/lines/${lineId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            unitId,
            countedQty,
            note
        })
    });

    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Cập nhật dòng thất bại.';
        scToast('danger', 'Cập nhật thất bại', api.data?.message || 'Không thể lưu dòng kiểm kê.');
        return;
    }

    sessionStorage.setItem('stockCountLastUpdatedLineId', lineId.toString());

    editStockCountLineModalInstance?.hide();
    scToast('success', 'Đã cập nhật dòng', api.data?.message || 'Số lượng kiểm kê đã được lưu.');

    await loadStockCountDetail();
}

function bindDeleteStockCountLineButtons() {
    document.querySelectorAll('.btn-delete-stock-count-line').forEach(btn => {
        btn.onclick = async function () {
            const lineId = this.dataset.lineId;
            const row = this.closest('tr');
            const productName = row?.querySelector('.sc-product-name')?.textContent || 'Dòng kiểm kê';

            const ok = await scConfirm({
                title: 'Xóa dòng kiểm kê?',
                subtitle: productName,
                message: 'Dòng này sẽ bị xóa khỏi phiếu. Thao tác này không dùng confirm mặc định.',
                okText: 'Xóa dòng',
                okClass: 'btn-danger'
            });

            if (!ok) return;

            const response = await fetch(`/admin/api/stock-counts/lines/${lineId}`, {
                method: 'DELETE'
            });

            const api = await readStockCountApiResponse(response);

            if (!api.ok) {
                scToast('danger', 'Xóa thất bại', api.data?.message || 'Không thể xóa dòng.');
                return;
            }

            scToast('success', 'Đã xóa dòng', api.data?.message || 'Dòng kiểm kê đã được xóa.');
            await loadStockCountDetail();
        };
    });
}

/* =========================================================
   DOCUMENT ACTIONS
========================================================= */

async function submitStockCountApproval() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    if (!stockCountDocumentId) return;

    const detail = stockCountUiState.currentDetail;
    const lines = detail?.lines || [];

    const ok = await scConfirm({
        title: 'Gửi phiếu chờ duyệt?',
        subtitle: 'Phiếu sẽ khóa chỉnh sửa cho đến khi được duyệt hoặc từ chối.',
        html: `
            <div class="row g-2">
                <div class="col-6">Số phiếu</div>
                <div class="col-6 fw-bold text-end">${stockCountEscapeHtml(detail?.documentNo || '')}</div>

                <div class="col-6">Kho</div>
                <div class="col-6 fw-bold text-end">${stockCountEscapeHtml(detail?.warehouseName || '')}</div>

                <div class="col-6">Số dòng</div>
                <div class="col-6 fw-bold text-end">${stockCountFormatNumber(lines.length)}</div>
            </div>
        `,
        okText: 'Gửi duyệt',
        okClass: 'btn-warning'
    });

    if (!ok) return;

    const response = await fetch(`/admin/api/stock-counts/${stockCountDocumentId}/submit-approval`, {
        method: 'POST'
    });

    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        scToast('danger', 'Gửi duyệt thất bại', api.data?.message || 'Không thể gửi duyệt phiếu.');
        return;
    }

    scToast('success', 'Đã gửi duyệt', api.data?.message || 'Phiếu đã chuyển sang chờ duyệt.');
    await loadStockCountDetail();
}

async function confirmStockCountDocument() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    if (!stockCountDocumentId) return;

    const detail = stockCountUiState.currentDetail;
    const groupedItems = buildStockCountGroupedItems(detail?.lines || []);
    const diffItems = groupedItems.filter(x => Number(x.difference || 0) !== 0);

    const ok = await scConfirm({
        title: 'Duyệt xác nhận kiểm kê?',
        subtitle: 'Sau khi xác nhận, phiếu sẽ khóa và phát sinh movement kho.',
        html: `
            <div class="row g-2">
                <div class="col-6">Số phiếu</div>
                <div class="col-6 fw-bold text-end">${stockCountEscapeHtml(detail?.documentNo || '')}</div>

                <div class="col-6">Kho</div>
                <div class="col-6 fw-bold text-end">${stockCountEscapeHtml(detail?.warehouseName || '')}</div>

                <div class="col-6">Số dòng</div>
                <div class="col-6 fw-bold text-end">${stockCountFormatNumber(detail?.lines?.length || 0)}</div>

                <div class="col-6">Sản phẩm lệch</div>
                <div class="col-6 fw-bold text-end">${stockCountFormatNumber(diffItems.length)}</div>
            </div>
        `,
        okText: 'Duyệt xác nhận',
        okClass: 'btn-success'
    });

    if (!ok) return;

    const response = await fetch(`/admin/api/stock-counts/${stockCountDocumentId}/confirm`, {
        method: 'POST'
    });

    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        scToast('danger', 'Duyệt thất bại', api.data?.message || 'Không thể xác nhận phiếu.');
        return;
    }

    scToast('success', 'Đã xác nhận', api.data?.message || 'Phiếu kiểm kê đã được xác nhận.');
    await loadStockCountDetail();
}

async function rejectStockCountDocument() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    if (!stockCountDocumentId) return;

    const detail = stockCountUiState.currentDetail;

    const ok = await scConfirm({
        title: 'Từ chối phiếu kiểm kê?',
        subtitle: detail?.documentNo || '',
        message: 'Phiếu sẽ quay về trạng thái bị từ chối để người lập có thể chỉnh sửa lại.',
        okText: 'Từ chối phiếu',
        okClass: 'btn-danger'
    });

    if (!ok) return;

    const response = await fetch(`/admin/api/stock-counts/${stockCountDocumentId}/reject`, {
        method: 'POST'
    });

    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        scToast('danger', 'Từ chối thất bại', api.data?.message || 'Không thể từ chối phiếu.');
        return;
    }

    scToast('success', 'Đã từ chối phiếu', api.data?.message || 'Phiếu đã chuyển sang trạng thái bị từ chối.');
    await loadStockCountDetail();
}

async function refreshStockCountSystemQty() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    if (!stockCountDocumentId) return;

    const detail = stockCountUiState.currentDetail;

    const ok = await scConfirm({
        title: 'Làm mới tồn hệ thống?',
        subtitle: detail?.documentNo || '',
        message: 'Hệ thống sẽ cập nhật lại tồn tham chiếu cho toàn bộ dòng kiểm kê và tính lại chênh lệch.',
        okText: 'Làm mới tồn',
        okClass: 'btn-info'
    });

    if (!ok) return;

    const response = await fetch(`/admin/api/stock-counts/${stockCountDocumentId}/refresh-system-qty`, {
        method: 'POST'
    });

    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        scToast('danger', 'Làm mới thất bại', api.data?.message || 'Không thể làm mới tồn hệ thống.');
        return;
    }

    scToast('success', 'Đã làm mới tồn', api.data?.message || 'Tồn hệ thống đã được cập nhật.');
    await loadStockCountDetail();
}