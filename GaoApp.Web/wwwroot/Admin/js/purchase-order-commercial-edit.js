(function () {
    'use strict';
    const page = document.getElementById('purchaseOrderCommercialPage');
    const form = document.getElementById('commercialOrderForm');
    if (!page || !form) return;

    const supplier = document.getElementById('SupplierId');
    const legalEntity = document.getElementById('LegalEntityId');
    const warehouse = document.getElementById('ExpectedWarehouseId');
    const orderDate = document.getElementById('OrderDate');
    const deliveryDate = document.getElementById('ExpectedDeliveryDate');
    const saveButton = document.getElementById('saveCommercial');
    let submitting = false;

    if (window.jQuery && jQuery.fn.select2) {
        jQuery(supplier).select2({
            theme: 'bootstrap-5', width: '100%', allowClear: true,
            placeholder: 'Gõ để tìm nhà cung cấp...', minimumInputLength: 1,
            ajax: { url: page.dataset.supplierLookupUrl, dataType: 'json', delay: 250, cache: true,
                data: params => ({ term: params.term || '', page: params.page || 1 }), processResults: data => data },
            templateResult: item => {
                if (item.loading) return item.text;
                const wrapper = document.createElement('div');
                const name = document.createElement('strong'); name.textContent = item.text || '';
                const meta = document.createElement('small'); meta.className = 'd-block text-muted'; meta.textContent = [item.code, item.phone, item.taxCode].filter(Boolean).join(' · ');
                wrapper.append(name, meta); return wrapper;
            },
            templateSelection: item => item.text || '',
            language: { inputTooShort: () => 'Nhập ít nhất 1 ký tự', searching: () => 'Đang tìm...', noResults: () => 'Không tìm thấy nhà cung cấp' }
        });
    }

    function filterWarehouses(selectDefault) {
        let first = '';
        Array.from(warehouse.options).forEach(option => {
            if (!option.value) return;
            const allowed = option.dataset.legal === legalEntity.value;
            option.hidden = !allowed; option.disabled = !allowed;
            if (allowed && !first) first = option.value;
        });
        if (warehouse.selectedOptions[0]?.disabled || (selectDefault && !warehouse.value)) warehouse.value = first;
    }

    function validate() {
        const required = [legalEntity, warehouse, orderDate];
        const invalid = required.find(element => !String(element.value || '').trim());
        if (invalid) { window.alert('Hãy nhập đầy đủ đơn vị mua hàng, kho và ngày đặt.'); invalid.focus(); return false; }
        if (orderDate.value && deliveryDate.value && deliveryDate.value < orderDate.value) {
            window.alert('Ngày dự kiến giao không được trước ngày đặt.'); deliveryDate.focus(); return false;
        }
        return true;
    }

    filterWarehouses(false);
    legalEntity.addEventListener('change', () => filterWarehouses(true));
    form.addEventListener('submit', event => {
        if (submitting || !validate()) { event.preventDefault(); return; }
        submitting = true; saveButton.disabled = true;
        saveButton.innerHTML = '<i class="bx bx-loader-alt bx-spin me-1"></i>Đang lưu...';
    });
    document.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && (event.key.toLowerCase() === 's' || event.key === 'Enter')) {
            event.preventDefault(); form.requestSubmit(saveButton);
        }
    });
})();
