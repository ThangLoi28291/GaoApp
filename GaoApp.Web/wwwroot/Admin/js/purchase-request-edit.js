(function () {
    'use strict';

    const page = document.getElementById('purchaseRequestPage');
    const form = document.getElementById('purchaseRequestForm');
    if (!page || !form) return;

    const ITEM_CATALOG = 1;
    const ITEM_FREE_TEXT = 2;
    const rows = document.getElementById('lineRows');
    const lineTemplate = document.getElementById('lineTemplate');
    const productModalElement = document.getElementById('productDetailModal');
    const freeTextModalElement = document.getElementById('freeTextProductModal');
    const shortcutModalElement = document.getElementById('shortcutHelpModal');
    const productModal = window.bootstrap ? new bootstrap.Modal(productModalElement) : null;
    const freeTextModal = window.bootstrap ? new bootstrap.Modal(freeTextModalElement) : null;
    const shortcutModal = window.bootstrap ? new bootstrap.Modal(shortcutModalElement) : null;
    const numberFormatter = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });

    let activeProduct = null;
    let isSubmitting = false;
    let isDirty = false;

    init();

    function init() {
        initProductSelect();
        rows.querySelectorAll('.request-line').forEach(bindLine);
        bindEvents();
        renumberLines();
        updateCounters();
        updateTextCounters();
        updateEmptyState();
    }

    function initProductSelect() {
        if (!window.jQuery || !jQuery.fn.select2) return;
        const element = jQuery('#productSearch');
        element.select2({
            theme: 'bootstrap-5',
            width: '100%',
            dropdownParent: jQuery(document.body),
            placeholder: 'Gõ tên, SKU hoặc quét barcode...',
            allowClear: true,
            minimumInputLength: 1,
            ajax: {
                url: page.dataset.productLookupUrl,
                dataType: 'json',
                delay: 220,
                cache: true,
                data: params => ({ term: params.term || '', page: params.page || 1 }),
                processResults: data => data
            },
            templateResult: renderProductResult,
            templateSelection: item => item.text || '',
            language: {
                inputTooShort: () => 'Nhập ít nhất 1 ký tự để tìm',
                searching: () => 'Đang tìm sản phẩm...',
                noResults: () => 'Không tìm thấy. Nhấn F4 để nhập tên mặt hàng mới.',
                loadingMore: () => 'Đang tải thêm...'
            },
            escapeMarkup: markup => markup
        });
        element.on('select2:select', event => {
            activeProduct = normalizeProduct(event.params.data);
            fillProductModal(activeProduct);
            productModal?.show();
            window.setTimeout(() => element.val(null).trigger('change'), 0);
        });
    }

    function renderProductResult(item) {
        if (item.loading) return item.text;
        const wrapper = document.createElement('div');
        wrapper.className = 'pr-select-result';
        const icon = document.createElement('span');
        icon.className = 'pr-select-result-icon';
        if (item.imageUrl) {
            const image = document.createElement('img');
            image.src = item.imageUrl;
            image.alt = '';
            icon.appendChild(image);
        } else {
            const glyph = document.createElement('i');
            glyph.className = 'bx bx-package';
            icon.appendChild(glyph);
        }
        const text = document.createElement('div');
        text.className = 'min-w-0';
        const name = document.createElement('div');
        name.className = 'pr-select-result-name';
        name.textContent = item.text || 'Sản phẩm';
        const meta = document.createElement('div');
        meta.className = 'pr-select-result-meta';
        meta.textContent = [item.sku ? 'SKU: ' + item.sku : '', item.barcode ? 'Barcode: ' + item.barcode : ''].filter(Boolean).join(' · ') || 'Không có SKU/barcode';
        text.append(name, meta);
        if (item.hasOpenRequest) {
            const warning = document.createElement('div');
            warning.className = 'pr-select-result-warning';
            warning.textContent = 'Đã có trong yêu cầu đang xử lý';
            text.appendChild(warning);
        }
        const unit = document.createElement('span');
        unit.className = 'pr-select-result-unit';
        unit.textContent = (item.unitName || 'Đơn vị') + ' ×' + formatQuantity(item.factor || 1);
        wrapper.append(icon, text, unit);
        return wrapper;
    }

    function bindEvents() {
        document.getElementById('Title').addEventListener('input', markDirtyAndUpdateText);
        document.getElementById('Note').addEventListener('input', markDirtyAndUpdateText);
        document.getElementById('emptyAddProduct').addEventListener('click', focusProductSearch);
        document.getElementById('openFreeTextModal').addEventListener('click', openFreeTextModal);
        document.getElementById('saveProductAndContinue').addEventListener('click', () => saveCatalogProduct(true));
        document.getElementById('saveProductLine').addEventListener('click', () => saveCatalogProduct(false));
        document.getElementById('saveFreeTextAndContinue').addEventListener('click', () => saveFreeTextProduct(true));
        document.getElementById('saveFreeTextLine').addEventListener('click', () => saveFreeTextProduct(false));
        document.querySelectorAll('.pr-submit-button').forEach(button => button.addEventListener('click', () => submitWithIntent(button.dataset.intent)));

        productModalElement.addEventListener('shown.bs.modal', () => selectInput('modalQuantity'));
        productModalElement.addEventListener('hidden.bs.modal', () => { activeProduct = null; clearError('modalProductError'); });
        productModalElement.addEventListener('keydown', event => handleModalEnter(event, saveCatalogProduct));
        freeTextModalElement.addEventListener('shown.bs.modal', () => selectInput('freeTextProductName'));
        freeTextModalElement.addEventListener('hidden.bs.modal', resetFreeTextModal);
        freeTextModalElement.addEventListener('keydown', event => handleModalEnter(event, saveFreeTextProduct));
        document.getElementById('freeTextUnitName').addEventListener('input', handleOtherUnit);

        document.addEventListener('keydown', handleGlobalShortcut);
        form.addEventListener('submit', validateAndSubmit);
        window.addEventListener('beforeunload', event => {
            if (!isDirty || isSubmitting) return;
            event.preventDefault();
            event.returnValue = '';
        });
    }

    function markDirtyAndUpdateText() {
        isDirty = true;
        updateTextCounters();
    }

    function handleGlobalShortcut(event) {
        const modalOpen = document.querySelector('.modal.show');
        if (event.key === 'F2' && !event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault();
            selectInput('Title');
        } else if (event.key === 'F3' && !event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault();
            focusProductSearch();
        } else if (event.key === 'F4' && !event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault();
            openFreeTextModal();
        } else if ((event.key === '/' || event.code === 'Slash') && event.ctrlKey && !event.altKey && !event.metaKey) {
            event.preventDefault();
            if (!modalOpen) shortcutModal?.show();
        } else if (event.key.toLowerCase() === 's' && event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault();
            submitWithIntent('save');
        } else if (event.key === 'Enter' && event.altKey && !event.ctrlKey && !event.metaKey && !modalOpen) {
            const submitButton = document.querySelector('.pr-submit-button[data-intent="submit"]');
            if (submitButton) {
                event.preventDefault();
                submitWithIntent('submit');
            }
        }
    }

    function handleModalEnter(event, saveHandler) {
        if (event.key !== 'Enter' || event.altKey || event.metaKey || event.target?.tagName === 'BUTTON') return;
        event.preventDefault();
        saveHandler(Boolean(event.ctrlKey));
    }

    function fillProductModal(item) {
        document.getElementById('modalProductName').textContent = item.text;
        document.getElementById('modalProductMeta').textContent = [item.sku ? 'SKU: ' + item.sku : '', item.barcode ? 'Barcode: ' + item.barcode : ''].filter(Boolean).join(' · ') || 'Không có SKU/barcode';
        document.getElementById('modalUnitName').textContent = item.unitName;
        document.getElementById('modalFactor').textContent = formatQuantity(item.factor);
        document.getElementById('modalCurrentStock').textContent = formatQuantity(item.currentStockBaseQuantity) + ' ĐV gốc';
        document.getElementById('modalIncoming').textContent = formatQuantity(item.incomingBaseQuantity) + ' ĐV gốc';
        document.getElementById('modalOpenRequestWarning').classList.toggle('d-none', !item.hasOpenRequest);
        document.getElementById('modalQuantity').value = '1';
        clearError('modalProductError');
    }

    function saveCatalogProduct(continueAdding) {
        if (!activeProduct) return;
        const quantity = parsePositive(document.getElementById('modalQuantity').value);
        if (quantity === null) {
            showError('modalProductError', 'Số lượng phải lớn hơn 0.');
            selectInput('modalQuantity');
            return;
        }
        addOrMergeLine(activeProduct, quantity);
        productModal?.hide();
        if (continueAdding) window.setTimeout(focusProductSearch, 180);
    }

    function openFreeTextModal() {
        resetFreeTextModal();
        freeTextModal?.show();
    }

    function handleOtherUnit(event) {
        if (event.target.value.trim().toLocaleLowerCase('vi-VN') !== 'đơn vị khác') return;
        event.target.value = '';
        event.target.placeholder = 'Nhập tên đơn vị khác...';
    }

    function saveFreeTextProduct(continueAdding) {
        const nameInput = document.getElementById('freeTextProductName');
        const unitInput = document.getElementById('freeTextUnitName');
        const quantityInput = document.getElementById('freeTextQuantity');
        const productName = nameInput.value.trim();
        const unitName = unitInput.value.trim();
        const quantity = parsePositive(quantityInput.value);
        if (!productName) {
            showError('freeTextProductError', 'Hãy nhập tên mặt hàng cần mua.');
            nameInput.focus();
            return;
        }
        if (!unitName || unitName.toLocaleLowerCase('vi-VN') === 'đơn vị khác') {
            showError('freeTextProductError', 'Hãy chọn hoặc nhập đơn vị mua cụ thể.');
            unitInput.focus();
            return;
        }
        if (quantity === null) {
            showError('freeTextProductError', 'Số lượng phải lớn hơn 0.');
            quantityInput.focus();
            return;
        }
        addOrMergeLine({ itemKind: ITEM_FREE_TEXT, text: productName, unitName, factor: 1 }, quantity);
        if (continueAdding) {
            nameInput.value = '';
            unitInput.value = '';
            quantityInput.value = '1';
            clearError('freeTextProductError');
            nameInput.focus();
        } else {
            freeTextModal?.hide();
        }
    }

    function resetFreeTextModal() {
        document.getElementById('freeTextProductName').value = '';
        document.getElementById('freeTextUnitName').value = '';
        document.getElementById('freeTextUnitName').placeholder = 'Gõ hoặc chọn đơn vị...';
        document.getElementById('freeTextQuantity').value = '1';
        clearError('freeTextProductError');
    }

    function addOrMergeLine(item, quantity) {
        const key = item.itemKind === ITEM_FREE_TEXT
            ? 'free:' + normalizeKey(item.text) + '|' + normalizeKey(item.unitName)
            : 'catalog:' + item.productUnitConversionId;
        const existing = Array.from(rows.querySelectorAll('.request-line')).find(row => row.dataset.lineKey === key);
        if (existing) {
            const input = existing.querySelector('.quantity');
            input.value = decimalValue((parseFloat(input.value) || 0) + quantity);
            flashRow(existing);
        } else {
            const row = lineTemplate.content.firstElementChild.cloneNode(true);
            const freeText = item.itemKind === ITEM_FREE_TEXT;
            row.dataset.lineKey = key;
            row.dataset.itemKind = String(freeText ? ITEM_FREE_TEXT : ITEM_CATALOG);
            row.dataset.productName = item.text;
            row.dataset.unitName = item.unitName;
            row.dataset.sku = item.sku || '';
            row.dataset.factor = decimalValue(item.factor || 1);
            row.querySelector('.line-id').value = '';
            row.querySelector('.item-kind').value = freeText ? ITEM_FREE_TEXT : ITEM_CATALOG;
            row.querySelector('.variant-id').value = freeText ? '' : item.productVariantId;
            row.querySelector('.conversion-id').value = freeText ? '' : item.productUnitConversionId;
            row.querySelector('.product-name-input').value = item.text;
            row.querySelector('.unit-name-input').value = item.unitName;
            row.querySelector('.pr-product-name').textContent = item.text;
            row.querySelector('.pr-product-sku').textContent = freeText ? '' : (item.sku ? 'SKU: ' + item.sku : '');
            row.querySelector('.pr-free-text-badge').classList.toggle('d-none', !freeText);
            row.querySelector('.pr-open-request-badge').classList.toggle('d-none', freeText || !item.hasOpenRequest);
            row.querySelector('.pr-unit-name').textContent = item.unitName;
            row.querySelector('.pr-factor-wrap').classList.toggle('d-none', freeText);
            row.querySelector('.pr-factor').textContent = formatQuantity(item.factor || 1);
            row.querySelector('.quantity').value = decimalValue(quantity);
            rows.appendChild(row);
            bindLine(row);
            flashRow(row);
        }
        isDirty = true;
        renumberLines();
        updateCounters();
        updateEmptyState();
        hideLineError();
    }

    function bindLine(row) {
        row.querySelector('.remove-line').addEventListener('click', () => {
            row.remove();
            isDirty = true;
            renumberLines();
            updateCounters();
            updateEmptyState();
        });
        row.querySelector('.quantity').addEventListener('input', () => {
            isDirty = true;
            updateCounters();
        });
    }

    function renumberLines() {
        rows.querySelectorAll('.request-line').forEach((row, index) => {
            row.querySelector('.pr-line-number').textContent = String(index + 1);
            const prefix = 'Lines[' + index + '].';
            row.querySelector('.line-id').name = prefix + 'Id';
            row.querySelector('.item-kind').name = prefix + 'ItemKind';
            row.querySelector('.variant-id').name = prefix + 'ProductVariantId';
            row.querySelector('.conversion-id').name = prefix + 'ProductUnitConversionId';
            row.querySelector('.product-name-input').name = prefix + 'ProductName';
            row.querySelector('.unit-name-input').name = prefix + 'UnitName';
            row.querySelector('.quantity').name = prefix + 'Quantity';
        });
    }

    function updateCounters() {
        const lineElements = Array.from(rows.querySelectorAll('.request-line'));
        const total = lineElements.reduce((sum, row) => sum + (parseFloat(row.querySelector('.quantity').value) || 0), 0);
        document.getElementById('lineCountBadge').textContent = String(lineElements.length);
        document.getElementById('summaryLineCount').textContent = String(lineElements.length);
        document.getElementById('summaryQuantity').textContent = formatQuantity(total);
    }

    function updateTextCounters() {
        document.getElementById('titleCount').textContent = String(document.getElementById('Title').value.length);
        document.getElementById('noteCount').textContent = String(document.getElementById('Note').value.length);
    }

    function updateEmptyState() {
        const empty = rows.querySelectorAll('.request-line').length === 0;
        document.getElementById('emptyLineState').classList.toggle('d-none', !empty);
        document.querySelector('.pr-lines-table').classList.toggle('d-none', empty);
    }

    function focusProductSearch() {
        if (window.jQuery && jQuery.fn.select2) {
            jQuery('#productSearch').select2('open');
            window.setTimeout(() => document.querySelector('.select2-container--open .select2-search__field')?.focus(), 20);
        } else {
            document.getElementById('productSearch').focus();
        }
    }

    function submitWithIntent(intent) {
        if (isSubmitting) return;
        if (intent === 'submit' && !window.confirm('Gửi yêu cầu mua hàng để quản lý duyệt số lượng? Sau khi gửi bạn sẽ không thể sửa cho đến khi quản lý trả lại.')) return;
        document.getElementById('formIntent').value = intent;
        form.requestSubmit();
    }

    function validateAndSubmit(event) {
        if (isSubmitting) { event.preventDefault(); return; }
        const title = document.getElementById('Title');
        if (!title.value.trim()) {
            event.preventDefault();
            title.classList.add('is-invalid');
            title.focus();
            return;
        }
        title.classList.remove('is-invalid');
        const lineElements = Array.from(rows.querySelectorAll('.request-line'));
        if (!lineElements.length) {
            event.preventDefault();
            showLineError('Hãy thêm ít nhất một mặt hàng vào yêu cầu mua.');
            focusProductSearch();
            return;
        }
        const invalidLine = lineElements.find(row => {
            const quantity = parseFloat(row.querySelector('.quantity').value);
            const kind = Number(row.querySelector('.item-kind').value);
            if (!Number.isFinite(quantity) || quantity <= 0) return true;
            if (kind === ITEM_FREE_TEXT)
                return !row.querySelector('.product-name-input').value.trim() || !row.querySelector('.unit-name-input').value.trim();
            return !row.querySelector('.variant-id').value || !row.querySelector('.conversion-id').value;
        });
        if (invalidLine) {
            event.preventDefault();
            showLineError('Kiểm tra lại tên mặt hàng, đơn vị và số lượng của từng dòng.');
            invalidLine.querySelector('.quantity').focus();
            return;
        }
        renumberLines();
        hideLineError();
        isSubmitting = true;
        isDirty = false;
        document.querySelectorAll('.pr-submit-button').forEach(button => { button.disabled = true; });
    }

    function normalizeProduct(item) {
        return {
            itemKind: ITEM_CATALOG,
            text: item.text || 'Sản phẩm',
            productVariantId: Number(item.productVariantId),
            productUnitConversionId: Number(item.productUnitConversionId || item.id),
            sku: item.sku || '', unitName: item.unitName || 'Đơn vị', factor: Number(item.factor || 1),
            barcode: item.barcode || '', imageUrl: item.imageUrl || '',
            currentStockBaseQuantity: Number(item.currentStockBaseQuantity || 0),
            incomingBaseQuantity: Number(item.incomingBaseQuantity || 0), hasOpenRequest: Boolean(item.hasOpenRequest)
        };
    }

    function normalizeKey(value) { return String(value || '').trim().toLocaleLowerCase('vi-VN').replace(/\s+/g, ' '); }
    function parsePositive(value) { const parsed = Number(String(value).replace(',', '.')); return Number.isFinite(parsed) && parsed > 0 ? parsed : null; }
    function decimalValue(value) { return Number(value).toFixed(3).replace(/\.?0+$/, ''); }
    function formatQuantity(value) { return numberFormatter.format(Number(value) || 0); }
    function selectInput(id) { const input = document.getElementById(id); input?.focus(); input?.select?.(); }
    function flashRow(row) { row.classList.add('table-primary'); window.setTimeout(() => row.classList.remove('table-primary'), 650); }
    function showError(id, message) { const error = document.getElementById(id); error.textContent = message; error.classList.remove('d-none'); }
    function clearError(id) { const error = document.getElementById(id); error.textContent = ''; error.classList.add('d-none'); }
    function showLineError(message) { showError('lineValidationMessage', message); }
    function hideLineError() { clearError('lineValidationMessage'); }
})();
