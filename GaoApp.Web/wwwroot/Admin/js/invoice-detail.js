(function () {
    "use strict";

    const modalElement = document.getElementById("manualDetailModal");
    const modalContent = document.getElementById("manualDetailModalContent");
    const openButton = document.getElementById("btnOpenManualDetailModal");
    const summaryBox = document.getElementById("invoiceSummaryBox");
    const manualLinesBox = document.getElementById("manualLinesBox");
    const deleteModalElement = document.getElementById("deleteManualDetailModal");

    const modal = modalElement && modalContent && window.bootstrap
        ? new bootstrap.Modal(modalElement)
        : null;

    const deleteModal = deleteModalElement && window.bootstrap
        ? new bootstrap.Modal(deleteModalElement)
        : null;

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

    function getInvoiceHeadId() {
        const page = document.getElementById("invoiceDetailPage");

        return openButton?.getAttribute("data-invoice-head-id")
            || page?.getAttribute("data-invoice-head-id")
            || "";
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

    // =====================================================
    // BUYER INFO - PHASE 21.2.2.3
    // =====================================================

    function bindBuyerInfoEvents() {
        const form = document.getElementById("buyerInfoForm");

        if (!form) {
            return;
        }

        const typeRadios = form.querySelectorAll(".buyer-type-radio");
        const lookupButton = document.getElementById("btnLookupBuyerTaxCode");
        const saveButton = document.getElementById("btnSaveBuyerInfo");
        const taxCodeInput = document.getElementById("buyerTaxCode");

        typeRadios.forEach(function (radio) {
            radio.addEventListener("change", function () {
                applyBuyerTypeUi();
            });
        });

        lookupButton?.addEventListener("click", function () {
            lookupBuyerByTaxCode();
        });

        taxCodeInput?.addEventListener("keydown", function (event) {
            if (event.key === "Enter") {
                event.preventDefault();
                lookupBuyerByTaxCode();
            }
        });

        saveButton?.addEventListener("click", function () {
            saveBuyerInfo();
        });

        applyBuyerTypeUi();
    }

    function getSelectedBuyerType() {
        const checked = document.querySelector('input[name="BuyerType"]:checked');

        return checked ? checked.value : "NoInvoice";
    }

    function applyBuyerTypeUi() {
        const buyerType = getSelectedBuyerType();

        const fieldsWrap = document.querySelector(".buyer-fields");
        const taxCodeWrap = document.querySelector(".buyer-tax-code-wrap");
        const nameWrap = document.querySelector(".buyer-name-wrap");
        const legalNameWrap = document.querySelector(".buyer-legal-name-wrap");
        const phoneWrap = document.querySelector(".buyer-phone-wrap");
        const nameLabel = document.getElementById("buyerNameLabel");

        const taxCodeInput = document.getElementById("buyerTaxCode");
        const buyerNameInput = document.getElementById("buyerName");
        const legalNameInput = document.getElementById("buyerLegalName");
        const addressInput = document.getElementById("buyerAddress");
        const emailInput = document.getElementById("buyerEmail");
        const phoneInput = document.getElementById("buyerPhone");
        const saveToProfileInput = document.getElementById("buyerSaveToProfile");

        hideBuyerMessage();

        if (!fieldsWrap) {
            return;
        }

        if (buyerType === "NoInvoice") {
            fieldsWrap.classList.add("opacity-50");

            taxCodeWrap?.classList.add("d-none");
            nameWrap?.classList.add("d-none");
            legalNameWrap?.classList.add("d-none");
            phoneWrap?.classList.add("d-none");

            clearBuyerInput(taxCodeInput);
            clearBuyerInput(buyerNameInput);
            clearBuyerInput(legalNameInput);
            clearBuyerInput(addressInput);
            clearBuyerInput(emailInput);
            clearBuyerInput(phoneInput);

            setBuyerInputsDisabled(true);

            if (saveToProfileInput) {
                saveToProfileInput.checked = false;
                saveToProfileInput.disabled = true;
            }

            return;
        }

        fieldsWrap.classList.remove("opacity-50");

        taxCodeWrap?.classList.remove("d-none");
        nameWrap?.classList.remove("d-none");
        phoneWrap?.classList.remove("d-none");

        setBuyerInputsDisabled(false);

        if (saveToProfileInput) {
            saveToProfileInput.disabled = false;
            saveToProfileInput.checked = true;
        }

        if (buyerType === "Individual") {
            if (nameLabel) {
                nameLabel.innerHTML = 'Tên khách hàng <span class="text-danger">*</span>';
            }

            legalNameWrap?.classList.add("d-none");
            clearBuyerInput(legalNameInput);
            return;
        }

        if (buyerType === "Business") {
            if (nameLabel) {
                nameLabel.textContent = "Người liên hệ / người mua";
            }

            legalNameWrap?.classList.remove("d-none");
        }
    }

    function setBuyerInputsDisabled(disabled) {
        const ids = [
            "buyerTaxCode",
            "buyerName",
            "buyerLegalName",
            "buyerAddress",
            "buyerEmail",
            "buyerPhone"
        ];

        ids.forEach(function (id) {
            const input = document.getElementById(id);

            if (input) {
                input.disabled = disabled;
            }
        });
    }

    function clearBuyerInput(input) {
        if (input) {
            input.value = "";
        }
    }

    async function lookupBuyerByTaxCode() {
        const form = document.getElementById("buyerInfoForm");

        if (!form) {
            return;
        }

        const lookupUrl = form.dataset.lookupUrl;
        const invoiceHeadId = document.getElementById("buyerInvoiceHeadId")?.value || "";
        const buyerType = getSelectedBuyerType();
        const taxCodeInput = document.getElementById("buyerTaxCode");
        const lookupButton = document.getElementById("btnLookupBuyerTaxCode");

        const taxCode = (taxCodeInput?.value || "").trim();

        hideBuyerMessage();

        if (buyerType === "NoInvoice") {
            showBuyerMessage("info", "Không lấy hóa đơn thì không cần tra cứu thông tin người mua.");
            return;
        }

        if (!taxCode) {
            showBuyerMessage("warning", "Vui lòng nhập MST/mã định danh để tra cứu.");
            taxCodeInput?.focus();
            return;
        }

        if (!isBuyerTaxCodeClientValid(taxCode)) {
            showBuyerMessage("warning", "MST/mã định danh chỉ cho phép chữ, số, dấu gạch ngang và tối đa 20 ký tự.");
            taxCodeInput?.focus();
            return;
        }

        if (!lookupUrl) {
            showBuyerMessage("danger", "Thiếu đường dẫn tra cứu người mua.");
            return;
        }

        const url = new URL(lookupUrl, window.location.origin);

        url.searchParams.set("invoiceHeadId", invoiceHeadId);
        url.searchParams.set("buyerType", buyerType);
        url.searchParams.set("taxCode", taxCode);

        const oldText = lookupButton ? lookupButton.innerHTML : "";

        try {
            if (lookupButton) {
                lookupButton.disabled = true;
                lookupButton.innerHTML = "Đang tra...";
            }

            const response = await fetch(url.toString(), {
                method: "GET",
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            });

            const data = await readResponseOnce(response);
            const json = data.json;

            if (!response.ok || !json?.success) {
                showBuyerMessage(
                    "warning",
                    json?.message || data.message || "Không tìm thấy thông tin người mua trong nội bộ. Bạn có thể tự nhập."
                );
                return;
            }

            fillBuyerInfoFromLookup(json);

            showBuyerMessage(
                "success",
                "Đã tìm thấy thông tin người mua từ nguồn: " + (json.source || "nội bộ") + ". Vui lòng kiểm tra lại rồi bấm Lưu."
            );
        } catch (error) {
            console.error(error);
            showBuyerMessage("danger", "Có lỗi khi tra cứu thông tin người mua.");
        } finally {
            if (lookupButton) {
                lookupButton.disabled = false;
                lookupButton.innerHTML = oldText || "Tra cứu";
            }
        }
    }

    function fillBuyerInfoFromLookup(data) {
        const buyerType = data.buyerType || getSelectedBuyerType();

        setBuyerTypeRadio(buyerType);
        applyBuyerTypeUi();

        const taxCodeInput = document.getElementById("buyerTaxCode");
        const buyerNameInput = document.getElementById("buyerName");
        const legalNameInput = document.getElementById("buyerLegalName");
        const addressInput = document.getElementById("buyerAddress");
        const emailInput = document.getElementById("buyerEmail");
        const phoneInput = document.getElementById("buyerPhone");
        const sourceInput = document.getElementById("buyerSource");

        if (taxCodeInput) {
            taxCodeInput.value = data.buyerTaxCode || taxCodeInput.value || "";
        }

        if (buyerType === "Business") {
            if (legalNameInput) {
                legalNameInput.value = data.buyerLegalName || data.buyerName || "";
            }

            if (buyerNameInput) {
                buyerNameInput.value = data.buyerName || "";
            }
        } else if (buyerType === "Individual") {
            if (buyerNameInput) {
                buyerNameInput.value = data.buyerName || data.buyerLegalName || "";
            }

            if (legalNameInput) {
                legalNameInput.value = "";
            }
        }

        if (addressInput) {
            addressInput.value = data.buyerAddress || "";
        }

        if (emailInput) {
            emailInput.value = data.buyerEmail || "";
        }

        if (phoneInput) {
            phoneInput.value = data.buyerPhone || "";
        }

        if (sourceInput) {
            sourceInput.value = data.source || "manual";
        }
    }

    function setBuyerTypeRadio(buyerType) {
        const radio = document.querySelector('input[name="BuyerType"][value="' + buyerType + '"]');

        if (radio) {
            radio.checked = true;
        }
    }

    async function saveBuyerInfo() {
        const form = document.getElementById("buyerInfoForm");

        if (!form) {
            return;
        }

        if (isInvoiceLocked()) {
            showBuyerMessage("warning", "Hóa đơn đã khóa hoặc đã phát hành, không thể sửa thông tin người mua.");
            return;
        }

        const updateUrl = form.dataset.updateUrl;
        const saveButton = document.getElementById("btnSaveBuyerInfo");

        hideBuyerMessage();

        const clientValidation = validateBuyerInfoClient();

        if (!clientValidation.ok) {
            showBuyerMessage("warning", clientValidation.message);
            clientValidation.focusElement?.focus();
            return;
        }

        if (!updateUrl) {
            showBuyerMessage("danger", "Thiếu đường dẫn lưu thông tin người mua.");
            return;
        }

        const formData = new FormData(form);

        if (!formData.has("SaveToProfile")) {
            formData.append("SaveToProfile", "false");
        }

        const oldText = saveButton ? saveButton.innerHTML : "";

        try {
            if (saveButton) {
                saveButton.disabled = true;
                saveButton.innerHTML = "Đang lưu...";
            }

            const response = await fetch(updateUrl, {
                method: "POST",
                body: formData,
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            });

            const data = await readResponseOnce(response);
            const json = data.json;

            if (!response.ok || !json?.success) {
                showBuyerMessage(
                    "danger",
                    json?.message || data.message || "Không cập nhật được thông tin người mua."
                );
                return;
            }

            showBuyerMessage("success", json.message || "Đã cập nhật thông tin người mua.");

            setTimeout(function () {
                window.location.reload();
            }, 600);
        } catch (error) {
            console.error(error);
            showBuyerMessage("danger", "Có lỗi khi lưu thông tin người mua.");
        } finally {
            if (saveButton) {
                saveButton.disabled = false;
                saveButton.innerHTML = oldText || "Lưu thông tin người mua";
            }
        }
    }

    function validateBuyerInfoClient() {
        const buyerType = getSelectedBuyerType();

        const buyerNameInput = document.getElementById("buyerName");
        const legalNameInput = document.getElementById("buyerLegalName");
        const taxCodeInput = document.getElementById("buyerTaxCode");

        const buyerName = (buyerNameInput?.value || "").trim();
        const legalName = (legalNameInput?.value || "").trim();
        const taxCode = (taxCodeInput?.value || "").trim();

        if (buyerType === "NoInvoice") {
            return { ok: true };
        }

        if (taxCode && !isBuyerTaxCodeClientValid(taxCode)) {
            return {
                ok: false,
                message: "MST/mã định danh chỉ cho phép chữ, số, dấu gạch ngang và tối đa 20 ký tự.",
                focusElement: taxCodeInput
            };
        }

        if (buyerType === "Individual" && !buyerName) {
            return {
                ok: false,
                message: "Vui lòng nhập tên khách hàng cá nhân.",
                focusElement: buyerNameInput
            };
        }

        if (buyerType === "Business" && !legalName) {
            return {
                ok: false,
                message: "Vui lòng nhập tên đơn vị/công ty/hộ kinh doanh.",
                focusElement: legalNameInput
            };
        }

        return { ok: true };
    }

    function isBuyerTaxCodeClientValid(value) {
        value = (value || "")
            .trim()
            .replaceAll(" ", "")
            .replaceAll(".", "");

        if (!value) {
            return true;
        }

        if (value.length > 20) {
            return false;
        }

        return /^[A-Za-z0-9-]+$/.test(value);
    }

    function showBuyerMessage(type, message) {
        const box = document.getElementById("buyerLookupMessage");

        if (!box) {
            alert(message);
            return;
        }

        box.className = "alert alert-" + type + " small mb-3";
        box.textContent = message;
        box.classList.remove("d-none");
    }

    function hideBuyerMessage() {
        const box = document.getElementById("buyerLookupMessage");

        if (!box) {
            return;
        }

        box.classList.add("d-none");
        box.textContent = "";
    }

    // =====================================================
    // MANUAL DETAIL
    // =====================================================

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

        if (!modalElement || !modalContent || !modal) {
            showError("Không tìm thấy popup thêm dòng manual.");
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

                modal?.hide();

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

            searchTimer = setTimeout(function () {
                searchProducts(keyword);
            }, 250);
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

        menu.innerHTML = currentItems.map(function (item, index) {
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

        rows.forEach(function (x) {
            x.classList.remove("active");
        });

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

        setTimeout(function () {
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
        if (!summaryBox) {
            return;
        }

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
        if (!manualLinesBox) {
            return;
        }

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

    // =====================================================
    // GLOBAL BINDINGS
    // =====================================================

    document.addEventListener("keydown", function (e) {
        const isManualModalOpen = modalElement?.classList.contains("show") === true;

        if (e.key === "F2" && !isManualModalOpen) {
            e.preventDefault();

            if (isInvoiceLocked()) {
                showError("Hóa đơn đã khóa, không thể thêm dòng manual.");
                return;
            }

            loadCreateManualModal();
            return;
        }

        if (e.ctrlKey && e.key.toLowerCase() === "m" && !isManualModalOpen) {
            e.preventDefault();

            if (isInvoiceLocked()) {
                showError("Hóa đơn đã khóa, không thể thêm dòng manual.");
                return;
            }

            loadCreateManualModal();
            return;
        }

        if (e.key === "Escape" && isManualModalOpen) {
            e.preventDefault();
            modal?.hide();
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

    bindBuyerInfoEvents();
    bindManualDeleteButtons();
    bindManualEditRows();
})();