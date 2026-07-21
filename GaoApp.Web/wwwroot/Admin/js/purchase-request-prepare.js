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
    const submitButton = document.getElementById('convertSubmitButton');
    let submitting = false;

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
            theme: 'bootstrap-5', width: '100%', allowClear: true,
            placeholder: 'Gõ để tìm nhà cung cấp...', minimumInputLength: 1,
            ajax: {
                url: page.dataset.supplierLookupUrl, dataType: 'json', delay: 280, cache: true,
                data: params => ({ term: params.term || '', page: params.page || 1 }),
                processResults: data => data
            },
            templateResult: formatSupplierResult,
            templateSelection: item => item.text || '',
            language: {
                inputTooShort: () => 'Nhập ít nhất 1 ký tự để tìm nhà cung cấp',
                searching: () => 'Đang tìm...', noResults: () => 'Không tìm thấy nhà cung cấp phù hợp',
                errorLoading: () => 'Không tải được danh sách nhà cung cấp'
            }
        });
        $supplier.on('select2:select', event => {
            supplierLabel.value = (event.params.data?.text || '').trim();
            updateSupplierWarnings();
            clearInvalidSupplier();
        });
        $supplier.on('select2:clear', () => { supplierLabel.value = ''; updateSupplierWarnings(); });
    }

    function clearInvalidSupplier() {
        supplier.classList.remove('prc-invalid');
        supplier.nextElementSibling?.classList.remove('prc-invalid');
    }

    function updateSupplierWarnings() {
        const selectedId = supplier.value || '';
        document.querySelectorAll('.prc-line').forEach(row => {
            const warning = row.querySelector('.prc-supplier-mismatch');
            if (warning) warning.classList.toggle('d-none', !selectedId || selectedId === row.dataset.suggestedSupplierId);
        });
    }

    function filterWarehouses(selectDefault) {
        const legalId = legalEntity.value || '';
        let firstAllowed = '';
        Array.from(warehouse.options).forEach(option => {
            if (!option.value) return;
            const allowed = option.dataset.legalEntityId === legalId;
            option.hidden = !allowed;
            option.disabled = !allowed;
            if (allowed && !firstAllowed) firstAllowed = option.value;
        });
        if (warehouse.selectedOptions[0]?.disabled || (selectDefault && !warehouse.value)) warehouse.value = firstAllowed;
    }

    function clearValidation() {
        form.querySelectorAll('.prc-invalid').forEach(element => element.classList.remove('prc-invalid'));
    }

    function markInvalid(element, errors, message) {
        element?.classList.add('prc-invalid');
        errors.push({ element, message });
    }

    function validateForm() {
        clearValidation();
        const errors = [];
        if (!supplier.value) markInvalid(supplier.nextElementSibling || supplier, errors, 'Vui lòng chọn nhà cung cấp cho đơn đặt hàng.');
        if (!legalEntity.value) markInvalid(legalEntity, errors, 'Vui lòng chọn LegalEntity.');
        if (!warehouse.value) markInvalid(warehouse, errors, 'Vui lòng chọn kho dự kiến nhận.');
        if (!orderDate.value) markInvalid(orderDate, errors, 'Vui lòng nhập ngày đặt hàng.');
        if (orderDate.value && deliveryDate.value && deliveryDate.value < orderDate.value)
            markInvalid(deliveryDate, errors, 'Ngày dự kiến giao không được trước ngày đặt hàng.');
        if (!errors.length) return true;
        showMessage(errors[0].message, 'danger');
        errors[0].element?.scrollIntoView?.({ behavior: 'smooth', block: 'center' });
        return false;
    }

    function showMessage(message, type) {
        document.querySelector('.prc-toast')?.remove();
        const toast = document.createElement('div');
        toast.className = 'prc-toast is-' + (type || 'info');
        const icon = document.createElement('i');
        icon.className = 'bx ' + (type === 'danger' ? 'bx-x-circle' : 'bx-info-circle');
        const text = document.createElement('span');
        text.textContent = message;
        toast.append(icon, text);
        document.body.appendChild(toast);
        requestAnimationFrame(() => toast.classList.add('is-visible'));
        window.setTimeout(() => { toast.classList.remove('is-visible'); window.setTimeout(() => toast.remove(), 200); }, 3600);
    }

    initializeSupplierLookup();
    filterWarehouses(false);
    updateSupplierWarnings();
    legalEntity.addEventListener('change', () => filterWarehouses(true));
    form.addEventListener('submit', event => {
        if (submitting || !validateForm()) { event.preventDefault(); return; }
        submitting = true;
        submitButton.disabled = true;
        submitButton.querySelector('span').textContent = 'Đang tạo đơn...';
        submitButton.querySelector('i').className = 'bx bx-loader-alt bx-spin me-1';
    });
    document.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
            event.preventDefault();
            form.requestSubmit(submitButton);
        }
    });
})();
