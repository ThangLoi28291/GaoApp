'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let data = ''; process.stdin.on('data', chunk => data += chunk); process.stdin.on('end', () => resolve(data));
    }));
    const output = path.resolve(process.env.GAO_POS_DETAIL_OUTPUT || 'TestResults/mobile-ui-04c-20261025/browser');
    fs.mkdirSync(output, { recursive: true });
    // This process owns its headless Chrome/profile. Never attaches to an existing user browser.
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
    const page = await context.newPage(), errors = [], cspErrors = [], commands = [], checks = [];
    let completed = false;
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error' && /Content Security Policy/i.test(message.text())) cspErrors.push(message.text()); });
    page.on('request', request => {
        if (request.method() !== 'GET' && /\/admin\/(pos|pos-shift)\//.test(new URL(request.url()).pathname)) commands.push(request.url());
    });
    async function login(user, password, targetPage = page) {
        await targetPage.goto(info.baseUrl + '/admin/account/login');
        await targetPage.locator('[name=UserName]').fill(user); await targetPage.locator('[name=Password]').fill(password);
        if (await targetPage.locator('select[name=SelectedTerminalId]').count())
            await targetPage.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([targetPage.waitForURL(url => !url.pathname.endsWith('/login')),
            targetPage.locator('#loginForm button[type=submit]').click()]);
    }
    async function load(id) {
        const response = await page.goto(info.baseUrl + '/admin/pos/order-detail/' + id);
        assert.equal(response.status(), 200);
        await page.locator('[data-mobile-order-detail]').waitFor();
        await page.evaluate(() => document.fonts.ready);
    }
    async function shot(name) { await page.screenshot({ path: path.join(output, name + '.png') }); }
    async function overflow() {
        const result = await page.evaluate(() => ({ viewport: innerWidth, document: document.documentElement.scrollWidth,
            root: document.querySelector('[data-mobile-order-detail]').getBoundingClientRect().right }));
        assert.ok(result.document <= result.viewport + 1 && result.root <= result.viewport + 1, JSON.stringify(result));
    }
    async function closeModal(id) {
        await page.locator('#' + id + ' .modal-header [data-bs-dismiss="modal"]').click();
        await page.locator('#' + id).waitFor({ state: 'hidden' });
    }
    try {
        await login(info.user, info.password); await load(info.orderId);
        assert.match(await page.locator('[data-order-lines]').innerText(), /Sữa dinh dưỡng/);
        const originalAmounts = await page.locator('.ord-stat-value').allTextContents();
        const canonical = html => html.replace(/(<input name="__RequestVerificationToken"[^>]*?) style=""/g, '$1');
        const baseline = (await page.evaluate(() => {
            const selectors = ['[data-mobile-order-detail]', '.order-head', '[data-order-utilities]'];
            window.__detailOriginal = selectors.flatMap(selector => [...document.querySelectorAll(selector + ', ' + selector + ' *')]);
            return selectors.map(selector => document.querySelector(selector).outerHTML);
        })).map(canonical);
        await shot('detail-desktop-1440');
        const sizes = [[1600,1000],[1440,900],[1366,768],[1024,768],[768,1024],[767,900],[430,932],[390,844],[360,800],[320,740]];
        for (const [width,height] of sizes) {
            await page.setViewportSize({width,height});
            if (width < 768) {
                await page.waitForFunction(() => document.querySelector('[data-mobile-order-detail]').dataset.mobileOrderDetailMode === 'phone');
                assert.equal(await page.locator('.od-mobile-tabs [role=tab]').count(), 3);
                assert.equal(await page.locator('.od-mobile-panel:not([hidden])').count(), 1);
                assert.equal(await page.locator('.od-mobile-dock a').count(), 2);
                assert.match(await page.locator('.od-mobile-hero').innerText(), /180[.,]000/);
                assert.match(await page.locator('.od-mobile-hero').innerText(), /Nguyễn Minh Anh/);
                for (let i = 0; i < 3; i++) {
                    await page.locator('.od-mobile-tab').nth(i).click();
                    assert.equal(await page.locator('.od-mobile-panel:not([hidden])').count(), 1);
                    assert.equal(await page.locator('.od-mobile-tab').nth(i).getAttribute('aria-selected'), 'true');
                    await overflow();
                }
                await page.locator('.od-mobile-tab').first().click(); await page.evaluate(() => scrollTo(0,0));
                const rect = await page.locator('.od-mobile-tabs').boundingBox();
                const dock = await page.locator('.od-mobile-dock').boundingBox();
                assert.ok(rect.y + rect.height < dock.y, 'Summary and tabs visible above fixed actions: ' + width);
                await shot('detail-phone-' + width);
            } else {
                assert.equal(await page.locator('.od-mobile-shell').count(), 0);
                await overflow();
            }
            checks.push('viewport-' + width + 'x' + height);
        }
        await page.setViewportSize({width:1440,height:900});
        await page.waitForFunction(() => !document.querySelector('.od-mobile-shell'));
        // Existing libraries may leave an inert style="" on the hidden antiforgery input after CSS measurement.
        // Only that empty attribute is normalized; its node, token and every other attribute/content must survive.
        assert.deepEqual((await page.evaluate(() => ['[data-mobile-order-detail]', '.order-head', '[data-order-utilities]']
            .map(selector => document.querySelector(selector).outerHTML))).map(canonical), baseline, 'Desktop DOM restored exactly');
        assert.equal(await page.evaluate(() => window.__detailOriginal.every(node => node.isConnected)), true);
        assert.deepEqual(await page.locator('.ord-stat-value').allTextContents(), originalAmounts);
        checks.push('exact-desktop-DOM-and-original-nodes-restored');
        // Compare desktop pixels with all new behavior/styles removed and the no-box wrapper unwrapped.
        const currentDesktop = await page.screenshot();
        await page.locator('link[href*="pos-order-detail.mobile.css"]').evaluate(node => node.remove());
        await page.locator('[data-order-utilities]').evaluate(node => node.replaceWith(...node.childNodes));
        const originalDesktop = await page.screenshot();
        fs.writeFileSync(path.join(output, 'detail-desktop-baseline.png'), originalDesktop);
        assert.ok(currentDesktop.equals(originalDesktop), 'Desktop pixels must be unchanged without the new mobile stylesheet');
        checks.push('desktop-pixel-equality');
        await load(info.orderId); await page.setViewportSize({width:390,height:844});
        await page.locator('.od-mobile-tab').nth(1).click();
        assert.match(await page.locator('#od-mobile-panel-1').innerText(), /Tiền cọc đã dùng/i);
        assert.match(await page.locator('#od-mobile-panel-1').innerText(), /Voucher đã dùng/i);
        assert.match(await page.locator('#od-mobile-panel-1').innerText(), /Giảm cấp đơn/);
        await page.evaluate(() => scrollTo(0,0)); await shot('detail-payment-390');
        await page.locator('.od-mobile-tab').nth(0).focus();
        await page.keyboard.press('ArrowRight');
        assert.equal(await page.locator('.od-mobile-tab').nth(1).getAttribute('aria-selected'), 'true');
        await page.keyboard.press('End');
        assert.equal(await page.locator('.od-mobile-tab').nth(2).getAttribute('aria-selected'), 'true');
        checks.push('tabs-ARIA-keyboard-and-existing-money-data');
        await page.locator('.od-mobile-tab').nth(1).click();
        assert.equal(await page.locator('#od-mobile-panel-1 [data-order-utility="bank"] a').getAttribute('data-acb-order-id'), String(info.orderId));
        assert.match(await page.locator('#od-mobile-panel-1 [data-order-utility="payment-request"] a').getAttribute('href'), new RegExp('orderId=' + info.orderId));
        await page.locator('#od-mobile-panel-1 [data-csp-action="payment-history"]').click();
        await page.locator('#paymentHistoryModal.show').waitFor(); await overflow();
        assert.equal(await page.locator('.od-mobile-dock').isVisible(), false);
        assert.match(await page.locator('#paymentHistoryModal').innerText(), /80[.,]000/);
        await shot('detail-payment-history-390'); await closeModal('paymentHistoryModal');
        await page.locator('.od-mobile-tab').nth(2).click();
        await page.locator('.od-mobile-history-actions [data-csp-action="return-history"]').click();
        await page.locator('#returnHistoryModal.show').waitFor();
        await page.waitForFunction(() => document.querySelector('#returnHistoryModalBody').getAttribute('aria-busy') === 'false');
        assert.equal(await page.locator('.od-mobile-dock').isVisible(), false);
        await overflow();
        await shot('detail-return-history-390'); await closeModal('returnHistoryModal');
        await page.locator('.od-mobile-tab').nth(0).click();
        await page.locator('.od-mobile-after-sale [data-csp-action="return-refund"]').click();
        await page.locator('#returnRefundModal.show').waitFor();
        await page.waitForFunction(() => document.querySelector('#returnRefundModalBody').getAttribute('aria-busy') === 'false');
        assert.equal(await page.locator('.od-mobile-dock').isVisible(), false);
        await page.waitForFunction(() => document.querySelector('[data-return-customer-order]')?.textContent.includes('Nguyễn Minh Anh'));
        await overflow();
        await shot('detail-return-390');
        // Changing presentation must preserve a partially entered form and restore legacy desktop display.
        await page.locator('#returnReason').fill('Kiểm tra giữ nguyên lý do khi đổi màn hình');
        await page.setViewportSize({width:1440,height:900});
        await page.waitForFunction(() => !document.querySelector('.od-mobile-shell'));
        assert.equal(await page.locator('[data-return-customer-order]').innerText(), 'Khách lẻ');
        assert.equal(await page.locator('#returnReason').inputValue(), 'Kiểm tra giữ nguyên lý do khi đổi màn hình');
        await page.setViewportSize({width:390,height:844});
        await page.waitForFunction(() => document.querySelector('[data-return-customer-order]')?.textContent.includes('Nguyễn Minh Anh'));
        assert.equal(await page.locator('#returnReason').inputValue(), 'Kiểm tra giữ nguyên lý do khi đổi màn hình');
        await closeModal('returnRefundModal');
        checks.push('contextual-payment-links-return-history-and-customer-form-resize');
        const actions = page.locator('.od-mobile-special-actions');
        await actions.locator('summary').click();
        await actions.locator('[data-csp-action="order-void"]').click();
        await page.locator('#orderActionModal.show').waitFor();
        assert.equal(await page.locator('.od-mobile-dock').isVisible(), false);
        assert.equal(await page.locator('.od-mobile-dock a').first().isVisible(), false);
        await shot('detail-action-390'); await closeModal('orderActionModal');
        checks.push('four-original-modals-open-and-close-without-submit');
        // Failure/late-response presentation uses the existing loader/error/abort owner.
        const eligibility = '**/admin/pos/returns/order/' + info.orderId + '/eligibility';
        await page.route(eligibility, route => route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({message:'Chưa thể tải điều kiện trả hàng.'})}));
        await page.locator('.od-mobile-tab').first().click();
        await page.locator('.od-mobile-after-sale [data-csp-action="return-refund"]').click();
        await page.waitForFunction(() => document.querySelector('#returnRefundModalBody').getAttribute('aria-busy') === 'false');
        assert.equal(await page.locator('#btnSubmitReturnRefund').isDisabled(), true);
        assert.match(await page.locator('#returnRefundModalBody').innerText(), /Chưa thể tải/);
        await shot('detail-return-error-390'); await closeModal('returnRefundModal');
        await page.unroute(eligibility);
        await page.route(eligibility, async route => { await new Promise(resolve=>setTimeout(resolve,500)); await route.continue().catch(()=>{}); });
        await page.locator('.od-mobile-after-sale [data-csp-action="return-refund"]').click();
        await page.locator('#returnRefundModal.show').waitFor(); await closeModal('returnRefundModal');
        await page.waitForTimeout(650);
        assert.equal(await page.locator('#returnRefundModal').isVisible(), false);
        await page.unroute(eligibility);
        checks.push('existing-error-disabled-submit-and-close-during-load');
        await load(info.emptyOrderId);
        assert.match(await page.locator('#od-mobile-panel-0').innerText(), /Chưa có sản phẩm/);
        assert.match(await page.locator('.od-mobile-hero').innerText(), /Khách lẻ/);
        assert.equal(await page.locator('[data-csp-action="order-void"]').count(), 0);
        await overflow(); await shot('detail-empty-390');
        checks.push('empty-draft-retail-customer-and-conditional-actions');
        await load(info.deliveryOrderId);
        assert.match(await page.locator('.od-mobile-hero').innerText(), /Giá trị dự kiến/i);
        assert.equal(await page.locator('[data-csp-action="return-refund"]').count(), 0);
        await page.locator('.od-mobile-disclosure > summary').filter({hasText:'Hồ sơ giao hàng liên kết'}).click();
        assert.equal(await page.locator('[data-order-delivery] a').first().getAttribute('href').then(href => /\/bill$/.test(href)), true);
        await page.locator('.od-mobile-tab').nth(1).click();
        assert.match(await page.locator('#od-mobile-panel-1').innerText(), /Chưa thu tiền/);
        assert.equal(await page.locator('.ord-payment-section').count(), 0);
        await overflow(); await page.evaluate(() => scrollTo(0,0)); await shot('detail-delivery-390');
        checks.push('real-delivery-source-prices-not-payment-and-linked-documents');
        const restricted = await browser.newContext({viewport:{width:390,height:844}});
        const restrictedPage = await restricted.newPage();
        await login(info.readOnlyUser, info.readOnlyPassword, restrictedPage);
        assert.equal((await restrictedPage.goto(info.baseUrl + '/admin/pos/order-detail/' + info.orderId)).status(), 200);
        assert.equal(await restrictedPage.locator('[data-order-legal]').count(), 0);
        await restricted.close(); checks.push('real-view-only-HKD-permission');
        for (const a of info.capabilityAccounts) {
            const roleContext = await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce'});
            const p = await roleContext.newPage();
            p.on('pageerror', error=>errors.push(a.Label+': '+error.message));
            await login(a.User,a.Password,p);
            assert.equal((await p.goto(info.baseUrl + '/admin/pos/order-detail/' + info.orderId)).status(),200);
            await p.locator('.od-mobile-shell').waitFor();
            const assertPhone = async () => {
                assert.equal(await p.locator('.od-mobile-after-sale [data-csp-action="return-refund"]').count(),Number(a.CanReturn),a.Label);
                assert.equal(await p.locator('.od-mobile-special-actions [data-csp-action="order-refund"]').count(),Number(a.CanReturn),a.Label);
                assert.equal(await p.locator('.od-mobile-special-actions [data-csp-action="order-void"]').count(),Number(a.CanVoid),a.Label);
                assert.equal(await p.locator('.od-mobile-dock .btn-ord-primary').count(),Number(a.CanReprint),a.Label);
                assert.equal(await p.locator('.od-mobile-payment-actions [data-order-utility="bank"]').count(),Number(a.CanViewBank),a.Label);
                assert.equal(await p.locator('.od-mobile-payment-actions [data-order-utility="payment-request"]').count(),Number(a.CanOpenPaymentRequests),a.Label);
                assert.equal(await p.locator('.od-mobile-history-actions [data-order-utility="restock"]').count(),Number(a.CanRestock),a.Label);
                assert.equal(await p.locator('.od-mobile-held').evaluate(node=>node.hidden&&node.inert),true);
                assert.ok(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
            };
            await assertPhone();
            if(a.Label==='view-only') await p.screenshot({path:path.join(output,'detail-view-only-390.png')});
            await p.setViewportSize({width:1440,height:900});
            await p.waitForFunction(()=>!document.querySelector('.od-mobile-shell'));
            assert.equal(await p.locator('.order-head [data-csp-action="order-void"]').isVisible(),true,'Legacy desktop controls preserved');
            assert.equal(await p.locator('.order-head .btn-ord-primary').isVisible(),true);
            await p.setViewportSize({width:320,height:740}); await p.locator('.od-mobile-shell').waitFor(); await assertPhone();
            await roleContext.close(); checks.push('phone-role-'+a.Label);
        }
        assert.deepEqual(errors, []); assert.deepEqual(cspErrors, []); assert.deepEqual(commands, []);
        completed = true;
        console.log('PASS UI-04c: ' + checks.length + ' checks; 10 viewports; contextual actions; seven permission combinations; desktop pixel equality; original handlers; no financial commands.');
    } finally {
        if (!completed) await shot('failure').catch(() => {});
        fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ completed, checks, errors, cspErrors, commands,
            isolatedHeadlessChrome: true, realRazorSql: true, createdAt:new Date().toISOString() },null,2));
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
