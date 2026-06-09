window.GaoVariantUnitConversion = (() => {
    const state = {
        currentVariantId: 0,
        currentVariantSku: '',
        currentVariantName: '',
        currentConversionId: 0,
        currentBarcodeId: 0,
        conversions: []
    };

    function byId(id) {
        return document.getElementById(id);
    }

    function token() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    function setValue(id, value) {
        const el = byId(id);
        if (!el) return;

        if ('value' in el) el.value = value ?? '';
        else el.textContent = value ?? '';
    }

    function setText(id, value) {
        const el = byId(id);
        if (el) el.textContent = value ?? '';
    }

    function setHtml(id, value) {
        const el = byId(id);
        if (el) el.innerHTML = value ?? '';
    }

    function setChecked(id, value) {
        const el = byId(id);
        if (el) el.checked = !!value;
    }

    function escapeHtml(str) {
        return (str ?? '')
            .toString()
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    function money(v) {
        if (v === null || v === undefined || v === '') return '—';
        const n = Number(v);
        if (!Number.isFinite(n)) return '—';
        return n.toLocaleString('vi-VN');
    }

    function parseMoneyInput(value) {
        const raw = String(value ?? '')
            .replaceAll('.', '')
            .replaceAll(',', '')
            .trim();

        if (!raw) return null;

        const n = Number(raw);
        return Number.isFinite(n) ? n : null;
    }

    function formatDateTime(value) {
        if (!value) return '';
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return value;
        return d.toLocaleString('vi-VN');
    }

    function normalizeBarcodeType(value) {
        if (value === null || value === undefined || value === '') return 0;
        if (typeof value === 'number') return value;

        const text = String(value).trim().toLowerCase();

        if (text === 'internal') return 0;
        if (text === 'external') return 1;
        if (text === 'supplier') return 2;
        if (text === 'packaging') return 3;
        if (text === 'legacy') return 4;

        const num = Number(text);
        return Number.isNaN(num) ? 0 : num;
    }

    function barcodeTypeText(value) {
        switch (normalizeBarcodeType(value)) {
            case 0: return 'Nội bộ';
            case 1: return 'Ngoài bao bì';
            case 2: return 'Nhà cung cấp';
            case 3: return 'Đóng gói';
            case 4: return 'Dữ liệu cũ';
            default: return String(value ?? '');
        }
    }

    async function getJson(url) {
        const res = await fetch(url, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin'
        });

        return await res.json();
    }

    async function postJson(url, data) {
        const res = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token(),
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify(data),
            credentials: 'same-origin'
        });

        return await res.json();
    }

    function getCurrentConversion() {
        return state.conversions.find(x => Number(x.id) === Number(state.currentConversionId)) || null;
    }

    function renderSummary() {
        const base = state.conversions.find(x => x.isBaseUnit);
        const def = state.conversions.find(x => x.isDefaultForSale);

        const barcodeCount = state.conversions.reduce((sum, x) => {
            return sum + (Array.isArray(x.barcodes) ? x.barcodes.length : 0);
        }, 0);

        setText('ucxBaseUnitText', base?.unitName || '—');
        setText('ucxDefaultUnitText', def?.unitName || '—');
        setText('ucxConversionCount', String(state.conversions.length || 0));
        setText('ucxBarcodeCount', String(barcodeCount || 0));
    }

    function closePanels() {
        window.GaoUcxView?.closePanels?.();
    }

    function openConversionPanel() {
        window.GaoUcxView?.openConversionPanel?.();
    }

    function openBarcodePanel() {
        window.GaoUcxView?.openBarcodePanel?.();
    }

    function resetConversionForm(clearSelected = false) {
        setValue('uc_Id', '');
        setValue('uc_UnitId', '');
        setValue('uc_Factor', '');
        setValue('uc_Price', '');
        setValue('uc_WholesalePrice', '');
        setValue('uc_SortOrder', '0');
        setChecked('uc_IsBaseUnit', false);
        setChecked('uc_IsDefaultForSale', false);
        setChecked('uc_IsActive', true);

        if (clearSelected) {
            state.currentConversionId = 0;
            setText('uc_SelectedHint', 'Chưa chọn dòng nào');
        }
    }

    function resetBarcodeForm(clearSelected = false) {
        const conversion = getCurrentConversion();

        setValue('bc_Id', '');
        setValue('bc_ProductUnitConversionId', conversion?.id || '');
        setValue('bc_ConversionName', conversion ? `${conversion.unitName} (x${conversion.factor})` : 'Chọn một dòng quy đổi');
        setValue('bc_Barcode', '');
        setValue('bc_BarcodeType', '0');
        setChecked('bc_IsPrimary', true);
        setChecked('bc_IsActive', true);
        setValue('bc_Note', '');

        if (clearSelected) {
            state.currentBarcodeId = 0;
            setText('bc_SelectedHint', 'Chưa chọn barcode nào');
        }
    }

    function fillConversionForm(item) {
        setValue('uc_Id', item.id || '');
        setValue('uc_ProductVariantId', item.productVariantId || state.currentVariantId || '');
        setValue('uc_UnitId', item.unitId || '');
        setValue('uc_Factor', item.factor || '');
        setValue('uc_Price', item.price ?? '');
        setValue('uc_WholesalePrice', item.wholesalePrice ?? '');
        setValue('uc_SortOrder', item.sortOrder ?? 0);
        setChecked('uc_IsBaseUnit', !!item.isBaseUnit);
        setChecked('uc_IsDefaultForSale', !!item.isDefaultForSale);
        setChecked('uc_IsActive', !!item.isActive);

        setText('uc_SelectedHint', `${item.unitName} • x${item.factor}`);
        state.currentConversionId = Number(item.id || 0);
    }

    function fillBarcodeForm(item, conversion) {
        setValue('bc_Id', item?.id || '');
        setValue('bc_ProductUnitConversionId', conversion?.id || '');
        setValue('bc_ConversionName', conversion ? `${conversion.unitName} (x${conversion.factor})` : 'Chọn một dòng quy đổi');
        setValue('bc_Barcode', item?.barcode || '');
        setValue('bc_BarcodeType', String(normalizeBarcodeType(item?.barcodeType)));
        setChecked('bc_IsPrimary', item ? !!item.isPrimary : true);
        setChecked('bc_IsActive', item ? !!item.isActive : true);
        setValue('bc_Note', item?.note || '');

        state.currentBarcodeId = Number(item?.id || 0);
        setText('bc_SelectedHint', item ? item.barcode : 'Chưa chọn barcode nào');
    }

    function historyBadgeClass(actionType) {
        const key = String(actionType || '').trim().toLowerCase();

        switch (key) {
            case 'assigned': return 'ucx-action-assigned';
            case 'replaced': return 'ucx-action-replaced';
            case 'deactivated': return 'ucx-action-deactivated';
            case 'reactivated': return 'ucx-action-reactivated';
            case 'imported': return 'ucx-action-imported';
            default: return 'ucx-action-assigned';
        }
    }

    function renderHistoryCode(value) {
        if (!value) return `<div class="ucx-history-code is-empty">—</div>`;
        return `<div class="ucx-history-code">${escapeHtml(value)}</div>`;
    }

    function renderBarcodeHistory(items) {
        const wrap = byId('barcodeHistoryWrap');
        if (!wrap) return;

        if (!items || !items.length) {
            wrap.innerHTML = `<div class="ucx-history-empty">Chưa có lịch sử barcode</div>`;
            return;
        }

        wrap.innerHTML = `
            <div class="ucx-history-scroll">
                <div class="ucx-history-list">
                    ${items.map(item => `
                        <div class="ucx-history-card">
                            <div class="ucx-history-card-head">
                                <span class="ucx-action-badge ${historyBadgeClass(item.actionType)}">
                                    ${escapeHtml(item.actionTypeText || item.actionType || '')}
                                </span>
                                <div class="text-muted fw-bold">
                                    ${escapeHtml(formatDateTime(item.changedAtUtc))}
                                </div>
                            </div>

                            <div class="ucx-history-card-body">
                                <div class="ucx-history-main">
                                    <div class="ucx-history-codebox">
                                        <div class="ucx-history-codebox-label">Mã cũ</div>
                                        ${renderHistoryCode(item.oldBarcode)}
                                    </div>

                                    <div class="ucx-history-codebox">
                                        <div class="ucx-history-codebox-label">Mã mới</div>
                                        ${renderHistoryCode(item.newBarcode)}
                                    </div>
                                </div>

                                <div class="ucx-history-meta">
                                    <div class="ucx-history-meta-row">
                                        <div class="ucx-history-meta-key">Lý do</div>
                                        <div class="ucx-history-meta-value">${escapeHtml(item.reason || '—')}</div>
                                    </div>

                                    <div class="ucx-history-meta-row">
                                        <div class="ucx-history-meta-key">Người đổi</div>
                                        <div class="ucx-history-meta-value">${escapeHtml(item.changedByUserName || '—')}</div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    `).join('')}
                </div>
            </div>
        `;
    }

    async function loadBarcodeHistory() {
        const wrap = byId('barcodeHistoryWrap');
        if (!wrap) return;

        const conversion = getCurrentConversion();

        if (!conversion) {
            wrap.innerHTML = `<div class="ucx-history-empty">Chọn một dòng đơn vị để xem lịch sử.</div>`;
            return;
        }

        wrap.innerHTML = `<div class="ucx-history-empty">Đang tải lịch sử barcode...</div>`;

        try {
            const json = await getJson(`/Admin/ProductUnitConversion/GetBarcodeHistoryByConversionId?productUnitConversionId=${conversion.id}&take=20`);

            if (!json.ok) {
                wrap.innerHTML = `<div class="ucx-history-empty">${escapeHtml(json.message || 'Không tải được lịch sử barcode.')}</div>`;
                return;
            }

            renderBarcodeHistory(json.data || []);
        } catch (error) {
            console.error(error);
            wrap.innerHTML = `<div class="ucx-history-empty">Không tải được lịch sử barcode.</div>`;
        }
    }

    function renderBarcodes() {
        const wrap = byId('barcodeListWrap');
        if (!wrap) return;

        const conversion = getCurrentConversion();

        if (!conversion) {
            wrap.innerHTML = `<div class="bc-empty">Chọn một dòng đơn vị để xem barcode phụ.</div>`;
            resetBarcodeForm(true);
            renderBarcodeHistory([]);
            return;
        }

        const barcodes = Array.isArray(conversion.barcodes) ? conversion.barcodes : [];

        if (!barcodes.length) {
            wrap.innerHTML = `
                <div class="bc-empty">
                    Đơn vị <b>${escapeHtml(conversion.unitName)}</b> chưa có barcode.
                    <div class="mt-2">
                        <button type="button" class="btn btn-sm btn-outline-primary js-add-barcode-inline">
                            + Thêm barcode
                        </button>
                    </div>
                </div>
            `;

            fillBarcodeForm(null, conversion);
            loadBarcodeHistory();

            wrap.querySelector('.js-add-barcode-inline')?.addEventListener('click', function () {
                resetBarcodeForm(false);
                openBarcodePanel();
            });

            return;
        }

        if (!state.currentBarcodeId || !barcodes.some(x => Number(x.id) === Number(state.currentBarcodeId))) {
            state.currentBarcodeId = Number(barcodes[0].id || 0);
        }

        wrap.innerHTML = `
            <div class="bc-table-wrap">
                <table class="table table-bordered align-middle bc-table">
                    <thead>
                        <tr>
                            <th>Barcode</th>
                            <th>Loại</th>
                            <th>Chính</th>
                            <th>Trạng thái</th>
                            <th>Ghi chú</th>
                            <th class="text-end">Thao tác</th>
                        </tr>
                    </thead>

                    <tbody>
                        ${barcodes.map(b => `
                            <tr class="bc-click-row ${Number(state.currentBarcodeId) === Number(b.id) ? 'is-selected' : ''}"
                                data-barcode-id="${b.id}">
                                <td class="ucx-code">${escapeHtml(b.barcode)}</td>
                              <td>${escapeHtml(b.barcodeTypeText || barcodeTypeText(b.barcodeType))}</td>
                                <td>${b.isPrimary ? '<span class="badge bg-primary">Chính</span>' : '—'}</td>
                                <td>${b.isActive ? '<span class="badge bg-success">Hoạt động</span>' : '<span class="badge bg-secondary">Ngưng</span>'}</td>
                                <td>${escapeHtml(b.note || '')}</td>
                                <td class="text-end">
                                    <button type="button"
                                            class="btn btn-sm btn-outline-primary js-edit-barcode"
                                            data-barcode-id="${b.id}">
                                        Sửa
                                    </button>
                                </td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        `;

        const currentBarcode = barcodes.find(x => Number(x.id) === Number(state.currentBarcodeId));
        fillBarcodeForm(currentBarcode || null, conversion);

        wrap.querySelectorAll('.bc-click-row').forEach(row => {
            row.addEventListener('click', function () {
                const barcodeId = Number(row.dataset.barcodeId || 0);
                state.currentBarcodeId = barcodeId;

                wrap.querySelectorAll('.bc-click-row').forEach(x => x.classList.remove('is-selected'));
                row.classList.add('is-selected');

                const found = barcodes.find(x => Number(x.id) === barcodeId);
                fillBarcodeForm(found || null, conversion);
            });
        });

        wrap.querySelectorAll('.js-edit-barcode').forEach(btn => {
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();

                const barcodeId = Number(btn.dataset.barcodeId || 0);
                const found = barcodes.find(x => Number(x.id) === barcodeId);
                if (!found) return;

                state.currentBarcodeId = barcodeId;
                fillBarcodeForm(found, conversion);
                openBarcodePanel();
            });
        });

        loadBarcodeHistory();
    }

    function renderConversions() {
        const wrap = byId('conversionListWrap');
        if (!wrap) return;

        renderSummary();

        if (!state.conversions.length) {
            wrap.innerHTML = `<div class="uc-empty">Biến thể này chưa có quy đổi đơn vị nào.</div>`;
            setHtml('barcodeListWrap', `<div class="bc-empty">Chọn một dòng đơn vị để xem barcode phụ.</div>`);
            setHtml('barcodeHistoryWrap', `<div class="ucx-history-empty">Chọn một dòng đơn vị để xem lịch sử.</div>`);
            resetConversionForm(true);
            resetBarcodeForm(true);
            return;
        }

        if (!state.currentConversionId || !state.conversions.some(x => Number(x.id) === Number(state.currentConversionId))) {
            state.currentConversionId = Number(state.conversions[0].id || 0);
        }

        wrap.innerHTML = `
            <div class="uc-table-wrap">
                <table class="table table-bordered align-middle uc-table">
                    <thead>
                        <tr>
                            <th>Đơn vị</th>
                            <th>Factor</th>
                            <th>Giá lẻ</th>
                            <th>Giá sỉ</th>
                            <th>Gốc</th>
                            <th>Mặc định</th>
                            <th>Trạng thái</th>
                            <th>Barcode</th>
                            <th class="text-end">Thao tác</th>
                        </tr>
                    </thead>

                    <tbody>
                        ${state.conversions.map(x => `
                            <tr class="uc-click-row ${Number(state.currentConversionId) === Number(x.id) ? 'is-selected' : ''}"
                                data-id="${x.id}">
                                <td class="fw-bold">${escapeHtml(x.unitName)}</td>
                                <td>x${escapeHtml(String(x.factor))}</td>

                                <td>
                                    <button type="button"
                                            class="btn btn-link p-0 uc-money js-quick-price"
                                            data-id="${x.id}"
                                            data-field="price">
                                        ${money(x.price)}
                                    </button>
                                </td>

                                <td>
                                    <button type="button"
                                            class="btn btn-link p-0 uc-money js-quick-price"
                                            data-id="${x.id}"
                                            data-field="wholesalePrice">
                                        ${money(x.wholesalePrice)}
                                    </button>
                                </td>

                                <td>${x.isBaseUnit ? '<span class="badge bg-info text-dark">Gốc</span>' : '—'}</td>
                                <td>${x.isDefaultForSale ? '<span class="badge bg-primary">Mặc định</span>' : '—'}</td>
                                <td>${x.isActive ? '<span class="badge bg-success">Hoạt động</span>' : '<span class="badge bg-secondary">Ngưng</span>'}</td>
                                <td>${x.barcodes?.length || 0}</td>

                                <td class="text-end">
                                  <button type="button"
        class="btn btn-sm btn-outline-success js-quick-edit-price"
        data-id="${x.id}">
    Sửa giá
</button>

<button type="button"
        class="btn btn-sm btn-outline-primary js-edit-conversion"
        data-id="${x.id}">
    Sửa đầy đủ
</button>
                                </td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        `;

        const current = state.conversions.find(x => Number(x.id) === Number(state.currentConversionId));
        if (current) fillConversionForm(current);

        wrap.querySelectorAll('.uc-click-row').forEach(row => {
            row.addEventListener('click', function () {
                const id = Number(row.dataset.id || 0);
                state.currentConversionId = id;
                state.currentBarcodeId = 0;

                wrap.querySelectorAll('.uc-click-row').forEach(x => x.classList.remove('is-selected'));
                row.classList.add('is-selected');

                const found = state.conversions.find(x => Number(x.id) === id);
                if (found) {
                    fillConversionForm(found);
                    renderBarcodes();
                }
            });
        });

        wrap.querySelectorAll('.js-edit-conversion').forEach(btn => {
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();

                const id = Number(btn.dataset.id || 0);
                const found = state.conversions.find(x => Number(x.id) === id);
                if (!found) return;

                state.currentConversionId = id;
                fillConversionForm(found);
                openConversionPanel();
            });
        });
        wrap.querySelectorAll('.js-quick-edit-price').forEach(btn => {
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();

                const id = Number(btn.dataset.id || 0);
                const found = state.conversions.find(x => Number(x.id) === id);
                if (!found) return;

                state.currentConversionId = id;

                fillConversionForm(found);

                document.getElementById('ucxEditorTitle').textContent =
                    `Sửa giá ${found.unitName}`;

                window.GaoUcxView?.openConversionPanel?.();

                setTimeout(() => {
                    document.getElementById('uc_Price')?.focus();
                    document.getElementById('uc_Price')?.select();
                }, 120);
            });
        });
        wrap.querySelectorAll('.js-quick-price').forEach(btn => {
            btn.addEventListener('click', async function (e) {
                e.preventDefault();
                e.stopPropagation();

                const id = Number(btn.dataset.id || 0);
                const field = btn.dataset.field;
                const found = state.conversions.find(x => Number(x.id) === id);

                if (!found) return;

                const label = field === 'price' ? 'giá lẻ' : 'giá sỉ';
                const oldValue = found[field] ?? '';

                const input = prompt(`Nhập ${label} mới cho đơn vị ${found.unitName}:`, oldValue ?? '');
                if (input === null) return;

                const newValue = parseMoneyInput(input);

                if (newValue === null || newValue < 0) {
                    toastr?.error('Giá không hợp lệ.');
                    return;
                }

                const dto = {
                    id: found.id,
                    productVariantId: state.currentVariantId,
                    unitId: found.unitId,
                    factor: found.factor,
                    price: field === 'price' ? newValue : found.price,
                    wholesalePrice: field === 'wholesalePrice' ? newValue : found.wholesalePrice,
                    sortOrder: found.sortOrder ?? 0,
                    isBaseUnit: !!found.isBaseUnit,
                    isDefaultForSale: !!found.isDefaultForSale,
                    isActive: !!found.isActive
                };

                try {
                    const json = await postJson('/Admin/ProductUnitConversion/SaveConversion', dto);

                    if (!json.ok) {
                        toastr?.error(json.message || 'Cập nhật giá thất bại.');
                        return;
                    }

                    toastr?.success('Đã cập nhật giá.');
                    state.currentConversionId = found.id;
                    await loadConversions();
                } catch (error) {
                    console.error(error);
                    toastr?.error('Cập nhật giá thất bại.');
                }
            });
        });

        renderBarcodes();
    }

    async function loadConversions() {
        if (!state.currentVariantId) return;

        try {
            const json = await getJson(`/Admin/ProductUnitConversion/GetByVariantId?productVariantId=${state.currentVariantId}`);

            if (!json.ok) {
                toastr?.error(json.message || 'Không tải được danh sách quy đổi.');
                return;
            }

            state.conversions = json.data || [];
            renderConversions();
        } catch (error) {
            console.error(error);
            toastr?.error('Không tải được danh sách quy đổi.');
        }
    }

    async function saveConversion() {
        const dto = {
            id: byId('uc_Id')?.value ? Number(byId('uc_Id').value) : null,
            productVariantId: state.currentVariantId,
            unitId: byId('uc_UnitId')?.value ? Number(byId('uc_UnitId').value) : 0,
            factor: byId('uc_Factor')?.value ? Number(byId('uc_Factor').value) : 0,
            price: byId('uc_Price')?.value ? Number(byId('uc_Price').value) : null,
            wholesalePrice: byId('uc_WholesalePrice')?.value ? Number(byId('uc_WholesalePrice').value) : null,
            sortOrder: byId('uc_SortOrder')?.value ? Number(byId('uc_SortOrder').value) : 0,
            isBaseUnit: !!byId('uc_IsBaseUnit')?.checked,
            isDefaultForSale: !!byId('uc_IsDefaultForSale')?.checked,
            isActive: !!byId('uc_IsActive')?.checked
        };

        if (!dto.unitId) {
            toastr?.error('Vui lòng chọn đơn vị.');
            byId('uc_UnitId')?.focus();
            return;
        }

        if (!dto.factor || dto.factor <= 0) {
            toastr?.error('Factor không hợp lệ.');
            byId('uc_Factor')?.focus();
            return;
        }

        try {
            const json = await postJson('/Admin/ProductUnitConversion/SaveConversion', dto);

            if (!json.ok) {
                toastr?.error(json.message || 'Lưu quy đổi thất bại.');
                return;
            }

            toastr?.success(json.message || 'Lưu quy đổi thành công.');
            state.currentConversionId = Number(json.id || dto.id || state.currentConversionId || 0);

            closePanels();
            await loadConversions();
        } catch (error) {
            console.error(error);
            toastr?.error('Lưu quy đổi thất bại.');
        }
    }

    async function saveBarcode() {
        const conversion = getCurrentConversion();

        if (!conversion) {
            toastr?.warning('Hãy chọn một dòng đơn vị trước.');
            return;
        }

        const rawBarcode = (byId('bc_Barcode')?.value || '').trim();

        let barcodeType = byId('bc_BarcodeType')?.value
            ? Number(byId('bc_BarcodeType').value)
            : 0;

        if (!rawBarcode) {
            barcodeType = 0;
            setValue('bc_BarcodeType', '0');
        }

        const isPrimary = !!byId('bc_IsPrimary')?.checked;
        let isActive = !!byId('bc_IsActive')?.checked;

        if (isPrimary && !isActive) {
            isActive = true;
            setChecked('bc_IsActive', true);
        }

        const dto = {
            id: byId('bc_Id')?.value ? Number(byId('bc_Id').value) : null,
            productUnitConversionId: conversion.id,
            barcode: rawBarcode,
            barcodeType: barcodeType,
            isPrimary: isPrimary,
            isActive: isActive,
            note: byId('bc_Note')?.value?.trim() || ''
        };

        try {
            const json = await postJson('/Admin/ProductUnitConversion/SaveBarcode', dto);

            if (!json.ok) {
                toastr?.error(json.message || 'Lưu barcode thất bại.');
                return;
            }

            toastr?.success(json.message || 'Lưu barcode thành công.');
            state.currentBarcodeId = Number(json.id || dto.id || state.currentBarcodeId || 0);

            closePanels();
            await loadConversions();
        } catch (error) {
            console.error(error);
            toastr?.error('Lưu barcode thất bại.');
        }
    }

    function open(variant) {
        state.currentVariantId = Number(variant.id || 0);
        state.currentVariantSku = variant.sku || '';
        state.currentVariantName = variant.name || '';
        state.currentConversionId = 0;
        state.currentBarcodeId = 0;
        state.conversions = [];

        setText('unitConvVariantName', variant.name || '(Chưa có tên biến thể)');

        const infoEl = byId('unitConvVariantInfo');
        if (infoEl) {
            infoEl.innerHTML = `
                Variant ID: <b>${state.currentVariantId}</b>
                • SKU: <b>${escapeHtml(state.currentVariantSku || '(trống)')}</b>
            `;
        }

        setValue('uc_ProductVariantId', state.currentVariantId);
        resetConversionForm(true);
        resetBarcodeForm(true);
        renderSummary();

        setHtml('conversionListWrap', `<div class="uc-empty">Đang tải danh sách quy đổi...</div>`);
        setHtml('barcodeListWrap', `<div class="bc-empty">Chọn một dòng đơn vị để xem barcode phụ.</div>`);
        setHtml('barcodeHistoryWrap', `<div class="ucx-history-empty">Chọn một dòng đơn vị để xem lịch sử.</div>`);

        const modalEl = byId('mdlVariantUnitConversions');

        if (modalEl && window.bootstrap && bootstrap.Modal) {
            bootstrap.Modal.getOrCreateInstance(modalEl, {
                backdrop: 'static',
                keyboard: true
            }).show();
        }

        loadConversions();
    }

    function bind() {
        byId('btnSaveConversion')?.addEventListener('click', saveConversion);
        byId('btnSaveBarcode')?.addEventListener('click', saveBarcode);
        byId('btnReloadConversions')?.addEventListener('click', loadConversions);
        byId('btnReloadBarcodeHistory')?.addEventListener('click', loadBarcodeHistory);

        byId('btnOpenBarcodeEditor')?.addEventListener('click', function () {
            const conversion = getCurrentConversion();

            if (!conversion) {
                toastr?.warning('Hãy chọn một dòng đơn vị trước.');
                return;
            }

            resetBarcodeForm(false);
            openBarcodePanel();
        });

        byId('btnResetConversionForm')?.addEventListener('click', function () {
            resetConversionForm(false);
            closePanels();
        });

        byId('btnResetBarcodeForm')?.addEventListener('click', function () {
            resetBarcodeForm(false);
            closePanels();
        });
        byId('bc_Barcode')?.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                saveBarcode();
            }
        });
        ['uc_UnitId', 'uc_Factor', 'uc_Price', 'uc_WholesalePrice'].forEach(id => {
            byId(id)?.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    saveConversion();
                }
            });
        });
    }

    return {
        bind,
        open,
        reload: loadConversions
    };
})();