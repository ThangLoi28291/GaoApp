/* =========================================================
   FILE: pos.render.js
   Mục đích:
   - Chứa toàn bộ hàm render UI cho màn hình POS
   - Không bind event, không gọi API
   - Giữ style cũ của hệ thống
   - Tối ưu đến mức:
     + 3A: tách render line/table rõ ràng
     + 3B: patch section-level chính xác
     + 3C: patch row-level theo data-line-id
     + 4C.1: render network state banner
========================================================= */

window.PosRender = (function () {
    'use strict';

    const {
        formatMoney,
        escapeHtml
    } = window.PosCommon;

    /* =========================================================
       1. HELPER CHUNG
    ========================================================= */
    function buildVariantDisplayName(productName, variantName) {
        const v = (variantName || '').trim();
        const p = (productName || '').trim();

        // Yêu cầu mới:
        // Ưu tiên chỉ hiện ProductVariantName.
        // Chỉ fallback về ProductName nếu variant rỗng.
        return v || p || '';
    }

    function buildCompactLineMeta(line) {
        const parts = [];

        if (line?.barcode) {
            parts.push(escapeHtml(line.barcode));
        }

        if (line?.sellingUnitName) {
            parts.push(escapeHtml(line.sellingUnitName));
        }

        return parts.join(' · ');
    }

    function buildLineDetailHtml(line) {
        const prices = Array.isArray(line?.unitPrices || line?.UnitPrices)
            ? (line.unitPrices || line.UnitPrices)
            : [];

        if (!prices.length) {
            return `
            <div class="pos-price-popover-empty">
                Chưa có bảng giá đơn vị cho sản phẩm này.
            </div>
        `;
        }

        const title = line?.productVariantName || line?.itemName || 'Bảng giá sản phẩm';

        return `
        <div class="pos-price-popover">
            <div class="pos-price-popover-title">
                ${escapeHtml(title)}
            </div>

            <table class="pos-price-popover-table">
                <thead>
                    <tr>
                        <th>Đơn vị</th>
                        <th>Quy đổi</th>
                        <th>Giá lẻ</th>
                        <th>Giá sỉ</th>
                        <th>Đang áp</th>
                    </tr>
                </thead>
                <tbody>
                    ${prices.map(function (x) {
            const unitName = x.unitName || x.UnitName || '';
            const factor = x.factor || x.Factor || 1;
            const retailPrice = x.retailPrice ?? x.RetailPrice ?? 0;
            const wholesalePrice = x.wholesalePrice ?? x.WholesalePrice ?? 0;
            const effectivePrice = x.effectivePrice ?? x.EffectivePrice ?? 0;
            const isBaseUnit = !!(x.isBaseUnit || x.IsBaseUnit);
            const isCurrentUnit = !!(x.isCurrentUnit || x.IsCurrentUnit);

            return `
                            <tr class="${isCurrentUnit ? 'is-current' : ''}">
                                <td>
                                    <strong>${escapeHtml(unitName)}</strong>
                                    ${isBaseUnit ? '<span class="price-badge">Gốc</span>' : ''}
                                </td>
                                <td>x${formatMoney(factor)}</td>
                                <td>${retailPrice > 0 ? formatMoney(retailPrice) : '—'}</td>
                                <td>${wholesalePrice > 0 ? formatMoney(wholesalePrice) : '—'}</td>
                                <td><b>${effectivePrice > 0 ? formatMoney(effectivePrice) : '—'}</b></td>
                            </tr>
                        `;
        }).join('')}
                </tbody>
            </table>
        </div>
    `;
    }

    function initLineInfoPopovers() {
        if (!window.bootstrap) return;

        document.querySelectorAll('[data-line-detail-html]').forEach(el => {
            const html = decodeURIComponent(el.getAttribute('data-line-detail-html') || '');

            const old = bootstrap.Popover.getInstance(el);
            if (old) old.dispose();

            new bootstrap.Popover(el, {
                trigger: 'hover focus',
                placement: 'left',
                html: true,
                sanitize: false,
                content: html,
                container: 'body'
            });
        });
    }

    /* =========================================================
       2. HELPER CUSTOMER / SUMMARY
       - Hỗ trợ cả schema flat và nested
    ========================================================= */
    function getDraftCustomerInfo(draft) {
        return {
            customerId: Number(
                draft?.customer?.customerId ||
                draft?.customerId ||
                0
            ),
            name:
                draft?.customer?.name ||
                draft?.customerName ||
                '',
            phone:
                draft?.customer?.phone ||
                draft?.customerPhone ||
                '',
            address:
                draft?.customer?.address ||
                draft?.customerAddress ||
                '',

            // NEW: loại giá khách hàng
            priceTier:
                draft?.customer?.priceTier ||
                draft?.customerPriceTier ||
                draft?.priceTier ||
                ''
        };
    }

    function getDraftPaidValue(draft) {
        return Number(
            draft?.paidTotal ??
            draft?.paidAmount ??
            0
        );
    }

    function getDraftBalanceValue(draft) {
        return Number(
            draft?.balanceDue ??
            draft?.remainingAmount ??
            0
        );
    }

    function getDraftChangeValue(draft) {
        return Number(
            draft?.changeDue ??
            0
        );
    }

    /* =========================================================
       2.1. NETWORK UI HELPER (4C.1)
    ========================================================= */
    function formatNetworkTime(value) {
        if (!value) return '';

        try {
            const dt = new Date(value);
            if (Number.isNaN(dt.getTime())) return '';
            return dt.toLocaleTimeString('vi-VN');
        } catch {
            return '';
        }
    }

    function getNetworkUiState(posState) {
        const offline = window.PosOffline?.status();
        if (offline?.queueIssue) {
            return { variant: 'offline', title: 'Có thao tác offline cần đối soát',
                message: offline.queueIssue.message + ` Còn ${offline.pending} thao tác trong hàng đợi; dữ liệu chưa bị xóa.`,
                lastSyncAt: offline.lastSyncAt || posState?.offline?.lastSyncAt };
        }
        if (offline?.sessionIssue) {
            return { variant: 'blocked', title: offline.sessionIssue.title,
                message: offline.sessionIssue.message + (offline.pending ? ` Đang giữ ${offline.pending} thao tác tại quầy để đồng bộ/đối soát.` : ''),
                lastSyncAt: null };
        }
        if (offline?.preparing && !offline.message) {
            return { variant: 'reconnecting', title: 'Đang chuẩn bị bán offline',
                message: 'Bạn có thể bán hàng trực tuyến trong lúc tải dữ liệu. Bán offline sẽ sẵn sàng khi tải xong.', lastSyncAt: null };
        }
        if (offline?.message || offline?.ready && (!offline.connected || offline.pending)) {
            return {
                variant: 'offline',
                title: !offline.ready || !window.PosOffline.canWork() ? 'POS offline chưa sẵn sàng' : offline.connected ? 'Đang đồng bộ dữ liệu tại quầy' : 'Mất kết nối máy chủ — đang bán offline',
                message: offline.message || [offline.pending ? `${offline.pending} thao tác đã lưu tại quầy, đang chờ đồng bộ.` : 'Giao dịch được lưu trên máy tính tiền.',
                    offline.syncError, offline.lastSyncAttemptAt ? `Lần thử gần nhất: ${formatNetworkTime(offline.lastSyncAttemptAt)}${offline.syncing ? ' (đang chạy)' : ''}.` : ''].filter(Boolean).join(' '),
                lastSyncAt: offline.lastSyncAt || posState?.offline?.lastSyncAt
            };
        }
        const isOnline = !!posState?.offline?.isOnline;
        const connectionStatus = String(posState?.realtime?.connectionStatus || '').trim().toLowerCase();
        const lastSyncAt = posState?.offline?.lastSyncAt || posState?.network?.lastSuccessAt || null;

        if (!isOnline) {
            return {
                variant: 'offline',
                title: 'Mất kết nối mạng',
                message: 'Một số thao tác có thể thất bại cho đến khi mạng ổn định trở lại.',
                lastSyncAt
            };
        }

        if (connectionStatus === 'reconnecting' || connectionStatus === 'connecting') {
            return {
                variant: 'reconnecting',
                title: 'Đang kết nối lại',
                message: 'POS đang thử kết nối lại và đồng bộ dữ liệu.',
                lastSyncAt
            };
        }

        return {
            variant: 'online',
            title: 'Kết nối ổn định',
            message: 'POS đang hoạt động bình thường.',
            lastSyncAt
        };
    }

    function ensureNetworkBannerContainer() {
        let el = document.getElementById('posNetworkStateBanner');
        if (el) return el;

        const anchor =
            document.querySelector('.pos-shell') ||
            document.querySelector('.container-fluid') ||
            document.querySelector('main') ||
            document.body;

        el = document.createElement('div');
        el.id = 'posNetworkStateBanner';
        el.className = 'pos-network-banner-wrap';

        if (anchor.firstChild) {
            anchor.insertBefore(el, anchor.firstChild);
        } else {
            anchor.appendChild(el);
        }

        return el;
    }

    let networkBannerHideTimer = null;

    function renderNetworkBanner(posState) {
        const container = ensureNetworkBannerContainer();
        if (!container) return;

        const ui = getNetworkUiState(posState);
        const previousVariant = container.dataset.networkVariant;
        // Heartbeats and cart refreshes must not reopen or extend the recovery notice.
        if (ui.variant === 'online' && previousVariant === 'online') return;
        container.dataset.networkVariant = ui.variant;
        if (ui.variant === 'online' && !previousVariant) {
            container.style.display = 'none';
            return;
        }
        const lastSyncText = formatNetworkTime(ui.lastSyncAt);

        clearTimeout(networkBannerHideTimer);

        container.innerHTML = `
        <div class="pos-network-banner pos-network-banner--${ui.variant}">
            <div class="pos-network-banner__title">${escapeHtml(ui.title)}</div>
            <div class="pos-network-banner__message">
                ${escapeHtml(ui.message)}
                ${lastSyncText ? ` • Đồng bộ lần cuối: ${escapeHtml(lastSyncText)}` : ''}
            </div>
        </div>
    `;

        container.style.display = '';

        if (window.PosOffline?.status().pending > 0) {
            const tools = document.createElement('div'); tools.className = 'd-flex gap-2 px-3 pb-2';
            const queueIssue = window.PosOffline.status().queueIssue;
            const actions = queueIssue
                ? [[queueIssue.kind === 'payment-overpay' ? 'Cô lập khoản thu lỗi' : 'Cô lập thao tác lỗi', async () => {
                        await window.PosOffline.quarantineFirstBlocked(); await window.PosOffline.sync(true);
                    }],
                    ['Lưu bản đối soát', () => window.PosOffline.exportPending()]]
                : [['Thử đồng bộ', () => window.PosOffline.sync(true)], ['Lưu bản đối soát', () => window.PosOffline.exportPending()]];
            for (const [label, action] of actions) {
                const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-sm btn-outline-secondary';
                button.textContent = label;
                if (label === 'Thử đồng bộ' && window.PosOffline.status().syncing) { button.disabled = true; button.textContent = 'Đang đồng bộ…'; }
                button.addEventListener('click', () => Promise.resolve(action()).catch(error => {
                    window.dispatchEvent(new CustomEvent('pos:offline-status'));
                    window.alert(error?.message || 'Không thể xử lý thao tác offline.');
                })); tools.appendChild(button);
            }
            container.appendChild(tools);
        }

        // Chỉ báo phục hồi một lần trong 3 giây sau offline / reconnecting.
        if (ui.variant === 'online') {
            networkBannerHideTimer = setTimeout(function () {
                container.style.display = 'none';
            }, 3000);
            return;
        }

        // Offline / reconnecting: luôn hiện
        container.style.display = '';
    }

    /* =========================================================
       3. BADGE / SUMMARY / PAYMENT
    ========================================================= */
    function renderCurrentBadge(screen, draft) {
        const currentCartInfo = document.getElementById('currentCartInfo');
        const lblCurrentOrder = document.getElementById('lblCurrentOrder');

        if (!currentCartInfo || !lblCurrentOrder) return;

        if (!draft) {
            lblCurrentOrder.textContent = 'Chưa có giỏ';
            currentCartInfo.textContent = 'Chưa có giỏ hiện tại.';
            return;
        }

        const orderId = draft.orderId || screen?.currentOrderId || '';
        const lineCount = Array.isArray(draft.lines) ? draft.lines.length : 0;
        const paidTotal = getDraftPaidValue(draft);
        const grandTotal = Number(draft?.grandTotal || 0);

        lblCurrentOrder.textContent = orderId ? `Giỏ #${orderId}` : 'Giỏ hiện tại';

        currentCartInfo.innerHTML = `
            <span><strong>Giỏ #${escapeHtml(orderId || '-')}</strong></span>
            <span>·</span>
            <span><strong>${lineCount}</strong> dòng hàng</span>
            <span>·</span>
            <span>Đã thanh toán <strong>${formatMoney(paidTotal)}</strong> / <strong>${formatMoney(grandTotal)}</strong></span>
        `;
    }
    function setOrCreateSummaryRow(id, label, value, variant) {
        let row = document.getElementById(id);

        const anchor =
            document.getElementById('sumDiscount')?.closest('.pos-summary-info-item') ||
            document.getElementById('sumSubtotal')?.closest('.pos-summary-info-item');

        if (!anchor) return;

        if (!row) {
            row = document.createElement('div');
            row.id = id;
            row.className = `pos-summary-info-item pos-summary-info-item--${variant || 'default'}`;
            row.innerHTML = `
            <span class="pos-summary-info-item__label"></span>
            <strong class="pos-summary-info-item__value"></strong>
        `;
            anchor.insertAdjacentElement('afterend', row);
        }

        const labelEl = row.querySelector('.pos-summary-info-item__label');
        const valueEl = row.querySelector('.pos-summary-info-item__value');

        if (labelEl) labelEl.textContent = label;
        if (valueEl) valueEl.textContent = value > 0 ? `-${formatMoney(value)}` : '0';

        row.style.display = value > 0 ? '' : 'none';
    }

    function renderSummary(draft) {
        const sumSubtotal = document.getElementById('sumSubtotal');
        const sumDiscount = document.getElementById('sumDiscount');
        const sumGrandTotal = document.getElementById('sumGrandTotal');
        const sumPaid = document.getElementById('sumPaid');
        const sumBalance = document.getElementById('sumBalance');
        const sumChange = document.getElementById('sumChange');

        const promotionDiscountTotal = Number(
            draft?.promotionDiscountTotal ??
            draft?.PromotionDiscountTotal ??
            0
        );

        const orderDiscount = Number(
            draft?.orderDiscount ??
            draft?.OrderDiscount ??
            0
        );

        const voucherDiscountTotal = Number(
            draft?.voucherDiscountTotal ??
            draft?.VoucherDiscountTotal ??
            0
        );

        const normalDiscount = Math.max(
            Number(draft?.discountTotal || 0)
            - promotionDiscountTotal
            - orderDiscount
            - voucherDiscountTotal,
            0
        );
        const comboDiscountTotal = Number(
            draft?.comboDiscountTotal ??
            draft?.ComboDiscountTotal ??
            0
        );

        const comboPromotionNote = String(
            draft?.comboPromotionNote ??
            draft?.ComboPromotionNote ??
            ''
        ).trim();

        if (sumSubtotal) sumSubtotal.textContent = formatMoney(draft?.subtotal || 0);

        // sumDiscount giữ tổng giảm giá để không phá view cũ.
        if (sumDiscount) sumDiscount.textContent = formatMoney(draft?.discountTotal || 0);

        setOrCreateSummaryRow('sumPromotionDiscount', 'KM sản phẩm', promotionDiscountTotal, 'promotion');
        setOrCreateSummaryRow(
            'sumComboDiscount',
            comboPromotionNote || 'KM combo',
            comboDiscountTotal,
            'combo'
        );
        setOrCreateSummaryRow('sumOrderDiscountDetail', 'Giảm giá đơn hàng', orderDiscount, 'order-discount');
        setOrCreateSummaryRow('sumVoucherDiscountDetail', 'Voucher', voucherDiscountTotal, 'voucher');

        if (sumGrandTotal) sumGrandTotal.textContent = formatMoney(draft?.grandTotal || 0);
        if (sumPaid) sumPaid.textContent = formatMoney(getDraftPaidValue(draft));
        if (sumBalance) sumBalance.textContent = formatMoney(getDraftBalanceValue(draft));
        if (sumChange) sumChange.textContent = formatMoney(getDraftChangeValue(draft));
    }
    function renderSummaryState(draft) {
        const heroEl = document.getElementById('posSummaryHero');
        const stateTextEl = document.getElementById('sumPaymentStateText');

        if (!heroEl || !stateTextEl) return;

        heroEl.classList.remove(
            'pos-summary-hero--empty',
            'pos-summary-hero--due',
            'pos-summary-hero--paid',
            'pos-summary-hero--change'
        );

        if (!draft) {
            heroEl.classList.add('pos-summary-hero--empty');
            stateTextEl.textContent = 'Chưa có giỏ hiện tại';
            return;
        }

        const paid = getDraftPaidValue(draft);
        const balance = getDraftBalanceValue(draft);
        const change = getDraftChangeValue(draft);

        if (paid <= 0 && balance > 0) {
            heroEl.classList.add('pos-summary-hero--empty');
            stateTextEl.textContent = 'Chưa thanh toán';
            return;
        }

        if (balance > 0) {
            heroEl.classList.add('pos-summary-hero--due');
            stateTextEl.textContent = 'Đơn hàng chưa được thanh toán đủ';
            return;
        }

        if (change > 0) {
            heroEl.classList.add('pos-summary-hero--change');
            stateTextEl.textContent = 'Khách đưa dư tiền';
            return;
        }

        heroEl.classList.add('pos-summary-hero--paid');
        stateTextEl.textContent = 'Đã thanh toán đủ';
    }
    function renderSummaryActionState(draft) {
        const actionGrid = document.getElementById('posSummaryActionGrid');
        if (!actionGrid) return;

        const clearCartLinesButton = document.getElementById('btnClearCartLines');
        const hasLines = Array.isArray(draft?.lines)
            && draft.lines.some(line => !line.isDeleted);
        if (clearCartLinesButton) {
            clearCartLinesButton.disabled = !hasLines;
        }

        actionGrid.classList.remove('is-empty', 'is-due', 'is-paid', 'is-change');

        if (!draft) {
            actionGrid.classList.add('is-empty');
            return;
        }

        const paid = getDraftPaidValue(draft);
        const balance = getDraftBalanceValue(draft);
        const change = getDraftChangeValue(draft);

        if (paid <= 0 && balance > 0) {
            actionGrid.classList.add('is-empty');
            return;
        }

        if (balance > 0) {
            actionGrid.classList.add('is-due');
            return;
        }

        if (change > 0) {
            actionGrid.classList.add('is-change');
            return;
        }

        actionGrid.classList.add('is-paid');
    }
    function normalizePaymentMethod(method) {
        const raw = (method || '').toString().trim().toLowerCase();

        if (raw === 'cash' || raw === '0') {
            return {
                key: 'cash',
                label: 'Tiền mặt',
                badgeClass: 'bg-success-subtle text-success border border-success-subtle'
            };
        }

        if (raw === 'banktransfer' || raw === 'bank_transfer' || raw === 'transfer' || raw === '1') {
            return {
                key: 'banktransfer',
                label: 'Chuyển khoản',
                badgeClass: 'bg-primary-subtle text-primary border border-primary-subtle'
            };
        }

        if (raw === 'card' || raw === '2') {
            return {
                key: 'card',
                label: 'Thẻ',
                badgeClass: 'bg-info-subtle text-info border border-info-subtle'
            };
        }

        if (raw === 'ewallet' || raw === 'wallet' || raw === '3') {
            return {
                key: 'ewallet',
                label: 'Ví điện tử',
                badgeClass: 'bg-warning-subtle text-warning border border-warning-subtle'
            };
        }

        return {
            key: raw || 'other',
            label: 'Thanh toán',
            badgeClass: 'bg-light text-dark border'
        };
    }

    function getPaymentDisplayTitle(x) {
        const meta = normalizePaymentMethod(x?.methodName || x?.method);

        if (meta.key === 'cash') return 'Thanh toán tiền mặt';
        if (meta.key === 'banktransfer') return 'Thanh toán chuyển khoản';
        if (meta.key === 'card') return 'Thanh toán thẻ';
        if (meta.key === 'ewallet') return 'Thanh toán ví điện tử';

        return 'Thanh toán';
    }

    function getPaymentSubLine1(x) {
        const meta = normalizePaymentMethod(x?.methodName || x?.method);
        const provider = (x?.provider || '').trim();
        const reference = (x?.reference || x?.referenceCode || '').trim();

        if (meta.key === 'cash') {
            return provider
                ? `Thu tiền mặt • ${escapeHtml(provider)}`
                : 'Thu tiền mặt';
        }

        if (provider && reference) {
            return `${escapeHtml(provider)} • Mã: ${escapeHtml(reference)}`;
        }

        if (provider) {
            return `${escapeHtml(provider)} • Chưa có mã tham chiếu`;
        }

        if (reference) {
            return `Mã: ${escapeHtml(reference)}`;
        }

        if (meta.key === 'banktransfer') return 'Thanh toán chuyển khoản';
        if (meta.key === 'card') return 'Thanh toán qua thẻ';
        if (meta.key === 'ewallet') return 'Thanh toán ví điện tử';

        return 'Thông tin thanh toán';
    }

    function getPaymentSubLine2(x) {
        const createdAt = x?.createdAt || x?.createdAtText || '';
        if (!createdAt) return '';

        try {
            const dt = new Date(createdAt);
            if (Number.isNaN(dt.getTime())) return '';
            return dt.toLocaleString('vi-VN');
        } catch {
            return '';
        }
    }

    function renderPaymentCard(x, options) {
        const meta = normalizePaymentMethod(x?.methodName || x?.method);
        const title = getPaymentDisplayTitle(x);
        const subLine1 = getPaymentSubLine1(x);
        const subLine2 = getPaymentSubLine2(x);
        const amount = Number(x?.amount || 0);
        const showRemove = options?.showRemove === true;

        return `
        <div class="pos-payment-ledger-item">
            <div class="pos-payment-ledger-item__main">
                <div class="pos-payment-ledger-item__top">
                    <div class="pos-payment-ledger-item__title-wrap">
                        <div class="pos-payment-ledger-item__title">
                            ${escapeHtml(title)}
                        </div>

                        <span class="pos-payment-ledger-item__badge ${meta.key}">
                            ${escapeHtml(meta.label)}
                        </span>
                    </div>

                    <div class="pos-payment-ledger-item__amount">
                        ${formatMoney(amount)}
                    </div>
                </div>

                <div class="pos-payment-ledger-item__meta">
                    <div class="pos-payment-ledger-item__meta-line">
                        ${subLine1}
                    </div>
                    ${subLine2 ? `
                        <div class="pos-payment-ledger-item__meta-line">
                            ${escapeHtml(subLine2)}
                        </div>
                    ` : ''}
                </div>
            </div>

            ${showRemove ? `
                <div class="pos-payment-ledger-item__actions">
                    <button type="button"
        class="btn btn-sm btn-outline-danger pos-payment-ledger-item__remove-btn"
        data-remove-payment-id="${x.paymentId}"
        data-payment-method="${escapeHtml(x?.methodName || x?.method || '')}"
        data-payment-reference="${escapeHtml(x?.reference || x?.referenceCode || '')}"
        data-payment-provider="${escapeHtml(x?.provider || '')}">
    Xóa
</button>
                </div>
            ` : ''}
        </div>
    `;
    }

    function renderPayments(payments) {
        const paymentListBox = document.getElementById('paymentListBox');
        if (!paymentListBox) return;

        if (!Array.isArray(payments) || payments.length === 0) {
            paymentListBox.innerHTML = '';
            return;
        }

        paymentListBox.innerHTML = `
            <div class="fw-bold mb-2">Thanh toán đã ghi nhận</div>
            <div class="d-grid gap-2">
                ${payments.map(x => renderPaymentCard(x, { showRemove: true })).join('')}
            </div>
        `;
    }

    function renderPaymentModalDraft(draft) {
        const paySumSubtotal = document.getElementById('paySumSubtotal');
        const paySumDiscount = document.getElementById('paySumDiscount');
        const paySumGrandTotal = document.getElementById('paySumGrandTotal');
        const paySumPaid = document.getElementById('paySumPaid');
        const paySumBalance = document.getElementById('paySumBalance');
        const paySumChange = document.getElementById('paySumChange');
        const paymentModalListBox = document.getElementById('paymentModalListBox');

        if (paySumSubtotal) paySumSubtotal.textContent = formatMoney(draft?.subtotal || 0);
        if (paySumDiscount) paySumDiscount.textContent = formatMoney(draft?.discountTotal || 0);
        if (paySumGrandTotal) paySumGrandTotal.textContent = formatMoney(draft?.grandTotal || 0);
        if (paySumPaid) paySumPaid.textContent = formatMoney(getDraftPaidValue(draft));
        if (paySumBalance) paySumBalance.textContent = formatMoney(getDraftBalanceValue(draft));
        if (paySumChange) paySumChange.textContent = formatMoney(getDraftChangeValue(draft));
        const payExactAmount = document.getElementById('payExactAmount');
        if (payExactAmount) {
            payExactAmount.textContent = formatMoney(getDraftBalanceValue(draft));
        }

        const btnPayExact = document.getElementById('btnPayExact');
        if (btnPayExact) {
            btnPayExact.setAttribute('data-pay-amount', String(getDraftBalanceValue(draft)));
        }

        if (!paymentModalListBox) return;

        const payments = draft?.payments || [];
        if (!payments.length) {
            paymentModalListBox.innerHTML = `<div class="text-muted">Chưa có thanh toán.</div>`;
            return;
        }

        paymentModalListBox.innerHTML = `
            <div class="d-grid gap-2">
                ${payments.map(x => renderPaymentCard(x, { showRemove: true })).join('')}
            </div>
        `;
    }

    function renderPaymentPreview(posState) {
        const payAmount = document.getElementById('payAmount');
        const payPreviewBox = document.getElementById('payPreviewBox');
        const payPreviewStateText = document.getElementById('payPreviewStateText');
        const payPreviewBalance = document.getElementById('payPreviewBalance');
        const payPreviewChange = document.getElementById('payPreviewChange');

        const payFooter = document.querySelector('.pos-payment-footer');
        const payFooterStateText = document.getElementById('payFooterStateText');

        if (!payAmount || !payPreviewBalance || !payPreviewChange) return;

        const draft =
            posState?.business?.currentDraft ||
            posState?.currentDraft ||
            null;

        const currentInput = window.PosCommon.parseMoneyInput(payAmount.value);

        function setPreviewState(state, text) {
            if (!payPreviewBox) return;

            payPreviewBox.classList.remove(
                'pay-preview-box--empty',
                'pay-preview-box--due',
                'pay-preview-box--paid',
                'pay-preview-box--change',
                'pay-preview-box--warning'
            );

            payPreviewBox.classList.add(state);

            if (payPreviewStateText) {
                payPreviewStateText.textContent = text || '';
            }
        }

        function setFooterState(state, text) {
            if (!payFooter) return;

            payFooter.classList.remove('is-due', 'is-paid', 'is-change');

            if (state) {
                payFooter.classList.add(state);
            }

            if (payFooterStateText) {
                payFooterStateText.textContent = text || '';
            }
        }

        if (!draft) {
            payPreviewBalance.textContent = '0';
            payPreviewChange.textContent = '0';
            setPreviewState('pay-preview-box--empty', 'Chưa có giỏ hiện tại');
            setFooterState('', 'Chưa có giỏ hiện tại.');
            return;
        }

        const balanceDue = getDraftBalanceValue(draft);
        const expectedChange = Math.max(currentInput - balanceDue, 0);
        const remainingAfterInput = Math.max(balanceDue - currentInput, 0);

        payPreviewBalance.textContent = formatMoney(remainingAfterInput);
        payPreviewChange.textContent = formatMoney(expectedChange);

        if (currentInput <= 0) {
            setPreviewState('pay-preview-box--empty', 'Chưa nhập số tiền');
            setFooterState('is-due', 'Nhập số tiền hoặc bấm Thu đủ để tiếp tục.');
            return;
        }

        if (remainingAfterInput > 0) {
            setPreviewState('pay-preview-box--due', 'Cần thu thêm');
            setFooterState('is-due', 'Số tiền hiện tại chưa đủ để chốt đơn.');
            return;
        }

        if (expectedChange > 0) {
            if (Number(document.getElementById('payMethod')?.value) === 1) {
                setPreviewState('pay-preview-box--warning',
                    `Chuyển khoản dư ${formatMoney(expectedChange)} đ. Vẫn ghi nhận đủ ${formatMoney(currentInput)} đ.`);
                return;
            }
            setPreviewState('pay-preview-box--change', 'Khách đưa dư tiền');
            setFooterState('is-change', 'Đã đủ tiền. Có thể chốt đơn và trả lại tiền thừa.');
            return;
        }

        setPreviewState('pay-preview-box--paid', 'Đã nhập đúng số tiền');
        setFooterState('is-paid', 'Đã đủ tiền. Có thể chốt đơn.');
    }

    /* =========================================================
       4. CUSTOMER BOX
    ========================================================= */
    function renderCustomerInfo(draft) {
        const customerInfoBox =
            document.getElementById(
                'customerInfoBox'
            );

        if (!customerInfoBox) {
            return;
        }

        const customer =
            getDraftCustomerInfo(draft);

        const priceTier =
            String(
                customer.priceTier || ''
            )
                .trim()
                .toUpperCase();

        const isWholesale =
            priceTier === 'WHOLESALE';

        const reward =
            draft?.rewardSummary ||
            draft?.RewardSummary ||
            null;

        /*
         * Không có customer:
         * giữ rất compact, không dựng empty-card lớn.
         * Customer selection vẫn qua @ Khách / command center.
         */
        if (
            !draft ||
            !customer.customerId
        ) {
            customerInfoBox.innerHTML = `
            <div class="pos-customer-empty">
                <div class="pos-customer-empty__identity">
                    <div class="pos-customer-empty__name">
                        Khách lẻ
                    </div>

                    <div class="pos-customer-empty__hint">
                        Dùng <strong>@ Khách</strong>
                        để chọn khách hàng và sử dụng
                        điểm thưởng / voucher.
                    </div>
                </div>
            </div>
        `;

            return;
        }

        const availablePoints =
            Number(
                reward?.availablePoints ||
                reward?.AvailablePoints ||
                0
            );

        const redeemableVoucherCount =
            Number(
                reward?.redeemableVoucherCount ||
                reward?.RedeemableVoucherCount ||
                0
            );

        const availableVoucherCount =
            Number(
                reward?.availableVoucherCount ||
                reward?.AvailableVoucherCount ||
                0
            );

        const availableVoucherValue =
            Number(
                reward?.availableVoucherValue ||
                reward?.AvailableVoucherValue ||
                0
            );

        const tierBadge =
            isWholesale
                ? `
                <span class="pos-customer-tier
                             pos-customer-tier--wholesale">
                    KHÁCH SỈ
                </span>
              `
                : `
                <span class="pos-customer-tier
                             pos-customer-tier--retail">
                    KHÁCH LẺ
                </span>
              `;

        const voucherValueText =
            availableVoucherValue > 0
                ? formatMoney(
                    availableVoucherValue
                )
                : 'Chưa có giá trị';

        const redeemText =
            redeemableVoucherCount > 0
                ? `Đổi được ${redeemableVoucherCount} phiếu`
                : 'Chưa đủ điểm đổi phiếu';

        const voucherText =
            availableVoucherCount > 0
                ? `${availableVoucherCount} phiếu khả dụng`
                : 'Chưa có voucher';

        customerInfoBox.innerHTML = `
        <div class="pos-customer-compact">

            <div class="pos-customer-identity">

                <div class="pos-customer-identity__main">

                    <div class="pos-customer-identity__name">
                        ${escapeHtml(
            customer.name ||
            'Khách hàng'
        )}
                    </div>

                    ${tierBadge}

                </div>

                ${customer.phone
                ? `
                        <div class="pos-customer-identity__phone">
                            ${escapeHtml(customer.phone)}
                        </div>
                        `
                : ''
            }

            </div>

            <div class="pos-customer-benefit-grid">

                <button type="button"
                        class="pos-customer-benefit-card
                               pos-customer-benefit-card--points"
                        data-customer-action="reward"
                        title="Xem và đổi điểm thưởng">

                    <div class="pos-customer-benefit-card__icon">
                        <i class="bx bx-star"></i>
                    </div>

                    <div class="pos-customer-benefit-card__content">

                        <div class="pos-customer-benefit-card__value">
                            ${formatMoney(
                availablePoints
            )}
                        </div>

                        <div class="pos-customer-benefit-card__label">
                            điểm
                        </div>

                        <div class="pos-customer-benefit-card__meta">
                            ${escapeHtml(redeemText)}
                        </div>

                    </div>

                </button>

                <button type="button"
                        class="pos-customer-benefit-card
                               pos-customer-benefit-card--voucher"
                        data-customer-action="use-voucher"
                        title="Xem voucher khả dụng">

                    <div class="pos-customer-benefit-card__icon">
                        <i class="bx bx-purchase-tag-alt"></i>
                    </div>

                    <div class="pos-customer-benefit-card__content">

                        <div class="pos-customer-benefit-card__value">
                            ${availableVoucherCount}
                        </div>

                        <div class="pos-customer-benefit-card__label">
                            voucher
                        </div>

                        <div class="pos-customer-benefit-card__meta">
                            ${escapeHtml(voucherText)}
                        </div>

                        ${availableVoucherValue > 0
                ? `
                                <div class="pos-customer-benefit-card__amount">
                                    ${voucherValueText}
                                </div>
                                `
                : ''
            }

                    </div>

                </button>

            </div>

            ${document.getElementById('posCustomerProfileAccess')?.dataset.allowed === 'true' && Number(customer.customerId)>0
                ? `<a class="pos-customer-profile-link" href="/admin/customers/${Number(customer.customerId)}/profile" target="_blank" rel="noopener" title="Xem lịch sử mua hàng, điểm và voucher trong tab mới">
                    <i class="bx bx-history" aria-hidden="true"></i>
                    <span>Lịch sử &amp; điểm</span>
                    <i class="bx bx-link-external" aria-hidden="true"></i>
                </a>`
                : ''}

            <div class="pos-customer-compact__footer">

                <span class="pos-customer-compact__hint">
                    Bấm vào Điểm hoặc Voucher để xem chi tiết.
                </span>

                <button type="button"
                        class="pos-customer-clear-link"
                        data-customer-action="clear">
                    <i class="bx bx-user-x"></i>
                    <span>Bỏ khách</span>
                </button>

            </div>

        </div>
    `;
    }
    function renderMetaPanelSummary(draft) {
        const noteEl = document.getElementById('metaOrderNoteSummary');
        const discountEl = document.getElementById('metaOrderDiscountSummary');

        const noteSection = document.querySelector('[data-meta-section="note"]');
        const discountSection = document.querySelector('[data-meta-section="discount"]');
        const customerBox = document.getElementById('customerInfoBox');

        let voucherBox = document.getElementById('metaVoucherDiscountSummaryBox');

        if (!voucherBox && discountSection) {
            voucherBox = document.createElement('div');
            voucherBox.id = 'metaVoucherDiscountSummaryBox';
            voucherBox.className = 'pos-applied-voucher-box';
            discountSection.insertAdjacentElement('afterend', voucherBox);
        }

        if (!draft) {
            if (noteEl) noteEl.textContent = 'Chưa có ghi chú';
            if (discountEl) discountEl.textContent = '0';

            noteSection?.classList.remove('has-note');
            discountSection?.classList.remove('has-discount');
            customerBox?.classList.remove('has-customer');

            if (voucherBox) {
                voucherBox.innerHTML = '';
                voucherBox.style.display = 'none';
            }

            return;
        }

        const note = (draft?.note || '').trim();

        if (noteEl) {
            noteEl.innerHTML = note
                ? `Đã có ghi chú <span class="pos-meta-badge pos-meta-badge-note">NOTE</span>`
                : 'Chưa có ghi chú';
        }

        if (noteSection) {
            noteSection.classList.toggle('has-note', !!note);
        }

        const discount = Number(draft?.orderDiscount || 0);

        if (discountEl) {
            discountEl.innerHTML = discount > 0
                ? `${formatMoney(discount)} <span class="pos-meta-badge pos-meta-badge-discount">SALE</span>`
                : '0';
        }

        if (discountSection) {
            discountSection.classList.toggle('has-discount', discount > 0);
        }

        const customer = draft?.customer?.customerId || draft?.customerId || 0;

        if (customerBox) {
            customerBox.classList.toggle('has-customer', !!customer);
        }

        const voucherTotal = Number(
            draft?.voucherDiscountTotal ||
            draft?.VoucherDiscountTotal ||
            0
        );

        const appliedVouchers =
            draft?.appliedRewardVouchers ||
            draft?.AppliedRewardVouchers ||
            [];

        if (voucherBox) {
            if (voucherTotal > 0) {
                voucherBox.innerHTML = `
                <div class="fw-bold mb-1">
                    Voucher đã áp dụng
                </div>

                ${(Array.isArray(appliedVouchers) ? appliedVouchers : []).map(v => `
                    <div class="pos-applied-voucher-row">
                        <span>${escapeHtml(v.voucherCode || v.VoucherCode || '')}</span>
                        <strong>${formatMoney(v.value || v.Value || 0)}</strong>
                    </div>
                `).join('')}

                <div class="pos-applied-voucher-row border-top mt-1 pt-1">
                    <span>Tổng voucher</span>
                    <strong>${formatMoney(voucherTotal)}</strong>
                </div>
            `;

                voucherBox.style.display = '';
            } else {
                voucherBox.innerHTML = '';
                voucherBox.style.display = 'none';
            }
        }
    }

    /* =========================================================
       5. DRAFT LINE RENDER
    ========================================================= */
    function renderEmptyDraftRow(message) {
        return `
            <tr>
                <td colspan="5" class="text-center text-muted py-4">${escapeHtml(message || '')}</td>
            </tr>
        `;
    }

    function getDraftLineId(line) {
        return Number(line?.lineId || 0);
    }

    function getDraftLineRenderKey(line) {
        return JSON.stringify({
            lineId: Number(line?.lineId || 0),
            productName: line?.productName || line?.itemName || '',
            productVariantName: line?.productVariantName || '',
            imageUrl: line?.imageUrl || '',
            imageThumbUrl: line?.imageThumbUrl || '',
            imageAlt: line?.imageAlt || '',
            hasImage: !!line?.hasImage,
            sku: line?.sku || '',
            barcode: line?.barcode || '',
            sellingUnitName: line?.sellingUnitName || '',
            baseUnitName: line?.baseUnitName || '',
            quantity: Number(line?.quantity || 0),
            baseQuantity: Number(line?.baseQuantity || 0),
            multiplier: Number(line?.multiplier || 1),
            unitPrice: Number(line?.unitPrice || 0),
            originalUnitPrice: Number(line?.originalUnitPrice || line?.OriginalUnitPrice || 0),
            promotionDiscount: Number(line?.promotionDiscount || line?.PromotionDiscount || 0),
            promotionName: line?.promotionName || line?.PromotionName || '',
            lineDiscount: Number(line?.lineDiscount || 0),

            lineTotal: Number(line?.lineTotal || 0),
            comboPromotionId: Number(line?.comboPromotionId || line?.ComboPromotionId || 0),
            comboPromotionName: line?.comboPromotionName || line?.ComboPromotionName || '',
            comboPromotionNote: line?.comboPromotionNote || line?.ComboPromotionNote || '',
            comboAllocatedDiscount: Number(line?.comboAllocatedDiscount || line?.ComboAllocatedDiscount || 0),
            promotionType: line?.promotionType || line?.PromotionType || '',
            promotionBuyQuantity: Number(line?.promotionBuyQuantity || line?.PromotionBuyQuantity || 0),
            promotionGiftQuantity: Number(line?.promotionGiftQuantity || line?.PromotionGiftQuantity || 0)

        });
    }

    function buildDraftLineRenderMap(lines) {
        const map = new Map();

        (Array.isArray(lines) ? lines : []).forEach((line, index) => {
            const lineId = getDraftLineId(line);
            if (!lineId) return;

            map.set(lineId, {
                lineId,
                index,
                renderKey: getDraftLineRenderKey(line)
            });
        });

        return map;
    }
    function findTouchedLineId(prevDraft, nextDraft) {
        const prevLines = Array.isArray(prevDraft?.lines) ? prevDraft.lines : [];
        const nextLines = Array.isArray(nextDraft?.lines) ? nextDraft.lines : [];

        if (!nextLines.length) return 0;

        const prevMap = buildDraftLineRenderMap(prevLines);

        for (const line of nextLines) {
            const lineId = getDraftLineId(line);
            if (!lineId) continue;

            const prevEntry = prevMap.get(lineId);
            const nextRenderKey = getDraftLineRenderKey(line);

            // Dòng mới thêm
            if (!prevEntry) {
                return lineId;
            }

            // Dòng cũ nhưng số lượng/thành tiền/giảm giá thay đổi
            if (prevEntry.renderKey !== nextRenderKey) {
                return lineId;
            }
        }

        return 0;
    }
    function areDraftLinesEquivalent(prevLines, nextLines) {

        const prevList = Array.isArray(prevLines) ? prevLines : [];
        const nextList = Array.isArray(nextLines) ? nextLines : [];

        if (prevList.length !== nextList.length) {
            return false;
        }

        const prevMap = buildDraftLineRenderMap(prevList);
        const nextMap = buildDraftLineRenderMap(nextList);

        if (prevMap.size !== nextMap.size) {
            return false;
        }

        for (const [lineId, prevEntry] of prevMap.entries()) {
            const nextEntry = nextMap.get(lineId);

            if (!nextEntry) {
                return false;
            }

            if (prevEntry.index !== nextEntry.index) {
                return false;
            }

            if (prevEntry.renderKey !== nextEntry.renderKey) {
                return false;
            }
        }

        return true;
    }

    function buildDraftLineRowHtml(line, index) {
        const displayName =
            buildVariantDisplayName(
                line.productName || line.itemName || '',
                line.productVariantName || ''
            ) ||
            line.itemName ||
            'Sản phẩm';

        const lineId = Number(line.lineId || 0);
        const quantity = Number(line.quantity || 0);
        const unitPrice = Number(line.unitPrice || 0);
        const lineTotal = Number(line.lineTotal || 0);
        const lineDiscount = Number(line.lineDiscount || 0);

        const isPromotionGift =
            line.isPromotionGift === true ||
            line.IsPromotionGift === true;

        const giftPromotionNote = String(
            line.giftPromotionNote ??
            line.GiftPromotionNote ??
            ''
        ).trim();

        const giftPromotionName = String(
            line.giftPromotionName ??
            line.GiftPromotionName ??
            ''
        ).trim();

        const promotionDiscount = Number(
            line.promotionDiscount ??
            line.PromotionDiscount ??
            0
        );

        const originalUnitPriceRaw = Number(
            line.originalUnitPrice ??
            line.OriginalUnitPrice ??
            0
        );

        const originalUnitPrice =
            originalUnitPriceRaw > 0
                ? originalUnitPriceRaw
                : unitPrice;

        const promotionName = String(
            line.promotionName ??
            line.PromotionName ??
            ''
        ).trim();

        const promotionType = Number(
            line.promotionType ??
            line.PromotionType ??
            0
        );

        const promotionGiftQuantity = Number(
            line.promotionGiftQuantity ??
            line.PromotionGiftQuantity ??
            0
        );

        const hasPromotion =
            promotionDiscount > 0 &&
            originalUnitPrice > 0 &&
            quantity > 0;

        const isProductDiscountPromotion =
            hasPromotion &&
            (promotionType === 1 || promotionType === 2);

        const isBuyXGetYPromotion =
            hasPromotion &&
            promotionType === 3;

        const roundVnd = function (value) {
            return Math.round(Number(value || 0));
        };

        const displayOriginalUnitPrice =
            roundVnd(originalUnitPrice);

        const promotionUnitPrice =
            hasPromotion
                ? roundVnd(
                    Math.max(
                        originalUnitPrice -
                        (promotionDiscount / quantity),
                        0
                    )
                )
                : roundVnd(unitPrice);

        const savePerUnit =
            hasPromotion
                ? roundVnd(
                    Math.max(
                        displayOriginalUnitPrice -
                        promotionUnitPrice,
                        0
                    )
                )
                : 0;

        const hasLineDiscount =
            lineDiscount > 0;

        const qtyText =
            line.sellingUnitName
                ? `${formatMoney(quantity)} ${escapeHtml(line.sellingUnitName)}`
                : '';

        const baseQtyText =
            Number(line.multiplier || 1) > 1 &&
                line.baseUnitName
                ? `${formatMoney(
                    Number(line.baseQuantity || 0)
                )} ${escapeHtml(line.baseUnitName)}`
                : '';

        const promoLabel =
            promotionName ||
            'Khuyến mãi sản phẩm';

        const comboPromotionName = String(
            line.comboPromotionName ??
            line.ComboPromotionName ??
            ''
        ).trim();

        const comboPromotionNote = String(
            line.comboPromotionNote ??
            line.ComboPromotionNote ??
            ''
        ).trim();

        const comboAllocatedDiscount = Number(
            line.comboAllocatedDiscount ??
            line.ComboAllocatedDiscount ??
            0
        );

        const hasComboPromotion =
            promotionType !== 2 &&
            (comboAllocatedDiscount > 0 || comboPromotionName.length > 0);

        const unitNameForGift =
            line.sellingUnitName ||
            'sản phẩm';

        return `
    <tr data-line-id="${lineId}"
        data-render-key="${escapeHtml(
            getDraftLineRenderKey(line)
        )}"
        data-row-index="${index}"
        class="pos-line-row
            ${isPromotionGift ? 'is-promotion-gift' : ''}
            ${hasLineDiscount ? 'has-line-discount' : ''}
            ${hasPromotion ? 'has-promotion' : ''}">

        <td class="pos-line-name-cell">
            <div class="pos-line-name-wrap">
                <div class="pos-line-product-wrap">

                    ${line?.hasImage &&
                (
                    line?.imageThumbUrl ||
                    line?.imageUrl
                )
                ? `
                        <div class="pos-line-thumb-wrap"
                             title="Xem ảnh"
                             data-image-url="${escapeHtml(
                    line.imageUrl ||
                    line.imageThumbUrl ||
                    ''
                )}">

                            <img class="pos-line-thumb"
                                 src="${escapeHtml(
                    line.imageThumbUrl ||
                    line.imageUrl ||
                    ''
                )}"
                                 alt="${escapeHtml(
                    line.imageAlt ||
                    displayName
                )}"
                                 loading="lazy" />
                        </div>
                        `
                : `
                        <div class="pos-line-thumb-placeholder">
                            IMG
                        </div>
                        `
            }

                    <div class="pos-line-name-main">
                    ${
            isPromotionGift
                ? `
        <div class="pos-gift-relation">
            <i class="bx bx-subdirectory-right"
               aria-hidden="true"></i>

            <span>
                Tặng kèm khuyến mãi
            </span>
        </div>
        `
                : ''
}

                        <div class="pos-line-name">
                            ${escapeHtml(displayName)}

                            ${isPromotionGift
                ? `
                                    <span class="pos-gift-inline-label">
                                        Hàng tặng
                                    </span>
                                    `
                : ''
            }
                        </div>

                        <div class="pos-line-submeta">
                            ${buildCompactLineMeta(line)}
                        </div>

                        ${hasLineDiscount
                ? `
                            <div class="pos-line-adjustment-note">
                                <i class="bx bx-purchase-tag-alt"></i>
                                <span>Giảm dòng</span>
                                <strong>
                                    ${formatMoney(lineDiscount)}
                                </strong>
                            </div>
                            `
                : ''
            }

                        ${isPromotionGift
                ? `
                            <div class="pos-gift-badge mt-1">
                                🎁 Hàng tặng khuyến mãi
                            </div>

                            <div class="pos-gift-note">
                                ${escapeHtml(
                    giftPromotionNote ||
                    giftPromotionName ||
                    'Hàng tặng từ chương trình khuyến mãi'
                )}
                            </div>
                            `
                : ''
            }

                        ${hasComboPromotion
                ? `
                            <div class="pos-line-combo-badge"
                                 title="${escapeHtml(
                    comboPromotionNote ||
                    comboPromotionName
                )}">
                                🎁 ${escapeHtml(
                    comboPromotionName ||
                    'Combo khuyến mãi'
                )}
                            </div>
                            `
                : ''
            }

                    </div>
                </div>
            </div>
        </td>

        <td class="pos-line-qty-cell">

            <div class="pos-qty-inline">

                <button type="button"
                        class="btn btn-sm btn-outline-secondary qty-btn"
                        data-dec-line-id="${lineId}"
                        title="Giảm số lượng"
                        aria-label="Giảm số lượng"
                        ${isPromotionGift ? 'disabled' : ''}>
                    −
                </button>

                <input type="number"
                       class="form-control form-control-sm qty-input"
                       min="1"
                       step="1"
                       value="${quantity}"
                       data-qty-line-id="${lineId}"
                       aria-label="Số lượng"
                       ${isPromotionGift
                ? 'disabled readonly'
                : ''
            } />

                <button type="button"
                        class="btn btn-sm btn-outline-secondary qty-btn"
                        data-inc-line-id="${lineId}"
                        title="Tăng số lượng"
                        aria-label="Tăng số lượng"
                        ${isPromotionGift ? 'disabled' : ''}>
                    +
                </button>

            </div>

            ${qtyText || baseQtyText
                ? `
                <div class="pos-qty-summary">

                    ${qtyText
                    ? `<span>${qtyText}</span>`
                    : ''
                }

                    ${baseQtyText
                    ? `
                            <span class="pos-qty-base">
                                · ${baseQtyText}
                            </span>
                            `
                    : ''
                }

                </div>
                `
                : ''
            }

        </td>

        <td class="pos-line-money-cell
                   pos-line-money-cell--center">

            ${isProductDiscountPromotion
                ? `
                <div class="pos-line-price-stack">

                    <div class="pos-line-original-price">
                        ${formatMoney(
                    displayOriginalUnitPrice
                )}
                    </div>

                    <div class="pos-line-promo-price">
                        ${formatMoney(
                    promotionUnitPrice
                )}
                    </div>

                    <div class="pos-line-promo-badge"
                         title="${escapeHtml(
                    promotionType === 2 ? (comboPromotionNote || promoLabel) : promoLabel
                )}">
                        <i class="bx bx-purchase-tag"></i>
                        <span>
                            ${escapeHtml(promoLabel)}
                        </span>
                    </div>

                    ${savePerUnit > 0
                    ? `
                        <div class="pos-line-promo-save">
                            Tiết kiệm
                            <strong>
                                ${formatMoney(savePerUnit)}
                            </strong>/
                            ${escapeHtml(
                        line.sellingUnitName ||
                        'đv'
                    )}
                        </div>
                        `
                    : ''
                }

                </div>
                `
                : isBuyXGetYPromotion
                    ? `
                <div class="pos-line-price-stack
                            pos-line-price-stack--gift">

                    <div class="pos-line-money">
                        ${formatMoney(unitPrice)}
                    </div>

                    <div class="pos-line-gift-badge"
                         title="${escapeHtml(
                        promoLabel
                    )}">
                        🎁
                        <span>
                            ${escapeHtml(promoLabel)}
                        </span>
                    </div>

                    ${promotionGiftQuantity > 0
                        ? `
                        <div class="pos-line-gift-note">
                            Đã tặng
                            <strong>
                                ${formatMoney(
                            promotionGiftQuantity
                        )}
                            </strong>
                            ${escapeHtml(
                            unitNameForGift
                        )}
                        </div>
                        `
                        : ''
                    }

                </div>
                `
                    : `
                <div class="pos-line-money">
                    ${formatMoney(unitPrice)}
                </div>
                `
            }

        </td>

        <td class="pos-line-total-cell
                   pos-line-total-cell--center">

            <div class="pos-line-total">
                ${formatMoney(lineTotal)}
            </div>

        </td>

        <td class="pos-line-actions-cell">

            ${!isPromotionGift
                ? `
                <div class="dropdown
                            dropstart
                            pos-line-actions">

                    <button type="button"
                            class="btn
                                   pos-line-actions-toggle"
                            data-bs-toggle="dropdown"
                            data-bs-auto-close="true"
                            aria-expanded="false"
                            aria-label="Mở tác vụ dòng hàng"
                            title="Tác vụ dòng hàng">

                        <i class="bx
                                  bx-dots-vertical-rounded">
                        </i>
                    </button>

                    <ul class="dropdown-menu
                               pos-line-actions-menu">

                        <li>
                            <button type="button"
                                    class="dropdown-item"
                                    data-price-line-id="${lineId}">

                                <i class="bx bx-purchase-tag"></i>
                                <span>Bảng giá</span>
                            </button>
                        </li>

                        <li>
                            <button type="button"
                                    class="dropdown-item"
                                    data-line-discount-id="${lineId}"
                                    data-line-discount-amount="${lineDiscount}"
                                    data-line-name="${escapeHtml(
                    displayName
                )}">

                                <i class="bx bx-discount"></i>
                                <span>Giảm giá dòng</span>
                            </button>
                        </li>

                        <li>
                            <button type="button"
                                    class="dropdown-item"
                                    data-open-qty-line-id="${lineId}">

                                <i class="bx bx-edit-alt"></i>
                                <span>Sửa số lượng</span>
                            </button>
                        </li>

                        <li>
                            <hr class="dropdown-divider" />
                        </li>

                        <li>
                            <button type="button"
                                    class="dropdown-item text-danger"
                                    data-remove-line-id="${lineId}">

                                <i class="bx bx-trash"></i>
                                <span>Xóa sản phẩm</span>
                            </button>
                        </li>

                    </ul>
                </div>
                `
                : `
                <span class="pos-gift-lock-text">
                    Tự động
                </span>
                `
            }

        </td>
    </tr>
`;
    }

    function buildDraftLinesHtml(lines) {
        if (!Array.isArray(lines) || lines.length === 0) {
            return renderEmptyDraftRow('Giỏ hiện tại chưa có sản phẩm.');
        }

        return lines.map((line, index) => buildDraftLineRowHtml(line, index)).join('');
    }

    function renderCurrentDraftLines(draft) {
        const currentDraftBody = document.getElementById('currentDraftBody');
        if (!currentDraftBody) return;

        if (!draft) {
            currentDraftBody.innerHTML = renderEmptyDraftRow('Chưa có giỏ hiện tại.');
            return;
        }

        currentDraftBody.innerHTML = buildDraftLinesHtml(draft.lines || []);
    }

    function renderCurrentDraftTable(draft) {
        renderCurrentDraftLines(draft);
    }
    function highlightTouchedLine(lineId, options) {
        const id = Number(lineId || 0);
        if (!id) return;

        const row = document.querySelector(`tr[data-line-id="${id}"]`);
        if (!row) return;

        window.__posLastTouchedLineId = id;

        row.classList.remove('pos-line-just-added');

        // ép browser restart animation
        void row.offsetWidth;

        row.classList.add('pos-line-just-added');

        if (options?.scroll !== false) {
            row.scrollIntoView({
                behavior: 'smooth',
                block: 'center'
            });
        }

        setTimeout(function () {
            row.classList.remove('pos-line-just-added');
        }, 5000);
    }
    /* =========================================================
       6. ROW-LEVEL PATCH (3C)
    ========================================================= */
    function createRowFromHtml(html) {
        const tempBody = document.createElement('tbody');
        tempBody.innerHTML = (html || '').trim();
        return tempBody.firstElementChild || null;
    }

    function getCurrentDraftBody() {
        return document.getElementById('currentDraftBody');
    }

    function getRenderedRowByLineId(lineId) {
        const currentDraftBody = getCurrentDraftBody();
        if (!currentDraftBody) return null;

        return currentDraftBody.querySelector(`tr[data-line-id="${Number(lineId || 0)}"]`);
    }

    function getRenderedRowLineId(row) {
        if (!row) return 0;
        return Number(row.getAttribute('data-line-id') || 0);
    }

    function isEmptyDraftRow(row) {
        if (!row) return false;
        return !row.hasAttribute('data-line-id');
    }

    function createDraftLineRowElement(line, index) {
        return createRowFromHtml(buildDraftLineRowHtml(line, index));
    }

    function replaceRowIfNeeded(existingRow, line, index, nextRenderKey) {
        if (!existingRow) return null;

        const currentRenderKey = existingRow.getAttribute('data-render-key') || '';
        const currentIndex = Number(existingRow.getAttribute('data-row-index') || -1);

        if (currentRenderKey === nextRenderKey && currentIndex === index) {
            return existingRow;
        }

        const nextRow = createDraftLineRowElement(line, index);
        if (!nextRow) return existingRow;

        existingRow.replaceWith(nextRow);
        return nextRow;
    }

    function applyRowMeta(row, renderKey, index) {
        if (!row) return;
        row.setAttribute('data-render-key', renderKey || '');
        row.setAttribute('data-row-index', String(index));
    }

    function patchDraftLines(prevDraft, nextDraft) {
        const currentDraftBody = getCurrentDraftBody();
        if (!currentDraftBody) return;

        const prevLines = Array.isArray(prevDraft?.lines) ? prevDraft.lines : [];
        const nextLines = Array.isArray(nextDraft?.lines) ? nextDraft.lines : [];

        if (!nextDraft) {
            currentDraftBody.innerHTML = renderEmptyDraftRow('Chưa có giỏ hiện tại.');
            return;
        }

        if (!nextLines.length) {
            currentDraftBody.innerHTML = renderEmptyDraftRow('Giỏ hiện tại chưa có sản phẩm.');
            return;
        }

        const prevMap = buildDraftLineRenderMap(prevLines);
        const nextMap = buildDraftLineRenderMap(nextLines);

        Array.from(currentDraftBody.querySelectorAll('tr')).forEach(row => {
            if (isEmptyDraftRow(row)) {
                row.remove();
            }
        });

        Array.from(currentDraftBody.querySelectorAll('tr[data-line-id]')).forEach(row => {
            const lineId = getRenderedRowLineId(row);
            if (!nextMap.has(lineId)) {
                row.remove();
            }
        });

        let previousRow = null;

        nextLines.forEach((line, index) => {
            const lineId = getDraftLineId(line);
            const nextEntry = nextMap.get(lineId);
            if (!nextEntry) return;

            let row = getRenderedRowByLineId(lineId);

            if (!row) {
                row = createDraftLineRowElement(line, index);
                if (!row) return;

                applyRowMeta(row, nextEntry.renderKey, index);

                if (!previousRow) {
                    currentDraftBody.prepend(row);
                } else {
                    previousRow.insertAdjacentElement('afterend', row);
                }

                previousRow = row;
                return;
            }

            row = replaceRowIfNeeded(row, line, index, nextEntry.renderKey);
            applyRowMeta(row, nextEntry.renderKey, index);

            if (!previousRow) {
                if (currentDraftBody.firstElementChild !== row) {
                    currentDraftBody.prepend(row);
                }
            } else {
                const expectedPreviousSibling = previousRow.nextElementSibling;
                if (expectedPreviousSibling !== row) {
                    previousRow.insertAdjacentElement('afterend', row);
                }
            }

            previousRow = row;
        });

        if (!currentDraftBody.querySelector('tr[data-line-id]')) {
            currentDraftBody.innerHTML = buildDraftLinesHtml(nextLines);
        }

       
    }
    function buildLinePriceTableHtml(line) {
        const prices = Array.isArray(line?.unitPrices || line?.UnitPrices)
            ? (line.unitPrices || line.UnitPrices)
            : [];

        const title = line?.productVariantName || line?.itemName || 'Bảng giá sản phẩm';
        const currentUnitName = line?.sellingUnitName || line?.unitName || '';
        const quantity = Number(line?.quantity || 0);
        const currentUnitPrice = Number(line?.unitPrice || 0);
        const lineTotal = Number(line?.lineTotal || 0);

        if (!prices.length) {
            return `
            <div class="pos-price-modal-empty">
                Chưa có bảng giá đơn vị cho sản phẩm này.
            </div>
        `;
        }

        return `
        <div class="pos-price-modal">
            <div class="pos-price-modal-head">
                <div>
                    <div class="pos-price-modal-title">${escapeHtml(title)}</div>
                 <div class="pos-price-current-box">
    <div>
        <span>SL hiện tại</span>
        <strong>${formatMoney(quantity)} ${escapeHtml(currentUnitName)}</strong>
    </div>
    <div>
        <span>Đơn giá</span>
        <strong>${formatMoney(currentUnitPrice)}</strong>
    </div>
    <div>
        <span>Thành tiền</span>
        <strong>${formatMoney(lineTotal)}</strong>
    </div>
</div>
                </div>
            </div>

            <div class="table-responsive">
                <table class="table table-bordered align-middle pos-price-modal-table">
                    <thead>
                        <tr>
                            <th>Đơn vị</th>
                            <th>Quy đổi</th>
                            <th>Giá lẻ</th>
                            <th>Giá sỉ</th>
                            <th>Giá áp dụng</th>
                         
                        </tr>
                    </thead>
                    <tbody>
                        ${prices.map(function (x) {
            const unitName = x.unitName || x.UnitName || '';
            const factor = Number(x.factor || x.Factor || 1);
            const retailPrice = Number(x.retailPrice ?? x.RetailPrice ?? 0);
            const wholesalePrice = Number(x.wholesalePrice ?? x.WholesalePrice ?? 0);
            const effectivePrice = Number(x.effectivePrice ?? x.EffectivePrice ?? 0);
            const isBaseUnit = !!(x.isBaseUnit || x.IsBaseUnit);
                            const isEffectivePriceUnit = !!(
                                x.isEffectivePriceUnit ||
                                x.IsEffectivePriceUnit
                            );
                            const isCurrentUnit = isEffectivePriceUnit;

            return `
                                <tr class="${isCurrentUnit ? 'is-current' : ''}">
                                    <td>
                                        <div class="fw-bold">${escapeHtml(unitName)}</div>
                                    
                                        ${isBaseUnit ? '<span class="badge bg-info text-dark">Đơn vị gốc</span>' : ''}
                                       ${isEffectivePriceUnit
                    ? '<span class="badge bg-success ms-1">Giá đang áp dụng</span>'
                    : ''}

                                    </td>
                                    <td>x${formatMoney(factor)}</td>
                                    <td>${retailPrice > 0 ? formatMoney(retailPrice) : '—'}</td>
                                    <td>${wholesalePrice > 0 ? formatMoney(wholesalePrice) : '—'}</td>
                                    <td class="fw-bold text-primary">${effectivePrice > 0 ? formatMoney(effectivePrice) : '—'}</td>
                                 
                                </tr>
                            `;
        }).join('')}
                    </tbody>
                </table>
            </div>
        </div>
    `;
    }
    /* =========================================================
       7. FORM / SECTION RENDER
    ========================================================= */
    function renderDraftFormFields(draft) {
        const txtOrderNote = document.getElementById('txtOrderNote');
        const txtOrderDiscount = document.getElementById('txtOrderDiscount');

        if (!draft) {
            if (txtOrderNote) txtOrderNote.value = '';
            if (txtOrderDiscount) txtOrderDiscount.value = '';
            return;
        }

        if (txtOrderNote) txtOrderNote.value = draft.note || '';
        if (txtOrderDiscount) txtOrderDiscount.value = draft.orderDiscount || 0;
    }

    function renderDraftSummaryPanels(draft, posState) {
        renderSummary(draft);
        renderSummaryState(draft);
        renderSummaryActionState(draft);
        renderPayments(draft?.payments || []);
        renderPaymentModalDraft(draft);
        renderPaymentPreview(posState);
    }

    function renderDraftSections(draft, posState) {
        renderCurrentDraftTable(draft);
        renderDraftFormFields(draft);
        renderDraftSummaryPanels(draft, posState);
        renderCustomerInfo(draft);
        renderMetaPanelSummary(draft);
        renderCurrentBadge({ currentOrderId: draft?.orderId || null }, draft);
    }

    /* =========================================================
       8. HELD ORDER LIST
    ========================================================= */
    function renderHeldList(items) {
        const heldList = document.getElementById('heldList');
        const currentShiftList = document.getElementById('heldOrdersCurrentShiftList');
        const otherShiftList = document.getElementById('heldOrdersOtherShiftList');

        if (!heldList) return;

        const list = Array.isArray(items) ? items : [];

        // =========================================================
        // BƯỚC 4.3:
        // Tách đúng đơn giữ ca hiện tại / ca khác từ dữ liệu backend
        // =========================================================
        function sortHeldOrders(arr) {
            return arr.slice().sort(function (a, b) {
                const timeA = new Date(a?.heldAtUtc || 0).getTime();
                const timeB = new Date(b?.heldAtUtc || 0).getTime();

                // 1. Mới hơn lên trước
                if (timeA !== timeB) {
                    return timeB - timeA;
                }

                const totalA = Number(a?.subtotal || 0);
                const totalB = Number(b?.subtotal || 0);

                // 2. Giá trị cao hơn lên trước
                return totalB - totalA;
            });
        }

        const currentShiftOrders = sortHeldOrders(
            list.filter(x => x?.isCurrentShift === true)
        );

        const otherShiftOrders = sortHeldOrders(
            list.filter(x => x?.isCurrentShift !== true)
        );

        const totalCount = list.length;

        function formatHeldAt(value) {
            if (!value) return '';

            try {
                const dt = new Date(value);
                if (Number.isNaN(dt.getTime())) return '';
                return dt.toLocaleString('vi-VN');
            } catch {
                return '';
            }
        }
        function getHeldAgeInfo(value) {
            if (!value) {
                return {
                    minutes: 0,
                    text: '',
                    badgeClass: 'pos-held-age-badge pos-held-age-badge--new',
                    label: 'Mới'
                };
            }

            try {
                const dt = new Date(value);
                if (Number.isNaN(dt.getTime())) {
                    return {
                        minutes: 0,
                        text: '',
                        badgeClass: 'pos-held-age-badge pos-held-age-badge--new',
                        label: 'Mới'
                    };
                }

                const diffMs = Date.now() - dt.getTime();
                const minutes = Math.max(0, Math.floor(diffMs / 60000));

                if (minutes < 5) {
                    return {
                        minutes,
                        text: minutes <= 0 ? 'Vừa giữ' : `${minutes} phút trước`,
                        badgeClass: 'pos-held-age-badge pos-held-age-badge--new',
                        label: 'Mới'
                    };
                }

                if (minutes < 30) {
                    return {
                        minutes,
                        text: `${minutes} phút trước`,
                        badgeClass: 'pos-held-age-badge pos-held-age-badge--waiting',
                        label: 'Chờ lâu'
                    };
                }

                const hours = Math.floor(minutes / 60);
                return {
                    minutes,
                    text: hours > 0 ? `${hours} giờ trước` : `${minutes} phút trước`,
                    badgeClass: 'pos-held-age-badge pos-held-age-badge--old',
                    label: 'Cũ'
                };
            } catch {
                return {
                    minutes: 0,
                    text: '',
                    badgeClass: 'pos-held-age-badge pos-held-age-badge--new',
                    label: 'Mới'
                };
            }
        }
        function renderHeldOrderCard(x, options) {
            const customerName = escapeHtml(x?.customerName || 'Khách lẻ');
            const customerPhone = escapeHtml(x?.customerPhone || '');
            const holdCode = escapeHtml(x?.holdCode || `#${x?.orderId || 0}`);
            const holdNote = escapeHtml(x?.holdNote || '');
            const lineCount = Number(x?.lineCount || 0);
            const totalQty = Number(x?.totalQuantity || 0);
            const subtotal = Number(x?.subtotal || 0);
            const heldAtText = formatHeldAt(x?.heldAtUtc);
            const heldAge = getHeldAgeInfo(x?.heldAtUtc);
            const isHighValue = subtotal >= 1000000;

            const shiftCode = escapeHtml(x?.shiftCode || '');
            const terminalId = escapeHtml(x?.terminalId || '');
            const terminalName = escapeHtml(x?.terminalName || '');
            const heldByUserName = escapeHtml(x?.heldByUserName || '');
            const heldByUserId = Number(x?.heldByUserId || 0);

            const isOtherShift = options?.isOtherShift === true;

            const metaParts = [];

            if (lineCount > 0) {
                metaParts.push(`${lineCount} dòng hàng`);
            }

            metaParts.push(`SL ${formatMoney(totalQty)}`);

            return `
   <div class="pos-list-item pos-held-order-card
    ${isOtherShift ? 'pos-held-order-card--other' : 'pos-held-order-card--current'}
    ${isHighValue ? 'pos-held-order-card--high' : ''}">
        <div class="pos-held-order-card__top">
            <div class="pos-held-order-card__identity">
                <div class="d-flex align-items-center gap-2 flex-wrap mb-1">
                    <div class="pos-held-order-card__code">${holdCode}</div>
                    <span class="badge rounded-pill text-bg-secondary">Đang giữ</span>
                    ${isOtherShift ? '<span class="badge rounded-pill text-bg-warning">Ca khác</span>' : ''}
                    <span class="${heldAge.badgeClass}">${heldAge.label}</span>
                </div>

                <div class="pos-held-order-card__customer">${customerName}</div>

                ${customerPhone ? `
                    <div class="pos-held-order-card__phone">${customerPhone}</div>
                ` : ''}
            </div>

            <div class="pos-held-order-card__money">
                ${formatMoney(subtotal)}
            </div>
        </div>

        <div class="pos-held-order-card__meta-row">
            <div class="pos-held-order-card__meta-chips">
                <span class="pos-held-meta-chip">${lineCount} dòng</span>
                <span class="pos-held-meta-chip">SL ${formatMoney(totalQty)}</span>
                ${heldAge.text ? `<span class="pos-held-meta-chip">${escapeHtml(heldAge.text)}</span>` : ''}
            </div>
        </div>

        ${holdNote ? `
            <div class="pos-held-order-card__note">
                <strong>Ghi chú:</strong> ${holdNote}
            </div>
        ` : ''}

        ${isOtherShift ? `
            <div class="pos-held-order-card__other-shift-box">
                ${shiftCode ? `<div><strong>Ca:</strong> ${shiftCode}</div>` : ''}
                ${(terminalName || terminalId) ? `<div><strong>Terminal:</strong> ${terminalName || terminalId}</div>` : ''}
                ${heldByUserName
                        ? `<div><strong>Nhân viên giữ:</strong> ${heldByUserName}</div>`
                        : heldByUserId > 0
                            ? `<div><strong>Nhân viên giữ:</strong> #${heldByUserId}</div>`
                            : ''}
                ${heldAtText ? `<div><strong>Giữ lúc:</strong> ${escapeHtml(heldAtText)}</div>` : ''}
            </div>
        ` : `
            ${heldAtText ? `
                <div class="pos-held-order-card__submeta">
                    Giữ lúc: ${escapeHtml(heldAtText)}
                </div>
            ` : ''}
        `}

        <div class="pos-held-order-card__actions">
            <button type="button"
                    class="btn btn-sm btn-outline-primary"
                    data-resume-btn-id="${Number(x?.orderId || 0)}"
                    data-is-other-shift="${isOtherShift ? 'true' : 'false'}"
                    data-held-shift-code="${escapeHtml(x?.shiftCode || '')}"
                    data-held-terminal-id="${escapeHtml(x?.terminalId || '')}"
                    data-held-terminal-name="${escapeHtml(x?.terminalName || '')}"
                    data-held-user-id="${Number(x?.heldByUserId || 0)}"
                    data-held-user-name="${escapeHtml(x?.heldByUserName || '')}"
                    data-held-customer-name="${customerName}"
                    data-held-subtotal="${subtotal}">
                Lấy lại
            </button>
        </div>
    </div>
`;
        }

        // =========================================================
        // 1. PANEL CHÍNH
        // =========================================================
        heldList.innerHTML = `
        <button type="button"
                id="btnOpenHeldOrders"
                class="pos-held-summary-launcher"
                data-bs-toggle="modal"
                data-bs-target="#heldOrdersModal"
                aria-label="Mở danh sách đơn giữ">
            <div class="pos-held-summary-launcher__main">
                <div class="pos-held-summary-launcher__header">
                    <div class="pos-held-summary-launcher__title">
                        Danh sách đơn giữ
                    </div>
                    <div class="pos-held-summary-launcher__total">
                        ${totalCount} đơn
                    </div>
                </div>

                <div class="pos-held-summary-launcher__stats">
                    <div class="pos-held-summary-stat">
                        <div class="pos-held-summary-stat__label">Ca hiện tại</div>
                        <div class="pos-held-summary-stat__value">${currentShiftOrders.length}</div>
                    </div>

                    <div class="pos-held-summary-stat">
                        <div class="pos-held-summary-stat__label">Ca khác</div>
                        <div class="pos-held-summary-stat__value">${otherShiftOrders.length}</div>
                    </div>
                </div>

                <div class="pos-held-summary-launcher__footer">
                    <span class="pos-held-summary-launcher__hint">
                        Bấm để xem chi tiết
                    </span>
                    <span class="pos-held-summary-launcher__hint-key">
                        Phím tắt: F6
                    </span>
                </div>
            </div>
        </button>
    `;

        // =========================================================
        // 2. MODAL - CA HIỆN TẠI
        // =========================================================
        if (currentShiftList) {
            if (!currentShiftOrders.length) {
                currentShiftList.innerHTML = `
                <div class="pos-held-modal-empty">
                    Chưa có đơn giữ trong ca hiện tại.
                </div>
            `;
            } else {
                currentShiftList.innerHTML = currentShiftOrders
                    .map(x => renderHeldOrderCard(x, { isOtherShift: false }))
                    .join('');
            }
        }

        // =========================================================
        // 3. MODAL - CA KHÁC
        // =========================================================
        if (otherShiftList) {
            if (!otherShiftOrders.length) {
                otherShiftList.innerHTML = `
                <div class="pos-held-modal-empty">
                    Không có đơn giữ từ ca khác.
                </div>
            `;
            } else {
                otherShiftList.innerHTML = otherShiftOrders
                    .map(x => renderHeldOrderCard(x, { isOtherShift: true }))
                    .join('');
            }
        }
    }

    /* =========================================================
       9. BARCODE AUTOCOMPLETE
    ========================================================= */
    function renderBarcodeAutocompleteLoading(barcodeAutocomplete, keyword) {
        if (!barcodeAutocomplete) return;

        barcodeAutocomplete.innerHTML = `
            <div class="pos-autocomplete-loading px-3 py-2 text-muted small">
                <span class="spinner-border spinner-border-sm me-2"></span>
                Đang tìm: <strong>${escapeHtml(keyword || '')}</strong>
            </div>
        `;
        barcodeAutocomplete.style.display = '';
    }

    function renderBarcodeAutocompleteEmpty(barcodeAutocomplete, keyword) {
        if (!barcodeAutocomplete) return;

        barcodeAutocomplete.innerHTML = `
            <div class="pos-autocomplete-empty px-3 py-2 text-muted small">
                Không tìm thấy kết quả cho:
                <strong>${escapeHtml(keyword || '')}</strong>
            </div>
        `;
        barcodeAutocomplete.style.display = '';
    }

    /* =========================================================
       10. PATCH KEY
    ========================================================= */
    function getDraftCustomerRenderKey(draft) {
        const customer = getDraftCustomerInfo(draft);
        const reward = draft?.rewardSummary || draft?.RewardSummary || null;

        return JSON.stringify({
            customerId: customer.customerId,
            name: customer.name,
            phone: customer.phone,
            address: customer.address,
            priceTier: customer.priceTier,
            availablePoints: Number(reward?.availablePoints || reward?.AvailablePoints || 0),
            redeemableVoucherCount: Number(reward?.redeemableVoucherCount || reward?.RedeemableVoucherCount || 0),
            availableVoucherCount: Number(reward?.availableVoucherCount || reward?.AvailableVoucherCount || 0),
            availableVoucherValue: Number(reward?.availableVoucherValue || reward?.AvailableVoucherValue || 0)
        });
    }

    function getDraftPaymentsRenderKey(draft) {
        const payments = Array.isArray(draft?.payments) ? draft.payments : [];

        return JSON.stringify(
            payments.map(x => ({
                paymentId: Number(x?.paymentId || 0),
                method: x?.method || x?.methodName || '',
                amount: Number(x?.amount || 0),
                referenceNumber: x?.referenceNumber || x?.reference || x?.referenceCode || '',
                note: x?.note || '',
                provider: x?.provider || ''
            }))
        );
    }

    function getDraftSummaryRenderKey(draft) {
        return JSON.stringify({
            lineCount: Array.isArray(draft?.lines) ? draft.lines.length : 0,
            subtotal: Number(draft?.subtotal || 0),
            discountTotal: Number(draft?.discountTotal || 0),
            orderDiscount: Number(draft?.orderDiscount || 0),
            voucherDiscountTotal: Number(draft?.voucherDiscountTotal || draft?.VoucherDiscountTotal || 0),
            grandTotal: Number(draft?.grandTotal || 0),
            paidValue: getDraftPaidValue(draft),
            balanceValue: getDraftBalanceValue(draft),
            changeValue: getDraftChangeValue(draft),
            comboDiscountTotal: Number(draft?.comboDiscountTotal || draft?.ComboDiscountTotal || 0),
            comboPromotionNote: draft?.comboPromotionNote || draft?.ComboPromotionNote || '',
            appliedRewardVouchers: JSON.stringify(draft?.appliedRewardVouchers || draft?.AppliedRewardVouchers || [])
        });
    }

    function getDraftPatch(prevDraft, nextDraft) {
        if (!prevDraft && !nextDraft) {
            return {
                draftIdentityChanged: false,
                linesChanged: false,
                customerChanged: false,
                fieldsChanged: false,
                paymentsChanged: false,
                summaryChanged: false
            };
        }

        if (!prevDraft || !nextDraft) {
            return {
                draftIdentityChanged: true,
                linesChanged: true,
                customerChanged: true,
                fieldsChanged: true,
                paymentsChanged: true,
                summaryChanged: true
            };
        }

        const prevDraftId = Number(prevDraft?.orderId || prevDraft?.id || 0);
        const nextDraftId = Number(nextDraft?.orderId || nextDraft?.id || 0);

        const draftIdentityChanged = prevDraftId !== nextDraftId;

        const prevLines = Array.isArray(prevDraft?.lines) ? prevDraft.lines : [];
        const nextLines = Array.isArray(nextDraft?.lines) ? nextDraft.lines : [];

        const linesChanged =
            draftIdentityChanged ||
            !areDraftLinesEquivalent(prevLines, nextLines);

        const customerChanged =
            draftIdentityChanged ||
            getDraftCustomerRenderKey(prevDraft) !== getDraftCustomerRenderKey(nextDraft);

        const fieldsChanged =
            draftIdentityChanged ||
            String(prevDraft?.note || '') !== String(nextDraft?.note || '') ||
            Number(prevDraft?.orderDiscount || 0) !== Number(nextDraft?.orderDiscount || 0);

        const paymentsChanged =
            draftIdentityChanged ||
            getDraftPaymentsRenderKey(prevDraft) !== getDraftPaymentsRenderKey(nextDraft);

        const summaryChanged =
            draftIdentityChanged ||
            getDraftSummaryRenderKey(prevDraft) !== getDraftSummaryRenderKey(nextDraft);

        return {
            draftIdentityChanged,
            linesChanged,
            customerChanged,
            fieldsChanged,
            paymentsChanged,
            summaryChanged
        };
    }

    /* =========================================================
       11. APPLY PATCH / RENDER SCREEN
    ========================================================= */
    function applyDraftPatch(prevDraft, nextDraft, posState) {
        const patch = getDraftPatch(prevDraft, nextDraft);
        const touchedLineId = findTouchedLineId(prevDraft, nextDraft);
        const nextOrderId =
            nextDraft?.currentOrderId ||
            nextDraft?.orderId ||
            nextDraft?.id ||
            nextDraft?.OrderId ||
            null;

        if (!patch) {
            renderDraftSections(nextDraft, posState);

            if (posState?.business) {
                posState.business.currentDraft = nextDraft || null;
                posState.business.currentOrderId = nextOrderId;
            }

            posState.currentDraft = nextDraft || null;
            posState.currentOrderId = nextOrderId;

            return null;
        }

        if (patch.draftIdentityChanged) {
            renderDraftSections(nextDraft, posState);

            if (posState?.business) {
                posState.business.currentDraft = nextDraft || null;
                posState.business.currentOrderId = nextOrderId;
            }

            posState.currentDraft = nextDraft || null;
            posState.currentOrderId = nextOrderId;

            return patch;
        }

        if (patch.linesChanged) {
            patchDraftLines(prevDraft, nextDraft);

            if (touchedLineId && !window.PosScanFeedback?.isPending()) {
                requestAnimationFrame(function () {
                    highlightTouchedLine(touchedLineId, {
                        scroll: false
                    });
                });
            }
        }

        if (patch.customerChanged) {
            renderCustomerInfo(nextDraft);
        }

        if (patch.fieldsChanged) {
            renderDraftFormFields(nextDraft);
            renderMetaPanelSummary(nextDraft);
        }

        if (patch.paymentsChanged) {
            renderPayments(nextDraft?.payments || []);
            renderPaymentModalDraft(nextDraft);
        }

        if (patch.linesChanged || patch.paymentsChanged || patch.summaryChanged || patch.fieldsChanged) {
            renderSummary(nextDraft);
            renderSummaryState(nextDraft);
            renderSummaryActionState(nextDraft);
            renderMetaPanelSummary(nextDraft);
            renderCurrentBadge({ currentOrderId: nextOrderId }, nextDraft);
            renderPaymentPreview(posState);
        }

        if (!patch.linesChanged && !patch.paymentsChanged && !patch.summaryChanged) {
            renderPaymentPreview(posState);
        }

        if (posState?.business) {
            posState.business.currentDraft = nextDraft || null;
            posState.business.currentOrderId = nextOrderId;
        }

        posState.currentDraft = nextDraft || null;
        posState.currentOrderId = nextOrderId;

        return patch;
    }
    function renderScreen(screen, posState) {
        const currentDraft = screen?.currentDraft || null;
        const cartScroller = document.querySelector('.pos-cart-scroll');
        const previousOrderId = posState?.business?.currentDraft?.orderId || posState?.currentDraft?.orderId;
        const cartScrollTop = previousOrderId === currentDraft?.orderId ? cartScroller?.scrollTop || 0 : 0;
        const currentOrderId =
            screen?.currentCart?.currentOrderId ||
            currentDraft?.currentOrderId ||
            currentDraft?.orderId ||
            currentDraft?.id ||
            currentDraft?.OrderId ||
            null;

        const heldOrders = screen?.heldOrders || [];

        if (posState?.business) {
            posState.business.screen = screen || null;
            posState.business.currentDraft = currentDraft;
            posState.business.currentOrderId = currentOrderId;
            posState.business.heldOrders = Array.isArray(heldOrders) ? heldOrders : [];
        }

        posState.currentDraft = currentDraft;
        posState.currentOrderId = currentOrderId;

        renderDraftSections(currentDraft, posState);
        renderHeldList(heldOrders);
        window.PosScanFeedback?.sync(currentDraft);
        if (cartScroller) cartScroller.scrollTop = cartScrollTop;
    }

    function syncDraftToUi(draft, posState) {
        const cartScroller = document.querySelector('.pos-cart-scroll');
        const cartScrollTop = cartScroller?.scrollTop || 0;
        const prevDraft =
            posState?.business?.currentDraft ||
            posState?.currentDraft ||
            null;

        const patch = applyDraftPatch(prevDraft, draft || null, posState);
        window.PosScanFeedback?.sync(draft || null);
        // Replacing/reordering rows can temporarily collapse the scroll area.
        // Restore after the whole patch, never scroll the page toward a scan.
        if (cartScroller) cartScroller.scrollTop = patch?.draftIdentityChanged ? 0 : cartScrollTop;
        return patch;
    }

    /* =========================================================
       12. EXPORT
    ========================================================= */
    return {
        buildVariantDisplayName,
        buildCompactLineMeta,
        buildLineDetailHtml,
        initLineInfoPopovers,

        getDraftCustomerInfo,
        getDraftPaidValue,
        getDraftBalanceValue,
        getDraftChangeValue,

        formatNetworkTime,
        getNetworkUiState,
        ensureNetworkBannerContainer,
        renderNetworkBanner,

        renderCurrentBadge,
        renderSummary,
        normalizePaymentMethod,
        getPaymentDisplayTitle,
        getPaymentSubLine1,
        getPaymentSubLine2,
        renderPaymentCard,
        renderPayments,
        renderPaymentModalDraft,
        renderPaymentPreview,
        renderCustomerInfo,

        renderEmptyDraftRow,
        getDraftLineId,
        getDraftLineRenderKey,
        buildDraftLineRenderMap,
        areDraftLinesEquivalent,
        findTouchedLineId,
        buildDraftLineRowHtml,
        buildDraftLinesHtml,
        renderCurrentDraftLines,
        renderCurrentDraftTable,
        highlightTouchedLine,
        renderSummaryActionState,


        createRowFromHtml,
        getCurrentDraftBody,
        getRenderedRowByLineId,
        getRenderedRowLineId,
        isEmptyDraftRow,
        createDraftLineRowElement,
        replaceRowIfNeeded,
        applyRowMeta,
        patchDraftLines,
        buildLinePriceTableHtml,

        renderDraftFormFields,
        renderDraftSummaryPanels,
        renderSummaryState,
        renderDraftSections,
        renderHeldList,
        renderBarcodeAutocompleteLoading,
        renderBarcodeAutocompleteEmpty,

        getDraftCustomerRenderKey,
        getDraftPaymentsRenderKey,
        getDraftSummaryRenderKey,
        getDraftPatch,
        applyDraftPatch,
        renderScreen,
        syncDraftToUi
    };
})();
