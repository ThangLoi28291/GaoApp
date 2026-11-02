(function () {
    'use strict';
    function initialize() {
        const root = document.querySelector('[data-mobile-order-detail]');
        if (!root) return;
        const media = window.matchMedia('(max-width: 767.98px)');
        const moved = [], generated = [];
        const summaryTexts = new Map();
        let summaryObserver;
        let active = false;
        function element(tag, className, text) {
            const node = document.createElement(tag);
            node.className = className;
            if (text) node.textContent = text;
            return node;
        }
        function move(node, destination) {
            if (!node) return;
            const anchor = document.createComment('order-detail-position');
            node.before(anchor);
            moved.push({ node, anchor });
            destination.append(node);
        }
        function disclosure(title, destination) {
            const details = element('details', 'od-mobile-disclosure');
            details.append(element('summary', '', title));
            destination.append(details);
            return details;
        }
        function contextCard(title, className, destination) {
            const card = element('section', 'od-mobile-context ' + className);
            card.append(element('h5', '', title));
            destination.append(card);
            return card;
        }
        function syncReturnSummary() {
            const name = root.querySelector('[data-order-field="customer"] .ord-info-value')?.textContent;
            const refunded = root.querySelector('[data-order-refunded-value]')?.textContent;
            if (!active) return;
            for (const [selector, key, text] of [
                ['[data-return-customer-order]', 'returnCustomerOrder', name],
                ['[data-return-refunded-order]', 'returnRefundedOrder', refunded]
            ]) {
                if (!text) continue;
                document.querySelectorAll(selector).forEach(node => {
                    if (node.dataset[key] !== root.dataset.orderId) return;
                    if (!summaryTexts.has(node)) summaryTexts.set(node, node.textContent);
                    if (node.textContent !== text) node.textContent = text;
                });
            }
            for (const node of summaryTexts.keys()) if (!node.isConnected) summaryTexts.delete(node);
        }
        function enter() {
            if (active) return;
            active = true;
            root.dataset.mobileOrderDetailMode = 'phone';
            const shell = element('div', 'od-mobile-shell');
            root.prepend(shell); generated.push(shell);
            const hero = element('section', 'od-mobile-hero');
            hero.setAttribute('aria-label', 'Tóm tắt đơn hàng');
            shell.append(hero);
            for (const key of ['identity', 'status', 'customer']) move(root.querySelector('[data-order-field="' + key + '"]'), hero);
            move(root.querySelector('.ord-stat.primary'), hero);
            const delivery = root.querySelector('[data-order-delivery]');
            if (delivery) move(delivery, disclosure('Hồ sơ giao hàng liên kết', shell));
            const tabs = element('div', 'od-mobile-tabs');
            tabs.setAttribute('role', 'tablist'); tabs.setAttribute('aria-label', 'Nội dung đơn hàng');
            shell.append(tabs);
            const panels = [], buttons = [];
            function select(index, focus) {
                panels.forEach((panel, i) => { panel.hidden = i !== index; });
                buttons.forEach((button, i) => {
                    button.setAttribute('aria-selected', String(i === index));
                    button.tabIndex = i === index ? 0 : -1;
                });
                if (focus) buttons[index].focus();
            }
            ['Hàng hóa', 'Thanh toán', 'Lịch sử'].forEach((label, index) => {
                const button = element('button', 'od-mobile-tab', label);
                button.type = 'button'; button.id = 'od-mobile-tab-' + index;
                button.setAttribute('role', 'tab'); button.setAttribute('aria-controls', 'od-mobile-panel-' + index);
                const panel = element('section', 'od-mobile-panel');
                panel.id = 'od-mobile-panel-' + index; panel.setAttribute('role', 'tabpanel');
                panel.setAttribute('aria-labelledby', button.id); panel.tabIndex = 0;
                buttons.push(button); panels.push(panel); tabs.append(button); shell.append(panel);
                button.addEventListener('click', () => select(index, false));
                button.addEventListener('keydown', event => {
                    const target = event.key === 'ArrowRight' ? (index + 1) % 3 : event.key === 'ArrowLeft' ? (index + 2) % 3
                        : event.key === 'Home' ? 0 : event.key === 'End' ? 2 : null;
                    if (target === null) return;
                    event.preventDefault(); select(target, true);
                });
            });
            // Move the existing nodes: preserve server conditions, values, CSP actions and listeners.
            move(root.querySelector('[data-order-goods]'), panels[0]);
            move(root.querySelector('.ord-price-breakdown'), panels[1]);
            move(root.querySelector('.ord-overview'), panels[1]);
            for (const key of ['deposit', 'voucher']) move(root.querySelector('[data-order-field="' + key + '"]'), panels[1]);
            move(root.querySelector('[data-order-information]'), panels[2]);
            const legal = root.querySelector('[data-order-legal]');
            if (legal) move(legal, disclosure('Phân bổ và hóa đơn theo HKD', panels[1]));
            // Hold original groups out of the phone accessibility tree. Move only authorized
            // existing controls into their context; desktop restores every node, including denied ones.
            const held = element('div', 'od-mobile-held'); held.hidden = true; held.inert = true; shell.append(held);
            move(document.querySelector('.order-actions-modern'), held);
            move(document.querySelector('[data-order-utilities]'), held);
            const allowed = name => root.dataset[name] === 'true';
            const returnButton = held.querySelector('[data-csp-action="return-refund"]');
            const restock = held.querySelector('[data-order-utility="restock"]');
            if (returnButton && allowed('mobileCanReturn')) {
                const card = contextCard('Sau bán hàng', 'od-mobile-after-sale', panels[0]);
                panels[0].prepend(card);
                move(returnButton, card);
            }
            const payment = root.querySelector('.ord-payment-section');
            const bank = allowed('mobileCanViewBank') ? held.querySelector('[data-order-utility="bank"]') : null;
            const request = allowed('mobileCanOpenPaymentRequests') ? held.querySelector('[data-order-utility="payment-request"]') : null;
            if (payment || bank || request) {
                const card = contextCard('Thanh toán của đơn', 'od-mobile-payment-actions', panels[1]);
                panels[1].prepend(card);
                move(payment, card); move(bank, card); move(request, card);
            }
            const history = held.querySelector('[data-csp-action="return-history"]');
            if (history || restock) {
                const card = contextCard('Lịch sử sau bán', 'od-mobile-history-actions', panels[2]);
                panels[2].prepend(card); move(history, card); move(restock, card);
                panels[2].querySelectorAll('[data-order-information] [data-csp-action="return-history"]').forEach(node => move(node, held));
            }
            const voidButton = allowed('mobileCanVoid') ? held.querySelector('[data-csp-action="order-void"]') : null;
            const refundButton = allowed('mobileCanReturn') ? held.querySelector('[data-csp-action="order-refund"]') : null;
            if (voidButton || refundButton) {
                const actions = disclosure('Xử lý toàn bộ đơn', shell);
                actions.classList.add('od-mobile-special-actions');
                actions.append(element('p', '', 'Dùng khi cần hủy hoặc hoàn toàn bộ đơn. Nếu chỉ trả một phần hàng, chọn Trả hàng / hoàn tiền.'));
                move(voidButton, actions); move(refundButton, actions);
            }
            const dock = element('nav', 'od-mobile-dock'); dock.setAttribute('aria-label', 'Điều hướng đơn hàng');
            shell.append(dock);
            move(held.querySelector('.order-action-group-nav'), dock);
            if (!allowed('mobileCanReprint')) move(dock.querySelector('.btn-ord-primary'), held);
            // Phone summary uses the already-rendered receipt for this order. The legacy
            // renderer has no customer name and reads a different refund-total property.
            // Preserve all original form values and restore its legacy text on desktop.
            const modalBody = document.getElementById('returnRefundModalBody');
            if (modalBody) {
                summaryObserver = new MutationObserver(syncReturnSummary);
                summaryObserver.observe(modalBody, { childList: true, subtree: true });
                syncReturnSummary();
            }
            select(0, false);
        }
        function leave() {
            if (!active) return;
            summaryObserver?.disconnect(); summaryObserver = null;
            for (const [node, text] of summaryTexts) if (node.isConnected) node.textContent = text;
            summaryTexts.clear();
            // Reverse order also restores nested nodes and their exact sibling positions.
            moved.reverse().forEach(({ node, anchor }) => { anchor.replaceWith(node); });
            moved.length = 0;
            generated.forEach(node => node.remove()); generated.length = 0;
            delete root.dataset.mobileOrderDetailMode;
            active = false;
        }
        function update() { if (media.matches) enter(); else leave(); }
        media.addEventListener('change', update); update();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize, { once: true });
    else initialize();
}());
