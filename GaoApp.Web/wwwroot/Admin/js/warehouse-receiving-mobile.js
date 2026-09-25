(() => {
    'use strict';
    const byId = id => document.getElementById(id), root = byId('wrdCaptureModal');
    if (!root) return;
    const esc = text => String(text ?? '').replace(/[&<>"']/g, x => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[x]));
    let modal, active = false, generation = 0, query = 0, timer, stream, controls, recognition, decoderPromise, torch = false, resultItems = [];
    let scanNext = false, saving = false, failed = false, handoff = null, shown = false;
    function message(text, error = false) { byId('wrdCaptureMessage').textContent = text; byId('wrdCaptureMessage').dataset.error = String(error); }
    function stopMedia() {
        controls?.stop(); controls = null;
        stream?.getTracks().forEach(track => track.stop()); stream = null;
        byId('wrdCameraVideo').srcObject = null;
        if (recognition) { const current = recognition; recognition = null; current.onresult = current.onerror = current.onend = null; current.abort(); }
        byId('wrdTorch').hidden = true; torch = false;
        byId('wrdTorch').textContent = 'Bật đèn'; byId('wrdTorch').setAttribute('aria-pressed','false');
        root.querySelector('.wrd-voice-orb').dataset.listening = 'false';
    }
    function invalidate() { generation++; query++; clearTimeout(timer); stopMedia(); }
    function canReceive() { return !saving && !window.ReceiptIntake?.isSaving() && !window.ReceiptQuantityControls?.isBusy() && !window.receivingHasPendingChanges?.(); }
    function show(mode) {
        if (!canReceive()) { setStatus('Hãy lưu xong thay đổi hiện tại trước khi nhận tiếp.', 'error'); return; }
        invalidate(); handoff = null; active = true;
        window.jQuery('#quickLookupInput').select2('close');
        byId('wrdCaptureTitle').textContent = mode === 'camera' ? 'Quét mã vạch' : mode === 'voice' ? 'Nói tên sản phẩm' : 'Tìm sản phẩm';
        byId('wrdCameraPanel').hidden = mode !== 'camera'; byId('wrdVoicePanel').hidden = mode !== 'voice';
        byId('wrdCaptureTerm').value = ''; byId('wrdCaptureResults').replaceChildren(); byId('wrdCaptureUnknown').hidden = true;
        message(''); modal.show();
        if (mode === 'camera') startCamera(); else if (mode === 'voice') startVoice();
        else root.addEventListener('shown.bs.modal', () => { if (active) byId('wrdCaptureTerm').focus(); }, {once:true});
    }
    function loadDecoder() {
        if (window.ZXingBrowser) return Promise.resolve(window.ZXingBrowser);
        if (!decoderPromise) decoderPromise = new Promise((resolve,reject) => {
            const script = document.createElement('script'); script.src = root.dataset.decoderUrl; script.async = true;
            script.onload = () => window.ZXingBrowser ? resolve(window.ZXingBrowser) : reject(new Error('Không khởi tạo được bộ quét.'));
            script.onerror = () => { script.remove(); decoderPromise = null; reject(new Error('Không tải được bộ quét. Hãy kiểm tra mạng và bấm Quét lại.')); };
            document.head.append(script);
        });
        return decoderPromise;
    }
    async function startCamera() {
        invalidate(); const turn = generation;
        byId('wrdCameraRetry').disabled = true;
        if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) { message('Camera cần đường dẫn HTTPS và trình duyệt hỗ trợ. Bạn vẫn có thể gõ tên hoặc mã bên dưới.', true); byId('wrdCameraRetry').disabled = false; return; }
        message('Đang mở camera sau. Cho phép truy cập camera khi điện thoại hỏi.');
        try {
            const media = await navigator.mediaDevices.getUserMedia({video:{facingMode:{ideal:'environment'},width:{ideal:1280},height:{ideal:720}},audio:false});
            if (!active || turn !== generation) { media.getTracks().forEach(x=>x.stop()); return; }
            stream = media;
            const ZXing = await loadDecoder(); if (!active || turn !== generation) return;
            const track = media.getVideoTracks()[0];
            byId('wrdTorch').hidden = !track?.getCapabilities?.().torch;
            const reader = new ZXing.BrowserMultiFormatOneDReader(undefined, {delayBetweenScanAttempts:180,delayBetweenScanSuccess:600});
            const scanning = await reader.decodeFromStream(media, byId('wrdCameraVideo'), (result, error, current) => {
                if (!result || !active || turn !== generation) return;
                // Invalidate before any async work: successive video frames cannot add twice.
                current.stop(); generation++; stopMedia();
                const code = result.getText().trim(); byId('wrdCaptureTerm').value = code;
                message(`Đã nhận mã ${code}. Đang tìm sản phẩm…`);
                search(code, true);
            });
            if (!active || turn !== generation) scanning.stop(); else { controls = scanning; message('Đưa mã vạch vào khung. Camera sẽ dừng khi nhận được mã.'); }
        } catch (error) {
            if (!active || turn !== generation) return;
            stopMedia();
            const detail = {NotAllowedError:'Chưa được cấp quyền camera. Cho phép camera trong cài đặt trình duyệt rồi bấm Quét lại.',NotFoundError:'Không tìm thấy camera trên thiết bị này.',NotReadableError:'Camera đang bận. Đóng ứng dụng khác đang dùng camera rồi thử lại.'};
            message(detail[error.name] || error.message || 'Không mở được camera. Bạn có thể gõ tên hoặc mã.', true);
        } finally { if (active) byId('wrdCameraRetry').disabled = false; }
    }
    function startVoice() {
        invalidate(); const turn = generation;
        const Speech = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!Speech) { message('Trình duyệt này chưa hỗ trợ tìm bằng giọng nói. Dùng nút micro trên bàn phím điện thoại hoặc gõ tên hàng.', true); return; }
        try {
            const speech = new Speech(); recognition = speech; speech.lang = 'vi-VN'; speech.interimResults = true; speech.continuous = false; speech.maxAlternatives = 1;
            speech.onresult = event => {
                if (!active || turn !== generation || recognition !== speech) return;
                let final = '', interim = '';
                for (let i = 0; i < event.results.length; i++) { if (event.results[i].isFinal) final += event.results[i][0].transcript; else interim += event.results[i][0].transcript; }
                byId('wrdCaptureTerm').value = (final || interim).trim().slice(0,100);
                if (final.trim()) { speech.stop(); search(final.trim(), false); }
            };
            speech.onerror = event => {
                if (!active || turn !== generation) return;
                const details = {'not-allowed':'Chưa được cấp quyền micro. Cho phép micro hoặc gõ tên hàng.','no-speech':'Chưa nghe rõ. Bấm Nói lại và đọc gần micro hơn.',network:'Không kết nối được dịch vụ giọng nói của trình duyệt. Hãy gõ tên hoặc thử lại.','audio-capture':'Không mở được micro trên thiết bị.'};
                message(details[event.error] || 'Không nhận được giọng nói. Bạn có thể gõ tìm bên dưới.', true);
            };
            speech.onend = () => { if (recognition === speech) { recognition = null; root.querySelector('.wrd-voice-orb').dataset.listening = 'false'; } };
            speech.start(); root.querySelector('.wrd-voice-orb').dataset.listening = 'true';
            message('Đang nghe tiếng Việt… Kết quả sẽ hiện để bạn chọn. Tính năng có thể cần Internet.');
        } catch { stopMedia(); message('Không mở được nhận diện giọng nói. Hãy gõ tên sản phẩm.', true); }
    }
    async function search(term, scanned = false) {
        clearTimeout(timer); term = term.trim(); const request = ++query;
        byId('wrdCaptureResults').replaceChildren(); byId('wrdCaptureUnknown').hidden = true; resultItems = [];
        if (!term) { message('Nhập tên hoặc mã để tìm sản phẩm.'); return; }
        message('Đang tìm sản phẩm…');
        try {
            const response = await fetch(`${window.ReceiptBarcodeProposals.lookupUrl}?term=${encodeURIComponent(term)}`,{cache:'no-store'});
            if (!response.ok) throw new Error('Không tìm được lúc này. Kiểm tra kết nối rồi bấm tìm lại.');
            const data = await response.json(); if (!active || request !== query) return;
            let items = data.results || [];
            if (scanned) {
                const exact = items.filter(item => [item.barcode,item.sku].some(x=>x && String(x).toUpperCase() === term.toUpperCase()));
                if (exact.length === 1) { select(exact[0]); return; }
                if (exact.length) items = exact;
            } else items = window.groupLookupResultsForTextSearch(items, 'tìm theo tên');
            resultItems = items;
            byId('wrdCaptureResults').innerHTML = items.map((item,index)=>`<button type="button" class="wrd-capture-result" data-capture-result="${index}">${item.imageUrl?`<img src="${esc(item.imageUrl)}" alt="" />`:''}<span><strong>${esc(item.productName || item.text)}</strong><small>${esc(item.isGroupedVariant ? item.units.map(x=>x.unitName).join(' · ') : `${item.unitName || ''} · ${item.barcode || item.sku || ''}`)}</small></span></button>`).join('');
            message(items.length ? (scanned ? 'Chọn đúng sản phẩm và đơn vị của mã vừa quét.' : `Tìm thấy ${items.length} sản phẩm. Chạm để chọn đơn vị và số lượng.`) : `Chưa tìm thấy “${term}”. Kiểm tra lại mã hoặc ghi nhận hàng chưa có.`);
            byId('wrdCaptureUnknown').hidden = items.length > 0;
        } catch (error) { if (active && request === query) message(error.message, true); }
    }
    function select(item) {
        if (!canReceive()) { message('Hãy chờ lưu xong hàng nhập hiện tại.', true); return; }
        handoff = () => item.isGroupedVariant ? window.openGroupedProductPopup(item) : window.openQtyPopup(item);
        if (shown) modal.hide();
        else root.addEventListener('shown.bs.modal',()=>{if(active && handoff)modal.hide();},{once:true});
    }
    function setStatus(text, state = 'saved') { byId('wrdSaveStatus').textContent = text; byId('wrdSaveStatus').dataset.state = state; }
    function start() {
        // Keep the camera dialog outside sticky panels and their stacking contexts.
        document.body.append(root); modal = bootstrap.Modal.getOrCreateInstance(root);
        document.body.classList.add('wrd-mobile-enabled');
        byId('wrdRetrySave').addEventListener('click',()=>window.receivingRetryChanges?.());
        byId('wrdOpenCamera').addEventListener('click',()=>show('camera'));
        byId('wrdOpenVoice').addEventListener('click',()=>show('voice'));
        byId('wrdCameraRetry').addEventListener('click', startCamera); byId('wrdVoiceRetry').addEventListener('click',startVoice);
        root.addEventListener('shown.bs.modal',()=>{shown=true;});
        root.addEventListener('hide.bs.modal',()=>{ active = false; shown=false; invalidate(); });
        root.addEventListener('hidden.bs.modal',()=>{ const next = handoff; handoff = null; next?.(); });
        byId('wrdTorch').addEventListener('click', async()=>{
            const track = stream?.getVideoTracks()[0]; if (!track) return;
            try { await track.applyConstraints({advanced:[{torch:!torch}]}); torch = !torch; byId('wrdTorch').textContent = torch ? 'Tắt đèn' : 'Bật đèn'; byId('wrdTorch').setAttribute('aria-pressed',String(torch)); }
            catch { message('Camera này không bật được đèn. Thử di chuyển đến chỗ sáng hơn.', true); }
        });
        byId('wrdCaptureTerm').addEventListener('input',()=>{ invalidate(); const term=byId('wrdCaptureTerm').value; byId('wrdCaptureResults').replaceChildren(); timer=setTimeout(()=>search(term),220); });
        byId('wrdCaptureSearch').addEventListener('submit',event=>{event.preventDefault(); invalidate(); search(byId('wrdCaptureTerm').value);});
        byId('wrdCaptureResults').addEventListener('click',event=>{const button=event.target.closest('[data-capture-result]');if(button)select(resultItems[Number(button.dataset.captureResult)]);});
        byId('wrdCaptureUnknown').addEventListener('click',()=>{
            const term=byId('wrdCaptureTerm').value.trim(), code=/^[!-~]+$/.test(term)&&/\d/.test(term);
            handoff=()=>{ if (!window.ReceiptIntake.open(code?term:'',{tab:'new',name:code?'':term})) setStatus('Chưa mở được hàng mới. Hãy chờ tải phiếu xong rồi thử lại.', 'error'); }; modal.hide();
        });
        document.querySelectorAll('[data-popup-step]').forEach(button=>button.addEventListener('click',()=>{
            if (saving) return; const input=byId('popupQuickQty'); input.value=String(Math.max(1,Number(input.value||0)+Number(button.dataset.popupStep))); input.dispatchEvent(new Event('input',{bubbles:true}));
        }));
        byId('wrdAddAndScan')?.addEventListener('click',async()=>{if(saving)return;scanNext=true;const saved=await window.addReceivingLine();if(!saved)scanNext=false;});
        document.addEventListener('receiving:save',event=>{
            const info=event.detail; saving=info.state==='saving'; failed=info.state==='error';
            byId('wrdRetrySave').hidden = !failed;
            setStatus(info.message || (saving?'Đang ghi nhận…':failed?'Chưa xác nhận lưu — hãy thử lại.':'Đã lưu thành công'),info.state);
            byId('wrdAddAndScan').disabled=saving;
            if (scanNext && info.state==='saved') {
                scanNext=false;
                const qtyModal=byId('quickAddProductModal');
                if (qtyModal.classList.contains('show') || qtyModal.style.display==='block') qtyModal.addEventListener('hidden.bs.modal',()=>show('camera'),{once:true});
                else show('camera');
            }
        });
        window.addEventListener('offline',()=>setStatus('Mất mạng. Thay đổi chưa gửi được cần lưu lại khi có mạng.', 'error'));
        window.addEventListener('online',()=>setStatus(failed?'Đã có mạng. Bấm lưu lại thay đổi chưa thành công.':'Đã kết nối. Sẵn sàng nhận hàng.',failed?'error':'saved'));
        document.addEventListener('visibilitychange',()=>{if(document.hidden){invalidate();if(active)message('Camera và micro đã dừng. Bấm Quét lại hoặc Nói lại để tiếp tục.');}});
        window.addEventListener('pagehide',invalidate);
        window.addEventListener('beforeunload',event=>{if(saving || failed || window.receivingHasPendingChanges?.() || window.ReceiptIntake?.hasDraft?.()){event.preventDefault();event.returnValue='';}});
    }
    window.WarehouseReceivingMobile = {openCamera:()=>show('camera')};
    if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
