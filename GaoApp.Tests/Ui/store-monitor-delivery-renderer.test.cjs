const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('../../GaoApp.Tests.Browser/node_modules/playwright');

test('office renderer keeps independent delivery facts through duplicate, offline, reload, clock and permission changes', async () => {
    const root = path.resolve(__dirname, '../..');
    const output = path.join(root, 'Logs/delivery-roadmap/D10/run-20261008-01');
    fs.mkdirSync(output, { recursive: true });
    const view = fs.readFileSync(path.join(root, 'GaoApp.Web/Areas/Admin/Views/StoreMonitor/Index.cshtml'), 'utf8');
    const markup = view.slice(view.indexOf('<!DOCTYPE html>')).replace(/data-stream-url=.*? role=/, 'data-stream-url="/admin/api/store-activity/stream" role=')
        .replace('@ViewData["StoreName"]', 'Tiệm kiểm tra D10').replace('data-view="3d"', 'data-view="2d"').replace('data-scene-url="@sceneUrl"', '').replace(/<script src="~\/Admin\/js\/store-activity.online.js".*?<\/script>/, '').replaceAll('~/', '/');
    const now = new Date().toISOString(), updated = new Date(Date.now() - 2000).toISOString();
    const departure = new Date(Date.now() - 27 * 60000).toISOString();
    const base = { revision: 4, recipient: 'Khách hàng tại tiệm', picker: 'Mai · Soạn hàng', courier: null,
        custodyConfirmed: false, tripId: null, tripCode: null, departedAtUtc: null, updatedAtUtc: updated,
        lastAction: 'vừa ghi nhận lượng hàng đã soạn', detail: 'Gạo ST25: 1/2 Túi',
        checkedLines: 1, totalLines: 2, shortageLines: 1, tripOrders: 0, settledTotal: null,
        lines: [{ itemName: 'Gạo ST25', unitName: 'Túi', planned: 2, prepared: 1 },
            { itemName: 'Sữa tươi TH', unitName: 'Hộp', planned: 20, prepared: null }] };
    const orders = [
        { ...base, orderId: 1, code: 'GH-20261008-0F5BAD6ED54E39F6', state: 'Picking', eventId: -11, lastActor: 'Mai · Soạn hàng' },
        { ...base, orderId: 2, code: 'GH-THIEU-02', state: 'AwaitingApproval', picker: 'Lan · Soạn hàng', eventId: -12 },
        { ...base, orderId: 3, code: 'GH-GIAO-03', state: 'Delivering', courier: 'Nam · Giao hàng', custodyConfirmed: true,
            tripId: 7, tripCode: 'GH-CHUYEN-07', tripOrders: 3, departedAtUtc: departure, eventId: -13,
            updatedAtUtc: departure, lastAction: 'vừa ghi nhận người giao xuất phát', detail: 'Lượng soạn đã ghi nhận: Gạo ST25: 2/2 Túi' },
        { ...base, orderId: 4, code: 'GH-QUAY-04', state: 'AwaitingReconciliation', courier: 'Nguyệt', custodyConfirmed: true,
            eventId: -14, lastAction: 'vừa ghi nhận kết quả thực giao' },
        { ...base, orderId: 5, code: 'GH-CHOT-05', state: 'Settled', courier: 'Linh', custodyConfirmed: true,
            eventId: -15, settledTotal: 23500, lastActor: 'Thu ngân B', detail: 'Thực giao: Bánh bao bí đỏ: 1 Cái · Bánh sữa: 1 Cái',
            lastAction: 'vừa đối chiếu hàng, tiền và chốt đơn giao' }
    ];
    const events = orders.map(o => ({ id: o.eventId, userId: o.orderId, person: o.picker, module: 'delivery',
        text: o.lastAction, document: o.code, occurredAtUtc: o.updatedAtUtc, action: 'Delivery.picking-report', detail: o.detail, workKey: 'delivery:' + o.orderId }));
    const payload = { serverTimeUtc: now, startedAtUtc: now, people: [], events,
        delivery: { orders, activeCount: 4, pickingCount: 1, deliveringCount: 1, waitingCount: 2, settledCount: 1 } };
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1920, height: 1080 } });
    const page = await context.newPage(), errors = [], commands = [];
    let forbidden = false;
    page.on('pageerror', e => errors.push(e.message));
    await page.addInitScript(snapshot => {
        // This regression covers the complete list; fitted overview has its own dense-screen test.
        localStorage.setItem('gao.monitor.layout', 'detail');
        window.latestSnapshot = snapshot; window.streams = [];
        window.EventSource = class {
            constructor() { this.handlers = {}; window.streams.push(this); setTimeout(() => this.emit('snapshot', window.latestSnapshot), 0); }
            addEventListener(name, callback) { this.handlers[name] = callback; }
            emit(name, body) { if (!this.closed) this.handlers[name]?.({ data: JSON.stringify(body) }); }
            close() { this.closed = true; }
        };
        window.publish = snapshot => {
            window.latestSnapshot = snapshot;
            window.streams.at(-1).emit('snapshot', snapshot);
        };
    }, payload);
    await page.route('**/*', async route => {
        const request = route.request(), url = new URL(request.url());
        assert.equal(url.hostname, 'delivery-wall.test');
        if (request.method() !== 'GET') commands.push(request.method());
        if (url.pathname.startsWith('/Admin/')) return route.fulfill({
            contentType: url.pathname.endsWith('.js') ? 'text/javascript' : 'text/css',
            body: fs.readFileSync(path.join(root, 'GaoApp.Web/wwwroot', url.pathname), 'utf8') });
        if (url.pathname.endsWith('/snapshot')) return route.fulfill({ status: forbidden ? 403 : 200, json: payload });
        return route.fulfill({ contentType: 'text/html', body: markup });
    });
    const cards = page.locator('#wm-delivery .wd-card');
    const card = id => page.locator('#wm-delivery [data-key="delivery-' + id + '"]');
    try {
        await page.goto('http://delivery-wall.test/admin/store-monitor');
        await page.waitForFunction(() => document.querySelectorAll('.wd-card').length === 5);
        assert.equal(await cards.count(), 5);
        assert.match(await card(1).innerText(), /Mai · Soạn hàng/);
        assert.match(await card(2).innerText(), /Lan · Soạn hàng/);
        assert.equal(await card(2).locator('.wd-step-done').count(), 1, 'Waiting for approval must not claim ready or handed over');
        assert.match(await card(1).innerText(), /1\/2 dòng đã ghi nhận · 1 dòng thiếu/);
        assert.match(await card(1).innerText(), /Gạo ST25\s+1 \/ 2 Túi/);
        assert.match(await card(3).innerText(), /Nam · Giao hàng/);
        assert.match(await card(3).innerText(), /Chuyến #7 · 3 đơn/);
        assert.match(await card(3).innerText(), /Đã đi 27 phút/);
        assert.match(await card(3).innerText(), /chưa có báo cáo mới giữa đường/);
        assert.match(await card(5).innerText(), /Đã chốt thực giao\s+23\.500 đ/);
        await page.evaluate(() => { window.originalCard = document.querySelector('[data-key="delivery-1"]'); window.publish(window.latestSnapshot); });
        assert.ok(await page.evaluate(() => window.originalCard === document.querySelector('[data-key="delivery-1"]')));
        assert.equal(await cards.count(), 5);
        // A clock update changes elapsed time only, never a stage, quantity, or saved timestamp.
        const savedTime = await card(3).locator('time').getAttribute('datetime');
        await page.evaluate(() => window.publish({ ...window.latestSnapshot, serverTimeUtc: new Date(Date.now() + 60000).toISOString() }));
        assert.match(await card(3).innerText(), /Đã đi 28 phút/);
        assert.equal(await card(3).getAttribute('data-phase'), 'route');
        assert.equal(await card(3).locator('time').getAttribute('datetime'), savedTime);
        assert.equal(await card(3).locator('progress').count(), 0);
        assert.equal(await card(3).locator('iframe,canvas,[data-latitude],[data-longitude]').count(), 0);
        assert.equal(await card(5).count(), 0, 'Completed card expires after 60 seconds; unfinished cards stay');
        await page.evaluate(() => {
            window.beforeReturn = window.latestSnapshot;
            const returned = structuredClone(window.latestSnapshot);
            returned.delivery.orders[2].returnedAtUtc = new Date().toISOString();
            window.publish(returned);
        });
        assert.equal(await card(3).getAttribute('data-phase'), 'counter');
        assert.match(await card(3).innerText(), /Đã trở về · chờ đối chiếu/);
        assert.equal(await card(3).locator('.wm-trip-elapsed,.wd-money').count(), 0);
        await page.evaluate(() => window.publish(window.beforeReturn));
        await page.evaluate(() => window.dispatchEvent(new Event('offline')));
        assert.equal(await page.locator('#gao-store-wall').getAttribute('data-paused'), 'true');
        assert.match(await card(3).innerText(), /Mất kết nối màn hình/);
        assert.equal(await cards.count(), 4);
        assert.equal(await page.locator('.wd-card[data-active=true]').count(), 0);
        await page.evaluate(() => window.dispatchEvent(new Event('online')));
        await page.waitForFunction(() => document.querySelector('#gao-store-wall').dataset.paused === 'false');
        assert.equal(await cards.count(), 4, 'Reconnecting to the same old snapshot does not revive the completed card');
        await page.reload();
        await page.waitForFunction(() => document.querySelectorAll('.wd-card').length === 5);
        for (const width of [1920, 1440, 390]) {
            await page.setViewportSize({ width, height: width === 390 ? 844 : 1080 });
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'No horizontal page overflow at ' + width);
            assert.ok(await cards.evaluateAll(cards => cards.every(c => c.scrollWidth <= c.clientWidth + 1)), 'No card overflow at ' + width);
            await page.screenshot({ path: path.join(output, 'office-delivery-' + width + '.png'), fullPage: true });
            if (width === 1920) {
                const bottoms = await cards.evaluateAll(cards => cards.map(c => c.getBoundingClientRect().bottom));
                assert.ok(bottoms.every(bottom => bottom <= 1080), 'Delivery cards must fit at 1920x1080: ' + JSON.stringify(bottoms));
            }
        }
        await page.emulateMedia({ reducedMotion: 'reduce' });
        await page.waitForFunction(() => document.querySelector('#gao-store-wall').dataset.motion === 'false');
        assert.equal(await page.locator('#gao-store-wall').getAttribute('data-motion'), 'false');
        assert.ok(await page.locator('.wd-picture *').evaluateAll(nodes => nodes.every(n => getComputedStyle(n).animationName === 'none')));
        await page.locator('#wm-motion').click();
        assert.equal(await page.locator('#gao-store-wall').getAttribute('data-force-motion'), 'true');
        await page.evaluate(() => {
            const saved = structuredClone(window.latestSnapshot), stamp = new Date().toISOString();
            saved.serverTimeUtc = stamp;
            saved.delivery.orders[0].updatedAtUtc = stamp; saved.delivery.orders[0].eventId = -30;
            saved.events.unshift({ ...saved.events[0], id: -30, occurredAtUtc: stamp });
            window.publish(saved);
            window.deliveryAnimation = document.querySelector('[data-key="delivery-1"] .wd-scan').getAnimations()[0];
        });
        assert.equal(await card(1).getAttribute('data-active'), 'true');
        assert.equal(await card(1).locator('.wd-scan').evaluate(n => getComputedStyle(n).animationName), 'wd-scan');
        await page.evaluate(() => window.publish(window.latestSnapshot));
        assert.ok(await page.evaluate(() => window.deliveryAnimation === document.querySelector('[data-key="delivery-1"] .wd-scan').getAnimations()[0]));
        await page.evaluate(() => {
            const hostile = structuredClone(window.latestSnapshot);
            hostile.delivery.orders[0].recipient = '<img src=x onerror="window.attacked=true">';
            hostile.delivery.orders[0].lastActor = '<svg onload="window.attacked=true">';
            hostile.delivery.orders[0].lines[0].itemName = '<img src=x onerror="window.attacked=true">';
            window.publish(hostile);
        });
        assert.equal(await page.locator('.wd-card img,.wd-card svg').count(), 0);
        assert.ok(!(await page.evaluate(() => window.attacked)));
        await page.evaluate(() => window.streams.at(-1).emit('revoked', {}));
        assert.equal(await cards.count(), 0); assert.equal(await page.locator('#wm-feed .wm-event').count(), 0);
        assert.ok(!(await page.locator('#gao-store-wall').innerText()).includes('Khách hàng tại tiệm'));
        await page.reload();
        await page.waitForFunction(() => document.querySelectorAll('.wd-card').length === 5);
        forbidden = true;
        await page.evaluate(() => window.streams.at(-1).onerror());
        await page.waitForFunction(() => document.querySelectorAll('.wd-card').length === 0);
        assert.ok(!(await page.locator('#gao-store-wall').innerText()).includes('Khách hàng tại tiệm'));
        assert.deepEqual(errors, []); assert.deepEqual(commands, []);
    } finally { await browser.close(); }
});
