let createReceiptModalInstance = null;
let editLineModalInstance = null;
let submitApprovalModalInstance = null;
let approveModalInstance = null;
let rejectModalInstance = null;
let quickAddProductModalInstance = null;

let supplierLookupTimer = null;
let editHeaderSaveTimer = null;
let suppressEditHeaderSave = false;
let mapInputInvoiceLineModalInstance = null;
let cachedInputInvoices = [];
let selectedInputInvoiceDetailId = null;
let selectedInputInvoiceDetail = null;
let itemCatalogDetailOptions = [];
let quickEditLineModalInstance = null;
let deleteLineModalInstance = null;
let returnToEditModalInstance = null;
let cachedReceiptFormOptions = null;
let linkedInputInvoicePreviewModalInstance = null;
let unlinkInputInvoiceModalInstance = null;
let linkedInputInvoicePreviewObjectUrl = null;
let pendingUnlinkInputInvoice = null;
let cachedInputInvoiceAssociation = null;
let inputInvoiceAssociationGeneration = 0;

function invalidateInputInvoiceAssociation() {
    inputInvoiceAssociationGeneration += 1;
    cachedInputInvoiceAssociation = null;
}
let inputInvoiceAssociationReturnFocus = null;
let inputInvoiceReconciliationReasonModalInstance = null;
let pendingReconciliationReasonResolve = null;
let inputInvoiceReconciliationReasonReturnFocus = null;
let cachedInputInvoiceReconciliation = null;
let completeCatalogProductModalInstance = null;

function normalizeStockDocumentRowVersion(value) {
    if (typeof value !== 'string') return null;
    const normalized = value.trim();
    if (!normalized) return null;

    try {
        if (window.atob(normalized).length !== 8) return null;
    } catch {
        return null;
    }

    return normalized;
}

window.stockDocumentRowVersion = {
    current: function () {
        return normalizeStockDocumentRowVersion(window.stockDocumentPage?.rowVersion) ||
            normalizeStockDocumentRowVersion(
                document.getElementById('provisionalReceivingPanel')?.dataset.documentRowVersion);
    },
    update: function (value) {
        const normalized = normalizeStockDocumentRowVersion(value);
        if (!normalized) return false;

        if (window.stockDocumentPage) window.stockDocumentPage.rowVersion = normalized;
        const provisionalPanel = document.getElementById('provisionalReceivingPanel');
        if (provisionalPanel) provisionalPanel.dataset.documentRowVersion = normalized;
        return true;
    }
};
let inputInvoiceReconciliationRenderGeneration = 0;

function beginInputInvoiceReconciliationRenderRequest() {
    inputInvoiceReconciliationRenderGeneration += 1;
    return inputInvoiceReconciliationRenderGeneration;
}

function renderInputInvoiceReconciliationIfCurrent(model, requestGeneration, commercialPreview) {
    if (!model || requestGeneration !== inputInvoiceReconciliationRenderGeneration) return false;
    if (!commercialPreview) cachedInputInvoiceReconciliation = model;
    renderInputInvoiceReconciliation(commercialPreview
        ? { ...model, isCommercialPreview: true }
        : model);
    return true;
}

window.beginInputInvoiceReconciliationRenderRequest =
    beginInputInvoiceReconciliationRenderRequest;
window.renderInputInvoiceReconciliationIfCurrent =
    renderInputInvoiceReconciliationIfCurrent;

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
        bindManagerOutsidePoActions();
        initLegacyCatalogReview();
        initStockDocumentWorkbenchTabs();

        bindDeleteLine();
        bindOpenEditLineModal();
        bindSaveEditLine();
        bindEditUnitChange();

        loadInputInvoicesForStockDocument();
        bindLinkedInputInvoiceActions();
        bindInputInvoiceReconciliationActions();

        bindOpenMapInputInvoiceLineModal();
        bindSaveInputInvoiceLineMap();
        initItemCatalogMappingControls();
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

function activateStockDocumentWorkbenchTab(tabName, remember = true) {
    const workbench = document.getElementById('commercialApprovalWorkbench');
    if (!workbench || typeof workbench.querySelectorAll !== 'function') return false;
    const buttons = Array.from(workbench.querySelectorAll('[data-workbench-tab]'));
    if (!buttons.some(button => button.dataset.workbenchTab === tabName)) tabName = 'goods';

    buttons.forEach(button => {
        const active = button.dataset.workbenchTab === tabName;
        button.classList.toggle('active', active);
        button.setAttribute('aria-selected', active ? 'true' : 'false');
        button.tabIndex = active ? 0 : -1;
    });
    workbench.querySelectorAll('[data-workbench-panel]').forEach(panel => {
        panel.hidden = panel.dataset.workbenchPanel !== tabName;
    });
    if (remember) {
        sessionStorage.setItem(
            `stockDocumentWorkbenchTab:${window.stockDocumentPage?.documentId || 0}`,
            tabName);
    }
    return true;
}

window.activateStockDocumentWorkbenchTab = activateStockDocumentWorkbenchTab;

function initStockDocumentWorkbenchTabs() {
    const workbench = document.getElementById('commercialApprovalWorkbench');
    if (!workbench) return;
    const storageKey = `stockDocumentWorkbenchTab:${window.stockDocumentPage?.documentId || 0}`;
    const requestedTab = new URLSearchParams(window.location.search).get('tab');
    activateStockDocumentWorkbenchTab(requestedTab || sessionStorage.getItem(storageKey) || 'goods', false);
    if (requestedTab === 'xml') {
        const invoiceSection = document.querySelector('.sd-optional-xml');
        if (invoiceSection) {
            invoiceSection.open = true;
            requestAnimationFrame(() => invoiceSection.scrollIntoView({ block: 'start' }));
        }
    }

    workbench.addEventListener('click', event => {
        const button = event.target.closest?.('[data-workbench-tab]');
        if (!button) return;
        activateStockDocumentWorkbenchTab(button.dataset.workbenchTab);
    });
    workbench.addEventListener('keydown', event => {
        const button = event.target.closest?.('[data-workbench-tab]');
        if (!button || !['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
        const buttons = Array.from(workbench.querySelectorAll('[data-workbench-tab]'));
        const current = buttons.indexOf(button);
        const direction = event.key === 'ArrowRight' ? 1 : -1;
        const next = buttons[(current + direction + buttons.length) % buttons.length];
        event.preventDefault();
        activateStockDocumentWorkbenchTab(next.dataset.workbenchTab);
        next.focus();
    });
}

window.initStockDocumentWorkbenchTabs = initStockDocumentWorkbenchTabs;

function bindManagerOutsidePoActions() {
    const buttons = document.querySelectorAll('.js-manager-outside-decision');
    if (!buttons.length) return;

    const token = document.querySelector(
        '#managerOutsidePoAntiforgery input[name="__RequestVerificationToken"]');

    buttons.forEach(function (button) {
        button.addEventListener('click', async function () {
            const documentId = Number(window.stockDocumentPage?.documentId || 0);
            const lineId = Number(button.dataset.lineId || 0);
            const accept = button.dataset.accept === 'true';
            const card = button.closest('[data-outside-line-id]');
            const message = card?.querySelector('.js-manager-outside-message');
            const cardButtons = card?.querySelectorAll('.js-manager-outside-decision') || [];

            if (!documentId || !lineId) return;
            if (message) {
                message.textContent = '';
                message.classList.add('d-none');
            }
            cardButtons.forEach(function (item) { item.disabled = true; });

            try {
                const rowVersion = window.stockDocumentRowVersion.current();
                if (!rowVersion) {
                    throw new Error('Phiên bản phiếu trên trang không hợp lệ. Vui lòng tải lại.');
                }
                const response = await fetch(
                    `/admin/purchase-receiving/${documentId}/outside/${lineId}`,
                    {
                        method: 'POST',
                        credentials: 'same-origin',
                        headers: {
                            'Content-Type': 'application/json',
                            'RequestVerificationToken': token?.value || ''
                        },
                        body: JSON.stringify({
                            rowVersion: rowVersion,
                            accept: accept
                        })
                    });
                const api = await readApiResponse(response);
                if (!api.ok) {
                    throw new Error(api.data?.message || 'Không thể xử lý hàng ngoài PO.');
                }
                if (!window.stockDocumentRowVersion.update(api.data?.rowVersion)) {
                    throw new Error('Máy chủ không trả về phiên bản phiếu hợp lệ. Vui lòng tải lại.');
                }
                window.location.reload();
            } catch (error) {
                if (message) {
                    message.textContent = error.message || 'Không thể xử lý hàng ngoài PO.';
                    message.classList.remove('d-none');
                    message.focus?.();
                }
                cardButtons.forEach(function (item) { item.disabled = false; });
            }
        });
    });
}

function initLegacyCatalogReview() {
    const modal = document.getElementById('completeCatalogProductModal');
    if (modal && window.bootstrap) {
        completeCatalogProductModalInstance =
            bootstrap.Modal.getOrCreateInstance(modal);
    }

    document.addEventListener('click', async function (event) {
        const filter = event.target.closest?.('[data-commercial-filter]');
        if (filter) {
            const value = filter.dataset.commercialFilter || 'all';
            document.querySelectorAll('[data-commercial-filter]').forEach(button =>
                button.classList.toggle('active', button === filter));
            document.querySelectorAll('.commercial-line').forEach(row => {
                const visible = value === 'all' ||
                    (value === 'catalog' && row.dataset.catalogReview === 'true') ||
                    (value === 'price' && row.dataset.priceMissing === 'true') ||
                    (value === 'exception' && row.dataset.exception === 'true');
                row.classList.toggle('d-none', !visible);
            });
            return;
        }

        const trigger = event.target.closest?.('.js-complete-catalog-product');
        if (trigger) {
            document.getElementById('catalogReviewLineId').value = trigger.dataset.lineId || '';
            document.getElementById('catalogReviewLegacyName').textContent =
                trigger.dataset.legacyName || '—';
            document.getElementById('catalogReviewIdentity').textContent =
                `Variant #${trigger.dataset.variantId || '—'} · SKU ${trigger.dataset.sku || '—'} · ` +
                `Mã vạch ${trigger.dataset.barcode || '—'}`;
            document.getElementById('catalogReviewProductName').value = '';
            document.getElementById('catalogReviewMessage').textContent = '';
            completeCatalogProductModalInstance?.show();
            window.setTimeout(() =>
                document.getElementById('catalogReviewProductName')?.focus(), 180);
            return;
        }

        if (event.target.closest?.('#btnSaveCatalogProduct')) {
            await saveLegacyCatalogProduct();
        }
    });

    document.getElementById('catalogReviewProductName')?.addEventListener('keydown', event => {
        if (event.key !== 'Enter') return;
        event.preventDefault();
        document.getElementById('btnSaveCatalogProduct')?.click();
    });
}

async function saveLegacyCatalogProduct() {
    const documentId = Number(window.stockDocumentPage?.documentId || 0);
    const lineId = Number(document.getElementById('catalogReviewLineId')?.value || 0);
    const productName = document.getElementById('catalogReviewProductName')?.value.trim() || '';
    const message = document.getElementById('catalogReviewMessage');
    const button = document.getElementById('btnSaveCatalogProduct');
    const token = document.querySelector(
        '#catalogReviewAntiforgery input[name="__RequestVerificationToken"]')?.value;

    if (productName.length < 2) {
        if (message) message.textContent = 'Vui lòng nhập tên sản phẩm từ 2 ký tự.';
        document.getElementById('catalogReviewProductName')?.focus();
        return;
    }

    button.disabled = true;
    button.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Đang lưu';
    if (message) message.textContent = '';
    try {
        const response = await fetch(
            `/admin/stock-documents/${documentId}/catalog-lines/${lineId}/complete`, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token || ''
                },
                body: JSON.stringify({
                    productName,
                    rowVersion: window.stockDocumentRowVersion?.current?.() ||
                        window.stockDocumentPage?.rowVersion || ''
                })
            });
        const api = await readApiResponse(response);
        if (!api.ok) throw new Error(api.data?.message || 'Không thể hoàn thiện sản phẩm.');
        window.location.reload();
    } catch (error) {
        if (message) message.textContent = error.message || 'Không thể hoàn thiện sản phẩm.';
        button.disabled = false;
        button.innerHTML = '<i class="bx bx-check me-1"></i>Lưu và nhận diện';
    }
}

