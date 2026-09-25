(() => {
    'use strict';

    // =========================================================
    // CONSTANTS
    // =========================================================
    const SUMMARY_EMPTY_TEXT =
        'Chọn một ca ở danh sách bên trái để xem chi tiết.';

    const SUMMARY_LOADING_TEXT =
        'Đang tải tổng quan ca...';

    const SUMMARY_ERROR_TEXT =
        'Không tải được tổng quan ca. Vui lòng thử lại.';

    const HISTORY_LOADING_TEXT =
        'Đang tải...';

    const HISTORY_ERROR_TEXT =
        'Không tải được dữ liệu';

    // =========================================================
    // REQUIRED DOM HELPER
    // =========================================================
    function requiredElement(id) {
        const element = document.getElementById(id);

        if (!element) {
            throw new Error(
                `[POS Shift History] Không tìm thấy #${id}.`
            );
        }

        return element;
    }

    // =========================================================
    // INIT
    // =========================================================
    function init() {

        // =====================================================
        // DOM - FILTER
        // =====================================================
        const filterStatus =
            requiredElement('filterStatus');

        const filterFrom =
            requiredElement('filterFrom');

        const filterTo =
            requiredElement('filterTo');

        const btnSearch =
            requiredElement('btnSearch');

        // =====================================================
        // DOM - HISTORY TABLE
        // =====================================================
        const historyBody =
            requiredElement('historyBody');

        const historyPagingInfo =
            requiredElement('historyPagingInfo');

        const btnPrevPage =
            requiredElement('btnPrevPage');

        const btnNextPage =
            requiredElement('btnNextPage');

        const pageSizeSelect =
            requiredElement('pageSizeSelect');

        // =====================================================
        // DOM - SUMMARY
        // =====================================================
        const summaryEmpty =
            requiredElement('summaryEmpty');

        const summaryContent =
            requiredElement('summaryContent');

        const selectedShiftCode =
            requiredElement('selectedShiftCode');

        const selectedShiftStatus =
            requiredElement('selectedShiftStatus');

        const selectedOpenedAt =
            requiredElement('selectedOpenedAt');

        const selectedClosedAt =
            requiredElement('selectedClosedAt');

        const sumTotalOrders =
            requiredElement('sumTotalOrders');

        const sumCompletedOrders =
            requiredElement('sumCompletedOrders');

        const sumDraftOrders =
            requiredElement('sumDraftOrders');

        const sumCancelledOrders =
            requiredElement('sumCancelledOrders');

        const sumCompletedSalesTotal =
            requiredElement('sumCompletedSalesTotal');

        const sumCashSalesTotal =
            requiredElement('sumCashSalesTotal');

        const sumNonCashSalesTotal =
            requiredElement('sumNonCashSalesTotal');

        const sumCashRefundTotal =
            requiredElement('sumCashRefundTotal');

        const sumNonCashRefundTotal =
            requiredElement('sumNonCashRefundTotal');

        const sumRefundTotal =
            requiredElement('sumRefundTotal');

        const sumOpeningCash =
            requiredElement('sumOpeningCash');

        const sumCashInTotal =
            requiredElement('sumCashInTotal');

        const sumCashOutTotal =
            requiredElement('sumCashOutTotal');

        const sumClosingCashExpected =
            requiredElement('sumClosingCashExpected');

        const sumClosingCashActual =
            requiredElement('sumClosingCashActual');

        const sumClosingCashActualHint =
            requiredElement('sumClosingCashActualHint');

        const sumOpenNote =
            requiredElement('sumOpenNote');

        const sumCloseNote =
            requiredElement('sumCloseNote');

        const ordersCountText =
            requiredElement('ordersCountText');

        const cashTxnsCountText =
            requiredElement('cashTxnsCountText');

        // =====================================================
        // DOM - SUMMARY ACTIONS
        // =====================================================
        const btnOpenOrdersModal =
            requiredElement('btnOpenOrdersModal');

        const btnOpenCashTxnsModal =
            requiredElement('btnOpenCashTxnsModal');

        const btnPrintShiftReport =
            requiredElement('btnPrintShiftReport');

        const btnExportShiftReport =
            requiredElement('btnExportShiftReport');

        // =====================================================
        // DOM - QUICK FILTER
        // =====================================================
        const btnToday =
            requiredElement('btnToday');

        const btnLast7Days =
            requiredElement('btnLast7Days');

        const btnLast30Days =
            requiredElement('btnLast30Days');

        const btnClearDate =
            requiredElement('btnClearDate');

        // =====================================================
        // DOM - MODALS
        // =====================================================
        const ordersModalElement =
            requiredElement('ordersModal');

        const cashTxnsModalElement =
            requiredElement('cashTxnsModal');

        const ordersModalBody =
            requiredElement('ordersModalBody');

        const cashTxnsModalBody =
            requiredElement('cashTxnsModalBody');

        const ordersModalPagingInfo =
            requiredElement('ordersModalPagingInfo');

        const cashTxnsModalPagingInfo =
            requiredElement('cashTxnsModalPagingInfo');

        const btnOrdersModalPrev =
            requiredElement('btnOrdersModalPrev');

        const btnOrdersModalNext =
            requiredElement('btnOrdersModalNext');

        const btnCashTxnsModalPrev =
            requiredElement('btnCashTxnsModalPrev');

        const btnCashTxnsModalNext =
            requiredElement('btnCashTxnsModalNext');

        // =====================================================
        // BOOTSTRAP CHECK
        // =====================================================
        if (
            !window.bootstrap ||
            !window.bootstrap.Modal
        ) {
            throw new Error(
                '[POS Shift History] Bootstrap Modal chưa sẵn sàng.'
            );
        }

        const ordersModal =
            new bootstrap.Modal(
                ordersModalElement
            );

        const cashTxnsModal =
            new bootstrap.Modal(
                cashTxnsModalElement
            );

        // =====================================================
        // PAGE STATE
        // =====================================================
        const pageState = {
            page: 1,
            pageSize: 20,
            totalPages: 1,

            selectedShiftId: null,
            selectedShiftRow: null,
            selectedSummary: null,

            currentItems: []
        };

        // =====================================================
        // ORDERS MODAL STATE
        // =====================================================
        const ordersModalState = {
            page: 1,
            pageSize: 8,
            items: []
        };

        // =====================================================
        // CASH TRANSACTION MODAL STATE
        // =====================================================
        const cashTxnsModalState = {
            page: 1,
            pageSize: 8,
            items: []
        };

        // =====================================================
        // REQUEST STATE
        // =====================================================
        let historyRequestSeq = 0;
        let summaryRequestSeq = 0;

        let historyAbortController = null;
        let summaryAbortController = null;

        let isHistoryLoading = false;
        let isSummaryLoading = false;
        function setupAccessibility() {
            historyPagingInfo.setAttribute(
                'aria-live',
                'polite'
            );

            historyPagingInfo.setAttribute(
                'aria-atomic',
                'true'
            );

            summaryEmpty.setAttribute(
                'aria-live',
                'polite'
            );

            summaryEmpty.setAttribute(
                'aria-atomic',
                'true'
            );

            summaryContent.setAttribute(
                'aria-busy',
                'false'
            );
        }

        // =====================================================
        // GENERIC HELPERS
        // =====================================================

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

        function toPositiveInt(
            value,
            fallback = 0
        ) {
            const number =
                Number.parseInt(
                    value,
                    10
                );

            return (
                Number.isInteger(number) &&
                number > 0
            )
                ? number
                : fallback;
        }

        function toNonNegativeInt(
            value,
            fallback = 0
        ) {
            const number =
                Number.parseInt(
                    value,
                    10
                );

            return (
                Number.isInteger(number) &&
                number >= 0
            )
                ? number
                : fallback;
        }

        function asArray(value) {
            return Array.isArray(value)
                ? value
                : [];
        }

        function formatMoney(value) {
            const number =
                Number(value ?? 0);

            if (!Number.isFinite(number)) {
                return '0';
            }

            return number
                .toLocaleString('vi-VN');
        }

        function hasValue(value) {
            return (
                value !== null &&
                value !== undefined &&
                value !== ''
            );
        }

        function formatOptionalMoney(value) {
            if (!hasValue(value)) {
                return '-';
            }

            const number =
                Number(value);

            if (!Number.isFinite(number)) {
                return '-';
            }

            return number
                .toLocaleString('vi-VN');
        }

        function textOrDash(value) {
            const text =
                String(value ?? '')
                    .trim();

            return text || '-';
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

            return date
                .toLocaleString(
                    'vi-VN'
                );
        }

        function toDateInputValue(date) {
            if (
                !(date instanceof Date) ||
                Number.isNaN(
                    date.getTime()
                )
            ) {
                return '';
            }

            const year =
                date.getFullYear();

            const month =
                String(
                    date.getMonth() + 1
                ).padStart(
                    2,
                    '0'
                );

            const day =
                String(
                    date.getDate()
                ).padStart(
                    2,
                    '0'
                );

            return `${year}-${month}-${day}`;
        }

        // =====================================================
        // TEXT NORMALIZE
        // =====================================================
        function normalizePlainText(
            value,
            maxLength = 500
        ) {
            const text =
                String(value ?? '')
                    .replace(
                        /<[^>]*>/g,
                        ' '
                    )
                    .replace(
                        /\s+/g,
                        ' '
                    )
                    .trim();

            if (!text) {
                return '';
            }

            return (
                text.length > maxLength
            )
                ? `${text.slice(
                    0,
                    maxLength
                )}...`
                : text;
        }

        // =====================================================
        // ERROR DISPLAY
        // =====================================================
        function showError(message) {
            const text =
                normalizePlainText(
                    message ||
                    'Có lỗi xảy ra'
                ) ||
                'Có lỗi xảy ra';

            if (
                window.toastr &&
                typeof window.toastr.error ===
                'function'
            ) {
                window.toastr.error(
                    escapeHtml(text)
                );

                return;
            }

            console.error(text);

            window.alert(text);
        }

        // =====================================================
        // DATE VALIDATION
        // =====================================================
        function validateDateRange() {
            if (
                filterFrom.value &&
                filterTo.value &&
                filterFrom.value >
                filterTo.value
            ) {
                showError(
                    'Từ ngày không được lớn hơn Đến ngày.'
                );

                filterFrom.focus();

                return false;
            }

            return true;
        }

        // =====================================================
        // ABORT ERROR
        // =====================================================
        function isAbortError(error) {
            return (
                error?.name ===
                'AbortError'
            );
        }

        // =====================================================
        // FETCH JSON
        // =====================================================
        async function fetchJson(
            url,
            options = {}
        ) {
            const response =
                await fetch(
                    url,
                    options
                );

            const contentType =
                response.headers
                    .get('content-type') ||
                '';

            let jsonData = null;
            let textData = '';

            try {
                if (
                    contentType.includes(
                        'application/json'
                    )
                ) {
                    jsonData =
                        await response.json();
                } else {
                    textData =
                        await response.text();
                }
            }
            catch {
                // Body lỗi parse:
                // status sẽ xử lý phía dưới.
            }

            if (!response.ok) {
                const message =
                    jsonData?.message ||
                    jsonData?.title ||
                    jsonData?.error ||
                    normalizePlainText(
                        textData
                    ) ||
                    `Yêu cầu thất bại (${response.status})`;

                throw new Error(
                    message
                );
            }

            return (
                jsonData ??
                textData ??
                null
            );
        }

        // =========================================================
        // HISTORY FILTER / URL
        //
        // LƯU Ý:
        // Giữ nguyên date -> UTC semantics
        // giống implementation hiện tại.
        // =========================================================

        function buildHistoryUrl() {
            const params =
                new URLSearchParams();

            if (
                filterStatus.value !== ''
            ) {
                params.append(
                    'status',
                    filterStatus.value
                );
            }

            if (filterFrom.value) {
                params.append(
                    'fromUtc',
                    new Date(
                        filterFrom.value
                    ).toISOString()
                );
            }

            if (filterTo.value) {
                const toDate =
                    new Date(
                        filterTo.value
                    );

                toDate.setDate(
                    toDate.getDate() + 1
                );

                params.append(
                    'toUtcExclusive',
                    toDate.toISOString()
                );
            }

            params.append(
                'page',
                String(
                    pageState.page
                )
            );

            params.append(
                'pageSize',
                String(
                    pageState.pageSize
                )
            );

            return (
                `/admin/pos/shift/history?` +
                params.toString()
            );
        }

        // =========================================================
        // SHIFT STATUS
        // =========================================================

        function getShiftStatusMeta(
            status
        ) {
            const raw =
                String(
                    status ?? ''
                )
                    .trim()
                    .toLowerCase();

            if (
                raw === '1' ||
                raw.includes('open')
            ) {
                return {
                    text: 'Đang mở',
                    css: 'open'
                };
            }

            if (
                raw === '2' ||
                raw.includes('closed')
            ) {
                return {
                    text: 'Đã đóng',
                    css: 'closed'
                };
            }

            return {
                text:
                    String(
                        status || '-'
                    ),
                css: 'closed'
            };
        }

        function getStatusBadge(status) {
            const meta =
                getShiftStatusMeta(
                    status
                );

            return `
                <span class="status-badge ${meta.css}">
                    ${escapeHtml(meta.text)}
                </span>`;
        }

        // =========================================================
        // ORDER STATUS
        // =========================================================

        function getOrderStatusMeta(
            status
        ) {
            const raw =
                String(
                    status ?? ''
                )
                    .trim()
                    .toLowerCase();

            if (
                raw === '0' ||
                raw.includes('draft')
            ) {
                return {
                    text: 'Nháp',
                    css: 'draft'
                };
            }

            if (
                raw === '1' ||
                raw.includes('onhold')
            ) {
                return {
                    text: 'Đang giữ',
                    css: 'onhold'
                };
            }

            if (
                raw === '2' ||
                raw.includes('completed')
            ) {
                return {
                    text: 'Hoàn tất',
                    css: 'completed'
                };
            }

            if (
                raw === '3' ||
                raw.includes('cancelled')
            ) {
                return {
                    text: 'Đã hủy',
                    css: 'cancelled'
                };
            }

            if (
                raw === '4' ||
                raw.includes('voided')
            ) {
                return {
                    text: 'Void',
                    css: 'voided'
                };
            }

            if (
                raw === '5' ||
                raw.includes('refunded')
            ) {
                return {
                    text: 'Hoàn tiền',
                    css: 'refunded'
                };
            }

            return {
                text:
                    String(
                        status || '-'
                    ),
                css: 'draft'
            };
        }

        function getOrderStatusBadge(
            status
        ) {
            const meta =
                getOrderStatusMeta(
                    status
                );

            return `
                <span class="order-status-badge ${meta.css}">
                    ${escapeHtml(meta.text)}
                </span>`;
        }

        // =========================================================
        // PAYMENT STATUS
        // Current business states observed in POS:
        // Unpaid / PartiallyPaid / Paid / Refunded.
        // Numeric compatibility is retained for enum JSON payloads.
        // =========================================================

        function getPaymentStatusMeta(
            status
        ) {
            const raw =
                String(
                    status ?? ''
                )
                    .trim()
                    .toLowerCase();

            if (
                raw === '0' ||
                raw.includes('unpaid')
            ) {
                return {
                    text: 'Chưa thanh toán',
                    css: 'unpaid'
                };
            }

            if (
                raw === '1' ||
                raw.includes('partiallypaid') ||
                raw.includes('partially_paid') ||
                raw.includes('partial')
            ) {
                return {
                    text: 'Thanh toán một phần',
                    css: 'partial'
                };
            }

            if (
                raw === '2' ||
                raw === 'paid' ||
                raw.endsWith('.paid')
            ) {
                return {
                    text: 'Đã thanh toán',
                    css: 'paid'
                };
            }

            if (
                raw === '3' ||
                raw.includes('refunded')
            ) {
                return {
                    text: 'Đã hoàn tiền',
                    css: 'refunded'
                };
            }

            return {
                text: String(status || '-'),
                css: 'unknown'
            };
        }

        function getPaymentStatusBadge(
            status
        ) {
            const meta =
                getPaymentStatusMeta(
                    status
                );

            return `
                <span class="payment-status-badge ${meta.css}">
                    ${escapeHtml(meta.text)}
                </span>`;
        }

        function formatCompactDateTimeCell(
            value
        ) {
            if (!value) {
                return '<span class="compact-time-empty">-</span>';
            }

            const date =
                new Date(value);

            if (
                Number.isNaN(
                    date.getTime()
                )
            ) {
                return '<span class="compact-time-empty">-</span>';
            }

            const dateText =
                date.toLocaleDateString(
                    'vi-VN'
                );

            const timeText =
                date.toLocaleTimeString(
                    'vi-VN',
                    {
                        hour: '2-digit',
                        minute: '2-digit',
                        second: '2-digit'
                    }
                );

            return `
                <div class="compact-time-cell">
                    <div class="compact-time-date">${dateText}</div>
                    <div class="compact-time-clock">${timeText}</div>
                </div>`;
        }

        // =========================================================
        // CASH TRANSACTION TYPE
        // =========================================================

        function getCashTxnTypeMeta(
            type
        ) {
            const raw =
                String(
                    type ?? ''
                )
                    .trim()
                    .toLowerCase();

            if (
                raw.includes('cashin') ||
                raw === '1' ||
                raw === 'in'
            ) {
                return {
                    text: 'Thu tiền mặt',
                    css: 'in',
                    amountCss: 'in'
                };
            }

            if (
                raw.includes('cashout') ||
                raw === '2' ||
                raw === 'out'
            ) {
                return {
                    text: 'Chi tiền mặt',
                    css: 'out',
                    amountCss: 'out'
                };
            }

            return {
                text: String(type || '-'),
                css: '',
                amountCss: 'unknown'
            };
        }

        function getCashTxnTypeBadge(
            type
        ) {
            const meta =
                getCashTxnTypeMeta(
                    type
                );

            const cssClass =
                meta.css
                    ? ` ${meta.css}`
                    : '';

            return `
                <span class="cash-badge${cssClass}">
                    ${escapeHtml(meta.text)}
                </span>`;
        }

        // =========================================================
        // GENERIC ARRAY PAGING
        // =========================================================

        function paginateItems(
            items,
            page,
            pageSize
        ) {
            const safeItems =
                asArray(items);

            const safePageSize =
                Math.max(
                    1,
                    toPositiveInt(
                        pageSize,
                        8
                    )
                );

            const total =
                safeItems.length;

            const totalPages =
                Math.max(
                    1,
                    Math.ceil(
                        total /
                        safePageSize
                    )
                );

            const currentPage =
                Math.min(
                    Math.max(
                        1,
                        toPositiveInt(
                            page,
                            1
                        )
                    ),
                    totalPages
                );

            const start =
                (
                    currentPage - 1
                ) *
                safePageSize;

            const pageItems =
                safeItems.slice(
                    start,
                    start +
                    safePageSize
                );

            return {
                total,
                totalPages,
                currentPage,
                start,
                pageItems
            };
        }

        // =========================================================
        // MAIN PAGING STATE
        // =========================================================

        function updateMainPager() {
            btnPrevPage.disabled =
                isHistoryLoading ||
                pageState.page <= 1;

            btnNextPage.disabled =
                isHistoryLoading ||
                pageState.page >=
                pageState.totalPages;
        }

        // =========================================================
        // HISTORY EMPTY RENDER
        // =========================================================

        function renderHistoryEmpty(
            message =
                'Không có dữ liệu.'
        ) {
            historyBody.innerHTML = `
                <tr>
                    <td
                        colspan="6"
                        class="text-center text-muted py-4">
                        ${escapeHtml(message)}
                    </td>
                </tr>`;
        }

        // =========================================================
        // HISTORY RENDER
        // =========================================================

        function renderHistory(items) {
            const safeItems =
                asArray(items);

            pageState.currentItems =
                safeItems;

            if (!safeItems.length) {
                renderHistoryEmpty();
                return;
            }

            const rows =
                safeItems
                    .map(item => {

                        const id =
                            toPositiveInt(
                                item?.id,
                                0
                            );

                        if (!id) {
                            return '';
                        }

                        const isActive =
                            pageState
                                .selectedShiftId ===
                            id;

                        const rowClass =
                            isActive
                                ? 'history-row active'
                                : 'history-row';

                        return `
                            <tr
                                class="${rowClass}"
                                data-shift-id="${id}"
                                role="button"
                                tabindex="0"
                                aria-selected="${isActive ? 'true' : 'false'}">

                                <td>
                                    <div class="history-main">
                                        #${id}
                                    </div>

                                    <div class="history-sub">
                                        ${escapeHtml(
                            item?.shiftCode ||
                            '-'
                        )}
                                    </div>
                                </td>

                                <td>
                                    ${getStatusBadge(
                            item?.status
                        )}
                                </td>

                                <td>
                                    <div class="history-main">
                                        ${formatDateTime(
                            item?.openedAtUtc
                        )}
                                    </div>
                                </td>

                                <td>
                                    <div class="history-main">
                                        ${formatDateTime(
                            item?.closedAtUtc
                        )}
                                    </div>
                                </td>

                                <td>
                                    <div class="history-money primary">
                                        ${formatMoney(
                            item?.openingCash
                        )}
                                    </div>
                                </td>

                                <td>
                                    <div class="history-money success">
                                        ${formatMoney(
                            item?.cashSalesTotal
                        )}
                                    </div>
                                </td>

                            </tr>`;
                    })
                    .filter(Boolean);

            if (!rows.length) {
                renderHistoryEmpty();
                return;
            }

            historyBody.innerHTML =
                rows.join('');
        }

        // =========================================================
        // HISTORY LOADING STATE
        // =========================================================
        const searchButtonDefaultHtml =
            btnSearch.innerHTML;
        function setHistoryLoading(
            isLoading
        ) {

            isHistoryLoading =
                isLoading;
            if (isLoading) {
                btnSearch.innerHTML = `
        <span
            class="spinner-border spinner-border-sm me-2"
            aria-hidden="true">
        </span>
        Đang tìm...
    `;
            }
            else {
                btnSearch.innerHTML =
                    searchButtonDefaultHtml;
            }

            filterStatus.disabled =
                isLoading;

            filterFrom.disabled =
                isLoading;

            filterTo.disabled =
                isLoading;

            btnSearch.disabled =
                isLoading;

            btnToday.disabled =
                isLoading;

            btnLast7Days.disabled =
                isLoading;

            btnLast30Days.disabled =
                isLoading;

            btnClearDate.disabled =
                isLoading;

            pageSizeSelect.disabled =
                isLoading;

            const historyTable =
                historyBody.closest(
                    'table'
                );

            historyTable?.setAttribute(
                'aria-busy',
                isLoading
                    ? 'true'
                    : 'false'
            );

            if (isLoading) {
                btnPrevPage.disabled =
                    true;

                btnNextPage.disabled =
                    true;

                renderHistoryEmpty(
                    HISTORY_LOADING_TEXT
                );

                historyPagingInfo.textContent =
                    HISTORY_LOADING_TEXT;

                return;
            }

            updateMainPager();
        }

        // =========================================================
        // LOAD HISTORY
        // =========================================================

        async function loadHistory() {

            const requestId =
                ++historyRequestSeq;

            // Hủy request cũ nếu user thao tác nhanh.
            historyAbortController
                ?.abort();

            historyAbortController =
                new AbortController();

            setHistoryLoading(true);

            try {

                const result =
                    await fetchJson(
                        buildHistoryUrl(),
                        {
                            signal:
                                historyAbortController
                                    .signal
                        }
                    );

                // Request cũ không còn authority.
                if (
                    requestId !==
                    historyRequestSeq
                ) {
                    return;
                }

                if (
                    !result ||
                    typeof result !==
                    'object'
                ) {
                    throw new Error(
                        'Dữ liệu lịch sử ca không hợp lệ.'
                    );
                }

                const resultPage =
                    toPositiveInt(
                        result.page,
                        pageState.page
                    );

                const totalPages =
                    toPositiveInt(
                        result.totalPages,
                        1
                    );

                const totalItems =
                    toNonNegativeInt(
                        result.totalItems,
                        0
                    );

                pageState.page =
                    Math.max(
                        1,
                        resultPage
                    );

                pageState.totalPages =
                    Math.max(
                        1,
                        totalPages
                    );

                renderHistory(
                    asArray(
                        result.items
                    )
                );

                historyPagingInfo.textContent =
                    `Trang ${pageState.page}/${pageState.totalPages}` +
                    ` - Tổng ${totalItems} ca`;
            }
            catch (error) {

                if (
                    isAbortError(error) ||
                    requestId !==
                    historyRequestSeq
                ) {
                    return;
                }

                pageState.page = 1;
                pageState.totalPages = 1;
                pageState.currentItems = [];

                renderHistoryEmpty();

                historyPagingInfo.textContent =
                    HISTORY_ERROR_TEXT;

                showError(
                    error?.message
                );
            }
            finally {

                if (
                    requestId ===
                    historyRequestSeq
                ) {
                    setHistoryLoading(
                        false
                    );
                }
            }
        }

        // =========================================================
        // SUMMARY HEADER
        // =========================================================

        function renderSummaryHeader(
            shiftRow
        ) {
            if (!shiftRow) {
                selectedShiftCode.textContent =
                    '-';

                selectedShiftStatus.innerHTML =
                    '';

                selectedOpenedAt.textContent =
                    '-';

                selectedClosedAt.textContent =
                    '-';

                return;
            }

            const id =
                toPositiveInt(
                    shiftRow.id,
                    0
                );

            selectedShiftCode.textContent =
                shiftRow.shiftCode ||
                (
                    id
                        ? `Ca #${id}`
                        : '-'
                );

            selectedShiftStatus.innerHTML =
                getStatusBadge(
                    shiftRow.status
                );

            selectedOpenedAt.textContent =
                formatDateTime(
                    shiftRow.openedAtUtc
                );

            selectedClosedAt.textContent =
                formatDateTime(
                    shiftRow.closedAtUtc
                );
        }

        // =========================================================
        // FINANCIAL SUMMARY RENDER
        // =========================================================

        function renderFinancialSummary(summary) {
            sumCashSalesTotal.textContent =
                formatMoney(
                    summary.cashSalesTotal
                );

            sumNonCashSalesTotal.textContent =
                formatMoney(
                    summary.nonCashSalesTotal
                );

            sumCashRefundTotal.textContent =
                formatMoney(
                    summary.cashRefundTotal
                );

            sumNonCashRefundTotal.textContent =
                formatMoney(
                    summary.nonCashRefundTotal
                );

            sumRefundTotal.textContent =
                formatMoney(
                    summary.refundTotal
                );

            sumOpeningCash.textContent =
                formatMoney(
                    summary.openingCash
                );

            sumCashInTotal.textContent =
                formatMoney(
                    summary.cashInTotal
                );

            sumCashOutTotal.textContent =
                formatMoney(
                    summary.cashOutTotal
                );

            sumClosingCashExpected.textContent =
                formatMoney(
                    summary.closingCashExpected
                );

            const hasActualClosingCash =
                hasValue(
                    summary.closingCashActual
                );

            sumClosingCashActual.textContent =
                formatOptionalMoney(
                    summary.closingCashActual
                );

            sumClosingCashActualHint.textContent =
                hasActualClosingCash
                    ? 'Đã ghi nhận tiền cuối ca thực tế.'
                    : 'Chưa ghi nhận tiền cuối ca thực tế.';

            sumClosingCashActualHint.classList.toggle(
                'has-value',
                hasActualClosingCash
            );

            sumOpenNote.textContent =
                textOrDash(
                    summary.openNote
                );

            sumCloseNote.textContent =
                textOrDash(
                    summary.closeNote
                );
        }

        // =========================================================
        // SUMMARY RENDER
        // =========================================================

        function renderSummary(summary) {

            if (
                !summary ||
                typeof summary !== 'object'
            ) {
                summaryEmpty.textContent =
                    SUMMARY_EMPTY_TEXT;

                summaryEmpty.style.display =
                    '';

                summaryContent.style.display =
                    'none';

                renderSummaryHeader(
                    null
                );

                pageState.selectedSummary =
                    null;

                ordersModalState.items =
                    [];

                ordersModalState.page =
                    1;

                cashTxnsModalState.items =
                    [];

                cashTxnsModalState.page =
                    1;

                ordersCountText.textContent =
                    '0';

                cashTxnsCountText.textContent =
                    '0';

                btnOpenOrdersModal.disabled =
                    true;

                btnOpenCashTxnsModal.disabled =
                    true;

                btnPrintShiftReport.disabled =
                    true;

                btnExportShiftReport.disabled =
                    true;

                return;
            }

            btnOpenOrdersModal.disabled =
                isSummaryLoading;

            btnOpenCashTxnsModal.disabled =
                isSummaryLoading;

            pageState.selectedSummary =
                summary;

            btnPrintShiftReport.disabled =
                isSummaryLoading ||
                !pageState.selectedShiftId;

            btnExportShiftReport.disabled =
                isSummaryLoading ||
                !pageState.selectedShiftId;

            summaryEmpty.style.display =
                'none';

            summaryContent.style.display =
                '';

            renderSummaryHeader(
                pageState
                    .selectedShiftRow
            );

            sumTotalOrders.textContent =
                String(
                    toNonNegativeInt(
                        summary.totalOrders,
                        0
                    )
                );

            sumCompletedOrders.textContent =
                String(
                    toNonNegativeInt(
                        summary.completedOrders,
                        0
                    )
                );

            sumDraftOrders.textContent =
                String(
                    toNonNegativeInt(
                        summary.draftOrders,
                        0
                    )
                );

            sumCancelledOrders.textContent =
                String(
                    toNonNegativeInt(
                        summary.cancelledOrders,
                        0
                    )
                );

            sumCompletedSalesTotal.textContent =
                formatMoney(
                    summary
                        .completedSalesTotal
                );

            renderFinancialSummary(
                summary
            );

            const orders =
                asArray(
                    summary.orders
                );

            const transactions =
                asArray(
                    summary
                        .cashTransactions
                );

            ordersModalState.items =
                orders;

            ordersModalState.page =
                1;

            cashTxnsModalState.items =
                transactions;

            cashTxnsModalState.page =
                1;

            ordersCountText.textContent =
                String(
                    orders.length
                );

            cashTxnsCountText.textContent =
                String(
                    transactions.length
                );
        }

        // =========================================================
        // SUMMARY LOADING
        // =========================================================

        function setSummaryLoading(
            isLoading
        ) {
            isSummaryLoading =
                isLoading;

            summaryContent.setAttribute(
                'aria-busy',
                isLoading
                    ? 'true'
                    : 'false'
            );

            btnOpenOrdersModal.disabled =
                isLoading ||
                !pageState.selectedShiftId;

            btnOpenCashTxnsModal.disabled =
                isLoading ||
                !pageState.selectedShiftId;

            btnPrintShiftReport.disabled =
                isLoading ||
                !pageState.selectedShiftId ||
                !pageState.selectedSummary;

            btnExportShiftReport.disabled =
                isLoading ||
                !pageState.selectedShiftId ||
                !pageState.selectedSummary;

            if (isLoading) {
                summaryEmpty.textContent =
                    SUMMARY_LOADING_TEXT;

                summaryEmpty.style.display =
                    '';

                summaryContent.style.display =
                    'none';
            }
        }

        // =========================================================
        // CLEAR SELECTED SHIFT
        // =========================================================

        function clearSelectionAndSummary() {

            pageState.selectedShiftId =
                null;

            pageState.selectedShiftRow =
                null;

            pageState.selectedSummary =
                null;

            // Vô hiệu hóa response summary cũ.
            summaryRequestSeq++;

            summaryAbortController
                ?.abort();

            summaryAbortController =
                null;

            setSummaryLoading(
                false
            );

            renderSummary(
                null
            );
        }

        // =========================================================
        // LOAD SUMMARY
        // =========================================================

        async function loadSummary(
            shiftId
        ) {

            const safeShiftId =
                toPositiveInt(
                    shiftId,
                    0
                );

            if (!safeShiftId) {
                renderSummary(null);
                return;
            }

            const requestId =
                ++summaryRequestSeq;

            // Hủy request summary cũ.
            summaryAbortController
                ?.abort();

            summaryAbortController =
                new AbortController();

            setSummaryLoading(true);

            try {

                const summary =
                    await fetchJson(
                        `/admin/pos/shift/summary?shiftId=${safeShiftId}`,
                        {
                            signal:
                                summaryAbortController
                                    .signal
                        }
                    );

                // Response cũ không được render.
                if (
                    requestId !==
                    summaryRequestSeq ||
                    pageState
                        .selectedShiftId !==
                    safeShiftId
                ) {
                    return;
                }

                if (
                    !summary ||
                    typeof summary !==
                    'object'
                ) {
                    throw new Error(
                        'Dữ liệu tổng quan ca không hợp lệ.'
                    );
                }
                setSummaryLoading(false);
                renderSummary(
                    summary
                );
            }
            catch (error) {

                if (
                    isAbortError(error) ||
                    requestId !==
                    summaryRequestSeq
                ) {
                    return;
                }
                setSummaryLoading(false);
                renderSummary(
                    null
                );

                summaryEmpty.textContent =
                    SUMMARY_ERROR_TEXT;

                showError(
                    error?.message
                );
            }
        }

        // =========================================================
        // SELECT SHIFT
        // =========================================================

        async function selectShiftByRow(
            row
        ) {
            if (!row) {
                return;
            }

            const shiftId =
                toPositiveInt(
                    row.getAttribute(
                        'data-shift-id'
                    ),
                    0
                );

            if (!shiftId) {
                return;
            }

            const shiftRow =
                pageState
                    .currentItems
                    .find(
                        item =>
                            toPositiveInt(
                                item?.id,
                                0
                            ) ===
                            shiftId
                    ) ||
                null;

            if (!shiftRow) {
                return;
            }

            pageState.selectedShiftId =
                shiftId;

            pageState.selectedShiftRow =
                shiftRow;
            const wasKeyboardFocused =
                document.activeElement === row;
            // Render selected-state ngay,
            // không chờ API summary.
            renderHistory(
                pageState.currentItems
            );
            if (wasKeyboardFocused) {
                const selectedRow =
                    historyBody.querySelector(
                        `[data-shift-id="${shiftId}"]`
                    );

                selectedRow?.focus();
            }
            await loadSummary(
                shiftId
            );
        }

        // =========================================================
        // ORDERS MODAL RENDER
        // =========================================================

        function renderOrdersModalPage() {

            const {
                total,
                totalPages,
                currentPage,
                start,
                pageItems
            } =
                paginateItems(
                    ordersModalState.items,
                    ordersModalState.page,
                    ordersModalState.pageSize
                );

            ordersModalState.page =
                currentPage;

            if (!pageItems.length) {

                ordersModalBody.innerHTML = `
                    <tr>
                        <td
                            colspan="7"
                            class="summary-muted-empty">
                            Không có dữ liệu.
                        </td>
                    </tr>`;
            }
            else {

                ordersModalBody.innerHTML =
                    pageItems
                        .map(
                            item => {

                                const id =
                                    toPositiveInt(
                                        item?.id,
                                        0
                                    );

                                return `
                                    <tr>

                                        <td>
                                            ${id || '-'}
                                        </td>

                                        <td>
                                            <div class="history-main order-number-cell">
                                                ${escapeHtml(
                                    item?.orderNumber ||
                                    '-'
                                )}
                                            </div>
                                        </td>

                                        <td>
                                            ${getOrderStatusBadge(
                                    item?.status
                                )}
                                        </td>

                                        <td>
                                            ${getPaymentStatusBadge(
                                    item?.paymentStatus
                                )}
                                        </td>

                                        <td>
                                            ${formatCompactDateTimeCell(
                                    item?.createdAtUtc
                                )}
                                        </td>

                                        <td>
                                            ${formatCompactDateTimeCell(
                                    item?.completedAtUtc
                                )}
                                        </td>

                                        <td class="text-end">
                                            <div class="order-total-cell">
                                                ${formatMoney(
                                    item?.grandTotal
                                )}
                                            </div>
                                        </td>

                                    </tr>`;
                            }
                        )
                        .join('');
            }

            if (total === 0) {

                ordersModalPagingInfo.textContent =
                    '0 đơn';
            }
            else {

                const from =
                    start + 1;

                const to =
                    Math.min(
                        start +
                        ordersModalState
                            .pageSize,
                        total
                    );

                ordersModalPagingInfo.textContent =
                    `${from}-${to} / ${total} đơn`;
            }

            btnOrdersModalPrev.disabled =
                currentPage <= 1;

            btnOrdersModalNext.disabled =
                currentPage >=
                totalPages;
        }

        // =========================================================
        // CASH TRANSACTION MODAL RENDER
        // =========================================================

        function renderCashTxnsModalPage() {

            const {
                total,
                totalPages,
                currentPage,
                start,
                pageItems
            } =
                paginateItems(
                    cashTxnsModalState.items,
                    cashTxnsModalState.page,
                    cashTxnsModalState.pageSize
                );

            cashTxnsModalState.page =
                currentPage;

            if (!pageItems.length) {

                cashTxnsModalBody.innerHTML = `
                    <tr>
                        <td
                            colspan="5"
                            class="summary-muted-empty">
                            Không có dữ liệu.
                        </td>
                    </tr>`;
            }
            else {

                cashTxnsModalBody.innerHTML =
                    pageItems
                        .map(
                            item => {
                                const typeMeta =
                                    getCashTxnTypeMeta(
                                        item?.type
                                    );

                                return `
                                    <tr>

                                        <td>
                                            ${formatCompactDateTimeCell(
                                    item?.createdAtUtc
                                )}
                                        </td>

                                        <td>
                                            ${getCashTxnTypeBadge(
                                    item?.type
                                )}
                                        </td>

                                        <td class="text-end">
                                            <div class="cash-txn-amount ${typeMeta.amountCss}">
                                                ${formatMoney(
                                    item?.amount
                                )}
                                            </div>
                                        </td>

                                        <td>
                                            <div class="cash-txn-reason">
                                                ${escapeHtml(
                                    item?.reason ||
                                    '-'
                                )}
                                            </div>
                                        </td>

                                        <td>
                                            <div class="cash-txn-note">
                                                ${escapeHtml(
                                    item?.note ||
                                    '-'
                                )}
                                            </div>
                                        </td>

                                    </tr>`;
                            }
                        )
                        .join('');
            }

            if (total === 0) {

                cashTxnsModalPagingInfo.textContent =
                    '0 giao dịch';
            }
            else {

                const from =
                    start + 1;

                const to =
                    Math.min(
                        start +
                        cashTxnsModalState
                            .pageSize,
                        total
                    );

                cashTxnsModalPagingInfo.textContent =
                    `${from}-${to} / ${total} giao dịch`;
            }

            btnCashTxnsModalPrev.disabled =
                currentPage <= 1;

            btnCashTxnsModalNext.disabled =
                currentPage >=
                totalPages;
        }

        // =========================================================
        // PHASE 4D - SHIFT REPORT / PRINT / EXPORT
        // =========================================================

        function sanitizeReportFilePart(value) {
            const cleaned =
                String(value ?? '')
                    .trim()
                    .replace(/[\\/:*?"<>|]+/g, '-')
                    .replace(/\s+/g, '-')
                    .replace(/-+/g, '-')
                    .replace(/^-|-$/g, '');

            return cleaned || 'shift';
        }

        function reportMoney(value) {
            return `${formatMoney(value)} đ`;
        }

        function reportOptionalMoney(value) {
            const formatted =
                formatOptionalMoney(value);

            return formatted === '-'
                ? '-'
                : `${formatted} đ`;
        }

        function buildPrintOrderRows(orders) {
            const safeOrders =
                asArray(orders);

            if (!safeOrders.length) {
                return `
                    <tr>
                        <td colspan="7" class="empty">
                            Không có đơn trong ca.
                        </td>
                    </tr>`;
            }

            return safeOrders
                .map(item => {
                    const status =
                        getOrderStatusMeta(
                            item?.status
                        ).text;

                    const paymentStatus =
                        getPaymentStatusMeta(
                            item?.paymentStatus
                        ).text;

                    return `
                        <tr>
                            <td>${toPositiveInt(item?.id, 0) || '-'}</td>
                            <td>${escapeHtml(item?.orderNumber || '-')}</td>
                            <td>${escapeHtml(status)}</td>
                            <td>${escapeHtml(paymentStatus)}</td>
                            <td>${escapeHtml(formatDateTime(item?.createdAtUtc))}</td>
                            <td>${escapeHtml(formatDateTime(item?.completedAtUtc))}</td>
                            <td class="num">${escapeHtml(reportMoney(item?.grandTotal))}</td>
                        </tr>`;
                })
                .join('');
        }

        function buildPrintCashRows(transactions) {
            const safeTransactions =
                asArray(transactions);

            if (!safeTransactions.length) {
                return `
                    <tr>
                        <td colspan="5" class="empty">
                            Không có giao dịch thu / chi.
                        </td>
                    </tr>`;
            }

            return safeTransactions
                .map(item => {
                    const typeText =
                        getCashTxnTypeMeta(
                            item?.type
                        ).text;

                    return `
                        <tr>
                            <td>${escapeHtml(formatDateTime(item?.createdAtUtc))}</td>
                            <td>${escapeHtml(typeText)}</td>
                            <td class="num">${escapeHtml(reportMoney(item?.amount))}</td>
                            <td>${escapeHtml(item?.reason || '-')}</td>
                            <td>${escapeHtml(item?.note || '-')}</td>
                        </tr>`;
                })
                .join('');
        }

        function buildShiftReportPrintHtml(summary) {
            const shiftCode =
                summary?.shiftCode ||
                pageState.selectedShiftRow?.shiftCode ||
                (
                    pageState.selectedShiftId
                        ? `SHIFT-${pageState.selectedShiftId}`
                        : '-'
                );

            const shiftStatus =
                getShiftStatusMeta(
                    summary?.status ??
                    pageState.selectedShiftRow?.status
                ).text;

            const orders =
                asArray(
                    summary?.orders
                );

            const transactions =
                asArray(
                    summary?.cashTransactions
                );

            const openNote =
                textOrDash(
                    summary?.openNote
                );

            const closeNote =
                textOrDash(
                    summary?.closeNote
                );

            const printedAt =
                new Date()
                    .toLocaleString(
                        'vi-VN'
                    );

            return `<!doctype html>
<html lang="vi">
<head>
<meta charset="utf-8">
<title>Báo cáo ca ${escapeHtml(shiftCode)}</title>
<style>
    @page { size: A4 portrait; margin: 12mm; }
    * { box-sizing: border-box; }
    body {
        margin: 0;
        font-family: Arial, Helvetica, sans-serif;
        color: #111827;
        font-size: 11px;
        line-height: 1.4;
        background: #fff;
    }
    h1 { margin: 0; font-size: 20px; }
    h2 {
        margin: 18px 0 8px;
        padding-bottom: 5px;
        border-bottom: 1px solid #d1d5db;
        font-size: 13px;
        text-transform: uppercase;
    }
    .muted { color: #6b7280; }
    .header {
        display: flex;
        justify-content: space-between;
        gap: 20px;
        padding-bottom: 12px;
        border-bottom: 2px solid #111827;
    }
    .header-right { text-align: right; }
    .meta-grid, .kpi-grid, .money-grid {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 6px 18px;
        margin-top: 10px;
    }
    .row {
        display: flex;
        justify-content: space-between;
        gap: 12px;
        padding: 4px 0;
        border-bottom: 1px dotted #e5e7eb;
    }
    .row strong {
        text-align: right;
        font-variant-numeric: tabular-nums;
    }
    .note {
        margin-top: 6px;
        padding: 8px;
        border: 1px solid #e5e7eb;
        border-radius: 6px;
        white-space: pre-wrap;
        overflow-wrap: anywhere;
    }
    table {
        width: 100%;
        border-collapse: collapse;
        margin-top: 6px;
    }
    th, td {
        border: 1px solid #d1d5db;
        padding: 5px 6px;
        vertical-align: top;
    }
    th {
        background: #f3f4f6;
        text-align: left;
        font-size: 10px;
    }
    .num {
        text-align: right;
        white-space: nowrap;
        font-variant-numeric: tabular-nums;
    }
    .empty {
        text-align: center;
        color: #6b7280;
        padding: 10px;
    }
    .footer {
        margin-top: 16px;
        padding-top: 8px;
        border-top: 1px solid #d1d5db;
        color: #6b7280;
        font-size: 9px;
    }
    @media print {
        .no-print { display: none !important; }
        thead { display: table-header-group; }
        tr, td, th { break-inside: avoid; }
    }
</style>
</head>
<body>
    <div class="header">
        <div>
            <h1>BÁO CÁO CA POS</h1>
            <div class="muted">Mã ca: ${escapeHtml(shiftCode)}</div>
        </div>
        <div class="header-right">
            <div><strong>${escapeHtml(shiftStatus)}</strong></div>
            <div class="muted">In lúc: ${escapeHtml(printedAt)}</div>
        </div>
    </div>

    <h2>Thông tin ca</h2>
    <div class="meta-grid">
        <div class="row">
            <span>Shift ID</span>
            <strong>${toPositiveInt(summary?.id, pageState.selectedShiftId || 0) || '-'}</strong>
        </div>
        <div class="row">
            <span>Trạng thái</span>
            <strong>${escapeHtml(shiftStatus)}</strong>
        </div>
        <div class="row">
            <span>Mở lúc</span>
            <strong>${escapeHtml(formatDateTime(summary?.openedAtUtc))}</strong>
        </div>
        <div class="row">
            <span>Đóng lúc</span>
            <strong>${escapeHtml(formatDateTime(summary?.closedAtUtc))}</strong>
        </div>
    </div>

    <h2>Tổng quan đơn hàng</h2>
    <div class="kpi-grid">
        <div class="row"><span>Tổng đơn</span><strong>${toNonNegativeInt(summary?.totalOrders, 0)}</strong></div>
        <div class="row"><span>Hoàn tất</span><strong>${toNonNegativeInt(summary?.completedOrders, 0)}</strong></div>
        <div class="row"><span>Nháp</span><strong>${toNonNegativeInt(summary?.draftOrders, 0)}</strong></div>
        <div class="row"><span>Đã hủy</span><strong>${toNonNegativeInt(summary?.cancelledOrders, 0)}</strong></div>
        <div class="row"><span>Refund count</span><strong>${toNonNegativeInt(summary?.refundCount, 0)}</strong></div>
        <div class="row"><span>Void count</span><strong>${toNonNegativeInt(summary?.voidCount, 0)}</strong></div>
        <div class="row"><span>Tổng bán hoàn tất</span><strong>${escapeHtml(reportMoney(summary?.completedSalesTotal))}</strong></div>
    </div>

    <h2>Tài chính ca</h2>
    <div class="money-grid">
        <div class="row"><span>Tiền đầu ca</span><strong>${escapeHtml(reportMoney(summary?.openingCash))}</strong></div>
        <div class="row"><span>Doanh thu tiền mặt</span><strong>${escapeHtml(reportMoney(summary?.cashSalesTotal))}</strong></div>
        <div class="row"><span>Doanh thu không tiền mặt</span><strong>${escapeHtml(reportMoney(summary?.nonCashSalesTotal))}</strong></div>
        <div class="row"><span>Thu thêm</span><strong>${escapeHtml(reportMoney(summary?.cashInTotal))}</strong></div>
        <div class="row"><span>Chi ra</span><strong>${escapeHtml(reportMoney(summary?.cashOutTotal))}</strong></div>
        <div class="row"><span>Hoàn tiền mặt</span><strong>${escapeHtml(reportMoney(summary?.cashRefundTotal))}</strong></div>
        <div class="row"><span>Hoàn không tiền mặt</span><strong>${escapeHtml(reportMoney(summary?.nonCashRefundTotal))}</strong></div>
        <div class="row"><span>Tổng hoàn tiền</span><strong>${escapeHtml(reportMoney(summary?.refundTotal))}</strong></div>
        <div class="row"><span>Tiền cuối ca dự kiến</span><strong>${escapeHtml(reportMoney(summary?.closingCashExpected))}</strong></div>
        <div class="row"><span>Tiền cuối ca thực tế</span><strong>${escapeHtml(reportOptionalMoney(summary?.closingCashActual))}</strong></div>
    </div>

    <h2>Ghi chú ca</h2>
    <div><strong>Ghi chú mở ca</strong></div>
    <div class="note">${escapeHtml(openNote)}</div>
    <div style="margin-top:8px;"><strong>Ghi chú đóng ca</strong></div>
    <div class="note">${escapeHtml(closeNote)}</div>

    <h2>Danh sách đơn (${orders.length})</h2>
    <table>
        <thead>
            <tr>
                <th>ID</th>
                <th>Mã đơn</th>
                <th>Trạng thái</th>
                <th>Thanh toán</th>
                <th>Tạo lúc</th>
                <th>Hoàn tất lúc</th>
                <th class="num">Tổng tiền</th>
            </tr>
        </thead>
        <tbody>
            ${buildPrintOrderRows(orders)}
        </tbody>
    </table>

    <h2>Thu / chi tiền mặt (${transactions.length})</h2>
    <table>
        <thead>
            <tr>
                <th>Thời gian</th>
                <th>Loại</th>
                <th class="num">Số tiền</th>
                <th>Lý do</th>
                <th>Ghi chú</th>
            </tr>
        </thead>
        <tbody>
            ${buildPrintCashRows(transactions)}
        </tbody>
    </table>

    <div class="footer">
        Báo cáo được tạo từ dữ liệu tổng quan ca mà tài khoản hiện tại đã được phép xem.
    </div>
</body>
</html>`;
        }

        function printSelectedShiftReport() {
            const summary =
                pageState.selectedSummary;

            if (
                !pageState.selectedShiftId ||
                !summary ||
                isSummaryLoading
            ) {
                showError(
                    'Vui lòng chọn ca và chờ tải xong tổng quan trước khi in.'
                );
                return;
            }

            const printWindow =
                window.open(
                    '',
                    '_blank',
                    'width=1100,height=800'
                );

            if (!printWindow) {
                showError(
                    'Trình duyệt đang chặn cửa sổ in. Vui lòng cho phép popup rồi thử lại.'
                );
                return;
            }

            try {
                printWindow.opener = null;
                printWindow.document.open();
                printWindow.document.write(
                    buildShiftReportPrintHtml(
                        summary
                    )
                );
                printWindow.document.close();
                GaoPrintLifecycle.autoClose(printWindow);

                window.setTimeout(
                    () => {
                        printWindow.focus();
                        printWindow.print();
                    },
                    250
                );
            }
            catch (error) {
                try {
                    printWindow.close();
                }
                catch {
                    // Ignore close failure.
                }

                showError(
                    error?.message ||
                    'Không thể tạo báo cáo để in.'
                );
            }
        }

        async function exportSelectedShiftReport() {
            const shiftId =
                toPositiveInt(
                    pageState.selectedShiftId,
                    0
                );

            if (
                !shiftId ||
                !pageState.selectedSummary ||
                isSummaryLoading
            ) {
                showError(
                    'Vui lòng chọn ca và chờ tải xong tổng quan trước khi xuất.'
                );
                return;
            }

            const defaultHtml =
                btnExportShiftReport.innerHTML;

            btnExportShiftReport.disabled =
                true;

            btnExportShiftReport.innerHTML = `
                <span
                    class="spinner-border spinner-border-sm me-2"
                    aria-hidden="true">
                </span>
                Đang xuất...
            `;

            try {
                const response =
                    await fetch(
                        `/admin/pos/shift/history-export/${shiftId}`,
                        {
                            method: 'GET',
                            headers: {
                                'Accept':
                                    'text/csv'
                            },
                            credentials:
                                'same-origin'
                        }
                    );

                if (!response.ok) {
                    if (
                        response.status ===
                        401
                    ) {
                        throw new Error(
                            'Phiên đăng nhập không còn hợp lệ. Vui lòng đăng nhập lại.'
                        );
                    }

                    if (
                        response.status ===
                        403
                    ) {
                        throw new Error(
                            'Bạn không có quyền xuất báo cáo ca.'
                        );
                    }

                    let message =
                        `Không thể xuất báo cáo (${response.status}).`;

                    try {
                        const contentType =
                            response.headers
                                .get('content-type') ||
                            '';

                        if (
                            contentType.includes(
                                'application/json'
                            )
                        ) {
                            const data =
                                await response.json();

                            message =
                                data?.message ||
                                data?.title ||
                                message;
                        }
                    }
                    catch {
                        // Keep deterministic fallback message.
                    }

                    throw new Error(
                        message
                    );
                }

                const blob =
                    await response.blob();

                const objectUrl =
                    URL.createObjectURL(
                        blob
                    );

                const anchor =
                    document.createElement(
                        'a'
                    );

                const shiftCode =
                    pageState.selectedSummary
                        ?.shiftCode ||
                    pageState.selectedShiftRow
                        ?.shiftCode ||
                    `SHIFT-${shiftId}`;

                anchor.href =
                    objectUrl;

                anchor.download =
                    `bao-cao-ca-${sanitizeReportFilePart(shiftCode)}.csv`;

                anchor.style.display =
                    'none';

                document.body.appendChild(
                    anchor
                );

                anchor.click();
                anchor.remove();

                window.setTimeout(
                    () => {
                        URL.revokeObjectURL(
                            objectUrl
                        );
                    },
                    1000
                );
            }
            catch (error) {
                showError(
                    error?.message ||
                    'Không thể xuất báo cáo ca.'
                );
            }
            finally {
                btnExportShiftReport.innerHTML =
                    defaultHtml;

                btnExportShiftReport.disabled =
                    isSummaryLoading ||
                    !pageState.selectedShiftId ||
                    !pageState.selectedSummary;
            }
        }

        // =========================================================
        // HISTORY ACTIONS
        // =========================================================

        async function reloadHistoryFromFirstPage() {

            pageState.page =
                1;

            clearSelectionAndSummary();

            await loadHistory();
        }

        // =========================================================
        // TODAY
        // =========================================================

        async function applyTodayFilter() {

            const now =
                new Date();

            const today =
                toDateInputValue(
                    now
                );

            filterFrom.value =
                today;

            filterTo.value =
                today;

            await reloadHistoryFromFirstPage();
        }

        // =========================================================
        // LAST N DAYS
        // =========================================================

        async function applyLastDaysFilter(
            dayCount
        ) {
            const safeDayCount =
                Math.max(
                    1,
                    toPositiveInt(
                        dayCount,
                        1
                    )
                );

            const now =
                new Date();

            const from =
                new Date(now);

            from.setDate(
                now.getDate() -
                (
                    safeDayCount - 1
                )
            );

            filterFrom.value =
                toDateInputValue(
                    from
                );

            filterTo.value =
                toDateInputValue(
                    now
                );

            await reloadHistoryFromFirstPage();
        }

        // =========================================================
        // CLEAR DATE
        // =========================================================

        async function clearDateFilter() {

            filterFrom.value =
                '';

            filterTo.value =
                '';

            await reloadHistoryFromFirstPage();
        }

        // =========================================================
        // SEARCH
        // =========================================================

        async function executeSearch() {

            if (
                !validateDateRange()
            ) {
                return;
            }

            await reloadHistoryFromFirstPage();
        }

        // =========================================================
        // EVENT: SEARCH
        // =========================================================

        btnSearch.addEventListener(
            'click',
            executeSearch
        );

        // =========================================================
        // EVENT: PREV PAGE
        // =========================================================

        btnPrevPage.addEventListener(
            'click',
            async () => {

                if (
                    isHistoryLoading ||
                    pageState.page <= 1
                ) {
                    return;
                }

                pageState.page--;

                clearSelectionAndSummary();

                await loadHistory();
            }
        );

        // =========================================================
        // EVENT: NEXT PAGE
        // =========================================================

        btnNextPage.addEventListener(
            'click',
            async () => {

                if (
                    isHistoryLoading ||
                    pageState.page >=
                    pageState.totalPages
                ) {
                    return;
                }

                pageState.page++;

                clearSelectionAndSummary();

                await loadHistory();
            }
        );

        // =========================================================
        // EVENT: PAGE SIZE
        // =========================================================

        pageSizeSelect.addEventListener(
            'change',
            async () => {

                pageState.pageSize =
                    toPositiveInt(
                        pageSizeSelect.value,
                        20
                    );

                await reloadHistoryFromFirstPage();
            }
        );

        // =========================================================
        // EVENT: SELECT SHIFT WITH MOUSE
        // =========================================================

        historyBody.addEventListener(
            'click',
            async event => {

                const row =
                    event.target.closest(
                        '[data-shift-id]'
                    );

                await selectShiftByRow(
                    row
                );
            }
        );

        // =========================================================
        // EVENT: SELECT SHIFT WITH KEYBOARD
        // =========================================================

        historyBody.addEventListener(
            'keydown',
            async event => {

                const row =
                    event.target.closest(
                        '[data-shift-id]'
                    );

                if (!row) {
                    return;
                }

                const rows =
                    Array.from(
                        historyBody.querySelectorAll(
                            '[data-shift-id]'
                        )
                    );

                const currentIndex =
                    rows.indexOf(row);

                if (currentIndex < 0) {
                    return;
                }

                if (
                    event.key === 'Enter' ||
                    event.key === ' '
                ) {
                    event.preventDefault();

                    await selectShiftByRow(
                        row
                    );

                    return;
                }

                let nextIndex =
                    currentIndex;

                if (
                    event.key ===
                    'ArrowDown'
                ) {
                    nextIndex =
                        Math.min(
                            currentIndex + 1,
                            rows.length - 1
                        );
                }
                else if (
                    event.key ===
                    'ArrowUp'
                ) {
                    nextIndex =
                        Math.max(
                            currentIndex - 1,
                            0
                        );
                }
                else if (
                    event.key ===
                    'Home'
                ) {
                    nextIndex = 0;
                }
                else if (
                    event.key ===
                    'End'
                ) {
                    nextIndex =
                        rows.length - 1;
                }
                else {
                    return;
                }

                event.preventDefault();

                const nextRow =
                    rows[nextIndex];

                nextRow?.focus();
            }
        );

        // =========================================================
        // EVENT: QUICK FILTER
        // =========================================================

        btnToday.addEventListener(
            'click',
            applyTodayFilter
        );

        btnLast7Days.addEventListener(
            'click',
            () =>
                applyLastDaysFilter(
                    7
                )
        );

        btnLast30Days.addEventListener(
            'click',
            () =>
                applyLastDaysFilter(
                    30
                )
        );

        btnClearDate.addEventListener(
            'click',
            clearDateFilter
        );

        // =========================================================
        // EVENT: ENTER ON DATE FILTER
        // =========================================================

        [
            filterFrom,
            filterTo
        ].forEach(
            input => {

                input.addEventListener(
                    'keydown',
                    async event => {

                        if (
                            event.key !==
                            'Enter'
                        ) {
                            return;
                        }

                        event.preventDefault();

                        await executeSearch();
                    }
                );
            }
        );

        // =========================================================
        // EVENT: PRINT / EXPORT SHIFT REPORT
        // =========================================================

        btnPrintShiftReport.addEventListener(
            'click',
            printSelectedShiftReport
        );

        btnExportShiftReport.addEventListener(
            'click',
            exportSelectedShiftReport
        );

        // =========================================================
        // EVENT: OPEN ORDERS MODAL
        // =========================================================

        btnOpenOrdersModal.addEventListener(
            'click',
            () => {

                if (
                    !pageState.selectedShiftId ||
                    isSummaryLoading
                ) {
                    return;
                }

                renderOrdersModalPage();

                ordersModal.show();
            }
        );

        // =========================================================
        // EVENT: OPEN CASH MODAL
        // =========================================================

        btnOpenCashTxnsModal.addEventListener(
            'click',
            () => {

                if (
                    !pageState.selectedShiftId ||
                    isSummaryLoading
                ) {
                    return;
                }

                renderCashTxnsModalPage();

                cashTxnsModal.show();
            }
        );

        // =========================================================
        // EVENT: ORDERS MODAL PREV
        // =========================================================

        btnOrdersModalPrev.addEventListener(
            'click',
            () => {

                if (
                    ordersModalState.page <=
                    1
                ) {
                    return;
                }

                ordersModalState.page--;

                renderOrdersModalPage();
            }
        );

        // =========================================================
        // EVENT: ORDERS MODAL NEXT
        // =========================================================

        btnOrdersModalNext.addEventListener(
            'click',
            () => {

                const totalPages =
                    Math.max(
                        1,
                        Math.ceil(
                            ordersModalState
                                .items
                                .length /
                            ordersModalState
                                .pageSize
                        )
                    );

                if (
                    ordersModalState.page >=
                    totalPages
                ) {
                    return;
                }

                ordersModalState.page++;

                renderOrdersModalPage();
            }
        );

        // =========================================================
        // EVENT: CASH MODAL PREV
        // =========================================================

        btnCashTxnsModalPrev.addEventListener(
            'click',
            () => {

                if (
                    cashTxnsModalState.page <=
                    1
                ) {
                    return;
                }

                cashTxnsModalState.page--;

                renderCashTxnsModalPage();
            }
        );

        // =========================================================
        // EVENT: CASH MODAL NEXT
        // =========================================================

        btnCashTxnsModalNext.addEventListener(
            'click',
            () => {

                const totalPages =
                    Math.max(
                        1,
                        Math.ceil(
                            cashTxnsModalState
                                .items
                                .length /
                            cashTxnsModalState
                                .pageSize
                        )
                    );

                if (
                    cashTxnsModalState.page >=
                    totalPages
                ) {
                    return;
                }

                cashTxnsModalState.page++;

                renderCashTxnsModalPage();
            }
        );

        // =========================================================
        // INITIAL LOAD
        // =========================================================
        setupAccessibility();
        pageState.pageSize =
            toPositiveInt(
                pageSizeSelect.value,
                20
            );

        renderSummary(null);

        updateMainPager();

        loadHistory()
            .catch(
                error => {

                    if (
                        !isAbortError(
                            error
                        )
                    ) {
                        showError(
                            error?.message
                        );
                    }
                }
            );
    }

    // =========================================================
    // START
    // =========================================================

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