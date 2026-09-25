let quickAddProductModalInstance = null;
let requestRevisionModalInstance = null;
let submitReceivingModalInstance = null;
let barcodeVerificationModalInstance = null;
let receivingQuickCreateModalInstance = null;
let barcodeVerificationItems = [];
let receivingPendingSaves = 0;
let receivingSaveFailed = false;
let receivingSubmissionInProgress = false;
let receivingRefreshSequence = 0;
let receivingAddCommand = null;
let receivingAddBusy = false;
let receivingLookupSequence = 0;
let receivingLookupTerm = '';
const receivingSavingMessage = 'Đang lưu hàng nhập. Vui lòng chờ trước khi gửi duyệt.';
const receivingSaveFailedMessage = 'Chưa lưu được thay đổi. Vui lòng kiểm tra lại hàng nhập và lưu lại trước khi gửi duyệt.';
document.addEventListener('DOMContentLoaded', function () {

    const modalEl = document.getElementById('quickAddProductModal');
    if (modalEl) {
        quickAddProductModalInstance = new bootstrap.Modal(modalEl);
        modalEl.addEventListener('hide.bs.modal', event => { if (receivingAddBusy && !receivingAddCommand?.saved) event.preventDefault(); });
    }

    const revisionModalEl = document.getElementById('requestRevisionModal');
    if (revisionModalEl) {
        requestRevisionModalInstance = new bootstrap.Modal(revisionModalEl);
    }
    const submitModalEl = document.getElementById('submitReceivingModal');
    if (submitModalEl) {
        submitReceivingModalInstance = new bootstrap.Modal(submitModalEl);
    }
    const barcodeModalEl = document.getElementById('barcodeVerificationModal');
    if (barcodeModalEl) {
        barcodeVerificationModalInstance = new bootstrap.Modal(barcodeModalEl);
    }
    const quickCreateModalEl = document.getElementById('receivingQuickCreateProductModal');
    if (quickCreateModalEl) {
        receivingQuickCreateModalInstance = new bootstrap.Modal(quickCreateModalEl);
    }
    initReceivingLookup();
    bindPopupAddLine();
    bindPopupQtyAutoSelect();
    bindReceivingQtyInputs();
    bindDeleteReceivingLine();
    bindSubmitReceiving();
    bindRequestRevision();
    bindBarcodeVerification();
    bindReceivingQuickCreateProduct();
    loadMissingBarcodeVerification(false);

    setTimeout(focusQuickLookup, 250);
});

async function readApiResponse(response) {
    const text = await response.text();

    try {
        return {
            ok: response.ok,
            data: JSON.parse(text)
        };
    } catch {
        return {
            ok: response.ok,
            data: { message: text }
        };
    }
}

function initReceivingLookup() {
    const el = $('#quickLookupInput');
    if (!el.length) return;

    el.select2({
        theme: 'bootstrap-5',
        width: '100%',
        placeholder: 'Quét mã / tìm tên có dấu hoặc không dấu…',
        minimumInputLength: 1,
        language: { inputTooShort: () => 'Nhập tên hoặc quét mã sản phẩm', searching: () => 'Đang tìm…',
            noResults: () => window.ReceiptIntake?.noResults() || 'Không tìm thấy sản phẩm.' },
        ajax: {
            url: window.ReceiptBarcodeProposals?.lookupUrl || '/admin/warehouse-receiving/product-lookup-select2',
            dataType: 'json',
            delay: 120,
            data: function (params) {
                return { term: params.term || '' };
            },
            processResults: function (data, params) {
                const term = params.term || '';
                receivingLookupTerm = term;
                const items = data.results || [];

                return {
                    results: groupLookupResultsForTextSearch(items, term)
                };
            }
        },
        templateResult: formatLookupResult,
        templateSelection: formatLookupSelection,
        escapeMarkup: function (markup) {
            return markup;
        }
    });

    el.on('select2:select', function (e) {
        const item = e.params.data;
        // This is an action lookup, not a persistent selection. Otherwise Select2
        // only closes on Enter when the employee chooses the same product again.
        el.val(null).trigger('change');

        if (item.isGroupedVariant) {
            openGroupedProductPopup(item);
            return;
        }

        openQtyPopup(item);
    });

    document.addEventListener('keydown', async function (e) {
        const field = e.target;
        if (e.key !== 'Enter' || e.isComposing || !field.matches('.select2-container--open .select2-search__field') ||
            !field.getAttribute('aria-controls')?.includes('quickLookupInput')) return;
        const term = field.value.trim();
        if (!term) return;
        const widget = el.data('select2');
        // Select2 owns its result data cache and keyboard highlight. Let it select the
        // highlighted current result; jQuery .data('data') does not read that cache.
        if (receivingLookupTerm === term && !widget?.results?.$results?.find('.loading-results').length &&
            widget?.results?.getHighlightedResults().filter('.select2-results__option--selectable').length) return;
        // A fast barcode can arrive before its result list. Resolve that term instead
        // of letting Enter accept a highlight left over from the previous search.
        e.preventDefault(); e.stopImmediatePropagation();
        const sequence = ++receivingLookupSequence;
        try {
        const response = await fetch(`${window.ReceiptBarcodeProposals?.lookupUrl || '/admin/warehouse-receiving/product-lookup-select2'}?term=${encodeURIComponent(term)}`);
        const api = await readApiResponse(response);
        if (sequence !== receivingLookupSequence || field.value.trim() !== term || !field.isConnected || document.querySelector('.modal.show')) return;
        if (!api.ok || !api.data?.results) return;
        if (api.data.results.length === 0) {
            window.ReceiptBarcodeProposals?.open(term, openQtyPopup);
            return;
        }
        let results = api.data.results || [];
        results = groupLookupResultsForTextSearch(results, term);
        const item = results.length === 1 ? results[0] : null;
        if (!item) return;
        const option = new Option(item.text, item.id, true, true);

        el.append(option).trigger('change');
        el.trigger({
            type: 'select2:select',
            params: { data: item }
        });

        el.select2('close');
        } catch { document.getElementById('wrdSaveStatus')?.replaceChildren(document.createTextNode('Không tìm được sản phẩm. Kiểm tra kết nối rồi thử lại.')); }
    }, true);
}
function openGroupedProductPopup(group) {
    if (!group || !Array.isArray(group.units) || group.units.length === 0) {
        return;
    }

    if (receivingAddBusy || receivingPendingSaves) return;
    receivingAddCommand = null;
    $('#quickLookupInput').select2('close');

    const firstUnit = group.units[0];

    $('#quickLookupInput').data('grouped-item', group);
    $('#quickLookupInput').data('selected-item', firstUnit);

    setText('popupProductName', group.productName || group.text || '-');

    setPopupProductImage(group.imageUrl || firstUnit.imageUrl, group.productName || group.text);

    renderUnitChooser(group.units, firstUnit.unitId);

    applySelectedUnitToPopup(firstUnit);

    const qty = document.getElementById('popupQuickQty');
    if (qty) qty.value = '1';

    updatePopupBaseQty();
    setText('popupQuickMessage', '');

    quickAddProductModalInstance?.show();

    setTimeout(function () {
        focusAndSelectPopupQty();
    }, 350);
}

