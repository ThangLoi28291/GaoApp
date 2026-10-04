(() => {
    'use strict';
    const root = document.getElementById('invoiceLibrary'); if (!root) return;
    const $ = id => document.getElementById(id), base = '/admin/input-invoices';
    const token = root.querySelector('[name="__RequestVerificationToken"]').value;
    const statuses = ['Chưa kiểm tra', 'Đã xác nhận hợp lệ', 'Chi phí cửa hàng', 'Không ghi nhận / cần làm rõ'];
    const kinds = ['Hóa đơn gốc', 'Thay thế', 'Điều chỉnh', 'Chưa xác định'];
    const colors = ['amber', 'green', 'purple', 'red'];
    const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const money = v => Number(v || 0).toLocaleString('vi-VN') + ' đ';
    const date = v => v ? new Date(v).toLocaleDateString('vi-VN') : '—';
    const time = v => v ? new Date(v.endsWith('Z') ? v : v + 'Z').toLocaleString('vi-VN') : '—';
    let page = 1, pages = 1, selected = null, listVersion = 0, detailVersion = 0, previewVersion = 0;
    async function request(url, options = {}) {
        const response = await fetch(url, { ...options, headers: { 'RequestVerificationToken': token, ...options.headers } });
        const type = response.headers.get('content-type') || '';
        if (!response.ok || response.redirected) {
            let message = 'Không tải được dữ liệu. Kiểm tra kết nối hoặc phiên đăng nhập.';
            if (type.includes('json')) { const data = await response.json(); message = data.message || message; }
            throw new Error(message);
        }
        return type.includes('json') ? response.json() : response.text();
    }
    function message(text, error = false) { $('ilMessage').textContent = text; $('ilMessage').hidden = !text; $('ilMessage').className = 'alert ' + (error ? 'alert-danger' : 'alert-info'); }
    function params() { const p = new URLSearchParams(new FormData($('ilFilters'))); for (const [k,v] of [...p]) if (!v) p.delete(k); p.set('page', page); return p; }
    async function load() {
        const version = ++listVersion; $('ilCount').textContent = 'Đang tải…';
        try {
            const data = await request(`${base}/list?${params()}`); if (version !== listVersion) return;
            page = data.page; pages = data.pages;
            $('ilTotal').textContent = data.total.toLocaleString('vi-VN'); $('ilUnchecked').textContent = data.uncheckedCount.toLocaleString('vi-VN'); $('ilUnlinked').textContent = data.unlinked.toLocaleString('vi-VN');
            $('ilTypes').innerHTML = '<span>PHÂN LOẠI THEO XML</span>' + data.summary.map(x => `<b>${esc(kinds[x.kind])} · ${x.count}<span>${money(x.total)}</span></b>`).join('');
            $('ilRows').innerHTML = data.rows.length ? data.rows.map(x => `<tr><td><strong>${esc(x.series)} · ${esc(x.number)}</strong><small>${date(x.invoiceDate)} · ${x.hasPdf ? 'PDF + XML' : 'XML'}</small></td><td><strong>${esc(x.sellerName)}</strong><small>MST ${esc(x.sellerTaxCode)}</small><small>Người mua: ${esc(x.buyerName)}</small></td><td><span class="il-chip purple">${esc(kinds[x.invoiceKind])}</span></td><td class="il-money"><strong>${money(x.total)}</strong><small>VAT ${money(x.tax)}</small></td><td><span class="il-chip ${x.linked ? 'green' : ''}">${x.linked ? 'Đã liên kết' : 'Chưa liên kết'}</span></td><td><span class="il-chip ${colors[x.reviewStatus]}">${esc(statuses[x.reviewStatus])}</span></td><td><button class="btn btn-sm btn-outline-primary" data-open="${x.id}">Xem & kiểm tra</button></td></tr>`).join('') : '<tr><td colspan="7" class="il-empty">Chưa có hóa đơn phù hợp.<br>Chọn “Cập nhật từ thư viện” để đọc các XML có sẵn, hoặc thêm file XML mới.</td></tr>';
            for (const row of data.rows.filter(x => x.folderMismatch)) {
                const cell = $('ilRows').querySelector(`[data-open="${row.id}"]`)?.closest('tr')?.children[1];
                if (cell) { const warning = document.createElement('span'); warning.className = 'il-chip amber'; warning.textContent = 'MST khác thư mục nguồn'; cell.append(document.createElement('br'), warning); }
            }
            $('ilCount').textContent = `${data.total.toLocaleString('vi-VN')} hóa đơn`; $('ilPageText').textContent = `Trang ${page} / ${pages}`;
            $('ilPrev').disabled = page <= 1; $('ilNext').disabled = page >= pages;
        } catch (e) { if (version === listVersion) { message(e.message, true); $('ilCount').textContent = 'Không tải được dữ liệu'; } }
    }
    async function preview(pdf) {
        const entry = selected, version = ++previewVersion; if (!entry) return;
        $('ilPreviewContent').textContent = 'Đang tải bản xem trước…';
        if (pdf && entry.hasPdf) {
            const frame = document.createElement('iframe'); frame.title = 'Bản PDF hóa đơn'; frame.src = `${base}/${entry.id}/pdf`; $('ilPreviewContent').replaceChildren(frame); return;
        }
        try { const html = await request(`${base}/${entry.id}/preview`); if (version === previewVersion && selected?.id === entry.id) $('ilPreviewContent').innerHTML = html; }
        catch(e) { if (version === previewVersion) $('ilPreviewContent').textContent = e.message; }
    }
    async function open(id, keepOpen = false) {
        const version = ++detailVersion; selected = null; ++previewVersion;
        $('ilSave').disabled = true; $('ilDetailTitle').textContent = 'Đang tải hóa đơn…'; $('ilDetailMessage').textContent = '';
        $('ilDetailMeta').textContent = ''; $('ilReceipts').textContent = ''; $('ilHistory').textContent = ''; $('ilPreviewContent').textContent = '';
        $('ilReviewStatus').disabled = true; $('ilReviewNote').disabled = true;
        if (!keepOpen) $('ilDetail').showModal();
        try {
            const d = await request(`${base}/${id}`); if (version !== detailVersion || !$('ilDetail').open) return;
            selected = d; $('ilDetailTitle').textContent = `${d.series} · ${d.number}`;
            $('ilDetailMeta').innerHTML = `<p><strong>${esc(d.sellerName)}</strong><br>MST ${esc(d.sellerTaxCode)}<br>Ngày lập ${date(d.invoiceDate)}</p><p>Người mua: ${esc(d.buyerName)}<br>MST ${esc(d.buyerTaxCode)}</p><p><span class="il-chip purple">${esc(kinds[d.invoiceKind])}</span></p>${d.relatedInvoice ? `<p>Hóa đơn liên quan:<br><strong>${esc(d.relatedInvoice)}</strong></p>` : ''}<p>Trước thuế: <strong>${money(d.beforeTax)}</strong><br>VAT: <strong>${money(d.tax)}</strong><br>Tổng tiền: <strong>${money(d.total)}</strong></p>`;
            if (d.folderMismatch) $('ilDetailMeta').innerHTML += `<div class="alert alert-warning small">MST thư mục nguồn: <strong>${esc(d.sourceSupplierTaxCode)}</strong>, khác MST người bán trên XML. Đang đọc đúng file nguồn; cần kiểm tra công ty / chi nhánh trước khi liên kết phiếu nhập.</div>`;
            $('ilReceipts').innerHTML = d.receipts.length ? d.receipts.map(r => `<p><a href="/admin/stock-documents/${r.stockDocumentId}?tab=xml" target="_blank" rel="noopener">${esc(r.documentTitle || r.documentNo)} ↗</a><br><small>${esc(r.documentNo)}</small></p>`).join('') : '<p>Chưa liên kết phiếu nhập. Hóa đơn chi phí vẫn có thể được xác nhận riêng.</p>';
            $('ilReviewStatus').value = d.reviewStatus; $('ilReviewNote').value = d.reviewNote || '';
            const readonly = root.dataset.review !== 'true'; $('ilSave').hidden = readonly; $('ilSave').disabled = readonly;
            $('ilReviewStatus').disabled = readonly; $('ilReviewNote').disabled = readonly;
            $('ilHistory').innerHTML = d.history.length ? d.history.map(h => `<div class="il-history-item"><strong>${esc(statuses[h.status])}</strong><small>${time(h.occurredAtUtc)} · ${esc(h.actorName || 'Người dùng #' + h.actorId)}</small>${esc(h.note || 'Không có ghi chú')}</div>`).join('') : '<p>Chưa có lần xác nhận nào.</p>';
            $('ilShowPdf').hidden = !d.hasPdf; $('ilDownload').href = `${base}/${id}/xml`; await preview(d.hasPdf);
        } catch(e) { if (version === detailVersion) $('ilDetailMessage').textContent = e.message; }
    }
    document.querySelectorAll('.il-dialog [data-close], .il-upload-dialog [data-close]').forEach(b => b.addEventListener('click', () => b.closest('dialog').close()));
    $('ilDetail').addEventListener('close', () => { selected = null; ++detailVersion; ++previewVersion; $('ilPreviewContent').replaceChildren(); });
    $('ilFilters').addEventListener('submit', e => { e.preventDefault(); page = 1; message(''); load(); });
    $('ilClear').addEventListener('click', () => { $('ilFilters').reset(); $('ilFilters').elements.year.value = ''; page = 1; load(); });
    $('ilPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } }); $('ilNext').addEventListener('click', () => { if (page < pages) { page++; load(); } });
    $('ilRows').addEventListener('click', e => { const b = e.target.closest('[data-open]'); if (b) open(Number(b.dataset.open)); });
    $('ilShowPdf').addEventListener('click', () => preview(true)); $('ilShowXml').addEventListener('click', () => preview(false));
    $('ilSave').addEventListener('click', async () => {
        if (!selected) return;
        const entry = selected, status = Number($('ilReviewStatus').value), note = $('ilReviewNote').value.trim();
        if (status === 3 && !note) { $('ilDetailMessage').textContent = 'Vui lòng ghi lý do cần làm rõ hóa đơn.'; $('ilReviewNote').focus(); return; }
        $('ilSave').disabled = true;
        try { await request(`${base}/${entry.id}/review`, { method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({status,note,rowVersion:entry.rowVersion}) }); await load(); if ($('ilDetail').open && selected?.id === entry.id) { await open(entry.id, true); $('ilDetailMessage').textContent = 'Đã lưu xác nhận.'; } }
        catch(e) { $('ilDetailMessage').textContent = e.message; $('ilSave').disabled = false; }
    });
    const syncText = d => `Đã thêm ${d.added}, đã có ${d.existing}. XML không hợp lệ: ${d.invalid}; ngoài phạm vi hoặc MST người mua chưa xác định: ${d.outsideScope}; trùng định danh nhưng khác nội dung: ${d.conflicts}.` + (d.folderMismatches ? ` Có ${d.folderMismatches} XML khác MST thư mục nguồn, vẫn đọc được; mở hóa đơn để kiểm tra.` : '') + (d.unavailable ? ` ${d.unavailable} file không còn đọc được trong lúc cập nhật; các hóa đơn khác đã tiếp tục được xử lý.` : '');
    $('ilSync')?.addEventListener('click', async () => {
        const button = $('ilSync'); button.disabled = true; message('Đang đọc thư viện theo năm / tháng đã chọn. Các bộ lọc khác không giới hạn lần cập nhật này.');
        try { const p = params(); const query = new URLSearchParams(); for(const key of ['year','month']) if(p.has(key)) query.set(key,p.get(key)); const result = await request(`${base}/sync?${query}`,{method:'POST'}); message(syncText(result)); await load(); }
        catch(e) { message(e.message,true); await load(); } finally { button.disabled = false; }
    });
    $('ilUpload')?.addEventListener('click', () => { $('ilUploadMessage').textContent = ''; $('ilUploadDialog').showModal(); });
    $('ilUploadForm').addEventListener('submit', async e => {
        e.preventDefault(); const button = e.submitter; button.disabled = true; $('ilUploadMessage').textContent = 'Đang nhập hóa đơn…';
        try { const result = await request(`${base}/upload`, {method:'POST',body:new FormData(e.target)}); if(result.conflicts) { $('ilUploadMessage').textContent = 'Đã có hóa đơn cùng định danh nhưng khác nội dung. Giữ nguyên bản cũ; cần kiểm tra nguồn XML.'; return; } $('ilUploadDialog').close(); e.target.reset(); message(syncText(result)); await load(); }
        catch(error) { $('ilUploadMessage').textContent = error.message; } finally { button.disabled = false; }
    });
    load();
})();
