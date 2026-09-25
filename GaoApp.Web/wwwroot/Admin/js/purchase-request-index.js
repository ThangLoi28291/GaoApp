(function () {
    'use strict';

    let purchaseRequestIndexAbortController = null;
    let purchaseRequestIndexRequestSequence = 0;
    let purchaseRequestIndexSearchTimer = null;
    let purchaseRequestQuickViewModal = null;
    let purchaseRequestImagePreviewModal = null;
    let purchaseRequestIndexImageHoverPreview = null;

    const state = {
        items: [],
        page: 1,
        pageSize: 20,
        totalItems: 0,
        totalPages: 1,
        lifecycle: 'all',
        scope: 'mine',
        initialScope: 'mine',
        canViewStore: false
    };

    document.addEventListener('DOMContentLoaded', initPurchaseRequestModernIndex);

    function root() {
        return document.querySelector('[data-purchase-request-index]');
    }

    async function initPurchaseRequestModernIndex() {
        const pageRoot = root();
        if (!pageRoot) return;

        state.canViewStore = pageRoot.dataset.canViewStore === 'true';
        state.initialScope = pageRoot.dataset.initialScope === 'store' && state.canViewStore
            ? 'store'
            : 'mine';
        state.scope = state.initialScope;

        const quickModal = document.getElementById('purchaseRequestQuickViewModal');
        const imageModal = document.getElementById('purchaseRequestImagePreviewModal');
        if (quickModal) purchaseRequestQuickViewModal = new bootstrap.Modal(quickModal);
        if (imageModal) purchaseRequestImagePreviewModal = new bootstrap.Modal(imageModal);

        bindEvents();
        updateScopeUi();
        updateFilterUi();
        await loadRequesterOptions();
        await loadPurchaseRequestModernIndex();
    }

    function bindEvents() {
        document.getElementById('btnPrReload')?.addEventListener('click', function () {
            state.page = 1;
            loadPurchaseRequestModernIndex();
        });

        document.querySelectorAll('[data-pr-scope]').forEach(button => {
            button.addEventListener('click', function () {
                const requestedScope = this.dataset.prScope;
                state.scope = requestedScope === 'store' && state.canViewStore ? 'store' : 'mine';
                if (state.scope === 'mine') setValue('prRequester', '');
                state.page = 1;
                updateScopeUi();
                syncDesktopToMobile();
                updateFilterUi();
                loadPurchaseRequestModernIndex();
            });
        });

        const keyword = document.getElementById('prKeyword');
        keyword?.addEventListener('input', function () {
            updateFilterUi();
            clearTimeout(purchaseRequestIndexSearchTimer);
            purchaseRequestIndexSearchTimer = setTimeout(function () {
                state.page = 1;
                loadPurchaseRequestModernIndex();
            }, 350);
        });

        document.getElementById('prClearSearch')?.addEventListener('click', function () {
            if (keyword) keyword.value = '';
            state.page = 1;
            updateFilterUi();
            loadPurchaseRequestModernIndex();
            keyword?.focus();
        });

        ['prRequester', 'prStatus', 'prFromDate', 'prToDate', 'prPageSize']
            .forEach(id => document.getElementById(id)?.addEventListener('change', function () {
                if (id === 'prStatus') state.lifecycle = this.value || 'all';
                if (id === 'prPageSize') state.pageSize = Number(this.value || 20);
                state.page = 1;
                updateFilterUi();
                if (datesAreValid()) loadPurchaseRequestModernIndex();
            }));

        document.getElementById('prResetFilters')?.addEventListener('click', resetFilters);

        document.querySelectorAll('.pr-index-kpi[data-pr-state]').forEach(button => {
            button.addEventListener('click', function () {
                const requested = this.dataset.prState || 'all';
                state.lifecycle = state.lifecycle === requested && requested !== 'all'
                    ? 'all'
                    : requested;
                setValue('prStatus', state.lifecycle);
                state.page = 1;
                syncDesktopToMobile();
                updateFilterUi();
                loadPurchaseRequestModernIndex();
            });
        });

        document.getElementById('prPageNumbers')?.addEventListener('click', function (event) {
            const button = event.target.closest('[data-page]');
            if (!button || button.disabled) return;
            const page = Number(button.dataset.page || 0);
            if (page < 1 || page > state.totalPages || page === state.page) return;
            state.page = page;
            loadPurchaseRequestModernIndex();
        });

        const results = document.getElementById('purchaseRequestResultsPanel');
        results?.addEventListener('click', function (event) {
            const quickButton = event.target.closest('[data-pr-quick]');
            if (quickButton) {
                event.preventDefault();
                event.stopPropagation();
                openPurchaseRequestQuickView(Number(quickButton.dataset.prQuick || 0));
                return;
            }

            const imageButton = event.target.closest('[data-pr-image]');
            if (imageButton) {
                event.preventDefault();
                event.stopPropagation();
                openImagePreview(imageButton.dataset.prImage, imageButton.dataset.prImageTitle);
            }
        });

        results?.addEventListener('dblclick', function (event) {
            if (event.target.closest('a,button,input,select')) return;
            const row = event.target.closest('[data-pr-request-id]');
            if (row) openPurchaseRequestQuickView(Number(row.dataset.prRequestId || 0));
        });

        results?.addEventListener('keydown', function (event) {
            if (event.key !== 'Enter' || event.target.closest('a,button,input,select')) return;
            const row = event.target.closest('[data-pr-request-id]');
            if (row) openPurchaseRequestQuickView(Number(row.dataset.prRequestId || 0));
        });

        document.getElementById('purchaseRequestMobileFilterSheet')
            ?.addEventListener('show.bs.offcanvas', syncDesktopToMobile);

        document.getElementById('prMobileApplyFilters')?.addEventListener('click', function () {
            syncMobileToDesktop();
            if (!datesAreValid()) return;
            bootstrap.Offcanvas.getInstance(document.getElementById('purchaseRequestMobileFilterSheet'))?.hide();
            state.page = 1;
            updateScopeUi();
            updateFilterUi();
            loadPurchaseRequestModernIndex();
        });

        document.getElementById('prMobileClearFilters')?.addEventListener('click', function () {
            resetFilters();
            syncDesktopToMobile();
        });

        document.getElementById('purchaseRequestQuickViewBody')?.addEventListener('click', function (event) {
            const imageButton = event.target.closest('[data-pr-image]');
            if (!imageButton) return;
            openImagePreview(imageButton.dataset.prImage, imageButton.dataset.prImageTitle);
        });

        document.getElementById('purchaseRequestQuickViewBody')?.addEventListener('pointerenter', function (event) {
            const imageButton = event.target.closest?.('[data-pr-image]');
            if (imageButton && event.pointerType !== 'touch') showImageHoverPreview(imageButton, event);
        }, true);

        document.getElementById('purchaseRequestQuickViewBody')?.addEventListener('pointerleave', function (event) {
            if (event.target.closest?.('[data-pr-image]')) hideImageHoverPreview();
        }, true);
    }

    async function loadRequesterOptions() {
        const pageRoot = root();
        if (!pageRoot || !state.canViewStore) return;

        try {
            const response = await fetch(pageRoot.dataset.requesterOptionsUrl, {
                headers: { Accept: 'application/json' }
            });
            if (!response.ok) return;
            const items = await response.json();
            const options = '<option value="">Tất cả nhân viên</option>'
                + (items || []).map(item => `<option value="${Number(item.userId)}">${escapeHtml(item.name || '')}</option>`).join('');
            ['prRequester', 'prMobileRequester'].forEach(id => {
                const select = document.getElementById(id);
                if (select) select.innerHTML = options;
            });
        } catch { }
    }

    function buildQuery() {
        const params = new URLSearchParams();
        const values = {
            scope: state.scope,
            keyword: valueOf('prKeyword'),
            requesterUserId: state.scope === 'store' ? valueOf('prRequester') : '',
            state: state.lifecycle,
            fromDate: valueOf('prFromDate'),
            toDate: valueOf('prToDate'),
            page: state.page,
            pageSize: valueOf('prPageSize') || state.pageSize
        };
        Object.entries(values).forEach(([key, value]) => {
            if (value !== '' && value !== null && value !== undefined) params.set(key, value);
        });
        return params.toString();
    }

    async function loadPurchaseRequestModernIndex() {
        const pageRoot = root();
        if (!pageRoot || !datesAreValid()) return;

        if (purchaseRequestIndexAbortController) purchaseRequestIndexAbortController.abort();
        purchaseRequestIndexAbortController = new AbortController();
        const sequence = ++purchaseRequestIndexRequestSequence;
        setLoading();

        try {
            const response = await fetch(`${pageRoot.dataset.dataUrl}?${buildQuery()}`, {
                signal: purchaseRequestIndexAbortController.signal,
                headers: { Accept: 'application/json' }
            });
            if (!response.ok) throw new Error('Không thể tải danh sách yêu cầu mua hàng.');
            const payload = await response.json();
            if (sequence !== purchaseRequestIndexRequestSequence) return;

            state.items = payload.items || [];
            state.page = Number(payload.page || 1);
            state.pageSize = Number(payload.pageSize || 20);
            state.totalItems = Number(payload.totalItems || 0);
            state.totalPages = Math.max(1, Number(payload.totalPages || 1));

            renderSummary(payload.summary || {});
            renderPurchaseRequestDesktopRows(state.items);
            renderPurchaseRequestMobileCards(state.items);
            renderPurchaseRequestCircularPagination();
            updateFilterUi();
            document.getElementById('purchaseRequestResultsPanel')?.setAttribute('aria-busy', 'false');
        } catch (error) {
            if (error.name === 'AbortError') return;
            renderError(error.message || 'Không thể tải dữ liệu.');
        }
    }

    function renderSummary(summary) {
        setText('prStatTotal', summary.totalItems);
        setText('prStatWorking', summary.workingItems);
        setText('prStatOrderReady', summary.orderReadyItems);
        setText('prStatConverted', summary.convertedItems);
    }

    function renderPurchaseRequestDesktopRows(items) {
        const body = document.getElementById('prTableBody');
        if (!body) return;
        if (!items.length) {
            body.innerHTML = '<tr><td colspan="6" class="gds-empty"><i class="bx bx-basket"></i><strong>Không có yêu cầu phù hợp</strong><span>Hãy thay đổi từ khóa hoặc bộ lọc.</span></td></tr>';
            return;
        }

        body.innerHTML = items.map(item => {
            const id = Number(item.requestId || 0);
            const detailUrl = detailUrlFor(id);
            return `<tr class="pr-index-row" tabindex="0" data-pr-request-id="${id}">
                <td><div class="pr-index-title">${escapeHtml(item.title || 'Chưa đặt tên')}</div><div class="pr-index-muted"><span class="gds-code">${escapeHtml(item.requestNumber || '')}</span><span>${formatDate(item.requestDate)}</span></div></td>
                <td class="pr-index-requester-column"><div class="pr-index-requester"><i class="bx bx-user"></i><span>${escapeHtml(item.requestedByName || '—')}</span></div></td>
                <td><strong class="pr-index-need-date">${item.needByDate ? formatDate(item.needByDate) : 'Không giới hạn'}</strong></td>
                <td>${progressMarkup(item)}</td>
                <td>${statusBadge(item.status, item.statusLabel)}<div class="pr-index-status-hint">${escapeHtml(item.statusHint || '')}</div></td>
                <td class="text-end"><div class="pr-index-actions"><button type="button" class="gds-icon-button" data-pr-quick="${id}" title="Xem nhanh" aria-label="Xem nhanh ${escapeHtml(item.requestNumber || '')}"><i class="bx bx-show"></i></button><a class="btn btn-sm btn-primary" href="${detailUrl}">Mở</a></div></td>
            </tr>`;
        }).join('');
    }

    function renderPurchaseRequestMobileCards(items) {
        const list = document.getElementById('purchaseRequestMobileList');
        if (!list) return;
        if (!items.length) {
            list.innerHTML = '<div class="gds-empty"><i class="bx bx-basket"></i><strong>Không có yêu cầu phù hợp</strong><span>Hãy thay đổi từ khóa hoặc bộ lọc.</span></div>';
            return;
        }

        list.innerHTML = items.map(item => {
            const id = Number(item.requestId || 0);
            return `<article class="pr-index-mobile-card" tabindex="0" data-pr-request-id="${id}">
                <div class="d-flex justify-content-between align-items-start gap-2"><div class="min-w-0"><div class="pr-index-mobile-title">${escapeHtml(item.title || 'Chưa đặt tên')}</div><div class="pr-index-muted"><span class="gds-code">${escapeHtml(item.requestNumber || '')}</span><span>${formatDate(item.requestDate)}</span></div></div>${statusBadge(item.status, item.statusLabel)}</div>
                <div class="pr-index-mobile-facts"><span class="pr-index-requester-column"><small>Người yêu cầu</small><strong>${escapeHtml(item.requestedByName || '—')}</strong></span><span><small>Ngày cần hàng</small><strong>${item.needByDate ? formatDate(item.needByDate) : 'Không giới hạn'}</strong></span></div>
                ${progressMarkup(item)}
                <div class="pr-index-status-hint">${escapeHtml(item.statusHint || '')}</div>
                <div class="pr-index-mobile-actions"><button type="button" class="btn btn-label-primary flex-grow-1" data-pr-quick="${id}"><i class="bx bx-show"></i>Xem nhanh</button><a class="btn btn-primary" href="${detailUrlFor(id)}">Mở</a></div>
            </article>`;
        }).join('');
    }

    function progressMarkup(item) {
        const lineCount = Number(item.lineCount || 0);
        const eligibleLineCount = Number(item.eligibleLineCount || 0);
        const completedLineCount = Number(item.completedLineCount || 0);
        if (eligibleLineCount <= 0) {
            return `<div class="pr-index-progress"><div class="pr-index-progress__label"><strong>${lineCount} mặt hàng</strong><span>Chưa chốt</span></div><div class="progress"><div class="progress-bar is-neutral" style="width:0%"></div></div></div>`;
        }

        const percent = Math.min(100, Math.max(0, Number(item.progressPercent || 0)));
        return `<div class="pr-index-progress"><div class="pr-index-progress__label"><strong>${completedLineCount}/${eligibleLineCount} mặt hàng</strong><span>${percent}%</span></div><div class="progress"><div class="progress-bar" style="width:${percent}%"></div></div></div>`;
    }

    function renderPurchaseRequestCircularPagination() {
        const list = document.getElementById('prPageNumbers');
        const info = document.getElementById('prPagingInfo');
        if (info) {
            const first = state.totalItems === 0 ? 0 : (state.page - 1) * state.pageSize + 1;
            const last = Math.min(state.totalItems, state.page * state.pageSize);
            info.textContent = state.totalItems === 0
                ? '0 kết quả'
                : `${first}–${last} / ${state.totalItems} yêu cầu`;
        }
        if (!list) return;

        const pages = paginationWindow(state.page, state.totalPages);
        const previous = `<li class="page-item ${state.page <= 1 ? 'disabled' : ''}"><button class="page-link" data-page="${state.page - 1}" aria-label="Trang trước"><i class="bx bx-chevron-left"></i></button></li>`;
        const next = `<li class="page-item ${state.page >= state.totalPages ? 'disabled' : ''}"><button class="page-link" data-page="${state.page + 1}" aria-label="Trang sau"><i class="bx bx-chevron-right"></i></button></li>`;
        const middle = pages.map(page => page === '…'
            ? '<li class="page-item disabled"><span class="page-link">…</span></li>'
            : `<li class="page-item ${page === state.page ? 'active' : ''}"><button class="page-link" data-page="${page}" aria-current="${page === state.page ? 'page' : 'false'}">${page}</button></li>`).join('');
        list.innerHTML = previous + middle + next;
    }

    function paginationWindow(current, total) {
        if (total <= 7) return Array.from({ length: total }, (_, index) => index + 1);
        if (current <= 4) return [1, 2, 3, 4, 5, '…', total];
        if (current >= total - 3) return [1, '…', total - 4, total - 3, total - 2, total - 1, total];
        return [1, '…', current - 1, current, current + 1, '…', total];
    }

    async function openPurchaseRequestQuickView(requestId) {
        const pageRoot = root();
        if (!pageRoot || requestId <= 0 || !purchaseRequestQuickViewModal) return;

        setText('purchaseRequestQuickViewTitle', 'Đang tải thông tin...');
        setText('purchaseRequestQuickNumber', '—');
        setText('purchaseRequestQuickState', '—');
        document.getElementById('purchaseRequestQuickViewBody').innerHTML = '<div class="text-center text-muted py-5">Đang tải thông tin...</div>';
        document.getElementById('btnOpenPurchaseRequestDetail').href = detailUrlFor(requestId);
        purchaseRequestQuickViewModal.show();

        try {
            const response = await fetch(`${pageRoot.dataset.quickViewUrl}?requestId=${requestId}`, {
                headers: { Accept: 'application/json' }
            });
            if (!response.ok) throw new Error('Không thể tải thông tin yêu cầu.');
            const item = await response.json();
            renderPurchaseRequestQuickView(item);
        } catch (error) {
            document.getElementById('purchaseRequestQuickViewBody').innerHTML = `<div class="gds-empty text-danger">${escapeHtml(error.message || 'Không thể tải thông tin.')}</div>`;
        }
    }

    function renderPurchaseRequestQuickView(item) {
        setText('purchaseRequestQuickViewTitle', item.title || 'Yêu cầu mua hàng');
        setText('purchaseRequestQuickNumber', item.requestNumber || '—');
        const stateElement = document.getElementById('purchaseRequestQuickState');
        if (stateElement) {
            stateElement.className = `gds-status gds-status--dot ${statusClass(item.status)}`;
            stateElement.textContent = item.statusLabel || 'Không rõ';
        }

        const lines = item.lines || [];
        const lineMarkup = lines.length
            ? lines.map(line => quickLineMarkup(line)).join('')
            : '<div class="gds-empty">Yêu cầu chưa có mặt hàng.</div>';
        const body = document.getElementById('purchaseRequestQuickViewBody');
        if (!body) return;
        body.innerHTML = `<div class="pr-index-quick-overview">
            <div><small>Ngày yêu cầu</small><strong>${formatDate(item.requestDate)}</strong></div>
            <div><small>Ngày cần hàng</small><strong>${item.needByDate ? formatDate(item.needByDate) : 'Không giới hạn'}</strong></div>
            <div><small>Người yêu cầu</small><strong>${escapeHtml(item.requestedByName || '—')}</strong></div>
            <div><small>Tiến độ theo mặt hàng</small><strong>${Number(item.eligibleLineCount || 0) > 0 ? `${Number(item.completedLineCount || 0)}/${Number(item.eligibleLineCount || 0)} · ${Number(item.progressPercent || 0)}%` : `${Number(item.lineCount || 0)} mặt hàng · Chưa chốt`}</strong></div>
        </div>
        <div class="pr-index-quick-note"><small>Trạng thái xử lý</small><strong>${escapeHtml(item.statusHint || '')}</strong></div>
        <div class="pr-index-quick-note"><small>Ghi chú</small><div>${escapeHtml(item.note || 'Không có ghi chú')}</div></div>
        <div class="pr-index-quick-lines"><div class="pr-index-quick-lines__heading"><div><small>Chi tiết mặt hàng</small><strong>${Number(item.lineCount || lines.length)} dòng</strong></div><span>Số lượng luôn đi cùng đúng đơn vị</span></div>${lineMarkup}</div>`;
        document.getElementById('btnOpenPurchaseRequestDetail').href = detailUrlFor(Number(item.requestId || 0));
    }

    function quickLineMarkup(line) {
        const image = line.imageUrl
            ? `<button type="button" class="pr-index-line-image-button" data-pr-image="${escapeHtml(line.imageUrl)}" data-pr-image-title="${escapeHtml(line.productName || 'Sản phẩm')}" aria-label="Phóng to ảnh ${escapeHtml(line.productName || 'sản phẩm')}"><img src="${escapeHtml(line.imageUrl)}" alt="${escapeHtml(line.productName || 'Sản phẩm')}" loading="lazy" /></button>`
            : '<span class="pr-index-line-image-placeholder"><i class="bx bx-image"></i></span>';
        const approved = line.approvedQuantity === null || line.approvedQuantity === undefined
            ? 'Chưa chốt'
            : `${formatNumber(line.approvedQuantity)} ${escapeHtml(line.unitName || '')}`;
        return `<div class="pr-index-quick-line">
            ${image}
            <div class="pr-index-line-identity"><strong>${escapeHtml(line.productName || 'Sản phẩm')}</strong><span>${traceability(line)}</span></div>
            <div class="pr-index-line-quantities">
                <span><small>Yêu cầu</small><strong>${formatNumber(line.requestedQuantity)} ${escapeHtml(line.unitName || '')}</strong></span>
                <span><small>Được chốt</small><strong>${approved}</strong></span>
                <span><small>Đã vào đơn</small><strong>${formatNumber(line.convertedQuantity)} ${escapeHtml(line.unitName || '')}</strong></span>
                <span><small>Còn lại</small><strong>${formatNumber(line.remainingQuantity)} ${escapeHtml(line.unitName || '')}</strong></span>
                <span><small>Tồn hiện tại</small><strong>${formatNumber(line.currentStockQuantity)} ${escapeHtml(line.unitName || '')}</strong></span>
                <span><small>Đang về</small><strong>${formatNumber(line.incomingQuantity)} ${escapeHtml(line.unitName || '')}</strong></span>
            </div>
        </div>`;
    }

    function traceability(line) {
        const values = [];
        if (line.sku) values.push(`SKU: ${escapeHtml(line.sku)}`);
        if (line.barcode) values.push(`Barcode: ${escapeHtml(line.barcode)}`);
        return values.length ? values.join(' · ') : 'Không có mã truy vết';
    }

    function showImageHoverPreview(button, event) {
        const src = button.dataset.prImage;
        if (!src) return;
        if (!purchaseRequestIndexImageHoverPreview) {
            purchaseRequestIndexImageHoverPreview = document.createElement('div');
            purchaseRequestIndexImageHoverPreview.className = 'pr-index-image-hover-preview';
            purchaseRequestIndexImageHoverPreview.innerHTML = '<img alt="Ảnh sản phẩm xem nhanh" />';
            document.body.appendChild(purchaseRequestIndexImageHoverPreview);
        }
        purchaseRequestIndexImageHoverPreview.querySelector('img').src = src;
        positionImageHoverPreview(event.clientX, event.clientY);
        purchaseRequestIndexImageHoverPreview.classList.add('is-visible');
    }

    function positionImageHoverPreview(clientX, clientY) {
        if (!purchaseRequestIndexImageHoverPreview) return;
        const size = 276;
        const left = Math.min(window.innerWidth - size - 12, Math.max(12, clientX + 20));
        const top = Math.min(window.innerHeight - size - 12, Math.max(12, clientY - 70));
        purchaseRequestIndexImageHoverPreview.style.left = `${left}px`;
        purchaseRequestIndexImageHoverPreview.style.top = `${top}px`;
    }

    function hideImageHoverPreview() {
        purchaseRequestIndexImageHoverPreview?.classList.remove('is-visible');
    }

    function openImagePreview(src, title) {
        if (!src || !purchaseRequestImagePreviewModal) return;
        hideImageHoverPreview();
        setText('purchaseRequestImagePreviewTitle', title || 'Ảnh sản phẩm');
        const image = document.getElementById('purchaseRequestImagePreviewImage');
        if (image) image.src = src;
        purchaseRequestImagePreviewModal.show();
    }

    function statusBadge(status, label) {
        return `<span class="pr-index-badge ${statusBadgeClass(status)}"><i></i>${escapeHtml(label || 'Không rõ')}</span>`;
    }

    function statusBadgeClass(status) {
        return {
            1: 'is-draft',
            2: 'is-pending',
            3: 'is-returned',
            4: 'is-rejected',
            5: 'is-approved',
            6: 'is-partial',
            7: 'is-converted',
            8: 'is-cancelled'
        }[Number(status)] || 'is-neutral';
    }

    function statusClass(status) {
        const value = Number(status);
        if (value === 5 || value === 7) return 'is-success';
        if (value === 2 || value === 6) return 'is-warning';
        if (value === 3) return 'is-info';
        if (value === 4 || value === 8) return 'is-danger';
        return 'is-neutral';
    }

    function updateScopeUi() {
        root()?.classList.toggle('is-own-scope', state.scope === 'mine');
        document.querySelectorAll('[data-pr-scope]').forEach(button => {
            const active = button.dataset.prScope === state.scope;
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
        const requester = document.getElementById('prRequester');
        if (requester) requester.disabled = state.scope !== 'store';
        const mobileRequesterWrap = document.getElementById('prMobileRequesterWrap');
        mobileRequesterWrap?.classList.toggle('d-none', state.scope !== 'store');
    }

    function updateFilterUi() {
        const keyword = valueOf('prKeyword');
        document.getElementById('prClearSearch')?.classList.toggle('d-none', !keyword);
        const activeCount = countActiveFilters();
        document.getElementById('prResetFilters')?.classList.toggle('d-none', activeCount === 0);
        const badge = document.getElementById('prActiveFilterCount');
        if (badge) {
            badge.textContent = activeCount;
            badge.classList.toggle('d-none', activeCount === 0);
        }
        document.querySelectorAll('.pr-index-kpi[data-pr-state]').forEach(button => {
            const active = button.dataset.prState === state.lifecycle;
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
    }

    function countActiveFilters() {
        let count = 0;
        if (state.scope !== state.initialScope) count++;
        if (valueOf('prKeyword')) count++;
        if (state.scope === 'store' && valueOf('prRequester')) count++;
        if (state.lifecycle !== 'all') count++;
        if (valueOf('prFromDate')) count++;
        if (valueOf('prToDate')) count++;
        if ((valueOf('prPageSize') || '20') !== '20') count++;
        return count;
    }

    function datesAreValid() {
        const from = valueOf('prFromDate');
        const to = valueOf('prToDate');
        const valid = !from || !to || from <= to;
        document.getElementById('prDateError')?.classList.toggle('d-none', valid);
        return valid;
    }

    function resetFilters() {
        state.scope = state.initialScope;
        state.lifecycle = 'all';
        state.page = 1;
        state.pageSize = 20;
        setValue('prKeyword', '');
        setValue('prRequester', '');
        setValue('prStatus', 'all');
        setValue('prFromDate', '');
        setValue('prToDate', '');
        setValue('prPageSize', '20');
        updateScopeUi();
        syncDesktopToMobile();
        updateFilterUi();
        datesAreValid();
        loadPurchaseRequestModernIndex();
    }

    function syncDesktopToMobile() {
        setValue('prMobileScope', state.scope);
        [['prRequester', 'prMobileRequester'], ['prStatus', 'prMobileStatus'], ['prFromDate', 'prMobileFromDate'], ['prToDate', 'prMobileToDate'], ['prPageSize', 'prMobilePageSize']]
            .forEach(([desktopId, mobileId]) => setValue(mobileId, valueOf(desktopId)));
        document.getElementById('prMobileRequesterWrap')?.classList.toggle('d-none', state.scope !== 'store');
    }

    function syncMobileToDesktop() {
        const mobileScope = valueOf('prMobileScope');
        state.scope = mobileScope === 'store' && state.canViewStore ? 'store' : 'mine';
        [['prMobileRequester', 'prRequester'], ['prMobileStatus', 'prStatus'], ['prMobileFromDate', 'prFromDate'], ['prMobileToDate', 'prToDate'], ['prMobilePageSize', 'prPageSize']]
            .forEach(([mobileId, desktopId]) => setValue(desktopId, valueOf(mobileId)));
        if (state.scope === 'mine') setValue('prRequester', '');
        state.lifecycle = valueOf('prStatus') || 'all';
        state.pageSize = Number(valueOf('prPageSize') || 20);
    }

    function setLoading() {
        document.getElementById('purchaseRequestResultsPanel')?.setAttribute('aria-busy', 'true');
        const body = document.getElementById('prTableBody');
        const mobile = document.getElementById('purchaseRequestMobileList');
        if (body) body.innerHTML = '<tr><td colspan="6" class="gds-empty">Đang tải dữ liệu...</td></tr>';
        if (mobile) mobile.innerHTML = '<div class="gds-empty">Đang tải dữ liệu...</div>';
    }

    function renderError(message) {
        document.getElementById('purchaseRequestResultsPanel')?.setAttribute('aria-busy', 'false');
        const safe = escapeHtml(message);
        const body = document.getElementById('prTableBody');
        const mobile = document.getElementById('purchaseRequestMobileList');
        if (body) body.innerHTML = `<tr><td colspan="6" class="gds-empty text-danger">${safe}</td></tr>`;
        if (mobile) mobile.innerHTML = `<div class="gds-empty text-danger">${safe}</div>`;
    }

    function detailUrlFor(requestId) {
        const base = (root()?.dataset.detailBaseUrl || '/admin/purchase-requests').replace(/\/$/, '');
        return `${base}/${Number(requestId || 0)}`;
    }

    function valueOf(id) {
        return document.getElementById(id)?.value?.trim() || '';
    }

    function setValue(id, value) {
        const element = document.getElementById(id);
        if (element) element.value = value ?? '';
    }

    function setText(id, value) {
        const element = document.getElementById(id);
        if (element) element.textContent = value ?? 0;
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
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }
})();
