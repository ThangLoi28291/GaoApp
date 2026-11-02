/* Local sample interactions for /admin/ui-foundation. Never sends or persists business data. */
(function () {
    "use strict";
    const root = document.querySelector("[data-ui-foundation]");
    if (!root || root.dataset.previewReady) return;
    root.dataset.previewReady = "true";
    const search = root.querySelector("#ui-order-search");
    const select = root.querySelector("#ui-state-select");
    const rows = Array.from(root.querySelectorAll("[data-sample-order]"));
    const chips = Array.from(root.querySelectorAll("[data-order-filter]"));
    let state = "all";
    let sectionKey = "home";
    let toastTimer;
    const normalize = value => value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLocaleLowerCase("vi").replace(/đ/g, "d");

    function filter() {
        const query = normalize(search.value.trim());
        let visible = 0;
        rows.forEach(row => {
            row.hidden = !(state === "all" || row.dataset.state === state) || !normalize(row.dataset.code + " " + row.dataset.customer).includes(query);
            if (!row.hidden) visible++;
        });
        chips.forEach(chip => chip.setAttribute("aria-pressed", String(chip.dataset.orderFilter === state)));
        root.querySelector("[data-no-results]").hidden = visible !== 0;
        root.querySelector("[data-visible-count]").textContent = visible + " đơn hàng mẫu";
        root.querySelector("[data-search-summary]").textContent = "Hiển thị " + visible + " đơn hàng mẫu.";
        root.querySelector("[data-clear-search]").hidden = !search.value;
        root.querySelector("[data-filter-count]").hidden = state === "all";
        select.value = state;
    }
    function toast(message) {
        const target = root.querySelector("[data-preview-toast]");
        clearTimeout(toastTimer);
        target.textContent = message;
        target.hidden = false;
        toastTimer = setTimeout(() => { target.hidden = true; }, 4500);
    }
    function showState(kind) {
        root.querySelectorAll("[data-state-panel]").forEach(panel => { panel.hidden = panel.dataset.statePanel !== kind; });
        root.querySelectorAll("[data-preview-state]").forEach(button => button.setAttribute("aria-pressed", String(button.dataset.previewState === kind)));
    }
    const navItems = Array.from(root.querySelectorAll("[data-preview-nav]"));
    const menu = root.querySelector("#ui-navigation-dialog");
    const menuButton = root.querySelector("[data-preview-nav-menu]");
    function setNavigation(key) {
        navItems.forEach(item => {
            if (item.dataset.previewNav === key) item.setAttribute("aria-current", "location");
            else item.removeAttribute("aria-current");
        });
    }
    ["ui-receiving-dialog", "ui-pos-dialog"].forEach(id => {
        root.querySelector("#" + id).addEventListener("close", () => setNavigation(sectionKey));
    });
    menu.addEventListener("close", () => menuButton.setAttribute("aria-expanded", "false"));
    function jumpToSection(item) {
        sectionKey = item.dataset.previewNav;
        setNavigation(sectionKey);
        const target = root.querySelector(item.getAttribute("href"));
        if (target) { target.scrollIntoView({ block: "start" }); target.focus({ preventScroll: true }); }
    }
    search.addEventListener("input", filter);
    root.addEventListener("click", function (event) {
        if (!(event.target instanceof Element)) return;
        const navigation = event.target.closest("[data-preview-nav]");
        if (navigation) {
            if (navigation.matches("a")) { event.preventDefault(); jumpToSection(navigation); }
            else setNavigation(navigation.dataset.previewNav);
        }
        if (event.target.closest("[data-preview-nav-menu]")) menuButton.setAttribute("aria-expanded", "true");
        const menuEntry = event.target.closest("[data-preview-menu-go]");
        if (menuEntry) {
            const destination = navItems.find(item => item.dataset.previewNav === menuEntry.dataset.previewMenuGo);
            menu.addEventListener("close", function () {
                // Wait until every close listener has restored focus and the previous scroll lock.
                queueMicrotask(() => {
                    if (!destination) return;
                    if (destination.matches("a")) jumpToSection(destination);
                    else if (window.GaoCommerce.openDialog(root.querySelector("#" + destination.dataset.gaoDialogOpen), menuButton)) setNavigation(destination.dataset.previewNav);
                });
            }, { once: true });
            menu.close();
        }
        const button = event.target.closest("button");
        if (!button) return;
        if (button.matches("[data-order-filter]")) { state = button.dataset.orderFilter; filter(); }
        if (button.matches("[data-clear-search]")) { search.value = ""; filter(); search.focus(); }
        if (button.matches("[data-reset-search]")) { state = "all"; search.value = ""; filter(); search.focus(); }
        if (button.matches("[data-clear-filters]")) { select.value = "all"; }
        if (button.matches("[data-show-pending]")) { state = "pending"; search.value = ""; filter(); search.focus(); }
        if (button.matches("[data-order-detail]")) {
            const row = button.closest("[data-sample-order]");
            ["code", "customer", "status", "amount"].forEach(key => { root.querySelector("[data-detail-" + key + "]").textContent = row.dataset[key]; });
            root.querySelector("[data-detail-total]").textContent = row.dataset.amount;
        }
        if (button.matches("[data-preview-state]")) showState(button.dataset.previewState);
        if (button.matches("[data-preview-retry]")) { showState("loading"); setTimeout(() => showState("empty"), 900); }
        if (button.matches("[data-preview-confirm]")) { button.closest("dialog").close(); toast("Đã thử xác nhận. Không có dữ liệu nào thay đổi."); }
    });
    root.querySelector("[data-filter-form]").addEventListener("submit", function (event) {
        event.preventDefault(); state = select.value; filter(); this.closest("dialog").close();
    });
    // Prevent Enter in the local search from submitting any enclosing future layout form.
    search.addEventListener("keydown", event => { if (event.key === "Enter") event.preventDefault(); });

    const form = root.querySelector("[data-sample-form]");
    const name = form.elements.namedItem("name");
    const phone = form.elements.namedItem("phone");
    function validateField(input) {
        const valid = input === name ? input.value.trim().length > 0 : /^[+\d\s().-]+$/.test(input.value.trim()) && /^\d{9,15}$/.test(input.value.replace(/\D/g, ""));
        input.setAttribute("aria-invalid", String(!valid));
        root.querySelector(input === name ? "#ui-name-error" : "#ui-phone-error").hidden = valid;
        return valid;
    }
    [name, phone].forEach(input => input.addEventListener("input", () => {
        if (input.hasAttribute("aria-invalid")) validateField(input);
        form.querySelector("[data-form-result]").hidden = true;
    }));
    form.addEventListener("submit", function (event) {
        event.preventDefault();
        const submit = form.querySelector("[data-form-submit]");
        if (submit.disabled) return;
        const nameValid = validateField(name);
        const phoneValid = validateField(phone);
        const valid = nameValid && phoneValid;
        form.querySelector("[data-form-summary]").hidden = valid;
        form.querySelector("[data-form-result]").hidden = true;
        if (!valid) { (nameValid ? phone : name).focus(); return; }
        submit.disabled = true; submit.setAttribute("aria-busy", "true"); submit.textContent = "Đang kiểm tra…";
        setTimeout(() => {
            submit.disabled = false; submit.removeAttribute("aria-busy"); submit.textContent = "Kiểm tra mẫu";
            const result = form.querySelector("[data-form-result]");
            result.textContent = "Thông tin mẫu hợp lệ. Chưa lưu dữ liệu khách hàng.";
            result.hidden = false;
            result.scrollIntoView({ block: "nearest" });
        }, 1000);
    });
})();
