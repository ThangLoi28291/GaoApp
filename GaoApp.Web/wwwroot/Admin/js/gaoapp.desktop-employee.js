/* One employee Home read owner across phone and desktop; existing shift guards apply. */
(function () {
    'use strict';
    const root = document.querySelector('[data-employee-home]');
    if (!root) return;
    const shift = root.querySelector('[data-employee-shift]');
    const state = root.querySelector('[data-employee-state]');
    const overview = root.querySelector('[data-employee-overview]');
    const stats = root.querySelector('[data-employee-statistics]');
    const refresh = root.querySelector('[data-employee-refresh]');
    const reload = root.querySelector('[data-employee-reload]');
    const userId = Number(root.dataset.employeeUser);
    const money = new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 });
    const count = new Intl.NumberFormat('vi-VN');
    const date = new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    const time = new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit', second: '2-digit' });
    const countKeys = ['totalOrders', 'draftOrders', 'completedOrders', 'cancelledOrders', 'refundCount', 'voidCount'];
    const moneyKeys = ['completedSalesTotal', 'openingCash', 'cashSalesTotal', 'nonCashSalesTotal', 'cashRefundTotal', 'nonCashRefundTotal', 'refundTotal', 'cashInTotal', 'cashOutTotal', 'closingCashExpected'];
    const orderStatuses = ['Draft', 'OnHold', 'Completed', 'Cancelled', 'Voided', 'Refunded'];
    const orderLabels = ['Đang soạn', 'Đang giữ', 'Hoàn tất', 'Hủy trước chốt', 'Hủy sau chốt', 'Hoàn tiền'];
    let pending = null, blocked = false;
    const text = (selector, value) => { const element = root.querySelector(selector); if (element) element.textContent = value; };
    const statusIndex = value => typeof value === 'number' ? value : orderStatuses.indexOf(value);
    function instant(value) {
        if (typeof value !== 'string') throw new Error('Invalid date');
        const result = new Date(/[Zz]|[+-]\d{2}:?\d{2}$/.test(value) ? value : value + 'Z');
        if (!Number.isFinite(result.getTime())) throw new Error('Invalid date');
        return result;
    }
    function clear() {
        overview.hidden = true;
        if (stats) stats.hidden = true;
        root.querySelectorAll('[data-employee-metric]').forEach(element => { element.textContent = '—'; });
        for (const key of ['code', 'owner', 'opened', 'terminal', 'warehouse', 'updated']) text(`[data-employee-${key}]`, '—');
        root.querySelector('[data-employee-orders]')?.replaceChildren();
        root.querySelector('[data-employee-transactions]')?.replaceChildren();
    }
    function message(title, detail, kind, needsReload = false) {
        clear(); state.hidden = false; state.dataset.kind = kind || '';
        text('[data-employee-state-title]', title); text('[data-employee-state-message]', detail);
        if (reload) reload.hidden = !needsReload;
    }
    function element(tag, value) { const result = document.createElement(tag); if (value !== undefined) result.textContent = value; return result; }
    function validate(current, summary) {
        if (!current || !summary || !Number.isInteger(userId) || userId <= 0 || current.openedByUserId !== userId || summary.openedByUserId !== userId
            || !Number.isInteger(current.id) || current.id <= 0 || current.id !== summary.id
            || ![1, 'Open'].includes(current.status) || ![1, 'Open'].includes(summary.status)
            || current.openedAtUtc !== summary.openedAtUtc) throw new Error('Shift context changed');
        for (const key of [...moneyKeys, ...countKeys]) {
            if (typeof summary[key] !== 'number' || !Number.isFinite(summary[key])) throw new Error('Invalid metric');
            if (countKeys.includes(key) && (!Number.isInteger(summary[key]) || summary[key] < 0)) throw new Error('Invalid count');
        }
        if (!Array.isArray(summary.orders) || !Array.isArray(summary.cashTransactions)) throw new Error('Invalid detail');
        for (const row of summary.orders) {
            if (typeof row.grandTotal !== 'number' || !Number.isFinite(row.grandTotal) || typeof row.orderNumber !== 'string') throw new Error('Invalid order');
            instant(row.createdAtUtc);
        }
        for (const row of summary.cashTransactions) {
            if (row.posShiftId !== current.id || typeof row.amount !== 'number' || !Number.isFinite(row.amount)) throw new Error('Invalid transaction');
            instant(row.createdAtUtc);
        }
        instant(current.openedAtUtc);
    }
    function render(current, summary) {
        validate(current, summary);
        const values = { ...summary, heldOrders: summary.orders.filter(row => statusIndex(row.status) === 1).length,
            refundedOrders: summary.orders.filter(row => statusIndex(row.status) === 5).length };
        for (const item of root.querySelectorAll('[data-employee-metric]')) {
            const key = item.dataset.employeeMetric;
            item.textContent = moneyKeys.includes(key) ? money.format(values[key]) : count.format(values[key]);
        }
        text('[data-employee-code]', current.shiftCode || `Ca #${current.id}`);
        text('[data-employee-owner]', current.openedByUserName || 'Ca do bạn phụ trách');
        text('[data-employee-opened]', date.format(instant(current.openedAtUtc)));
        text('[data-employee-terminal]', current.terminalName || current.terminalCode || 'Quầy hiện tại');
        text('[data-employee-warehouse]', current.warehouseName || current.warehouseCode || 'Kho của ca');
        text('[data-employee-updated]', time.format(new Date()));
        const orders = root.querySelector('[data-employee-orders]'); orders.replaceChildren();
        for (const row of [...summary.orders].sort((a, b) => instant(b.createdAtUtc) - instant(a.createdAtUtc) || b.id - a.id).slice(0, 6)) {
            const tr = element('tr'); tr.append(element('td', row.orderNumber), element('td', orderLabels[statusIndex(row.status)] || 'Khác'), element('td', money.format(row.grandTotal))); orders.append(tr);
        }
        if (!summary.orders.length) { const tr = element('tr'), td = element('td', 'Chưa có đơn hàng trong ca.'); td.colSpan = 3; tr.append(td); orders.append(tr); }
        const transactions = root.querySelector('[data-employee-transactions]'); transactions.replaceChildren();
        for (const row of [...summary.cashTransactions].sort((a, b) => instant(b.createdAtUtc) - instant(a.createdAtUtc) || b.id - a.id).slice(0, 6)) {
            const li = element('li'), caption = element('div');
            const isIn = row.type === 'CashIn' || row.type === 1;
            caption.append(element('strong', row.reason || (isIn ? 'Thu vào két' : 'Chi khỏi két')), element('small', date.format(instant(row.createdAtUtc))));
            const amount = element('span', `${isIn ? '+' : '−'}${money.format(row.amount)}`); amount.className = isIn ? 'is-in' : 'is-out';
            li.append(caption, amount); transactions.append(li);
        }
        if (!summary.cashTransactions.length) transactions.append(element('li', 'Chưa có giao dịch thu / chi trong ca.'));
        state.hidden = true; overview.hidden = false; stats.hidden = false;
    }
    async function read(address, signal) {
        const url = new URL(address, location.href);
        if (url.origin !== location.origin || url.search) throw new Error('Invalid shift endpoint');
        const response = await fetch(url.href, { credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal, headers: { Accept: 'application/json' } });
        if (response.status === 401 || response.status === 403) throw Object.assign(new Error('Access changed'), { access: true });
        if (response.status === 204) return null;
        if (!response.headers.get('content-type')?.includes('application/json') || response.redirected) throw new Error('Invalid response');
        const data = await response.json();
        if (!response.ok) throw Object.assign(new Error('Shift unavailable'), { code: data.errorCode || data.code || data.error?.code, status: response.status });
        return data;
    }
    function cancel() { if (pending) { clearTimeout(pending.timer); pending.controller.abort(); pending = null; } shift.setAttribute('aria-busy', 'false'); }
    async function load() {
        if (document.hidden || blocked || !shift.dataset.currentUrl || !shift.dataset.summaryUrl || !stats) return;
        cancel();
        const operation = { controller: new AbortController() }; pending = operation;
        const owns = () => pending === operation && !document.hidden;
        operation.timer = setTimeout(() => operation.controller.abort(), 18000);
        message('Đang tải ca làm việc…', 'Kiểm tra ca và cập nhật số liệu mới nhất.');
        shift.setAttribute('aria-busy', 'true'); refresh.disabled = true;
        try {
            const current = await read(shift.dataset.currentUrl, operation.controller.signal);
            if (!owns() || operation.controller.signal.aborted) return;
            if (!current) { message('Bạn chưa mở ca trên quầy này', 'Vào Ca làm việc để mở hoặc nhận ca trước khi bán hàng.', 'empty'); return; }
            const summary = await read(shift.dataset.summaryUrl, operation.controller.signal);
            if (owns() && !operation.controller.signal.aborted) render(current, summary);
        } catch (error) {
            if (!owns()) return;
            if (error.access) { blocked = true; message('Phiên đăng nhập hoặc quyền xem ca đã thay đổi', 'Tải lại trang để xác minh tài khoản trước khi tiếp tục.', 'error', true); }
            else if (error.status === 409) message('Ca trên quầy đã thay đổi', 'Ca đang thuộc tài khoản khác hoặc vừa được bàn giao. Vào Ca làm việc để kiểm tra.', 'empty');
            else if (error.code === 'POS_CONTEXT_TERMINAL_NOT_RESOLVED') message('Chưa xác định quầy POS', 'Đăng nhập lại và chọn đúng quầy làm việc để xem ca.', 'empty');
            else message('Chưa tải được ca làm việc', 'Bấm Làm mới ca để thử lại. Số liệu chỉ hiển thị khi xác minh được đúng ca của bạn.', 'error');
        } finally {
            clearTimeout(operation.timer);
            if (pending === operation) { pending = null; shift.setAttribute('aria-busy', 'false'); refresh.disabled = blocked; }
        }
    }
    function sync() {
        if (document.hidden) { cancel(); clear(); if (refresh) refresh.disabled = blocked; return; }
        text('[data-employee-today]', new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' }).format(new Date()));
        load();
    }
    refresh?.addEventListener('click', load);
    document.addEventListener('visibilitychange', sync);
    window.addEventListener('pagehide', () => { cancel(); clear(); });
    window.addEventListener('pageshow', event => { if (event.persisted) { cancel(); blocked = true; if (refresh) refresh.disabled = true; message('Cần xác minh lại phiên đăng nhập', 'Tải lại trang để xem đúng ca làm việc của bạn.', 'error', true); } });
    sync();
})();
