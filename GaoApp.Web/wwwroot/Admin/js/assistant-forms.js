(() => {
    'use strict';
    function start(){
        const root=document.getElementById('gaoAssistant');if(!root||!window.GaoAssistant)return;
        const documentId=window.warehouseReceivingDetail?.documentId||null;
        function form(mode,input,anchor,closeRoot){
            const receiving=mode==='Receiving';
            const session=new GaoAssistant.Session(root,receiving?documentId:null);let enabled=false,generation=0,original='',source='typed',result=null,busy=false,finish=null,imageRequest=null;
            const panel=document.createElement('div');panel.className='ga-form';panel.hidden=true;
            const send=document.createElement('button');send.type='button';send.textContent=receiving?'Tìm lại bằng AI':'Đề xuất tên';send.hidden=receiving;
            const mic=document.createElement('button');mic.type='button';mic.textContent='🎙 Nói';
            const output=document.createElement('div');output.className='ga-form-output';output.setAttribute('role','status');
            const choices=document.createElement('div');choices.className='ga-form-choices';
            const apply=document.createElement('button');apply.type='button';apply.textContent=mode==='Receiving'?'Điền để kiểm tra':'Dùng tên đề xuất';apply.hidden=true;
            const recovery=document.createElement('div');recovery.hidden=true;
            const manual=document.createElement('button');manual.type='button';manual.textContent='Tìm theo cách thường';manual.hidden=!receiving;
            const check=document.createElement('button'),retry=document.createElement('button'),cancel=document.createElement('button');
            for(const b of [check,retry,cancel])b.type='button';check.textContent='Kiểm tra lượt này';retry.textContent='Gửi lại lượt này';cancel.textContent='Hủy';recovery.append(check,retry,cancel);
            if(receiving)mic.hidden=true;
            panel.append(send,mic,output,choices,apply,recovery,manual);anchor.after(panel);
            function controls(){send.disabled=busy||!!session.job;mic.disabled=busy;recovery.hidden=!session.job;retry.hidden=!session.job?.retry;}
            function clear(){imageRequest?.abort();imageRequest=null;result=null;apply.hidden=true;choices.replaceChildren();}
            async function loadReceivingImages(cards,snapshot){
                const controller=imageRequest=new AbortController(),turn=generation;
                const current=()=>!controller.signal.aborted&&turn===generation&&snapshot===result;
                // Read current catalog photos from GaoApp, never from model-generated URLs.
                // Limit parallel reads and cancel them when the suggestion is replaced/closed.
                async function worker(){while(cards.length&&current()){
                    const {product,photo}=cards.shift();
                    try{
                        const response=await fetch(`/admin/api/stock-documents/${documentId}/barcode-proposals/products/${encodeURIComponent(product.variantId)}/units`,{cache:'no-store',signal:controller.signal});
                        if(!response.ok)throw new Error('photo unavailable');
                        const data=await response.json();if(!current())return;
                        const image=data.items?.find(item=>String(item.productVariantId)===product.variantId&&item.imageUrl)?.imageUrl;
                        const url=image?new URL(image,window.location.href):null;
                        if(!url||!['http:','https:'].includes(url.protocol))throw new Error('photo unavailable');
                        const img=document.createElement('img');img.alt='';img.loading='lazy';img.decoding='async';
                        img.addEventListener('error',()=>{if(current())photo.textContent='Chưa có ảnh';},{once:true});
                        img.src=url.href;photo.replaceChildren(img);
                    }catch{if(current())photo.textContent='Chưa có ảnh';}
                }}
                await Promise.all(Array.from({length:Math.min(3,cards.length)},worker));
                if(imageRequest===controller)imageRequest=null;
            }
            function invalid(){generation++;busy=false;clear();if(session.job)void session.cancel();else session.invalidate();controls();}
            input.addEventListener('input',()=>{invalid();original='';output.textContent='';if(source==='speech')source='mixed';});
            input.addEventListener('assistant:speech-input',invalid);
            closeRoot?.addEventListener('hide.bs.modal',()=>{invalid();original='';source='typed';output.textContent='';GaoAssistant.stopSpeech();});
            async function available(){try{const who=await session.context();const right=mode==='Receiving'?'assistant.receiving.resolve':'assistant.products.normalize_name';enabled=who.permissions.includes(right);panel.hidden=!enabled;if(enabled){const cap=await session.read('Capabilities');enabled=mode==='Receiving'?!!cap.receiving?.available:!!cap.normalizeName?.available;panel.hidden=!enabled;}}catch{enabled=false;panel.hidden=true;}return enabled;}
            available();closeRoot?.addEventListener('shown.bs.modal',available);
            async function render(r, handoff=false){
                clear();if(!r||r.generation!==generation||!GaoAssistant.sameScope(r.scope,session.identity.scope)||Date.parse(r.expiresAt)<=Date.now())return;
                result=r;output.textContent=(mode==='Receiving'?r.summary:`Đề xuất: ${r.proposedName}`)+'\n'+(r.clarifications||[]).map(c=>c.message).join('\n')+'\n'+(r.warnings||[]).join('\n');
                if(mode==='Receiving'){
                    apply.hidden=r.status!=='matched'||!r.match;
                    const photoCards=[];
                    for(const c of r.candidates||[]){const units=c.units||[];if(!units.length)continue;
                        const choose=(unit)=>{const selection={productId:c.product.productId,variantId:c.product.variantId,...(unit?{unitId:unit.unitId,...(unit.conversionId?{conversionId:unit.conversionId}:{})}:{})};void propose(selection,true);};
                        const b=document.createElement('button');b.type='button';b.className='ga-receiving-choice';b.setAttribute('aria-label',c.product.name);
                        const photo=document.createElement('span');photo.className='ga-receiving-photo';photo.setAttribute('aria-hidden','true');photo.textContent='Ảnh';
                        const label=document.createElement('span'),title=document.createElement('strong'),detail=document.createElement('small');
                        title.textContent=c.product.name;detail.textContent=units.filter(u=>u.canReceive).map(u=>u.name).join(' · ');label.append(title,detail);b.append(photo,label);
                        b.addEventListener('click',()=>choose());choices.append(b);photoCards.push({product:c.product,photo});
                        if(r.resolvedProduct?.variantId===c.product.variantId)for(const u of units.filter(u=>u.canReceive)){const ub=document.createElement('button');ub.type='button';ub.textContent=`${u.name} (quy đổi ×${u.factor})`;ub.addEventListener('click',()=>choose(u));choices.append(ub);}
                    }
                    if(!handoff||!r.resolvedProduct)void loadReceivingImages(photoCards,r);
                    if(handoff&&(r.status==='matched'&&r.match||r.status==='needs_clarification'&&r.resolvedProduct&&r.candidates?.some(c=>c.product.variantId===r.resolvedProduct.variantId)))await applyResult();
                }else{
                    const safePartial=r.status==='needs_clarification'&&(r.clarifications||[]).length>0&&(r.clarifications||[]).every(c=>['capacity','brand','sugar'].includes(c.field));
                    apply.hidden=r.status!=='suggested'&&!safePartial;for(const d of r.duplicates||[]){const p=document.createElement('div');p.textContent=`Hàng có thể trùng: ${d.product.name}. Kiểm tra trước khi tạo.`;choices.append(p);}
                    for(const c of r.changes||[]){const p=document.createElement('div');p.textContent=`${c.from} → ${c.to}: ${c.reason}`;choices.append(p);}
                }
            }
            async function propose(selection,handoff=false){
                if(busy||session.job)return true;const text=input.value.trim();if(!text){output.textContent='Hãy gõ hoặc nói nội dung trước.';return true;}
                if(mode==='Receiving'&&!original)original=text;
                generation++;clear();busy=true;controls();const turn=generation;
                const body=mode==='Receiving'?{originalText:original,text,inputSource:source,generation,...(selection?{selection}:{})}:{originalText:text,inputSource:source,generation};
                output.textContent=receiving?'Gemini đang hiểu câu và tìm hàng…':'Đang tìm đề xuất…';finish=async r=>{if(turn===generation)await render(r,handoff);};
                try{const r=await session.begin(mode,body);if(r)await finish(r);else if(turn===generation)output.textContent='Đang chờ xử lý. Bấm kiểm tra lượt này.';return true;}
                catch(error){if(turn===generation)output.textContent=error.message+' Bạn có thể tiếp tục nhập thủ công.';return !!session.job||turn!==generation;}
                finally{if(turn===generation){busy=false;controls();}}
            }
            send.addEventListener('click',()=>propose());mic.addEventListener('click',()=>GaoAssistant.speech(input,text=>output.textContent=text,()=>{source='speech';if(!receiving)void propose();}));
            if(!receiving)input.addEventListener('keydown',event=>{
                if(!enabled||event.key!=='Enter'||event.isComposing||event.ctrlKey||event.altKey||event.shiftKey)return;
                event.preventDefault();event.stopPropagation();void propose();
            });
            async function recover(retryJob){busy=true;controls();try{const r=await(retryJob?session.retry():session.check());if(r)await finish?.(r);else output.textContent=session.job?.retry?'Chưa có lượt này. Có thể gửi lại cùng nội dung.':'Đang chờ xử lý.';}catch(error){output.textContent=error.message;}finally{busy=false;controls();}}
            check.addEventListener('click',()=>recover(false));retry.addEventListener('click',()=>recover(true));cancel.addEventListener('click',async()=>{await session.cancel();clear();output.textContent='Đã hủy lượt này.';controls();});
            async function applyResult(){
                const snapshot=result,turn=generation;if(!snapshot)return;apply.disabled=true;
                try{await session.context();if(turn!==generation||snapshot!==result||!GaoAssistant.sameScope(snapshot.scope,session.identity.scope)||Date.parse(snapshot.expiresAt)<=Date.now())throw new Error('Đề xuất đã cũ. Hãy yêu cầu lại.');
                    if(mode==='Receiving'){
                        if(!session.identity.permissions.includes('assistant.receiving.resolve'))throw new Error('Quyền hỗ trợ nhập hàng đã thay đổi.');
                        await window.applyAssistantReceivingMatch(snapshot.match||{product:snapshot.resolvedProduct},()=>turn===generation&&snapshot===result);
                    }
                    else{if(!session.identity.permissions.includes('assistant.products.normalize_name'))throw new Error('Quyền đề xuất tên đã thay đổi.');if(!snapshot.proposedName?.trim()||snapshot.proposedName.length>input.maxLength)throw new Error('Tên đề xuất vượt giới hạn của form. Hãy sửa lại.');input.value=snapshot.proposedName;input.dispatchEvent(new Event('input',{bubbles:true}));output.textContent='Đã điền tên đề xuất. Kiểm tra trước khi lưu sản phẩm.';}
                }catch(error){output.textContent=error.message;}finally{apply.disabled=false;}
            }
            apply.addEventListener('click',applyResult);
            manual.addEventListener('click',()=>{invalid();panel.hidden=true;void window.WarehouseReceivingMobile?.searchManually();});
            return {invalidate:invalid,search:async inputSource=>{
                const turn=generation,text=input.value.trim();if(!text||!await available())return false;
                if(turn!==generation||text!==input.value.trim())return true;
                original=text;source=inputSource;return propose(null,true);
            }};
        }
        const term=document.getElementById('wrdCaptureTerm'),capture=document.getElementById('wrdCaptureModal');
        if(term&&documentId){term.maxLength=2000;window.AssistantReceiving=form('Receiving',term,document.getElementById('wrdCaptureSearch'),capture);}
        const name=document.getElementById('riNewName');if(name)form('NormalizeName',name,name,document.getElementById('receiptIntakeModal'));
    }
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
