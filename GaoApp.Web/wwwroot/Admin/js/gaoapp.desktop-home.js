/* Desktop workspace: configured navigation and the existing authorized sales read API. */
(function () {
    'use strict';
    const root = document.querySelector('[data-desktop-home]');
    if (!root || root.hasAttribute('data-employee-home')) return;
    const desktop = matchMedia('(min-width: 768px)');
    const business = root.querySelector('[data-dwh-business]');
    const content = business?.querySelector('[data-dwh-content]');
    const status = business?.querySelector('[data-dwh-status]');
    const message = business?.querySelector('[data-dwh-message]');
    const refresh = business?.querySelector('[data-dwh-refresh]');
    const reload = business?.querySelector('[data-dwh-reload]');
    const periodSelect = root.querySelector('#desktopHomePeriod');
    const money = new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 });
    const number = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });
    const compact = new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 });
    const calendar = new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', day: '2-digit', month: '2-digit', year: 'numeric' });
    const clock = new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit', second: '2-digit' });
    const countKeys = new Set(['salesOrders', 'returnCount', 'voidCount']);
    const keys = ['netSales', 'salesOrders', 'aov', 'returns', 'returnCount', 'voidCount'];
    let sequence = 0, pending = null, blocked = false;
    function node(tag, className, text) {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (text !== undefined) element.textContent = text;
        return element;
    }
    function clearValues() {
        if (!business) return;
        content.hidden = true;
        business.querySelectorAll('[data-dwh-metric]').forEach(element => { element.textContent = '—'; });
        business.querySelector('[data-dwh-chart]').replaceChildren();
        business.querySelector('[data-dwh-trend-rows]').replaceChildren();
        business.querySelector('[data-dwh-products]').replaceChildren();
        const time = business.querySelector('[data-dwh-generated]'); time.textContent = '—'; time.removeAttribute('datetime');
    }
    function showStatus(text, kind = 'info', needsReload = false) {
        if (!business) return;
        status.hidden = false; status.dataset.kind = kind; message.textContent = text; reload.hidden = !needsReload;
    }
    function cancel() {
        sequence++;
        if (pending) { clearTimeout(pending.timer); pending.controller.abort(); pending = null; }
        clearValues();
        if (business) { business.setAttribute('aria-busy', 'false'); refresh.disabled = blocked; }
    }
    function dates(mode) {
        const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
        const values = Object.fromEntries(parts.map(part => [part.type, part.value]));
        const last = new Date(Date.UTC(Number(values.year), Number(values.month) - 1, Number(values.day)));
        const first = new Date(last);
        if (mode === 'week') first.setUTCDate(first.getUTCDate() - 6);
        if (mode === 'month') first.setUTCDate(1);
        return { from: first.toISOString().slice(0, 10), to: last.toISOString().slice(0, 10) };
    }
    const finite = value => typeof value === 'number' && Number.isFinite(value);
    function snapshot(data) {
        const summary = data?.current?.summary, trend = data?.current?.trend, products = data?.current?.topProducts;
        if (!summary || keys.some(key => !finite(summary[key])) || Array.from(countKeys).some(key => !Number.isSafeInteger(summary[key]) || summary[key] < 0)) throw new Error('Invalid metrics');
        if (!Array.isArray(trend) || trend.some(point => !finite(point.netSales) || !Number.isSafeInteger(point.salesOrders) || typeof point.label !== 'string')) throw new Error('Invalid trend');
        if (!Array.isArray(products) || products.some(product => !finite(product.grossSales) || !finite(product.baseQuantity) || typeof product.itemName !== 'string')) throw new Error('Invalid products');
        const from = data.currentPeriod?.fromDate?.slice(0, 10), to = data.currentPeriod?.toDate?.slice(0, 10);
        if (!/^\d{4}-\d{2}-\d{2}$/.test(from || '') || !/^\d{4}-\d{2}-\d{2}$/.test(to || '')) throw new Error('Invalid period');
        const raw = data.generatedAtUtc;
        if (typeof raw !== 'string') throw new Error('Missing timestamp');
        const generated = new Date(/[Zz]|[+-]\d{2}:?\d{2}$/.test(raw) ? raw : raw + 'Z');
        if (!Number.isFinite(generated.getTime())) throw new Error('Invalid timestamp');
        return { summary, trend, products: products.slice(0, 5), from, to, generated };
    }
    function chart(points) {
        const ns = 'http://www.w3.org/2000/svg';
        function svgNode(tag, attrs, text) {
            const element = document.createElementNS(ns, tag);
            for (const [key, value] of Object.entries(attrs)) element.setAttribute(key, String(value));
            if (text !== undefined) element.textContent = text;
            return element;
        }
        const svg = svgNode('svg', { viewBox: '0 0 720 250', role: 'img', 'aria-label': `Diễn biến doanh thu thuần, ${points.length} mốc thời gian; bảng số liệu ở bên dưới.` });
        const low = Math.min(0, ...points.map(point => point.netSales)), high = Math.max(0, ...points.map(point => point.netSales));
        const range = high - low || 1, x = index => 63 + index * 635 / Math.max(1, points.length - 1), y = value => 211 - (value - low) / range * 183;
        for (let step = 0; step <= 3; step++) {
            const value = low + range * step / 3, yy = y(value);
            svg.append(svgNode('line', { x1: 63, x2: 698, y1: yy, y2: yy, class: 'dwh-chart-grid' }), svgNode('text', { x: 51, y: yy + 4, 'text-anchor': 'end' }, high === low ? (step === 0 ? '0' : '') : compact.format(value)));
        }
        if (points.length) {
            const path = points.map((point, index) => `${index ? 'L' : 'M'} ${x(index).toFixed(2)} ${y(point.netSales).toFixed(2)}`).join(' ');
            svg.append(svgNode('path', { d: `${path} L ${x(points.length - 1)} ${y(0)} L ${x(0)} ${y(0)} Z`, class: 'dwh-chart-area' }), svgNode('path', { d: path, class: 'dwh-chart-line' }));
            const labels = new Set([0, Math.floor((points.length - 1) / 4), Math.floor((points.length - 1) / 2), Math.floor((points.length - 1) * 3 / 4), points.length - 1]);
            for (const index of labels) svg.append(svgNode('text', { x: x(index), y: 238, 'text-anchor': index === 0 ? 'start' : index === points.length - 1 ? 'end' : 'middle' }, points[index].label));
        }
        business.querySelector('[data-dwh-chart]').replaceChildren(svg);
        const rows = points.map(point => {
            const row = node('tr'); row.append(node('td', '', point.label), node('td', '', money.format(point.netSales)), node('td', '', number.format(point.salesOrders))); return row;
        });
        business.querySelector('[data-dwh-trend-rows]').replaceChildren(...rows);
    }
    function render(payload) {
        const data = snapshot(payload);
        business.querySelectorAll('[data-dwh-metric]').forEach(element => {
            const key = element.dataset.dwhMetric; element.textContent = countKeys.has(key) ? number.format(data.summary[key]) : money.format(data.summary[key]);
        });
        const first = calendar.format(new Date(data.from + 'T00:00:00+07:00')), last = calendar.format(new Date(data.to + 'T00:00:00+07:00'));
        business.querySelector('[data-dwh-period]').textContent = `${first === last ? first : first + ' – ' + last} · Toàn cửa hàng · Giờ Việt Nam`;
        const time = business.querySelector('[data-dwh-generated]'); time.dateTime = data.generated.toISOString(); time.textContent = clock.format(data.generated);
        chart(data.trend);
        const products = data.products.map((product, index) => {
            const item = node('li'), body = node('div');
            body.append(node('span', 'dwh-product-name', product.itemName), node('span', 'dwh-product-caption', `${number.format(product.baseQuantity)} ${product.baseUnitName || 'đơn vị gốc'}`));
            item.append(node('span', 'dwh-product-rank', String(index + 1).padStart(2, '0')), body, node('span', 'dwh-product-value', money.format(product.grossSales))); return item;
        });
        business.querySelector('[data-dwh-products]').replaceChildren(...products);
        business.querySelector('[data-dwh-no-products]').hidden = products.length > 0;
        status.hidden = true; content.hidden = false;
    }
    async function load() {
        if (!business || !desktop.matches || document.hidden || blocked) return;
        cancel();
        const url = new URL(business.dataset.url, location.href);
        if (url.origin !== location.origin) return;
        const mode = periodSelect.value;
        url.searchParams.set('compare', 'none');
        if (mode !== 'today') { const period = dates(mode); url.searchParams.set('fromDate', period.from); url.searchParams.set('toDate', period.to); }
        const operation = { controller: new AbortController(), sequence: ++sequence, timer: null, timedOut: false };
        pending = operation;
        const owns = () => pending === operation && sequence === operation.sequence && desktop.matches && !document.hidden;
        business.setAttribute('aria-busy', 'true'); refresh.disabled = true; showStatus('Đang tải tổng quan kinh doanh…');
        operation.timer = setTimeout(() => { operation.timedOut = true; operation.controller.abort(); }, 18000);
        try {
            const response = await fetch(url.href, { credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal: operation.controller.signal, headers: { Accept: 'application/json' } });
            if (!owns()) return;
            if (response.status === 401 || response.status === 403) { blocked = true; periodSelect.disabled = true; clearValues(); showStatus('Phiên đăng nhập hoặc quyền báo cáo đã thay đổi. Tải lại trang để tiếp tục.', 'error', true); return; }
            if (!response.ok || response.redirected || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Read failed');
            const data = await response.json();
            if (owns() && !operation.controller.signal.aborted) render(data);
        } catch (_) {
            if (owns()) { clearValues(); showStatus(operation.timedOut ? 'Chưa nhận được kết quả. Bấm Làm mới để thử lại.' : 'Không tải được tổng quan. Bấm Làm mới để thử lại.', 'error'); }
        } finally {
            clearTimeout(operation.timer);
            if (pending === operation) { pending = null; business.setAttribute('aria-busy', 'false'); refresh.disabled = blocked; }
        }
    }
    function sync() {
        if (!desktop.matches || document.hidden) { cancel(); return; }
        root.querySelector('[data-dwh-today]').textContent = new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' }).format(new Date());
        load();
    }
    refresh?.addEventListener('click', load); periodSelect?.addEventListener('change', load);
    desktop.addEventListener('change', sync); document.addEventListener('visibilitychange', sync);
    window.addEventListener('pagehide', cancel);
    window.addEventListener('pageshow', event => { if (event.persisted) {
        cancel(); blocked = true;
        if (refresh) refresh.disabled = true;
        if (periodSelect) periodSelect.disabled = true;
        showStatus('Tải lại trang để xác minh phiên đăng nhập.', 'error', true);
    } });
    sync();
})();
