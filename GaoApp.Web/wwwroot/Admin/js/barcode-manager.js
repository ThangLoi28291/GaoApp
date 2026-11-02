(() => {
    'use strict';
    const root = document.getElementById('barcodeManagerPage');
    if (!root) return;
    const list = document.getElementById('barcodeManagerList'), history = document.getElementById('barcodeManagerHistory');
    const notice = document.getElementById('barcodeManagerNotice'), form = document.getElementById('barcodeManagerForm');
    let pending = false, uncertain = false;
    function row(target, title, detail) {
        const article = document.createElement('article'), heading = document.createElement('strong'), body = document.createElement('p');
        heading.textContent = title; body.textContent = detail; article.append(heading, body); target.append(article);
    }
    async function get(suffix) {
        const response = await fetch(root.dataset.read + encodeURIComponent(root.dataset.conversion) + suffix, { credentials: 'same-origin', cache: 'no-store' });
        if (!response.ok) throw new Error('Chưa tải được dữ liệu barcode. Vui lòng thử tải lại.');
        return response.json();
    }
    async function reload() {
        const button = document.getElementById('barcodeManagerReload'); button.disabled = true;
        try {
            const [codes, audit] = await Promise.all([get('/list'), get('/history')]);
            list.replaceChildren(); history.replaceChildren();
            for (const code of codes) row(list, code.barcode || 'Chưa có mã', [code.isPrimary ? 'Mã chính' : 'Mã phụ', code.isActive ? 'Đang dùng' : 'Ngừng dùng'].join(' · '));
            if (!codes.length) list.textContent = 'Chưa có barcode.';
            const items = audit.items || [];
            for (const item of items) row(history, `${item.oldBarcode || '—'} → ${item.newBarcode || '—'}`, [item.actionTypeText, item.reason, item.changedAtUtc ? new Date(item.changedAtUtc.endsWith('Z') ? item.changedAtUtc : item.changedAtUtc + 'Z').toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' }) : ''].filter(Boolean).join(' · '));
            if (!items.length) history.textContent = 'Chưa có thay đổi.';
            notice.textContent = uncertain ? 'Đã tải dữ liệu mới. Kiểm tra mã hiện có trước khi tiếp tục.' : '';
            uncertain = false; if (form && !pending) form.querySelector('[type=submit]').disabled = false;
        } catch (error) { notice.textContent = error.message; }
        finally { button.disabled = false; }
    }
    document.getElementById('barcodeManagerReload').addEventListener('click', reload);
    form?.addEventListener('submit', async event => {
        event.preventDefault(); if (pending || uncertain) return;
        pending = true; const submit = form.querySelector('[type=submit]'), error = document.getElementById('barcodeManagerError');
        submit.disabled = true; error.textContent = '';
        try {
            const data = new FormData(form);
            const response = await fetch(root.dataset.change, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', RequestVerificationToken: data.get('__RequestVerificationToken') },
                body: JSON.stringify({ productUnitConversionId: Number(data.get('ProductUnitConversionId')), newBarcode: data.get('NewBarcode'), reason: data.get('Reason'), setAsPrimary: true }) });
            const body = await response.json();
            if (!response.ok) {
                if (response.status >= 500) throw new Error('uncertain');
                error.textContent = response.status === 403 ? 'Bạn không có quyền đổi barcode.' : body.message || 'Chưa đổi được barcode. Kiểm tra nội dung và thử lại.'; return;
            }
            bootstrap.Modal.getInstance(document.getElementById('barcodeChangeDialog'))?.hide(); form.reset();
            await reload(); notice.textContent = 'Đổi barcode thành công.';
        } catch {
            uncertain = true; error.textContent = 'Chưa xác định được kết quả. Đóng hộp thoại và tải lại dữ liệu để kiểm tra trước khi thử tiếp.';
        } finally { pending = false; submit.disabled = uncertain; }
    });
    reload();
})();
