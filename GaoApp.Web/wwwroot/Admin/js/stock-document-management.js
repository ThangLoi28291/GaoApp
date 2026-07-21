let createReceiptModalInstance = null;
let editLineModalInstance = null;
let submitApprovalModalInstance = null;
let approveModalInstance = null;
let rejectModalInstance = null;
let quickAddProductModalInstance = null;

let supplierLookupTimer = null;
let editHeaderSaveTimer = null;
let mapInputInvoiceLineModalInstance = null;
let cachedInputInvoices = [];
let selectedInputInvoiceDetailId = null;
let quickEditLineModalInstance = null;
let deleteLineModalInstance = null;
let returnToEditModalInstance = null;
let cachedReceiptFormOptions = null;

/* =========================================================
 * BOOTSTRAP
 * ========================================================= */

document.addEventListener('DOMContentLoaded', function () {
    if (!window.stockDocumentPage) return;

    initModalInstances();

    if (window.stockDocumentPage.mode === 'index') {
        loadReceiptList();
        bindOpenCreateReceiptModal();
        bindCreateReceiptModal();
        bindSupplierLookup();
        bindCreateReceiptEnterSubmit();
    }

    if (window.stockDocumentPage.mode === 'edit') {
        bindOpenApprovalModals();
        bindSubmitApproval();
        bindApprove();
        bindReject();
        bindRevisionRequestActions();

        bindDeleteLine();
        bindOpenEditLineModal();
        bindSaveEditLine();
        bindEditUnitChange();

        bindUploadInputInvoiceXml();
        loadInputInvoicesForStockDocument();

        bindOpenMapInputInvoiceLineModal();
        bindSaveInputInvoiceLineMap();
        bindToggleAllInputInvoiceLines();
        syncToggleAllInputInvoiceLinesState();

        initEditSelect2Mode();

        if (sessionStorage.getItem('stockDocumentFocusQuickLookup') === '1') {
            sessionStorage.removeItem('stockDocumentFocusQuickLookup');

            setTimeout(function () {
                focusQuickLookup();
            }, 200);
        }
    }
});

/* =========================================================
 * INIT
 * ========================================================= */

function initModalInstances() {
    const createReceiptModal = document.getElementById('createReceiptModal');
    const editLineModal = document.getElementById('editLineModal');
    const submitApprovalModal = document.getElementById('submitApprovalModal');
    const approveModal = document.getElementById('approveModal');
    const rejectModal = document.getElementById('rejectModal');
    const mapInputInvoiceLineModal = document.getElementById('mapInputInvoiceLineModal');
    const quickAddProductModal = document.getElementById('quickAddProductModal');
    const quickEditLineModal = document.getElementById('quickEditLineModal');
    const deleteLineModal = document.getElementById('deleteLineModal');
    const returnToEditModal =
        document.getElementById('returnToEditModal');

    if (returnToEditModal) {
        returnToEditModalInstance =
            new bootstrap.Modal(returnToEditModal);
    }

    if (quickEditLineModal) quickEditLineModalInstance = new bootstrap.Modal(quickEditLineModal);
    if (deleteLineModal) deleteLineModalInstance = new bootstrap.Modal(deleteLineModal);

    if (createReceiptModal) createReceiptModalInstance = new bootstrap.Modal(createReceiptModal);
    if (editLineModal) editLineModalInstance = new bootstrap.Modal(editLineModal);
    if (submitApprovalModal) submitApprovalModalInstance = new bootstrap.Modal(submitApprovalModal);
    if (approveModal) approveModalInstance = new bootstrap.Modal(approveModal);
    if (rejectModal) rejectModalInstance = new bootstrap.Modal(rejectModal);
    if (mapInputInvoiceLineModal) mapInputInvoiceLineModalInstance = new bootstrap.Modal(mapInputInvoiceLineModal);
    if (quickAddProductModal) quickAddProductModalInstance = new bootstrap.Modal(quickAddProductModal);
}

/* =========================================================
 * COMMON
 * ========================================================= */

