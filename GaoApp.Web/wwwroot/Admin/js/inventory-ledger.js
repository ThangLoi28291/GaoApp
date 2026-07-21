(function () {
    const api = {
        warehouses: "/admin/api/warehouses/select2?term=",
        ledger: "/admin/api/inventory/ledger"
    };

    const state = {
        page: 1,
        pageSize: 20,
        totalPages: 1,
        totalItems: 0
    };

    const els = {
        form: document.getElementById("inventoryLedgerFilterForm"),

        warehouseId: document.getElementById("WarehouseId"),
        productVariantId: document.getElementById("ProductVariantId"),
        transactionType: document.getElementById("TransactionType"),
        referenceType: document.getElementById("ReferenceType"),
        fromDate: document.getElementById("FromDate"),
        toDate: document.getElementById("ToDate"),
        referenceId: document.getElementById("ReferenceId"),
        keyword: document.getElementById("Keyword"),
        sortBy: document.getElementById("SortBy"),
        sortDirection: document.getElementById("SortDirection"),
        onlyNegativeAfterTransaction: document.getElementById("OnlyNegativeAfterTransaction"),

        btnReload: document.getElementById("btnReloadLedger"),
        btnResetFilter: document.getElementById("btnResetFilter"),
        btnPrevPage: document.getElementById("btnPrevPage"),
        btnNextPage: document.getElementById("btnNextPage"),

        tableBody: document.getElementById("inventoryLedgerTableBody"),
        pagingInfo: document.getElementById("pagingInfo"),
        resultInfo: document.getElementById("resultInfo"),

        sumTotalItems: document.getElementById("sumTotalItems"),
        sumNegativeItems: document.getElementById("sumNegativeItems"),
        sumIncreaseItems: document.getElementById("sumIncreaseItems"),
        sumCurrentPage: document.getElementById("sumCurrentPage")
    };

    document.addEventListener("DOMContentLoaded", async function () {
        bindEvents();
        await loadWarehouses();
        await loadLedger();
    });

    function bindEvents() {
        if (els.form) {
            els.form.addEventListener("submit", async function (e) {
                e.preventDefault();
                state.page = 1;
                await loadLedger();
            });
        }

        if (els.btnReload) {
            els.btnReload.addEventListener("click", async function () {
                await loadLedger();
            });
        }

        if (els.btnResetFilter) {
            els.btnResetFilter.addEventListener("click", async function () {
                resetFilter();
                state.page = 1;
                await loadLedger();
            });
        }

        if (els.btnPrevPage) {
            els.btnPrevPage.addEventListener("click", async function () {
                if (state.page <= 1) return;
                state.page--;
                await loadLedger();
            });
        }

        if (els.btnNextPage) {
            els.btnNextPage.addEventListener("click", async function () {
                if (state.page >= state.totalPages) return;
                state.page++;
                await loadLedger();
            });
        }
    }

    async function loadWarehouses() {
        try {
            const response = await fetch(api.warehouses, {
                method: "GET",
                headers: { "Accept": "application/json" }
            });

            if (!response.ok) {
                throw new Error("Không tải được danh sách kho.");
            }

            const data = await response.json();
            const items = data.results || [];

            els.warehouseId.innerHTML = `<option value="">-- Tất cả kho --</option>`;

            items.forEach(item => {
                const option = document.createElement("option");
                option.value = item.id;
                option.textContent = item.text;
                els.warehouseId.appendChild(option);
            });
        } catch (error) {
            console.error(error);
        }
    }

    async function loadLedger() {
        setLoading();

        const params = buildQueryParams();

        try {
            const response = await fetch(`${api.ledger}?${params.toString()}`, {
                method: "GET",
                headers: { "Accept": "application/json" }
            });

            if (!response.ok) {
                throw new Error("Không tải được dữ liệu ledger.");
            }

            const result = await response.json();

            state.totalItems = result.totalItems || 0;
            state.totalPages = result.totalPages || 1;
            state.page = result.page || 1;

            const items = result.items || [];

            renderTable(items);
            renderSummary(items);
            renderPaging();
        } catch (error) {
            console.error(error);
            els.tableBody.innerHTML = `
                <tr>
                    <td colspan="11" class="ledger-empty text-danger">Không tải được dữ liệu ledger.</td>
                </tr>
            `;
            els.resultInfo.textContent = "Có lỗi khi tải dữ liệu.";
        }
    }

    function buildQueryParams() {
        const params = new URLSearchParams({
            page: state.page,
            pageSize: state.pageSize
        });

        const warehouseId = (els.warehouseId.value || "").trim();
        const productVariantId = (els.productVariantId.value || "").trim();
        const transactionType = (els.transactionType.value || "").trim();
        const referenceType = (els.referenceType.value || "").trim();
        const fromDate = (els.fromDate.value || "").trim();
        const toDate = (els.toDate.value || "").trim();
        const referenceId = (els.referenceId.value || "").trim();
        const keyword = (els.keyword.value || "").trim();
        const sortBy = (els.sortBy.value || "").trim();
        const sortDirection = (els.sortDirection.value || "").trim();
        const onlyNegativeAfterTransaction = !!els.onlyNegativeAfterTransaction.checked;

        if (warehouseId) params.append("warehouseId", warehouseId);
        if (productVariantId) params.append("productVariantId", productVariantId);
        if (transactionType) params.append("transactionType", transactionType);
        if (referenceType) params.append("referenceType", referenceType);
        if (fromDate) params.append("fromDate", fromDate);
        if (toDate) params.append("toDate", toDate);
        if (referenceId) params.append("referenceId", referenceId);
        if (keyword) params.append("keyword", keyword);
        if (sortBy) params.append("sortBy", sortBy);
        if (sortDirection) params.append("sortDirection", sortDirection);
        if (onlyNegativeAfterTransaction) params.append("onlyNegativeAfterTransaction", "true");

        return params;
    }

    function renderTable(items) {
        if (!items.length) {
            els.tableBody.innerHTML = `
                <tr>
                    <td colspan="11" class="ledger-empty">Không có dữ liệu ledger phù hợp.</td>
                </tr>
            `;
            return;
        }

        const html = items.map(item => {
            const quantityChange = Number(item.quantityChange || 0);
            const afterQty = Number(item.afterQty || 0);

            return `
                <tr>
                    <td>${formatDateTime(item.occurredAtUtc)}</td>
                    <td>${escapeHtml(item.warehouseName || "")}</td>
                    <td class="ledger-product-name">
                        ${escapeHtml(item.productVariantName || "")}
                        <span class="ledger-product-sub">
                            SKU: ${escapeHtml(item.sku || "")}
                            ${item.barcode ? ` | Barcode: ${escapeHtml(item.barcode)}` : ""}
                        </span>
                    </td>
                    <td>${renderTransactionType(item)}</td>
                    <td>${renderReferenceType(item)}</td>
                    <td>
                        <div class="ledger-ref-block">
                            <div class="ledger-ref-id">${escapeHtml(item.referenceId || "") || "-"}</div>
                            ${item.referenceLineId ? `<div class="ledger-ref-line">Line: ${item.referenceLineId}</div>` : ""}
                        </div>
                    </td>
                    <td class="text-end ledger-qty-neutral">${formatNumber(item.beforeQty)}</td>
                    <td class="text-end ${quantityChange >= 0 ? "ledger-qty-positive" : "ledger-qty-negative"}">
                        ${formatSignedNumber(quantityChange)}
                    </td>
                    <td class="text-end ${afterQty < 0 ? "ledger-qty-negative" : "ledger-qty-neutral"}">
                        ${formatNumber(afterQty)}
                    </td>
                    <td>
                        ${item.isNegativeAfterTransaction
                    ? `<span class="ledger-badge ledger-badge-negative">Âm sau GD</span>`
                    : `<span class="ledger-badge ledger-badge-normal">Bình thường</span>`}
                    </td>
                    <td class="ledger-note">${escapeHtml(item.note || "") || "-"}</td>
                </tr>
            `;
        }).join("");

        els.tableBody.innerHTML = html;
    }

    function renderTransactionType(item) {
        const isIncrease = Number(item.quantityChange || 0) >= 0;
        const cssClass = isIncrease ? "ledger-badge-increase" : "ledger-badge-decrease";

        return `<span class="ledger-badge ${cssClass}">
            ${escapeHtml(item.transactionTypeName || "")}
        </span>`;
    }

    function renderReferenceType(item) {
        return escapeHtml(item.referenceTypeName || "") || "-";
    }

    function renderSummary(items) {
        const negativeCount = items.filter(x => x.isNegativeAfterTransaction).length;
        const increaseCount = items.filter(x => Number(x.quantityChange || 0) > 0).length;

        els.sumTotalItems.textContent = formatNumber(state.totalItems);
        els.sumNegativeItems.textContent = formatNumber(negativeCount);
        els.sumIncreaseItems.textContent = formatNumber(increaseCount);
        els.sumCurrentPage.textContent = formatNumber(state.page);
    }

    function renderPaging() {
        els.pagingInfo.textContent = `Trang ${state.page} / ${state.totalPages}`;
        els.resultInfo.textContent = `Tổng ${formatNumber(state.totalItems)} dòng kết quả.`;

        els.btnPrevPage.disabled = state.page <= 1;
        els.btnNextPage.disabled = state.page >= state.totalPages;
    }

    function resetFilter() {
        els.form.reset();

        // Reset về mặc định nghiệp vụ
        if (els.sortBy) {
            els.sortBy.value = "TransactionDate";
        }

        if (els.sortDirection) {
            els.sortDirection.value = "desc";
        }
    }

    function setLoading() {
        els.tableBody.innerHTML = `
            <tr>
                <td colspan="11" class="ledger-empty">Đang tải dữ liệu...</td>
            </tr>
        `;
        els.resultInfo.textContent = "Đang tải dữ liệu...";
    }

    function formatDateTime(value) {
        if (!value) return "";
        const d = new Date(value);
        if (isNaN(d.getTime())) return value;

        return d.toLocaleString("vi-VN", {
            year: "numeric",
            month: "2-digit",
            day: "2-digit",
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit"
        });
    }

    function formatNumber(value) {
        const number = Number(value || 0);
        return number.toLocaleString("vi-VN", {
            minimumFractionDigits: 0,
            maximumFractionDigits: 3
        });
    }

    function formatSignedNumber(value) {
        const number = Number(value || 0);
        const formatted = formatNumber(number);
        return number > 0 ? `+${formatted}` : formatted;
    }

    function escapeHtml(value) {
        return String(value || "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }
})();