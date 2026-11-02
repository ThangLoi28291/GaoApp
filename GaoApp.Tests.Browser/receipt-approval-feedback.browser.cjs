const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const results = [];
  try {
    for (const scenario of ['labels-pending', 'no-labels', 'server-rejected', 'network-uncertain', 'draft-rejected']) {
      const page = await browser.newPage();
      await page.setContent(`<button id="btnApprove">Xác nhận duyệt</button><div id="approveMessage"></div>
        <input id="commercialSupplierId" value="1"><input id="approvalQueueLabels" type="checkbox">
        <div id="commercialApprovalWorkbench"><div class="commercial-line" data-line-id="1" data-line-no="1">
          <input class="commercial-unit-price" value="10"><input class="commercial-quantity" value="2">
          <span class="commercial-price-error"></span></div></div>`);
      await page.evaluate(scenario => {
        window.events = []; window.apiCalls = 0; window.labelsPending = false;
        window.stockDocumentPage = { documentId: 1, rowVersion: 'current', canApproveCommercial: true };
        window.GaoReceiptApprovalWorkspace = { getState: () => ({ blocking: [] }) };
        window.GaoReceiptPriceDrafts = { hold: async () => {
          events.push('draft'); if (scenario === 'draft-rejected') throw new Error('Giá nháp chưa lưu');
          return () => events.push('released');
        }};
        window.GaoReceiptInvoiceFollowUp = { approvalChoice: async () => { events.push('invoice'); return false; }, approvalReview: () => null };
        window.showStockDocumentToast = (...args) => { events.push('success'); window.toast = args; };
        window.GaoLabels = { afterApproval: async () => {
          events.push('labels'); window.labelsPending = true;
          return await new Promise(resolve => { window.finishLabels = resolve; });
        }};
        document.getElementById('approvalQueueLabels').checked = scenario === 'labels-pending';
        window.fetch = async () => {
          window.apiCalls++; events.push('approval');
          if (scenario === 'network-uncertain') throw new TypeError('Offline');
          return new Response(JSON.stringify({ message: scenario === 'server-rejected' ? 'Phiếu đã thay đổi' : 'Đã ghi sổ', redirectUrl: '/done' }),
            { status: scenario === 'server-rejected' ? 409 : 200 });
        };
      }, scenario);
      await page.addScriptTag({ path: path.join(__dirname, '../GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js') });
      await page.evaluate(() => { window.submitting = window.GaoAppPurchaseReceiptApproval.submit(); });
      await page.waitForFunction(() => window.labelsPending || window.toast || document.getElementById('approveMessage').textContent.length > 0);
      const state = await page.evaluate(() => ({ events, toast: window.toast, calls: apiCalls,
        disabled: document.getElementById('btnApprove').disabled,
        button: document.getElementById('btnApprove').textContent,
        message: document.getElementById('approveMessage').textContent }));
      if (scenario === 'labels-pending') {
        assert(state.events.indexOf('success') < state.events.indexOf('labels'), 'Approval must be acknowledged before waiting for labels');
        assert(state.disabled); assert.match(state.button, /Đã duyệt/); assert.equal(state.toast[3], null);
        await page.evaluate(async () => { finishLabels(true); await submitting; });
        assert(await page.locator('#btnApprove').isDisabled(), 'Committed approval must remain disabled');
      } else if (scenario === 'no-labels') {
        await page.evaluate(() => submitting);
        assert(!state.events.includes('labels')); assert(state.disabled); assert.equal(state.toast[4], 250);
      } else {
        await page.evaluate(() => submitting);
        assert(!state.events.includes('success')); assert(!state.disabled);
        assert.match(state.message, scenario === 'network-uncertain' ? /Chưa xác định/ : scenario === 'draft-rejected' ? /Giá nháp chưa lưu/ : /Phiếu đã thay đổi/);
        assert.equal(state.calls, scenario === 'draft-rejected' ? 0 : 1);
      }
      results.push({ scenario, passed: true });
      await page.close();
    }
    const directory = path.join(__dirname, '../Logs/receipt-approval-fix-20261029');
    fs.mkdirSync(directory, { recursive: true });
    fs.writeFileSync(path.join(directory, 'feedback-browser.json'), JSON.stringify(results, null, 2));
    console.log(JSON.stringify(results));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
