(function () {
    const api = {
        warehouses: "/admin/api/warehouses",

        // Adjustment
        createAdjustment: "/admin/api/inventory-adjustments",

        // Lookup
        productLookupSelect2: "/admin/api/inventory-adjustments/product-lookup-select2",
        barcodeLookup: "/admin/api/inventory-adjustments/barcode-lookup",

        // Inventory query
        negativeBalances: "/admin/api/inventory/negative-balances",
        adjustmentHistory: "/admin/api/inventory/adjustment-history",
        negativeLogs: "/admin/api/inventory/negative-logs",
        balanceItem: "/admin/api/inventory/balance-item"
    };

    const state = {
        balanceItem: null
    };

    const els = {
        form: document.getElementById("inventoryAdjustmentForm"),
        warehouseId: document.getElementById("WarehouseId"),
        adjustmentType: document.getElementById("AdjustmentType"),
        quantity: document.getElementById("Quantity"),
        note: document.getElementById("Note"),

        lookupInput: $("#AdjustmentLookupInput"),

        selectedProductVariantId: document.getElementById("SelectedProductVariantId"),
        selectedUnitId: document.getElementById("SelectedUnitId"),
        selectedFactor: document.getElementById("SelectedFactor"),
        selectedUnitName: document.getElementById("SelectedUnitName"),

        btnSubmit: document.getElementById("btnSubmitAdjustment"),
        btnReset: document.getElementById("btnResetForm"),
        btnReloadAll: document.getElementById("btnReloadAll"),

        negativeBalancesBody: document.getElementById("negativeBalancesBody"),
        adjustmentHistoryBody: document.getElementById("adjustmentHistoryBody"),
        negativeLogsBody: document.getElementById("negativeLogsBody"),

        sumNegativeBalances: document.getElementById("sumNegativeBalances"),
        sumAdjustmentHistory: document.getElementById("sumAdjustmentHistory"),
        sumNegativeLogs: document.getElementById("sumNegativeLogs"),

        currentProductName: document.getElementById("CurrentProductName"),
        currentOnHandQty: document.getElementById("CurrentOnHandQty"),
        currentReservedQty: document.getElementById("CurrentReservedQty"),
        currentAvailableQty: document.getElementById("CurrentAvailableQty"),
        currentAllowNegative: document.getElementById("CurrentAllowNegative"),

        currentSelectedUnitName: document.getElementById("CurrentSelectedUnitName"),
        currentSelectedFactor: document.getElementById("CurrentSelectedFactor"),
        currentBaseQuantityPreview: document.getElementById("CurrentBaseQuantityPreview"),

        previewAfterQty: document.getElementById("PreviewAfterQty"),
        previewWarning: document.getElementById("PreviewWarning"),
        previewBox: document.getElementById("AdjustmentPreviewBox")
    };

    document.addEventListener("DOMContentLoaded", async function () {
        bindEvents();
        initAdjustmentLookupSelect2();
        await loadInitialData();
    });

    function bindEvents() {
        if (els.form) {
            els.form.addEventListener("submit", onSubmitAdjustment);
        }

        if (els.btnReset) {
            els.btnReset.addEventListener("click", resetForm);
        }

        if (els.btnReloadAll) {
            els.btnReloadAll.addEventListener("click", async function () {
                await reloadAllData();
                toastSuccess("Đã tải lại dữ liệu.");
            });
        }

        if (els.warehouseId) {
            els.warehouseId.addEventListener("change", onSelectionChanged);
        }

        if (els.adjustmentType) {
            els.adjustmentType.addEventListener("change", updatePreview);
        }

        if (els.quantity) {
            els.quantity.addEventListener("input", updatePreview);
        }
    }

    async function loadInitialData() {
        try {
            await loadWarehouses();
            await reloadAllData();
            renderSelectedUnitInfo();
            updatePreview();
        } catch (error) {
            console.error(error);
            toastError(getErrorMessage(error));
        }
    }

    async function reloadAllData() {
        await Promise.all([
            loadNegativeBalances(),
            loadAdjustmentHistory(),
            loadNegativeLogs()
        ]);
    }

    async function loadWarehouses() {
        const response = await fetch(api.warehouses, {
            method: "GET",
            headers: { "Accept": "application/json" }
        });

        if (!response.ok) {
            throw new Error("Không tải được danh sách kho.");
        }

        const data = await response.json();
        const items = data?.results || data || [];

        els.warehouseId.innerHTML = `<option value="">-- Chọn kho --</option>`;

        (items || []).forEach(item => {
            const option = document.createElement("option");
            option.value = item.id;
            option.textContent = item.text || item.name || "";
            els.warehouseId.appendChild(option);
        });
    }

    function initAdjustmentLookupSelect2() {
        const el = $('#AdjustmentLookupInput');
        if (!el.length) return;

        if (!window.jQuery || !$.fn.select2) {
            console.error('Select2 chưa được load cho AdjustmentLookupInput');
            return;
        }

        el.select2({
            theme: 'bootstrap-5',
            width: '100%',
            placeholder: 'Gõ tên sản phẩm / SKU / barcode...',
            minimumInputLength: 1,
            allowClear: true,
            ajax: {
                url: api.productLookupSelect2,
                dataType: 'json',
                delay: 150,
                data: function (params) {
                    return { term: params.term || '' };
                },
                processResults: function (data) {
                    return data;
                }
            },
            templateResult: formatAdjustmentLookupResult,
            templateSelection: formatAdjustmentLookupSelection,
            escapeMarkup: function (markup) {
                return markup;
            }
        });

        el.on('select2:select', function (e) {
            const item = e.params.data;

            els.selectedProductVariantId.value = item.productVariantId || '';
            els.selectedUnitId.value = item.unitId || '';
            els.selectedFactor.value = item.factor || 1;
            els.selectedUnitName.value = item.unitName || '';

            renderSelectedUnitInfo();
            onSelectionChanged();
        });

        el.on('select2:clear', function () {
            clearSelectedLookup();
            clearBalanceInfo();
            updatePreview();
        });

        bindAdjustmentLookupEnterAutoSelect();
    }

    function bindAdjustmentLookupEnterAutoSelect() {
        $(document).on('keydown', '.select2-container--open .select2-search__field', async function (e) {
            if (e.key !== 'Enter') return;

            const term = $(this).val();
            if (!term) return;

            try {
                const response = await fetch(`${api.productLookupSelect2}?term=${encodeURIComponent(term)}`);
                const data = await response.json();

                if (!data || !data.results || data.results.length !== 1) return;

                e.preventDefault();

                const item = data.results[0];
                const el = $('#AdjustmentLookupInput');

                const option = new Option(item.text, item.id, true, true);
                el.append(option).trigger('change');
                el.trigger({
                    type: 'select2:select',
                    params: { data: item }
                });

                el.select2('close');
            } catch (error) {
                console.error(error);
            }
        });
    }

    function formatAdjustmentLookupResult(item) {
        if (!item.id) return item.text || '';

        const title = escapeHtml(item.productName || '');

        const meta = [
            item.sku ? 'SKU: ' + escapeHtml(item.sku) : '',
            item.unitName ? 'Đơn vị: ' + escapeHtml(item.unitName) : '',
            item.factor ? 'x' + item.factor : '',
            item.barcode ? 'BC: ' + escapeHtml(item.barcode) : '',
            item.isBaseUnitFallback ? 'Đơn vị gốc' : '',
            item.sourceType ? escapeHtml(item.sourceType) : ''
        ].filter(Boolean).join(' | ');

        return `
            <div>
                <div class="select2-result-title">${title}</div>
                <div class="select2-result-meta">${meta}</div>
            </div>
        `;
    }

    function formatAdjustmentLookupSelection(item) {
        if (!item || !item.id) return item.text || 'Chọn sản phẩm';

        const parts = [];
        if (item.productName) parts.push(item.productName);
        if (item.unitName) parts.push(item.unitName);
        if (item.factor) parts.push('x' + item.factor);
        if (item.barcode) parts.push(item.barcode);

        return parts.join(' | ');
    }

    async function onSelectionChanged() {
        const warehouseId = parseInt(els.warehouseId.value || "0", 10);
        const productVariantId = parseInt(els.selectedProductVariantId.value || "0", 10);

        if (!warehouseId || !productVariantId) {
            clearBalanceInfo();
            updatePreview();
            return;
        }

        try {
            const response = await fetch(`${api.balanceItem}?warehouseId=${warehouseId}&productVariantId=${productVariantId}`, {
                method: "GET",
                headers: { "Accept": "application/json" }
            });

            if (!response.ok) {
                throw new Error("Không tải được tồn hiện tại.");
            }

            const item = await response.json();
            state.balanceItem = item;
            renderBalanceInfo(item);
            updatePreview();
        } catch (error) {
            console.error(error);
            clearBalanceInfo();
            toastError(getErrorMessage(error));
        }
    }

    function renderBalanceInfo(item) {
        els.currentProductName.textContent = item.productVariantName || "-";
        els.currentOnHandQty.textContent = formatNumber(item.onHandQty);
        els.currentReservedQty.textContent = formatNumber(item.reservedQty);
        els.currentAvailableQty.textContent = formatNumber(item.availableQty);
        els.currentAllowNegative.textContent = item.allowNegativeInventory ? "Có" : "Không";
    }

    function clearBalanceInfo() {
        state.balanceItem = null;
        els.currentProductName.textContent = "-";
        els.currentOnHandQty.textContent = "0";
        els.currentReservedQty.textContent = "0";
        els.currentAvailableQty.textContent = "0";
        els.currentAllowNegative.textContent = "-";
    }

    function renderSelectedUnitInfo() {
        const unitName = els.selectedUnitName.value || '-';
        const factor = Number(els.selectedFactor.value || 1);

        if (els.currentSelectedUnitName) {
            els.currentSelectedUnitName.textContent = unitName;
        }

        if (els.currentSelectedFactor) {
            els.currentSelectedFactor.textContent = formatNumber(factor);
        }
    }

    function clearSelectedLookup() {
        els.selectedProductVariantId.value = '';
        els.selectedUnitId.value = '';
        els.selectedFactor.value = '';
        els.selectedUnitName.value = '';

        if (els.currentSelectedUnitName) {
            els.currentSelectedUnitName.textContent = '-';
        }

        if (els.currentSelectedFactor) {
            els.currentSelectedFactor.textContent = '1';
        }

        if (els.currentBaseQuantityPreview) {
            els.currentBaseQuantityPreview.textContent = '0';
        }
    }

    function updatePreview() {
        const onHandQty = Number(state.balanceItem?.onHandQty || 0);
        const allowNegative = Boolean(state.balanceItem?.allowNegativeInventory);
        const adjustmentType = parseInt(els.adjustmentType.value || "0", 10);
        const quantity = Number(els.quantity.value || 0);
        const factor = Number(els.selectedFactor.value || 1);

        const baseQty = quantity > 0 ? quantity * factor : 0;

        if (els.currentBaseQuantityPreview) {
            els.currentBaseQuantityPreview.textContent = formatNumber(baseQty);
        }

        if (!adjustmentType || !quantity) {
            els.previewAfterQty.textContent = formatNumber(onHandQty);
            els.previewWarning.textContent = "Chưa có dữ liệu để tính.";
            els.previewWarning.className = "mt-2 text-muted";
            setPreviewBoxState("normal");
            return;
        }

        const quantityChange = adjustmentType === 30 ? baseQty : -baseQty;
        const afterQty = onHandQty + quantityChange;

        els.previewAfterQty.textContent = formatNumber(afterQty);

        if (afterQty < 0) {
            if (allowNegative) {
                els.previewWarning.textContent = `Sau điều chỉnh, tồn kho sẽ âm. Kho này cho phép âm kho. SL gốc quy đổi: ${formatNumber(baseQty)}.`;
                els.previewWarning.className = "mt-2 text-warning fw-semibold";
                setPreviewBoxState("warning");
            } else {
                els.previewWarning.textContent = `Sau điều chỉnh, tồn kho sẽ âm và kho này không cho phép âm kho. Lưu sẽ bị chặn. SL gốc quy đổi: ${formatNumber(baseQty)}.`;
                els.previewWarning.className = "mt-2 text-danger fw-semibold";
                setPreviewBoxState("danger");
            }
            return;
        }

        els.previewWarning.textContent = `Sau điều chỉnh, tồn kho vẫn hợp lệ. SL gốc quy đổi: ${formatNumber(baseQty)}.`;
        els.previewWarning.className = "mt-2 text-success fw-semibold";
        setPreviewBoxState("success");
    }

    function setPreviewBoxState(stateName) {
        if (!els.previewBox) return;

        els.previewBox.classList.remove("text-danger-soft", "text-warning-soft", "text-success-soft");

        if (stateName === "danger") {
            els.previewBox.classList.add("text-danger-soft");
            return;
        }

        if (stateName === "warning") {
            els.previewBox.classList.add("text-warning-soft");
            return;
        }

        if (stateName === "success") {
            els.previewBox.classList.add("text-success-soft");
        }
    }

    async function onSubmitAdjustment(e) {
        e.preventDefault();

        const warehouseId = parseInt(els.warehouseId.value || "0", 10);
        const productVariantId = parseInt(els.selectedProductVariantId.value || "0", 10);
        const unitId = parseInt(els.selectedUnitId.value || "0", 10) || null;
        const adjustmentType = parseInt(els.adjustmentType.value || "0", 10);
        const quantity = parseFloat(els.quantity.value || "0");
        const note = (els.note.value || "").trim();

        // 👉 NEW: đọc thêm cost từ UI
        const unitCost = parseFloat(document.getElementById("UnitCost")?.value || "0");
        const provisionalUnitCost = parseFloat(document.getElementById("ProvisionalUnitCost")?.value || "0");

        if (!warehouseId) {
            toastWarning("Vui lòng chọn kho.");
            els.warehouseId.focus();
            return;
        }

        if (!productVariantId) {
            toastWarning("Vui lòng chọn sản phẩm.");
            return;
        }

        if (!quantity || quantity <= 0) {
            toastWarning("Số lượng phải lớn hơn 0.");
            els.quantity.focus();
            return;
        }

        // 👉 VALIDATION mức 2
        if (adjustmentType === 30) { // AdjustmentIncrease
            if (!unitCost || unitCost <= 0) {
                toastWarning("Điều chỉnh tăng bắt buộc nhập giá vốn > 0.");
                document.getElementById("UnitCost")?.focus();
                return;
            }
        }

        const payload = {
            warehouseId: warehouseId,
            productVariantId: productVariantId,
            unitId: unitId,
            adjustmentType: adjustmentType,
            quantity: quantity,
            note: note,

            // 👉 NEW: gửi lên backend
            unitCost: adjustmentType === 30 ? unitCost : null,
            provisionalUnitCost: adjustmentType === 31 ? provisionalUnitCost : null
        };

        try {
            setSubmitting(true);

            const response = await fetch(api.createAdjustment, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                const errorText = await tryReadErrorResponse(response);
                throw new Error(errorText || "Không thể tạo điều chỉnh kho.");
            }

            const result = await response.json();

            toastSuccess(result.message || "Điều chỉnh kho thành công.");
            resetForm();
            await reloadAllData();
        } catch (error) {
            console.error(error);
            toastError(getErrorMessage(error));
        } finally {
            setSubmitting(false);
        }
    }

    function resetForm() {
        if (els.form) {
            els.form.reset();
        }

        if (els.adjustmentType) {
            els.adjustmentType.value = "30";
        }

        clearSelectedLookup();
        clearBalanceInfo();
        updatePreview();

        const select2 = $('#AdjustmentLookupInput');
        if (select2.length) {
            select2.val(null).trigger('change');
        }
    }

    async function loadNegativeBalances() {
        setLoading(els.negativeBalancesBody, 5);

        const response = await fetch(api.negativeBalances, {
            method: "GET",
            headers: { "Accept": "application/json" }
        });

        if (!response.ok) {
            throw new Error("Không tải được danh sách tồn âm.");
        }

        const items = await response.json();
        renderNegativeBalances(items || []);
    }

    async function loadAdjustmentHistory() {
        setLoading(els.adjustmentHistoryBody, 9);

        const response = await fetch(api.adjustmentHistory, {
            method: "GET",
            headers: { "Accept": "application/json" }
        });

        if (!response.ok) {
            throw new Error("Không tải được lịch sử điều chỉnh.");
        }

        const items = await response.json();
        renderAdjustmentHistory(items || []);
    }

    async function loadNegativeLogs() {
        setLoading(els.negativeLogsBody, 8);

        const response = await fetch(api.negativeLogs, {
            method: "GET",
            headers: { "Accept": "application/json" }
        });

        if (!response.ok) {
            throw new Error("Không tải được log âm kho.");
        }

        const items = await response.json();
        renderNegativeLogs(items || []);
    }

    function renderNegativeBalances(items) {
        els.sumNegativeBalances.textContent = items.length.toString();

        if (!items.length) {
            els.negativeBalancesBody.innerHTML = `
                <tr>
                    <td colspan="5" class="inv-empty">Hiện chưa có sản phẩm âm kho.</td>
                </tr>
            `;
            return;
        }

        const html = items.map(item => `
            <tr>
                <td>${escapeHtml(item.warehouseName || "")}</td>
                <td class="inv-product-name">${escapeHtml(item.productVariantName || "")}</td>
                <td class="text-end text-danger fw-bold">${formatNumber(item.onHandQty)}</td>
                <td class="text-end">${formatNumber(item.reservedQty)}</td>
                <td class="text-end">${formatNumber(item.availableQty)}</td>
            </tr>
        `).join("");

        els.negativeBalancesBody.innerHTML = html;
    }

    function renderAdjustmentHistory(items) {
        els.sumAdjustmentHistory.textContent = items.length.toString();

        if (!items.length) {
            els.adjustmentHistoryBody.innerHTML = `
                <tr>
                    <td colspan="9" class="inv-empty">Chưa có lịch sử điều chỉnh kho.</td>
                </tr>
            `;
            return;
        }

        const html = items.map(item => {
            const isIncrease = item.transactionType === 30;
            const unitName = extractUnitNameFromNote(item.note);

            return `
                <tr>
                    <td>${formatDateTime(item.occurredAtUtc)}</td>
                    <td>${escapeHtml(item.warehouseName || "")}</td>
                    <td class="inv-product-name">${escapeHtml(item.productVariantName || "")}</td>
                    <td>
                        <span class="${isIncrease ? "inv-badge-increase" : "inv-badge-decrease"}">
                            ${isIncrease ? "Tăng tồn" : "Giảm tồn"}
                        </span>
                    </td>
                    <td>${escapeHtml(unitName)}</td>
                    <td class="text-end">${formatNumber(item.beforeQty)}</td>
                    <td class="text-end ${isIncrease ? "text-success" : "text-warning"} fw-bold">
                        ${formatSignedNumber(item.quantityChange)}
                    </td>
                    <td class="text-end">${formatNumber(item.afterQty)}</td>
                    <td>${escapeHtml(item.note || "")}</td>
                </tr>
            `;
        }).join("");

        els.adjustmentHistoryBody.innerHTML = html;
    }

    function renderNegativeLogs(items) {
        els.sumNegativeLogs.textContent = items.length.toString();

        if (!items.length) {
            els.negativeLogsBody.innerHTML = `
                <tr>
                    <td colspan="8" class="inv-empty">Chưa có log âm kho.</td>
                </tr>
            `;
            return;
        }

        const html = items.map(item => `
            <tr>
                <td>${formatDateTime(item.occurredAtUtc)}</td>
                <td>${escapeHtml(item.warehouseName || "")}</td>
                <td class="inv-product-name">${escapeHtml(item.productVariantName || "")}</td>
                <td><span class="inv-badge-negative">${mapTransactionType(item.transactionType)}</span></td>
                <td class="text-end">${formatNumber(item.beforeQty)}</td>
                <td class="text-end text-warning fw-bold">${formatSignedNumber(item.quantityChange)}</td>
                <td class="text-end text-danger fw-bold">${formatNumber(item.afterQty)}</td>
                <td>${escapeHtml(item.note || "")}</td>
            </tr>
        `).join("");

        els.negativeLogsBody.innerHTML = html;
    }

    function extractUnitNameFromNote(note) {
        const text = String(note || "");
        const marker = "Đơn vị:";
        const idx = text.indexOf(marker);
        if (idx < 0) return "-";

        const start = idx + marker.length;
        const rest = text.substring(start).trim();
        const endIdx = rest.indexOf("|");

        if (endIdx >= 0) {
            return rest.substring(0, endIdx).trim() || "-";
        }

        return rest || "-";
    }

    function mapTransactionType(value) {
        switch (value) {
            case 30: return "AdjustmentIncrease";
            case 31: return "AdjustmentDecrease";
            case 20: return "SaleIssue";
            case 10: return "PurchaseReceipt";
            default: return `Type ${value}`;
        }
    }

    function setLoading(tbody, colspan) {
        if (!tbody) return;

        tbody.innerHTML = `
            <tr>
                <td colspan="${colspan}" class="inv-empty">Đang tải dữ liệu...</td>
            </tr>
        `;
    }

    function setSubmitting(isSubmitting) {
        if (!els.btnSubmit) return;

        els.btnSubmit.disabled = isSubmitting;
        els.btnSubmit.innerHTML = isSubmitting
            ? `<span class="spinner-border spinner-border-sm me-2"></span>Đang lưu...`
            : `<i class="bx bx-save me-1"></i> Lưu điều chỉnh`;
    }

    async function tryReadErrorResponse(response) {
        try {
            const contentType = response.headers.get("content-type") || "";
            if (contentType.includes("application/json")) {
                const json = await response.json();
                return json.message || json.title || JSON.stringify(json);
            }

            return await response.text();
        } catch {
            return null;
        }
    }

    function formatDateTime(value) {
        if (!value) return "";
        const d = new Date(value);
        if (isNaN(d.getTime())) return value;
        return d.toLocaleString("vi-VN");
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

    function getErrorMessage(error) {
        if (!error) return "Đã xảy ra lỗi.";
        if (typeof error === "string") return error;
        return error.message || "Đã xảy ra lỗi.";
    }

    function toastSuccess(message) {
        if (window.toastr) {
            toastr.success(message);
            return;
        }
        alert(message);
    }

    function toastError(message) {
        if (window.toastr) {
            toastr.error(message);
            return;
        }
        alert(message);
    }

    function toastWarning(message) {
        if (window.toastr) {
            toastr.warning(message);
            return;
        }
        alert(message);
    }
})();