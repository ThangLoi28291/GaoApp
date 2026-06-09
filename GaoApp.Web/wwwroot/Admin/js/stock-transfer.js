/* =========================================================
   STOCK TRANSFER UI JS
   - Index + Detail
   - Toast đẹp giống StockCount
   - Autofocus Select2 ổn định
   - Không dùng alert/confirm mặc định
========================================================= */

let createStockTransferModalInstance = null;
let quickDetailStockTransferModalInstance = null;
let editStockTransferLineModalInstance = null;
let rejectStockTransferModalInstance = null;
let deleteStockTransferLineModalInstance = null;
let actionStockTransferModalInstance = null;

let stockTransferHeaderSaveTimer = null;
let stockTransferIndexSearchTimer = null;
let stockTransferLineModalInstance = null;
const stockTransferUiState = {
    index: {
        page: 1,
        pageSize: 20,
        totalItems: 0,
        items: []
    },
    currentDetail: null,
    pendingAction: null
};

document.addEventListener('DOMContentLoaded', function () {
    if (!window.stockTransferPage) return;

    initStockTransferModalInstances();
    bindStockTransferGlobalKeyboard();

    if (window.stockTransferPage.mode === 'index') {
        bindStockTransferIndexEvents();
        loadStockTransferList();
    }

    if (window.stockTransferPage.mode === 'detail') {
        bindStockTransferDetailEvents();
        initStockTransferQuickLookupSelect2();
        initStockTransferWarehouseSelect();
        loadStockTransferDetail();
    }
});

/* =========================================================
   COMMON
========================================================= */

async function readStockTransferApiResponse(response) {
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
            data: { message: text || 'Có lỗi xảy ra.' }
        };
    }
}

function stockTransferEscapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function stockTransferFormatDateTime(value) {
    if (!value) return '---';
    const d = new Date(value);
    if (isNaN(d.getTime())) return '---';
    return d.toLocaleString('vi-VN', { hour12: false });
}

function stockTransferFormatDate(value) {
    if (!value) return '---';
    const d = new Date(value);
    if (isNaN(d.getTime())) return '---';
    return d.toLocaleDateString('vi-VN');
}

function stockTransferFormatNumber(value) {
    return Number(value ?? 0).toLocaleString('vi-VN', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 3
    });
}

function stockTransferStatusText(status) {
    switch (Number(status)) {
        case 0: return 'Nháp';
        case 1: return 'Chờ duyệt';
        case 2: return 'Từ chối';
        case 3: return 'Đã xác nhận';
        default: return 'Không rõ';
    }
}

function stockTransferStatusClass(status) {
    switch (Number(status)) {
        case 0: return 'st-status-draft';
        case 1: return 'st-status-pending';
        case 2: return 'st-status-rejected';
        case 3: return 'st-status-confirmed';
        default: return 'bg-secondary text-white';
    }
}

function stockTransferStatusBadge(status) {
    return `<span class="st-status ${stockTransferStatusClass(status)}">${stockTransferStatusText(status)}</span>`;
}

function stockTransferEditable(status) {
    return Number(status) === 0 || Number(status) === 1 || Number(status) === 2;
}

function stockTransferCanSubmit(status) {
    return Number(status) === 0 || Number(status) === 2;
}

function stockTransferCanApprove(status) {
    return Number(status) === 1;
}

function transferToLocalDateTime(value) {
    if (!value) return '';
    const d = new Date(value);
    if (isNaN(d.getTime())) return '';

    return new Date(d.getTime() - d.getTimezoneOffset() * 60000)
        .toISOString()
        .slice(0, 16);
}

function initStockTransferModalInstances() {
    const createModal = document.getElementById('createStockTransferModal');
    const quickDetailModal = document.getElementById('stockTransferQuickDetailModal');
    const editModal = document.getElementById('editStockTransferLineModal');
    const rejectModal = document.getElementById('rejectStockTransferModal');
    const deleteModal = document.getElementById('deleteStockTransferLineModal');
    const actionModal = document.getElementById('stockTransferActionConfirmModal');
    const lineModal = document.getElementById('stockTransferLineModal');
    if (lineModal) stockTransferLineModalInstance = new bootstrap.Modal(lineModal);

    if (createModal) createStockTransferModalInstance = new bootstrap.Modal(createModal);
    if (quickDetailModal) quickDetailStockTransferModalInstance = new bootstrap.Modal(quickDetailModal);
    if (editModal) editStockTransferLineModalInstance = new bootstrap.Modal(editModal);
    if (rejectModal) rejectStockTransferModalInstance = new bootstrap.Modal(rejectModal);
    if (deleteModal) deleteStockTransferLineModalInstance = new bootstrap.Modal(deleteModal);
    if (actionModal) actionStockTransferModalInstance = new bootstrap.Modal(actionModal);
}

/* =========================================================
   TOAST + FOCUS
========================================================= */

