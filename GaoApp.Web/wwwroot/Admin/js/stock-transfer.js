let createStockTransferModalInstance = null;
let editStockTransferLineModalInstance = null;
let rejectStockTransferModalInstance = null;
let stockTransferHeaderSaveTimer = null;

const stockTransferUiState = {
    currentDetail: null
};

document.addEventListener('DOMContentLoaded', function () {
    if (!window.stockTransferPage) return;

    initStockTransferModalInstances();

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

/* ========================= COMMON ========================= */

async function readStockTransferApiResponse(response) {
    const text = await response.text();
    try {
        return { ok: response.ok, status: response.status, data: JSON.parse(text) };
    } catch {
        return { ok: response.ok, status: response.status, data: { message: text } };
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
    if (!value) return '';
    const d = new Date(value);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleString('vi-VN');
}

function stockTransferFormatNumber(value) {
    return Number(value ?? 0).toLocaleString('vi-VN', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 3
    });
}

function initStockTransferModalInstances() {
    const createModal = document.getElementById('createStockTransferModal');
    const editModal = document.getElementById('editStockTransferLineModal');
    const rejectModal = document.getElementById('rejectStockTransferModal');

    if (createModal) createStockTransferModalInstance = new bootstrap.Modal(createModal);
    if (editModal) editStockTransferLineModalInstance = new bootstrap.Modal(editModal);
    if (rejectModal) rejectStockTransferModalInstance = new bootstrap.Modal(rejectModal);
}

function stockTransferStatusBadge(status) {
    switch (status) {
        case 0: return `<span class="st-status-draft">Draft</span>`;
        case 1: return `<span class="st-status-pending">PendingApproval</span>`;
        case 2: return `<span class="st-status-rejected">Rejected</span>`;
        case 3: return `<span class="st-status-confirmed">Confirmed</span>`;
        default: return `<span class="badge bg-secondary">Unknown</span>`;
    }
}

function stockTransferStatusBadgeDetail(status) {
    switch (status) {
        case 0: return `<span class="std-status-draft">Draft</span>`;
        case 1: return `<span class="std-status-pending">PendingApproval</span>`;
        case 2: return `<span class="std-status-rejected">Rejected</span>`;
        case 3: return `<span class="std-status-confirmed">Confirmed</span>`;
        default: return `<span class="badge bg-secondary">Unknown</span>`;
    }
}

function focusStockTransferQuickLookup() {
    const el = $('#QuickTransferLookupInput');
    if (!el.length) return;
    if (!window.jQuery || !$.fn.select2) return;

    setTimeout(function () {
        el.select2('open');
    }, 120);
}

function stockTransferEditable(status) {
    return Number(status) === 0 || Number(status) === 1 || Number(status) === 2;
}

/* ========================= INDEX ========================= */

function bindStockTransferIndexEvents() {
    document.getElementById('btnReloadStockTransferList')?.addEventListener('click', loadStockTransferList);

    document.getElementById('btnOpenCreateStockTransferModal')?.addEventListener('click', async function () {
        await loadTransferWarehouseOptionsForCreate();
        resetCreateStockTransferForm();
        createStockTransferModalInstance?.show();
    });

    document.getElementById('btnCreateStockTransfer')?.addEventListener('click', createStockTransferDocument);
}

async function loadStockTransferList() {
    const response = await fetch('/admin/api/stock-transfers');
    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        setTransferIndexTableError('Không tải được danh sách phiếu chuyển kho.');
        return;
    }

    const items = api.data?.items || api.data || [];
    renderStockTransferIndexSummary(items);
    renderStockTransferIndexTabs(items);
}

function setTransferIndexTableError(message) {
    ['stockTransferWorkingBody', 'stockTransferPendingBody', 'stockTransferConfirmedBody']
        .forEach(id => {
            const body = document.getElementById(id);
            if (body) body.innerHTML = `<tr><td colspan="8" class="st-empty text-danger">${message}</td></tr>`;
        });
}

function renderStockTransferIndexSummary(items) {
    const total = items.length;
    const working = items.filter(x => x.status === 0 || x.status === 2).length;
    const pending = items.filter(x => x.status === 1).length;
    const confirmed = items.filter(x => x.status === 3).length;

    document.getElementById('sumTotalTransfers').textContent = total.toString();
    document.getElementById('sumWorkingTransfers').textContent = working.toString();
    document.getElementById('sumPendingTransfers').textContent = pending.toString();
    document.getElementById('sumConfirmedTransfers').textContent = confirmed.toString();
}

function renderStockTransferIndexTabs(items) {
    const working = items.filter(x => x.status === 0 || x.status === 2);
    const pending = items.filter(x => x.status === 1);
    const confirmed = items.filter(x => x.status === 3);

    renderTransferWorkingRows(working);
    renderTransferPendingRows(pending);
    renderTransferConfirmedRows(confirmed);
}

function renderTransferWorkingRows(items) {
    const body = document.getElementById('stockTransferWorkingBody');
    if (!body) return;

    if (!items.length) {
        body.innerHTML = `<tr><td colspan="8" class="st-empty">Không có dữ liệu</td></tr>`;
        return;
    }

    body.innerHTML = items.map(x => `
        <tr>
            <td>${stockTransferEscapeHtml(x.documentNo || '')}</td>
            <td>${stockTransferFormatDateTime(x.documentDate)}</td>
            <td>${stockTransferEscapeHtml(x.fromWarehouseName || '')}</td>
            <td>${stockTransferEscapeHtml(x.toWarehouseName || '')}</td>
            <td>${stockTransferStatusBadge(x.status)}</td>
            <td>${x.totalLines ?? 0}</td>
            <td>${stockTransferEscapeHtml(x.note || '')}</td>
            <td><a href="/admin/stock-transfers/${x.id}" class="btn btn-sm btn-primary">Mở</a></td>
        </tr>
    `).join('');
}

function renderTransferPendingRows(items) {
    const body = document.getElementById('stockTransferPendingBody');
    if (!body) return;

    if (!items.length) {
        body.innerHTML = `<tr><td colspan="7" class="st-empty">Không có dữ liệu</td></tr>`;
        return;
    }

    body.innerHTML = items.map(x => `
        <tr>
            <td>${stockTransferEscapeHtml(x.documentNo || '')}</td>
            <td>${stockTransferFormatDateTime(x.documentDate)}</td>
            <td>${stockTransferEscapeHtml(x.fromWarehouseName || '')}</td>
            <td>${stockTransferEscapeHtml(x.toWarehouseName || '')}</td>
            <td>${x.totalLines ?? 0}</td>
            <td>${stockTransferEscapeHtml(x.note || '')}</td>
            <td><a href="/admin/stock-transfers/${x.id}" class="btn btn-sm btn-warning">Xử lý</a></td>
        </tr>
    `).join('');
}

function renderTransferConfirmedRows(items) {
    const body = document.getElementById('stockTransferConfirmedBody');
    if (!body) return;

    if (!items.length) {
        body.innerHTML = `<tr><td colspan="7" class="st-empty">Không có dữ liệu</td></tr>`;
        return;
    }

    body.innerHTML = items.map(x => `
        <tr>
            <td>${stockTransferEscapeHtml(x.documentNo || '')}</td>
            <td>${stockTransferFormatDateTime(x.documentDate)}</td>
            <td>${stockTransferEscapeHtml(x.fromWarehouseName || '')}</td>
            <td>${stockTransferEscapeHtml(x.toWarehouseName || '')}</td>
            <td>${x.totalLines ?? 0}</td>
            <td>${stockTransferFormatDateTime(x.confirmedAtUtc)}</td>
            <td><a href="/admin/stock-transfers/${x.id}" class="btn btn-sm btn-outline-primary">Xem</a></td>
        </tr>
    `).join('');
}

async function loadTransferWarehouseOptionsForCreate() {
    const fromSelect = document.getElementById('createTransferFromWarehouseId');
    const toSelect = document.getElementById('createTransferToWarehouseId');
    if (!fromSelect || !toSelect) return;

    const response = await fetch('/admin/api/warehouses/select2?term=');
    const api = await readStockTransferApiResponse(response);

    if (!api.ok) {
        fromSelect.innerHTML = `<option value="">Không tải được kho</option>`;
        toSelect.innerHTML = `<option value="">Không tải được kho</option>`;
        return;
    }

    const items = api.data?.results || [];
    const html = `<option value="">-- Chọn --</option>` +
        items.map(x => `<option value="${x.id}">${stockTransferEscapeHtml(x.text)}</option>`).join('');

    fromSelect.innerHTML = html;
    toSelect.innerHTML = html;
}

function resetCreateStockTransferForm() {
    const fromWarehouse = document.getElementById('createTransferFromWarehouseId');
    const toWarehouse = document.getElementById('createTransferToWarehouseId');
    const dateInput = document.getElementById('createTransferDocumentDate');
    const note = document.getElementById('createTransferNote');
    const msg = document.getElementById('createTransferMessage');

    if (fromWarehouse) fromWarehouse.value = '';
    if (toWarehouse) toWarehouse.value = '';
    if (note) note.value = '';
    if (msg) msg.textContent = '';

    if (dateInput) {
        const now = new Date();
        const local = new Date(now.getTime() - now.getTimezoneOffset() * 60000)
            .toISOString()
            .slice(0, 16);
        dateInput.value = local;
    }
}

async function createStockTransferDocument() {
    const fromWarehouseId = document.getElementById('createTransferFromWarehouseId')?.value || '';
    const toWarehouseId = document.getElementById('createTransferToWarehouseId')?.value || '';
    const documentDate = document.getElementById('createTransferDocumentDate')?.value || '';
    const note = document.getElementById('createTransferNote')?.value || '';
    const msg = document.getElementById('createTransferMessage');

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

    const response = await fetch('/admin/api/stock-transfers', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            fromWarehouseId: Number(fromWarehouseId),
            toWarehouseId: Number(toWarehouseId),
            documentDate: documentDate ? new Date(documentDate).toISOString() : null,
            note: note
        })
    });

    const api = await readStockTransferApiResponse(response);
    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Tạo phiếu chuyển kho thất bại.';
        return;
    }

    createStockTransferModalInstance?.hide();
    window.location.href = `/admin/stock-transfers/${api.data.id}`;
}

