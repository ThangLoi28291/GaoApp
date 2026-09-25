(function () {
    'use strict';

    const page = document.getElementById('purchaseRequestPage');
    const form = document.getElementById('purchaseRequestForm');
    if (!page || !form) return;

    const ITEM_CATALOG = 1;
    const ITEM_FREE_TEXT = 2;
    const byId = id => document.getElementById(id);
    const rows = byId('lineRows');
    const lineTemplate = byId('lineTemplate');
    const productModalElement = byId('productDetailModal');
    const freeTextModalElement = byId('freeTextProductModal');
    const shortcutModalElement = byId('shortcutHelpModal');
    const cameraModalElement = byId('purchaseCameraModal');
    const productModal = window.bootstrap ? bootstrap.Modal.getOrCreateInstance(productModalElement) : null;
    const freeTextModal = window.bootstrap ? bootstrap.Modal.getOrCreateInstance(freeTextModalElement) : null;
    const shortcutModal = window.bootstrap ? bootstrap.Modal.getOrCreateInstance(shortcutModalElement) : null;
    const cameraModal = window.bootstrap ? bootstrap.Modal.getOrCreateInstance(cameraModalElement) : null;
    const numberFormatter = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });

    let activeProduct = null;
    let activeUnit = null;
    let activeSource = 'search';
    let isSubmitting = false;
    let isDirty = false;
    let cameraGeneration = 0;
    let cameraStream = null;
    let cameraControls = null;
    let cameraDecoderPromise = null;
    let cameraTorch = false;
    let cameraHandoff = null;
    let cameraShown = false;
    let autoSelectingBarcode = false;

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
                processResults: (data, params) => {
                    const results = data.results || [];
                    const matches = results.filter(result => (result.unitOptions || []).some(unit =>
                        unit.isBarcodeMatch || unit.isHistoricalBarcodeMatch));
                    if (!autoSelectingBarcode && params.term && matches.length === 1) {
                        autoSelectingBarcode = true;
                        window.setTimeout(() => {
                            element.select2('close').val(null).trigger('change');
                            openProduct(matches[0], 'scanner');
                            window.setTimeout(() => { autoSelectingBarcode = false; }, 250);
                        }, 0);
                        return { ...data, results: [] };
                    }
                    return data;
                }
            },
            templateResult: renderProductResult,
            templateSelection: item => item.text || '',
            language: {
                inputTooShort: () => 'Nhập ít nhất 1 ký tự để tìm',
                searching: () => 'Đang tìm sản phẩm...',
                noResults: () => 'Không tìm thấy. Nhấn F4 để nhập mặt hàng mới.',
                loadingMore: () => 'Đang tải thêm...'
            },
            escapeMarkup: markup => markup
        });
        element.on('select2:select', event => {
            openProduct(event.params.data, 'search');
            window.setTimeout(() => element.val(null).trigger('change'), 0);
        });
    }

    function renderProductResult(item) {
        if (item.loading) return item.text;
        const product = normalizeProduct(item);
        const wrapper = document.createElement('div');
        wrapper.className = 'pr-select-result';
        const icon = document.createElement('span');
        icon.className = 'pr-select-result-icon';
        if (product.imageUrl) {
            const image = document.createElement('img');
            image.src = product.imageUrl;
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
        name.textContent = product.text;
        const meta = document.createElement('div');
        meta.className = 'pr-select-result-meta';
        meta.textContent = product.sku ? 'SKU: ' + product.sku : 'Chưa có SKU';
        text.append(name, meta);
        if (product.hasOpenRequest) {
            const warning = document.createElement('div');
            warning.className = 'pr-select-result-warning';
            warning.textContent = openRequestMessage(product);
            text.appendChild(warning);
        }
        const units = document.createElement('div');
        units.className = 'pr-select-result-units';
        product.unitOptions.slice(0, 4).forEach(unit => {
            const badge = document.createElement('span');
            badge.className = 'pr-select-result-unit' + (unit.isBarcodeMatch || unit.isHistoricalBarcodeMatch ? ' is-match' : '');
            badge.textContent = unit.unitName + ' ×' + formatQuantity(unit.factor);
            units.appendChild(badge);
        });
        if (product.unitOptions.length > 4) {
            const more = document.createElement('span');
            more.className = 'pr-select-result-unit';
            more.textContent = '+' + (product.unitOptions.length - 4);
            units.appendChild(more);
        }
        wrapper.append(icon, text, units);
        return wrapper;
    }

    function bindEvents() {
        byId('Title').addEventListener('input', markDirtyAndUpdateText);
        byId('Note').addEventListener('input', markDirtyAndUpdateText);
        byId('emptyAddProduct').addEventListener('click', focusProductSearch);
        byId('openFreeTextModal').addEventListener('click', openFreeTextModal);
        byId('openCameraScanner').addEventListener('click', openCameraScanner);
        byId('emptyOpenCamera').addEventListener('click', openCameraScanner);
        byId('purchaseCameraRetry').addEventListener('click', startCamera);
        byId('purchaseCameraTorch').addEventListener('click', toggleTorch);
        byId('saveProductAndContinue').addEventListener('click', () => saveCatalogProduct(true));
        byId('saveProductLine').addEventListener('click', () => saveCatalogProduct(false));
        byId('saveFreeTextAndContinue').addEventListener('click', () => saveFreeTextProduct(true));
        byId('saveFreeTextLine').addEventListener('click', () => saveFreeTextProduct(false));
        byId('modalQuantity').addEventListener('input', updateModalInventory);
        byId('modalUnitOptions').addEventListener('change', event => {
            if (!event.target.matches('input[name="purchaseUnit"]')) return;
            selectUnit(Number(event.target.value));
        });
        document.querySelectorAll('.pr-submit-button').forEach(button => button.addEventListener('click', () => submitWithIntent(button.dataset.intent)));

        productModalElement.addEventListener('shown.bs.modal', () => selectInput('modalQuantity'));
        productModalElement.addEventListener('hidden.bs.modal', () => { activeProduct = null; activeUnit = null; clearError('modalProductError'); });
        productModalElement.addEventListener('keydown', event => handleModalEnter(event, saveCatalogProduct));
        freeTextModalElement.addEventListener('shown.bs.modal', () => selectInput('freeTextProductName'));
        freeTextModalElement.addEventListener('hidden.bs.modal', resetFreeTextModal);
        freeTextModalElement.addEventListener('keydown', event => handleModalEnter(event, saveFreeTextProduct));
        byId('freeTextUnitName').addEventListener('input', handleOtherUnit);

        cameraModalElement.addEventListener('shown.bs.modal', () => { cameraShown = true; startCamera(); });
        cameraModalElement.addEventListener('hide.bs.modal', () => { cameraShown = false; invalidateCamera(); });
        cameraModalElement.addEventListener('hidden.bs.modal', () => {
            const next = cameraHandoff;
            cameraHandoff = null;
            next?.();
        });

        document.addEventListener('keydown', handleGlobalShortcut);
        document.addEventListener('visibilitychange', () => { if (document.hidden) invalidateCamera(); });
        window.addEventListener('pagehide', invalidateCamera);
        form.addEventListener('submit', validateAndSubmit);
        window.addEventListener('beforeunload', event => {
            if (!isDirty || isSubmitting) return;
            event.preventDefault();
            event.returnValue = '';
        });
    }

    function markDirtyAndUpdateText() { isDirty = true; updateTextCounters(); }

    function handleGlobalShortcut(event) {
        const modalOpen = document.querySelector('.modal.show');
        if (event.key === 'F2' && !event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault(); selectInput('Title');
        } else if (event.key === 'F3' && !event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault(); focusProductSearch();
        } else if (event.key === 'F4' && !event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault(); openFreeTextModal();
        } else if (event.key.toLowerCase() === 'c' && event.altKey && !event.ctrlKey && !event.metaKey && !modalOpen) {
            event.preventDefault(); openCameraScanner();
        } else if ((event.key === '/' || event.code === 'Slash') && event.ctrlKey && !event.altKey && !event.metaKey) {
            event.preventDefault(); if (!modalOpen) shortcutModal?.show();
        } else if (event.key.toLowerCase() === 's' && event.ctrlKey && !event.altKey && !event.metaKey && !modalOpen) {
            event.preventDefault(); submitWithIntent('save');
        } else if (event.key === 'Enter' && event.altKey && !event.ctrlKey && !event.metaKey && !modalOpen) {
            const submitButton = document.querySelector('.pr-submit-button[data-intent="submit"]');
            if (submitButton) { event.preventDefault(); submitWithIntent('submit'); }
        }
    }

    function handleModalEnter(event, saveHandler) {
        if (event.key !== 'Enter' || event.altKey || event.metaKey || event.target?.tagName === 'BUTTON' || event.target?.type === 'radio') return;
        event.preventDefault();
        saveHandler(Boolean(event.ctrlKey));
    }

    function openProduct(raw, source) {
        activeProduct = normalizeProduct(raw);
        activeSource = source || 'search';
        activeUnit = chooseInitialUnit(activeProduct.unitOptions);
        fillProductModal();
        productModal?.show();
    }

    function fillProductModal() {
        if (!activeProduct) return;
        byId('modalProductName').textContent = activeProduct.text;
        byId('modalProductMeta').textContent = activeProduct.sku ? 'SKU: ' + activeProduct.sku : 'Chưa có SKU';
        const image = byId('modalProductImage');
        image.replaceChildren();
        if (activeProduct.imageUrl) {
            const img = document.createElement('img'); img.src = activeProduct.imageUrl; img.alt = ''; image.appendChild(img);
        } else {
            const icon = document.createElement('i'); icon.className = 'bx bx-package'; image.appendChild(icon);
        }
        const warning = byId('modalOpenRequestWarning');
        warning.classList.toggle('d-none', !activeProduct.hasOpenRequest);
        warning.querySelector('span').textContent = openRequestMessage(activeProduct);
        renderUnitOptions();
        byId('modalQuantity').value = '1';
        clearError('modalProductError');
        updateModalInventory();
    }

    function renderUnitOptions() {
        const root = byId('modalUnitOptions');
        root.replaceChildren();
        activeProduct.unitOptions.forEach(unit => {
            const label = document.createElement('label');
            label.className = 'pr-unit-option' + (activeUnit?.productUnitConversionId === unit.productUnitConversionId ? ' is-selected' : '');
            const radio = document.createElement('input');
            radio.className = 'form-check-input'; radio.type = 'radio'; radio.name = 'purchaseUnit';
            radio.value = String(unit.productUnitConversionId); radio.checked = activeUnit?.productUnitConversionId === unit.productUnitConversionId;
            const info = document.createElement('span');
            const name = document.createElement('strong'); name.textContent = unit.unitName;
            const meta = document.createElement('small');
            meta.textContent = 'Quy đổi ×' + formatQuantity(unit.factor) + (unit.barcode ? ' · ' + unit.barcode : ' · Chưa có barcode');
            info.append(name, meta); label.append(radio, info);
            if (unit.isBarcodeMatch || unit.isHistoricalBarcodeMatch) {
                const match = document.createElement('em'); match.textContent = unit.isBarcodeMatch ? 'ĐÚNG MÃ QUÉT' : 'MÃ CŨ'; label.appendChild(match);
            }
            root.appendChild(label);
        });
        const historical = Boolean(activeUnit?.isHistoricalBarcodeMatch);
        byId('modalHistoricalBarcodeWarning').classList.toggle('d-none', !historical);
    }

    function selectUnit(conversionId) {
        activeUnit = activeProduct?.unitOptions.find(unit => unit.productUnitConversionId === conversionId) || null;
        byId('modalUnitOptions').querySelectorAll('.pr-unit-option').forEach(label => {
            label.classList.toggle('is-selected', Number(label.querySelector('input').value) === conversionId);
        });
        byId('modalHistoricalBarcodeWarning').classList.toggle('d-none', !activeUnit?.isHistoricalBarcodeMatch);
        updateModalInventory();
    }

    function updateModalInventory() {
        if (!activeProduct || !activeUnit) return;
        const factor = activeUnit.factor || 1;
        const stock = activeProduct.currentStockBaseQuantity / factor;
        const incoming = activeProduct.incomingBaseQuantity / factor;
        byId('modalCurrentStock').textContent = formatQuantity(stock) + ' ' + activeUnit.unitName;
        byId('modalIncoming').textContent = formatQuantity(incoming) + ' ' + activeUnit.unitName;
        byId('modalCurrentStockBase').textContent = formatQuantity(activeProduct.currentStockBaseQuantity) + ' đơn vị gốc';
        byId('modalIncomingBase').textContent = formatQuantity(activeProduct.incomingBaseQuantity) + ' đơn vị gốc';
        const quantity = parsePositive(byId('modalQuantity').value) || 0;
        byId('modalBaseEquivalent').textContent = quantity > 0
            ? formatQuantity(quantity) + ' ' + activeUnit.unitName + ' = ' + formatQuantity(quantity * factor) + ' đơn vị gốc'
            : 'Nhập số lượng lớn hơn 0.';
    }

    function saveCatalogProduct(continueAdding) {
        if (!activeProduct || !activeUnit) {
            showError('modalProductError', 'Hãy chọn đơn vị mua.');
            return;
        }
        const quantity = parsePositive(byId('modalQuantity').value);
        if (quantity === null) {
            showError('modalProductError', 'Số lượng phải lớn hơn 0.');
            selectInput('modalQuantity');
            return;
        }
        const item = { ...activeProduct, ...activeUnit, itemKind: ITEM_CATALOG };
        addOrMergeLine(item, quantity);
        const resumeCamera = continueAdding && activeSource === 'camera';
        productModal?.hide();
        if (continueAdding) window.setTimeout(resumeCamera ? openCameraScanner : focusProductSearch, 180);
    }

    function openFreeTextModal() { resetFreeTextModal(); freeTextModal?.show(); }

    function handleOtherUnit(event) {
        if (event.target.value.trim().toLocaleLowerCase('vi-VN') !== 'đơn vị khác') return;
        event.target.value = ''; event.target.placeholder = 'Nhập tên đơn vị khác...';
    }

    function saveFreeTextProduct(continueAdding) {
        const nameInput = byId('freeTextProductName');
        const unitInput = byId('freeTextUnitName');
        const quantityInput = byId('freeTextQuantity');
        const productName = nameInput.value.trim();
        const unitName = unitInput.value.trim();
        const quantity = parsePositive(quantityInput.value);
        if (!productName) { showError('freeTextProductError', 'Hãy nhập tên mặt hàng cần mua.'); nameInput.focus(); return; }
        if (!unitName || unitName.toLocaleLowerCase('vi-VN') === 'đơn vị khác') { showError('freeTextProductError', 'Hãy chọn hoặc nhập đơn vị mua cụ thể.'); unitInput.focus(); return; }
        if (quantity === null) { showError('freeTextProductError', 'Số lượng phải lớn hơn 0.'); quantityInput.focus(); return; }
        addOrMergeLine({ itemKind: ITEM_FREE_TEXT, text: productName, unitName, factor: 1 }, quantity);
        if (continueAdding) {
            nameInput.value = ''; unitInput.value = ''; quantityInput.value = '1'; clearError('freeTextProductError'); nameInput.focus();
        } else freeTextModal?.hide();
    }

    function resetFreeTextModal() {
        byId('freeTextProductName').value = '';
        byId('freeTextUnitName').value = '';
        byId('freeTextUnitName').placeholder = 'Gõ hoặc chọn đơn vị...';
        byId('freeTextQuantity').value = '1';
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
            const factor = Number(item.factor || 1);
            row.dataset.lineKey = key;
            row.dataset.itemKind = String(freeText ? ITEM_FREE_TEXT : ITEM_CATALOG);
            row.dataset.productName = item.text;
            row.dataset.unitName = item.unitName;
            row.dataset.sku = item.sku || '';
            row.dataset.factor = decimalValue(factor);
            row.dataset.stockBase = String(item.currentStockBaseQuantity || 0);
            row.dataset.incomingBase = String(item.incomingBaseQuantity || 0);
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
            row.querySelector('.pr-factor').textContent = formatQuantity(factor);
            row.querySelector('.pr-stock').textContent = freeText ? 'Chưa có dữ liệu' : formatQuantity((item.currentStockBaseQuantity || 0) / factor) + ' ' + item.unitName;
            row.querySelector('.pr-incoming').textContent = freeText ? '' : 'Đang về: ' + formatQuantity((item.incomingBaseQuantity || 0) / factor) + ' ' + item.unitName;
            const thumb = row.querySelector('.pr-product-thumb');
            if (!freeText && item.imageUrl) { thumb.replaceChildren(); const img = document.createElement('img'); img.src = item.imageUrl; img.alt = ''; thumb.appendChild(img); }
            row.querySelector('.quantity').value = decimalValue(quantity);
            rows.appendChild(row);
            bindLine(row);
            flashRow(row);
        }
        isDirty = true;
        renumberLines(); updateCounters(); updateEmptyState(); hideLineError();
    }

    function bindLine(row) {
        row.querySelector('.remove-line').addEventListener('click', () => {
            row.remove(); isDirty = true; renumberLines(); updateCounters(); updateEmptyState();
        });
        row.querySelector('.quantity').addEventListener('input', () => { isDirty = true; updateCounters(); });
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
        byId('lineCountBadge').textContent = String(lineElements.length);
        byId('summaryLineCount').textContent = String(lineElements.length);
        byId('summaryQuantity').textContent = formatQuantity(total);
    }

    function updateTextCounters() {
        byId('titleCount').textContent = String(byId('Title').value.length);
        byId('noteCount').textContent = String(byId('Note').value.length);
    }

    function updateEmptyState() {
        const empty = rows.querySelectorAll('.request-line').length === 0;
        byId('emptyLineState').classList.toggle('d-none', !empty);
        document.querySelector('.pr-lines-table').classList.toggle('d-none', empty);
    }

    function focusProductSearch() {
        if (window.jQuery && jQuery.fn.select2) {
            jQuery('#productSearch').select2('open');
            window.setTimeout(() => document.querySelector('.select2-container--open .select2-search__field')?.focus(), 20);
        } else byId('productSearch').focus();
    }

    function submitWithIntent(intent) {
        if (isSubmitting) return;
        if (intent === 'submit' && !window.confirm('Gửi yêu cầu mua hàng để quản lý duyệt số lượng? Sau khi gửi bạn sẽ không thể sửa cho đến khi quản lý trả lại.')) return;
        byId('formIntent').value = intent;
        form.requestSubmit();
    }

    function validateAndSubmit(event) {
        if (isSubmitting) { event.preventDefault(); return; }
        const title = byId('Title');
        if (!title.value.trim()) { event.preventDefault(); title.classList.add('is-invalid'); title.focus(); return; }
        title.classList.remove('is-invalid');
        const lineElements = Array.from(rows.querySelectorAll('.request-line'));
        if (!lineElements.length) { event.preventDefault(); showLineError('Hãy thêm ít nhất một mặt hàng vào yêu cầu mua.'); focusProductSearch(); return; }
        const invalidLine = lineElements.find(row => {
            const quantity = parseFloat(row.querySelector('.quantity').value);
            const kind = Number(row.querySelector('.item-kind').value);
            if (!Number.isFinite(quantity) || quantity <= 0) return true;
            if (kind === ITEM_FREE_TEXT) return !row.querySelector('.product-name-input').value.trim() || !row.querySelector('.unit-name-input').value.trim();
            return !row.querySelector('.variant-id').value || !row.querySelector('.conversion-id').value;
        });
        if (invalidLine) { event.preventDefault(); showLineError('Kiểm tra lại tên mặt hàng, đơn vị và số lượng của từng dòng.'); invalidLine.querySelector('.quantity').focus(); return; }
        renumberLines(); hideLineError(); isSubmitting = true; isDirty = false;
        document.querySelectorAll('.pr-submit-button').forEach(button => { button.disabled = true; });
    }

    function normalizeProduct(item) {
        const units = Array.isArray(item.unitOptions) && item.unitOptions.length
            ? item.unitOptions.map(normalizeUnit)
            : [normalizeUnit(item)];
        return {
            itemKind: ITEM_CATALOG,
            text: item.text || 'Sản phẩm',
            productVariantId: Number(item.productVariantId),
            sku: item.sku || '',
            imageUrl: item.imageUrl || '',
            currentStockBaseQuantity: Number(item.currentStockBaseQuantity || 0),
            incomingBaseQuantity: Number(item.incomingBaseQuantity || 0),
            hasOpenRequest: Boolean(item.hasOpenRequest),
            openRequestCount: Number(item.openRequestCount || 0),
            openRequestBaseQuantity: Number(item.openRequestBaseQuantity || 0),
            openRequestNumber: item.openRequestNumber || '',
            unitOptions: units
        };
    }

    function normalizeUnit(item) {
        return {
            productUnitConversionId: Number(item.productUnitConversionId || item.id),
            unitId: Number(item.unitId),
            unitName: item.unitName || 'Đơn vị',
            factor: Number(item.factor || 1),
            barcode: item.barcode || '',
            isBaseUnit: Boolean(item.isBaseUnit),
            isDefaultForSale: Boolean(item.isDefaultForSale),
            isBarcodeMatch: Boolean(item.isBarcodeMatch),
            isHistoricalBarcodeMatch: Boolean(item.isHistoricalBarcodeMatch)
        };
    }

    function chooseInitialUnit(units) {
        return units.find(x => x.isBarcodeMatch)
            || units.find(x => x.isHistoricalBarcodeMatch)
            || units.find(x => x.isDefaultForSale)
            || units.find(x => x.isBaseUnit)
            || units[0]
            || null;
    }

    function openRequestMessage(product) {
        if (!product.hasOpenRequest) return '';
        const quantity = formatQuantity(product.openRequestBaseQuantity) + ' đơn vị gốc';
        if (product.openRequestCount > 1) return `Đang có ${quantity} trong ${product.openRequestCount} yêu cầu chưa hoàn tất.`;
        if (product.openRequestNumber) return `Đang có ${quantity} trong ${product.openRequestNumber}.`;
        return 'Sản phẩm đã có trong một yêu cầu đang xử lý.';
    }

    function openCameraScanner() {
        cameraHandoff = null;
        cameraMessage('');
        cameraModal?.show();
    }

    function loadCameraDecoder() {
        if (window.ZXingBrowser) return Promise.resolve(window.ZXingBrowser);
        if (!cameraDecoderPromise) cameraDecoderPromise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = page.dataset.decoderUrl; script.async = true;
            script.onload = () => window.ZXingBrowser ? resolve(window.ZXingBrowser) : reject(new Error('Không khởi tạo được bộ quét.'));
            script.onerror = () => { script.remove(); cameraDecoderPromise = null; reject(new Error('Không tải được bộ quét barcode.')); };
            document.head.append(script);
        });
        return cameraDecoderPromise;
    }

    async function startCamera() {
        invalidateCamera();
        const turn = cameraGeneration;
        byId('purchaseCameraRetry').disabled = true;
        if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
            cameraMessage('Camera cần HTTPS hoặc localhost và một trình duyệt có hỗ trợ.', true);
            byId('purchaseCameraRetry').disabled = false;
            return;
        }
        cameraMessage('Đang mở camera sau. Hãy cho phép truy cập camera khi trình duyệt hỏi.');
        try {
            const media = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } }, audio: false });
            if (!cameraShown || turn !== cameraGeneration) { media.getTracks().forEach(track => track.stop()); return; }
            cameraStream = media;
            byId('purchaseCameraVideo').srcObject = media;
            const ZXing = await loadCameraDecoder();
            if (!cameraShown || turn !== cameraGeneration) return;
            const track = media.getVideoTracks()[0];
            byId('purchaseCameraTorch').hidden = !track?.getCapabilities?.().torch;
            const reader = new ZXing.BrowserMultiFormatOneDReader(undefined, { delayBetweenScanAttempts: 180, delayBetweenScanSuccess: 600 });
            const scanning = await reader.decodeFromStream(media, byId('purchaseCameraVideo'), (result, error, current) => {
                if (!result || !cameraShown || turn !== cameraGeneration) return;
                current.stop(); cameraGeneration++; stopCameraMedia();
                const code = result.getText().trim();
                cameraMessage(`Đã nhận mã ${code}. Đang tìm sản phẩm…`);
                signalScanSuccess();
                lookupScannedBarcode(code);
            });
            if (!cameraShown || turn !== cameraGeneration) scanning.stop();
            else { cameraControls = scanning; cameraMessage('Đưa barcode vào giữa khung. Camera sẽ dừng khi nhận được mã.'); }
        } catch (error) {
            if (!cameraShown || turn !== cameraGeneration) return;
            stopCameraMedia();
            const details = {
                NotAllowedError: 'Chưa được cấp quyền camera. Cho phép camera trong cài đặt trình duyệt rồi bấm Quét lại.',
                NotFoundError: 'Không tìm thấy camera trên thiết bị này.',
                NotReadableError: 'Camera đang được ứng dụng khác sử dụng.'
            };
            cameraMessage(details[error.name] || error.message || 'Không mở được camera.', true);
        } finally {
            if (cameraShown) byId('purchaseCameraRetry').disabled = false;
        }
    }

    async function lookupScannedBarcode(code) {
        try {
            const response = await fetch(`${page.dataset.productLookupUrl}?term=${encodeURIComponent(code)}&page=1`, { cache: 'no-store' });
            if (!response.ok) throw new Error('Không tìm được sản phẩm lúc này.');
            const data = await response.json();
            const products = (data.results || []).map(normalizeProduct);
            const exact = products.filter(product => product.unitOptions.some(unit => unit.isBarcodeMatch));
            const historical = products.filter(product => product.unitOptions.some(unit => unit.isHistoricalBarcodeMatch));
            const matches = exact.length ? exact : historical;
            if (matches.length !== 1) {
                cameraMessage(matches.length > 1
                    ? `Mã ${code} đang khớp nhiều sản phẩm. Hãy đóng camera và tìm thủ công để chọn đúng.`
                    : `Không tìm thấy sản phẩm cho mã ${code}. Kiểm tra mã hoặc dùng “Hàng chưa có”.`, true);
                return;
            }
            cameraHandoff = () => openProduct(matches[0], 'camera');
            cameraModal?.hide();
        } catch (error) {
            cameraMessage(error.message || 'Không tìm được sản phẩm lúc này.', true);
        }
    }

    async function toggleTorch() {
        const track = cameraStream?.getVideoTracks()[0];
        if (!track) return;
        try {
            await track.applyConstraints({ advanced: [{ torch: !cameraTorch }] });
            cameraTorch = !cameraTorch;
            byId('purchaseCameraTorch').textContent = cameraTorch ? 'Tắt đèn' : 'Bật đèn';
        } catch { cameraMessage('Camera này không bật được đèn. Hãy thử ở nơi sáng hơn.', true); }
    }

    function stopCameraMedia() {
        cameraControls?.stop(); cameraControls = null;
        cameraStream?.getTracks().forEach(track => track.stop()); cameraStream = null;
        byId('purchaseCameraVideo').srcObject = null;
        byId('purchaseCameraTorch').hidden = true;
        byId('purchaseCameraTorch').textContent = 'Bật đèn';
        cameraTorch = false;
    }

    function invalidateCamera() { cameraGeneration++; stopCameraMedia(); }
    function cameraMessage(message, error = false) { byId('purchaseCameraMessage').textContent = message; byId('purchaseCameraMessage').dataset.error = String(error); }

    function signalScanSuccess() {
        navigator.vibrate?.(80);
        try {
            const Audio = window.AudioContext || window.webkitAudioContext;
            if (!Audio) return;
            const context = new Audio();
            const oscillator = context.createOscillator();
            const gain = context.createGain();
            oscillator.frequency.value = 880; gain.gain.value = .035;
            oscillator.connect(gain); gain.connect(context.destination);
            oscillator.start(); oscillator.stop(context.currentTime + .08);
            oscillator.addEventListener('ended', () => context.close());
        } catch { /* Âm báo là hỗ trợ bổ sung; quét vẫn tiếp tục nếu trình duyệt chặn. */ }
    }

    function normalizeKey(value) { return String(value || '').trim().toLocaleLowerCase('vi-VN').replace(/\s+/g, ' '); }
    function parsePositive(value) { const parsed = Number(String(value).replace(',', '.')); return Number.isFinite(parsed) && parsed > 0 ? parsed : null; }
    function decimalValue(value) { return Number(value).toFixed(3).replace(/\.?0+$/, ''); }
    function formatQuantity(value) { return numberFormatter.format(Number(value) || 0); }
    function selectInput(id) { const input = byId(id); input?.focus(); input?.select?.(); }
    function flashRow(row) { row.classList.remove('pr-line-flash'); void row.offsetWidth; row.classList.add('pr-line-flash'); window.setTimeout(() => row.classList.remove('pr-line-flash'), 850); }
    function showError(id, message) { const error = byId(id); error.textContent = message; error.classList.remove('d-none'); }
    function clearError(id) { const error = byId(id); error.textContent = ''; error.classList.add('d-none'); }
    function showLineError(message) { showError('lineValidationMessage', message); }
    function hideLineError() { clearError('lineValidationMessage'); }
})();