function showStockTransferToast(type, title, message) {
    const containerId = 'stockTransferToastContainer';
    let container = document.getElementById(containerId);

    if (!container) {
        container = document.createElement('div');
        container.id = containerId;
        container.className = 'toast-container position-fixed top-0 end-0 p-3';
        container.style.zIndex = '1090';
        document.body.appendChild(container);
    }

    const toastId = `st-toast-${Date.now()}`;

    const icon = type === 'success'
        ? 'bx-check-circle'
        : type === 'danger'
            ? 'bx-error-circle'
            : type === 'warning'
                ? 'bx-error'
                : 'bx-info-circle';

    container.insertAdjacentHTML('beforeend', `
        <div id="${toastId}" class="toast st-toast border-0 shadow-lg" role="alert">
            <div class="toast-body d-flex gap-3">
                <div class="st-toast-icon st-toast-${stockTransferEscapeHtml(type)}">
                    <i class="bx ${icon}"></i>
                </div>

                <div class="flex-grow-1">
                    <div class="fw-bold">${stockTransferEscapeHtml(title)}</div>
                    <div class="small text-muted">${stockTransferEscapeHtml(message || '')}</div>
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

function focusStockTransferProductInput(delay = 250) {
    const select = $('#QuickTransferLookupInput');

    if (!select.length || !window.jQuery || !$.fn.select2) return;

    setTimeout(function () {
        try {
            select.select2('open');
        } catch {
            return;
        }

        setTimeout(function () {
            const searchInput = document.querySelector('.select2-container--open .select2-search__field');

            if (searchInput) {
                searchInput.focus();
                searchInput.select();
                return;
            }

            setTimeout(function () {
                try {
                    select.select2('open');
                } catch {
                    return;
                }

                setTimeout(function () {
                    const retryInput = document.querySelector('.select2-container--open .select2-search__field');
                    retryInput?.focus();
                    retryInput?.select();
                }, 120);
            }, 180);
        }, 140);
    }, delay);
}

function focusStockTransferQuickLookup() {
    focusStockTransferProductInput(250);
}

/* =========================================================
   KEYBOARD
========================================================= */

function bindStockTransferGlobalKeyboard() {
    document.addEventListener('keydown', function (e) {
        if (!window.stockTransferPage) return;

        if (e.altKey && e.key.toLowerCase() === 'a') {
            e.preventDefault();
            focusStockTransferQuickLookup();
            return;
        }

        if (e.ctrlKey && e.key.toLowerCase() === 's') {
            e.preventDefault();

            if (window.stockTransferPage.mode === 'detail') {
                saveStockTransferHeader(true);
            }

            return;
        }

        if (e.ctrlKey && e.key === 'Enter') {
            e.preventDefault();

            if (window.stockTransferPage.mode !== 'detail') return;

            const detail = stockTransferUiState.currentDetail;
            if (!detail) return;

            if (stockTransferCanSubmit(detail.status)) {
                openStockTransferActionConfirm(
                    'Gửi duyệt phiếu chuyển kho',
                    'Phiếu sẽ chuyển sang trạng thái chờ duyệt.',
                    submitStockTransferDocument
                );
                return;
            }

            if (stockTransferCanApprove(detail.status)) {
                openStockTransferActionConfirm(
                    'Xác nhận phiếu chuyển kho',
                    'Sau khi xác nhận, hệ thống sẽ sinh ledger xuất kho nguồn và nhập kho đích.',
                    confirmStockTransferDocument
                );
            }
        }
    });
}

/* =========================================================
   INDEX
========================================================= */

function bindStockTransferIndexEvents() {
    document.getElementById('btnReloadStockTransferList')
        ?.addEventListener('click', function () {
            stockTransferUiState.index.page = 1;
            loadStockTransferList();
        });

    document.getElementById('btnOpenCreateStockTransferModal')
        ?.addEventListener('click', async function () {
            await loadTransferWarehouseOptionsForCreate();
            resetCreateStockTransferForm();
            createStockTransferModalInstance?.show();
        });

    document.getElementById('createStockTransferModal')
        ?.addEventListener('shown.bs.modal', function () {
            setTimeout(() => {
                document.getElementById('createTransferDocumentName')?.focus();
            }, 150);
        });

    document.getElementById('btnCreateStockTransfer')
        ?.addEventListener('click', createStockTransferDocument);

    ['stKeyword', 'stStatus', 'stFromDate', 'stToDate', 'stPageSize'].forEach(id => {
        const el = document.getElementById(id);
        if (!el) return;

        const eventName = id === 'stKeyword' ? 'input' : 'change';

        el.addEventListener(eventName, function () {
            if (stockTransferIndexSearchTimer) {
                clearTimeout(stockTransferIndexSearchTimer);
            }

            stockTransferIndexSearchTimer = setTimeout(function () {
                stockTransferUiState.index.page = 1;
                loadStockTransferList();
            }, id === 'stKeyword' ? 350 : 0);
        });
    });

    document.getElementById('btnStPrevPage')?.addEventListener('click', function () {
        if (stockTransferUiState.index.page <= 1) return;
        stockTransferUiState.index.page--;
        loadStockTransferList();
    });

    document.getElementById('btnStNextPage')?.addEventListener('click', function () {
        const totalPages = getStockTransferTotalPages();
        if (stockTransferUiState.index.page >= totalPages) return;
        stockTransferUiState.index.page++;
        loadStockTransferList();
    });
}

function buildStockTransferIndexQuery() {
    const keyword = document.getElementById('stKeyword')?.value?.trim() || '';
    const statusRaw = document.getElementById('stStatus')?.value ?? '';
    const fromDate = document.getElementById('stFromDate')?.value || '';
    const toDate = document.getElementById('stToDate')?.value || '';
    const pageSize = Number(document.getElementById('stPageSize')?.value || 20);

    stockTransferUiState.index.pageSize = pageSize;

    const params = new URLSearchParams();
    params.set('page', String(stockTransferUiState.index.page));
    params.set('pageSize', String(pageSize));

    if (keyword) params.set('keyword', keyword);
    if (fromDate) params.set('fromDate', fromDate);
    if (toDate) params.set('toDate', toDate);

    if (statusRaw && statusRaw !== 'all') {
        params.set('status', statusRaw);
    }

    return {
        query: params.toString(),
        statusRaw
    };
}

async function loadStockTransferList() {
    const body = document.getElementById('stockTransferIndexBody');

    if (body) {
        body.innerHTML = `<tr><td colspan="8" class="st-empty">Đang tải dữ liệu...</td></tr>`;
    }

    const built = buildStockTransferIndexQuery();
    const response = await fetch(`/admin/api/stock-transfers?${built.query}`);
    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        setTransferIndexTableError(api.data?.message || 'Không tải được danh sách phiếu chuyển kho.');
        showStockTransferToast('danger', 'Không tải được dữ liệu', api.data?.message || 'Vui lòng thử lại.');
        return;
    }

    const payload = api.data || {};
    let items = payload.items || payload.Items || payload.data || payload || [];

    if (!Array.isArray(items)) items = [];

    if (built.statusRaw === '') {
        items = items.filter(x => Number(x.status) === 0 || Number(x.status) === 1);
    }

    stockTransferUiState.index.items = items;
    stockTransferUiState.index.totalItems = Number(payload.totalItems ?? payload.TotalItems ?? items.length);

    renderStockTransferIndexSummary(items);
    renderStockTransferIndexRows(items);
    renderStockTransferPagination();
}

function setTransferIndexTableError(message) {
    const body = document.getElementById('stockTransferIndexBody');
    if (!body) return;

    body.innerHTML = `
        <tr>
            <td colspan="8" class="st-empty text-danger fw-semibold">
                ${stockTransferEscapeHtml(message)}
            </td>
        </tr>
    `;
}

function renderStockTransferIndexSummary(items) {
    document.getElementById('sumTotalTransfers').textContent = String(items.length);
    document.getElementById('sumWorkingTransfers').textContent = String(items.filter(x => Number(x.status) === 0 || Number(x.status) === 2).length);
    document.getElementById('sumPendingTransfers').textContent = String(items.filter(x => Number(x.status) === 1).length);
    document.getElementById('sumConfirmedTransfers').textContent = String(items.filter(x => Number(x.status) === 3).length);
}

function renderStockTransferIndexRows(items) {
    const body = document.getElementById('stockTransferIndexBody');
    if (!body) return;

    if (!items.length) {
        body.innerHTML = `<tr><td colspan="8" class="st-empty">Không có phiếu chuyển kho phù hợp.</td></tr>`;
        return;
    }

    body.innerHTML = items.map(x => {
        const docName = x.documentName || x.name || x.note || 'Phiếu chuyển kho';

        return `
            <tr class="st-row" data-id="${x.id}">
                <td>
                    <div class="st-doc-name">${stockTransferEscapeHtml(docName)}</div>
                    <div class="st-doc-no">${stockTransferEscapeHtml(x.documentNo || '')}</div>
                </td>
                <td class="st-mobile-hide">${stockTransferFormatDate(x.documentDate)}</td>
                <td>${stockTransferEscapeHtml(x.fromWarehouseName || '')}</td>
                <td>${stockTransferEscapeHtml(x.toWarehouseName || '')}</td>
                <td>${stockTransferStatusBadge(x.status)}</td>
                <td class="text-end st-mobile-hide">${stockTransferFormatNumber(x.totalLines)}</td>
                <td class="st-mobile-hide"><div class="st-note">${stockTransferEscapeHtml(x.note || '')}</div></td>
                <td class="text-end">
                    <a href="/admin/stock-transfers/${x.id}" class="btn btn-sm btn-primary">Mở</a>
                </td>
            </tr>
        `;
    }).join('');

    bindStockTransferIndexRows();
}

function bindStockTransferIndexRows() {
    document.querySelectorAll('#stockTransferIndexBody tr[data-id]').forEach(row => {
        row.addEventListener('dblclick', function (e) {
            if (e.target.closest('a,button')) return;
            const id = Number(this.dataset.id || 0);
            if (id) openStockTransferQuickDetail(id);
        });
    });
}

async function openStockTransferQuickDetail(id) {
    const body = document.getElementById('stockTransferQuickDetailBody');
    const openBtn = document.getElementById('btnOpenStockTransferDetail');

    if (body) body.innerHTML = 'Đang tải...';
    if (openBtn) openBtn.href = `/admin/stock-transfers/${id}`;

    quickDetailStockTransferModalInstance?.show();

    const response = await fetch(`/admin/api/stock-transfers/${id}`);
    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        if (body) {
            body.innerHTML = `<div class="text-danger fw-semibold">${stockTransferEscapeHtml(api.data?.message || 'Không tải được chi tiết.')}</div>`;
        }
        return;
    }

    const d = api.data;
    const lines = d.lines || [];
    const totalQty = lines.reduce((sum, x) => sum + Number(x.quantity || 0), 0);
    const productTypes = new Set(lines.map(x => x.productVariantId)).size;

    if (body) {
        body.innerHTML = `
            <div class="mb-3">
                <div class="st-doc-name fs-5">${stockTransferEscapeHtml(d.documentName || d.note || 'Phiếu chuyển kho')}</div>
                <div class="st-doc-no">${stockTransferEscapeHtml(d.documentNo || '')}</div>
            </div>

            <div class="st-quick-detail-row">
                <div class="st-quick-detail-label">Trạng thái</div>
                <div class="st-quick-detail-value">${stockTransferStatusBadge(d.status)}</div>
            </div>

            <div class="st-quick-detail-row">
                <div class="st-quick-detail-label">Ngày phiếu</div>
                <div class="st-quick-detail-value">${stockTransferFormatDateTime(d.documentDate)}</div>
            </div>

            <div class="st-quick-detail-row">
                <div class="st-quick-detail-label">Kho nguồn</div>
                <div class="st-quick-detail-value">${stockTransferEscapeHtml(d.fromWarehouseName || '')}</div>
            </div>

            <div class="st-quick-detail-row">
                <div class="st-quick-detail-label">Kho đích</div>
                <div class="st-quick-detail-value">${stockTransferEscapeHtml(d.toWarehouseName || '')}</div>
            </div>

            <div class="st-quick-detail-row">
                <div class="st-quick-detail-label">Số dòng</div>
                <div class="st-quick-detail-value">${stockTransferFormatNumber(lines.length)}</div>
            </div>

            <div class="st-quick-detail-row">
                <div class="st-quick-detail-label">Tổng loại sản phẩm</div>
                <div class="st-quick-detail-value">${stockTransferFormatNumber(productTypes)}</div>
            </div>

            <div class="st-quick-detail-row">
                <div class="st-quick-detail-label">Tổng SL chuyển</div>
                <div class="st-quick-detail-value">${stockTransferFormatNumber(totalQty)}</div>
            </div>

            <div class="mt-3">
                <div class="text-muted mb-1">Ghi chú</div>
                <div>${stockTransferEscapeHtml(d.note || '---')}</div>
            </div>
        `;
    }
}

function getStockTransferTotalPages() {
    return Math.max(1, Math.ceil(stockTransferUiState.index.totalItems / (stockTransferUiState.index.pageSize || 20)));
}

function renderStockTransferPagination() {
    const info = document.getElementById('stPaginationInfo');
    const prev = document.getElementById('btnStPrevPage');
    const next = document.getElementById('btnStNextPage');

    const page = stockTransferUiState.index.page;
    const totalPages = getStockTransferTotalPages();

    if (info) info.textContent = `Trang ${page}/${totalPages} - ${stockTransferFormatNumber(stockTransferUiState.index.totalItems)} phiếu`;
    if (prev) prev.disabled = page <= 1;
    if (next) next.disabled = page >= totalPages;
}

async function loadTransferWarehouseOptionsForCreate() {
    const fromSelect = document.getElementById('createTransferFromWarehouseId');
    const toSelect = document.getElementById('createTransferToWarehouseId');

    if (!fromSelect || !toSelect) return;

    fromSelect.innerHTML = `<option value="">Đang tải kho...</option>`;
    toSelect.innerHTML = `<option value="">Đang tải kho...</option>`;

    const response = await fetch('/admin/api/warehouses/select2?term=');
    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        fromSelect.innerHTML = `<option value="">Không tải được kho</option>`;
        toSelect.innerHTML = `<option value="">Không tải được kho</option>`;
        showStockTransferToast('danger', 'Không tải được kho', api.data?.message || 'Vui lòng thử lại.');
        return;
    }

    const items = api.data?.results || [];
    const html = `<option value="">-- Chọn --</option>` +
        items.map(x => `<option value="${x.id}">${stockTransferEscapeHtml(x.text)}</option>`).join('');

    fromSelect.innerHTML = html;
    toSelect.innerHTML = html;
}

function resetCreateStockTransferForm() {
    const name = document.getElementById('createTransferDocumentName');
    const fromWarehouse = document.getElementById('createTransferFromWarehouseId');
    const toWarehouse = document.getElementById('createTransferToWarehouseId');
    const dateInput = document.getElementById('createTransferDocumentDate');
    const note = document.getElementById('createTransferNote');
    const msg = document.getElementById('createTransferMessage');

    if (name) name.value = '';
    if (fromWarehouse) fromWarehouse.value = '';
    if (toWarehouse) toWarehouse.value = '';
    if (note) note.value = '';
    if (msg) msg.textContent = '';

    if (dateInput) {
        const now = new Date();
        dateInput.value = new Date(now.getTime() - now.getTimezoneOffset() * 60000)
            .toISOString()
            .slice(0, 16);
    }
}

async function createStockTransferDocument() {
    const btn = document.getElementById('btnCreateStockTransfer');
    const documentName = document.getElementById('createTransferDocumentName')?.value?.trim() || '';
    const fromWarehouseId = document.getElementById('createTransferFromWarehouseId')?.value || '';
    const toWarehouseId = document.getElementById('createTransferToWarehouseId')?.value || '';
    const documentDate = document.getElementById('createTransferDocumentDate')?.value || '';
    const note = document.getElementById('createTransferNote')?.value || '';
    const msg = document.getElementById('createTransferMessage');

    if (msg) msg.textContent = '';

    if (!fromWarehouseId) {
        if (msg) msg.textContent = 'Vui lòng chọn kho nguồn.';
        return;
    }

    if (!toWarehouseId) {
        if (msg) msg.textContent = 'Vui lòng chọn kho đích.';
        return;
    }

    if (Number(fromWarehouseId) === Number(toWarehouseId)) {
        if (msg) msg.textContent = 'Kho nguồn và kho đích không được trùng nhau.';
        return;
    }

    if (btn) btn.disabled = true;

    const response = await fetch('/admin/api/stock-transfers', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            documentName,
            fromWarehouseId: Number(fromWarehouseId),
            toWarehouseId: Number(toWarehouseId),
            documentDate: documentDate ? new Date(documentDate).toISOString() : null,
            note
        })
    });

    const api = await readStockTransferApiResponse(response);
    if (btn) btn.disabled = false;

    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Tạo phiếu chuyển kho thất bại.';
        showStockTransferToast('danger', 'Tạo phiếu thất bại', api.data?.message || 'Vui lòng kiểm tra lại thông tin.');
        return;
    }

    createStockTransferModalInstance?.hide();
    showStockTransferToast('success', 'Tạo phiếu thành công', api.data?.message || 'Đang mở chi tiết phiếu.');
    window.location.href = `/admin/stock-transfers/${api.data.id}`;
}

/* =========================================================
   DETAIL
========================================================= */

function bindStockTransferDetailEvents() {
    document.getElementById('btnSubmitStockTransfer')?.addEventListener('click', function () {
        openStockTransferActionConfirm('Gửi duyệt phiếu chuyển kho', 'Phiếu sẽ chuyển sang trạng thái chờ duyệt.', submitStockTransferDocument);
    });

    document.getElementById('btnConfirmStockTransfer')?.addEventListener('click', function () {
        openStockTransferActionConfirm('Xác nhận phiếu chuyển kho', 'Sau khi xác nhận, hệ thống sẽ sinh ledger xuất kho nguồn và nhập kho đích.', confirmStockTransferDocument);
    });

    document.getElementById('btnRejectStockTransfer')?.addEventListener('click', function () {
        const reason = document.getElementById('RejectTransferReason');
        const msg = document.getElementById('RejectTransferMessage');

        if (reason) reason.value = '';
        if (msg) msg.textContent = '';

        rejectStockTransferModalInstance?.show();
        setTimeout(() => reason?.focus(), 150);
    });

    document.getElementById('btnConfirmRejectTransfer')?.addEventListener('click', rejectStockTransferDocument);
    document.getElementById('btnQuickAddTransferLine')?.addEventListener('click', quickAddStockTransferLine);
    document.getElementById('btnSaveEditTransferLine')?.addEventListener('click', saveEditStockTransferLine);
    document.getElementById('btnConfirmDeleteTransferLine')?.addEventListener('click', deleteStockTransferLineConfirmed);

    document.getElementById('btnConfirmStockTransferAction')?.addEventListener('click', async function () {
        if (typeof stockTransferUiState.pendingAction === 'function') {
            actionStockTransferModalInstance?.hide();
            await stockTransferUiState.pendingAction();
        }
    });

    document.getElementById('TransferFromWarehouseId')?.addEventListener('change', queueStockTransferHeaderSave);
    document.getElementById('TransferToWarehouseId')?.addEventListener('change', queueStockTransferHeaderSave);
    document.getElementById('TransferDocumentDate')?.addEventListener('change', queueStockTransferHeaderSave);
    document.getElementById('TransferDocumentNote')?.addEventListener('input', queueStockTransferHeaderSave);

    document.getElementById('QuickTransferQty')?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            quickAddStockTransferLine();
        }
    });

    document.getElementById('EditTransferQty')?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            saveEditStockTransferLine();
        }
    });
    document.getElementById('btnSaveTransferLineModal')
        ?.addEventListener('click', saveStockTransferLineModal);

    document.getElementById('TransferLineModalQty')
        ?.addEventListener('input', updateStockTransferLineModalPreview);

    document.getElementById('TransferLineModalQty')
        ?.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                saveStockTransferLineModal();
            }
        });
    document.getElementById('deleteStockTransferLineModal')
        ?.addEventListener('keydown', function (e) {

            if (e.key === 'Enter') {
                e.preventDefault();
                e.stopPropagation();

                deleteStockTransferLineConfirmed();
            }
        });
}

function openStockTransferActionConfirm(title, message, action) {
    const titleEl = document.getElementById('StockTransferActionTitle');
    const messageEl = document.getElementById('StockTransferActionMessage');

    if (titleEl) titleEl.textContent = title;
    if (messageEl) messageEl.textContent = message;

    stockTransferUiState.pendingAction = action;
    actionStockTransferModalInstance?.show();
}

async function initStockTransferWarehouseSelect() {
    const fromSelect = document.getElementById('TransferFromWarehouseId');
    const toSelect = document.getElementById('TransferToWarehouseId');

    if (!fromSelect || !toSelect) return;

    const response = await fetch('/admin/api/warehouses/select2?term=');
    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        fromSelect.innerHTML = `<option value="">Không tải được kho</option>`;
        toSelect.innerHTML = `<option value="">Không tải được kho</option>`;
        showStockTransferToast('danger', 'Không tải được kho', api.data?.message || 'Vui lòng thử lại.');
        return;
    }

    const items = api.data?.results || [];
    const html = `<option value="">-- Chọn kho --</option>` +
        items.map(x => `<option value="${x.id}">${stockTransferEscapeHtml(x.text)}</option>`).join('');

    fromSelect.innerHTML = html;
    toSelect.innerHTML = html;
}

async function loadStockTransferDetail() {
    const documentId = Number(window.stockTransferPage.stockTransferDocumentId || 0);
    if (!documentId) return;

    const response = await fetch(`/admin/api/stock-transfers/${documentId}`);
    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        showStockTransferToast('danger', 'Không tải được chi tiết', api.data?.message || 'Vui lòng quay lại danh sách.');
        return;
    }

    const detail = api.data;
    stockTransferUiState.currentDetail = detail;

    renderStockTransferDetailHeader(detail);
    renderStockTransferLines(detail.lines || []);
    applyStockTransferReadonlyState(detail.status);

    if (stockTransferEditable(detail.status)) {
        focusStockTransferProductInput(450);
    }
}

function renderStockTransferDetailHeader(detail) {
    const lines = detail.lines || [];
    const totalQty = lines.reduce((sum, x) => sum + Number(x.quantity || 0), 0);
    const productTypes = new Set(lines.map(x => x.productVariantId)).size;

    document.getElementById('TransferDocumentNo').value = detail.documentNo || '';
    document.getElementById('TransferDocumentDate').value = transferToLocalDateTime(detail.documentDate);
    document.getElementById('TransferFromWarehouseId').value = detail.fromWarehouseId || '';
    document.getElementById('TransferToWarehouseId').value = detail.toWarehouseId || '';
    document.getElementById('TransferDocumentNote').value = detail.note || '';
    document.getElementById('TransferStatusBadge').innerHTML = stockTransferStatusBadge(detail.status);

    const totalLinesEl = document.getElementById('sumTransferTotalLines');
    const docNoEl = document.getElementById('sumTransferDocumentNo');
    const productTypesEl = document.getElementById('sumTransferProductTypes');
    const totalQtyEl = document.getElementById('sumTransferTotalQty');
    const fromNameEl = document.getElementById('sumTransferFromWarehouse');
    const toNameEl = document.getElementById('sumTransferToWarehouse');

    if (totalLinesEl) totalLinesEl.textContent = stockTransferFormatNumber(lines.length);
    if (docNoEl) docNoEl.textContent = detail.documentNo || '---';
    if (productTypesEl) productTypesEl.textContent = stockTransferFormatNumber(productTypes);
    if (totalQtyEl) totalQtyEl.textContent = stockTransferFormatNumber(totalQty);
    if (fromNameEl) fromNameEl.textContent = detail.fromWarehouseName || '---';
    if (toNameEl) toNameEl.textContent = detail.toWarehouseName || '---';
}

function applyStockTransferReadonlyState(status) {
    const isEditable = stockTransferEditable(status);
    const isPending = Number(status) === 1;
    const isDraftOrRejected = Number(status) === 0 || Number(status) === 2;

    const canUpdate = window.stockTransferPage.permissions?.canUpdate ?? true;
    const canApprove = window.stockTransferPage.permissions?.canApprove ?? true;
    const canConfirm = window.stockTransferPage.permissions?.canConfirm ?? true;

    const realEditable = isEditable && canUpdate;

    document.getElementById('TransferFromWarehouseId').disabled = !realEditable;
    document.getElementById('TransferToWarehouseId').disabled = !realEditable;
    document.getElementById('TransferDocumentDate').disabled = !realEditable;
    document.getElementById('TransferDocumentNote').disabled = !realEditable;

    const qty = document.getElementById('QuickTransferQty');
    const addBtn = document.getElementById('btnQuickAddTransferLine');

    if (qty) qty.disabled = !realEditable;
    if (addBtn) addBtn.disabled = !realEditable;

    const quickLookup = $('#QuickTransferLookupInput');
    if (quickLookup.length) quickLookup.prop('disabled', !realEditable);

    const submitBtn = document.getElementById('btnSubmitStockTransfer');
    const confirmBtn = document.getElementById('btnConfirmStockTransfer');
    const rejectBtn = document.getElementById('btnRejectStockTransfer');

    if (submitBtn) submitBtn.disabled = !(isDraftOrRejected && canUpdate);
    if (confirmBtn) confirmBtn.disabled = !(isPending && canConfirm);
    if (rejectBtn) rejectBtn.disabled = !(isPending && canApprove);
}

function queueStockTransferHeaderSave() {
    if (stockTransferHeaderSaveTimer) clearTimeout(stockTransferHeaderSaveTimer);
    stockTransferHeaderSaveTimer = setTimeout(() => saveStockTransferHeader(false), 600);
}

async function saveStockTransferHeader(showToast) {
    const stockTransferDocumentId = Number(document.getElementById('StockTransferDocumentId')?.value || 0);
    const fromWarehouseId = Number(document.getElementById('TransferFromWarehouseId')?.value || 0);
    const toWarehouseId = Number(document.getElementById('TransferToWarehouseId')?.value || 0);
    const documentDate = document.getElementById('TransferDocumentDate')?.value || '';
    const note = document.getElementById('TransferDocumentNote')?.value || '';

    if (!stockTransferDocumentId || !fromWarehouseId || !toWarehouseId) return;

    const response = await fetch(`/admin/api/stock-transfers/${stockTransferDocumentId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            fromWarehouseId,
            toWarehouseId,
            documentDate: documentDate ? new Date(documentDate).toISOString() : null,
            note
        })
    });

    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        showStockTransferToast('danger', 'Lưu phiếu thất bại', api.data?.message || 'Không thể lưu thông tin phiếu.');
        return;
    }

    if (showToast) {
        showStockTransferToast('success', 'Đã lưu phiếu', api.data?.message || 'Thông tin phiếu chuyển kho đã được lưu.');
    }
}

