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
        leftMarginMm: 1, rightMarginMm: 1, quantityMode: 'received', printerId: null, showPrintButton: true, barcodeFormat: 'AUTO', layout: 'standard', fontSize: 8, showName: true, showBarcode: true, showBarcodeText: true, showUnit: true, showPrice: true });
    const layouts = [...root.querySelectorAll('[data-layout]')];
    const layoutName = key => layouts.find(x => x.dataset.layout === key)?.dataset.layoutName || 'Cân đối';
    let templates = [], printers = [], tasks = [], task = null, editingTemplate = null, editingPrinter = null;
    let receiptEditor = null;
    const taskActivityTickets = new Map();
    const showActivityTask = id => window.dispatchEvent(new CustomEvent('gao:store-activity-ticket', {
        detail: id ? taskActivityTickets.get(id) || document.body.dataset.storeActivityTicket : document.body.dataset.storeActivityTicket
    }));
    let templateDirty = false, previewUrl = null, previewSequence = 0, dialogAction = null;
    const statuses = ['Chờ server in', 'Đang gửi máy in', 'Chờ xác nhận tem', 'Cần xử lý', 'Đã xác nhận', 'Đã hủy', 'Đã gửi in'];
    function notice(message, error = false) { $('labelMessage').textContent = message; $('labelMessage').classList.toggle('error', error); $('labelMessage').hidden = false; }
    async function api(path, method = 'GET', body, binary = false) {
        const response = await fetch('/admin/label-printing/' + path, { method, credentials: 'same-origin', headers: {
            'Content-Type': 'application/json', 'RequestVerificationToken': root.querySelector('[name="__RequestVerificationToken"]').value
        }, body: body === undefined ? undefined : JSON.stringify(body) });
        if (!response.ok) {
            const data = await response.json().catch(() => ({}));
            const error = new Error(data.message || data.detail || Object.values(data.errors || {}).flat().join(' ') || `Không thực hiện được (${response.status}).`);
            error.status = response.status; throw error;
        }
        const activityTicket = response.headers.get('X-Gao-Activity-Ticket');
        const activityTaskId = /^tasks\/(\d+)$/.exec(path)?.[1];
        if (activityTicket && activityTaskId) taskActivityTickets.set(Number(activityTaskId), activityTicket);
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
        showActivityTask(name === 'tasks' ? task?.id : null);
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
        options($('testTemplate'), templates, x => `${x.design.name} · ${x.layoutName || layoutName(x.design.layout)} · ${x.design.widthMm}×${x.design.heightMm} · ${x.design.columns} cột`, 'Chọn mẫu đã lưu');
        options($('templatePrinter'), printers, x => x.name + (x.enabled ? '' : ' · Đã tắt'), 'Chọn máy in gắn với mẫu');
    }
    async function loadTemplates() {
        templates = await api('templates'); fillSelects();
        if (!$('templateList')) return;
        $('templateList').innerHTML = templates.length ? templates.map(x => `<button class="label-template-card ${editingTemplate?.id === x.id ? 'active' : ''}" data-template="${x.id}"><strong>${escape(x.design.name)}</strong><small>${escape(layoutName(x.design.layout))} · ${x.design.widthMm} × ${x.design.heightMm} mm · ${x.design.columns} cột</small><small>${x.design.quantityMode === 'one' ? 'Mặc định 1 tem / sản phẩm' : x.design.quantityMode === 'custom' ? 'Nhân viên tự nhập' : 'Theo số lượng nhập quy đổi'}</small><small>${escape(printers.find(p => p.id === x.design.printerId)?.name || 'Chưa gắn máy in')}${x.design.showPrintButton === false ? ' · Đã ẩn nút in' : ''}</small></button>`).join('') : '<div class="label-empty">Chưa có mẫu. Chọn kiểu trình bày bên cạnh và lưu mẫu đầu tiên.</div>';
    }
    function readDesign() {
        const design = {};
        $('templateFields').querySelectorAll('[name]').forEach(x => design[x.name] = x.type === 'checkbox' ? x.checked : x.name === 'printerId' ? (Number(x.value) || null) : x.type === 'number' ? Number(x.value) : x.value);
        return design;
    }
    function showTemplate(item, preset) {
        editingTemplate = item || null;
        const design = { ...defaults(), ...preset, ...item?.design, barcodeFormat: 'AUTO' };
        $('templateFields').querySelectorAll('[name]').forEach(x => { if (x.type === 'checkbox') x.checked = design[x.name]; else x.value = design[x.name] ?? '';  });
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
        if (button.dataset.layout === 'retail-large') {
            const preset = JSON.parse($('newLargeTemplate').dataset.design);
            for (const key of ['widthMm', 'heightMm', 'fontSize']) $('templateFields').querySelector(`[name="${key}"]`).value = preset[key];
            const name = $('templateFields').querySelector('[name="name"]');
            if (!editingTemplate && name.value === defaults().name) name.value = preset.name;
        }
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
    bind('newLargeTemplate', () => {
        const open = () => showTemplate(null, JSON.parse($('newLargeTemplate').dataset.design));
        if (templateDirty) return dialog('Tạo mẫu lớn 50 × 30?', '<p>Bỏ thay đổi chưa lưu và tạo mẫu lớn riêng. Các mẫu đã lưu được giữ nguyên.</p>', open);
        open();
    });
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

    let taskPage = 1, taskListVersion = 0, taskSearchTimer;
    const stateText = { pending: 'Cần xử lý', sending: 'Đang gửi in', completed: 'Hoàn tất', attention: 'Cần kiểm tra' };
    function progressBar(p) {
        const handled = p.total ? p.handled / p.total * 100 : 0, skipped = p.total ? p.skipped / p.total * 100 : 0;
        return `<div class="label-progress" role="progressbar" aria-label="Sản phẩm đã xử lý hoặc bỏ qua" aria-valuemin="0" aria-valuemax="${p.total}" aria-valuenow="${p.handled + p.skipped}"><span style="width:${handled}%"></span><i style="width:${skipped}%"></i></div><small>${p.handled} đã xử lý · ${p.skipped} bỏ qua · <b>${p.pending} còn lại</b></small>`;
    }
    function renderTasks(data) {
        const stats = data.stats;
        $('labelTaskStats').innerHTML = [['total','Tổng phiếu','all'],['pending','Cần xử lý','pending'],['completed','Đã hoàn tất','completed'],['attention','Cần kiểm tra','attention']].map(([key, title, state]) => `<button type="button" class="label-stat ${state === $('taskState').value ? 'active' : ''}" data-state="${state}"><small>${title}</small><strong>${money(stats[key])}</strong><span>${state === 'completed' ? 'Có thể vào in lại' : state === 'attention' ? 'Kiểm tra lịch sử gửi in' : 'Xem danh sách →'}</span></button>`).join('');
        $('labelTaskList').innerHTML = `<div class="label-toolbar"><h3>${stateText[$('taskState').value] || 'Tất cả phiếu'} <span class="badge-label">${money(data.total)}</span></h3><small>Tiến độ tính theo sản phẩm</small></div>` + (tasks.length ? `<div class="label-table-scroll"><table class="table label-tasks-table"><thead><tr><th>Tên phiếu / Nhà cung cấp</th><th>Tiến độ sản phẩm</th><th>Trạng thái</th><th>Đưa vào lúc</th><th></th></tr></thead><tbody>${tasks.map(x => `<tr class="${task?.id === x.id ? 'label-current-task' : ''}"><td><strong>${escape(x.documentTitle || x.documentNo)}</strong><small>${escape(x.documentNo)}</small><small>${escape(x.supplierName || 'Chưa có nhà cung cấp')}</small></td><td><b>${x.progress.handled + x.progress.skipped} / ${x.progress.total}</b>${progressBar(x.progress)}</td><td><span class="label-state is-${x.state}">${stateText[x.state]}</span></td><td>${date(x.createdAtUtc)}</td><td><button class="btn btn-sm btn-outline-primary" data-task="${x.id}">${x.state === 'completed' ? 'Xem / In lại' : 'Mở phiếu in'}</button></td></tr>`).join('')}</tbody></table></div>` : '<div class="label-empty">Không có phiếu phù hợp. Đổi bộ lọc hoặc đưa phiếu nhập vào danh sách in tem.</div>') + `<div class="label-pagination"><span>Trang ${data.page} / ${Math.max(1, Math.ceil(data.total / data.pageSize))}</span><div><button class="btn btn-sm btn-outline-secondary" data-page="${data.page - 1}" ${data.page <= 1 ? 'disabled' : ''}>Trước</button> <button class="btn btn-sm btn-outline-secondary" data-page="${data.page + 1}" ${data.page * data.pageSize >= data.total ? 'disabled' : ''}>Sau</button></div></div>`;
    }
    async function loadTasks() {
        if (!$('labelTaskList')) return;
        const version = ++taskListVersion;
        const data = await api('workspace?' + new URLSearchParams({ q: $('taskSearch').value.trim(), state: $('taskState').value, page: taskPage }));
        if (version !== taskListVersion) return;
        tasks = data.items; taskPage = data.page; renderTasks(data);
    }
    const reloadFiltered = () => { taskPage = 1; loadTasks().catch(e => notice(e.message, true)); };
    $('taskSearch')?.addEventListener('input', () => { ++taskListVersion; clearTimeout(taskSearchTimer); taskSearchTimer = setTimeout(reloadFiltered, 300); });
    $('taskState')?.addEventListener('change', reloadFiltered);
    $('labelTaskStats')?.addEventListener('click', e => { const b = e.target.closest('[data-state]'); if (b) { $('taskState').value = b.dataset.state; reloadFiltered(); } });
    $('labelTaskList')?.addEventListener('click', e => { const b = e.target.closest('[data-page]'); if (b && !b.disabled) { taskPage = Number(b.dataset.page); loadTasks().catch(e => notice(e.message, true)); } });
    $('labelTaskList')?.addEventListener('click', event => {
        const button = event.target.closest('[data-task]'); if (!button) return;
        const run = () => loadTask(Number(button.dataset.task)).catch(e => notice(e.message, true));
        run();
    });
    function renderTask(preserveSelection = false) {
        $('labelTaskDetail').hidden = false; $('taskTitle').textContent = task.documentTitle || task.documentNo;
        $('taskSubtitle').textContent = task.documentNo + ' · ' + (task.supplierName || 'Chưa có nhà cung cấp');
        $('sourceReceipt').href = '/admin/stock-documents/' + task.stockDocumentId;
        $('sourceChanged').hidden = !task.sourceChanged && !task.provisionalCount;
        $('sourceChanged').textContent = task.sourceError || (task.provisionalCount ? `Phiếu còn ${task.provisionalCount} dòng hàng tạm chưa ghép danh mục.` : 'Phiếu hoặc giá bán đã đổi. Cập nhật từ phiếu nhập trước khi in.');
        if (preserveSelection && receiptEditor) receiptEditor.sync(task);
        else {
            receiptEditor?.dispose();
            const editor = document.createElement('div'); editor.id = 'taskPrintEditor'; $('taskPrintEditor').replaceWith(editor);
            receiptEditor = window.GaoLabelControls.receiptEditor(editor, task, templates, printers, { notice,
                onPrinted: async id => { await loadTask(id); await loadTasks(); await loadHistory(); },
                onChanged: async id => { await loadTask(id); await loadTasks(); } });
        }
        const p = task.progress;
        $('taskTotals').innerHTML = `<div><strong>${p.handled + p.skipped} / ${p.total} sản phẩm đã xử lý</strong><span class="label-state ${task.completed ? 'is-done' : 'is-pending'}">${task.completed ? 'Hoàn tất · vẫn có thể in lại' : 'Đang xử lý'}</span></div>${progressBar(p)}<p class="label-help">In lại chỉ ghi lịch sử, không cộng tiến độ. “Đã gửi in” là máy chủ đã chuyển lệnh tới máy in.</p>`;
        const actions = task.lines.flatMap(l => (l.actions || []).map(a => ({ ...a, variantId: l.product.variantId }))).sort((a, b) => b.atUtc.localeCompare(a.atUtc));
        $('taskResolutionHistory').innerHTML = actions.length ? actions.map(a => `<div class="label-audit-event"><span class="label-state ${a.action === 'skip' ? 'is-skipped' : 'is-pending'}">${a.action === 'skip' ? 'Bỏ qua' : 'Khôi phục'}</span><div><strong>${escape(a.productName)}</strong><p>${escape(a.reason)}</p><small>${escape(a.userName)} · ${date(a.atUtc)}</small></div></div>`).join('') : '<p class="label-empty">Chưa có thao tác bỏ qua hoặc khôi phục.</p>';
        renderHistory($('taskHistory'), task.jobs);
    }
    async function loadTask(id) {
        if (receiptEditor?.locked) throw new Error('Chờ kết quả hoặc thử lại lệnh in trước.');
        task = await api('tasks/' + id); renderTask(); history.replaceState(null, '', '?task=' + id);
        if (root.querySelector('[data-panel="tasks"]')?.hidden === false) showActivityTask(task.id);
    }
    bind('refreshSource', () => {
        if (receiptEditor?.locked) throw new Error('Chờ kết quả hoặc thử lại lệnh in trước.');
        if (task) dialog('Cập nhật từ phiếu nhập?', '<p>Lấy lại sản phẩm, mã vạch và giá bán hiện tại. Giữ trạng thái xử lý và toàn bộ lịch sử đã ghi nhận.</p>', async () => {
            task = await api(`tasks/${task.id}/refresh`, 'POST', { rowVersion: task.rowVersion }); renderTask(); await loadTasks();
        });
    });
    bind('reloadTasks', loadTasks);
    bind('closeTask', () => {
        if (receiptEditor?.locked) throw new Error('Chờ kết quả hoặc thử lại lệnh in trước.');
        receiptEditor?.dispose(); receiptEditor = null; task = null; $('labelTaskDetail').hidden = true;
        showActivityTask(null);
    });

    function dialog(title, content, action, accept = 'Xác nhận') {
        $('labelDialogContent').innerHTML = `<h2>${escape(title)}</h2>${content}`; dialogAction = action;
        $('dialogAccept').textContent = accept; $('dialogAccept').hidden = !action;
        if (!$('labelDialog').open) $('labelDialog').showModal();
    }
    bind('dialogAccept', async () => { await dialogAction?.(); $('labelDialog').close(); });
    bind('dialogClose', () => $('labelDialog').close());
    async function sendJob(body) {
        const result = await api('jobs', 'POST', body);
        notice(`Đã đưa lệnh #${result.id} vào hàng đợi server.`);
    }
    bind('printTest', () => {
        const template = templates.find(x => x.id === Number($('testTemplate').value)), printer = printers.find(x => x.id === template?.design.printerId);
        if (!template || !printer) throw new Error('Chọn mẫu đã lưu và máy in.');
        dialog('In thử một hàng tem?', `<p>${escape(template.design.name)} · ${template.design.columns} tem thử<br>Máy: ${escape(printer.name)}</p>`, async () => {
            await sendJob({ taskId: null, templateId: template.id, printerId: printer.id, lines: [], requestId: crypto.randomUUID(), rowVersion: null, templateVersion: template.rowVersion });
            location.href = '/admin/label-printing?tab=history';
        }, 'In thử');
    });
    function renderHistory(container, jobs) {
        const signature = JSON.stringify(jobs);
        if (container._signature === signature) return;
        container._signature = signature;
        container.innerHTML = jobs.length ? jobs.map(j => `<div class="label-job"><div class="label-toolbar"><div><strong>#${j.id} · ${j.quantity} tem ${j.isReprint ? '· In lại' : !j.taskId ? (j.payload.items.some(x => x.product.unitId != null) ? '· In nhanh sản phẩm' : '· In thử') : ''}</strong> <span class="badge-label">${statuses[j.status]}</span><br><small>${escape(j.requestedByName)} · ${date(j.createdAtUtc)} · ${escape(j.payload.printer.name)}</small></div><div class="label-inline">${j.status === 0 ? `<button class="btn btn-sm btn-outline-danger" data-cancel-job="${j.id}">Hủy lệnh chờ</button>` : ''}${[2, 3].includes(j.status) || j.status === 1 ? `<button class="btn btn-sm btn-outline-primary" data-confirm-job="${j.id}">Xác nhận tem nhận được</button>` : ''}</div></div>${j.error ? `<p class="text-error mt-2">${escape(j.error)}</p>` : ''}<details><summary class="mt-2">Chi tiết mẫu, sản phẩm và kết quả</summary><p>${escape(j.payload.design.name)} · ${j.payload.design.widthMm}×${j.payload.design.heightMm} mm · ${j.payload.design.columns} cột<br>${escape(j.reason)}</p><ul>${j.payload.items.map(x => `<li>${escape(x.product.name)} · ${escape(x.product.unit)} · ${money(x.product.price)} đ · Gửi ${x.quantity}${j.payload.productProgress ? x.countsForProgress ? ' · Xử lý lần đầu' : ' · In lại (không cộng tiến độ)' : ''}${j.status === 4 ? ' / Nhận ' + (j.result.find(r => r.variantId === x.product.variantId && (r.unitId ?? null) === (x.product.unitId ?? null))?.quantity ?? 0) : ''}</li>`).join('')}</ul><small>Gửi máy in: ${date(j.sentAtUtc)} · Mã spool: ${j.spoolJobId ?? '—'}<br>Xác nhận: ${escape(j.confirmedByName || '—')} · ${date(j.confirmedAtUtc)}</small></details></div>`).join('') : '<p class="label-empty">Chưa có lần in nào.</p>';
        container._jobs = jobs;
    }
    async function loadHistory() { if ($('allHistory')) renderHistory($('allHistory'), await api('jobs')); }
    bind('reloadHistory', loadHistory);
    for (const container of [$('taskHistory'), $('allHistory')].filter(Boolean)) container.addEventListener('click', event => {
        const confirm = event.target.closest('[data-confirm-job]'), cancel = event.target.closest('[data-cancel-job]');
        const job = container._jobs?.find(x => x.id === Number(confirm?.dataset.confirmJob || cancel?.dataset.cancelJob)); if (!job) return;
        if (cancel) return dialog('Hủy lệnh đang chờ?', '<p>Chỉ hủy khi server chưa bắt đầu gửi lệnh.</p>', async () => {
            await api(`jobs/${job.id}/cancel`, 'POST', { rowVersion: job.rowVersion }); if (task) await loadTask(task.id); await loadHistory(); await loadTasks();
        });
        const initial = job.status === 2;
        dialog('Xác nhận số tem thực nhận', `<p>Kiểm tra tem đã ra đủ. Nếu lệnh chưa rõ kết quả, kiểm tra hàng đợi Windows và máy in trước khi ghi nhận; chỉ nhập số tem dùng được.</p>${job.payload.items.map(x => `<label class="d-block mb-2">${escape(x.product.name)} · ${escape(x.product.unit)} · đã gửi ${x.quantity}<input class="form-control received-qty" data-variant="${x.product.variantId}" data-unit="${x.product.unitId ?? ''}" type="number" min="0" max="${x.quantity}" step="1" value="${initial ? x.quantity : 0}" /></label>`).join('')}<label>Ghi chú nếu thiếu / lỗi<textarea id="confirmationNote" class="form-control" maxlength="500"></textarea></label>`, async () => {
            const lines = [...$('labelDialogContent').querySelectorAll('.received-qty')].map(x => ({ variantId: Number(x.dataset.variant), unitId: x.dataset.unit ? Number(x.dataset.unit) : null, quantity: Number(x.value) }));
            await api(`jobs/${job.id}/confirm`, 'POST', { rowVersion: job.rowVersion, lines, note: $('confirmationNote').value });
            if (task) await loadTask(task.id); await loadHistory(); await loadTasks(); notice('Đã ghi nhận số tem thực nhận.');
        }, 'Lưu kết quả');
    });
    bind('addReceipt', async () => {
        const id = Number(new URLSearchParams(location.search).get('receipt'));
        const result = await api('receipts/' + id, 'POST'); $('receiptEntry').hidden = true; await loadTasks(); await loadTask(result.id);
    });
    window.addEventListener('beforeunload', event => { if (templateDirty) { event.preventDefault(); event.returnValue = ''; } });
    async function start() {
        await loadPrinters(); await loadTemplates();
        if (!isSettings) await window.initQuickLabelPrinting?.({ api, templates, printers, dialog, notice, loadHistory, tab });
        await loadTasks();
        if (isSettings) { showTemplate(templates[0]); await installedPrinters(); }
        const params = new URLSearchParams(location.search);
        if (!isSettings && params.get('task')) { tab('tasks'); await loadTask(Number(params.get('task'))); }
        if (!isSettings && params.get('receipt')) { tab('tasks'); $('receiptEntry').hidden = false; $('receiptEntryText').textContent = 'Đưa phiếu nhập #' + Number(params.get('receipt')) + ' vào danh sách in tem. Nếu đã có, hệ thống mở lại phiếu in hiện tại.'; }
        if (params.get('tab')) tab(params.get('tab'));
    }
    start().catch(e => notice(e.message, true));
    setInterval(async () => {
        if (document.hidden || document.querySelector('dialog[open]') || receiptEditor?.locked) return;
        try {
            if (task) {
                const id = task.id, state = await api(`tasks/${id}/state`);
                const stamp = jobs => JSON.stringify(jobs.map(j => [j.id, j.status, j.rowVersion]));
                if (task?.id === id && (state.rowVersion !== task.rowVersion || stamp(state.jobs) !== stamp(task.jobs))) {
                    const fresh = await api('tasks/' + id);
                    if (task?.id === id && !receiptEditor?.locked && !document.querySelector('dialog[open]')) { task = fresh; renderTask(true); await loadTasks(); }
                }
            }
            if (root.querySelector('[data-panel="history"]')?.hidden === false) await loadHistory();
        } catch { /* Manual actions surface connectivity errors; polling does not flash banners. */ }
    }, 5000);
})();
