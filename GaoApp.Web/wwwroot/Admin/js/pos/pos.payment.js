window.PosPayment = (function () {
    'use strict';

    function create(deps) {
        const {
            posState,
            elements,
            modals,
            helpers
        } = deps;

        const {
            payMethod,
            payAmount,
            payReference,
            payProvider,
            btnAddPayment,
            btnFinalizeFromPaymentModal,
            btnPayExact,
            paymentModalEl
        } = elements;

        const {
            paymentModal,
            confirmModal
        } = modals;

        const {
            postJson,
            deleteJson,
            runPosAction,
            focusBarcodeInput,
            syncDraftToUi,
            showSuccess,
            showError,
            renderPaymentModalDraft,
            renderPaymentPreview,
            renderPayments,
            openConfirmModal,
            openReceiptPrint,
            requestScreenRefresh,
            applyDraftActionSuccess,
            applyScreenActionSuccess
        } = helpers;

        const {
            clearInlineError,
            setInlineError,
            registerUiLock,
            refreshUiLocks
        } = window.PosCommon;

        let currentPaymentQr = null;
        let customerDisplayConnection = null;
        let customerDisplayJoined = false;
        let previewSendTimer = null;
        const SUCCESS_HOLD_MS = 4500;
        const INFO_HOLD_MS = 3600;
        const RESET_AFTER_SUCCESS_MS = 4700;

        let pendingFinalizeCustomerPayload = null;
        function ensurePaymentErrorBox() {
            if (!paymentModalEl) return null;

            let box = paymentModalEl.querySelector('#paymentInlineErrorBox');
            if (box) return box;

            box = document.createElement('div');
            box.id = 'paymentInlineErrorBox';
            box.style.display = 'none';

            const errorHost =
                paymentModalEl.querySelector('.pos-payment-footer__status') ||
                paymentModalEl.querySelector('.modal-body') ||
                paymentModalEl.querySelector('.modal-content') ||
                paymentModalEl;

            if (errorHost.firstChild) {
                errorHost.insertBefore(box, errorHost.firstChild);
            } else {
                errorHost.appendChild(box);
            }

            return box;
        }

        function clearPaymentInlineError() {
            clearInlineError(ensurePaymentErrorBox());
        }

        function showPaymentInlineError(message, variant) {
            setInlineError(ensurePaymentErrorBox(), message, variant || 'danger');
        }

        function refreshLocksSafe() {
            refreshUiLocks?.(posState);
        }

        function applyDraftSuccess(options) {
            return applyDraftActionSuccess({
                draft: options?.draft,
                posState,
                syncDraftToUi,
                showSuccess,
                focusBarcodeInput,
                successMessage: options?.successMessage || '',
                focusBarcode: options?.focusBarcode !== false,
                afterSync: options?.afterSync || null
            });
        }

        async function applyScreenSuccess(options) {
            await applyScreenActionSuccess({
                requestScreenRefresh,
                showSuccess,
                successMessage: options?.successMessage || '',
                reason: options?.reason || 'payment-screen-action-success',
                silent: !!options?.silent,
                force: options?.force !== false,
                focusBarcode: options?.focusBarcode !== false,
                beforeRefresh: options?.beforeRefresh || null,
                afterSuccess: options?.afterSuccess || null
            });
        }

        function getCurrentMethod() {
            return parseInt(payMethod?.value || '0', 10);
        }

        function isReferenceRequired(method) {
            const m = parseInt(method || '0', 10);
            return m === 1 || m === 2 || m === 3;
        }

        function getAddPaymentButtonText() {
            const method = getCurrentMethod();

            switch (method) {
                case 0:
                    return 'Ghi nhận tiền mặt';
                case 1:
                    return 'Ghi nhận chuyển khoản';
                case 2:
                    return 'Ghi nhận thẻ';
                case 3:
                    return 'Ghi nhận ví điện tử';
                default:
                    return 'Ghi nhận thanh toán';
            }
        }

        function getCurrentDraft() {
            return posState?.business?.currentDraft || posState?.currentDraft || null;
        }

        function getDraftBalance(draft) {
            if (!draft) return 0;

            // Ưu tiên field backend đã tính sẵn.
            // DTO hiện tại của POS đang dùng balanceDue / remainingAmount.
            const directBalance = Number(
                draft?.balanceDue ??
                draft?.remainingAmount ??
                draft?.balance
            );

            if (Number.isFinite(directBalance)) {
                return directBalance > 0 ? directBalance : 0;
            }

            // Fallback nếu backend không trả balance.
            // Lưu ý: dùng paidTotal / paidAmount, không dùng draft.paid.
            const grandTotal = Number(draft?.grandTotal || 0);
            const paidTotal = Number(
                draft?.paidTotal ??
                draft?.paidAmount ??
                draft?.paid ??
                0
            );

            const numeric = grandTotal - paidTotal;

            if (!Number.isFinite(numeric)) {
                return 0;
            }

            return numeric > 0 ? numeric : 0;
        }
        function formatMoneyLocal(value) {
            const n = Number(value || 0);
            return Number.isFinite(n) ? n.toLocaleString('vi-VN') : '0';
        }
        function updateAddPaymentButtonText() {
            if (!btnAddPayment) return;
            btnAddPayment.textContent = getAddPaymentButtonText();
        }

        function toggleReferenceFieldsByMethod() {
            const method = getCurrentMethod();
            const requireReference = isReferenceRequired(method);

            if (payReference) {
                payReference.disabled = !requireReference;
                payReference.closest('.form-group, .mb-3, .col, .col-md-6, .col-lg-6')?.classList
                    ?.toggle('is-disabled', !requireReference);

                if (!requireReference) {
                    payReference.value = '';
                }
            }

            if (payProvider) {
                const shouldEnableProvider = method === 1 || method === 2 || method === 3;
                payProvider.disabled = !shouldEnableProvider;
                payProvider.closest('.form-group, .mb-3, .col, .col-md-6, .col-lg-6')?.classList
                    ?.toggle('is-disabled', !shouldEnableProvider);

                if (!shouldEnableProvider) {
                    payProvider.value = '';
                }
            }
        }
        function getPosShellDataset() {
            const shell = document.getElementById('posShell');
            return shell?.dataset || {};
        }

        async function ensureCustomerDisplayConnection() {
            if (!window.signalR) return null;

            if (customerDisplayConnection &&
                customerDisplayConnection.state === signalR.HubConnectionState.Connected &&
                customerDisplayJoined) {
                return customerDisplayConnection;
            }

            if (!customerDisplayConnection) {
                customerDisplayConnection = new signalR.HubConnectionBuilder()
                    .withUrl('/hubs/pos')
                    .withAutomaticReconnect([0, 1000, 2000, 5000])
                    .build();

                customerDisplayConnection.onreconnected(async function () {
                    customerDisplayJoined = false;
                    await joinCustomerDisplayGroup();
                });

                customerDisplayConnection.onclose(function () {
                    customerDisplayJoined = false;
                });
            }

            if (customerDisplayConnection.state === signalR.HubConnectionState.Disconnected) {
                await customerDisplayConnection.start();
            }

            await joinCustomerDisplayGroup();

            return customerDisplayConnection;
        }

        async function joinCustomerDisplayGroup() {
            if (!customerDisplayConnection || customerDisplayJoined) return;

            const ds = getPosShellDataset();
            const storeId = Number(ds.storeId || 0);
            const terminalId = ds.terminalId || '';

            if (!storeId || !terminalId) return;

            await customerDisplayConnection.invoke('JoinStoreGroup', storeId, terminalId);
            customerDisplayJoined = true;
        }

        async function sendCustomerDisplayEvent(eventType, payload) {
            try {
                const conn = await ensureCustomerDisplayConnection();
                if (!conn) return;

                await conn.invoke('BroadcastTerminalEvent', {
                    eventType: eventType,
                    payload: payload || {}
                });
            } catch (err) {
                console.warn('Send customer display event failed', eventType, err);
            }
        }

        function sendPaymentPreviewToCustomerDisplayDebounced() {
            clearTimeout(previewSendTimer);

            previewSendTimer = setTimeout(function () {
                sendPaymentPreviewToCustomerDisplay();
            }, 40);
        }

        function sendPaymentPreviewToCustomerDisplay() {
            const draft = getCurrentDraft();
            if (!draft) return;

            const method = getCurrentMethod();
            const amount = Number(payAmount?.value || 0);
            const balance = getDraftBalance(draft);

            const expectedChange = Math.max(0, amount - balance);
            const expectedBalance = Math.max(0, balance - amount);

            sendCustomerDisplayEvent('customer_payment_preview', {
                method: method,
                amount: Number.isFinite(amount) ? amount : 0,
                balance: balance,
                expectedBalance: expectedBalance,
                expectedChange: expectedChange,
                grandTotal: Number(draft.grandTotal || 0),
                paidTotal: Number(draft.paidTotal || draft.paidAmount || 0)
            });
        }

        function hidePaymentPreviewOnCustomerDisplay() {
            sendCustomerDisplayEvent('customer_payment_hide', {});
        }
        function showCustomerPaymentSuccess(payload) {
            sendCustomerDisplayEvent('customer_payment_success', payload || {
                durationMs: 1800
            });
        }

        function resetCustomerDisplayAfterFinalize(payload) {
            sendCustomerDisplayEvent('customer_display_reset', payload || {});
        }
        function setPayAmountExact(options) {
            const draft = getCurrentDraft();
            if (!draft || !payAmount) return;

            // Thu đủ = số tiền còn thiếu, không phải tổng tiền hàng
            const balance = getDraftBalance(draft);

            payAmount.value = balance;

            updatePayExactAmountText(balance);
            clearPaymentInlineError();
            renderPaymentPreview(posState);
            sendPaymentPreviewToCustomerDisplayDebounced();
            if (options?.focus !== false) {
                focusPayAmountInput();
            }
        }
        function updatePayExactAmountText(value) {
            const amount = Number(value || 0);
            const safeAmount = Number.isFinite(amount) && amount > 0 ? amount : 0;

            // Text số trên nút Thu đủ
            const exactAmountEl = document.getElementById('payExactAmount');
            if (exactAmountEl) {
                exactAmountEl.textContent = safeAmount.toLocaleString('vi-VN');
            }

            // Nếu nút có data-pay-amount thì cập nhật luôn để click không lấy số cũ
            if (btnPayExact) {
                btnPayExact.setAttribute('data-pay-amount', String(safeAmount));
            }
        }
        function focusPayAmountInput() {
            setTimeout(() => {
                payAmount?.focus();
                payAmount?.select?.();
            }, 0);
        }

        function setPayAmountQuick(amount) {
            if (!payAmount) return;

            const value = Number(amount || 0);
            payAmount.value = Number.isFinite(value) ? value : 0;

            clearPaymentInlineError();
            renderPaymentPreview(posState);
            sendPaymentPreviewToCustomerDisplayDebounced();
            payAmount.focus();
            payAmount.select();
        }

        function increasePayAmount(amount) {
            if (!payAmount) return;

            const current = parseFloat(payAmount.value || '0');
            const plus = Number(amount || 0);

            const next =
                (Number.isFinite(current) ? current : 0) +
                (Number.isFinite(plus) ? plus : 0);

            payAmount.value = next > 0 ? next : 0;

            clearPaymentInlineError();
            renderPaymentPreview(posState);
            sendPaymentPreviewToCustomerDisplayDebounced();
            payAmount.focus();
            payAmount.select();
        }

        function onPayMethodChanged() {
            clearPaymentInlineError();
            updateAddPaymentButtonText();
            toggleReferenceFieldsByMethod();
            togglePaymentQrBox();

            // Luôn đưa về số tiền còn thiếu để cashier thao tác nhanh hơn
            setPayAmountExact({ focus: true });
            sendPaymentPreviewToCustomerDisplayDebounced();
        }

        function resetPaymentForm() {
            if (payMethod) payMethod.value = '0';
            if (payAmount) payAmount.value = '';
            if (payReference) payReference.value = '';
            if (payProvider) payProvider.value = '';

            clearPaymentInlineError();
            updateAddPaymentButtonText();
            toggleReferenceFieldsByMethod();
            togglePaymentQrBox();
            renderPaymentPreview(posState);
        }

        function openPaymentModal() {
            const draft = getCurrentDraft();

            if (!draft) {
                showError('Chưa có giỏ hiện tại.');
                return;
            }

            if (payMethod) payMethod.value = '0';
            if (payReference) payReference.value = '';
            if (payProvider) payProvider.value = '';

            clearPaymentInlineError();
            updateAddPaymentButtonText();
            toggleReferenceFieldsByMethod();
            togglePaymentQrBox();
            renderPaymentModalDraft(draft);
            setPayAmountExact({ focus: false });

            paymentModal?.show();

            // Fallback: phòng trường hợp sự kiện shown.bs.modal không chạy vì lỗi trình duyệt / cache
            setTimeout(() => {
                focusPayAmountInput();
            }, 350);
        }

        function validateAddPaymentInput() {
            const draft = getCurrentDraft();
            const method = getCurrentMethod();
            const amount = parseFloat(payAmount?.value || '0');
            const reference = (payReference?.value || '').trim();

            if (!draft) {
                return 'Chưa có giỏ hiện tại.';
            }

            if (Number.isNaN(amount) || amount <= 0) {
                return 'Vui lòng nhập số tiền thanh toán hợp lệ.';
            }

            if (isReferenceRequired(method) && !reference) {
                return 'Phương thức này yêu cầu mã tham chiếu.';
            }

            return '';
        }

        async function addPayment(options) {
            clearPaymentInlineError();

            const validationMessage = validateAddPaymentInput();

            if (validationMessage) {

                showPaymentInlineError(validationMessage, 'warning');

                if (validationMessage.includes('số tiền')) {
                    payAmount?.focus();
                }
                else if (validationMessage.includes('mã tham chiếu')) {
                    payReference?.focus();
                }

                return;
            }

            const method = getCurrentMethod();

            const amount =
                parseFloat(payAmount?.value || '0');

            const reference =
                (payReference?.value || '').trim();

            const provider =
                (payProvider?.value || '').trim();

            return await runPosAction(
                posState,
                `payment:add:${method}`,

                async function () {

                    return await postJson(
                        '/admin/pos/cart/current/payments',
                        {
                            method: method,
                            amount: amount,
                            referenceCode: reference || null,
                            provider: provider || null
                        }
                    );
                },

                {
                    button: btnAddPayment,
                    busyText: 'Đang ghi nhận.',
                    fallbackMessage: 'Không thể thêm thanh toán.',

                    scopes: ['checkout', 'modalSubmit'],

                    conflictScopes: [
                        'cartMutate',
                        'checkout',
                        'modalSubmit'
                    ],

                    blockedMessage:
                        'POS đang xử lý thao tác thanh toán khác, chưa thể thêm thanh toán.',

                    requireOnline: true,

                    offlineMessage:
                        'Đang offline, chưa thể thêm thanh toán.',

                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',

                    inlineTarget: ensurePaymentErrorBox(),

                    clearInlineOnStart: true,

                    onSuccess: async function (draft) {

                        clearPaymentInlineError();

                        const nextBalance =
                            Number(
                                draft?.balanceDue ??
                                draft?.remainingAmount ??
                                0
                            );

                        const nextChange =
                            Number(
                                draft?.changeDue ??
                                0
                            );

                        const paidTotal =
                            Number(
                                draft?.paidTotal ??
                                draft?.paidAmount ??
                                0
                            );

                        applyDraftSuccess({
                            draft,
                            successMessage: 'Đã thêm thanh toán',
                            focusBarcode: false,

                            afterSync: function (nextDraft) {

                                renderPaymentModalDraft(nextDraft);

                                if (typeof renderPayments === 'function') {
                                    renderPayments(nextDraft.payments || []);
                                }

                                const remaining =
                                    getDraftBalance(nextDraft);

                                if (payAmount) {
                                    payAmount.value = remaining;
                                }

                                updatePayExactAmountText(remaining);

                                if (payReference) {
                                    payReference.value = '';
                                }

                                if (payProvider) {
                                    payProvider.value = '';
                                }

                                toggleReferenceFieldsByMethod();

                                renderPaymentPreview(posState);

                                // KHÔNG hide overlay ở đây nữa
                                // để màn xanh không bị tắt ngay

                                setTimeout(function () {

                                    payAmount?.focus();
                                    payAmount?.select();

                                }, 0);
                            }
                        });

                        // =========================
                        // TIỀN MẶT
                        // =========================

                        if (method === 0) {

                            // =====================
                            // CHƯA ĐỦ TIỀN
                            // =====================

                            if (nextBalance > 0) {

                                showCustomerPaymentSuccess({
                                    finalized: false,
                                    method: 'cash',

                                    paidAmount: paidTotal,

                                    remainingAmount: nextBalance,

                                    changeAmount: 0,

                                    durationMs: 3600
                                });

                                return;
                            }

                            // =====================
                            // ĐỦ / THỪA TIỀN
                            // =====================

                            pendingFinalizeCustomerPayload = {

                                finalized: true,

                                method: 'cash',

                                paidAmount: paidTotal,

                                remainingAmount: 0,

                                changeAmount: nextChange,

                                durationMs: 4500
                            };

                            setTimeout(async function () {

                                await finalizeFromPaymentModal();

                            }, 150);

                            return;
                        }

                        // =========================
                        // METHOD KHÁC
                        // =========================

                        if (nextBalance <= 0 && options?.finalizeWhenPaid === true) {
                            pendingFinalizeCustomerPayload = {
                                finalized: true,
                                method: `payment_method_${method}`,
                                paidAmount: paidTotal,
                                remainingAmount: 0,
                                changeAmount: nextChange,
                                durationMs: SUCCESS_HOLD_MS
                            };

                            // Chờ runPosAction của payment nhả checkout/modalSubmit lock
                            // rồi mới chạy finalize. Không gọi lồng trực tiếp trong onSuccess.
                            setTimeout(async function () {
                                await finalizeRecordedPayment();
                            }, 150);

                            return;
                        }

                        hidePaymentPreviewOnCustomerDisplay();
                    },

                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );
        }

        async function finalizeRecordedPayment() {
            clearPaymentInlineError();

            return await runPosAction(
                posState,
                'payment:finalizeFromModal',
                async function () {
                    return await postJson('/admin/pos/cart/current/finalize', {});
                },
                {
                    button: btnFinalizeFromPaymentModal,
                    busyText: 'Đang chốt đơn.',
                    fallbackMessage: 'Không thể chốt đơn.',
                    scopes: ['checkout', 'modalSubmit'],
                    conflictScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                    blockedMessage: 'POS đang cập nhật giỏ hoặc xử lý thanh toán khác, chưa thể chốt đơn.',
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể chốt đơn.',
                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',
                    inlineTarget: ensurePaymentErrorBox(),
                    clearInlineOnStart: true,
                    onSuccess: async function (data) {
                        clearPaymentInlineError();

                        await applyScreenSuccess({
                            successMessage: data?.message || 'Đã chốt đơn',
                            reason: 'payment-finalize-from-modal',
                            silent: false,
                            force: true,
                            focusBarcode: true,
                            beforeRefresh: async function () {
                                paymentModal?.hide();

                                const payload = pendingFinalizeCustomerPayload || {
                                    finalized: true,
                                    method: 'manual',
                                    durationMs: SUCCESS_HOLD_MS
                                };

                                pendingFinalizeCustomerPayload = null;

                                showCustomerPaymentSuccess(payload);

                                setTimeout(function () {
                                    resetCustomerDisplayAfterFinalize({
                                        reason: 'finalized',
                                        orderId: data?.orderId || null
                                    });
                                }, RESET_AFTER_SUCCESS_MS);

                                if (data?.orderId) {
                                    openReceiptPrint(data.orderId, '80', true);
                                }
                            }
                        });
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );
        }

        async function finalizeFromPaymentModal() {
            clearPaymentInlineError();

            const draft = getCurrentDraft();
            const balance = getDraftBalance(draft);

            // Số tiền trong input mới chỉ là preview. Khi cashier bấm Chốt đơn,
            // phải ghi payment trước rồi mới finalize; nếu không backend sẽ đúng
            // khi từ chối vì PaidTotal vẫn bằng 0.
            if (balance > 0) {
                if (isBankTransferMethod() && currentPaymentQr) {
                    showPaymentInlineError(
                        'QR đang chờ xác nhận. Sau khi nhận tiền, bấm "Đã nhận chuyển khoản" để ghi nhận và chốt đơn.',
                        'info'
                    );
                    return;
                }

                return await addPayment({ finalizeWhenPaid: true });
            }

            return await finalizeRecordedPayment();
        }
        async function cancelPaymentQrByReference(referenceCode) {
            const reference = (referenceCode || '').trim();

            if (!reference) {
                return;
            }

            // Chỉ xử lý mã QR do POS tạo
            if (!reference.toUpperCase().includes('QR-') && !reference.toUpperCase().startsWith('POS-')) {
                return;
            }

            try {
                await postJson('/admin/pos/payment-qr/cancel-by-content', {
                    content: reference
                });
            } catch (err) {
                console.warn('Không thể cập nhật trạng thái QR khi xóa payment.', err);
            }
        }

        async function removePayment(paymentId, paymentMeta) {
            clearPaymentInlineError();

            const id = parseInt(paymentId || '0', 10);
            if (!id) {
                showPaymentInlineError('Mã thanh toán không hợp lệ.', 'warning');
                return;
            }

            openConfirmModal({
                title: 'Xóa thanh toán',
                message: 'Bạn có chắc chắn muốn xóa khoản thanh toán này?',
                confirmText: 'Xóa thanh toán',
                confirmClass: 'btn-danger',
                onConfirm: async () => {
                    const btnConfirmAction = document.getElementById('btnConfirmAction');

                    await runPosAction(
                        posState,
                        `payment:remove:${id}`,
                        async function () {
                            return await deleteJson(`/admin/pos/payments/${id}`);
                        },
                        {
                            button: btnConfirmAction,
                            busyText: 'Đang xóa...',
                            fallbackMessage: 'Không thể xóa thanh toán.',
                            scopes: ['checkout', 'modalSubmit'],
                            conflictScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                            blockedMessage: 'POS đang xử lý thao tác thanh toán khác, chưa thể xóa khoản thanh toán.',
                            requireOnline: true,
                            offlineMessage: 'Đang offline, chưa thể xóa thanh toán.',
                            offlineDisplayMode: 'inline',
                            displayMode: 'inline',
                            inlineTarget: ensurePaymentErrorBox(),
                            clearInlineOnStart: true,
                            onSuccess: function (draft) {
                                cancelPaymentQrByReference(paymentMeta?.referenceCode);
                                clearPaymentInlineError();

                                applyDraftSuccess({
                                    draft,
                                    successMessage: 'Đã xóa thanh toán',
                                    focusBarcode: false,
                                    afterSync: function (nextDraft) {
                                        confirmModal?.hide();
                                        renderPaymentModalDraft(nextDraft);

                                        if (typeof renderPayments === 'function') {
                                            renderPayments(nextDraft.payments || []);
                                        }

                                        renderPaymentPreview(posState);

                                        setTimeout(() => {
                                            payAmount?.focus();
                                            payAmount?.select();
                                        }, 0);
                                    }
                                });
                            },
                            onFinally: function () {
                                refreshLocksSafe();
                            }
                        }
                    );
                }
            });
        }
        function getQrEls() {
            return {
                box: document.getElementById('paymentQrBox'),
                modal: document.getElementById('paymentQrModal'),
                btnOpenPopup: document.getElementById('btnOpenPaymentQrPopup'),
                btnCreate: document.getElementById('btnCreatePaymentQr'),
                btnConfirmPaid: document.getElementById('btnConfirmPaymentQrPaid'),
                btnCancelQr: document.getElementById('btnCancelPaymentQr'),
                statusText: document.getElementById('paymentQrStatusText'),
                popupStatusText: document.getElementById('paymentQrPopupStatusText'),
                content: document.getElementById('paymentQrContent'),
                image: document.getElementById('paymentQrImage'),
                bankName: document.getElementById('paymentQrBankName'),
                accountNumber: document.getElementById('paymentQrAccountNumber'),
                accountName: document.getElementById('paymentQrAccountName'),
                amount: document.getElementById('paymentQrAmount'),
                transferContent: document.getElementById('paymentQrTransferContent')
            };
        }

        function isBankTransferMethod() {
            return getCurrentMethod() === 1;
        }

        function togglePaymentQrBox() {
            const els = getQrEls();
            if (!els.box) return;

            const show = isBankTransferMethod();
            els.box.style.display = show ? '' : 'none';

            if (!show) {
                clearPaymentQrUi();
            }
        }

        function clearPaymentQrUi() {
            currentPaymentQr = null;

            const els = getQrEls();

            if (els.statusText) {
                els.statusText.textContent = 'Nhập số tiền rồi bấm Enter để tạo QR.';
            }

            if (els.popupStatusText) {
                els.popupStatusText.textContent = 'Khách quét mã để thanh toán';
            }

            if (els.btnOpenPopup) {
                els.btnOpenPopup.style.display = 'none';
            }

            if (els.content) {
                els.content.style.display = 'none';
            }

            if (els.image) {
                els.image.src = '';
            }

            if (els.bankName) els.bankName.textContent = '-';
            if (els.accountNumber) els.accountNumber.textContent = '-';
            if (els.accountName) els.accountName.textContent = '-';
            if (els.amount) els.amount.textContent = '0';
            if (els.transferContent) els.transferContent.textContent = '-';

            if (els.modal && window.bootstrap) {
                bootstrap.Modal.getInstance(els.modal)?.hide();
            }
        }

        function renderPaymentQr(qr) {
            const els = getQrEls();

            if (!qr) return;

            currentPaymentQr = qr;

            if (els.statusText) {
                els.statusText.textContent = 'QR đã tạo. Bấm "Xem QR" hoặc kiểm tra popup QR.';
            }

            if (els.popupStatusText) {
                els.popupStatusText.textContent = 'Khách quét mã, sau đó nhân viên kiểm tra app ngân hàng.';
            }

            if (els.btnOpenPopup) {
                els.btnOpenPopup.style.display = '';
            }

            if (els.content) {
                els.content.style.display = '';
            }

            if (els.image) {
                els.image.src = qr.qrDataUrl || '';
            }

            if (els.bankName) {
                els.bankName.textContent = qr.bankName || qr.bankCode || '-';
            }

            if (els.accountNumber) {
                els.accountNumber.textContent = qr.accountNumber || '-';
            }

            if (els.accountName) {
                els.accountName.textContent = qr.accountName || '-';
            }

            if (els.amount) {
                els.amount.textContent = formatMoneyLocal(qr.amount);
            }

            if (els.transferContent) {
                els.transferContent.textContent = qr.content || '-';
            }

            if (payProvider) {
                payProvider.value = qr.bankCode || qr.bankName || 'BANK';
            }

            if (payReference) {
                payReference.value = qr.content || qr.requestCode || '';
            }

            if (els.modal && window.bootstrap) {
                bootstrap.Modal.getOrCreateInstance(els.modal).show();
                setTimeout(() => {
                    els.btnConfirmPaid?.focus();
                }, 200);
            }
            sendCustomerDisplayEvent('customer_payment_qr_created', {
                qrDataUrl: qr.qrDataUrl || '',
                amount: Number(qr.amount || 0),
                content: qr.content || '',
                bankName: qr.bankName || qr.bankCode || '',
                accountNumber: qr.accountNumber || '',
                accountName: qr.accountName || ''
            });
        }

        async function createPaymentQr() {
            clearPaymentInlineError();

            if (!isBankTransferMethod()) {
                showPaymentInlineError('Chỉ tạo QR khi chọn phương thức chuyển khoản.', 'warning');
                return;
            }

            const draft = getCurrentDraft();
            if (!draft) {
                showPaymentInlineError('Chưa có giỏ hiện tại.', 'warning');
                return;
            }

            let amount = Number(payAmount?.value || 0);

            if (!Number.isFinite(amount) || amount <= 0) {
                amount = getDraftBalance(draft);
            }

            if (amount <= 0)  {
                showPaymentInlineError('Đơn hàng đã đủ tiền, không cần tạo QR.', 'warning');
                return;
            }

            const els = getQrEls();

            return await runPosAction(
                posState,
                'payment:createQr',
                async function () {
                    return await postJson('/admin/pos/cart/current/payment-qr', {
                        bankAccountId: null,
                        amount: amount
                    });
                },
                {
                    button: els.btnCreate || null,
                    busyText: 'Đang tạo QR...',
                    fallbackMessage: 'Không thể tạo QR chuyển khoản.',
                    scopes: ['checkout', 'modalSubmit'],
                    conflictScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                    blockedMessage: 'POS đang xử lý thao tác khác, chưa thể tạo QR.',
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể tạo QR.',
                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',
                    inlineTarget: ensurePaymentErrorBox(),
                    clearInlineOnStart: true,
                    onSuccess: function (qr) {
                        renderPaymentQr(qr);
                        showSuccess?.('Đã tạo QR chuyển khoản');
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );
        }
        function bindUiLocks() {
            registerUiLock(posState, {
                target: btnAddPayment,
                requireOnline: true,
                busyScopes: ['checkout', 'modalSubmit'],
                pendingActions: ['payment:finalizeFromModal', 'order:finalizeCurrentCart', 'order:holdCurrentCart', 'order:cancelCurrentCart'],
                offlineMessage: 'Đang offline, chưa thể thêm thanh toán.',
                busyMessage: 'POS đang bận xử lý thanh toán khác.',
                pendingMessage: 'Tác vụ thanh toán đang được xử lý.'
            });

            registerUiLock(posState, {
                target: btnFinalizeFromPaymentModal,
                requireOnline: true,
                busyScopes: ['checkout', 'modalSubmit'],
                pendingActions: ['payment:finalizeFromModal', 'order:finalizeCurrentCart', 'order:holdCurrentCart', 'order:cancelCurrentCart'],
                offlineMessage: 'Đang offline, chưa thể chốt đơn.',
                busyMessage: 'POS đang bận cập nhật thanh toán.',
                pendingMessage: 'Đơn đang được chốt.'
            });
        }
        function isPaymentModalOpen() {
            return paymentModalEl?.classList?.contains('show') === true;
        }

        function isBusyPaymentAction() {
            // Dựa vào lock hiện tại của posState.
            // Nếu project bạn lưu busy khác tên, đoạn này vẫn an toàn vì có optional chaining.
            return !!(
                posState?.ui?.busyScopes?.checkout ||
                posState?.ui?.busyScopes?.modalSubmit ||
                posState?.ui?.pendingActions?.length
            );
        }

        function moveFocusToReferenceIfNeeded() {
            const method = getCurrentMethod();

            if (isReferenceRequired(method) && payReference && !payReference.disabled) {
                payReference.focus();
                payReference.select?.();
                return true;
            }

            return false;
        }
        async function handlePaymentEnterAction() {
            clearPaymentInlineError();

            // Chuyển khoản:
            // Enter lần đầu = tạo QR.
            // Sau khi có QR rồi, không tự add payment để tránh ghi nhận nhầm.
            // Nhân viên phải bấm "Đã nhận chuyển khoản".
            if (isBankTransferMethod()) {
                if (!currentPaymentQr) {
                    await createPaymentQr();
                    return;
                }

                showPaymentInlineError(
                    'QR đã được tạo. Sau khi kiểm tra app ngân hàng, bấm "Đã nhận chuyển khoản" để ghi nhận payment.',
                    'info'
                );
                return;
            }

            await addPayment();
        }
        async function handlePaymentModalHotkeys(e) {
            if (!isPaymentModalOpen()) return;

            const target = e.target;
            const tag = (target?.tagName || '').toLowerCase();

            // Không phá khi đang gõ trong textarea
            if (tag === 'textarea') return;

            // ESC: đóng popup
            if (e.key === 'Escape') {
                e.preventDefault();
                paymentModal?.hide();
                setTimeout(() => focusBarcodeInput?.(), 100);
                return;
            }

            // F8: thu đủ
            if (e.key === 'F8') {
                e.preventDefault();
                setPayAmountExact({ focus: true });
                return;
            }

            // Ctrl + Enter: chốt đơn từ popup
            if (e.key === 'Enter' && e.ctrlKey) {
                e.preventDefault();

                if (isBusyPaymentAction()) return;

                await finalizeFromPaymentModal();
                return;
            }

            // Enter thường: ghi nhận thanh toán
            if (e.key === 'Enter') {
                e.preventDefault();

                if (isBusyPaymentAction()) return;

                // Nếu phương thức cần mã tham chiếu mà chưa nhập,
                // Enter lần đầu đưa qua ô mã tham chiếu.
                const reference = (payReference?.value || '').trim();
                if (isReferenceRequired(getCurrentMethod()) && !reference) {
                    if (moveFocusToReferenceIfNeeded()) return;
                }

                await handlePaymentEnterAction();
                return;
            }

            // Alt + 1/2/3/4: đổi phương thức thanh toán
            // 1: tiền mặt, 2: chuyển khoản, 3: thẻ, 4: ví
            if (e.altKey && ['1', '2', '3', '4'].includes(e.key)) {
                e.preventDefault();

                const methodMap = {
                    '1': '0',
                    '2': '1',
                    '3': '2',
                    '4': '3'
                };

                if (payMethod) {
                    payMethod.value = methodMap[e.key];
                    onPayMethodChanged();
                }

                return;
            }

            // Ctrl + 1..6: chọn nhanh 10k,20k,50k,100k,200k,500k
            if (e.ctrlKey && ['1', '2', '3', '4', '5', '6'].includes(e.key)) {
                e.preventDefault();

                const quickMap = {
                    '1': 10000,
                    '2': 20000,
                    '3': 50000,
                    '4': 100000,
                    '5': 200000,
                    '6': 500000
                };

                setPayAmountQuick(quickMap[e.key]);
                return;
            }

            // Phím +: cộng 10k
            if (e.key === '+' || e.key === '=') {
                e.preventDefault();
                increasePayAmount(10000);
                return;
            }

            // Phím -: trừ 10k
            if (e.key === '-') {
                e.preventDefault();
                increasePayAmount(-10000);
                return;
            }

            // Alt + B: focus lại ô tiền
            if (e.altKey && e.key.toLowerCase() === 'b') {
                e.preventDefault();
                focusPayAmountInput();
                return;
            }
        }
        async function handlePaymentQrModalHotkeys(e) {
            const qrModalEl = document.getElementById('paymentQrModal');

            if (!qrModalEl || !qrModalEl.classList.contains('show')) {
                return;
            }

            // ESC: chỉ đóng popup QR, không hủy QR, không đóng payment modal
            if (e.key === 'Escape') {
                e.preventDefault();
                e.stopPropagation();

                bootstrap.Modal.getInstance(qrModalEl)?.hide();

                setTimeout(() => {
                    payAmount?.focus();
                    payAmount?.select?.();
                }, 150);

                return;
            }

            // F9: xác nhận đã nhận tiền
            if (e.key === 'F9') {
                e.preventDefault();
                e.stopPropagation();

                if (currentPaymentQr?.id) {
                    await confirmPaymentQrPaid();
                }

                return;
            }

            // F10: hủy QR thật sự, update DB Status = Cancelled
            if (e.key === 'F10') {
                e.preventDefault();
                e.stopPropagation();

                if (currentPaymentQr?.id) {
                    await cancelPaymentQr();
                }

                return;
            }
        }
        async function markPaymentQrManualConfirmed() {
            if (!currentPaymentQr?.id) return;

            return await postJson(`/admin/pos/payment-qr/${currentPaymentQr.id}/manual-confirm`, {});
        }

      
        async function confirmPaymentQrPaid() {
            clearPaymentInlineError();

            if (!currentPaymentQr) {
                showPaymentInlineError('Chưa có QR chuyển khoản để xác nhận.', 'warning');
                return;
            }

            if (!isBankTransferMethod()) {
                showPaymentInlineError('Vui lòng chọn phương thức chuyển khoản.', 'warning');
                return;
            }

            const qrAmount = Number(currentPaymentQr.amount || 0);

            if (!Number.isFinite(qrAmount) || qrAmount <= 0) {
                showPaymentInlineError('Số tiền QR không hợp lệ.', 'warning');
                return;
            }

            if (payAmount) {
                payAmount.value = qrAmount;
            }

            if (payReference) {
                payReference.value = currentPaymentQr.content || currentPaymentQr.requestCode || '';
            }

            if (payProvider) {
                payProvider.value = currentPaymentQr.bankCode || currentPaymentQr.bankName || 'BANK';
            }

            renderPaymentPreview(posState);

            const els = getQrEls();

            return await runPosAction(
                posState,
                `payment:confirmQrPaid:${currentPaymentQr.id}`,
                async function () {
                    return await postJson('/admin/pos/cart/current/payment-and-finalize', {
                        method: 1,
                        amount: qrAmount,
                        referenceCode: currentPaymentQr.content || currentPaymentQr.requestCode || null,
                        provider: currentPaymentQr.bankCode || currentPaymentQr.bankName || null
                    });
                },
                {
                    button: els.btnConfirmPaid,
                    busyText: 'Đang xác nhận...',
                    fallbackMessage: 'Không thể xác nhận chuyển khoản.',
                    scopes: ['checkout', 'modalSubmit'],
                    conflictScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                    blockedMessage: 'POS đang xử lý thao tác khác, chưa thể xác nhận chuyển khoản.',
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể xác nhận chuyển khoản.',
                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',
                    inlineTarget: ensurePaymentErrorBox(),
                    clearInlineOnStart: true,
                    onSuccess: async function (data) {
                        clearPaymentInlineError();
                        
                     
                        await markPaymentQrManualConfirmed();

                        const qrModalEl = document.getElementById('paymentQrModal');
                        if (qrModalEl && window.bootstrap) {
                            bootstrap.Modal.getInstance(qrModalEl)?.hide();
                        }
                        if (data?.finalized === true) {

                            showCustomerPaymentSuccess({
                                finalized: true,
                                method: 'qr',
                                paidAmount: Number(data?.paidTotal || data?.paidAmount || 0),
                                remainingAmount: 0,
                                changeAmount: Number(data?.changeDue || 0),
                                durationMs: SUCCESS_HOLD_MS
                            });

                            await applyScreenSuccess({
                                successMessage: data?.message || 'Đã nhận chuyển khoản và chốt đơn.',
                                reason: 'payment-qr-confirm-paid-finalized',
                                silent: false,
                                force: true,
                                focusBarcode: true,

                                beforeRefresh: async function () {

                                    paymentModal?.hide();

                                    const qrModalEl = document.getElementById('paymentQrModal');

                                    if (qrModalEl && window.bootstrap) {
                                        bootstrap.Modal.getInstance(qrModalEl)?.hide();
                                    }

                                    // CHỜ animation chạy xong mới reset màn khách
                                    setTimeout(function () {
                                        resetCustomerDisplayAfterFinalize({
                                            reason: 'qr_payment_finalized',
                                            orderId: data?.orderId || null
                                        });
                                    }, RESET_AFTER_SUCCESS_MS);

                                    if (data?.orderId) {
                                        openReceiptPrint(data.orderId, '80', true);
                                    }
                                }
                            });

                            return;
                        }
                        hidePaymentPreviewOnCustomerDisplay();
                        const previewDraft = data?.draft || data;

                        showCustomerPaymentSuccess({
                            finalized: false,
                            paidAmount:
                                Number(
                                    previewDraft?.paidTotal ||
                                    previewDraft?.paidAmount ||
                                    0
                                ),

                            remainingAmount:
                                Number(
                                    previewDraft?.balanceDue ||
                                    previewDraft?.remainingAmount ||
                                    0
                                ),

                            durationMs: 3200
                        });
                        const nextDraft = data?.draft || data;

                        applyDraftSuccess({
                            draft: nextDraft,
                            successMessage: 'Đã ghi nhận chuyển khoản',
                            focusBarcode: false,
                            afterSync: function (draftAfterPayment) {
                                renderPaymentModalDraft(draftAfterPayment);

                                if (typeof renderPayments === 'function') {
                                    renderPayments(draftAfterPayment.payments || []);
                                }

                                const nextBalance = getDraftBalance(draftAfterPayment);

                                if (payAmount) {
                                    payAmount.value = nextBalance;
                                }

                                updatePayExactAmountText(nextBalance);
                                clearPaymentQrUi();
                                togglePaymentQrBox();
                                renderPaymentPreview(posState);

                                setTimeout(() => {
                                    payAmount?.focus();
                                    payAmount?.select();
                                }, 0);
                            }
                        });
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );
        }

        function openPaymentQrPopup() {
            const els = getQrEls();

            if (!currentPaymentQr) {
                showPaymentInlineError('Chưa có QR chuyển khoản. Nhập số tiền rồi bấm Enter để tạo QR.', 'warning');
                return;
            }

            if (els.modal && window.bootstrap) {
                bootstrap.Modal.getOrCreateInstance(els.modal).show();
            }
        }
        async function cancelPaymentQr() {
            clearPaymentInlineError();

            if (!currentPaymentQr?.id) {
                showPaymentInlineError('Chưa có QR để hủy.', 'warning');
                return;
            }

            const els = getQrEls();

            return await runPosAction(
                posState,
                `payment:cancelQr:${currentPaymentQr.id}`,
                async function () {
                    return await postJson(`/admin/pos/payment-qr/${currentPaymentQr.id}/cancel`, {});
                },
                {
                    button: els.btnCancelQr,
                    busyText: 'Đang hủy QR...',
                    fallbackMessage: 'Không thể hủy QR.',
                    scopes: ['modalSubmit'],
                    conflictScopes: ['checkout', 'modalSubmit'],
                    displayMode: 'inline',
                    inlineTarget: ensurePaymentErrorBox(),
                    clearInlineOnStart: true,
                    onSuccess: function () {
                        const qrModalEl = document.getElementById('paymentQrModal');
                        if (qrModalEl && window.bootstrap) {
                            bootstrap.Modal.getInstance(qrModalEl)?.hide();
                        }

                        clearPaymentQrUi();
                        togglePaymentQrBox();

                        hidePaymentPreviewOnCustomerDisplay();
                        sendCustomerDisplayEvent('customer_payment_changed', {
                            reason: 'qr_cancelled'
                        });

                        showSuccess?.('Đã hủy QR chuyển khoản.');
                        focusPayAmountInput();
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );
        }
        function bindEvents() {
            ensurePaymentErrorBox();
            bindUiLocks();

            btnAddPayment?.addEventListener('click', addPayment);
            btnFinalizeFromPaymentModal?.addEventListener('click', finalizeFromPaymentModal);
            btnPayExact?.addEventListener('click', function (e) {
                e.preventDefault();
                setPayAmountExact({ focus: true });
            });
            payMethod?.addEventListener('change', onPayMethodChanged);
            document.getElementById('btnConfirmPaymentQrPaid')?.addEventListener('click', confirmPaymentQrPaid);
            document.getElementById('btnOpenPaymentQrPopup')?.addEventListener('click', openPaymentQrPopup);
            document.getElementById('btnCancelPaymentQr')?.addEventListener('click', cancelPaymentQr);
            payAmount?.addEventListener('input', function () {
                clearPaymentInlineError();
                renderPaymentPreview(posState);
                sendPaymentPreviewToCustomerDisplayDebounced();
            });

            payReference?.addEventListener('input', clearPaymentInlineError);
            payProvider?.addEventListener('input', clearPaymentInlineError);

            payAmount?.addEventListener('keydown', async function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    e.stopPropagation();
                    await handlePaymentEnterAction();
                }
            });
            payReference?.addEventListener('keydown', async function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    e.stopPropagation();
                    await handlePaymentEnterAction();
                }
            });

            payProvider?.addEventListener('keydown', async function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    e.stopPropagation();
                    await handlePaymentEnterAction();
                }
            });
            document.addEventListener('keydown', handlePaymentModalHotkeys);
            document.addEventListener('keydown', handlePaymentQrModalHotkeys);
            paymentModalEl?.addEventListener('click', function (e) {
                const quickBtn = e.target.closest('[data-pay-amount]');
                if (quickBtn) {
                    const amount = parseFloat(quickBtn.getAttribute('data-pay-amount') || '0');
                    setPayAmountQuick(amount);
                    return;
                }

                const plusBtn = e.target.closest('[data-pay-plus]');
                if (plusBtn) {
                    const amount = parseFloat(plusBtn.getAttribute('data-pay-plus') || '0');
                    increasePayAmount(amount);
                    return;
                }

                const removeBtn = e.target.closest('[data-remove-payment-id]');
                if (removeBtn) {
                    const paymentId = parseInt(removeBtn.getAttribute('data-remove-payment-id') || '0', 10);
                    if (!paymentId) return;

                    removePayment(paymentId, {
                        method: removeBtn.getAttribute('data-payment-method') || '',
                        referenceCode: removeBtn.getAttribute('data-payment-reference') || '',
                        provider: removeBtn.getAttribute('data-payment-provider') || ''
                    });
                }
            });
            paymentModalEl?.addEventListener('shown.bs.modal', function () {
                // Khi modal đã mở xong, focus chắc chắn vào ô số tiền
                setTimeout(function () {
                    focusPayAmountInput();
                }, 80);
            });
            paymentModalEl?.addEventListener('hidden.bs.modal', function () {
                hidePaymentPreviewOnCustomerDisplay();
            });
        }

        return {
            bindEvents,
            openPaymentModal,
            resetPaymentForm,
            addPayment,
            removePayment,
            finalizeFromPaymentModal,
            setPayAmountExact,
            setPayAmountQuick,
            createPaymentQr,
            confirmPaymentQrPaid,
            openPaymentQrPopup,
            cancelPaymentQr,
            increasePayAmount
        };
    }

    return {
        create
    };
})();
