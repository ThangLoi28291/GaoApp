(() => {
    'use strict';

    const PAGE_SCRIPT_ID = 'posOrderDetailPageScript';
    const RETURN_CREATE_ENDPOINT = '/admin/pos/returns';

    const pageScriptElement = document.getElementById(PAGE_SCRIPT_ID);
    if (!pageScriptElement) {
        throw new Error('[POS Order Detail] Missing page script configuration.');
    }

    function toFiniteNumber(value, fallback = 0) {
        const n = Number(value);
        return Number.isFinite(n) ? n : fallback;
    }

    function toPositiveInt(value, fallback = 0) {
        const n = Number.parseInt(value, 10);
        return Number.isInteger(n) && n > 0 ? n : fallback;
    }

    function toNonNegativeNumber(value, fallback = 0) {
        const n = Number(value);
        return Number.isFinite(n) && n >= 0 ? n : fallback;
    }

    const posOrderDetailServerData = Object.freeze({
        grandTotal: toFiniteNumber(pageScriptElement.dataset.grandTotal, 0),
        subtotal: toFiniteNumber(pageScriptElement.dataset.subtotal, 0),
        paidTotal: toFiniteNumber(pageScriptElement.dataset.paidTotal, 0),
        refundableRemaining: toFiniteNumber(
            pageScriptElement.dataset.refundableRemaining,
            0
        )
    });

    const state = {
        initialized: false,
        orderActionSubmitting: false,
        returnSubmitting: false,
        returnOrderId: 0,
        returnEligibility: null,
        eligibilitySequence: 0,
        orderActionEligibilitySequence: 0,
        historySequence: 0,
        eligibilityController: null,
        historyController: null
    };

    const dom = {};
    const modal = {};
    const originalButtonHtml = new WeakMap();

    function requiredElement(id) {
        const element = document.getElementById(id);

        if (!element) {
            throw new Error(
                `[POS Order Detail] Missing required element #${id}.`
            );
        }

        return element;
    }

    function optionalElement(id) {
        return document.getElementById(id);
    }

    function safeArray(value) {
        return Array.isArray(value)
            ? value
            : [];
    }

    function escapeHtml(value) {
        const map = {
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        };

        return String(value ?? '')
            .replace(
                /[&<>"']/g,
                char => map[char]
            );
    }

    function isAbortError(error) {
        return (
            error?.name === 'AbortError' ||
            error?.code === 20
        );
    }

    function extractMessage(value) {
        if (!value) {
            return '';
        }

        if (typeof value === 'string') {
            return value.trim();
        }

        if (typeof value !== 'object') {
            return String(value);
        }

        const candidates = [
            value.message,
            value.title,
            value.detail,
            value.error
        ];

        for (const candidate of candidates) {
            if (
                typeof candidate === 'string' &&
                candidate.trim()
            ) {
                return candidate.trim();
            }

            if (
                candidate &&
                typeof candidate === 'object'
            ) {
                const nested =
                    extractMessage(candidate);

                if (nested) {
                    return nested;
                }
            }
        }

        return '';
    }

    function getAntiForgeryToken() {
        return (
            document.querySelector(
                'input[name="__RequestVerificationToken"]'
            )?.value ||
            ''
        );
    }

    function buildJsonHeaders(includeAntiForgery = false) {
        const headers = {
            Accept: 'application/json',
            'Content-Type': 'application/json'
        };

        if (includeAntiForgery) {
            const token =
                getAntiForgeryToken();

            if (token) {
                headers.RequestVerificationToken =
                    token;
            }
        }

        return headers;
    }

    function notifySuccess(message) {
        const text =
            String(
                message ||
                'Thành công'
            );

        if (window.toastr) {
            window.toastr.success(text);
        }
        else {
            window.alert(text);
        }
    }

    function notifyError(message) {
        const text =
            String(
                message ||
                'Có lỗi xảy ra'
            );

        if (window.toastr) {
            window.toastr.error(text);
        }
        else {
            window.alert(text);
        }
    }

    async function fetchJson(url, options = {}) {
        const response =
            await fetch(
                url,
                options
            );

        const contentType =
            response.headers
                .get('content-type') ||
            '';

        const rawText =
            await response.text();

        let payload = null;

        if (rawText) {
            if (
                contentType.includes(
                    'application/json'
                )
            ) {
                try {
                    payload =
                        JSON.parse(rawText);
                }
                catch {
                    payload =
                        rawText;
                }
            }
            else {
                payload =
                    rawText;
            }
        }

        if (!response.ok) {
            const primaryMessage =
                extractMessage(payload) ||
                `Yêu cầu thất bại (${response.status})`;

            const extra = [];
            if (
                payload &&
                typeof payload === 'object'
            ) {
                if (
                    typeof payload.actionHint === 'string' &&
                    payload.actionHint.trim() &&
                    payload.actionHint.trim() !== primaryMessage
                ) {
                    extra.push(payload.actionHint.trim());
                }

                if (
                    typeof payload.errorCode === 'string' &&
                    payload.errorCode.trim()
                ) {
                    extra.push(`Mã lỗi: ${payload.errorCode.trim()}`);
                }

                if (
                    typeof payload.traceId === 'string' &&
                    payload.traceId.trim()
                ) {
                    extra.push(`Mã truy vết: ${payload.traceId.trim()}`);
                }
            }

            throw new Error(
                [primaryMessage, ...extra].join(' ')
            );
        }

        return payload;
    }

    function formatMoney(value) {
        return (
            `${toFiniteNumber(value, 0)
                .toLocaleString('vi-VN')} đ`
        );
    }

    function formatNumber(value) {
        return toFiniteNumber(
            value,
            0
        ).toLocaleString('vi-VN');
    }

    function formatInputNumber(value) {
        return String(
            toFiniteNumber(
                value,
                0
            )
        );
    }

    function formatDateTime(value) {
        if (!value) {
            return '-';
        }

        const date =
            new Date(value);

        if (
            Number.isNaN(
                date.getTime()
            )
        ) {
            return '-';
        }

        return date.toLocaleString(
            'vi-VN'
        );
    }

    function setButtonBusy(
        button,
        busy,
        busyText = 'Đang xử lý...'
    ) {
        if (!button) {
            return;
        }

        if (
            !originalButtonHtml.has(
                button
            )
        ) {
            originalButtonHtml.set(
                button,
                button.innerHTML
            );
        }

        button.disabled =
            Boolean(busy);

        button.setAttribute(
            'aria-busy',
            busy
                ? 'true'
                : 'false'
        );

        if (busy) {
            button.innerHTML = `
                <span
                    class="spinner-border spinner-border-sm me-1"
                    aria-hidden="true"></span>
                ${escapeHtml(busyText)}
            `;
        }
        else {
            button.innerHTML =
                originalButtonHtml.get(
                    button
                ) || '';
        }
    }

    function setElementBusy(
        element,
        busy
    ) {
        if (!element) {
            return;
        }

        element.setAttribute(
            'aria-busy',
            busy
                ? 'true'
                : 'false'
        );
    }

    async function refreshOrderDetail(orderId) {
        const safeOrderId =
            toPositiveInt(
                orderId,
                0
            );

        if (!safeOrderId) {
            return;
        }

        if (
            typeof window.loadOrderDetail ===
            'function'
        ) {
            await window.loadOrderDetail(
                safeOrderId
            );

            return;
        }

        window.location.reload();
    }

    function renderModalLoading(
        target,
        message
    ) {
        target.innerHTML = `
            <div
                class="text-center py-5 text-muted"
                role="status"
                aria-live="polite">

                <span
                    class="spinner-border spinner-border-sm me-2"
                    aria-hidden="true"></span>

                ${escapeHtml(message)}

            </div>
        `;
    }

    function renderModalError(
        target,
        message
    ) {
        target.innerHTML = `
            <div
                class="ord-empty"
                role="alert">

                <div class="ord-empty-title">
                    Không tải được dữ liệu
                </div>

                <div class="ord-empty-desc">
                    ${escapeHtml(
            message ||
            'Vui lòng thử lại.'
        )
            }
                </div>

            </div>
        `;
    }

    function abortEligibilityRequest() {
        if (
            !state.eligibilityController
        ) {
            return;
        }

        state
            .eligibilityController
            .abort();

        state.eligibilityController =
            null;
    }

    function abortHistoryRequest() {
        if (
            !state.historyController
        ) {
            return;
        }

        state
            .historyController
            .abort();

        state.historyController =
            null;
    }

    function openPaymentHistoryModal() {
        modal.paymentHistory.show();
    }

    async function openOrderActionModal(
        type,
        orderId
    ) {
        if (
            state.orderActionSubmitting
        ) {
            return;
        }

        const normalizedType =
            String(type || '')
                .trim()
                .toLowerCase();

        const safeOrderId =
            toPositiveInt(
                orderId,
                0
            );

        if (
            ![
                'refund',
                'void'
            ].includes(
                normalizedType
            )
        ) {
            notifyError(
                'Loại xử lý đơn không hợp lệ.'
            );

            return;
        }

        if (!safeOrderId) {
            notifyError(
                'Không xác định được đơn hàng.'
            );

            return;
        }

        dom.orderActionType.value =
            normalizedType;

        dom.orderActionOrderId.value =
            String(safeOrderId);

        dom.orderActionReason.value =
            '';

        if (
            dom.orderActionRefundMethod
        ) {
            dom.orderActionRefundMethod.value =
                '0';
        }

        if (
            dom.orderActionRefundReferenceCode
        ) {
            dom.orderActionRefundReferenceCode.value =
                '';
        }

        if (
            dom.orderActionRefundProvider
        ) {
            dom.orderActionRefundProvider.value =
                '';
        }

        if (
            dom.orderActionRefundMethodBox
        ) {
            dom
                .orderActionRefundMethodBox
                .style.display =
                normalizedType ===
                    'refund'
                    ? ''
                    : 'none';
        }

        if (
            dom.orderActionRefundNonCashBox
        ) {
            dom
                .orderActionRefundNonCashBox
                .style.display =
                'none';
        }

        if (
            normalizedType ===
            'void'
        ) {
            dom.orderActionSubtitle.textContent =
                'Hủy sau khi chốt cho đơn vừa hoàn thành';

            dom.orderActionCallout.textContent =
                'Void dùng khi hóa đơn vừa chốt nhưng cần hủy ngay. ' +
                'Hãy nhập lý do rõ ràng để audit và truy vết.';
        }
        else {
            dom.orderActionSubtitle.textContent =
                'Hoàn tiền và trả toàn bộ hàng';

            dom.orderActionCallout.textContent =
                'Hoàn lại toàn bộ số tiền đã thu. ' +
                'Chọn phương thức thực tế đã hoàn cho khách.';
        }

        const sequence = ++state.orderActionEligibilitySequence;
        const warning = optionalElement('orderActionCostWarning');
        if (warning) { warning.hidden = true; warning.textContent = ''; }
        const pendingBox = optionalElement('orderActionPendingRestockBox');
        const pendingChoice = optionalElement('orderActionPendingRestock');
        if (pendingBox) pendingBox.hidden = true;
        if (pendingChoice) { pendingChoice.checked = false; pendingChoice.onchange = null; }
        dom.btnConfirmOrderAction.disabled = normalizedType === 'refund';
        modal.orderAction.show();
        if (normalizedType === 'refund') {
            try {
                const data = await fetchJson(`/admin/pos/returns/order/${safeOrderId}/eligibility`);
                if (sequence !== state.orderActionEligibilitySequence) return;
                const returned = safeArray(data?.lines).some(line => Number(line.returnedQuantity) > 0) || Number(data?.refundedTotal) > 0;
                const blocked = safeArray(data?.lines).filter(line => Number(line.returnableQuantity) > 0 && line.canRestock === false);
                if (returned || blocked.length || !(Number(data?.refundableRemaining) > 0)) {
                    if (warning) {
                        warning.hidden = false;
                        warning.textContent = returned ? 'Đơn đã có trả hàng hoặc hoàn tiền. Hãy dùng Trả hàng / hoàn tiền để xử lý phần còn lại.'
                            : blocked.length ? blocked.map(line => `${line.itemName}: ${line.restockBlockReason}`).join('\n') + '\n' + (blocked[0].restockActionHint || '')
                            : 'Đơn không còn tiền để hoàn.';
                    }
                    if (!returned && blocked.length && Number(data?.refundableRemaining) > 0 && pendingBox && pendingChoice) {
                        pendingBox.hidden = false;
                        pendingChoice.onchange = () => { dom.btnConfirmOrderAction.disabled = !pendingChoice.checked; };
                    }
                    return;
                }
                dom.btnConfirmOrderAction.disabled = false;
            } catch (error) {
                if (sequence !== state.orderActionEligibilitySequence) return;
                if (warning) { warning.hidden = false; warning.textContent = error?.message || 'Chưa kiểm tra được điều kiện trả hàng. Hãy đóng popup và thử lại.'; }
            }
        }
    }

    function buildOrderActionPayload(type) {
        const payload = {
            reason:
                dom.orderActionReason
                    .value
                    .trim()
        };

        if (type === 'refund') {
            payload.allowPendingRestock = optionalElement('orderActionPendingRestockBox')?.hidden === false && optionalElement('orderActionPendingRestock')?.checked === true;
            payload.refundMethod =
                Number.parseInt(
                    dom
                        .orderActionRefundMethod
                        ?.value ||
                    '0',
                    10
                );

            payload.refundReferenceCode =
                dom
                    .orderActionRefundReferenceCode
                    ?.value
                    .trim() ||
                null;

            payload.refundProvider =
                dom
                    .orderActionRefundProvider
                    ?.value
                    .trim() ||
                null;
        }

        return payload;
    }

    async function submitOrderAction() {
        if (
            state.orderActionSubmitting
        ) {
            return;
        }

        const type =
            String(
                dom.orderActionType.value ||
                ''
            )
                .trim()
                .toLowerCase();

        const orderId =
            toPositiveInt(
                dom.orderActionOrderId.value,
                0
            );

        const reason =
            dom.orderActionReason
                .value
                .trim();

        if (
            ![
                'refund',
                'void'
            ].includes(type)
        ) {
            notifyError(
                'Loại xử lý đơn không hợp lệ.'
            );

            return;
        }

        if (!orderId) {
            notifyError(
                'Không xác định được đơn hàng.'
            );

            return;
        }

        if (!reason) {
            notifyError(
                'Vui lòng nhập lý do.'
            );

            dom.orderActionReason.focus();
            return;
        }

        const endpoint =
            type === 'refund'
                ? `/admin/pos/orders/${orderId}/refund`
                : `/admin/pos/orders/${orderId}/void`;

        state.orderActionSubmitting =
            true;

        setButtonBusy(
            dom.btnConfirmOrderAction,
            true,
            'Đang xử lý...'
        );

        try {
            const result =
                await fetchJson(
                    endpoint,
                    {
                        method: 'POST',
                        headers:
                            buildJsonHeaders(
                                true
                            ),
                        body:
                            JSON.stringify(
                                buildOrderActionPayload(
                                    type
                                )
                            )
                    }
                );

            modal.orderAction.hide();

            notifySuccess(
                extractMessage(result) ||
                'Xử lý thành công.'
            );

            await refreshOrderDetail(
                orderId
            );
        }
        catch (error) {
            notifyError(
                error?.message ||
                'Không thể xử lý đơn.'
            );
        }
        finally {
            state.orderActionSubmitting =
                false;

            setButtonBusy(
                dom.btnConfirmOrderAction,
                false
            );
        }
    }

    async function openReturnRefundModal(orderId) {
        if (
            state.returnSubmitting
        ) {
            return;
        }

        const safeOrderId =
            toPositiveInt(
                orderId,
                0
            );

        if (!safeOrderId) {
            notifyError(
                'Không xác định được đơn hàng.'
            );

            return;
        }

        abortEligibilityRequest();

        const requestSequence =
            ++state.eligibilitySequence;

        const controller =
            new AbortController();

        state.eligibilityController =
            controller;

        state.returnOrderId =
            safeOrderId;

        state.returnEligibility =
            null;

        dom.btnSubmitReturnRefund.disabled =
            true;

        renderModalLoading(
            dom.returnRefundModalBody,
            'Đang tải dữ liệu...'
        );

        setElementBusy(
            dom.returnRefundModalBody,
            true
        );

        modal.returnRefund.show();

        try {
            const data =
                await fetchJson(
                    `/admin/pos/returns/order/${safeOrderId}/eligibility`,
                    {
                        method: 'GET',

                        headers: {
                            Accept:
                                'application/json'
                        },

                        signal:
                            controller.signal
                    }
                );

            if (
                requestSequence !==
                state.eligibilitySequence ||
                safeOrderId !==
                state.returnOrderId
            ) {
                return;
            }

            state.returnEligibility =
                data;

            dom.returnRefundModalBody
                .innerHTML =
                renderReturnRefundForm(
                    data
                );

            applyReturnTypeUi();

            dom.btnSubmitReturnRefund.disabled =
                false;

            window.setTimeout(
                () => {
                    optionalElement(
                        'returnReason'
                    )?.focus();
                },
                0
            );
        }
        catch (error) {
            if (
                isAbortError(error) ||
                requestSequence !==
                state.eligibilitySequence
            ) {
                return;
            }

            const message =
                error?.message ||
                'Không tải được dữ liệu trả hàng / hoàn tiền.';

            renderModalError(
                dom.returnRefundModalBody,
                message
            );

            notifyError(message);
        }
        finally {
            if (
                state.eligibilityController ===
                controller
            ) {
                state.eligibilityController =
                    null;
            }

            if (
                requestSequence ===
                state.eligibilitySequence
            ) {
                setElementBusy(
                    dom.returnRefundModalBody,
                    false
                );
            }
        }
    }

    function renderReturnRefundForm(data) {
        const lines =
            safeArray(
                data?.lines
            );

        const refundMethodOptions = `
            <option value="0">
                Tiền mặt
            </option>

            <option value="1">
                Chuyển khoản
            </option>

            <option value="2">
                Thẻ
            </option>

            <option value="3">
                Ví điện tử
            </option>

            <option value="99">
                Khác
            </option>
            ${Number(data?.depositRefundable) > 0 ? '<option value="100">Hoàn vào số dư đặt cọc</option>' : ''}
        `;

        const lineRows =
            lines.length === 0
                ? `
                    <tr>
                        <td
                            colspan="9"
                            class="text-center text-muted py-4">

                            Không có dòng hàng nào để xử lý.

                        </td>
                    </tr>
                `
                : lines
                    .map(
                        (
                            line,
                            index
                        ) => {
                            const orderLineId =
                                toPositiveInt(
                                    line?.orderLineId,
                                    0
                                );

                            const maxQty =
                                toNonNegativeNumber(
                                    line?.returnableQuantity,
                                    0
                                );

                            const soldQty =
                                toNonNegativeNumber(
                                    line?.quantity ??
                                    line?.soldQuantity ??
                                    line?.originalQuantity ??
                                    line?.orderedQuantity,
                                    0
                                );

                            const rawMultiplier =
                                toFiniteNumber(
                                    line?.multiplier,
                                    1
                                );

                            const multiplier =
                                rawMultiplier > 0
                                    ? rawMultiplier
                                    : 1;

                            const unitPrice =
                                toNonNegativeNumber(
                                    line?.unitPrice,
                                    0
                                );

                            const defaultRefundUnit =
                                toNonNegativeNumber(
                                    line?.suggestedRefundUnitAmount ?? line?.refundUnitAmount ??
                                    unitPrice,
                                    unitPrice
                                );

                            const disabled =
                                orderLineId
                                    ? ''
                                    : ' disabled';

                            return `
                                <tr
                                    class="align-middle"
                                    data-order-line-id="${orderLineId}"
                                    data-restock-block-reason="${escapeHtml(line?.canRestock === false ? line.restockBlockReason || 'Chưa đủ dữ liệu giá vốn để nhập lại hàng.' : '')}">

                                    <td class="text-center">
                                        ${index + 1}
                                    </td>

                                    <td>
                                        <div class="fw-semibold">
                                            ${escapeHtml(
                                line?.itemName ||
                                ''
                            )
                                }
                                        </div>

                                        <div class="small text-muted">
                                            SKU:
                                            ${escapeHtml(
                                    line?.sku ||
                                    '-'
                                )
                                }

                                            ${line?.unitName
                                    ? ` - ĐVT: ${escapeHtml(
                                        line.unitName
                                    )
                                    }`
                                    : ''
                                }
                                        </div>
                                        ${line?.canRestock === false && maxQty > 0 ? `<div class="small text-warning mt-2">${escapeHtml(line.restockBlockReason)} ${escapeHtml(line.restockActionHint)}</div>` : ''}
                                    </td>

                                    <td class="text-end">
                                        ${formatNumber(soldQty)}
                                    </td>

                                    <td
                                        class="text-end text-primary fw-semibold">

                                        ${formatNumber(maxQty)}

                                    </td>

                                    <td style="width: 120px;">
                                        <input
                                            type="number"
                                            class="form-control text-end js-return-qty"
                                            data-order-line-id="${orderLineId}"
                                            data-max="${formatInputNumber(maxQty)}"
                                            data-multiplier="${formatInputNumber(multiplier)}"
                                            min="0"
                                            max="${formatInputNumber(maxQty)}"
                                            step="1"
                                            value="0"${disabled}>
                                    </td>

                                    <td style="width: 140px;">
                                        <input
                                            type="number"
                                            class="form-control text-end js-refund-unit"
                                            data-order-line-id="${orderLineId}"
                                            min="0"
                                            step="100"
                                            value="${formatInputNumber(defaultRefundUnit)}"${disabled}>
                                    </td>

                                    <td
                                        class="text-center"
                                        style="min-width: 170px;">
                                        <select class="form-select js-restock" aria-label="Xử lý hàng trả" data-order-line-id="${orderLineId}"${disabled}>
                                            <option value="1" ${line?.canRestock === false ? '' : 'selected'}>Nhập lại kho</option>
                                            <option value="2" ${line?.canRestock === false ? 'selected' : ''}>Chờ nhập kho</option>
                                            <option value="0">Không nhập kho</option>
                                        </select>
                                    </td>

                                    <td>
                                        <input
                                            type="text"
                                            class="form-control js-line-reason"
                                            data-order-line-id="${orderLineId}"
                                            placeholder="Lý do dòng này (nếu có)"${disabled}>
                                    </td>

                                    <td
                                        class="text-end fw-semibold js-line-total"
                                        data-order-line-id="${orderLineId}">

                                        ${formatMoney(0)}

                                    </td>

                                </tr>
                            `;
                        }
                    )
                    .join('');

        return `
            <div class="return-refund-form">

                <div class="ord-popup-callout">
                    Chọn cách xử lý ở từng món. <strong>Chờ nhập kho</strong> vẫn ghi nhận hàng đã trả và tiền hoàn,
                    hàng chưa cộng vào tồn có thể bán. Quản lý hoàn tất nhập kho sau khi xác định giá vốn.
                    Hàng lỗi không bán lại được có thể chọn <strong>Không nhập kho</strong>.
                </div>

                <div class="ret-summary">

                    <div class="ret-stat">
                        <div class="ret-stat-label">
                            Đơn hàng
                        </div>

                        <div class="ret-stat-value">
                            ${escapeHtml(
            data?.orderNumber ||
            '-'
        )
            }
                        </div>
                    </div>

                    <div class="ret-stat">
                        <div class="ret-stat-label">
                            Khách hàng
                        </div>

                        <div class="ret-stat-value">
                            ${escapeHtml(
                data?.customerName ||
                'Khách lẻ'
            )
            }
                        </div>
                    </div>

                    <div class="ret-stat">
                        <div class="ret-stat-label">
                            Tổng thanh toán
                        </div>

                        <div class="ret-stat-value">
                            ${formatMoney(data?.grandTotal || 0)}
                        </div>
                    </div>

                    <div class="ret-stat">
                        <div class="ret-stat-label">
                            Đã hoàn trước đó
                        </div>

                        <div class="ret-stat-value text-danger">
                            ${formatMoney(data?.alreadyRefunded || 0)}
                        </div>
                    </div>

                </div>

                <div class="row g-3 mb-3">

                    <div class="col-md-4">

                        <label
                            class="form-label fw-semibold"
                            for="returnType">

                            Loại xử lý

                        </label>

                        <select
                            class="form-select"
                            id="returnType">

                            <option
                                value="3"
                                selected>
                                Trả hàng + hoàn tiền
                            </option>

                            <option value="2">
                                Chỉ trả hàng
                            </option>

                            <option value="1" ${data?.isCreditSale || Number(data?.depositAmount) > 0 ? 'disabled' : ''}>
                                Chỉ hoàn tiền
                            </option>

                        </select>

                    </div>

                    <div class="col-md-8">

                        <label
                            class="form-label fw-semibold"
                            for="returnReason">

                            Lý do chung

                        </label>

                        <input
                            type="text"
                            class="form-control"
                            id="returnReason"
                            placeholder="Nhập lý do chung">

                    </div>

                </div>

                <div class="ord-box mb-3">

                    <div class="table-responsive">

                        <table
                            class="table ord-table table-hover align-middle mb-0">

                            <thead>
                                <tr>
                                    <th
                                        class="text-center"
                                        style="width: 60px;">
                                        #
                                    </th>

                                    <th>
                                        Sản phẩm
                                    </th>

                                    <th
                                        class="text-end"
                                        style="width: 100px;">
                                        Đã bán
                                    </th>

                                    <th
                                        class="text-end"
                                        style="width: 120px;">
                                        Còn trả được
                                    </th>

                                    <th
                                        class="text-end"
                                        style="width: 120px;">
                                        SL trả
                                    </th>

                                    <th
                                        class="text-end"
                                        style="width: 140px;">
                                        Đơn giá hoàn
                                    </th>

                                    <th
                                        class="text-center"
                                        style="width: 110px;">
                                        Nhập kho
                                    </th>

                                    <th style="width: 220px;">
                                        Lý do dòng
                                    </th>

                                    <th
                                        class="text-end"
                                        style="width: 140px;">
                                        Thành tiền
                                    </th>
                                </tr>
                            </thead>

                            <tbody id="returnLinesBody">
                                ${lineRows}
                            </tbody>

                        </table>

                    </div>

                </div>

                <div class="row g-3">

                    <div class="col-md-3">

                        <label
                            class="form-label fw-semibold"
                            for="refundMethod">

                            Phương thức hoàn

                        </label>

                        <select
                            class="form-select"
                            id="refundMethod">

                            ${refundMethodOptions}

                        </select>

                    </div>

                    <div class="col-md-3">

                        <label
                            class="form-label fw-semibold"
                            for="refundAmount">

                            Số tiền hoàn

                        </label>

                        <input
                            type="number"
                            class="form-control"
                            id="refundAmount"
                            min="0"
                            step="100"
                            value="0">

                    </div>

                    <div class="col-md-3">

                        <label
                            class="form-label fw-semibold"
                            for="refundReference">

                            Mã tham chiếu

                        </label>

                        <input
                            type="text"
                            class="form-control"
                            id="refundReference"
                            placeholder="Nếu có">

                    </div>

                    <div class="col-md-3">

                        <label
                            class="form-label fw-semibold"
                            for="returnSubtotalDisplay">

                            Tổng tiền hàng trả

                        </label>

                        <input
                            type="text"
                            class="form-control fw-bold"
                            id="returnSubtotalDisplay"
                            value="0 đ"
                            readonly>

                    </div>

                </div>

            </div>
        `;
    }

    function applyReturnTypeUi() {
        const returnType =
            optionalElement(
                'returnType'
            );

        const refundAmount =
            optionalElement(
                'refundAmount'
            );

        if (
            !returnType ||
            !refundAmount
        ) {
            return;
        }

        const type =
            Number.parseInt(
                returnType.value ||
                '3',
                10
            );

        const qtyInputs =
            dom.returnRefundModalBody
                .querySelectorAll(
                    '.js-return-qty'
                );

        const refundUnitInputs =
            dom.returnRefundModalBody
                .querySelectorAll(
                    '.js-refund-unit'
                );

        const restockInputs =
            dom.returnRefundModalBody
                .querySelectorAll(
                    '.js-restock'
                );

        const lineReasonInputs =
            dom.returnRefundModalBody
                .querySelectorAll(
                    '.js-line-reason'
                );

        if (type === 1) {
            qtyInputs.forEach(
                input => {
                    input.value =
                        '0';

                    input.readOnly =
                        true;
                }
            );

            refundUnitInputs.forEach(
                input => {
                    input.readOnly =
                        true;
                }
            );

            restockInputs.forEach(
                input => {
                    input.disabled =
                        true;
                }
            );

            lineReasonInputs.forEach(
                input => {
                    input.value =
                        '';

                    input.readOnly =
                        true;
                }
            );

            refundAmount.readOnly =
                false;
        }
        else if (type === 2) {
            qtyInputs.forEach(
                input => {
                    input.readOnly =
                        false;
                }
            );

            refundUnitInputs.forEach(
                input => {
                    input.readOnly =
                        false;
                }
            );

            restockInputs.forEach(
                input => {
                    input.disabled =
                        false;
                }
            );

            lineReasonInputs.forEach(
                input => {
                    input.readOnly =
                        false;
                }
            );

            refundAmount.value =
                '0';

            refundAmount.readOnly =
                true;
        }
        else {
            qtyInputs.forEach(
                input => {
                    input.readOnly =
                        false;
                }
            );

            refundUnitInputs.forEach(
                input => {
                    input.readOnly =
                        false;
                }
            );

            restockInputs.forEach(
                input => {
                    input.disabled =
                        false;
                }
            );

            lineReasonInputs.forEach(
                input => {
                    input.readOnly =
                        false;
                }
            );

            refundAmount.readOnly =
                false;
        }

        recalcReturnRefundSubtotal();
    }

    function recalcReturnRefundSubtotal() {
        let subtotal = 0;

        dom.returnRefundModalBody
            .querySelectorAll(
                '.js-return-qty'
            )
            .forEach(
                qtyInput => {
                    const row =
                        qtyInput.closest(
                            'tr'
                        );

                    if (!row) {
                        return;
                    }

                    const max =
                        toNonNegativeNumber(
                            qtyInput.dataset.max,
                            0
                        );

                    let qty =
                        toNonNegativeNumber(
                            qtyInput.value,
                            0
                        );

                    if (qty > max) {
                        qty =
                            max;

                        qtyInput.value =
                            formatInputNumber(
                                max
                            );
                    }

                    const priceInput =
                        row.querySelector(
                            '.js-refund-unit'
                        );

                    const lineTotalElement =
                        row.querySelector(
                            '.js-line-total'
                        );

                    const price =
                        toNonNegativeNumber(
                            priceInput?.value,
                            0
                        );

                    const lineTotal =
                        qty *
                        price;

                    subtotal +=
                        lineTotal;

                    if (
                        lineTotalElement
                    ) {
                        lineTotalElement.textContent =
                            formatMoney(
                                lineTotal
                            );
                    }
                }
            );

        if (state.returnEligibility?.isCreditSale) {
            const reduction = Math.min(subtotal, Number(state.returnEligibility.balanceDue) || 0);
            const refund = optionalElement('refundAmount');
            if (refund && optionalElement('returnType')?.value !== '2') { refund.value = formatInputNumber(Math.max(0, subtotal - reduction)); refund.readOnly = true; }
        }
        const subtotalDisplay =
            optionalElement(
                'returnSubtotalDisplay'
            );

        if (
            subtotalDisplay
        ) {
            subtotalDisplay.value =
                formatMoney(
                    subtotal
                );
        }
    }

    function buildReturnRefundRequest() {
        const orderId =
            toPositiveInt(
                state.returnOrderId,
                0
            );

        const returnTypeElement =
            optionalElement(
                'returnType'
            );

        const returnReasonElement =
            optionalElement(
                'returnReason'
            );

        const refundAmountElement =
            optionalElement(
                'refundAmount'
            );

        const refundMethodElement =
            optionalElement(
                'refundMethod'
            );

        const refundReferenceElement =
            optionalElement(
                'refundReference'
            );

        if (
            !returnTypeElement ||
            !returnReasonElement ||
            !refundAmountElement ||
            !refundMethodElement
        ) {
            throw new Error(
                'Biểu mẫu trả hàng / hoàn tiền chưa sẵn sàng.'
            );
        }

        const type =
            Number.parseInt(
                returnTypeElement.value ||
                '3',
                10
            );

        if (
            ![
                1,
                2,
                3
            ].includes(type)
        ) {
            throw new Error(
                'Loại xử lý trả hàng / hoàn tiền không hợp lệ.'
            );
        }

        const reason =
            returnReasonElement
                .value
                .trim();

        const refundAmount =
            toNonNegativeNumber(
                refundAmountElement.value,
                0
            );

        const refundMethod =
            Number.parseInt(
                refundMethodElement.value ||
                '0',
                10
            );

        const refundReference =
            refundReferenceElement
                ?.value
                .trim() ||
            '';

        const lines = [];

        if (
            type === 2 ||
            type === 3
        ) {
            dom.returnRefundModalBody
                .querySelectorAll(
                    '.js-return-qty'
                )
                .forEach(
                    qtyInput => {
                        const qty =
                            Number(
                                qtyInput.value ||
                                0
                            );

                        if (
                            !Number.isFinite(qty) ||
                            qty <= 0
                        ) {
                            return;
                        }

                        const max =
                            toNonNegativeNumber(
                                qtyInput.dataset.max,
                                0
                            );

                        if (
                            qty > max
                        ) {
                            throw new Error(
                                'Số lượng trả vượt quá số lượng còn được phép trả.'
                            );
                        }

                        const row =
                            qtyInput.closest(
                                'tr'
                            );

                        if (!row) {
                            throw new Error(
                                'Không xác định được dòng hàng trả.'
                            );
                        }

                        const orderLineId =
                            toPositiveInt(
                                qtyInput
                                    .dataset
                                    .orderLineId,
                                0
                            );

                        if (!orderLineId) {
                            throw new Error(
                                'Dòng hàng trả không hợp lệ.'
                            );
                        }

                        const multiplier =
                            toFiniteNumber(
                                qtyInput
                                    .dataset
                                    .multiplier,
                                0
                            );

                        if (
                            multiplier <= 0
                        ) {
                            throw new Error(
                                'Hệ số quy đổi dòng hàng không hợp lệ.'
                            );
                        }

                        const refundUnitInput =
                            row.querySelector(
                                '.js-refund-unit'
                            );

                        const refundUnitAmount =
                            Number(
                                refundUnitInput
                                    ?.value ||
                                0
                            );

                        if (
                            !Number.isFinite(
                                refundUnitAmount
                            ) ||
                            refundUnitAmount < 0
                        ) {
                            throw new Error(
                                'Đơn giá hoàn không hợp lệ.'
                            );
                        }

                        const restockInput =
                            row.querySelector(
                                '.js-restock'
                            );

                        const returnAction = Number(restockInput?.value);
                        if (![0, 1, 2].includes(returnAction)) throw new Error('Chọn cách xử lý hàng trả.');
                        if (returnAction === 1 && row.dataset.restockBlockReason)
                            throw new Error(row.dataset.restockBlockReason + ' Có thể chọn Chờ nhập kho để nhận trả và hoàn tiền ngay.');

                        const lineReasonInput =
                            row.querySelector(
                                '.js-line-reason'
                            );

                        lines.push({
                            orderLineId,

                            returnQuantity:
                                qty,

                            returnBaseQuantity:
                                qty *
                                multiplier,

                            refundUnitAmount,

                            action: returnAction,

                            reason:
                                lineReasonInput
                                    ?.value
                                    .trim() ||
                                null
                        });
                    }
                );
        }

        const payments = [];

        if (
            (
                type === 1 ||
                type === 3
            ) &&
            refundAmount > 0 && refundMethod !== 100
        ) {
            payments.push({
                method:
                    Number.isInteger(
                        refundMethod
                    )
                        ? refundMethod
                        : 0,

                amount:
                    refundAmount,

                referenceCode:
                    refundReference
            });
        }

        return {
            orderId,
            posShiftId: 0,
            depositRefundAmount: refundMethod === 100 ? refundAmount : 0,
            type,
            reason,
            lines,
            payments
        };
    }

    async function submitReturnRefund() {
        if (
            state.returnSubmitting
        ) {
            return;
        }

        let payload;

        try {
            payload =
                buildReturnRefundRequest();
        }
        catch (error) {
            notifyError(
                error?.message ||
                'Dữ liệu trả hàng / hoàn tiền không hợp lệ.'
            );

            return;
        }

        if (!payload.orderId) {
            notifyError(
                'Không xác định được đơn hàng.'
            );

            return;
        }

        if (!payload.reason) {
            notifyError(
                'Vui lòng nhập lý do.'
            );

            optionalElement(
                'returnReason'
            )?.focus();

            return;
        }

        if (
            (
                payload.type === 2 ||
                payload.type === 3
            ) &&
            payload.lines.length === 0
        ) {
            notifyError(
                'Vui lòng nhập ít nhất một dòng hàng trả.'
            );

            return;
        }

        if (
            (
                payload.type === 1 ||
                payload.type === 3
            ) &&
            payload.payments.length === 0 && !(payload.depositRefundAmount > 0)
        ) {
            notifyError(
                'Vui lòng nhập số tiền hoàn.'
            );

            optionalElement(
                'refundAmount'
            )?.focus();

            return;
        }

        const orderId =
            payload.orderId;

        state.returnSubmitting =
            true;

        setButtonBusy(
            dom.btnSubmitReturnRefund,
            true,
            'Đang lưu...'
        );

        try {
            const result =
                await fetchJson(
                    RETURN_CREATE_ENDPOINT,
                    {
                        method: 'POST',

                        headers:
                            buildJsonHeaders(
                                true
                            ),

                        body:
                            JSON.stringify(
                                payload
                            )
                    }
                );

            modal.returnRefund.hide();

            notifySuccess(
                extractMessage(result) ||
                'Đã tạo phiếu trả hàng / hoàn tiền thành công.'
            );

            await refreshOrderDetail(
                orderId
            );
        }
        catch (error) {
            notifyError(
                error?.message ||
                'Không thể tạo phiếu trả hàng / hoàn tiền.'
            );
        }
        finally {
            state.returnSubmitting =
                false;

            setButtonBusy(
                dom.btnSubmitReturnRefund,
                false
            );
        }
    }

    async function openReturnHistoryModal(orderId) {
        const safeOrderId =
            toPositiveInt(
                orderId,
                0
            );

        if (!safeOrderId) {
            notifyError(
                'Không xác định được đơn hàng.'
            );

            return;
        }

        abortHistoryRequest();

        const requestSequence =
            ++state.historySequence;

        const controller =
            new AbortController();

        state.historyController =
            controller;

        renderModalLoading(
            dom.returnHistoryModalBody,
            'Đang tải lịch sử...'
        );

        setElementBusy(
            dom.returnHistoryModalBody,
            true
        );

        modal.returnHistory.show();

        try {
            const data =
                await fetchJson(
                    `/admin/pos/returns/order/${safeOrderId}/history`,
                    {
                        method: 'GET',

                        headers: {
                            Accept:
                                'application/json'
                        },

                        signal:
                            controller.signal
                    }
                );

            if (
                requestSequence !==
                state.historySequence
            ) {
                return;
            }

            dom.returnHistoryModalBody
                .innerHTML =
                renderReturnHistory(
                    data
                );
        }
        catch (error) {
            if (
                isAbortError(error) ||
                requestSequence !==
                state.historySequence
            ) {
                return;
            }

            const message =
                error?.message ||
                'Không tải được lịch sử return.';

            renderModalError(
                dom.returnHistoryModalBody,
                message
            );

            notifyError(message);
        }
        finally {
            if (
                state.historyController ===
                controller
            ) {
                state.historyController =
                    null;
            }

            if (
                requestSequence ===
                state.historySequence
            ) {
                setElementBusy(
                    dom.returnHistoryModalBody,
                    false
                );
            }
        }
    }

    function normalizeReturnHistoryItems(value) {
        if (
            Array.isArray(value)
        ) {
            return value;
        }

        return safeArray(
            value?.items
        );
    }

    function renderReturnHistory(input) {
        const items =
            normalizeReturnHistoryItems(
                input
            );

        if (
            items.length === 0
        ) {
            return `
                <div class="ord-empty">

                    <div class="ord-empty-title">
                        Chưa có lịch sử trả hàng / hoàn tiền
                    </div>

                    <div class="ord-empty-desc">
                        Đơn này hiện chưa phát sinh nghiệp vụ hậu mãi.
                    </div>

                </div>
            `;
        }

        const totalRefund =
            items.reduce(
                (
                    sum,
                    item
                ) =>
                    sum +
                    toFiniteNumber(
                        item?.refundTotal,
                        0
                    ),
                0
            );

        const itemHtml =
            items
                .map(
                    (
                        item,
                        index
                    ) => {
                        const returnId =
                            toPositiveInt(
                                item?.id,
                                0
                            );

                        const detailId =
                            `return-history-detail-${index + 1}-${returnId || 0}`;

                        const lineCount =
                            safeArray(
                                item?.lines
                            ).length;

                        const paymentCount =
                            safeArray(
                                item?.payments
                            ).length;

                        return `
                            <div class="ret-history-item">

                                <div class="ret-history-header">

                                    <div>

                                        <div class="ret-code">
                                            ${escapeHtml(
                            item?.returnNumber ||
                            '-'
                        )
                            }
                                        </div>
                                        ${item?.hasPendingRestock ? '<div class="badge bg-warning text-dark mt-1">Đã nhận hàng · Chờ nhập kho</div>' : ''}

                                        <div class="ret-meta-line">

                                            <span
                                                class="ord-badge ord-badge-info">

                                                ${escapeHtml(
                                getReturnTypeText(
                                    item?.type
                                )
                            )
                            }

                                            </span>

                                            <span
                                                class="ord-badge ord-badge-success">

                                                ${escapeHtml(
                                getReturnStatusText(
                                    item?.status
                                )
                            )
                            }

                                            </span>

                                            <span class="ret-meta-text">
                                                ${formatDateTime(
                                item?.createdAtUtc
                            )
                            }
                                            </span>

                                        </div>

                                        <div class="ret-meta-line">

                                            <span class="ret-meta-text">
                                                ${lineCount} dòng hàng trả
                                            </span>

                                            <span class="ret-meta-text">
                                                •
                                            </span>

                                            <span class="ret-meta-text">
                                                ${paymentCount} payment hoàn
                                            </span>

                                        </div>

                                    </div>

                                    <div class="ret-amount-box">

                                        <div class="ret-amount-label">
                                            Hoàn tiền
                                        </div>

                                        <div class="ret-amount-value">
                                            ${formatMoney(
                                item?.refundTotal
                            )
                            }
                                        </div>

                                        <div class="ret-amount-sub">
                                            ${Number(item?.depositRestoredTotal) > 0 ? 'Hoàn vào cọc: ' + formatMoney(item.depositRestoredTotal) : 'Tiền hoàn thực tế'}
                                        </div>

                                    </div>

                                </div>

                                <div class="ret-reason-box">

                                    <div class="ret-reason-label">
                                        Lý do
                                    </div>

                                    <div class="ret-reason-value">
                                        ${escapeHtml(
                                item?.reason ||
                                '-'
                            )
                            }
                                    </div>

                                </div>

                                <div class="ret-action-row">

                                    <button
                                        type="button"
                                        class="btn btn-sm btn-ord btn-ord-soft"
                                        data-return-history-toggle="${detailId}"
                                        aria-expanded="false"
                                        aria-controls="${detailId}">

                                        Xem chi tiết phiếu

                                    </button>

                                </div>

                                <div
                                    id="${detailId}"
                                    class="ret-detail-shell"
                                    hidden>

                                    ${renderReturnHistoryDetail(
                                item
                            )
                            }

                                </div>

                            </div>
                        `;
                    }
                )
                .join('');

        return `
            <div class="ord-popup-callout">
                Đây là toàn bộ các phiếu hậu mãi phát sinh trên đơn này.
                Mỗi phiếu có thể chứa hàng trả, payment hoàn
                và thông tin nhập kho.
            </div>

            <div class="ret-summary-modern">

                <div class="ret-hero-main">

                    <div class="ret-hero-label">
                        Tổng tiền đã hoàn
                    </div>

                    <div class="ret-hero-value">
                        ${formatMoney(totalRefund)}
                    </div>

                    <div class="ret-hero-sub">
                        Tính từ toàn bộ phiếu return/refund
                        đã ghi nhận trên đơn này.
                    </div>

                </div>

                <div class="ret-hero-stats">

                    <div class="ret-mini-stat">

                        <div class="ret-mini-label">
                            Số phiếu hậu mãi
                        </div>

                        <div class="ret-mini-value">
                            ${items.length}
                        </div>

                    </div>

                    <div class="ret-mini-stat">

                        <div class="ret-mini-label">
                            Tổng tiền đơn
                        </div>

                        <div class="ret-mini-value">
                            ${formatMoney(
            posOrderDetailServerData
                .grandTotal
        )
            }
                        </div>

                        <div class="ret-mini-sub">
                            Tạm tính:
                            ${formatMoney(
                posOrderDetailServerData
                    .subtotal
            )
            }
                        </div>

                    </div>

                    <div class="ret-mini-stat">

                        <div class="ret-mini-label">
                            Đã thanh toán
                        </div>

                        <div class="ret-mini-value">
                            ${formatMoney(
                posOrderDetailServerData
                    .paidTotal
            )
            }
                        </div>

                    </div>

                    <div class="ret-mini-stat">

                        <div class="ret-mini-label">
                            Còn có thể hoàn
                        </div>

                        <div class="ret-mini-value">
                            ${formatMoney(
                posOrderDetailServerData
                    .refundableRemaining
            )
            }
                        </div>

                    </div>

                </div>

            </div>

            <div class="ret-list">
                ${itemHtml}
            </div>
        `;
    }

    function renderReturnHistoryDetail(item) {
        const lines =
            safeArray(
                item?.lines
            );

        const payments =
            safeArray(
                item?.payments
            );

        const linesHtml =
            lines.length === 0
                ? `
                    <div class="ret-empty-box">
                        Không có dòng hàng trả.
                    </div>
                `
                : `
                    <div class="ret-panel">

                        <div class="ret-panel-head">
                            Dòng hàng trả
                        </div>

                        <div class="ret-panel-body">

                            <div class="table-responsive">

                                <table
                                    class="table ret-table align-middle mb-0">

                                    <thead>
                                        <tr>
                                            <th>
                                                Sản phẩm
                                            </th>

                                            <th class="text-end">
                                                SL trả
                                            </th>

                                            <th class="text-end">
                                                SL quy đổi
                                            </th>

                                            <th class="text-end">
                                                Đơn giá hoàn
                                            </th>

                                            <th class="text-end">
                                                Thành tiền
                                            </th>

                                            <th class="text-center">
                                                Nhập kho
                                            </th>
                                        </tr>
                                    </thead>

                                    <tbody>

                                        ${lines
                    .map(
                        line => `
                                                        <tr>

                                                            <td>
                                                                <strong>
                                                                    ${escapeHtml(
                            line?.itemName ||
                            ''
                        )
                            }
                                                                </strong>
                                                            </td>

                                                            <td class="text-end">
                                                                ${formatNumber(
                                line?.returnQuantity
                            )
                            }
                                                            </td>

                                                            <td class="text-end">
                                                                ${formatNumber(
                                line?.returnBaseQuantity
                            )
                            }
                                                            </td>

                                                            <td class="text-end">
                                                                ${formatMoney(
                                line?.refundUnitAmount
                            )
                            }
                                                            </td>

                                                            <td class="text-end fw-bold">
                                                                ${formatMoney(
                                line?.refundLineTotal
                            )
                            }
                                                            </td>

                                                            <td class="text-center">
                                                                ${escapeHtml(
                                getRestockText(
                                    line?.action
                                )
                            )
                            }
                                                            </td>

                                                        </tr>
                                                    `
                    )
                    .join('')
                }

                                    </tbody>

                                </table>

                            </div>

                        </div>

                    </div>
                `;

        const paymentsHtml =
            payments.length === 0
                ? `
                    <div class="ret-empty-box">
                        Không có payment hoàn tiền.
                    </div>
                `
                : `
                    <div class="ret-panel">

                        <div class="ret-panel-head">
                            Payment hoàn tiền
                        </div>

                        <div class="ret-panel-body">

                            <div class="table-responsive">

                                <table
                                    class="table ret-table align-middle mb-0">

                                    <thead>
                                        <tr>
                                            <th>
                                                Phương thức
                                            </th>

                                            <th>
                                                Mã tham chiếu
                                            </th>

                                            <th>
                                                Provider
                                            </th>

                                            <th>
                                                Thời gian
                                            </th>

                                            <th class="text-end">
                                                Số tiền
                                            </th>
                                        </tr>
                                    </thead>

                                    <tbody>

                                        ${payments
                    .map(
                        payment => `
                                                        <tr>

                                                            <td>
                                                                <strong>
                                                                    ${escapeHtml(
                            payment?.method ??
                            '-'
                        )
                            }
                                                                </strong>
                                                            </td>

                                                            <td>
                                                                ${escapeHtml(
                                payment?.referenceCode ||
                                '-'
                            )
                            }
                                                            </td>

                                                            <td>
                                                                ${escapeHtml(
                                payment?.provider ||
                                '-'
                            )
                            }
                                                            </td>

                                                            <td>
                                                                ${formatDateTime(
                                payment?.paidAtUtc
                            )
                            }
                                                            </td>

                                                            <td class="text-end fw-bold">
                                                                ${formatMoney(
                                payment?.amount
                            )
                            }
                                                            </td>

                                                        </tr>
                                                    `
                    )
                    .join('')
                }

                                    </tbody>

                                </table>

                            </div>

                        </div>

                    </div>
                `;

        return `
            <div class="ret-detail-grid">
                ${linesHtml}
                ${paymentsHtml}
            </div>
        `;
    }

    function toggleReturnHistoryDetail(idOrElement) {
        let element = null;

        if (
            typeof idOrElement ===
            'string'
        ) {
            element =
                document.getElementById(
                    idOrElement
                );
        }
        else {
            const legacyId =
                toPositiveInt(
                    idOrElement,
                    0
                );

            if (legacyId) {
                element =
                    document.getElementById(
                        `return-history-detail-${legacyId}`
                    );
            }
        }

        if (!element) {
            return;
        }

        const button =
            dom.returnHistoryModalBody
                ?.querySelector(
                    `[aria-controls="${element.id}"]`
                ) ||
            null;

        const willOpen =
            element.hidden ||
            element.style.display ===
            'none';

        element.hidden =
            !willOpen;

        element.style.display =
            willOpen
                ? ''
                : 'none';

        if (button) {
            button.setAttribute(
                'aria-expanded',
                willOpen
                    ? 'true'
                    : 'false'
            );

            button.textContent =
                willOpen
                    ? 'Thu gọn chi tiết'
                    : 'Xem chi tiết phiếu';
        }
    }

    function getReturnTypeText(type) {
        const value =
            String(type ?? '');

        switch (value) {
            case '1':
            case 'RefundOnly':
                return 'Chỉ hoàn tiền';

            case '2':
            case 'ReturnOnly':
                return 'Chỉ trả hàng';

            case '3':
            case 'ReturnAndRefund':
                return 'Trả hàng + hoàn tiền';

            case '4':
            case 'Exchange':
                return 'Đổi hàng';

            default:
                return value || '-';
        }
    }

    function getReturnStatusText(status) {
        const value =
            String(status ?? '');

        switch (value) {
            case '0':
            case 'Draft':
                return 'Nháp';

            case '1':
            case 'Completed':
                return 'Hoàn tất';

            case '2':
            case 'Cancelled':
                return 'Đã hủy';

            default:
                return value || '-';
        }
    }

    function getRestockText(action) {
        const value =
            String(action ?? '');

        switch (value) {
            case '1':
            case 'Restock':
                return 'Có';

            case '0':
            case 'NoRestock':
                return 'Không';
            case '2':
            case 'PendingRestock':
                return 'Chờ nhập kho';

            default:
                return '-';
        }
    }

    function bindStaticEvents() {
        dom.btnConfirmOrderAction
            .addEventListener(
                'click',
                () => {
                    void submitOrderAction();
                }
            );

        dom.btnSubmitReturnRefund
            .addEventListener(
                'click',
                () => {
                    void submitReturnRefund();
                }
            );

        dom.orderActionRefundMethod
            ?.addEventListener(
                'change',
                function () {
                    const method =
                        Number.parseInt(
                            this.value ||
                            '0',
                            10
                        );

                    if (
                        dom.orderActionRefundNonCashBox
                    ) {
                        dom
                            .orderActionRefundNonCashBox
                            .style.display =
                            method === 0
                                ? 'none'
                                : '';
                    }

                    if (
                        method !== 0
                    ) {
                        window.setTimeout(
                            () => {
                                dom
                                    .orderActionRefundReferenceCode
                                    ?.focus();
                            },
                            0
                        );
                    }
                }
            );

        dom.returnRefundModalBody
            .addEventListener(
                'input',
                event => {
                    const target =
                        event.target;

                    if (
                        target instanceof Element &&
                        target.matches(
                            '.js-return-qty, .js-refund-unit'
                        )
                    ) {
                        recalcReturnRefundSubtotal();
                    }
                }
            );

        dom.returnRefundModalBody
            .addEventListener(
                'change',
                event => {
                    const target =
                        event.target;

                    if (
                        target instanceof Element &&
                        target.id ===
                        'returnType'
                    ) {
                        applyReturnTypeUi();
                    }
                }
            );

        dom.returnHistoryModalBody
            .addEventListener(
                'click',
                event => {
                    const target =
                        event.target instanceof Element
                            ? event.target.closest(
                                '[data-return-history-toggle]'
                            )
                            : null;

                    if (
                        !target ||
                        !dom
                            .returnHistoryModalBody
                            .contains(target)
                    ) {
                        return;
                    }

                    const detailId =
                        target.getAttribute(
                            'data-return-history-toggle'
                        );

                    if (detailId) {
                        toggleReturnHistoryDetail(
                            detailId
                        );
                    }
                }
            );

        dom.orderActionModalElement
            .addEventListener(
                'shown.bs.modal',
                () => {
                    const type =
                        String(
                            dom
                                .orderActionType
                                .value ||
                            ''
                        ).toLowerCase();

                    if (
                        type === 'refund'
                    ) {
                        dom
                            .orderActionRefundMethod
                            ?.focus();
                    }
                    else {
                        dom
                            .orderActionReason
                            .focus();
                    }
                }
            );

        dom.orderActionModalElement.addEventListener('hide.bs.modal', () => { ++state.orderActionEligibilitySequence; });

        dom.returnRefundModalElement
            .addEventListener(
                'hide.bs.modal',
                () => {
                    abortEligibilityRequest();

                    ++state.eligibilitySequence;

                    if (
                        !state.returnSubmitting
                    ) {
                        state.returnOrderId =
                            0;

                        state.returnEligibility =
                            null;
                    }
                }
            );

        dom.returnHistoryModalElement
            .addEventListener(
                'hidden.bs.modal',
                () => {
                    abortHistoryRequest();

                    ++state.historySequence;
                }
            );
    }

    function init() {
        if (
            state.initialized
        ) {
            return;
        }

        if (
            !window.bootstrap?.Modal
        ) {
            throw new Error(
                '[POS Order Detail] Bootstrap Modal is unavailable.'
            );
        }

        dom.orderActionModalElement =
            requiredElement(
                'orderActionModal'
            );

        dom.paymentHistoryModalElement =
            requiredElement(
                'paymentHistoryModal'
            );

        dom.returnRefundModalElement =
            requiredElement(
                'returnRefundModal'
            );

        dom.returnHistoryModalElement =
            requiredElement(
                'returnHistoryModal'
            );

        dom.btnConfirmOrderAction =
            requiredElement(
                'btnConfirmOrderAction'
            );

        dom.btnSubmitReturnRefund =
            requiredElement(
                'btnSubmitReturnRefund'
            );

        dom.returnRefundModalBody =
            requiredElement(
                'returnRefundModalBody'
            );

        dom.returnHistoryModalBody =
            requiredElement(
                'returnHistoryModalBody'
            );

        dom.orderActionType =
            requiredElement(
                'orderActionType'
            );

        dom.orderActionOrderId =
            requiredElement(
                'orderActionOrderId'
            );

        dom.orderActionReason =
            requiredElement(
                'orderActionReason'
            );

        dom.orderActionSubtitle =
            requiredElement(
                'orderActionSubtitle'
            );

        dom.orderActionCallout =
            requiredElement(
                'orderActionCallout'
            );

        dom.orderActionRefundMethodBox =
            optionalElement(
                'orderActionRefundMethodBox'
            );

        dom.orderActionRefundNonCashBox =
            optionalElement(
                'orderActionRefundNonCashBox'
            );

        dom.orderActionRefundMethod =
            optionalElement(
                'orderActionRefundMethod'
            );

        dom.orderActionRefundReferenceCode =
            optionalElement(
                'orderActionRefundReferenceCode'
            );

        dom.orderActionRefundProvider =
            optionalElement(
                'orderActionRefundProvider'
            );

        modal.orderAction =
            new window.bootstrap.Modal(
                dom.orderActionModalElement
            );

        modal.paymentHistory =
            new window.bootstrap.Modal(
                dom.paymentHistoryModalElement
            );

        modal.returnRefund =
            new window.bootstrap.Modal(
                dom.returnRefundModalElement
            );

        modal.returnHistory =
            new window.bootstrap.Modal(
                dom.returnHistoryModalElement
            );

        dom.returnRefundModalBody
            .setAttribute(
                'aria-live',
                'polite'
            );

        dom.returnHistoryModalBody
            .setAttribute(
                'aria-live',
                'polite'
            );

        bindStaticEvents();

        state.initialized =
            true;
    }

    /*
     * Giữ page-level function surface hiện tại.
     * Razor View có thể đang gọi các function này qua onclick.
     */
    Object.assign(
        window,
        {
            getAntiForgeryToken,
            notifySuccess,
            notifyError,
            fetchJson,
            formatMoney,
            formatNumber,
            formatDateTime,
            refreshOrderDetail,
            openPaymentHistoryModal,
            openOrderActionModal,
            submitOrderAction,
            openReturnRefundModal,
            renderReturnRefundForm,
            applyReturnTypeUi,
            recalcReturnRefundSubtotal,
            buildReturnRefundRequest,
            submitReturnRefund,
            openReturnHistoryModal,
            renderReturnHistory,
            renderReturnHistoryDetail,
            toggleReturnHistoryDetail,
            getReturnTypeText,
            getReturnStatusText,
            getRestockText
        }
    );

    if (
        document.readyState ===
        'loading'
    ) {
        document.addEventListener(
            'DOMContentLoaded',
            init,
            {
                once: true
            }
        );
    }
    else {
        init();
    }
})();
