let createStockCountModalInstance = null;
let editStockCountLineModalInstance = null;
let stockCountHeaderSaveTimer = null;
const stockCountUiState = {
    currentDetail: null
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

/* ========================= COMMON ========================= */

async function readStockCountApiResponse(response) {
    const text = await response.text();
    try {
        return { ok: response.ok, status: response.status, data: JSON.parse(text) };
    } catch {
        return { ok: response.ok, status: response.status, data: { message: text } };
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

function initStockCountModalInstances() {
    const createModal = document.getElementById('createStockCountModal');
    const editModal = document.getElementById('editStockCountLineModal');

    if (createModal) createStockCountModalInstance = new bootstrap.Modal(createModal);
    if (editModal) editStockCountLineModalInstance = new bootstrap.Modal(editModal);
}

function stockCountStatusBadge(status) {
    switch (status) {
        case 1: return `<span class="sc-status-draft">Draft</span>`;
        case 2: return `<span class="sc-status-pending">PendingApproval</span>`;
        case 3: return `<span class="sc-status-confirmed">Confirmed</span>`;
        case 4: return `<span class="sc-status-rejected">Rejected</span>`;
        case 5: return `<span class="sc-status-cancelled">Cancelled</span>`;
        default: return `<span class="badge bg-secondary">Unknown</span>`;
    }
}

function stockCountStatusBadgeDetail(status) {
    switch (status) {
        case 1: return `<span class="scd-status-draft">Draft</span>`;
        case 2: return `<span class="scd-status-pending">PendingApproval</span>`;
        case 3: return `<span class="scd-status-confirmed">Confirmed</span>`;
        case 4: return `<span class="scd-status-rejected">Rejected</span>`;
        case 5: return `<span class="scd-status-cancelled">Cancelled</span>`;
        default: return `<span class="badge bg-secondary">Unknown</span>`;
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

function focusStockCountQuickLookup() {
    const el = $('#QuickLookupInput');
    if (!el.length) return;
    if (!window.jQuery || !$.fn.select2) return;

    setTimeout(function () {
        el.select2('open');
    }, 120);
}

/* ========================= INDEX ========================= */

function bindStockCountIndexEvents() {
    document.getElementById('btnReloadStockCountList')?.addEventListener('click', loadStockCountList);

    document.getElementById('btnOpenCreateStockCountModal')?.addEventListener('click', async function () {
        await loadStockCountWarehouseOptionsForCreate();
        resetCreateStockCountForm();
        createStockCountModalInstance?.show();
    });

    document.getElementById('btnCreateStockCount')?.addEventListener('click', createStockCountDocument);
}

async function loadStockCountList() {
    const response = await fetch('/admin/api/stock-counts');
    const api = await readStockCountApiResponse(response);

    if (!api.ok) {
        setIndexTableError('Không tải được danh sách phiếu kiểm kê.');
        return;
    }

    const items = api.data || [];
    renderStockCountIndexSummary(items);
    renderStockCountIndexTabs(items);
}

function setIndexTableError(message) {
    ['stockCountWorkingBody', 'stockCountPendingBody', 'stockCountConfirmedBody', 'stockCountCancelledBody']
        .forEach(id => {
            const body = document.getElementById(id);
            if (body) body.innerHTML = `<tr><td colspan="8" class="sc-empty text-danger">${message}</td></tr>`;
        });
}

function renderStockCountIndexSummary(items) {
    const total = items.length;
    const working = items.filter(x => x.status === 1 || x.status === 4).length;
    const pending = items.filter(x => x.status === 2).length;
    const confirmed = items.filter(x => x.status === 3).length;

    document.getElementById('sumTotalDocuments').textContent = total.toString();
    document.getElementById('sumWorkingDocuments').textContent = working.toString();
    document.getElementById('sumPendingDocuments').textContent = pending.toString();
    document.getElementById('sumConfirmedDocuments').textContent = confirmed.toString();
}

function renderStockCountIndexTabs(items) {
    const working = items.filter(x => x.status === 1 || x.status === 4);
    const pending = items.filter(x => x.status === 2);
    const confirmed = items.filter(x => x.status === 3);
    const cancelled = items.filter(x => x.status === 5);

    renderWorkingRows(working);
    renderPendingRows(pending);
    renderConfirmedRows(confirmed);
    renderCancelledRows(cancelled);
}

function renderWorkingRows(items) {
    const body = document.getElementById('stockCountWorkingBody');
    if (!body) return;

    if (!items.length) {
        body.innerHTML = `<tr><td colspan="7" class="sc-empty">Không có dữ liệu</td></tr>`;
        return;
    }

    body.innerHTML = items.map(x => `
        <tr>
            <td>${stockCountEscapeHtml(x.documentNo || '')}</td>
            <td>${stockCountFormatDateTime(x.documentDate)}</td>
            <td>${stockCountEscapeHtml(x.warehouseName || '')}</td>
            <td>${stockCountStatusBadge(x.status)}</td>
            <td>${x.totalLines ?? 0}</td>
            <td>${stockCountEscapeHtml(x.note || '')}</td>
            <td><a href="/admin/stock-counts/${x.id}" class="btn btn-sm btn-primary">Mở</a></td>
        </tr>
    `).join('');
}

function renderPendingRows(items) {
    const body = document.getElementById('stockCountPendingBody');
    if (!body) return;

    if (!items.length) {
        body.innerHTML = `<tr><td colspan="6" class="sc-empty">Không có dữ liệu</td></tr>`;
        return;
    }

    body.innerHTML = items.map(x => `
        <tr>
            <td>${stockCountEscapeHtml(x.documentNo || '')}</td>
            <td>${stockCountFormatDateTime(x.documentDate)}</td>
            <td>${stockCountEscapeHtml(x.warehouseName || '')}</td>
            <td>${x.totalLines ?? 0}</td>
            <td>${stockCountEscapeHtml(x.note || '')}</td>
            <td><a href="/admin/stock-counts/${x.id}" class="btn btn-sm btn-warning">Duyệt</a></td>
        </tr>
    `).join('');
}

function renderConfirmedRows(items) {
    const body = document.getElementById('stockCountConfirmedBody');
    if (!body) return;

    if (!items.length) {
        body.innerHTML = `<tr><td colspan="6" class="sc-empty">Không có dữ liệu</td></tr>`;
        return;
    }

    body.innerHTML = items.map(x => `
        <tr>
            <td>${stockCountEscapeHtml(x.documentNo || '')}</td>
            <td>${stockCountFormatDateTime(x.documentDate)}</td>
            <td>${stockCountEscapeHtml(x.warehouseName || '')}</td>
            <td>${x.totalLines ?? 0}</td>
            <td>${stockCountFormatDateTime(x.confirmedAtUtc)}</td>
            <td><a href="/admin/stock-counts/${x.id}" class="btn btn-sm btn-outline-primary">Xem</a></td>
        </tr>
    `).join('');
}

function renderCancelledRows(items) {
    const body = document.getElementById('stockCountCancelledBody');
    if (!body) return;

    if (!items.length) {
        body.innerHTML = `<tr><td colspan="4" class="sc-empty">Không có dữ liệu</td></tr>`;
        return;
    }

    body.innerHTML = items.map(x => `
        <tr>
            <td>${stockCountEscapeHtml(x.documentNo || '')}</td>
            <td>${stockCountFormatDateTime(x.documentDate)}</td>
            <td>${stockCountEscapeHtml(x.warehouseName || '')}</td>
            <td>${stockCountEscapeHtml(x.note || '')}</td>
        </tr>
    `).join('');
}

async function loadStockCountWarehouseOptionsForCreate() {
    const select = document.getElementById('createStockCountWarehouseId');
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

function resetCreateStockCountForm() {
    const warehouse = document.getElementById('createStockCountWarehouseId');
    const dateInput = document.getElementById('createStockCountDocumentDate');
    const note = document.getElementById('createStockCountNote');
    const msg = document.getElementById('createStockCountMessage');

    if (warehouse) warehouse.value = '';
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

async function createStockCountDocument() {
    const warehouseId = document.getElementById('createStockCountWarehouseId')?.value || '';
    const documentDate = document.getElementById('createStockCountDocumentDate')?.value || '';
    const note = document.getElementById('createStockCountNote')?.value || '';
    const msg = document.getElementById('createStockCountMessage');

    if (!warehouseId) {
        if (msg) msg.textContent = 'Vui lòng chọn kho.';
        return;
    }

    const response = await fetch('/admin/api/stock-counts', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            warehouseId: Number(warehouseId),
            documentDate: documentDate ? new Date(documentDate).toISOString() : null,
            note: note
        })
    });

    const api = await readStockCountApiResponse(response);
    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Tạo phiếu kiểm kê thất bại.';
        return;
    }

    createStockCountModalInstance?.hide();
    window.location.href = `/admin/stock-counts/${api.data.id}`;
}

/* ========================= DETAIL ========================= */

function bindStockCountDetailEvents() {
    document.getElementById('btnSubmitStockCountApproval')?.addEventListener('click', submitStockCountApproval);
    document.getElementById('btnConfirmStockCount')?.addEventListener('click', confirmStockCountDocument);
    document.getElementById('btnRejectStockCount')?.addEventListener('click', rejectStockCountDocument);
    document.getElementById('btnQuickAddStockCountLine')?.addEventListener('click', quickAddStockCountLine);
    document.getElementById('btnSaveEditStockCountLine')?.addEventListener('click', saveEditStockCountLine);

    document.getElementById('WarehouseId')?.addEventListener('change', queueStockCountHeaderSave);
    document.getElementById('DocumentDate')?.addEventListener('change', queueStockCountHeaderSave);
    document.getElementById('DocumentNote')?.addEventListener('input', queueStockCountHeaderSave);

    document.getElementById('QuickCountedQty')?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            quickAddStockCountLine();
        }
    });

    
    document.getElementById('btnRefreshSystemQty')?.addEventListener('click', refreshStockCountSystemQty);
    document.getElementById('ToggleOnlyDifferentGrouped')?.addEventListener('change', function () {
        rerenderStockCountDetailTables();
    });

    document.getElementById('ToggleOnlyDifferentLines')?.addEventListener('change', function () {
        rerenderStockCountDetailTables();
    });
}
async function refreshStockCountSystemQty() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    if (!stockCountDocumentId) return;

    if (!confirm('Làm mới tồn hệ thống cho toàn bộ dòng kiểm kê?')) return;

    const response = await fetch(`/admin/api/stock-counts/${stockCountDocumentId}/refresh-system-qty`, {
        method: 'POST'
    });


    const api = await readStockCountApiResponse(response);
    if (!api.ok) {
        alert(api.data?.message || 'Làm mới tồn hệ thống thất bại.');
        return;
    }

    alert(api.data?.message || 'Đã làm mới tồn hệ thống.');
    await loadStockCountDetail();
}

