/* UI-15: presentation only; native reward and voucher owners retain every command and value. */
(function () {
    'use strict';
    function start() {
        const root = document.querySelector('[data-reward-settings-page], [data-reward-voucher-index]');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map();
        const kind = root.matches('[data-reward-settings-page]') ? 'settings' : 'vouchers';
        let sections = null, printing = false;
        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restore() {
            sections?.remove(); sections = null;
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function settingsSections() {
            if (kind !== 'settings' || sections) return;
            const cards = root.querySelectorAll('.reward-settings-main > .reward-settings-card');
            const targets = [[cards[0], 'Điều kiện'], [cards[1], 'Trạng thái'], [cards[2], 'Quy đổi'], [root.querySelector('.reward-settings-preview-card'), 'Xem trước']].filter(([node]) => node);
            sections = document.createElement('nav');
            sections.className = 'rewards-phone-sections';
            sections.setAttribute('aria-label', 'Các phần cấu hình tích điểm');
            targets.forEach(([node, title], index) => {
                if (!node.id) mark(node, 'id', 'rewards-phone-section-' + index);
                mark(node, 'data-rewards-phone-section', '');
                const link = document.createElement('a'); link.href = '#' + node.id; link.textContent = title; sections.append(link);
            });
            root.querySelector('#rewardSettingsForm')?.before(sections);
        }
        function apply() {
            if (printing || !phone.matches) { restore(); return; }
            mark(document.body, 'data-customer-rewards-phone', kind);
            mark(root, 'data-rewards-phone-root', kind);
            if (kind === 'vouchers') mark(document.querySelector('.gds-page-header'), 'data-rewards-phone-header', '');
            settingsSections();
        }
        phone.addEventListener('change', apply);
        window.addEventListener('beforeprint', () => { printing = true; restore(); });
        window.addEventListener('afterprint', () => { printing = false; apply(); });
        apply();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
