const test = require('node:test');
const assert = require('node:assert/strict');
const { sourceLineMarkup, priceBreakdownRows, priceBreakdownMarkup, displayStatus, deliveryBillUrl, appendDeliveryOnly, isDeliverySource, deliveryDocumentsMarkup } =
    require('../../GaoApp.Web/wwwroot/Admin/js/pos/pos.orders.page.js');

test('saved line gross, promotion and net remain distinct and escaped, including free gifts', () => {
    const milk = sourceLineMarkup({ itemName: 'Sữa <saved>', quantity: 2, unitName: 'Hộp', unitPrice: 95000,
        grossAmount: 190000, promotionDiscount: 23333, promotionName: 'KM <saved>', lineTotal: 166667, sku: 'MILK-02' });
    for (const amount of ['95.000', '190.000', '23.333', '166.667']) assert.ok(milk.includes(amount));
    assert.ok(milk.includes('Sữa &lt;saved&gt;')); assert.ok(milk.includes('KM &lt;saved&gt;'));
    assert.ok(milk.includes('MILK-02')); assert.ok(!milk.includes('<saved>'));
    const single = sourceLineMarkup({ quantity: 1, unitPrice: 95000, grossAmount: 95000, promotionDiscount: 11667,
        lineTotal: 83333, itemName: 'Sữa đơn' });
    assert.ok(single.includes('11.667')); assert.ok(single.includes('83.333'));
    const gift = sourceLineMarkup({ quantity: 1, unitPrice: 0, grossAmount: 0, lineTotal: 0,
        itemName: 'Quà', isPromotionGift: true, giftPromotionName: 'Quà đã lưu' });
    assert.ok(gift.includes('Hàng tặng')); assert.ok(gift.includes('Sau giảm</small>0 đ'));
});

test('saved header discount components appear once and combo allocation is not charged twice', () => {
    const golden = { subtotal: 494000, discountTotal: 58333, promotionDiscountTotal: 58333, grandTotal: 435667 };
    assert.deepEqual(priceBreakdownRows(golden), [['Khuyến mãi sản phẩm', 58333]]);
    const header = { ...golden, discountTotal: 78333, lineDiscountTotal: 2000, comboDiscountTotal: 5000,
        orderDiscount: 6000, voucherDiscountTotal: 7000, grandTotal: 415667, comboAllocatedDiscount: 5000,
        comboPromotionName: '<Combo đã lưu>' };
    const snapshot = JSON.stringify(header);
    const rows = priceBreakdownRows(header);
    assert.equal(rows.length, 5); assert.equal(rows.reduce((total, row) => total + row[1], 0), 78333);
    assert.equal(header.subtotal - rows.reduce((total, row) => total + row[1], 0), header.grandTotal);
    assert.deepEqual(rows.map(row => row[1]), [2000, 58333, 5000, 6000, 7000]);
    const markup = priceBreakdownMarkup(header);
    assert.equal((markup.match(/Giảm combo/g) || []).length, 1);
    assert.ok(markup.includes('494.000')); assert.ok(markup.includes('&lt;Combo đã lưu&gt;'));
    assert.equal(JSON.stringify(header), snapshot);
});

test('delivery read status overlays an archived cart and reprint uses the typed delivery identity', () => {
    const archived = { status: 'Cancelled', delivery: { deliveryOrderId: 17, settlementStatus: 'pending', billUrl: 'javascript:bad' } };
    assert.equal(displayStatus(archived), 'DeliveryPending'); assert.equal(deliveryBillUrl(archived), '/admin/deliveries/17/bill');
    assert.equal(displayStatus({ ...archived, delivery: { ...archived.delivery, settlementStatus: 'cancelled' } }), 'DeliveryCancelled');
    assert.equal(displayStatus({ ...archived, delivery: { ...archived.delivery, settlementStatus: 'settled' } }), 'DeliverySettled');
    assert.equal(displayStatus({ status: 'Cancelled', note: 'Giao hàng GH-17' }), 'Cancelled');
    assert.equal(deliveryBillUrl({ status: 'Cancelled' }), '');
    assert.equal(deliveryBillUrl({ delivery: { deliveryOrderId: -1 } }), '');
    assert.equal(displayStatus({ ...archived, displayStatus: 'DeliveryPending' }), 'DeliveryPending');
});

test('delivery-only URL filtering is explicit and preserves other lookup filters', () => {
    const query = new URLSearchParams('keyword=sua&status=Cancelled&page=2');
    appendDeliveryOnly(query, true);
    assert.equal(query.get('deliveryOnly'), 'true'); assert.equal(query.get('keyword'), 'sua');
    assert.equal(query.get('status'), 'Cancelled'); assert.equal(query.get('page'), '2');
    appendDeliveryOnly(query, false); assert.equal(query.has('deliveryOnly'), false);
    appendDeliveryOnly(query, 'true'); assert.equal(query.get('deliveryOnly'), 'true');
});

test('settled sale is financial while source remains a quote, and linked documents retain QR and safe IDs', () => {
    const sale = { orderId: 22, status: 'Completed', grandTotal: 23500, paidTotal: 23500,
        delivery: { deliveryOrderId: 7, code: 'GH-7', settlementStatus: 'settled', isSettlementSale: true,
            sourceOrderId: 11, saleOrderId: 22, saleOrderNumber: 'POS-<saved>' } };
    const before = JSON.stringify(sale);
    assert.equal(isDeliverySource(sale), false);
    assert.equal(isDeliverySource({ ...sale, orderId: 11, delivery: { ...sale.delivery, isSettlementSale: false } }), true);
    assert.equal(isDeliverySource({ status: 'Completed' }), false);
    const docs = deliveryDocumentsMarkup(sale);
    assert.ok(docs.includes('/admin/deliveries/7/bill'));
    assert.ok(docs.includes('/admin/pos/order-detail/11'));
    assert.ok(docs.includes('/admin/pos/order-detail/22'));
    assert.ok(docs.includes('/admin/pos/orders/22/print?autoPrint=false'));
    assert.ok(docs.includes('POS-&lt;saved&gt;')); assert.ok(!docs.includes('<saved>'));
    assert.ok(!deliveryDocumentsMarkup({ delivery: { sourceOrderId: 'javascript:bad', saleOrderId: -1 } }).includes('href='));
    assert.equal(JSON.stringify(sale), before);
});
