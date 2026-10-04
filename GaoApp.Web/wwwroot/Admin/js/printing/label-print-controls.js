(() => {
    'use strict';
    if (window.GaoLabelControls) return;
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const money = n => Number(n || 0).toLocaleString('vi-VN');
    async function api(path, method = 'GET', body, binary = false) {
        const response = await fetch('/admin/label-printing/' + path, { method, credentials: 'same-origin', cache: 'no-store',
            headers: { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('[name="__RequestVerificationToken"]')?.value || '' },
            body: body === undefined ? undefined : JSON.stringify(body) });
        if (!response.ok) {
            const data = await response.json().catch(() => ({}));
            const error = new Error(data.message || data.detail || Object.values(data.errors || {}).flat().join(' ') || `Không thực hiện được (${response.status}).`);
            error.status = response.status; throw error;
        }
        return binary ? response.blob() : response.status === 204 ? null : response.json();
    }
    function available(templates, printers) {
        return templates.filter(t => t.design.showPrintButton !== false).map(template => {
            const printer = printers.find(p => p.id === template.design.printerId);
            const d = template.design, width = d.leftMarginMm + d.columns * d.widthMm + (d.columns - 1) * d.columnGapMm + d.rightMarginMm;
            return { template, printer, problem: !printer ? 'Chưa gắn máy in · liên hệ admin' : !printer.enabled ? 'Máy in đang tắt' : width > printer.printableWidthMm ? 'Mẫu vượt khổ máy in · liên hệ admin' : '' };
        });
    }
    function cards(container, templates, printers, count, disabled = false) {
        container.innerHTML = available(templates, printers).map(({ template: t, printer: p, problem }) => `<article class="label-print-choice">
            <button type="button" class="label-direct-print" data-print-template="${t.id}" ${disabled || problem || !Number.isInteger(count) || count < 1 || count > 10000 ? 'disabled' : ''}>
                <small>${t.design.widthMm} × ${t.design.heightMm} mm · ${t.design.columns} cột</small><strong>${esc(t.design.name)}</strong>
                <span><i class="bx bx-printer" aria-hidden="true"></i> In ${Number.isInteger(count) ? money(count) : '—'} tem</span>
                <small>${esc(problem || p.name)}</small></button>
            <button type="button" class="btn btn-sm btn-outline-secondary" data-preview-template="${t.id}" ${disabled || !count ? 'disabled' : ''}>Xem trước mẫu</button></article>`).join('') || '<p class="label-empty">Chưa có mẫu in được bật. Nhờ admin tạo mẫu và gắn máy in trong Cấu hình in tem.</p>';
    }
    async function preview(template, printer, product) {
        const blob = await api('preview', 'POST', { design: template.design, product, dpi: printer?.dpi || 203 }, true);
        const url = URL.createObjectURL(blob), dialog = document.createElement('dialog');
        dialog.className = 'label-print-popup label-preview-popup';
        dialog.innerHTML = `<h2>${esc(template.design.name)}</h2><p>${esc(printer?.name || 'Chưa gắn máy in')}</p><img src="${url}" alt="Xem trước tem ${esc(product.name)}" /><form method="dialog"><button class="btn btn-outline-secondary">Đóng</button></form>`;
        document.body.append(dialog); dialog.addEventListener('close', () => { URL.revokeObjectURL(url); dialog.remove(); }, { once: true }); dialog.showModal();
    }
    // Every retry sends the immutable original request, including its template version and quantities.
    function sender(onState) {
        let pending = null, busy = false;
        const warn = e => { if (pending) { e.preventDefault(); e.returnValue = ''; } };
        window.addEventListener('beforeunload', warn);
        return {
            get pending() { return pending; }, get busy() { return busy; },
            dispose() { window.removeEventListener('beforeunload', warn); },
            async send(makeRequest) {
                if (busy) return null;
                busy = true; onState();
                try {
                    if (!pending) pending = await makeRequest();
                    const result = await api('jobs', 'POST', pending); pending = null; return result;
                } catch (e) {
                    if (e.status && e.status < 500) pending = null;
                    else if (pending) e.message += ' Chưa rõ server đã nhận lệnh. Bấm “Thử lại lệnh trước” để kiểm tra bằng cùng mã yêu cầu, tránh in trùng.';
                    throw e;
                } finally { busy = false; onState(); }
            }
        };
    }
    function confirmPrint({ title, template, printer, items, receipt = false }) {
        const total = items.reduce((sum, x) => sum + x.quantity, 0);
        const first = items.filter(x => x.countsForProgress).length;
        return ask('Xác nhận gửi in tem', `<div class="label-confirm-heading"><span class="label-confirm-icon">▤</span><div><p class="label-eyebrow">KIỂM TRA TRƯỚC KHI IN</p><h3>${esc(title)}</h3></div></div>
            <div class="label-confirm-printer"><small>Máy in nhận lệnh</small><strong>${esc(printer.name)}</strong><span>${esc(printer.windowsPrinterName)}</span></div>
            <div class="label-confirm-metrics"><div><strong>${money(items.length)}</strong><span>Sản phẩm / đơn vị</span></div><div><strong>${money(total)}</strong><span>Tem lần này</span></div></div>
            <p><strong>${esc(template.design.name)}</strong> · ${template.design.widthMm} × ${template.design.heightMm} mm · ${template.design.columns} cột</p>
            ${receipt ? `<p class="label-help">${first} sản phẩm xử lý lần đầu · ${items.length - first} sản phẩm in lại. In lại không thay đổi tiến độ.</p>` : ''}
            <details class="label-confirm-lines"><summary>Xem ${items.length} dòng sản phẩm</summary><ul>${items.map(x => `<li><div><strong>${esc(x.product.name)}</strong><small>${esc(x.product.unit)} · ${money(x.product.price)} đ${receipt ? x.countsForProgress ? ' · Lần đầu' : ' · In lại' : ''}</small></div><b>${money(x.quantity)} tem</b></li>`).join('')}</ul></details>
            <p class="label-help">Sau khi máy chủ gửi lệnh thành công, hệ thống ghi “Đã gửi in”. Kiểm tra tem tại máy in nếu hết giấy hoặc kẹt giấy.</p>`, 'Xác nhận · Gửi ' + money(total) + ' tem');
    }
    async function prepareBarcodes(request, products = []) {
        const plan = await api('barcodes/check', 'POST', request);
        if (!plan.issues.length) return { changed: 0 };
        const create = plan.issues.filter(x => !x.reuse).length;
        const yes = await ask('Chuẩn bị mã vạch phù hợp để in tem', `
            <p><strong>${plan.issues.length} sản phẩm / đơn vị</strong> có mã vạch chưa phù hợp khổ tem đã chọn.</p>
            <div class="label-barcode-notice"><strong>Mã cũ vẫn quét bán hàng bình thường</strong><p>Mã phù hợp sẽ trở thành mặc định của đúng đơn vị đang in. ${create ? `Tạo ${create} mã nội bộ mới. ` : ''}${plan.issues.length - create ? `Dùng lại ${plan.issues.length - create} mã đã có. ` : ''}Không thay đổi tiến độ xử lý.</p></div>
            <ul class="label-barcode-issues">${plan.issues.map(x => {
                const image = products.find(p => p.variantId === x.product.variantId && (!p.unitId || p.unitId === x.product.unitId))?.imageUrl;
                return `<li>${image ? `<img src="${esc(image)}" alt="" />` : '<span class="label-image-empty">▧</span>'}<div><strong>${esc(x.product.name)}</strong><small>Đơn vị: ${esc(x.product.unit)}</small><small>Mã hiện tại: <code>${esc(x.product.barcode)}</code></small><small>${x.reuse ? 'Dùng mã đã có' : 'Tạo mã nội bộ'}: <code>${esc(x.newBarcode)}</code></small></div></li>`;
            }).join('')}</ul>
            <p class="label-help">Sau khi xác nhận, mã mặc định được lưu kể cả khi anh/chị hủy bước in tiếp theo. Hệ thống lưu người thực hiện và lịch sử thay đổi.</p>`, 'Đồng ý · Cập nhật mã và tiếp tục');
        if (!yes) return null;
        return await api('barcodes/prepare', 'POST', { ...request, planToken: plan.token });
    }
    function ask(title, content, accept = 'Xác nhận', collect = () => true) {
        return new Promise(resolve => {
            const d = document.createElement('dialog'); d.className = 'label-print-popup label-confirm-popup';
            d.setAttribute('aria-label', title);
            d.innerHTML = `<form><h2>${esc(title)}</h2>${content}<div class="label-confirm-actions"><button type="button" class="btn btn-outline-secondary" data-close>Quay lại</button><button class="btn btn-primary" type="submit">${esc(accept)}</button></div></form>`;
            let result = null;
            d.querySelector('form').addEventListener('submit', event => { event.preventDefault(); result = collect(d); if (result != null) d.close(); });
            d.querySelector('[data-close]').onclick = () => d.close();
            d.querySelector('[name=reason]')?.addEventListener('change', () => d.querySelector('[name=note]')?.setCustomValidity(''));
            d.addEventListener('close', () => { d.remove(); resolve(result); }, { once: true });
            document.body.append(d); d.showModal(); d.querySelector('[data-close]').focus();
        });
    }
    const done = line => line.done || line.handled || line.printed > 0;
    function receiptEditor(container, initial, templates, printers, { onPrinted, onQueued, onChanged, notice, popup = false } = {}) {
        let task = initial, queueBusy = false;
        let mode = available(templates, printers).find(x => !x.problem)?.template.design.quantityMode || 'one';
        const defaults = () => task.lines.map(l => l.removed ? 0 : mode === 'custom' ? Math.max(1, l.required - l.printed) : mode === 'one' ? 1 : Math.max(0, Math.floor(l.product.receivedQuantity)));
        let quantities = defaults(), selected = task.lines.map(() => false);
        const send = sender(update);
        const $ = selector => container.querySelector(selector);
        const active = () => task.jobs?.some(j => [0, 1, 2, 3].includes(j.status));
        const locked = () => queueBusy || send.busy || !!send.pending;
        const blocked = () => locked() || task.sourceChanged || active() || task.provisionalCount > 0;
        const total = () => quantities.reduce((sum, q, i) => sum + (selected[i] ? q : 0), 0);
        const valid = () => quantities.every((q, i) => !selected[i] || Number.isInteger(q) && q > 0 && q <= 10000) && total() > 0 && total() <= 10000;
        const safe = action => async event => { try { await action(event); } catch (e) { notice?.(e.message, true); } };
        container.innerHTML = `<fieldset class="label-receipt-fields"><div class="label-editor-tools"><div><h3>1. Chọn sản phẩm cần in</h3><p class="label-help">Chọn chủ động từng sản phẩm. In lần đầu xong là hoàn tất sản phẩm; vẫn có thể in lại bất cứ lúc nào.</p></div><div class="label-selection-tools"><button type="button" class="btn btn-sm btn-outline-primary" data-select="pending">Chọn chưa xử lý</button><button type="button" class="btn btn-sm btn-outline-secondary" data-select="all">Chọn tất cả</button><button type="button" class="btn btn-sm btn-outline-secondary" data-select="none">Bỏ chọn</button></div></div>
            <div class="label-quantity-modes" role="group" aria-label="Cách tính số tem"><span>Số tem:</span><button type="button" data-quantity-mode="received">Theo SL nhập</button><button type="button" data-quantity-mode="one">Mỗi SP 1 tem</button><button type="button" data-quantity-mode="custom">Tự nhập</button></div>
            <div class="label-table-scroll"><table class="table label-receipt-table"><thead><tr><th><input type="checkbox" data-all aria-label="Chọn tất cả" /></th><th>Ảnh</th><th>Sản phẩm / đơn vị in</th><th>SL nhập</th><th>Tem đã ghi nhận</th><th>Tem lần này</th><th>Xử lý</th></tr></thead><tbody id="${popup ? 'popupTaskLines' : 'taskLines'}"></tbody></table></div>
            <div class="label-resolution-tools"><button type="button" class="btn btn-outline-secondary" data-skip-selected>Bỏ qua SP đã chọn</button><button type="button" class="btn btn-outline-secondary" data-skip-rest>Bỏ qua phần còn lại</button></div></fieldset>
            <p class="label-receipt-warning" role="status"></p>${popup ? '<button type="button" class="btn btn-outline-secondary" data-refresh-source hidden>Cập nhật từ phiếu nhập</button>' : ''}
            <div class="label-print-dock"><div class="label-toolbar"><h3>2. Chọn mẫu &amp; xác nhận in</h3><strong data-total aria-live="polite"></strong></div><div class="label-print-choices"></div><button type="button" class="btn btn-warning" data-retry hidden>Thử lại lệnh trước</button></div>
            ${popup ? '<div class="label-popup-footer"><button type="button" class="btn btn-outline-secondary" data-later>Để sau</button><button type="button" class="btn btn-outline-primary" data-queue>Đưa vào chờ in</button></div>' : ''}`;
        function rows() {
            $('tbody').innerHTML = task.lines.map((l, i) => `<tr class="${selected[i] ? 'label-line-selected' : ''} ${l.skipped ? 'label-line-skipped' : ''}"><td><input type="checkbox" data-selected="${i}" ${selected[i] ? 'checked' : ''} ${l.removed || l.product.problem ? 'disabled' : ''} aria-label="Chọn ${esc(l.product.name)}" /></td>
                <td>${l.product.imageUrl ? `<button type="button" class="label-product-image" data-image="${i}" aria-label="Xem ảnh ${esc(l.product.name)}"><img src="${esc(l.product.imageUrl)}" alt="" loading="lazy" /></button>` : '<span class="label-image-empty">▧</span>'}</td>
                <td><strong>${esc(l.product.name)}</strong><small>${esc(l.product.unit)} · ${money(l.product.price)} đ</small><small>${esc(l.product.barcode)}</small>${l.removed || l.product.problem ? `<span class="text-error">${esc(l.product.problem || 'Đã bỏ khỏi phiếu')}</span>` : ''}</td>
                <td data-label="SL nhập">${money(l.product.receivedQuantity)}</td><td data-label="Tem đã ghi nhận">${money((l.sent || 0) + l.printed)}</td><td data-label="Tem lần này"><input class="form-control qty-input" type="number" min="1" max="10000" step="1" data-quantity="${i}" value="${quantities[i]}" ${l.removed ? 'disabled' : ''} aria-label="Số tem ${esc(l.product.name)}" /></td>
                <td><span class="label-state ${done(l) ? 'is-done' : l.skipped ? 'is-skipped' : 'is-pending'}">${l.removed ? 'Đã bỏ khỏi phiếu' : done(l) ? 'Đã xử lý' : l.skipped ? 'Đã bỏ qua' : 'Chưa xử lý'}</span>${l.skipped && !l.removed ? `<button type="button" class="label-restore" data-restore="${i}">Khôi phục</button>` : ''}</td></tr>`).join('');
            update();
        }
        function update() {
            if (!$('fieldset')) return;
            $('fieldset').disabled = locked() || active();
            container.querySelectorAll('[data-quantity-mode]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.quantityMode === mode)));
            $('[data-total]').textContent = `${selected.filter(Boolean).length} sản phẩm · ${Number.isFinite(total()) ? money(total()) : '—'} tem`;
            $('.label-receipt-warning').textContent = task.sourceChanged ? 'Phiếu hoặc giá bán đã thay đổi. Cập nhật từ phiếu nhập trước khi in.' : active() ? 'Có lệnh đang chờ hoặc cần kiểm tra kết quả. Theo dõi lịch sử phía dưới; không gửi lặp khi chưa rõ kết quả.' : task.provisionalCount ? 'Ghép hàng tạm vào danh mục trước khi in.' : !valid() ? 'Chưa chọn sản phẩm hoặc số tem chưa hợp lệ. Chọn sản phẩm và nhập số tem từ 1 đến 10.000.' : '';
            cards($('.label-print-choices'), templates, printers, total(), blocked() || !valid());
            $('[data-retry]').hidden = !send.pending; $('[data-retry]').disabled = send.busy;
            if ($('[data-queue]')) $('[data-queue]').disabled = locked() || active();
            if ($('[data-later]')) $('[data-later]').disabled = locked();
            if ($('[data-refresh-source]')) { $('[data-refresh-source]').hidden = !task.sourceChanged; $('[data-refresh-source]').disabled = locked() || active(); }
            const pending = task.lines.map((l, i) => !l.removed && !done(l) && !l.skipped ? i : -1).filter(i => i >= 0);
            $('[data-skip-rest]').disabled = blocked() || !pending.length;
            $('[data-skip-selected]').disabled = blocked() || !pending.some(i => selected[i]);
            const eligible = task.lines.map((l, i) => !l.removed && !l.product.problem ? i : -1).filter(i => i >= 0);
            $('[data-all]').checked = eligible.length > 0 && eligible.every(i => selected[i]);
            $('[data-all]').indeterminate = eligible.some(i => selected[i]) && !eligible.every(i => selected[i]);
        }
        async function ensureTask() {
            if (task.id) return;
            const added = await api('receipts/' + task.stockDocumentId, 'POST');
            const fresh = await api('tasks/' + added.id);
            const signature = lines => JSON.stringify(lines.map(l => [l.product.variantId, l.product.price, l.product.barcode, l.product.receivedQuantity, l.printed, l.sent || 0, !!l.handled, !!l.skipped]));
            if (fresh.sourceChanged || signature(fresh.lines) !== signature(task.lines)) throw new Error('Sản phẩm, giá bán hoặc tiến độ vừa thay đổi. Đóng và mở lại phần in tem để kiểm tra.');
            task = fresh;
        }
        async function print(id) {
            if (!send.pending) {
                if (blocked() || !valid()) return;
                const choice = available(templates, printers).find(x => x.template.id === id);
                if (!choice || choice.problem) throw new Error(choice?.problem || 'Không tìm thấy mẫu in.');
                queueBusy = true; update();
                let confirmed;
                try {
                    await ensureTask();
                    const prepared = await prepareBarcodes({ templateId: choice.template.id, templateVersion: choice.template.rowVersion,
                        taskId: task.id, rowVersion: task.rowVersion, variantIds: task.lines.filter((l, i) => selected[i]).map(l => l.product.variantId) }, task.lines.map(l => l.product));
                    if (!prepared) return;
                    if (prepared.task) { task = prepared.task; rows(); notice?.('Đã cập nhật mã mặc định. Mã cũ vẫn hoạt động; tiến độ không thay đổi.'); }
                    confirmed = await confirmPrint({ title: task.documentTitle || task.documentNo, ...choice, receipt: true, items: task.lines.flatMap((l, i) => selected[i] ? [{ product: l.product, quantity: quantities[i], countsForProgress: !done(l) && !l.skipped }] : []) });
                }
                finally { queueBusy = false; update(); }
                if (!confirmed) return;
            }
            const result = await send.send(async () => {
                const { template, printer } = available(templates, printers).find(x => x.template.id === id);
                await ensureTask();
                return { taskId: task.id, templateId: template.id, printerId: printer.id, templateVersion: template.rowVersion,
                    rowVersion: task.rowVersion, requestId: crypto.randomUUID(), productProgress: true,
                    lines: task.lines.flatMap((l, i) => selected[i] ? [{ variantId: l.product.variantId, quantity: quantities[i] }] : []) };
            });
            if (result) {
                task.jobs = [...(task.jobs || []), { id: result.id, status: 0 }]; selected.fill(false); rows();
                notice?.(`Đã đưa lệnh #${result.id} vào hàng đợi. Tiến độ cập nhật khi máy chủ gửi thành công.`); await onPrinted?.(task.id, result);
            }
        }
        async function resolve(indices, restore) {
            if (blocked() || !indices.length) return;
            queueBusy = true; update();
            let changed = false;
            try {
                const answer = await ask(restore ? 'Khôi phục sản phẩm cần xử lý?' : 'Bỏ qua sản phẩm không cần in?', `<p>${restore ? 'Đưa sản phẩm về danh sách chưa xử lý. Lịch sử cũ được giữ nguyên.' : 'Những sản phẩm này được tính là đã xử lý bằng cách bỏ qua. Vẫn có thể in lại sau, không thay đổi tiến độ.'}</p><details open><summary>${indices.length} sản phẩm</summary><ul>${indices.map(i => `<li>${esc(task.lines[i].product.name)}</li>`).join('')}</ul></details>${restore ? '' : '<label class="d-block">Lý do<select class="form-select" name="reason"><option>Đã có tem</option><option>Không cần dán tem</option><option>Khác</option></select></label><label class="d-block mt-2">Ghi chú (bắt buộc nếu chọn Khác)<textarea class="form-control" name="note" maxlength="240"></textarea></label>'}`, restore ? 'Khôi phục' : 'Xác nhận bỏ qua', d => {
                    if (restore) return { reason: 'Khôi phục để xử lý tiếp' };
                    const reason = d.querySelector('[name=reason]').value, note = d.querySelector('[name=note]');
                    note.setCustomValidity(reason === 'Khác' && !note.value.trim() ? 'Nhập lý do cụ thể.' : '');
                    if (!note.reportValidity()) { note.oninput = () => note.setCustomValidity(''); return null; }
                    return { reason: reason + (note.value.trim() ? ': ' + note.value.trim() : '') };
                });
                if (!answer) return;
                await ensureTask();
                task = await api(`tasks/${task.id}/resolve`, 'POST', { rowVersion: task.rowVersion, requestId: crypto.randomUUID(), variantIds: indices.map(i => task.lines[i].product.variantId), restore, reason: answer.reason });
                selected.fill(false); rows(); changed = true;
            } finally { queueBusy = false; update(); }
            if (changed) { notice?.(restore ? 'Đã khôi phục sản phẩm.' : 'Đã ghi nhận bỏ qua và lưu lịch sử.'); await onChanged?.(task.id); }
        }
        container.addEventListener('input', event => {
            if (event.target.matches('[data-quantity]')) { quantities[Number(event.target.dataset.quantity)] = Number(event.target.value); mode = 'custom'; update(); }
        });
        container.addEventListener('change', event => {
            if (event.target.matches('[data-selected]')) selected[Number(event.target.dataset.selected)] = event.target.checked;
            if (event.target.matches('[data-all]')) selected = task.lines.map(l => event.target.checked && !l.removed && !l.product.problem);
            if (event.target.matches('[data-selected],[data-all]')) rows();
        });
        container.addEventListener('click', safe(async event => {
            const b = event.target.closest('button'); if (!b || b.disabled) return;
            if (b.dataset.quantityMode) { mode = b.dataset.quantityMode; if (mode !== 'custom') quantities = defaults(); rows(); }
            if (b.dataset.select) { selected = task.lines.map(l => b.dataset.select !== 'none' && !l.removed && !l.product.problem && (b.dataset.select === 'all' || !done(l) && !l.skipped)); rows(); }
            if (b.dataset.printTemplate || b.hasAttribute('data-retry')) await print(Number(b.dataset.printTemplate));
            if (b.dataset.previewTemplate) { const t = templates.find(t => t.id === Number(b.dataset.previewTemplate)), line = task.lines.find((l, i) => selected[i]); if (line) await preview(t, printers.find(p => p.id === t.design.printerId), line.product); }
            if (b.hasAttribute('data-skip-rest') || b.hasAttribute('data-skip-selected')) await resolve(task.lines.map((l, i) => !l.removed && !done(l) && !l.skipped && (b.hasAttribute('data-skip-rest') || selected[i]) ? i : -1).filter(i => i >= 0), false);
            if (b.hasAttribute('data-restore')) await resolve([Number(b.dataset.restore)], true);
            if (b.hasAttribute('data-image')) { const p = task.lines[Number(b.dataset.image)].product; await ask(p.name, `<img class="label-image-large" src="${esc(p.imageUrl)}" alt="${esc(p.name)}" />`, 'Đóng'); }
            if (b.hasAttribute('data-refresh-source')) {
                queueBusy = true; update();
                try { task = await api(`tasks/${task.id}/refresh`, 'POST', { rowVersion: task.rowVersion }); quantities = defaults(); selected = task.lines.map(() => false); rows(); }
                finally { queueBusy = false; update(); }
            }
            if (b.hasAttribute('data-queue')) {
                queueBusy = true; update();
                try { await ensureTask(); } finally { queueBusy = false; update(); }
                await onQueued?.(task.id);
            }
        }));
        // Fixed-position image preview is not clipped by the scrollable product table.
        let zoom;
        const hideZoom = () => { zoom?.remove(); zoom = null; };
        container.addEventListener('pointerover', e => {
            const button = e.target.closest('[data-image]'); if (!button || zoom) return;
            const product = task.lines[Number(button.dataset.image)].product, box = button.getBoundingClientRect();
            zoom = document.createElement('img'); zoom.className = 'label-image-zoom'; zoom.src = product.imageUrl; zoom.alt = product.name;
            zoom.style.left = Math.min(window.innerWidth - 252, box.right + 12) + 'px'; zoom.style.top = Math.max(8, Math.min(window.innerHeight - 252, box.top - 80)) + 'px'; (container.closest('dialog') || document.body).append(zoom);
        });
        container.addEventListener('pointerout', e => { if (e.target.closest('[data-image]')) hideZoom(); });
        rows();
        return { get locked() { return locked(); }, dispose() { send.dispose(); hideZoom(); }, get task() { return task; },
            sync(fresh) {
                if (locked()) return;
                const changed = JSON.stringify(fresh.lines) !== JSON.stringify(task.lines);
                const old = new Map(task.lines.map((l, i) => [l.product.variantId, { selected: selected[i], quantity: quantities[i] }]));
                task = fresh;
                if (changed) {
                    selected = task.lines.map(l => !l.removed && !l.product.problem && !!old.get(l.product.variantId)?.selected);
                    quantities = task.lines.map(l => old.get(l.product.variantId)?.quantity ?? 1);
                    rows();
                } else update();
            }, get hasSelection() { return selected.some(Boolean); } };
    }
    window.GaoLabelControls = { api, esc, cards, available, preview, sender, receiptEditor, confirmPrint, prepareBarcodes };
})();
