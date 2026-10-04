(() => {
    'use strict';
    const el = id => document.getElementById(id), val = id => el(id).value.trim();
    const n = id => val(id) === '' ? null : Number(val(id));
    const unit = id => ({id:n(id) > 0 ? n(id):null, name:el(id).selectedOptions[0]?.text || ''});
    const format = x => new Intl.NumberFormat('vi-VN', {maximumFractionDigits:3}).format(x);
    let hasPhoto=false;
    let initialized = false, variantId = null, baseline = '', removing = false, hasSupplier = false;
    function select(id, value, text) {
        if (value && ![...el(id).options].some(x=>x.value===String(value))) el(id).add(new Option(text || String(value),String(value)));
        jQuery(el(id)).val(value || '').trigger('change.select2');
    }
    function payload() {
        const base=unit('riReviewBaseUnit'), receive=unit('riReviewReceiveUnit');
        return {name:val('riReviewName'), barcode:val('riReviewBarcode') || null,
            productVariantId:val('riReviewMode')==='existing' ? variantId:null,
            unitId:receive.id, unitName:receive.name, baseUnitId:base.id, baseUnitName:base.name,
            factor:n('riReviewFactor'), quantity:n('riReviewQuantity'), categoryId:n('riReviewCategory'), brandId:n('riReviewBrand'),
            purchasePrice:n('riReviewCost'), retailPrice:n('riReviewRetail'), wholesalePrice:n('riReviewWholesale'),
            isSellable:el('riReviewSellable').checked, makeBarcodePrimary:el('riReviewPrimary').checked,
            generateBaseBarcode:el('riReviewGenerateBase').checked, usePackagingPhoto:el('riReviewUsePhoto').checked, note:val('riReviewNote') || null};
    }
    const identity = () => JSON.stringify({completion:payload(),supplier:val('riReviewSupplier'),photo:window.ReceiptIntakeReviewPhoto?.identity()});
    function update() {
        const existing=val('riReviewMode')==='existing';
        el('riReviewExistingWrap').hidden=!existing;
        el('riReviewCategoryWrap').hidden=existing;
        el('riReviewBrandWrap').hidden=existing;
        el('riReviewSupplierWrap').hidden=!hasSupplier || existing;
        el('riReviewName').readOnly=existing;
        el('riReviewPrimary').disabled=!existing;
        if(!existing)el('riReviewPrimary').checked=true;
        el('riReviewBaseUnit').disabled=existing;
        document.querySelectorAll('.ri-review-new-only').forEach(x=>x.hidden=existing);
        el('riReviewPhotoWrap').hidden=existing || !hasPhoto;
        el('riReviewUploadWrap').hidden=existing || removing;
        el('riReviewUsePhoto').disabled=!!window.ReceiptIntakeReviewPhoto?.present();
        if(el('riReviewUsePhoto').disabled)el('riReviewUsePhoto').checked=false;
        const base=unit('riReviewBaseUnit'), receive=unit('riReviewReceiveUnit'), factor=n('riReviewFactor'), quantity=n('riReviewQuantity');
        el('riReviewEquivalent').textContent = factor>0 && quantity>0 ? `${format(quantity)} ${receive.name} × ${format(factor)} = ${format(quantity*factor)} ${base.name}` : 'Chọn đơn vị, quy đổi và số lượng thực nhận.';
        el('riReviewPriceUnit').textContent=receive.name || 'đơn vị nhận';
        const purchasePrice=n('riReviewCost');
        el('riReviewCostHint').textContent=existing
            ? 'Sản phẩm có sẵn: giá nhập lưu vào phiếu, giữ giá vốn hiện tại của biến thể.'
            : purchasePrice>0 && factor>0
                ? `Giá vốn lưu khi duyệt: ${format(Math.round((purchasePrice/factor+Number.EPSILON)*100)/100)} đ/${base.name || 'đơn vị gốc'} (giá nhập ÷ ${format(factor)}).`
                : 'Chưa nhập giá: có thể bổ sung sau; chưa có giá vốn để lưu vào biến thể.';
        el('riReviewBarcodeHint').textContent=val('riReviewBarcode')
            ? `Giữ nguyên mã đã nhập, gắn cho ${receive.name || 'đơn vị nhận'}. Không tự sinh thêm mã thay thế.`
            : 'Chưa có mã: khi duyệt sẽ tạo mã nội bộ cho đơn vị mới chưa có barcode.';
        el('riReviewGenerateBaseWrap').hidden=existing || base.name===receive.name || !val('riReviewBarcode');
        if(el('riReviewGenerateBaseWrap').hidden) el('riReviewGenerateBase').checked=false;
        if(baseline && identity()!==baseline) el('riReviewSaveStatus').textContent='Có thay đổi chưa lưu';
    }
    function init(config, endpoint) {
        if(initialized)return; initialized=true;
        window.ReceiptIntakeReviewPhoto?.init();
        for(const id of ['riReviewBaseUnit','riReviewReceiveUnit','riReviewBrand']) {
            const list=id==='riReviewBrand'?config.brands:config.options?.units;
            el(id).replaceChildren(new Option(id==='riReviewBrand'?'Chưa chọn thương hiệu':'Chọn hoặc gõ tên đơn vị',''));
            (list || []).forEach(x=>el(id).add(new Option(x.text,String(x.id))));
            jQuery(el(id)).select2({theme:'bootstrap-5',width:'100%',dropdownParent:jQuery('#receiptIntakeReviewModal'),
                tags:id!=='riReviewBrand',placeholder:'Gõ để tìm…',allowClear:true,
                matcher:(p,d)=>!p.term || String(d.text).normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/đ/gi,'d').toLowerCase().includes(p.term.normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/đ/gi,'d').toLowerCase()) ? d:null,
                createTag:p=>p.term.trim()?{id:'new:'+p.term.trim(),text:p.term.trim()}:null});
        }
        jQuery('#riReviewProduct').select2({theme:'bootstrap-5',width:'100%',dropdownParent:jQuery('#receiptIntakeReviewModal'),minimumInputLength:1,
            placeholder:'Tìm sản phẩm và đơn vị…',ajax:{url:endpoint+'/review-products',dataType:'json',delay:250,data:p=>({term:p.term}),processResults:d=>d},
            language:{inputTooShort:()=>'Gõ tên, SKU hoặc barcode',noResults:()=>'Không tìm thấy sản phẩm',searching:()=>'Đang tìm…'}})
            .on('select2:select',e=>{const d=e.params.data;variantId=d.productVariantId;el('riReviewName').value=d.name;
                select('riReviewBaseUnit',d.baseUnitId,d.baseUnitName);select('riReviewReceiveUnit',d.unitId,d.unitName);el('riReviewFactor').value=d.factor;update();});
        jQuery('#receiptIntakeReviewModal select').on('change',update);
        el('receiptIntakeReviewModal').addEventListener('input',update);
        window.addEventListener('beforeunload',e=>{if(dirty()){e.preventDefault();e.returnValue='';}});
    }
    function open(item, removeOnly) {
        window.ReceiptIntakeReviewPhoto?.open(item, `/admin/api/stock-documents/${document.getElementById('receiptIntake').dataset.documentId}/intake`);
        hasPhoto=!!item.hasPhoto;
        removing=removeOnly;hasSupplier=!!window.GaoAppPurchaseReceiptApproval?.supplierForIntake?.();
        const d=item.reviewDraft || {name:item.name,barcode:item.rawBarcode,productVariantId:item.proposedProductVariantId,
            unitId:item.unitId,unitName:item.unitName,baseUnitId:item.proposedBaseUnitId,baseUnitName:item.proposedBaseUnitName,
            factor:item.proposedFactor,quantity:item.quantity,categoryId:item.proposedCategoryId,note:item.note,makeBarcodePrimary:true};
        variantId=d.productVariantId || null;
        el('riReviewMode').value=variantId?'existing':'new';
        el('riReviewEditor').hidden=removeOnly;
        for(const id of ['riReviewSaveDraft','riApproveNext']) el(id).hidden=removeOnly;
        el('riReviewDiscard').hidden=true;
        for(const [id,value] of Object.entries({riReviewName:d.name,riReviewBarcode:d.barcode,riReviewFactor:d.factor,
            riReviewQuantity:d.quantity,riReviewCost:d.purchasePrice,riReviewRetail:d.retailPrice,riReviewWholesale:d.wholesalePrice,riReviewNote:d.note})) el(id).value=value??'';
        select('riReviewBaseUnit',d.baseUnitId || (d.baseUnitName?'new:'+d.baseUnitName:''),d.baseUnitName);
        select('riReviewReceiveUnit',d.unitId || (d.unitName?'new:'+d.unitName:''),d.unitName);
        select('riReviewCategory',d.categoryId);select('riReviewBrand',d.brandId);
        el('riReviewProduct').replaceChildren(new Option('Tìm sản phẩm…',''));
        if(variantId)select('riReviewProduct','current',d.name);
        el('riReviewPrimary').checked=d.makeBarcodePrimary!==false;el('riReviewGenerateBase').checked=!!d.generateBaseBarcode;el('riReviewSellable').checked=!!d.isSellable;
        el('riReviewUsePhoto').checked=!!d.usePackagingPhoto;
        update();markSaved(item.reviewDraft?'Đã tải bản nháp':'Chưa thay đổi');
    }
    function markSaved(message='Đã lưu nháp vào phiếu') {baseline=identity();el('riReviewSaveStatus').textContent=message;el('riReviewDiscard').hidden=true;}
    function dirty(){return !removing && el('receiptIntakeReviewModal')?.classList.contains('show') && (baseline!==identity() || !!window.ReceiptIntakeReviewPhoto?.blocked());}
    function validate(){
        const d=payload();
        if(val('riReviewMode')==='existing' && !d.productVariantId)throw new Error('Chọn sản phẩm có sẵn để liên kết.');
        if(!d.name || !d.unitName || !d.baseUnitName || !(d.factor>0) || !(d.quantity>0))throw new Error('Nhập đủ tên, đơn vị, quy đổi và số lượng lớn hơn 0.');
        return d;
    }
    window.ReceiptIntakeReview={init,open,payload:validate,markSaved,dirty,update};
})();
