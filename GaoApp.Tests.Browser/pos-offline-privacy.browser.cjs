'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', x => text += x); process.stdin.on('end', () => resolve(text)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 950 } });
    const checks = [], errors = [];
    const page = await context.newPage();
    context.on('page', p => p.on('pageerror', error => errors.push(error.message)));
    page.on('pageerror', error => errors.push(error.message));
    const check = name => {
        checks.push(name); console.log('PASS:', name);
        fs.writeFileSync(path.join(info.output, 'checks.json'), JSON.stringify({ checks, errors }, null, 2));
    };
    async function login(target) {
        await target.goto(info.baseUrl + '/admin/account/login');
        await target.locator('[name=UserName]').fill(info.user);
        await target.locator('[name=Password]').fill(info.password);
        if (await target.locator('select[name=SelectedTerminalId]').count()) await target.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([target.waitForURL(url => !url.pathname.endsWith('/login')), target.locator('#loginForm button[type=submit]').click()]);
        await target.goto(info.baseUrl + '/admin/pos');
        await target.waitForFunction(() => window.PosOffline?.canWork(), null, { timeout: 60000 });
        await target.waitForFunction(() => PosOffline.status().shellReady, null, { timeout: 60000 });
    }
    async function api(target, url, method = 'GET', body) {
        return target.evaluate(async ({ url, method, body }) => {
            const response = await fetch(url, { method, headers: { 'Content-Type': 'application/json',
                RequestVerificationToken: window.PosCommon?.getAntiForgeryToken?.() || '' },
                body: body == null ? undefined : JSON.stringify(body) });
            const data = await response.json(); if (!response.ok) throw new Error(response.status + ': ' + JSON.stringify(data));
            return data;
        }, { url, method, body });
    }
    async function snapshot(target) {
        return target.evaluate(async () => {
            const db = await new Promise((resolve, reject) => {
                const request = indexedDB.open('gao-pos-offline-v1', 1); request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
            });
            try {
                const values = await Promise.all(['sessions', 'catalogs', 'meta'].map(name => new Promise((resolve, reject) => {
                    const request = db.transaction(name).objectStore(name).getAll(); request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
                })));
                return { sessions: values[0], catalogs: values[1], meta: values[2] };
            } finally { db.close(); }
        });
    }
    try {
        await login(page);
        let saved = await snapshot(page);
        const key = saved.sessions[0].key;
        assert.deepEqual(saved.catalogs[0].customers.map(x => x.name), ['Attached privacy customer']);
        assert.equal(saved.catalogs[0].customers[0].address, undefined);
        assert.equal(saved.sessions[0].context.antiForgeryToken, undefined);
        assert.equal((await api(page, '/admin/pos/customers/search?keyword=0900000002')).length, 1);
        check('catalog limited to attached customers; addresses and CSRF token absent; online search unchanged');

        await context.setOffline(true);
        assert.equal((await api(page, '/admin/pos/customers/search?keyword=0900000002')).length, 0);
        assert.equal((await api(page, '/admin/pos/customers/search?keyword=0900000001')).length, 1);
        const draft = (await api(page, '/admin/pos/screen')).currentDraft;
        await api(page, `/admin/pos/${draft.orderId}/items?variantId=${info.variantId}&qty=2`, 'POST', {});
        // Keep the existing automatic receipt owner; this probe has no printer.
        await page.evaluate(() => { window.open = () => null; });
        const paid = await api(page, '/admin/pos/cart/current/payment-and-finalize', 'POST', {
            orderId: draft.orderId, clientRequestId: require('node:crypto').randomUUID(), method: 0, amount: 40 });
        assert.equal(paid.finalized, true);
        await page.waitForFunction(id => [1, 2].includes(PosOffline.invoiceIntentStatus(id)?.route), draft.orderId);
        await api(page, '/admin/pos/screen'); // Prepare the next empty cart through the normal POS owner.
        saved = await snapshot(page); let original = saved.sessions.find(x => x.key === key).queue;
        assert.ok(original.length >= 3);
        await page.reload();
        await page.waitForFunction(() => window.PosOffline?.canWork(), null, { timeout: 45000 });
        const afterReload = (await snapshot(page)).sessions.find(x => x.key === key).queue;
        assert.deepEqual(afterReload.slice(0, original.length), original);
        assert.ok(afterReload.slice(original.length).every(op => op.url === '/admin/pos/cart/ensure'));
        original = afterReload;
        check('offline search is limited; cash sale and invoice choice survive an offline reload');

        await page.evaluate(async key => {
            const db = await PosOfflinePrivacy.open();
            const tx = db.transaction(['sessions', 'catalogs'], 'readwrite');
            const sessions = tx.objectStore('sessions'), catalogs = tx.objectStore('catalogs');
            const request = sessions.get(key);
            request.onsuccess = () => {
                const previous = structuredClone(request.result);
                previous.key = key + '-expired-clean'; previous.context.expiresAtUtc = '2000-01-01T00:00:00';
                previous.queue = []; previous.quarantined = []; previous.orders = {}; previous.conflict = null;
                sessions.put(previous, previous.key); catalogs.put({ customers: [{ name: 'old pii' }] }, previous.key);
                const review = structuredClone(previous); review.key = key + '-review';
                review.quarantined = [{ id: 'DO-NOT-DELETE-REVIEW', url: '/admin/pos/123/payments', body: { amount: 5 } }];
                sessions.put(review, review.key); catalogs.put({ products: [], customers: [{ customerId: 123, name: 'unrelated old pii' }] }, review.key);
                catalogs.put({ customers: [{ name: 'orphan pii' }] }, 'orphan');
            };
            await new Promise((resolve, reject) => { tx.oncomplete = resolve; tx.onabort = () => reject(tx.error); });
            await PosOfflinePrivacy.sweep(db); await PosOfflinePrivacy.pruneShells(db); db.close();
        }, key);
        saved = await snapshot(page);
        assert.equal(saved.sessions.length, 2); assert.equal(saved.catalogs.length, 2);
        assert.equal(saved.sessions.find(x => x.key.endsWith('-review')).privacyLocked, true);
        assert.equal(saved.sessions.find(x => x.key.endsWith('-review')).quarantined[0].id, 'DO-NOT-DELETE-REVIEW');
        check('expired clean caches and orphan catalogs removed; quarantine-only journal retained and locked');

        await page.evaluate(async key => {
            const db = await PosOfflinePrivacy.open(), tx = db.transaction(['sessions', 'meta'], 'readwrite');
            const request = tx.objectStore('sessions').get(key);
            request.onsuccess = () => {
                const state = request.result; state.context.expiresAtUtc = '2000-01-01T00:00:00';
                tx.objectStore('sessions').put(state, key);
                tx.objectStore('meta').put({ key, expiresAt: state.context.expiresAtUtc, privacyVersion: 1, revision: state.privacyRevision }, 'active');
            };
            await new Promise((resolve, reject) => { tx.oncomplete = resolve; tx.onabort = () => reject(tx.error); }); db.close();
        }, key);
        const expired = await page.reload(); assert.equal(expired.status(), 503);
        saved = await snapshot(page);
        assert.deepEqual(saved.sessions.find(x => x.key === key).queue, original);
        assert.equal(saved.sessions.find(x => x.key === key).privacyLocked, true);
        check('expired offline shell denied while the original pending payment commands remain intact');

        await context.setOffline(false);
        let release; const gate = new Promise(resolve => { release = resolve; }); const replayed = [];
        await page.route('**/admin/pos/**', async route => {
            const id = route.request().headers()['x-pos-operation-id'];
            if (id) { replayed.push(id); await gate; }
            await route.continue();
        });
        await login(page);
        // Replay durably fills serverRequest before its first HTTP call; business commands keep their original identity and payload.
        const commands = values => values.map(({ serverRequest, ...operation }) => operation);
        assert.deepEqual(commands((await snapshot(page)).sessions.find(x => x.key === key).queue), commands(original));
        release(); await page.waitForFunction(() => PosOffline.status().pending === 0, null, { timeout: 60000 });
        await page.unroute('**/admin/pos/**');
        assert.deepEqual([...new Set(replayed)], original.map(x => x.id));
        check('verified reauthentication restores the original journal and replays the same operation IDs once');

        await context.setOffline(true);
        await api(page, '/admin/pos/cart/current/new', 'POST', {});
        await api(page, '/admin/pos/cart/current/note', 'POST', { note: 'Preserve this logout draft' });
        const beforeLogout = (await snapshot(page)).sessions.find(x => x.key === key).queue;
        const second = await context.newPage(); await second.goto(info.baseUrl + '/admin/pos');
        await second.waitForFunction(() => window.PosOffline?.status());
        await page.route('**/admin/pos/**', route => route.abort('internetdisconnected'));
        await context.setOffline(false);
        const accountPage = await context.newPage(); await accountPage.goto(info.baseUrl + '/admin');
        await accountPage.locator('form[action="/admin/account/logout"]').evaluate(form => form.requestSubmit());
        await accountPage.waitForURL(url => url.pathname.endsWith('/login'));
        for (const target of [page, second]) {
            await target.waitForFunction(() => PosOffline.status().locked);
            assert.equal(await target.locator('dialog[aria-label="Phiên POS đã khóa"]').isVisible(), true);
            assert.equal(await target.evaluate(async () => (await fetch('/admin/pos/screen')).status), 401);
        }
        saved = await snapshot(accountPage);
        assert.deepEqual(saved.sessions.find(x => x.key === key).queue, beforeLogout);
        assert.equal(saved.sessions.find(x => x.key === key).privacyLocked, true);
        await page.screenshot({ path: path.join(info.output, 'locked-pos.png') });
        check('logout from another admin page locks both POS tabs and preserves unsynced work');
        await page.close(); await second.close();
        await login(accountPage);
        await accountPage.waitForFunction(() => PosOffline.status().pending === 0, null, { timeout: 60000 });
        await accountPage.locator('form[action="/admin/account/logout"]').evaluate(form => form.requestSubmit());
        await accountPage.waitForURL(url => url.pathname.endsWith('/login'));
        saved = await snapshot(accountPage);
        assert.equal(saved.sessions.some(x => x.key === key), false);
        assert.equal(saved.catalogs.length, 1);
        assert.equal(saved.sessions[0].quarantined[0].id, 'DO-NOT-DELETE-REVIEW');
        check('after replay, clean logout deletes the synchronized session and retains only unresolved review data');
        assert.equal(errors.length, 0, JSON.stringify(errors));
        fs.writeFileSync(path.join(info.output, 'checks.json'), JSON.stringify({ checks, errors }, null, 2));
    } catch (error) {
        console.error(error);
        for (const target of context.pages()) console.error('PAGE', target.url(), await target.evaluate(() => {
            const status = window.PosOffline?.status(); if (status?.context) delete status.context.antiForgeryToken; return status;
        }).catch(() => null));
        fs.writeFileSync(path.join(info.output, 'checks.json'), JSON.stringify({ checks, errors, failure: String(error) }, null, 2));
        process.exitCode = 1;
    } finally { await browser.close(); }
})();
