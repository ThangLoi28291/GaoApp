(function () {
    'use strict';

    const entryQuery = new URLSearchParams(window.location.search);
    const receivingForPos = entryQuery.get('start') === '1';
    function returnToPos() {
        let target = '/admin/pos';
        try {
            const requested = new URL(entryQuery.get('returnUrl') || target, window.location.origin);
            if (requested.origin === window.location.origin && /^\/admin\/pos(?:\/(?:v3|legacy))?\/?$/.test(requested.pathname))
                target = requested.pathname + requested.search;
        } catch { /* Fall back to the POS page on this host. */ }
        window.location.replace(target);
    }

    // =========================
    // Elements
    // =========================
    const shiftStatusBadge = document.getElementById('shiftStatusBadge');

    const openShiftSection = document.getElementById('openShiftSection');
    const noOpenShiftSection = document.getElementById('noOpenShiftSection');

    const btnShowOpenModal = document.getElementById('btnShowOpenModal');
    const btnShowCashTxnModal = document.getElementById('btnShowCashTxnModal');
    const btnShowCloseModal = document.getElementById('btnShowCloseModal');

    const shiftId = document.getElementById('shiftId');
    const shiftCode = document.getElementById('shiftCode');
    const openedAt = document.getElementById('openedAt');
    const openingCash = document.getElementById('openingCash');
    const cashSalesTotal = document.getElementById('cashSalesTotal');
    const nonCashSalesTotal = document.getElementById('nonCashSalesTotal');
    const cashRefundTotal = document.getElementById('cashRefundTotal');
    const nonCashRefundTotal = document.getElementById('nonCashRefundTotal');
    const refundTotalShift = document.getElementById('refundTotalShift');
    const refundCountShift = document.getElementById('refundCountShift');
    const cashInTotal = document.getElementById('cashInTotal');
    const cashOutTotal = document.getElementById('cashOutTotal');
    const closingCashExpected = document.getElementById('closingCashExpected');
    const openNote = document.getElementById('openNote');
    const voidCountShift = document.getElementById('voidCountShift');

    const txtOpeningCash = document.getElementById('txtOpeningCash');
    const txtOpenNote = document.getElementById('txtOpenNote');

    const cashTxnType = document.getElementById('cashTxnType');
    const cashTxnTypeButtons = Array.from(document.querySelectorAll('[data-cash-txn-type]'));
    const cashTxnAmount = document.getElementById('cashTxnAmount');
    const cashTxnReason = document.getElementById('cashTxnReason');
    const cashTxnNote = document.getElementById('cashTxnNote');

    const txtClosingCashActual = document.getElementById('txtClosingCashActual');
    const txtCloseNote = document.getElementById('txtCloseNote');

    const btnOpenShift = document.getElementById('btnOpenShiftConfirm');
    const btnAddCashTxn = document.getElementById('btnAddCashTxn');
    const btnCloseShift = document.getElementById('btnCloseShift');

    const cashTxnBody = document.getElementById('cashTxnBody');
    const ddlWarehouse = document.getElementById('ddlWarehouse');

    const cashTxnPagerInfo = document.getElementById('cashTxnPagerInfo');
    const btnCashTxnPrev = document.getElementById('btnCashTxnPrev');
    const btnCashTxnNext = document.getElementById('btnCashTxnNext');

    const openShiftModalEl = document.getElementById('openShiftModal');
    const cashTxnModalEl = document.getElementById('cashTxnModal');
    const closeShiftModalEl = document.getElementById('closeShiftModal');

    const openShiftModal = openShiftModalEl ? new bootstrap.Modal(openShiftModalEl) : null;
    const cashTxnModal = cashTxnModalEl ? new bootstrap.Modal(cashTxnModalEl) : null;
    const closeShiftModal = closeShiftModalEl ? new bootstrap.Modal(closeShiftModalEl) : null;

    const openDenomGrid = document.getElementById('openDenomGrid');
    const closeDenomGrid = document.getElementById('closeDenomGrid');
    const openDenomTotalText = document.getElementById('openDenomTotalText');
    const closeDenomTotalText = document.getElementById('closeDenomTotalText');
    const btnResetOpenDenoms = document.getElementById('btnResetOpenDenoms');
    const btnResetCloseDenoms = document.getElementById('btnResetCloseDenoms');
    const closeExpectedCashText = document.getElementById('closeExpectedCashText');
    const closeActualCashText = document.getElementById('closeActualCashText');
    const closeDiffCashText = document.getElementById('closeDiffCashText');
    const closeDiffHint = document.getElementById('closeDiffHint');
    const btnReviewShiftDifference = document.getElementById('btnReviewShiftDifference');
    const txtHandoverSlipBarcode = document.getElementById('txtHandoverSlipBarcode');
    const btnLoadHandoverSlip = document.getElementById('btnLoadHandoverSlip');
    const selectedHandoverSlipId = document.getElementById('selectedHandoverSlipId');
    const handoverSlipInfo = document.getElementById('handoverSlipInfo');

    // =========================
    // Constants / State
    // =========================
    const DENOMS = [500000, 200000, 100000, 50000, 20000, 10000, 5000, 2000, 1000];
    const PAGE_SIZE = 8;

    let cashTxnAllItems = [];
    let cashTxnPage = 1;
    const printContext = JSON.parse(document.getElementById('shiftPrintContext')?.textContent || '{}');
    const drawerModalEl = document.getElementById('cashDrawerModal');
    const drawerModal = drawerModalEl ? new bootstrap.Modal(drawerModalEl) : null;
    const btnShowDrawerModal = document.getElementById('btnShowDrawerModal');
    const btnOpenCashDrawer = document.getElementById('btnOpenCashDrawer');
    const drawerReason = document.getElementById('cashDrawerReason');
    const drawerError = document.getElementById('cashDrawerError');
    const shiftDrawerEl = document.getElementById('shiftDrawerConfirmModal');
    const shiftDrawerModal = shiftDrawerEl ? new bootstrap.Modal(shiftDrawerEl, { backdrop: 'static', keyboard: false }) : null;
    const shiftDrawerConfirm = document.getElementById('btnConfirmShiftDrawer');
    const shiftDrawerCancel = document.getElementById('btnCancelShiftDrawer');
    const shiftDrawerContinue = document.getElementById('btnContinueShiftCount');
    const shiftDrawerError = document.getElementById('shiftDrawerConfirmError');
    let shiftDrawerPurpose = null;
    let shiftDrawerBusy = false;
    let shiftDrawerNext = null;
    let shiftDrawerShiftId = null;
    let activeShift = null;

    // =========================
    // Helpers
    // =========================
    function getToken() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function formatMoney(value) {
        const n = Number(value || 0);
        return n.toLocaleString('vi-VN');
    }
    function formatMoneyWithCurrency(value) {
        return `${formatMoney(value)} đ`;
    }

    function getRawMoney(value) {
        return Number((value || '').toString().replace(/\./g, '').replace(/,/g, '').replace(/[^\d]/g, '') || 0);
    }

    function parseCashTxnAmount(value) {
        return Number((value || '').toString().replace(/\./g, '').replace(',', '.'));
    }

    function formatCashTxnAmountInput(input) {
        const value = input.value;
        const parts = value.replace(/\./g, '').match(/^(-?)(\d*)(,\d*)?$/);
        if (!parts) return;

        const caret = input.selectionStart;
        const charactersBeforeCaret = value.slice(0, caret).replace(/\./g, '').length;
        const formatted = parts[1] + parts[2].replace(/\B(?=(\d{3})+(?!\d))/g, '.') + (parts[3] || '');
        if (formatted === value) return;

        input.value = formatted;
        if (caret !== null) {
            let position = 0;
            let characters = 0;
            while (position < formatted.length && characters < charactersBeforeCaret) {
                if (formatted[position] !== '.') characters++;
                position++;
            }
            input.setSelectionRange(position, position);
        }
    }

    function normalizeCashTxnPastedAmount(text) {
        const value = text.trim().replace(/\s/g, '').replace(/(?:₫|đ|vnd)$/i, '');
        if (/^-?\d{1,3}(?:,\d{3})+(?:\.\d+)?$/.test(value)) {
            return value.replace(/,/g, '').replace('.', ',');
        }
        if (/^-?\d+\.\d+$/.test(value) && !/^-?\d{1,3}\.\d{3}$/.test(value)) {
            return value.replace('.', ',');
        }
        return value;
    }

    function setOpenDirectInput(value) {
        if (!txtOpeningCash) return;
        txtOpeningCash.value = formatMoney(value);
    }

    function getOpenDirectInputValue() {
        return txtOpeningCash ? getRawMoney(txtOpeningCash.value) : 0;
    }

    function getOpenDenomTotalValue() {
        let total = 0;

        getDenomInputs('open').forEach(input => {
            const denom = Number(input.dataset.denom || 0);
            const qty = Math.max(0, Number(input.value || 0));
            total += denom * qty;
        });

        return total;
    }

    function getOpenBillCount() {
        return getDenomInputs('open')
            .reduce((sum, input) => sum + Math.max(0, Number(input.value || 0)), 0);
    }

    async function loadHandoverSlipByBarcode() {
        const barcode = (txtHandoverSlipBarcode && txtHandoverSlipBarcode.value || '').trim();

        if (!barcode) {
            showError('Vui lòng quét hoặc nhập mã phiếu nhận ca.');
            return;
        }

        try {
            const slip = await fetchJson(`/admin/pos/shift-handover-slips/barcode?barcodeValue=${encodeURIComponent(barcode)}`);

            if (!slip || !slip.id) {
                showError('Không tìm thấy phiếu nhận ca.');
                return;
            }

            if (selectedHandoverSlipId) {
                selectedHandoverSlipId.value = slip.id;
            }

            if (ddlWarehouse && slip.warehouseId) {
                ddlWarehouse.value = slip.warehouseId;
            }

            resetDenoms('open');

            (slip.denominations || []).forEach(x => {
                const input = document.getElementById(`openQty_${x.denominationValue}`);
                if (input) {
                    input.value = x.quantity || 0;
                }
            });

            setOpenDirectInput(slip.openingCashTotal || 0);
            calcDenomTotal('open');
            updateOpenShiftSummary();

            if (txtOpenNote) {
                txtOpenNote.value = `Nhận ca từ phiếu ${slip.slipCode}`;
            }

            if (handoverSlipInfo) {
                handoverSlipInfo.style.display = '';
                handoverSlipInfo.innerHTML = `
                Đã tải phiếu <strong>${slip.slipCode}</strong><br/>
                Tổng tiền: <strong>${formatMoney(slip.openingCashTotal)} đ</strong><br/>
                Kho: <strong>${slip.warehouseCode || ''} ${slip.warehouseName || ''}</strong>
            `;
            }

            showSuccess('Đã tải phiếu nhận ca.');
        } catch (err) {
            if (selectedHandoverSlipId) selectedHandoverSlipId.value = '';
            if (handoverSlipInfo) handoverSlipInfo.style.display = 'none';
            window.PosError.handle(err);
        }
    }

    function updateOpenShiftSummary() {
        const denomTotal = getOpenDenomTotalValue();
        const directTotal = getOpenDirectInputValue();
        const finalTotal = denomTotal > 0 ? denomTotal : directTotal;
        const diff = denomTotal - directTotal;

        const openLeftTotalText = document.getElementById('openLeftTotalText');
        const openMoneyText = document.getElementById('openMoneyText');
        const openSummaryTotalText = document.getElementById('openSummaryTotalText');
        const openBillCountText = document.getElementById('openBillCountText');
        const openDirectDiffText = document.getElementById('openDirectDiffText');

        if (openDenomTotalText) {
            openDenomTotalText.textContent = `${formatMoney(finalTotal)} VND`;
        }

        if (openLeftTotalText) {
            openLeftTotalText.textContent = formatMoneyWithCurrency(finalTotal);
        }

        if (openMoneyText) {
            openMoneyText.textContent = finalTotal > 0
                ? `Tổng tiền nhận ca: ${formatMoneyWithCurrency(finalTotal)}`
                : 'Không đồng';
        }

        if (openSummaryTotalText) {
            openSummaryTotalText.textContent = formatMoneyWithCurrency(finalTotal);
        }

        if (openBillCountText) {
            openBillCountText.textContent = `${getOpenBillCount()} tờ`;
        }

        if (openDirectDiffText) {
            openDirectDiffText.textContent = formatMoneyWithCurrency(Math.abs(diff));
            openDirectDiffText.className = diff === 0 ? 'text-success' : 'text-danger';
        }

        // Giá trị thật gửi backend vẫn là số không format
        if (txtOpeningCash) {
            txtOpeningCash.dataset.rawValue = finalTotal.toString();
        }
    }
    function getDenomTotalValue(prefix) {
        let total = 0;

        getDenomInputs(prefix).forEach(input => {
            const denom = Number(input.dataset.denom || 0);
            const qty = Math.max(0, Number(input.value || 0));
            total += denom * qty;
        });

        return total;
    }

    function getBillCount(prefix) {
        return getDenomInputs(prefix)
            .reduce((sum, input) => sum + Math.max(0, Number(input.value || 0)), 0);
    }

    function updateCloseShiftSummary() {
        const actual = Number((txtClosingCashActual && txtClosingCashActual.value || '0').toString().replace(/\./g, '').replace(/[^\d]/g, '') || 0);
        const expected = getExpectedClosingCashValue();
        const diff = actual - expected;

        if (btnReviewShiftDifference) btnReviewShiftDifference.hidden = diff === 0;

        const closeLeftTotalText = document.getElementById('closeLeftTotalText');
        const closeMoneyText = document.getElementById('closeMoneyText');
        const closeBillCountText = document.getElementById('closeBillCountText');
        const closeSummaryTotalText = document.getElementById('closeSummaryTotalText');
        const closeSummaryExpectedText = document.getElementById('closeSummaryExpectedText');
        const closeSummaryDiffText = document.getElementById('closeSummaryDiffText');

        if (closeLeftTotalText) closeLeftTotalText.textContent = `${formatMoney(actual)} đ`;
        if (closeMoneyText) closeMoneyText.textContent = actual > 0 ? `Tổng tiền thực đếm: ${formatMoney(actual)} đ` : 'Không đồng';
        if (closeBillCountText) closeBillCountText.textContent = `${getBillCount('close')} tờ`;
        if (closeSummaryTotalText) closeSummaryTotalText.textContent = `${formatMoney(actual)} đ`;
        if (closeSummaryExpectedText) closeSummaryExpectedText.textContent = `${formatMoney(expected)} đ`;

        if (closeSummaryDiffText) {
            closeSummaryDiffText.textContent = `${formatMoney(Math.abs(diff))} đ`;
            closeSummaryDiffText.className = diff === 0
                ? 'text-success'
                : diff > 0
                    ? 'text-primary'
                    : 'text-danger';
        }

        renderCloseShiftCompare();
    }
    function autoConvertOpenCash(amount) {
        let remaining = Math.max(0, Number(amount || 0));

        getDenomInputs('open').forEach(input => {
            const denom = Number(input.dataset.denom || 0);
            const qty = Math.floor(remaining / denom);

            input.value = qty;
            remaining -= qty * denom;
        });

        calcDenomTotal('open');
    }
    function formatDateTime(value) {
        if (!value) return '-';

        try {
            return new Date(value).toLocaleString('vi-VN');
        } catch (_) {
            return '-';
        }
    }

    function showSuccess(msg) {
        if (window.toastr) {
            toastr.success(msg);
            return;
        }

        console.log(msg);
    }

    function showError(msg) {
        const finalMsg = (msg || 'Có lỗi xảy ra').replace(/\n/g, '<br/>');

        if (window.toastr) {
            toastr.error(finalMsg);
            return;
        }

        console.error(finalMsg);
    }

    function parseMoneyText(text) {
        const raw = (text || '')
            .toString()
            .replace(/\./g, '')
            .replace(/,/g, '')
            .trim();

        const n = Number(raw || 0);
        return isNaN(n) ? 0 : n;
    }

    function getExpectedClosingCashValue() {
        return parseMoneyText(closingCashExpected ? closingCashExpected.textContent : '0');
    }

    async function postJson(url, data) {
        return await window.PosError.postJson(url, data, getToken);
    }

    async function fetchJson(url, options) {
        return await window.PosError.fetchJson(url, options);
    }

    async function withButtonLoading(button, action, loadingText) {
        if (!button) return await action();
        if (button.disabled) return;

        const oldHtml = button.innerHTML;
        const text = loadingText || 'Đang xử lý';

        button.disabled = true;
        button.innerHTML = `<span class="spinner-border spinner-border-sm me-2"></span>${text}`;

        try {
            return await action();
        } finally {
            button.disabled = false;
            button.innerHTML = oldHtml;
        }
    }

    function setText(el, value) {
        if (!el) return;
        el.textContent = value;
    }

    function setMoney(el, value) {
        if (!el) return;
        el.textContent = formatMoney(value);
    }

    function setClosingCashExpectedText(value) {
        const text = formatMoney(value);
        setText(closingCashExpected, text);
        const warning = document.getElementById('shiftNegativeCashWarning');
        if (warning) {
            warning.hidden = !(Number(value) < 0);
            warning.textContent = `Cần đối soát: tiền dự kiến trong ca là ${text} đ. Chi và hoàn tiền đang vượt tiền đầu ca, doanh thu tiền mặt và thu thêm. Kiểm tra chứng từ và tiền thực đếm với Admin; hệ thống không tự bù khoản thiếu.`;
        }
        closingCashExpected?.classList.toggle('text-danger', Number(value) < 0);
    }

    function syncCashTxnTypePresentation() {
        const currentValue = (cashTxnType && cashTxnType.value || '2').toString();

        if (cashTxnModalEl) {
            cashTxnModalEl.dataset.cashTxnTheme = currentValue === '2' ? 'out' : 'in';
            const title = document.getElementById('cashTxnTitle');
            if (title) title.textContent = currentValue === '2' ? 'Chi tiền mặt' : 'Thu tiền mặt';
        }

        cashTxnTypeButtons.forEach(button => {
            const isActive = button.dataset.cashTxnType === currentValue;
            button.classList.toggle('active', isActive);
            button.setAttribute('aria-pressed', isActive ? 'true' : 'false');
        });
    }

    function setCashTxnType(value) {
        if (!cashTxnType) return;
        cashTxnType.value = value.toString();
        syncCashTxnTypePresentation();
    }

    function getCashTypeText(type) {
        const val = (type || '').toString().toLowerCase();

        if (val.includes('cashin') || val === '1' || val === 'in') return 'Thu tiền mặt';
        if (val.includes('cashout') || val === '2' || val === 'out') return 'Chi tiền mặt';

        return type || '-';
    }

    function isCashInType(type) {
        const val = (type || '').toString().toLowerCase();
        return val.includes('cashin') || val === '1' || val === 'in';
    }

    function setBadgeLoading() {
        if (!shiftStatusBadge) return;

        shiftStatusBadge.className = 'shift-status-pill loading';
        shiftStatusBadge.innerHTML = `<i class="bx bx-loader-alt"></i><span>Đang tải...</span>`;
    }

    function renderNoOpenShift() {
        activeShift = null;
        syncShiftIdentity(null);
        if (btnShowDrawerModal) btnShowDrawerModal.style.display = 'none';
        if (window.PosError && window.PosError.clearBanner) {
            window.PosError.clearBanner();
        }

        if (shiftStatusBadge) {
            shiftStatusBadge.className = 'shift-status-pill warning';
            shiftStatusBadge.innerHTML = `<i class="bx bx-error-circle"></i><span>Chưa mở ca</span>`;
        }

        if (openShiftSection) openShiftSection.style.display = 'none';
        if (noOpenShiftSection) noOpenShiftSection.style.display = '';

        if (btnShowOpenModal) btnShowOpenModal.style.display = '';
        if (btnShowCashTxnModal) btnShowCashTxnModal.style.display = 'none';
        if (btnShowCloseModal) btnShowCloseModal.style.display = 'none';

        setText(shiftId, '-');
        setText(shiftCode, '-');
        setText(openedAt, '-');
        setMoney(openingCash, 0);
        setMoney(cashSalesTotal, 0);
        setMoney(nonCashSalesTotal, 0);
        setMoney(cashRefundTotal, 0);
        setMoney(nonCashRefundTotal, 0);
        setMoney(refundTotalShift, 0);
        setText(refundCountShift, '0');
        setText(voidCountShift, '0');
        setMoney(cashInTotal, 0);
        setMoney(cashOutTotal, 0);
        setClosingCashExpectedText(0);
        setText(openNote, '-');

        renderCashTransactions([]);
        renderCloseShiftCompare();
    }

    function renderOpenShift(shift) {
        activeShift = shift;
        syncShiftIdentity(shift);
        if (btnShowDrawerModal) btnShowDrawerModal.style.display = printContext.canReconcile ? '' : 'none';
        if (window.PosError && window.PosError.clearBanner) {
            window.PosError.clearBanner();
        }

        if (shiftStatusBadge) {
            shiftStatusBadge.className = 'shift-status-pill open';
            shiftStatusBadge.innerHTML = `<i class="bx bx-check-circle"></i><span>Đang mở ca</span>`;
        }

        if (noOpenShiftSection) noOpenShiftSection.style.display = 'none';
        if (openShiftSection) openShiftSection.style.display = '';

        if (btnShowOpenModal) btnShowOpenModal.style.display = 'none';
        if (btnShowCashTxnModal) btnShowCashTxnModal.style.display = printContext.canReconcile ? '' : 'none';
        if (btnShowCloseModal) btnShowCloseModal.style.display = '';

        setText(shiftId, shift.id || '-');
        setText(shiftCode, shift.shiftCode || '-');
        setText(openedAt, formatDateTime(shift.openedAtUtc));
        setMoney(openingCash, shift.openingCash);
        setMoney(cashSalesTotal, shift.cashSalesTotal);
        setMoney(nonCashSalesTotal, shift.nonCashSalesTotal);
        setMoney(cashRefundTotal, shift.cashRefundTotal);
        setMoney(nonCashRefundTotal, shift.nonCashRefundTotal);
        setMoney(refundTotalShift, shift.refundTotal);
        setText(refundCountShift, (shift.refundCount ?? 0).toString());
        setText(voidCountShift, (shift.voidCount ?? 0).toString());
        setMoney(cashInTotal, shift.cashInTotal);
        setMoney(cashOutTotal, shift.cashOutTotal);
        setClosingCashExpectedText(shift.closingCashExpected);
        setText(openNote, shift.openNote || '-');

        renderCloseShiftCompare();
    }

    function syncShiftIdentity(shift) {
        const employee = shift?.openedByUserName || printContext.userName || 'Chưa xác định nhân viên';
        const terminal = shift?.terminalName || shift?.terminalCode || printContext.terminalName || 'Chưa xác định máy tính tiền';
        const displayName = employee + ' · ' + terminal;
        setText(document.getElementById('shiftDisplayName'), displayName);
        setText(document.getElementById('posCtxUserName'), employee);
        const code = document.getElementById('posCtxShiftCode');
        setText(code, shift ? displayName : 'Chưa mở ca');
        if (code) code.title = shift?.shiftCode || '';
        const pill = document.getElementById('posCtxShiftPill');
        if (pill) {
            pill.className = 'pos-shift-pill ' + (shift ? 'open' : 'warning');
            const icon = pill.querySelector('i');
            if (icon) icon.className = shift ? 'bx bx-check-circle' : 'bx bx-error-circle';
        }
        setText(document.getElementById('posCtxShiftOpenedAt'), shift ? 'Mở lúc: ' + formatDateTime(shift.openedAtUtc) : '');
        setText(document.getElementById('posCtxWarehouseName'), shift?.warehouseName || 'Chưa chọn kho');
    }

    function switchShiftTab(tabId) {
        document.querySelectorAll('.shift-tab-btn').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.shiftTab === tabId);
        });

        document.querySelectorAll('.shift-tab-pane').forEach(pane => {
            pane.classList.toggle('active', pane.id === tabId);
        });
    }

    // =========================
    // Denomination
    // =========================

    function getDenominationPayload(prefix) {
        return getDenomInputs(prefix)
            .map(input => {
                return {
                    denominationValue: Number(input.dataset.denom || 0),
                    quantity: Math.max(0, Number(input.value || 0))
                };
            })
            .filter(x => x.denominationValue > 0 && x.quantity > 0);
    }
    function buildDenomGrid(container, prefix) {
        if (!container) return;

        // Riêng popup mở ca dùng layout dạng bảng giống mẫu đã chốt
        if (prefix === 'open' || prefix === 'close')  {
            container.innerHTML = DENOMS.map((value, index) => `
            <div class="shift-open-money-row">
                <div class="shift-open-denom-info">
                    <div class="shift-open-bill-img bill-${value}"></div>
                    <div class="shift-open-denom-label">${formatMoney(value)} đ</div>
                </div>

                <div class="shift-open-stepper">
                    <button type="button"
                            class="shift-open-stepper-btn"
                            data-step="down"
                            data-prefix="${prefix}"
                            data-value="${value}">
                        −
                    </button>

                    <input id="${prefix}Qty_${value}"
                           data-prefix="${prefix}"
                           data-denom="${value}"
                           data-index="${index}"
                           type="number"
                           min="0"
                           step="1"
                           value="0"
                           class="form-control shift-open-stepper-input" />

                    <button type="button"
                            class="shift-open-stepper-btn"
                            data-step="up"
                            data-prefix="${prefix}"
                            data-value="${value}">
                        +
                    </button>
                </div>

                <div class="shift-open-line-total" id="${prefix}LineTotal_${value}">
                    0 đ
                </div>

                <button type="button"
                        class="shift-open-line-clear"
                        data-clear-denom="${value}"
                        data-prefix="${prefix}">
                    ×
                </button>
            </div>
        `).join('');

            return;
        }

        // Layout cũ giữ cho popup đóng ca
        container.innerHTML = DENOMS.map((value, index) => `
        <div class="shift-denom-item">
            <div class="shift-denom-head">
                <div class="shift-denom-label">${formatMoney(value)}đ</div>
                <div class="shift-denom-total" id="${prefix}LineTotal_${value}">0</div>
            </div>

            <div class="shift-stepper">
                <button type="button"
                        class="shift-stepper-btn"
                        data-step="down"
                        data-prefix="${prefix}"
                        data-value="${value}">
                    −
                </button>

                <input id="${prefix}Qty_${value}"
                       data-prefix="${prefix}"
                       data-denom="${value}"
                       data-index="${index}"
                       type="number"
                       min="0"
                       step="1"
                       value="0"
                       class="form-control shift-stepper-input" />

                <button type="button"
                        class="shift-stepper-btn"
                        data-step="up"
                        data-prefix="${prefix}"
                        data-value="${value}">
                    +
                </button>
            </div>
        </div>
    `).join('');
    }

    function getDenomInputs(prefix) {
        return Array
            .from(document.querySelectorAll(`input[data-prefix="${prefix}"][data-denom]`))
            .sort((a, b) => Number(a.dataset.index) - Number(b.dataset.index));
    }

    function renderCloseShiftCompare() {
        if (!closeExpectedCashText || !closeActualCashText || !closeDiffCashText || !closeDiffHint || !txtClosingCashActual) {
            return;
        }

        const expected = getExpectedClosingCashValue();
        const actual = getRawMoney(txtClosingCashActual.value);
        const diff = actual - expected;

        closeExpectedCashText.textContent = formatMoney(expected);
        closeActualCashText.textContent = formatMoney(actual);
        closeDiffCashText.textContent = formatMoney(Math.abs(diff));

        closeExpectedCashText.className = 'shift-close-compare-value expected';
        closeActualCashText.className = 'shift-close-compare-value actual';

        if (diff === 0) {
            closeDiffCashText.className = 'shift-close-compare-value match';
            closeDiffHint.className = 'shift-close-compare-hint match';
            closeDiffHint.textContent = 'Khớp quỹ. Tiền thực đếm đúng bằng tiền dự kiến cuối ca.';
            return;
        }

        if (diff > 0) {
            closeDiffCashText.className = 'shift-close-compare-value positive';
            closeDiffHint.className = 'shift-close-compare-hint positive';
            closeDiffHint.textContent = `Thừa quỹ ${formatMoney(diff)}. Tiền thực đếm lớn hơn tiền dự kiến cuối ca.`;
            return;
        }

        closeDiffCashText.className = 'shift-close-compare-value negative';
        closeDiffHint.className = 'shift-close-compare-hint negative';
        closeDiffHint.textContent = `Thiếu quỹ ${formatMoney(Math.abs(diff))}. Tiền thực đếm nhỏ hơn tiền dự kiến cuối ca.`;
    }

    function calcDenomTotal(prefix) {
        let total = 0;

        getDenomInputs(prefix).forEach(input => {
            const denom = Number(input.dataset.denom || 0);
            const qty = Math.max(0, Number(input.value || 0));
            const lineTotal = denom * qty;

            total += lineTotal;

            const lineEl = document.getElementById(`${prefix}LineTotal_${denom}`);
            if (lineEl) {
                lineEl.textContent = prefix === 'open'
                    ? formatMoneyWithCurrency(lineTotal)
                    : formatMoney(lineTotal);
            }
        });

        if (prefix === 'open') {
            if (txtOpeningCash) {
                txtOpeningCash.value = formatMoney(total);
                txtOpeningCash.dataset.rawValue = total.toString();
            }

            updateOpenShiftSummary();
        }
        else if (prefix === 'close') {
            if (closeDenomTotalText) {
                closeDenomTotalText.textContent = formatMoneyWithCurrency(total);
            }

            if (txtClosingCashActual) {
                txtClosingCashActual.value = formatMoney(total);
            }

            updateCloseShiftSummary();
        }

        return total;
    }

    function resetDenoms(prefix) {
        getDenomInputs(prefix).forEach(input => {
            input.value = 0;
        });

        calcDenomTotal(prefix);
    }

    function focusNextDenomInput(prefix, currentIndex) {
        const inputs = getDenomInputs(prefix);
        const next = inputs.find(x => Number(x.dataset.index) === currentIndex + 1);

        if (next) {
            next.focus();
            next.select();
        }
    }

    function wireDenomEvents(prefix) {
        getDenomInputs(prefix).forEach(input => {
            input.addEventListener('focus', function () {
                this.select();
            });

            input.addEventListener('input', function () {
                if (Number(this.value || 0) < 0) {
                    this.value = 0;
                }
                calcDenomTotal(prefix);
            });

            input.addEventListener('keydown', function (e) {
                const currentIndex = Number(this.dataset.index || 0);

                if (e.key === 'Enter' && !e.ctrlKey) {
                    e.preventDefault();
                    focusNextDenomInput(prefix, currentIndex);
                    return;
                }

                if (e.key === 'ArrowUp') {
                    e.preventDefault();
                    this.value = Number(this.value || 0) + 1;
                    calcDenomTotal(prefix);
                    this.select();
                    return;
                }

                if (e.key === 'ArrowDown') {
                    e.preventDefault();
                    this.value = Math.max(0, Number(this.value || 0) - 1);
                    calcDenomTotal(prefix);
                    this.select();
                }
            });
        });

        document.querySelectorAll(`button[data-prefix="${prefix}"][data-step]`).forEach(btn => {
            btn.addEventListener('click', function () {
                const denom = this.dataset.value;
                const input = document.getElementById(`${prefix}Qty_${denom}`);
                if (!input) return;

                const current = Number(input.value || 0);
                input.value = this.dataset.step === 'up'
                    ? current + 1
                    : Math.max(0, current - 1);

                calcDenomTotal(prefix);
                input.focus();
                input.select();
            });
        });
    }

    function initDenomPanels() {
        buildDenomGrid(openDenomGrid, 'open');
        buildDenomGrid(closeDenomGrid, 'close');

        wireDenomEvents('open');
        wireDenomEvents('close');

        calcDenomTotal('open');
        calcDenomTotal('close');
    }

    function focusFirstDenom(prefix) {
        const first = getDenomInputs(prefix)[0];
        if (!first) return;

        setTimeout(() => {
            first.focus();
            first.select();
        }, 120);
    }

    // =========================
    // Modal actions
    // =========================
    function confirmShiftDrawer(purpose) {
        if (shiftDrawerPurpose || !shiftDrawerModal) return;
        if (purpose === 'Close' && !activeShift) return;
        shiftDrawerPurpose = purpose;
        shiftDrawerShiftId = purpose === 'Close' ? activeShift.id : null;
        const receiving = purpose === 'Receive';
        document.getElementById('shiftDrawerConfirmTitle').textContent = receiving ? 'Mở két để nhận ca?' : 'Mở két để chốt ca?';
        document.getElementById('shiftDrawerConfirmDescription').textContent = receiving
            ? 'Đếm tiền có trong két, sau đó nhập số tiền đầu ca để bắt đầu nhận ca.'
            : 'Đếm tiền thực tế trong két, sau đó nhập số tiền cuối ca để đối chiếu và chốt ca.';
        document.getElementById('shiftDrawerAutoReason').textContent = receiving ? 'Mở két nhận ca' : 'Mở két chốt ca';
        document.getElementById('shiftDrawerTerminal').textContent = printContext.terminalName || 'Quầy hiện tại';
        document.getElementById('shiftDrawerUser').textContent = printContext.userName || '';
        shiftDrawerError.hidden = true;
        shiftDrawerError.textContent = '';
        shiftDrawerContinue.hidden = true;
        shiftDrawerConfirm.disabled = true; // Enabled only when the confirmation is visible.
        shiftDrawerModal.show();
    }

    function continueShiftCount() {
        if (shiftDrawerBusy || !shiftDrawerPurpose) return;
        shiftDrawerNext = shiftDrawerPurpose === 'Receive' ? openOpenShiftModal : openCloseShiftPopup;
        shiftDrawerModal.hide();
    }

    async function confirmShiftDrawerOpen() {
        if (shiftDrawerBusy || !shiftDrawerPurpose || shiftDrawerConfirm.disabled) return;
        const purpose = shiftDrawerPurpose;
        const shiftId = shiftDrawerShiftId;
        shiftDrawerBusy = true;
        shiftDrawerCancel.disabled = true;
        shiftDrawerContinue.hidden = true;
        let opened = false;
        await withButtonLoading(shiftDrawerConfirm, async () => {
            shiftDrawerError.hidden = true;
            try {
                await window.PosPrinting.drawerReady(printContext);
                const recorded = await postJson(`/admin/pos/shift/cash-drawer/${purpose === 'Receive' ? 'receive' : 'close'}`, { shiftId });
                if (!(recorded?.auditId > 0) || recorded.purpose !== purpose || recorded.shiftId !== shiftId)
                    throw new Error('Chưa xác nhận lưu lý do mở két.');
                await window.PosPrinting.openDrawer(printContext);
                cashFeedback(purpose === 'Receive' ? 'Đã gửi lệnh mở két nhận ca. Hãy đếm và nhập tiền đầu ca.' : 'Đã gửi lệnh mở két chốt ca. Hãy đếm và nhập tiền cuối ca.');
                opened = true;
            } catch (error) {
                shiftDrawerError.textContent = (error.message || 'Không thể mở két.') + ' Kiểm tra két trước khi thử lại. Nếu đã đếm được tiền, có thể tiếp tục nhập số tiền.';
                shiftDrawerError.hidden = false;
                shiftDrawerContinue.hidden = false;
            }
        }, 'Đang mở két…');
        shiftDrawerBusy = false;
        shiftDrawerCancel.disabled = false;
        if (opened) continueShiftCount();
    }

    function openOpenShiftModal() {
        resetDenoms('open');
        setOpenDirectInput(0);
        updateOpenShiftSummary();
        if (txtOpenNote) {
            txtOpenNote.value = '';
        }

        if (txtOpeningCash) {
            txtOpeningCash.value = 0;
        }

        if (openShiftModal) {
            openShiftModal.show();
        }
        if (txtHandoverSlipBarcode) txtHandoverSlipBarcode.value = '';
        if (selectedHandoverSlipId) selectedHandoverSlipId.value = '';
        if (handoverSlipInfo) {
            handoverSlipInfo.style.display = 'none';
            handoverSlipInfo.innerHTML = '';
        }
    }

    function openCashTxnPopup() {
        if (cashTxnAmount) cashTxnAmount.value = '0';
        if (cashTxnReason) cashTxnReason.value = '';
        if (cashTxnNote) cashTxnNote.value = '';
        setCashTxnType('2');

        if (cashTxnModal) {
            cashTxnModal.show();
        }
    }

    function closeDraftContext() {
        return {storeId:printContext.storeId,userId:printContext.userId,terminalId:printContext.terminalId,shiftId:activeShift?.id};
    }
    function reviewShiftDifference() {
        if (!activeShift) return;
        try {
            window.PosShiftCloseDraft.save(closeDraftContext(), {
                actual:getRawMoney(txtClosingCashActual?.value),note:txtCloseNote?.value || '',
                denominations:getDenomInputs('close').map(x=>({value:Number(x.dataset.denom),quantity:Number(x.value || 0)}))
            });
            window.location.assign(`/admin/pos-shift/reconciliation?shiftId=${activeShift.id}`);
        } catch(error) {showError(error.message || 'Chưa lưu được bảng kiểm đếm. Vui lòng kiểm tra trình duyệt.');}
    }

    function openCloseShiftPopup(restoreCount = false) {
        resetDenoms('close');

        if (txtCloseNote) {
            txtCloseNote.value = '';
        }
        if (txtClosingCashActual) {
            txtClosingCashActual.value = '0';
        }

        if (restoreCount && activeShift) {
            try {
                const count = window.PosShiftCloseDraft.read(closeDraftContext());
                if (count) {
                    getDenomInputs('close').forEach(input => {
                        input.value = count.denominations.find(x=>x.value === Number(input.dataset.denom))?.quantity || 0;
                    });
                    calcDenomTotal('close');
                    if (txtClosingCashActual) txtClosingCashActual.value = formatMoney(count.actual);
                    if (txtCloseNote) txtCloseNote.value = count.note;
                }
            } catch(error) {showError(error.message || 'Không đọc được kiểm đếm đã lưu. Vui lòng kiểm đếm lại.');}
        }

        updateCloseShiftSummary();
        renderCloseShiftCompare();

        if (closeShiftModal) {
            closeShiftModal.show();
        }

    }

    // =========================
    // Load data
    // =========================
    async function loadWarehouses() {
        try {
            const list = await fetchJson('/admin/api/warehouses');

            if (!ddlWarehouse) return;

            ddlWarehouse.innerHTML = '<option value="">-- Chọn kho --</option>';

            (list || []).forEach(w => {
                const opt = document.createElement('option');
                opt.value = w.id;
                opt.textContent = `${w.code} - ${w.name}`;

                // Lưu cờ mặc định để chọn tự động
                if (w.isDefault === true || w.isDefaultForSale === true) {
                    opt.dataset.isDefault = 'true';
                }

                ddlWarehouse.appendChild(opt);
            });

            // Ưu tiên kho mặc định
            const defaultWarehouse = (list || []).find(w =>
                w.isDefault === true ||
                w.isDefaultForSale === true ||
                w.isDefaultWarehouse === true
            );

            if (defaultWarehouse) {
                ddlWarehouse.value = defaultWarehouse.id;
                return;
            }

            // Nếu chỉ có 1 kho thì tự chọn
            if ((list || []).length === 1) {
                ddlWarehouse.value = list[0].id;
            }
        } catch (err) {
            window.PosError.handle(err);
        }
    }

    // =========================
    // Cash transactions
    // =========================
    function renderCashTransactions(items) {
        cashTxnAllItems = Array.isArray(items) ? items : [];
        cashTxnPage = 1;
        renderCashTransactionPage();
    }

    function renderCashTransactionEmpty() {
        if (!cashTxnBody) return;

        cashTxnBody.innerHTML = `
            <tr>
                <td colspan="6" class="text-center text-muted py-4">
                    Chưa có giao dịch.
                </td>
            </tr>`;
    }

    function renderCashTransactionPage() {
        if (!cashTxnBody || !cashTxnPagerInfo || !btnCashTxnPrev || !btnCashTxnNext) return;

        const total = cashTxnAllItems.length;
        const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

        cashTxnPage = Math.min(Math.max(1, cashTxnPage), totalPages);

        const start = (cashTxnPage - 1) * PAGE_SIZE;
        const pageItems = cashTxnAllItems.slice(start, start + PAGE_SIZE);

        if (!pageItems.length) {
            renderCashTransactionEmpty();
        } else {
            cashTxnBody.innerHTML = pageItems.map(x => {
                const isIn = isCashInType(x.type);

                return `
                    <tr>
                        <td>
                            <div class="cash-time">${formatDateTime(x.createdAtUtc)}</div>
                        </td>
                        <td>
                            <span class="shift-type-badge ${isIn ? 'in' : 'out'}">
                                ${getCashTypeText(x.type)}
                            </span>
                        </td>
                        <td class="${isIn ? 'shift-money-positive' : 'shift-money-negative'} cash-amount">
                            ${formatMoney(x.amount)}
                        </td>
                        <td class="cash-reason">${window.PosShiftCashPrinting.escape(x.reason || '-')}</td>
                        <td class="cash-note">${window.PosShiftCashPrinting.escape(x.note || '-')}</td>
                        <td><button type="button" class="btn btn-sm btn-outline-primary" data-print-cash="${Number(x.id)}">In lại</button>
                            <a class="btn btn-sm btn-outline-secondary mt-1" href="/admin/pos-shift/requests?tab=cash&transactionId=${Number(x.id)}">${x.pendingAdjustmentId ? 'Chờ duyệt điều chỉnh' : 'Yêu cầu sửa / hủy'}</a></td>
                    </tr>
                `;
            }).join('');
        }

        if (total === 0) {
            cashTxnPagerInfo.textContent = '0 giao dịch';
        } else {
            const from = start + 1;
            const to = Math.min(start + PAGE_SIZE, total);
            cashTxnPagerInfo.textContent = `${from}-${to} / ${total} giao dịch`;
        }

        btnCashTxnPrev.disabled = cashTxnPage <= 1;
        btnCashTxnNext.disabled = cashTxnPage >= totalPages;
    }

    async function loadCashTransactions() {
        try {
            const items = await fetchJson('/admin/pos/shift/cash-transactions');
            renderCashTransactions(items || []);
        } catch (err) {
            renderCashTransactions([]);
            window.PosError.handle(err);
        }
    }

    async function loadCurrentShift() {
        setBadgeLoading();

        try {
            const shift = await fetchJson('/admin/pos/shift/current');

            if (!shift) {
                renderNoOpenShift();
                return null;
            }

            renderOpenShift(shift);
            await loadCashTransactions();
            return shift;
        } catch (err) {
            renderNoOpenShift();
            window.PosError.handle(err, { showToast: false });
        }
    }

    // =========================
    // Submit actions
    // =========================
    async function openShift() {
        const warehouseId = parseInt((ddlWarehouse && ddlWarehouse.value) || '0', 10);

        if (!warehouseId) {
            showError('Vui lòng chọn kho bán hàng');
            return;
        }

        await withButtonLoading(btnOpenShift, async () => {
            try {
                await postJson('/admin/pos/shift/open', {
                    warehouseId: warehouseId,
                    openingCash: getOpenDirectInputValue(),
                    note: (txtOpenNote && txtOpenNote.value) || null,
                    denominations: getDenominationPayload('open'),

                    handoverSlipId: selectedHandoverSlipId && selectedHandoverSlipId.value
                        ? parseInt(selectedHandoverSlipId.value, 10)
                        : null,

                    handoverBarcodeValue: txtHandoverSlipBarcode && txtHandoverSlipBarcode.value
                        ? txtHandoverSlipBarcode.value.trim()
                        : null
                });
                showSuccess('Đã mở ca thành công');

                if (receivingForPos) {
                    returnToPos();
                    return;
                }

                if (openShiftModal) {
                    openShiftModal.hide();
                }

                await loadCurrentShift();
                switchShiftTab('summaryTab');
            } catch (err) {
                window.PosError.handle(err);
            }
        }, 'Đang mở ca');
    }

    async function addCashTransaction() {
        await withButtonLoading(btnAddCashTxn, async () => {
            try {
                const amount = parseCashTxnAmount((cashTxnAmount && cashTxnAmount.value) || '0');
                if (!Number.isFinite(amount) || amount <= 0) {
                    throw new Error('Vui lòng nhập số tiền lớn hơn 0.');
                }
                const saved = await postJson('/admin/pos/shift/cash-transaction', {
                    type: parseInt((cashTxnType && cashTxnType.value) || '0', 10),
                    amount: amount,
                    reason: (cashTxnReason && cashTxnReason.value) || null,
                    note: (cashTxnNote && cashTxnNote.value) || null
                });

                showSuccess('Đã ghi nhận thu / chi tiền mặt');

                if (cashTxnModal) {
                    cashTxnModal.hide();
                }

                await printCashTransaction(saved, true);

                await loadCurrentShift();
                await loadCashTransactions();
                switchShiftTab('historyTab');
            } catch (err) {
                window.PosError.handle(err);
            }
        }, 'Đang ghi nhận');
    }
    function cashFeedback(message, failed = false) {
        const feedback = document.getElementById('shiftCashFeedback');
        if (feedback) { feedback.textContent = message; feedback.className = 'alert ' + (failed ? 'alert-warning' : 'alert-success'); }
    }
    async function printCashTransaction(transaction, immediate = false) {
        try {
            const result = await window.PosShiftCashPrinting.print(transaction, printContext, immediate === true);
            cashFeedback(result.mode === 'helper' ? `Đã gửi phiếu #${transaction.id} tới Print Helper.` : `Đã gửi lệnh in phiếu #${transaction.id}.`);
        } catch (error) {
            cashFeedback(`Phiếu #${transaction.id} đã được lưu. Chưa xác nhận được kết quả in. ${error.message} Có thể bấm In lại trong lịch sử; in lại không mở két.`, true);
        }
    }
    async function openCashDrawer() {
        const reason = (drawerReason?.value || '').trim();
        if (!reason || reason.length > 300) { drawerError.textContent = 'Nhập lý do mở két từ 1 đến 300 ký tự.'; drawerReason?.focus(); return; }
        if (!activeShift) { drawerError.textContent = 'Vui lòng mở ca trước khi mở két.'; return; }
        await withButtonLoading(btnOpenCashDrawer, async () => {
            drawerError.textContent = '';
            btnShowDrawerModal.disabled = true;
            try {
                await window.PosPrinting.drawerReady(printContext);
                const recorded = await postJson('/admin/pos/shift/cash-drawer', {shiftId:activeShift.id,reason});
                if (!(recorded?.auditId > 0) || recorded.shiftId !== activeShift.id) throw new Error('Chưa xác nhận lưu lý do mở két.');
                await window.PosPrinting.openDrawer(printContext);
                drawerModal?.hide(); drawerReason.value = '';
                cashFeedback('Đã lưu lý do và gửi lệnh mở két.');
            } catch (error) {
                drawerError.textContent = error.message || 'Không thể mở két. Vui lòng kiểm tra kết nối.';
                cashFeedback(drawerError.textContent, true);
            } finally { btnShowDrawerModal.disabled = false; }
        }, 'Đang mở két');
    }
    function confirmCloseShiftWithDiff(diff) {
        return new Promise(resolve => {
            const isOver = diff > 0;
            const amount = formatMoney(Math.abs(diff));

            const html = `
            <div class="modal fade" id="shiftCloseConfirmModal" tabindex="-1" aria-hidden="true">
                <div class="modal-dialog modal-dialog-centered">
                    <div class="modal-content shift-confirm-modal">
                        <div class="modal-header">
                            <div>
                                <h5 class="modal-title">
                                    ${isOver ? 'Thừa quỹ cuối ca' : 'Thiếu quỹ cuối ca'}
                                </h5>
                                <div class="shift-modal-subtitle">
                                    Vui lòng kiểm tra lại tiền thực đếm trước khi đóng ca.
                                </div>
                            </div>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>

                        <div class="modal-body">
                            <div class="shift-confirm-icon ${isOver ? 'over' : 'short'}">
                                <i class="bx ${isOver ? 'bx-trending-up' : 'bx-trending-down'}"></i>
                            </div>

                            <div class="shift-confirm-title">
                                ${isOver ? 'Tiền thực đếm đang THỪA' : 'Tiền thực đếm đang THIẾU'}
                            </div>

                            <div class="shift-confirm-amount">${amount}</div>

                            <div class="shift-confirm-desc">
                                Bạn vẫn muốn đóng ca với số tiền thực đếm hiện tại?
                            </div>
                        </div>

                        <div class="modal-footer">
                            <button type="button" class="btn btn-light shift-modal-btn" data-bs-dismiss="modal">
                                Kiểm tra lại
                            </button>
                            <button type="button" class="btn btn-outline-primary shift-modal-btn" id="btnReviewConfirmedShiftDifference">
                                Kiểm tra lệch ca
                            </button>
                            <button type="button" class="btn btn-danger shift-modal-btn" id="btnConfirmCloseShiftDiff">
                                Vẫn đóng ca
                            </button>
                        </div>
                    </div>
                </div>
            </div>`;

            const old = document.getElementById('shiftCloseConfirmModal');
            if (old) old.remove();

            document.body.insertAdjacentHTML('beforeend', html);

            const modalEl = document.getElementById('shiftCloseConfirmModal');
            const modal = new bootstrap.Modal(modalEl);

            modalEl.addEventListener('hidden.bs.modal', function () {
                modalEl.remove();
                resolve(false);
            }, { once: true });

            document.getElementById('btnConfirmCloseShiftDiff').addEventListener('click', function () {
                resolve(true);
                modal.hide();
            }, { once: true });

            document.getElementById('btnReviewConfirmedShiftDifference').addEventListener('click', function () {
                resolve(false);
                modal.hide();
                reviewShiftDifference();
            }, { once: true });

            modal.show();
        });
    }
    async function closeShift() {
        const expected = getExpectedClosingCashValue();
        const actual = getRawMoney(txtClosingCashActual && txtClosingCashActual.value);
        const diff = actual - expected;

        if (diff !== 0) {
            const ok = await confirmCloseShiftWithDiff(diff);
            if (!ok) return;
        }

        await withButtonLoading(btnCloseShift, async () => {
            try {
                const result = await postJson('/admin/pos/shift/close', {
                    closingCashActual: actual,
                    note: (txtCloseNote && txtCloseNote.value) || null,
                    denominations: getDenominationPayload('close')
                });

                showSuccess('Đã đóng ca thành công');

                try { window.PosShiftCloseDraft?.clear(closeDraftContext()); } catch { /* A stale browser draft cannot change a saved closing. */ }

                if (result && result.id) {
                    window.open(`/admin/pos-shift/closing-slip-print?shiftId=${result.id}`, '_blank');
                }

                if (closeShiftModal) {
                    closeShiftModal.hide();
                }

                await loadCurrentShift();
            } catch (err) {
                window.PosError.handle(err);
            }
        }, 'Đang đóng ca');
    }

    // =========================
    // Events
    // =========================
    function bindEvents() {

        if (btnLoadHandoverSlip) {
            btnLoadHandoverSlip.addEventListener('click', loadHandoverSlipByBarcode);
        }

        if (txtHandoverSlipBarcode) {
            txtHandoverSlipBarcode.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    loadHandoverSlipByBarcode();
                }
            });
        }
        document.querySelectorAll('[data-clear-denom]').forEach(btn => {
            btn.addEventListener('click', function () {
                const denom = this.dataset.clearDenom;
                const prefix = this.dataset.prefix;
                const input = document.getElementById(`${prefix}Qty_${denom}`);

                if (!input) return;

                input.value = 0;
                calcDenomTotal(prefix);
                input.focus();
                input.select();
            });
        });

        document.querySelectorAll('[data-open-preset]').forEach(btn => {
            btn.addEventListener('click', function () {
                const val = this.dataset.openPreset;

                if (val === 'custom') {
                    if (txtOpeningCash) {
                        txtOpeningCash.focus();
                        txtOpeningCash.select();
                    }
                    return;
                }

                const amount = Number(val || 0);
                setOpenDirectInput(amount);
                autoConvertOpenCash(amount);
            });
        });

        const btnAutoConvertOpenCash = document.getElementById('btnAutoConvertOpenCash');

        if (btnAutoConvertOpenCash) {
            btnAutoConvertOpenCash.addEventListener('click', function () {
                const amount = getOpenDirectInputValue();

                if (amount <= 0) {
                    showError('Vui lòng nhập tổng tiền đầu ca trước khi tự động quy đổi.');
                    return;
                }

                autoConvertOpenCash(amount);
            });
        }

        document.querySelectorAll('[data-keypad]').forEach(btn => {
            btn.addEventListener('click', function () {
                if (!txtOpeningCash) return;

                const key = this.dataset.keypad;
                let raw = getOpenDirectInputValue().toString();

                if (key === 'clear') {
                    raw = '0';
                } else if (key === 'back') {
                    raw = raw.length > 1 ? raw.slice(0, -1) : '0';
                } else if (key === 'plus' || key === 'minus' || key === ',') {
                    return;
                } else {
                    raw = raw === '0' ? key : raw + key;
                }

                const amount = Number(raw || 0);
                setOpenDirectInput(amount);
                updateOpenShiftSummary();
            });
        });

        const btnResetOpenDenomsTop = document.getElementById('btnResetOpenDenomsTop');

        if (btnResetOpenDenomsTop) {
            btnResetOpenDenomsTop.addEventListener('click', function () {
                resetDenoms('open');
                focusFirstDenom('open');
            });
        }
        const noteCounter = document.querySelector('.shift-note-counter');

        if (txtOpenNote && noteCounter) {
            txtOpenNote.addEventListener('input', function () {
                noteCounter.textContent = `${this.value.length}/200`;
            });
        }
        document.querySelectorAll('.shift-tab-btn').forEach(btn => {
            btn.addEventListener('click', function () {
                switchShiftTab(this.dataset.shiftTab);
            });
        });

        if (btnCashTxnPrev) {
            btnCashTxnPrev.addEventListener('click', function () {
                if (cashTxnPage > 1) {
                    cashTxnPage--;
                    renderCashTransactionPage();
                }
            });
        }

        if (btnCashTxnNext) {
            btnCashTxnNext.addEventListener('click', function () {
                const totalPages = Math.max(1, Math.ceil(cashTxnAllItems.length / PAGE_SIZE));

                if (cashTxnPage < totalPages) {
                    cashTxnPage++;
                    renderCashTransactionPage();
                }
            });
        }

        if (btnShowOpenModal) btnShowOpenModal.addEventListener('click', () => confirmShiftDrawer('Receive'));
        if (btnShowCashTxnModal) btnShowCashTxnModal.addEventListener('click', openCashTxnPopup);
        if (btnShowCloseModal) btnShowCloseModal.addEventListener('click', () => confirmShiftDrawer('Close'));
        shiftDrawerEl?.addEventListener('shown.bs.modal', () => { shiftDrawerConfirm.disabled = false; shiftDrawerConfirm.focus(); });
        shiftDrawerEl?.addEventListener('hide.bs.modal', event => { if (shiftDrawerBusy) event.preventDefault(); });
        shiftDrawerEl?.addEventListener('hidden.bs.modal', () => {
            const next = shiftDrawerNext;
            shiftDrawerPurpose = null;
            shiftDrawerNext = null;
            next?.();
        });
        shiftDrawerConfirm?.addEventListener('click', confirmShiftDrawerOpen);
        shiftDrawerContinue?.addEventListener('click', continueShiftCount);
        btnShowDrawerModal?.addEventListener('click', () => {
            drawerReason.value = ''; drawerError.textContent = ''; btnOpenCashDrawer.disabled = true; drawerModal?.show();
        });
        drawerModalEl?.addEventListener('shown.bs.modal', () => { btnOpenCashDrawer.disabled = false; drawerReason?.focus(); });
        btnOpenCashDrawer?.addEventListener('click', openCashDrawer);
        cashTxnBody?.addEventListener('click', event => {
            const button = event.target.closest('[data-print-cash]');
            const transaction = button && cashTxnAllItems.find(item => item.id === Number(button.dataset.printCash));
            if (transaction) withButtonLoading(button, () => printCashTransaction(transaction, false), 'Đang in');
        });

        cashTxnTypeButtons.forEach(button => {
            button.addEventListener('click', function () {
                setCashTxnType(this.dataset.cashTxnType || '1');

                if (cashTxnAmount) {
                    cashTxnAmount.focus();
                    cashTxnAmount.select();
                }
            });
        });

        if (cashTxnType) {
            cashTxnType.addEventListener('change', syncCashTxnTypePresentation);
        }

        if (btnOpenShift) btnOpenShift.addEventListener('click', openShift);
        if (btnAddCashTxn) btnAddCashTxn.addEventListener('click', addCashTransaction);
        if (btnCloseShift) btnCloseShift.addEventListener('click', closeShift);

        if (btnResetOpenDenoms) {
            btnResetOpenDenoms.addEventListener('click', function () {
                resetDenoms('open');
                focusFirstDenom('open');
            });
        }

        if (btnResetCloseDenoms) {
            btnResetCloseDenoms.addEventListener('click', function () {
                resetDenoms('close');
                focusFirstDenom('close');
            });
        }

        if (txtOpeningCash) {
            txtOpeningCash.addEventListener('focus', function () {
                this.select();
            });

            txtOpeningCash.addEventListener('input', function () {
                const amount = getOpenDirectInputValue();
                setOpenDirectInput(amount);
                updateOpenShiftSummary();
            });

            txtOpeningCash.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    autoConvertOpenCash(getOpenDirectInputValue());
                }
            });
        }

        if (txtClosingCashActual) {
            txtClosingCashActual.addEventListener('focus', function () {
                this.select();
            });

            txtClosingCashActual.addEventListener('input', function () {
                const amount = getRawMoney(this.value);
                this.value = formatMoney(amount);
                updateCloseShiftSummary();
            });
        }

        if (cashTxnAmount) {
            cashTxnAmount.addEventListener('focus', function () {
                this.select();
            });

            cashTxnAmount.addEventListener('input', function () {
                formatCashTxnAmountInput(this);
            });

            cashTxnAmount.addEventListener('beforeinput', function (event) {
                const start = this.selectionStart;
                if (start !== this.selectionEnd) return;
                // Delete the adjacent digit together with a cosmetic grouping separator.
                if (event.inputType === 'deleteContentBackward' && this.value[start - 1] === '.') {
                    event.preventDefault();
                    this.setRangeText('', start - 2, start, 'end');
                } else if (event.inputType === 'deleteContentForward' && this.value[start] === '.') {
                    event.preventDefault();
                    this.setRangeText('', start, start + 2, 'end');
                } else return;
                formatCashTxnAmountInput(this);
            });

            cashTxnAmount.addEventListener('paste', function (event) {
                const text = event.clipboardData?.getData('text');
                if (!text) return;
                event.preventDefault();
                this.setRangeText(normalizeCashTxnPastedAmount(text), this.selectionStart, this.selectionEnd, 'end');
                formatCashTxnAmountInput(this);
            });
        }

        [cashTxnAmount, cashTxnReason, cashTxnNote].forEach(el => {
            if (!el) return;

            el.addEventListener('focus', function () {
                if (this.select) this.select();
            });
        });

        if (cashTxnType) {
            cashTxnType.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    if (cashTxnAmount) {
                        cashTxnAmount.focus();
                        cashTxnAmount.select();
                    }
                }
            });
        }

        if (cashTxnAmount) {
            cashTxnAmount.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    if (cashTxnReason) {
                        cashTxnReason.focus();
                        cashTxnReason.select();
                    }
                }
            });
        }

        if (cashTxnReason) {
            cashTxnReason.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    if (cashTxnNote) {
                        cashTxnNote.focus();
                        cashTxnNote.select();
                    }
                }
            });
        }

        if (txtOpenNote) {
            txtOpenNote.addEventListener('keydown', function (e) {
                if (e.ctrlKey && e.key === 'Enter') {
                    e.preventDefault();
                    openShift();
                }
            });
        }

        if (txtCloseNote) {
            txtCloseNote.addEventListener('keydown', function (e) {
                if (e.ctrlKey && e.key === 'Enter') {
                    e.preventDefault();
                    closeShift();
                }
            });
        }

        if (openShiftModalEl) {
            openShiftModalEl.addEventListener('keydown', function (e) {
                if (e.ctrlKey && e.key === 'Enter') {
                    e.preventDefault();
                    openShift();
                }
            });

            openShiftModalEl.addEventListener('shown.bs.modal', function () {
                focusFirstDenom('open');
            });
        }

        if (cashTxnModalEl) {
            cashTxnModalEl.addEventListener('keydown', function (e) {
                if (e.ctrlKey && e.key === 'Enter') {
                    e.preventDefault();
                    addCashTransaction();
                    return;
                }

                if (e.key === 'Enter' && e.target.tagName !== 'TEXTAREA') {
                    const fields = [cashTxnType, cashTxnAmount, cashTxnReason, cashTxnNote].filter(Boolean);
                    const currentIndex = fields.indexOf(e.target);

                    if (currentIndex >= 0 && currentIndex < fields.length - 1) {
                        e.preventDefault();
                        fields[currentIndex + 1].focus();

                        if (fields[currentIndex + 1].select) {
                            fields[currentIndex + 1].select();
                        }
                    }
                }
            });

            cashTxnModalEl.addEventListener('shown.bs.modal', function () {
                syncCashTxnTypePresentation();

                if (cashTxnAmount) {
                    cashTxnAmount.focus();
                    cashTxnAmount.select();
                }
            });
        }

        if (closeShiftModalEl) {
            closeShiftModalEl.addEventListener('keydown', function (e) {
                if (e.ctrlKey && e.key === 'Enter') {
                    e.preventDefault();
                    closeShift();
                }
            });

            closeShiftModalEl.addEventListener('shown.bs.modal', function () {
                renderCloseShiftCompare();
                focusFirstDenom('close');
            });
        }
    }

    // =========================
    // Boot
    // =========================
    document.addEventListener('DOMContentLoaded', async function () {
        initDenomPanels();
        bindEvents();
        syncCashTxnTypePresentation();
        await loadWarehouses();
        const currentShift = await loadCurrentShift();
        btnReviewShiftDifference?.addEventListener('click', reviewShiftDifference);
        if (entryQuery.get('resumeClose') === '1') {
            if (currentShift?.id === Number(entryQuery.get('shiftId'))) openCloseShiftPopup(true);
            else showError('Ca cần đóng đã thay đổi hoặc đã kết thúc. Bảng kiểm đếm không được áp dụng sang ca khác.');
        }
        if (receivingForPos && currentShift === null) confirmShiftDrawer('Receive');
    });
})();
