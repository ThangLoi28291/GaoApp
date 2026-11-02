/* UI-03e: one native login form; phone presentation and viewport only. No credential reads or requests. */
(function () {
    "use strict";
    const root = document.getElementById("loginHalloween");
    if (!root || !document.body.hasAttribute("data-mobile-login") || root.dataset.mobileLoginReady) return;
    root.dataset.mobileLoginReady = "true";
    const phone = matchMedia("(max-width: 767.98px)");
    const form = document.getElementById("loginForm"), password = document.getElementById("PasswordInput");
    const toggle = document.getElementById("btnTogglePassword"), submit = document.getElementById("btnLogin");
    const summary = document.getElementById("loginValidationSummary");
    if (!form || !password || !toggle || !submit) return;
    const label = toggle.querySelector(".btn-eye-label");
    const desktopLabel = toggle.getAttribute("aria-label");
    const submitText = submit.textContent;
    const previousBusy = form.getAttribute("aria-busy");
    const metrics = new Map();
    let wasPhone = false, frame = 0, revealRequested = false;
    function masked(desktop = false) {
        password.type = "password";
        toggle.setAttribute("aria-pressed", "false");
        toggle.setAttribute("aria-label", desktop ? desktopLabel : "Hiện mật khẩu");
        if (label) label.textContent = "Hiện";
    }
    toggle.addEventListener("click", () => {
        if (!phone.matches || submit.disabled) return;
        const show = password.type === "password";
        password.type = show ? "text" : "password";
        toggle.setAttribute("aria-pressed", String(show));
        toggle.setAttribute("aria-label", show ? "Ẩn mật khẩu" : "Hiện mật khẩu");
        if (label) label.textContent = show ? "Ẩn" : "Hiện";
    });
    function busy() {
        if (!phone.matches) return;
        root.toggleAttribute("data-mobile-login-busy", submit.disabled);
        form.setAttribute("aria-busy", String(submit.disabled));
    }
    new MutationObserver(busy).observe(submit, { attributes: true, attributeFilter: ["disabled"] });
    function clearMetrics() {
        metrics.forEach(([value, priority], name) => {
            if (value) root.style.setProperty(name, value, priority); else root.style.removeProperty(name);
        });
        metrics.clear();
    }
    function metric(name, value) {
        const property = "--login-mobile-" + name;
        if (!metrics.has(property)) metrics.set(property, [root.style.getPropertyValue(property), root.style.getPropertyPriority(property)]);
        const next = Math.max(0, Math.round(value)) + "px";
        if (root.style.getPropertyValue(property) !== next) root.style.setProperty(property, next);
    }
    function reveal(top, bottom) {
        const field = document.activeElement;
        if (!(field instanceof HTMLElement) || !root.contains(field) || !field.getClientRects().length) return;
        if (field !== summary && !field.matches('input:not([type="hidden"]),select')) return;
        const bounds = field.getBoundingClientRect();
        top += 16; bottom -= 20;
        if (bottom <= top) return;
        const delta = bounds.height > bottom - top ? bounds.top - top
            : bounds.bottom > bottom ? bounds.bottom - bottom : bounds.top < top ? bounds.top - top : 0;
        if (Math.abs(delta) > 1) window.scrollBy({ top: delta, left: 0, behavior: "auto" });
    }
    function update() {
        frame = 0;
        if (!phone.matches) { clearMetrics(); revealRequested = false; return; }
        const viewport = window.visualViewport;
        if (viewport && Math.abs(viewport.scale - 1) > 0.02) { clearMetrics(); revealRequested = false; return; }
        const height = viewport?.height || window.innerHeight;
        const top = Math.max(0, viewport?.offsetTop || 0);
        metric("height", height);
        metric("occlusion", window.innerHeight - height - top);
        if (revealRequested) { revealRequested = false; reveal(top, top + height); }
    }
    function schedule(revealField = false) {
        revealRequested ||= revealField;
        if (!frame) frame = requestAnimationFrame(update);
    }
    function sync() {
        if (phone.matches) { if (!wasPhone) masked(); busy(); }
        else {
            if (wasPhone) masked(true);
            root.removeAttribute("data-mobile-login-busy");
            if (wasPhone) {
                if (previousBusy === null) form.removeAttribute("aria-busy"); else form.setAttribute("aria-busy", previousBusy);
            }
        }
        wasPhone = phone.matches; schedule();
    }
    phone.addEventListener("change", sync);
    window.addEventListener("resize", () => schedule(true), { passive: true });
    window.visualViewport?.addEventListener("resize", () => schedule(true), { passive: true });
    window.visualViewport?.addEventListener("scroll", () => schedule(), { passive: true });
    root.addEventListener("focusin", () => { if (phone.matches) schedule(true); });
    form.addEventListener("submit", () => { if (phone.matches) { masked(); schedule(true); } });
    document.addEventListener("visibilitychange", () => { if (phone.matches && document.hidden) masked(); });
    window.addEventListener("blur", () => { if (phone.matches) masked(); });
    window.addEventListener("pagehide", () => { if (phone.matches) masked(); });
    window.addEventListener("pageshow", event => {
        if (!event.persisted || !phone.matches) return;
        masked(); submit.disabled = false; submit.textContent = submitText; busy(); schedule();
    });
    function initialError() {
        if (!phone.matches || !summary?.classList.contains("validation-summary-errors")) return;
        const active = document.activeElement;
        if (!root.contains(active) || active.id === "UserName") { summary.focus({ preventScroll: true }); schedule(true); }
    }
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialError, { once: true }); else initialError();
    sync();
})();
