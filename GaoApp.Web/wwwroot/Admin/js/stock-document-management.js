let createReceiptModalInstance = null;
let editLineModalInstance = null;
let submitApprovalModalInstance = null;
let approveModalInstance = null;
let rejectModalInstance = null;

let supplierLookupTimer = null;
let editHeaderSaveTimer = null;

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

        bindDeleteLine();
        bindOpenEditLineModal();
        bindSaveEditLine();
        bindEditUnitChange();

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

    if (createReceiptModal) createReceiptModalInstance = new bootstrap.Modal(createReceiptModal);
    if (editLineModal) editLineModalInstance = new bootstrap.Modal(editLineModal);
    if (submitApprovalModal) submitApprovalModalInstance = new bootstrap.Modal(submitApprovalModal);
    if (approveModal) approveModalInstance = new bootstrap.Modal(approveModal);
    if (rejectModal) rejectModalInstance = new bootstrap.Modal(rejectModal);
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

function focusQuickLookup() {
    const el = $('#quickLookupInput');
    if (!el.length) return;
    if (!window.jQuery || !$.fn.select2) return;

    setTimeout(function () {
        el.select2('open');
    }, 80);
}

function focusQuickQty() {
    const qtyInput = document.getElementById('quickQty');
    if (!qtyInput) return;

    setTimeout(function () {
        qtyInput.focus();
        qtyInput.select();
    }, 60);
}

function renderStatusBadge(status) {
    switch (status) {
        case 1:
            return `<span class="badge bg-label-primary">Draft</span>`;
        case 2:
            return `<span class="badge bg-label-warning">PendingApproval</span>`;
        case 3:
            return `<span class="badge bg-label-success">Confirmed</span>`;
        case 4:
            return `<span class="badge bg-label-danger">Rejected</span>`;
        case 5:
            return `<span class="badge bg-label-secondary">Cancelled</span>`;
        default:
            return `<span class="badge bg-label-dark">Unknown</span>`;
    }
}

/* =========================================================
 * INDEX
 * ========================================================= */

async function loadReceiptList() {
    const workingBody = document.getElementById('tbodyWorking');
    const pendingBody = document.getElementById('tbodyPending');
    const confirmedBody = document.getElementById('tbodyConfirmed');

    if (!workingBody || !pendingBody || !confirmedBody) return;

    try {
        const response = await fetch('/admin/api/stock-documents/receipts');
        const api = await readApiResponse(response);

        if (!api.ok) {
            const message = api.data?.message || 'Không tải được danh sách phiếu nhập.';
            workingBody.innerHTML = `<tr><td colspan="7" class="text-center text-danger">${message}</td></tr>`;
            pendingBody.innerHTML = `<tr><td colspan="7" class="text-center text-danger">${message}</td></tr>`;
            confirmedBody.innerHTML = `<tr><td colspan="7" class="text-center text-danger">${message}</td></tr>`;
            return;
        }

        const data = api.data || [];

        const working = data.filter(x => x.status === 1 || x.status === 4);
        const pending = data.filter(x => x.status === 2);
        const confirmed = data.filter(x => x.status === 3);

        workingBody.innerHTML = renderWorkingRows(working);
        pendingBody.innerHTML = renderPendingRows(pending);
        confirmedBody.innerHTML = renderConfirmedRows(confirmed);
    } catch {
        const message = 'Có lỗi khi tải danh sách phiếu nhập.';
        workingBody.innerHTML = `<tr><td colspan="7" class="text-center text-danger">${message}</td></tr>`;
        pendingBody.innerHTML = `<tr><td colspan="7" class="text-center text-danger">${message}</td></tr>`;
        confirmedBody.innerHTML = `<tr><td colspan="7" class="text-center text-danger">${message}</td></tr>`;
    }
}

function renderWorkingRows(items) {
    if (!items || items.length === 0) {
        return `<tr><td colspan="7" class="text-center text-muted">Không có dữ liệu</td></tr>`;
    }

    return items.map(x => `
        <tr>
            <td>${escapeHtml(x.documentNo || '')}</td>
            <td>${formatDate(x.documentDate)}</td>
            <td>${escapeHtml(x.warehouseName || '')}</td>
            <td>${escapeHtml(x.supplierName || '')}</td>
            <td>${renderStatusBadge(x.status)}</td>
            <td class="text-end">${formatNumber(x.totalAmount)}</td>
            <td>
                <a class="btn btn-sm btn-primary" href="/admin/stock-documents/${x.id}">
                    Mở
                </a>
            </td>
        </tr>
    `).join('');
}

