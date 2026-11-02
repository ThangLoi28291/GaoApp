(function (root) {
    'use strict';
    const states = { Created:'Chờ soạn', Picking:'Đang soạn', AwaitingApproval:'Chờ duyệt thay đổi', ReadyForHandover:'Sẵn sàng giao', HandedOver:'Đã nhận hàng · chờ đi', Delivering:'Đang giao', AwaitingReconciliation:'Chờ bàn giao tại quầy', ReadyToSettle:'Chờ chốt', CancellationPending:'Cần xử lý hủy', Settled:'Đã hoàn tất', Cancelled:'Đã hủy' };
    const stages = { picking:'Đơn chờ soạn', ready:'Đơn sẵn sàng giao', delivering:'Đơn đang giao · chờ bàn giao', completed:'Đơn đã kết thúc', attention:'Đơn cần xử lý' };
    const positive = value => Number.isSafeInteger(value) && value > 0;
    const guid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
    function qty(value) {
        const text = String(value ?? '').replace(',', '.');
        if (!/^\d{1,14}(?:\.\d{1,4})?$/.test(text)) throw new Error('Nhập số lượng không âm, tối đa 4 số lẻ.');
        const [whole, fraction = ''] = text.split('.');
        const amount = BigInt(whole) * 10000n + BigInt(fraction.padEnd(4, '0'));
        if (amount >= 1000000000000000000n) throw new Error('Số lượng vượt giới hạn.');
        return amount;
    }
    function quantityText(amount) { const fraction = (amount % 10000n).toString().padStart(4, '0').replace(/0+$/, ''); return (amount / 10000n).toString() + (fraction ? '.' + fraction : ''); }
    function moneyValue(text) { if (typeof text !== 'string' || !/^\d{1,16}$/.test(text)) throw new Error('Nhập số tiền bằng đồng, không có dấu phân cách.'); return BigInt(text); }
    function money(text) { return moneyValue(String(text)).toLocaleString('vi-VN') + ' ₫'; }
    function salePriceText(value) {
        const text=String(value??'').trim().replace(',','.');
        if(!/^\d{1,16}(?:\.\d{1,2})?$/.test(text))throw new Error('Đơn giá tối đa 2 số lẻ, không dùng dấu phân cách hàng nghìn.');
        const [whole,fraction='']=text.split('.'),cents=BigInt(whole)*100n+BigInt(fraction.padEnd(2,'0'));
        if(cents<=0n||cents>=1000000000000000000n)throw new Error('Đơn giá phải lớn hơn 0 và trong giới hạn.');
        const tail=(cents%100n).toString().padStart(2,'0').replace(/0+$/,'');
        return (cents/100n).toString()+(tail?'.'+tail:'');
    }
    function saleLineTotal(quantity,price,discount) {
        const [whole,fraction='']=salePriceText(price).split('.');
        const gross=(qty(quantity)*(BigInt(whole)*100n+BigInt(fraction.padEnd(2,'0')))+500000n)/1000000n;
        const net=gross-moneyValue(discount);
        if(net<0n)throw new Error('Giá mới thấp hơn khoản giảm giá đã duyệt.');
        if(net>=10000000000000000n)throw new Error('Thành tiền vượt giới hạn.');
        return net;
    }
    function quantityDisplay(text) { const [whole, fraction] = quantityText(qty(text)).split('.'); return BigInt(whole).toLocaleString('vi-VN') + (fraction ? ',' + fraction : ''); }
    function factor(text) { if (!/^\d+(?:\.\d{1,6})?$/.test(text || '')) throw new Error('Hệ số đơn vị chưa hợp lệ.'); const [w, f = ''] = text.split('.'); const value = BigInt(w) * 1000000n + BigInt(f.padEnd(6,'0')); if (value <= 0n) throw new Error('Hệ số đơn vị phải lớn hơn 0.'); return value; }
    function toBase(text, multiplier) { const product = qty(text) * factor(multiplier); if (product % 1000000n) throw new Error('Lượng quy đổi cần quá 4 số lẻ.'); return quantityText(product / 1000000n); }
    function toSelling(text, multiplier) { const product = qty(text) * 1000000n, divisor = factor(multiplier); if (product % divisor) return null; return quantityText(product / divisor); }
    function stageOf(state) { return ['Created','Picking','AwaitingApproval'].includes(state) ? 'picking' : state === 'ReadyForHandover' ? 'ready' : ['Settled','Cancelled'].includes(state) ? 'completed' : 'delivering'; }
    function parseLookup(raw, origin, initialRevision = null) {
        let key=String(raw||'').trim(),revision=initialRevision;
        if(/^https?:\/\//i.test(key)){const url=new URL(key);if(url.origin!==origin||url.pathname!=='/admin/deliveries')throw new Error('QR không thuộc trang giao hàng của cửa hàng này.');key=url.searchParams.get('key')||'';revision=url.searchParams.get('revision');}
        return {key,revision:/^[1-9]\d{0,8}$/.test(String(revision||''))?String(revision):null};
    }
    function counterLines(lines, values) {
        return lines.map(line => {
            const delivered = qty(values.get(line.lineId)), sent = qty(line.dispatchedBaseQuantityText), accepted = qty(line.acceptedReturnedBaseQuantityText);
            if (delivered > sent - accepted) throw new Error('Lượng khách nhận vượt hàng còn được giao.');
            return { lineId:line.lineId, deliveredBaseQuantityText:quantityText(delivered), actualReturnedBaseQuantityText:quantityText(sent - delivered) };
        });
    }
    function outcomeOf(lines) { const any = lines.some(x => qty(x.deliveredBaseQuantityText) > 0n); return !any ? 'NotDelivered' : lines.every(x => qty(x.actualReturnedBaseQuantityText) === 0n) ? 'Full' : 'Partial'; }
    function intentPath(intent) {
        const c = intent.context;
        if (!c || !positive(c.orderId) || !guid(intent.body?.clientRequestId)) throw new Error('Yêu cầu đang lưu không hợp lệ.');
        if (['claim','complete'].includes(intent.kind)) return `/admin/api/deliveries/${c.orderId}/picking/${intent.kind}`;
        if(intent.kind==='sale-price-change')return `/admin/api/deliveries/${c.orderId}/sale-prices`;
        if (intent.kind === 'dispatch' && Array.isArray(intent.body.orders) && intent.body.orders.length && intent.body.orders.every(x => positive(x.deliveryOrderId))) return '/admin/api/delivery-trips/dispatch';
        if (!positive(c.tripId)) throw new Error('Chuyến trong yêu cầu chưa hợp lệ.');
        if (intent.kind === 'handover') return `/admin/api/delivery-trips/${c.tripId}/handover/${c.orderId}`;
        if (intent.kind === 'counter-issue') return `/admin/api/delivery-trips/${c.tripId}/orders/${c.orderId}/counter-issue`;
        if (intent.kind === 'depart') return `/admin/api/delivery-trips/${c.tripId}/depart`;
        throw new Error('Thao tác đang lưu không hợp lệ.');
    }
    function checkAck(intent, ack) {
        if (!ack || ack.clientRequestId !== intent.body.clientRequestId || ack.operation !== intent.kind ||
            !positive(ack.appliedRevision ?? ack.appliedOrderRevision)) throw new Error('Phản hồi chưa khớp yêu cầu; giữ nguyên để thử lại.');
        if (['claim','complete','sale-price-change'].includes(intent.kind) && ack.deliveryOrderId !== intent.context.orderId) throw new Error('Phản hồi không thuộc đơn đã gửi.');
        if (intent.kind === 'dispatch') {
            const expected = intent.body.orders.map(x => x.deliveryOrderId).sort((a,b)=>a-b);
            if (!positive(ack.tripId) || !Array.isArray(ack.orders) || JSON.stringify(ack.orders.map(x => x.deliveryOrderId).sort((a,b)=>a-b)) !== JSON.stringify(expected) || ack.orders.some(x => x.state !== 'Delivering')) throw new Error('Chưa xác nhận đủ đơn đã bàn giao.');
        } else if (!['claim','complete','sale-price-change'].includes(intent.kind) && ack.tripId !== intent.context.tripId) throw new Error('Phản hồi không thuộc chuyến đã gửi.');
        if (intent.kind === 'handover' && (ack.deliveryOrderId !== intent.context.orderId || ack.settlement?.deliveryOrderId !== intent.context.orderId || !['Settled','Cancelled'].includes(ack.state))) throw new Error('Chưa xác nhận đơn đã chốt hàng và tiền.');
        if (intent.kind === 'counter-issue' && !ack.orders?.some(x => x.deliveryOrderId === intent.context.orderId && ['AwaitingReconciliation','CancellationPending'].includes(x.state))) throw new Error('Chưa xác nhận đúng đơn cần xử lý.');
        return ack;
    }
    const api = { qty, quantityText, moneyValue, salePriceText, saleLineTotal, toBase, toSelling, counterLines, outcomeOf, stageOf, parseLookup, intentPath, checkAck };
    if (typeof module !== 'undefined' && module.exports) { module.exports = api; return; }
    root.DeliveryWorkspaceUi = api;
    const desk = document.getElementById('deliveryDesk'); if (!desk) return;
    const $ = id => document.getElementById(id), storeId = Number(desk.dataset.store), actorId = desk.dataset.actor;
    const scope = JSON.stringify([location.origin, storeId, actorId]), intentKey = 'gao.delivery.workspace.v1:' + scope;
    const token = desk.querySelector('[name="__RequestVerificationToken"]').value;
    let stage = 'picking', page = 1, pages = 1, search = '', rows = [], selected = null, picking = null, context = null, handover = null;
    let listEpoch = 0, detailEpoch = 0, previewEpoch = 0, busy = false, needsFresh = false, canDispatch = false, draft = null, dispatchOrders = [];
    const selectedOrders = new Map(); let controls = [];
    const node = (tag, text, cls) => { const element = document.createElement(tag); if (text != null) element.textContent = text; if (cls) element.className = cls; return element; };
    function button(text, action, cls = 'dd-button') { const b = node('button', text, cls); b.type = 'button'; b.addEventListener('click', action); return b; }
    function link(text, href, cls = 'dd-button') { const a = node('a', text, cls); a.href = href; return a; }
    function message(text, error = false) { $('ddMessage').hidden = !text; $('ddMessage').classList.toggle('is-error', error); $('ddMessage').textContent = text; }
    function date(value) { if (!value) return '—'; return new Date(/(?:Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : value + 'Z').toLocaleString('vi-VN',{timeZone:'Asia/Ho_Chi_Minh',day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit'}); }
    async function request(url, options = {}) {
        const response = await fetch(url, {credentials:'same-origin',cache:'no-store',redirect:'error',...options,headers:{Accept:'application/json',...options.headers}});
        if (!response.headers.get('content-type')?.includes('application/json')) throw new Error('Phiên đăng nhập đã thay đổi. Tải lại trang để tiếp tục.');
        const data = await response.json();
        if (!response.ok) { const error = new Error(data.message || 'Chưa thực hiện được thao tác.'); error.status = response.status; error.code = data.code; throw error; }
        return data;
    }
    function pending() {
        const raw = localStorage.getItem(intentKey); if (!raw) return null;
        const intent = JSON.parse(raw);
        if (intent.schema !== 1 || intent.scope !== scope || typeof intent.uncertain !== 'boolean') throw new Error('Yêu cầu đang lưu không khớp phiên cửa hàng.');
        intentPath(intent); return intent;
    }
    function pendingPanel() {
        const box = $('ddPending'); box.replaceChildren();
        try {
            const intent = pending(); box.hidden = !intent; if (!intent) return;
            box.append(node('strong','Còn một thao tác chưa xác nhận kết quả'),node('p','Thử lại đúng yêu cầu cũ để xác nhận. Hệ thống giữ mã yêu cầu để tránh ghi trùng hàng hoặc tiền.'),button('Thử lại yêu cầu cũ',()=>send(intent),'dd-button dd-button--primary'));
        } catch (e) { box.hidden = false; box.append(node('strong',e.message),node('p','Cần kiểm tra phiên đăng nhập và dữ liệu đang lưu trước khi gửi thao tác khác.')); }
    }
    function lock(on) {
        busy = on; desk.setAttribute('aria-busy',String(on));
        if (on) { controls = [...desk.querySelectorAll('button,input,select,textarea')].map(el=>[el,el.disabled]); controls.forEach(([el])=>el.disabled=true); }
        else { controls.forEach(([el,disabled])=>{if(el.isConnected)el.disabled=disabled;}); controls=[]; }
    }
    async function mutate(kind, body, operationContext) {
        if (busy) return;
        try {
            if (needsFresh) throw new Error('Đơn đã thay đổi. Bấm làm mới và kiểm tra bản hiện tại trước khi gửi.');
            if (pending()) throw new Error('Xác nhận yêu cầu đang lưu trước khi thao tác tiếp.');
            const intent = {schema:1,scope,kind,body:{...body,clientRequestId:crypto.randomUUID()},context:operationContext,uncertain:false};
            intentPath(intent); const raw = JSON.stringify(intent); localStorage.setItem(intentKey,raw);
            if (localStorage.getItem(intentKey)!==raw) throw new Error('Chưa lưu được yêu cầu an toàn.');
            await send(intent);
        } catch(e) { message(e.message,true); pendingPanel(); }
    }
    async function send(intent) {
        if (busy) return;
        let sent; const controller = new AbortController(); let timer;
        try {
            const current = pending();
            if (!current || JSON.stringify(current.body)!==JSON.stringify(intent.body) || current.kind!==intent.kind) throw new Error('Yêu cầu đã thay đổi ở tab khác.');
            sent = {...current,uncertain:true}; const raw = JSON.stringify(sent); localStorage.setItem(intentKey,raw);
            if(localStorage.getItem(intentKey)!==raw)throw new Error('Không giữ được yêu cầu an toàn.');
            lock(true); message('Đang xác nhận thao tác…');
            timer = setTimeout(()=>controller.abort(),22000);
            const ack = checkAck(intent,await request(intentPath(intent),{method:'POST',signal:controller.signal,headers:{'Content-Type':'application/json','RequestVerificationToken':token},body:JSON.stringify(intent.body)}));
            const saved = pending(); if (!saved || saved.body.clientRequestId!==intent.body.clientRequestId) throw new Error('Phản hồi đã về; yêu cầu tại tab khác cần được kiểm tra.');
            localStorage.removeItem(intentKey);
            if (intent.kind==='counter-issue' && draft) { draft.version=ack.orders.find(x=>x.deliveryOrderId===intent.context.orderId)?.version; saveDraft(); }
            else if(intent.kind==='sale-price-change'&&draft){draft.version=ack.appliedVersion;delete draft.prices;if(draft.counter){draft.counter.cash='';draft.counter.transfer='';}saveDraft();}
            else sessionStorage.removeItem(draftKey(intent.context.orderId));
            if ($('ddDispatch').open) $('ddDispatch').close();
            selectedOrders.clear(); needsFresh=false; draft=null;
            message(intent.kind==='sale-price-change'?'Đã cập nhật giá bán. Kiểm tra số tiền mới trước khi chốt.':intent.kind==='handover' ? 'Đã hoàn tất đơn theo hàng thực giao và tiền đã xác nhận.' : intent.kind==='counter-issue' ? 'Đã lưu đơn vào Cần xử lý. Hàng và tiền chưa được chốt.' : intent.kind==='dispatch' ? 'Đã bàn giao hàng và ghi nhận xuất phát.' : intent.kind==='claim' ? 'Bạn đã nhận phụ trách soạn đơn này.' : 'Đã xác nhận kết quả soạn.');
            lock(false);
            await openOrder(intent.context.orderId,true);
            if (picking) stage=stageOf(picking.delivery.state);
            if (intent.kind==='counter-issue') stage='attention';
            page=1; await loadList();
        } catch(e) {
            if(sent && e.status>=400 && e.status<500 && !intent.uncertain) {
                const saved = pending(); if(saved?.body.clientRequestId===intent.body.clientRequestId)localStorage.removeItem(intentKey);
                if(e.status===409)needsFresh=true;
            }
            message(e.name==='AbortError'?'Chưa nhận được kết quả. Thử lại yêu cầu cũ để xác nhận an toàn.':e.message,true);
            if($('ddDispatch').open)$('ddDispatchMessage').textContent=e.message;
        } finally { clearTimeout(timer); if(busy)lock(false); pendingPanel(); }
    }
    function draftKey(id) { return 'gao.delivery.desk-draft.v1:' + scope + ':' + id; }
    function saveDraft() { if(draft && selected) { try { sessionStorage.setItem(draftKey(selected),JSON.stringify(draft)); } catch(_) { message('Chưa giữ được bản nhập trên thiết bị. Hoàn tất thao tác trước khi rời trang.',true); } } }
    function restoreDraft(version) {
        try { const value=JSON.parse(sessionStorage.getItem(draftKey(selected))||'null'); if(value?.version===version)return value; if(value)message('Đơn có bản mới. Kiểm tra lượng và tiền hiện tại trước khi nhập tiếp.'); } catch(_) { /* A local draft never overrides authoritative server state. */ }
        return {version};
    }
    function badge(state, attention) { const cls=attention?'attention':state==='ReadyForHandover'?'ready':state==='Settled'?'done':''; return node('span',states[state]||state,'dd-badge'+(cls?' dd-badge--'+cls:'')); }
    async function loadList() {
        const epoch=++listEpoch; $('ddList').setAttribute('aria-busy','true');
        try {
            const data=await request('/admin/api/deliveries/workspace?'+new URLSearchParams({stage,page,search})); if(epoch!==listEpoch)return;
            if(data.storeId!==storeId)throw new Error('Cửa hàng trong phiên đã thay đổi. Tải lại trang để tiếp tục.');
            rows=data.items; page=data.page; pages=data.pages; canDispatch=data.canDispatch===true;
            if(!Array.isArray(rows)||rows.some(x=>!positive(x.id)||!states[x.state]))throw new Error('Danh sách đơn chưa hợp lệ.');
            for(const key of Object.keys(stages)) { const count=data.counts[key]; if(!Number.isSafeInteger(count)||count<0)throw new Error('Số lượng đơn chưa hợp lệ.'); desk.querySelector(`[data-dd-count="${key}"]`).textContent=count.toLocaleString('vi-VN'); }
            desk.querySelectorAll('[data-dd-stage]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.ddStage===stage)));
            $('ddListTitle').textContent=stages[stage]; $('ddListCount').textContent=data.total.toLocaleString('vi-VN')+' đơn'; $('ddPage').textContent=`Trang ${page} / ${pages}`; $('ddPrev').disabled=page<=1; $('ddNext').disabled=page>=pages;
            renderList();
        } catch(e) { if(epoch===listEpoch){message(e.message,true);$('ddListCount').textContent='Chưa tải được danh sách';} }
        finally { if(epoch===listEpoch)$('ddList').setAttribute('aria-busy','false'); }
    }
    function renderList() {
        const list=$('ddList'); list.replaceChildren();
        for(const row of rows) {
            const wrap=node('div',null,'dd-card-wrap');
            if(stage==='ready' && canDispatch && !row.tripId) {
                const label=node('label',null,'dd-select'),check=node('input'); check.type='checkbox'; check.checked=selectedOrders.has(row.id); check.setAttribute('aria-label','Chọn đơn '+row.code+' để giao cùng người');
                check.addEventListener('change',()=>{if(check.checked)selectedOrders.set(row.id,row);else selectedOrders.delete(row.id);selectionCount();}); label.append(check);wrap.append(label);
            }
            const b=button('',()=>openOrder(row.id),'dd-card'); b.setAttribute('aria-current',String(selected===row.id));
            const top=node('div',null,'dd-card-top');top.append(node('strong',row.recipientName),badge(row.state,row.needsAttention));
            b.append(top,node('p',row.recipientAddress));
            const bottom=node('div',null,'dd-card-bottom');bottom.append(node('span',row.code+' · '+date(row.createdAtUtc)),node('b',money(row.totalText)));b.append(bottom);
            if(row.courierName)b.append(node('p','Người giao: '+row.courierName)); else if(row.pickerName)b.append(node('p','Người soạn: '+row.pickerName));
            wrap.append(b);list.append(wrap);
        }
        if(!rows.length){const box=node('div',null,'dd-empty');box.append(node('i',null,'bx bx-check-circle'),node('h2','Chưa có đơn ở bước này'),node('p',search?'Thử từ khóa khác hoặc chuyển bước.':'Các đơn mới sẽ xuất hiện khi được chuyển sang bước này.'));list.append(box);}
        selectionCount();
    }
    function selectionCount() { $('ddBulkDispatch').hidden=stage!=='ready'||!canDispatch||selectedOrders.size===0; $('ddSelectedCount').textContent=selectedOrders.size; }
    async function openOrder(id, force=false, printedRevision=null) {
        if(busy||!positive(id))return;
        const epoch=++detailEpoch; ++previewEpoch; selected=id; picking=null; handover=null; context=null; needsFresh=false;
        desk.setAttribute('data-detail-open','true'); $('ddDetail').replaceChildren(node('p','Đang tải bản đơn hiện tại…','dd-help')); renderList();
        try {
            const detail=await request(`/admin/api/deliveries/${id}/picking`);if(epoch!==detailEpoch)return;
            const metadata=await request(`/admin/api/deliveries/${id}/workspace`);if(epoch!==detailEpoch)return;
            if(detail.delivery.id!==id||metadata.deliveryOrderId!==id||!states[detail.delivery.state]||detail.inventory && detail.inventory.storeId!==storeId)throw new Error('Bản đơn không khớp cửa hàng đang làm việc.');
            picking=detail;context=metadata;draft=restoreDraft(detail.delivery.version);
            if(metadata.tripId) { const data=await request(`/admin/api/delivery-trips/${metadata.tripId}/handover`);if(epoch!==detailEpoch)return;
                root.DeliveryTripUi.validateDetail(data.trip,storeId);if(data.trip.tripId!==metadata.tripId||!data.trip.orders.some(x=>x.deliveryOrderId===id&&x.isActive))throw new Error('Đơn không thuộc chuyến đang đọc.');handover=data;
            }
            if(printedRevision && Number(printedRevision)<detail.delivery.revision)message('Bill đang quét là bản cũ. Đang hiển thị lượng hàng mới nhất của đơn.');
            renderDetail();
        } catch(e) {if(epoch===detailEpoch){message(e.message,true);const box=node('div',null,'dd-empty');box.append(node('h2','Chưa mở được đơn'),node('p',e.message),button('Thử tải lại',()=>openOrder(id,true)));$('ddDetail').replaceChildren(box);}}
    }
    function field(label,value='',numeric=false) {const wrapper=node('label',null,'dd-field');wrapper.append(node('span',label));const input=node('input');input.type='text';input.value=value;input.maxLength=numeric?20:500;if(numeric)input.inputMode='decimal';wrapper.append(input);return {wrapper,input};}
    function confirmed(label) {const wrapper=node('label',null,'dd-confirm'),input=node('input');input.type='checkbox';wrapper.append(input,node('span',label));return {wrapper,input};}
    function heading(panel,title,extra) {const h=node('div',null,'dd-section-heading');h.append(node('h3',title));if(extra)h.append(extra);panel.append(h);}
    function renderDetail() {
        if(!picking)return;
        const panel=$('ddDetail'),d=picking.delivery;panel.replaceChildren();
        panel.append(button('← Danh sách đơn',()=>{desk.removeAttribute('data-detail-open');$('ddListTitle').scrollIntoView({block:'start'});},'dd-button dd-back'));
        const head=node('header',null,'dd-detail-heading'),identity=node('div');identity.append(node('h2',d.recipientName),node('p',d.code+' · '+date(d.createdAtUtc)));head.append(identity,badge(d.state,Boolean(context.issueReason)||['AwaitingApproval','CancellationPending'].includes(d.state)));panel.append(head);
        const recipient=node('div',null,'dd-recipient'),address=node('div');address.append(node('p',d.recipientAddress),node('small',d.recipientPhone+(d.note?' · '+d.note:'')));recipient.append(address);const bill=link('Xem / in bill',`/admin/deliveries/${d.id}/bill`);bill.target='_blank';bill.rel='noopener';recipient.append(bill);panel.append(recipient);
        if(context.issueReason)panel.append(node('p','Cần xử lý: '+context.issueReason,'dd-difference'));
        renderSalePrices(panel);
        if(['Created','Picking'].includes(d.state))renderPicking(panel);
        else if(d.state==='AwaitingApproval')renderChanges(panel);
        else if(d.state==='ReadyForHandover')renderReady(panel);
        else if(stageOf(d.state)==='delivering')renderCounter(panel);
        else renderClosed(panel);
        const more=node('details');more.append(node('summary','Lịch sử và xử lý khác'));const body=node('div');body.append(node('p','Bill gốc, lượng mang đi và kết quả bàn giao được giữ để đối chiếu.','dd-help'),link('Xử lý thiếu / thay hàng',`/admin/deliveries/picking?key=${encodeURIComponent(d.code)}`,'dd-button dd-button--quiet'));
        if(context.tripId)body.append(link('Chi tiết chuyến',`/admin/deliveries/trips?trip=${context.tripId}`,'dd-button dd-button--quiet'));
        more.append(body);let loaded=false;more.addEventListener('toggle',async()=>{if(!more.open||loaded)return;loaded=true;try{const data=await request(`/admin/api/deliveries/${d.id}/history`);if(selected!==d.id)return;const list=node('ol',null,'dd-timeline');for(const row of data.slice(-12))list.append(node('li',date(row.recordedAtUtc)+' · '+historyLabel(row.action)));body.append(list);}catch(e){body.append(node('p',e.message,'dd-help'));}});panel.append(more);
    }
    function historyLabel(action) {return ({'sale-price-change':'Điều chỉnh giá bán','foundation-create':'Tạo đơn','picking-complete':'Hoàn tất soạn','picking-claim':'Nhận soạn','picking-report':'Lưu lượng soạn','picking-submit':'Chốt soạn','picking-approve':'Duyệt thay đổi','trip-handover':'Bàn giao người giao','trip-depart':'Xuất phát','trip-counter-issue':'Ghi nhận cần xử lý','handover-reconciled':'Đối chiếu hàng và tiền','counter-handover':'Hoàn tất tại quầy'})[action]||'Cập nhật đơn';}
    function renderSalePrices(panel) {
        const prices=picking.salePrices;if(!prices)return;
        if(prices.changedAtUtc)panel.append(node('p','Giá bán đã được '+prices.changedBy+' cập nhật lúc '+date(prices.changedAtUtc)+'. Lý do: '+prices.reason,'dd-help'));
        if(!prices.canEdit)return;
        const section=node('details',null,'dd-price-editor');section.append(node('summary','Sửa giá bán'));
        const body=node('div',null,'dd-price-body');body.append(node('p','Nhập giá theo đơn vị bán, không dùng dấu phân cách hàng nghìn. Các khoản giảm đã duyệt được giữ nguyên; bill gốc và lượng đã xuất không đổi.','dd-help'));
        const state=draft.prices||(draft.prices={values:{},reason:''}),fields=new Map();
        for(const line of prices.lines){
            const row=node('div',null,'dd-price-row'),identity=node('div');identity.append(node('strong',line.itemName),node('small',quantityDisplay(line.quantityText)+' '+line.unitName+' · Giảm đã duyệt: '+money(line.discountText)));
            const price=field('Đơn giá mới ('+line.unitName+')',state.values[line.lineId]??line.unitPriceText.replace('.',','),true);
            price.input.disabled=!line.canEdit;price.input.maxLength=20;price.input.placeholder='Ví dụ: 17500 hoặc 17500,50';fields.set(line.lineId,price.input);row.append(identity,price.wrapper);body.append(row);
        }
        const reason=field('Lý do sửa giá',state.reason);reason.input.maxLength=1000;reason.input.placeholder='Ví dụ: giá đã thống nhất lại với khách';body.append(reason.wrapper);
        const impact=node('p',null,'dd-price-impact'),error=node('p',null,'dd-difference');error.setAttribute('role','status');error.hidden=true;body.append(impact,error);
        const save=button('Lưu giá bán',async()=>{
            try{const lines=prices.lines.filter(x=>x.canEdit&&salePriceText(fields.get(x.lineId).value)!==salePriceText(x.unitPriceText)).map(x=>({lineId:x.lineId,unitPriceText:salePriceText(fields.get(x.lineId).value)}));
                await mutate('sale-price-change',{expectedVersion:picking.delivery.version,lines,reason:reason.input.value.trim()},{orderId:selected});
            }catch(e){error.textContent=e.message;error.hidden=false;}
        },'dd-button dd-button--primary');body.append(save);
        function update(){
            state.reason=reason.input.value;for(const [id,input]of fields)state.values[id]=input.value;saveDraft();
            try{let total=0n,changed=false;for(const line of prices.lines){if(!line.canEdit){total+=moneyValue(line.netText);continue;}const value=salePriceText(fields.get(line.lineId).value),different=value!==salePriceText(line.unitPriceText);changed ||= different;total+=different?saleLineTotal(line.quantityText,value,line.discountText):moneyValue(line.netText);}
                if(total<=0n||total>=10000000000000000n)throw new Error('Tổng giá bán phải dương và trong giới hạn.');
                impact.textContent='Tổng trước: '+money(prices.totalText)+' → Sau sửa: '+money(total.toString());error.textContent='';error.hidden=true;save.disabled=!changed||!reason.input.value.trim()||busy;
            }catch(e){error.textContent=e.message;error.hidden=false;save.disabled=true;}
        }
        for(const input of fields.values())input.addEventListener('input',update);reason.input.addEventListener('input',update);update();section.open=Boolean(state.reason);section.append(body);panel.append(section);
    }
    function pickingLines() {
        return picking.lines.filter(x=>x.isActive||x.sourceOrderLineId!=null);
    }
    function renderPicking(panel) {
        const allowed=picking.capabilities.canClaim||picking.capabilities.canReport,lines=pickingLines(),fields=new Map();
        if(picking.delivery.state==='Created'){
            heading(panel,'1. Nhận đơn để soạn');const list=node('div',null,'dd-lines');
            for(const line of lines){const row=node('div',null,'dd-line'),head=node('div',null,'dd-line-header');head.append(node('strong',line.itemName),node('span',quantityDisplay(line.plannedQuantityText)+' '+line.unitName));row.append(head);list.append(row);}panel.append(list);
            panel.append(node('p','Nhận phụ trách trước khi lấy hàng để các nhân viên khác biết đơn đã có người soạn.','dd-help'));
            if(picking.capabilities.canClaim)panel.append(button('Bắt đầu soạn',()=>mutate('claim',{expectedVersion:picking.delivery.version},{orderId:selected}),'dd-button dd-button--primary'));
            else panel.append(node('p','Tài khoản chưa có quyền nhận soạn đơn.','dd-help'));
            return;
        }
        const fill=button('Đủ theo bill',()=>{for(const line of lines){const f=fields.get(line.lineId);f.quantity.input.value=line.plannedQuantityText;f.reason.input.value='';f.reason.wrapper.hidden=qty(line.plannedQuantityText)>0n;draft.pick[line.lineId]={quantity:line.plannedQuantityText,reason:''};}saveDraft();},'dd-button');fill.disabled=!allowed;
        heading(panel,'1. Kiểm hàng thực có',fill);panel.append(node('p','Kiểm từng mặt hàng. Nếu đủ, bấm “Đủ theo bill”; hàng thiếu nhập lượng thực có và lý do.','dd-help'));
        draft.pick=draft.pick||{};const list=node('div',null,'dd-lines');
        for(const line of lines){const row=node('article',null,'dd-line'),head=node('div',null,'dd-line-header');head.append(node('strong',line.itemName),node('span','Cần '+quantityDisplay(line.plannedQuantityText)+' '+line.unitName));row.append(head);
            const saved=draft.pick[line.lineId],quantity=field('Thực có ('+line.unitName+')',saved?.quantity??line.reportedQuantityText??'',true),reason=field('Lý do nếu thiếu',saved?.reason??line.shortageReason??'');reason.input.placeholder='Ví dụ: còn 8, thiếu 2';reason.wrapper.classList.add('dd-shortage');quantity.input.disabled=reason.input.disabled=!allowed;
            const update=()=>{draft.pick[line.lineId]={quantity:quantity.input.value,reason:reason.input.value};try{reason.wrapper.hidden=qty(quantity.input.value)>=qty(line.plannedQuantityText)&&qty(quantity.input.value)>0n;}catch(_){reason.wrapper.hidden=!reason.input.value;}saveDraft();};quantity.input.addEventListener('input',update);reason.input.addEventListener('input',update);update();row.append(quantity.wrapper,reason.wrapper);list.append(row);fields.set(line.lineId,{quantity,reason});}
        panel.append(list);
        if(!allowed){panel.append(node('p',picking.pickerName?'Người đang soạn: '+picking.pickerName+'. Cần người phụ trách hoặc quản lý xử lý tiếp.':'Tài khoản chưa có quyền xác nhận soạn hàng.','dd-help'));return;}
        const fact=confirmed('Tôi đã kiểm đủ các dòng và xác nhận lượng hàng thực có ở trên.');panel.append(fact.wrapper);
        const complete=button('Soạn xong',()=>{try{if(!fact.input.checked)throw new Error('Xác nhận đã kiểm hàng trước khi chốt soạn.');const reports=lines.map(line=>{const f=fields.get(line.lineId),amount=qty(f.quantity.input.value),planned=qty(line.plannedQuantityText),reason=f.reason.input.value.trim();if(amount>planned)throw new Error(line.itemName+': lượng soạn vượt bill.');if((amount<planned||amount===0n)&&!reason)throw new Error(line.itemName+': cần ghi lý do thiếu hàng.');return {lineId:line.lineId,pickedQuantityText:quantityText(amount),shortageReason:reason||null};});mutate('complete',{expectedVersion:picking.delivery.version,lines:reports},{orderId:selected});}catch(e){message(e.message,true);}},'dd-button dd-button--success');
        const actions=node('div',null,'dd-actions');actions.append(node('small','Đủ theo bill chuyển thẳng sang chờ giao. Thiếu hoặc đổi hàng sẽ chờ duyệt.'),complete);panel.append(actions);
    }
    function renderChanges(panel) {
        heading(panel,'Kiểm tra thay đổi trước khi giao');
        panel.append(node('p','Đơn có hàng thiếu hoặc phương án thay thế. Quản lý kiểm tra lượng thực có và xác nhận với khách trước khi đưa sang chờ giao.','dd-difference'));
        const list=node('div',null,'dd-lines');
        for(const line of pickingLines()){const row=node('div',null,'dd-line');row.append(node('strong',line.itemName),node('p','Cần '+quantityDisplay(line.plannedQuantityText)+' · Thực có '+(line.reportedQuantityText==null?'chưa ghi nhận':quantityDisplay(line.reportedQuantityText))+' '+line.unitName,'dd-help'));if(line.shortageReason)row.append(node('p',line.shortageReason,'dd-help'));list.append(row);}panel.append(list);
        if(picking.capabilities.canApprove||picking.capabilities.canPlan)panel.append(link('Duyệt / xử lý thay đổi',`/admin/deliveries/picking?key=${encodeURIComponent(picking.delivery.code)}`,'dd-button dd-button--primary'));
        else panel.append(node('p','Đang chờ người có quyền duyệt thay đổi.','dd-help'));
    }
    function approvedSummary(panel) {
        const list=node('div',null,'dd-lines');
        for(const line of picking.lines.filter(x=>x.isActive&&x.approvedQuantityText!=null&&qty(x.approvedQuantityText)>0n)){const row=node('div',null,'dd-line'),head=node('div',null,'dd-line-header');head.append(node('strong',line.itemName),node('span',quantityDisplay(line.approvedQuantityText)+' '+line.unitName));row.append(head);list.append(row);}panel.append(list);
    }
    function renderReady(panel) {
        heading(panel,'2. Bàn giao cho người giao');approvedSummary(panel);
        panel.append(node('div','Tiền dự kiến: '+money(picking.approvedTotalText||picking.quotedTotalText),'dd-total'));
        if(context.tripId){panel.append(node('p','Đơn đã được xếp chuyến. Mở chuyến hiện tại để tiếp tục bàn giao đúng các đơn đã chọn.','dd-help'),link('Tiếp tục chuyến đã xếp',`/admin/deliveries/trips?trip=${context.tripId}`,'dd-button dd-button--primary'));return;}
        if(!canDispatch){panel.append(node('p','Cần quyền bàn giao và ghi nhận xuất phát để giao hàng.','dd-help'));return;}
        const actions=node('div',null,'dd-actions');actions.append(node('small','Một lần xác nhận cho việc nhận hàng và bắt đầu giao.'),button('Giao hàng',()=>openDispatch([{id:selected,code:picking.delivery.code,version:picking.delivery.version}]),'dd-button dd-button--primary'));panel.append(actions);
    }
    async function openDispatch(orders) {
        if(busy)return;
        try {
            if(pending())throw new Error('Xác nhận yêu cầu đang lưu trước khi giao hàng.');
            if(!orders.length||orders.length>50)throw new Error('Chọn từ 1 đến 50 đơn để giao cùng người.');
            const options=await request('/admin/api/delivery-trips/options');
            if(options.storeId!==storeId||!Array.isArray(options.couriers)||!Array.isArray(options.orders))throw new Error('Danh sách giao hàng không khớp cửa hàng.');
            dispatchOrders=orders.map(order=>{const row=options.orders.find(x=>x.deliveryOrderId===order.id);if(!row||row.version!==order.version)throw new Error('Đơn '+order.code+' đã thay đổi hoặc đang thuộc chuyến khác. Làm mới và kiểm tra lại lượng hàng.');return row;});
            const list=$('ddDispatchOrders');list.replaceChildren();for(const order of dispatchOrders){const body=node('div');body.append(node('strong',order.recipientName+' · '+order.code),node('p',order.recipientAddress));for(const line of order.lines)body.append(node('p',line.itemName+' · '+quantityDisplay(line.approvedBaseQuantityText)+' '+line.baseUnitName,'dd-help'));list.append(body);}
            const select=$('ddCourier');select.replaceChildren(node('option','Chọn người giao…'));select.firstChild.value='';
            for(const courier of options.couriers){const option=node('option',courier.name);option.value=String(courier.userId);select.append(option);}
            if(!options.couriers.length)throw new Error('Chưa có người giao đang hoạt động được phép nhận chuyến.');
            $('ddDispatchNote').value='';$('ddDispatchConfirmed').checked=false;$('ddDispatchMessage').textContent='';$('ddDispatch').showModal();select.focus();
        }catch(e){message(e.message,true);}
    }
    function counterOrder() {return handover?.trip.orders.find(x=>x.deliveryOrderId===selected&&x.isActive);}
    function counterInfo() {return handover?.orders.find(x=>x.deliveryOrderId===selected);}
    function renderCounter(panel) {
        const order=counterOrder(),details=counterInfo();
        if(!order||!details){panel.append(node('p','Chưa có thông tin chuyến cho đơn này. Tải lại để kiểm tra.','dd-help'));return;}
        if(handover.trip.state==='HandedOver'){
            heading(panel,'Người giao đã nhận hàng');approvedSummary(panel);panel.append(node('p','Người giao: '+handover.trip.courierName+'. Chuyến đã bàn giao trước đó nhưng chưa ghi nhận xuất phát.','dd-help'));
            if(handover.trip.capabilities.canDepart)panel.append(button('Xác nhận đã xuất phát',()=>mutate('depart',{expectedTripVersion:handover.trip.version,orders:handover.trip.orders.filter(x=>x.isActive).map(x=>({deliveryOrderId:x.deliveryOrderId,expectedVersion:x.version}))},{orderId:selected,tripId:handover.trip.tripId}),'dd-button dd-button--primary'));
            return;
        }
        heading(panel,'3. Nhận bàn giao tại quầy');
        panel.append(node('p','Người giao: '+handover.trip.courierName+' · Xuất phát '+date(handover.trip.departedAtUtc),'dd-help'));
        if(!handover.capabilities.canFinalize){approvedSummary(panel);panel.append(node('p','Đơn đang chờ người có quyền chốt hàng và tiền tại quầy nhận bàn giao.','dd-help'));return;}
        if(!draft.counterOpen){panel.append(node('p','Khi người giao về, kiểm hàng mang về và tiền thực bàn giao rồi ghi nhận một lần tại đây.','dd-help'),button('Nhận bàn giao',()=>{draft.counterOpen=true;saveDraft();renderDetail();},'dd-button dd-button--primary'));return;}
        const caps=handover.capabilities,counter=handover.counter,lines=order.lines.filter(x=>qty(x.dispatchedBaseQuantityText)>0n);
        const available=[['Cash','Tiền mặt',caps.canReconcileCash],['BankTransfer','Chuyển khoản',caps.canConfirmBankTransfer],['CustomerCredit','Công nợ',caps.canCredit&&details.customerCreditEligible]].filter(x=>x[2]);
        const d=draft.counter||(draft.counter={quantities:{},method:available[0]?.[0]||'',reportedCash:'',cash:'',transfer:'',bankId:null,reference:'',reason:'',creditNote:'',dueDate:''});
        if(!available.some(x=>x[0]===d.method))d.method=available[0]?.[0]||'';
        const values=new Map(),fields=new Map();let preview=null,previewKey='',timer;
        const readLines=()=>counterLines(lines,values);
        const quick=node('div',null,'dd-methods');
        const setAll=(full)=>{physical.input.checked=false;for(const line of lines){const delivered=full?qty(line.dispatchedBaseQuantityText)-qty(line.acceptedReturnedBaseQuantityText):0n;values.set(line.lineId,quantityText(delivered));d.quantities[line.lineId]=quantityText(delivered);const f=fields.get(line.lineId);f.input.value=toSelling(quantityText(delivered),f.multiplier);f.returnValue.textContent=quantityDisplay(quantityText(qty(line.dispatchedBaseQuantityText)-delivered))+' '+line.baseUnitName;}saveDraft();queuePreview(true);};
        quick.append(button('Giao đủ',()=>setAll(true)),button('Không giao được',()=>setAll(false)));panel.append(quick);
        const list=node('div',null,'dd-lines');
        for(const line of lines){const quote=picking.lines.find(x=>x.lineId===line.lineId);let multiplier=quote?.baseMultiplierText||'1',unit=quote?.unitName||line.baseUnitName;
            if(toSelling(line.dispatchedBaseQuantityText,multiplier)==null||toSelling(line.acceptedReturnedBaseQuantityText,multiplier)==null||d.quantities[line.lineId]!=null&&toSelling(d.quantities[line.lineId],multiplier)==null){multiplier='1';unit=line.baseUnitName;}
            const saved=d.quantities[line.lineId]??order.outcome?.lines.find(x=>x.lineId===line.lineId)?.deliveredBaseQuantityText;
            if(saved!=null){values.set(line.lineId,saved);d.quantities[line.lineId]=saved;}
            const row=node('article',null,'dd-line'),head=node('div',null,'dd-line-header');head.append(node('strong',quote?.itemName||line.itemName),node('span',unit));row.append(head);
            const grid=node('div',null,'dd-line-inputs'),sent=node('div',null,'dd-field');sent.append(node('span','Mang đi'),node('span',quantityDisplay(toSelling(line.dispatchedBaseQuantityText,multiplier)),'dd-value'));
            const delivered=field('Khách nhận',saved==null?'':toSelling(saved,multiplier),true),returned=node('div',null,'dd-field'),returnValue=node('span',saved==null?'—':quantityDisplay(quantityText(qty(line.dispatchedBaseQuantityText)-qty(saved)))+' '+line.baseUnitName,'dd-value dd-value--return');returned.append(node('span','Nhận về kho'),returnValue);grid.append(sent,delivered.wrapper,returned);row.append(grid);
            const f={input:delivered.input,multiplier,returnValue};fields.set(line.lineId,f);
            delivered.input.addEventListener('input',()=>{physical.input.checked=false;try{const base=toBase(delivered.input.value,multiplier);if(qty(base)>qty(line.dispatchedBaseQuantityText)-qty(line.acceptedReturnedBaseQuantityText))throw new Error('Lượng khách nhận vượt hàng còn được giao.');values.set(line.lineId,base);d.quantities[line.lineId]=base;returnValue.textContent=quantityDisplay(quantityText(qty(line.dispatchedBaseQuantityText)-qty(base)))+' '+line.baseUnitName;}catch(e){values.delete(line.lineId);delete d.quantities[line.lineId];returnValue.textContent='Kiểm tra lượng';}saveDraft();queuePreview();});list.append(row);
        }
        panel.append(list,node('p','Hàng nhận về phải được kiểm đếm thực tế. Đơn chốt theo lượng khách nhận; phần mang về được nhận lại kho nguồn.','dd-help'));
        const total=node('div',null,'dd-total'),totalValue=node('strong','—');total.append(node('span','Tiền phải thu theo thực giao'),totalValue);panel.append(total);
        const reason=field('Lý do nếu giao thiếu / không giao được',d.reason);reason.input.placeholder='Ví dụ: khách chỉ nhận 8, mang về 2';reason.input.addEventListener('input',()=>{d.reason=reason.input.value;saveDraft();validate();});panel.append(reason.wrapper);
        heading(panel,'Tiền thực bàn giao');const methods=node('div',null,'dd-methods');
        for(const [key,label]of available){const b=button(label,()=>{d.method=key;saveDraft();renderDetail();});b.setAttribute('aria-pressed',String(d.method===key));methods.append(b);}panel.append(methods);
        if(!available.length)panel.append(node('p','Cần người có quyền nhận tiền mặt, xác nhận chuyển khoản hoặc ghi công nợ.','dd-difference'));
        const moneyFields=node('div',null,'dd-money-grid');let transferFact=null;
        if(d.method==='Cash'){
            const report=field('Khách đã trả · người giao báo (đồng)',d.reportedCash,true),actual=field('Tiền mặt quầy thực nhận (đồng)',d.cash,true);report.input.inputMode=actual.input.inputMode='numeric';
            report.input.addEventListener('input',()=>{physical.input.checked=false;d.reportedCash=report.input.value;saveDraft();validate();});actual.input.addEventListener('input',()=>{physical.input.checked=false;d.cash=actual.input.value;saveDraft();validate();});moneyFields.append(report.wrapper,actual.wrapper);
            const fullCash=button('Khách trả đủ · quầy nhận đủ',()=>{if(!preview){message('Xác nhận lượng thực giao để tính tiền trước.',true);return;}physical.input.checked=false;report.input.value=actual.input.value=preview.payableText;d.reportedCash=d.cash=preview.payableText;saveDraft();validate();});panel.append(fullCash,node('p','Chỉ bấm sau khi đã kiểm tiền thực nhận.','dd-help'));
        }else if(d.method==='BankTransfer'){
            const amount=field('Số tiền đã vào tài khoản (đồng)',d.transfer,true);amount.input.inputMode='numeric';amount.input.addEventListener('input',()=>{physical.input.checked=false;transferFact.input.checked=false;d.transfer=amount.input.value;saveDraft();validate();});moneyFields.append(amount.wrapper);
            const account=node('label',null,'dd-field'),select=node('select');account.append(node('span','Tài khoản đã nhận tiền'));const empty=node('option','Chọn tài khoản…');empty.value='';select.append(empty);for(const bank of handover.bankAccounts){const option=node('option',bank.name);option.value=String(bank.bankAccountId);select.append(option);}select.value=d.bankId?String(d.bankId):'';select.addEventListener('change',()=>{physical.input.checked=false;transferFact.input.checked=false;d.bankId=Number(select.value)||null;saveDraft();validate();});account.append(select);moneyFields.append(account);
            const reference=field('Tham chiếu chuyển khoản · không bắt buộc',d.reference);reference.input.maxLength=100;reference.input.addEventListener('input',()=>{d.reference=reference.input.value;saveDraft();});moneyFields.append(reference.wrapper);
            transferFact=confirmed('Đã kiểm tra tiền thực vào tài khoản cửa hàng.');transferFact.input.addEventListener('change',validate);
        }else if(d.method==='CustomerCredit'){
            const note=field('Ghi chú công nợ',d.creditNote),due=field('Hẹn thanh toán',d.dueDate);due.input.type='date';note.input.addEventListener('input',()=>{d.creditNote=note.input.value;saveDraft();});due.input.addEventListener('input',()=>{d.dueDate=due.input.value;saveDraft();});moneyFields.append(due.wrapper,note.wrapper);
            panel.append(node('p','Ghi công nợ đúng số tiền hàng thực giao cho khách được phép công nợ.','dd-help'));
        }
        panel.append(moneyFields);if(transferFact)panel.append(transferFact.wrapper);
        const physical=confirmed('Người giao đã về quầy. Tôi đã kiểm đếm hàng nhận lại và đối chiếu tiền / công nợ thực tế.');physical.input.addEventListener('change',validate);panel.append(physical.wrapper);
        const problem=node('p','Nhập lượng khách nhận để tính tiền.','dd-form-message');problem.setAttribute('role','status');panel.append(problem);
        const actions=node('div',null,'dd-actions'),left=node('div');left.append(node('small',counter?.isOpen?'Chốt vào ca hiện tại · quầy '+(counter.terminalName||'#'+counter.terminalId):'Cần mở ca POS tại quầy để hoàn tất.'));
        const issue=button('Lưu Cần xử lý',()=>{const note=reason.input.value.trim();if(!note){message('Nhập lý do cần xử lý trước khi lưu.',true);reason.input.focus();return;}saveDraft();mutate('counter-issue',{expectedTripVersion:handover.trip.version,expectedOrderVersion:order.version,reason:note},{orderId:selected,tripId:handover.trip.tripId});},'dd-button dd-button--quiet');issue.disabled=!caps.canFinalize;left.append(issue);
        const finish=button('Xác nhận & hoàn tất',()=>{try{const result=validate(true);if(!result)return;mutate('handover',result,{orderId:selected,tripId:handover.trip.tripId});}catch(e){message(e.message,true);}},'dd-button dd-button--success');finish.disabled=true;actions.append(left,finish);panel.append(actions);
        function validate(throwError=false){
            try{
                const result=readLines(),key=JSON.stringify(result);
                if(!preview||previewKey!==key)throw new Error('Đang chờ tính tiền theo lượng thực giao.');
                if(!caps.canFinalize||!details.canConfirm)throw new Error('Cần người có quyền chốt và ca POS đang mở của chính mình.');
                if(!counter?.isOpen||counter.storeId!==storeId||counter.ownerUserId!==Number(actorId))throw new Error('Chọn quầy và mở ca POS của bạn trước khi hoàn tất.');
                if(preview.lines.some(x=>qty(x.newReturnBaseQuantityText)>0n)&&!caps.canReceiveReturns)throw new Error('Có hàng mang về: cần quyền nhận hàng trả.');
                const outcome=outcomeOf(result),note=reason.input.value.trim();if(outcome!=='Full'&&!note)throw new Error('Giao thiếu hoặc không giao được cần ghi lý do.');
                const payable=moneyValue(preview.payableText);
                if(!available.some(x=>x[0]===d.method))throw new Error('Chọn phương thức được phép chốt.');
                if(d.method==='Cash'){
                    const report=moneyValue(d.reportedCash),cash=moneyValue(d.cash);
                    if(report!==cash)throw new Error('Tiền người giao báo và tiền quầy nhận đang lệch. Kiểm tra lại hoặc lưu Cần xử lý.');
                    if(cash!==payable)throw new Error('Tiền thực nhận chưa khớp tiền hàng thực giao. Kiểm tra lại hoặc lưu Cần xử lý.');
                }
                if(d.method==='BankTransfer'&&(moneyValue(d.transfer)!==payable||!positive(d.bankId)||!transferFact.input.checked))throw new Error('Xác nhận đúng số tiền đã vào tài khoản được chọn.');
                if(d.method==='CustomerCredit'&&payable===0n)throw new Error('Đơn không giao được chọn tiền mặt 0 đồng để nhận lại hàng.');
                if(!physical.input.checked)throw new Error('Xác nhận đã kiểm hàng và tiền thực tế tại quầy.');
                if(needsFresh)throw new Error('Đơn đã thay đổi. Làm mới bản đơn trước khi xác nhận.');
                problem.textContent='Hàng và tiền đã khớp. Có thể hoàn tất đơn.';problem.className='dd-help';finish.disabled=busy;
                return {expectedTripVersion:handover.trip.version,expectedOrderVersion:order.version,expectedTerminalId:counter.terminalId,expectedShiftId:counter.shiftId,courierUserId:handover.trip.courierUserId,physicalGoodsConfirmed:true,outcome,reason:note||null,customerReceivedAtUtc:null,lines:result,paymentKind:d.method,reportedCashText:d.method==='Cash'?moneyValue(d.reportedCash).toString():'0',actualReceivedCashText:d.method==='Cash'?moneyValue(d.cash).toString():'0',confirmedTransferText:d.method==='BankTransfer'?moneyValue(d.transfer).toString():'0',transferConfirmed:d.method==='BankTransfer'&&transferFact.input.checked,bankAccountId:d.method==='BankTransfer'?d.bankId:null,transferReference:d.method==='BankTransfer'?d.reference.trim()||null:null,creditDueDate:d.method==='CustomerCredit'?d.dueDate||null:null,creditNote:d.method==='CustomerCredit'?d.creditNote.trim()||null:null};
            }catch(e){finish.disabled=true;problem.textContent=e.message;problem.className='dd-form-message';if(throwError)throw e;return null;}
        }
        function queuePreview(immediate=false){
            const epoch=++previewEpoch;
            clearTimeout(timer);preview=null;finish.disabled=true;totalValue.textContent='—';
            let result;try{result=readLines();}catch(e){problem.textContent='Nhập lượng thực giao hợp lệ cho tất cả mặt hàng.';return;}
            const key=JSON.stringify(result),orderId=selected,tripId=handover.trip.tripId,tripVersion=handover.trip.version,orderVersion=order.version;
            problem.textContent='Đang tính tiền thực giao…';
            timer=setTimeout(async()=>{try{
                const data=await request(`/admin/api/delivery-trips/${tripId}/handover/${orderId}/preview`,{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':token},body:JSON.stringify({expectedTripVersion:tripVersion,expectedOrderVersion:orderVersion,lines:result})});
                if(epoch!==previewEpoch||selected!==orderId||!totalValue.isConnected)return;
                if(data.tripId!==tripId||data.deliveryOrderId!==orderId||data.tripVersion!==tripVersion||data.orderVersion!==orderVersion||!Array.isArray(data.lines)||data.lines.length!==result.length||data.lines.some(x=>!result.some(r=>r.lineId===x.lineId&&r.deliveredBaseQuantityText===x.deliveredBaseQuantityText&&r.actualReturnedBaseQuantityText===x.actualReturnedBaseQuantityText)))throw new Error('Bản tính tiền không khớp lượng đang kiểm.');
                moneyValue(data.payableText);preview=data;previewKey=key;totalValue.textContent=money(data.payableText);validate();
            }catch(e){if(epoch===previewEpoch&&totalValue.isConnected){preview=null;finish.disabled=true;problem.textContent=e.message;if(e.status===409)needsFresh=true;}}},immediate?0:300);
        }
        queuePreview();saveDraft();
    }
    function renderClosed(panel) {
        const details=counterInfo(),result=details?.settlement;
        heading(panel,picking.delivery.state==='Cancelled'?'Đơn đã hủy':'Đơn đã hoàn tất');
        if(!result){panel.append(node('p',picking.delivery.state==='Cancelled'?'Đơn đã được kết thúc theo trạng thái hủy.':'Xem lịch sử để đối chiếu bản chốt của đơn.','dd-success'));return;}
        const box=node('div',null,'dd-success');box.append(node('strong','Chốt theo hàng thực giao: '+money(result.payableText)),node('p','Tiền mặt: '+money(result.receivedCashText)+' · Chuyển khoản: '+money(result.confirmedTransferText)+' · Công nợ: '+money(result.customerDebtText)),node('p','Ghi nhận '+date(result.recordedAtUtc)+(result.saleOrderId?' · Đơn bán #'+result.saleOrderId:'')));panel.append(box);
        const order=counterOrder();for(const line of details.settledLines||[]){const name=order?.lines.find(x=>x.lineId===line.lineId);panel.append(node('p',(name?.itemName||'Mặt hàng')+': khách nhận '+quantityDisplay(line.deliveredBaseQuantityText)+' · nhận về '+quantityDisplay(line.actualReturnedBaseQuantityText)+' '+(name?.baseUnitName||''),'dd-help'));}
    }
    $('ddDispatchForm').addEventListener('submit',event=>{event.preventDefault();try{const courier=Number($('ddCourier').value);if(!positive(courier)||!$('ddDispatchConfirmed').checked)throw new Error('Chọn người giao và xác nhận đã nhận đủ hàng để xuất phát.');mutate('dispatch',{courierUserId:courier,orders:dispatchOrders.map(x=>({deliveryOrderId:x.deliveryOrderId,expectedVersion:x.version})),physicalReceiptConfirmed:true,note:$('ddDispatchNote').value.trim()||null},{orderId:dispatchOrders[0].deliveryOrderId});}catch(e){$('ddDispatchMessage').textContent=e.message;}});
    for(const id of ['ddDispatchClose','ddDispatchCancel'])$(id).addEventListener('click',()=>{if(!busy)$('ddDispatch').close();});
    $('ddDispatch').addEventListener('cancel',event=>{if(busy)event.preventDefault();});
    $('ddBulkDispatch').addEventListener('click',()=>openDispatch([...selectedOrders.values()]));
    desk.querySelectorAll('[data-dd-stage]').forEach(b=>b.addEventListener('click',()=>{if(busy)return;stage=b.dataset.ddStage;page=1;selectedOrders.clear();desk.removeAttribute('data-detail-open');loadList();}));
    $('ddPrev').addEventListener('click',()=>{if(page>1){page--;loadList();}});$('ddNext').addEventListener('click',()=>{if(page<pages){page++;loadList();}});
    $('ddRefresh').addEventListener('click',async()=>{if(busy)return;needsFresh=false;await loadList();if(selected)await openOrder(selected,true);});
    async function lookup(raw, initialRevision=null) {
        const {key,revision}=parseLookup(raw,location.origin,initialRevision);
        const delivery=await request('/admin/api/deliveries/lookup?key='+encodeURIComponent(key));if(!positive(delivery.id))throw new Error('Mã đơn chưa hợp lệ.');await openOrder(delivery.id,true,revision);stage=stageOf(picking?.delivery.state||delivery.state);page=1;await loadList();
    }
    $('ddSearch').addEventListener('submit',async event=>{event.preventDefault();const text=$('ddQuery').value.trim();if(!text){search='';page=1;await loadList();return;}try{await lookup(text);}catch(e){if(e.status===404||!/^https?:\/\//i.test(text)&&!e.status){if(text.length>200){message('Từ khóa tìm kiếm tối đa 200 ký tự.',true);return;}search=text;page=1;desk.removeAttribute('data-detail-open');await loadList();}else message(e.message,true);}});
    root.addEventListener('storage',event=>{if(event.key===intentKey)pendingPanel();});
    document.addEventListener('visibilitychange',()=>{if(!document.hidden&&!busy)loadList();});
    setInterval(()=>{if(!document.hidden&&!busy&&!$('ddDispatch').open)loadList();},30000);
    async function start(){pendingPanel();await loadList();const params=new URLSearchParams(location.search);if(params.get('key')){try{await lookup(params.get('key'),params.get('revision'));}catch(e){message(e.message,true);}}}
    start();
})(typeof window === 'undefined' ? globalThis : window);