async function readApiResponse(response) {
    const text = await response.text();

    try {
        return {
            ok: response.ok,
            status: response.status,
            data: JSON.parse(text)
        };
    } catch {
        return {
            ok: response.ok,
            status: response.status,
            data: {
                message: text
            }
        };
    }
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function formatDate(value) {
    if (!value) return '';

    const d = new Date(value);
    if (isNaN(d.getTime())) return '';

    return d.toLocaleString('vi-VN');
}

function formatNumber(value) {
    return new Intl.NumberFormat('vi-VN').format(value ?? 0);
}

function setText(id, value) {
    const el = document.getElementById(id);
    if (el) el.textContent = value;
}

function toNumber(value, fallback = 0) {
    const n = Number(value);
    return Number.isFinite(n) ? n : fallback;
}

function focusQuickLookup() {
    const el = $('#quickLookupInput');

    if (!el.length) return;
    if (!window.jQuery || !$.fn.select2) return;

    setTimeout(function () {
        el.select2('open');
    }, 80);
}

function renderStatusBadge(status) {
    switch (status) {
        case 1:
            return `<span class="badge bg-label-primary">Nháp</span>`;
        case 2:
            return `<span class="badge bg-label-warning">Chờ duyệt</span>`;
        case 3:
            return `<span class="badge bg-label-success">Đã duyệt</span>`;
        case 4:
            return `<span class="badge bg-label-danger">Bị từ chối</span>`;
        case 5:
            return `<span class="badge bg-label-secondary">Đã hủy</span>`;
        default:
            return `<span class="badge bg-label-dark">Không rõ</span>`;
    }
}

function statusToText(status) {
    switch (status) {
        case 1: return 'Draft Nháp Đang làm';
        case 2: return 'PendingApproval Chờ duyệt';
        case 3: return 'Confirmed Approved Đã duyệt';
        case 4: return 'Rejected Bị từ chối';
        case 5: return 'Cancelled Đã hủy';
        default: return 'Unknown Không rõ';
    }
}

/* =========================================================
 * INDEX - STOCK DOCUMENT LIST
 * ========================================================= */

let stockDocumentIndexState = {
    allItems: [],
    filteredItems: [],
    page: 1,
    pageSize: 20,
    keyword: '',
    status: 'active'
};

async function loadReceiptList() {
    const body = document.getElementById('sdReceiptTableBody');
    if (!body) return;

    body.innerHTML = renderIndexLoadingRow();

    try {
        const response = await fetch('/admin/api/stock-documents/receipts', {
            method: 'GET',
            cache: 'no-store'
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            body.innerHTML = renderIndexErrorRow(api.data?.message || 'Không tải được danh sách phiếu nhập.');
            return;
        }

        stockDocumentIndexState.allItems = api.data || [];
        stockDocumentIndexState.page = 1;

        bindStockDocumentIndexFilters();
        applyStockDocumentIndexFilter();
    } catch (error) {
        console.error(error);
        body.innerHTML = renderIndexErrorRow('Có lỗi khi tải danh sách phiếu nhập.');
    }
}

function bindStockDocumentIndexFilters() {
    const keywordInput = document.getElementById('sdSearchKeyword');
    const statusSelect = document.getElementById('sdStatusFilter');
    const pageSizeSelect = document.getElementById('sdPageSize');
    const resetBtn = document.getElementById('sdBtnResetFilter');
    const prevBtn = document.getElementById('sdBtnPrevPage');
    const nextBtn = document.getElementById('sdBtnNextPage');

    if (keywordInput && !keywordInput.dataset.bound) {
        keywordInput.dataset.bound = '1';

        keywordInput.addEventListener('input', function () {
            stockDocumentIndexState.keyword = keywordInput.value.trim().toLowerCase();
            stockDocumentIndexState.page = 1;
            applyStockDocumentIndexFilter();
        });
    }

    if (statusSelect && !statusSelect.dataset.bound) {
        statusSelect.dataset.bound = '1';

        statusSelect.addEventListener('change', function () {
            stockDocumentIndexState.status = statusSelect.value || 'all';
            stockDocumentIndexState.page = 1;
            applyStockDocumentIndexFilter();
        });
    }

    if (pageSizeSelect && !pageSizeSelect.dataset.bound) {
        pageSizeSelect.dataset.bound = '1';

        pageSizeSelect.addEventListener('change', function () {
            stockDocumentIndexState.pageSize = Number(pageSizeSelect.value || 20);
            stockDocumentIndexState.page = 1;
            renderStockDocumentIndex();
        });
    }

    if (resetBtn && !resetBtn.dataset.bound) {
        resetBtn.dataset.bound = '1';

        resetBtn.addEventListener('click', function () {
            stockDocumentIndexState.keyword = '';
            stockDocumentIndexState.status = 'all';
            stockDocumentIndexState.page = 1;

            if (keywordInput) keywordInput.value = '';
            if (statusSelect) statusSelect.value = 'all';

            applyStockDocumentIndexFilter();
        });
    }

    if (prevBtn && !prevBtn.dataset.bound) {
        prevBtn.dataset.bound = '1';

        prevBtn.addEventListener('click', function () {
            if (stockDocumentIndexState.page <= 1) return;

            stockDocumentIndexState.page--;
            renderStockDocumentIndex();
        });
    }

    if (nextBtn && !nextBtn.dataset.bound) {
        nextBtn.dataset.bound = '1';

        nextBtn.addEventListener('click', function () {
            const totalPages = getStockDocumentTotalPages();
            if (stockDocumentIndexState.page >= totalPages) return;

            stockDocumentIndexState.page++;
            renderStockDocumentIndex();
        });
    }
}

function applyStockDocumentIndexFilter() {
    const keyword = stockDocumentIndexState.keyword;
    const status = stockDocumentIndexState.status;

    stockDocumentIndexState.filteredItems = stockDocumentIndexState.allItems.filter(x => {
        const matchKeyword = !keyword || [
            x.documentNo,
            x.documentTitle,
            x.legalEntityName,
            x.warehouseName,
            x.revisionRequestNote,
            x.hasRevisionRequest ? 'yêu cầu đề nghị sửa' : '',
            statusToText(x.status)
        ].some(v => String(v || '').toLowerCase().includes(keyword));

        const matchStatus = status === 'all' || matchStockDocumentStatusGroup(x.status, status);

        return matchKeyword && matchStatus;
    });

    updateStockDocumentIndexStats();
    renderStockDocumentIndex();
}

function renderStockDocumentIndex() {
    const body = document.getElementById('sdReceiptTableBody');
    if (!body) return;

    const items = stockDocumentIndexState.filteredItems || [];
    const totalItems = items.length;
    const totalPages = getStockDocumentTotalPages();

    if (stockDocumentIndexState.page > totalPages) {
        stockDocumentIndexState.page = totalPages;
    }

    const startIndex = (stockDocumentIndexState.page - 1) * stockDocumentIndexState.pageSize;
    const pageItems = items.slice(startIndex, startIndex + stockDocumentIndexState.pageSize);

    body.innerHTML = renderStockDocumentIndexRows(pageItems);
    bindStockDocumentIndexRowDoubleClick(pageItems);

    updateStockDocumentPaginationInfo(totalItems, totalPages);
}
let stockDocumentInfoModalInstance = null;
let stockDocumentIndexCurrentPageItems = [];

function bindStockDocumentIndexRowDoubleClick(pageItems) {
    stockDocumentIndexCurrentPageItems = pageItems || [];

    document.querySelectorAll('.sd-index-row').forEach(row => {
        row.ondblclick = function () {
            const id = Number(this.dataset.id || 0);
            const item = stockDocumentIndexCurrentPageItems.find(x => Number(x.id) === id);

            if (!item) return;

            openStockDocumentInfoModal(item);
        };
    });
}

function openStockDocumentInfoModal(item) {
    const modalEl = document.getElementById('stockDocumentInfoModal');

    if (!stockDocumentInfoModalInstance && modalEl) {
        stockDocumentInfoModalInstance = new bootstrap.Modal(modalEl);
    }

    setText('sdInfoTitle', item.documentTitle || item.documentNo || 'Chi tiết phiếu');
    setText('sdInfoSubTitle', item.documentNo ? `Mã phiếu: ${item.documentNo}` : '-');
    setText('sdInfoStatus', statusToDisplayText(item.status));
    setText(
        'sdInfoWarehouse',
        [item.legalEntityName, item.warehouseName].filter(Boolean).join(' · ') || '-');
    setText('sdInfoSupplier', item.supplierName || '-');

    setText('sdInfoDocumentDate', formatDate(item.documentDate) || '-');
    setText('sdInfoCreatedAt', formatDate(item.createdAtUtc || item.documentDate) || '-');
    setText('sdInfoUpdatedAt', formatDate(item.updatedAtUtc) || '-');
    setText('sdInfoSubmittedAt', formatDate(item.submittedAtUtc) || '-');

    setText('sdInfoTotalLines', formatNumber(item.totalLines || 0));
    setText('sdInfoTotalProducts', formatNumber(item.totalProductTypes || 0));
    setText('sdInfoTotalAmount', formatNumber(item.totalAmount || 0));

    const detailLink = document.getElementById('sdInfoOpenDetail');
    if (detailLink) {
        detailLink.href = `/admin/stock-documents/${item.id}`;
    }

    stockDocumentInfoModalInstance?.show();
}

function statusToDisplayText(status) {
    switch (status) {
        case 1: return 'Nháp';
        case 2: return 'Chờ duyệt';
        case 3: return 'Đã duyệt';
        case 4: return 'Bị từ chối';
        case 5: return 'Đã hủy';
        default: return 'Không rõ';
    }
}

function renderStockDocumentIndexRows(items) {
    if (!items || items.length === 0) {
        return `
            <tr>
                <td colspan="9" class="text-center text-muted py-4">
                    Không có dữ liệu phù hợp
                </td>
            </tr>`;
    }

    return items.map(x => {
        const hasRevisionRequest = x.hasRevisionRequest === true;

        const actionText = hasRevisionRequest
            ? 'Xử lý sửa'
            : (x.status === 2 ? 'Duyệt' : (x.status === 3 ? 'Xem' : 'Mở'));

        const actionClass = hasRevisionRequest
            ? 'btn-danger'
            : (x.status === 2
                ? 'btn-warning'
                : (x.status === 3 ? 'btn-outline-primary' : 'btn-primary'));

        const title = x.documentTitle || 'Chưa đặt tên phiếu';

        return `
            <tr class="sd-index-row"
                data-id="${x.id}"
                title="Bấm đúp để xem nhanh">
             <td>
    <div class="sd-index-title ${x.documentTitle ? '' : 'sd-index-title-muted'}">
    ${escapeHtml(x.documentTitle || 'Chưa đặt tên phiếu')}

    ${x.hasRevisionRequest ? `
        <span class="badge bg-label-danger ms-2"
              title="${escapeHtml(x.revisionRequestNote || 'Có yêu cầu sửa')}">
            Yêu cầu sửa
        </span>
    ` : ''}
</div>

    <div class="sd-index-doc-no">
        ${escapeHtml(x.documentNo || '')}
    </div>

    <div class="sd-index-id">
        #${x.id}
    </div>
</td>

              <td>${renderIndexDate(x.createdAtUtc || x.documentDate)}</td>
<td>${renderIndexDate(x.updatedAtUtc)}</td>
<td>${renderIndexDate(x.submittedAtUtc)}</td>

                <td>
                    <div class="fw-semibold">${escapeHtml(x.warehouseName || '-')}</div>
                    <div class="text-muted small">${escapeHtml(x.legalEntityName || '-')}</div>
                </td>

                <td>${renderStatusBadge(x.status)}</td>

                <td class="text-end fw-semibold">${formatNumber(x.totalProductTypes || 0)}</td>

              <td class="text-end sd-index-amount">${formatNumber(x.totalAmount || 0)}</td>

                <td class="sd-action-cell">
                    <a class="btn btn-sm ${actionClass}" href="/admin/stock-documents/${x.id}">
                        ${actionText}
                    </a>
                </td>
            </tr>`;
    }).join('');
}
function bindApproveKeyboard() {
    const modal = document.getElementById('approveModal');
    if (!modal || modal.dataset.keyboardBound === '1') return;

    modal.dataset.keyboardBound = '1';

    modal.addEventListener('keydown', function (e) {
        if (e.ctrlKey && e.key === 'Enter') {
            e.preventDefault();
            document.getElementById('btnApprove')?.click();
        }
    });
}
function updateStockDocumentIndexStats() {
    const all = stockDocumentIndexState.allItems || [];

    setText('sdCountAll', formatNumber(all.length));
    setText('sdCountWorking', formatNumber(all.filter(x => x.status === 1 || x.status === 4).length));
    setText('sdCountPending', formatNumber(all.filter(x => x.status === 2).length));
    setText('sdCountConfirmed', formatNumber(all.filter(x => x.status === 3).length));
}

function updateStockDocumentPaginationInfo(totalItems, totalPages) {
    const info = document.getElementById('sdPaginationInfo');
    const pageText = document.getElementById('sdCurrentPageText');
    const prevBtn = document.getElementById('sdBtnPrevPage');
    const nextBtn = document.getElementById('sdBtnNextPage');

    const page = stockDocumentIndexState.page;
    const pageSize = stockDocumentIndexState.pageSize;

    const from = totalItems === 0 ? 0 : ((page - 1) * pageSize) + 1;
    const to = Math.min(page * pageSize, totalItems);

    if (info) info.textContent = `Hiển thị ${from} - ${to} / ${totalItems} phiếu`;
    if (pageText) pageText.textContent = `${page} / ${totalPages}`;
    if (prevBtn) prevBtn.disabled = page <= 1;
    if (nextBtn) nextBtn.disabled = page >= totalPages;
}

function getStockDocumentTotalPages() {
    const totalItems = stockDocumentIndexState.filteredItems.length;
    const pageSize = stockDocumentIndexState.pageSize || 20;

    return Math.max(1, Math.ceil(totalItems / pageSize));
}

function matchStockDocumentStatusGroup(status, group) {
    if (group === 'active') return status === 1 || status === 2;
    if (group === 'working') return status === 1;
    if (group === 'pending') return status === 2;
    if (group === 'confirmed') return status === 3;
    if (group === 'rejected') return status === 4;

    return true;
}

function renderIndexLoadingRow() {
    return `
        <tr>
            <td colspan="9" class="text-center text-muted py-4">
                Đang tải dữ liệu...
            </td>
        </tr>`;
}

function renderIndexErrorRow(message) {
    return `
        <tr>
            <td colspan="9" class="text-center text-danger py-4">
                ${escapeHtml(message)}
            </td>
        </tr>`;
}

/* =========================================================
 * INDEX - CREATE RECEIPT MODAL
 * ========================================================= */

function bindOpenCreateReceiptModal() {
    const btn = document.getElementById('btnCreateReceipt');
    if (!btn) return;

    btn.onclick = async function () {
        resetCreateReceiptModal();
        await loadWarehouseOptionsForCreate();

        const modalEl = document.getElementById('createReceiptModal');

        if (modalEl && !modalEl.dataset.titleFocusBound) {
            modalEl.dataset.titleFocusBound = '1';

            modalEl.addEventListener('shown.bs.modal', function () {
                focusCreateDocumentTitle();
            });
        }

        if (createReceiptModalInstance) {
            createReceiptModalInstance.show();
        }

        setTimeout(focusCreateDocumentTitle, 350);
    };
}

function focusCreateDocumentTitle() {
    const titleInput = document.getElementById('createDocumentTitle');

    if (!titleInput) return;

    titleInput.removeAttribute('readonly');
    titleInput.focus();
    titleInput.select();
}
function renderIndexDate(value) {
    if (!value) return '-';

    const d = new Date(value);
    if (isNaN(d.getTime())) return '-';

    return `
        <div class="sd-index-date">
            <span class="time">${d.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}</span>
            <span class="date">${d.toLocaleDateString('vi-VN')}</span>
        </div>`;
}

function resetCreateReceiptModal() {
    const legalEntity = document.getElementById('createLegalEntityId');
    const warehouse = document.getElementById('createWarehouseId');
    const supplierKeyword = document.getElementById('createSupplierKeyword');
    const supplierId = document.getElementById('createSupplierId');
    const note = document.getElementById('createNote');
    const directReason = document.getElementById('createDirectReceiptReason');
    const msg = document.getElementById('createReceiptMessage');
    const supplierList = document.getElementById('supplierLookupList');
    const documentDate = document.getElementById('createDocumentDate');
    const title = document.getElementById('createDocumentTitle');
    if (title) title.value = '';
    if (directReason) directReason.value = '';

    if (legalEntity) legalEntity.value = '';
    if (warehouse) warehouse.innerHTML = '<option value="">-- Chọn HKD trước --</option>';
    if (supplierKeyword) supplierKeyword.value = '';
    if (supplierId) supplierId.value = '';
    if (note) note.value = '';
    if (msg) msg.textContent = '';
    if (supplierList) supplierList.innerHTML = '';

    if (documentDate) {
        const now = new Date();
        now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
        documentDate.value = now.toISOString().slice(0, 16);
    }
}

async function loadWarehouseOptionsForCreate() {
    const legalEntitySelect = document.getElementById('createLegalEntityId');
    const select = document.getElementById('createWarehouseId');
    if (!legalEntitySelect || !select) return;

    legalEntitySelect.innerHTML = `<option value="">Đang tải HKD...</option>`;
    select.innerHTML = `<option value="">Đang tải kho...</option>`;

    try {
        const options = await loadReceiptFormOptions();
        const legalEntities = options.legalEntities || [];

        legalEntitySelect.innerHTML = `<option value="">-- Chọn HKD --</option>`;
        legalEntities.forEach(x => {
            const option = document.createElement('option');
            option.value = x.id;
            option.textContent = `${x.code} - ${x.name}`;
            legalEntitySelect.appendChild(option);
        });

        const defaultLegalEntityId = Number(options.defaultLegalEntityId || 0);
        if (defaultLegalEntityId) {
            legalEntitySelect.value = String(defaultLegalEntityId);
        } else if (legalEntities.length === 1) {
            legalEntitySelect.value = String(legalEntities[0].id);
        }

        renderReceiptWarehouseOptions(
            select,
            Number(legalEntitySelect.value || 0),
            options);

        if (!legalEntitySelect.dataset.receiptChangeBound) {
            legalEntitySelect.dataset.receiptChangeBound = '1';
            legalEntitySelect.addEventListener('change', function () {
                renderReceiptWarehouseOptions(
                    select,
                    Number(legalEntitySelect.value || 0),
                    cachedReceiptFormOptions);
            });
        }
    } catch (error) {
        console.error(error);
        legalEntitySelect.innerHTML = `<option value="">Không tải được HKD</option>`;
        select.innerHTML = `<option value="">Không tải được kho</option>`;
    }
}

async function loadReceiptFormOptions() {
    if (cachedReceiptFormOptions) return cachedReceiptFormOptions;

    const url = window.stockDocumentPage?.mode === 'edit'
        ? '/admin/stock-documents/receipt-form-options'
        : '/admin/api/stock-documents/receipt-form-options';
    const response = await fetch(url, {
        method: 'GET',
        cache: 'no-store'
    });
    const api = await readApiResponse(response);

    if (!api.ok) {
        throw new Error(api.data?.message || 'Không tải được cấu hình HKD nhập hàng.');
    }

    cachedReceiptFormOptions = api.data || { legalEntities: [], warehouses: [] };
    return cachedReceiptFormOptions;
}

function renderReceiptWarehouseOptions(select, legalEntityId, options, selectedWarehouseId = 0) {
    if (!select) return;

    const legalEntities = options?.legalEntities || [];
    const warehouses = (options?.warehouses || [])
        .filter(x => Number(x.legalEntityId) === Number(legalEntityId));
    const legalEntity = legalEntities.find(x => Number(x.id) === Number(legalEntityId));

    select.innerHTML = `<option value="">-- Chọn kho --</option>`;
    warehouses.forEach(x => {
        const option = document.createElement('option');
        option.value = x.id;
        option.textContent = `${x.code} - ${x.name}`;
        select.appendChild(option);
    });

    const preferredWarehouseId = Number(selectedWarehouseId || legalEntity?.defaultWarehouseId || 0);
    if (preferredWarehouseId && warehouses.some(x => Number(x.id) === preferredWarehouseId)) {
        select.value = String(preferredWarehouseId);
    } else if (warehouses.length === 1) {
        select.value = String(warehouses[0].id);
    }
}

function bindSupplierLookup() {
    const input = document.getElementById('createSupplierKeyword');
    const list = document.getElementById('supplierLookupList');

    if (!input || !list) return;

    input.addEventListener('input', function () {
        const keyword = input.value.trim();

        const hiddenId = document.getElementById('createSupplierId');
        if (hiddenId) hiddenId.value = '';

        if (supplierLookupTimer) clearTimeout(supplierLookupTimer);

        if (!keyword) {
            list.innerHTML = '';
            return;
        }

        supplierLookupTimer = setTimeout(async function () {
            try {
                const response = await fetch(`/admin/api/suppliers/select2?term=${encodeURIComponent(keyword)}`, {
                    method: 'GET',
                    cache: 'no-store'
                });

                const api = await readApiResponse(response);

                if (!api.ok) {
                    list.innerHTML = `
                        <div class="list-group-item text-danger small">
                            Không tải được nhà cung cấp
                        </div>`;
                    return;
                }

                const items = api.data?.results || [];

                if (!items.length) {
                    list.innerHTML = `
                        <div class="list-group-item text-muted small">
                            Không tìm thấy nhà cung cấp
                        </div>`;
                    return;
                }

                list.innerHTML = items.map(x => `
                    <button type="button"
                            class="list-group-item list-group-item-action js-select-supplier"
                            data-id="${x.id}"
                            data-text="${escapeHtml(x.text)}">
                        ${escapeHtml(x.text)}
                    </button>
                `).join('');

                bindSelectSupplier();
            } catch (error) {
                console.error(error);
            }
        }, 250);
    });
}

function bindSelectSupplier() {
    document.querySelectorAll('.js-select-supplier').forEach(btn => {
        btn.onclick = function () {
            const id = this.dataset.id || '';
            const text = this.dataset.text || '';

            const supplierId = document.getElementById('createSupplierId');
            const supplierKeyword = document.getElementById('createSupplierKeyword');
            const list = document.getElementById('supplierLookupList');

            if (supplierId) supplierId.value = id;
            if (supplierKeyword) supplierKeyword.value = text;
            if (list) list.innerHTML = '';
        };
    });
}

function bindCreateReceiptEnterSubmit() {
    const modal = document.getElementById('createReceiptModal');
    if (!modal) return;

    modal.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter' || e.shiftKey) return;

        const tag = document.activeElement?.tagName?.toLowerCase();

        if (tag === 'textarea') return;

        e.preventDefault();
        document.getElementById('btnSubmitCreateReceipt')?.click();
    });
}

