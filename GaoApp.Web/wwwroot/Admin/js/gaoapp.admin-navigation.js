/* Search/disclosure/focus for the configured navigation. No requests or stored menu data. */
(function () {
    "use strict";
    const root = document.querySelector("[data-admin-navigation]");
    if (!root || root.dataset.navigationReady) return;
    root.dataset.navigationReady = "true";
    const input = root.querySelector("#admin-nav-search");
    const list = root.querySelector(".menu-inner");
    const status = root.querySelector("[data-nav-status]");
    const noResults = root.querySelector("[data-nav-no-results]");
    const drawer = root.closest(".offcanvas");
    const small = matchMedia("(max-width: 1199.98px)");
    const phone = matchMedia("(max-width: 767.98px)");
    const normalize = value => value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLocaleLowerCase("vi").replace(/đ/g, "d").trim();
    const direct = (node, selector) => Array.from(node.children).find(child => child.matches(selector));
    let returnFocus = null, modalOpen = false, previousOverflow = "", previousInert = false;
    const page = document.querySelector(".layout-page");
    let bodyLock = null;
    function syncPhoneScrollLock(open) {
        // One owner: only this existing menu controller locks the page, never a second modal controller.
        const body = document.body;
        if (open && phone.matches && !bodyLock) {
            const properties = ["position", "top", "left", "right", "width"];
            bodyLock = { x: window.scrollX, y: window.scrollY, styles: properties.map(name =>
                [name, body.style.getPropertyValue(name), body.style.getPropertyPriority(name)]) };
            body.style.setProperty("position", "fixed");
            body.style.setProperty("top", -bodyLock.y + "px");
            body.style.setProperty("left", -bodyLock.x + "px");
            body.style.setProperty("right", "0");
            body.style.setProperty("width", "100%");
        } else if ((!open || !phone.matches) && bodyLock) {
            const saved = bodyLock; bodyLock = null;
            saved.styles.forEach(([name, value, priority]) => {
                if (value) body.style.setProperty(name, value, priority); else body.style.removeProperty(name);
            });
            const html = document.documentElement;
            const behavior = html.style.getPropertyValue("scroll-behavior"), priority = html.style.getPropertyPriority("scroll-behavior");
            html.style.setProperty("scroll-behavior", "auto", "important");
            window.scrollTo(saved.x, saved.y);
            if (behavior) html.style.setProperty("scroll-behavior", behavior, priority); else html.style.removeProperty("scroll-behavior");
        }
    }

    function search() {
        const query = normalize(input.value);
        let destinations = 0;
        function visit(ul, parentMatches) {
            let hasMatches = false;
            Array.from(ul.children).filter(node => node.hasAttribute("data-nav-node")).forEach(node => {
                const ownMatches = !query || parentMatches || normalize(node.dataset.navTitle).includes(query);
                const children = direct(node, ".menu-sub");
                const childMatches = children ? visit(children, ownMatches) : false;
                node.hidden = !ownMatches && !childMatches;
                node.classList.toggle("gao-nav-search-open", !!query && !node.hidden && !!children);
                const branch = direct(node, "[data-nav-branch]");
                if (branch) branch.setAttribute("aria-expanded", String(node.classList.contains("open") || node.classList.contains("gao-nav-search-open")));
                if (!node.hidden && direct(node, "[data-nav-destination]")) destinations++;
                hasMatches ||= !node.hidden;
            });
            return hasMatches;
        }
        const any = visit(list, false);
        noResults.hidden = !query || any;
        list.hidden = !any;
        root.querySelector("[data-nav-clear]").hidden = !input.value;
        status.textContent = query ? destinations + " chức năng phù hợp" : destinations + " chức năng trong menu của bạn";
    }
    function reset() { input.value = ""; search(); input.focus(); }
    function syncVisibility() {
        // Bootstrap owns POS drawer focus, scroll lock and modal semantics.
        if (drawer) return;
        // Desktop uses the classic menu without a search field. Do not carry
        // a hidden phone/tablet filter over when the viewport becomes desktop.
        if (!small.matches && input.value) { input.value = ""; search(); }
        const open = small.matches && document.documentElement.classList.contains("layout-menu-expanded");
        syncPhoneScrollLock(open);
        root.inert = small.matches && !open;
        if (small.matches && !open) root.setAttribute("aria-hidden", "true"); else root.removeAttribute("aria-hidden");
        if (open === modalOpen) return;
        modalOpen = open;
        if (open) {
            root.setAttribute("role", "dialog"); root.setAttribute("aria-modal", "true");
            previousOverflow = document.documentElement.style.overflow;
            document.documentElement.style.overflow = "hidden";
            if (page) { previousInert = page.inert; page.inert = true; }
            root.querySelector("[data-nav-close]").focus({ preventScroll: true });
        } else {
            root.removeAttribute("role"); root.removeAttribute("aria-modal");
            document.documentElement.style.overflow = previousOverflow;
            if (page) page.inert = previousInert;
            if (returnFocus?.isConnected && small.matches) returnFocus.focus({ preventScroll: true });
        }
    }
    document.addEventListener("click", event => {
        const opener = event.target instanceof Element && event.target.closest(".layout-menu-toggle");
        if (opener && !modalOpen && !root.contains(opener)) returnFocus = opener;
    }, true);
    // Own only opt-in branch clicks, preserving vendor selectors without running two toggles.
    root.addEventListener("click", event => {
        const branch = event.target instanceof Element && event.target.closest("[data-nav-branch]");
        if (!branch) return;
        event.preventDefault(); event.stopImmediatePropagation();
        if (input.value.trim()) return; // Search reveals matching paths; clear it to collapse groups.
        const node = branch.closest("[data-nav-node]");
        node.classList.toggle("open"); branch.setAttribute("aria-expanded", String(node.classList.contains("open")));
    }, true);
    root.addEventListener("click", event => {
        if (event.target instanceof Element && event.target.closest("[data-nav-clear],[data-nav-reset]")) reset();
    });
    root.addEventListener("keydown", event => {
        if (event.key === "Escape" && input.value) { event.preventDefault(); event.stopPropagation(); reset(); return; }
        if (!modalOpen) return;
        if (event.key === "Escape") {
            event.preventDefault(); event.stopPropagation();
            root.querySelector("[data-nav-close]").click();
        }
        if (event.key === "Tab") {
            const controls = Array.from(root.querySelectorAll("a[href],button,input,[tabindex]"))
                .filter(el => !el.disabled && el.tabIndex >= 0 && el.getClientRects().length);
            const first = controls[0], last = controls[controls.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
        }
    });
    input.addEventListener("input", search);
    const hasMenus = !!list.querySelector("[data-nav-node]");
    root.querySelector("[data-nav-search-region]").hidden = !hasMenus;
    status.hidden = !hasMenus;
    search(); syncVisibility();
    new MutationObserver(syncVisibility).observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    small.addEventListener("change", syncVisibility);
    phone.addEventListener("change", syncVisibility);
})();
