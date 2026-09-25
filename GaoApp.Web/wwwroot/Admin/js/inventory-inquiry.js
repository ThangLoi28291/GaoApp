(function () {
    "use strict";

    const root = document.querySelector("[data-inventory-inquiry-index]");
    if (!root) return;

    const endpoints = {
        data: root.dataset.dataUrl || "/admin/inventory-inquiry/data",
        quickView: root.dataset.quickViewUrl || "/admin/inventory-inquiry/quick-view",
        warehouses: root.dataset.warehouseUrl || "/admin/api/warehouses/select2?term="
    };

    const state = {
        page: 1,
        pageSize: 20,
        totalItems: 0,
        totalPages: 1,
        canViewCost: false,
        items: []
    };

    const elements = {
        keyword: document.getElementById("inventoryKeyword"),
        clearSearch: document.getElementById("inventoryClearSearch"),
        warehouse: document.getElementById("inventoryWarehouseFilter"),
        inventoryState: document.getElementById("inventoryStateFilter"),
        sortBy: document.getElementById("inventorySortBy"),
        sortDirection: document.getElementById("inventorySortDirection"),
        pageSize: document.getElementById("inventoryPageSize"),
        reset: document.getElementById("inventoryResetFilters"),
        reload: document.getElementById("inventoryReloadButton"),
        resultsPanel: document.getElementById("inventoryResultsPanel"),
        desktopBody: document.getElementById("inventoryDesktopBody"),
        mobileList: document.getElementById("inventoryMobileList"),
        pagination: document.getElementById("inventoryPagination"),
        resultSummary: document.getElementById("inventoryResultSummary"),
        totalCount: document.getElementById("inventoryTotalCount"),
        negativeCount: document.getElementById("inventoryNegativeCount"),
        positiveCount: document.getElementById("inventoryPositiveCount"),
        zeroCount: document.getElementById("inventoryZeroCount"),
        activeFilterCount: document.getElementById("inventoryActiveFilterCount"),
        mobileWarehouse: document.getElementById("inventoryMobileWarehouse"),
        mobileState: document.getElementById("inventoryMobileState"),
        mobileSortBy: document.getElementById("inventoryMobileSortBy"),
        mobileSortDirection: document.getElementById("inventoryMobileSortDirection"),
        mobilePageSize: document.getElementById("inventoryMobilePageSize"),
        mobileClear: document.getElementById("inventoryMobileClearFilters"),
        mobileApply: document.getElementById("inventoryMobileApplyFilters"),
        mobileFilterSheet: document.getElementById("inventoryMobileFilterSheet"),
        quickViewModal: document.getElementById("inventoryQuickViewModal"),
        quickViewTitle: document.getElementById("inventoryQuickViewTitle"),
        quickWarehouse: document.getElementById("inventoryQuickWarehouse"),
        quickState: document.getElementById("inventoryQuickState"),
        quickBody: document.getElementById("inventoryQuickBody"),
        imagePreviewModal: document.getElementById("inventoryImagePreviewModal"),
        imagePreviewTitle: document.getElementById("inventoryImagePreviewTitle"),
        imagePreviewImage: document.getElementById("inventoryImagePreviewImage")
    };

    let requestSequence = 0;
    let pageAbortController = null;
    let quickViewAbortController = null;
    let searchDebounceTimer = null;
    let inventoryImageHoverPreview = null;

    document.addEventListener("DOMContentLoaded", initialize);

    async function initialize() {
        bindStaticEvents();
        await loadWarehouses();
        await loadInventoryPage();
    }

    function bindStaticEvents() {
        elements.keyword?.addEventListener("input", function () {
            updateClearSearchVisibility();
            window.clearTimeout(searchDebounceTimer);
            searchDebounceTimer = window.setTimeout(function () {
                state.page = 1;
                loadInventoryPage();
            }, 350);
        });

        elements.keyword?.addEventListener("keydown", function (event) {
            if (event.key !== "Enter") return;
            event.preventDefault();
            window.clearTimeout(searchDebounceTimer);
            state.page = 1;
            loadInventoryPage();
        });

        elements.clearSearch?.addEventListener("click", function () {
            elements.keyword.value = "";
            updateClearSearchVisibility();
            state.page = 1;
            loadInventoryPage();
            elements.keyword.focus();
        });

        [
            elements.warehouse,
            elements.inventoryState,
            elements.sortBy,
            elements.sortDirection,
            elements.pageSize
        ].forEach(function (control) {
            control?.addEventListener("change", function () {
                state.page = 1;
                state.pageSize = Number(elements.pageSize.value || 20);
                syncDesktopFiltersToMobile();
                updateKpiSelection();
                updateActiveFilterCount();
                loadInventoryPage();
            });
        });

        document.querySelectorAll("[data-inventory-state]").forEach(function (button) {
            button.addEventListener("click", function () {
                const nextState = button.dataset.inventoryState || "";
                elements.inventoryState.value = nextState;
                state.page = 1;
                syncDesktopFiltersToMobile();
                updateKpiSelection();
                updateActiveFilterCount();
                loadInventoryPage();
            });
        });

        elements.reset?.addEventListener("click", function () {
            resetAllFilters();
            loadInventoryPage();
        });

        elements.reload?.addEventListener("click", function () {
            loadInventoryPage();
        });

        elements.mobileFilterSheet?.addEventListener("show.bs.offcanvas", function () {
            syncDesktopFiltersToMobile();
        });

        elements.mobileApply?.addEventListener("click", function () {
            syncMobileFiltersToDesktop();
            state.page = 1;
            state.pageSize = Number(elements.pageSize.value || 20);
            updateKpiSelection();
            updateActiveFilterCount();
            window.bootstrap?.Offcanvas
                ?.getOrCreateInstance(elements.mobileFilterSheet)
                .hide();
            loadInventoryPage();
        });

        elements.mobileClear?.addEventListener("click", function () {
            resetMobileFilters();
        });

        updateClearSearchVisibility();
        updateKpiSelection();
        updateActiveFilterCount();
    }

    async function loadWarehouses() {
        try {
            const response = await fetch(endpoints.warehouses, {
                headers: { "Accept": "application/json" }
            });

            if (!response.ok) throw new Error("Không tải được danh sách kho.");

            const payload = await response.json();
            const items = Array.isArray(payload.results) ? payload.results : [];

            const options = ["<option value=\"\">Tất cả kho</option>"]
                .concat(items.map(function (item) {
                    return `<option value="${escapeAttribute(item.id)}">${escapeHtml(item.text || "")}</option>`;
                }))
                .join("");

            elements.warehouse.innerHTML = options;
            elements.mobileWarehouse.innerHTML = options;
        } catch (error) {
            console.error(error);
        }
    }

    async function loadInventoryPage() {
        const sequence = ++requestSequence;
        pageAbortController?.abort();
        pageAbortController = new AbortController();

        setPageBusy(true);
        setCostVisibility(false);
        renderLoadingState();

        try {
            const response = await fetch(
                `${endpoints.data}?${buildQueryParameters().toString()}`,
                {
                    headers: { "Accept": "application/json" },
                    signal: pageAbortController.signal
                });

            if (!response.ok) throw new Error("Không tải được dữ liệu tồn kho.");

            const payload = await response.json();
            if (sequence !== requestSequence) return;

            state.page = Number(payload.page || 1);
            state.pageSize = Number(payload.pageSize || 20);
            state.totalItems = Number(payload.totalItems || 0);
            state.totalPages = Math.max(1, Number(payload.totalPages || 1));
            state.items = Array.isArray(payload.items) ? payload.items : [];
            setCostVisibility(payload.canViewCost === true);

            renderInventorySummary(payload.summary || {});
            renderInventoryDesktopRows(state.items);
            renderInventoryMobileCards(state.items);
            renderInventoryPagination();
            bindRenderedEvents();
        } catch (error) {
            if (error?.name === "AbortError") return;
            console.error(error);
            renderErrorState(error?.message || "Không tải được dữ liệu tồn kho.");
        } finally {
            if (sequence === requestSequence) setPageBusy(false);
        }
    }

    function buildQueryParameters() {
        const parameters = new URLSearchParams({
            page: String(state.page),
            pageSize: String(Number(elements.pageSize.value || 20)),
            sortBy: elements.sortBy.value || "productname",
            sortDirection: elements.sortDirection.value || "asc"
        });

        const keyword = elements.keyword.value.trim();
        const warehouseId = elements.warehouse.value;
        const inventoryState = elements.inventoryState.value;

        if (keyword) parameters.set("keyword", keyword);
        if (warehouseId) parameters.set("warehouseId", warehouseId);
        if (inventoryState) parameters.set("state", inventoryState);

        return parameters;
    }

    function renderInventorySummary(summary) {
        elements.totalCount.textContent = formatQuantity(summary.totalItems);
        elements.negativeCount.textContent = formatQuantity(summary.negativeItems);
        elements.positiveCount.textContent = formatQuantity(summary.positiveItems);
        elements.zeroCount.textContent = formatQuantity(summary.zeroItems);
    }

    function renderInventoryDesktopRows(items) {
        if (!items.length) {
            elements.desktopBody.innerHTML = `
                <tr>
                    <td colspan="${state.canViewCost ? 9 : 8}">
                        ${renderEmptyState("Không có dữ liệu tồn kho phù hợp.")}
                    </td>
                </tr>`;
            return;
        }

        elements.desktopBody.innerHTML = items.map(function (item) {
            return `
                <tr class="inventory-row"
                    tabindex="0"
                    title="Nhấp đúp để xem nhanh"
                    data-warehouse-id="${escapeAttribute(item.warehouseId)}"
                    data-product-variant-id="${escapeAttribute(item.productVariantId)}">
                    <td class="inventory-product-cell">
                        <div class="inventory-product">
                            ${renderProductImage(item, false)}
                            <div class="min-w-0">
                                <div class="inventory-product-name text-truncate">${escapeHtml(item.productName || "—")}</div>
                            </div>
                        </div>
                    </td>
                    <td><span class="fw-semibold text-dark">${escapeHtml(item.warehouseName || "—")}</span></td>
                    <td><span class="inventory-barcode" title="${escapeAttribute(item.barcode || "")}">${escapeHtml(item.barcode || "—")}</span></td>
                    <td class="text-end"><span class="inventory-qty ${quantityClass(item.onHandQty)}">${formatQuantity(item.onHandQty)}</span></td>
                    <td class="text-end"><span class="inventory-qty">${formatQuantity(item.reservedQty)}</span></td>
                    <td class="text-end"><span class="inventory-qty ${quantityClass(item.availableQty)}">${formatQuantity(item.availableQty)}</span></td>
                    ${state.canViewCost ? `<td class="text-end inventory-cost-cell">${renderFifoCost(item.cost)}</td>` : ""}
                    <td>${renderState(item.state)}</td>
                    <td class="text-end">
                        <button type="button"
                                class="gds-icon-button js-inventory-quick-view"
                                aria-label="Xem nhanh ${escapeAttribute(item.productName || "sản phẩm")}" title="Xem nhanh">
                            <i class="bx bx-show" aria-hidden="true"></i>
                        </button>
                    </td>
                </tr>`;
        }).join("");
    }

    function renderInventoryMobileCards(items) {
        if (!items.length) {
            elements.mobileList.innerHTML = renderEmptyState("Không có dữ liệu tồn kho phù hợp.");
            return;
        }

        elements.mobileList.innerHTML = items.map(function (item) {
            return `
                <article class="inventory-mobile-card"
                         data-warehouse-id="${escapeAttribute(item.warehouseId)}"
                         data-product-variant-id="${escapeAttribute(item.productVariantId)}">
                    <div class="inventory-mobile-card__header">
                        <div class="inventory-mobile-card__product">
                            ${renderProductImage(item, true)}
                            <div class="min-w-0">
                                <div class="inventory-product-name">${escapeHtml(item.productName || "—")}</div>
                                <div class="inventory-mobile-card__context mt-1">
                                    <span><i class="bx bx-store-alt me-1" aria-hidden="true"></i>${escapeHtml(item.warehouseName || "—")}</span>
                                </div>
                            </div>
                        </div>
                        ${renderState(item.state)}
                    </div>
                    <div class="inventory-mobile-card__barcode">
                        <span class="inventory-barcode" title="${escapeAttribute(item.barcode || "")}">${escapeHtml(item.barcode || "Chưa có barcode")}</span>
                        <button type="button"
                                class="gds-icon-button js-copy-barcode"
                                data-barcode="${escapeAttribute(item.barcode || "")}" aria-label="Sao chép barcode" title="Sao chép barcode">
                            <i class="bx bx-copy" aria-hidden="true"></i>
                        </button>
                    </div>
                    <div class="inventory-mobile-card__metrics">
                        ${renderMobileMetric("Tồn thực tế", item.onHandQty)}
                        ${renderMobileMetric("Đang giữ", item.reservedQty)}
                        ${renderMobileMetric("Khả dụng", item.availableQty)}
                    </div>
                    ${state.canViewCost && item.cost ? `<div class="inventory-mobile-cost"><span>Giá vốn lô FIFO đầu</span><div class="text-end">${renderFifoCost(item.cost)}</div></div>` : ""}
                    <button type="button" class="btn btn-label-primary inventory-mobile-card__action js-inventory-quick-view">
                        <i class="bx bx-show me-1" aria-hidden="true"></i>Xem nhanh
                    </button>
                </article>`;
        }).join("");
    }

    function renderProductImage(item, mobile) {
        const imageUrl = String(item.imageUrl || "").trim();
        const mobileClass = mobile ? " js-mobile-product-image" : "";

        if (!imageUrl) {
            return `
                <button type="button" class="inventory-product-image-button" disabled aria-label="Sản phẩm chưa có ảnh">
                    <i class="bx bx-image fs-4" aria-hidden="true"></i>
                </button>`;
        }

        return `
            <button type="button"
                    class="inventory-product-image-button js-inventory-product-image${mobileClass}"
                    data-image-url="${escapeAttribute(imageUrl)}"
                    data-product-name="${escapeAttribute(item.productName || "")}" aria-label="Phóng to ảnh ${escapeAttribute(item.productName || "sản phẩm")}">
                <img src="${escapeAttribute(imageUrl)}" data-image-fallback loading="lazy" alt="Ảnh ${escapeAttribute(item.productName || "sản phẩm")}" />
                <span class="inventory-image-zoom-mark" aria-hidden="true"><i class="bx bx-search-alt-2"></i></span>
            </button>`;
    }

    function renderMobileMetric(label, value) {
        return `
            <div class="inventory-mobile-card__metric">
                <span class="inventory-mobile-card__metric-label">${escapeHtml(label)}</span>
                <span class="inventory-mobile-card__metric-value ${quantityClass(value)}">${formatQuantity(value)}</span>
            </div>`;
    }

    function renderInventoryPagination() {
        const firstItem = state.totalItems === 0
            ? 0
            : ((state.page - 1) * state.pageSize) + 1;
        const lastItem = state.totalItems === 0
            ? 0
            : Math.min(state.page * state.pageSize, state.totalItems);

        elements.resultSummary.innerHTML = state.totalItems === 0
            ? "0 kết quả"
            : `<strong>${formatQuantity(firstItem)}–${formatQuantity(lastItem)}</strong> / ${formatQuantity(state.totalItems)} kết quả`;

        const compact = window.matchMedia("(max-width: 575.98px)").matches;
        const pages = compact
            ? [state.page]
            : buildVisiblePages(state.page, state.totalPages);
        const html = [];

        html.push(renderPageItem(state.page - 1, "<i class=\"bx bx-chevron-left\"></i>", state.page <= 1, false, "Trang trước"));

        let previous = 0;
        pages.forEach(function (page) {
            if (previous && page - previous > 1) {
                html.push('<li class="page-item disabled"><span class="page-link">…</span></li>');
            }
            html.push(renderPageItem(page, String(page), false, page === state.page, `Trang ${page}`));
            previous = page;
        });

        html.push(renderPageItem(state.page + 1, "<i class=\"bx bx-chevron-right\"></i>", state.page >= state.totalPages, false, "Trang sau"));
        elements.pagination.innerHTML = html.join("");

        elements.pagination.querySelectorAll("button[data-page]").forEach(function (button) {
            button.addEventListener("click", function () {
                const page = Number(button.dataset.page || 1);
                if (page < 1 || page > state.totalPages || page === state.page) return;
                state.page = page;
                loadInventoryPage();
                root.scrollIntoView({ behavior: "smooth", block: "start" });
            });
        });
    }

    function buildVisiblePages(currentPage, totalPages) {
        const values = new Set([1, totalPages]);
        for (let page = currentPage - 2; page <= currentPage + 2; page += 1) {
            if (page >= 1 && page <= totalPages) values.add(page);
        }
        return Array.from(values).sort(function (a, b) { return a - b; });
    }

    function renderPageItem(page, content, disabled, active, label) {
        return `
            <li class="page-item${disabled ? " disabled" : ""}${active ? " active" : ""}">
                <button type="button" class="page-link" data-page="${page}" aria-label="${escapeAttribute(label)}"${disabled ? " disabled" : ""}${active ? ' aria-current="page"' : ""}>
                    ${content}
                </button>
            </li>`;
    }

    function bindRenderedEvents() {
        elements.desktopBody.querySelectorAll(".inventory-row").forEach(function (row) {
            row.addEventListener("dblclick", function (event) {
                if (event.target.closest("button, a, input, select, textarea")) return;
                openInventoryQuickView(row);
            });

            row.addEventListener("keydown", function (event) {
                if (event.key !== "Enter" || event.target !== row) return;
                event.preventDefault();
                openInventoryQuickView(row);
            });
        });

        root.querySelectorAll(".js-inventory-quick-view").forEach(function (button) {
            button.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();
                openInventoryQuickView(button);
            });
        });

        root.querySelectorAll(".js-inventory-product-image").forEach(function (button) {
            button.addEventListener("pointerenter", function () {
                if (!window.matchMedia("(max-width: 991.98px)").matches) {
                    showInventoryImageHoverPreview(button);
                }
            });
            button.addEventListener("pointerleave", hideInventoryImageHoverPreview);
            button.addEventListener("focus", function () {
                if (!window.matchMedia("(max-width: 991.98px)").matches) {
                    showInventoryImageHoverPreview(button);
                }
            });
            button.addEventListener("blur", hideInventoryImageHoverPreview);
            button.addEventListener("click", function (event) {
                event.stopPropagation();
                if (window.matchMedia("(max-width: 991.98px)").matches) {
                    openInventoryImagePreview(button.dataset.imageUrl, button.dataset.productName);
                }
            });
        });

        root.querySelectorAll(".js-copy-barcode").forEach(function (button) {
            button.addEventListener("click", async function () {
                const barcode = button.dataset.barcode || "";
                if (!barcode) return;
                try {
                    await navigator.clipboard.writeText(barcode);
                    button.classList.add("text-success");
                    window.setTimeout(function () { button.classList.remove("text-success"); }, 1000);
                } catch (error) {
                    console.error(error);
                }
            });
        });
    }

    function getInventoryImageHoverPreview() {
        if (inventoryImageHoverPreview) return inventoryImageHoverPreview;

        const preview = document.createElement("div");
        preview.className = "inventory-image-hover-preview";
        preview.hidden = true;
        preview.setAttribute("aria-hidden", "true");
        preview.innerHTML = '<img alt="Ảnh sản phẩm phóng to" />';
        document.body.appendChild(preview);
        inventoryImageHoverPreview = preview;
        return preview;
    }

    function showInventoryImageHoverPreview(button) {
        const imageUrl = button.dataset.imageUrl || "";
        if (!imageUrl) return;

        const preview = getInventoryImageHoverPreview();
        const image = preview.querySelector("img");
        const rect = button.getBoundingClientRect();
        const previewSize = 280;
        const gap = 12;
        let left = rect.right + gap;

        if (left + previewSize > window.innerWidth - gap) {
            left = rect.left - previewSize - gap;
        }

        const top = Math.min(
            Math.max(gap, rect.top - ((previewSize - rect.height) / 2)),
            Math.max(gap, window.innerHeight - previewSize - gap));

        image.src = imageUrl;
        image.alt = `Ảnh phóng to ${button.dataset.productName || "sản phẩm"}`;
        preview.style.left = `${Math.max(gap, left)}px`;
        preview.style.top = `${top}px`;
        preview.hidden = false;
    }

    function hideInventoryImageHoverPreview() {
        if (inventoryImageHoverPreview) inventoryImageHoverPreview.hidden = true;
    }

    function openInventoryImagePreview(imageUrl, productName) {
        if (!imageUrl || !elements.imagePreviewModal || !window.bootstrap?.Modal) return;
        elements.imagePreviewTitle.textContent = productName || "Ảnh sản phẩm";
        elements.imagePreviewImage.src = imageUrl;
        elements.imagePreviewImage.alt = `Ảnh phóng to ${productName || "sản phẩm"}`;
        window.bootstrap.Modal.getOrCreateInstance(elements.imagePreviewModal).show();
    }

    async function openInventoryQuickView(source) {
        const container = source.closest("[data-warehouse-id][data-product-variant-id]");
        if (!container || !elements.quickViewModal || !window.bootstrap?.Modal) return;

        const warehouseId = container.dataset.warehouseId;
        const productVariantId = container.dataset.productVariantId;
        if (!warehouseId || !productVariantId) return;

        hideInventoryImageHoverPreview();
        quickViewAbortController?.abort();
        quickViewAbortController = new AbortController();

        elements.quickViewTitle.textContent = "Thông tin tồn kho";
        elements.quickWarehouse.textContent = "Đang tải...";
        setQuickState("", "—");
        elements.quickBody.innerHTML = `
            <div class="inventory-quick-loading text-center text-muted py-5">
                <span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>
                Đang tải thông tin...
            </div>`;

        window.bootstrap.Modal.getOrCreateInstance(elements.quickViewModal).show();

        const parameters = new URLSearchParams({ warehouseId, productVariantId });

        try {
            const response = await fetch(
                `${endpoints.quickView}?${parameters.toString()}`,
                {
                    headers: { "Accept": "application/json" },
                    signal: quickViewAbortController.signal
                });

            if (!response.ok) throw new Error("Không tải được thông tin xem nhanh.");

            const item = await response.json();
            renderInventoryQuickView(item);
        } catch (error) {
            if (error?.name === "AbortError") return;
            console.error(error);
            elements.quickBody.innerHTML = renderEmptyState(
                error?.message || "Không tải được thông tin xem nhanh.",
                true);
        }
    }

    function renderInventoryQuickView(item) {
        elements.quickViewTitle.textContent = item.productName || "Thông tin tồn kho";
        elements.quickWarehouse.textContent = item.warehouseName || "—";
        setQuickState(item.state, stateLabel(item.state));

        const variantName = String(item.variantName || "").trim();
        const showVariant = variantName
            && variantName.toLocaleLowerCase("vi-VN") !== String(item.productName || "").trim().toLocaleLowerCase("vi-VN");
        const units = Array.isArray(item.units) ? item.units : [];

        elements.quickBody.innerHTML = `
            <div class="inventory-quick-grid">
                <div>
                    <button type="button"
                            class="inventory-quick-image-panel border-0 w-100 js-quick-image-preview"
                            ${item.imageUrl ? "" : "disabled"}
                            data-image-url="${escapeAttribute(item.imageUrl || "")}"
                            data-product-name="${escapeAttribute(item.productName || "")}">
                        ${item.imageUrl
                            ? `<img src="${escapeAttribute(item.imageUrl)}" data-image-fallback alt="Ảnh ${escapeAttribute(item.productName || "sản phẩm")}" />`
                            : `<span class="inventory-quick-image-empty"><i class="bx bx-image fs-1 d-block mb-2"></i>Chưa có hình ảnh</span>`}
                    </button>
                    <div class="mt-3">
                        <div class="text-muted small">Barcode đại diện</div>
                        <div class="inventory-barcode mt-1">${escapeHtml(item.barcode || "—")}</div>
                    </div>
                    ${showVariant
                        ? `<div class="mt-3"><div class="text-muted small">Phân loại</div><div class="fw-semibold text-dark mt-1">${escapeHtml(variantName)}</div></div>`
                        : ""}
                </div>
                <div>
                    <div class="inventory-quick-metrics">
                        ${renderQuickMetric("Tồn thực tế", item.onHandQty)}
                        ${renderQuickMetric("Đang giữ", item.reservedQty)}
                        ${renderQuickMetric("Khả dụng", item.availableQty, true)}
                    </div>
                    ${item.cost ? renderCostDetails(item.cost) : ""}
                    <div class="d-flex align-items-center justify-content-between gap-2 mt-4 mb-2">
                        <h5 class="mb-0 text-dark">Đơn vị &amp; giá bán</h5>
                        <span class="gds-code">Đơn vị gốc: ${escapeHtml(item.baseUnitName || "—")}</span>
                    </div>
                    <div class="table-responsive border rounded-3">
                        <table class="table align-middle inventory-unit-table mb-0">
                            <thead><tr><th>Đơn vị</th><th>Quy đổi</th><th>Barcode</th><th class="text-end">Giá bán</th></tr></thead>
                            <tbody>
                                ${units.length
                                    ? units.map(function (unit) {
                                        return renderUnitRow(unit, item.baseUnitName);
                                    }).join("")
                                    : '<tr><td colspan="4" class="text-center text-muted py-4">Chưa có đơn vị quy đổi đang hoạt động.</td></tr>'}
                            </tbody>
                        </table>
                    </div>
                    <div class="inventory-quick-note mt-3">
                        <i class="bx bx-info-circle" aria-hidden="true"></i>
                        <span>Số lượng tồn được quy về đơn vị gốc.</span>
                    </div>
                </div>
            </div>`;

        elements.quickBody.querySelector(".js-quick-image-preview")?.addEventListener("click", function (event) {
            const button = event.currentTarget;
            openInventoryImagePreview(button.dataset.imageUrl, button.dataset.productName);
        });
    }

    function renderQuickMetric(label, value, emphasize) {
        return `
            <div class="inventory-quick-metric">
                <div class="inventory-quick-metric__label">${escapeHtml(label)}</div>
                <div class="inventory-quick-metric__value ${emphasize ? quantityClass(value) : ""}">${formatQuantity(value)}</div>
            </div>`;
    }

    function setCostVisibility(visible) {
        state.canViewCost = visible;
        root.querySelectorAll("[data-inventory-cost-column]").forEach(function (element) {
            element.hidden = !visible;
        });
    }

    function renderFifoCost(cost) {
        if (!cost) return '<span class="text-muted">—</span>';
        const value = cost.nextFifoUnitCost == null
            ? '<span class="text-muted small">Chưa có lô FIFO</span>'
            : `<strong>${formatCost(cost.nextFifoUnitCost)}</strong><small class="d-block text-muted">/ ${escapeHtml(cost.baseUnitName || "đơn vị gốc")}</small>`;
        return `${value}${cost.hasProvisionalCost || cost.nextFifoIsProvisional ? '<small class="d-block text-warning">Có giá vốn tạm tính</small>' : ""}`;
    }

    function renderCostDetails(cost) {
        const unit = escapeHtml(cost.baseUnitName || "đơn vị gốc");
        const timestamp = String(cost.lastInboundAtUtc || "");
        const date = timestamp
            ? new Date(/(Z|[+-]\d{2}:\d{2})$/i.test(timestamp) ? timestamp : `${timestamp}Z`)
            : null;
        const dateText = date && !Number.isNaN(date.getTime())
            ? date.toLocaleString("vi-VN") : "Chưa có lần nhập kho";
        const fifoQuantity = cost.nextFifoRemainingQuantity == null
            ? "Chưa có lô FIFO còn hàng"
            : `Lô đầu còn ${formatQuantity(cost.nextFifoRemainingQuantity)} ${unit}`;
        return `
            <section class="inventory-cost-details mt-3" aria-label="Giá vốn tồn kho">
                <div class="d-flex justify-content-between align-items-center gap-2 mb-2">
                    <h5 class="mb-0">Giá vốn tồn kho</h5><span class="badge bg-label-primary">ADMIN</span>
                </div>
                <div class="inventory-cost-grid">
                    <div><span>Giá vốn lô FIFO đầu</span><strong>${formatCost(cost.nextFifoUnitCost)}</strong><small>${fifoQuantity}</small></div>
                    <div><span>Giá vốn bình quân tồn</span><strong>${formatCost(cost.averageUnitCost)}</strong><small>Giá trị tồn / số lượng tồn dương</small></div>
                    <div><span>Giá vốn lần nhập kho gần nhất</span><strong>${formatCost(cost.lastInboundUnitCost)}</strong><small>${escapeHtml(dateText)}</small></div>
                    <div><span>Tổng giá trị tồn</span><strong>${formatCost(cost.inventoryValue)}</strong><small>Theo sổ giá vốn hiện tại</small></div>
                </div>
                ${cost.hasProvisionalCost || cost.nextFifoIsProvisional
                    ? '<div class="alert alert-warning py-2 mt-2 mb-0">Có giá vốn tạm tính hoặc tồn âm. Giá trị tồn có thể thay đổi khi nhập bù.</div>' : ""}
                <p class="small text-muted mt-2 mb-0">Các đơn giá tính trên 1 ${unit}. Bán vượt số lượng lô đầu sẽ dùng tiếp giá của các lô FIFO sau. Giá vốn nhập kho có thể gồm VAT và phí vận chuyển theo phiếu đã duyệt.</p>
            </section>`;
    }

    function formatCost(value) {
        if (value == null || !Number.isFinite(Number(value))) return "Chưa có";
        return formatMoney(value);
    }

    function renderUnitRow(unit, baseUnitName) {
        const markers = [];
        if (unit.isBaseUnit) markers.push("Đơn vị gốc");
        if (unit.isDefaultForSale) markers.push("Mặc định bán");

        return `
            <tr>
                <td data-label="Đơn vị">
                    <div class="fw-semibold text-dark">${escapeHtml(unit.unitName || "—")}</div>
                    ${markers.length ? `<div class="text-muted small mt-1">${escapeHtml(markers.join(" · "))}</div>` : ""}
                </td>
                <td data-label="Quy đổi">${formatQuantity(unit.factor)} ${escapeHtml(baseUnitName || "đơn vị gốc")}</td>
                <td data-label="Barcode"><span class="inventory-barcode">${escapeHtml(unit.barcode || "—")}</span></td>
                <td data-label="Giá bán" class="text-end"><span class="inventory-unit-price">${formatMoney(unit.sellPrice)}</span></td>
            </tr>`;
    }

    function setQuickState(value, label) {
        elements.quickState.classList.remove("is-success", "is-danger", "is-neutral");
        elements.quickState.classList.add(
            value === "positive"
                ? "is-success"
                : value === "negative"
                    ? "is-danger"
                    : "is-neutral");
        elements.quickState.textContent = label;
    }

    function renderState(value) {
        return `<span class="inventory-state is-${escapeAttribute(value || "zero")}">${escapeHtml(stateLabel(value))}</span>`;
    }

    function stateLabel(value) {
        if (value === "positive") return "Còn hàng";
        if (value === "negative") return "Âm kho";
        return "Hết hàng";
    }

    function quantityClass(value) {
        const number = Number(value || 0);
        return number < 0 ? "is-negative" : number > 0 ? "is-positive" : "";
    }

    function syncDesktopFiltersToMobile() {
        elements.mobileWarehouse.value = elements.warehouse.value;
        elements.mobileState.value = elements.inventoryState.value;
        elements.mobileSortBy.value = elements.sortBy.value;
        elements.mobileSortDirection.value = elements.sortDirection.value;
        elements.mobilePageSize.value = elements.pageSize.value;
    }

    function syncMobileFiltersToDesktop() {
        elements.warehouse.value = elements.mobileWarehouse.value;
        elements.inventoryState.value = elements.mobileState.value;
        elements.sortBy.value = elements.mobileSortBy.value;
        elements.sortDirection.value = elements.mobileSortDirection.value;
        elements.pageSize.value = elements.mobilePageSize.value;
    }

    function resetMobileFilters() {
        elements.mobileWarehouse.value = "";
        elements.mobileState.value = "";
        elements.mobileSortBy.value = "productname";
        elements.mobileSortDirection.value = "asc";
        elements.mobilePageSize.value = "20";
    }

    function resetAllFilters() {
        elements.keyword.value = "";
        elements.warehouse.value = "";
        elements.inventoryState.value = "";
        elements.sortBy.value = "productname";
        elements.sortDirection.value = "asc";
        elements.pageSize.value = "20";
        state.page = 1;
        state.pageSize = 20;
        syncDesktopFiltersToMobile();
        updateClearSearchVisibility();
        updateKpiSelection();
        updateActiveFilterCount();
    }

    function updateKpiSelection() {
        const current = elements.inventoryState.value || "";
        document.querySelectorAll("[data-inventory-state]").forEach(function (button) {
            const active = (button.dataset.inventoryState || "") === current;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-pressed", String(active));
        });
    }

    function updateActiveFilterCount() {
        let count = 0;
        if (elements.warehouse.value) count += 1;
        if (elements.inventoryState.value) count += 1;
        if (elements.sortBy.value !== "productname" || elements.sortDirection.value !== "asc") count += 1;

        elements.activeFilterCount.textContent = String(count);
        elements.activeFilterCount.classList.toggle("d-none", count === 0);
    }

    function updateClearSearchVisibility() {
        elements.clearSearch.classList.toggle("d-none", !elements.keyword.value.trim());
    }

    function setPageBusy(busy) {
        elements.resultsPanel.setAttribute("aria-busy", String(busy));
        elements.reload.disabled = busy;
    }

    function renderLoadingState() {
        elements.desktopBody.innerHTML = '<tr><td colspan="8"><div class="gds-empty"><span class="spinner-border spinner-border-sm me-2"></span>Đang tải dữ liệu...</div></td></tr>';
        elements.mobileList.innerHTML = '<div class="gds-empty"><span class="spinner-border spinner-border-sm me-2"></span>Đang tải dữ liệu...</div>';
    }

    function renderErrorState(message) {
        const content = renderEmptyState(message, true);
        elements.desktopBody.innerHTML = `<tr><td colspan="8">${content}</td></tr>`;
        elements.mobileList.innerHTML = content;
        elements.resultSummary.textContent = "Không tải được dữ liệu.";
        elements.pagination.innerHTML = "";
    }

    function renderEmptyState(message, danger) {
        return `
            <div class="gds-empty${danger ? " text-danger" : ""}">
                <div class="gds-empty__icon"><i class="bx ${danger ? "bx-error-circle" : "bx-package"}"></i></div>
                <div class="fw-semibold">${escapeHtml(message)}</div>
            </div>`;
    }

    function formatQuantity(value) {
        return Number(value || 0).toLocaleString("vi-VN", {
            minimumFractionDigits: 0,
            maximumFractionDigits: 3
        });
    }

    function formatMoney(value) {
        return `${Number(value || 0).toLocaleString("vi-VN", {
            minimumFractionDigits: 0,
            maximumFractionDigits: 0
        })} ₫`;
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }

    function escapeAttribute(value) {
        return escapeHtml(value);
    }
})();
