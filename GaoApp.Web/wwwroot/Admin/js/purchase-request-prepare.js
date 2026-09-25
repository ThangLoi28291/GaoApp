(function () {
    'use strict';

    const page = document.getElementById('purchaseRequestPreparePage');
    if (!page) return;

    const form = document.getElementById('purchaseRequestConvertForm');
    const supplier = document.getElementById('supplierSelect');
    const supplierLabel = document.getElementById('supplierLabel');
    const legalEntity = document.getElementById('legalEntitySelect');
    const warehouse = document.getElementById('warehouseSelect');
    const orderDate = document.getElementById('orderDate');
    const deliveryDate = document.getElementById('deliveryDate');
    const orderNote = document.getElementById('orderNote');
    const orderNoteCount = document.getElementById('orderNoteCount');
    const submitButton = document.getElementById('convertSubmitButton');
    const confirmButton = document.getElementById('confirmCreateOrderButton');
    const confirmModalElement = document.getElementById('purchaseOrderConfirmModal');
    const helpModalElement = document.getElementById('purchaseOrderHelpModal');
    const confirmModal = confirmModalElement && window.bootstrap
        ? window.bootstrap.Modal.getOrCreateInstance(confirmModalElement)
        : null;
    const helpModal = helpModalElement && window.bootstrap
        ? window.bootstrap.Modal.getOrCreateInstance(helpModalElement)
        : null;
    let confirmed = false;
    let submitting = false;
    let mismatchCount = 0;

    function formatSupplierResult(item) {
        if (item.loading) return item.text;
        const wrapper = document.createElement('div');
        wrapper.className = 'prc-supplier-result';
        const title = document.createElement('strong');
        title.textContent = item.text || '';
        wrapper.appendChild(title);
        const metadata = [item.code, item.phone, item.taxCode].filter(Boolean).join(' · ');
        if (metadata) {
            const detail = document.createElement('small');
            detail.textContent = metadata;
            wrapper.appendChild(detail);
        }
        return window.jQuery(wrapper);
    }

    function initializeSupplierLookup() {
        if (!window.jQuery || !window.jQuery.fn.select2) return;
        const $supplier = window.jQuery(supplier);
        $supplier.select2({
            theme: 'bootstrap-5',
            width: '100%',
            allowClear: true,
            placeholder: 'Tìm nhà cung cấp...',
            minimumInputLength: 1,
            ajax: {
                url: page.dataset.supplierLookupUrl,
                dataType: 'json',
                delay: 280,
                cache: true,
                data: function (params) { return { term: params.term || '', page: params.page || 1 }; },
                processResults: function (data) { return data; }
            },
            templateResult: formatSupplierResult,
            templateSelection: function (item) { return item.text || ''; },
            language: {
                inputTooShort: function () { return 'Nhập ít nhất 1 ký tự để tìm nhà cung cấp'; },
                searching: function () { return 'Đang tìm...'; },
                noResults: function () { return 'Không tìm thấy nhà cung cấp phù hợp'; },
                errorLoading: function () { return 'Không tải được danh sách nhà cung cấp'; }
            }
        });

        $supplier.on('select2:select', function (event) {
            supplierLabel.value = String(event.params.data && event.params.data.text || '').trim();
            updateSupplierWarnings();
            clearInvalidSupplier();
        });
        $supplier.on('select2:clear', function () {
            supplierLabel.value = '';
            updateSupplierWarnings();
        });
    }

    function clearInvalidSupplier() {
        supplier.classList.remove('prc-invalid');
        const select2 = supplier.nextElementSibling;
        if (select2) select2.classList.remove('prc-invalid');
    }

    function updateSupplierWarnings() {
        const selectedId = supplier.value || '';
        mismatchCount = 0;
        let configuredCount = 0;
        document.querySelectorAll('.prc-line').forEach(function (row) {
            const suggestedId = row.dataset.suggestedSupplierId || '';
            const warning = row.querySelector('.prc-supplier-mismatch');
            const hasConfiguredSupplier = Boolean(suggestedId);
            const mismatch = Boolean(selectedId && hasConfiguredSupplier && selectedId !== suggestedId);
            if (hasConfiguredSupplier) configuredCount++;
            if (mismatch) mismatchCount++;
            row.classList.toggle('is-supplier-mismatch', mismatch);
            if (warning) warning.classList.toggle('d-none', !mismatch);
        });

        const summary = document.getElementById('supplierMatchSummary');
        if (!summary) return;
        summary.classList.toggle('is-warning', mismatchCount > 0);
        if (!selectedId) {
            summary.innerHTML = '<i class="bx bx-store"></i><span>Chưa chọn nhà cung cấp</span>';
        } else if (mismatchCount > 0) {
            summary.innerHTML = '<i class="bx bx-error-circle"></i><span>' + mismatchCount + ' mặt hàng khác nhà cung cấp đã chọn</span>';
        } else if (configuredCount > 0) {
            summary.innerHTML = '<i class="bx bx-check-circle"></i><span>Phù hợp nhà cung cấp cấu hình</span>';
        } else {
            summary.innerHTML = '<i class="bx bx-info-circle"></i><span>Không có cấu hình để đối chiếu</span>';
        }
    }

    function selectSingleOption(select) {
        const enabledOptions = Array.from(select.options).filter(function (option) {
            return option.value && !option.disabled && !option.hidden;
        });
        if (!select.value && enabledOptions.length === 1) select.value = enabledOptions[0].value;
    }

    function filterWarehouses(selectDefault) {
        const legalId = legalEntity.value || '';
        let firstAllowed = '';
        Array.from(warehouse.options).forEach(function (option) {
            if (!option.value) return;
            const allowed = option.dataset.legalEntityId === legalId;
            option.hidden = !allowed;
            option.disabled = !allowed;
            if (allowed && !firstAllowed) firstAllowed = option.value;
        });

        if (warehouse.selectedOptions[0] && warehouse.selectedOptions[0].disabled) warehouse.value = '';
        if (selectDefault && !warehouse.value) warehouse.value = firstAllowed;
        selectSingleOption(warehouse);
    }

    function clearValidation() {
        form.querySelectorAll('.prc-invalid').forEach(function (element) { element.classList.remove('prc-invalid'); });
    }

    function markInvalid(element, errors, message) {
        if (element) element.classList.add('prc-invalid');
        errors.push({ element: element, message: message });
    }

    function validateForm() {
        clearValidation();
        const errors = [];
        if (!legalEntity.value) markInvalid(legalEntity, errors, 'Vui lòng chọn đơn vị pháp lý.');
        if (!warehouse.value) markInvalid(warehouse, errors, 'Vui lòng chọn kho nhận hàng.');
        if (!orderDate.value) markInvalid(orderDate, errors, 'Vui lòng nhập ngày đặt hàng.');
        if (orderDate.value && deliveryDate.value && deliveryDate.value < orderDate.value) {
            markInvalid(deliveryDate, errors, 'Ngày dự kiến giao không được trước ngày đặt hàng.');
        }

        if (!errors.length) return true;
        showMessage(errors[0].message, 'danger');
        if (errors[0].element && errors[0].element.scrollIntoView) {
            errors[0].element.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
        return false;
    }

    function selectedText(select) {
        const option = select.selectedOptions && select.selectedOptions[0];
        return option ? option.textContent.trim() : '';
    }

    function formatDate(value) {
        if (!value) return 'Chưa xác định';
        const parts = value.split('-');
        return parts.length === 3 ? parts[2] + '/' + parts[1] + '/' + parts[0] : value;
    }

    function populateConfirmation() {
        setText('confirmSupplier', supplierLabel.value || selectedText(supplier) || 'Quản lý chọn khi duyệt');
        setText('confirmLegalEntity', selectedText(legalEntity) || '—');
        setText('confirmWarehouse', selectedText(warehouse) || '—');
        setText('confirmDeliveryDate', formatDate(deliveryDate.value));

        const warning = document.getElementById('confirmSupplierWarning');
        if (!warning) return;
        warning.classList.toggle('d-none', mismatchCount === 0);
        const message = warning.querySelector('span');
        if (message) message.textContent = mismatchCount + ' mặt hàng đang được cấu hình với nhà cung cấp khác. Bạn vẫn có thể tạo đơn này.';
    }

    function showMessage(message, type) {
        const oldToast = document.querySelector('.prc-toast');
        if (oldToast) oldToast.remove();
        const toast = document.createElement('div');
        toast.className = 'prc-toast is-' + (type || 'info');
        const icon = document.createElement('i');
        icon.className = 'bx ' + (type === 'danger' ? 'bx-x-circle' : 'bx-info-circle');
        const text = document.createElement('span');
        text.textContent = message;
        toast.append(icon, text);
        document.body.appendChild(toast);
        window.requestAnimationFrame(function () { toast.classList.add('is-visible'); });
        window.setTimeout(function () {
            toast.classList.remove('is-visible');
            window.setTimeout(function () { toast.remove(); }, 200);
        }, 3600);
    }

    function setText(id, value) {
        const element = document.getElementById(id);
        if (element) element.textContent = value;
    }

    function beginSubmit() {
        submitting = true;
        submitButton.disabled = true;
        if (confirmButton) confirmButton.disabled = true;
        const label = submitButton.querySelector('span');
        const icon = submitButton.querySelector('i');
        if (label) label.textContent = 'Đang tạo đơn...';
        if (icon) icon.className = 'bx bx-loader-alt bx-spin me-1';
    }

    initializeSupplierLookup();
    selectSingleOption(legalEntity);
    filterWarehouses(false);
    updateSupplierWarnings();
    if (orderNoteCount && orderNote) orderNoteCount.textContent = String(orderNote.value.length);

    legalEntity.addEventListener('change', function () {
        filterWarehouses(true);
        legalEntity.classList.remove('prc-invalid');
        warehouse.classList.remove('prc-invalid');
    });
    warehouse.addEventListener('change', function () { warehouse.classList.remove('prc-invalid'); });
    orderDate.addEventListener('change', function () { orderDate.classList.remove('prc-invalid'); });
    deliveryDate.addEventListener('change', function () { deliveryDate.classList.remove('prc-invalid'); });
    if (orderNote) {
        orderNote.addEventListener('input', function () {
            if (orderNoteCount) orderNoteCount.textContent = String(orderNote.value.length);
        });
    }

    form.addEventListener('submit', function (event) {
        if (submitting) {
            event.preventDefault();
            return;
        }
        if (confirmed) {
            beginSubmit();
            return;
        }

        event.preventDefault();
        if (!validateForm()) return;
        populateConfirmation();
        if (confirmModal) confirmModal.show();
    });

    if (confirmButton) {
        confirmButton.addEventListener('click', function () {
            if (!validateForm()) {
                if (confirmModal) confirmModal.hide();
                return;
            }
            confirmed = true;
            if (confirmModal) confirmModal.hide();
            form.requestSubmit(submitButton);
        });
    }

    document.addEventListener('keydown', function (event) {
        if (event.key === 'F1') {
            event.preventDefault();
            if (helpModal) helpModal.show();
            return;
        }
        if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
            event.preventDefault();
            form.requestSubmit(submitButton);
        }
    });
})();
