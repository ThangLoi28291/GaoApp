(() => {
    'use strict';
    const root = document.getElementById('receiptBarcodeProposals');
    if (!root) return;
    const endpoint = `/admin/api/stock-documents/${Number(root.dataset.documentId)}/barcode-proposals`;
    const byId = id => document.getElementById(id);
    const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const number = value => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 4 }).format(value || 0);
    let state = null, selected = null, callback = null, saving = false, started = false;
    let modal, $candidate;
    let units = [], unitRequest = 0;
    const message = text => { byId('rbpMessage').textContent = text; };
    async function api(path = '', body) {
        const response = await fetch(endpoint + path, {
            method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin',
            headers: body === undefined ? {} : { 'Content-Type': 'application/json',
                RequestVerificationToken: root.querySelector('[name="__RequestVerificationToken"]').value },
            body: body === undefined ? undefined : JSON.stringify(body)
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.message || 'Không tải được mã mới. Hãy kiểm tra quyền và kết nối.');
        return result;
    }
    async function reload() {
        try {
            state = await api();
            document.dispatchEvent(new CustomEvent('receipt-barcode:state', { detail: state }));
            byId('rbpOpen').classList.toggle('d-none', !state.canPropose);
            const pending = state.items.filter(x => Number(x.status) === 1).length;
            byId('rbpCount').textContent = pending ? `${pending} chờ duyệt` : '';
            byId('rbpItems').innerHTML = state.items.length ? state.items.map(item => {
                const review = state.canReview && Number(item.status) === 1;
                const stamp = `${item.requestedByUserName || `Nhân viên #${item.requestedByUserId}`} · ${new Date(item.requestedAtUtc).toLocaleString('vi-VN')}`;
                return `<article class="border rounded p-3 mt-2" data-proposal-id="${Number(item.id)}">
                    <div class="d-flex justify-content-between flex-wrap gap-2"><strong>${escape(item.suggestedBarcode || 'Không có mã hãng')}</strong>
                        <span class="badge ${Number(item.status) === 1 ? 'bg-label-warning' : 'bg-label-secondary'}">${escape(item.statusText)}</span></div>
                    <div>${escape(item.productNameSnapshot)} · <strong>${escape(item.unitNameSnapshot)}</strong> · Quy đổi ×${number(item.factorSnapshot)}</div>
                    <div class="small text-muted mt-1">${escape(stamp)}</div>
                    ${item.employeeNote ? `<div class="small mt-1">Ghi chú: ${escape(item.employeeNote)}</div>` : ''}
                    ${item.resolvedAtUtc ? `<div class="small text-muted mt-1">Xử lý: ${escape(item.resolvedByUserName || `Nhân viên #${item.resolvedByUserId}`)} · ${escape(new Date(item.resolvedAtUtc).toLocaleString('vi-VN'))}</div>` : ''}
                    ${item.managerNote ? `<div class="small">Ý kiến quản lý: ${escape(item.managerNote)}</div>` : ''}
                    ${review ? `<label class="form-label small mt-2" for="rbpReviewNote${Number(item.id)}">Ghi chú duyệt / từ chối</label>
                        <input id="rbpReviewNote${Number(item.id)}" class="form-control form-control-sm" maxlength="1000" />
                        <div class="d-flex gap-2 mt-2">${item.suggestedBarcode ? '<button type="button" class="btn btn-sm btn-success" data-review="approve">Duyệt thêm mã</button>' : ''}
                        <button type="button" class="btn btn-sm btn-outline-danger" data-review="reject">Từ chối mã</button></div>` : ''}</article>`;
            }).join('') : '<p class="small text-muted mb-0">Phiếu chưa có mã mới được đề xuất.</p>';
        } catch (error) { message(error.message); }
    }
    function open(code = '', next) {
        if (!started || !state?.canPropose || saving) return false;
        callback = next || (item => {
            if (typeof window.openQtyPopup === 'function') window.openQtyPopup(item);
            else if (typeof window.openConfirmProductPopup === 'function') {
                window.jQuery('#quickLookupInput').data('selected-item', item);
                window.openConfirmProductPopup(item);
            } else document.dispatchEvent(new CustomEvent('receipt-barcode:selected', { detail: item }));
        });
        resetProduct();
        byId('rbpCode').value = code;
        byId('rbpNote').value = '';
        byId('rbpModalError').textContent = '';
        $candidate.val(null).trigger('change');
        window.jQuery('#quickLookupInput,#rwReceiveLookup').each(function () {
            if (window.jQuery(this).hasClass('select2-hidden-accessible')) window.jQuery(this).select2('close');
        });
        modal.show();
        return true;
    }
    function resetProduct() {
        unitRequest++;
        selected = null; units = [];
        byId('rbpSave').disabled = true;
        byId('rbpProduct').classList.add('d-none');
        byId('rbpUnits').replaceChildren();
        byId('rbpConversion').textContent = 'Chưa chọn sản phẩm.';
    }
    function groupProducts(data) {
        const products = new Map();
        for (const item of data.results || []) {
            if (!item.productUnitConversionId || !item.productVariantId) continue;
            if (!products.has(item.productVariantId)) products.set(item.productVariantId, {
                id: String(item.productVariantId), productVariantId: item.productVariantId,
                text: item.productName, productName: item.productName, sku: item.sku, imageUrl: item.imageUrl
            });
        }
        return { results: [...products.values()] };
    }
    function formatProduct(item) {
        if (!item.id) return item.text || '';
        return window.jQuery(`<div class="rbp-search-result">
            ${item.imageUrl ? `<img src="${escape(item.imageUrl)}" alt="" />` : ''}
            <div><strong>${escape(item.productName || item.text)}</strong><small>${escape(item.sku || '')}</small></div></div>`);
    }
    async function loadUnits(product) {
        resetProduct();
        const requestId = unitRequest;
        byId('rbpConversion').textContent = 'Đang tải các đơn vị của sản phẩm…';
        byId('rbpModalError').textContent = '';
        try {
            const result = await api(`/products/${Number(product.productVariantId)}/units`);
            if (requestId !== unitRequest) return;
            units = (result.items || []).filter(x => Number(x.productVariantId) === Number(product.productVariantId));
            if (!units.length) throw new Error('Sản phẩm chưa có đơn vị đang sử dụng. Hãy chọn sản phẩm khác.');
            const imageUrl = units[0].imageUrl || product.imageUrl;
            const img = byId('rbpImage');
            img.classList.toggle('d-none', !imageUrl);
            byId('rbpNoImage').classList.toggle('d-none', !!imageUrl);
            img.alt = units[0].productName || product.productName;
            if (imageUrl) img.src = imageUrl; else img.removeAttribute('src');
            byId('rbpProductName').textContent = units[0].productName || product.productName;
            for (const id of ['rbpExistingCode', 'rbpSelectedUnit', 'rbpSelectedFactor', 'rbpEquivalent']) byId(id).textContent = '—';
            byId('rbpUnits').innerHTML = units.map(item => `<button type="button" class="rbp-unit" data-conversion-id="${Number(item.productUnitConversionId)}" aria-pressed="false">
                <strong>${escape(item.unitName)}</strong><span>Quy đổi: ×${number(item.factor)}</span>
                <span>Mã: ${escape(item.barcode || 'Chưa có')}</span></button>`).join('');
            byId('rbpProduct').classList.remove('d-none');
            byId('rbpConversion').textContent = 'Chọn đơn vị trên bao bì của mã mới.';
            if (units.length === 1) chooseUnit(units[0].productUnitConversionId);
        } catch (error) {
            if (requestId !== unitRequest) return;
            byId('rbpConversion').textContent = 'Chưa chọn sản phẩm / đơn vị.';
            byId('rbpModalError').textContent = error.message;
        }
    }
    function chooseUnit(conversionId) {
        if (saving) return;
        selected = units.find(x => Number(x.productUnitConversionId) === Number(conversionId)) || null;
        if (!selected) return;
        byId('rbpUnits').querySelectorAll('[data-conversion-id]').forEach(button => {
            button.setAttribute('aria-pressed', String(Number(button.dataset.conversionId) === Number(conversionId)));
        });
        byId('rbpExistingCode').textContent = selected.barcode || 'Chưa có';
        byId('rbpSelectedUnit').textContent = selected.unitName;
        byId('rbpSelectedFactor').textContent = `×${number(selected.factor)}`;
        byId('rbpEquivalent').textContent = `${number(selected.factor)} ${selected.baseUnitName}`;
        byId('rbpConversion').textContent = `1 ${selected.unitName} = ${number(selected.factor)} ${selected.baseUnitName}. Mã hãng mới sẽ được gắn vào ${selected.unitName} của sản phẩm này.`;
        byId('rbpSave').disabled = false;
        byId('rbpModalError').textContent = '';
    }
    function start() {
        if (started) return;
        started = true;
        modal = bootstrap.Modal.getOrCreateInstance(byId('rbpModal'));
        $candidate = window.jQuery('#rbpCandidate');
        $candidate.select2({ dropdownParent: window.jQuery('#rbpModal'), theme: 'bootstrap-5', width: '100%',
            minimumInputLength: 1, placeholder: 'Tên sản phẩm, SKU hoặc mã nội bộ…',
            ajax: { url: endpoint + '/lookup', dataType: 'json', delay: 200,
                data: params => ({ term: params.term || '', catalogOnly: true }),
                processResults: groupProducts }, templateResult: formatProduct
        }).on('select2:select', event => loadUnits(event.params.data))
            .on('select2:opening', event => { if (saving) event.preventDefault(); })
            .on('select2:open', () => {
                byId('rbpModal').querySelector('.select2-search__field')?.focus();
            });
        // Wait for Bootstrap's focus trap and opening animation before focusing.
        byId('rbpModal').addEventListener('shown.bs.modal', () => {
            if (byId('rbpCode').value.trim()) $candidate.select2('open');
            else byId('rbpCode').focus();
        });
        byId('rbpCode').addEventListener('keydown', event => {
            if (event.key === 'Enter' && byId('rbpCode').value.trim()) {
                event.preventDefault();
                $candidate.select2('open');
            }
        });
        byId('rbpUnits').addEventListener('click', event => {
            const button = event.target.closest('[data-conversion-id]');
            if (button) chooseUnit(button.dataset.conversionId);
        });
        byId('rbpImage').addEventListener('error', () => {
            byId('rbpImage').classList.add('d-none'); byId('rbpNoImage').classList.remove('d-none');
        });
        byId('rbpModal').addEventListener('hidden.bs.modal', () => {
            unitRequest++;
            $candidate.select2('close');
        });
        byId('rbpOpen').addEventListener('click', () => open());
        byId('rbpReload').addEventListener('click', reload);
        byId('rbpSave').addEventListener('click', async () => {
            if (saving) return;
            if (!selected) { byId('rbpModalError').textContent = 'Hãy chọn sản phẩm và đúng đơn vị.'; return; }
            saving = true; byId('rbpSave').disabled = true;
            try {
                const workbench = byId('receivingWorkbench');
                const result = await api('', { productUnitConversionId: selected.productUnitConversionId,
                    factor: selected.factor, barcode: byId('rbpCode').value, note: byId('rbpNote').value,
                    leaseToken: workbench?.dataset.leaseToken || sessionStorage.getItem(`gaoapp:receiving-lease:${root.dataset.documentId}`) || null });
                const item = { ...result.item, id: String(result.item.productUnitConversionId) };
                const next = callback;
                byId('rbpModal').addEventListener('hidden.bs.modal', () => next?.(item), { once: true });
                modal.hide();
                message('Đã lưu mã mới chờ duyệt. Tiếp tục nhập số lượng cho đơn vị vừa chọn.');
                await reload();
            } catch (error) { byId('rbpModalError').textContent = error.message; }
            finally { saving = false; byId('rbpSave').disabled = false; }
        });
        byId('rbpItems').addEventListener('click', async event => {
            const button = event.target.closest('[data-review]');
            if (!button || saving) return;
            const row = button.closest('[data-proposal-id]');
            saving = true;
            row.querySelectorAll('button').forEach(x => { x.disabled = true; });
            try {
                const result = await api(`/${Number(row.dataset.proposalId)}/review`, {
                    approve: button.dataset.review === 'approve', note: row.querySelector('input').value });
                message(result.message); await reload();
            } catch (error) { message(error.message); row.querySelectorAll('button').forEach(x => { x.disabled = false; }); }
            finally { saving = false; }
        });
        const fallback = byId('btnCaptureProvisional');
        byId('rbpUnknownProduct').classList.toggle('d-none', !fallback);
        byId('rbpUnknownProduct').addEventListener('click', () => {
            const rawInput = byId('rbpCode').value;
            byId('rbpModal').addEventListener('hidden.bs.modal', () => {
                document.dispatchEvent(new CustomEvent('provisional:capture-request', { detail: { rawInput } }));
            }, { once: true });
            modal.hide();
        });
        reload();
    }
    window.ReceiptBarcodeProposals = { open, reload, lookupUrl: endpoint + '/lookup' };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