/* =========================================================
   SELECT2 LOOKUP
========================================================= */

function initStockTransferQuickLookupSelect2() {
    const el = $('#QuickTransferLookupInput');
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
        templateResult: formatStockTransferLookupResult,
        templateSelection: formatStockTransferLookupSelection,
        escapeMarkup: function (markup) {
            return markup;
        }
    });

    el.on('select2:select', function (e) {
        const item = e.params.data;
        el.data('selected-item', item);
        openStockTransferLineModalForAdd(item);
    });

    el.on('select2:clear', function () {
        el.removeData('selected-item');
    });

    bindStockTransferLookupEnterAutoSelect();
}
async function openStockTransferLineModalForAdd(item) {
    $('#QuickTransferLookupInput').select2('close');

    document.getElementById('TransferLineModalMode').value = 'add';
    document.getElementById('TransferLineModalLineId').value = '';
    document.getElementById('TransferLineModalVariantId').value = item.productVariantId || '';
    document.getElementById('TransferLineModalQty').value = '1';
    document.getElementById('TransferLineModalNote').value = '';
    document.getElementById('TransferLineModalMessage').textContent = '';

    setTransferLineModalProductInfo({
        productName: item.productName || item.text || '-',
        sku: item.sku || '-',
        barcode: item.barcode || '-',
        unitName: item.unitName || '-',
        factor: item.factor || 1,
        imageUrl: item.imageUrl || ''
    });

    await loadStockTransferLineModalUnits(item.productVariantId, item.unitId);

    updateStockTransferLineModalPreview();

    stockTransferLineModalInstance?.show();
    focusTransferLineModalQty();
}