function renderPendingRows(items) {
    if (!items || items.length === 0) {
        return `<tr><td colspan="7" class="text-center text-muted">Không có dữ liệu</td></tr>`;
    }

    return items.map(x => `
        <tr>
            <td>${escapeHtml(x.documentNo || '')}</td>
            <td>${formatDate(x.documentDate)}</td>
            <td>${escapeHtml(x.warehouseName || '')}</td>
            <td>${escapeHtml(x.supplierName || '')}</td>
            <td>${formatDate(x.submittedAtUtc)}</td>
            <td class="text-end">${formatNumber(x.totalAmount)}</td>
            <td>
                <a class="btn btn-sm btn-warning" href="/admin/stock-documents/${x.id}">
                    Duyệt
                </a>
            </td>
        </tr>
    `).join('');
}

function renderConfirmedRows(items) {
    if (!items || items.length === 0) {
        return `<tr><td colspan="7" class="text-center text-muted">Không có dữ liệu</td></tr>`;
    }

    return items.map(x => `
        <tr>
            <td>${escapeHtml(x.documentNo || '')}</td>
            <td>${formatDate(x.documentDate)}</td>
            <td>${escapeHtml(x.warehouseName || '')}</td>
            <td>${escapeHtml(x.supplierName || '')}</td>
            <td>${formatDate(x.approvedAtUtc)}</td>
            <td class="text-end">${formatNumber(x.totalAmount)}</td>
            <td>
                <a class="btn btn-sm btn-outline-primary" href="/admin/stock-documents/${x.id}">
                    Xem
                </a>
            </td>
        </tr>
    `).join('');
}

function bindOpenCreateReceiptModal() {
    const btn = document.getElementById('btnCreateReceipt');
    if (!btn || !createReceiptModalInstance) return;

    btn.addEventListener('click', async function () {
        await loadWarehouseOptions();
        resetCreateReceiptForm();
        createReceiptModalInstance.show();
    });
}

async function loadWarehouseOptions() {
    const select = document.getElementById('createWarehouseId');
    if (!select) return;

    const response = await fetch('/admin/api/warehouses/select2?term=');
    const api = await readApiResponse(response);

    if (!api.ok) {
        select.innerHTML = `<option value="">Không tải được kho</option>`;
        return;
    }

    const items = api.data?.results || [];

    select.innerHTML = `<option value="">-- Chọn kho --</option>` +
        items.map(x => `<option value="${x.id}">${escapeHtml(x.text)}</option>`).join('');
}

function resetCreateReceiptForm() {
    const supplierList = document.getElementById('supplierLookupList');
    const supplierKeyword = document.getElementById('createSupplierKeyword');
    const supplierId = document.getElementById('createSupplierId');
    const note = document.getElementById('createNote');
    const msg = document.getElementById('createReceiptMessage');
    const dateInput = document.getElementById('createDocumentDate');
    const warehouseSelect = document.getElementById('createWarehouseId');

    if (supplierList) supplierList.innerHTML = '';
    if (supplierKeyword) supplierKeyword.value = '';
    if (supplierId) supplierId.value = '';
    if (note) note.value = '';
    if (msg) msg.textContent = '';
    if (warehouseSelect) warehouseSelect.value = '';

    if (dateInput) {
        const now = new Date();
        const local = new Date(now.getTime() - now.getTimezoneOffset() * 60000)
            .toISOString()
            .slice(0, 16);
        dateInput.value = local;
    }
}

function bindSupplierLookup() {
    const input = document.getElementById('createSupplierKeyword');
    const list = document.getElementById('supplierLookupList');
    const hiddenId = document.getElementById('createSupplierId');

    if (!input || !list || !hiddenId) return;

    input.addEventListener('input', function () {
        const keyword = input.value.trim();
        hiddenId.value = '';

        if (supplierLookupTimer) {
            clearTimeout(supplierLookupTimer);
        }

        supplierLookupTimer = setTimeout(async () => {
            if (!keyword) {
                list.innerHTML = '';
                return;
            }

            const response = await fetch(`/admin/api/suppliers/select2?term=${encodeURIComponent(keyword)}`);
            const api = await readApiResponse(response);

            if (!api.ok) {
                list.innerHTML = `<div class="list-group-item text-danger">Không tải được nhà cung cấp</div>`;
                return;
            }

            const items = api.data?.results || [];

            if (!items.length) {
                list.innerHTML = `<div class="list-group-item text-muted">Không tìm thấy nhà cung cấp</div>`;
                return;
            }

            list.innerHTML = items.map(x => `
                <button type="button"
                        class="list-group-item list-group-item-action supplier-item"
                        data-id="${x.id}"
                        data-name="${escapeHtml(x.text)}">
                    ${escapeHtml(x.text)}
                </button>
            `).join('');

            list.querySelectorAll('.supplier-item').forEach(btn => {
                btn.onclick = function () {
                    hiddenId.value = this.dataset.id;
                    input.value = this.dataset.name;
                    list.innerHTML = '';
                };
            });
        }, 250);
    });
}

