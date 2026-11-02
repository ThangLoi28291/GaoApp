(function () {
    'use strict';

    window.CustomerDisplayReceipt = {
        create({ app, onShow, onClose }) {
            const overlay = document.getElementById('cdReceiptOverlay');
            const paper = document.getElementById('cdReceiptPaper');
            const viewport = document.getElementById('cdReceiptViewport');
            const seen = new Set();
            let orderId = 0, request = null, version = 0, closeTimer = null, scrollFrame = null;
            let pauseUntil = 0;
            const money = value => Number(value || 0).toLocaleString('vi-VN', { maximumFractionDigits: 2 });
            const quantity = value => Number(value || 0).toLocaleString('vi-VN', { maximumFractionDigits: 4 });
            const escape = value => String(value ?? '').replace(/[&<>"']/g,
                char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
            const set = (id, value) => { document.getElementById(id).textContent = value; };
            const row = (label, value, cls = '') => `<div class="cd-bill-row ${cls}"><span>${escape(label)}</span><strong>${money(value)} ₫</strong></div>`;
            const methods = { 0: 'Tiền mặt', 1: 'Chuyển khoản', 2: 'Thẻ', Cash: 'Tiền mặt', BankTransfer: 'Chuyển khoản', Card: 'Thẻ' };

            function render(receipt) {
                const customer = window.CustomerDisplayCustomer.render(receipt, ['cdReceiptCustomer']);
                const timestamp = receipt.finalizedAtUtc || receipt.createdAtUtc || '';
                // SQL datetime2 can serialize UTC fields without a suffix; do not interpret them as browser-local time.
                const time = new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(timestamp) ? timestamp : timestamp + 'Z');
                const date = Number.isNaN(time.getTime()) ? '' : time.toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' });
                const number = receipt.orderNumber || '#' + receipt.orderId;
                const meta = (label, value) => `<span>${escape(label)}</span><strong>${escape(value)}</strong>`;
                // Use the committed receipt DTO, including its aggregate discount (already contains order/voucher discounts).
                paper.innerHTML = `
                    <div class="cd-bill-brand"><strong>${escape(receipt.storeName || app.dataset.storeName)}</strong>
                        ${receipt.storeAddress ? `<div>${escape(receipt.storeAddress)}</div>` : ''}
                        ${receipt.storePhone ? `<div>ĐT: ${escape(receipt.storePhone)}</div>` : ''}</div>
                    <div class="cd-bill-heading">HÓA ĐƠN BÁN HÀNG</div>
                    <div class="cd-bill-meta">${meta('Số bill', number)}${meta('Ngày', date)}
                        ${meta('Quầy', receipt.terminalName || receipt.terminalCode || app.dataset.terminalName)}
                        ${receipt.cashierName ? meta('Thu ngân', receipt.cashierName) : ''}
                        ${meta('Khách hàng', receipt.customerName || 'Khách lẻ')}
                        ${customer.phone ? meta('Điện thoại', customer.phone) : ''}
                        ${customer.points != null ? meta('Điểm hiện tại', money(customer.points) + ' điểm') : ''}
                        ${meta('Trạng thái', receipt.isCreditSale ? 'Đã chốt · Công nợ' : 'Đã thanh toán')}</div>
                    <table class="cd-bill-items"><thead><tr><th>SẢN PHẨM / SL × ĐƠN GIÁ</th><th>THÀNH TIỀN</th></tr></thead><tbody>
                        ${receipt.lines.map(line => `<tr data-receipt-line="${escape(line.lineId)}"><td>${escape(line.productVariantName || line.itemName || 'Sản phẩm')}
                            <small>${quantity(line.quantity)} ${escape(line.sellingUnitName || line.unitName || '')} × ${money(line.unitPrice)} ₫</small></td>
                            <td>${money(line.lineTotal)} ₫</td></tr>`).join('')}</tbody></table>
                    <div class="cd-bill-totals">${row('Tạm tính', receipt.subtotal)}
                        ${Number(receipt.discountTotal) > 0 ? row('Giảm giá', receipt.discountTotal) : ''}
                        ${row('TỔNG CỘNG', receipt.grandTotal, 'cd-bill-grand')}
                        ${Number(receipt.depositAmount) > 0 ? row('Trừ tiền cọc', receipt.depositAmount) : ''}
                        ${(receipt.payments || []).map(payment => row(methods[payment.method] || 'Thanh toán khác', payment.amount)).join('')}
                        ${row(Number(receipt.depositAmount) > 0 ? 'Đã thanh toán (gồm cọc)' : 'Đã thanh toán', receipt.paidTotal)}
                        ${Number(receipt.balanceDue) > 0 ? row('Còn công nợ', receipt.balanceDue) : ''}
                        ${row('Tiền thừa', receipt.changeDue)}</div>
                    <div class="cd-bill-footer">Cảm ơn quý khách. Hẹn gặp lại!<br />${escape(number)}</div>`;
                paper.dataset.orderId = String(receipt.orderId);
                set('cdReceiptTitle', receipt.isCreditSale ? 'Đã chốt đơn công nợ.' : 'Thanh toán thành công.');
                set('cdReceiptTotal', money(receipt.grandTotal) + ' ₫');
                set('cdReceiptChange', money(receipt.changeDue) + ' ₫');
                set('cdReceiptBalance', money(receipt.balanceDue) + ' ₫');
                document.getElementById('cdReceiptChangeRow').hidden = Number(receipt.changeDue) <= 0;
                document.getElementById('cdReceiptBalanceRow').hidden = Number(receipt.balanceDue) <= 0;
            }

            function close() {
                version++;
                request?.abort(); request = null;
                clearTimeout(closeTimer); cancelAnimationFrame(scrollFrame);
                orderId = 0;
                overlay.hidden = true;
                paper.classList.remove('is-feeding');
                onClose();
            }

            function feed() {
                viewport.scrollTop = 0;
                paper.classList.remove('is-feeding');
                void paper.offsetWidth;
                paper.classList.add('is-feeding');
                const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
                const overflow = Math.max(0, viewport.scrollHeight - viewport.clientHeight);
                clearTimeout(closeTimer);
                closeTimer = setTimeout(close, Math.min(60000, Math.max(20000, overflow / 40 * 1000 + 10000)));
                if (reducedMotion) return;
                const start = performance.now() + 3800;
                let last = start;
                function scroll(now) {
                    if (now >= start && now >= pauseUntil && !viewport.matches(':hover, :focus-within')) {
                        viewport.scrollTop += Math.min(50, Math.max(0, now - last)) * .04;
                    }
                    last = now;
                    scrollFrame = requestAnimationFrame(scroll);
                }
                scrollFrame = requestAnimationFrame(scroll);
            }
            viewport.addEventListener('wheel', () => { pauseUntil = performance.now() + 6000; }, { passive: true });
            viewport.addEventListener('touchstart', () => { pauseUntil = performance.now() + 6000; }, { passive: true });

            async function show(id) {
                id = Number(id);
                if (!Number.isInteger(id) || id <= 0 || seen.has(id) || orderId === id) return;
                close();
                orderId = id;
                const currentVersion = version;
                const controller = new AbortController(); request = controller;
                try {
                    const response = await fetch(`/admin/pos/orders/${id}/receipt`, {
                        credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
                        headers: { Accept: 'application/json' }
                    });
                    if (!response.ok || response.redirected) throw new Error('Receipt unavailable');
                    const receipt = await response.json();
                    if (version !== currentVersion) return;
                    if (Number(receipt.orderId) !== id || receipt.status !== 'Completed' || !Array.isArray(receipt.lines) || !receipt.lines.length)
                        throw new Error('Receipt is not a completed sale');
                    render(receipt);
                    request = null;
                    seen.add(id);
                    if (seen.size > 100) seen.delete(seen.values().next().value);
                    overlay.hidden = false;
                    onShow();
                    feed();
                } catch (error) {
                    if (version === currentVersion) close();
                    if (error.name !== 'AbortError') console.warn('Không tải được bill vừa chốt.');
                }
            }

            return {
                show, close,
                isActive: () => orderId > 0,
                onDraft(draft) {
                    // Empty new carts/polling/reset must not cut off the bill. The next purchase takes priority.
                    if (orderId && draft?.lines?.length && Number(draft.orderId) !== orderId) close();
                },
                onFinalized(event) {
                    // order_finalized is a server-only store broadcast: bind it to this exact counter.
                    if (String(event.storeId) === String(app.dataset.storeId) &&
                        String(event.terminalId) === String(app.dataset.terminalId)) void show(event.orderId);
                }
            };
        }
    };
})();
