/* References only: shortcut labels, icons and destinations come from the rendered menu. */
(function () {
    "use strict";
    const bar = document.querySelector("[data-mobile-pins]"), menu = document.querySelector("[data-admin-navigation]");
    if (!bar || !menu || bar.dataset.pinsReady) return;
    bar.dataset.pinsReady = "true";
    const phone = matchMedia("(max-width: 767.98px)"), menuButton = bar.querySelector("[data-pins-menu]");
    let loaded = false, pending = null;
    let defaults = [];
    try { defaults = JSON.parse(bar.querySelector('[data-mobile-pin-defaults]')?.textContent || '[]'); } catch (_) { /* Keep the server-rendered menu if metadata cannot be read. */ }
    if (!Array.isArray(defaults)) defaults = [];
    const currentSection = defaults.find(tab => tab.current)?.key;
    const normalizeDefaults = tabs => tabs.filter(tab => Number.isSafeInteger(tab?.id) && tab.id > 0 &&
        ['overview', 'orders', 'receiving', 'sales'].includes(tab.key) && typeof tab.label === 'string').slice(0, 4);
    defaults = normalizeDefaults(defaults);
    function render(ids) {
        const focused = document.activeElement;
        const focusedId = focused?.closest("[data-mobile-pin-id]")?.dataset.mobilePinId;
        const restoreFocus = !!focusedId && bar.contains(focused);
        bar.querySelectorAll("a").forEach(node => node.remove());
        const destinations = new Map(Array.from(menu.querySelectorAll("[data-nav-destination]"))
            .map(anchor => [Number(anchor.closest("[data-nav-menu-id]").dataset.navMenuId), anchor]));
        const configured = Array.from(new Set(ids.filter(id => Number.isSafeInteger(id) && id > 0))).slice(0, 4);
        const useDefaults = configured.length === 0;
        const selected = useDefaults ? defaults.map(tab => tab.id) : configured;
        selected.forEach(id => {
            const source = destinations.get(id);
            if (!source) return; // Hidden/disabled/unauthorized/deleted items never receive a shortcut.
            const anchor = document.createElement("a"), label = document.createElement("span");
            const title = source.querySelector(".gao-nav-label").textContent;
            const tab = useDefaults ? defaults.find(item => item.id === id) : null;
            const display = tab?.label || title;
            anchor.className = "gao-mobile-pin"; anchor.href = source.getAttribute("href");
            anchor.dataset.mobilePinId = String(id);
            anchor.title = title; anchor.setAttribute("aria-label", display);
            if (tab ? tab.key === currentSection : source.getAttribute("aria-current") === "page") anchor.setAttribute("aria-current", "page");
            const icon = source.querySelector("i").cloneNode(false);
            icon.classList.remove("menu-icon"); icon.setAttribute("aria-hidden", "true");
            label.textContent = display; anchor.append(icon, label); bar.insertBefore(anchor, menuButton);
        });
        if (bar.querySelector('a[aria-current="page"]')) menuButton.removeAttribute('aria-current');
        else menuButton.setAttribute('aria-current', 'page');
        if (restoreFocus) {
            const replacement = Array.from(bar.querySelectorAll("[data-mobile-pin-id]"))
                .find(anchor => anchor.dataset.mobilePinId === focusedId);
            const fallback = bar.closest(".offcanvas")?.querySelector('[data-bs-dismiss="offcanvas"]') ?? menuButton;
            (replacement ?? fallback)?.focus({ preventScroll: true });
        }
        bar.dispatchEvent(new CustomEvent("gao:mobile-pins-rendered"));
    }
    function cancelLoad() { pending?.abort(); pending = null; }
    const isCurrent = controller => pending === controller && !controller.signal.aborted && phone.matches;
    async function load() {
        if (!phone.matches || loaded || pending || !bar.dataset.pinsUrl || document.body.classList.contains("wrd-app-mode")) return;
        const controller = new AbortController(); pending = controller;
        try {
            const response = await fetch(bar.dataset.pinsUrl, { credentials: "same-origin", cache: "no-store", signal: controller.signal, headers: { Accept: "application/json" } });
            if (!isCurrent(controller) || !response.ok || response.redirected) return;
            const data = await response.json();
            if (isCurrent(controller) && Array.isArray(data.menuIds)) {
                if (Array.isArray(data.defaultTabs)) defaults = normalizeDefaults(data.defaultTabs);
                render(data.menuIds); loaded = true;
            }
        } catch (_) { /* Menu remains usable when pins cannot be read. */ }
        finally { if (pending === controller) pending = null; }
    }
    phone.addEventListener("change", () => { if (!phone.matches) cancelLoad(); else load(); });
    document.addEventListener("gao:menu-pins-saved", event => {
        if (phone.matches && Array.isArray(event.detail?.menuIds)) {
            cancelLoad(); // An older GET must not overwrite the settings just saved.
            render(event.detail.menuIds); loaded = true;
        }
    });
    // Retry a failed first read on an explicit menu open or restored connectivity; no polling.
    document.addEventListener("click", event => {
        if (event.target instanceof Element && event.target.closest('.layout-menu-toggle,[data-bs-target="#posPrimeAdminDrawer"]')) load();
    });
    window.addEventListener("online", load);
    new MutationObserver(() => menuButton.setAttribute("aria-expanded", String(document.documentElement.classList.contains("layout-menu-expanded"))))
        .observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    load();
})();
