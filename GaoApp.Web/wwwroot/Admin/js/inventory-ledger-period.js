/* One stock key per summary. The server totals the full ledger, never the visible rows. */
(function () {
    "use strict";
    const root = document.querySelector("[data-inventory-ledger-index]");
    const panel = root?.querySelector("[data-ledger-period]");
    if (!panel) return;
    const select = panel.querySelector("select");
    const output = panel.querySelector("[data-period-result]");
    const phone = window.matchMedia("(max-width: 767.98px)");
    let stocks = new Map(), fromDate = "", toDate = "", controller, sequence = 0;
    const keyOf = item => `${item.warehouseId}:${item.productVariantId}`;
    const valid = item => Number.isInteger(item?.warehouseId) && item.warehouseId > 0
        && Number.isInteger(item?.productVariantId) && item.productVariantId > 0;
    function options(items, preferred) {
        const previous = stocks.get(preferred);
        stocks = new Map(items.filter(valid).map(item => [keyOf(item), item]));
        if (previous && !stocks.has(preferred)) stocks.set(preferred, previous);
        select.replaceChildren();
        for (const [key, item] of stocks) {
            const option = document.createElement("option");
            option.value = key;
            option.textContent = `${item.productName}${item.variantName && item.variantName !== item.productName ? " · " + item.variantName : ""} · ${item.warehouseName}`;
            select.append(option);
        }
        select.disabled = !stocks.size;
        if (stocks.has(preferred)) select.value = preferred;
    }
    root.addEventListener("gao:ledger-loaded", event => {
        fromDate = event.detail.fromDate; toDate = event.detail.toDate;
        const warehouse = document.getElementById("ledgerWarehouseFilter")?.value;
        const previous = stocks.get(select.value);
        const items = event.detail.items || [];
        let preferred = !warehouse || String(previous?.warehouseId) === warehouse ? select.value : "";
        // Keep the chosen key for an empty period, but follow the current results when
        // a search/page changes to another stock key. Never show an unrelated old total.
        if (items.length && !items.some(item => keyOf(item) === preferred)) preferred = "";
        options(items, preferred);
        if (phone.matches) load();
    });
    root.addEventListener("gao:ledger-quickview", event => {
        const item = event.detail.item;
        if (!valid(item)) return;
        const items = Array.from(stocks.values());
        if (!stocks.has(keyOf(item))) items.push(item);
        options(items, keyOf(item));
        if (phone.matches) load();
    });
    select.addEventListener("change", load);
    phone.addEventListener("change", () => { if (phone.matches) load(); else { ++sequence; controller?.abort(); } });
    async function load() {
        controller?.abort();
        const current = ++sequence;
        if (!phone.matches) return;
        const item = stocks.get(select.value);
        if (!item) { output.textContent = "Chưa có giao dịch để chọn sản phẩm. Thử đổi bộ lọc."; return; }
        controller = new AbortController();
        const activeController = controller;
        const timer = setTimeout(() => activeController.abort(), 20000);
        output.textContent = "Đang tính tổng kỳ…";
        panel.setAttribute("aria-busy", "true");
        const query = new URLSearchParams({ warehouseId: item.warehouseId, productVariantId: item.productVariantId });
        if (fromDate) query.set("fromDate", fromDate);
        if (toDate) query.set("toDate", toDate);
        try {
            const response = await fetch(`${panel.dataset.url}?${query}`, {
                cache: "no-store", headers: { Accept: "application/json" }, signal: activeController.signal
            });
            if (!response.ok) throw new Error("Không tải được tổng kỳ. Bấm Thử lại để tải lại.");
            const period = await response.json();
            if (current !== sequence) return;
            output.replaceChildren();
            if (!period.hasRecordedHistory) { output.textContent = "Chưa có lịch sử ghi sổ để xác định tồn đầu và cuối kỳ."; return; }
            const grid = document.createElement("div"); grid.className = "ledger-period__grid";
            for (const [label, value] of [["Tồn đầu kỳ", period.openingQty], ["Nhập / tăng", period.increaseQty],
                ["Xuất / giảm", period.decreaseQty], ["Tồn cuối kỳ", period.closingQty]]) {
                const cell = document.createElement("div"); cell.className = "ledger-period__metric";
                const name = document.createElement("span"); name.textContent = label;
                const number = document.createElement("strong");
                number.textContent = value == null ? "—" : Number(value).toLocaleString("vi-VN", { maximumFractionDigits: 6 });
                cell.append(name, number); grid.append(cell);
            }
            const caption = document.createElement("p"); caption.className = "ledger-period__caption";
            caption.textContent = `${period.fromDate?.slice(0, 10) || "Đầu sổ"} → ${period.toDate?.slice(0, 10) || "Hết lịch sử đã ghi"} · ${period.baseUnitName || "Đơn vị gốc"} · Theo ngày nghiệp vụ, giờ Việt Nam. Có tính các bút toán ghi muộn.`;
            output.append(grid, caption);
        } catch (error) {
            if (current !== sequence) return;
            output.textContent = error.name === "AbortError" ? "Tính tổng kỳ mất nhiều thời gian." : error.message;
            const retry = document.createElement("button"); retry.type = "button";
            retry.className = "btn btn-sm btn-label-primary mt-2"; retry.textContent = "Thử lại";
            retry.addEventListener("click", load, { once: true }); output.append(document.createElement("br"), retry);
        } finally { clearTimeout(timer); if (current === sequence) panel.setAttribute("aria-busy", "false"); }
    }
}());
