/* UI-20: phone presentation only. Native inbox owns polling, selection, drafts, media and commands. */
(function () {
    'use strict';
    function start() {
        const root = document.getElementById('customerInbox');
        if (!root) return;
        const conversation = document.getElementById('ciConversation');
        const heading = root.querySelector('.ci-heading');
        const notice = document.getElementById('ciNotice');
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map();
        let presentation = null, printing = false, queued = false, queueScroll = 0;
        let pane = 'queue', selectedId = null, hasConversation = false, infoOpen = false, optionsOpen = false, nativePending = false;

        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function button(className, text, label, onClick) {
            const node = document.createElement('button');
            node.type = 'button'; node.className = className; node.textContent = text;
            if (label) node.setAttribute('aria-label', label);
            node.addEventListener('click', onClick);
            return node;
        }
        function anchor(node) {
            const marker = document.createComment('UI-20 original inbox position');
            node.before(marker); return { node, marker };
        }
        function prepare() {
            if (presentation || !conversation) return;
            const customer = root.querySelector('.ci-customer'), actions = root.querySelector('.ci-actions');
            if (!customer || !actions) return;
            const meta = document.createElement('div'); meta.className = 'cim-meta';
            const moves = [customer, actions, document.getElementById('ciIdentity')].filter(Boolean).map(anchor);
            moves.forEach(({ node }) => meta.appendChild(node)); conversation.prepend(meta);
            const noticeMove = notice ? anchor(notice) : null;
            const back = button('cim-back', '‹', 'Quay lại danh sách hội thoại', () => {
                pane = 'queue'; paint();
                window.scrollTo({ top: queueScroll, behavior: 'instant' });
                root.querySelector('#ciList [aria-current="true"]')?.focus({ preventScroll: true });
            });
            const info = button('cim-info', 'Thông tin', 'Thông tin và thao tác hội thoại', () => { infoOpen = !infoOpen; paint(); });
            const options = button('cim-options', 'Thiết lập và liên kết', null, () => { optionsOpen = !optionsOpen; paint(); });
            const pending = document.createElement('p'); pending.className = 'cim-pending'; pending.setAttribute('role', 'status');
            pending.textContent = 'Đang xử lý · chờ xác nhận…'; meta.after(pending);
            const resume = button('cim-resume', 'Tiếp tục hội thoại', null, () => {
                if (!hasConversation || conversation.hidden) return;
                queueScroll = window.scrollY; pane = 'conversation'; paint(); back.focus({ preventScroll: true });
            });
            customer.appendChild(back); actions.appendChild(info); heading?.appendChild(options);
            root.insertBefore(resume, root.querySelector('.ci-grid'));
            presentation = { meta, moves, noticeMove, back, info, options, resume, pending };
        }
        function restore() {
            if (presentation) {
                const { meta, moves, noticeMove, back, info, options, resume, pending } = presentation;
                [back, info, options, resume, pending].forEach(node => node.remove());
                if (noticeMove?.marker.parentNode) { noticeMove.marker.before(noticeMove.node); noticeMove.marker.remove(); }
                moves.forEach(({ node, marker }) => { if (marker.parentNode) { marker.before(node); marker.remove(); } });
                meta.remove(); presentation = null;
            }
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function paint() {
            if (printing || !phone.matches) { restore(); return; }
            if (conversation?.hidden && hasConversation) { hasConversation = false; selectedId = null; pane = 'queue'; }
            prepare();
            if (!presentation) return;
            mark(root, 'data-ci-phone', '');
            mark(root, 'data-ci-phone-pane', hasConversation ? pane : 'queue');
            mark(root, 'data-ci-phone-selected', String(hasConversation));
            mark(root, 'data-ci-phone-info-open', String(infoOpen));
            mark(root, 'data-ci-phone-options-open', String(optionsOpen));
            mark(root, 'data-ci-phone-pending', String(nativePending));
            presentation.info.setAttribute('aria-expanded', String(infoOpen));
            presentation.options.setAttribute('aria-expanded', String(optionsOpen));
            const name = document.getElementById('ciName')?.textContent.trim();
            const resumeText = name ? 'Tiếp tục: ' + name : 'Tiếp tục hội thoại';
            if (presentation.resume.textContent !== resumeText) presentation.resume.textContent = resumeText;
            if (presentation.noticeMove) {
                const target = hasConversation && pane === 'conversation' ? conversation : presentation.noticeMove.marker.parentNode;
                if (notice.parentNode !== target) {
                    if (target === conversation) conversation.insertBefore(notice, presentation.meta);
                    else presentation.noticeMove.marker.before(notice);
                }
            }
        }
        function queue() {
            if (queued || printing) return;
            queued = true; requestAnimationFrame(() => { queued = false; paint(); });
        }
        document.addEventListener('gao:cskh-staff', event => {
            const current = event.detail?.conversation;
            nativePending = !!event.detail?.pending;
            if (!current) { hasConversation = false; selectedId = null; pane = 'queue'; infoOpen = false; paint(); return; }
            const fresh = !hasConversation || selectedId !== null && selectedId !== current.id;
            selectedId = current.id; hasConversation = true;
            if (fresh) { queueScroll = window.scrollY; pane = 'conversation'; infoOpen = false; }
            paint();
        });
        // A native conversation may already have loaded before this deferred adapter starts.
        if (conversation && !conversation.hidden) {
            hasConversation = true; pane = 'conversation';
            selectedId = root.querySelector('#ciList [aria-current="true"]')?.dataset.id || null;
        }
        new MutationObserver(queue).observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['hidden'] });
        phone.addEventListener('change', paint);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; paint(); });
        paint();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
