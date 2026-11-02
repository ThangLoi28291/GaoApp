'use strict';
const fs = require('node:fs');

// Test instrumentation observes the same nonce policy as the application's scripts.
async function addNoncedScript(page, options) {
    const nonce = await page.evaluate(() => document.querySelector('script[nonce],style[nonce]')?.nonce);
    if (!nonce) throw new Error('A real application page must provide its CSP nonce before test instrumentation.');
    const content = options.path ? fs.readFileSync(options.path, 'utf8') : options.content;
    if (options.url && new URL(options.url, page.url()).origin !== new URL(page.url()).origin)
        throw new Error('Browser test assets must come from the local application.');
    await page.evaluate(({ nonce, content, url }) => new Promise((resolve, reject) => {
        const script = document.createElement('script'); script.nonce = nonce;
        if (url) { script.src = url; script.onload = () => resolve(); script.onerror = () => reject(new Error('Test asset failed to load.')); }
        else script.textContent = content;
        document.head.append(script); if (!url) resolve();
    }), { nonce, content, url: options.url });
}

// Open real disclosure controls; never remove hidden attributes or force visibility.
async function reveal(page, selector) {
    const target = page.locator(selector); await target.waitFor({ state: 'attached' });
    for (let attempt = 0; attempt < 8; attempt++) {
        const folded = target.locator('xpath=ancestor::details[not(@open)]').first();
        if (await folded.count()) { await folded.locator(':scope > summary').click(); continue; }
        const panel = target.locator('xpath=ancestor::*[@data-workbench-panel]').first();
        if (await panel.count()) {
            const key = await panel.getAttribute('data-workbench-panel');
            const tab = page.locator('[data-workbench-tab="' + key + '"]');
            if (await tab.count() && await tab.isVisible() && !await target.isVisible()) { await tab.click(); continue; }
        }
        break;
    }
    await target.waitFor({ state: 'visible' }); return target;
}
async function continueShiftCountWithMockPrinter(page) {
    await page.locator('#shiftDrawerConfirmModal.show').waitFor();
    await page.evaluate(() => { window.PosPrinting.drawerReady = async () => { throw new Error('Synthetic browser test: printer unavailable'); }; });
    await page.locator('#btnConfirmShiftDrawer').click();
    await page.locator('#btnContinueShiftCount').waitFor();
    await page.locator('#btnContinueShiftCount').click();
}
module.exports = { addNoncedScript, reveal, continueShiftCountWithMockPrinter };
