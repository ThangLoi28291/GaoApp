(function () {
    'use strict';

    let manualFreightAllocation = false;

    // Expose this before DOMContentLoaded so the generic receipt page can
    // delegate without depending on script event-handler order.
    window.GaoAppPurchaseReceiptApproval = {
        submit: submitCommercialApproval,
        open: openCommercialApprovalConfirmation
    };

    document.addEventListener('DOMContentLoaded', initCommercialApproval);

    function initCommercialApproval() {
        const workbench = document.getElementById('commercialApprovalWorkbench');
        if (!workbench) return;

        const readOnly = workbench.dataset.readonly === 'true';
        const canApprove = window.stockDocumentPage?.canApproveCommercial === true;
        const isPurchaseOrderReceipt = Number(window.stockDocumentPage?.receiptSource) === 2;
        const supplier = document.getElementById('commercialSupplierId');
        const hasPersistedFreight = document.getElementById('commercialHasFreight')?.checked === true;
        manualFreightAllocation = !readOnly && hasPersistedFreight &&
            Array.from(document.querySelectorAll('.commercial-freight-allocation'))
                .some(function (input) { return readNumber(input) > 0; });

        if (!readOnly && !isPurchaseOrderReceipt) {
            initCommercialSupplierLookup(supplier);
        } else if (supplier) {
            supplier.disabled = true;
        }

        bindCommercialInputs(readOnly);
        bindCommercialActions(canApprove, readOnly);
        syncVatState();
        syncFreightState(false);
        recalculateCommercialTotals();
    }

    function initCommercialSupplierLookup(element) {
        if (!element) return;
        if (!window.jQuery || !jQuery.fn.select2) {
            populateNativeSupplierOptions(element);
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
        });
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
        });

        document.getElementById('commercialMerchandisePaid')?.addEventListener('change', syncSettlementState);
        document.getElementById('commercialHasFreight')?.addEventListener('change', function () {
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
        if (!validateCommercialApproval(true)) return;
        updateApproveModalSummary();
        document.getElementById('btnOpenApproveModal')?.click();
    }

    async function submitCommercialApproval() {
        if (!validateCommercialApproval(true)) {
            hideApproveModal();
            return;
        }

        const button = document.getElementById('btnApprove');
        const message = document.getElementById('approveMessage');
        const documentId = Number(window.stockDocumentPage?.documentId || 0);
        const payload = buildCommercialPayload();

        if (message) message.textContent = '';
        if (button) {
            button.disabled = true;
            button.dataset.originalText = button.innerHTML;
            button.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Đang ghi sổ...';
        }

        try {
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
            if (message) message.textContent = 'Không kết nối được máy chủ. Dữ liệu chưa được ghi sổ.';
        } finally {
            if (button) {
                button.disabled = false;
                button.innerHTML = button.dataset.originalText || 'Xác nhận duyệt';
            }
        }
    }

    function buildCommercialPayload() {
        const hasVat = document.getElementById('commercialHasVat')?.checked === true;
        const hasFreight = document.getElementById('commercialHasFreight')?.checked === true;
        const lines = getCommercialRows().map(function (row) {
            const tax = row.querySelector('.commercial-tax');
            return {
                stockDocumentLineId: Number(row.dataset.lineId),
                unitPriceBeforeVat: roundMoney(readNumber(row.querySelector('.commercial-unit-price'))),
                taxId: hasVat && tax?.value ? Number(tax.value) : null
            };
        });

        return {
            rowVersion: String(window.stockDocumentPage?.rowVersion || ''),
            approvalNote: document.getElementById('approveNote')?.value?.trim() || null,
            hasVat: hasVat,
            supplierId: nullablePositiveInt(document.getElementById('commercialSupplierId')?.value),
            isMerchandisePaid: document.getElementById('commercialMerchandisePaid')?.checked === true,
            merchandisePayeeName: valueOrNull('commercialPayeeName'),
            lines: lines,
            hasFreight: hasFreight,
            freightTotal: hasFreight ? roundMoney(readNumber(document.getElementById('commercialFreightTotal'))) : 0,
            freightPayeeName: hasFreight ? valueOrNull('commercialFreightPayee') : null,
            freightNote: hasFreight ? valueOrNull('commercialFreightNote') : null,
            isFreightPaid: hasFreight && document.getElementById('commercialFreightPaid')?.checked === true,
            resetAutomaticAllocation: false,
            allocations: lines.map(function (line, index) {
                const row = getCommercialRows()[index];
                return {
                    stockDocumentLineId: line.stockDocumentLineId,
                    amount: hasFreight
                        ? roundMoney(readNumber(row.querySelector('.commercial-freight-allocation')))
                        : 0
                };
            })
        };
    }

    function validateCommercialApproval(focusInvalid) {
        clearCommercialError();
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

        const paid = document.getElementById('commercialMerchandisePaid')?.checked === true;
        const supplierId = nullablePositiveInt(document.getElementById('commercialSupplierId')?.value);
        const payee = valueOrNull('commercialPayeeName');
        if (!paid && !supplierId) {
            return fail('Tiền hàng còn nợ: vui lòng chọn nhà cung cấp.', document.getElementById('commercialSupplierId'), focusInvalid);
        }
        if (paid && !supplierId && !payee) {
            return fail('Đã trả ngay nhưng chưa chọn nhà cung cấp: vui lòng nhập người hoặc đơn vị nhận tiền.', document.getElementById('commercialPayeeName'), focusInvalid);
        }

        const hasFreight = document.getElementById('commercialHasFreight')?.checked === true;
        if (hasFreight) {
            const freightTotal = readNumber(document.getElementById('commercialFreightTotal'));
            const freightPayee = valueOrNull('commercialFreightPayee');
            if (!Number.isFinite(freightTotal) || freightTotal <= 0) {
                return fail('Đã chọn có phí vận chuyển: tổng phí phải lớn hơn 0.', document.getElementById('commercialFreightTotal'), focusInvalid);
            }
            if (!freightPayee) {
                return fail('Vui lòng nhập người hoặc đơn vị nhận tiền vận chuyển.', document.getElementById('commercialFreightPayee'), focusInvalid);
            }

            const allocated = roundMoney(rows.reduce(function (sum, row) {
                return sum + readNumber(row.querySelector('.commercial-freight-allocation'));
            }, 0));
            if (roundMoney(allocated - freightTotal) !== 0) {
                return fail('Tổng phí phân bổ phải bằng đúng tổng phí vận chuyển.', document.querySelector('.commercial-freight-allocation'), focusInvalid);
            }
        }

        return true;
    }

    function fail(message, element, focusInvalid) {
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
        const paid = document.getElementById('commercialMerchandisePaid')?.checked === true;
        const supplierId = nullablePositiveInt(document.getElementById('commercialSupplierId')?.value);
        const required = document.querySelector('.sd-supplier-required');
        const payee = document.getElementById('commercialPayeeName');
        if (required) required.classList.toggle('d-none', paid);
        if (payee) payee.placeholder = paid && !supplierId
            ? 'Bắt buộc: tên người/đơn vị nhận tiền'
            : 'Không bắt buộc khi đã chọn NCC';
    }

    function syncFreightState(resetWhenDisabled) {
        const readOnly = document.getElementById('commercialApprovalWorkbench')?.dataset.readonly === 'true';
        const enabled = document.getElementById('commercialHasFreight')?.checked === true;
        const ids = ['commercialFreightTotal', 'commercialFreightPayee', 'commercialFreightPaid', 'commercialFreightNote', 'commercialAutoAllocate'];
        ids.forEach(function (id) {
            const element = document.getElementById(id);
            if (element) element.disabled = readOnly || !enabled;
        });
        document.querySelectorAll('.commercial-freight-allocation').forEach(function (input) {
            input.disabled = readOnly || !enabled;
            if (!enabled && resetWhenDisabled) input.value = '0';
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
            const before = roundMoney(quantity * unitPrice);
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
        const freight = hasFreight ? roundMoney(readNumber(document.getElementById('commercialFreightTotal'))) : 0;
        setText(document.getElementById('commercialSubtotal'), formatMoney(subtotal));
        setText(document.getElementById('commercialVatTotal'), formatMoney(vatTotal));
        setText(document.getElementById('commercialMerchandiseTotal'), formatMoney(merchandiseTotal));
        setText(document.getElementById('commercialFreightSummary'), formatMoney(freight));
        setText(document.getElementById('commercialLandedTotal'), formatMoney(roundMoney(merchandiseTotal + freight)));
        setText(document.getElementById('commercialApprovalModalMerchandiseTotal'), formatMoney(merchandiseTotal));
        const headerTotal = document.getElementById('txtTotalAmount');
        if (headerTotal) headerTotal.value = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(merchandiseTotal);
        renderAllocationStatus(freight, hasFreight);
        clearCommercialError();
    }

    function renderPriceVariance(row, currentPrice) {
        const previousElement = row.querySelector('.commercial-last-price');
        const varianceElement = row.querySelector('.commercial-price-variance');
        const previous = Number(previousElement?.dataset.value || 0);
        varianceElement.classList.remove('is-up', 'is-down');
        if (!(previous > 0) || !(currentPrice > 0)) {
            varianceElement.textContent = '';
            return;
        }

        const percentage = ((currentPrice - previous) / previous) * 100;
        const sign = percentage > 0 ? '+' : '';
        varianceElement.textContent = `${sign}${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 }).format(percentage)}%`;
        if (percentage > 0) varianceElement.classList.add('is-up');
        if (percentage < 0) varianceElement.classList.add('is-down');
    }

    function autoAllocateFreight() {
        const rows = getCommercialRows();
        const hasFreight = document.getElementById('commercialHasFreight')?.checked === true;
        const total = hasFreight ? roundMoney(readNumber(document.getElementById('commercialFreightTotal'))) : 0;
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

    function renderAllocationStatus(freightTotal, hasFreight) {
        const status = document.getElementById('commercialAllocationStatus');
        if (!status) return;
        if (!hasFreight) {
            status.innerHTML = '<span class="sd-allocation-pill">Không có phí cần phân bổ</span>';
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
            summary.innerHTML = '<strong>Thao tác nguyên tử:</strong> hệ thống sẽ chốt giá/VAT/phí, cộng tồn kho, tạo lớp FIFO và ghi công nợ đúng một lần.';
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

        input.dataset.saving = 'true';
        input.disabled = true;
        try {
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
            input.dataset.saving = 'false';
            input.disabled = false;
        }
    }

    function readNumber(element) {
        if (!element) return 0;
        const value = Number(String(element.value ?? '').replace(',', '.'));
        return Number.isFinite(value) ? value : 0;
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
