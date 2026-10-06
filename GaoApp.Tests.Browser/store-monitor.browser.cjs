const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

(async () => {
    const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', chunk => text += chunk); process.stdin.on('end', () => resolve(text)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const evidence = info.evidenceDirectory || path.join(process.cwd(), 'TestResults', 'store-monitor');
    fs.mkdirSync(evidence, { recursive: true });
    const errors = [], checks = [];
    async function newPage(width = 1440, height = 1000) {
        const context = await browser.newContext({ viewport: { width, height } });
        const page = await context.newPage(); page.on('pageerror', error => errors.push(error.message)); return { context, page };
    }
    async function login(page, user, password, terminalId = info.terminalId) {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(user); await page.locator('[name=Password]').fill(password);
        await page.locator('[name=SelectedTerminalId]').selectOption(String(terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
    }
    async function post(page, url, body) {
        const response = await page.evaluate(async ({ url, body }) => {
            const token = document.querySelector('meta[name="request-verification-token"]').content;
            const result = await fetch(url, { method: 'POST', headers: { RequestVerificationToken: token, 'Content-Type': 'application/json' }, body: body == null ? null : JSON.stringify(body) });
            return { status: result.status, body: await result.text() };
        }, { url, body });
        assert.equal(response.status, 200, `${url}: ${response.body}`); return JSON.parse(response.body);
    }
    try {
        const { page: monitor, context: monitorContext } = await newPage(1920, 1080);
        await login(monitor, info.monitorUser, info.monitorPassword);
        await monitor.evaluate(() => localStorage.setItem('gao.monitor.theme', 'dark'));
        await monitor.goto(info.baseUrl + '/admin/store-monitor');
        await monitor.waitForFunction(() => document.querySelector('#wm-connection')?.textContent.includes('ĐANG NHẬN'));
        assert.equal(await monitor.locator('#gao-store-wall').getAttribute('data-theme'), 'light');
        assert.equal(await monitor.locator('#wm-delivery [data-active=false]').count(), 1);
        const { page: receiving } = await newPage();
        await login(receiving, info.user, info.password); await receiving.goto(info.baseUrl + '/admin/warehouse-receiving');
        await monitor.waitForSelector('#wm-receipt .wm-live-person');
        await receiving.locator('#wrSearch').fill('Phiếu nhập đang làm');
        await monitor.waitForSelector('#wm-receipt[data-state=working]');
        await monitor.waitForSelector('#wm-receipt .wm-scene[data-active=true]');
        checks.push('Real receiving presence and animation');
        const counters = [];
        for (const [i, terminalId] of info.terminalIds.entries()) {
            const { page } = await newPage(); await login(page, info.user, info.password, terminalId);
            await post(page, '/admin/pos/shift/open', { openingCash: 0, warehouseId: info.warehouseId });
            const draft = await post(page, '/admin/pos/draft', null); const orderId = draft.orderId;
            await post(page, `/admin/pos/${orderId}/items?variantId=${info.variantId}&qty=${i + 1}`, null);
            await page.goto(info.baseUrl + '/admin/pos');
            counters.push({ page, orderId });
        }
        await monitor.waitForFunction(() => document.querySelectorAll('#wm-pos .wm-live-person').length === 3);
        const actualStations = await monitor.locator('#wm-pos .wm-station').evaluateAll(items => items.map(item => ({ terminal: item.dataset.terminal, text: item.textContent })));
        assert.equal(new Set(actualStations.map(item => item.terminal)).size, 3);
        for (let i = 0; i < 3; i++) {
            assert.match(actualStations[i].text, new RegExp('#' + counters[i].orderId));
            assert.match(actualStations[i].text, new RegExp('Số lượng ' + (i + 1)));
            assert.match(actualStations[i].text, /\d{2}:\d{2}:\d{2}/);
        }
        await post(counters[1].page, `/admin/pos/${counters[1].orderId}/payments`, { clientRequestId: require('node:crypto').randomUUID(), amount: 40, method: 0 });
        await post(counters[1].page, `/admin/pos/${counters[1].orderId}/finalize`, null);
        await monitor.waitForFunction(() => document.querySelector('#wm-pos [data-terminal="E2E-02"]')?.textContent.includes('Chốt đơn bán hàng'));
        await post(counters[2].page, `/admin/pos/orders/${counters[2].orderId}/hold`, { holdNote: 'PRIVATE TEST NOTE' });
        await monitor.waitForFunction(() => document.querySelector('#wm-pos [data-terminal="E2E-03"]')?.textContent.includes('Giữ đơn chờ xử lý'));
        await post(counters[0].page, `/admin/pos/${counters[0].orderId}/items?variantId=${info.variantId}&qty=2`, null);
        await monitor.waitForFunction(() => document.querySelector('#wm-pos [data-terminal="E2E-01"]')?.textContent.includes('Số lượng 2'));
        await counters[1].page.goto(info.baseUrl + '/admin/home');
        await monitor.waitForFunction(() => !document.querySelector('#wm-pos [data-terminal="E2E-02"]')?.classList.contains('wm-live-person'));
        assert.match(await monitor.locator('#wm-pos [data-terminal="E2E-02"]').innerText(), /Chốt đơn bán hàng/);
        assert.deepEqual(await monitor.locator('#wm-pos .wm-station').evaluateAll(items => items.map(item => item.dataset.terminal)), ['E2E-01', 'E2E-02', 'E2E-03']);
        assert.equal(await monitor.locator('#wm-pos [data-terminal="E2E-02"] .wm-scene').getAttribute('data-mode'), 'saved');
        assert.equal(await monitor.locator('#wm-pos').innerText().then(text => text.includes('PRIVATE TEST NOTE')), false);
        await monitor.screenshot({ path: path.join(evidence, 'office-live-three-counters.png'), fullPage: true });
        const age = monitor.locator('#wm-pos [data-terminal="E2E-01"] .wm-age'), previousAge = await age.innerText();
        await monitor.waitForFunction(old => document.querySelector('#wm-pos [data-terminal="E2E-01"] .wm-age')?.textContent !== old, previousAge);
        checks.push('Real SQL: same employee on 3 counters; distinct add/finalize/hold details, precise timestamps, ticking ages; saved animation after leaving');
        await monitorContext.setOffline(true);
        await monitor.waitForFunction(() => /MẤT KẾT NỐI|GIÁN ĐOẠN/.test(document.querySelector('#wm-connection')?.textContent));
        assert.equal(await monitor.locator('#gao-store-wall').getAttribute('data-paused'), 'true');
        await monitorContext.setOffline(false);
        await monitor.waitForFunction(() => document.querySelector('#wm-connection')?.textContent.includes('ĐANG NHẬN'), null, { timeout: 30000 });
        checks.push('Real stream offline pause and reconnect');

        const receiptPages = [receiving], receiptIds = [];
        for (let i = 1; i < 3; i++) {
            const { page } = await newPage(); await login(page, info.staffAccounts[i].user, info.staffAccounts[i].password); receiptPages.push(page);
        }
        for (const [i, page] of receiptPages.entries()) {
            const receipt = await post(page, '/admin/warehouse-receiving/receipts', { legalEntityId: info.legalEntityId, warehouseId: info.warehouseId, directReceiptReason: 'Khác' });
            receiptIds.push(receipt.id);
            await post(page, `/admin/api/stock-documents/${receipt.id}/lines`, { productVariantId: info.variantId, quantity: i + 3, unitCost: 10 });
            await page.goto(info.baseUrl + '/admin/warehouse-receiving/' + receipt.id);
        }
        const { page: secondReceipt } = await newPage(); await login(secondReceipt, info.user, info.password);
        const extra = await post(secondReceipt, '/admin/warehouse-receiving/receipts', { legalEntityId: info.legalEntityId, warehouseId: info.warehouseId, directReceiptReason: 'Khác' });
        receiptIds.push(extra.id); await secondReceipt.goto(info.baseUrl + '/admin/warehouse-receiving/' + extra.id);
        await monitor.waitForFunction(() => document.querySelectorAll('#wm-receipt .wm-live-person').length === 4);
        for (const id of receiptIds) {
            const card = monitor.locator(`#wm-receipt .wm-live-person[data-work-key="receipt:${id}"]`);
            assert.equal(await card.count(), 1); assert.match(await card.innerText(), new RegExp('Phiếu nhập #' + id));
        }
        for (let i = 0; i < 3; i++) {
            const saved = await receiptPages[i].evaluate(async id => (await fetch('/admin/api/stock-documents/' + id)).json(), receiptIds[i]);
            const item = saved.lines[0];
            const card = monitor.locator(`#wm-receipt .wm-live-person[data-work-key="receipt:${receiptIds[i]}"]`);
            await card.getByText(item.productNameSnapshot, { exact: false }).first().waitFor();
            assert.match(await card.innerText(), new RegExp('Số lượng ' + (i + 3)));
            assert.match(await card.innerText(), /Thêm .* vào phiếu nhập/);
        }
        checks.push('Real persisted receipt product names and quantities arrive in each independent work card');
        assert.equal(new Set(await monitor.locator('#wm-receipt .wm-live-person .wm-person strong').allTextContents()).size, 3);
        await monitor.screenshot({ path: path.join(evidence, 'office-live-multiple-receipts.png'), fullPage: true });
        await receiptPages[0].goto(info.baseUrl + '/admin/home');
        await monitor.waitForFunction(() => document.querySelectorAll('#wm-receipt .wm-live-person').length === 3);
        assert.equal(await monitor.locator(`#wm-receipt .wm-live-person[data-work-key="receipt:${extra.id}"]`).count(), 1);
        checks.push('Real Web/SQL: 3 employees on 4 independent receipt documents, one employee on 2 documents; leaving one keeps the other');
        const labelPages = [];
        for (const taskId of info.labelTaskIds) {
            const { page } = await newPage(); await login(page, info.user, info.password); await page.goto(info.baseUrl + '/admin/label-printing?task=' + taskId); labelPages.push(page);
            await monitor.waitForSelector(`#wm-label .wm-live-person[data-work-key="label:${taskId}"]`);
        }
        assert.equal(await monitor.locator('#wm-label .wm-live-person[data-work-key^="label:"]').count(), 2);
        await labelPages[0].locator('#closeTask').click();
        await monitor.waitForFunction(id => !document.querySelector(`#wm-label .wm-live-person[data-work-key="label:${id}"]`), info.labelTaskIds[0]);
        assert.equal(await monitor.locator(`#wm-label .wm-live-person[data-work-key="label:${info.labelTaskIds[1]}"]`).count(), 1);
        checks.push('Real label UI: signed context arrives through XHR; two tasks of one employee stay separate; closing one resets only its tab');

        // The following is explicitly a UI fixture; no synthetic events enter the application registry or database.
        const { page: layout } = await newPage(1920, 1080);
        await login(layout, info.monitorUser, info.monitorPassword);
        const utc = Date.now(), at = offset => new Date(utc + offset).toISOString();
        const definitions = [
            ['pos', 'Lan Anh', 'Quầy 01', 'vừa quét mã hàng vào giỏ', '#182041', 'Gạo ST25 · Số lượng 2 · Tổng 370.000 ₫'],
            ['pos', 'Minh Đức', 'Quầy 02', 'vừa ghi nhận thanh toán', '#182039', '3 mặt hàng · Tổng 620.000 ₫ · Ghi nhận 620.000 ₫'],
            ['pos', 'Thu Hà', 'Quầy 03', 'vừa giữ đơn chờ xử lý', '#182038', '4 mặt hàng · Tổng 495.000 ₫'],
            ['receipt', 'Hoàng Nam', 'Kho nhận hàng', 'vừa thêm Gạo ST25 vào phiếu nhập', '#6792', 'Số lượng 24 bao · Kho chính · NH-6792'],
            ['warehouse', 'Tuấn Anh', 'Kho 01', 'vừa đếm Dầu ăn Simply', 'Kiểm kê #903', 'Đã đếm 130 chai → 128 chai · Chênh lệch -2 ĐV gốc'],
            ['label', 'Mai Hương', 'Bàn in tem', 'vừa gửi in 72 tem · Gạo ST25, Dầu ăn', 'Lệnh in #248', '3 mặt hàng · Gạo ST25: 48 tem, Dầu ăn: 24 tem, …'],
            ['review', 'Quản lý', 'Văn phòng', 'vừa duyệt phiếu nhập NH-6790', '#6790', '3 mặt hàng: Gạo ST25, Đường Biên Hòa, … · Kho chính'],
            ['receipt', 'Bảo Ngọc', 'Kho nhận hàng', 'vừa quét nhập Nước mắm Nam Ngư', '#6793', 'Số lượng 8 thùng · Kho chính · NH-6793'],
            ['receipt', 'Hoàng Nam', 'Kho nhận hàng', 'vừa sửa dòng nhập Đường Biên Hòa', '#6794', 'Số lượng 12 bao → 16 bao · NH-6794'],
            ['warehouse', 'Tuấn Anh', 'Kho 01', 'vừa thêm Gạo ST25 vào phiếu chuyển kho', 'Chuyển kho #903', 'Số lượng 20 bao · Kho chính → Kho quầy'],
            ['warehouse', 'Văn Hải', 'Kho 01', 'vừa đếm Sữa Vinamilk', 'Kiểm kê #904', 'Đã đếm 64 hộp · Chênh lệch 0 ĐV gốc'],
            ['label', 'Thảo Vy', 'Bàn in tem', 'vừa lập kế hoạch in tem', 'Phiếu tem #249', '2 mặt hàng · Sữa Vinamilk: 24 tem, Đường: 12 tem · Kế hoạch 36 tem'],
            ['label', 'Mai Hương', 'Bàn in tem', 'vừa xác nhận in 90/96 tem · Dầu ăn', 'Phiếu tem #250', '4 mặt hàng · Dầu ăn: 90 tem, Đường: 0 tem, …'],
            ['review', 'Phương Linh', 'Văn phòng', 'vừa gửi chờ duyệt phiếu nhập NH-6791', '#6791', '2 mặt hàng: Nước mắm Nam Ngư, Đường · Kho chính'],
            ['review', 'Quản lý', 'Văn phòng', 'vừa lưu giá nháp phiếu nhập NH-6789', '#6789', '2 mặt hàng: Gạo ST25, Dầu ăn · Kho chính']
        ];
        const actorIds = new Map([...new Set(definitions.map(row => row[1]))].map((person, i) => [person, i + 1]));
        const work = ([module, , , , document]) => `${module === 'pos' ? 'order' : module === 'warehouse' ? document.startsWith('Chuyển') ? 'transfer' : 'count' : module === 'label' ? 'label' : 'receipt'}:${document.match(/\d+/)[0]}`;
        const fixture = {
            serverTimeUtc: at(0), startedAtUtc: at(-3600000),
            people: definitions.map(([module, person, terminal, , document], i) => ({ userId: actorIds.get(person), module, person, terminal, workKey: work(definitions[i]), document, state: 'editing', seenAtUtc: at(0), openedAtUtc: at(-180000 - i * 25000), editedAtUtc: at(1) })),
            events: definitions.map(([module, person, terminal, text, document, detail], i) => ({ id: 100 - i, userId: actorIds.get(person), module, person, terminal, text, document, detail, workKey: work(definitions[i]), occurredAtUtc: at(-i * 300), action: 'UI fixture' }))
        };
        await layout.addInitScript(payload => {
            window.EventSource = class extends EventTarget {
                constructor() { super(); window.probeSource = this; this.timer = setTimeout(() => this.dispatchEvent(new MessageEvent('snapshot', { data: JSON.stringify({ ...payload, serverTimeUtc: new Date().toISOString() }) })), 20); this.heartbeat = setInterval(() => this.dispatchEvent(new MessageEvent('heartbeat', { data: JSON.stringify({ serverTimeUtc: new Date().toISOString() }) })), 5000); }
                close() { clearTimeout(this.timer); clearInterval(this.heartbeat); }
            };
        }, fixture);
        await layout.goto(info.baseUrl + '/admin/store-monitor');
        await layout.waitForSelector('#wm-review[data-state=working]');
        await layout.locator('.wm-title-row .wm-overline').evaluate(node => node.textContent = 'KIỂM TRA BỐ CỤC · DỮ LIỆU MÔ PHỎNG');
        const animations = await layout.evaluate(() => Object.fromEntries(['pos', 'receipt', 'warehouse', 'label', 'review', 'delivery'].map(key => [key, document.querySelector('#wm-' + key).getAnimations({ subtree: true }).map(a => a.animationName)])));
        for (const [key, name] of Object.entries({ pos: 'wm-scan', receipt: 'wm-pen', warehouse: 'wm-package', label: 'wm-print', review: 'wm-stamp' })) assert.ok(animations[key].includes(name), `${key} approved animation missing`);
        assert.deepEqual(animations.delivery, []);
        assert.equal(await layout.locator('#wm-pos .wm-station').count(), 3);
        for (const key of ['receipt', 'warehouse', 'label', 'review']) {
            assert.equal(await layout.locator(`#wm-${key} .wm-station`).count(), 3);
            assert.equal(await layout.locator(`#wm-${key} .wm-stations`).evaluate(node => node.scrollHeight > node.clientHeight), false, `${key} must not hide active work in an internal scroll`);
            assert.equal(await layout.locator(`#wm-${key} .wm-scene[data-active=true]`).count(), 3);
        }
        assert.equal(await layout.locator('#wm-receipt .wm-station[data-work-key="receipt:6792"]').count(), 1);
        assert.equal(await layout.locator('#wm-receipt .wm-station[data-work-key="receipt:6794"]').count(), 1);
        assert.equal(await layout.locator('#wm-warehouse .wm-station[data-work-key="count:903"]').count(), 1);
        assert.equal(await layout.locator('#wm-warehouse .wm-station[data-work-key="transfer:903"]').count(), 1);
        const clippedArt = await layout.locator('.wm-station .wm-art').evaluateAll(items => items.filter(art => {
            const sprite = art.getBoundingClientRect(), stage = art.parentElement.getBoundingClientRect();
            return sprite.left < stage.left - 1 || sprite.right > stage.right + 1 || sprite.top < stage.top - 1 || sprite.bottom > stage.bottom + 1;
        }).length);
        assert.equal(clippedArt, 0, 'Approved art must stay centered and fully inside all 15 work cards');
        await layout.screenshot({ path: path.join(evidence, 'office-light.png'), fullPage: true });
        const officeMetrics = await layout.evaluate(() => ({ height: document.documentElement.scrollHeight, viewport: innerHeight, areas: [...document.querySelectorAll('.wm-area')].map(node => ({ id: node.id, top: node.offsetTop, height: node.offsetHeight })) }));
        fs.writeFileSync(path.join(evidence, 'office-metrics.json'), JSON.stringify(officeMetrics, null, 2));
        assert.equal(officeMetrics.height <= officeMetrics.viewport, true, `All areas must fit the office 1920x1080 viewport: ${officeMetrics.height}`);
        assert.match(await layout.locator('#wm-clock').innerText(), /\d{2}:\d{2}:\d{2}/);
        await layout.locator('#wm-theme').click(); assert.equal(await layout.locator('#gao-store-wall').getAttribute('data-theme'), 'dark');
        await layout.screenshot({ path: path.join(evidence, 'office-dark.png'), fullPage: true });
        await layout.locator('#wm-theme').click();
        for (const width of [1366, 1024, 704, 390]) {
            await layout.setViewportSize({ width, height: 900 });
            assert.equal(await layout.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `Horizontal overflow at ${width}`);
            if (width === 1366 || width === 390) await layout.screenshot({ path: path.join(evidence, width === 390 ? 'mobile.png' : 'office-1366.png'), fullPage: true });
        }
        await layout.setViewportSize({ width: 1920, height: 1080 });
        await layout.locator('#wm-motion').click(); assert.equal(await layout.locator('#gao-store-wall').getAttribute('data-motion'), 'false');
        assert.equal(await layout.evaluate(() => document.querySelector('.wm-operations').getAnimations({ subtree: true }).length), 0);
        await layout.locator('#wm-motion').click(); await layout.emulateMedia({ reducedMotion: 'reduce' });
        await layout.waitForFunction(() => document.querySelector('#gao-store-wall')?.dataset.paused === 'true');
        assert.equal(await layout.evaluate(() => document.querySelector('.wm-operations').getAnimations({ subtree: true }).length), 0);
        checks.push('Separate UI fixture: 15 independent work cards, 3 per module, same person on 2 documents and count/transfer with same numeric ID; no internal clipping; 5 animations, delivery static, themes, responsive widths, reduced motion');
        await layout.emulateMedia({ reducedMotion: 'no-preference' });
        await layout.waitForFunction(() => document.querySelector('#gao-store-wall').dataset.paused === 'false' && document.querySelector('#wm-pos .wm-visual').getAnimations({ subtree: true }).length > 0);
        await layout.evaluate(payload => {
            window.probeScene = document.querySelector('#wm-pos .wm-visual');
            window.probeAnimation = window.probeScene.getAnimations({ subtree: true })[0];
            window.probeSource.dispatchEvent(new MessageEvent('snapshot', { data: JSON.stringify({ ...payload, serverTimeUtc: new Date().toISOString() }) }));
        }, fixture);
        assert.equal(await layout.evaluate(() => window.probeScene === document.querySelector('#wm-pos .wm-visual') && window.probeAnimation === window.probeScene.getAnimations({ subtree: true })[0]), true, 'Snapshots must preserve active animation nodes');
        const escaped = structuredClone(fixture); escaped.events[0].detail = '<img src=x onerror="window.probeXss=true">';
        escaped.events.find(event => event.module === 'receipt').text = 'vừa nhập <img src=x onerror="window.probeXss=true">';
        await layout.evaluate(payload => window.probeSource.dispatchEvent(new MessageEvent('snapshot', { data: JSON.stringify({ ...payload, serverTimeUtc: new Date().toISOString() }) })), escaped);
        assert.equal(await layout.locator('#wm-pos img').count(), 0); assert.equal(await layout.evaluate(() => window.probeXss), undefined);
        assert.match(await layout.locator('#wm-pos .wm-task-detail').first().innerText(), /<img/);
        assert.equal(await layout.locator('#wm-receipt img').count(), 0);
        assert.match(await layout.locator('#wm-receipt .wm-task-title').first().innerText(), /<img/);

        const motionSamples = {};
        for (const [key, selector] of Object.entries({ pos: '.wm-scanner-beam', receipt: '.wm-pen-art', warehouse: '.wm-package-art', label: '.wm-label-art', review: '.wm-review-stamp' })) {
            const moving = layout.locator('#wm-' + key + ' .wm-scene[data-mode=working] ' + selector).first();
            const before = await moving.evaluate(node => { const style = getComputedStyle(node); return [style.transform, style.opacity, style.left].join('|'); });
            await layout.waitForTimeout(1400);
            const after = await moving.evaluate(node => { const style = getComputedStyle(node); return [style.transform, style.opacity, style.left].join('|'); });
            assert.notEqual(after, before, key + ' must visibly move, rather than only declare an animation');
            motionSamples[key] = { before, after };
        }
        // Test save/edit ordering on one receipt while another document of the same employee keeps moving.
        const savedAt = Date.now(), transition = structuredClone(fixture);
        const target = transition.people.find(person => person.workKey === 'receipt:6792');
        target.editedAtUtc = new Date(savedAt - 1000).toISOString();
        const savedEvent = { ...transition.events.find(event => event.workKey === target.workKey), id: 101, occurredAtUtc: new Date(savedAt).toISOString(), text: 'vừa lưu số lượng phiếu nhập' };
        transition.events.unshift(savedEvent);
        transition.serverTimeUtc = new Date(savedAt).toISOString();
        await layout.evaluate(() => {
            window.neighborArt = document.querySelector('#wm-receipt [data-work-key="receipt:6794"] .wm-art');
            window.neighborAnimation = window.neighborArt.querySelector('.wm-pen-art').getAnimations()[0];
        });
        const emit = payload => layout.evaluate(value => window.probeSource.dispatchEvent(new MessageEvent('snapshot', { data: JSON.stringify(value) })), payload);
        const targetCard = layout.locator('#wm-receipt [data-work-key="receipt:6792"]');
        await emit(transition);
        assert.equal(await targetCard.locator('.wm-scene').getAttribute('data-mode'), 'saved', 'Newer successful save takes priority over earlier editing presence');
        assert.match(await targetCard.innerText(), /Vừa lưu xong/);
        assert.equal(await targetCard.locator('.wm-pen-art').evaluate(node => node.getAnimations().length), 0, 'Writing stops when the save is confirmed');
        await layout.waitForTimeout(700);
        assert.equal(await targetCard.locator('.wm-save-confirm').evaluate(node => getComputedStyle(node).opacity), '1');
        assert.equal(await targetCard.locator('.wm-save-confirm').evaluate(node => node.getAnimations()[0].effect.getTiming().iterations), 1, 'Confirmation must not loop');
        const repeatAt = Date.now();
        transition.events.unshift({ ...savedEvent, id: 102, occurredAtUtc: new Date(repeatAt).toISOString() });
        transition.serverTimeUtc = new Date(repeatAt).toISOString();
        await emit(transition);
        assert.equal(await targetCard.locator('.wm-save-confirm').evaluate(node => {
            const animation = node.getAnimations()[0]; return animation.playState === 'running' && animation.currentTime < 400;
        }), true, 'A second save replays only its brief check');
        transition.serverTimeUtc = new Date(repeatAt + 12500).toISOString();
        await emit(transition);
        assert.equal(await targetCard.locator('.wm-scene').getAttribute('data-mode'), 'idle', 'Expiring save confirmation must not revive older typing presence');
        target.editedAtUtc = new Date(repeatAt + 12600).toISOString();
        transition.serverTimeUtc = new Date(repeatAt + 12620).toISOString();
        await emit(transition);
        assert.equal(await targetCard.locator('.wm-scene').getAttribute('data-mode'), 'working', 'Editing after the save resumes writing');
        assert.equal(await targetCard.locator('.wm-save-confirm').evaluate(node => getComputedStyle(node).opacity), '0');
        target.state = 'viewing';
        transition.serverTimeUtc = new Date(repeatAt + 30000).toISOString();
        await emit(transition);
        assert.equal(await targetCard.locator('.wm-scene').getAttribute('data-mode'), 'idle');
        assert.equal(await targetCard.locator('.wm-visual').evaluate(node => node.getAnimations({ subtree: true }).length), 0, 'Waiting work must be static');
        assert.equal(await layout.evaluate(() => {
            const art = document.querySelector('#wm-receipt [data-work-key="receipt:6794"] .wm-art');
            return art === window.neighborArt && art.querySelector('.wm-pen-art').getAnimations()[0] === window.neighborAnimation;
        }), true, 'Save/edit/idle transitions on one receipt must preserve the other document and animation');
        checks.push('Approved animation artwork visibly moves in all 5 modules, centered in 15 cards; save/edit ordering, one-shot confirmation with rapid replay, idle static; neighboring receipt animation unaffected');
        await emit({ ...fixture, serverTimeUtc: new Date().toISOString() });
        await layout.screenshot({ path: path.join(evidence, 'office-approved-animations.png'), fullPage: true });
        const old = { ...fixture, people: [], serverTimeUtc: new Date(utc + 20000).toISOString() };
        await layout.evaluate(payload => window.probeSource.dispatchEvent(new MessageEvent('snapshot', { data: JSON.stringify(payload) })), old);
        assert.equal(await layout.locator('.wm-operations .wm-live-person').count(), 0);
        assert.equal(await layout.locator('.wm-operations .wm-scene[data-active=true]').count(), 0);
        assert.equal(await layout.evaluate(() => document.querySelector('.wm-operations').getAnimations({ subtree: true }).length), 0, 'Old saved work must not keep playing idle animations');
        assert.match(await layout.locator('#wm-receipt').innerText(), /Thêm Gạo ST25 vào phiếu nhập/);
        checks.push('Stable counter positions and animation nodes; escaped metadata; old saved events retain content without false live activity');
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(evidence, 'browser-results.json'), JSON.stringify({ status: 'PASS', checks, pageErrors: errors, fixtureAnimations: animations, animationMotionSamples: motionSamples, realCounters: actualStations.map(item => item.terminal) }, null, 2));
        console.log('PASS: ' + checks.join('; '));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