/* ========================= DETAIL ========================= */

function bindStockTransferDetailEvents() {
    document.getElementById('btnSubmitStockTransfer')?.addEventListener('click', submitStockTransferDocument);
    document.getElementById('btnConfirmStockTransfer')?.addEventListener('click', confirmStockTransferDocument);
    document.getElementById('btnRejectStockTransfer')?.addEventListener('click', function () {
        document.getElementById('RejectTransferReason').value = '';
        document.getElementById('RejectTransferMessage').textContent = '';
        rejectStockTransferModalInstance?.show();
    });

    document.getElementById('btnConfirmRejectTransfer')?.addEventListener('click', rejectStockTransferDocument);
    document.getElementById('btnQuickAddTransferLine')?.addEventListener('click', quickAddStockTransferLine);
    document.getElementById('btnSaveEditTransferLine')?.addEventListener('click', saveEditStockTransferLine);

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
        alert(api.data?.message || 'Không tải được chi tiết phiếu chuyển kho.');
        return;
    }

    const detail = api.data;
    stockTransferUiState.currentDetail = detail;

    renderStockTransferDetailHeader(detail);
    renderStockTransferLines(detail.lines || []);
    applyStockTransferReadonlyState(detail.status);

    if (detail.status === 0 || detail.status === 1 || detail.status === 2) {
        focusStockTransferQuickLookup();
    }
}

