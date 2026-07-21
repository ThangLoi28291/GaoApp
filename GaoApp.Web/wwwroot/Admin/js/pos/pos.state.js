window.PosState = (function () {
    'use strict';

    function createSet() {
        return new Set();
    }

    function create() {
        const state = {
            /* =========================================================
               1. BUSINESS STATE
            ========================================================= */
            business: {
                screen: null,
                currentDraft: null,
                currentOrderId: null,
                heldOrders: []
            },

            /* =========================================================
               2. UI STATE
            ========================================================= */
            ui: {
                modals: {
                    payment: false,
                    customer: false,
                    quickCreateCustomer: false,
                    lineDiscount: false,
                    qtyEdit: false,
                    hold: false,
                    confirm: false
                },

                confirm: {
                    pendingAction: null
                }
            },

            /* =========================================================
               3. NETWORK STATE
               - pendingActions: action cụ thể đang chạy
               - busyScopes: scope logic đang bị khóa
            ========================================================= */
            network: {
                pendingActions: createSet(),
                busyScopes: createSet(),
                lastError: null,
                lastSuccessAt: null
            },

            /* =========================================================
               4. REALTIME STATE
            ========================================================= */
            realtime: {
                enabled: false,
                connectionStatus: 'disconnected',
                terminalId: null,
                lastEventId: null,
                lastEventAt: null
            },

            /* =========================================================
               5. OFFLINE STATE
            ========================================================= */
            offline: {
                isOnline: typeof navigator !== 'undefined' ? navigator.onLine : true,
                queue: [],
                lastSyncAt: null
            },

            /* =========================================================
               6. RUNTIME STATE
               - dùng riêng cho coordinator ở pos.app.js
               - mục tiêu:
                 + chống refresh cũ ghi đè refresh mới
                 + chống spam refresh
                 + cho phép "đang refresh thì đánh dấu refresh lại 1 lần"
                 + chuẩn bị nền cho SignalR / multi-tab / visibility refresh

               BƯỚC 5B.1:
                 + bổ sung debug / diagnostic state
                 + KHÔNG đổi flow hiện tại
            ========================================================= */
            runtime: {
                refreshSeq: 0,
                activeRefreshToken: 0,
                isRefreshingScreen: false,
                refreshRequestedWhileBusy: false,
                queuedRefreshOptions: null,

                lastRefreshReason: '',
                lastRefreshStartedAt: null,
                lastRefreshCompletedAt: null,

                // cooldown refresh chung
                minRefreshGapMs: 800,

                // khi quay lại tab/focus thì chỉ refresh nếu đã đủ lâu
                visibilityRefreshGapMs: 15000,
                focusRefreshGapMs: 10000,

                // lưu lần gần nhất user nhìn thấy tab/focus app
                lastVisibleAt: null,
                lastFocusedAt: null,

                /* =====================================================
                   DEBUG / DIAGNOSTIC
                   - bước này mới chỉ thêm state
                   - dữ liệu sẽ được ghi ở 5B.2 và 5B.3
                ===================================================== */
                lastActionKey: '',
                lastActionStatus: '',
                lastActionStartedAt: null,
                lastActionCompletedAt: null,
                lastActionMessage: '',

                lastRealtimeEventType: '',
                lastRealtimeEventId: '',

                // chuẩn bị cho 5C realtime refresh scope
                lastRefreshScope: '',

                // bật / tắt debug panel phía app nếu cần
                debugEnabled: false
            }
        };

        Object.defineProperties(state, {
            screen: {
                get() { return state.business.screen; },
                set(value) { state.business.screen = value; }
            },
            currentDraft: {
                get() { return state.business.currentDraft; },
                set(value) { state.business.currentDraft = value; }
            },
            currentOrderId: {
                get() { return state.business.currentOrderId; },
                set(value) { state.business.currentOrderId = value; }
            },

            paymentModalOpen: {
                get() { return state.ui.modals.payment; },
                set(value) { state.ui.modals.payment = !!value; }
            },
            customerModalOpen: {
                get() { return state.ui.modals.customer; },
                set(value) { state.ui.modals.customer = !!value; }
            },

            qtyEditModalOpen: {
                get() { return state.ui.modals.qtyEdit; },
                set(value) { state.ui.modals.qtyEdit = !!value; }
            },
            quickCreateCustomerModalOpen: {
                get() { return state.ui.modals.quickCreateCustomer; },
                set(value) { state.ui.modals.quickCreateCustomer = !!value; }
            },
            lineDiscountModalOpen: {
                get() { return state.ui.modals.lineDiscount; },
                set(value) { state.ui.modals.lineDiscount = !!value; }
            },
            holdModalOpen: {
                get() { return state.ui.modals.hold; },
                set(value) { state.ui.modals.hold = !!value; }
            },
            confirmModalOpen: {
                get() { return state.ui.modals.confirm; },
                set(value) { state.ui.modals.confirm = !!value; }
            },

            pendingConfirmAction: {
                get() { return state.ui.confirm.pendingAction; },
                set(value) { state.ui.confirm.pendingAction = value || null; }
            }
        });

        return state;
    }

    function createBarcodeSearchState() {
        return {
            items: [],
            activeIndex: -1,
            debounceTimer: null,
            keyword: '',
            lastKeyword: '',
            isLoading: false,
            isSubmitting: false,
            requestSeq: 0,
            lastAppliedSeq: 0,
            abortController: null,
            cache: null
        };
    }

    function createCustomerSearchState() {
        return {
            items: [],
            keyword: '',
            lastKeyword: '',
            isLoading: false,
            requestSeq: 0,
            lastAppliedSeq: 0,
            abortController: null,
            cache: null
        };
    }

    function normalizeScopes(scopes) {
        if (!Array.isArray(scopes)) return [];
        return scopes
            .map(x => String(x || '').trim())
            .filter(Boolean);
    }

    function isActionPending(posState, actionKey) {
        if (!posState?.network?.pendingActions || !actionKey) return false;
        return posState.network.pendingActions.has(actionKey);
    }

    function setActionPending(posState, actionKey, isPending) {
        if (!posState?.network?.pendingActions || !actionKey) return;

        if (isPending) {
            posState.network.pendingActions.add(actionKey);
        } else {
            posState.network.pendingActions.delete(actionKey);
        }
    }

    function setBusyScopes(posState, scopes, isBusy) {
        if (!posState?.network?.busyScopes) return;

        const normalized = normalizeScopes(scopes);

        normalized.forEach(scope => {
            if (isBusy) {
                posState.network.busyScopes.add(scope);
            } else {
                posState.network.busyScopes.delete(scope);
            }
        });
    }

    function isScopeBusy(posState, scope) {
        if (!posState?.network?.busyScopes || !scope) return false;
        return posState.network.busyScopes.has(scope);
    }

    function hasAnyBusyScope(posState, scopes) {
        if (!posState?.network?.busyScopes) return false;

        const normalized = normalizeScopes(scopes);
        return normalized.some(scope => posState.network.busyScopes.has(scope));
    }

    function getBusyScopes(posState) {
        if (!posState?.network?.busyScopes) return [];
        return Array.from(posState.network.busyScopes.values());
    }

    function setLastError(posState, error) {
        if (!posState?.network) return;
        posState.network.lastError = error || null;
    }

    function markNetworkSuccess(posState) {
        if (!posState?.network) return;
        posState.network.lastSuccessAt = new Date().toISOString();
        posState.network.lastError = null;
    }

    function isPosOnline(posState) {
        if (posState?.offline?.isOnline === false) {
            return false;
        }

        if (typeof navigator !== 'undefined' && navigator.onLine === false) {
            return false;
        }

        return true;
    }

    /* =========================================================
       7. DEBUG / DIAGNOSTIC HELPER
       - BƯỚC 5B.1
       - bước này chỉ thêm helper ghi runtime state
       - các file khác sẽ gọi ở 5B.2 / 5B.3
    ========================================================= */
    function setLastActionState(posState, payload) {
        if (!posState?.runtime) return;

        const runtime = posState.runtime;

        runtime.lastActionKey = String(payload?.key || '').trim();
        runtime.lastActionStatus = String(payload?.status || '').trim();

        if (payload?.startedAt !== undefined) {
            runtime.lastActionStartedAt = payload.startedAt || null;
        }

        if (payload?.completedAt !== undefined) {
            runtime.lastActionCompletedAt = payload.completedAt || null;
        }

        runtime.lastActionMessage = String(payload?.message || '').trim();
    }

    function setLastRealtimeState(posState, payload) {
        if (!posState?.runtime) return;

        posState.runtime.lastRealtimeEventType = String(payload?.eventType || '').trim();
        posState.runtime.lastRealtimeEventId = String(payload?.eventId || '').trim();
    }

    function setLastRefreshState(posState, payload) {
        if (!posState?.runtime) return;

        const runtime = posState.runtime;

        if (payload?.reason !== undefined) {
            runtime.lastRefreshReason = String(payload?.reason || '').trim();
        }

        if (payload?.scope !== undefined) {
            runtime.lastRefreshScope = String(payload?.scope || '').trim();
        }

        if (payload?.startedAt !== undefined) {
            runtime.lastRefreshStartedAt = payload.startedAt || null;
        }

        if (payload?.completedAt !== undefined) {
            runtime.lastRefreshCompletedAt = payload.completedAt || null;
        }
    }

    function setDebugEnabled(posState, enabled) {
        if (!posState?.runtime) return;
        posState.runtime.debugEnabled = !!enabled;
    }

    return {
        create,
        createBarcodeSearchState,
        createCustomerSearchState,

        isActionPending,
        setActionPending,
        setBusyScopes,
        isScopeBusy,
        hasAnyBusyScope,
        getBusyScopes,
        setLastError,
        markNetworkSuccess,
        isPosOnline,

        // BƯỚC 5B.1
        setLastActionState,
        setLastRealtimeState,
        setLastRefreshState,
        setDebugEnabled
    };
})();