/* UI-03b: presentation geometry only. Bootstrap, native dialogs and POS keep their focus/transaction owners. */
(function () {
    "use strict";
    const shell = document.querySelector("[data-mobile-app-shell]");
    if (!shell || shell.dataset.mobileViewportReady) return;
    shell.dataset.mobileViewportReady = "true";
    const html = document.documentElement, body = document.body;
    const phone = matchMedia("(max-width: 767.98px)");
    const viewport = window.visualViewport;
    const owner = shell.dataset.mobileAppShell;
    const assistant = document.getElementById("gaoAssistant");
    const navigation = document.querySelector("[data-admin-navigation]");
    const header = shell.querySelector(owner === "admin" ? "[data-mobile-app-header]"
        : owner === "receiving" ? ".wra-header" : owner === "pos-prime" ? "[data-pos-prime-header]" : "[data-pos-cockpit-header]");
    const dock = owner === "receiving" ? shell.querySelector(".wra-dock")
        : owner === "pos-prime" ? shell.querySelector("#posPrimeCheckoutDock")
        : owner === "pos-legacy" ? shell.querySelector(".pos-cockpit-checkout__actions")
        : shell.querySelector(".layout-container > [data-mobile-pins]");
    const previousStyles = new Map();
    let frame = 0, revealRequested = false, assistantInert = null;
    let baselineHeight = 0, layoutWidth = 0, dockHeight = 0;
    const visible = node => !!node?.isConnected && node.getClientRects().length > 0;
    function editable(node) {
        return node instanceof HTMLElement && !node.matches(":disabled,[readonly]") && !node.closest("[inert]")
            && node.matches('textarea,select,[contenteditable="true"],input:not([type="hidden"]):not([type="checkbox"]):not([type="radio"]):not([type="button"]):not([type="submit"]):not([type="reset"]):not([type="file"]):not([type="color"]):not([type="range"])');
    }
    function setMetric(name, value) {
        const property = "--gao-mobile-" + name;
        if (!previousStyles.has(property)) previousStyles.set(property, [html.style.getPropertyValue(property), html.style.getPropertyPriority(property)]);
        const next = Math.max(0, Math.round(value)) + "px";
        if (html.style.getPropertyValue(property) !== next) html.style.setProperty(property, next);
    }
    function clearMetrics() {
        previousStyles.forEach(([value, priority], name) => {
            if (value) html.style.setProperty(name, value, priority); else html.style.removeProperty(name);
        });
        previousStyles.clear();
        body.removeAttribute("data-mobile-viewport");
        body.removeAttribute("data-mobile-keyboard");
        body.removeAttribute("data-mobile-assistant-compact");
    }
    function suspendAssistant(suspend) {
        if (!assistant) return;
        if (suspend && assistantInert === null) { assistantInert = assistant.inert; assistant.inert = true; }
        else if (!suspend && assistantInert !== null) { assistant.inert = assistantInert; assistantInert = null; }
        body.toggleAttribute("data-mobile-assistant-suspended", suspend);
    }
    function overlays() {
        const nodes = Array.from(document.querySelectorAll('.modal.show,.modal.showing,.modal.hiding,.offcanvas.show,.offcanvas.showing,.offcanvas.hiding,dialog[open],.swal2-container,#employeeAccountMenu:not([hidden])'))
            .filter(node => visible(node) && (!node.matches('.swal2-container') || node.querySelector('.swal2-modal')));
        const top = nodes.findLast(node => node.matches('dialog[open]'))
            ?? nodes.findLast(node => node.matches('.swal2-container'))
            ?? nodes.findLast(node => node.id === 'employeeAccountMenu')
            ?? nodes.findLast(node => node.matches('.modal.show,.modal.showing'))
            ?? nodes.findLast(node => node.matches('.offcanvas.show,.offcanvas.showing')) ?? nodes.at(-1);
        const menu = !!navigation && !navigation.closest(".offcanvas") && html.classList.contains("layout-menu-expanded");
        const active = menu || nodes.length > 0;
        body.toggleAttribute("data-mobile-overlay", active);
        // A correction dialog belonging to the assistant remains usable in the native top layer.
        suspendAssistant(!(top && assistant?.contains(top)) && (menu || nodes.some(node => !assistant?.contains(node))));
        return { active, top, menu };
    }
    function revealField(top, bottom, state) {
        const field = document.activeElement;
        if (!editable(field) || !visible(field) || navigation?.contains(field) || field.closest(".select2-container")) return;
        if (state.top && !state.top.contains(field)) return;
        // POS Prime already scrolls its own sheets/payment form; do not run a second owner there.
        if (owner === "pos-prime" && !field.closest("#employeeAccountDrawer")) return;
        if (assistant?.contains(field) && !field.closest('dialog[open]')) return;
        let scroller = field.parentElement;
        while (scroller && scroller !== body && scroller !== html) {
            if (scroller.scrollHeight > scroller.clientHeight + 1 && /auto|scroll|overlay/.test(getComputedStyle(scroller).overflowY)) break;
            scroller = scroller.parentElement;
        }
        const inside = scroller && scroller !== body && scroller !== html;
        if (inside) {
            const bounds = scroller.getBoundingClientRect();
            top = Math.max(top, bounds.top); bottom = Math.min(bottom, bounds.bottom);
        } else {
            if (state.active || field.closest('.modal,.offcanvas,dialog[open]')) return;
            const head = visible(header) ? header.getBoundingClientRect() : null;
            if (head && head.bottom > top && head.top < bottom) top = Math.max(top, head.bottom);
            const launcher = document.getElementById("gaOpen");
            if (visible(launcher)) {
                const bounds = launcher.getBoundingClientRect();
                if (bounds.bottom > top && bounds.top < bottom) bottom = Math.min(bottom, bounds.top);
            }
        }
        top += 12; bottom -= 12;
        if (bottom <= top) return;
        const bounds = field.getBoundingClientRect();
        const delta = bounds.height > bottom - top ? bounds.top - top
            : bounds.bottom > bottom ? bounds.bottom - bottom : bounds.top < top ? bounds.top - top : 0;
        if (Math.abs(delta) < 1) return;
        if (inside) scroller.scrollTop += delta;
        else window.scrollBy({ top: delta, left: 0, behavior: "auto" });
    }
    function update() {
        frame = 0;
        if (!phone.matches) {
            clearMetrics(); suspendAssistant(false);
            body.removeAttribute("data-mobile-overlay"); body.removeAttribute("data-mobile-assistant-suspended");
            baselineHeight = 0; layoutWidth = 0; dockHeight = 0; revealRequested = false;
            return;
        }
        const state = overlays();
        // Pinch zoom belongs to the browser, not keyboard detection or layout resizing.
        if (viewport && Math.abs(viewport.scale - 1) > 0.02) { clearMetrics(); revealRequested = false; return; }
        const height = viewport?.height || window.innerHeight;
        const top = Math.max(0, viewport?.offsetTop || 0);
        const bottom = top + height;
        const layoutHeight = Math.max(window.innerHeight, html.clientHeight);
        const width = html.clientWidth || window.innerWidth;
        if (Math.abs(width - layoutWidth) > 32) { baselineHeight = layoutHeight; layoutWidth = width; }
        if (!editable(document.activeElement)) baselineHeight = Math.max(baselineHeight, layoutHeight, height);
        const keyboard = editable(document.activeElement) && Math.max(layoutHeight, baselineHeight) - height > Math.max(120, layoutHeight * 0.18);
        body.setAttribute("data-mobile-viewport", "");
        body.toggleAttribute("data-mobile-keyboard", keyboard);
        const headerHeight = visible(header) ? header.getBoundingClientRect().height : 0;
        const dockBounds = visible(dock) ? dock.getBoundingClientRect() : null;
        if (dockBounds?.height) dockHeight = dockBounds.height;
        const reserved = dock ? dockHeight || (owner === "receiving" ? 188 : owner === "admin" ? 88 : 0) : 0;
        let clearance = 0;
        if (!state.active && dockBounds && dockBounds.top < bottom && dockBounds.bottom > top && dockBounds.bottom >= bottom - 80)
            clearance = Math.min(height, bottom - dockBounds.top);
        const overlayHeading = state.top?.querySelector('.modal-header,.offcanvas-header,.gao-menu-review-heading,.ga-correction-header,.swal2-title,.gao-account-menu-heading')
            ?? (state.menu ? navigation?.querySelector(".app-brand") : header);
        const headingBottom = visible(overlayHeading) ? overlayHeading.getBoundingClientRect().bottom : top;
        setMetric("visible-height", height);
        setMetric("visible-top", top);
        setMetric("visible-bottom", Math.max(0, window.innerHeight - height - top));
        setMetric("header-height", headerHeight);
        setMetric("dock-height", reserved);
        setMetric("dock-clearance", clearance);
        setMetric("toast-top", Math.max(top + 12, Math.min(headingBottom + 12, bottom - 64)));
        const panel = document.getElementById("gaPanel");
        body.toggleAttribute("data-mobile-assistant-compact", !!panel && !panel.hidden && height - clearance - 100 < 220);
        if (revealRequested) { revealRequested = false; revealField(top, bottom - clearance, state); }
    }
    function schedule(reveal = false) {
        revealRequested ||= reveal;
        if (!frame) frame = requestAnimationFrame(update);
    }
    phone.addEventListener("change", () => schedule());
    window.addEventListener("resize", () => schedule(true), { passive: true });
    window.addEventListener("scroll", () => schedule(), { passive: true });
    viewport?.addEventListener("resize", () => schedule(true), { passive: true });
    viewport?.addEventListener("scroll", () => schedule(), { passive: true });
    document.addEventListener("focusin", () => schedule(true));
    document.addEventListener("focusout", () => schedule());
    for (const name of ['shown.bs.modal','hidden.bs.modal','shown.bs.offcanvas','hidden.bs.offcanvas'])
        document.addEventListener(name, () => schedule());
    for (const name of ['gao-assistant:opened','gao-assistant:closed']) window.addEventListener(name, () => schedule());
    window.addEventListener("pageshow", () => schedule());
    const changes = new MutationObserver(records => {
        if (records.some(record => record.target === html || record.target === body
            || (record.type === "attributes" && record.target.matches('.modal,.offcanvas,dialog,#gaPanel,.swal2-container,#employeeAccountMenu'))
            || (record.type === "childList" && [...record.addedNodes, ...record.removedNodes].some(node => node instanceof Element
                && (node.matches('.modal,.offcanvas,dialog,.swal2-container,#employeeAccountMenu') || node.querySelector('.modal,.offcanvas,dialog,.swal2-container,#employeeAccountMenu')))))) schedule();
    });
    changes.observe(body, { attributes: true, attributeFilter: ["class", "open", "hidden"], subtree: true, childList: true });
    changes.observe(html, { attributes: true, attributeFilter: ["class"] });
    if (window.ResizeObserver) {
        const sizes = new ResizeObserver(() => schedule());
        if (header) sizes.observe(header);
        if (dock) sizes.observe(dock);
    }
    schedule();
})();
