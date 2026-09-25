(() => {
    'use strict';
    if (window.GaoLabels) return;
    let progress = [];
    const token = () => document.querySelector('[name="__RequestVerificationToken"]')?.value || '';
    async function add(id) {
        const response = await fetch('/admin/label-printing/receipts/' + id, { method: 'POST', headers: { RequestVerificationToken: token() } });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.message || result.detail || 'Không đưa được phiếu vào in tem. Hãy lưu sản phẩm trên phiếu rồi thử lại.');
        return result.id;
    }
    async function loadProgress() {
        try {
            const response = await fetch('/admin/label-printing/tasks', { cache: 'no-store' });
            if (response.ok) progress = await response.json();
            document.querySelectorAll('[data-label-progress]').forEach(element => {
                const item = progress.find(x => x.stockDocumentId === Number(element.dataset.labelProgress));
                element.textContent = item ? `${item.completed ? 'Hoàn thành in tem' : 'In tem'}: ${item.printed}/${item.required} tem` : 'Chưa đưa vào in tem';
            });
        } catch { /* Receipt viewing stays available if printing progress is unavailable. */ }
    }
    window.GaoLabels = {
        loadProgress,
        progressHtml(id) {
            const item = progress.find(x => x.stockDocumentId === Number(id));
            return item ? `<div class="small mt-1"><a href="/admin/label-printing?task=${item.id}">${item.completed ? '✓ Hoàn thành tem' : 'Tem'}: ${item.printed}/${item.required}</a></div>` : '';
        },
        async afterApproval(id) {
            if (!document.querySelector('[data-label-receipt]')) return false;
            if (!window.confirm('Đã duyệt phiếu nhập thành công. Bạn có muốn đưa phiếu này vào danh sách in tem không?')) return false;
            try { const taskId = await add(id); window.location.href = '/admin/label-printing?task=' + taskId; return true; }
            catch (e) { window.alert('Phiếu nhập đã được duyệt. ' + e.message + ' Bạn có thể dùng nút Đưa vào in tem để thử lại.'); return false; }
        }
    };
    document.addEventListener('click', async event => {
        const button = event.target.closest('[data-label-add]'); if (!button) return;
        button.disabled = true;
        try { const taskId = await add(Number(button.dataset.labelAdd)); window.location.href = '/admin/label-printing?task=' + taskId; }
        catch (e) { window.alert(e.message); button.disabled = false; }
    });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', loadProgress); else loadProgress();
})();
