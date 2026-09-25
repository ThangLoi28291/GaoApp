(function () {
    'use strict';

    const app = document.getElementById('customerDisplayApp');

    let connection = null;

    let refreshTimer = null;
    let pollingTimer = null;
    let backupPollingTimer = null;
    let depositQrPollingTimer = null;
    let activeDepositQr = false;

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
    let promotionStarted = false;
    let promotionLoadVersion = 0;
    let isIdleVisible = false;
    let idleMediaPreloadCache = new Map();

    let countdownTimer = null;

    let cartScrollTimer = null;
    let cartSignature = '';
    let displayInfoLoading = false;

    async function loadDisplayInfo() {
        if (displayInfoLoading) return;
        displayInfoLoading = true;
        try {
            const res = await fetch('/admin/pos/customer-display/info', {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            if (res.status === 401 || res.status === 403 || res.redirected) {
                setText('cdCashierName', 'Chờ nhân viên đăng nhập');
                return;
            }
            if (!res.ok) return;
            const info = await res.json();
            if (String(info.storeId) !== String(getStoreId()) || String(info.terminalId ?? '') !== String(getTerminalId())) {
                // Rebind the display and hub groups together if a new login selects another counter.
                location.reload();
                return;
            }
            setText('cdCashierName', info.cashierName || 'Chưa xác định nhân viên');
            setText('cdTerminalName', info.terminalName || 'Quầy thanh toán');
            setText('cdWifiName', info.wifiName || '');
            setText('cdWifiPassword', info.wifiPassword || 'Không cần mật khẩu');
            document.getElementById('cdWifi').hidden = !info.wifiName;
        } catch { /* Keep confirmed information while the server is unavailable. */ }
        finally { displayInfoLoading = false; }
    }

    function productImage(line) {
        const value = line?.imageThumbUrl || line?.imageUrl || '';
        if (!value) return '';
        try {
            const url = new URL(value, location.origin);
            return ['http:', 'https:'].includes(url.protocol) ? url.href : '';
        } catch { return ''; }
    }

    function startCartScroll() {
        clearInterval(cartScrollTimer);
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        cartScrollTimer = setInterval(() => {
            const box = document.getElementById('cdCartLines');
            if (!box || isIdleVisible || app.classList.contains('is-paying') ||
                app.classList.contains('is-thanking') || box.matches(':hover, :focus-within')) return;
            if (box.scrollHeight <= box.clientHeight + 2) return;
            const atEnd = box.scrollTop + box.clientHeight >= box.scrollHeight - 5;
            box.scrollTo({ top: atEnd ? 0 : box.scrollTop + box.clientHeight - 40, behavior: 'smooth' });
        }, 7000);
    }

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
    // SHARED PROMOTION ENGINE: the same player stays mounted in idle and checkout.
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

            if (!res.ok) return null;

            const result = await res.json();

            return Array.isArray(result?.data) ? result.data : [];
        }
        catch (err) {
            console.warn('Load idle media failed:', err);

            return null;
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

        app.classList.add('has-promotions');

        const type = String(item.mediaType || 'text').toLowerCase();
        const title = escapeHtml(item.title || app.dataset.storeName || 'Cửa hàng');
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
                    if (!img.isConnected) return;
                    console.warn('Idle image load failed:', mediaUrl);
                    skipFailedIdleMedia(mediaUrl);
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
                        if (!video.isConnected) return;
                        console.warn('Video autoplay blocked. Skip to next media.');
                        skipFailedIdleMedia(mediaUrl);
                    });
                }

                video.onended = function () {
                    if (!video.isConnected) return;
                    goNextIdleMedia();
                };

                video.onerror = function () {
                    if (!video.isConnected) return;
                    console.warn('Idle video load failed:', mediaUrl);
                    skipFailedIdleMedia(mediaUrl);
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
        app.classList.remove('has-promotions');

        stage.replaceChildren(document.getElementById('cdIdleFallbackTemplate').content.cloneNode(true));
    }

    function skipFailedIdleMedia(url) {
        stopIdlePromoRotation();
        idleMediaItems = idleMediaItems.filter(item => item.mediaUrl !== url);
        idleMediaIndex--;
        goNextIdleMedia();
    }

    function goNextIdleMedia() {
        if (!Array.isArray(idleMediaItems) || idleMediaItems.length === 0) {
            const stage = document.getElementById('cdIdleMediaStage');

            if (stage) {
                renderIdleFallback(stage);
            }

            return;
        }

        clearTimeout(idleMediaTimer);

        idleMediaIndex++;

        if (idleMediaIndex >= idleMediaItems.length) {
            idleMediaIndex = 0;
        }

        renderIdleMedia(idleMediaItems[idleMediaIndex]);

        scheduleNextIdleMedia();
    }

    function scheduleNextIdleMedia() {
        clearTimeout(idleMediaTimer);

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
        const version = ++promotionLoadVersion;
        const items = await loadIdleMediaItems();
        if (version !== promotionLoadVersion) return;
        // Keep the current player on an unchanged update or a temporary server failure.
        if ((items === null || JSON.stringify(items) === JSON.stringify(idleMediaItems)) &&
            document.getElementById('cdIdleMediaStage')?.childElementCount) return;

        stopIdlePromoRotation();
        idleMediaItems = items || [];
        preloadIdleMediaItems(idleMediaItems);

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
        await startIdlePromoRotation();
    }

    function showIdleScreen() {
        const idle = document.getElementById('cdIdleScreen');

        if (!idle) return;

        if (isIdleVisible) {
            return;
        }

        isIdleVisible = true;
        app.dataset.view = 'idle';
        clearInterval(cartScrollTimer);

        idle.setAttribute('aria-label', 'Chào mừng quý khách');
        if (!promotionStarted) {
            promotionStarted = true;
            startIdlePromoRotation();
        }
    }

    function hideIdleScreen() {
        const idle = document.getElementById('cdIdleScreen');
        if (!idle) return;

        isIdleVisible = false;
        app.dataset.view = 'cart';

        idle.setAttribute('aria-label', 'Thông tin và quảng cáo tại tiệm');
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
            line?.lineId ||
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
        const image = document.getElementById('cdHeroImage');
        setText('cdHeroName', line ? getDisplayName(line) : 'Sản phẩm của bạn');
        const fields = [
            ['cdHeroQty', 'Số lượng', `${formatMoney(line?.quantity)} ${escapeHtml(line?.sellingUnitName || line?.unitName || '')}`],
            ['cdHeroPrice', 'Đơn giá', `${formatMoney(line?.unitPrice)} ₫`],
            ['cdHeroTotal', 'Thành tiền', `${formatMoney(line?.lineTotal)} ₫`]
        ];
        fields.forEach(([id, label, value]) => {
            document.getElementById(id).innerHTML = `<span class="cd-hero-meta-label">${label}</span>${value}`;
        });
        const url = productImage(line);
        image.hidden = !url;
        if (url && image.getAttribute('src') !== url) {
            image.src = url;
            image.alt = getDisplayName(line);
            image.onerror = () => { image.hidden = true; };
        } else if (!url) image.removeAttribute('src');
        const signature = line ? buildLineSignature(line) : '';
        if (signature !== lastHeroLineKey) {
            hero.classList.remove('is-hero-changed');
            void hero.offsetWidth;
            hero.classList.add('is-hero-changed');
            lastHeroLineKey = signature;
        }
    }
    function renderEmpty(message) {
        cartSignature = '';
        setText('cdCustomerName', 'Quý khách');
        setText('cdCustomerPhone', '');
        document.getElementById('cdSettlement').hidden = true;
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
        if (!Array.isArray(lines) || !lines.length) { renderEmpty(); return; }
        const signature = JSON.stringify(lines);
        hideIdleScreen();
        if (signature === cartSignature) return;
        cartSignature = signature;
        const changedKey = detectChangedLine(lines);
        if (changedKey) lastChangedLineKey = changedKey;
        const sorted = [...lines].sort((a, b) => (buildLineKey(b) === lastChangedLineKey ? 1 : 0) - (buildLineKey(a) === lastChangedLineKey ? 1 : 0));
        renderHeroProduct(sorted[0]);
        box.innerHTML = sorted.map((line, index) => {
            const key = buildLineKey(line) || String(index);
            const changed = key === lastChangedLineKey;
            const name = getDisplayName(line);
            const url = productImage(line);
            return `<div class="cd-line ${changed ? 'is-changed' : ''}">
                <div class="cd-line-image"><svg aria-hidden="true"><use href="#cd-icon-bag" /></svg>${url ? `<img src="${escapeHtml(url)}" alt="" loading="lazy" />` : ''}</div>
                <div class="cd-line-main"><div class="cd-line-name">${escapeHtml(name)}${changed ? '<span class="cd-new-label">VỪA CẬP NHẬT</span>' : ''}</div>
                <div class="cd-line-meta">${formatMoney(line.quantity)} ${escapeHtml(line.sellingUnitName || line.unitName || '')}<span>×</span>${formatMoney(line.unitPrice)} ₫</div></div>
                <div class="cd-line-total">${formatMoney(line.lineTotal)}<span class="cd-line-currency"> ₫</span></div></div>`;
        }).join('');
        box.querySelectorAll('img').forEach(img => { img.onerror = () => img.remove(); });
        box.scrollTop = 0;
        setText('cdItemCount', formatMoney(lines.length));
        setText('cdQtyCount', formatMoney(lines.reduce((sum, line) => sum + Number(line.quantity || 0), 0)));
        lastLineSignatureMap = Object.fromEntries(lines.map(line => [buildLineKey(line), buildLineSignature(line)]));
        startCartScroll();
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
            'Quý khách'
        );

        setText(
            'cdCustomerPhone',
            draft?.customerPhone ||
            draft?.customer?.phone ||
            draft?.customer?.phoneNumber ||
            ''
        );

        setText('cdGrandTotal', formatMoney(draft.grandTotal));
        setText('cdSubtotal', formatMoney(draft.subtotal));
        setText('cdDiscount', formatMoney((draft.discountTotal || 0) + (draft.orderDiscount || 0)));
        setText('cdPaid', formatMoney(getPaidValue(draft)));
        setText('cdBalance', formatMoney(getBalanceValue(draft)));
        setText('cdChange', formatMoney(getChangeValue(draft)));
        document.getElementById('cdDiscountRow').hidden = !((draft.discountTotal || 0) + (draft.orderDiscount || 0));
        document.getElementById('cdSettlement').hidden = getPaidValue(draft) <= 0 && getChangeValue(draft) <= 0;
        document.getElementById('cdChangeRow').hidden = getChangeValue(draft) <= 0;
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
                setConnectionState('Đang chờ cập nhật từ quầy', false);
                return;
            }

            const screen = await res.json();
            setConnectionState('Sẵn sàng phục vụ', true);

            renderDraft(screen?.currentDraft || null);
        }
        catch (err) {
            console.error('Customer display load screen failed:', reason, err);
            setConnectionState('Đang chờ cập nhật từ quầy', false);
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
        overlay.classList.remove('is-qr-fullscreen');
        app.classList.add('is-paying');
        document.getElementById('cdCashVisual').hidden = false;

        const method = Number(payload?.method || 0);
        const amount = Number(payload?.amount || 0);
        const expectedBalance = Number(payload?.expectedBalance || 0);
        const expectedChange = Number(payload?.expectedChange || 0);

        if (qrBox) {
            qrBox.classList.add('d-none');
        }

        if (title) {
            title.textContent = method === 1 ? 'Thanh toán chuyển khoản.' : 'Thanh toán tiền mặt.';
        }

        if (message) {
            message.innerHTML = `
                <div>${method === 1 ? 'Số tiền chuyển khoản' : 'Số tiền khách đưa'}<strong>${formatMoney(amount)} ₫</strong></div>
                ${expectedBalance > 0 ? `<div class="cd-pay-row"><span>Còn cần thanh toán</span><b>${formatMoney(expectedBalance)} ₫</b></div>` : ''}
                <div class="cd-pay-row"><span>Tiền thừa trả bạn</span><b>${formatMoney(expectedChange)} ₫</b></div>
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

        activeDepositQr = payload?.kind === 'deposit';

        overlay.classList.remove('d-none');
        overlay.classList.add('is-qr-fullscreen');
        app.classList.add('is-paying');
        document.getElementById('cdCashVisual').hidden = true;

        if (title) {
            title.textContent = payload?.title || 'Thanh toán chuyển khoản';
        }

        if (message) {
            message.innerHTML = `
                <div>${escapeHtml(payload?.intro || 'Chuyển khoản đến')}</div>
                <div class="cd-bank-details"><span>${escapeHtml(payload?.bankName || '')}</span>
                    <b>${escapeHtml(payload?.accountNumber || '')}</b>
                    ${payload?.accountName ? `<span>${escapeHtml(payload.accountName)}</span>` : ''}</div>
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
        activeDepositQr = false;
        app.classList.remove('is-paying');
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
        app.classList.add('is-thanking');

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
        setText('cdSuccessIcon', isFinalized ? '✓' : '…');

        overlay.classList.remove('d-none', 'is-success', 'is-info');
        overlay.classList.add(isFinalized ? 'is-success' : 'is-info');

        if (isFinalized) {
            if (title) {
                title.textContent = payload?.title || 'Thanh toán thành công';
            }

            if (message) {
                message.textContent = payload?.message || 'Cảm ơn bạn. Hẹn gặp lại!';
            }

            if (amountBox) {
                amountBox.innerHTML = payload?.kind === 'deposit'
                    ? `<div class="is-good">Tiền cọc đã nhận: ${formatMoney(paid)} ₫</div>`
                    : change > 0
                        ? `<div class="is-good">Tiền thừa trả bạn: ${formatMoney(change)} ₫</div>`
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
                    <div>Đã nhận: ${formatMoney(paid)} ₫</div>
                    <div class="is-danger">Còn cần thanh toán: ${formatMoney(balance)} ₫</div>
                `;
            }
        }

        scheduleRefresh('payment-success');

        successTimer = setTimeout(function () {
            overlay.classList.add('d-none');
            app.classList.remove('is-thanking');
        }, Number(payload?.durationMs || 4500));
    }

    function resetCustomerDisplayAfterFinalize() {
        clearTimeout(successTimer);
        app.classList.remove('is-thanking');

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

        if (eventType === 'customer_display_info_changed') {
            loadDisplayInfo();
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
            setConnectionState('Đang cập nhật từ quầy', false);
            startFallbackPolling();
            return;
        }

        connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/pos')
            .withAutomaticReconnect([0, 1000, 2000, 5000])
            .build();

        connection.on('joined', function (data) {
            console.log('Customer display joined SignalR group:', data);

            setConnectionState('Sẵn sàng phục vụ', true);
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
            setConnectionState('Sẵn sàng phục vụ', true);
            stopFallbackPolling();

            await joinGroupsSafe();

            scheduleRefresh('signalr-reconnected');
        });

        connection.onclose(function () {
            setConnectionState('Đang cập nhật từ quầy', false);
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

            setConnectionState('Đang cập nhật từ quầy', false);
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

            setConnectionState('Vui lòng liên hệ thu ngân', false);

            showIdleScreen();

            stopFallbackPolling();
        }
        catch (err) {
            console.warn('JoinStoreGroup failed', err);

            setConnectionState('Đang cập nhật từ quầy', false);
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
            loadDisplayInfo();
        }, 15000);
    }

    async function loadActiveDepositQr() {
        try {
            const response = await fetch('/admin/customer-deposit/active-qr', {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
            });
            if (response.status === 204) {
                if (activeDepositQr) hidePaymentOverlay();
                return;
            }
            if (!response.ok) return;
            const qr = await response.json();
            showPaymentQr({
                ...qr,
                kind: 'deposit',
                title: 'Quét mã để đặt cọc',
                intro: 'Chuyển tiền đặt cọc đến'
            });
        } catch { /* SignalR remains the primary real-time path. */ }
    }

    function startDepositQrPolling() {
        if (depositQrPollingTimer) return;
        void loadActiveDepositQr();
        depositQrPollingTimer = setInterval(loadActiveDepositQr, 3000);
    }

    // =========================================================
    // STARTUP
    // =========================================================

    document.addEventListener('DOMContentLoaded', async function () {
        startIdleClock();
        loadDisplayInfo();
        startDepositQrPolling();
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden) loadDisplayInfo();
        });
        const fullscreen = document.getElementById('cdFullscreen');
        if (!document.fullscreenEnabled) fullscreen.hidden = true;
        fullscreen.addEventListener('click', async () => {
            try {
                if (document.fullscreenElement) await document.exitFullscreen();
                else await document.documentElement.requestFullscreen();
            } catch { /* The display remains usable in its current window. */ }
        });
        document.addEventListener('fullscreenchange', () => {
            fullscreen.setAttribute('aria-label', document.fullscreenElement ? 'Thoát toàn màn hình' : 'Bật toàn màn hình');
        });
        renderEmpty();

        const storeId = getStoreId();
        const terminalId = getTerminalId();

        if (!storeId || !terminalId) {
            setConnectionState('Vui lòng liên hệ thu ngân', false);
            showIdleScreen();
            return;
        }

        await loadScreen('initial');
        await startSignalR();
        startBackupPolling();
    });
})();
