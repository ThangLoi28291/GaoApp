window.PosApp = (function () {
    'use strict';

    function create() {
        const posState = window.PosState.create();
        const barcodeSearchState = window.PosState.createBarcodeSearchState();
        const customerSearchState = window.PosState.createCustomerSearchState();

        const debugMode = new URLSearchParams(window.location.search).get('debug') === '1';
        if (typeof window.PosState?.setDebugEnabled === 'function') {
            window.PosState.setDebugEnabled(posState, debugMode);
        } else if (posState?.runtime) {
            posState.runtime.debugEnabled = debugMode;
        }

        const dom = window.PosDom.get();

        const {
            txtBarcode,
            btnScan,
            btnFocusBarcode,
            btnNewCart,
            btnHoldCart,
            btnClearCartLines,
            btnOpenPayment,
            btnFinalizeCart,
            btnCancelCart,
            currentDraftBody,
            heldList,
            customerInfoBox,
            txtOrderNote,
            btnSaveOrderNote,
            txtOrderDiscount,
            btnSaveOrderDiscount,
            btnClearOrderDiscount,
            barcodeAutocomplete
        } = dom.common;

        const {
            txtCustomerKeyword,
            btnSearchCustomer,
            customerSearchResult,
            btnClearCustomer,
            btnOpenQuickCreateCustomer,
            qcCustomerName,
            qcCustomerPhone,
            qcCustomerAddress,
            qcCustomerNote,
            btnSubmitQuickCreateCustomer
        } = dom.customer;

        const {
            payMethod,
            payAmount,
            payReference,
            payProvider,
            btnAddPayment,
            btnFinalizeFromPaymentModal,
            btnPayExact
        } = dom.payment;

        const {
            txtHoldNote,
            btnConfirmHold,
            lineDiscountLineId,
            lineDiscountAmount,
            lineDiscountItemName,
            btnSaveLineDiscount,
            btnClearLineDiscount,

            qtyEditLineId,
            qtyEditItemName,
            qtyEditValue,
            btnSaveQtyEdit,

            confirmTitle,
            confirmMessage,
            confirmNoteBox,
            confirmNoteLabel,
            confirmNoteInput,
            btnConfirmAction
        } = dom.support;

        const {
            paymentModalEl,
            customerModalEl,
            quickCreateCustomerModalEl,
            holdModalEl,
            confirmModalEl,
            lineDiscountModalEl,
            qtyEditModalEl
        } = dom.modalElements;

        const cartRetryStateEl = document.getElementById('posCartRetryState');
        const paymentRetryStateEl = document.getElementById('posPaymentRetryState');
        const holdRetryStateEl = document.getElementById('posHoldRetryState');
        const invoiceIntentModalEl =
    document.getElementById('invoiceIntentModal');

const invoiceIntentOrderText =
    document.getElementById('invoiceIntentOrderText');

const invoiceIntentErrorBox =
    document.getElementById('invoiceIntentErrorBox');

const btnInvoiceIntentManual =
    document.getElementById('btnInvoiceIntentManual');

const btnInvoiceIntentAutomatic =
    document.getElementById('btnInvoiceIntentAutomatic');

const invoiceIntentModal =
    invoiceIntentModalEl && window.bootstrap?.Modal
        ? window.bootstrap.Modal.getOrCreateInstance(
            invoiceIntentModalEl,
            {
                backdrop: 'static',
                keyboard: false
            })
        : null;

let pendingInvoiceIntentOrderId = null;
let pendingPostPaymentPrintOrderId = null;
let pendingAskBeforePrintingReceipt = false;
let pendingReceiptPrint = null;
let invoiceIntentBusy = false;
const checkoutFeedback = window.PosCheckoutFeedback?.create({
    modal: invoiceIntentModalEl, submit: submitInvoiceIntent,
    choosePrint: chooseReceiptPrint,
    isBusy: () => invoiceIntentBusy
});

        const {
            paymentModal,
            customerModal,
            quickCreateCustomerModal,
            holdModal,
            confirmModal,
            lineDiscountModal,
            qtyEditModal
        } = dom.modals;

        const {
            formatMoney,
            escapeHtml,
            showSuccess,
            showWarning,
            showError,
            getJson,
            postJson,
            patchJson,
            patchWithToken,
            deleteJson,
            deleteWithToken,
            withButtonLoading,
            runPosAction,
            normalizeApiError,
            refreshUiLocks,
            applyDraftActionSuccess,
            applyScreenActionSuccess,

            executeWithRetry,

            registerRetryUi,
            showRetryingState,
            showRetrySuccessState,
            showRetryFailureState,
            clearRetryState,
            buildRetryAttemptText,
            buildRetryDelayText
        } = window.PosCommon;

        const {
            renderPayments,
            renderCustomerInfo,
            renderHeldList,
            renderPaymentModalDraft,
            renderPaymentPreview,
            renderScreen,
            syncDraftToUi,
            renderNetworkBanner
        } = window.PosRender;

        const setLastRealtimeState =
            typeof window.PosState?.setLastRealtimeState === 'function'
                ? window.PosState.setLastRealtimeState
                : function (state, payload) {
                    if (!state?.runtime) return;
                    state.runtime.lastRealtimeEventType = String(payload?.eventType || '').trim();
                    state.runtime.lastRealtimeEventId = String(payload?.eventId || '').trim();
                };

        const setLastRefreshState =
            typeof window.PosState?.setLastRefreshState === 'function'
                ? window.PosState.setLastRefreshState
                : function (state, payload) {
                    if (!state?.runtime) return;

                    if (payload?.reason !== undefined) {
                        state.runtime.lastRefreshReason = String(payload?.reason || '').trim();
                    }

                    if (payload?.scope !== undefined) {
                        state.runtime.lastRefreshScope = String(payload?.scope || '').trim();
                    }

                    if (payload?.startedAt !== undefined) {
                        state.runtime.lastRefreshStartedAt = payload.startedAt || null;
                    }

                    if (payload?.completedAt !== undefined) {
                        state.runtime.lastRefreshCompletedAt = payload.completedAt || null;
                    }
                };

        const realtimeRuntime = {
            refreshTimer: null,
            refreshPending: false,
            isRealtimeRefreshRunning: false,
            lastHandledEventId: null,
            pendingScope: 'draft'
        };

        function registerRetryScopes() {
            if (cartRetryStateEl) {
                registerRetryUi({
                    scope: 'cart',
                    target: cartRetryStateEl,
                    autoHideSuccessMs: 1500,
                    autoHideFailureMs: 0
                });
            }

            if (paymentRetryStateEl) {
                registerRetryUi({
                    scope: 'payment',
                    target: paymentRetryStateEl,
                    autoHideSuccessMs: 1500,
                    autoHideFailureMs: 0
                });
            }

            if (holdRetryStateEl) {
                registerRetryUi({
                    scope: 'hold',
                    target: holdRetryStateEl,
                    autoHideSuccessMs: 1500,
                    autoHideFailureMs: 0
                });
            }
        }

        function showCartRetryingUi(ctx) {
            showRetryingState('cart', {
                title: buildRetryAttemptText(ctx?.autoRetried, ctx?.policy?.maxAutoRetry),
                message: 'Hệ thống đang thử lại thao tác giỏ hàng.',
                detail: buildRetryDelayText(ctx?.delayMs)
            });
        }

        function showCartRetryRecoveredUi() {
            showRetrySuccessState('cart', {
                title: 'Đã phục hồi thao tác giỏ hàng',
                message: 'Dữ liệu giỏ hàng đã được đồng bộ lại.'
            });
        }

        function showCartRetryFailedUi(error) {
            showRetryFailureState('cart', {
                title: 'Thao tác giỏ hàng chưa hoàn tất',
                message: error?.message || 'Không thể xử lý thao tác giỏ hàng.',
                detail: 'Bạn có thể thử lại hoặc tải lại giỏ hàng.'
            });
        }

        function clearCartRetryUi() {
            clearRetryState('cart');
        }

        function showPaymentRetryingUi(ctx) {
            showRetryingState('payment', {
                title: buildRetryAttemptText(ctx?.autoRetried, ctx?.policy?.maxAutoRetry),
                message: 'Hệ thống đang kiểm tra lại trạng thái thanh toán.',
                detail: buildRetryDelayText(ctx?.delayMs)
            });
        }

        function showPaymentRetryRecoveredUi() {
            showRetrySuccessState('payment', {
                title: 'Đã phục hồi trạng thái thanh toán',
                message: 'Thông tin thanh toán đã được đồng bộ lại.'
            });
        }

        function showPaymentRetryFailedUi(error) {
            showRetryFailureState('payment', {
                title: 'Không thể thử lại tự động',
                message: error?.message || 'Không thể xác nhận thao tác thanh toán.',
                detail: 'Bạn có thể thử lại hoặc tải lại trạng thái thanh toán.'
            });
        }

        function clearPaymentRetryUi() {
            clearRetryState('payment');
        }

        function showHoldRetryingUi(ctx) {
            showRetryingState('hold', {
                title: buildRetryAttemptText(ctx?.autoRetried, ctx?.policy?.maxAutoRetry),
                message: 'Hệ thống đang thử lại thao tác giữ đơn.',
                detail: buildRetryDelayText(ctx?.delayMs)
            });
        }

        function showHoldRetryRecoveredUi() {
            showRetrySuccessState('hold', {
                title: 'Đã phục hồi thao tác giữ đơn',
                message: 'Trạng thái đơn giữ đã được đồng bộ lại.'
            });
        }

        function showHoldRetryFailedUi(error) {
            showRetryFailureState('hold', {
                title: 'Không thể giữ đơn tự động',
                message: error?.message || 'Không thể xác nhận thao tác giữ đơn.',
                detail: 'Bạn có thể thử lại hoặc tải lại trạng thái đơn.'
            });
        }

        function clearHoldRetryUi() {
            clearRetryState('hold');
        }

        function clearAllRetryUi() {
            clearCartRetryUi();
            clearPaymentRetryUi();
            clearHoldRetryUi();
        }

        function getPosRuntimeContext() {
            const shell = document.getElementById('posShell');

            return {
                storeId: Number(shell?.dataset?.storeId || 0),
                terminalId: String(shell?.dataset?.terminalId || '').trim(),
                terminalName: String(shell?.dataset?.terminalName || '').trim(),
                terminalCode: String(shell?.dataset?.terminalCode || '').trim()
            };
        }
        function readBootstrapError() {
            const el = document.getElementById('posBootstrapError');
            if (!el) return null;

            const raw = el.getAttribute('data-error');
            if (!raw || raw === 'null') return null;

            try {
                return JSON.parse(raw);
            } catch {
                return null;
            }
        }

        function generateTerminalId() {
            const key = 'pos_terminal_id';

            let id = localStorage.getItem(key);
            if (id) return id;

            id = 'T-' + Math.random().toString(36).substring(2, 10).toUpperCase();
            localStorage.setItem(key, id);

            return id;
        }

        function getCurrentTerminalId() {
            const runtimeContext = getPosRuntimeContext();

            return String(
                runtimeContext.terminalId ||
                posState?.realtime?.terminalId ||
                ''
            ).trim();
        }

        async function startSignalRConnection(connection, storeId, terminalId) {
            try {
                posState.realtime.connectionStatus = 'connecting';
                renderNetworkBanner(posState);

                await connection.start();
                await connection.invoke('JoinStoreGroup', storeId, terminalId);

                posState.realtime.connectionStatus = 'connected';
                renderNetworkBanner(posState);

                console.log('SignalR connected:', storeId, terminalId);
            } catch (err) {
                console.error('SignalR start failed:', err);

                posState.realtime.connectionStatus = 'disconnected';
                renderNetworkBanner(posState);

                setTimeout(function () {
                    startSignalRConnection(connection, storeId, terminalId);
                }, 5000);
            }
        }

        let signalRConnection = null;

        function initSignalR() {
            if (!window.signalR) {
                console.warn('SignalR library not loaded.');
                return;
            }

            const runtimeContext = getPosRuntimeContext();
            const storeId = runtimeContext.storeId;
            const terminalId =
                runtimeContext.terminalId ||
                posState?.realtime?.terminalId ||
                generateTerminalId();

            if (!storeId) {
                console.warn('Cannot init SignalR: missing storeId.');
                return;
            }

            posState.realtime = posState.realtime || {};
            posState.realtime.enabled = true;
            posState.realtime.terminalId = terminalId;
            posState.realtime.connectionStatus = 'disconnected';

            signalRConnection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/pos')
                .withAutomaticReconnect([0, 2000, 5000, 10000])
                .build();

            bindSignalREvents(signalRConnection);
            startSignalRConnection(signalRConnection, storeId, terminalId);
        }

        function focusBarcodeInput() {
            setTimeout(function () {
                txtBarcode?.focus();
                txtBarcode?.select();
            }, 80);
        }

function clearInvoiceIntentError() {
    if (!invoiceIntentErrorBox) return;

    invoiceIntentErrorBox.textContent = '';
    invoiceIntentErrorBox.classList.add('d-none');
}

function showInvoiceIntentError(message) {
    if (!invoiceIntentErrorBox) {
        showError?.(
            message ||
            'Không thể lưu phương thức phát hành hóa đơn.'
        );
        return;
    }

    invoiceIntentErrorBox.textContent =
        message ||
        'Không thể lưu phương thức phát hành hóa đơn.';

    invoiceIntentErrorBox.classList.remove('d-none');
}

function setInvoiceIntentBusy(busy) {
    invoiceIntentBusy = !!busy;
    ['btnReceiptPrint', 'btnReceiptDoNotPrint'].forEach(id => {
        const button = document.getElementById(id);
        if (button) button.disabled = invoiceIntentBusy;
    });
    if (!invoiceIntentBusy && pendingReceiptPrint) document.getElementById('btnReceiptDoNotPrint')?.focus();

    if (btnInvoiceIntentManual) {
        btnInvoiceIntentManual.disabled =
            invoiceIntentBusy;
    }

    if (btnInvoiceIntentAutomatic) {
        btnInvoiceIntentAutomatic.disabled =
            invoiceIntentBusy;
    }

    if (btnInvoiceIntentManual) {
        btnInvoiceIntentManual.classList.toggle(
            'disabled',
            invoiceIntentBusy
        );
    }

    if (btnInvoiceIntentAutomatic) {
        btnInvoiceIntentAutomatic.classList.toggle(
            'disabled',
            invoiceIntentBusy
        );
    }
}

function printReceiptAfterInvoiceIntent(
    orderId,
    preopenedWindow,
    postPayment = false
) {
    if (window.PosOffline?.print(orderId, postPayment, preopenedWindow)) {
        return;
    }

    const receiptUrl =
        '/admin/pos/receipt/' +
        encodeURIComponent(orderId);
    const printUrl = postPayment === true
        ? window.PosPrinting.postPaymentUrl(receiptUrl, orderId) : receiptUrl;

    if (
        preopenedWindow &&
        !preopenedWindow.closed
    ) {
        try {
            preopenedWindow.opener = null;
        } catch (_) {
        }

        preopenedWindow.location.replace(
            printUrl
        );

        return;
    }

    // Fallback nếu browser không cho pre-open.
    const opened = window.open(printUrl, '_blank');
    if (!opened) throw new Error('Trình duyệt đang chặn cửa sổ in. Cho phép cửa sổ bật lên rồi thử lại.');
    try { opened.opener = null; } catch (_) { }
}

async function submitInvoiceIntent(route) {
    const orderId =
        Number(window.PosOffline?.resolveOrderId(pendingInvoiceIntentOrderId) || pendingInvoiceIntentOrderId);

    if (
        invoiceIntentBusy || pendingReceiptPrint ||
        !Number.isInteger(orderId) ||
        orderId <= 0
    ) {
        return;
    }

    clearInvoiceIntentError();

    // Mở tab trắng ngay trong user gesture.
    // Sau await fetch browser vẫn cho ta điều hướng tab này.
    const printWindow = pendingAskBeforePrintingReceipt ? null :
        window.open(
            'about:blank',
            '_blank'
        );

    if (printWindow) {
        try {
            printWindow.opener = null;
        } catch (_) {
        }
    }

    setInvoiceIntentBusy(true);

    try {
        const data =
            await postJson(
                `/admin/pos/${orderId}/invoice-route`,
                {
                    route: route
                }
            );

        const postPayment = pendingPostPaymentPrintOrderId === pendingInvoiceIntentOrderId;
        const askPrint = (postPayment || pendingAskBeforePrintingReceipt) && (typeof data?.askBeforePrintingReceipt === 'boolean'
            ? data.askBeforePrintingReceipt : pendingAskBeforePrintingReceipt);
        if (askPrint) {
            try { printWindow?.close(); } catch (_) { }
            pendingReceiptPrint = { orderId: Number(data?.orderId) || orderId, postPayment };
            checkoutFeedback.askPrint();
        } else {
            pendingAskBeforePrintingReceipt = false;
            printReceiptAfterInvoiceIntent(Number(data?.orderId) || orderId, printWindow, postPayment);
        }
        // Keep the saved choice and visible error/retry controls if opening print fails.
        pendingPostPaymentPrintOrderId = null;
        pendingInvoiceIntentOrderId = null;
        if (!askPrint) invoiceIntentModal?.hide();
    } catch (error) {
        try {
            printWindow?.close();
        } catch (_) {
        }

        showInvoiceIntentError(
            error?.message ||
            'Không thể lưu phương thức phát hành hóa đơn. Vui lòng thử lại.'
        );
    } finally {
        setInvoiceIntentBusy(false);
        restorePendingInvoiceIntent();
    }
}

function chooseReceiptPrint(shouldPrint) {
    if (invoiceIntentBusy || !pendingReceiptPrint) return;
    clearInvoiceIntentError();
    setInvoiceIntentBusy(true);
    let printWindow = null;
    try {
        if (shouldPrint) {
            printWindow = window.open('about:blank', '_blank');
            if (!printWindow) throw new Error('Trình duyệt đang chặn cửa sổ in. Cho phép cửa sổ bật lên rồi bấm In bill để thử lại.');
            printReceiptAfterInvoiceIntent(pendingReceiptPrint.orderId, printWindow, pendingReceiptPrint.postPayment);
        }
        pendingReceiptPrint = null;
        invoiceIntentModal?.hide();
    } catch (error) {
        try { printWindow?.close(); } catch (_) { }
        showInvoiceIntentError(error?.message || 'Không mở được bill. Vui lòng thử lại.');
    } finally {
        setInvoiceIntentBusy(false);
    }
}

function restorePendingInvoiceIntent() {
    const orderId = window.PosOffline?.pendingInvoiceIntentOrderId();
    if (orderId && !invoiceIntentBusy && Number(pendingInvoiceIntentOrderId) !== Number(orderId))
        openReceiptPrint(orderId, '80', true, false, null, window.PosOffline?.receiptPrintPreference?.(orderId));
}

function openReceiptPrint(orderId, size, autoPrint, postPayment = false, cashSummary = null, askBeforePrintingReceipt = false) {
    if (pendingReceiptPrint) return;
    const targetOrderId =
        Number(orderId);

    if (
        !Number.isInteger(targetOrderId) ||
        targetOrderId <= 0
    ) {
        return;
    }

    pendingAskBeforePrintingReceipt = askBeforePrintingReceipt === true || (postPayment === true && window.PosOffline?.receiptPrintPreference?.(targetOrderId) === true);
    if (window.PosOffline?.invoiceIntentStatus(targetOrderId) && !pendingAskBeforePrintingReceipt) {
        printReceiptAfterInvoiceIntent(targetOrderId, null, postPayment === true);
        return;
    }

    // Fail closed:
    // nếu modal không tồn tại thì không được in bỏ qua intent.
    if (!invoiceIntentModal) {
        showError?.(
            'Không tải được bước chọn hóa đơn. ' +
            'Vui lòng tải lại màn hình POS.'
        );

        return;
    }

    pendingInvoiceIntentOrderId =
        targetOrderId;
    pendingPostPaymentPrintOrderId = postPayment === true ? targetOrderId : null;

    if (invoiceIntentOrderText) {
        invoiceIntentOrderText.textContent =
            `#${targetOrderId}`;
    }

    clearInvoiceIntentError();
    setInvoiceIntentBusy(false);
    checkoutFeedback?.begin(postPayment === true ? cashSummary : null);
    if (window.PosOffline?.invoiceIntentStatus(targetOrderId) && pendingAskBeforePrintingReceipt) {
        pendingReceiptPrint = { orderId: targetOrderId, postPayment: postPayment === true };
        pendingInvoiceIntentOrderId = null;
        pendingPostPaymentPrintOrderId = null;
        checkoutFeedback.askPrint();
    }

    posState.ui.modals =
        posState.ui.modals || {};

    posState.ui.modals.invoiceIntent =
        true;

    invoiceIntentModal.show();
}

        function nowIso() {
            return new Date().toISOString();
        }

        function nowMs() {
            return Date.now();
        }

        function getLastRefreshCompletedAtMs() {
            const raw = posState?.runtime?.lastRefreshCompletedAt;
            if (!raw) return 0;

            const time = new Date(raw).getTime();
            return Number.isFinite(time) ? time : 0;
        }

function hasAnyModalOpen() {
    const modals = posState?.ui?.modals || {};

    return !!(
        modals.payment ||
        modals.customer ||
        modals.quickCreateCustomer ||
        modals.lineDiscount ||
        modals.qtyEdit ||
        modals.hold ||
        modals.confirm ||
        modals.invoiceIntent
    );
}

        function hasPendingActions() {
            const pending = posState?.network?.pendingActions;
            return !!pending && typeof pending.size === 'number' && pending.size > 0;
        }

        function hasBusyScopes() {
            const busy = posState?.network?.busyScopes;
            return !!busy && typeof busy.size === 'number' && busy.size > 0;
        }

        function isPosInteractionBusy() {
            return hasPendingActions() || hasBusyScopes();
        }

        const refreshScopePriorityMap = {
            draft: 1,
            held: 2,
            full: 3
        };

        function normalizeRefreshScope(scope) {
            const normalized = String(scope || '').trim().toLowerCase();
            if (normalized === 'draft' || normalized === 'held' || normalized === 'full') {
                return normalized;
            }
            return 'full';
        }

        function getRefreshScopePriority(scope) {
            return refreshScopePriorityMap[normalizeRefreshScope(scope)] || refreshScopePriorityMap.full;
        }

        function mergeRefreshScope(currentScope, nextScope) {
            const currentNormalized = normalizeRefreshScope(currentScope);
            const nextNormalized = normalizeRefreshScope(nextScope);

            return getRefreshScopePriority(nextNormalized) >= getRefreshScopePriority(currentNormalized)
                ? nextNormalized
                : currentNormalized;
        }

        function resolveRealtimeRefreshScope(payload) {
            const explicitScope = String(payload?.refreshScope || '').trim().toLowerCase();
            if (explicitScope === 'held' || explicitScope === 'draft' || explicitScope === 'full') {
                return explicitScope;
            }

            const eventType = String(payload?.eventType || '').trim().toLowerCase();

            const draftEvents = new Set([
                'draft_created',
                'cart_created',
                'cart_ensured',
                'current_cart_switched',
                'cart_changed',
                'payment_changed',
                'customer_changed',
                'order_note_changed',
                'order_discount_changed'
            ]);

            if (draftEvents.has(eventType)) {
                return 'draft';
            }

            const heldEvents = new Set([
                'held_changed',
                'held_resumed',
                'order_finalized',
                'order_voided',
                'order_refunded',
                'cart_cancelled'
            ]);

            if (heldEvents.has(eventType)) {
                return 'held';
            }

            console.warn('Unknown eventType → fallback FULL:', eventType);
            return 'full';
        }

        function markScreenRefreshStarted(reason, scope) {
            const runtime = posState.runtime || {};
            runtime.isRefreshingScreen = true;

            setLastRefreshState(posState, {
                reason: reason || '',
                scope: normalizeRefreshScope(scope || 'full'),
                startedAt: nowIso()
            });
        }

        function markScreenRefreshCompleted(reason, scope) {
            const runtime = posState.runtime || {};
            runtime.isRefreshingScreen = false;

            setLastRefreshState(posState, {
                reason: reason !== undefined ? reason : runtime.lastRefreshReason,
                scope: normalizeRefreshScope(scope || runtime.lastRefreshScope || 'full'),
                completedAt: nowIso()
            });
        }

        function queueRefreshWhileBusy(options) {
            const nextScope = normalizeRefreshScope(options?.scope || 'full');
            const previousOptions = posState.runtime.queuedRefreshOptions || null;
            const mergedScope = mergeRefreshScope(previousOptions?.scope || 'draft', nextScope);

            posState.runtime.refreshRequestedWhileBusy = true;
            posState.runtime.queuedRefreshOptions = {
                reason: options?.reason || previousOptions?.reason || 'queued',
                silent: !!(options?.silent ?? previousOptions?.silent),
                force: !!(options?.force ?? previousOptions?.force),
                focusBarcode: (options?.focusBarcode ?? previousOptions?.focusBarcode) !== false,
                scope: mergedScope
            };

            setLastRefreshState(posState, {
                reason: options?.reason || 'queued',
                scope: mergedScope
            });
        }

        function shouldThrottleRefresh(options) {
            const force = !!options?.force;
            if (force) return false;

            const runtime = posState.runtime;
            const lastCompletedAtMs = getLastRefreshCompletedAtMs();
            const minGapMs = Number(runtime?.minRefreshGapMs || 0);

            if (!lastCompletedAtMs || minGapMs <= 0) {
                return false;
            }

            const diff = nowMs() - lastCompletedAtMs;
            return diff >= 0 && diff < minGapMs;
        }

        function mergeRefreshOptions(baseOptions, extraOptions) {
            const baseScope = normalizeRefreshScope(baseOptions?.scope || 'full');
            const extraScope = normalizeRefreshScope(extraOptions?.scope || baseScope);

            return {
                reason: extraOptions?.reason || baseOptions?.reason || 'manual',
                silent: !!(extraOptions?.silent ?? baseOptions?.silent),
                force: !!(extraOptions?.force ?? baseOptions?.force),
                focusBarcode: (extraOptions?.focusBarcode ?? baseOptions?.focusBarcode) !== false,
                scope: mergeRefreshScope(baseScope, extraScope)
            };
        }

        function applyScreenData(screen, options) {
            posState.business.screen = screen || null;
            posState.business.currentDraft = screen?.currentDraft || null;
            posState.business.currentOrderId = screen?.currentCart?.currentOrderId || null;
            posState.business.heldOrders = Array.isArray(screen?.heldOrders) ? screen.heldOrders : [];

            posState.offline.isOnline = true;
            posState.offline.lastSyncAt = nowIso();

            renderScreen(screen, posState);
            renderNetworkBanner(posState);
            refreshUiLocks?.(posState);

            if (options?.focusBarcode !== false) {
                focusBarcodeInput();
            }
        }

        function applyDraftData(draft, options) {
            const currentOrderId = draft?.orderId || draft?.id || draft?.OrderId || null;
            const currentScreen = posState.business.screen || {};

            posState.business.currentDraft = draft || null;
            posState.business.currentOrderId = currentOrderId;
            posState.business.screen = {
                ...currentScreen,
                currentDraft: draft || null,
                currentCart: {
                    ...(currentScreen?.currentCart || {}),
                    currentOrderId: currentOrderId
                },
                heldOrders: Array.isArray(posState.business.heldOrders) ? posState.business.heldOrders : []
            };

            posState.offline.isOnline = true;
            posState.offline.lastSyncAt = nowIso();

            syncDraftToUi(draft || null, posState);
            renderNetworkBanner(posState);
            refreshUiLocks?.(posState);
            clearCartRetryUi();

            if (options?.focusBarcode !== false) {
                focusBarcodeInput();
            }
        }

        function applyHeldOrdersData(heldOrders, options) {
            const nextHeldOrders = Array.isArray(heldOrders) ? heldOrders : [];
            const currentScreen = posState.business.screen || {};

            posState.business.heldOrders = nextHeldOrders;
            posState.business.screen = {
                ...currentScreen,
                currentDraft: posState.business.currentDraft || null,
                currentCart: {
                    ...(currentScreen?.currentCart || {}),
                    currentOrderId: posState.business.currentOrderId || null
                },
                heldOrders: nextHeldOrders
            };

            posState.offline.isOnline = true;
            posState.offline.lastSyncAt = nowIso();

            renderHeldList(nextHeldOrders);
            renderNetworkBanner(posState);
            refreshUiLocks?.(posState);
            clearHoldRetryUi();

            if (options?.focusBarcode !== false) {
                focusBarcodeInput();
            }
        }

        // B8:
        // performScreenRefresh là action LOAD_SCREEN.
        // B8.1 chỉ chuẩn bị retry policy.
        // B8.2 sẽ bọc retry executor tại đây.

        async function performScreenRefresh(options) {
            const runtime = posState.runtime;
            const reason = options?.reason || 'manual';
            const silent = !!options?.silent;
            const focusBarcode = options?.focusBarcode !== false;
            const scope = normalizeRefreshScope(options?.scope || 'full');

            if (runtime.isRefreshingScreen) {
                queueRefreshWhileBusy(options);
                return null;
            }

            if (shouldThrottleRefresh(options)) {
                setLastRefreshState(posState, {
                    reason,
                    scope
                });
                return null;
            }

            runtime.refreshSeq = Number(runtime.refreshSeq || 0) + 1;
            const refreshToken = runtime.refreshSeq;
            runtime.activeRefreshToken = refreshToken;

            markScreenRefreshStarted(reason, scope);

            try {
                renderNetworkBanner(posState);
                refreshUiLocks?.(posState);

                const result = await executeWithRetry({
                    actionType: window.PosCommon.PosActionTypes.LOAD_SCREEN,

                    operation: async function () {
                        if (scope === 'draft') {
                            return await getJson('/admin/pos/cart/current');
                        }

                        if (scope === 'held') {
                            return await getJson('/admin/pos/orders/held');
                        }

                        return await getJson('/admin/pos/screen');
                    },

                });

                if (refreshToken !== posState.runtime.activeRefreshToken) {
                    return null;
                }

                if (!result?.ok) {
                    const err = result?.error || null;
                    const hasPosErrorCode =
                        err &&
                        typeof err === 'object' &&
                        typeof err.errorCode === 'string' &&
                        err.errorCode.length > 0;

                    // 1) backend đã trả lỗi POS chuẩn
                    if (hasPosErrorCode) {
                        posState.offline.isOnline = true;

                        renderNetworkBanner(posState);
                        refreshUiLocks?.(posState);

                        if (!silent && window.PosError) {
                            window.PosError.handle(err, {
                                displayMode: window.PosError.getUiBehavior(err),
                                showToast: false
                            });
                        }

                        return null;
                    }

                    // 2) fallback lỗi không chuẩn hóa
                    const normalized = normalizeApiError(err, 'Không thể tải màn hình POS.');

                    if (normalized?.type === 'network') {
                        posState.offline.isOnline = false;
                    } else {
                        posState.offline.isOnline = true;
                    }

                    renderNetworkBanner(posState);
                    refreshUiLocks?.(posState);

                    if (!silent && !normalized?.isAbort) {
                        showError(normalized?.message || 'Không thể tải màn hình POS.');
                    }

                    return null;
                }

                const response = result.data;

                if (scope === 'draft') {
                    applyDraftData(response, {
                        focusBarcode: focusBarcode
                    });
                } else if (scope === 'held') {
                    applyHeldOrdersData(response, {
                        focusBarcode: focusBarcode
                    });
                } else {
                    applyScreenData(response, {
                        focusBarcode: focusBarcode
                    });
                }

                return response;
            } finally {
                if (refreshToken === posState.runtime.activeRefreshToken) {
                    markScreenRefreshCompleted(reason, scope);
                    refreshUiLocks?.(posState);

                    if (runtime.refreshRequestedWhileBusy) {
                        const nextOptions = mergeRefreshOptions(
                            {
                                reason: 'queued-refresh',
                                silent: true,
                                force: true,
                                focusBarcode: true,
                                scope: 'full'
                            },
                            runtime.queuedRefreshOptions || {}
                        );

                        runtime.refreshRequestedWhileBusy = false;
                        runtime.queuedRefreshOptions = null;

                        await performScreenRefresh(nextOptions);
                    }
                }
            }
        }

        async function requestScreenRefresh(options) {
            const normalizedOptions = {
                reason: options?.reason || 'manual',
                silent: !!options?.silent,
                force: !!options?.force,
                focusBarcode: options?.focusBarcode !== false,
                scope: normalizeRefreshScope(options?.scope || 'full')
            };

            try {
                return await performScreenRefresh(normalizedOptions);
            } catch (err) {
                if (!normalizedOptions.silent && window.PosError) {
                    window.PosError.handle(err, {
                        displayMode: window.PosError.getUiBehavior(err),
                        showToast: false
                    });
                }

                return null;
            }
        }

        async function loadScreen() {
            return await requestScreenRefresh({
                reason: 'legacy-load-screen',
                silent: false,
                force: true,
                focusBarcode: true,
                scope: 'full'
            });
        }

        function shouldIgnoreRealtimeEvent(payload) {
            if (!payload) return true;

            const eventId = String(payload.eventId || '').trim();
            const eventType = String(payload.eventType || '').trim().toLowerCase();
            const sourceTerminalId = String(payload.terminalId || '').trim();
            const currentTerminalId = getCurrentTerminalId();

            if (!eventId || !eventType) {
                return true;
            }

            if (realtimeRuntime.lastHandledEventId === eventId) {
                return true;
            }

            if (posState.realtime.lastEventId === eventId) {
                return true;
            }

            const storeWideEvents = new Set([
                'held_changed',
                'held_resumed',
                'order_finalized',
                'order_voided',
                'order_refunded',
                'cart_cancelled'
            ]);

            // 1. Event từ terminal khác:
            // - với event draft/payment/customer... thì bỏ qua
            // - với event store-wide thì vẫn nhận
            if (!storeWideEvents.has(eventType)) {
                if (currentTerminalId && sourceTerminalId && currentTerminalId !== sourceTerminalId) {
                    return true;
                }
            }

            // 2. Event từ CHÍNH terminal hiện tại:
            // local action đã tự update UI rồi -> không refresh lại nữa
            if (currentTerminalId && sourceTerminalId && currentTerminalId === sourceTerminalId) {
                return true;
            }

            return false;
        }

        function markRealtimeEvent(payload) {
            const eventId = String(payload?.eventId || '').trim() || null;
            const occurredAtUtc = payload?.occurredAtUtc || nowIso();
            const eventType = String(payload?.eventType || '').trim();

            realtimeRuntime.lastHandledEventId = eventId;
            posState.realtime.lastEventId = eventId;
            posState.realtime.lastEventAt = occurredAtUtc;

            setLastRealtimeState(posState, {
                eventType,
                eventId
            });
        }

        async function tryRunRealtimeRefresh() {
            if (!realtimeRuntime.refreshPending) {
                return;
            }

            if (realtimeRuntime.isRealtimeRefreshRunning) {
                return;
            }

            if (isPosInteractionBusy()) {
                realtimeRuntime.refreshTimer = setTimeout(tryRunRealtimeRefresh, 400);
                return;
            }

            realtimeRuntime.refreshPending = false;
            realtimeRuntime.isRealtimeRefreshRunning = true;

            const scope = realtimeRuntime.pendingScope || 'full';

            try {
                await requestScreenRefresh({
                    reason: `realtime-pos-event:${scope}`,
                    silent: true,
                    force: true,
                    focusBarcode: !hasAnyModalOpen(),
                    scope: scope
                });
            } catch (_) {
            } finally {
                realtimeRuntime.isRealtimeRefreshRunning = false;
                refreshUiLocks?.(posState);

                if (realtimeRuntime.refreshPending) {
                    realtimeRuntime.refreshTimer = setTimeout(tryRunRealtimeRefresh, 250);
                }
            }
        }

        function scheduleRealtimeRefresh(payload) {
            const scope = resolveRealtimeRefreshScope(payload);
            const previousScope = realtimeRuntime.pendingScope || 'draft';
            const mergedScope = mergeRefreshScope(previousScope, scope);

            realtimeRuntime.refreshPending = true;
            realtimeRuntime.pendingScope = mergedScope;

            setLastRefreshState(posState, {
                reason: 'realtime-scheduled',
                scope: mergedScope
            });

            if (realtimeRuntime.refreshTimer) {
                clearTimeout(realtimeRuntime.refreshTimer);
                realtimeRuntime.refreshTimer = null;
            }
            if (!payload?.eventType) {
                return;
            }

            const delayMs = hasAnyModalOpen() ? 1200 : 250;
            realtimeRuntime.refreshTimer = setTimeout(tryRunRealtimeRefresh, delayMs);
        }

        function handleRealtimePosEvent(payload) {
            if (shouldIgnoreRealtimeEvent(payload)) {
                return;
            }

            markRealtimeEvent(payload);
            scheduleRealtimeRefresh(payload);
        }

        function bindSignalREvents(connection) {
            connection.on('joined', function (info) {
                console.log('SignalR joined:', info);
            });

            connection.on('pos:event', function (payload) {
                if (!payload) return;

                if ((payload.eventType || payload.EventType) === 'acb_payment_changed') {
                    window.dispatchEvent(new CustomEvent('acb:payment-changed', { detail: payload }));
                }
                console.log('SignalR pos:event', payload);
                handleRealtimePosEvent(payload);
            });

            connection.onreconnecting(function (err) {
                console.warn('SignalR reconnecting...', err);

                posState.realtime.connectionStatus = 'connecting';
                renderNetworkBanner(posState);
            });

            connection.onreconnected(async function (connectionId) {
                console.log('SignalR reconnected:', connectionId);

                posState.realtime.connectionStatus = 'connected';
                renderNetworkBanner(posState);

                try {
                    const runtimeContext = getPosRuntimeContext();
                    const storeId = runtimeContext.storeId;
                    const terminalId =
                        posState?.realtime?.terminalId ||
                        runtimeContext.terminalId ||
                        generateTerminalId();

                    await connection.invoke('JoinStoreGroup', storeId, terminalId);

                    console.log('SignalR rejoin success:', storeId, terminalId);
                } catch (err) {
                    console.error('SignalR rejoin failed:', err);
                }
            });

            connection.onclose(function (err) {
                console.error('SignalR closed:', err);

                posState.realtime.connectionStatus = 'disconnected';
                renderNetworkBanner(posState);
            });
        }

        let posCustomer = null;
        let posPayment = null;
        let posOrder = null;
        let posBarcode = null;

        posOrder = window.PosOrder.create({
            posState,
            elements: {
                btnNewCart,
                btnHoldCart,
                btnClearCartLines,
                btnOpenPayment,
                btnFinalizeCart,
                btnCancelCart,
                txtOrderNote,
                btnSaveOrderNote,
                txtOrderDiscount,
                btnSaveOrderDiscount,
                btnClearOrderDiscount,
                lineDiscountLineId,
                lineDiscountAmount,
                lineDiscountItemName,
                btnSaveLineDiscount,
                btnClearLineDiscount,
                qtyEditLineId,
                qtyEditValue,
                qtyEditItemName,
                btnSaveQtyEdit,
                txtHoldNote,
                btnConfirmHold,
                confirmTitle,
                confirmMessage,
                confirmNoteBox,
                confirmNoteLabel,
                confirmNoteInput,
                btnConfirmAction,
                currentDraftBody,
                heldList
            },
            modals: {
                holdModal,
                confirmModal,
                lineDiscountModal,
                qtyEditModal
            },
            helpers: {
                postJson,
                patchJson,
                deleteWithToken,
                patchWithToken,
                withButtonLoading,
                runPosAction,
                focusBarcodeInput,
                syncDraftToUi,
                showSuccess,
                showError,
                formatMoney,
                openReceiptPrint,
                openPaymentModal: function () {
                    return posPayment?.openPaymentModal?.();
                },
                requestScreenRefresh,
                loadScreen,
                applyDraftActionSuccess,
                applyScreenActionSuccess
            }
        });

        posPayment = window.PosPayment.create({
            posState,
            elements: {
                payMethod,
                payAmount,
                payReference,
                payProvider,
                btnAddPayment,
                btnFinalizeFromPaymentModal,
                btnPayExact,
                paymentModalEl
            },
            modals: {
                paymentModal,
                confirmModal
            },
            helpers: {
                postJson,
                deleteJson,
                withButtonLoading,
                runPosAction,
                focusBarcodeInput,
                syncDraftToUi,
                showSuccess,
                showError,
                renderPayments,
                renderPaymentModalDraft,
                renderPaymentPreview,
                openConfirmModal: function (options) {
                    return posOrder?.openConfirmModal?.(options);
                },
                openReceiptPrint,
                requestScreenRefresh,
                loadScreen,
                applyDraftActionSuccess,
                applyScreenActionSuccess
            }
        });

        posCustomer = window.PosCustomer.create({
            posState,
            customerSearchState,
            elements: {
                customerInfoBox,
                txtCustomerKeyword,
                btnSearchCustomer,
                customerSearchResult,
                btnClearCustomer,
                btnOpenQuickCreateCustomer,
                qcCustomerName,
                qcCustomerPhone,
                qcCustomerAddress,
                qcCustomerNote,
                btnSubmitQuickCreateCustomer,
                customerModalEl,
                quickCreateCustomerModalEl
            },
            modals: {
                customerModal,
                quickCreateCustomerModal
            },
            helpers: {
                fetchJson: getJson,
                postJson,
                deleteWithToken,
                withButtonLoading,
                runPosAction,
                focusBarcodeInput,
                syncDraftToUi,
                renderCustomerInfo,
                showSuccess,
                showError,
                requestScreenRefresh,
                loadScreen,
                applyDraftActionSuccess,
                applyScreenActionSuccess
            }
        });

        posBarcode = window.PosBarcode.create({
            posState,
            barcodeSearchState,
            elements: {
                txtBarcode,
                btnScan,
                btnFocusBarcode,
                barcodeAutocomplete
            },
            helpers: {
                formatMoney,
                escapeHtml,
                fetchJson: getJson,
                postJson,
                runPosAction,
                focusBarcodeInput,
                syncDraftToUi,
                showSuccess,
                showError,
                applyDraftActionSuccess,
                applyScreenActionSuccess
            }
        });

        const {
            resetPaymentForm
        } = posPayment;

        const keyboard = window.PosKeyboard.create({
            posState,
            elements: {
                txtBarcode,
                txtCustomerKeyword,
                payAmount,
                confirmNoteInput,
                lineDiscountAmount,
                txtOrderDiscount,
                txtOrderNote,
                qtyEditValue,
                btnNewCart,
                btnHoldCart,
                btnClearCartLines,
                btnOpenPayment,
                btnFinalizeCart,
                btnCancelCart,
                btnSaveQtyEdit
            }
        });


        function bindModalEvents() {
            paymentModalEl?.addEventListener('shown.bs.modal', function () {
                posState.ui.modals.payment = true;
            });

            paymentModalEl?.addEventListener('hidden.bs.modal', function () {
                posState.ui.modals.payment = false;
                resetPaymentForm?.();
                clearPaymentRetryUi();
                focusBarcodeInput();
            });

            customerModalEl?.addEventListener('shown.bs.modal', function () {
                posState.ui.modals.customer = true;
            });

            customerModalEl?.addEventListener('hidden.bs.modal', function () {
                posState.ui.modals.customer = false;
                document.activeElement?.blur();
                focusBarcodeInput();
            });

            quickCreateCustomerModalEl?.addEventListener('shown.bs.modal', function () {
                posState.ui.modals.quickCreateCustomer = true;
            });

            quickCreateCustomerModalEl?.addEventListener('hidden.bs.modal', function () {
                posState.ui.modals.quickCreateCustomer = false;
            });

            holdModalEl?.addEventListener('shown.bs.modal', function () {
                posState.ui.modals.hold = true;
            });

            holdModalEl?.addEventListener('hidden.bs.modal', function () {
                posState.ui.modals.hold = false;
                if (txtHoldNote) txtHoldNote.value = '';
                clearHoldRetryUi();
                focusBarcodeInput();
            });

            confirmModalEl?.addEventListener('shown.bs.modal', function () {
                posState.ui.modals.confirm = true;
            });

            confirmModalEl?.addEventListener('hidden.bs.modal', function () {
                posState.ui.modals.confirm = false;
                posState.ui.confirm.pendingAction = null;
                if (confirmNoteInput) confirmNoteInput.value = '';
                focusBarcodeInput();
            });

            lineDiscountModalEl?.addEventListener('shown.bs.modal', function () {
                posState.ui.modals.lineDiscount = true;

                setTimeout(function () {
                    lineDiscountAmount?.focus();
                    lineDiscountAmount?.select?.();
                }, 0);
            });

            lineDiscountModalEl?.addEventListener('hidden.bs.modal', function () {
                posState.ui.modals.lineDiscount = false;
                focusBarcodeInput();
            });

            qtyEditModalEl?.addEventListener('shown.bs.modal', function () {
                posState.ui.modals.qtyEdit = true;

                setTimeout(function () {
                    qtyEditValue?.focus();
                    qtyEditValue?.select?.();
                }, 0);
            });

            qtyEditModalEl?.addEventListener('hidden.bs.modal', function () {
                posState.ui.modals.qtyEdit = false;
                if (qtyEditLineId) qtyEditLineId.value = '';
                if (qtyEditValue) qtyEditValue.value = '';
                focusBarcodeInput();
            });
        }

        function bindRuntimeEvents() {
            window.addEventListener('online', async function () {
                posState.offline.isOnline = true;
                renderNetworkBanner(posState);
                refreshUiLocks?.(posState);

                try {
                    await requestScreenRefresh({
                        reason: 'browser-online',
                        silent: true,
                        force: true,
                        focusBarcode: !hasAnyModalOpen(),
                        scope: 'full'
                    });
                } catch (_) {
                }
            });

            window.addEventListener('offline', function () {
                posState.offline.isOnline = false;
                renderNetworkBanner(posState);
                refreshUiLocks?.(posState);
            });

            document.addEventListener('visibilitychange', async function () {
                if (document.hidden) {
                    return;
                }

                posState.runtime.lastVisibleAt = nowIso();

                if (hasAnyModalOpen()) {
                    return;
                }

                const lastSyncAtMs = new Date(
                    posState?.offline?.lastSyncAt ||
                    posState?.network?.lastSuccessAt ||
                    0
                ).getTime();

                const gapMs = Number(posState.runtime.visibilityRefreshGapMs || 0);
                const ageMs = lastSyncAtMs ? (nowMs() - lastSyncAtMs) : Number.MAX_SAFE_INTEGER;

                if (ageMs < gapMs) {
                    return;
                }

                try {
                    await requestScreenRefresh({
                        reason: 'tab-visible',
                        silent: true,
                        force: false,
                        focusBarcode: true,
                        scope: 'full'
                    });
                } catch (_) {
                }
            });

            window.addEventListener('focus', async function () {
                posState.runtime.lastFocusedAt = nowIso();

                if (hasAnyModalOpen()) {
                    return;
                }

                const lastSyncAtMs = new Date(
                    posState?.offline?.lastSyncAt ||
                    posState?.network?.lastSuccessAt ||
                    0
                ).getTime();

                const gapMs = Number(posState.runtime.focusRefreshGapMs || 0);
                const ageMs = lastSyncAtMs ? (nowMs() - lastSyncAtMs) : Number.MAX_SAFE_INTEGER;

                if (ageMs < gapMs) {
                    return;
                }

                try {
                    await requestScreenRefresh({
                        reason: 'window-focus',
                        silent: true,
                        force: false,
                        focusBarcode: true,
                        scope: 'full'
                    });
                } catch (_) {
                }
            });
        }
        function bindInvoiceIntentEvents() {
    btnInvoiceIntentManual
        ?.addEventListener(
            'click',
            async function () {
                await submitInvoiceIntent(2);
            });

    btnInvoiceIntentAutomatic
        ?.addEventListener(
            'click',
            async function () {
                await submitInvoiceIntent(1);
            });

    invoiceIntentModalEl
        ?.addEventListener(
            'shown.bs.modal',
            function () {
                posState.ui.modals =
                    posState.ui.modals || {};

                posState.ui.modals.invoiceIntent =
                    true;
            });

    invoiceIntentModalEl
        ?.addEventListener(
            'hidden.bs.modal',
            function () {
                posState.ui.modals =
                    posState.ui.modals || {};

                posState.ui.modals.invoiceIntent =
                    false;

                clearInvoiceIntentError();
                focusBarcodeInput();
            });
}
function bindModuleEvents() {
    posCustomer.bindEvents();
    posPayment.bindEvents();
    posBarcode.bindEvents();
    posOrder.bindEvents();

    bindInvoiceIntentEvents();
}
        function bindMetaPanelToggle() {
            const { metaToggleButtons, metaSections } = dom.common;

            if (!metaToggleButtons || !metaSections) return;

            let activeSection = null;

            function closeAll() {
                metaSections.forEach(function (el) {
                    el.classList.add('is-collapsed');
                    el.classList.remove('is-active');
                });
                activeSection = null;
            }

            function openSection(section, key) {
                closeAll();

                section.classList.remove('is-collapsed');
                section.classList.add('is-active');
                activeSection = key;

                // focus
                if (key === 'note') {
                    txtOrderNote?.focus();
                }

                if (key === 'discount') {
                    txtOrderDiscount?.focus();
                    txtOrderDiscount?.select?.();
                }
            }

            // CLICK TOGGLE
            metaToggleButtons.forEach(function (btn) {
                btn.addEventListener('click', function (e) {
                    e.stopPropagation();

                    const key = btn.getAttribute('data-toggle-meta');
                    if (!key) return;

                    const section = document.querySelector('[data-meta-section="' + key + '"]');
                    if (!section) return;

                    const isOpen = activeSection === key;

                    if (isOpen) {
                        closeAll();
                    } else {
                        openSection(section, key);
                    }
                });
            });

            // CLICK OUTSIDE
            document.addEventListener('click', function (e) {
                if (!activeSection) return;

                const panel = e.target.closest('.pos-meta-section');
                if (!panel) {
                    closeAll();
                }
            });

            // ESC KEY
            document.addEventListener('keydown', function (e) {
                if (e.key === 'Escape' && activeSection) {
                    closeAll();
                }
            });

            // SAVE NOTE → CLOSE
            btnSaveOrderNote?.addEventListener('click', function () {
                setTimeout(closeAll, 150);
            });

            // SAVE DISCOUNT → CLOSE
            btnSaveOrderDiscount?.addEventListener('click', function () {
                setTimeout(closeAll, 150);
            });

            // CLEAR DISCOUNT → CLOSE
            btnClearOrderDiscount?.addEventListener('click', function () {
                setTimeout(closeAll, 150);
            });
            txtOrderDiscount?.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    btnSaveOrderDiscount?.click();
                }

                if (e.key === 'Escape') {
                    e.preventDefault();
                    closeAll();
                    focusBarcodeInput();
                }
            });

            txtOrderNote?.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' && e.ctrlKey) {
                    e.preventDefault();
                    btnSaveOrderNote?.click();
                }

                if (e.key === 'Escape') {
                    e.preventDefault();
                    closeAll();
                    focusBarcodeInput();
                }
            });
        }
        window.PosErrorActionContext = {
            focusBarcodeInput: focusBarcodeInput,
            requestScreenRefresh: requestScreenRefresh,
            openHoldModal: function () {
                if (posOrder && typeof posOrder.openHoldModal === 'function') {
                    posOrder.openHoldModal();
                }
            },
            cancelCurrentCart: async function () {
                if (posOrder && typeof posOrder.cancelCurrentCart === 'function') {
                    await posOrder.cancelCurrentCart();
                }
            },
            goToShiftPage: function () {
                window.location.href = '/admin/pos-shift';
            }
        };
        async function init() {
            await window.PosOffline?.init();
            if (window.PosError.redirectToShiftIfNeeded(window.PosOffline?.status()?.sessionIssue?.code)) return;
            window.addEventListener('pos:offline-status', function () {
                const offline = window.PosOffline?.status();
                if (window.PosError.redirectToShiftIfNeeded(offline?.sessionIssue?.code)) return;
                if (offline?.ready) posState.offline.isOnline = offline.connected;
                restorePendingInvoiceIntent();
                renderNetworkBanner(posState);
                refreshUiLocks?.(posState);
            });
            window.addEventListener('pos:offline-synced', function () {
                requestScreenRefresh({ reason: 'offline-synced', silent: true, scope: 'full', focusBarcode: false });
            });
            registerRetryScopes();
            bindModuleEvents();
            bindModalEvents();
            bindRuntimeEvents();
            bindMetaPanelToggle();
            keyboard.bind();
            posState.offline.isOnline = true;
            renderNetworkBanner(posState);
            clearAllRetryUi();

            const bootstrapError = readBootstrapError();
            if (bootstrapError) {
                window.PosError.handle(bootstrapError, {
                    displayMode: window.PosError.getUiBehavior(bootstrapError),
                    showToast: false
                });

                initSignalR();
                return;
            }

            await requestScreenRefresh({
                reason: 'init',
                silent: false,
                force: true,
                focusBarcode: true,
                scope: 'full'
            });
            restorePendingInvoiceIntent();

            initSignalR();
        }
        return {
            init,
            loadScreen,
            requestScreenRefresh,
            applyScreenData,
            state: posState,
            dom,
            modules: {
                posCustomer,
                posPayment,
                posOrder,
                posBarcode
            },
            retryUi: {

                showCartRetryingUi,
                showCartRetryRecoveredUi,
                showCartRetryFailedUi,
                clearCartRetryUi,

                showPaymentRetryingUi,
                showPaymentRetryRecoveredUi,
                showPaymentRetryFailedUi,
                clearPaymentRetryUi,

                showHoldRetryingUi,
                showHoldRetryRecoveredUi,
                showHoldRetryFailedUi,
                clearHoldRetryUi,

                clearAllRetryUi
            },
            debugState() {
                return posState;
            }
        };
    }

    return {
        create
    };
})();
