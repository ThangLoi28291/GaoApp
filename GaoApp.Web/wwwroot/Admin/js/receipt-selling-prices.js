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
        },
        parseMoney(value) {
            const text = String(value ?? '').trim().replace(/\s|\u00a0/g, '').replace(/(?:VND|đ)$/i, '');
            if (!text || !/^\d[\d.,]*$/.test(text)) return NaN;
            if (text.includes(',')) {
                if (!/^\d+(?:\.\d{3})*,\d{1,2}$/.test(text)) return NaN;
                return Number(text.replace(/\./g, '').replace(',', '.'));
            }
            if (/^\d{1,3}(?:\.\d{3})+$/.test(text)) return Number(text.replace(/\./g, ''));
            return /^\d+(?:\.\d{1,2})?$/.test(text) ? Number(text) : NaN;
        },
        validMoney(value) {
            return Number.isFinite(value) && value > 0 && value <= 999999999999 &&
                Math.abs(value * 100 - Math.round(value * 100)) < 0.001;
        },
        unitPriceToBase(amount, factor) {
            if (!Number.isFinite(amount) || amount < 0 || !Number.isFinite(factor) || factor <= 0) return NaN;
            // Round the decimal ratio like the server, without binary midpoint drift.
            const fraction = value => {
                const [mantissa, exponent = '0'] = String(value).toLowerCase().split('e');
                const parts = mantissa.split('.');
                let numerator = BigInt(parts.join('')), scale = (parts[1]?.length || 0) - Number(exponent);
                if (scale < 0) { numerator *= 10n ** BigInt(-scale); scale = 0; }
                return [numerator, 10n ** BigInt(scale)];
            };
            const [a, ad] = fraction(amount), [f, fd] = fraction(factor);
            const numerator = a * fd * 100n, denominator = ad * f;
            return Number((2n * numerator + denominator) / (2n * denominator)) / 100;
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
    const target = value => Number.isFinite(value) && value >= 0 && value <= 95 ? value : 10;
    let data, units = [], endpoint, variantId, saving = false, blocked = false, generation = 0;
    let confirming = false, allowDiscard = false, lastEdited, releaseReceiptDrafts, savedNotice = '';

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
        if (!Array.isArray(result?.units)) throw new Error('Phản hồi giá bán không hợp lệ. Vui lòng tải lại trang.');
        return result;
    }
    function history() {
        el('rspHistory').replaceChildren();
        if (!data.history?.length) el('rspHistory').textContent = 'Chưa có lần đổi giá tại phiếu nhập.';
        for (const item of data.history || []) {
            const row = document.createElement('div'); row.className = 'rsp-history-row';
            const date = new Date(/(Z|[+-]\d{2}:?\d{2})$/i.test(item.atUtc) ? item.atUtc : item.atUtc + 'Z');
            row.textContent = `${date.toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' })} · ${item.userName || 'Hệ thống'} · ${item.summary}`;
            el('rspHistory').append(row);
        }
    }
    function createRows() {
        el('rspUnits').replaceChildren();
        for (const unit of units) {
            const row = document.createElement('section'); row.className = 'rsp-unit'; row.dataset.unitId = unit.id;
            row.innerHTML = `<header class="rsp-unit-heading"><div><strong data-out="name"></strong><small data-out="factor"></small></div><div class="rsp-unit-cost"><small>Giá vốn / đơn vị</small><strong data-out="cost"></strong></div></header>
                <table class="rsp-comparison"><thead><tr><th>Kênh bán</th><th>Giá đang bán</th><th>Giá muốn áp dụng</th><th>Lãi gộp dự kiến</th></tr></thead><tbody>
                <tr class="rsp-channel" data-channel="retail"><th scope="row">Bán lẻ</th><td data-label="Giá đang bán"><strong data-out="current"></strong><small data-out="current-profit"></small></td>
                <td data-label="Giá muốn áp dụng"><div class="rsp-money-input"><input class="form-control" type="text" inputmode="decimal" autocomplete="off" data-field="price" /><span aria-hidden="true">đ</span></div><small class="rsp-price-delta" data-out="delta"></small><small class="rsp-field-error" data-error="price" role="alert" hidden></small></td>
                <td data-label="Lãi gộp dự kiến"><strong class="rsp-profit-money" data-out="profit"></strong><small data-out="rate"></small></td></tr>
                <tr class="rsp-channel" data-channel="wholesale"><th scope="row">Bán sỉ</th><td data-label="Giá đang bán"><strong data-out="current-wholesale"></strong><small data-out="current-wholesale-profit"></small></td>
                <td data-label="Giá muốn áp dụng"><div class="rsp-money-input"><input class="form-control" type="text" inputmode="decimal" autocomplete="off" data-field="wholesale" /><span aria-hidden="true">đ</span></div><label class="rsp-inherit"><input class="form-check-input" type="checkbox" data-field="wholesale-inherit" />Theo giá lẻ</label><small class="rsp-price-delta" data-out="wholesale-delta"></small><small class="rsp-field-error" data-error="wholesale" role="alert" hidden></small></td>
                <td data-label="Lãi gộp dự kiến"><strong class="rsp-profit-money" data-out="wholesale-profit"></strong><small data-out="wholesale-rate"></small></td></tr></tbody></table>
                <details class="rsp-unit-calculator"><summary>Tính giá đề xuất cho đơn vị này</summary><div class="rsp-unit-tools">
                <div class="rsp-suggestion-channel"><label class="rsp-target"><span data-target-label="retail">Lãi trên giá bán lẻ (%)</span><input class="form-control" type="number" min="0" max="95" step="0.1" data-field="target" /></label><button type="button" class="rsp-suggestion" data-action="suggest"></button></div>
                <div class="rsp-suggestion-channel"><label class="rsp-target"><span data-target-label="wholesale">Lãi trên giá bán sỉ (%)</span><input class="form-control" type="number" min="0" max="95" step="0.1" data-field="wholesale-target" /></label><button type="button" class="rsp-suggestion" data-action="suggest-wholesale"></button></div>
                </div><p class="rsp-calculator-note">Chỉ điền vào ô giá khi bấm Dùng đề xuất. Cách tính và làm tròn theo công cụ chung bên dưới.</p></details>`;
            row.querySelector('[data-out="name"]').textContent = unit.unitName;
            row.querySelector('[data-out="factor"]').textContent = unit.factor === 1 && unit.unitName === data.baseUnitName
                ? 'Đơn vị gốc' : `1 ${unit.unitName} = ${money(unit.factor)} ${data.baseUnitName}`;
            for (const [field, label] of [['price', 'Giá lẻ muốn áp dụng'], ['wholesale', 'Giá sỉ muốn áp dụng'],
                ['target', 'Lãi mục tiêu bán lẻ'], ['wholesale-target', 'Lãi mục tiêu bán sỉ'], ['wholesale-inherit', 'Giá sỉ theo giá lẻ']])
                row.querySelector(`[data-field="${field}"]`).setAttribute('aria-label', `${label} / ${unit.unitName}`);
            row.querySelector('[data-field="target"]').value = unit.target;
            row.querySelector('[data-field="wholesale-target"]').value = unit.wholesaleTarget;
            unit.row = row; el('rspUnits').append(row);
        }
    }
    function changedUnits() { return units.filter(unit => unit.draft !== unit.price || unit.wholesaleDraft !== unit.wholesalePrice); }
    function productPrice() {
        const source = units.find(unit => unit.id === Number(el('rspProductSource').value));
        return source ? math.unitPriceToBase(source.draft, source.factor) : NaN;
    }
    function changes() {
        const result = [];
        for (const unit of units) {
            if (unit.draft !== unit.price) result.push({ label: `${unit.unitName} · Giá lẻ`, before: unit.price, after: unit.draft });
            const before = unit.wholesalePrice ?? unit.price, after = unit.wholesaleDraft ?? unit.draft;
            if (unit.wholesaleDraft !== unit.wholesalePrice || before !== after)
                result.push({ label: `${unit.unitName} · Giá sỉ`, before, after,
                    note: unit.wholesaleDraft == null ? 'Theo giá lẻ' : unit.wholesalePrice == null ? 'Đặt giá sỉ riêng' : '' });
        }
        if (checked('rspUpdateProductPrice') && productPrice() !== data.productPrice)
            result.push({ label: `Giá lẻ mặc định / ${data.baseUnitName}`, before: data.productPrice, after: productPrice(),
                note: 'Áp dụng cả sản phẩm/biến thể đang dùng giá mặc định.' });
        return result;
    }
    function delta(before, after, note) {
        return !Number.isFinite(after) ? 'Chưa nhập giá hợp lệ' :
            (after === before ? 'Giữ nguyên giá' : `${after > before ? 'Tăng' : 'Giảm'} ${money(Math.abs(after - before))} đ`) + (note ? ` · ${note}` : '');
    }
    function setError(row, field, text) {
        const input = row.querySelector(`[data-field="${field}"]`), error = row.querySelector(`[data-error="${field}"]`);
        input.classList.toggle('is-invalid', !!text); input.setAttribute('aria-invalid', String(!!text));
        error.textContent = text; error.hidden = !text;
    }
    function renderConfirmation(entries) {
        const list = el('rspConfirmationLines'); list.replaceChildren();
        for (const entry of entries) {
            const item = document.createElement('div'); item.className = 'rsp-change-item';
            const label = document.createElement('strong'); label.textContent = entry.label;
            const value = document.createElement('span'); value.className = 'rsp-change-values';
            value.textContent = `${money(entry.before)} → ${money(entry.after)} đ`;
            item.append(label, value);
            if (entry.note) { const note = document.createElement('small'); note.textContent = entry.note; item.append(note); }
            list.append(item);
        }
        const reductions = entries.filter(entry => entry.after < entry.before);
        el('rspConfirmationWarning').hidden = reductions.length === 0;
        el('rspConfirmationWarning').textContent = reductions.length ? `${reductions.length} mức giá giảm. Kiểm tra lại trước khi xác nhận áp dụng.` : '';
    }
    function render() {
        if (!data) return;
        const cost = num(el('rspCost')), basis = el('rspBasis').value, rounding = num(el('rspRound'));
        const validCost = Number.isFinite(cost) && cost >= 0.0001 && cost <= 999999999999;
        let invalid = !validCost, belowCost = 0;
        el('rspCostDisplay').textContent = validCost ? `${money(cost)} đ` : 'Chưa có giá vốn';
        const basisHelp = el('rspBasisHelp');
        if (basisHelp) basisHelp.textContent = basis === 'markup'
            ? 'Ví dụ: cộng trên vốn 10% với vốn 100.000 đ sẽ đề xuất giá 110.000 đ.'
            : 'Lãi trên giá bán 10% nghĩa là phần lãi gộp chiếm 10% giá bán.';
        el('rspTarget').parentElement.firstChild.textContent = basis === 'markup' ? 'Cộng trên vốn cho giá lẻ (%)' : 'Lãi trên giá bán lẻ (%)';
        el('rspWholesaleTarget').parentElement.firstChild.textContent = basis === 'markup' ? 'Cộng trên vốn cho giá sỉ (%)' : 'Lãi trên giá bán sỉ (%)';
        for (const unit of units) {
            const row = unit.row, unitCost = cost * unit.factor;
            row.querySelector('[data-target-label="retail"]').textContent = basis === 'markup' ? 'Cộng trên vốn cho giá lẻ (%)' : 'Lãi trên giá bán lẻ (%)';
            row.querySelector('[data-target-label="wholesale"]').textContent = basis === 'markup' ? 'Cộng trên vốn cho giá sỉ (%)' : 'Lãi trên giá bán sỉ (%)';
            unit.suggested = math.suggest(unitCost, unit.target, basis, rounding);
            unit.wholesaleSuggested = math.suggest(unitCost, unit.wholesaleTarget, basis, rounding);
            const priceInput = row.querySelector('[data-field="price"]'), wholesaleInput = row.querySelector('[data-field="wholesale"]');
            if (document.activeElement !== priceInput && Number.isFinite(unit.draft)) priceInput.value = money(unit.draft);
            if (document.activeElement !== wholesaleInput && (unit.wholesaleDraft == null || Number.isFinite(unit.wholesaleDraft)))
                wholesaleInput.value = unit.wholesaleDraft == null ? '' : money(unit.wholesaleDraft);
            wholesaleInput.placeholder = unit.wholesaleDraft == null ? `Theo lẻ: ${money(unit.draft)}` : '';
            row.querySelector('[data-field="wholesale-inherit"]').checked = unit.wholesaleDraft == null;
            const afterWholesale = unit.wholesaleDraft ?? unit.draft, beforeWholesale = unit.wholesalePrice ?? unit.price;
            const output = (key, text) => { row.querySelector(`[data-out="${key}"]`).textContent = text; };
            output('cost', validCost ? `${money(unitCost)} đ` : '—'); output('current', `${money(unit.price)} đ`);
            output('current-wholesale', `${money(beforeWholesale)} đ${unit.wholesalePrice == null ? ' (theo lẻ)' : ''}`);
            const retailRate = math.profit(unitCost, unit.price, 'margin'), wholesaleRate = math.profit(unitCost, beforeWholesale, 'margin');
            output('current-profit', Number.isFinite(retailRate) ? `${retailRate < 0 ? 'Lỗ' : 'Lãi gộp'} ${percent(Math.abs(retailRate))}` : '');
            output('current-wholesale-profit', Number.isFinite(wholesaleRate) ? `${wholesaleRate < 0 ? 'Lỗ' : 'Lãi gộp'} ${percent(Math.abs(wholesaleRate))}` : '');
            const paintProfit = (value, key, rateKey) => {
                const rate = math.profit(unitCost, value, 'margin'), loss = value < unitCost;
                output(key, validCost && math.validMoney(value) ? `${loss ? 'Lỗ ' : ''}${money(Math.abs(value - unitCost))} đ` : '—');
                output(rateKey, Number.isFinite(rate) ? `${percent(rate)} trên giá bán` : 'Chưa xác định');
                row.querySelector(`[data-out="${key}"]`).classList.toggle('is-loss', loss);
                row.querySelector(`[data-out="${rateKey}"]`).classList.toggle('is-loss', loss);
            };
            paintProfit(unit.draft, 'profit', 'rate'); paintProfit(afterWholesale, 'wholesale-profit', 'wholesale-rate');
            output('delta', delta(unit.price, unit.draft));
            output('wholesale-delta', delta(beforeWholesale, afterWholesale,
                unit.wholesaleDraft == null ? 'Theo giá lẻ' : unit.wholesalePrice == null ? 'Giá sỉ riêng' : ''));
            const retailEdited = unit.draft !== unit.price, wholesaleEdited = unit.wholesaleDraft !== unit.wholesalePrice || afterWholesale !== beforeWholesale;
            row.classList.toggle('is-edited', retailEdited || wholesaleEdited);
            row.classList.toggle('rsp-current-loss', retailRate < 0 || wholesaleRate < 0);
            row.querySelector('[data-channel="retail"]').classList.toggle('is-edited', retailEdited);
            row.querySelector('[data-channel="wholesale"]').classList.toggle('is-edited', wholesaleEdited);
            row.querySelector('[data-channel="retail"]').classList.toggle('is-loss', unit.draft < unitCost);
            row.querySelector('[data-channel="wholesale"]').classList.toggle('is-loss', afterWholesale < unitCost);
            row.querySelector('[data-out="delta"]').classList.toggle('is-down', unit.draft < unit.price);
            row.querySelector('[data-out="wholesale-delta"]').classList.toggle('is-down', afterWholesale < beforeWholesale);
            setError(row, 'price', math.validMoney(unit.draft) ? '' : 'Nhập giá lẻ lớn hơn 0, tối đa 2 số thập phân.');
            setError(row, 'wholesale', unit.wholesaleDraft == null || math.validMoney(unit.wholesaleDraft) ? '' : 'Nhập giá sỉ hợp lệ, hoặc chọn Theo giá lẻ.');
            invalid ||= !math.validMoney(unit.draft) || (unit.wholesaleDraft != null && !math.validMoney(unit.wholesaleDraft));
            if (retailEdited && unit.draft < unitCost) belowCost++;
            if (wholesaleEdited && afterWholesale < unitCost) belowCost++;
            const retailSuggestion = row.querySelector('[data-action="suggest"]'), wholesaleSuggestion = row.querySelector('[data-action="suggest-wholesale"]');
            retailSuggestion.textContent = `Dùng ${money(unit.suggested)} đ cho lẻ`;
            wholesaleSuggestion.textContent = `Dùng ${money(unit.wholesaleSuggested)} đ cho sỉ`;
        }
        const defaultPrice = productPrice(), updateDefault = checked('rspUpdateProductPrice');
        invalid ||= updateDefault && !math.validMoney(defaultPrice);
        el('rspProductPrice').hidden = !updateDefault;
        el('rspProductPrice').textContent = updateDefault ? `Giá lẻ mặc định / ${data.baseUnitName}: ${money(data.productPrice)} → ${money(defaultPrice)} đ. Các biến thể dùng giá mặc định sẽ nhận giá này.` : '';
        const entries = changes(), reductions = entries.filter(entry => entry.after < entry.before);
        const warning = !validCost ? 'Chưa xác định được vốn dự kiến. Mở Tùy chọn nâng cao để nhập vốn dùng tính lãi.' :
            invalid ? 'Có ô giá chưa hợp lệ. Kiểm tra các ô được đánh dấu.' :
            belowCost ? `${belowCost} mức giá muốn áp dụng thấp hơn vốn dự kiến. Kiểm tra các dòng báo lỗ.` :
            reductions.length ? `${reductions.length} mức giá giảm so với đang bán. Các mức giảm sẽ được liệt kê trước khi áp dụng.` : '';
        el('rspWarning').hidden = !warning; el('rspWarning').textContent = warning;
        el('rspCount').textContent = entries.length ? `${entries.length} mức giá đang chỉnh sửa` : 'Chưa thay đổi giá bán';
        el('rspEditingHint').textContent = blocked ? 'Chưa xác nhận kết quả. Đóng và mở lại bảng giá để kiểm tra.' :
            saving ? 'Đang gửi thay đổi đến máy chủ…' : entries.length ? 'Các giá đang chỉnh chưa có hiệu lực.' : savedNotice;
        el('rspConfirmation').hidden = !confirming;
        el('rspSave').disabled = saving || blocked || invalid || entries.length === 0;
        el('rspSave').textContent = saving ? 'Đang áp dụng…' : confirming ? 'Xác nhận áp dụng giá mới' : entries.length ? `Xem lại ${entries.length} thay đổi` : 'Xem lại thay đổi';
        el('rspReview').disabled = saving || blocked || invalid || entries.length > 0;
        el('rspBackToEdit').disabled = saving;
        el('rspBackToEdit').hidden = !confirming;
        for (const control of el('rspContent').querySelectorAll('input,select,button'))
            control.disabled = saving || blocked || (confirming && control.id !== 'rspBackToEdit');
        el('rspProductSource').disabled ||= !updateDefault;
        el('rspUseSuggestions').disabled ||= units.length === 0 || units.some(unit => !Number.isFinite(unit.suggested) || !Number.isFinite(unit.wholesaleSuggested));
        for (const unit of units) {
            unit.row.querySelector('[data-field="wholesale"]').disabled ||= unit.wholesaleDraft == null;
            unit.row.querySelector('[data-action="suggest"]').disabled ||= !Number.isFinite(unit.suggested);
            unit.row.querySelector('[data-action="suggest-wholesale"]').disabled ||= !Number.isFinite(unit.wholesaleSuggested);
        }
        if (confirming) renderConfirmation(entries);
    }

    document.addEventListener('click', async event => {
        const button = event.target.closest('.js-receipt-selling-prices');
        if (!button || saving) return;
        const sequence = ++generation, row = button.closest('.commercial-line');
        variantId = Number(button.dataset.variantId);
        if (el('rspPrintLabels')) el('rspPrintLabels').hidden = true;
        data = null; units = []; blocked = false; confirming = false; allowDiscard = false; lastEdited = null; savedNotice = '';
        el('rspContent').hidden = true; el('rspSave').disabled = true; el('rspCount').textContent = ''; el('rspProduct').textContent = '';
        el('rspReview').disabled = true; el('rspConfirmation').hidden = true; el('rspDiscardChanges').hidden = true;
        el('rspCalculator').open = false; el('rspAdvanced').open = false; el('rspUpdateProductPrice').checked = false;
        message('Đang tải giá bán…');
        bootstrap.Modal.getOrCreateInstance(modal, { keyboard: true }).show();
        endpoint = `/admin/api/stock-documents/${window.stockDocumentPage.documentId}/lines/${button.dataset.lineId}/selling-prices`;
        try {
            if (window.stockDocumentPage.canApproveCommercial && window.GaoReceiptPriceDrafts) {
                const release = await window.GaoReceiptPriceDrafts.hold();
                if (sequence !== generation) { release(); return; }
                releaseReceiptDrafts = release;
            }
            const loaded = await request();
            if (sequence !== generation) return;
            data = loaded;
            const factor = Number(row.dataset.factor), quantity = Number(row.dataset.quantity);
            const price = num(row.querySelector('.commercial-unit-price'));
            const tax = Number(row.querySelector('.commercial-tax')?.selectedOptions[0]?.dataset.rate || 0);
            const vat = checked('commercialHasVat') && checked('includeVatInInventoryCost');
            const freight = checked('commercialHasFreight') && checked('capitalizeFreightInInventoryCost') ?
                num(row.querySelector('.commercial-freight-allocation')) || 0 : 0;
            const cost = math.baseCost(price, factor, quantity, tax, vat, freight);
            el('rspCost').value = Number.isFinite(cost) ? Math.round(cost * 10000) / 10000 : '';
            const last = Number(row.querySelector('.commercial-last-price')?.dataset.value || 0);
            el('rspCostNote').textContent = last > 0 ? `Giá nhập gần nhất / ${data.baseUnitName} (chưa VAT): ${money(last / factor)} đ.` : 'Chưa có giá nhập trước đó để so sánh.';
            el('rspProduct').textContent = data.productName; el('rspBaseUnit').textContent = data.baseUnitName;
            el('rspTarget').value = 10; el('rspWholesaleTarget').value = 10; el('rspBasis').value = 'margin'; el('rspRound').value = 100;
            units = data.units.map(unit => ({ ...unit, target: 10, wholesaleTarget: 10,
                draft: unit.price, wholesalePrice: unit.wholesalePrice ?? null, wholesaleDraft: unit.wholesalePrice ?? null }));
            el('rspProductSource').replaceChildren(...units.map(unit => new Option(`${unit.unitName} (1 = ${money(unit.factor)} ${data.baseUnitName})`, unit.id)));
            el('rspProductSource').value = data.productPriceUnitId;
            createRows(); history(); el('rspContent').hidden = false;
            if (!Number.isFinite(cost)) el('rspAdvanced').open = true;
            message(units.length ? '' : 'Chưa có đơn vị bán hoạt động. Vui lòng cấu hình đơn vị trong danh mục.', 'warning');
            render();
        } catch (error) { if (sequence === generation) message(error.message, 'danger'); }
    });
    modal.addEventListener('hide.bs.modal', event => {
        if (saving) { event.preventDefault(); return; }
        if (data && !blocked && !allowDiscard && changes().length > 0) {
            event.preventDefault(); el('rspDiscardChanges').hidden = false; el('rspKeepEditing').focus(); return;
        }
        generation++;
    });
    modal.addEventListener('hidden.bs.modal', () => { releaseReceiptDrafts?.(); releaseReceiptDrafts = undefined; });
    el('rspKeepEditing').addEventListener('click', () => { el('rspDiscardChanges').hidden = true; lastEdited?.focus(); });
    el('rspDiscardAndClose').addEventListener('click', () => { allowDiscard = true; bootstrap.Modal.getInstance(modal)?.hide(); });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || event.defaultPrevented || !modal.classList.contains('show')) return;
        const openModals = document.querySelectorAll('.modal.show');
        if (openModals[openModals.length - 1] !== modal) return;
        event.preventDefault(); bootstrap.Modal.getInstance(modal)?.hide();
    });
    modal.addEventListener('keydown', event => {
        if (event.key !== 'Enter' || !event.target.matches('[data-field="price"],[data-field="wholesale"]')) return;
        event.preventDefault();
        const fields = [...el('rspUnits').querySelectorAll('[data-field="price"],[data-field="wholesale"]')].filter(input => !input.disabled);
        const next = fields[fields.indexOf(event.target) + 1]; next?.focus();
    });
    modal.addEventListener('input', event => {
        if (!data || saving || confirming) return;
        const row = event.target.closest('[data-unit-id]');
        if (row) {
            const unit = units.find(item => item.id === Number(row.dataset.unitId));
            if (event.target.dataset.field === 'price') { unit.draft = math.parseMoney(event.target.value); lastEdited = event.target; }
            if (event.target.dataset.field === 'wholesale') { unit.wholesaleDraft = math.parseMoney(event.target.value); lastEdited = event.target; }
            if (event.target.dataset.field === 'wholesale-inherit') unit.wholesaleDraft = event.target.checked ? null : unit.wholesalePrice ?? unit.draft;
            if (event.target.dataset.field === 'target') unit.target = num(event.target);
            if (event.target.dataset.field === 'wholesale-target') unit.wholesaleTarget = num(event.target);
        } else if (event.target.id === 'rspTarget') {
            for (const unit of units) { unit.target = num(event.target); unit.row.querySelector('[data-field="target"]').value = event.target.value; }
        } else if (event.target.id === 'rspWholesaleTarget') {
            for (const unit of units) { unit.wholesaleTarget = num(event.target); unit.row.querySelector('[data-field="wholesale-target"]').value = event.target.value; }
        }
        el('rspDiscardChanges').hidden = true; render();
    });
    modal.addEventListener('focusout', event => {
        if (!data || saving || !event.target.matches('[data-field="price"],[data-field="wholesale"]')) return;
        const unit = units.find(item => item.id === Number(event.target.closest('[data-unit-id]').dataset.unitId));
        const value = event.target.dataset.field === 'price' ? unit.draft : unit.wholesaleDraft;
        if (math.validMoney(value)) event.target.value = money(value);
    });
    modal.addEventListener('change', event => {
        if (!data || saving || confirming) return;
        if (event.target.dataset.field === 'wholesale-inherit') {
            const unit = units.find(item => item.id === Number(event.target.closest('[data-unit-id]').dataset.unitId));
            unit.wholesaleDraft = event.target.checked ? null : unit.wholesalePrice ?? unit.draft;
        }
        render();
    });
    modal.addEventListener('click', event => {
        if (!data || saving || confirming) return;
        const button = event.target.closest('button'); if (!button) return;
        const row = button.closest('[data-unit-id]'), unit = row && units.find(item => item.id === Number(row.dataset.unitId));
        if (button.dataset.action === 'suggest' && Number.isFinite(unit?.suggested)) unit.draft = unit.suggested;
        if (button.dataset.action === 'suggest-wholesale' && Number.isFinite(unit?.wholesaleSuggested)) unit.wholesaleDraft = unit.wholesaleSuggested;
        if (button.id === 'rspUseSuggestions') for (const item of units) {
            if (Number.isFinite(item.suggested)) item.draft = item.suggested;
            if (Number.isFinite(item.wholesaleSuggested)) item.wholesaleDraft = item.wholesaleSuggested;
        }
        if (button.dataset.action || button.id === 'rspUseSuggestions') { el('rspDiscardChanges').hidden = true; render(); }
    });
    el('rspBackToEdit').addEventListener('click', () => { if (!saving) { confirming = false; render(); lastEdited?.focus(); } });
    el('rspSave').addEventListener('click', () => {
        if (!data || el('rspSave').disabled) return;
        if (!confirming) { confirming = true; render(); el('rspConfirmation').scrollIntoView({ behavior: 'smooth', block: 'nearest' }); }
        else save(false);
    });
    el('rspReview').addEventListener('click', () => save(true));
    async function save(reviewOnly) {
        if (!data || saving || el(reviewOnly ? 'rspReview' : 'rspSave').disabled) return;
        const writes = reviewOnly ? units : changedUnits(), entries = changes();
        const updateDefault = !reviewOnly && checked('rspUpdateProductPrice');
        saving = true; el('rspDiscardChanges').hidden = true; render();
        message(reviewOnly ? 'Đang ghi nhận kiểm tra…' : 'Đang áp dụng giá bán…');
        try {
            const updated = await request({ method: 'POST', headers: {
                'Content-Type': 'application/json', Accept: 'application/json',
                RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
            }, body: JSON.stringify({ productVersion: data.productVersion, variantVersion: data.variantVersion,
                catalogVersion: data.catalogVersion, productPriceUnitId: Number(el('rspProductSource').value),
                updateProductPrice: updateDefault, reviewOnly, lineVersion: data.lineVersion,
                estimatedBaseCost: num(el('rspCost')), basis: el('rspBasis').value,
                units: writes.map(unit => ({ id: unit.id, rowVersion: unit.rowVersion, price: reviewOnly ? unit.price : unit.draft,
                    updateWholesalePrice: true, wholesalePrice: reviewOnly ? unit.wholesalePrice : unit.wholesaleDraft,
                    targetPercent: target(unit.target), wholesaleTargetPercent: target(unit.wholesaleTarget) })) }) });
            data = updated;
            document.querySelectorAll(`[data-retail-variant="${variantId}"]`).forEach(cell => {
                const unitId = Number(cell.dataset.retailUnit), unit = data.units.find(item => item.id === unitId);
                const price = unit?.price ?? (unitId === 0 ? data.baseRetailPrice : null);
                cell.textContent = price > 0 ? `${money(price)} đ/${unit?.unitName || data.baseUnitName}` : 'Chưa có giá bán';
            });
            if (!reviewOnly && variantId > 0 && el('rspPrintLabels')) {
                el('rspPrintLabels').href = `/admin/label-printing?tab=quick&variantId=${variantId}&units=${writes.map(unit => unit.id).filter(id => id > 0).join(',')}`;
                el('rspPrintLabels').hidden = false;
            }
            for (const unit of units) {
                const saved = data.units.find(item => item.id === unit.id);
                if (saved) { unit.price = saved.price; unit.draft = saved.price; unit.wholesalePrice = saved.wholesalePrice ?? null;
                    unit.wholesaleDraft = saved.wholesalePrice ?? null; unit.rowVersion = saved.rowVersion; }
            }
            confirming = false; el('rspUpdateProductPrice').checked = false;
            savedNotice = reviewOnly ? 'Đã ghi nhận kiểm tra và giữ nguyên giá.' : 'Đã áp dụng giá mới thành công.';
            el('rspProductSource').value = data.productPriceUnitId;
            history(); message(reviewOnly ? 'Đã ghi nhận kiểm tra, giữ nguyên giá bán.' :
                `Đã cập nhật ${writes.length} đơn vị bán${updateDefault ? ' và giá lẻ mặc định' : ''} (${entries.length} mức giá). Giá mới dùng cho lần bán tiếp theo; các dòng đang có trong giỏ giữ nguyên giá.`, 'success');
            window.GaoReceiptPriceReviews?.refresh();
        } catch (error) {
            blocked = true;
            message(error.message + ' Đóng và mở lại bảng giá để kiểm tra kết quả trước khi thử lại.', 'danger');
        } finally { saving = false; render(); }
    }
})();
