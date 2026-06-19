(function () {
    "use strict";

    const modalElement = document.getElementById("manualDetailModal");
    const modalContent = document.getElementById("manualDetailModalContent");
    const openButton = document.getElementById("btnOpenManualDetailModal");
    const summaryBox = document.getElementById("invoiceSummaryBox");
    const manualLinesBox = document.getElementById("manualLinesBox");
    const deleteModalElement = document.getElementById("deleteManualDetailModal");
    const deleteModal = deleteModalElement ? new bootstrap.Modal(deleteModalElement) : null;

    if (!modalElement || !modalContent) return;

    const modal = new bootstrap.Modal(modalElement);

    let searchTimer = null;
    let searchSeq = 0;
    let activeIndex = -1;
    let currentItems = [];

    function isInvoiceLocked() {
        const page = document.getElementById("invoiceDetailPage");
        return page?.getAttribute("data-is-locked") === "true";
    }
    function isAdjustmentAmountInvoice() {
        const page = document.getElementById("invoiceDetailPage");
        return page?.getAttribute("data-is-adjustment-amount-invoice") === "true";
    }

    function applyManualQuantityRule() {
        const quantityInput = document.getElementById("manualQuantity");

        if (!quantityInput) {
            return;
        }

        if (isAdjustmentAmountInvoice()) {
            quantityInput.removeAttribute("min");
            quantityInput.setAttribute("step", "0.001");
            quantityInput.setAttribute("placeholder", "VD: -1 để điều chỉnh giảm");
        } else {
            quantityInput.setAttribute("min", "0.001");
            quantityInput.setAttribute("step", "0.001");
            quantityInput.setAttribute("placeholder", "VD: 1");
        }
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function formatMoney(value) {
        const n = Number(value || 0);
        return n.toLocaleString("vi-VN");
    }

    function showError(message) {
        alert(message || "Có lỗi xảy ra.");
    }

    function getInvoiceHeadId() {
        const page = document.getElementById("invoiceDetailPage");
        return openButton?.getAttribute("data-invoice-head-id")
            || page?.getAttribute("data-invoice-head-id")
            || "";
    }

    function bindManualDeleteButtons() {
        document.querySelectorAll("[data-delete-manual-detail-id]").forEach(function (btn) {
            btn.onclick = function () {
                if (isInvoiceLocked()) {
                    showError("Hóa đơn đã khóa, không thể xóa dòng manual.");
                    return;
                }

                const id = btn.getAttribute("data-delete-manual-detail-id");
                const name = btn.getAttribute("data-delete-manual-detail-name") || "";

                const idInput = document.getElementById("deleteManualDetailId");
                const nameEl = document.getElementById("deleteManualDetailName");

                if (idInput) idInput.value = id;
                if (nameEl) nameEl.textContent = name;

                deleteModal?.show();
            };
        });
    }

    async function deleteManualDetail() {
        if (isInvoiceLocked()) {
            showError("Hóa đơn đã khóa, không thể xóa dòng manual.");
            return;
        }

        const idInput = document.getElementById("deleteManualDetailId");
        const btn = document.getElementById("btnConfirmDeleteManualDetail");
        const invoiceDetailId = Number(idInput?.value || 0);

        if (!invoiceDetailId) {
            showError("Không xác định được dòng cần xóa.");
            return;
        }

        const oldText = btn ? btn.innerHTML : "";

        if (btn) {
            btn.disabled = true;
            btn.innerHTML = "Đang xóa...";
        }

        try {
            const tokenInput = document.querySelector("#invoiceAntiForgeryForm input[name='__RequestVerificationToken']");
            const formData = new FormData();

            formData.append("invoiceDetailId", invoiceDetailId);

            if (tokenInput) {
                formData.append("__RequestVerificationToken", tokenInput.value);
            }

            const response = await fetch("/Admin/Invoice/DeleteManualDetail", {
                method: "POST",
                body: formData,
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            });

            const data = await readResponseOnce(response);

            if (!response.ok) {
                showError(data.message || "Không xóa được dòng manual.");
                return;
            }

            if (!data.json?.success) {
                showError(data.json?.message || "Xóa manual line thất bại.");
                return;
            }

            deleteModal?.hide();

            await reloadInvoiceSummary(data.json.invoiceHeadId);
            await reloadManualLines(data.json.invoiceHeadId);
        } catch (err) {
            console.error(err);
            showError("Lỗi kết nối khi xóa dòng manual.");
        } finally {
            if (btn) {
                btn.disabled = false;
                btn.innerHTML = oldText;
            }
        }
    }

    async function loadCreateManualModal() {
        if (isInvoiceLocked()) {
            showError("Hóa đơn đã khóa, không thể thêm dòng manual.");
            return;
        }

        const invoiceHeadId = getInvoiceHeadId();

        const response = await fetch(`/Admin/Invoice/CreateManualDetail?invoiceHeadId=${encodeURIComponent(invoiceHeadId)}`, {
            method: "GET",
            headers: {
                "X-Requested-With": "XMLHttpRequest"
            }
        });

        if (!response.ok) {
            showError("Không tải được popup thêm dòng manual.");
            return;
        }

        modalContent.innerHTML = await response.text();

        applyManualQuantityRule();

        bindManualForm();
        bindManualProductSearch();

        modalElement.addEventListener("shown.bs.modal", function focusManualSearchOnce() {
            const input = document.getElementById("manualProductSearch");

            if (input) {
                input.focus();
                input.select();
            }

            modalElement.removeEventListener("shown.bs.modal", focusManualSearchOnce);
        });

        modal.show();
    }

    function bindManualEditRows() {
        document.querySelectorAll(".manual-detail-row").forEach(function (row) {
            row.ondblclick = function (e) {
                if (isInvoiceLocked()) return;
                if (row.classList.contains("is-locked")) return;
                if (e.target.closest("button")) return;

                startEditManualRow(row);
            };
        });
    }

    function startEditManualRow(row) {
        if (!row || row.classList.contains("is-editing")) return;

        cancelOtherEditingRows();

        row.classList.add("is-editing");

        const detailId = row.getAttribute("data-manual-detail-id");
        const quantity = row.getAttribute("data-manual-quantity") || "1";
        const unitPrice = row.getAttribute("data-manual-unit-price") || "0";
        const vatRate = row.getAttribute("data-manual-vat-rate") || "0";
        const note = row.getAttribute("data-manual-note") || "";

        row.dataset.originalHtml = row.innerHTML;

        const quantityCell = row.querySelector("[data-manual-display='quantity']");
        const unitPriceCell = row.querySelector("[data-manual-display='unitPrice']");
        const vatRateCell = row.querySelector("[data-manual-display='vatRate']");
        const noteCell = row.querySelector("[data-manual-display='note']");

        if (quantityCell) {
            const minAttr = isAdjustmentAmountInvoice()
                ? ""
                : `min="0.001"`;

            const placeholderAttr = isAdjustmentAmountInvoice()
                ? `placeholder="VD: -1"`
                : `placeholder="VD: 1"`;

            quantityCell.innerHTML = `
        <input type="number"
               class="form-control form-control-sm text-end manual-edit-input"
               data-manual-edit-field="quantity"
               ${minAttr}
               step="0.001"
               ${placeholderAttr}
               value="${escapeHtml(quantity)}" />
    `;
        }

        if (unitPriceCell) {
            unitPriceCell.innerHTML = `
                <input type="number"
                       class="form-control form-control-sm text-end manual-edit-input"
                       data-manual-edit-field="unitPrice"
                       min="0"
                       step="1"
                       value="${escapeHtml(unitPrice)}" />
            `;
        }

        if (vatRateCell) {
            vatRateCell.innerHTML = `
                <input type="number"
                       class="form-control form-control-sm text-end manual-edit-input"
                       data-manual-edit-field="vatRate"
                       min="0"
                       step="0.01"
                       value="${escapeHtml(vatRate)}" />
            `;
        }

        if (noteCell) {
            noteCell.innerHTML = `
                <input type="text"
                       class="form-control form-control-sm manual-edit-input"
                       data-manual-edit-field="note"
                       value="${escapeHtml(note)}" />
            `;
        }

        const actionCell = row.querySelector("td:last-child");

        if (actionCell) {
            actionCell.innerHTML = `
                <div class="d-flex gap-1 justify-content-end">
                    <button type="button"
                            class="btn btn-sm btn-success"
                            data-save-manual-edit-id="${escapeHtml(detailId)}">
                        Lưu
                    </button>

                    <button type="button"
                            class="btn btn-sm btn-outline-secondary"
                            data-cancel-manual-edit-id="${escapeHtml(detailId)}">
                        Hủy
                    </button>
                </div>
            `;
        }

        bindManualEditRowEvents(row);

        const firstInput = row.querySelector("[data-manual-edit-field='quantity']");
        firstInput?.focus();
        firstInput?.select();
    }

    function bindManualEditRowEvents(row) {
        row.querySelectorAll(".manual-edit-input").forEach(function (input) {
            input.addEventListener("keydown", async function (e) {
                if (e.key === "Enter") {
                    e.preventDefault();
                    await saveManualEditRow(row);
                    return;
                }

                if (e.key === "Escape") {
                    e.preventDefault();
                    cancelManualEditRow(row);
                }
            });
        });

        row.querySelector("[data-save-manual-edit-id]")?.addEventListener("click", async function () {
            await saveManualEditRow(row);
        });

        row.querySelector("[data-cancel-manual-edit-id]")?.addEventListener("click", function () {
            cancelManualEditRow(row);
        });
    }

    async function saveManualEditRow(row) {
        if (isInvoiceLocked()) {
            showError("Hóa đơn đã khóa, không thể sửa dòng manual.");
            return;
        }

        const detailId = Number(row.getAttribute("data-manual-detail-id") || 0);

        if (!detailId) {
            showError("Không xác định được dòng cần cập nhật.");
            return;
        }

        const quantity = Number(row.querySelector("[data-manual-edit-field='quantity']")?.value || 0);
        const unitPrice = Number(row.querySelector("[data-manual-edit-field='unitPrice']")?.value || 0);
        const vatRate = Number(row.querySelector("[data-manual-edit-field='vatRate']")?.value || 0);
        const note = row.querySelector("[data-manual-edit-field='note']")?.value || "";

        if (quantity === 0) {
            showError("Số lượng không được bằng 0.");
            return;
        }

        if (!isAdjustmentAmountInvoice() && quantity < 0) {
            showError("Chỉ hóa đơn điều chỉnh tiền mới được nhập số lượng âm.");
            return;
        }

        if (unitPrice < 0) {
            showError("Đơn giá không được âm.");
            return;
        }

        if (vatRate < 0) {
            showError("VAT không được âm.");
            return;
        }

        const tokenInput = document.querySelector("#invoiceAntiForgeryForm input[name='__RequestVerificationToken']");
        const formData = new FormData();

        formData.append("InvoiceDetailId", detailId);
        formData.append("Quantity", quantity);
        formData.append("UnitPrice", unitPrice);
        formData.append("VatRate", vatRate);
        formData.append("Note", note);

        if (tokenInput) {
            formData.append("__RequestVerificationToken", tokenInput.value);
        }

        row.classList.add("is-saving");

        try {
            const response = await fetch("/Admin/Invoice/UpdateManualDetail", {
                method: "POST",
                body: formData,
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            });

            const data = await readResponseOnce(response);

            if (!response.ok) {
                showError(data.message || "Không cập nhật được dòng manual.");
                return;
            }

            if (!data.json?.success) {
                showError(data.json?.message || "Cập nhật dòng manual thất bại.");
                return;
            }

            await reloadInvoiceSummary(data.json.invoiceHeadId);
            await reloadManualLines(data.json.invoiceHeadId);
        } catch (err) {
            console.error(err);
            showError("Lỗi kết nối khi cập nhật dòng manual.");
        } finally {
            row.classList.remove("is-saving");
        }
    }

    function cancelManualEditRow(row) {
        if (!row || !row.dataset.originalHtml) return;

        row.innerHTML = row.dataset.originalHtml;
        row.classList.remove("is-editing");
        row.classList.remove("is-saving");

        bindManualDeleteButtons();
        bindManualEditRows();
    }

    function cancelOtherEditingRows() {
        document.querySelectorAll(".manual-detail-row.is-editing").forEach(function (row) {
            cancelManualEditRow(row);
        });
    }

    function bindManualForm() {
        const form = document.getElementById("manualDetailForm");
        if (!form) return;

        form.addEventListener("submit", async function (e) {
            e.preventDefault();

            if (isInvoiceLocked()) {
                showError("Hóa đơn đã khóa, không thể thêm dòng manual.");
                return;
            }
            const quantityInput = document.getElementById("manualQuantity");
            const unitPriceInput = document.getElementById("manualUnitPrice");
            const vatRateInput = document.getElementById("manualVatRate");

            const quantity = Number(quantityInput?.value || 0);
            const unitPrice = Number(unitPriceInput?.value || 0);
            const vatRate = Number(vatRateInput?.value || 0);

            if (quantity === 0) {
                showError("Số lượng không được bằng 0.");
                quantityInput?.focus();
                return;
            }

            if (!isAdjustmentAmountInvoice() && quantity < 0) {
                showError("Chỉ hóa đơn điều chỉnh tiền mới được nhập số lượng âm.");
                quantityInput?.focus();
                return;
            }

            if (unitPrice < 0) {
                showError("Đơn giá không được âm. Muốn điều chỉnh giảm thì nhập số lượng âm.");
                unitPriceInput?.focus();
                return;
            }

            if (vatRate < 0 || vatRate > 100) {
                showError("VAT không hợp lệ.");
                vatRateInput?.focus();
                return;
            }
            const submitButton = form.querySelector("button[type='submit']");
            const oldText = submitButton ? submitButton.innerHTML : "";

            if (submitButton) {
                submitButton.disabled = true;
                submitButton.innerHTML = "Đang lưu...";
            }

            try {
                const response = await fetch(form.action, {
                    method: "POST",
                    body: new FormData(form),
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    }
                });

                const data = await readResponseOnce(response);

                if (!response.ok) {
                    showError(data.message || "Không thêm được dòng manual.");
                    return;
                }

                if (!data.json?.success) {
                    showError(data.json?.message || "Thêm dòng manual thất bại.");
                    return;
                }

                modal.hide();

                await reloadInvoiceSummary(data.json.invoiceHeadId);
                await reloadManualLines(data.json.invoiceHeadId);
            } catch (err) {
                console.error(err);
                showError("Lỗi kết nối khi thêm dòng manual.");
            } finally {
                if (submitButton) {
                    submitButton.disabled = false;
                    submitButton.innerHTML = oldText;
                }
            }
        });
    }

    function bindManualProductSearch() {
        const input = document.getElementById("manualProductSearch");
        const menu = document.getElementById("manualProductAutocomplete");

        if (!input || !menu) return;

        input.addEventListener("input", function () {
            clearTimeout(searchTimer);

            const keyword = input.value.trim();

            if (keyword.length < 2) {
                hideMenu();
                return;
            }

            searchTimer = setTimeout(() => searchProducts(keyword), 250);
        });

        input.addEventListener("keydown", function (e) {
            if (!currentItems.length) return;

            if (e.key === "ArrowDown") {
                e.preventDefault();
                setActive(activeIndex + 1);
                return;
            }

            if (e.key === "ArrowUp") {
                e.preventDefault();
                setActive(activeIndex - 1);
                return;
            }

            if (e.key === "Enter") {
                e.preventDefault();

                if (activeIndex >= 0 && activeIndex < currentItems.length) {
                    selectItem(currentItems[activeIndex]);
                }

                return;
            }

            if (e.key === "Escape") {
                hideMenu();
            }
        });

        menu.addEventListener("click", function (e) {
            const row = e.target.closest("[data-invoice-product-index]");
            if (!row) return;

            const index = Number(row.getAttribute("data-invoice-product-index") || -1);
            if (index < 0 || index >= currentItems.length) return;

            selectItem(currentItems[index]);
        });

        document.addEventListener("click", function (e) {
            if (input.contains(e.target) || menu.contains(e.target)) return;
            hideMenu();
        }, { once: false });
    }

    async function searchProducts(keyword) {
        const menu = document.getElementById("manualProductAutocomplete");
        if (!menu) return;

        const seq = ++searchSeq;

        menu.innerHTML = `
            <div class="invoice-ac-loading">
                <span class="spinner-border spinner-border-sm me-2"></span>
                Đang tìm: <strong>${escapeHtml(keyword)}</strong>
            </div>
        `;
        menu.style.display = "block";

        try {
            const response = await fetch(`/Admin/Invoice/SearchProductVariants?keyword=${encodeURIComponent(keyword)}`, {
                method: "GET",
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            });

            if (seq !== searchSeq) return;

            if (!response.ok) {
                renderEmpty(keyword);
                return;
            }

            const items = await response.json();

            if (seq !== searchSeq) return;

            currentItems = Array.isArray(items) ? items : [];
            activeIndex = -1;

            renderItems(keyword);
        } catch {
            if (seq !== searchSeq) return;
            renderEmpty(keyword);
        }
    }

    function renderItems(keyword) {
        const menu = document.getElementById("manualProductAutocomplete");
        if (!menu) return;

        if (!currentItems.length) {
            renderEmpty(keyword);
            return;
        }

        menu.innerHTML = currentItems.map((item, index) => {
            const price = formatMoney(item.unitPrice || 0);

            return `
                <button type="button"
                        class="invoice-ac-item"
                        data-invoice-product-index="${index}">
                    <div class="invoice-ac-main">
                        <div class="invoice-ac-title">
                            ${escapeHtml(item.text || item.displayName || "")}
                        </div>

                        <div class="invoice-ac-meta">
                            <span>SKU: <strong>${escapeHtml(item.sku || "-")}</strong></span>
                            <span>Barcode: <strong>${escapeHtml(item.barcode || "-")}</strong></span>
                            <span>ĐVT: <strong>${escapeHtml(item.unitName || "-")}</strong></span>
                        </div>
                    </div>

                    <div class="invoice-ac-price">${price}</div>
                </button>
            `;
        }).join("");

        menu.style.display = "block";
        setActive(0);
    }

    function renderEmpty(keyword) {
        const menu = document.getElementById("manualProductAutocomplete");
        if (!menu) return;

        currentItems = [];
        activeIndex = -1;

        menu.innerHTML = `
            <div class="invoice-ac-empty">
                Không tìm thấy sản phẩm cho: <strong>${escapeHtml(keyword)}</strong>
            </div>
        `;
        menu.style.display = "block";
    }

    function setActive(index) {
        const menu = document.getElementById("manualProductAutocomplete");
        if (!menu) return;

        const rows = menu.querySelectorAll("[data-invoice-product-index]");

        if (!rows.length) {
            activeIndex = -1;
            return;
        }

        if (index < 0) index = 0;
        if (index >= rows.length) index = rows.length - 1;

        activeIndex = index;

        rows.forEach(x => x.classList.remove("active"));
        rows[index].classList.add("active");
        rows[index].scrollIntoView({ block: "nearest" });
    }

    function selectItem(item) {
        const variantIdInput = document.getElementById("manualProductVariantId");
        const productSearchInput = document.getElementById("manualProductSearch");
        const itemNameInput = document.getElementById("manualItemName");
        const unitNameInput = document.getElementById("manualUnitName");
        const unitPriceInput = document.getElementById("manualUnitPrice");

        const name = item.text || item.displayName || "";

        if (variantIdInput) variantIdInput.value = item.id || item.productVariantId || "";
        if (productSearchInput) productSearchInput.value = name;
        if (itemNameInput) itemNameInput.value = name;
        if (unitNameInput) unitNameInput.value = item.unitName || "";
        if (unitPriceInput) unitPriceInput.value = item.unitPrice || 0;

        hideMenu();

        setTimeout(() => {
            document.getElementById("manualQuantity")?.focus();
            document.getElementById("manualQuantity")?.select();
        }, 50);
    }

    function hideMenu() {
        const menu = document.getElementById("manualProductAutocomplete");
        if (!menu) return;

        menu.style.display = "none";
        menu.innerHTML = "";
        currentItems = [];
        activeIndex = -1;
    }

    async function reloadInvoiceSummary(invoiceHeadId) {
        const response = await fetch(`/Admin/Invoice/InvoiceSummaryPartial?id=${encodeURIComponent(invoiceHeadId)}`, {
            method: "GET",
            headers: {
                "X-Requested-With": "XMLHttpRequest"
            }
        });

        if (!response.ok) {
            showError("Không cập nhật được tổng tiền hóa đơn.");
            return;
        }

        summaryBox.innerHTML = await response.text();
    }

    async function reloadManualLines(invoiceHeadId) {
        const response = await fetch(`/Admin/Invoice/ManualLinesPartial?id=${encodeURIComponent(invoiceHeadId)}`, {
            method: "GET",
            headers: {
                "X-Requested-With": "XMLHttpRequest"
            }
        });

        if (!response.ok) {
            showError("Không cập nhật được danh sách manual line.");
            return;
        }

        manualLinesBox.innerHTML = await response.text();

        bindManualDeleteButtons();
        bindManualEditRows();
    }

    async function postInvoiceLockAction(url, reason) {
        const page = document.getElementById("invoiceDetailPage");
        const invoiceHeadId = Number(page?.getAttribute("data-invoice-head-id") || 0);

        if (!invoiceHeadId) {
            showError("Không xác định được hóa đơn.");
            return;
        }

        const tokenInput = document.querySelector("#invoiceAntiForgeryForm input[name='__RequestVerificationToken']");
        const formData = new FormData();

        formData.append("invoiceHeadId", invoiceHeadId);
        formData.append("reason", reason || "");

        if (tokenInput) {
            formData.append("__RequestVerificationToken", tokenInput.value);
        }

        const response = await fetch(url, {
            method: "POST",
            body: formData,
            headers: {
                "X-Requested-With": "XMLHttpRequest"
            }
        });

        const data = await readResponseOnce(response);

        if (!response.ok) {
            showError(data.message || "Thao tác thất bại.");
            return;
        }

        if (!data.json?.success) {
            showError(data.json?.message || "Thao tác thất bại.");
            return;
        }

        window.location.reload();
    }

    async function readResponseOnce(response) {
        const contentType = response.headers.get("content-type") || "";

        if (contentType.includes("application/json")) {
            const json = await response.json();
            return {
                json,
                text: "",
                message: json?.message || ""
            };
        }

        const text = await response.text();

        return {
            json: null,
            text,
            message: text || ""
        };
    }

    document.addEventListener("keydown", function (e) {
        const isModalOpen = modalElement.classList.contains("show");

        if (e.key === "F2" && !isModalOpen) {
            e.preventDefault();

            if (isInvoiceLocked()) {
                showError("Hóa đơn đã khóa, không thể thêm dòng manual.");
                return;
            }

            loadCreateManualModal();
            return;
        }

        if (e.ctrlKey && e.key.toLowerCase() === "m" && !isModalOpen) {
            e.preventDefault();

            if (isInvoiceLocked()) {
                showError("Hóa đơn đã khóa, không thể thêm dòng manual.");
                return;
            }

            loadCreateManualModal();
            return;
        }

        if (e.key === "Escape" && isModalOpen) {
            e.preventDefault();
            modal.hide();
        }
    });

    openButton?.addEventListener("click", loadCreateManualModal);

    document
        .getElementById("btnConfirmDeleteManualDetail")
        ?.addEventListener("click", deleteManualDetail);

    document.getElementById("btnLockInvoice")?.addEventListener("click", async function () {
        const reason = prompt("Nhập lý do khóa hóa đơn:", "Khóa hóa đơn để chốt/sync kế toán.");
        if (reason === null) return;

        await postInvoiceLockAction("/Admin/Invoice/Lock", reason);
    });

    document.getElementById("btnUnlockInvoice")?.addEventListener("click", async function () {
        const reason = prompt("Nhập lý do mở khóa:", "Mở khóa để điều chỉnh hóa đơn.");
        if (reason === null) return;

        await postInvoiceLockAction("/Admin/Invoice/Unlock", reason);
    });

    bindManualDeleteButtons();
    bindManualEditRows();
})();