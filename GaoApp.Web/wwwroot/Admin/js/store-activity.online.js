(function () {
    'use strict';
    const url = document.body.dataset.storeOnlineUrl;
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    if (!url || !token || !window.crypto?.randomUUID) return;
    const tab = crypto.randomUUID();
    let busy = false, stopped = false;
    async function pulse() {
        if (busy || stopped) return;
        busy = true;
        const body = new FormData();
        body.append('tabId', tab); body.append('__RequestVerificationToken', token);
        try {
            const response = await fetch(url, { method: 'POST', body, credentials: 'same-origin' });
            if ([401, 403].includes(response.status)) stopped = true;
        } catch (_) { /* Missing signals expire after 60 seconds. */ }
        finally { busy = false; }
    }
    // Hidden business tabs remain logged in. Closing the last tab lets the server expire the employee.
    const timer = setInterval(pulse, 10000);
    window.addEventListener('pagehide', () => { stopped = true; clearInterval(timer); });
    window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
    window.addEventListener('online', pulse);
    pulse();
})();
