(function () {
    'use strict';

    const page = document.getElementById('purchaseOrderPage');
    const form = document.getElementById('purchaseOrderForm');
    if (!page || !form) return;

    const ITEM_CATALOG = 1;
    const ITEM_FREE_TEXT = 2;
    const rows = document.getElementById('lineRows');
    const lineTemplate = document.getElementById('lineTemplate');
    const supplier = document.getElementById('SupplierId');
    const legalEntity = document.getElementById('LegalEntityId');
    const warehouse = document.getElementById('ExpectedWarehouseId');
    const productModalElement = document.getElementById('productDetailModal');
    const freeTextModalElement = document.getElementById('freeTextProductModal');
    const productModal = window.bootstrap ? new bootstrap.Modal(productModalElement) : null;
    const freeTextModal = window.bootstrap ? new bootstrap.Modal(freeTextModalElement) : null;
    const quantityFormatter = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });
    let activeProduct = null;
    let submitting = false;
    let dirty = false;

    init();

    function init() {
        initSupplierLookup();
        initProductLookup();
        rows.querySelectorAll('.purchase-line').forEach(bindLine);
        filterWarehouses(false);
        bindEvents();
        renumber();
        updateSummary();
        updateEmptyState();
    }

    function initSupplierLookup() {
        if (!window.jQuery || !jQuery.fn.select2) return;
        jQuery(supplier).select2({
            theme: 'bootstrap-5', width: '100%', allowClear: true, placeholder: 'Gõ để tìm nhà cung cấp...', minimumInputLength: 1,
            ajax: { url: page.dataset.supplierLookupUrl, dataType: 'json', delay: 250, cache: true,
                data: params => ({ term: params.term || '', page: params.page || 1 }), processResults: data => data },
            templateResult: renderSupplier,
            templateSelection: item => item.text || '',
            language: { inputTooShort: () => 'Nhập ít nhất 1 ký tự', searching: () => 'Đang tìm...', noResults: () => 'Không tìm thấy nhà cung cấp' }
        });
        jQuery(supplier).on('select2:select select2:clear', () => { dirty = true; });
    }

    function renderSupplier(item) {
        if (item.loading) return item.text;
        const wrapper = document.createElement('div');
        const name = document.createElement('strong');
        name.textContent = item.text || '';
        const meta = document.createElement('small');
        meta.className = 'd-block text-muted';
        meta.textContent = [item.code, item.phone, item.taxCode].filter(Boolean).join(' · ');
        wrapper.append(name, meta);
        return wrapper;
    }

    function initProductLookup() {
        if (!window.jQuery || !jQuery.fn.select2) return;
        const element = jQuery('#productQuickSearch');
        element.select2({
            theme: 'bootstrap-5', width: '100%', placeholder: 'Gõ tên, SKU hoặc quét barcode...', allowClear: true, minimumInputLength: 1,
            ajax: { url: page.dataset.productLookupUrl, dataType: 'json', delay: 220, cache: true,
                data: params => ({ term: params.term || '', supplierId: supplier.value || null, page: params.page || 1 }), processResults: data => data },
            templateResult: renderProduct,
            templateSelection: item => item.text || '',
            language: { inputTooShort: () => 'Nhập ít nhất 1 ký tự', searching: () => 'Đang tìm sản phẩm...', noResults: () => 'Không thấy. Nhấn F4 để nhập tên mặt hàng mới.' },
            escapeMarkup: markup => markup
        });
        element.on('select2:select', event => {
            activeProduct = normalizeProduct(event.params.data);
            fillProductModal(activeProduct);
            productModal?.show();
            window.setTimeout(() => element.val(null).trigger('change'), 0);
        });
    }

    function renderProduct(item) {
        if (item.loading) return item.text;
        const wrapper = document.createElement('div');
        wrapper.className = 'd-flex justify-content-between gap-3';
        const body = document.createElement('div');
        const name = document.createElement('strong');
        name.className = 'd-block';
        name.textContent = item.productName || item.text || '';
        const meta = document.createElement('small');
        meta.className = 'text-muted';
        meta.textContent = [item.sku ? 'SKU: ' + item.sku : '', item.barcode ? 'Barcode: ' + item.barcode : '', item.supplierName ? 'NCC: ' + item.supplierName : ''].filter(Boolean).join(' · ');
        body.append(name, meta);
        const unit = document.createElement('span');
        unit.className = 'badge bg-label-secondary align-self-center';
        unit.textContent = (item.unitName || 'Đơn vị') + ' ×' + formatQuantity(item.factor || 1);
        wrapper.append(body, unit);
        return wrapper;
    }

    function bindEvents() {
        legalEntity.addEventListener('change', () => { filterWarehouses(true); dirty = true; });
        form.querySelectorAll('input, select, textarea').forEach(element => element.addEventListener('change', () => { dirty = true; }));
        document.getElementById('emptyAddProduct').addEventListener('click', focusProductSearch);
        document.getElementById('openFreeTextProduct').addEventListener('click', openFreeTextModal);
        document.getElementById('saveProductAndContinue').addEventListener('click', () => saveCatalog(true));
        document.getElementById('saveProductLine').addEventListener('click', () => saveCatalog(false));
        document.getElementById('saveFreeTextAndContinue').addEventListener('click', () => saveFreeText(true));
        document.getElementById('saveFreeTextLine').addEventListener('click', () => saveFreeText(false));
        document.getElementById('freeTextUnitName').addEventListener('input', handleOtherUnit);
        productModalElement.addEventListener('shown.bs.modal', () => selectInput('modalQuantity'));
        productModalElement.addEventListener('hidden.bs.modal', () => { activeProduct = null; clearError('modalProductError'); });
        productModalElement.addEventListener('keydown', event => handleModalEnter(event, saveCatalog));
        freeTextModalElement.addEventListener('shown.bs.modal', () => selectInput('freeTextProductName'));
        freeTextModalElement.addEventListener('hidden.bs.modal', resetFreeTextModal);
        freeTextModalElement.addEventListener('keydown', event => handleModalEnter(event, saveFreeText));
        document.addEventListener('keydown', handleShortcut);
        form.addEventListener('submit', validateAndSubmit);
        window.addEventListener('beforeunload', event => { if (dirty && !submitting) { event.preventDefault(); event.returnValue = ''; } });
    }

    function handleShortcut(event) {
        const modalOpen = document.querySelector('.modal.show');
        if (event.key === 'F3' && !event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault(); focusProductSearch();
        } else if (event.key === 'F4' && !event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault(); openFreeTextModal();
        } else if (event.key.toLowerCase() === 's' && event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault(); form.requestSubmit(document.getElementById('savePurchaseOrder'));
        }
    }

    function handleModalEnter(event, handler) {
        if (event.key !== 'Enter' || event.altKey || event.metaKey || event.target?.tagName === 'BUTTON') return;
        event.preventDefault();
        handler(Boolean(event.ctrlKey));
    }

    function fillProductModal(item) {
        document.getElementById('modalProductName').textContent = item.productName;
        document.getElementById('modalProductMeta').textContent = [item.sku ? 'SKU: ' + item.sku : '', item.barcode ? 'Barcode: ' + item.barcode : '', item.supplierName ? 'NCC: ' + item.supplierName : ''].filter(Boolean).join(' · ') || 'Không có mã sản phẩm';
        document.getElementById('modalUnitName').textContent = item.unitName;
        document.getElementById('modalFactor').textContent = formatQuantity(item.factor);
        document.getElementById('modalQuantity').value = '1';
        clearError('modalProductError');
    }

    function saveCatalog(continueAdding) {
        if (!activeProduct) return;
        const quantity = parsePositive(document.getElementById('modalQuantity').value);
        if (quantity === null) { showError('modalProductError', 'Số lượng phải lớn hơn 0.'); selectInput('modalQuantity'); return; }
        addOrMerge(activeProduct, quantity);
        productModal?.hide();
        if (continueAdding) window.setTimeout(focusProductSearch, 180);
    }

    function openFreeTextModal() { resetFreeTextModal(); freeTextModal?.show(); }
    function handleOtherUnit(event) {
        if (event.target.value.trim().toLocaleLowerCase('vi-VN') === 'đơn vị khác') {
            event.target.value = ''; event.target.placeholder = 'Nhập tên đơn vị khác...';
        }
    }

    function saveFreeText(continueAdding) {
        const nameInput = document.getElementById('freeTextProductName');
        const unitInput = document.getElementById('freeTextUnitName');
        const quantityInput = document.getElementById('freeTextQuantity');
        const productName = nameInput.value.trim();
        const unitName = unitInput.value.trim();
        const quantity = parsePositive(quantityInput.value);
        if (!productName) { showError('freeTextProductError', 'Hãy nhập tên mặt hàng.'); nameInput.focus(); return; }
        if (!unitName || unitName.toLocaleLowerCase('vi-VN') === 'đơn vị khác') { showError('freeTextProductError', 'Hãy nhập đơn vị mua cụ thể.'); unitInput.focus(); return; }
        if (quantity === null) { showError('freeTextProductError', 'Số lượng phải lớn hơn 0.'); quantityInput.focus(); return; }
        addOrMerge({ itemKind: ITEM_FREE_TEXT, productName, unitName, factor: 1 }, quantity);
        if (continueAdding) {
            nameInput.value = ''; unitInput.value = ''; quantityInput.value = '1'; clearError('freeTextProductError'); nameInput.focus();
        } else freeTextModal?.hide();
    }

    function resetFreeTextModal() {
        document.getElementById('freeTextProductName').value = '';
        document.getElementById('freeTextUnitName').value = '';
        document.getElementById('freeTextUnitName').placeholder = 'Gõ hoặc chọn đơn vị...';
        document.getElementById('freeTextQuantity').value = '1';
        clearError('freeTextProductError');
    }

    function addOrMerge(item, quantity) {
        const free = item.itemKind === ITEM_FREE_TEXT;
        const key = free ? 'free:' + normalizeKey(item.productName) + '|' + normalizeKey(item.unitName) : 'catalog:' + item.productUnitConversionId;
        const existing = Array.from(rows.querySelectorAll('.purchase-line')).find(row => row.dataset.lineKey === key);
        if (existing) {
            const input = existing.querySelector('.qty');
            input.value = decimalValue((parseFloat(input.value) || 0) + quantity);
            flash(existing);
        } else {
            const row = lineTemplate.content.firstElementChild.cloneNode(true);
            row.dataset.lineKey = key;
            row.querySelector('.line-id').value = '';
            row.querySelector('.item-kind').value = free ? ITEM_FREE_TEXT : ITEM_CATALOG;
            row.querySelector('.variant-id').value = free ? '' : item.productVariantId;
            row.querySelector('.conversion-id').value = free ? '' : item.productUnitConversionId;
            row.querySelector('.product-name-input').value = item.productName;
            row.querySelector('.unit-name-input').value = item.unitName;
            row.querySelector('.po-product-name').textContent = item.productName;
            row.querySelector('.po-product-meta').textContent = free ? '' : (item.sku ? 'SKU: ' + item.sku : '');
            row.querySelector('.po-free-text').classList.toggle('d-none', !free);
            row.querySelector('.po-unit-name').textContent = item.unitName;
            row.querySelector('.po-factor').textContent = free ? '' : '× ' + formatQuantity(item.factor);
            row.querySelector('.qty').value = decimalValue(quantity);
            rows.appendChild(row);
            bindLine(row);
            flash(row);
        }
        dirty = true;
        renumber(); updateSummary(); updateEmptyState(); hideLineError();
    }

    function bindLine(row) {
        row.querySelector('.remove-line').addEventListener('click', () => { row.remove(); dirty = true; renumber(); updateSummary(); updateEmptyState(); });
        row.querySelector('.qty').addEventListener('input', () => { dirty = true; updateSummary(); });
    }

    function renumber() {
        rows.querySelectorAll('.purchase-line').forEach((row, index) => {
            row.querySelector('.po-line-number').textContent = String(index + 1);
            const prefix = 'Lines[' + index + '].';
            row.querySelector('.line-id').name = prefix + 'Id';
            row.querySelector('.item-kind').name = prefix + 'ItemKind';
            row.querySelector('.variant-id').name = prefix + 'ProductVariantId';
            row.querySelector('.conversion-id').name = prefix + 'ProductUnitConversionId';
            row.querySelector('.product-name-input').name = prefix + 'ProductName';
            row.querySelector('.unit-name-input').name = prefix + 'UnitName';
            row.querySelector('.tax-id').name = prefix + 'TaxId';
            row.querySelector('.unit-price').name = prefix + 'UnitPriceBeforeVat';
            row.querySelector('.qty').name = prefix + 'Quantity';
        });
    }

    function updateSummary() {
        const list = Array.from(rows.querySelectorAll('.purchase-line'));
        const total = list.reduce((sum, row) => sum + (parseFloat(row.querySelector('.qty').value) || 0), 0);
        document.getElementById('lineCount').textContent = String(list.length);
        document.getElementById('summaryLineCount').textContent = String(list.length);
        document.getElementById('summaryQuantity').textContent = formatQuantity(total);
    }

    function updateEmptyState() {
        const empty = !rows.querySelector('.purchase-line');
        document.getElementById('emptyLineState').classList.toggle('d-none', !empty);
        document.getElementById('purchaseLineTable').classList.toggle('d-none', empty);
    }

    function filterWarehouses(selectDefault) {
        const legalId = legalEntity.value;
        let first = '';
        Array.from(warehouse.options).forEach(option => {
            if (!option.value) return;
            const allowed = option.dataset.legal === legalId;
            option.hidden = !allowed; option.disabled = !allowed;
            if (allowed && !first) first = option.value;
        });
        if (warehouse.selectedOptions[0]?.disabled || (selectDefault && !warehouse.value)) warehouse.value = first;
    }

    function focusProductSearch() {
        if (window.jQuery && jQuery.fn.select2) {
            jQuery('#productQuickSearch').select2('open');
            window.setTimeout(() => document.querySelector('.select2-container--open .select2-search__field')?.focus(), 20);
        } else document.getElementById('productQuickSearch').focus();
    }

    function validateAndSubmit(event) {
        if (submitting) { event.preventDefault(); return; }
        const required = [supplier, legalEntity, warehouse, document.getElementById('OrderDate'), document.getElementById('OutsideRequestReason')];
        const invalidHeader = required.find(element => !String(element.value || '').trim());
        if (invalidHeader) { event.preventDefault(); invalidHeader.focus(); showLineError('Hãy nhập đầy đủ nhà cung cấp, đơn vị mua, kho, ngày đặt và lý do lập ngoài yêu cầu.'); return; }
        const orderDate = document.getElementById('OrderDate').value;
        const deliveryDate = document.getElementById('ExpectedDeliveryDate').value;
        if (orderDate && deliveryDate && deliveryDate < orderDate) { event.preventDefault(); document.getElementById('ExpectedDeliveryDate').focus(); showLineError('Ngày dự kiến giao không được trước ngày đặt.'); return; }
        const lineList = Array.from(rows.querySelectorAll('.purchase-line'));
        if (!lineList.length) { event.preventDefault(); showLineError('Hãy thêm ít nhất một mặt hàng vào đơn.'); focusProductSearch(); return; }
        const invalid = lineList.find(row => {
            const kind = Number(row.querySelector('.item-kind').value);
            const quantity = parseFloat(row.querySelector('.qty').value);
            if (!Number.isFinite(quantity) || quantity <= 0) return true;
            return kind === ITEM_FREE_TEXT
                ? !row.querySelector('.product-name-input').value.trim() || !row.querySelector('.unit-name-input').value.trim()
                : !row.querySelector('.variant-id').value || !row.querySelector('.conversion-id').value;
        });
        if (invalid) { event.preventDefault(); showLineError('Kiểm tra lại tên, đơn vị và số lượng của từng mặt hàng.'); invalid.querySelector('.qty').focus(); return; }
        renumber(); hideLineError(); submitting = true; dirty = false;
        const button = document.getElementById('savePurchaseOrder');
        button.disabled = true; button.querySelector('.spinner-border').classList.remove('d-none');
    }

    function normalizeProduct(item) {
        return { itemKind: ITEM_CATALOG, productName: item.productName || item.text || 'Sản phẩm', productVariantId: Number(item.productVariantId), productUnitConversionId: Number(item.productUnitConversionId || item.id), unitName: item.unitName || 'Đơn vị', factor: Number(item.factor || 1), sku: item.sku || '', barcode: item.barcode || '', supplierName: item.supplierName || '' };
    }
    function normalizeKey(value) { return String(value || '').trim().toLocaleLowerCase('vi-VN').replace(/\s+/g, ' '); }
    function parsePositive(value) { const parsed = Number(String(value).replace(',', '.')); return Number.isFinite(parsed) && parsed > 0 ? parsed : null; }
    function decimalValue(value) { return Number(value).toFixed(3).replace(/\.?0+$/, ''); }
    function formatQuantity(value) { return quantityFormatter.format(Number(value) || 0); }
    function selectInput(id) { const element = document.getElementById(id); element?.focus(); element?.select?.(); }
    function flash(row) { row.classList.add('table-primary'); window.setTimeout(() => row.classList.remove('table-primary'), 650); }
    function showError(id, message) { const box = document.getElementById(id); box.textContent = message; box.classList.remove('d-none'); }
    function clearError(id) { const box = document.getElementById(id); box.textContent = ''; box.classList.add('d-none'); }
    function showLineError(message) { const box = document.getElementById('lineValidationMessage'); box.querySelector('span').textContent = message; box.classList.remove('d-none'); }
    function hideLineError() { const box = document.getElementById('lineValidationMessage'); box.querySelector('span').textContent = ''; box.classList.add('d-none'); }
})();
