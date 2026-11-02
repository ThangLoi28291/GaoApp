/* UI-20: presentation only. Native media and FilePond handlers retain all data, progress and commands. */
(function () {
    'use strict';
    function start() {
        const library = document.querySelector('[data-media-library]');
        const upload = document.getElementById('testUpload');
        if (!library && !upload) return;
        const home = upload?.parentElement;
        const phone = matchMedia('(max-width: 767.98px)');
        const originals = new Map();
        let printing = false, queued = false, policyMove = null, uploadMove = null;
        function mark(node, name, value) {
            if (!node) return;
            let attributes = originals.get(node);
            if (!attributes) { attributes = new Map(); originals.set(node, attributes); }
            if (!attributes.has(name)) attributes.set(name, node.getAttribute(name));
            if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        }
        function restoreMove(move) {
            if (!move) return;
            if (move.anchor.parentNode) { move.anchor.parentNode.insertBefore(move.node, move.anchor); move.anchor.remove(); }
            move.wrapper?.remove();
        }
        function restore() {
            restoreMove(policyMove); policyMove = null;
            restoreMove(uploadMove); uploadMove = null;
            for (const [node, attributes] of originals) for (const [name, value] of attributes) {
                if (value === null) node.removeAttribute(name); else node.setAttribute(name, value);
            }
            originals.clear();
        }
        function paint() {
            if (printing || !phone.matches) { restore(); return; }
            if (library) {
                mark(library, 'data-media-workspace-phone', 'library');
                const header = document.querySelector('.gao-admin-page-heading-content .gds-page-header');
                mark(header, 'data-media-workspace-phone', 'header');
                const policy = library.querySelector('.media-policy'), collection = library.querySelector('.media-collection');
                if (!policyMove && policy && collection) {
                    const anchor = document.createComment('UI-20 original media policy position');
                    policy.parentNode.insertBefore(anchor, policy); collection.after(policy);
                    policyMove = { node: policy, anchor };
                }
            }
            if (home) {
                mark(home, 'data-media-workspace-phone', 'upload');
                const pond = home.querySelector('.filepond--root');
                if (pond && !uploadMove) {
                    const anchor = document.createComment('UI-20 original FilePond position');
                    pond.parentNode.insertBefore(anchor, pond);
                    const wrapper = document.createElement('section'); wrapper.className = 'media-phone-upload';
                    const title = document.createElement('h1'); title.textContent = 'Kiểm tra tải ảnh';
                    const note = document.createElement('p'); note.textContent = 'Chọn một hoặc nhiều ảnh để kiểm tra tải lên. Bạn có thể theo dõi tiến trình và hủy từng ảnh.';
                    wrapper.append(title, note); anchor.after(wrapper); wrapper.appendChild(pond);
                    uploadMove = { node: pond, anchor, wrapper };
                }
            }
        }
        function queue() {
            if (queued || printing) return;
            queued = true;
            requestAnimationFrame(() => { queued = false; paint(); });
        }
        if (home) new MutationObserver(queue).observe(home, { childList: true, subtree: true });
        phone.addEventListener('change', paint);
        addEventListener('beforeprint', () => { printing = true; restore(); });
        addEventListener('afterprint', () => { printing = false; paint(); });
        paint();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
