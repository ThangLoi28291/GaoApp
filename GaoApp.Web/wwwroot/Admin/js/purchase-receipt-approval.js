(function () {
    'use strict';

    let manualFreightAllocation = false;
    let commercialReconciliationPreviewTimer = null;
    let commercialReconciliationPreviewAbortController = null;
    let previewRequestVersion = 0;
    let supplierSaving = false;
    let supplierSaveFailed = false;
    let imagePreviewBound = false;

    // Expose this before DOMContentLoaded so the generic receipt page can
    // delegate without depending on script event-handler order.
    window.GaoAppPurchaseReceiptApproval = {
        submit: submitCommercialApproval,
        open: openCommercialApprovalConfirmation,
        prepareConfirmation: prepareCommercialApprovalConfirmation,
        refreshReconciliationPreview: refreshCommercialReconciliationPreview,
        refreshAfterIntake: refreshAfterIntake,
        supplierForIntake: supplierForIntake,
        saveSupplierForIntake: saveSupplierForIntake
    };

    document.addEventListener('DOMContentLoaded', initCommercialApproval);

    async function refreshAfterIntake() {
        const current = document.getElementById('commercialApprovalWorkbench');
        if (!current) return;
        const response = await fetch(window.location.href, { cache: 'no-store', headers: { Accept: 'text/html' } });
        if (!response.ok) throw new Error('Đã lưu hàng nhận nhưng chưa tải được phần duyệt phiếu. Vui lòng tải lại trang.');
        const fresh = new DOMParser().parseFromString(await response.text(), 'text/html');
        const next = fresh.getElementById('commercialApprovalWorkbench');
        if (!next) throw new Error('Phiếu vừa đổi trạng thái. Vui lòng tải lại trang.');
        // Keep prices/settlement currently being entered. Quantities and the item list come from the server.
        const selectorFor = element => {
            if (element.id) return '#' + CSS.escape(element.id);
            const row = element.closest('[data-line-id]');
            const field = [...element.classList].find(x => x.startsWith('commercial-'));
            return row && field && !field.includes('quantity') ? `[data-line-id="${row.dataset.lineId}"] .${CSS.escape(field)}` : null;
        };
        for (const field of current.querySelectorAll('input,select,textarea')) {
            const selector = selectorFor(field), target = selector ? next.querySelector(selector) : null;
            if (!target) continue;
            if (field.tagName === 'SELECT' && ![...target.options].some(x => x.value === field.value))
                for (const option of field.selectedOptions) target.append(option.cloneNode(true));
            target.value = field.value; target.checked = field.checked;
        }
        current.replaceWith(next);
        const summary = document.querySelector('.sd-manager-summary'), freshSummary = fresh.querySelector('.sd-manager-summary');
        if (summary && freshSummary) summary.replaceWith(freshSummary);
        const openButton = document.getElementById('btnOpenApproveModal'), freshButton = fresh.getElementById('btnOpenApproveModal');
        if (openButton && freshButton) openButton.disabled = freshButton.disabled;
        window.initStockDocumentWorkbenchTabs?.();
        initCommercialApproval();
        window.GaoReceiptPriceReviews?.refresh();
    }

    function initCommercialApproval() {
        bindCommercialImagePreview();
        const workbench = document.getElementById('commercialApprovalWorkbench');
        if (!workbench) return;

        bindCommercialImagePreview();

        const readOnly = workbench.dataset.readonly === 'true';
        const canApprove = window.stockDocumentPage?.canApproveCommercial === true;
        const isPurchaseOrderReceipt = Number(window.stockDocumentPage?.receiptSource) === 2;
        const supplier = document.getElementById('commercialSupplierId');
        const hasPersistedFreight = document.getElementById('commercialHasFreight')?.checked === true;
        const hasPersistedCapitalization = document.getElementById('capitalizeFreightInInventoryCost')?.checked === true;
        manualFreightAllocation = !readOnly && hasPersistedFreight && hasPersistedCapitalization &&
            Array.from(document.querySelectorAll('.commercial-freight-allocation'))
                .some(function (input) { return readNumber(input) > 0; });

        if (!readOnly && !isPurchaseOrderReceipt) {
            initCommercialSupplierLookup(supplier);
        } else if (supplier) {
            supplier.disabled = true;
        }

        bindCommercialInputs(readOnly);
        bindCommercialActions(canApprove, readOnly);
        window.GaoReceiptPriceDrafts?.init();
        window.GaoReceiptPricingAllocation?.init();
        syncVatState();
        syncFreightState(false);
        recalculateCommercialTotals();
        if (!readOnly) scheduleCommercialReconciliationPreview();
    }

    function bindCommercialImagePreview() {
        if (imagePreviewBound) return;
        imagePreviewBound = true;
        let preview = null;
        let active = null;
        const buttonFor = target => target?.closest?.('.sd-commercial-image-button');
        function hide() {
            if (preview) preview.hidden = true;
            active?.setAttribute('aria-expanded', 'false');
            active = null;
        }
        function show(button) {
            if (!button || button.disabled) return;
            const thumbnail = button.querySelector('img');
            if (!thumbnail || thumbnail.hidden || (thumbnail.complete && !thumbnail.naturalWidth)) return;
            if (!preview) {
                preview = document.createElement('div');
                preview.id = 'commercialImagePreview';
                preview.setAttribute('aria-hidden', 'true');
                const image = document.createElement('img');
                image.alt = '';
                image.addEventListener('error', hide);
                preview.appendChild(image);
                document.body.appendChild(preview);
            }
            hide();
            active = button;
            const size = Math.max(1, Math.min(300, window.innerWidth - 24, window.innerHeight - 24));
            const rect = button.getBoundingClientRect();
            let left = rect.right + 12;
            if (left + size > window.innerWidth - 12) left = rect.left - size - 12;
            preview.style.width = preview.style.height = size + 'px';
            preview.style.left = Math.max(12, Math.min(left, window.innerWidth - size - 12)) + 'px';
            preview.style.top = Math.max(12, Math.min(rect.top, window.innerHeight - size - 12)) + 'px';
            preview.querySelector('img').src = thumbnail.currentSrc || thumbnail.src;
            preview.hidden = false;
            button.setAttribute('aria-expanded', 'true');
        }
        // Delegation also handles rows rebuilt after receiving more items.
        document.addEventListener('pointerover', event => {
            if (event.pointerType !== 'touch') show(buttonFor(event.target));
        });
        document.addEventListener('pointerout', event => {
            const button = buttonFor(event.target);
            if (button && !button.contains(event.relatedTarget)) hide();
        });
        document.addEventListener('focusin', event => show(buttonFor(event.target)));
        document.addEventListener('focusout', event => { if (buttonFor(event.target)) hide(); });
        document.addEventListener('click', event => {
            const button = buttonFor(event.target);
            if (button) show(button); else hide();
        });
        document.addEventListener('keydown', event => { if (event.key === 'Escape') hide(); });
        document.addEventListener('scroll', hide, true);
        window.addEventListener('resize', hide);
        document.addEventListener('error', event => {
            if (!event.target.matches?.('.sd-commercial-thumbnail')) return;
            const button = buttonFor(event.target);
            event.target.hidden = true;
            if (button) {
                button.disabled = true;
                button.setAttribute('aria-label', 'Không tải được ảnh sản phẩm');
                button.querySelector('i').hidden = false;
                if (button === active) hide();
            }
        }, true);
    }

    function initCommercialSupplierLookup(element) {
        if (!element) return;
        if (!window.jQuery || !jQuery.fn.select2) {
            populateNativeSupplierOptions(element);
            element.addEventListener('change', saveCommercialSupplier);
            return;
        }

        const $element = jQuery(element);
        $element.select2({
            theme: 'bootstrap-5',
            width: '100%',
            placeholder: 'Gõ để tìm nhà cung cấp...',
            allowClear: true,
            minimumInputLength: 1,
            ajax: {
                url: '/admin/api/suppliers/select2',
                dataType: 'json',
                delay: 250,
                data: function (params) {
                    return { term: params.term || '' };
                },
                processResults: function (data) {
                    return data || { results: [] };
                }
            },
            language: {
                inputTooShort: function () { return 'Nhập tên, mã, số điện thoại hoặc mã số thuế'; },
                searching: function () { return 'Đang tìm...'; },
                noResults: function () { return 'Không tìm thấy nhà cung cấp'; },
                errorLoading: function () { return 'Không tải được dữ liệu'; }
            }
        });

        $element.on('change', function () {
            clearCommercialError();
            syncSettlementState();
            saveCommercialSupplier();
        });
    }

    function supplierForIntake() {
        const workbench = document.getElementById('commercialApprovalWorkbench');
        if (!workbench) return null;
        return {
            id: nullablePositiveInt(document.getElementById('CurrentSupplierId')?.value),
            name: document.getElementById('CurrentSupplierText')?.value || '',
            canEdit: workbench.dataset.readonly !== 'true' && window.stockDocumentPage?.canApproveCommercial === true &&
                Number(window.stockDocumentPage?.receiptSource) !== 2,
            busy: supplierSaving
        };
    }

    async function saveSupplierForIntake(id, name) {
        const current = supplierForIntake();
        if (!current || current.busy) throw new Error('Đang lưu nhà cung cấp. Vui lòng đợi rồi duyệt lại.');
        if (!(id > 0)) throw new Error('Vui lòng chọn nhà cung cấp trước khi duyệt sản phẩm mới.');
        if (current.id === id && !supplierSaveFailed) return;
        if (!current.canEdit) throw new Error('Nhà cung cấp được lấy từ đơn mua; không thể đổi tại đây.');
        const element = document.getElementById('commercialSupplierId');
        if (!element) throw new Error('Chưa tải được nhà cung cấp của phiếu. Vui lòng tải lại.');
        if (![...element.options].some(option => Number(option.value) === id)) element.add(new Option(name, String(id)));
        element.value = String(id);
        if (window.jQuery) jQuery(element).trigger('change.select2');
        clearCommercialError(); syncSettlementState();
        const saved = await saveCommercialSupplier();
        if (!saved) throw new Error(document.getElementById('commercialSupplierSaveState')?.textContent || 'Không lưu được nhà cung cấp.');
    }

    async function saveCommercialSupplier() {
        const element = document.getElementById('commercialSupplierId');
        if (!element || supplierSaving) return;
        const status = document.getElementById('commercialSupplierSaveState');
        const picker = document.getElementById('btnOpenInputInvoicePicker');
        const supplierId = nullablePositiveInt(element.value);
        supplierSaving = true;
        supplierSaveFailed = false;
        element.disabled = true;
        if (picker) picker.disabled = true;
        document.dispatchEvent(new Event('input-invoice-supplier-changing'));
        window.invalidateInputInvoiceAssociation?.();
        if (status) { status.className = 'small mt-1 text-muted'; status.textContent = 'Đang lưu nhà cung cấp...'; }
        let releaseDrafts;
        const association = document.getElementById('inputInvoiceAssociationState');
        if (association) { association.className = 'small mt-2 text-muted'; association.textContent = 'Đang cập nhật nhà cung cấp...'; }
        try {
            releaseDrafts = await window.GaoReceiptPriceDrafts?.hold();
            const response = await fetch('/admin/stock-documents/update-header', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                },
                body: JSON.stringify({
                    stockDocumentId: Number(window.stockDocumentPage?.documentId || 0),
                    rowVersion: window.stockDocumentRowVersion?.current?.() || window.stockDocumentPage?.rowVersion || '',
                    warehouseId: nullablePositiveInt(document.getElementById('CurrentWarehouseId')?.value),
                    legalEntityId: nullablePositiveInt(document.getElementById('CurrentLegalEntityId')?.value),
                    supplierId: supplierId
                })
            });
            const api = await readJsonResponse(response);
            if (!api.ok || api.data?.success !== true || !api.data?.rowVersion) {
                throw new Error(api.data?.message || 'Không lưu được nhà cung cấp. Vui lòng tải lại phiếu và thử lại.');
            }
            const saved = api.data;
            if (window.stockDocumentRowVersion?.update) window.stockDocumentRowVersion.update(saved.rowVersion);
            else window.stockDocumentPage.rowVersion = saved.rowVersion;
            document.getElementById('CurrentSupplierId').value = saved.supplierId || '';
            document.getElementById('CurrentSupplierText').value = saved.supplierName || '';
            setText(document.getElementById('inputInvoiceSupplierName'), saved.supplierName || 'Chưa chọn');
            setText(document.getElementById('inputInvoiceSupplierTaxCode'), saved.supplierTaxCode || 'Chưa có');
            const missingSupplier = document.getElementById('inputInvoiceSupplierMissing');
            const missingTax = document.getElementById('inputInvoiceSupplierTaxMissing');
            if (missingSupplier) missingSupplier.hidden = Boolean(saved.supplierId);
            if (missingTax) missingTax.hidden = !saved.supplierId || Boolean(saved.supplierTaxCode?.trim());
            if (picker) picker.disabled = !saved.supplierId || !saved.supplierTaxCode?.trim();
            if (status) { status.className = 'small mt-1 text-success'; status.textContent = 'Đã lưu nhà cung cấp.'; }
        } catch (error) {
            supplierSaveFailed = true;
            const originalId = document.getElementById('CurrentSupplierId')?.value || '';
            const originalText = document.getElementById('CurrentSupplierText')?.value || '';
            if (originalId && !Array.from(element.options).some(option => option.value === originalId))
                element.add(new Option(originalText, originalId));
            element.value = originalId;
            if (window.jQuery) jQuery(element).trigger('change.select2');
            if (status) { status.className = 'small mt-1 text-danger'; status.textContent = error.message; }
            if (association) { association.className = 'small mt-2 text-danger'; association.textContent = 'Chưa cập nhật được nhà cung cấp. Vui lòng chọn lại hoặc tải lại phiếu.'; }
        } finally {
            releaseDrafts?.();
            supplierSaving = false;
            element.disabled = false;
            syncSettlementState();
        }
        if (!supplierSaveFailed) {
            try { await window.loadInputInvoiceAssociation?.(); }
            catch (_) { if (association) association.textContent = 'Đã lưu nhà cung cấp. Chưa tải được trạng thái XML; vui lòng thử mở Chọn hóa đơn.'; }
        }
        return !supplierSaveFailed;
    }

    async function populateNativeSupplierOptions(element) {
        try {
            const response = await fetch('/admin/api/suppliers/select2?term=');
            const api = await readJsonResponse(response);
            const items = api.data?.results || [];
            items.forEach(function (item) {
                const exists = Array.from(element.options).some(function (option) {
                    return String(option.value) === String(item?.id);
                });
                if (!item?.id || exists) return;
                element.add(new Option(item.text || `NCC #${item.id}`, item.id));
            });
        } catch (error) {
            console.warn('Không tải được danh sách nhà cung cấp dự phòng.', error);
        }
    }

    function bindCommercialInputs(readOnly) {
        if (readOnly) return;

        document.getElementById('commercialHasVat')?.addEventListener('change', function () {
            syncVatState();
            recalculateCommercialTotals();
            if (!manualFreightAllocation) {
                autoAllocateFreight();
                recalculateCommercialTotals();
            }
            scheduleCommercialReconciliationPreview();
        });

        document.getElementById('includeVatInInventoryCost')?.addEventListener('change', recalculateCommercialTotals);
        document.getElementById('commercialMerchandisePaid')?.addEventListener('change', syncSettlementState);
        document.getElementById('commercialHasFreight')?.addEventListener('change', function () {
            manualFreightAllocation = false;
            syncFreightState(true);
            autoAllocateFreight();
            recalculateCommercialTotals();
        });
        document.getElementById('capitalizeFreightInInventoryCost')?.addEventListener('change', function () {
            manualFreightAllocation = false;
            syncFreightState(true);
            autoAllocateFreight();
            recalculateCommercialTotals();
        });

        document.getElementById('commercialFreightTotal')?.addEventListener('input', function () {
            if (!manualFreightAllocation) autoAllocateFreight();
            recalculateCommercialTotals();
        });

        document.getElementById('commercialAutoAllocate')?.addEventListener('click', function () {
            manualFreightAllocation = false;
            autoAllocateFreight();
            recalculateCommercialTotals();
        });

        document.querySelectorAll('.commercial-unit-price, .commercial-tax').forEach(function (input) {
            const refresh = function () {
                if (input.classList.contains('commercial-tax')) {
                    if (input.value) input.dataset.previousTaxId = input.value;
                    else delete input.dataset.previousTaxId;
                }
                recalculateCommercialTotals();
                if (!manualFreightAllocation) {
                    autoAllocateFreight();
                    recalculateCommercialTotals();
                }
                scheduleCommercialReconciliationPreview();
            };
            input.addEventListener('input', refresh);
            input.addEventListener('change', refresh);
        });

        document.querySelectorAll('.commercial-freight-allocation').forEach(function (input) {
            input.addEventListener('input', function () {
                manualFreightAllocation = true;
                recalculateCommercialTotals();
            });
        });

        document.querySelectorAll('.commercial-quantity').forEach(function (input) {
            input.addEventListener('change', function () { savePhysicalQuantity(input); });
            input.addEventListener('keydown', function (event) {
                if (event.key !== 'Enter') return;
                event.preventDefault();
                input.blur();
            });
        });

        syncSettlementState();
    }

    function scheduleCommercialReconciliationPreview() {
        if (!document.getElementById('inputInvoiceReconciliationPanel')) return;
        if (commercialReconciliationPreviewTimer) {
            window.clearTimeout(commercialReconciliationPreviewTimer);
        }
        const requestVersion = ++previewRequestVersion;
        commercialReconciliationPreviewTimer = window.setTimeout(function () {
            requestCommercialReconciliationPreview(requestVersion);
        }, 350);
    }

    async function refreshCommercialReconciliationPreview() {
        const workbench = document.getElementById('commercialApprovalWorkbench');
        if (!workbench || workbench.dataset.readonly === 'true') return;
        if (commercialReconciliationPreviewTimer) {
            window.clearTimeout(commercialReconciliationPreviewTimer);
            commercialReconciliationPreviewTimer = null;
        }
        const requestVersion = ++previewRequestVersion;
        await requestCommercialReconciliationPreview(requestVersion);
    }

    async function requestCommercialReconciliationPreview(requestVersion) {
        const documentId = Number(window.stockDocumentPage?.documentId || 0);
        const rowVersion = String(window.stockDocumentPage?.rowVersion || '');
        const hasVat = document.getElementById('commercialHasVat')?.checked === true;
        const lines = getCommercialRows().map(function (row) {
            const tax = row.querySelector('.commercial-tax');
            return {
                stockDocumentLineId: Number(row.dataset.lineId),
                unitPriceBeforeVat: roundMoney(
                    readNumber(row.querySelector('.commercial-unit-price'))),
                taxId: hasVat && tax?.value ? Number(tax.value) : null
            };
        });
        const ready = documentId > 0 && rowVersion && lines.length > 0 &&
            lines.every(function (line) {
                return line.stockDocumentLineId > 0 &&
                    Number.isFinite(line.unitPriceBeforeVat) &&
                    line.unitPriceBeforeVat > 0 &&
                    (!hasVat || Number.isInteger(line.taxId) && line.taxId > 0);
        });
        if (!ready || requestVersion !== previewRequestVersion) return;

        const reconciliationRenderGeneration =
            window.beginInputInvoiceReconciliationRenderRequest?.();
        if (!Number.isInteger(reconciliationRenderGeneration)) return;

        commercialReconciliationPreviewAbortController?.abort();
        const controller = new AbortController();
        commercialReconciliationPreviewAbortController = controller;
        try {
            const response = await fetch(
                `/admin/api/stock-documents/${documentId}/input-invoices/reconciliation/preview`,
                {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json' },
                    signal: controller.signal,
                    body: JSON.stringify({
                        stockDocumentId: documentId,
                        rowVersion: rowVersion,
                        hasVat: hasVat,
                        lines: lines
                    })
                });
            const api = await readJsonResponse(response);
            if (requestVersion !== previewRequestVersion) return;
            if (!api.ok) throw new Error(api.data?.message ||
                'Không thể xem trước đối chiếu XML.');
            window.renderInputInvoiceReconciliationPreview?.(api.data, reconciliationRenderGeneration);
        } catch (error) {
            if (error?.name !== 'AbortError' && requestVersion === previewRequestVersion) {
                console.warn('Không thể cập nhật xem trước đối chiếu XML.', error);
            }
        } finally {
            if (commercialReconciliationPreviewAbortController === controller) {
                commercialReconciliationPreviewAbortController = null;
            }
        }
    }

    function bindCommercialActions(canApprove, readOnly) {
        if (readOnly || !canApprove) return;

        document.getElementById('commercialRejectButton')?.addEventListener('click', function () {
            document.getElementById('btnOpenRejectModal')?.click();
        });

        document.getElementById('commercialApproveButton')?.addEventListener('click', openCommercialApprovalConfirmation);

        // stock-document-management.js gắn handler legacy trước. Ghi đè nút xác nhận
        // để giá, VAT, công nợ, phí và nghiệp vụ ghi kho đi trong đúng một request.
        document.addEventListener('keydown', function (event) {
            if (!event.ctrlKey || event.key !== 'Enter' || event.altKey || event.metaKey) return;
            if (document.querySelector('.modal.show')) return;
            event.preventDefault();
            openCommercialApprovalConfirmation();
        });
    }

    function openCommercialApprovalConfirmation() {
        // The shared button handler prepares every entry point before showing the modal.
        document.getElementById('btnOpenApproveModal')?.click();
    }

    function prepareCommercialApprovalConfirmation() {
        const workbench = document.getElementById('commercialApprovalWorkbench');
        if (workbench?.dataset.hasPendingOutside === 'true') {
            window.activateStockDocumentWorkbenchTab?.('goods');
            const blocker = document.getElementById('managerApprovalBlockerSummary');
            fail('Còn hàng Ngoài PO chưa được Manager xử lý.', blocker, true);
            return false;
        }
        if (workbench?.dataset.hasUnresolvedProvisional === 'true') {
            window.activateStockDocumentWorkbenchTab?.('goods');
            const blocker = document.getElementById('managerApprovalBlockerSummary');
            fail('Còn sản phẩm chưa có trong danh mục cần xử lý.', blocker, true);
            return false;
        }
        if (workbench?.dataset.hasCatalogReview === 'true') {
            window.activateStockDocumentWorkbenchTab?.('goods');
            const blocker = document.getElementById('managerApprovalBlockerSummary');
            fail('Còn sản phẩm tạm cần hoàn thiện tên trong danh mục.', blocker, true);
            return false;
        }
        if (!validateCommercialApproval(true, false)) return false;
        renderPriceVarianceAcceptance();
        updateApproveModalSummary();
        return true;
    }

    async function submitCommercialApproval() {
        if (document.getElementById('btnApprove')?.disabled) return;
        if (!validateCommercialApproval(true, true)) {
            hideApproveModal();
            return;
        }

        const button = document.getElementById('btnApprove');
        const message = document.getElementById('approveMessage');
        const documentId = Number(window.stockDocumentPage?.documentId || 0);
        let releaseDrafts;
        let approvalSent = false;

        if (message) message.textContent = '';
        if (button) {
            button.disabled = true;
            button.dataset.originalText = button.innerHTML;
            button.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Đang ghi sổ...';
        }

        try {
            releaseDrafts = await window.GaoReceiptPriceDrafts?.hold();
            const waitForInputInvoice = await window.GaoReceiptInvoiceFollowUp.approvalChoice();
            const payload = buildCommercialPayload();
            payload.waitForInputInvoice = waitForInputInvoice;
            approvalSent = true;
            const response = await fetch(`/admin/api/stock-documents/${documentId}/approve-commercial`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            const api = await readJsonResponse(response);

            if (!api.ok) {
                if (message) message.textContent = api.data?.message || 'Không thể duyệt phiếu nhập.';
                return;
            }

            hideApproveModal();
            if (await window.GaoLabels?.afterApproval(documentId, document.getElementById('approvalQueueLabels')?.checked === true)) return;
            if (typeof window.showStockDocumentToast === 'function') {
                window.showStockDocumentToast(
                    'success',
                    'Đã duyệt và ghi sổ',
                    api.data?.message || 'Tồn kho, FIFO và công nợ đã được ghi nhận.',
                    api.data?.redirectUrl || '/admin/stock-documents');
            } else {
                window.location.href = api.data?.redirectUrl || `/admin/stock-documents/${documentId}`;
            }
        } catch (error) {
            console.error(error);
            if (message) message.textContent = approvalSent
                ? 'Chưa xác định được kết quả duyệt. Vui lòng tải lại phiếu để kiểm tra trước khi thử lại.'
                : error.message;
        } finally {
            releaseDrafts?.();
            if (button) {
                button.disabled = false;
                button.innerHTML = button.dataset.originalText || 'Xác nhận duyệt';
            }
        }
    }

    function buildCommercialPayload() {
        const hasVat = document.getElementById('commercialHasVat')?.checked === true;
        const hasFreight = document.getElementById('commercialHasFreight')?.checked === true;
        const includeVatInInventoryCost = hasVat && document.getElementById('includeVatInInventoryCost')?.checked === true;
        const capitalizeFreightInInventoryCost = hasFreight && document.getElementById('capitalizeFreightInInventoryCost')?.checked === true;
        const lines = getCommercialRows().map(function (row) {
            const tax = row.querySelector('.commercial-tax');
            const lastPrice = readLastPurchasePrice(row);
            return {
                stockDocumentLineId: Number(row.dataset.lineId),
                unitPriceBeforeVat: roundMoney(readNumber(row.querySelector('.commercial-unit-price'))),
                expectedLastPurchaseUnitPriceBeforeVat: lastPrice,
                taxId: hasVat && tax?.value ? Number(tax.value) : null
            };
        });

        return {
            rowVersion: String(window.stockDocumentPage?.rowVersion || ''),
            approvalNote: document.getElementById('approveNote')?.value?.trim() || null,
            acceptOverdelivery: document.getElementById('acceptOverdelivery')?.checked === true,
            overdeliveryNote: valueOrNull('overdeliveryNote'),
            acceptPriceVariance: document.getElementById('acceptPriceVariance')?.checked === true,
            hasVat: hasVat,
            includeVatInInventoryCost: includeVatInInventoryCost,
            supplierId: nullablePositiveInt(document.getElementById('commercialSupplierId')?.value),
            isMerchandisePaid: document.getElementById('commercialMerchandisePaid')?.checked === true,
            merchandisePayeeName: valueOrNull('commercialPayeeName'),
            lines: lines,
            hasFreight: hasFreight,
            capitalizeFreightInInventoryCost: capitalizeFreightInInventoryCost,
            freightTotal: hasFreight ? roundMoney(readNumber(document.getElementById('commercialFreightTotal'))) : 0,
            freightPayeeName: hasFreight ? valueOrNull('commercialFreightPayee') : null,
            freightNote: hasFreight ? valueOrNull('commercialFreightNote') : null,
            isFreightPaid: hasFreight && document.getElementById('commercialFreightPaid')?.checked === true,
            resetAutomaticAllocation: false,
            allocations: lines.map(function (line, index) {
                const row = getCommercialRows()[index];
                return {
                    stockDocumentLineId: line.stockDocumentLineId,
                    amount: capitalizeFreightInInventoryCost
                        ? roundMoney(readNumber(row.querySelector('.commercial-freight-allocation')))
                        : 0
                };
            })
        };
    }

    function validateCommercialApproval(focusInvalid, requireOverdeliveryAcceptance) {
        clearCommercialError();
        if (supplierSaving || supplierSaveFailed) {
            return fail(supplierSaving ? 'Đang lưu nhà cung cấp. Vui lòng đợi trước khi duyệt.'
                : 'Nhà cung cấp chưa được lưu thành công. Vui lòng chọn lại hoặc tải lại phiếu.',
                document.getElementById('commercialSupplierId'), focusInvalid);
        }
        const rows = getCommercialRows();
        if (!rows.length) return fail('Phiếu nhập chưa có dòng hàng.', null, focusInvalid);
        if (document.querySelector('.commercial-quantity[data-saving="true"]')) {
            return fail('Đang lưu số lượng thực nhận. Vui lòng chờ trong giây lát.', null, focusInvalid);
        }

        for (const row of rows) {
            const hasVat = document.getElementById('commercialHasVat')?.checked === true;
            const tax = row.querySelector('.commercial-tax');
            if (hasVat && !nullablePositiveInt(tax?.value)) {
                return fail(
                    'Khi bật VAT, mỗi dòng phải chọn một thuế suất đã cấu hình (kể cả thuế suất 0%).',
                    tax,
                    focusInvalid);
            }

            const price = readNumber(row.querySelector('.commercial-unit-price'));
            if (!Number.isFinite(price) || price <= 0) {
                row.querySelector('.commercial-price-error').textContent = 'Giá phải lớn hơn 0';
                return fail('Mỗi dòng thực nhận phải có giá nhập chưa VAT lớn hơn 0.', row.querySelector('.commercial-unit-price'), focusInvalid);
            }
        }

        const supplierId = nullablePositiveInt(document.getElementById('commercialSupplierId')?.value);
        if (!supplierId) {
            return fail('Vui lòng chọn nhà cung cấp trước khi duyệt nhập kho.', document.getElementById('commercialSupplierId'), focusInvalid);
        }

        const overdeliveryAcceptance = document.getElementById('acceptOverdelivery');
        if (requireOverdeliveryAcceptance && overdeliveryAcceptance && !overdeliveryAcceptance.checked) {
            return fail(
                'Vui lòng xác nhận đã kiểm tra và chấp nhận số lượng nhận vượt đơn đặt hàng.',
                overdeliveryAcceptance,
                focusInvalid);
        }

        const priceVarianceAcceptance = document.getElementById('acceptPriceVariance');
        if (requireOverdeliveryAcceptance &&
            getPriceVariances().length > 0 &&
            priceVarianceAcceptance &&
            !priceVarianceAcceptance.checked) {
            return fail(
                'Vui lòng xác nhận đã kiểm tra và chấp nhận chênh lệch giá nhập.',
                priceVarianceAcceptance,
                focusInvalid);
        }

        const hasFreight = document.getElementById('commercialHasFreight')?.checked === true;
        const capitalizeFreight = hasFreight && document.getElementById('capitalizeFreightInInventoryCost')?.checked === true;
        if (hasFreight) {
            const freightTotal = readNumber(document.getElementById('commercialFreightTotal'));
            const freightPayee = valueOrNull('commercialFreightPayee');
            if (!Number.isFinite(freightTotal) || freightTotal <= 0) {
                return fail('Đã chọn có phí vận chuyển: tổng phí phải lớn hơn 0.', document.getElementById('commercialFreightTotal'), focusInvalid);
            }
            if (!freightPayee) {
                return fail('Vui lòng nhập người hoặc đơn vị nhận tiền vận chuyển.', document.getElementById('commercialFreightPayee'), focusInvalid);
            }

            if (capitalizeFreight) {
                const allocated = roundMoney(rows.reduce(function (sum, row) {
                    return sum + readNumber(row.querySelector('.commercial-freight-allocation'));
                }, 0));
                if (roundMoney(allocated - freightTotal) !== 0) {
                    return fail('Tổng phí phân bổ phải bằng đúng tổng phí vận chuyển.', document.querySelector('.commercial-freight-allocation'), focusInvalid);
                }
            }
        }

        return true;
    }

    function fail(message, element, focusInvalid) {
        const tabName = element?.closest?.('[data-workbench-panel]')?.dataset.workbenchPanel;
        if (tabName) window.activateStockDocumentWorkbenchTab?.(tabName);
        const box = document.getElementById('commercialValidationMessage');
        if (box) {
            box.textContent = message;
            box.classList.remove('d-none');
        }
        if (focusInvalid && element) {
            document.getElementById('commercialApprovalWorkbench')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
            window.setTimeout(function () {
                if (window.jQuery && jQuery.fn?.select2 && element.id === 'commercialSupplierId') {
                    jQuery(element).select2('open');
                } else {
                    element.focus();
                    if (typeof element.select === 'function') element.select();
                }
            }, 260);
        }
        return false;
    }

    function clearCommercialError() {
        const box = document.getElementById('commercialValidationMessage');
        if (box) {
            box.textContent = '';
            box.classList.add('d-none');
        }
        document.querySelectorAll('.commercial-price-error').forEach(function (x) { x.textContent = ''; });
    }

    function syncVatState() {
        const readOnly = document.getElementById('commercialApprovalWorkbench')?.dataset.readonly === 'true';
        const hasVat = document.getElementById('commercialHasVat')?.checked === true;
        document.querySelectorAll('[data-commercial-vat]').forEach(element => { element.hidden = !hasVat; });
        const includeVat = document.getElementById('includeVatInInventoryCost');
        if (includeVat) {
            if (!hasVat) includeVat.checked = false;
            includeVat.disabled = readOnly || !hasVat;
        }
        document.querySelectorAll('.commercial-tax').forEach(function (select) {
            if (!hasVat) {
                if (select.value) select.dataset.previousTaxId = select.value;
                select.value = '';
            } else if (!select.value && select.dataset.previousTaxId) {
                select.value = select.dataset.previousTaxId;
            }
            select.disabled = readOnly || !hasVat;
        });
    }

    function syncSettlementState() {
        const supplierId = nullablePositiveInt(document.getElementById('commercialSupplierId')?.value);
        const required = document.querySelector('.sd-supplier-required');
        const payee = document.getElementById('commercialPayeeName');
        if (required) required.classList.remove('d-none');
        if (payee) payee.placeholder = supplierId
            ? 'Thông tin thanh toán/hiển thị nếu cần'
            : 'Chọn nhà cung cấp trước; trường này không thay thế NCC';
    }

    function syncFreightState(resetWhenDisabled) {
        const readOnly = document.getElementById('commercialApprovalWorkbench')?.dataset.readonly === 'true';
        const enabled = document.getElementById('commercialHasFreight')?.checked === true;
        const capitalization = document.getElementById('capitalizeFreightInInventoryCost');
        if (capitalization) {
            if (!enabled) capitalization.checked = false;
            capitalization.disabled = readOnly || !enabled;
        }
        const capitalized = enabled && capitalization?.checked === true;
        const ids = ['commercialFreightTotal', 'commercialFreightPayee', 'commercialFreightPaid', 'commercialFreightNote', 'commercialAutoAllocate'];
        ids.forEach(function (id) {
            const element = document.getElementById(id);
            if (element) element.disabled = readOnly || !enabled || (id === 'commercialAutoAllocate' && !capitalized);
        });
        document.querySelectorAll('.commercial-freight-allocation').forEach(function (input) {
            input.disabled = readOnly || !capitalized;
            if (!capitalized && resetWhenDisabled) input.value = '0';
        });
        document.querySelectorAll('.commercial-line-freight').forEach(function (panel) {
            panel.classList.toggle('d-none', !capitalized);
        });
        if (!enabled && resetWhenDisabled) {
            const total = document.getElementById('commercialFreightTotal');
            if (total) total.value = '0';
        }
    }

    function recalculateCommercialTotals() {
        const hasVat = document.getElementById('commercialHasVat')?.checked === true;
        let subtotal = 0;
        let vatTotal = 0;
        let merchandiseTotal = 0;

        getCommercialRows().forEach(function (row) {
            const quantity = Number(row.dataset.quantity || 0);
            const unitPrice = roundMoney(readNumber(row.querySelector('.commercial-unit-price')));
            const taxSelect = row.querySelector('.commercial-tax');
            const selected = taxSelect?.options[taxSelect.selectedIndex];
            const taxRate = hasVat ? Number(selected?.dataset.rate || 0) : 0;
            const hasSavedAmount = unitPrice > 0 && row.dataset.exactUnitPrice !== '' && Number(row.dataset.exactUnitPrice) === unitPrice;
            const before = hasSavedAmount ? Number(row.dataset.exactBeforeVat || 0) : roundMoney(quantity * unitPrice);
            const vat = hasVat ? roundMoney(before * taxRate / 100) : 0;
            const after = roundMoney(before + vat);

            row.dataset.lineAfterVat = String(after);
            subtotal = roundMoney(subtotal + before);
            vatTotal = roundMoney(vatTotal + vat);
            merchandiseTotal = roundMoney(merchandiseTotal + after);
            setText(row.querySelector('.commercial-line-vat'), formatMoney(vat));
            setText(row.querySelector('.commercial-line-after-vat'), formatMoney(after));
            renderPriceVariance(row, unitPrice);
        });

        const hasFreight = document.getElementById('commercialHasFreight')?.checked === true;
        const includeVatInInventoryCost = hasVat && document.getElementById('includeVatInInventoryCost')?.checked === true;
        const capitalizeFreightInInventoryCost = hasFreight && document.getElementById('capitalizeFreightInInventoryCost')?.checked === true;
        const freight = hasFreight ? roundMoney(readNumber(document.getElementById('commercialFreightTotal'))) : 0;
        setText(document.getElementById('commercialSubtotal'), formatMoney(subtotal));
        setText(document.getElementById('commercialVatTotal'), formatMoney(vatTotal));
        setText(document.getElementById('commercialMerchandiseTotal'), formatMoney(merchandiseTotal));
        setText(document.getElementById('commercialFreightSummary'), formatMoney(freight));
        const inventoryValue = roundMoney(subtotal + (includeVatInInventoryCost ? vatTotal : 0) + (capitalizeFreightInInventoryCost ? freight : 0));
        setText(document.getElementById('commercialLandedTotal'), formatMoney(inventoryValue));
        setText(document.getElementById('commercialApprovalModalMerchandiseTotal'), formatMoney(merchandiseTotal));
        const headerTotal = document.getElementById('txtTotalAmount');
        if (headerTotal) {
            const formatted = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 })
                .format(merchandiseTotal);
            if ('value' in headerTotal) headerTotal.value = formatted;
            else headerTotal.textContent = `${formatted} đ`;
        }
        renderAllocationStatus(freight, hasFreight, capitalizeFreightInInventoryCost);
        clearCommercialError();
        document.dispatchEvent(new Event('receipt-pricing-inputs-changed'));
    }

    function renderPriceVariance(row, currentPrice) {
        const varianceElement = row.querySelector('.commercial-price-variance');
        const previous = readLastPurchasePrice(row);
        varianceElement.classList.remove('is-up', 'is-down');
        if (previous === null || !(currentPrice > 0)) {
            varianceElement.textContent = '';
            return;
        }

        if (previous === 0) {
            varianceElement.textContent = `0 → ${formatMoney(currentPrice)}`;
            varianceElement.classList.add('is-up');
            return;
        }

        const percentage = ((currentPrice - previous) / previous) * 100;
        const sign = percentage > 0 ? '+' : '';
        varianceElement.textContent = `${sign}${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 }).format(percentage)}%`;
        if (percentage > 0) varianceElement.classList.add('is-up');
        if (percentage < 0) varianceElement.classList.add('is-down');
    }

    function getPriceVariances() {
        return getCommercialRows().map(function (row) {
            const previous = readLastPurchasePrice(row);
            const current = roundMoney(readNumber(row.querySelector('.commercial-unit-price')));
            if (previous === null || !(current > 0) || roundMoney(current - previous) === 0) return null;
            return {
                lineNo: Number(row.dataset.lineNo || 0),
                previous: roundMoney(previous),
                current: current
            };
        }).filter(Boolean);
    }

    function renderPriceVarianceAcceptance() {
        const panel = document.getElementById('priceVarianceAcceptancePanel');
        const details = document.getElementById('priceVarianceAcceptanceDetails');
        const checkbox = document.getElementById('acceptPriceVariance');
        if (!panel || !details || !checkbox) return;

        const variances = getPriceVariances();
        checkbox.checked = false;
        panel.classList.toggle('d-none', variances.length === 0);
        details.replaceChildren();
        variances.forEach(function (variance) {
            const line = document.createElement('div');
            line.textContent = `Dòng ${variance.lineNo}: ${formatMoney(variance.previous)} → ${formatMoney(variance.current)}`;
            details.appendChild(line);
        });
    }

    function autoAllocateFreight() {
        const rows = getCommercialRows();
        const hasFreight = document.getElementById('commercialHasFreight')?.checked === true;
        const capitalized = hasFreight && document.getElementById('capitalizeFreightInInventoryCost')?.checked === true;
        const total = capitalized ? roundMoney(readNumber(document.getElementById('commercialFreightTotal'))) : 0;
        const weightTotal = roundMoney(rows.reduce(function (sum, row) {
            return sum + Number(row.dataset.lineAfterVat || 0);
        }, 0));

        let allocated = 0;
        rows.forEach(function (row, index) {
            let amount = 0;
            if (total > 0 && weightTotal > 0) {
                amount = index === rows.length - 1
                    ? roundMoney(total - allocated)
                    : roundMoney(total * Number(row.dataset.lineAfterVat || 0) / weightTotal);
            }
            row.querySelector('.commercial-freight-allocation').value = decimalInput(amount);
            allocated = roundMoney(allocated + amount);
        });
    }

    function renderAllocationStatus(freightTotal, hasFreight, capitalized) {
        const status = document.getElementById('commercialAllocationStatus');
        if (!status) return;
        if (!hasFreight) {
            status.innerHTML = '<span class="sd-allocation-pill">Không có phí cần phân bổ</span>';
            return;
        }
        if (!capitalized) {
            status.innerHTML = '<span class="sd-allocation-pill">Phí được ghi nhận riêng, không vào giá vốn</span>';
            return;
        }

        const allocated = roundMoney(getCommercialRows().reduce(function (sum, row) {
            return sum + readNumber(row.querySelector('.commercial-freight-allocation'));
        }, 0));
        const difference = roundMoney(freightTotal - allocated);
        const balanced = difference === 0;
        const differenceLabel = balanced
            ? 'Đã phân bổ đủ'
            : difference > 0
                ? `Còn thiếu ${formatMoney(difference)}`
                : `Đang vượt ${formatMoney(Math.abs(difference))}`;
        status.innerHTML =
            `<span class="sd-allocation-pill">Đã phân bổ <strong>${formatMoney(allocated)}</strong></span>` +
            `<span class="sd-allocation-pill ${balanced ? 'is-balanced' : 'is-unbalanced'}">${differenceLabel}</span>`;
    }

    function updateApproveModalSummary() {
        const modal = document.getElementById('approveModal');
        if (!modal) return;
        const summary = modal.querySelector('.alert-success');
        if (summary) {
            summary.textContent = 'Tồn kho, FIFO/cost và công nợ sẽ được ghi một lần.';
        }
    }

    function getCommercialRows() {
        return Array.from(document.querySelectorAll('#commercialApprovalWorkbench .commercial-line'));
    }

    async function savePhysicalQuantity(input) {
        const row = input.closest('.commercial-line');
        const lineId = Number(row?.dataset.lineId || 0);
        const documentId = Number(window.stockDocumentPage?.documentId || 0);
        const quantity = readNumber(input);
        const original = Number(input.dataset.original || row?.dataset.quantity || 0);
        const unitId = nullablePositiveInt(input.dataset.unitId);
        if (!lineId || !unitId || !(quantity > 0)) {
            input.value = decimalInput(original);
            fail('Số lượng thực nhận phải lớn hơn 0.', input, true);
            return;
        }
        if (roundQuantity(quantity) === roundQuantity(original)) return;

        let releaseDrafts;
        input.dataset.saving = 'true';
        input.disabled = true;
        try {
            releaseDrafts = await window.GaoReceiptPriceDrafts?.hold();
            const response = await fetch(`/admin/api/stock-documents/${documentId}/lines/${lineId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    unitId: unitId,
                    quantity: roundQuantity(quantity),
                    unitCost: null,
                    taxId: null,
                    note: input.dataset.note || null
                })
            });
            const api = await readJsonResponse(response);
            if (!api.ok) {
                input.value = decimalInput(original);
                fail(api.data?.message || 'Không thể cập nhật số lượng thực nhận.', input, true);
                return;
            }

            window.stockDocumentRowVersion?.update(api.data?.rowVersion);
            window.GaoReceiptPriceDrafts?.updateLineVersion(lineId, api.data?.lineRowVersion);
            const normalized = roundQuantity(quantity);
            input.dataset.original = String(normalized);
            row.dataset.quantity = String(normalized);
            recalculateCommercialTotals();
            if (!manualFreightAllocation) {
                autoAllocateFreight();
                recalculateCommercialTotals();
            }
        } catch (error) {
            console.error(error);
            input.value = decimalInput(original);
            fail('Không kết nối được máy chủ; số lượng chưa được lưu.', input, true);
        } finally {
            releaseDrafts?.();
            input.dataset.saving = 'false';
            input.disabled = false;
        }
    }

    function readNumber(element) {
        if (!element) return 0;
        const value = Number(String(element.value ?? '').replace(',', '.'));
        return Number.isFinite(value) ? value : 0;
    }

    function readLastPurchasePrice(row) {
        const raw = row.querySelector('.commercial-last-price')?.dataset.value;
        if (raw === undefined || raw === null || raw.trim() === '') return null;
        const value = Number(raw);
        return Number.isFinite(value) && value >= 0 ? roundMoney(value) : null;
    }

    function nullablePositiveInt(value) {
        const parsed = Number(value || 0);
        return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
    }

    function valueOrNull(id) {
        const value = document.getElementById(id)?.value?.trim() || '';
        return value || null;
    }

    function roundMoney(value) {
        return Math.round((Number(value) + Number.EPSILON) * 100) / 100;
    }

    function roundQuantity(value) {
        return Math.round((Number(value) + Number.EPSILON) * 1000) / 1000;
    }

    function decimalInput(value) {
        return roundMoney(value).toFixed(2).replace(/\.00$/, '');
    }

    function formatMoney(value) {
        return new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(Number(value) || 0) + ' đ';
    }

    function setText(element, text) {
        if (element) element.textContent = text;
    }

    async function readJsonResponse(response) {
        const text = await response.text();
        try {
            return { ok: response.ok, data: JSON.parse(text) };
        } catch {
            return { ok: response.ok, data: { message: text } };
        }
    }

    function hideApproveModal() {
        const element = document.getElementById('approveModal');
        if (!element || !window.bootstrap) return;
        bootstrap.Modal.getInstance(element)?.hide();
    }
})();

(() => {
    'use strict';
    let host, workspace, draft, preview, dirty = false, busy = false, writing = false, pricingRevision = 0;
    let step = 'bill', selectedProduct, editingBillKey, editingProgramKey, programDraft, timer;
    let receiptId;
    let billPriceError = '';
    let activeSuggestion = -1;
    let programSearches = [];
    let localDraftKey, restoringDraft = true, localDraftRemembered = false, pendingLocalDraft;
    let xmlInvoices = [], xmlChoices = new Map(), xmlLoading = false, xmlLoaded = false, xmlGeneration = 0;
    const previousPhysical = new Map();
    const byId = id => document.getElementById(id);
    const money = value => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(value || 0) + ' đ';
    const unitMoney = value => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 12 }).format(value || 0) + ' đ';
    const quantity = value => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 9 }).format(value || 0);
    const numeric = id => Number(byId(id)?.value || 0);
    const enumIs = (value, number, name) => value === number || value === name;
    const giftRule = rule => enumIs(rule.type, 1, 'Gift');
    const applied = () => enumIs(workspace?.state, 2, 'Applied');
    const editable = () => workspace?.canEdit === true;
    const physical = id => workspace.physicalLines.find(x => x.stockDocumentLineId === Number(id)) || previousPhysical.get(Number(id));
    const products = () => [...new Map(workspace.physicalLines.map(x => [x.productVariantId, x])).values()];
    const product = id => products().find(x => x.productVariantId === Number(id)) || [...previousPhysical.values()].find(x => x.productVariantId === Number(id));
    const unit = (id, unitId) => product(id)?.units.find(x => x.unitId === Number(unitId)) || [...previousPhysical.values()].find(x => x.productVariantId === Number(id))?.units.find(x => x.unitId === Number(unitId));
    const bill = key => draft.billLines.find(x => x.billLineKey === key);
    const groupKey = rule => rule.programKey || rule.ruleKey;
    const editorPending = () => host?.querySelector('#pricingRuleEditor')?.hidden === false;
    const invalidBillRows = () => draft.billLines.filter(l => !workspace.physicalLines.some(p => p.productVariantId === l.productVariantId && p.units.some(u => u.unitId === l.billUnitId)));
    const entryPending = () => !!editingBillKey || !!selectedProduct || !!host?.querySelector('#pricingBillSearch')?.value.trim() || !!host?.querySelector('#pricingBillPrice')?.value || !!host?.querySelector('#pricingBillAmount')?.value;
    const normalize = text => String(text).toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd');
    const create = (tag, text, className) => { const el = document.createElement(tag); if (text !== undefined) el.textContent = text; if (className) el.className = className; return el; };
    // Keep decimal division exact until the existing server decimal pricing calculation.
    // JSON decimal strings preserve all twelve supported unit-price decimal places.
    function decimalParts(value) {
        const match = String(value).trim().match(/^(\d+)(?:\.(\d*))?(?:e([+-]?\d+))?$/i);
        if (!match || match[1].length + (match[2]?.length || 0) > 40) return null;
        let scale = (match[2]?.length || 0) - Number(match[3] || 0);
        if (!Number.isInteger(scale) || Math.abs(scale) > 30) return null;
        let n = BigInt(match[1] + (match[2] || ''));
        if (scale < 0) { n *= 10n ** BigInt(-scale); scale = 0; }
        while (scale > 0 && n % 10n === 0n) { n /= 10n; scale--; }
        return { n, scale };
    }
    function decimalText(n, scale) {
        const digits = n.toString().padStart(scale + 1, '0');
        if (!scale) return digits;
        const fraction = digits.slice(-scale).replace(/0+$/, '');
        return digits.slice(0, -scale) + (fraction ? '.' + fraction : '');
    }
    function roundedRatio(n, d, scale) {
        return decimalText(n / d + (2n * (n % d) >= d ? 1n : 0n), scale);
    }
    function lineAmount(qty, price) {
        const q = decimalParts(qty), p = decimalParts(price);
        return q && p ? roundedRatio(q.n * p.n * 100n, 10n ** BigInt(q.scale + p.scale), 2) : '';
    }
    function thumbnail(p) {
        const frame = create('span', undefined, 'sd-bill-product-image');
        const fallback = () => { frame.replaceChildren(create('i', undefined, 'bx bx-image')); frame.title = 'Chưa có ảnh sản phẩm'; frame.setAttribute('aria-label', frame.title); frame.setAttribute('role', 'img'); };
        fallback();
        if (p?.productImageUrl) {
            const img = create('img'); img.alt = ''; img.loading = 'lazy'; img.src = p.productImageUrl;
            img.addEventListener('error', fallback, { once: true });
            frame.replaceChildren(img); frame.title = p.productName; frame.removeAttribute('aria-label'); frame.removeAttribute('role');
        }
        return frame;
    }
    function renderSelectedProduct() {
        const box = byId('pricingSelectedProduct'); box.replaceChildren(); box.hidden = !selectedProduct;
        if (selectedProduct) box.append(thumbnail(selectedProduct), create('strong', selectedProduct.productName));
    }
    const message = (value, error = false) => { const box = byId('pricingAllocationMessage'); box.textContent = value; box.classList.toggle('text-danger', error); };
    const url = () => `/admin/api/stock-documents/${window.stockDocumentPage.documentId}/pricing-allocation`;
    function button(text, callback, className = 'btn btn-sm btn-link') {
        const el = create('button', text, className); el.type = 'button'; el.disabled = !editable() || writing;
        el.addEventListener('click', callback); return el;
    }
    function field(label, input) { const el = create('label', undefined, 'sd-pricing-field'); el.append(create('span', label), input); return el; }
    function number(value, callback, max) {
        const el = create('input', undefined, 'form-control'); el.type = 'number'; el.min = '0.000000001'; el.step = 'any'; el.value = value ?? '';
        if (max !== undefined) el.max = String(max); el.addEventListener('input', () => callback(Number(el.value))); return el;
    }
    function fillUnits(select, target, value) {
        select.replaceChildren(); for (const u of target?.units || []) select.add(new Option(`${u.name} ×${u.factor}`, u.unitId));
        if (value) select.value = value;
    }
    function localDraftStatus(text, error = false) {
        const label = byId('pricingLocalDraftStatus'); if (!label) return;
        label.textContent = text; label.className = 'small ' + (error ? 'text-danger' : 'text-success');
    }
    function captureDraft(sourceHost = host) {
        const input = id => sourceHost.querySelector('#' + id)?.value || '';
        const metadata = new Map(workspace.physicalLines.map(p => [p.stockDocumentLineId, p]));
        for (const rule of draft.rules.filter(giftRule)) { const p = physical(rule.giftLineId); if (p) metadata.set(p.stockDocumentLineId, p); }
        for (const variantId of [...draft.billLines.map(l => l.productVariantId), selectedProduct?.productVariantId]) { const p = product(variantId); if (p) metadata.set(p.stockDocumentLineId, p); }
        return { draft: structuredClone(draft), dirty, step, physicalLines: structuredClone([...metadata.values()]),
            basePlanId: workspace.planId, basePlanVersion: workspace.planRowVersion, baseState: workspace.state,
            entry: { key: editingBillKey, productVariantId: selectedProduct?.productVariantId,
                fields: Object.fromEntries(['pricingBillSearch', 'pricingBillKind', 'pricingBillUnit', 'pricingBillQuantity', 'pricingBillPriceMode', 'pricingBillPrice', 'pricingBillAmount'].map(id => [id, input(id)])) },
            editor: sourceHost.querySelector('#pricingRuleEditor')?.hidden === false ? { key: editingProgramKey, value: structuredClone(programDraft),
                type: Number(input('pricingRuleType')), name: input('pricingRuleName'), percent: Number(input('pricingRuleDiscount')), amount: input('pricingRuleDiscountAmount') } : null };
    }
    function clearLocalDraft() {
        try { if (localDraftKey) localStorage.removeItem(localDraftKey); }
        catch { localDraftStatus('Chưa xóa được bản nháp trên trình duyệt này.', true); return; }
        pendingLocalDraft = null; localDraftRemembered = false;
        byId('pricingRestoreLocalDraft').hidden = true;
        localDraftStatus('Bill và chương trình tự nhớ trên trình duyệt này khi đang nhập.');
    }
    function rememberDraft() {
        if (restoringDraft || !workspace || !editable() || pendingLocalDraft) return;
        // A clean tab must not delete input remembered by another tab of the receipt.
        if (!dirty && !editorPending() && !entryPending()) { if (localDraftRemembered) clearLocalDraft(); return; }
        try {
            if (!localDraftKey) throw new Error('Missing draft scope');
            const saved = { version: 1, scope: localDraftKey, savedAt: new Date().toISOString(), state: captureDraft() };
            localStorage.setItem(localDraftKey, JSON.stringify(saved)); localDraftRemembered = true;
            localDraftStatus(`Đã nhớ bản nháp trên trình duyệt này · ${new Date(saved.savedAt).toLocaleTimeString('vi-VN')}`);
        } catch {
            localDraftRemembered = false;
            localDraftStatus('Chưa nhớ được bản nháp. Giữ trang này mở để tránh mất phần đang nhập.', true);
        }
    }
    function readLocalDraft() {
        if (!localDraftKey) return null;
        try {
            const text = localStorage.getItem(localDraftKey); if (!text) return null;
            const saved = JSON.parse(text), state = saved.state;
            if (saved.version !== 1 || saved.scope !== localDraftKey || !state?.draft ||
                !Array.isArray(state.draft.billLines) || state.draft.billLines.length > 500 || !Array.isArray(state.draft.rules) || state.draft.rules.length > 100 ||
                !Array.isArray(state.draft.giftValuations) || !Array.isArray(state.physicalLines) || state.physicalLines.length > 1500 || !['bill', 'program', 'price'].includes(state.step) ||
                state.physicalLines.some(p => !p || !Array.isArray(p.units)) ||
                state.draft.billLines.some(l => !l || typeof l.billLineKey !== 'string' || !Number.isInteger(l.productVariantId) || !Number.isInteger(l.billUnitId) || !Number.isFinite(l.billQuantity) || l.billQuantity <= 0 || !decimalParts(l.billUnitPriceBeforeVat)) ||
                state.draft.rules.some(r => !r || typeof r.ruleKey !== 'string' || !Array.isArray(r.billSources) || r.billSources.some(s => !s || typeof s.billLineKey !== 'string' || !state.draft.billLines.some(l => l.billLineKey === s.billLineKey))) ||
                (state.editor && (!state.editor.value || !state.editor.value.sources || !Array.isArray(state.editor.value.gifts)))) throw new Error('Invalid draft');
            return state;
        } catch { localDraftStatus('Không đọc được bản nháp trên trình duyệt này. Dữ liệu kế hoạch trên máy chủ vẫn được giữ.', true); return null; }
    }
    function restoreEntry(saved) {
        if (!saved?.fields) return;
        selectedProduct = products().find(p => p.productVariantId === saved.productVariantId) || null;
        editingBillKey = bill(saved.key) ? saved.key : null;
        fillUnits(byId('pricingBillUnit'), selectedProduct, saved.fields.pricingBillUnit);
        for (const id of ['pricingBillSearch', 'pricingBillKind', 'pricingBillUnit', 'pricingBillQuantity', 'pricingBillPriceMode', 'pricingBillPrice', 'pricingBillAmount']) if (typeof saved.fields[id] === 'string') byId(id).value = saved.fields[id];
        byId('pricingBillAdd').textContent = editingBillKey ? 'Lưu dòng bill' : 'Thêm dòng bill';
        renderSelectedProduct(); updateConversion(); closeSuggestions(); updateActions();
    }
    function restoreDraft(saved) {
        for (const p of saved.physicalLines || []) previousPhysical.set(p.stockDocumentLineId, p);
        draft.billLines = structuredClone(saved.draft.billLines); draft.rules = structuredClone(saved.draft.rules);
        draft.giftValuations = structuredClone(saved.draft.giftValuations);
        draft.actualBillTotal = saved.draft.actualBillTotal; draft.globalDiscountPercent = saved.draft.globalDiscountPercent;
        for (const rule of draft.rules.filter(giftRule)) {
            const previous = previousPhysical.get(rule.giftLineId);
            const target = workspace.physicalLines.find(p => p.productVariantId === previous?.productVariantId);
            if (target) rule.giftLineId = target.stockDocumentLineId;
        }
        // Keep the current server's tokens and receiving rows. A local draft is input,
        // never proof that costs can be applied or that the receipt can be confirmed.
        dirty = saved.dirty || workspace.isStale; pricingRevision++; preview = null; step = saved.editor ? 'program' : saved.step;
        render(); restoreEditor(saved.editor); restoreEntry(saved.entry);
        message('Đã khôi phục phần đang nhập. Tiếp tục nhập bill và chương trình; tính lại, lưu kế hoạch trước khi áp dụng giá.');
    }
    function markDirty() {
        dirty = true; pricingRevision++; preview = null; clearTimeout(timer);
        renderPreview(); renderComparison(); updateActions(); rememberDraft(); message('Kế hoạch có thay đổi chưa lưu. Đối chiếu và lưu trước khi áp dụng giá.');
    }
    function updateActions() {
        if (!workspace) return;
        for (const id of ['pricingPreview', 'pricingSave', 'pricingNewProgram', 'pricingBillAdd']) byId(id).disabled = busy || !editable();
        byId('pricingApply').disabled = busy || !editable() || dirty || editorPending() || entryPending() || !workspace.planId || workspace.isStale || !preview?.canApply;
        byId('pricingReload').disabled = busy;
        for (const el of host.querySelectorAll('input,select')) el.disabled = !editable() || writing;
        byId('pricingBillUnit').disabled = !editable() || writing || !selectedProduct;
        byId('pricingBillPrice').disabled = !editable() || writing || byId('pricingBillKind').value === 'gift';
        byId('pricingBillAmount').disabled = byId('pricingBillPrice').disabled;
        byId('pricingBillPriceMode').disabled = byId('pricingBillPrice').disabled;
        byId('pricingBillPrice').readOnly = byId('pricingBillPriceMode').value === 'amount';
        byId('pricingBillAmount').readOnly = byId('pricingBillPriceMode').value !== 'amount';
        byId('pricingBillPrice').tabIndex = byId('pricingBillPrice').readOnly ? -1 : 0;
        byId('pricingBillAmount').tabIndex = byId('pricingBillAmount').readOnly ? -1 : 0;
        for (const el of host.querySelectorAll('[data-source-quantity]')) el.disabled = !editable() || writing || !Object.hasOwn(programDraft?.sources || {}, el.dataset.sourceQuantity);
        for (const el of host.querySelectorAll('[data-gift-unit-locked]')) el.disabled = true;
        byId('pricingSave').disabled ||= editorPending() || entryPending();
        byId('pricingBillCancelEdit').hidden = !editingBillKey && !entryPending();
        byId('pricingBillCancelEdit').textContent = editingBillKey ? 'Hủy sửa' : 'Hủy dòng đang nhập';
        for (const el of host.querySelectorAll('[data-bill-action]')) el.disabled = !editable() || writing;
        updateXmlActions();
    }
    async function api(method, suffix = '', data) {
        const requestHost = host;
        const response = await fetch(url() + suffix, { method, cache: 'no-store', headers: {
            'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
        }, body: data === undefined ? undefined : JSON.stringify(data) });
        const result = await response.json().catch(() => ({}));
        if (requestHost !== host) throw new Error('Phần duyệt phiếu đã tải lại.');
        if (!response.ok) { if (response.status === 409 && workspace) workspace.isStale = true; throw new Error(result.message || 'Không tải/lưu được kế hoạch giá. Vui lòng kiểm tra và thử lại.'); }
        return result;
    }
    async function action(run, holdPrices = false) {
        if (busy) return; const actionHost = host; busy = true; writing = holdPrices; updateActions(); let release;
        try { if (holdPrices) release = await window.GaoReceiptPriceDrafts?.hold(); if (actionHost === host) await run(); }
        catch (error) { if (actionHost === host) message(error.message, true); }
        finally { release?.(); if (actionHost === host) { busy = false; writing = false; updateActions(); } }
    }
    function importLegacy() {
        if (Array.isArray(draft.billLines)) return;
        draft.billLines = [];
        for (const line of draft.lines) if (line.billQuantity > 0) draft.billLines.push({ billLineKey: `legacy-${line.stockDocumentLineId}`, lineNo: draft.billLines.length + 1,
            productVariantId: physical(line.stockDocumentLineId).productVariantId, billUnitId: line.billUnitId, billQuantity: line.billQuantity, billUnitPriceBeforeVat: line.billUnitPriceBeforeVat, isGift: false });
        for (const rule of draft.rules) {
            rule.programKey ||= rule.ruleKey;
            rule.billSources = rule.sourceLineIds.map(id => ({ billLineKey: `legacy-${id}`, quantity: bill(`legacy-${id}`)?.billQuantity || 0 }));
            if (giftRule(rule)) {
                const key = crypto.randomUUID(); rule.giftBillLineKey = key;
                draft.billLines.push({ billLineKey: key, lineNo: draft.billLines.length + 1, productVariantId: physical(rule.giftLineId).productVariantId,
                    billUnitId: rule.giftUnitId, billQuantity: rule.giftQuantity, billUnitPriceBeforeVat: 0, isGift: true });
            }
        }
    }
    function accept(value, clearRemembered = false) {
        const wasRestoring = restoringDraft; restoringDraft = true;
        workspace = value; draft = structuredClone(value.draft); preview = value.preview; dirty = false; pricingRevision++;
        draft.receiptRowVersion = value.receiptRowVersion; draft.planRowVersion = value.planRowVersion;
        draft.lines = value.physicalLines.map(line => {
            const input = draft.lines.find(x => x.stockDocumentLineId === line.stockDocumentLineId) || {
                stockDocumentLineId: line.stockDocumentLineId, billUnitId: line.unitId, billQuantity: line.quantity, billUnitPriceBeforeVat: line.currentUnitPriceBeforeVat };
            input.rowVersion = line.rowVersion; return input;
        });
        importLegacy(); resetEntry(); byId('pricingRuleEditor').hidden = true; render();
        window.stockDocumentRowVersion?.update(value.receiptRowVersion); window.stockDocumentPage.rowVersion = value.receiptRowVersion;
        const workbench = byId('commercialApprovalWorkbench'); workbench.dataset.allocationApplied = applied() || enumIs(workspace.state, 3, 'Confirmed') ? 'true' : 'false';
        for (const line of workspace.physicalLines) {
            const row = workbench.querySelector(`.commercial-line[data-line-id="${line.stockDocumentLineId}"]`);
            const input = row?.querySelector('.commercial-unit-price'); if (!input) continue;
            input.readOnly = !editable();
            row.dataset.exactUnitPrice = String(line.currentUnitPriceBeforeVat);
            row.dataset.exactBeforeVat = String(line.currentAmountBeforeVat);
            if (line.currentUnitPriceBeforeVat > 0)
                window.GaoReceiptPriceDrafts?.acceptSavedPrice(line.stockDocumentLineId, line.currentUnitPriceBeforeVat, line.rowVersion);
        }
        byId('commercialHasVat')?.dispatchEvent(new Event('change'));
        restoringDraft = wasRestoring;
        if (clearRemembered) clearLocalDraft();
    }
    function showStep(next, calculate = true) {
        if (!byId('pricingRuleEditor').hidden && next !== 'program') { message('Gắn hoặc hủy chương trình đang nhập trước khi chuyển bước.', true); return; }
        step = next;
        for (const page of host.querySelectorAll('[data-pricing-page]')) page.hidden = page.dataset.pricingPage !== step;
        for (const el of host.querySelectorAll('.sd-pricing-steps button')) el.setAttribute('aria-pressed', String(el.dataset.pricingStep === step));
        byId('pricingFinalActions').hidden = step !== 'price'; closeSuggestions();
        if (step === 'price' && calculate && editable() && !preview) calculatePreview();
        rememberDraft();
    }
    function render() {
        byId('pricingActualBillTotal').value = draft.actualBillTotal || ''; byId('pricingGlobalDiscount').value = draft.globalDiscountPercent || 0;
        renderBill(); renderRules(); renderComparison(); renderPreview(); showStep(step, false); updateActions();
        if (!editable()) message('Phiếu đã ghi sổ. Kế hoạch giá và khuyến mãi là bằng chứng chỉ đọc.');
        else if (workspace.isStale) message('Giá Hàng hóa hoặc dữ liệu thực nhận đã thay đổi. Tính lại và lưu kế hoạch trước khi áp dụng lại. Khi duyệt, hệ thống dùng giá tại Hàng hóa.', true);
        else if (applied()) message('Đã điền giá nhập sang Hàng hóa. Bạn vẫn có thể sửa giá thủ công tại đó trước khi Duyệt và ghi sổ.');
        else message(workspace.planId ? 'Kế hoạch nháp chưa áp dụng giá.' : 'Nhìn bill và nhập từng dòng. Chỉ tìm sản phẩm trên phiếu nhập này.');
    }
    function renderBill() {
        const list = byId('pricingAllocationLines'); list.replaceChildren(); byId('pricingBillCount').textContent = `${draft.billLines.length} dòng`;
        if (!draft.billLines.length) list.append(create('div', 'Chưa nhập dòng bill nào.', 'sd-pricing-empty'));
        for (const [index, line] of draft.billLines.entries()) {
            line.lineNo = index + 1;
            const p = product(line.productVariantId), u = unit(line.productVariantId, line.billUnitId);
            const row = create('div', undefined, 'sd-bill-row'); row.dataset.billLineKey = line.billLineKey;
            const name = create('div'), identity = create('div', undefined, 'sd-bill-row-product'); name.append(create('div', p.productName, 'fw-semibold'));
            if (line.isGift) name.append(create('span', 'Hàng tặng', 'badge bg-label-success'));
            name.append(create('small', `${quantity(line.billQuantity)} ${u?.name || ''} × ${unitMoney(line.billUnitPriceBeforeVat)} · Quy đổi ${quantity(line.billQuantity * (u?.factor || 0))} ${baseUnitName(p)}`, 'd-block text-muted'));
            identity.append(thumbnail(p), name);
            const actions = create('div', undefined, 'sd-bill-row-actions');
            for (const [text, run] of [['Sửa', () => editBill(line)], ['Xóa', () => removeBill(line)]]) {
                const el = button(text, run); el.dataset.billAction = text; el.setAttribute('aria-label', `${text} dòng bill ${line.lineNo}`); actions.append(el);
            }
            row.append(create('span', `${line.lineNo}.`, 'text-muted'), identity, create('strong', money(lineAmount(line.billQuantity, line.billUnitPriceBeforeVat)), 'sd-bill-row-amount'), actions); list.append(row);
        }
        let cents = 0n;
        for (const line of draft.billLines) {
            const amount = decimalParts(lineAmount(line.billQuantity, line.billUnitPriceBeforeVat));
            if (amount) cents += amount.n * 10n ** BigInt(2 - amount.scale);
        }
        byId('pricingBillTotal').textContent = draft.billLines.length ? money(decimalText(cents, 2)) : '—';
        renderXmlSourceWarning();
    }
    const xmlKey = (invoice, detail) => `xml-${invoice.id}-${detail.id}`;
    const xmlRows = () => xmlInvoices.flatMap(invoice => invoice.details.map(detail => ({ invoice, detail })));
    function xmlChoice(invoice, detail) {
        const key = xmlKey(invoice, detail);
        if (!xmlChoices.has(key)) xmlChoices.set(key, { touched: false });
        const choice = xmlChoices.get(key);
        if (!choice.touched) {
            const mapping = detail.itemCatalogMapping, p = products().find(x => x.productVariantId === mapping?.productVariantId);
            const confirmedUnit = p?.units.find(u => u.conversionId === mapping?.productUnitConversionId && u.unitId === mapping?.confirmedUnitId);
            const namedUnits = p?.units.filter(u => normalize(u.name).trim() === normalize(detail.unitName || '').trim()) || [];
            Object.assign(choice, { productVariantId: p?.productVariantId || null,
                unitId: confirmedUnit?.unitId || (namedUnits.length === 1 ? namedUnits[0].unitId : null),
                kind: Number(detail.lineAmount) > 0 ? 'buy' : '', touched: false });
        }
        return choice;
    }
    function xmlMappingMatches(detail, choice) {
        const mapping = detail.itemCatalogMapping, p = products().find(x => x.productVariantId === choice.productVariantId);
        const u = p?.units.find(x => x.unitId === choice.unitId);
        return !!p && !!u && mapping?.isAutoApplicable === true && mapping.productVariantId === p.productVariantId &&
            mapping.productUnitConversionId === u.conversionId && mapping.confirmedUnitId === u.unitId;
    }
    function xmlAmount(detail, kind) {
        const q = decimalParts(detail.quantity), amount = decimalParts(detail.lineAmount);
        if (!q || q.n <= 0n || q.scale > 9) throw new Error('Dòng này không có số lượng dương hợp lệ để thêm vào bill. Kiểm tra nội dung XML và nhập thủ công nếu cần.');
        if (!amount || amount.scale > 2) throw new Error('Thành tiền XML không hợp lệ. Dòng điều chỉnh hoặc giảm tiền riêng cần được kiểm tra ở bước Chương trình.');
        if (!kind) throw new Error('Chọn Hàng mua hoặc Hàng tặng sau khi kiểm tra nội dung XML. Giá bằng 0 chưa đủ để xác định hàng tặng.');
        if (kind === 'gift') {
            if (amount.n !== 0n) throw new Error('Hàng tặng phải có thành tiền bằng 0. Kiểm tra loại dòng XML trước khi thêm.');
            return { price: '0', amount: '0' };
        }
        if (amount.n <= 0n) throw new Error('Hàng mua phải có thành tiền lớn hơn 0. Kiểm tra hàng tặng hoặc dòng đặc biệt trên XML.');
        const price = roundedRatio(amount.n * 10n ** BigInt(q.scale + 12), q.n * 10n ** BigInt(amount.scale), 12);
        if (lineAmount(detail.quantity, price) !== decimalText(amount.n, amount.scale))
            throw new Error('Chưa quy đổi được đơn giá khớp thành tiền XML. Kiểm tra và nhập dòng bill thủ công.');
        return { price, amount: decimalText(amount.n, amount.scale) };
    }
    function xmlProblem(detail, choice) { try { xmlAmount(detail, choice.kind); return ''; } catch (error) { return error.message; } }
    function renderXmlSourceWarning() {
        const box = byId('pricingXmlSourceWarning'); if (!box || !draft) return;
        if (!xmlLoaded) { box.hidden = true; return; }
        const heads = new Set(xmlInvoices.map(x => Number(x.id)));
        const old = draft.billLines.filter(row => /^xml-\d+-\d+$/.test(row.billLineKey) && !heads.has(Number(row.billLineKey.split('-')[1])));
        box.hidden = !old.length;
        box.textContent = old.length ? `${old.length} dòng bill đã được chép từ XML khác hoặc XML không còn liên kết. Dữ liệu bill vẫn được giữ; hãy kiểm tra số lượng và giá trước khi áp dụng. Thay XML không tự sửa hoặc xóa bill đang nhập.` : '';
    }
    function updateXmlActions() {
        const open = byId('pricingXmlImport'); if (!open || !workspace) return;
        open.disabled = busy || xmlLoading || !editable();
        byId('pricingXmlClose').disabled = writing;
        const ready = xmlRows().filter(({ invoice, detail }) => !bill(xmlKey(invoice, detail)) &&
            xmlMappingMatches(detail, xmlChoice(invoice, detail)) && !xmlProblem(detail, xmlChoice(invoice, detail)));
        byId('pricingXmlAddAll').disabled = busy || xmlLoading || !editable() || !ready.length;
        for (const row of byId('pricingXmlRows').querySelectorAll('[data-xml-detail-id]')) {
            const item = xmlRows().find(x => x.detail.id === Number(row.dataset.xmlDetailId) && x.invoice.id === Number(row.dataset.xmlHeadId));
            if (!item) continue;
            const choice = xmlChoice(item.invoice, item.detail), p = products().find(x => x.productVariantId === choice.productVariantId);
            for (const control of row.querySelectorAll('input,select')) control.disabled = busy || xmlLoading || !editable() || !!bill(xmlKey(item.invoice, item.detail));
            row.querySelector('[data-xml-add]').disabled = busy || xmlLoading || !editable() || !!bill(xmlKey(item.invoice, item.detail)) ||
                !p || !p.units.some(x => x.unitId === choice.unitId) || !!xmlProblem(item.detail, choice);
        }
    }
    function bindXmlProductSearch(input, list, invoice, detail, choice) {
        let active = -1;
        const close = () => { list.hidden = true; input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant'); };
        const highlight = () => {
            for (const [index, option] of [...list.children].entries()) { option.classList.toggle('is-active', index === active); option.setAttribute('aria-selected', String(index === active)); }
            if (active >= 0 && list.children[active]) { input.setAttribute('aria-activedescendant', list.children[active].id); list.children[active].scrollIntoView({ block: 'nearest' }); }
        };
        const show = () => {
            if (!editable() || busy || xmlLoading) return;
            list.replaceChildren(); active = 0;
            for (const p of products().filter(p => normalize(p.productName).includes(normalize(input.value.trim()))).slice(0, 30)) {
                const option = create('button', undefined, 'sd-bill-suggestion'); option.type = 'button'; option.dataset.xmlProductOption = p.productVariantId;
                option.id = `pricingXmlProduct-${invoice.id}-${detail.id}-${p.productVariantId}`; option.setAttribute('role', 'option');
                const text = create('span'); text.append(create('strong', p.productName), create('small', `Có trong thực nhận · ${quantity(workspace.physicalLines.filter(x => x.productVariantId === p.productVariantId).reduce((n, x) => n + x.baseQuantity, 0))} ${baseUnitName(p)}`));
                option.append(thumbnail(p), text);
                option.addEventListener('mousedown', event => event.preventDefault());
                option.addEventListener('click', () => {
                    if (!editable() || busy) return;
                    choice.productVariantId = p.productVariantId; choice.unitId = null; choice.touched = true;
                    renderXmlRows(); const row = byId('pricingXmlRows').querySelector(`[data-xml-detail-id="${detail.id}"]`);
                    row?.querySelector('[data-xml-unit]')?.focus();
                });
                list.append(option);
            }
            if (!list.children.length) list.append(create('div', 'Không tìm thấy sản phẩm trên phiếu nhập.', 'sd-pricing-empty'));
            list.hidden = false; input.setAttribute('aria-expanded', 'true'); highlight();
        };
        input.addEventListener('focus', () => { input.select(); show(); });
        input.addEventListener('input', () => { choice.productVariantId = null; choice.unitId = null; choice.touched = true; show(); updateXmlActions(); });
        input.addEventListener('blur', close);
        input.addEventListener('keydown', event => {
            if (event.isComposing) return;
            if (event.key === 'Escape') { event.preventDefault(); close(); }
            else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault(); if (list.hidden) show();
                else { active = Math.max(0, Math.min(list.children.length - 1, active + (event.key === 'ArrowDown' ? 1 : -1))); highlight(); }
            } else if (event.key === 'Enter' && !list.hidden) { event.preventDefault(); list.children[active]?.click(); }
        });
    }
    function renderXmlRows() {
        const list = byId('pricingXmlRows'); if (!list || !workspace) return; list.replaceChildren();
        let added = 0, recognized = 0, unresolved = 0, special = 0;
        for (const { invoice, detail } of xmlRows()) {
            const choice = xmlChoice(invoice, detail), p = products().find(x => x.productVariantId === choice.productVariantId);
            const imported = !!bill(xmlKey(invoice, detail)), known = xmlMappingMatches(detail, choice), problem = xmlProblem(detail, choice);
            if (imported) added++; else if (problem) special++; else if (known) recognized++; else unresolved++;
            const row = create('div', undefined, 'sd-pricing-xml-row'); row.dataset.xmlDetailId = detail.id; row.dataset.xmlHeadId = invoice.id;
            const original = create('div', undefined, 'sd-pricing-xml-original');
            original.append(create('strong', `Dòng XML ${detail.lineNo || '—'} · ${detail.itemName}`),
                create('small', `${quantity(detail.quantity)} ${detail.unitName || '(chưa có đơn vị)'} × ${unitMoney(detail.unitPrice)} · Thành tiền ${money(detail.lineAmount)} trước VAT`),
                create('small', `Hóa đơn ${invoice.invoiceSeries || ''} ${invoice.invoiceNumber || invoice.id}`));
            const fields = create('div', undefined, 'sd-pricing-xml-fields'), searchBox = create('div', undefined, 'sd-bill-search');
            const searchId = `pricingXmlSearch-${invoice.id}-${detail.id}`, suggestionsId = `pricingXmlSuggestions-${invoice.id}-${detail.id}`;
            const search = create('input', undefined, 'form-control'); search.id = searchId; search.dataset.xmlProductSearch = ''; search.value = p?.productName || ''; search.placeholder = 'Gõ chọn sản phẩm trên phiếu nhập…'; search.autocomplete = 'off';
            search.setAttribute('role', 'combobox'); search.setAttribute('aria-autocomplete', 'list'); search.setAttribute('aria-expanded', 'false'); search.setAttribute('aria-controls', suggestionsId);
            const label = create('label', 'Sản phẩm hệ thống', 'form-label'); label.htmlFor = searchId;
            const suggestions = create('div', undefined, 'sd-bill-suggestions'); suggestions.id = suggestionsId; suggestions.hidden = true; suggestions.setAttribute('role', 'listbox');
            searchBox.append(label, search, suggestions); bindXmlProductSearch(search, suggestions, invoice, detail, choice);
            const units = create('select', undefined, 'form-select'); units.dataset.xmlUnit = ''; units.add(new Option('Chọn đơn vị / quy đổi', ''));
            for (const u of p?.units || []) units.add(new Option(`${u.name} ×${quantity(u.factor)}`, u.unitId)); units.value = choice.unitId || '';
            units.addEventListener('change', () => { choice.unitId = Number(units.value) || null; choice.touched = true; renderXmlRows(); byId('pricingXmlRows').querySelector(`[data-xml-detail-id="${detail.id}"] [data-xml-kind]`)?.focus(); });
            const kind = create('select', undefined, 'form-select'); kind.dataset.xmlKind = ''; kind.add(new Option('Kiểm tra loại dòng', '')); kind.add(new Option('Hàng mua', 'buy')); kind.add(new Option('Hàng tặng', 'gift')); kind.value = choice.kind;
            kind.addEventListener('change', () => { choice.kind = kind.value; choice.touched = true; renderXmlRows(); byId('pricingXmlRows').querySelector(`[data-xml-detail-id="${detail.id}"] [data-xml-add]`)?.focus(); });
            fields.append(searchBox, field('Đơn vị hệ thống tương ứng trên XML', units), field('Loại dòng', kind));
            const identity = create('div', undefined, 'sd-pricing-xml-identity'); if (p) identity.append(thumbnail(p));
            const state = create('span', imported ? '✓ Đã thêm vào bill' : known ? 'Đã nhớ sản phẩm và quy đổi' : p ? 'Cần xác nhận sản phẩm / đơn vị để nhớ' : 'Chưa ghép sản phẩm', imported ? 'text-success' : known ? 'text-success' : 'text-warning'); identity.append(state);
            if (!p && detail.itemCatalogMapping?.productVariantId) identity.append(create('span', 'Sản phẩm đã nhớ không có trong phiếu nhập này.', 'text-warning'));
            if (problem) identity.append(create('span', problem, 'text-warning'));
            else if (choice.kind === 'buy' && lineAmount(detail.quantity, detail.unitPrice) !== xmlAmount(detail, choice.kind).amount)
                identity.append(create('span', 'Đơn giá × số lượng khác thành tiền XML. Khi thêm sẽ giữ thành tiền XML và tính lại đơn giá; kiểm tra chiết khấu trước khi thêm chương trình.', 'text-warning'));
            const add = button(imported ? 'Đã thêm' : known ? 'Thêm dòng' : 'Nhớ và thêm dòng', () => importXmlRow(invoice, detail)); add.dataset.xmlAdd = ''; add.className = 'btn btn-outline-primary';
            row.append(original, fields, identity, add); list.append(row);
        }
        if (!xmlRows().length) list.append(create('div', xmlLoading ? 'Đang tải các dòng XML…' : 'Chưa có hóa đơn XML liên kết. Chọn hóa đơn XML của phiếu trước, rồi mở lại mục này.', 'sd-pricing-empty'));
        byId('pricingXmlSummary').textContent = `${recognized} sẵn sàng thêm · ${added} đã thêm · ${unresolved} cần ghép / xác nhận · ${special} cần kiểm tra loại dòng hoặc giá`;
        renderXmlSourceWarning(); updateActions();
    }
    async function loadXmlRows(invoices) {
        const generation = ++xmlGeneration, requestHost = host; xmlLoading = true; updateActions();
        try {
            if (!Array.isArray(invoices)) {
                const response = await fetch(`/admin/api/stock-documents/${receiptId}/input-invoices`, { cache: 'no-store' });
                const data = await response.json().catch(() => ({}));
                if (!response.ok) throw new Error(data.message || 'Chưa tải được XML liên kết.'); invoices = data;
            }
            if (generation !== xmlGeneration || requestHost !== host) return;
            if (!Array.isArray(invoices) || invoices.some(x => !Array.isArray(x.details))) throw new Error('Dữ liệu XML không hợp lệ.');
            if (invoices.length > 1) { xmlInvoices = []; throw new Error('Phiếu có nhiều XML đang liên kết. Kiểm tra liên kết để chỉ dùng một hóa đơn trước khi nhập nhanh.'); }
            xmlInvoices = invoices; xmlLoaded = true; byId('pricingXmlMessage').textContent = '';
        } catch (error) { if (generation === xmlGeneration && requestHost === host) byId('pricingXmlMessage').textContent = error.message; }
        finally { if (generation === xmlGeneration && requestHost === host) { xmlLoading = false; renderXmlRows(); } }
    }
    function appendXmlRow(invoice, detail) {
        if (bill(xmlKey(invoice, detail))) return false;
        const choice = xmlChoice(invoice, detail); if (!xmlMappingMatches(detail, choice)) throw new Error('Xác nhận sản phẩm và đơn vị XML trước khi thêm dòng.');
        const value = xmlAmount(detail, choice.kind);
        if (draft.billLines.length >= 500) throw new Error('Bill đã đạt giới hạn 500 dòng.');
        draft.billLines.push({ billLineKey: xmlKey(invoice, detail), lineNo: draft.billLines.length + 1,
            productVariantId: choice.productVariantId, billUnitId: choice.unitId, billQuantity: Number(detail.quantity),
            billUnitPriceBeforeVat: value.price, isGift: choice.kind === 'gift', billPriceInputMode: 'amount', billEnteredAmount: value.amount });
        return true;
    }
    function xmlDetailSignature(detail) {
        return JSON.stringify([detail.id, detail.lineNo, detail.itemName, detail.unitName, detail.quantity, detail.unitPrice, detail.lineAmount]);
    }
    async function freshXmlInvoice(expectedHeadId, generation) {
        const requestHost = host, response = await fetch(`/admin/api/stock-documents/${receiptId}/input-invoices`, { cache: 'no-store' });
        const invoices = await response.json().catch(() => ({}));
        if (generation !== xmlGeneration || requestHost !== host) throw new Error('Liên kết XML vừa thay đổi. Kiểm tra lại trước khi thêm.');
        if (!response.ok) throw new Error(invoices.message || 'Chưa kiểm tra được XML hiện đang liên kết.');
        if (!Array.isArray(invoices) || invoices.length !== 1 || invoices[0].id !== expectedHeadId || !Array.isArray(invoices[0].details)) {
            xmlInvoices = Array.isArray(invoices) && invoices.length <= 1 ? invoices : []; xmlLoaded = true; renderXmlRows();
            throw new Error('XML đã đổi hoặc không còn liên kết. Bill đang nhập được giữ; mở lại danh sách XML trước khi thêm dòng mới.');
        }
        xmlInvoices = invoices; xmlLoaded = true;
        return invoices[0];
    }
    function importXmlRow(invoice, detail) {
        if (!editable() || busy || xmlLoading || bill(xmlKey(invoice, detail))) return;
        return action(async () => {
            const generation = xmlGeneration, choice = xmlChoice(invoice, detail), p = products().find(x => x.productVariantId === choice.productVariantId), u = p?.units.find(x => x.unitId === choice.unitId);
            xmlAmount(detail, choice.kind);
            if (!p || !u) throw new Error('Chọn sản phẩm trên phiếu và đơn vị hệ thống tương ứng với đơn vị XML.');
            const original = detail, freshInvoice = await freshXmlInvoice(invoice.id, generation), freshDetail = freshInvoice.details.find(x => x.id === detail.id);
            if (!freshDetail || xmlDetailSignature(freshDetail) !== xmlDetailSignature(original)) { renderXmlRows(); throw new Error('Dòng XML vừa thay đổi. Kiểm tra dữ liệu mới trước khi thêm.'); }
            if (original.itemCatalogMapping?.mappingRowVersion !== freshDetail.itemCatalogMapping?.mappingRowVersion && !xmlMappingMatches(freshDetail, choice)) { renderXmlRows(); throw new Error('Ghép sản phẩm XML vừa thay đổi ở nơi khác. Kiểm tra lại sản phẩm và đơn vị trước khi thêm.'); }
            invoice = freshInvoice; detail = freshDetail;
            if (!xmlMappingMatches(detail, choice)) {
                const response = await fetch(`/admin/api/stock-documents/${receiptId}/pricing/xml-mappings`, { method: 'POST', headers: {
                    'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                }, body: JSON.stringify({ receiptRowVersion: workspace.receiptRowVersion, inputInvoiceHeadId: invoice.id, inputInvoiceDetailId: detail.id,
                    productVariantId: p.productVariantId, productUnitConversionId: u.conversionId, mappingRowVersion: detail.itemCatalogMapping?.mappingRowVersion || null }) });
                const result = await response.json().catch(() => ({}));
                if (!response.ok) throw new Error(result.message || 'Chưa nhớ được ghép sản phẩm XML.');
                if (generation !== xmlGeneration) throw new Error('Liên kết XML vừa thay đổi. Kiểm tra danh sách XML trước khi thêm.');
                detail.itemCatalogMapping = result;
            }
            if (generation !== xmlGeneration || !xmlRows().some(x => x.invoice.id === invoice.id && x.detail.id === detail.id)) throw new Error('XML vừa thay đổi. Kiểm tra lại trước khi thêm.');
            if (appendXmlRow(invoice, detail)) { renderBill(); markDirty(); renderXmlRows(); await loadXmlRows(); byId('pricingXmlMessage').textContent = 'Đã thêm dòng XML vào bill. Dữ liệu đang nhập được nhớ trên trình duyệt; giá Hàng hóa chỉ đổi khi Áp dụng giá nhập.'; }
        }, true);
    }
    function importKnownXmlRows() {
        if (!editable() || busy || xmlLoading) return;
        action(async () => {
            const generation = xmlGeneration, ready = xmlRows().filter(({ invoice, detail }) => !bill(xmlKey(invoice, detail)) && xmlMappingMatches(detail, xmlChoice(invoice, detail)) && !xmlProblem(detail, xmlChoice(invoice, detail)))
                .map(item => ({ ...item, choice: { ...xmlChoice(item.invoice, item.detail) } }));
            if (!ready.length) return;
            const freshInvoice = await freshXmlInvoice(ready[0].invoice.id, generation);
            const fresh = ready.map(item => ({ invoice: freshInvoice, detail: freshInvoice.details.find(x => x.id === item.detail.id), original: item }));
            if (fresh.some(item => !item.detail || xmlDetailSignature(item.detail) !== xmlDetailSignature(item.original.detail) || !xmlMappingMatches(item.detail, item.original.choice))) { renderXmlRows(); throw new Error('Dữ liệu hoặc ghép sản phẩm XML vừa thay đổi. Kiểm tra danh sách mới trước khi thêm các dòng.'); }
            if (draft.billLines.length + ready.length > 500) throw new Error('Thêm các dòng XML sẽ vượt giới hạn 500 dòng bill.');
            let added = 0; for (const { invoice, detail } of fresh) if (appendXmlRow(invoice, detail)) added++;
            if (added) { renderBill(); markDirty(); renderXmlRows(); }
            byId('pricingXmlMessage').textContent = `Đã thêm ${added} dòng đã nhận diện. Các dòng cần ghép hoặc kiểm tra vẫn ở danh sách XML; giá Hàng hóa chưa thay đổi.`;
        }, true);
    }
    function baseUnitName(p) { return p.units.find(u => u.factor === 1)?.name || 'đơn vị gốc'; }
    function quantityTicks(qty, factor = 1) {
        const q = decimalParts(qty), f = decimalParts(factor);
        if (!q || !f) return 0n;
        const n = q.n * f.n * 1000n, d = 10n ** BigInt(q.scale + f.scale);
        return n / d + (2n * (n % d) >= d ? 1n : 0n);
    }
    const tickQuantity = ticks => quantity(decimalText(ticks < 0n ? -ticks : ticks, 3));
    function quantityTerm(qty, name, factor) {
        return `${quantity(qty)} ${name}` + (factor !== 1 ? ` ×${quantity(factor)}` : '');
    }
    function totals() {
        const values = new Map(products().map(p => [p.productVariantId, { receivedTicks: 0n, buyTicks: 0n, giftTicks: 0n }]));
        for (const line of workspace.physicalLines) values.get(line.productVariantId).receivedTicks += quantityTicks(line.baseQuantity);
        // The server rounds each converted bill/gift line to three decimal places before summing by SKU.
        for (const row of draft.billLines) if (values.has(row.productVariantId)) values.get(row.productVariantId)[row.isGift ? 'giftTicks' : 'buyTicks'] += quantityTicks(row.billQuantity, unit(row.productVariantId, row.billUnitId)?.factor || 0);
        for (const rule of draft.rules.filter(x => giftRule(x) && !x.giftBillLineKey)) {
            const p = physical(rule.giftLineId); if (p && values.has(p.productVariantId)) values.get(p.productVariantId).giftTicks += quantityTicks(rule.giftQuantity, unit(p.productVariantId, rule.giftUnitId)?.factor || 0);
        }
        for (const v of values.values()) for (const k of ['received', 'buy', 'gift']) v[k] = Number(decimalText(v[k + 'Ticks'], 3));
        return values;
    }
    function renderComparison() {
        const values = totals(), invalid = invalidBillRows(), catalog = products(); let matched = 0, mismatched = 0, pending = 0;
        for (const id of ['pricingQuantityComparison', 'pricingProgramQuantityComparison']) {
            const list = byId(id); list.replaceChildren();
            list.append(create('p', 'Cộng mọi dòng của cùng sản phẩm/SKU sau khi quy đổi về đơn vị gốc. Quà đã có trên bill chỉ tính một lần.', 'sd-pricing-compare-note'));
            for (const p of catalog) {
                const v = values.get(p.productVariantId), difference = v.receivedTicks - v.buyTicks - v.giftTicks, baseName = baseUnitName(p);
                const received = workspace.physicalLines.filter(x => x.productVariantId === p.productVariantId);
                const rows = draft.billLines.filter(x => x.productVariantId === p.productVariantId);
                const extraGifts = draft.rules.filter(x => giftRule(x) && !x.giftBillLineKey && physical(x.giftLineId)?.productVariantId === p.productVariantId);
                const invalidUnit = invalid.some(x => x.productVariantId === p.productVariantId);
                const state = invalidUnit ? 'mismatch' : !rows.length && !extraGifts.length ? 'pending' : difference === 0n ? 'matched' : 'mismatch';
                if (id === 'pricingQuantityComparison') { if (state === 'matched') matched++; else if (state === 'pending') pending++; else mismatched++; }
                const row = create('div', undefined, 'sd-pricing-compare-row'), text = create('div'); text.append(create('strong', p.productName, 'sd-pricing-compare-name'));
                row.dataset.productVariantId = p.productVariantId; row.dataset.pricingComparisonState = state;
                const amounts = create('div', undefined, 'sd-pricing-compare-amounts');
                const section = (label, total, parts, key) => {
                    const box = create('div', undefined, 'sd-pricing-compare-side');
                    box.append(create('span', label), create('strong', `${tickQuantity(total)} ${baseName}`));
                    const detail = create('small', parts); detail.dataset.comparison = key; box.append(detail); return box;
                };
                const receivingParts = received.map(x => `Dòng ${x.lineNo}: ${quantityTerm(x.quantity, unit(p.productVariantId, x.unitId)?.name || '', x.factor)}`).join(' + ');
                const billParts = rows.map(x => { const u = unit(p.productVariantId, x.billUnitId); return `Dòng ${x.lineNo} (${x.isGift ? 'quà' : 'mua'}): ${quantityTerm(x.billQuantity, u?.name || '', u?.factor || 0)}`; });
                billParts.push(...extraGifts.map(x => { const u = unit(p.productVariantId, x.giftUnitId); return `Quà trong chương trình: ${quantityTerm(x.giftQuantity, u?.name || '', u?.factor || 0)}`; }));
                amounts.append(section('Thực nhận', v.receivedTicks, receivingParts, 'received-detail'), section('Theo bill', v.buyTicks + v.giftTicks, billParts.join(' + ') || 'Chưa nhập dòng bill.', 'bill-detail'));
                text.append(amounts, create('small', `Bill: mua ${tickQuantity(v.buyTicks)} + quà ${tickQuantity(v.giftTicks)} ${baseName}`, 'sd-pricing-compare-gift-total'));
                const status = create('div', undefined, 'sd-pricing-compare-status'), badge = create('span', undefined, 'sd-pricing-compare-badge');
                const icon = create('span', state === 'matched' ? '✓' : state === 'pending' ? '…' : '!'); icon.setAttribute('aria-hidden', 'true');
                badge.append(icon, create('span', state === 'matched' ? 'Khớp số lượng' : state === 'pending' ? 'Chưa nhập bill' : 'Lệch số lượng')); status.append(badge);
                if (state === 'mismatch') status.append(create('small', invalidUnit ? 'Đơn vị bill cần kiểm tra lại.' : difference > 0n ? `Thực nhận nhiều hơn bill ${tickQuantity(difference)} ${baseName}` : `Thực nhận thiếu ${tickQuantity(difference)} ${baseName} so với bill`));
                row.append(text, status); list.append(row);
            }
            for (const line of invalid) {
                const exists = workspace.physicalLines.some(p => p.productVariantId === line.productVariantId);
                const row = create('div', undefined, 'sd-pricing-compare-row');
                row.dataset.pricingComparisonState = 'mismatch';
                row.append(create('div', `Dòng bill ${line.lineNo}. ${product(line.productVariantId)?.productName || 'Sản phẩm'}`),
                    create('span', exists ? 'Đơn vị bill không còn hợp lệ; chọn lại đơn vị.' : 'Bill có sản phẩm không còn trong thực nhận; kiểm tra lại phiếu hoặc dòng bill.', 'text-danger sd-pricing-compare-status'));
                list.append(row);
            }
        }
        const summary = byId('pricingQuantitySummary');
        summary.textContent = `${matched}/${catalog.length} khớp` + (mismatched ? ` · ${mismatched} lệch` : '') + (pending ? ` · ${pending} chưa nhập` : '') + (invalid.length ? ` · ${invalid.length} dòng bill cần xử lý` : '');
        summary.dataset.state = mismatched || invalid.length ? 'mismatch' : pending ? 'pending' : 'matched';
        const overview = byId('pricingQuantityOverview'); overview.replaceChildren(); overview.dataset.state = summary.dataset.state;
        overview.append(create('strong', summary.dataset.state === 'matched' && catalog.length ? '✓ Số lượng đã khớp toàn bộ' : 'Đối chiếu số lượng: còn mục cần kiểm tra', 'sd-pricing-overview-title'));
        const counts = create('div', undefined, 'sd-pricing-overview-counts');
        for (const [state, count, label, icon] of [['matched', matched, 'Khớp', '✓'], ['mismatch', mismatched, 'Đang lệch', '!'], ['pending', pending, 'Chưa nhập bill', '…']]) {
            const card = create('div', undefined, 'sd-pricing-overview-count'); card.dataset.state = state; card.dataset.comparisonCount = state;
            card.append(create('span', `${icon} ${label}`), create('strong', String(count)), create('small', 'sản phẩm / SKU')); counts.append(card);
        }
        overview.append(counts);
        if (invalid.length) overview.append(create('small', `${invalid.length} dòng bill có sản phẩm hoặc đơn vị cần kiểm tra lại.`, 'text-danger fw-semibold'));
    }
    function renderPreview() {
        byId('pricingSystemTotal').textContent = preview ? money(preview.systemTotal) : '—';
        byId('pricingDifference').textContent = preview ? money(preview.difference) : '—';
        byId('pricingDifference').classList.toggle('text-danger', !!preview?.difference);
        const results = new Map((preview?.lines || []).map(x => [x.stockDocumentLineId, x])), list = byId('pricingFinalPrices'); list.replaceChildren();
        for (const received of workspace.physicalLines) {
            const line = results.get(received.stockDocumentLineId), row = create('div', undefined, 'sd-pricing-price-row'); row.dataset.pricingLineId = received.stockDocumentLineId;
            const receivedUnit = received.units.find(u => u.unitId === received.unitId)?.name || 'đơn vị thực nhận';
            const text = create('div'); text.append(create('strong', received.productName), create('small', `Dòng thực nhận ${received.lineNo} · ${quantity(received.quantity)} ${receivedUnit}`, 'd-block text-muted'));
            if (line) text.append(create('small', `Mua / quà: ${quantity(line.purchasedBaseQuantity)} / ${quantity(line.giftBaseQuantity)} ${baseUnitName(received)} · Tiền nhập ${money(line.finalAmountBeforeVat)}`, 'd-block text-muted'));
            const price = create('strong', line?.effectiveUnitPriceBeforeVat > 0 ? `${money(line.effectiveUnitPriceBeforeVat)}/${receivedUnit}` : '—', 'sd-pricing-price-value'); price.dataset.pricingResult = 'unitPrice';
            row.append(text, price); list.append(row);
        }
        if (preview?.errors?.length) message(preview.errors.join(' '), true);
    }
    function programGroups() { const groups = new Map(); for (const rule of draft.rules) { const key = groupKey(rule); if (!groups.has(key)) groups.set(key, []); groups.get(key).push(rule); } return groups; }
    function renderRules() {
        const list = byId('pricingRuleList'); list.replaceChildren();
        if (!draft.rules.length) list.append(create('div', 'Chưa có chương trình.', 'sd-pricing-empty'));
        for (const [key, rules] of programGroups()) {
            const first = rules[0], card = create('div', undefined, 'sd-pricing-rule'); card.dataset.programKey = key;
            const text = create('div', undefined, 'sd-pricing-rule-description');
            text.append(create('strong', first.name || (giftRule(first) ? 'Tặng hàng' : enumIs(first.type, 3, 'FixedAmountDiscount') ? `Giảm ${money(first.discountAmount)}` : `Giảm ${quantity(first.discountPercent)}%`)));
            text.append(create('small', first.billSources.map(s => { const row = bill(s.billLineKey); return `Dòng ${row?.lineNo}: ${quantity(s.quantity)} ${unit(row?.productVariantId, row?.billUnitId)?.name || ''}`; }).join(' · '), 'd-block text-muted'));
            for (const rule of rules) if (giftRule(rule)) {
                const target = physical(rule.giftLineId), fromBill = bill(rule.giftBillLineKey);
                text.append(create('small', `${fromBill ? `Quà từ dòng ${fromBill.lineNo}` : 'Quà ghi trong chương trình'}: ${quantity(rule.giftQuantity)} ${unit(target.productVariantId, rule.giftUnitId)?.name || ''} ${target.productName}`, 'd-block text-muted'));
            }
            const actions = create('div', undefined, 'sd-pricing-rule-buttons');
            actions.append(button('Sửa', () => openProgram(key)), button('Gỡ', () => {
                draft.rules = draft.rules.filter(x => groupKey(x) !== key); markDirty(); renderRules();
                if (editorPending()) { resetProgramSearches(); renderSources(); renderGifts(); }
            }));
            card.append(text, actions); list.append(card);
        }
    }
    function activateSuggestion(index, scroll = true) {
        const list = byId('pricingBillSuggestions'), search = byId('pricingBillSearch');
        const options = [...list.querySelectorAll('.sd-bill-suggestion')];
        activeSuggestion = index < 0 || !options.length ? -1 : Math.min(index, options.length - 1);
        options.forEach((el, i) => { el.classList.toggle('is-active', i === activeSuggestion); el.setAttribute('aria-selected', String(i === activeSuggestion)); });
        const active = options[activeSuggestion];
        if (!active) { search.removeAttribute('aria-activedescendant'); return; }
        search.setAttribute('aria-activedescendant', active.id);
        if (scroll) {
            const bounds = list.getBoundingClientRect(), row = active.getBoundingClientRect();
            const top = bounds.top + list.clientTop, bottom = top + list.clientHeight;
            if (row.top < top) list.scrollTop += row.top - top;
            else if (row.bottom > bottom) list.scrollTop += row.bottom - bottom;
        }
    }
    function closeSuggestions() { activateSuggestion(-1, false); byId('pricingBillSuggestions').hidden = true; byId('pricingBillSearch').setAttribute('aria-expanded', 'false'); }
    function suggestions() {
        if (!editable() || writing) return;
        const list = byId('pricingBillSuggestions'); list.replaceChildren(); const query = normalize(byId('pricingBillSearch').value);
        const matches = products().filter(p => normalize(p.productName).includes(query));
        for (const [index, p] of matches.entries()) {
            const el = button(undefined, () => selectProduct(p), 'sd-bill-suggestion'), text = create('span');
            el.id = `pricingBillSuggestion-${p.productVariantId}`; el.tabIndex = -1; el.setAttribute('role', 'option'); el.setAttribute('aria-selected', 'false');
            el.addEventListener('mouseenter', () => activateSuggestion(index, false));
            text.append(create('strong', p.productName), create('small', `Thực nhận ${quantity(totals().get(p.productVariantId).received)} ${baseUnitName(p)}`, 'd-block text-muted'));
            el.append(thumbnail(p), text); list.append(el);
        }
        if (!matches.length) list.append(create('div', 'Không có sản phẩm này trên phiếu nhập.', 'p-3 text-muted'));
        activateSuggestion(-1, false); list.scrollTop = 0;
        list.hidden = false; byId('pricingBillSearch').setAttribute('aria-expanded', 'true');
    }
    function suggestionKeydown(e) {
        if (e.isComposing || !editable() || writing) return;
        const list = byId('pricingBillSuggestions');
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault(); if (list.hidden) suggestions();
            const count = list.querySelectorAll('.sd-bill-suggestion').length;
            const index = activeSuggestion < 0 ? (e.key === 'ArrowDown' ? 0 : count - 1) : Math.max(0, activeSuggestion + (e.key === 'ArrowDown' ? 1 : -1));
            activateSuggestion(index);
        } else if (e.key === 'Enter' && !list.hidden && activeSuggestion >= 0) {
            e.preventDefault(); list.querySelectorAll('.sd-bill-suggestion')[activeSuggestion]?.click();
        } else if (e.key === 'Escape') {
            if (!list.hidden) e.preventDefault(); closeSuggestions();
        } else if (e.key === 'Tab') closeSuggestions();
    }
    function selectProduct(p) { selectedProduct = p; byId('pricingBillSearch').value = p.productName; fillUnits(byId('pricingBillUnit'), p, p.unitId); byId('pricingBillPrice').value = ''; byId('pricingBillAmount').value = ''; renderSelectedProduct(); closeSuggestions(); updateConversion(); byId('pricingBillKind').focus(); }
    function syncBillPrice() {
        const price = byId('pricingBillPrice'), amount = byId('pricingBillAmount'), hint = byId('pricingBillPriceHint');
        billPriceError = '';
        hint.classList.remove('text-danger');
        if (byId('pricingBillKind').value === 'gift') {
            price.value = 0; amount.value = 0; hint.textContent = 'Dòng hàng tặng có đơn giá và thành tiền bằng 0.'; return;
        }
        const qty = byId('pricingBillQuantity').value;
        if (byId('pricingBillPriceMode').value === 'amount') {
            const q = decimalParts(qty), a = decimalParts(amount.value);
            price.value = '';
            if (a && a.scale > 2) billPriceError = 'Thành tiền chỉ được có tối đa hai số lẻ.';
            else if (q?.n > 0n && a) {
                price.value = roundedRatio(a.n * 10n ** BigInt(q.scale + 12), q.n * 10n ** BigInt(a.scale), 12);
                if (lineAmount(qty, price.value) !== decimalText(a.n, a.scale)) billPriceError = 'Kiểm tra lại số lượng: đơn giá quy đổi chưa khớp thành tiền đã nhập.';
            }
            hint.textContent = billPriceError || (price.value && q?.n > 0n ? `${money(amount.value)} ÷ ${quantity(qty)} = ${unitMoney(price.value)} / đơn vị bill.` : 'Nhập thành tiền của dòng trước VAT; đơn giá tự tính theo số lượng trên bill.');
        } else {
            amount.value = lineAmount(qty, price.value);
            hint.textContent = amount.value ? `Thành tiền: ${quantity(qty)} × ${unitMoney(price.value)} = ${money(amount.value)}.` : 'Nhập đơn giá trước VAT; thành tiền tự tính theo số lượng trên bill.';
        }
        hint.classList.toggle('text-danger', !!billPriceError);
    }
    function updateConversion() {
        syncBillPrice();
        byId('pricingBillConversion').textContent = selectedProduct ? `Quy đổi: ${quantity(numeric('pricingBillQuantity') * (unit(selectedProduct.productVariantId, numeric('pricingBillUnit'))?.factor || 0))} ${baseUnitName(selectedProduct)}` : `Chỉ tìm trong ${products().length} sản phẩm của phiếu nhập này.`;
        updateActions();
    }
    function resetEntry() { selectedProduct = null; editingBillKey = null; byId('pricingBillSearch').value = ''; byId('pricingBillUnit').replaceChildren(); byId('pricingBillKind').value = 'buy'; byId('pricingBillQuantity').value = 1; byId('pricingBillPrice').value = ''; byId('pricingBillAmount').value = ''; renderSelectedProduct(); byId('pricingBillAdd').textContent = 'Thêm dòng bill'; byId('pricingBillCancelEdit').hidden = true; closeSuggestions(); updateConversion(); }
    function dependent(key) { return draft.rules.some(r => r.giftBillLineKey === key || r.billSources.some(s => s.billLineKey === key)); }
    function editBill(line) {
        if (!workspace.physicalLines.some(p => p.productVariantId === line.productVariantId)) { message('Sản phẩm này không còn trong thực nhận. Kiểm tra phiếu nhập hoặc gỡ chương trình rồi xóa dòng bill.', true); return; }
        editingBillKey = line.billLineKey; selectedProduct = product(line.productVariantId); byId('pricingBillSearch').value = selectedProduct.productName;
        fillUnits(byId('pricingBillUnit'), selectedProduct, line.billUnitId); byId('pricingBillKind').value = line.isGift ? 'gift' : 'buy'; byId('pricingBillQuantity').value = line.billQuantity; byId('pricingBillPrice').value = line.billUnitPriceBeforeVat;
        byId('pricingBillPriceMode').value = line.billPriceInputMode || 'unit'; byId('pricingBillAmount').value = line.billEnteredAmount ?? lineAmount(line.billQuantity, line.billUnitPriceBeforeVat); renderSelectedProduct();
        byId('pricingBillAdd').textContent = 'Lưu dòng bill'; byId('pricingBillCancelEdit').hidden = false; updateConversion(); byId('pricingBillSearch').focus(); closeSuggestions();
    }
    function removeBill(line) { if (dependent(line.billLineKey)) { message('Gỡ chương trình của dòng này trước khi xóa.', true); return; } draft.billLines = draft.billLines.filter(x => x !== line); resetEntry(); markDirty(); renderBill(); }
    function saveBill() {
        if (!editable() || busy) return;
        if (!selectedProduct || byId('pricingBillSearch').value !== selectedProduct.productName) { message('Chọn sản phẩm từ gợi ý trên phiếu nhập.', true); return; }
        syncBillPrice();
        if (billPriceError) { message(billPriceError, true); return; }
        const price = decimalParts(byId('pricingBillPrice').value), qty = decimalParts(byId('pricingBillQuantity').value);
        if (!price || !qty || qty.scale > 9 || price.scale > 12) { message('Kiểm tra số lượng và giá: số lượng tối đa 9 số lẻ, đơn giá tối đa 12 số lẻ.', true); return; }
        const row = { billLineKey: editingBillKey || crypto.randomUUID(), lineNo: draft.billLines.length + 1, productVariantId: selectedProduct.productVariantId,
            billUnitId: numeric('pricingBillUnit'), billQuantity: numeric('pricingBillQuantity'), billUnitPriceBeforeVat: decimalText(price.n, price.scale), isGift: byId('pricingBillKind').value === 'gift',
            billPriceInputMode: byId('pricingBillPriceMode').value, billEnteredAmount: byId('pricingBillAmount').value };
        if (row.billQuantity <= 0 || (!row.isGift && row.billUnitPriceBeforeVat <= 0)) { message('Nhập số lượng và đơn giá hàng mua lớn hơn 0.', true); return; }
        if (editingBillKey && dependent(editingBillKey)) { message('Gỡ chương trình của dòng này trước khi sửa. Chương trình có thể sửa SL tham gia riêng.', true); return; }
        if (editingBillKey) draft.billLines[draft.billLines.findIndex(x => x.billLineKey === editingBillKey)] = row; else draft.billLines.push(row);
        renderBill(); resetEntry(); markDirty();
        queueMicrotask(() => { byId('pricingBillSearch').focus(); suggestions(); });
    }
    function openProgram(key) {
        if (!editable() || busy) return;
        if (!draft.billLines.some(x => !x.isGift)) { message('Nhập dòng hàng mua trên bill trước khi thêm chương trình.', true); showStep('bill', false); return; }
        const rules = key ? programGroups().get(key) : null; editingProgramKey = key;
        const first = rules?.[0]; programDraft = { sources: Object.fromEntries((first?.billSources || []).map(s => [s.billLineKey, s.quantity])), gifts: [] };
        if (rules?.some(giftRule)) programDraft.gifts = rules.map(r => ({ key: r.giftBillLineKey ? `b:${r.giftBillLineKey}` : `p:${physical(r.giftLineId).productVariantId}`, quantity: r.giftQuantity, unitId: r.giftUnitId,
            manual: draft.giftValuations.find(v => v.productVariantId === physical(r.giftLineId).productVariantId)?.manualUnitValueBeforeVat ?? '' }));
        else programDraft.gifts = [];
        byId('pricingRuleEditor').hidden = false; byId('pricingRuleError').textContent = ''; byId('pricingRuleType').value = first && !giftRule(first) ? (enumIs(first.type, 3, 'FixedAmountDiscount') ? '3' : '2') : '1'; byId('pricingRuleName').value = first?.name || ''; byId('pricingRuleDiscount').value = first?.discountPercent || 10; byId('pricingRuleDiscountAmount').value = first?.discountAmount || '';
        byId('pricingRuleEditorTitle').textContent = key ? 'Sửa chương trình' : 'Chương trình mới';
        byId('pricingExtraGiftChooser').open = false; host.querySelector('.sd-program-optional-name').open = !!first?.name;
        resetProgramSearches(); renderSources(); renderGifts(); programType(); updateActions(); byId('pricingRuleType').focus();
    }
    function remainingProgramQuantity(line) {
        const used = [];
        for (const [key, rules] of programGroups()) {
            if (key === editingProgramKey) continue;
            if (!line.isGift) {
                // A program can have several gift rules sharing the same source rows.
                // Reserve each source once for that program, rather than once per gift.
                const quantities = rules.flatMap(r => r.billSources.filter(s => s.billLineKey === line.billLineKey).map(s => s.quantity));
                if (quantities.length) used.push(Math.max(...quantities));
            } else {
                for (const rule of rules.filter(r => giftRule(r) && r.giftBillLineKey === line.billLineKey)) {
                    if (rule.giftUnitId === line.billUnitId) { used.push(rule.giftQuantity); continue; }
                    const q = decimalParts(rule.giftQuantity), from = decimalParts(unit(line.productVariantId, rule.giftUnitId)?.factor), to = decimalParts(unit(line.productVariantId, line.billUnitId)?.factor);
                    if (!q || !from || !to?.n) return 0;
                    used.push(roundedRatio(q.n * from.n * 10n ** BigInt(to.scale + 9), to.n * 10n ** BigInt(q.scale + from.scale), 9));
                }
            }
        }
        const values = [line.billQuantity, ...used].map(decimalParts);
        if (values.some(v => !v)) return 0;
        const scale = Math.max(...values.map(v => v.scale));
        const amounts = values.map(v => v.n * 10n ** BigInt(scale - v.scale));
        const left = amounts[0] - amounts.slice(1).reduce((sum, value) => sum + value, 0n);
        return left > 0n ? Number(decimalText(left, scale)) : 0;
    }
    function hasOtherDiscount(line) {
        return draft.rules.some(r => groupKey(r) !== editingProgramKey && !giftRule(r) && r.billSources.some(s => s.billLineKey === line.billLineKey));
    }
    function programBillDescription(line, available) {
        const p = product(line.productVariantId), u = unit(line.productVariantId, line.billUnitId);
        const identity = create('div', undefined, 'sd-program-line-identity'), text = create('span');
        text.append(create('strong', `Dòng ${line.lineNo} · ${p?.productName || 'Sản phẩm'}`),
            create('span', line.isGift ? 'Quà tặng' : 'Hàng mua', `badge ${line.isGift ? 'bg-label-success' : 'bg-label-primary'} ms-2`),
            create('small', `${quantity(line.billQuantity)} ${u?.name || ''} × ${unitMoney(line.billUnitPriceBeforeVat)} · Thành tiền ${money(lineAmount(line.billQuantity, line.billUnitPriceBeforeVat))} trước VAT`, 'd-block text-muted'));
        if (available !== undefined && available < line.billQuantity) text.append(create('strong', `Còn lại: ${quantity(available)} ${u?.name || ''} · ${money(lineAmount(available, line.billUnitPriceBeforeVat))} trước VAT`, 'd-block text-primary'));
        identity.append(thumbnail(p), text); return identity;
    }
    function resetProgramSearches() { for (const search of programSearches) { search.input.value = ''; search.close(); } }
    function bindProgramSearch(inputId, listId, choices, choose) {
        const input = byId(inputId), list = byId(listId); let active = -1;
        const activate = (index, scroll = true) => {
            const options = [...list.querySelectorAll('[role="option"]')];
            active = index < 0 || !options.length ? -1 : Math.min(index, options.length - 1);
            options.forEach((el, i) => { el.classList.toggle('is-active', i === active); el.setAttribute('aria-selected', String(i === active)); });
            const option = options[active];
            if (!option) { input.removeAttribute('aria-activedescendant'); return; }
            input.setAttribute('aria-activedescendant', option.id);
            if (scroll) {
                const bounds = list.getBoundingClientRect(), row = option.getBoundingClientRect(), top = bounds.top + list.clientTop, bottom = top + list.clientHeight;
                if (row.top < top) list.scrollTop += row.top - top; else if (row.bottom > bottom) list.scrollTop += row.bottom - bottom;
            }
        };
        const close = () => { activate(-1, false); list.hidden = true; input.setAttribute('aria-expanded', 'false'); };
        const show = () => {
            if (!editable() || writing || !editorPending()) return;
            list.replaceChildren(); const query = normalize(input.value.trim());
            const matches = choices().filter(x => normalize(x.search).includes(query));
            for (const [index, item] of matches.entries()) {
                const option = button(undefined, () => { if (!editable() || writing) return; close(); input.value = ''; choose(item); }, 'sd-bill-suggestion');
                option.id = `${listId}-${index}`; option.tabIndex = -1; option.setAttribute('role', 'option'); option.setAttribute('aria-selected', 'false');
                if (item.line) { option.dataset.programBillKey = item.line.billLineKey; option.dataset.programAvailableQuantity = item.available; option.append(programBillDescription(item.line, item.available)); }
                else { option.dataset.programGiftProduct = item.product.productVariantId; const text = create('span'); text.append(create('strong', item.product.productName), create('small', 'Quà ghi trong nội dung chương trình · sản phẩm có trong thực nhận', 'd-block text-muted')); option.append(thumbnail(item.product), text); }
                option.addEventListener('mouseenter', () => activate(index, false)); list.append(option);
            }
            if (!matches.length) list.append(create('div', 'Không còn dòng phù hợp để chọn. Các dòng đã dùng hết trong chương trình sẽ được ẩn.', 'p-3 text-muted'));
            activate(-1, false); list.scrollTop = 0; list.hidden = false; input.setAttribute('aria-expanded', 'true');
        };
        for (const event of ['focus', 'click', 'input']) input.addEventListener(event, show);
        input.addEventListener('keydown', e => {
            if (e.isComposing || !editable() || writing) return;
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault(); if (list.hidden) show(); const count = list.querySelectorAll('[role="option"]').length;
                activate(active < 0 ? (e.key === 'ArrowDown' ? 0 : count - 1) : Math.max(0, active + (e.key === 'ArrowDown' ? 1 : -1)));
            } else if (e.key === 'Enter' && !list.hidden && active >= 0) { e.preventDefault(); list.querySelectorAll('[role="option"]')[active]?.click(); }
            else if (e.key === 'Escape' || e.key === 'Tab') { if (e.key === 'Escape' && !list.hidden) e.preventDefault(); close(); }
        });
        host.addEventListener('click', e => { if (e.target !== input && !list.contains(e.target)) close(); }, true);
        programSearches.push({ input, close });
    }
    function addProgramBill(line) {
        const available = remainingProgramQuantity(line);
        if (!(available > 0) || (!line.isGift && numeric('pricingRuleType') !== 1 && hasOtherDiscount(line))) return;
        if (line.isGift) {
            if (numeric('pricingRuleType') !== 1 || programDraft.gifts.some(g => g.key === `b:${line.billLineKey}`)) return;
            programDraft.gifts.push({ key: `b:${line.billLineKey}`, quantity: available, unitId: line.billUnitId, manual: '' });
            programDraft.gifts.sort((a, b) => (bill(a.key.slice(2))?.lineNo ?? Infinity) - (bill(b.key.slice(2))?.lineNo ?? Infinity));
        } else {
            if (Object.hasOwn(programDraft.sources, line.billLineKey)) return;
            programDraft.sources[line.billLineKey] = available;
        }
        renderSources(); renderGifts(); updateActions();
        const input = line.isGift ? byId('pricingGiftTargets').querySelector(`[data-gift-bill-key="${line.billLineKey}"] [data-gift-quantity]`) : host.querySelector(`[data-source-quantity="${line.billLineKey}"]`);
        input?.focus(); input?.select();
    }
    function renderSources() {
        const list = byId('pricingRuleSources'); list.replaceChildren();
        const selected = draft.billLines.filter(x => !x.isGift && Object.hasOwn(programDraft.sources, x.billLineKey)).sort((a, b) => a.lineNo - b.lineNo);
        if (!selected.length) list.append(create('div', 'Gõ tìm ở trên và chọn dòng mua để thêm vào chương trình.', 'sd-pricing-empty'));
        for (const line of selected) {
            const row = create('div', undefined, 'sd-pricing-source-row'); row.dataset.selectedSourceBillKey = line.billLineKey;
            const available = remainingProgramQuantity(line);
            const qty = number(programDraft.sources[line.billLineKey], value => programDraft.sources[line.billLineKey] = value, available); qty.dataset.sourceQuantity = line.billLineKey; qty.addEventListener('focus', () => qty.select());
            qty.addEventListener('keydown', e => {
                if (e.key !== 'Enter' || e.isComposing || !editable() || writing) return;
                e.preventDefault();
                const search = byId('pricingRuleSearch'); search.value = ''; search.focus();
            });
            const remove = button('Bỏ', () => { delete programDraft.sources[line.billLineKey]; renderSources(); renderGifts(); updateActions(); }); remove.setAttribute('aria-label', `Bỏ dòng mua ${line.lineNo} khỏi chương trình`);
            row.append(programBillDescription(line, available), field('SL tham gia', qty), remove); list.append(row);
        }
    }
    function programType() {
        const gift = numeric('pricingRuleType') === 1;
        byId('pricingDiscountFields').hidden = numeric('pricingRuleType') !== 2; byId('pricingDiscountAmountFields').hidden = numeric('pricingRuleType') !== 3; byId('pricingGiftFields').hidden = !gift;
        byId('pricingRuleSearchHint').textContent = gift ? 'Chỉ hiện dòng còn SL chưa dùng trong chương trình khác. Chọn dòng mua hoặc quà; mặc định lấy hết SL còn lại, có thể sửa sau khi thêm.' : 'Chỉ hiện dòng mua còn SL chưa dùng và chưa có chương trình giảm giá khác. Mặc định lấy hết SL còn lại, có thể sửa SL tham gia.';
        resetProgramSearches();
    }
    function giftProduct(g) { return g.key.startsWith('b:') ? product(bill(g.key.slice(2))?.productVariantId) : product(g.key.slice(2)); }
    function renderGifts() {
        const list = byId('pricingGiftTargets'); list.replaceChildren();
        if (!programDraft.gifts.length) list.append(create('div', 'Chọn dòng quà ở ô tìm trên; nếu quà chỉ ghi trong nội dung chương trình, tìm ở mục bên dưới.', 'sd-pricing-empty'));
        for (const [index, g] of programDraft.gifts.entries()) {
            const row = create('div', undefined, 'sd-pricing-gift-row'), first = create('div', undefined, 'sd-pricing-gift-choice');
            const p = giftProduct(g), fromBill = g.key.startsWith('b:') ? bill(g.key.slice(2)) : null;
            if (fromBill) { row.dataset.giftBillKey = fromBill.billLineKey; first.append(programBillDescription(fromBill, remainingProgramQuantity(fromBill))); }
            else { const identity = create('div', undefined, 'sd-program-line-identity'), text = create('span'); text.append(create('strong', p?.productName || 'Quà cần chọn lại'), create('small', 'Quà ghi trong nội dung chương trình · không có dòng riêng trên bill', 'd-block text-muted')); identity.append(thumbnail(p), text); first.append(identity); }
            const remove = button('Bỏ', () => { programDraft.gifts.splice(index, 1); renderGifts(); updateActions(); }); remove.setAttribute('aria-label', `Bỏ quà ${fromBill ? `dòng ${fromBill.lineNo}` : p?.productName || index + 1}`);
            first.append(remove); row.append(first);
            if (p) {
                const fields = create('div', undefined, 'sd-pricing-gift-fields'); const qty = number(g.quantity, v => g.quantity = v, fromBill ? remainingProgramQuantity(fromBill) : undefined); qty.dataset.giftQuantity = index; qty.addEventListener('focus', () => qty.select());
                const units = create('select', undefined, 'form-select'); fillUnits(units, p, g.unitId); units.dataset.giftUnit = index;
                if (g.key.startsWith('b:')) { units.disabled = true; units.dataset.giftUnitLocked = 'true'; }
                units.addEventListener('change', () => { g.unitId = Number(units.value); g.manual = ''; renderGifts(); });
                fields.append(field('Số lượng quà', qty), field('Đơn vị', units));
                const same = Object.keys(programDraft.sources).length > 0 && Object.keys(programDraft.sources).every(k => bill(k).productVariantId === p.productVariantId);
                if (!same) { const ref = number(g.manual, v => g.manual = v); ref.min = '0.000000000001'; ref.dataset.giftValue = index;
                    fields.append(field('Giá quà thủ công / đơn vị đã chọn', ref)); row.append(create('small', 'Ưu tiên giá nhập đã xác nhận. Chỉ nhập giá thủ công khi chưa có lịch sử; mỗi sản phẩm quà dùng một giá trị.', 'd-block text-muted')); }
                row.append(fields);
            }
            list.append(row);
        }
    }
    function saveProgram() {
        if (!editable() || busy) return;
        const fail = text => byId('pricingRuleError').textContent = text;
        const sources = Object.entries(programDraft.sources).map(([billLineKey, quantity]) => ({ billLineKey, quantity }));
        if (invalidBillRows().length) { fail('Kiểm tra các dòng bill không còn khớp sản phẩm hoặc đơn vị thực nhận trước khi gắn chương trình.'); return; }
        sources.sort((a, b) => bill(a.billLineKey).lineNo - bill(b.billLineKey).lineNo);
        if (!sources.length || sources.some(s => !Number.isFinite(s.quantity) || s.quantity <= 0 || s.quantity > bill(s.billLineKey).billQuantity)) { fail('Chọn dòng mua và SL tham gia lớn hơn 0, không vượt số lượng dòng bill.'); return; }
        const overused = sources.find(s => s.quantity > remainingProgramQuantity(bill(s.billLineKey)));
        if (overused) { const row = bill(overused.billLineKey); fail(`Dòng bill ${row.lineNo}: chỉ còn ${quantity(remainingProgramQuantity(row))} ${unit(row.productVariantId, row.billUnitId)?.name || ''} chưa dùng trong chương trình khác.`); return; }
        const type = numeric('pricingRuleType'), percent = numeric('pricingRuleDiscount'), key = editingProgramKey || crypto.randomUUID(), name = byId('pricingRuleName').value.trim();
        const remaining = draft.rules.filter(r => groupKey(r) !== editingProgramKey);
        if (type === 2 && (percent <= 0 || percent >= 100)) { fail('Mức giảm phải lớn hơn 0% và dưới 100%.'); return; }
        const amount = decimalParts(byId('pricingRuleDiscountAmount').value);
        if (type === 3 && (!amount || amount.n <= 0n || amount.scale > 2)) { fail('Nhập số tiền giảm lớn hơn 0, tối đa hai số lẻ.'); return; }
        if (type === 3) {
            const parts = sources.map(s => ({ q: decimalParts(s.quantity), p: decimalParts(bill(s.billLineKey).billUnitPriceBeforeVat) }));
            if (parts.some(x => !x.q || !x.p)) { fail('Kiểm tra lại số lượng và đơn giá của các dòng tham gia.'); return; }
            const scale = Math.max(amount.scale, ...parts.map(x => x.q.scale + x.p.scale));
            const total = parts.reduce((sum, x) => sum + x.q.n * x.p.n * 10n ** BigInt(scale - x.q.scale - x.p.scale), 0n);
            if (amount.n * 10n ** BigInt(scale - amount.scale) >= total) { fail('Tiền giảm phải nhỏ hơn tổng tiền hàng của số lượng tham gia.'); return; }
        }
        if (type !== 1 && remaining.some(r => !giftRule(r) && r.billSources.some(s => sources.some(x => x.billLineKey === s.billLineKey)))) { fail('Một dòng bill chỉ có một chương trình giảm giá.'); return; }
        const common = { programKey: key, name, type, sourceLineIds: [], billSources: sources, discountPercent: type === 2 ? percent : 0, discountAmount: type === 3 ? decimalText(amount.n, amount.scale) : 0 };
        const rules = [], valuations = new Map(draft.giftValuations.map(v => [v.productVariantId, { ...v }]));
        if (type !== 1) rules.push({ ...common, ruleKey: crypto.randomUUID(), giftLineId: null, giftUnitId: null, giftQuantity: 0, giftMode: 2, giftBillLineKey: null });
        else {
            if (!programDraft.gifts.length || programDraft.gifts.some(g => !giftProduct(g) || !Number.isFinite(g.quantity) || g.quantity <= 0)) { fail('Chọn hàng tặng và số lượng quà lớn hơn 0.'); return; }
            const giftAmounts = new Map();
            for (const g of programDraft.gifts) {
                const p = giftProduct(g), giftBill = g.key.startsWith('b:') ? bill(g.key.slice(2)) : null;
                if (!workspace.physicalLines.some(x => x.productVariantId === p.productVariantId && x.units.some(u => u.unitId === g.unitId))) { fail('Hàng tặng hoặc đơn vị quà không còn trong phiếu nhập; chọn lại hàng tặng.'); return; }
                if (giftBill) {
                    if (g.quantity > remainingProgramQuantity(giftBill)) { fail(`Dòng quà bill ${giftBill.lineNo}: chỉ còn ${quantity(remainingProgramQuantity(giftBill))} ${unit(p.productVariantId, giftBill.billUnitId)?.name || ''} chưa dùng trong chương trình khác.`); return; }
                    const used = remaining.filter(r => r.giftBillLineKey === giftBill.billLineKey).reduce((n, r) => n + r.giftQuantity * unit(p.productVariantId, r.giftUnitId).factor, 0);
                    const sum = (giftAmounts.get(giftBill.billLineKey) || 0) + g.quantity * unit(p.productVariantId, g.unitId).factor;
                    giftAmounts.set(giftBill.billLineKey, sum);
                    if (Math.round((used + sum - giftBill.billQuantity * unit(p.productVariantId, giftBill.billUnitId).factor) * 1000) > 0) { fail('Số lượng quà gắn chương trình vượt số lượng trên dòng bill.'); return; }
                }
                const same = sources.every(s => bill(s.billLineKey).productVariantId === p.productVariantId);
                rules.push({ ...common, ruleKey: crypto.randomUUID(), giftLineId: p.stockDocumentLineId, giftUnitId: g.unitId, giftQuantity: g.quantity, giftMode: same ? 1 : 2, giftBillLineKey: giftBill?.billLineKey || null });
                if (!same && g.manual !== '') valuations.set(p.productVariantId, { productVariantId: p.productVariantId, unitId: g.unitId, manualUnitValueBeforeVat: Number(g.manual) || null });
            }
        }
        draft.rules = [...remaining, ...rules]; draft.giftValuations = [...valuations.values()]; byId('pricingRuleEditor').hidden = true;
        markDirty(); renderRules();
    }
    function calculatePreview() {
        clearTimeout(timer);
        return action(async () => {
            const revision = pricingRevision;
            preview = await api('POST', '/preview', draft);
            if (revision !== pricingRevision) { preview = null; renderPreview(); return; }
            renderPreview(); renderRules();
            if (preview.canApply) message(dirty ? 'Số lượng và tiền hàng đã khớp. Lưu kế hoạch trước khi áp dụng giá.' : 'Số lượng và tiền hàng đã khớp.');
        });
    }
    function restoreEditor(saved) {
        if (!saved) return;
        editingProgramKey = saved.key; programDraft = saved.value;
        byId('pricingRuleEditor').hidden = false; byId('pricingRuleType').value = saved.type;
        byId('pricingRuleName').value = saved.name; byId('pricingRuleDiscount').value = saved.percent; byId('pricingRuleDiscountAmount').value = saved.amount;
        host.querySelector('.sd-program-optional-name').open = !!saved.name;
        renderSources(); renderGifts(); programType(); updateActions();
    }
    function init() {
        const next = byId('receiptPricingAllocation'); if (!next || next === host) return;
        // The receiving workbench replaces its DOM after editing received goods.
        // Keep bill/program input locally; only the new server tokens and receiving rows are authoritative.
        const sameReceipt = receiptId === window.stockDocumentPage.documentId;
        const oldWorkspace = sameReceipt ? workspace : null;
        const retained = oldWorkspace && (dirty || editorPending() || entryPending()) ? captureDraft() : null;
        if (!sameReceipt) previousPhysical.clear();
        for (const p of oldWorkspace?.physicalLines || []) previousPhysical.set(p.stockDocumentLineId, p);
        receiptId = window.stockDocumentPage.documentId; clearTimeout(timer); pricingRevision++;
        host = next; workspace = null; dirty = false; busy = false; writing = false; step = 'bill'; programSearches = []; restoringDraft = true; pendingLocalDraft = null; localDraftRemembered = false;
        xmlGeneration++; xmlLoading = false; xmlLoaded = false; xmlInvoices = []; if (!sameReceipt) xmlChoices.clear();
        localDraftKey = Number(host.dataset.draftStoreId) > 0 && Number(host.dataset.draftUserId) > 0
            ? `gaoapp:receipt-pricing-draft:v1:${host.dataset.draftStoreId}:${host.dataset.draftUserId}:${receiptId}` : null;
        const remembered = retained || readLocalDraft();
        byId('pricingReload').addEventListener('click', () => action(async () => { if ((dirty || editorPending() || entryPending() || pendingLocalDraft) && !window.confirm('Tải lại sẽ bỏ bản nháp bill và chương trình đang nhập, lấy kế hoạch từ máy chủ. Tiếp tục?')) return; accept(await api('GET'), true); }, true));
        byId('pricingPreview').addEventListener('click', calculatePreview);
        byId('pricingXmlImport').addEventListener('click', () => { byId('pricingXmlPanel').hidden = false; loadXmlRows(); });
        byId('pricingXmlClose').addEventListener('click', () => { byId('pricingXmlPanel').hidden = true; });
        byId('pricingXmlAddAll').addEventListener('click', importKnownXmlRows);
        byId('pricingSave').addEventListener('click', () => action(async () => accept(await api('PUT', '', draft), true), true));
        byId('pricingApply').addEventListener('click', () => action(async () => accept(await api('POST', '/apply', { receiptRowVersion: workspace.receiptRowVersion, planRowVersion: workspace.planRowVersion }), true), true));
        byId('pricingRestoreLocalDraft').addEventListener('click', () => {
            if (!pendingLocalDraft || !editable() || busy) return;
            restoringDraft = true; restoreDraft(pendingLocalDraft); pendingLocalDraft = null; restoringDraft = false;
            byId('pricingRestoreLocalDraft').hidden = true; dirty = true; updateActions(); rememberDraft();
        });
        for (const event of ['input', 'change']) host.addEventListener(event, rememberDraft);
        host.addEventListener('click', () => queueMicrotask(rememberDraft));
        for (const el of host.querySelectorAll('[data-pricing-step]')) el.addEventListener('click', () => showStep(el.dataset.pricingStep));
        for (const [id, key] of [['pricingActualBillTotal', 'actualBillTotal'], ['pricingGlobalDiscount', 'globalDiscountPercent']]) byId(id).addEventListener('input', () => { draft[key] = numeric(id); markDirty(); timer = setTimeout(calculatePreview, 450); });
        byId('pricingBillSearch').addEventListener('focus', suggestions);
        byId('pricingBillSearch').addEventListener('click', suggestions);
        byId('pricingBillSearch').addEventListener('input', () => { selectedProduct = null; renderSelectedProduct(); suggestions(); updateActions(); });
        byId('pricingBillSearch').setAttribute('role', 'combobox'); byId('pricingBillSearch').setAttribute('aria-autocomplete', 'list'); byId('pricingBillSearch').setAttribute('aria-haspopup', 'listbox');
        byId('pricingBillSuggestions').setAttribute('role', 'listbox'); byId('pricingBillSuggestions').setAttribute('aria-label', 'Sản phẩm trên phiếu nhập');
        byId('pricingBillSearch').addEventListener('keydown', suggestionKeydown);
        host.addEventListener('click', e => { if (!e.target.closest('.sd-bill-search')) closeSuggestions(); }, true);
        for (const id of ['pricingBillQuantity', 'pricingBillKind', 'pricingBillUnit', 'pricingBillPriceMode']) byId(id).addEventListener('input', updateConversion);
        for (const id of ['pricingBillPrice', 'pricingBillAmount']) byId(id).addEventListener('input', syncBillPrice);
        const quantityInput = byId('pricingBillQuantity'); let quantityJustFocused = false;
        quantityInput.addEventListener('focus', () => { quantityInput.select(); quantityJustFocused = true; });
        quantityInput.addEventListener('click', () => { if (quantityJustFocused) quantityInput.select(); quantityJustFocused = false; });
        quantityInput.addEventListener('keydown', () => { quantityJustFocused = false; });
        quantityInput.addEventListener('blur', () => { quantityJustFocused = false; });
        byId('pricingBillAdd').addEventListener('click', saveBill); byId('pricingBillCancelEdit').addEventListener('click', resetEntry);
        host.querySelector('.sd-bill-entry').addEventListener('keydown', e => { if (e.key === 'Enter' && e.target.tagName === 'INPUT' && e.target.id !== 'pricingBillSearch') { e.preventDefault(); saveBill(); } });
        byId('pricingNewProgram').addEventListener('click', () => openProgram()); byId('pricingCloseRule').addEventListener('click', () => { resetProgramSearches(); byId('pricingRuleEditor').hidden = true; updateActions(); });
        byId('pricingRuleType').addEventListener('change', programType); byId('pricingSaveRule').addEventListener('click', saveProgram);
        byId('pricingRuleEditor').addEventListener('input', () => { byId('pricingRuleError').textContent = ''; });
        bindProgramSearch('pricingRuleSearch', 'pricingRuleSuggestions', () => draft.billLines.map(line => ({ line, available: remainingProgramQuantity(line), search: `Dòng ${line.lineNo} ${product(line.productVariantId)?.productName || ''}` }))
            .filter(({ line, available }) => available > 0 && (line.isGift
                ? numeric('pricingRuleType') === 1 && !programDraft.gifts.some(g => g.key === `b:${line.billLineKey}`)
                : !Object.hasOwn(programDraft.sources, line.billLineKey) && (numeric('pricingRuleType') === 1 || !hasOtherDiscount(line))))
            .sort((a, b) => a.line.lineNo - b.line.lineNo), item => addProgramBill(item.line));
        bindProgramSearch('pricingExtraGiftSearch', 'pricingExtraGiftSuggestions', () => products().map(p => ({ product: p, search: p.productName })), item => {
            const p = item.product; programDraft.gifts.push({ key: `p:${p.productVariantId}`, quantity: 1, unitId: p.unitId, manual: draft.giftValuations.find(v => v.productVariantId === p.productVariantId)?.manualUnitValueBeforeVat ?? '' });
            renderGifts(); updateActions(); const input = byId('pricingGiftTargets').querySelector(`[data-gift-quantity="${programDraft.gifts.length - 1}"]`); input?.focus(); input?.select();
        });
        action(async () => {
            try {
                accept(await api('GET'));
                if (!editable()) {
                    if (byId('commercialApprovalWorkbench').dataset.readonly === 'true') clearLocalDraft();
                    return;
                }
                if (!remembered) return;
                if (remembered.basePlanId === workspace.planId && remembered.basePlanVersion === workspace.planRowVersion && remembered.baseState === workspace.state) restoreDraft(remembered);
                else {
                    pendingLocalDraft = remembered; byId('pricingRestoreLocalDraft').hidden = false;
                    localDraftStatus('Có bản nháp trên máy, nhưng kế hoạch trên máy chủ đã thay đổi. Kiểm tra trước khi khôi phục.', true);
                }
            } catch (error) {
                if (next !== host) return;
                if (!retained) throw error;
                workspace = oldWorkspace; workspace.isStale = true; draft = retained.draft; dirty = true; preview = null; step = retained.step; render();
                restoreEditor(retained.editor); restoreEntry(retained.entry);
                message('Đã giữ bill chưa lưu nhưng chưa tải được thực nhận mới. Vui lòng tải lại phần duyệt phiếu. ' + error.message, true);
            } finally { restoringDraft = false; rememberDraft(); if (workspace && next === host) loadXmlRows(); }
        }, window.stockDocumentPage?.canApproveCommercial === true);
    }
    window.addEventListener('pagehide', rememberDraft);
    window.addEventListener('input-invoices-updated', event => {
        const documentId = event.detail?.stockDocumentId || event.detail?.receiptId;
        if (!workspace || (documentId && Number(documentId) !== receiptId)) return;
        loadXmlRows(event.detail?.invoices);
    });
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') rememberDraft(); });
    window.addEventListener('beforeunload', event => { rememberDraft(); if ((dirty || editorPending() || entryPending()) && !localDraftRemembered) { event.preventDefault(); event.returnValue = ''; } });
    function pricesSaved(value) {
        if (!workspace) return;
        workspace.receiptRowVersion = draft.receiptRowVersion = value.rowVersion;
        for (const line of workspace.physicalLines) {
            const version = value.lineVersions[line.stockDocumentLineId];
            if (!version) continue;
            line.rowVersion = version;
            const input = draft.lines.find(x => x.stockDocumentLineId === line.stockDocumentLineId);
            if (input) input.rowVersion = version;
        }
        if (workspace.planId) {
            workspace.isStale = true;
            message('Giá Hàng hóa đã lưu. Khi duyệt, hệ thống dùng các giá này. Muốn áp dụng lại theo bill, lưu kế hoạch rồi áp dụng lại.');
        }
        updateActions();
    }
    // The optional calculator does not block approval of valid Goods prices.
    window.GaoReceiptPricingAllocation = { init, pricesSaved, hasUnsaved: () => dirty || editorPending() || entryPending(), blocksConfirm: () => false };
})();