function renderUnitChooser(units, selectedUnitId) {
    const wrap = document.getElementById('popupUnitChooser');
    const list = document.getElementById('popupUnitChooserList');

    if (!wrap || !list) return;

    wrap.classList.remove('d-none');

    list.innerHTML = units.map(unit => {
        const selected = Number(unit.unitId || 0) === Number(selectedUnitId || 0);

        return `
            <button type="button"
                    class="wrd-unit-choice ${selected ? 'is-selected' : ''}"
                    data-unit-id="${unit.unitId || ''}">
                <div class="wrd-unit-name">
                    ${escapeHtml(unit.unitName || '-')}
                </div>

                <div class="wrd-unit-factor">
                    Quy đổi: x${escapeHtml(unit.factor || '1')}
                </div>

                <div class="wrd-unit-barcode">
                    Barcode: ${escapeHtml(unit.barcode || '-')}
                </div>
            </button>
        `;
    }).join('');

    list.querySelectorAll('.wrd-unit-choice').forEach(btn => {
        btn.addEventListener('click', function () {
            const unitId = Number(this.dataset.unitId || 0);
            const group = $('#quickLookupInput').data('grouped-item');

            if (!group || !Array.isArray(group.units)) return;

            const selected = group.units.find(x => Number(x.unitId || 0) === unitId);
            if (!selected) return;

            list.querySelectorAll('.wrd-unit-choice')
                .forEach(x => x.classList.remove('is-selected'));

            this.classList.add('is-selected');

            $('#quickLookupInput').data('selected-item', selected);

            applySelectedUnitToPopup(selected);
            updatePopupBaseQty();

            setTimeout(function () {
                focusAndSelectPopupQty();
            }, 80);
        });
    });
}

function applySelectedUnitToPopup(item) {
    setValue('popupProductVariantId', item.productVariantId || '');
    setValue('popupUnitId', item.unitId || '');

    setText('popupBarcode', item.barcode || '-');
    setText('popupUnitName', item.unitName || '-');
    setText('popupFactor', item.factor || 1);
    setText('popupRetailPrice', formatMoney(item.price || 0));
}
function formatMoney(value) {
    return new Intl.NumberFormat('vi-VN').format(value || 0);
}
function focusAndSelectPopupQty() {
    if (window.matchMedia('(max-width: 768px)').matches) return;
    const qty = document.getElementById('popupQuickQty');
    if (!qty) return;

    qty.focus({ preventScroll: true });

    setTimeout(function () {
        qty.select();
    }, 30);

    setTimeout(function () {
        qty.focus({ preventScroll: true });
        qty.select();
    }, 120);
}

function bindPopupQtyAutoSelect() {
    const modalEl = document.getElementById('quickAddProductModal');
    const qty = document.getElementById('popupQuickQty');

    if (modalEl && modalEl.dataset.qtyFocusBound !== '1') {
        modalEl.dataset.qtyFocusBound = '1';

        modalEl.addEventListener('shown.bs.modal', function () {
            focusAndSelectPopupQty();
        });
    }

    if (qty && qty.dataset.selectBound !== '1') {
        qty.dataset.selectBound = '1';

        qty.addEventListener('focus', function () {
            this.select();
        });

        qty.addEventListener('click', function () {
            this.select();
        });

        qty.addEventListener('mouseup', function (e) {
            e.preventDefault();
        });
    }
}
function openQtyPopup(item) {
    if (!item) return;
    if (receivingAddBusy || receivingPendingSaves) return;
    receivingAddCommand = null;
    $('#quickLookupInput').removeData('grouped-item');

    const unitChooser = document.getElementById('popupUnitChooser');
    const unitChooserList = document.getElementById('popupUnitChooserList');

    if (unitChooser) unitChooser.classList.add('d-none');
    if (unitChooserList) unitChooserList.innerHTML = '';

    $('#quickLookupInput').select2('close');
    $('#quickLookupInput').data('selected-item', item);

    setValue('popupProductVariantId', item.productVariantId || '');
    setValue('popupUnitId', item.unitId || '');

    setText('popupProductName', item.productName || item.text || '-');
    setText('popupBarcode', item.barcode || '-');
    setText('popupUnitName', item.unitName || '-');
    setText('popupFactor', item.factor || 1);
    setText('popupRetailPrice', formatMoney(item.price || 0));

    const qty = document.getElementById('popupQuickQty');
    if (qty) qty.value = '1';

    setPopupProductImage(item.imageUrl, item.productName || item.text);

    updatePopupBaseQty();
    setText('popupQuickMessage', '');

    quickAddProductModalInstance?.show();

    setTimeout(function () {
        focusAndSelectPopupQty();
    }, 350);
}

