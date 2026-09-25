(function () {
    'use strict';

    let inventoryAdjustmentIndexAbortController = null;
    let inventoryAdjustmentIndexRequestSequence = 0;
    let inventoryAdjustmentIndexSearchTimer = null;
    let inventoryAdjustmentQuickViewModal = null;
    let inventoryAdjustmentImagePreviewModal = null;
    let inventoryAdjustmentIndexImageHoverPreview = null;
    let permissions = {};

    const state = {
        items: [],
        page: 1,
        pageSize: 20,
        totalItems: 0,
        totalPages: 1,
        lifecycle: 'open'
    };

    document.addEventListener('DOMContentLoaded', initInventoryAdjustmentModernIndex);

    function root() {
        return document.querySelector('[data-inventory-adjustment-index]');
    }

    async function initInventoryAdjustmentModernIndex() {
        const pageRoot = root();
        if (!pageRoot) return;
        const quickModal = document.getElementById('inventoryAdjustmentQuickViewModal');
        const imageModal = document.getElementById('inventoryAdjustmentImagePreviewModal');
        if (quickModal) inventoryAdjustmentQuickViewModal = new bootstrap.Modal(quickModal);
        if (imageModal) inventoryAdjustmentImagePreviewModal = new bootstrap.Modal(imageModal);
        bindEvents();
        updateFilterUi();
        await Promise.all([loadPermissions(), loadWarehouseOptions()]);
        await loadInventoryAdjustmentModernIndex();
    }

    function bindEvents() {
        document.getElementById('btnAdjReload')?.addEventListener('click', function () {
            state.page = 1;
            loadInventoryAdjustmentModernIndex();
        });

        const keyword = document.getElementById('adjKeyword');
        keyword?.addEventListener('input', function () {
            updateFilterUi();
            clearTimeout(inventoryAdjustmentIndexSearchTimer);
            inventoryAdjustmentIndexSearchTimer = setTimeout(function () {
                state.page = 1;
                loadInventoryAdjustmentModernIndex();
            }, 350);
        });
        document.getElementById('adjClearSearch')?.addEventListener('click', function () {
            if (keyword) keyword.value = '';
            state.page = 1;
            updateFilterUi();
            loadInventoryAdjustmentModernIndex();
            keyword?.focus();
        });

        ['adjWarehouse', 'adjType', 'adjReason', 'adjStatus', 'adjFromDate', 'adjToDate', 'adjPageSize']
            .forEach(id => document.getElementById(id)?.addEventListener('change', function () {
                if (id === 'adjStatus') state.lifecycle = this.value || 'open';
                state.page = 1;
                updateFilterUi();
                if (datesAreValid()) loadInventoryAdjustmentModernIndex();
            }));

        document.getElementById('adjResetFilters')?.addEventListener('click', resetFilters);
        document.querySelectorAll('.adj-index-kpi[data-adj-state]').forEach(button => {
            button.addEventListener('click', function () {
                const requested = this.dataset.adjState || 'all';
                state.lifecycle = state.lifecycle === requested ? 'all' : requested;
                const select = document.getElementById('adjStatus');
                if (select) select.value = state.lifecycle;
                state.page = 1;
                syncDesktopToMobile();
                updateFilterUi();
                loadInventoryAdjustmentModernIndex();
            });
        });

        document.getElementById('adjPageNumbers')?.addEventListener('click', function (event) {
            const button = event.target.closest('[data-page]');
            if (!button || button.disabled) return;
            const page = Number(button.dataset.page || 0);
            if (page < 1 || page > state.totalPages) return;
            state.page = page;
            loadInventoryAdjustmentModernIndex();
        });

        document.getElementById('inventoryAdjustmentMobileFilterSheet')?.addEventListener('show.bs.offcanvas', syncDesktopToMobile);
        document.getElementById('adjMobileApplyFilters')?.addEventListener('click', function () {
            syncMobileToDesktop();
            if (!datesAreValid()) return;
            bootstrap.Offcanvas.getInstance(document.getElementById('inventoryAdjustmentMobileFilterSheet'))?.hide();
            state.page = 1;
            updateFilterUi();
            loadInventoryAdjustmentModernIndex();
        });
        document.getElementById('adjMobileClearFilters')?.addEventListener('click', function () {
            resetFilters();
            syncDesktopToMobile();
        });
    }

    async function loadPermissions() {
        const pageRoot = root();
        if (!pageRoot) return;
        try {
            const response = await fetch(pageRoot.dataset.permissionsUrl, { headers: { Accept: 'application/json' } });
            permissions = response.ok ? await response.json() : {};
        } catch { permissions = {}; }
        document.getElementById('btnCreateDocument')?.classList.toggle('d-none', !permissions.canCreate);
    }

    async function loadWarehouseOptions() {
        const pageRoot = root();
        if (!pageRoot) return;
        try {
            const response = await fetch(pageRoot.dataset.optionsUrl);
            if (!response.ok) return;
            const items = await response.json();
            ['adjWarehouse', 'adjMobileWarehouse'].forEach(id => {
                const select = document.getElementById(id);
                if (!select) return;
                const selected = select.value;
                select.innerHTML = '<option value="">Tất cả kho</option>' + (items || []).map(item => `<option value="${Number(item.id)}">${escapeHtml(item.name || '')}</option>`).join('');
                select.value = selected;
            });
        } catch { }
    }

    function buildQuery() {
        const params = new URLSearchParams();
        const values = {
            keyword: document.getElementById('adjKeyword')?.value?.trim() || '',
            warehouseId: document.getElementById('adjWarehouse')?.value || '',
            adjustmentType: document.getElementById('adjType')?.value || '',
            reasonType: document.getElementById('adjReason')?.value || '',
            fromDate: document.getElementById('adjFromDate')?.value || '',
            toDate: document.getElementById('adjToDate')?.value || ''
        };
        state.pageSize = Number(document.getElementById('adjPageSize')?.value || 20);
        state.lifecycle = document.getElementById('adjStatus')?.value || 'open';
        params.set('page', String(state.page));
        params.set('pageSize', String(state.pageSize));
        params.set('state', state.lifecycle);
        Object.entries(values).forEach(([key, value]) => { if (value !== '') params.set(key, value); });
        return params.toString();
    }

    async function loadInventoryAdjustmentModernIndex() {
        if (!datesAreValid()) return;
        const pageRoot = root();
        if (!pageRoot) return;
        const panel = document.getElementById('inventoryAdjustmentResultsPanel');
        panel?.setAttribute('aria-busy', 'true');
        setLoading();
        inventoryAdjustmentIndexAbortController?.abort();
        inventoryAdjustmentIndexAbortController = new AbortController();
        const requestSequence = ++inventoryAdjustmentIndexRequestSequence;
        try {
            const response = await fetch(`${pageRoot.dataset.dataUrl}?${buildQuery()}`, { signal: inventoryAdjustmentIndexAbortController.signal });
            if (!response.ok) throw new Error('Không tải được danh sách phiếu điều chỉnh kho.');
            const payload = await response.json();
            if (requestSequence !== inventoryAdjustmentIndexRequestSequence) return;
            state.items = payload.items || [];
            state.page = Number(payload.page || 1);
            state.pageSize = Number(payload.pageSize || 20);
            state.totalItems = Number(payload.totalItems || 0);
            state.totalPages = Math.max(1, Number(payload.totalPages || 1));
            renderExactSummary(payload.summary || {});
            renderInventoryAdjustmentDesktopRows(state.items);
            renderInventoryAdjustmentMobileCards(state.items);
            renderInventoryAdjustmentCircularPagination();
            bindRows();
        } catch (error) {
            if (error?.name === 'AbortError') return;
            renderError(error?.message || 'Không tải được danh sách phiếu điều chỉnh kho.');
        } finally {
            if (requestSequence === inventoryAdjustmentIndexRequestSequence) panel?.setAttribute('aria-busy', 'false');
        }
    }

    function renderExactSummary(summary) {
        document.getElementById('statTotal').textContent = formatNumber(summary.totalItems || 0);
        document.getElementById('statWorking').textContent = formatNumber(summary.workingItems || 0);
        document.getElementById('statPending').textContent = formatNumber(summary.pendingItems || 0);
        document.getElementById('statApproved').textContent = formatNumber(summary.approvedItems || 0);
    }

    function renderInventoryAdjustmentDesktopRows(items) {
        const body = document.getElementById('adjTableBody');
        if (!body) return;
        if (!items.length) {
            body.innerHTML = '<tr><td colspan="7" class="gds-empty">Không có phiếu điều chỉnh phù hợp.</td></tr>';
            return;
        }
        body.innerHTML = items.map(item => `
            <tr class="adj-index-row" data-document-id="${Number(item.documentId)}" tabindex="0">
                <td><div class="adj-index-document-no">${escapeHtml(item.documentNo || '')}</div><div class="adj-index-muted">${formatDate(item.documentDate)}</div></td>
                <td><strong class="text-dark">${escapeHtml(item.warehouseName || '')}</strong></td>
                <td><div class="${Number(item.adjustmentType) === 30 ? 'adj-type-in' : 'adj-type-out'}">${Number(item.adjustmentType) === 30 ? '+' : '−'} ${escapeHtml(item.adjustmentTypeLabel || '')}</div><div class="adj-index-muted">${escapeHtml(item.reasonTypeLabel || '')}</div></td>
                <td><strong class="text-dark">${formatNumber(item.totalLines)} dòng</strong></td>
                <td>${statusBadge(item.status, item.statusLabel)}</td>
                <td><div class="adj-index-note">${escapeHtml(item.note || '—')}</div></td>
                <td class="text-end"><div class="d-inline-flex gap-2"><button type="button" class="gds-icon-button adj-index-quick-button" aria-label="Xem nhanh"><i class="bx bx-show"></i></button>${renderActions(item)}</div></td>
            </tr>`).join('');
    }

    function renderInventoryAdjustmentMobileCards(items) {
        const list = document.getElementById('adjMobileList');
        if (!list) return;
        if (!items.length) {
            list.innerHTML = '<div class="gds-empty">Không có phiếu điều chỉnh phù hợp.</div>';
            return;
        }
        list.innerHTML = items.map(item => `
            <article class="adj-index-mobile-card" data-document-id="${Number(item.documentId)}">
                <div class="d-flex justify-content-between align-items-start gap-2"><div><div class="adj-index-document-no">${escapeHtml(item.documentNo || '')}</div><div class="adj-index-muted">${formatDate(item.documentDate)}</div></div>${statusBadge(item.status, item.statusLabel)}</div>
                <div class="adj-index-mobile-route"><span><small>Kho</small><strong>${escapeHtml(item.warehouseName || '')}</strong></span><span><small>Điều chỉnh</small><strong class="${Number(item.adjustmentType) === 30 ? 'adj-type-in' : 'adj-type-out'}">${escapeHtml(item.adjustmentTypeLabel || '')}</strong></span><span><small>Lý do</small><strong>${escapeHtml(item.reasonTypeLabel || '')}</strong></span></div>
                <div class="adj-index-mobile-metrics"><span><strong>${formatNumber(item.totalLines)}</strong> dòng sản phẩm</span></div>
                ${item.note ? `<div class="adj-index-mobile-note">${escapeHtml(item.note)}</div>` : ''}
                <div class="d-flex gap-2 mt-3"><button type="button" class="btn btn-label-primary flex-grow-1 adj-index-quick-button"><i class="bx bx-show me-1"></i>Xem nhanh</button>${renderActions(item, true)}</div>
            </article>`).join('');
    }

    function renderActions(item, mobile = false) {
        let html = `<a class="btn ${mobile ? '' : 'btn-sm '}btn-primary" href="/admin/inventory-adjustment-documents/${Number(item.documentId)}">Mở phiếu</a>`;
        if (Number(item.status) === 0 && permissions.canUpdate) {
            html += `<a class="btn ${mobile ? '' : 'btn-sm '}btn-label-secondary" href="/admin/inventory-adjustment-documents/${Number(item.documentId)}/edit">Sửa</a>`;
        }
        return html;
    }

    function bindRows() {
        document.querySelectorAll('.adj-index-row, .adj-index-mobile-card').forEach(item => {
            item.querySelector('.adj-index-quick-button')?.addEventListener('click', () => openInventoryAdjustmentQuickView(Number(item.dataset.documentId || 0)));
            item.addEventListener('dblclick', event => { if (!event.target.closest('a,button')) openInventoryAdjustmentQuickView(Number(item.dataset.documentId || 0)); });
            item.addEventListener('keydown', event => { if (event.key === 'Enter' && !event.target.closest('a,button')) { event.preventDefault(); openInventoryAdjustmentQuickView(Number(item.dataset.documentId || 0)); } });
        });
    }

    function renderInventoryAdjustmentCircularPagination() {
        const pagination = document.getElementById('adjPageNumbers');
        const info = document.getElementById('adjPagingInfo');
        if (!pagination || !info) return;
        const first = state.totalItems ? ((state.page - 1) * state.pageSize) + 1 : 0;
        const last = Math.min(state.totalItems, state.page * state.pageSize);
        info.textContent = state.totalItems ? `${first}–${last} / ${formatNumber(state.totalItems)} phiếu` : '0 kết quả';
        const pages = visiblePages(state.page, state.totalPages);
        pagination.innerHTML = `${pageButton(state.page - 1, '<i class="bx bx-chevron-left"></i>', state.page <= 1, 'Trang trước')}${pages.map(value => value === '…' ? '<li class="page-item disabled"><span class="page-link">…</span></li>' : pageButton(value, String(value), false, `Trang ${value}`, value === state.page)).join('')}${pageButton(state.page + 1, '<i class="bx bx-chevron-right"></i>', state.page >= state.totalPages, 'Trang sau')}`;
    }

    function visiblePages(page, totalPages) {
        if (totalPages <= 5) return Array.from({ length: totalPages }, (_, index) => index + 1);
        if (page <= 3) return [1, 2, 3, 4, '…', totalPages];
        if (page >= totalPages - 2) return [1, '…', totalPages - 3, totalPages - 2, totalPages - 1, totalPages];
        return [1, '…', page - 1, page, page + 1, '…', totalPages];
    }

    function pageButton(page, content, disabled, label, active = false) {
        return `<li class="page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}"><button type="button" class="page-link" data-page="${page}" aria-label="${label}"${disabled ? ' disabled' : ''}>${content}</button></li>`;
    }

    async function openInventoryAdjustmentQuickView(id) {
        if (!id) return;
        const pageRoot = root();
        const body = document.getElementById('inventoryAdjustmentQuickViewBody');
        const title = document.getElementById('inventoryAdjustmentQuickViewTitle');
        const date = document.getElementById('inventoryAdjustmentQuickDate');
        const status = document.getElementById('inventoryAdjustmentQuickState');
        const open = document.getElementById('btnOpenInventoryAdjustmentDetail');
        if (!pageRoot || !body) return;
        if (open) open.href = `/admin/inventory-adjustment-documents/${id}`;
        if (title) title.textContent = 'Thông tin phiếu';
        if (date) date.textContent = '—';
        if (status) status.textContent = 'Đang tải';
        body.innerHTML = '<div class="text-center text-muted py-5"><span class="spinner-border spinner-border-sm me-2"></span>Đang tải thông tin...</div>';
        inventoryAdjustmentQuickViewModal?.show();
        try {
            const response = await fetch(`${pageRoot.dataset.quickViewUrl}?documentId=${encodeURIComponent(id)}`);
            if (!response.ok) throw new Error('Không tải được chi tiết phiếu điều chỉnh.');
            const detail = await response.json();
            const lines = detail.lines || [];
            if (title) title.textContent = detail.documentNo || 'Phiếu điều chỉnh';
            if (date) date.textContent = formatDate(detail.documentDate);
            if (status) { status.className = `gds-status gds-status--dot ${statusClass(detail.status)}`; status.textContent = detail.statusLabel || 'Không rõ'; }
            body.innerHTML = `<div class="adj-index-quick-overview"><div><small>Kho</small><strong>${escapeHtml(detail.warehouseName || '')}</strong></div><div><small>Hướng điều chỉnh</small><strong class="${Number(detail.adjustmentType) === 30 ? 'text-success' : 'text-warning'}">${escapeHtml(detail.adjustmentTypeLabel || '')}</strong></div><div><small>Lý do</small><strong>${escapeHtml(detail.reasonTypeLabel || '')}</strong></div><div><small>Số dòng</small><strong>${formatNumber(detail.totalLines)}</strong></div></div><div class="adj-index-quick-note"><small>Ghi chú</small><div>${escapeHtml(detail.note || 'Không có ghi chú.')}</div></div><div class="adj-index-quick-lines"><div class="d-flex justify-content-between mb-3"><h5 class="mb-0 text-dark">Sản phẩm điều chỉnh</h5><span class="text-muted small">${formatNumber(lines.length)} dòng</span></div>${lines.length ? lines.map(line => renderQuickLine(line, detail.adjustmentType)).join('') : '<div class="gds-empty">Phiếu chưa có dòng sản phẩm.</div>'}</div>`;
            bindQuickImages();
        } catch (error) {
            body.innerHTML = `<div class="gds-empty text-danger">${escapeHtml(error?.message || 'Không tải được chi tiết phiếu.')}</div>`;
        }
    }

    function renderQuickLine(line, adjustmentType) {
        const image = line.imageUrl ? `<button type="button" class="adj-index-line-image-button" data-preview-image="${escapeHtml(line.imageUrl)}" data-preview-title="${escapeHtml(line.productName || 'Sản phẩm')}"><img src="${escapeHtml(line.imageUrl)}" alt="${escapeHtml(line.productName || 'Sản phẩm')}" /></button>` : '<span class="adj-index-line-image-placeholder"><i class="bx bx-image"></i></span>';
        const sign = Number(adjustmentType) === 30 ? '+' : '−';
        return `<article class="adj-index-quick-line">${image}<div><strong>${escapeHtml(line.productName || '')}</strong><span>${escapeHtml(line.unitName || 'Đơn vị')}</span></div><div class="adj-index-line-quantity"><small>Số lượng điều chỉnh</small><strong class="${Number(adjustmentType) === 30 ? 'text-success' : 'text-warning'}">${sign}${formatNumber(line.quantity)} ${escapeHtml(line.unitName || '')}</strong></div></article>`;
    }

    function bindQuickImages() {
        document.querySelectorAll('[data-preview-image]').forEach(button => {
            button.addEventListener('pointerenter', event => showImageHoverPreview(button, event));
            button.addEventListener('pointermove', moveImageHoverPreview);
            button.addEventListener('pointerleave', hideImageHoverPreview);
            button.addEventListener('click', () => {
                const image = document.getElementById('inventoryAdjustmentImagePreviewImage');
                const title = document.getElementById('inventoryAdjustmentImagePreviewTitle');
                if (image) image.src = button.dataset.previewImage || '';
                if (title) title.textContent = button.dataset.previewTitle || 'Ảnh sản phẩm';
                inventoryAdjustmentImagePreviewModal?.show();
            });
        });
    }

    function showImageHoverPreview(button, event) {
        if (!window.matchMedia('(hover: hover) and (pointer: fine)').matches) return;
        if (!inventoryAdjustmentIndexImageHoverPreview) {
            inventoryAdjustmentIndexImageHoverPreview = document.createElement('div');
            inventoryAdjustmentIndexImageHoverPreview.className = 'adj-index-image-hover-preview';
            inventoryAdjustmentIndexImageHoverPreview.innerHTML = '<img alt="Ảnh sản phẩm phóng to" />';
            document.body.appendChild(inventoryAdjustmentIndexImageHoverPreview);
        }
        inventoryAdjustmentIndexImageHoverPreview.querySelector('img').src = button.dataset.previewImage || '';
        inventoryAdjustmentIndexImageHoverPreview.classList.add('is-visible');
        moveImageHoverPreview(event);
    }

    function moveImageHoverPreview(event) {
        if (!inventoryAdjustmentIndexImageHoverPreview) return;
        const left = Math.min(window.innerWidth - 276, event.clientX + 18);
        const top = Math.min(window.innerHeight - 276, event.clientY + 18);
        inventoryAdjustmentIndexImageHoverPreview.style.left = `${Math.max(12, left)}px`;
        inventoryAdjustmentIndexImageHoverPreview.style.top = `${Math.max(12, top)}px`;
    }

    function hideImageHoverPreview() {
        inventoryAdjustmentIndexImageHoverPreview?.classList.remove('is-visible');
    }

    function datesAreValid() {
        const from = document.getElementById('adjFromDate')?.value || '';
        const to = document.getElementById('adjToDate')?.value || '';
        const valid = !from || !to || from <= to;
        document.getElementById('adjDateError')?.classList.toggle('d-none', valid);
        return valid;
    }

    function updateFilterUi() {
        const keyword = document.getElementById('adjKeyword')?.value?.trim() || '';
        document.getElementById('adjClearSearch')?.classList.toggle('d-none', !keyword);
        const activeCount = [keyword, document.getElementById('adjWarehouse')?.value, document.getElementById('adjType')?.value, document.getElementById('adjReason')?.value, document.getElementById('adjFromDate')?.value, document.getElementById('adjToDate')?.value, (document.getElementById('adjStatus')?.value || 'open') !== 'open' ? 'state' : ''].filter(Boolean).length;
        document.getElementById('adjResetFilters')?.classList.toggle('d-none', activeCount === 0);
        const badge = document.getElementById('adjActiveFilterCount');
        if (badge) { badge.textContent = String(activeCount); badge.classList.toggle('d-none', activeCount === 0); }
        document.querySelectorAll('.adj-index-kpi[data-adj-state]').forEach(button => { const active = button.dataset.adjState === state.lifecycle; button.classList.toggle('is-active', active); button.setAttribute('aria-pressed', String(active)); });
    }

    function resetFilters() {
        const defaults = { adjKeyword: '', adjWarehouse: '', adjType: '', adjReason: '', adjStatus: 'open', adjFromDate: '', adjToDate: '', adjPageSize: '20' };
        Object.entries(defaults).forEach(([id, value]) => { const element = document.getElementById(id); if (element) element.value = value; });
        state.page = 1;
        state.lifecycle = 'open';
        syncDesktopToMobile();
        updateFilterUi();
        datesAreValid();
        loadInventoryAdjustmentModernIndex();
    }

    function syncDesktopToMobile() {
        [['adjWarehouse','adjMobileWarehouse'],['adjType','adjMobileType'],['adjReason','adjMobileReason'],['adjStatus','adjMobileStatus'],['adjFromDate','adjMobileFromDate'],['adjToDate','adjMobileToDate'],['adjPageSize','adjMobilePageSize']].forEach(([desktopId,mobileId]) => { const desktop=document.getElementById(desktopId); const mobile=document.getElementById(mobileId); if(desktop&&mobile) mobile.value=desktop.value; });
    }

    function syncMobileToDesktop() {
        [['adjMobileWarehouse','adjWarehouse'],['adjMobileType','adjType'],['adjMobileReason','adjReason'],['adjMobileStatus','adjStatus'],['adjMobileFromDate','adjFromDate'],['adjMobileToDate','adjToDate'],['adjMobilePageSize','adjPageSize']].forEach(([mobileId,desktopId]) => { const mobile=document.getElementById(mobileId); const desktop=document.getElementById(desktopId); if(mobile&&desktop) desktop.value=mobile.value; });
        state.lifecycle = document.getElementById('adjStatus')?.value || 'open';
    }

    function setLoading() {
        const body = document.getElementById('adjTableBody');
        const mobile = document.getElementById('adjMobileList');
        if (body) body.innerHTML = '<tr><td colspan="7" class="gds-empty">Đang tải dữ liệu...</td></tr>';
        if (mobile) mobile.innerHTML = '<div class="gds-empty">Đang tải dữ liệu...</div>';
    }

    function renderError(message) {
        const safe = escapeHtml(message);
        const body = document.getElementById('adjTableBody');
        const mobile = document.getElementById('adjMobileList');
        if (body) body.innerHTML = `<tr><td colspan="7" class="gds-empty text-danger">${safe}</td></tr>`;
        if (mobile) mobile.innerHTML = `<div class="gds-empty text-danger">${safe}</div>`;
    }

    function statusBadge(status, label) {
        const classes = ['adj-badge-draft', 'adj-badge-pending', 'adj-badge-approved', 'adj-badge-rejected', 'adj-badge-cancelled'];
        return `<span class="adj-badge ${classes[Number(status)] || classes[0]}">${escapeHtml(label || 'Không rõ')}</span>`;
    }

    function statusClass(status) {
        return Number(status) === 2 ? 'is-success' : Number(status) === 1 ? 'is-warning' : Number(status) === 3 || Number(status) === 4 ? 'is-danger' : 'is-neutral';
    }

    function formatDate(value) {
        if (!value) return '—';
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? '—' : date.toLocaleDateString('vi-VN');
    }

    function formatNumber(value) {
        return Number(value || 0).toLocaleString('vi-VN', { maximumFractionDigits: 3 });
    }

    function escapeHtml(value) {
        return String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;');
    }
})();
