(() => {
    'use strict';

    const root = document.querySelector('[data-sales-executive-report]');
    if (!root) return;

    const dataUrl = root.dataset.dataUrl;
    const detailUrl = root.dataset.detailUrl;

    function openSalesDetail(segment, options = {}) {
        if (!detailUrl) return;
        const url = new URL(detailUrl, window.location.origin);
        const current = new URLSearchParams(window.location.search);
        ['fromDate', 'toDate', 'terminalId', 'customerState', 'compare'].forEach(key => {
            if (current.has(key)) url.searchParams.set(key, current.get(key));
        });
        url.searchParams.set('segment', segment || 'overview');
        if (options.focus) url.searchParams.set('focus', options.focus);
        if (options.variantId) url.searchParams.set('variantId', String(options.variantId));
        if (Number.isInteger(options.bucketIndex)) url.searchParams.set('bucketIndex', String(options.bucketIndex));
        window.location.assign(`${url.pathname}?${url.searchParams.toString()}`);
    }

    root.addEventListener('click', event => {
        const trigger = event.target.closest('[data-sales-drill-segment]');
        if (!trigger) return;
        openSalesDetail(trigger.dataset.salesDrillSegment, { focus: trigger.dataset.salesDrillFocus });
    });
    root.addEventListener('keydown', event => {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        const trigger = event.target.closest('[data-sales-drill-segment]');
        if (!trigger) return;
        event.preventDefault();
        openSalesDetail(trigger.dataset.salesDrillSegment, { focus: trigger.dataset.salesDrillFocus });
    });
    const els = {
        fromDate: document.getElementById('salesReportFromDate'),
        toDate: document.getElementById('salesReportToDate'),
        compare: document.getElementById('salesReportCompare'),
        terminal: document.getElementById('salesReportTerminal'),
        customerState: document.getElementById('salesReportCustomerState'),
        secondaryToggle: document.getElementById('salesReportSecondaryFilterToggle'),
        secondaryFilters: document.getElementById('salesReportSecondaryFilters'),
        clearSecondary: document.getElementById('salesReportClearSecondaryFilters'),
        filterCount: document.getElementById('salesReportFilterCount'),
        refresh: document.getElementById('salesReportRefreshButton'),
        lastRefresh: document.getElementById('salesReportLastRefresh'),
        liveRegion: document.getElementById('salesReportLiveRegion'),
        staleBanner: document.getElementById('salesReportStaleBanner'),
        skeleton: document.getElementById('salesReportSkeleton'),
        statePanel: document.getElementById('salesReportStatePanel'),
        stateTitle: document.getElementById('salesReportStateTitle'),
        stateMessage: document.getElementById('salesReportStateMessage'),
        stateRetry: document.getElementById('salesReportStateRetry'),
        content: document.getElementById('salesReportContent'),
        trendPeriodText: document.getElementById('salesTrendPeriodText'),
        trendChart: document.getElementById('salesTrendChart'),
        trendDataBody: document.getElementById('salesTrendDataBody'),
        salesByHourMetric: document.getElementById('salesByHourMetric'),
        salesByHourChart: document.getElementById('salesByHourChart'),
        salesByHourPeakText: document.getElementById('salesByHourPeakText'),
        salesByHourDataBody: document.getElementById('salesByHourDataBody'),
        topProductsChart: document.getElementById('salesTopProductsChart'),
        topProductsDataBody: document.getElementById('salesTopProductsDataBody'),
        discountTotal: document.getElementById('salesDiscountTotal'),
        discountReconcile: document.getElementById('salesDiscountReconcile'),
        discountChart: document.getElementById('salesDiscountChart'),
        discountDataBody: document.getElementById('salesDiscountDataBody'),
        returnsRefundsChart: document.getElementById('salesReturnsRefundsChart'),
        returnsRefundsDataBody: document.getElementById('salesReturnsRefundsDataBody'),
        returnCountInline: document.getElementById('salesReturnCountInline'),
        refundCountInline: document.getElementById('salesRefundCountInline'),
        customerMixChart: document.getElementById('salesCustomerMixChart'),
        customerMixDataBody: document.getElementById('salesCustomerMixDataBody'),
        customerLinkedRateInline: document.getElementById('salesCustomerLinkedRateInline'),
        customerLinkedOrders: document.getElementById('salesCustomerLinkedOrders'),
        customerGuestOrders: document.getElementById('salesCustomerGuestOrders')
    };

    let activeController = null;
    let requestSequence = 0;
    let lastGoodData = null;
    let trendChart = null;
    let salesByHourChart = null;
    let topProductsChart = null;
    let discountChart = null;
    let returnsRefundsChart = null;
    let customerMixChart = null;

    const moneyFormatter = new Intl.NumberFormat('vi-VN', {
        style: 'currency',
        currency: 'VND',
        maximumFractionDigits: 0
    });

    const numberFormatter = new Intl.NumberFormat('vi-VN', {
        maximumFractionDigits: 0
    });

    function formatMoney(value) {
        return moneyFormatter.format(Number(value || 0));
    }

    function formatNumber(value) {
        return numberFormatter.format(Number(value || 0));
    }

    function formatPercent(value) {
        return `${Number(value || 0).toLocaleString('vi-VN', {
            maximumFractionDigits: 1
        })}%`;
    }


    function cssVar(name, fallback) {
        const value = getComputedStyle(document.documentElement)
            .getPropertyValue(name)
            .trim();
        return value || fallback;
    }

    function chartPalette() {
        return {
            primary: cssVar('--bs-primary', '#696cff'),
            secondary: cssVar('--bs-secondary', '#8592a3'),
            success: cssVar('--bs-success', '#71dd37'),
            warning: cssVar('--bs-warning', '#ffab00'),
            danger: cssVar('--bs-danger', '#ff3e1d'),
            info: cssVar('--bs-info', '#03c3ec'),
            border: cssVar('--sales-chart-grid', '#e8ecf2'),
            body: cssVar('--sales-chart-ink', '#344054'),
            axis: cssVar('--sales-chart-axis', '#526071'),
            muted: cssVar('--sales-chart-muted', '#667085')
        };
    }

    function chartFontFamily() {
        return getComputedStyle(document.body).fontFamily || 'system-ui, sans-serif';
    }

    function chartBase(height) {
        const palette = chartPalette();
        return {
            chart: {
                height,
                toolbar: { show: false },
                fontFamily: chartFontFamily(),
                foreColor: palette.axis,
                parentHeightOffset: 0,
                animations: {
                    enabled: !window.matchMedia('(prefers-reduced-motion: reduce)').matches,
                    easing: 'easeinout',
                    speed: 360
                }
            },
            grid: {
                borderColor: palette.border,
                strokeDashArray: 3,
                padding: { left: 8, right: 12, top: 4, bottom: 2 }
            },
            dataLabels: { enabled: false },
            tooltip: {
                theme: document.documentElement.dataset.bsTheme === 'dark' ? 'dark' : 'light'
            },
            noData: {
                text: 'Chưa có dữ liệu trong kỳ',
                style: { color: palette.muted, fontSize: '13px', fontWeight: 600 }
            }
        };
    }

    function compactNumber(value) {
        const number = Number(value || 0);
        const absolute = Math.abs(number);
        const signed = divisor => (number / divisor).toLocaleString('vi-VN', {
            maximumFractionDigits: 1
        });

        if (absolute >= 1_000_000_000) return `${signed(1_000_000_000)} tỷ`;
        if (absolute >= 1_000_000) return `${signed(1_000_000)} tr`;
        if (absolute >= 1_000) return `${signed(1_000)}K`;
        return number.toLocaleString('vi-VN', { maximumFractionDigits: 1 });
    }

    function hourAxisLabel(value) {
        const hour = Number.parseInt(String(value || '').slice(0, 2), 10);
        if (!Number.isFinite(hour) || hour % 2 !== 0) return '';
        return `${String(hour).padStart(2, '0')}h`;
    }

    function renderChartEmpty(container, iconClass, title, message) {
        container.innerHTML = `
            <div class="sales-report-chart-empty" role="status">
                <span class="sales-report-chart-empty-icon"><i class="${iconClass}" aria-hidden="true"></i></span>
                <strong>${title}</strong>
                <span>${message}</span>
            </div>`;
    }

    function dateInputValue(value) {
        if (!value) return '';
        return String(value).slice(0, 10);
    }

    function formatDisplayDate(value) {
        if (!value) return '';
        const parts = dateInputValue(value).split('-');
        if (parts.length !== 3) return dateInputValue(value);
        return `${parts[2]}/${parts[1]}/${parts[0]}`;
    }

    function readInitialQueryState() {
        const params = new URLSearchParams(window.location.search);
        const fromDate = params.get('fromDate');
        const toDate = params.get('toDate');
        const compare = params.get('compare');
        const terminalId = params.get('terminalId');
        const customerState = params.get('customerState');

        els.fromDate.value = fromDate || '';
        els.toDate.value = toDate || '';
        els.compare.value = compare === 'none' ? 'none' : 'previous';
        els.customerState.value = customerState === 'linked' || customerState === 'guest'
            ? customerState
            : 'all';

        if (terminalId) {
            const terminalExists = [...els.terminal.options]
                .some(option => option.value === terminalId);

            if (terminalExists) {
                els.terminal.value = terminalId;
                delete els.terminal.dataset.pendingValue;
            } else {
                els.terminal.value = '';
                els.terminal.dataset.pendingValue = terminalId;
            }
        } else {
            els.terminal.value = '';
            delete els.terminal.dataset.pendingValue;
        }
    }

    function buildRequestUrl() {
        const url = new URL(dataUrl, window.location.origin);

        if (els.fromDate.value) url.searchParams.set('fromDate', els.fromDate.value);
        if (els.toDate.value) url.searchParams.set('toDate', els.toDate.value);
        url.searchParams.set('compare', els.compare.value || 'previous');

        const terminalValue = els.terminal.value || els.terminal.dataset.pendingValue;
        if (terminalValue) {
            url.searchParams.set('terminalId', terminalValue);
        }

        if (els.customerState.value && els.customerState.value !== 'all') {
            url.searchParams.set('customerState', els.customerState.value);
        }

        return url;
    }

    function syncBrowserUrl() {
        const params = new URLSearchParams();

        if (els.fromDate.value) params.set('fromDate', els.fromDate.value);
        if (els.toDate.value) params.set('toDate', els.toDate.value);
        if (els.compare.value && els.compare.value !== 'previous') {
            params.set('compare', els.compare.value);
        }
        if (els.terminal.value) params.set('terminalId', els.terminal.value);
        if (els.customerState.value && els.customerState.value !== 'all') {
            params.set('customerState', els.customerState.value);
        }

        const query = params.toString();
        const next = `${window.location.pathname}${query ? `?${query}` : ''}`;
        window.history.replaceState(null, '', next);
    }

    function setLoading(isLoading) {
        root.setAttribute('aria-busy', isLoading ? 'true' : 'false');
        els.refresh.disabled = isLoading;
        els.refresh.querySelector('i')?.classList.toggle('bx-spin', isLoading);

        if (!lastGoodData) {
            els.skeleton.hidden = !isLoading;
            if (isLoading) {
                els.statePanel.hidden = true;
                els.content.hidden = true;
            }
        }
    }

    function showState(title, message, { retry = true } = {}) {
        els.skeleton.hidden = true;
        els.content.hidden = true;
        els.statePanel.hidden = false;
        els.stateTitle.textContent = title;
        els.stateMessage.textContent = message;
        els.stateRetry.hidden = !retry;
    }

    function hideState() {
        els.statePanel.hidden = true;
    }

    function announce(message) {
        els.liveRegion.textContent = '';
        window.requestAnimationFrame(() => {
            els.liveRegion.textContent = message;
        });
    }

    function applyResolvedQuery(data) {
        const query = data.query || {};
        els.fromDate.value = dateInputValue(query.fromDate || data.currentPeriod?.fromDate);
        els.toDate.value = dateInputValue(query.toDate || data.currentPeriod?.toDate);
        els.compare.value = query.compare === 'none' ? 'none' : 'previous';
        els.customerState.value = query.customerState || 'all';

        renderTerminalOptions(data.terminals || [], query.terminalId);
        updateSecondaryFilterCount();
        syncBrowserUrl();
    }

    function renderTerminalOptions(options, selectedId) {
        const pending = els.terminal.dataset.pendingValue;
        const desired = String(selectedId || pending || els.terminal.value || '');

        els.terminal.replaceChildren();
        els.terminal.append(new Option('Tất cả terminal', ''));

        options.forEach(item => {
            const label = item.code
                ? `${item.code} · ${item.name}`
                : item.name;
            els.terminal.append(new Option(label, String(item.id)));
        });

        if ([...els.terminal.options].some(option => option.value === desired)) {
            els.terminal.value = desired;
        }

        delete els.terminal.dataset.pendingValue;
    }

    function updateSecondaryFilterCount() {
        let count = 0;
        if (els.terminal.value) count += 1;
        if (els.customerState.value && els.customerState.value !== 'all') count += 1;

        els.filterCount.hidden = count === 0;
        els.filterCount.textContent = String(count);
    }

    function renderKpi(id, value, formatter) {
        const element = document.getElementById(id);
        if (element) element.textContent = formatter(value);
    }

    function renderDelta(key, delta, hasComparison) {
        const element = document.querySelector(`[data-kpi-delta="${key}"]`);
        if (!element) return;

        element.classList.remove('is-up', 'is-down', 'is-new');

        if (!hasComparison || !delta) {
            element.textContent = 'Không so sánh';
            return;
        }

        if (delta.state === 'new') {
            element.classList.add('is-new');
            element.innerHTML = '<i class="bx bx-sparkles" aria-hidden="true"></i><span>Kỳ trước = 0</span>';
            return;
        }

        const percent = delta.percentChange;
        if (percent == null || Number(percent) === 0) {
            element.textContent = 'Không đổi so với kỳ trước';
            return;
        }

        const isUp = Number(delta.difference) > 0;
        element.classList.add(isUp ? 'is-up' : 'is-down');
        element.innerHTML = `<i class="bx ${isUp ? 'bx-up-arrow-alt' : 'bx-down-arrow-alt'}" aria-hidden="true"></i><span>${Math.abs(Number(percent)).toLocaleString('vi-VN', { maximumFractionDigits: 1 })}% so với kỳ trước</span>`;
    }

    function renderBridge(bridge) {
        document.getElementById('salesBridgeGross').textContent = formatMoney(bridge.grossSales);
        document.getElementById('salesBridgeDiscounts').textContent = formatMoney(bridge.discounts);
        document.getElementById('salesBridgeAfterDiscount').textContent = formatMoney(bridge.salesAfterDiscount);
        document.getElementById('salesBridgeReturns').textContent = formatMoney(bridge.returns);
        document.getElementById('salesBridgeNet').textContent = formatMoney(bridge.netSales);
    }

    function renderAttention(summary) {
        document.getElementById('salesAttentionVoid').textContent =
            `${formatNumber(summary.voidCount)} · ${formatMoney(summary.voidValue)}`;
        document.getElementById('salesAttentionReturns').textContent =
            formatNumber(summary.returnCount);
        document.getElementById('salesAttentionCustomerRate').textContent =
            formatPercent(summary.customerLinkedRate);

        const hasAttention = Number(summary.voidCount) > 0 || Number(summary.returnCount) > 0;
        document.querySelector('.sales-report-attention')?.classList.toggle('has-attention', hasAttention);
        document.getElementById('salesReportAttentionMessage').textContent = hasAttention
            ? 'Có nghiệp vụ return hoặc đơn đã Void trong cohort bán của kỳ. Nên kiểm tra chi tiết khi Sales Detail được mở.'
            : 'Không có return hoặc đơn đã Void trong cohort bán của kỳ hiện tại.';
    }

    function renderTrendTable(points) {
        els.trendDataBody.replaceChildren();

        points.forEach(point => {
            const row = document.createElement('tr');
            const label = document.createElement('td');
            const value = document.createElement('td');
            const orders = document.createElement('td');

            const bucketLink = document.createElement('button');
            bucketLink.type = 'button';
            bucketLink.className = 'btn btn-link p-0 text-start';
            bucketLink.textContent = point.label;
            bucketLink.addEventListener('click', () => openSalesDetail('orders', { bucketIndex: Number(point.bucketIndex) }));
            label.append(bucketLink);
            value.textContent = formatMoney(point.netSales);
            orders.textContent = formatNumber(point.salesOrders);
            value.className = 'text-end';
            orders.className = 'text-end';

            row.append(label, value, orders);
            els.trendDataBody.append(row);
        });
    }

    function renderTrend(data) {
        const current = data.current?.trend || [];
        const comparison = data.comparison?.trend || [];
        const hasComparison = Boolean(data.comparisonPeriod);
        const palette = chartPalette();
        const base = chartBase(350);

        const from = formatDisplayDate(data.currentPeriod?.fromDate);
        const to = formatDisplayDate(data.currentPeriod?.toDate);
        els.trendPeriodText.textContent = from === to
            ? `Ngày ${from}`
            : `${from} → ${to}`;

        renderTrendTable(current);

        if (trendChart) {
            trendChart.destroy();
            trendChart = null;
        }

        if (!window.ApexCharts) {
            els.trendChart.innerHTML = '<div class="alert alert-warning mb-0">Không tải được thư viện biểu đồ. Dữ liệu dạng bảng vẫn khả dụng bên dưới.</div>';
            return;
        }

        const series = [{
            name: 'Kỳ hiện tại',
            data: current.map(point => Number(point.netSales || 0))
        }];

        if (hasComparison) {
            series.push({
                name: 'Kỳ trước',
                data: comparison.map(point => Number(point.netSales || 0))
            });
        }

        trendChart = new ApexCharts(els.trendChart, {
            ...base,
            chart: {
                ...base.chart,
                type: 'area'
            },
            colors: [palette.primary, palette.secondary],
            series,
            stroke: {
                curve: 'smooth',
                width: hasComparison ? [3, 2] : 3,
                dashArray: hasComparison ? [0, 6] : 0,
                lineCap: 'round'
            },
            fill: {
                type: 'gradient',
                gradient: {
                    shadeIntensity: .15,
                    opacityFrom: hasComparison ? .26 : .3,
                    opacityTo: .025,
                    stops: [0, 82, 100]
                }
            },
            markers: {
                size: 0,
                hover: { size: 5 }
            },
            xaxis: {
                categories: current.map(point => point.label),
                axisBorder: { show: false },
                axisTicks: { show: false },
                labels: {
                    rotate: current.length > 16 ? -28 : 0,
                    hideOverlappingLabels: true,
                    trim: false,
                    style: {
                        colors: palette.axis,
                        fontSize: '12px',
                        fontWeight: 600
                    }
                }
            },
            yaxis: {
                labels: {
                    formatter: compactNumber,
                    style: {
                        colors: [palette.axis],
                        fontSize: '12px',
                        fontWeight: 600
                    }
                }
            },
            legend: {
                show: hasComparison,
                position: 'top',
                horizontalAlign: 'right',
                fontSize: '12px',
                fontWeight: 600,
                labels: { colors: palette.axis },
                markers: { width: 8, height: 8, radius: 8 }
            },
            tooltip: {
                ...base.tooltip,
                shared: true,
                intersect: false,
                y: { formatter: value => formatMoney(value) }
            }
        });

        trendChart.render();
    }

    function resetChart(chart) {
        if (chart) chart.destroy();
        return null;
    }

    function renderSalesByHourTable(points) {
        els.salesByHourDataBody.replaceChildren();

        points.forEach(point => {
            const row = document.createElement('tr');
            const hour = document.createElement('td');
            const netSales = document.createElement('td');
            const orders = document.createElement('td');

            hour.textContent = point.label;
            netSales.textContent = formatMoney(point.netSales);
            orders.textContent = formatNumber(point.salesOrders);
            netSales.className = 'text-end';
            orders.className = 'text-end';

            row.append(hour, netSales, orders);
            els.salesByHourDataBody.append(row);
        });
    }

    function renderSalesByHour(data) {
        const points = data.current?.salesByHour || [];
        const metric = els.salesByHourMetric.value === 'salesOrders'
            ? 'salesOrders'
            : 'netSales';
        const isOrders = metric === 'salesOrders';
        const palette = chartPalette();
        const base = chartBase(280);

        renderSalesByHourTable(points);
        salesByHourChart = resetChart(salesByHourChart);

        const peak = points.reduce((best, point) => {
            if (!best) return point;
            return Number(point[metric] || 0) > Number(best[metric] || 0)
                ? point
                : best;
        }, null);

        els.salesByHourPeakText.textContent = peak && Number(peak[metric] || 0) > 0
            ? `Khung giờ nổi bật: ${peak.label} · ${isOrders ? formatNumber(peak.salesOrders) + ' đơn' : formatMoney(peak.netSales)}`
            : 'Chưa có khung giờ nổi bật trong kỳ.';

        if (!window.ApexCharts) {
            els.salesByHourChart.textContent = 'Không tải được thư viện biểu đồ. Dữ liệu dạng bảng vẫn khả dụng.';
            return;
        }

        salesByHourChart = new ApexCharts(els.salesByHourChart, {
            ...base,
            chart: {
                ...base.chart,
                type: 'bar'
            },
            colors: [palette.primary],
            series: [{
                name: isOrders ? 'Đơn bán' : 'Net Sales',
                data: points.map(point => Number(point[metric] || 0))
            }],
            plotOptions: {
                bar: {
                    borderRadius: 7,
                    borderRadiusApplication: 'end',
                    columnWidth: '48%'
                }
            },
            fill: { opacity: 0.92 },
            xaxis: {
                categories: points.map(point => point.label),
                axisBorder: { show: false },
                axisTicks: { show: false },
                labels: {
                    rotate: 0,
                    trim: false,
                    hideOverlappingLabels: false,
                    formatter: hourAxisLabel,
                    style: {
                        colors: palette.axis,
                        fontSize: '12px',
                        fontWeight: 600
                    }
                }
            },
            yaxis: {
                labels: {
                    formatter: value => isOrders ? formatNumber(value) : compactNumber(value),
                    style: {
                        colors: [palette.axis],
                        fontSize: '12px',
                        fontWeight: 600
                    }
                }
            },
            tooltip: {
                ...base.tooltip,
                y: {
                    formatter: value => isOrders
                        ? `${formatNumber(value)} đơn`
                        : formatMoney(value)
                }
            }
        });

        salesByHourChart.render();
    }

    function renderTopProducts(data) {
        const products = data.current?.topProducts || [];
        const palette = chartPalette();
        const base = chartBase(Math.max(260, products.length * 50));

        els.topProductsDataBody.replaceChildren();
        topProductsChart = resetChart(topProductsChart);

        products.forEach(product => {
            const row = document.createElement('tr');
            const identity = document.createElement('td');
            const quantity = document.createElement('td');
            const gross = document.createElement('td');
            const share = document.createElement('td');

            const title = document.createElement('div');
            const meta = document.createElement('small');
            const productLink = document.createElement('button');
            productLink.type = 'button';
            productLink.className = 'btn btn-link p-0 text-start';
            productLink.textContent = product.itemName || `Variant #${product.variantId}`;
            productLink.addEventListener('click', () => openSalesDetail('products', { variantId: Number(product.variantId) }));
            title.append(productLink);
            meta.textContent = product.sku ? `SKU: ${product.sku}` : `Variant #${product.variantId}`;
            meta.className = 'text-muted';
            identity.append(title, meta);

            quantity.textContent = `${Number(product.baseQuantity || 0).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} ${product.baseUnitName || ''}`.trim();
            gross.textContent = formatMoney(product.grossSales);
            share.textContent = formatPercent(product.grossSalesShare);
            quantity.className = 'text-end';
            gross.className = 'text-end';
            share.className = 'text-end';

            row.append(identity, quantity, gross, share);
            els.topProductsDataBody.append(row);
        });

        if (!window.ApexCharts) {
            els.topProductsChart.textContent = 'Không tải được thư viện biểu đồ. Dữ liệu dạng bảng vẫn khả dụng.';
            return;
        }

        topProductsChart = new ApexCharts(els.topProductsChart, {
            ...base,
            chart: {
                ...base.chart,
                type: 'bar'
            },
            colors: [palette.primary],
            series: [{
                name: 'Gross Sales',
                data: products.map(product => Number(product.grossSales || 0))
            }],
            plotOptions: {
                bar: {
                    horizontal: true,
                    borderRadius: 7,
                    borderRadiusApplication: 'end',
                    barHeight: '48%',
                    dataLabels: { position: 'top' }
                }
            },
            fill: { opacity: 0.9 },
            xaxis: {
                categories: products.map(product => product.itemName || `Variant #${product.variantId}`),
                axisBorder: { show: false },
                axisTicks: { show: false },
                labels: {
                    formatter: compactNumber,
                    style: {
                        colors: palette.axis,
                        fontSize: '12px',
                        fontWeight: 600
                    }
                }
            },
            yaxis: {
                labels: {
                    maxWidth: 215,
                    style: {
                        colors: [palette.body],
                        fontSize: '12px',
                        fontWeight: 650
                    }
                }
            },
            dataLabels: {
                enabled: true,
                textAnchor: 'start',
                offsetX: 8,
                formatter: value => compactNumber(value),
                style: {
                    fontSize: '12px',
                    fontWeight: 700,
                    colors: [palette.body]
                },
                background: {
                    enabled: true,
                    foreColor: palette.body,
                    borderRadius: 6,
                    padding: 4,
                    opacity: 0.88,
                    borderWidth: 0
                }
            },
            tooltip: {
                ...base.tooltip,
                y: { formatter: value => formatMoney(value) }
            },
            noData: {
                ...base.noData,
                text: 'Chưa có sản phẩm trong kỳ'
            }
        });

        topProductsChart.render();
    }

    function renderDiscountBreakdown(data) {
        const breakdown = data.current?.discountBreakdown || {};
        const palette = chartPalette();
        const rows = [
            ['Manual line', breakdown.manualLine],
            ['Promotion', breakdown.promotion],
            ['Combo', breakdown.combo],
            ['Manual order', breakdown.manualOrder],
            ['Voucher', breakdown.voucher]
        ];

        els.discountTotal.textContent = formatMoney(breakdown.totalDiscounts);
        els.discountReconcile.classList.toggle('is-ok', Boolean(breakdown.isReconciled));
        els.discountReconcile.classList.toggle('is-mismatch', !breakdown.isReconciled);
        els.discountReconcile.textContent = breakdown.isReconciled
            ? `Đã đối soát: 5 thành phần = ${formatMoney(breakdown.totalDiscounts)}.`
            : `Chênh lệch đối soát ${formatMoney(breakdown.reconciliationDifference)}. Không cộng lại các component vào Discounts.`;

        els.discountDataBody.replaceChildren();
        rows.forEach(([name, value]) => {
            const row = document.createElement('tr');
            const label = document.createElement('td');
            const amount = document.createElement('td');
            label.textContent = name;
            amount.textContent = formatMoney(value);
            amount.className = 'text-end';
            row.append(label, amount);
            els.discountDataBody.append(row);
        });

        discountChart = resetChart(discountChart);
        if (!window.ApexCharts) {
            els.discountChart.textContent = 'Không tải được thư viện biểu đồ. Dữ liệu dạng bảng vẫn khả dụng.';
            return;
        }

        const nonZero = rows
            .map(([name, value]) => ({ name, value: Number(value || 0) }))
            .filter(item => item.value > 0);

        const total = Number(breakdown.totalDiscounts || 0);

        if (!nonZero.length) {
            renderChartEmpty(
                els.discountChart,
                'bx bx-purchase-tag-alt',
                'Không có giảm giá',
                'Kỳ đang chọn không phát sinh discount.'
            );
            return;
        }

        discountChart = new ApexCharts(els.discountChart, {
            chart: {
                type: 'donut',
                height: 278,
                fontFamily: chartFontFamily(),
                foreColor: palette.axis,
                animations: {
                    enabled: !window.matchMedia('(prefers-reduced-motion: reduce)').matches
                }
            },
            colors: [palette.primary, palette.info, palette.warning, palette.secondary, palette.success],
            series: nonZero.length ? nonZero.map(item => item.value) : [],
            labels: nonZero.map(item => item.name),
            stroke: {
                width: 3,
                colors: [cssVar('--bs-card-bg', '#ffffff')]
            },
            legend: {
                position: 'bottom',
                fontSize: '12px',
                fontWeight: 600,
                labels: { colors: palette.axis },
                itemMargin: { horizontal: 9, vertical: 5 }
            },
            plotOptions: {
                pie: {
                    donut: {
                        size: '70%',
                        labels: {
                            show: true,
                            name: { show: true, fontSize: '12px', fontWeight: 600, color: palette.axis },
                            value: {
                                show: true,
                                fontSize: '20px',
                                fontWeight: 750,
                                color: palette.body,
                                color: palette.body,
                                formatter: value => compactNumber(value)
                            },
                            total: {
                                show: true,
                                label: 'Discounts',
                                fontSize: '12px',
                                fontWeight: 600,
                                color: palette.axis,
                                formatter: () => compactNumber(total)
                            }
                        }
                    }
                }
            },
            dataLabels: { enabled: false },
            tooltip: {
                theme: document.documentElement.dataset.bsTheme === 'dark' ? 'dark' : 'light',
                y: { formatter: value => formatMoney(value) }
            },
            noData: {
                text: 'Không có giảm giá trong kỳ',
                style: { color: palette.muted }
            }
        });

        discountChart.render();
    }

    function renderReturnsRefunds(data) {
        const points = data.current?.trend || [];
        const summary = data.current?.summary || {};
        const palette = chartPalette();
        const base = chartBase(285);

        els.returnCountInline.textContent = formatNumber(summary.returnCount);
        els.refundCountInline.textContent = formatNumber(summary.refundCount);
        els.returnsRefundsDataBody.replaceChildren();

        points.forEach(point => {
            const row = document.createElement('tr');
            const label = document.createElement('td');
            const returns = document.createElement('td');
            const refunds = document.createElement('td');
            label.textContent = point.label;
            returns.textContent = formatMoney(point.returns);
            refunds.textContent = formatMoney(point.refundAmount);
            returns.className = 'text-end';
            refunds.className = 'text-end';
            row.append(label, returns, refunds);
            els.returnsRefundsDataBody.append(row);
        });

        returnsRefundsChart = resetChart(returnsRefundsChart);
        if (!window.ApexCharts) {
            els.returnsRefundsChart.textContent = 'Không tải được thư viện biểu đồ. Dữ liệu dạng bảng vẫn khả dụng.';
            return;
        }

        returnsRefundsChart = new ApexCharts(els.returnsRefundsChart, {
            ...base,
            chart: {
                ...base.chart,
                type: 'line'
            },
            colors: [palette.danger, palette.warning],
            series: [
                { name: 'Returns', data: points.map(point => Number(point.returns || 0)) },
                { name: 'Refund Amount', data: points.map(point => Number(point.refundAmount || 0)) }
            ],
            stroke: {
                curve: 'smooth',
                width: [3, 2],
                dashArray: [0, 5],
                lineCap: 'round'
            },
            markers: {
                size: 0,
                hover: { size: 5 }
            },
            xaxis: {
                categories: points.map(point => point.label),
                axisBorder: { show: false },
                axisTicks: { show: false },
                labels: {
                    trim: false,
                    style: {
                        colors: palette.axis,
                        fontSize: '12px',
                        fontWeight: 600
                    }
                }
            },
            yaxis: {
                labels: {
                    formatter: compactNumber,
                    style: {
                        colors: [palette.axis],
                        fontSize: '12px',
                        fontWeight: 600
                    }
                }
            },
            legend: {
                position: 'top',
                horizontalAlign: 'right',
                fontSize: '12px',
                fontWeight: 600,
                labels: { colors: palette.axis }
            },
            tooltip: {
                ...base.tooltip,
                shared: true,
                intersect: false,
                y: { formatter: value => formatMoney(value) }
            },
            noData: {
                ...base.noData,
                text: 'Không có return/refund trong kỳ'
            }
        });

        returnsRefundsChart.render();
    }

    function renderCustomerMix(data) {
        const mix = data.current?.customerMix || {};
        const palette = chartPalette();
        const rows = [
            ['Có khách hàng', Number(mix.linkedOrders || 0), Number(mix.linkedRate || 0)],
            ['Khách lẻ', Number(mix.guestOrders || 0), Number(mix.guestRate || 0)]
        ];

        els.customerLinkedRateInline.textContent = formatPercent(mix.linkedRate);
        els.customerLinkedOrders.textContent = formatNumber(mix.linkedOrders);
        els.customerGuestOrders.textContent = formatNumber(mix.guestOrders);
        els.customerMixDataBody.replaceChildren();

        rows.forEach(([name, orders, rate]) => {
            const row = document.createElement('tr');
            const label = document.createElement('td');
            const orderCell = document.createElement('td');
            const rateCell = document.createElement('td');
            label.textContent = name;
            orderCell.textContent = formatNumber(orders);
            rateCell.textContent = formatPercent(rate);
            orderCell.className = 'text-end';
            rateCell.className = 'text-end';
            row.append(label, orderCell, rateCell);
            els.customerMixDataBody.append(row);
        });

        customerMixChart = resetChart(customerMixChart);
        if (!window.ApexCharts) {
            els.customerMixChart.textContent = 'Không tải được thư viện biểu đồ. Dữ liệu dạng bảng vẫn khả dụng.';
            return;
        }

        const totalOrders = rows.reduce((sum, row) => sum + row[1], 0);

        customerMixChart = new ApexCharts(els.customerMixChart, {
            chart: {
                type: 'donut',
                height: 235,
                fontFamily: chartFontFamily(),
                foreColor: palette.axis,
                animations: {
                    enabled: !window.matchMedia('(prefers-reduced-motion: reduce)').matches
                }
            },
            colors: [palette.primary, palette.secondary],
            series: totalOrders > 0 ? rows.map(row => row[1]) : [],
            labels: rows.map(row => row[0]),
            stroke: {
                width: 3,
                colors: [cssVar('--bs-card-bg', '#ffffff')]
            },
            legend: {
                position: 'bottom',
                fontSize: '12px',
                fontWeight: 600,
                labels: { colors: palette.axis },
                itemMargin: { horizontal: 9, vertical: 5 }
            },
            plotOptions: {
                pie: {
                    donut: {
                        size: '72%',
                        labels: {
                            show: true,
                            name: { show: true, fontSize: '12px', fontWeight: 600, color: palette.axis },
                            value: {
                                show: true,
                                fontSize: '18px',
                                fontWeight: 750,
                                formatter: value => formatNumber(value)
                            },
                            total: {
                                show: true,
                                label: 'Tỷ lệ gắn KH',
                                fontSize: '12px',
                                fontWeight: 600,
                                color: palette.axis,
                                formatter: () => formatPercent(mix.linkedRate)
                            }
                        }
                    }
                }
            },
            dataLabels: { enabled: false },
            tooltip: {
                theme: document.documentElement.dataset.bsTheme === 'dark' ? 'dark' : 'light',
                y: { formatter: value => `${formatNumber(value)} đơn` }
            },
            noData: {
                text: 'Chưa có đơn bán trong kỳ',
                style: { color: palette.muted }
            }
        });

        customerMixChart.render();
    }

    function renderDashboard(data) {
        const summary = data.current.summary;
        const hasComparison = Boolean(data.comparisonPeriod);

        renderKpi('salesKpiNetSales', summary.netSales, formatMoney);
        renderKpi('salesKpiOrders', summary.salesOrders, formatNumber);
        renderKpi('salesKpiAov', summary.aov, formatMoney);
        renderKpi('salesKpiDiscounts', summary.discounts, formatMoney);
        renderKpi('salesKpiReturns', summary.returns, formatMoney);
        renderKpi('salesKpiRefundAmount', summary.refundAmount, formatMoney);

        Object.entries(data.comparisons || {}).forEach(([key, delta]) => {
            renderDelta(key, delta, hasComparison);
        });

        renderBridge(data.current.bridge);
        renderAttention(summary);
        renderTrend(data);
        renderSalesByHour(data);
        renderTopProducts(data);
        renderDiscountBreakdown(data);
        renderReturnsRefunds(data);
        renderCustomerMix(data);

        const generatedAt = data.generatedAtUtc
            ? new Date(data.generatedAtUtc)
            : new Date();
        els.lastRefresh.textContent = generatedAt.toLocaleTimeString('vi-VN', {
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });

        els.skeleton.hidden = true;
        hideState();
        els.content.hidden = false;
        root.setAttribute('aria-busy', 'false');
    }

    function isZeroData(data) {
        const summary = data?.current?.summary;
        if (!summary) return true;

        return Number(summary.salesOrders || 0) === 0
            && Number(summary.returnCount || 0) === 0
            && Number(summary.voidCount || 0) === 0;
    }

    async function readErrorMessage(response) {
        try {
            const body = await response.json();
            return body?.message || `Không tải được báo cáo (${response.status}).`;
        } catch {
            return `Không tải được báo cáo (${response.status}).`;
        }
    }

    async function loadReport({ userInitiated = false } = {}) {
        const sequence = ++requestSequence;

        if (activeController) activeController.abort();
        activeController = new AbortController();

        setLoading(true);
        els.staleBanner.hidden = true;

        try {
            const response = await fetch(buildRequestUrl(), {
                method: 'GET',
                headers: { Accept: 'application/json' },
                signal: activeController.signal
            });

            if (!response.ok) {
                throw new Error(await readErrorMessage(response));
            }

            const data = await response.json();
            if (sequence !== requestSequence) return;

            applyResolvedQuery(data);
            lastGoodData = data;

            if (isZeroData(data)) {
                els.skeleton.hidden = true;
                els.content.hidden = true;
                showState(
                    'Chưa có giao dịch trong kỳ',
                    'Không có đơn bán, return hoặc đơn đã Void trong khoảng thời gian và bộ lọc hiện tại.',
                    { retry: false });
            } else {
                renderDashboard(data);
            }

            announce(userInitiated
                ? 'Đã cập nhật báo cáo bán hàng.'
                : 'Báo cáo bán hàng đã sẵn sàng.');
        } catch (error) {
            if (error?.name === 'AbortError') return;
            if (sequence !== requestSequence) return;

            const message = error?.message || 'Không tải được báo cáo.';

            if (lastGoodData) {
                els.staleBanner.hidden = false;
                els.content.hidden = false;
                hideState();
                announce('Không tải được dữ liệu mới. Đang giữ kết quả gần nhất.');
            } else {
                showState('Không tải được báo cáo', message, { retry: true });
                announce('Không tải được báo cáo bán hàng.');
            }
        } finally {
            if (sequence === requestSequence) {
                setLoading(false);
            }
        }
    }

    function onFilterChanged() {
        updateSecondaryFilterCount();
        syncBrowserUrl();
        loadReport({ userInitiated: true });
    }

    els.salesByHourMetric.addEventListener('change', () => {
        if (lastGoodData) renderSalesByHour(lastGoodData);
    });

    els.secondaryToggle.addEventListener('click', () => {
        const isOpen = !els.secondaryFilters.hidden;
        els.secondaryFilters.hidden = isOpen;
        els.secondaryToggle.setAttribute('aria-expanded', String(!isOpen));
    });

    els.clearSecondary.addEventListener('click', () => {
        els.terminal.value = '';
        els.customerState.value = 'all';
        onFilterChanged();
    });

    [els.fromDate, els.toDate, els.compare, els.terminal, els.customerState]
        .forEach(element => element.addEventListener('change', onFilterChanged));

    els.refresh.addEventListener('click', () => {
        loadReport({ userInitiated: true });
    });

    els.stateRetry.addEventListener('click', () => {
        loadReport({ userInitiated: true });
    });

    window.addEventListener('popstate', () => {
        readInitialQueryState();
        loadReport();
    });

    readInitialQueryState();
    updateSecondaryFilterCount();
    loadReport();
})();