async function openStockTransferLineModalForEdit(btn) {
    const lineId = btn.dataset.lineId || '';
    const variantId = btn.dataset.variantId || '';
    const unitId = btn.dataset.unitId || '';
    const qty = btn.dataset.qty || '1';
    const note = btn.dataset.note || '';

    const row = btn.closest('tr');
    const productName = row?.querySelector('.st-product-name')?.textContent || '-';
    const barcode = row?.querySelector('.st-barcode')?.textContent?.replace('Barcode:', '').trim() || '-';
    const imageUrl =
        btn.dataset.imageUrl ||
        row?.querySelector('.st-product-img')?.getAttribute('src') ||
        '';

    document.getElementById('TransferLineModalMode').value = 'edit';
    document.getElementById('TransferLineModalLineId').value = lineId;
    document.getElementById('TransferLineModalVariantId').value = variantId;
    document.getElementById('TransferLineModalQty').value = qty;
    document.getElementById('TransferLineModalNote').value = note;
    document.getElementById('TransferLineModalMessage').textContent = '';

    document.getElementById('TransferLineModalTitle').textContent = 'Sửa số lượng chuyển kho';
    document.getElementById('TransferLineModalSubtitle').textContent = 'Nhập số lượng rồi nhấn Enter để lưu.';

    setTransferLineModalProductInfo({
        productName,
        barcode,
        unitName: '-',
        factor: 1,
        imageUrl
    });

    await loadStockTransferLineModalUnits(variantId, unitId);

    updateStockTransferLineModalPreview();

    stockTransferLineModalInstance?.show();
    focusTransferLineModalQty();
}

