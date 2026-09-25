(function () {
    'use strict';

    let purchaseOrderIndexAbortController = null;
    let purchaseOrderIndexRequestSequence = 0;
    let purchaseOrderIndexSearchTimer = null;
    let purchaseOrderQuickViewModal = null;
    let purchaseOrderImagePreviewModal = null;
    let purchaseOrderIndexImageHoverPreview = null;

    const state = {
        items: [],
        page: 1,
        pageSize: 20,
        totalItems: 0,
        totalPages: 1,
        lifecycle: 'open',
        canViewCost: false,
        warehouses: []
    };

    document.addEventListener('DOMContentLoaded', initPurchaseOrderModernIndex);

    function root() {
        return document.querySelector('[data-purchase-order-index]');
    }

    async function initPurchaseOrderModernIndex() {
        const pageRoot = root();
        if (!pageRoot) return;

        state.canViewCost = pageRoot.dataset.canViewCost === 'true';
        const quickModal = document.getElementById('purchaseOrderQuickViewModal');
        const imageModal = document.getElementById('purchaseOrderImagePreviewModal');
        if (quickModal) purchaseOrderQuickViewModal = new bootstrap.Modal(quickModal);
        if (imageModal) purchaseOrderImagePreviewModal = new bootstrap.Modal(imageModal);

        bindEvents();
        updateFilterUi();
        await loadFilterOptions();
        await loadPurchaseOrderModernIndex();
    }

    function bindEvents() {
        document.getElementById('btnPoReload')?.addEventListener('click', function () {
            state.page = 1;
            loadPurchaseOrderModernIndex();
        });

        const keyword = document.getElementById('poKeyword');
        keyword?.addEventListener('input', function () {
            updateFilterUi();
            clearTimeout(purchaseOrderIndexSearchTimer);
            purchaseOrderIndexSearchTimer = setTimeout(function () {
                state.page = 1;
                loadPurchaseOrderModernIndex();
            }, 350);
        });

        document.getElementById('poClearSearch')?.addEventListener('click', function () {
            if (keyword) keyword.value = '';
            state.page = 1;
            updateFilterUi();
            loadPurchaseOrderModernIndex();
            keyword?.focus();
        });

        ['poLegalEntity', 'poSupplier', 'poWarehouse', 'poSource', 'poState',
            'poFromDate', 'poToDate', 'poPageSize']
            .forEach(id => document.getElementById(id)?.addEventListener('change', function () {
                if (id === 'poLegalEntity') filterWarehouseOptions('poWarehouse', this.value);
                if (id === 'poState') state.lifecycle = this.value || 'open';
                if (id === 'poPageSize') state.pageSize = Number(this.value || 20);
                state.page = 1;
                updateFilterUi();
                if (datesAreValid()) loadPurchaseOrderModernIndex();
            }));

        document.getElementById('poResetFilters')?.addEventListener('click', resetFilters);

        document.querySelectorAll('.po-index-kpi[data-po-state]').forEach(button => {
            button.addEventListener('click', function () {
                const requested = this.dataset.poState || 'open';
                state.lifecycle = state.lifecycle === requested ? 'open' : requested;
                setValue('poState', state.lifecycle);
                state.page = 1;
                syncDesktopToMobile();
                updateFilterUi();
                loadPurchaseOrderModernIndex();
            });
        });

        document.getElementById('poPageNumbers')?.addEventListener('click', function (event) {
            const button = event.target.closest('[data-page]');
            if (!button || button.disabled) return;
            const page = Number(button.dataset.page || 0);
            if (page < 1 || page > state.totalPages || page === state.page) return;
            state.page = page;
            loadPurchaseOrderModernIndex();
        });

        const results = document.getElementById('purchaseOrderResultsPanel');
        results?.addEventListener('click', function (event) {
            const quickButton = event.target.closest('[data-po-quick]');
            if (!quickButton) return;
            event.preventDefault();
            event.stopPropagation();
            openPurchaseOrderQuickView(Number(quickButton.dataset.poQuick || 0));
        });

        results?.addEventListener('dblclick', function (event) {
            if (event.target.closest('a,button,input,select')) return;
            const row = event.target.closest('[data-po-order-id]');
            if (row) openPurchaseOrderQuickView(Number(row.dataset.poOrderId || 0));
        });

        results?.addEventListener('keydown', function (event) {
            if (event.key !== 'Enter' || event.target.closest('a,button,input,select')) return;
            const row = event.target.closest('[data-po-order-id]');
            if (row) openPurchaseOrderQuickView(Number(row.dataset.poOrderId || 0));
        });

        document.getElementById('purchaseOrderMobileFilterSheet')
            ?.addEventListener('show.bs.offcanvas', syncDesktopToMobile);

        document.getElementById('poMobileLegalEntity')?.addEventListener('change', function () {
            filterWarehouseOptions('poMobileWarehouse', this.value);
        });

        document.getElementById('poMobileApplyFilters')?.addEventListener('click', function () {
            syncMobileToDesktop();
            if (!datesAreValid()) return;
            bootstrap.Offcanvas.getInstance(document.getElementById('purchaseOrderMobileFilterSheet'))?.hide();
            state.page = 1;
            updateFilterUi();
            loadPurchaseOrderModernIndex();
        });

        document.getElementById('poMobileClearFilters')?.addEventListener('click', function () {
            resetFilters();
            syncDesktopToMobile();
        });

        const quickBody = document.getElementById('purchaseOrderQuickViewBody');
        quickBody?.addEventListener('click', function (event) {
            const imageButton = event.target.closest('[data-po-image]');
            if (!imageButton) return;
            openImagePreview(imageButton.dataset.poImage, imageButton.dataset.poImageTitle);
        });

        quickBody?.addEventListener('pointerenter', function (event) {
            const imageButton = event.target.closest?.('[data-po-image]');
            if (imageButton && event.pointerType !== 'touch') showImageHoverPreview(imageButton, event);
        }, true);

        quickBody?.addEventListener('pointerleave', function (event) {
            if (event.target.closest?.('[data-po-image]')) hideImageHoverPreview();
        }, true);
    }

    async function loadFilterOptions() {
        const pageRoot = root();
        if (!pageRoot) return;

        try {
            const response = await fetch(pageRoot.dataset.filterOptionsUrl, {
                headers: { Accept: 'application/json' }
            });
            if (!response.ok) return;
            const payload = await response.json();
            state.warehouses = payload.warehouses || [];
            setSelectOptions(
                ['poLegalEntity', 'poMobileLegalEntity'],
                payload.legalEntities || [],
                'Tất cả HKD');
            setSelectOptions(
                ['poSupplier', 'poMobileSupplier'],
                payload.suppliers || [],
                'Tất cả nhà cung cấp');
            filterWarehouseOptions('poWarehouse', '');
            filterWarehouseOptions('poMobileWarehouse', '');
        } catch { }
    }

    function setSelectOptions(ids, items, emptyLabel) {
        const markup = `<option value="">${escapeHtml(emptyLabel)}</option>`
            + items.map(item => `<option value="${Number(item.id)}">${escapeHtml(optionLabel(item))}</option>`).join('');
        ids.forEach(id => {
            const select = document.getElementById(id);
            if (select) select.innerHTML = markup;
        });
    }

    function filterWarehouseOptions(id, legalEntityId) {
        const select = document.getElementById(id);
        if (!select) return;
        const previous = select.value;
        const entityId = Number(legalEntityId || 0);
        const available = state.warehouses.filter(item =>
            entityId <= 0 || Number(item.legalEntityId || 0) === entityId);
        select.innerHTML = '<option value="">Tất cả kho nhận</option>'
            + available.map(item => `<option value="${Number(item.id)}">${escapeHtml(optionLabel(item))}</option>`).join('');
        select.value = available.some(item => String(item.id) === previous) ? previous : '';
    }

    function optionLabel(item) {
        return item.code ? `${item.code} · ${item.name || ''}` : (item.name || '');
    }

    function buildQuery() {
        const params = new URLSearchParams();
        const values = {
            keyword: valueOf('poKeyword'),
            legalEntityId: valueOf('poLegalEntity'),
            supplierId: valueOf('poSupplier'),
            warehouseId: valueOf('poWarehouse'),
            source: valueOf('poSource') || 'all',
            state: state.lifecycle,
            fromDate: valueOf('poFromDate'),
            toDate: valueOf('poToDate'),
            page: state.page,
            pageSize: valueOf('poPageSize') || state.pageSize
        };
        Object.entries(values).forEach(([key, value]) => {
            if (value !== '' && value !== null && value !== undefined) params.set(key, value);
        });
        return params.toString();
    }

    async function loadPurchaseOrderModernIndex() {
        const pageRoot = root();
        if (!pageRoot || !datesAreValid()) return;

        if (purchaseOrderIndexAbortController) purchaseOrderIndexAbortController.abort();
        purchaseOrderIndexAbortController = new AbortController();
        const sequence = ++purchaseOrderIndexRequestSequence;
        setLoading();

        try {
            const response = await fetch(`${pageRoot.dataset.dataUrl}?${buildQuery()}`, {
                signal: purchaseOrderIndexAbortController.signal,
                headers: { Accept: 'application/json' }
            });
            if (!response.ok) throw new Error('Không thể tải danh sách đơn đặt hàng.');
            const payload = await response.json();
            if (sequence !== purchaseOrderIndexRequestSequence) return;

            state.items = payload.items || [];
            state.page = Number(payload.page || 1);
            state.pageSize = Number(payload.pageSize || 20);
            state.totalItems = Number(payload.totalItems || 0);
            state.totalPages = Math.max(1, Number(payload.totalPages || 1));
            state.canViewCost = payload.canViewCost === true;

            renderSummary(payload.summary || {});
            renderPurchaseOrderDesktopRows(state.items);
            renderPurchaseOrderMobileCards(state.items);
            renderPurchaseOrderCircularPagination();
            updateFilterUi();
            document.getElementById('purchaseOrderResultsPanel')?.setAttribute('aria-busy', 'false');
        } catch (error) {
            if (error.name === 'AbortError') return;
            renderError(error.message || 'Không thể tải dữ liệu.');
        }
    }

    function renderSummary(summary) {
        setText('poStatTotal', summary.totalItems);
        setText('poStatNeedsAction', summary.needsActionItems);
        setText('poStatInProgress', summary.inProgressItems);
        setText('poStatCompleted', summary.completedItems);
    }

    function renderPurchaseOrderDesktopRows(items) {
        const body = document.getElementById('poTableBody');
        if (!body) return;
        if (!items.length) {
            body.innerHTML = '<tr><td colspan="7" class="gds-empty"><i class="bx bx-purchase-tag-alt"></i><strong>Không có đơn phù hợp</strong><span>Hãy thay đổi từ khóa hoặc bộ lọc.</span></td></tr>';
            return;
        }

        body.innerHTML = items.map(item => {
            const id = Number(item.orderId || 0);
            const source = sourceMarkup(item);
            const cost = state.canViewCost && item.totalAfterVat !== null && item.totalAfterVat !== undefined
                ? `<div class="po-index-cost">${formatCurrency(item.totalAfterVat)}</div>`
                : '';
            return `<tr class="po-index-row" tabindex="0" data-po-order-id="${id}">
                <td><div class="po-index-title">${escapeHtml(item.title || item.orderNumber || 'Chưa đặt tên')}</div><div class="po-index-muted"><span class="gds-code">${escapeHtml(item.orderNumber || '')}</span><span>${formatDate(item.orderDate)}</span></div>${source}</td>
                <td><div class="po-index-supplier"><strong>${escapeHtml(item.supplierName || '—')}</strong>${item.supplierCode ? `<small>${escapeHtml(item.supplierCode)}</small>` : ''}${cost}</div></td>
                <td><div class="po-index-destination"><strong>${escapeHtml(item.legalEntityName || '—')}</strong><small>${escapeHtml(item.warehouseName || '—')}</small></div></td>
                <td>${deliveryMarkup(item)}</td>
                <td>${progressMarkup(item)}</td>
                <td>${statusBadge(item.status, item.statusLabel)}<div class="po-index-status-hint">${escapeHtml(item.statusHint || '')}</div></td>
                <td class="text-end"><div class="po-index-actions"><button type="button" class="gds-icon-button" data-po-quick="${id}" title="Xem nhanh" aria-label="Xem nhanh ${escapeHtml(item.orderNumber || '')}"><i class="bx bx-show"></i></button><a class="btn btn-sm btn-primary" href="${detailUrlFor(id)}">Mở</a></div></td>
            </tr>`;
        }).join('');
    }

    function renderPurchaseOrderMobileCards(items) {
        const list = document.getElementById('purchaseOrderMobileList');
        if (!list) return;
        if (!items.length) {
            list.innerHTML = '<div class="gds-empty"><i class="bx bx-purchase-tag-alt"></i><strong>Không có đơn phù hợp</strong><span>Hãy thay đổi từ khóa hoặc bộ lọc.</span></div>';
            return;
        }

        list.innerHTML = items.map(item => {
            const id = Number(item.orderId || 0);
            const cost = state.canViewCost && item.totalAfterVat !== null && item.totalAfterVat !== undefined
                ? `<span><small>Giá trị đơn</small><strong class="text-primary">${formatCurrency(item.totalAfterVat)}</strong></span>`
                : '';
            return `<article class="po-index-mobile-card" tabindex="0" data-po-order-id="${id}">
                <div class="po-index-mobile-card__top"><div class="min-w-0"><div class="po-index-mobile-title">${escapeHtml(item.title || item.orderNumber || 'Chưa đặt tên')}</div><div class="po-index-muted"><span class="gds-code">${escapeHtml(item.orderNumber || '')}</span><span>${formatDate(item.orderDate)}</span></div></div>${statusBadge(item.status, item.statusLabel)}</div>
                ${sourceMarkup(item)}
                <div class="po-index-mobile-facts"><span><small>Nhà cung cấp</small><strong>${escapeHtml(item.supplierName || '—')}</strong></span><span><small>Kho nhận</small><strong>${escapeHtml(item.warehouseName || '—')}</strong></span><span><small>Ngày dự kiến</small><strong>${item.expectedDeliveryDate ? formatDate(item.expectedDeliveryDate) : 'Chưa đặt'}</strong></span>${cost}</div>
                ${progressMarkup(item)}
                <div class="po-index-status-hint">${escapeHtml(item.statusHint || '')}</div>
                <div class="po-index-mobile-actions"><button type="button" class="btn btn-label-primary flex-grow-1" data-po-quick="${id}"><i class="bx bx-show"></i>Xem nhanh</button><a class="btn btn-primary" href="${detailUrlFor(id)}">Mở</a></div>
            </article>`;
        }).join('');
    }

    function sourceMarkup(item) {
        if (item.sourcePurchaseRequestId) {
            return `<span class="po-index-source is-request"><i class="bx bx-link-alt"></i>Từ yêu cầu ${escapeHtml(item.sourcePurchaseRequestNumber || '')}</span>`;
        }
        return '<span class="po-index-source is-direct"><i class="bx bx-git-branch"></i>Đơn trực tiếp</span>';
    }

    function deliveryMarkup(item) {
        if (!item.expectedDeliveryDate) return '<div class="po-index-delivery"><strong>Chưa đặt</strong><small>Không có ngày dự kiến</small></div>';
        const overdue = isOverdue(item.expectedDeliveryDate, item.status);
        return `<div class="po-index-delivery ${overdue ? 'is-overdue' : ''}"><strong>${formatDate(item.expectedDeliveryDate)}</strong>${overdue ? '<span class="po-index-overdue"><i class="bx bx-error"></i>Quá ngày dự kiến</span>' : '<small>Theo kế hoạch đơn</small>'}</div>`;
    }

    function progressMarkup(item) {
        const lineCount = Number(item.lineCount || 0);
        const eligibleLineCount = Number(item.eligibleLineCount || 0);
        const resolvedLineCount = Number(item.resolvedLineCount || 0);
        const receivingLineCount = Number(item.receivingLineCount || 0);
        if (eligibleLineCount <= 0) {
            return `<div class="po-index-progress"><div class="po-index-progress__label"><strong>${lineCount} dòng hàng</strong><span>Chưa có tiến độ</span></div><div class="progress"><div class="progress-bar is-neutral" style="width:0%"></div></div></div>`;
        }
        const percent = Math.min(100, Math.max(0, Number(item.progressPercent || 0)));
        const metadata = [receivingLineCount > 0 ? `${receivingLineCount} dòng đang nhận` : '', item.hasShortClosedLine ? 'Có đóng thiếu' : ''].filter(Boolean).join(' · ');
        return `<div class="po-index-progress"><div class="po-index-progress__label"><strong>${resolvedLineCount}/${eligibleLineCount} dòng đã xử lý</strong><span>${percent}%</span></div><div class="progress"><div class="progress-bar" style="width:${percent}%"></div></div>${metadata ? `<div class="po-index-progress__meta">${escapeHtml(metadata)}</div>` : ''}</div>`;
    }

    function renderPurchaseOrderCircularPagination() {
        const list = document.getElementById('poPageNumbers');
        const info = document.getElementById('poPagingInfo');
        if (info) {
            const first = state.totalItems === 0 ? 0 : (state.page - 1) * state.pageSize + 1;
            const last = Math.min(state.totalItems, state.page * state.pageSize);
            info.textContent = state.totalItems === 0
                ? '0 kết quả'
                : `${first}–${last} / ${state.totalItems} đơn`;
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

    async function openPurchaseOrderQuickView(orderId) {
        const pageRoot = root();
        if (!pageRoot || orderId <= 0 || !purchaseOrderQuickViewModal) return;

        setText('purchaseOrderQuickViewTitle', 'Đang tải thông tin...');
        setText('purchaseOrderQuickNumber', '—');
        setText('purchaseOrderQuickState', '—');
        document.getElementById('purchaseOrderQuickViewBody').innerHTML = '<div class="text-center text-muted py-5">Đang tải thông tin...</div>';
        document.getElementById('btnOpenPurchaseOrderDetail').href = detailUrlFor(orderId);
        purchaseOrderQuickViewModal.show();

        try {
            const response = await fetch(`${pageRoot.dataset.quickViewUrl}?orderId=${orderId}`, {
                headers: { Accept: 'application/json' }
            });
            if (!response.ok) throw new Error('Không thể tải thông tin đơn đặt hàng.');
            renderPurchaseOrderQuickView(await response.json());
        } catch (error) {
            document.getElementById('purchaseOrderQuickViewBody').innerHTML = `<div class="gds-empty text-danger">${escapeHtml(error.message || 'Không thể tải thông tin.')}</div>`;
        }
    }

    function renderPurchaseOrderQuickView(item) {
        setText('purchaseOrderQuickViewTitle', item.title || 'Đơn đặt hàng');
        setText('purchaseOrderQuickNumber', item.orderNumber || '—');
        const stateElement = document.getElementById('purchaseOrderQuickState');
        if (stateElement) {
            stateElement.className = `gds-status gds-status--dot ${statusClass(item.status)}`;
            stateElement.textContent = item.statusLabel || 'Không rõ';
        }

        const lines = item.lines || [];
        const lineMarkup = lines.length
            ? lines.map(line => quickLineMarkup(line, item.canViewCost === true)).join('')
            : '<div class="gds-empty">Đơn chưa có dòng hàng.</div>';
        const costMarkup = item.canViewCost === true
            ? `<div class="po-index-quick-costs"><div><small>Trước VAT</small><strong>${formatCurrency(item.subtotalBeforeVat)}</strong></div><div><small>VAT</small><strong>${formatCurrency(item.vatTotal)}</strong></div><div><small>Giá trị đơn</small><strong class="text-primary">${formatCurrency(item.totalAfterVat)}</strong></div></div>`
            : '';
        const source = item.sourcePurchaseRequestNumber
            ? `Từ yêu cầu ${escapeHtml(item.sourcePurchaseRequestNumber)}`
            : 'Đơn trực tiếp';
        const progress = Number(item.eligibleLineCount || 0) > 0
            ? `${Number(item.resolvedLineCount || 0)}/${Number(item.eligibleLineCount || 0)} dòng · ${Number(item.progressPercent || 0)}%`
            : `${Number(item.lineCount || 0)} dòng · Chưa có tiến độ`;
        const notes = [item.note, item.outsideRequestReason].filter(Boolean).join('\n');
        const body = document.getElementById('purchaseOrderQuickViewBody');
        if (!body) return;
        body.innerHTML = `<div class="po-index-quick-overview">
            <div><small>Ngày đặt</small><strong>${formatDate(item.orderDate)}</strong></div>
            <div><small>Ngày dự kiến</small><strong>${item.expectedDeliveryDate ? formatDate(item.expectedDeliveryDate) : 'Chưa đặt'}</strong></div>
            <div><small>Nhà cung cấp</small><strong>${escapeHtml(item.supplierName || '—')}</strong></div>
            <div><small>Tiến độ dòng hàng</small><strong>${progress}</strong></div>
            <div><small>Đơn vị mua hàng</small><strong>${escapeHtml(item.legalEntityName || '—')}</strong></div>
            <div><small>Kho nhận</small><strong>${escapeHtml(item.warehouseName || '—')}</strong></div>
            <div><small>Nguồn đơn</small><strong>${source}</strong></div>
            <div><small>Trạng thái xử lý</small><strong>${escapeHtml(item.statusHint || '')}</strong></div>
        </div>${costMarkup}
        <div class="po-index-quick-note"><small>Ghi chú</small><div>${escapeHtml(notes || 'Không có ghi chú')}</div></div>
        <div class="po-index-quick-lines"><div class="po-index-quick-lines__heading"><div><small>Chi tiết mặt hàng</small><strong>${Number(item.lineCount || lines.length)} dòng</strong></div><span>Mỗi số lượng luôn đi cùng đúng đơn vị</span></div>${lineMarkup}</div>`;
        document.getElementById('btnOpenPurchaseOrderDetail').href = detailUrlFor(Number(item.orderId || 0));
    }

    function quickLineMarkup(line, canViewCost) {
        const image = line.imageUrl
            ? `<button type="button" class="po-index-line-image-button" data-po-image="${escapeHtml(line.imageUrl)}" data-po-image-title="${escapeHtml(line.productName || 'Sản phẩm')}" aria-label="Phóng to ảnh ${escapeHtml(line.productName || 'sản phẩm')}"><img src="${escapeHtml(line.imageUrl)}" alt="${escapeHtml(line.productName || 'Sản phẩm')}" loading="lazy" /></button>`
            : '<span class="po-index-line-image-placeholder"><i class="bx bx-image"></i></span>';
        const unit = escapeHtml(line.unitName || '');
        const cost = canViewCost
            ? `<div class="po-index-line-costs"><span><small>Đơn giá trước VAT</small><strong>${formatCurrency(line.unitPriceBeforeVat)}</strong></span><span><small>Thuế suất</small><strong>${formatNumber(line.taxRate)}%</strong></span><span><small>Thành tiền</small><strong>${formatCurrency(line.lineTotalAfterVat)}</strong></span></div>`
            : '';
        return `<div class="po-index-quick-line">
            ${image}
            <div class="po-index-line-identity"><strong>${escapeHtml(line.productName || 'Sản phẩm')}</strong><span>${traceability(line)}</span><span>${line.isResolved ? 'Đã xử lý đủ dòng' : 'Còn số lượng chờ xử lý'}</span></div>
            <div class="po-index-line-quantities"><span><small>Đặt hàng</small><strong>${formatNumber(line.orderedQuantity)} ${unit}</strong></span><span><small>Đã nhận</small><strong>${formatNumber(line.receivedQuantity)} ${unit}</strong></span><span><small>Đóng thiếu</small><strong>${formatNumber(line.shortClosedQuantity)} ${unit}</strong></span><span><small>Còn lại</small><strong>${formatNumber(line.remainingQuantity)} ${unit}</strong></span></div>${cost}
        </div>`;
    }

    function traceability(line) {
        const values = [];
        if (line.sku) values.push(`SKU: ${escapeHtml(line.sku)}`);
        if (line.barcode) values.push(`Barcode: ${escapeHtml(line.barcode)}`);
        return values.length ? values.join(' · ') : 'Không có mã truy vết';
    }

    function showImageHoverPreview(button, event) {
        const src = button.dataset.poImage;
        if (!src) return;
        if (!purchaseOrderIndexImageHoverPreview) {
            purchaseOrderIndexImageHoverPreview = document.createElement('div');
            purchaseOrderIndexImageHoverPreview.className = 'po-index-image-hover-preview';
            purchaseOrderIndexImageHoverPreview.innerHTML = '<img alt="Ảnh sản phẩm xem nhanh" />';
            document.body.appendChild(purchaseOrderIndexImageHoverPreview);
        }
        purchaseOrderIndexImageHoverPreview.querySelector('img').src = src;
        const size = 276;
        purchaseOrderIndexImageHoverPreview.style.left = `${Math.min(window.innerWidth - size - 12, Math.max(12, event.clientX + 20))}px`;
        purchaseOrderIndexImageHoverPreview.style.top = `${Math.min(window.innerHeight - size - 12, Math.max(12, event.clientY - 70))}px`;
        purchaseOrderIndexImageHoverPreview.classList.add('is-visible');
    }

    function hideImageHoverPreview() {
        purchaseOrderIndexImageHoverPreview?.classList.remove('is-visible');
    }

    function openImagePreview(src, title) {
        if (!src || !purchaseOrderImagePreviewModal) return;
        hideImageHoverPreview();
        setText('purchaseOrderImagePreviewTitle', title || 'Ảnh sản phẩm');
        const image = document.getElementById('purchaseOrderImagePreviewImage');
        if (image) image.src = src;
        purchaseOrderImagePreviewModal.show();
    }

    function statusBadge(status, label) {
        return `<span class="po-index-badge ${statusBadgeClass(status)}"><i></i>${escapeHtml(label || 'Không rõ')}</span>`;
    }

    function statusBadgeClass(status) {
        return {
            1: 'is-draft', 2: 'is-pending', 3: 'is-returned', 4: 'is-rejected',
            5: 'is-approved', 6: 'is-sent', 7: 'is-receiving', 8: 'is-fully-received',
            9: 'is-short-closed', 10: 'is-cancelled'
        }[Number(status)] || 'is-neutral';
    }

    function statusClass(status) {
        const value = Number(status);
        if (value === 5 || value === 8) return 'is-success';
        if (value === 2 || value === 9) return 'is-warning';
        if (value === 3 || value === 6 || value === 7) return 'is-info';
        if (value === 4 || value === 10) return 'is-danger';
        return 'is-neutral';
    }

    function updateFilterUi() {
        document.getElementById('poClearSearch')?.classList.toggle('d-none', !valueOf('poKeyword'));
        const activeCount = countActiveFilters();
        document.getElementById('poResetFilters')?.classList.toggle('d-none', activeCount === 0);
        const badge = document.getElementById('poActiveFilterCount');
        if (badge) {
            badge.textContent = activeCount;
            badge.classList.toggle('d-none', activeCount === 0);
        }
        document.querySelectorAll('.po-index-kpi[data-po-state]').forEach(button => {
            const active = button.dataset.poState === state.lifecycle;
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
    }

    function countActiveFilters() {
        let count = 0;
        if (valueOf('poKeyword')) count++;
        if (valueOf('poLegalEntity')) count++;
        if (valueOf('poSupplier')) count++;
        if (valueOf('poWarehouse')) count++;
        if ((valueOf('poSource') || 'all') !== 'all') count++;
        if (state.lifecycle !== 'open') count++;
        if (valueOf('poFromDate')) count++;
        if (valueOf('poToDate')) count++;
        if ((valueOf('poPageSize') || '20') !== '20') count++;
        return count;
    }

    function datesAreValid() {
        const from = valueOf('poFromDate');
        const to = valueOf('poToDate');
        const valid = !from || !to || from <= to;
        document.getElementById('poDateError')?.classList.toggle('d-none', valid);
        return valid;
    }

    function resetFilters() {
        state.lifecycle = 'open';
        state.page = 1;
        state.pageSize = 20;
        setValue('poKeyword', '');
        setValue('poLegalEntity', '');
        setValue('poSupplier', '');
        filterWarehouseOptions('poWarehouse', '');
        setValue('poSource', 'all');
        setValue('poState', 'open');
        setValue('poFromDate', '');
        setValue('poToDate', '');
        setValue('poPageSize', '20');
        syncDesktopToMobile();
        updateFilterUi();
        datesAreValid();
        loadPurchaseOrderModernIndex();
    }

    function syncDesktopToMobile() {
        setValue('poMobileLegalEntity', valueOf('poLegalEntity'));
        filterWarehouseOptions('poMobileWarehouse', valueOf('poLegalEntity'));
        [['poSupplier', 'poMobileSupplier'], ['poWarehouse', 'poMobileWarehouse'],
            ['poSource', 'poMobileSource'], ['poState', 'poMobileState'],
            ['poFromDate', 'poMobileFromDate'], ['poToDate', 'poMobileToDate'],
            ['poPageSize', 'poMobilePageSize']]
            .forEach(([desktopId, mobileId]) => setValue(mobileId, valueOf(desktopId)));
    }

    function syncMobileToDesktop() {
        setValue('poLegalEntity', valueOf('poMobileLegalEntity'));
        filterWarehouseOptions('poWarehouse', valueOf('poMobileLegalEntity'));
        [['poMobileSupplier', 'poSupplier'], ['poMobileWarehouse', 'poWarehouse'],
            ['poMobileSource', 'poSource'], ['poMobileState', 'poState'],
            ['poMobileFromDate', 'poFromDate'], ['poMobileToDate', 'poToDate'],
            ['poMobilePageSize', 'poPageSize']]
            .forEach(([mobileId, desktopId]) => setValue(desktopId, valueOf(mobileId)));
        state.lifecycle = valueOf('poState') || 'open';
        state.pageSize = Number(valueOf('poPageSize') || 20);
    }

    function setLoading() {
        document.getElementById('purchaseOrderResultsPanel')?.setAttribute('aria-busy', 'true');
        const body = document.getElementById('poTableBody');
        const mobile = document.getElementById('purchaseOrderMobileList');
        if (body) body.innerHTML = '<tr><td colspan="7" class="gds-empty">Đang tải dữ liệu...</td></tr>';
        if (mobile) mobile.innerHTML = '<div class="gds-empty">Đang tải dữ liệu...</div>';
    }

    function renderError(message) {
        document.getElementById('purchaseOrderResultsPanel')?.setAttribute('aria-busy', 'false');
        const safe = escapeHtml(message);
        const body = document.getElementById('poTableBody');
        const mobile = document.getElementById('purchaseOrderMobileList');
        if (body) body.innerHTML = `<tr><td colspan="7" class="gds-empty text-danger">${safe}</td></tr>`;
        if (mobile) mobile.innerHTML = `<div class="gds-empty text-danger">${safe}</div>`;
    }

    function detailUrlFor(orderId) {
        const base = (root()?.dataset.detailBaseUrl || '/admin/purchase-orders').replace(/\/$/, '');
        return `${base}/${Number(orderId || 0)}`;
    }

    function isOverdue(value, status) {
        if (![1, 2, 3, 5, 6, 7].includes(Number(status))) return false;
        const isoDate = String(value || '').slice(0, 10);
        if (!/^\d{4}-\d{2}-\d{2}$/.test(isoDate)) return false;
        const now = new Date();
        const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
        return isoDate < today;
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
        const match = String(value).match(/^(\d{4})-(\d{2})-(\d{2})/);
        if (match) return `${match[3]}/${match[2]}/${match[1]}`;
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? '—' : date.toLocaleDateString('vi-VN');
    }

    function formatNumber(value) {
        return Number(value || 0).toLocaleString('vi-VN', { maximumFractionDigits: 3 });
    }

    function formatCurrency(value) {
        return `${Number(value || 0).toLocaleString('vi-VN', { maximumFractionDigits: 0 })} ₫`;
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
