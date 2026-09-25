(() => {
    'use strict';
    if (!document.querySelector('[data-xml-stock]')) return;
    const $ = id => document.getElementById(id);
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const num = value => Number(value || 0).toLocaleString('vi-VN', { maximumFractionDigits: 4 });
    const date = value => { const d = new Date(value); return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' }); };
    const labels = { increase: 'Nhập từ XML', decrease: 'Đã phát hành', hold: 'Đang giữ' };
    const params = new URLSearchParams(location.search);
    let view = params.get('view') === 'ledger' ? 'ledger' : 'balance', page = Math.max(1, Number(params.get('page')) || 1), totalPages = 1, rows = [], controller, sequence = 0, returnFocus;
    let productId = Number(params.get('productVariantId')) || null;
    $('xsKeyword').value = params.get('keyword') || '';
    $('xsFrom').value = params.get('fromDate') || ''; $('xsTo').value = params.get('toDate') || '';
    if (['10', '20', '50', '100'].includes(params.get('pageSize'))) $('xsPageSize').value = params.get('pageSize');
    const initialWarehouse = Number(params.get('warehouseId')) || null;
    if (initialWarehouse) $('xsWarehouse').add(new Option('Kho #' + initialWarehouse, String(initialWarehouse)));
    $('xsWarehouse').value = initialWarehouse ? String(initialWarehouse) : '';
    function setView(next, kind = '') {
        view = next;
        document.querySelectorAll('[data-view]').forEach(button => { const active = button.dataset.view === view; button.classList.toggle('active', active); button.setAttribute('aria-selected', String(active)); });
        document.querySelectorAll('.xs-date').forEach(label => { label.hidden = view !== 'ledger'; });
        $('xsKind').innerHTML = '<option value="">Tất cả</option>' + (view === 'ledger'
            ? '<option value="increase">Tăng từ XML</option><option value="decrease">Giảm do phát hành</option><option value="hold">Đang giữ</option>'
            : '<option value="hold">Đang giữ</option><option value="negative">Khả dụng âm</option>');
        $('xsKind').value = kind; if ($('xsKind').selectedIndex < 0) $('xsKind').value = '';
        $('xsHeading').textContent = view === 'ledger' ? 'Lịch sử tăng / giảm tồn XML' : 'Tồn XML theo sản phẩm';
        $('xsHint').textContent = view === 'ledger' ? 'Tồn trước → sau tính theo toàn bộ lịch sử. Nhấp đúp để xem chứng từ.' : 'Nhấp đúp một dòng để xem nhanh và truy vết chứng từ.';
        const columns = view === 'ledger' ? ['Ngày ghi nhận', 'Sản phẩm', 'Kho', 'Biến động', 'Tồn trước → sau', 'Nguồn / chứng từ', '']
            : ['Sản phẩm', 'Kho', 'Tồn đầu', 'Nhập / tăng', 'Xuất / giảm', 'Đang giữ', 'Còn khả dụng', ''];
        $('xsHead').innerHTML = '<tr>' + columns.map(x => `<th scope="col">${esc(x)}</th>`).join('') + '</tr>';
    }
    const cell = (label, content, css = '') => `<td data-label="${esc(label)}" class="${css}">${content}</td>`;
    const product = row => `<strong class="xs-name">${esc(row.productName)}</strong><small class="xs-sub">Mã: ${esc(row.code || '—')} · ${esc(row.baseUnit)}</small>`;
    const warehouse = row => `${esc(row.warehouseName)}<small class="xs-sub">${esc(row.legalEntityName)}</small>`;
    const badge = row => `<span class="xs-badge ${esc(row.kind)}">${esc(row.operationLabel || labels[row.kind] || '')}</span>`;
    function render() {
        $('xsBody').innerHTML = rows.length ? rows.map((row, index) => {
            const action = cell('', `<button type="button" class="xs-eye" data-preview="${index}" aria-label="Xem ${esc(row.productName)}"><i class="bx bx-show" aria-hidden="true"></i></button>`, 'action');
            const content = view === 'balance'
                ? cell('Sản phẩm', product(row), 'product') + cell('Kho', warehouse(row))
                    + cell('Tồn đầu', num(row.opening), `num ${row.opening < 0 ? 'xs-bad' : ''}`)
                    + cell('Nhập / tăng', num(row.received), 'num xs-good')
                    + cell('Xuất / giảm', num(row.issued), 'num') + cell('Đang giữ', num(row.held), 'num xs-warn')
                    + cell('Còn khả dụng', `<strong>${num(row.available)}</strong><small class="xs-sub">Tồn XML: ${num(row.remaining)}</small>`, `num ${row.available < 0 ? 'xs-bad' : 'xs-good'}`)
                : cell('Ngày ghi nhận', esc(date(row.dateUtc))) + cell('Sản phẩm', product(row), 'product') + cell('Kho', warehouse(row))
                    + cell('Biến động', `<strong>${row.kind === 'hold' ? 'Giữ ' + num(row.held) : (row.change > 0 ? '+' : '') + num(row.change)}</strong><small class="xs-sub">${badge(row)}</small>`, row.kind === 'hold' ? 'xs-warn' : row.change > 0 ? 'xs-good' : 'xs-bad')
                    + cell('Tồn trước → sau', `${num(row.before)} <span aria-hidden="true">→</span> <strong class="${row.after < 0 ? 'xs-bad' : ''}">${num(row.after)}</strong>`, 'num')
                    + cell('Nguồn / chứng từ', `${esc(row.sourceCode)}${row.xmlNumber ? `<small class="xs-sub">XML: ${esc(row.xmlNumber)}</small>` : ''}`);
            return `<tr data-index="${index}">${content}${action}</tr>`;
        }).join('') : `<tr><td colspan="${view === 'balance' ? 8 : 7}" class="xs-empty"><i class="bx bx-file-find" aria-hidden="true"></i>Chưa có dữ liệu phù hợp.<small class="xs-sub">Chỉ các dòng nhập đã duyệt và map đủ chi tiết XML mới được ghi tăng.</small></td></tr>`;
    }
    async function load(reset = false) {
        if (view === 'ledger' && $('xsFrom').value && $('xsTo').value && $('xsFrom').value > $('xsTo').value) { $('xsDateError').hidden = false; return; }
        $('xsDateError').hidden = true;
        if (reset) page = 1;
        controller?.abort(); controller = new AbortController(); const active = controller, ticket = ++sequence;
        const query = new URLSearchParams({ view, page: String(page), pageSize: $('xsPageSize').value });
        [['keyword', 'xsKeyword'], ['warehouseId', 'xsWarehouse'], ['kind', 'xsKind']].forEach(([key, id]) => { if ($(id).value.trim()) query.set(key, $(id).value.trim()); });
        if (view === 'ledger') [['fromDate', 'xsFrom'], ['toDate', 'xsTo']].forEach(([key, id]) => { if ($(id).value) query.set(key, $(id).value); });
        if (productId) query.set('productVariantId', String(productId));
        $('xsClearProduct').hidden = !productId; $('xsResults').setAttribute('aria-busy', 'true');
        $('xsPrevious').disabled = true; $('xsNext').disabled = true;
        rows = []; $('xsBody').innerHTML = `<tr><td colspan="${view === 'balance' ? 8 : 7}" class="xs-empty">Đang tải dữ liệu…</td></tr>`;
        const timeout = setTimeout(() => active.abort(), 20000);
        try {
            const response = await fetch('/admin/invoice-input-stock/data?' + query, { signal: active.signal, headers: { Accept: 'application/json' } });
            if (!response.ok || !(response.headers.get('content-type') || '').includes('application/json')) throw new Error('Không tải được dữ liệu. Kiểm tra kết nối hoặc phiên đăng nhập.');
            const data = await response.json(); if (ticket !== sequence) return;
            page = data.page; totalPages = data.totalPages; rows = view === 'ledger' ? data.movements : data.balances;
            $('xsProducts').textContent = num(data.productCount); $('xsIn').textContent = num(data.increaseCount);
            $('xsOut').textContent = num(data.decreaseCount); $('xsHold').textContent = num(data.holdCount);
            $('xsCount').textContent = num(data.totalItems) + ' kết quả'; $('xsSummary').textContent = `Trang ${page}/${totalPages} · ${num(data.totalItems)} kết quả`;
            const selectedWarehouse = $('xsWarehouse').value;
            $('xsWarehouse').replaceChildren(new Option('Tất cả kho', ''), ...data.warehouses.map(x => new Option(x.name, String(x.id))));
            if (selectedWarehouse && ![...$('xsWarehouse').options].some(x => x.value === selectedWarehouse)) $('xsWarehouse').add(new Option('Kho #' + selectedWarehouse, selectedWarehouse));
            $('xsWarehouse').value = selectedWarehouse;
            $('xsNegative').hidden = !data.negativeCount;
            $('xsNegative').textContent = `${num(data.negativeCount)} sản phẩm / kho có khả dụng âm. Kiểm tra lại nguồn XML đã map và các hóa đơn đã phát hành.`;
            $('xsPrevious').disabled = page <= 1; $('xsNext').disabled = page >= totalPages;
            query.set('page', String(page)); history.replaceState(null, '', location.pathname + '?' + query);
            render();
        } catch (error) {
            if (ticket !== sequence) return;
            ['xsProducts', 'xsIn', 'xsOut', 'xsHold'].forEach(id => $(id).textContent = '—');
            $('xsCount').textContent = 'Chưa tải được'; $('xsSummary').textContent = ''; $('xsNegative').hidden = true;
            $('xsBody').innerHTML = `<tr><td colspan="${view === 'balance' ? 8 : 7}" class="xs-empty">${esc(error.name === 'AbortError' ? 'Máy chủ phản hồi quá lâu.' : error.message)}<br><button type="button" data-retry class="btn btn-label-primary mt-3">Thử lại</button></td></tr>`;
        } finally { clearTimeout(timeout); if (ticket === sequence) $('xsResults').setAttribute('aria-busy', 'false'); }
    }
    function preview(index, focus) {
        const row = rows[index]; if (!row) return;
        returnFocus = focus; $('xsDialogTitle').textContent = row.productName;
        const pairs = [['Mã sản phẩm', row.code || '—'], ['Kho', row.warehouseName], ['Đơn vị gốc', row.baseUnit], ['Chủ thể', row.legalEntityName || '—']];
        if (view === 'balance') pairs.push(['Tồn đầu chuyển sang', num(row.opening)], ['Nhập / tăng', num(row.received)], ['Xuất / giảm', num(row.issued)], ['Tồn XML', num(row.remaining)], ['Đang giữ', num(row.held)], ['Còn khả dụng', num(row.available)]);
        else pairs.push(['Ngày ghi nhận', date(row.dateUtc)], ['Nghiệp vụ', row.operationLabel || labels[row.kind]], ['Chứng từ', row.sourceCode], ['Biến động', row.kind === 'hold' ? 'Giữ ' + num(row.held) : num(row.change)], ['Tồn trước → sau', `${num(row.before)} → ${num(row.after)}`]);
        if (view === 'ledger') {
            if (row.legacySourceKey) {
                pairs.push(['OrderID (GaoStore)', row.legacyOrderId || 'Không áp dụng'],
                    ['Số hóa đơn (GaoStore)', row.legacyInvoiceNumber || 'Không có'],
                    ['Ký hiệu hóa đơn (GaoStore)', row.legacyInvoiceSymbol || 'Không có'],
                    ['Khóa nguồn đối chiếu', row.legacySourceKey]);
            } else pairs.push(['Hóa đơn XML', row.xmlNumber || '—']);
        }
        $('xsDialogBody').innerHTML = '<div class="xs-detail-grid">' + pairs.map(([label, value]) => `<div><small>${esc(label)}</small><strong>${esc(value)}</strong></div>`).join('') + '</div>' + (row.note ? `<p class="xs-note">${esc(row.note)}</p>` : '');
        $('xsDialogActions').replaceChildren();
        const historyButton = document.createElement('button'); historyButton.className = 'btn btn-primary'; historyButton.textContent = 'Lịch sử sản phẩm';
        historyButton.onclick = () => { $('xsDialog').close(); productId = row.productVariantId; $('xsWarehouse').value = String(row.warehouseId); $('xsKeyword').value = ''; $('xsFrom').value = ''; $('xsTo').value = ''; setView('ledger'); load(true); };
        $('xsDialogActions').append(historyButton);
        const link = row.stockDocumentId ? '/admin/stock-documents/' + Number(row.stockDocumentId) : row.invoiceHeadId ? '/Admin/Invoice/Detail/' + Number(row.invoiceHeadId) : null;
        if (link) { const a = document.createElement('a'); a.href = link; a.className = 'btn btn-label-primary'; a.textContent = 'Mở chứng từ'; a.target = '_blank'; a.rel = 'noopener'; $('xsDialogActions').append(a); }
        if (!$('xsDialog').open) $('xsDialog').showModal();
    }
    $('xsBody').addEventListener('click', event => { if (event.target.closest('[data-retry]')) load(); const button = event.target.closest('[data-preview]'); if (button) preview(Number(button.dataset.preview), button); });
    $('xsBody').addEventListener('dblclick', event => { if (event.target.closest('button,a,input,select')) return; const row = event.target.closest('[data-index]'); if (row) preview(Number(row.dataset.index), row.querySelector('button')); });
    $('xsClose').onclick = () => $('xsDialog').close();
    $('xsDialog').addEventListener('close', () => { if (returnFocus?.isConnected) returnFocus.focus(); });
    $('xsDialog').addEventListener('click', event => { if (event.detail > 1 || event.target !== $('xsDialog')) return; const r = $('xsDialog').getBoundingClientRect(); if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) $('xsDialog').close(); });
    $('xsFilters').onsubmit = event => { event.preventDefault(); load(true); };
    $('xmlReload').onclick = () => load();
    $('xsReset').onclick = () => { $('xsFilters').reset(); productId = null; setView(view); load(true); };
    $('xsClearProduct').onclick = () => { productId = null; load(true); };
    $('xsPrevious').onclick = () => { if (page > 1) { page--; load(); } }; $('xsNext').onclick = () => { if (page < totalPages) { page++; load(); } };
    document.querySelectorAll('[data-view]').forEach(button => button.onclick = () => { setView(button.dataset.view); load(true); });
    document.querySelectorAll('[data-metric]').forEach(button => button.onclick = () => { const kind = button.dataset.metric; setView(kind === 'balance' ? 'balance' : 'ledger', kind === 'balance' ? '' : kind); load(true); });
    setView(view, params.get('kind') || ''); load();
})();
