/* =========================================================
   FILE: pos.common.js
   Mục đích:
   - Chứa helper dùng chung cho toàn màn hình POS
   - Không đụng DOM nghiệp vụ cụ thể
   - BƯỚC 4A:
     + chuẩn hóa error pipeline production
     + hỗ trợ toast / inline / silent
     + chuẩn bị cho modal/form error UX

   - BƯỚC 5B.2:
     + gắn debug / diagnostic runtime vào runPosAction(...)
     + KHÔNG đổi nghiệp vụ hiện tại
========================================================= */

window.PosCommon = (function () {
    'use strict';

    const globalActionLocker = createActionLocker();

    const uiLockRegistry = new Map();
    let uiLockSeq = 0;

    const retryUiRegistry = new Map();

    /* =========================================================
   RETRY CONSTANTS / HELPERS
   - B8.1:
     + chỉ khai báo policy metadata
     + CHƯA chạy retry thật
========================================================= */

    const PosActionTypes = {
        LOAD_SCREEN: 'load-screen',
        ADD_PRODUCT: 'add-product',
        HOLD_CART: 'hold-cart',
        PAYMENT: 'payment',
        GENERIC_READ: 'generic-read',
        GENERIC_WRITE: 'generic-write'
    };

    const PosRetryBehavior = {
        NONE: 'none',
        AUTO: 'auto',
        MANUAL_RETRY: 'manual_retry',
        RELOAD_ONLY: 'reload_only',
        AUTO_THEN_MANUAL: 'auto_then_manual'
    };

    const PosRetryClassification = {
        TRANSIENT: 'transient',
        BUSINESS: 'business',
        UNKNOWN: 'unknown',
        NON_RETRYABLE: 'non-retryable'
    };

    function resolveControlTarget(target) {
        if (!target) return null;

        if (isDomElement(target)) {
            return target;
        }

        if (typeof target === 'string') {
            return document.querySelector(target);
        }

        return null;
    }

    function nextUiLockId() {
        uiLockSeq += 1;
        return `ui-lock-${uiLockSeq}`;
    }

    function nextRetryUiId(scope) {
        const safeScope = String(scope || 'global').trim().toLowerCase() || 'global';
        return `pos-retry-ui-${safeScope}`;
    }

    function normalizeArray(value) {
        if (!Array.isArray(value)) return [];
        return value
            .map(x => String(x || '').trim())
            .filter(Boolean);
    }

    function isPosOnline(posState) {
        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.isPosOnline === 'function') {
            return stateHelpers.isPosOnline(posState);
        }

        const networkState = posState?.network || {};
        const status = String(networkState.status || '').trim().toLowerCase();

        if (networkState.isOnline === false) {
            return false;
        }

        if (status === 'offline') {
            return false;
        }

        if (typeof navigator !== 'undefined' && navigator.onLine === false) {
            return false;
        }

        return true;
    }

    function isActionPending(posState, actionKey) {
        const key = String(actionKey || '').trim();
        if (!key) return false;

        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.isActionPending === 'function') {
            return stateHelpers.isActionPending(posState, key);
        }

        return !!posState?.network?.pendingActions?.has?.(key);
    }

    function getUiLockMessage(entry, state) {
        if (state.reason === 'offline') {
            return entry.offlineMessage || 'Đang offline.';
        }

        if (state.reason === 'busy-scope') {
            return entry.busyMessage || 'Đang bận xử lý thao tác khác.';
        }

        if (state.reason === 'pending-action') {
            return entry.pendingMessage || 'Tác vụ đang được xử lý.';
        }

        return entry.defaultMessage || '';
    }

    function setControlLockedState(target, locked, message) {
        const el = resolveControlTarget(target);
        if (!el) return;

        const tagName = String(el.tagName || '').toLowerCase();
        const isButtonLike =
            tagName === 'button' ||
            tagName === 'input' ||
            el.getAttribute('role') === 'button';

        if (!el.dataset.uiLockOriginalTitle) {
            el.dataset.uiLockOriginalTitle = el.getAttribute('title') || '';
        }

        if (locked) {
            if (isButtonLike || 'disabled' in el) {
                el.disabled = true;
            }

            el.dataset.uiLocked = 'true';
            el.dataset.uiLockReason = message || '';
            el.setAttribute('aria-disabled', 'true');

            if (message) {
                el.setAttribute('title', message);
            }

            if (typeof el.classList?.add === 'function') {
                el.classList.add('is-ui-locked');
            }

            return;
        }

        if (el.dataset.loading !== 'true') {
            if (isButtonLike || 'disabled' in el) {
                el.disabled = false;
            }
        }

        el.dataset.uiLocked = 'false';
        el.dataset.uiLockReason = '';
        el.setAttribute('aria-disabled', 'false');

        if (typeof el.classList?.remove === 'function') {
            el.classList.remove('is-ui-locked');
        }

        const originalTitle = el.dataset.uiLockOriginalTitle || '';
        if (originalTitle) {
            el.setAttribute('title', originalTitle);
        } else {
            el.removeAttribute('title');
        }
    }

    function evaluateUiLockEntry(posState, entry) {
        if (!entry) {
            return {
                locked: false,
                reason: '',
                message: ''
            };
        }

        const requireOnline = entry.requireOnline === true;
        const busyScopes = normalizeArray(entry.busyScopes);
        const pendingActions = normalizeArray(entry.pendingActions);

        if (requireOnline && !isPosOnline(posState)) {
            return {
                locked: true,
                reason: 'offline',
                message: entry.offlineMessage || 'Đang offline.'
            };
        }

        if (busyScopes.length && hasAnyBusyScope(posState, busyScopes)) {
            return {
                locked: true,
                reason: 'busy-scope',
                message: entry.busyMessage || 'Đang bận xử lý thao tác khác.'
            };
        }

        const matchedPendingAction = pendingActions.find(actionKey => isActionPending(posState, actionKey));
        if (matchedPendingAction) {
            return {
                locked: true,
                reason: 'pending-action',
                message: entry.pendingMessage || 'Tác vụ đang được xử lý.'
            };
        }

        return {
            locked: false,
            reason: '',
            message: ''
        };
    }

    function applyUiLockEntry(posState, entry) {
        if (!entry) return;

        const state = evaluateUiLockEntry(posState, entry);
        const target = resolveControlTarget(entry.target);

        if (!target) return;

        if (typeof entry.beforeApply === 'function') {
            try {
                entry.beforeApply(target, state, posState);
            } catch (_) {
            }
        }

        setControlLockedState(target, state.locked, getUiLockMessage(entry, state));

        if (typeof entry.afterApply === 'function') {
            try {
                entry.afterApply(target, state, posState);
            } catch (_) {
            }
        }
    }

    function refreshUiLocks(posState) {
        uiLockRegistry.forEach(entry => {
            applyUiLockEntry(posState, entry);
        });
    }

    function registerUiLock(posState, options) {
        const target = resolveControlTarget(options?.target);
        if (!target) return null;

        const id = options?.id || nextUiLockId();

        const entry = {
            id,
            target,
            requireOnline: options?.requireOnline === true,
            busyScopes: normalizeArray(options?.busyScopes),
            pendingActions: normalizeArray(options?.pendingActions),
            offlineMessage: options?.offlineMessage || '',
            busyMessage: options?.busyMessage || '',
            pendingMessage: options?.pendingMessage || '',
            defaultMessage: options?.defaultMessage || '',
            beforeApply: typeof options?.beforeApply === 'function' ? options.beforeApply : null,
            afterApply: typeof options?.afterApply === 'function' ? options.afterApply : null
        };

        uiLockRegistry.set(id, entry);
        applyUiLockEntry(posState, entry);

        return {
            id,
            refresh: function () {
                applyUiLockEntry(posState, entry);
            },
            dispose: function () {
                uiLockRegistry.delete(id);
                setControlLockedState(target, false, '');
            }
        };
    }

    /* =========================================================
       1. BASIC HELPER
    ========================================================= */
    function getAntiForgeryToken() {
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        return tokenInput ? tokenInput.value : '';
    }

    function formatMoney(value) {
        const number = Number(value || 0);
        return number.toLocaleString('vi-VN', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function showSuccess(message) {
        if (window.toastr) {
            toastr.success(message);
            return;
        }
        console.log(message);
    }

    function showWarning(message) {
        if (window.toastr) {
            toastr.warning(message);
            return;
        }
        console.warn(message);
    }

    function showError(message) {
        const text = String(message || 'Có lỗi xảy ra.').replace(/\n/g, '<br/>');

        if (window.toastr) {
            toastr.error(text);
            return;
        }

        alert(String(message || 'Có lỗi xảy ra.'));
    }

    function getRetryDelay(attempt, profile) {
        const safeAttempt = Number(attempt || 1);
        const baseDelayMs = Number(profile?.baseDelayMs || 500);
        const maxDelayMs = Number(profile?.maxDelayMs || 2500);
        const multiplier = Number(profile?.multiplier || 2);

        let delay = baseDelayMs * Math.pow(multiplier, Math.max(0, safeAttempt - 1));

        if (delay > maxDelayMs) {
            delay = maxDelayMs;
        }

        return delay;
    }

    function sleepAsync(ms) {
        const safeMs = Number(ms || 0);
        return new Promise(resolve => setTimeout(resolve, safeMs));
    }

    function normalizeRetryCount(value, fallback) {
        const num = Number(value);
        if (!Number.isFinite(num) || num < 0) {
            return Number(fallback || 0);
        }

        if (num > 10) {
            return 10;
        }

        return Math.floor(num);
    }

    function isBrowserOnline() {
        if (typeof navigator === 'undefined') return true;
        if (typeof navigator.onLine !== 'boolean') return true;
        return navigator.onLine;
    }
    function createRetryResult(payload) {
        return {
            ok: payload?.ok === true,
            data: payload?.data ?? null,
            error: payload?.error ?? null,
            attempts: Number(payload?.attempts || 0),
            autoRetried: Number(payload?.autoRetried || 0),
            recovered: payload?.recovered === true,
            policy: payload?.policy || null
        };
    }
    async function executeWithRetry(options) {
        const actionType =
            options?.actionType ||
            PosActionTypes.GENERIC_READ;

        const operation =
            typeof options?.operation === 'function'
                ? options.operation
                : null;

        if (!operation) {
            throw new Error('executeWithRetry requires operation function.');
        }

        const onRetry =
            typeof options?.onRetry === 'function'
                ? options.onRetry
                : null;

        const onRetrySuccess =
            typeof options?.onRetrySuccess === 'function'
                ? options.onRetrySuccess
                : null;

        const onFinalFailure =
            typeof options?.onFinalFailure === 'function'
                ? options.onFinalFailure
                : null;

        const shouldRetry =
            typeof options?.shouldRetry === 'function'
                ? options.shouldRetry
                : null;

        let attempt = 0;
        let autoRetried = 0;
        let lastError = null;
        let resolvedPolicy = null;

        while (true) {
            attempt += 1;

            try {
                const data = await operation({
                    attempt,
                    autoRetried,
                    actionType
                });

                if (autoRetried > 0 && onRetrySuccess) {
                    try {
                        await onRetrySuccess({
                            attempt,
                            autoRetried,
                            actionType,
                            policy: resolvedPolicy,
                            data
                        });
                    } catch (_) {
                    }
                }

                return createRetryResult({
                    ok: true,
                    data,
                    attempts: attempt,
                    autoRetried,
                    recovered: autoRetried > 0,
                    policy: resolvedPolicy
                });
            } catch (error) {
                lastError = error;
                resolvedPolicy = window.PosError?.resolveRetryPolicy
                    ? window.PosError.resolveRetryPolicy(actionType, error)
                    : null;

                const allowAutoRetry =
                    resolvedPolicy?.allowAutoRetry === true &&
                    Number(resolvedPolicy?.maxAutoRetry || 0) > autoRetried;

                let retryApproved = allowAutoRetry;

                if (retryApproved && shouldRetry) {
                    try {
                        retryApproved = shouldRetry({
                            error,
                            actionType,
                            attempt,
                            autoRetried,
                            policy: resolvedPolicy
                        }) !== false;
                    } catch {
                        retryApproved = false;
                    }
                }

                if (!retryApproved) {
                    if (onFinalFailure) {
                        try {
                            await onFinalFailure({
                                error,
                                actionType,
                                attempt,
                                autoRetried,
                                policy: resolvedPolicy
                            });
                        } catch (_) {
                        }
                    }

                    return createRetryResult({
                        ok: false,
                        error,
                        attempts: attempt,
                        autoRetried,
                        recovered: false,
                        policy: resolvedPolicy
                    });
                }

                autoRetried += 1;

                const delayMs = getRetryDelay(autoRetried, resolvedPolicy || {});

                if (onRetry) {
                    try {
                        await onRetry({
                            error,
                            actionType,
                            attempt,
                            autoRetried,
                            delayMs,
                            policy: resolvedPolicy
                        });
                    } catch (_) {
                    }
                }

                await sleepAsync(delayMs);
            }
        }
    }

    /* =========================================================
       1.1. ACTION SUCCESS STRATEGY HELPER
       - BƯỚC 5A.1
    ========================================================= */
    function applyDraftActionSuccess(options) {
        const draft = options?.draft || null;
        const posState = options?.posState;
        const syncDraftToUi = options?.syncDraftToUi;
        const showSuccessFn = options?.showSuccess;
        const focusBarcodeInput = options?.focusBarcodeInput;
        const successMessage = String(options?.successMessage || '').trim();
        const afterSync =
            typeof options?.afterSync === 'function'
                ? options.afterSync
                : null;
        const focusBarcode = options?.focusBarcode !== false;

        if (!draft) {
            throw new Error('Draft action success requires draft payload.');
        }

        if (typeof syncDraftToUi === 'function') {
            syncDraftToUi(draft, posState);
        }

        if (afterSync) {
            afterSync(draft);
        }

        if (successMessage && typeof showSuccessFn === 'function') {
            showSuccessFn(successMessage);
        }

        if (focusBarcode && typeof focusBarcodeInput === 'function') {
            focusBarcodeInput();
        }

        return draft;
    }

    async function applyScreenActionSuccess(options) {
        const requestScreenRefresh = options?.requestScreenRefresh;
        const showSuccessFn = options?.showSuccess;
        const successMessage = String(options?.successMessage || '').trim();

        const beforeRefresh =
            typeof options?.beforeRefresh === 'function'
                ? options.beforeRefresh
                : null;

        const afterSuccess =
            typeof options?.afterSuccess === 'function'
                ? options.afterSuccess
                : null;

        if (beforeRefresh) {
            await beforeRefresh();
        }

        if (successMessage && typeof showSuccessFn === 'function') {
            showSuccessFn(successMessage);
        }

        if (afterSuccess) {
            await afterSuccess();
        }

        if (typeof requestScreenRefresh === 'function') {
            await requestScreenRefresh({
                reason: options?.reason || 'screen-action-success',
                silent: !!options?.silent,
                force: options?.force !== false,
                focusBarcode: options?.focusBarcode !== false
            });
        }

        return true;
    }

    function isAbortError(err) {
        return !!err && (err.name === 'AbortError' || err.code === 20);
    }

    function tryParseJson(text) {
        if (!text || typeof text !== 'string') return null;

        try {
            return JSON.parse(text);
        } catch {
            return null;
        }
    }

    function isDomElement(value) {
        return !!value && typeof value === 'object' && value.nodeType === 1;
    }

    /* =========================================================
       2. INLINE ERROR HELPER
    ========================================================= */
    function resolveInlineTarget(target) {
        if (!target) return null;

        if (isDomElement(target)) {
            return target;
        }

        if (typeof target === 'string') {
            return document.querySelector(target);
        }

        return null;
    }

    function clearInlineError(target) {
        const el = resolveInlineTarget(target);
        if (!el) return;

        el.innerHTML = '';
        el.style.display = 'none';
        el.dataset.errorVisible = 'false';
    }

    function setInlineError(target, message, variant) {
        const el = resolveInlineTarget(target);
        if (!el) return false;

        const safeMessage = escapeHtml(message || 'Đã có lỗi xảy ra.');
        const mode = (variant || 'error').toLowerCase();

        let className = 'pos-alert-error';

        if (mode === 'warning') {
            className = 'pos-alert-warning';
        } else if (mode === 'info') {
            className = 'pos-alert-info';
        }

        el.innerHTML = `
        <div class="${className}" role="alert">
            ${safeMessage}
        </div>
    `;

        el.style.display = '';
        el.dataset.errorVisible = 'true';
        return true;
    }

    function buildRetryStateHtml(options) {
        const state = String(options?.state || 'retrying').trim().toLowerCase();
        const title = String(options?.title || '').trim();
        const message = String(options?.message || '').trim();
        const detail = String(options?.detail || '').trim();

        let className = 'pos-retry-state pos-retry-state--retrying';

        if (state === 'success') {
            className = 'pos-retry-state pos-retry-state--success';
        } else if (state === 'failed') {
            className = 'pos-retry-state pos-retry-state--failed';
        }

        return `
            <div class="${className}" data-retry-ui-state="${escapeHtml(state)}" role="status" aria-live="polite">
                <div class="pos-retry-state__title">${escapeHtml(title || 'Đang thử lại...')}</div>
                ${message ? `<div class="pos-retry-state__message">${escapeHtml(message)}</div>` : ''}
                ${detail ? `<div class="pos-retry-state__detail">${escapeHtml(detail)}</div>` : ''}
            </div>
        `;
    }
    function resolveRetryUiTarget(target) {
        return resolveInlineTarget(target);
    }
    function registerRetryUi(options) {
        const scope = String(options?.scope || 'global').trim().toLowerCase() || 'global';
        const target = resolveRetryUiTarget(options?.target);

        if (!target) {
            return null;
        }

        const id = options?.id || nextRetryUiId(scope);

        const entry = {
            id,
            scope,
            target,
            autoHideSuccessMs: Number(options?.autoHideSuccessMs || 1800),
            autoHideFailureMs: Number(options?.autoHideFailureMs || 0),
            timerId: null
        };

        retryUiRegistry.set(scope, entry);

        return {
            id,
            scope,
            clear: function () {
                clearRetryState(scope);
            },
            dispose: function () {
                clearRetryState(scope);
                retryUiRegistry.delete(scope);
            }
        };
    }

    function getRetryUiEntry(scope) {
        const safeScope = String(scope || 'global').trim().toLowerCase() || 'global';
        return retryUiRegistry.get(safeScope) || null;
    }

    function setRetryUiContent(scope, payload) {
        const entry = getRetryUiEntry(scope);
        if (!entry || !entry.target) return false;

        if (entry.timerId) {
            clearTimeout(entry.timerId);
            entry.timerId = null;
        }

        const visible = payload?.visible !== false;
        if (!visible) {
            entry.target.innerHTML = '';
            entry.target.style.display = 'none';
            entry.target.dataset.retryUiVisible = 'false';
            entry.target.dataset.retryUiState = '';
            return true;
        }

        const state = String(payload?.state || 'retrying').trim().toLowerCase();
        entry.target.innerHTML = buildRetryStateHtml(payload);
        entry.target.style.display = '';
        entry.target.dataset.retryUiVisible = 'true';
        entry.target.dataset.retryUiState = state;

        if (state === 'success' && entry.autoHideSuccessMs > 0) {
            entry.timerId = setTimeout(function () {
                clearRetryState(scope);
            }, entry.autoHideSuccessMs);
        }

        if (state === 'failed' && entry.autoHideFailureMs > 0) {
            entry.timerId = setTimeout(function () {
                clearRetryState(scope);
            }, entry.autoHideFailureMs);
        }

        return true;
    }

    function showRetryingState(scope, options) {
        return setRetryUiContent(scope, {
            visible: true,
            state: 'retrying',
            title: options?.title || 'Đang thử lại...',
            message: options?.message || '',
            detail: options?.detail || ''
        });
    }

    function showRetrySuccessState(scope, options) {
        return setRetryUiContent(scope, {
            visible: true,
            state: 'success',
            title: options?.title || 'Đã phục hồi thành công',
            message: options?.message || '',
            detail: options?.detail || ''
        });
    }

    function showRetryFailureState(scope, options) {
        return setRetryUiContent(scope, {
            visible: true,
            state: 'failed',
            title: options?.title || 'Thử lại không thành công',
            message: options?.message || '',
            detail: options?.detail || ''
        });
    }

    function clearRetryState(scope) {
        return setRetryUiContent(scope, {
            visible: false
        });
    }
    function buildRetryAttemptText(autoRetried, maxAutoRetry) {
        const current = Number(autoRetried || 0);
        const max = Number(maxAutoRetry || 0);

        if (max > 0) {
            return `Đang thử lại lần ${current}/${max}...`;
        }

        return 'Đang thử lại...';
    }

    function buildRetryDelayText(delayMs) {
        const ms = Number(delayMs || 0);
        if (ms <= 0) return '';
        return `Tự động thử lại sau ${ms}ms.`;
    }

    /* =========================================================
       3. API ERROR NORMALIZE
    ========================================================= */
    function extractPayloadMessage(payload) {
        if (!payload) return '';

        if (typeof payload === 'string') {
            return payload.trim();
        }

        if (typeof payload?.message === 'string' && payload.message.trim()) {
            return payload.message.trim();
        }

        if (typeof payload?.error === 'string' && payload.error.trim()) {
            return payload.error.trim();
        }

        if (typeof payload?.title === 'string' && payload.title.trim()) {
            return payload.title.trim();
        }

        if (Array.isArray(payload?.errors) && payload.errors.length) {
            const first = payload.errors.find(x => typeof x === 'string' && x.trim());
            if (first) return first.trim();
        }

        if (payload?.errors && typeof payload.errors === 'object' && !Array.isArray(payload.errors)) {
            const fieldNames = Object.keys(payload.errors);
            for (const field of fieldNames) {
                const value = payload.errors[field];

                if (Array.isArray(value) && value.length) {
                    const first = value.find(x => typeof x === 'string' && x.trim());
                    if (first) return first.trim();
                }

                if (typeof value === 'string' && value.trim()) {
                    return value.trim();
                }
            }
        }

        return '';
    }

    function normalizeApiError(err, fallbackMessage) {
        if (isAbortError(err)) {
            return {
                isNormalizedPosError: true,
                type: 'abort',
                message: 'Yêu cầu đã bị hủy.',
                status: 0,
                isAbort: true,
                payload: null,
                fieldErrors: null,
                raw: err
            };
        }

        if (err && err.isNormalizedPosError) {
            return err;
        }

        const status = Number(err?.status || err?.statusCode || err?.response?.status || 0);
        const payload = err?.payload || err?.response?.payload || null;

        const payloadMessage = extractPayloadMessage(payload);
        const message =
            payloadMessage ||
            err?.message ||
            fallbackMessage ||
            'Đã có lỗi xảy ra.';

        let type = 'system';

        if (status === 0) {
            type = 'network';
        } else if (status === 400 || status === 422) {
            type = 'validation';
        } else if (status === 401 || status === 403) {
            type = 'auth';
        } else if (status === 404) {
            type = 'not_found';
        } else if (status === 409) {
            type = 'conflict';
        } else if (status >= 500) {
            type = 'system';
        }

        return {
            isNormalizedPosError: true,
            type,
            status,
            message,
            isAbort: false,
            payload,
            fieldErrors: payload?.errors || null,
            raw: err
        };
    }

    async function parseErrorResponse(response, fallbackMessage) {
        let text = '';

        try {
            text = await response.text();
        } catch {
        }

        const json = tryParseJson(text);
        const payloadMessage = extractPayloadMessage(json);

        const error = new Error(
            payloadMessage ||
            text ||
            fallbackMessage ||
            'Yêu cầu thất bại.'
        );

        error.status = response?.status || 0;
        error.response = response;
        error.payload = json;

        return normalizeApiError(error, fallbackMessage);
    }

    /* =========================================================
       4. REQUEST HELPER
    ========================================================= */
    async function requestJson(url, options) {
        const response = await fetch(url, {
            credentials: 'same-origin',
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                ...(options?.headers || {})
            },
            ...options
        });

        if (!response.ok) {
            throw await parseErrorResponse(
                response,
                `${options?.method || 'REQUEST'} request failed.`
            );
        }

        return await response.json();
    }

    async function getJson(url, fetchOptions) {
        return await window.PosError.fetchJson(url, {
            method: 'GET',
            credentials: 'same-origin',
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                ...((fetchOptions && fetchOptions.headers) || {})
            },
            ...(fetchOptions || {})
        });
    }

    async function postJson(url, data, fetchOptions) {
        return await window.PosError.postJson(
            url,
            data,
            getAntiForgeryToken
        );
    }

    async function postForm(url, formData, fetchOptions) {
        return await requestJson(url, {
            method: 'POST',
            headers: {
                'RequestVerificationToken': getAntiForgeryToken(),
                ...(fetchOptions?.headers || {})
            },
            body: formData,
            ...(fetchOptions || {})
        });
    }


    async function patchJson(url, data, fetchOptions) {
        return await window.PosError.patchJson(
            url,
            data,
            getAntiForgeryToken
        );
    }

    async function patchWithToken(url, fetchOptions) {
        return await requestJson(url, {
            method: 'PATCH',
            headers: {
                'RequestVerificationToken': getAntiForgeryToken(),
                ...(fetchOptions?.headers || {})
            },
            ...(fetchOptions || {})
        });
    }

    async function deleteJson(url, fetchOptions) {
        return await requestJson(url, {
            method: 'DELETE',
            headers: {
                'RequestVerificationToken': getAntiForgeryToken(),
                ...(fetchOptions?.headers || {})
            },
            ...(fetchOptions || {})
        });
    }

    async function deleteWithToken(url, fetchOptions) {
        return await window.PosError.deleteWithToken(
            url,
            getAntiForgeryToken
        );
    }

    /* =========================================================
       5. CACHE / DEBOUNCE / LOCK
    ========================================================= */
    function debounce(fn, wait) {
        let timer = null;

        const debounced = function (...args) {
            const ctx = this;
            clearTimeout(timer);

            timer = setTimeout(function () {
                fn.apply(ctx, args);
            }, wait);
        };

        debounced.cancel = function () {
            clearTimeout(timer);
            timer = null;
        };

        return debounced;
    }

    function createTtlCache(ttlMs, maxItems) {
        const ttl = Number(ttlMs || 60000);
        const limit = Number(maxItems || 100);
        const map = new Map();

        function cleanup() {
            const now = Date.now();

            for (const [key, entry] of map.entries()) {
                if (!entry || entry.expireAt <= now) {
                    map.delete(key);
                }
            }

            while (map.size > limit) {
                const firstKey = map.keys().next().value;
                map.delete(firstKey);
            }
        }

        return {
            get(key) {
                cleanup();
                const entry = map.get(key);
                if (!entry) return null;
                return entry.value;
            },
            set(key, value) {
                cleanup();
                map.set(key, {
                    value,
                    expireAt: Date.now() + ttl
                });
            },
            remove(key) {
                map.delete(key);
            },
            clear() {
                map.clear();
            }
        };
    }

    function createActionLocker() {
        const locks = new Map();

        return {
            isLocked(key) {
                return locks.get(key) === true;
            },
            lock(key) {
                if (locks.get(key) === true) return false;
                locks.set(key, true);
                return true;
            },
            unlock(key) {
                locks.set(key, false);
            }
        };
    }

    /* =========================================================
       6. BUTTON HELPER
    ========================================================= */
    function setButtonBusy(button, isBusy, busyText) {
        if (!button) return;

        if (isBusy) {
            if (!button.dataset.originalText) {
                button.dataset.originalText = button.innerHTML;
            }

            button.disabled = true;
            button.dataset.loading = 'true';

            if (busyText) {
                button.innerHTML = busyText;
            }
            return;
        }

        button.dataset.loading = 'false';

        const isUiLocked = button.dataset.uiLocked === 'true';
        if (!isUiLocked) {
            button.disabled = false;
        }

        if (button.dataset.originalText) {
            button.innerHTML = button.dataset.originalText;
            delete button.dataset.originalText;
        }
    }

    async function withButtonLoading(button, fn, busyText) {
        setButtonBusy(button, true, busyText);
        try {
            return await fn();
        } finally {
            setButtonBusy(button, false);
        }
    }

    /* =========================================================
       7. POS STATE BRIDGE
    ========================================================= */
    function getPosStateHelpers() {
        return window.PosState || {};
    }

    function setActionPending(posState, actionKey, isPending) {
        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.setActionPending === 'function') {
            stateHelpers.setActionPending(posState, actionKey, isPending);
            refreshUiLocks(posState);
            return;
        }

        if (!posState?.network?.pendingActions || !actionKey) return;

        if (isPending) {
            posState.network.pendingActions.add(actionKey);
        } else {
            posState.network.pendingActions.delete(actionKey);
        }

        refreshUiLocks(posState);
    }

    function setBusyScopes(posState, scopes, isBusy) {
        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.setBusyScopes === 'function') {
            stateHelpers.setBusyScopes(posState, scopes, isBusy);
            refreshUiLocks(posState);
            return;
        }

        if (!posState?.network?.busyScopes || !Array.isArray(scopes)) return;

        scopes.forEach(scope => {
            const value = String(scope || '').trim();
            if (!value) return;

            if (isBusy) {
                posState.network.busyScopes.add(value);
            } else {
                posState.network.busyScopes.delete(value);
            }
        });

        refreshUiLocks(posState);
    }

    function hasAnyBusyScope(posState, scopes) {
        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.hasAnyBusyScope === 'function') {
            return stateHelpers.hasAnyBusyScope(posState, scopes);
        }

        if (!posState?.network?.busyScopes || !Array.isArray(scopes)) return false;

        return scopes.some(scope => {
            const value = String(scope || '').trim();
            return value && posState.network.busyScopes.has(value);
        });
    }

    function getBusyScopes(posState) {
        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.getBusyScopes === 'function') {
            return stateHelpers.getBusyScopes(posState);
        }

        if (!posState?.network?.busyScopes) return [];
        return Array.from(posState.network.busyScopes.values());
    }

    function isPosOnline(posState) {
        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.isPosOnline === 'function') {
            return stateHelpers.isPosOnline(posState);
        }

        const networkState = posState?.network || {};
        const status = String(networkState.status || '').trim().toLowerCase();

        if (networkState.isOnline === false) {
            return false;
        }

        if (status === 'offline') {
            return false;
        }

        if (typeof navigator !== 'undefined' && navigator.onLine === false) {
            return false;
        }

        return true;
    }

    function getOfflineDisplayMode(options) {
        return String(
            options?.offlineDisplayMode ||
            options?.displayMode ||
            'toast'
        ).trim().toLowerCase();
    }

    function buildOfflineLockError(message) {
        const normalized = normalizeApiError({
            status: 0,
            message: message || 'POS đang offline, chưa thể thực hiện thao tác này.'
        }, message || 'POS đang offline, chưa thể thực hiện thao tác này.');

        normalized.isOfflineLock = true;
        normalized.reason = 'offline';
        return normalized;
    }

    function normalizeScopeArray(scopes) {
        if (!Array.isArray(scopes)) return [];
        return scopes
            .map(x => String(x || '').trim())
            .filter(Boolean);
    }

    function resolveConflictScopes(options) {
        const ownScopes = normalizeScopeArray(options?.scopes);
        const conflictScopes = normalizeScopeArray(options?.conflictScopes);

        if (conflictScopes.length) {
            return {
                ownScopes,
                conflictScopes
            };
        }

        const map = {
            cartMutate: ['checkout'],
            checkout: ['cartMutate']
        };

        const resolved = new Set(ownScopes);

        ownScopes.forEach(scope => {
            const conflicts = map[scope] || [];
            conflicts.forEach(x => resolved.add(x));
        });

        return {
            ownScopes,
            conflictScopes: Array.from(resolved)
        };
    }

    function setLastError(posState, error) {
        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.setLastError === 'function') {
            stateHelpers.setLastError(posState, error);
            return;
        }

        if (!posState?.network) return;
        posState.network.lastError = error || null;
    }

    function markNetworkSuccess(posState) {
        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.markNetworkSuccess === 'function') {
            stateHelpers.markNetworkSuccess(posState);
            return;
        }

        if (!posState?.network) return;
        posState.network.lastSuccessAt = new Date().toISOString();
        posState.network.lastError = null;
    }

    /* =========================================================
       7.1. DEBUG / DIAGNOSTIC BRIDGE
       - BƯỚC 5B.2
    ========================================================= */
    function nowIso() {
        return new Date().toISOString();
    }

    function setLastActionState(posState, payload) {
        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.setLastActionState === 'function') {
            stateHelpers.setLastActionState(posState, payload || {});
            return;
        }

        if (!posState?.runtime) return;

        posState.runtime.lastActionKey = String(payload?.key || '').trim();
        posState.runtime.lastActionStatus = String(payload?.status || '').trim();

        if (payload?.startedAt !== undefined) {
            posState.runtime.lastActionStartedAt = payload.startedAt || null;
        }

        if (payload?.completedAt !== undefined) {
            posState.runtime.lastActionCompletedAt = payload.completedAt || null;
        }

        posState.runtime.lastActionMessage = String(payload?.message || '').trim();
    }

    function setDebugEnabled(posState, enabled) {
        const stateHelpers = getPosStateHelpers();

        if (typeof stateHelpers.setDebugEnabled === 'function') {
            stateHelpers.setDebugEnabled(posState, enabled);
            return;
        }

        if (!posState?.runtime) return;
        posState.runtime.debugEnabled = !!enabled;
    }

    /* =========================================================
       8. ERROR DISPLAY PIPELINE
    ========================================================= */
    function getDisplayMode(options) {
        return String(options?.displayMode || 'toast').trim().toLowerCase();
    }

    function displayPosError(normalized, options) {
        const displayMode = getDisplayMode(options);

        if (!normalized || normalized.isAbort) {
            return normalized;
        }

        if (displayMode === 'silent') {
            return normalized;
        }

        if (displayMode === 'inline') {
            const inlineTarget = resolveInlineTarget(options?.inlineTarget);

            if (inlineTarget) {
                let variant = 'danger';

                if (normalized.type === 'validation' || normalized.type === 'conflict') {
                    variant = 'warning';
                } else if (normalized.type === 'network') {
                    variant = 'danger';
                }

                setInlineError(inlineTarget, normalized.message, variant);
                return normalized;
            }
        }

        // 🔥 QUAN TRỌNG NHẤT:
        // Nếu backend đã trả payload lỗi POS chuẩn thì ưu tiên đẩy qua PosError.handle
        if (normalized?.payload?.errorCode && window.PosError) {
            window.PosError.handle({
                message: normalized.message,
                errorCode: normalized.payload.errorCode,
                actionHint: normalized.payload.actionHint,
                errorType: normalized.payload.errorType,
                metadata: normalized.payload.metadata,
                statusCode: normalized.status,
                traceId: normalized.payload.traceId,
                detail: normalized.payload.detail
            }, {
                displayMode: options?.displayMode || undefined,
                showToast: options?.showToast !== false
            });

            return normalized;
        }

        // fallback cũ
        if (normalized.type === 'validation') {
            showWarning(options?.validationMessage || normalized.message);
            return normalized;
        }

        if (normalized.type === 'network') {
            showError(options?.networkMessage || 'Mất kết nối hoặc máy chủ không phản hồi.');
            return normalized;
        }

        if (normalized.type === 'conflict') {
            showWarning(options?.conflictMessage || normalized.message || 'Dữ liệu đã thay đổi, vui lòng tải lại.');
            return normalized;
        }

        if (normalized.type === 'auth') {
            showError(options?.authMessage || normalized.message || 'Bạn không có quyền thực hiện thao tác này.');
            return normalized;
        }

        if (normalized.type === 'not_found') {
            showWarning(options?.notFoundMessage || normalized.message || 'Không tìm thấy dữ liệu.');
            return normalized;
        }

        showError(normalized.message || options?.fallbackMessage || 'Đã có lỗi xảy ra.');
        return normalized;
    }
    function handlePosError(err, options) {
        const normalized = normalizeApiError(err, options?.fallbackMessage);

        if (normalized.isAbort) {
            return normalized;
        }

        if (typeof options?.onError === 'function') {
            options.onError(normalized);
        }

        displayPosError(normalized, options);
        return normalized;
    }

    /* =========================================================
       9. ACTION PIPELINE
       - BƯỚC 5B.2:
         + ghi debug runtime vào đầu / success / error / skip
    ========================================================= */
    async function runPosAction(posState, actionKey, handler, options) {
        const key = String(actionKey || '').trim();
        const button = options?.button || null;
        const busyText = options?.busyText || null;
        const blockedMessage = options?.blockedMessage || 'Tác vụ khác đang xử lý, vui lòng đợi xong rồi thử lại.';
        const requireOnline = options?.requireOnline === true;
        const offlineMessage = options?.offlineMessage || 'POS đang offline, chưa thể thực hiện thao tác này.';
        const {
            ownScopes,
            conflictScopes
        } = resolveConflictScopes(options);

        if (!key) {
            throw new Error('actionKey is required.');
        }

        if (options?.clearInlineOnStart !== false && options?.inlineTarget) {
            clearInlineError(options.inlineTarget);
        }

        // BƯỚC 5B.2:
        // bật debug nếu app chưa bật mà caller muốn ép bật
        if (options?.enableDebug === true) {
            setDebugEnabled(posState, true);
        }

        // 0. chặn sớm nếu action bắt buộc online
        if (requireOnline && !isPosOnline(posState)) {
            const offlineError = buildOfflineLockError(offlineMessage);

            setLastError(posState, offlineError);
            setLastActionState(posState, {
                key,
                status: 'skipped-offline',
                completedAt: nowIso(),
                message: offlineError.message || offlineMessage
            });

            if (typeof options?.onError === 'function') {
                options.onError(offlineError);
            }

            displayPosError(offlineError, {
                ...options,
                displayMode: getOfflineDisplayMode(options),
                networkMessage: offlineMessage,
                fallbackMessage: offlineMessage
            });

            return {
                ok: false,
                skipped: true,
                reason: 'offline',
                error: offlineError
            };
        }

        // 1. lock theo action key
        if (!globalActionLocker.lock(key)) {
            setLastActionState(posState, {
                key,
                status: 'skipped-locked',
                completedAt: nowIso(),
                message: 'Action đang bị lock.'
            });

            return {
                ok: false,
                skipped: true,
                reason: 'locked'
            };
        }

        // 2. lock theo scope
        if (hasAnyBusyScope(posState, conflictScopes)) {
            globalActionLocker.unlock(key);

            const busyScopeError = normalizeApiError({
                status: 409,
                message: blockedMessage
            }, blockedMessage);

            setLastActionState(posState, {
                key,
                status: 'skipped-busy-scope',
                completedAt: nowIso(),
                message: busyScopeError.message || blockedMessage
            });

            displayPosError(busyScopeError, {
                ...options,
                displayMode: options?.blockedDisplayMode || options?.displayMode || 'toast'
            });

            return {
                ok: false,
                skipped: true,
                reason: 'busy-scope',
                busyScopes: getBusyScopes(posState),
                error: busyScopeError
            };
        }

        const startedAt = nowIso();

        setLastActionState(posState, {
            key,
            status: 'running',
            startedAt,
            completedAt: null,
            message: ''
        });

        setActionPending(posState, key, true);
        setBusyScopes(posState, ownScopes, true);
        setLastError(posState, null);
        setButtonBusy(button, true, busyText);

        try {
            const result = await handler();

            markNetworkSuccess(posState);

            if (typeof options?.onSuccess === 'function') {
                await options.onSuccess(result);
            }

            setLastActionState(posState, {
                key,
                status: 'success',
                startedAt,
                completedAt: nowIso(),
                message: ''
            });

            return {
                ok: true,
                skipped: false,
                data: result
            };
        } catch (err) {
            const normalized = handlePosError(err, options);
            setLastError(posState, normalized);

            setLastActionState(posState, {
                key,
                status: normalized?.isAbort ? 'abort' : 'error',
                startedAt,
                completedAt: nowIso(),
                message: normalized?.message || options?.fallbackMessage || 'Đã có lỗi xảy ra.'
            });

            return {
                ok: false,
                skipped: false,
                error: normalized
            };
        } finally {
            setButtonBusy(button, false);
            setActionPending(posState, key, false);
            setBusyScopes(posState, ownScopes, false);
            globalActionLocker.unlock(key);

            if (typeof options?.onFinally === 'function') {
                try {
                    options.onFinally();
                } catch (_) {
                }
            }
        }
    }

    function isActionPendingLocal(posState, actionKey) {
        if (!posState?.network?.pendingActions || !actionKey) return false;
        return posState.network.pendingActions.has(actionKey);
    }

    /* =========================================================
       10. EXPORT
    ========================================================= */
    return {
        getAntiForgeryToken,
        formatMoney,
        escapeHtml,

        showSuccess,
        showWarning,
        showError,

        resolveInlineTarget,
        clearInlineError,
        setInlineError,

        requestJson,
        getJson,
        postJson,
        postForm,
        patchJson,
        patchWithToken,
        deleteJson,
        deleteWithToken,

        debounce,
        createTtlCache,
        createActionLocker,
        isAbortError,

        extractPayloadMessage,
        normalizeApiError,
        displayPosError,
        handlePosError,

        withButtonLoading,
        setButtonBusy,
        runPosAction,

        setBusyScopes,
        hasAnyBusyScope,
        getBusyScopes,
        isPosOnline,
        registerUiLock,
        refreshUiLocks,

        applyDraftActionSuccess,
        applyScreenActionSuccess,

        // BƯỚC 5B.2
        setLastActionState,
        setDebugEnabled,
        PosActionTypes,
        PosRetryBehavior,
        PosRetryClassification,
        getRetryDelay,
        sleepAsync,
        normalizeRetryCount,
        isBrowserOnline,
        createRetryResult,
        executeWithRetry,
        registerRetryUi,
        showRetryingState,
        showRetrySuccessState,
        showRetryFailureState,
        clearRetryState,
        buildRetryAttemptText,
        buildRetryDelayText,


        isActionPending: isActionPendingLocal
    };
})();
