(function () {
    'use strict';
    const setup = JSON.parse(document.getElementById('receiptStudioData').textContent), el = id => document.getElementById(id);
    let catalog = setup.templates, current, filter = 'all', dirty = false, editingId, previewWidth;
    let storeInfo = setup.storeInfo || { storeName: 'Cửa hàng của bạn', storeAddress: '', storePhone: '' }, storeInfoDirty = false;
    const previewFrame = el('receiptPreview'), previewStage = previewFrame.parentElement;
    function fitPreview() {
        if (!previewWidth) return;
        const style = getComputedStyle(previewStage);
        const available = previewStage.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
        if (available > 0) previewFrame.style.zoom = Math.min(1, available / previewWidth);
        const receipt = previewFrame.contentDocument?.querySelector('.receipt');
        if (receipt) previewFrame.style.height = Math.ceil(receipt.getBoundingClientRect().height) + 1 + 'px';
    }
    new ResizeObserver(fitPreview).observe(previewStage);
    const sample = {
        orderId: 1286, orderNumber: 'POS-001286', createdAtUtc: '2026-09-10T03:25:00Z', cashierName: 'Nguyễn Minh Anh', terminalName: 'Quầy thu ngân 01',
        customerName: 'Nguyễn Hoàng An', subtotal: 285000, discountTotal: 15000, grandTotal: 270000, paidTotal: 300000, changeDue: 30000, balanceDue: 0,
        lines: [{ itemName: 'Cà phê Arabica rang mộc', sku: 'CF-001', quantity: 2, sellingUnitName: 'Gói', unitPrice: 85000, lineTotal: 170000 },
            { itemName: 'Trà ô long túi lọc', sku: 'TRA-02', quantity: 1, sellingUnitName: 'Hộp', unitPrice: 75000, lineTotal: 75000 },
            { itemName: 'Bánh hạnh nhân nguyên hạt', sku: 'BANH-03', quantity: 1, sellingUnitName: 'Gói', unitPrice: 40000, lineTotal: 40000 }],
        payments: [{ method: 0, amount: 300000 }] };
    function message(text, error = false) { const box = el('studioMessage'); box.hidden = false; box.classList.toggle('error', error); box.textContent = text; }
    function storeFields() {
        return { storeName: el('receiptStoreName')?.value ?? storeInfo.storeName,
            storeAddress: el('receiptStoreAddress')?.value ?? storeInfo.storeAddress,
            storePhone: el('receiptStorePhone')?.value ?? storeInfo.storePhone };
    }
    function fillStoreInfo() {
        for (const [id, key] of [['receiptStoreName', 'storeName'], ['receiptStoreAddress', 'storeAddress'], ['receiptStorePhone', 'storePhone']])
            if (el(id)) el(id).value = storeInfo[key] || '';
    }
    function design() { return ReceiptTemplates.normalize({ name: el('designName').value, layout: el('designLayout').value, paperSize: el('designSize').value,
        title: el('designTitle').value, headerText: el('designHeader').value, footerText: el('designFooter').value,
        showCustomer: el('showCustomer').checked, showCashier: el('showCashier').checked, showSku: el('showSku').checked, showPayments: el('showPayments').checked }); }
    function syncPaperSize() {
        const allowed = ReceiptTemplates.paperSizes(el('designLayout').value), select = el('designSize');
        for (const option of select.options) option.disabled = !allowed.includes(option.value);
        if (!allowed.includes(select.value)) select.value = '80';
        el('wideLayoutNote').hidden = el('designLayout').value !== 'itemwide';
    }
    function preview() {
        syncPaperSize();
        const d = design(), rendered = ReceiptTemplates.render({ ...sample, ...storeFields() }, d), frame = el('receiptPreview');
        el('previewTitle').textContent = d.name; el('previewSize').textContent = d.paperSize + (rendered.size.height ? '' : ' mm');
        previewWidth = rendered.size.width * 96 / 25.4;
        frame.style.width = rendered.size.width + 'mm';
        fitPreview();
        frame.onload = fitPreview;
        frame.srcdoc = rendered.html;
    }
    function choose(option) {
        current = option; editingId = option.builtIn ? null : Number(option.key.replace('custom-', '')); dirty = false;
        const d = option.design;
        for (const [id, field] of Object.entries({ designName: 'name', designLayout: 'layout', designSize: 'paperSize', designTitle: 'title', designHeader: 'headerText', designFooter: 'footerText' })) el(id).value = d[field] ?? '';
        for (const id of ['showCustomer', 'showCashier', 'showSku', 'showPayments']) el(id).checked = d[id];
        if (el('saveTemplate')) el('saveTemplate').textContent = editingId ? 'Lưu thay đổi' : 'Lưu mẫu mới';
        if (el('deleteTemplate')) el('deleteTemplate').hidden = !editingId;
        gallery(); preview(); previewStage.scrollTop = 0;
    }
    function gallery() {
        const visible = catalog.filter(x => filter === 'all' || x.design.paperSize === filter);
        el('templateCount').textContent = `${catalog.length} mẫu · 5 khổ giấy`;
        const nodes = visible.map(option => {
            const button = document.createElement('button'); button.type = 'button'; button.className = 'template-card' + (option.key === current?.key ? ' selected' : '');
            button.dataset.templateKey = option.key; button.setAttribute('aria-pressed', String(option.key === current?.key));
            const e = ReceiptTemplates.escape;
            const miniItems = option.design.layout === 'itemwide' ? '<i></i><div class="mini-item-columns"><span>SL</span><span>ĐVT</span><span>Đơn giá</span><span>Thành tiền</span></div><i></i>' : '<i></i><i></i><i></i>';
            button.innerHTML = `<div class="template-thumb"><div class="mini-receipt ${e(option.design.layout)}"><strong>${e(storeFields().storeName || 'Tên tiệm')}</strong><div>${e(option.design.title)}</div>${miniItems}<div class="mini-total">270.000 đ</div><i></i></div></div><div class="template-caption"><strong>${e(option.design.name)}</strong><small>${option.builtIn ? 'Mẫu có sẵn' : 'Mẫu của cửa hàng'} · ${e(option.design.paperSize)}</small></div>`;
            button.addEventListener('click', () => { if (!dirty || confirm('Bỏ thay đổi chưa lưu để chọn mẫu khác?')) choose(option); });
            return button;
        });
        el('templateGallery').replaceChildren(...nodes);
    }
    async function api(path = '', method = 'GET', body) {
        const url = path === '/store-info' ? '/admin/receipt-templates/store-info' : '/admin/receipt-templates/data' + path;
        const response = await fetch(url, { method, headers: { 'Content-Type': 'application/json',
            RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]').value }, body: body ? JSON.stringify(body) : undefined });
        const data = await response.json();
        if (!response.ok) throw new Error(data.message || data.detail || Object.values(data.errors || {}).flat().join('\n') || 'Không lưu được mẫu. Hãy thử lại.');
        return data;
    }
    async function busy(button, work) { button.disabled = true; try { await work(); } catch (e) { message(e.message, true); } finally { button.disabled = false; } }
    function localChoice() {
        if (storeInfoDirty) throw new Error('Bấm Lưu thông tin tiệm trước khi áp dụng hoặc in thử.');
        if (dirty) throw new Error('Lưu các chỉnh sửa thành mẫu trước khi áp dụng cho client.');
        return { mode: el('printMode').value, printer: el('clientPrinter').value, copies: Number(el('printCopies').value),
            color: false, templateKey: current.key, template: current.design };
    }
    function profile() {
        const p = PosPrinting.preferences(setup);
        el('activePrintProfile').textContent = `Đang dùng: ${p.template?.name || catalog.find(x => x.key === p.templateKey)?.design.name || 'Hiện đại · 80 mm'} · ${p.mode === 'qz' ? p.printer : 'Hộp thoại trình duyệt'}`;
    }
    el('paperFilters').addEventListener('click', event => { const button = event.target.closest('[data-size]'); if (!button) return;
        filter = button.dataset.size; el('paperFilters').querySelectorAll('button').forEach(x => x.classList.toggle('active', x === button)); gallery(); });
    el('designFields').addEventListener('input', () => { dirty = true; preview(); });
    el('storeIdentityFields')?.addEventListener('input', () => {
        storeInfoDirty = true; el('storeIdentityStatus').textContent = 'Thông tin vừa sửa chưa lưu.'; gallery(); preview();
    });
    el('saveStoreIdentity')?.addEventListener('click', event => busy(event.currentTarget, async () => {
        const fields = el('storeIdentityFields'); fields.disabled = true;
        try {
            storeInfo = await api('/store-info', 'PUT', { ...storeFields(), rowVersion: storeInfo.rowVersion });
            storeInfoDirty = false; fillStoreInfo(); gallery(); preview();
            el('storeIdentityStatus').textContent = 'Đã lưu cho tất cả mẫu. Các quầy đang kết nối sẽ nhận cập nhật sau vài giây.';
            message('Đã lưu thông tin tiệm trên hóa đơn.');
        } finally { fields.disabled = !setup.canManage; }
    }));
    el('printMode').addEventListener('change', () => { el('qzSettings').hidden = el('printMode').value !== 'qz'; });
    el('connectPrinters').addEventListener('click', event => busy(event.currentTarget, async () => {
        const printers = await PosPrinting.printers(), selected = el('clientPrinter').value;
        el('clientPrinter').replaceChildren(new Option('Chọn máy in', ''), ...printers.map(name => new Option(name, name)));
        el('clientPrinter').value = printers.includes(selected) ? selected : '';
        message(printers.length ? `Đã tìm thấy ${printers.length} máy in trên client này.` : 'Chưa có máy in. Cài driver máy in trong hệ điều hành rồi kết nối lại.');
    }));
    el('applyTemplate').addEventListener('click', event => busy(event.currentTarget, async () => {
        PosPrinting.savePreferences(setup, localChoice()); profile(); message('Đã lưu mẫu và máy in cho client này. Mẫu đã lưu cũng được dùng khi bán offline.');
    }));
    el('testPrint').addEventListener('click', () => {
        try { PosPrinting.savePreferences(setup, localChoice()); profile(); PosPrinting.openLocal({ ...sample, ...storeInfo }, setup, catalog); }
        catch (e) { message(e.message, true); }
    });
    el('saveTemplate')?.addEventListener('click', event => busy(event.currentTarget, async () => {
        const saved = await api(editingId ? '/' + editingId : '', editingId ? 'PUT' : 'POST', { design: design(), rowVersion: editingId ? current.rowVersion : null });
        catalog = await api(); choose(catalog.find(x => x.key === saved.key)); message('Đã lưu mẫu cửa hàng. Bấm Dùng mẫu & máy in này để áp dụng bản vừa lưu cho client.');
    }));
    el('duplicateTemplate')?.addEventListener('click', () => { editingId = null; dirty = true; el('designName').value = design().name.slice(0, 80) + ' · Bản sao'; el('saveTemplate').textContent = 'Lưu mẫu mới'; el('deleteTemplate').hidden = true; preview(); });
    el('newTemplate').addEventListener('click', () => { if (dirty && !confirm('Bỏ thay đổi chưa lưu?')) return; choose(catalog.find(x => x.key === 'modern-80')); el('designName').value = 'Mẫu mới của cửa hàng'; dirty = true; preview(); el('designName').focus(); });
    el('deleteTemplate')?.addEventListener('click', event => busy(event.currentTarget, async () => {
        if (!editingId || !confirm('Xóa mẫu khỏi thư viện cửa hàng? Bản đã chọn trên client được giữ tới khi chọn mẫu khác.')) return;
        await api('/' + editingId, 'DELETE', { rowVersion: current.rowVersion }); catalog = await api(); choose(catalog[0]); message('Đã xóa mẫu khỏi thư viện.');
    }));
    try {
        fillStoreInfo();
        const p = PosPrinting.preferences(setup); el('printMode').value = p.mode; el('qzSettings').hidden = p.mode !== 'qz';
        if (p.printer) el('clientPrinter').append(new Option(p.printer, p.printer, true, true));
        el('printCopies').value = p.copies;
        choose(catalog.find(x => x.key === p.templateKey) || catalog.find(x => x.key === 'modern-80')); profile();
    } catch (error) { choose(catalog[0]); message(error.message, true); }
})();
