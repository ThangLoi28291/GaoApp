'use strict';
window.initQuickLabelPrinting = async function ({ api, templates, printers, dialog, notice, loadHistory, tab }) {
    const $ = id => document.getElementById(id);
    if (!$('quickFields')) return;
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const money = n => Number(n).toLocaleString('vi-VN') + ' đ';
    let results = [], lines = [], sequence = 0, pending = null, busy = false;
    const image = option => option.imageUrl ? `<img class="label-quick-image" src="${esc(option.imageUrl)}" alt="" loading="lazy" />` : '<span class="label-quick-image" aria-hidden="true">▧</span>';
    const safe = action => async event => { event?.preventDefault(); try { await action(event); } catch (e) { notice(e.message, true); } };
    function summary() {
        const total = lines.reduce((n, x) => n + x.quantity, 0);
        $('quickTotal').textContent = lines.length ? `${lines.length} đơn vị · ${Number.isFinite(total) ? total : '—'} tem` : 'Chưa chọn sản phẩm';
        $('quickPrint').disabled = busy || (!pending && (!lines.length || total > 10000 || lines.some(x => !Number.isInteger(x.quantity) || x.quantity < 1 || x.quantity > 10000 || x.product.problem)));
        $('quickFields').disabled = busy || !!pending;
        $('quickPrint').hidden = !pending;
        window.GaoLabelControls.cards($('quickPrintChoices'), templates, printers, total, busy || !!pending || !lines.length || lines.some(x => !Number.isInteger(x.quantity) || x.quantity < 1 || x.product.problem));
    }
    function render() {
        $('quickLines').innerHTML = lines.map(x => `<tr data-quick-line="${x.conversionId}"><td><div class="label-inline">${image(x)}<div><strong>${esc(x.product.name)}</strong><div>${esc(x.product.unit)} · ${esc(x.sku)}</div><small>${esc(x.product.barcode)}</small>${x.product.problem ? `<div class="text-error">${esc(x.product.problem)}</div>` : ''}</div></div></td><td data-label="Giá bán lẻ">${money(x.product.price)}</td><td data-label="Số tem"><input class="form-control qty-input" type="number" min="1" max="10000" step="1" value="${x.quantity}" aria-label="Số tem ${esc(x.product.name)} ${esc(x.product.unit)}" /></td><td><button class="btn btn-sm btn-outline-danger" type="button" data-quick-remove="${x.conversionId}" aria-label="Bỏ ${esc(x.product.name)} ${esc(x.product.unit)}">×</button></td></tr>`).join('') || '<tr><td colspan="4" class="text-muted">Tìm và thêm sản phẩm ở phía trên.</td></tr>';
        summary();
    }
    function add(option) {
        if (pending || busy || option.product.problem) return;
        const existing = lines.find(x => x.conversionId === option.conversionId);
        if (existing) existing.quantity = Math.min(10000, existing.quantity + 1);
        else if (lines.length < 500) lines.push({ ...option, quantity: 1 });
        else throw new Error('Mỗi lần tối đa 500 đơn vị sản phẩm.');
        render();
    }
    function renderResults() {
        const groups = new Map();
        for (const option of results) { const key = option.product.variantId; if (!groups.has(key)) groups.set(key, []); groups.get(key).push(option); }
        $('quickResults').innerHTML = [...groups.values()].map(options => {
            const first = options[0];
            return `<article class="label-quick-result" data-quick-variant="${first.product.variantId}"><div class="label-inline">${image(first)}<div><strong>${esc(first.product.name)}</strong><small class="d-block">${esc(first.sku)}</small></div></div><label class="small mt-2">Đơn vị in<select class="form-select">${options.map(x => `<option value="${x.conversionId}">${esc(x.product.unit)} · ${money(x.product.price)}${x.product.problem ? ' · Chưa đủ thông tin' : ''}</option>`).join('')}</select></label><small class="d-block mt-2" data-quick-code></small><button class="btn btn-outline-primary mt-2" type="button" data-quick-add>+ Thêm vào danh sách</button></article>`;
        }).join('');
        $('quickResults').querySelectorAll('[data-quick-variant]').forEach(updateResult);
    }
    function updateResult(card) {
        const option = results.find(x => x.conversionId === Number(card.querySelector('select').value));
        card.querySelector('[data-quick-code]').textContent = option.product.problem || 'Mã: ' + option.product.barcode;
        card.querySelector('[data-quick-add]').disabled = !!option.product.problem;
    }
    async function search(params, autoAdd = false) {
        const seq = ++sequence; $('quickSearchStatus').textContent = 'Đang tìm…';
        try {
            const found = await api('products?' + params.toString());
            if (seq !== sequence) return;
            results = found; renderResults();
            $('quickSearchStatus').textContent = found.length ? `${found.length} đơn vị tìm thấy${found.length === 60 ? ' · tối đa 60 kết quả, nhập cụ thể hơn để thu hẹp' : ''}` : 'Không tìm thấy sản phẩm/đơn vị đang hoạt động có cấu hình in tem.';
            const exact = found.filter(x => x.product.barcode === params.get('q'));
            if (autoAdd && exact.length === 1 && !exact[0].product.problem) { add(exact[0]); $('quickSearch').select(); }
        } catch (e) { if (seq === sequence) { results = []; renderResults(); $('quickSearchStatus').textContent = e.message; } }
    }
    $('quickSearchForm').addEventListener('submit', safe(() => { clearTimeout(timer); return search(new URLSearchParams({ q: $('quickSearch').value.trim() }), true); }));
    let timer;
    $('quickSearch').addEventListener('input', () => { clearTimeout(timer); sequence++; timer = setTimeout(() => search(new URLSearchParams({ q: $('quickSearch').value.trim() })), 350); });
    $('quickResults').addEventListener('change', e => { const card = e.target.closest('[data-quick-variant]'); if (card) updateResult(card); });
    $('quickResults').addEventListener('click', safe(e => { const button = e.target.closest('[data-quick-add]'); if (button) add(results.find(x => x.conversionId === Number(button.closest('article').querySelector('select').value))); }));
    $('quickLines').addEventListener('input', e => { const row = e.target.closest('[data-quick-line]'); if (row && !pending && !busy) { lines.find(x => x.conversionId === Number(row.dataset.quickLine)).quantity = Number(e.target.value); summary(); } });
    $('quickLines').addEventListener('click', e => { const button = e.target.closest('[data-quick-remove]'); if (button && !pending && !busy) { lines = lines.filter(x => x.conversionId !== Number(button.dataset.quickRemove)); render(); } });
    $('quickRefresh').addEventListener('click', safe(async () => {
        if (pending || busy) return;
        busy = true; summary();
        try {
            const refreshed = await api('products/refresh', 'POST', { conversionIds: lines.map(x => x.conversionId) });
            lines = lines.map(old => {
                const current = refreshed.find(x => x.conversionId === old.conversionId);
                return current ? { ...current, quantity: old.quantity } : { ...old, product: { ...old.product, problem: 'Đơn vị này không còn hoạt động. Bỏ khỏi danh sách.' } };
            });
            notice('Đã lấy lại giá bán và mã vạch hiện tại. Kiểm tra trước khi in.');
        } finally { busy = false; render(); }
    }));
    function choices(templateId) {
        const template = templates.find(x => x.id === templateId);
        const printer = printers.find(x => x.id === template?.design.printerId && x.enabled);
        if (!template || !printer || template.design.showPrintButton === false) throw new Error('Mẫu chưa sẵn sàng. Nhờ admin gắn máy in và bật mẫu.');
        if (!lines.length || lines.some(x => x.product.problem)) throw new Error('Chọn sản phẩm đủ mã vạch và giá bán.');
        return { template, printer };
    }
    async function previewQuick(templateId) {
        const { template, printer } = choices(templateId);
        const urls = [];
        try {
            for (const line of lines.slice(0, 5)) urls.push({ line, url: URL.createObjectURL(await api('preview', 'POST', { design: template.design, product: line.product, dpi: printer.dpi }, true)) });
            dialog('Xem trước tem · ' + template.design.name, urls.map(x => `<p>${esc(x.line.product.name)} · ${esc(x.line.product.unit)}</p><img src="${x.url}" alt="Tem ${esc(x.line.product.name)}" />`).join('') + (lines.length > 5 ? '<p>Hiển thị 5 đơn vị đầu tiên.</p>' : ''), null);
            $('labelDialog').addEventListener('close', () => urls.forEach(x => URL.revokeObjectURL(x.url)), { once: true });
        } catch (e) { urls.forEach(x => URL.revokeObjectURL(x.url)); throw e; }
    }
    async function printQuick(templateId) {
        if (busy) return;
        if (!pending) {
            const { template, printer } = choices(templateId);
            busy = true; summary();
            let confirmed;
            try {
                const prepared = await window.GaoLabelControls.prepareBarcodes({ templateId: template.id, templateVersion: template.rowVersion,
                    variantIds: [], quickLines: lines.map(x => ({ conversionId: x.conversionId, quantity: x.quantity, fingerprint: x.fingerprint })) }, lines.map(x => ({ ...x.product, imageUrl: x.imageUrl })));
                if (!prepared) return;
                if (prepared.options) {
                    lines = lines.map(old => ({ ...prepared.options.find(x => x.conversionId === old.conversionId), quantity: old.quantity }));
                    render(); notice('Đã cập nhật mã mặc định. Mã cũ vẫn quét được.');
                }
                confirmed = await window.GaoLabelControls.confirmPrint({ title: 'In nhanh sản phẩm', template, printer, items: lines });
            }
            finally { busy = false; summary(); }
            if (!confirmed) return;
            pending = { taskId: null, templateId: template.id, printerId: printer.id, templateVersion: template.rowVersion,
                lines: [], quickLines: lines.map(x => ({ conversionId: x.conversionId, quantity: x.quantity, fingerprint: x.fingerprint })), requestId: crypto.randomUUID() };
        }
        const body = pending;
        busy = true; summary();
        try {
            const job = await api('jobs', 'POST', body);
            pending = null; lines = []; render();
            notice(`Đã gửi lệnh in nhanh #${job.id}. Theo dõi và xác nhận tem tại Lịch sử in.`);
            try { await loadHistory(); } catch { notice(`Đã gửi lệnh #${job.id}, nhưng chưa tải được lịch sử. Bấm Tải lại để kiểm tra.`); }
            tab('history');
        } catch (e) {
            if (e.status && e.status < 500) pending = null;
            else e.message += ' Chưa rõ server đã nhận lệnh. Thử lại lệnh trước dùng cùng mã yêu cầu để tránh in trùng.';
            throw e;
        } finally { busy = false; summary(); }
    }
    $('quickPrint').addEventListener('click', safe(() => printQuick()));
    $('quickPrintChoices').addEventListener('click', safe(async e => {
        const button = e.target.closest('button'); if (!button || button.disabled) return;
        if (button.dataset.printTemplate) await printQuick(Number(button.dataset.printTemplate));
        if (button.dataset.previewTemplate) await previewQuick(Number(button.dataset.previewTemplate));
    }));
    render(); $('quickSearchStatus').textContent = '';
    window.addEventListener('beforeunload', event => { if (pending) { event.preventDefault(); event.returnValue = ''; } });
    const params = new URLSearchParams(location.search);
    if (params.get('productId') || params.get('variantId')) {
        tab('quick');
        const filter = new URLSearchParams();
        for (const key of ['productId', 'variantId']) if (params.get(key)) filter.set(key, params.get(key));
        await search(filter);
        const wanted = (params.get('units') || '').split(',').map(Number);
        const selected = results.filter(x => wanted.includes(x.conversionId));
        if (selected.length) selected.forEach(add);
        else if (new Set(results.map(x => x.product.variantId)).size === 1 && results.length) add(results.find(x => x.isBaseUnit) || results[0]);
    }
};