function bindCreateReceiptModal() {
    const btn = document.getElementById('btnSubmitCreateReceipt');
    if (!btn) return;

    btn.onclick = async function () {
        const legalEntityId = Number(document.getElementById('createLegalEntityId')?.value || 0);
        const warehouseId = Number(document.getElementById('createWarehouseId')?.value || 0);
        const supplierIdRaw = document.getElementById('createSupplierId')?.value || '';
        const supplierId = supplierIdRaw ? Number(supplierIdRaw) : null;
        const note = document.getElementById('createNote')?.value || '';
        const documentDateRaw = document.getElementById('createDocumentDate')?.value || '';
        const msg = document.getElementById('createReceiptMessage');
        const documentTitle = document.getElementById('createDocumentTitle')?.value || '';
        const directReceiptReason = document.getElementById('createDirectReceiptReason')?.value?.trim() || '';

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
            return;
        }

        try {
            btn.disabled = true;

            const response = await fetch('/admin/api/stock-documents/receipts', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    documentTitle: documentTitle,
                    legalEntityId: legalEntityId,
                    warehouseId: warehouseId,
                    supplierId: supplierId,
                    note: note,
                    hasVat: false,
                    directReceiptReason: directReceiptReason,
                    documentDate: documentDateRaw ? new Date(documentDateRaw).toISOString() : null
                })
            });

            const api = await readApiResponse(response);

            if (!api.ok) {
                if (msg) msg.textContent = api.data?.message || 'Tạo phiếu thất bại.';
                return;
            }

            if (createReceiptModalInstance) {
                createReceiptModalInstance.hide();
            }

            const id = api.data?.id || api.data?.data?.id;

            if (!id) {
                window.location.reload();
                return;
            }

            window.location.href = `/admin/stock-documents/${id}`;
        } catch (error) {
            console.error(error);

            if (msg) {
                msg.textContent = 'Có lỗi khi tạo phiếu.';
            }
        } finally {
            btn.disabled = false;
        }
    };
}

/* =========================================================
 * EDIT - DETAIL / PARTIAL REFRESH
 * ========================================================= */

async function loadStockDocumentDetail(documentId) {
    const response = await fetch(`/admin/api/stock-documents/${documentId}`);
    const api = await readApiResponse(response);

    if (!api.ok) {
        throw new Error(api.data?.message || 'Không tải được chi tiết phiếu.');
    }

    return api.data;
}

function updateStockDocumentSummary(detail) {
    const totalEl = document.getElementById('stockDocumentTotalAmount');
    const totalInputEl = document.getElementById('txtTotalAmount');

    if (totalEl) totalEl.textContent = formatNumber(detail.totalAmount || 0);
    if (totalInputEl) totalInputEl.value = formatNumber(detail.totalAmount || 0);
}

async function refreshStockDocumentDetailUI() {
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    if (!documentId) return;

    try {
        const detail = await loadStockDocumentDetail(documentId);
        updateStockDocumentSummary(detail);

        const response = await fetch(`/admin/stock-documents/lines-table?id=${documentId}`, {
            method: 'GET',
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            },
            cache: 'no-store'
        });

        if (!response.ok) {
            throw new Error('Không tải được partial danh sách dòng.');
        }

        const html = await response.text();

        const container = document.getElementById('stockDocumentLinesContainer');
        if (container) container.innerHTML = html;

        bindDeleteLine();
        bindOpenEditLineModal();
        bindOpenMapInputInvoiceLineModal();
        bindInlineLineQuantityChange();
        bindInlineLineUnitCostChange();
        syncToggleAllInputInvoiceLinesState();
    } catch (error) {
        console.error(error);
        alert('Không tải lại được danh sách dòng nhập.');
    }
}

/* =========================================================
 * EDIT - APPROVAL MODALS
 * ========================================================= */

function bindOpenApprovalModals() {
    const btnOpenSubmit = document.getElementById('btnOpenSubmitApprovalModal');
    const btnOpenApprove = document.getElementById('btnOpenApproveModal');
    const btnOpenReject = document.getElementById('btnOpenRejectModal');

    if (btnOpenSubmit && submitApprovalModalInstance) {
        btnOpenSubmit.onclick = function () {
            const note = document.getElementById('submitApprovalNote');
            const msg = document.getElementById('submitApprovalMessage');
            const modalEl = document.getElementById('submitApprovalModal');

            if (note) note.value = '';
            if (msg) msg.textContent = '';

            if (modalEl && !modalEl.dataset.focusBound) {
                modalEl.dataset.focusBound = '1';

                modalEl.addEventListener('shown.bs.modal', function () {
                    const noteInput = document.getElementById('submitApprovalNote');

                    if (noteInput) {
                        noteInput.focus();
                        noteInput.select();
                    }
                });
            }

            submitApprovalModalInstance.show();

            setTimeout(function () {
                const noteInput = document.getElementById('submitApprovalNote');

                if (noteInput) {
                    noteInput.focus();
                    noteInput.select();
                }
            }, 250);
        };
    }

    if (btnOpenApprove && approveModalInstance) {
        btnOpenApprove.onclick = function () {
            const note = document.getElementById('approveNote');
            const msg = document.getElementById('approveMessage');
            const modalEl = document.getElementById('approveModal');

            if (note) note.value = '';
            if (msg) msg.textContent = '';

            if (modalEl && !modalEl.dataset.focusBound) {
                modalEl.dataset.focusBound = '1';

                modalEl.addEventListener('shown.bs.modal', function () {
                    const noteInput = document.getElementById('approveNote');

                    if (noteInput) {
                        noteInput.focus();
                        noteInput.select();
                    }
                });
            }

            approveModalInstance.show();

            setTimeout(function () {
                const noteInput = document.getElementById('approveNote');

                if (noteInput) {
                    noteInput.focus();
                    noteInput.select();
                }
            }, 250);
        };
    }

    if (btnOpenReject && rejectModalInstance) {
        btnOpenReject.onclick = function () {
            const note = document.getElementById('rejectNote');
            const msg = document.getElementById('rejectMessage');

            if (note) note.value = '';
            if (msg) msg.textContent = '';

            rejectModalInstance.show();

            setTimeout(function () {
                note?.focus();
            }, 250);
        };
    }
}
function bindSubmitApprovalKeyboard() {
    const modal = document.getElementById('submitApprovalModal');
    if (!modal || modal.dataset.keyboardBound === '1') return;

    modal.dataset.keyboardBound = '1';

    modal.addEventListener('keydown', function (e) {
        if (e.ctrlKey && e.key === 'Enter') {
            e.preventDefault();
            document.getElementById('btnSubmitApproval')?.click();
        }
    });
}

