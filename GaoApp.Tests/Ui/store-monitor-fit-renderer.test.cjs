const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('../../GaoApp.Tests.Browser/node_modules/playwright');
const root = path.resolve(__dirname, '../..');
const output = path.join(root, 'Logs/store-monitor-fit-20261009');
const keys = ['pos', 'receipt', 'warehouse', 'label', 'review', 'delivery'];
const counts = [8, 7, 6, 5, 4, 12];

function fixture() {
    const now = new Date().toISOString(), saved = new Date(Date.now() - 2000).toISOString();
    const people = [], events = [];
    const actions = ['POS.AddByBarcode', 'StockDocuments.AddLineByBarcode', 'StockCounts.UpdateLine', 'LabelPrinting.Print', 'StockDocuments.Approve'];
    const texts = ['vừa quét Sữa tươi TH chocolate', 'vừa nhập Gạo ST25', 'vừa đếm Bánh quy D-max', 'vừa gửi in tem Sữa tươi TH', 'vừa duyệt phiếu nhập gạo'];
    keys.slice(0, 5).forEach((module, m) => {
        for (let i = 1; i <= counts[m]; i++) {
            const userId = m * 20 + i, workKey = ['order:', 'receipt:', 'count:', 'label:', 'receipt:'][m] + (m * 100 + i);
            const item = { module, userId, person: ['Thu ngân', 'Nhân viên nhập', 'Kiểm kho', 'In tem', 'Quản lý'][m] + ' ' + i,
                terminal: 'POS0' + i, workKey, document: ['Giỏ', 'Phiếu nhập', 'Kiểm kê', 'Lệnh in', 'Phiếu nhập'][m] + ' #' + (m * 100 + i) };
            people.push({ ...item, state: 'editing', openedAtUtc: new Date(Date.now() - 300000).toISOString(), editedAtUtc: saved });
            events.push({ ...item, id: 1000 + userId, action: actions[m], text: texts[m], detail: 'Sữa tươi TH chocolate hộp 180ml · 20 Hộp → 25 Hộp', occurredAtUtc: saved });
        }
    });
    const orders = Array.from({ length: counts[5] }, (_, i) => ({ orderId: i + 1, code: 'GH-20261009-ABCDEF123456789' + i,
        state: ['Picking', 'AwaitingApproval', 'Delivering', 'Settled'][i % 4], eventId: -i - 1, updatedAtUtc: saved,
        picker: 'Mai · Soạn hàng ' + (i + 1), courier: 'Nguyệt ' + (i + 1), custodyConfirmed: i % 4 >= 2,
        recipient: 'Khách hàng ' + (i + 1), lastActor: 'Quản trị Demo', lastAction: 'vừa ghi nhận lượng Sữa tươi TH đã soạn',
        detail: 'Sữa tươi TH chocolate hộp 180ml: 20/25 Hộp · Gạo ST25: 2/2 Túi', totalLines: 6, checkedLines: 2,
        shortageLines: i % 4 === 1 ? 1 : 0, tripId: i % 4 >= 2 ? 2 : null, tripOrders: 3,
        departedAtUtc: i % 4 >= 2 ? new Date(Date.now() - 600000).toISOString() : null,
        settledTotal: i % 4 === 3 ? 23500 : null,
        lines: Array.from({ length: 6 }, (_, n) => ({ itemName: 'Sữa tươi TH chocolate hộp 180ml · Lô ' + (n + 1), unitName: 'Hộp', planned: 25, prepared: n < 2 ? 20 : null })) }));
    return { serverTimeUtc: now, startedAtUtc: now, people, events, delivery: { orders, activeCount: 9, pickingCount: 3, deliveringCount: 3, waitingCount: 3 } };
}