function setTransferLineModalProductInfo(item) {
    document.getElementById('TransferLineModalProductName').textContent = item.productName || '-';
    document.getElementById('TransferLineModalBarcode').textContent = item.barcode || '-';
    document.getElementById('TransferLineModalUnitName').textContent = item.unitName || '-';
    document.getElementById('TransferLineModalFactor').textContent = stockTransferFormatNumber(item.factor || 1);

    const img = document.getElementById('TransferLineModalProductImage');
    const noImg = document.getElementById('TransferLineModalNoImage');

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

function focusTransferLineModalQty() {
    setTimeout(function () {
        const qty = document.getElementById('TransferLineModalQty');
        qty?.focus();
        qty?.select();
    }, 350);
}

async function loadStockTransferLineModalUnits(variantId, selectedUnitId) {
    const select = document.getElementById('TransferLineModalUnitSelect');
    const info = document.getElementById('TransferLineModalUnitInfo');

    select.innerHTML = `<option value="">Đang tải...</option>`;
    if (info) info.textContent = '';

    const response = await fetch(`/admin/api/stock-documents/product-variants/${variantId}/units`);
    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        select.innerHTML = `<option value="">Không tải được đơn vị</option>`;
        if (info) info.textContent = api.data?.message || '';
        return;
    }

    const items = api.data || [];

    select.innerHTML = items.map(x => {
        const selected = Number(selectedUnitId) === Number(x.unitId) ? 'selected' : '';
        return `
            <option value="${x.unitId}"
                    data-factor="${x.factor}"
                    data-unit-name="${stockTransferEscapeHtml(x.unitName)}"
                    ${selected}>
                ${stockTransferEscapeHtml(x.unitName)} (x${x.factor})
            </option>
        `;
    }).join('');

    updateStockTransferLineModalUnitInfo();

    select.onchange = function () {
        updateStockTransferLineModalUnitInfo();
        updateStockTransferLineModalPreview();
    };
}

