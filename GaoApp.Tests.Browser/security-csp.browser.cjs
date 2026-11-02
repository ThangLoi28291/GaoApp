'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => resolve(s)); }));
    fs.mkdirSync(info.output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const results = [], errors = [], requests = [], origin = new URL(info.baseUrl).origin;
    await context.route('**/*', route => {
        const url = new URL(route.request().url());
        if (['http:', 'https:'].includes(url.protocol) && url.origin !== origin) { requests.push(url.origin); return route.abort(); }
        return route.continue();
    });
    await context.addInitScript(() => {
        window.cspViolations = [];
        window.print = () => { window.cspPrints = (window.cspPrints || 0) + 1; };
        document.addEventListener('securitypolicyviolation', e => window.cspViolations.push({ directive: e.effectiveDirective, blocked: e.blockedURI, disposition: e.disposition }));
    });
    const page = await context.newPage();
    page.on('pageerror', e => errors.push({ page: page.url(), message: e.message }));
    async function open(route, check) {
        const response = await page.goto(info.baseUrl + route, { waitUntil: 'load' });
        // Some operational screens poll continuously; networkidle is not a page readiness signal.
        await page.waitForTimeout(700);
        assert.equal(response.status(), 200, route + ' HTTP');
        assert.equal(new URL(page.url()).pathname.toLowerCase().replace(/\/$/, ''), route.toLowerCase().replace(/\/$/, ''), route + ' redirect');
        const policy = response.headers()[info.reportOnly ? 'content-security-policy-report-only' : 'content-security-policy'];
        assert.match(policy, /script-src 'nonce-[^']+'/);
        assert.ok(!policy.includes('unsafe-eval'));
        const nonce = policy.match(/script-src 'nonce-([^']+)'/)[1];
        const missing = await page.locator('script').evaluateAll((elements, expected) => elements.filter(s => ['','text/javascript','application/javascript','module'].includes(s.type) && s.nonce !== expected).map(s => s.src || s.id || 'inline'), nonce);
        assert.deepEqual(missing, [], route + ': every intended script must have the response nonce');
        if (check) await check();
        const violations = await page.evaluate(() => window.cspViolations);
        results.push({ route, status: response.status(), scripts: await page.locator('script').count(), violations });
        assert.deepEqual(violations, [], route + ': legitimate CSP violations');
        assert.deepEqual(errors, [], 'browser JavaScript errors');
    }
    try {
        await open('/admin/account/login', async () => {
            assert.equal(await page.evaluate(() => $.fn.jquery), '3.7.1');
            assert.equal(await page.evaluate(() => typeof $.validator), 'function');
        });
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('select[name=SelectedTerminalId]').count()) await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
        await open('/admin/pos', async () => {
            assert.equal(await page.evaluate(() => typeof bootstrap.Modal), 'function');
            assert.equal(await page.evaluate(() => typeof toastr.success), 'function');
            assert.equal(await page.evaluate(() => typeof PosOffline), 'object');
            const registered = await page.evaluate(async () => { const registration = await navigator.serviceWorker.register('/pos-offline-worker.js'); await navigator.serviceWorker.ready; return !!registration.active; });
            assert.equal(registered, true);
        });
        await open('/admin/pos/customer-display', async () => {
            const connected = await page.evaluate(async ({ storeId, terminalId }) => {
                const connection = new signalR.HubConnectionBuilder().withUrl('/hubs/pos').build();
                await connection.start(); await connection.invoke('JoinStoreGroup', storeId, String(terminalId));
                const state = connection.state; await connection.stop(); return state;
            }, info);
            assert.equal(connected, 'Connected');
        });
        await open('/admin/displaypromotion', async () => {
            await page.locator('[data-csp-action=promotion-create]').first().click();
            await page.locator('#promotionModal').waitFor({ state: 'visible' });
            await page.locator('#promotionModal .modal-footer [data-bs-dismiss=modal]').click();
        });
        await open('/admin/media/test', async () => {
            assert.equal(await page.evaluate(() => typeof FilePond.create), 'function');
        });
        await open('/admin/product/create', async () => {
            assert.equal(await page.evaluate(() => typeof $.fn.select2), 'function');
            assert.equal(await page.evaluate(() => typeof FilePond.create), 'function');
            const decoder = await page.evaluate(() => new Promise((resolve, reject) => {
                const script = document.createElement('script');
                script.src = '/lib/zxing-browser/0.1.5/zxing-browser.min.js';
                script.nonce = document.querySelector('script[nonce]').nonce;
                script.onload = () => resolve(typeof ZXingBrowser.BrowserMultiFormatReader);
                script.onerror = () => reject(new Error('Local barcode decoder blocked'));
                document.head.append(script);
            }));
            assert.equal(decoder, 'function');
        });
        for (const route of ['/admin/stock-counts', '/admin/inventory-adjustment-documents/create', '/admin/warehouse-receiving', '/admin/reports/overview', '/admin/label-printing']) await open(route);
        await open('/admin/pos/receipt/' + info.receiptId, async () => {
            await page.waitForFunction(() => document.getElementById('receiptPaper')?.contentDocument?.body?.textContent?.length > 0);
            assert.equal(await page.evaluate(() => typeof PosPrinting.sendReceipt), 'function');
        });
        await open('/admin/store-monitor', async () => {
            await page.waitForFunction(() => !!window.GaoStoreLiveMonitor?.scene);
            assert.equal(await page.evaluate(() => GaoStoreLiveMonitor.view), '3d');
            await page.locator('#wm-view').click();
            assert.equal(await page.evaluate(() => GaoStoreLiveMonitor.view), '2d');
            await page.locator('#wm-view').click();
            await page.waitForFunction(() => GaoStoreLiveMonitor.view === '3d' && !!GaoStoreLiveMonitor.scene);
        });
        assert.deepEqual(requests, [], 'production resources must work with all external HTTP origins unavailable');

        await page.evaluate(() => {
            window.cspInlineRan = 0; window.cspHandlerRan = 0;
            const script = document.createElement('script'); script.textContent = 'window.cspInlineRan=1'; document.body.append(script);
            const button = document.createElement('button'); button.setAttribute('onclick', 'window.cspHandlerRan=1'); document.body.append(button); button.click();
            const external = document.createElement('script'); external.src = 'https://cdn-csp-probe.invalid/untrusted.js'; document.body.append(external);
            const trusted = document.createElement('script'); trusted.nonce = document.querySelector('script[nonce]').nonce;
            // Run in a normal browser task: DevTools' synchronous evaluation context
            // explicitly permits eval and is not representative of page execution.
            trusted.textContent = "setTimeout(function(){try { eval('window.cspEvalRan=1'); } catch { window.cspEvalBlocked=true; }}, 0)"; document.body.append(trusted);
        });
        await page.waitForTimeout(150);
        const attack = await page.evaluate(() => ({ inline: cspInlineRan, handler: cspHandlerRan, evalBlocked: window.cspEvalBlocked === true, evalRan: window.cspEvalRan || 0, violations: cspViolations }));
        assert.equal(attack.inline, info.reportOnly ? 1 : 0); assert.equal(attack.handler, info.reportOnly ? 1 : 0);
        assert.ok(attack.violations.some(v => v.directive === 'script-src-elem'));
        assert.ok(attack.violations.some(v => v.directive === 'script-src-attr'));
        assert.ok(attack.violations.every(v => v.disposition === (info.reportOnly ? 'report' : 'enforce')));
        assert.equal(attack.evalRan, info.reportOnly ? 1 : 0); assert.equal(attack.evalBlocked, !info.reportOnly);
        if (!info.reportOnly) assert.deepEqual(requests, [], 'untrusted CDN script must be blocked before any network request');
        await page.screenshot({ path: path.join(info.output, 'last-page.png'), fullPage: true });
        fs.writeFileSync(path.join(info.output, 'result.json'), JSON.stringify({ mode: info.reportOnly ? 'report-only' : 'enforced', routes: results, attack, externalResourceRequestsBeforeAttack: [], errors }, null, 2));
        console.log('CSP PASS: ' + results.length + ' actual pages, local assets, real SignalR and worker, ' + (info.reportOnly ? 'report' : 'blocking') + ' verified.');
    } catch (error) {
        fs.writeFileSync(path.join(info.output, 'failure.json'), JSON.stringify({ message: error.message, results, errors, requests, url: page.url(), violations: await page.evaluate(() => window.cspViolations).catch(() => []) }, null, 2));
        await page.screenshot({ path: path.join(info.output, 'failure.png'), fullPage: true }).catch(() => {});
        throw error;
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
