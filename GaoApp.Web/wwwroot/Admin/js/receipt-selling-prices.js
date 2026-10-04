(function () {
    'use strict';
    const math = {
        suggest(cost, rate, basis, rounding) {
            if (!Number.isFinite(cost) || cost <= 0 || !Number.isFinite(rate) || rate < 0 || rate > 95 || !(rounding > 0)) return NaN;
            const raw = basis === 'markup' ? cost * (1 + rate / 100) : cost / (1 - rate / 100);
            return Math.ceil((raw - 1e-8) / rounding) * rounding;
        },
        profit(cost, price, basis) {
            return price > 0 && cost > 0 ? (price - cost) / (basis === 'markup' ? cost : price) * 100 : NaN;
        },
        baseCost(price, factor, quantity, taxRate, includeVat, freight) {
            return factor > 0 && price > 0 ?
                (price * (1 + (includeVat ? taxRate / 100 : 0)) + (quantity > 0 ? freight / quantity : 0)) / factor : NaN;
        }
    };
    if (typeof module !== 'undefined' && module.exports) { module.exports = math; return; }
    const modal = document.getElementById('receiptSellingPrices');
    if (!modal) return;
    const el = id => document.getElementById(id);
    const money = n => Number.isFinite(n) ? n.toLocaleString('vi-VN', { maximumFractionDigits: 2 }) : '—';
    const percent = n => Number.isFinite(n) ? n.toLocaleString('vi-VN', { maximumFractionDigits: 1 }) + '%' : '—';
    const num = input => input && input.value.trim() !== '' ? Number(input.value) : NaN;
    const checked = id => el(id)?.checked === true;
    let data, units = [], endpoint, variantId, saving = false, blocked = false, generation = 0;

    function message(text, kind) {
        el('rspMessage').textContent = text;
        el('rspMessage').className = 'alert alert-' + (kind || 'info');
        el('rspMessage').hidden = !text;
    }
    async function request(options) {
        const response = await fetch(endpoint, { cache: 'no-store', headers: { Accept: 'application/json' }, ...options });
        const json = await response.json().catch(() => null);
        const result = json?.data ?? json;
        if (!response.ok || json?.success === false || json?.isSuccess === false)
            throw new Error(result?.message || json?.message || (response.status === 403 ? 'Bạn không có quyền chỉnh giá bán.' : 'Không tải/lưu được giá bán. Đóng và mở lại bảng giá để kiểm tra.'));
        if (!result?.units) throw new Error('Phản hồi giá bán không hợp lệ. Vui lòng tải lại trang.');
        return result;
    }
    function history() {
        el('rspHistory').replaceChildren();
        if (!data.history.length) el('rspHistory').textContent = 'Chưa có lần đổi giá tại phiếu nhập.';
        for (const item of data.history) {
            const row = document.createElement('div'); row.className = 'rsp-history-row small';
            const date = new Date(item.atUtc.endsWith('Z') ? item.atUtc : item.atUtc + 'Z');
            row.textContent = `${date.toLocaleString('vi-VN')} · ${item.userName || 'Hệ thống'} · ${item.summary}`;
            el('rspHistory').append(row);
        }
    }
    function createRows() {
        el('rspUnits').replaceChildren();
        for (const unit of units) {
            const row = document.createElement('tr'); row.dataset.unitId = unit.id;
            row.innerHTML = `<td><label class="rsp-unit-label"><input class="form-check-input" type="checkbox" data-field="selected" checked /><span data-out="name"></span></label><small class="text-muted" data-out="factor"></small></td>
                <td data-label="Giá vốn mới"><span data-out="cost"></span></td>
                <td data-label="Đang bán"><small>Lẻ</small> <strong data-out="current"></strong><small class="d-block" data-out="current-profit"></small><span class="d-block small" data-out="current-wholesale"></span></td>
                <td data-label="Giá lẻ mới"><label class="small">Lãi muốn (%)<input class="form-control mb-2" type="number" min="0" max="95" step="0.1" data-field="target" /></label><input class="form-control" type="number" min="0.01" max="999999999999" step="0.01" data-field="price" /><button type="button" class="rsp-suggestion" data-action="suggest"></button><small class="d-block" data-out="delta"></small></td>
                <td data-label="Giá sỉ mới"><label class="small">Lãi muốn (%)<input class="form-control mb-2" type="number" min="0" max="95" step="0.1" data-field="wholesale-target" /></label><input class="form-control" type="number" min="0.01" max="999999999999" step="0.01" data-field="wholesale" placeholder="Theo giá lẻ" /><button type="button" class="rsp-suggestion" data-action="suggest-wholesale"></button><small class="d-block" data-out="wholesale-delta"></small></td>
                <td data-label="Lãi sau đổi"><strong data-out="profit"></strong><small class="d-block" data-out="rate"></small><strong class="d-block mt-2" data-out="wholesale-profit"></strong><small class="d-block" data-out="wholesale-rate"></small></td>`;
            row.querySelector('[data-out="name"]').textContent = unit.unitName;
            row.querySelector('[data-out="factor"]').textContent = `${money(unit.factor)} ${data.baseUnitName}`;
            row.querySelector('[data-field="target"]').value = unit.target;
            row.querySelector('[data-field="wholesale-target"]').value = unit.wholesaleTarget;
            row.querySelector('[data-field="wholesale"]').value = unit.wholesaleDraft ?? '';
            row.querySelector('[data-field="wholesale"]').setAttribute('aria-label', `Giá sỉ mới ${unit.unitName}`);
            row.querySelector('[data-field="wholesale-target"]').setAttribute('aria-label', `Lợi nhuận bán sỉ ${unit.unitName}`);
            row.querySelector('[data-field="target"]').setAttribute('aria-label', `Lợi nhuận mong muốn ${unit.unitName}`);
            row.querySelector('[data-field="price"]').setAttribute('aria-label', `Giá bán mới ${unit.unitName}`);
            unit.row = row; el('rspUnits').append(row);
        }
    }
    function render() {
        const cost = num(el('rspCost')), basis = el('rspBasis').value, rounding = num(el('rspRound'));
        let invalid = !Number.isFinite(cost) || cost <= 0 || cost > 999999999999 || !el('rspTarget').checkValidity() || el('rspTarget').value === '' || !el('rspWholesaleTarget').checkValidity() || el('rspWholesaleTarget').value === '';
        let changed = 0, belowTarget = 0, belowCost = 0;
        for (const unit of units) {
            const row = unit.row, unitCost = cost * unit.factor;
            unit.suggested = math.suggest(unitCost, unit.target, basis, rounding);
            if (!unit.manual) unit.draft = unit.suggested;
            unit.wholesaleSuggested = math.suggest(unitCost, unit.wholesaleTarget, basis, rounding);
            if (!unit.wholesaleManual) unit.wholesaleDraft = unit.wholesaleSuggested;
            const wholesaleInput = row.querySelector('[data-field="wholesale"]');
            if (document.activeElement !== wholesaleInput) wholesaleInput.value = unit.wholesaleDraft ?? '';
            const priceInput = row.querySelector('[data-field="price"]');
            if (document.activeElement !== priceInput) priceInput.value = Number.isFinite(unit.draft) ? unit.draft : '';
            const afterRate = math.profit(unitCost, unit.draft, basis);
            const currentRate = math.profit(unitCost, unit.price, basis);
            const output = (key, text) => { row.querySelector(`[data-out="${key}"]`).textContent = text; };
            output('cost', money(unitCost)); output('current', money(unit.price));
            output('current-wholesale', 'Sỉ: ' + (unit.wholesalePrice == null ? 'Theo giá lẻ' : money(unit.wholesalePrice)));
            row.querySelector('[data-action="suggest-wholesale"]').textContent = 'Đề xuất: ' + money(unit.wholesaleSuggested);
            output('wholesale-delta', unit.wholesaleDraft === unit.wholesalePrice ? 'Không đổi' : unit.wholesaleDraft == null ? 'Chuyển sang giá lẻ' : 'Thay đổi ' + money(unit.wholesaleDraft - (unit.wholesalePrice ?? unit.price)) + ' đ');
            const wholesaleAfter = unit.wholesaleDraft ?? unit.draft;
            const wholesaleRate = math.profit(unitCost, wholesaleAfter, basis);
            output('wholesale-profit', 'Sỉ: ' + money(wholesaleAfter - unitCost) + ' đ');
            output('wholesale-rate', percent(wholesaleRate) + (wholesaleRate < unit.wholesaleTarget ? ' · Dưới mục tiêu' : ''));
            row.querySelector('[data-out="wholesale-rate"]').className = 'd-block small ' + (wholesaleRate < unit.wholesaleTarget ? 'text-danger' : 'text-success');
            output('current-profit', 'Lãi ' + percent(currentRate));
            row.querySelector('[data-out="current-profit"]').className = 'd-block small ' + (currentRate < unit.target ? 'text-warning' : 'text-muted');
            row.querySelector('[data-action="suggest"]').textContent = 'Đề xuất: ' + money(unit.suggested);
            const delta = unit.draft - unit.price;
            output('delta', (unit.manual ? 'Sửa tay · ' : '') + (delta > 0 ? 'Tăng ' : delta < 0 ? 'Giảm ' : 'Không đổi ') + (delta ? money(Math.abs(delta)) + ' đ' : ''));
            output('profit', 'Lẻ: ' + money(unit.draft - unitCost) + ' đ');
            output('rate', percent(afterRate) + (afterRate < unit.target ? ' · Dưới mục tiêu' : ''));
            row.querySelector('[data-out="rate"]').className = 'd-block small ' + (afterRate < unit.target ? 'text-danger' : 'text-success');
            if (currentRate < unit.target || math.profit(unitCost, unit.wholesalePrice ?? unit.price, basis) < unit.wholesaleTarget) belowTarget++;
            if (unit.selected && (unit.draft < unitCost || wholesaleAfter < unitCost)) belowCost++;
            if (unit.selected) {
                invalid ||= !Number.isFinite(unit.draft) || unit.draft <= 0 || unit.draft > 999999999999 ||
                    Math.abs(unit.draft * 100 - Math.round(unit.draft * 100)) > 0.001 ||
                    !Number.isFinite(unit.target) || unit.target < 0 || unit.target > 95 ||
                    !Number.isFinite(unit.wholesaleTarget) || unit.wholesaleTarget < 0 || unit.wholesaleTarget > 95 ||
                    (unit.wholesaleDraft != null && (!Number.isFinite(unit.wholesaleDraft) || unit.wholesaleDraft <= 0 || unit.wholesaleDraft > 999999999999 || Math.abs(unit.wholesaleDraft * 100 - Math.round(unit.wholesaleDraft * 100)) > 0.001));
                if (unit.draft !== unit.price || unit.wholesaleDraft !== unit.wholesalePrice) changed++;
            }
        }
        el('rspWarning').textContent = invalid ? 'Nhập giá vốn, giá bán lớn hơn 0 và lợi nhuận từ 0 đến 95%. Giá bán tối đa 2 chữ số thập phân.' :
            `${belowTarget} đơn vị có giá đang bán chưa đạt lợi nhuận mong muốn.` +
            (belowCost ? ` Chú ý: ${belowCost} giá bán mới thấp hơn giá vốn dự kiến.` : '');
        const source = units.find(x => x.id === Number(el('rspProductSource').value));
        const productPrice = source ? Math.round(((source.selected ? source.draft : source.price) / source.factor + Number.EPSILON) * 100) / 100 : NaN;
        const productChanged = productPrice !== data.productPrice;
        invalid ||= !Number.isFinite(productPrice) || productPrice <= 0 || productPrice > 999999999999;
        el('rspProductPrice').textContent = `Giá Product / ${data.baseUnitName}: ${money(data.productPrice)} → ${money(productPrice)} đ`;
        el('rspCount').textContent = `${changed} đơn vị có giá mới` + (productChanged ? ' · cập nhật giá Product' : '');
        el('rspSave').disabled = saving || blocked || invalid || (changed === 0 && !productChanged);
        el('rspReview').disabled = saving || blocked || invalid;
    }

    document.addEventListener('click', async event => {
        const button = event.target.closest('.js-receipt-selling-prices');
        if (!button || saving) return;
        const sequence = ++generation, row = button.closest('.commercial-line');
        variantId = Number(button.dataset.variantId);
        if (el('rspPrintLabels')) el('rspPrintLabels').hidden = true;
        data = null; units = []; blocked = false; el('rspContent').hidden = true;
        el('rspSave').disabled = true; el('rspCount').textContent = ''; el('rspProduct').textContent = '';
        el('rspReview').disabled = true;
        message('Đang tải giá bán…');
        bootstrap.Modal.getOrCreateInstance(modal, { keyboard: true }).show();
        endpoint = `/admin/api/stock-documents/${window.stockDocumentPage.documentId}/lines/${button.dataset.lineId}/selling-prices`;
        try {
            const loaded = await request();
            if (sequence !== generation) return;
            data = loaded;
            const factor = Number(row.dataset.factor), quantity = Number(row.dataset.quantity);
            const price = num(row.querySelector('.commercial-unit-price'));
            const tax = Number(row.querySelector('.commercial-tax')?.selectedOptions[0]?.dataset.rate || 0);
            const vat = checked('commercialHasVat') && checked('includeVatInInventoryCost');
            const freight = checked('commercialHasFreight') && checked('capitalizeFreightInInventoryCost') ?
                num(document.querySelector(`.commercial-line[data-line-id="${button.dataset.lineId}"] .commercial-freight-allocation`)) || 0 : 0;
            const cost = math.baseCost(price, factor, quantity, tax, vat, freight);
            el('rspCost').value = Number.isFinite(cost) ? Math.round(cost * 10000) / 10000 : '';
            const last = Number(row.querySelector('.commercial-last-price')?.dataset.value || 0);
            el('rspCostNote').textContent = last > 0 ? `Giá nhập gần nhất (chưa VAT) / đơn vị gốc: ${money(last / factor)} đ. Giá nhập hiện tại ${price > last ? 'tăng' : price < last ? 'giảm' : 'không đổi'}${price !== last ? ' ' + percent(Math.abs(price - last) / last * 100) : ''}.` : 'Chưa có giá nhập trước đó để so sánh.';
            el('rspProduct').textContent = data.productName; el('rspBaseUnit').textContent = data.baseUnitName;
            el('rspTarget').value = 10; el('rspWholesaleTarget').value = 10; el('rspBasis').value = 'margin'; el('rspRound').value = 100;
            units = data.units.map(unit => ({ ...unit, selected: true, target: 10, manual: false,
                wholesalePrice: unit.wholesalePrice ?? null, wholesaleTarget: 10, wholesaleManual: true, wholesaleDraft: unit.wholesalePrice ?? null }));
            el('rspProductSource').replaceChildren(...units.map(unit => new Option(`${unit.unitName} (÷ ${money(unit.factor)})`, unit.id)));
            el('rspProductSource').value = data.productPriceUnitId;
            createRows(); history(); el('rspContent').hidden = false;
            message(units.length ? '' : 'Chưa có đơn vị bán hoạt động. Vui lòng cấu hình đơn vị trong danh mục.', 'warning');
            render();
        } catch (error) { if (sequence === generation) message(error.message, 'danger'); }
    });
    modal.addEventListener('hide.bs.modal', event => { if (saving) event.preventDefault(); else generation++; });
    // Also handle Escape when focus has moved outside the price dialog.
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || event.defaultPrevented || !modal.classList.contains('show')) return;
        const openModals = document.querySelectorAll('.modal.show');
        if (openModals[openModals.length - 1] !== modal) return;
        event.preventDefault();
        bootstrap.Modal.getInstance(modal)?.hide();
    });
    modal.addEventListener('input', event => {
        if (!data || saving) return;
        const row = event.target.closest('[data-unit-id]');
        if (row) {
            const unit = units.find(x => x.id === Number(row.dataset.unitId));
            if (event.target.dataset.field === 'price') { unit.draft = num(event.target); unit.manual = true; }
            if (event.target.dataset.field === 'wholesale') { unit.wholesaleDraft = event.target.value === '' ? null : num(event.target); unit.wholesaleManual = true; }
            if (event.target.dataset.field === 'wholesale-target') unit.wholesaleTarget = num(event.target);
            if (event.target.dataset.field === 'target') unit.target = num(event.target);
            if (event.target.dataset.field === 'selected') unit.selected = event.target.checked;
        } else if (event.target.id === 'rspTarget') {
            for (const unit of units) { unit.target = num(event.target); unit.row.querySelector('[data-field="target"]').value = event.target.value; }
        } else if (event.target.id === 'rspWholesaleTarget') {
            for (const unit of units) { unit.wholesaleTarget = num(event.target); unit.row.querySelector('[data-field="wholesale-target"]').value = event.target.value; }
        }
        render();
    });
    modal.addEventListener('click', event => {
        if (!data || saving) return;
        const suggestion = event.target.closest('[data-action="suggest"]');
        const wholesaleSuggestion = event.target.closest('[data-action="suggest-wholesale"]');
        if (wholesaleSuggestion) units.find(x => x.id === Number(wholesaleSuggestion.closest('[data-unit-id]').dataset.unitId)).wholesaleManual = false;
        if (suggestion) units.find(x => x.id === Number(suggestion.closest('[data-unit-id]').dataset.unitId)).manual = false;
        if (event.target.id === 'rspUseSuggestions') units.filter(x => x.selected).forEach(x => { x.manual = false; x.wholesaleManual = false; });
        if (suggestion || wholesaleSuggestion || event.target.id === 'rspUseSuggestions') render();
    });
    el('rspSave').addEventListener('click', () => save(false));
    el('rspReview').addEventListener('click', () => save(true));
    async function save(reviewOnly) {
        if (!data || saving || el(reviewOnly ? 'rspReview' : 'rspSave').disabled) return;
        saving = true; render();
        const controls = [...el('rspContent').querySelectorAll('input,select,button')];
        controls.forEach(x => { x.disabled = true; });
        message('Đang cập nhật giá bán…');
        const changes = reviewOnly ? units : units.filter(x => x.selected && (x.draft !== x.price || x.wholesaleDraft !== x.wholesalePrice));
        try {
            const updated = await request({ method: 'POST', headers: {
                'Content-Type': 'application/json', Accept: 'application/json',
                RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
            }, body: JSON.stringify({ productVersion: data.productVersion, variantVersion: data.variantVersion,
                catalogVersion: data.catalogVersion, productPriceUnitId: Number(el('rspProductSource').value), reviewOnly,
                lineVersion: data.lineVersion, estimatedBaseCost: num(el('rspCost')), basis: el('rspBasis').value,
                units: changes.map(x => ({ id: x.id, rowVersion: x.rowVersion, price: reviewOnly ? x.price : x.draft,
                    updateWholesalePrice: true, wholesalePrice: reviewOnly ? x.wholesalePrice : x.wholesaleDraft,
                    targetPercent: x.target, wholesaleTargetPercent: x.wholesaleTarget })) }) });
            data = updated;
            document.querySelectorAll(`[data-retail-variant="${variantId}"]`).forEach(cell => {
                const unitId = Number(cell.dataset.retailUnit), unit = data.units.find(x => x.id === unitId);
                const price = unit?.price ?? (unitId === 0 ? data.baseRetailPrice : null);
                cell.textContent = price > 0
                    ? `${Number(price).toLocaleString('vi-VN')} đ/${unit?.unitName || data.baseUnitName}` : 'Chưa có giá bán';
            });
            if (!reviewOnly && variantId > 0 && el('rspPrintLabels')) {
                el('rspPrintLabels').href = `/admin/label-printing?tab=quick&variantId=${variantId}&units=${changes.map(x => x.id).filter(x => x > 0).join(',')}`;
                el('rspPrintLabels').hidden = false;
            }
            for (const unit of units) {
                const saved = data.units.find(x => x.id === unit.id);
                if (saved) { unit.price = saved.price; unit.wholesalePrice = saved.wholesalePrice ?? null; unit.rowVersion = saved.rowVersion; }
            }
            history(); message(reviewOnly ? 'Đã ghi nhận kiểm tra, giữ nguyên giá bán.' : `Đã cập nhật ${changes.length} đơn vị và đồng bộ giá Product. Giá mới dùng cho lần lấy giá tiếp theo; các dòng đã có trong giỏ POS giữ nguyên.`, 'success');
            window.GaoReceiptPriceReviews?.refresh();
        } catch (error) {
            blocked = true;
            message(error.message + ' Đóng và mở lại bảng giá để kiểm tra kết quả trước khi thử lại.', 'danger');
        } finally {
            saving = false; controls.forEach(x => { x.disabled = false; }); render();
        }
    }
})();
