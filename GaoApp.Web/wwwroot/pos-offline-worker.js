/* Cache only a prepared POS shell and public assets. API responses remain private in IndexedDB. */
'use strict';
const ASSETS = 'gao-pos-assets-v1', SHELLS = 'gao-pos-shells-v1';
const POS_PAGES = new Set(['/admin/pos', '/admin/pos/', '/admin/pos/v3', '/admin/pos/legacy']);
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
function meta(mode, work) {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open('gao-pos-offline-v1', 1);
        request.onupgradeneeded = () => { for (const name of ['sessions', 'catalogs', 'meta']) request.result.createObjectStore(name); };
        request.onerror = () => reject(request.error);
        request.onsuccess = () => {
            const db = request.result, tx = db.transaction('meta', mode), result = work(tx.objectStore('meta'));
            tx.oncomplete = () => { db.close(); resolve(result.result); };
            tx.onerror = () => { db.close(); reject(tx.error); };
        };
    });
}
async function prepare(data) {
    const page = new URL(data.page);
    if (page.origin !== self.location.origin || !POS_PAGES.has(page.pathname) || typeof data.key !== 'string') return;
    const response = await fetch(page.href, { credentials: 'same-origin', cache: 'no-store' });
    if (!response.ok || response.redirected || !response.headers.get('content-type')?.includes('text/html')) return;
    const cache = await caches.open(ASSETS);
    const urls = data.assets.filter(value => {
        const url = new URL(value);
        return url.origin === self.location.origin && /\.(js|css)$/i.test(url.pathname);
    });
    for (const url of urls) {
        const asset = await fetch(url, { credentials: 'same-origin' });
        if (!asset.ok) throw new Error('POS asset preparation incomplete');
        await cache.put(url, asset);
        if (new URL(url).pathname.endsWith('.css')) {
            const css = await (await cache.match(url)).text();
            for (const match of css.matchAll(/url\(["']?([^)'"\s]+)["']?\)/g)) {
                const font = new URL(match[1], url);
                if (font.origin === self.location.origin && /\.(woff2?|ttf|svg)$/i.test(font.pathname)) {
                    const value = await fetch(font.href).catch(() => null);
                    if (value?.ok) await cache.put(font.href, value);
                }
            }
        }
    }
    await (await caches.open(SHELLS)).put('/__pos-shell__/' + encodeURIComponent(data.key), response);
    for (const client of await self.clients.matchAll()) client.postMessage({ type: 'pos-offline-prepared', key: data.key });
}
self.addEventListener('message', event => {
    if (event.data?.type === 'prepare') event.waitUntil(prepare(event.data));
});
async function pageResponse(request) {
    const controller = new AbortController(), timer = setTimeout(() => controller.abort(), 4000);
    try {
        const response = await fetch(request, { signal: controller.signal });
        if (response.status < 500) return response;
    } catch { /* Use only a previously prepared, unexpired terminal. */ }
    finally { clearTimeout(timer); }
    const active = await meta('readonly', store => store.get('active'));
    if (active && new Date(active.expiresAt).getTime() > Date.now()) {
        const response = await (await caches.open(SHELLS)).match('/__pos-shell__/' + encodeURIComponent(active.key));
        if (response) return response;
    }
    return new Response('<!doctype html><meta charset="utf-8"><title>POS chưa sẵn sàng</title><p>Chưa có phiên POS offline còn hiệu lực. Kết nối máy chủ và mở POS để chuẩn bị quầy. Dữ liệu chưa đồng bộ vẫn được giữ trên máy.</p>',
        { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8' } });
}
self.addEventListener('fetch', event => {
    const request = event.request, url = new URL(request.url);
    if (url.origin !== self.location.origin) return;
    if (/\/(?:account|auth)\/logout$/i.test(url.pathname)) {
        event.respondWith((async () => {
            await meta('readwrite', store => store.delete('active'));
            await caches.delete(SHELLS);
            return fetch(request);
        })());
        return;
    }
    if (request.method !== 'GET') return;
    if (request.mode === 'navigate' && POS_PAGES.has(url.pathname)) { event.respondWith(pageResponse(request)); return; }
    if (/\.(js|css|woff2?|ttf|svg)$/i.test(url.pathname) && !url.pathname.startsWith('/uploads/')) {
        event.respondWith((async () => {
            const cache = await caches.open(ASSETS);
            // Cache.match returns a Promise; wait for a hit before choosing the network fallback.
            const cached = await cache.match(request);
            return cached || fetch(request);
        })());
    }
});
