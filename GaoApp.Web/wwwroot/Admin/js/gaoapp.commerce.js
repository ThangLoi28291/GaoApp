/* Declarative native dialogs only. No business requests, menu state or form submit handlers. */
(function () {
    "use strict";
    if (window.GaoCommerce) return;
    const scope = '[data-gao-ui="modern"][data-gao-theme="commerce"]';
    const openers = new WeakMap();
    const scrollLocks = new WeakMap();
    const initialized = new WeakSet();

    function prepare(dialog) {
        if (initialized.has(dialog)) return;
        initialized.add(dialog);
        dialog.addEventListener("close", function () {
            const previous = scrollLocks.get(dialog);
            if (previous) {
                document.documentElement.style.overflow = previous.overflow;
                document.documentElement.style.scrollbarGutter = previous.gutter;
                scrollLocks.delete(dialog);
            }
            const opener = openers.get(dialog);
            if (opener && opener.isConnected) opener.focus({ preventScroll: true });
            openers.delete(dialog);
        });
    }

    function open(dialog, opener) {
        if (!(dialog instanceof HTMLDialogElement) || !dialog.closest(scope) || dialog.open) return false;
        // One top-layer interaction at a time. Do not stack on existing Bootstrap or native dialogs.
        if (document.querySelector('dialog[open], .modal.show, .offcanvas.show')) return false;
        prepare(dialog);
        openers.set(dialog, opener || document.activeElement);
        dialog.showModal();
        scrollLocks.set(dialog, {
            overflow: document.documentElement.style.overflow,
            gutter: document.documentElement.style.scrollbarGutter
        });
        // App-style screens scroll inside their content region; reserving a document
        // scrollbar there would shrink the whole phone layout when a sheet opens.
        if (document.documentElement.scrollHeight > document.documentElement.clientHeight) {
            document.documentElement.style.scrollbarGutter = "stable";
        }
        document.documentElement.style.overflow = "hidden";
        return true;
    }

    document.addEventListener("click", function (event) {
        if (!(event.target instanceof Element)) return;
        const opener = event.target.closest("[data-gao-dialog-open]");
        if (opener && opener.closest(scope)) {
            const dialog = document.getElementById(opener.dataset.gaoDialogOpen);
            if (open(dialog, opener)) event.preventDefault();
        }
        const closer = event.target.closest("[data-gao-dialog-close]");
        if (closer && closer.closest(scope)) {
            const dialog = closer.closest("dialog");
            if (dialog && dialog.open) dialog.close();
        }
    });
    document.addEventListener("keydown", function (event) {
        if (event.key !== "Tab") return;
        const dialog = document.querySelector("dialog.gds-dialog[open]");
        if (!dialog || !dialog.closest(scope)) return;
        const controls = Array.from(dialog.querySelectorAll('a[href],button,input,select,textarea,[tabindex]'))
            .filter(control => !control.disabled && control.tabIndex >= 0 && control.getClientRects().length);
        if (!controls.length) { event.preventDefault(); return; }
        const first = controls[0];
        const last = controls[controls.length - 1];
        const active = document.activeElement;
        if (event.shiftKey && (active === first || !dialog.contains(active))) {
            event.preventDefault(); last.focus();
        } else if (!event.shiftKey && (active === last || !dialog.contains(active))) {
            event.preventDefault(); first.focus();
        }
    });
    window.GaoCommerce = Object.freeze({ openDialog: open });
})();
