(() => {
    'use strict';
    const root = document.getElementById('labelPrinting');
    if (!root) return;
    const $ = id => document.getElementById(id), canManage = root.dataset.canManage === 'true';
    const isSettings = root.dataset.page === 'settings';
    const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const money = value => new Intl.NumberFormat('vi-VN').format(value || 0);
    const date = value => value ? new Date(value.endsWith('Z') ? value : value + 'Z').toLocaleString('vi-VN') : '—';
    const defaults = () => ({ name: 'Tem sản phẩm 35 × 22', widthMm: 35, heightMm: 22, columns: 2, columnGapMm: 2, rowGapMm: 2,
        leftMarginMm: 1, rightMarginMm: 1, quantityMode: 'one', barcodeFormat: 'AUTO', layout: 'standard', fontSize: 8, showName: true, showBarcode: true, showBarcodeText: true, showUnit: true, showPrice: true });
    const layouts = [...root.querySelectorAll('[data-layout]')];
    const layoutName = key => layouts.find(x => x.dataset.layout === key)?.dataset.layoutName || 'Cân đối';
    let templates = [], printers = [], tasks = [], task = null, editingTemplate = null, editingPrinter = null;
    let templateDirty = false, planDirty = false, previewUrl = null, previewSequence = 0, dialogAction = null, pendingJob = null;
    const statuses = ['Chờ server in', 'Đang gửi máy in', 'Chờ xác nhận tem', 'Cần xử lý', 'Đã xác nhận', 'Đã hủy'];
    function notice(message, error = false) { $('labelMessage').textContent = message; $('labelMessage').classList.toggle('error', error); $('labelMessage').hidden = false; }
    async function api(path, method = 'GET', body, binary = false) {
        const response = await fetch('/admin/label-printing/' + path, { method, credentials: 'same-origin', headers: {
            'Content-Type': 'application/json', 'RequestVerificationToken': root.querySelector('[name="__RequestVerificationToken"]').value
        }, body: body === undefined ? undefined : JSON.stringify(body) });
        if (!response.ok) {
            const data = await response.json().catch(() => ({}));
            throw new Error(data.message || data.detail || Object.values(data.errors || {}).flat().join(' ') || `Không thực hiện được (${response.status}).`);
        }
        return binary ? response.blob() : response.status === 204 ? null : response.text().then(t => t ? JSON.parse(t) : null);
    }
    function bind(id, action) {
        $(id)?.addEventListener('click', async event => {
            const button = event.currentTarget; button.disabled = true;
            try { await action(event); } catch (error) { notice(error.message, true); }
            finally { button.disabled = false; }
        });
    }
    function tab(name) {
        if (![...root.querySelectorAll('[data-panel]')].some(x => x.dataset.panel === name)) return;
        root.querySelectorAll('[data-panel]').forEach(x => x.hidden = x.dataset.panel !== name);
        root.querySelectorAll('[data-tab]').forEach(x => { x.classList.toggle('active', x.dataset.tab === name); x.setAttribute('aria-current', x.dataset.tab === name ? 'page' : 'false'); });
        if (name === 'history') loadHistory().catch(e => notice(e.message, true));
    }
    root.querySelectorAll('[data-tab]').forEach(x => x.addEventListener('click', () => tab(x.dataset.tab)));
    function options(select, items, label, placeholder) {
        if (!select) return;
        const value = select.value;
        select.innerHTML = `<option value="">${escape(placeholder)}</option>` + items.map(x => `<option value="${x.id}">${escape(label(x))}</option>`).join('');
        if (items.some(x => String(x.id) === value)) select.value = value;
    }
    function fillSelects() {
        for (const id of ['taskTemplate', 'testTemplate']) options($(id), templates, x => `${x.design.name} · ${x.layoutName || layoutName(x.design.layout)} · ${x.design.widthMm}×${x.design.heightMm} · ${x.design.columns} cột`, 'Chọn mẫu đã lưu');
        for (const id of ['taskPrinter', 'testPrinter']) options($(id), printers.filter(x => x.enabled), x => x.name, 'Chọn máy in tại server');
    }
    async function loadTemplates() {
        templates = await api('templates'); fillSelects();
        if (!$('templateList')) return;
        $('templateList').innerHTML = templates.length ? templates.map(x => `<button class="label-template-card ${editingTemplate?.id === x.id ? 'active' : ''}" data-template="${x.id}"><strong>${escape(x.design.name)}</strong><small>${escape(layoutName(x.design.layout))} · ${x.design.widthMm} × ${x.design.heightMm} mm · ${x.design.columns} cột</small><small>${x.design.quantityMode === 'one' ? 'Mặc định 1 tem / sản phẩm' : 'Theo số lượng nhập quy đổi'}</small></button>`).join('') : '<div class="label-empty">Chưa có mẫu. Chọn kiểu trình bày bên cạnh và lưu mẫu đầu tiên.</div>';
    }
    function readDesign() {
        const design = {};
        $('templateFields').querySelectorAll('[name]').forEach(x => design[x.name] = x.type === 'checkbox' ? x.checked : x.type === 'number' ? Number(x.value) : x.value);
        return design;
    }
    function showTemplate(item) {
        editingTemplate = item || null;
        const design = { ...defaults(), ...item?.design, barcodeFormat: 'AUTO' };
        $('templateFields').querySelectorAll('[name]').forEach(x => { if (x.type === 'checkbox') x.checked = design[x.name]; else x.value = design[x.name]; });
        templateDirty = false; $('templateSaveState').textContent = item ? 'Đã lưu' : 'Mẫu mới';
        $('templateHeading').textContent = item ? item.design.name : 'Tạo mẫu tem';
        root.querySelectorAll('[data-template]').forEach(x => x.classList.toggle('active', Number(x.dataset.template) === item?.id));
        selectLayout(design.layout);
        previewTemplate();
    }
    function selectLayout(key) {
        $('templateFields').querySelector('[name="layout"]').value = key;
        layouts.forEach(x => { const selected = x.dataset.layout === key; x.classList.toggle('active', selected); x.setAttribute('aria-pressed', String(selected)); });
    }
    $('layoutGallery')?.addEventListener('click', event => {
        const button = event.target.closest('[data-layout]');
        if (!button || !canManage) return;
        selectLayout(button.dataset.layout);
        markTemplateDirty();
        clearTimeout(previewTimer); previewTemplate();
    });
    $('templateList')?.addEventListener('click', event => {
        const button = event.target.closest('[data-template]'); if (!button) return;
        const item = templates.find(x => x.id === Number(button.dataset.template));
        if (templateDirty) return dialog('Bỏ thay đổi chưa lưu?', '<p>Nội dung đang sửa sẽ được thay bằng mẫu được chọn.</p>', () => showTemplate(item));
        showTemplate(item);
    });
    let previewTimer;
    function markTemplateDirty() {
        templateDirty = true; $('templateSaveState').textContent = 'Chưa lưu';
    }
    $('templateFields')?.addEventListener('input', () => {
        markTemplateDirty();
        clearTimeout(previewTimer); previewTimer = setTimeout(previewTemplate, 450);
    });
    async function previewTemplate() {
        const seq = ++previewSequence, design = readDesign();
        const width = design.columns * design.widthMm + (design.columns - 1) * design.columnGapMm + design.leftMarginMm + design.rightMarginMm;
        $('previewDimensions').textContent = `${layoutName(design.layout)} · ${design.widthMm} × ${design.heightMm} mm / tem · ${design.columns} cột · rộng cuộn ${Math.round(width * 100) / 100} mm`;
        try {
            const blob = await api('preview', 'POST', { design }, true);
            if (seq !== previewSequence) return;
            if (previewUrl) URL.revokeObjectURL(previewUrl);
            previewUrl = URL.createObjectURL(blob); $('labelPreview').src = previewUrl; $('labelPreview').hidden = false; $('previewError').textContent = '';
        } catch (e) { if (seq === previewSequence) { $('labelPreview').hidden = true; $('previewError').textContent = e.message; } }
    }
    async function saveTemplate(copy) {
        const design = readDesign();
        const saved = await api('templates' + (editingTemplate && !copy ? '/' + editingTemplate.id : ''), editingTemplate && !copy ? 'PUT' : 'POST', { design, rowVersion: editingTemplate?.rowVersion });
        editingTemplate = saved; await loadTemplates(); showTemplate(saved); notice('Đã lưu kiểu trình bày, cấu hình tem và số lượng mặc định.');
    }
    bind('newTemplate', () => { if (templateDirty) return dialog('Tạo mẫu mới?', '<p>Bỏ nội dung chưa lưu và mở mẫu mới.</p>', () => showTemplate(null)); showTemplate(null); });
    bind('saveTemplate', () => saveTemplate(false)); bind('copyTemplate', () => saveTemplate(true)); bind('previewTemplate', previewTemplate);
    bind('deleteTemplate', () => {
        if (!editingTemplate) return;
        dialog('Xóa mẫu tem?', `<p>${escape(editingTemplate.design.name)} sẽ không còn trong danh sách chọn. Lịch sử in vẫn giữ mẫu đã dùng.</p>`, async () => {
            await api('templates/' + editingTemplate.id, 'DELETE', { rowVersion: editingTemplate.rowVersion }); editingTemplate = null; await loadTemplates(); showTemplate(templates[0]);
        });
    });
    async function loadPrinters() {
        printers = await api('printers'); fillSelects();
        if (!$('printerList')) return;
        $('printerList').innerHTML = printers.length ? printers.map(p => `<div class="label-job label-toolbar"><div><strong>${escape(p.name)}</strong> <span class="badge-label">${p.enabled ? 'Cho phép in' : 'Đã tắt'}</span><br><small>${escape(p.windowsPrinterName)} · ${p.dpi} DPI · rộng tối đa ${p.printableWidthMm} mm<br>Dịch vụ liên lạc lần cuối: ${date(p.lastSeenAtUtc)}</small></div>${canManage ? `<button class="btn btn-outline-secondary" data-printer="${p.id}">Cấu hình</button>` : ''}</div>`).join('') : '<div class="label-empty">Chưa cấu hình máy in. Quản lý thêm máy đã cài trên Windows Server.</div>';
    }
    async function installedPrinters() {
        if (!isSettings || !canManage) return;
        const list = await api('installed-printers'), select = $('printerForm').elements.windowsPrinterName, value = select.value;
        select.replaceChildren(new Option('Chọn máy đã cài', ''));
        for (const name of list) select.add(new Option(name, name));
        if (value && !list.includes(value)) select.add(new Option(value + ' (chưa thấy)', value));
        select.value = value;
    }
    function showPrinter(p) {
        editingPrinter = p || null; const form = $('printerForm'); form.reset();
        $('printerHeading').textContent = p ? 'Cấu hình ' + p.name : 'Thêm máy in';
        if (!p) return;
        if (![...form.elements.windowsPrinterName.options].some(x => x.value === p.windowsPrinterName)) form.elements.windowsPrinterName.add(new Option(p.windowsPrinterName, p.windowsPrinterName));
        for (const x of form.elements) if (x.name && p[x.name] !== undefined) { if (x.type === 'checkbox') x.checked = p[x.name]; else x.value = p[x.name]; }
    }
    $('printerList')?.addEventListener('click', event => { const button = event.target.closest('[data-printer]'); if (button) showPrinter(printers.find(x => x.id === Number(button.dataset.printer))); });
    $('printerForm')?.addEventListener('submit', async event => {
        event.preventDefault(); const button = event.submitter; button.disabled = true;
        try {
            const body = { rowVersion: editingPrinter?.rowVersion };
            for (const field of event.target.elements) if (field.name) body[field.name] = field.type === 'checkbox' ? field.checked : field.type === 'number' || field.name === 'dpi' ? Number(field.value) : field.value;
            const saved = await api('printers' + (editingPrinter ? '/' + editingPrinter.id : ''), editingPrinter ? 'PUT' : 'POST', body);
            await loadPrinters(); showPrinter(saved); notice('Đã lưu cấu hình máy in.');
        } catch (e) { notice(e.message, true); } finally { button.disabled = false; }
    });
    bind('newPrinter', () => showPrinter(null)); bind('reloadInstalled', installedPrinters);

    function renderTasks() {
        const query = $('taskSearch').value.trim().toLowerCase(), rows = tasks.filter(x => x.documentNo.toLowerCase().includes(query));
        $('labelTaskList').innerHTML = rows.length ? `<div class="label-table-scroll"><table class="table"><thead><tr><th>Phiếu nhập</th><th>Tiến độ</th><th>Trạng thái</th><th>Đưa vào lúc</th><th></th></tr></thead><tbody>${rows.map(x => `<tr><td><strong>${escape(x.documentNo)}</strong></td><td>${x.printed} / ${x.required} tem</td><td><span class="badge-label">${x.completed ? 'Hoàn thành' : x.required ? 'Đang xử lý' : 'Chưa chọn mẫu / SL'}</span></td><td>${date(x.createdAtUtc)}</td><td><button class="btn btn-sm btn-outline-primary" data-task="${x.id}">Mở phiếu in</button></td></tr>`).join('')}</tbody></table></div>` : '<div class="label-empty">Chưa có phiếu chờ in. Mở phiếu nhập và chọn “Đưa vào in tem”.</div>';
    }
    async function loadTasks() { if (!$('labelTaskList')) return; tasks = await api('tasks'); renderTasks(); }
    $('taskSearch')?.addEventListener('input', renderTasks);
    $('labelTaskList')?.addEventListener('click', event => {
        const button = event.target.closest('[data-task]'); if (!button) return;
        const run = () => loadTask(Number(button.dataset.task)).catch(e => notice(e.message, true));
        if (planDirty) dialog('Mở phiếu khác?', '<p>Thay đổi số lượng chưa lưu sẽ được bỏ.</p>', run); else run();
    });
    function renderTask() {
        $('labelTaskDetail').hidden = false; $('taskTitle').textContent = task.documentNo + (task.completed ? ' · Hoàn thành' : '');
        $('sourceReceipt').href = '/admin/stock-documents/' + task.stockDocumentId; $('taskTemplate').value = task.templateId || '';
        $('sourceChanged').hidden = !task.sourceChanged && !task.provisionalCount;
        $('sourceChanged').textContent = task.sourceError || 'Phiếu nhập, thông tin sản phẩm hoặc giá bán đã thay đổi. Bấm “Cập nhật từ phiếu nhập”, kiểm tra số lượng rồi lưu lại trước khi in.';
        if (task.provisionalCount) $('sourceChanged').textContent = `Phiếu còn ${task.provisionalCount} dòng hàng tạm chưa ghép danh mục. Có thể in các sản phẩm đã hoàn thiện; cần xử lý hàng tạm và cập nhật phiếu in trước khi chốt.`;
        $('taskLines').innerHTML = task.lines.map(l => `<tr data-variant="${l.product.variantId}"><td><input class="line-selected" type="checkbox" ${l.removed ? 'disabled' : 'checked'} aria-label="Chọn ${escape(l.product.name)}" /></td><td><strong>${escape(l.product.name)}</strong><br><small>${escape(l.product.barcode || 'Chưa có mã vạch ĐVT gốc')}</small>${l.removed ? '<div class="text-error">Đã bỏ khỏi phiếu nhập</div>' : ''}${l.product.problem ? `<div class="text-error">${escape(l.product.problem)}</div>` : ''}</td><td>${escape(l.product.unit)}</td><td>${money(l.product.price)} đ</td><td>${money(l.product.receivedQuantity)}</td><td><input class="form-control qty-input required-qty" type="number" min="${l.printed}" max="10000" step="1" value="${l.required}" aria-label="Số cần in ${escape(l.product.name)}" ${l.removed || task.completed ? 'disabled' : ''} /></td><td>${l.printed}</td><td><input class="form-control qty-input print-qty" type="number" min="0" max="10000" step="1" value="${Math.max(0, l.required - l.printed)}" aria-label="Số in lần này ${escape(l.product.name)}" ${l.removed ? 'disabled' : ''} /></td></tr>`).join('');
        $('taskTotals').textContent = `Đã nhận ${task.lines.reduce((s, x) => s + x.printed, 0)} / ${task.lines.reduce((s, x) => s + x.required, 0)} tem`;
        renderHistory($('taskHistory'), task.jobs); planDirty = false;
    }
    async function loadTask(id) { task = await api('tasks/' + id); renderTask(); history.replaceState(null, '', '?task=' + id); }
    function quantities(selector, selectedOnly = false) {
        return [...$('taskLines').rows].filter(row => !selectedOnly || row.querySelector('.line-selected').checked)
            .map(row => ({ variantId: Number(row.dataset.variant), quantity: Number(row.querySelector(selector).value) }));
    }
    $('taskLines')?.addEventListener('input', event => { if (event.target.matches('.required-qty')) planDirty = true; });
    $('taskTemplate')?.addEventListener('change', () => {
        if (task && !task.templateId && quantities('.required-qty').every(x => x.quantity === 0)) applyTemplateDefaults();
        planDirty = true; notice('Mẫu đã đổi. Kiểm tra số lượng mặc định rồi lưu số lượng cho phiếu.');
    });
    $('selectAllLines')?.addEventListener('change', event => $('taskLines').querySelectorAll('.line-selected:not(:disabled)').forEach(x => x.checked = event.target.checked));
    function applyTemplateDefaults() {
        const template = templates.find(x => x.id === Number($('taskTemplate').value)); if (!task || !template) throw new Error('Chọn mẫu đã lưu trước.');
        if (task.completed) throw new Error('Phiếu đã hoàn thành.');
        for (const row of $('taskLines').rows) {
            const line = task.lines.find(x => x.product.variantId === Number(row.dataset.variant)); if (line.removed) continue;
            let qty = template.design.quantityMode === 'one' ? 1 : line.product.receivedQuantity;
            if (!Number.isInteger(qty)) { qty = 0; notice('Dòng có số lượng nhập lẻ không tự quy đổi ra số tem. Chức năng này để nâng cấp sau.', true); }
            qty = Math.max(line.printed, qty); row.querySelector('.required-qty').value = qty; row.querySelector('.print-qty').value = qty - line.printed;
        }
        planDirty = true;
    }
    bind('applyDefaults', applyTemplateDefaults);
    bind('savePlan', async () => {
        if (!task) return;
        task = await api(`tasks/${task.id}/plan`, 'PUT', { templateId: Number($('taskTemplate').value), rowVersion: task.rowVersion, lines: quantities('.required-qty') });
        renderTask(); await loadTasks(); notice('Đã lưu mẫu và số lượng cần in của phiếu.');
    });
    bind('refreshSource', () => task && dialog('Cập nhật từ phiếu nhập?', '<p>Lấy lại sản phẩm, đơn vị gốc và giá bán lẻ hiện tại. Giữ số tem đã nhận và số cần in đã lưu; bạn kiểm tra hoặc áp dụng lại SL mặc định của mẫu sau khi cập nhật.</p>', async () => {
        task = await api(`tasks/${task.id}/refresh`, 'POST', { rowVersion: task.rowVersion }); renderTask(); await loadTasks();
    }));
    bind('reloadTasks', loadTasks); bind('closeTask', () => { if (planDirty) return dialog('Đóng phiếu?', '<p>Bỏ thay đổi số lượng chưa lưu.</p>', () => { task = null; planDirty = false; $('labelTaskDetail').hidden = true; }); task = null; $('labelTaskDetail').hidden = true; });
    bind('completeTask', () => task && dialog('Chốt hoàn thành in tem?', '<p>Xác nhận đã nhận đủ số tem cần in của phiếu. Trạng thái duyệt nhập kho được giữ nguyên.</p>', async () => {
        if (planDirty) throw new Error('Lưu số lượng trước khi chốt.');
        await api(`tasks/${task.id}/complete`, 'POST', { rowVersion: task.rowVersion }); await loadTask(task.id); await loadTasks(); notice('Đã chốt hoàn thành in tem.');
    }));

    function dialog(title, content, action, accept = 'Xác nhận') {
        $('labelDialogContent').innerHTML = `<h2>${escape(title)}</h2>${content}`; dialogAction = action;
        $('dialogAccept').textContent = accept; $('dialogAccept').hidden = !action;
        if (!$('labelDialog').open) $('labelDialog').showModal();
    }
    bind('dialogAccept', async () => { await dialogAction?.(); $('labelDialog').close(); });
    bind('dialogClose', () => $('labelDialog').close());
    bind('previewTask', async () => {
        if (!task) return;
        const template = templates.find(x => x.id === Number($('taskTemplate').value)), row = [...$('taskLines').rows].find(x => x.querySelector('.line-selected').checked);
        if (!template || !row) throw new Error('Chọn mẫu và ít nhất một sản phẩm.');
        const product = task.lines.find(x => x.product.variantId === Number(row.dataset.variant)).product;
        const blob = await api('preview', 'POST', { design: template.design, product, dpi: printers.find(x => x.id === Number($('taskPrinter').value))?.dpi }, true);
        const url = URL.createObjectURL(blob); dialog('Xem trước · ' + product.name, `<img src="${url}" alt="Tem sản phẩm đã chọn" /><p>${escape(template.design.name)} · ${template.design.columns} cột</p>`, null);
        $('labelDialog').addEventListener('close', () => URL.revokeObjectURL(url), { once: true });
    });
    async function sendJob(body) {
        // Retrying an uncertain submission reuses its exact request, never a new print ID.
        if (pendingJob) throw new Error('Yêu cầu trước chưa có phản hồi. Tải lại lịch sử để kiểm tra trước khi gửi thêm.');
        pendingJob = body;
        try { const result = await api('jobs', 'POST', body); pendingJob = null; notice(`Đã đưa lệnh #${result.id} vào hàng đợi server.`); }
        catch (e) {
            pendingJob = null;
            notice(e.message + ' Nếu mất kết nối, hãy tải lại lịch sử để kiểm tra lệnh đã được nhận chưa.', true); throw e;
        }
        if (task) await loadTask(task.id); await loadHistory();
    }
    function preparePrint(reprint = false) {
        if (!task) return;
        if (planDirty) throw new Error('Lưu mẫu và số lượng trước khi in.');
        if (task.sourceChanged) throw new Error('Cập nhật từ phiếu nhập trước khi in.');
        const template = templates.find(x => x.id === Number($('taskTemplate').value)), printer = printers.find(x => x.id === Number($('taskPrinter').value));
        if (!template || !printer) throw new Error('Chọn mẫu và máy in.');
        const lines = quantities('.print-qty', true).filter(x => x.quantity > 0);
        if (!lines.length) throw new Error('Chọn sản phẩm và nhập số tem cần in lần này.');
        const body = { taskId: task.id, templateId: template.id, printerId: printer.id, lines, requestId: crypto.randomUUID(), rowVersion: task.rowVersion, templateVersion: template.rowVersion, isReprint: reprint };
        dialog(reprint ? 'In lại tem' : 'Gửi lệnh in', `<p><strong>${lines.reduce((s, x) => s + x.quantity, 0)} tem</strong> · ${escape(template.design.name)}<br>Máy in: <strong>${escape(printer.name)}</strong><br>${template.design.widthMm}×${template.design.heightMm} mm · ${template.design.columns} cột</p>${reprint ? '<label>Lý do in lại<textarea id="reprintReason" class="form-control" maxlength="300" required></textarea></label><p>In lại không tăng tiến độ của lần in gốc.</p>' : '<p>Kiểm tra đúng cuộn giấy trên máy trước khi in.</p>'}`, async () => { if (reprint) body.reason = $('reprintReason').value; await sendJob(body); }, 'Gửi server in');
    }
    bind('printTask', () => preparePrint(false)); bind('reprintTask', () => preparePrint(true));
    bind('printTest', () => {
        const template = templates.find(x => x.id === Number($('testTemplate').value)), printer = printers.find(x => x.id === Number($('testPrinter').value));
        if (!template || !printer) throw new Error('Chọn mẫu đã lưu và máy in.');
        dialog('In thử một hàng tem?', `<p>${escape(template.design.name)} · ${template.design.columns} tem thử<br>Máy: ${escape(printer.name)}</p>`, async () => {
            await sendJob({ taskId: null, templateId: template.id, printerId: printer.id, lines: [], requestId: crypto.randomUUID(), rowVersion: null, templateVersion: template.rowVersion });
            location.href = '/admin/label-printing?tab=history';
        }, 'In thử');
    });
    function renderHistory(container, jobs) {
        container.innerHTML = jobs.length ? jobs.map(j => `<div class="label-job"><div class="label-toolbar"><div><strong>#${j.id} · ${j.quantity} tem ${j.isReprint ? '· In lại' : !j.taskId ? '· In thử' : ''}</strong> <span class="badge-label">${statuses[j.status]}</span><br><small>${escape(j.requestedByName)} · ${date(j.createdAtUtc)} · ${escape(j.payload.printer.name)}</small></div><div class="label-inline">${j.status === 0 ? `<button class="btn btn-sm btn-outline-danger" data-cancel-job="${j.id}">Hủy lệnh chờ</button>` : ''}${[2, 3].includes(j.status) || j.status === 1 ? `<button class="btn btn-sm btn-outline-primary" data-confirm-job="${j.id}">Xác nhận tem nhận được</button>` : ''}</div></div>${j.error ? `<p class="text-error mt-2">${escape(j.error)}</p>` : ''}<details><summary class="mt-2">Chi tiết mẫu, sản phẩm và kết quả</summary><p>${escape(j.payload.design.name)} · ${j.payload.design.widthMm}×${j.payload.design.heightMm} mm · ${j.payload.design.columns} cột<br>${escape(j.reason)}</p><ul>${j.payload.items.map(x => `<li>${escape(x.product.name)} · ${escape(x.product.unit)} · ${money(x.product.price)} đ · Gửi ${x.quantity}${j.status === 4 ? ' / Nhận ' + (j.result.find(r => r.variantId === x.product.variantId)?.quantity ?? 0) : ''}</li>`).join('')}</ul><small>Gửi máy in: ${date(j.sentAtUtc)} · Mã spool: ${j.spoolJobId ?? '—'}<br>Xác nhận: ${escape(j.confirmedByName || '—')} · ${date(j.confirmedAtUtc)}</small></details></div>`).join('') : '<p class="label-empty">Chưa có lần in nào.</p>';
        container._jobs = jobs;
    }
    async function loadHistory() { if ($('allHistory')) renderHistory($('allHistory'), await api('jobs')); }
    bind('reloadHistory', loadHistory);
    for (const container of [$('taskHistory'), $('allHistory')].filter(Boolean)) container.addEventListener('click', event => {
        const confirm = event.target.closest('[data-confirm-job]'), cancel = event.target.closest('[data-cancel-job]');
        const job = container._jobs?.find(x => x.id === Number(confirm?.dataset.confirmJob || cancel?.dataset.cancelJob)); if (!job) return;
        if (cancel) return dialog('Hủy lệnh đang chờ?', '<p>Chỉ hủy khi server chưa bắt đầu gửi lệnh.</p>', async () => {
            await api(`jobs/${job.id}/cancel`, 'POST', { rowVersion: job.rowVersion }); if (task) await loadTask(task.id); await loadHistory();
        });
        const initial = job.status === 2;
        dialog('Xác nhận số tem thực nhận', `<p>Kiểm tra tem đã ra đủ. Nếu lệnh chưa rõ kết quả, kiểm tra hàng đợi Windows và máy in trước khi ghi nhận; chỉ nhập số tem dùng được.</p>${job.payload.items.map(x => `<label class="d-block mb-2">${escape(x.product.name)} · đã gửi ${x.quantity}<input class="form-control received-qty" data-variant="${x.product.variantId}" type="number" min="0" max="${x.quantity}" step="1" value="${initial ? x.quantity : 0}" /></label>`).join('')}<label>Ghi chú nếu thiếu / lỗi<textarea id="confirmationNote" class="form-control" maxlength="500"></textarea></label>`, async () => {
            const lines = [...$('labelDialogContent').querySelectorAll('.received-qty')].map(x => ({ variantId: Number(x.dataset.variant), quantity: Number(x.value) }));
            await api(`jobs/${job.id}/confirm`, 'POST', { rowVersion: job.rowVersion, lines, note: $('confirmationNote').value });
            if (task) await loadTask(task.id); await loadHistory(); await loadTasks(); notice('Đã ghi nhận số tem thực nhận.');
        }, 'Lưu kết quả');
    });
    bind('addReceipt', async () => {
        const id = Number(new URLSearchParams(location.search).get('receipt'));
        const result = await api('receipts/' + id, 'POST'); $('receiptEntry').hidden = true; await loadTasks(); await loadTask(result.id);
    });
    window.addEventListener('beforeunload', event => { if (templateDirty || planDirty) { event.preventDefault(); event.returnValue = ''; } });
    async function start() {
        await Promise.all([loadTemplates(), loadPrinters(), loadTasks()]);
        if (isSettings) { showTemplate(templates[0]); await installedPrinters(); }
        const params = new URLSearchParams(location.search);
        if (!isSettings && params.get('task')) await loadTask(Number(params.get('task')));
        if (!isSettings && params.get('receipt')) { $('receiptEntry').hidden = false; $('receiptEntryText').textContent = 'Đưa phiếu nhập #' + Number(params.get('receipt')) + ' vào danh sách in tem. Nếu đã có, hệ thống mở lại phiếu in hiện tại.'; }
        if (params.get('tab')) tab(params.get('tab'));
    }
    start().catch(e => notice(e.message, true));
    setInterval(async () => {
        if (document.hidden || $('labelDialog').open) return;
        try {
            if (task) { const jobs = await api('jobs?taskId=' + task.id); renderHistory($('taskHistory'), jobs); }
            if (root.querySelector('[data-panel="history"]')?.hidden === false) await loadHistory();
        } catch { /* Manual actions surface connectivity errors; polling does not flash banners. */ }
    }, 5000);
})();