(() => {
    'use strict';
    const root = document.querySelector('[data-sales-detail-report]');
    if (!root) return;

    const urls = {
        context: root.dataset.contextUrl,
        orders: root.dataset.ordersUrl,
        products: root.dataset.productsUrl,
        discounts: root.dataset.discountsUrl,
        returns: root.dataset.returnsUrl,
        void: root.dataset.voidsUrl,
        overview: root.dataset.overviewDataUrl,
        orderDetail: root.dataset.orderDetailBaseUrl
    };
    const els = {
        from: document.getElementById('salesDetailFromDate'), to: document.getElementById('salesDetailToDate'),
        terminal: document.getElementById('salesDetailTerminal'), customer: document.getElementById('salesDetailCustomerState'),
        refresh: document.getElementById('salesDetailRefreshButton'), drill: document.getElementById('salesDetailDrillContext'),
        tabs: [...root.querySelectorAll('[data-sales-detail-segment]')], overview: document.getElementById('salesDetailOverview'),
        dataSection: document.getElementById('salesDetailDataSection'), title: document.getElementById('salesDetailTitle'),
        subtitle: document.getElementById('salesDetailSubtitle'), search: document.getElementById('salesDetailSearch'),
        sort: document.getElementById('salesDetailSort'), pageSize: document.getElementById('salesDetailPageSize'),
        loading: document.getElementById('salesDetailLoading'), state: document.getElementById('salesDetailState'),
        stateTitle: document.getElementById('salesDetailStateTitle'), stateMessage: document.getElementById('salesDetailStateMessage'),
        tableWrap: document.getElementById('salesDetailTableWrap'), head: document.getElementById('salesDetailTableHead'),
        body: document.getElementById('salesDetailTableBody'), pageMeta: document.getElementById('salesDetailPageMeta'),
        prev: document.getElementById('salesDetailPrev'), next: document.getElementById('salesDetailNext'), live: document.getElementById('salesDetailLiveRegion')
    };
    let activeSegment = 'overview';
    let page = 1;
    let totalPages = 1;
    let requestSequence = 0;
    let controller = null;
    let refreshSequence = 0;
    let contextReady = false;
    const money = value => `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }).format(Number(value || 0))} ₫`;
    const number = value => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(Number(value || 0));
    const dateTime = value => value ? new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) : '—';
    const params = () => new URLSearchParams(window.location.search);

    function syncFromUrl() {
        const q = params();
        activeSegment = ['overview','orders','products','discounts','returns','void'].includes(q.get('segment')) ? q.get('segment') : 'overview';
        page = Math.max(1, Number(q.get('page') || 1));
        els.search.value = q.get('search') || '';
        els.pageSize.value = ['10','25','50','100'].includes(q.get('pageSize')) ? q.get('pageSize') : '25';
    }

    function buildQuery({ includePaging = true } = {}) {
        const q = new URLSearchParams();
        if (els.from.value) q.set('fromDate', els.from.value);
        if (els.to.value) q.set('toDate', els.to.value);
        if (els.terminal.value) q.set('terminalId', els.terminal.value);
        if (els.customer.value && els.customer.value !== 'all') q.set('customerState', els.customer.value);
        const current = params();
        if (current.get('bucketIndex')) q.set('bucketIndex', current.get('bucketIndex'));
        if (current.get('variantId')) q.set('variantId', current.get('variantId'));
        if (current.get('focus')) q.set('focus', current.get('focus'));
        if (includePaging) {
            if (els.search.value.trim()) q.set('search', els.search.value.trim());
            q.set('page', String(page));
            q.set('pageSize', els.pageSize.value || '25');
            if (els.sort.value) q.set('sort', els.sort.value);
        }
        return q;
    }

    function syncUrl() {
        const q = buildQuery({ includePaging: activeSegment !== 'overview' });
        q.set('segment', activeSegment);
        const old = params();
        if (old.get('compare')) q.set('compare', old.get('compare'));
        history.replaceState(null, '', `${location.pathname}?${q}`);
        syncOverviewLinks();
    }

    function syncOverviewLinks() {
        const current = params();
        document.querySelectorAll('[data-sales-overview-link]').forEach(link => {
            const url = new URL(link.href, location.origin);
            url.search = '';
            ['fromDate', 'toDate', 'compare', 'terminalId', 'customerState'].forEach(key => {
                if (current.has(key)) url.searchParams.set(key, current.get(key));
            });
            link.href = `${url.pathname}?${url.searchParams}`;
        });
    }

    async function fetchJson(base, query) {
        if (controller) controller.abort();
        controller = new AbortController();
        const seq = ++requestSequence;
        const url = new URL(base, location.origin);
        query.forEach((v,k) => url.searchParams.set(k,v));
        const response = await fetch(url, { headers: { Accept: 'application/json' }, signal: controller.signal });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const data = await response.json();
        if (seq !== requestSequence) throw new DOMException('stale', 'AbortError');
        return data;
    }

    function renderTerminals(items, selected) {
        els.terminal.replaceChildren(new Option('Tất cả terminal',''));
        (items || []).forEach(x => els.terminal.append(new Option(x.code ? `${x.code} · ${x.name}` : x.name, String(x.id))));
        if (selected) els.terminal.value = String(selected);
    }

    async function loadContext() {
        const q = params();
        const data = await fetchJson(urls.context, q);
        els.from.value = String(data.query?.fromDate || data.period?.fromDate || '').slice(0,10);
        els.to.value = String(data.query?.toDate || data.period?.toDate || '').slice(0,10);
        els.customer.value = data.query?.customerState || 'all';
        renderTerminals(data.terminals, data.query?.terminalId);
        renderDrillContext(data);
    }

    function renderDrillContext(data) {
        const current = params();
        const chips = [];
        if (data.bucketLabel) chips.push(`Bucket: ${data.bucketLabel}`);
        if (current.get('variantId')) chips.push(`Variant #${current.get('variantId')}`);
        if (current.get('focus') === 'refund') chips.push('Tập trung: Refund Amount');
        els.drill.replaceChildren();
        chips.forEach(text => { const span=document.createElement('span'); span.className='sales-detail-drill-chip'; span.textContent=text; els.drill.append(span); });
        if (chips.length) { const clear=document.createElement('button'); clear.type='button'; clear.className='btn btn-link btn-sm'; clear.textContent='Xóa drill filter'; clear.onclick=()=>{ const q=params(); ['bucketIndex','variantId','focus'].forEach(k=>q.delete(k)); q.set('page','1'); history.replaceState(null,'',`${location.pathname}?${q}`); loadAll(); }; els.drill.append(clear); }
        els.drill.hidden = chips.length === 0;
    }

    function sortOptions(segment) {
        const options = segment === 'products'
            ? [['value-desc','Doanh thu giảm dần'],['quantity-desc','Số lượng giảm dần'],['name-asc','Tên A → Z']]
            : segment === 'discounts'
                ? [['discount-desc','Giảm giá cao nhất'],['newest','Mới nhất'],['oldest','Cũ nhất']]
                : [['newest','Mới nhất'],['oldest','Cũ nhất'],['value-desc','Giá trị cao nhất'],['value-asc','Giá trị thấp nhất']];
        const desired = params().get('sort') || options[0][0];
        els.sort.replaceChildren(...options.map(([v,t]) => new Option(t,v)));
        els.sort.value = options.some(x=>x[0]===desired) ? desired : options[0][0];
    }

    function setSegment(segment) {
        activeSegment = segment;
        els.tabs.forEach(x => { const active=x.dataset.salesDetailSegment===segment; x.classList.toggle('is-active',active); x.setAttribute('aria-current',active?'page':'false'); });
        els.overview.hidden = !contextReady || segment !== 'overview';
        els.dataSection.hidden = contextReady && segment === 'overview';
        if (segment !== 'overview') sortOptions(segment);
    }

    function td(text, cls='') { const e=document.createElement('td'); e.textContent=text; if(cls)e.className=cls; return e; }
    function orderLink(orderId) { const a=document.createElement('a'); a.className='btn btn-sm btn-label-primary'; a.textContent='Xem đơn'; a.href=`${urls.orderDetail}/${orderId}`; return a; }
    function setHead(labels) { const tr=document.createElement('tr'); labels.forEach(([t,cls])=>{ const th=document.createElement('th'); th.textContent=t; if(cls)th.className=cls; tr.append(th); }); els.head.replaceChildren(tr); }

    const renderers = {
        orders(data) {
            setHead([['Đơn'],['Hoàn tất'],['Khách hàng'],['Gross Sales','text-end'],['Discounts','text-end'],['Sau giảm','text-end'],['Return'],['']]);
            data.items.forEach(x=>{ const tr=document.createElement('tr'); tr.append(td(x.orderNumber),td(dateTime(x.completedAtLocal)),td(x.customerName),td(money(x.subtotal),'text-end'),td(money(x.discounts),'text-end'),td(money(x.salesAfterDiscount),'text-end')); const ret=td(x.returnCount?`${x.returnCount} phiếu · ${money(x.returnValue)}`:'—'); if(x.returnCount)ret.innerHTML=`<span class="sales-detail-return-badge">${x.returnCount} phiếu · ${money(x.returnValue)}</span>`; tr.append(ret); const action=td(''); action.append(orderLink(x.orderId)); tr.append(action); els.body.append(tr); });
        },
        products(data) {
            setHead([['Sản phẩm / Variant'],['SKU'],['Đơn vị gốc'],['Số lượng','text-end'],['Gross Sales','text-end'],['Số đơn','text-end']]);
            data.items.forEach(x=>{ const tr=document.createElement('tr'); tr.append(td(x.itemName),td(x.sku||'—'),td(x.baseUnitName||'Đơn vị gốc'),td(number(x.baseQuantity),'text-end'),td(money(x.grossSales),'text-end'),td(number(x.salesOrders),'text-end')); els.body.append(tr); });
        },
        discounts(data) {
            setHead([['Đơn'],['Hoàn tất'],['Khách hàng'],['Total','text-end'],['Line','text-end'],['Promo','text-end'],['Combo','text-end'],['Order','text-end'],['Voucher','text-end'],['Đối soát'],['']]);
            data.items.forEach(x=>{ const tr=document.createElement('tr'); tr.append(td(x.orderNumber),td(dateTime(x.completedAtLocal)),td(x.customerName),td(money(x.totalDiscounts),'text-end'),td(money(x.manualLine),'text-end'),td(money(x.promotion),'text-end'),td(money(x.combo),'text-end'),td(money(x.manualOrder),'text-end'),td(money(x.voucher),'text-end')); const rec=td(x.isReconciled?'Khớp':'Lệch '+money(x.reconciliationDifference),x.isReconciled?'sales-detail-reconcile-ok':'sales-detail-reconcile-bad'); tr.append(rec); const action=td(''); action.append(orderLink(x.orderId)); tr.append(action); els.body.append(tr); });
        },
        returns(data) {
            setHead([['Phiếu trả'],['Hoàn tất'],['Đơn gốc'],['Khách hàng'],['Loại'],['Giá trị hàng trả','text-end'],['Refund Amount','text-end'],['']]);
            data.items.forEach(x=>{ const tr=document.createElement('tr'); tr.append(td(x.returnNumber),td(dateTime(x.completedAtLocal)),td(x.orderNumber),td(x.customerName),td(x.typeLabel),td(money(x.returnSubtotal),'text-end'),td(money(x.refundAmount),'text-end')); const action=td(''); action.append(orderLink(x.orderId)); tr.append(action); els.body.append(tr); });
        },
        void(data) {
            setHead([['Đơn'],['Thời điểm bán gốc'],['Khách hàng'],['Gross Sales','text-end'],['Discounts','text-end'],['Giá trị Void','text-end'],['']]);
            data.items.forEach(x=>{ const tr=document.createElement('tr'); tr.append(td(x.orderNumber),td(dateTime(x.completedAtLocal)),td(x.customerName),td(money(x.subtotal),'text-end'),td(money(x.discounts),'text-end'),td(money(x.voidValue),'text-end')); const action=td(''); action.append(orderLink(x.orderId)); tr.append(action); els.body.append(tr); });
        }
    };

    const meta = {
        orders:['Đơn bán','Cohort theo thời điểm hoàn tất đơn. Return phát sinh sau được hiển thị như hoạt động riêng.'],
        products:['Sản phẩm','Gross Sales và BaseQuantity + BaseUnit. Product Net Sales chưa được sử dụng trong RPT-1.'],
        discounts:['Giảm giá','Total = Order.DiscountTotal; các thành phần chỉ dùng để giải thích và đối soát.'],
        returns:['Trả hàng & hoàn tiền','Giá trị hàng trả và Refund Amount là hai cột độc lập.'],
        void:['Void','Đơn đã Void trong kỳ bán; thời gian hiển thị là thời điểm bán gốc, không phải thời điểm thao tác Void.']
    };

    async function loadOverview() {
        const q=buildQuery({includePaging:false}); q.set('compare','none');
        const data=await fetchJson(urls.overview,q); const s=data.current?.summary||{}; const b=data.current?.bridge||{};
        document.getElementById('salesDetailOverviewNet').textContent=money(s.netSales); document.getElementById('salesDetailOverviewOrders').textContent=number(s.salesOrders); document.getElementById('salesDetailOverviewReturns').textContent=money(s.returns); document.getElementById('salesDetailOverviewRefunds').textContent=money(s.refundAmount);
        document.getElementById('salesDetailBridgeGross').textContent=money(b.grossSales); document.getElementById('salesDetailBridgeDiscounts').textContent=money(b.discounts); document.getElementById('salesDetailBridgeReturns').textContent=money(b.returns); document.getElementById('salesDetailBridgeNet').textContent=money(b.netSales);
        syncOverviewLinks();
    }

    async function loadSegment() {
        if (!contextReady) return;
        if (activeSegment==='overview') { await loadOverview(); return; }
        els.loading.hidden=false; els.state.hidden=true; els.tableWrap.hidden=true; els.body.replaceChildren();
        const [title,subtitle]=meta[activeSegment]; els.title.textContent=title; els.subtitle.textContent=subtitle;
        try {
            const data=await fetchJson(urls[activeSegment],buildQuery()); totalPages=Math.max(1,Number(data.totalPages||1)); page=Math.min(page,totalPages); els.loading.hidden=true;
            if (!data.items?.length) { els.state.hidden=false; els.stateTitle.textContent='Chưa có dữ liệu'; els.stateMessage.textContent='Không có bản ghi phù hợp bộ lọc hiện tại.'; }
            else { els.tableWrap.hidden=false; renderers[activeSegment](data); }
            els.pageMeta.textContent=`Trang ${data.page} / ${data.totalPages} · ${number(data.totalItems)} bản ghi`; els.prev.disabled=data.page<=1; els.next.disabled=data.page>=data.totalPages;
        } catch(error) { if(error.name==='AbortError')return; els.loading.hidden=true; els.state.hidden=false; els.stateTitle.textContent='Không tải được dữ liệu'; els.stateMessage.textContent='Vui lòng thử lại. Dữ liệu lỗi không được thay bằng số 0.'; }
    }

    async function loadAll() {
        const sequence = ++refreshSequence;
        contextReady = false;
        root.setAttribute('aria-busy','true');
        setSegment(activeSegment);
        els.tableWrap.hidden = true;
        els.body.replaceChildren();
        els.overview.querySelectorAll('strong[id]').forEach(value => { value.textContent = '—'; });
        els.pageMeta.textContent = '—';
        els.prev.disabled = true;
        els.next.disabled = true;
        els.state.hidden = true;
        els.loading.hidden = false;
        try {
            await loadContext();
            if (sequence !== refreshSequence) return;
            contextReady = true;
            syncUrl();
            setSegment(activeSegment);
            await loadSegment();
            els.live.textContent = `Đã tải ${activeSegment}`;
        } catch (error) {
            if (sequence !== refreshSequence || error.name === 'AbortError') return;
            contextReady = false;
            els.overview.hidden = true;
            els.dataSection.hidden = false;
            els.tableWrap.hidden = true;
            els.body.replaceChildren();
            els.state.hidden = false;
            els.stateTitle.textContent = 'Không tải được dữ liệu';
            els.stateMessage.textContent = 'Vui lòng thử lại. Dữ liệu lỗi không được thay bằng số 0.';
            els.live.textContent = 'Không tải được dữ liệu báo cáo.';
        } finally {
            if (sequence === refreshSequence) {
                els.loading.hidden = true;
                root.setAttribute('aria-busy','false');
            }
        }
    }

    els.tabs.forEach(tab=>tab.addEventListener('click',()=>{
        activeSegment=tab.dataset.salesDetailSegment;
        page=1;
        const q=params();
        if (activeSegment !== 'products') q.delete('variantId');
        if (activeSegment !== 'returns') q.delete('focus');
        q.set('segment', activeSegment);
        q.set('page', '1');
        history.replaceState(null,'',`${location.pathname}?${q}`);
        syncUrl();
        setSegment(activeSegment);
        loadSegment();
    }));
    [els.from,els.to,els.terminal,els.customer].forEach(x=>x.addEventListener('change',()=>{ page=1; syncUrl(); loadAll(); }));
    els.search.addEventListener('keydown',e=>{ if(e.key==='Enter'){ page=1; syncUrl(); loadSegment(); }});
    els.sort.addEventListener('change',()=>{ page=1; syncUrl(); loadSegment(); });
    els.pageSize.addEventListener('change',()=>{ page=1; syncUrl(); loadSegment(); });
    els.prev.addEventListener('click',()=>{ if(page>1){ page--; syncUrl(); loadSegment(); }}); els.next.addEventListener('click',()=>{ if(page<totalPages){ page++; syncUrl(); loadSegment(); }});
    els.refresh.addEventListener('click',loadAll);
    window.addEventListener('popstate',()=>{ syncFromUrl(); loadAll(); });

    syncFromUrl(); loadAll();
})();
