/* Only DOM destinations projected from this store's eligible AdminMenuItems. */
(function () {
    "use strict";
    document.addEventListener("click", event => {
        const target = event.target instanceof Element ? event.target : null;
        const display = target?.closest("[data-configured-customer-display]");
        if (display) {
            const href = display.dataset.url;
            if (!href) return;
            const url = new URL(href, location.href);
            if (url.origin !== location.origin) return;
            const features = ["popup=yes", "width=1280", "height=720", "left=1920", "top=0",
                "menubar=no", "toolbar=no", "location=no", "status=no", "resizable=yes", "scrollbars=no"].join(",");
            const popup = window.open(url.href, "GaoMartCustomerDisplay", features);
            if (popup) popup.focus();
            else window.alert("Trình duyệt đang chặn popup. Hãy cho phép popup cho trang POS.");
        }
        const opener = target?.closest("[data-configured-menu-open]");
        if (opener) {
            const trigger = document.querySelector("[data-wra-menu]");
            if (!trigger) return;
            const modal = opener.closest(".modal");
            if (modal?.classList.contains("show")) {
                modal.addEventListener("hidden.bs.modal", () => trigger.click(), { once: true });
                window.bootstrap?.Modal.getInstance(modal)?.hide();
            } else trigger.click();
        }
    });
})();