function bindSubmitApproval() {
    const btn = document.getElementById('btnSubmitApproval');
    if (!btn) return;

    btn.onclick = async function () {
        const note = document.getElementById('submitApprovalNote')?.value || '';
        const msg = document.getElementById('submitApprovalMessage');
        const documentId = window.stockDocumentPage.documentId;

        const response = await fetch(`/admin/api/stock-documents/${documentId}/submit-approval`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ note: note, rowVersion: window.stockDocumentPage.rowVersion })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Gửi duyệt thất bại.';
            return;
        }

        if (submitApprovalModalInstance) submitApprovalModalInstance.hide();

        showStockDocumentToast(
            'success',
            'Đã gửi duyệt',
            api.data?.message || 'Phiếu nhập đã chuyển sang trạng thái chờ duyệt.',
            api.data?.redirectUrl || '/admin/stock-documents'
        );
    };
}

function bindApprove() {
    const btn = document.getElementById('btnApprove');
    if (!btn) return;

    btn.onclick = async function () {
        if (window.stockDocumentPage?.canApproveCommercial === true &&
            typeof window.GaoAppPurchaseReceiptApproval?.submit === 'function') {
            await window.GaoAppPurchaseReceiptApproval.submit();
            return;
        }

        const note = document.getElementById('approveNote')?.value || '';
        const msg = document.getElementById('approveMessage');
        const documentId = window.stockDocumentPage.documentId;

        const response = await fetch(`/admin/api/stock-documents/${documentId}/approve`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ note: note, rowVersion: window.stockDocumentPage.rowVersion })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Duyệt phiếu thất bại.';
            return;
        }

        if (approveModalInstance) approveModalInstance.hide();

        showStockDocumentToast(
            'success',
            'Duyệt phiếu thành công',
            api.data?.message || 'Phiếu nhập kho đã được duyệt và ghi nhận tồn kho.',
            api.data?.redirectUrl || '/admin/stock-documents'
        );
    };
}

function bindReject() {
    const btn = document.getElementById('btnReject');
    if (!btn) return;

    btn.onclick = async function () {
        const note = document.getElementById('rejectNote')?.value || '';
        const msg = document.getElementById('rejectMessage');
        const documentId = window.stockDocumentPage.documentId;

        const response = await fetch(`/admin/api/stock-documents/${documentId}/reject`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ note: note, rowVersion: window.stockDocumentPage.rowVersion })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Từ chối duyệt thất bại.';
            return;
        }

        if (rejectModalInstance) rejectModalInstance.hide();

        alert(api.data?.message || 'Đã từ chối duyệt.');
        window.location.href = api.data?.redirectUrl || '/admin/stock-documents';
    };
}

/* =========================================================
 * EDIT - LINE ACTIONS
 * ========================================================= */

function bindDeleteLine() {
    const canEditLines = document.getElementById('CanEditLines')?.value === 'true';
    if (!canEditLines) return;

    document.querySelectorAll('.btn-delete-line').forEach(btn => {
        btn.onclick = function () {
            const lineId = this.dataset.lineId || '';
            const productName = this.dataset.productName || '-';

            document.getElementById('deleteLineId').value = lineId;
            setText('deleteLineProductName', productName);

            deleteLineModalInstance?.show();
        };
    });
}
function bindConfirmDeleteLine() {
    const btn = document.getElementById('btnConfirmDeleteLine');
    if (!btn || btn.dataset.bound) return;

    btn.dataset.bound = '1';

    btn.onclick = async function () {
        const lineId = document.getElementById('deleteLineId')?.value || '';
        const documentId = window.stockDocumentPage.documentId;

        if (!lineId) return;

        try {
            btn.disabled = true;
            btn.textContent = 'Đang xóa...';

            const response = await fetch(`/admin/api/stock-documents/${documentId}/lines/${lineId}`, {
                method: 'DELETE'
            });

            const api = await readApiResponse(response);

            if (!api.ok) {
                alert(api.data?.message || 'Xóa dòng thất bại.');
                return;
            }

            deleteLineModalInstance?.hide();

            await refreshStockDocumentDetailUI();
            focusQuickLookup();
        } finally {
            btn.disabled = false;
            btn.textContent = 'Xóa';
        }
    };
}
function focusAndSelectQuickEditQty() {
    const qtyInput = document.getElementById('quickEditQty');
    if (!qtyInput) return;

    qtyInput.focus();

    // Delay nhỏ để browser focus xong mới select toàn bộ.
    setTimeout(function () {
        qtyInput.select();
    }, 30);
}
function bindOpenEditLineModal() {
    const canEditLines = document.getElementById('CanEditLines')?.value === 'true';
    if (!canEditLines) return;

    document.querySelectorAll('.btn-edit-line').forEach(btn => {
        btn.onclick = function () {
            const lineId = this.dataset.lineId || '';
            const productName = this.dataset.productName || '-';
            const imageUrl = this.dataset.imageUrl || '';
            const unitId = this.dataset.unitId || '';
            const unitName = this.dataset.unitName || '-';
            const factor = parseDecimalInput(this.dataset.factor || '1');
            const quantity = parseDecimalInput(this.dataset.quantity || '1');
            const unitCost = parseDecimalInput(this.dataset.unitCost || '0');
            const note = this.dataset.note || '';

            // Chỉ lưu thông tin của đúng dòng đang sửa.
            document.getElementById('quickEditLineId').value = lineId;
            document.getElementById('quickEditUnitId').value = unitId;
            document.getElementById('quickEditUnitCost').value = unitCost;
            document.getElementById('quickEditNote').value = note;

            setText('quickEditProductName', productName);
            setText('quickEditUnitName', unitName);
            setText('quickEditFactor', formatDecimalForInput(factor));

            const unitCostInput = document.getElementById('quickEditUnitCostInput');
            if (unitCostInput) {
                unitCostInput.value = formatDecimalForInput(unitCost);
            }

            const qtyInput = document.getElementById('quickEditQty');
            if (qtyInput) {
                qtyInput.value = formatDecimalForInput(quantity);
            }

            const img = document.getElementById('quickEditImage');
            const noImg = document.getElementById('quickEditNoImage');

            if (imageUrl) {
                img.src = imageUrl;
                img.classList.remove('d-none');
                noImg.classList.add('d-none');
            } else {
                img.src = '';
                img.classList.add('d-none');
                noImg.classList.remove('d-none');
            }

            const msg = document.getElementById('quickEditMessage');
            if (msg) msg.textContent = '';

            updateQuickEditPreview();

            const modalEl = document.getElementById('quickEditLineModal');

            if (modalEl && !modalEl.dataset.quickEditFocusBound) {
                modalEl.dataset.quickEditFocusBound = '1';

                modalEl.addEventListener('shown.bs.modal', function () {
                    focusAndSelectQuickEditQty();
                });
            }

            quickEditLineModalInstance?.show();

            setTimeout(focusAndSelectQuickEditQty, 300);
        };
    });
}

function bindQuickEditLineModal() {
    const qty = document.getElementById('quickEditQty');
    const unitCostInput = document.getElementById('quickEditUnitCostInput');
    const btn = document.getElementById('btnQuickSaveEditLine');

    if (qty && !qty.dataset.bound) {
        qty.dataset.bound = '1';

        qty.addEventListener('input', updateQuickEditPreview);
        qty.addEventListener('focus', function () {
            this.select();
        });

        qty.addEventListener('click', function () {
            this.select();
        });

        qty.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                btn?.click();
            }
        });
    }

    if (unitCostInput && !unitCostInput.dataset.bound) {
        unitCostInput.dataset.bound = '1';

        // Sửa giá là cập nhật ngay thành tiền dự kiến
        unitCostInput.addEventListener('input', updateQuickEditPreview);

        unitCostInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                btn?.click();
            }
        });
    }

    if (!btn || btn.dataset.bound) return;

    btn.dataset.bound = '1';

    btn.onclick = async function () {
        await saveQuickEditLine();
    };
}

function updateQuickEditPreview() {
    const qty = parseDecimalInput(document.getElementById('quickEditQty')?.value || '0');
    const factor = normalizeFactor(document.getElementById('quickEditFactor')?.textContent || '1');
    const unitCost = parseDecimalInput(document.getElementById('quickEditUnitCostInput')?.value || '0');

    const baseQty = qty * factor;

    // UnitCost là giá theo đơn vị nhập.
    const lineTotal = qty * unitCost;

    setText('quickEditBaseQty', formatDecimalForInput(baseQty));
    setText('quickEditLineTotal', formatNumber(lineTotal));
}

