(() => {
    'use strict';
    if (window.GaoLabels) return;
    let progress = [];
    let opened = false;
    async function openPrint(id, approved = false) {
        if (opened) return false;
        opened = true;
        const ui = window.GaoLabelControls, dialog = document.createElement('dialog');
        dialog.className = 'label-print-popup'; dialog.setAttribute('aria-labelledby', 'receiptPrintTitle');
        dialog.innerHTML = `<div class="label-popup-heading"><span class="label-popup-icon"><i class="bx bx-check-shield" aria-hidden="true"></i></span><button type="button" class="btn btn-outline-secondary" data-close aria-label="Đóng">×</button></div>
            <h2 id="receiptPrintTitle">${approved ? 'Phiếu nhập đã được duyệt' : 'In tem phiếu nhập'}</h2><p data-document></p>
            <h3>In tem cho lô hàng này?</h3><p data-print-message role="status">Đang tải mẫu và sản phẩm…</p><div data-editor></div>
            <p class="label-help">${approved ? 'Đã duyệt phiếu thành công. Để sau hoặc Esc vẫn giữ kết quả duyệt.' : 'Chọn số lượng rồi bấm mẫu để gửi tới máy in đã cấu hình.'}</p>`;
        document.body.append(dialog); dialog.showModal();
        let editor = null, destination = null;
        const notice = (message, error = false) => { const el = dialog.querySelector('[data-print-message]'); el.textContent = message; el.classList.toggle('text-error', error); };
        const close = () => { if (!editor?.locked) dialog.close(); };
        dialog.addEventListener('cancel', e => { if (editor?.locked) e.preventDefault(); });
        dialog.addEventListener('click', e => { if (e.target.closest('[data-close],[data-later]')) close(); });
        const finished = new Promise(resolve => dialog.addEventListener('close', () => {
            editor?.dispose(); dialog.remove(); opened = false;
            if (destination) { window.location.href = destination; resolve(true); } else resolve(false);
        }, { once: true }));
        try {
            const [task, templates, printers] = await Promise.all([ui.api(`receipts/${id}/preview`), ui.api('templates'), ui.api('printers')]);
            if (!dialog.open) return finished;
            dialog.querySelector('[data-document]').textContent = (task.documentTitle ? task.documentTitle + ' · ' : '') + task.documentNo + ' · ' + task.lines.length + ' sản phẩm';
            notice('');
            editor = ui.receiptEditor(dialog.querySelector('[data-editor]'), task, templates, printers, { popup: true, notice,
                onPrinted: async taskId => { destination = '/admin/label-printing?task=' + taskId; dialog.close(); },
                onQueued: async taskId => { destination = '/admin/label-printing?task=' + taskId; dialog.close(); } });
        } catch (e) { notice('Không tải được phần in tem. ' + e.message, true); }
        return finished;
    }
    async function loadProgress() {
        try {
            const response = await fetch('/admin/label-printing/tasks', { cache: 'no-store' });
            if (response.ok) progress = await response.json();
            document.querySelectorAll('[data-label-progress]').forEach(element => {
                const item = progress.find(x => x.stockDocumentId === Number(element.dataset.labelProgress));
                element.textContent = item ? `${item.completed ? 'Hoàn tất tem' : 'Xử lý tem'}: ${item.progress.handled + item.progress.skipped}/${item.progress.total} SP · ${item.progress.skipped} bỏ qua` : 'Chưa đưa vào in tem';
            });
        } catch { /* Receipt viewing stays available if printing progress is unavailable. */ }
    }
    window.GaoLabels = {
        loadProgress,
        progressHtml(id) {
            const item = progress.find(x => x.stockDocumentId === Number(id));
            return item ? `<div class="small mt-1"><a href="/admin/label-printing?task=${item.id}">${item.completed ? '✓ Hoàn tất tem' : 'Xử lý tem'}: ${item.progress.handled + item.progress.skipped}/${item.progress.total} SP · ${item.progress.skipped} bỏ qua</a></div>` : '';
        },
        async afterApproval(id, enqueue = false) {
            if (!enqueue) return false;
            try {
                await window.GaoLabelControls.api(`receipts/${id}`, 'POST');
                return false;
            } catch {
                // Approval is already committed. Retry only the idempotent queue operation.
                const dialog = document.createElement('dialog');
                dialog.className = 'label-print-popup';
                dialog.innerHTML = '<h2>Đã duyệt và ghi sổ</h2><p role="status">Chưa đưa được phiếu vào danh sách in tem. Có thể thử lại hoặc mở phiếu để đưa vào sau.</p><div class="d-flex gap-2"><button type="button" class="btn btn-primary" data-retry>Thử đưa vào in tem</button><button type="button" class="btn btn-outline-secondary" data-later>Để sau</button></div>';
                document.body.append(dialog); dialog.showModal();
                let busy = false;
                dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
                dialog.querySelector('[data-later]').onclick = () => { if (!busy) dialog.close(); };
                dialog.querySelector('[data-retry]').onclick = async function () {
                    if (busy) return;
                    busy = true; this.disabled = true;
                    try { await window.GaoLabelControls.api(`receipts/${id}`, 'POST'); dialog.close(); }
                    catch (error) { dialog.querySelector('[role="status"]').textContent = 'Phiếu đã duyệt. Chưa đưa vào in tem: ' + error.message; }
                    finally { busy = false; this.disabled = false; }
                };
                await new Promise(resolve => dialog.addEventListener('close', resolve, { once: true }));
                dialog.remove();
                window.location.href = `/admin/stock-documents/${id}`;
                return true;
            }
        }
    };
    document.addEventListener('click', async event => {
        const queue = event.target.closest('[data-label-queue]');
        if (queue) {
            if (queue.disabled) return;
            queue.disabled = true;
            try {
                await window.GaoLabelControls.api(`receipts/${Number(queue.dataset.labelQueue)}`, 'POST');
                queue.textContent = 'Đã có trong danh sách in tem';
                await loadProgress();
            } catch (error) {
                const status = queue.closest('[data-label-receipt]')?.querySelector('[data-label-progress]');
                if (status) status.textContent = error.message;
                queue.disabled = false;
            }
            return;
        }
        const button = event.target.closest('[data-label-add]'); if (!button) return;
        button.disabled = true;
        try { await openPrint(Number(button.dataset.labelAdd)); }
        finally { button.disabled = false; }
    });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', loadProgress); else loadProgress();
})();
