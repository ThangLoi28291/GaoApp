(function () {
    'use strict';
    const escape = value => String(value ?? '').replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
    function render(transaction, context) {
        if (!Number.isInteger(transaction?.id) || transaction.id <= 0 ||
            !['CashIn', 'CashOut'].includes(transaction.type) ||
            typeof transaction.amount !== 'number' || !Number.isFinite(transaction.amount) || transaction.amount <= 0)
            throw new Error('Phiếu thu/chi chưa được lưu hoặc không hợp lệ.');
        const title = transaction.type === 'CashIn' ? 'PHIẾU THU TIỀN MẶT' : 'PHIẾU CHI TIỀN MẶT';
        const code = (transaction.type === 'CashIn' ? 'PT-' : 'PC-') + transaction.id;
        const row = (name, value) => `<div class="row"><span>${name}</span><strong>${escape(value)}</strong></div>`;
        const css = 'html,body{margin:0;background:#fff;color:#000;font-family:Arial,sans-serif}.receipt{width:80mm;box-sizing:border-box;padding:4mm;font-size:12px;line-height:1.45;overflow-wrap:anywhere}.center{text-align:center}h1{font-size:17px;margin:6px 0}.row{display:flex;justify-content:space-between;gap:8px;margin:3px 0}.row strong{text-align:right}.rule{border-top:1px dashed #000;margin:10px 0}.amount{font-size:22px;font-weight:bold;text-align:center}.note{white-space:pre-wrap}.signatures{display:flex;justify-content:space-around;text-align:center;margin-top:16px;padding-bottom:30px}@page{size:80mm auto;margin:0}';
        const body = `<main class="receipt"><div class="center"><strong>${escape(context.storeName || 'GaoApp')}</strong><h1>${title}</h1><div>${escape(code)}</div></div><div class="rule"></div>` +
            row('Thời gian', new Date(transaction.createdAtUtc).toLocaleString('vi-VN')) +
            row('Ca / Quầy', `${transaction.posShiftId} / ${context.terminalName || context.terminalId || ''}`) +
            row('Người in', context.userName || '') +
            `<div class="rule"></div><div class="amount">${escape(transaction.amount.toLocaleString('vi-VN'))} đ</div><div class="rule"></div>` +
            `<strong>Lý do</strong><div class="note">${escape(transaction.reason)}</div>` +
            (transaction.note ? `<strong>Ghi chú</strong><div class="note">${escape(transaction.note)}</div>` : '') +
            '<div class="signatures"><span>Người giao tiền<br><small>(Ký, ghi rõ họ tên)</small></span><span>Người nhận tiền<br><small>(Ký, ghi rõ họ tên)</small></span></div></main>';
        return {size:{width:80,height:null},body,css,html:`<!doctype html><html lang="vi"><head><meta charset="utf-8"><title>${title} ${escape(code)}</title><style>${css}</style></head><body>${body}</body></html>`};
    }
    async function print(transaction, context, cashDrawer = false) {
        const rendered = render(transaction, context), frame = document.createElement('iframe');
        frame.title = 'Phiếu thu/chi tiền mặt';
        frame.setAttribute('sandbox', 'allow-same-origin allow-modals');
        frame.style.cssText = 'position:fixed;left:-10000px;top:0;width:80mm;height:1000px;border:0';
        try {
            await new Promise((resolve, reject) => {
                const timeout = window.setTimeout(() => reject(new Error('Không tải được phiếu để in.')), 10000);
                frame.onload = () => { window.clearTimeout(timeout); resolve(); };
                frame.srcdoc = rendered.html; document.body.appendChild(frame);
            });
            return await window.PosPrinting.send(rendered, context, frame.contentWindow, () => frame.remove(), cashDrawer === true);
        } catch (error) { frame.remove(); throw error; }
    }
    const api = {render, print, escape};
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    if (typeof window !== 'undefined') window.PosShiftCashPrinting = api;
})();
