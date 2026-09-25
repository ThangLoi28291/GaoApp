(() => {
    'use strict';
    const container=document.getElementById('wrdLinesContainer');
    if(!container)return;
    const drafts=new Map(), pause=650;
    let timer,runner=null,busy=false,failed=false,forced=false,activeKey='',actionRevision=0;
    const key=input=>input.closest('tr').dataset.receivingKey;
    const inputFor=id=>[...container.querySelectorAll('tr[data-receiving-key]')].find(row=>row.dataset.receivingKey===id)?.querySelector('.js-receiving-qty,[data-intake-quantity]');
    function publish(){
        document.dispatchEvent(new Event('receiving:quantity-draft'));
        window.updateReceivingSubmitState?.();
    }
    function canStep(input){
        if(!input || input.readOnly || failed || document.querySelector('.modal.show') || window.ReceiptQuantityControls?.isBusy())return false;
        if(drafts.size)return true;
        return !input.disabled && !window.receivingHasPendingChanges?.() && !window.ReceiptIntake?.isSaving();
    }
    const value=input=>drafts.get(key(input))?.quantity ?? Number(input.value);
    function schedule(){
        clearTimeout(timer);
        if(busy || failed || !drafts.size)return;
        timer=setTimeout(()=>void drain(),Math.max(0,Math.min(...[...drafts.values()].map(x=>x.due))-Date.now()));
    }
    function step(input,delta){
        if(![1,-1].includes(delta) || !canStep(input))return;
        const id=key(input),old=drafts.get(id),current=value(input);
        const next=Math.max(1,Math.round((current+delta)*1000)/1000);
        if(next===current)return;
        if(activeKey!==id && drafts.has(activeKey))drafts.get(activeKey).due=0;
        activeKey=id;
        const draft=old || {key:id,base:Number(input.value),sent:null,replay:null};
        draft.quantity=next;draft.due=Date.now()+pause;drafts.set(id,draft);
        if(draft.sent===null && next===draft.base)drafts.delete(id);
        publish();schedule();
    }
    async function run(){
        while(drafts.size && !failed){
            const draft=[...drafts.values()].find(x=>forced || x.due<=Date.now());
            if(!draft)break;
            const input=inputFor(draft.key);
            if(!input || input.readOnly){failed=true;window.receivingSaveEvent?.('error','Dòng hàng đã thay đổi. Kiểm tra lại phiếu trước khi tiếp tục.');break;}
            // Freeze the command being sent, while later taps keep updating a separate target.
            // On a lost response, replay that exact command before sending the newer target.
            if(draft.sent===null)draft.sent=draft.quantity;
            input.value=String(draft.sent);
            if(draft.replay)Object.assign(input.dataset,draft.replay);
            input.dataset.receiptStepSaving='1';
            let saved=false;
            try {saved=(input.dataset.intakeQuantity ? await window.ReceiptIntake.saveQuantity(input) : await window.saveReceivingQty(input))!==false;}
            catch {window.receivingSaveEvent?.('error','Chưa xác nhận lưu số lượng. Bấm Lưu lại để thử lại.');}
            finally {delete input.dataset.receiptStepSaving;}
            if(!saved){
                draft.replay={};
                for(const name of ['command','commandQuantity','receiptQuantityUncertain'])if(input.dataset[name]!==undefined)draft.replay[name]=input.dataset[name];
                failed=true;break;
            }
            draft.base=draft.sent;draft.sent=null;draft.replay=null;
            if(draft.quantity===draft.base)drafts.delete(draft.key);
            publish();
        }
    }
    function drain(){
        if(runner)return runner;
        clearTimeout(timer);busy=true;
        runner=run().finally(()=>{
            busy=false;runner=null;forced=false;
            if(!drafts.size && !failed)window.receivingSaveEvent?.('saved','Đã lưu số lượng');
            publish();schedule();
        });
        return runner;
    }
    async function flush(retry=false){
        if(failed && !retry)return false;
        if(!drafts.size)return true;
        if(retry)failed=false;
        forced=true;await drain();
        return !failed && !drafts.size;
    }
    function start(){
        document.addEventListener('pointerdown',event=>{
            if(!drafts.size || failed || event.target.closest('.modal'))return;
            const row=event.target.closest('tr[data-receiving-key]');
            if(row?.dataset.receivingKey!==activeKey){for(const draft of drafts.values())draft.due=0;schedule();}
        },true);
        // Finish pending quantities before starting a different server operation.
        document.addEventListener('click',async event=>{
            const button=event.target.closest('#wrdOpenCamera,#wrdOpenVoice,#btnSubmitReceiving,#wraNew');
            if(!button || !drafts.size)return;
            event.preventDefault();event.stopImmediatePropagation();
            const revision=++actionRevision;
            if(await flush() && revision===actionRevision && button.isConnected)button.click();
        },true);
        window.jQuery?.('#quickLookupInput').on('select2:opening',event=>{
            if(!drafts.size)return;
            event.preventDefault();const revision=++actionRevision;
            void flush().then(saved=>{if(saved && revision===actionRevision && !document.querySelector('.modal.show'))window.jQuery('#quickLookupInput').select2('open');});
        });
        matchMedia('(max-width: 768px)').addEventListener('change',()=>{if(drafts.size)void flush();});
        document.addEventListener('visibilitychange',()=>{if(document.hidden && drafts.size)void flush();});
    }
    window.WarehouseReceivingQuantity={step,canStep,value,hasPending:()=>drafts.size>0,isSaving:()=>busy,hasFailed:()=>failed,flush,retry:()=>flush(true)};
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
