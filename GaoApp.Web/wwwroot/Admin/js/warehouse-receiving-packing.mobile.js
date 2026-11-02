// UI-05c: presentation only. Original quantity, intake, photo and review modules own all commands.
(function () {
    'use strict';
    function initialize() {
        const root = document.querySelector('[data-receiving-detail]');
        if (!root) return;
        const phone = matchMedia('(max-width: 767.98px)'), rows = new Map(), placements = [], constraints = [];
        const byId = id => document.getElementById(id);
        let active = false, frame = 0, summary, lineCount, pendingButton, photoButton, evidence, photoChoose;
        const element = (tag, className, text) => {
            const node = document.createElement(tag); node.className = className;
            if (text) node.textContent = text; return node;
        };
        const text = (node, value) => { if (node.textContent !== value) node.textContent = value; };
        function openTask(id) {
            const target = byId(id), tab = root.querySelector('[data-wra-tab="info"]');
            if (!active || !target || target.hidden || !tab) return;
            tab.click(); target.open = true;
            requestAnimationFrame(() => { if (active && target.isConnected) target.scrollIntoView({ block: 'start', behavior: 'instant' }); });
        }
        function refresh() {
            if (!active) return;
            for (const row of rows.keys()) if (!row.isConnected) rows.delete(row);
            root.querySelectorAll('#wrdLinesContainer tr[data-receiving-key]').forEach(row => {
                const cell = row.children[3], unit = row.children[2]?.textContent.trim();
                if (!cell || !unit) return;
                let saved = rows.get(row);
                if (!saved) {
                    const caption = element('span', 'wrc-quantity-unit');
                    caption.id = 'wrc-unit-' + row.dataset.receivingKey.replace(/[^a-zA-Z0-9_-]/g, '-');
                    cell.prepend(caption);
                    const edit = cell.querySelector('[data-wra-edit]');
                    const description = edit?.getAttribute('aria-describedby') ?? null;
                    if (edit) edit.setAttribute('aria-describedby', [description, caption.id].filter(Boolean).join(' '));
                    saved = { caption, edit, description }; rows.set(row, saved);
                }
                text(saved.caption, 'SL nhận · ' + unit);
            });
            text(lineCount, (byId('wraCount')?.textContent.trim() || '0') + ' dòng trên phiếu');
            const pending = byId('receiptIntakePending');
            pendingButton.hidden = !pending || pending.hidden;
            text(pendingButton, (byId('riPendingCount')?.textContent.trim() || '0') + ' mục cần xử lý');
            const photos = byId('riPhotoEvidence'); photoButton.hidden = !photos || photos.hidden;
            text(photoButton, 'Ảnh bao bì ' + (byId('riPhotoCount')?.textContent.trim() || ''));
        }
        function schedule() {
            if (frame || !active) return;
            frame = requestAnimationFrame(() => { frame = 0; refresh(); });
        }
        function enter() {
            if (active) return;
            active = true; root.dataset.receivingPacking = '';
            summary = element('section', 'wrc-workload'); summary.setAttribute('aria-label', 'Dòng hàng và mục cần xử lý');
            lineCount = element('strong', '');
            const note = element('small', '', 'Số lượng được theo dõi riêng theo từng quy cách.');
            const counts = element('div', ''); counts.append(lineCount, note); summary.append(counts);
            const actions = element('div', 'wrc-task-actions');
            pendingButton = element('button', '', ''); pendingButton.type = 'button'; pendingButton.setAttribute('aria-controls', 'receiptIntakePending');
            photoButton = element('button', '', ''); photoButton.type = 'button'; photoButton.setAttribute('aria-controls', 'riPhotoEvidence');
            pendingButton.addEventListener('click', () => openTask('receiptIntakePending'));
            photoButton.addEventListener('click', () => openTask('riPhotoEvidence'));
            actions.append(pendingButton, photoButton); summary.append(actions);
            const scan = root.querySelector('.wrd-scan-panel');
            if (scan) scan.after(summary); else root.querySelector('#wraItems')?.prepend(summary);
            const quantity = byId('riQuantity');
            if (quantity) for (const name of ['min', 'step']) {
                constraints.push({ node: quantity, name, value: quantity.getAttribute(name) });
                quantity.setAttribute(name, '0.001');
            }
            const noteLabel = byId('receiptIntakeModal')?.querySelector('label[for="riNote"]');
            const fields = [noteLabel, byId('riNote'), byId('receiptIntakeModal')?.querySelector('.ri-photo-tools')].filter(Boolean);
            if (fields.length === 3) {
                evidence = element('details', 'wrc-evidence');
                evidence.append(element('summary', '', 'Ảnh bao bì và ghi chú · không bắt buộc'));
                const body = element('div', 'wrc-evidence-body'); evidence.append(body);
                for (const node of fields) {
                    const anchor = document.createComment('receiving-packing-desktop-position'); node.before(anchor); placements.push({ node, anchor });
                }
                placements[0].anchor.before(evidence); body.append(...fields);
                photoChoose = element('label', 'wrc-photo-choose', 'Chụp / chọn ảnh bao bì'); photoChoose.htmlFor = 'riPhoto';
                byId('riPhoto').before(photoChoose);
                evidence.open = !byId('riPhotoPreview').hidden || !!byId('riNote').value;
            }
            refresh();
        }
        function leave() {
            if (!active) return;
            active = false; summary.remove();
            for (const { node, anchor } of placements) anchor.replaceWith(node);
            placements.length = 0; evidence?.remove(); evidence = null; photoChoose?.remove(); photoChoose = null;
            for (const { node, name, value } of constraints) { if (value === null) node.removeAttribute(name); else node.setAttribute(name, value); }
            constraints.length = 0;
            for (const { caption, edit, description } of rows.values()) {
                caption.remove();
                if (edit?.isConnected) { if (description === null) edit.removeAttribute('aria-describedby'); else edit.setAttribute('aria-describedby', description); }
            }
            rows.clear(); delete root.dataset.receivingPacking;
        }
        for (const id of ['wrdLinesContainer', 'wraCount', 'riPendingCount', 'riPhotoCount']) {
            const node = byId(id); if (node) new MutationObserver(schedule).observe(node, { childList: true, subtree: true, characterData: true });
        }
        for (const id of ['receiptIntakePending', 'riPhotoEvidence']) {
            const node = byId(id); if (node) new MutationObserver(schedule).observe(node, { attributes: true, attributeFilter: ['hidden'] });
        }
        function update() { if (phone.matches) enter(); else leave(); }
        phone.addEventListener('change', update); window.addEventListener('pageshow', update); update();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize, { once: true });
    else initialize();
}());
