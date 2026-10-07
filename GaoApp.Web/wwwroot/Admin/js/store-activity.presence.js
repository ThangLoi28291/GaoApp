(function () {
    'use strict';
    let ticket = document.body.dataset.storeActivityTicket;
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    if (!ticket || !token || !window.crypto?.randomUUID) return;
    const tabId = crypto.randomUUID();
    const endpoint = new URL('admin/api/store-activity/presence', document.querySelector('base')?.href || location.origin + '/');
    // Honor PathBase when GaoApp is hosted under a prefix.
    const assets = document.documentElement.dataset.assetsPath;
    if (assets) endpoint.pathname = assets.replace(/\/$/, '') + '/admin/api/store-activity/presence';
    let lastEdit = 0, lastSent = 0, busy = false, stopped = false, editTimer, pendingTicket = false;
    const form = state => {
        const body = new FormData();
        body.append('ticket', ticket); body.append('tabId', tabId); body.append('state', state);
        body.append('__RequestVerificationToken', token);
        return body;
    };
    async function send(state) {
        if (stopped || busy) return;
        busy = true; lastSent = Date.now();
        try {
            const response = await fetch(endpoint, { method: 'POST', body: form(state), credentials: 'same-origin', keepalive: true });
            if ([401, 403].includes(response.status)) stopped = true;
        } catch (_) { /* The server expires missing heartbeats; business work continues. */ }
        finally {
            busy = false;
            if (pendingTicket && !stopped && !document.hidden) { pendingTicket = false; send('viewing'); }
        }
    }
    // In-page task switches receive only a server-issued, protected ticket after an authorized read.
    window.addEventListener('gao:store-activity-ticket', event => {
        if (typeof event.detail !== 'string' || !event.detail || event.detail === ticket) return;
        clearTimeout(editTimer); lastEdit = 0; ticket = event.detail;
        if (!document.hidden) { if (busy) pendingTicket = true; else send('viewing'); }
    });
    function interact(event) {
        if (document.hidden || stopped || !event.isTrusted) return;
        if (event.type === 'pointerdown' && !event.target.closest('input,textarea,select,button,[role="button"]')) return;
        lastEdit = Date.now();
        clearTimeout(editTimer);
        editTimer = setTimeout(() => send('editing'), Math.max(0, 2000 - (Date.now() - lastSent)));
    }
    ['input', 'change', 'pointerdown'].forEach(name => document.addEventListener(name, interact, { passive: true }));
    function leave() {
        clearTimeout(editTimer);
        if (!stopped) navigator.sendBeacon(endpoint, form('leave'));
    }
    document.addEventListener('visibilitychange', () => document.hidden ? leave() : send('viewing'));
    window.addEventListener('pagehide', leave);
    window.addEventListener('pageshow', () => { if (!document.hidden) send('viewing'); });
    setInterval(() => {
        if (!document.hidden) send(Date.now() - lastEdit < 12000 ? 'editing' : 'viewing');
    }, 10000);
    if (!document.hidden) send('viewing');
})();
