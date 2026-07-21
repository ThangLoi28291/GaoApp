(function () {
    const page = document.querySelector(".adj-page");
    const documentId = Number(page?.dataset.documentId || 0);
    const apiBase = "/admin/api/inventory-adjustment-documents";

    let currentDoc = null;
    let currentPermissions = {};

    const els = {
        actionBox: document.getElementById("actionBox"),
        docNo: document.getElementById("docNo"),
        docSubTitle: document.getElementById("docSubTitle"),
        docStatus: document.getElementById("docStatus"),
        docType: document.getElementById("docType"),
        docWarehouse: document.getElementById("docWarehouse"),
        docLineCount: document.getElementById("docLineCount"),
        docDate: document.getElementById("docDate"),
        docReason: document.getElementById("docReason"),
        docSubmittedAt: document.getElementById("docSubmittedAt"),
        docFinalAt: document.getElementById("docFinalAt"),
        docNote: document.getElementById("docNote"),
        docApprovalNote: document.getElementById("docApprovalNote"),
        lineBody: document.getElementById("lineBody"),
        lineMobileList: document.getElementById("lineMobileList"),
        sumLineCount: document.getElementById("sumLineCount"),
        sumBaseQty: document.getElementById("sumBaseQty"),
        sumIncreaseAmount: document.getElementById("sumIncreaseAmount")
    };

    document.addEventListener("DOMContentLoaded", init);

    function init() {
        if (!documentId) {
            showError("Không xác định được phiếu.");
            return;
        }

        loadDetail();
    }

    async function loadDetail() {
        try {
            const res = await fetch(`${apiBase}/${documentId}`, {
                headers: { "Accept": "application/json" }
            });

            if (!res.ok) {
                throw new Error("Không tải được chi tiết phiếu.");
            }

            currentDoc = await res.json();
            currentPermissions = await loadPermissions();

            renderDocument(currentDoc);
            renderLines(currentDoc.lines || []);
            renderActions(currentDoc);
            applyPermissionVisibility();
        } catch (err) {
            showError(err.message || "Có lỗi xảy ra.");
        }
    }

    async function loadPermissions() {
        try {
            const res = await fetch(`${apiBase}/permissions`, {
                headers: { "Accept": "application/json" }
            });

            if (!res.ok) return {};

            return await res.json();
        } catch {
            return {};
        }
    }

    function renderDocument(doc) {
        els.docNo.textContent = doc.documentNo || "";
        els.docSubTitle.textContent =
            `${formatDate(doc.documentDate)} · ${doc.warehouseName || "-"} · ${doc.reasonTypeText || "-"}`;

        els.docStatus.innerHTML = renderStatus(doc.status, doc.statusText);
        els.docType.innerHTML = renderType(doc.adjustmentType, doc.adjustmentTypeText);
        els.docWarehouse.textContent = doc.warehouseName || "-";
        els.docLineCount.textContent = (doc.lines || []).length;

        els.docDate.textContent = formatDate(doc.documentDate);
        els.docReason.textContent = getReasonText(doc.reasonType, doc.reasonTypeText);
        els.docSubmittedAt.textContent = formatDateTime(doc.submittedAtUtc);
        els.docFinalAt.textContent =
            formatDateTime(doc.approvedAtUtc || doc.rejectedAtUtc || doc.cancelledAtUtc);

        els.docNote.textContent = doc.note || "-";
        els.docApprovalNote.textContent = doc.approvalNote || "-";
    }

    function renderLines(lines) {
        if (!lines.length) {
            els.lineBody.innerHTML = `
                <tr>
                    <td colspan="10" class="text-center text-muted py-4">
                        Phiếu chưa có dòng sản phẩm.
                    </td>
                </tr>`;

            els.lineMobileList.innerHTML =
                `<div class="text-center text-muted py-4">Phiếu chưa có dòng sản phẩm.</div>`;

            renderLineSummary([]);
            return;
        }

        els.lineBody.innerHTML = lines.map(x => `
    <tr>
        <td>
            <div class="d-flex align-items-center gap-3">
                ${renderProductImage(x.imageUrl)}
                <div>
                    <div class="adj-line-product">
                        ${escapeHtml(x.productName || x.productVariantName || "-")}
                    </div>
                    <div class="adj-line-sub">
                        Barcode: ${escapeHtml(x.barcode || "-")}
                    </div>
                </div>
            </div>
        </td>

        <td>${escapeHtml(x.unitName || "-")}</td>
        <td class="text-end">${formatNumber(x.quantity)}</td>
        <td class="text-end">${formatNumber(x.factor)}</td>
        <td class="text-end fw-bold">${formatNumber(x.baseQuantity)}</td>
        <td class="text-end">${formatMoney(x.price)}</td>

        <td class="text-end adj-cost-col">
            ${formatMoney(x.unitCost || x.costPrice)}
        </td>

        <td class="text-end">${formatMoney(x.provisionalUnitCost)}</td>
        <td>${escapeHtml(x.note || "-")}</td>
    </tr>
`).join("");
        els.lineMobileList.innerHTML = lines.map(x => `
    <div class="adj-mobile-card">
        <div class="d-flex gap-3">
            ${renderProductImage(x.imageUrl)}
            <div class="flex-grow-1">
                <div class="adj-mobile-title">
                    ${escapeHtml(x.productName || x.productVariantName || "-")}
                </div>
                <div class="adj-mobile-meta">
                    Barcode: ${escapeHtml(x.barcode || "-")} · ĐVT: ${escapeHtml(x.unitName || "-")}
                </div>
            </div>
        </div>

        <div class="row g-2 mt-3">
            <div class="col-4">
                <div class="adj-info-label">SL nhập</div>
                <div class="fw-bold">${formatNumber(x.quantity)}</div>
            </div>
            <div class="col-4">
                <div class="adj-info-label">Factor</div>
                <div class="fw-bold">${formatNumber(x.factor)}</div>
            </div>
            <div class="col-4">
                <div class="adj-info-label">SL gốc</div>
                <div class="fw-bold">${formatNumber(x.baseQuantity)}</div>
            </div>
            <div class="col-6">
                <div class="adj-info-label">Giá lẻ</div>
                <div class="fw-bold">${formatMoney(x.price)}</div>
            </div>
            <div class="col-6 adj-cost-col">
                <div class="adj-info-label">Giá vốn</div>
                <div class="fw-bold">${formatMoney(x.unitCost || x.costPrice)}</div>
            </div>
        </div>

        <div class="text-muted mt-2">${escapeHtml(x.note || "")}</div>
    </div>
`).join("");
        renderLineSummary(lines);
        applyPermissionVisibility();
    }

    function renderProductImage(imageUrl) {
        if (!imageUrl) {
            return `
                <div class="adj-detail-thumb">
                    <i class="bx bx-package"></i>
                </div>`;
        }

        return `
            <div class="adj-detail-thumb adj-image-hover">
                <img src="${escapeAttr(imageUrl)}" alt="product" />
                <div class="adj-image-preview">
                    <img src="${escapeAttr(imageUrl)}" alt="preview" />
                </div>
            </div>`;
    }

    function renderLineSummary(lines) {
        const totalLines = lines.length;
        const totalBaseQty = lines.reduce((sum, x) => sum + Number(x.baseQuantity || 0), 0);

        const totalIncreaseAmount = lines.reduce((sum, x) => {
            const cost = Number(x.unitCost || x.costPrice || 0);
            const qty = Number(x.baseQuantity || 0);
            return sum + cost * qty;
        }, 0);

        if (els.sumLineCount) els.sumLineCount.textContent = totalLines;
        if (els.sumBaseQty) els.sumBaseQty.textContent = formatNumber(totalBaseQty);

        if (els.sumIncreaseAmount) {
            els.sumIncreaseAmount.textContent =
                currentDoc && currentDoc.adjustmentType === 30
                    ? formatMoney(totalIncreaseAmount)
                    : "-";
        }
    }

    function applyPermissionVisibility() {
        const canSeeCost = currentPermissions.canApprove === true;

        document.querySelectorAll(".adj-cost-col").forEach(x => {
            x.classList.toggle("d-none", !canSeeCost);
        });
    }

    function renderActions(doc) {
        const backButton = `
            <a href="/admin/inventory-adjustment-documents" class="btn btn-outline-secondary">
                <i class="bx bx-arrow-back me-1"></i> Quay lại
            </a>`;

        let html = backButton;

        if (doc.status === 0) {
            if (currentPermissions.canUpdate) {
                html += `
                    <a href="/admin/inventory-adjustment-documents/${doc.id}/edit" class="btn btn-outline-primary">
                        <i class="bx bx-edit me-1"></i> Sửa
                    </a>`;
            }

            if (currentPermissions.canSubmit) {
                html += `
                    <button type="button" class="btn btn-warning" id="btnSubmit">
                        <i class="bx bx-send me-1"></i> Gửi duyệt
                    </button>`;
            }

            if (currentPermissions.canCancel) {
                html += `
                    <button type="button" class="btn btn-outline-danger" id="btnCancel">
                        <i class="bx bx-x me-1"></i> Hủy phiếu
                    </button>`;
            }
        }

        if (doc.status === 1) {
            if (currentPermissions.canApprove) {
                html += `
                    <button type="button" class="btn btn-success" id="btnApprove">
                        <i class="bx bx-check me-1"></i> Duyệt
                    </button>`;
            }

            if (currentPermissions.canReject) {
                html += `
                    <button type="button" class="btn btn-outline-danger" id="btnReject">
                        <i class="bx bx-block me-1"></i> Từ chối
                    </button>`;
            }

            if (currentPermissions.canCancel) {
                html += `
                    <button type="button" class="btn btn-outline-secondary" id="btnCancel">
                        <i class="bx bx-x me-1"></i> Hủy phiếu
                    </button>`;
            }
        }

        els.actionBox.innerHTML = html;

        document.getElementById("btnSubmit")?.addEventListener("click", submitDocument);
        document.getElementById("btnApprove")?.addEventListener("click", approveDocument);
        document.getElementById("btnReject")?.addEventListener("click", rejectDocument);
        document.getElementById("btnCancel")?.addEventListener("click", cancelDocument);
    }

    async function submitDocument() {
        const ok = await Swal.fire({
            title: "Gửi duyệt phiếu?",
            text: "Sau khi gửi duyệt, phiếu sẽ không còn sửa trực tiếp.",
            icon: "question",
            showCancelButton: true,
            confirmButtonText: "Gửi duyệt",
            cancelButtonText: "Đóng"
        });

        if (!ok.isConfirmed) return;

        await postAction(`${apiBase}/${documentId}/submit`, {});
    }

    async function approveDocument() {
        let costSuggestions = [];

        if (currentDoc && currentDoc.adjustmentType === 30) {
            try {
                const res = await fetch(`${apiBase}/${documentId}/cost-suggestions`, {
                    headers: { "Accept": "application/json" }
                });

                if (!res.ok) {
                    throw new Error("Không tải được giá vốn đề xuất.");
                }

                costSuggestions = await res.json();
            } catch (err) {
                Swal.fire({
                    title: "Không tải được giá vốn",
                    text: err.message || "Có lỗi xảy ra.",
                    icon: "error"
                });
                return;
            }
        }

        const costHtml = currentDoc && currentDoc.adjustmentType === 30
            ? buildCostSuggestionHtml(costSuggestions)
            : `<div class="text-muted">Phiếu giảm kho sẽ xuất theo cost layer hiện có.</div>`;

        const result = await Swal.fire({
            title: "Duyệt phiếu điều chỉnh?",
            html: `
                <div class="text-start">
                    <div class="mb-3">
                        Sau khi duyệt, hệ thống sẽ tạo giao dịch kho và cập nhật tồn kho.
                    </div>

                    <div class="border rounded p-3 mb-3 bg-light">
                        <div class="fw-bold mb-2">Giá vốn áp dụng</div>
                        ${costHtml}
                    </div>

                    <label class="form-label fw-bold">Ghi chú duyệt</label>
                    <textarea id="approvalNoteInput"
                              class="form-control"
                              rows="3"
                              placeholder="Ghi chú duyệt..."></textarea>
                </div>
            `,
            icon: "warning",
            showCancelButton: true,
            confirmButtonText: "Duyệt phiếu",
            cancelButtonText: "Đóng",
            confirmButtonColor: "#12b76a",
            width: 760,
            preConfirm: () => {
                const missing = costSuggestions.find(x => !x.hasValidCost);

                if (currentDoc && currentDoc.adjustmentType === 30 && missing) {
                    Swal.showValidationMessage(
                        `Sản phẩm "${missing.productName}" chưa có giá vốn hợp lệ.`
                    );
                    return false;
                }

                return {
                    approvalNote: document.getElementById("approvalNoteInput")?.value || null
                };
            }
        });

        if (!result.isConfirmed) return;

        await postAction(`${apiBase}/${documentId}/approve`, {
            approvalNote: result.value?.approvalNote || null
        });
    }

    function buildCostSuggestionHtml(items) {
        if (!items || !items.length) {
            return `<div class="text-muted">Không có dữ liệu giá vốn.</div>`;
        }

        return items.map(x => {
            const validClass = x.hasValidCost ? "text-success" : "text-danger";
            const costText = x.hasValidCost ? formatMoney(x.unitCost) : "Chưa có giá";

            return `
                <div class="d-flex justify-content-between align-items-start border-bottom py-2">
                    <div>
                        <div class="fw-bold">${escapeHtml(x.productName || "-")}</div>
                        <div class="small text-muted">
                            SKU: ${escapeHtml(x.sku || "-")} · ${escapeHtml(x.sourceText || "")}
                        </div>
                    </div>
                    <div class="fw-bold ${validClass}">
                        ${costText}
                    </div>
                </div>
            `;
        }).join("");
    }

    async function rejectDocument() {
        const result = await Swal.fire({
            title: "Từ chối phiếu?",
            text: "Phiếu sẽ chuyển sang trạng thái từ chối.",
            icon: "error",
            input: "textarea",
            inputPlaceholder: "Lý do từ chối...",
            inputValidator: value => {
                if (!value || !value.trim()) return "Vui lòng nhập lý do từ chối.";
                return null;
            },
            showCancelButton: true,
            confirmButtonText: "Từ chối",
            cancelButtonText: "Đóng",
            confirmButtonColor: "#f04438"
        });

        if (!result.isConfirmed) return;

        await postAction(`${apiBase}/${documentId}/reject`, {
            approvalNote: result.value || null
        });
    }

    async function cancelDocument() {
        const result = await Swal.fire({
            title: "Hủy phiếu?",
            text: "Phiếu sẽ chuyển sang trạng thái đã hủy và không tác động tồn kho.",
            icon: "warning",
            input: "textarea",
            inputPlaceholder: "Lý do hủy phiếu...",
            showCancelButton: true,
            confirmButtonText: "Hủy phiếu",
            cancelButtonText: "Đóng",
            confirmButtonColor: "#f04438"
        });

        if (!result.isConfirmed) return;

        await postAction(`${apiBase}/${documentId}/cancel`, {
            cancelNote: result.value || null
        });
    }

    async function postAction(url, body) {
        try {
            Swal.fire({
                title: "Đang xử lý...",
                allowOutsideClick: false,
                didOpen: () => Swal.showLoading()
            });

            const res = await fetch(url, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                body: JSON.stringify(body || {})
            });

            if (!res.ok) {
                const err = await safeJson(res);
                throw new Error(err?.message || "Thao tác thất bại.");
            }

            await Swal.fire({
                title: "Thành công",
                text: "Thao tác đã được thực hiện.",
                icon: "success",
                timer: 1300,
                showConfirmButton: false
            });

            await loadDetail();
        } catch (err) {
            Swal.fire({
                title: "Không thành công",
                text: err.message || "Có lỗi xảy ra.",
                icon: "error"
            });
        }
    }

    function showError(message) {
        els.lineBody.innerHTML = `
            <tr>
                <td colspan="10" class="text-center text-danger py-4">
                    ${escapeHtml(message)}
                </td>
            </tr>`;
    }

    function renderType(type, text) {
        if (type === 30) return `<span class="adj-type-in">+ ${escapeHtml(text || "Điều chỉnh tăng")}</span>`;
        if (type === 31) return `<span class="adj-type-out">- ${escapeHtml(text || "Điều chỉnh giảm")}</span>`;
        return escapeHtml(text || "-");
    }

    function renderStatus(status, text) {
        return `<span class="adj-badge ${getStatusClass(status)}">${getStatusText(status, text)}</span>`;
    }

    function getStatusText(status, fallback) {
        switch (status) {
            case 0: return "Nháp";
            case 1: return "Chờ duyệt";
            case 2: return "Đã duyệt";
            case 3: return "Từ chối";
            case 4: return "Đã hủy";
            default: return fallback || "-";
        }
    }

    function getStatusClass(status) {
        switch (status) {
            case 0: return "adj-badge-draft";
            case 1: return "adj-badge-pending";
            case 2: return "adj-badge-approved";
            case 3: return "adj-badge-rejected";
            case 4: return "adj-badge-cancelled";
            default: return "adj-badge-draft";
        }
    }

    function getReasonText(reason, fallback) {
        switch (reason) {
            case 0: return "Khác";
            case 1: return "Lệch kiểm kê";
            case 2: return "Hư hỏng";
            case 3: return "Hết hạn";
            case 4: return "Mất hàng";
            case 5: return "Tìm thấy hàng";
            case 6: return "Điều chỉnh tồn đầu";
            case 7: return "Điều chỉnh giá vốn";
            default: return fallback || "-";
        }
    }

    function formatDate(value) {
        if (!value) return "-";
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return "-";
        return d.toLocaleDateString("vi-VN");
    }

    function formatDateTime(value) {
        if (!value) return "-";
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return "-";
        return d.toLocaleString("vi-VN");
    }

    function formatNumber(value) {
        if (value === null || value === undefined) return "-";
        return Number(value).toLocaleString("vi-VN", {
            maximumFractionDigits: 3
        });
    }

    function formatMoney(value) {
        if (value === null || value === undefined || Number(value) <= 0) return "-";
        return Number(value).toLocaleString("vi-VN", {
            maximumFractionDigits: 0
        });
    }

    async function safeJson(res) {
        try {
            return await res.json();
        } catch {
            return null;
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

    function escapeAttr(value) {
        return escapeHtml(value).replaceAll("`", "&#096;");
    }
})();