function bindCreateReceiptModal() {
    const btn = document.getElementById('btnSubmitCreateReceipt');
    if (!btn) return;

    btn.addEventListener('click', async function () {
        const warehouseId = document.getElementById('createWarehouseId')?.value || '';
        const supplierId = document.getElementById('createSupplierId')?.value || '';
        const note = document.getElementById('createNote')?.value || '';
        const dateValue = document.getElementById('createDocumentDate')?.value || '';
        const msg = document.getElementById('createReceiptMessage');

        if (!warehouseId) {
            if (msg) msg.textContent = 'Vui lòng chọn kho.';
            return;
        }

        try {
            const response = await fetch('/admin/api/stock-documents/receipts', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    warehouseId: Number(warehouseId),
                    supplierId: supplierId ? Number(supplierId) : null,
                    documentDate: dateValue ? new Date(dateValue).toISOString() : null,
                    note: note
                })
            });

            const api = await readApiResponse(response);

            if (!api.ok) {
                if (msg) msg.textContent = api.data?.message || 'Tạo phiếu thất bại.';
                return;
            }

            window.location.href = `/admin/stock-documents/${api.data.id}`;
        } catch {
            if (msg) msg.textContent = 'Có lỗi khi tạo phiếu.';
        }
    });
}

function bindCreateReceiptEnterSubmit() {
    document.addEventListener('keydown', function (e) {
        const modalEl = document.getElementById('createReceiptModal');
        if (!modalEl || !modalEl.classList.contains('show')) return;

        if (e.key === 'Enter') {
            const active = document.activeElement;
            if (active && active.tagName === 'TEXTAREA') return;

            e.preventDefault();
            const btn = document.getElementById('btnSubmitCreateReceipt');
            if (btn) btn.click();
        }
    });
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

    if (totalEl) {
        totalEl.textContent = formatNumber(detail.totalAmount || 0);
    }

    if (totalInputEl) {
        totalInputEl.value = formatNumber(detail.totalAmount || 0);
    }
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
        if (container) {
            container.innerHTML = html;
        }

        bindDeleteLine();
        bindOpenEditLineModal();
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
            if (note) note.value = '';
            if (msg) msg.textContent = '';
            submitApprovalModalInstance.show();
        };
    }

    if (btnOpenApprove && approveModalInstance) {
        btnOpenApprove.onclick = function () {
            const note = document.getElementById('approveNote');
            const msg = document.getElementById('approveMessage');
            if (note) note.value = '';
            if (msg) msg.textContent = '';
            approveModalInstance.show();
        };
    }

    if (btnOpenReject && rejectModalInstance) {
        btnOpenReject.onclick = function () {
            const note = document.getElementById('rejectNote');
            const msg = document.getElementById('rejectMessage');
            if (note) note.value = '';
            if (msg) msg.textContent = '';
            rejectModalInstance.show();
        };
    }
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
            body: JSON.stringify({ note: note })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Gửi duyệt thất bại.';
            return;
        }

        if (submitApprovalModalInstance) submitApprovalModalInstance.hide();

        alert(api.data?.message || 'Đã gửi duyệt.');
        window.location.href = api.data?.redirectUrl || '/admin/stock-documents';
    };
}

function bindApprove() {
    const btn = document.getElementById('btnApprove');
    if (!btn) return;

    btn.onclick = async function () {
        const note = document.getElementById('approveNote')?.value || '';
        const msg = document.getElementById('approveMessage');
        const documentId = window.stockDocumentPage.documentId;

        const response = await fetch(`/admin/api/stock-documents/${documentId}/approve`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ note: note })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Duyệt phiếu thất bại.';
            return;
        }

        if (approveModalInstance) approveModalInstance.hide();
        alert(api.data?.message || 'Duyệt thành công.');
        window.location.href = api.data?.redirectUrl || "/admin/stock-documents";
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
            body: JSON.stringify({ note: note })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Từ chối duyệt thất bại.';
            return;
        }

        if (rejectModalInstance) rejectModalInstance.hide();
        alert(api.data?.message || 'Đã từ chối duyệt.');
        window.location.href = api.data?.redirectUrl || "/admin/stock-documents";
    };
}