function setPopupProductImage(imageUrl, productName) {
    const img = document.getElementById('popupProductImage');
    const noImg = document.getElementById('popupProductNoImage');
    if (!img || !noImg) return;

    img.alt = productName ? `Ảnh ${productName}` : 'Ảnh sản phẩm đang nhập';
    img.onerror = function () {
        img.classList.add('d-none');
        noImg.classList.remove('d-none');
    };
    if (imageUrl) {
        img.src = imageUrl;
        img.classList.remove('d-none');
        noImg.classList.add('d-none');
    } else {
        img.removeAttribute('src');
        img.classList.add('d-none');
        noImg.classList.remove('d-none');
    }
}

function bindPopupAddLine() {
    const qty = document.getElementById('popupQuickQty');
    const btn = document.getElementById('btnPopupQuickAddLine');

    qty?.addEventListener('input', updatePopupBaseQty);

    qty?.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            btn?.click();
        }
    });

    btn?.addEventListener('click', addReceivingLine);
}

function updateReceivingSubmitState() {
    const btn = document.getElementById('btnConfirmSubmitReceiving');
    const msg = document.getElementById('submitReceivingMessage');
    if (btn) btn.disabled = receivingPendingSaves > 0 || receivingSaveFailed || receivingSubmissionInProgress || !!window.WarehouseReceivingQuantity?.hasPending();
    if (!msg) return;
    if (receivingPendingSaves > 0) msg.textContent = receivingSavingMessage;
    else if (receivingSaveFailed) msg.textContent = receivingSaveFailedMessage;
    else if (msg.textContent === receivingSavingMessage || msg.textContent === receivingSaveFailedMessage) msg.textContent = '';
}

function receivingHasPendingChanges() {
    return !!window.WarehouseReceivingQuantity?.hasPending() || receivingPendingSaves > 0 || receivingSaveFailed || receivingSubmissionInProgress || !!receivingAddCommand?.uncertain ||
        [...document.querySelectorAll('.js-receiving-qty')].some(x => parseDecimalInput(x.value) !== parseDecimalInput(x.defaultValue));
}

function receivingSaveEvent(state, message, item, feedback) {
    if (state === 'error') receivingSaveFailed = true;
    if (state === 'saved' && receivingPendingSaves === 0) receivingSaveFailed = false;
    document.dispatchEvent(new CustomEvent('receiving:save', {detail:{state,message,item,feedback}}));
}
function receivingRetryChanges() {
    if (receivingAddCommand || $('#quickLookupInput').data('selected-item')) { quickAddProductModalInstance?.show(); return; }
    if (window.ReceiptIntake?.retryPending?.()) return;
    const dirty = [...document.querySelectorAll('.js-receiving-qty')].find(x => parseDecimalInput(x.value) !== parseDecimalInput(x.defaultValue));
    if (dirty) { dirty.scrollIntoView({block:'center'}); dirty.focus(); saveReceivingQty(dirty); }
}

async function withReceivingLineSave(action) {
    if (receivingSubmissionInProgress) return false;
    if (receivingPendingSaves === 0) receivingSaveFailed = false;
    receivingPendingSaves++;
    receivingSaveEvent('saving');
    updateReceivingSubmitState();
    try {
        const saved = await action();
        if (!saved) receivingSaveFailed = true;
        return saved;
    } catch (error) {
        receivingSaveFailed = true;
        alert(error.message || 'Không lưu được hàng nhập. Vui lòng thử lại.');
        return false;
    } finally {
        receivingPendingSaves--;
        updateReceivingSubmitState();
        if (receivingPendingSaves === 0) receivingSaveEvent(receivingSaveFailed ? 'error' : 'saved');
    }
}

function addReceivingLine() {
    if (receivingAddBusy || receivingPendingSaves > 0) return Promise.resolve(false);
    receivingAddBusy = true;
    return withReceivingLineSave(addReceivingLineCore).finally(() => { receivingAddBusy = false; });
}