/* =========================================================
 * INIT
 * ========================================================= */

function initModalInstances() {
    const createReceiptModal = document.getElementById('createReceiptModal');
    const editLineModal = document.getElementById('editLineModal');
    const submitApprovalModal = document.getElementById('submitApprovalModal');
    const approveModal = document.getElementById('approveModal');
    const rejectModal = document.getElementById('rejectModal');
    const reconciliationReasonModal = document.getElementById('inputInvoiceReconciliationReasonModal');
    const mapInputInvoiceLineModal = document.getElementById('mapInputInvoiceLineModal');
    const quickAddProductModal = document.getElementById('quickAddProductModal');
    const quickEditLineModal = document.getElementById('quickEditLineModal');
    const deleteLineModal = document.getElementById('deleteLineModal');
    const returnToEditModal =
        document.getElementById('returnToEditModal');
    const linkedInputInvoicePreviewModal =
        document.getElementById('linkedInputInvoicePreviewModal');
    const unlinkInputInvoiceModal =
        document.getElementById('unlinkInputInvoiceModal');

    if (returnToEditModal) {
        returnToEditModalInstance =
            new bootstrap.Modal(returnToEditModal);
    }
    if (linkedInputInvoicePreviewModal) {
        linkedInputInvoicePreviewModalInstance = new bootstrap.Modal(linkedInputInvoicePreviewModal);
        linkedInputInvoicePreviewModal.addEventListener('hidden.bs.modal', releaseLinkedInputInvoicePreview);
    }
    if (unlinkInputInvoiceModal) {
        unlinkInputInvoiceModalInstance = new bootstrap.Modal(unlinkInputInvoiceModal);
        unlinkInputInvoiceModal.addEventListener('shown.bs.modal', function () {
            document.getElementById('inputInvoiceAssociationReason')?.focus();
        });
        unlinkInputInvoiceModal.addEventListener('hidden.bs.modal', function () {
            inputInvoiceAssociationReturnFocus?.focus();
            inputInvoiceAssociationReturnFocus = null;
        });
    }

    if (quickEditLineModal) quickEditLineModalInstance = new bootstrap.Modal(quickEditLineModal);
    if (deleteLineModal) deleteLineModalInstance = new bootstrap.Modal(deleteLineModal);

    if (createReceiptModal) createReceiptModalInstance = new bootstrap.Modal(createReceiptModal);
    if (editLineModal) editLineModalInstance = new bootstrap.Modal(editLineModal);
    if (submitApprovalModal) submitApprovalModalInstance = new bootstrap.Modal(submitApprovalModal);
    if (approveModal) approveModalInstance = new bootstrap.Modal(approveModal);
    if (rejectModal) rejectModalInstance = new bootstrap.Modal(rejectModal);
    if (reconciliationReasonModal) {
        inputInvoiceReconciliationReasonModalInstance = new bootstrap.Modal(reconciliationReasonModal);
        reconciliationReasonModal.addEventListener('hidden.bs.modal', function () {
            if (pendingReconciliationReasonResolve) {
                pendingReconciliationReasonResolve(null);
                pendingReconciliationReasonResolve = null;
            }
            inputInvoiceReconciliationReasonReturnFocus?.focus();
            inputInvoiceReconciliationReasonReturnFocus = null;
        });
    }
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

function formatDateTime(value) {
    if (!value) return '-';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? '-' : date.toLocaleString('vi-VN');
}

function formatLinkedInputInvoiceDate(value) {
    if (!value) return '';

    const d = new Date(value);
    if (isNaN(d.getTime())) return '';

    return d.toLocaleDateString('vi-VN', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric'
    });
}

function formatNumber(value) {
    return new Intl.NumberFormat('vi-VN').format(value ?? 0);
}

const wholeVndFormatter = new Intl.NumberFormat('vi-VN', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 0
});

function formatWholeVnd(value) {
    if (value === null || value === undefined || value === '') return '—';
    const number = Number(value);
    return Number.isFinite(number) ? wholeVndFormatter.format(number) : '—';
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

function showUnknownProvisionalCapture(term, searchField) {
    const panel = document.getElementById('provisionalReceivingPanel');
    const message = document.getElementById('provisionalReceivingMessage');
    if (!panel || !message || panel.dataset.mode !== 'warehouse') return false;

    message.className = 'alert alert-warning mt-2';
    message.innerHTML = `<div class="fw-semibold">Không tìm thấy sản phẩm</div>
        <div class="small mb-2">Có thể tìm lại hoặc ghi nhận nguyên trạng để Manager xử lý danh mục sau.</div>
        <div class="d-flex flex-wrap gap-2">
            <button type="button" class="btn btn-sm btn-outline-secondary" id="sdRetryUnknown">Tìm lại</button>
            <button type="button" class="btn btn-sm btn-primary" id="sdCaptureUnknown">Ghi nhận hàng mới</button>
        </div>`;
    document.getElementById('sdRetryUnknown')?.addEventListener('click', function () {
        message.textContent = '';
        searchField.focus();
        searchField.select();
    });
    document.getElementById('sdCaptureUnknown')?.addEventListener('click', function () {
        message.textContent = '';
        document.dispatchEvent(new CustomEvent('provisional:capture-request', {
            detail: { rawInput: term }
        }));
    });
    return true;
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
    status: 'needsAction',
    invoice: 'all'
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
            showReceiptListError(api.data?.message || 'Không tải được danh sách phiếu nhập.');
            return;
        }

        stockDocumentIndexState.allItems = api.data || [];
        await window.GaoLabels?.loadProgress();
        stockDocumentIndexState.page = 1;

        bindStockDocumentIndexFilters();
        applyStockDocumentIndexFilter();
    } catch (error) {
        console.error(error);
        showReceiptListError('Có lỗi khi tải danh sách phiếu nhập.');
    }
}

function showReceiptListError(message) {
    const body = document.getElementById('sdReceiptTableBody');
    if (body) body.innerHTML = renderIndexErrorRow(message);
    const mobile = document.getElementById('sdReceiptMobileList');
    if (mobile) mobile.innerHTML = `<div class="text-center text-danger py-4">${escapeHtml(message)}</div>`;
    setText('sdPaginationInfo', 'Không tải được danh sách. Vui lòng tải lại trang.');
    setText('sdCurrentPageText', '—');
    ['sdCountAll', 'sdCountWorking', 'sdCountPending', 'sdCountConfirmed', 'sdCountWaitingInvoice'].forEach(id => setText(id, '—'));
    ['sdBtnPrevPage', 'sdBtnNextPage'].forEach(id => {
        const button = document.getElementById(id);
        if (button) button.disabled = true;
    });
}

