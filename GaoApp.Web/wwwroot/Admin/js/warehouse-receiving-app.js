(() => {
    'use strict';
    const page = document.querySelector('.wrd-page');
    if (!page || !document.getElementById('wraMore')) return;
    const byId = id => document.getElementById(id), media = matchMedia('(max-width: 768px)');
    const inputSelector = '.js-receiving-qty,[data-intake-quantity]';
    const rows = () => [...byId('wrdLinesContainer').querySelectorAll('tr[data-receiving-key]')];
    const number = value => Number(String(value).trim().replace(',', '.'));
    const quantities=()=>window.WarehouseReceivingQuantity;
    const quantityValue=input=>quantities()?.value(input) ?? number(input.value);
    const format = value => Number(value).toLocaleString('vi-VN', {maximumFractionDigits:3});
    const text = (id, value) => { const node=byId(id); if(node && node.textContent!==String(value))node.textContent=value; };
    const tabs = ['items','info'], placements = [], scrollPositions = {items:0,info:0};
    let tab='items', quantityKey='', quantityBusy=false, quantityFailed=false, scheduled=false;
    let quantityModal, moreModal;
    const pending = () => !!(window.receivingHasPendingChanges?.() || window.ReceiptIntake?.isSaving?.() || window.ReceiptQuantityControls?.isBusy());
    function place(element, target) {
        if (!element || !target) return;
        const anchor=document.createComment('receiving desktop position'); element.before(anchor);
        placements.push({element,target,anchor});
    }
    function chooseTab(next, restore=true) {
        if (!tabs.includes(next)) return;
        scrollPositions[tab]=window.scrollY; tab=next; page.dataset.appTab=next;
        page.querySelectorAll('[data-wra-tab]').forEach(button=>{
            button.setAttribute('aria-selected',String(button.dataset.wraTab===next));
            button.tabIndex=button.dataset.wraTab===next?0:-1;
        });
        window.jQuery?.('#quickLookupInput').select2?.('close');
        if (restore && media.matches) window.scrollTo({top:scrollPositions[next],behavior:'instant'});
    }
    function syncMode() {
        document.body.classList.toggle('wrd-app-mode',media.matches);
        for (const {element,target,anchor} of placements) {
            if(media.matches)target.append(element);else anchor.after(element);
        }
        chooseTab(tab,false); refresh();
    }
    function enhanceRows() {
        for(const row of rows()) {
            const input=row.querySelector(inputSelector); if(!input || input.readOnly)continue;
            if(!media.matches && quantities()?.hasPending()){input.dataset.wraDraftDisabled='1';input.disabled=true;}
            else if(input.dataset.wraDraftDisabled==='1'){delete input.dataset.wraDraftDisabled;input.disabled=false;}
            let controls=row.querySelector('.wra-row-stepper');
            if(!controls){
                controls=document.createElement('div'); controls.className='wra-row-stepper wra-mobile';
                controls.innerHTML='<button type="button" data-wra-step="-1" aria-label="Giảm 1 đơn vị">−</button><button type="button" data-wra-edit aria-haspopup="dialog"></button><button type="button" data-wra-step="1" aria-label="Tăng 1 đơn vị">+</button>';
                input.after(controls);
            }
            const name=row.querySelector('.wrd-line-name,strong')?.textContent.trim() || 'sản phẩm';
            const edit=controls.querySelector('[data-wra-edit]'), value=format(quantityValue(input));
            if(edit.textContent!==value)edit.textContent=value;
            edit.setAttribute('aria-label',`Sửa số lượng ${name}: ${value}`);
            const busy=quantityBusy || (pending() && !quantities()?.hasPending()) || quantities()?.hasFailed();
            edit.disabled=busy;
            controls.querySelectorAll('[data-wra-step]').forEach(button=>button.disabled=quantityBusy || !quantities()?.canStep(input) || (button.dataset.wraStep==='-1' && quantityValue(input)<=1));
            row.classList.toggle('wra-unsaved',quantityValue(input)!==number(input.defaultValue));
        }
    }
    function refresh() {
        enhanceRows();
        const entries=rows(), total=entries.reduce((sum,row)=>{const input=row.querySelector(inputSelector);return sum+(input?quantityValue(input):number(row.dataset.receivingQuantity||0));},0);
        text('wraCount',entries.length); text('wraSummary',`${format(total)} SL`);
        text('wrdTotalLines',entries.length);text('wrdTotalQty',format(total));
        const source=byId('wrdSaveStatus');
        if(source){text('wraStatus',source.textContent);byId('wraStatus').dataset.state=source.dataset.state||'saved';}
        if(quantities()?.hasPending()){
            text('wraStatus',quantities().hasFailed()?'Chưa xác nhận lưu số lượng. Bấm Lưu lại.':quantities().isSaving()?'Đang lưu · có thể tiếp tục chỉnh số lượng':'Số lượng đã đổi · sẽ tự lưu');
            byId('wraStatus').dataset.state=quantities().hasFailed()?'error':quantities().isSaving()?'saving':'pending';
        }
        const dirty=entries.some(row=>{const input=row.querySelector(inputSelector);return input&&!input.readOnly&&number(input.value)!==number(input.defaultValue);});
        byId('wraRetry').hidden=!((dirty || window.receivingHasPendingChanges?.()) && !quantityBusy && !window.ReceiptQuantityControls?.isBusy() && source?.dataset.state!=='saving');
        if(quantities()?.hasPending())byId('wraRetry').hidden=!quantities().hasFailed();
        text('wraPending',byId('riPendingCount')?.textContent || '0');
        byId('wraPending').hidden=!(Number(byId('wraPending').textContent)>0);
    }
    function scheduleRefresh(){if(scheduled)return;scheduled=true;requestAnimationFrame(()=>{scheduled=false;refresh();});}
    function captureListViewport(){
        if(tab!=='items')return null;
        // Keep the visible row anchored on every layout. On desktop the table is
        // replaced after saving a quantity as well, and replacing the focused
        // input can otherwise make the browser jump back to the page top.
        const top=page.querySelector('.wra-header')?.getBoundingClientRect().bottom ?? 0;
        const anchor=rows().find(row=>row.getBoundingClientRect().bottom>top);
        return {key:anchor?.dataset.receivingKey,top:anchor?.getBoundingClientRect().top,left:scrollX,scroll:scrollY};
    }
    function restoreListViewport(viewport){
        // The server partial has no mobile steppers. Build them before measuring or painting
        // so the receipt never collapses for a frame when its rows are replaced.
        enhanceRows();
        if(!viewport || tab!=='items')return;
        const anchor=rows().find(row=>row.dataset.receivingKey===viewport.key);
        const top=anchor && Number.isFinite(viewport.top) ? scrollY+anchor.getBoundingClientRect().top-viewport.top : viewport.scroll;
        window.scrollTo({left:viewport.left,top,behavior:'instant'});
    }
    function currentInput(){return rows().find(row=>row.dataset.receivingKey===quantityKey)?.querySelector(inputSelector);}
    function updateQuantityLock(){
        const locked=quantityBusy || currentInput()?.dataset.receiptQuantityUncertain==='1';
        byId('wraQuantity').disabled=locked;
        document.querySelectorAll('[data-wra-quantity-step]').forEach(button=>button.disabled=locked);
    }
    async function openQuantity(row, retry=false){
        const rowKey=row?.dataset.receivingKey;
        if(quantities()?.hasPending()){
            if(!await quantities().flush())return;
            row=rows().find(entry=>entry.dataset.receivingKey===rowKey);
        }
        const input=row?.querySelector(inputSelector);
        if(!input || input.readOnly || quantityBusy || (!retry && pending()))return;
        quantityKey=row.dataset.receivingKey;quantityFailed=retry;
        text('wraQuantityName',row.querySelector('.wrd-line-name,strong')?.textContent.trim()||'Sản phẩm');
        text('wraQuantityMeta',row.querySelector('.wrd-line-meta,small')?.textContent.trim()||'');
        text('wraQuantityUnit',`(${row.children[2].textContent.trim()})`);
        byId('wraQuantity').value=input.value;text('wraQuantityError','');
        updateQuantityLock();
        if(input.dataset.receiptQuantityUncertain==='1')text('wraQuantityError','Giữ nguyên số lượng đang chờ xác nhận. Bấm Thử lưu lại để kiểm tra kết quả.');
        text('wraSaveQuantity',retry?'Thử lưu lại':'Lưu số lượng');
        byId('wraRemove').hidden=retry;
        window.jQuery?.('#quickLookupInput').select2?.('close');quantityModal.show();
    }
    async function saveQuantity(){
        if(quantityBusy)return;
        const input=currentInput(), value=number(byId('wraQuantity').value);
        if(!input){text('wraQuantityError','Dòng hàng đã thay đổi. Đóng và chọn lại sản phẩm.');return;}
        if(!Number.isFinite(value)||value<=0||Math.abs(value*1000-Math.round(value*1000))>0.000001){text('wraQuantityError','Nhập số lượng lớn hơn 0, tối đa 3 chữ số thập phân.');return;}
        if(value===number(input.defaultValue) && !quantityFailed){quantityModal.hide();return;}
        quantityBusy=true; input.dataset.receiptStepSaving='1';input.value=String(value);
        byId('wraQuantity').disabled=true;byId('wraSaveQuantity').disabled=true;byId('wraRemove').disabled=true;
        text('wraSaveQuantity','Đang lưu…');text('wraQuantityError','');refresh();
        let saved=false;
        try {saved=(input.dataset.intakeQuantity ? await window.ReceiptIntake.saveQuantity(input) : await window.saveReceivingQty(input))!==false;}
        catch {saved=false;}
        finally {
            delete input.dataset.receiptStepSaving;quantityBusy=false;quantityFailed=!saved;
            updateQuantityLock();byId('wraSaveQuantity').disabled=false;byId('wraRemove').disabled=false;
            text('wraSaveQuantity',saved?'Lưu số lượng':'Thử lưu lại');
        }
        if(saved)quantityModal.hide();
        else {text('wraQuantityError','Chưa xác nhận lưu. Số lượng vẫn được giữ để bạn thử lại.');byId('wraRemove').hidden=true;}
        refresh();
    }
    function afterClose(root, modal, action){root.addEventListener('hidden.bs.modal',action,{once:true});modal.hide();}
    function start(){
        // Dialogs must remain reachable when switching tabs or changing screen size.
        page.querySelectorAll('.modal').forEach(modal=>document.body.append(modal));
        quantityModal=bootstrap.Modal.getOrCreateInstance(byId('wraQuantityModal'));
        moreModal=bootstrap.Modal.getOrCreateInstance(byId('wraMoreModal'));
        const side=page.querySelector('.wrd-side'), info=document.createElement('div');info.className='wra-info-extra';side.append(info);
        page.querySelector('.wrd-main').id='wraItems';side.id='wraInfo';
        for(const [name,id] of Object.entries({items:'wraItems',info:'wraInfo'})){
            const button=page.querySelector(`[data-wra-tab="${name}"]`);button.id=`${id}Tab`;button.setAttribute('aria-controls',id);
            byId(id).setAttribute('role','tabpanel');byId(id).setAttribute('aria-labelledby',button.id);
        }
        place(byId('receiptIntake'),info);
        [...page.children].filter(node=>node.classList.contains('alert')).forEach(node=>place(node,info));
        const dock=byId('wraDockActions');
        place(byId('wrdOpenVoice'),dock);place(byId('wrdOpenCamera'),dock);place(byId('btnSubmitReceiving'),dock);
        page.querySelectorAll('[data-wra-tab]').forEach(button=>{
            button.addEventListener('click',()=>chooseTab(button.dataset.wraTab));
            button.addEventListener('keydown',event=>{if(!['ArrowLeft','ArrowRight'].includes(event.key))return;event.preventDefault();const next=tabs[(tabs.indexOf(tab)+(event.key==='ArrowRight'?1:tabs.length-1))%tabs.length];chooseTab(next);page.querySelector(`[data-wra-tab="${next}"]`).focus();});
        });
        byId('wraMore').addEventListener('click',()=>moreModal.show());
        byId('wraShowInfo').addEventListener('click',()=>afterClose(byId('wraMoreModal'),moreModal,()=>chooseTab('info')));
        byId('wraNew')?.addEventListener('click',()=>afterClose(byId('wraMoreModal'),moreModal,()=>{
            if(pending()){text('wraStatus','Hãy lưu xong thay đổi hiện tại trước.');return;}
            chooseTab('items');window.ReceiptIntake?.open('',{tab:'new'});
        }));
        byId('wraRetry').addEventListener('click',()=>{
            if(quantities()?.hasPending()){void quantities().retry();return;}
            const dirty=rows().find(row=>{const input=row.querySelector(inputSelector);return input&&!input.readOnly&&number(input.value)!==number(input.defaultValue);});
            if(dirty)openQuantity(dirty,true);else window.receivingRetryChanges?.();
        });
        byId('wrdLinesContainer').addEventListener('click',event=>{
            const button=event.target.closest('[data-wra-edit],[data-wra-step]');if(!button)return;
            const row=button.closest('tr');
            if(button.hasAttribute('data-wra-edit'))openQuantity(row);
            else {quantities()?.step(row.querySelector(inputSelector),Number(button.dataset.wraStep));scheduleRefresh();}
        });
        byId('wraQuantityModal').addEventListener('hide.bs.modal',event=>{if(quantityBusy)event.preventDefault();});
        byId('wraQuantityModal').addEventListener('hidden.bs.modal',()=>{const row=rows().find(row=>row.dataset.receivingKey===quantityKey);row?.querySelector('[data-wra-edit]')?.focus({preventScroll:true});});
        byId('wraQuantity').addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();void saveQuantity();}});
        document.querySelectorAll('[data-wra-quantity-step]').forEach(button=>button.addEventListener('click',()=>{if(!byId('wraQuantity').disabled)byId('wraQuantity').value=String(Math.max(1,Math.round((number(byId('wraQuantity').value)||0)*1000+Number(button.dataset.wraQuantityStep)*1000)/1000));}));
        byId('wraSaveQuantity').addEventListener('click',()=>void saveQuantity());
        byId('wraRemove').addEventListener('click',()=>{
            if(quantityBusy || quantityFailed)return;
            const remove=currentInput()?.closest('tr').querySelector('.btn-delete-receiving-line,[data-intake-remove]');
            afterClose(byId('wraQuantityModal'),quantityModal,()=>remove?.click());
        });
        new MutationObserver(scheduleRefresh).observe(byId('wrdLinesContainer'),{childList:true,subtree:true});
        for(const id of ['wrdSaveStatus','riPendingCount'])if(byId(id))new MutationObserver(scheduleRefresh).observe(byId(id),{childList:true,subtree:true,attributes:true});
        document.addEventListener('receiving:save',event=>{
            scheduleRefresh();
            if(media.matches && event.detail?.state==='saved' && event.detail.feedback){
                // A successful scan may switch back to the items tab, but it
                // must not reset the employee's current position in a long list.
                chooseTab('items',false);
            }
        });
        document.addEventListener('receipt-quantity:idle',scheduleRefresh);
        document.addEventListener('receiving:quantity-draft',refresh);
        // canStep depends on modal visibility as well as pending work. Recompute
        // at lifecycle completion so the next action sees the current lock state.
        document.addEventListener('shown.bs.modal',refresh);
        document.addEventListener('hidden.bs.modal',refresh);
        // Queue state clears after the acknowledgement event's listeners finish.
        window.addEventListener('online',scheduleRefresh);window.addEventListener('offline',scheduleRefresh);
        window.addEventListener('beforeunload',event=>{
            if(quantityBusy || (byId('wraQuantityModal').classList.contains('show') && number(byId('wraQuantity').value)!==number(currentInput()?.value))){event.preventDefault();event.returnValue='';}
        });
        media.addEventListener('change',syncMode);syncMode();
        window.WarehouseReceivingApp={selectTab:chooseTab,captureListViewport,restoreListViewport};
    }
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