function updateStockTransferLineModalUnitInfo() {
    const select = document.getElementById('TransferLineModalUnitSelect');
    const info = document.getElementById('TransferLineModalUnitInfo');
    const option = select?.options[select.selectedIndex];

    if (!option || !option.value) {
        if (info) info.textContent = '';
        return;
    }

    const factor = Number(option.getAttribute('data-factor') || 1);
    const unitName = option.getAttribute('data-unit-name') || option.textContent || '-';

    document.getElementById('TransferLineModalUnitName').textContent = unitName.replace(/\(x.*?\)/, '').trim();
    document.getElementById('TransferLineModalFactor').textContent = stockTransferFormatNumber(factor);

    if (info) {
        info.textContent = `Hệ số quy đổi về đơn vị gốc: x${stockTransferFormatNumber(factor)}`;
    }
}

function updateStockTransferLineModalPreview() {
    const qty = Number(document.getElementById('TransferLineModalQty')?.value || 0);
    const select = document.getElementById('TransferLineModalUnitSelect');
    const option = select?.options[select.selectedIndex];

    const factor = Number(option?.getAttribute('data-factor') || 1);
    const baseQty = qty * factor;

    document.getElementById('TransferLineModalQtyPreview').textContent = stockTransferFormatNumber(qty);
    document.getElementById('TransferLineModalBaseQtyPreview').textContent = stockTransferFormatNumber(baseQty);
    document.getElementById('TransferLineModalFactor').textContent = stockTransferFormatNumber(factor);
}

