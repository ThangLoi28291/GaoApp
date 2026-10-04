(() => {
    'use strict';
    const states = new Map();
    let timer, pending, error = '', savedAt = '', held = 0;
    const inputs = () => [...document.querySelectorAll('#commercialApprovalWorkbench .commercial-unit-price')];
    const dirty = () => [...states.values()].filter(s => s.value !== s.saved);
    const valid = s => s.value.trim() !== '' && Number.isFinite(Number(s.value)) && Number(s.value) > 0 && Number(s.value) <= 99999999999999.99;
    function render() {
        const changed = dirty();
        for (const s of states.values()) {
            const label = s.input.closest('.commercial-line')?.querySelector('.commercial-price-save-state');
            if (!label) continue;
            const unsaved = s.value !== s.saved;
            label.textContent = unsaved ? (!valid(s) ? 'Chưa lưu — nhập giá hợp lệ lớn hơn 0' : pending ? 'Đang lưu…' : error ? 'Chưa lưu — bấm Lưu nháp ngay để thử lại' : 'Chờ lưu…') : (s.done ? 'Đã lưu ✓' : '');
            label.className = 'small mt-1 commercial-price-save-state ' + (unsaved ? (error || !valid(s) ? 'text-danger' : 'text-muted') : 'text-success');
        }
        const label = document.getElementById('receiptPriceDraftStatus');
        if (label) {
            label.textContent = pending ? 'Đang lưu giá nháp…' : changed.length ? (error || `Còn ${changed.length} dòng giá chưa lưu`) : savedAt ? `Đã lưu tất cả thay đổi giá · Lần cuối ${savedAt}` : 'Giá nhập được tự động lưu nháp';
            label.className = 'small d-block ' + (changed.length && error ? 'text-danger' : changed.length ? 'text-warning' : 'text-success');
        }
        const footer = document.querySelector('.sd-manager-save-state span');
        if (footer) { footer.textContent = pending ? 'Đang lưu giá…' : changed.length ? 'Giá chưa lưu' : 'Giá đã lưu'; footer.className = changed.length ? 'text-danger' : ''; }
        const button = document.getElementById('receiptPriceDraftSave');
        if (button) button.disabled = !!pending || held > 0;
    }
    function schedule() {
        clearTimeout(timer);
        if (!held) timer = setTimeout(() => flush(), 1000);
    }
    async function flush() {
        clearTimeout(timer);
        if (pending) { await pending; if (error) return false; return flush(); }
        const batch = dirty().filter(valid).slice(0, 500).map(s => ({ s, value: s.value, version: s.version }));
        if (!batch.length) { render(); return dirty().length === 0; }
        error = '';
        pending = (async () => {
            const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 20000);
            try {
                const response = await fetch(`/admin/api/stock-documents/${window.stockDocumentPage.documentId}/price-draft`, {
                    method: 'POST', signal: controller.signal,
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || '' },
                    body: JSON.stringify({ rowVersion: window.stockDocumentRowVersion?.current() || window.stockDocumentPage.rowVersion,
                        lines: batch.map(x => ({ stockDocumentLineId: x.s.id, rowVersion: x.version, unitPriceBeforeVat: Number(x.value) })) })
                });
                const data = await response.json().catch(() => ({}));
                if (!response.ok || !data.rowVersion || !data.lineVersions || !data.lineAmountsBeforeVat) throw new Error(data.message || 'Chưa lưu được giá nháp. Kiểm tra kết nối rồi bấm Lưu nháp ngay.');
                for (const x of batch) {
                    x.s.saved = x.value; x.s.version = data.lineVersions[x.s.id]; x.s.done = true;
                    x.s.input.dataset.lineVersion = x.s.version;
                    const row = x.s.input.closest('.commercial-line');
                    row.dataset.exactUnitPrice = x.value;
                    row.dataset.exactBeforeVat = String(data.lineAmountsBeforeVat[x.s.id]);
                }
                window.stockDocumentRowVersion?.update(data.rowVersion);
                window.stockDocumentPage.rowVersion = data.rowVersion;
                window.GaoReceiptPricingAllocation?.pricesSaved(data);
                document.getElementById('commercialHasVat')?.dispatchEvent(new Event('change'));
                savedAt = new Date(data.savedAtUtc).toLocaleTimeString('vi-VN');
            } catch (e) {
                error = e.name === 'AbortError' ? 'Chưa xác nhận được kết quả lưu. Giá đang nhập vẫn được giữ ở trang này; kiểm tra kết nối trước khi rời trang.' : e instanceof TypeError ? 'Không kết nối được máy chủ. Giá chưa được lưu; kiểm tra mạng rồi bấm Lưu nháp ngay.' : e.message;
            } finally { clearTimeout(timeout); }
        })();
        render(); await pending; pending = null; render();
        if (error) return false;
        if (dirty().some(valid)) return flush();
        return dirty().length === 0;
    }
    async function hold() {
        held++;
        inputs().forEach(input => { input.disabled = true; });
        const release = () => { held = Math.max(0, held - 1); if (!held) inputs().forEach(input => { input.disabled = false; }); render(); if (dirty().length && !error) schedule(); };
        if (!await flush()) { release(); throw new Error(error || 'Còn giá nhập chưa hợp lệ hoặc chưa lưu. Kiểm tra các dòng được đánh dấu trước khi tiếp tục.'); }
        return release;
    }
    function init() {
        const bar = document.getElementById('receiptPriceDraftBar');
        if (!bar) return;
        if (!window.stockDocumentPage?.canApproveCommercial) { bar.hidden = true; return; }
        for (const input of inputs()) {
            if (input.dataset.draftBound) continue;
            input.dataset.draftBound = 'true';
            const id = Number(input.closest('.commercial-line').dataset.lineId);
            const previous = states.get(id);
            const state = previous && previous.value !== previous.saved ? previous : { id, saved: input.value, value: input.value, version: input.dataset.lineVersion };
            state.input = input; states.set(id, state);
            input.addEventListener('input', () => { state.value = input.value; render(); schedule(); });
            input.addEventListener('blur', () => { if (!held && state.value !== state.saved) flush(); });
        }
        const button = document.getElementById('receiptPriceDraftSave');
        if (button && !button.dataset.bound) { button.dataset.bound = 'true'; button.addEventListener('click', () => flush()); }
        render();
    }
    window.addEventListener('beforeunload', event => { if (pending || dirty().length) { event.preventDefault(); event.returnValue = ''; } });
    window.GaoReceiptPriceDrafts = { init, flush, hold,
        acceptSavedPrice: (id, value, version) => {
            const state = states.get(id);
            if (!state || state.value !== state.saved || pending) return;
            state.saved = state.value = String(value); state.input.value = value;
            state.version = version; state.input.dataset.lineVersion = version; render();
        },
        updateLineVersion: (id, version) => { const state = states.get(id); if (state && version) { state.version = version; state.input.dataset.lineVersion = version; } }, hasUnsaved: () => !!pending || dirty().length > 0 };
})();
