(function () {
    "use strict";
    function calculate(gross, rate) {
        const amount = BigInt(gross);
        if (![0, 1, 2, 3, 5].includes(rate) || amount < 0n) throw new Error("Invalid tax reduction");
        const reduction = (amount * BigInt(rate) + 250n) / 500n;
        return { reduction, total: amount - reduction };
    }
    if (typeof module !== "undefined" && module.exports) module.exports = { calculate };
    if (typeof document === "undefined") return;
    function initialize(pending) {
    for (const root of document.querySelectorAll("[data-invoice-tax-relief]")) {
        if (root.dataset.taxInitialized) continue;
        root.dataset.taxInitialized = "true";
        const enabled = root.querySelector("[data-tax-enabled]");
        const rate = root.querySelector("[data-tax-percentage]");
        if (!enabled || !rate) continue;
        if (pending) { enabled.checked = pending.enabled; rate.value = pending.rate; }
        function update() {
            root.querySelector("[data-tax-controls]").hidden = !enabled.checked;
            rate.disabled = !enabled.checked;
            rate.required = enabled.checked;
            const dirty = Number(root.dataset.savedRate) !== (enabled.checked ? Number(rate.value) : 0);
            root.dataset.taxDirty = String(dirty);
            root.querySelector("[data-tax-unsaved]").hidden = !dirty;
            for (const action of document.querySelectorAll("[data-tax-dependent]")) {
                action.setAttribute("aria-disabled", String(dirty));
                for (const button of action.querySelectorAll("button[type=submit]")) {
                    if (!button.hasAttribute("data-tax-original-disabled")) button.dataset.taxOriginalDisabled = String(button.disabled);
                    button.disabled = dirty || button.dataset.taxOriginalDisabled === "true";
                }
            }
            const value = calculate(root.dataset.baseAmount, enabled.checked ? Number(rate.value) : 0);
            root.querySelector("[data-tax-reduction]").textContent = value.reduction.toLocaleString("vi-VN");
            root.querySelector("[data-tax-total]").textContent = value.total.toLocaleString("vi-VN");
            root.querySelector("[data-tax-explanation]").textContent = "Tỷ lệ " + rate.value + "%: số tiền giảm bằng " + (Number(rate.value) / 5).toLocaleString("vi-VN") + "% tiền hàng.";
        }
        enabled.addEventListener("change", update);
        rate.addEventListener("change", update);
        update();
    }
    }
    initialize();
    document.addEventListener("invoice:lines-changed", event => initialize(event.detail?.pendingTax));
    document.addEventListener("click", event => {
        const action = event.target.closest("[data-tax-dependent]");
        if (action?.getAttribute("aria-disabled") === "true") { event.preventDefault(); event.stopImmediatePropagation(); }
    }, true);
    document.addEventListener("submit", event => {
        if (event.target.matches("[data-tax-dependent]") && document.querySelector("[data-invoice-tax-relief]")?.dataset.taxDirty === "true") {
            event.preventDefault(); event.stopImmediatePropagation();
        }
    }, true);
}());