async function saveStockTransferLineModal() {
    const mode = document.getElementById('TransferLineModalMode')?.value || 'add';
    const stockTransferDocumentId = Number(document.getElementById('StockTransferDocumentId')?.value || 0);
    const lineId = Number(document.getElementById('TransferLineModalLineId')?.value || 0);
    const productVariantId = Number(document.getElementById('TransferLineModalVariantId')?.value || 0);
    const unitId = Number(document.getElementById('TransferLineModalUnitSelect')?.value || 0);
    const quantity = Number(document.getElementById('TransferLineModalQty')?.value || 0);
    const note = document.getElementById('TransferLineModalNote')?.value || '';
    const msg = document.getElementById('TransferLineModalMessage');

    if (msg) msg.textContent = '';

    if (!unitId) {
        if (msg) msg.textContent = 'Vui lòng chọn đơn vị.';
        return;
    }

    if (quantity <= 0) {
        if (msg) msg.textContent = 'Số lượng chuyển phải lớn hơn 0.';
        return;
    }

    let url = '';
    let method = '';
    let body = {};

    if (mode === 'add') {
        url = `/admin/api/stock-transfers/${stockTransferDocumentId}/lines`;
        method = 'POST';
        body = { productVariantId, unitId, quantity, note };
    } else {
        url = `/admin/api/stock-transfers/lines/${lineId}`;
        method = 'PUT';
        body = { unitId, quantity, note };
    }

    const response = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
    });

    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Không thể lưu dòng chuyển kho.';
        showStockTransferToast('danger', 'Lưu dòng thất bại', api.data?.message || 'Không thể lưu dòng chuyển kho.');
        return;
    }

    stockTransferLineModalInstance?.hide();

    $('#QuickTransferLookupInput').val(null).trigger('change');
    $('#QuickTransferLookupInput').removeData('selected-item');

    resetStockTransferQuickInput();

    showStockTransferToast('success', 'Đã lưu dòng chuyển kho', api.data?.message || 'Dòng chuyển kho đã được cập nhật.');

    await loadStockTransferDetail();
    focusStockTransferProductInput(650);
}
function resetStockTransferQuickInput() {
    $('#QuickTransferLookupInput').val(null).trigger('change');
    $('#QuickTransferLookupInput').removeData('selected-item');

    const variant = document.getElementById('SelectedTransferProductVariantId');
    const unit = document.getElementById('SelectedTransferUnitId');
    const factor = document.getElementById('SelectedTransferFactor');
    const qty = document.getElementById('QuickTransferQty');

    if (variant) variant.value = '';
    if (unit) unit.value = '';
    if (factor) factor.value = '';
    if (qty) qty.value = '1';
}
function formatStockTransferLookupResult(item) {
    if (!item.id) return item.text || '';

    const title = stockTransferEscapeHtml(item.productName || item.text || '');

    const img = item.imageUrl
        ? `<img src="${stockTransferEscapeHtml(item.imageUrl)}" class="select2-result-img" alt="">`
        : `<div class="select2-result-img d-flex align-items-center justify-content-center"><i class="bx bx-package"></i></div>`;

    const meta = [
        item.sku ? `SKU: ${stockTransferEscapeHtml(item.sku)}` : '',
        item.unitName ? `Đơn vị: ${stockTransferEscapeHtml(item.unitName)}` : '',
        item.factor ? `x${item.factor}` : '',
        item.barcode ? `BC: ${stockTransferEscapeHtml(item.barcode)}` : '',
        item.sourceType ? stockTransferEscapeHtml(item.sourceType) : ''
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

function formatStockTransferLookupSelection(item) {
    if (!item || !item.id) return item.text || 'Chọn sản phẩm';

    const parts = [];

    if (item.productName) parts.push(item.productName);
    if (item.unitName) parts.push(item.unitName);
    if (item.factor) parts.push('x' + item.factor);
    if (item.barcode) parts.push(item.barcode);

    return stockTransferEscapeHtml(parts.join(' | ') || item.text || '');
}

function bindStockTransferLookupEnterAutoSelect() {
    $(document).off('keydown.stockTransferLookup');

    $(document).on('keydown.stockTransferLookup', '.select2-container--open .select2-search__field', async function (e) {
        if (e.key !== 'Enter') return;

        const term = $(this).val();
        if (!term) return;

        const response = await fetch(`/admin/api/inventory-adjustments/product-lookup-select2?term=${encodeURIComponent(term)}`);
        const api = await readStockTransferApiResponse(response);

        if (!api.ok || !api.data?.results || api.data.results.length !== 1) return;

        e.preventDefault();

        const item = api.data.results[0];
        const el = $('#QuickTransferLookupInput');
        const option = new Option(item.text, item.id, true, true);

        el.append(option).trigger('change');
        el.trigger({
            type: 'select2:select',
            params: { data: item }
        });

        el.select2('close');
    });
}

async function quickAddStockTransferLine() {
    const stockTransferDocumentId = Number(document.getElementById('StockTransferDocumentId')?.value || 0);
    const productVariantId = Number(document.getElementById('SelectedTransferProductVariantId')?.value || 0);
    const unitId = Number(document.getElementById('SelectedTransferUnitId')?.value || 0);
    const quantity = Number(document.getElementById('QuickTransferQty')?.value || 0);
    const selectedItem = $('#QuickTransferLookupInput').data('selected-item');

    if (!productVariantId || !unitId) {
        showStockTransferToast('warning', 'Chưa chọn sản phẩm', 'Vui lòng quét barcode hoặc chọn sản phẩm trước.');
        focusStockTransferProductInput(250);
        return;
    }

    if (quantity <= 0) {
        showStockTransferToast('warning', 'Số lượng không hợp lệ', 'Số lượng chuyển phải lớn hơn 0.');
        const qty = document.getElementById('QuickTransferQty');
        qty?.focus();
        qty?.select();
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

    const btn = document.getElementById('btnQuickAddTransferLine');
    if (btn) btn.disabled = true;

    const response = await fetch(`/admin/api/stock-transfers/${stockTransferDocumentId}/lines`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            productVariantId,
            unitId,
            quantity,
            note
        })
    });

    const api = await readStockTransferApiResponse(response);
    if (btn) btn.disabled = false;

    if (!api.ok) {
        showStockTransferToast('danger', 'Thêm dòng thất bại', api.data?.message || 'Không thể thêm dòng chuyển kho.');
        focusStockTransferProductInput(350);
        return;
    }

    $('#QuickTransferLookupInput').val(null).trigger('change');
    $('#QuickTransferLookupInput').removeData('selected-item');

    document.getElementById('SelectedTransferProductVariantId').value = '';
    document.getElementById('SelectedTransferUnitId').value = '';
    document.getElementById('SelectedTransferFactor').value = '';
    document.getElementById('QuickTransferQty').value = '1';

    showStockTransferToast('success', 'Đã thêm sản phẩm', api.data?.message || 'Dòng chuyển kho đã được cập nhật.');

    await loadStockTransferDetail();
    focusStockTransferProductInput(650);
}

function renderStockTransferLines(lines) {
    const body = document.getElementById('stockTransferLineTableBody');
    if (!body) return;

    if (!lines.length) {
        body.innerHTML = `<tr><td colspan="9" class="text-center text-muted py-4">Chưa có dòng chuyển kho.</td></tr>`;
        return;
    }

    const editable = stockTransferEditable(stockTransferUiState.currentDetail?.status);
    const canUpdate = window.stockTransferPage.permissions?.canUpdate ?? true;
    const canDelete = window.stockTransferPage.permissions?.canDelete ?? true;

    body.innerHTML = lines.map(line => {
        const imageUrl = line.imageUrl || line.ImageUrl || line.productImageUrl || line.thumbnailUrl || '';

        return `
            <tr>
                <td>${stockTransferFormatNumber(line.lineNo)}</td>

               <td>
    <div class="st-product-cell">
        ${renderStockTransferProductImage(imageUrl)}
        <div>
            <div class="st-product-name">${stockTransferEscapeHtml(line.productNameSnapshot || '')}</div>
            <div class="text-muted small">SKU: ${stockTransferEscapeHtml(line.skuSnapshot || '---')}</div>
            <div class="st-barcode">Barcode: ${stockTransferEscapeHtml(line.barcodeSnapshot || '---')}</div>
        </div>
    </div>
</td>

                <td class="st-mobile-hide">${stockTransferEscapeHtml(line.barcodeSnapshot || '---')}</td>
                <td>${stockTransferEscapeHtml(line.unitNameSnapshot || '')}</td>
                <td class="text-end st-mobile-hide">${stockTransferFormatNumber(line.factor)}</td>
                <td class="text-end fw-bold">${stockTransferFormatNumber(line.quantity)}</td>
                <td class="text-end st-mobile-hide">${stockTransferFormatNumber(line.baseQuantity)}</td>
                <td class="st-mobile-hide"><div class="st-note">${stockTransferEscapeHtml(line.note || '')}</div></td>

                <td class="text-end">
                    <div class="btn-group">
                        <button type="button"
                                class="btn btn-sm btn-warning btn-edit-stock-transfer-line"
                                data-line-id="${line.id}"
                                data-variant-id="${line.productVariantId}"
                                data-image-url="${stockTransferEscapeHtml(imageUrl)}"
                                data-unit-id="${line.unitId}"
                                data-qty="${line.quantity}"
                                data-note="${stockTransferEscapeHtml(line.note || '')}"
                                ${editable && canUpdate ? '' : 'disabled'}>
                            Sửa
                        </button>

                        <button type="button"
                                class="btn btn-sm btn-danger btn-delete-stock-transfer-line"
                                data-line-id="${line.id}"
                                data-product-name="${stockTransferEscapeHtml(line.productNameSnapshot || '')}"
                                ${editable && canDelete ? '' : 'disabled'}>
                            Xóa
                        </button>
                    </div>
                </td>
            </tr>
        `;
    }).join('');

    bindEditStockTransferLineButtons();
    bindDeleteStockTransferLineButtons();
}

function renderStockTransferProductImage(imageUrl) {
    if (!imageUrl) {
        return `
            <div class="st-product-img-placeholder">
                <i class="bx bx-package"></i>
            </div>
        `;
    }

    return `
        <img src="${stockTransferEscapeHtml(imageUrl)}"
             class="st-product-img"
             loading="lazy"
             onerror="this.outerHTML='<div class=&quot;st-product-img-placeholder&quot;><i class=&quot;bx bx-package&quot;></i></div>';" />
    `;
}

