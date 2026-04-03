(function () {
    const api = {
        warehouses: "/admin/api/warehouses/select2?term=",
        currentBalances: "/admin/api/inventory/current-balances"
    };

    const state = {
        page: 1,
        pageSize: 20,
        totalPages: 1,
        totalItems: 0
    };

    const els = {
        form: document.getElementById("inventoryInquiryFilterForm"),

        warehouseId: document.getElementById("WarehouseId"),
        productVariantId: document.getElementById("ProductVariantId"),
        keyword: document.getElementById("Keyword"),
        onlyNegative: document.getElementById("OnlyNegative"),
        onlyPositive: document.getElementById("OnlyPositive"),
        sortBy: document.getElementById("SortBy"),
        sortDirection: document.getElementById("SortDirection"),

        btnReload: document.getElementById("btnReloadBalances"),
        btnResetFilter: document.getElementById("btnResetFilter"),
        btnPrevPage: document.getElementById("btnPrevPage"),
        btnNextPage: document.getElementById("btnNextPage"),

        tableBody: document.getElementById("inventoryInquiryTableBody"),
        pagingInfo: document.getElementById("pagingInfo"),
        resultInfo: document.getElementById("resultInfo"),

        sumTotalItems: document.getElementById("sumTotalItems"),
        sumNegativeItems: document.getElementById("sumNegativeItems"),
        sumPositiveItems: document.getElementById("sumPositiveItems"),
        sumCurrentPage: document.getElementById("sumCurrentPage")
    };

    document.addEventListener("DOMContentLoaded", async function () {
        bindEvents();
        await loadWarehouses();
        await loadCurrentBalances();
    });

    function bindEvents() {
        if (els.form) {
            els.form.addEventListener("submit", async function (e) {
                e.preventDefault();
                state.page = 1;
                await loadCurrentBalances();
            });
        }

        if (els.btnReload) {
            els.btnReload.addEventListener("click", async function () {
                await loadCurrentBalances();
            });
        }

        if (els.btnResetFilter) {
            els.btnResetFilter.addEventListener("click", async function () {
                resetFilter();
                state.page = 1;
                await loadCurrentBalances();
            });
        }

        if (els.btnPrevPage) {
            els.btnPrevPage.addEventListener("click", async function () {
                if (state.page <= 1) return;
                state.page--;
                await loadCurrentBalances();
            });
        }

        if (els.btnNextPage) {
            els.btnNextPage.addEventListener("click", async function () {
                if (state.page >= state.totalPages) return;
                state.page++;
                await loadCurrentBalances();
            });
        }

        // Giúp UX rõ hơn:
        // Nếu chọn "âm kho" thì tự bỏ "dương"
        if (els.onlyNegative) {
            els.onlyNegative.addEventListener("change", function () {
                if (els.onlyNegative.checked && els.onlyPositive.checked) {
                    els.onlyPositive.checked = false;
                }
            });
        }

        // Nếu chọn "dương" thì tự bỏ "âm kho"
        if (els.onlyPositive) {
            els.onlyPositive.addEventListener("change", function () {
                if (els.onlyPositive.checked && els.onlyNegative.checked) {
                    els.onlyNegative.checked = false;
                }
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

    async function loadCurrentBalances() {
        setLoading();

        const params = buildQueryParams();

        try {
            const response = await fetch(`${api.currentBalances}?${params.toString()}`, {
                method: "GET",
                headers: { "Accept": "application/json" }
            });

            if (!response.ok) {
                throw new Error("Không tải được dữ liệu tồn kho.");
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
                    <td colspan="8" class="iq-empty text-danger">Không tải được dữ liệu tồn kho.</td>
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
        const keyword = (els.keyword.value || "").trim();
        const onlyNegative = !!els.onlyNegative.checked;
        const onlyPositive = !!els.onlyPositive.checked;
        const sortBy = (els.sortBy.value || "").trim();
        const sortDirection = (els.sortDirection.value || "").trim();

        if (warehouseId) {
            params.append("warehouseId", warehouseId);
        }

        if (productVariantId) {
            params.append("productVariantId", productVariantId);
        }

        if (keyword) {
            params.append("keyword", keyword);
        }

        if (onlyNegative) {
            params.append("onlyNegative", "true");
        }

        if (onlyPositive) {
            params.append("onlyPositive", "true");
        }

        if (sortBy) {
            params.append("sortBy", sortBy);
        }

        if (sortDirection) {
            params.append("sortDirection", sortDirection);
        }

        return params;
    }

    function renderTable(items) {
        if (!items.length) {
            els.tableBody.innerHTML = `
                <tr>
                    <td colspan="8" class="iq-empty">Không có dữ liệu tồn kho phù hợp.</td>
                </tr>
            `;
            return;
        }

        const html = items.map(item => {
            const onHandQty = Number(item.onHandQty || 0);
            const availableQty = Number(item.availableQty || 0);

            return `
                <tr>
                    <td>${escapeHtml(item.warehouseName || "")}</td>
                    <td class="iq-product-name">
                        ${escapeHtml(item.productVariantName || "")}
                    </td>
                    <td>${escapeHtml(item.sku || "")}</td>
                    <td>${escapeHtml(item.barcode || "") || "-"}</td>
                    <td class="text-end ${onHandQty < 0 ? "iq-qty-negative" : onHandQty > 0 ? "iq-qty-positive" : "iq-qty-neutral"}">
                        ${formatNumber(item.onHandQty)}
                    </td>
                    <td class="text-end iq-qty-neutral">${formatNumber(item.reservedQty)}</td>
                    <td class="text-end ${availableQty < 0 ? "iq-qty-negative" : availableQty > 0 ? "iq-qty-positive" : "iq-qty-neutral"}">
                        ${formatNumber(item.availableQty)}
                    </td>
                    <td>
                        ${item.isNegative
                    ? `<span class="iq-badge iq-badge-negative">Âm kho</span>`
                    : `<span class="iq-badge iq-badge-normal">Bình thường</span>`}
                    </td>
                </tr>
            `;
        }).join("");

        els.tableBody.innerHTML = html;
    }

    function renderSummary(items) {
        const negativeCount = items.filter(x => x.isNegative).length;
        const positiveCount = items.filter(x => Number(x.onHandQty || 0) > 0).length;

        els.sumTotalItems.textContent = formatNumber(state.totalItems);
        els.sumNegativeItems.textContent = formatNumber(negativeCount);
        els.sumPositiveItems.textContent = formatNumber(positiveCount);
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

        if (els.sortBy) {
            els.sortBy.value = "ProductName";
        }

        if (els.sortDirection) {
            els.sortDirection.value = "asc";
        }
    }

    function setLoading() {
        els.tableBody.innerHTML = `
            <tr>
                <td colspan="8" class="iq-empty">Đang tải dữ liệu...</td>
            </tr>
        `;
        els.resultInfo.textContent = "Đang tải dữ liệu...";
    }

    function formatNumber(value) {
        const number = Number(value || 0);
        return number.toLocaleString("vi-VN", {
            minimumFractionDigits: 0,
            maximumFractionDigits: 3
        });
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