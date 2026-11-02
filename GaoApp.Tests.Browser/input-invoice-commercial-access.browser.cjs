const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const ui11 = require('./receipt-mobile-review.cjs'); // UI-11 review hook
(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let input = ''; process.stdin.on('data', x => input += x); process.stdin.on('end', () => resolve(input));
    }));
    fs.mkdirSync(info.output, { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        for (const role of ['viewer', 'cost', 'direct', 'purchase']) {
            const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
            await ui11.protect(context, info.baseUrl); // UI-11 review hook
            const page = await context.newPage(), errors = [], files = [];
            page.on('pageerror', e => errors.push(e.message));
            page.on('request', request => {
                if (/\/admin\/input-invoices\/\d+\/(pdf|xml|preview)(?:\?|$)/.test(request.url())) files.push(request.url());
            });
            await page.goto(info.baseUrl + '/admin/account/login');
            await page.locator('[name=UserName]').fill(info.accounts[role].user);
            await page.locator('[name=Password]').fill(info.accounts[role].password);
            if (await page.locator('[name=SelectedTerminalId]').count())
                await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
            await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
            await page.goto(info.baseUrl + '/admin/input-invoices');
            await page.waitForFunction(() => document.querySelectorAll('#ilRows [data-open]').length === 6);
            if(role==='viewer')await ui11.review(page,'invoice-no-cost-list',ui11.output('invoice-rights')); // UI-11 review hook
            if (role === 'viewer') {
                assert.match(await page.locator('#ilTypes').innerText(), /Không có quyền xem giá/);
                assert.doesNotMatch(await page.locator('#ilRows').innerText(), /108\.000|8\.000/);
            }
            for (const kind of ['unlinked', 'purchase', 'mixed', 'zero']) {
                const allowed = role === 'cost' || (role === 'direct' && ['unlinked', 'zero'].includes(kind)) ||
                    (role === 'purchase' && kind === 'purchase');
                const canReview = allowed && role !== 'cost';
                const beforeFiles = files.length;
                await page.locator(`#ilRows [data-open="${info.ids[kind]}"]`).click();
                await page.waitForFunction(() => document.getElementById('ilDetailMeta').textContent.includes('MST'));
                assert.equal(await page.locator('#ilSave').isVisible(), canReview, `${role}/${kind} review`);
                assert.equal(await page.locator('#ilReviewStatus').isEnabled(), canReview);
                assert.equal(await page.locator('#ilDownload').isVisible(), allowed);
                assert.equal(await page.locator('#ilShowPdf').isVisible(), allowed);
                assert.equal(await page.locator('#ilShowXml').isVisible(), allowed);
                if (allowed) {
                    await page.waitForFunction(() => document.querySelector('#ilPreviewContent iframe'));
                    await page.locator('#ilShowXml').click();
                    await page.waitForFunction(() => document.querySelector('#ilPreviewContent article'));
                    assert.match(await page.locator('#ilDetailMeta').innerText(), kind === 'zero' ? /Tổng tiền:\s*0 đ/ : /108\.000/);
                    assert.equal(await page.locator('#ilDownload').getAttribute('href'), `/admin/input-invoices/${info.ids[kind]}/xml`);
                } else {
                    assert.equal(files.length, beforeFiles, `${role}/${kind} must not request a preview or original`);
                    assert.equal(await page.locator('#ilDownload').getAttribute('href'), null);
                    assert.equal(await page.locator('#ilPreviewContent iframe').count(), 0);
                    assert.match(await page.locator('#ilPreviewContent').innerText(), /Không có quyền xem giá/);
                    assert.doesNotMatch(await page.locator('#ilDetailMeta').innerText(), /100\.000|108\.000/);
                    assert.doesNotMatch(await page.locator('#ilHistory').innerText(), /B2_PRIVATE_PRICE_NOTE/);
                }
                if (kind === 'unlinked' || kind === 'purchase')
                    await page.screenshot({ path: path.join(info.output, `${role}-${kind}.png`) });
                if(kind==='unlinked')await ui11.dialog(page,'#ilDetail','invoice-'+role+'-rights',ui11.output('invoice-rights')); // UI-11 review hook
                if (role === 'direct' && kind === 'unlinked') {
                    await page.locator('#ilReviewNote').fill('B2_PRIVATE_PRICE_NOTE_100000');
                    await page.locator('#ilSave').click();
                    await page.waitForFunction(() => document.getElementById('ilDetailMessage').textContent === 'Đã lưu xác nhận.');
                    assert.match(await page.locator('#ilHistory').innerText(), /B2_PRIVATE_PRICE_NOTE_100000/);
                }
                if (role === 'viewer' && kind === 'unlinked') {
                    await page.setViewportSize({ width: 390, height: 740 });
                    const metadata = await page.locator('#ilDetailMeta').boundingBox();
                    assert.ok(metadata && metadata.y < 400, 'restricted mobile preview must not push metadata below the screen');
                    await page.screenshot({ path: path.join(info.output, 'viewer-mobile.png') });
                    await page.setViewportSize({ width: 1440, height: 900 });
                }
                await page.locator('#ilDetail footer [data-close]').click();
                await page.waitForFunction(() => !document.getElementById('ilDownload').hasAttribute('href'));
                assert.equal(await page.locator('#ilDownload').getAttribute('href'), null);
            }
            assert.deepEqual(errors, [], role + ' JavaScript errors');
            console.log(`PASS ${role}: list, metadata, per-invoice controls, original preview gates, cleared links and zero amounts`);
            await context.close();
        }
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
