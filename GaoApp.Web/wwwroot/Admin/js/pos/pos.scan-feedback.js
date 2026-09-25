/* Successful scan receipts belong to a cart, never to a transient row highlight.
 * This module presents acknowledged drafts; it does not mutate quantities or prices. */
window.PosScanFeedback = (() => {
    'use strict';
    const el = id => document.getElementById(id);
    const root = el('posScanFeedback'), shell = el('posShell');
    if (!root || !shell) return null;
    const number = value => Number(value || 0).toLocaleString('vi-VN', { maximumFractionDigits: 6 });
    const lines = draft => (draft?.lines || []).filter(line => !line.isPromotionGift);
    const key = line => [line.variantId, line.sellingUnitId || line.sellingUnitName || line.unitName || '', line.multiplier || 1].join('|');
    const orderId = draft => String(draft?.orderId || draft?.id || '');
    const storageKey = ['pos-scan-history-v1', shell.dataset.storeId, shell.dataset.terminalId, shell.dataset.userId].join(':');
    let carts = {}, draft = null, receipts = [], active = null, retry = null, flashTimer;
    try { carts = JSON.parse(sessionStorage.getItem(storageKey) || '{}'); if (!carts || Array.isArray(carts) || typeof carts !== 'object') carts = {}; } catch { carts = {}; }
    function persist() {
        // Bound storage to recent carts; failure to store presentation must never fail a sale.
        const entries = Object.entries(carts).sort((a,b) => (b[1]?.[0]?.at || '').localeCompare(a[1]?.[0]?.at || '')).slice(0,20);
        carts = Object.fromEntries(entries);
        try { sessionStorage.setItem(storageKey, JSON.stringify(carts)); } catch { /* Session-only fallback. */ }
    }
    function status(state, message) { root.dataset.state = state; el('posScanStatus').textContent = message; }
    function savedStatus() { status('saved', window.PosOffline?.status()?.pending ? 'Đã lưu tại quầy · Chờ đồng bộ' : 'Đã ghi nhận'); }
    function write(id, value) { el(id).textContent = value; }
    function currentLine(receipt) { return lines(draft).find(line => key(line) === receipt.key); }
    function render() {
        const latest = receipts[0];
        el('posScanLast').hidden = !latest;
        write('posScanCount', receipts.length);
        if (latest) {
            const line = currentLine(latest);
            write('posScanName', latest.name);
            write('posScanMeta', [latest.barcode ? 'Mã: ' + latest.barcode : '', latest.packing].filter(Boolean).join(' · '));
            write('posScanAdded', 'Vừa cộng ' + number(latest.added) + ' ' + latest.unit);
            write('posScanTransition', number(latest.before) + ' → ' + number(latest.after) + ' ' + latest.unit);
            write('posScanTime', new Date(latest.at).toLocaleTimeString('vi-VN'));
            el('posScanTime').dateTime = latest.at;
            el('posScanCurrent').hidden = !!line && Number(line.quantity) === latest.after;
            write('posScanCurrent', line ? 'Hiện trong giỏ: ' + number(line.quantity) + ' ' + latest.unit : 'Sản phẩm này đã được xóa khỏi giỏ');
            const image = el('posScanImage');
            if (image.getAttribute('src') !== (latest.image || '')) {
                image.hidden = true; el('posScanImageFallback').hidden = false; el('posScanPhoto').disabled = true;
                if (latest.image) image.src = latest.image; else image.removeAttribute('src');
            }
        }
        const list = el('posScanHistoryList'); list.replaceChildren();
        el('posScanHistoryEmpty').hidden = receipts.length > 0;
        for (const receipt of receipts) {
            const li = document.createElement('li'); li.dataset.scanReceipt = receipt.id;
            const top = document.createElement('div'), time = document.createElement('time'), qty = document.createElement('b');
            time.dateTime = receipt.at; time.textContent = new Date(receipt.at).toLocaleTimeString('vi-VN');
            qty.textContent = '+' + number(receipt.added) + ' ' + receipt.unit; top.append(time, qty);
            const name = document.createElement('strong'); name.textContent = receipt.name;
            const meta = document.createElement('small'); meta.textContent = [receipt.barcode, receipt.packing].filter(Boolean).join(' · ');
            const change = document.createElement('small'); change.textContent = number(receipt.before) + ' → ' + number(receipt.after) + ' ' + receipt.unit + (currentLine(receipt) ? '' : ' · Đã xóa khỏi giỏ');
            li.append(top, name, meta, change); list.append(li);
        }
    }
    function reorder() {
        const body = el('currentDraftBody'); if (!body) return;
        const ranks = new Map(); receipts.forEach(receipt => { if (!ranks.has(receipt.key)) ranks.set(receipt.key, ranks.size); });
        const rowKeys = new Map(lines(draft).map(line => [String(line.lineId), key(line)]));
        const rows = [...body.querySelectorAll('tr[data-line-id]')];
        const ordered = rows.map((row,index) => ({ row,index,rank:ranks.get(rowKeys.get(row.dataset.lineId)) ?? Infinity }))
            .sort((a,b) => a.rank - b.rank || a.index - b.index).map(x => x.row);
        const scroll = body.closest('.pos-cart-scroll'), top = scroll?.scrollTop;
        if (ordered.some((row,index) => row !== rows[index])) ordered.forEach(row => body.append(row));
        if (scroll) scroll.scrollTop = top;
    }
    function sync(nextDraft) {
        const changed = orderId(draft) !== orderId(nextDraft);
        draft = nextDraft;
        // Offline order IDs can become server IDs after synchronisation.
        for (const storedId of Object.keys(carts)) {
            const mapped = String(window.PosOffline?.resolveOrderId?.(Number(storedId)) ?? storedId);
            if (mapped !== storedId) { carts[mapped] = [...(carts[mapped] || []), ...carts[storedId]].sort((a,b) => b.at.localeCompare(a.at)).filter((x,i,a) => a.findIndex(y => y.id === x.id) === i).slice(0,10); delete carts[storedId]; persist(); }
        }
        receipts = Array.isArray(carts[orderId(draft)]) ? carts[orderId(draft)].filter(x => x?.id && x.key && x.added > 0).slice(0,10) : [];
        if (changed && !active) { retry = null; el('posScanRetry').hidden = true; status('ready', 'Sẵn sàng quét sản phẩm'); }
        reorder(); render();
    }
    function begin(beforeDraft) {
        active = { id: crypto.randomUUID(), order: orderId(beforeDraft), before: new Map(lines(beforeDraft).map(line => [key(line), Number(line.quantity)])) };
        retry = null; el('posScanRetry').hidden = true;
        root.classList.remove('pos-scan-confirmed');
        status('saving', 'Đang ghi nhận…');
        return active;
    }
    function preserveCartScroll(action) {
        const scroll = el('currentDraftBody')?.closest('.pos-cart-scroll'), top = scroll?.scrollTop;
        // Feedback and badge layout must finish before restoring the offset. An
        // intermediate shorter layout can clamp it even when the final one fits.
        try { action(); } finally { if (scroll) scroll.scrollTop = top; }
    }
    function confirmed(attempt, nextDraft) {
        if (!attempt || active !== attempt) return;
        preserveCartScroll(() => {
            active = null; sync(nextDraft);
            const changed = lines(nextDraft).map(line => ({ line, before: attempt.before.get(key(line)) || 0 }))
                .filter(x => Number(x.line.quantity) > x.before);
            // Never label an unrelated gift, price change or ambiguous concurrent update as a scan.
            if (changed.length !== 1) { status('ready', 'Giỏ đã cập nhật. Kiểm tra số lượng trong giỏ.'); return; }
            const { line, before } = changed[0], unit = line.sellingUnitName || line.unitName || line.baseUnitName || 'đơn vị';
            const receipt = { id: attempt.id, key: key(line), name: line.productVariantName || line.itemName || '',
                barcode: line.scannedBarcode || line.barcode || line.sku || '', unit,
                packing: Number(line.multiplier) > 1 ? `${unit} (${number(line.multiplier)} ${line.baseUnitName || 'đơn vị gốc'})` : unit,
                image: line.imageThumbUrl || line.imageUrl || '', fullImage: line.imageUrl || line.imageThumbUrl || '',
                before, after: Number(line.quantity), added: Number((Number(line.quantity) - before).toFixed(6)), at: new Date().toISOString() };
            receipts = [receipt, ...receipts.filter(x => x.id !== receipt.id)].slice(0,10);
            carts[orderId(nextDraft)] = receipts; persist(); reorder(); render();
            savedStatus();
            clearTimeout(flashTimer);
            document.querySelectorAll('.pos-scan-received').forEach(row => row.classList.remove('pos-scan-received'));
            document.querySelectorAll('.pos-scan-badge').forEach(badge => badge.remove());
            const row = el('currentDraftBody')?.querySelector(`tr[data-line-id="${Number(line.lineId)}"]`);
            if (row) {
                window.__posLastTouchedLineId = Number(line.lineId);
                row.classList.add('pos-scan-received');
                const badge = document.createElement('span'); badge.className = 'pos-scan-badge'; badge.textContent = 'Vừa quét +' + number(receipt.added);
                row.querySelector('.pos-line-name')?.append(badge);
                if (!matchMedia('(prefers-reduced-motion: reduce)').matches) row.querySelector('.qty-input')?.animate([{ opacity:.45 },{ opacity:1 }], { duration:300 });
            }
            void root.offsetWidth; root.classList.add('pos-scan-confirmed');
            // Capture the user's current offset at expiry, not the old scan offset.
            flashTimer = setTimeout(() => preserveCartScroll(() => { root.classList.remove('pos-scan-confirmed'); row?.classList.remove('pos-scan-received'); row?.querySelector('.pos-scan-badge')?.remove(); }),1500);
        });
    }
    function failed(attempt, error, retryAction) {
        if (attempt && active !== attempt) return;
        active = null;
        const code = Number(error?.status);
        const rejected = code >= 400 && code < 500 && code !== 408 && error?.raw?.errorCode !== 'POS_OFFLINE_REVIEW';
        status('error', (rejected ? 'Chưa ghi nhận — ' : 'Chưa xác định kết quả — ') + (error?.message || 'Kiểm tra giỏ trước khi quét lại.'));
        // Retry only a rejected command. An uncertain response must use the existing
        // offline journal's recovery, never manufacture another sale operation.
        retry = rejected ? retryAction : null;
        el('posScanRetry').hidden = !retry;
    }
    el('posScanRetry').addEventListener('click', () => { const action = retry; retry = null; el('posScanRetry').hidden = true; action?.(); });
    window.addEventListener('pos:offline-status', () => { if (root.dataset.state === 'saved') savedStatus(); });
    const sheet = el('posScanHistory');
    // Keep Bootstrap overlays outside grid/overflow containers and their transforms.
    (shell.closest('.pos-prime-layout') || document.body).append(sheet);
    el('posScanHistoryOpen').addEventListener('click', () => {
        if (document.querySelector('.modal.show,.offcanvas.show')) return;
        window.bootstrap?.Offcanvas.getOrCreateInstance(sheet).show(el('posScanHistoryOpen'));
    });
    sheet.addEventListener('show.bs.offcanvas', () => el('posScanHistoryOpen').setAttribute('aria-expanded','true'));
    sheet.addEventListener('hidden.bs.offcanvas', () => { el('posScanHistoryOpen').setAttribute('aria-expanded','false'); el('posScanHistoryOpen').focus({ preventScroll:true }); });
    // Also protect legacy POS shortcuts while the history drawer owns focus.
    window.addEventListener('keydown', event => {
        if (!sheet.matches('.show,.showing')) return;
        if (/^F\d+$/.test(event.key) || event.ctrlKey && event.key === 'Delete') { event.preventDefault(); event.stopImmediatePropagation(); }
        else if (event.key === 'Enter') event.stopImmediatePropagation();
    },true);
    el('posScanImage').addEventListener('load', () => { el('posScanImage').hidden = false; el('posScanImageFallback').hidden = true; el('posScanPhoto').disabled = false; });
    el('posScanImage').addEventListener('error', () => { el('posScanImage').hidden = true; el('posScanImageFallback').hidden = false; el('posScanPhoto').disabled = true; });
    el('posScanPhoto').addEventListener('click', () => {
        const latest = receipts[0]; if (!latest?.fullImage) return;
        const image = el('imagePreviewEl'), modal = el('imagePreviewModal');
        if (!image || !modal) return;
        image.src = latest.fullImage; image.alt = latest.name;
        window.bootstrap?.Modal.getOrCreateInstance(modal).show(el('posScanPhoto'));
    });
    return { begin, confirmed, failed, sync, isPending: () => !!active };
})();