function renderStockTransferDetailHeader(detail) {
    document.getElementById('TransferDocumentNo').value = detail.documentNo || '';
    document.getElementById('TransferDocumentDate').value = transferToLocalDateTime(detail.documentDate);
    document.getElementById('TransferFromWarehouseId').value = detail.fromWarehouseId || '';
    document.getElementById('TransferToWarehouseId').value = detail.toWarehouseId || '';
    document.getElementById('TransferDocumentNote').value = detail.note || '';
    document.getElementById('TransferStatusBadge').innerHTML = stockTransferStatusBadgeDetail(detail.status);

    document.getElementById('sumTransferTotalLines').textContent = String((detail.lines || []).length);
    document.getElementById('sumTransferDocumentNo').textContent = detail.documentNo || '---';
}

function transferToLocalDateTime(value) {
    if (!value) return '';
    const d = new Date(value);
    if (isNaN(d.getTime())) return '';
    return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
}

function renderStockTransferLines(lines) {
    const body = document.getElementById('stockTransferLineTableBody');
    if (!body) return;

    if (!lines.length) {
        body.innerHTML = `<tr><td colspan="9" class="text-center text-muted">Chưa có dòng chuyển kho.</td></tr>`;
        return;
    }

    const editable = stockTransferEditable(stockTransferUiState.currentDetail?.status);

    body.innerHTML = lines.map(line => `
        <tr>
            <td>${line.lineNo}</td>
            <td>
                <div>${stockTransferEscapeHtml(line.productNameSnapshot || '')}</div>
                <small class="text-muted">${stockTransferEscapeHtml(line.skuSnapshot || '')}</small>
            </td>
            <td>${stockTransferEscapeHtml(line.barcodeSnapshot || '')}</td>
            <td>${stockTransferEscapeHtml(line.unitNameSnapshot || '')}</td>
            <td class="text-end">${stockTransferFormatNumber(line.factor)}</td>
            <td class="text-end">${stockTransferFormatNumber(line.quantity)}</td>
            <td class="text-end">${stockTransferFormatNumber(line.baseQuantity)}</td>
            <td>${stockTransferEscapeHtml(line.note || '')}</td>
            <td>
                <button type="button"
                        class="btn btn-sm btn-warning btn-edit-stock-transfer-line"
                        data-line-id="${line.id}"
                        data-variant-id="${line.productVariantId}"
                        data-unit-id="${line.unitId}"
                        data-qty="${line.quantity}"
                        data-note="${stockTransferEscapeHtml(line.note || '')}"
                        ${editable ? '' : 'disabled'}>
                    Sửa
                </button>
                <button type="button"
                        class="btn btn-sm btn-danger btn-delete-stock-transfer-line"
                        data-line-id="${line.id}"
                        ${editable ? '' : 'disabled'}>
                    Xóa
                </button>
            </td>
        </tr>
    `).join('');

    bindEditStockTransferLineButtons();
    bindDeleteStockTransferLineButtons();
}