async function saveQuickEditLine() {
    const msg = document.getElementById('quickEditMessage');
    const btn = document.getElementById('btnQuickSaveEditLine');

    const documentId = window.stockDocumentPage.documentId;
    const lineId = document.getElementById('quickEditLineId')?.value || '';
    const unitId = Number(document.getElementById('quickEditUnitId')?.value || 0);
    const quantity = parseDecimalInput(document.getElementById('quickEditQty')?.value || '0');
    const note = document.getElementById('quickEditNote')?.value || '';

    if (msg) msg.textContent = '';

    if (!lineId || !unitId) {
        if (msg) msg.textContent = 'Không xác định được dòng nhập.';
        return;
    }

    if (quantity <= 0) {
        if (msg) msg.textContent = 'Số lượng phải lớn hơn 0.';
        return;
    }

    try {
        if (btn) {
            btn.disabled = true;
            btn.textContent = 'Đang lưu...';
        }

        // API này chỉ cập nhật đúng lineId đang sửa, không ảnh hưởng dòng khác.
        const response = await fetch(`/admin/api/stock-documents/${documentId}/lines/${lineId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                unitId: unitId,
                quantity: quantity,
                // Giá mua chỉ được xác nhận trong workbench duyệt thương mại.
                unitCost: null,
                note: note
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Cập nhật dòng thất bại.';
            return;
        }

        quickEditLineModalInstance?.hide();

        await refreshStockDocumentDetailUI();
        focusQuickLookup();
    } finally {
        if (btn) {
            btn.disabled = false;
            btn.textContent = 'Lưu thay đổi';
        }
    }
}

function bindSaveEditLine() {
    const canEditLines = document.getElementById('CanEditLines')?.value === 'true';
    if (!canEditLines) return;

    const btn = document.getElementById('btnSaveEditLine');
    if (!btn) return;

    btn.onclick = async function () {
        const lineId = document.getElementById('editLineId')?.value || '';
        const unitId = Number(document.getElementById('editLineUnitId')?.value || 0);
        const quantity = Number(document.getElementById('editLineQuantity')?.value || 0);
        const note = document.getElementById('editLineNote')?.value || '';
        const msg = document.getElementById('editLineMessage');
        const documentId = window.stockDocumentPage.documentId;

        if (!lineId) {
            if (msg) msg.textContent = 'Không xác định được line.';
            return;
        }

        if (!unitId) {
            if (msg) msg.textContent = 'Vui lòng chọn đơn vị.';
            return;
        }

        if (quantity <= 0) {
            if (msg) msg.textContent = 'Số lượng phải lớn hơn 0.';
            return;
        }

        const response = await fetch(`/admin/api/stock-documents/${documentId}/lines/${lineId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                unitId: unitId,
                quantity: quantity,
                unitCost: null,
                note: note
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Cập nhật dòng thất bại.';
            return;
        }

        if (editLineModalInstance) editLineModalInstance.hide();

        await refreshStockDocumentDetailUI();
        focusQuickLookup();
    };
}

/* =========================================================
 * EDIT - INLINE QUANTITY
 * ========================================================= */

function bindInlineLineQuantityChange() {
    document.querySelectorAll('.js-inline-line-qty').forEach(input => {
        if (input.dataset.bound === '1') return;

        input.dataset.bound = '1';
        let focusLookupAfterSave = false;

        input.setAttribute('step', '1');
        input.setAttribute('min', '1');
        input.setAttribute('inputmode', 'decimal');

        input.addEventListener('focus', function () {
            markEditingRow(this);
            this.select();
        });

        // Chặn phím mũi tên mặc định vì Chrome đôi khi nhảy theo 1 khi locale có dấu phẩy.
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowUp') {
                e.preventDefault();
                changeInlineQtyByStep(this, 0.001);
                return;
            }

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                changeInlineQtyByStep(this, -0.001);
                return;
            }

            if (e.key === 'Enter') {
                e.preventDefault();
                focusLookupAfterSave = true;
                this.blur();
            }
        });

        input.addEventListener('blur', async function () {
            await saveInlineLineQuantity(this);

            if (focusLookupAfterSave) {
                focusLookupAfterSave = false;
                focusQuickLookup();
            }
        });
    });
}
function bindInlineLineUnitCostChange() {
    document.querySelectorAll('.js-inline-line-cost').forEach(input => {
        if (input.dataset.bound === '1') return;

        input.dataset.bound = '1';
        let focusLookupAfterSave = false;

        input.addEventListener('focus', function () {
            markEditingRow(this);
            this.select();
        });

        input.addEventListener('input', function () {
            previewInlineLineTotal(this);
        });

        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                focusLookupAfterSave = true;
                this.blur();
            }
        });

        input.addEventListener('blur', async function () {
            await saveInlineLineUnitCost(this);

            if (focusLookupAfterSave) {
                focusLookupAfterSave = false;
                focusQuickLookup();
            }
        });
    });
}
function previewInlineLineTotal(input) {
    const row = input.closest('tr');
    if (!row) return;

    const quantity = parseDecimalInput(input.dataset.quantity || '0');
    const unitCost = parseDecimalInput(input.value || '0');
    const total = quantity * unitCost;

    const moneyCell = row.querySelector('.sd-money');
    if (moneyCell) {
        moneyCell.textContent = formatNumber(total);
    }
}

async function saveInlineLineUnitCost(input) {
    const documentId = window.stockDocumentPage.documentId;
    const lineId = input.dataset.lineId;
    const unitId = Number(input.dataset.unitId || 0);
    const quantity = parseDecimalInput(input.dataset.quantity || '0');
    const unitCost = parseDecimalInput(input.value || '0');
    const note = input.dataset.note || '';

    if (!lineId || !unitId) return;

    if (unitCost <= 0) {
        alert('Đơn giá nhập phải lớn hơn 0.');
        input.focus();
        input.select();
        return;
    }

    const response = await fetch(`/admin/api/stock-documents/${documentId}/lines/${lineId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            unitId: unitId,
            quantity: quantity,
            unitCost: unitCost,
            note: note
        })
    });

    const api = await readApiResponse(response);

    if (!api.ok) {
        alert(api.data?.message || 'Cập nhật đơn giá thất bại.');
        return;
    }

    await refreshStockDocumentDetailUI();
}
function changeInlineQtyByStep(input, delta) {
    const current = parseDecimalInput(input.value);
    const next = Math.max(0.001, current + delta);

    input.value = formatDecimalForInput(next);
    markEditingRow(input);
}

function parseDecimalInput(value) {
    if (value === null || value === undefined) return 0;

    return Number(String(value).replace(',', '.')) || 0;
}
function normalizeFactor(value) {
    const factor = parseDecimalInput(value || '1');
    return factor <= 0 ? 1 : factor;
}

function resolveDefaultPurchaseUnitCost(item) {
    if (!item) return 0;

    const defaultUnitCost = parseDecimalInput(item.defaultUnitCost || '0');
    if (defaultUnitCost > 1) {
        return defaultUnitCost;
    }

    const baseCostPrice = parseDecimalInput(item.costPrice || '0');
    const factor = normalizeFactor(item.factor || '1');

    // Chặn giá 1 do dữ liệu cũ/mặc định sai.
    if (baseCostPrice <= 1) {
        return 0;
    }

    return Math.round(baseCostPrice * factor);
}

function formatDecimalForInput(value) {
    return Number(value).toFixed(3).replace(/\.?0+$/, '');
}
function markEditingRow(input) {
    document.querySelectorAll('.sd-line-row-editing')
        .forEach(row => row.classList.remove('sd-line-row-editing'));

    input.closest('tr')?.classList.add('sd-line-row-editing');
}

async function saveInlineLineQuantity(input) {
    const documentId = window.stockDocumentPage.documentId;
    const lineId = input.dataset.lineId;
    const quantity = parseDecimalInput(input.value);
    const unitId = Number(input.dataset.unitId || 0);
    const note = input.dataset.note || '';

    if (!lineId || !unitId) return;

    if (quantity <= 0) {
        alert('Số lượng phải lớn hơn 0.');
        input.focus();
        input.select();
        return;
    }

    const row = input.closest('tr');
    row?.classList.add('sd-line-row-editing');

    const response = await fetch(`/admin/api/stock-documents/${documentId}/lines/${lineId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            unitId: unitId,
            quantity: quantity,
            unitCost: null,
            note: note
        })
    });

    const api = await readApiResponse(response);

    if (!api.ok) {
        alert(api.data?.message || 'Cập nhật số lượng thất bại.');
        return;
    }

    await refreshStockDocumentDetailUI();
}

/* =========================================================
 * EDIT - UNIT CHANGE
 * ========================================================= */

async function loadVariantUnitsToEditModal(variantId, selectedUnitId) {
    const select = document.getElementById('editLineUnitId');
    const info = document.getElementById('editLineUnitInfo');

    if (!select) return;

    select.innerHTML = `<option value="">Đang tải...</option>`;
    if (info) info.textContent = '';

    const response = await fetch(`/admin/api/stock-documents/product-variants/${variantId}/units`);
    const api = await readApiResponse(response);

    if (!api.ok) {
        select.innerHTML = `<option value="">Không tải được đơn vị</option>`;
        if (info) info.textContent = api.data?.message || 'Không tải được cấu hình đơn vị.';
        return;
    }

    const items = api.data || [];

    if (!items.length) {
        select.innerHTML = `<option value="">Không có đơn vị</option>`;
        if (info) info.textContent = 'Variant chưa có cấu hình quy đổi đơn vị.';
        return;
    }

    select.innerHTML = items.map(x => {
        const selected = Number(selectedUnitId) === Number(x.unitId) ? 'selected' : '';
        const flags = [
            x.isBaseUnit ? 'gốc' : '',
            x.isDefaultForSale ? 'mặc định' : ''
        ].filter(Boolean).join(', ');

        const suffix = flags ? ` - ${flags}` : '';

        return `<option value="${x.unitId}" data-factor="${x.factor}" ${selected}>${escapeHtml(x.unitName)} (x${x.factor})${suffix}</option>`;
    }).join('');

    updateEditUnitInfo();
}

function updateEditUnitInfo() {
    const select = document.getElementById('editLineUnitId');
    const info = document.getElementById('editLineUnitInfo');

    if (!select || !info) return;

    const option = select.options[select.selectedIndex];

    if (!option || !option.value) {
        info.textContent = '';
        return;
    }

    const factor = option.getAttribute('data-factor') || '1';
    info.textContent = `Hệ số quy đổi về đơn vị gốc: x${factor}`;
}

function bindEditUnitChange() {
    const select = document.getElementById('editLineUnitId');
    if (!select) return;

    select.onchange = function () {
        updateEditUnitInfo();
    };
}

/* =========================================================
 * EDIT - SELECT2 / AUTO SAVE
 * ========================================================= */

async function initEditSelect2Mode() {
    await initReceiptOwnershipForEdit();
    initSupplierSelect2ForEdit();
    initQuickLookupSelect2();
    bindQuickEditLineModal();
    bindConfirmDeleteLine();
    bindSubmitApprovalKeyboard();
    bindApproveKeyboard();
    bindEditHeaderAutoSave();
    bindPopupQuickAddLine();
    bindInlineLineQuantityChange();
    bindInlineLineUnitCostChange();
    bindPopupQuickQtySelectAll();
    applyEditReadonlyState();
    bindStockDocumentKeyboardFlow();

    focusQuickLookup();
}

function applyEditReadonlyState() {
    const canEditHeader = document.getElementById('CanEditHeader')?.value === 'true';
    const canEditLines = document.getElementById('CanEditLines')?.value === 'true';

    if (!canEditHeader) {
        $('#LegalEntityId').prop('disabled', true);
        $('#WarehouseId').prop('disabled', true);
        $('#SupplierId').prop('disabled', true);

        const documentNote = document.getElementById('DocumentNote');
        const approvalNote = document.getElementById('ApprovalNote');

        if (documentNote) documentNote.disabled = true;
        if (approvalNote) approvalNote.disabled = true;
    }

    if (!canEditLines) {
        $('#quickLookupInput').prop('disabled', true);
        const btnPopupQuickAddLine = document.getElementById('btnPopupQuickAddLine');
        if (btnPopupQuickAddLine) btnPopupQuickAddLine.disabled = true;
    }
}

