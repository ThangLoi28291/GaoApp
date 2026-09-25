(() => {
    "use strict";
    const root = document.querySelector("[data-profit-report]");
    if (!root) return;
    const one = selector => root.querySelector(selector);
    const mode = root.dataset.mode;
    const filters = one("[data-profit-filters]");
    const detailFilter = one("[data-profit-detail-filter]");
    const shared = ["fromDate", "toDate", "compare", "terminalId", "customerState"];
    const url = new URL(location.href);
    let requested = Object.fromEntries(url.searchParams);
    let active = null, generation = 0, response = null;
    const amount = v => v === null || v === undefined ? "—" :
        v !== 0 && Math.abs(v) < 1 ? (v < 0 ? "−" : "") + "<1 ₫" :
        new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 0 }).format(v) + " ₫";
    const percentage = v => v === null || v === undefined ? "—" :
        new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 2 }).format(v) + "%";
    const date = v => v ? new Intl.DateTimeFormat("vi-VN", {
        timeZone: "Asia/Ho_Chi_Minh", dateStyle: "short", timeStyle: "short"
    }).format(new Date(v.endsWith("Z") ? v : v + "Z")) : "—";
    const day = v => v ? v.slice(0, 10) : "";
    const node = (tag, text, className) => {
        const n = document.createElement(tag);
        if (text !== undefined) n.textContent = text;
        if (className) n.className = className;
        return n;
    };
    const qualityName = q => q === 2 ? "Tạm tính" : q >= 3 ? "Chưa đủ dữ liệu" : q === 0 ? "Không có dữ liệu" : "Đã xác định";
    const metric = (value, q) => ({ value, quality: q });
    function link(path, source = requested) {
        const destination = new URL(path, location.origin);
        shared.forEach(key => { if (source[key] !== null && source[key] !== undefined && source[key] !== "") destination.searchParams.set(key, source[key]); });
        return destination;
    }
    function drill(segment, bucket) {
        if (!response) return;
        const target = link("/admin/reports/profit/detail");
        target.searchParams.set("segment", segment);
        if (bucket !== undefined) target.searchParams.set("bucket", bucket);
        location.assign(target.href);
    }
    function navigation() {
        const origin = {};
        shared.forEach(key => origin[key] = requested["origin_" + key] ?? requested[key]);
        one("[data-profit-back]").href = link("/admin/reports/profit", mode === "activity" ? origin : requested).href;
        const activity = one("[data-profit-activity]");
        if (activity) {
            const target = link("/admin/reports/cost-adjustments");
            shared.forEach(key => { if (requested[key]) target.searchParams.set("origin_" + key, requested[key]); });
            activity.href = target.href;
        }
    }
    function table(element, caption, headings, rows) {
        element.replaceChildren();
        element.append(node("caption", caption));
        const head = node("thead"), hrow = node("tr");
        headings.forEach(h => { const th = node("th", h); th.scope = "col"; hrow.append(th); });
        head.append(hrow); element.append(head);
        const body = node("tbody");
        rows.forEach(values => {
            const tr = node("tr");
            values.forEach(value => { const td = node("td"); if (value instanceof Node) td.append(value); else td.textContent = value; tr.append(td); });
            body.append(tr);
        });
        if (!rows.length) { const tr = node("tr"), td = node("td", "Không có dòng phù hợp."); td.colSpan = headings.length; tr.append(td); body.append(tr); }
        element.append(body);
    }
    function badge(q) { return node("span", qualityName(q), "profit-badge" + (q === 2 ? " provisional" : "")); }
    function card(label, value, q, action, previous, isPercent = false) {
        const article = node("article", undefined, "profit-kpi");
        article.append(node("h2", label));
        const text = isPercent ? percentage(value) : typeof value === "string" ? value : amount(value);
        const display = node(action ? "button" : "div", text, "profit-value" + (typeof value === "number" && value < 0 ? " profit-negative" : ""));
        if (action) { display.type = "button"; display.setAttribute("aria-label", "Xem chi tiết " + label); display.addEventListener("click", action); }
        article.append(display);
        if (text !== qualityName(q)) article.append(badge(q));
        if (previous) {
            let change = "So sánh: chưa đủ dữ liệu";
            if (typeof value === "number" && previous.value !== null) {
                const delta = value - previous.value;
                change = (delta > 0 ? "+" : "") + (isPercent ? percentage(delta).replace("%", " điểm %") : amount(delta)) + " so với kỳ trước";
                if (q === 2 || previous.quality === 2) change += " • Tạm tính";
            }
            article.append(node("p", change, "profit-delta"));
        }
        one("[data-profit-kpis]").append(article);
    }
    function chart(series, labels, onSelect, bars = false) {
        const container = one("[data-profit-chart]");
        container.replaceChildren();
        const ns = "http://www.w3.org/2000/svg";
        const svg = document.createElementNS(ns, "svg");
        const width = Math.max(640, container.clientWidth || 1000), left = 130, right = width - 30;
        svg.setAttribute("viewBox", "0 0 " + width + " 280"); svg.setAttribute("role", "img");
        svg.setAttribute("aria-label", "Biểu đồ xu hướng; số liệu và trạng thái đầy đủ ở bảng bên dưới");
        const values = series.flatMap(s => s.values).filter(v => typeof v === "number" && Number.isFinite(v));
        if (!values.length) { container.append(node("p", "Chưa có dữ liệu đủ tin cậy để vẽ biểu đồ.")); return; }
        const lo = Math.min(0, ...values), hi = Math.max(0, ...values), span = hi - lo || 1;
        const count = Math.max(labels.length, ...series.map(s => s.values.length));
        const x = i => count === 1 ? (left + right) / 2 : left + i * (right - left) / Math.max(1, count - 1);
        const y = v => 215 - (v - lo) * 180 / span;
        const label = (text, px, py, anchor = "middle") => {
            const t = document.createElementNS(ns, "text"); t.textContent = text;
            t.setAttribute("x", px); t.setAttribute("y", py); t.setAttribute("text-anchor", anchor);
            t.setAttribute("class", "profit-axis-label"); svg.append(t);
        };
        [...new Set([lo, 0, hi])].forEach(v => label(amount(v), left - 12, y(v) + 5, "end"));
        const intervals = Math.max(1, Math.min(labels.length - 1, Math.floor((right - left) / 120)));
        const ticks = new Set(Array.from({ length: intervals + 1 }, (_, i) => Math.round(i * (labels.length - 1) / intervals)));
        ticks.forEach(i => { if (labels[i] !== undefined) label(labels[i], x(i), 250); });
        label("Ngày / khoảng", right, 274, "end");
        const baseline = document.createElementNS(ns, "path");
        baseline.setAttribute("d", "M" + left + " " + y(0) + " H" + right); baseline.setAttribute("stroke", "#8190a5"); svg.append(baseline);
        series.forEach((s, si) => {
            let path = "", open = false;
            s.values.forEach((v, i) => {
                if (v === null || v === undefined) { open = false; return; }
                path += (open ? " L" : " M") + x(i) + " " + y(v); open = true;
                const dot = document.createElementNS(ns, bars ? "rect" : "circle");
                if (bars) {
                    dot.setAttribute("x", x(i) + (si ? 1 : -9)); dot.setAttribute("width", "8");
                    dot.setAttribute("y", Math.min(y(0), y(v))); dot.setAttribute("height", Math.max(1, Math.abs(y(v) - y(0))));
                    if (si) { dot.setAttribute("stroke", "#633714"); dot.setAttribute("stroke-dasharray", "2 2"); }
                } else {
                    dot.setAttribute("cx", x(i)); dot.setAttribute("cy", y(v)); dot.setAttribute("r", "4");
                }
                const color = si ? "#b06126" : "#2459b7";
                dot.setAttribute("fill", s.qualities?.[i] === 2 ? "white" : color);
                if (s.qualities?.[i] === 2) { dot.setAttribute("stroke", color); dot.setAttribute("stroke-width", "2"); }
                const pointLabel = s.labels?.[i] ?? labels[i];
                const description = s.tooltips?.[i] ?? pointLabel + " • " + s.label + ": " + amount(v);
                const title = document.createElementNS(ns, "title"); title.textContent = description; dot.append(title);
                dot.setAttribute("aria-label", description);
                if (count === 1) label(s.label + ": " + amount(v), x(i), si ? 20 : 270);
                if (onSelect && si === 0) {
                    dot.setAttribute("tabindex", "0"); dot.setAttribute("role", "button"); dot.classList.add("profit-chart-point");
                    dot.setAttribute("aria-label", "Chi tiết " + description);
                    dot.addEventListener("click", () => onSelect(i));
                    dot.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onSelect(i); } });
                }
                svg.append(dot);
            });
            const line = document.createElementNS(ns, "path");
            line.setAttribute("d", path); line.setAttribute("fill", "none");
            line.setAttribute("stroke", si ? "#b06126" : "#2459b7"); line.setAttribute("stroke-width", "2");
            if (si) line.setAttribute("stroke-dasharray", "7 5");
            if (!bars) svg.prepend(line);
        });
        container.append(svg);
    }
    function renderTrend(data) {
        if (mode === "activity") {
            const days = data.activity.days;
            one("[data-profit-legend]").textContent = "Cột xanh: giá vốn tăng • Cột nâu viền đứt: giá vốn giảm (độ lớn)";
            chart([{ label: "Tăng", values: days.map(x => x.increase) }, { label: "Giảm", values: days.map(x => x.decrease) }], days.map(x => day(x.date)), undefined, true);
            table(one("[data-profit-trend-table]"), "Hoạt động theo ngày điều chỉnh — toàn kỳ", ["Ngày", "Tăng", "Giảm", "Trạng thái"],
                days.map(x => [day(x.date), amount(x.increase), amount(x.decrease), qualityName(x.quality)]));
            return;
        }
        const chosen = one("[data-profit-metric]").value;
        const metricName = { grossProfit: "Lợi nhuận gộp", netSales: "Net Sales", cogs: "COGS — Giá vốn" }[chosen];
        one("[data-profit-chart-title]").textContent = "Xu hướng " + metricName;
        const periodRange = period => period ? day(period.fromDate) + " → " + day(period.toDate) : "";
        const currentRange = periodRange(data.period), previousRange = periodRange(data.comparisonPeriod);
        const describe = (p, period) => p ? period + " • " + p.label + " • " + metricName + ": " + amount(p.summary[chosen].value) +
            " • " + qualityName(p.summary[chosen].quality) + " • Giá vốn: " + p.summary.costState +
            " • Giá vốn tạm tính: " + amount(p.summary.provisionalCogs.value) +
            " • Phạm vi: " + (period === "Kỳ hiện tại" ? currentRange : previousRange) : period + " • Không có khoảng tương ứng";
        const makeSeries = (points, period) => ({
            label: period, labels: points.map(p => p.label), values: points.map(p => p.summary[chosen].value),
            qualities: points.map(p => p.summary[chosen].quality),
            tooltips: points.map((p, i) => describe(data.trend[i], "Kỳ hiện tại") +
                (data.comparison ? " | " + describe(data.comparisonTrend[i], "Kỳ trước") : ""))
        });
        const series = [makeSeries(data.trend, "Kỳ hiện tại")];
        if (data.comparison) series.push(makeSeries(data.comparisonTrend, "Kỳ trước"));
        one("[data-profit-legend]").textContent = "Nét liền: kỳ hiện tại " + currentRange +
            (data.comparison ? " • Nét đứt: kỳ trước " + previousRange + " (ghép theo vị trí khoảng; ngày thực tế ở tooltip và bảng)." : " • Không so sánh.") +
            " Điểm rỗng: tạm tính. Khoảng thiếu dữ liệu không được nối thành số 0.";
        chart(series, data.trend.map(p => p.label), i => drill("profit", data.trend[i].bucket));
        const rows = [];
        const appendRow = (p, period, current) => {
            if (!p) return;
            const s = p.summary;
            const button = current ? node("button", "Xem", "btn btn-sm btn-outline-primary") : "—";
            if (current) { button.type = "button"; button.addEventListener("click", () => drill("profit", p.bucket)); }
            rows.push([period, p.label, amount(s.netSales.value), amount(s.cogs.value), amount(s.grossProfit.value),
                s.costState, amount(s.provisionalCogs.value), button]);
        };
        for (let i = 0; i < Math.max(data.trend.length, data.comparison ? data.comparisonTrend.length : 0); i++) {
            appendRow(data.trend[i], "Kỳ hiện tại", true);
            if (data.comparison) appendRow(data.comparisonTrend[i], "Kỳ trước", false);
        }
        table(one("[data-profit-trend-table]"), "Xu hướng toàn kỳ — đơn vị ₫ • Kỳ hiện tại: " + currentRange +
            (data.comparison ? " • Kỳ trước: " + previousRange : ""),
            ["Kỳ", "Khoảng", "Net Sales", "COGS", "Lợi nhuận gộp", "Trạng thái giá vốn", "Giá vốn tạm tính", "Chi tiết"], rows);
    }
    function render(data) {
        one("[data-profit-kpis]").replaceChildren();
        const s = data.current, previous = data.comparison;
        const empty = one("[data-profit-empty]");
        empty.hidden = mode === "activity" ? data.activity.quality !== 0 : !s.isEmpty;
        empty.textContent = mode === "activity" ? "Không có điều chỉnh giá vốn trong kỳ này." : "Chưa có dữ liệu bán hàng hoặc trả hàng trong kỳ này.";
        one("[data-profit-quality]").textContent = mode === "activity" ?
            (data.activity.quality >= 3 ? "Chưa đủ dữ liệu tin cậy cho toàn phạm vi điều chỉnh." : "Điều chỉnh đã phản ánh về kỳ bán liên quan; không cộng lần nữa vào lợi nhuận.") :
            (s.message || "Giá vốn theo dữ liệu cập nhật mới nhất.") + (s.netSales.value < 0 ? " Net Sales đang âm; đọc tỷ lệ cùng số tiền lợi nhuận." : "");
        if (mode === "activity") {
            const a = data.activity;
            card("Giá vốn tăng", a.increase, a.quality); card("Giá vốn giảm", a.decrease, a.quality);
            card("Điều chỉnh ròng", a.net, a.quality); card("Số điều chỉnh", a.count === null ? "—" : String(a.count), a.quality);
            table(one("[data-profit-detail-table]"), "Điều chỉnh theo thời điểm phát sinh", ["Ngày điều chỉnh", "Đơn gốc", "Ngày bán", "Tác động giá vốn", "Trạng thái"],
                a.rows.map(x => [date(x.adjustmentDate), x.orderNumber, date(x.saleDate), amount(x.impact), qualityName(x.quality)]));
        } else {
            const salesAction = root.dataset.canSales === "true" ? () => location.assign(link("/admin/reports/overview").href) : undefined;
            card("Net Sales", s.netSales.value, s.netSales.quality, salesAction, previous?.netSales);
            card("COGS — Giá vốn", s.cogs.value, s.cogs.quality, () => drill("cogs"), previous?.cogs);
            card("Lợi nhuận gộp", s.grossProfit.value, s.grossProfit.quality, () => drill("profit"), previous?.grossProfit);
            card("Biên lợi nhuận gộp", s.grossMargin.value, s.grossMargin.quality, () => drill("profit"), previous?.grossMargin, true);
            card("Giá vốn tạm tính", s.provisionalCogs.value, s.provisionalCogs.quality, () => drill("provisional"), previous?.provisionalCogs);
            card("Mức độ xác định giá vốn", s.costState, s.cogs.quality, () => one("[data-profit-cost-state]").scrollIntoView({ block: "center" }));
            const bridge = one("[data-profit-bridge]"); bridge.replaceChildren();
            [["Net Sales", s.netSales.value], ["Trừ COGS", s.cogs.value === null ? null : -s.cogs.value], ["Lợi nhuận gộp", s.grossProfit.value]]
                .forEach(([name, value]) => bridge.append(node("dt", name), node("dd", amount(value))));
            one("[data-profit-cost-state]").textContent = s.costState;
            one("[data-profit-exposure]").textContent = "Giá vốn tạm tính: " + amount(s.provisionalCogs.value) +
                (s.affectedOrders === null ? " • Chưa xác định đủ số đơn/dòng liên quan" : " • " + s.affectedOrders + " đơn / " + s.affectedLines + " dòng liên quan");
            table(one("[data-profit-detail-table]"), "Chi tiết trong phạm vi — giá vốn theo kỳ bán gốc", ["Đơn / dòng", "Ngày bán gốc", "Ngày ghi nhận", "Loại", "Net Sales", "COGS", "Lợi nhuận", "Tạm tính", "Trạng thái"],
                data.details.map(x => [x.orderNumber + (x.itemName ? " / " + x.itemName : ""), date(x.saleDate), date(x.eventDate), x.eventKind,
                    amount(x.summary.netSales.value), amount(x.summary.cogs.value), amount(x.summary.grossProfit.value), amount(x.summary.provisionalCogs.value), x.summary.costState]));
        }
        renderTrend(data);
        one("[data-profit-page]").textContent = "Trang " + data.query.page + " • " + data.totalItems + " dòng";
        one("[data-profit-prev]").disabled = data.query.page <= 1;
        one("[data-profit-next]").disabled = data.query.page * data.query.pageSize >= data.totalItems;
        const bucket = one("[data-profit-bucket]"); bucket.replaceChildren(); bucket.hidden = !requested.bucket;
        if (!bucket.hidden) {
            bucket.append(node("span", "Đang giới hạn khoảng " + (Number(requested.bucket) + 1) + " "));
            const clear = node("button", "Bỏ giới hạn bucket", "btn btn-sm btn-outline-primary"); clear.type = "button";
            clear.addEventListener("click", () => { delete requested.bucket; requested.page = "1"; load(); }); bucket.append(clear);
        }
        one("[data-profit-context]").textContent = (mode === "activity" ? "Kỳ điều chỉnh " : "Kỳ bán ") +
            day(data.query.fromDate) + " → " + day(data.query.toDate) + " • Cập nhật " + date(data.generatedAtUtc);
    }
    async function load() {
        const mine = ++generation;
        active?.abort(); active = new AbortController();
        response = null;
        // Invalidate the entire old snapshot before changing/loading context.
        one("[data-profit-results]").hidden = true;
        one("[data-profit-context]").textContent = "";
        one("[data-profit-error]").hidden = true;
        one("[data-profit-live]").textContent = "Đang tải báo cáo…";
        root.setAttribute("aria-busy", "true");
        const requestUrl = new URL(root.dataset.url, location.origin);
        Object.entries(requested).forEach(([key, value]) => { if (value !== "" && value !== null && value !== undefined && !key.startsWith("origin_")) requestUrl.searchParams.set(key, value); });
        navigation();
        let publicMessage = "Không tải được dữ liệu báo cáo. Vui lòng thử lại.";
        try {
            const res = await fetch(requestUrl, { signal: active.signal, headers: { Accept: "application/json" }, credentials: "same-origin" });
            if (!res.ok) {
                if (res.status === 400) {
                    const body = await res.json();
                    if (typeof body.message === "string") publicMessage = body.message.replace(/\s*\(Parameter '[^']*'\)\s*$/, "").trim() || publicMessage;
                }
                if (res.status === 401 || res.status === 403) publicMessage = "Không có quyền truy cập hoặc phiên làm việc đã hết hạn.";
                if (res.status === 503) publicMessage = "Báo cáo đang bận hoặc mất quá lâu để xử lý. Vui lòng đợi vài giây rồi thử lại, hoặc chọn khoảng ngày ngắn hơn.";
                throw new Error(publicMessage);
            }
            const data = await res.json();
            if (mine !== generation) return;
            response = data;
            shared.forEach(key => {
                const value = key.endsWith("Date") ? day(data.query[key]) : data.query[key];
                if (value === null || value === undefined) delete requested[key]; else requested[key] = String(value);
            });
            const terminal = filters.elements.namedItem("terminalId");
            terminal.replaceChildren(new Option("Tất cả terminal", ""));
            data.terminals.forEach(t => terminal.append(new Option(t.name, t.id)));
            shared.forEach(key => { filters.elements.namedItem(key).value = requested[key] ?? ""; });
            ["search", "sort", "pageSize"].forEach(key => { detailFilter.elements.namedItem(key).value = requested[key] ?? (key === "sort" ? "newest" : key === "pageSize" ? "25" : ""); });
            const currentUrl = new URL(location.href); currentUrl.search = "";
            Object.entries(requested).forEach(([key, value]) => { if (value) currentUrl.searchParams.set(key, value); });
            history.replaceState(null, "", currentUrl);
            render(data); navigation();
            one("[data-profit-results]").hidden = false;
            one("[data-profit-live]").textContent = "Đã cập nhật báo cáo.";
        } catch (error) {
            if (mine !== generation || error.name === "AbortError") return;
            one("[data-profit-error] span").textContent = publicMessage;
            one("[data-profit-error]").hidden = false;
            one("[data-profit-live]").textContent = "Tải báo cáo thất bại.";
        } finally {
            if (mine === generation) root.setAttribute("aria-busy", "false");
        }
    }
    filters.addEventListener("submit", e => {
        e.preventDefault();
        shared.forEach(key => { requested[key] = filters.elements.namedItem(key).value; });
        ["bucket", "search", "page", "sort"].forEach(key => delete requested[key]);
        load();
    });
    detailFilter.addEventListener("submit", e => {
        e.preventDefault();
        ["search", "sort", "pageSize"].forEach(key => requested[key] = detailFilter.elements.namedItem(key).value);
        requested.page = "1"; load();
    });
    one("[data-profit-prev]").addEventListener("click", () => { requested.page = String(Math.max(1, Number(requested.page || 1) - 1)); load(); });
    one("[data-profit-next]").addEventListener("click", () => { requested.page = String(Number(requested.page || 1) + 1); load(); });
    ["[data-profit-refresh]", "[data-profit-retry]"].forEach(selector => one(selector).addEventListener("click", load));
    root.querySelectorAll("[data-profit-drill]").forEach(button => button.addEventListener("click", () => drill(button.dataset.profitDrill)));
    one("[data-profit-metric]")?.addEventListener("change", () => { if (response) renderTrend(response); });
    if (typeof ResizeObserver !== "undefined") {
        let chartWidth;
        new ResizeObserver(entries => {
            const width = entries[0].contentRect.width;
            if (width === chartWidth) return;
            chartWidth = width;
            if (response) renderTrend(response);
        }).observe(one("[data-profit-chart]"));
    }
    load();
})();
