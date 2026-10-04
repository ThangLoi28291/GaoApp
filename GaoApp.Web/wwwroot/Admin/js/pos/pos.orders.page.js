(() => {
    'use strict';
    const root = document.querySelector('.pos-orders-page');
    if (!root) return;
    const $ = id => root.querySelector('#' + id);
    const endpoint = '/admin/pos/orders';
    const state = { page: 1, pageSize: 20, total: 0, items: [], loaded: false, busy: false, applied: null, displayedPage: 1, displayedSize: 20 };
    let listSequence = 0, listController, previewSequence = 0, previewController, previewId = 0, previewTrigger, menuTrigger;
    const body = $('ordersBody'), table = body.closest('table'), dialog = $('orderPreview');
    const fields = ['keyword', 'fromDate', 'toDate', 'status', 'employee', 'employeeId', 'customer', 'terminal', 'terminalId', 'settlement'];
    const settlementLabels = { Cash: 'Tiền mặt', BankTransfer: 'Chuyển khoản', Credit: 'Đơn bán công nợ',
        OutstandingCredit: 'Công nợ — còn nợ', Mixed: 'Nhiều hình thức thanh toán', Card: 'Thẻ', EWallet: 'Ví điện tử', Other: 'Khác' };
    const num = value => Number.isFinite(Number(value)) ? Number(value) : 0;
    const positiveInt = (value, fallback = 1) => Number.isSafeInteger(Number(value)) && Number(value) > 0 ? Number(value) : fallback;
    const money = value => num(value).toLocaleString('vi-VN');
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const token = value => String(value ?? '').trim().toLowerCase().replace(/[\s_-]/g, '');
    const list = value => Array.isArray(value) ? value : [];
    const VIETNAM_TIME_ZONE = 'Asia/Ho_Chi_Minh';
    const detailUrl = id => '/admin/pos/order-detail/' + id;
    const icon = name => '<i class="bx bx-' + name + '" aria-hidden="true"></i>';
    const statuses = {
        completed: ['Đã chốt', 'success'], draft: ['Nháp', 'neutral'], onhold: ['Đang giữ', 'warning'],
        cancelled: ['Đã hủy', 'danger'], voided: ['Hủy sau chốt', 'danger'], refunded: ['Đã hoàn tiền', 'info']
    };
    const payments = {
        paid: ['Đã thanh toán', 'success'], unpaid: ['Chưa thanh toán', 'danger'],
        partial: ['Thanh toán một phần', 'warning'], partialpaid: ['Thanh toán một phần', 'warning'],
        partiallypaid: ['Thanh toán một phần', 'warning'], refunded: ['Đã hoàn tiền', 'info']
    };
    function badge(value, mapping) {
        const [label, tone] = mapping[token(value)] || [value || 'Chưa xác định', 'neutral'];
        return '<span class="po-badge po-badge-' + tone + '">' + esc(label) + '</span>';
    }
    function dateTime(value) {
        if (!value) return '—';
        const date = parseUtcDate(value);
        if (Number.isNaN(date.getTime())) return '—';
        return new Intl.DateTimeFormat('vi-VN', {
            timeZone: VIETNAM_TIME_ZONE,
            day: '2-digit',
            month: '2-digit',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
            hour12: false
        }).format(date).replace(', ', ' · ');
    }
    function parseUtcDate(value) {
        if (value instanceof Date) return value;
        const raw = String(value ?? '').trim();
        if (!raw) return new Date(NaN);
        return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(raw) ? raw : raw + 'Z');
    }
    function vietnamDateParts(value = new Date()) {
        const parts = new Intl.DateTimeFormat('en-CA', {
            timeZone: VIETNAM_TIME_ZONE,
            year: 'numeric',
            month: '2-digit',
            day: '2-digit'
        }).formatToParts(value).reduce((result, part) => {
            if (part.type !== 'literal') result[part.type] = Number(part.value);
            return result;
        }, {});
        return parts;
    }
    function dateInput(date) {
        return date.getUTCFullYear() + '-' + String(date.getUTCMonth() + 1).padStart(2, '0') + '-' + String(date.getUTCDate()).padStart(2, '0');
    }
    function period(type) {
        if (type === 'clear') return { fromDate: '', toDate: '' };
        const today = vietnamDateParts();
        const end = new Date(Date.UTC(today.year, today.month - 1, today.day));
        const start = new Date(end);
        start.setUTCDate(start.getUTCDate() - (type === '7days' ? 6 : type === '30days' ? 29 : 0));
        return { fromDate: dateInput(start), toDate: dateInput(end) };
    }
    function syncPeriods() {
        root.querySelectorAll('[data-range]').forEach(button => {
            const range = period(button.dataset.range);
            button.setAttribute('aria-pressed', String($('fromDate').value === range.fromDate && $('toDate').value === range.toDate));
        });
        const active = fields.filter(id => id !== 'keyword' && !id.endsWith('Id') && $(id).value).length;
        $('btnToggleFilters').innerHTML = icon('filter-alt') + ' Bộ lọc' + (active ? ' · ' + active : '');
    }
    function readFilters() {
        return Object.fromEntries(fields.map(name => [name, $(name).value.trim()]));
    }
    function validFilters() {
        $('toDate').setCustomValidity($('fromDate').value && $('toDate').value && $('fromDate').value > $('toDate').value
            ? 'Đến ngày phải bằng hoặc sau Từ ngày.' : '');
        if (!$('ordersFilters').checkValidity()) {
            $('ordersFilters').classList.add('po-filters-expanded');
            $('btnToggleFilters').setAttribute('aria-expanded', 'true');
        }
        return $('ordersFilters').reportValidity();
    }
    function restoreUrl() {
        const query = new URLSearchParams(location.search);
        fields.forEach(name => { $(name).value = query.get(name) || ''; });
        root.dispatchEvent(new Event('orders:filters-reset'));
        if (!$('status').value) $('status').value = '';
        state.page = positiveInt(query.get('page'), 1);
        state.pageSize = [10, 20, 50].includes(Number(query.get('pageSize'))) ? Number(query.get('pageSize')) : 20;
        $('pageSize').value = String(state.pageSize);
        syncPeriods();
    }
    function queryFor(filters) {
        const query = new URLSearchParams();
        fields.forEach(name => { if (filters[name]) query.set(name, filters[name]); });
        query.set('page', state.page); query.set('pageSize', state.pageSize);
        return query;
    }
    function updateSummary(filters) {
        const day = value => value.split('-').reverse().join('/');
        const range = filters.fromDate && filters.toDate ? day(filters.fromDate) + ' – ' + day(filters.toDate)
            : filters.fromDate ? 'Từ ' + day(filters.fromDate) : filters.toDate ? 'Đến ' + day(filters.toDate) : 'Tất cả thời gian';
        const status = statuses[token(filters.status)]?.[0] || 'Tất cả trạng thái';
        const parts = [range, status];
        if (filters.keyword) parts.push('“' + filters.keyword + '”');
        [['employee', 'Nhân viên'], ['customer', 'Khách hàng'], ['terminal', 'Máy tính tiền']].forEach(([key, label]) => {
            if (filters[key]) parts.push(label + ': ' + filters[key]);
        });
        if (settlementLabels[filters.settlement]) parts.push(settlementLabels[filters.settlement]);
        $('filterSummary').textContent = parts.join(' · ');
    }
    function updateMetrics() {
        $('metricCount').textContent = state.loaded ? money(state.total) : '—';
        [['metricTotal', 'grandTotal'], ['metricPaid', 'paidTotal'], ['metricDue', 'balanceDue']].forEach(([id, key]) => {
            $(id).innerHTML = state.loaded ? money(state.items.reduce((sum, order) => sum + num(order[key]), 0)) + '<small>đ</small>' : '—';
        });
        $('metricDue').classList.toggle('po-text-warning', state.loaded && state.items.some(order => num(order.balanceDue) > 0));
    }
    function updatePaging() {
        const page = state.loaded ? state.displayedPage : state.page;
        const size = state.loaded ? state.displayedSize : state.pageSize;
        const pages = Math.max(1, Math.ceil(state.total / size));
        const start = state.total && state.items.length ? (page - 1) * size + 1 : 0;
        const end = Math.min(state.total, start ? start + state.items.length - 1 : 0);
        $('totalOrdersText').textContent = state.loaded ? money(state.total) + ' đơn' : 'Chưa tải được';
        $('pagingInfo').textContent = state.loaded ? 'Hiển thị ' + start + '–' + end + ' trong ' + money(state.total) + ' đơn' : 'Chưa có dữ liệu';
        $('pageIndicator').textContent = 'Trang ' + page + ' / ' + pages;
        $('btnPrevPage').disabled = state.busy || !state.loaded || state.page <= 1;
        $('btnNextPage').disabled = state.busy || !state.loaded || state.page >= pages;
    }
    function setBusy(value) {
        state.busy = value;
        table.setAttribute('aria-busy', String(value));
        $('btnRefresh').disabled = value;
        $('btnRefresh').firstElementChild.classList.toggle('po-spin', value);
        $('btnSearch').innerHTML = icon(value ? 'loader-alt po-spin' : 'search') + (value ? ' Đang tìm…' : ' Tìm kiếm');
        updatePaging();
    }
    function stateMarkup(kind, message) {
        const error = kind === 'error';
        return '<div class="po-state"><span class="po-state-icon">' + icon(error ? 'wifi-off' : 'search-alt') + '</span>' +
            '<strong>' + (error ? 'Chưa tải được đơn hàng' : 'Không tìm thấy đơn hàng') + '</strong><p>' +
            esc(message || 'Thử đổi từ khóa, trạng thái hoặc khoảng thời gian để tìm lại.') + '</p>' +
            '<button type="button" class="po-button" data-' + (error ? 'retry-orders' : 'clear-orders') + '>' +
            icon(error ? 'refresh' : 'reset') + (error ? ' Thử lại' : ' Xóa bộ lọc') + '</button></div>';
    }
    function skeleton() {
        body.innerHTML = Array.from({ length: 5 }, () => '<tr class="po-skeleton-row" aria-hidden="true">' +
            '<td><span class="po-skeleton"></span><span class="po-skeleton"></span></td>'.repeat(6) + '</tr>').join('');
    }
    function extras(order, full = false) {
        const parts = [], vouchers = list(order.rewardVouchers ?? order.RewardVouchers);
        if (num(order.depositAmount) > 0) parts.push('<span class="po-extra">' + icon('wallet') +
            'Dùng cọc ' + money(order.depositAmount) + ' đ</span>');
        const refunded = num(order.refundedTotal), returns = num(order.returnCount);
        if (refunded > 0 || returns > 0) parts.push('<span class="po-extra po-extra-return" title="' +
            esc('Đã hoàn: ' + money(refunded) + ' đ · ' + returns + ' phiếu trả') + '">' + icon('undo') +
            (refunded > 0 ? 'Hoàn ' + money(refunded) + ' đ' : returns + ' phiếu trả') + '</span>');
        if (num(order.voucherDiscountTotal) > 0) parts.push('<span class="po-extra">' + icon('purchase-tag-alt') +
            'Giảm ' + money(order.voucherDiscountTotal) + ' đ</span>');
        if (vouchers.length) parts.push(...(full ? vouchers : [null]).map(voucher => '<span class="po-extra" title="' +
            esc(full ? voucher.voucherCode ?? voucher.VoucherCode : vouchers.map(v => v.voucherCode ?? v.VoucherCode).join(', ')) + '">' +
            icon('gift') + (full ? esc(voucher.voucherCode ?? voucher.VoucherCode) : vouchers.length + ' voucher') + '</span>'));
        return '<div class="po-extras">' + (parts.join('') || '<span class="po-none" aria-label="Không có hậu mãi hoặc voucher">—</span>') + '</div>';
    }
    function orderName(order) {
        return order.orderNumber || (token(order.status) === 'draft' ? 'Đơn nháp #' : 'Đơn #') + positiveInt(order.orderId);
    }
    function settlementBadges(order) {
        const tags = list(order.paymentMethods).map(method => '<span class="po-badge po-badge-' +
            (method === 'Cash' ? 'success' : 'info') + '">' + esc(settlementLabels[method] || 'Khác') + '</span>');
        if (num(order.depositAmount) > 0) tags.push('<span class="po-badge po-badge-info">Tiền đặt cọc</span>');
        if (order.isCreditSale) tags.push('<span class="po-badge po-badge-warning">' +
            (token(order.status) === 'completed' ? (num(order.balanceDue) > 0 ? 'Công nợ · còn nợ' : 'Công nợ · đã trả hết') : 'Đơn bán công nợ') + '</span>');
        if (!tags.length) tags.push('<span class="po-payment-unknown">' + (num(order.paidTotal) > 0 ? 'Chưa rõ hình thức' : 'Chưa ghi nhận thanh toán') + '</span>');
        return '<div class="po-settlement-tags">' + tags.join('') + '</div>';
    }
    function renderOrders() {
        if (!state.items.length) { body.innerHTML = '<tr><td colspan="6">' + stateMarkup('empty') + '</td></tr>'; return; }
        body.innerHTML = state.items.map(order => {
            const id = positiveInt(order.orderId, 0), name = esc(orderName(order)), due = num(order.balanceDue);
            const payToken = token(order.paymentStatus);
            return '<tr class="po-order-row" data-order-id="' + id + '" title="Nhấp đúp để xem nhanh đơn hàng">' +
                '<td><button type="button" class="po-order-link" data-preview-id="' + id + '" aria-label="Xem nhanh ' + name + '"' + (!id ? ' disabled' : '') + '><span class="po-order-number">' + name + '</span></button>' +
                '<span class="po-order-meta">' + icon('time-five') + esc(dateTime(order.createdAtUtc)) + '</span>' +
                '<span class="po-order-meta po-customer">' + icon('user') + esc(order.customerName || 'Khách lẻ') + '</span>' +
                (order.customerPhone ? '<span class="po-order-meta">' + icon('phone') + esc(order.customerPhone) + '</span>' : '') + '</td>' +
                '<td><span class="po-order-meta po-cashier">' + icon('user-circle') + esc(order.cashierName || 'Chưa rõ nhân viên') + '</span>' +
                '<span class="po-order-meta po-terminal">' + icon('desktop') + esc(order.terminalName || order.terminalCode || 'Chưa rõ máy') + '</span>' +
                '<div class="po-statuses">' + badge(order.status, statuses) + '<span class="po-payment-label">' + badge(order.paymentStatus, payments) + '</span></div></td>' +
                '<td class="po-align-right"><span class="po-mobile-label">Tổng tiền đơn</span><span class="po-amount">' + money(order.grandTotal) + ' <small>đ</small></span></td>' +
                '<td><div class="po-paid-line"><span>Đã trả</span><strong>' + money(order.paidTotal) + ' đ</strong></div>' +
                (due > 0 ? '<div class="po-due-line"><span>Còn thiếu</span><strong>' + money(due) + ' đ</strong></div>'
                    : '<span class="po-settled">' + icon('check') + (payToken === 'paid' ? 'Đã thanh toán đủ' : 'Không còn thiếu') + '</span>') + settlementBadges(order) + '</td>' +
                '<td>' + extras(order) + '</td>' +
                '<td><div class="po-actions"><button type="button" class="po-icon-button" data-print-order-id="' + id + '" aria-label="In lại ' + name + '" title="In lại hóa đơn"' + (!id ? ' disabled' : '') + '>' + icon('printer') + '</button>' +
                '<button type="button" class="po-icon-button po-action-trigger" data-menu-id="' + id + '" aria-label="Thao tác ' + name + '" aria-expanded="false" aria-controls="orderActions" title="Thao tác khác"' + (!id ? ' disabled' : '') + '>' + icon('dots-horizontal-rounded') + '</button></div></td></tr>';
        }).join('');
    }
    async function readJson(response) {
        if (response.status === 401 || response.redirected) throw new Error('Phiên đăng nhập đã hết. Hãy đăng nhập lại rồi tải lại trang.');
        if (response.status === 403) throw new Error('Bạn chưa có quyền xem dữ liệu này.');
        const payload = await response.json().catch(() => null);
        if (!response.ok) throw new Error(typeof payload?.message === 'string' ? payload.message : 'Không kết nối được máy chủ. Hãy thử lại sau.');
        if (!payload || typeof payload !== 'object') throw new Error('Máy chủ trả về dữ liệu không hợp lệ. Hãy tải lại.');
        return payload;
    }
    async function loadOrders({ useApplied = false, recoverPage = true } = {}) {
        if (!useApplied && !validFilters()) return;
        const filters = useApplied && state.applied ? { ...state.applied } : readFilters();
        const seq = ++listSequence;
        listController?.abort();
        const controller = listController = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 15000);
        closeMenu();
        setBusy(true);
        // Keep the previous page readable until the replacement arrives; do not flash an empty table/KPIs.
        $('ordersNotice').hidden = !state.loaded;
        $('ordersNotice').textContent = 'Đang tải dữ liệu mới…';
        if (!state.loaded) { updateMetrics(); skeleton(); }
        try {
            const response = await fetch(endpoint + '?' + queryFor(filters), { headers: { Accept: 'application/json' }, signal: controller.signal, cache: 'no-store' });
            const data = await readJson(response);
            if (seq !== listSequence) return;
            if (!Array.isArray(data.items) || !Number.isSafeInteger(data.totalItems) || data.totalItems < 0) throw new Error('Dữ liệu danh sách đơn không hợp lệ.');
            state.total = data.totalItems;
            state.page = positiveInt(data.page, state.page);
            state.pageSize = positiveInt(data.pageSize, state.pageSize);
            const lastPage = Math.max(1, Math.ceil(state.total / state.pageSize));
            if (state.page > lastPage && recoverPage) {
                state.page = lastPage; state.applied = filters;
                await loadOrders({ useApplied: true, recoverPage: false }); return;
            }
            state.items = data.items; state.loaded = true; state.applied = filters;
            state.displayedPage = state.page; state.displayedSize = state.pageSize;
            $('pageSize').value = String(state.pageSize);
            renderOrders(); updateMetrics(); updateSummary(filters);
            history.replaceState(null, '', location.pathname + '?' + queryFor(filters) + location.hash);
        } catch (error) {
            if (seq !== listSequence) return;
            state.items = []; state.total = 0; state.loaded = false;
            body.innerHTML = '<tr><td colspan="6">' + stateMarkup('error', error.name === 'AbortError'
                ? 'Máy chủ phản hồi chậm. Bạn có thể thử tải lại danh sách.' : error.message) + '</td></tr>';
            updateMetrics();
        } finally {
            window.clearTimeout(timeout);
            if (seq === listSequence) { listController = null; $('ordersNotice').hidden = true; setBusy(false); }
        }
    }

    // One top-layer popover avoids clipping the last row and keeps native light-dismiss/Escape.
    const menu = document.createElement('div');
    menu.id = 'orderActions'; menu.className = 'po-action-popover'; menu.setAttribute('popover', 'auto');
    root.append(menu);
    function closeMenu() {
        if (menu.matches(':popover-open')) menu.hidePopover();
        menuTrigger?.setAttribute('aria-expanded', 'false');
    }
    function showMenu(trigger, order) {
        if (menuTrigger === trigger && menu.matches(':popover-open')) { closeMenu(); return; }
        closeMenu(); menuTrigger = trigger;
        const id = positiveInt(order.orderId), completed = token(order.status) === 'completed';
        const completedAt = parseUtcDate(order.completedAtUtc);
        const elapsed = completedAt.getTime() === completedAt.getTime()
            ? Date.now() - completedAt.getTime()
            : Number.POSITIVE_INFINITY;
        const canVoid = completed && elapsed >= 0 && elapsed <= 15 * 60 * 1000;
        menu.innerHTML = '<button type="button" data-preview-id="' + id + '">' + icon('show') + 'Xem nhanh đơn hàng</button>' +
            '<a href="' + detailUrl(id) + '">' + icon('file') + 'Chi tiết đầy đủ</a>' +
            (order.hasBankTransfer === true ? '<a href="/admin/acb/payments/orders/' + id + '" data-acb-order-id="' + id + '">' + icon('transfer-alt') + 'Thông tin chuyển khoản</a>' : '') +
            (completed ? '<div class="po-menu-separator"></div><a href="' + detailUrl(id) + '" data-refund-order-id="' + id + '">' + icon('undo') + 'Hoàn tiền / trả hàng</a>' : '') +
            (canVoid ? '<a class="po-action-danger" href="' + detailUrl(id) + '" data-void-order-id="' + id + '">' + icon('x-circle') + 'Hủy sau chốt</a>' : '');
        menu.showPopover();
        const rect = trigger.getBoundingClientRect();
        menu.style.left = Math.max(8, Math.min(innerWidth - menu.offsetWidth - 8, rect.right - menu.offsetWidth)) + 'px';
        menu.style.top = Math.max(8, rect.bottom + menu.offsetHeight + 8 > innerHeight ? rect.top - menu.offsetHeight - 5 : rect.bottom + 5) + 'px';
        trigger.setAttribute('aria-expanded', 'true');
        menu.querySelector('button, a')?.focus({ preventScroll: true });
    }
    menu.addEventListener('keydown', event => {
        const links = [...menu.querySelectorAll('button, a')];
        const index = links.indexOf(document.activeElement);
        if (event.key === 'Escape') { event.preventDefault(); closeMenu(); menuTrigger?.focus({ preventScroll: true }); }
        else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault(); links[(index + (event.key === 'ArrowDown' ? 1 : links.length - 1)) % links.length]?.focus();
        }
    });
    menu.addEventListener('toggle', event => {
        if (event.newState === 'closed') menuTrigger?.setAttribute('aria-expanded', 'false');
    });
    window.addEventListener('resize', closeMenu);
    window.addEventListener('scroll', closeMenu, { passive: true });
    async function openPreview(id, trigger) {
        closeMenu(); previewController?.abort(); previewId = id; previewTrigger = trigger || previewTrigger;
        const order = state.items.find(item => Number(item.orderId) === id);
        $('previewTitle').textContent = order ? orderName(order) : 'Đơn #' + id;
        $('previewDetailLink').href = detailUrl(id);
        $('btnPreviewPrint').dataset.printOrderId = String(id);
        $('previewBody').innerHTML = '<div class="po-state" role="status">' + icon('loader-alt po-spin') + 'Đang tải chi tiết đơn…</div>';
        $('previewBody').setAttribute('aria-busy', 'true');
        $('previewBody').scrollTop = 0;
        if (!dialog.open) dialog.showModal();
        const seq = ++previewSequence, controller = previewController = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 15000);
        try {
            const data = await readJson(await fetch(endpoint + '/' + id, { headers: { Accept: 'application/json' }, signal: controller.signal, cache: 'no-store' }));
            if (seq !== previewSequence || !dialog.open) return;
            if (Number(data.orderId) !== id || !Array.isArray(data.lines)) throw new Error('Chi tiết đơn trả về không hợp lệ.');
            renderPreview(data);
        } catch (error) {
            if (seq !== previewSequence || !dialog.open) return;
            $('previewBody').innerHTML = '<div class="po-state"><span class="po-state-icon">' + icon('wifi-off') + '</span><strong>Chưa tải được chi tiết</strong><p>' +
                esc(error.name === 'AbortError' ? 'Máy chủ phản hồi chậm. Hãy thử lại.' : error.message) +
                '</p><button type="button" class="po-button" data-retry-preview>Thử lại</button></div>';
        } finally {
            window.clearTimeout(timeout);
            if (seq === previewSequence) { previewController = null; $('previewBody').setAttribute('aria-busy', 'false'); }
        }
    }
    function renderPreview(order) {
        const productCode = line => [line.scannedBarcode, line.barcode, line.sku]
            .map(value => String(value ?? '').trim()).find(Boolean) || '—';
        const meta = (label, value) => '<div><dt>' + label + '</dt><dd>' + esc(value || '—') + '</dd></div>';
        const total = (label, value, cls = '') => '<div class="' + cls + '"><dt>' + label + '</dt><dd>' + money(value) + ' đ</dd></div>';
        const paymentMethod = method => ({ cash: 'Tiền mặt', banktransfer: 'Chuyển khoản', card: 'Thẻ', ewallet: 'Ví điện tử' }[token(method)] || method);
        $('previewTitle').textContent = orderName(order);
        $('previewBody').innerHTML = '<div class="po-preview-status">' + badge(order.status, statuses) + badge(order.paymentStatus, payments) + '</div>' +
            '<dl class="po-preview-meta">' + meta('Khách hàng', order.customerName || 'Khách lẻ') + meta('Thu ngân', order.cashierName) +
            (order.customerPhone ? meta('Điện thoại', order.customerPhone) : '') + meta('Máy tính tiền', order.terminalName || order.terminalCode) +
            meta('Tạo lúc', dateTime(order.createdAtUtc)) + meta('Hoàn tất', dateTime(order.finalizedAtUtc)) + '</dl>' +
            (order.shiftCode ? '<details class="po-preview-section"><summary>Mã ca để tra cứu</summary><p class="po-preview-note">' + esc(order.shiftCode) + '</p></details>' : '') +
            '<section class="po-preview-section"><h3>Sản phẩm <span>' + order.lines.length + ' dòng hàng</span></h3><ul class="po-preview-lines">' +
            (order.lines.map(line => '<li><div><strong class="po-product-name">' + esc(line.itemName) + '</strong>' +
                '<small class="po-product-code">Mã: ' + esc(productCode(line)) + '</small>' +
                '<small>' + money(line.quantity) + ' ' + esc(line.sellingUnitName || line.unitName || '') + ' × ' + money(line.unitPrice) + ' đ</small></div>' +
                '<strong class="po-amount">' + money(line.lineTotal) + ' đ</strong></li>').join('') ||
                '<li><span class="po-text-muted">Đơn chưa có sản phẩm.</span></li>') + '</ul></section>' +
            '<section class="po-preview-section"><h3>Thanh toán</h3><dl class="po-preview-totals">' +
            total('Tạm tính', order.subtotal) + (num(order.discountTotal) ? total('Giảm giá', order.discountTotal) : '') +
            total('Tổng tiền', order.grandTotal, 'po-preview-grand') +
            (num(order.depositAmount) > 0 ? total('Trừ tiền cọc', order.depositAmount) + total('Cần trả sau cọc', Math.max(0, num(order.grandTotal) - num(order.depositAmount))) : '') +
            total(num(order.depositAmount) > 0 ? 'Đã thanh toán (gồm cọc)' : 'Đã thanh toán', order.paidTotal, 'po-text-success') +
            total('Còn thiếu', order.balanceDue, num(order.balanceDue) > 0 ? 'po-text-warning' : '') +
            (num(order.changeDue) > 0 ? total('Tiền thừa', order.changeDue) : '') +
            (num(order.refundedTotal) > 0 ? total('Đã hoàn tiền', order.refundedTotal, 'po-text-warning') : '') + '</dl></section>' +
            (list(order.payments).length ? '<section class="po-preview-section"><h3>Các khoản thanh toán</h3><ul class="po-preview-lines">' +
                order.payments.map(payment => '<li><div><strong>' + esc(paymentMethod(payment.method)) + '</strong><small>' + esc(dateTime(payment.createdAt)) +
                    (payment.reference ? ' · ' + esc(payment.reference) : '') + '</small></div><strong class="po-amount">' + money(payment.amount) + ' đ</strong></li>').join('') + '</ul></section>' : '') +
            (num(order.depositAmount) > 0 || num(order.voucherDiscountTotal) > 0 || list(order.rewardVouchers).length ? '<section class="po-preview-section"><h3>Cọc / Voucher</h3>' + extras(order, true) + '</section>' : '') +
            (order.note ? '<section class="po-preview-section"><h3>Ghi chú</h3><p class="po-preview-note">' + esc(order.note) + '</p></section>' : '');
    }
    dialog.addEventListener('close', () => {
        ++previewSequence; previewController?.abort(); previewId = 0;
        (previewTrigger?.isConnected ? previewTrigger : $('keyword')).focus({ preventScroll: true });
    });
    $('btnClosePreview').addEventListener('click', () => dialog.close());
    dialog.addEventListener('click', event => {
        if (event.target !== dialog || event.detail > 1) return;
        const rect = dialog.getBoundingClientRect();
        if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) dialog.close();
    });
    function printOrder(id, button) {
        if (button.dataset.busy === 'true') return;
        button.dataset.busy = 'true'; button.disabled = true;
        window.open(endpoint + '/' + id + '/print?autoPrint=false', '_blank', 'noopener');
        window.setTimeout(() => { button.dataset.busy = 'false'; button.disabled = false; }, 900);
    }
    function resetFilters() {
        $('ordersFilters').reset(); $('toDate').setCustomValidity('');
        fields.forEach(name => { $(name).value = ''; });
        root.dispatchEvent(new Event('orders:filters-reset'));
        state.page = 1; syncPeriods(); void loadOrders();
    }
    body.addEventListener('dblclick', event => {
        if (state.busy || dialog.open || event.button !== 0 || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
        // Buttons/links keep their own click behavior, including print and action menus.
        if (event.target.closest('button, a, input, select, textarea, label, [role="button"], [contenteditable]')) return;
        const row = event.target.closest('.po-order-row[data-order-id]');
        if (!row || !body.contains(row)) return;
        const id = positiveInt(row.dataset.orderId, 0);
        if (!id) return;
        event.preventDefault();
        void openPreview(id, row.querySelector('[data-preview-id]'));
    });
    root.addEventListener('click', event => {
        const target = event.target.closest('button, a');
        if (!target || target.disabled) return;
        if (target.hasAttribute('data-preview-id')) {
            const id = positiveInt(target.dataset.previewId, 0);
            if (id) void openPreview(id, menu.contains(target) ? menuTrigger : target);
        } else if (target.hasAttribute('data-menu-id')) {
            const order = state.items.find(item => String(item.orderId) === target.dataset.menuId);
            if (order) showMenu(target, order);
        } else if (target.hasAttribute('data-print-order-id')) {
            const id = positiveInt(target.dataset.printOrderId, 0);
            if (id) printOrder(id, target);
        } else if (target.hasAttribute('data-retry-orders')) void loadOrders();
        else if (target.hasAttribute('data-clear-orders')) resetFilters();
        else if (target.hasAttribute('data-retry-preview')) void openPreview(previewId);
        else if (target.hasAttribute('data-range')) {
            const range = period(target.dataset.range);
            $('fromDate').value = range.fromDate; $('toDate').value = range.toDate;
            $('toDate').setCustomValidity(''); syncPeriods(); state.page = 1; void loadOrders();
        } else if (menu.contains(target) && target.tagName === 'A') {
            const returnFocus = menuTrigger;
            closeMenu();
            if (target.hasAttribute('data-acb-order-id')) {
                // The shared ACB launcher runs on document after this delegated handler.
                window.setTimeout(() => document.querySelector('.acb-dialog[open]')?.addEventListener('close', () => {
                    if (returnFocus?.isConnected) returnFocus.focus({ preventScroll: true });
                }, { once: true }), 0);
            }
        }
    });
    $('ordersFilters').addEventListener('submit', event => { event.preventDefault(); state.page = 1; void loadOrders(); });
    $('btnResetFilters').addEventListener('click', resetFilters);
    $('btnToggleFilters').addEventListener('click', () => {
        const expanded = $('ordersFilters').classList.toggle('po-filters-expanded');
        $('btnToggleFilters').setAttribute('aria-expanded', String(expanded));
    });
    fields.forEach(id => $(id).addEventListener('change', syncPeriods));
    $('btnRefresh').addEventListener('click', () => void loadOrders({ useApplied: true }));
    $('pageSize').addEventListener('change', () => { state.pageSize = Number($('pageSize').value); state.page = 1; void loadOrders({ useApplied: true }); });
    $('btnPrevPage').addEventListener('click', () => { if (!state.busy && state.page > 1) { state.page--; void loadOrders({ useApplied: true }); } });
    $('btnNextPage').addEventListener('click', () => { if (!state.busy && state.page * state.pageSize < state.total) { state.page++; void loadOrders({ useApplied: true }); } });
    ['fromDate', 'toDate'].forEach(id => $(id).addEventListener('input', () => { $('toDate').setCustomValidity(''); syncPeriods(); }));
    window.addEventListener('popstate', () => { restoreUrl(); void loadOrders(); });
    restoreUrl();
    void loadOrders();
})();
