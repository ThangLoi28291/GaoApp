(() => {
    'use strict';
    const root = document.getElementById('receiptIntake');
    if (!root) return;
    const id = Number(root.dataset.documentId), endpoint = `/admin/api/stock-documents/${id}/intake`;
    const byId = id => document.getElementById(id);
    const esc = text => String(text ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const num = value => new Intl.NumberFormat('vi-VN', {maximumFractionDigits:3}).format(Number(value) || 0);
    let config, state, barcodeState, modal, reviewModal, $product;
    let tab = 'existing', product = null, units = [], selected = null, addingUnit = false;
    let saving = false, loading = false, allowClose = false, requestNumber = 0, reviewItem = null, command = null, commandPayload = null;
    let pendingIntake = false, uncertainIntake = false;
    let scanFeedback = null;
    const values = name => byId(name).value.trim();
    const number = name => Number(values(name).replace(',', '.'));
    const lease = () => document.getElementById('receivingWorkbench')?.dataset.leaseToken || sessionStorage.getItem(`gaoapp:receiving-lease:${id}`) || null;
    function rowVersion() {
        return window.stockDocumentRowVersion?.current() || window.warehouseReceivingDetail?.rowVersion ||
            document.getElementById('receivingWorkbench')?.dataset.rowVersion || state?.documentRowVersion;
    }
    async function api(path = '', body) {
        const response = await fetch(endpoint + path, {method:body === undefined ? 'GET':'POST', credentials:'same-origin', cache:'no-store',
            headers:body === undefined ? {} : {'Content-Type':'application/json', RequestVerificationToken:root.querySelector('[name="__RequestVerificationToken"]').value},
            body:body === undefined ? undefined:JSON.stringify(body)});
        const result = await response.json().catch(() => ({}));
        if (!response.ok) { const error=new Error(result.message || 'Không lưu được hàng nhận. Hãy kiểm tra kết nối và thử lại.');error.status=response.status;throw error; }
        return result;
    }
    function applyState(next) {
        state = next;
        if (!next.documentRowVersion) throw new Error('Thiếu phiên bản phiếu. Vui lòng tải lại.');
        window.stockDocumentRowVersion?.update(next.documentRowVersion);
        if (window.warehouseReceivingDetail) window.warehouseReceivingDetail.rowVersion = next.documentRowVersion;
        const workbench = document.getElementById('receivingWorkbench');
        if (workbench) workbench.dataset.rowVersion = next.documentRowVersion;
        document.dispatchEvent(new CustomEvent('provisional:state', {detail:next}));
        renderPending();
    }
    const fold = text => String(text || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').toLowerCase().trim();
    function options(select, items, placeholder) {
        select.innerHTML = `<option value="">${esc(placeholder)}</option>` + items.map(x => `<option value="${Number(x.id)}">${esc(x.text)}</option>`).join('');
    }
    function initAutocomplete() {
        for (const id of ['riBaseUnit','riReceiveUnit','riCategory','riReviewCategory']) {
            const unit = id === 'riBaseUnit' || id === 'riReceiveUnit';
            const $select = window.jQuery(byId(id));
            $select.select2({theme:'bootstrap-5', width:'100%', tags:unit, selectOnClose:unit,
                dropdownParent:window.jQuery(byId(id).closest('.modal')), minimumResultsForSearch:0,
                placeholder:unit ? 'Gõ tên đơn vị…':'Gõ tìm danh mục…',
                matcher:(params,data)=>!params.term || fold(data.text).includes(fold(params.term)) ? data:null,
                language:{noResults:()=> 'Không tìm thấy kết quả'},
                createTag:params=>{
                    const name=params.term.trim();
                    return name && name.length <= 100 && ![...byId(id).options].some(x=>fold(x.text)===fold(name))
                        ? {id:'new:'+name,text:name,newTag:true}:null;
                },
                templateResult:data=>data.newTag ? window.jQuery('<span>').text(`＋ Dùng đơn vị “${data.text}”`):data.text
            }).on('select2:open',()=>byId(id).closest('.modal').querySelector('.select2-search__field')?.focus());
        }
        window.jQuery('#riBaseUnit,#riReceiveUnit').on('change', function () {
            if (this.id === 'riBaseUnit') {
                const base=unitValue('riBaseUnit');
                if (!values('riReceiveUnit')) selectDeclaredUnit('riReceiveUnit',null,base.id,base.name);
                byId('riFactor').value=base.name && fold(base.name)===fold(unitValue('riReceiveUnit').name) ? '1':'';
            }
            if (this.id === 'riReceiveUnit') {
                const receive=unitValue('riReceiveUnit');
                const existing=tab === 'existing' && addingUnit && units.find(x=>x.unitId===receive.id || fold(x.unitName)===fold(receive.name));
                if (existing) { chooseUnit(String(existing.productUnitConversionId)); return; }
                const pack=packing(); byId('riFactor').value=pack.unitName && fold(pack.unitName)===fold(pack.baseUnitName) ? '1':'';
            }
            update();
        });
    }
    async function reload() {
        try {
            config = await api(); applyState(config.state);
            window.WarehouseReceivingFeedback?.loadState(config.recentReceipts);
            const choices = config.options?.units || [];
            if (!byId('riBaseUnit').options.length) {
                options(byId('riBaseUnit'), choices, 'Gõ tên đơn vị gốc…');
                options(byId('riReceiveUnit'), choices, 'Gõ tên đơn vị nhận…');
                options(byId('riCategory'), config.options?.categories || [], 'Chọn danh mục');
                options(byId('riReviewCategory'), config.options?.categories || [], 'Chọn danh mục');
                initAutocomplete();
            }
            byId('riPageMessage').textContent = '';
        } catch (error) { byId('riPageMessage').textContent = error.message; }
    }
    function renderPending() {
        const photos=(state?.items || []).filter(x=>x.hasPhoto && x.status!==2);
        byId('riPhotoEvidence').hidden=!photos.length;byId('riPhotoCount').textContent=`(${photos.length})`;
        byId('riPhotoItems').innerHTML=photos.map(item=>`<a href="${endpoint}/${Number(item.id)}/photo" target="_blank" rel="noopener"><img src="${endpoint}/${Number(item.id)}/photo" alt="Ảnh bao bì ${esc(item.name)}" loading="lazy" /><span>${esc(item.name)}<small>${num(item.quantity)} ${esc(item.unitName)}</small></span></a>`).join('');
        const items = (state?.items || []).filter(x => x.status === 0);
        const barcodeItems = (barcodeState?.items || []).filter(x => x.status === 1);
        byId('riPendingCount').textContent = items.length + barcodeItems.length;
        byId('receiptIntakePending').hidden = !items.length && !barcodeItems.length;
        byId('receiptBarcodeProposals').hidden = !barcodeItems.length;
        byId('riPendingItems').innerHTML = items.map(item => {
            const packing = item.proposedFactor ? `1 ${item.unitName} = ${num(item.proposedFactor)} ${item.proposedBaseUnitName}` : 'Chưa khai báo quy đổi';
            const equivalent = item.proposedFactor ? ` · ${num(item.quantity * item.proposedFactor)} ${item.proposedBaseUnitName}` : '';
            return `<article class="ri-pending-item"><div><strong>${esc(item.name)}</strong><small>${esc(item.rawBarcode || 'Không có barcode')} · ${num(item.quantity)} ${esc(item.unitName)}${esc(equivalent)}</small><small>${esc(packing)} · ${item.proposedProductVariantId ? 'Quy cách mới chờ duyệt':'Sản phẩm mới chờ duyệt'}</small>${item.note ? `<small>${esc(item.note)}</small>`:''}</div>
                <div class="ri-pending-buttons">${config?.canReview && item.proposedFactor ? `<button type="button" class="btn btn-sm btn-outline-primary" data-ri-review="${item.id}">Xem và duyệt</button>`:''}
                ${config?.canCapture ? `<button type="button" class="btn btn-sm btn-outline-secondary" data-ri-repeat="${item.id}">Nhận thêm</button><button type="button" class="btn btn-sm btn-outline-danger" data-ri-remove="${item.id}">Loại</button>`:''}</div></article>`;
        }).join('');
    }
    function unitValue(selectId) {
        const value = values(selectId);
        return {id:Number(value) > 0 ? Number(value):null,
            name:value ? byId(selectId).selectedOptions[0].textContent.trim():''};
    }
    function packing() {
        if (tab === 'existing' && !addingUnit) return {unitId:selected?.unitId, unitName:selected?.unitName,
            baseUnitId:units.find(x => x.isBaseUnit)?.unitId, baseUnitName:selected?.baseUnitName, factor:selected?.factor};
        const receive = unitValue('riReceiveUnit','riReceiveUnitName');
        const base = tab === 'new' ? unitValue('riBaseUnit','riBaseUnitName') :
            {id:units.find(x => x.isBaseUnit)?.unitId, name:units[0]?.baseUnitName};
        return {unitId:receive.id, unitName:receive.name, baseUnitId:base.id, baseUnitName:base.name, factor:number('riFactor')};
    }
    function update() {
        byId('riPacking').hidden = tab !== 'new' && !addingUnit;
        const pack = packing(), qty = number('riQuantity');
        byId('riReceivingLabel').textContent = pack.unitName || 'đơn vị';
        byId('riBaseLabel').textContent = pack.baseUnitName || 'đơn vị gốc';
        byId('riEquivalent').innerHTML = pack.factor > 0 && pack.unitName && pack.baseUnitName
            ? `${num(qty)} ${esc(pack.unitName)} × ${num(pack.factor)}<strong>= ${num(qty * pack.factor)} ${esc(pack.baseUnitName)}</strong>`
            : 'Chọn đơn vị và nhập tỷ lệ quy đổi.';
        const needsCatalog = tab === 'new' || addingUnit;
        const rights = config?.permissions || {};
        const catalogRight = tab === 'new' ? rights.canCreateProduct : rights.canUpdateProduct;
        const needsUnit = !pack.unitId || !pack.baseUnitId;
        const canApprove = needsCatalog && config?.canReview && catalogRight && (!values('riBarcode') || rights.canCreateBarcode) && (!needsUnit || rights.canCreateUnit);
        byId('riApproveWrap').hidden = !canApprove;
        if (!canApprove) byId('riApproveNow').checked = false;
        byId('riSave').disabled = saving || loading || !config?.canCapture || (tab === 'existing' && (!product || (!selected && !addingUnit)));
        byId('riSave').textContent = saving ? 'Đang lưu…' : byId('riApproveNow').checked ? 'Duyệt và nhận hàng' : 'Lưu và nhận hàng';
    }
    function switchTab(next) {
        if (saving) return;
        tab = next;
        rootTabs().forEach(button => {
            const active = button.dataset.intakeTab === tab;
            button.setAttribute('aria-selected', String(active)); button.tabIndex = active ? 0:-1;
        });
        byId('riExistingPanel').hidden = tab !== 'existing'; byId('riNewPanel').hidden = tab !== 'new';
        byId('riError').textContent = ''; update();
    }
    const rootTabs = () => byId('receiptIntakeModal').querySelectorAll('[data-intake-tab]');
    function clearProduct() {
        requestNumber++; product = null; units = []; selected = null; addingUnit = false;
        byId('riProductCard').hidden = true; byId('riUnitCards').replaceChildren(); update();
    }
    function selectDeclaredUnit(selectId, nameId, unitId, unitName) {
        const select = byId(selectId);
        const existing = [...select.options].find(x => unitId ? x.value === String(unitId) : unitName && fold(x.text) === fold(unitName));
        const value=existing?.value || (unitName ? unitId ? String(unitId):'new:'+unitName : '');
        if (value && !existing) select.add(new Option(unitName,value));
        window.jQuery(select).val(value).trigger('change.select2');
    }
    async function prefillItem(item) {
        if (item.proposedProductVariantId) {
            $product.append(new Option(item.name, String(item.proposedProductVariantId), true, true)).trigger('change');
            await chooseProduct({productVariantId:item.proposedProductVariantId, productName:item.name});
            if (!product) return;
            chooseUnit('new', false);
        }
        byId('riNewName').value = item.name;
        selectDeclaredUnit('riBaseUnit','riBaseUnitName',item.proposedBaseUnitId,item.proposedBaseUnitName);
        selectDeclaredUnit('riReceiveUnit','riReceiveUnitName',item.unitId,item.unitName);
        byId('riFactor').value = item.proposedFactor || '';
        window.jQuery('#riCategory').val(item.proposedCategoryId || '').trigger('change.select2');
        byId('riNote').value = item.note || ''; update();
        $product.select2('close'); byId('riQuantity').focus(); byId('riQuantity').select();
    }
    async function chooseProduct(choice) {
        clearProduct(); const revision = requestNumber; loading = true; update();
        try {
            const response = await fetch(`/admin/api/stock-documents/${id}/barcode-proposals/products/${Number(choice.productVariantId)}/units`, {cache:'no-store'});
            const result = await response.json();
            if (!response.ok) throw new Error(result.message || 'Không tải được đơn vị sản phẩm.');
            if (revision !== requestNumber) return;
            units = result.items || []; if (!units.length) throw new Error('Sản phẩm chưa có đơn vị đang hoạt động.');
            product = choice; byId('riProductName').textContent = units[0].productName;
            byId('riProductBase').textContent = `Đơn vị gốc: ${units[0].baseUnitName}`;
            const image = byId('riProductImage'), imageUrl = units[0].imageUrl || choice.imageUrl;
            image.hidden = !imageUrl; byId('riImageFallback').hidden = !!imageUrl;
            if (imageUrl) image.src = imageUrl; else image.removeAttribute('src');
            byId('riUnitCards').innerHTML = units.map(unit => `<button type="button" class="ri-unit-card" data-ri-unit="${unit.productUnitConversionId}" aria-pressed="false"><strong>${esc(unit.unitName)}</strong><small>1 ${esc(unit.unitName)} = ${num(unit.factor)} ${esc(unit.baseUnitName)}</small><small>${esc(unit.barcode || 'Chưa có mã')}</small></button>`).join('') +
                '<button type="button" class="ri-unit-card is-add" data-ri-unit="new" aria-pressed="false"><strong>＋ Thêm quy cách</strong><small>Ví dụ: thùng, kiện, khay…</small></button>';
            byId('riProductCard').hidden = false;
            if (units.length === 1) chooseUnit(String(units[0].productUnitConversionId));
        } catch (error) { if (revision === requestNumber) byId('riError').textContent = error.message; }
        finally { if (revision === requestNumber) { loading = false; update(); } }
    }
    function chooseUnit(value, focus = true) {
        if (saving) return;
        addingUnit = value === 'new'; selected = addingUnit ? null : units.find(x => String(x.productUnitConversionId) === value);
        byId('riUnitCards').querySelectorAll('[data-ri-unit]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.riUnit === value)));
        byId('riError').textContent = ''; update();
        if (focus && addingUnit) window.jQuery('#riReceiveUnit').select2('open');
        else if (focus) { byId('riQuantity').focus(); byId('riQuantity').select(); }
    }
    function open(code = '', options = {}) {
        if (!config?.canCapture || saving || dirtyQuantity() || window.ReceiptQuantityControls?.isBusy()) return false;
        if (pendingIntake) { modal.show(); return true; }
        if (typeof options === 'function') options = {};
        command = null; commandPayload = null; loading = false;
        window.ReceiptIntakePhoto?.reset();
        byId('riBarcode').value = code; byId('riNewName').value = options.name || '';
        byId('riQuantity').value = '1'; byId('riFactor').value = '';
        byId('riNote').value = ''; byId('riApproveNow').checked = false;
        ['riBaseUnit','riReceiveUnit','riCategory'].forEach(x => window.jQuery(byId(x)).val('').trigger('change.select2'));
        clearProduct(); $product.val(null).trigger('change'); switchTab(options.tab || 'existing');
        window.jQuery('#quickLookupInput').select2('close');
        const pending = (state?.items || []).find(x => x.status === 0 && x.proposedFactor && code &&
            String(x.rawBarcode).toUpperCase() === code.trim().toUpperCase());
        if (pending) switchTab(pending.proposedProductVariantId ? 'existing':'new');
        modal.show();
        if (pending) prefillItem(pending);
        return true;
    }
    async function afterSave(next) {
        applyState(next);
        window.WarehouseReceivingFeedback?.loadState(next.recentReceipts);
        await window.ReceiptBarcodeProposals?.reload();
        if (typeof window.refreshReceivingLines === 'function') await window.refreshReceivingLines();
        else if (typeof window.refreshStockDocumentDetailUI === 'function') await window.refreshStockDocumentDetailUI();
        await window.GaoAppPurchaseReceiptApproval?.refreshAfterIntake?.();
        await reload();
    }
    async function save() {
        if (saving || loading) return;
        if (window.ReceiptIntakePhoto?.blocked()) { byId('riError').textContent='Ảnh đang xử lý hoặc chưa hợp lệ. Chờ xử lý xong, chọn lại ảnh hoặc bấm Bỏ ảnh.'; return; }
        byId('riError').textContent = '';
        const pack = packing(), qty = number('riQuantity');
        if (tab === 'existing' && (!product || (!addingUnit && !selected))) { byId('riError').textContent = 'Hãy chọn sản phẩm và đơn vị nhận.'; return; }
        if (tab === 'new' && !values('riNewName')) { byId('riError').textContent = 'Vui lòng nhập tên sản phẩm mới.'; return; }
        if (!pack.unitName || !pack.baseUnitName || !(pack.factor > 0) || !(qty > 0)) { byId('riError').textContent = 'Nhập đủ đơn vị gốc, đơn vị nhận, tỷ lệ quy đổi và số lượng lớn hơn 0.'; return; }
        const known = tab === 'existing' && !addingUnit;
        const payload = known ? {productUnitConversionId:selected.productUnitConversionId, factor:pack.factor, quantity:qty, barcode:values('riBarcode'), note:values('riNote')} :
            {name:tab === 'new' ? values('riNewName') : units[0].productName, productVariantId:tab === 'existing' ? Number(product.productVariantId):null,
                ...pack, quantity:qty, barcode:values('riBarcode'), categoryId:Number(values('riCategory')) || null,
                note:values('riNote'), approveNow:byId('riApproveNow').checked};
        payload.photoDataUrl = window.ReceiptIntakePhoto?.current() || null;
        const identity = JSON.stringify({known, payload});
        if (identity !== commandPayload) {
            command = crypto.randomUUID(); commandPayload = identity;
            scanFeedback = window.WarehouseReceivingFeedback?.prepare({commandId:command,
                conversionId:known ? selected.productUnitConversionId : null,
                name:payload.name || units[0]?.productName || 'Hàng nhập', unitName:pack.unitName,
                barcode:payload.barcode || '', quantity:qty, factor:pack.factor, imageUrl:product?.imageUrl});
        }
        saving = true; setBusy(true); update();
        pendingIntake = true;
        window.receivingSaveEvent?.('saving');
        let committed = false;
        try {
            const next = await api(known ? '/known':'', {...payload, commandId:command, leaseToken:lease(), documentRowVersion:rowVersion()});
            applyState(next);
            byId('riPageMessage').textContent = '';
            await afterSave(next);
            committed = true; pendingIntake=false; uncertainIntake=false; allowClose = true; modal.hide(); allowClose = false;
            window.ReceiptIntakePhoto?.reset();
            window.receivingSaveEvent?.('saved',null,`${payload.name || units[0]?.productName || 'Hàng nhập'} · ${num(qty)} ${pack.unitName}`,
                scanFeedback ? {...scanFeedback, next} : null);
            window.jQuery('#quickLookupInput').val(null).trigger('change');
            window.focusQuickLookup?.();
        } catch (error) { uncertainIntake=!error.status || error.status>=500;byId('riError').textContent = uncertainIntake?'Chưa nhận đủ xác nhận lưu. Bấm Lưu và nhận hàng để kiểm tra, thử lại; số lượng không bị cộng hai lần.':error.message; byId('riPageMessage').textContent = byId('riError').textContent; }
        finally { saving = false; setBusy(false); update(); if(!committed)window.receivingSaveEvent?.('error'); }
    }
    function setBusy(value) {
        byId('receiptIntakeModal').querySelectorAll('button,input,textarea,select').forEach(el => el.disabled = value);
        if (!value && uncertainIntake) byId('receiptIntakeModal').querySelectorAll('input,textarea,select,[data-ri-unit],[data-intake-tab],#riPhotoRemove').forEach(el=>el.disabled=true);
        byId('receiptIntakeReviewModal').querySelectorAll('button,select').forEach(el => el.disabled = value);
        document.querySelectorAll('[data-intake-quantity],[data-intake-remove]').forEach(el=>el.disabled=value);
    }
    function dirtyQuantity() {
        return [...document.querySelectorAll('[data-intake-quantity]')].some(input=>{
            const item=state?.items.find(x=>x.id===Number(input.dataset.intakeQuantity));
            return item && Number(input.value)!==item.quantity;
        });
    }
    async function saveQuantity(input) {
        if (saving || !config?.canCapture) return false;
        const item=state?.items.find(x=>x.id===Number(input.dataset.intakeQuantity));
        if (!item) return false;
        // Resolve an unacknowledged command before accepting a different quantity.
        if (input.dataset.receiptQuantityUncertain==='1') input.value=input.dataset.commandQuantity;
        if (Number(input.value)===item.quantity && input.dataset.receiptQuantityUncertain!=='1') return true;
        const quantity=Number(input.value), message=input.closest('tr').querySelector('.ri-quantity-error');
        if (!(quantity>0)) { message.textContent='Số lượng phải lớn hơn 0.';return false; }
        const identity=String(quantity);
        if (input.dataset.commandQuantity!==identity) {input.dataset.commandQuantity=identity;input.dataset.command=crypto.randomUUID();}
        saving=true;setBusy(true);message.textContent='';
        let quantitySaved=false;
        window.receivingSaveEvent?.('saving');
        try {
            const next=await api(`/${item.id}/quantity`,{quantity,commandId:input.dataset.command,
                documentRowVersion:rowVersion(),itemRowVersion:item.rowVersion,leaseToken:lease()});
            await afterSave(next);
            quantitySaved=true;
            delete input.dataset.receiptQuantityUncertain;
            return true;
        } catch(error) {input.dataset.receiptQuantityUncertain=!error.status || error.status>=500?'1':'0';message.textContent=error.message;byId('riPageMessage').textContent=error.message;return false;}
        finally {saving=false;setBusy(false);update();window.receivingSaveEvent?.(quantitySaved?'saved':'error');}
    }
    function openReview(item, removeOnly = false) {
        if (saving) return;
        reviewItem = item; command = null; commandPayload = null;
        byId('riReviewTitle').textContent = removeOnly ? 'Xóa hàng khỏi phiếu' : 'Duyệt khai báo nhận hàng';
        byId('riApprove').hidden = removeOnly;
        byId('riReviewFacts').innerHTML = `<strong>${esc(item.name)}</strong><p class="text-muted mt-2">${esc(item.rawBarcode || 'Không có barcode')}</p><div class="ri-equivalent">1 ${esc(item.unitName)} = ${num(item.proposedFactor)} ${esc(item.proposedBaseUnitName)}<strong>${num(item.quantity)} ${esc(item.unitName)} = ${num(item.quantity * item.proposedFactor)} ${esc(item.proposedBaseUnitName)}</strong></div><p class="small mt-3 mb-0">${esc(item.note || 'Quy cách do nhân viên khai báo khi nhận hàng.')}</p>`;
        byId('riReviewCategoryWrap').hidden = removeOnly || !!item.proposedProductVariantId;
        if(item.hasPhoto) byId('riReviewFacts').insertAdjacentHTML('beforeend',`<img class="ri-evidence-photo" src="${endpoint}/${Number(item.id)}/photo" alt="Ảnh bao bì do nhân viên ghi nhận" />`);
        window.jQuery('#riReviewCategory').val(item.proposedCategoryId || '').trigger('change.select2');
        byId('riReviewError').textContent = ''; reviewModal.show();
    }
    async function review(approve) {
        if (!reviewItem || saving) return;
        const payload = {approve, categoryId:Number(values('riReviewCategory')) || null};
        const identity = JSON.stringify({id:reviewItem.id,...payload});
        if (identity !== commandPayload) { command = crypto.randomUUID(); commandPayload = identity; }
        saving = true; setBusy(true); byId('riReviewError').textContent = '';
        try {
            const action = byId('riApprove').hidden ? 'remove':'review';
            const next = await api(`/${reviewItem.id}/${action}`, {...payload, commandId:command, leaseToken:lease(),
                documentRowVersion:rowVersion(), itemRowVersion:reviewItem.rowVersion});
            allowClose = true; reviewModal.hide(); allowClose = false; await afterSave(next);
        } catch (error) { byId('riReviewError').textContent = error.message; byId('riPageMessage').textContent = error.message; }
        finally { saving = false; setBusy(false); update(); }
    }
    function start() {
        modal = bootstrap.Modal.getOrCreateInstance(byId('receiptIntakeModal'));
        reviewModal = bootstrap.Modal.getOrCreateInstance(byId('receiptIntakeReviewModal'));
        $product = window.jQuery('#riProduct');
        $product.select2({dropdownParent:window.jQuery('#receiptIntakeModal'), theme:'bootstrap-5', width:'100%', minimumInputLength:1,
            placeholder:'Tên sản phẩm, SKU hoặc mã đã có…', language:{inputTooShort:()=>'Nhập tên hoặc mã sản phẩm', searching:()=>'Đang tìm…', noResults:()=>'Không tìm thấy. Bạn có thể chuyển sang tab Sản phẩm mới.'},
            ajax:{url:`/admin/api/stock-documents/${id}/barcode-proposals/lookup`, dataType:'json', delay:180, data:params=>({term:params.term || '', catalogOnly:true}),
                processResults:result=>({results:[...new Map((result.results || []).filter(x=>x.productUnitConversionId).map(x=>[x.productVariantId,{...x,id:String(x.productVariantId)}])).values()]})},
            templateResult:item=> !item.id ? item.text : window.jQuery(`<div class="ri-search-result">${item.imageUrl ? `<img src="${esc(item.imageUrl)}" alt="" />`:''}<div><strong>${esc(item.productName)}</strong><small>${esc(item.sku)}</small></div></div>`),
            templateSelection:item=>item.productName || item.text || ''
        }).on('select2:select', event=>chooseProduct(event.params.data)).on('select2:open',()=>byId('receiptIntakeModal').querySelector('.select2-search__field')?.focus());
        rootTabs().forEach(button=>{
            button.addEventListener('click',()=>switchTab(button.dataset.intakeTab));
            button.addEventListener('keydown',event=>{if (['ArrowLeft','ArrowRight'].includes(event.key)) {event.preventDefault(); switchTab(tab === 'new'?'existing':'new'); (tab === 'new'?byId('riNewTab'):byId('riExistingTab')).focus();}});
        });
        byId('riUnitCards').addEventListener('click',event=>{const button=event.target.closest('[data-ri-unit]');if(button)chooseUnit(button.dataset.riUnit);});
        byId('riProductImage').addEventListener('error',()=>{byId('riProductImage').hidden=true;byId('riImageFallback').hidden=false;});
        byId('receiptIntakeModal').addEventListener('input',update);
        byId('receiptIntakeModal').addEventListener('change',update);
        byId('receiptIntakeModal').addEventListener('shown.bs.modal',()=>{if(tab === 'new')byId('riNewName').focus();else if(values('riBarcode'))$product.select2('open');else byId('riBarcode').focus();});
        byId('receiptIntakeModal').addEventListener('hide.bs.modal',event=>{if(saving&&!allowClose)event.preventDefault();else {requestNumber++;loading=false;window.jQuery('#receiptIntakeModal select.select2-hidden-accessible').select2('close');}});
        byId('receiptIntakeReviewModal').addEventListener('hide.bs.modal',event=>{if(saving&&!allowClose)event.preventDefault();});
        byId('riBarcode').addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();if(tab==='existing')$product.select2('open');else byId('riNewName').focus();}});
        byId('receiptIntakeModal').addEventListener('keydown',event=>{if(event.ctrlKey&&event.key==='Enter'){event.preventDefault();save();}});
        byId('riSave').addEventListener('click',save);
        byId('riApprove').addEventListener('click',()=>review(true)); byId('riReject').addEventListener('click',()=>review(false));
        byId('riPendingItems').addEventListener('click',event=>{
            const button=event.target.closest('[data-ri-review],[data-ri-repeat],[data-ri-remove]');if(!button)return;
            const item=state.items.find(x=>x.id===Number(button.dataset.riReview||button.dataset.riRepeat||button.dataset.riRemove));if(!item)return;
            if(button.dataset.riReview || button.dataset.riRemove)openReview(item,!!button.dataset.riRemove);else {
                open(item.rawBarcode || '',{tab:item.proposedProductVariantId?'existing':'new',name:item.name});
                if (!item.rawBarcode) prefillItem(item);
            }
        });
        document.addEventListener('receipt-barcode:state',event=>{barcodeState=event.detail;renderPending();});
        document.addEventListener('change',event=>{if(event.target.matches('[data-intake-quantity]') && event.target.dataset.receiptStepSaving!=='1')saveQuantity(event.target);});
        document.addEventListener('keydown',event=>{if(event.key==='Enter'&&event.target.matches('[data-intake-quantity]')){event.preventDefault();saveQuantity(event.target);}});
        document.addEventListener('click',event=>{
            const button=event.target.closest('[data-intake-remove]');if(!button || saving)return;
            const item=state?.items.find(x=>x.id===Number(button.dataset.intakeRemove));if(item)openReview(item,true);
        });
        document.addEventListener('keydown',event=>{if(event.key==='F4'&&!document.querySelector('.modal.show')){event.preventDefault();open('',{tab:'new'});}});
        document.addEventListener('click',event=>{if(!event.target.closest('.ri-open-new'))return;event.preventDefault();
            const raw=document.querySelector('.select2-container--open .select2-search__field')?.value || '';
            const looksLikeCode=/^[!-~]+$/.test(raw)&&/\d/.test(raw);
            open(looksLikeCode?raw:'',{tab:'new',name:looksLikeCode?'':raw});
        });
        if (window.ReceiptBarcodeProposals) window.ReceiptBarcodeProposals.open = open;
        reload();
    }
    window.ReceiptIntake = {open,reload,saveQuantity,retryPending:()=>{if(!pendingIntake)return false;modal.show();return true;},hasDraft:()=>saving || pendingIntake || (byId('receiptIntakeModal').classList.contains('show') && !!(values('riNewName') || values('riNote') || window.ReceiptIntakePhoto?.current())),isSaving:()=>saving || pendingIntake || dirtyQuantity() || !!window.ReceiptQuantityControls?.isBusy(),
        noResults:()=>'<div>Không tìm thấy sản phẩm.</div><button type="button" class="btn btn-sm btn-outline-primary mt-2 ri-open-new">Ghi nhận hàng chưa có</button>'};
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
