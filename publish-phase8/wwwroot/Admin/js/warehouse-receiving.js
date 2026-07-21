let wrCreateModalInstance = null;
let wrAllReceipts = [];
let wrPage = 1;
let wrPageSize = 10;
let wrFilteredReceipts = [];
document.addEventListener('DOMContentLoaded', function () {
    const modalEl = document.getElementById('wrCreateModal');
    if (modalEl) {
        wrCreateModalInstance = new bootstrap.Modal(modalEl);
    }

    bindWarehouseReceivingEvents();
    loadWarehouseReceivingReceipts();
});

function bindWarehouseReceivingEvents() {
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
        if (!keyword) return true;

        return [
            x.documentNo,
            x.documentTitle,
            statusText(x.status)
        ].some(v => String(v || '').toLowerCase().includes(keyword));
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
        const isReadonly = status === 2;
        const actionText = isReadonly ? 'Xem' : 'Nhập';

        return `
            <div class="wr-card wr-status-${status}">
                <div class="wr-card-icon">
                    <i class="bx bx-file"></i>
                </div>

                <div class="wr-card-main">
                    <div class="wr-title">${escapeHtml(x.documentTitle || 'Chưa đặt tên phiếu')}</div>
                   <div class="wr-code">${escapeHtml(x.documentNo || '')} · #${x.id}</div>
<div class="wr-date">Ngày tạo: ${formatDateTime(x.createdAtUtc || x.documentDate)}</div>
                </div>

                <div class="wr-card-meta">
                    ${renderStatusBadge(x.status)}
                    <div class="wr-small">
                        ${formatNumber(x.totalLines || 0)} dòng · ${formatNumber(x.totalProductTypes || 0)} loại SP
                    </div>
                </div>

                <a class="wr-open-btn ${isReadonly ? 'readonly' : ''}"
                   href="/admin/warehouse-receiving/${x.id}">
                    ${actionText}
                </a>
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
    const select = document.getElementById('wrWarehouseId');
    if (!select) return;

    select.innerHTML = `<option value="">Đang tải kho...</option>`;

    try {
        const response = await fetch('/admin/api/warehouses/select2?term=', {
            method: 'GET',
            cache: 'no-store'
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            select.innerHTML = `<option value="">Không tải được kho</option>`;
            return;
        }

        const items = api.data?.results || [];

        select.innerHTML = `<option value="">-- Chọn kho --</option>`;

        items.forEach(x => {
            const option = document.createElement('option');
            option.value = x.id;
            option.textContent = x.text;

            if (x.isDefault === true || x.isDefaultWarehouse === true || x.isDefaultForSale === true) {
                option.dataset.isDefault = 'true';
            }

            select.appendChild(option);
        });

        const defaultWarehouse = items.find(x =>
            x.isDefault === true ||
            x.isDefaultWarehouse === true ||
            x.isDefaultForSale === true
        );

        if (defaultWarehouse) {
            select.value = defaultWarehouse.id;
        } else if (items.length === 1) {
            select.value = items[0].id;
        }
    } catch (error) {
        console.error(error);
        select.innerHTML = `<option value="">Không tải được kho</option>`;
    }
}

async function createReceivingReceipt() {
    const btn = document.getElementById('btnCreateReceiving');
    const msg = document.getElementById('wrCreateMessage');

    const warehouseId = Number(document.getElementById('wrWarehouseId')?.value || 0);
    const documentTitle = document.getElementById('wrDocumentTitle')?.value || '';
    const note = document.getElementById('wrNote')?.value || '';

    if (msg) msg.textContent = '';

    if (!warehouseId) {
        if (msg) msg.textContent = 'Vui lòng chọn kho.';
        return;
    }

    try {
        btn.disabled = true;
        btn.textContent = 'Đang tạo...';

        const response = await fetch('/admin/warehouse-receiving/receipts', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                warehouseId: warehouseId,
                supplierId: null,
                documentTitle: documentTitle,
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
    setValue('wrNote', '');
    setText('wrCreateMessage', '');
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