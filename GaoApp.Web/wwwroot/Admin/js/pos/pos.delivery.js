(function () {
    'use strict';
    document.addEventListener('DOMContentLoaded', () => {
        const modal = document.getElementById('deliveryModal');
        const open = document.getElementById('btnOpenDelivery');
        if (!modal || !open) return;
        const el = id => document.getElementById(id);
        const shell = el('posShell');
        const storageKey = ['gao.delivery.intent.v1', shell.dataset.storeId, shell.dataset.terminalId, modal.dataset.actorId].join(':');
        const fields = ['deliveryName', 'deliveryPhone', 'deliveryAddress', 'deliveryNote'];
        let snapshot = null, intent = null, busy = false;
        const money = v => Number(v).toLocaleString('vi-VN') + ' đ';
        const online = () => {
            const status = window.PosOffline?.status();
            return navigator.onLine && !window.PosOffline?.localMode() && !(status?.pending > 0) && !(status?.context && !status.writer);
        };
        function message(text, type = 'info') { el('deliveryMessage').textContent = text; el('deliveryMessage').className = 'alert alert-' + type; }
        function buttons() {
            const completed = !el('deliveryResult').hidden;
            el('deliverySubmit').disabled = busy || (!snapshot && !intent);
            if (completed) el('deliverySubmit').textContent = 'Đã tạo đơn';
            el('deliveryRefresh').disabled = busy || !!intent;
            el('deliveryFields').disabled = busy || !!intent || completed;
        }
        async function request(path, body) {
            const response = await fetch('/admin/api/deliveries/' + path, { method: body ? 'POST' : 'GET', credentials: 'same-origin',
                cache: 'no-store', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('meta[name="request-verification-token"]')?.content || '' },
                body: body ? JSON.stringify(body) : undefined });
            let data;
            try { data = await response.json(); } catch (error) {
                if (response.ok) throw new Error('Chưa đọc được kết quả xác nhận từ máy chủ.');
                data = { message: response.status === 403 ? 'Bạn không còn quyền tạo đơn giao.' : 'Không thể xử lý đơn giao. Kiểm tra phiên đăng nhập.' };
            }
            if (!response.ok) { const error = new Error(data.message || 'Không thể xử lý đơn giao.'); error.status = response.status; error.code = data.code; throw error; }
            return data;
        }
        function fill(body) { fields.forEach((id, i) => el(id).value = [body.recipientName, body.recipientPhone, body.recipientAddress, body.note][i] || ''); }
        async function load() {
            snapshot = null; el('deliveryResult').hidden = true; el('deliveryCart').replaceChildren();
            try {
                intent = JSON.parse(sessionStorage.getItem(storageKey) || 'null');
                if (intent) { fill(intent); el('deliverySubmit').textContent = 'Kiểm tra / gửi lại yêu cầu cũ'; message('Có yêu cầu chưa xác nhận của giỏ #' + intent.sourceCartId + '. Gửi lại để nhận kết quả; cùng mã yêu cầu sẽ không tạo trùng đơn.', 'warning'); return; }
                el('deliverySubmit').textContent = 'Tạo đơn chờ soạn'; fill({});
                if (!online()) throw new Error(window.PosOffline?.status()?.message || 'Cần có mạng và đồng bộ xong giỏ POS trước khi tạo đơn giao.');
                message('Đang lấy nội dung giỏ…'); snapshot = await request('current-cart'); fill(snapshot);
                const title = document.createElement('strong'); title.textContent = 'Giỏ #' + snapshot.sourceCartId + ' · ' + snapshot.warehouseName;
                el('deliveryCart').append(title);
                for (const line of snapshot.lines) { const row = document.createElement('div'); row.textContent = line.itemName + ' · ' + line.quantity + ' ' + line.unitName + ' · ' + money(line.lineTotal); el('deliveryCart').append(row); }
                message('Giá trị dự kiến ' + money(snapshot.quotedTotal) + '. Tạo đơn chưa ghi nhận thanh toán.');
            } catch (error) { message(error.message, 'warning'); }
            finally { buttons(); }
        }
        open.addEventListener('click', () => {
            const payment = document.getElementById('paymentModal');
            if (payment) bootstrap.Modal.getInstance(payment)?.hide();
            bootstrap.Modal.getOrCreateInstance(modal).show(); load();
        });
        el('deliveryRefresh').addEventListener('click', load);
        modal.addEventListener('shown.bs.modal', () => { if (!intent) el('deliveryName').focus(); });
        // Isolate this form from the existing global POS keyboard owner.
        window.addEventListener('keydown', event => {
            if (!modal.classList.contains('show')) return;
            event.stopImmediatePropagation();
            if (/^F\d+$/.test(event.key) || (event.ctrlKey && event.key === 'Delete')) event.preventDefault();
            if (event.key === 'Escape') { event.preventDefault(); if (!busy) bootstrap.Modal.getInstance(modal)?.hide(); }
        }, true);
        el('deliveryForm').addEventListener('submit', async event => {
            event.preventDefault(); if (busy) return;
            if (!online()) { message(window.PosOffline?.status()?.message || 'Cần có mạng và đồng bộ xong POS. Yêu cầu giao không được xếp hàng offline.', 'warning'); return; }
            busy = true; buttons(); el('deliveryResult').hidden = true;
            try {
                if (!intent) {
                    if (!snapshot) throw new Error('Tải lại giỏ trước khi tạo đơn.');
                    intent = { clientRequestId: crypto.randomUUID(), sourceCartId: snapshot.sourceCartId, expectedVersion: snapshot.version, expectedFingerprint: snapshot.fingerprint,
                        recipientName: el('deliveryName').value.trim(), recipientPhone: el('deliveryPhone').value.trim(), recipientAddress: el('deliveryAddress').value.trim(), note: el('deliveryNote').value.trim() || null };
                    try { sessionStorage.setItem(storageKey, JSON.stringify(intent)); } catch (error) { intent = null; throw new Error('Không lưu được mã yêu cầu. Cho phép lưu phiên trình duyệt rồi thử lại.'); }
                }
                buttons(); message('Đang tạo đơn, vui lòng đợi xác nhận…');
                const result = await request('from-current-cart', intent);
                if (!result.delivery?.id || !result.billUrl || !result.lookupUrl) throw new Error('Chưa nhận đủ kết quả xác nhận.');
                sessionStorage.removeItem(storageKey); intent = null; snapshot = null;
                el('deliveryCreatedCode').textContent = result.delivery.code + ' · Chờ soạn';
                el('deliveryBillLink').href = result.billUrl; el('deliveryLookupLink').href = result.lookupUrl;
                el('deliveryResult').hidden = false; message('Đã tạo đơn giao thành công.', 'success');
                try { await window.posApp?.loadScreen(); } catch (error) { message('Đã tạo đơn. Tải lại POS để xem giỏ mới; có thể mở phiếu bên dưới.', 'success'); }
            } catch (error) {
                // Only explicit transaction rejections can discard the intent. Network/5xx failures remain ambiguous.
                if ([400, 403, 404, 409].includes(error.status) && error.code !== 'REQUEST_KEY_CONFLICT') { sessionStorage.removeItem(storageKey); intent = null; snapshot = null; }
                message(error.message + (intent ? ' Chưa xác nhận kết quả; bấm gửi lại cùng yêu cầu, kể cả sau khi tải lại trang.' : ' Tải lại giỏ để kiểm tra.'), 'warning');
                if (intent) el('deliverySubmit').textContent = 'Kiểm tra / gửi lại yêu cầu cũ';
            } finally { busy = false; buttons(); }
        });
    });
})();
