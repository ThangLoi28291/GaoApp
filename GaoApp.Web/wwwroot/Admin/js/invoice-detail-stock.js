(() => {
    "use strict";
    const page = document.getElementById("invoiceDetailPage");
    const status = document.getElementById("invoiceStockStatus");
    const refreshButton = document.getElementById("btnRefreshInvoiceStock");
    const checkedAt = document.getElementById("invoiceStockCheckedAt");
    if (!page || !status || !refreshButton) return;

    const escapeHtml = value => String(value ?? "").replace(/[&<>"']/g,
        char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char]));
    const quantity = value => Number(value).toLocaleString("vi-VN", { maximumFractionDigits: 4 });
    let controller, sequence = 0;
    const cells = () => page.querySelectorAll("[data-invoice-stock-detail-id]");

    function setStatus(message, tone = "muted") {
        status.className = `fw-semibold text-${tone}`;
        status.textContent = message;
    }

    function resetCells(message) {
        cells().forEach(cell => {
            cell.closest("tr")?.classList.remove("invoice-stock-shortage");
            cell.title = "";
            cell.innerHTML = `<span class="text-muted small">${cell.dataset.stockProductId ? escapeHtml(message) : "—"}</span>`;
        });
    }

    function render(data) {
        if (!data.checkRequired) {
            resetCells("—");
            setStatus(data.message || "Không cần kiểm tra tồn.");
            return;
        }

        const availability = data.availability;
        if (!availability || !Array.isArray(availability.detailLines) || !Array.isArray(availability.lines)) {
            throw new Error("Không có dữ liệu tồn.");
        }
        const byId = new Map(availability.detailLines.map(line => [String(line.invoiceDetailId), line]));
        cells().forEach(cell => {
            const line = byId.get(cell.dataset.invoiceStockDetailId);
            if (!line) {
                cell.textContent = "—";
                cell.title = "Dòng không có sản phẩm hoặc không có số lượng cần kiểm tra tồn.";
                return;
            }
            if (line.warehouseId <= 0) {
                cell.innerHTML = '<span class="text-warning small fw-semibold">Chưa xác định kho</span>';
                return;
            }
            const shortage = Number(line.shortageQuantity) > 0;
            cell.closest("tr")?.classList.toggle("invoice-stock-shortage", shortage);
            const unit = escapeHtml(line.unitName);
            const warehouse = escapeHtml(line.warehouseName);
            const shared = Number(line.sharedLineCount) > 1;
            cell.title = `${line.warehouseName}. Cần ${quantity(line.requiredQuantity)} ${line.unitName}`
                + (shared ? ` cho ${line.sharedLineCount} dòng cùng sản phẩm.` : ".")
                + (Number(line.unitFactor) !== 1 ? ` 1 ${line.unitName} = ${quantity(line.unitFactor)} ${line.baseUnitName}.` : "");
            const trace = data.canViewStock
                ? `<a class="small" target="_blank" rel="noopener" href="/admin/invoice-input-stock?productVariantId=${encodeURIComponent(line.productVariantId)}&amp;warehouseId=${encodeURIComponent(line.warehouseId)}">Tra tồn · ${warehouse}</a>`
                : `<small class="text-muted">${warehouse}</small>`;
            cell.innerHTML = `<div class="invoice-stock-quantity text-${shortage ? "danger" : "success"}">${quantity(line.availableQuantity)} <span class="small fw-normal">${unit}</span></div>`
                + (shortage ? `<small class="text-danger fw-semibold">Thiếu ${quantity(line.shortageQuantity)} ${unit}</small>` : '<small class="text-success">Đủ số lượng</small>')
                + (shared ? `<small class="text-muted">Cần ${quantity(line.requiredQuantity)} ${unit} / ${Number(line.sharedLineCount)} dòng</small>` : "")
                + trace;
        });

        const unknown = availability.lines.filter(line => line.warehouseId <= 0).length;
        const shortages = availability.lines.filter(line => line.warehouseId > 0 && !line.isSufficient).length;
        if (unknown || shortages) {
            setStatus([
                shortages ? `${shortages} sản phẩm còn thiếu tồn để phát hành` : "",
                unknown ? `${unknown} sản phẩm chưa xác định kho` : ""
            ].filter(Boolean).join(" · "), "danger");
        } else {
            setStatus(availability.lines.length ? "Đủ tồn để phát hành hóa đơn" : "Không có dòng sản phẩm cần kiểm tra tồn.", availability.lines.length ? "success" : "muted");
        }
        if (checkedAt && data.checkedAtUtc) {
            const date = new Date(data.checkedAtUtc);
            if (!Number.isNaN(date.getTime())) {
                checkedAt.textContent = "Cập nhật: " + date.toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" });
            }
        }
    }

    async function reload() {
        controller?.abort();
        controller = new AbortController();
        const currentSequence = ++sequence;
        refreshButton.disabled = true;
        if (checkedAt) checkedAt.textContent = "";
        resetCells("Đang tải…");
        setStatus("Đang kiểm tra tồn trước phát hành…");
        try {
            const response = await fetch(`/Admin/Invoice/StockAvailability?id=${encodeURIComponent(page.dataset.invoiceHeadId)}`, {
                headers: { "X-Requested-With": "XMLHttpRequest" },
                cache: "no-store",
                signal: controller.signal
            });
            if (!response.ok) throw new Error("Không tải được tồn.");
            const data = await response.json();
            if (currentSequence !== sequence) return;
            if (!data.success) throw new Error(data.message || "Không tải được tồn.");
            render(data);
        } catch (error) {
            if (currentSequence !== sequence || error.name === "AbortError") return;
            resetCells("Chưa tải được");
            setStatus("Chưa tải được tồn. Bấm Cập nhật tồn để thử lại.", "warning");
        } finally {
            if (currentSequence === sequence) refreshButton.disabled = false;
        }
    }

    refreshButton.addEventListener("click", reload);
    document.addEventListener("invoice:lines-changed", reload);
    reload();
})();
