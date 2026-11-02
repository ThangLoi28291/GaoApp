/* UI-03d: phone-only read adapter; navigation and reporting retain their existing owners. */
(function () {
    "use strict";
    const root = document.querySelector("[data-mobile-home]");
    if (!root || root.dataset.homeReady) return;
    root.dataset.homeReady = "true";
    const phone = matchMedia("(max-width: 767.98px)");
    const bar = document.querySelector("[data-mobile-pins]");
    const pins = root.querySelector("[data-home-pins]");
    const sales = root.querySelector("[data-home-sales]");
    const content = sales?.querySelector("[data-home-content]");
    const loading = sales?.querySelector("[data-home-loading]");
    const status = sales?.querySelector("[data-home-status]");
    const message = sales?.querySelector("[data-home-status-text]");
    const refresh = sales?.querySelector("[data-home-refresh]");
    const reload = sales?.querySelector("[data-home-reload]");
    const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 });
    const integer = new Intl.NumberFormat("vi-VN");
    const dayFormat = new Intl.DateTimeFormat("vi-VN", { timeZone: "Asia/Ho_Chi_Minh", day: "2-digit", month: "2-digit", year: "numeric" });
    const clockFormat = new Intl.DateTimeFormat("vi-VN", { timeZone: "Asia/Ho_Chi_Minh", hour: "2-digit", minute: "2-digit", second: "2-digit" });
    const countKeys = new Set(["salesOrders", "returnCount", "voidCount"]);
    const metricKeys = ["netSales", "salesOrders", "aov", "returns", "returnCount", "voidCount"];
    let pending = null, sequence = 0, blocked = false;

    function syncPins() {
        if (!phone.matches || !bar) return;
        const focused = document.activeElement;
        const restore = pins.contains(focused);
        const focusedId = focused?.closest("[data-home-pin-id]")?.dataset.homePinId;
        const fragment = document.createDocumentFragment();
        // This bar has already projected valid IDs into the current store's rendered menu.
        const sources = Array.from(bar.querySelectorAll("[data-mobile-pin-id]")).slice(0, 4);
        let count = 0;
        for (const source of sources) {
            const id = Number(source.dataset.mobilePinId);
            const title = source.querySelector("span")?.textContent.trim();
            const href = source.getAttribute("href");
            if (!Number.isSafeInteger(id) || id <= 0 || !title || !href) continue;
            const anchor = document.createElement("a"), icon = document.createElement("span"), label = document.createElement("span"), arrow = document.createElement("i");
            anchor.setAttribute("href", href); anchor.title = title;
            anchor.dataset.homePinId = String(id); anchor.dataset.menuId = String(id);
            if (source.hasAttribute("aria-current")) anchor.setAttribute("aria-current", source.getAttribute("aria-current"));
            icon.className = "gmh-pin-icon";
            const sourceIcon = source.querySelector("i");
            if (sourceIcon) icon.append(sourceIcon.cloneNode(false));
            label.textContent = title; arrow.className = "bx bx-chevron-right"; arrow.setAttribute("aria-hidden", "true");
            anchor.append(icon, label, arrow); fragment.append(anchor); count++;
        }
        pins.replaceChildren(fragment);
        root.querySelector("[data-home-pin-count]").textContent = String(count);
        root.querySelector("[data-home-empty-pins]").hidden = count > 0;
        if (restore) {
            const replacement = Array.from(pins.querySelectorAll("[data-home-pin-id]")).find(anchor => anchor.dataset.homePinId === focusedId);
            (replacement ?? root.querySelector(".gmh-menu-button"))?.focus({ preventScroll: true });
        }
    }
    bar?.addEventListener("gao:mobile-pins-rendered", syncPins);

    function setBusy(value) {
        if (!sales) return;
        sales.setAttribute("aria-busy", String(value));
        refresh.disabled = value || blocked;
        refresh.classList.toggle("is-loading", value);
    }
    function clearValues() {
        if (!sales) return;
        content.hidden = true; loading.hidden = true;
        sales.querySelectorAll("[data-home-metric]").forEach(node => { node.textContent = "—"; });
        sales.querySelector("[data-home-generated]").textContent = "—";
        sales.querySelector("[data-home-generated]").removeAttribute("datetime");
        sales.querySelector("[data-home-period]").textContent = "Toàn cửa hàng · UTC+7";
        sales.querySelector("[data-home-empty-day]").hidden = true;
    }
    function showStatus(text, kind = "info", needsReload = false) {
        status.hidden = false; status.dataset.kind = kind;
        message.textContent = text; reload.hidden = !needsReload;
    }
    function cancel() {
        sequence++;
        if (pending) { clearTimeout(pending.timer); pending.controller.abort(); pending = null; }
        blocked = false; clearValues(); setBusy(false);
        if (sales) showStatus("Bấm Làm mới để tải tổng quan kinh doanh.");
    }
    function snapshot(data) {
        const summary = data?.current?.summary;
        if (!summary || metricKeys.some(key => typeof summary[key] !== "number" || !Number.isFinite(summary[key]))) throw new Error("Invalid summary");
        if (Array.from(countKeys).some(key => !Number.isSafeInteger(summary[key]) || summary[key] < 0)) throw new Error("Invalid counts");
        const from = data?.currentPeriod?.fromDate, to = data?.currentPeriod?.toDate;
        if (typeof from !== "string" || typeof to !== "string" || !/^\d{4}-\d{2}-\d{2}(T|$)/.test(from) || !/^\d{4}-\d{2}-\d{2}(T|$)/.test(to) || from.slice(0, 10) !== to.slice(0, 10)) throw new Error("Invalid period");
        const day = new Date(from.slice(0, 10) + "T00:00:00+07:00");
        const rawTime = data.generatedAtUtc;
        if (typeof rawTime !== "string" || !/^\d{4}-\d{2}-\d{2}T/.test(rawTime)) throw new Error("Missing timestamp");
        const generated = new Date(/[Zz]|[+-]\d{2}:?\d{2}$/.test(rawTime) ? rawTime : rawTime + "Z");
        if (!Number.isFinite(day.getTime()) || !Number.isFinite(generated.getTime())) throw new Error("Invalid timestamp");
        return { summary, day, generated };
    }
    function render(data) {
        const value = snapshot(data);
        sales.querySelectorAll("[data-home-metric]").forEach(node => {
            const key = node.dataset.homeMetric;
            node.textContent = countKeys.has(key) ? integer.format(value.summary[key]) : money.format(value.summary[key]);
        });
        sales.querySelector("[data-home-period]").textContent = dayFormat.format(value.day) + " · Toàn cửa hàng · UTC+7";
        const time = sales.querySelector("[data-home-generated]");
        time.dateTime = value.generated.toISOString(); time.textContent = clockFormat.format(value.generated);
        sales.querySelector("[data-home-empty-day]").hidden = !(value.summary.salesOrders === 0 && value.summary.returnCount === 0 && value.summary.voidCount === 0 && value.summary.netSales === 0 && value.summary.returns === 0);
        loading.hidden = true; status.hidden = true; content.hidden = false;
    }
    async function load() {
        if (!sales || !phone.matches || document.hidden || pending || blocked || !sales.dataset.homeSalesUrl) return;
        const url = new URL(sales.dataset.homeSalesUrl, location.href);
        if (url.origin !== location.origin) return;
        // Server resolves today's Vietnam period and the authenticated current store. No store/user override.
        url.searchParams.set("compare", "none");
        const operation = { controller: new AbortController(), sequence: ++sequence, timer: null, timedOut: false };
        pending = operation;
        const owns = () => pending === operation && sequence === operation.sequence && phone.matches && !document.hidden && root.isConnected;
        clearValues(); setBusy(true); loading.hidden = false; showStatus("Đang tải tổng quan kinh doanh…");
        operation.timer = setTimeout(() => { operation.timedOut = true; operation.controller.abort(); }, 18000);
        try {
            const response = await fetch(url.href, { method: "GET", credentials: "same-origin", cache: "no-store", redirect: "error", signal: operation.controller.signal, headers: { Accept: "application/json" } });
            if (!owns()) return;
            if (operation.controller.signal.aborted) throw new Error("Read aborted");
            if (response.status === 401 || response.status === 403) {
                blocked = true; clearValues(); showStatus("Phiên đăng nhập hoặc quyền truy cập đã thay đổi. Tải lại trang để tiếp tục.", "error", true); return;
            }
            if (!response.ok || response.redirected || !response.headers.get("content-type")?.includes("application/json")) throw new Error("Read failed");
            const data = await response.json();
            if (!owns()) return;
            if (operation.controller.signal.aborted) throw new Error("Read aborted");
            render(data);
        } catch (_) {
            if (owns()) { clearValues(); showStatus(operation.timedOut ? "Chưa nhận được kết quả. Bấm Làm mới để thử lại." : "Không tải được tổng quan kinh doanh. Bấm Làm mới để thử lại.", "error"); }
        } finally {
            clearTimeout(operation.timer);
            if (pending === operation) { pending = null; setBusy(false); }
        }
    }
    function sync() {
        if (!phone.matches || document.hidden) { cancel(); return; }
        const name = document.querySelector("[data-employee-account] .gao-account-person strong")?.textContent.trim();
        if (name) root.querySelector("[data-home-person]").textContent = "Chào, " + name;
        load();
    }
    refresh?.addEventListener("click", load);
    phone.addEventListener("change", sync);
    document.addEventListener("visibilitychange", sync);
    window.addEventListener("pagehide", cancel);
    window.addEventListener("pageshow", event => { if (event.persisted) cancel(); }); // Existing account guard reloads restored sessions.
    sync();
})();