async function initReceiptOwnershipForEdit() {
    const legalEntitySelect = document.getElementById('LegalEntityId');
    const warehouseSelect = document.getElementById('WarehouseId');
    if (!legalEntitySelect || !warehouseSelect) return;

    const canEditHeader = document.getElementById('CanEditHeader')?.value === 'true';
    if (!canEditHeader) {
        const legalId = document.getElementById('CurrentLegalEntityId')?.value || '';
        const legalText = document.getElementById('CurrentLegalEntityText')?.value || 'HKD hiện tại';
        const warehouseId = document.getElementById('CurrentWarehouseId')?.value || '';
        const warehouseText = document.getElementById('CurrentWarehouseText')?.value || 'Kho hiện tại';
        legalEntitySelect.innerHTML = `<option value="${escapeHtml(legalId)}" selected>${escapeHtml(legalText)}</option>`;
        warehouseSelect.innerHTML = `<option value="${escapeHtml(warehouseId)}" selected>${escapeHtml(warehouseText)}</option>`;
        return;
    }

    try {
        const options = await loadReceiptFormOptions();
        const legalEntities = options.legalEntities || [];
        const currentLegalEntityId = Number(
            document.getElementById('CurrentLegalEntityId')?.value || 0);
        const currentWarehouseId = Number(
            document.getElementById('CurrentWarehouseId')?.value || 0);

        legalEntitySelect.innerHTML = '<option value="">-- Chọn HKD --</option>';
        legalEntities.forEach(x => {
            const option = document.createElement('option');
            option.value = x.id;
            option.textContent = `${x.code} - ${x.name}`;
            legalEntitySelect.appendChild(option);
        });

        legalEntitySelect.value = String(currentLegalEntityId);
        renderReceiptWarehouseOptions(
            warehouseSelect,
            currentLegalEntityId,
            options,
            currentWarehouseId);

        legalEntitySelect.addEventListener('change', function () {
            renderReceiptWarehouseOptions(
                warehouseSelect,
                Number(legalEntitySelect.value || 0),
                cachedReceiptFormOptions);
        });
    } catch (error) {
        console.error(error);
        legalEntitySelect.innerHTML = '<option value="">Không tải được HKD</option>';
        warehouseSelect.innerHTML = '<option value="">Không tải được kho</option>';
    }
}

function initSupplierSelect2ForEdit() {
    const el = $('#SupplierId');
    if (!el.length) return;

    const currentId = $('#CurrentSupplierId').val();
    const currentText = $('#CurrentSupplierText').val();
    const canEditHeader = document.getElementById('CanEditHeader')?.value === 'true';

    if (!canEditHeader) {
        el.empty();
        if (currentId) {
            el.append(new Option(currentText || `Nhà cung cấp #${currentId}`, currentId, true, true));
        } else {
            el.append(new Option('Chưa chọn nhà cung cấp', '', true, true));
        }
        return;
    }

    el.select2({
        theme: 'bootstrap-5',
        width: '100%',
        placeholder: 'Chọn nhà cung cấp...',
        allowClear: true,
        ajax: {
            url: '/admin/api/suppliers/select2',
            dataType: 'json',
            delay: 250,
            data: function (params) {
                return { term: params.term || '' };
            },
            processResults: function (data) {
                return data;
            }
        },
        templateResult: formatSimpleSelect2Result,
        templateSelection: formatSimpleSelect2Selection,
        escapeMarkup: function (markup) {
            return markup;
        }
    });

    if (currentId && currentText) {
        const option = new Option(currentText, currentId, true, true);
        el.append(option).trigger('change');
    }
}

function initQuickLookupSelect2() {
    const el = $('#quickLookupInput');
    if (!el.length) return;

    el.select2({
        theme: 'bootstrap-5',
        width: '100%',
        placeholder: 'Quét barcode / nhập tên sản phẩm...',
        minimumInputLength: 1,
        ajax: {
            url: '/admin/stock-documents/product-lookup-select2',
            dataType: 'json',
            delay: 120,
            data: function (params) {
                return { term: params.term || '' };
            },
            processResults: function (data) {
                return data;
            }
        },
        templateResult: formatQuickLookupResult,
        templateSelection: formatQuickLookupSelection,
        escapeMarkup: function (markup) {
            return markup;
        }
    });

    el.on('select2:select', function (e) {
        const item = e.params.data;
        el.data('selected-item', item);

        openConfirmProductPopup(item);
    });

    el.on('select2:clear', function () {
        el.removeData('selected-item');
    });

    bindQuickLookupEnterAutoSelect();
}

function formatQuickLookupResult(item) {
    if (!item.id) return item.text || '';

    const title = escapeHtml(item.productName || item.text || '');
    const imageUrl = item.imageUrl || '';

    const imageHtml = imageUrl
        ? `<img class="sd-select2-product-img" src="${escapeHtml(imageUrl)}" alt="${title}" />`
        : `<div class="sd-select2-product-empty"><i class="bx bx-image"></i></div>`;

    const meta = [
        item.barcode ? 'Barcode: ' + escapeHtml(item.barcode) : '',
        item.unitName ? 'ĐVT: ' + escapeHtml(item.unitName) : '',
        item.factor ? 'Factor: x' + escapeHtml(item.factor) : '',
        item.price ? 'Giá lẻ: ' + formatNumber(item.price) : '',
        item.sourceType ? escapeHtml(item.sourceType) : ''
    ].filter(Boolean).join(' | ');

    return `
        <div class="sd-select2-product">
            ${imageHtml}
            <div class="sd-select2-product-body">
                <div class="sd-select2-product-title">${title}</div>
                <div class="sd-select2-product-meta">${meta}</div>
            </div>
        </div>
    `;
}

function formatQuickLookupSelection(item) {
    if (!item || !item.id) return item.text || 'Chọn sản phẩm';

    const parts = [];

    if (item.productName) parts.push(item.productName);
    if (item.barcode) parts.push('BC: ' + item.barcode);
    if (item.unitName) parts.push(item.unitName);
    if (item.factor) parts.push('x' + item.factor);

    return parts.join(' | ');
}

function bindQuickLookupEnterAutoSelect() {
    $(document).on('keydown', '.select2-container--open .select2-search__field', async function (e) {
        if (e.key !== 'Enter') return;

        const term = $(this).val();
        if (!term) return;

        try {
            const response = await fetch(`/admin/stock-documents/product-lookup-select2?term=${encodeURIComponent(term)}`);
            const api = await readApiResponse(response);

            if (!api.ok || !api.data?.results || api.data.results.length !== 1) return;

            e.preventDefault();

            const item = api.data.results[0];
            const el = $('#quickLookupInput');

            const option = new Option(item.text, item.id, true, true);
            el.append(option).trigger('change');

            el.trigger({
                type: 'select2:select',
                params: { data: item }
            });

            el.select2('close');
        } catch (error) {
            console.error(error);
        }
    });
}

function formatSimpleSelect2Result(item) {
    if (!item.id) return item.text || '';

    return `
        <div class="sd-select2-item sd-select2-item-simple">
            <div class="select2-result-title">${escapeHtml(item.text || '')}</div>
        </div>
    `;
}

function formatSimpleSelect2Selection(item) {
    return item.text || 'Chọn';
}

/* =========================================================
 * EDIT - PRODUCT CONFIRM POPUP
 * ========================================================= */

function openConfirmProductPopup(item) {
    if (!item || !quickAddProductModalInstance) return;

    $('#quickLookupInput').select2('close');

    const defaultUnitCost = resolveDefaultPurchaseUnitCost(item);

    $('#popupProductVariantId').val(item.productVariantId || '');
    $('#popupUnitId').val(item.unitId || '');

    setText('popupProductName', item.productName || item.text || '-');
    setText('popupBarcode', item.barcode || '-');
    setText('popupUnitName', item.unitName || '-');
    setText('popupFactor', item.factor || 1);
    setText('popupPrice', formatNumber(item.price || item.retailPrice || 0));
    setText('popupCostPrice', formatNumber(defaultUnitCost));

    const unitCostInput = document.getElementById('popupQuickUnitCost');
    if (unitCostInput) {
        unitCostInput.value = formatDecimalForInput(defaultUnitCost);
    }

    const qty = document.getElementById('popupQuickQty');
    if (qty) qty.value = '1';

    const msg = document.getElementById('popupQuickMessage');
    if (msg) msg.textContent = '';

    const img = document.getElementById('popupProductImage');
    const noImg = document.getElementById('popupProductNoImage');

    if (item.imageUrl) {
        img.src = item.imageUrl;
        img.classList.remove('d-none');
        noImg.classList.add('d-none');
    } else {
        img.src = '';
        img.classList.add('d-none');
        noImg.classList.remove('d-none');
    }

    updatePopupPreview();

    const modalEl = document.getElementById('quickAddProductModal');

    if (modalEl && modalEl.dataset.popupQtyFocusBound !== '1') {
        modalEl.dataset.popupQtyFocusBound = '1';

        modalEl.addEventListener('shown.bs.modal', function () {
            focusAndSelectPopupQuickQty();
        });
    }

    quickAddProductModalInstance.show();

    setTimeout(focusAndSelectPopupQuickQty, 350);
}
function updatePopupPreview() {
    const qty = parseDecimalInput(document.getElementById('popupQuickQty')?.value || '0');
    const factor = normalizeFactor(document.getElementById('popupFactor')?.textContent || '1');

    const baseQty = qty * factor;

    // UnitCost là giá theo đơn vị nhập, nên thành tiền = SL nhập * đơn giá nhập.
    const lineTotal = qty * unitCost;

    setText('popupBaseQty', formatDecimalForInput(baseQty));
    setText('popupLineTotal', formatNumber(lineTotal));
}


function bindPopupQuickAddLine() {
    const btn = document.getElementById('btnPopupQuickAddLine');
    const qty = document.getElementById('popupQuickQty');
    const unitCostInput = document.getElementById('popupQuickUnitCost');

    if (qty && !qty.dataset.bound) {
        qty.dataset.bound = '1';

        qty.addEventListener('input', updatePopupPreview);

        qty.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                btn?.click();
            }
        });
    }

    if (unitCostInput && !unitCostInput.dataset.bound) {
        unitCostInput.dataset.bound = '1';

        unitCostInput.addEventListener('input', updatePopupPreview);

        unitCostInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                btn?.click();
            }
        });
    }

    if (!btn || btn.dataset.bound) return;

    btn.dataset.bound = '1';

    btn.onclick = async function () {
        await popupQuickAddLine();
    };
}

async function popupQuickAddLine() {
    const selected = $('#quickLookupInput').data('selected-item');
    const msg = document.getElementById('popupQuickMessage');
    const btn = document.getElementById('btnPopupQuickAddLine');

    const stockDocumentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    const qty = parseDecimalInput(document.getElementById('popupQuickQty')?.value || '0');
    const unitCost = parseDecimalInput(document.getElementById('popupQuickUnitCost')?.value || '0');

    if (msg) msg.textContent = '';

    if (!stockDocumentId) {
        if (msg) msg.textContent = 'Không xác định được phiếu nhập.';
        return;
    }

    if (!selected) {
        if (msg) msg.textContent = 'Vui lòng chọn sản phẩm.';
        return;
    }

    if (qty <= 0) {
        if (msg) msg.textContent = 'Số lượng phải lớn hơn 0.';
        return;
    }

    try {
        if (btn) {
            btn.disabled = true;
            btn.textContent = 'Đang thêm...';
        }

        const response = await fetch(`/admin/api/stock-documents/${stockDocumentId}/lines`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                productVariantId: Number(selected.productVariantId),
                unitId: selected.unitId ? Number(selected.unitId) : null,
                quantity: qty,
                // Nhân viên chỉ ghi nhận hàng và số lượng. Giá được duyệt sau.
                unitCost: 0
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Thêm dòng thất bại.';
            return;
        }

        quickAddProductModalInstance?.hide();

        await refreshStockDocumentDetailUI();

        $('#quickLookupInput').val(null).trigger('change');
        $('#quickLookupInput').removeData('selected-item');

        setTimeout(focusQuickLookup, 120);
    } finally {
        if (btn) {
            btn.disabled = false;
            btn.textContent = 'Thêm dòng';
        }
    }
}

