window.PosPayment = (function () {
    'use strict';

    // One pending collection per order. A lost response must be resolved before another collection.
    function createCollectionIntents(storage, randomUuid) {
        const prefix = 'pos:collection:v1:';
        return {
            read: function (orderId) { return JSON.parse(storage.getItem(prefix + orderId) || 'null'); },
            prepare: function (orderId, values) {
                if (!Number.isInteger(orderId) || orderId <= 0) throw new Error('Không xác định được đơn hàng. Vui lòng tải lại POS.');
                const key = prefix + orderId;
                const saved = storage.getItem(key);
                if (saved) {
                    const pending = JSON.parse(saved);
                    if (pending.orderId !== orderId || !/^[0-9a-f-]{36}$/i.test(pending.clientRequestId))
                        throw new Error('Lần thu đang chờ không hợp lệ. Vui lòng kiểm tra lịch sử thanh toán.');
                    return { body: pending, recovered: true };
                }
                const body = { ...values, orderId, clientRequestId: randomUuid() };
                storage.setItem(key, JSON.stringify(body)); // Fail before HTTP if durable tab storage is unavailable.
                return { body, recovered: false };
            },
            complete: function (body) {
                const key = prefix + body.orderId;
                const saved = JSON.parse(storage.getItem(key) || 'null');
                if (saved?.clientRequestId === body.clientRequestId) storage.removeItem(key);
            }
        };
    }

    function collectionUuid() {
        if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
        const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }

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

        /*
         * P3 note:
         * Các element dưới đây hiện chưa chắc đã được PosApp truyền
         * vào object elements, nên dùng elements trước và fallback
         * về DOM hiện tại. Không tạo state thứ hai.
         */
        const payFooterStateText =
            elements?.payFooterStateText ||
            document.getElementById(
                'payFooterStateText'
            );

        const payPreviewStateText =
            elements?.payPreviewStateText ||
            document.getElementById(
                'payPreviewStateText'
            );

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
        const qrHistory = window.PosQrHistory?.create({
            getOrderId: () => getCurrentDraft()?.orderId,
            onOpen: qr => {
                // Opening a saved QR does not change the amount entered for another payment.
                if (payMethod) payMethod.value = '1';
                syncPaymentMethodButtons();
                updateAddPaymentButtonText();
                toggleReferenceFieldsByMethod();
                togglePaymentQrBox();
                renderPaymentQr(qr);
            }
        });
        async function applyQrPaymentResult(data) {
            const sameOrder = Number(getCurrentDraft()?.orderId) === Number(data.orderId);
            if (currentPaymentQr?.id === data.qrId) {
                window.bootstrap?.Modal.getInstance(document.getElementById('paymentQrModal'))?.hide();
                clearPaymentQrUi();
            }
            if (sameOrder) {
                if (data.finalized) paymentModal?.hide();
                showCustomerPaymentSuccess({ finalized: data.finalized === true, method: 'qr',
                    paidAmount: data.paidAmount, remainingAmount: data.remainingAmount, changeAmount: 0, durationMs: 4500 });
            }
            await requestScreenRefresh({ reason: 'qr-payment-recorded', force: true, scope: 'full' });
            if (sameOrder && !data.finalized && Number(getCurrentDraft()?.orderId) === Number(data.orderId)) {
                if (payAmount) payAmount.value = getDraftBalance(getCurrentDraft());
                renderPaymentModalDraftSafe(getCurrentDraft());
                renderPaymentPreviewSafe();
                showSuccess?.('Đã ghi nhận chuyển khoản. Có thể tạo QR tiếp theo cho số còn thiếu.');
            }
            qrHistory?.refresh();
        }
        window.addEventListener('acb:completed', event => applyQrPaymentResult(event.detail));
        window.addEventListener('acb:payment-changed', () => {
            if (paymentModalEl?.classList.contains('show')) qrHistory?.refresh();
        });
        let customerDisplayConnection = null;
        let customerDisplayJoined = false;
        let previewSendTimer = null;

        const SUCCESS_HOLD_MS = 4500;
        const INFO_HOLD_MS = 3600;
        const RESET_AFTER_SUCCESS_MS = 4700;

        let pendingFinalizeCustomerPayload = null;

        /* =====================================================
         * INLINE ERROR
         * ===================================================== */

        function ensurePaymentErrorBox() {
            if (!paymentModalEl) {
                return null;
            }

            let box =
                paymentModalEl.querySelector(
                    '#paymentInlineErrorBox'
                );

            if (box) {
                return box;
            }

            box =
                document.createElement('div');

            box.id =
                'paymentInlineErrorBox';

            box.style.display =
                'none';

            const errorHost =
                paymentModalEl.querySelector(
                    '.pos-payment-footer__status'
                ) ||
                paymentModalEl.querySelector(
                    '.modal-body'
                ) ||
                paymentModalEl.querySelector(
                    '.modal-content'
                ) ||
                paymentModalEl;

            if (errorHost.firstChild) {
                errorHost.insertBefore(
                    box,
                    errorHost.firstChild
                );
            } else {
                errorHost.appendChild(box);
            }

            return box;
        }

        function clearPaymentInlineError() {
            clearInlineError(
                ensurePaymentErrorBox()
            );
        }

        function showPaymentInlineError(
            message,
            variant
        ) {
            setInlineError(
                ensurePaymentErrorBox(),
                message,
                variant || 'danger'
            );
        }

        /* =====================================================
         * DRAFT / STATE
         * ===================================================== */

        function getCurrentDraft() {
            return (
                posState?.business?.currentDraft ||
                posState?.currentDraft ||
                null
            );
        }

        function getDraftBalance(draft) {
            if (!draft) {
                return 0;
            }

            /*
             * Ưu tiên field backend đã tính sẵn.
             */
            const directBalance =
                Number(
                    draft?.balanceDue ??
                    draft?.remainingAmount ??
                    draft?.balance
                );

            if (
                Number.isFinite(
                    directBalance
                )
            ) {
                return directBalance > 0
                    ? directBalance
                    : 0;
            }

            /*
             * Fallback nếu backend không trả balance.
             */
            const grandTotal =
                Number(
                    draft?.grandTotal || 0
                );

            const paidTotal =
                Number(
                    draft?.paidTotal ??
                    draft?.paidAmount ??
                    draft?.paid ??
                    0
                );

            const numeric =
                grandTotal - paidTotal;

            if (!Number.isFinite(numeric)) {
                return 0;
            }

            return numeric > 0
                ? numeric
                : 0;
        }

        function formatMoneyLocal(value) {
            const numeric =
                Number(value || 0);

            return Number.isFinite(numeric)
                ? numeric.toLocaleString(
                    'vi-VN'
                )
                : '0';
        }

        function getCurrentMethod() {
            const parsed =
                parseInt(
                    payMethod?.value || '0',
                    10
                );

            return Number.isFinite(parsed)
                ? parsed
                : 0;
        }

        function isReferenceRequired(method) {
            const parsed =
                parseInt(
                    method || '0',
                    10
                );

            return (
                parsed === 1 ||
                parsed === 2 ||
                parsed === 3
            );
        }

        /* =====================================================
         * ACTION SUCCESS HELPERS
         * ===================================================== */

        function applyDraftSuccess(options) {
            return applyDraftActionSuccess({
                draft: options?.draft,
                posState,
                syncDraftToUi,
                showSuccess,
                focusBarcodeInput,

                successMessage:
                    options?.successMessage ||
                    '',

                focusBarcode:
                    options?.focusBarcode !==
                    false,

                afterSync:
                    options?.afterSync ||
                    null
            });
        }

        async function applyScreenSuccess(
            options
        ) {
            await applyScreenActionSuccess({
                requestScreenRefresh,
                showSuccess,

                successMessage:
                    options?.successMessage ||
                    '',

                reason:
                    options?.reason ||
                    'payment-screen-action-success',

                silent:
                    !!options?.silent,

                force:
                    options?.force !== false,

                focusBarcode:
                    options?.focusBarcode !==
                    false,

                beforeRefresh:
                    options?.beforeRefresh ||
                    null,

                afterSuccess:
                    options?.afterSuccess ||
                    null
            });
        }

        /*
         * refreshUiLocks vẫn là authority cho busy/offline/pending.
         * Sau khi lock manager chạy xong, tái áp dụng balance gate.
         */
        function refreshLocksSafe() {
            refreshUiLocks?.(
                posState
            );

            syncFinalizePresentationFromDraft();
        }

        /* =====================================================
         * PAYMENT METHOD PRESENTATION
         * ===================================================== */

        function getAddPaymentButtonText() {
            const method =
                getCurrentMethod();

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

        function updateAddPaymentButtonText() {
            if (!btnAddPayment) {
                return;
            }

            btnAddPayment.textContent =
                getAddPaymentButtonText();
        }

        function getPaymentMethodButtons() {
            return Array.from(
                paymentModalEl
                    ?.querySelectorAll(
                        '[data-pay-method-value]'
                    ) ||
                []
            );
        }

        function syncPaymentMethodButtons() {
            const method =
                getCurrentMethod();

            paymentModalEl?.setAttribute(
                'data-payment-method',
                String(method)
            );

            getPaymentMethodButtons()
                .forEach(function (button) {
                    const buttonMethod =
                        parseInt(
                            button.getAttribute(
                                'data-pay-method-value'
                            ) || '-1',
                            10
                        );

                    const active =
                        buttonMethod === method;

                    button.classList.toggle(
                        'is-active',
                        active
                    );

                    button.setAttribute(
                        'aria-pressed',
                        active
                            ? 'true'
                            : 'false'
                    );
                });
        }

        function toggleReferenceFieldsByMethod() {
            const method =
                getCurrentMethod();

            const requireReference =
                isReferenceRequired(
                    method
                );

            if (payReference) {
                payReference.disabled =
                    !requireReference;

                const host =
                    payReference.closest(
                        '.form-group, .mb-3, .col, [class*="col-"]'
                    );

                host?.classList?.toggle(
                    'is-disabled',
                    !requireReference
                );

                if (!requireReference) {
                    payReference.value =
                        '';
                }
            }

            if (payProvider) {
                const shouldEnableProvider =
                    (
                        method === 1 ||
                        method === 2 ||
                        method === 3
                    );

                payProvider.disabled =
                    !shouldEnableProvider;

                const host =
                    payProvider.closest(
                        '.form-group, .mb-3, .col, [class*="col-"]'
                    );

                host?.classList?.toggle(
                    'is-disabled',
                    !shouldEnableProvider
                );

                if (
                    !shouldEnableProvider
                ) {
                    payProvider.value =
                        '';
                }
            }
        }

        /* =====================================================
         * QUICK CASH
         * ===================================================== */

        function buildQuickCashCandidates(
            balance
        ) {
            const amount =
                Number(balance || 0);

            if (
                !Number.isFinite(amount) ||
                amount <= 0
            ) {
                return [];
            }

            const steps = [
                10000,
                50000,
                100000,
                500000,
                1000000
            ];

            const result = [];

            for (const step of steps) {
                const candidate =
                    Math.ceil(
                        amount / step
                    ) * step;

                if (
                    candidate <= amount ||
                    result.includes(
                        candidate
                    )
                ) {
                    continue;
                }

                result.push(
                    candidate
                );

                if (
                    result.length >= 3
                ) {
                    break;
                }
            }

            return result;
        }

        function renderQuickCashButtons() {
            const host =
                document.getElementById(
                    'paymentQuickAmounts'
                );

            if (!host) {
                return;
            }

            const draft =
                getCurrentDraft();

            const balance =
                getDraftBalance(
                    draft
                );

            const values =
                buildQuickCashCandidates(
                    balance
                );

            if (!values.length) {
                host.innerHTML = '';
                return;
            }

            host.innerHTML =
                values
                    .map(
                        function (value) {
                            return `
                                <button type="button"
                                        class="pos-payment-quick-button"
                                        data-pay-amount="${value}">
                                    ${formatMoneyLocal(value)}
                                </button>
                            `;
                        }
                    )
                    .join('');
        }

        const depositPanel = document.getElementById('posDepositPanel');
        const depositChoice = document.getElementById('posDepositChoice');
        let depositSignature = '';
        let depositBusy = false;
        let depositOptOutOrderId = 0;
        async function applyDepositSelection(depositId, automatic) {
            const draft = getCurrentDraft();
            if (!draft?.customerId || navigator.onLine === false || depositBusy) return;
            const selected = draft.availableDeposits?.find(item => item.id === Number(depositId));
            const amount = selected
                ? Math.min(Number(selected.balance), getDraftBalance(draft) + Number(draft.depositAmount || 0))
                : 0;
            const status = document.getElementById('posDepositStatus');
            depositBusy = true;
            depositChoice.disabled = true;
            if (status) status.textContent = automatic ? 'Đang tự áp dụng tiền cọc...' : 'Đang cập nhật...';
            try {
                const next = await postJson(`/admin/customer-deposit/orders/${draft.orderId}/select`, {
                    expectedCustomerId: draft.customerId,
                    depositId: selected?.id || null,
                    amount
                });
                applyDraftSuccess({
                    draft: next,
                    successMessage: selected ? `Đã áp dụng ${formatMoneyLocal(amount)} tiền cọc` : 'Đã bỏ áp dụng tiền cọc',
                    focusBarcode: false,
                    afterSync: value => {
                        renderPaymentModalDraftSafe(value);
                        setPayAmountExact({ focus: false });
                        refreshLocksSafe();
                    }
                });
                if (status) status.textContent = selected ? `Đã tự trừ ${formatMoneyLocal(amount)} khỏi số cần thanh toán.` : 'Đơn này không sử dụng tiền cọc.';
            } catch (error) {
                if (status) status.textContent = error.message;
                showPaymentInlineError(error.message, 'warning');
            } finally {
                depositBusy = false;
                depositChoice.disabled = navigator.onLine === false;
            }
        }
        function ensureAutomaticDepositSelection() {
            const draft = getCurrentDraft();
            const deposits = draft?.availableDeposits || [];
            if (!paymentModalEl?.classList.contains('show') || depositBusy || !draft?.customerId ||
                draft.customerDepositId || !deposits.length || getDraftBalance(draft) <= 0 ||
                depositOptOutOrderId === Number(draft.orderId) || navigator.onLine === false) return;
            void applyDepositSelection(deposits[0].id, true);
        }
        function syncDepositPresentation() {
            if (!depositPanel) return;
            const draft = getCurrentDraft();
            const deposits = draft?.availableDeposits || [];
            depositPanel.hidden = !draft?.customerId || (!deposits.length && !draft?.depositAmount);
            document.getElementById('depositCustomerLabel').textContent = draft?.customerName || '';
            document.getElementById('depositAvailableLabel').textContent = 'Khả dụng ' + formatMoneyLocal(deposits.reduce((sum, d) => sum + Number(d.balance), 0));
            const applied = document.getElementById('depositAppliedLabel');
            if (applied) applied.textContent = Number(draft?.depositAmount) > 0
                ? `Đã áp dụng ${formatMoneyLocal(draft.depositAmount)} cho đơn này.`
                : 'Hệ thống tự áp dụng tối đa khi mở thanh toán.';
            const signature = JSON.stringify([draft?.orderId, draft?.customerId, draft?.customerDepositId, draft?.depositAmount, deposits]);
            if (signature !== depositSignature) {
                depositSignature = signature;
                depositChoice.replaceChildren(new Option('Không sử dụng cọc', ''));
                deposits.forEach(d => depositChoice.add(new Option(`DC-${d.id} · ${d.purpose} · ${formatMoneyLocal(d.balance)}`, d.id)));
                depositChoice.value = draft?.customerDepositId || '';
            }
            depositChoice.disabled = navigator.onLine === false || depositBusy;
        }
        depositChoice?.addEventListener('change', async () => {
            const draft = getCurrentDraft();
            depositOptOutOrderId = depositChoice.value ? 0 : Number(draft?.orderId || 0);
            await applyDepositSelection(Number(depositChoice.value) || null, false);
        });

        const creditToggle = document.getElementById('btnCreditPayment');
        const creditPanel = document.getElementById('posCreditPanel');
        let creditModeActive = false;
        function syncCreditModePresentation() {
            creditToggle?.classList.toggle('is-active', creditModeActive);
            creditToggle?.setAttribute('aria-pressed', String(creditModeActive));
            creditToggle?.setAttribute('aria-expanded', String(creditModeActive));
            if (creditPanel) creditPanel.hidden = !creditModeActive;
            if (btnFinalizeFromPaymentModal) {
                btnFinalizeFromPaymentModal.textContent = creditModeActive ? 'Chốt đơn và ghi công nợ' : 'Chốt đơn';
                btnFinalizeFromPaymentModal.classList.toggle('btn-warning', creditModeActive);
                btnFinalizeFromPaymentModal.classList.toggle('btn-success', !creditModeActive);
            }
        }
        function syncCreditPresentation() {
            syncDepositPresentation();
            if (!creditToggle) return;
            const draft = getCurrentDraft();
            const eligible = draft?.customerCanBuyOnCredit === true && Number(draft?.customerId) > 0;
            creditToggle.hidden = !eligible;
            if (!eligible) creditModeActive = false;
            syncCreditModePresentation();
            const name = document.getElementById('creditCustomerName');
            if (name) name.textContent = draft?.customerName || '';
            const amount = document.getElementById('creditBalance');
            if (amount) amount.textContent = formatMoneyLocal(getDraftBalance(draft));
            const existing = Number(draft?.customerDebtBalance) || 0;
            const balanceBefore = document.getElementById('creditExistingBalance');
            const balanceAfter = document.getElementById('creditBalanceAfter');
            if (balanceBefore) balanceBefore.textContent = formatMoneyLocal(existing);
            if (balanceAfter) balanceAfter.textContent = formatMoneyLocal(existing + getDraftBalance(draft));
        }
        creditToggle?.addEventListener('click', () => {
            syncCreditPresentation();
            if (!creditToggle.hidden) creditModeActive = true;
            syncCreditModePresentation();
            refreshLocksSafe();
        });
        async function finalizeCreditFromModal() {
            const draft = getCurrentDraft();
            if (!draft?.customerCanBuyOnCredit || navigator.onLine === false) return;
            const key = 'pos:credit:' + draft.orderId;
            try {
                const previous = sessionStorage.getItem(key);
                const request = previous ? JSON.parse(previous) : {
                    clientRequestId: collectionUuid(), expectedCustomerId: draft.customerId,
                    expectedBalance: getDraftBalance(draft),
                    note: document.getElementById('creditNote').value.trim() || null
                };
                sessionStorage.setItem(key, JSON.stringify(request));
                await finalizeRecordedPayment(draft.orderId, request);
            } catch (error) { showPaymentInlineError(error.message, 'warning'); }
        }

        function syncPaymentWorkspacePresentation() {
            syncCreditPresentation();
            syncPaymentMethodButtons();
            renderQuickCashButtons();
            ensureAutomaticDepositSelection();
        }

        function setPaymentWorkspaceOpen(open) {
            document.body.classList.toggle(
                'pos-payment-workspace-open',
                open === true
            );
        }

        /* =====================================================
         * HIGH-RISK P3 BALANCE GATE
         *
         * IMPORTANT:
         * - Preview input KHÔNG quyết định Finalize.
         * - Chỉ persisted/current draft quyết định Finalize.
         * - Không force-enable khi ready; lock manager vẫn có quyền
         *   giữ disabled vì busy/offline/pending.
         * ===================================================== */

        function syncFinalizePresentationFromDraft(
            draftOverride
        ) {
            const draft =
                draftOverride ||
                getCurrentDraft();

            const actualBalance =
                draft
                    ? getDraftBalance(draft)
                    : Number.NaN;

            syncCreditPresentation();
            const creditReady = creditModeActive && draft?.customerCanBuyOnCredit === true && actualBalance > 0 && navigator.onLine !== false;
            const canFinalize = !!draft && Number.isFinite(actualBalance) && (actualBalance <= 0 || creditReady);

            if (
                btnFinalizeFromPaymentModal
            ) {
                /*
                 * Chỉ cưỡng bức DISABLE khi chưa đủ tiền.
                 *
                 * Khi đủ tiền, KHÔNG cưỡng bức disabled=false tại đây,
                 * vì registerUiLock/refreshUiLocks vẫn phải giữ authority
                 * đối với busy/offline/pending action.
                 */
                if (!canFinalize) {
                    btnFinalizeFromPaymentModal.disabled =
                        true;
                }

                btnFinalizeFromPaymentModal
                    .classList
                    .toggle(
                        'is-payment-finalize-ready',
                        canFinalize
                    );

                btnFinalizeFromPaymentModal
                    .setAttribute(
                        'data-payment-ready',
                        canFinalize
                            ? 'true'
                            : 'false'
                    );
            }

            if (payFooterStateText) {
                payFooterStateText.textContent =
                    creditReady
                        ? `Sẽ ghi công nợ ${formatMoneyLocal(actualBalance)} cho khách hàng này.`
                        : canFinalize
                        ? 'Đã đủ tiền. Có thể chốt đơn.'
                        : 'Ghi nhận thanh toán để cập nhật số tiền thực tế.';
            }
        }

        /*
         * Chỉ đổi COPY của preview.
         * Không thay số học / balance classes do PosRender sở hữu.
         */
        function syncPaymentPreviewCopyFromTypedAmount() {
            if (!payPreviewStateText) {
                return;
            }

            const draft =
                getCurrentDraft();

            if (!draft) {
                payPreviewStateText.textContent =
                    'Chưa có giỏ hiện tại.';
                return;
            }

            const balance =
                getDraftBalance(
                    draft
                );

            const amount =
                Number(
                    payAmount?.value || 0
                );

            if (balance <= 0) {
                payPreviewStateText.textContent =
                    'Đơn đã đủ tiền.';
                return;
            }

            if (
                !Number.isFinite(amount) ||
                amount <= 0
            ) {
                payPreviewStateText.textContent =
                    'Chưa nhập số tiền';
                return;
            }

            if (amount < balance) {
                payPreviewStateText.textContent =
                    'Sau khoản này vẫn còn thiếu';
                return;
            }

            payPreviewStateText.textContent =
                'Khoản đang nhập sẽ thanh toán đủ';
        }

        /*
         * Wrapper bắt buộc:
         * renderPaymentPreview bên ngoài có thể thay đổi footer/button
         * dựa trên preview; ngay sau đó phải tái khóa theo persisted draft.
         */
        function renderPaymentPreviewSafe() {
            renderPaymentPreview(
                posState
            );

            syncPaymentPreviewCopyFromTypedAmount();

            syncFinalizePresentationFromDraft();
        }

        /*
         * Wrapper cho server/durable draft.
         */
        function renderPaymentModalDraftSafe(
            draft
        ) {
            renderPaymentModalDraft(
                draft
            );

            syncFinalizePresentationFromDraft(
                draft
            );
            if (paymentModalEl?.classList.contains('show')) qrHistory?.refresh();
        }

        /* =====================================================
         * CUSTOMER DISPLAY
         * ===================================================== */

        function getPosShellDataset() {
            const shell =
                document.getElementById(
                    'posShell'
                );

            return shell?.dataset || {};
        }

        async function ensureCustomerDisplayConnection() {
            if (!window.signalR) {
                return null;
            }

            if (
                customerDisplayConnection &&
                customerDisplayConnection.state ===
                signalR.HubConnectionState.Connected &&
                customerDisplayJoined
            ) {
                return customerDisplayConnection;
            }

            if (!customerDisplayConnection) {
                customerDisplayConnection =
                    new signalR.HubConnectionBuilder()
                        .withUrl(
                            '/hubs/pos'
                        )
                        .withAutomaticReconnect(
                            [
                                0,
                                1000,
                                2000,
                                5000
                            ]
                        )
                        .build();

                customerDisplayConnection
                    .onreconnected(
                        async function () {
                            customerDisplayJoined =
                                false;

                            await joinCustomerDisplayGroup();
                        }
                    );

                customerDisplayConnection
                    .onclose(
                        function () {
                            customerDisplayJoined =
                                false;
                        }
                    );
            }

            if (
                customerDisplayConnection.state ===
                signalR.HubConnectionState.Disconnected
            ) {
                await customerDisplayConnection.start();
            }

            await joinCustomerDisplayGroup();

            return customerDisplayConnection;
        }

        async function joinCustomerDisplayGroup() {
            if (
                !customerDisplayConnection ||
                customerDisplayJoined
            ) {
                return;
            }

            const dataset =
                getPosShellDataset();

            const storeId =
                Number(
                    dataset.storeId || 0
                );

            const terminalId =
                dataset.terminalId || '';

            if (
                !storeId ||
                !terminalId
            ) {
                return;
            }

            await customerDisplayConnection.invoke(
                'JoinStoreGroup',
                storeId,
                terminalId
            );

            customerDisplayJoined =
                true;
        }

        async function sendCustomerDisplayEvent(
            eventType,
            payload
        ) {
            try {
                const connection =
                    await ensureCustomerDisplayConnection();

                if (!connection) {
                    return;
                }

                await connection.invoke(
                    'BroadcastTerminalEvent',
                    {
                        eventType: eventType,
                        payload:
                            payload || {}
                    }
                );
            } catch (err) {
                console.warn(
                    'Send customer display event failed',
                    eventType,
                    err
                );
            }
        }

        function sendPaymentPreviewToCustomerDisplayDebounced() {
            clearTimeout(
                previewSendTimer
            );

            previewSendTimer =
                setTimeout(
                    function () {
                        sendPaymentPreviewToCustomerDisplay();
                    },
                    40
                );
        }

        function sendPaymentPreviewToCustomerDisplay() {
            const draft =
                getCurrentDraft();

            if (!draft) {
                return;
            }

            const method =
                getCurrentMethod();

            const amount =
                Number(
                    payAmount?.value || 0
                );

            const balance =
                getDraftBalance(
                    draft
                );

            const expectedChange =
                Math.max(
                    0,
                    amount - balance
                );

            const expectedBalance =
                Math.max(
                    0,
                    balance - amount
                );

            sendCustomerDisplayEvent(
                'customer_payment_preview',
                {
                    method: method,

                    amount:
                        Number.isFinite(amount)
                            ? amount
                            : 0,

                    balance: balance,

                    expectedBalance:
                        expectedBalance,

                    expectedChange:
                        expectedChange,

                    grandTotal:
                        Number(
                            draft.grandTotal ||
                            0
                        ),

                    paidTotal:
                        Number(
                            draft.paidTotal ||
                            draft.paidAmount ||
                            0
                        )
                }
            );
        }

        function hidePaymentPreviewOnCustomerDisplay() {
            sendCustomerDisplayEvent(
                'customer_payment_hide',
                {}
            );
        }

        function showCustomerPaymentSuccess(
            payload
        ) {
            sendCustomerDisplayEvent(
                'customer_payment_success',
                payload || {
                    durationMs: 1800
                }
            );
        }

        function resetCustomerDisplayAfterFinalize(
            payload
        ) {
            sendCustomerDisplayEvent(
                'customer_display_reset',
                payload || {}
            );
        }

        /* =====================================================
         * AMOUNT INPUT
         * ===================================================== */

        function updatePayExactAmountText(value) {
            const amount =
                Number(value || 0);

            const safeAmount =
                Number.isFinite(amount) &&
                    amount > 0
                    ? amount
                    : 0;

            const exactAmountEl =
                document.getElementById(
                    'payExactAmount'
                );

            if (exactAmountEl) {
                exactAmountEl.textContent =
                    safeAmount.toLocaleString(
                        'vi-VN'
                    );
            }

            if (btnPayExact) {
                btnPayExact.setAttribute(
                    'data-pay-amount',
                    String(safeAmount)
                );
            }
        }

        function focusPayAmountInput() {
            setTimeout(
                function () {
                    payAmount?.focus();
                    payAmount?.select?.();
                },
                0
            );
        }

        function setPayAmountExact(options) {
            const draft =
                getCurrentDraft();

            if (
                !draft ||
                !payAmount
            ) {
                return;
            }

            /*
             * Thu đủ = persisted balance hiện tại.
             */
            const balance =
                getDraftBalance(
                    draft
                );

            payAmount.value =
                balance;

            updatePayExactAmountText(
                balance
            );

            renderQuickCashButtons();

            clearPaymentInlineError();

            renderPaymentPreviewSafe();

            sendPaymentPreviewToCustomerDisplayDebounced();

            if (
                options?.focus !== false
            ) {
                focusPayAmountInput();
            }
        }

        function setPayAmountQuick(amount) {
            if (!payAmount) {
                return;
            }

            const value =
                Number(amount || 0);

            payAmount.value =
                Number.isFinite(value)
                    ? value
                    : 0;

            clearPaymentInlineError();

            renderPaymentPreviewSafe();

            sendPaymentPreviewToCustomerDisplayDebounced();

            payAmount.focus();
            payAmount.select();
        }

        function increasePayAmount(amount) {
            if (!payAmount) {
                return;
            }

            const current =
                parseFloat(
                    payAmount.value ||
                    '0'
                );

            const plus =
                Number(amount || 0);

            const next =
                (
                    Number.isFinite(current)
                        ? current
                        : 0
                ) +
                (
                    Number.isFinite(plus)
                        ? plus
                        : 0
                );

            payAmount.value =
                next > 0
                    ? next
                    : 0;

            clearPaymentInlineError();

            renderPaymentPreviewSafe();

            sendPaymentPreviewToCustomerDisplayDebounced();

            payAmount.focus();
            payAmount.select();
        }

        /* =====================================================
         * PAYMENT METHOD CHANGE
         * ===================================================== */

        function onPayMethodChanged() {
            clearPaymentInlineError();

            creditModeActive = false;
            syncCreditModePresentation();

            syncPaymentMethodButtons();

            updateAddPaymentButtonText();

            toggleReferenceFieldsByMethod();

            togglePaymentQrBox();

            /*
             * Giữ nguyên nghiệp vụ:
             * đổi phương thức đưa amount về persisted balance.
             */
            setPayAmountExact({
                focus: true
            });

            sendPaymentPreviewToCustomerDisplayDebounced();
        }

        function resetPaymentForm() {
            qrHistory?.invalidate();
            if (payMethod) {
                payMethod.value = '0';
            }

            if (payAmount) {
                payAmount.value = '';
            }

            if (payReference) {
                payReference.value = '';
            }

            if (payProvider) {
                payProvider.value = '';
            }

            clearPaymentInlineError();

            updateAddPaymentButtonText();

            toggleReferenceFieldsByMethod();

            togglePaymentQrBox();

            renderPaymentPreviewSafe();

            syncPaymentWorkspacePresentation();
        }

        function restorePendingCollectionInputs() {
            const pending = createCollectionIntents(sessionStorage, collectionUuid).read(Number(getCurrentDraft()?.orderId));
            if (!pending) return false;
            if (payMethod) payMethod.value = String(pending.method);
            if (payAmount) payAmount.value = String(pending.amount);
            if (payReference) payReference.value = pending.referenceCode || '';
            if (payProvider) payProvider.value = pending.provider || '';
            updateAddPaymentButtonText();
            toggleReferenceFieldsByMethod();
            showPaymentInlineError('Có lần thu trước chưa xác định kết quả. Bấm ghi nhận để đối chiếu lại, hệ thống sẽ không thu trùng.', 'warning');
            return true;
        }

        function openPaymentModal() {
            const draft =
                getCurrentDraft();

            if (!draft) {
                showError(
                    'Chưa có giỏ hiện tại.'
                );

                return;
            }

            if (payMethod) {
                payMethod.value = '0';
            }

            if (payReference) {
                payReference.value = '';
            }

            if (payProvider) {
                payProvider.value = '';
            }

            clearPaymentInlineError();

            updateAddPaymentButtonText();

            toggleReferenceFieldsByMethod();

            togglePaymentQrBox();

            renderPaymentModalDraftSafe(
                draft
            );

            setPayAmountExact({
                focus: false
            });

            try { restorePendingCollectionInputs(); } catch (error) { showPaymentInlineError('Không đọc được lần thu đang chờ. Vui lòng kiểm tra trình duyệt.', 'warning'); }

            syncPaymentWorkspacePresentation();

            /*
             * Đảm bảo lock state + balance gate đồng bộ
             * trước khi modal xuất hiện.
             */
            refreshLocksSafe();

            paymentModal?.show();

            /*
             * Fallback focus.
             */
            setTimeout(
                function () {
                    focusPayAmountInput();
                },
                350
            );
        }

        /* =====================================================
         * VALIDATION
         * ===================================================== */

        function validateAddPaymentInput() {
            const draft =
                getCurrentDraft();

            const method =
                getCurrentMethod();

            const amount =
                parseFloat(
                    payAmount?.value ||
                    '0'
                );

            const reference =
                (
                    payReference?.value ||
                    ''
                ).trim();

            if (!draft) {
                return 'Chưa có giỏ hiện tại.';
            }

            if (!Number.isFinite(amount) || amount <= 0 || amount >= 10000000000000000 || !Number.isInteger(amount)) {
                return 'Vui lòng nhập số tiền thanh toán hợp lệ.';
            }

            if (
                isReferenceRequired(
                    method
                ) &&
                !reference
            ) {
                return 'Phương thức này yêu cầu mã tham chiếu.';
            }

            return '';
        }

        /* =====================================================
         * ADD PAYMENT
         * ===================================================== */

        async function addPayment(options) {
            let hasPendingCollection = false;
            try { hasPendingCollection = restorePendingCollectionInputs(); } catch (error) { showPaymentInlineError('Không đọc được lần thu đang chờ.', 'warning'); return; }
            if (!hasPendingCollection && isBankTransferMethod() && currentPaymentQr) return await confirmPaymentQrPaid();

            clearPaymentInlineError();

            const validationMessage =
                validateAddPaymentInput();

            if (validationMessage) {
                showPaymentInlineError(
                    validationMessage,
                    'warning'
                );

                if (
                    validationMessage.includes(
                        'số tiền'
                    )
                ) {
                    payAmount?.focus();
                } else if (
                    validationMessage.includes(
                        'mã tham chiếu'
                    )
                ) {
                    payReference?.focus();
                }

                return;
            }

            let collectionIntents, collectionIntent;
            try {
                collectionIntents = createCollectionIntents(sessionStorage, collectionUuid);
                collectionIntent = collectionIntents.prepare(Number(getCurrentDraft()?.orderId), {
                    method: getCurrentMethod(), amount: parseFloat(payAmount?.value || '0'),
                    referenceCode: (payReference?.value || '').trim() || null,
                    provider: (payProvider?.value || '').trim() || null
                });
            } catch (error) {
                showPaymentInlineError(error.message || 'Không lưu được lần thu tiền trong trình duyệt.', 'warning');
                return;
            }
            const { method, amount } = collectionIntent.body;
            return await runPosAction(
                posState,
                `payment:add:${method}`,

                async function () {
                    return await postJson(
                        '/admin/pos/cart/current/payments',
                        collectionIntent.body
                    );
                },

                {
                    button:
                        btnAddPayment,

                    busyText:
                        'Đang ghi nhận.',

                    fallbackMessage:
                        'Không thể thêm thanh toán.',

                    scopes: [
                        'checkout',
                        'modalSubmit'
                    ],

                    conflictScopes: [
                        'cartMutate',
                        'checkout',
                        'modalSubmit'
                    ],

                    blockedMessage:
                        'POS đang xử lý thao tác thanh toán khác, chưa thể thêm thanh toán.',

                    requireOnline:
                        true,

                    offlineMessage:
                        'Đang offline, chưa thể thêm thanh toán.',

                    offlineDisplayMode:
                        'inline',

                    displayMode:
                        'inline',

                    inlineTarget:
                        ensurePaymentErrorBox(),

                    clearInlineOnStart:
                        true,

                    onSuccess:
                        async function (draft) {
                            clearPaymentInlineError();
                            if (Number(getCurrentDraft()?.orderId) !== collectionIntent.body.orderId || Number(draft?.status) !== 0) {
                                await requestScreenRefresh({ reason: 'collection-recovered', force: true, scope: 'full' });
                                collectionIntents.complete(collectionIntent.body);
                                showSuccess?.('Đã đối chiếu lần thu trước. Vui lòng kiểm tra trạng thái đơn.');
                                return;
                            }

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

                                successMessage:
                                    collectionIntent.recovered ? 'Đã đối chiếu lần thu trước. Kiểm tra số còn thiếu trước khi thu tiếp.' : 'Đã thêm thanh toán',

                                focusBarcode:
                                    false,

                                afterSync:
                                    function (nextDraft) {
                                        renderPaymentModalDraftSafe(
                                            nextDraft
                                        );

                                        if (
                                            typeof renderPayments ===
                                            'function'
                                        ) {
                                            renderPayments(
                                                nextDraft.payments ||
                                                []
                                            );
                                        }

                                        const remaining =
                                            getDraftBalance(
                                                nextDraft
                                            );

                                        if (payAmount) {
                                            payAmount.value =
                                                remaining;
                                        }

                                        updatePayExactAmountText(
                                            remaining
                                        );

                                        renderQuickCashButtons();

                                        if (payReference) {
                                            payReference.value =
                                                '';
                                        }

                                        if (payProvider) {
                                            payProvider.value =
                                                '';
                                        }

                                        toggleReferenceFieldsByMethod();

                                        renderPaymentPreviewSafe();

                                        setTimeout(
                                            function () {
                                                payAmount?.focus();
                                                payAmount?.select();
                                            },
                                            0
                                        );
                                    }
                            });

                            collectionIntents.complete(collectionIntent.body);

                            /*
                             * =========================
                             * TIỀN MẶT
                             * =========================
                             */
                            if (method === 0) {
                                /*
                                 * Chưa đủ tiền.
                                 */
                                if (
                                    nextBalance > 0
                                ) {
                                    showCustomerPaymentSuccess({
                                        finalized:
                                            false,

                                        method:
                                            'cash',

                                        paidAmount:
                                            paidTotal,

                                        remainingAmount:
                                            nextBalance,

                                        changeAmount:
                                            0,

                                        durationMs:
                                            INFO_HOLD_MS
                                    });

                                    return;
                                }

                                /*
                                 * Đủ / thừa tiền.
                                 * GIỮ NGUYÊN cash auto-finalize.
                                 */
                                pendingFinalizeCustomerPayload = {
                                    finalized:
                                        true,

                                    method:
                                        'cash',

                                    paidAmount:
                                        paidTotal,

                                    remainingAmount:
                                        0,

                                    changeAmount:
                                        nextChange,

                                    durationMs:
                                        SUCCESS_HOLD_MS
                                };

                                setTimeout(
                                    async function () {
                                        await finalizeRecordedPayment(draft.orderId);
                                    },
                                    150
                                );

                                return;
                            }

                            /*
                             * Method khác.
                             */
                            if (
                                nextBalance <= 0 &&
                                options?.finalizeWhenPaid ===
                                true
                            ) {
                                pendingFinalizeCustomerPayload = {
                                    finalized:
                                        true,

                                    method:
                                        `payment_method_${method}`,

                                    paidAmount:
                                        paidTotal,

                                    remainingAmount:
                                        0,

                                    changeAmount:
                                        nextChange,

                                    durationMs:
                                        SUCCESS_HOLD_MS
                                };

                                /*
                                 * Không finalize lồng trong onSuccess.
                                 */
                                setTimeout(
                                    async function () {
                                        await finalizeRecordedPayment(draft.orderId);
                                    },
                                    150
                                );

                                return;
                            }

                            hidePaymentPreviewOnCustomerDisplay();
                        },

                    onFinally:
                        function () {
                            refreshLocksSafe();
                        }
                }
            );
        }

        /* =====================================================
         * FINALIZE
         * ===================================================== */

        async function finalizeRecordedPayment(orderId = Number(getCurrentDraft()?.orderId), creditRequest = null) {
            const targetOrderId = Number(orderId);
            if (!Number.isInteger(targetOrderId) || targetOrderId <= 0) return;
            clearPaymentInlineError();

            return await runPosAction(
                posState,
                'payment:finalizeFromModal',

                async function () {
                    try {
                        return await postJson(
                            creditRequest ? `/admin/pos/${targetOrderId}/` + 'finalize-credit' : `/admin/pos/${targetOrderId}/finalize`,
                            creditRequest || {}
                        );
                    } catch (error) {
                        if (creditRequest && [400, 403, 409, 422].includes(Number(error.statusCode || error.status)))
                            sessionStorage.removeItem('pos:credit:' + targetOrderId);
                        throw error;
                    }
                },

                {
                    button:
                        btnFinalizeFromPaymentModal,

                    busyText:
                        'Đang chốt đơn.',

                    fallbackMessage:
                        'Không thể chốt đơn.',

                    scopes: [
                        'checkout',
                        'modalSubmit'
                    ],

                    conflictScopes: [
                        'cartMutate',
                        'checkout',
                        'modalSubmit'
                    ],

                    blockedMessage:
                        'POS đang cập nhật giỏ hoặc xử lý thanh toán khác, chưa thể chốt đơn.',

                    requireOnline:
                        true,

                    offlineMessage:
                        'Đang offline, chưa thể chốt đơn.',

                    offlineDisplayMode:
                        'inline',

                    displayMode:
                        'inline',

                    inlineTarget:
                        ensurePaymentErrorBox(),

                    clearInlineOnStart:
                        true,

                    onSuccess:
                        async function (data) {
                            if (creditRequest) sessionStorage.removeItem('pos:credit:' + targetOrderId);
                            clearPaymentInlineError();

                            await applyScreenSuccess({
                                successMessage:
                                    data?.message ||
                                    'Đã chốt đơn',

                                reason:
                                    'payment-finalize-from-modal',

                                silent:
                                    false,

                                force:
                                    true,

                                focusBarcode:
                                    true,

                                beforeRefresh:
                                    async function () {
                                        paymentModal?.hide();

                                        const payload =
                                            pendingFinalizeCustomerPayload ||
                                            {
                                                finalized:
                                                    true,

                                                method:
                                                    'manual',

                                                durationMs:
                                                    SUCCESS_HOLD_MS
                                            };

                                        pendingFinalizeCustomerPayload =
                                            null;

                                        showCustomerPaymentSuccess(
                                            payload
                                        );

                                        setTimeout(
                                            function () {
                                                resetCustomerDisplayAfterFinalize({
                                                    reason:
                                                        'finalized',

                                                    orderId:
                                                        data?.orderId ||
                                                        null
                                                });
                                            },
                                            RESET_AFTER_SUCCESS_MS
                                        );

                                        if (data?.orderId) {
                                            openReceiptPrint(
                                                data.orderId,
                                                '80',
                                                true
                                            );
                                        }
                                    }
                            });
                        },

                    onFinally:
                        function () {
                            refreshLocksSafe();
                        }
                }
            );
        }

        async function finalizeFromPaymentModal() {
            clearPaymentInlineError();

            const draft =
                getCurrentDraft();

            const balance =
                getDraftBalance(
                    draft
                );

            if (creditModeActive) {
                return await finalizeCreditFromModal();
            }

            /*
             * GIỮ NGUYÊN business behavior:
             * nếu chưa đủ persisted balance thì thao tác manual
             * finalize có thể ghi payment đang nhập trước,
             * sau đó mới finalize.
             *
             * UI button P3 bị disable khi balance > 0,
             * nhưng keyboard/business function hiện hữu không bị rewrite.
             */
            if (balance > 0) {
                if (
                    isBankTransferMethod() &&
                    currentPaymentQr
                ) {
                    showPaymentInlineError(
                        'QR đang chờ xác nhận. Sau khi nhận tiền, bấm "Đã nhận chuyển khoản" để ghi nhận và chốt đơn.',
                        'info'
                    );

                    return;
                }

                return await addPayment({
                    finalizeWhenPaid:
                        true
                });
            }

            return await finalizeRecordedPayment();
        }

        /* =====================================================
         * REMOVE PAYMENT
         * ===================================================== */

        async function cancelPaymentQrByReference(
            referenceCode
        ) {
            const reference =
                (
                    referenceCode ||
                    ''
                ).trim();

            if (!reference) {
                return;
            }

            const upper =
                reference.toUpperCase();

            if (
                !upper.includes('QR-') &&
                !upper.startsWith('POS-')
            ) {
                return;
            }

            try {
                await postJson(
                    '/admin/pos/payment-qr/cancel-by-content',
                    {
                        content:
                            reference
                    }
                );
            } catch (err) {
                console.warn(
                    'Không thể cập nhật trạng thái QR khi xóa payment.',
                    err
                );
            }
        }

        async function removePayment(
            paymentId,
            paymentMeta
        ) {
            clearPaymentInlineError();

            const id =
                parseInt(
                    paymentId || '0',
                    10
                );

            if (!id) {
                showPaymentInlineError(
                    'Mã thanh toán không hợp lệ.',
                    'warning'
                );

                return;
            }

            openConfirmModal({
                title:
                    'Xóa thanh toán',

                message:
                    'Bạn có chắc chắn muốn xóa khoản thanh toán này?',

                confirmText:
                    'Xóa thanh toán',

                confirmClass:
                    'btn-danger',

                onConfirm:
                    async function () {
                        const btnConfirmAction =
                            document.getElementById(
                                'btnConfirmAction'
                            );

                        await runPosAction(
                            posState,
                            `payment:remove:${id}`,

                            async function () {
                                return await deleteJson(
                                    `/admin/pos/payments/${id}`
                                );
                            },

                            {
                                button:
                                    btnConfirmAction,

                                busyText:
                                    'Đang xóa...',

                                fallbackMessage:
                                    'Không thể xóa thanh toán.',

                                scopes: [
                                    'checkout',
                                    'modalSubmit'
                                ],

                                conflictScopes: [
                                    'cartMutate',
                                    'checkout',
                                    'modalSubmit'
                                ],

                                blockedMessage:
                                    'POS đang xử lý thao tác thanh toán khác, chưa thể xóa khoản thanh toán.',

                                requireOnline:
                                    true,

                                offlineMessage:
                                    'Đang offline, chưa thể xóa thanh toán.',

                                offlineDisplayMode:
                                    'inline',

                                displayMode:
                                    'inline',

                                inlineTarget:
                                    ensurePaymentErrorBox(),

                                clearInlineOnStart:
                                    true,

                                onSuccess:
                                    function (draft) {
                                        cancelPaymentQrByReference(
                                            paymentMeta?.referenceCode
                                        );

                                        clearPaymentInlineError();

                                        applyDraftSuccess({
                                            draft,

                                            successMessage:
                                                'Đã xóa thanh toán',

                                            focusBarcode:
                                                false,

                                            afterSync:
                                                function (nextDraft) {
                                                    confirmModal?.hide();

                                                    renderPaymentModalDraftSafe(
                                                        nextDraft
                                                    );

                                                    if (
                                                        typeof renderPayments ===
                                                        'function'
                                                    ) {
                                                        renderPayments(
                                                            nextDraft.payments ||
                                                            []
                                                        );
                                                    }

                                                    renderQuickCashButtons();

                                                    renderPaymentPreviewSafe();

                                                    setTimeout(
                                                        function () {
                                                            payAmount?.focus();
                                                            payAmount?.select();
                                                        },
                                                        0
                                                    );
                                                }
                                        });
                                    },

                                onFinally:
                                    function () {
                                        refreshLocksSafe();
                                    }
                            }
                        );
                    }
            });
        }

        /* =====================================================
         * QR
         * ===================================================== */

        function getQrEls() {
            return {
                box:
                    document.getElementById(
                        'paymentQrBox'
                    ),

                modal:
                    document.getElementById(
                        'paymentQrModal'
                    ),

                btnOpenPopup:
                    document.getElementById(
                        'btnOpenPaymentQrPopup'
                    ),

                btnCreate:
                    document.getElementById(
                        'btnCreatePaymentQr'
                    ),

                btnConfirmPaid:
                    document.getElementById(
                        'btnConfirmPaymentQrPaid'
                    ),

                btnCancelQr:
                    document.getElementById(
                        'btnCancelPaymentQr'
                    ),

                statusText:
                    document.getElementById(
                        'paymentQrStatusText'
                    ),

                popupStatusText:
                    document.getElementById(
                        'paymentQrPopupStatusText'
                    ),

                content:
                    document.getElementById(
                        'paymentQrContent'
                    ),

                image:
                    document.getElementById(
                        'paymentQrImage'
                    ),

                bankName:
                    document.getElementById(
                        'paymentQrBankName'
                    ),

                accountNumber:
                    document.getElementById(
                        'paymentQrAccountNumber'
                    ),

                accountName:
                    document.getElementById(
                        'paymentQrAccountName'
                    ),

                amount:
                    document.getElementById(
                        'paymentQrAmount'
                    ),

                transferContent:
                    document.getElementById(
                        'paymentQrTransferContent'
                    )
            };
        }

        function isBankTransferMethod() {
            return (
                getCurrentMethod() === 1
            );
        }

        function togglePaymentQrBox() {
            const qrEls =
                getQrEls();

            if (!qrEls.box) {
                return;
            }

            const show =
                isBankTransferMethod();

            qrEls.box.style.display =
                show
                    ? ''
                    : 'none';

            if (!show) {
                clearPaymentQrUi();
            }
        }

        function clearPaymentQrUi() {
            window.PosAcb?.blur?.();
            currentPaymentQr =
                null;

            const qrEls =
                getQrEls();

            if (qrEls.statusText) {
                qrEls.statusText.textContent =
                    'Nhập số tiền rồi bấm Enter để tạo QR.';
            }

            if (qrEls.popupStatusText) {
                qrEls.popupStatusText.textContent =
                    'Khách quét mã để thanh toán';
            }

            if (qrEls.btnOpenPopup) {
                qrEls.btnOpenPopup.style.display =
                    'none';
            }

            if (qrEls.content) {
                qrEls.content.style.display =
                    'none';
            }

            if (qrEls.image) {
                qrEls.image.src =
                    '';
            }

            if (qrEls.bankName) {
                qrEls.bankName.textContent =
                    '-';
            }

            if (qrEls.accountNumber) {
                qrEls.accountNumber.textContent =
                    '-';
            }

            if (qrEls.accountName) {
                qrEls.accountName.textContent =
                    '-';
            }

            if (qrEls.amount) {
                qrEls.amount.textContent =
                    '0';
            }

            if (qrEls.transferContent) {
                qrEls.transferContent.textContent =
                    '-';
            }

            if (
                qrEls.modal &&
                window.bootstrap
            ) {
                window.bootstrap.Modal
                    .getInstance(
                        qrEls.modal
                    )
                    ?.hide();
            }
        }

        function renderPaymentQr(qr) {
            qr = window.PosOffline?.manualQr(qr) || qr;
            const qrEls =
                getQrEls();

            if (!qr) {
                return;
            }

            currentPaymentQr =
                qr;
            const qrTitle = document.getElementById('paymentQrPopupTitle');
            if (qrTitle) qrTitle.textContent = qr.savedStatus ? 'QR thanh toán đã tạo' : 'Quét mã thanh toán';
            const qrCode = document.getElementById('paymentQrRequestCode');
            if (qrCode) qrCode.textContent = qr.requestCode ? `Mã QR: ${qr.requestCode}` : '';
            if (qrEls.btnConfirmPaid) qrEls.btnConfirmPaid.style.display = qr.automaticConfirmation || qr.readOnly ? 'none' : '';
            if (qrEls.btnConfirmPaid && window.PosOffline?.localMode()) qrEls.btnConfirmPaid.textContent = 'Đã nhận thủ công';
            if (qr.automaticConfirmation && !qr.readOnly) window.PosAcb?.track(qr);
            else window.PosAcb?.blur?.();
            let acbCheck = document.getElementById('btnCheckAcbPayment');
            if (!acbCheck && qrEls.btnConfirmPaid) {
                acbCheck = document.createElement('button');
                acbCheck.id = 'btnCheckAcbPayment'; acbCheck.type = 'button';
                acbCheck.className = 'btn btn-outline-primary'; acbCheck.textContent = 'Kiểm tra ngay';
                acbCheck.addEventListener('click', () => window.PosAcb?.check(currentPaymentQr?.id));
                qrEls.btnConfirmPaid.before(acbCheck);
            }
            if (acbCheck) acbCheck.hidden = !qr.automaticConfirmation || qr.readOnly;
            const acbStatus = document.getElementById('acbPaymentCheckStatus');
            if (acbStatus) { acbStatus.hidden = !qr.automaticConfirmation && !qr.savedMessage; acbStatus.textContent = qr.savedMessage || (qr.automaticConfirmation ? 'Chưa nhận xác nhận thanh toán. Có thể bấm Kiểm tra ngay để tra cứu ACB.' : ''); }
            if (qrEls.btnCancelQr) qrEls.btnCancelQr.hidden = qr.canCancel === false;
            const shortcuts = document.getElementById('paymentQrShortcuts');
            if (shortcuts) shortcuts.textContent = qr.readOnly ? 'ESC = Đóng QR · Chỉ xem lịch sử' : qr.automaticConfirmation
                ? 'F9 = Kiểm tra ACB · ESC = Đóng QR' + (qr.canCancel === false ? '' : ' · F10 = Hủy QR')
                : 'F9 = Đã nhận tiền · ESC = Đóng QR · F10 = Hủy QR';

            if (qrEls.statusText) {
                qrEls.statusText.textContent =
                    'QR đã tạo. Bấm "Xem QR" hoặc kiểm tra popup QR.';
            }

            if (qrEls.popupStatusText) {
                qrEls.popupStatusText.textContent =
                    qr.readOnly ? 'QR đã kết thúc. Không chuyển thêm tiền vào mã này.' : qr.automaticConfirmation ? 'ACB xác nhận từng khoản. Khi đơn đủ tiền sẽ tự chốt và in bill.' : 'Khách quét mã, sau đó nhân viên kiểm tra app ngân hàng.';
            }

            if (qrEls.btnOpenPopup) {
                qrEls.btnOpenPopup.style.display =
                    '';
            }

            if (qrEls.content) {
                qrEls.content.style.display =
                    '';
            }

            if (qrEls.image) {
                qrEls.image.src =
                    qr.qrDataUrl ||
                    '';
            }

            if (qrEls.bankName) {
                qrEls.bankName.textContent =
                    qr.bankName ||
                    qr.bankCode ||
                    '-';
            }

            if (qrEls.accountNumber) {
                qrEls.accountNumber.textContent =
                    qr.accountNumber ||
                    '-';
            }

            if (qrEls.accountName) {
                qrEls.accountName.textContent =
                    qr.accountName ||
                    '-';
            }

            if (qrEls.amount) {
                qrEls.amount.textContent =
                    formatMoneyLocal(
                        qr.amount
                    );
            }

            if (qrEls.transferContent) {
                qrEls.transferContent.textContent =
                    qr.content ||
                    '-';
            }

            if (payProvider) {
                payProvider.value =
                    qr.bankCode ||
                    qr.bankName ||
                    'BANK';
            }

            if (payReference) {
                payReference.value =
                    qr.content ||
                    qr.requestCode ||
                    '';
            }

            if (
                qrEls.modal &&
                window.bootstrap
            ) {
                window.bootstrap.Modal
                    .getOrCreateInstance(
                        qrEls.modal
                    )
                    .show();

                setTimeout(
                    function () {
                        qrEls.btnConfirmPaid
                            ?.focus();
                    },
                    200
                );
            }

            if (qr.readOnly) return;
            sendCustomerDisplayEvent(
                'customer_payment_qr_created',
                {
                    qrDataUrl:
                        qr.qrDataUrl ||
                        '',

                    amount:
                        Number(
                            qr.amount ||
                            0
                        ),

                    content:
                        qr.content ||
                        '',

                    bankName:
                        qr.bankName ||
                        qr.bankCode ||
                        '',

                    accountNumber:
                        qr.accountNumber ||
                        '',

                    accountName:
                        qr.accountName ||
                        ''
                }
            );
        }

        async function createPaymentQr() {
            clearPaymentInlineError();

            if (
                !isBankTransferMethod()
            ) {
                showPaymentInlineError(
                    'Chỉ tạo QR khi chọn phương thức chuyển khoản.',
                    'warning'
                );

                return;
            }

            const draft =
                getCurrentDraft();

            if (!draft) {
                showPaymentInlineError(
                    'Chưa có giỏ hiện tại.',
                    'warning'
                );

                return;
            }

            let amount =
                Number(
                    payAmount?.value ||
                    0
                );

            if (
                !Number.isFinite(amount) ||
                amount <= 0
            ) {
                amount =
                    getDraftBalance(
                        draft
                    );
            }

            if (amount <= 0) {
                showPaymentInlineError(
                    'Đơn hàng đã đủ tiền, không cần tạo QR.',
                    'warning'
                );

                return;
            }

            if (amount > getDraftBalance(draft) || !Number.isInteger(amount)) {
                showPaymentInlineError('Số tiền QR phải là số đồng nguyên, không vượt quá số còn thiếu.', 'warning');
                return;
            }
            // Keep the same key after an uncertain response, including a page reload.
            const intentStorageKey = `pos:qr-create:${draft.orderId}`;
            let intent;
            try { intent = JSON.parse(sessionStorage.getItem(intentStorageKey) || 'null'); } catch (_) { }
            if (!intent || intent.amount !== amount) {
                intent = { amount, clientRequestId: crypto.randomUUID() };
                try { sessionStorage.setItem(intentStorageKey, JSON.stringify(intent)); } catch (_) { }
            }
            const qrEls =
                getQrEls();

            return await runPosAction(
                posState,
                'payment:createQr',

                async function () {
                    return await postJson(
                        '/admin/pos/cart/current/payment-qr',
                        {
                            clientRequestId: intent.clientRequestId,
                            bankAccountId:
                                null,

                            amount:
                                amount
                        }
                    );
                },

                {
                    button:
                        qrEls.btnCreate ||
                        null,

                    busyText:
                        'Đang tạo QR...',

                    fallbackMessage:
                        'Không thể tạo QR chuyển khoản.',

                    scopes: [
                        'checkout',
                        'modalSubmit'
                    ],

                    conflictScopes: [
                        'cartMutate',
                        'checkout',
                        'modalSubmit'
                    ],

                    blockedMessage:
                        'POS đang xử lý thao tác khác, chưa thể tạo QR.',

                    requireOnline:
                        true,

                    offlineMessage:
                        'Đang offline, chưa thể tạo QR.',

                    offlineDisplayMode:
                        'inline',

                    displayMode:
                        'inline',

                    inlineTarget:
                        ensurePaymentErrorBox(),

                    clearInlineOnStart:
                        true,

                    onSuccess:
                        function (qr) {
                            try { sessionStorage.removeItem(intentStorageKey); } catch (_) { }
                            renderPaymentQr(
                                qr
                            );

                            showSuccess?.(
                                'Đã tạo QR chuyển khoản'
                            );
                            qrHistory?.refresh();
                        },

                    onFinally:
                        function () {
                            refreshLocksSafe();
                        }
                }
            );
        }

        async function confirmPaymentQrPaid() {
            clearPaymentInlineError();
            const qr = window.PosOffline?.manualQr(currentPaymentQr) || currentPaymentQr;
            if (!qr || qr.readOnly) return;
            if (qr.automaticConfirmation) return await window.PosAcb?.check(qr.id);
            return await runPosAction(posState, `payment:confirmQrPaid:${qr.id}`,
                async () => { await window.PosOffline?.prepareManualConfirmation(qr); return await postJson(`/admin/pos/payment-qr/${qr.id}/manual-confirm`, {}); }, {
                    button: getQrEls().btnConfirmPaid, busyText: 'Đang xác nhận...',
                    fallbackMessage: 'Không thể xác nhận chuyển khoản.',
                    scopes: ['checkout', 'modalSubmit'], conflictScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                    requireOnline: true, displayMode: 'inline', inlineTarget: ensurePaymentErrorBox(),
                    onSuccess: async data => {
                        if (data.printUrl) openReceiptPrint(data.orderId, '80', true);
                        await applyQrPaymentResult(data);
                    },
                    onFinally: refreshLocksSafe
                });
        }

        function openPaymentQrPopup() {
            if (currentPaymentQr) renderPaymentQr(currentPaymentQr);
            else showPaymentInlineError('Chọn một mã trong Lịch sử QR để mở lại.', 'info');
        }

        window.addEventListener('pos:offline-status', () => {
            if (currentPaymentQr && window.PosOffline?.localMode() && currentPaymentQr.automaticConfirmation)
                renderPaymentQr(currentPaymentQr);
        });

        async function cancelPaymentQr() {
            clearPaymentInlineError();
            if (currentPaymentQr?.canCancel === false) return;

            if (!currentPaymentQr?.id) {
                showPaymentInlineError(
                    'Chưa có QR để hủy.',
                    'warning'
                );

                return;
            }

            const qrEls =
                getQrEls();

            return await runPosAction(
                posState,
                `payment:cancelQr:${currentPaymentQr.id}`,

                async function () {
                    return await postJson(
                        `/admin/pos/payment-qr/${currentPaymentQr.id}/cancel`,
                        {}
                    );
                },

                {
                    button:
                        qrEls.btnCancelQr,

                    busyText:
                        'Đang hủy QR...',

                    fallbackMessage:
                        'Không thể hủy QR.',

                    scopes: [
                        'modalSubmit'
                    ],

                    conflictScopes: [
                        'checkout',
                        'modalSubmit'
                    ],

                    displayMode:
                        'inline',

                    inlineTarget:
                        ensurePaymentErrorBox(),

                    clearInlineOnStart:
                        true,

                    onSuccess:
                        function () {
                            window.PosAcb?.forget?.(currentPaymentQr?.id);
                            const qrModalEl =
                                document.getElementById(
                                    'paymentQrModal'
                                );

                            if (
                                qrModalEl &&
                                window.bootstrap
                            ) {
                                window.bootstrap.Modal
                                    .getInstance(
                                        qrModalEl
                                    )
                                    ?.hide();
                            }

                            clearPaymentQrUi();

                            togglePaymentQrBox();
                            qrHistory?.refresh();

                            hidePaymentPreviewOnCustomerDisplay();

                            sendCustomerDisplayEvent(
                                'customer_payment_changed',
                                {
                                    reason:
                                        'qr_cancelled'
                                }
                            );

                            showSuccess?.(
                                'Đã hủy QR chuyển khoản.'
                            );

                            focusPayAmountInput();
                        },

                    onFinally:
                        function () {
                            refreshLocksSafe();
                        }
                }
            );
        }

        /* =====================================================
         * UI LOCKS
         * ===================================================== */

        function bindUiLocks() {
            registerUiLock(
                posState,
                {
                    target:
                        btnAddPayment,

                    requireOnline:
                        true,

                    busyScopes: [
                        'checkout',
                        'modalSubmit'
                    ],

                    pendingActions: [
                        'payment:finalizeFromModal',
                        'order:finalizeCurrentCart',
                        'order:holdCurrentCart',
                        'order:cancelCurrentCart'
                    ],

                    offlineMessage:
                        'Đang offline, chưa thể thêm thanh toán.',

                    busyMessage:
                        'POS đang bận xử lý thanh toán khác.',

                    pendingMessage:
                        'Tác vụ thanh toán đang được xử lý.'
                }
            );

            registerUiLock(
                posState,
                {
                    target:
                        btnFinalizeFromPaymentModal,

                    requireOnline:
                        true,

                    busyScopes: [
                        'checkout',
                        'modalSubmit'
                    ],

                    pendingActions: [
                        'payment:finalizeFromModal',
                        'order:finalizeCurrentCart',
                        'order:holdCurrentCart',
                        'order:cancelCurrentCart'
                    ],

                    offlineMessage:
                        'Đang offline, chưa thể chốt đơn.',

                    busyMessage:
                        'POS đang bận cập nhật thanh toán.',

                    pendingMessage:
                        'Đơn đang được chốt.'
                }
            );
        }

        /* =====================================================
         * HOTKEYS
         * ===================================================== */

        function isPaymentModalOpen() {
            return (
                paymentModalEl
                    ?.classList
                    ?.contains(
                        'show'
                    ) === true
            );
        }

        function isBusyPaymentAction() {
            return !!(
                posState?.ui
                    ?.busyScopes
                    ?.checkout ||

                posState?.ui
                    ?.busyScopes
                    ?.modalSubmit ||

                posState?.ui
                    ?.pendingActions
                    ?.length
            );
        }

        function moveFocusToReferenceIfNeeded() {
            const method =
                getCurrentMethod();

            if (
                isReferenceRequired(
                    method
                ) &&
                payReference &&
                !payReference.disabled
            ) {
                payReference.focus();
                payReference.select?.();

                return true;
            }

            return false;
        }

        async function handlePaymentEnterAction() {
            clearPaymentInlineError();

            /*
             * Chuyển khoản:
             * Enter đầu = tạo QR.
             * QR đã có => không tự ghi payment.
             */
            if (
                isBankTransferMethod()
            ) {
                if (!currentPaymentQr) {
                    await createPaymentQr();
                    return;
                }

                showPaymentInlineError(
                    currentPaymentQr.automaticConfirmation
                        ? 'QR đã được tạo. Bấm "Xem QR", rồi "Kiểm tra ngay" để xác minh chuyển khoản.'
                        : 'QR đã được tạo. Sau khi kiểm tra app ngân hàng, bấm "Đã nhận chuyển khoản" để ghi nhận payment.',
                    'info'
                );

                return;
            }

            await addPayment();
        }

        async function handlePaymentModalHotkeys(e) {
            if (!isPaymentModalOpen()) {
                return;
            }

            const target =
                e.target;

            const tag =
                (
                    target?.tagName ||
                    ''
                ).toLowerCase();

            if (tag === 'textarea') {
                return;
            }

            /*
             * ESC
             */
            if (e.key === 'Escape') {
                e.preventDefault();

                paymentModal?.hide();

                setTimeout(
                    function () {
                        focusBarcodeInput?.();
                    },
                    100
                );

                return;
            }

            /*
             * F8 = Thu đủ trong payment context.
             */
            if (e.key === 'F8') {
                e.preventDefault();

                setPayAmountExact({
                    focus: true
                });

                return;
            }

            /*
             * Ctrl + Enter.
             * GIỮ NGUYÊN business flow hiện tại.
             */
            if (
                e.key === 'Enter' &&
                e.ctrlKey
            ) {
                e.preventDefault();

                if (
                    isBusyPaymentAction()
                ) {
                    return;
                }

                await finalizeFromPaymentModal();

                return;
            }

            /*
             * Enter thường.
             */
            if (e.key === 'Enter') {
                e.preventDefault();

                if (
                    isBusyPaymentAction()
                ) {
                    return;
                }

                const reference =
                    (
                        payReference?.value ||
                        ''
                    ).trim();

                if (
                    isReferenceRequired(
                        getCurrentMethod()
                    ) &&
                    !reference
                ) {
                    if (
                        moveFocusToReferenceIfNeeded()
                    ) {
                        return;
                    }
                }

                await handlePaymentEnterAction();

                return;
            }

            /*
             * Alt + 1..4
             *
             * Chỉ chọn method nếu option thật sự tồn tại.
             * P3 markup hiện có 0/1/2; method 3 được giữ để
             * backward-compatible nếu option được bổ sung lại.
             */
            if (
                e.altKey &&
                ['1', '2', '3', '4']
                    .includes(e.key)
            ) {
                e.preventDefault();

                if (e.key === '4') {
                    if (creditToggle && !creditToggle.hidden) {
                        creditModeActive = true;
                        syncCreditModePresentation();
                        refreshLocksSafe();
                    }
                    return;
                }

                const methodMap = {
                    '1': '0',
                    '2': '1',
                    '3': '2'
                };

                const nextMethod =
                    methodMap[e.key];

                const optionExists =
                    Array.from(
                        payMethod?.options ||
                        []
                    ).some(
                        function (option) {
                            return (
                                option.value ===
                                nextMethod
                            );
                        }
                    );

                if (
                    payMethod &&
                    optionExists
                ) {
                    payMethod.value =
                        nextMethod;

                    onPayMethodChanged();
                }

                return;
            }

            /*
             * Ctrl + 1..6
             */
            if (
                e.ctrlKey &&
                ['1', '2', '3', '4', '5', '6']
                    .includes(e.key)
            ) {
                e.preventDefault();

                const quickMap = {
                    '1': 10000,
                    '2': 20000,
                    '3': 50000,
                    '4': 100000,
                    '5': 200000,
                    '6': 500000
                };

                setPayAmountQuick(
                    quickMap[e.key]
                );

                return;
            }

            /*
             * +
             */
            if (
                e.key === '+' ||
                e.key === '='
            ) {
                e.preventDefault();

                increasePayAmount(
                    10000
                );

                return;
            }

            /*
             * -
             */
            if (e.key === '-') {
                e.preventDefault();

                increasePayAmount(
                    -10000
                );

                return;
            }

            /*
             * Alt + B
             */
            if (
                e.altKey &&
                e.key
                    .toLowerCase() ===
                'b'
            ) {
                e.preventDefault();

                focusPayAmountInput();

                return;
            }
        }

        async function handlePaymentQrModalHotkeys(e) {
            const qrModalEl =
                document.getElementById(
                    'paymentQrModal'
                );

            if (
                !qrModalEl ||
                !qrModalEl
                    .classList
                    .contains(
                        'show'
                    )
            ) {
                return;
            }

            /*
             * ESC:
             * chỉ đóng popup QR.
             */
            if (e.key === 'Escape') {
                e.preventDefault();
                e.stopPropagation();

                if (window.bootstrap) {
                    window.bootstrap.Modal
                        .getInstance(
                            qrModalEl
                        )
                        ?.hide();
                }

                setTimeout(
                    function () {
                        payAmount?.focus();
                        payAmount?.select?.();
                    },
                    150
                );

                return;
            }

            /*
             * F9
             */
            if (e.key === 'F9') {
                e.preventDefault();
                e.stopPropagation();

                if (
                    currentPaymentQr?.id
                ) {
                    await confirmPaymentQrPaid();
                }

                return;
            }

            /*
             * F10
             */
            if (e.key === 'F10') {
                e.preventDefault();
                e.stopPropagation();

                if (
                    currentPaymentQr?.id
                ) {
                    await cancelPaymentQr();
                }
            }
        }

        /* =====================================================
         * EVENTS
         * ===================================================== */

        function bindEvents() {
            ensurePaymentErrorBox();

            bindUiLocks();

            btnAddPayment
                ?.addEventListener(
                    'click',
                    addPayment
                );

            btnFinalizeFromPaymentModal
                ?.addEventListener(
                    'click',
                    finalizeFromPaymentModal
                );

            /*
             * stopPropagation tránh btnPayExact vừa chạy
             * handler trực tiếp vừa rơi xuống delegation
             * data-pay-amount.
             */
            btnPayExact
                ?.addEventListener(
                    'click',
                    function (e) {
                        e.preventDefault();
                        e.stopPropagation();

                        setPayAmountExact({
                            focus: true
                        });
                    }
                );

            payMethod
                ?.addEventListener(
                    'change',
                    onPayMethodChanged
                );

            document.getElementById('btnCreatePaymentQr')?.addEventListener('click', createPaymentQr);

            document
                .getElementById(
                    'btnConfirmPaymentQrPaid'
                )
                ?.addEventListener(
                    'click',
                    confirmPaymentQrPaid
                );

            document
                .getElementById(
                    'btnOpenPaymentQrPopup'
                )
                ?.addEventListener(
                    'click',
                    openPaymentQrPopup
                );

            document
                .getElementById(
                    'btnCancelPaymentQr'
                )
                ?.addEventListener(
                    'click',
                    cancelPaymentQr
                );

            payAmount
                ?.addEventListener(
                    'input',
                    function () {
                        clearPaymentInlineError();

                        renderPaymentPreviewSafe();

                        sendPaymentPreviewToCustomerDisplayDebounced();
                    }
                );

            payReference
                ?.addEventListener(
                    'input',
                    clearPaymentInlineError
                );

            payProvider
                ?.addEventListener(
                    'input',
                    clearPaymentInlineError
                );

            payAmount
                ?.addEventListener(
                    'keydown',
                    async function (e) {
                        if (
                            e.key !==
                            'Enter'
                        ) {
                            return;
                        }

                        e.preventDefault();
                        e.stopPropagation();

                        await handlePaymentEnterAction();
                    }
                );

            payReference
                ?.addEventListener(
                    'keydown',
                    async function (e) {
                        if (
                            e.key !==
                            'Enter'
                        ) {
                            return;
                        }

                        e.preventDefault();
                        e.stopPropagation();

                        await handlePaymentEnterAction();
                    }
                );

            payProvider
                ?.addEventListener(
                    'keydown',
                    async function (e) {
                        if (
                            e.key !==
                            'Enter'
                        ) {
                            return;
                        }

                        e.preventDefault();
                        e.stopPropagation();

                        await handlePaymentEnterAction();
                    }
                );

            document.addEventListener(
                'keydown',
                handlePaymentModalHotkeys
            );

            document.addEventListener(
                'keydown',
                handlePaymentQrModalHotkeys
            );

            paymentModalEl
                ?.addEventListener(
                    'click',
                    function (e) {
                        const target =
                            e.target;

                        if (
                            !(target instanceof Element)
                        ) {
                            return;
                        }

                        const methodButton =
                            target.closest(
                                '[data-pay-method-value]'
                            );

                        if (methodButton) {
                            e.preventDefault();

                            const method =
                                methodButton.getAttribute(
                                    'data-pay-method-value'
                                );

                            const optionExists =
                                Array.from(
                                    payMethod?.options ||
                                    []
                                ).some(
                                    function (option) {
                                        return (
                                            option.value ===
                                            method
                                        );
                                    }
                                );

                            if (
                                payMethod &&
                                optionExists
                            ) {
                                payMethod.value =
                                    method;

                                onPayMethodChanged();
                            }

                            return;
                        }

                        const quickBtn =
                            target.closest(
                                '[data-pay-amount]'
                            );

                        /*
                         * Exact button có handler riêng ở trên.
                         */
                        if (
                            quickBtn &&
                            quickBtn !== btnPayExact
                        ) {
                            const amount =
                                parseFloat(
                                    quickBtn.getAttribute(
                                        'data-pay-amount'
                                    ) || '0'
                                );

                            setPayAmountQuick(
                                amount
                            );

                            return;
                        }

                        const plusBtn =
                            target.closest(
                                '[data-pay-plus]'
                            );

                        if (plusBtn) {
                            const amount =
                                parseFloat(
                                    plusBtn.getAttribute(
                                        'data-pay-plus'
                                    ) || '0'
                                );

                            increasePayAmount(
                                amount
                            );

                            return;
                        }

                        const removeBtn =
                            target.closest(
                                '[data-remove-payment-id]'
                            );

                        if (removeBtn) {
                            const paymentId =
                                parseInt(
                                    removeBtn.getAttribute(
                                        'data-remove-payment-id'
                                    ) || '0',
                                    10
                                );

                            if (!paymentId) {
                                return;
                            }

                            removePayment(
                                paymentId,
                                {
                                    method:
                                        removeBtn.getAttribute(
                                            'data-payment-method'
                                        ) || '',

                                    referenceCode:
                                        removeBtn.getAttribute(
                                            'data-payment-reference'
                                        ) || '',

                                    provider:
                                        removeBtn.getAttribute(
                                            'data-payment-provider'
                                        ) || ''
                                }
                            );
                        }
                    }
                );

            paymentModalEl
                ?.addEventListener(
                    'shown.bs.modal',
                    function () {
                        qrHistory?.refresh();
                        setPaymentWorkspaceOpen(
                            true
                        );

                        syncPaymentWorkspacePresentation();

                        /*
                         * Lock manager chạy trước,
                         * balance gate chạy sau.
                         */
                        refreshLocksSafe();

                        setTimeout(
                            function () {
                                focusPayAmountInput();
                            },
                            80
                        );
                    }
                );

            paymentModalEl
                ?.addEventListener(
                    'hidden.bs.modal',
                    function () {
                        creditModeActive = false;
                        syncCreditModePresentation();
                        setPaymentWorkspaceOpen(
                            false
                        );

                        hidePaymentPreviewOnCustomerDisplay();
                    }
                );
        }

        /* =====================================================
         * PUBLIC API
         * ===================================================== */

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
        create, createCollectionIntents
    };
})();