function bindStockDocumentIndexFilters() {
    const invoiceSelect = document.getElementById('sdInvoiceFilter');
    if (invoiceSelect && !invoiceSelect.dataset.bound) {
        invoiceSelect.dataset.bound = '1';
        invoiceSelect.addEventListener('change', () => {
            stockDocumentIndexState.invoice = invoiceSelect.value;
            // Choosing an invoice status must also show already-approved receipts.
            stockDocumentIndexState.status = 'all';
            document.getElementById('sdStatusFilter').value = 'all';
            stockDocumentIndexState.page = 1;
            syncStockDocumentIndexKpiFilters();
            applyStockDocumentIndexFilter();
        });
        document.getElementById('sdWaitingInvoiceKpi')?.addEventListener('click', () => {
            invoiceSelect.value = 'Waiting';
            invoiceSelect.dispatchEvent(new Event('change'));
        });
    }
    const keywordInput = document.getElementById('sdSearchKeyword');
    const statusSelect = document.getElementById('sdStatusFilter');
    const pageSizeSelect = document.getElementById('sdPageSize');
    const resetBtn = document.getElementById('sdBtnResetFilter');
    const prevBtn = document.getElementById('sdBtnPrevPage');
    const nextBtn = document.getElementById('sdBtnNextPage');

    bindStockDocumentIndexKpiFilters();

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
            syncStockDocumentIndexKpiFilters();
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
            stockDocumentIndexState.invoice = 'all';
            if (invoiceSelect) invoiceSelect.value = 'all';
            stockDocumentIndexState.page = 1;

            if (keywordInput) keywordInput.value = '';
            if (statusSelect) statusSelect.value = 'all';

            syncStockDocumentIndexKpiFilters();
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

function bindStockDocumentIndexKpiFilters() {
    document.querySelectorAll('[data-stock-document-index] .sd-index-kpi-filter[data-status-filter]')
        .forEach(button => {
            if (button.dataset.bound === '1') return;
            button.dataset.bound = '1';

            button.addEventListener('click', function () {
                const filter = this.dataset.statusFilter || 'all';
                const statusSelect = document.getElementById('sdStatusFilter');

                stockDocumentIndexState.status = filter;
                stockDocumentIndexState.invoice = 'all';
                const invoiceSelect = document.getElementById('sdInvoiceFilter');
                if (invoiceSelect) invoiceSelect.value = 'all';
                stockDocumentIndexState.page = 1;
                if (statusSelect) statusSelect.value = filter;

                syncStockDocumentIndexKpiFilters();
                applyStockDocumentIndexFilter();
            });
        });

    syncStockDocumentIndexKpiFilters();
}

function syncStockDocumentIndexKpiFilters() {
    document.getElementById('sdWaitingInvoiceKpi')?.setAttribute('aria-pressed', String(stockDocumentIndexState.invoice === 'Waiting'));
    document.querySelectorAll('[data-stock-document-index] .sd-index-kpi-filter[data-status-filter]')
        .forEach(button => {
            button.setAttribute(
                'aria-pressed',
                String(button.dataset.statusFilter === stockDocumentIndexState.status));
        });
}

function applyStockDocumentIndexFilter() {
    const keyword = stockDocumentIndexState.keyword;
    const status = stockDocumentIndexState.status;

    stockDocumentIndexState.filteredItems = stockDocumentIndexState.allItems.filter(x => {
        const matchKeyword = !keyword || [
            x.documentNo,
            x.documentTitle,
            x.purchaseOrderTitle,
            x.purchaseOrderNumber,
            x.supplierName,
            x.legalEntityName,
            x.warehouseName,
            x.revisionRequestNote,
            x.hasRevisionRequest ? 'yêu cầu đề nghị sửa' : '',
            statusToText(x.status)
        ].some(v => String(v || '').toLowerCase().includes(keyword));

        const matchStatus = status === 'all' || (status === 'needsAction'
            ? [1, 2, 4].includes(Number(x.status)) || Number(x.status) === 3 && ['Waiting', 'NeedsReview'].includes(x.invoiceFollowUp)
            : matchStockDocumentStatusGroup(x.status, status));
        const matchInvoice = stockDocumentIndexState.invoice === 'all' ||
            Number(x.status) === 3 && (x.invoiceFollowUp || 'Unclassified') === stockDocumentIndexState.invoice;

        return matchKeyword && matchStatus && matchInvoice;
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
    const mobileList = document.getElementById('sdReceiptMobileList');
    if (mobileList) {
        mobileList.innerHTML = renderStockDocumentIndexMobileCards(pageItems);
    }
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

    document.querySelectorAll('.js-stock-document-quick-view').forEach(button => {
        button.onclick = function (event) {
            event.preventDefault();
            event.stopPropagation();

            const id = Number(this.dataset.id || 0);
            const item = stockDocumentIndexCurrentPageItems.find(x => Number(x.id) === id);
            if (item) openStockDocumentInfoModal(item);
        };
    });
}

function openStockDocumentInfoModal(item) {
    const modalEl = document.getElementById('stockDocumentInfoModal');

    if (!stockDocumentInfoModalInstance && modalEl) {
        stockDocumentInfoModalInstance = new bootstrap.Modal(modalEl);
    }

    setText('sdInfoTitle', item.purchaseOrderTitle || item.documentTitle || item.documentNo || 'Chi tiết phiếu');
    setText('sdInfoSubTitle', [
        item.purchaseOrderNumber,
        item.supplierName,
        item.documentNo ? `Phiếu nhận ${item.documentNo}` : null
    ].filter(Boolean).join(' · ') || '-');
    setText('sdInfoStatus', statusToDisplayText(item.status));
    setText(
        'sdInfoWarehouse',
        [item.legalEntityName, item.warehouseName].filter(Boolean).join(' · ') || '-');
    setText('sdInfoSupplier', item.supplierName || '-');
    setText('sdInfoCreatedBy', item.createdByName || 'Chưa ghi nhận');
    setText('sdInfoEntryTerminal', [item.entryTerminalName, item.entryTerminalCode].filter(Boolean).join(' · ') || 'Chưa ghi nhận');

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
                <td colspan="7" class="text-center text-muted py-4">
                    Không có dữ liệu phù hợp
                </td>
            </tr>`;
    }

    return items.map(x => {
        const hasRevisionRequest = x.hasRevisionRequest === true;

        const actionText = hasRevisionRequest
            ? 'Xử lý sửa'
            : (x.status === 2 ? 'Duyệt' : (x.status === 3 ? (x.invoiceFollowUp === 'Waiting' ? 'Bổ sung XML' : x.invoiceFollowUp === 'NeedsReview' ? 'Đối chiếu XML' : 'Xem') : 'Mở'));

        const actionClass = hasRevisionRequest
            ? 'btn-danger'
            : (x.status === 2
                ? 'btn-warning'
                : (x.status === 3 ? 'btn-outline-primary' : 'btn-primary'));

        const title = x.documentTitle || x.purchaseOrderTitle || 'Chưa đặt tên phiếu';
        const purchaseContext = [x.purchaseOrderNumber, x.supplierName]
            .filter(Boolean)
            .join(' · ');
        const receiptContext = [x.documentNo, x.purchaseOrderNumber ? 'Theo đơn mua' : 'Phiếu nhập trực tiếp']
            .filter(Boolean)
            .join(' · ');
        const submittedContext = x.submittedAtUtc
            ? `Gửi ${formatDate(x.submittedAtUtc)}`
            : null;

        return `
            <tr class="sd-index-row"
                data-id="${x.id}"
                title="Bấm đúp để xem nhanh">
                <td>
                    <div class="sd-index-title ${title === 'Chưa đặt tên phiếu' ? 'sd-index-title-muted' : ''}">${escapeHtml(title)}</div>
                    <div class="sd-index-doc-no">${escapeHtml(receiptContext || '-')}</div>
                    <div class="text-muted small text-truncate">${escapeHtml(purchaseContext || x.supplierName || '-')}</div>
                </td>
                <td class="sd-index-entry">
                    <div class="sd-index-entry-line fw-semibold"><i class="bx bx-user" aria-hidden="true"></i><span>${escapeHtml(x.createdByName || 'Chưa ghi nhận')}</span></div>
                    <div class="sd-index-entry-line text-muted small"><i class="bx bx-desktop" aria-hidden="true"></i><span>${escapeHtml(x.entryTerminalName || 'Chưa ghi nhận')}${x.entryTerminalCode ? ` <span class="sd-index-terminal-code">${escapeHtml(x.entryTerminalCode)}</span>` : ''}</span></div>
                </td>
                <td>
                    ${renderStatusBadge(x.status)}
                    ${window.GaoReceiptInvoiceFollowUp?.badgeHtml(x) || ''}
                    ${window.GaoLabels?.progressHtml(x.id) || ''}
                    ${submittedContext ? `<div class="sd-index-progress-note">${submittedContext}</div>` : ''}
                    ${hasRevisionRequest ? `<div class="sd-index-revision-note" title="${escapeHtml(x.revisionRequestNote || 'Có yêu cầu sửa')}">Có yêu cầu sửa</div>` : ''}
                </td>
                <td class="text-end fw-semibold">${formatNumber(x.totalProductTypes || 0)}</td>
                <td class="text-end sd-index-amount">${formatNumber(x.totalAmount || 0)} ₫</td>
                <td>${renderIndexDate(x.updatedAtUtc || x.createdAtUtc || x.documentDate)}</td>
                <td class="text-end">
                    <div class="sd-index-actions">
                        ${window.GaoReceiptDocumentActions?.buttons(x) || ''}
                        <button type="button"
                                class="btn btn-outline-secondary btn-sm gds-icon-button js-stock-document-quick-view"
                                data-id="${x.id}"
                                aria-label="Xem nhanh ${escapeHtml(title)}">
                            <i class="bx bx-show" aria-hidden="true"></i>
                        </button>
                        <a class="btn btn-sm ${actionClass} gds-action-button" href="/admin/stock-documents/${x.id}${x.status === 3 && ['Waiting', 'NeedsReview'].includes(x.invoiceFollowUp) ? '?tab=xml' : ''}">${actionText}</a>
                    </div>
                </td>
            </tr>`;
    }).join('');
}

function renderStockDocumentIndexMobileCards(items) {
    if (!items || items.length === 0) {
        return `<div class="text-center text-muted py-4">Không có dữ liệu phù hợp</div>`;
    }

    return items.map(x => {
        const hasRevisionRequest = x.hasRevisionRequest === true;
        const actionText = hasRevisionRequest
            ? 'Xử lý sửa'
            : (x.status === 2 ? 'Duyệt' : (x.status === 3 ? (x.invoiceFollowUp === 'Waiting' ? 'Bổ sung XML' : x.invoiceFollowUp === 'NeedsReview' ? 'Đối chiếu XML' : 'Xem') : 'Mở'));
        const actionClass = hasRevisionRequest
            ? 'btn-danger'
            : (x.status === 2 ? 'btn-warning' : (x.status === 3 ? 'btn-outline-primary' : 'btn-primary'));
        const title = x.documentTitle || x.purchaseOrderTitle || 'Chưa đặt tên phiếu';
        const context = [x.documentNo, x.purchaseOrderNumber, x.supplierName]
            .filter(Boolean)
            .join(' · ');

        return `
            <article class="gds-row-card sd-index-mobile-card" data-id="${x.id}">
                <div class="gds-row-card__header">
                    <div>
                        <div class="gds-row-card__title">${escapeHtml(title)}</div>
                        <div class="gds-row-card__meta">${escapeHtml(context || '-')}</div>
                    </div>
                    ${renderStatusBadge(x.status)}
                    ${window.GaoReceiptInvoiceFollowUp?.badgeHtml(x) || ''}
                </div>
                ${hasRevisionRequest ? `<div class="sd-index-revision-note">Có yêu cầu sửa</div>` : ''}
                <div class="sd-index-mobile-facts">
                    <div><span>Nhân viên tạo phiếu</span><strong>${escapeHtml(x.createdByName || 'Chưa ghi nhận')}</strong></div>
                    <div><span>Quầy nhập</span><strong>${escapeHtml([x.entryTerminalName, x.entryTerminalCode].filter(Boolean).join(' · ') || 'Chưa ghi nhận')}</strong></div>
                    <div><span>Loại sản phẩm</span><strong>${formatNumber(x.totalProductTypes || 0)}</strong></div>
                    <div><span>Tổng tiền</span><strong class="text-primary">${formatNumber(x.totalAmount || 0)} ₫</strong></div>
                    <div><span>Cập nhật</span><strong>${formatDate(x.updatedAtUtc || x.createdAtUtc || x.documentDate) || '-'}</strong></div>
                </div>
                <div class="gds-row-card__actions">
                        ${window.GaoReceiptDocumentActions?.buttons(x) || ''}
                    <button type="button"
                            class="btn btn-outline-secondary gds-action-button js-stock-document-quick-view"
                            data-id="${x.id}">
                        <i class="bx bx-show" aria-hidden="true"></i> Xem nhanh
                    </button>
                    <a class="btn ${actionClass} gds-action-button" href="/admin/stock-documents/${x.id}${x.status === 3 && ['Waiting', 'NeedsReview'].includes(x.invoiceFollowUp) ? '?tab=xml' : ''}">${actionText}</a>
                </div>
            </article>`;
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
    setText('sdCountWaitingInvoice', formatNumber(all.filter(x => x.status === 3 && x.invoiceFollowUp === 'Waiting').length));
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
    if (group === 'working') return status === 1 || status === 4;
    if (group === 'draft') return status === 1;
    if (group === 'pending') return status === 2;
    if (group === 'confirmed') return status === 3;
    if (group === 'rejected') return status === 4;

    return true;
}

function renderIndexLoadingRow() {
    return `
        <tr>
                <td colspan="7" class="text-center text-muted py-4">
                Đang tải dữ liệu...
            </td>
        </tr>`;
}

function renderIndexErrorRow(message) {
    return `
        <tr>
                <td colspan="7" class="text-center text-danger py-4">
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
    if (directReason) directReason.value = 'Nhà phân phối giao';

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
        const template = document.createElement('template');
        template.innerHTML = html;
        const snapshot = template.content.querySelector('[data-stock-row-version]');
        const version = normalizeStockDocumentRowVersion(snapshot?.dataset.stockRowVersion);
        if (!version || Number(snapshot?.dataset.stockDocumentId) !== documentId)
            throw new Error('Không đọc được phiên bản của bảng hàng nhập.');
        if (container) container.replaceChildren(template.content);
        window.stockDocumentRowVersion.update(version);

        bindDeleteLine();
        bindOpenEditLineModal();
        bindOpenMapInputInvoiceLineModal();
        bindInlineLineQuantityChange();
        bindInlineLineUnitCostChange();
        syncToggleAllInputInvoiceLinesState();
        await loadItemCatalogLineStatuses();
        return true;
    } catch (error) {
        console.error(error);
        alert('Không tải lại được danh sách dòng nhập.');
        return false;
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
            // Top, workbench and keyboard actions share the same commercial preflight.
            if (document.getElementById('commercialApprovalWorkbench') &&
                window.GaoAppPurchaseReceiptApproval?.prepareConfirmation?.() !== true) return;

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
        if (window.ReceiptIntake?.isSaving() || window.ReceiptQuantityControls?.hasFailed()) {
            if (msg) msg.textContent = window.ReceiptQuantityControls?.hasFailed()
                ? 'Chưa lưu được số lượng. Vui lòng kiểm tra dòng hàng và lưu lại trước khi gửi duyệt.'
                : 'Đang lưu hàng nhận. Vui lòng chờ lưu xong trước khi gửi duyệt.';
            return;
        }
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
            const message = api.data?.message || 'Duyệt phiếu thất bại.';
            if (msg) msg.textContent = message;
            if (/MST người mua|chủ thể|pháp nhân/i.test(message)) {
                approveModalInstance?.hide();
                document.getElementById('inputInvoicePickerSection')
                    ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
                document.getElementById('btnOpenInputInvoicePicker')?.focus();
                setInputInvoicePageMessage(message, true);
            }
            if (/đối chiếu|chênh lệch|hóa đơn XML/i.test(message)) {
                approveModalInstance?.hide();
                await loadInputInvoiceReconciliation();
                document.getElementById('inputInvoiceReconciliationPanel')
                    ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
                document.getElementById('inputInvoiceReconciliationTitle')?.focus();
            }
            return;
        }

        if (approveModalInstance) approveModalInstance.hide();

        if (await window.GaoLabels?.afterApproval(documentId)) return;

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
            if (msg) msg.textContent = api.data?.message || 'Không thể trả phiếu về chỉnh sửa.';
            return;
        }

        if (rejectModalInstance) rejectModalInstance.hide();

        alert(api.data?.message || 'Đã trả phiếu về chỉnh sửa.');
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

        // Mỗi lần nhấn mũi tên thay đổi đúng một đơn vị.
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowUp') {
                e.preventDefault();
                changeInlineQtyByStep(this, 1);
                return;
            }

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                changeInlineQtyByStep(this, -1);
                return;
            }

            if (e.key === 'Enter') {
                e.preventDefault();
                focusLookupAfterSave = true;
                this.blur();
            }
        });

        input.addEventListener('blur', async function () {
            if (this.dataset.receiptStepSaving === '1') return;
            if (Number(this.value) !== Number(this.defaultValue)) await saveInlineLineQuantity(this);

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
    const next = Math.max(1, current + delta);

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

    if (!lineId || !unitId) return false;

    if (quantity <= 0) {
        alert('Số lượng phải lớn hơn 0.');
        input.focus();
        input.select();
        return false;
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
        return false;
    }

    return await refreshStockDocumentDetailUI();
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
    const canEditPendingOwnership =
        document.getElementById('CanEditPendingOwnership')?.value === 'true';
    const canEditLines = document.getElementById('CanEditLines')?.value === 'true';

    if (!canEditHeader && !canEditPendingOwnership) {
        $('#LegalEntityId').prop('disabled', true);

    }

    if (!canEditHeader) {

        const documentNote = document.getElementById('DocumentNote');
        const approvalNote = document.getElementById('ApprovalNote');

        if (documentNote) documentNote.disabled = true;
        if (approvalNote) approvalNote.disabled = true;
    }

    if (!canEditHeader && !canEditPendingOwnership) {
        $('#WarehouseId').prop('disabled', true);
        $('#SupplierId').prop('disabled', true);
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
    const canEditPendingOwnership =
        document.getElementById('CanEditPendingOwnership')?.value === 'true';
    if (!canEditHeader && !canEditPendingOwnership) {
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

        if (canEditHeader || canEditPendingOwnership) {
            legalEntitySelect.addEventListener('change', function () {
                renderReceiptWarehouseOptions(
                    warehouseSelect,
                    Number(legalEntitySelect.value || 0),
                    cachedReceiptFormOptions);
            });
        }
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
    const canEditPendingOwnership =
        document.getElementById('CanEditPendingOwnership')?.value === 'true';

    if (!canEditHeader && !canEditPendingOwnership) {
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
        language: { inputTooShort: () => 'Nhập tên hoặc quét mã sản phẩm', searching: () => 'Đang tìm…',
            noResults: () => window.ReceiptIntake?.noResults() || 'Không tìm thấy sản phẩm.' },
        ajax: {
            url: window.ReceiptBarcodeProposals?.lookupUrl || '/admin/stock-documents/product-lookup-select2',
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
        if (e.key !== 'Enter' || this.closest('#rbpModal,#receiptIntakeModal')) return;

        const term = $(this).val();
        if (!term) return;

        try {
            const response = await fetch(`${window.ReceiptBarcodeProposals?.lookupUrl || '/admin/stock-documents/product-lookup-select2'}?term=${encodeURIComponent(term)}`);
            const api = await readApiResponse(response);

            if (!api.ok || !api.data?.results) return;

            if (api.data.results.length === 0) {
                if (window.ReceiptBarcodeProposals?.open(term) || showUnknownProvisionalCapture(term, this)) {
                    e.preventDefault();
                    $('#quickLookupInput').select2('close');
                }
                return;
            }

            if (api.data.results.length !== 1) return;

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
    const unitCost = parseDecimalInput(document.getElementById('popupQuickUnitCost')?.value || '0');

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
    const canEditPendingOwnership =
        document.getElementById('CanEditPendingOwnership')?.value === 'true';
    if (!canEditHeader && !canEditPendingOwnership) return;

    const legalEntityEl = $('#LegalEntityId');
    const warehouseEl = $('#WarehouseId');
    const supplierEl = $('#SupplierId');
    const documentNote = document.getElementById('DocumentNote');
    const approvalNote = document.getElementById('ApprovalNote');

    if (warehouseEl.length) warehouseEl.on('change', queueEditHeaderSave);
    if (supplierEl.length) supplierEl.on('change', queueEditHeaderSave);

    if (canEditHeader) {
        if (legalEntityEl.length) legalEntityEl.on('change', queueEditHeaderSave);
        if (documentNote) documentNote.addEventListener('input', queueEditHeaderSave);
        if (approvalNote) approvalNote.addEventListener('input', queueEditHeaderSave);
    }
}

function queueEditHeaderSave() {
    if (suppressEditHeaderSave) return;
    if (editHeaderSaveTimer) clearTimeout(editHeaderSaveTimer);

    editHeaderSaveTimer = setTimeout(function () {
        saveEditHeader();
    }, 500);
}

async function saveEditHeader() {
    const canEditHeader = document.getElementById('CanEditHeader')?.value === 'true';
    const canEditPendingOwnership =
        document.getElementById('CanEditPendingOwnership')?.value === 'true';
    if (!canEditHeader && !canEditPendingOwnership) return;

    const stockDocumentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    if (!stockDocumentId) return;

    const selectedWarehouseId = $('#WarehouseId').val()
        ? Number($('#WarehouseId').val()) : null;
    const selectedWarehouse = (cachedReceiptFormOptions?.warehouses || [])
        .find(item => Number(item.id) === Number(selectedWarehouseId));
    const payload = {
        stockDocumentId: stockDocumentId,
        // Filter UI is never owner authority. The posted value is derived from
        // the selected Warehouse and the server re-validates that relation.
        legalEntityId: selectedWarehouse ? Number(selectedWarehouse.legalEntityId) : null,
        warehouseId: selectedWarehouseId,
        supplierId: $('#SupplierId').val() ? Number($('#SupplierId').val()) : null
    };
    if (canEditHeader) {
        payload.note = document.getElementById('DocumentNote')?.value || '';
        payload.approvalNote = document.getElementById('ApprovalNote')?.value || '';
    }

    showEditHeaderMessage('');
    try {
        const response = await fetch('/admin/stock-documents/update-header', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            restoreEditHeaderOwnershipControls();
            const message = api.data?.message || 'Cập nhật thông tin phiếu thất bại.';
            showEditHeaderMessage(message, true);
            if (message.toLowerCase().includes('gỡ liên kết'))
                document.getElementById('inputInvoicePickerSection')?.focus({ preventScroll: false });
            return;
        }

        window.location.reload();
    } catch (error) {
        console.error(error);
        restoreEditHeaderOwnershipControls();
        showEditHeaderMessage('Không thể cập nhật thông tin phiếu. Vui lòng thử lại.', true);
    }
}

function restoreEditHeaderOwnershipControls() {
    suppressEditHeaderSave = true;
    const warehouseId = document.getElementById('CurrentWarehouseId')?.value || '';
    const supplierId = document.getElementById('CurrentSupplierId')?.value || '';
    const supplierText = document.getElementById('CurrentSupplierText')?.value || '';

    $('#WarehouseId').val(warehouseId).trigger('change.select2');
    const supplier = $('#SupplierId');
    if (supplier.length) {
        supplier.empty();
        if (supplierId) {
            supplier.append(new Option(
                supplierText || `Nhà cung cấp #${supplierId}`,
                supplierId,
                true,
                true));
        }
        supplier.trigger('change.select2');
    }
    suppressEditHeaderSave = false;
}

function showEditHeaderMessage(message, isError = false) {
    const element = document.getElementById('editHeaderMessage');
    if (!element) return;
    element.textContent = message || '';
    element.classList.toggle('text-danger', Boolean(message) && isError);
    element.classList.toggle('text-success', Boolean(message) && !isError);
}

/* =========================================================
 * EDIT - LINKED INPUT INVOICES
 * ========================================================= */

async function loadInputInvoicesForStockDocument() {
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    const container = document.getElementById('inputInvoiceListContainer');

    if (!documentId || !container) return;

    try {
        container.innerHTML = `<div class="text-muted small">Đang tải hóa đơn XML...</div>`;

        await loadInputInvoiceAssociation();

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
        window.dispatchEvent(new CustomEvent('input-invoices-updated', {
            detail: { stockDocumentId: documentId }
        }));
        await loadItemCatalogLineStatuses();
        await loadInputInvoiceReconciliation();
        await refreshCommercialReconciliationPreviewAfterXmlMutation();
    } catch (error) {
        console.error(error);

        container.innerHTML = `
            <div class="text-danger small">
                Có lỗi khi tải danh sách XML.
            </div>`;
    }
}

async function loadInputInvoiceAssociation() {
    const generation = ++inputInvoiceAssociationGeneration;
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    const state = document.getElementById('inputInvoiceAssociationState');
    if (!documentId || !state) return;
    const response = await fetch(
        `/admin/api/stock-documents/${documentId}/input-invoices/association`,
        { credentials: 'same-origin', cache: 'no-store' });
    const api = await readApiResponse(response);
    if (generation !== inputInvoiceAssociationGeneration) return;
    if (!api.ok) {
        cachedInputInvoiceAssociation = null;
        state.textContent = api.data?.message || 'Không tải được trạng thái liên kết XML.';
        state.className = 'small mt-2 text-danger';
        return;
    }
    cachedInputInvoiceAssociation = api.data;
    const waiting = api.data?.lifecycleState === 'WaitingXml';
    const linked = api.data?.lifecycleState === 'Linked';
    state.textContent = waiting
        ? 'Chờ hóa đơn XML · Phiếu đã xác nhận và đang chờ liên kết thủ công.'
        : linked
            ? `Đã liên kết XML ${api.data.currentInvoiceSeries || '-'} · ${api.data.currentInvoiceNumber || '-'}.`
            : 'Chưa liên kết hóa đơn XML.';
    state.className = `small mt-2 ${waiting ? 'text-warning fw-semibold' : linked ? 'text-success' : 'text-muted'}`;
}

function renderInputInvoiceList(invoices) {
    const container = document.getElementById('inputInvoiceListContainer');
    if (!container) return;

    if (!invoices || invoices.length === 0) {
        cachedInputInvoices = [];
        container.innerHTML = `
            <div class="text-muted small">
                Chưa có hóa đơn nào được gắn với phiếu này.
            </div>`;
        return;
    }

    cachedInputInvoices = invoices;
    container.innerHTML = invoices.map((invoice, index) => {
        const ownerWarning = invoice.ownerWarningReasonCode
            ? `<div class="alert alert-warning py-2 px-3 mt-2 mb-0 small" role="alert"
                    data-owner-warning="${escapeHtml(invoice.ownerWarningReasonCode)}">
                    <strong>Cảnh báo chủ thể:</strong>
                    ${escapeHtml(invoice.ownerWarningMessage || 'Chủ thể người mua của hóa đơn không còn hợp lệ với phiếu nhập.')}
               </div>`
            : '';
        return `
            <article class="border rounded-3 p-3 mb-2 bg-white d-flex justify-content-between align-items-center flex-wrap gap-3">
                <div>
                    <div class="fw-semibold">
                        ${escapeHtml(invoice.invoiceSeries || '-')} · ${escapeHtml(invoice.invoiceNumber || '-')}
                    </div>
                    <div class="small text-muted mt-1">
                        Ngày: ${invoice.invoiceDate ? formatLinkedInputInvoiceDate(invoice.invoiceDate) : '-'}
                        · MST người mua: ${escapeHtml(invoice.buyerTaxCode || '-')}
                        · Chủ thể: ${escapeHtml(invoice.resolvedBuyerLegalEntityName || invoice.buyerOwnerResolutionStatus || 'Chưa xác định')}
                    </div>
                    ${ownerWarning}
                </div>
                <div class="d-flex gap-2 flex-wrap">
                    <button type="button" class="btn btn-sm btn-outline-primary js-view-linked-input-invoice"
                            data-index="${index}">Xem hóa đơn</button>
                    <button type="button" class="btn btn-sm btn-outline-danger js-unlink-input-invoice"
                            data-index="${index}">Gỡ liên kết</button>
                    ${cachedInputInvoiceAssociation?.capabilities?.canRelink
                        ? `<button type="button" class="btn btn-sm btn-outline-secondary js-relink-input-invoice"
                                  data-index="${index}">Thay hóa đơn</button>`
                        : ''}
                </div>
            </article>`;
    }).join('');
}

function bindLinkedInputInvoiceActions() {
    const container = document.getElementById('inputInvoiceListContainer');
    if (container) {
        container.addEventListener('click', async function (event) {
            const viewButton = event.target.closest('.js-view-linked-input-invoice');
            if (viewButton) {
                const invoice = cachedInputInvoices[Number(viewButton.dataset.index)];
                if (invoice) await openLinkedInputInvoicePreview(invoice);
                return;
            }
            const unlinkButton = event.target.closest('.js-unlink-input-invoice');
            if (unlinkButton) {
                const invoice = cachedInputInvoices[Number(unlinkButton.dataset.index)];
                if (invoice) openUnlinkInputInvoiceConfirmation(invoice);
                return;
            }
            const relinkButton = event.target.closest('.js-relink-input-invoice');
            if (relinkButton) {
                const invoice = cachedInputInvoices[Number(relinkButton.dataset.index)];
                if (invoice?.id && typeof window.openInputInvoicePickerForRelink === 'function') {
                    inputInvoiceAssociationReturnFocus = relinkButton;
                    window.openInputInvoicePickerForRelink(invoice.id);
                }
            }
        });
    }

    const confirm = document.getElementById('btnConfirmUnlinkInputInvoice');
    if (confirm) confirm.addEventListener('click', unlinkPendingInputInvoice);
}

async function openLinkedInputInvoicePreview(invoice) {
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    const body = document.getElementById('linkedInputInvoicePreviewBody');
    if (!documentId || !invoice?.id || !body || !linkedInputInvoicePreviewModalInstance) return;
    releaseLinkedInputInvoicePreview();
    document.getElementById('linkedInputInvoicePreviewTitle').textContent =
        `Xem hóa đơn ${invoice.invoiceSeries || '-'} · ${invoice.invoiceNumber || '-'}`;
    body.innerHTML = '<div class="p-4 text-muted">Đang tải bản xem trước...</div>';
    linkedInputInvoicePreviewModalInstance.show();

    const baseUrl = `/admin/api/stock-documents/${documentId}/input-invoices/${invoice.id}/preview`;
    try {
        const pdf = await fetch(`${baseUrl}/pdf`, {
            credentials: 'same-origin', cache: 'no-store'
        });
        if (pdf.ok) {
            linkedInputInvoicePreviewObjectUrl = URL.createObjectURL(await pdf.blob());
            body.innerHTML = `<iframe title="Bản xem trước PDF hóa đơn" src="${linkedInputInvoicePreviewObjectUrl}"
                style="width:100%;min-height:70vh;border:0;background:#fff"></iframe>`;
            return;
        }

        const xml = await fetch(`${baseUrl}/xml`, {
            credentials: 'same-origin', cache: 'no-store'
        });
        if (!xml.ok) {
            const api = await readApiResponse(xml);
            throw new Error(api.data?.message || 'Không có PDF hoặc XML để xem trước.');
        }
        body.innerHTML = await xml.text();
    } catch (error) {
        body.innerHTML = `<div class="alert alert-warning m-4">${escapeHtml(error.message)}</div>`;
    }
}

function releaseLinkedInputInvoicePreview() {
    if (linkedInputInvoicePreviewObjectUrl)
        URL.revokeObjectURL(linkedInputInvoicePreviewObjectUrl);
    linkedInputInvoicePreviewObjectUrl = null;
}

function openUnlinkInputInvoiceConfirmation(invoice) {
    inputInvoiceAssociationReturnFocus = document.activeElement;
    pendingUnlinkInputInvoice = invoice;
    const label = `${invoice.invoiceSeries || '-'} · ${invoice.invoiceNumber || '-'}`;
    const description = document.getElementById('unlinkInputInvoiceDescription');
    const message = document.getElementById('unlinkInputInvoiceMessage');
    const reason = document.getElementById('inputInvoiceAssociationReason');
    if (description)
        description.textContent = `Hóa đơn ${label} sẽ được gỡ khỏi phiếu nhập này.`;
    if (message) message.textContent = '';
    if (reason) reason.value = '';
    unlinkInputInvoiceModalInstance?.show();
}

async function unlinkPendingInputInvoice() {
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    const invoice = pendingUnlinkInputInvoice;
    const button = document.getElementById('btnConfirmUnlinkInputInvoice');
    const message = document.getElementById('unlinkInputInvoiceMessage');
    const reasonInput = document.getElementById('inputInvoiceAssociationReason');
    const reason = String(reasonInput?.value || '').trim();
    if (!documentId || !invoice?.id || !button) return;
    if (!reason) {
        if (message) message.textContent = 'Vui lòng nhập lý do gỡ liên kết.';
        reasonInput?.focus();
        return;
    }

    button.disabled = true;
    if (message) message.textContent = '';
    try {
        const response = await fetch(
            `/admin/api/stock-documents/${documentId}/input-invoices/${invoice.id}/unlink`, {
                method: 'POST', credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    expectedCurrentInputInvoiceHeadId: invoice.id,
                    reason: reason
                })
            });
        const api = await readApiResponse(response);
        if (!api.ok) {
            if (response.status === 409 && api.data?.code === 'AssociationChanged')
                throw new Error(api.data.message || 'Liên kết đã thay đổi. Vui lòng tải lại và thử lại.');
            throw new Error(api.data?.message || 'Không thể gỡ liên kết hóa đơn.');
        }
        unlinkInputInvoiceModalInstance?.hide();
        pendingUnlinkInputInvoice = null;
        setInputInvoicePageMessage(api.data?.message || 'Đã gỡ liên kết hóa đơn.', false);
        await loadInputInvoicesForStockDocument();
    } catch (error) {
        if (message) message.textContent = error.message;
        setInputInvoicePageMessage(error.message, true);
    } finally {
        button.disabled = false;
    }
}

function setInputInvoicePageMessage(message, isError) {
    const node = document.getElementById('inputInvoicePickerPageMessage');
    if (!node) return;
    node.textContent = message || '';
    node.className = `small px-4 pb-3 ${isError ? 'text-danger' : 'text-success'}`;
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
                        <th style="min-width:130px;">Mã hàng XML</th>
                        <th style="min-width:280px;">Tên hàng XML</th>
                        <th style="width:100px;">ĐVT</th>
                        <th style="min-width:130px;">Nhận diện</th>
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
                            <td>${escapeHtml(d.supplierItemCode || 'Không có mã')}</td>
                            <td>${escapeHtml(d.itemName || '')}</td>
                            <td>${escapeHtml(d.unitName || '')}</td>
                            <td>${renderItemCatalogStatusBadge(d.itemCatalogMapping)}</td>
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
            const preferredDetailId = Number(this.dataset.inputInvoiceDetailId || 0);
            if (!lineId) return;

            selectedInputInvoiceDetailId = null;
            selectedInputInvoiceDetail = null;

            document.getElementById('mapStockDocumentLineId').value = lineId;
            document.getElementById('mapUseInputInvoice').checked = true;
            document.getElementById('mapUseInputInvoice').dispatchEvent(new Event('change'));
            document.getElementById('mapRememberItemCatalogMapping').checked = false;
            document.getElementById('mapInputInvoiceExclusionReason').value = '';
            document.getElementById('mapInputInvoiceExclusionReasonBox')
                ?.classList.add('d-none');
            document.getElementById('mapInputInvoiceMessage').textContent = '';
            document.getElementById('mapReceiptLineProduct').textContent =
                this.dataset.productName || `Dòng #${lineId}`;
            const quantity = formatNumber(Number(this.dataset.quantity || 0));
            const unit = this.dataset.unitName || '';
            const factor = formatNumber(Number(this.dataset.factor || 1));
            document.getElementById('mapReceiptLineQuantity').textContent =
                `${quantity} ${unit} · hệ số ${factor}`;

            await ensureInputInvoicesLoaded();
            renderXmlLinePicker();
            if (preferredDetailId) {
                const preferred = document.querySelector(
                    `.js-pick-xml-detail[value="${preferredDetailId}"]`);
                if (preferred) {
                    preferred.checked = true;
                    preferred.dispatchEvent(new Event('change'));
                }
            }

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
                invoiceNumber: inv.invoiceNumber,
                supplierLabel: [inv.sellerName, inv.sellerTaxCode].filter(Boolean).join(' · ')
            });
        });
    });
    itemCatalogDetailOptions = details;

    if (!details.length) {
        box.innerHTML = `<div class="text-muted">Chưa có dòng XML. Hãy chọn hóa đơn trước.</div>`;
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
                        <th>Mã hàng XML</th>
                        <th>Tên hàng XML</th>
                        <th>ĐVT</th>
                        <th>Gợi ý lần sau</th>
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
                            <td>${escapeHtml(d.supplierItemCode || 'Không có mã')}</td>
                            <td>${escapeHtml(d.itemName || '')}</td>
                            <td>${escapeHtml(d.unitName || '')}</td>
                            <td>${renderItemCatalogStatusBadge(d.itemCatalogMapping)}</td>
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
            selectedInputInvoiceDetail = itemCatalogDetailOptions.find(
                x => Number(x.id) === selectedInputInvoiceDetailId) || null;
        };
    });
}

