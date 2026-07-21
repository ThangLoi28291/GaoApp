(function () {
    'use strict';

    const app = document.getElementById('customerDisplayApp');

    let connection = null;

    let refreshTimer = null;
    let pollingTimer = null;
    let backupPollingTimer = null;

    let lastLoadAt = 0;
    let isLoading = false;
    let pendingReason = null;

    let successTimer = null;

    let lastLineSignatureMap = {};
    let lastChangedLineKey = null;
    let lastHeroLineKey = null;

    let idleClockTimer = null;
    let idleMediaItems = [];
    let idleMediaIndex = 0;
    let idleMediaTimer = null;
    let idleMediaLoaded = false;
    let isIdleVisible = false;
    let idleMediaPreloadCache = new Map();

    let countdownTimer = null;

    const MAX_VISIBLE_LINES = 7;

    // =========================================================
    // BASIC HELPERS
    // =========================================================

    function formatMoney(value) {
        const n = Number(value || 0);
        return Number.isFinite(n) ? n.toLocaleString('vi-VN') : '0';
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function setText(id, value) {
        const el = document.getElementById(id);
        if (el) {
            el.textContent = value;
        }
    }

    function getStoreId() {
        return app?.dataset?.storeId || '';
    }

    function getTerminalId() {
        return app?.dataset?.terminalId || '';
    }

    function getPaidValue(draft) {
        return Number(draft?.paidTotal ?? draft?.paidAmount ?? 0);
    }

    function getBalanceValue(draft) {
        return Number(draft?.balanceDue ?? draft?.remainingAmount ?? 0);
    }

    function getChangeValue(draft) {
        return Number(draft?.changeDue ?? 0);
    }

    function getDisplayName(line) {
        return (
            line?.productVariantName ||
            line?.itemName ||
            line?.productName ||
            'Sản phẩm'
        );
    }

    function setConnectionState(text, isOnline) {
        const status = document.querySelector('.cd-status');
        if (!status) return;

        status.innerHTML = `
            <span class="cd-status-dot"></span>
            ${escapeHtml(text)}
        `;

        status.classList.toggle('is-offline', !isOnline);
    }

    // =========================================================
    // IDLE PROMOTION ENGINE
    // =========================================================

    async function loadIdleMediaItems() {
        try {
            const res = await fetch('/admin/displaypromotion/activeforcustomerdisplay', {
                method: 'GET',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json'
                }
            });

            if (!res.ok) {
                idleMediaItems = [];
                idleMediaLoaded = true;
                return;
            }

            const result = await res.json();

            idleMediaItems = Array.isArray(result?.data)
                ? result.data
                : [];

            preloadIdleMediaItems(idleMediaItems);

            idleMediaLoaded = true;
        }
        catch (err) {
            console.warn('Load idle media failed:', err);

            idleMediaItems = [];
            idleMediaLoaded = true;
        }
    }

    function preloadIdleMediaItems(items) {
        if (!Array.isArray(items)) return;

        items.forEach(function (item) {
            const url = item?.mediaUrl;
            const type = String(item?.mediaType || '').toLowerCase();

            if (!url || idleMediaPreloadCache.has(url)) return;

            if (type === 'image') {
                const img = new Image();

                img.onload = function () {
                    idleMediaPreloadCache.set(url, true);
                };

                img.onerror = function () {
                    idleMediaPreloadCache.set(url, false);
                };

                img.src = url;
                return;
            }

            if (type === 'video') {
                const video = document.createElement('video');

                video.preload = 'metadata';
                video.muted = true;
                video.playsInline = true;

                video.onloadedmetadata = function () {
                    idleMediaPreloadCache.set(url, true);
                };

                video.onerror = function () {
                    idleMediaPreloadCache.set(url, false);
                };

                video.src = url;
            }
        });
    }

    function formatCountdown(ms) {
        if (ms <= 0) return '00:00:00';

        const totalSeconds = Math.floor(ms / 1000);

        const h = Math.floor(totalSeconds / 3600);
        const m = Math.floor((totalSeconds % 3600) / 60);
        const s = totalSeconds % 60;

        return [
            String(h).padStart(2, '0'),
            String(m).padStart(2, '0'),
            String(s).padStart(2, '0')
        ].join(':');
    }

    function startPromotionCountdown() {
        stopPromotionCountdown();

        const el = document.querySelector('.cd-countdown');
        if (!el) return;

        const target = new Date(el.dataset.countdown);
        if (Number.isNaN(target.getTime())) return;

        function tick() {
            const remain = target.getTime() - Date.now();

            if (remain <= 0) {
                el.textContent = 'ĐÃ KẾT THÚC';
                stopPromotionCountdown();
                return;
            }

            el.textContent = formatCountdown(remain);
        }

        tick();

        countdownTimer = setInterval(tick, 1000);
    }

    function stopPromotionCountdown() {
        if (countdownTimer) {
            clearInterval(countdownTimer);
            countdownTimer = null;
        }
    }

    function buildPromotionOverlayHtml(item) {
        const isFlashSale = !!item?.isFlashSale;
        const countdownToUtc = item?.countdownToUtc;

        if (!isFlashSale && !countdownToUtc) {
            return '';
        }

        return `
            <div class="cd-idle-media-overlay">
                ${isFlashSale ? `
                    <div class="cd-flash-sale-badge">
                        FLASH SALE
                    </div>
                ` : ''}

                ${countdownToUtc ? `
                    <div class="cd-countdown"
                         data-countdown="${escapeHtml(countdownToUtc)}">
                        00:00:00
                    </div>
                ` : ''}
            </div>
        `;
    }

    function renderIdleMedia(item) {
        const stage = document.getElementById('cdIdleMediaStage');
        const idle = document.getElementById('cdIdleScreen');

        if (!stage) return;

        stopPromotionCountdown();

        stage.classList.remove('is-changing');
        void stage.offsetWidth;
        stage.classList.add('is-changing');

        stage.classList.remove('is-fullscreen-media');
        idle?.classList.remove('has-fullscreen-media');

        if (!item) {
            renderIdleFallback(stage);
            return;
        }

        const type = String(item.mediaType || 'text').toLowerCase();
        const title = escapeHtml(item.title || 'GaoMart');
        const desc = escapeHtml(item.description || '');
        const mediaUrl = item.mediaUrl || '';
        const bg = item.backgroundColor || '';
        const color = item.textColor || '';
        const isFullscreen = !!item.isFullscreen;

        stage.classList.toggle('is-fullscreen-media', isFullscreen);
        idle?.classList.toggle('has-fullscreen-media', isFullscreen);

        const overlayHtml = buildPromotionOverlayHtml(item);

        if (type === 'image' && mediaUrl) {
            stage.innerHTML = `
                <img class="cd-idle-media-image"
                     src="${escapeHtml(mediaUrl)}"
                     alt="${title}" />
                ${overlayHtml}
            `;

            const img = stage.querySelector('img');

            if (img) {
                img.onerror = function () {
                    console.warn('Idle image load failed:', mediaUrl);
                    goNextIdleMedia();
                };
            }

            startPromotionCountdown();
            return;
        }

        if (type === 'video' && mediaUrl) {
            stage.innerHTML = `
                <video class="cd-idle-media-video"
                       src="${escapeHtml(mediaUrl)}"
                       autoplay
                       muted
                       playsinline
                       preload="auto"></video>
                ${overlayHtml}
            `;

            const video = stage.querySelector('video');

            if (video) {
                video.muted = true;
                video.playsInline = true;

                const playPromise = video.play();

                if (playPromise && typeof playPromise.catch === 'function') {
                    playPromise.catch(function () {
                        console.warn('Video autoplay blocked. Skip to next media.');
                        goNextIdleMedia();
                    });
                }

                video.onended = function () {
                    goNextIdleMedia();
                };

                video.onerror = function () {
                    console.warn('Idle video load failed:', mediaUrl);
                    goNextIdleMedia();
                };
            }

            startPromotionCountdown();
            return;
        }

        stage.innerHTML = `
            <div class="cd-idle-media-text"
                 style="${bg ? `background:${escapeHtml(bg)};` : ''}${color ? `color:${escapeHtml(color)};` : ''}">
                <div class="cd-idle-promo-title">
                    ${title}
                </div>

                <div class="cd-idle-promo-desc">
                    ${desc}
                </div>

                ${overlayHtml}
            </div>
        `;

        startPromotionCountdown();
    }

    function renderIdleFallback(stage) {
        const idle = document.getElementById('cdIdleScreen');

        stage.classList.remove('is-fullscreen-media');
        idle?.classList.remove('has-fullscreen-media');

        stage.innerHTML = `
            <div class="cd-idle-media-fallback">
                <div class="cd-idle-promo-title">
                    GaoMart
                </div>

                <div class="cd-idle-promo-desc">
                    Hàng tốt mỗi ngày • Thanh toán nhanh chóng
                </div>
            </div>
        `;
    }

    function goNextIdleMedia() {
        if (!isIdleVisible) return;

        if (!Array.isArray(idleMediaItems) || idleMediaItems.length === 0) {
            const stage = document.getElementById('cdIdleMediaStage');

            if (stage) {
                renderIdleFallback(stage);
            }

            return;
        }

        stopIdlePromoRotation();

        idleMediaIndex++;

        if (idleMediaIndex >= idleMediaItems.length) {
            idleMediaIndex = 0;
        }

        renderIdleMedia(idleMediaItems[idleMediaIndex]);

        scheduleNextIdleMedia();
    }

    function scheduleNextIdleMedia() {
        stopIdlePromoRotation();

        if (!Array.isArray(idleMediaItems) || idleMediaItems.length === 0) {
            return;
        }

        const current = idleMediaItems[idleMediaIndex];

        const seconds = Number(current?.durationSeconds || 6);
        const durationMs = Math.max(3, seconds) * 1000;

        idleMediaTimer = setTimeout(function () {
            goNextIdleMedia();
        }, durationMs);
    }

    async function startIdlePromoRotation() {
        stopIdlePromoRotation();

        if (!idleMediaLoaded) {
            await loadIdleMediaItems();
        }

        if (!idleMediaItems.length) {
            const stage = document.getElementById('cdIdleMediaStage');

            if (stage) {
                renderIdleFallback(stage);
            }

            return;
        }

        idleMediaIndex = 0;

        renderIdleMedia(idleMediaItems[idleMediaIndex]);

        scheduleNextIdleMedia();
    }

    function stopIdlePromoRotation() {
        if (idleMediaTimer) {
            clearTimeout(idleMediaTimer);
            idleMediaTimer = null;
        }

        stopPromotionCountdown();
    }

    async function refreshIdleMediaRealtime() {
        idleMediaLoaded = false;

        stopIdlePromoRotation();

        await loadIdleMediaItems();

        if (isIdleVisible) {
            await startIdlePromoRotation();
        }
    }

    function showIdleScreen() {
        const idle = document.getElementById('cdIdleScreen');

        if (!idle) return;

        if (isIdleVisible) {
            return;
        }

        isIdleVisible = true;

        idle.classList.remove('d-none');

        startIdleClock();
        startIdlePromoRotation();
    }

    function hideIdleScreen() {
        const idle = document.getElementById('cdIdleScreen');
        const stage = document.getElementById('cdIdleMediaStage');

        if (!idle) return;

        isIdleVisible = false;

        idle.classList.add('d-none');
        idle.classList.remove('has-fullscreen-media');

        stage?.classList.remove('is-fullscreen-media');

        stopIdleClock();
        stopIdlePromoRotation();

        idleMediaLoaded = false;
    }

    function startIdleClock() {
        stopIdleClock();

        function renderClock() {
            const now = new Date();

            const timeEl = document.getElementById('cdIdleTime');
            const dateEl = document.getElementById('cdIdleDate');

            if (timeEl) {
                timeEl.textContent =
                    now.toLocaleTimeString('vi-VN', {
                        hour: '2-digit',
                        minute: '2-digit'
                    });
            }

            if (dateEl) {
                dateEl.textContent =
                    now.toLocaleDateString('vi-VN', {
                        weekday: 'long',
                        day: '2-digit',
                        month: '2-digit',
                        year: 'numeric'
                    });
            }
        }

        renderClock();

        idleClockTimer = setInterval(renderClock, 1000);
    }

    function stopIdleClock() {
        if (idleClockTimer) {
            clearInterval(idleClockTimer);
            idleClockTimer = null;
        }
    }

    // =========================================================
    // CART / DRAFT RENDER
    // =========================================================

    function buildLineKey(line) {
        return String(
            line?.id ||
            line?.orderLineId ||
            line?.variantId ||
            line?.productVariantId ||
            line?.productId ||
            line?.barcode ||
            line?.itemName ||
            line?.productName ||
            ''
        );
    }

    function buildLineSignature(line) {
        return [
            buildLineKey(line),
            Number(line?.quantity || 0),
            Number(line?.unitPrice || 0),
            Number(line?.lineTotal || 0),
            line?.sellingUnitName || ''
        ].join('|');
    }

    function detectChangedLine(lines) {
        for (const line of lines) {
            const key = buildLineKey(line);
            const signature = buildLineSignature(line);

            if (!lastLineSignatureMap[key] || lastLineSignatureMap[key] !== signature) {
                return key;
            }
        }

        return null;
    }

    function renderHeroProduct(line) {
        const hero = document.getElementById('cdHeroProduct');
        const nameEl = document.getElementById('cdHeroName');
        const qtyEl = document.getElementById('cdHeroQty');
        const priceEl = document.getElementById('cdHeroPrice');
        const totalEl = document.getElementById('cdHeroTotal');

        if (!hero || !nameEl || !qtyEl || !priceEl) return;

        if (!line) {
            nameEl.textContent = 'Chưa có sản phẩm';

            qtyEl.innerHTML = `
                <span class="cd-hero-meta-label">Số lượng</span>
                0
            `;

            priceEl.innerHTML = `
                <span class="cd-hero-meta-label">Đơn giá</span>
                0
            `;

            if (totalEl) {
                totalEl.innerHTML = `
                    <span class="cd-hero-meta-label">Thành tiền</span>
                    0
                `;
            }

            return;
        }

        const key = buildLineKey(line);
        const name = getDisplayName(line);
        const qty = Number(line?.quantity || 0);
        const unit = line?.sellingUnitName || '';
        const price = Number(line?.unitPrice || 0);
        const total = Number(line?.lineTotal || 0);

        nameEl.textContent = name;

        qtyEl.innerHTML = `
            <span class="cd-hero-meta-label">Số lượng</span>
            ${formatMoney(qty)} ${escapeHtml(unit)}
        `;

        priceEl.innerHTML = `
            <span class="cd-hero-meta-label">Đơn giá</span>
            ${formatMoney(price)} đ
        `;

        if (totalEl) {
            totalEl.innerHTML = `
                <span class="cd-hero-meta-label">Thành tiền</span>
                ${formatMoney(total)} đ
            `;
        }

        if (key !== lastHeroLineKey) {
            hero.classList.remove('is-hero-changed');
            void hero.offsetWidth;
            hero.classList.add('is-hero-changed');

            lastHeroLineKey = key;
        }
    }

    function renderEmpty(message) {
        const box = document.getElementById('cdCartLines');

        if (box) {
            box.innerHTML = `
                <div class="cd-empty">
                    ${escapeHtml(message || 'Chưa có sản phẩm trong giỏ')}
                </div>
            `;
        }

        setText('cdGrandTotal', '0');
        setText('cdSubtotal', '0');
        setText('cdDiscount', '0');
        setText('cdPaid', '0');
        setText('cdBalance', '0');
        setText('cdChange', '0');
        setText('cdItemCount', '0');
        setText('cdQtyCount', '0');

        renderHeroProduct(null);

        lastLineSignatureMap = {};
        lastChangedLineKey = null;
        lastHeroLineKey = null;

        showIdleScreen();
    }

    function renderLines(lines) {
        const box = document.getElementById('cdCartLines');

        if (!box) return;

        if (!Array.isArray(lines) || lines.length === 0) {
            renderEmpty('Chưa có sản phẩm');
            return;
        }

        const changedKey = detectChangedLine(lines);

        hideIdleScreen();

        if (changedKey) {
            lastChangedLineKey = changedKey;
        }

        const sortedLines = [...lines].sort(function (a, b) {
            const ak = buildLineKey(a);
            const bk = buildLineKey(b);

            if (ak === lastChangedLineKey) return -1;
            if (bk === lastChangedLineKey) return 1;

            return 0;
        });

        const heroLine =
            sortedLines.find(x => buildLineKey(x) === lastChangedLineKey) ||
            sortedLines[0];

        renderHeroProduct(heroLine);

        const visibleLines = sortedLines.slice(0, MAX_VISIBLE_LINES);
        const hiddenCount = Math.max(0, sortedLines.length - visibleLines.length);

        const nextSignatureMap = {};
        let totalQty = 0;

        for (const line of lines) {
            totalQty += Number(line?.quantity || 0);
            nextSignatureMap[buildLineKey(line)] = buildLineSignature(line);
        }

        box.innerHTML = visibleLines.map(function (line, index) {
            const key = buildLineKey(line) || String(index);
            const isChanged = key === lastChangedLineKey;

            const name = getDisplayName(line);
            const unit = line?.sellingUnitName || '';
            const qty = Number(line?.quantity || 0);
            const price = Number(line?.unitPrice || 0);
            const total = Number(line?.lineTotal || 0);

            return `
                <div class="cd-line ${isChanged ? 'is-changed' : ''}">
                    <div class="cd-line-index">${index + 1}</div>

                    <div class="cd-line-main">
                        <div class="cd-line-name" title="${escapeHtml(name)}">
                            ${escapeHtml(name)}
                        </div>

                        <div class="cd-line-meta">
                            ${formatMoney(qty)} ${escapeHtml(unit)}
                            <span>×</span>
                            ${formatMoney(price)}
                        </div>
                    </div>

                    <div class="cd-line-total">
                        ${formatMoney(total)}
                    </div>
                </div>
            `;
        }).join('') + (
                hiddenCount > 0
                    ? `
                    <div class="cd-more-lines">
                        +${hiddenCount} sản phẩm khác trong giỏ
                    </div>
                `
                    : ''
            );

        setText('cdItemCount', formatMoney(lines.length));
        setText('cdQtyCount', formatMoney(totalQty));

        lastLineSignatureMap = nextSignatureMap;
    }

    function renderDraft(draft) {
        if (!draft) {
            renderEmpty('Chưa có giỏ hiện tại');
            return;
        }

        renderLines(draft.lines || []);

        setText(
            'cdCustomerName',
            draft?.customerName ||
            draft?.customer?.name ||
            'Khách lẻ'
        );

        setText(
            'cdCustomerPhone',
            draft?.customerPhone ||
            draft?.customer?.phone ||
            draft?.customer?.phoneNumber ||
            ''
        );

        setText(
            'cdCashierName',
            draft?.cashierName ||
            draft?.employeeName ||
            draft?.createdByName ||
            'Thu ngân'
        );

        setText('cdGrandTotal', formatMoney(draft.grandTotal));
        setText('cdSubtotal', formatMoney(draft.subtotal));
        setText('cdDiscount', formatMoney((draft.discountTotal || 0) + (draft.orderDiscount || 0)));
        setText('cdPaid', formatMoney(getPaidValue(draft)));
        setText('cdBalance', formatMoney(getBalanceValue(draft)));
        setText('cdChange', formatMoney(getChangeValue(draft)));
    }

    // =========================================================
    // POS SCREEN LOADING
    // =========================================================

    async function loadScreen(reason) {
        const now = Date.now();

        if (isLoading) {
            pendingReason = reason || 'pending';
            return;
        }

        if (now - lastLoadAt < 80) {
            clearTimeout(refreshTimer);
            refreshTimer = setTimeout(() => loadScreen(reason), 80);
            return;
        }

        isLoading = true;
        lastLoadAt = now;

        try {
            const res = await fetch('/admin/pos/screen', {
                method: 'GET',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json'
                }
            });

            if (!res.ok) {
                renderEmpty('Không tải được dữ liệu POS');
                return;
            }

            const screen = await res.json();

            renderDraft(screen?.currentDraft || null);
        }
        catch (err) {
            console.error('Customer display load screen failed:', reason, err);
            renderEmpty('Mất kết nối tới POS');
        }
        finally {
            isLoading = false;

            if (pendingReason) {
                const nextReason = pendingReason;
                pendingReason = null;
                scheduleRefresh(nextReason);
            }
        }
    }

    function scheduleRefresh(reason) {
        clearTimeout(refreshTimer);
        refreshTimer = setTimeout(() => loadScreen(reason || 'signalr'), 20);
    }

    // =========================================================
    // PAYMENT OVERLAY
    // =========================================================

    function showPaymentPreview(payload) {
        const overlay = document.getElementById('cdPaymentOverlay');
        const title = document.getElementById('cdPaymentTitle');
        const message = document.getElementById('cdPaymentMessage');
        const qrBox = document.getElementById('cdQrBox');

        if (!overlay) return;

        overlay.classList.remove('d-none');

        const method = Number(payload?.method || 0);
        const amount = Number(payload?.amount || 0);
        const expectedBalance = Number(payload?.expectedBalance || 0);
        const expectedChange = Number(payload?.expectedChange || 0);

        if (qrBox) {
            qrBox.classList.add('d-none');
        }

        if (title) {
            title.textContent = method === 1
                ? 'Thanh toán chuyển khoản'
                : 'Thanh toán tiền mặt';
        }

        if (message) {
            message.innerHTML = `
                <div style="font-size:30px;font-weight:900;margin-top:12px;">
                    Khách đưa: ${formatMoney(amount)}
                </div>
                <div style="font-size:24px;margin-top:8px;color:#dc2626;font-weight:800;">
                    Còn thiếu: ${formatMoney(expectedBalance)}
                </div>
                <div style="font-size:24px;margin-top:8px;color:#047857;font-weight:800;">
                    Tiền thừa: ${formatMoney(expectedChange)}
                </div>
            `;
        }
    }

    function showPaymentQr(payload) {
        const overlay = document.getElementById('cdPaymentOverlay');
        const title = document.getElementById('cdPaymentTitle');
        const message = document.getElementById('cdPaymentMessage');
        const qrBox = document.getElementById('cdQrBox');
        const qrImg = document.getElementById('cdQrImage');

        if (!overlay) return;

        overlay.classList.remove('d-none');
        overlay.classList.add('is-qr-fullscreen');

        if (title) {
            title.textContent = 'Quét mã QR để thanh toán';
        }

        if (message) {
            message.innerHTML = `
                <div style="font-size:26px;font-weight:800;">
                    ${escapeHtml(payload?.bankName || '')}
                </div>
                <div style="font-size:24px;margin-top:8px;">
                    STK: <b>${escapeHtml(payload?.accountNumber || '')}</b>
                </div>
            `;
        }

        if (qrBox) {
            qrBox.classList.remove('d-none');
        }

        if (qrImg) {
            qrImg.src = payload?.qrDataUrl || '';
        }

        setText('cdQrAmount', formatMoney(payload?.amount || 0));
        setText('cdQrContent', payload?.content || '');
    }

    function hidePaymentOverlay() {
        const overlay = document.getElementById('cdPaymentOverlay');
        const qrBox = document.getElementById('cdQrBox');
        const qrImg = document.getElementById('cdQrImage');

        if (overlay) {
            overlay.classList.add('d-none');
            overlay.classList.remove('is-qr-fullscreen');
        }

        if (qrBox) {
            qrBox.classList.add('d-none');
        }

        if (qrImg) {
            qrImg.src = '';
        }
    }

    function showPaymentSuccess(payload) {
        hidePaymentOverlay();

        clearTimeout(successTimer);

        const overlay = document.getElementById('cdSuccessOverlay');
        if (!overlay) return;

        const title = document.getElementById('cdSuccessTitle');
        const message = document.getElementById('cdSuccessMessage');
        const amountBox = document.getElementById('cdSuccessAmount');

        const paid = Number(payload?.paidAmount || 0);
        const balance = Number(payload?.remainingAmount || 0);
        const change = Number(payload?.changeAmount || 0);
        const isFinalized = payload?.finalized === true;

        overlay.classList.remove('d-none', 'is-success', 'is-info');
        overlay.classList.add(isFinalized ? 'is-success' : 'is-info');

        if (isFinalized) {
            if (title) {
                title.textContent = 'Thanh toán thành công';
            }

            if (message) {
                message.textContent = 'Cảm ơn quý khách!';
            }

            if (amountBox) {
                amountBox.innerHTML = change > 0
                    ? `<div class="is-good">Tiền thối: ${formatMoney(change)}</div>`
                    : '';
            }
        }
        else {
            if (title) {
                title.textContent = 'Đã nhận thanh toán';
            }

            if (message) {
                message.textContent = 'Vui lòng thanh toán phần còn lại';
            }

            if (amountBox) {
                amountBox.innerHTML = `
                    <div>Đã nhận: ${formatMoney(paid)}</div>
                    <div class="is-danger">Còn thiếu: ${formatMoney(balance)}</div>
                `;
            }
        }

        scheduleRefresh('payment-success');

        successTimer = setTimeout(function () {
            overlay.classList.add('d-none');
        }, Number(payload?.durationMs || 4500));
    }

    function resetCustomerDisplayAfterFinalize() {
        clearTimeout(successTimer);

        hidePaymentOverlay();

        const successOverlay = document.getElementById('cdSuccessOverlay');

        if (successOverlay) {
            successOverlay.classList.add('d-none');
        }

        renderEmpty('Cảm ơn quý khách. Đang chờ đơn hàng mới');

        scheduleRefresh('order-finalized-reset');
    }

    // =========================================================
    // SIGNALR
    // =========================================================

    function handlePosEvent(eventData) {
        console.log('Customer display SignalR event:', eventData);

        const eventType = String(
            eventData?.eventType ||
            eventData?.type ||
            eventData?.name ||
            ''
        ).toLowerCase();

        const payload = eventData?.payload || {};

        if (eventType === 'customer_payment_preview') {
            showPaymentPreview(payload);
            return;
        }

        if (eventType === 'customer_payment_qr_created') {
            showPaymentQr(payload);
            return;
        }

        if (eventType === 'customer_payment_hide') {
            hidePaymentOverlay();
            scheduleRefresh('customer-payment-hide');
            return;
        }

        if (eventType === 'customer_payment_changed') {
            hidePaymentOverlay();
            scheduleRefresh('customer-payment-changed');
            return;
        }

        if (eventType === 'customer_payment_success') {
            showPaymentSuccess(payload);
            return;
        }

        if (eventType === 'customer_display_reset') {
            resetCustomerDisplayAfterFinalize();
            return;
        }

        if (eventType === 'customer_display_promotion_changed') {
            refreshIdleMediaRealtime();
            return;
        }

        if (
            eventType.includes('cart') ||
            eventType.includes('draft') ||
            eventType.includes('payment') ||
            eventType.includes('order') ||
            eventType.includes('qr') ||
            eventType.includes('line') ||
            eventType.includes('item') ||
            eventType.includes('screen')
        ) {
            scheduleRefresh(eventType);
            return;
        }

        scheduleRefresh('unknown-pos-event');
    }

    async function startSignalR() {
        if (!window.signalR) {
            console.warn('SignalR client not found. Fallback polling enabled.');
            setConnectionState('Mất realtime - đang tự cập nhật', false);
            startFallbackPolling();
            return;
        }

        connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/pos')
            .withAutomaticReconnect([0, 1000, 2000, 5000])
            .build();

        connection.on('joined', function (data) {
            console.log('Customer display joined SignalR group:', data);

            setConnectionState('Đã kết nối realtime', true);
            stopFallbackPolling();
            scheduleRefresh('joined');
        });

        connection.on('pos:event', handlePosEvent);
        connection.on('pos.event', handlePosEvent);
        connection.on('PosEvent', handlePosEvent);
        connection.on('ReceivePosEvent', handlePosEvent);
        connection.on('cart_updated', handlePosEvent);
        connection.on('draft_updated', handlePosEvent);
        connection.on('order_updated', handlePosEvent);
        connection.on('payment_updated', handlePosEvent);
        connection.on('payment_qr_created', handlePosEvent);
        connection.on('payment_completed', handlePosEvent);

        connection.onreconnecting(function () {
            setConnectionState('Đang kết nối lại', false);
            startFallbackPolling();
        });

        connection.onreconnected(async function () {
            setConnectionState('Đã kết nối realtime', true);
            stopFallbackPolling();

            await joinGroupsSafe();

            scheduleRefresh('signalr-reconnected');
        });

        connection.onclose(function () {
            setConnectionState('Mất realtime - đang tự cập nhật', false);
            startFallbackPolling();
        });

        try {
            await connection.start();

            console.log('Customer display SignalR connected.');

            await joinGroupsSafe();

            scheduleRefresh('signalr-started');
        }
        catch (err) {
            console.error('SignalR start failed', err);

            setConnectionState('Mất realtime - đang tự cập nhật', false);
            startFallbackPolling();
        }
    }

    async function joinGroupsSafe() {
        if (!connection) return;

        const storeId = getStoreId();
        const terminalId = getTerminalId();

        console.log('Customer display joining group:', { storeId, terminalId });

        try {
            if (storeId && terminalId) {
                await connection.invoke('JoinStoreGroup', Number(storeId), terminalId);
                return;
            }

            console.warn('Missing storeId or terminalId, cannot join POS group.', { storeId, terminalId });

            setConnectionState('Thiếu thông tin quầy - đang chờ cấu hình', false);

            showIdleScreen();

            stopFallbackPolling();
        }
        catch (err) {
            console.warn('JoinStoreGroup failed', err);

            setConnectionState('Không join được realtime - đang tự cập nhật', false);
            startFallbackPolling();
        }
    }

    // =========================================================
    // POLLING FALLBACK
    // =========================================================

    function startFallbackPolling() {
        if (pollingTimer) return;

        pollingTimer = setInterval(function () {
            loadScreen('fallback-polling');
        }, 3000);
    }

    function stopFallbackPolling() {
        if (!pollingTimer) return;

        clearInterval(pollingTimer);
        pollingTimer = null;
    }

    function startBackupPolling() {
        if (backupPollingTimer) return;

        backupPollingTimer = setInterval(function () {
            loadScreen('backup-polling');
        }, 15000);
    }

    // =========================================================
    // STARTUP
    // =========================================================

    document.addEventListener('DOMContentLoaded', async function () {
        renderEmpty();

        const storeId = getStoreId();
        const terminalId = getTerminalId();

        if (!storeId || !terminalId) {
            setConnectionState('Thiếu thông tin quầy - đang chờ cấu hình', false);
            showIdleScreen();
            return;
        }

        await loadScreen('initial');
        await startSignalR();
        startBackupPolling();
    });
})();