/* =========================================================
 * EDIT - HEADER AUTO SAVE
 * ========================================================= */

function bindEditHeaderAutoSave() {
    const canEditHeader = document.getElementById('CanEditHeader')?.value === 'true';
    if (!canEditHeader) return;

    const legalEntityEl = $('#LegalEntityId');
    const warehouseEl = $('#WarehouseId');
    const supplierEl = $('#SupplierId');
    const documentNote = document.getElementById('DocumentNote');
    const approvalNote = document.getElementById('ApprovalNote');

    if (legalEntityEl.length) legalEntityEl.on('change', queueEditHeaderSave);
    if (warehouseEl.length) warehouseEl.on('change', queueEditHeaderSave);
    if (supplierEl.length) supplierEl.on('change', queueEditHeaderSave);

    if (documentNote) documentNote.addEventListener('input', queueEditHeaderSave);
    if (approvalNote) approvalNote.addEventListener('input', queueEditHeaderSave);
}

function queueEditHeaderSave() {
    if (editHeaderSaveTimer) clearTimeout(editHeaderSaveTimer);

    editHeaderSaveTimer = setTimeout(function () {
        saveEditHeader();
    }, 500);
}

async function saveEditHeader() {
    const canEditHeader = document.getElementById('CanEditHeader')?.value === 'true';
    if (!canEditHeader) return;

    const stockDocumentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    if (!stockDocumentId) return;

    const payload = {
        stockDocumentId: stockDocumentId,
        legalEntityId: $('#LegalEntityId').val() ? Number($('#LegalEntityId').val()) : null,
        warehouseId: $('#WarehouseId').val() ? Number($('#WarehouseId').val()) : null,
        supplierId: $('#SupplierId').val() ? Number($('#SupplierId').val()) : null,
        note: document.getElementById('DocumentNote')?.value || '',
        approvalNote: document.getElementById('ApprovalNote')?.value || ''
    };

    const response = await fetch('/admin/stock-documents/update-header', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    });

    const api = await readApiResponse(response);

    if (!api.ok) {
        console.error(api.data?.message || 'Cập nhật header thất bại');
    }
}

/* =========================================================
 * EDIT - INPUT INVOICE XML UPLOAD
 * ========================================================= */

function bindUploadInputInvoiceXml() {
    const btn = document.getElementById('btnUploadInputInvoiceXml');
    const fileInput = document.getElementById('inputInvoiceXmlFile');

    if (!btn || !fileInput) return;

    btn.onclick = async function () {
        const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
        const file = fileInput.files && fileInput.files.length > 0
            ? fileInput.files[0]
            : null;

        if (!documentId) {
            setInputInvoiceUploadMessage('Không xác định được phiếu nhập.', true);
            return;
        }

        if (!file) {
            setInputInvoiceUploadMessage('Vui lòng chọn file XML.', true);
            return;
        }

        const fileName = file.name || '';

        if (!fileName.toLowerCase().endsWith('.xml')) {
            setInputInvoiceUploadMessage('Chỉ hỗ trợ file XML.', true);
            return;
        }

        const formData = new FormData();
        formData.append('file', file);

        try {
            btn.disabled = true;
            btn.textContent = 'Đang upload...';

            setInputInvoiceUploadMessage('Đang đọc file XML...', false);

            const response = await fetch(`/admin/api/stock-documents/${documentId}/input-invoices/upload-xml`, {
                method: 'POST',
                body: formData,
                credentials: 'same-origin'
            });

            const api = await readApiResponse(response);

            if (!api.ok) {
                setInputInvoiceUploadMessage(api.data?.message || 'Upload XML thất bại.', true);
                return;
            }

            setInputInvoiceUploadMessage(api.data?.message || 'Đã upload XML thành công.', false);

            renderInputInvoiceInfo(api.data?.data);

            await loadInputInvoicesForStockDocument();

            fileInput.value = '';
        } catch (error) {
            console.error(error);
            setInputInvoiceUploadMessage('Có lỗi khi upload XML.', true);
        } finally {
            btn.disabled = false;
            btn.textContent = 'Upload XML';
        }
    };
}

function setInputInvoiceUploadMessage(message, isError) {
    const msg = document.getElementById('inputInvoiceUploadMessage');
    if (!msg) return;

    msg.textContent = message || '';
    msg.classList.remove('text-danger', 'text-success', 'text-muted');
    msg.classList.add(isError ? 'text-danger' : 'text-success');
}

function renderInputInvoiceInfo(data) {
    if (!data) return;

    const box = document.getElementById('inputInvoiceInfoBox');
    if (box) box.classList.remove('d-none');

    setText('xmlInvoiceSeries', data.invoiceSeries || '-');
    setText('xmlInvoiceNumber', data.invoiceNumber || '-');
    setText('xmlInvoiceDate', data.invoiceDate ? formatDate(data.invoiceDate) : '-');
    setText('xmlDetailCount', data.detailCount ?? 0);

    setText('xmlSellerTaxCode', data.sellerTaxCode || '-');
    setText('xmlSellerName', data.sellerName || '-');

    setText('xmlTotalBeforeTax', formatNumber(data.totalBeforeTax || 0));
    setText('xmlTotalTaxAmount', formatNumber(data.totalTaxAmount || 0));
    setText('xmlTotalPaymentAmount', formatNumber(data.totalPaymentAmount || 0));
}

