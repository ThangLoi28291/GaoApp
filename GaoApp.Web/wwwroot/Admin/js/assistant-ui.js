(() => {
    'use strict';
    const scriptNonce = document.currentScript?.nonce || ''; 
    function start(){
        const root=document.getElementById('gaoAssistant');if(!root||!window.GaoAssistant)return;
        const api=new GaoAssistant.Session(root),by=id=>document.getElementById(id);let conversation=null,historyCursor=null,listCursor=null,opened=false,busy=false,lastKey=null,finish=null;
        let viewGeneration=0,workCount=0,queue=Promise.resolve(),ready=false,chatAvailable=false,openingError=null,conversationTitle='Cuộc trò chuyện',screenState=null;
        const openRetry=document.createElement('button');openRetry.type='button';openRetry.id='gaOpenRetry';openRetry.textContent='Thử mở lại trợ lý';openRetry.hidden=true;by('gaStatus').after(openRetry);
        let scanGeneration=0,scanControls=null,scanStream=null,decoderPromise=null,clarificationTimer=null,cardGeneration=0;
        const status=(text,tone='')=>{const node=by('gaStatus');node.textContent=text;node.hidden=!text;node.dataset.tone=tone;};
        function fitViewport(){
            const viewport=window.visualViewport;
            const height=Math.round(viewport?.height||window.innerHeight);
            root.style.setProperty('--ga-viewport-height',`${height}px`);
            root.style.setProperty('--ga-viewport-top',`${Math.round(viewport?.offsetTop||0)}px`);
            by('gaPanel').classList.toggle('ga-compact-height',height<520);
        }
        function showHistory(show,focus=false){
            if(show&&!by('gaScreenView').hidden)showScreen(false);
            by('gaHistoryDrawer').hidden=!show;by('gaThread').hidden=show||!by('gaScreenView').hidden;
            by('gaHistoryToggle').setAttribute('aria-expanded',String(show));
            by('gaPanel').classList.toggle('ga-show-history',show);
            if(!show)by('gaManage').hidden=true;
            if(focus){const target=show&&!by('gaConversations').disabled?by('gaConversations'):by('gaHistoryToggle');target.focus({preventScroll:true});}
        }
        function showScreen(show){
            if(show){stopScan();showHistory(false);}
            by('gaScreenView').hidden=!show;by('gaThread').hidden=show||!by('gaHistoryDrawer').hidden;
            by('gaPanel').classList.toggle('ga-show-screen',show);by('gaScreenToggle').setAttribute('aria-expanded',String(show));
            window.dispatchEvent(new CustomEvent(show?'gao-assistant:screen-opened':'gao-assistant:screen-closed'));
        }
        function controls(){
            by('gaRecovery').hidden=!api.job;by('gaRetry').hidden=!api.job?.retry;
            for(const id of ['gaSend','gaScan','gaConversations','gaRename','gaDelete'])by(id).disabled=busy||!!api.job||!ready;
            by('gaNew').disabled=busy||!!api.job||!chatAvailable;
            for(const id of ['gaCheck','gaRetry'])by(id).disabled=busy;
            openRetry.hidden=!opened||!openingError||!!api.job;openRetry.disabled=busy;
            by('gaScreenToggle').disabled=busy||!!api.job;
            for(const button of by('gaMessages').querySelectorAll('.ga-product-choice,.ga-choice-more'))button.disabled=busy||!!api.job||!ready||button.dataset.active!=='true';
            for(const button of by('gaMessages').querySelectorAll('.ga-attribute-choice'))button.disabled=busy||!!api.job||!ready||button.dataset.active!=='true'||Number(button.dataset.expires)<=Date.now();
            for(const button of by('gaMessages').querySelectorAll('.ga-card-action,.ga-rich-choice'))button.disabled=busy||!!api.job||!ready||button.dataset.active!=='true'||Number(button.dataset.expires)<=Date.now();
            for(const button of by('gaMessages').querySelectorAll('.ga-feedback-choice,.ga-correction-choice'))button.disabled=busy||!!api.job||!ready||Number(button.dataset.expires)<=Date.now();
            const state=by('gaConnectionState');
            state.dataset.state=openingError?'error':api.job?'pending':busy?'busy':ready?'ready':'loading';
            state.textContent=openingError?'Chưa kết nối':api.job?'Cần kiểm tra lượt đang chờ':busy?'Đang xử lý':ready?'Sẵn sàng':'Đang mở hội thoại';
            if(!by('gaScreenView').hidden&&screenState){
                state.dataset.state=screenState.pending?'pending':screenState.busy?'busy':screenState.ready?'ready':'error';
                state.textContent=screenState.pending?'Cần kiểm tra đề xuất':screenState.busy?'Đang kiểm tra màn hình':screenState.ready?'Sẵn sàng hỗ trợ màn hình':'Chưa sẵn sàng điền form';
            }
            by('gaMessages').setAttribute('aria-busy',String(busy));
        }
        window.addEventListener('gao-assistant:screen-state',event=>{screenState=event.detail;controls();});
        function stopScan(){scanGeneration++;scanControls?.stop();scanControls=null;scanStream?.getTracks().forEach(track=>track.stop());scanStream=null;by('gaScanVideo').srcObject=null;by('gaScanner').hidden=true;by('gaScan').setAttribute('aria-expanded','false');}
        function decoder(){
            if(window.ZXingBrowser)return Promise.resolve(window.ZXingBrowser);
            if(!decoderPromise)decoderPromise=new Promise((resolve,reject)=>{
                const script=document.createElement('script');
                const decoderUrl = new URL(root.dataset.barcodeDecoderUrl, location.href);
                if (decoderUrl.origin !== location.origin || !decoderUrl.pathname.endsWith('/lib/zxing-browser/0.1.5/zxing-browser.min.js')) throw new Error('Nguồn bộ quét không hợp lệ.');
                script.src=decoderUrl.href;script.nonce=scriptNonce;script.async=true;
                script.onload=()=>{if(window.ZXingBrowser)resolve(window.ZXingBrowser);else{script.remove();decoderPromise=null;reject(new Error('Chưa khởi tạo được bộ quét. Mình gõ mã vào ô chat rồi Enter nhé.'));}};
                script.onerror=()=>{script.remove();decoderPromise=null;reject(new Error('Chưa tải được bộ quét. Mình gõ mã vào ô chat rồi Enter nhé.'));};document.head.append(script);
            });return decoderPromise;
        }
        async function scanBarcode(){
            if(busy||api.job||!opened||!conversation)return;
            stopScan();GaoAssistant.stopSpeech();const turn=scanGeneration,id=conversation,epoch=api.epoch;
            const current=()=>opened&&turn===scanGeneration&&id===conversation&&epoch===api.epoch;
            if(!window.isSecureContext||!navigator.mediaDevices?.getUserMedia){status('Camera cần HTTPS hoặc localhost. Mình dùng máy quét hoặc gõ mã rồi Enter nhé.');return;}
            by('gaScanner').hidden=false;by('gaScan').setAttribute('aria-expanded','true');status('Cho phép camera và đưa mã vạch vào khung…');
            try{
                const media=await navigator.mediaDevices.getUserMedia({video:{facingMode:{ideal:'environment'}},audio:false});
                if(!current()){media.getTracks().forEach(track=>track.stop());return;}scanStream=media;
                const ZXing=await decoder();if(!current())return;
                const reader=new ZXing.BrowserMultiFormatOneDReader(undefined,{delayBetweenScanAttempts:180,delayBetweenScanSuccess:600});
                const scanning=await reader.decodeFromStream(media,by('gaScanVideo'),(result,error,control)=>{
                    if(!result||!current()||busy||api.job)return;
                    const code=result.getText().trim();if(!/^[\x21-\x7e]{1,64}$/.test(code)){status('Mã chưa hợp lệ. Mình quét lại nhé.');return;}
                    control.stop();stopScan();by('gaInput').value=/^[0-9]{8,64}$/.test(code)?code:`Mã vạch: ${code}`;by('gaComposer').requestSubmit();
                });
                if(!current())scanning.stop();else{scanControls=scanning;status('Đang quét mã sản phẩm…');}
            }catch(error){if(current()){stopScan();status('Chưa mở được camera hoặc bộ quét. Mình cho phép camera, hoặc gõ mã rồi Enter nhé.');}}
        }
        function fail(error){
            status(error.message||'Chưa kết nối được trợ lý.','error');
            if(['permission_denied','permission_revoked','unauthenticated','stale','source_scope_mismatch'].includes(error.code)){
                stopScan();conversation=null;ready=false;finish=null;by('gaMessages').replaceChildren();by('gaConversations').replaceChildren();
                by('gaStoreName').textContent='Chưa xác định cửa hàng';by('gaWelcome').textContent='';by('gaThreadTitle').textContent='Cuộc trò chuyện';showHistory(false);
            }
            if(!ready)openingError=error;controls();
        }
        const visible=t=>opened&&t.view===viewGeneration;
        const current=t=>visible(t)&&t.epoch===api.epoch;
        const ticket=()=>({view:viewGeneration,epoch:api.epoch,key:lastKey});
        function schedule(action){
            const view=viewGeneration;workCount++;busy=true;controls();
            const task=queue.then(async()=>{
                if(!opened||view!==viewGeneration)return;
                const t=ticket();
                try{return await action(t);}catch(error){
                    if(current(t))fail(error);
                    else if(visible(t)&&t.key!==(api.identity?GaoAssistant.fingerprint(api.identity):null))fail(new GaoAssistant.AssistantError('Phiên hoặc cửa hàng đã đổi. Hãy mở lại trợ lý.','stale'));
                }
            });
            queue=task.catch(()=>{});
            return task.finally(()=>{workCount--;busy=workCount>0;controls();});
        }
        async function identity(t){
            const who=await api.context();if(!visible(t))return null;
            const key=GaoAssistant.fingerprint(who);
            if(lastKey!==key){stopScan();conversation=null;ready=false;chatAvailable=false;openingError=null;finish=null;historyCursor=null;listCursor=null;by('gaMessages').replaceChildren();by('gaConversations').replaceChildren();showHistory(false);by('gaThreadTitle').textContent='Cuộc trò chuyện';lastKey=key;window.dispatchEvent(new CustomEvent('gao-assistant:identity-changed'));}
            t.epoch=api.epoch;t.key=key;by('gaWelcome').textContent=`Chào ${who.displayName}.`;
            const store=typeof who.storeName==='string'&&who.storeName.trim()?who.storeName:`Cửa hàng #${who.scope.storeId}`;
            by('gaStoreName').textContent=store;by('gaStoreName').title=store;return who;
        }
        function quoteCard(content,display=null){
            const lines=content.trim().split('\n'),groups=new Map();let test=false;
            for(let line of lines){
                if(line.startsWith('[TEST] ')){test=true;line=line.slice(7);}
                const match=/^(.+) — ([^:]+): (.+)$/.exec(line);if(!match)return null;
                const prices=new Map();
                for(const part of match[3].split('; ')){
                    const price=/^giá (lẻ|sỉ)(?: (\d[\d.,]* đ\/.+?)( \(chưa có giá sỉ, dùng giá lẻ\))?|: (chưa có giá|chưa đọc được giá từ nguồn, vui lòng thử lại))$/.exec(part);
                    if(!price||prices.has(price[1]))return null;
                    prices.set(price[1],{value:price[2]||price[4],fallback:!!price[3],missing:!price[2]});
                }
                if(!groups.has(match[1]))groups.set(match[1],[]);
                groups.get(match[1]).push({unit:match[2],prices});
            }
            const card=document.createElement('div');card.className='ga-quote';
            const title=document.createElement('div');title.className='ga-quote-label';title.textContent=test?'BÁO GIÁ · DỮ LIỆU TEST':'BÁO GIÁ';card.append(title);
            for(const [name,items] of groups){
                const enriched=display?.kind==='quote'&&groups.size===1&&display.items[0].product.name===name;
                if(enriched)card.append(productHeading(display.items[0]));
                else{const heading=document.createElement('h4');heading.textContent=name;card.append(heading);}
                const tiers=['lẻ','sỉ'].filter(t=>items.some(i=>i.prices.has(t)));
                const table=document.createElement('table'),head=document.createElement('thead'),body=document.createElement('tbody'),header=document.createElement('tr');
                for(const text of ['Đơn vị',...tiers.map(t=>'Giá '+t)]){const cell=document.createElement('th');cell.scope='col';cell.textContent=text;header.append(cell);}head.append(header);
                for(const item of items){const row=document.createElement('tr'),unit=document.createElement('th');unit.scope='row';unit.textContent=item.unit;row.append(unit);
                    for(const tier of tiers){const cell=document.createElement('td'),price=item.prices.get(tier);cell.textContent=price?.value||'—';
                        if(price?.missing)cell.className='ga-price-unavailable';
                        if(price?.fallback){const note=document.createElement('small');note.textContent='Chưa có giá sỉ, dùng giá lẻ';cell.append(note);}row.append(cell);}body.append(row);}
                table.append(head,body);card.append(table);
                if(enriched){
                    const stamp=document.createElement('time');stamp.className='ga-price-time';stamp.dateTime=display.priceReadAt;
                    stamp.textContent='Đọc giá lúc '+new Intl.DateTimeFormat('vi-VN',{day:'2-digit',month:'2-digit',year:'numeric',hour:'2-digit',minute:'2-digit',second:'2-digit'}).format(new Date(display.priceReadAt));card.append(stamp);
                    const actions=document.createElement('div');actions.className='ga-product-actions';
                    if(display.canReadWholesale&&['catalog.read','quotes.retail.read','quotes.wholesale.read'].every(p=>api.identity.permissions.includes(p)))actions.append(productAction('Giá sỉ','giá sỉ',display));
                    actions.append(productAction('Tất cả đơn vị','tất cả đơn vị',display),productAction('Chọn hàng khác','đổi mặt hàng khác',display));card.append(actions);
                    if(display.expiry<=Date.now()){const note=document.createElement('small');note.className='ga-card-expired';note.textContent='Lượt báo giá đã hết hạn. Mình hỏi lại để đọc giá mới nhé.';card.append(note);}
                }
            }
            return card;
        }
        function sendChoice(text){if(busy||api.job||!opened)return;by('gaInput').value=text;by('gaComposer').requestSubmit();}
        function choiceCard(content,active,display=null){
            const lines=content.trim().split('\n'),marker=lines.findIndex(line=>line==='Mình chọn sản phẩm bên dưới nhé:'||line==='[TEST] Mình chọn sản phẩm bên dưới nhé:');
            if(marker<0)return null;
            const options=[];let more=false;
            for(const line of lines.slice(marker+1)){
                const match=/^([1-9]\d?|100)\. (.+)$/.exec(line);
                if(match)options.push({number:Number(match[1]),name:match[2]});
                else if(line==='Xem thêm sản phẩm.')more=true;
                else if(line!=='Mình bổ sung loại hoặc quy cách để lọc thêm nhé?')return null;
            }
            if(!options.length||options.length>5||options.some((item,i)=>i&&item.number!==options[i-1].number+1))return null;
            const card=document.createElement('div');card.className='ga-choice-card';
            const intro=document.createElement('p');intro.textContent=lines.slice(0,marker+1).join('\n');card.append(intro);
            if(display?.kind==='choices'){
                if(display.items.length!==options.length||display.hasMore!==more||display.items.some((item,i)=>item.choice!==options[i].number||item.product.name.replace(/[\r\n\t]+/g,' ').trim()!==options[i].name))throw invalid();
                for(const item of display.items){const section=document.createElement('div');section.className='ga-product-result';section.append(productHeading(item));
                    section.append(productAction('Chọn sản phẩm',`chọn ${item.choice}. ${item.product.name}`,display,'ga-rich-choice'));card.append(section);}
                if(more)card.append(productAction('Xem thêm sản phẩm','xem thêm',display));return card;
            }
            for(const option of options){const button=document.createElement('button');button.type='button';button.className='ga-product-choice';button.dataset.active=String(active);button.disabled=!active||busy||!!api.job;
                const number=document.createElement('span');number.className='ga-choice-number';number.textContent=option.number;
                const name=document.createElement('span');name.textContent=option.name;button.append(number,name);
                button.addEventListener('click',()=>sendChoice(`chọn ${option.number}. ${option.name}`));card.append(button);}
            if(more){const button=document.createElement('button');button.type='button';button.className='ga-choice-more';button.dataset.active=String(active);button.disabled=!active||busy||!!api.job;button.textContent='Xem thêm sản phẩm';button.addEventListener('click',()=>sendChoice('xem thêm'));card.append(button);}
            if(!active){const note=document.createElement('small');note.className='ga-choice-history';note.textContent='Danh sách từ lượt trước';card.append(note);}
            return card;
        }
        function message(content,role,active=false,display=null){
            const row=document.createElement('div');row.className='ga-message';row.dataset.role=role;
            const quote=role==='assistant'?quoteCard(content,display):null;
            const choices=role==='assistant'?choiceCard(content,active,display):null;
            if(quote){row.classList.add('ga-message-quote');row.append(quote);}
            else if(choices){row.classList.add('ga-message-choices');row.append(choices);}
            else{
                const list=role==='assistant'?/^(.*?)\n(1\. [\s\S]*\n2\. [\s\S]*)$/s.exec(content):null;
                if(list){const intro=document.createElement('div');intro.textContent=list[1];const details=document.createElement('details'),label=document.createElement('summary'),items=document.createElement('div');
                    label.textContent='Xem danh sách sản phẩm đã gợi ý';items.textContent=list[2];details.append(label,items);row.append(intro,details);}
                else row.textContent=content;
            }
            return row;
        }
        function imagePath(value){
            if(value==null)return null;
            if(typeof value!=='string'||value.length>1000||!value.startsWith('/uploads/products/')||/[?#:\\]/.test(value))throw invalid();
            let path;try{path=decodeURIComponent(value);}catch{throw invalid();}
            if(/[\u0000-\u001f\u007f%\\]/.test(path)||path.split('/').some((v,i)=>i&&(!v||v==='.'||v==='..'))||!/^.+\.(?:jpe?g|png|webp|gif)$/i.test(path))throw invalid();
            return value;
        }
        function productDisplay(value,items){
            if(value==null)return null;
            if(!GaoAssistant.sameScope(value.scope,api.identity.scope))throw new GaoAssistant.AssistantError('Thẻ sản phẩm sai cửa hàng.','source_scope_mismatch');
            const latest=items[items.length-1],expiry=Date.parse(value.expiresAt),text=(v,max)=>typeof v==='string'&&!!v.trim()&&v.length<=max&&!/[\u0000-\u001f\u007f]/.test(v);
            if(typeof value.messageId!=='string'||!['quote','choices','product'].includes(value.kind)||!Number.isFinite(expiry)
                ||!Array.isArray(value.items)||value.items.length<1||value.items.length>5||typeof value.hasMore!=='boolean'
                ||typeof value.canReadWholesale!=='boolean'||value.kind!=='choices'&&(value.items.length!==1||value.hasMore)
                ||value.items.some(item=>!item||!item.product||!text(item.product.productId,120)||!text(item.product.variantId,120)||!text(item.product.name,300)
                    ||item.sku!=null&&!text(item.sku,60)||!item.attributes||!Array.isArray(item.saleUnits)||item.saleUnits.length>100||item.saleUnits.some(v=>!text(v,120))
                    ||value.kind==='choices'&&(!Number.isInteger(item.choice)||item.choice<1||item.choice>100)
                    ||value.kind!=='choices'&&item.choice!=null)
                ||new Set(value.items.map(i=>i.product.variantId)).size!==value.items.length
                ||value.kind==='quote'&&(!Number.isFinite(Date.parse(value.priceReadAt))||!Array.isArray(value.priceTiers)||value.priceTiers.length<1||value.priceTiers.length>2
                    ||value.priceTiers.some(t=>!['retail','wholesale'].includes(t))||new Set(value.priceTiers).size!==value.priceTiers.length))throw invalid();
            const required=['catalog.read',...(value.kind==='quote'?['quotes.retail.read',...(value.priceTiers.includes('wholesale')?['quotes.wholesale.read']:[])]:[])];
            if(required.some(p=>!api.identity.permissions.includes(p)))throw new GaoAssistant.AssistantError('Quyền đọc kết quả sản phẩm vừa thay đổi. Mình mở lại trợ lý nhé.','permission_revoked');
            for(const item of value.items){imagePath(item.imageUrl);for(const name of ['family','brand','capacity','flavor'])if(item.attributes[name]!=null&&!text(item.attributes[name],120))throw invalid();
                if(item.attributes.sugar!=null&&!['sweetened','less_sugar','unsweetened','not_applicable'].includes(item.attributes.sugar))throw invalid();}
            return latest?.role==='assistant'&&latest.messageId===value.messageId?{...value,expiry}:null;
        }
        function productHeading(item){
            const header=document.createElement('div');header.className='ga-product-heading';
            const photo=document.createElement('div');photo.className='ga-product-photo';const placeholder=document.createElement('span');placeholder.textContent='▧';placeholder.setAttribute('aria-hidden','true');photo.append(placeholder);
            if(item.imageUrl){const img=document.createElement('img');img.alt=item.product.name;img.width=64;img.height=64;img.loading='lazy';img.decoding='async';img.referrerPolicy='no-referrer';
                img.addEventListener('load',()=>placeholder.hidden=true);img.addEventListener('error',()=>{img.remove();placeholder.hidden=false;});img.src=item.imageUrl;photo.append(img);}
            const details=document.createElement('div');details.className='ga-product-details';const title=document.createElement('h4');title.textContent=item.product.name;
            const sku=document.createElement('div');sku.className='ga-product-sku';sku.textContent=item.sku?'Mã hàng: '+item.sku:'Chưa có mã hàng';details.append(title,sku);
            const a=item.attributes,sugar={sweetened:'Có đường',less_sugar:'Ít đường',unsweetened:'Không đường'}[a.sugar];
            const attributes=[a.brand,sugar,a.capacity,a.flavor].filter(Boolean);if(attributes.length){const tags=document.createElement('div');tags.className='ga-product-tags';
                for(const value of attributes){const tag=document.createElement('span');tag.textContent=value;tags.append(tag);}details.append(tags);}
            header.append(photo,details);return header;
        }
        function productAction(label,text,display,className='ga-card-action'){
            const button=document.createElement('button');button.type='button';button.className=className;button.textContent=label;
            button.dataset.active='true';button.dataset.expires=String(display.expiry);
            const generation=cardGeneration,id=conversation,key=lastKey;
            button.addEventListener('click',()=>{if(button.disabled||!ready||!opened||display.expiry<=Date.now()||generation!==cardGeneration||id!==conversation||key!==lastKey)return;sendChoice(text);});return button;
        }
        function clarificationHint(value,items){
            if(value==null)return null;
            if(!GaoAssistant.sameScope(value.scope,api.identity.scope))throw new GaoAssistant.AssistantError('Gợi ý sai cửa hàng.','source_scope_mismatch');
            const latest=items[items.length-1],expiry=Date.parse(value.expiresAt);
            if(typeof value.messageId!=='string'||!['brand','sugar','capacity','flavor'].includes(value.field)||!Number.isFinite(expiry)
                ||!Array.isArray(value.options)||value.options.length<2||value.options.length>8
                ||value.options.some(v=>typeof v!=='string'||!v.trim()||v.length>100||/[\u0000-\u001f\u007f]/.test(v))
                ||new Set(value.options.map(v=>v.toLocaleLowerCase('vi'))).size!==value.options.length)throw invalid();
            return latest?.role==='assistant'&&latest.messageId===value.messageId&&expiry>Date.now()?{...value,expiry}:null;
        }
        function attributeChoices(row,hint,id){
            const group=document.createElement('div');group.className='ga-attribute-choices';group.setAttribute('role','group');group.setAttribute('aria-label','Chọn thuộc tính sản phẩm');
            const identityKey=lastKey;
            for(const option of hint.options){
                const button=document.createElement('button');button.type='button';button.className='ga-attribute-choice';button.textContent=option;button.dataset.active='true';button.dataset.expires=String(hint.expiry);
                button.addEventListener('click',()=>{if(button.disabled||button.dataset.active!=='true'||hint.expiry<=Date.now()||!ready||identityKey!==lastKey||id!==conversation)return;sendChoice(option);});group.append(button);
            }
            row.append(group);
        }
        const invalid=()=>new GaoAssistant.AssistantError('Phản hồi hội thoại chưa hợp lệ. Vui lòng thử mở lại.','invalid_response');
        function conversationId(value){if(typeof value!=='string'||!/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(value))throw invalid();return value;}
        function selectConversation(){
            if(!conversation)return;
            const select=by('gaConversations');
            if(!Array.from(select.options).some(option=>option.value===conversation)){
                const option=document.createElement('option');option.value=conversation;option.textContent=conversationTitle;select.append(option);
            }
            select.value=conversation;
            const title=select.selectedOptions[0]?.textContent||conversationTitle;
            by('gaThreadTitle').textContent=title;by('gaThreadTitle').title=title;
        }
        function adopt(r,t){
            if(!current(t))return false;
            conversation=conversationId(r.conversationId);conversationTitle=r.title||'Cuộc trò chuyện';selectConversation();return true;
        }
        let correctionDialog=null;
        function closeCorrection(){if(correctionDialog){correctionDialog.dispatchEvent(new Event('gao-cancel'));correctionDialog.close();correctionDialog.remove();correctionDialog=null;}}
        window.addEventListener('gao-assistant:closed',closeCorrection);
        window.addEventListener('gao-assistant:identity-changed',closeCorrection);
        function correctProduct(id,item,initial,originButton){
            closeCorrection();
            const key=lastKey,epoch=api.epoch,view=viewGeneration;
            const dialog=document.createElement('dialog');dialog.className='ga-correction';correctionDialog=dialog;
            const make=(tag,text='',className='')=>{const element=document.createElement(tag);element.textContent=text;if(className)element.className=className;return element;};
            const heading=make('h3','Sửa đúng sản phẩm');heading.id='gaCorrectionTitle';dialog.setAttribute('aria-labelledby',heading.id);
            const close=make('button','Đóng');close.type='button';close.className='ga-correction-close';close.addEventListener('click',()=>{closeCorrection();if(originButton.isConnected&&!by('gaPanel').hidden)originButton.focus();});
            const header=make('div','','ga-correction-header');header.append(heading,close);
            const note=make('p','Chọn đúng mã hàng và gửi cách gọi để quản trị duyệt. Đề xuất mới chưa làm thay đổi kết quả tìm kiếm.');
            const teachLabel=make('label','Cách gọi muốn dạy cho trợ lý');teachLabel.htmlFor='gaTeachQuery';
            const teach=make('input');teach.id='gaTeachQuery';teach.maxLength=200;teach.value=initial.trim().slice(0,200);teach.autocomplete='off';
            const searchLabel=make('label','Tìm sản phẩm đúng bằng tên, SKU hoặc mã vạch');searchLabel.htmlFor='gaCorrectionSearch';
            const search=make('input');search.id='gaCorrectionSearch';search.maxLength=200;search.value=teach.value;search.autocomplete='off';
            const find=make('button','Tìm sản phẩm');find.type='submit';const searchForm=make('form','','ga-correction-search');searchForm.append(search,find);
            const results=make('div','','ga-correction-results'),selectedNote=make('p'),notice=make('p','','ga-correction-status');notice.setAttribute('role','status');notice.setAttribute('aria-live','polite');
            const send=make('button','Gửi để duyệt');send.type='button';send.className='ga-correction-send';send.disabled=true;
            dialog.append(header,note,teachLabel,teach,searchLabel,searchForm,results,selectedNote,notice,send);by('gaPanel').append(dialog);
            let selected=null,working=false,controller=null,requestId=null;
            const alive=()=>opened&&dialog.open&&conversation===id&&lastKey===key&&api.epoch===epoch&&viewGeneration===view;
            const controls=()=>{find.disabled=working;search.disabled=working;teach.disabled=working;send.disabled=working||!selected||!teach.value.trim();for(const button of results.querySelectorAll('button'))button.disabled=working;};
            dialog.addEventListener('gao-cancel',()=>controller?.abort());
            dialog.addEventListener('cancel',event=>{event.preventDefault();close.click();});
            dialog.addEventListener('keydown',event=>{if(event.key==='Escape'){event.preventDefault();event.stopPropagation();close.click();}});
            dialog.addEventListener('click',event=>{if(event.target===dialog)close.click();});
            teach.addEventListener('input',()=>{requestId=null;controls();});
            async function request(endpoint,method,body){
                const t=ticket();await identity(t);if(!alive())throw new GaoAssistant.AssistantError('Phiên hoặc cửa hàng đã đổi.','stale');
                const url=new URL(root.dataset.searchUrl,location.origin);if(url.origin!==location.origin)throw invalid();url.pathname+=endpoint;
                if(method==='GET')url.searchParams.set('query',body.query);
                controller=new AbortController();const timeout=setTimeout(()=>controller.abort(),20000);
                try{
                    const token=root.querySelector('[name="__RequestVerificationToken"]')?.value||'';
                    const response=await fetch(url,{method,credentials:'same-origin',redirect:'error',cache:'no-store',signal:controller.signal,
                        headers:{'Content-Type':'application/json',RequestVerificationToken:token},...(method==='GET'?{}:{body:JSON.stringify(body)})});
                    const result=await response.json();if(!response.ok)throw new GaoAssistant.AssistantError(result.message||'Chưa xử lý được đề xuất.',result.code,response.status);
                    await identity(t);if(!alive())throw new GaoAssistant.AssistantError('Phiên hoặc cửa hàng đã đổi.','stale');
                    if(result.contractVersion!=='1.0'||!GaoAssistant.sameScope(result.scope,api.identity.scope))throw invalid();return result;
                }finally{clearTimeout(timeout);controller=null;}
            }
            function errorText(error){if(['stale','permission_denied','permission_revoked','unauthenticated'].includes(error.code)){closeCorrection();fail(error);return;}if(alive())notice.textContent=error.message||'Chưa xác nhận được thao tác. Mình có thể thử lại cùng đề xuất.';}
            searchForm.addEventListener('submit',async event=>{
                event.preventDefault();if(working||!alive()||!search.value.trim())return;
                working=true;selected=null;requestId=null;results.replaceChildren();selectedNote.textContent='';notice.textContent='Đang tìm trong cửa hàng hiện tại…';controls();
                try{
                    const text=search.value.trim();const found=await request('products','GET',{query:/^[0-9]{8,64}$/.test(text)?'barcode:'+text:text});
                    if(!Array.isArray(found.items)||found.items.length>20||found.items.some(p=>!p||!/^\d+$/.test(p.productId)||!/^\d+$/.test(p.variantId)||typeof p.name!=='string'||p.name.length>300))throw invalid();
                    for(const product of found.items){
                        const row=make('article'),name=make('strong',product.name),code=make('small','Mã biến thể #'+product.variantId),pick=make('button','Đúng sản phẩm này');pick.type='button';
                        pick.addEventListener('click',()=>{if(working||!alive())return;selected=product;requestId=null;selectedNote.textContent='Đã chọn: '+product.name+' (#'+product.variantId+')';for(const b of results.querySelectorAll('button'))b.setAttribute('aria-pressed',String(b===pick));controls();});
                        row.append(name,code,pick);results.append(row);
                    }
                    notice.textContent=found.items.length?(found.hasMore?'Còn nhiều kết quả. Bổ sung tên hoặc quy cách để tìm chính xác.':'Kiểm tra tên và quy cách rồi chọn mã đúng.'):'Chưa thấy mã phù hợp. Thử tên khác hoặc quét/gõ đúng mã vạch.';
                }catch(error){errorText(error);}finally{working=false;if(alive())controls();}
            });
            send.addEventListener('click',async()=>{
                if(working||!alive()||!selected||!teach.value.trim())return;
                working=true;requestId??=crypto.randomUUID();controls();notice.textContent='Đang ghi đề xuất…';
                try{
                    const result=await request('corrections','POST',{requestId,conversationId:id,messageId:item.messageId,query:teach.value.trim(),productId:Number(selected.productId),variantId:Number(selected.variantId)});
                    if(result.requestId!==requestId||!['pending','approved','rejected'].includes(result.correction?.status))throw invalid();
                    const outcome=result.correction.status==='approved'?'Cách gọi đã được quản trị duyệt.':result.correction.status==='rejected'?'Đề xuất này đã bị từ chối. Mình kiểm tra lại cách gọi và mã hàng nhé.':'Đã ghi sản phẩm đúng và chờ quản trị duyệt.';
                    by('gaInput').value=selected.name;closeCorrection();status(outcome+' Tên sản phẩm đã chọn được điền vào ô chat để bạn kiểm tra và hỏi lại.');
                }catch(error){errorText(error);}finally{working=false;if(alive())controls();}
            });
            dialog.showModal();teach.focus();controls();
        }
        async function history(older=false,t=ticket()){
            const id=conversation;if(!id||!current(t))return false;
            const r=await api.read('History',{resourceId:id,limit:50,includeClarification:true,includeProducts:true,includeFeedback:true,...(older&&historyCursor?{cursor:historyCursor}:{})});
            if(!current(t)||id!==conversation)return false;
            if(r.conversationId!==id||!Array.isArray(r.items))throw invalid();
            if(r.feedback!=null&&(!Array.isArray(r.feedback)||r.feedback.length>r.items.length||r.feedback.some(f=>!f||typeof f.messageId!=='string'||!['wrong_product','wrong_price','unclear'].includes(f.kind)
                ||!r.items.some(m=>m.role==='assistant'&&m.messageId===f.messageId))||new Set(r.feedback.map(f=>f.messageId)).size!==r.feedback.length))throw invalid();
            const hint=older?null:clarificationHint(r.clarification,r.items);
            const display=older?null:productDisplay(r.productDisplay,r.items);
            if(!older)cardGeneration++;
            const rows=r.items.map((m,i)=>{
                if(!GaoAssistant.sameScope(m.scope,api.identity.scope))throw new GaoAssistant.AssistantError('Tin nhắn sai cửa hàng.','source_scope_mismatch');
                const hinted=hint&&m.messageId===hint.messageId&&i===r.items.length-1;
                const suffix=hinted?' '+hint.options.map(v=>hint.field==='sugar'?v.toLowerCase():v).join(' / ')+'. Mình chọn hoặc gõ thêm nhé.':null;
                const content=suffix&&m.content.endsWith(suffix)?m.content.slice(0,-suffix.length):m.content;
                const rowDisplay=display&&i===r.items.length-1?display:null;
                const active=!older&&i===r.items.length-1&&(!Object.hasOwn(r,'productDisplay')||!!rowDisplay&&rowDisplay.expiry>Date.now());
                const row=message(content,m.role,active,rowDisplay);
                if(rowDisplay?.kind==='product'){
                    row.replaceChildren();if(content.startsWith('[TEST] ')){const label=document.createElement('small');label.textContent='DỮ LIỆU TEST';label.className='ga-source-note';row.append(label);}
                    row.append(productHeading(rowDisplay.items[0]));const units=document.createElement('p');units.className='ga-sale-units';units.textContent='Đơn vị bán: '+rowDisplay.items[0].saleUnits.join(', ');row.append(units);
                }
                if(hinted)attributeChoices(row,hint,id);
                if(m.role==='assistant'&&Array.isArray(r.feedback)&&Number.isFinite(Date.parse(m.expiresAt))){
                    const selected=r.feedback.find(f=>f.messageId===m.messageId)?.kind||null,expiry=Date.parse(m.expiresAt),key=lastKey;
                    const group=document.createElement('div');group.className='ga-feedback';group.setAttribute('role','group');group.setAttribute('aria-label','Phản hồi câu trả lời');
                    for(const [kind,label] of [['wrong_product','Sai hàng'],['wrong_price','Sai giá'],['unclear','Chưa hiểu']]){
                        const button=document.createElement('button');button.type='button';button.className='ga-feedback-choice';button.textContent=label;button.dataset.expires=expiry;
                        button.setAttribute('aria-pressed',String(selected===kind));button.title=selected===kind?'Bấm lại để bỏ phản hồi':'Báo câu trả lời cần kiểm tra';
                        button.addEventListener('click',()=>{
                            if(!opened||busy||api.job||!ready||!row.isConnected||conversation!==id||lastKey!==key||expiry<=Date.now())return;
                            void schedule(async turn=>{
                                if(!await identity(turn)||!current(turn)||conversation!==id||lastKey!==key||!row.isConnected)return;
                                await run('Feedback',{conversationId:id,messageId:m.messageId,kind:selected===kind?null:kind},null,async(result,active)=>{
                                    if(!current(active))return false;const done=await history(false,active);if(done)status('Đã ghi phản hồi. Cảm ơn bạn đã kiểm tra kết quả.');return done;
                                },turn);
                            });
                        });group.append(button);
                    }
                    if(root.dataset.searchUrl&&api.identity.permissions.includes('catalog.read')){
                        const correct=document.createElement('button');correct.type='button';correct.className='ga-correction-choice';correct.textContent='Sửa đúng sản phẩm';correct.dataset.expires=expiry;
                        correct.disabled=expiry<=Date.now();correct.addEventListener('click',()=>{
                            if(!opened||busy||api.job||!ready||!row.isConnected||conversation!==id||lastKey!==key||expiry<=Date.now())return;
                            const previous=r.items.slice(0,i).reverse().find(message=>message.role==='user');correctProduct(id,m,previous?.content||'',correct);
                        });group.append(correct);
                    }
                    row.append(group);
                }
                return row;
            });
            if(older)by('gaMessages').prepend(...rows);else by('gaMessages').replaceChildren(...rows);
            clearTimeout(clarificationTimer);
            function expireControls(){
                controls();const expiries=Array.from(by('gaMessages').querySelectorAll('[data-expires]'),b=>Number(b.dataset.expires)).filter(v=>Number.isFinite(v)&&v>Date.now());
                clarificationTimer=expiries.length?setTimeout(expireControls,Math.min(2147483647,Math.max(1,Math.min(...expiries)-Date.now()+1))):null;
            }
            expireControls();
            historyCursor=r.nextCursor;by('gaHistoryMore').hidden=!historyCursor;
            if(!older)by('gaMessages').scrollTop=by('gaMessages').scrollHeight;return true;
        }
        async function list(more=false,t=ticket()){
            const r=await api.read('ListConversations',{limit:50,...(more&&listCursor?{cursor:listCursor}:{})});
            if(!current(t))return false;if(!Array.isArray(r.items))throw invalid();
            const options=r.items.map(item=>{
                if(!GaoAssistant.sameScope(item.scope,api.identity.scope))throw new GaoAssistant.AssistantError('Hội thoại sai cửa hàng.','source_scope_mismatch');
                const option=document.createElement('option');option.value=conversationId(item.conversationId);option.textContent=item.title;return option;
            });
            if(!more)by('gaConversations').replaceChildren();by('gaConversations').append(...options);
            listCursor=r.nextCursor;by('gaListMore').hidden=!listCursor;selectConversation();return true;
        }
        async function hydrate(r,t){
            if(!adopt(r,t))return false;ready=false;
            if(!await list(false,t)||!await history(false,t))return false;
            if(!current(t))return false;ready=true;openingError=null;showHistory(false);controls();return true;
        }
        async function run(operation,body,resourceId,onDone,t=ticket()){
            if(!current(t))return false;stopScan();status(operation==='Chat'?'Đang xử lý câu hỏi…':operation==='ResumeConversation'?'Đang mở hội thoại…':'Đang cập nhật hội thoại…');
            const completion={onDone,key:lastKey,epoch:api.epoch};finish=completion;
            const result=await api.begin(operation,body,resourceId);
            if(!current(t)){if(!api.job&&finish===completion)finish=null;return false;}
            if(!result){status('Lượt này đang chờ. Bấm kiểm tra để lấy kết quả.');controls();return false;}
            const done=await onDone(result,t);
            if(current(t)&&finish===completion){finish=null;if(done!==false)status('');}
            return current(t)&&done!==false;
        }
        async function initialize(t){
            ready=false;if(!await identity(t)||!current(t))return false;
            const cap=await api.read('Capabilities');if(!current(t))return false;
            chatAvailable=!!cap.chat?.available;by('gaComposer').hidden=!chatAvailable;
            if(!chatAvailable)throw new GaoAssistant.AssistantError('Chat đang tắt hoặc bạn chưa có quyền chat.','feature_disabled');
            if(api.job){status('Có một lượt đang chờ. Kiểm tra trước khi gửi tiếp.');controls();return false;}
            return run('ResumeConversation',{},null,hydrate,t);
        }
        function closePanel(){
            const returnFocus=by('gaPanel').contains(document.activeElement);
            viewGeneration++;opened=false;stopScan();GaoAssistant.stopSpeech();
            clearTimeout(clarificationTimer);
            showScreen(false);
            by('gaPanel').hidden=true;showHistory(false);by('gaOpen').setAttribute('aria-expanded','false');controls();
            window.dispatchEvent(new CustomEvent('gao-assistant:closed'));
            if(returnFocus)by('gaOpen').focus({preventScroll:true});
        }
        function openPanel(){
            opened=true;viewGeneration++;ready=false;by('gaPanel').hidden=false;by('gaOpen').setAttribute('aria-expanded','true');status('Đang mở hội thoại…');
            fitViewport();
            window.dispatchEvent(new CustomEvent('gao-assistant:opened'));
            return schedule(async t=>{if(await initialize(t)&&current(t))by('gaInput').focus();});
        }
        by('gaOpen').addEventListener('click',()=>{if(opened)closePanel();else void openPanel();});
        by('gaClose').addEventListener('click',closePanel);
        by('gaScreenToggle').addEventListener('click',()=>{if(opened&&!busy&&!api.job)showScreen(by('gaScreenView').hidden);});
        by('gaScreenBack').addEventListener('click',()=>showScreen(false));
        by('gaHistoryToggle').addEventListener('click',()=>showHistory(by('gaHistoryDrawer').hidden,true));
        by('gaHistoryBack').addEventListener('click',()=>showHistory(false,true));
        by('gaExpand').addEventListener('click',()=>{
            const expanded=by('gaPanel').classList.toggle('ga-expanded');
            by('gaExpand').setAttribute('aria-pressed',String(expanded));
            const label=expanded?'Thu gọn cửa sổ chat':'Mở rộng cửa sổ chat';by('gaExpand').setAttribute('aria-label',label);by('gaExpand').title=label;fitViewport();
        });
        window.addEventListener('resize',fitViewport);window.visualViewport?.addEventListener('resize',fitViewport);window.visualViewport?.addEventListener('scroll',fitViewport);
        document.addEventListener('pointerdown',event=>{if(!root.inert&&opened&&!by('gaPanel').contains(event.target)&&!by('gaOpen').contains(event.target))closePanel();});
        document.addEventListener('keydown',event=>{if(root.inert||(window.matchMedia('(max-width: 767.98px)').matches&&(event.defaultPrevented||by('gaPanel').querySelector('dialog[open]'))))return;if(opened&&event.key==='Escape'){event.preventDefault();closePanel();by('gaOpen').focus();}});
        openRetry.addEventListener('click',()=>schedule(async t=>{if(await initialize(t)&&current(t))by('gaInput').focus();}));
        by('gaInput').addEventListener('keydown',event=>{
            if(event.key==='Enter'&&!event.shiftKey&&!event.isComposing&&event.keyCode!==229){
                event.preventDefault();if(!event.repeat&&!busy&&!api.job&&ready)by('gaComposer').requestSubmit();
            }
        });
        by('gaComposer').addEventListener('submit',event=>{
            event.preventDefault();if(!opened||busy||api.job)return;
            const text=by('gaInput').value.trim();if(!text)return;
            if(!ready&&openingError){status(openingError.message,'error');controls();return;}
            void schedule(async t=>{
                stopScan();if(!await identity(t)||!current(t))return;
                if(!conversation||!ready){if(!await initialize(t)||!current(t))return;}
                const id=conversation;by('gaInput').value='';
                await run('Chat',{conversationId:id,message:text},null,async(r,active)=>{
                    if(r.conversationId!==id)throw invalid();return history(false,active);
                },t);
            });
        });
        by('gaMic').addEventListener('click',()=>{stopScan();GaoAssistant.speech(by('gaInput'),status);});
        by('gaScan').addEventListener('click',scanBarcode);by('gaScanStop').addEventListener('click',()=>{stopScan();status('Đã dừng quét.');by('gaInput').focus();});
        by('gaNew').addEventListener('click',()=>{if(busy||api.job||!chatAvailable)return;by('gaManage').hidden=true;void schedule(t=>run('CreateConversation',{},null,hydrate,t));});
        by('gaConversations').addEventListener('change',()=>{
            if(busy||api.job){selectConversation();return;}
            const id=by('gaConversations').value;
            void schedule(async t=>{
                stopScan();by('gaManage').hidden=true;ready=false;conversation=conversationId(id);by('gaMessages').replaceChildren();
                const result=await api.read('GetConversation',{resourceId:id});if(!current(t))return;
                adopt(result,t);if(await history(false,t)&&current(t)){ready=true;openingError=null;showHistory(false);controls();by('gaInput').focus();}
            });
        });
        by('gaHistoryMore').addEventListener('click',()=>{if(!busy&&!api.job)void schedule(t=>history(true,t));});
        by('gaListMore').addEventListener('click',()=>{if(!busy&&!api.job)void schedule(t=>list(true,t));});
        by('gaRename').addEventListener('click',()=>{by('gaManage').hidden=false;by('gaTitle').hidden=false;by('gaTitleLabel').hidden=false;by('gaDeleteHint').hidden=true;by('gaTitle').value=by('gaConversations').selectedOptions[0]?.textContent||'';by('gaRenameSave').hidden=false;by('gaDeleteConfirm').hidden=true;by('gaTitle').focus();});
        by('gaDelete').addEventListener('click',()=>{by('gaManage').hidden=false;by('gaTitle').hidden=true;by('gaTitleLabel').hidden=true;by('gaDeleteHint').hidden=false;by('gaRenameSave').hidden=true;by('gaDeleteConfirm').hidden=false;by('gaManageCancel').focus();});
        by('gaManageCancel').addEventListener('click',()=>{by('gaManage').hidden=true;});
        by('gaRenameSave').addEventListener('click',()=>{const title=by('gaTitle').value.trim();if(!title||!conversation||busy||api.job)return;void schedule(t=>run('RenameConversation',{title},conversation,async(r,active)=>{if(!adopt(r,active))return false;by('gaManage').hidden=true;return list(false,active);},t));});
        by('gaDeleteConfirm').addEventListener('click',()=>{if(!conversation||busy||api.job)return;void schedule(t=>run('DeleteConversation',null,conversation,async(r,active)=>{by('gaManage').hidden=true;conversation=null;ready=false;by('gaMessages').replaceChildren();return run('ResumeConversation',{},null,hydrate,active);},t));});
        function recover(retry=false){
            if(busy||!api.job)return;
            void schedule(async t=>{
                if(!await identity(t)||!current(t)||!api.job)return;
                const completion=finish;
                const r=await(retry?api.retry():api.check());if(!current(t))return;
                if(r){
                    if(!completion||completion.key!==lastKey||completion.epoch!==api.epoch)throw new GaoAssistant.AssistantError('Lượt này thuộc phiên trước. Hãy mở lại trợ lý.','stale');
                    const done=await completion.onDone(r,t);
                    if(current(t)){
                        if(finish===completion)finish=null;
                        if(done!==false){
                            openingError=null;
                            if(!ready&&!api.job)await initialize(t);
                            if(current(t)&&ready)status('');
                        }
                    }
                }else status(api.job?.retry?'Chưa có lượt này trên server. Có thể gửi lại cùng nội dung.':'Đang chờ xử lý.');
            });
        }
        by('gaCheck').addEventListener('click',()=>recover());by('gaRetry').addEventListener('click',()=>recover(true));
        by('gaCancel').addEventListener('click',async()=>{
            const view=viewGeneration;await api.cancel();finish=null;
            if(opened&&view===viewGeneration){status('Đã hủy lượt đang chờ.');controls();}
        });
        window.addEventListener('pageshow',()=>{if(opened)void schedule(async t=>{if(await identity(t)&&current(t)&&!ready&&!api.job)await initialize(t);});});
        window.addEventListener('pagehide',closePanel);
        fitViewport();
        controls();
        GaoAssistant.panel={isOpen:()=>opened,isBusy:()=>busy||!!api.job,showScreen,close:closePanel};

    }
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
