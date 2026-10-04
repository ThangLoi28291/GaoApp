(() => {
    'use strict';
    function init() {
    const root=document.getElementById('paymentAdjustments');if(!root||root.dataset.initialized==='true')return;root.dataset.initialized='true';
    const $=id=>document.getElementById(id),base='/admin/pos-shift/payment-adjustments',dialog=$('paDialog');
    const isAdmin=root.dataset.admin==='true',url=new URL(location.href);
    let paymentId=Number(url.searchParams.get('paymentId'))||null,orderId=Number(url.searchParams.get('orderId'))||null,shiftId=Number(url.searchParams.get('shiftId'))||null,entryId=Number(url.searchParams.get('entryId'))||null;
    let tab=entryId||url.searchParams.get('source')==='deposits'?'deposits':paymentId||orderId||!isAdmin?'payments':'requests',page=1,pages=1,rows=[],busy=false,sequence=0,detailSequence=0,listController;
    if(['requests','payments','deposits'].includes(url.searchParams.get('view')))tab=url.searchParams.get('view');
    const names={Pending:'Chờ duyệt',Approved:'Đã duyệt',Rejected:'Từ chối',Withdrawn:'Đã rút'};
    const methods={Cash:0,BankTransfer:1,Card:2,EWallet:3,Other:99};
    const method=m=>({Cash:'Tiền mặt',BankTransfer:'Chuyển khoản',Card:'Thẻ',EWallet:'Ví điện tử',Other:'Khác'}[m]||m);
    const money=n=>n==null?'Chưa ghi nhận':Number(n).toLocaleString('vi-VN',{maximumFractionDigits:2})+' đ';
    const signed=n=>(n>0?'+':'')+money(n);
    const date=s=>s?new Date(/Z$|[+-]\d\d:\d\d$/.test(s)?s:s+'Z').toLocaleString('vi-VN'):'—';
    const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const badge=r=>`<span class="pa-status is-${r.status==='Approved'?'approved':r.status==='Pending'?'pending':'other'}">${names[r.status]||'—'}</span>${r.appliedToClosedShift&&!r.reconciledAtUtc?'<small class="text-warning">Cần đối soát lại</small>':''}`;
    const orderLink=r=>r.depositEntryId?`<a href="/admin/customer-deposit?customerId=${Number(r.customerId)}" target="_blank" rel="noopener">${esc(r.number)}</a>`:`<a href="/admin/pos/order-detail/${Number(r.orderId)}" target="_blank" rel="noopener">${esc(r.number)}</a>`;
    const sourcePath=r=>r.depositEntryId?'deposits':'payments';
    function message(text,error=false){$('paMessage').hidden=!text;$('paMessage').className='alert alert-'+(error?'danger':'success');$('paMessage').textContent=text;}
    function modalError(text){$('paDialogError').hidden=!text;$('paDialogError').textContent=text;}
    function show(title,body){$('paDialogTitle').textContent=title;$('paDialogBody').innerHTML=body;modalError('');if(!dialog.open)dialog.showModal();dialog.scrollTop=0;}
    function setBusy(value){busy=value;dialog.querySelectorAll('button,input,select,textarea').forEach(el=>el.disabled=value);}
    dialog.addEventListener('cancel',e=>{if(busy)e.preventDefault();});dialog.addEventListener('close',()=>{detailSequence++;});
    $('paClose').onclick=()=>{if(!busy)dialog.close();};
    async function api(path,body,signal){
        const c=new AbortController(),timer=setTimeout(()=>c.abort(),20000),abort=()=>c.abort();signal?.addEventListener('abort',abort,{once:true});
        try{
            const response=await fetch(base+path,{method:body===undefined?'GET':'POST',credentials:'same-origin',cache:'no-store',signal:c.signal,
                headers:{'Content-Type':'application/json',RequestVerificationToken:root.querySelector('[name="__RequestVerificationToken"]').value},
                body:body===undefined?undefined:JSON.stringify(body)});
            const data=await response.json().catch(()=>({}));
            if(!response.ok||response.redirected)throw Error(data.message||data.detail||'Không thể xử lý. Kiểm tra đăng nhập và tải lại dữ liệu.');return data;
        }catch(e){if(e.name==='AbortError')throw Error('Chưa nhận được kết quả. Tải lại để kiểm tra; gửi lại cùng yêu cầu sẽ không ghi nhận hai lần.');throw e;}
        finally{clearTimeout(timer);signal?.removeEventListener('abort',abort);}
    }
    async function load(){
        const seq=++sequence;listController?.abort();listController=new AbortController();
        root.querySelectorAll('[data-tab]').forEach(b=>{b.classList.toggle('active',b.dataset.tab===tab);b.setAttribute('aria-pressed',String(b.dataset.tab===tab));});
        $('paStatusLabel').hidden=tab!=='requests';$('paKeywordLabel').hidden=tab==='requests';
        $('paScope').textContent=[shiftId?'Ca #'+shiftId:'',orderId&&tab==='payments'?'Đơn #'+orderId:'',paymentId&&tab==='payments'?'Khoản thanh toán #'+paymentId:'',entryId&&tab==='deposits'?'Khoản cọc #'+entryId:''].filter(Boolean).join(' · ');
        if($('paReconciliation'))$('paReconciliation').href='/admin/pos-shift/reconciliation'+(shiftId?'?shiftId='+shiftId:'');
        const params=new URLSearchParams({page});if(shiftId)params.set('shiftId',shiftId);
        if(tab==='requests')params.set('status',$('paStatus').value);else{params.set('keyword',$('paKeyword').value);if(tab==='deposits'){if(entryId)params.set('entryId',entryId);}else{if(paymentId)params.set('paymentId',paymentId);if(orderId)params.set('orderId',orderId);}}
        $('paPrev').disabled=$('paNext').disabled=true;$('paRows').innerHTML='<tr><td colspan="6">Đang tải…</td></tr>';
        $('paHead').innerHTML=tab==='requests'?'<tr><th>Khoản tiền</th><th>Đổi phương thức</th><th>Trạng thái</th><th></th></tr>':'<tr><th>Khoản tiền</th><th>Số tiền</th><th>Đang ghi nhận</th><th></th></tr>';
        try{
            const data=await api('/'+(tab==='requests'?'data':tab)+'?'+params,undefined,listController.signal);if(seq!==sequence)return;
            rows=data.items;pages=Math.max(1,data.totalPages);
            $('paRows').innerHTML=rows.map(r=>tab==='requests'
                ?`<tr><td>${orderLink(r)} · <b>${money(r.amount)}</b><small>${esc(r.customerName||r.requestedBy)} · ${esc(r.shiftCode)}</small></td><td>${esc(method(r.oldMethod))} → <b>${esc(method(r.newMethod))}</b></td><td>${badge(r)}</td><td><button class="btn btn-outline-primary btn-sm" data-detail="${r.id}">Xem yêu cầu</button></td></tr>`
                :`<tr><td>${orderLink(r)}<small>${esc(r.customerName||'')} ${date(r.paidAtUtc)}${r.unavailableReason?'<br>'+esc(r.unavailableReason):''}</small></td><td><b>${money(r.amount)}</b></td><td>${esc(method(r.method))}${r.pendingRequestId?'<small>Chờ duyệt thay đổi</small>':''}</td><td>${r.pendingRequestId?`<button class="btn btn-outline-primary btn-sm" data-detail="${r.pendingRequestId}">Xem yêu cầu</button>`:r.canRequest?`<button class="btn btn-outline-primary btn-sm" data-create="${r.id}">Đổi phương thức</button>`:'—'}</td></tr>`).join('')||'<tr><td colspan="4" class="text-center p-5">Không có dữ liệu phù hợp.</td></tr>';
            $('paCount').textContent=data.totalItems.toLocaleString('vi-VN')+(tab==='requests'?' yêu cầu':' khoản thanh toán');$('paPage').textContent=page+' / '+pages;
            $('paPrev').disabled=page<=1;$('paNext').disabled=page>=pages;
        }catch(e){if(seq===sequence){$('paRows').innerHTML='<tr><td colspan="6">Không tải được dữ liệu. Bấm Tải lại để thử lại.</td></tr>';message(e.message,true);}}
    }
    function position(p){return `<div class="ca-position">${[['Tiền mặt bán hàng',p.cashSales],['Không tiền mặt bán hàng',p.nonCashSales],['Tiền mặt dự kiến',p.expected],['Tiền thực đếm',p.actual],['Tiền đã bàn giao',p.received],['Thực đếm − dự kiến',p.difference]].map(([name,n])=>`<div>${name}<strong>${money(n)}</strong></div>`).join('')}</div>`;}
    function methodBox(title,m,reference,amount,proposed=false){return `<div class="ca-box ${proposed?'proposed':''}"><h3>${title}</h3><div class="pa-method">${esc(method(m))}</div><div class="ca-amount">${money(amount)}</div>${reference?'<small>Mã giao dịch: '+esc(reference)+'</small>':''}</div>`;}
    function impact(d){const p=d.proposedShift;return `<div class="pa-simple-impact"><div>Quỹ dự kiến hiện tại<strong>${money(d.currentShift.expected)}</strong></div><span aria-hidden="true">→</span><div>Sau khi duyệt<strong>${money(p.expected)}</strong></div></div><p class="mb-0">Thay đổi quỹ: <b>${signed(d.expectedDelta??d.request.expectedDelta)}</b>${p.actual==null?'':` · ${p.difference===0?'Khớp tiền thực đếm':(p.difference<0?'Còn thiếu ':'Còn thừa ')+money(Math.abs(p.difference))}`}</p>`;}
    function create(r){
        if(!r?.canRequest)return;const guard=++detailSequence,clientRequestId=crypto.randomUUID();let previewSequence=0,ready=false;
        show(r.depositEntryId?'Đổi phương thức nhận cọc':'Đổi phương thức thanh toán',`<p>${orderLink(r)}${r.customerName?' · '+esc(r.customerName):''} · ${esc(r.shiftCode)}</p><div class="pa-receipt"><strong>${money(r.amount)}</strong><span>Đang ghi nhận: ${esc(method(r.method))}</span></div>
            <form id="paCreateForm"><label>Đổi sang<select id="paMethod" class="form-select">${Object.entries(methods).filter(([,value])=>!r.depositEntryId||value===0||value===1).map(([name,value])=>`<option value="${value}" ${name===r.method?'selected':''}>${esc(method(name))}</option>`).join('')}</select></label>
            <label><span>Lý do đề nghị <span class="text-danger">*</span></span><textarea id="paReason" class="form-control" required maxlength="500" placeholder="Ghi rõ khoản đã nhận thực tế và nội dung nhập nhầm…"></textarea></label>
            <details id="paReferenceExtra" class="pa-extra"><summary>Thông tin bổ sung (không bắt buộc)</summary><label id="paReferenceLabel">Mã giao dịch<input id="paReference" class="form-control" maxlength="100" value="${esc(r.reference||'')}" placeholder="Có thể để trống" /></label></details>
            <div id="paPreview" class="pa-impact" role="status">Đang tính ảnh hưởng cho ca…</div>
            <p class="text-muted small">Số tiền nhận được giữ nguyên. Chỉ cập nhật sau khi quản lý duyệt.</p>
            <div class="ca-actions"><button id="paSend" type="submit" class="btn btn-primary" disabled>Gửi yêu cầu</button></div></form>`);
        function updateSubmit(){const m=Number($('paMethod').value),ref=m===0?null:$('paReference').value.trim()||null;
            $('paSend').disabled=busy||!ready||(m===methods[r.method]&&ref===(r.reference||null));}
        async function preview(){
            const seq=++previewSequence,m=Number($('paMethod').value);ready=false;updateSubmit();
            $('paReferenceExtra').hidden=m===0;$('paReferenceLabel').hidden=m===0;$('paReference').disabled=m===0;$('paReference').required=false;
            $('paPreview').textContent='Đang tính ảnh hưởng cho ca…';modalError('');
            try{const d=await api(`/${sourcePath(r)}/${r.id}/preview?method=${m}`);if(seq!==previewSequence||guard!==detailSequence||!dialog.open)return;
                $('paPreview').innerHTML=impact(d);
                ready=true;updateSubmit();
            }catch(e){if(seq===previewSequence&&guard===detailSequence&&dialog.open){$('paPreview').textContent='Chưa tính được ảnh hưởng. Chọn lại phương thức hoặc tải lại danh sách.';modalError(e.message);}}
        }
        $('paMethod').onchange=preview;$('paReference').oninput=updateSubmit;preview();
        $('paCreateForm').onsubmit=async e=>{
            e.preventDefault();if(busy||!ready||$('paSend').disabled)return;
            const input={clientRequestId,rowVersion:r.rowVersion,method:Number($('paMethod').value),reference:Number($('paMethod').value)===0?null:$('paReference').value,requestReason:$('paReason').value};
            setBusy(true);modalError('');let result;
            try{result=await api('/'+sourcePath(r)+'/'+r.id+'/requests',input);}catch(error){modalError(error.message);}finally{setBusy(false);$('paReference').disabled=Number($('paMethod').value)===0;updateSubmit();}
            if(result){message('Đã gửi yêu cầu. Khoản thanh toán và số liệu ca chưa thay đổi.');tab='requests';$('paStatus').value='Pending';page=1;await load();await detail(result.id);}
        };
    }
    async function detail(id){
        const seq=++detailSequence;show('Yêu cầu #'+id,'<p>Đang tải chi tiết…</p>');
        try{
            const d=await api('/'+id);if(seq!==detailSequence||!dialog.open)return;const r=d.request;let snapshots='';
            if(r.beforeShiftJson&&r.afterShiftJson){try{snapshots=`<details class="ca-audit"><summary>Số liệu ca trước / sau khi duyệt</summary><h3>Trước</h3>${position(JSON.parse(r.beforeShiftJson))}<h3>Sau</h3>${position(JSON.parse(r.afterShiftJson))}</details>`;}catch{}}
            show('Yêu cầu #'+r.id+' · '+(r.depositEntryId?'Đổi cọc':'Sửa thanh toán'),`<p>${badge(r)}</p><p>${orderLink(r)}${r.customerName?' · '+esc(r.customerName):''} · ${esc(r.requestedBy)}</p>
                <div class="ca-compare">${methodBox('Trước điều chỉnh',r.oldMethod,r.oldReference,r.amount)}${methodBox('Đề nghị',r.newMethod,r.newReference,r.amount,true)}</div>
                <h3>Lý do</h3><p class="ca-detail-note">${esc(r.requestReason)}</p>
                ${r.status==='Pending'&&!d.unavailableReason?'<div class="pa-impact">'+impact(d)+'</div>':''}
                <details class="pa-extra"><summary>Số liệu và thông tin đầy đủ</summary><p>${esc(r.shiftCode)} · ${date(r.createdAtUtc)}</p><h3>Số liệu ca hiện tại</h3>${position(d.currentShift)}${r.status==='Pending'&&!d.unavailableReason?'<h3>Sau khi duyệt</h3>'+position(d.proposedShift):''}<p class="pa-count-note">Ca đã đóng cần đối soát lại. Tiền thực đếm và số tiền bàn giao được giữ nguyên.</p></details>
                ${r.status==='Pending'&&d.unavailableReason?'<div class="alert alert-warning">'+esc(d.unavailableReason)+'</div>':''}
                ${r.reviewedAtUtc?`<div class="ca-audit">${names[r.status]} bởi <b>${esc(r.reviewedBy)}</b> · ${date(r.reviewedAtUtc)}<div class="ca-detail-note">${esc(r.reviewNote)}</div></div>`:''}
                ${r.reconciledAtUtc?`<div class="ca-audit">Đối soát lại bởi <b>${esc(r.reconciledBy)}</b> · ${date(r.reconciledAtUtc)}<div class="ca-detail-note">${esc(r.reconciliationNote)}</div></div>`:''}${snapshots}
                ${d.currentShift.needsReconciliation?'<div class="alert alert-warning">Ca cần đối soát lại sau các điều chỉnh thu/chi, thanh toán và cọc đã duyệt.</div>':''}
                ${isAdmin&&((r.status==='Pending')||d.currentShift.needsReconciliation)||d.canWithdraw?'<label>Ghi chú xử lý<textarea id="paDecisionNote" class="form-control" maxlength="500" placeholder="Bắt buộc khi từ chối hoặc xác nhận đối soát lại"></textarea></label>':''}
                <div class="ca-actions">${d.canWithdraw?'<button class="btn btn-outline-secondary" data-decision="withdraw">Rút yêu cầu</button>':''}${isAdmin&&r.status==='Pending'?'<button class="btn btn-outline-danger" data-decision="reject">Từ chối</button>':''}${d.canApprove?'<button class="btn btn-primary" data-decision="approve">Duyệt điều chỉnh</button>':''}${isAdmin&&d.currentShift.needsReconciliation?'<button class="btn btn-success" data-decision="reconcile">Xác nhận đã đối soát lại ca</button>':''}</div>`);
            dialog.querySelectorAll('[data-decision]').forEach(b=>b.onclick=async()=>{
                if(busy)return;const action=b.dataset.decision,note=$('paDecisionNote')?.value.trim();
                if((action==='reject'||action==='reconcile')&&!note){modalError('Vui lòng nhập ghi chú xử lý.');$('paDecisionNote').focus();return;}
                setBusy(true);modalError('');let ok=false;
                try{await api(action==='reconcile'?'/shifts/'+r.shiftId+'/reconcile':'/'+r.id+'/'+action,
                    {rowVersion:action==='reconcile'?d.shiftRowVersion:r.rowVersion,shiftRowVersion:d.shiftRowVersion,note});ok=true;}
                catch(e){modalError(e.message);}finally{setBusy(false);}
                if(ok){message(action==='approve'?'Đã duyệt và cập nhật phương thức thanh toán cùng số liệu ca.':action==='reconcile'?'Đã xác nhận đối soát lại ca.':'Đã cập nhật yêu cầu.');await load();await detail(id);}
            });
        }catch(e){if(seq===detailSequence&&dialog.open)modalError(e.message);}
    }
    $('paRows').onclick=e=>{const b=e.target.closest('button');if(!b||busy)return;if(b.dataset.detail)detail(Number(b.dataset.detail));else if(b.dataset.create)create(rows.find(r=>r.id===Number(b.dataset.create)));};
    root.querySelectorAll('[data-tab]').forEach(b=>b.onclick=()=>{if(busy)return;tab=b.dataset.tab;page=1;load();});
    $('paFilters').onsubmit=e=>{e.preventDefault();page=1;load();};$('paStatus').onchange=()=>{page=1;load();};
    $('paRefresh').onclick=()=>{message('');load();};$('paClear').onclick=()=>{shiftId=orderId=paymentId=entryId=null;$('paKeyword').value='';$('paStatus').value='Pending';page=1;if(window.POSRequestTabs)window.POSRequestTabs.clearFilterUrl('payment');else history.replaceState(null,'',base);load();};
    $('paPrev').onclick=()=>{if(page>1){page--;load();}};$('paNext').onclick=()=>{if(page<pages){page++;load();}};
    if(url.searchParams.has('status'))$('paStatus').value=url.searchParams.get('status');
    if(url.searchParams.has('keyword'))$('paKeyword').value=url.searchParams.get('keyword');
    window.POSPaymentAdjustmentPage.getQuery=()=>{
        const query=new URLSearchParams({tab:'payment',view:tab});
        if(shiftId)query.set('shiftId',shiftId);if(paymentId)query.set('paymentId',paymentId);if(orderId)query.set('orderId',orderId);if(entryId)query.set('entryId',entryId);
        if(tab==='requests')query.set('status',$('paStatus').value);else if($('paKeyword').value)query.set('keyword',$('paKeyword').value);
        return query;
    };
    load();if(Number(url.searchParams.get('requestId'))>0)detail(Number(url.searchParams.get('requestId')));
    }
    window.POSPaymentAdjustmentPage={init};
    if(!document.getElementById('posOperationsRequests'))init();
})();
