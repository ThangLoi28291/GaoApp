/* POS offline domain adapter. No network, DOM, or storage side effects. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.PosOfflineCore = api;
})(typeof window !== 'undefined' ? window : globalThis, function () {
    'use strict';
    const clone = value => structuredClone(value);
    const round = (value, digits = 0) => Math.round((Number(value) + Number.EPSILON) * 10 ** digits) / 10 ** digits;
    const error = message => { throw new Error(message); };
    const normalize = value => String(value || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').toLowerCase();
    const draftOf = result => result?.draft || result?.data || (Array.isArray(result?.lines) ? result : null);
    const contextKey = value => `${value.storeId}:${value.terminalId}:${value.userId}:${value.shiftId}`;
    const maxPosQuantity = 1000000;

    function initial(context) {
        const state = { version: 1, context: clone(context), key: contextKey(context), orders: {}, currentId: null,
            nextId: 1500000000, queue: [], quarantined: [], maps: { order: {}, line: {}, payment: {}, customer: {} }, qrs: {}, conflict: null };
        absorb(state, context.screen);
        return state;
    }
    function id(state) { if (state.nextId >= 2000000000) error('Đã hết dải mã tạm của quầy.'); return ++state.nextId; }
    function permission(state, code) {
        if (!state.context.permissions.includes(code)) error('Bạn chưa được cấp quyền thực hiện thao tác này khi offline.');
    }
    function newDraft(state, body = {}) {
        permission(state, 'pos.order.create');
        const orderId = id(state);
        state.orders[orderId] = { orderId, status: 0, orderNumber: null, customerId: null, customerName: null,
            note: body.note || null, subtotal: 0, discountTotal: 0, orderDiscount: 0, grandTotal: 0, paidTotal: 0,
            balanceDue: 0, changeDue: 0, voucherDiscountTotal: 0, promotionDiscountTotal: 0, comboDiscountTotal: 0,
            appliedRewardVouchers: [], lines: [], payments: [], createdAtUtc: new Date().toISOString(), offline: true };
        state.currentId = orderId;
        return state.orders[orderId];
    }
    function current(state) {
        const draft = state.orders[state.currentId];
        if (!draft || ![0, 1].includes(draft.status)) error('Chưa có giỏ đang bán tại quầy.');
        return draft;
    }
    function unitPrice(product, unit, tier) {
        if (tier === 'WHOLESALE' && Number(unit?.wholesalePrice) > 0) return Number(unit.wholesalePrice);
        return Number(unit?.price) > 0 ? Number(unit.price) : Number(product.price || 0);
    }
    function recalc(draft, catalog, reprice = false, state) {
        if (reprice) for (const variantId of new Set(draft.lines.filter(x => !x.isPromotionGift).map(x => x.variantId))) {
            const product = catalog.products.find(x => x.id === variantId);
            if (!product) continue;
            const lines = draft.lines.filter(x => x.variantId === variantId && !x.isPromotionGift);
            const total = lines.reduce((sum, x) => sum + x.quantity * (x.multiplier || 1), 0);
            const packs = product.units.filter(x => x.factor >= 4 && total >= x.factor).sort((a, b) => b.factor - a.factor);
            for (const line of lines) {
                const unit = product.units.find(x => x.id === line.productUnitConversionId) || product.units.find(x => x.unitId === line.sellingUnitId);
                line.unitPrice = packs.length ? round(unitPrice(product, packs[0], draft.customerPriceTier) / packs[0].factor * line.multiplier, 6)
                    : unitPrice(product, unit, draft.customerPriceTier);
                line.originalUnitPrice = line.unitPrice;
                line.unitPrices = product.units.map(u => ({ unitName: u.unitName, factor: u.factor,
                    retailPrice: u.price, wholesalePrice: u.wholesalePrice, effectivePrice: unitPrice(product, u, draft.customerPriceTier),
                    isBaseUnit: u.isBaseUnit, isCurrentUnit: u.unitId === line.sellingUnitId, isEffectivePriceUnit: (packs[0] || unit)?.id === u.id }));
            }
        }
        if (state && catalog.promotions) applyPromotions(state, draft, catalog);
        for (const line of draft.lines) {
            line.baseQuantity = line.quantity * (line.multiplier || 1);
            const gross = Math.max(0, round(line.quantity * line.unitPrice));
            line.lineDiscount = Math.min(gross, Math.max(0, Number(line.lineDiscount || 0)));
            line.promotionDiscount = Math.min(gross - line.lineDiscount, Math.max(0, Number(line.promotionDiscount || 0)));
            line.lineTotal = Math.max(0, round(gross - line.lineDiscount - line.promotionDiscount));
        }
        draft.subtotal = round(draft.lines.reduce((sum, x) => sum + x.quantity * x.unitPrice, 0));
        const lineDiscount = round(draft.lines.reduce((sum, x) => sum + x.lineDiscount, 0));
        draft.promotionDiscountTotal = round(draft.lines.reduce((sum, x) => sum + x.promotionDiscount, 0));
        draft.comboDiscountTotal = Math.min(draft.comboDiscountTotal || 0, Math.max(0, draft.subtotal - lineDiscount - draft.promotionDiscountTotal));
        draft.orderDiscount = Math.min(Math.max(0, round(draft.orderDiscount || 0)), Math.max(0, draft.subtotal - lineDiscount - draft.promotionDiscountTotal - draft.comboDiscountTotal));
        draft.discountTotal = lineDiscount + draft.promotionDiscountTotal + draft.comboDiscountTotal + draft.orderDiscount + (draft.voucherDiscountTotal || 0);
        draft.grandTotal = Math.max(0, round(draft.subtotal - draft.discountTotal));
        draft.paidTotal = draft.payments.reduce((sum, x) => sum + x.amount, 0);
        draft.balanceDue = Math.max(0, draft.grandTotal - draft.paidTotal);
        draft.changeDue = Math.max(0, draft.paidTotal - draft.grandTotal);
        return draft;
    }
    function absorb(state, response) {
        if (response?.currentCart) state.currentId = response.currentCart.currentOrderId;
        const draft = response?.currentDraft || draftOf(response);
        if (draft) {
            const local = invoiceIntentOrder(state, draft.orderId);
            state.orders[draft.orderId] = { ...clone(draft),
                ...(local?.invoiceIntentRequired ? { invoiceIntentRequired: true } : {}),
                ...(local?.invoiceIntent ? { invoiceIntent: clone(local.invoiceIntent) } : {}) };
            if (draft.status === 0) state.currentId = draft.orderId;
            else if (state.currentId === draft.orderId) state.currentId = null;
        }
    }
    function applyPromotions(state, draft, catalog) {
        const tier = draft.customerPriceTier || 'RETAIL';
        const promotions = catalog.promotions.filter(p => !p.customerPriceTier || p.customerPriceTier.trim().toUpperCase() === tier)
            .sort((a, b) => b.priority - a.priority || b.id - a.id);
        const matches = (rule, line, minimum = true) => (!rule.productId || rule.productId === catalog.products.find(p => p.id === line.variantId)?.productId)
            && (!rule.variantId || rule.variantId === line.variantId)
            && (!rule.productUnitConversionId || rule.productUnitConversionId === line.productUnitConversionId)
            && (!minimum || !rule.minQuantity || line.quantity >= rule.minQuantity);
        const normal = draft.lines.filter(l => !l.isPromotionGift);
        const gifts = [];
        for (const line of normal) {
            line.promotionId = null; line.promotionName = null; line.promotionType = null; line.promotionDiscount = 0;
            line.comboPromotionId = null; line.comboPromotionName = null; line.comboPromotionNote = null; line.comboAllocatedDiscount = 0;
            const giftRule = promotions.find(p => p.type === 3 && p.items.some(i => matches(i, line)));
            if (giftRule?.buyQuantity > 0 && giftRule.getQuantity > 0 && line.unitPrice > 0) {
                const quantity = Math.floor(line.quantity / giftRule.buyQuantity) * giftRule.getQuantity;
                if (quantity > 0) {
                    const old = draft.lines.find(l => l.isPromotionGift && l.giftSourceLineId === line.lineId && l.giftPromotionId === giftRule.id);
                    gifts.push({ ...clone(line), lineId: old?.lineId || id(state), quantity, unitPrice: 0, originalUnitPrice: 0,
                        lineDiscount: 0, promotionDiscount: 0, lineTotal: 0, isPromotionGift: true,
                        giftSourceLineId: line.lineId, giftPromotionId: giftRule.id, giftPromotionName: giftRule.name,
                        giftPromotionNote: `Mua ${giftRule.buyQuantity} tặng ${giftRule.getQuantity}` });
                }
            }
            const discount = promotions.find(p => p.type === 1 && p.items.some(i => matches(i, line)));
            if (discount?.discountValue > 0 && line.unitPrice > 0) {
                const amount = discount.discountType === 1 ? line.quantity * line.unitPrice * discount.discountValue / 100
                    : discount.discountType === 2 ? line.quantity * discount.discountValue : 0;
                if (amount > 0) {
                    line.promotionDiscount = round(Math.min(line.quantity * line.unitPrice, amount));
                    line.promotionId = discount.id; line.promotionName = discount.name; line.promotionType = 1;
                }
            }
        }
        draft.lines = [...normal, ...gifts];
        draft.comboDiscountTotal = 0; draft.comboPromotionId = null; draft.comboPromotionName = null; draft.comboPromotionNote = null;
        const combos = [];
        for (const promotion of promotions.filter(p => p.type === 2 && p.comboFixedPrice > 0 && p.comboRules.length)) {
            let count = Infinity, price = 0; const involved = new Set();
            for (const rule of promotion.comboRules) {
                const lines = normal.filter(l => l.unitPrice > 0 && matches(rule, l, false));
                const quantity = lines.reduce((s, l) => s + l.quantity, 0), required = rule.requiredQuantity > 0 ? rule.requiredQuantity : 1;
                count = Math.min(count, Math.floor(quantity / required));
                if (quantity > 0) price += required * round(Math.max(0, lines.reduce((s, l) => s + l.quantity * l.unitPrice - l.promotionDiscount, 0)) / quantity);
                lines.forEach(l => involved.add(l));
            }
            const discount = round((round(price) - round(promotion.comboFixedPrice)) * count);
            if (Number.isFinite(discount) && discount > 0) combos.push({ promotion, discount, involved });
        }
        const best = combos.sort((a, b) => b.promotion.priority - a.promotion.priority || b.discount - a.discount || b.promotion.id - a.promotion.id)[0];
        if (best) {
            draft.comboDiscountTotal = best.discount; draft.comboPromotionId = best.promotion.id; draft.comboPromotionName = best.promotion.name;
            draft.comboPromotionNote = best.promotion.comboNote || `${best.promotion.name} giảm ${best.discount.toLocaleString('vi-VN')}`;
            for (const line of best.involved) { line.comboPromotionId = best.promotion.id; line.comboPromotionName = best.promotion.name;
                line.comboPromotionNote = draft.comboPromotionNote; line.comboAllocatedDiscount = round(best.discount / best.involved.size); }
        }
    }
    function summary(state, order) {
        return { ...order, lineCount: order.lines.length, totalQuantity: order.lines.reduce((s, x) => s + x.quantity, 0),
            isCurrent: state.currentId === order.orderId, subtotal: order.grandTotal, posShiftId: state.context.shiftId,
            shiftCode: state.context.shiftCode, terminalId: String(state.context.terminalId), terminalName: state.context.terminalName,
            heldByUserId: state.context.userId, heldByUserName: state.context.userName, isCurrentShift: true, isCurrentTerminal: true };
    }
    function screen(state) {
        return { currentCart: { currentOrderId: state.currentId, hasCurrentOrder: !!state.currentId }, currentDraft: state.orders[state.currentId] || null,
            draftOrders: Object.values(state.orders).filter(x => x.status === 0).map(x => summary(state, x)),
            heldOrders: Object.values(state.orders).filter(x => x.status === 1).map(x => summary(state, x)) };
    }
    function editable(draft) {
        if (draft.status !== 0) error('Chỉ được sửa giỏ đang bán.');
        if (draft.appliedRewardVouchers?.length)
            error('Giỏ có voucher cần server kiểm tra. Giữ giỏ này và tạo giỏ mới để tiếp tục bán offline.');
    }
    function addItem(state, catalog, draft, product, unit, qty, barcode) {
        permission(state, 'pos.order.create'); editable(draft);
        if (!product || !unit) error('Sản phẩm hoặc đơn vị chưa có trong dữ liệu offline của quầy.');
        if (!Number.isFinite(qty) || qty <= 0 || qty > maxPosQuantity) error('Số lượng không hợp lệ (tối đa 1.000.000).');
        let line = draft.lines.find(x => x.variantId === product.id && x.sellingUnitId === unit.unitId && !x.isPromotionGift);
        if (line && line.quantity + qty > maxPosQuantity) error('Tổng số lượng dòng hàng không được vượt quá 1.000.000.');
        if (line) line.quantity += qty;
        else {
            line = { lineId: id(state), variantId: product.id, productUnitConversionId: unit.id,
                itemName: product.productName, productVariantName: product.productVariantName, sku: product.sku,
                barcode: barcode || unit.barcodes[0] || null, scannedBarcode: barcode || unit.barcodes[0] || null,
                quantity: qty, unitPrice: unitPrice(product, unit, draft.customerPriceTier), multiplier: unit.factor,
                sellingUnitId: unit.unitId, sellingUnitName: unit.unitName, unitName: unit.unitName,
                baseUnitId: product.units.find(x => x.isBaseUnit)?.unitId, baseUnitName: product.units.find(x => x.isBaseUnit)?.unitName,
                lineDiscount: 0, promotionDiscount: 0, comboAllocatedDiscount: 0, unitPrices: [], hasImage: false };
            draft.lines.push(line);
        }
        return recalc(draft, catalog, true, state);
    }
    function addPayment(state, catalog, draft, body, at) {
        permission(state, 'pos.payment.create');
        const amount = Number(body.amount), method = Number(body.method);
        if (!body.clientRequestId || !Number.isSafeInteger(amount) || amount <= 0 || ![0, 1, 2, 3, 99].includes(method)) error('Khoản thu không hợp lệ.');
        const previous = draft.payments.find(x => x.clientRequestId === body.clientRequestId);
        if (previous) {
            if (previous.amount !== amount || previous.method !== ['Cash', 'BankTransfer', 'Card', 'EWallet'][method] && !(method === 99 && previous.method === 'Other')) error('Mã lần thu đã được dùng.');
            if ((previous.reference || '') !== (body.referenceCode || '').trim() || (previous.provider || '') !== (body.provider || '').trim()) error('Nội dung khoản thu đã thay đổi.');
            return draft;
        }
        if (draft.status !== 0) error('Đơn đã kết thúc.');
        if (method !== 0 && method !== 1 && amount > draft.balanceDue) error('Số tiền nhận vượt số còn thiếu.');
        draft.payments.push({ paymentId: id(state), clientRequestId: body.clientRequestId,
            method: ['Cash', 'BankTransfer', 'Card', 'EWallet'][method] || 'Other', amount,
            reference: body.referenceCode?.trim() || null, provider: body.provider?.trim() || null, createdAt: at, confirmationSource: 'offline-manual' });
        return recalc(draft, catalog);
    }
    function finalize(state, draft, at) {
        permission(state, 'pos.order.finalize');
        if (draft.status === 2) return draft;
        if (draft.status !== 0) error('Chỉ được chốt giỏ đang bán.');
        if (!draft.lines.length || draft.grandTotal <= 0 || draft.balanceDue > 0) error('Đơn chưa có hàng, tổng tiền không hợp lệ hoặc chưa nhận đủ tiền.');
        draft.invoiceIntentRequired = true;
        draft.status = 2; draft.completedAtUtc = at; draft.orderNumber = `OFF-${state.context.terminalId}-${draft.orderId}`;
        if (state.currentId === draft.orderId) state.currentId = null;
        return draft;
    }
    function read(state, catalog, url) {
        const path = url.pathname, p = url.searchParams;
        if (path === '/admin/pos/screen') return screen(state);
        if (path === '/admin/pos/cart/current') return { currentOrderId: state.currentId, hasCurrentOrder: !!state.currentId };
        if (path === '/admin/pos/orders/drafts') return screen(state).draftOrders;
        if (path === '/admin/pos/orders/held') return screen(state).heldOrders;
        if (path === '/admin/pos/products/search') {
            const term = normalize(p.get('keyword'));
            return catalog.products.filter(x => normalize(`${x.productName} ${x.productVariantName} ${x.sku} ${x.units.flatMap(u => u.barcodes).join(' ')}`).includes(term))
                .slice(0, Math.min(50, Number(p.get('take')) || 20)).map(x => {
                    const u = x.units.find(y => y.isDefaultForSale) || x.units.find(y => y.isBaseUnit) || x.units[0];
                    return { variantId: x.id, productId: x.productId, productName: x.productName, productVariantName: x.productVariantName,
                        displayName: x.productVariantName || x.productName, sku: x.sku, barcode: u?.barcodes[0], price: unitPrice(x, u, current(state).customerPriceTier),
                        isActive: true, onHandQty: Math.max(0, x.onHandQty), isNegativeStock: x.onHandQty < 0, hasImage: false,
                        unitOptions: x.units.filter(y => y.factor > 1 && !y.isBaseUnit).map(y => ({ productUnitConversionId: y.id, unitId: y.unitId,
                            unitName: y.unitName, factor: y.factor, price: unitPrice(x, y, current(state).customerPriceTier), barcode: y.barcodes[0],
                            availableQty: Math.max(0, Math.floor(x.onHandQty / y.factor)), isNegativeStock: x.onHandQty < 0 })) };
                });
        }
        if (path === '/admin/pos/customers/search') return [...catalog.customers, ...(state.customers || [])].filter(x => normalize(`${x.name} ${x.phone}`).includes(normalize(p.get('keyword')))).slice(0, 50);
        const orderId = Number(path.match(/^\/admin\/pos\/(?:orders\/)?(\d+)(?:\/receipt)?$/)?.[1]);
        if (orderId && state.orders[orderId]) return state.orders[orderId];
        error('Chức năng này cần kết nối server. Các đơn tại quầy vẫn được giữ.');
    }
    function invoiceIntentOrder(state, orderId) {
        return state.orders[orderId] || state.orders[mapId(state, 'order', orderId)] ||
            Object.values(state.orders).find(x => Number(mapId(state, 'order', x.orderId)) === Number(orderId));
    }
    function pendingInvoiceIntent(state) {
        return Object.values(state.orders).filter(x => x.status === 2 &&
            (x.invoiceIntentRequired || x.offline) && ![1, 2].includes(x.invoiceIntent?.route))
            .sort((a, b) => String(a.completedAtUtc || '').localeCompare(String(b.completedAtUtc || '')) || a.orderId - b.orderId)[0]?.orderId || null;
    }
    function confirmInvoiceIntent(state, operation, response) {
        const match = operation.url.match(/^\/admin\/pos\/(\d+)\/invoice-route$/);
        if (!match) return;
        const expectedId = Number(mapId(state, 'order', match[1]));
        const route = operation.body?.route;
        if (response?.success !== true || Number(response.orderId) !== expectedId ||
            response.route !== (route === 1 ? 'Automatic' : 'Manual'))
            error('Kết quả lựa chọn hóa đơn không khớp yêu cầu tại quầy. Cần đối soát trước khi gửi tiếp.');
        const order = invoiceIntentOrder(state, Number(match[1]));
        if (order) {
            order.invoiceIntentRequired = true;
            order.invoiceIntent = { route, operationId: operation.id, selectedAtUtc: operation.occurredAt,
                contextKey: state.key, pendingSync: false };
        }
    }
    function apply(state, catalog, operation) {
        const url = new URL(operation.url, 'https://pos.local'), path = url.pathname, p = url.searchParams;
        const body = operation.body || {}, method = operation.method, at = operation.occurredAt;
        if (method === 'GET') return clone(read(state, catalog, url));
        const intentRoute = path.match(/^\/admin\/pos\/(\d+)\/invoice-route$/);
        if (intentRoute) {
            permission(state, 'pos.order.finalize');
            if (method !== 'POST' || ![1, 2].includes(body.route) || state.key !== contextKey(state.context))
                error('Lựa chọn hóa đơn hoặc phiên POS không hợp lệ.');
            const order = invoiceIntentOrder(state, Number(intentRoute[1]));
            if (!order || order.status !== 2) error('Chỉ chọn hóa đơn cho đơn đã hoàn tất tại quầy.');
            if (order.invoiceIntent && (order.invoiceIntent.contextKey !== state.key || order.invoiceIntent.route !== body.route))
                error('Đơn đã có lựa chọn hóa đơn. Kết nối server và dùng chức năng chuyển phương thức của quản lý.');
            order.invoiceIntentRequired = true;
            order.invoiceIntent ||= { route: body.route, operationId: operation.id, selectedAtUtc: at,
                contextKey: state.key, pendingSync: true };
            return { success: true, orderId: order.orderId, route: body.route === 1 ? 'Automatic' : 'Manual',
                askBeforePrintingReceipt: order.askBeforePrintingReceipt === true,
                pendingSync: order.invoiceIntent.pendingSync };
        }
        if (pendingInvoiceIntent(state) && /\/(?:items|scan|payments|payment-and-finalize|manual-transfer|finalize)$/.test(path))
            error('Vui lòng lưu lựa chọn hóa đơn của đơn đã hoàn tất trước khi bán tiếp.');
        if (path === '/admin/pos/customers/quick-create') {
            permission(state, 'pos.order.create');
            const draft = current(state); editable(draft);
            const name = String(body.name || '').trim(), phone = String(body.phone || '').trim();
            if (!name) error('Tên khách hàng không được để trống.');
            state.customers ||= [];
            if (phone && [...catalog.customers, ...state.customers].some(x => x.phone === phone)) error('Số điện thoại đã tồn tại.');
            const customer = { customerId: id(state), name, phone: phone || null, address: body.address?.trim() || null,
                priceTier: String(body.priceTier || '').toUpperCase() === 'WHOLESALE' ? 'WHOLESALE' : 'RETAIL' };
            state.customers.push(customer);
            return setCustomer(state, catalog, draft, customer.customerId, false);
        }
        if (path === '/admin/pos/cart/current/new' || path === '/admin/pos/draft') {
            const draft = newDraft(state, body);
            if (body.customerId) setCustomer(state, catalog, draft, body.customerId, false);
            return { success: true, orderId: draft.orderId };
        }
        if (path === '/admin/pos/cart/ensure') return state.currentId ? current(state) : newDraft(state);
        if (path === '/admin/pos/cart/current') {
            const draft = state.orders[body.orderId];
            if (!draft || draft.status !== 0) error('Giỏ này chưa được lưu tại quầy.');
            state.currentId = body.orderId; return { success: true };
        }
        const resume = path.match(/^\/admin\/pos\/orders\/(\d+)\/resume$/);
        if (resume) {
            permission(state, 'pos.order.hold');
            const draft = state.orders[Number(resume[1])];
            if (!draft || draft.status !== 1) error('Đơn giữ chưa có tại quầy.');
            draft.status = 0; state.currentId = draft.orderId; return { success: true, orderId: draft.orderId };
        }
        const lineRoute = path.match(/^\/admin\/pos\/lines\/(\d+)(\/discount)?$/);
        if (lineRoute) {
            const draft = Object.values(state.orders).find(x => x.lines.some(l => l.lineId === Number(lineRoute[1])));
            if (!draft) error('Không tìm thấy dòng hàng tại quầy.');
            permission(state, 'pos.order.create');
            editable(draft);
            const line = draft.lines.find(x => x.lineId === Number(lineRoute[1]));
            if (line.isPromotionGift) error('Hàng tặng được tính theo dòng mua; hãy sửa số lượng dòng mua.');
            if (lineRoute[2]) { permission(state, 'pos.order.discount'); line.lineDiscount = Number(body.discountAmount || 0);
                if (!Number.isFinite(line.lineDiscount) || line.lineDiscount < 0 || line.lineDiscount > round(line.quantity * line.unitPrice)) error('Giảm giá dòng hàng không hợp lệ.'); }
            else if (method === 'DELETE') draft.lines = draft.lines.filter(x => x !== line);
            else { const qty = Number(p.get('qty')); if (!Number.isFinite(qty) || qty <= 0 || qty > maxPosQuantity) error('Số lượng không hợp lệ (tối đa 1.000.000).'); line.quantity = qty; }
            return recalc(draft, catalog, true, state);
        }
        const paymentRoute = path.match(/^\/admin\/pos\/payments\/(\d+)$/);
        if (paymentRoute && method === 'DELETE') {
            permission(state, 'pos.payment.create');
            const draft = Object.values(state.orders).find(x => x.payments.some(v => v.paymentId === Number(paymentRoute[1])));
            if (!draft || draft.status !== 0) error('Không thể xóa khoản thu của đơn đã kết thúc.');
            draft.payments = draft.payments.filter(x => x.paymentId !== Number(paymentRoute[1])); return recalc(draft, catalog);
        }
        const targetId = Number(path.match(/^\/admin\/pos\/(\d+)\//)?.[1] || body.orderId || state.currentId);
        const draft = state.orders[targetId];
        if (!draft) error('Không tìm thấy giỏ tại quầy.');
        if (path.endsWith('/items') || path.endsWith('/scan')) {
            let product, unit;
            if (path.endsWith('/scan')) {
                product = catalog.products.find(x => x.units.some(u => u.barcodes.includes(String(body.barcode || '').trim())));
                unit = product?.units.find(u => u.barcodes.includes(String(body.barcode || '').trim()));
            } else {
                product = catalog.products.find(x => x.id === Number(p.get('variantId')));
                unit = product?.units.find(x => x.id === Number(p.get('productUnitConversionId'))) || product?.units.find(x => x.isDefaultForSale) || product?.units.find(x => x.isBaseUnit) || product?.units[0];
            }
            return addItem(state, catalog, draft, product, unit, Number(body.quantity || body.qty || p.get('qty') || 1), body.barcode);
        }
        if (path.endsWith('/payments') || path.endsWith('/payment-and-finalize')) {
            addPayment(state, catalog, draft, body, at);
            if (path.endsWith('/payment-and-finalize')) {
                const finalized = draft.balanceDue === 0;
                if (finalized) finalize(state, draft, at);
                return { success: true, finalized, orderId: draft.orderId, draft };
            }
            return draft;
        }
        if (path.endsWith('/offline/manual-transfer')) {
            addPayment(state, catalog, draft, { ...body, method: 1, provider: 'OFFLINE-MANUAL' }, at);
            const finalized = draft.balanceDue === 0;
            if (finalized) finalize(state, draft, at);
            return { orderId: draft.orderId, finalized, draft, paidAmount: body.amount, paidTotal: draft.paidTotal,
                remainingAmount: draft.balanceDue, confirmationSource: 'offline-manual', printUrl: finalized ? `/admin/pos/orders/${draft.orderId}/print` : null };
        }
        if (path.endsWith('/finalize')) return { success: true, orderId: draft.orderId, data: finalize(state, draft, at) };
        if (path.endsWith('/clear-lines')) {
            permission(state, 'pos.order.create');
            editable(draft);
            if (draft.payments.length) error('Giỏ đã nhận tiền; cần xử lý khoản thu trước khi xóa toàn bộ sản phẩm.');
            draft.lines = [];
            return recalc(draft, catalog, true, state);
        }
        if (path.endsWith('/hold')) {
            permission(state, 'pos.order.hold'); draft.status = 1; draft.holdNote = body.holdNote || null; draft.heldAtUtc = at;
            draft.holdCode = `OFF-${state.context.terminalId}-${draft.orderId}`;
            const next = newDraft(state);
            return { heldOrderId: draft.orderId, holdCode: draft.holdCode, newDraftOrderId: next.orderId, message: 'Đã giữ đơn tại quầy.' };
        }
        if (path.endsWith('/cancel')) {
            permission(state, 'pos.order.create');
            if (draft.payments.length) error('Giỏ đã nhận tiền; cần xử lý khoản thu trước khi hủy.');
            draft.status = 3; state.currentId = null; return { success: true };
        }
        if (path.endsWith('/note')) { permission(state, 'pos.order.create'); if (draft.status !== 0) error('Giỏ đã kết thúc.'); draft.note = body.note?.trim() || null; return draft; }
        if (path.endsWith('/discount')) { permission(state, 'pos.order.discount'); editable(draft); draft.orderDiscount = Number(body.discountAmount || 0); if (!Number.isFinite(draft.orderDiscount) || draft.orderDiscount < 0 || draft.orderDiscount > draft.subtotal) error('Giảm giá không hợp lệ.'); return recalc(draft, catalog); }
        const customerRoute = path.match(/\/customer\/(\d+)$/);
        if (customerRoute) return setCustomer(state, catalog, draft, Number(customerRoute[1]), body.repriceExistingLines === true);
        if (path.endsWith('/customer') && method === 'DELETE') return setCustomer(state, catalog, draft, null, false);
        error('Chức năng này cần kết nối server. Các đơn tại quầy vẫn được giữ.');
    }
    function setCustomer(state, catalog, draft, customerId, reprice) {
        permission(state, 'pos.order.create'); editable(draft);
        const customer = customerId ? [...catalog.customers, ...(state.customers || [])].find(x => x.customerId === customerId) : null;
        if (customerId && !customer) error('Khách hàng chưa có trong dữ liệu offline.');
        draft.customerId = customerId; draft.customerName = customer?.name || null; draft.customerPhone = customer?.phone || null;
        draft.customerPriceTier = customer?.priceTier || 'RETAIL'; draft.rewardSummary = null;
        draft.askBeforePrintingReceipt = customer?.askBeforePrintingReceipt === true;
        return recalc(draft, catalog, reprice, state);
    }
    function mapId(state, kind, value) { return state.maps[kind]?.[value] || value; }
    function linesSignature(draft) {
        return draft.lines.map(l => `${l.variantId}:${l.sellingUnitId || 0}:${l.isPromotionGift ? 1 : 0}:${round(l.quantity, 4).toFixed(4).replace(/\.?0+$/, '')}`).sort().join('|');
    }
    function translate(state, op) {
        const intentRoute = op.url.match(/^\/admin\/pos\/(\d+)\/invoice-route$/);
        if (intentRoute && Number(intentRoute[1]) >= 1500000000 &&
            Number(mapId(state, 'order', intentRoute[1])) === Number(intentRoute[1]))
            error('Chưa ánh xạ được đơn offline. Phải đồng bộ đơn trước lựa chọn hóa đơn.');
        let url = op.url.replace(/(\/admin\/pos\/(?:orders\/)?)((?:\d)+)(?=\/|\?|$)/, (_, a, b) => a + mapId(state, 'order', b))
            .replace(/(\/lines\/)(\d+)/, (_, a, b) => a + mapId(state, 'line', b))
            .replace(/(\/payments\/)(\d+)/, (_, a, b) => a + mapId(state, 'payment', b))
            .replace(/(\/customer\/)(\d+)/, (_, a, b) => a + mapId(state, 'customer', b));
        const body = clone(op.body);
        if (body?.orderId) body.orderId = Number(mapId(state, 'order', body.orderId));
        if (body?.customerId) body.customerId = Number(mapId(state, 'customer', body.customerId));
        return { url, body, expectedOrderId: op.expectedOrderId ? Number(mapId(state, 'order', op.expectedOrderId)) : null };
    }
    function learn(state, local, remote) {
        if (!local || !remote) return;
        for (const key of ['orderId', 'newDraftOrderId', 'heldOrderId']) if (local[key] && remote[key] && local[key] !== remote[key]) state.maps.order[local[key]] = remote[key];
        const l = draftOf(local), r = draftOf(remote);
        if (l && r) {
            if (l.customerId && r.customerId && l.customerId !== r.customerId) state.maps.customer[l.customerId] = r.customerId;
            if (l.orderId !== r.orderId) state.maps.order[l.orderId] = r.orderId;
            for (const line of l.lines) {
                const match = r.lines.find(x => x.variantId === line.variantId && x.sellingUnitId === line.sellingUnitId && !!x.isPromotionGift === !!line.isPromotionGift);
                if (match && line.lineId !== match.lineId) state.maps.line[line.lineId] = match.lineId;
            }
            const used = new Set();
            for (const payment of l.payments) {
                const match = r.payments.find(x => !used.has(x.paymentId) && x.amount === payment.amount && x.method === payment.method && (x.reference || '') === (payment.reference || ''));
                if (match) { used.add(match.paymentId); if (payment.paymentId !== match.paymentId) state.maps.payment[payment.paymentId] = match.paymentId; }
            }
        }
    }
    function vietQr(bankBin, account, amount, content) {
        if (!/^\d{6}$/.test(bankBin) || !/^[A-Za-z0-9]{1,30}$/.test(account) || !Number.isSafeInteger(amount) || amount <= 0 || !/^[A-Za-z0-9 -]{1,40}$/.test(content)) error('Thông tin QR không hợp lệ.');
        const tlv = (tag, value) => tag + String(value.length).padStart(2, '0') + value;
        let text = tlv('00', '01') + tlv('01', '12') + tlv('38', tlv('00', 'A000000727') + tlv('01', tlv('00', bankBin) + tlv('01', account)) + tlv('02', 'QRIBFTTA'))
            + tlv('53', '704') + tlv('54', String(amount)) + tlv('58', 'VN') + tlv('62', tlv('08', content)) + '6304';
        let crc = 0xffff;
        for (const c of text) { crc ^= c.charCodeAt(0) << 8; for (let i = 0; i < 8; i++) crc = ((crc & 0x8000) ? (crc << 1) ^ 0x1021 : crc << 1) & 0xffff; }
        return text + crc.toString(16).toUpperCase().padStart(4, '0');
    }
    return { initial, clone, contextKey, absorb, screen, current, newDraft, recalc, apply, read, translate, learn, draftOf, mapId, vietQr, permission, linesSignature, invoiceIntentOrder, pendingInvoiceIntent, confirmInvoiceIntent };
});
