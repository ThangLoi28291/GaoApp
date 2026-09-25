let promotionModal;

document.addEventListener('DOMContentLoaded', function () {
    document.getElementById('guestWifiForm').addEventListener('submit', saveGuestWifi);
    ['guestWifiName', 'guestWifiPassword'].forEach(id => document.getElementById(id).addEventListener('input', renderWifiPreview));
    renderWifiPreview();
    promotionModal = new bootstrap.Modal(document.getElementById('promotionModal'));

    document.getElementById('promotionForm').addEventListener('submit', savePromotion);

    ['title', 'description', 'backgroundColor', 'textColor', 'mediaType'].forEach(function (id) {
        document.getElementById(id)?.addEventListener('input', syncPreview);
        document.getElementById(id)?.addEventListener('change', syncPreview);
    });
});

function renderWifiPreview() {
    const name = document.getElementById('guestWifiName').value;
    const password = document.getElementById('guestWifiPassword').value;
    document.getElementById('wifiPreviewName').textContent = name.trim() ? name : 'Tên Wi-Fi của cửa hàng';
    document.getElementById('wifiPreviewPassword').textContent = name.trim() ? (password || 'Không cần mật khẩu') : 'Chưa thiết lập';
}

async function saveGuestWifi(event) {
    event.preventDefault();
    const form = event.currentTarget, button = form.querySelector('button[type=submit]');
    if (!button || button.disabled) return;
    const status = document.getElementById('guestWifiStatus');
    button.disabled = true;
    status.textContent = 'Đang lưu…';
    status.className = 'mt-3 text-muted';
    try {
        const response = await fetch('/admin/displaypromotion/savewifi', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json', Accept: 'application/json',
                RequestVerificationToken: form.querySelector('[name=__RequestVerificationToken]').value },
            body: JSON.stringify({ name: document.getElementById('guestWifiName').value,
                password: document.getElementById('guestWifiPassword').value, rowVersion: form.dataset.version })
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.message || result.detail || 'Không lưu được Wi-Fi. Kiểm tra quyền hoặc tải lại trang rồi thử lại.');
        form.dataset.version = result.rowVersion;
        const badge = document.getElementById('wifiSavedBadge');
        badge.textContent = result.name ? 'Đã thiết lập' : 'Chưa thiết lập';
        badge.className = 'dp-badge ' + (result.name ? 'dp-badge-active' : 'dp-badge-muted');
        status.textContent = 'Đã lưu. Màn hình khách sẽ tự cập nhật.';
        status.className = 'mt-3 text-success';
    } catch (error) {
        status.textContent = error.message || 'Không kết nối được máy chủ.';
        status.className = 'mt-3 text-danger';
    } finally { button.disabled = false; }
}

       function resetForm() {
    document.getElementById('promotionForm').reset();

    document.getElementById('promotionId').value = '0';
    document.getElementById('mediaUrl').value = '';
    document.getElementById('mediaType').value = 'text';
    document.getElementById('durationSeconds').value = '6';
    document.getElementById('sortOrder').value = '0';
    document.getElementById('priority').value = '0';
    document.getElementById('countdownToUtc').value = '';
    document.getElementById('isFlashSale').checked = false;
    document.getElementById('isFullscreen').checked = false;
    document.getElementById('isActive').checked = true;

    const img = document.getElementById('previewImage');
    const video = document.getElementById('previewVideo');

    img.removeAttribute('src');
    video.pause();
    video.removeAttribute('src');
    video.load();

    syncPreview();
}

function openCreateModal() {
    resetForm();
    document.getElementById('promotionModalTitle').textContent = 'Thêm nội dung màn hình khách';
    promotionModal.show();
}

async function openEditModal(id) {
    resetForm();

    const res = await fetch(`/admin/displaypromotion/detail?id=${id}`);
    const result = await res.json();

    if (!result.success) {
        alert(result.message || 'Không lấy được dữ liệu.');
        return;
    }

    const x = result.data;

    document.getElementById('promotionId').value = x.id;
    document.getElementById('title').value = x.title || '';
    document.getElementById('description').value = x.description || '';
    document.getElementById('mediaType').value = x.mediaType || 'text';
    document.getElementById('mediaUrl').value = x.mediaUrl || '';
    document.getElementById('durationSeconds').value = x.durationSeconds || 6;
    document.getElementById('sortOrder').value = x.sortOrder || 0;
    document.getElementById('backgroundColor').value = x.backgroundColor || '';
    document.getElementById('textColor').value = x.textColor || '';
    document.getElementById('isActive').checked = !!x.isActive;

    document.getElementById('startAt').value = toDateTimeLocal(x.startAt);
    document.getElementById('endAt').value = toDateTimeLocal(x.endAt);
            document.getElementById('priority').value = x.priority || 0;
document.getElementById('countdownToUtc').value = toDateTimeLocal(x.countdownToUtc);
document.getElementById('isFlashSale').checked = !!x.isFlashSale;
document.getElementById('isFullscreen').checked = !!x.isFullscreen;

    document.getElementById('promotionModalTitle').textContent = 'Sửa nội dung màn hình khách';

    syncPreview();
    promotionModal.show();
}

