(function () {
    "use strict";

    const root = document.querySelector("[data-inventory-ledger-index]");
    if (!root) return;

    const urls = {
        data: root.dataset.dataUrl,
        quickView: root.dataset.quickViewUrl,
        warehouses: root.dataset.warehouseUrl
    };

    const transactionLabels = {
        1: "Số dư đầu kỳ",
        10: "Nhập mua hàng",
        11: "Trả hàng nhà cung cấp",
        20: "Xuất bán hàng",
        21: "Nhập lại hàng bán",
        22: "Hoàn kho do hủy đơn",
        23: "Khách trả hàng",
        30: "Điều chỉnh tăng",
        31: "Điều chỉnh giảm",
        40: "Nhập chuyển kho",
        41: "Xuất chuyển kho",
        50: "Kiểm kê tăng",
        51: "Kiểm kê giảm",
        60: "Giữ hàng",
        61: "Nhả giữ hàng",
        70: "Định giá lại"
    };

    const referenceLabels = {
        0: "Không có nguồn",
        1: "Đơn bán hàng",
        2: "Phiếu nhập mua",
        3: "Phiếu trả nhà cung cấp",
        4: "Hoàn trả",
        5: "Phiếu điều chỉnh",
        6: "Phiếu chuyển kho",
        7: "Phiếu kiểm kê",
        8: "Giữ hàng",
        10: "Chứng từ kho"
    };

    const state = {
        page: 1,
        pageSize: 20,
        totalPages: 1,
        totalItems: 0,
        canViewCost: false,
        summary: {},
        summarySignature: null,
        ledgerState: "",
        requestController: null,
        requestSequence: 0,
        quickRequestController: null,
        items: []
    };

    let searchDebounceTimer = null;
    let ledgerImageHoverPreview = null;
    let quickViewModal = null;
    let imagePreviewModal = null;
    let mobileFilterSheet = null;

    const elements = {
        keyword: document.getElementById("ledgerKeyword"),
        searchScope: document.getElementById("ledgerSearchScope"),
        clearSearch: document.getElementById("ledgerClearSearch"),
        warehouse: document.getElementById("ledgerWarehouseFilter"),
        transactionType: document.getElementById("ledgerTransactionTypeFilter"),
        referenceType: document.getElementById("ledgerReferenceTypeFilter"),
        fromDate: document.getElementById("ledgerFromDate"),
        toDate: document.getElementById("ledgerToDate"),
        referenceCode: document.getElementById("ledgerReferenceCode"),
        sortBy: document.getElementById("ledgerSortBy"),
        sortDirection: document.getElementById("ledgerSortDirection"),
        pageSize: document.getElementById("ledgerPageSize"),
        reset: document.getElementById("ledgerResetFilters"),
        reload: document.getElementById("ledgerReloadButton"),
        dateError: document.getElementById("ledgerDateError"),
        resultsPanel: document.getElementById("ledgerResultsPanel"),
        desktopBody: document.getElementById("ledgerDesktopBody"),
        mobileList: document.getElementById("ledgerMobileList"),
        resultSummary: document.getElementById("ledgerResultSummary"),
        pagination: document.getElementById("ledgerPagination"),
        totalCount: document.getElementById("ledgerTotalCount"),
        increaseCount: document.getElementById("ledgerIncreaseCount"),
        decreaseCount: document.getElementById("ledgerDecreaseCount"),
        negativeCount: document.getElementById("ledgerNegativeCount"),
        mobileWarehouse: document.getElementById("ledgerMobileWarehouse"),
        mobileTransactionType: document.getElementById("ledgerMobileTransactionType"),
        mobileReferenceType: document.getElementById("ledgerMobileReferenceType"),
        mobileFromDate: document.getElementById("ledgerMobileFromDate"),
        mobileToDate: document.getElementById("ledgerMobileToDate"),
        mobileReferenceCode: document.getElementById("ledgerMobileReferenceCode"),
        mobileSortBy: document.getElementById("ledgerMobileSortBy"),
        mobileSortDirection: document.getElementById("ledgerMobileSortDirection"),
        mobilePageSize: document.getElementById("ledgerMobilePageSize"),
        mobileApply: document.getElementById("ledgerMobileApplyFilters"),
        mobileClear: document.getElementById("ledgerMobileClearFilters"),
        activeFilterCount: document.getElementById("ledgerActiveFilterCount"),
        quickModal: document.getElementById("ledgerQuickViewModal"),
        quickTitle: document.getElementById("ledgerQuickViewTitle"),
        quickDate: document.getElementById("ledgerQuickDate"),
        quickState: document.getElementById("ledgerQuickState"),
        quickBody: document.getElementById("ledgerQuickBody"),
        imageModal: document.getElementById("ledgerImagePreviewModal"),
        imageTitle: document.getElementById("ledgerImagePreviewTitle"),
        image: document.getElementById("ledgerImagePreviewImage"),
        filterSheet: document.getElementById("ledgerMobileFilterSheet")
    };

    document.addEventListener("DOMContentLoaded", initialize);

    async function initialize() {
        quickViewModal = elements.quickModal && window.bootstrap
            ? bootstrap.Modal.getOrCreateInstance(elements.quickModal)
            : null;
        imagePreviewModal = elements.imageModal && window.bootstrap
            ? bootstrap.Modal.getOrCreateInstance(elements.imageModal)
            : null;
        mobileFilterSheet = elements.filterSheet && window.bootstrap
            ? bootstrap.Offcanvas.getOrCreateInstance(elements.filterSheet)
            : null;

        createImageHoverPreview();
        bindEvents();
        await Promise.all([
            loadWarehouses(),
            loadLedgerPage(1)
        ]);
    }

    function bindEvents() {
        elements.keyword?.addEventListener("input", function () {
            elements.clearSearch?.classList.toggle("d-none", !elements.keyword.value);
            clearTimeout(searchDebounceTimer);
            state.requestController?.abort();
            ++state.requestSequence;
            searchDebounceTimer = setTimeout(function () {
                loadLedgerPage(1);
            }, 350);
        });

        elements.keyword?.addEventListener("keydown", function (event) {
            if (event.key === "Enter" && !event.isComposing) {
                event.preventDefault();
                loadLedgerPage(1);
            }
        });

        elements.clearSearch?.addEventListener("click", function () {
            elements.keyword.value = "";
            elements.clearSearch.classList.add("d-none");
            elements.keyword.focus();
            loadLedgerPage(1);
        });

        [
            elements.searchScope,
            elements.warehouse,
            elements.transactionType,
            elements.referenceType,
            elements.sortBy,
            elements.sortDirection,
            elements.pageSize
        ].forEach(function (control) {
            control?.addEventListener("change", function () {
                syncDesktopToMobile();
                loadLedgerPage(1);
            });
        });

        [elements.fromDate, elements.toDate].forEach(function (control) {
            control?.addEventListener("change", function () {
                syncDesktopToMobile();
                if (validateDateRange()) loadLedgerPage(1);
            });
        });

        elements.referenceCode?.addEventListener("input", function () {
            clearTimeout(searchDebounceTimer);
            state.requestController?.abort();
            ++state.requestSequence;
            searchDebounceTimer = setTimeout(function () {
                syncDesktopToMobile();
                loadLedgerPage(1);
            }, 350);
        });

        elements.reset?.addEventListener("click", resetDesktopFilters);
        elements.reload?.addEventListener("click", function () {
            loadLedgerPage(state.page);
        });

        root.querySelectorAll("[data-ledger-state]").forEach(function (button) {
            button.addEventListener("click", function () {
                const requested = button.dataset.ledgerState || "";
                state.ledgerState = state.ledgerState === requested && requested
                    ? ""
                    : requested;
                updateKpiSelection();
                loadLedgerPage(1);
            });
        });

        elements.pagination?.addEventListener("click", function (event) {
            const link = event.target.closest("[data-page]");
            if (!link || link.closest(".page-item")?.classList.contains("disabled")) return;
            const page = Number.parseInt(link.dataset.page || "1", 10);
            if (page > 0 && page !== state.page) loadLedgerPage(page);
        });

        elements.desktopBody?.addEventListener("dblclick", handleQuickViewEvent);
        elements.desktopBody?.addEventListener("click", handleActionClick);
        elements.desktopBody?.addEventListener("keydown", function (event) {
            if (event.key !== "Enter") return;
            handleQuickViewEvent(event);
        });
        elements.mobileList?.addEventListener("click", handleActionClick);

        elements.mobileApply?.addEventListener("click", function () {
            syncMobileToDesktop();
            if (!validateDateRange()) return;
            mobileFilterSheet?.hide();
            updateActiveFilterCount();
            loadLedgerPage(1);
        });

        elements.mobileClear?.addEventListener("click", function () {
            clearFilterValues(true);
        });

        elements.filterSheet?.addEventListener("show.bs.offcanvas", syncDesktopToMobile);

        document.addEventListener("pointerenter", showHoverImage, true);
        document.addEventListener("pointermove", moveHoverImage, true);
        document.addEventListener("pointerleave", hideHoverImage, true);

        window.addEventListener("resize", function () {
            if (window.innerWidth <= 991.98) hideImageHoverPreview();
        });
    }

    async function loadWarehouses() {
        try {
            const response = await fetch(urls.warehouses, {
                headers: { Accept: "application/json" },
                cache: "no-store"
            });
            if (!response.ok) return;

            const payload = await response.json();
            const items = payload.results || [];
            appendWarehouseOptions(elements.warehouse, items);
            appendWarehouseOptions(elements.mobileWarehouse, items);
        } catch (error) {
            console.error("Không tải được danh sách kho.", error);
        }
    }

    function appendWarehouseOptions(select, items) {
        if (!select) return;
        const current = select.value;
        select.innerHTML = '<option value="">Tất cả kho</option>';
        items.forEach(function (item) {
            const option = document.createElement("option");
            option.value = item.id;
            option.textContent = item.text;
            select.appendChild(option);
        });
        select.value = current;
    }

    async function loadLedgerPage(page) {
        clearTimeout(searchDebounceTimer);
        if (!validateDateRange()) return;

        state.requestController?.abort();
        const controller = new AbortController();
        state.requestController = controller;
        const sequence = ++state.requestSequence;
        let timedOut = false;
        const timeout = setTimeout(function () {
            timedOut = true;
            controller.abort();
        }, 20000);
        renderLoadingState();

        try {
            const summarySignature = buildSummarySignature();
            const includeSummary = state.summarySignature !== summarySignature;
            const response = await fetch(`${urls.data}?${buildQuery(page, includeSummary)}`, {
                headers: { Accept: "application/json" },
                cache: "no-store",
                signal: controller.signal
            });
            if (!response.ok) throw new Error("Không tải được lịch sử giao dịch kho.");

            const result = await response.json();
            if (sequence !== state.requestSequence) return;

            state.page = Number(result.page || 1);
            state.pageSize = Number(result.pageSize || 20);
            state.totalItems = Number(result.totalItems || 0);
            state.totalPages = Math.max(1, Number(result.totalPages || 1));
            if (result.summaryIncluded !== false) {
                state.summary = result.summary || {};
                state.summarySignature = summarySignature;
            }
            state.items = result.items || [];
            setCostVisibility(result.canViewCost === true);

            renderSummary();
            renderLedgerDesktopRows(state.items);
            renderLedgerMobileCards(state.items);
            renderLedgerPagination();
            updateResultSummary();
            updateActiveFilterCount();
            elements.resultsPanel?.setAttribute("aria-busy", "false");
        } catch (error) {
            if (sequence !== state.requestSequence) return;
            if (error.name === "AbortError" && !timedOut) return;
            console.error(error);
            renderErrorState(timedOut
                ? "Tìm kiếm mất nhiều thời gian. Hãy chọn khoảng ngày hoặc từ khóa cụ thể hơn, rồi bấm Tải lại."
                : error.message || "Không tải được dữ liệu.");
        } finally {
            clearTimeout(timeout);
        }
    }

    function buildQuery(page, includeSummary) {
        const params = new URLSearchParams({
            page: String(Math.max(1, page || 1)),
            pageSize: elements.pageSize?.value || "20",
            sortBy: elements.sortBy?.value || "date",
            sortDirection: elements.sortDirection?.value || "desc",
            includeSummary: String(includeSummary !== false)
        });

        appendIfValue(params, "warehouseId", elements.warehouse?.value);
        appendIfValue(params, "keyword", elements.keyword?.value.trim());
        appendIfValue(params, "searchScope", elements.searchScope?.value || "product");
        appendIfValue(params, "transactionType", elements.transactionType?.value);
        appendIfValue(params, "referenceType", elements.referenceType?.value);
        appendIfValue(params, "fromDate", elements.fromDate?.value);
        appendIfValue(params, "toDate", elements.toDate?.value);
        appendIfValue(params, "referenceCode", elements.referenceCode?.value.trim());
        appendIfValue(params, "state", state.ledgerState);
        return params.toString();
    }

    function buildSummarySignature() {
        return JSON.stringify([
            elements.warehouse?.value || "",
            elements.keyword?.value.trim() || "",
            elements.searchScope?.value || "product",
            elements.transactionType?.value || "",
            elements.referenceType?.value || "",
            elements.fromDate?.value || "",
            elements.toDate?.value || "",
            elements.referenceCode?.value.trim() || ""
        ]);
    }

    function appendIfValue(params, name, value) {
        if (value !== undefined && value !== null && value !== "") {
            params.set(name, value);
        }
    }

    function renderSummary() {
        elements.totalCount.textContent = formatNumber(state.summary.totalItems);
        elements.increaseCount.textContent = formatNumber(state.summary.increaseItems);
        elements.decreaseCount.textContent = formatNumber(state.summary.decreaseItems);
        elements.negativeCount.textContent = formatNumber(state.summary.negativeItems);
    }

    function renderLedgerDesktopRows(items) {
        if (!items.length) {
            elements.desktopBody.innerHTML = `<tr><td colspan="${state.canViewCost ? 7 : 6}">` + renderEmptyState("Không có giao dịch phù hợp.") + "</td></tr>";
            return;
        }

        elements.desktopBody.innerHTML = items.map(function (item) {
            const change = Number(item.quantityChange || 0);
            return `
                <tr class="ledger-row" tabindex="0" data-transaction-id="${Number(item.transactionId || 0)}">
                    <td><span class="ledger-date">${formatDateOnly(item.occurredAtUtc)}</span></td>
                    <td class="ledger-product-cell">${renderProduct(item, false)}</td>
                    <td>
                        <div class="ledger-change ${quantityClass(change)}">${formatSignedNumber(change)}</div>
                        <div class="ledger-transaction-label">${escapeHtml(resolveTransactionLabel(item))}</div>
                    </td>
                    <td>${renderQuantityFlow(item)}</td>
                    ${state.canViewCost ? `<td class="text-end ledger-cost-cell">${renderInboundCost(item)}</td>` : ""}
                    <td>${renderReference(item)}</td>
                    <td class="text-end">
                        <div class="ledger-row-actions">
                            ${renderNegativeBadge(item)}
                            <button type="button" class="gds-icon-button js-ledger-quick" data-transaction-id="${Number(item.transactionId || 0)}" aria-label="Xem nhanh" title="Xem nhanh">
                                <i class="bx bx-show" aria-hidden="true"></i>
                            </button>
                        </div>
                    </td>
                </tr>`;
        }).join("");
    }

    function renderLedgerMobileCards(items) {
        if (!items.length) {
            elements.mobileList.innerHTML = renderEmptyState("Không có giao dịch phù hợp.");
            return;
        }

        elements.mobileList.innerHTML = items.map(function (item) {
            const change = Number(item.quantityChange || 0);
            return `
                <article class="ledger-mobile-card" data-transaction-id="${Number(item.transactionId || 0)}">
                    <div class="ledger-mobile-card__top">
                        <span class="ledger-date">${formatDateOnly(item.occurredAtUtc)}</span>
                        ${renderNegativeBadge(item)}
                    </div>
                    ${renderProduct(item, true)}
                    <div class="ledger-mobile-card__movement">
                        <div>
                            <span class="ledger-mobile-card__label">Biến động</span>
                            <strong class="ledger-change ${quantityClass(change)}">${formatSignedNumber(change)}</strong>
                        </div>
                        <div class="text-end">
                            <span class="ledger-mobile-card__label">Tồn trước → sau</span>
                            ${renderQuantityFlow(item)}
                        </div>
                    </div>
                    <div class="ledger-mobile-card__facts">
                        <span><i class="bx bx-transfer"></i>${escapeHtml(resolveTransactionLabel(item))}</span>
                        <span><i class="bx bx-file"></i>${escapeHtml(resolveReferenceLabel(item))}${item.referenceCode ? ` · ${escapeHtml(item.referenceCode)}` : ""}</span>
                    </div>
                    ${state.canViewCost && (change > 0 || isRevaluation(item)) ? `<div class="ledger-mobile-cost"><span>${isRevaluation(item) ? "Điều chỉnh giá vốn" : "Giá nhập lô"}</span><div class="text-end">${renderInboundCost(item)}</div></div>` : ""}
                    <button type="button" class="btn btn-label-primary ledger-mobile-card__action js-ledger-quick" data-transaction-id="${Number(item.transactionId || 0)}">
                        <i class="bx bx-show me-1" aria-hidden="true"></i>Xem nhanh
                    </button>
                </article>`;
        }).join("");
    }

    function renderProduct(item, mobile) {
        const image = item.imageUrl
            ? `<img src="${escapeAttribute(item.imageUrl)}" data-image-fallback alt="Ảnh ${escapeAttribute(item.productName || "sản phẩm")}" />`
            : '<i class="bx bx-image" aria-hidden="true"></i>';
        const variant = item.variantName && item.variantName !== item.productName
            ? `<span>${escapeHtml(item.variantName)}</span>`
            : "";
        return `
            <div class="ledger-product ${mobile ? "is-mobile" : ""}">
                <button type="button" class="ledger-product-image-button js-ledger-image" ${item.imageUrl ? "" : "disabled"}
                        data-image-url="${escapeAttribute(item.imageUrl || "")}" data-product-name="${escapeAttribute(item.productName || "")}">
                    ${image}<span class="ledger-image-zoom-mark"><i class="bx bx-zoom-in"></i></span>
                </button>
                <div class="ledger-product__text">
                    <div class="ledger-product-name">${escapeHtml(item.productName || "—")}</div>
                    <div class="ledger-product-meta">
                        ${variant}
                        ${item.sku ? `<span>SKU: ${escapeHtml(item.sku)}</span>` : ""}
                        ${item.barcode ? `<span>${escapeHtml(item.barcode)}</span>` : ""}
                    </div>
                </div>
            </div>`;
    }

    function renderQuantityFlow(item) {
        const after = Number(item.afterQty || 0);
        return `<div class="ledger-quantity-flow"><span>${formatNumber(item.beforeQty)}</span><i class="bx bx-right-arrow-alt"></i><strong class="${after < 0 ? "is-negative" : ""}">${formatNumber(after)}</strong></div>`;
    }

    function renderReference(item) {
        return `<div class="ledger-reference"><strong>${escapeHtml(resolveReferenceLabel(item))}</strong><span>${escapeHtml(item.referenceCode || "Không có mã chứng từ")}</span></div>`;
    }

    function renderNegativeBadge(item) {
        return item.isNegativeAfterTransaction
            ? '<span class="ledger-status is-negative">Âm sau giao dịch</span>'
            : '<span class="ledger-status is-normal">Bình thường</span>';
    }

    function renderLedgerPagination() {
        const pages = compactPages(state.page, state.totalPages);
        const items = [pageButton(state.page - 1, '<i class="bx bx-chevron-left"></i>', state.page <= 1)];
        let previous = 0;

        pages.forEach(function (page) {
            if (previous && page - previous > 1) {
                items.push('<li class="page-item disabled"><span class="page-link">…</span></li>');
            }
            items.push(pageButton(page, String(page), false, page === state.page));
            previous = page;
        });

        items.push(pageButton(state.page + 1, '<i class="bx bx-chevron-right"></i>', state.page >= state.totalPages));
        elements.pagination.innerHTML = items.join("");
    }

    function compactPages(current, total) {
        const values = new Set([1, total, current - 1, current, current + 1]);
        return Array.from(values)
            .filter(function (value) { return value >= 1 && value <= total; })
            .sort(function (a, b) { return a - b; });
    }

    function pageButton(page, label, disabled, active) {
        return `<li class="page-item ${disabled ? "disabled" : ""} ${active ? "active" : ""}"><button type="button" class="page-link" data-page="${page}" ${active ? 'aria-current="page"' : ""}>${label}</button></li>`;
    }

    function updateResultSummary() {
        if (!state.totalItems) {
            elements.resultSummary.textContent = "0 kết quả";
            return;
        }
        const first = (state.page - 1) * state.pageSize + 1;
        const last = Math.min(state.totalItems, state.page * state.pageSize);
        elements.resultSummary.innerHTML = `Hiển thị <strong>${formatNumber(first)}–${formatNumber(last)}</strong> trong <strong>${formatNumber(state.totalItems)}</strong> giao dịch`;
    }

    function handleQuickViewEvent(event) {
        const row = event.target.closest("[data-transaction-id]");
        const transactionId = Number(row?.dataset.transactionId || 0);
        if (transactionId > 0) openLedgerQuickView(transactionId);
    }

    function handleActionClick(event) {
        const imageButton = event.target.closest(".js-ledger-image");
        if (imageButton) {
            event.stopPropagation();
            if (window.innerWidth <= 991.98) {
                openLedgerImagePreview(imageButton.dataset.imageUrl, imageButton.dataset.productName);
            }
            return;
        }

        const quickButton = event.target.closest(".js-ledger-quick");
        if (quickButton) {
            event.stopPropagation();
            openLedgerQuickView(Number(quickButton.dataset.transactionId || 0));
        }
    }

    async function openLedgerQuickView(transactionId) {
        if (!transactionId || !quickViewModal) return;

        state.quickRequestController?.abort();
        state.quickRequestController = new AbortController();
        elements.quickTitle.textContent = "Đang tải giao dịch...";
        elements.quickDate.textContent = "—";
        setQuickState(false);
        elements.quickBody.innerHTML = '<div class="text-center text-muted py-5"><span class="spinner-border spinner-border-sm me-2"></span>Đang tải thông tin...</div>';
        quickViewModal.show();

        try {
            const response = await fetch(`${urls.quickView}?transactionId=${transactionId}`, {
                headers: { Accept: "application/json" },
                cache: "no-store",
                signal: state.quickRequestController.signal
            });
            if (!response.ok) throw new Error("Không tải được thông tin giao dịch.");
            const payload = await response.json();
            renderQuickView(payload.item || {});
        } catch (error) {
            if (error.name === "AbortError") return;
            elements.quickBody.innerHTML = renderEmptyState(error.message || "Không tải được thông tin.", true);
        }
    }

    function renderQuickView(item) {
        elements.quickTitle.textContent = item.productName || "Thông tin giao dịch";
        elements.quickDate.textContent = formatDateOnly(item.occurredAtUtc);
        setQuickState(item.isNegativeAfterTransaction);
        const change = Number(item.quantityChange || 0);

        elements.quickBody.innerHTML = `
            <div class="ledger-quick-grid">
                <div>
                    <button type="button" class="ledger-quick-image-panel border-0 w-100 js-quick-image-preview" ${item.imageUrl ? "" : "disabled"}
                            data-image-url="${escapeAttribute(item.imageUrl || "")}" data-product-name="${escapeAttribute(item.productName || "")}">
                        ${item.imageUrl
                            ? `<img src="${escapeAttribute(item.imageUrl)}" data-image-fallback alt="Ảnh ${escapeAttribute(item.productName || "sản phẩm")}" />`
                            : '<span class="ledger-quick-image-empty"><i class="bx bx-image fs-1 d-block mb-2"></i>Chưa có hình ảnh</span>'}
                    </button>
                    <div class="ledger-quick-product mt-3">
                        <h4>${escapeHtml(item.productName || "—")}</h4>
                        ${item.variantName && item.variantName !== item.productName ? `<div>${escapeHtml(item.variantName)}</div>` : ""}
                        ${item.sku ? `<div>SKU: ${escapeHtml(item.sku)}</div>` : ""}
                        ${item.barcode ? `<div>Barcode: ${escapeHtml(item.barcode)}</div>` : ""}
                    </div>
                </div>
                <div>
                    <div class="ledger-quick-metrics">
                        ${renderQuickMetric("Tồn trước", item.beforeQty)}
                        ${renderQuickMetric("Biến động", change, quantityClass(change))}
                        ${renderQuickMetric("Tồn sau", item.afterQty, Number(item.afterQty || 0) < 0 ? "is-negative" : "")}
                    </div>
                    <div class="ledger-quick-facts">
                        ${renderQuickFact("Kho", item.warehouseName)}
                        ${renderQuickFact("Loại giao dịch", resolveTransactionLabel(item))}
                        ${renderQuickFact("Nguồn / chứng từ", resolveReferenceLabel(item))}
                        ${renderQuickFact("Mã chứng từ", item.referenceCode || "Không có")}
                    </div>
                    ${renderCostDetails(item)}
                    <div class="ledger-quick-note">
                        <div class="ledger-quick-note__label">Ghi chú</div>
                        <div>${escapeHtml(item.note || "Không có ghi chú")}</div>
                    </div>
                </div>
            </div>`;

        elements.quickBody.querySelector(".js-quick-image-preview")?.addEventListener("click", function (event) {
            const button = event.currentTarget;
            openLedgerImagePreview(button.dataset.imageUrl, button.dataset.productName);
        });
    }

    function renderQuickMetric(label, value, cssClass) {
        return `<div class="ledger-quick-metric"><span>${escapeHtml(label)}</span><strong class="${cssClass || ""}">${label === "Biến động" ? formatSignedNumber(value) : formatNumber(value)}</strong></div>`;
    }

    function setCostVisibility(visible) {
        state.canViewCost = visible;
        root.querySelectorAll("[data-ledger-cost-column]").forEach(function (element) {
            element.hidden = !visible;
        });
    }

    function formatMoney(value) {
        if (value == null || !Number.isFinite(Number(value))) return "Chưa có";
        return `${Number(value).toLocaleString("vi-VN", { maximumFractionDigits: 0 })} ₫`;
    }

    function formatSignedMoney(value) {
        const number = Number(value);
        if (!Number.isFinite(number)) return "Chưa có";
        return `${number > 0 ? "+" : ""}${formatMoney(number)}`;
    }

    function isRevaluation(item) {
        return Number(item.transactionType) === 70;
    }

    function renderInboundCost(item) {
        if (isRevaluation(item)) {
            return `<strong>${formatMoney(item.unitCostSnapshot)}</strong><small class="d-block text-muted">/ ${escapeHtml(item.baseUnitName || "đơn vị gốc")}</small><small class="d-block ${Number(item.totalCost || 0) < 0 ? "text-danger" : "text-primary"}">Chênh lệch: ${formatSignedMoney(item.totalCost)}</small>`;
        }
        if (Number(item.quantityChange || 0) <= 0) return '<span class="text-muted">—</span>';
        const cost = item.inboundCost;
        if (!cost) return '<span class="text-muted small">Chưa có giá nhập</span>';
        return `<strong>${formatMoney(cost.unitCost)}</strong><small class="d-block text-muted">/ ${escapeHtml(cost.baseUnitName || "đơn vị gốc")}</small>${cost.isMixedCost ? '<small class="d-block text-muted">Bình quân các lô nhập</small>' : ""}${cost.isProvisional ? '<small class="d-block text-warning">Giá tạm tính</small>' : ""}`;
    }

    function renderCostDetails(item) {
        if (isRevaluation(item)) {
            const adjustmentClass = Number(item.totalCost || 0) < 0 ? "text-danger" : "text-primary";
            return `<section class="ledger-inbound-cost mb-3" aria-label="Điều chỉnh giá vốn">
                <div class="d-flex align-items-center justify-content-between gap-2 mb-2"><h5 class="mb-0">Điều chỉnh giá vốn</h5><span class="badge bg-label-primary">ADMIN</span></div>
                <div class="ledger-cost-grid">
                    <div><span>Giá vốn sau điều chỉnh / ${escapeHtml(item.baseUnitName || "đơn vị gốc")}</span><strong>${formatMoney(item.unitCostSnapshot)}</strong></div>
                    <div><span>Chênh lệch giá trị tồn</span><strong class="${adjustmentClass}">${formatSignedMoney(item.totalCost)}</strong></div>
                    <div><span>Giá trị tồn trước</span><strong>${formatMoney(item.beforeInventoryValue)}</strong></div>
                    <div><span>Giá trị tồn sau</span><strong>${formatMoney(item.afterInventoryValue)}</strong></div>
                </div>
                <p class="small text-muted mt-2 mb-0">Số lượng không đổi; bút toán này chỉ cập nhật giá vốn của lượng hàng đang còn tồn và giữ nguyên lịch sử phiếu nhập ban đầu.</p>
            </section>`;
        }
        return item.inboundCost ? renderInboundCostDetails(item.inboundCost) : "";
    }

    function renderInboundCostDetails(cost) {
        const unit = escapeHtml(cost.baseUnitName || "đơn vị gốc");
        return `<section class="ledger-inbound-cost mb-3" aria-label="Giá nhập lô">
            <div class="d-flex align-items-center justify-content-between gap-2 mb-2"><h5 class="mb-0">Giá nhập lô</h5><span class="badge bg-label-primary">ADMIN</span></div>
            <div class="ledger-cost-grid">
                <div><span>Đơn giá nhập / ${unit}</span><strong>${formatMoney(cost.unitCost)}</strong></div>
                <div><span>Tổng giá trị nhập</span><strong>${formatMoney(cost.totalCost)}</strong></div>
            </div>
            ${cost.isProvisional ? '<div class="alert alert-warning py-2 mt-2 mb-0">Giá nhập đang tạm tính.</div>' : ""}
            <p class="small text-muted mt-2 mb-0">Giá vốn ghi nhận tại lần nhập này, có thể gồm VAT và phí vận chuyển theo phiếu đã duyệt. Giá tiền hiển thị làm tròn đến đồng.${cost.isMixedCost ? " Giao dịch có nhiều mức giá: đơn giá là bình quân theo số lượng nhập." : ""}</p>
        </section>`;
    }

    function renderQuickFact(label, value) {
        return `<div class="ledger-quick-fact"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value || "—")}</strong></div>`;
    }

    function setQuickState(isNegative) {
        elements.quickState.classList.remove("is-danger", "is-success", "is-neutral");
        elements.quickState.classList.add(isNegative ? "is-danger" : "is-success");
        elements.quickState.textContent = isNegative ? "Âm sau giao dịch" : "Bình thường";
    }

    function openLedgerImagePreview(url, productName) {
        if (!url || !imagePreviewModal) return;
        elements.image.src = url;
        elements.image.alt = `Ảnh ${productName || "sản phẩm"}`;
        elements.imageTitle.textContent = productName || "Ảnh sản phẩm";
        imagePreviewModal.show();
    }

    function createImageHoverPreview() {
        ledgerImageHoverPreview = document.createElement("div");
        ledgerImageHoverPreview.className = "ledger-image-hover-preview";
        ledgerImageHoverPreview.id = "ledgerImageHoverPreview";
        ledgerImageHoverPreview.hidden = true;
        ledgerImageHoverPreview.innerHTML = '<img alt="Ảnh sản phẩm phóng to" />';
        document.body.appendChild(ledgerImageHoverPreview);
    }

    function showHoverImage(event) {
        if (window.innerWidth <= 991.98) return;
        const button = event.target.closest?.(".js-ledger-image");
        if (!button?.dataset.imageUrl || !ledgerImageHoverPreview) return;
        ledgerImageHoverPreview.querySelector("img").src = button.dataset.imageUrl;
        ledgerImageHoverPreview.hidden = false;
        positionHoverImage(event.clientX, event.clientY);
    }

    function moveHoverImage(event) {
        if (ledgerImageHoverPreview?.hidden) return;
        positionHoverImage(event.clientX, event.clientY);
    }

    function hideHoverImage(event) {
        if (!event.target.closest?.(".js-ledger-image")) return;
        hideImageHoverPreview();
    }

    function hideImageHoverPreview() {
        if (ledgerImageHoverPreview) ledgerImageHoverPreview.hidden = true;
    }

    function positionHoverImage(x, y) {
        if (!ledgerImageHoverPreview) return;
        const size = 280;
        const gap = 18;
        const left = Math.min(window.innerWidth - size - gap, x + gap);
        const top = Math.min(window.innerHeight - size - gap, Math.max(gap, y - size / 2));
        ledgerImageHoverPreview.style.left = `${Math.max(gap, left)}px`;
        ledgerImageHoverPreview.style.top = `${top}px`;
    }

    function validateDateRange() {
        const invalid = elements.fromDate?.value
            && elements.toDate?.value
            && elements.fromDate.value > elements.toDate.value;
        elements.dateError?.classList.toggle("d-none", !invalid);
        return !invalid;
    }

    function syncDesktopToMobile() {
        copyValue(elements.warehouse, elements.mobileWarehouse);
        copyValue(elements.transactionType, elements.mobileTransactionType);
        copyValue(elements.referenceType, elements.mobileReferenceType);
        copyValue(elements.fromDate, elements.mobileFromDate);
        copyValue(elements.toDate, elements.mobileToDate);
        copyValue(elements.referenceCode, elements.mobileReferenceCode);
        copyValue(elements.sortBy, elements.mobileSortBy);
        copyValue(elements.sortDirection, elements.mobileSortDirection);
        copyValue(elements.pageSize, elements.mobilePageSize);
    }

    function syncMobileToDesktop() {
        copyValue(elements.mobileWarehouse, elements.warehouse);
        copyValue(elements.mobileTransactionType, elements.transactionType);
        copyValue(elements.mobileReferenceType, elements.referenceType);
        copyValue(elements.mobileFromDate, elements.fromDate);
        copyValue(elements.mobileToDate, elements.toDate);
        copyValue(elements.mobileReferenceCode, elements.referenceCode);
        copyValue(elements.mobileSortBy, elements.sortBy);
        copyValue(elements.mobileSortDirection, elements.sortDirection);
        copyValue(elements.mobilePageSize, elements.pageSize);
    }

    function copyValue(source, target) {
        if (source && target) target.value = source.value;
    }

    function resetDesktopFilters() {
        clearFilterValues(false);
        syncDesktopToMobile();
        state.ledgerState = "";
        updateKpiSelection();
        updateActiveFilterCount();
        loadLedgerPage(1);
    }

    function clearFilterValues(mobileOnly) {
        const controls = mobileOnly
            ? [elements.mobileWarehouse, elements.mobileTransactionType, elements.mobileReferenceType, elements.mobileFromDate, elements.mobileToDate, elements.mobileReferenceCode]
            : [elements.warehouse, elements.transactionType, elements.referenceType, elements.fromDate, elements.toDate, elements.referenceCode];
        controls.forEach(function (control) { if (control) control.value = ""; });

        if (!mobileOnly) {
            elements.keyword.value = "";
            elements.searchScope.value = "product";
            elements.clearSearch?.classList.add("d-none");
            elements.sortBy.value = "date";
            elements.sortDirection.value = "desc";
            elements.pageSize.value = "20";
        } else {
            elements.mobileSortBy.value = "date";
            elements.mobileSortDirection.value = "desc";
            elements.mobilePageSize.value = "20";
        }
    }

    function updateKpiSelection() {
        root.querySelectorAll("[data-ledger-state]").forEach(function (button) {
            const active = (button.dataset.ledgerState || "") === state.ledgerState;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-pressed", String(active));
        });
    }

    function updateActiveFilterCount() {
        const values = [
            elements.warehouse?.value,
            elements.transactionType?.value,
            elements.referenceType?.value,
            elements.fromDate?.value,
            elements.toDate?.value,
            elements.referenceCode?.value,
            state.ledgerState
        ];
        const count = values.filter(function (value) { return value !== undefined && value !== null && value !== ""; }).length;
        elements.activeFilterCount.textContent = String(count);
        elements.activeFilterCount.classList.toggle("d-none", count === 0);
    }

    function resolveTransactionLabel(item) {
        return item.transactionTypeLabel || transactionLabels[item.transactionType] || "Giao dịch kho";
    }

    function resolveReferenceLabel(item) {
        return item.referenceTypeLabel || referenceLabels[item.referenceType] || "Nguồn khác";
    }

    function formatDateOnly(value) {
        if (!value) return "—";
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return "—";
        return date.toLocaleDateString("vi-VN");
    }

    function formatNumber(value) {
        return Number(value || 0).toLocaleString("vi-VN", {
            minimumFractionDigits: 0,
            maximumFractionDigits: 3
        });
    }

    function formatSignedNumber(value) {
        const number = Number(value || 0);
        return number > 0 ? `+${formatNumber(number)}` : formatNumber(number);
    }

    function quantityClass(value) {
        const number = Number(value || 0);
        return number > 0 ? "is-positive" : number < 0 ? "is-negative" : "is-neutral";
    }

    function renderLoadingState() {
        setCostVisibility(false);
        elements.resultsPanel?.setAttribute("aria-busy", "true");
        elements.desktopBody.innerHTML = '<tr><td colspan="6"><div class="gds-empty"><span class="spinner-border spinner-border-sm me-2"></span>Đang tải dữ liệu...</div></td></tr>';
        elements.mobileList.innerHTML = '<div class="gds-empty"><span class="spinner-border spinner-border-sm me-2"></span>Đang tải dữ liệu...</div>';
    }

    function renderErrorState(message) {
        setCostVisibility(false);
        const content = renderEmptyState(message, true);
        elements.desktopBody.innerHTML = `<tr><td colspan="6">${content}</td></tr>`;
        elements.mobileList.innerHTML = content;
        elements.resultSummary.textContent = "Không tải được dữ liệu.";
        elements.pagination.innerHTML = "";
        elements.resultsPanel?.setAttribute("aria-busy", "false");
    }

    function renderEmptyState(message, danger) {
        return `<div class="gds-empty${danger ? " text-danger" : ""}"><div class="gds-empty__icon"><i class="bx ${danger ? "bx-error-circle" : "bx-transfer"}"></i></div><div class="fw-semibold">${escapeHtml(message)}</div></div>`;
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
        return escapeHtml(value).replaceAll("`", "&#96;");
    }

    document.addEventListener("error", function (event) {
        const image = event.target;
        if (!(image instanceof HTMLImageElement) || !image.matches("[data-image-fallback]")) return;
        const parent = image.parentElement;
        image.remove();
        parent?.insertAdjacentHTML("afterbegin", '<i class="bx bx-image" aria-hidden="true"></i>');
    }, true);
})();
