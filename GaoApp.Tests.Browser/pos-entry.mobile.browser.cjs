'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => resolve(s)); }));
    fs.mkdirSync(info.output, { recursive: true });
    const result = { completed: false, checks: [], viewports: [], errors: [], csp: [] };
    const pass = text => { result.checks.push(text); console.log('PASS ' + text); };
    const browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--disable-lcd-text'] });
    let page;
    async function session(account, size = { width: 1440, height: 1000 }) {
        const context = await browser.newContext({ viewport: size, hasTouch: true });
        const tab = await context.newPage(); tab.setDefaultTimeout(20000);
        tab.on('pageerror', e => result.errors.push(e.message));
        tab.on('console', m => { if (/violates.*Content Security Policy|Refused to (?:load|execute|apply)/i.test(m.text())) result.csp.push(m.text()); });
        await tab.route('**/uploads/products/scan*', r => r.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80"><rect width="80" height="80" rx="12" fill="#dceafa"/></svg>' }));
        await tab.goto(account.baseUrl + '/admin/account/login');
        await tab.locator('[name=UserName]').fill(account.user); await tab.locator('[name=Password]').fill(account.password);
        if (await tab.locator('[name=SelectedTerminalId]').count()) await tab.locator('[name=SelectedTerminalId]').selectOption(String(account.terminalId));
        await Promise.all([tab.waitForURL(u => !u.pathname.endsWith('/login')), tab.locator('#loginForm button[type=submit]').click()]);
        return { context, tab };
    }
    const state = () => page.evaluate(() => posApp.state.business.currentDraft);
    const headerSnapshot = () => page.locator('[data-pos-prime-header]').evaluate(el => {
        const snapshot=el.cloneNode(true);
        // Bootstrap correctly adds this state when its drawer has been opened/closed.
        snapshot.querySelector('#btnOpenPosPrimeAdminDrawer').removeAttribute('aria-expanded');
        return snapshot.outerHTML;
    });
    async function ready(route) {
        await page.goto(info.manager.baseUrl + route);
        await page.waitForFunction(() => window.posApp?.state.business.currentDraft && window.PosOffline?.status().ready);
        await page.evaluate(() => document.fonts.ready);
        await page.waitForFunction(() => document.getAnimations().every(a => a.playState !== 'running' || a.effect?.getTiming().iterations === Infinity));
        await idle();
        assert.equal((await state()).orderId, info.orderId, 'Use the existing server draft');
    }
    async function idle() {
        await page.waitForFunction(() => !window.posApp?.state.network.busyScopes.size);
        await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
    }
    async function qty(id, unit) { return (await state()).lines.filter(x => x.variantId === id && (!unit || x.sellingUnitName === unit)).reduce((s, x) => s + x.quantity, 0); }
    async function search() {
        await page.locator('#txtBarcode').fill('bia ken');
        await page.locator('#barcodeAutocomplete [data-autocomplete-kind=parent]').first().waitFor();
    }
    async function expectQty(id, unit, quantity) {
        await page.waitForFunction(({ id, unit, quantity }) => window.posApp.state.business.currentDraft.lines.filter(x => x.variantId === id && (!unit || x.sellingUnitName === unit)).reduce((s, x) => s + x.quantity, 0) === quantity, { id, unit, quantity });
        await idle(); assert.equal(await qty(id, unit), quantity);
    }
    async function desktopStyleProof(name) {
        // Disabling an inactive stylesheet repaints Chrome text with different antialiasing.
        // Compare every computed property and element box; retain both rasters for visual review.
        const comparison = await page.evaluate(() => {
            const snapshot = () => Array.from(document.querySelectorAll('#posShell, #posShell *')).map(el => {
                const style=getComputedStyle(el),box=el.getBoundingClientRect();
                return {tag:el.tagName,id:el.id,css:Array.from(style).map(p=>[p,style.getPropertyValue(p)]),box:[box.x,box.y,box.width,box.height]};
            });
            // One event-loop turn: background sync/status rendering cannot alter the comparison.
            const link=document.querySelector('link[href*="pos.entry.mobile.css"]'),before=snapshot();
            link.disabled=true;const after=snapshot();link.disabled=false;
            return {elementCount:before.length,allComputedPropertiesAndBoxesEqual:JSON.stringify(before)===JSON.stringify(after),
                differences:before.flatMap((item,i)=>JSON.stringify(item)===JSON.stringify(after[i])?[]:[{before:item,after:after[i]}]).slice(0,3)};
        });
        assert.ok(comparison.allComputedPropertiesAndBoxesEqual,JSON.stringify(comparison));
        await page.screenshot({path:path.join(info.output,name+'.png'),animations:'disabled',caret:'hide'});
        await page.locator('link[href*="pos.entry.mobile.css"]').evaluate(el => { el.disabled=true; });
        await page.screenshot({path:path.join(info.output,name+'-without-phone-css.png'),animations:'disabled',caret:'hide'});
        await page.locator('link[href*="pos.entry.mobile.css"]').evaluate(el => { el.disabled=false; });
        result.desktopStyleProofs ??= []; result.desktopStyleProofs.push({name,...comparison});
    }
    async function geometry(width, height) {
        await page.setViewportSize({ width, height }); await idle();
        const phone = width < 768;
        const g = await page.evaluate(() => {
            const rect = s => { const b = document.querySelector(s).getBoundingClientRect(); return { x: b.x, y: b.y, right: b.right, bottom: b.bottom, width: b.width, height: b.height }; };
            return { w: innerWidth, document: document.documentElement.scrollWidth, phone: document.querySelector('#posShell').hasAttribute('data-pos-phone'), details: document.querySelectorAll('.pos-entry-context').length,
                input: rect('#txtBarcode'), header: rect('[data-pos-prime-header], [data-pos-cockpit-header]'),
                cart: rect('.pos-cart-scroll'), inputCount: document.querySelectorAll('#txtBarcode').length, cartCount: document.querySelectorAll('#currentDraftBody').length,
                font: getComputedStyle(document.querySelector('#txtBarcode')).fontSize };
        });
        assert.equal(g.phone, phone); assert.equal(g.details, phone ? 1 : 0); assert.equal(g.inputCount, 1); assert.equal(g.cartCount, 1);
        assert.ok(g.document <= width + 1, JSON.stringify(g));
        if (phone) {
            assert.equal(g.font, '16px'); assert.ok(g.input.height >= 44); assert.ok(g.header.right <= width + 1); assert.ok(g.cart.height >= 20, JSON.stringify(g));
            const targets=await page.locator('[data-pos-prime-header] button, [data-pos-cockpit-header] button').evaluateAll(nodes=>nodes.filter(el=>el.checkVisibility() && !el.closest('details:not([open])') && el.getBoundingClientRect().width && el.getBoundingClientRect().height).map(el=>{
                const b=el.getBoundingClientRect();return {id:el.id,label:el.getAttribute('aria-label'),width:b.width,height:b.height,hit:el.contains(document.elementFromPoint(b.x+b.width/2,b.y+b.height/2))};
            }));
            for(const target of targets)assert.ok(target.width>=44 && target.height>=44 && target.hit,JSON.stringify(target));
        }
        assert.equal((await state()).orderId, info.orderId);
        result.viewports.push({ width, height, ...g });
    }
    try {
        const main = await session(info.manager); page = main.tab;
        await ready('/admin/pos');
        assert.equal(await qty(info.waterId), 2); assert.equal((await state()).lines.length, 1);
        const initialDraft = await state();
        const headerHtml = await headerSnapshot();
        await page.evaluate(() => { window.__entryInput = document.querySelector('#txtBarcode'); window.__entryContext = document.querySelector('.pos-prime-header__context'); window.__entryShortcuts = document.querySelector('[data-pos-shortcuts-toggle]'); });
        // Finish the original delayed scanner autofocus before comparing raster output.
        await page.locator('#txtBarcode').focus(); await page.locator('[data-pos-command-focus]').focus();
        await page.screenshot({animations:'disabled',caret:'hide'});
        await desktopStyleProof('desktop-1440');
        pass('Default Prime: existing draft and every 1440 desktop computed style/box preserved');
        const menus = await page.locator('#posPrimeAdminDrawer .menu-link').evaluateAll(nodes => nodes.map(n => [n.textContent.trim(), n.getAttribute('href')]));
        for (const [w, h] of [[320,568],[360,780],[390,844],[430,932],[600,900],[767,900],[768,1024],[1024,768],[1440,1000],[390,440]]) await geometry(w, h);
        assert.deepEqual(await state(), initialDraft, 'Presentation/viewport changes cannot mutate the draft');
        pass('10 viewports: no horizontal overflow, one input/cart and original draft unchanged');
        await page.setViewportSize({ width:390, height:844 }); await idle();
        const disclosure = page.locator('.pos-entry-context > summary');
        await disclosure.focus(); await page.keyboard.press('Enter');
        assert.equal(await page.locator('.pos-entry-context').getAttribute('open'), '');
        assert.match(await page.locator('.pos-entry-context').innerText(), /Kho bán lẻ trung tâm/);
        assert.match(await page.locator('.pos-entry-context').innerText(), /Quầy bán hàng số 01/);
        assert.equal(await page.evaluate(() => window.__entryContext === document.querySelector('.pos-entry-context .pos-prime-header__context') && window.__entryShortcuts === document.querySelector('.pos-entry-context [data-pos-shortcuts-toggle]') && window.__entryInput === document.querySelector('#txtBarcode')), true);
        await page.locator('[data-pos-shortcuts-toggle]').click(); await page.locator('#posCockpitShortcutPanel').waitFor();
        await page.locator('[data-pos-shortcuts-close]').click();
        await page.screenshot({ path: path.join(info.output, 'phone-390-context.png'), animations:'disabled' });
        await disclosure.click();
        pass('Native keyboard disclosure shows true server shift/warehouse/terminal and original shortcut control');
        await page.locator('#btnOpenPosPrimeAdminDrawer').click(); await page.locator('#posPrimeAdminDrawer.show').waitFor();
        assert.deepEqual(await page.locator('#posPrimeAdminDrawer .menu-link').evaluateAll(nodes => nodes.map(n => [n.textContent.trim(), n.getAttribute('href')])), menus);
        await page.locator('#posPrimeAdminDrawer [data-bs-dismiss=offcanvas]').first().click(); await page.locator('#posPrimeAdminDrawer').waitFor({ state:'hidden' });
        await page.locator('#btnOpenHeldOrders').click(); await page.locator('#heldOrdersModal.show').waitFor();
        await page.locator('#heldOrdersModal [data-bs-dismiss=modal]').first().click(); await page.locator('#heldOrdersModal').waitFor({ state:'hidden' });
        assert.deepEqual(await state(), initialDraft);
        pass('Original configured drawer and held-order modal open/close without changing cart');
        for (const prefix of ['@', '#', '4+']) {
            await page.locator('#posCockpitCommandHints [data-pos-command-prefix="' + prefix + '"]').tap();
            if (prefix === '4+') {
                assert.equal(await page.locator('#txtBarcode').inputValue(), '');
                assert.equal(await page.locator('#posQtyPrefixBadge').innerText(), 'SL: 4');
            } else assert.equal(await page.locator('#txtBarcode').inputValue(), prefix);
            assert.equal(await page.locator('#txtBarcode').evaluate(el => el === document.activeElement), true);
            await page.locator('#txtBarcode').fill('');
            await page.locator('#txtBarcode').press('Escape');
        }
        const toastClose=page.locator('#toast-container .toast-close-button');
        if(await toastClose.count()){await toastClose.first().click();await page.locator('#toast-container .toast').waitFor({state:'hidden'});}
        pass('Original customer/voucher/quantity command chips retain their handlers and keyboard focus');
        await search();
        await page.screenshot({ path:path.join(info.output,'phone-390-search.png'), animations:'disabled' });
        await page.locator('#txtBarcode').press('Enter'); await expectQty(info.variantId, 'Lon', 1);
        await search(); await page.locator('#barcodeAutocomplete [data-autocomplete-kind=parent]').first().tap(); await expectQty(info.variantId, 'Lon', 2);
        pass('Product name: keyboard Enter and touch each add exactly one to the same cart');
        await search(); await page.locator('#barcodeAutocomplete .pos-ac-expand-btn').first().tap();
        const child = page.locator('#barcodeAutocomplete [data-product-unit-conversion-id="' + info.packId + '"]');
        await child.waitFor(); assert.ok((await child.boundingBox()).height >= 44);
        await child.tap(); await expectQty(info.variantId, 'Lốc', 1);
        pass('Touch expand/select uses original pack conversion and separate pack line');
        let mutations = 0;
        await page.route('**/admin/pos/cart/current/scan', async route => { mutations++; await new Promise(r => setTimeout(r, 450)); await route.continue(); });
        await page.locator('#txtBarcode').fill('3+2099900000001'); await page.locator('#txtBarcode').press('Enter');
        await page.locator('#posScanFeedback[data-state=saving]').waitFor();
        await page.locator('#txtBarcode').press('Enter'); await expectQty(info.variantId, 'Lon', 5);
        await page.unroute('**/admin/pos/cart/current/scan'); assert.equal(mutations, 1);
        assert.match(await page.locator('#posScanMeta').innerText(), /Lon/);
        assert.equal(await page.locator('#txtBarcode').inputValue(), '');
        pass('Exact scanner + quantity prefix + repeated Enter while saving sends one acknowledged mutation');
        await page.locator('#posScanHistoryOpen').click(); await page.locator('#posScanHistory.show').waitFor();
        await page.keyboard.press('F2'); await page.keyboard.press('F3');
        assert.equal(await page.locator('.modal.show').count(), 0);
        await page.locator('#posScanHistory .btn-close').click(); await page.locator('#posScanHistory').waitFor({ state:'hidden' });
        pass('Original scan history retains Prime overlay and keyboard ownership');
        const afterActions = await state();
        for (const [w, h] of [[320,568],[390,440],[430,932]]) {
            await geometry(w,h);
            await page.screenshot({ path:path.join(info.output,`phone-${w}-${h}.png`),animations:'disabled' });
            if(h<=680){
                await page.locator('.pos-cart-scroll').scrollIntoViewIfNeeded(); await idle();
                const visible=await page.evaluate(()=>{
                    const cart=document.querySelector('.pos-cart-scroll').getBoundingClientRect(),dock=document.querySelector('#posPrimeCheckoutDock').getBoundingClientRect();
                    return {exposed:Math.min(cart.bottom,dock.y,innerHeight)-Math.max(cart.y,0),overlap:Math.min(cart.bottom,dock.bottom)-Math.max(cart.y,dock.y)};
                });
                assert.ok(visible.exposed>=44,'Cart must have at least one reachable touch row: '+JSON.stringify(visible));
                assert.ok(visible.overlap<=0,'Dock must follow the cart without covering its rows: '+JSON.stringify(visible));
                result.shortViewportScroll??=[];result.shortViewportScroll.push({width:w,height:h,exposedCartPx:visible.exposed,dockOverlapsCart:false});
                await page.screenshot({path:path.join(info.output,`phone-${w}-${h}-cart-visible.png`),animations:'disabled'});
                await page.locator('#posPrimeCheckoutDock').scrollIntoViewIfNeeded();
                const paymentReachable=await page.locator('#posPrimeDockActions #btnOpenPayment').evaluate(el=>{const b=el.getBoundingClientRect();return b.height>=44 && b.y>=0 && b.bottom<=innerHeight && el.contains(document.elementFromPoint(b.x+b.width/2,b.y+b.height/2));});
                assert.ok(paymentReachable,'Original payment button must remain reachable by scrolling');
                await page.locator('[data-pos-prime-header]').scrollIntoViewIfNeeded(); await idle();
            }
        }
        await page.setViewportSize({ width:1440,height:1000 }); await idle();
        assert.equal(await headerSnapshot(), headerHtml);
        assert.equal(await page.evaluate(()=>window.__entryContext===document.querySelector('.pos-prime-header__context') && window.__entryShortcuts===document.querySelector('[data-pos-shortcuts-toggle]')),true);
        assert.deepEqual(await state(), afterActions);
        pass('Phone to desktop restores exact original header DOM, controls and unchanged cart');
        await page.setViewportSize({width:390,height:844}); await ready('/admin/pos/v3');
        assert.deepEqual((await state()).lines,afterActions.lines);
        pass('Prime v3 navigation/reload preserves existing server cart and quantities');
        await page.emulateMedia({ reducedMotion:'reduce' });
        assert.equal(await page.locator('.pos-entry-context summary > i').evaluate(el=>getComputedStyle(el).transitionDuration),'0s');
        await page.emulateMedia({forcedColors:'active'}); await page.screenshot({path:path.join(info.output,'phone-forced-colors.png')});
        await page.emulateMedia({forcedColors:'none',reducedMotion:'no-preference'});
        pass('Reduced motion and forced-colors rendering remain usable');
        await ready('/admin/pos/legacy'); await geometry(390,844);
        await page.locator('.pos-entry-context > summary').click();
        assert.match(await page.locator('.pos-entry-context__terminal').innerText(),/Quầy bán hàng số 01/);
        await page.locator('.pos-entry-context > summary').click();
        await page.locator('#txtBarcode').fill('2099900000003'); await page.locator('#txtBarcode').press('Enter');
        await expectQty(info.waterId,null,3);
        await page.screenshot({path:path.join(info.output,'legacy-phone-390.png'),animations:'disabled'});
        await page.setViewportSize({width:1440,height:1000}); await idle();
        await page.locator('[data-pos-command-focus]').focus();
        await page.screenshot({animations:'disabled',caret:'hide'});
        await desktopStyleProof('legacy-desktop-1440');
        pass('Legacy phone uses same true terminal and scanner; legacy desktop computed styles/boxes unchanged');
        await main.context.close();
        const closed = await session(info.closed,{width:390,height:844}); page=closed.tab;
        await page.goto(info.closed.baseUrl+'/admin/pos'); await page.locator('.pos-entry-context').waitFor();
        assert.match(await page.locator('.pos-entry-context').innerText(),/Chưa mở ca/);
        // Existing POS owner redirects to opening a shift; never suppress that guard for the demo.
        await page.waitForURL(url=>url.pathname==='/admin/pos-shift');
        await page.screenshot({path:path.join(info.output,'phone-no-shift.png')});
        await closed.context.close(); pass('Other store without shift displays original warning and preserves shift-opening redirect');
        const denied = await session(info.denied,{width:390,height:844}); page=denied.tab;
        for(const route of ['/admin/pos','/admin/pos/v3','/admin/pos/legacy']){
            const response=await page.request.get(info.denied.baseUrl+route,{maxRedirects:0});
            assert.ok([302,403].includes(response.status()),'Denied route '+route+' status '+response.status());
            if(response.status()===302)assert.match(response.headers().location,/access.?denied|forbidden/i);
        }
        await denied.context.close(); pass('Server rejects POS routes for account without POS permission');
        // Missing optional presentation script retains the original functional page.
        const fallback=await session(info.manager,{width:390,height:844}); page=fallback.tab;
        await page.route('**/pos.entry.mobile.js*',r=>r.abort()); await ready('/admin/pos');
        assert.equal(await page.locator('[data-pos-phone]').count(),0); assert.equal(await page.locator('#txtBarcode').isVisible(),true);
        assert.equal(await qty(info.variantId,'Lon'),5); assert.equal(await qty(info.variantId,'Lốc'),1);assert.equal(await qty(info.waterId),3);
        await fallback.context.close(); pass('Missing optional adapter keeps original controls and saved draft available');
        assert.deepEqual(result.errors,[]);assert.deepEqual(result.csp,[]);result.completed=true;
    } finally {
        if(!result.completed && page && !page.isClosed())await page.screenshot({path:path.join(info.output,'failure.png'),fullPage:true}).catch(()=>{});
        fs.writeFileSync(path.join(info.output,'result.json'),JSON.stringify(result,null,2));await browser.close();
    }
})().catch(e=>{console.error(e);process.exitCode=1;});
