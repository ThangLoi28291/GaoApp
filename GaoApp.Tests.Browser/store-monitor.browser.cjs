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
        // This probe verifies all work/scene nodes; the fitted overview is covered separately.
        await context.addInitScript(() => {
            localStorage.setItem('gao.monitor.layout', 'detail');
            // This probe targets the detailed 2D work nodes; 3D is now the default.
            localStorage.setItem('gao.monitor.view', '2d');
        });
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
        assert.equal(await monitor.locator('#wm-delivery .wd-empty').count(), 1);
        assert.equal(await monitor.locator('#wm-delivery .wd-card').count(), 0);
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
            people: definitions.map(([module, person, terminal, , document], i) => ({ userId: actorIds.get(person), module, person, terminal, workKey: work(definitions[i]), document, state: 'editing', seenAtUtc: at(0), openedAtUtc: at(-180000 - i * 25000), editedAtUtc: at(-10000) })),
            events: definitions.map(([module, person, terminal, text, document, detail], i) => ({ id: 100 - i, userId: actorIds.get(person), module, person, terminal, text, document, detail, workKey: work(definitions[i]), occurredAtUtc: at(-i * 300), action: ['POS.AddByBarcode','POS.Finalize','POS.HoldOrder','StockDocuments.AddLineByBarcode','StockCounts.UpdateLine','LabelPrinting.Print','StockDocuments.Approve','ReceiptIntake.Capture','StockDocuments.UpdateLine','StockTransfers.AddLine','StockCounts.UpdateLine','LabelPrinting.Plan','LabelPrinting.Confirm','StockDocuments.SubmitApproval','StockDocuments.SavePriceDraft'][i] }))
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
        const emit = payload => layout.evaluate(value => window.probeSource.dispatchEvent(new MessageEvent('snapshot', { data: JSON.stringify(value) })), payload);
        const expected = {
            'order:182041': ['scan', '.sm-product'], 'order:182039': ['print', '.sm-bill'],
            'receipt:6792': ['check', '.sm-receive-beam'], 'receipt:6794': ['input', '.sm-document-sheet'],
            'count:903': ['check', '.sm-stock-beam'], 'transfer:903': ['transfer', '.sm-transfer-crate'],
            'label:248': ['print', '.sm-label-feed'], 'label:249': ['input', '.sm-preview-label:nth-child(2)'],
            'receipt:6790': ['approved', '.sm-stamp']
        };
        const animations = {};
        for (const [key, [effect, part]] of Object.entries(expected)) {
            const scene = layout.locator(`.wm-station[data-work-key="${key}"] .wm-scene`);
            assert.equal(await scene.getAttribute('data-effect'), effect);
            animations[key] = await scene.locator(part).evaluate(node => node.getAnimations().map(a => ({ name:a.animationName, iterations:a.effect.getTiming().iterations })));
            assert.ok(animations[key].some(a => a.iterations === 1), `${key} must actually animate once`);
        }
        assert.equal(await layout.locator('[class*=progress],progress,[role=progressbar]').count(), 0);
        assert.equal(await layout.locator('#wm-pos .wm-station').count(), 3);
        for (const key of ['receipt', 'warehouse', 'label', 'review']) {
            assert.equal(await layout.locator(`#wm-${key} .wm-station`).count(), 3);
            assert.equal(await layout.locator(`#wm-${key} .wm-stations`).evaluate(node => node.scrollHeight > node.clientHeight), false, `${key} must not hide work in an internal scroll`);
        }
        assert.equal(await layout.locator('.wm-art').evaluateAll(items => items.filter(art => {
            const sprite=art.getBoundingClientRect(),stage=art.closest('.wm-visual').getBoundingClientRect();
            return sprite.left<stage.left-1||sprite.right>stage.right+1||sprite.top<stage.top-1||sprite.bottom>stage.bottom+1;
        }).length), 0);
        await layout.evaluate(() => {
            window.probeScene=document.querySelector('[data-work-key="receipt:6794"] .wm-art');
            window.probeAnimation=window.probeScene.querySelector('.sm-document-sheet').getAnimations()[0];
        });
        await emit(fixture);
        assert.equal(await layout.evaluate(() => window.probeScene===document.querySelector('[data-work-key="receipt:6794"] .wm-art')&&window.probeAnimation===window.probeScene.querySelector('.sm-document-sheet').getAnimations()[0]),true,'Repeated snapshot preserves the model and animation');
        const savedAt=Date.now(),transition=structuredClone(fixture);
        transition.serverTimeUtc=new Date(savedAt).toISOString();
        transition.events.unshift({...transition.events.find(e=>e.workKey==='receipt:6792'),id:101,occurredAtUtc:new Date(savedAt).toISOString()});
        await emit(transition);
        assert.equal(await layout.locator('[data-work-key="receipt:6792"] .sm-receive-beam').evaluate(node=>node.getAnimations()[0].currentTime<400),true,'New save replays its own physical effect');
        assert.equal(await layout.evaluate(()=>window.probeScene===document.querySelector('[data-work-key="receipt:6794"] .wm-art')&&window.probeAnimation===window.probeScene.querySelector('.sm-document-sheet').getAnimations()[0]),true,'Neighboring work remains undisturbed');
        const expired=structuredClone(transition);expired.people=[];expired.serverTimeUtc=new Date(savedAt+30000).toISOString();
        await emit(expired);
        assert.equal(await layout.locator('.wm-operations .wm-scene[data-active=true]').count(),0);
        assert.equal(await layout.locator('.wm-visual').evaluateAll(items=>items.reduce((n,node)=>n+node.getAnimations({subtree:true}).length,0)),0,'Old work has no looping machine animation');
        await emit({...fixture,serverTimeUtc:new Date().toISOString()});
        await layout.screenshot({path:path.join(evidence,'office-approved-animations.png'),fullPage:true});
        await layout.locator('#wm-theme').click();
        assert.equal(await layout.locator('#gao-store-wall').getAttribute('data-theme'),'dark');
        await layout.screenshot({path:path.join(evidence,'office-dark.png'),fullPage:true});
        await layout.locator('#wm-theme').click();
        for(const width of [1366,1024,704,390]) {
            await layout.setViewportSize({width,height:900});
            assert.equal(await layout.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,`No horizontal overflow at ${width}`);
        }
        await layout.locator('#wm-motion').click();
        assert.equal(await layout.locator('.wm-operations').evaluate(node=>node.getAnimations({subtree:true}).length),0);
        await layout.locator('#wm-motion').click();
        await layout.emulateMedia({reducedMotion:'reduce'});
        await layout.waitForFunction(()=>document.querySelector('#gao-store-wall').dataset.paused==='true');
        assert.equal(await layout.locator('.wm-operations').evaluate(node=>node.getAnimations({subtree:true}).length),0);
        await layout.locator('#wm-motion').click();
        assert.equal(await layout.locator('#gao-store-wall').getAttribute('data-force-motion'),'true','User can explicitly enable effects despite reduced-motion preference');
        await layout.emulateMedia({reducedMotion:'no-preference'});
        const escaped=structuredClone(fixture);escaped.events[0].detail='<img src=x onerror="window.probeXss=true">';
        escaped.events.find(event=>event.module==='receipt').document='<img src=x onerror="window.probeXss=true">';
        await emit(escaped);
        assert.equal(await layout.locator('.wm-station img').count(),0);
        assert.equal(await layout.evaluate(()=>window.probeXss),undefined);
        await layout.locator('[data-work-key="receipt:6792"]').click();
        assert.equal(await layout.locator('#wm-selection').isVisible(),true);
        assert.equal(await layout.locator('#wm-feed .wm-event').evaluateAll(items=>items.every(item=>item.textContent.includes('Hoàng Nam'))),true);
        await layout.locator('#wm-show-all').click();
        checks.push('UI fixture: 15 independent model cards, action-specific one-shot animations, no progress bars or clipped models, repeated snapshot stability, rapid save isolation, stale/idle stop, responsive layout, themes, reduced motion with explicit opt-in, escaped metadata and employee filter');
        const motionSamples={finite:true};
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(evidence, 'browser-results.json'), JSON.stringify({ status: 'PASS', checks, pageErrors: errors, fixtureAnimations: animations, animationMotionSamples: motionSamples, realCounters: actualStations.map(item => item.terminal) }, null, 2));
        console.log('PASS: ' + checks.join('; '));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
