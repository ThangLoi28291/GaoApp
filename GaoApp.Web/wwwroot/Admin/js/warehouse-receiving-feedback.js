(() => {
    'use strict';
    const byId=id=>document.getElementById(id);
    if(!byId('wrdScanFeedback'))return;
    const num=value=>new Intl.NumberFormat('vi-VN',{maximumFractionDigits:3}).format(value);
    const time=value=>new Intl.DateTimeFormat('vi-VN',{day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit'}).format(value);
    const rows=()=>[...document.querySelectorAll('#wrdLinesContainer tbody [data-receiving-key]')];
    const rowFor=key=>rows().find(row=>row.dataset.receivingKey===key);
    const reducedMotion=()=>matchMedia('(prefers-reduced-motion: reduce)').matches;
    let recentReceipts=[],highlightTimer,imageRevision=0;
    const acknowledged=new Set();
    // The server journal owns identity, order and quantities, including after a reload.
    // The command only tells us which acknowledged row should receive an animation.
    const prepare=item=>({...item});
    function restoreOrder(){
        const list=rows(),ranks=new Map();
        recentReceipts.forEach((entry,index)=>{if(!entry.isRemoved && !ranks.has(entry.rowKey))ranks.set(entry.rowKey,index);});
        list.sort((a,b)=>(ranks.get(a.dataset.receivingKey)??Infinity)-(ranks.get(b.dataset.receivingKey)??Infinity));
        const parent=list[0]?.parentElement;
        list.forEach((row,index)=>{if(parent.children[index]!==row)parent.insertBefore(row,parent.children[index]||null);});
        renderLatest();
    }
    function setImage(url,name){
        const revision=++imageRevision,img=byId('wrdScanImage'),button=byId('wrdScanImageButton');
        if(url && img.getAttribute('src')===url && !img.hidden){img.alt=name;return;}
        img.hidden=true;img.removeAttribute('src');button.disabled=true;byId('wrdScanImageFallback').hidden=false;
        if(!url)return;
        const pending=new Image();
        pending.onload=()=>{if(revision!==imageRevision)return;img.alt=name;img.src=url;img.hidden=false;button.disabled=false;byId('wrdScanImageFallback').hidden=true;};
        pending.src=url;
    }
    function renderLatest(){
        const entry=recentReceipts[0],card=byId('wrdLastSaved');card.hidden=!entry;
        if(!entry)return;
        const row=rowFor(entry.rowKey);
        byId('wrdScanName').textContent=entry.name;
        byId('wrdScanCode').textContent=`${entry.unitName} · Mã: ${entry.barcode||'Chưa có'}${entry.factor>1?` · Quy đổi ×${num(entry.factor)}`:''}`;
        byId('wrdScanAdded').textContent=`Đã nhận +${num(entry.quantity)} ${entry.unitName}`;
        const total=row?Number(row.dataset.receivingQuantity):entry.currentQuantity;
        byId('wrdScanTotal').textContent=entry.isRemoved?'Đã loại khỏi phiếu':`Hiện trong phiếu: ${num(total)} ${entry.unitName}`;
        byId('wrdScanTime').textContent=time(entry.at);byId('wrdScanTime').dateTime=entry.at.toISOString();
        setImage(row?.querySelector('img')?.getAttribute('src') || (entry.photoItemId?
            `/admin/api/stock-documents/${window.warehouseReceivingDetail.documentId}/intake/${entry.photoItemId}/photo`:null),entry.name);
    }
    function loadState(entries){
        if(!Array.isArray(entries))return;
        recentReceipts=entries.map(entry=>({...entry,at:new Date(entry.occurredAtUtc)})).filter(entry=>entry.quantity>0&&!isNaN(entry.at)).slice(0,10);
        restoreOrder();
    }
    function confirm(item){
        if(!item?.commandId || acknowledged.has(item.commandId))return;
        const entry=recentReceipts.find(x=>x.commandId===item.commandId);
        if(!entry)return;
        acknowledged.add(item.commandId);restoreOrder();
        clearTimeout(highlightTimer);
        rows().forEach(row=>{row.classList.remove('wrd-just-received');row.querySelector('.wrd-scan-row-badge')?.remove();});
        const row=rowFor(entry.rowKey),card=byId('wrdLastSaved');card.classList.remove('wrd-confirm-flash');
        if(row){
            row.classList.add('wrd-just-received');
            const badge=document.createElement('span');badge.className='wrd-scan-row-badge';badge.textContent=`Vừa nhận +${num(entry.quantity)} ${entry.unitName}`;
            (row.querySelector('.wrd-line-name')?.parentElement||row.children[1]).append(badge);
        }
        void card.offsetWidth;card.classList.add('wrd-confirm-flash');
        highlightTimer=setTimeout(()=>{card.classList.remove('wrd-confirm-flash');row?.classList.remove('wrd-just-received');row?.querySelector('.wrd-scan-row-badge')?.remove();},1500);
        // Avoid moving row animations crossing the confirmation card.
        if(!reducedMotion())row?.querySelector('.wrd-table-qty,.wra-row-stepper')?.animate([{opacity:.65},{opacity:1}],{duration:220});
    }
    function start(){
        const photoModal=byId('wrdScanPhotoModal');document.body.append(photoModal);
        byId('wrdScanImageButton').addEventListener('click',()=>{
            window.jQuery('#quickLookupInput').select2('close');byId('wrdScanPhotoLarge').src=byId('wrdScanImage').src;
            byId('wrdScanPhotoLarge').alt=byId('wrdScanName').textContent;byId('wrdScanPhotoTitle').textContent=byId('wrdScanName').textContent;
            bootstrap.Modal.getOrCreateInstance(photoModal).show();
        });
        photoModal.addEventListener('hidden.bs.modal',()=>{byId('wrdScanPhotoLarge').removeAttribute('src');byId('wrdScanImageButton').focus({preventScroll:true});});
        document.addEventListener('receiving:save',event=>{
            byId('wrdScanFeedback').dataset.state=event.detail.state;
            if(event.detail.state==='saved'&&event.detail.feedback)confirm(event.detail.feedback);
        });
    }
    window.WarehouseReceivingFeedback={prepare,restoreOrder,loadState};
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
