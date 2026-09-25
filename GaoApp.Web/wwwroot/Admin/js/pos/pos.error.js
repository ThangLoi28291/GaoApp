window.PosError = (function () {
    'use strict';
    function getPosCommon() {
        return window.PosCommon || {};
    }

    function getActionTypes() {
        return getPosCommon().PosActionTypes || {
            LOAD_SCREEN: 'load-screen',
            ADD_PRODUCT: 'add-product',
            HOLD_CART: 'hold-cart',
            PAYMENT: 'payment',
            GENERIC_READ: 'generic-read',
            GENERIC_WRITE: 'generic-write'
        };
    }

    function getRetryBehaviorConstants() {
        return getPosCommon().PosRetryBehavior || {
            NONE: 'none',
            AUTO: 'auto',
            MANUAL_RETRY: 'manual_retry',
            RELOAD_ONLY: 'reload_only',
            AUTO_THEN_MANUAL: 'auto_then_manual'
        };
    }

    function getRetryClassificationConstants() {
        return getPosCommon().PosRetryClassification || {
            TRANSIENT: 'transient',
            BUSINESS: 'business',
            UNKNOWN: 'unknown',
            NON_RETRYABLE: 'non-retryable'
        };
    }

    function normalize(error) {
        if (!error || typeof error !== 'object') {
            return {
                success: false,
                message: 'Có lỗi xảy ra.',
                errorCode: null,
                actionHint: null,
                errorType: 'technical',
                metadata: null,
                statusCode: 500,
                traceId: null,
                detail: null
            };
        }

        return {
            success: false,
            message: error.message || 'Có lỗi xảy ra.',
            errorCode: error.errorCode || null,
            actionHint: error.actionHint || null,
            errorType: error.errorType || 'technical',
            metadata: error.metadata || null,
            statusCode: Number(error.statusCode || error.status || 500),
            traceId: error.traceId || null,
            detail: error.detail || null
        };
    }
    const BUSINESS_ERROR_CODES = {
        POS_SHIFT_OWNED_BY_ANOTHER_USER: true,
        POS_SHIFT_OPENED_BY_ANOTHER_USER: true,
        POS_SHIFT_OPEN_OWNED_BY_CURRENT_USER: true,
        POS_SHIFT_NOT_OPEN: true,
        POS_CART_NEW_BLOCKED_BY_ACTIVE_CART: true,
        POS_CART_EMPTY_CANNOT_HOLD: true,
        POS_CONTEXT_TERMINAL_NOT_RESOLVED: true,
        POS_AUTH_UNAUTHORIZED: true,
        POS_AUTH_FORBIDDEN: true,

        POS_OWNERSHIP_REQUIRED: true,
        POS_OWNERSHIP_CONFLICT: true,
        POS_VALIDATION_FAILED: true,
        POS_CART_CONFLICT: true,
        POS_CART_CHANGED: true,
        POS_CART_NOT_FOUND: true,
        POS_ORDER_NOT_FOUND: true,
        POS_ORDER_ALREADY_FINALIZED: true,
        POS_PAYMENT_INVALID: true,
        POS_PAYMENT_METHOD_INVALID: true,
        POS_PRODUCT_NOT_FOUND: true,
        POS_QUANTITY_INVALID: true,
        POS_TERMINAL_CONTEXT_INVALID: true,
        POS_SESSION_INVALID: true,
        POS_HOLD_NOT_ALLOWED: true,
        POS_RESUME_NOT_ALLOWED: true,
        POS_STATE_INVALID: true
    };

    const TRANSIENT_ERROR_CODES = {
        POS_NETWORK_ERROR: true,
        POS_TIMEOUT: true,
        POS_FETCH_FAILED: true,
        POS_SERVER_TEMPORARY: true,
        POS_GATEWAY_TIMEOUT: true,
        POS_BAD_GATEWAY: true,
        POS_SERVICE_UNAVAILABLE: true,
        POS_TOO_MANY_REQUESTS: true,
        POS_REQUEST_ABORTED: true
    };

    const ActionTypes = getActionTypes();
    const RetryBehavior = getRetryBehaviorConstants();
    const ACTION_RETRY_PROFILES = {};

    ACTION_RETRY_PROFILES[ActionTypes.LOAD_SCREEN] = {
        actionType: ActionTypes.LOAD_SCREEN,
        riskLevel: 'read-safe',
        behaviorWhenTransient: RetryBehavior.AUTO_THEN_MANUAL,
        behaviorWhenUnknown: RetryBehavior.AUTO_THEN_MANUAL,
        behaviorWhenBusiness: RetryBehavior.NONE,
        maxAutoRetry: 3,
        baseDelayMs: 500,
        maxDelayMs: 2500,
        multiplier: 2,
        allowManualRetry: true,
        allowReload: true,
        requiresResyncAfterSuccess: false
    };

    ACTION_RETRY_PROFILES[ActionTypes.ADD_PRODUCT] = {
        actionType: ActionTypes.ADD_PRODUCT,
        riskLevel: 'write-light',
        behaviorWhenTransient: RetryBehavior.AUTO_THEN_MANUAL,
        behaviorWhenUnknown: RetryBehavior.MANUAL_RETRY,
        behaviorWhenBusiness: RetryBehavior.NONE,
        maxAutoRetry: 1,
        baseDelayMs: 400,
        maxDelayMs: 1200,
        multiplier: 2,
        allowManualRetry: true,
        allowReload: true,
        requiresResyncAfterSuccess: true
    };

    ACTION_RETRY_PROFILES[ActionTypes.HOLD_CART] = {
        actionType: ActionTypes.HOLD_CART,
        riskLevel: 'write-sensitive',
        behaviorWhenTransient: RetryBehavior.MANUAL_RETRY,
        behaviorWhenUnknown: RetryBehavior.RELOAD_ONLY,
        behaviorWhenBusiness: RetryBehavior.NONE,
        maxAutoRetry: 0,
        baseDelayMs: 500,
        maxDelayMs: 1000,
        multiplier: 2,
        allowManualRetry: true,
        allowReload: true,
        requiresResyncAfterSuccess: true
    };

    ACTION_RETRY_PROFILES[ActionTypes.PAYMENT] = {
        actionType: ActionTypes.PAYMENT,
        riskLevel: 'write-sensitive',
        behaviorWhenTransient: RetryBehavior.MANUAL_RETRY,
        behaviorWhenUnknown: RetryBehavior.RELOAD_ONLY,
        behaviorWhenBusiness: RetryBehavior.NONE,
        maxAutoRetry: 0,
        baseDelayMs: 500,
        maxDelayMs: 1000,
        multiplier: 2,
        allowManualRetry: true,
        allowReload: true,
        requiresResyncAfterSuccess: true
    };

    ACTION_RETRY_PROFILES[ActionTypes.GENERIC_READ] = {
        actionType: ActionTypes.GENERIC_READ,
        riskLevel: 'read-safe',
        behaviorWhenTransient: RetryBehavior.AUTO_THEN_MANUAL,
        behaviorWhenUnknown: RetryBehavior.MANUAL_RETRY,
        behaviorWhenBusiness: RetryBehavior.NONE,
        maxAutoRetry: 2,
        baseDelayMs: 500,
        maxDelayMs: 2000,
        multiplier: 2,
        allowManualRetry: true,
        allowReload: true,
        requiresResyncAfterSuccess: false
    };

    ACTION_RETRY_PROFILES[ActionTypes.GENERIC_WRITE] = {
        actionType: ActionTypes.GENERIC_WRITE,
        riskLevel: 'write-light',
        behaviorWhenTransient: RetryBehavior.MANUAL_RETRY,
        behaviorWhenUnknown: RetryBehavior.MANUAL_RETRY,
        behaviorWhenBusiness: RetryBehavior.NONE,
        maxAutoRetry: 0,
        baseDelayMs: 500,
        maxDelayMs: 1000,
        multiplier: 2,
        allowManualRetry: true,
        allowReload: true,
        requiresResyncAfterSuccess: true
    };
    function buildText(error) {
        const err = normalize(error);
        return err.actionHint
            ? `${err.message}\n${err.actionHint}`
            : err.message;
    }

    function getErrorCode(error) {
        if (!error) return '';

        return String(
            error.errorCode ||
            error.code ||
            error.posErrorCode ||
            error.normalizedErrorCode ||
            ''
        ).trim().toUpperCase();
    }
    function classifyRetryError(error) {
        const RetryClassification = getRetryClassificationConstants();
        const PosCommon = getPosCommon();
        const errorCode = getErrorCode(error);
        const httpStatus = Number(
            error?.statusCode ||
            error?.status ||
            error?.httpStatus ||
            0
        );

        const message = String(
            error?.message ||
            error?.detail ||
            ''
        ).toLowerCase();

        if (errorCode && BUSINESS_ERROR_CODES[errorCode]) {
            return RetryClassification.BUSINESS;
        }

        if (errorCode && TRANSIENT_ERROR_CODES[errorCode]) {
            return RetryClassification.TRANSIENT;
        }

        if (
            httpStatus === 408 ||
            httpStatus === 429 ||
            httpStatus === 502 ||
            httpStatus === 503 ||
            httpStatus === 504
        ) {
            return RetryClassification.TRANSIENT;
        }

        if (
            httpStatus === 400 ||
            httpStatus === 401 ||
            httpStatus === 403 ||
            httpStatus === 404 ||
            httpStatus === 409 ||
            httpStatus === 422
        ) {
            return RetryClassification.BUSINESS;
        }

        if (
            message.includes('network') ||
            message.includes('timeout') ||
            message.includes('failed to fetch') ||
            message.includes('fetch failed') ||
            message.includes('gateway timeout') ||
            message.includes('temporarily unavailable') ||
            message.includes('request aborted')
        ) {
            return RetryClassification.TRANSIENT;
        }

        if (typeof PosCommon.isBrowserOnline === 'function' && PosCommon.isBrowserOnline() === false) {
            return RetryClassification.TRANSIENT;
        }

        if (httpStatus >= 500) {
            return RetryClassification.UNKNOWN;
        }

        return RetryClassification.NON_RETRYABLE;
    }
    function getActionRetryProfile(actionType) {
        const ActionTypes = getActionTypes();
        if (!actionType) {
            return ACTION_RETRY_PROFILES[ActionTypes.GENERIC_READ];
        }

        return ACTION_RETRY_PROFILES[actionType]
            || ACTION_RETRY_PROFILES[ActionTypes.GENERIC_READ];
    }
    function resolveRetryPolicy(actionType, error) {
        const RetryBehavior = getRetryBehaviorConstants();
        const RetryClassification = getRetryClassificationConstants();
        const PosCommon = getPosCommon();

        const profile = getActionRetryProfile(actionType);
        const classification = classifyRetryError(error);

        let behavior = RetryBehavior.NONE;

        if (classification === RetryClassification.BUSINESS) {
            behavior = profile.behaviorWhenBusiness || RetryBehavior.NONE;
        } else if (classification === RetryClassification.TRANSIENT) {
            behavior = profile.behaviorWhenTransient || RetryBehavior.NONE;
        } else if (classification === RetryClassification.UNKNOWN) {
            behavior = profile.behaviorWhenUnknown || RetryBehavior.NONE;
        } else {
            behavior = RetryBehavior.NONE;
        }

        let maxAutoRetry = 0;

        if (behavior === RetryBehavior.AUTO || behavior === RetryBehavior.AUTO_THEN_MANUAL) {
            maxAutoRetry = typeof PosCommon.normalizeRetryCount === 'function'
                ? PosCommon.normalizeRetryCount(profile.maxAutoRetry, 0)
                : Number(profile.maxAutoRetry || 0);
        }

        return {
            actionType: profile.actionType,
            riskLevel: profile.riskLevel,
            classification,
            behavior,

            maxAutoRetry,
            baseDelayMs: Number(profile.baseDelayMs || 500),
            maxDelayMs: Number(profile.maxDelayMs || 2500),
            multiplier: Number(profile.multiplier || 2),

            allowAutoRetry:
                behavior === RetryBehavior.AUTO ||
                behavior === RetryBehavior.AUTO_THEN_MANUAL,

            allowManualRetry:
                profile.allowManualRetry === true &&
                (
                    behavior === RetryBehavior.MANUAL_RETRY ||
                    behavior === RetryBehavior.AUTO_THEN_MANUAL
                ),

            allowReload:
                profile.allowReload === true &&
                (
                    behavior === RetryBehavior.RELOAD_ONLY ||
                    behavior === RetryBehavior.MANUAL_RETRY ||
                    behavior === RetryBehavior.AUTO_THEN_MANUAL
                ),

            requiresResyncAfterSuccess: profile.requiresResyncAfterSuccess === true,

            errorCode: getErrorCode(error),
            statusCode: Number(error?.statusCode || error?.status || 0)
        };
    }

    function getUiBehavior(error) {
        const err = normalize(error);

        switch (err.errorCode) {
            case 'POS_SHIFT_OWNED_BY_ANOTHER_USER':
            case 'POS_SHIFT_OPENED_BY_ANOTHER_USER':
            case 'POS_SHIFT_OPEN_OWNED_BY_CURRENT_USER':
            case 'POS_CART_NEW_BLOCKED_BY_ACTIVE_CART':
                return 'modal';

            case 'POS_SHIFT_NOT_OPEN':
            case 'POS_CONTEXT_TERMINAL_NOT_RESOLVED':
            case 'POS_AUTH_UNAUTHORIZED':
                return 'banner';

            default:
                return 'toast';
        }
    }

    function renderBanner(error) {
        const err = normalize(error);

        const banner = document.getElementById('posErrorBanner');
        const msg = document.getElementById('posErrorBannerMessage');
        const hint = document.getElementById('posErrorBannerHint');

        if (!banner || !msg || !hint) return;

        msg.textContent = err.message || '';
        hint.textContent = err.actionHint || '';
        banner.classList.remove('d-none');
    }

    function clearBanner() {
        const banner = document.getElementById('posErrorBanner');
        const msg = document.getElementById('posErrorBannerMessage');
        const hint = document.getElementById('posErrorBannerHint');

        if (!banner || !msg || !hint) return;

        msg.textContent = '';
        hint.textContent = '';
        banner.classList.add('d-none');
    }

    function row(label, value) {
        return `
            <div class="pos-error-meta-item">
                <div class="pos-error-meta-label">${label}</div>
                <div class="pos-error-meta-value">${value ?? '-'}</div>
            </div>`;
    }

    function getAntiForgeryToken() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function formatMoney(value) {
        const n = Number(value || 0);
        return n.toLocaleString('vi-VN');
    }

    function formatDateTime(value) {
        if (!value) return '-';

        try {
            return new Date(value).toLocaleString('vi-VN');
        } catch (_) {
            return '-';
        }
    }

    function buildMetaHtml(error) {
        const err = normalize(error);
        const meta = err.metadata || {};

        switch (err.errorCode) {
            case 'POS_SHIFT_OWNED_BY_ANOTHER_USER':
            case 'POS_SHIFT_OPENED_BY_ANOTHER_USER':
                return `
                <div class="pos-error-meta-list">
                    ${row('Mã ca', meta.shiftCode || '-')}
                    ${row('Người mở ca', meta.openedByUserName || meta.openedByUserId || '-')}
                    ${row('Terminal', meta.terminalName || meta.terminalCode || meta.terminalId || '-')}
                    ${row('Kho bán', meta.warehouseName || meta.warehouseCode || meta.warehouseId || '-')}
                    ${row('Mở lúc', formatDateTime(meta.openedAtUtc))}
                    ${row('Tiền đầu ca', formatMoney(meta.openingCash))}
                    ${row('Tiền mặt dự kiến', formatMoney(meta.closingCashExpected))}
                </div>
            `;

            case 'POS_CART_NEW_BLOCKED_BY_ACTIVE_CART':
                return `
                <div class="pos-error-meta-list">
                    ${row('Giỏ hiện tại', meta.orderNumber || meta.id || '-')}
                    ${row('Có dòng hàng', meta.hasLines ? 'Có' : 'Không')}
                    ${row('Có thanh toán', meta.hasPayments ? 'Có' : 'Không')}
                </div>
            `;

            case 'POS_CART_EMPTY_CANNOT_HOLD':
                return `
                <div class="pos-error-meta-list">
                    ${row('Mã giỏ', meta.orderNumber || meta.id || '-')}
                </div>
            `;

            default:
                return '';
        }
    }
    function getActionContext() {
        return window.PosErrorActionContext || {};
    }
    async function loadOwnershipInfo() {
        return await fetchJson('/admin/pos/shift/ownership-info', {
            method: 'GET',
            headers: {
                'Accept': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            }
        });
    }

    async function takeOverShift(shiftId) {
        const reason = prompt('Nhập lý do tiếp quản ca:');

        if (!reason || !reason.trim()) {
            if (window.toastr) toastr.warning('Vui lòng nhập lý do tiếp quản ca.');
            return;
        }

        const result = await postJson('/admin/pos/shift/takeover', {
            shiftId: shiftId,
            reason: reason.trim()
        }, getAntiForgeryToken);

        if (window.toastr) {
            toastr.success(result?.message || 'Đã tiếp quản ca POS thành công.');
        }

        setTimeout(function () {
            window.location.reload();
        }, 500);
    }

    async function forceCloseShift(shiftId) {
        const actualRaw = prompt('Nhập tiền thực đếm cuối ca:');

        if (actualRaw == null) return;

        const actual = Number(actualRaw.toString().replace(/\./g, '').replace(/,/g, ''));

        if (isNaN(actual) || actual < 0) {
            if (window.toastr) toastr.warning('Tiền thực đếm không hợp lệ.');
            return;
        }

        const reason = prompt('Nhập lý do đóng hộ ca:');

        if (!reason || !reason.trim()) {
            if (window.toastr) toastr.warning('Vui lòng nhập lý do đóng hộ ca.');
            return;
        }

        const result = await postJson('/admin/pos/shift/force-close', {
            shiftId: shiftId,
            closingCashActual: actual,
            reason: reason.trim(),
            note: null
        }, getAntiForgeryToken);

        if (window.toastr) {
            toastr.success(result?.message || 'Đã đóng hộ ca POS thành công.');
        }

        setTimeout(function () {
            window.location.reload();
        }, 500);
    }
    function getActions(error) {
        const err = normalize(error);
        const meta = err.metadata || {};
        const ctx = getActionContext();

        switch (err.errorCode) {
            case 'POS_CART_NEW_BLOCKED_BY_ACTIVE_CART':
                return [
                    {
                        key: 'continue-current-cart',
                        text: 'Tiếp tục giỏ hiện tại',
                        cssClass: 'btn btn-primary pos-error-action-btn',
                        onClick: async function () {
                            closeModal();
                            if (typeof ctx.focusBarcodeInput === 'function') {
                                ctx.focusBarcodeInput();
                            }
                        }
                    },
                    {
                        key: 'hold-current-cart',
                        text: 'Giữ giỏ hiện tại',
                        cssClass: 'btn btn-warning pos-error-action-btn',
                        onClick: async function () {
                            closeModal();
                            if (typeof ctx.openHoldModal === 'function') {
                                ctx.openHoldModal();
                            }
                        }
                    },
                    {
                        key: 'cancel-current-cart',
                        text: 'Hủy giỏ hiện tại',
                        cssClass: 'btn btn-danger pos-error-action-btn',
                        onClick: async function () {
                            closeModal();
                            if (typeof ctx.cancelCurrentCart === 'function') {
                                await ctx.cancelCurrentCart();
                            }
                        }
                    }
                ];

            case 'POS_SHIFT_OWNED_BY_ANOTHER_USER':
                return [
                   
                    {
                        key: 'takeover-shift',
                        text: 'Tiếp quản ca',
                        cssClass: 'btn btn-primary pos-error-action-btn',
                        onClick: async function () {
                            const info = await loadOwnershipInfo();
                            const shiftId = info?.shiftId || meta.shiftId;

                            if (!shiftId) {
                                if (window.toastr) toastr.error('Không xác định được ca cần tiếp quản.');
                                return;
                            }

                            await takeOverShift(shiftId);
                        }
                    },
                    {
                        key: 'force-close-shift',
                        text: 'Đóng hộ ca',
                        cssClass: 'btn btn-danger pos-error-action-btn',
                        onClick: async function () {
                            const info = await loadOwnershipInfo();
                            const shiftId = info?.shiftId || meta.shiftId;

                            if (!shiftId) {
                                if (window.toastr) toastr.error('Không xác định được ca cần đóng hộ.');
                                return;
                            }

                            await forceCloseShift(shiftId);
                        }
                    },
                    {
                        key: 'go-shift-page',
                        text: 'Đi tới ca POS',
                        cssClass: 'btn btn-warning pos-error-action-btn',
                        onClick: async function () {
                            window.location.href = '/admin/pos-shift';
                        }
                    },
                    {
                        key: 'refresh-screen',
                        text: 'Tải lại',
                        cssClass: 'btn btn-outline-secondary pos-error-action-btn',
                        onClick: async function () {
                            window.location.reload();
                        }
                    }
                ];

            case 'POS_SHIFT_NOT_OPEN':
                return [
                    {
                        key: 'go-shift-page',
                        text: 'Mở trang ca POS',
                        cssClass: 'btn btn-primary pos-error-action-btn',
                        onClick: async function () {
                            window.location.href = '/admin/pos-shift';
                        }
                    },
                    {
                        key: 'refresh-screen',
                        text: 'Tải lại',
                        cssClass: 'btn btn-outline-secondary pos-error-action-btn',
                        onClick: async function () {
                            if (typeof ctx.requestScreenRefresh === 'function') {
                                await ctx.requestScreenRefresh({
                                    reason: 'shift-not-open-refresh',
                                    force: true,
                                    silent: false,
                                    focusBarcode: false,
                                    scope: 'full'
                                });
                            }
                        }
                    }
                ];

            default:
                return [];
        }
    }
    function renderActions(error) {
        const err = normalize(error);
        const container = document.getElementById('posErrorModalActions');
        if (!container) return;

        const actions = getActions(err);
        if (!actions.length) {
            container.innerHTML = '';
            return;
        }

        container.innerHTML = actions.map(action => `
        <button type="button"
                class="${action.cssClass || 'btn btn-light pos-error-action-btn'}"
                data-pos-error-action="${action.key}">
            ${action.text}
        </button>
    `).join('');

        actions.forEach(action => {
            const btn = container.querySelector(`[data-pos-error-action="${action.key}"]`);
            if (!btn) return;

            btn.addEventListener('click', async function () {
                try {
                    btn.disabled = true;
                    await action.onClick();
                } catch (err) {
                    if (window.toastr) {
                        toastr.error(buildText(err).replace(/\n/g, '<br/>'));
                    }
                } finally {
                    btn.disabled = false;
                }
            });
        });
    }
    function closeModal() {
        const modalEl = document.getElementById('posErrorModal');
        if (!modalEl || !window.bootstrap) return;

        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.hide();
    }

    async function enrichOwnershipError(error) {
        const err = normalize(error);

        if (
            err.errorCode !== 'POS_SHIFT_OWNED_BY_ANOTHER_USER' &&
            err.errorCode !== 'POS_SHIFT_OPENED_BY_ANOTHER_USER'
        ) {
            return err;
        }

        try {
            const info = await loadOwnershipInfo();

            if (!info) return err;

            return {
                ...err,
                metadata: {
                    ...(err.metadata || {}),
                    shiftId: info.shiftId,
                    shiftCode: info.shiftCode,
                    openedByUserId: info.openedByUserId,
                    openedByUserName: info.openedByUserName,
                    terminalId: info.terminalId,
                    terminalCode: info.terminalCode,
                    terminalName: info.terminalName,
                    warehouseId: info.warehouseId,
                    warehouseCode: info.warehouseCode,
                    warehouseName: info.warehouseName,
                    openedAtUtc: info.openedAtUtc,
                    openingCash: info.openingCash,
                    closingCashExpected: info.closingCashExpected,
                    canTakeOver: info.canTakeOver,
                    canForceClose: info.canForceClose
                }
            };
        } catch (_) {
            return err;
        }
    }
    async function showModal(error) {
        const err = await enrichOwnershipError(error);

        const msgEl = document.getElementById('posErrorModalMessage');
        const hintEl = document.getElementById('posErrorModalHint');
        const metaEl = document.getElementById('posErrorModalMeta');

        if (msgEl) msgEl.textContent = err.message || '';
        if (hintEl) hintEl.textContent = err.actionHint || '';
        if (metaEl) metaEl.innerHTML = buildMetaHtml(err);

        renderActions(err);

        const modalEl = document.getElementById('posErrorModal');
        if (!modalEl || !window.bootstrap) {
            if (window.toastr) {
                toastr.error(buildText(err).replace(/\n/g, '<br/>'));
            }
            return;
        }

        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.show();
    }

    async function fetchJson(url, options) {
        const response = await fetch(url, options);

        const contentType = response.headers.get('content-type') || '';
        const isJson = contentType.includes('application/json');

        let payload = null;
        let rawText = null;

        try {
            if (isJson) {
                payload = await response.json();
            } else {
                rawText = await response.text();
            }
        } catch {
            rawText = rawText || null;
        }

        if (response.ok) {
            return payload;
        }

        if (response.status === 401) {
            throw normalize({
                message: payload?.message || 'Phiên đăng nhập đã hết hạn.',
                errorCode: payload?.errorCode || 'POS_AUTH_UNAUTHORIZED',
                actionHint: payload?.actionHint || 'Vui lòng đăng nhập lại để tiếp tục.',
                errorType: payload?.errorType || 'authentication',
                metadata: payload?.metadata || null,
                statusCode: response.status
            });
        }

        if (response.status === 403) {
            throw normalize({
                message: payload?.message || 'Bạn không có quyền thực hiện thao tác này.',
                errorCode: payload?.errorCode || 'POS_AUTH_FORBIDDEN',
                actionHint: payload?.actionHint || 'Vui lòng liên hệ quản lý nếu bạn cần quyền này.',
                errorType: payload?.errorType || 'permission',
                metadata: payload?.metadata || null,
                statusCode: response.status
            });
        }

        if (payload && typeof payload === 'object') {
            throw normalize({
                message: payload.message || 'Có lỗi xảy ra.',
                errorCode: payload.errorCode || null,
                actionHint: payload.actionHint || null,
                errorType: payload.errorType || null,
                metadata: payload.metadata || null,
                statusCode: payload.statusCode || response.status,
                traceId: payload.traceId || null,
                detail: payload.detail || null
            });
        }

        throw normalize({
            message: rawText || 'Có lỗi xảy ra.',
            errorCode: null,
            actionHint: null,
            errorType: 'technical',
            metadata: null,
            statusCode: response.status
        });
    }

    async function postJson(url, data, getToken) {
        return await fetchJson(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json',
                'RequestVerificationToken': typeof getToken === 'function' ? getToken() : '',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify(data || {})
        });
    }

    async function patchJson(url, data, getToken) {
        return await fetchJson(url, {
            method: 'PATCH',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json',
                'RequestVerificationToken': typeof getToken === 'function' ? getToken() : '',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: data == null ? null : JSON.stringify(data)
        });
    }

    async function deleteWithToken(url, getToken) {
        return await fetchJson(url, {
            method: 'DELETE',
            headers: {
                'Accept': 'application/json',
                'RequestVerificationToken': typeof getToken === 'function' ? getToken() : '',
                'X-Requested-With': 'XMLHttpRequest'
            }
        });
    }

    let navigatingToShift = false;
    function redirectToShiftIfNeeded(errorCode) {
        if (errorCode !== 'POS_SHIFT_NOT_OPEN' ||
            !/^\/admin\/pos(?:\/(?:v3|legacy))?\/?$/.test(window.location.pathname)) return false;
        const offline = window.PosOffline?.status();
        // Keep the original journal and its recovery controls available until
        // pending operations have been reconciled against their own shift.
        if (offline?.pending > 0 || offline?.connected === false) return false;
        if (navigatingToShift) return true;
        navigatingToShift = true;
        const query = new URLSearchParams({ start: '1', returnUrl: window.location.pathname + window.location.search });
        window.location.replace('/admin/pos-shift?' + query.toString());
        return true;
    }

    function handle(error, options) {
        const err = normalize(error);
        const displayMode = options?.displayMode || getUiBehavior(err);

        if (redirectToShiftIfNeeded(err.errorCode)) {
            return err;
        }

        if (displayMode === 'modal') {
            showModal(err).catch(function () {
                if (window.toastr) {
                    toastr.error(buildText(err).replace(/\n/g, '<br/>'));
                }
            });
            return err;
        }

        if (displayMode === 'banner') {
            renderBanner(err);

            if (options?.showToast !== false && window.toastr) {
                toastr.error(buildText(err).replace(/\n/g, '<br/>'));
            }

            return err;
        }

        if (displayMode === 'inline') {
            return err;
        }

        if (window.toastr) {
            toastr.error(buildText(err).replace(/\n/g, '<br/>'));
        }

        return err;
    }

    return {
        normalize,
        buildText,
        getErrorCode,
        getUiBehavior,
        renderBanner,
        clearBanner,
        showModal,
        fetchJson,
        postJson,
        patchJson,
        deleteWithToken,
        getActions,
        closeModal,
        handle,
        redirectToShiftIfNeeded,

        classifyRetryError,
        getActionRetryProfile,
        resolveRetryPolicy,

        retryConstants: {
            businessErrorCodes: BUSINESS_ERROR_CODES,
            transientErrorCodes: TRANSIENT_ERROR_CODES,
            actionRetryProfiles: ACTION_RETRY_PROFILES
        }
    };
})();