async function addReceivingLineCore() {
    const selected = $('#quickLookupInput').data('selected-item');
    const documentId = window.warehouseReceivingDetail?.documentId || 0;
    const qty = parseDecimalInput(document.getElementById('popupQuickQty')?.value || '0');
    const btn = document.getElementById('btnPopupQuickAddLine');

    if (!selected) {
        setText('popupQuickMessage', 'Vui lòng chọn sản phẩm.');
        return false;
    }

    if (qty <= 0) {
        setText('popupQuickMessage', 'Số lượng phải lớn hơn 0.');
        return false;
    }

    try {
        btn.disabled = true;
        btn.textContent = 'Đang thêm...';
        document.querySelectorAll('#quickAddProductModal input, #quickAddProductModal [data-popup-step], #popupUnitChooserList button').forEach(x=>x.disabled=true);

        let conversion = selected;
        if (!conversion.productUnitConversionId) {
            const unitsResponse = await fetch(`/admin/api/stock-documents/${documentId}/barcode-proposals/products/${Number(selected.productVariantId)}/units`, {cache:'no-store'});
            const unitsApi = await readApiResponse(unitsResponse);
            if (!unitsApi.ok) throw new Error(unitsApi.data?.message || 'Không tải được đơn vị sản phẩm.');
            conversion = unitsApi.data.items?.find(x => Number(x.unitId) === Number(selected.unitId));
            if (!conversion) throw new Error('Không tìm thấy đơn vị nhập. Hãy chọn lại sản phẩm.');
        }
        const payload = {productUnitConversionId:Number(conversion.productUnitConversionId),factor:Number(conversion.factor),quantity:qty,barcode:selected.barcode || null,note:null};
        const identity = JSON.stringify(payload);
        if (!receivingAddCommand || (!receivingAddCommand.uncertain && receivingAddCommand.identity !== identity)) receivingAddCommand = {identity,commandId:crypto.randomUUID()};
        receivingAddCommand.feedback ??= window.WarehouseReceivingFeedback?.prepare({
            commandId:receivingAddCommand.commandId, conversionId:payload.productUnitConversionId,
            name:selected.productName || selected.text, unitName:selected.unitName || conversion.unitName || '',
            barcode:selected.barcode || selected.sku || '', factor:payload.factor, quantity:qty, imageUrl:selected.imageUrl
        });
        const response = await fetch(`/admin/api/stock-documents/${documentId}/intake/known`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                ...payload,commandId:receivingAddCommand.commandId,documentRowVersion:window.warehouseReceivingDetail.rowVersion,
                leaseToken:sessionStorage.getItem(`gaoapp:receiving-lease:${documentId}`) || null
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            setText('popupQuickMessage', api.data?.message || 'Thêm dòng thất bại.');
            receivingAddCommand.uncertain = response.status >= 500;
            return false;
        }

        window.warehouseReceivingDetail.rowVersion = api.data.documentRowVersion;
        window.WarehouseReceivingFeedback?.loadState(api.data.recentReceipts);
        await refreshReceivingLines();
        await window.ReceiptIntake?.reload();
        receivingAddCommand.saved = true;
        quickAddProductModalInstance?.hide();
        $('#quickLookupInput').val(null).trigger('change').removeData('selected-item');
        const feedback = receivingAddCommand.feedback ? {...receivingAddCommand.feedback, next:api.data} : null;
        receivingAddCommand = null;
        receivingSaveEvent('saved',null,`${selected.productName || selected.text} · ${formatDecimal(qty)} ${selected.unitName || ''}`,feedback);
        setTimeout(focusQuickLookup, 150);
        return true;
    } catch (error) {
        if(receivingAddCommand)receivingAddCommand.uncertain=true;
        setText('popupQuickMessage', receivingAddCommand ? 'Chưa nhận đủ xác nhận lưu. Giữ nguyên số lượng và bấm Thêm dòng để kiểm tra, thử lại; hàng không bị cộng hai lần.' : error.message);
        return false;
    } finally {
        btn.disabled = false;
        btn.textContent = 'Thêm dòng';
        document.querySelectorAll('#quickAddProductModal input, #quickAddProductModal [data-popup-step], #popupUnitChooserList button').forEach(x=>x.disabled=!!receivingAddCommand?.uncertain);
    }
}

async function refreshReceivingLines() {
    const documentId = window.warehouseReceivingDetail?.documentId || 0;
    const sequence = ++receivingRefreshSequence;

    const response = await fetch(`/admin/warehouse-receiving/${documentId}/lines`, {
        method: 'GET',
        cache: 'no-store'
    });

    if (!response.ok) {
        throw new Error('Không tải lại được danh sách hàng. Vui lòng tải lại phiếu trước khi gửi duyệt.');
    }

    const html = await response.text();
    if (sequence !== receivingRefreshSequence) return;
    const template = document.createElement('template');
    template.innerHTML = html;
    const snapshot = template.content.querySelector('[data-receiving-row-version]');
    const rowVersion = snapshot?.dataset.receivingRowVersion || '';
    let validVersion = false;
    try { validVersion = atob(rowVersion).length === 8; } catch { /* Invalid snapshot: do not enable submission. */ }
    if (Number(snapshot?.dataset.receivingDocumentId) !== Number(documentId) || !validVersion) {
        throw new Error('Không đọc được phiên bản phiếu. Vui lòng tải lại trang.');
    }
    // Advance the version only with the corresponding visible receipt snapshot.
    // Fetching a fresh token at submission would bypass unseen concurrent edits.
    const viewport = window.WarehouseReceivingApp?.captureListViewport();
    document.getElementById('wrdLinesContainer').replaceChildren(template.content);
    window.WarehouseReceivingFeedback?.restoreOrder();
    window.warehouseReceivingDetail.rowVersion = rowVersion;

    bindReceivingQtyInputs();
    bindDeleteReceivingLine();
    updateSummaryFromDom();
    window.WarehouseReceivingApp?.restoreListViewport(viewport);
    await loadMissingBarcodeVerification(false);
}

function bindReceivingQtyInputs() {
    document.querySelectorAll('.js-receiving-qty').forEach(input => {
        if (input.dataset.bound === '1') return;

        input.dataset.bound = '1';
        let focusLookupAfterSave = false;

        input.addEventListener('focus', function () {
            this.select();
        });

        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                focusLookupAfterSave = true;
                this.blur();
            }
        });

        input.addEventListener('blur', async function () {
            if (this.dataset.receiptStepSaving === '1') return;
            if (Number(this.value) !== Number(this.defaultValue)) await saveReceivingQty(this);

            if (focusLookupAfterSave) {
                focusLookupAfterSave = false;
                focusQuickLookup();
            }
        });
    });
}

function saveReceivingQty(input) {
    return withReceivingLineSave(() => saveReceivingQtyCore(input));
}

