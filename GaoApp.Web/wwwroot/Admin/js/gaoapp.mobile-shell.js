/* Presentation only: move the existing account/utility controls; transaction modules keep their headers/docks. */
(function () {
    "use strict";
    const shell = document.querySelector("[data-mobile-app-shell]");
    if (!shell || shell.dataset.mobileShellReady) return;
    shell.dataset.mobileShellReady = "true";
    const phone = matchMedia("(max-width: 767.98px)");
    const control = document.querySelector("[data-account-control]");
    const trigger = document.getElementById("employeeAccountTrigger");
    const moves = [];
    function register(node, target, account = false) {
        if (!node || !target || (account && node.hidden)) return;
        const marker = document.createComment("UI-03a desktop control position");
        node.before(marker);
        moves.push({ node, target, marker, account });
    }
    register(control, shell.querySelector("[data-mobile-account-slot]"), true);
    register(shell.querySelector("[data-admin-utility-slot]"), shell.querySelector("[data-mobile-utilities-slot]"));
    const storeName = document.querySelector("[data-employee-account] .gao-account-store > span")?.textContent.trim();
    const storeLabel = shell.querySelector("[data-mobile-store-label]");
    if (storeLabel && storeName) { storeLabel.textContent = storeName; storeLabel.title = storeName; }
    const menuButtons = shell.querySelectorAll('.gao-mobile-shell-menu[aria-controls="layout-menu"]');
    function syncMenuState() {
        const expanded = phone.matches && document.documentElement.classList.contains("layout-menu-expanded");
        menuButtons.forEach(button => button.setAttribute("aria-expanded", String(expanded)));
    }
    function sync() {
        for (const move of moves) {
            const parent = phone.matches ? move.target : move.marker.parentNode;
            if (move.node.parentNode === parent) continue;
            const focused = document.activeElement;
            const wasFocused = move.node.contains(focused);
            const accountOpen = move.account && trigger?.getAttribute("aria-expanded") === "true";
            if (accountOpen) trigger.click(); // Existing account handler closes its own popup.
            if (phone.matches) move.target.append(move.node); else move.marker.after(move.node);
            if (wasFocused || accountOpen) (accountOpen ? trigger : focused)?.focus({ preventScroll: true });
        }
        shell.toggleAttribute("data-mobile-header-ready", phone.matches && moves.some(move => move.account));
        syncMenuState();
    }
    phone.addEventListener("change", sync);
    if (menuButtons.length) new MutationObserver(syncMenuState).observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    sync();
})();