function getItemCatalogStatePresentation(mapping) {
    const state = mapping?.stateName || mapping?.state || 'Unmapped';
    if (state === 'Confirmed' || state === 2)
        return { label: 'Đã nhớ', action: 'Đã nhớ', badge: 'bg-label-success' };
    if (mapping?.reasonCode === 'CrossUnitProductInherited')
        return { label: 'Đã nhận diện', action: 'Xác nhận đơn vị', badge: 'bg-label-info' };
    if (mapping?.reasonCode === 'NameUnitSuggestionRequiresConfirmation')
        return {
            label: 'Gợi ý cần xác nhận',
            action: 'Xác nhận gợi ý',
            badge: 'bg-label-warning'
        };
    if (state === 'NeedsConfirmation' || state === 1)
        return { label: 'Chưa nhận diện', action: 'Chọn sản phẩm', badge: 'bg-label-warning' };
    return { label: 'Chưa nhận diện', action: 'Chọn sản phẩm', badge: 'bg-label-secondary' };
}

function renderItemCatalogStatusBadge(mapping) {
    const state = getItemCatalogStatePresentation(mapping);
    const reason = mapping?.message
        ? ` title="${escapeHtml(mapping.message)}"`
        : '';
    return `<span class="badge ${state.badge}"${reason}>${state.label}</span>`;
}

