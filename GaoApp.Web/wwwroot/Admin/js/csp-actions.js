/* Fixed UI actions only. Attribute values are data, never JavaScript source. */
(() => {
    'use strict';
    if (window.GaoCspActionsInstalled) return;
    window.GaoCspActionsInstalled = true;
    const id = element => Number(element.dataset.cspId || 0);
    const actions = {
        print: () => window.print(), close: () => window.close(), back: () => history.back(),
        reload: () => location.reload(),
        'promotion-create': () => window.openCreateModal(),
        'promotion-edit': el => window.openEditModal(id(el)),
        'promotion-delete': el => window.deletePromotion(id(el)),
        'promotion-toggle': el => window.toggleActive(id(el), el.checked),
        'promotion-preview': () => window.syncPreview(),
        'promotion-file': el => window.previewFile(el),
        'invoice-preview': () => window.loadPreviewPdf(),
        'invoice-copy': () => window.copyPayloadJson(),
        'invoice-copy-request': () => window.copyText('requestBody'),
        'invoice-copy-response': () => window.copyText('responseBody'),
        'invoice-password': () => window.toggleInvoicePassword(),
        'return-refund': el => window.openReturnRefundModal(id(el)),
        'return-history': el => window.openReturnHistoryModal(id(el)),
        'order-void': el => window.openOrderActionModal('void', id(el)),
        'order-refund': el => window.openOrderActionModal('refund', id(el)),
        'payment-history': () => window.openPaymentHistoryModal(),
        'legal-entity-edit': el => window.LegalEntityPage.openEdit(id(el)),
        'legal-entity-active': el => window.LegalEntityPage.confirmActive(id(el), el.checked, el),
        'warehouse-edit': el => window.WarehousePage.openEditModal(id(el)),
        'warehouse-default': el => window.WarehousePage.confirmSetDefault(id(el), el),
        'warehouse-negative': el => window.WarehousePage.confirmToggleNegative(id(el), el.checked, el),
        'warehouse-active': el => window.WarehousePage.confirmToggleActive(id(el), el.checked, el),
        'report-profit': el => {
            const url = new URL(el.href);
            for (const key of ['fromDate', 'toDate', 'compare', 'terminalId', 'customerState']) {
                const value = new URL(location.href).searchParams.get(key);
                if (value !== null) url.searchParams.set(key, value); else url.searchParams.delete(key);
            }
            el.href = url.href;
        }
    };
    // Capture confirmations before page-specific AJAX handlers see the click/submit.
    for (const type of ['click', 'submit']) document.addEventListener(type, event => {
        const el = event.target.closest?.('[data-csp-confirm]');
        if (!el || (el.tagName === 'FORM' ? type !== 'submit' : type !== 'click')) return;
        if (!window.confirm(el.dataset.cspConfirm)) { event.preventDefault(); event.stopImmediatePropagation(); }
    }, true);
    for (const type of ['click', 'change', 'submit']) document.addEventListener(type, event => {
        const el = event.target.closest?.('[data-csp-action]');
        if (!el || (el.dataset.cspEvent || 'click') !== type) return;
        const action = el.dataset.cspAction;
        if (action === 'prevent') { event.preventDefault(); return; }
        if (action === 'stop') { event.stopPropagation(); return; }
        if (el.tagName === 'A' && el.getAttribute('href') === '#') event.preventDefault();
        if (Object.hasOwn(actions, action)) actions[action](el);
    });
    document.addEventListener('error', event => {
        const img = event.target;
        if (!(img instanceof HTMLImageElement) || !img.dataset.cspImage) return;
        if (img.dataset.cspImage === 'stock-line') {
            img.hidden = true;
            if (img.nextElementSibling) img.nextElementSibling.hidden = false;
            if (img.parentElement) img.parentElement.disabled = true;
        } else if (img.dataset.cspImage === 'stock-transfer') {
            const placeholder = document.createElement('div'), icon = document.createElement('i');
            placeholder.className = 'st-product-img-placeholder'; icon.className = 'bx bx-package';
            placeholder.append(icon); img.replaceWith(placeholder);
        }
    }, true);
})();
