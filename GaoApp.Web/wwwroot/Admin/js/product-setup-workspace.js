(function () {
    'use strict';

    const root = document.querySelector('[data-product-setup-workspace]');
    if (!root) return;

    const form = root.querySelector('#productSetupForm');
    const uploadState = root.querySelector('#productUploadState');
    const primaryAction = root.querySelector('[data-product-primary-action], #btnSaveAll');
    const stage = root.dataset.productSetupStage || 'create';

    const completionState = {
        imageCount: Number(root.querySelector('#sumImages')?.textContent || 0),
        persistedVariantCount: 0,
        hasVariantRetailPrice: false,
        hasSellingUnit: false,
        hasActiveBarcode: false,
        dirty: stage === 'create'
    };

    function hasValue(selector) {
        const element = root.querySelector(selector);
        return !!element && String(element.value || '').trim().length > 0;
    }

    function hasPositiveNumber(selector) {
        const element = root.querySelector(selector);
        const value = Number(element?.value || 0);
        return Number.isFinite(value) && value > 0;
    }

    const completionCriteria = [
        { name: 'name', ready: () => hasValue('#txtName') },
        { name: 'alias', ready: () => hasValue('#txtAlias') },
        { name: 'category', ready: () => hasValue('#ddlCategory') },
        { name: 'supplier', ready: () => hasValue('#ddlSupplier') },
        { name: 'unit', ready: () => hasValue('#ddlUnit') },
        { name: 'variant', ready: () => completionState.persistedVariantCount > 0 },
        { name: 'price', ready: () => hasPositiveNumber('#txtBasePrice') || completionState.hasVariantRetailPrice },
        { name: 'images', ready: () => completionState.imageCount > 0 },
        { name: 'selling-unit', ready: () => completionState.hasSellingUnit },
        { name: 'barcode', ready: () => completionState.hasActiveBarcode }
    ];

    function setSaveState() {
        const text = stage === 'create'
            ? 'Chưa lưu'
            : completionState.dirty ? 'Có thay đổi chưa lưu' : 'Đã lưu';

        root.querySelectorAll('[data-product-save-state]').forEach(element => {
            element.textContent = text;
            element.classList.toggle('is-unsaved', completionState.dirty);
        });
    }

    function markReadiness(name, ready) {
        const item = root.querySelector(`[data-readiness-item="${name}"]`);
        if (!item) return;

        item.classList.toggle('is-ready', ready);
        const icon = item.querySelector('.psw-completion-icon i');
        if (icon) icon.className = ready ? 'bx bx-check' : 'bx bx-right-arrow-alt';
        const status = item.querySelector('[data-readiness-status]');
        if (status) status.textContent = ready ? 'Hoàn tất' : 'Chưa có';
    }

    function syncPosState() {
        const isSellable = root.querySelector('#IsSellable')?.checked === true;
        root.querySelectorAll('[data-pos-state]').forEach(element => {
            element.textContent = isSellable ? 'Đang bật' : 'Đang tắt';
            element.classList.toggle('is-on', isSellable);
            element.classList.toggle('is-off', !isSellable);
        });
    }

    function syncReadiness() {
        let readyCount = 0;

        completionCriteria.forEach(criterion => {
            const ready = criterion.ready();
            if (ready) readyCount += 1;
            markReadiness(criterion.name, ready);
        });

        const percent = readyCount * 10;
        root.querySelectorAll('[data-completion-percent]').forEach(element => {
            element.textContent = String(percent);
        });
        root.querySelectorAll('[data-completion-count]').forEach(element => {
            element.textContent = `${readyCount}/10 tiêu chí hoàn tất`;
        });
        root.querySelectorAll('[data-completion-bar]').forEach(element => {
            element.style.width = `${percent}%`;
        });
        root.querySelectorAll('[data-completion-track]').forEach(element => {
            element.setAttribute('aria-valuenow', String(percent));
        });

        syncPosState();
        setSaveState();
    }

    function setUploadState(detail) {
        const pending = detail?.pending === true;
        const count = Number(detail?.count || 0);

        completionState.imageCount = count;
        if (form) form.dataset.uploadState = pending ? 'pending' : 'ready';
        if (primaryAction) primaryAction.disabled = pending;

        if (uploadState) {
            uploadState.classList.toggle('is-pending', pending);
            uploadState.innerHTML = pending
                ? '<i class="bx bx-loader-alt bx-spin"></i><span>Đang tải ảnh lên, vui lòng chờ trước khi lưu.</span>'
                : `<i class="bx bx-check-circle"></i><span>${count > 0 ? `${count} ảnh đã sẵn sàng.` : 'Ảnh đã sẵn sàng.'}</span>`;
        }

        syncReadiness();

        window.dispatchEvent(new CustomEvent('gao:product-upload-state', {
            detail: { pending, count }
        }));
    }

    const progress = root.querySelector('[data-completion-progress]');
    const trigger = progress?.querySelector('[data-completion-toggle]');
    const panel = progress?.querySelector('[data-completion-panel]');
    const backdrop = progress?.querySelector('[data-completion-backdrop]');

    function setPanelOpen(open) {
        if (!trigger || !panel) return;

        trigger.setAttribute('aria-expanded', open ? 'true' : 'false');
        panel.hidden = !open;
        if (backdrop) backdrop.hidden = !open;
        root.classList.toggle('psw-completion-open', open);

        if (open) {
            panel.querySelector('[data-completion-close]')?.focus({ preventScroll: true });
        }
    }

    trigger?.addEventListener('click', () => {
        setPanelOpen(trigger.getAttribute('aria-expanded') !== 'true');
    });

    panel?.querySelector('[data-completion-close]')?.addEventListener('click', () => {
        setPanelOpen(false);
        trigger?.focus({ preventScroll: true });
    });

    backdrop?.addEventListener('click', () => setPanelOpen(false));

    panel?.addEventListener('click', event => {
        const action = event.target.closest('[data-completion-target]');
        if (!action) return;

        const target = root.querySelector(action.dataset.completionTarget);
        if (!target) return;

        setPanelOpen(false);
        window.setTimeout(() => {
            target.scrollIntoView({ behavior: 'smooth', block: 'center' });
            target.classList.add('psw-highlight');
            window.setTimeout(() => target.classList.remove('psw-highlight'), 1400);

            if (target.matches('input:not([readonly]), textarea, button')) {
                target.focus({ preventScroll: true });
            }
        }, 80);
    });

    document.addEventListener('click', event => {
        if (trigger?.getAttribute('aria-expanded') !== 'true') return;
        if (progress?.contains(event.target)) return;
        setPanelOpen(false);
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && trigger?.getAttribute('aria-expanded') === 'true') {
            setPanelOpen(false);
            trigger.focus({ preventScroll: true });
        }
    });

    root.addEventListener('input', event => {
        if (event.target.closest('[data-completion-panel]')) return;
        completionState.dirty = true;
        syncReadiness();
    });

    root.addEventListener('change', event => {
        if (event.target.closest('[data-completion-panel]')) return;
        completionState.dirty = true;
        syncReadiness();
    });

    if (window.jQuery) {
        window.jQuery(document).on('select2:select select2:clear', () => {
            completionState.dirty = true;
            syncReadiness();
        });
    }

    window.addEventListener('gao:product-variants-state', event => {
        completionState.persistedVariantCount = Number(event.detail?.persistedCount || 0);
        completionState.hasVariantRetailPrice = event.detail?.hasRetailPrice === true;
        completionState.hasSellingUnit = event.detail?.hasSellingUnit === true;
        completionState.hasActiveBarcode = event.detail?.hasActiveBarcode === true;
        syncReadiness();
    });

    const initialVariantState = window.GaoVariant?.getCompletionState?.();
    if (initialVariantState) {
        completionState.persistedVariantCount = Number(initialVariantState.persistedCount || 0);
        completionState.hasVariantRetailPrice = initialVariantState.hasRetailPrice === true;
        completionState.hasSellingUnit = initialVariantState.hasSellingUnit === true;
        completionState.hasActiveBarcode = initialVariantState.hasActiveBarcode === true;
    }

    window.GaoProductSetup = Object.assign(window.GaoProductSetup || {}, {
        setUploadState,
        syncReadiness,
        setPanelOpen
    });

    syncReadiness();
    setUploadState({
        pending: form?.dataset.uploadState === 'pending',
        count: Number(root.querySelector('#sumImages')?.textContent || 0)
    });

    if (root.dataset.initialSetup === 'true' && window.location.hash === '#variants') {
        window.requestAnimationFrame(() => {
            root.querySelector('#variants')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        });
    }
})();
