(function () {
    'use strict';
    function buttons(item, permissions) {
        const id = Number(item.id), state = Number(item.status);
        if (!Number.isInteger(id) || id <= 0 || state === 5) return '';
        const rights = item.purchaseOrderId ? permissions.purchase : permissions.inventory;
        const draft = state === 1 && !item.submittedAtUtc && !item.approvedAtUtc;
        const edit = rights?.manage || rights?.edit && (draft || state === 2);
        const label = state === 2 && !rights?.manage ? 'Yêu cầu đổi tên' : 'Đổi tên';
        return (edit ? `<button type="button" class="btn btn-sm btn-outline-primary" data-receipt-title="${id}">${label}</button>` : '') +
            (draft && rights?.delete ? `<button type="button" class="btn btn-sm btn-outline-danger" data-receipt-delete="${id}">Xóa phiếu</button>` : '');
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { buttons };
    if (typeof document === 'undefined') return;
    const config = document.getElementById('receiptDocumentPermissions');
    if (!config) return;
    const permissions = JSON.parse(config.textContent), el = id => document.getElementById(id);
    const modalElement = el('receiptDocumentModal'), modal = new bootstrap.Modal(modalElement);
    const detailId = Number(el('receiptDocumentActions')?.dataset.documentId || 0);
    let current, deleteMode = false, busy = false, loading = false;
    window.GaoReceiptDocumentActions = { buttons: item => buttons(item, permissions) };
    const show = (id, visible) => el(id).classList.toggle('d-none', !visible);
    async function request(id, action = '', body) {
        const response = await fetch(`/admin/api/stock-documents/${id}/document-actions${action ? '/' + action : ''}`, {
            method: body ? 'POST' : 'GET', cache: 'no-store', headers: { Accept: 'application/json',
                ...(body ? { 'Content-Type':'application/json', RequestVerificationToken:modalElement.querySelector('[name="__RequestVerificationToken"]').value } : {}) },
            ...(body ? { body:JSON.stringify(body) } : {}) });
        const json = await response.json(), data = json?.data ?? json;
        if (!response.ok) throw new Error(data.message || json.message || 'Không thể thao tác phiếu. Kiểm tra quyền và tải lại trang.');
        return data;
    }
    function updateDetail(data) {
        if (!detailId) return;
        const item = data.document, toolbar = el('receiptDocumentToolbar');
        toolbar.replaceChildren();
        const add = (label, kind) => { const b = document.createElement('button'); b.type='button'; b.className='btn btn-sm btn-outline-primary';
            b.textContent=label; b.dataset[kind]=String(item.id); toolbar.append(b); };
        if (data.canRename || data.canRequest || item.proposal) add(data.canRename ? 'Đổi tên phiếu' : 'Yêu cầu đổi tên', 'receiptTitle');
        if (data.canCorrect) {
            const b = document.createElement('a');
            b.className = 'btn btn-sm btn-outline-warning';
            b.textContent = 'Điều chỉnh sau duyệt';
            b.href = `/admin/inventory-adjustment-documents/create?sourceReceiptId=${item.id}&warehouseId=${item.warehouseId}`;
            toolbar.append(b);
        }
        if (data.canDelete) add('Xóa phiếu nháp', 'receiptDelete');
        const notice = el('receiptTitleProposalNotice');
        notice.classList.toggle('d-none', !item.proposal);
        notice.textContent = item.proposal ? `Đang chờ duyệt tên: ${item.proposal.title}. ${data.canManage ? 'Bấm Đổi tên phiếu để chấp nhận hoặc từ chối.' : 'Quản lý sẽ kiểm tra yêu cầu.'}` : '';
    }
    function syncTitle(data) {
        const item = data.document;
        document.querySelectorAll('[data-receipt-document-title],.wrd-document-title,.wra-heading h1').forEach(node => node.textContent=item.documentTitle || 'Chưa đặt tên phiếu');
        window.stockDocumentRowVersion?.update?.(item.rowVersion);
        if (window.warehouseReceivingDetail) window.warehouseReceivingDetail.rowVersion=item.rowVersion;
        document.querySelectorAll('[data-receiving-row-version]').forEach(node=>node.dataset.receivingRowVersion=item.rowVersion);
        updateDetail(data);
    }
    async function open(id, deleting) {
        if (busy || loading) return;
        if (window.receivingHasPendingChanges?.() || window.ReceiptIntake?.isSaving?.() || window.ReceiptQuantityControls?.isBusy()) {
            window.alert('Vui lòng lưu xong các dòng hàng trước khi thao tác tên hoặc xóa phiếu.'); return;
        }
        loading=true; current=null; deleteMode=deleting;
        el('receiptDocumentMessage').textContent='Đang tải phiếu…'; el('receiptTitleSave').disabled=true;
        show('receiptTitleApprove',false); show('receiptTitleDecline',false); show('receiptTitleProposal',false);
        show('receiptTitleFields',false); show('receiptDeleteWarning',false); el('receiptDocumentIdentity').textContent='';
        modal.show();
        try {
            current=await request(id); const item=current.document;
            el('receiptDocumentIdentity').textContent=`${item.documentNo} · ${item.documentTitle || 'Chưa đặt tên phiếu'}`;
            el('receiptDocumentModalTitle').textContent=deleting?'Xóa phiếu nhập nháp':'Tên phiếu nhập';
            el('receiptNewTitle').value=item.documentTitle || '';
            show('receiptTitleFields',!deleting); show('receiptDeleteWarning',deleting);
            show('receiptTitleProposal',!deleting && !!item.proposal);
            el('receiptTitleProposal').textContent=item.proposal ? `Tên đề nghị: ${item.proposal.title} · ${item.proposal.requestedBy || 'Nhân viên'}` : '';
            show('receiptTitleApprove',!deleting && current.canManage && !!item.proposal);
            show('receiptTitleDecline',!deleting && current.canManage && !!item.proposal);
            el('receiptTitleHint').textContent=current.canRename?'Đổi tên không thay đổi hàng hóa, giá nhập hoặc trạng thái duyệt.':'Tên hiện tại được giữ nguyên cho đến khi quản lý chấp nhận yêu cầu.';
            el('receiptTitleSave').textContent=deleting?'Xác nhận xóa':current.canRename?'Lưu tên':'Gửi yêu cầu đổi tên';
            el('receiptTitleSave').disabled=deleting ? !current.canDelete : !(current.canRename || current.canRequest);
            el('receiptDocumentMessage').textContent=deleting && !current.canDelete?'Phiếu này không còn đủ điều kiện xóa.':'';
        } catch(error) { el('receiptDocumentMessage').textContent=error.message; }
        finally { loading=false; }
    }
    async function save(review) {
        if (busy || loading || !current) return;
        const item=current.document, title=el('receiptNewTitle').value.trim();
        const action=typeof review === 'boolean'?'review-title':deleteMode?'delete':current.canRename?'rename':'request-title';
        if (['rename','request-title'].includes(action) && (!title || title.length>255)) {el('receiptDocumentMessage').textContent='Nhập tên phiếu từ 1 đến 255 ký tự.';return;}
        busy=true; const controls=[...modalElement.querySelectorAll('button,input')]; controls.forEach(x=>x.disabled=true);
        el('receiptDocumentMessage').textContent='Đang lưu…';
        try {
            await request(item.id,action,{rowVersion:item.rowVersion,...(action==='review-title'?{requestId:item.proposal.id,approve:review}:action==='delete'?{}:{title})});
            if(action==='delete') { window.location.assign(window.location.pathname.startsWith('/admin/warehouse-receiving')?'/admin/warehouse-receiving':'/admin/stock-documents');return; }
            if (!detailId) { window.location.reload(); return; }
            const latest=await request(item.id); syncTitle(latest); current=latest; busy=false; modal.hide();
        } catch(error) {
            el('receiptDocumentMessage').textContent=error.message; current=null;
            el('receiptDocumentMessage').textContent+=' Đóng và mở lại để lấy dữ liệu mới trước khi thử lại.';
        } finally {busy=false;controls.forEach(x=>x.disabled=false);if(!current)el('receiptTitleSave').disabled=true;}
    }
    document.addEventListener('click',event=>{
        const button=event.target.closest('[data-receipt-title],[data-receipt-delete]'); if(!button)return;
        event.preventDefault(); event.stopPropagation(); open(Number(button.dataset.receiptTitle || button.dataset.receiptDelete),!!button.dataset.receiptDelete);
    });
    modalElement.addEventListener('hide.bs.modal',event=>{if(busy)event.preventDefault();});
    el('receiptTitleSave').addEventListener('click',()=>save());
    el('receiptTitleApprove').addEventListener('click',()=>save(true));
    el('receiptTitleDecline').addEventListener('click',()=>save(false));
    if(detailId) request(detailId).then(updateDetail).catch(()=>{});
})();
