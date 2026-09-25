(function () {
    'use strict';

    const shellSelector = '.pos-cockpit-v2';

    let resizeFrame = 0;
    let cartObserver = null;

    function getShell() {
        return document.querySelector(shellSelector);
    }

    function getBarcodeInput() {
        return document.getElementById('txtBarcode');
    }

    function focusBarcode() {
        const input = getBarcodeInput();
        if (!input) return;

        input.focus();

        try {
            input.setSelectionRange(
                input.value.length,
                input.value.length
            );
        } catch {
            // Number/non-text input would not support selection range.
        }
    }

    function startCommand(prefix) {
        const input = getBarcodeInput();
        if (!input) return;

        input.value = prefix || '';
        input.focus();

        input.dispatchEvent(new Event('input', {
            bubbles: true
        }));

        try {
            input.setSelectionRange(
                input.value.length,
                input.value.length
            );
        } catch {
            // Ignore selection-range limitations.
        }
    }

    function bindCommandHints() {
        document
            .querySelectorAll('[data-pos-command-focus]')
            .forEach(button => {
                button.addEventListener('click', function () {
                    focusBarcode();
                });
            });

        document
            .querySelectorAll('[data-pos-command-prefix]')
            .forEach(button => {
                button.addEventListener('click', function () {
                    startCommand(
                        button.getAttribute('data-pos-command-prefix') || ''
                    );
                });
            });
    }

    function getShortcutPanel() {
        return document.getElementById('posCockpitShortcutPanel');
    }

    function setShortcutPanelOpen(open) {
        const panel = getShortcutPanel();
        if (!panel) return;

        panel.hidden = !open;

        document
            .querySelectorAll('[data-pos-shortcuts-toggle]')
            .forEach(button => {
                button.setAttribute(
                    'aria-expanded',
                    open ? 'true' : 'false'
                );
            });
    }

    function bindShortcutPanel() {
        document
            .querySelectorAll('[data-pos-shortcuts-toggle]')
            .forEach(button => {
                button.addEventListener('click', function (event) {
                    event.stopPropagation();

                    const panel = getShortcutPanel();
                    if (!panel) return;

                    setShortcutPanelOpen(panel.hidden);
                });
            });

        document
            .querySelectorAll('[data-pos-shortcuts-close]')
            .forEach(button => {
                button.addEventListener('click', function () {
                    setShortcutPanelOpen(false);
                    focusBarcode();
                });
            });

        document.addEventListener('click', function (event) {
            const panel = getShortcutPanel();

            if (!panel || panel.hidden) {
                return;
            }

            const target = event.target;

            if (!(target instanceof Node)) {
                return;
            }

            if (
                panel.contains(target) ||
                target.closest?.('[data-pos-shortcuts-toggle]')
            ) {
                return;
            }

            setShortcutPanelOpen(false);
        });

        document.addEventListener('keydown', function (event) {
            if (event.key !== 'Escape') {
                return;
            }

            const panel = getShortcutPanel();

            if (panel && !panel.hidden) {
                setShortcutPanelOpen(false);
            }
        });
    }

    function bindCustomerDisplay() {
        const button =
            document.getElementById('btnOpenCustomerDisplay');

        if (!button) return;

        button.addEventListener('click', function () {
            const url =
                button.getAttribute('data-url') ||
                '/admin/pos/customer-display';

            const features = [
                'popup=yes',
                'width=1280',
                'height=720',
                'left=1920',
                'top=0',
                'menubar=no',
                'toolbar=no',
                'location=no',
                'status=no',
                'resizable=yes',
                'scrollbars=no'
            ].join(',');

            const popup = window.open(
                url,
                'GaoMartCustomerDisplay',
                features
            );

            if (popup) {
                popup.focus();
                return;
            }

            window.alert(
                'Trình duyệt đang chặn popup. Hãy cho phép popup cho trang POS.'
            );
        });
    }

    function updateCartCount() {
        const body =
            document.getElementById('currentDraftBody');

        const counter =
            document.getElementById('posCockpitCartCount');

        if (!body || !counter) {
            return;
        }

        const count =
            body.querySelectorAll('tr[data-line-id]').length;

        counter.textContent =
            `${count} ${count === 1 ? 'sản phẩm' : 'sản phẩm'}`;
    }

    function bindCartCount() {
        const body =
            document.getElementById('currentDraftBody');

        if (!body) {
            return;
        }

        updateCartCount();

        cartObserver?.disconnect();

        cartObserver =
            new MutationObserver(updateCartCount);

        cartObserver.observe(body, {
            childList: true,
            subtree: false
        });
    }

    function syncAvailableHeight() {
        const shell = getShell();
        if (!shell) return;

        if (!window.matchMedia('(min-width: 992px)').matches) {
            shell.style.removeProperty(
                '--pos-cockpit-available-height'
            );
            return;
        }

        const rect = shell.getBoundingClientRect();

        const available =
            Math.max(
                560,
                Math.floor(
                    window.innerHeight -
                    rect.top -
                    18
                )
            );

        shell.style.setProperty(
            '--pos-cockpit-available-height',
            `${available}px`
        );
    }

    function bindViewportSync() {
        syncAvailableHeight();

        window.addEventListener('resize', function () {
            if (resizeFrame) {
                window.cancelAnimationFrame(resizeFrame);
            }

            resizeFrame =
                window.requestAnimationFrame(
                    syncAvailableHeight
                );
        });
    }

    function init() {
        if (!getShell()) {
            return;
        }

        bindCommandHints();
        bindShortcutPanel();
        bindCustomerDisplay();
        bindCartCount();
        bindViewportSync();
    }

    if (document.readyState === 'loading') {
        document.addEventListener(
            'DOMContentLoaded',
            init,
            { once: true }
        );
    } else {
        init();
    }

    window.PosCockpit = Object.freeze({
        init,
        syncAvailableHeight,
        updateCartCount
    });
})();