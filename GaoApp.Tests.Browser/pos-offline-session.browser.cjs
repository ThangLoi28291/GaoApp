'use strict';
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');
const core = require('../GaoApp.Web/wwwroot/Admin/js/pos/pos.offline.core.js');
const scripts = path.resolve(__dirname, '../GaoApp.Web/wwwroot/Admin/js/pos');

(async () => {
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const noShift = { errorCode: 'POS_SHIFT_NOT_OPEN', message: 'Chưa có ca POS đang mở.', actionHint: 'Mở ca trực tuyến trước khi bán hàng.' };
    try {
        for (const scenario of ['no-cache', 'cached', 'legacy-rejection', 'pending', 'shift-closes', 'real-disconnection']) {
            const context = await browser.newContext();
            const page = await context.newPage();
            page.setDefaultTimeout(5000);
            const errors = [], mutations = [];
            page.on('pageerror', error => errors.push(error.message));
            const boot = { storeId: 1, terminalId: 2, userId: 3, shiftId: 4,
                antiForgeryToken: 'test-token', expiresAtUtc: new Date(Date.now() + 3600000).toISOString(),
                permissions: ['pos.order.create', 'pos.order.finalize', 'pos.order.hold', 'pos.order.discount', 'pos.payment.create'],
                screen: { currentDraft: null, draftOrders: [], heldOrders: [] } };
            const catalog = { fetchedAtUtc: new Date().toISOString(), products: [{ id: 10, productId: 11, productName: 'Sữa',
                productVariantName: 'Sữa tươi', sku: 'MILK', price: 10000, onHandQty: 12,
                units: [{ id: 12, unitId: 13, unitName: 'Hộp', factor: 1, price: 10000, isBaseUnit: true, isDefaultForSale: true, barcodes: ['123456'] }] }],
                customers: [], promotions: [] };
            const stored = core.initial(boot);
            core.apply(stored, catalog, { url: '/admin/pos/cart/ensure', method: 'POST', body: {} });
            boot.screen.currentDraft = structuredClone(stored.orders[stored.currentId]);
            if (scenario === 'pending') stored.queue.push({ id: 'original-pending-operation', url: '/admin/pos/cart/current/scan',
                method: 'POST', body: { barcode: '123456' }, offline: true });
            let server = scenario === 'shift-closes' ? 200 : scenario === 'real-disconnection' ? 0 : 409;
            let responseContext = boot;
            const respond = (route, value, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
            await page.route('http://localhost/**', async route => {
                const request = route.request(), url = new URL(request.url());
                if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: '<div id="posShell" class="pos-shell" data-store-id="1" data-terminal-id="2" data-user-id="3"></div>' });
                if (url.pathname === '/pos-offline-worker.js') return route.fulfill({ status: 404, body: '' });
                if (/\/offline\/(bootstrap|status)$/.test(url.pathname)) {
                    if (!server) return route.abort('connectionrefused');
                    return respond(route, server === 200 ? responseContext : scenario === 'legacy-rejection'
                        ? { message: 'Cần mở ca trực tuyến trước khi chuẩn bị POS offline.' } : noShift, server);
                }
                if (request.method() !== 'GET') { mutations.push(url.pathname); return respond(route, {}, 409); }
                if (url.pathname.endsWith('/screen')) return respond(route, { source: 'server', currentDraft: boot.screen.currentDraft, draftOrders: [], heldOrders: [] });
                return respond(route, {}, 404);
            });
            await page.goto('http://localhost/');
            if (scenario !== 'no-cache') await page.evaluate(async ({ stored, catalog }) => {
                const db = await new Promise((resolve, reject) => {
                    const request = indexedDB.open('gao-pos-offline-v1', 1);
                    request.onupgradeneeded = () => ['sessions','catalogs','meta'].forEach(name => request.result.createObjectStore(name));
                    request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
                });
                await new Promise((resolve, reject) => {
                    const tx = db.transaction(['sessions','catalogs','meta'], 'readwrite');
                    tx.objectStore('sessions').put(stored, stored.key); tx.objectStore('catalogs').put(catalog, stored.key);
                    tx.objectStore('meta').put({ key: stored.key, expiresAt: stored.context.expiresAtUtc }, 'active');
                    tx.oncomplete = resolve; tx.onerror = () => reject(tx.error);
                }); db.close();
            }, { stored, catalog });
            for (const file of ['pos.offline.core.js', 'pos.offline.js', 'pos.common.js', 'pos.render.js'])
                await page.addScriptTag({ path: path.join(scripts, file) });
            await page.evaluate(() => PosOffline.init());
            const readStatus = () => page.evaluate(() => ({ ...PosOffline.status(), canWork: PosOffline.canWork(), localMode: PosOffline.localMode() }));
            const sync = async () => {
                // startTransport launches its first sync without blocking initialization.
                await page.waitForTimeout(50);
                await page.evaluate(() => PosOffline.sync(true));
            };
            const snapshot = () => page.evaluate(async key => {
                const db = await new Promise(resolve => { const r = indexedDB.open('gao-pos-offline-v1',1); r.onsuccess=()=>resolve(r.result); });
                return new Promise(resolve => { const r=db.transaction('sessions').objectStore('sessions').get(key); r.onsuccess=()=>{db.close();resolve(r.result);}; });
            }, stored.key);
            if (scenario === 'real-disconnection') {
                const offline = await readStatus();
                assert.equal(offline.connected, false); assert.equal(offline.canWork, true);
                await page.evaluate(async () => {
                    const r=await fetch('/admin/pos/cart/current/scan', { method:'POST',body:JSON.stringify({barcode:'123456'}) });
                    if(!r.ok) throw new Error(await r.text());
                });
                assert.equal((await readStatus()).pending,1);
                server=409; await sync();
            }
            if (scenario === 'shift-closes') {
                await sync();
                assert.equal((await readStatus()).canWork,true);
                server=409; await sync();
            }
            const blocked = await readStatus();
            assert.equal(blocked.connected,true,scenario);
            assert.equal(blocked.offline,false,scenario);
            assert.equal(blocked.canWork,false,scenario);
            assert.equal(blocked.localMode,false,scenario);
            assert.ok(blocked.sessionIssue);
            await page.evaluate(() => PosRender.renderNetworkBanner({ offline:{ isOnline:true } }));
            const banner=await page.locator('#posNetworkStateBanner').innerText();
            assert.doesNotMatch(banner,/Mất kết nối|đang bán offline/);
            assert.match(banner,/ca|phiên POS/);
            if(scenario !== 'no-cache') {
                const before=await snapshot();
                const rejected=await page.evaluate(async()=> {
                    const r=await fetch('/admin/pos/cart/current/scan',{method:'POST',body:JSON.stringify({barcode:'123456'})});
                    return {status:r.status,body:await r.json()};
                });
                assert.equal(rejected.status,409);
                assert.deepEqual(await snapshot(),before,'Rejected session must not edit saved orders or pending commands');
                assert.deepEqual(mutations,[],'No mutation is sent or replayed for a closed shift');
                const screen=await page.evaluate(async()=>await(await fetch('/admin/pos/screen')).json());
                assert.equal(screen.source,'server','Show authoritative online screen instead of old offline data');
                assert.deepEqual(await snapshot(),before,'Online reads must not overwrite pending local quantities');
                // A new shift cannot receive commands belonging to the previous shift.
                if (before.queue.length) {
                    server=200; responseContext={...boot,shiftId:5}; await sync();
                    assert.equal((await readStatus()).sessionIssue.code,'POS_SESSION_CHANGED');
                    assert.deepEqual((await snapshot()).queue,before.queue); assert.deepEqual(mutations,[]);
                    server=0; await sync();
                    assert.equal((await readStatus()).canWork,false,'Network loss cannot re-enable a known invalid shift');
                }
            }
            if(scenario === 'shift-closes') {
                server=200; await sync();
                const recovered=await readStatus();
                assert.equal(recovered.sessionIssue,null); assert.equal(recovered.canWork,true);
            }
            assert.deepEqual(errors,[]);
            console.log('PASS',scenario);
            await context.close();
        }
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode=1; });
