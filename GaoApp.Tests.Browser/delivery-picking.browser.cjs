const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let input = ''; process.stdin.on('data', c => input += c); process.stdin.on('end', () => resolve(input)); }));
    const evidence = info.evidenceDirectory; fs.mkdirSync(evidence, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const errors = [], checks = [], commandIds = new Set(), recordings = [], mainId = info.main.id;
    const managerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const pickerContext = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const manager = await managerContext.newPage(), picker = await pickerContext.newPage();
    const recordResponse = async response => {
        if (!/\/picking\/(claim|report|submit|plan|approve|reopen|reassign)$/.test(new URL(response.url()).pathname) || response.status() !== 200) return;
        const ack = await response.json(); commandIds.add(ack.clientRequestId);
    };
    for (const page of [manager, picker]) {
        page.on('pageerror', e => errors.push(e.message));
        page.on('response', response => { const task = recordResponse(response); recordings.push(task); task.catch(() => {}); });
    }
    async function login(page, user, password) {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(user); await page.locator('[name=Password]').fill(password);
        await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
    }
    async function http(page, method, path, body) {
        return await page.evaluate(async ({ method, path, body }) => {
            const response = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store',
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('meta[name="request-verification-token"]')?.content || '' },
                body: body == null ? null : JSON.stringify(body) });
            const text = await response.text(); let data; try { data = JSON.parse(text); } catch { data = null; } return { status: response.status, text, data };
        }, { method, path, body });
    }
    async function projection(page, id = mainId) {
        const result = await http(page, 'GET', '/admin/api/deliveries/' + id + '/picking');
        assert.equal(result.status, 200, result.text); return result.data;
    }
    async function open(page, key, revision = null) {
        await page.goto(info.baseUrl + '/admin/deliveries?key=' + encodeURIComponent(key) + (revision ? '&revision=' + revision : ''));
        await page.locator('#deliveryDetail:not([hidden])').waitFor(); await page.waitForFunction(() => document.querySelector('#deliveryPageMessage').textContent === '');
    }
    async function clickCommand(page, selector) {
        await page.locator(selector).click();
        await page.waitForFunction(() => /Đã (lưu thao tác|xác nhận yêu cầu cũ)/.test(document.querySelector('#deliveryPageMessage').textContent));
    }
    async function postCommand(page, operation, fields = {}, id = mainId, expected = 200) {
        const current = await projection(page, id), body = { clientRequestId: crypto.randomUUID(), expectedVersion: current.delivery.version, ...fields };
        const result = await http(page, 'POST', '/admin/api/deliveries/' + id + '/picking/' + operation, body);
        assert.equal(result.status, expected, operation + ': ' + result.text); if (result.status === 200) commandIds.add(result.data.clientRequestId); return result;
    }
    async function refresh(page) { const latest = await projection(page); await page.getByRole('button', { name: 'Tải bản mới', exact: true }).click(); await page.waitForFunction(version => document.querySelector('#deliveryDetail').dataset.version === version, latest.delivery.version); }
    async function managerNotes(page, prefix, reason) {
        await page.locator('#' + prefix + 'Reason').fill(reason);
        await page.locator('#' + prefix + 'Confirmation').fill('Đã hỏi khách và khách đồng ý lượng này.');
    }
    try {
        await login(manager, info.user, info.password); await login(picker, info.pickerUser, info.pickerPassword);
        await open(manager, info.main.lookupToken, 1); await open(picker, info.main.code, 1);
        assert.equal(await manager.locator('#deliveryClaim').isVisible(), true);
        await open(picker, 'GH-D04-LEGACY'); assert.equal(await picker.locator('#deliveryClaim').count(), 0);
        const legacy = await projection(picker, info.legacyId); assert.equal(legacy.capabilities.canClaim, false);
        const rejectedLegacy = await postCommand(picker, 'claim', {}, info.legacyId, 409); assert.equal(rejectedLegacy.data.code, 'SOURCE_NOT_CONVERTED');
        await open(picker, info.main.code, 1); checks.push('1: authenticated QR/code lookup and legacy Draft cannot be claimed');

        const beforeClaim = await projection(picker);
        await clickCommand(manager, '#deliveryClaim');
        const race = await http(picker, 'POST', '/admin/api/deliveries/' + mainId + '/picking/claim', {
            clientRequestId: crypto.randomUUID(), expectedVersion: beforeClaim.delivery.version
        }); assert.equal(race.status, 409);
        let current = await projection(manager); assert.equal(current.pickerUserId, info.actorId);
        assert.ok(current.startedAtUtc); assert.equal(current.lines.every(x => x.reportedQuantityText === null), true);
        assert.equal((await postCommand(manager, 'submit', {}, mainId, 400)).data.code, 'PICKING_INCOMPLETE');
        checks.push('2: sole picker claim, competing claim denied, server time and all lines initially unreported');

        const original = current.lines.find(x => x.variantId === info.main.lines[0].variantId || x.lineId === info.main.lines[0].id);
        const missing = current.lines.find(x => x.lineId !== original.lineId);
        await manager.locator('#deliveryQty-' + original.lineId).fill('8');
        await manager.locator('#deliveryReason-' + original.lineId).fill('Thiếu 2 gói hàng gốc');
        await manager.locator('#deliveryQty-' + missing.lineId).fill('0');
        await manager.locator('#deliveryReason-' + missing.lineId).fill('Hết hàng này');
        const lostBodies = [];
        await manager.route('**/admin/api/deliveries/' + mainId + '/picking/report', async route => {
            lostBodies.push(route.request().postData()); const response = await route.fetch();
            assert.equal(response.status(), 200); const ack = await response.json(); commandIds.add(ack.clientRequestId);
            await route.abort('failed');
        });
        await manager.locator('#deliverySaveReport').click();
        await manager.waitForFunction(() => document.querySelector('#deliveryPageMessage').textContent.includes('Chưa xác nhận'));
        assert.equal(await manager.locator('#deliverySubmitPicking').isDisabled(), true);
        await manager.unroute('**/admin/api/deliveries/' + mainId + '/picking/report');
        // Another manager tab commits a newer assignment while the first tab has an uncertain report response.
        const sideContext = await browser.newContext(), side = await sideContext.newPage();
        await login(side, info.user, info.password); await open(side, info.main.lookupToken);
        await postCommand(side, 'reassign', { pickerUserId: info.pickerId, reason: 'Bàn giao việc soạn cho người thứ hai' });
        await sideContext.close();
        await manager.reload(); await manager.locator('#deliveryDetail:not([hidden])').waitFor();
        await manager.route('**/admin/api/deliveries/' + mainId + '/picking/report', async route => { lostBodies.push(route.request().postData()); await route.continue(); });
        await clickCommand(manager, 'button:has-text("Thử lại yêu cầu cũ")');
        await manager.unroute('**/admin/api/deliveries/' + mainId + '/picking/report');
        assert.equal(lostBodies.length, 2); assert.equal(lostBodies[0], lostBodies[1]);
        current = await projection(manager); assert.equal(current.pickerUserId, info.pickerId);
        assert.match(await manager.locator('#deliveryDetail').innerText(), /Đã soạn: 8/);
        assert.equal(await manager.locator('#deliverySaveReport').count(), 0);
        checks.push('7: commit response lost, reload exact GUID/body/version replay, latest GET retains newer reassignment');

        await open(picker, info.main.lookupToken, 1); await clickCommand(picker, '#deliverySubmitPicking');
        current = await projection(picker); assert.equal(current.delivery.state, 'AwaitingApproval');
        assert.equal(current.lines.find(x => x.lineId === missing.lineId).reportedQuantityText, '0');
        await refresh(manager);
        const awaitingBill = await managerContext.newPage(); await awaitingBill.goto(info.baseUrl + '/admin/deliveries/' + mainId + '/bill');
        assert.match(await awaitingBill.locator('.delivery-badge').innerText(), /CHỜ DUYỆT/);
        assert.equal(await awaitingBill.locator('.delivery-recipient img,.delivery-recipient script').count(), 0);
        await awaitingBill.pdf({ path: path.join(evidence, 'awaiting-approval-a5.pdf'), preferCSSPageSize: true, printBackground: true });
        await awaitingBill.close(); checks.push('4: partial and explicit zero shortages need reasons, incomplete submit rejected, awaiting approval is distinct');

        await manager.locator('#deliveryPlanSection summary').click();
        await manager.locator('#deliveryPlanQty-' + original.lineId).fill('8');
        await manager.locator('#deliveryPlanReason-' + original.lineId).fill('Khách đồng ý đổi phần thiếu');
        await manager.locator('#deliveryPlanQty-' + missing.lineId).fill('0');
        await manager.locator('#deliveryPlanReason-' + missing.lineId).fill('Bỏ hàng đã hết');
        await manager.locator('#deliveryReplacementSearch').fill('Gạo thay thế'); await manager.locator('#deliveryReplacementFind').click();
        await manager.locator('#deliveryReplacementOptions option').first().waitFor({ state: 'attached' });
        await manager.locator('#deliveryReplacementRoot').selectOption(String(original.lineId));
        await manager.locator('#deliveryReplacementQty').fill('2'); await manager.locator('#deliveryReplacementCoverage').fill('2');
        await manager.locator('#deliveryReplacementAdd').click(); await managerNotes(manager, 'deliveryPlan', 'Giảm hàng gốc và thêm hàng thay thế');
        await clickCommand(manager, '#deliveryPlanSave'); current = await projection(manager);
        const replacement = current.lines.find(x => x.isReplacement && x.isActive);
        assert.equal(replacement.variantId, info.replacementVariantId); assert.equal(replacement.reportedQuantityText, null);
        assert.equal(current.approvedTotalText, null); assert.equal(current.draftTotalText, '880');
        await postCommand(manager, 'approve', { lines: current.lines.filter(x => x.isActive).map(x => ({ lineId: x.lineId, allowedQuantityText: x.plannedQuantityText, confirmedOriginalCoverageText: x.isReplacement ? x.draftOriginalCoverageText : null })), reason: 'Thử chặn duyệt chưa soạn', customerConfirmationNote: 'Fixture' }, mainId, 409);
        await open(picker, info.main.code, 1);
        await picker.locator('#deliveryQty-' + replacement.lineId).fill('2'); await clickCommand(picker, '#deliverySaveReport');
        await clickCommand(picker, '#deliverySubmitPicking'); await refresh(manager);
        await manager.locator('#deliveryApprovalSection summary').click(); await managerNotes(manager, 'deliveryApprove', 'Duyệt đúng lượng người soạn báo');
        await clickCommand(manager, '#deliveryApprove'); current = await projection(manager);
        assert.equal(current.delivery.state, 'ReadyForHandover'); assert.equal(current.quotedTotalText, '830'); assert.equal(current.approvedTotalText, '880');
        assert.equal(current.lines.find(x => x.lineId === original.lineId).approvedNetText, '640');
        const approvedBill = await managerContext.newPage(); await approvedBill.goto(info.baseUrl + '/admin/deliveries/' + mainId + '/bill');
        assert.match(await approvedBill.locator('.delivery-bill-total').innerText(), /880/); assert.match(await approvedBill.locator('.delivery-bill-summary').innerText(), /830/);
        assert.match(await approvedBill.locator('.delivery-badge').innerText(), /ĐÃ CHỐT SOẠN/);
        await approvedBill.pdf({ path: path.join(evidence, 'approved-a5.pdf'), preferCSSPageSize: true, printBackground: true }); await approvedBill.close();
        checks.push('5: manager removes/reduces, fresh replacement stays unreported, actual picker reports then approval freezes 880 apart from source 830');

        await manager.locator('#deliveryAdministration summary').click(); await manager.locator('#deliveryAdminReason').fill('Kiểm tra lại trước giao');
        await clickCommand(manager, '#deliveryReopen'); current = await projection(manager); assert.equal(current.approvedTotalText, null);
        assert.equal(current.lines.filter(x => x.isActive).every(x => x.reportedQuantityText !== null), true);
        await manager.locator('#deliveryAdministration summary').click(); await manager.locator('#deliveryAdminReason').fill('Quản lý nhận lại việc kiểm tra');
        await manager.locator('#deliveryPickerSelect').selectOption(String(info.actorId)); await clickCommand(manager, '#deliveryReassign');
        current = await projection(manager); assert.equal(current.pickerUserId, info.actorId);
        assert.equal(current.lines.find(x => x.lineId === replacement.lineId).reporterUserId, info.pickerId);
        await clickCommand(manager, '#deliverySubmitPicking'); assert.equal((await projection(manager)).delivery.state, 'AwaitingApproval');
        await manager.locator('#deliveryApprovalSection summary').click(); await managerNotes(manager, 'deliveryApprove', 'Duyệt lại sau mở soạn');
        await clickCommand(manager, '#deliveryApprove'); await manager.locator('.delivery-history summary').click();
        await manager.waitForFunction(() => document.querySelector('.delivery-history').textContent.includes('reassign'));
        checks.push('6: reopen invalidates approval, reassignment retains original reporters and time, renewed approval and history required');

        await open(manager, info.exact.lookupToken, 1); await clickCommand(manager, '#deliveryClaim');
        const exact = await projection(manager, info.exact.id), exactLine = exact.lines[0], exactBodies = [];
        assert.match(await manager.locator('#deliveryDetail').innerText(), /99\.999\.999\.999\.999,9999/);
        await manager.locator('#deliveryQty-' + exactLine.lineId).fill('99999999999999.9999');
        await manager.route('**/admin/api/deliveries/' + info.exact.id + '/picking/report', async route => { exactBodies.push(route.request().postDataJSON()); await route.continue(); });
        await clickCommand(manager, '#deliverySaveReport'); await manager.unroute('**/admin/api/deliveries/' + info.exact.id + '/picking/report');
        assert.equal(exactBodies[0].lines[0].pickedQuantityText, '99999999999999.9999');
        assert.equal((await projection(manager, info.exact.id)).lines[0].reportedQuantityText, '99999999999999.9999');
        // Hold a real precommand GET response, commit another report and render its newer GET, then deliver the old bytes.
        let releaseOld, capturedOld; const oldRelease = new Promise(resolve => { releaseOld = resolve; });
        const oldCaptured = new Promise(resolve => { capturedOld = resolve; }); let oldHeld = false, oldVersion;
        await manager.route('**/admin/api/deliveries/' + info.exact.id + '/picking', async route => {
            if (oldHeld) { await route.continue(); return; }
            oldHeld = true; const response = await route.fetch(); assert.equal(response.status(), 200);
            oldVersion = (await response.json()).delivery.version; capturedOld(); await oldRelease; await route.fulfill({ response });
        });
        await manager.getByRole('button', { name: 'Tải bản mới', exact: true }).click(); await oldCaptured;
        await manager.locator('#deliveryQty-' + exactLine.lineId).fill('99999999999999.9999');
        await clickCommand(manager, '#deliverySaveReport'); const newExact = await projection(manager, info.exact.id);
        assert.notEqual(newExact.delivery.version, oldVersion);
        const oldReturned = manager.waitForResponse(async response => response.url().endsWith('/' + info.exact.id + '/picking') && response.status() === 200 && (await response.json()).delivery.version === oldVersion);
        releaseOld(); const oldResponse = await oldReturned; await oldResponse.finished(); await manager.waitForTimeout(150);
        assert.equal(await manager.locator('#deliveryDetail').getAttribute('data-version'), newExact.delivery.version);
        assert.equal(await manager.locator('#deliveryDetail').getAttribute('data-needs-fresh'), 'false');
        await manager.unroute('**/admin/api/deliveries/' + info.exact.id + '/picking');
        // A confirmed command followed by a failed fresh GET cannot reuse old capabilities or report an uncertain effect.
        await manager.route('**/admin/api/deliveries/' + info.exact.id + '/picking', route => route.abort('failed'));
        await manager.locator('#deliveryQty-' + exactLine.lineId).fill('99999999999999.9999');
        await manager.locator('#deliverySaveReport').click();
        await manager.waitForFunction(() => document.querySelector('#deliveryPageMessage').textContent.includes('Đã xác nhận thao tác nhưng chưa tải'));
        assert.equal(await manager.locator('#deliverySaveReport').isDisabled(), true);
        assert.equal(await manager.locator('#deliverySubmitPicking').isDisabled(), true);
        assert.equal(await manager.locator('button:has-text("Thử lại yêu cầu cũ")').count(), 0);
        assert.equal(await manager.locator('#deliveryDetail').getAttribute('data-needs-fresh'), 'true');
        await manager.unroute('**/admin/api/deliveries/' + info.exact.id + '/picking');
        const latestExact = await projection(manager, info.exact.id);
        await manager.getByRole('button', { name: 'Tải bản mới', exact: true }).click();
        await manager.waitForFunction(version => document.querySelector('#deliveryDetail').dataset.version === version && document.querySelector('#deliveryDetail').dataset.needsFresh === 'false', latestExact.delivery.version);
        assert.equal(await manager.locator('#deliverySaveReport').isDisabled(), false);
        // Unsaved shortage edits must never submit the previously saved full report or create a durable intent.
        const beforeDirty = await projection(manager, info.exact.id), dirtySubmitRequests = [];
        const observeDirtySubmit = request => { if (request.method() === 'POST' && request.url().endsWith('/' + info.exact.id + '/picking/submit')) dirtySubmitRequests.push(request.postData()); };
        manager.on('request', observeDirtySubmit);
        const unsavedQuantity = '99999999999998.9999', unsavedReason = 'Báo thiếu một phần chưa lưu';
        await manager.locator('#deliveryQty-' + exactLine.lineId).fill(unsavedQuantity);
        await manager.locator('#deliveryReason-' + exactLine.lineId).fill(unsavedReason);
        await manager.locator('#deliverySubmitPicking').click();
        await manager.waitForFunction(() => document.querySelector('#deliveryPageMessage').textContent.includes('Có chỉnh sửa chưa lưu'));
        assert.equal(dirtySubmitRequests.length, 0);
        assert.equal(await manager.evaluate(() => Object.keys(localStorage).some(key => key.startsWith('gao.delivery.picking.v1:'))), false);
        const afterDirty = await projection(manager, info.exact.id);
        assert.equal(afterDirty.delivery.version, beforeDirty.delivery.version);
        assert.equal(afterDirty.delivery.revision, beforeDirty.delivery.revision);
        assert.equal(afterDirty.delivery.state, beforeDirty.delivery.state);
        assert.equal(afterDirty.lines[0].reportedQuantityText, '99999999999999.9999');
        assert.equal(await manager.locator('#deliveryQty-' + exactLine.lineId).inputValue(), unsavedQuantity);
        assert.equal(await manager.locator('#deliveryReason-' + exactLine.lineId).inputValue(), unsavedReason);
        await clickCommand(manager, '#deliverySaveReport');
        const savedShortage = await projection(manager, info.exact.id);
        assert.equal(savedShortage.lines[0].reportedQuantityText, unsavedQuantity);
        assert.equal(savedShortage.lines[0].shortageReason, unsavedReason);
        await clickCommand(manager, '#deliverySubmitPicking');
        const submittedShortage = await projection(manager, info.exact.id);
        assert.equal(dirtySubmitRequests.length, 1);
        assert.equal(submittedShortage.delivery.state, 'AwaitingApproval');
        assert.equal(submittedShortage.lines[0].reportedQuantityText, unsavedQuantity);
        manager.off('request', observeDirtySubmit);
        checks.push('3: exact large qty4, delayed GET and confirmed-effect guards; unsaved edits cannot submit until actual Save');

        await open(manager, info.main.lookupToken, 1);
        assert.equal(await manager.locator('.delivery-revision-warning').count(), 1);
        await manager.locator('#deliveryKey').fill(info.main.code); await manager.locator('#deliverySearch button').click();
        await manager.waitForFunction(() => document.querySelector('#deliveryPageMessage').textContent === '');
        assert.equal(new URL(manager.url()).searchParams.get('revision'), '1'); await manager.reload();
        await manager.locator('.delivery-revision-warning').waitFor();
        checks.push('8: stale printed revision warning persists manual lookup, cleaned URL and reload; A5 distinguishes draft and approved values');

        await manager.screenshot({ path: path.join(evidence, 'picking-desktop.png'), fullPage: true });
        await manager.setViewportSize({ width: 390, height: 844 });
        await manager.screenshot({ path: path.join(evidence, 'picking-mobile.png'), fullPage: true });
        assert.equal(await manager.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
        assert.equal(await manager.locator('#deliveryDetail script,#deliveryDetail img[onerror]').count(), 0);
        await manager.locator('#deliveryKey').focus(); await manager.keyboard.press('Enter');
        await manager.waitForFunction(() => document.querySelector('#deliveryPageMessage').textContent === '');
        assert.equal(await manager.locator('.delivery-revision-warning').count(), 1);
        checks.push('9: 1440/390 layout, keyboard lookup, escaped names/recipient, no document overflow');

        await open(manager, info.handed.lookupToken, 1); const handed = await projection(manager, info.handed.id);
        assert.equal(Object.values(handed.capabilities).every(x => x === false), true);
        assert.equal(await manager.locator('#deliveryClaim,#deliverySaveReport,#deliveryPlanSave,#deliveryApprove,#deliveryReopen,#deliveryReassign').count(), 0);
        const before = await http(manager, 'GET', '/admin/api/deliveries/' + info.handed.id + '/history');
        await postCommand(manager, 'reopen', { reason: 'Không được mở sau bàn giao' }, info.handed.id, 409);
        const after = await http(manager, 'GET', '/admin/api/deliveries/' + info.handed.id + '/history'); assert.deepEqual(after.data, before.data);
        checks.push('10: post-handover controls absent, server mutation denied without new history; C# verifies exact receipts/outbox and no stock/money');
        await Promise.all(recordings); assert.deepEqual(errors, []); assert.equal(checks.length, 10);
        fs.writeFileSync(path.join(evidence, 'browser-checks.json'), JSON.stringify({ checks, errors, commandIds: [...commandIds], exactLostRequest: lostBodies[0], fixtureOnly: true }, null, 2));
        console.log('D04 BROWSER PASS: ' + checks.join('; '));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

