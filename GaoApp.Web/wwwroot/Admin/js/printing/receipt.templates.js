/* One receipt renderer for preview, browser printing, QZ Tray and offline sales. */
(function (root) {
    'use strict';
    const sizes = { '45': { width: 45, margin: 3 }, '80': { width: 80, margin: 4 },
        A6: { width: 105, height: 148, margin: 6 }, A5: { width: 148, height: 210, margin: 8 }, A4: { width: 210, height: 297, margin: 12 } };
    const layouts = { modern: 'Hiện đại', classic: 'Thanh lịch', compact: 'Gọn gàng', itemwide: 'Tên hàng rộng' };
    const paperSizes = layout => layout === 'itemwide' ? ['80'] : Object.keys(sizes);
    const escape = value => String(value ?? '').replace(/[&<>"']/g, x => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[x]));
    const money = value => Number(value || 0).toLocaleString('vi-VN', { maximumFractionDigits: 2 });
    const text = (value, max) => String(value ?? '').slice(0, max);
    function normalize(input = {}) {
        return { name: text(input.name || 'Hiện đại · 80 mm', 100), layout: layouts[input.layout] ? input.layout : 'modern',
            paperSize: paperSizes(input.layout).includes(input.paperSize) ? input.paperSize : '80', accentColor: '#000000',
            title: text(input.title || 'HÓA ĐƠN BÁN HÀNG', 100), headerText: text(input.headerText, 300),
            footerText: text(input.footerText ?? 'Cảm ơn quý khách. Hẹn gặp lại!', 400),
            showCustomer: input.showCustomer !== false, showCashier: input.showCashier !== false,
            showSku: input.showSku === true, showPayments: input.showPayments !== false };
    }
    function builtIns() {
        return Object.entries(layouts).flatMap(([layout, name]) => paperSizes(layout).map(paperSize => ({ key: layout + '-' + paperSize,
            builtIn: true, design: normalize({ layout, paperSize, name: name + ' · ' + paperSize + (paperSize.length === 2 && !paperSize.startsWith('A') ? ' mm' : '') }) })));
    }
    function render(receipt, input, options = {}) {
        const d = normalize(input), size = sizes[d.paperSize], narrow = d.paperSize === '45', thermal = !size.height;
        const e = escape, m = money, order = receipt || {}, lines = order.lines || [], payments = order.payments || [];
        const date = new Date(order.finalizedAtUtc || order.completedAtUtc || order.createdAtUtc || Date.now());
        const dateText = Number.isNaN(date.getTime()) ? '' : date.toLocaleString('vi-VN');
        const label = order.orderNumber || '#' + (order.orderId || 'XEM-TRƯỚC');
        const methods = { 0: 'Tiền mặt', 1: 'Chuyển khoản', 2: 'Thẻ', Cash: 'Tiền mặt', BankTransfer: 'Chuyển khoản', Card: 'Thẻ' };
        const statuses = { 0: 'Đơn đang lập', 1: 'Đơn đang giữ', 2: 'Hoàn tất', 3: 'Đã hủy', 4: 'Đã hủy sau chốt', 5: 'Đã hoàn trả',
            Draft: 'Đơn đang lập', OnHold: 'Đơn đang giữ', Completed: 'Hoàn tất', Cancelled: 'Đã hủy', Voided: 'Đã hủy sau chốt', Refunded: 'Đã hoàn trả' };
        const wideTotals = d.layout === 'itemwide' && [order.subtotal, order.discountTotal, order.grandTotal, order.paidTotal, order.balanceDue, order.changeDue].some(value => m(value).length > 12);
        // Always black on white, including old custom designs and cached offline selections.
        const css = `*{box-sizing:border-box}html,body{margin:0;padding:0;color:#000;font-family:Arial,Helvetica,sans-serif;font-weight:600;background:#fff}
            .receipt{width:${size.width}mm;padding:${size.margin}mm;font-size:${narrow ? 10 : 12}px;line-height:1.5;overflow-wrap:anywhere}
            .brand{text-align:center;padding:2mm 0 4mm;border-bottom:2px solid #000}
            .store{font-size:${narrow ? 15 : thermal ? 20 : 25}px;font-weight:800;letter-spacing:.3px;line-height:1.25;margin-bottom:2mm}
            .muted{color:#000;font-size:.92em;font-weight:600}.header-note,.footer{white-space:pre-line}.title{text-align:center;font-size:${narrow ? 12 : thermal ? 16 : 19}px;margin:4mm 0 1mm;font-weight:800;letter-spacing:.4px}
            .number{text-align:center;letter-spacing:.6px;font-size:1em;font-weight:800;margin-bottom:4mm}.meta{width:100%;border-collapse:collapse;margin-bottom:4mm}.meta td{padding:.8mm 0;vertical-align:top}.meta td:first-child{width:32%}
            table.items{width:100%;border-collapse:collapse;table-layout:fixed}thead{display:table-header-group}tr{break-inside:avoid;page-break-inside:avoid}td,th{overflow-wrap:anywhere}th{background:#fff;padding:2mm 1mm;border-bottom:1px solid #000;font-weight:800;text-align:left}td{vertical-align:top}table.items td{padding:2.5mm 1mm;border-bottom:1px solid #000}
            strong,b,.item-name{font-weight:800}.num{text-align:right;font-variant-numeric:tabular-nums}td.num,.payment-line span:last-child{white-space:nowrap}table.items td.num{padding-left:.5mm;padding-right:.5mm}td.num .muted{white-space:normal}.totals{width:100%;border-collapse:collapse;margin-top:4mm;break-inside:avoid}.totals td{padding:1mm}.settlement td{font-weight:800;font-size:1.05em}.grand td{font-weight:800;font-size:1.35em;border-top:2px solid #000;border-bottom:3px double #000;padding:2.5mm 1mm}.payment{border-top:1px dashed #000;margin-top:3mm;padding-top:2mm}.payment-line{display:table;width:100%;margin:1mm 0}.payment-line span{display:table-cell}.payment-line span:last-child{text-align:right;font-weight:800}.footer{margin-top:5mm;padding-top:4mm;border-top:1px dashed #000;text-align:center;font-size:1em;break-inside:avoid}.offline-note{border:1px solid #000;padding:2mm;margin-top:4mm;font-size:1em;font-weight:800;break-inside:avoid}
            .classic .brand{border-top:3px double #000;border-bottom:3px double #000;padding:4mm 0}.classic th{border-top:1px solid #000;border-bottom:1px solid #000}
            .compact .brand{text-align:left;border-bottom:1px solid #000;padding:1mm 0 2mm}.compact .store{font-size:${narrow ? 14 : 18}px}.compact .title,.compact .number{text-align:left;margin:2mm 0}.compact table.items td{padding:1.5mm 0;border-bottom:1px dashed #000}.compact .meta{margin-bottom:2mm}.compact .footer{margin-top:3mm;padding-top:2mm}
            .itemwide .item-group{break-inside:avoid;page-break-inside:avoid}.itemwide .items .item-heading td{padding:2.5mm 0 .5mm;border-bottom:0}.itemwide .item-name{font-size:13px}.itemwide .items .item-values td{padding:.5mm .5mm 2mm;border-bottom:1px dashed #000}.itemwide .items th{padding:2mm .5mm;white-space:nowrap}.itemwide .item-unit{text-align:center}.itemwide .item-quantity{font-weight:800}
            .itemwide .wide-details{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:2mm 3mm}.itemwide .wide-details .num{white-space:nowrap}.itemwide .wide-detail-label{display:block;font-weight:600;margin-bottom:.5mm}.itemwide .wide-detail-full{grid-column:1/-1}.itemwide .items .item-expanded td{padding:1mm 0 2.5mm}.itemwide .totals .amount-label{margin-bottom:.5mm}
            .amount{white-space:nowrap}.amount-label{text-align:left}.receipt[data-paper-size="45"] .items td,.receipt[data-paper-size="45"] .items th{padding-left:0;padding-right:0}.receipt[data-paper-size="45"] .totals .num{margin-top:1mm}
            @page{size:${size.height ? size.width + 'mm ' + size.height + 'mm' : size.width + 'mm 300mm'};margin:0}
            @media print{html,body{width:${size.width}mm}.receipt{width:${size.width}mm}body{-webkit-print-color-adjust:exact;print-color-adjust:exact}}`;
        const row = (title, value, cls = '') => (narrow && cls) || wideTotals
            ? `<tr class="${cls}"><td colspan="2"><div class="amount-label">${e(title)}</div><div class="num">${m(value)}</div></td></tr>`
            : `<tr class="${cls}"><td>${e(title)}</td><td class="num">${m(value)}</td></tr>`;
        const meta = (title, value) => `<tr><td>${e(title)}</td><td>${e(value)}</td></tr>`;
        function wideValues() {
            const width = (size.width - size.margin * 2) * 96 / 25.4;
            // Conservative widths for the renderer's 12 px bold Arial/Helvetica digits and separators.
            // Static sizing also works in sandboxed previews and QZ/offline HTML without executing scripts.
            const numberWidth = (value, bold = false) => [...m(value)].reduce((sum, character) => sum + (/\d/.test(character) ? (bold ? 9 : 7) : (bold ? 5 : 4)), 0);
            const needed = line => [Math.max(30, numberWidth(line.quantity, true) + 5), 42, Math.max(52, numberWidth(line.unitPrice) + 5), Math.max(75, numberWidth(line.lineTotal, true) + 5)];
            const ratios = [.15, .17, .31, .37];
            let columns = ratios.map(ratio => ratio * width);
            const compactLines = lines.filter(line => needed(line).reduce((sum, value) => sum + value, 0) <= width);
            const maximums = compactLines.reduce((max, line) => needed(line).map((value, i) => Math.max(value, max[i])), [30, 42, 52, 75]);
            const remaining = width - maximums.reduce((sum, value) => sum + value, 0);
            if (remaining >= 0 && maximums.some((value, i) => value > columns[i])) columns = maximums.map((value, i) => value + remaining * ratios[i]);
            const fitsColumns = line => needed(line).every((value, i) => value <= columns[i]);
            const values = line => {
                const unit = e(line.sellingUnitName || line.unitName || '—');
                if (fitsColumns(line))
                    return `<tr class="item-values"><td class="num item-quantity" data-item-value="quantity">${m(line.quantity)}</td><td class="item-unit" data-item-value="unit">${unit}</td><td class="num" data-item-value="price">${m(line.unitPrice)}</td><td class="num" data-item-value="total"><strong>${m(line.lineTotal)}</strong></td></tr>`;
                const detail = (label, key, value, numeric, bold = false) => `<div class="${numeric && numberWidth(value, bold) + 5 > (width - 3 * 96 / 25.4) / 2 ? 'wide-detail-full' : ''}"><span class="wide-detail-label">${label}</span><div class="${numeric ? 'num' : ''}" data-item-value="${key}">${bold ? '<strong>' : ''}${numeric ? m(value) : unit}${bold ? '</strong>' : ''}</div></div>`;
                return `<tr class="item-values item-expanded"><td colspan="4"><div class="wide-details">${detail('SL', 'quantity', line.quantity, true, true)}${detail('ĐVT', 'unit', null, false)}${detail('Đơn giá', 'price', line.unitPrice, true)}${detail('Thành tiền', 'total', line.lineTotal, true, true)}</div></td></tr>`;
            };
            return { columns: columns.map(value => `<col style="width:${value / width * 100}%">`).join(''), values, showHead: !lines.length || lines.some(fitsColumns) };
        }
        const wide = d.layout === 'itemwide' ? wideValues() : null;
        const wideItems = wide ? `<table class="items"><colgroup>${wide.columns}</colgroup>
            ${wide.showHead ? '<thead><tr><th class="num" scope="col">SL</th><th class="item-unit" scope="col">ĐVT</th><th class="num" scope="col">Đơn giá</th><th class="num" scope="col">Thành tiền</th></tr></thead>' : ''}
            ${lines.map(line => `<tbody class="item-group"><tr class="item-heading"><td colspan="4"><div class="item-name">${e(line.productVariantName || line.itemName || line.productName)}</div>
            ${d.showSku && line.sku ? `<div class="muted">${e(line.sku)}</div>` : ''}
            ${Number(line.lineDiscount) > 0 ? `<div class="muted">Giảm ${m(line.lineDiscount)}</div>` : ''}</td></tr>
            ${wide.values(line)}</tbody>`).join('') || '<tbody><tr><td colspan="4">Chưa có sản phẩm</td></tr></tbody>'}</table>` : '';
        const body = `<article class="receipt ${d.layout}" data-paper-size="${d.paperSize}">
            <header class="brand"><div class="store">${e(order.storeName || 'GaoApp POS')}</div>
            ${order.storeAddress ? `<div class="store-address" style="white-space:pre-line">${e(order.storeAddress)}</div>` : ''}${order.storePhone ? `<div>Điện thoại: ${e(order.storePhone)}</div>` : ''}
            ${d.headerText ? `<div class="header-note">${e(d.headerText)}</div>` : ''}</header>
            <h1 class="title">${e(d.title)}</h1><div class="number">${e(label)}</div>
            <table class="meta">${meta('Ngày bán', dateText)}${order.status != null ? meta('Trạng thái', statuses[order.status] || order.status) : ''}${d.showCashier ? meta('Thu ngân', order.cashierName || order.userName || '—') : ''}
            ${order.terminalName ? meta('Quầy', order.terminalName) : ''}${d.showCustomer ? meta('Khách hàng', order.customerName || order.customer?.name || 'Khách lẻ') : ''}
            ${d.showCustomer && order.customerPhone ? meta('Điện thoại', order.customerPhone) : ''}${order.note ? meta('Ghi chú', order.note) : ''}</table>
            ${d.layout === 'itemwide' ? wideItems : `<table class="items"><colgroup><col style="width:${narrow ? '48' : thermal ? '33' : '40'}%">${narrow ? '' : `<col style="width:${thermal ? '12' : '10'}%"><col style="width:24%">`}<col style="width:${narrow ? '52' : thermal ? '31' : '26'}%"></colgroup>
            <thead><tr><th>Sản phẩm</th>${narrow ? '' : '<th class="num">SL</th><th class="num">Đơn giá</th>'}<th class="num">Thành tiền</th></tr></thead><tbody>
            ${lines.map(line => `<tr><td><div class="item-name">${e(line.productVariantName || line.itemName || line.productName)}</div>
            ${d.showSku && line.sku ? `<div class="muted">${e(line.sku)}</div>` : ''}
            ${narrow ? `<div class="muted">${m(line.quantity)} ${e(line.sellingUnitName || line.unitName || '')} × <span class="amount">${m(line.unitPrice)}</span></div>` : ''}
            ${Number(line.lineDiscount) > 0 ? `<div class="muted">Giảm ${m(line.lineDiscount)}</div>` : ''}</td>
            ${narrow ? '' : `<td class="num">${m(line.quantity)}<div class="muted">${e(line.sellingUnitName || line.unitName || '')}</div></td><td class="num">${m(line.unitPrice)}</td>`}
            <td class="num"><strong>${m(line.lineTotal)}</strong></td></tr>`).join('') || '<tr><td>Chưa có sản phẩm</td></tr>'}</tbody></table>`}
            <table class="totals">${row('Tạm tính', order.subtotal)}${row('Giảm giá', order.discountTotal)}${row('TỔNG CỘNG', order.grandTotal, 'grand')}
            ${row('Đã thanh toán', order.paidTotal, 'settlement')}${Number(order.balanceDue) > 0 ? row('Còn thiếu', order.balanceDue, 'settlement') : ''}${row('Tiền thừa', order.changeDue, 'settlement')}</table>
            ${d.showPayments && payments.length ? `<section class="payment"><strong>Thanh toán</strong>${payments.map(p => `<div class="payment-line"><span>${e(methods[p.method] || p.method || 'Khác')}${p.reference ? `<br><small class="muted">${e(p.reference)}</small>` : ''}</span><span>${m(p.amount)}</span></div>`).join('')}</section>` : ''}
            ${options.offline ? '<div class="offline-note">Phiếu bán tại quầy · Chờ đồng bộ máy chủ.' + (payments.some(p => p.method === 1 || p.method === 'BankTransfer') ? '<br>Chuyển khoản do nhân viên xác nhận thủ công.' : '') + '</div>' : ''}
            <footer class="footer">${e(d.footerText)}</footer></article>`;
        return { design: d, size, css, body, html: `<!doctype html><html lang="vi"><head><meta charset="utf-8"><title>Phiếu ${e(label)}</title><style>${css}</style></head><body>${body}</body></html>` };
    }
    root.ReceiptTemplates = { sizes, layouts, paperSizes, normalize, builtIns, render, escape, money };
    if (typeof module === 'object' && module.exports) module.exports = root.ReceiptTemplates;
})(typeof window === 'object' ? window : globalThis);
