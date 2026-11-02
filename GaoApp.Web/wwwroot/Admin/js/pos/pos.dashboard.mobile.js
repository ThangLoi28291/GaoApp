/* UI-07d: presentation only. The existing dashboard owns refresh, snapshots, totals and charts. */
(() => {
    'use strict';
    function init() {
        const root = document.getElementById('posDashboardV2');
        if (!root) return;
        const content = document.getElementById('dashboardContent');
        const phone = matchMedia('(max-width: 767.98px)');
        const moves = [], wrappers = [];
        let mounted = false;
        function move(node, parent, before = null) {
            const marker = document.createComment('UI-07d original position');
            node.before(marker); moves.push({node, marker}); parent.insertBefore(node, before);
        }
        function disclosure(title, before) {
            const details = document.createElement('details'), summary = document.createElement('summary');
            details.className = 'pos-dashboard-phone-group'; summary.textContent = title;
            details.append(summary); before.before(details); wrappers.push(details); return details;
        }
        function sync() {
            if (phone.matches && !mounted) {
                mounted = true;
                const main = content.querySelector('.pos-dashboard-main-grid');
                const secondary = content.querySelector('.pos-dashboard-secondary-grid');
                const payment = content.querySelector('.pos-dashboard-payment-card');
                const health = content.querySelector('.pos-dashboard-shift-health');
                const attention = content.querySelector('.pos-dashboard-attention');
                const actions = content.querySelector('.pos-dashboard-actions');
                move(attention, content, main);
                move(payment, content, main);
                move(health, disclosure('Chi tiết quỹ và thu / chi', main));
                if (actions.querySelector('a[data-menu-id]')) move(actions, disclosure('Lối tắt POS của cửa hàng', payment));
                const hourly = disclosure('Diễn biến theo giờ', main);
                const hint = document.createElement('p');
                hint.className = 'pos-dashboard-phone-hint';
                hint.textContent = 'Vuốt ngang biểu đồ để xem đầy đủ các giờ có phát sinh.';
                hourly.append(hint); move(main, hourly); move(secondary, hourly);
                hourly.addEventListener('toggle', () => {
                    // Ask the original chart owner to measure its now-visible container; no network or new data owner.
                    if (hourly.open && phone.matches && !content.hidden && root.getAttribute('aria-busy') !== 'true')
                        window.dispatchEvent(new Event('resize'));
                });
            } else if (!phone.matches && mounted) {
                mounted = false;
                for (const {node, marker} of moves.reverse()) marker.replaceWith(node);
                moves.length = 0;
                for (const wrapper of wrappers) wrapper.remove();
                wrappers.length = 0;
            }
        }
        phone.addEventListener('change', sync);
        sync();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init, {once: true});
    else init();
})();