function applyStockTransferReadonlyState(status) {
    const isEditable = stockTransferEditable(status);
    const isPending = Number(status) === 1;
    const isDraftOrRejected = Number(status) === 0 || Number(status) === 2;

    document.getElementById('TransferFromWarehouseId').disabled = !isEditable;
    document.getElementById('TransferToWarehouseId').disabled = !isEditable;
    document.getElementById('TransferDocumentDate').disabled = !isEditable;
    document.getElementById('TransferDocumentNote').disabled = !isEditable;

    const quickLookup = $('#QuickTransferLookupInput');
    if (quickLookup.length) quickLookup.prop('disabled', !isEditable);

    document.getElementById('QuickTransferQty').disabled = !isEditable;
    document.getElementById('btnQuickAddTransferLine').disabled = !isEditable;

    document.getElementById('btnSubmitStockTransfer').disabled = !isDraftOrRejected;
    document.getElementById('btnConfirmStockTransfer').disabled = !isPending;
    document.getElementById('btnRejectStockTransfer').disabled = !isPending;
}

function queueStockTransferHeaderSave() {
    if (stockTransferHeaderSaveTimer) clearTimeout(stockTransferHeaderSaveTimer);
    stockTransferHeaderSaveTimer = setTimeout(saveStockTransferHeader, 500);
}

