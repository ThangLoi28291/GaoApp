/* UI-22: move the original sheet into a scroll viewport; restore the exact DOM position before print/desktop. */
(() => {
    'use strict';
    if(!document.querySelector('[data-ui22-print-page]'))return;
    const phone=matchMedia('screen and (max-width: 767.98px)'),mounted=[];
    const bodyValue=document.body.getAttribute('data-ui22-print');let active=false;
    const cleanup=()=>{
        for(const item of mounted.splice(0)){item.marker.replaceWith(item.paper);item.viewport.remove();item.note.remove();}
        if(bodyValue===null)document.body.removeAttribute('data-ui22-print');else document.body.setAttribute('data-ui22-print',bodyValue);
        active=false;
    };
    const apply=()=>{
        if(!phone.matches){cleanup();return;}
        if(active)return;
        document.body.setAttribute('data-ui22-print',document.querySelector('[data-ui22-print-page]').dataset.ui22PrintPage==='CustomerDebt/Receipt'?'layout':'true');
        for(const paper of document.querySelectorAll('#receiptPaper,.deposit-receipt,.delivery-sheet,main.paper,body > .receipt')){
            const marker=document.createComment('UI22 original paper position'),viewport=document.createElement('div'),note=document.createElement('p');
            viewport.className='ui22-paper-viewport';viewport.tabIndex=0;viewport.setAttribute('role','region');viewport.setAttribute('aria-label','Bản in đúng kích thước, có thể cuộn');
            note.className='ui22-paper-note';note.textContent='100% · Cuộn để xem hết bản in. Khổ giấy và cỡ chữ khi in giữ nguyên.';
            paper.before(marker,note,viewport);viewport.append(paper);mounted.push({paper,marker,viewport,note});
        }
        active=true;
    };
    phone.addEventListener('change',apply);addEventListener('beforeprint',cleanup);addEventListener('afterprint',apply);apply();
})();
