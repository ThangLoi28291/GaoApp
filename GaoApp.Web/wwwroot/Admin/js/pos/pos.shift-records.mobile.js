/* UI-07b: phone disclosure/navigation/viewport only; original page scripts own all data and commands. */
(function () {
    'use strict';
    function mount() {
        const root = document.querySelector('.shift-history-shell, .handover-shell');
        if (!root || root.__shiftRecordsPhoneMounted) return;
        root.__shiftRecordsPhoneMounted = true;
        const phone = matchMedia('(max-width: 767.98px)');
        const history = root.classList.contains('shift-history-shell');
        const modals = [...document.querySelectorAll(history ? '.shift-history-modal' : '.handover-modal, #slipDetailModal')];
        const moves = [], disclosures = [];
        const context = document.querySelector('.pos-context-bar');
        const filter = root.querySelector(history ? '.filter-shell' : '.handover-filter');
        const listHint = history ? root.querySelector('.shift-history-card .shift-section-sub') : null;
        const originalListHint = listHint?.textContent;
        let bar = null, selectedId = null, listScroll = 0, frame = 0;
        function move(node, target) {
            if (!node || !target) return;
            const marker = document.createComment('shift record original position');
            node.before(marker); moves.push({ node, marker }); target.append(node);
        }
        function disclose(node, label) {
            if (!node) return;
            const details = document.createElement('details'); details.className = 'shift-records-disclosure';
            const summary = document.createElement('summary'); summary.textContent = label; details.append(summary);
            node.before(details); move(node, details); disclosures.push(details);
        }
        function back() {
            delete root.dataset.phoneHistoryView;
            window.scrollTo({ top: listScroll, behavior: 'instant' });
            if (selectedId) root.querySelector('[data-shift-id="' + selectedId + '"]')?.focus({ preventScroll: true });
        }
        function showSummary(event) {
            if (!phone.matches || !history || (event.type === 'keydown' && !['Enter', ' '].includes(event.key))) return;
            const row = event.target.closest('[data-shift-id]');
            if (!row || !/^[1-9]\d*$/.test(row.dataset.shiftId)) return;
            selectedId = row.dataset.shiftId; listScroll = window.scrollY;
            root.dataset.phoneHistoryView = 'detail';
            root.scrollIntoView({ block: 'start', behavior: 'instant' });
            bar.querySelector('button').focus({ preventScroll: true });
        }
        function geometry() {
            frame = 0;
            const vv = window.visualViewport;
            for (const modal of modals) {
                if (!phone.matches || !vv || Math.abs(vv.scale - 1) > .02) {
                    modal.style.removeProperty('--shift-records-height'); modal.style.removeProperty('--shift-records-top'); continue;
                }
                modal.style.setProperty('--shift-records-height', Math.floor(vv.height) + 'px');
                modal.style.setProperty('--shift-records-top', Math.max(0, vv.offsetTop) + 'px');
                const field = document.activeElement, body = modal.querySelector('.modal-body');
                if (!modal.classList.contains('show') || !body?.contains(field)) continue;
                const b = field.getBoundingClientRect(), visible = body.getBoundingClientRect();
                if (b.bottom > visible.bottom - 12) body.scrollTop += b.bottom - visible.bottom + 12;
                else if (b.top < visible.top + 12) body.scrollTop -= visible.top - b.top + 12;
            }
        }
        function schedule() { if (!frame) frame = requestAnimationFrame(geometry); }
        function apply() {
            const focused = document.activeElement;
            if (phone.matches && !disclosures.length) {
                if (listHint) listHint.textContent = 'Chạm vào một ca để mở chi tiết.';
                disclose(context, 'Cửa hàng & quầy đang dùng'); disclose(filter, 'Bộ lọc ' + (history ? 'lịch sử ca' : 'phiếu nhận ca'));
                if (history) {
                    bar = document.createElement('div'); bar.className = 'shift-history-phone-bar';
                    const button = document.createElement('button'); button.type = 'button'; button.textContent = '‹ Danh sách ca';
                    button.className = 'btn btn-outline-primary'; button.addEventListener('click', back);
                    const title = document.createElement('strong'); title.textContent = 'Chi tiết ca'; bar.append(button, title); root.prepend(bar);
                } else {
                    const layout = document.querySelector('#createSlipModal .shift-open-cashier-layout');
                    const blocks = [...layout.querySelectorAll('.shift-open-left > .shift-open-block')];
                    move(blocks[0], layout); move(blocks[1], layout); move(blocks[2], layout);
                    move(layout.querySelector('.shift-open-middle'), layout); move(blocks[3], layout);
                    move(layout.querySelector('.shift-open-summary-card'), layout); move(layout.querySelector('.shift-open-warning-card'), layout);
                }
            } else if (!phone.matches && disclosures.length) {
                for (const { node, marker } of moves) marker.replaceWith(node);
                moves.length = 0; for (const node of disclosures) node.remove(); disclosures.length = 0;
                bar?.remove(); bar = null; delete root.dataset.phoneHistoryView;
                if (listHint) listHint.textContent = originalListHint;
            }
            root.querySelector('.handover-phone-filter')?.toggleAttribute('hidden', !phone.matches);
            if (focused && focused.isConnected && focused !== document.body && focused !== document.activeElement) focused.focus({ preventScroll: true });
            schedule();
        }
        // Original history delegation runs first at tbody, then this presentation listener.
        root.addEventListener('click', showSummary); root.addEventListener('keydown', showSummary);
        if (history) new MutationObserver(() => {
            if (phone.matches && root.dataset.phoneHistoryView === 'detail' && document.getElementById('summaryContent').style.display !== 'none')
                requestAnimationFrame(() => { if (root.dataset.phoneHistoryView === 'detail') root.scrollIntoView({ block: 'start', behavior: 'instant' }); });
        }).observe(document.getElementById('summaryContent'), { attributes: true, attributeFilter: ['style'] });
        phone.addEventListener('change', apply); window.addEventListener('resize', schedule);
        window.visualViewport?.addEventListener('resize', schedule); window.visualViewport?.addEventListener('scroll', schedule);
        for (const modal of modals) modal.addEventListener('shown.bs.modal', schedule);
        document.addEventListener('focusin', schedule); apply();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true }); else mount();
})();