function initItemCatalogMappingControls() {
    const product = $('#mapCatalogProductVariant');
    const unit = document.getElementById('mapCatalogUnitConversion');
    if (!product.length || !unit || !$.fn.select2) return;

    product.select2({
        theme: 'bootstrap-5',
        width: '100%',
        dropdownParent: $('#mapInputInvoiceLineModal'),
        placeholder: 'Tìm ProductVariant...',
        minimumInputLength: 1,
        ajax: {
            url: '/admin/api/stock-documents/search-products',
            dataType: 'json',
            delay: 150,
            data: params => ({
                keyword: params.term || '',
                stockDocumentId: Number(
                    document.getElementById('StockDocumentId')?.value || 0)
            }),
            processResults: data => {
                const variants = new Map();
                (Array.isArray(data) ? data : []).forEach(item => {
                    const id = Number(item.productVariantId || 0);
                    if (!id || variants.has(id)) return;
                    variants.set(id, {
                        id,
                        text: [item.productName || item.text, item.sku]
                            .filter(Boolean).join(' · '),
                        productName: item.productName,
                        sku: item.sku
                    });
                });
                return { results: [...variants.values()] };
            }
        }
    });

    product.on('select2:select', async event => {
        await loadItemCatalogVariantConversions(
            Number(event.params.data.id), null);
    });
    product.on('select2:clear', resetItemCatalogConversionSelect);
    unit.addEventListener('change', updateItemCatalogConversionPreview);
}

function resetItemCatalogMappingPanel() {
    const panel = document.getElementById('mapItemCatalogPanel');
    if (panel) panel.classList.add('d-none');
    selectedInputInvoiceDetail = null;
    $('#mapCatalogProductVariant').val(null).trigger('change');
    resetItemCatalogConversionSelect();
    const rowVersion = document.getElementById('mapCatalogRowVersion');
    if (rowVersion) rowVersion.value = '';
}

function resetItemCatalogConversionSelect() {
    const unit = document.getElementById('mapCatalogUnitConversion');
    if (unit)
        unit.innerHTML = '<option value="">Chọn đơn vị quy đổi</option>';
    updateItemCatalogConversionPreview();
}

