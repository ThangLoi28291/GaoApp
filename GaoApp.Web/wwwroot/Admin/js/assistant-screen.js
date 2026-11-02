(() => {
    'use strict';
    function start(){
        const root=document.getElementById('gaoAssistant'),by=id=>document.getElementById(id);if(!root||!window.GaoAssistant||!by('gaScreenView'))return;
        let session=null,host=null,context=null,result=null,busy=false,refreshing=false,generation=0,refreshId=0,source='typed',original='',snapshot=null,expiryTimer=null;
        function candidate(){
            const receiving=document.querySelector('[data-assistant-screen="receiving"]'),form=document.querySelector('#productSetupForm[data-assistant-screen="product_create"]');
            if(receiving){const id=Number(receiving.dataset.assistantDocumentId);if(!Number.isSafeInteger(id)||id<=0)return null;return {kind:'receiving',element:receiving,documentId:id,title:'Nhập hàng'};}
            const input=form?.querySelector('#txtName');return input?{kind:'product_create',element:form,input,documentId:null,title:'Tạo sản phẩm'}:null;
        }
        function hostKey(h=host){return h?JSON.stringify([h.kind,h.documentId,h.element.isConnected,h.kind==='receiving'?window.warehouseReceivingDetail?.documentId:null,
            h.kind==='receiving'?window.warehouseReceivingDetail?.rowVersion:h.input.value,h.kind==='receiving'?by('CanEditReceiving')?.value:h.input.disabled||h.input.readOnly]):null;}
        function formBusy(){return host?.kind==='receiving'&&(!!window.ReceiptIntake?.isSaving?.()||!!window.ReceiptIntake?.hasDraft?.()||!!window.receivingHasPendingChanges?.()
            ||['quickAddProductModal','receiptIntakeModal','wrdCaptureModal'].some(id=>by(id)?.classList.contains('show')));}
        const visible=()=>GaoAssistant.panel?.isOpen()&&!by('gaScreenView').hidden;
        function status(text,tone=''){by('gaTaskStatus').textContent=text;by('gaTaskStatus').dataset.tone=tone;}
        function clear(){clearTimeout(expiryTimer);expiryTimer=null;result=null;by('gaTaskPreview').replaceChildren();by('gaTaskPreview').hidden=true;by('gaTaskChoices').replaceChildren();by('gaTaskApply').hidden=true;}
        function controls(){
            const enabled=!!context?.canApply&&!refreshing,waiting=!!session?.job;
            by('gaTaskSend').disabled=busy||waiting||!enabled;by('gaTaskMic').disabled=busy||waiting||!enabled;
            by('gaTaskApply').disabled=busy||waiting||!enabled||!result||Date.parse(result.expiresAt)<=Date.now()||snapshot?.key!==hostKey()||formBusy();
            for(const b of by('gaTaskChoices').querySelectorAll('button'))b.disabled=busy||waiting||!enabled;
            by('gaTaskRecovery').hidden=!waiting;by('gaTaskRetry').hidden=!session?.job?.retry;
            by('gaTaskCheck').disabled=busy;by('gaTaskRetry').disabled=busy;by('gaTaskCancel').disabled=busy;
            window.dispatchEvent(new CustomEvent('gao-assistant:screen-state',{detail:{ready:!!context?.canApply,busy:busy||refreshing,pending:waiting}}));
        }
        function invalidate(message=''){
            generation++;busy=false;clear();snapshot=null;
            const old=session;if(old?.job)void old.cancel();else if(!refreshing)old?.invalidate();
            if(message)status(message);controls();
        }
        function compatible(h){return h&&host&&h.kind===host.kind&&h.documentId===host.documentId&&h.element===host.element&&h.input===host.input;}
        function validContext(who){
            const c=who.screen;
            if(!c||c.kind!==host.kind||typeof c.key!=='string'||!c.key||c.key.length>220||typeof c.title!=='string'||c.title.length>200||typeof c.canApply!=='boolean'
                ||c.documentId!==host.documentId||host.kind==='receiving'&&typeof c.documentVersion!=='string')throw new Error('Thông tin màn hình chưa hợp lệ. Mình tải lại trang nhé.');
            return c;
        }
        async function refresh(){
            const h=candidate(),ticket=++refreshId;by('gaScreenEntry').hidden=!h;
            if(!h){if(host){invalidate();host=null;session=null;context=null;}return false;}
            if(!compatible(h)){
                invalidate();host=h;context=null;session=new GaoAssistant.Session(root,h.documentId,h.kind);
                h.input?.addEventListener('input',()=>{if(host?.input===h.input)invalidate('Tên trên form đã đổi. Mình gửi lại đề xuất để kiểm tra.');});
                h.input?.addEventListener('change',()=>{if(host?.input===h.input&&snapshot?.key!==hostKey())invalidate('Tên trên form đã đổi.');});
                if(h.kind==='product_create')h.element.addEventListener('submit',()=>{if(host?.element===h.element)invalidate('Đang lưu sản phẩm; đề xuất cũ đã được bỏ.');});
            }
            const active=session;refreshing=true;controls();by('gaScreenLabel').textContent=h.title;by('gaScreenTitle').textContent=h.title;
            try{
                const who=await active.context();if(ticket!==refreshId||active!==session)return false;
                context=validContext(who);const cap=await active.read('Capabilities');if(ticket!==refreshId||active!==session)return false;
                const available=h.kind==='receiving'?cap.receiving?.available:cap.normalizeName?.available;
                if(!available)context={...context,canApply:false,message:'Chức năng hỗ trợ màn hình này đang tắt hoặc bạn chưa có quyền.'};
                if(h.kind==='receiving'&&context.documentVersion!==window.warehouseReceivingDetail?.rowVersion)
                    context={...context,canApply:false,message:'Phiếu đã thay đổi. Mình tải lại trang để dùng bản mới nhé.'};
                if(h.kind==='product_create'&&(h.input.disabled||h.input.readOnly))context={...context,canApply:false,message:'Ô tên trên form hiện không cho sửa.'};
                by('gaScreenLabel').textContent=context.title;by('gaScreenTitle').textContent=context.title;
                by('gaTaskLabel').textContent=h.kind==='receiving'?'Mặt hàng, số lượng và đơn vị nhận':'Tên sản phẩm cần chuẩn hóa';
                by('gaTaskSend').textContent=h.kind==='receiving'?'Đề xuất nhập hàng':'Đề xuất tên';
                by('gaTaskInput').placeholder=h.kind==='receiving'?'Ví dụ: sữa tươi TH có đường 180ml, 4 hộp':'Gõ hoặc nói tên sản phẩm cần kiểm tra…';
                by('gaScreenHint').textContent=h.kind==='receiving'?'Đề xuất một mặt hàng cho phiếu này. Kiểm tra rồi mở form nhận hàng.':'Xem tên đề xuất và hàng có thể trùng trước khi điền vào ô tên.';
                if(!context.canApply)status(context.message||'Màn hình này hiện không cho điền đề xuất.','error');else if(!result&&!session.job)status('Sẵn sàng tạo đề xuất cho màn hình này.');
                controls();return !!context.canApply;
            }catch(error){if(ticket===refreshId&&active===session){context=null;clear();status(error.message||'Chưa xác minh được màn hình. Mình vẫn có thể nhập thủ công.','error');controls();}return false;}
            finally{if(ticket===refreshId&&active===session){refreshing=false;controls();}}
        }
        function current(turn,active){return turn===generation&&active===session&&visible()&&compatible(candidate())&&snapshot?.key===hostKey();}
        function row(label,value){const tr=document.createElement('tr'),th=document.createElement('th'),td=document.createElement('td');th.scope='row';th.textContent=label;td.textContent=value;tr.append(th,td);return tr;}
        function validResult(r){
            if(!r||r.generation!==generation||!GaoAssistant.sameScope(r.scope,session.identity?.scope)||!Number.isFinite(Date.parse(r.expiresAt))||Date.parse(r.expiresAt)<=Date.now())throw new Error('Đề xuất đã cũ hoặc sai cửa hàng. Mình gửi lại nhé.');
            if(host.kind==='receiving'&&(typeof r.summary!=='string'||!['matched','needs_clarification','not_found'].includes(r.status)
                ||r.status==='matched'&&(!r.match?.product||!r.match.unit||!/^\d+(?:\.\d{1,4})?$/.test(r.match.quantity)||typeof r.match.equivalentBaseQuantity!=='string')))throw new Error('Đề xuất nhập hàng chưa hợp lệ.');
            if(host.kind==='product_create'&&(typeof r.proposedName!=='string'||!r.proposedName.trim()||!['suggested','needs_clarification'].includes(r.status)))throw new Error('Đề xuất tên chưa hợp lệ.');
        }
        function render(r){
            validResult(r);clear();result=r;
            const preview=by('gaTaskPreview'),table=document.createElement('table'),body=document.createElement('tbody');
            if(host.kind==='receiving'){
                status(r.summary);if(r.match){body.append(row('Mặt hàng',r.match.product.name),row('Đơn vị nhận',r.match.unit.name),row('Số lượng',r.match.quantity),row('Tương đương đơn vị gốc',r.match.equivalentBaseQuantity+' '+r.match.baseUnit.name));by('gaTaskApply').hidden=false;}
                else for(const c of r.candidates||[]){
                    const card=document.createElement('div');card.className='ga-task-candidate';const title=document.createElement('strong');title.textContent=c.product.name;card.append(title);
                    const choices=c.units?.filter(u=>u.canReceive)||[];if(!choices.length)continue;
                    const selection=unit=>({productId:c.product.productId,variantId:c.product.variantId,...(unit?{unitId:unit.unitId,...(unit.conversionId?{conversionId:unit.conversionId}:{})}:{})});
                    const choose=unit=>{if(result===r&&visible()&&!busy&&!session.job&&Date.parse(r.expiresAt)>Date.now())void propose(selection(unit));};
                    const button=document.createElement('button');button.type='button';button.textContent='Chọn mặt hàng';button.addEventListener('click',()=>choose(null));card.append(button);
                    if(r.resolvedProduct?.variantId===c.product.variantId)for(const unit of choices){const b=document.createElement('button');b.type='button';b.textContent='Chọn '+unit.name;b.addEventListener('click',()=>choose(unit));card.append(b);}by('gaTaskChoices').append(card);
                }
            }else{
                status('Tên đề xuất đã sẵn sàng để kiểm tra.');body.append(row('Tên đang có trên form',host.input.value||'(chưa có)'),row('Tên đề xuất',r.proposedName));
                const partial=r.status==='needs_clarification'&&r.clarifications?.length&&r.clarifications.every(c=>['capacity','brand','sugar'].includes(c.field));by('gaTaskApply').hidden=r.status!=='suggested'&&!partial;
                for(const duplicate of r.duplicates||[]){const note=document.createElement('p');note.className='ga-task-warning';note.textContent='Hàng có thể trùng: '+duplicate.product.name+'. Kiểm tra trước khi tạo sản phẩm.';preview.append(note);}
            }
            if(body.childNodes.length){table.append(body);preview.prepend(table);}
            for(const text of [...(r.clarifications||[]).map(c=>c.message),...(r.warnings||[])]){const p=document.createElement('p');p.className='ga-task-warning';p.textContent=text;preview.append(p);}
            preview.hidden=!preview.childNodes.length;
            expiryTimer=setTimeout(()=>{controls();if(result===r)status('Đề xuất đã hết hạn. Mình gửi lại để kiểm tra dữ liệu mới.','error');},Math.min(2147483647,Math.max(1,Date.parse(r.expiresAt)-Date.now()+1)));controls();
        }
        async function propose(selection=null){
            if(busy||session?.job||!visible())return;
            const text=by('gaTaskInput').value.trim();if(!text){status('Mình gõ hoặc nói nội dung trước nhé.');return;}
            if(formBusy()){status('Hãy hoàn tất form hoặc thay đổi đang chờ lưu trước.','error');return;}
            busy=true;controls();const before=generation;
            if(!await refresh()||before!==generation||!visible()){if(before===generation){busy=false;controls();}return;}
            generation++;clear();const turn=generation,active=session;snapshot={key:hostKey(),text,contextKey:context.key,identity:GaoAssistant.fingerprint(active.identity)};
            if(!selection)original=text;
            const operation=host.kind==='receiving'?'Receiving':'NormalizeName',body=operation==='Receiving'?{originalText:original,text,inputSource:source,generation,...(selection?{selection}:{})}:{originalText:text,inputSource:source,generation};
            status('Đang chuẩn bị đề xuất…');controls();
            try{const r=await active.begin(operation,body);if(!current(turn,active))return;if(r)render(r);else status('Đang chờ đề xuất. Bấm kiểm tra lượt này để lấy kết quả.');}
            catch(error){if(current(turn,active)){clear();status(error.message+' Mình có thể tiếp tục nhập thủ công.','error');}}
            finally{if(turn===generation&&active===session){busy=false;controls();}}
        }
        async function recover(retry){
            if(busy||!session?.job)return;busy=true;controls();const turn=generation,active=session;
            try{const r=await(retry?active.retry():active.check());if(!current(turn,active))return;if(r)render(r);else status(active.job?.retry?'Chưa có kết quả. Có thể gửi lại cùng lượt.':'Đề xuất vẫn đang xử lý.');}
            catch(error){if(current(turn,active)){clear();status(error.message,'error');}}finally{if(turn===generation){busy=false;controls();}}
        }
        async function apply(){
            if(busy||!result||session.job||!visible())return;
            const r=result,turn=generation,active=session;if(snapshot?.key!==hostKey()||formBusy()){invalidate('Form đã thay đổi. Mình gửi lại đề xuất nhé.');return;}
            busy=true;controls();
            try{
                await active.context();if(!current(turn,active)||result!==r||Date.parse(r.expiresAt)<=Date.now()||GaoAssistant.fingerprint(active.identity)!==snapshot.identity)throw new Error('Màn hình, phiếu hoặc đề xuất đã thay đổi. Mình gửi lại nhé.');
                context=validContext(active.identity);const right=host.kind==='receiving'?'assistant.receiving.resolve':'assistant.products.normalize_name';
                if(!context.canApply||!active.identity.permissions.includes(right)||!active.identity.permissions.includes('catalog.read')||formBusy())throw new Error('Hiện không được điền đề xuất vào form.');
                if(host.kind==='receiving'){
                    if(r.status!=='matched'||!r.match||typeof window.applyAssistantReceivingMatch!=='function')throw new Error('Đề xuất chưa đủ thông tin để mở form nhận hàng.');
                    await window.applyAssistantReceivingMatch(r.match,()=>current(turn,active)&&result===r,{fromScreen:true,documentId:host.documentId,documentVersion:window.warehouseReceivingDetail.rowVersion,beforeOpen:()=>GaoAssistant.panel.close()});
                }else{
                    const maximum=host.input.maxLength>0?host.input.maxLength:Number(host.input.dataset.valLengthMax)||200;
                    if(r.proposedName.length>maximum||host.input.disabled||host.input.readOnly)throw new Error('Tên đề xuất vượt giới hạn hoặc ô tên không cho sửa.');
                    host.input.value=r.proposedName;host.input.dispatchEvent(new Event('input',{bubbles:true}));host.input.dispatchEvent(new Event('change',{bubbles:true}));
                    status('Đã điền tên vào form. Bạn kiểm tra các trường khác rồi lưu sản phẩm.');GaoAssistant.panel.close();host.input.focus({preventScroll:true});
                }
            }catch(error){if(visible())status(error.message,'error');}finally{if(turn===generation){busy=false;controls();}}
        }
        by('gaTaskInput').addEventListener('input',()=>{invalidate();original='';if(source==='speech')source='mixed';status('Nội dung đã đổi. Gửi lại để tạo đề xuất mới.');});
        by('gaTaskInput').addEventListener('keydown',event=>{if(event.key==='Enter'&&!event.shiftKey&&!event.ctrlKey&&!event.altKey&&!event.isComposing&&event.keyCode!==229){event.preventDefault();if(!event.repeat)void propose();}});
        by('gaTaskSend').addEventListener('click',()=>propose());by('gaTaskApply').addEventListener('click',apply);
        by('gaTaskMic').addEventListener('click',()=>GaoAssistant.speech(by('gaTaskInput'),status,()=>{source='speech';status('Đã nhận giọng nói. Kiểm tra chữ rồi bấm tạo đề xuất.');}));
        by('gaTaskCheck').addEventListener('click',()=>recover(false));by('gaTaskRetry').addEventListener('click',()=>recover(true));by('gaTaskCancel').addEventListener('click',()=>invalidate('Đã hủy đề xuất.'));
        window.addEventListener('gao-assistant:opened',()=>{void refresh();});
        window.addEventListener('gao-assistant:identity-changed',()=>{refreshId++;invalidate();context=null;controls();if(GaoAssistant.panel?.isOpen())void refresh();});
        window.addEventListener('gao-assistant:screen-opened',()=>{void refresh().then(()=>{if(visible()){if(host?.kind==='product_create'&&!by('gaTaskInput').value)by('gaTaskInput').value=host.input.value;by('gaTaskInput').focus();}});});
        for(const event of ['gao-assistant:screen-closed','gao-assistant:closed','pagehide'])window.addEventListener(event,()=>{refreshId++;invalidate();GaoAssistant.stopSpeech();});
        document.addEventListener('receiving:save',()=>{if(host?.kind==='receiving'&&result)invalidate('Phiếu đang thay đổi. Mình kiểm tra và gửi lại đề xuất.');});
        by('gaScreenEntry').hidden=!candidate();controls();
    }
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