async function saveStockTransferHeader() {
    const stockTransferDocumentId = Number(document.getElementById('StockTransferDocumentId')?.value || 0);
    const fromWarehouseId = Number(document.getElementById('TransferFromWarehouseId')?.value || 0);
    const toWarehouseId = Number(document.getElementById('TransferToWarehouseId')?.value || 0);
    const documentDate = document.getElementById('TransferDocumentDate')?.value || '';
    const note = document.getElementById('TransferDocumentNote')?.value || '';

    if (!stockTransferDocumentId || !fromWarehouseId || !toWarehouseId) return;

    await fetch(`/admin/api/stock-transfers/${stockTransferDocumentId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            fromWarehouseId,
            toWarehouseId,
            documentDate: documentDate ? new Date(documentDate).toISOString() : null,
            note
        })
    });
}

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
        escapeMarkup: function (markup) { return markup; }
    });

    el.on('select2:select', function (e) {
        const item = e.params.data;

        document.getElementById('SelectedTransferProductVariantId').value = item.productVariantId || '';
        document.getElementById('SelectedTransferUnitId').value = item.unitId || '';
        document.getElementById('SelectedTransferFactor').value = item.factor || 1;

        el.data('selected-item', item);

        setTimeout(() => document.getElementById('QuickTransferQty')?.focus(), 80);
    });

    el.on('select2:clear', function () {
        el.removeData('selected-item');
    });

    bindStockTransferLookupEnterAutoSelect();
}

function formatStockTransferLookupResult(item) {
    if (!item.id) return item.text || '';

    const title = stockTransferEscapeHtml(item.productName || '');
    const meta = [
        item.sku ? 'SKU: ' + stockTransferEscapeHtml(item.sku) : '',
        item.unitName ? 'Đơn vị: ' + stockTransferEscapeHtml(item.unitName) : '',
        item.factor ? 'x' + item.factor : '',
        item.barcode ? 'BC: ' + stockTransferEscapeHtml(item.barcode) : '',
        item.sourceType ? stockTransferEscapeHtml(item.sourceType) : ''
    ].filter(Boolean).join(' | ');

    return `<div><div class="select2-result-title">${title}</div><div class="select2-result-meta">${meta}</div></div>`;
}

function formatStockTransferLookupSelection(item) {
    if (!item || !item.id) return item.text || 'Chọn sản phẩm';

    const parts = [];
    if (item.productName) parts.push(item.productName);
    if (item.unitName) parts.push(item.unitName);
    if (item.factor) parts.push('x' + item.factor);
    if (item.barcode) parts.push(item.barcode);

    return parts.join(' | ');
}

function bindStockTransferLookupEnterAutoSelect() {
    $(document).on('keydown', '.select2-container--open .select2-search__field', async function (e) {
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
        alert('Vui lòng chọn sản phẩm.');
        focusStockTransferQuickLookup();
        return;
    }

    if (quantity <= 0) {
        alert('Số lượng không hợp lệ.');
        return;
    }

    let note = null;

    if (selectedItem && selectedItem.barcode &&
        (selectedItem.sourceType === 'UnitBarcode'
            || selectedItem.sourceType === 'VariantBarcode'
            || selectedItem.sourceType === 'BarcodeHistory')) {
        note = `Quét mã: ${selectedItem.barcode}`;
    }

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
    if (!api.ok) {
        alert(api.data?.message || 'Thêm dòng chuyển kho thất bại.');
        focusStockTransferQuickLookup();
        return;
    }

    $('#QuickTransferLookupInput').val(null).trigger('change');
    $('#QuickTransferLookupInput').removeData('selected-item');

    document.getElementById('SelectedTransferProductVariantId').value = '';
    document.getElementById('SelectedTransferUnitId').value = '';
    document.getElementById('SelectedTransferFactor').value = '';
    document.getElementById('QuickTransferQty').value = '1';

    await loadStockTransferDetail();
    focusStockTransferQuickLookup();
}

function bindEditStockTransferLineButtons() {
    document.querySelectorAll('.btn-edit-stock-transfer-line').forEach(btn => {
        btn.onclick = async function () {
            document.getElementById('EditTransferLineId').value = this.dataset.lineId || '';
            document.getElementById('EditTransferVariantId').value = this.dataset.variantId || '';
            document.getElementById('EditTransferQty').value = this.dataset.qty || '';
            document.getElementById('EditTransferLineNote').value = this.dataset.note || '';
            document.getElementById('EditTransferLineMessage').textContent = '';

            await loadStockTransferLineUnits(this.dataset.variantId || '', this.dataset.unitId || '');
            editStockTransferLineModalInstance?.show();
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
        return `<option value="${x.unitId}" data-factor="${x.factor}" ${selected}>${stockTransferEscapeHtml(x.unitName)} (x${x.factor})</option>`;
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
    info.textContent = `Hệ số quy đổi về đơn vị gốc: x${factor}`;
}

async function saveEditStockTransferLine() {
    const lineId = Number(document.getElementById('EditTransferLineId')?.value || 0);
    const unitId = Number(document.getElementById('EditTransferLineUnitId')?.value || 0);
    const quantity = Number(document.getElementById('EditTransferQty')?.value || 0);
    const note = document.getElementById('EditTransferLineNote')?.value || '';
    const msg = document.getElementById('EditTransferLineMessage');

    if (!lineId || !unitId) {
        if (msg) msg.textContent = 'Thông tin dòng không hợp lệ.';
        return;
    }

    if (quantity <= 0) {
        if (msg) msg.textContent = 'Số lượng không hợp lệ.';
        return;
    }

    const response = await fetch(`/admin/api/stock-transfers/lines/${lineId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ unitId, quantity, note })
    });

    const api = await readStockTransferApiResponse(response);
    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Cập nhật dòng thất bại.';
        return;
    }

    editStockTransferLineModalInstance?.hide();
    await loadStockTransferDetail();
}