async function showItemCatalogMappingPanel(detail) {
    const panel = document.getElementById('mapItemCatalogPanel');
    if (!panel || !detail) return;
    panel.classList.remove('d-none');

    const supplier = document.getElementById('mapItemCatalogSupplier');
    if (supplier)
        supplier.textContent = `Supplier: ${detail.supplierLabel || 'Chưa xác định'}`;
    const identity = document.getElementById('mapItemCatalogXmlIdentity');
    if (identity) {
        identity.innerHTML = `
            <div><strong>Mã hàng XML:</strong> ${escapeHtml(detail.supplierItemCode || 'Không có mã hàng trên XML')}</div>
            <div><strong>Tên hàng:</strong> ${escapeHtml(detail.itemName || '-')}</div>
            <div><strong>XML Unit / Quantity:</strong> ${escapeHtml(detail.unitName || '-')} · ${formatNumber(detail.quantity || 0)}</div>`;
    }

    const mapping = detail.itemCatalogMapping;
    const state = getItemCatalogStatePresentation(mapping);
    const status = document.getElementById('mapItemCatalogStatus');
    if (status) {
        status.className = `badge ${state.badge}`;
        status.textContent = state.label;
        status.title = mapping?.message || '';
    }
    const rowVersion = document.getElementById('mapCatalogRowVersion');
    if (rowVersion) rowVersion.value = mapping?.mappingRowVersion || '';

    const variantId = Number(mapping?.productVariantId || 0);
    if (!variantId) {
        $('#mapCatalogProductVariant').val(null).trigger('change');
        resetItemCatalogConversionSelect();
        return;
    }

    const product = $('#mapCatalogProductVariant');
    const label = [mapping.productName || `Variant #${variantId}`, mapping.variantSku]
        .filter(Boolean).join(' · ');
    product.empty().append(new Option(label, String(variantId), true, true))
        .trigger('change');
    await loadItemCatalogVariantConversions(
        variantId, Number(mapping.productUnitConversionId || 0));
}

async function loadItemCatalogVariantConversions(variantId, selectedConversionId) {
    const unit = document.getElementById('mapCatalogUnitConversion');
    const message = document.getElementById('mapInputInvoiceMessage');
    if (!unit || !variantId) return;
    unit.disabled = true;
    unit.innerHTML = '<option value="">Đang tải đơn vị...</option>';
    try {
        const documentId = Number(
            document.getElementById('StockDocumentId')?.value || 0);
        const response = await fetch(
            `/admin/api/stock-documents/product-variants/${variantId}/units?stockDocumentId=${documentId}`, {
                credentials: 'same-origin', cache: 'no-store'
            });
        const api = await readApiResponse(response);
        if (!api.ok)
            throw new Error(api.data?.message || 'Không tải được đơn vị quy đổi.');
        unit.innerHTML = '<option value="">Chọn đơn vị quy đổi</option>';
        (api.data || []).forEach(item => {
            if (!item.productUnitConversionId) return;
            const option = new Option(
                `${item.unitName} · ×${item.factor}`,
                String(item.productUnitConversionId),
                Number(item.productUnitConversionId) === selectedConversionId,
                Number(item.productUnitConversionId) === selectedConversionId);
            option.dataset.unitName = item.unitName || '';
            option.dataset.factor = String(item.factor || '');
            option.dataset.baseUnitId = String(item.baseUnitId || '');
            option.dataset.baseUnitName = item.baseUnitName || '';
            option.dataset.isDefaultForSale = item.isDefaultForSale ? 'true' : 'false';
            unit.appendChild(option);
        });
        updateItemCatalogConversionPreview();
    } catch (error) {
        unit.innerHTML = '<option value="">Không tải được đơn vị</option>';
        if (message) message.textContent = error.message;
    } finally {
        unit.disabled = false;
    }
}

function updateItemCatalogConversionPreview() {
    const unit = document.getElementById('mapCatalogUnitConversion');
    const preview = document.getElementById('mapCatalogConversionPreview');
    if (!unit || !preview) return;
    const option = unit.selectedOptions?.[0];
    const factor = Number(option?.dataset.factor || 0);
    if (!option?.value || factor <= 0) {
        preview.textContent = 'Chọn sản phẩm và đơn vị quy đổi để xem Factor/Base Unit.';
        return;
    }
    const xmlUnit = selectedInputInvoiceDetail?.unitName || '-';
    const quantity = Number(selectedInputInvoiceDetail?.quantity || 0);
    const baseUnit = option.dataset.baseUnitName || 'Base Unit';
    const defaultUnit = [...unit.options].find(
        item => item.dataset.isDefaultForSale === 'true')?.dataset.unitName;
    preview.innerHTML = `
        <strong>${escapeHtml(xmlUnit)}</strong> →
        <strong>${escapeHtml(option.dataset.unitName || '-')}</strong> →
        ×${escapeHtml(option.dataset.factor)} →
        <strong>${escapeHtml(baseUnit)}</strong><br>
        ${formatNumber(quantity)} × ${formatNumber(factor)} =
        <strong>${formatNumber(quantity * factor)} ${escapeHtml(baseUnit)}</strong>
        ${defaultUnit ? `<br>Default Unit: ${escapeHtml(defaultUnit)}` : ''}`;
}

async function loadItemCatalogLineStatuses() {
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    const nodes = document.querySelectorAll(
        '.js-item-map-status, .js-commercial-item-map-status');
    if (!documentId || !nodes.length) return;
    try {
        const response = await fetch(
            `/admin/api/stock-documents/${documentId}/input-invoices/line-maps`, {
                credentials: 'same-origin', cache: 'no-store'
            });
        const api = await readApiResponse(response);
        if (!api.ok) return;
        const items = api.data || [];
        const byLine = new Map(items.map(item =>
            [Number(item.stockDocumentLineId), item]));
        nodes.forEach(node => {
            const item = byLine.get(Number(node.dataset.itemMapLineId));
            const associated = Number(item?.inputInvoiceDetailId || 0) > 0;
            const remembered = item?.itemCatalogMapping?.stateName === 'Confirmed' ||
                item?.itemCatalogMapping?.state === 2;
            const marker = node.classList.contains('js-commercial-item-map-status')
                ? 'js-commercial-item-map-status'
                : 'js-item-map-status';
            node.className = `badge mb-1 ${marker} ${associated
                ? 'bg-label-success' : 'bg-label-warning'}`;
            node.textContent = associated
                ? remembered ? 'Đã ghép · đã ghi nhớ' : 'Đã ghép'
                : 'Chưa đối chiếu';
            node.title = item?.itemCatalogMapping?.message || '';

            const lineId = Number(node.dataset.itemMapLineId);
            const action = document.querySelector(
                `.btn-map-input-invoice-line[data-line-id="${lineId}"]`);
            if (action) {
                action.textContent = associated ? 'Đổi dòng XML' : 'Ghép với XML';
                action.disabled = false;
                action.dataset.inputInvoiceDetailId = String(
                    item?.inputInvoiceDetailId || '');
            }
        });
        const total = items.length;
        const associated = items.filter(item =>
            Number(item.inputInvoiceDetailId || 0) > 0).length;
        const summary = document.getElementById('inputInvoiceMappingSummary');
        if (summary) {
            summary.textContent = `${associated}/${total} đã ghép · ${total - associated} chưa đối chiếu`;
            summary.className = `badge ${associated === total && total > 0
                ? 'bg-label-success' : 'bg-label-warning'}`;
        }
    } catch (error) {
        console.error(error);
    }
}

