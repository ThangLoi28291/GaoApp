/* UI-07a: arrange the original count controls on phones. No shift/drawer/draft API owner. */
(function () {
    'use strict';
    function mount() {
        const shell = document.querySelector('.shift-shell');
        if (!shell || shell.__shiftPhoneMounted) return;
        shell.__shiftPhoneMounted = true;
        const phone = matchMedia('(max-width: 767.98px)');
        const modals = [...document.querySelectorAll('.shift-modal')];
        const moves = [];
        const context = document.querySelector('.pos-context-bar');
        let contextMarker = null, contextDetails = null;
        let frame = 0;
        function move(node, target) {
            if (!node || !target) return;
            const marker = document.createComment('shift count original position');
            node.before(marker);
            moves.push({ node, marker });
            target.append(node);
        }
        function arrange(id) {
            const modal = document.getElementById(id);
            const layout = modal?.querySelector('.shift-open-cashier-layout');
            if (!layout) return;
            const left = layout.querySelector('.shift-open-left');
            const right = layout.querySelector('.shift-open-right');
            const blocks = [...left.querySelectorAll(':scope > .shift-open-block')];
            const direct = right.querySelector('.shift-open-direct-card');
            const middle = layout.querySelector('.shift-open-middle');
            if (id === 'openShiftModal') {
                // Keep warehouse and optional handover before the counted opening amount.
                move(blocks[0], layout); move(blocks[1], layout); move(direct, layout);
                move(middle, layout); blocks.slice(2).forEach(node => move(node, layout));
            } else {
                move(direct, layout); move(blocks[0], layout); move(middle, layout);
                blocks.slice(1).forEach(node => move(node, layout));
            }
            move(right.querySelector('.shift-open-summary-card'), layout);
            move(left.querySelector('.shift-open-warning-card'), layout);
            move(right.querySelector('.shift-open-warning-card'), layout);
        }
        function geometry() {
            frame = 0;
            const viewport = window.visualViewport;
            for (const modal of modals) {
                if (!phone.matches || !viewport || Math.abs(viewport.scale - 1) > .02) {
                    modal.style.removeProperty('--shift-visible-height');
                    modal.style.removeProperty('--shift-visible-top');
                    continue;
                }
                modal.style.setProperty('--shift-visible-height', Math.floor(viewport.height) + 'px');
                modal.style.setProperty('--shift-visible-top', Math.max(0, viewport.offsetTop) + 'px');
                const field = document.activeElement;
                const body = modal.querySelector('.modal-body');
                if (!modal.classList.contains('show') || !body?.contains(field)) continue;
                const box = field.getBoundingClientRect(), visible = body.getBoundingClientRect();
                if (box.bottom > visible.bottom - 12) body.scrollTop += box.bottom - visible.bottom + 12;
                else if (box.top < visible.top + 12) body.scrollTop -= visible.top - box.top + 12;
            }
        }
        function schedule() { if (!frame) frame = requestAnimationFrame(geometry); }
        function apply() {
            const focused = document.activeElement;
            if (phone.matches && !moves.length) {
                arrange('openShiftModal'); arrange('closeShiftModal');
                if (context) {
                    contextMarker = document.createComment('shift context original position');
                    context.before(contextMarker);
                    contextDetails = document.createElement('details');
                    contextDetails.className = 'shift-phone-context';
                    const summary = document.createElement('summary');
                    summary.textContent = 'Cửa hàng & kho bán';
                    contextDetails.append(summary, context);
                    contextMarker.after(contextDetails);
                }
            } else if (!phone.matches && moves.length) {
                for (const { node, marker } of moves) marker.replaceWith(node);
                moves.length = 0;
                if (contextMarker) {
                    contextMarker.replaceWith(context);
                    contextDetails.remove(); contextMarker = null; contextDetails = null;
                }
            }
            // Reparenting must retain the live input and its selection, never create a second control.
            if (focused && focused !== document.body && document.activeElement !== focused)
                focused.focus({ preventScroll: true });
            schedule();
        }
        phone.addEventListener('change', apply);
        window.addEventListener('resize', schedule);
        window.visualViewport?.addEventListener('resize', schedule);
        window.visualViewport?.addEventListener('scroll', schedule);
        for (const modal of modals) modal.addEventListener('shown.bs.modal', schedule);
        document.addEventListener('focusin', schedule);
        apply();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
    else mount();
})();
