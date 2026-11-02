(function (global) {
    'use strict';
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const number = value => (Number.isFinite(Number(value)) ? Number(value) : 0).toLocaleString('vi-VN', { maximumFractionDigits: 4 });
    const states = { Created: ['Chờ soạn', 'packing', -1], Picking: ['Đang soạn', 'packing', 0],
        AwaitingApproval: ['Chờ quản lý xử lý', 'attention', 0], ReadyForHandover: ['Đã chốt soạn', 'ready', 1],
        HandedOver: ['Đã nhận hàng · chờ đi', 'ready', 2], Delivering: ['Đang giao', 'route', 2],
        AwaitingReconciliation: ['Đã có kết quả · chờ đối chiếu', 'counter', 3], ReadyToSettle: ['Chờ chốt tại quầy', 'counter', 3],
        CancellationPending: ['Yêu cầu thu hồi', 'attention', 2], Settled: ['Đã chốt', 'complete', 4], Cancelled: ['Đã hủy', 'cancelled', -1] };
    const utc = value => new Date(typeof value === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value + 'Z' : value);
    const time = value => value ? new Date(typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value) && !/(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value + 'Z' : value).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', hour12: false }) : 'Chưa ghi nhận';
    function expiresAt(order, snapshot) {
        if (!['Settled', 'Cancelled'].includes(order.state)) return Infinity;
        const seconds = Number(snapshot?.completedCardLifetimeSeconds) || 60;
        return utc(order.completedAtUtc || order.updatedAtUtc).getTime() + seconds * 1000;
    }
    function visibleOrders(snapshot, now) {
        return (Array.isArray(snapshot?.orders) ? snapshot.orders : []).filter(order => now < expiresAt(order, snapshot));
    }
    function elapsed(value, now) {
        const s = Math.max(0, Math.floor((now - new Date(typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value) && !/(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value + 'Z' : value).getTime()) / 1000));
        if (!Number.isFinite(s)) return '—';
        return s < 60 ? s + ' giây' : s < 3600 ? Math.floor(s / 60) + ' phút ' + s % 60 + ' giây' : Math.floor(s / 3600) + ' giờ ' + Math.floor(s % 3600 / 60) + ' phút';
    }
    function picture(phase) {
        const art = phase === 'route' ? '<span class="wd-truck"><i></i><b></b><em></em></span><span class="wd-signal"></span>'
            : phase === 'counter' ? '<span class="wd-register"><i>₫</i></span><span class="wd-paper"></span>'
            : phase === 'complete' ? '<span class="wd-done">✓</span><span class="wd-confetti"></span>'
            : '<span class="wd-box"><i></i><b></b></span><span class="wd-scan"></span>';
        return '<div class="wd-picture" aria-hidden="true">' + art + '</div>';
    }
    function render(snapshot, connected, now, options = {}) {
        const rows = visibleOrders(snapshot, now);
        const visibleIds = new Set(rows.map(order => order.orderId));
        const head = '<div class="wm-area-heading"><div class="wm-area-title"><span class="wm-area-number">06</span><h2>Giao hàng</h2></div><div class="wm-area-tools"><span class="wm-badge" data-status="' + (connected ? 'working' : 'stale') + '"><i class="wm-dot"></i>' + (connected ? number(snapshot?.activeCount) + ' đơn đang xử lý' : 'Dữ liệu cũ') + '</span>' + (options.pager || '') + '</div></div>';
        const summary = '<div class="wd-summary"><span><b>' + number(snapshot?.pickingCount) + '</b> Chờ / đang soạn</span><span><b>' + number(snapshot?.deliveringCount) + '</b> Bàn giao / đang giao</span><span><b>' + number(snapshot?.waitingCount) + '</b> Chờ xử lý / chốt</span></div>';
        const cards = (options.rows || rows).filter(order => visibleIds.has(order.orderId)).map(order => {
            const returned = order.returnedAtUtc && ['Delivering', 'AwaitingReconciliation', 'ReadyToSettle', 'CancellationPending'].includes(order.state);
            const [label, phase, stage] = returned ? ['Đã trở về · chờ đối chiếu', 'counter', 3] : states[order.state] || ['Trạng thái đã lưu', 'ready', 0];
            const event = order.eventId == null ? '' : String(order.eventId);
            const age = now - new Date(typeof order.updatedAtUtc === 'string' && !/(Z|[+-]\d{2}:?\d{2})$/i.test(order.updatedAtUtc) ? order.updatedAtUtc + 'Z' : order.updatedAtUtc).getTime();
            const changed = connected && event && age >= 0 && age < 12000;
            const picking = ['Created', 'Picking', 'AwaitingApproval', 'ReadyForHandover'].includes(order.state);
            const shortage = picking && Number(order.shortageLines) > 0;
            const lines = Array.isArray(order.lines) ? order.lines : [];
            const progress = ['Soạn', 'Sẵn sàng', 'Bàn giao', 'Đối chiếu', 'Chốt'].map((text, i) => '<span class="' + (stage >= i ? 'wd-step-done' : '') + '">' + text + '</span>').join('');
            const cancelled = order.state === 'Cancelled';
            const person = cancelled ? order.lastActor || 'Đã hủy đơn' : picking ? order.picker || 'Chưa nhận soạn' : order.courier || 'Chưa phân công người giao';
            const timing = order.departedAtUtc ? '<span>Xuất phát <b>' + esc(time(order.departedAtUtc)) + '</b></span>' +
                (order.state === 'Delivering' && !returned ? '<span class="wm-trip-elapsed" data-at="' + esc(order.departedAtUtc) + '">Đã đi ' + elapsed(order.departedAtUtc, now) + '</span>' : '') : '<span>Chưa ghi nhận xuất phát</span>';
            return '<section class="wd-card' + (shortage ? ' wd-shortage' : '') + '" role="button" tabindex="0" aria-label="Xem đơn giao ' + esc(order.code) + '" data-delivery-id="' + Number(order.orderId) + '" data-key="delivery-' + Number(order.orderId) + '" data-phase="' + phase + '" data-event-id="' + esc(event) + '" data-effect-signature="' + (changed ? esc(event) : '') + '" data-active="' + Boolean(changed) + '">' +
                '<div class="wd-card-head"><strong title="' + esc(order.code) + '">' + esc(order.code) + '</strong><span class="wd-state">' + esc(label) + '</span></div>' +
                '<div class="wd-work">' + picture(phase) + '<div><strong class="wd-person">' + esc(person) + '</strong><span class="wd-role">' + (cancelled ? 'Người ghi nhận hủy' : picking ? 'Người soạn' : order.custodyConfirmed ? 'Đã xác nhận nhận hàng' : 'Phân công dự kiến') + '</span>' +
                '<div class="wd-customer">' + esc(order.recipient) + '</div>' + (order.tripId ? '<span class="wd-trip">Chuyến #' + Number(order.tripId) + ' · ' + number(order.tripOrders) + ' đơn</span>' : '') + '</div></div>' +
                '<div class="wd-steps" aria-label="Tiến trình theo trạng thái đã lưu">' + progress + '</div>' +
                (picking && order.totalLines > 0 ? '<div class="wd-picking-progress"><label>' + number(order.checkedLines) + '/' + number(order.totalLines) + ' dòng đã ghi nhận' + (shortage ? ' · ' + number(order.shortageLines) + ' dòng thiếu' : '') + '</label><progress value="' + Math.min(Number(order.checkedLines) || 0, Number(order.totalLines)) + '" max="' + Number(order.totalLines) + '"></progress></div>' : '') +
                (lines.length && picking ? '<ul class="wd-lines">' + lines.map(line => '<li><span>' + esc(line.itemName) + '</span><b>' + (line.prepared == null ? 'Chưa soạn' : number(line.prepared)) + ' / ' + number(line.planned) + ' ' + esc(line.unitName) + '</b></li>').join('') + '</ul>' : '') +
                '<div class="wd-last-action">' + (order.lastActor ? '<b>' + esc(order.lastActor) + '</b> ' : '') + esc(order.lastAction) + '</div>' + ((!picking || options.compact) && order.detail ? '<p class="wd-detail">' + esc(order.detail) + '</p>' : '') +
                (order.settledTotal != null ? '<div class="wd-money">Đã chốt thực giao <strong>' + number(order.settledTotal) + ' đ</strong></div>' : '') +
                '<div class="wd-timing">' + timing + (returned ? '<span>Trở về <b>' + esc(time(order.returnedAtUtc)) + '</b></span>' : '') + '<span>Cập nhật cuối <time datetime="' + esc(order.updatedAtUtc) + '">' + esc(time(order.updatedAtUtc)) + '</time></span></div>' +
                (!connected ? '<div class="wd-stale">Mất kết nối màn hình · giữ lần ghi nhận cuối</div>' : order.state === 'Delivering' && !returned ? '<div class="wd-source">Theo mốc đã lưu · chưa có báo cáo mới giữa đường</div>' : '') + '</section>';
        }).join('');
        const hidden = Math.max(0, Number(snapshot?.activeCount || 0) - rows.filter(x => !['Settled', 'Cancelled'].includes(x.state)).length);
        return head + summary + '<div class="wd-list">' + (cards || '<div class="wd-empty">' + picture('packing') + '<strong>Chưa có đơn giao đang xử lý</strong><p>Đơn và nhân viên xuất hiện sau khi thao tác được lưu.</p></div>') + '</div>' +
            (hidden ? '<div class="wd-more">Còn ' + number(hidden) + ' đơn đang xử lý ngoài ' + rows.length + ' thẻ hiển thị.</div>' : '');
    }
    global.GaoDeliveryWall = Object.freeze({ render, elapsed, states, visibleOrders, expiresAt });
})(window);