function bindSaveInputInvoiceLineMap() {
    const btn = document.getElementById('btnSaveInputInvoiceLineMap');
    const useToggle = document.getElementById('mapUseInputInvoice');
    const rememberToggle = document.getElementById('mapRememberItemCatalogMapping');
    const exclusionBox = document.getElementById('mapInputInvoiceExclusionReasonBox');
    if (useToggle && exclusionBox) {
        const syncExclusionReason = () => {
            exclusionBox.classList.toggle('d-none', useToggle.checked);
            if (rememberToggle) {
                rememberToggle.disabled = !useToggle.checked;
                if (!useToggle.checked) rememberToggle.checked = false;
            }
        };
        useToggle.addEventListener('change', syncExclusionReason);
        syncExclusionReason();
    }
    if (!btn) return;

    btn.onclick = async function () {
        const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
        const lineId = Number(document.getElementById('mapStockDocumentLineId')?.value || 0);
        const useInputInvoice = document.getElementById('mapUseInputInvoice')?.checked === true;
        const msg = document.getElementById('mapInputInvoiceMessage');
        const rememberItemCatalogMapping =
            document.getElementById('mapRememberItemCatalogMapping')?.checked === true;
        const exclusionReason =
            document.getElementById('mapInputInvoiceExclusionReason')?.value.trim() || '';

        if (!lineId) {
            if (msg) msg.textContent = 'Không xác định được dòng nhập.';
            return;
        }
        if (rememberItemCatalogMapping &&
            (!useInputInvoice || !selectedInputInvoiceDetailId)) {
            if (msg) msg.textContent = 'Hãy chọn một dòng XML trước khi bật ghi nhớ.';
            return;
        }
        if (!useInputInvoice && !exclusionReason) {
            if (msg) msg.textContent = 'Vui lòng nhập lý do dòng này không thuộc XML.';
            document.getElementById('mapInputInvoiceExclusionReason')?.focus();
            return;
        }

        const response = await fetch(`/admin/api/stock-documents/${documentId}/input-invoices/line-maps`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify({
                stockDocumentLineId: lineId,
                useInputInvoice: useInputInvoice,
                inputInvoiceDetailId: useInputInvoice ? selectedInputInvoiceDetailId : null,
                rememberItemCatalogMapping: rememberItemCatalogMapping,
                mappingRowVersion: rememberItemCatalogMapping
                    ? selectedInputInvoiceDetail?.itemCatalogMapping?.mappingRowVersion || null
                    : null,
                exclusionReason: useInputInvoice ? null : exclusionReason
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
        await refreshInputInvoiceMappingWorkspace();
    };
}

async function refreshInputInvoiceMappingWorkspace() {
    await loadInputInvoicesForStockDocument();
}

async function refreshCommercialReconciliationPreviewAfterXmlMutation() {
    const refresh = window.GaoAppPurchaseReceiptApproval?.refreshReconciliationPreview;
    if (typeof refresh !== 'function') return;
    await refresh();
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

    const exclusionReason = useInputInvoice
        ? null
        : await requestInputInvoiceReconciliationReason(
            'Lý do loại tất cả dòng khỏi XML');
    if (!useInputInvoice && !exclusionReason)
        throw new Error('Bulk exclusion cancelled');

    const response = await fetch(`/admin/api/stock-documents/${documentId}/input-invoices/line-maps/bulk`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({
            useInputInvoice: useInputInvoice,
            exclusionReason
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

async function loadInputInvoiceReconciliation() {
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    const panel = document.getElementById('inputInvoiceReconciliationPanel');
    if (!documentId || !panel) return;
    const reconciliationRenderGeneration =
        beginInputInvoiceReconciliationRenderRequest();
    try {
        const response = await fetch(
            `/admin/api/stock-documents/${documentId}/input-invoices/reconciliation`,
            { credentials: 'same-origin', cache: 'no-store' });
        const api = await readApiResponse(response);
        if (!api.ok) throw new Error(api.data?.message || 'Không tải được đối chiếu.');
        renderInputInvoiceReconciliationIfCurrent(
            api.data,
            reconciliationRenderGeneration,
            false);
    } catch (error) {
        panel.classList.remove('d-none');
        document.getElementById('inputInvoiceReconciliationMessage').textContent = error.message;
        document.getElementById('inputInvoiceReconciliationMessage').className = 'small mt-2 text-danger';
    }
}

function reconciliationStatePresentation(state) {
    const name = typeof state === 'string' ? state : cachedInputInvoiceReconciliation?.stateName;
    const values = {
        NotApplicable: ['Không áp dụng', 'bg-label-secondary'],
        Incomplete: ['Chưa đầy đủ', 'bg-label-warning'],
        Matched: ['Đã khớp', 'bg-label-success'],
        Mismatch: ['Có chênh lệch', 'bg-label-warning'],
        AcceptedMismatch: ['Đã chấp nhận chênh lệch', 'bg-label-primary']
    };
    return values[name] || ['Cần đối chiếu', 'bg-label-warning'];
}

function reconciliationStateIsWarning(state) {
    return state === 'Incomplete' || state === 'Mismatch' || state === 1 || state === 3;
}

function inputInvoiceProductOriginalUnitsText(rows) {
    if (!rows?.length) return 'Không có';
    return rows.map(row => {
        const quantity = `${formatNumber(row.quantity)} ${row.unitName || ''}`.trim();
        if (Number(row.receiptAllocationKind) !== 2) return quantity;
        const outsideStatus = Number(row.outsidePoDecisionStatus) === 2
            ? 'Ngoài PO · Đã chấp nhận'
            : 'Ngoài PO · Chờ xử lý';
        return `${quantity} (${outsideStatus})`;
    })
        .join(' + ');
}

function inputInvoiceProductQuantityResultHtml(product) {
    const unit = escapeHtml(product.baseUnitDisplayName || 'đơn vị gốc');
    const difference = Math.abs(Number(product.quantityDifference || 0));
    if (product.quantityStatus === 'Matched')
        return '<span class="text-success">Khớp</span>';
    if (product.quantityStatus === 'XmlShort')
        return `<span class="text-warning">XML thiếu ${formatNumber(difference)} ${unit}</span>`;
    if (product.quantityStatus === 'XmlExcess')
        return `<span class="text-warning">XML dư ${formatNumber(difference)} ${unit}</span>`;
    return '<span class="text-warning">Chưa đủ dữ liệu</span>';
}

function inputInvoiceProductPriceResultHtml(product) {
    const unit = escapeHtml(product.baseUnitDisplayName || 'đơn vị gốc');
    const difference = Math.abs(Number(product.baseUnitPriceDifference || 0));
    if (product.priceStatus === 'Matched')
        return '<span class="text-success">Giá khớp</span>';
    if (product.priceStatus === 'XmlHigher')
        return `<span class="text-warning">Giá XML cao hơn ${formatWholeVnd(difference)} đ/${unit}</span>`;
    if (product.priceStatus === 'XmlLower')
        return `<span class="text-warning">Giá XML thấp hơn ${formatWholeVnd(difference)} đ/${unit}</span>`;
    return '<span class="text-warning">Chưa đủ dữ liệu</span>';
}

function inputInvoiceProductVatLabel(status) {
    return status === 'Matched' ? 'VAT khớp' : 'VAT cần xem xét';
}

function inputInvoiceProductVatResultText(status) {
    return status === 'Matched' ? 'Khớp' : 'Cần xem xét';
}

function inputInvoiceProductGlobalSummaryText(model) {
    const products = model.productSummaries || [];
    const productCount = Number(model.productCount || 0);
    const matchedCount = Number(model.matchedProductCount || 0);
    const differingCount = Number(model.differingProductCount || 0);
    const unresolvedCount = Number(model.unresolvedXmlDetailCount || 0);
    const vatOnlyReviewCount = products.filter(product =>
        product.vatStatus !== 'Matched' &&
        product.quantityStatus === 'Matched' &&
        product.priceStatus === 'Matched').length;
    const fragments = [];
    if (productCount > 0) {
        fragments.push(`${formatNumber(productCount)} sản phẩm`);
        if (differingCount > 0)
            fragments.push(`${formatNumber(differingCount)} có chênh lệch`);
        if (vatOnlyReviewCount > 0)
            fragments.push(`${formatNumber(vatOnlyReviewCount)} cần xem xét`);
        if (differingCount === 0 && vatOnlyReviewCount === 0 &&
            matchedCount >= productCount)
            fragments.push('Tất cả khớp');
    }
    if (unresolvedCount > 0)
        fragments.push(`${formatNumber(unresolvedCount)} dòng XML chưa nhận diện`);
    return fragments.join(' · ');
}

function inputInvoiceProductSummaryHtml(model) {
    const products = model.productSummaries || [];
    if (!products.length && !Number(model.unresolvedXmlDetailCount || 0)) return '';
    const cards = products.map(product => {
        const receiptText = inputInvoiceProductOriginalUnitsText(product.receiptLines);
        const xmlText = inputInvoiceProductOriginalUnitsText(product.xmlDetails);
        const unit = escapeHtml(product.baseUnitDisplayName || 'đơn vị gốc');
        return `<article class="product-reconciliation-card border rounded-3 p-3"
                         data-product-variant-id="${Number(product.productVariantId || 0)}">
          <div class="fw-semibold">${escapeHtml(product.productDisplayName || 'Sản phẩm')}</div>
          <div class="small mt-2"><span class="text-muted">Phiếu:</span> ${escapeHtml(receiptText)}</div>
          <div class="small"><span class="text-muted">XML:</span> ${escapeHtml(xmlText)}</div>
          <div class="row g-1 small mt-2 align-items-baseline">
            <div class="col-md-7"><span class="text-muted me-2">SL</span>` +
              `${formatNumber(product.receiptBaseQuantity)} ↔ ` +
              `${formatNumber(product.xmlBaseQuantity)} ${unit}</div>
            <div class="col-md-5">${inputInvoiceProductQuantityResultHtml(product)}</div>
            <div class="col-md-7"><span class="text-muted me-2">Giá</span>` +
              `${formatWholeVnd(product.receiptBaseUnitPriceBeforeVat)} ↔ ` +
              `${formatWholeVnd(product.xmlBaseUnitPriceBeforeVat)} đ/${unit}</div>
            <div class="col-md-5">${inputInvoiceProductPriceResultHtml(product)}</div>
            <div class="col-md-7"><span class="text-muted me-2">VAT</span></div>
            <div class="col-md-5">${escapeHtml(inputInvoiceProductVatLabel(product.vatStatus))}</div>
          </div>
        </article>`;
    }).join('');
    const globalSummary = inputInvoiceProductGlobalSummaryText(model);
    return `<section class="product-reconciliation-summary mb-3">
      ${globalSummary ? `<div class="small fw-semibold mb-2">${escapeHtml(globalSummary)}</div>` : ''}
      ${products.length ? `<div class="d-grid gap-2">${cards}</div>` : ''}
    </section>`;
}

function buildInputInvoiceProductSummaryByDetailId(model) {
    const productSummaryByDetailId = new Map();
    const ambiguousDetailIds = new Set();
    for (const product of model.productSummaries || []) {
        for (const xmlDetail of product.xmlDetails || []) {
            const detailId = Number(xmlDetail.inputInvoiceDetailId || 0);
            if (!detailId || ambiguousDetailIds.has(detailId)) continue;
            if (productSummaryByDetailId.has(detailId)) {
                productSummaryByDetailId.delete(detailId);
                ambiguousDetailIds.add(detailId);
                continue;
            }
            productSummaryByDetailId.set(detailId, product);
        }
    }
    return productSummaryByDetailId;
}

function inputInvoiceDetailHasLineAssociation(detail) {
    return (detail.stockDocumentLineIds || []).length > 0 ||
        (detail.receiptLines || []).length > 0;
}

function inputInvoiceRecognizedDetailEvidenceHtml(detail, product) {
    const unit = escapeHtml(product.baseUnitDisplayName || 'đơn vị gốc');
    const associated = inputInvoiceDetailHasLineAssociation(detail);
    return `<div class="small">
      <div class="mb-2">
        <span class="badge bg-label-info">Đã tính vào đối chiếu tổng</span>
        ${associated ? '<span class="badge bg-label-success ms-1">Đã ghép dòng</span>' : ''}
      </div>
      <div><span class="text-muted">SL tổng:</span> ` +
        `${formatNumber(product.receiptBaseQuantity)} ↔ ` +
        `${formatNumber(product.xmlBaseQuantity)} ${unit} · ` +
        `${inputInvoiceProductQuantityResultHtml(product)}</div>
      <div><span class="text-muted">Giá BQ:</span> ` +
        `${formatWholeVnd(product.receiptBaseUnitPriceBeforeVat)} ↔ ` +
        `${formatWholeVnd(product.xmlBaseUnitPriceBeforeVat)} đ/${unit} · ` +
        `${inputInvoiceProductPriceResultHtml(product)}</div>
      <div><span class="text-muted">VAT:</span> ` +
        `${escapeHtml(inputInvoiceProductVatResultText(product.vatStatus))}</div>
    </div>`;
}

function inputInvoiceUnresolvedDetailStatusHtml(detail) {
    if (detail.isIgnored) {
        return `<div class="small">
          <span class="badge bg-label-secondary">Đã bỏ qua</span>
          ${detail.ignoreReason
            ? `<div class="text-muted mt-1">Lý do: ${escapeHtml(detail.ignoreReason)}</div>`
            : ''}
        </div>`;
    }
    return '<span class="badge bg-label-warning">Chưa nhận diện</span>';
}

function inputInvoiceReconciliationIsDiscrepancy(detail) {
    return !['Matched', 'Ignored'].includes(detail.stateName);
}

function formatInputInvoiceReconciliationTolerance(value, decimals) {
    const number = Number(value || 0);
    return number.toFixed(decimals).replace('.', ',');
}

function buildInputInvoiceReconciliationAcceptanceSnapshot(model) {
    const details = model?.details || [];
    const quantityTolerance = Number(model?.quantityTolerance ?? 0.0001);
    const moneyTolerance = Number(model?.moneyTolerance ?? 1);
    const discrepant = details.filter(inputInvoiceReconciliationIsDiscrepancy);
    const quantityCount = details.filter(detail =>
        Math.abs(Number(detail.quantityDifference || 0)) > quantityTolerance).length;
    const moneyVatCount = details.filter(detail =>
        Math.abs(Number(detail.baseUnitPriceDifference ??
            detail.amountDifference ?? 0)) > moneyTolerance ||
        Math.abs(Number(detail.vatAmountDifference || 0)) > moneyTolerance ||
        detail.stateName === 'NeedsReview').length;
    const header = model?.header;
    const headerCount = header && (
        Math.abs(Number(header.subtotalDifference || 0)) > moneyTolerance ||
        Math.abs(Number(header.vatDifference || 0)) > moneyTolerance ||
        Math.abs(Number(header.paymentDifference || 0)) > moneyTolerance ||
        header.needsReview) ? 1 : 0;
    const unresolvedCount = details.filter(detail =>
        ['Unmatched', 'Ignored', 'Incomplete'].includes(detail.stateName)).length;
    return `<strong>Snapshot chênh lệch</strong>
        <div class="small mt-1">${discrepant.length} dòng lệch · ${quantityCount} số lượng · ` +
        `${moneyVatCount + headerCount} tiền/VAT/header · ${unresolvedCount} chưa ghép/bỏ qua</div>`;
}

function focusInputInvoiceReconciliationTarget(target) {
    let element = null;
    if (target === 'summary')
        element = document.getElementById('inputInvoiceReconciliationTitle');
    else {
        const detailEvidence = document.getElementById(
            'inputInvoiceReconciliationDetailEvidence');
        if (detailEvidence) detailEvidence.open = true;
        const selector = target === 'first-mismatch'
            ? '[data-recon-focus="first-mismatch"]'
            : target === 'action' ? '[data-recon-focus="action"]' : null;
        if (selector && typeof document.querySelectorAll === 'function') {
            const candidates = Array.from(document.querySelectorAll(selector));
            element = candidates.find(candidate => candidate.offsetParent !== null) ||
                candidates[0] || null;
        } else if (selector) {
            element = document.querySelector(selector);
        }
    }
    element?.focus();
    return Boolean(element);
}

function renderInputInvoiceReconciliation(model) {
    if (!model?.isCommercialPreview) window.GaoReceiptInvoiceFollowUp?.refresh();
    const panel = document.getElementById('inputInvoiceReconciliationPanel');
    if (!panel || !model) return;
    const preview = model.isCommercialPreview === true;
    panel.classList.remove('d-none');
    const state = reconciliationStatePresentation(model.stateName || model.state);
    const badge = document.getElementById('inputInvoiceReconciliationState');
    badge.textContent = state[0];
    badge.className = `badge ${state[1]}`;
    const message = document.getElementById('inputInvoiceReconciliationMessage');
    message.textContent = preview
        ? `Xem trước từ giá và thuế chưa lưu · ${model.message || ''}`
        : model.isLateAssociationException
        ? `Ngoại lệ XML đến sau xác nhận · chỉ đọc. ${model.message || ''}`
        : model.message || '';
    message.className = `small mt-2 ${preview
        ? 'text-info'
        : model.isLateAssociationException
        ? 'alert alert-warning py-2'
        : reconciliationStateIsWarning(model.stateName || model.state)
            ? 'text-warning' : model.stateName === 'Matched'
                ? 'text-success' : 'text-muted'}`;
    const tolerance = document.getElementById('inputInvoiceReconciliationTolerance');
    if (tolerance) tolerance.textContent =
        `Sai số cho phép: ${formatInputInvoiceReconciliationTolerance(model.quantityTolerance, 4)} đơn vị gốc · ` +
        `giá quy đổi/VAT/header: ${formatInputInvoiceReconciliationTolerance(model.moneyTolerance, 0)} ₫.`;

    const header = model.header;
    document.getElementById('inputInvoiceReconciliationHeader').innerHTML = header ? `
        <div class="table-responsive"><table class="table table-sm align-middle mb-0">
        <thead><tr><th>Tiêu chí</th><th class="text-end">Phiếu nhập</th><th class="text-end">XML</th><th class="text-end">Lệch</th></tr></thead>
        <tbody>
        <tr><td>Trước VAT</td><td class="text-end">${formatNumber(header.receiptSubtotalBeforeVat)}</td><td class="text-end">${formatNumber(header.xmlTotalBeforeTax)}</td><td class="text-end">${formatNumber(header.subtotalDifference)}</td></tr>
        <tr><td>VAT</td><td class="text-end">${formatNumber(header.receiptVatAmount)}</td><td class="text-end">${formatNumber(header.xmlTaxAmount)}</td><td class="text-end">${formatNumber(header.vatDifference)}</td></tr>
        <tr><td>Tổng hàng sau VAT</td><td class="text-end">${formatNumber(header.receiptGoodsTotal)}</td><td class="text-end">${formatNumber(header.xmlPaymentAmount)}</td><td class="text-end">${formatNumber(header.paymentDifference)}</td></tr>
        </tbody></table></div>
        ${header.needsReview ? `<div class="alert alert-warning py-2 mt-2 mb-0">${escapeHtml(header.reason || 'Header XML cần xem xét.')}</div>` : ''}` : '';

    const confirmed = preview || model.isConfirmedReadOnly === true ||
        document.getElementById('IsConfirmedReceipt')?.value === 'true';
    const details = model.details || [];
    const productSummary = inputInvoiceProductSummaryHtml(model);
    const productSummaryByDetailId = buildInputInvoiceProductSummaryByDetailId(model);
    let firstMismatchAssigned = false;
    let firstActionAssigned = false;
    const detailRows = details.map(detail => {
        const mismatchFocus = inputInvoiceReconciliationIsDiscrepancy(detail) &&
            !firstMismatchAssigned;
        if (mismatchFocus) firstMismatchAssigned = true;
        let action = '';
        if (!confirmed && detail.isIgnored)
            action = `<button class="btn btn-sm btn-outline-secondary js-recon-unignore" data-detail-id="${detail.inputInvoiceDetailId}">Bỏ trạng thái bỏ qua</button>`;
        else if (!confirmed && detail.stateName === 'Unmatched')
            action = `<button class="btn btn-sm btn-outline-warning js-recon-ignore" data-detail-id="${detail.inputInvoiceDetailId}">Bỏ qua có lý do</button>`;
        if (action && !firstActionAssigned) {
            action = action.replace('<button ', '<button data-recon-focus="action" ');
            firstActionAssigned = true;
        }
        const recognizedProduct = productSummaryByDetailId.get(
            Number(detail.inputInvoiceDetailId || 0));
        const detailEvidence = recognizedProduct
            ? inputInvoiceRecognizedDetailEvidenceHtml(detail, recognizedProduct)
            : inputInvoiceUnresolvedDetailStatusHtml(detail);
        return `
        <tr data-recon-detail-id="${Number(detail.inputInvoiceDetailId || 0)}"
            ${mismatchFocus ? 'tabindex="0" data-recon-focus="first-mismatch"' : ''}>
          <td><strong>${escapeHtml(detail.itemName || `#${detail.inputInvoiceDetailId}`)}</strong>
              <div class="small text-muted">${formatNumber(detail.xmlQuantity)} ` +
                `${escapeHtml(detail.unitName || '')}</div></td>
          <td>${detailEvidence}</td>
          <td class="text-end">${action}</td>
        </tr>`;
    }).join('');
    const mobileCards = details.map((detail, index) => {
        let action = '';
        if (!confirmed && detail.isIgnored)
            action = `<button data-recon-focus="action" class="btn btn-sm btn-outline-secondary js-recon-unignore" data-detail-id="${detail.inputInvoiceDetailId}">Bỏ trạng thái bỏ qua</button>`;
        else if (!confirmed && detail.stateName === 'Unmatched')
            action = `<button data-recon-focus="action" class="btn btn-sm btn-outline-warning js-recon-ignore" data-detail-id="${detail.inputInvoiceDetailId}">Bỏ qua có lý do</button>`;
        const recognizedProduct = productSummaryByDetailId.get(
            Number(detail.inputInvoiceDetailId || 0));
        const detailEvidence = recognizedProduct
            ? inputInvoiceRecognizedDetailEvidenceHtml(detail, recognizedProduct)
            : inputInvoiceUnresolvedDetailStatusHtml(detail);
        return `
        <article class="recon-stacked-card border rounded-3 p-3 mb-2 ${index === 0 ? '' : ''}"
                 data-recon-detail-id="${Number(detail.inputInvoiceDetailId || 0)}"
                 ${inputInvoiceReconciliationIsDiscrepancy(detail) && index === details.findIndex(inputInvoiceReconciliationIsDiscrepancy)
                    ? 'tabindex="0" data-recon-focus="first-mismatch"' : ''}>
          <div><strong>${escapeHtml(detail.itemName || `#${detail.inputInvoiceDetailId}`)}</strong></div>
          <div class="small text-muted">${formatNumber(detail.xmlQuantity)} ` +
            `${escapeHtml(detail.unitName || '')}</div>
          <div class="mt-2">${detailEvidence}</div>
          ${action ? `<div class="mt-2">${action}</div>` : ''}
        </article>`;
    }).join('');
    const detailEvidence = details.length ? `
        <details id="inputInvoiceReconciliationDetailEvidence" class="mt-2">
        <summary class="small fw-semibold">Xem chi tiết</summary>
        <div class="mt-2">
        <div class="table-responsive d-none d-md-block"><table class="table table-sm align-middle mb-0">
        <thead><tr><th>XML</th><th>Đối chiếu tổng</th><th></th></tr></thead>
        <tbody>${detailRows}</tbody></table></div>
        <div class="d-md-none">${mobileCards}</div>
        </div>
        </details>` : '';
    document.getElementById('inputInvoiceReconciliationDetails').innerHTML =
        `${productSummary}${detailEvidence}`;

    const acceptance = document.getElementById('inputInvoiceReconciliationAcceptance');
    if (preview) {
        acceptance.innerHTML =
            '<div class="alert alert-info mb-0">Xem trước chỉ dùng giá và thuế đang nhập. ' +
            'Dữ liệu đối chiếu chính thức chỉ được cập nhật trong giao dịch duyệt.</div>';
    } else acceptance.innerHTML = model.acceptedAtUtc
        ? `<div class="alert alert-info mb-0">Đã chấp nhận bởi user #${model.acceptedByUserId || '-'} lúc ${formatDateTime(model.acceptedAtUtc)}.<br>${escapeHtml(model.acceptanceReason || '')}</div>`
        : (!confirmed && (model.stateName === 'Incomplete' || model.stateName === 'Mismatch' || model.state === 1 || model.state === 3))
            ? `<button type="button" class="btn btn-warning js-recon-accept"
                       ${firstActionAssigned ? '' : 'data-recon-focus="action"'}>Quản lý xác nhận để duyệt</button>`
            : '';
}

window.renderInputInvoiceReconciliationPreview = function (model, requestGeneration) {
    return renderInputInvoiceReconciliationIfCurrent(model, requestGeneration, true);
};

function bindInputInvoiceReconciliationActions() {
    const panel = document.getElementById('inputInvoiceReconciliationPanel');
    if (panel) panel.addEventListener('click', async function (event) {
        const ignore = event.target.closest('.js-recon-ignore');
        const unignore = event.target.closest('.js-recon-unignore');
        const accept = event.target.closest('.js-recon-accept');
        if (ignore) {
            const reason = await requestInputInvoiceReconciliationReason('Lý do bỏ qua dòng XML');
            if (reason) await mutateInputInvoiceReconciliation(
                `details/${ignore.dataset.detailId}/ignore`, { reason });
        } else if (unignore) {
            await mutateInputInvoiceReconciliation(
                `details/${unignore.dataset.detailId}/unignore`, { reason: null });
        } else if (accept) {
            const reason = await requestInputInvoiceReconciliationReason(
                'Lý do chấp nhận chênh lệch',
                buildInputInvoiceReconciliationAcceptanceSnapshot(
                    cachedInputInvoiceReconciliation));
            if (reason) await mutateInputInvoiceReconciliation('accept', {
                reason,
                expectedEvidenceFingerprint: cachedInputInvoiceReconciliation?.evidenceFingerprint
            });
        }
    });
    if (panel) panel.addEventListener('keydown', function (event) {
        if (!event.target.closest?.('[data-recon-focus="first-mismatch"]') ||
            (event.key !== 'ArrowDown' && event.key !== 'Enter')) return;
        event.preventDefault();
        focusInputInvoiceReconciliationTarget('action');
    });

    const confirm = document.getElementById('btnConfirmInputInvoiceReconciliationReason');
    if (confirm) confirm.addEventListener('click', function () {
        const input = document.getElementById('inputInvoiceReconciliationReason');
        const reason = input?.value.trim() || '';
        const error = document.getElementById('inputInvoiceReconciliationReasonMessage');
        if (!reason) {
            error.textContent = 'Vui lòng nhập lý do.';
            input?.focus();
            return;
        }
        const resolve = pendingReconciliationReasonResolve;
        pendingReconciliationReasonResolve = null;
        inputInvoiceReconciliationReasonModalInstance?.hide();
        resolve?.(reason);
    });

    const title = document.getElementById('inputInvoiceReconciliationTitle');
    if (title) title.addEventListener('keydown', function (event) {
        if (event.key !== 'ArrowDown' && event.key !== 'Enter') return;
        event.preventDefault();
        if (!focusInputInvoiceReconciliationTarget('first-mismatch'))
            focusInputInvoiceReconciliationTarget('action');
    });
}

function requestInputInvoiceReconciliationReason(title, snapshotHtml = null) {
    if (!inputInvoiceReconciliationReasonModalInstance) return Promise.resolve(null);
    document.getElementById('inputInvoiceReconciliationReasonTitle').textContent = title;
    document.getElementById('inputInvoiceReconciliationReason').value = '';
    document.getElementById('inputInvoiceReconciliationReasonMessage').textContent = '';
    const snapshot = document.getElementById('inputInvoiceReconciliationReasonSnapshot');
    if (snapshot) {
        snapshot.innerHTML = snapshotHtml || '';
        snapshot.classList.toggle('d-none', !snapshotHtml);
    }
    inputInvoiceReconciliationReasonReturnFocus = document.activeElement;
    inputInvoiceReconciliationReasonModalInstance.show();
    setTimeout(() => document.getElementById('inputInvoiceReconciliationReason')?.focus(), 150);
    return new Promise(resolve => { pendingReconciliationReasonResolve = resolve; });
}

async function mutateInputInvoiceReconciliation(path, body) {
    const documentId = Number(document.getElementById('StockDocumentId')?.value || 0);
    const response = await fetch(
        `/admin/api/stock-documents/${documentId}/input-invoices/reconciliation/${path}`,
        {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
    const api = await readApiResponse(response);
    if (!api.ok) {
        document.getElementById('inputInvoiceReconciliationMessage').textContent =
            api.data?.message || 'Cập nhật đối chiếu thất bại.';
        return;
    }
    cachedInputInvoiceReconciliation = api.data;
    renderInputInvoiceReconciliation(api.data);
    focusInputInvoiceReconciliationTarget('summary');
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
                        note: note,
                        rowVersion: window.stockDocumentPage.rowVersion
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
                        returnToEdit: false,
                        rowVersion: window.stockDocumentPage.rowVersion
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