function bindDeleteStockTransferLineButtons() {
    document.querySelectorAll('.btn-delete-stock-transfer-line').forEach(btn => {
        btn.onclick = async function () {
            const lineId = this.dataset.lineId;
            if (!confirm('Bạn có chắc muốn xóa dòng chuyển kho này không?')) return;

            const response = await fetch(`/admin/api/stock-transfers/lines/${lineId}`, {
                method: 'DELETE'
            });

            const api = await readStockTransferApiResponse(response);
            if (!api.ok) {
                alert(api.data?.message || 'Xóa dòng thất bại.');
                return;
            }

            await loadStockTransferDetail();
        };
    });
}

async function submitStockTransferDocument() {
    const stockTransferDocumentId = Number(document.getElementById('StockTransferDocumentId')?.value || 0);
    if (!stockTransferDocumentId) return;

    if (!confirm('Gửi phiếu chuyển kho sang trạng thái chờ duyệt?')) return;

    const response = await fetch(`/admin/api/stock-transfers/${stockTransferDocumentId}/submit`, {
        method: 'POST'
    });

    const api = await readStockTransferApiResponse(response);
    if (!api.ok) {
        alert(api.data?.message || 'Submit phiếu chuyển kho thất bại.');
        return;
    }

    alert(api.data?.message || 'Đã submit phiếu chuyển kho.');
    await loadStockTransferDetail();
}

async function confirmStockTransferDocument() {
    const stockTransferDocumentId = Number(document.getElementById('StockTransferDocumentId')?.value || 0);
    if (!stockTransferDocumentId) return;

    if (!confirm('Bạn có chắc muốn confirm phiếu chuyển kho? Sau khi confirm sẽ sinh movement kho và khóa phiếu.')) return;

    const response = await fetch(`/admin/api/stock-transfers/${stockTransferDocumentId}/confirm`, {
        method: 'POST'
    });

    const api = await readStockTransferApiResponse(response);
    if (!api.ok) {
        alert(api.data?.message || 'Confirm phiếu chuyển kho thất bại.');
        return;
    }

    alert(api.data?.message || 'Confirm phiếu chuyển kho thành công.');
    await loadStockTransferDetail();
}

async function rejectStockTransferDocument() {
    const stockTransferDocumentId = Number(document.getElementById('StockTransferDocumentId')?.value || 0);
    const reason = document.getElementById('RejectTransferReason')?.value || '';
    const msg = document.getElementById('RejectTransferMessage');

    if (!stockTransferDocumentId) return;

    const response = await fetch(`/admin/api/stock-transfers/${stockTransferDocumentId}/reject`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason })
    });

    const api = await readStockTransferApiResponse(response);
    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Từ chối phiếu chuyển kho thất bại.';
        return;
    }

    rejectStockTransferModalInstance?.hide();
    alert(api.data?.message || 'Đã từ chối phiếu chuyển kho.');
    await loadStockTransferDetail();
}