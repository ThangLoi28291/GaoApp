(function () {
    'use strict';
    const app = document.getElementById('customerDisplayApp');
    const receipt = document.getElementById('cdReceiptOverlay');
    const paper = document.getElementById('cdReceiptPaper');
    if (!app || !receipt || !paper) return;

    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let celebratedOrder = null;
    let celebrationTimer = null;
    const confetti = app.querySelector('.cd-hw-confetti');
    if (confetti) {
        for (let index = 0; index < 14; index++) {
            const piece = document.createElement('i');
            piece.style.setProperty('--n', index);
            confetti.append(piece);
        }
    }

    // Watch only committed-bill visibility/identity. Business events and totals remain in the POS player.
    function celebrateReceipt() {
        const order = paper.dataset.orderId;
        if (receipt.hidden || !order || order === celebratedOrder) return;
        celebratedOrder = order;
        clearTimeout(celebrationTimer);
        app.classList.remove('is-halloween-celebrating');
        if (reducedMotion.matches) return;
        void confetti?.offsetWidth;
        app.classList.add('is-halloween-celebrating');
        celebrationTimer = setTimeout(() => app.classList.remove('is-halloween-celebrating'), 5000);
    }
    const observer = new MutationObserver(celebrateReceipt);
    observer.observe(receipt, { attributes: true, attributeFilter: ['hidden'] });
    observer.observe(paper, { attributes: true, attributeFilter: ['data-order-id'] });

    function syncMotion() {
        app.classList.toggle('is-festival-paused', document.hidden || reducedMotion.matches);
        if (reducedMotion.matches) {
            clearTimeout(celebrationTimer);
            app.classList.remove('is-halloween-celebrating');
        }
    }
    document.addEventListener('visibilitychange', syncMotion);
    reducedMotion.addEventListener('change', syncMotion);
    syncMotion();
    window.addEventListener('pagehide', event => {
        if (event.persisted) return;
        observer.disconnect();
        clearTimeout(celebrationTimer);
        document.removeEventListener('visibilitychange', syncMotion);
        reducedMotion.removeEventListener('change', syncMotion);
    });
})();
