(() => {
    "use strict";

    const workspace = window.purchaseReceiptSplitWorkspace;
    const root = document.getElementById("splitReceiptWorkspace");
    if (!workspace || !root) return;

    const sourceId = Number(root.dataset.sourceId);
    const apiBase = root.dataset.apiBase;
    const cards = document.getElementById("splitTargetCards");
    const remainingRows = document.getElementById("splitRemainingRows");
    const message = document.getElementById("splitReceiptMessage");
    const resultLinks = document.getElementById("splitReceiptResultLinks");
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    const template = document.getElementById("splitTargetTemplate");
    let nextTargetKey = 3;

    const makeAllocations = (first) => Object.fromEntries(
        workspace.lines.map(line => [line.sourceLineId, first ? Number(line.baseQuantity) : 0]));

    const targets = [
        {
            key: 1,
            supplierId: workspace.supplierId,
            warehouseId: workspace.warehouseId,
            allocations: makeAllocations(true),
            isMerchandisePaid: workspace.isMerchandisePaid,
            merchandisePayeeName: workspace.merchandisePayeeName || "",
            paymentStateConfirmed: true,
            existingInputInvoiceHeadId: workspace.sourceInvoiceHeadId || null,
            invoiceDocumentKey: null,
            invoiceLabel: workspace.sourceInvoiceLabel || "Chưa chọn",
            invoiceDate: null
        },
        {
            key: 2,
            supplierId: workspace.supplierId,
            warehouseId: workspace.warehouseId,
            allocations: makeAllocations(false),
            isMerchandisePaid: workspace.isMerchandisePaid,
            merchandisePayeeName: workspace.merchandisePayeeName || "",
            paymentStateConfirmed: true,
            existingInputInvoiceHeadId: null,
            invoiceDocumentKey: null,
            invoiceLabel: "Chưa chọn",
            invoiceDate: null
        }
    ];

    const escapeHtml = value => String(value ?? "")
        .replaceAll("&", "&amp;").replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;").replaceAll('"', "&quot;");

    const formatDate = value => {
        const raw = String(value || "").slice(0, 10);
        const parts = raw.split("-");
        return parts.length === 3 ? `${parts[2]}/${parts[1]}/${parts[0]}` : raw;
    };

    const showMessage = (text, kind = "danger") => {
        message.innerHTML = `<div class="alert alert-${kind}">${escapeHtml(text)}</div>`;
    };

    const updateRemaining = () => {
        remainingRows.innerHTML = workspace.lines.map(line => {
            const allocated = targets.reduce(
                (sum, target) => sum + Number(target.allocations[line.sourceLineId] || 0), 0);
            const remaining = Number(line.baseQuantity) - allocated;
            const css = Math.abs(remaining) < 0.0005 ? "text-success" : "text-danger";
            return `<tr><td>${line.lineNo}</td><td>${escapeHtml(line.productName)}</td>` +
                `<td>${line.baseQuantity}</td><td class="fw-semibold ${css}">${remaining.toFixed(3)}</td></tr>`;
        }).join("");
    };

    function addTarget() {
        targets.push({
            key: nextTargetKey++,
            supplierId: workspace.supplierId,
            warehouseId: workspace.warehouseId,
            allocations: makeAllocations(false),
            isMerchandisePaid: workspace.isMerchandisePaid,
            merchandisePayeeName: workspace.merchandisePayeeName || "",
            paymentStateConfirmed: true,
            existingInputInvoiceHeadId: null,
            invoiceDocumentKey: null,
            invoiceLabel: "Chưa chọn",
            invoiceDate: null
        });
        render();
    }

    function removeTarget(key) {
        if (targets.length <= 2) {
            showMessage("Tách phiếu cần ít nhất hai phiếu kết quả.");
            return;
        }
        const index = targets.findIndex(target => target.key === key);
        if (index >= 0) targets.splice(index, 1);
        render();
    }

    const optionHtml = (items, selected) => items.map(item =>
        `<option value="${item.id}" ${Number(item.id) === Number(selected) ? "selected" : ""}>` +
        `${escapeHtml(item.code)} - ${escapeHtml(item.name)}</option>`).join("");

    async function browseInvoice(target, host) {
        host.innerHTML = '<span class="text-muted">Đang tải...</span>';
        const url = `${apiBase}/split/invoice-picker/browse?supplierId=${target.supplierId}&warehouseId=${target.warehouseId}`;
        const response = await fetch(url, { headers: { Accept: "application/json" } });
        const payload = await readPayload(response);
        if (!response.ok) throw new Error(payload.message || "Không tải được thư viện hóa đơn.");
        const candidates = payload.candidates || [];
        if (!candidates.length) {
            host.innerHTML = '<div class="text-muted">Không tìm thấy hóa đơn phù hợp.</div>';
            return;
        }
        host.innerHTML = `<select class="form-select form-select-sm split-candidate-select">` +
            `<option value="">-- Chọn một hóa đơn --</option>` +
            candidates.map(candidate => {
                const label = `${formatDate(candidate.invoiceDate)} · ${candidate.invoiceSeries || ""} ${candidate.invoiceNumber || ""} · ${candidate.sellerName || ""}`;
                return `<option value="${escapeHtml(candidate.documentKey)}" ` +
                    `data-date="${escapeHtml(candidate.invoiceDate || "")}" ` +
                    `data-label="${escapeHtml(label)}" ` +
                    `data-pdf="${candidate.hasPdf}" data-xml="${candidate.hasXml}" ` +
                    `data-allowed="${candidate.selectionAllowed}" ` +
                    `data-reason="${escapeHtml(candidate.selectionBlockMessage || '')}">` +
                    `${escapeHtml(label)} · ${candidate.buyerOwnerMatchesReceipt ? 'Đúng pháp nhân' : 'Không thể liên kết'}</option>`;
            }).join("") + `</select><div class="small text-muted mt-1">Ứng viên không hợp lệ vẫn xem trước được; hệ thống chặn gán.</div>`;
        const select = host.querySelector("select");
        select.addEventListener("change", () => {
            const option = select.selectedOptions[0];
            if (!option?.value) return;
            target.existingInputInvoiceHeadId = null;
            target.invoiceDocumentKey = option.value;
            target.invoiceDate = option.dataset.date || null;
            target.invoiceLabel = option.dataset.label || option.textContent;
            target.invoiceSelectionAllowed = option.dataset.allowed === "true";
            target.invoiceSelectionBlockMessage = option.dataset.reason || "Chủ thể người mua không khớp kho đích.";
            render();
        });
    }

    async function previewInvoice(target, host) {
        if (!target.invoiceDocumentKey) return;
        const query = `supplierId=${target.supplierId}&warehouseId=${target.warehouseId}&documentKey=${encodeURIComponent(target.invoiceDocumentKey)}`;
        const pdfUrl = `${apiBase}/split/invoice-picker/pdf?${query}`;
        const xmlUrl = `${apiBase}/split/invoice-picker/xml?${query}`;
        host.innerHTML = `<div class="btn-group btn-group-sm" role="group">` +
            `<button type="button" class="btn btn-outline-secondary split-preview-pdf">Xem PDF</button>` +
            `<button type="button" class="btn btn-outline-secondary split-preview-xml">Xem XML</button></div>` +
            `<div class="split-preview-content mt-2"></div>`;
        const content = host.querySelector(".split-preview-content");
        host.querySelector(".split-preview-pdf").addEventListener("click", () => {
            content.innerHTML = `<iframe title="PDF hóa đơn" src="${pdfUrl}" style="width:100%;height:520px;border:1px solid #ddd"></iframe>`;
        });
        host.querySelector(".split-preview-xml").addEventListener("click", async () => {
            content.innerHTML = '<span class="text-muted">Đang tải XML...</span>';
            const response = await fetch(xmlUrl);
            content.innerHTML = response.ok ? await response.text() : "Không xem được XML.";
        });
    }

    function wireTarget(card, target, targetIndex) {
        card.querySelector(".split-target-title").textContent = `Phiếu kết quả ${targetIndex + 1}`;
        const remove = card.querySelector(".split-remove-target");
        remove.disabled = targetIndex === 0 || targets.length <= 2;
        remove.addEventListener("click", () => removeTarget(target.key));

        const supplier = card.querySelector(".split-supplier");
        supplier.innerHTML = optionHtml(workspace.suppliers, target.supplierId);
        supplier.addEventListener("change", () => {
            target.supplierId = Number(supplier.value);
            target.existingInputInvoiceHeadId = null;
            target.invoiceDocumentKey = null;
            target.invoiceLabel = "Chưa chọn";
            target.invoiceDate = null;
            target.paymentStateConfirmed = target.supplierId === workspace.supplierId;
            render();
        });

        const warehouse = card.querySelector(".split-warehouse");
        warehouse.innerHTML = optionHtml(workspace.warehouses, target.warehouseId);
        warehouse.addEventListener("change", () => {
            target.warehouseId = Number(warehouse.value);
            target.existingInputInvoiceHeadId = null;
            target.invoiceDocumentKey = null;
            target.invoiceSelectionAllowed = true;
            target.invoiceLabel = "Chưa chọn";
            render();
        });
        card.querySelector(".split-warehouse-date").value = formatDate(target.invoiceDate || workspace.documentDate);

        const paymentBox = card.querySelector(".split-payment-box");
        paymentBox.classList.toggle("border-warning", target.supplierId !== workspace.supplierId);
        const paymentConfirm = card.querySelector(".split-payment-confirm");
        paymentConfirm.checked = target.paymentStateConfirmed;
        paymentConfirm.disabled = target.supplierId === workspace.supplierId;
        paymentConfirm.addEventListener("change", () => { target.paymentStateConfirmed = paymentConfirm.checked; });
        const paid = card.querySelector(".split-paid");
        paid.checked = Boolean(target.isMerchandisePaid);
        paid.addEventListener("change", () => { target.isMerchandisePaid = paid.checked; });
        const payee = card.querySelector(".split-payee");
        payee.value = target.merchandisePayeeName || "";
        payee.addEventListener("input", () => { target.merchandisePayeeName = payee.value; });

        card.querySelector(".split-invoice-label").textContent = target.invoiceLabel;
        const browser = card.querySelector(".split-invoice-browser");
        card.querySelector(".split-browse-invoice").addEventListener("click", async () => {
            try { await browseInvoice(target, browser); } catch (error) { showMessage(error.message); }
        });
        card.querySelector(".split-clear-invoice").addEventListener("click", () => {
            target.existingInputInvoiceHeadId = null;
            target.invoiceDocumentKey = null;
            target.invoiceLabel = "Chưa chọn";
            target.invoiceDate = null;
            render();
        });
        previewInvoice(target, card.querySelector(".split-invoice-preview"));

        card.querySelector(".split-allocation-rows").innerHTML = workspace.lines.map(line =>
            `<tr><td>${line.lineNo} · ${escapeHtml(line.productName)}</td><td>${line.baseQuantity}</td>` +
            `<td><input class="form-control form-control-sm split-allocation" type="number" min="0" step="0.001" ` +
            `data-line-id="${line.sourceLineId}" value="${target.allocations[line.sourceLineId] || 0}"></td></tr>`).join("");
        card.querySelectorAll(".split-allocation").forEach(input => input.addEventListener("input", () => {
            target.allocations[Number(input.dataset.lineId)] = Number(input.value || 0);
            updateRemaining();
        }));
    }

    function render() {
        cards.innerHTML = "";
        targets.forEach((target, index) => {
            const fragment = template.content.cloneNode(true);
            const card = fragment.querySelector(".split-target-card");
            wireTarget(card, target, index);
            cards.appendChild(fragment);
        });
        updateRemaining();
    }

    const buildRequest = () => ({
        sourceRowVersion: workspace.rowVersion,
        sourceInvoiceHeadId: workspace.sourceInvoiceHeadId,
        sourceLines: workspace.lines.map(line => ({
            sourceLineId: line.sourceLineId,
            baseQuantity: line.baseQuantity,
            rowVersion: line.rowVersion
        })),
        targets: targets.map((target, index) => ({
            targetIndex: index + 1,
            warehouseId: target.warehouseId,
            supplierId: target.supplierId,
            isMerchandisePaid: target.isMerchandisePaid,
            merchandisePayeeName: target.merchandisePayeeName || null,
            paymentStateConfirmed: target.paymentStateConfirmed,
            existingInputInvoiceHeadId: target.existingInputInvoiceHeadId,
            invoiceDocumentKey: target.invoiceDocumentKey,
            allocations: workspace.lines.map(line => ({
                sourceLineId: line.sourceLineId,
                baseQuantity: Number(target.allocations[line.sourceLineId] || 0)
            }))
        }))
    });

    async function readPayload(response) {
        const contentType = response.headers.get("content-type") || "";
        return contentType.includes("application/json") ? response.json() : { message: await response.text() };
    }

    async function submitSplit() {
        const invalidInvoiceTarget = targets.find(target =>
            target.invoiceDocumentKey && target.invoiceSelectionAllowed === false);
        if (invalidInvoiceTarget) {
            showMessage(invalidInvoiceTarget.invoiceSelectionBlockMessage ||
                "Hóa đơn không đúng pháp nhân của kho đích; không thể gán vào phiếu kết quả.");
            return;
        }
        const hasRemainder = workspace.lines.some(line => {
            const sum = targets.reduce((total, target) => total + Number(target.allocations[line.sourceLineId] || 0), 0);
            return Math.abs(Number(line.baseQuantity) - sum) >= 0.0005;
        });
        if (hasRemainder) {
            showMessage("Phân bổ chưa bảo toàn toàn bộ BaseQuantity; Còn lại phải bằng 0.");
            return;
        }
        if (!window.confirm("Xác nhận tách phiếu nhập? Thao tác sẽ tạo và cập nhật các phiếu kết quả trong một transaction.")) return;

        const button = document.getElementById("btnSubmitSplitReceipt");
        button.disabled = true;
        try {
            const response = await fetch(`${apiBase}/${sourceId}/split`, {
                method: "POST",
                headers: { "Content-Type": "application/json", RequestVerificationToken: token },
                body: JSON.stringify(buildRequest())
            });
            const payload = await readPayload(response);
            if (!response.ok) throw new Error(payload.message || "Không thể tách phiếu nhập.");
            showMessage(payload.message || "Đã tách phiếu nhập.", "success");
            resultLinks.innerHTML = `<div class="card"><div class="card-body"><h5>Kết quả</h5><ul class="mb-0">` +
                (payload.resultLinks || []).map(link =>
                    `<li><a href="${escapeHtml(link.url)}">${escapeHtml(link.documentNo)}</a></li>`).join("") +
                `</ul></div></div>`;
        } catch (error) {
            showMessage(error.message);
            button.disabled = false;
        }
    }

    document.getElementById("btnAddSplitTarget").addEventListener("click", addTarget);
    document.getElementById("btnSubmitSplitReceipt").addEventListener("click", submitSplit);
    render();
})();
