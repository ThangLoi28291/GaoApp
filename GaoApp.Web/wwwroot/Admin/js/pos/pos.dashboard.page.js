(function () {
    'use strict';

    const REFRESH_INTERVAL_MS = 10000;
    const COMPLETED_STATUS_VALUE = 2;
    const HOUR_MS = 60 * 60 * 1000;

    const root = document.querySelector('[data-pos-dashboard-v2]');
    if (!root) return;

    const probeUrl = root.getAttribute('data-probe-url') || '/admin/pos/shift/dashboard';
    const summaryUrlTemplate = root.getAttribute('data-summary-url-template') || '/admin/pos/shift/summary?shiftId={shiftId}';

    const elements = {
        skeleton: document.getElementById('dashboardSkeleton'),
        statePanel: document.getElementById('dashboardStatePanel'),
        stateIcon: document.getElementById('dashboardStateIcon'),
        stateTitle: document.getElementById('dashboardStateTitle'),
        stateMessage: document.getElementById('dashboardStateMessage'),
        stateActions: document.getElementById('dashboardStateActions'),
        content: document.getElementById('dashboardContent'),
        liveRegion: document.getElementById('dashboardLiveRegion'),
        liveStatus: document.getElementById('dashboardLiveStatus'),
        liveStatusText: document.getElementById('dashboardLiveStatusText'),
        lastRefreshText: document.getElementById('dashboardLastRefreshText'),
        refreshButton: document.getElementById('btnRefreshDashboard'),
        shiftCode: document.getElementById('dashboardShiftCode'),
        openedAt: document.getElementById('dashboardOpenedAt'),
        elapsed: document.getElementById('dashboardElapsed'),
        kpiCompletedSales: document.getElementById('kpiCompletedSales'),
        kpiCompletedOrders: document.getElementById('kpiCompletedOrders'),
        kpiAverageOrderValue: document.getElementById('kpiAverageOrderValue'),
        kpiAverageOrderNote: document.getElementById('kpiAverageOrderNote'),
        kpiClosingCashExpected: document.getElementById('kpiClosingCashExpected'),
        completedSalesChart: document.getElementById('completedSalesChart'),
        completedSalesChartSummary: document.getElementById('completedSalesChartSummary'),
        completedOrdersChart: document.getElementById('completedOrdersChart'),
        completedOrdersChartSummary: document.getElementById('completedOrdersChartSummary'),
        paymentMixTotal: document.getElementById('paymentMixTotal'),
        paymentMixCashBar: document.getElementById('paymentMixCashBar'),
        paymentMixNonCashBar: document.getElementById('paymentMixNonCashBar'),
        paymentMixCashAmount: document.getElementById('paymentMixCashAmount'),
        paymentMixCashPercent: document.getElementById('paymentMixCashPercent'),
        paymentMixNonCashAmount: document.getElementById('paymentMixNonCashAmount'),
        paymentMixNonCashPercent: document.getElementById('paymentMixNonCashPercent'),
        paymentMixSummary: document.getElementById('paymentMixSummary'),
        healthOpeningCash: document.getElementById('healthOpeningCash'),
        healthCashSales: document.getElementById('healthCashSales'),
        healthNonCashSales: document.getElementById('healthNonCashSales'),
        healthCashIn: document.getElementById('healthCashIn'),
        healthCashOut: document.getElementById('healthCashOut'),
        healthRefundTotal: document.getElementById('healthRefundTotal'),
        healthRefundCount: document.getElementById('healthRefundCount'),
        healthClosingCashExpected: document.getElementById('healthClosingCashExpected'),
        attention: document.querySelector('.pos-dashboard-attention'),
        attentionMessage: document.getElementById('dashboardAttentionMessage'),
        attentionRefund: document.getElementById('attentionRefund'),
        attentionVoid: document.getElementById('attentionVoid')
    };

    const state = {
        requestSequence: 0,
        controller: null,
        timer: null,
        resizeTimer: null,
        lastGoodData: null,
        lastGoodAt: null,
        disposed: false
    };

    function numberValue(value) {
        const n = Number(value);
        return Number.isFinite(n) ? n : 0;
    }

    function nonNegative(value) {
        return Math.max(0, numberValue(value));
    }

    function integerValue(value) {
        return Math.max(0, Math.trunc(numberValue(value)));
    }

    function formatMoney(value) {
        return Math.round(numberValue(value)).toLocaleString('vi-VN') + ' ₫';
    }

    function formatInteger(value) {
        return integerValue(value).toLocaleString('vi-VN');
    }

    function formatCompactMoney(value) {
        const n = Math.abs(numberValue(value));

        if (n >= 1000000000) {
            return (numberValue(value) / 1000000000).toLocaleString('vi-VN', {
                maximumFractionDigits: 1
            }) + ' tỷ';
        }

        if (n >= 1000000) {
            return (numberValue(value) / 1000000).toLocaleString('vi-VN', {
                maximumFractionDigits: 1
            }) + ' tr';
        }

        if (n >= 1000) {
            return (numberValue(value) / 1000).toLocaleString('vi-VN', {
                maximumFractionDigits: 0
            }) + 'k';
        }

        return Math.round(numberValue(value)).toLocaleString('vi-VN');
    }

    function parseDate(value) {
        if (!value) return null;
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? null : date;
    }

    function formatDateTime(value) {
        const date = value instanceof Date ? value : parseDate(value);
        if (!date) return '-';

        return date.toLocaleString('vi-VN', {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        });
    }

    function formatClock(value) {
        const date = value instanceof Date ? value : parseDate(value);
        if (!date) return '--:--:--';
        return date.toLocaleTimeString('vi-VN');
    }

    function formatElapsed(openedAtUtc) {
        const opened = parseDate(openedAtUtc);
        if (!opened) return '-';

        const diffMs = Math.max(0, Date.now() - opened.getTime());
        const totalMinutes = Math.floor(diffMs / 60000);
        const days = Math.floor(totalMinutes / 1440);
        const hours = Math.floor((totalMinutes % 1440) / 60);
        const minutes = totalMinutes % 60;

        const parts = [];
        if (days > 0) parts.push(days + ' ngày');
        if (hours > 0 || days > 0) parts.push(hours + 'h');
        parts.push(minutes + 'm');
        return parts.join(' ');
    }

    function isCompletedStatus(status) {
        if (typeof status === 'number') return status === COMPLETED_STATUS_VALUE;

        const raw = String(status ?? '').trim().toLowerCase();
        if (!raw) return false;
        if (raw === String(COMPLETED_STATUS_VALUE)) return true;
        return raw === 'completed' || raw.endsWith('.completed');
    }

    function setBusy(isBusy) {
        root.setAttribute('aria-busy', isBusy ? 'true' : 'false');

        if (elements.refreshButton) {
            elements.refreshButton.disabled = isBusy;
            elements.refreshButton.classList.toggle('is-refreshing', isBusy);
            elements.refreshButton.setAttribute('aria-busy', isBusy ? 'true' : 'false');
        }
    }

    function announce(message) {
        if (!elements.liveRegion) return;
        elements.liveRegion.textContent = '';
        window.setTimeout(function () {
            elements.liveRegion.textContent = message || '';
        }, 20);
    }

    function setLiveState(kind, text) {
        if (!elements.liveStatus || !elements.liveStatusText) return;

        elements.liveStatus.classList.remove('is-live', 'is-loading', 'is-stale', 'is-error', 'is-idle');
        elements.liveStatus.classList.add('is-' + kind);
        elements.liveStatusText.textContent = text;
    }

    function setLastRefresh(date) {
        if (elements.lastRefreshText) {
            elements.lastRefreshText.textContent = formatClock(date);
        }
    }

    function showSkeleton() {
        if (elements.skeleton) elements.skeleton.hidden = false;
        if (elements.statePanel) elements.statePanel.hidden = true;
        if (elements.content) elements.content.hidden = true;
    }

    function showContent() {
        if (elements.skeleton) elements.skeleton.hidden = true;
        if (elements.statePanel) elements.statePanel.hidden = true;
        if (elements.content) elements.content.hidden = false;
    }

    function showStatePanel(options) {
        const config = options || {};

        if (elements.skeleton) elements.skeleton.hidden = true;
        if (elements.content) elements.content.hidden = true;
        if (!elements.statePanel) return;

        elements.statePanel.hidden = false;
        elements.statePanel.classList.toggle('is-error', config.kind === 'error');

        if (elements.stateIcon) {
            elements.stateIcon.innerHTML = '<i class="bx ' + (config.iconClass || 'bx-info-circle') + '"></i>';
        }

        if (elements.stateTitle) elements.stateTitle.textContent = config.title || '';
        if (elements.stateMessage) elements.stateMessage.textContent = config.message || '';

        if (elements.stateActions) {
            elements.stateActions.innerHTML = '';

            (config.actions || []).forEach(function (action) {
                const node = action.href
                    ? document.createElement('a')
                    : document.createElement('button');

                if (action.href) node.setAttribute('href', action.href);
                if (!action.href) node.setAttribute('type', 'button');

                node.className = 'btn ' + (action.className || 'btn-outline-primary');
                node.innerHTML = '<i class="bx ' + (action.iconClass || 'bx-right-arrow-alt') + '" aria-hidden="true"></i><span></span>';
                node.querySelector('span').textContent = action.label || 'Tiếp tục';

                if (typeof action.onClick === 'function') {
                    node.addEventListener('click', action.onClick);
                }

                elements.stateActions.appendChild(node);
            });
        }
    }

    function renderInitialLoading() {
        showSkeleton();
        setLiveState('loading', 'Đang cập nhật');
    }

    function renderNoOpenShift() {
        state.lastGoodData = null;
        state.lastGoodAt = null;
        setLastRefresh(null);
        setLiveState('idle', 'Chưa mở ca');

        showStatePanel({
            title: 'Chưa có ca POS đang mở',
            message: 'Mở ca để bắt đầu theo dõi đơn hoàn tất, dòng tiền và các chỉ số vận hành theo thời gian thực.',
            iconClass: 'bx-time-five',
            actions: [
                {
                    label: 'Đi tới Ca POS',
                    href: '/admin/pos-shift',
                    className: 'btn-primary',
                    iconClass: 'bx-time-five'
                },
                {
                    label: 'Lịch sử ca',
                    href: '/admin/pos-shift/history',
                    className: 'btn-outline-secondary',
                    iconClass: 'bx-history'
                }
            ]
        });

        announce('Chưa có ca POS đang mở.');
    }

    function renderInitialError(error) {
        const message = error && error.message
            ? error.message
            : 'Không tải được dữ liệu dashboard. Vui lòng thử lại.';

        setLiveState('error', 'Tạm gián đoạn');

        showStatePanel({
            kind: 'error',
            title: 'Không tải được dữ liệu dashboard',
            message: message,
            iconClass: 'bx-error-circle',
            actions: [
                {
                    label: 'Thử lại',
                    className: 'btn-primary',
                    iconClass: 'bx-refresh',
                    onClick: function () { refreshDashboard(true); }
                },
                {
                    label: 'Đi tới Ca POS',
                    href: '/admin/pos-shift',
                    className: 'btn-outline-secondary',
                    iconClass: 'bx-time-five'
                }
            ]
        });

        announce('Không tải được dữ liệu dashboard.');
    }

    function renderStale(error) {
        showContent();
        setLiveState('stale', 'Tạm gián đoạn');
        setLastRefresh(state.lastGoodAt);

        const suffix = state.lastGoodAt ? ' Dữ liệu gần nhất lúc ' + formatClock(state.lastGoodAt) + '.' : '';
        const safeMessage = error && error.message ? error.message : 'Không thể cập nhật dashboard.';
        announce(safeMessage + suffix);
    }

    async function fetchJson(url, signal) {
        const response = await fetch(url, {
            method: 'GET',
            headers: {
                'Accept': 'application/json'
            },
            signal: signal,
            cache: 'no-store'
        });

        const contentType = response.headers.get('content-type') || '';
        let payload = null;

        try {
            payload = contentType.includes('application/json')
                ? await response.json()
                : await response.text();
        } catch {
            payload = null;
        }

        if (!response.ok) {
            const message = payload && typeof payload === 'object'
                ? (payload.message || payload.title || payload.error)
                : payload;

            throw new Error(message || ('Yêu cầu thất bại (' + response.status + ')'));
        }

        return payload || {};
    }

    function buildSummaryUrl(shiftId) {
        return summaryUrlTemplate.replace('{shiftId}', encodeURIComponent(String(shiftId)));
    }

    function createSnapshot(probe, summary) {
        const completedOrders = integerValue(summary.completedOrders);
        const completedSalesTotal = nonNegative(summary.completedSalesTotal);
        const averageOrderValue = completedOrders > 0
            ? completedSalesTotal / completedOrders
            : 0;

        return Object.freeze({
            shiftId: integerValue(summary.id || probe.shiftId),
            shiftCode: String(summary.shiftCode || probe.shiftCode || ('SHIFT-' + probe.shiftId)),
            openedAtUtc: summary.openedAtUtc || probe.openedAt || null,
            completedSalesTotal: completedSalesTotal,
            completedOrders: completedOrders,
            averageOrderValue: averageOrderValue,
            openingCash: nonNegative(summary.openingCash),
            cashSalesTotal: nonNegative(summary.cashSalesTotal),
            nonCashSalesTotal: nonNegative(summary.nonCashSalesTotal),
            refundTotal: nonNegative(summary.refundTotal),
            refundCount: integerValue(summary.refundCount),
            voidCount: integerValue(summary.voidCount),
            cashInTotal: nonNegative(summary.cashInTotal),
            cashOutTotal: nonNegative(summary.cashOutTotal),
            closingCashExpected: nonNegative(summary.closingCashExpected),
            orders: Array.isArray(summary.orders) ? summary.orders.slice() : []
        });
    }

    function renderSnapshot(snapshot) {
        showContent();

        if (elements.shiftCode) elements.shiftCode.textContent = snapshot.shiftCode || '-';
        if (elements.openedAt) elements.openedAt.textContent = formatDateTime(snapshot.openedAtUtc);
        if (elements.elapsed) elements.elapsed.textContent = formatElapsed(snapshot.openedAtUtc);

        if (elements.kpiCompletedSales) elements.kpiCompletedSales.textContent = formatMoney(snapshot.completedSalesTotal);
        if (elements.kpiCompletedOrders) elements.kpiCompletedOrders.textContent = formatInteger(snapshot.completedOrders);
        if (elements.kpiAverageOrderValue) elements.kpiAverageOrderValue.textContent = formatMoney(snapshot.averageOrderValue);
        if (elements.kpiAverageOrderNote) {
            elements.kpiAverageOrderNote.textContent = snapshot.completedOrders > 0
                ? 'Trung bình trên ' + formatInteger(snapshot.completedOrders) + ' đơn hoàn tất'
                : 'Chưa có đơn hoàn tất';
        }
        if (elements.kpiClosingCashExpected) elements.kpiClosingCashExpected.textContent = formatMoney(snapshot.closingCashExpected);

        renderPaymentMix(snapshot);
        renderShiftHealth(snapshot);
        renderAttention(snapshot);

        const buckets = buildHourlyBuckets(snapshot.openedAtUtc, snapshot.orders);
        renderCompletedSalesChart(buckets, snapshot.completedOrders);
        renderCompletedOrdersChart(buckets, snapshot.completedOrders);
    }

    function renderPaymentMix(snapshot) {
        const cash = snapshot.cashSalesTotal;
        const nonCash = snapshot.nonCashSalesTotal;
        const total = cash + nonCash;
        const cashPercent = total > 0 ? (cash / total) * 100 : 0;
        const nonCashPercent = total > 0 ? (nonCash / total) * 100 : 0;

        if (elements.paymentMixTotal) elements.paymentMixTotal.textContent = formatMoney(total);
        if (elements.paymentMixCashAmount) elements.paymentMixCashAmount.textContent = formatMoney(cash);
        if (elements.paymentMixNonCashAmount) elements.paymentMixNonCashAmount.textContent = formatMoney(nonCash);
        if (elements.paymentMixCashPercent) elements.paymentMixCashPercent.textContent = cashPercent.toLocaleString('vi-VN', { maximumFractionDigits: 1 }) + '%';
        if (elements.paymentMixNonCashPercent) elements.paymentMixNonCashPercent.textContent = nonCashPercent.toLocaleString('vi-VN', { maximumFractionDigits: 1 }) + '%';
        if (elements.paymentMixCashBar) elements.paymentMixCashBar.style.width = cashPercent + '%';
        if (elements.paymentMixNonCashBar) elements.paymentMixNonCashBar.style.width = nonCashPercent + '%';

        if (elements.paymentMixSummary) {
            elements.paymentMixSummary.textContent = total > 0
                ? 'Tiền mặt chiếm ' + cashPercent.toLocaleString('vi-VN', { maximumFractionDigits: 1 }) + '%; không tiền mặt chiếm ' + nonCashPercent.toLocaleString('vi-VN', { maximumFractionDigits: 1 }) + '%.'
                : 'Chưa phát sinh tiền bán trong ca.';
        }
    }

    function renderShiftHealth(snapshot) {
        if (elements.healthOpeningCash) elements.healthOpeningCash.textContent = formatMoney(snapshot.openingCash);
        if (elements.healthCashSales) elements.healthCashSales.textContent = formatMoney(snapshot.cashSalesTotal);
        if (elements.healthNonCashSales) elements.healthNonCashSales.textContent = formatMoney(snapshot.nonCashSalesTotal);
        if (elements.healthCashIn) elements.healthCashIn.textContent = formatMoney(snapshot.cashInTotal);
        if (elements.healthCashOut) elements.healthCashOut.textContent = formatMoney(snapshot.cashOutTotal);
        if (elements.healthRefundTotal) elements.healthRefundTotal.textContent = formatMoney(snapshot.refundTotal);
        if (elements.healthRefundCount) elements.healthRefundCount.textContent = formatInteger(snapshot.refundCount);
        if (elements.healthClosingCashExpected) elements.healthClosingCashExpected.textContent = formatMoney(snapshot.closingCashExpected);
    }

    function renderAttention(snapshot) {
        const hasActivity = snapshot.refundCount > 0 || snapshot.refundTotal > 0 || snapshot.voidCount > 0;

        if (elements.attention) elements.attention.classList.toggle('has-activity', hasActivity);
        if (elements.attentionRefund) elements.attentionRefund.textContent = formatMoney(snapshot.refundTotal) + ' · ' + formatInteger(snapshot.refundCount) + ' lượt';
        if (elements.attentionVoid) elements.attentionVoid.textContent = formatInteger(snapshot.voidCount);

        if (elements.attentionMessage) {
            elements.attentionMessage.textContent = hasActivity
                ? 'Ca hiện tại có phát sinh refund hoặc void; có thể mở Đơn POS để đối chiếu chi tiết.'
                : 'Không có refund hoặc void trong ca hiện tại.';
        }
    }

    function floorToLocalHour(date) {
        const d = new Date(date.getTime());
        d.setMinutes(0, 0, 0);
        return d;
    }

    function formatBucketLabel(date, spansMultipleDays) {
        const hour = String(date.getHours()).padStart(2, '0') + 'h';

        if (!spansMultipleDays) {
            return hour;
        }

        const day = String(date.getDate()).padStart(2, '0');
        const month = String(date.getMonth() + 1).padStart(2, '0');
        return hour + ' · ' + day + '/' + month;
    }

    function formatBucketDetailLabel(date) {
        const hour = String(date.getHours()).padStart(2, '0') + 'h';
        const day = String(date.getDate()).padStart(2, '0');
        const month = String(date.getMonth() + 1).padStart(2, '0');
        return hour + ' · ' + day + '/' + month;
    }

    function buildHourlyBuckets(openedAtUtc, orders) {
        const bucketMap = new Map();

        (orders || [])
            .filter(function (order) {
                return isCompletedStatus(order && order.status) &&
                    !!parseDate(order && order.completedAtUtc);
            })
            .forEach(function (order) {
                const completedAt = parseDate(order.completedAtUtc);
                if (!completedAt) return;

                const start = floorToLocalHour(completedAt);
                const timestamp = start.getTime();

                let bucket = bucketMap.get(timestamp);

                if (!bucket) {
                    bucket = {
                        timestamp: timestamp,
                        start: start,
                        amount: 0,
                        count: 0,
                        detailLabel: formatBucketDetailLabel(start)
                    };

                    bucketMap.set(timestamp, bucket);
                }

                bucket.amount += nonNegative(order.grandTotal);
                bucket.count += 1;
            });

        return Array.from(bucketMap.values())
            .sort(function (a, b) {
                return a.timestamp - b.timestamp;
            });
    }

    function bucketAxisParts(bucket) {
        const date = bucket && bucket.start instanceof Date
            ? bucket.start
            : parseDate(bucket && bucket.start);

        if (!date) {
            return {
                hour: '-',
                date: '-'
            };
        }

        return {
            hour: String(date.getHours()).padStart(2, '0') + 'h',
            date:
                String(date.getDate()).padStart(2, '0') +
                '/' +
                String(date.getMonth() + 1).padStart(2, '0')
        };
    }

    function niceMax(value, tickCount) {
        const maxValue = Math.max(0, numberValue(value));
        const ticks = Math.max(2, tickCount || 4);
        if (maxValue <= 0) return ticks;

        const rawStep = maxValue / ticks;
        const magnitude = Math.pow(10, Math.floor(Math.log10(rawStep)));
        const normalized = rawStep / magnitude;

        let niceNormalized;
        if (normalized <= 1) niceNormalized = 1;
        else if (normalized <= 2) niceNormalized = 2;
        else if (normalized <= 5) niceNormalized = 5;
        else niceNormalized = 10;

        const step = niceNormalized * magnitude;
        return Math.ceil(maxValue / step) * step;
    }

    function escapeXml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&apos;');
    }

    function getActiveBuckets(buckets) {
        return (buckets || [])
            .filter(function (bucket) {
                return bucket && bucket.count > 0;
            })
            .sort(function (a, b) {
                return a.timestamp - b.timestamp;
            });
    }

    function chartLayout(element, bucketCount, compact) {
        const availableWidth = Math.max(
            320,
            Math.floor((element && element.clientWidth) || 820)
        );

        const margin = compact
            ? { top: 34, right: 22, bottom: 64, left: 56 }
            : { top: 40, right: 22, bottom: 64, left: 72 };

        const minimumSlotWidth = bucketCount <= 4
            ? 118
            : bucketCount <= 8
                ? 92
                : 78;

        const minimumWidth =
            margin.left +
            margin.right +
            Math.max(1, bucketCount) * minimumSlotWidth;

        const width = Math.max(availableWidth, minimumWidth);
        const height = compact ? 258 : 292;
        const plotWidth = width - margin.left - margin.right;
        const plotHeight = height - margin.top - margin.bottom;
        const slotWidth = plotWidth / Math.max(1, bucketCount);
        const barWidth = Math.max(
            26,
            Math.min(76, slotWidth * 0.52)
        );

        if (element) {
            element.classList.toggle(
                'is-scrollable',
                width > availableWidth + 2
            );
        }

        return {
            width: width,
            height: height,
            margin: margin,
            plotWidth: plotWidth,
            plotHeight: plotHeight,
            slotWidth: slotWidth,
            barWidth: barWidth
        };
    }

    function renderXAxisLabel(bucket, x, y) {
        const parts = bucketAxisParts(bucket);

        return ''
            + '<text class="posdash-axis-label" x="' + x.toFixed(2) + '" y="' + y + '" text-anchor="middle">'
            + '  <tspan class="posdash-axis-hour" x="' + x.toFixed(2) + '" dy="0">' + escapeXml(parts.hour) + '</tspan>'
            + '  <tspan class="posdash-axis-date" x="' + x.toFixed(2) + '" dy="14">' + escapeXml(parts.date) + '</tspan>'
            + '</text>';
    }

    function formatColumnMoneyLabel(value, bucketCount) {
        if (bucketCount <= 6) {
            return Math.round(numberValue(value)).toLocaleString('vi-VN');
        }

        return formatCompactMoney(value);
    }

    function integerTickValues(maxValue) {
        const maxCount = Math.max(1, Math.ceil(numberValue(maxValue)));
        const step = Math.max(1, Math.ceil(maxCount / 4));
        const values = [0];

        for (let value = step; value < maxCount; value += step) {
            values.push(value);
        }

        if (values[values.length - 1] !== maxCount) values.push(maxCount);
        return values;
    }

    function renderCompletedSalesChart(buckets, completedOrders) {
        if (!elements.completedSalesChart || !elements.completedSalesChartSummary) return;

        const activeBuckets = getActiveBuckets(buckets);

        if (completedOrders <= 0 || activeBuckets.length === 0) {
            elements.completedSalesChart.classList.remove('is-scrollable');
            elements.completedSalesChart.innerHTML =
                '<div class="pos-dashboard-chart-empty">Chưa có đơn hoàn tất để hiển thị giá trị theo giờ.</div>';

            elements.completedSalesChartSummary.textContent =
                'Biểu đồ sẽ xuất hiện sau khi có đơn hoàn tất trong ca.';
            return;
        }

        const total = activeBuckets.reduce(function (sum, bucket) {
            return sum + bucket.amount;
        }, 0);

        const peak = activeBuckets.reduce(function (best, bucket) {
            return bucket.amount > best.amount ? bucket : best;
        }, activeBuckets[0]);

        const aria =
            'Biểu đồ cột giá trị đơn hoàn tất theo giờ. ' +
            'Chỉ hiển thị các giờ có phát sinh. ' +
            'Tổng ' + formatMoney(total) + '. ' +
            'Cao nhất ' + peak.detailLabel +
            ' với ' + formatMoney(peak.amount) + '.';

        elements.completedSalesChart.setAttribute('aria-label', aria);

        const layout = chartLayout(
            elements.completedSalesChart,
            activeBuckets.length,
            false
        );

        const width = layout.width;
        const height = layout.height;
        const margin = layout.margin;
        const plotHeight = layout.plotHeight;
        const slotWidth = layout.slotWidth;
        const barWidth = layout.barWidth;

        const maxAmount = Math.max.apply(
            null,
            activeBuckets.map(function (bucket) {
                return bucket.amount;
            })
        );

        const yMax = niceMax(maxAmount * 1.18, 4);

        function xCenter(index) {
            return margin.left + slotWidth * index + slotWidth / 2;
        }

        function yFor(value) {
            return margin.top +
                plotHeight -
                (numberValue(value) / yMax) * plotHeight;
        }

        let grid = '';
        const yTicks = 4;

        for (let i = 0; i <= yTicks; i++) {
            const value = (yMax / yTicks) * i;
            const y = yFor(value);

            grid +=
                '<line class="posdash-grid-line" ' +
                'x1="' + margin.left + '" ' +
                'y1="' + y.toFixed(2) + '" ' +
                'x2="' + (width - margin.right) + '" ' +
                'y2="' + y.toFixed(2) + '"></line>';

            grid +=
                '<text class="posdash-axis-label" ' +
                'x="' + (margin.left - 10) + '" ' +
                'y="' + (y + 4).toFixed(2) + '" ' +
                'text-anchor="end">' +
                escapeXml(formatCompactMoney(value)) +
                '</text>';
        }

        let bars = '';

        activeBuckets.forEach(function (bucket, index) {
            const center = xCenter(index);
            const y = yFor(bucket.amount);
            const baseline = margin.top + plotHeight;
            const barHeight = Math.max(2, baseline - y);
            const x = center - barWidth / 2;
            const isPeak = bucket.timestamp === peak.timestamp;
            const showValue =
                activeBuckets.length <= 8 ||
                isPeak;

            bars +=
                '<rect class="posdash-bar is-money' +
                (isPeak ? ' is-peak' : '') +
                '" x="' + x.toFixed(2) +
                '" y="' + y.toFixed(2) +
                '" width="' + barWidth.toFixed(2) +
                '" height="' + barHeight.toFixed(2) +
                '">' +
                '<title>' +
                escapeXml(
                    bucket.detailLabel +
                    ': ' +
                    formatMoney(bucket.amount) +
                    ' · ' +
                    formatInteger(bucket.count) +
                    ' đơn'
                ) +
                '</title></rect>';

            if (showValue) {
                bars +=
                    '<text class="posdash-bar-value' +
                    (isPeak ? ' is-peak' : '') +
                    '" x="' + center.toFixed(2) +
                    '" y="' + Math.max(17, y - 9).toFixed(2) +
                    '">' +
                    escapeXml(
                        formatColumnMoneyLabel(
                            bucket.amount,
                            activeBuckets.length
                        )
                    ) +
                    '</text>';
            }
        });

        let xLabels = '';

        activeBuckets.forEach(function (bucket, index) {
            xLabels += renderXAxisLabel(
                bucket,
                xCenter(index),
                height - 31
            );
        });

        elements.completedSalesChart.innerHTML =
            '<svg xmlns="http://www.w3.org/2000/svg" ' +
            'width="' + width + '" ' +
            'height="' + height + '" ' +
            'style="width:' + width + 'px;height:' + height + 'px;" ' +
            'role="img" ' +
            'aria-label="' + escapeXml(aria) + '">' +
            '<title>' + escapeXml(aria) + '</title>' +
            grid +
            '<line class="posdash-axis-line" ' +
            'x1="' + margin.left + '" ' +
            'y1="' + (margin.top + plotHeight) + '" ' +
            'x2="' + (width - margin.right) + '" ' +
            'y2="' + (margin.top + plotHeight) + '"></line>' +
            bars +
            xLabels +
            '</svg>';

        elements.completedSalesChartSummary.textContent =
            'Tổng ' + formatMoney(total) +
            ' · ' +
            activeBuckets.length.toLocaleString('vi-VN') +
            ' giờ có phát sinh' +
            ' · cao nhất ' +
            peak.detailLabel +
            ': ' +
            formatMoney(peak.amount) +
            '.';
    }

    function renderCompletedOrdersChart(buckets, completedOrders) {
        if (!elements.completedOrdersChart || !elements.completedOrdersChartSummary) return;

        const activeBuckets = getActiveBuckets(buckets);

        if (completedOrders <= 0 || activeBuckets.length === 0) {
            elements.completedOrdersChart.classList.remove('is-scrollable');
            elements.completedOrdersChart.innerHTML =
                '<div class="pos-dashboard-chart-empty">Chưa có đơn hoàn tất trong ca.</div>';

            elements.completedOrdersChartSummary.textContent =
                'Biểu đồ số đơn sẽ xuất hiện sau khi có đơn hoàn tất.';
            return;
        }

        const peak = activeBuckets.reduce(function (best, bucket) {
            return bucket.count > best.count ? bucket : best;
        }, activeBuckets[0]);

        const aria =
            'Biểu đồ cột số đơn hoàn tất theo giờ. ' +
            'Chỉ hiển thị các giờ có phát sinh. ' +
            'Tổng ' + completedOrders + ' đơn. ' +
            'Cao nhất ' + peak.detailLabel +
            ' với ' + peak.count + ' đơn.';

        elements.completedOrdersChart.setAttribute('aria-label', aria);

        const layout = chartLayout(
            elements.completedOrdersChart,
            activeBuckets.length,
            true
        );

        const width = layout.width;
        const height = layout.height;
        const margin = layout.margin;
        const plotHeight = layout.plotHeight;
        const slotWidth = layout.slotWidth;
        const barWidth = layout.barWidth;

        const maxCount = Math.max.apply(
            null,
            activeBuckets.map(function (bucket) {
                return bucket.count;
            })
        );

        const yMax = Math.max(1, Math.ceil(maxCount));

        function xCenter(index) {
            return margin.left + slotWidth * index + slotWidth / 2;
        }

        function yFor(value) {
            return margin.top +
                plotHeight -
                (numberValue(value) / yMax) * plotHeight;
        }

        let grid = '';

        integerTickValues(yMax).forEach(function (value) {
            const y = yFor(value);

            grid +=
                '<line class="posdash-grid-line" ' +
                'x1="' + margin.left + '" ' +
                'y1="' + y.toFixed(2) + '" ' +
                'x2="' + (width - margin.right) + '" ' +
                'y2="' + y.toFixed(2) + '"></line>';

            grid +=
                '<text class="posdash-axis-label" ' +
                'x="' + (margin.left - 10) + '" ' +
                'y="' + (y + 4).toFixed(2) + '" ' +
                'text-anchor="end">' +
                escapeXml(value) +
                '</text>';
        });

        let bars = '';

        activeBuckets.forEach(function (bucket, index) {
            const center = xCenter(index);
            const y = yFor(bucket.count);
            const baseline = margin.top + plotHeight;
            const barHeight = Math.max(2, baseline - y);
            const x = center - barWidth / 2;
            const isPeak = bucket.timestamp === peak.timestamp;

            bars +=
                '<rect class="posdash-bar is-count' +
                (isPeak ? ' is-peak' : '') +
                '" x="' + x.toFixed(2) +
                '" y="' + y.toFixed(2) +
                '" width="' + barWidth.toFixed(2) +
                '" height="' + barHeight.toFixed(2) +
                '">' +
                '<title>' +
                escapeXml(
                    bucket.detailLabel +
                    ': ' +
                    formatInteger(bucket.count) +
                    ' đơn · ' +
                    formatMoney(bucket.amount)
                ) +
                '</title></rect>';

            bars +=
                '<text class="posdash-bar-value' +
                (isPeak ? ' is-peak' : '') +
                '" x="' + center.toFixed(2) +
                '" y="' + Math.max(17, y - 9).toFixed(2) +
                '">' +
                escapeXml(formatInteger(bucket.count)) +
                '</text>';
        });

        let xLabels = '';

        activeBuckets.forEach(function (bucket, index) {
            xLabels += renderXAxisLabel(
                bucket,
                xCenter(index),
                height - 31
            );
        });

        elements.completedOrdersChart.innerHTML =
            '<svg xmlns="http://www.w3.org/2000/svg" ' +
            'width="' + width + '" ' +
            'height="' + height + '" ' +
            'style="width:' + width + 'px;height:' + height + 'px;" ' +
            'role="img" ' +
            'aria-label="' + escapeXml(aria) + '">' +
            '<title>' + escapeXml(aria) + '</title>' +
            grid +
            '<line class="posdash-axis-line" ' +
            'x1="' + margin.left + '" ' +
            'y1="' + (margin.top + plotHeight) + '" ' +
            'x2="' + (width - margin.right) + '" ' +
            'y2="' + (margin.top + plotHeight) + '"></line>' +
            bars +
            xLabels +
            '</svg>';

        elements.completedOrdersChartSummary.textContent =
            'Tổng ' + formatInteger(completedOrders) +
            ' đơn · ' +
            activeBuckets.length.toLocaleString('vi-VN') +
            ' giờ có phát sinh' +
            ' · cao nhất ' +
            peak.detailLabel +
            ': ' +
            formatInteger(peak.count) +
            ' đơn.';
    }

    function scheduleNextRefresh() {
        if (state.disposed) return;
        window.clearTimeout(state.timer);
        state.timer = window.setTimeout(function () {
            refreshDashboard(false);
        }, REFRESH_INTERVAL_MS);
    }

    async function refreshDashboard(manual) {
        if (state.disposed) return;

        window.clearTimeout(state.timer);
        state.timer = null;

        if (state.controller) {
            state.controller.abort();
        }

        const requestSequence = ++state.requestSequence;
        const controller = new AbortController();
        state.controller = controller;
        let resolvedShiftId = null;
        const hadGoodData = !!state.lastGoodData;

        setBusy(true);

        if (hadGoodData) {
            setLiveState('loading', 'Đang cập nhật');
        } else {
            renderInitialLoading();
        }

        try {
            const probe = await fetchJson(probeUrl, controller.signal);
            if (requestSequence !== state.requestSequence) return;

            resolvedShiftId = integerValue(probe.shiftId);

            if (resolvedShiftId <= 0) {
                renderNoOpenShift();
                return;
            }

            const shiftChanged = state.lastGoodData && state.lastGoodData.shiftId !== resolvedShiftId;
            if (shiftChanged) {
                showSkeleton();
                setLiveState('loading', 'Đang chuyển ca');
            }

            const summary = await fetchJson(buildSummaryUrl(resolvedShiftId), controller.signal);
            if (requestSequence !== state.requestSequence) return;

            const summaryShiftId = integerValue(summary.id);
            if (summaryShiftId > 0 && summaryShiftId !== resolvedShiftId) {
                throw new Error('Dữ liệu ca trả về không khớp ca hiện tại.');
            }

            const snapshot = createSnapshot(probe, summary);
            const now = new Date();

            state.lastGoodData = snapshot;
            state.lastGoodAt = now;

            renderSnapshot(snapshot);
            setLastRefresh(now);
            setLiveState('live', 'Trực tiếp · vừa cập nhật');
            announce(manual ? 'Đã làm mới Dashboard POS.' : 'Dashboard POS đã được cập nhật.');
        } catch (error) {
            if (error && error.name === 'AbortError') return;
            if (requestSequence !== state.requestSequence) return;

            const canRetainGoodData = state.lastGoodData &&
                (!resolvedShiftId || state.lastGoodData.shiftId === resolvedShiftId);

            if (canRetainGoodData) {
                renderStale(error);
            } else {
                renderInitialError(error);
            }
        } finally {
            if (requestSequence === state.requestSequence) {
                setBusy(false);
                if (state.controller === controller) state.controller = null;
                scheduleNextRefresh();
            }
        }
    }

    function bindEvents() {
        if (elements.refreshButton) {
            elements.refreshButton.addEventListener('click', function () {
                refreshDashboard(true);
            });
        }

        window.addEventListener('resize', function () {
            window.clearTimeout(state.resizeTimer);

            state.resizeTimer = window.setTimeout(function () {
                if (state.disposed || !state.lastGoodData) return;
                renderSnapshot(state.lastGoodData);
            }, 160);
        });

        window.addEventListener('beforeunload', function () {
            state.disposed = true;
            window.clearTimeout(state.timer);
            window.clearTimeout(state.resizeTimer);
            if (state.controller) state.controller.abort();
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        bindEvents();
        renderInitialLoading();
        refreshDashboard(false);
    });
}());
