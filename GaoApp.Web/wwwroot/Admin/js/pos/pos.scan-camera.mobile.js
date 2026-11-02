/* Phone camera input. The existing PosBarcode owner still owns scan, pricing, locks and acknowledgement. */
(function () {
    'use strict';
    const nonce = document.currentScript?.nonce || '';
    let guarding = false, dialog = null, dismiss = null;
    // Install before DOMContentLoaded/PosApp binds its keyboard owner, including legacy POS.
    window.addEventListener('keydown', event => {
        if (!guarding || event.key === 'Tab') return;
        event.stopImmediatePropagation();
        if (event.key === 'Escape') { event.preventDefault(); dismiss?.(); return; }
        if (/^F\d{1,2}$/.test(event.key) || (event.ctrlKey && event.key === 'Delete')
            || (event.key === 'Enter' && !event.target.closest?.('#posScanCameraModal button'))) event.preventDefault();
    }, true);

    function mount() {
        const root = document.getElementById('posShell'), modal = document.getElementById('posScanCameraModal');
        const openButton = document.getElementById('posScanCameraOpen'), input = document.getElementById('txtBarcode');
        if (!root || !modal || !openButton || !input || !window.bootstrap?.Modal) return;
        const byId = id => document.getElementById(id), phone = matchMedia('(max-width: 767.98px)');
        const video = byId('posScanCameraVideo'), retry = byId('posScanCameraRetry'), torchButton = byId('posScanCameraTorch');
        dialog = bootstrap.Modal.getOrCreateInstance(modal);
        let active = false, generation = 0, session = 0, expected = null, handoff = null;
        let stream = null, controls = null, decoderPromise = null, torch = false, lock = null;
        function status(text, error = false) { const node = byId('posScanCameraStatus'); node.textContent = text; node.dataset.error = String(error); }
        function stop() {
            generation++;
            const previous = controls; controls = null; previous?.stop();
            stream?.getTracks().forEach(track => track.stop()); stream = null; video.srcObject = null;
            torch = false; torchButton.hidden = true; torchButton.textContent = 'Bật đèn'; torchButton.setAttribute('aria-pressed', 'false');
        }
        function cancel() { session++; handoff = null; stop(); }
        dismiss = () => { cancel(); dialog.hide(); };
        const identity = () => [root.dataset.storeId, root.dataset.terminalId, root.dataset.userId, window.posApp?.state.business.currentDraft?.orderId].join(':');
        function available(announce = true) {
            const app = window.posApp, common = window.PosCommon;
            let message = '';
            if (!app?.state.business.currentDraft || !app.modules?.posBarcode) message = 'POS đang tải giỏ. Hãy chờ tải xong rồi quét lại.';
            else if (window.PosScanGuard?.isOpen()) message = 'Hãy xác nhận thông báo mã chưa tìm thấy trước khi quét tiếp.';
            else if (!common.isPosOnline(app.state)) message = 'Đang offline, chưa thể quét hoặc thêm sản phẩm vào giỏ.';
            else if (common.hasAnyBusyScope(app.state, ['cartMutate', 'checkout'])) message = 'POS đang xử lý giỏ hoặc thanh toán. Hãy chờ xong rồi quét lại.';
            if (message && announce) common?.showWarning(message);
            return !message;
        }
        function bindLock() {
            if (lock || !window.posApp) return;
            lock = PosCommon.registerUiLock(posApp.state, {
                target: openButton, requireOnline: true, busyScopes: ['cartMutate', 'checkout'],
                offlineMessage: 'Đang offline, chưa thể quét sản phẩm.', busyMessage: 'POS đang xử lý giỏ hoặc thanh toán.'
            });
        }
        function loadDecoder() {
            if (window.ZXingBrowser) return Promise.resolve(window.ZXingBrowser);
            if (!decoderPromise) decoderPromise = new Promise((resolve, reject) => {
                const url = new URL(modal.dataset.decoderUrl, location.href);
                if (url.origin !== location.origin || !url.pathname.endsWith('/lib/zxing-browser/0.1.5/zxing-browser.min.js')) { reject(new Error('Bộ quét phải được tải từ ứng dụng.')); return; }
                const script = document.createElement('script'); script.src = url.href; script.nonce = nonce; script.async = true;
                function failed() { script.remove(); decoderPromise = null; reject(new Error('Không tải được bộ quét. Kiểm tra kết nối rồi bấm Quét lại.')); }
                script.onload = () => window.ZXingBrowser ? resolve(window.ZXingBrowser) : failed();
                script.onerror = failed; document.head.append(script);
            });
            return decoderPromise;
        }
        async function start() {
            if (!active || !phone.matches) return;
            stop(); const turn = generation; retry.disabled = true;
            if (!available(false)) { status('POS chưa sẵn sàng hoặc đang offline/bận. Đóng cửa sổ và thử lại sau.', true); retry.disabled = false; return; }
            if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
                status('Camera cần HTTPS hoặc localhost và trình duyệt hỗ trợ. Bạn vẫn có thể đóng cửa sổ để gõ mã hoặc dùng máy quét.', true);
                retry.disabled = false; return;
            }
            status('Đang mở camera sau. Cho phép camera khi điện thoại hỏi.');
            try {
                const media = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } }, audio: false });
                if (!active || turn !== generation || !phone.matches) { media.getTracks().forEach(track => track.stop()); return; }
                stream = media;
                const ZXing = await loadDecoder();
                if (!active || turn !== generation) return;
                const track = media.getVideoTracks()[0]; torchButton.hidden = !track?.getCapabilities?.().torch;
                const reader = new ZXing.BrowserMultiFormatOneDReader(undefined, { delayBetweenScanAttempts: 180, delayBetweenScanSuccess: 600 });
                const scanning = await reader.decodeFromStream(media, video, (result, error, current) => {
                    if (!result || !active || turn !== generation) return;
                    const code = result.getText().trim(); if (!code) return;
                    // Stop and invalidate before hiding or awaiting the POS owner: one code per opening.
                    current.stop(); stop();
                    handoff = { code, session, identity: expected };
                    status('Đã đọc mã ' + code + '. Đang chuyển về giỏ…'); dialog.hide();
                });
                if (!active || turn !== generation) scanning.stop();
                else { controls = scanning; status('Đưa mã vạch vào khung. Camera dừng sau khi đọc được một mã.'); retry.disabled = false; }
            } catch (error) {
                if (!active || turn !== generation) return;
                stop();
                const errors = {
                    NotAllowedError: 'Chưa được cấp quyền camera. Cho phép camera trong cài đặt trình duyệt rồi bấm Quét lại.',
                    NotFoundError: 'Không tìm thấy camera trên thiết bị này. Bạn có thể đóng cửa sổ để gõ mã.',
                    NotReadableError: 'Camera đang bận. Đóng ứng dụng khác đang dùng camera rồi thử lại.'
                };
                status(errors[error.name] || error.message || 'Không mở được camera. Bạn vẫn có thể gõ mã.', true);
                retry.disabled = false;
            }
        }
        openButton.addEventListener('click', () => {
            if (!phone.matches || guarding || !available()) return;
            if (document.querySelector('.modal.show, .offcanvas.show, dialog[open]')) return;
            bindLock(); cancel(); expected = identity(); guarding = true; active = true;
            status(''); retry.disabled = true; dialog.show(openButton);
        });
        modal.addEventListener('shown.bs.modal', () => { if (active) void start(); });
        modal.addEventListener('hide.bs.modal', () => { active = false; stop(); });
        modal.addEventListener('hidden.bs.modal', () => {
            guarding = false; const job = handoff; handoff = null;
            if (!job) return;
            // Let Bootstrap/Prime finish closing before the native missing-code dialog can open.
            requestAnimationFrame(async () => {
                if (job.session !== session || !phone.matches) return;
                if (job.identity !== identity()) { PosCommon.showWarning('Phiên giỏ đã thay đổi. Hãy quét lại cho giỏ hiện tại.'); return; }
                if (!available()) return;
                input.value = job.code;
                try { await posApp.modules.posBarcode.scanCurrentCart(); }
                catch { PosCommon.showError('Chưa xử lý được mã vừa đọc. Hãy kiểm tra trạng thái giỏ trước khi thử lại.'); }
            });
        });
        // Explicit dismissal also cancels a handoff that has not reached the POS owner yet.
        modal.addEventListener('click', event => { if (event.target === modal || event.target.closest('[data-bs-dismiss="modal"]')) cancel(); }, true);
        retry.addEventListener('click', () => { if (!retry.disabled && active) { session++; expected = identity(); void start(); } });
        torchButton.addEventListener('click', async () => {
            const track = stream?.getVideoTracks()[0], turn = generation; if (!track || torchButton.disabled) return;
            torchButton.disabled = true;
            try {
                await track.applyConstraints({ advanced: [{ torch: !torch }] });
                if (!active || turn !== generation) return;
                torch = !torch; torchButton.textContent = torch ? 'Tắt đèn' : 'Bật đèn'; torchButton.setAttribute('aria-pressed', String(torch));
            } catch { if (active && turn === generation) status('Camera này không bật được đèn. Hãy đưa mã đến nơi sáng hơn.', true); }
            finally { torchButton.disabled = false; }
        });
        function adapt() {
            openButton.hidden = !phone.matches;
            root.toggleAttribute('data-pos-camera', phone.matches);
            if (!phone.matches) { cancel(); if (guarding) dialog.hide(); }
        }
        phone.addEventListener('change', adapt); adapt();
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) { cancel(); if (active) { retry.disabled = false; status('Camera đã dừng khi chuyển nền. Bấm Quét lại để tiếp tục.'); } }
        });
        window.addEventListener('pagehide', () => { cancel(); active = false; guarding = false; });
        window.addEventListener('pageshow', () => { adapt(); if (modal.classList.contains('show')) { active = true; guarding = true; status('Camera đã dừng. Bấm Quét lại để tiếp tục.'); } });
        window.addEventListener('pos:offline-status', () => {
            bindLock();
            if (active && !available(false)) { cancel(); retry.disabled = false; status('Camera đã dừng vì POS chưa sẵn sàng hoặc mất kết nối. Đóng cửa sổ và thử lại sau.', true); }
        });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
    else mount();
})();