function stockCountDiffConclusion(value) {
    const n = Number(value || 0);
    if (n > 0) return '<span class="badge bg-label-success">Tăng tồn</span>';
    if (n < 0) return '<span class="badge bg-label-danger">Giảm tồn</span>';
    return '<span class="badge bg-label-secondary">Khớp</span>';
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
        alert(api.data?.message || 'Không tải được chi tiết phiếu kiểm kê.');
        return;
    }

    const detail = api.data;
    stockCountUiState.currentDetail = detail;

    renderStockCountDetailHeader(detail);
    rerenderStockCountDetailTables();
    renderStockCountDetailSummary(detail.lines || []);
    applyStockCountReadonlyState(detail.status);

    if (detail.status === 1 || detail.status === 4) {
        focusStockCountQuickLookup();
    }
}


function renderStockCountDetailHeader(detail) {
    document.getElementById('DocumentNo').value = detail.documentNo || '';
    document.getElementById('WarehouseId').value = detail.warehouseId || '';
    document.getElementById('DocumentNote').value = detail.note || '';
    document.getElementById('DocumentStatusBadge').innerHTML = stockCountStatusBadgeDetail(detail.status);

    const documentDate = document.getElementById('DocumentDate');
    if (documentDate && detail.documentDate) {
        const d = new Date(detail.documentDate);
        if (!isNaN(d.getTime())) {
            const local = new Date(d.getTime() - d.getTimezoneOffset() * 60000)
                .toISOString()
                .slice(0, 16);
            documentDate.value = local;
        }
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
function renderStockCountDetailSummary(lines) {
    const totalLines = lines.length;

    const grouped = new Map();

    lines.forEach(x => {
        const key = String(x.productVariantId);

        if (!grouped.has(key)) {
            grouped.set(key, {
                systemQtyBase: Number(x.systemQtyBase || 0),
                countedQtyBaseSum: 0
            });
        }

        const item = grouped.get(key);
        item.countedQtyBaseSum += Number(x.countedQtyBase || 0);
    });

    let positiveLines = 0;
    let negativeLines = 0;
    let sumDifference = 0;

    Array.from(grouped.values()).forEach(g => {
        const diff = g.countedQtyBaseSum - g.systemQtyBase;
        sumDifference += diff;

        if (diff > 0) positiveLines++;
        else if (diff < 0) negativeLines++;
    });

    document.getElementById('sumTotalLines').textContent = totalLines.toString();
    document.getElementById('sumPositiveLines').textContent = positiveLines.toString();
    document.getElementById('sumNegativeLines').textContent = negativeLines.toString();
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
        body.innerHTML = `<tr><td colspan="12" class="text-center text-muted">Không có dòng lệch.</td></tr>`;
        return;
    }

    const lastUpdatedLineId = Number(sessionStorage.getItem('stockCountLastUpdatedLineId') || 0);

    body.innerHTML = renderLines.map(line => {
        const rowClass = `${stockCountDiffRowClass(line.differenceQtyBase)} ${lastUpdatedLineId === Number(line.id) ? 'scd-last-updated' : ''}`.trim();

        return `
            <tr class="${rowClass}" data-line-id="${line.id}">
                <td>${line.lineNo}</td>
                <td>
                    <div>${stockCountEscapeHtml(line.productNameSnapshot || '')}</div>
                    <small class="text-muted">${stockCountEscapeHtml(line.skuSnapshot || '')}</small>
                </td>
                <td>${stockCountEscapeHtml(line.barcodeSnapshot || '')}</td>
                <td>${stockCountEscapeHtml(line.unitName || '')}</td>
                <td class="text-end">${stockCountFormatNumber(line.factor)}</td>
                <td class="text-end">${stockCountFormatNumber(line.systemQtyBase)}</td>
                <td class="text-end">${stockCountFormatNumber(line.countedQty)}</td>
               <td class="text-end">${stockCountFormatNumber(line.countedQtyBase)}</td>
<td class="text-end ${stockCountDiffClass(line.differenceQtyBase)}">
    ${Number(line.differenceQtyBase) > 0 ? '+' : ''}${stockCountFormatNumber(line.differenceQtyBase)}
</td>
<td>${stockCountDiffConclusion(line.differenceQtyBase)}</td>
                <td>${stockCountEscapeHtml(line.note || '')}</td>
                <td>
                    <button type="button"
                            class="btn btn-sm btn-warning btn-edit-stock-count-line"
                            data-line-id="${line.id}"
                            data-variant-id="${line.productVariantId}"
                            data-unit-id="${line.unitId}"
                            data-system-qty="${line.systemQtyBase}"
                            data-counted-qty="${line.countedQty}"
                            data-note="${stockCountEscapeHtml(line.note || '')}">
                        Sửa
                    </button>
                    <button type="button"
                            class="btn btn-sm btn-danger btn-delete-stock-count-line"
                            data-line-id="${line.id}">
                        Xóa
                    </button>
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

function applyStockCountReadonlyState(status) {
    const isEditable = Number(status) === 1 || Number(status) === 4;
    const isPending = Number(status) === 2;

    document.getElementById('WarehouseId').disabled = !isEditable;
    document.getElementById('DocumentDate').disabled = !isEditable;
    document.getElementById('DocumentNote').disabled = !isEditable;

    const quickLookup = $('#QuickLookupInput');
    if (quickLookup.length) quickLookup.prop('disabled', !isEditable);

    document.getElementById('QuickCountedQty').disabled = !isEditable;
    document.getElementById('btnQuickAddStockCountLine').disabled = !isEditable;

    document.getElementById('btnSubmitStockCountApproval').disabled = !isEditable;
    document.getElementById('btnConfirmStockCount').disabled = !isPending;
    document.getElementById('btnRejectStockCount').disabled = !isPending;

    document.querySelectorAll('.btn-edit-stock-count-line').forEach(btn => btn.disabled = !isEditable);
    document.querySelectorAll('.btn-delete-stock-count-line').forEach(btn => btn.disabled = !isEditable);
    document.getElementById('btnRefreshSystemQty').disabled = !isEditable;
}

function queueStockCountHeaderSave() {
    if (stockCountHeaderSaveTimer) clearTimeout(stockCountHeaderSaveTimer);
    stockCountHeaderSaveTimer = setTimeout(saveStockCountHeader, 500);
}

async function saveStockCountHeader() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    const warehouseId = Number(document.getElementById('WarehouseId')?.value || 0);
    const documentDate = document.getElementById('DocumentDate')?.value || '';
    const note = document.getElementById('DocumentNote')?.value || '';

    if (!stockCountDocumentId || !warehouseId) return;

    await fetch('/admin/api/stock-counts/update-header', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            stockCountDocumentId,
            warehouseId,
            documentDate: documentDate ? new Date(documentDate).toISOString() : null,
            note
        })
    });
}

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
        escapeMarkup: function (markup) { return markup; }
    });

    el.on('select2:select', function (e) {
        const item = e.params.data;

        document.getElementById('SelectedProductVariantId').value = item.productVariantId || '';
        document.getElementById('SelectedUnitId').value = item.unitId || '';
        document.getElementById('SelectedFactor').value = item.factor || 1;

        el.data('selected-item', item);

        setTimeout(() => document.getElementById('QuickCountedQty')?.focus(), 80);
    });
    el.on('select2:clear', function () {
        el.removeData('selected-item');
    });

    bindStockCountLookupEnterAutoSelect();
}

function formatStockCountLookupResult(item) {
    if (!item.id) return item.text || '';

    const title = stockCountEscapeHtml(item.productName || '');
    const meta = [
        item.sku ? 'SKU: ' + stockCountEscapeHtml(item.sku) : '',
        item.unitName ? 'Đơn vị: ' + stockCountEscapeHtml(item.unitName) : '',
        item.factor ? 'x' + item.factor : '',
        item.barcode ? 'BC: ' + stockCountEscapeHtml(item.barcode) : '',
        item.sourceType ? stockCountEscapeHtml(item.sourceType) : ''
    ].filter(Boolean).join(' | ');

    return `<div><div class="select2-result-title">${title}</div><div class="select2-result-meta">${meta}</div></div>`;
}

function formatStockCountLookupSelection(item) {
    if (!item || !item.id) return item.text || 'Chọn sản phẩm';

    const parts = [];
    if (item.productName) parts.push(item.productName);
    if (item.unitName) parts.push(item.unitName);
    if (item.factor) parts.push('x' + item.factor);
    if (item.barcode) parts.push(item.barcode);

    return parts.join(' | ');
}

function bindStockCountLookupEnterAutoSelect() {
    $(document).on('keydown', '.select2-container--open .select2-search__field', async function (e) {
        if (e.key !== 'Enter') return;

        const term = $(this).val();
        if (!term) return;

        const response = await fetch(`/admin/inventory-adjustments/product-lookup-select2?term=${encodeURIComponent(term)}`);
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

async function quickAddStockCountLine() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    const productVariantId = Number(document.getElementById('SelectedProductVariantId')?.value || 0);
    const unitId = Number(document.getElementById('SelectedUnitId')?.value || 0);
    const countedQty = Number(document.getElementById('QuickCountedQty')?.value || 0);

    const selectedItem = $('#QuickLookupInput').data('selected-item');

    if (!productVariantId || !unitId) {
        alert('Vui lòng chọn sản phẩm.');
        focusStockCountQuickLookup();
        return;
    }

    if (countedQty < 0) {
        alert('Số lượng đếm không hợp lệ.');
        return;
    }

    let note = null;

    // Nếu nguồn là barcode thì note chuẩn hóa là "Quét mã: ..."
    if (selectedItem && selectedItem.barcode &&
        (selectedItem.sourceType === 'UnitBarcode'
            || selectedItem.sourceType === 'VariantBarcode'
            || selectedItem.sourceType === 'BarcodeHistory')) {
        note = `Quét mã: ${selectedItem.barcode}`;
    }

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
    if (!api.ok) {
        alert(api.data?.message || 'Thêm dòng kiểm kê thất bại.');
        focusStockCountQuickLookup();
        return;
    }

    // Lưu line vừa update để highlight đỏ sau reload
    if (api.data?.id) {
        sessionStorage.setItem('stockCountLastUpdatedLineId', api.data.id);
    }

    $('#QuickLookupInput').val(null).trigger('change');
    $('#QuickLookupInput').removeData('selected-item');

    document.getElementById('SelectedProductVariantId').value = '';
    document.getElementById('SelectedUnitId').value = '';
    document.getElementById('SelectedFactor').value = '';
    document.getElementById('QuickCountedQty').value = '1';

    await loadStockCountDetail();
    focusStockCountQuickLookup();
}

function renderStockCountGroupedSummary(lines, onlyDifferent = false) {
    const body = document.getElementById('stockCountGroupedBody');
    if (!body) return;

    if (!lines || !lines.length) {
        body.innerHTML = `<tr><td colspan="6" class="text-center text-muted">Chưa có dữ liệu tổng hợp.</td></tr>`;
        return;
    }

    const groups = new Map();

    lines.forEach(line => {
        const key = String(line.productVariantId);

        if (!groups.has(key)) {
            groups.set(key, {
                productVariantId: line.productVariantId,
                productNameSnapshot: line.productNameSnapshot,
                skuSnapshot: line.skuSnapshot,
                barcodeSnapshot: line.barcodeSnapshot,
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

    let groupedItems = Array.from(groups.values()).map(item => {
        const difference = item.countedQtyBaseSum - item.systemQtyBase;

        return {
            ...item,
            difference
        };
    });

    if (onlyDifferent) {
        groupedItems = groupedItems.filter(x => Number(x.difference || 0) !== 0);
    }

    if (!groupedItems.length) {
        body.innerHTML = `<tr><td colspan="6" class="text-center text-muted">Không có sản phẩm lệch.</td></tr>`;
        return;
    }

    body.innerHTML = groupedItems.map(item => `
        <tr>
            <td>
                <div>${stockCountEscapeHtml(item.productNameSnapshot || '')}</div>
                <small class="text-muted d-block">${stockCountEscapeHtml(item.barcodeSnapshot || '')}</small>
                <small class="text-muted d-block">${stockCountEscapeHtml(item.skuSnapshot || '')}</small>
            </td>
            <td class="text-end">${stockCountFormatNumber(item.systemQtyBase)}</td>
            <td class="text-end">${stockCountFormatNumber(item.countedQtyBaseSum)}</td>
            <td class="text-end ${stockCountDiffClass(item.difference)}">
                ${item.difference > 0 ? '+' : ''}${stockCountFormatNumber(item.difference)}
            </td>
            <td>${stockCountDiffConclusion(item.difference)}</td>
            <td>${stockCountEscapeHtml(item.units.join(' | '))}</td>
        </tr>
    `).join('');
}

function bindEditStockCountLineButtons() {
    document.querySelectorAll('.btn-edit-stock-count-line').forEach(btn => {
        btn.onclick = async function () {
            document.getElementById('EditLineId').value = this.dataset.lineId || '';
            document.getElementById('EditVariantId').value = this.dataset.variantId || '';
            document.getElementById('EditSystemQtyBase').value = this.dataset.systemQty || '';
            document.getElementById('EditCountedQty').value = this.dataset.countedQty || '';
            document.getElementById('EditLineNote').value = this.dataset.note || '';
            document.getElementById('EditLineMessage').textContent = '';

            await loadStockCountLineUnits(this.dataset.variantId || '', this.dataset.unitId || '');
            editStockCountLineModalInstance?.show();
        };
    });
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
        return `<option value="${x.unitId}" data-factor="${x.factor}" ${selected}>${stockCountEscapeHtml(x.unitName)} (x${x.factor})</option>`;
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
        body: JSON.stringify({ unitId, countedQty, note })
    });

    const api = await readStockCountApiResponse(response);
    if (!api.ok) {
        if (msg) msg.textContent = api.data?.message || 'Cập nhật dòng thất bại.';
        return;
    }

    editStockCountLineModalInstance?.hide();
    await loadStockCountDetail();
}

function bindDeleteStockCountLineButtons() {
    document.querySelectorAll('.btn-delete-stock-count-line').forEach(btn => {
        btn.onclick = async function () {
            const lineId = this.dataset.lineId;
            if (!confirm('Bạn có chắc muốn xóa dòng kiểm kê này không?')) return;

            const response = await fetch(`/admin/api/stock-counts/lines/${lineId}`, {
                method: 'DELETE'
            });

            const api = await readStockCountApiResponse(response);
            if (!api.ok) {
                alert(api.data?.message || 'Xóa dòng thất bại.');
                return;
            }

            await loadStockCountDetail();
        };
    });
}

async function submitStockCountApproval() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    if (!stockCountDocumentId) return;

    if (!confirm('Gửi phiếu kiểm kê chờ duyệt?')) return;

    const response = await fetch(`/admin/api/stock-counts/${stockCountDocumentId}/submit-approval`, {
        method: 'POST'
    });

    const api = await readStockCountApiResponse(response);
    if (!api.ok) {
        alert(api.data?.message || 'Gửi duyệt thất bại.');
        return;
    }

    alert(api.data?.message || 'Đã gửi duyệt.');
    await loadStockCountDetail();
}

async function confirmStockCountDocument() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    if (!stockCountDocumentId) return;

    if (!confirm('Bạn có chắc muốn duyệt xác nhận phiếu kiểm kê? Sau khi xác nhận sẽ sinh movement kho và khóa phiếu.')) return;

    const response = await fetch(`/admin/api/stock-counts/${stockCountDocumentId}/confirm`, {
        method: 'POST'
    });

    const api = await readStockCountApiResponse(response);
    if (!api.ok) {
        alert(api.data?.message || 'Xác nhận phiếu kiểm kê thất bại.');
        return;
    }

    alert(api.data?.message || 'Xác nhận phiếu kiểm kê thành công.');
    await loadStockCountDetail();
}

async function rejectStockCountDocument() {
    const stockCountDocumentId = Number(document.getElementById('StockCountDocumentId')?.value || 0);
    if (!stockCountDocumentId) return;

    if (!confirm('Bạn có chắc muốn từ chối phiếu kiểm kê?')) return;

    const response = await fetch(`/admin/api/stock-counts/${stockCountDocumentId}/reject`, {
        method: 'POST'
    });

    const api = await readStockCountApiResponse(response);
    if (!api.ok) {
        alert(api.data?.message || 'Từ chối phiếu kiểm kê thất bại.');
        return;
    }

    alert(api.data?.message || 'Đã từ chối phiếu kiểm kê.');
    await loadStockCountDetail();
}