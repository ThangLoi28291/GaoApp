(function (global) {
    'use strict';
    const esc = value => String(value ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
    const crates = n => '<span class="sm-crate"></span>'.repeat(n);
    const barcode = '<span class="sm-barcode"></span>';
    const rack = (destination = false, n = 6) => `<span class="sm-rack${destination ? ' sm-destination-rack' : ''}">${crates(n)}</span>`;
    const actionName = event => (event?.action || '').split('.').pop();

    function kind(module, workKey, event) {
        if (module === 'pos') return 'pos';
        if (module === 'receipt') return /^(AddLine|AddLineByBarcode|Capture|Known|Quantity)$/.test(actionName(event)) ? 'check' : 'input';
        if (module === 'warehouse') return (workKey || '').startsWith('transfer:') ? 'transfer' : 'count';
        if (module === 'label') return actionName(event) === 'Plan' || !event ? 'prepare' : 'print';
        return 'review';
    }

    // Only successful server events can trigger printing or approval. Presence means interaction.
    function effect(module, workKey, event, editing) {
        if (editing) return 'input';
        if (!event) return '';
        const action = actionName(event);
        if (module === 'pos') {
            if (/^(Finalize|FinalizeCurrentCart|FinalizeCredit)$/.test(action) ||
                action === 'AddPaymentAndMaybeFinalizeCurrentCart' && event.text === 'vừa thanh toán và chốt đơn') return 'print';
            if (/^(AddItem|AddByBarcode|ScanToCurrentCart)$/.test(action)) return 'scan';
            return 'input';
        }
        if (module === 'receipt') return kind(module, workKey, event) === 'check' ? 'check' : 'input';
        if (module === 'warehouse') {
            if (/^(AddLine|UpdateLine|Confirm|Approve)$/.test(action)) return (workKey || '').startsWith('transfer:') ? 'transfer' : 'check';
            return 'input';
        }
        if (module === 'label') return action === 'Print' ? 'print' : 'input';
        if (module === 'review') return /^(Approve|ApproveCommercial|Confirm)$/.test(action) ||
            event.action === 'ReceiptIntake.Review' && (event.text || '').startsWith('vừa duyệt mặt hàng') ? 'approved' : 'input';
        return '';
    }

    function render(module, workKey, event, document, editing, documentTitle) {
        const type = kind(module, workKey, event);
        const doc = `<span class="sm-scene-document" title="${esc([document, documentTitle].filter(Boolean).join(' · '))}">${esc(documentTitle || document || 'CHỜ PHIẾU')}</span>`;
        // No progress or made-up quantities: the full persisted detail stays below the model.
        const label = editing ? 'NHẬP LIỆU' : event ? 'ĐÃ LƯU' : 'CHỜ';
        const screen = `<span class="sm-scene-count">${label}</span>`;
        const computer = `<span class="sm-model-computer"><span class="sm-model-screen">${doc}${screen}</span><span class="sm-model-indicator"></span></span><span class="sm-model-keyboard"></span>`;
        const printer = `<span class="sm-label-machine"><span class="sm-label-control"><span class="sm-scene-count">TEM</span></span><span class="sm-label-light"></span></span>`;
        let model;
        if (type === 'pos') {
            const content = event?.detail || document || 'CHỜ ĐƠN';
            model = `<span class="sm-counter-top"></span><span class="sm-monitor"><span class="sm-screen-label">GIỎ HÀNG</span><span class="sm-screen-total" title="${esc(content)}">${esc(content)}</span></span><span class="sm-product"><span class="sm-barcode"></span></span><span class="sm-scan-line"></span><span class="sm-printer"><span class="sm-bill"></span></span>`;
        } else if (module === 'receipt') {
            model = `<span class="sm-pallet"></span><span class="sm-receive-stack">${crates(3)}</span>${computer}`;
            model += type === 'check' ? '<span class="sm-crate sm-active-crate"></span><span class="sm-hand-scanner"></span><span class="sm-receive-beam"></span>' : '<span class="sm-document-sheet"></span>';
        } else if (type === 'count') {
            model = `${rack()}<span class="sm-stock-device"><span class="sm-device-screen"><span class="sm-scene-count">KIỂM</span></span></span><span class="sm-stock-beam"></span>`;
        } else if (type === 'transfer') {
            model = `<span class="sm-rack-label">Kho nguồn</span>${rack(false, 3)}<span class="sm-rack-label sm-rack-label-destination">Kho đích</span>${rack(true, 3)}<span class="sm-transfer-route">→</span><span class="sm-crate sm-transfer-crate"></span>`;
        } else if (type === 'print') {
            model = `<span class="sm-label-roll"></span>${printer}<span class="sm-label-feed">${barcode.repeat(3)}</span><span class="sm-ready-labels">${barcode}</span>`;
        } else if (type === 'prepare') {
            model = `<span class="sm-label-preview"><span class="sm-label-preview-screen">${(`<span class="sm-preview-label">${barcode}</span>`).repeat(3)}</span></span>${printer}`;
        } else {
            model = `<span class="sm-review-desk"></span><span class="sm-paper-stack"></span><span class="sm-review-document">${doc}<span class="sm-review-lines"></span><span class="sm-review-pointer"></span><span class="sm-review-seal">✓</span></span><span class="sm-stamp"></span><span class="sm-stamp-pad"></span>`;
        }
        return `<div class="${type === 'pos' ? 'sm-pos-visual' : 'sm-work-scene'} sm-scene-${type}"><div class="wm-art sm-scene-stage"><span class="sm-scene-ground"></span>${model}</div></div>`;
    }
    global.GaoStoreScenes = Object.freeze({ kind, effect, render });
})(window);
