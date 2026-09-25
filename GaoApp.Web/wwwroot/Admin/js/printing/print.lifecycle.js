(function () {
    'use strict';

    // A print dialog may return immediately (Safari). Wait for its completion event,
    // never close a bill merely because print() returned or a fixed timer elapsed.
    function watch(printWindow, onComplete) {
        let finished = false, enteredPrintMedia = false;
        const media = printWindow.matchMedia?.('print');
        function cleanup() {
            printWindow.removeEventListener('afterprint', complete);
            if (media?.removeEventListener) media.removeEventListener('change', mediaChanged);
            else media?.removeListener(mediaChanged);
        }
        function complete() {
            if (finished) return;
            finished = true;
            cleanup();
            window.setTimeout(onComplete, 0);
        }
        function mediaChanged(event) {
            if (event.matches) enteredPrintMedia = true;
            else if (enteredPrintMedia) complete();
        }
        printWindow.addEventListener('afterprint', complete);
        if (media?.addEventListener) media.addEventListener('change', mediaChanged);
        else media?.addListener(mediaChanged);
        return function cancel() { finished = true; cleanup(); };
    }

    // Only call for a dedicated print page/popup, never the POS or template editor.
    function close(printPage) {
        if (printPage.closed) return;
        if (printPage.parent !== printPage) {
            printPage.frameElement?.remove();
            return;
        }
        try { if (printPage.opener && !printPage.opener.closed) printPage.opener.focus(); } catch { /* noopener */ }
        printPage.close();
    }

    function autoClose(printPage = window) {
        return watch(printPage, () => close(printPage));
    }

    window.GaoPrintLifecycle = { watch, close, autoClose };
    if (document.currentScript?.hasAttribute('data-auto-close')) autoClose();
})();
