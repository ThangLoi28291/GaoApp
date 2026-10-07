(function () {
    'use strict';

    const shell = document.querySelector('.promo-shell');
    if (!shell) return;

    const urls = {
        search: shell.dataset.searchUrl,
        get: shell.dataset.getUrl,
        save: shell.dataset.saveUrl,
        toggle: shell.dataset.toggleUrl,
        delete: shell.dataset.deleteUrl,
        duplicate: shell.dataset.duplicateUrl,
        searchProduct: shell.dataset.searchProductUrl,
        productUnits: shell.dataset.productUnitsUrl,
        getProduct: shell.dataset.getProductUrl
    };

    const modalEl = document.getElementById('promotionEditModal');
    const deleteModalEl = document.getElementById('promotionDeleteModal');

    const editModal = modalEl ? new bootstrap.Modal(modalEl) : null;
    const deleteModal = deleteModalEl ? new bootstrap.Modal(deleteModalEl) : null;

    const token = () =>
        document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]')?.value || '';

    function toastSuccess(message) {
        if (window.Swal) {
            Swal.fire({ icon: 'success', title: message, timer: 1400, showConfirmButton: false });
        } else {
            alert(message);
        }
    }

    function toastError(message) {
        if (window.Swal) {
            Swal.fire({ icon: 'error', title: 'Lỗi', text: message });
        } else {
            alert(message);
        }
    }

    function confirmPopup(message) {
        if (!window.Swal) return Promise.resolve(confirm(message));

        return Swal.fire({
            icon: 'question',
            title: 'Xác nhận',
            text: message,
            showCancelButton: true,
            confirmButtonText: 'Đồng ý',
            cancelButtonText: 'Hủy'
        }).then(r => r.isConfirmed);
    }

    function val(id) {
        return document.getElementById(id)?.value ?? '';
    }

    function setVal(id, value) {
        const el = document.getElementById(id);
        if (el) el.value = value ?? '';
    }

    function setChecked(id, checked) {
        const el = document.getElementById(id);
        if (el) el.checked = !!checked;
    }

    function getChecked(id) {
        return !!document.getElementById(id)?.checked;
    }

    function toNumber(value) {
        const n = Number(value);
        return Number.isFinite(n) ? n : 0;
    }

    function escapeHtml(s) {
        return String(s ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function toDateTimeLocal(dateValue) {
        if (!dateValue) return '';

        const d = new Date(dateValue);
        if (Number.isNaN(d.getTime())) return '';

        const pad = n => String(n).padStart(2, '0');

        return [
            d.getFullYear(),
            pad(d.getMonth() + 1),
            pad(d.getDate())
        ].join('-') + 'T' + [
            pad(d.getHours()),
            pad(d.getMinutes())
        ].join(':');
    }

    function nowDateTimeLocal(offsetDays) {
        const d = new Date();
        d.setDate(d.getDate() + (offsetDays || 0));

        const pad = n => String(n).padStart(2, '0');

        return [
            d.getFullYear(),
            pad(d.getMonth() + 1),
            pad(d.getDate())
        ].join('-') + 'T' + [
            pad(d.getHours()),
            pad(d.getMinutes())
        ].join(':');
    }

    function typeText(type) {
        type = Number(type);

        if (type === 1) return 'Giảm giá sản phẩm';
        if (type === 2) return 'Combo giá cố định';
        if (type === 3) return 'Mua X tặng Y';

        return 'Khuyến mãi';
    }

    function tierText(tier) {
        tier = String(tier || '').toUpperCase();

        if (tier === 'RETAIL') return 'Khách lẻ';
        if (tier === 'WHOLESALE') return 'Khách sỉ';

        return 'Tất cả';
    }

    function formatMoney(value) {
        const n = Number(value || 0);
        return n.toLocaleString('vi-VN');
    }

    function normalizeProductData(data) {
        data = data || {};

        return {
            productId: toNumber(data.productId ?? data.ProductId),
            variantId: toNumber(data.variantId ?? data.VariantId ?? data.id ?? data.Id),
            text: data.text ?? data.Text ?? data.productName ?? data.ProductName ?? '',
            productName: (data.variantName ?? data.VariantName) || (data.productName ?? data.ProductName ?? data.text ?? data.Text ?? ''),
            barcode: data.barcode ?? data.Barcode ?? '',
            sku: data.sku ?? data.Sku ?? data.SKU ?? '',
            baseUnitName: data.baseUnitName ?? data.BaseUnitName ?? '',
            price: toNumber(data.price ?? data.Price)
        };
    }

    function normalizeUnitData(data) {
        data = data || {};

        const conversionIdRaw =
            data.productUnitConversionId ??
            data.ProductUnitConversionId ??
            data.id ??
            data.Id;

        return {
            productUnitConversionId: conversionIdRaw === null || conversionIdRaw === undefined || conversionIdRaw === ''
                ? null
                : toNumber(conversionIdRaw),
            unitId: toNumber(data.unitId ?? data.UnitId),
            unitName: data.unitName ?? data.UnitName ?? '',
            factor: toNumber(data.factor ?? data.Factor ?? 1),
            text: data.text ?? data.Text ?? data.unitName ?? data.UnitName ?? 'Đơn vị'
        };
    }

    function refreshTypePanels() {
        const type = Number(val('promoType') || 1);

        document.querySelectorAll('.promo-type-panel').forEach(p => {
            p.classList.toggle('d-none', Number(p.dataset.panelType) !== type);
        });

        const helper = document.getElementById('promoRuleHelper');
        if (helper) {
            helper.textContent = isMixedQuantity()
                ? 'Chọn các vị được phép ghép, có cùng đơn vị gốc. Khách có thể chọn bất kỳ tỉ lệ nào; hộp, lốc và thùng đều được quy đổi để cộng tổng.'
                : type === 2 ? 'Combo dùng danh sách sản phẩm bắt buộc. Mỗi dòng là một món trong combo.'
                : 'Gõ tên sản phẩm hoặc barcode để chọn. Hệ thống tự lưu ProductId / VariantId / đơn vị quy đổi.';
        }

        const mixed = isMixedQuantity();
        ['promoComboQuantityPanel', 'promoMixedQuantityHelp'].forEach(id =>
            document.getElementById(id)?.classList.toggle('d-none', !mixed));
        const priceLabel = document.getElementById('promoComboPriceLabel');
        if (priceLabel) priceLabel.textContent = mixed ? 'Giá một thùng' : 'Giá combo';
        document.querySelectorAll('#promoRuleBody .promo-rule-unit-area, #promoRuleBody .promo-rule-qty-area')
            .forEach(el => el.classList.toggle('d-none', mixed));
        document.querySelectorAll('#promoRuleBody .promo-rule-card')
            .forEach(card => card.classList.toggle('is-mixed-quantity', mixed));

        updatePreview();
    }

    function isMixedQuantity() {
        return Number(val('promoType')) === 2 && Number(val('promoComboPricingMode')) === 2;
    }

    function updatePreview() {
        const name = val('promoName').trim() || 'Chưa đặt tên';
        const type = Number(val('promoType') || 1);
        const tier = val('promoCustomerTier');
        const priority = val('promoPriority') || '0';
        const ruleCount = document.querySelectorAll('#promoRuleBody .promo-rule-card').length;

        const nameEl = document.getElementById('promoPreviewName');
        const typeEl = document.getElementById('promoPreviewType');
        const tierEl = document.getElementById('promoPreviewTier');
        const priorityEl = document.getElementById('promoPreviewPriority');
        const ruleCountEl = document.getElementById('promoPreviewRuleCount');

        if (nameEl) nameEl.textContent = name;
        if (typeEl) typeEl.textContent = isMixedQuantity()
            ? `Ghép vị từ ${val('promoComboQuantity')} đơn vị gốc · ${formatMoney(val('promoComboFixedPrice'))} / thùng`
            : typeText(type);
        if (tierEl) tierEl.textContent = tierText(tier);
        if (priorityEl) priorityEl.textContent = priority;
        if (ruleCountEl) ruleCountEl.textContent = ruleCount;
    }

    async function loadUnitsForRow(tr, variantId, selectedConversionId) {
        const unitSelect = tr.querySelector('[data-field="unitSelect"]');
        const hiddenConversion = tr.querySelector('[data-field="productUnitConversionId"]');

        if (!unitSelect) return;

        unitSelect.innerHTML = `<option value="">Tất cả đơn vị / đơn vị gốc</option>`;

        if (!variantId || !urls.productUnits) {
            if (hiddenConversion) hiddenConversion.value = '';
            return;
        }

        try {
            const res = await fetch(`${urls.productUnits}?variantId=${encodeURIComponent(variantId)}`, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });

            const json = await res.json();
            const list = Array.isArray(json) ? json : [];

            list.forEach(raw => {
                const u = normalizeUnitData(raw);
                const option = document.createElement('option');

                option.value = u.productUnitConversionId ?? '';
                option.textContent = u.text || `${u.unitName} - x${u.factor}`;
                option.dataset.unitName = u.unitName;
                option.dataset.factor = String(u.factor);

                unitSelect.appendChild(option);
            });

            const valueToSelect = selectedConversionId ?? '';
            unitSelect.value = valueToSelect === null ? '' : String(valueToSelect);

            if (hiddenConversion) hiddenConversion.value = unitSelect.value || '';
        } catch {
            unitSelect.innerHTML = `<option value="">Không tải được đơn vị</option>`;
            if (hiddenConversion) hiddenConversion.value = '';
        }
    }
    function getDisplayName(x) {
        return (
            x.productVariantName ||
            x.ProductVariantName ||
            x.displayName ||
            x.DisplayName ||
            x.productName ||
            x.ProductName ||
            x.text ||
            x.Text ||
            ''
        );
    }

    function normalizePromoSearchItem(x) {
        x = x || {};

        const productId = toNumber(x.productId ?? x.ProductId);
        const variantId = toNumber(x.variantId ?? x.VariantId ?? x.id ?? x.Id);
        const displayName = getDisplayName(x);

        const unitOptions = Array.isArray(x.unitOptions || x.UnitOptions)
            ? (x.unitOptions || x.UnitOptions)
            : [];

        return {
            kind: 'parent',
            productId,
            variantId,
            productName: x.productName ?? x.ProductName ?? displayName,
            productVariantName: x.productVariantName ?? x.ProductVariantName ?? displayName,
            displayName,
            barcode: x.barcode ?? x.Barcode ?? '',
            price: toNumber(x.price ?? x.Price),
            imageUrl: x.imageUrl ?? x.ImageUrl ?? '',
            imageThumbUrl: x.imageThumbUrl ?? x.ImageThumbUrl ?? '',
            imageAlt: x.imageAlt ?? x.ImageAlt ?? displayName,
            hasImage: !!(x.hasImage ?? x.HasImage),
            unitOptions
        };
    }

    function normalizePromoUnit(x) {
        x = x || {};

        return {
            productUnitConversionId: toNumber(x.productUnitConversionId ?? x.ProductUnitConversionId),
            unitId: toNumber(x.unitId ?? x.UnitId),
            unitName: x.unitName ?? x.UnitName ?? 'Đơn vị',
            factor: toNumber(x.factor ?? x.Factor ?? 1),
            price: toNumber(x.price ?? x.Price)
        };
    }

    function renderPromoProductOption(item, index) {
        const p = normalizePromoSearchItem(item);
        const img = p.hasImage && (p.imageThumbUrl || p.imageUrl)
            ? `<img class="promo-ac-thumb" src="${escapeHtml(p.imageThumbUrl || p.imageUrl)}" alt="${escapeHtml(p.imageAlt)}" />`
            : `<div class="promo-ac-thumb-placeholder">IMG</div>`;

        return `
        <button type="button"
                class="promo-ac-option"
                data-promo-ac-index="${index}">
            <div class="promo-ac-row">
                ${img}

                <div class="promo-ac-main">
                    <div class="promo-ac-name">${escapeHtml(p.displayName)}</div>
                    <div class="promo-ac-meta">
                        ${p.barcode ? `Barcode: ${escapeHtml(p.barcode)} · ` : ''}
                        VID: ${p.variantId}
                        ${p.price > 0 ? ` · Giá: ${formatMoney(p.price)}` : ''}
                    </div>
                </div>
            </div>
        </button>
    `;
    }

    function setPromoActiveOption(dropdown, index) {
        const rows = dropdown.querySelectorAll('[data-promo-ac-index]');
        rows.forEach(x => x.classList.remove('active'));

        if (!rows.length) return -1;

        if (index < 0) index = 0;
        if (index >= rows.length) index = rows.length - 1;

        rows[index].classList.add('active');
        rows[index].scrollIntoView({ block: 'nearest' });

        return index;
    }

    function setRowUnitsFromProduct(tr, product, selectedConversionId) {
        const unitSelect = tr.querySelector('[data-field="unitSelect"]');
        const hiddenConversion = tr.querySelector('[data-field="productUnitConversionId"]');

        if (!unitSelect) return;

        unitSelect.innerHTML = `<option value="">Tất cả đơn vị / đơn vị gốc</option>`;

        const units = Array.isArray(product.unitOptions)
            ? product.unitOptions.map(normalizePromoUnit)
            : [];

        units.forEach(u => {
            const opt = document.createElement('option');
            opt.value = u.productUnitConversionId > 0 ? String(u.productUnitConversionId) : '';
            opt.textContent = `${u.unitName} - x${u.factor}`;
            opt.dataset.unitName = u.unitName;
            opt.dataset.factor = String(u.factor);
            unitSelect.appendChild(opt);
        });

        if (selectedConversionId) {
            unitSelect.value = String(selectedConversionId);
        }

        if (hiddenConversion) {
            hiddenConversion.value = unitSelect.value || '';
        }
    }

    function setRowProduct(tr, rawProduct, selectedConversionId) {
        const p = normalizePromoSearchItem(rawProduct);

        const productIdInput = tr.querySelector('[data-field="productId"]');
        const variantIdInput = tr.querySelector('[data-field="variantId"]');
        const productNameInput = tr.querySelector('[data-field="productName"]');
        const selected = tr.querySelector('.promo-selected-product');
        const systemInfo = tr.querySelector('[data-role="systemInfo"]');

        if (productIdInput) productIdInput.value = p.productId || '';
        if (variantIdInput) variantIdInput.value = p.variantId || '';
        if (productNameInput) productNameInput.value = p.displayName || '';

        if (selected) {
            selected.innerHTML = `
            <div class="promo-selected-name">${escapeHtml(p.displayName)}</div>
            <div class="promo-selected-meta">
                ${p.barcode ? `Barcode: ${escapeHtml(p.barcode)} · ` : ''}
                VariantId: ${p.variantId}
            </div>
        `;
        }

        if (systemInfo) {
            systemInfo.innerHTML = `
            <div>PID: <b>${p.productId || '-'}</b></div>
            <div>VID: <b>${p.variantId || '-'}</b></div>
        `;
        }

        setRowUnitsFromProduct(tr, p, selectedConversionId);
    }

    function initProductSearchInput(input, initialData) {
        if (!input) return;

        const tr = input.closest('.promo-rule-card');
        let items = [];
        let activeIndex = -1;
        let timer = null;
        let abortController = null;

        function getPicker() {
            return input.closest('.promo-product-picker');
        }

        function closeDropdown() {
            const picker = getPicker();
            picker?.querySelector('.promo-ac-dropdown')?.remove();
            activeIndex = -1;
        }

        function openDropdown(list) {
            const picker = getPicker();
            if (!picker) return;

            closeDropdown();

            const dropdown = document.createElement('div');
            dropdown.className = 'promo-ac-dropdown';

            if (!list.length) {
                dropdown.innerHTML = `<div class="promo-ac-empty">Không tìm thấy sản phẩm</div>`;
            } else {
                dropdown.innerHTML = list.map(renderPromoProductOption).join('');
            }

            picker.appendChild(dropdown);

            if (list.length) {
                activeIndex = setPromoActiveOption(dropdown, 0);
            }

            dropdown.addEventListener('mousedown', function (e) {
                const btn = e.target.closest('[data-promo-ac-index]');
                if (!btn) return;

                e.preventDefault();

                const index = Number(btn.dataset.promoAcIndex || -1);
                const item = items[index];
                if (!item) return;

                setRowProduct(tr, item, null);
                closeDropdown();
                updatePreview();
            });

            dropdown.addEventListener('mousemove', function (e) {
                const btn = e.target.closest('[data-promo-ac-index]');
                if (!btn) return;

                const index = Number(btn.dataset.promoAcIndex || -1);
                if (index >= 0) {
                    activeIndex = setPromoActiveOption(dropdown, index);
                }
            });
        }

        async function search(keyword) {
            keyword = String(keyword || '').trim();

            if (keyword.length < 1) {
                closeDropdown();
                return;
            }

            if (abortController) {
                try { abortController.abort(); } catch (_) { }
            }

            abortController = new AbortController();

            try {
                const url = `${urls.searchProduct}?keyword=${encodeURIComponent(keyword)}&take=10`;

                const res = await fetch(url, {
                    method: 'GET',
                    signal: abortController.signal,
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });

                const json = await res.json();
                items = Array.isArray(json) ? json : [];

                openDropdown(items);
            } catch (err) {
                if (err?.name === 'AbortError') return;
                items = [];
                openDropdown([]);
            }
        }

        input.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(() => search(input.value), 220);
        });

        input.addEventListener('focus', function () {
            if (input.value.trim()) {
                clearTimeout(timer);
                timer = setTimeout(() => search(input.value), 80);
            }
        });

        input.addEventListener('keydown', function (e) {
            const dropdown = getPicker()?.querySelector('.promo-ac-dropdown');
            const hasDropdown = !!dropdown && items.length > 0;

            if (e.key === 'ArrowDown') {
                if (!hasDropdown) return;
                e.preventDefault();
                activeIndex = setPromoActiveOption(dropdown, activeIndex + 1);
                return;
            }

            if (e.key === 'ArrowUp') {
                if (!hasDropdown) return;
                e.preventDefault();
                activeIndex = setPromoActiveOption(dropdown, activeIndex - 1);
                return;
            }

            if (e.key === 'Enter') {
                e.preventDefault();

                if (hasDropdown) {
                    const item = items[activeIndex >= 0 ? activeIndex : 0];
                    if (item) {
                        setRowProduct(tr, item, null);
                        closeDropdown();
                        updatePreview();
                        return;
                    }
                }

                search(input.value);
                return;
            }

            if (e.key === 'Escape') {
                e.preventDefault();
                closeDropdown();
                return;
            }
        });

        if (initialData?.productName) {
            input.value = initialData.productName;
        }
    }
  


    async function hydrateExistingRuleRow(card, variantId, selectedConversionId) {
        if (!card || !variantId) return;

        try {
            if (urls.getProduct) {
                const res = await fetch(`${urls.getProduct}?variantId=${encodeURIComponent(variantId)}`, {
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });

                const json = await res.json();

                if (json.success && json.data) {
                    setRowProduct(card, json.data, selectedConversionId);

                    // QUAN TRỌNG:
                    // GetProductForPromotion chỉ trả thông tin sản phẩm,
                    // còn danh sách đơn vị phải gọi riêng GetProductUnits.
                    await loadUnitsForRow(card, variantId, selectedConversionId);

                    return;
                }
            }

            await loadUnitsForRow(card, variantId, selectedConversionId);
        } catch {
            await loadUnitsForRow(card, variantId, selectedConversionId);
        }
    }
    function addRuleRow(data) {
        data = data || {};

        const tbody = document.getElementById('promoRuleBody');
        if (!tbody) return;

        const productId = data.productId ?? data.ProductId ?? '';
        const variantId = data.variantId ?? data.VariantId ?? '';
        const conversionId = data.productUnitConversionId ?? data.ProductUnitConversionId ?? '';
        const productName = data.productName ?? data.ProductName ?? data.scopeName ?? data.ScopeName ?? '';
        const qty = data.minQuantity ?? data.MinQuantity ?? data.requiredQuantity ?? data.RequiredQuantity ?? 1;

        const tr = document.createElement('div');
        tr.className = 'promo-rule-card';

        tr.innerHTML = `
        <div class="promo-rule-main">
            <div class="promo-rule-product-area">
                <label class="promo-rule-label">Sản phẩm</label>

                <div class="promo-product-picker">
                    <input class="form-control form-control-sm promo-rule-product-search"
                           data-field="productName"
                           placeholder="Gõ tên sản phẩm hoặc barcode..."
                           autocomplete="off"
                           value="${escapeHtml(productName)}" />

                    <input type="hidden" data-field="productId" value="${escapeHtml(productId)}" />
                    <input type="hidden" data-field="variantId" value="${escapeHtml(variantId)}" />
                </div>

                <div class="promo-selected-product">
                    ${productName ? escapeHtml(productName) : 'Chưa chọn sản phẩm'}
                </div>
            </div>

            <div class="promo-rule-unit-area">
                <label class="promo-rule-label">Đơn vị áp dụng</label>
                <select class="form-select form-select-sm" data-field="unitSelect">
                    <option value="">Tất cả đơn vị / đơn vị gốc</option>
                </select>
                <input type="hidden" data-field="productUnitConversionId" value="${escapeHtml(conversionId)}" />
            </div>

            <div class="promo-rule-qty-area">
                <label class="promo-rule-label">Số lượng</label>
                <input class="form-control form-control-sm"
                       data-field="qty"
                       type="number"
                       min="1"
                       step="1"
                       value="${escapeHtml(qty)}" />
            </div>

            <div class="promo-rule-id-area">
                <label class="promo-rule-label">ID hệ thống</label>
                <div class="promo-rule-system-info" data-role="systemInfo">
                    <div>PID: <b>${productId || '-'}</b></div>
                    <div>VID: <b>${variantId || '-'}</b></div>
                </div>
            </div>

            <div class="promo-rule-action-area">
                <button type="button" class="btn btn-sm btn-label-danger" data-remove-rule title="Xóa dòng">
                    <i class="bx bx-x"></i>
                </button>
            </div>
        </div>
    `;

        tbody.appendChild(tr);

        const input = tr.querySelector('[data-field="productName"]');
        initProductSearchInput(input, {
            productId: productId,
            variantId: variantId,
            productName: productName
        });

        if (variantId) {
            hydrateExistingRuleRow(tr, variantId, conversionId);
        }

        refreshTypePanels();
    }

    function clearForm() {
        setVal('promoId', '0');
        setVal('promoRowVersion', '');

        setVal('promoName', '');
        setVal('promoDescription', '');
        setVal('promoType', '1');
        setVal('promoCustomerTier', '');
        setVal('promoPriority', '0');
        setVal('promoStartAt', nowDateTimeLocal(0));
        setVal('promoEndAt', nowDateTimeLocal(30));
        setChecked('promoIsActive', true);

        setVal('promoDiscountType', '1');
        setVal('promoDiscountValue', '0');
        setVal('promoItemMinQtyDefault', '1');

        setVal('promoComboFixedPrice', '0');
        setVal('promoComboPricingMode', '1');
        setVal('promoComboQuantity', '48');
        setVal('promoComboNote', '');

        setVal('promoBuyQuantity', '10');
        setVal('promoGetQuantity', '1');
        setVal('promoRequireGiftQuantityInCart', 'false');

        const tbody = document.getElementById('promoRuleBody');
        if (tbody) tbody.innerHTML = '';

        refreshTypePanels();
    }

    function openCreate() {
        clearForm();

        const title = document.getElementById('promotionModalTitle');
        if (title) title.textContent = 'Thêm khuyến mãi';

        addRuleRow();
        editModal?.show();
    }

    async function openEdit(id) {
        clearForm();

        const res = await fetch(`${urls.get}?id=${encodeURIComponent(id)}`, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        });

        const json = await res.json();

        if (!json.success) {
            toastError(json.message || 'Không tải được chương trình.');
            return;
        }

        const d = json.data || json.Data;
        if (!d) {
            toastError('Dữ liệu chương trình không hợp lệ.');
            return;
        }

        const title = document.getElementById('promotionModalTitle');
        if (title) title.textContent = 'Sửa khuyến mãi';

        setVal('promoId', d.id ?? d.Id ?? 0);
        setVal('promoRowVersion', d.rowVersion ?? d.RowVersion ?? '');

        setVal('promoName', d.name ?? d.Name ?? '');
        setVal('promoDescription', d.description ?? d.Description ?? '');
        setVal('promoType', d.type ?? d.Type ?? 1);
        setVal('promoCustomerTier', d.customerPriceTier ?? d.CustomerPriceTier ?? '');
        setVal('promoPriority', d.priority ?? d.Priority ?? 0);
        setVal('promoStartAt', toDateTimeLocal(d.startAtUtc ?? d.StartAtUtc));
        setVal('promoEndAt', toDateTimeLocal(d.endAtUtc ?? d.EndAtUtc));
        setChecked('promoIsActive', d.isActive ?? d.IsActive ?? true);

        setVal('promoDiscountType', d.discountType ?? d.DiscountType ?? 1);
        setVal('promoDiscountValue', d.discountValue ?? d.DiscountValue ?? 0);

        setVal('promoComboFixedPrice', d.comboFixedPrice ?? d.ComboFixedPrice ?? 0);
        setVal('promoComboPricingMode', d.comboPricingMode ?? d.ComboPricingMode ?? 1);
        setVal('promoComboQuantity', d.comboQuantity ?? d.ComboQuantity ?? 48);
        setVal('promoComboNote', d.comboNote ?? d.ComboNote ?? '');

        setVal('promoBuyQuantity', d.buyQuantity ?? d.BuyQuantity ?? 10);
        setVal('promoGetQuantity', d.getQuantity ?? d.GetQuantity ?? 1);
        setVal('promoRequireGiftQuantityInCart', String(d.requireGiftQuantityInCart ?? d.RequireGiftQuantityInCart ?? true));

        const type = Number(d.type ?? d.Type ?? 1);
        const rows = type === 2
            ? (d.comboRules ?? d.ComboRules ?? [])
            : (d.items ?? d.Items ?? []);

        const tbody = document.getElementById('promoRuleBody');
        if (tbody) tbody.innerHTML = '';

        rows.forEach(addRuleRow);

        if (rows.length === 0) addRuleRow();

        refreshTypePanels();
        editModal?.show();
    }

    function readRuleRows(type) {
        const rows = [];

        document.querySelectorAll('#promoRuleBody .promo-rule-card').forEach(card => {
            const get = field => card.querySelector(`[data-field="${field}"]`)?.value ?? '';

            const productId = toNumber(get('productId'));
            if (productId <= 0) return;

            const variantId = toNumber(get('variantId'));
            const conversionId = toNumber(get('productUnitConversionId'));
            const qty = toNumber(get('qty')) || 1;

            if (type === 2) {
                rows.push({
                    productId: productId,
                    variantId: variantId > 0 ? variantId : null,
                    productUnitConversionId: !isMixedQuantity() && conversionId > 0 ? conversionId : null,
                    requiredQuantity: isMixedQuantity() ? 1 : qty
                });
            } else {
                rows.push({
                    productId: productId,
                    variantId: variantId > 0 ? variantId : null,
                    productUnitConversionId: conversionId > 0 ? conversionId : null,
                    minQuantity: qty
                });
            }
        });

        return rows;
    }

    function buildRequest() {
        const type = Number(val('promoType') || 1);

        const items = type === 2 ? [] : readRuleRows(type);
        const comboRules = type === 2 ? readRuleRows(type) : [];

        return {
            id: toNumber(val('promoId')),
            name: val('promoName').trim(),
            description: val('promoDescription').trim() || null,
            type: type,
            discountType: toNumber(val('promoDiscountType') || 1),
            discountValue: toNumber(val('promoDiscountValue')),
            comboFixedPrice: type === 2 ? toNumber(val('promoComboFixedPrice')) : null,
            comboPricingMode: type === 2 ? toNumber(val('promoComboPricingMode') || 1) : 1,
            comboQuantity: isMixedQuantity() ? toNumber(val('promoComboQuantity')) : null,
            comboNote: type === 2 ? (val('promoComboNote').trim() || null) : null,
            buyQuantity: type === 3 ? toNumber(val('promoBuyQuantity')) : null,
            getQuantity: type === 3 ? toNumber(val('promoGetQuantity')) : null,
            requireGiftQuantityInCart: val('promoRequireGiftQuantityInCart') === 'true',
            startAtUtc: new Date(val('promoStartAt')).toISOString(),
            endAtUtc: new Date(val('promoEndAt')).toISOString(),
            isActive: getChecked('promoIsActive'),
            priority: toNumber(val('promoPriority')),
            customerPriceTier: val('promoCustomerTier') || null,
            rowVersion: val('promoRowVersion') || null,
            items: items,
            comboRules: comboRules
        };
    }

    function validateRequest(req) {
        if (!req.name) return 'Vui lòng nhập tên chương trình.';

        if (!req.startAtUtc || !req.endAtUtc) return 'Vui lòng chọn thời gian áp dụng.';

        if (new Date(req.endAtUtc) <= new Date(req.startAtUtc)) {
            return 'Ngày kết thúc phải lớn hơn ngày bắt đầu.';
        }

        if (req.type === 1) {
            if (req.discountValue <= 0) return 'Giá trị giảm phải lớn hơn 0.';
            if (!req.items.length) return 'Vui lòng thêm ít nhất 1 sản phẩm áp dụng.';
        }

        if (req.type === 2) {
            if ((req.comboFixedPrice || 0) <= 0) return 'Giá combo phải lớn hơn 0.';
            if (req.comboRules.length < 2) return 'Combo phải có ít nhất 2 sản phẩm.';
            if (req.comboPricingMode === 2) {
                if (!(req.comboQuantity > 0)) return 'Số lượng gốc mỗi thùng phải lớn hơn 0.';
                const variants = req.comboRules.map(row => row.variantId);
                if (variants.some(id => !id) || new Set(variants).size !== variants.length) {
                    return 'Chọn ít nhất 2 mã hàng khác nhau để ghép vị.';
                }
            }
        }

        if (req.type === 3) {
            if ((req.buyQuantity || 0) <= 0 || (req.getQuantity || 0) <= 0) {
                return 'Số lượng mua và số lượng tặng phải lớn hơn 0.';
            }

            if (!req.items.length) return 'Vui lòng thêm sản phẩm áp dụng.';
        }

        return '';
    }

    async function savePromotion() {
        const req = buildRequest();
        const error = validateRequest(req);

        if (error) {
            toastError(error);
            return;
        }

        const btn = document.getElementById('btnSavePromotion');
        if (btn) btn.disabled = true;

        try {
            const res = await fetch(urls.save, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token(),
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: JSON.stringify(req)
            });

            const json = await res.json();

            if (!json.success) {
                toastError(json.message || 'Lưu thất bại.');
                return;
            }

            editModal?.hide();
            toastSuccess(json.message || 'Đã lưu chương trình.');
            await reloadTable();
        } finally {
            if (btn) btn.disabled = false;
        }
    }

    function currentQuery(page) {
        const params = new URLSearchParams();

        const type = val('ddlType');
        const isActive = val('ddlIsActive');
        const tier = val('ddlCustomerTier');
        const search = val('txtSearch');
        const pageSize = val('ddlPageSize') || '20';

        if (type) params.set('type', type);
        if (isActive) params.set('isActive', isActive);
        if (tier) params.set('customerPriceTier', tier);
        if (search) params.set('search', search);

        params.set('page', String(page || 1));
        params.set('pageSize', pageSize);

        return params;
    }

    async function reloadTable(page) {
        const wrapper = document.getElementById('promotionTableWrapper');
        if (!wrapper) return;

        wrapper.classList.add('opacity-50');

        try {
            const res = await fetch(`${urls.search}?${currentQuery(page).toString()}`, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });

            wrapper.innerHTML = await res.text();
        } finally {
            wrapper.classList.remove('opacity-50');
        }
    }

    async function postSimple(url, id, successMessage) {
        const res = await fetch(`${url}?id=${encodeURIComponent(id)}`, {
            method: 'POST',
            headers: {
                'RequestVerificationToken': token(),
                'X-Requested-With': 'XMLHttpRequest'
            }
        });

        const json = await res.json();

        if (!json.success) {
            toastError(json.message || 'Thao tác thất bại.');
            return;
        }

        toastSuccess(json.message || successMessage);
        await reloadTable();
    }

    let searchTimer = null;

    document.addEventListener('click', async function (e) {
        const createBtn = e.target.closest('#btnOpenCreatePromotion');
        if (createBtn) {
            openCreate();
            return;
        }

        const addRuleBtn = e.target.closest('#btnAddPromoRule');
        if (addRuleBtn) {
            addRuleRow();
            return;
        }

        const removeRuleBtn = e.target.closest('[data-remove-rule]');
        if (removeRuleBtn) {
            removeRuleBtn.closest('.promo-rule-card')?.remove();
            updatePreview();
            return;
        }

       

        const saveBtn = e.target.closest('#btnSavePromotion');
        if (saveBtn) {
            await savePromotion();
            return;
        }

        const editBtn = e.target.closest('[data-promotion-edit]');
        if (editBtn) {
            await openEdit(editBtn.dataset.promotionEdit);
            return;
        }

        const toggleBtn = e.target.closest('[data-promotion-toggle]');
        if (toggleBtn) {
            const ok = await confirmPopup('Bạn muốn đổi trạng thái chương trình này?');
            if (!ok) return;

            await postSimple(urls.toggle, toggleBtn.dataset.promotionToggle, 'Đã cập nhật trạng thái.');
            return;
        }

        const duplicateBtn = e.target.closest('[data-promotion-duplicate]');
        if (duplicateBtn) {
            const ok = await confirmPopup('Nhân bản chương trình này? Bản sao sẽ ở trạng thái khóa.');
            if (!ok) return;

            await postSimple(urls.duplicate, duplicateBtn.dataset.promotionDuplicate, 'Đã nhân bản chương trình.');
            return;
        }

        const deleteBtn = e.target.closest('[data-promotion-delete]');
        if (deleteBtn) {
            setVal('promoDeleteId', deleteBtn.dataset.promotionDelete);
            const nameEl = document.getElementById('promoDeleteName');
            if (nameEl) nameEl.textContent = deleteBtn.dataset.promotionName || '';
            deleteModal?.show();
            return;
        }

        const confirmDeleteBtn = e.target.closest('#btnConfirmDeletePromotion');
        if (confirmDeleteBtn) {
            const id = val('promoDeleteId');
            deleteModal?.hide();
            await postSimple(urls.delete, id, 'Đã xóa chương trình.');
            return;
        }

        const pageBtn = e.target.closest('[data-promo-page]');
        if (pageBtn && !pageBtn.disabled) {
            await reloadTable(pageBtn.dataset.promoPage);
            return;
        }

        const reloadBtn = e.target.closest('#btnReload');
        if (reloadBtn) {
            await reloadTable(1);
            return;
        }

        const clearBtn = e.target.closest('#btnClearSearch');
        if (clearBtn) {
            setVal('txtSearch', '');
            clearBtn.classList.add('d-none');
            await reloadTable(1);
            return;
        }
    });

    document.addEventListener('change', function (e) {
        if (e.target.matches('#promoComboPricingMode')) {
            refreshTypePanels();
            return;
        }

        if (e.target.matches('#promoType')) {
            refreshTypePanels();
            return;
        }

        if (e.target.matches('[data-field="unitSelect"]')) {
            const card = e.target.closest('.promo-rule-card');
            const hidden = card?.querySelector('[data-field="productUnitConversionId"]');
            if (hidden) hidden.value = e.target.value || '';
            return;
        }

        if (e.target.matches('#ddlType, #ddlIsActive, #ddlCustomerTier, #ddlPageSize')) {
            reloadTable(1);
            return;
        }

        if (e.target.closest('#promotionEditModal')) {
            updatePreview();
        }
    });

    document.addEventListener('input', function (e) {
        if (e.target.matches('#txtSearch')) {
            const clearBtn = document.getElementById('btnClearSearch');
            if (clearBtn) clearBtn.classList.toggle('d-none', !e.target.value);

            clearTimeout(searchTimer);
            searchTimer = setTimeout(() => reloadTable(1), 350);
            return;
        }

        if (e.target.closest('#promotionEditModal')) {
            updatePreview();
        }
    });

    document.addEventListener('click', function (e) {
        if (!e.target.closest('.promo-product-picker')) {
            document.querySelectorAll('.promo-ac-dropdown').forEach(x => x.remove());
        }
    });
})();
