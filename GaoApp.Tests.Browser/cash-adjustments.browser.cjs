'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => resolve(s)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const staff = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const admin = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = []; for (const page of [staff, admin]) page.on('pageerror', e => errors.push(e.message));
    const out = 'TestResults/cash-adjustments'; fs.mkdirSync(out, { recursive: true });
    const base = '/admin/pos-shift/cash-adjustments';
    async function login(page, user, password) {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(user); await page.locator('[name=Password]').fill(password);
        if (await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
    }
    async function clickPost(page, selector, suffix) {
        const response = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith(suffix));
        await page.locator(selector).click(); const r = await response;
        assert.equal(r.status(), 200, await r.text());
    }
    try {
        await login(staff, info.user, info.password); await login(admin, info.admin, info.adminPassword);
        await staff.goto(info.baseUrl + base + '?transactionId=' + info.voucherId);
        await staff.locator('[data-create]').click();
        await staff.locator('#caType').selectOption('2'); await staff.locator('#caAmount').fill('35000');
        await staff.locator('#caReason').fill('Chi phí giao hàng'); await staff.locator('#caRequestReason').fill('Nhập nhầm thu thành chi và sai số tiền');
        assert.match(await staff.locator('#caDelta').textContent(), /-65\.000/);
        await staff.screenshot({ path: out + '/staff-request.png' });
        await clickPost(staff, '#caCreateForm button[type=submit]', `/transactions/${info.voucherId}/requests`);
        await staff.locator('[data-decision=withdraw]').waitFor();
        assert.equal(await staff.locator('[data-decision=approve]').count(), 0);
        await staff.keyboard.press('Escape'); await staff.locator('#caDialog').waitFor({ state: 'hidden' });
        await admin.goto(info.baseUrl + base);
        await admin.locator('[data-detail]').first().click(); await admin.locator('[data-decision=approve]').waitFor();
        assert.match(await admin.locator('#caDialogBody').textContent(), /-35\.000/);
        const warning = admin.locator('#caNegativeExpectedWarning');
        assert.match(await warning.textContent(), /30\.000/);
        assert.match(await warning.textContent(), /-65\.000/);
        assert.match(await warning.textContent(), /-35\.000/);
        assert.match(await warning.textContent(), /chưa phải kết luận về tiền thực tế/);
        assert.match(await warning.textContent(), /Admin cần kiểm tra/);
        await warning.scrollIntoViewIfNeeded();
        await admin.screenshot({ path: out + '/admin-review.png' });
        await clickPost(admin, '[data-decision=approve]', '/approve');
        await admin.locator('[data-decision=reconcile]').waitFor();
        assert.match(await admin.locator('#caNegativeExpectedWarning').textContent(), /tiền dự kiến đang âm/);
        assert.match(await admin.locator('#caDialogBody').textContent(), /Cần đối soát lại/);
        await admin.locator('[data-decision=reconcile]').click();
        assert.match(await admin.locator('#caDialogError').textContent(), /ghi chú/);
        await admin.locator('#caDecisionNote').fill('Đã đối chiếu tiền thực đếm và chứng từ điều chỉnh');
        await clickPost(admin, '[data-decision=reconcile]', '/reconcile');
        await admin.waitForFunction(() => document.getElementById('caDialogBody').textContent.includes('Đã đối soát lại bởi'));
        await admin.screenshot({ path: out + '/closed-reconciled.png' });
        await admin.keyboard.press('Escape'); await admin.locator('#caStatus').selectOption('Approved');
        await admin.locator('[data-detail]').waitFor();
        for (const width of [390, 768, 1440]) {
            await admin.setViewportSize({ width, height: 1000 });
            await admin.locator('[data-detail]').first().click();
            await admin.waitForFunction(() => document.getElementById('caDialogBody').textContent.includes('Đã đối soát lại bởi'));
            assert.equal(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
            assert.equal(await admin.locator('#caDialog').evaluate(el => el.scrollWidth <= el.clientWidth + 1), true);
            await admin.screenshot({ path: `${out}/detail-${width}.png` });
            await admin.keyboard.press('Escape');
        }
        // Reopen the corrected transaction, request cancellation, then withdraw it.
        await staff.goto(info.baseUrl + base + '?transactionId=' + info.voucherId);
        await staff.locator('[data-create]').click(); await staff.locator('#caKind').selectOption('cancel');
        assert.equal(await staff.locator('#caEditFields').isVisible(), false);
        await staff.locator('#caRequestReason').fill('Đề nghị hủy phiếu trùng');
        await clickPost(staff, '#caCreateForm button[type=submit]', `/transactions/${info.voucherId}/requests`);
        await staff.locator('[data-decision=withdraw]').waitFor();
        await clickPost(staff, '[data-decision=withdraw]', '/withdraw');
        await staff.waitForFunction(() => document.getElementById('caDialogBody').textContent.includes('Đã rút bởi'));
        assert.deepEqual(errors, []);
        console.log('PASS: staff request, type + amount correction, admin-only approval, closed reconciliation, ESC, responsive dialog and withdrawal.');
    } catch (e) { await staff.screenshot({ path: out + '/staff-failure.png' }); await admin.screenshot({ path: out + '/admin-failure.png' }); throw e; }
    finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