async function loadInputInvoicesForStockDocument() {
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    const container = document.getElementById('inputInvoiceListContainer');

    if (!documentId || !container) return;

    try {
        container.innerHTML = `<div class="text-muted small">Đang tải hóa đơn XML...</div>`;

        const response = await fetch(`/admin/api/stock-documents/${documentId}/input-invoices`, {
            method: 'GET',
            credentials: 'same-origin',
            cache: 'no-store'
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            container.innerHTML = `
                <div class="text-danger small">
                    ${escapeHtml(api.data?.message || 'Không tải được danh sách XML.')}
                </div>`;
            return;
        }

        renderInputInvoiceList(api.data || []);
    } catch (error) {
        console.error(error);

        container.innerHTML = `
            <div class="text-danger small">
                Có lỗi khi tải danh sách XML.
            </div>`;
    }
}

function renderInputInvoiceList(invoices) {
    const container = document.getElementById('inputInvoiceListContainer');
    if (!container) return;

    if (!invoices || invoices.length === 0) {
        container.innerHTML = `
            <div class="text-muted small">
                Chưa có hóa đơn XML nào được gắn với phiếu này.
            </div>`;
        return;
    }

    container.innerHTML = invoices.map((invoice, index) => {
        const collapseId = `inputInvoiceDetails_${invoice.id}`;

        return `
            <div class="border rounded-3 mb-3 overflow-hidden">
                <div class="p-3 bg-white d-flex justify-content-between align-items-start flex-wrap gap-2">
                    <div>
                        <div class="fw-bold">
                            XML #${index + 1} -
                            Ký hiệu: ${escapeHtml(invoice.invoiceSeries || '-')}
                            | Số: ${escapeHtml(invoice.invoiceNumber || '-')}
                        </div>

                        <div class="small text-muted mt-1">
                            Ngày: ${invoice.invoiceDate ? formatDate(invoice.invoiceDate) : '-'}
                            | MST bán: ${escapeHtml(invoice.sellerTaxCode || '-')}
                            | NCC: ${escapeHtml(invoice.sellerName || '-')}
                        </div>

                        <div class="small mt-1">
                            Tổng thanh toán:
                            <b class="text-primary">${formatNumber(invoice.totalPaymentAmount || 0)}</b>
                            | Số dòng: <b>${invoice.detailCount || 0}</b>
                        </div>
                    </div>

                    <button class="btn btn-sm btn-outline-primary"
                            type="button"
                            data-bs-toggle="collapse"
                            data-bs-target="#${collapseId}">
                        Xem dòng XML
                    </button>
                </div>

                <div class="collapse show" id="${collapseId}">
                    ${renderInputInvoiceDetailTable(invoice.details || [])}
                </div>
            </div>
        `;
    }).join('');
}

function renderInputInvoiceDetailTable(details) {
    if (!details || details.length === 0) {
        return `
            <div class="p-3 text-muted small">
                XML chưa có dòng hàng.
            </div>`;
    }

    return `
        <div class="table-responsive">
            <table class="table table-bordered table-sm align-middle mb-0">
                <thead class="table-light">
                    <tr>
                        <th style="width:70px;" class="text-center">STT</th>
                        <th style="min-width:280px;">Tên hàng XML</th>
                        <th style="width:100px;">ĐVT</th>
                        <th style="width:110px;" class="text-end">SL</th>
                        <th style="width:130px;" class="text-end">Đơn giá</th>
                        <th style="width:140px;" class="text-end">Thành tiền</th>
                        <th style="width:90px;" class="text-center">VAT</th>
                        <th style="width:130px;" class="text-end">Tiền VAT</th>
                    </tr>
                </thead>

                <tbody>
                    ${details.map(d => `
                        <tr>
                            <td class="text-center">${d.lineNo || ''}</td>
                            <td>${escapeHtml(d.itemName || '')}</td>
                            <td>${escapeHtml(d.unitName || '')}</td>
                            <td class="text-end">${formatNumber(d.quantity || 0)}</td>
                            <td class="text-end">${formatNumber(d.unitPrice || 0)}</td>
                            <td class="text-end fw-semibold">${formatNumber(d.lineAmount || 0)}</td>
                            <td class="text-center">${escapeHtml(d.vatRate || '')}</td>
                            <td class="text-end">${formatNumber(d.vatAmount || 0)}</td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        </div>
    `;
}

/* =========================================================
 * EDIT - INPUT INVOICE LINE MAP
 * ========================================================= */

function bindOpenMapInputInvoiceLineModal() {
    document.querySelectorAll('.btn-map-input-invoice-line').forEach(btn => {
        btn.onclick = async function () {
            const lineId = Number(this.dataset.lineId || 0);
            if (!lineId) return;

            selectedInputInvoiceDetailId = null;

            document.getElementById('mapStockDocumentLineId').value = lineId;
            document.getElementById('mapUseInputInvoice').checked = true;
            document.getElementById('mapInputInvoiceMessage').textContent = '';

            await ensureInputInvoicesLoaded();
            renderXmlLinePicker();

            if (mapInputInvoiceLineModalInstance) {
                mapInputInvoiceLineModalInstance.show();
            }
        };
    });
}

async function ensureInputInvoicesLoaded() {
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    if (!documentId) return;

    const response = await fetch(`/admin/api/stock-documents/${documentId}/input-invoices`, {
        method: 'GET',
        credentials: 'same-origin',
        cache: 'no-store'
    });

    const api = await readApiResponse(response);

    if (!api.ok) {
        cachedInputInvoices = [];
        return;
    }

    cachedInputInvoices = api.data || [];
}

function renderXmlLinePicker() {
    const box = document.getElementById('mapInputInvoiceXmlLinesBox');
    if (!box) return;

    const details = [];

    (cachedInputInvoices || []).forEach(inv => {
        (inv.details || []).forEach(d => {
            details.push({
                ...d,
                invoiceSeries: inv.invoiceSeries,
                invoiceNumber: inv.invoiceNumber
            });
        });
    });

    if (!details.length) {
        box.innerHTML = `<div class="text-muted">Chưa có dòng XML. Hãy upload XML trước.</div>`;
        return;
    }

    box.innerHTML = `
        <div class="table-responsive" style="max-height:420px; overflow:auto;">
            <table class="table table-bordered table-hover table-sm align-middle">
                <thead class="table-light sticky-top">
                    <tr>
                        <th style="width:60px;">Chọn</th>
                        <th>Hóa đơn</th>
                        <th>STT</th>
                        <th>Tên hàng XML</th>
                        <th>ĐVT</th>
                        <th class="text-end">SL</th>
                        <th class="text-end">Đơn giá</th>
                        <th class="text-end">Thành tiền</th>
                    </tr>
                </thead>

                <tbody>
                    ${details.map(d => `
                        <tr>
                            <td class="text-center">
                                <input type="radio"
                                       name="xmlDetailPicker"
                                       value="${d.id}"
                                       class="form-check-input js-pick-xml-detail" />
                            </td>
                            <td>${escapeHtml(d.invoiceSeries || '')}-${escapeHtml(d.invoiceNumber || '')}</td>
                            <td>${d.lineNo || ''}</td>
                            <td>${escapeHtml(d.itemName || '')}</td>
                            <td>${escapeHtml(d.unitName || '')}</td>
                            <td class="text-end">${formatNumber(d.quantity || 0)}</td>
                            <td class="text-end">${formatNumber(d.unitPrice || 0)}</td>
                            <td class="text-end fw-semibold">${formatNumber(d.lineAmount || 0)}</td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        </div>
    `;

    box.querySelectorAll('.js-pick-xml-detail').forEach(radio => {
        radio.onchange = function () {
            selectedInputInvoiceDetailId = Number(this.value || 0);
        };
    });
}

function bindSaveInputInvoiceLineMap() {
    const btn = document.getElementById('btnSaveInputInvoiceLineMap');
    if (!btn) return;

    btn.onclick = async function () {
        const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
        const lineId = Number(document.getElementById('mapStockDocumentLineId')?.value || 0);
        const useInputInvoice = document.getElementById('mapUseInputInvoice')?.checked === true;
        const msg = document.getElementById('mapInputInvoiceMessage');

        if (!lineId) {
            if (msg) msg.textContent = 'Không xác định được dòng nhập.';
            return;
        }

        const response = await fetch(`/admin/api/stock-documents/${documentId}/input-invoices/line-maps`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify({
                stockDocumentLineId: lineId,
                useInputInvoice: useInputInvoice,
                inputInvoiceDetailId: useInputInvoice ? selectedInputInvoiceDetailId : null
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Lưu map thất bại.';
            return;
        }

        if (mapInputInvoiceLineModalInstance) {
            mapInputInvoiceLineModalInstance.hide();
        }

        await refreshStockDocumentDetailUI();
        await loadInputInvoicesForStockDocument();
    };
}

function bindToggleAllInputInvoiceLines() {
    const toggle = document.getElementById('toggleAllInputInvoiceLines');
    if (!toggle) return;

    toggle.onchange = async function () {
        const useInputInvoice = toggle.checked;
        const oldValue = !useInputInvoice;

        toggle.disabled = true;
        updateToggleAllInputInvoiceLinesLabel(useInputInvoice, true);

        try {
            await bulkUpdateInputInvoiceLines(useInputInvoice);
            updateToggleAllInputInvoiceLinesLabel(useInputInvoice, false);
        } catch (error) {
            console.error(error);

            toggle.checked = oldValue;
            updateToggleAllInputInvoiceLinesLabel(oldValue, false);
        } finally {
            toggle.disabled = false;
        }
    };
}

function updateToggleAllInputInvoiceLinesLabel(isOn, isLoading) {
    const label = document.getElementById('toggleAllInputInvoiceLinesLabel');
    if (!label) return;

    if (isLoading) {
        label.textContent = 'Đang cập nhật...';
        return;
    }

    label.textContent = isOn ? 'Tất cả thuộc XML' : 'Không thuộc XML';
}

function syncToggleAllInputInvoiceLinesState() {
    const toggle = document.getElementById('toggleAllInputInvoiceLines');
    if (!toggle) return;

    const container = document.getElementById('stockDocumentLinesContainer');
    if (!container) return;

    const badges = container.querySelectorAll('.badge');

    let hasXml = false;
    let hasNonXml = false;

    badges.forEach(badge => {
        const text = (badge.textContent || '').trim();

        if (text.includes('Có HĐ XML')) hasXml = true;
        if (text.includes('Không thuộc XML')) hasNonXml = true;
    });

    toggle.checked = hasXml && !hasNonXml;

    updateToggleAllInputInvoiceLinesLabel(toggle.checked, false);
}

async function bulkUpdateInputInvoiceLines(useInputInvoice) {
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);

    if (!documentId) {
        throw new Error('Không xác định được phiếu nhập.');
    }

    const response = await fetch(`/admin/api/stock-documents/${documentId}/input-invoices/line-maps/bulk`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({
            useInputInvoice: useInputInvoice
        })
    });

    const api = await readApiResponse(response);

    if (!api.ok) {
        alert(api.data?.message || 'Cập nhật hàng loạt thất bại.');
        throw new Error(api.data?.message || 'Bulk update failed');
    }

    await refreshStockDocumentDetailUI();
    await loadInputInvoicesForStockDocument();
}

/* =========================================================
 * KEYBOARD FLOW
 * ========================================================= */

function bindStockDocumentKeyboardFlow() {
    if (document.body.dataset.stockdocKeyboardBound === '1') return;
    document.body.dataset.stockdocKeyboardBound = '1';

    document.addEventListener('keydown', async function (e) {
        const isEditMode = window.stockDocumentPage?.mode === 'edit';
        if (!isEditMode) return;

        if (e.ctrlKey && !e.shiftKey && e.key.toLowerCase() === 's') {
            e.preventDefault();

            const canEditHeader = document.getElementById('CanEditHeader')?.value === 'true';
            if (canEditHeader) {
                await saveEditHeader();
                showStockDocumentMiniToast('Đã lưu thông tin phiếu.');
            }

            return;
        }

        if (e.ctrlKey && e.key === 'Enter') {
            e.preventDefault();

            const canSubmitApproval = window.stockDocumentPage?.canSubmitApproval === true;
            const btnOpenSubmit = document.getElementById('btnOpenSubmitApprovalModal');

            if (canSubmitApproval && btnOpenSubmit) {
                btnOpenSubmit.click();
            }

            return;
        }

        if (e.altKey && !e.ctrlKey && !e.shiftKey && e.key.toLowerCase() === 'a') {
            e.preventDefault();
            focusQuickLookup();
        }
    });
}

function showStockDocumentMiniToast(message) {
    let el = document.getElementById('stockDocumentMiniToast');

    if (!el) {
        el = document.createElement('div');
        el.id = 'stockDocumentMiniToast';
        el.style.position = 'fixed';
        el.style.right = '24px';
        el.style.bottom = '24px';
        el.style.zIndex = '9999';
        el.style.padding = '10px 14px';
        el.style.borderRadius = '12px';
        el.style.background = '#198754';
        el.style.color = '#fff';
        el.style.boxShadow = '0 .5rem 1.5rem rgba(0,0,0,.18)';
        el.style.fontWeight = '700';

        document.body.appendChild(el);
    }

    el.textContent = message || 'Đã lưu.';
    el.style.display = 'block';

    clearTimeout(el._timer);

    el._timer = setTimeout(function () {
        el.style.display = 'none';
    }, 1400);
}
function focusAndSelectPopupQuickQty() {
    const qtyInput = document.getElementById('popupQuickQty');
    if (!qtyInput) return;

    qtyInput.focus();

    setTimeout(function () {
        qtyInput.select();
    }, 30);
}

function bindPopupQuickQtySelectAll() {
    const qty = document.getElementById('popupQuickQty');
    const unitCost = document.getElementById('popupQuickUnitCost');

    [qty, unitCost].forEach(input => {
        if (!input || input.dataset.selectAllBound === '1') return;

        input.dataset.selectAllBound = '1';

        input.addEventListener('focus', function () {
            this.select();
        });

        input.addEventListener('click', function () {
            this.select();
        });
    });
}
function showStockDocumentToast(type, title, message, redirectUrl) {
    let toast = document.getElementById('stockDocumentProToast');

    if (!toast) {
        toast = document.createElement('div');
        toast.id = 'stockDocumentProToast';
        toast.className = 'sd-pro-toast';
        document.body.appendChild(toast);
    }

    const icon = type === 'success' ? 'bx-check-circle' : 'bx-error-circle';
    const cls = type === 'success' ? 'success' : 'danger';

    toast.innerHTML = `
        <div class="sd-pro-toast-icon ${cls}">
            <i class="bx ${icon}"></i>
        </div>

        <div class="sd-pro-toast-body">
            <div class="sd-pro-toast-title">${escapeHtml(title || '')}</div>
            <div class="sd-pro-toast-message">${escapeHtml(message || '')}</div>
        </div>
    `;

    toast.classList.add('show');

    setTimeout(function () {
        toast.classList.remove('show');

        if (redirectUrl) {
            window.location.href = redirectUrl;
        }
    }, 1400);
}
function bindRevisionRequestActions() {

    const btnReturn =
        document.getElementById('btnReturnToEdit');

    const btnIgnore =
        document.getElementById('btnIgnoreRevisionRequest');

    if (btnReturn) {
        btnReturn.onclick = function () {

            const note =
                document.getElementById('returnToEditNote');

            const msg =
                document.getElementById('returnToEditMessage');

            if (note) note.value = '';
            if (msg) msg.textContent = '';

            returnToEditModalInstance?.show();
        };
    }

    const btnConfirm =
        document.getElementById('btnConfirmReturnToEdit');

    if (btnConfirm) {

        btnConfirm.onclick = async function () {

            const documentId =
                window.stockDocumentPage.documentId;

            const note =
                document.getElementById('returnToEditNote')?.value || '';

            const response = await fetch(
                `/admin/api/stock-documents/${documentId}/resolve-revision-request`,
                {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({
                        returnToEdit: true,
                        note: note
                    })
                });

            const api = await readApiResponse(response);

            if (!api.ok) {
                document.getElementById(
                    'returnToEditMessage'
                ).textContent =
                    api.data?.message || 'Thao tác thất bại';

                return;
            }

            location.reload();
        };
    }

    if (btnIgnore) {

        btnIgnore.onclick = async function () {

            const documentId =
                window.stockDocumentPage.documentId;

            const response = await fetch(
                `/admin/api/stock-documents/${documentId}/resolve-revision-request`,
                {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({
                        returnToEdit: false
                    })
                });

            const api = await readApiResponse(response);

            if (!api.ok) {
                alert(
                    api.data?.message ||
                    'Thao tác thất bại');
                return;
            }

            location.reload();
        };
    }
}
