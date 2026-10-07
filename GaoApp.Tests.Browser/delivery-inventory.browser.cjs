const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let input = ''; process.stdin.on('data', chunk => input += chunk); process.stdin.on('end', () => resolve(input)); }));
    fs.mkdirSync(info.evidenceDirectory, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const contexts = [], errors = [], checks = [], commandIds = new Set(), responseTasks = [], requests = [];
    let csrfProof = null;
    const recordAck = ack => { assert.ok(ack.clientRequestId); commandIds.add(ack.clientRequestId); };
    async function accountPage(account, width = 1440) {
        const context = await browser.newContext({ viewport: { width, height: 1000 } }); contexts.push(context); const page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        page.on('request', request => { if (request.method() === 'POST' && /\/(picking|inventory)\//.test(request.url())) requests.push({ url: new URL(request.url()).pathname, body: request.postData() }); });
        page.on('response', response => {
            if (response.status() === 200 && /\/(picking\/report|inventory\/cancel)$/.test(new URL(response.url()).pathname)) {
                const task = response.json().then(recordAck); responseTasks.push(task); task.catch(() => {});
            }
        });
        await page.goto(info.baseUrl + '/admin/account/login'); await page.locator('[name=UserName]').fill(account.name); await page.locator('[name=Password]').fill(account.password);
        await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]); return page;
    }
    async function open(page, order) {
        await page.goto(info.baseUrl + '/admin/deliveries?key=' + encodeURIComponent(order.lookupToken) + '&revision=1');
        await page.locator('#deliveryInventory').waitFor(); await page.waitForFunction(() => document.querySelector('#deliveryPageMessage').textContent === '');
    }
    async function http(page, method, url, body, csrf = true) {
        return await page.evaluate(async ({ method, url, body, csrf }) => {
            const headers = { 'Content-Type': 'application/json' }; if (csrf) headers.RequestVerificationToken = document.querySelector('meta[name="request-verification-token"]')?.content || '';
            const requestFetch = csrf ? window.fetch : window.fetch.__gaoAppOriginalFetch;
            if (typeof requestFetch !== 'function') throw new Error('Unwrapped native fetch is unavailable for the missing-CSRF control.');
            const response = await requestFetch.call(window, url, { method, credentials: 'same-origin', cache: 'no-store', headers, body: body == null ? null : JSON.stringify(body) });
            const text = await response.text(); let data; try { data = JSON.parse(text); } catch { data = null; } return { status: response.status, data, text };
        }, { method, url, body, csrf });
    }
    async function picking(page, order) { const result = await http(page, 'GET', '/admin/api/deliveries/' + order.id + '/picking'); assert.equal(result.status, 200, result.text); return result.data; }
    async function inventory(page, order) { const result = await http(page, 'GET', '/admin/api/deliveries/' + order.id + '/inventory'); assert.equal(result.status, 200, result.text); return result.data; }
    async function saved(page, selector) { await page.locator(selector).click(); await page.waitForFunction(() => /Đã (lưu thao tác|xác nhận yêu cầu cũ)/.test(document.querySelector('#deliveryPageMessage').textContent)); }
    async function pending(page) { return await page.evaluate(() => Object.keys(localStorage).filter(key => key.startsWith('gao.delivery.inventory.v1:')).map(key => ({ key, intent: JSON.parse(localStorage.getItem(key)) }))); }
    async function quantity(page, field, expected) { assert.equal((await page.locator('#deliveryInventory [data-quantity="' + field + '"]').innerText()).split(' ')[0], expected); }
    try {
        const creator = await accountPage(info.creator), viewer = await accountPage(info.viewer), costViewer = await accountPage(info.costViewer), canceller = await accountPage(info.cancel);
        await open(creator, info.created); await quantity(creator, 'heldBaseQuantityText', '0');
        assert.equal(await creator.locator('#deliveryCancel').count(), 1);
        await open(viewer, info.posted);
        const redacted = await inventory(viewer, info.posted); assert.equal(redacted.canViewCosts, false); assert.equal(redacted.costFragments.length, 0);
        for (const line of redacted.lines) { assert.equal(line.dispatchedCostText, null); assert.equal(line.acceptedReturnedCostText, null); }
        assert.equal(redacted.sourceWarehouseId, info.sourceWarehouseId); assert.equal(redacted.sourceLegalEntityId, info.sourceLegalEntityId);
        assert.equal(redacted.sourceWarehouseName, info.sourceWarehouseName); assert.equal(redacted.sourceLegalEntityName, info.sourceLegalEntityName);
        await quantity(viewer, 'dispatchedBaseQuantityText', '8'); await quantity(viewer, 'acceptedReturnedBaseQuantityText', '2'); await quantity(viewer, 'stockOutsideBaseQuantityText', '6');
        assert.equal(redacted.inCustodyBaseQuantityText, null); assert.match(await viewer.locator('#deliveryInventory').innerText(), /Trách nhiệm giao: chưa có xác nhận/);
        assert.equal(await viewer.locator('#deliveryCancel').count(), 0);
        await open(costViewer, info.posted); const cost = await inventory(costViewer, info.posted); assert.equal(cost.canViewCosts, true);
        assert.equal(cost.lines[0].dispatchedCostText, info.expected.DispatchedCost); assert.equal(cost.lines[0].acceptedReturnedCostText, info.expected.AcceptedReturnedCost); assert.ok(cost.costFragments.length);
        const embedded = (await picking(viewer, info.posted)).inventory; assert.equal(embedded.canViewCosts, false); assert.equal(embedded.costFragments.length, 0);
        checks.push('1: authenticated genuine 8 dispatched / 2 accepted / 6 outside source, unknown custody and separately authorized cost redaction');

        await open(creator, info.held); const heldBefore = await picking(creator, info.held), heldLine = heldBefore.lines.find(line => line.isActive);
        await quantity(creator, 'heldBaseQuantityText', '8'); await creator.locator('#deliveryQty-' + heldLine.lineId).fill('7'); await creator.locator('#deliveryReason-' + heldLine.lineId).fill('Thiếu 1 khi kiểm tra lại');
        await creator.locator('.delivery-cancel-section summary').click(); await creator.locator('#deliveryCancelReason').fill('Khách muốn hủy');
        const beforeDirtyPosts = requests.length; await creator.locator('#deliveryCancel').click();
        assert.match(await creator.locator('#deliveryPageMessage').innerText(), /chỉnh sửa chưa lưu/); assert.equal(requests.length, beforeDirtyPosts); assert.equal((await pending(creator)).length, 0);
        assert.equal(await creator.locator('#deliveryQty-' + heldLine.lineId).inputValue(), '7'); assert.equal((await picking(creator, info.held)).delivery.version, heldBefore.delivery.version);
        await saved(creator, '#deliverySaveReport'); await quantity(creator, 'heldBaseQuantityText', '7');
        await creator.locator('#deliveryQty-' + heldLine.lineId).fill('8'); await creator.locator('#deliveryReason-' + heldLine.lineId).fill(''); await saved(creator, '#deliverySaveReport'); await quantity(creator, 'heldBaseQuantityText', '8');
        checks.push('2: dirty picking blocks cancellation before POST/intent; real saved report reconciles held quantity 8 to 7 to 8');

        await open(canceller, info.held); await canceller.locator('.delivery-cancel-section summary').click(); await canceller.locator('#deliveryCancelReason').fill('Khách hủy trước bàn giao');
        const cancelPath = '**/admin/api/deliveries/' + info.held.id + '/inventory/cancel', cancelBodies = [];
        await canceller.route(cancelPath, async route => { cancelBodies.push(route.request().postData()); const response = await route.fetch(); assert.equal(response.status(), 200); recordAck(await response.json()); await route.abort('failed'); });
        await canceller.locator('#deliveryCancel').click(); await canceller.waitForFunction(() => document.querySelector('#deliveryPageMessage').textContent.includes('Chưa xác nhận'));
        const uncertain = await pending(canceller); assert.equal(uncertain.length, 1); assert.equal(uncertain[0].intent.uncertain, true); assert.equal(uncertain[0].intent.storeId, info.storeId); assert.equal(String(uncertain[0].intent.actorId), String(info.cancel.userId));
        assert.equal((await picking(creator, info.held)).delivery.state, 'Cancelled'); assert.equal((await inventory(creator, info.held)).heldBaseQuantityText, '0');
        await canceller.unroute(cancelPath); const preReloadPosts = requests.length; await canceller.reload(); await canceller.locator('.delivery-pending').waitFor(); assert.equal(requests.length, preReloadPosts);
        await canceller.route(cancelPath, async route => { cancelBodies.push(route.request().postData()); await route.continue(); });
        await saved(canceller, '.delivery-pending button'); await canceller.unroute(cancelPath);
        assert.equal(cancelBodies.length, 2); assert.equal(cancelBodies[0], cancelBodies[1]); assert.equal((await pending(canceller)).length, 0);
        assert.equal(await canceller.locator('#deliveryDetail').getAttribute('data-state'), 'Cancelled'); await quantity(canceller, 'heldBaseQuantityText', '0');
        checks.push('3: separate authorized cancel actor, lost commit response, reload without autoPOST, exact GUID/body/version retry and current zero holds');

        await open(creator, info.created); const created = await picking(creator, info.created);
        const missingClientRequestId = crypto.randomUUID();
        const missingRequest = creator.waitForRequest(request => request.method() === 'POST' && new URL(request.url()).pathname === '/admin/api/deliveries/' + info.created.id + '/inventory/cancel' && JSON.parse(request.postData() || '{}').clientRequestId === missingClientRequestId);
        const missingToken = await http(creator, 'POST', '/admin/api/deliveries/' + info.created.id + '/inventory/cancel', { clientRequestId: missingClientRequestId, expectedVersion: created.delivery.version, reason: 'No CSRF' }, false);
        const missingHeaders = await (await missingRequest).allHeaders();
        csrfProof = { clientRequestId: missingClientRequestId, hasCsrfHeader: Boolean(missingHeaders.requestverificationtoken), status: missingToken.status, createdVersionUnchanged: false };
        assert.equal(csrfProof.hasCsrfHeader, false); assert.equal(missingToken.status, 400);
        assert.equal((await picking(creator, info.created)).delivery.version, created.delivery.version); csrfProof.createdVersionUnchanged = true;
        const forged = await http(creator, 'POST', '/admin/api/deliveries/' + info.created.id + '/inventory/cancel', { clientRequestId: crypto.randomUUID(), expectedVersion: created.delivery.version, reason: 'Forged source', sourceWarehouseId: info.sourceWarehouseId }); assert.equal(forged.status, 400);
        const denied = await http(viewer, 'POST', '/admin/api/deliveries/' + info.posted.id + '/inventory/cancel', { clientRequestId: crypto.randomUUID(), expectedVersion: redacted.version, reason: 'Viewer cannot cancel' }); assert.equal(denied.status, 403);
        for (const action of ['dispatch', 'receive']) { const result = await http(creator, 'POST', '/admin/api/deliveries/' + info.created.id + '/inventory/' + action, {}); assert.equal(result.status, 404); }
        assert.equal((await picking(creator, info.created)).delivery.version, created.delivery.version);
        checks.push('4: CSRF, strict cancel body and View-only permissions reject writes; raw dispatch/receive have no public routes');

        let releaseOld, oldCapturedResolve; const oldCaptured = new Promise(resolve => { oldCapturedResolve = resolve; }); let getCount = 0, allowFresh = false;
        const detailPath = '**/admin/api/deliveries/' + info.created.id + '/picking';
        await creator.route(detailPath, async route => {
            getCount++;
            if (getCount === 1) { const response = await route.fetch(); oldCapturedResolve(); await new Promise(resolve => { releaseOld = resolve; }); await route.fulfill({ response }); }
            else if (!allowFresh) await route.abort('failed');
            else await route.continue();
        });
        await creator.getByRole('button', { name: 'Tải bản mới', exact: true }).click(); await oldCaptured;
        await creator.locator('.delivery-cancel-section summary').click(); await creator.locator('#deliveryCancelReason').fill('Hủy Created trước nhận soạn'); await creator.locator('#deliveryCancel').click();
        await creator.waitForFunction(() => document.querySelector('#deliveryDetail').dataset.needsFresh === 'true');
        assert.match(await creator.locator('#deliveryPageMessage').innerText(), /đã xác nhận thao tác|Đã xác nhận thao tác/); assert.equal(await creator.locator('#deliveryCancel').isDisabled(), true);
        assert.equal((await pending(creator)).length, 0); const oldFinished = creator.waitForResponse(response => new URL(response.url()).pathname.endsWith('/' + info.created.id + '/picking') && response.status() === 200); releaseOld(); await oldFinished;
        // A subsequent event round trip observes the returned old request without using a timing delay as proof.
        await creator.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        assert.equal(await creator.locator('#deliveryDetail').getAttribute('data-needs-fresh'), 'true'); assert.equal(await creator.locator('#deliveryDetail').getAttribute('data-state'), 'Created'); assert.equal(await creator.locator('#deliveryCancel').isDisabled(), true);
        allowFresh = true; await creator.getByRole('button', { name: 'Tải bản mới', exact: true }).click(); await creator.waitForFunction(() => document.querySelector('#deliveryDetail').dataset.state === 'Cancelled' && document.querySelector('#deliveryDetail').dataset.needsFresh === 'false'); await creator.unroute(detailPath);
        await quantity(creator, 'heldBaseQuantityText', '0'); assert.equal(await creator.locator('#deliveryCancel').count(), 0);
        checks.push('5: confirmed cancel with fresh GET failure blocks new mutation; delayed precommand GET cannot restore stale capability; explicit fresh read shows Cancelled');

        await open(viewer, info.posted); await viewer.screenshot({ path: path.join(info.evidenceDirectory, 'inventory-desktop-1440.png'), fullPage: true });
        const mobile = await accountPage(info.viewer, 390); await open(mobile, info.posted); await quantity(mobile, 'stockOutsideBaseQuantityText', '6');
        assert.equal(await mobile.locator('#deliveryInventory img,#deliveryInventory script').count(), 0);
        assert.equal(await mobile.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
        await mobile.screenshot({ path: path.join(info.evidenceDirectory, 'inventory-mobile-390.png'), fullPage: true });
        const beforePrint = await inventory(creator, info.posted), bill = await creator.context().newPage(); await bill.goto(info.baseUrl + '/admin/deliveries/' + info.posted.id + '/bill');
        assert.equal(await bill.locator('.delivery-bill-footer img').count(), 1); await bill.pdf({ path: path.join(info.evidenceDirectory, 'inventory-approved-a5.pdf'), preferCSSPageSize: true, printBackground: true }); await bill.close();
        assert.equal((await inventory(creator, info.posted)).version, beforePrint.version);
        checks.push('6: desktop1440/mobile390 exact per-line facts without overflow, escaped DOM and existing approved A5 order QR remain read-only');
        await Promise.all(responseTasks); assert.deepEqual(errors, []); assert.equal(checks.length, 6); assert.equal(commandIds.size, 4);
        fs.writeFileSync(path.join(info.evidenceDirectory, 'browser-checks.json'), JSON.stringify({ checks, commandIds: [...commandIds], errors, csrfProof, stockExecutorSeed: 'internal ambient-transaction dispatch/physical receive; no courier/outcome seed', visualInspectionPending: true }, null, 2));
        fs.writeFileSync(path.join(info.evidenceDirectory, 'requests.json'), JSON.stringify(requests, null, 2));
        console.log('D05 INVENTORY BROWSER PASS 6 groups');
    } catch (error) {
        fs.writeFileSync(path.join(info.evidenceDirectory, 'browser-failure.json'), JSON.stringify({ checks, commandIds: [...commandIds], errors, csrfProof, message: error.message, stack: error.stack }, null, 2)); throw error;
    } finally { for (const context of contexts) await context.close(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
