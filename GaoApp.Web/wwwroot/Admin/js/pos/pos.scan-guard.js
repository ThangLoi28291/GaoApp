(function () {
    'use strict';
    const dialog = document.getElementById('posScanGuard');
    if (!dialog) return;
    let acknowledge = null, scanned = false, quietUntil = 0, releaseEnter = false;
    const isOpen = () => dialog.open;
    const stop = event => { event.preventDefault(); event.stopImmediatePropagation(); };
    function close() {
        if (!isOpen()) return;
        dialog.close();
        const callback = acknowledge;
        acknowledge = null;
        callback?.();
    }
    dialog.addEventListener('cancel', event => event.preventDefault());
    document.getElementById('posScanGuardConfirm').addEventListener('click', close);
    // Capture before POS hotkeys. A scanner's characters + terminator must never
    // acknowledge a warning; require a separate Enter after the scan has ended.
    window.addEventListener('keydown', event => {
        if (!isOpen()) return;
        if (event.key === 'Tab') { event.stopImmediatePropagation(); return; }
        stop(event);
        if (event.key === 'Enter') {
            releaseEnter = true;
            const canClose = !scanned && Date.now() >= quietUntil && !event.repeat
                && !event.isComposing && !event.ctrlKey && !event.altKey && !event.metaKey;
            scanned = false;
            quietUntil = Date.now() + 500;
            if (canClose) close();
        } else if (event.key.length === 1 || event.isComposing || event.key === 'Unidentified') {
            scanned = true;
            quietUntil = Date.now() + 500;
        }
    }, true);
    window.addEventListener('keyup', event => {
        if (isOpen() || (releaseEnter && event.key === 'Enter')) stop(event);
        if (event.key === 'Enter') releaseEnter = false;
    }, true);
    window.addEventListener('paste', event => { if (isOpen()) { stop(event); scanned = true; } }, true);
    window.PosScanGuard = {
        isOpen,
        show(barcode, message, onAcknowledge) {
            if (isOpen()) return;
            document.getElementById('posScanGuardCode').textContent = barcode;
            document.getElementById('posScanGuardMessage').textContent = message;
            acknowledge = onAcknowledge;
            scanned = false;
            quietUntil = Date.now() + 500;
            dialog.showModal();
        }
    };
})();
