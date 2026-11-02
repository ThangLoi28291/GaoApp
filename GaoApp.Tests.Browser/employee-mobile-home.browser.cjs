'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const { PNG } = require(path.join(path.dirname(require.resolve('playwright-core/package.json')), 'lib', 'utilsBundle.js'));
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let raw = ''; process.stdin.on('data', chunk => raw += chunk); process.stdin.on('end', () => resolve(raw)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--host-resolver-rules=MAP *.phase6.test 127.0.0.1', '--no-proxy-server'] });
    // Mask the fixed footer box: the timestamp span changes width as its digits change.
    const errors = [], proof = { phase: info.phase, viewports: [], externalRequests: [], phoneMasks: ['.gmh-report-footer', '[data-home-person]'], existingChromeWindowsPreserved: true };
    const contexts = [];
    try {
        const origin = new URL(info.baseUrl);
        async function login(name, width = 1440, foreign = false) {
            const context = await browser.newContext({ viewport: { width, height: 960 }, ignoreHTTPSErrors: true, reducedMotion: 'reduce', timezoneId: 'America/Los_Angeles', serviceWorkers: 'block' });
            contexts.push(context);
            await context.route('**/*', route => {
                const url = new URL(route.request().url());
                if (url.protocol === 'https:' && url.port === origin.port && ['shop.phase6.test', 'other.phase6.test'].includes(url.hostname)) return route.continue();
                if (['data:', 'blob:', 'about:'].includes(url.protocol)) return route.continue();
                proof.externalRequests.push(url.origin); return route.abort();
            });
            const page = await context.newPage(); page.on('pageerror', error => errors.push(error.message));
            await page.addInitScript(() => {
                window.__desktopHomeVisibility = [];
                document.addEventListener('visibilitychange', () => window.__desktopHomeVisibility.push({ visibility: document.visibilityState, width: innerWidth }));
            });
            const base = foreign ? info.baseUrl.replace('shop.phase6.test', 'other.phase6.test') : info.baseUrl;
            const loginResponse = await page.goto(base + '/admin/account/login');
            if (!await page.locator('[name=UserName]').count()) {
                await page.screenshot({ path: path.join(info.output, info.phase + '-login-diagnostic.png'), fullPage: true });
                throw new Error(`Login form missing: ${loginResponse.status()} ${page.url()} ${(await page.locator('body').innerText()).slice(0, 800)}`);
            }
            await page.locator('[name=UserName]').fill(info.accounts[name].user); await page.locator('[name=Password]').fill(info.accounts[name].password);
            if (await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.accounts[name].terminalId || (foreign ? info.foreignTerminalId : info.terminalId)));
            await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
            return { context, page, base };
        }
        async function settle(page) {
            await page.evaluate(() => document.fonts.ready);
            await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        }
        async function phone(page) {
            await page.goto('about:blank');
            await page.setViewportSize({ width: 390, height: 844 });
            let salesReads = 0;
            const listener = request => { if (new URL(request.url()).pathname === '/admin/reports/overview/data') salesReads++; };
            page.on('request', listener);
            await page.goto(info.baseUrl + '/admin');
            await page.locator('[data-mobile-home] [data-home-content]:visible').waitFor();
            await page.locator('[data-mobile-header-ready]').first().waitFor({ state: 'attached' });
            await settle(page);
            assert.equal(await page.locator('[data-mobile-home] [data-home-metric=netSales]').innerText(), '350 ₫');
            assert.equal(await page.locator('[data-desktop-home]').isVisible(), false);
            await page.screenshot({ path: path.join(info.output, info.phase + '-phone.png'), animations: 'disabled',
                mask: proof.phoneMasks.map(selector => page.locator(selector)) });
            if (info.phase === 'candidate') {
                const before = PNG.sync.read(fs.readFileSync(path.join(info.output, 'baseline-phone.png')));
                const after = PNG.sync.read(fs.readFileSync(path.join(info.output, 'candidate-phone.png')));
                assert.equal(before.width, after.width); assert.equal(before.height, after.height);
                let changed = 0;
                for (let i = 0; i < before.data.length; i += 4)
                    if ([0, 1, 2, 3].some(channel => before.data[i + channel] !== after.data[i + channel])) changed++;
                const budget = Math.floor(before.width * before.height * .0001);
                assert.ok(changed <= budget, `Phone layout changed: ${changed} pixels, budget ${budget}`);
                proof.phonePixelReview = { width: before.width, height: before.height, changedPixels: changed, maximumChangedPixels: budget,
                    maximumChangedFraction: .0001, dynamicMasks: proof.phoneMasks };
            }
            page.off('request', listener); assert.equal(salesReads, 1, 'Only the phone owner may fetch sales on a phone');
            proof.phoneSalesReads = salesReads;
        }
        const manager = await login('home_manager');
        await phone(manager.page);
        const page = manager.page;
        await page.setViewportSize({ width: 1440, height: 960 });
        await page.goto(info.baseUrl + '/admin');
        if (info.phase === 'baseline') {
            await settle(page);
            await page.screenshot({ path: path.join(info.output, 'baseline-desktop.png'), fullPage: true, animations: 'disabled' });
            const employeeBefore=await login('home_staff',390);
            await employeeBefore.page.goto(info.baseUrl+'/admin');
            await employeeBefore.page.locator('[data-mobile-header-ready]').first().waitFor({state:'attached'}); await settle(employeeBefore.page);
            await employeeBefore.page.waitForFunction(()=>document.querySelector('[data-mobile-pins]')?.dataset.pinsReady==='true');
            const dock=await employeeBefore.page.locator('[data-mobile-pins]').evaluate(node=>[...node.querySelectorAll('a,button')].map(n=>({id:n.dataset.mobilePinId||null,href:n.getAttribute('href'),label:n.textContent.trim(),current:n.getAttribute('aria-current')})));
            fs.writeFileSync(path.join(info.output,'baseline-employee-dock.json'),JSON.stringify(dock));
            await employeeBefore.page.locator('[data-mobile-pins]').screenshot({path:path.join(info.output,'baseline-employee-dock.png'),animations:'disabled'});
            await employeeBefore.page.screenshot({path:path.join(info.output,'baseline-employee-phone.png'),animations:'disabled'});
            await employeeBefore.page.setViewportSize({width:1440,height:960});
            await employeeBefore.page.locator('[data-employee-statistics]:visible').waitFor(); await settle(employeeBefore.page);
            await employeeBefore.page.mouse.move(0,0); await employeeBefore.page.evaluate(()=>document.activeElement?.blur());
            await employeeBefore.page.screenshot({path:path.join(info.output,'baseline-employee-desktop.png'),animations:'disabled',mask:[employeeBefore.page.locator('[data-employee-statistics] > .dwh-section-heading')]});

        } else {
            await page.locator('[data-dwh-business][aria-busy=false] [data-dwh-content]:visible').waitFor();
            await settle(page);
            assert.equal(await page.locator('[data-dwh-metric=netSales]').innerText(), '350 ₫');
            assert.equal(await page.locator('[data-dwh-metric=salesOrders]').innerText(), '2');
            assert.equal(await page.locator('[data-dwh-metric=aov]').innerText(), '175 ₫');
            assert.equal(await page.locator('[data-dwh-products] li').count(), 1);
            const contrast = await page.evaluate(() => {
                const rgb = value => (value.match(/[\d.]+/g) || []).slice(0, 3).map(Number);
                const light = color => rgb(color).map(v => { v /= 255; return v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; }).reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
                return [...document.querySelectorAll('[data-desktop-home] .dwh-section-heading p,[data-desktop-home] .dwh-section-tag,[data-desktop-home] .dwh-kpi small,[data-desktop-home] .dwh-panel-heading > span,[data-desktop-home] .dwh-product-caption,[data-desktop-home] .dwh-product-rank')].map(element => {
                    let parent = element, background = 'rgb(255,255,255)';
                    while (parent) { const color = getComputedStyle(parent).backgroundColor; if (color !== 'transparent' && !/rgba\([^)]*,\s*0\)$/.test(color)) { background = color; break; } parent = parent.parentElement; }
                    const a = light(getComputedStyle(element).color), b = light(background);
                    return { text: element.textContent.trim(), ratio: (Math.max(a, b) + .05) / (Math.min(a, b) + .05) };
                });
            });
            assert.ok(contrast.every(item => item.ratio >= 4.5), JSON.stringify(contrast)); proof.secondaryTextContrast = contrast;
            assert.match(await page.locator('[data-dwh-products]').innerText(), /Sữa tươi hữu cơ/);
            assert.equal(await page.locator('[data-dwh-item]').count(), 0);
            const html = await page.locator('[data-desktop-home]').innerHTML();
            assert.doesNotMatch(html, /HOME_HIDDEN_CONFIG_CANARY|HOME_INACTIVE_CANARY|HOME_OTHER_STORE_CANARY/);
            assert.match(await page.locator('[data-dwh-quick][href*="sort=name"]').getAttribute('href'), /sort=name&status=active/);
            const links = await page.locator('[data-dwh-quick]').evaluateAll(nodes => nodes.map(node => ({ id: node.dataset.menuId, href: node.getAttribute('href') })));
            const sidebar = await page.locator('[data-admin-navigation] [data-nav-destination]').evaluateAll(nodes => nodes.map(node => ({ id: node.closest('[data-nav-menu-id]').dataset.navMenuId, href: node.getAttribute('href') })));
            for (const link of links) assert.ok(sidebar.some(item => item.id === link.id && item.href === link.href), 'Home uses exactly the configured sidebar destinations');
            for (const width of [1024, 1366, 1440, 1920]) {
                await page.setViewportSize({ width, height: 960 });
                await page.bringToFront();
                await page.locator('[data-dwh-business][aria-busy=false] [data-dwh-content]:visible').waitFor();
                await settle(page);
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `Overflow at ${width}`);
                assert.equal(await page.locator('[data-mobile-home]').isVisible(), false);
                const state = await page.evaluate(() => ({ visibility: document.visibilityState, busy: document.querySelector('[data-dwh-business]').getAttribute('aria-busy'),
                    contentHidden: document.querySelector('[data-dwh-content]').hidden, value: document.querySelector('[data-dwh-metric=netSales]').textContent,
                    statusHidden: document.querySelector('[data-dwh-status]').hidden, computed: getComputedStyle(document.querySelector('[data-dwh-content]')).display }));
                await page.screenshot({ path: path.join(info.output, `manager-${width}-viewport.png`), animations: 'disabled' });
                await page.screenshot({ path: path.join(info.output, `manager-${width}.png`), animations: 'disabled' });
                const after = await page.evaluate(() => ({ visibility: document.visibilityState, busy: document.querySelector('[data-dwh-business]').getAttribute('aria-busy'),
                    contentHidden: document.querySelector('[data-dwh-content]').hidden, statusHidden: document.querySelector('[data-dwh-status]').hidden, events: window.__desktopHomeVisibility }));
                fs.writeFileSync(path.join(info.output, `manager-${width}-state.json`), JSON.stringify({ before: state, after }));
                proof.viewports.push({ width, overflow: false });
            }
            await page.setViewportSize({ width: 1440, height: 960 });
            assert.equal(await page.locator('#desktopHomeSearch, #desktopHomeCatalog, [data-dwh-group-filter]').count(), 0);
            await page.locator('#desktopHomePeriod').selectOption('week');
            await page.waitForFunction(() => document.querySelector('[data-dwh-business]')?.getAttribute('aria-busy') === 'false'
                && !document.querySelector('[data-dwh-content]')?.hidden);
            assert.equal(await page.locator('[data-dwh-metric=netSales]').innerText(), '350 ₫');
            await page.locator('#desktopHomePeriod').selectOption('month');
            await page.waitForFunction(() => document.querySelector('[data-dwh-business]')?.getAttribute('aria-busy') === 'false'
                && !document.querySelector('[data-dwh-content]')?.hidden);
            let failure = 503;
            await manager.context.route('**/admin/reports/overview/data?**', route => route.fulfill({ status: failure, contentType: 'application/json', body: '{}' }));
            await page.locator('[data-dwh-refresh]').click();
            await page.waitForFunction(() => document.querySelector('[data-dwh-status]')?.dataset.kind === 'error');
            assert.equal(await page.locator('[data-dwh-content]').isVisible(), false);
            assert.equal(await page.locator('[data-dwh-metric=netSales]').innerText(), '—');
            await manager.context.unroute('**/admin/reports/overview/data?**');
            await page.locator('[data-dwh-refresh]').click();
            await page.locator('[data-dwh-content]:visible').waitFor();
            await manager.context.route('**/admin/reports/overview/data?**', route => route.fulfill({ status: 403, contentType: 'application/json', body: '{}' }));
            await page.locator('[data-dwh-refresh]').click();
            await page.locator('[data-dwh-reload]:visible').waitFor();
            assert.equal(await page.locator('[data-dwh-metric=netSales]').innerText(), '—');
            assert.equal(await page.locator('[data-dwh-chart] svg').count(), 0);
            assert.equal(await page.locator('#desktopHomePeriod').isDisabled(), true);
            await manager.context.unroute('**/admin/reports/overview/data?**');
            const staff = await login('home_staff'); let staffSalesReads = 0, shiftReads = 0;
            staff.page.on('request', request => {
                const url = new URL(request.url());
                if (url.pathname === '/admin/reports/overview/data') staffSalesReads++;
                if (/\/admin\/pos\/shift\/(current|summary)$/.test(url.pathname)) { shiftReads++; assert.equal(url.search, ''); }
            });
            await staff.page.goto(info.baseUrl + '/admin');
            await staff.page.locator('[data-employee-statistics]:visible').waitFor(); await settle(staff.page);
            const employee = staff.page.locator('[data-employee-home]');
            assert.equal(await employee.locator('[data-dwh-business], [data-dwh-item], #desktopHomeSearch, #desktopHomeCatalog').count(), 0);
            assert.equal(staffSalesReads, 0);
            assert.deepEqual(await employee.locator('[data-employee-action]').evaluateAll(nodes => nodes.map(n => n.dataset.employeeAction)), ['shift', 'sales', 'orders', 'receiving']);
            assert.match(await employee.locator('[data-employee-action=receiving]').getAttribute('href'), /^\/admin\/warehouse-receiving\?view=staff$/);
            assert.match(await employee.locator('[data-employee-action=shift]').getAttribute('href'), /\?from=home$/);
            const staffSidebar = await staff.page.locator('#layout-menu [data-nav-destination]').evaluateAll(nodes => nodes.map(n => ({ id: n.closest('[data-nav-menu-id]').dataset.navMenuId, href: n.getAttribute('href') })));
            for (const link of await employee.locator('[data-employee-action]').evaluateAll(nodes => nodes.map(n => ({ id: n.dataset.menuId, href: n.getAttribute('href') })))) assert.ok(staffSidebar.some(n => n.id === link.id && n.href === link.href));
            assert.equal(await employee.locator('[data-employee-code]').innerText(), 'STAFF-OWN-01');
            assert.match(await employee.locator('[data-employee-terminal]').innerText(), /Quầy nhân viên/);
            const expected = { completedSalesTotal: '250 ₫', closingCashExpected: '1.170 ₫', refundTotal: '30 ₫', openingCash: '1.000 ₫',
                cashSalesTotal: '180 ₫', nonCashSalesTotal: '120 ₫', cashRefundTotal: '20 ₫', nonCashRefundTotal: '10 ₫', cashInTotal: '50 ₫', cashOutTotal: '40 ₫',
                completedOrders: '1', totalOrders: '6', draftOrders: '1', heldOrders: '1', cancelledOrders: '1', refundedOrders: '1', refundCount: '1', voidCount: '1' };
            for (const [key, value] of Object.entries(expected)) for (const node of await employee.locator(`[data-employee-metric=${key}]`).all()) assert.equal(await node.innerText(), value, key);
            assert.equal(await employee.locator('[data-employee-orders] tr').count(), 6);
            assert.equal(await employee.locator('[data-employee-transactions] li').count(), 2);
            assert.doesNotMatch(await employee.innerText(), /GT thuộc tính|Sản phẩm|Tra cứu tồn kho|Thẻ kho|PEER-PRIVATE|880\.000|HOME_OTHER_STORE_CANARY/);
            const staffWidths = [];
            for (const width of [1024, 1366, 1440, 1920]) {
                await staff.page.setViewportSize({ width, height: 960 }); await settle(staff.page);
                assert.equal(await staff.page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
                await staff.page.screenshot({ path: path.join(info.output, `staff-${width}.png`), animations: 'disabled' });
                staffWidths.push(width);
            }
            await staff.page.setViewportSize({ width: 1440, height: 960 });
            await employee.locator('[data-employee-statistics]').scrollIntoViewIfNeeded(); await settle(staff.page);
            await staff.page.screenshot({ path: path.join(info.output, 'staff-statistics.png'), animations: 'disabled' });
            await staff.page.evaluate(() => scrollTo(0,0));
            const employeeContrast = await employee.locator('.dwh-employee-actions small,.dwh-employee-meta dt,.dwh-employee-money dt,.dwh-employee-order-counts span').evaluateAll(nodes => {
                const light = color => (color.match(/[\d.]+/g) || []).slice(0, 3).map(Number).map(v => { v /= 255; return v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; }).reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
                return nodes.map(n => { let parent = n, background = 'rgb(255,255,255)'; while (parent) { const c = getComputedStyle(parent).backgroundColor; if (c !== 'transparent' && !/rgba\([^)]*,\s*0\)$/.test(c)) { background = c; break; } parent = parent.parentElement; } const a=light(getComputedStyle(n).color),b=light(background); return (Math.max(a,b)+.05)/(Math.min(a,b)+.05); });
            });
            assert.ok(employeeContrast.length > 10 && employeeContrast.every(value => value >= 4.5));
            const rawApi = async (session, endpoint) => session.context.request.get(session.base.replace('shop.phase6.test','127.0.0.1') + endpoint, { headers: { Host: 'shop.phase6.test', Cookie: (await session.context.cookies()).map(c => `${c.name}=${c.value}`).join('; '), Accept: 'application/json' } });
            const api = await rawApi(staff, '/admin/reports/overview/data'); assert.equal(api.status(), 403);
            await staff.context.route('**/admin/pos/shift/summary', route => route.fulfill({ status: 503, contentType: 'application/json', body: '{}' }));
            await employee.locator('[data-employee-refresh]').click();
            await employee.locator('[data-employee-state][data-kind=error]').waitFor();
            assert.equal(await employee.locator('[data-employee-statistics]').isVisible(), false);
            assert.equal(await employee.locator('[data-employee-metric=completedSalesTotal]').innerText(), '—');
            await staff.context.unroute('**/admin/pos/shift/summary');
            await employee.locator('[data-employee-refresh]').click(); await employee.locator('[data-employee-statistics]:visible').waitFor();
            await staff.context.route('**/admin/pos/shift/summary', async route => { const response=await rawApi(staff, '/admin/pos/shift/summary'); const data=await response.json(); data.openedByUserId+=1000; return route.fulfill({ response, json:data }); });
            await employee.locator('[data-employee-refresh]').click(); await employee.locator('[data-employee-state][data-kind=error]').waitFor();
            assert.equal(await employee.locator('[data-employee-overview]').isVisible(), false);
            await staff.context.unroute('**/admin/pos/shift/summary');
            await employee.locator('[data-employee-refresh]').click(); await employee.locator('[data-employee-statistics]:visible').waitFor();
            await staff.context.route('**/admin/pos/shift/current', route => route.fulfill({ status: 403, contentType: 'application/json', body: '{}' }));
            await employee.locator('[data-employee-refresh]').click(); await employee.locator('[data-employee-reload]:visible').waitFor();
            assert.equal(await employee.locator('[data-employee-refresh]').isDisabled(), true);
            assert.equal(await employee.locator('[data-employee-orders] tr').count(), 0);
            assert.equal(await employee.locator('[data-employee-code]').innerText(), '—');
            await staff.context.unroute('**/admin/pos/shift/current');
            const empty = await login('home_empty');
            await empty.page.goto(info.baseUrl + '/admin'); await empty.page.locator('[data-employee-state][data-kind=empty]').waitFor();
            assert.match(await empty.page.locator('[data-employee-state]').innerText(), /chưa mở ca/);
            assert.equal(await empty.page.locator('[data-employee-statistics]').isVisible(), false);
            info.accounts.home_staff_other = { ...info.accounts.home_staff, terminalId: info.accounts.home_peer.terminalId };
            const wrong = await login('home_staff_other');
            await wrong.page.goto(info.baseUrl + '/admin'); await wrong.page.locator('[data-employee-state][data-kind=empty]').waitFor();
            assert.doesNotMatch(await wrong.page.locator('[data-employee-home]').innerText(), /PEER-PRIVATE|880\.000/);
            assert.equal(await wrong.page.locator('[data-employee-statistics]').isVisible(), false);
            const direct = await rawApi(wrong, '/admin/pos/shift/summary'); assert.equal(direct.status(), 409);
            const limited = await login('home_limited'); let limitedReads=0;
            limited.page.on('request', request => { if (/\/admin\/pos\/shift\/(current|summary)$/.test(new URL(request.url()).pathname)) limitedReads++; });
            await limited.page.goto(info.baseUrl + '/admin'); await settle(limited.page);
            assert.equal(limitedReads, 0);
            assert.match(await limited.page.locator('[data-employee-state]').innerText(), /Chưa có quyền/);
            assert.deepEqual(await limited.page.locator('[data-employee-action]').evaluateAll(nodes=>nodes.map(n=>n.dataset.employeeAction)), ['sales','orders']);
            assert.equal((await rawApi(limited, '/admin/pos/shift/summary')).status(), 403);
            const staffPhone = await login('home_staff',390); let phoneShiftReads=0, phoneReportReads=0;
            await staffPhone.page.goto('about:blank');
            staffPhone.page.on('request', request => { const p=new URL(request.url()).pathname; if (/\/admin\/pos\/shift\/(current|summary)$/.test(p)) phoneShiftReads++; if(p==='/admin/reports/overview/data')phoneReportReads++; });
            await staffPhone.page.goto(info.baseUrl+'/admin');
            await staffPhone.page.locator('[data-employee-statistics]:visible').waitFor(); await settle(staffPhone.page);
            assert.equal(await staffPhone.page.locator('[data-employee-home]').count(),1);
            assert.equal(await staffPhone.page.locator('[data-mobile-home]').count(),0);
            assert.equal(phoneShiftReads,2); assert.equal(phoneReportReads,0);
            const mobileRoot=staffPhone.page.locator('[data-employee-home]');
            const dock=staffPhone.page.locator('[data-mobile-pins]');
            await staffPhone.page.waitForFunction(()=>document.querySelector('[data-mobile-pins]')?.dataset.pinsReady==='true');
            const mobileDock=await dock.evaluate(node=>[...node.querySelectorAll('a,button')].map(n=>({id:n.dataset.mobilePinId||null,href:n.getAttribute('href'),label:n.textContent.trim(),current:n.getAttribute('aria-current')})));
            assert.deepEqual(mobileDock,JSON.parse(fs.readFileSync(path.join(info.output,'baseline-employee-dock.json'),'utf8')));
            await dock.screenshot({path:path.join(info.output,'candidate-employee-dock.png'),animations:'disabled'});
            const dockBefore=PNG.sync.read(fs.readFileSync(path.join(info.output,'baseline-employee-dock.png'))),dockAfter=PNG.sync.read(fs.readFileSync(path.join(info.output,'candidate-employee-dock.png')));
            assert.equal(dockBefore.width,dockAfter.width);assert.equal(dockBefore.height,dockAfter.height);
            let dockChanged=0;for(let i=0;i<dockBefore.data.length;i+=4)if([0,1,2,3].some(c=>dockBefore.data[i+c]!==dockAfter.data[i+c]))dockChanged++;
            assert.ok(dockChanged<=Math.floor(dockBefore.width*dockBefore.height*.0001),`Dock changed ${dockChanged} pixels`);
            assert.equal(await mobileRoot.locator('[data-employee-code]').innerText(),'STAFF-OWN-01');
            for(const[key,value]of Object.entries(expected))for(const node of await mobileRoot.locator(`[data-employee-metric=${key}]`).all())assert.equal(await node.innerText(),value,key);
            assert.deepEqual(await mobileRoot.locator('[data-employee-action]').evaluateAll(nodes=>nodes.map(n=>n.dataset.employeeAction)),['shift','sales','orders','receiving']);
            assert.equal(await mobileRoot.locator('[data-employee-action=receiving]').getAttribute('href'),'/admin/warehouse-receiving?view=staff');
            const phoneWidths=[];
            for(const width of [320,360,390,430,767]){
                await staffPhone.page.setViewportSize({width,height:844}); await settle(staffPhone.page);
                assert.equal(await staffPhone.page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false,`Overflow ${width}`);
                assert.equal(await dock.isVisible(),true);
                const rect=await dock.boundingBox();assert.ok(Math.abs(rect.y+rect.height-844)<2);
                for(const action of await mobileRoot.locator('[data-employee-action]').all()){const box=await action.boundingBox();assert.ok(box.height>=44&&box.width>=44);}
                const refreshBox=await mobileRoot.locator('[data-employee-refresh]').boundingBox();assert.ok(refreshBox.height>=44&&refreshBox.width>=44);
                await staffPhone.page.screenshot({path:path.join(info.output,`employee-phone-${width}.png`),animations:'disabled'});
                phoneWidths.push(width);
            }
            assert.equal(phoneShiftReads,2,'Changing screen width must not duplicate API reads');
            await staffPhone.page.setViewportSize({width:390,height:844});
            await mobileRoot.locator('[data-employee-statistics]').scrollIntoViewIfNeeded();await settle(staffPhone.page);
            await staffPhone.page.screenshot({path:path.join(info.output,'employee-phone-statistics.png'),animations:'disabled'});
            const recent=mobileRoot.locator('[data-employee-transactions]');await recent.scrollIntoViewIfNeeded();
            await staffPhone.page.evaluate(()=>scrollTo(0,document.documentElement.scrollHeight)); await settle(staffPhone.page);
            const lastBox=await recent.boundingBox(),dockBox=await dock.boundingBox();assert.ok(lastBox.y+lastBox.height<=dockBox.y,'Dock must not cover the final transaction');
            await staffPhone.page.screenshot({path:path.join(info.output,'employee-phone-recent.png'),animations:'disabled'});
            await staffPhone.page.evaluate(()=>scrollTo(0,0));
            await dock.locator('[data-pins-menu]').click();
            await staffPhone.page.waitForFunction(()=>document.documentElement.classList.contains('layout-menu-expanded'));
            await staffPhone.page.locator('[data-nav-close]').click();
            await staffPhone.page.waitForFunction(()=>!document.documentElement.classList.contains('layout-menu-expanded'));
            await staffPhone.page.setViewportSize({width:1440,height:960});await staffPhone.page.evaluate(()=>scrollTo(0,0));await settle(staffPhone.page);
            assert.equal(phoneShiftReads,2,'There must be one data owner across phone/desktop');
            await staffPhone.page.mouse.move(0,0);await staffPhone.page.evaluate(()=>document.activeElement?.blur());
            await staffPhone.page.screenshot({path:path.join(info.output,'candidate-employee-desktop.png'),animations:'disabled',mask:[staffPhone.page.locator('[data-employee-statistics] > .dwh-section-heading')]});
            const desktopBefore=PNG.sync.read(fs.readFileSync(path.join(info.output,'baseline-employee-desktop.png'))),desktopAfter=PNG.sync.read(fs.readFileSync(path.join(info.output,'candidate-employee-desktop.png')));
            assert.equal(desktopBefore.width,desktopAfter.width);assert.equal(desktopBefore.height,desktopAfter.height);
            let desktopChanged=0;for(let i=0;i<desktopBefore.data.length;i+=4)if([0,1,2,3].some(c=>desktopBefore.data[i+c]!==desktopAfter.data[i+c]))desktopChanged++;
            const desktopPixelBudget=Math.floor(desktopBefore.width*desktopBefore.height*.0002);
            assert.ok(desktopChanged<=desktopPixelBudget,`Desktop changed ${desktopChanged} pixels`);
            await staffPhone.page.setViewportSize({width:390,height:844});
            await mobileRoot.locator('[data-employee-refresh]').click();await mobileRoot.locator('[data-employee-statistics]:visible').waitFor();
            await staffPhone.context.route('**/admin/pos/shift/summary',route=>route.fulfill({status:503,contentType:'application/json',body:'{}'}));
            await mobileRoot.locator('[data-employee-refresh]').click();await mobileRoot.locator('[data-employee-state][data-kind=error]').waitFor();
            assert.equal(await mobileRoot.locator('[data-employee-statistics]').isVisible(),false);assert.equal(await mobileRoot.locator('[data-employee-metric=completedSalesTotal]').innerText(),'—');
            await staffPhone.context.unroute('**/admin/pos/shift/summary');await mobileRoot.locator('[data-employee-refresh]').click();await mobileRoot.locator('[data-employee-statistics]:visible').waitFor();
            await staffPhone.context.route('**/admin/pos/shift/summary',async route=>{const response=await rawApi(staffPhone,'/admin/pos/shift/summary');const data=await response.json();data.openedByUserId+=1000;return route.fulfill({response,json:data});});
            await mobileRoot.locator('[data-employee-refresh]').click();await mobileRoot.locator('[data-employee-state][data-kind=error]').waitFor();assert.equal(await mobileRoot.locator('[data-employee-overview]').isVisible(),false);
            await staffPhone.context.unroute('**/admin/pos/shift/summary');await mobileRoot.locator('[data-employee-refresh]').click();await mobileRoot.locator('[data-employee-statistics]:visible').waitFor();
            await staffPhone.context.route('**/admin/pos/shift/current',route=>route.fulfill({status:403,contentType:'application/json',body:'{}'}));
            await mobileRoot.locator('[data-employee-refresh]').click();await mobileRoot.locator('[data-employee-reload]:visible').waitFor();
            assert.equal(await mobileRoot.locator('[data-employee-refresh]').isDisabled(),true);assert.equal(await mobileRoot.locator('[data-employee-code]').innerText(),'—');assert.equal(await dock.isVisible(),true);
            for(const[name,pattern]of [['home_empty',/chưa mở ca/],['home_staff_other',/Ca trên quầy đã thay đổi/],['home_limited',/Chưa có quyền/]]){
                const session=await login(name,390);await session.page.goto(info.baseUrl+'/admin');await session.page.locator('[data-employee-state]:visible').waitFor();
                await session.page.waitForFunction(()=>document.querySelector('[data-employee-shift]').getAttribute('aria-busy')==='false');
                assert.match(await session.page.locator('[data-employee-state]').innerText(),pattern);assert.equal(await session.page.locator('[data-employee-statistics]').isVisible(),false);
                assert.doesNotMatch(await session.page.locator('[data-employee-home]').innerText(),/PEER-PRIVATE|880\.000/);assert.equal(await session.page.locator('[data-mobile-pins]').isVisible(),true);
            }
            proof.employeeMobile={passed:true,widths:phoneWidths,dockUnchanged:true,dockChangedPixels:dockChanged,dockMenuWorks:true,dockDoesNotCoverContent:true,actionsUseConfiguredMenus:true,
                ownShiftStats:true,noReportReads:phoneReportReads===0,noDuplicateReadsOnResize:true,desktopChangedPixels:desktopChanged,desktopPixelBudget,desktopMaximumChangedFraction:.0002,changedOwnerRejected:true,
                failuresClearData:true,missingPermissionsAndNoShiftHandled:true,existingChromeWindowsPreserved:true};

            proof.employee={ passed:true, staffWidths, employeeContrast, ownShiftOnly:true, noGenericCatalog:true, fourConfiguredActions:true,
                warehouseReceiving:true, noReportReads:true, terminalOwnershipEnforced:true, noShiftHandled:true, missingPermissionsHandled:true,
                staleDataCleared:true, changedOwnerRejected:true, phoneShiftReads, shiftReads };

            const other = await login('home_foreign', 1440, true);
            await other.page.goto(other.base + '/admin');
            await other.page.locator('[data-dwh-content]:visible').waitFor();
            assert.equal(await other.page.locator('[data-dwh-metric=netSales]').innerText(), '900.000 ₫');
            assert.match(await other.page.locator('[data-desktop-home]').innerText(), /HOME_OTHER_STORE_CANARY/);
            assert.equal(await other.page.locator('[data-dwh-quick]').count(), 1);
            const denied = await login('home_denied');
            const deniedResponse = await denied.page.goto(info.baseUrl + '/admin');
            assert.ok([302, 403].includes(deniedResponse.status()) || /access-?denied/i.test(denied.page.url()));
            assert.equal(await denied.page.locator('[data-desktop-home]').count(), 0);
            proof.staffSalesReads = staffSalesReads; proof.apiStillForbidsStaff = true; proof.foreignTenantIsolated = true;
            proof.failuresClearPrivateMetrics = true; proof.configuredNavigationMatchesSidebar = true;
        }
        assert.deepEqual(errors, []); proof.pageErrors = errors; proof.passed = true;
        fs.writeFileSync(path.join(info.output, info.phase + '-browser.json'), JSON.stringify(proof, null, 2));
        console.log(JSON.stringify(proof));
    } finally { for (const context of contexts) await context.close(); await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
