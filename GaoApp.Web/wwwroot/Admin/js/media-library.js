(() => {
    'use strict';
    const root = document.querySelector('[data-media-library]');
    if (!root) return;
    const message = root.querySelector('[data-media-message]');
    const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
    let busy = false;
    function show(text, error = false) {
        message.textContent = text;
        message.className = `alert ${error ? 'alert-warning' : 'alert-info'}`;
    }
    function lock(value) {
        busy = value;
        root.querySelectorAll('[data-media-action], [data-cleanup-all]').forEach(button => button.disabled = value);
    }
    async function post(path) {
        const response = await fetch(`/admin/media-library/${path}`, {
            method: 'POST', credentials: 'same-origin', headers: { 'RequestVerificationToken': token, 'Accept': 'application/json' }
        });
        if (!response.ok || response.redirected) throw new Error('Không hoàn tất thao tác. Vui lòng tải lại trang và kiểm tra quyền truy cập.');
        return response.headers.get('content-type')?.includes('application/json') ? response.json() : null;
    }
    function finish(text) {
        sessionStorage.setItem('gao.media.notice', text);
        window.location.reload();
    }
    const notice = sessionStorage.getItem('gao.media.notice');
    if (notice) { sessionStorage.removeItem('gao.media.notice'); show(notice); }
    root.querySelectorAll('time[data-local-time]').forEach(time => {
        const date = new Date(time.dateTime.endsWith('Z') ? time.dateTime : time.dateTime + 'Z');
        if (!Number.isNaN(date.getTime())) time.textContent = date.toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'short' });
    });
    root.querySelectorAll('[data-media-image]').forEach(img => {
        const fail = () => { img.hidden = true; img.closest('.media-preview').querySelector('.media-missing').hidden = false; };
        img.addEventListener('error', fail);
        if (img.complete && img.naturalWidth === 0) fail();
    });
    const dialog = root.querySelector('[data-media-lightbox]');
    root.querySelectorAll('[data-preview]').forEach(button => button.addEventListener('click', () => {
        dialog.querySelector('img').src = button.dataset.preview;
        dialog.querySelector('img').alt = button.dataset.name;
        dialog.querySelector('p').textContent = button.dataset.name;
        dialog.showModal();
    }));
    dialog.querySelector('[data-close-preview]').addEventListener('click', () => dialog.close());
    dialog.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
    root.querySelector('[data-cleanup-all]')?.addEventListener('click', async () => {
        if (busy || !window.confirm('Kiểm tra thư viện và xóa file ảnh đã đến hạn, không còn được sử dụng? Ảnh chưa đến hạn sẽ được giữ lại.')) return;
        lock(true);
        let after = 0, scanned = 0, deleted = 0, scheduled = 0, failed = 0;
        try {
            while (true) {
                show(`Đang kiểm tra… Đã xem ${scanned} ảnh, dọn ${deleted} ảnh.`);
                const response = await post(`cleanup?afterId=${after}`);
                const result = response.result;
                scanned += result.scanned; deleted += result.deleted; scheduled += result.scheduled; failed += result.failed;
                if (!response.hasMore || result.lastId <= after) break;
                after = result.lastId;
            }
            finish(`Đã kiểm tra ${scanned} ảnh: dọn ${deleted}, đưa vào chờ ${scheduled}, cần thử lại ${failed}.`);
        } catch (error) { show(`${error.message} Đã dọn ${deleted} ảnh trong lần này.`, true); lock(false); }
    });
    root.querySelectorAll('[data-media-action]').forEach(button => button.addEventListener('click', async () => {
        if (busy) return;
        const cancel = button.dataset.mediaAction === 'cancel-temp';
        if (!window.confirm(cancel ? 'Hủy ảnh tải tạm này? Biểu mẫu đang dùng ảnh này sẽ cần tải ảnh lại.' : 'Kiểm tra ảnh này và dọn nếu đã đến hạn?')) return;
        lock(true);
        try {
            const result = await post(`${button.dataset.id}/${button.dataset.mediaAction}`);
            const descriptions = { Deleted: 'Đã dọn ảnh.', Scheduled: 'Đã đưa ảnh vào thời gian chờ dọn.', Kept: 'Đã giữ lại ảnh: ảnh chưa đến hạn hoặc vẫn có liên kết sử dụng/chung file.', Failed: 'Chưa xóa được file. Hệ thống sẽ thử lại ở lần dọn tiếp theo.' };
            finish(cancel ? 'Đã hủy ảnh tải tạm. Ảnh sẽ được dọn trong lần kiểm tra tiếp theo.' : descriptions[result.outcome] || 'Đã kiểm tra ảnh.');
        } catch (error) { show(error.message, true); lock(false); }
    }));
})();