function bindEditStockTransferLineButtons() {
    document.querySelectorAll('.btn-edit-stock-transfer-line').forEach(btn => {
        btn.onclick = async function () {
            await openStockTransferLineModalForEdit(this);
        };
    });
}

async function loadStockTransferLineUnits(variantId, selectedUnitId) {
    const select = document.getElementById('EditTransferLineUnitId');
    const info = document.getElementById('EditTransferUnitInfo');

    if (!select) return;

    select.innerHTML = `<option value="">Đang tải...</option>`;
    if (info) info.textContent = '';

    const response = await fetch(`/admin/api/stock-documents/product-variants/${variantId}/units`);
    const api = await readStockTransferApiResponse(response);

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
        return `
            <option value="${x.unitId}" data-factor="${x.factor}" ${selected}>
                ${stockTransferEscapeHtml(x.unitName)} (x${stockTransferFormatNumber(x.factor)})
            </option>
        `;
    }).join('');

    updateStockTransferEditUnitInfo();
    select.onchange = updateStockTransferEditUnitInfo;
}

function updateStockTransferEditUnitInfo() {
    const select = document.getElementById('EditTransferLineUnitId');
    const info = document.getElementById('EditTransferUnitInfo');

    if (!select || !info) return;

    const option = select.options[select.selectedIndex];

    if (!option || !option.value) {
        info.textContent = '';
        return;
    }

    const factor = option.getAttribute('data-factor') || '1';
    info.textContent = `Hệ số quy đổi về đơn vị gốc: x${stockTransferFormatNumber(factor)}`;
}

async function saveEditStockTransferLine() {
    const lineId = Number(document.getElementById('EditTransferLineId')?.value || 0);
    const unitId = Number(document.getElementById('EditTransferLineUnitId')?.value || 0);
    const quantity = Number(document.getElementById('EditTransferQty')?.value || 0);
    const note = document.getElementById('EditTransferLineNote')?.value || '';
    const msg = document.getElementById('EditTransferLineMessage');

    if (msg) msg.textContent = '';

    if (!lineId || !unitId) {
        if (msg) msg.textContent = 'Thông tin dòng không hợp lệ.';
        return;
    }

    if (quantity <= 0) {
        if (msg) msg.textContent = 'Số lượng phải lớn hơn 0.';
        return;
    }

    const response = await fetch(`/admin/api/stock-transfers/lines/${lineId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            unitId,
            quantity,
            note
        })
    });

    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Cập nhật dòng thất bại.';
        showStockTransferToast('danger', 'Cập nhật thất bại', api.data?.message || 'Không thể lưu dòng chuyển kho.');
        return;
    }

    editStockTransferLineModalInstance?.hide();

    showStockTransferToast('success', 'Đã cập nhật dòng', api.data?.message || 'Dòng chuyển kho đã được lưu.');
    await loadStockTransferDetail();
    focusStockTransferProductInput(650);
}

function bindDeleteStockTransferLineButtons() {
    document.querySelectorAll('.btn-delete-stock-transfer-line').forEach(btn => {
        btn.onclick = function () {
            const lineId = this.dataset.lineId || '';
            const productName = this.dataset.productName || 'Dòng chuyển kho';

            const idEl = document.getElementById('DeleteTransferLineId');
            const nameEl = document.getElementById('DeleteTransferLineName');

            if (idEl) idEl.value = lineId;
            if (nameEl) nameEl.textContent = productName;

            deleteStockTransferLineModalInstance?.show();
            setTimeout(() => {
                document.getElementById('btnConfirmDeleteTransferLine')?.focus();
            }, 120);
        };
    });
}

async function deleteStockTransferLineConfirmed() {

    const btn = document.getElementById('btnConfirmDeleteTransferLine');

    if (btn?.disabled) {
        return;
    }

    const lineId = document.getElementById('DeleteTransferLineId')?.value || '';

    if (!lineId) {
        return;
    }

    if (btn) {
        btn.disabled = true;
    }

    const response = await fetch(`/admin/api/stock-transfers/lines/${lineId}`, {
        method: 'DELETE'
    });

    const api = await readStockTransferApiResponse(response);

    if (btn) {
        btn.disabled = false;
    }

    if (!api.ok) {

        showStockTransferToast(
            'danger',
            'Xóa dòng thất bại',
            api.data?.message || 'Không thể xóa dòng chuyển kho.'
        );

        return;
    }

    deleteStockTransferLineModalInstance?.hide();

    showStockTransferToast(
        'success',
        'Đã xóa dòng',
        api.data?.message || 'Dòng chuyển kho đã được xóa.'
    );

    await loadStockTransferDetail();

    focusStockTransferProductInput(650);
}

/* =========================================================
   DOCUMENT ACTIONS
========================================================= */

async function submitStockTransferDocument() {
    const stockTransferDocumentId = Number(document.getElementById('StockTransferDocumentId')?.value || 0);
    if (!stockTransferDocumentId) return;

    const response = await fetch(`/admin/api/stock-transfers/${stockTransferDocumentId}/submit`, {
        method: 'POST'
    });

    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        showStockTransferToast('danger', 'Gửi duyệt thất bại', api.data?.message || 'Không thể gửi duyệt phiếu.');
        return;
    }

    showStockTransferToast('success', 'Đã gửi duyệt', api.data?.message || 'Phiếu đã chuyển sang chờ duyệt.');
    await loadStockTransferDetail();
}

async function confirmStockTransferDocument() {
    const stockTransferDocumentId = Number(document.getElementById('StockTransferDocumentId')?.value || 0);
    if (!stockTransferDocumentId) return;

    const response = await fetch(`/admin/api/stock-transfers/${stockTransferDocumentId}/confirm`, {
        method: 'POST'
    });

    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        showStockTransferToast('danger', 'Xác nhận thất bại', api.data?.message || 'Không thể xác nhận phiếu.');
        return;
    }

    showStockTransferToast('success', 'Đã xác nhận', api.data?.message || 'Phiếu chuyển kho đã được xác nhận.');
    await loadStockTransferDetail();
}

async function rejectStockTransferDocument() {
    const stockTransferDocumentId = Number(document.getElementById('StockTransferDocumentId')?.value || 0);
    const reason = document.getElementById('RejectTransferReason')?.value || '';
    const msg = document.getElementById('RejectTransferMessage');

    if (!stockTransferDocumentId) return;
    if (msg) msg.textContent = '';

    const response = await fetch(`/admin/api/stock-transfers/${stockTransferDocumentId}/reject`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason })
    });

    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Từ chối phiếu chuyển kho thất bại.';
        showStockTransferToast('danger', 'Từ chối thất bại', api.data?.message || 'Không thể từ chối phiếu.');
        return;
    }

    rejectStockTransferModalInstance?.hide();

    showStockTransferToast('success', 'Đã từ chối phiếu', api.data?.message || 'Phiếu đã chuyển sang trạng thái từ chối.');
    await loadStockTransferDetail();
}