async function savePromotion(e) {
    e.preventDefault();

    const id = Number(document.getElementById('promotionId').value || 0);
    const form = document.getElementById('promotionForm');
    const formData = new FormData(form);

    formData.set('IsActive', document.getElementById('isActive').checked ? 'true' : 'false');
            formData.set('IsFlashSale', document.getElementById('isFlashSale').checked ? 'true' : 'false');
formData.set('IsFullscreen', document.getElementById('isFullscreen').checked ? 'true' : 'false');
    const url = id > 0
        ? `/admin/displaypromotion/update?id=${id}`
        : '/admin/displaypromotion/create';

    const btn = document.getElementById('btnSavePromotion');
    btn.disabled = true;
    btn.textContent = 'Đang lưu...';

    try {
        const res = await fetch(url, {
            method: 'POST',
            body: formData
        });

        const result = await res.json();

        if (!result.success) {
            alert(result.message || 'Không lưu được promotion.');
            return;
        }

        location.reload();
    }
    finally {
        btn.disabled = false;
        btn.textContent = 'Lưu nội dung';
    }
}

async function deletePromotion(id) {
    if (!confirm('Xóa nội dung này?')) return;

    const form = new FormData();
    form.append('id', id);

    const res = await fetch('/admin/displaypromotion/delete', {
        method: 'POST',
        body: form
    });

    const result = await res.json();

    if (result.success) {
        location.reload();
    }
    else {
        alert(result.message || 'Không xóa được.');
    }
}

async function toggleActive(id, isActive) {
    const row = document.querySelector(`[data-row-id="${id}"]`);
    const input = row?.querySelector('input[type=checkbox]');
    if (input) input.disabled = true;
    const form = new FormData();
    form.append('id', id);
    form.append('isActive', isActive ? 'true' : 'false');

    try {
        const res = await fetch('/admin/displaypromotion/toggleactive', { method: 'POST', body: form });
        const result = await res.json();
        if (!res.ok || !result.success) throw new Error(result.message || 'Không cập nhật được trạng thái.');
        row?.classList.toggle('is-inactive', !isActive);
        const label = row?.querySelector('.dp-state-text');
        if (label) label.textContent = isActive ? 'Đang bật' : 'Tạm ẩn';
        const rows = Array.from(document.querySelectorAll('.dp-table tbody tr'));
        const active = rows.filter(item => item.querySelector('input[type=checkbox]')?.checked).length;
        document.getElementById('promotionActiveCount').textContent = active;
        document.getElementById('promotionHiddenCount').textContent = rows.length - active;
    } catch (error) {
        if (input) input.checked = !isActive;
        alert(error.message || 'Không kết nối được máy chủ.');
    } finally { if (input) input.disabled = false; }
}

        function previewFile(input) {
    const file = input.files?.[0];

    if (!file) {
        syncPreview();
        return;
    }

    const url = URL.createObjectURL(file);

    const img = document.getElementById('previewImage');
    const video = document.getElementById('previewVideo');

    img.removeAttribute('src');
    video.pause();
    video.removeAttribute('src');
    video.load();

    if (file.type.startsWith('image/')) {
        document.getElementById('mediaType').value = 'image';
        img.setAttribute('src', url);
    }
    else if (file.type.startsWith('video/')) {
        document.getElementById('mediaType').value = 'video';
        video.setAttribute('src', url);
        video.load();
    }

    syncPreview();
}

       function syncPreview() {
    const type = document.getElementById('mediaType').value;
    const title = document.getElementById('title').value || 'Kính chào quý khách';
    const desc = document.getElementById('description').value || '';
    const mediaUrl = document.getElementById('mediaUrl').value || '';
    const bg = document.getElementById('backgroundColor').value || '';
    const color = document.getElementById('textColor').value || '';

    const box = document.getElementById('livePreview');
    const text = document.getElementById('previewText');
    const img = document.getElementById('previewImage');
    const video = document.getElementById('previewVideo');

    box.style.background = bg || '#173d31';
    box.style.color = color || '#fff';

    text.classList.add('d-none');
    img.classList.add('d-none');
    video.classList.add('d-none');

    if (type === 'image') {
        if (mediaUrl && !img.getAttribute('src')) {
            img.setAttribute('src', mediaUrl);
        }

        img.classList.remove('d-none');
        return;
    }

    if (type === 'video') {
        if (mediaUrl && !video.getAttribute('src')) {
            video.setAttribute('src', mediaUrl);
            video.load();
        }

        video.classList.remove('d-none');
        return;
    }

    text.innerHTML = `
        <div>${escapeHtml(title)}</div>
        <div style="font-size:18px;font-weight:600;margin-top:12px;opacity:.82;">
            ${escapeHtml(desc)}
        </div>
    `;

    text.classList.remove('d-none');
}

function toDateTimeLocal(value) {
    if (!value) return '';

    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return '';

    const pad = n => String(n).padStart(2, '0');

    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}