/* =========================================================
 * EDIT - LINE ACTIONS
 * ========================================================= */

function bindDeleteLine() {
    const canEditLines = document.getElementById('CanEditLines')?.value === 'true';
    if (!canEditLines) return;

    document.querySelectorAll('.btn-delete-line').forEach(btn => {
        btn.onclick = async function () {
            const lineId = this.dataset.lineId;
            const documentId = window.stockDocumentPage.documentId;

            if (!confirm('Bạn có chắc muốn xóa dòng này không?')) return;

            const response = await fetch(`/admin/api/stock-documents/${documentId}/lines/${lineId}`, {
                method: 'DELETE'
            });

            const api = await readApiResponse(response);

            if (!api.ok) {
                alert(api.data?.message || 'Xóa dòng thất bại.');
                return;
            }

            await refreshStockDocumentDetailUI();
            focusQuickLookup();
        };
    });
}

function bindOpenEditLineModal() {
    const canEditLines = document.getElementById('CanEditLines')?.value === 'true';
    if (!canEditLines) return;

    document.querySelectorAll('.btn-edit-line').forEach(btn => {
        btn.onclick = async function () {
            const lineId = this.dataset.lineId || '';
            const variantId = this.dataset.variantId || '';
            const unitId = this.dataset.unitId || '';
            const quantity = this.dataset.quantity || '';
            const unitCost = this.dataset.unitCost || '';
            const note = this.dataset.note || '';

            const idEl = document.getElementById('editLineId');
            const qtyEl = document.getElementById('editLineQuantity');
            const unitCostEl = document.getElementById('editLineUnitCost');
            const noteEl = document.getElementById('editLineNote');
            const msgEl = document.getElementById('editLineMessage');

            if (idEl) idEl.value = lineId;
            if (qtyEl) qtyEl.value = quantity;
            if (unitCostEl) unitCostEl.value = unitCost;
            if (noteEl) noteEl.value = note;
            if (msgEl) msgEl.textContent = '';

            await loadVariantUnitsToEditModal(variantId, unitId);

            if (editLineModalInstance) editLineModalInstance.show();
        };
    });
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
        const unitCost = Number(document.getElementById('editLineUnitCost')?.value || 0);
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
                unitCost: unitCost,
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

function initEditSelect2Mode() {
    initWarehouseSelect2ForEdit();
    initSupplierSelect2ForEdit();
    initQuickLookupSelect2();
    bindEditHeaderAutoSave();
    bindQuickAddLineBySelect2();
    applyEditReadonlyState();
    focusQuickLookup();
}

function applyEditReadonlyState() {
    const canEditHeader = document.getElementById('CanEditHeader')?.value === 'true';
    const canEditLines = document.getElementById('CanEditLines')?.value === 'true';

    if (!canEditHeader) {
        $('#WarehouseId').prop('disabled', true);
        $('#SupplierId').prop('disabled', true);

        const documentNote = document.getElementById('DocumentNote');
        const approvalNote = document.getElementById('ApprovalNote');

        if (documentNote) documentNote.disabled = true;
        if (approvalNote) approvalNote.disabled = true;
    }

    if (!canEditLines) {
        $('#quickLookupInput').prop('disabled', true);

        const quickQty = document.getElementById('quickQty');
        const btnQuickAddLine = document.getElementById('btnQuickAddLine');

        if (quickQty) quickQty.disabled = true;
        if (btnQuickAddLine) btnQuickAddLine.disabled = true;
    }
}

function initWarehouseSelect2ForEdit() {
    const el = $('#WarehouseId');
    if (!el.length) return;

    const currentId = $('#CurrentWarehouseId').val();
    const currentText = $('#CurrentWarehouseText').val();

    el.select2({
        theme: 'bootstrap-5',
        width: '100%',
        placeholder: 'Chọn kho...',
        allowClear: false,
        ajax: {
            url: '/admin/api/warehouses/select2',
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

function initSupplierSelect2ForEdit() {
    const el = $('#SupplierId');
    if (!el.length) return;

    const currentId = $('#CurrentSupplierId').val();
    const currentText = $('#CurrentSupplierText').val();

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
        placeholder: 'Quét barcode / nhập SKU / gõ tên sản phẩm...',
        minimumInputLength: 1,
        ajax: {
            url: '/admin/stock-documents/product-lookup-select2',
            dataType: 'json',
            delay: 150,
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
        focusQuickQty();
    });

    el.on('select2:clear', function () {
        el.removeData('selected-item');
    });

    bindQuickLookupEnterAutoSelect();
}

function formatQuickLookupResult(item) {
    if (!item.id) return item.text || '';

    const title = escapeHtml(item.productName || '');

    const meta = [
        item.sku ? 'SKU: ' + escapeHtml(item.sku) : '',
        item.unitName ? 'Đơn vị: ' + escapeHtml(item.unitName) : '',
        item.factor ? 'x' + item.factor : '',
        item.barcode ? 'BC: ' + escapeHtml(item.barcode) : '',
        item.isBaseUnitFallback ? 'Đơn vị gốc' : '',
        item.sourceType ? escapeHtml(item.sourceType) : ''
    ].filter(Boolean).join(' | ');

    return `
        <div>
            <div class="select2-result-title">${title}</div>
            <div class="select2-result-meta">${meta}</div>
        </div>
    `;
}

function formatQuickLookupSelection(item) {
    if (!item || !item.id) return item.text || 'Chọn sản phẩm';

    const parts = [];
    if (item.productName) parts.push(item.productName);
    if (item.unitName) parts.push(item.unitName);
    if (item.factor) parts.push('x' + item.factor);
    if (item.barcode) parts.push(item.barcode);

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

            if (!api.ok || !api.data || !api.data.results || api.data.results.length !== 1) return;

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

function bindEditHeaderAutoSave() {
    const canEditHeader = document.getElementById('CanEditHeader')?.value === 'true';
    if (!canEditHeader) return;

    const warehouseEl = $('#WarehouseId');
    const supplierEl = $('#SupplierId');
    const documentNote = document.getElementById('DocumentNote');
    const approvalNote = document.getElementById('ApprovalNote');

    if (warehouseEl.length) {
        warehouseEl.on('change', queueEditHeaderSave);
    }

    if (supplierEl.length) {
        supplierEl.on('change', queueEditHeaderSave);
    }

    if (documentNote) {
        documentNote.addEventListener('input', queueEditHeaderSave);
    }

    if (approvalNote) {
        approvalNote.addEventListener('input', queueEditHeaderSave);
    }
}

function queueEditHeaderSave() {
    if (editHeaderSaveTimer) {
        clearTimeout(editHeaderSaveTimer);
    }

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

function bindQuickAddLineBySelect2() {
    const canEditLines = document.getElementById('CanEditLines')?.value === 'true';
    if (!canEditLines) return;

    const btn = document.getElementById('btnQuickAddLine');
    const qtyInput = document.getElementById('quickQty');

    if (qtyInput) {
        qtyInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                if (btn) btn.click();
            }
        });
    }

    if (!btn) return;

    btn.onclick = async function () {
        const selected = $('#quickLookupInput').data('selected-item');
        const qty = Number(document.getElementById('quickQty')?.value || 0);
        const stockDocumentId = Number(document.getElementById('StockDocumentId')?.value || 0);

        if (!selected) {
            alert('Vui lòng chọn sản phẩm.');
            focusQuickLookup();
            return;
        }

        if (qty <= 0) {
            alert('Số lượng phải lớn hơn 0.');
            const quickQtyInput = document.getElementById('quickQty');
            if (quickQtyInput) {
                quickQtyInput.focus();
                quickQtyInput.select();
            }
            return;
        }

        const response = await fetch(`/admin/api/stock-documents/${stockDocumentId}/lines`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                productVariantId: Number(selected.productVariantId),
                unitId: selected.unitId ? Number(selected.unitId) : null,
                quantity: qty
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            alert(api.data?.message || 'Thêm dòng thất bại.');
            focusQuickLookup();
            return;
        }

        await refreshStockDocumentDetailUI();

        $('#quickLookupInput').val(null).trigger('change');
        $('#quickLookupInput').removeData('selected-item');

        const quickQtyEl = document.getElementById('quickQty');
        if (quickQtyEl) {
            quickQtyEl.value = '1';
        }

        focusQuickLookup();
    };
}