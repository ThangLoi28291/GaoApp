(() => {
    'use strict';
    const root = document.getElementById('gao-kiosk'), content = document.getElementById('g-content'), dialog = document.getElementById('g-dialog'), network = document.getElementById('g-network');
    const token = root.querySelector('[name="__RequestVerificationToken"]').value;
    let server, screen = 'idle', items = [], selected, customer, history, tab = 'orders', page = 1, busy = false, polling = false, query = '', error = '', keyboard = false;
    let activity = Date.now(), promoIndex = 0, promos = [], promoAt = Date.now(), searchTimer, searchAbort, searchSequence = 0, scanBuffer = '', scanAt = 0, from = '', to = '';
    let scanQueue = Promise.resolve(), disposed = false, checkingCancellation = false;
    let paymentQrId, nextBankCheckAt = 0, nextPollAt = 0, checkingBank = false, bankRetry = false;
    const e = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const money = n => Number(n ?? 0).toLocaleString('vi-VN', { maximumFractionDigits: 2 }) + 'đ';
    const date = s => s ? new Date(s.endsWith('Z') ? s : s + 'Z').toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' }) : '—';
    const paths = {
        bag: '<path d="M5 7h14l1 14H4L5 7Z M8 8V6a4 4 0 0 1 8 0v2"/>',
        scan: '<path d="M3 8V3h5m8 0h5v5M3 16v5h5m8 0h5v-5M7 7v10m3-10v10m4-10v10m3-10v10"/>',
        person: '<circle cx="12" cy="7" r="4"/><path d="M4 21v-2a8 8 0 0 1 16 0v2"/>',
        qr: '<path d="M3 3h6v6H3zM15 3h6v6h-6zM3 15h6v6H3zM15 15h3v3h3v3h-6v-6Z"/>',
        home: '<path d="m3 10 9-7 9 7v11H3V10Zm6 11v-8h6v8"/>',
        help: '<path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4"/>',
        zoom: '<circle cx="10" cy="10" r="7"/><path d="m15 15 6 6M7 10h6m-3-3v6"/>',
        trash: '<path d="M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7m4-7v7"/>',
        arrow: '<path d="M4 12h16m-6-6 6 6-6 6"/>', back: '<path d="M20 12H4m6-6-6 6 6 6"/>',
        check: '<path d="m4 12 5 5L20 6"/>', keyboard: '<rect x="2" y="5" width="20" height="14" rx="2"/><path d="M6 9h1m4 0h1m4 0h1M6 13h1m4 0h1m4 0h1M7 16h10"/>'
    };
    const icon = name => `<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths[name] || paths.bag}</svg>`;
    const btn = (text, action, primary = false, disabled = false) => `<button type="button" class="g-btn ${primary ? 'g-primary' : ''}" data-action="${action}" ${disabled ? 'disabled' : ''}>${text}</button>`;
    const safeImage = url => { if (!url) return ''; try { const u = new URL(url, location.origin); return ((u.origin === location.origin && ['http:', 'https:'].includes(u.protocol)) || u.protocol === 'https:') || (u.protocol === 'data:' && /^data:image\/(png|jpeg|webp);base64,/i.test(url)) ? url : ''; } catch { return ''; } };
    async function api(path, body, signal) {
        const timeout = AbortSignal.timeout(35000);
        const response = await fetch('/kiosk/' + path, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store', signal: signal || timeout,
            headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json', RequestVerificationToken: token }) }, body: body === undefined ? undefined : JSON.stringify(body) });
        const data = await response.json().catch(() => ({}));
        if (response.status === 401) { server = null; customer = null; screen = 'activate'; render(); throw new Error(data.message || 'Thiết bị cần được kích hoạt lại.'); }
        if (response.status === 403 && path.startsWith('api/customer')) { customer = null; history = null; screen = 'login'; dialog.close(); }
        if (!response.ok) throw new Error(data.message || data.detail || data.title || (response.status === 429 ? 'Thao tác quá nhanh. Vui lòng chờ rồi thử lại.' : 'Chưa thể xử lý. Vui lòng thử lại hoặc gọi nhân viên.'));
        return data;
    }
    function apply(next) {
        const was = server?.mode, previousSession = server?.sessionKey, previousPaymentStatus = server?.paymentStatus; server = next;
        if (next.mode === 'payment' && next.qr?.id !== paymentQrId) {
            paymentQrId = next.qr?.id;
            const deadline = next.paymentCheckAfterUtc;
            nextBankCheckAt = deadline ? Date.parse(deadline.endsWith('Z') ? deadline : deadline + 'Z') : Date.now() + 30000;
            if (!Number.isFinite(nextBankCheckAt)) nextBankCheckAt = Date.now() + 30000;
            bankRetry = false;
        } else if (!['payment', 'review'].includes(next.mode)) { paymentQrId = null; nextBankCheckAt = 0; bankRetry = false; }
        if (previousSession && previousSession !== next.sessionKey) { customer = null; history = null; selected = null; query = ''; items = []; error = ''; dialog.close(); screen = 'idle'; }
        if (next.mode === 'success') error = ''; // Authoritative completion supersedes an earlier create/check error.
        if (previousPaymentStatus === 'Creating' && next.paymentStatus === 'Pending' && next.qr?.qrDataUrl) error = '';
        if (was === 'shop' && next.mode === 'idle') screen = 'idle';
        if (['payment', 'review', 'success'].includes(next.mode)) { customer = null; screen = next.mode; }
        else if (['payment', 'review', 'success'].includes(screen)) screen = next.mode === 'shop' ? 'shop' : 'idle';
        else if (!was && next.mode === 'shop') screen = 'shop';
        if (next.isPaused && !['payment', 'review', 'success'].includes(screen)) screen = 'paused';
        else if (screen === 'paused' && !next.isPaused) screen = 'idle';
    }
    async function command(action, values = {}) {
        if (!server) return;
        const request = { sessionKey: server.sessionKey, revision: server.revision, commandId: crypto.randomUUID(), action, ...values };
        try { apply(await api('api/command', request)); error = ''; }
        catch (err) {
            let recovered = false;
            if (server) { try { apply(await api('api/state')); recovered = true; } catch { /* Original message is more useful. */ } }
            if (recovered && server.mode === 'success' && ['checkout', 'retry-payment', 'cancel-payment'].includes(action)) return;
            throw err;
        }
    }
    async function work(fn) {
        while (busy) await new Promise(resolve => setTimeout(resolve, 30));
        busy = true; root.setAttribute('aria-busy', 'true');
        try { while (polling) await new Promise(resolve => setTimeout(resolve, 30)); await fn(); error = ''; } catch (err) { error = err.name === 'AbortError' || err.name === 'TimeoutError' ? 'Kết nối chậm. Đang giữ giao dịch để kiểm tra lại; vui lòng không thanh toán thêm.' : err.message; }
        finally { busy = false; root.setAttribute('aria-busy', 'false'); render(); }
    }
    function idle() {
        const p = promos[promoIndex % Math.max(promos.length, 1)], url = safeImage(p?.mediaUrl), poster = url && p?.isFullscreen;
        const media = url ? (p.mediaType?.toLowerCase() === 'video' ? `<video class="g-idle-media" src="${e(url)}" autoplay muted loop playsinline></video>` : `<img class="g-idle-media" src="${e(url)}" alt="${e(p.title)}">`) : '';
        const color = value => value && CSS.supports('color', value) ? value : '';
        return `<button class="g-idle ${poster ? 'g-idle-poster' : ''}" data-action="menu" style="${color(p?.backgroundColor) ? 'background:' + e(color(p.backgroundColor)) + ';' : ''}${color(p?.textColor) ? 'color:' + e(color(p.textColor)) : ''}">${media}<div class="g-idle-copy">${poster ? '' : `<div><p>${p?.isFlashSale ? 'ƯU ĐÃI ĐẶC BIỆT' : 'CHÀO MỪNG BẠN'}</p><h1>${e(p?.title || 'Mua sắm theo cách của bạn.')}</h1><p>${e(p?.description || 'Tra cứu sản phẩm, xem thông tin thành viên và thanh toán QR ngay tại đây.')}</p></div>`}<span class="g-touch">Chạm để bắt đầu ${icon('arrow')}</span></div></button>`;
    }
    function menu() { return `<div class="g-menu">${[['scan', 'Thông tin<br>sản phẩm', 'Quét mã, xem thông tin<br>và giá từng đơn vị.', 'product'], ['person', 'Thông tin<br>khách hàng', 'Xem điểm, voucher<br>và lịch sử mua hàng.', 'login'], ['qr', 'Mua hàng<br>không tiền mặt', 'Tự quét sản phẩm<br>và thanh toán bằng QR.', 'shop']].map(([i, t, d, s]) => `<button class="g-tile" data-action="${s}"><span class="g-tile-icon">${icon(i)}</span><strong>${t}</strong><p>${d}</p><span class="g-go">Bắt đầu →</span></button>`).join('')}</div>`; }
    function search() { return `<div class="g-search"><label class="g-input-row">${icon('scan')}<input id="g-query" autocomplete="off" placeholder="Quét mã vạch hoặc gõ tên sản phẩm…" value="${e(query)}" aria-label="Tìm sản phẩm">${btn(icon('keyboard'), 'keyboard')}</label>${keyboard ? `<div class="g-keyboard">${['1234567890', 'qwertyuiop', 'asdfghjkl', 'zxcvbnm'].map(row => `<div class="g-keyboard-row">${[...row].map(k => btn(k, 'letter-' + k)).join('')}</div>`).join('')}<div class="g-keyboard-row">${btn('Xóa hết', 'letter-clear')}${btn('Khoảng trắng', 'letter-space')}${btn('⌫', 'letter-back')}</div></div>` : ''}<div id="g-results" class="g-suggestions" ${!query ? 'hidden' : ''}>${results()}</div></div>`; }
    function results() { return items.length ? items.map((p, i) => `<button class="g-result" data-action="choose-${i}" ${screen === 'shop' && !p.canSelfCheckout ? 'disabled' : ''}>${safeImage(p.imageUrl) ? `<img src="${e(safeImage(p.imageUrl))}" width="44" height="44" style="object-fit:contain" alt="">` : icon('bag')}<span><strong>${e(p.displayName)}</strong><small>${e(p.barcode)} · ${e(p.baseUnit)}${screen === 'shop' && !p.canSelfCheckout ? ' · Thanh toán tại quầy thu ngân' : ''}</small></span><span class="g-result-price">${money(p.price)}</span></button>`).join('') : '<div class="g-result">Chưa tìm thấy sản phẩm. Thử từ khóa khác hoặc gọi nhân viên.</div>'; }
    function product() {
        const p = selected, image = safeImage(p?.imageUrl);
        const heading = `<div class="g-product-heading"><div><h1>Thông tin sản phẩm</h1><p>Quét mã hoặc tìm tên để xem giá</p></div>${btn(icon('back') + ' Chọn chức năng', 'menu')}</div>${search()}`;
        if (!p) return heading + `<div class="g-product-empty"><span class="g-product-scan-icon">${icon('scan')}</span><div class="g-eyebrow">TRA CỨU GIÁ TẠI CỬA HÀNG</div><h2>Đưa mã vạch đến máy quét</h2><p>Xem hình ảnh, thông tin và giá bán từng đơn vị.<br>Bạn cũng có thể gõ tên sản phẩm ở ô phía trên.</p></div>`;
        return heading + `<article class="g-product-detail" aria-labelledby="g-product-name">
            <figure class="g-product-visual">
                <div class="g-product-photo-wrap">
                    <div class="g-product-no-image" ${image ? 'hidden' : ''}>${icon('bag')}<span>Sản phẩm chưa có hình ảnh</span></div>
                    ${image ? `<button type="button" class="g-product-photo" data-action="product-image" aria-label="Phóng to ảnh ${e(p.displayName)}"><img src="${e(image)}" alt="${e(p.displayName)}"><span class="g-product-zoom">${icon('zoom')} Chạm để xem ảnh lớn</span></button>` : ''}
                </div>
                <figcaption>${icon('scan')}<span>Mã sản phẩm<strong>${e(p.barcode || 'Chưa có mã')}</strong></span></figcaption>
            </figure>
            <section class="g-product-info">
                <div class="g-product-title">${p.brand ? `<span class="g-product-brand">${e(p.brand)}</span>` : '<div class="g-eyebrow">SẢN PHẨM TẠI CỬA HÀNG</div>'}<h2 id="g-product-name">${e(p.displayName)}</h2></div>
                <section class="g-product-pricing" aria-labelledby="g-product-price-heading"><h3 id="g-product-price-heading">Giá bán theo đơn vị</h3>
                    <ul class="g-unit-prices">${p.units.map(u => `<li class="g-unit-price ${Number(u.factor) === 1 ? 'g-unit-price-base' : ''}"><span class="g-unit-name">${e(u.name)}</span><span class="g-unit-pack">${Number(u.factor) === 1 ? '1 ' + e(u.name) : '1 ' + e(u.name) + ' = ' + Number(u.factor).toLocaleString('vi-VN', { maximumFractionDigits: 6 }) + ' ' + e(p.baseUnit)}</span><strong class="g-unit-amount">${Number(u.price).toLocaleString('vi-VN', { maximumFractionDigits: 2 })}<small>đ</small></strong></li>`).join('')}</ul>
                    <p class="g-product-price-note">Giá bán lẻ tại cửa hàng. Khuyến mãi được tính khi mua hàng.</p>
                </section>
                ${p.description ? `<p class="g-product-description">${e(p.description)}</p>` : ''}
                <div class="g-product-next">${icon('scan')}<span>Muốn xem sản phẩm khác?<strong>Quét mã tiếp theo để tra cứu ngay.</strong></span></div>
            </section>
        </article>`;
    }
    function login() { return `<div class="g-login"><div><div class="g-login-icon">${icon('person')}</div><div class="g-eyebrow">KHÁCH HÀNG GAO MART</div><h1>Thông tin<br>của bạn</h1><p class="g-muted" style="margin-top:22px">Nhập số điện thoại đã đăng ký để xem điểm, voucher và lịch sử mua hàng.</p>${btn(icon('back') + ' Chọn chức năng', 'menu')}<div class="g-note">Chỉ tra cứu thông tin.<br>Không đổi điểm hoặc sử dụng voucher tại màn hình này.</div></div><div class="g-panel"><label for="g-phone">Số điện thoại</label><input id="g-phone" type="tel" inputmode="numeric" maxlength="15" autocomplete="off" placeholder="Nhập số điện thoại" style="border-bottom:2px solid var(--g-accent);margin:12px 0"><div class="g-keypad">${['1', '2', '3', '4', '5', '6', '7', '8', '9', 'Xóa', '0', '⌫'].map(k => btn(k, 'key-' + k)).join('')}</div><button class="g-btn g-primary g-wide" style="margin-top:20px" data-action="lookup">Xem thông tin ${icon('arrow')}</button></div></div>`; }
    function customerScreen() {
        const rewards = customer.rewards;
        return `<div class="g-heading"><div><div class="g-eyebrow">HỒ SƠ KHÁCH HÀNG</div><h1>Xin chào, ${e(customer.name)}</h1><p>${e(customer.phone)}</p></div>${btn('Kết thúc tra cứu', 'end-customer', true)}</div><div class="g-stats"><div class="g-stat">Điểm hiện tại<strong>${rewards ? e(rewards.availablePoints) : '—'} <small>điểm</small></strong></div><div class="g-stat">Voucher còn hiệu lực<strong>${rewards ? e(rewards.availableVoucherCount) : '—'} <small>phiếu</small></strong><small class="g-muted">${rewards ? money(rewards.availableVoucherValue) : e(customer.rewardNotice)}</small></div><div class="g-stat">Đơn đã mua<strong>${e(customer.completedOrders)} <small>đơn</small></strong><small class="g-muted">${money(customer.netSales)} sau hoàn trả</small></div></div><div class="g-tabs" role="tablist">${[['orders', 'Lịch sử mua hàng'], ['points', 'Lịch sử điểm'], ['vouchers', 'Voucher']].map(([v, t]) => `<button class="g-btn" role="tab" aria-selected="${tab === v}" data-action="tab-${v}">${t}</button>`).join('')}</div><div class="g-panel"><div class="g-filters"><label>Từ ngày<input id="g-from" type="date" value="${e(from)}"></label><label>Đến ngày<input id="g-to" type="date" value="${e(to)}"></label>${btn('Lọc lịch sử', 'filter')}</div>${history ? historyRows() : '<div class="g-loading">Đang tải lịch sử…</div>'}${history ? `<div class="g-pager">${btn('← Trước', 'prev', false, page <= 1)}<span>Trang ${page} / ${Math.max(1, Math.ceil(history.total / history.pageSize))}</span>${btn('Tiếp →', 'next', false, page * history.pageSize >= history.total)}</div>` : ''}</div>`;
    }
    function historyRows() {
        if (!history.items.length) return '<div class="g-empty">Chưa có lịch sử trong khoảng thời gian này.</div>';
        const rows = history.items.map(x => {
            if (tab === 'orders') return `<div class="g-history"><div><h3>${e(x.title)}</h3><p>${date(x.atUtc)} · ${({ 0: 'Đang làm', 1: 'Đơn giữ', 2: 'Đã hoàn tất', 3: 'Đã hủy', 4: 'Đã hủy', 5: 'Đã hoàn tiền' })[x.status] || 'Đã ghi nhận'}</p></div><div><strong>${money(x.amount)}</strong> ${btn('Chi tiết', 'order-' + x.id)}</div></div>`;
            if (tab === 'points') return `<div class="g-history"><div><h3>${e(x.title)}</h3><p>${date(x.atUtc)}</p></div><div><strong>${x.deltaPoints == null ? money(x.amount) : (x.deltaPoints > 0 ? '+' : '') + x.deltaPoints + ' điểm'}</strong><p>Số dư: ${x.balancePoints == null ? money(x.balanceAmount) : x.balancePoints + ' điểm'}</p></div></div>`;
            return `<div class="g-history"><div><h3>Phiếu giảm giá ${money(x.amount)}</h3><p>${e(x.title)} · ${date(x.atUtc)}</p></div><span class="g-badge">${({ 1: 'Còn hiệu lực', 2: 'Đã sử dụng', 3: 'Đã hủy', 4: 'Hết hạn', 5: 'Đang khóa' })[x.status] || 'Đã ghi nhận'}</span> ${btn('Chi tiết', 'voucher-' + x.id)}</div>`;
        }).join('');
        return rows + (tab === 'points' ? '<p class="g-status-note">Điểm lịch sử được quy đổi theo cấu hình hiện tại; giá trị tích lũy gốc vẫn được lưu.</p>' : '');
    }
    const invoiceChoice = () => server?.order?.invoiceIssuanceRoute === 'Manual' ? 'Khách có lấy hóa đơn' : 'Khách không lấy hóa đơn';
    const invoiceSummary = () => `<div class="g-summary-line g-invoice-choice"><span>Hóa đơn điện tử</span><strong>${invoiceChoice()}</strong></div>`;
    function cartPhoto(line) {
        const url = safeImage(line.imageUrl);
        return `<div class="g-cart-thumb"><span class="g-cart-photo-fallback" ${url ? 'hidden' : ''}>${icon('bag')}</span>${url ? `<button type="button" data-action="cart-image-${line.id}" aria-label="Xem ảnh ${e(line.itemName)}"><img src="${e(url)}" alt="${e(line.itemName)}"></button>` : ''}</div>`;
    }
    function shop() {
        const order = server.order, lines = order?.lines || [];
        return `<div class="g-heading g-shop-heading"><div><div class="g-eyebrow">MUA HÀNG KHÔNG TIỀN MẶT</div><h1>Giỏ hàng của bạn</h1></div><div class="g-shop-heading-actions">${btn(icon('back') + ' Rời mua hàng', 'home')}<span class="g-badge">${icon('qr')} Thanh toán bằng QR</span></div></div>
        <div class="g-shop"><div class="g-shop-products">${search()}<section class="g-panel g-cart-panel"><div class="g-cart-heading"><h3>Sản phẩm đã chọn <span>${lines.length}</span></h3><p>Quét mã để thêm sản phẩm</p></div>
            <div class="g-cart-list">${lines.length ? lines.map(l => `<article class="g-cart-row">${cartPhoto(l)}<div class="g-cart-copy"><h3>${e(l.itemName)}</h3><p>${e(l.unitName)} · ${money(l.unitPrice)}${l.isPromotionGift ? ' · Quà tặng' : ''}</p><span class="g-cart-barcode">${e(l.barcode)}</span></div><div class="g-cart-controls"><strong class="g-cart-line-total">${money(l.lineTotal)}</strong><div class="g-quantity"><button type="button" class="g-btn" data-action="minus-${l.id}" aria-label="Giảm số lượng ${e(l.itemName)}" ${l.isPromotionGift ? 'disabled' : ''}>−</button><button class="g-btn g-qty-value" data-action="setqty-${l.id}" ${l.isPromotionGift ? 'disabled' : ''} aria-label="Thay đổi số lượng ${e(l.itemName)}">${e(l.quantity)}</button><button type="button" class="g-btn" data-action="plus-${l.id}" aria-label="Tăng số lượng ${e(l.itemName)}" ${l.isPromotionGift ? 'disabled' : ''}>+</button></div>${!l.isPromotionGift ? `<button type="button" class="g-btn g-cart-remove" data-action="remove-${l.id}" aria-label="Xóa ${e(l.itemName)} khỏi giỏ">${icon('trash')} Xóa sản phẩm</button>` : ''}</div></article>`).join('') : `<div class="g-cart-empty"><span>${icon('scan')}</span><h3>Quét sản phẩm đầu tiên</h3><p>Hình ảnh và giá sẽ hiện trong giỏ hàng.<br>Bạn cũng có thể tìm sản phẩm bằng tên.</p></div>`}</div>
            <div class="g-cart-tip">${icon('check')} Giá theo quy cách và khuyến mãi được tính tự động.</div></section></div>
            <aside class="g-panel g-checkout-summary"><h3>Thông tin thanh toán</h3><div class="g-summary-line"><span>Tạm tính</span><strong>${money(order?.subtotal)}</strong></div><div class="g-summary-line"><span>Giảm giá</span><strong>${money(order?.discountTotal)}</strong></div><div class="g-checkout-total"><span>Tổng thanh toán</span><div class="g-total">${money(order?.grandTotal)}</div></div>
            <button class="g-btn g-primary g-wide g-checkout-button" data-action="checkout" ${!lines.length ? 'disabled' : ''}>${icon('qr')} Thanh toán QR ${icon('arrow')}</button>${invoiceSummary()}<p class="g-checkout-invoice-help">Cần lấy hóa đơn? Bấm Gọi nhân viên để được hỗ trợ.</p><button class="g-btn g-cancel-cart" data-action="cancel-cart">Hủy giỏ hàng</button></aside>
        </div>`;
    }
    function renderPaymentCountdown() {
        const value = root.querySelector('#g-bank-countdown'), label = root.querySelector('#g-bank-countdown-label');
        if (!value || !label) return;
        const seconds = Math.max(0, Math.ceil((nextBankCheckAt - Date.now()) / 1000));
        label.textContent = checkingCancellation ? 'Đang kiểm tra để hủy…' : checkingBank ? 'Đang kiểm tra ACB…' : !network.hidden ? 'Kết nối gián đoạn · thử lại sau' : bankRetry ? 'Thử kiểm tra lại sau' : seconds === 0 ? 'Đang chờ lượt kiểm tra…' : 'Kiểm tra ACB tiếp theo sau';
        value.textContent = checkingBank || checkingCancellation ? '…' : `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`;
    }
    function payment() {
        const reviewing = screen === 'review', qr = server.qr, creating = !reviewing && server.paymentStatus === 'Creating';
        return `<div class="g-payment"><div class="g-payment-copy"><div class="g-eyebrow">${reviewing || creating ? 'ĐANG KIỂM TRA GIAO DỊCH' : 'THANH TOÁN AN TOÀN'}</div><h1>${reviewing ? 'Vui lòng chờ<br>nhân viên hỗ trợ' : creating ? 'Đang xác minh<br>lần tạo QR' : 'Quét mã QR<br>để thanh toán'}</h1><p class="g-muted g-payment-intro">${reviewing ? (server.paymentStatus === 'Received' ? 'Đã nhận tiền. Hệ thống đang kiểm tra để hoàn tất đơn; vui lòng không thanh toán thêm.' : 'Giao dịch cần kiểm tra trước khi hoàn tất. Vui lòng không thanh toán thêm.') : creating ? 'Chưa nhận được mã QR hợp lệ từ ngân hàng. Giỏ hàng vẫn được giữ nguyên để kiểm tra.' : 'Mở ứng dụng ngân hàng, quét mã và kiểm tra số tiền trước khi chuyển.'}</p>
        <section class="g-panel g-payment-amount"><span>Số tiền thanh toán</span><div class="g-total">${money(server.order?.grandTotal)}</div><p>${e(server.terminal)}</p>${invoiceSummary()}</section>
        <div class="g-payment-status" role="status"><span class="g-spinner"></span><span>${checkingCancellation ? 'Đang kiểm tra ngân hàng để hủy…' : reviewing ? 'Đã gửi yêu cầu hỗ trợ.' : 'Tự động xác nhận khi nhận được tiền.'}</span></div>${!reviewing ? `<div class="g-bank-check"><div><span id="g-bank-countdown-label">Kiểm tra ACB tiếp theo sau</span><strong id="g-bank-countdown">00:30</strong></div><button type="button" class="g-btn" data-action="check-payment" ${checkingBank || checkingCancellation ? 'disabled' : ''}>Kiểm tra ngay</button></div>` : ''}<p class="g-payment-reminder">Đã chuyển tiền? Vui lòng chờ xác nhận, không chuyển thêm lần nữa.</p></div>
        <section class="g-panel g-payment-code"><div class="g-payment-bank">${icon('qr')} ${creating ? 'Chưa có QR để thanh toán' : 'Quét bằng ứng dụng ngân hàng'}</div>${!reviewing && safeImage(qr?.qrDataUrl) ? `<img class="g-qr-image" src="${e(safeImage(qr.qrDataUrl))}" alt="Mã QR thanh toán cho giỏ hàng hiện tại">` : `<div class="g-loading"><span class="g-spinner"></span>${creating ? 'Đang kiểm tra lần tạo QR' : 'Đang xử lý giao dịch'}</div>`}<h3>${e(server.store)}</h3><p class="g-payment-content">${e(qr?.content)}</p><p class="g-status-note">${!creating && qr?.expireAtUtc ? 'Hiệu lực đến ' + date(qr.expireAtUtc) : 'Giữ màn hình này trong lúc hệ thống kiểm tra.'}</p>
        ${creating ? `<button type="button" class="g-btn g-primary g-wide g-retry-payment" data-action="retry-payment">Kiểm tra & tạo lại QR</button><p class="g-payment-cancel-hint">Chỉ tạo lại sau khi xác nhận hủy được lần trước. Nếu đã nhận tiền, hệ thống sẽ hoàn tất đơn.</p>` : ''}
        ${!reviewing ? `<button type="button" class="g-btn g-wide g-cancel-payment" data-action="cancel-payment" ${checkingCancellation ? 'disabled' : ''}>${checkingCancellation ? 'Đang kiểm tra ngân hàng…' : 'Hủy thanh toán'}</button><p class="g-payment-cancel-hint">Đổi ý không mua? Hệ thống sẽ kiểm tra giao dịch trước khi hủy.</p>` : btn('Gọi nhân viên hỗ trợ', 'help')}</section></div>`;
    }
    function success() { return `<div class="g-success"><div class="g-success-message"><div class="g-success-icon">${icon('check')}</div><div class="g-eyebrow">THANH TOÁN THÀNH CÔNG</div><h1>Cảm ơn bạn<br>đã mua sắm tại ${e(server.store)}!</h1><p>Giao dịch đã được xác nhận tự động.</p><p>Vui lòng mang đủ sản phẩm của bạn.<br>Nhân viên luôn sẵn sàng hỗ trợ.</p></div><div class="g-success-receipt"><section class="g-panel"><h3>Đơn hàng đã hoàn tất</h3><div class="g-summary-line"><span>Mã đơn</span><strong>${e(server.order?.orderNumber || '#' + server.order?.id)}</strong></div><div class="g-summary-line"><span>Đã thanh toán</span><strong>${money(server.order?.grandTotal)}</strong></div><div class="g-summary-line"><span>Hình thức</span><strong>Chuyển khoản QR</strong></div>${invoiceSummary()}</section>${btn('Hoàn tất', 'finish', true)}<p id="g-countdown" class="g-muted">Tự trở về quảng cáo sau 15 giây</p></div></div>`; }
    function activate() { return `<main><div class="g-panel g-activation"><div class="g-eyebrow">GAO MART · QUẦY TỰ PHỤC VỤ</div><h1>Kích hoạt thiết bị</h1><p class="g-muted" style="margin:20px 0">Nhập khóa do Admin cấp cho máy tại cửa hàng.</p><label for="g-key">Khóa kích hoạt</label><input id="g-key" autocomplete="off" spellcheck="false" maxlength="64" style="border:1px solid var(--g-line);border-radius:10px;padding:12px;margin:12px 0 20px">${btn('Kích hoạt máy', 'activate', true)}</div></main>`; }
    function render() {
        searchAbort?.abort(); searchSequence++;
        const body = ({ menu, product, login, customer: customerScreen, shop, payment, review: payment, success, paused: () => '<div class="g-empty"><h1>Quầy tạm ngưng phục vụ</h1><p style="margin:20px 0">Vui lòng sử dụng quầy thu ngân hoặc gọi nhân viên hỗ trợ.</p></div>' })[screen];
        content.innerHTML = (screen === 'activate' ? activate() : screen === 'idle' ? idle() : `<main data-screen="${screen}">${error ? `<div class="g-error" role="alert">${e(error)} ${btn('Đóng', 'dismiss')}</div>` : ''}${body ? body() : menu()}</main>`);
        renderPaymentCountdown();
        if (error && ['activate', 'idle'].includes(screen)) { network.hidden = false; network.textContent = error; }
    }
    function showDialog(title, html, buttons, className = '') { dialog.className = className; dialog.setAttribute('aria-labelledby', 'g-dialog-title'); dialog.innerHTML = `<h2 id="g-dialog-title">${e(title)}</h2><div>${html}</div><div class="g-actions" style="margin-top:22px">${buttons}</div>`; if (!dialog.open) dialog.showModal(); }
    async function endCustomer() { customer = null; history = null; screen = 'idle'; dialog.close(); render(); from = ''; to = ''; await api('api/customer/end', {}); }
    async function loadHistory() { history = await api('api/customer/history', { tab, page, pageSize: 20, from: from || null, to: to || null }); page = history.page; }
    async function searchNow(exact = false) {
        const value = query.trim(); if (!value) { items = []; const r = root.querySelector('#g-results'); if (r) r.hidden = true; return; }
        const seq = ++searchSequence; searchAbort?.abort(); searchAbort = new AbortController();
        try {
            const found = await api('api/products?q=' + encodeURIComponent(value), undefined, searchAbort.signal);
            if (seq !== searchSequence) return;
            items = found;
            if (exact) { const p = found.find(x => x.barcode === value || x.units.some(u => u.barcodes.includes(value))); if (p) { selected = p; query = ''; items = []; render(); return; } }
            const resultsBox = root.querySelector('#g-results'); if (resultsBox) { resultsBox.hidden = false; resultsBox.innerHTML = results(); }
        } catch (err) { if (err.name !== 'AbortError') { error = err.message; render(); } }
    }
    async function scan(code) {
        activity = Date.now();
        if (screen === 'shop') { await command('scan', { barcode: code }); query = ''; items = []; }
        else if (['idle', 'menu', 'product'].includes(screen)) { screen = 'product'; query = code; render(); await searchNow(true); }
    }
    root.addEventListener('input', ev => { activity = Date.now(); if (ev.target.id === 'g-query') { query = ev.target.value; clearTimeout(searchTimer); searchTimer = setTimeout(() => searchNow(), 250); } });
    root.addEventListener('error', ev => {
        const thumb = ev.target.closest?.('.g-cart-thumb');
        if (thumb) { ev.target.closest('button').hidden = true; thumb.querySelector('.g-cart-photo-fallback').hidden = false; return; }
        const photo = ev.target.closest?.('.g-product-photo');
        if (!photo) return;
        photo.hidden = true;
        photo.parentElement.querySelector('.g-product-no-image').hidden = false;
    }, true);
    root.addEventListener('click', ev => {
        activity = Date.now(); const action = ev.target.closest('[data-action]')?.dataset.action; if (!action || busy) return;
        if (action === 'close-dialog') { dialog.close(); return; }
        if (action === 'product-image' && selected && safeImage(selected.imageUrl)) {
            showDialog(selected.displayName, `<img class="g-product-enlarged" src="${e(safeImage(selected.imageUrl))}" alt="${e(selected.displayName)}">`, btn('Đóng ảnh', 'close-dialog', true), 'g-product-image-dialog');
            return;
        }
        if (action.startsWith('cart-image-')) {
            const line = server.order?.lines.find(l => l.id === Number(action.slice(11)));
            if (line && safeImage(line.imageUrl)) showDialog(line.itemName, `<img class="g-product-enlarged" src="${e(safeImage(line.imageUrl))}" alt="${e(line.itemName)}">`, btn('Đóng ảnh', 'close-dialog', true), 'g-product-image-dialog');
            return;
        }
        if (action === 'check-payment') { poll(true); return; }
        if (action === 'retry-payment') { showDialog('Kiểm tra trước khi tạo lại QR', '<p>Giỏ hàng hiện tại được giữ nguyên. Hệ thống kiểm tra và hủy an toàn lần tạo trước rồi mới tạo QR mới.</p><p><strong>Nếu đã chuyển tiền, vui lòng chờ xác nhận hoặc gọi nhân viên.</strong></p>', btn('Tiếp tục chờ', 'close-dialog') + btn('Kiểm tra & tạo lại', 'confirm-retry-payment', true)); return; }
        if (action === 'cancel-payment') { showDialog('Hủy thanh toán và giỏ hàng?', '<p>Hệ thống chỉ hủy khi ngân hàng xác nhận chưa nhận tiền. Các sản phẩm trong giỏ sẽ được bỏ.</p><p><strong>Nếu đã chuyển tiền, hãy tiếp tục chờ xác nhận.</strong></p>', btn('Tiếp tục thanh toán', 'close-dialog') + btn('Kiểm tra & hủy', 'confirm-cancel-payment', true)); return; }
        if (action.startsWith('qtykey-')) { const input = root.querySelector('#g-qty'), k = action.slice(7); input.value = k === 'Xóa' ? '' : k === '⌫' ? input.value.slice(0, -1) : (input.value + k).slice(0, 3); return; }
        if (action.startsWith('setqty-')) { const line = server.order.lines.find(l => l.id === Number(action.slice(7))); showDialog('Số lượng sản phẩm', `<p>${e(line.itemName)}</p><input id="g-qty" type="number" inputmode="numeric" min="0" max="999" value="${e(line.quantity)}" aria-label="Số lượng"><div class="g-keypad">${['1','2','3','4','5','6','7','8','9','Xóa','0','⌫'].map(k => btn(k, 'qtykey-' + k)).join('')}</div>`, btn('Bỏ sản phẩm', 'remove-' + line.id) + btn('Lưu số lượng', 'saveqty-' + line.id, true)); return; }
        if (action.startsWith('key-')) { const input = root.querySelector('#g-phone'), k = action.slice(4); input.value = k === 'Xóa' ? '' : k === '⌫' ? input.value.slice(0, -1) : (input.value + k).slice(0, 15); return; }
        if (action === 'keyboard') { keyboard = !keyboard; render(); return; }
        if (action.startsWith('letter-')) { const k = action.slice(7); query = k === 'clear' ? '' : k === 'back' ? query.slice(0, -1) : query + (k === 'space' ? ' ' : k); root.querySelector('#g-query').value = query; searchNow(); return; }
        if (action === 'dismiss') { error = ''; render(); return; }
        if (action === 'checkout') { showDialog('Kiểm tra trước khi thanh toán', `<p>Tổng tiền: <strong>${money(server.order.grandTotal)}</strong></p><p>Vui lòng kiểm tra đã quét đủ sản phẩm. Giỏ hàng được khóa trong lúc chờ thanh toán.</p>${invoiceSummary()}`, btn('Kiểm tra lại', 'close-dialog') + btn('Tạo QR thanh toán', 'pay', true)); return; }
        if (action === 'cancel-cart' || (action === 'home' && screen === 'shop')) { showDialog('Hủy giỏ hàng?', '<p>Các sản phẩm chưa thanh toán sẽ được xóa khỏi phiên này.</p>', btn('Tiếp tục mua', 'close-dialog') + btn('Hủy giỏ', 'confirm-cancel', true)); return; }
        work(async () => {
            if (action === 'activate') { await api('activate', { key: root.querySelector('#g-key').value.trim() }); location.reload(); return; }
            if (action === 'menu' || action === 'home') { if (customer) await endCustomer(); screen = 'menu'; selected = null; query = ''; }
            else if (action === 'product') { screen = 'product'; query = ''; selected = null; }
            else if (action === 'login') { screen = 'login'; }
            else if (action === 'shop') { await command('start'); screen = 'shop'; query = ''; }
            else if (action.startsWith('choose-')) { const p = items[Number(action.slice(7))]; if (!p) return; if (screen === 'shop') { const matched = p.units.find(u => u.barcodes.includes(query.trim())); await command('add', { variantId: p.variantId, unitId: matched?.id || null }); } else selected = p; query = ''; items = []; }
            else if (action.startsWith('saveqty-') || action.startsWith('remove-')) { const quantity = action.startsWith('remove-') ? 0 : Number(root.querySelector('#g-qty').value); if (!Number.isInteger(quantity) || quantity < 0 || quantity > 999) throw new Error('Vui lòng nhập số lượng từ 0 đến 999.'); await command('quantity', { lineId: Number(action.split('-')[1]), quantity }); dialog.close(); }
            else if (action.startsWith('plus-') || action.startsWith('minus-')) { const id = Number(action.split('-')[1]), line = server.order.lines.find(l => l.id === id); await command('quantity', { lineId: id, quantity: Math.max(0, line.quantity + (action.startsWith('plus-') ? 1 : -1)) }); }
            else if (action === 'lookup') { customer = await api('api/customer', { phone: root.querySelector('#g-phone').value }); screen = 'customer'; tab = 'orders'; page = 1; await loadHistory(); }
            else if (action.startsWith('tab-')) { tab = action.slice(4); page = 1; await loadHistory(); }
            else if (action === 'next' || action === 'prev') { page += action === 'next' ? 1 : -1; await loadHistory(); }
            else if (action === 'filter') { from = root.querySelector('#g-from').value; to = root.querySelector('#g-to').value; page = 1; await loadHistory(); }
            else if (action.startsWith('order-')) { const o = await api('api/customer/order/' + Number(action.slice(6)), {}); showDialog(o.orderNumber || 'Chi tiết đơn hàng', o.lines.map(l => `<div class="g-history"><span>${e(l.itemName)}<p>${e(l.quantity)} ${e(l.sellingUnitName)}</p></span><strong>${money(l.lineTotal)}</strong></div>`).join('') + `<p>Tổng tiền: <strong>${money(o.grandTotal)}</strong></p>`, btn('Đóng', 'close-dialog', true)); }
            else if (action.startsWith('voucher-')) { const v = await api('api/customer/voucher/' + Number(action.slice(8)), {}); showDialog(v.voucherCode, `<p>Giá trị: <strong>${money(v.value)}</strong></p><p>Phát hành: ${date(v.issuedAtUtc)}</p><p>Trạng thái: ${({1: 'Còn hiệu lực', 2: 'Đã sử dụng', 3: 'Đã hủy', 4: 'Hết hạn', 5: 'Đang khóa'})[v.status] || 'Đã ghi nhận'}</p>${v.usedAtUtc ? `<p>Đã sử dụng: ${date(v.usedAtUtc)}</p>` : ''}${v.usedOrderNumber ? `<p>Đơn mua hàng: ${e(v.usedOrderNumber)}</p>` : ''}`, btn('Đóng', 'close-dialog', true)); }
            else if (action === 'end-customer') { await endCustomer(); screen = 'idle'; }
            else if (action === 'confirm-cancel') { dialog.close(); await command('cancel'); screen = 'menu'; }
            else if (action === 'confirm-cancel-payment') {
                dialog.close(); checkingCancellation = true; render();
                try {
                    await command('cancel-payment');
                    if (server.mode === 'idle') {
                        screen = 'menu'; query = ''; items = [];
                        showDialog('Đã hủy thanh toán', '<p>QR và giỏ hàng đã được hủy. Bạn có thể bắt đầu lượt mua mới.</p>', btn('Về trang chủ', 'close-dialog', true));
                    }
                } finally { checkingCancellation = false; }
            }
            else if (action === 'confirm-retry-payment') { dialog.close(); await command('retry-payment'); }
            else if (action === 'pay') { dialog.close(); await command('checkout'); }
            else if (action === 'finish') { await command('finish'); customer = null; history = null; selected = null; screen = 'idle'; }
            else if (action === 'help') { await command('help'); showDialog('Đã gửi yêu cầu hỗ trợ', '<p>Nhân viên sẽ đến hỗ trợ bạn tại quầy này.</p>', btn('Đã hiểu', 'close-dialog', true)); }
        });
    });
    document.addEventListener('keydown', ev => {
        activity = Date.now();
        if (dialog.open || ['activate', 'login', 'customer', 'payment', 'review', 'success'].includes(screen)) return;
        if (/^(INPUT|TEXTAREA|SELECT)$/.test(ev.target.tagName) && ev.target.id !== 'g-query') return;
        // Keep the scan buffer independent of the input node: cart renders can replace it mid-scan.
        if (Date.now() - scanAt > 150) scanBuffer = ''; scanAt = Date.now();
        if (ev.key === 'Enter') {
            const code = scanBuffer.length >= 4 ? scanBuffer : ev.target.id === 'g-query' ? ev.target.value.trim() : '';
            scanBuffer = ''; clearTimeout(searchTimer);
            if (code) { ev.preventDefault(); scanQueue = scanQueue.then(() => work(() => scan(code))); }
        } else if (ev.key.length === 1 && !ev.ctrlKey && !ev.altKey) scanBuffer = (scanBuffer + ev.key).slice(-100);
        else if (ev.key === 'Backspace' || ev.key === 'Escape') scanBuffer = '';
    });
    root.addEventListener('pointerdown', () => { activity = Date.now(); });
    async function poll(forceBank = false) {
        if (!server || busy || polling || disposed) return;
        polling = true;
        const checkPayment = screen === 'payment' && (forceBank || Date.now() >= nextBankCheckAt);
        checkingBank = checkPayment; nextPollAt = Date.now() + (screen === 'payment' ? 2000 : 4000); renderPaymentCountdown();
        try {
            const old = server.mode, oldQr = server.qr?.id, oldStatus = server.paymentStatus, hadQrImage = !!server.qr?.qrDataUrl;
            const next = await api('api/poll', { recentActivity: Date.now() - activity < 15000 && !['idle', 'paused'].includes(screen), checkPayment });
            const before = screen; apply(next); network.hidden = true; bankRetry = false;
            if (before !== screen || old !== next.mode || oldQr !== next.qr?.id || oldStatus !== next.paymentStatus || hadQrImage !== !!next.qr?.qrDataUrl) { dialog.close(); render(); }
        }
        catch (err) { network.hidden = false; network.textContent = server ? 'Kết nối đang gián đoạn. Giao dịch được giữ để kiểm tra lại; không thanh toán thêm.' : err.message; if (checkPayment) bankRetry = true; }
        finally {
            // Callback-state reads must not postpone the independent bank-check deadline.
            if (checkPayment && screen === 'payment') nextBankCheckAt = Date.now() + 8000;
            checkingBank = false; polling = false; renderPaymentCountdown();
        }
    }
    setInterval(() => { renderPaymentCountdown(); if (Date.now() >= nextPollAt) poll(); }, 1000);
    setInterval(() => {
        if (!server || busy || disposed) return;
        if (screen === 'success') {
            const completed = server.completedAtUtc ? Date.parse(server.completedAtUtc.endsWith('Z') ? server.completedAtUtc : server.completedAtUtc + 'Z') : Date.now();
            const remain = Math.max(0, Math.ceil((completed + 15000 - Date.now()) / 1000)); const label = root.querySelector('#g-countdown'); if (label) label.textContent = `Tự trở về quảng cáo sau ${remain} giây`;
            if (remain <= 0) work(async () => { await command('finish'); customer = null; history = null; screen = 'idle'; });
        } else if (screen === 'idle') {
            const duration = Math.max(5, Number(promos[promoIndex % Math.max(promos.length, 1)]?.durationSeconds || 15)) * 1000;
            if (promos.length > 1 && Date.now() - promoAt >= duration) { promoIndex++; promoAt = Date.now(); render(); }
        } else if (!['payment', 'review', 'activate', 'paused'].includes(screen) && Date.now() - activity > 90000) {
            work(async () => { dialog.close(); if (customer) await endCustomer(); if (server.order && server.mode === 'shop') await command('cancel'); screen = 'idle'; selected = null; query = ''; activity = Date.now(); });
        }
    }, 1000);
    async function promotions() { if (!server) return; try { promos = await api('api/promotions'); if (screen === 'idle') render(); } catch { /* Idle fallback is always available. */ } }
    setInterval(promotions, 60000);
    window.addEventListener('pageshow', ev => { if (ev.persisted) location.reload(); });
    window.addEventListener('pagehide', () => { disposed = true; customer = null; history = null; content.replaceChildren(); dialog.close(); });
    (async () => { try { apply(await api('api/state')); await poll(); await promotions(); render(); } catch (err) { error = err.message; if (!server) screen = 'activate'; render(); } })();
})();
