window.GaoVariantUnitConversion = (() => {
    const state = {
        currentVariantId: 0,
        currentVariantSku: '',
        currentVariantName: '',
        currentConversionId: 0,
        currentBarcodeId: 0,
        conversions: []
    };

    function token() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    function byId(id) {
        return document.getElementById(id);
    }

    function setValue(id, value) {
        const el = byId(id);
        if (el) el.value = value ?? '';
    }

    function setChecked(id, value) {
        const el = byId(id);
        if (el) el.checked = !!value;
    }

    function setText(id, value) {
        const el = byId(id);
        if (el) el.textContent = value ?? '';
    }

    function setHtml(id, value) {
        const el = byId(id);
        if (el) el.innerHTML = value ?? '';
    }

    function escapeHtml(str) {
        return (str || '')
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
        if (Number.isNaN(n)) return String(v);
        return n.toLocaleString('vi-VN');
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
        const v = normalizeBarcodeType(value);

        switch (v) {
            case 0: return 'Internal';
            case 1: return 'External';
            case 2: return 'Supplier';
            case 3: return 'Packaging';
            case 4: return 'Legacy';
            default: return String(value ?? '');
        }
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

    async function getJson(url) {
        const res = await fetch(url, {
            headers: {
                'X-Requested-With': 'XMLHttpRequest'
            },
            credentials: 'same-origin'
        });

        return await res.json();
    }

    function getCurrentConversion() {
        return state.conversions.find(x => x.id === state.currentConversionId) || null;
    }

    function resetConversionForm(clearSelected = false) {
        setValue('uc_Id', '');
        setValue('uc_UnitId', '');
        setValue('uc_Factor', '');
        setValue('uc_Price', '');
        setValue('uc_SortOrder', '0');
        setChecked('uc_IsBaseUnit', false);
        setChecked('uc_IsDefaultForSale', false);
        setChecked('uc_IsActive', true);

        if (clearSelected) {
            setText('uc_SelectedHint', 'Chưa chọn dòng nào');
            state.currentConversionId = 0;
        }
    }

    function resetBarcodeForm(clearSelected = false) {
        const currentConversion = getCurrentConversion();

        setValue('bc_Id', '');

        if (currentConversion) {
            setValue('bc_ProductUnitConversionId', currentConversion.id);
            setValue('bc_ConversionName', `${currentConversion.unitName} (x${currentConversion.factor})`);
        } else {
            setValue('bc_ProductUnitConversionId', '');
            setValue('bc_ConversionName', '');
        }

        setValue('bc_Barcode', '');
        setValue('bc_BarcodeType', '0');
        setChecked('bc_IsPrimary', true);
        setChecked('bc_IsActive', true);
        setValue('bc_Note', '');

        if (clearSelected) {
            setText('bc_SelectedHint', 'Chưa chọn barcode nào');
            state.currentBarcodeId = 0;
        }
    }

    function fillConversionForm(item) {
        setValue('uc_Id', item.id || '');
        setValue('uc_ProductVariantId', item.productVariantId || '');
        setValue('uc_UnitId', item.unitId || '');
        setValue('uc_Factor', item.factor || '');
        setValue('uc_Price', item.price ?? '');
        setValue('uc_SortOrder', item.sortOrder ?? 0);
        setChecked('uc_IsBaseUnit', !!item.isBaseUnit);
        setChecked('uc_IsDefaultForSale', !!item.isDefaultForSale);
        setChecked('uc_IsActive', !!item.isActive);
        setText('uc_SelectedHint', `${item.unitName} • x${item.factor}`);
        state.currentConversionId = item.id;
    }

    function fillBarcodeForm(item, conversion) {
        setValue('bc_Id', item?.id || '');
        setValue('bc_ProductUnitConversionId', conversion.id);
        setValue('bc_ConversionName', `${conversion.unitName} (x${conversion.factor})`);
        setValue('bc_Barcode', item?.barcode || '');
        setValue('bc_BarcodeType', String(normalizeBarcodeType(item?.barcodeType)));
        setChecked('bc_IsPrimary', item ? !!item.isPrimary : true);
        setChecked('bc_IsActive', item ? !!item.isActive : true);
        setValue('bc_Note', item?.note || '');
        setText('bc_SelectedHint', item ? item.barcode : 'Chưa chọn barcode nào');
        state.currentBarcodeId = item?.id || 0;
    }

    function scrollIntoViewIfNeeded(el, container) {
        if (!el || !container) return;

        const cTop = container.scrollTop;
        const cBottom = cTop + container.clientHeight;
        const eTop = el.offsetTop;
        const eBottom = eTop + el.offsetHeight;

        if (eTop < cTop) {
            container.scrollTop = eTop - 8;
        } else if (eBottom > cBottom) {
            container.scrollTop = eBottom - container.clientHeight + 8;
        }
    }

    function historyBadgeClass(actionType) {
        const key = String(actionType || '').trim().toLowerCase();

        switch (key) {
            case 'assigned':
                return 'ucx-action-assigned';
            case 'replaced':
                return 'ucx-action-replaced';
            case 'deactivated':
                return 'ucx-action-deactivated';
            case 'reactivated':
                return 'ucx-action-reactivated';
            case 'imported':
                return 'ucx-action-imported';
            default:
                return 'ucx-action-assigned';
        }
    }

    function renderHistoryCode(value) {
        if (!value) {
            return `<div class="ucx-history-code is-empty">—</div>`;
        }

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
                            <div class="ucx-history-time">
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
                                    <div class="ucx-history-meta-value">
                                        ${escapeHtml(item.reason || '—')}
                                    </div>
                                </div>

                                <div class="ucx-history-meta-row">
                                    <div class="ucx-history-meta-key">Người đổi</div>
                                    <div class="ucx-history-meta-value">
                                        ${escapeHtml(item.changedByUserName || '—')}
                                    </div>
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
            wrap.innerHTML = `<div class="bh-empty">Chọn một dòng quy đổi để xem lịch sử barcode.</div>`;
            return;
        }

        wrap.innerHTML = `<div class="bh-empty">Đang tải lịch sử barcode...</div>`;

        try {
            const json = await getJson(`/Admin/ProductUnitConversion/GetBarcodeHistoryByConversionId?productUnitConversionId=${conversion.id}&take=20`);

            if (!json.ok) {
                wrap.innerHTML = `<div class="bh-empty">${escapeHtml(json.message || 'Không tải được lịch sử barcode.')}</div>`;
                return;
            }

            renderBarcodeHistory(json.data || []);
        } catch (error) {
            console.error(error);
            wrap.innerHTML = `<div class="bh-empty">Không tải được lịch sử barcode.</div>`;
        }
    }

    function renderBarcodes() {
        const wrap = byId('barcodeListWrap');
        if (!wrap) return;

        const conversion = getCurrentConversion();

        if (!conversion) {
            wrap.innerHTML = `<div class="bc-empty">Chọn một dòng quy đổi để xem barcode.</div>`;
            resetBarcodeForm(true);
            renderBarcodeHistory([]);
            return;
        }

        if (!conversion.barcodes || !conversion.barcodes.length) {
            wrap.innerHTML = `
                <div class="bc-empty">
                    Đơn vị <b>${escapeHtml(conversion.unitName)}</b> chưa có barcode nào.
                </div>
            `;
            fillBarcodeForm(null, conversion);
            loadBarcodeHistory();
            return;
        }

        if (!state.currentBarcodeId || !conversion.barcodes.some(x => x.id === state.currentBarcodeId)) {
            state.currentBarcodeId = conversion.barcodes[0].id;
        }

        wrap.innerHTML = `
            <div class="bc-table-wrap">
                <table class="table table-bordered align-middle bc-table">
                    <thead class="table-light">
                        <tr>
                            <th>Barcode</th>
                            <th>Loại</th>
                            <th>Chính</th>
                            <th>Trạng thái</th>
                            <th>Ghi chú</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${(conversion.barcodes || []).map(b => `
                            <tr class="bc-click-row ${state.currentBarcodeId === b.id ? 'is-selected' : ''}" data-barcode-id="${b.id}">
                                <td class="fw-semibold">${escapeHtml(b.barcode)}</td>
                                <td>${escapeHtml(barcodeTypeText(b.barcodeType))}</td>
                                <td>${b.isPrimary ? '<span class="badge bg-primary">Chính</span>' : '—'}</td>
                                <td>${b.isActive ? '<span class="badge bg-success">Hoạt động</span>' : '<span class="badge bg-secondary">Ngưng</span>'}</td>
                                <td>${escapeHtml(b.note || '')}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        `;

        const currentBarcode = conversion.barcodes.find(x => x.id === state.currentBarcodeId);
        if (currentBarcode) {
            fillBarcodeForm(currentBarcode, conversion);
        } else {
            fillBarcodeForm(null, conversion);
        }

        const tableWrap = wrap.querySelector('.bc-table-wrap');

        wrap.querySelectorAll('.bc-click-row').forEach(row => {
            row.addEventListener('click', () => {
                const barcodeId = parseInt(row.dataset.barcodeId || '0');
                state.currentBarcodeId = barcodeId;

                wrap.querySelectorAll('.bc-click-row').forEach(x => x.classList.remove('is-selected'));
                row.classList.add('is-selected');

                const found = conversion.barcodes.find(x => x.id === barcodeId);
                if (found) {
                    fillBarcodeForm(found, conversion);
                }

                scrollIntoViewIfNeeded(row, tableWrap);
            });
        });

        const selectedRow = wrap.querySelector(`.bc-click-row[data-barcode-id="${state.currentBarcodeId}"]`);
        if (selectedRow && tableWrap) {
            scrollIntoViewIfNeeded(selectedRow, tableWrap);
        }

        loadBarcodeHistory();
    }

    function renderConversions() {
        const wrap = byId('conversionListWrap');
        if (!wrap) return;

        if (!state.conversions.length) {
            wrap.innerHTML = `<div class="uc-empty">Biến thể này chưa có quy đổi đơn vị nào.</div>`;
            setHtml('barcodeListWrap', `<div class="bc-empty">Chọn một dòng quy đổi để xem barcode.</div>`);
            setHtml('barcodeHistoryWrap', `<div class="bh-empty">Chọn một dòng quy đổi để xem lịch sử barcode.</div>`);
            resetConversionForm(true);
            resetBarcodeForm(true);
            return;
        }

        if (!state.currentConversionId || !state.conversions.some(x => x.id === state.currentConversionId)) {
            state.currentConversionId = state.conversions[0].id;
        }

        wrap.innerHTML = `
            <div class="uc-table-wrap">
                <table class="table table-bordered align-middle uc-table">
                    <thead class="table-light">
                        <tr>
                            <th>Đơn vị</th>
                            <th>Factor</th>
                            <th>Giá</th>
                            <th>Gốc</th>
                            <th>Mặc định</th>
                            <th>Trạng thái</th>
                            <th>Barcode</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${state.conversions.map(x => `
                            <tr class="uc-click-row ${state.currentConversionId === x.id ? 'is-selected' : ''}" data-id="${x.id}">
                                <td class="fw-semibold">${escapeHtml(x.unitName)}</td>
                                <td>x${escapeHtml(String(x.factor))}</td>
                                <td class="uc-money">${money(x.price)}</td>
                                <td>${x.isBaseUnit ? '<span class="badge bg-info text-dark">Gốc</span>' : '—'}</td>
                                <td>${x.isDefaultForSale ? '<span class="badge bg-primary">Mặc định</span>' : '—'}</td>
                                <td>${x.isActive ? '<span class="badge bg-success">Hoạt động</span>' : '<span class="badge bg-secondary">Ngưng</span>'}</td>
                                <td>${x.barcodes?.length || 0}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        `;

        const current = state.conversions.find(x => x.id === state.currentConversionId);
        if (current) {
            fillConversionForm(current);
        }

        const tableWrap = wrap.querySelector('.uc-table-wrap');

        wrap.querySelectorAll('.uc-click-row').forEach(row => {
            row.addEventListener('click', () => {
                const id = parseInt(row.dataset.id || '0');
                state.currentConversionId = id;
                state.currentBarcodeId = 0;

                wrap.querySelectorAll('.uc-click-row').forEach(x => x.classList.remove('is-selected'));
                row.classList.add('is-selected');

                const found = state.conversions.find(x => x.id === id);
                if (found) {
                    fillConversionForm(found);
                    renderBarcodes();
                }

                scrollIntoViewIfNeeded(row, tableWrap);
            });
        });

        const selectedRow = wrap.querySelector(`.uc-click-row[data-id="${state.currentConversionId}"]`);
        if (selectedRow && tableWrap) {
            scrollIntoViewIfNeeded(selectedRow, tableWrap);
        }

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
            id: byId('uc_Id')?.value ? parseInt(byId('uc_Id').value) : null,
            productVariantId: state.currentVariantId,
            unitId: byId('uc_UnitId')?.value ? parseInt(byId('uc_UnitId').value) : 0,
            factor: byId('uc_Factor')?.value ? parseFloat(byId('uc_Factor').value) : 0,
            price: byId('uc_Price')?.value ? parseFloat(byId('uc_Price').value) : null,
            sortOrder: byId('uc_SortOrder')?.value ? parseInt(byId('uc_SortOrder').value) : 0,
            isBaseUnit: !!byId('uc_IsBaseUnit')?.checked,
            isDefaultForSale: !!byId('uc_IsDefaultForSale')?.checked,
            isActive: !!byId('uc_IsActive')?.checked
        };

        try {
            const json = await postJson('/Admin/ProductUnitConversion/SaveConversion', dto);

            if (!json.ok) {
                toastr?.error(json.message || 'Lưu quy đổi thất bại.');
                return;
            }

            toastr?.success(json.message || 'Lưu quy đổi thành công.');
            state.currentConversionId = json.id || state.currentConversionId;
            await loadConversions();
        } catch (error) {
            console.error(error);
            toastr?.error('Lưu quy đổi thất bại.');
        }
    }

    async function saveBarcode() {
        const conversion = getCurrentConversion();
        if (!conversion) {
            toastr?.warning('Hãy chọn một dòng quy đổi trước.');
            return;
        }

        const rawBarcode = (byId('bc_Barcode')?.value || '').trim();

        let barcodeType = byId('bc_BarcodeType')?.value
            ? parseInt(byId('bc_BarcodeType').value)
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
            id: byId('bc_Id')?.value ? parseInt(byId('bc_Id').value) : null,
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
            state.currentBarcodeId = json.id || state.currentBarcodeId;
            await loadConversions();
        } catch (error) {
            console.error(error);
            toastr?.error('Lưu barcode thất bại.');
        }
    }

    function open(variant) {
        state.currentVariantId = variant.id;
        state.currentVariantSku = variant.sku || '';
        state.currentVariantName = variant.name || '';
        state.currentConversionId = 0;
        state.currentBarcodeId = 0;
        state.conversions = [];

        const nameEl = byId('unitConvVariantName');
        if (nameEl) {
            nameEl.textContent = variant.name || '(Chưa có tên biến thể)';
        }

        const infoEl = byId('unitConvVariantInfo');
        if (infoEl) {
            infoEl.innerHTML = `
                Variant ID: <b>${variant.id}</b>
                • SKU: <b>${escapeHtml(variant.sku || '(trống)')}</b>
            `;
        }

        setValue('uc_ProductVariantId', variant.id);
        resetConversionForm(true);
        resetBarcodeForm(true);
        setHtml('barcodeListWrap', `<div class="bc-empty">Chọn một dòng quy đổi để xem barcode.</div>`);
        setHtml('barcodeHistoryWrap', `<div class="bh-empty">Chọn một dòng quy đổi để xem lịch sử barcode.</div>`);

        const modalEl = byId('mdlVariantUnitConversions');
        if (modalEl && window.bootstrap && bootstrap.Modal) {
            bootstrap.Modal.getOrCreateInstance(modalEl, {
                backdrop: 'static',
                keyboard: true
            }).show();
        }

        loadConversions();
    }

    function wrapClearSelection(selector) {
        document.querySelectorAll(selector).forEach(x => x.classList.remove('is-selected'));
    }

    function bind() {
        byId('btnSaveConversion')?.addEventListener('click', saveConversion);
        byId('btnSaveBarcode')?.addEventListener('click', saveBarcode);
        byId('btnReloadConversions')?.addEventListener('click', loadConversions);
        byId('btnReloadBarcodeHistory')?.addEventListener('click', loadBarcodeHistory);

        byId('btnResetConversionForm')?.addEventListener('click', () => {
            resetConversionForm(true);
            wrapClearSelection('.uc-click-row');
            setHtml('barcodeListWrap', `<div class="bc-empty">Chọn một dòng quy đổi để xem barcode.</div>`);
            setHtml('barcodeHistoryWrap', `<div class="bh-empty">Chọn một dòng quy đổi để xem lịch sử barcode.</div>`);
        });

        byId('btnResetBarcodeForm')?.addEventListener('click', () => {
            wrapClearSelection('.bc-click-row');
            state.currentBarcodeId = 0;

            const conversion = getCurrentConversion();
            if (conversion) {
                fillBarcodeForm(null, conversion);
            } else {
                resetBarcodeForm(true);
            }
        });
    }

    return {
        bind,
        open
    };
})();