async function saveReceivingQtyCore(input) {
    const documentId = window.warehouseReceivingDetail?.documentId || 0;
    const lineId = input.dataset.lineId;
    const qty = parseDecimalInput(input.value);
    const unitId = Number(input.dataset.unitId || 0);
    const note = input.dataset.note || null;

    if (!lineId || !unitId) return false;

    if (qty <= 0) {
        alert('Số lượng phải lớn hơn 0.');
        input.focus();
        input.select();
        return false;
    }

    const response = await fetch(`/admin/api/stock-documents/${documentId}/lines/${lineId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            unitId: unitId,
            quantity: qty,

            // Null yêu cầu server giữ nguyên snapshot giá; nhân viên không được
            // nhìn thấy hoặc thay đổi giá thương mại.
            unitCost: null,
            note: note
        })
    });

    const api = await readApiResponse(response);

    if (!api.ok) {
        alert(api.data?.message || 'Cập nhật số lượng thất bại.');
        return false;
    }

    await refreshReceivingLines();
    return true;
}

function bindDeleteReceivingLine() {
    document.querySelectorAll('.btn-delete-receiving-line').forEach(btn => {
        if (btn.dataset.bound === '1') return;

        btn.dataset.bound = '1';

        btn.addEventListener('click', async function () {
            const lineId = this.dataset.lineId;
            const productName = this.dataset.productName || '';

            if (!confirm(`Xóa dòng "${productName}" khỏi phiếu nhập?`)) {
                return;
            }

            await withReceivingLineSave(async () => {
                const documentId = window.warehouseReceivingDetail?.documentId || 0;
                const response = await fetch(`/admin/api/stock-documents/${documentId}/lines/${lineId}`, {
                    method: 'DELETE'
                });
                const api = await readApiResponse(response);
                if (!api.ok) {
                    alert(api.data?.message || 'Xóa dòng thất bại.');
                    return false;
                }
                await refreshReceivingLines();
                focusQuickLookup();
                return true;
            });
        });
    });
}

function bindSubmitReceiving() {
    const btnOpen = document.getElementById('btnSubmitReceiving');
    const btnConfirm = document.getElementById('btnConfirmSubmitReceiving');

    if (btnOpen && btnOpen.dataset.bound !== '1') {
        btnOpen.dataset.bound = '1';

        btnOpen.addEventListener('click', function () {
            setText('submitReceivingMessage', '');
            updateReceivingSubmitState();
            submitReceivingModalInstance?.show();
        });
    }

    if (btnConfirm && btnConfirm.dataset.bound !== '1') {
        btnConfirm.dataset.bound = '1';

        btnConfirm.addEventListener('click', submitReceivingForApproval);
    }
}
async function submitReceivingForApproval() {
    if (window.WarehouseReceivingQuantity?.hasPending() && !await window.WarehouseReceivingQuantity.flush()) {
        setText('submitReceivingMessage', 'Chưa lưu xong số lượng. Bấm Lưu lại trước khi gửi duyệt.');
        return;
    }
    if (window.ReceiptIntake?.isSaving() || window.ReceiptQuantityControls?.hasFailed()) {
        setText('submitReceivingMessage', window.ReceiptQuantityControls?.hasFailed()
            ? 'Chưa lưu được số lượng. Vui lòng kiểm tra dòng hàng và lưu lại trước khi gửi duyệt.'
            : 'Đang lưu hàng nhận. Vui lòng chờ lưu xong trước khi gửi duyệt.');
        return;
    }
    if (receivingPendingSaves > 0 || receivingSaveFailed || receivingSubmissionInProgress) {
        updateReceivingSubmitState();
        return;
    }
    const documentId = window.warehouseReceivingDetail?.documentId || 0;
    const btn = document.getElementById('btnConfirmSubmitReceiving');
    const msg = document.getElementById('submitReceivingMessage');

    if (msg) msg.textContent = '';

    if (!documentId) {
        if (msg) msg.textContent = 'Không xác định được phiếu nhập.';
        return;
    }

    try {
        receivingSubmissionInProgress = true;
        btn.disabled = true;
        btn.textContent = 'Đang gửi...';

        const response = await fetch(`/admin/api/stock-documents/${documentId}/submit-approval`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                note: 'Nhân viên kho gửi duyệt.',
                rowVersion: window.warehouseReceivingDetail?.rowVersion || ''
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Gửi duyệt thất bại.';
            return;
        }

        submitReceivingModalInstance?.hide();

        showReceivingToast('success', api.data?.message || 'Đã gửi phiếu cho quản lý duyệt.');

        setTimeout(function () {
            window.location.href = '/admin/warehouse-receiving';
        }, 900);
    } catch (error) {
        console.error(error);

        if (msg) {
            msg.textContent = 'Có lỗi khi gửi duyệt.';
        }
    } finally {
        receivingSubmissionInProgress = false;
        updateReceivingSubmitState();
        btn.textContent = 'Gửi quản lý duyệt';
    }
}
function updatePopupBaseQty() {
    const qty = parseDecimalInput(document.getElementById('popupQuickQty')?.value || '0');
    const factor = parseDecimalInput(document.getElementById('popupFactor')?.textContent || '1');
    const selected = $('#quickLookupInput').data('selected-item');
    const box = document.getElementById('popupBaseQuantityBox');

    if (selected?.isBaseUnit) {
        box?.classList.add('d-none');
        return;
    }

    box?.classList.remove('d-none');
    setText('popupBaseQty', `${formatDecimal(qty * factor)} ${selected?.baseUnitName || ''}`.trim());
}

function updateSummaryFromDom() {
    const rows = document.querySelectorAll('#wrdLinesContainer tr[data-receiving-key]');

    let totalQty = 0;
    rows.forEach(row => totalQty += parseDecimalInput(row.querySelector('.wrd-table-qty')?.value || row.dataset.receivingQuantity || '0'));

    setText('wrdTotalLines', rows.length.toString());
    setText('wrdTotalQty', formatDecimal(totalQty));
}

function bindReceivingQuickCreateProduct() {
    const modal = document.getElementById('receivingQuickCreateProductModal');
    const openButton = document.getElementById('btnReceivingQuickCreateProduct');
    const confirmButton = document.getElementById('btnConfirmReceivingQuickCreate');
    const supplier = $('#receivingQuickSupplier');
    const unit = document.getElementById('receivingQuickUnit');

    if (!modal || !confirmButton) return;

    if (supplier.length && $.fn.select2) {
        supplier.select2({
            theme: 'bootstrap-5',
            width: '100%',
            dropdownParent: $('#receivingQuickCreateProductModal'),
            placeholder: 'Gõ tên, mã, số điện thoại hoặc mã số thuế...',
            minimumInputLength: 1,
            ajax: {
                url: '/admin/warehouse-receiving/supplier-lookup',
                dataType: 'json',
                delay: 220,
                data: function (params) {
                    return { term: params.term || '', page: params.page || 1 };
                },
                processResults: function (data) {
                    return {
                        results: data.results || [],
                        pagination: data.pagination || { more: false }
                    };
                }
            },
            language: {
                inputTooShort: function () { return 'Nhập từ khóa nhà cung cấp'; },
                searching: function () { return 'Đang tìm...'; },
                noResults: function () { return 'Không tìm thấy nhà cung cấp'; }
            }
        });
    }

    function updateOtherUnit() {
        const isOther = unit?.value === '__other';
        document.getElementById('receivingQuickOtherUnitWrap')?.classList.toggle('d-none', !isOther);
    }
    unit?.addEventListener('change', updateOtherUnit);

    function openModal() {
        const activeSearch = document.querySelector('.select2-container--open .select2-search__field');
        const term = (activeSearch?.value || '').trim();
        try { $('#quickLookupInput').select2('close'); } catch { }
        setValue('receivingQuickProductName', term);
        setText('receivingQuickCreateMessage', '');
        receivingQuickCreateModalInstance?.show();
        setTimeout(function () {
            const input = document.getElementById('receivingQuickProductName');
            input?.focus();
            input?.select();
        }, 250);
    }

    openButton?.addEventListener('click', openModal);
    document.addEventListener('keydown', function (event) {
        if (event.key !== 'F4' || modal.classList.contains('show')) return;
        const canEdit = document.getElementById('CanEditReceiving')?.value === 'true';
        if (!canEdit) return;
        event.preventDefault();
        openModal();
    });
    modal.addEventListener('keydown', function (event) {
        if (event.ctrlKey && event.key === 'Enter') {
            event.preventDefault();
            confirmButton.click();
        }
    });
    confirmButton.addEventListener('click', createReceivingPendingProduct);
    updateOtherUnit();
}

async function createReceivingPendingProduct() {
    const button = document.getElementById('btnConfirmReceivingQuickCreate');
    const name = (document.getElementById('receivingQuickProductName')?.value || '').trim();
    const supplierId = Number($('#receivingQuickSupplier').val() || 0);
    const categoryId = Number(document.getElementById('receivingQuickCategory')?.value || 0);
    const rawUnit = document.getElementById('receivingQuickUnit')?.value || '';
    const isOtherUnit = rawUnit === '__other';
    const unitId = isOtherUnit ? null : Number(rawUnit || 0) || null;
    const newUnitName = isOtherUnit
        ? (document.getElementById('receivingQuickOtherUnit')?.value || '').trim()
        : null;

    setText('receivingQuickCreateMessage', '');
    if (!name) return setText('receivingQuickCreateMessage', 'Vui lòng nhập tên sản phẩm.');
    if (!supplierId) return setText('receivingQuickCreateMessage', 'Vui lòng chọn nhà cung cấp.');
    if (!categoryId) return setText('receivingQuickCreateMessage', 'Vui lòng chọn danh mục.');
    if (!unitId && !newUnitName) return setText('receivingQuickCreateMessage', 'Vui lòng chọn hoặc nhập đơn vị gốc.');

    try {
        button.disabled = true;
        button.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Đang tạo...';
        const token = document.querySelector('#warehouseReceivingAntiforgery input[name="__RequestVerificationToken"]')?.value || '';
        const response = await fetch('/admin/warehouse-receiving/quick-create-product', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify({ name, supplierId, categoryId, unitId, newUnitName })
        });
        const api = await readApiResponse(response);
        if (!api.ok) {
            setText('receivingQuickCreateMessage', api.data?.message || 'Không tạo được sản phẩm.');
            return;
        }

        const created = api.data?.product;
        if (!created) {
            setText('receivingQuickCreateMessage', 'Máy chủ không trả về sản phẩm vừa tạo.');
            return;
        }
        receivingQuickCreateModalInstance?.hide();
        showReceivingToast('success', api.data?.message || 'Đã tạo sản phẩm chờ hoàn thiện.');
        setTimeout(function () { openQtyPopup(created); }, 220);
    } catch (error) {
        console.error(error);
        setText('receivingQuickCreateMessage', 'Có lỗi khi tạo sản phẩm.');
    } finally {
        button.disabled = false;
        button.innerHTML = '<i class="bx bx-plus-circle me-1"></i>Tạo và nhập số lượng';
    }
}

function focusQuickLookup() {
    if (window.matchMedia('(max-width: 768px)').matches || document.querySelector('.modal.show, .offcanvas.show')) return;
    const el = $('#quickLookupInput');
    if (!el.length) return;

    setTimeout(function () {
        if (document.querySelector('.modal.show, .offcanvas.show')) return;
        el.select2('open');
    }, 80);
}

function formatLookupResult(item) {
    if (!item.id) return item.text || '';
    if (item.isGroupedVariant) {
        const units = item.units || [];

        const unitText = units
            .map(x => `${x.unitName || '-'} x${x.factor || 1}`)
            .join(' · ');

        const image = item.imageUrl
            ? `<img class="wrd-select-img" src="${escapeHtml(item.imageUrl)}" />`
            : `<div class="wrd-select-img-empty"><i class="bx bx-image"></i></div>`;

        return `
        <div class="wrd-select-item">
            ${image}
            <div>
                <div class="wrd-select-title">${escapeHtml(item.productName || item.text || '')}</div>
                <div class="wrd-select-meta">
                    Có ${units.length} đơn vị: ${escapeHtml(unitText)}
                </div>
            </div>
        </div>
    `;
    }
    const image = item.imageUrl
        ? `<img class="wrd-select-img" src="${escapeHtml(item.imageUrl)}" />`
        : `<div class="wrd-select-img-empty"><i class="bx bx-image"></i></div>`;

    return `
        <div class="wrd-select-item">
            ${image}
            <div>
                <div class="wrd-select-title">${escapeHtml(item.productName || item.text || '')}</div>
                <div class="wrd-select-meta">
                    Barcode: ${escapeHtml(item.barcode || '-')}
                    · ĐVT: ${escapeHtml(item.unitName || '-')}
                    · Factor: ${escapeHtml(item.factor || '1')}
                </div>
            </div>
        </div>
    `;
}

function formatLookupSelection(item) {
    if (!item || !item.id) return item.text || 'Chọn sản phẩm';

    if (item.isGroupedVariant) {
        return item.productName || item.text || 'Sản phẩm';
    }

    return item.productName || item.text || 'Sản phẩm';
}

function parseDecimalInput(value) {
    return Number(String(value ?? '0').replace(',', '.')) || 0;
}

function formatDecimal(value) {
    return Number(value || 0).toFixed(3).replace(/\.?0+$/, '');
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function setText(id, value) {
    const el = document.getElementById(id);
    if (el) el.textContent = value ?? '';
}

function setValue(id, value) {
    const el = document.getElementById(id);
    if (el) el.value = value ?? '';
}
function bindRequestRevision() {
    const btnOpen = document.getElementById('btnRequestRevision');
    const btnConfirm = document.getElementById('btnConfirmRevision');
    const modalEl = document.getElementById('requestRevisionModal');
    const note = document.getElementById('txtRevisionNote');

    if (btnOpen && btnOpen.dataset.bound !== '1') {
        btnOpen.dataset.bound = '1';

        btnOpen.addEventListener('click', function () {
            setValue('txtRevisionNote', '');
            setText('revisionRequestMessage', '');

            requestRevisionModalInstance?.show();

            setTimeout(function () {
                note?.focus();
            }, 250);
        });
    }

    if (modalEl && modalEl.dataset.revisionFocusBound !== '1') {
        modalEl.dataset.revisionFocusBound = '1';

        modalEl.addEventListener('shown.bs.modal', function () {
            note?.focus();
        });

        modalEl.addEventListener('keydown', function (e) {
            if (e.ctrlKey && e.key === 'Enter') {
                e.preventDefault();
                btnConfirm?.click();
            }
        });
    }

    if (btnConfirm && btnConfirm.dataset.bound !== '1') {
        btnConfirm.dataset.bound = '1';

        btnConfirm.addEventListener('click', sendRevisionRequest);
    }
}

async function sendRevisionRequest() {
    const documentId = window.warehouseReceivingDetail?.documentId || 0;
    const note = (document.getElementById('txtRevisionNote')?.value || '').trim();
    const msg = document.getElementById('revisionRequestMessage');
    const btn = document.getElementById('btnConfirmRevision');

    if (msg) msg.textContent = '';

    if (!documentId) {
        if (msg) msg.textContent = 'Không xác định được phiếu nhập.';
        return;
    }

    if (!note) {
        if (msg) msg.textContent = 'Vui lòng nhập lý do cần sửa.';
        document.getElementById('txtRevisionNote')?.focus();
        return;
    }

    try {
        btn.disabled = true;
        btn.textContent = 'Đang gửi...';

        const response = await fetch(`/admin/api/stock-documents/${documentId}/request-revision`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                note: note,
                rowVersion: window.warehouseReceivingDetail?.rowVersion || ''
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Không thể gửi yêu cầu sửa.';
            return;
        }

        requestRevisionModalInstance?.hide();

        alert(api.data?.message || 'Đã gửi yêu cầu sửa phiếu.');
        window.location.reload();
    } catch (error) {
        console.error(error);

        if (msg) {
            msg.textContent = 'Có lỗi khi gửi yêu cầu sửa.';
        }
    } finally {
        btn.disabled = false;
        btn.textContent = 'Gửi yêu cầu';
    }
}
function showReceivingToast(type, message) {
    let toast = document.getElementById('wrdToast');

    if (!toast) {
        toast = document.createElement('div');
        toast.id = 'wrdToast';
        toast.className = 'wrd-toast';
        document.body.appendChild(toast);
    }

    toast.innerHTML = `
        <div class="wrd-toast-icon">✓</div>
        <div class="wrd-toast-text">${escapeHtml(message || '')}</div>
    `;

    toast.classList.add('show');

    setTimeout(function () {
        toast.classList.remove('show');
    }, 1600);
}
function bindBarcodeVerification() {
    const btnOpen = document.getElementById('btnOpenBarcodeVerificationModal');
    const btnSubmit = document.getElementById('btnSubmitBarcodeVerification');

    if (btnOpen && btnOpen.dataset.bound !== '1') {
        btnOpen.dataset.bound = '1';

        btnOpen.addEventListener('click', async function () {
            await loadMissingBarcodeVerification(true);
            barcodeVerificationModalInstance?.show();
        });
    }

    if (btnSubmit && btnSubmit.dataset.bound !== '1') {
        btnSubmit.dataset.bound = '1';

        btnSubmit.addEventListener('click', submitBarcodeVerificationRequests);
    }
}

async function loadMissingBarcodeVerification(renderModalList = false) {
    const documentId = window.warehouseReceivingDetail?.documentId || 0;
    const alert = document.getElementById('barcodeVerificationAlert');
    const countEl = document.getElementById('barcodeVerificationCount');

    if (!documentId) return;

    try {
        const response = await fetch(`/admin/api/stock-documents/${documentId}/barcode-verification/missing`, {
            method: 'GET',
            cache: 'no-store'
        });

        const api = await readApiResponse(response);

        if (!api.ok) return;

        barcodeVerificationItems = api.data?.items || [];

        if (barcodeVerificationItems.length > 0) {
            alert?.classList.remove('d-none');
            if (countEl) countEl.textContent = barcodeVerificationItems.length;
        } else {
            alert?.classList.add('d-none');
            if (countEl) countEl.textContent = '0';
        }

        if (renderModalList) {
            renderBarcodeVerificationList();
        }
    } catch (error) {
        console.error(error);
    }
}

function renderBarcodeVerificationList() {
    const wrap = document.getElementById('barcodeVerificationList');
    const msg = document.getElementById('barcodeVerificationMessage');

    if (msg) msg.textContent = '';

    if (!wrap) return;

    if (!barcodeVerificationItems.length) {
        wrap.innerHTML = `
            <div class="alert alert-success rounded-4 mb-0">
                Không còn đơn vị nào cần chuẩn hóa barcode.
            </div>
        `;
        return;
    }

    wrap.innerHTML = barcodeVerificationItems.map(item => `
        <div class="border rounded-4 p-3 mb-3 bg-white js-barcode-verification-row"
             data-conversion-id="${item.productUnitConversionId}">
            <div class="d-flex justify-content-between align-items-start flex-wrap gap-2">
                <div>
                    <div class="fw-bold fs-6">
                        ${escapeHtml(item.productName || '')}
                    </div>

                    <div class="small text-muted mt-1">
                        Đơn vị: <b>${escapeHtml(item.unitName || '')}</b>
                        · Factor: <b>x${escapeHtml(item.factor || '1')}</b>
                        · Mã cũ: <b>${escapeHtml(item.legacyBarcode || '-')}</b>
                    </div>
                </div>

                <span class="badge bg-label-warning">Cần xử lý</span>
            </div>

            <div class="row g-2 mt-3 align-items-end">
                <div class="col-md-6">
                    <label class="form-label fw-semibold">Mã nhà sản xuất / nhà cung cấp</label>
                    <input type="text"
                           class="form-control js-suggested-barcode"
                           placeholder="Quét hoặc nhập barcode nếu có..." />
                </div>

                <div class="col-md-3">
                    <div class="form-check mt-4">
                        <input type="checkbox"
                               class="form-check-input js-no-supplier-barcode"
                               id="noBarcode_${item.productUnitConversionId}" />

                        <label class="form-check-label"
                               for="noBarcode_${item.productUnitConversionId}">
                            Không có mã NSX
                        </label>
                    </div>
                </div>

                <div class="col-md-3">
                    <label class="form-label fw-semibold">Ghi chú</label>
                    <input type="text"
                           class="form-control js-barcode-note"
                           placeholder="Nếu có..." />
                </div>
            </div>
        </div>
    `).join('');

    wrap.querySelectorAll('.js-no-supplier-barcode').forEach(chk => {
        chk.addEventListener('change', function () {
            const row = chk.closest('.js-barcode-verification-row');
            const input = row?.querySelector('.js-suggested-barcode');

            if (!input) return;

            if (chk.checked) {
                input.value = '';
                input.disabled = true;
            } else {
                input.disabled = false;
                input.focus();
            }
        });
    });
}

async function submitBarcodeVerificationRequests() {
    const documentId = window.warehouseReceivingDetail?.documentId || 0;
    const msg = document.getElementById('barcodeVerificationMessage');
    const btn = document.getElementById('btnSubmitBarcodeVerification');

    if (msg) msg.textContent = '';

    const rows = document.querySelectorAll('.js-barcode-verification-row');

    const items = [];

    rows.forEach(row => {
        const productUnitConversionId = Number(row.dataset.conversionId || 0);
        const barcode = row.querySelector('.js-suggested-barcode')?.value?.trim() || '';
        const noSupplierBarcode = row.querySelector('.js-no-supplier-barcode')?.checked === true;
        const note = row.querySelector('.js-barcode-note')?.value?.trim() || '';

        if (!productUnitConversionId) return;

        if (!barcode && !noSupplierBarcode) return;

        items.push({
            productUnitConversionId: productUnitConversionId,
            suggestedBarcode: barcode,
            noSupplierBarcode: noSupplierBarcode,
            note: note
        });
    });

    if (!items.length) {
        if (msg) msg.textContent = 'Vui lòng nhập barcode hoặc tick Không có mã NSX ít nhất 1 dòng.';
        return;
    }

    try {
        btn.disabled = true;
        btn.textContent = 'Đang gửi...';

        const response = await fetch(`/admin/api/stock-documents/${documentId}/barcode-verification/requests`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                items: items
            })
        });

        const api = await readApiResponse(response);

        if (!api.ok) {
            if (msg) msg.textContent = api.data?.message || 'Gửi yêu cầu thất bại.';
            return;
        }

        barcodeVerificationModalInstance?.hide();

        showReceivingToast('success', api.data?.message || 'Đã gửi yêu cầu chuẩn hóa barcode.');

        await loadMissingBarcodeVerification(false);
    } catch (error) {
        console.error(error);

        if (msg) {
            msg.textContent = 'Có lỗi khi gửi yêu cầu.';
        }
    } finally {
        btn.disabled = false;
        btn.textContent = 'Gửi quản lý xử lý';
    }
}
function isBarcodeTerm(term) {
    term = String(term || '').trim();

    if (!term) return false;

    return /^[0-9]{6,}$/.test(term);
}

function groupLookupResultsForTextSearch(items, term) {
    if (isBarcodeTerm(term)) {
        return items;
    }

    const map = new Map();

    items.forEach(item => {
        const key = Number(item.productVariantId || 0);

        if (!key) return;

        if (!map.has(key)) {
            map.set(key, {
                id: `variant_${key}`,
                text: item.productName || item.text || '',
                productVariantId: item.productVariantId,
                productName: item.productName || item.text || '',
                imageUrl: item.imageUrl,
                isGroupedVariant: true,
                units: []
            });
        }

        const group = map.get(key);

        group.units.push({
            productUnitConversionId: item.productUnitConversionId,
            baseUnitName: item.baseUnitName,
            isBaseUnit: item.isBaseUnit,
            id: item.id,
            productVariantId: item.productVariantId,
            unitId: item.unitId,
            productName: item.productName,
            sku: item.sku,
            barcode: item.barcode,
            unitName: item.unitName,
            factor: item.factor,
            imageUrl: item.imageUrl,
            price: item.price,
            sourceType: item.sourceType,
            text: item.text
        });
    });

    return Array.from(map.values());
}
