(function () {
    const page = document.querySelector(".adj-page");
    const mode = page?.dataset.mode || "create";
    const documentId = Number(page?.dataset.documentId || 0);

    const apiBase = "/admin/api/inventory-adjustment-documents";
    const lookupApi = "/admin/api/inventory-adjustments/product-lookup-select2";

    let lines = [];
    let currentPermissions = {};
    let isSaving = false;

    const els = {
        pageTitle: document.getElementById("pageTitle"),
        documentDate: document.getElementById("DocumentDate"),
        warehouseId: document.getElementById("WarehouseId"),
        adjustmentType: document.getElementById("AdjustmentType"),
        reasonType: document.getElementById("ReasonType"),
        note: document.getElementById("Note"),
        quickSearch: document.getElementById("ProductQuickSearch"),
        tableBody: document.getElementById("AdjustmentLineTableBody"),
        btnSave: document.getElementById("btnSaveDocument")
    };

    document.addEventListener("DOMContentLoaded", init);

    async function init() {
        if (els.documentDate) {
            els.documentDate.value = new Date().toISOString().slice(0, 10);
        }

        await loadPermissions();
        await loadWarehouses();

        initProductQuickSelect();
        bindEvents();

        if (mode === "edit" && documentId > 0) {
            els.pageTitle.textContent = "Sửa phiếu điều chỉnh kho";
            await loadDocument(documentId);
        }

        renderLines();

        setTimeout(() => {
            $("#ProductQuickSearch").select2("open");
        }, 250);
    }

    function bindEvents() {
        els.btnSave?.addEventListener("click", function () {
            saveDocument(false);
        });

        els.adjustmentType?.addEventListener("change", function () {
            renderLines();
        });

        document.addEventListener("keydown", handleGlobalShortcuts);
    }

    async function loadPermissions() {
        try {
            const res = await fetch(`${apiBase}/permissions`);
            currentPermissions = res.ok ? await res.json() : {};
        } catch {
            currentPermissions = {};
        }
    }

    async function loadWarehouses() {
        const res = await fetch("/admin/api/warehouses?isActive=true");
        if (!res.ok) return;

        const data = await res.json();
        const items = data.items || data.results || data || [];

        els.warehouseId.innerHTML = `<option value="">-- Chọn kho --</option>` +
            items.map(x => `<option value="${x.id}">${escapeHtml(x.name || x.text || x.code)}</option>`).join("");
    }

    async function loadDocument(id) {
        const res = await fetch(`${apiBase}/${id}`);
        if (!res.ok) {
            alert("Không tải được phiếu.");
            return;
        }

        const doc = await res.json();

        els.documentDate.value = toDateInput(doc.documentDate);
        els.warehouseId.value = doc.warehouseId;
        els.adjustmentType.value = doc.adjustmentType;
        els.reasonType.value = doc.reasonType;
        els.note.value = doc.note || "";

        lines = (doc.lines || []).map(x => ({
            productVariantId: x.productVariantId,
            productUnitConversionId: x.productUnitConversionId,
            unitId: x.unitId,
            productName: x.productName || x.productVariantName || "",
            sku: x.sku || "",
            barcode: x.barcode || "",
            unitName: x.unitName || "",
            factor: Number(x.factor || 1),
            quantity: Number(x.quantity || 0),
            baseQuantity: Number(x.baseQuantity || 0),
            unitCost: x.unitCost,
            inputUnitCost: x.unitCost && x.factor ? Number(x.unitCost) * Number(x.factor) : null,
            price: x.price || x.salePrice || x.retailPrice || null,
            costPrice: x.costPrice || null,
            note: x.note || "",
            imageUrl: x.imageUrl || null
        }));

        renderLines();
    }

    function initProductQuickSelect() {
        const $select = $("#ProductQuickSearch");

        $select.select2({
            theme: "bootstrap-5",
            width: "100%",
            placeholder: "Gõ tên, SKU hoặc quét barcode...",
            minimumInputLength: 1,
            ajax: {
                url: lookupApi,
                dataType: "json",
                delay: 150,
                data: function (params) {
                    return {
                        term: params.term || ""
                    };
                },
                processResults: function (data) {
                    const results = data.results || [];

                    return {
                        results: results.map(x => ({
                            id: `${x.productVariantId}_${x.unitId || ""}_${x.productUnitConversionId || ""}`,
                            text: x.text || x.productName,
                            productVariantId: x.productVariantId,
                            productUnitConversionId: x.productUnitConversionId,
                            unitId: x.unitId,
                            productName: x.productName,
                            sku: x.sku,
                            barcode: x.barcode,
                            unitName: x.unitName,
                            factor: x.factor,
                            price: x.price || x.salePrice || x.retailPrice || null,
                            costPrice: x.costPrice || null,
                            imageUrl: x.imageUrl || null
                        }))
                    };
                }
            },
            templateResult: formatProductSelect2Result,
            templateSelection: function (item) {
                return item.text || item.productName || "";
            }
        });

        $select.on("select2:select", function (e) {
            const item = e.params.data;

            setTimeout(() => {
                $select.val(null).trigger("change");
            }, 50);

            openQuantityPopup(item);
        });
    }

    function formatProductSelect2Result(item) {
        if (item.loading) return item.text;

        return $(`
            <div class="d-flex align-items-center gap-2">
                <div class="adj-product-thumb-sm">
                    ${item.imageUrl
                ? `<img src="${escapeAttr(item.imageUrl)}" alt="">`
                : `<i class="bx bx-package"></i>`}
                </div>

                <div>
                    <div class="fw-bold">${escapeHtml(item.productName || item.text || "-")}</div>
                    <div class="small text-muted">
                        SKU: ${escapeHtml(item.sku || "-")}
                        · ĐVT: ${escapeHtml(item.unitName || "-")}
                        · Factor: ${escapeHtml(item.factor || 1)}
                        · Barcode: ${escapeHtml(item.barcode || "-")}
                    </div>
                    <div class="small text-muted">
                        Giá lẻ: <strong>${formatMoney(item.price)}</strong>
                        ${canShowCostBox()
                ? ` · Giá vốn: <strong>${formatMoney(item.costPrice)}</strong>`
                : ""}
                    </div>
                </div>
            </div>
        `);
    }

    function openQuantityPopup(item) {
        Swal.fire({
            title: "Nhập số lượng điều chỉnh",
            html: `
                <div class="text-start">
                    <div class="d-flex gap-3 align-items-center mb-3">
                        <div class="adj-product-thumb">
                            ${item.imageUrl
                    ? `<img src="${escapeAttr(item.imageUrl)}" alt="">`
                    : `<i class="bx bx-package"></i>`}
                        </div>

                        <div>
                            <div class="fw-bold fs-5">${escapeHtml(item.productName || item.text || "-")}</div>
                            <div class="small text-muted">
                                SKU: ${escapeHtml(item.sku || "-")}
                                · Barcode: ${escapeHtml(item.barcode || "-")}
                            </div>
                            <div class="small text-muted">
                                ĐVT: <b>${escapeHtml(item.unitName || "-")}</b>
                                · Factor: <b>${formatNumber(item.factor || 1)}</b>
                            </div>
                            <div class="small text-muted">
                                Giá lẻ:
                                <b class="text-primary">${formatMoney(item.price)}</b>
                                ${canShowCostBox()
                    ? ` · Giá vốn hiện tại: <b class="text-danger">${formatMoney(item.costPrice)}</b>`
                    : ""}
                            </div>
                        </div>
                    </div>

                    <label class="form-label fw-bold">Số lượng</label>
                    <input id="popupQty"
                           type="number"
                           min="0.001"
                           step="0.001"
                           class="form-control form-control-lg"
                           placeholder="Nhập số lượng..." />

                    <div class="mt-2 text-muted">
                        SL quy đổi:
                        <b id="popupBaseQty">0</b>
                    </div>

                    ${canShowCostBox() && Number(els.adjustmentType.value) === 30
                    ? `
                            <div class="mt-3">
                                <label class="form-label fw-bold">Giá vốn theo ĐVT chọn</label>
                                <input id="popupInputCost"
                                       type="number"
                                       min="0.001"
                                       step="0.001"
                                       class="form-control"
                                       placeholder="VD: 72000/${escapeAttr(item.unitName || "ĐVT")}" />

                                <div class="small text-muted mt-1">
                                    Giá vốn gốc:
                                    <b id="popupBaseCost">-</b>
                                </div>
                            </div>
                          `
                    : ""}
                </div>
            `,
            showCancelButton: true,
            confirmButtonText: "Thêm vào phiếu",
            cancelButtonText: "Đóng",
            width: 720,
            didOpen: () => {
                const qty = document.getElementById("popupQty");
                const baseQty = document.getElementById("popupBaseQty");
                const cost = document.getElementById("popupInputCost");
                const baseCost = document.getElementById("popupBaseCost");

                qty.focus();

                qty.addEventListener("input", function () {
                    const q = Number(qty.value || 0);
                    const f = Number(item.factor || 1);
                    baseQty.textContent = formatNumber(q * f);
                });

                qty.addEventListener("keydown", function (e) {
                    if (e.key === "Enter") {
                        e.preventDefault();
                        Swal.clickConfirm();
                    }
                });

                cost?.addEventListener("input", function () {
                    const inputCost = Number(cost.value || 0);
                    const factor = Number(item.factor || 1);
                    baseCost.textContent = inputCost > 0
                        ? formatMoney(inputCost / factor)
                        : "-";
                });

                cost?.addEventListener("keydown", function (e) {
                    if (e.key === "Enter") {
                        e.preventDefault();
                        Swal.clickConfirm();
                    }
                });
            },
            preConfirm: () => {
                const qty = Number(document.getElementById("popupQty")?.value || 0);

                if (qty <= 0) {
                    Swal.showValidationMessage("Vui lòng nhập số lượng lớn hơn 0.");
                    return false;
                }

                const inputCostElement = document.getElementById("popupInputCost");
                const inputCost = inputCostElement
                    ? Number(inputCostElement.value || 0)
                    : 0;

                return {
                    quantity: qty,
                    inputUnitCost: inputCost > 0 ? inputCost : null
                };
            }
        }).then(result => {
            if (!result.isConfirmed) {
                focusSearch();
                return;
            }

            addProductLine(item, result.value.quantity, result.value.inputUnitCost);

            focusSearch();
        });
    }

    function addProductLine(item, quantity, inputUnitCost) {
        const factor = Number(item.factor || 1);
        const baseQuantity = quantity * factor;

        const unitCost = inputUnitCost && inputUnitCost > 0
            ? inputUnitCost / factor
            : null;

        lines.push({
            productVariantId: Number(item.productVariantId || 0),
            productUnitConversionId: toNullableNumber(item.productUnitConversionId),
            unitId: toNullableNumber(item.unitId),
            productName: item.productName || item.text || "",
            sku: item.sku || "",
            barcode: item.barcode || "",
            unitName: item.unitName || "",
            factor,
            quantity,
            baseQuantity,
            unitCost,
            inputUnitCost: inputUnitCost || null,
            price: item.price || null,
            costPrice: item.costPrice || null,
            note: "",
            imageUrl: item.imageUrl || null
        });

        renderLines();
    }

    function renderLines() {
        if (!lines.length) {
            els.tableBody.innerHTML = `
                <tr>
                    <td colspan="9" class="text-center text-muted py-4">
                        Chưa có sản phẩm nào.
                    </td>
                </tr>`;
            return;
        }

        els.tableBody.innerHTML = lines.map((x, index) => `
            <tr>
                <td>${index + 1}</td>

                <td>
                    <div class="fw-bold">${escapeHtml(x.productName || "-")}</div>
                    <div class="small text-muted">
                        SKU: ${escapeHtml(x.sku || "-")}
                        · Barcode: ${escapeHtml(x.barcode || "-")}
                    </div>
                </td>

                <td>${escapeHtml(x.unitName || "-")}</td>

                <td class="text-end">
                    ${formatNumber(x.quantity)}
                    <div class="small text-muted">x ${formatNumber(x.factor)}</div>
                </td>

                <td class="text-end fw-bold">${formatNumber(x.baseQuantity)}</td>

                <td class="text-end adj-cost-col">
                    ${renderCostCell(x, index)}
                </td>

                <td class="text-end">${formatMoney(x.price)}</td>

                <td>
                    <input type="text"
                           class="form-control form-control-sm js-line-note"
                           data-index="${index}"
                           value="${escapeAttr(x.note || "")}"
                           placeholder="Ghi chú..." />
                </td>

                <td class="text-end">
                    <button type="button"
                            class="btn btn-sm btn-outline-danger js-remove-line"
                            data-index="${index}">
                        <i class="bx bx-trash"></i>
                    </button>
                </td>
            </tr>
        `).join("");

        bindLineTableEvents();
        refreshCostColumn();
    }

    function renderCostCell(line, index) {
        if (!canShowCostBox() || Number(els.adjustmentType.value) !== 30) {
            return `<span class="text-muted">-</span>`;
        }

        return `
            <input type="number"
                   min="0.001"
                   step="0.001"
                   class="form-control form-control-sm text-end js-line-cost"
                   data-index="${index}"
                   value="${line.inputUnitCost || ""}"
                   placeholder="Giá/${escapeAttr(line.unitName || "ĐVT")}" />

            <div class="small text-muted">
                Gốc: ${line.unitCost
                ? formatMoney(line.unitCost)
                : line.costPrice
                    ? formatMoney(line.costPrice)
                    : "-"
            }
            </div>
        `;
    }

    function bindLineTableEvents() {
        document.querySelectorAll(".js-remove-line").forEach(btn => {
            btn.addEventListener("click", function () {
                const index = Number(btn.dataset.index);
                lines.splice(index, 1);
                renderLines();
                focusSearch();
            });
        });

        document.querySelectorAll(".js-line-note").forEach(input => {
            input.addEventListener("input", function () {
                const index = Number(input.dataset.index);
                lines[index].note = input.value;
            });
        });

        document.querySelectorAll(".js-line-cost").forEach(input => {
            input.addEventListener("input", function () {
                const index = Number(input.dataset.index);
                const inputCost = Number(input.value || 0);
                const factor = Number(lines[index].factor || 1);

                lines[index].inputUnitCost = input.value;

                if (inputCost > 0) {
                    lines[index].unitCost = inputCost / factor;
                } else {
                    lines[index].unitCost = null;
                }

                renderLines();
            });
        });
    }

    function refreshCostColumn() {
        const show = canShowCostBox() && Number(els.adjustmentType.value) === 30;

        document.querySelectorAll(".adj-cost-col").forEach(x => {
            x.classList.toggle("d-none", !show);
        });
    }

    async function saveDocument(submitAfterSave) {
        if (isSaving) return;

        const payload = buildPayload();
        const validation = validatePayload(payload);

        if (validation) {
            alert(validation);
            return;
        }

        const url = mode === "edit" ? `${apiBase}/${documentId}` : apiBase;
        const method = mode === "edit" ? "PUT" : "POST";

        isSaving = true;
        els.btnSave.disabled = true;

        try {
            const res = await fetch(url, {
                method,
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                body: JSON.stringify(payload)
            });

            if (!res.ok) {
                const err = await safeJson(res);
                throw new Error(err?.message || "Lưu phiếu thất bại.");
            }

            const result = await res.json();

            if (submitAfterSave) {
                await submitDocumentAfterSave(result.id);
                return;
            }

            window.location.href = `/admin/inventory-adjustment-documents/${result.id}`;
        } catch (err) {
            alert(err.message || "Có lỗi xảy ra.");
        } finally {
            isSaving = false;
            els.btnSave.disabled = false;
        }
    }

    async function submitDocumentAfterSave(id) {
        const res = await fetch(`${apiBase}/${id}/submit`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json"
            },
            body: JSON.stringify({})
        });

        if (!res.ok) {
            const err = await safeJson(res);
            alert(err?.message || "Lưu phiếu thành công nhưng gửi duyệt thất bại.");
            return;
        }

        window.location.href = `/admin/inventory-adjustment-documents/${id}`;
    }

    function buildPayload() {
        return {
            id: mode === "edit" ? documentId : undefined,
            warehouseId: Number(els.warehouseId.value || 0),
            documentDate: els.documentDate.value || null,
            adjustmentType: Number(els.adjustmentType.value),
            reasonType: Number(els.reasonType.value),
            note: els.note.value || null,
            lines: lines.map(x => ({
                productVariantId: x.productVariantId,
                unitId: x.unitId,
                productUnitConversionId: x.productUnitConversionId,
                quantity: x.quantity,
                unitCost: x.unitCost,
                provisionalUnitCost: null,
                note: x.note || null
            }))
        };
    }

    function validatePayload(payload) {
        if (!payload.warehouseId) return "Vui lòng chọn kho.";
        if (!payload.lines.length) return "Vui lòng nhập ít nhất 1 sản phẩm.";

        for (let i = 0; i < payload.lines.length; i++) {
            const line = payload.lines[i];

            if (!line.productVariantId) return `Dòng ${i + 1}: sản phẩm không hợp lệ.`;
            if (!line.quantity || line.quantity <= 0) return `Dòng ${i + 1}: số lượng phải lớn hơn 0.`;
        }

        return null;
    }

    function handleGlobalShortcuts(e) {
        const key = e.key.toLowerCase();

        if (e.ctrlKey && key === "s") {
            e.preventDefault();
            saveDocument(false);
        }

        if (e.ctrlKey && e.key === "Enter") {
            e.preventDefault();
            saveDocument(true);
        }
    }

    function focusSearch() {
        setTimeout(() => {
            const $select = $("#ProductQuickSearch");

            $select.val(null).trigger("change");
            $select.select2("close");

            setTimeout(() => {
                $select.select2("open");

                setTimeout(() => {
                    const input = document.querySelector(".select2-container--open .select2-search__field");
                    input?.focus();
                    input?.select();
                }, 80);
            }, 80);
        }, 200);
    }

    function canShowCostBox() {
        return currentPermissions.canApprove === true;
    }

    function toNullableNumber(value) {
        if (value === undefined || value === null || String(value).trim() === "") return null;
        return Number(value);
    }

    function toDateInput(value) {
        if (!value) return "";

        const d = new Date(value);
        return Number.isNaN(d.getTime()) ? "" : d.toISOString().slice(0, 10);
    }

    async function safeJson(res) {
        try { return await res.json(); } catch { return null; }
    }

    function formatNumber(value) {
        return Number(value || 0).toLocaleString("vi-VN", {
            maximumFractionDigits: 3
        });
    }

    function formatMoney(value) {
        if (!value || Number(value) <= 0) return "-";

        return Number(value).toLocaleString("vi-VN", {
            maximumFractionDigits: 0
        });
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function escapeAttr(value) {
        return escapeHtml(value).replaceAll("`", "&#096;");
    }
})();
