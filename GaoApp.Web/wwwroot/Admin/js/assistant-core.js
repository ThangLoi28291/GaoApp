(() => {
    'use strict';
    const fingerprint = c => JSON.stringify([c.identity,c.scope.storeId,c.scope.warehouseId,c.scope.legalEntityId,c.screen?.key||null]);
    const sameScope = (a,b) => !!a && !!b && ['storeId','warehouseId','legalEntityId'].every(k=>a[k]===b[k]);
    class AssistantError extends Error { constructor(message,code,status=0,retryAfterSeconds=0){super(message);this.code=code;this.status=status;this.retryAfterSeconds=retryAfterSeconds;} }
    const transient = error => error.status===429 || ['resource_busy','connection_lost','assistant_timeout','assistant_unavailable'].includes(error.code);
    const responseError = (response,body) => new AssistantError(body.message||'Chưa kết nối được trợ lý. Bạn có thể thao tác thủ công.',
        response.status===429?'resource_busy':body.code,response.status,
        Math.min(3600,Math.max(0,Number(response.headers.get('Retry-After'))||Number(body.retryAfterSeconds)||0)));
    class Session {
        constructor(root,documentId=null,screen=null){this.root=root;this.documentId=documentId;this.screen=screen;this.epoch=0;this.identity=null;this.job=null;this.cooldowns=new Map();}
        wait(group){const seconds=Math.ceil(((this.cooldowns.get(group)||0)-Date.now())/1000);if(seconds>0)throw new AssistantError(`Hệ thống đang bận. Vui lòng thử lại sau ${seconds} giây.`,'resource_busy',429,seconds);}
        busy(group,response,body){const error=responseError(response,body);if(error.code==='resource_busy')this.cooldowns.set(group,Date.now()+Math.max(1,error.retryAfterSeconds)*1000);return error;}
        async context(){
            this.wait('context');
            const url=new URL(this.root.dataset.contextUrl,location.origin);if(this.documentId)url.searchParams.set('documentId',this.documentId);
            if(this.screen)url.searchParams.set('screen',this.screen);
            const response=await fetch(url,{cache:'no-store',credentials:'same-origin',redirect:'error'});
            const body=await response.json();if(!response.ok)throw this.busy('context',response,body);
            if(body.contractVersion!=='1.0'||!body.scope||!Array.isArray(body.permissions))throw new AssistantError('Thông tin trợ lý chưa hợp lệ.','invalid_response');
            if(this.screen&&(!body.screen||body.screen.kind!==this.screen||typeof body.screen.key!=='string'||!body.screen.key||typeof body.screen.canApply!=='boolean'))throw new AssistantError('Chưa xác minh được màn hình hiện tại.','invalid_response');
            if(this.identity&&fingerprint(this.identity)!==fingerprint(body)){this.invalidate();this.job=null;}
            this.identity=body;return body;
        }
        invalidate(){this.epoch++;}
        async raw(operation,payload={}){
            const group=['RequestStatus','CancelRequest'].includes(operation)?'control':
                ['Chat','Quote','Receiving','NormalizeName','CreateConversation','ResumeConversation','RenameConversation','DeleteConversation','Feedback'].includes(operation)?'work':'read';
            this.wait(group);
            const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),110000);
            try{
                const token=this.root.querySelector('[name="__RequestVerificationToken"]')?.value||'';
                const response=await fetch(this.root.dataset.callUrl+operation,{method:'POST',credentials:'same-origin',redirect:'error',cache:'no-store',signal:controller.signal,
                    headers:{'Content-Type':'application/json',RequestVerificationToken:token},body:JSON.stringify({...payload,...(this.documentId?{documentId:this.documentId}:{}),...(this.screen?{screen:this.screen}:{})})});
                const body=await response.json();if(body.contractVersion!=='1.0')throw new AssistantError('Phản hồi trợ lý chưa hợp lệ.','invalid_response',response.status);
                if(!response.ok)throw this.busy(group,response,body);
                return {body,status:response.status};
            }catch(error){if(error instanceof AssistantError)throw error;throw new AssistantError('Mất kết nối với trợ lý. Kiểm tra lại lượt này trước khi gửi tiếp.','connection_lost');}
            finally{clearTimeout(timer);}
        }
        async read(operation,payload={}){const epoch=this.epoch;const result=await this.raw(operation,payload);await this.context();if(epoch!==this.epoch)throw new AssistantError('Cửa hàng hoặc phiên đăng nhập đã đổi.','stale');return this.validate(result.body);}
        validate(body){
            if(!body||body.contractVersion!=='1.0')throw new AssistantError('Phản hồi trợ lý chưa hợp lệ.','invalid_response');
            if(body.scope&&!sameScope(body.scope,this.identity?.scope))throw new AssistantError('Phản hồi không thuộc cửa hàng hiện tại.','stale');
            if(body.expiresAt&&Date.parse(body.expiresAt)<=Date.now())throw new AssistantError('Đề xuất đã hết hạn. Hãy yêu cầu lại.','expired');
            return body;
        }
        async begin(operation,body={},resourceId=null){
            if(this.job)throw new AssistantError('Hãy kiểm tra hoặc hủy lượt đang chờ trước.','pending');
            const epoch=this.epoch;await this.context();if(epoch!==this.epoch)throw new AssistantError('Đã bỏ lượt yêu cầu cũ.','stale');
            const job={operation,payload:{requestId:crypto.randomUUID(),body,...(resourceId?{resourceId}:{})},epoch:this.epoch,identity:fingerprint(this.identity),retry:false};
            this.job=job;return this.send(job);
        }
        async send(job){
            try{const r=await this.raw(job.operation,job.payload);await this.context();if(this.job!==job||this.epoch!==job.epoch||fingerprint(this.identity)!==job.identity)throw new AssistantError('Đã bỏ phản hồi cũ.','stale');
                if(r.body.requestId!==job.payload.requestId)throw new AssistantError('Phản hồi không khớp lượt yêu cầu.','invalid_response');
                if(r.status===202)return null;this.job=null;return this.validate(r.body);
            }catch(error){if(this.job===job && !transient(error))this.job=null;throw error;}
        }
        async check(){
            const job=this.job;if(!job)return null;
            await this.context();if(this.job!==job||this.epoch!==job.epoch)throw new AssistantError('Phiên đã đổi.','stale');
            try{const r=await this.raw('RequestStatus',{resourceId:job.payload.requestId});await this.context();
                if(this.job!==job||this.epoch!==job.epoch||r.body.requestId!==job.payload.requestId)throw new AssistantError('Đã bỏ phản hồi cũ.','stale');
                this.validate(r.body);
                if(r.body.status==='processing')return null;
                this.job=null;if(r.body.status==='completed')return this.validate(r.body.result);
                throw new AssistantError(r.body.error?.message||'Lượt này đã hủy hoặc không hoàn tất.',r.body.error?.code||r.body.status);
            }catch(error){if(error.code==='request_not_found'){job.retry=true;return null;}if(!transient(error)&&this.job===job)this.job=null;throw error;}
        }
        async retry(){const job=this.job;await this.context();if(!job?.retry||this.job!==job||this.epoch!==job.epoch)throw new AssistantError('Không thể gửi lại lượt này.','stale');job.retry=false;return this.send(job);}
        async cancel(){const job=this.job;this.invalidate();this.job=null;if(job){try{await this.raw('CancelRequest',{resourceId:job.payload.requestId});}catch{/* Local invalidation also prevents late responses from being applied. */}}}
    }
    let recognition;
    function stopSpeech(){if(recognition){const r=recognition;recognition=null;r.onresult=r.onerror=r.onend=null;r.abort();}}
    function speech(input,status,onFinal=()=>{}){
        stopSpeech();const Speech=window.SpeechRecognition||window.webkitSpeechRecognition;
        if(!Speech){status('Trình duyệt chưa hỗ trợ micro. Bạn vẫn có thể gõ.');return;}
        const r=new Speech();recognition=r;r.lang='vi-VN';r.interimResults=true;r.continuous=false;r.maxAlternatives=1;
        let final='';r.onresult=e=>{if(recognition!==r)return;let interim='';for(let i=e.resultIndex;i<e.results.length;i++){if(e.results[i].isFinal)final+=e.results[i][0].transcript+' ';else interim+=e.results[i][0].transcript;}
            if(final.trim()){input.value=final.trim().slice(0,input.maxLength>0?input.maxLength:2000);input.dispatchEvent(new Event('input',{bubbles:true}));
                status('Đã nhận giọng nói. Sửa chữ nếu cần rồi bấm gửi.');onFinal(final.trim());r.stop();}
            else if(interim.trim())status('Đang nghe: '+interim.trim());};
        r.onerror=e=>status({'not-allowed':'Chưa được cấp quyền micro. Bạn có thể gõ.','no-speech':'Chưa nghe rõ, hãy nói lại.',network:'Dịch vụ giọng nói mất kết nối. Bạn có thể gõ.'}[e.error]||'Không mở được micro. Bạn có thể gõ.');
        r.onend=()=>{if(recognition===r)recognition=null;};try{r.start();status('Đang nghe tiếng Việt…');}catch{stopSpeech();status('Không mở được micro. Bạn có thể gõ.');}
    }
    window.GaoAssistant={Session,AssistantError,sameScope,fingerprint,speech,stopSpeech};
    window.addEventListener('pagehide',stopSpeech);document.addEventListener('visibilitychange',()=>{if(document.hidden)stopSpeech();});
})();