async function setup(viewport = { width: 1366, height: 768 }, reducedMotion = 'no-preference', useClock = false) {
    fs.mkdirSync(output, { recursive: true });
    const view = fs.readFileSync(path.join(root, 'GaoApp.Web/Areas/Admin/Views/StoreMonitor/Index.cshtml'), 'utf8');
    const html = view.slice(view.indexOf('<!DOCTYPE html>')).replace(/data-stream-url=.*? role=/, 'data-stream-url="/admin/api/store-activity/stream" role=')
        .replace('@ViewData["StoreName"]', 'Gao Mart · Văn phòng').replace('data-view="3d"', 'data-view="2d"').replace('data-scene-url="@sceneUrl"', '').replace(/<script src="~\/Admin\/js\/store-activity.online.js".*?<\/script>/, '').replaceAll('~/', '/');
    const payload = fixture();
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const page = await browser.newPage({ viewport, reducedMotion }), errors = [], writes = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.addInitScript(snapshot => {
        window.latestSnapshot = snapshot; window.streams = [];
        window.EventSource = class {
            constructor() { this.handlers = {}; window.streams.push(this); setTimeout(() => this.emit('snapshot', window.latestSnapshot), 0); }
            addEventListener(name, callback) { this.handlers[name] = callback; }
            emit(name, body) { if (!this.closed) this.handlers[name]?.({ data: JSON.stringify(body) }); }
            close() { this.closed = true; }
        };
        window.publish = snapshot => { window.latestSnapshot = snapshot; window.streams.at(-1).emit('snapshot', snapshot); };
        window.heartbeat = () => window.streams.at(-1).emit('heartbeat', { serverTimeUtc: new Date().toISOString() });
    }, payload);
    await page.route('**/*', route => {
        const request = route.request(), url = new URL(request.url());
        assert.equal(url.hostname, 'fit-wall.test');
        if (request.method() !== 'GET') writes.push(request.method());
        if (url.pathname.startsWith('/Admin/')) return route.fulfill({ contentType: url.pathname.endsWith('.js') ? 'text/javascript' : 'text/css', body: fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot', url.pathname), 'utf8') });
        if (url.pathname.endsWith('/snapshot')) return route.fulfill({ status: 200, json: payload });
        return route.fulfill({ contentType: 'text/html', body: html });
    });
    if (useClock) await page.clock.install();
    await page.goto('http://fit-wall.test/admin/store-monitor');
    await page.waitForFunction(() => document.querySelector('#wm-receipt .wm-station'));
    return { browser, page, errors, writes };
}

test('dense overview fits at 100% zoom, shows the first three POS terminals and keeps other work reachable', async () => {
    const { browser, page, errors, writes } = await setup();
    try {
        await page.locator('#wm-cycle').click();
        for (const [width, height] of [[1920, 1080], [1600, 900], [1366, 768], [1280, 650], [1100, 620]]) {
            await page.setViewportSize({ width, height });
            await page.waitForTimeout(100);
            const geometry = await page.evaluate(() => ({
                mode: document.querySelector('#gao-store-wall').dataset.layout,
                width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight,
                areas: [...document.querySelectorAll('.wm-area')].map(a => {
                    const rect = a.getBoundingClientRect(), heading = a.querySelector('h2').getBoundingClientRect();
                    return { id: a.id, bottom: rect.bottom, heading: heading.bottom, clipping: a.scrollHeight - a.clientHeight };
                }),
                text: [...document.querySelectorAll('.wm-operations .wm-person strong,.wm-operations .wm-task-detail,.wm-operations .wm-task-time,.wm-operations .wd-person,.wm-operations .wd-state')].filter(el => el.getClientRects().length).map(el => parseFloat(getComputedStyle(el).fontSize)),
                clocks: [...document.querySelectorAll('.wm-operations .wm-task-time,.wm-operations .wd-timing')].map(el => ({ bottom: el.getBoundingClientRect().bottom, parent: el.closest('.wm-station,.wd-card').getBoundingClientRect().bottom })),
                cards: [...document.querySelectorAll('.wm-operations .wm-station,.wm-operations .wd-card')].map(el => ({ key: el.dataset.key, clipping: el.scrollHeight - el.clientHeight })),
                details: [...document.querySelectorAll('.wm-operations .wm-task-title,.wm-operations .wm-task-detail,.wm-operations .wd-last-action,.wm-operations .wd-detail')].filter(el => el.getClientRects().length).map(el => ({ bottom: el.getBoundingClientRect().bottom, parent: (el.closest('.wm-station-work') || el.closest('.wd-card')).getBoundingClientRect().bottom }))
            }));
            assert.equal(geometry.mode, 'wall');
            assert.ok(geometry.width <= width + 1, 'Page width at ' + width + ': ' + JSON.stringify(geometry));
            assert.ok(geometry.height <= height + 1, 'Page height at ' + width + ': ' + JSON.stringify(geometry));
            assert.equal(geometry.areas.length, 6);
            assert.ok(geometry.areas.every(a => a.bottom <= height && a.heading <= height && a.clipping <= 1), 'All area content fits: ' + JSON.stringify(geometry));
            assert.ok(geometry.clocks.every(c => c.bottom <= c.parent + 1), 'Actual clock must remain visible: ' + JSON.stringify(geometry.clocks));
            assert.ok(geometry.cards.every(c => c.clipping <= 1), 'No cropped card content: ' + width + ' ' + JSON.stringify(geometry.cards));
            assert.ok(geometry.details.every(c => c.bottom <= c.parent + 1), 'No half-hidden activity text: ' + width + ' ' + JSON.stringify(geometry.details));
            assert.ok(geometry.text.every(size => size >= 12), 'Readable text at ' + width);
            await page.screenshot({ path: path.join(output, 'overview-' + width + 'x' + height + '.png') });
        }
        await page.setViewportSize({ width: 1366, height: 768 });
        await page.waitForTimeout(100);
        for (let k = 0; k < keys.length; k++) {
            const key = keys[k], cards = '#wm-' + key + ' ' + (key === 'delivery' ? '.wd-card' : '.wm-station[data-user-id]');
            const seen = new Set();
            const area = page.locator('#wm-' + key);
            const visibleCount = key === 'pos' ? 3 : counts[k];
            assert.match(await area.locator('.wm-area-heading .wm-badge').innerText(), new RegExp('^' + (key === 'delivery' ? 9 : visibleCount) + '(?: |$)'));
            for (let n = 0; n < visibleCount; n++) {
                for (const id of await page.locator(cards).evaluateAll(nodes => nodes.map(node => node.dataset.key))) seen.add(id);
                const nextPage = area.locator('[data-page-step="1"]');
                if (await nextPage.count()) await nextPage.click();
                else break;
            }
            assert.equal(seen.size, visibleCount, 'Visible ' + key + ' work reachable without zooming');
        }
        await page.evaluate(() => { window.retained = document.querySelector('#wm-receipt .wm-station'); window.publish(window.latestSnapshot); });
        assert.ok(await page.evaluate(() => window.retained === document.querySelector('#wm-receipt .wm-station')));
        const first = page.locator('#wm-receipt .wm-station').first();
        await first.focus(); await page.keyboard.press('Enter');
        const dialog = page.locator('#wm-work-detail');
        assert.ok(await dialog.evaluate(d => d.open));
        assert.match(await dialog.innerText(), /Sữa tươi TH chocolate hộp 180ml · 20 Hộp → 25 Hộp/);
        await page.evaluate(() => {
            const next = structuredClone(window.latestSnapshot), key = document.querySelector('#wm-detail-body .wm-station').dataset.workKey;
            next.events.find(e => e.workKey === key && e.module === 'receipt').detail = 'Gạo ST25 · 25 Túi → 30 Túi';
            window.publish(next);
        });
        assert.match(await dialog.innerText(), /Gạo ST25 · 25 Túi → 30 Túi/);
        await page.screenshot({ path: path.join(output, 'receipt-detail.png') });
        await page.keyboard.press('Escape');
        assert.ok(await dialog.evaluate(d => !d.open));
        await page.locator('#wm-delivery .wd-card').first().click();
        assert.equal(await dialog.locator('.wd-lines li').count(), 6);
        assert.match(await dialog.innerText(), /Lô 6/);
        await page.screenshot({ path: path.join(output, 'delivery-detail.png') });
        await page.locator('#wm-detail-close').click();
        await page.locator('#wm-layout').click();
        assert.equal(await page.locator('#gao-store-wall').getAttribute('data-layout'), 'detail');
        assert.equal(await page.locator('#wm-receipt .wm-station').count(), counts[1]);
        await page.locator('#wm-layout').click();
        await page.locator('#wm-theme').click();
        await page.screenshot({ path: path.join(output, 'overview-dark.png') });
        await page.setViewportSize({ width: 390, height: 844 });
        await page.waitForTimeout(100);
        assert.equal(await page.locator('#gao-store-wall').getAttribute('data-layout'), 'detail');
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        assert.equal(await page.locator('#wm-delivery .wd-card').count(), counts[5]);
        assert.deepEqual(errors, []); assert.deepEqual(writes, []);
    } finally { await browser.close(); }
});

test('rotation moves presentation only and pauses for hover, focus, details, offline and reduced motion', async () => {
    const { browser, page, errors } = await setup(undefined, undefined, true);
    try {
        const key = () => page.locator('#wm-receipt .wm-station').first().getAttribute('data-key');
        const before = await key();
        const facts = await page.evaluate(() => JSON.stringify(window.latestSnapshot));
        await page.mouse.move(1, 1);
        for (let i = 0; i < 3; i++) { await page.evaluate(() => window.heartbeat()); await page.clock.runFor(4000); }
        assert.notEqual(await key(), before);
        assert.equal(await page.evaluate(() => JSON.stringify(window.latestSnapshot)), facts, 'Rotation must not alter saved quantities, states, timestamps or events');
        await page.locator('#wm-receipt').hover();
        const hovered = await key();
        for (let i = 0; i < 3; i++) { await page.evaluate(() => window.heartbeat()); await page.clock.runFor(4000); }
        assert.equal(await key(), hovered);
        await page.mouse.move(1, 1); await page.locator('#wm-receipt .wm-station').first().focus();
        for (let i = 0; i < 3; i++) { await page.evaluate(() => window.heartbeat()); await page.clock.runFor(4000); }
        assert.equal(await key(), hovered);
        await page.keyboard.press('Enter');
        const deliveryPage = await page.locator('#wm-delivery .wd-card').first().getAttribute('data-key');
        for (let i = 0; i < 3; i++) { await page.evaluate(() => window.heartbeat()); await page.clock.runFor(4000); }
        assert.equal(await page.locator('#wm-delivery .wd-card').first().getAttribute('data-key'), deliveryPage);
        await page.locator('#wm-detail-close').click();
        await page.mouse.move(1, 1); await page.locator('#wm-receipt .wm-station').evaluate(el => el.blur());
        await page.evaluate(() => window.dispatchEvent(new Event('offline')));
        const offline = await key();
        await page.clock.runFor(12000);
        assert.equal(await key(), offline);
        assert.equal(await page.locator('#gao-store-wall').getAttribute('data-paused'), 'true');
        await page.evaluate(() => window.dispatchEvent(new Event('online')));
        await page.clock.runFor(1);
        await page.emulateMedia({ reducedMotion: 'reduce' });
        await page.clock.runFor(20);
        await page.waitForFunction(() => document.querySelector('#wm-cycle').getAttribute('aria-pressed') === 'false');
        assert.equal(await page.locator('#wm-cycle').getAttribute('aria-pressed'), 'false');
        const reduced = await key();
        for (let i = 0; i < 3; i++) { await page.evaluate(() => window.heartbeat()); await page.clock.runFor(4000); }
        assert.equal(await key(), reduced);
        await page.locator('#wm-cycle').click(); await page.mouse.move(1, 1);
        for (let i = 0; i < 3; i++) { await page.evaluate(() => window.heartbeat()); await page.clock.runFor(4000); }
        assert.notEqual(await key(), reduced, 'Explicit rotation opt-in works with reduced motion');
        await page.reload(); await page.clock.runFor(1);
        assert.equal(await page.locator('#wm-cycle').getAttribute('aria-pressed'), 'true', 'Explicit rotation preference survives reload');
        await page.evaluate(() => {
            const empty = { ...window.latestSnapshot, people: [], events: [], delivery: { orders: [], activeCount: 0 } };
            window.publish(empty);
        });
        assert.equal(await page.locator('.wm-area').count(), 6);
        assert.equal(await page.locator('.wm-pager').count(), 0);
        assert.ok(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight + 1));
        assert.match(await page.locator('#wm-delivery .wd-empty').innerText(), /Chưa có đơn giao/);
        assert.deepEqual(errors, []);
    } finally { await browser.close(); }
});

test('revoke clears customer details in the open dialog and all pages', async () => {
    const { browser, page, errors } = await setup();
    try {
        await page.locator('#wm-delivery .wd-card').first().click();
        assert.match(await page.locator('#wm-work-detail').innerText(), /Khách hàng 1/);
        await page.evaluate(() => window.streams.at(-1).emit('revoked', {}));
        assert.ok(await page.locator('#wm-work-detail').evaluate(d => !d.open));
        assert.equal(await page.locator('#wm-detail-body').innerText(), '');
        assert.equal(await page.locator('.wm-operations .wm-station[data-user-id],.wd-card').count(), 0);
        assert.equal(await page.locator('.wm-pager').count(), 0);
        assert.ok(!(await page.locator('#gao-store-wall').innerText()).includes('Khách hàng 1'));
        assert.deepEqual(errors, []);
    } finally { await browser.close(); }
});

test('finished cards, pagers and open details expire without needing another snapshot', async () => {
    const { browser, page, errors, writes } = await setup(undefined, undefined, true);
    try {
        await page.locator('#wm-cycle').click();
        await page.evaluate(() => {
            const snapshot = structuredClone(window.latestSnapshot), stamp = new Date().toISOString();
            snapshot.serverTimeUtc = stamp;
            snapshot.delivery.completedCardLifetimeSeconds = 60;
            snapshot.delivery.orders = [0, 1, 2].map((n) => ({ ...snapshot.delivery.orders[n], state: n === 2 ? 'Delivering' : n === 1 ? 'Cancelled' : 'Settled', completedAtUtc: n === 2 ? null : stamp, updatedAtUtc: stamp }));
            snapshot.delivery.activeCount = 1;
            window.publish(snapshot);
        });
        await page.locator('#wm-delivery .wd-card[data-delivery-id="1"]').click();
        assert.ok(await page.locator('#wm-work-detail').evaluate(d => d.open));
        for (let i = 0; i < 11; i++) { await page.evaluate(() => window.heartbeat()); await page.clock.runFor(5000); }
        await page.clock.runFor(4000);
        assert.equal(await page.locator('#wm-delivery .wd-card[data-delivery-id="1"]').count(), 1);
        await page.evaluate(() => window.dispatchEvent(new Event('offline')));
        // The wall's one-second clock tick removes expired cards even without SSE.
        await page.clock.runFor(2000);
        assert.equal(await page.locator('#wm-delivery .wd-card[data-delivery-id="1"],#wm-delivery .wd-card[data-delivery-id="2"]').count(), 0);
        assert.equal(await page.locator('#wm-delivery .wd-card[data-delivery-id="3"]').count(), 1);
        assert.equal(await page.locator('#wm-delivery .wm-pager').count(), 0);
        assert.ok(await page.locator('#wm-work-detail').evaluate(d => !d.open));
        assert.equal(await page.locator('#wm-detail-body').innerText(), '');
        await page.evaluate(() => window.publish({ ...window.latestSnapshot, serverTimeUtc: new Date().toISOString() }));
        assert.equal(await page.locator('#wm-delivery .wd-card').count(), 1, 'Repeated old snapshot cannot resurrect expired cards');
        assert.equal(await page.locator('#wm-delivery .wd-card').getAttribute('data-delivery-id'), '3');
        assert.deepEqual(errors, []); assert.deepEqual(writes, []);
    } finally { await browser.close(); }
});
