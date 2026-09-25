(() => {
    'use strict';

    const root = document.querySelector('[data-admin-menu-index]');
    if (!root) return;

    const allowedLevels = new Set(['all', 'parent', 'child']);
    const allowedTypes = new Set(['all', 'system', 'custom']);
    const allowedLifecycles = new Set(['all', 'active', 'inactive']);
    const allowedPageSizes = new Set([10, 20, 50]);
    const query = new URLSearchParams(window.location.search);
    const initialPageSize = Number.parseInt(query.get('pageSize') || '10', 10);

    const state = {
        keyword: (query.get('keyword') || '').trim(),
        level: allowedLevels.has(query.get('level')) ? query.get('level') : 'all',
        type: allowedTypes.has(query.get('type')) ? query.get('type') : 'all',
        lifecycle: allowedLifecycles.has(query.get('lifecycle')) ? query.get('lifecycle') : 'all',
        page: Math.max(1, Number.parseInt(query.get('page') || '1', 10) || 1),
        pageSize: allowedPageSizes.has(initialPageSize) ? initialPageSize : 10
    };

    const elements = {
        keyword: document.getElementById('adminMenuKeyword'),
        level: document.getElementById('adminMenuLevel'),
        type: document.getElementById('adminMenuType'),
        lifecycle: document.getElementById('adminMenuLifecycle'),
        pageSize: document.getElementById('adminMenuPageSize'),
        mobileLevel: document.getElementById('adminMenuMobileLevel'),
        mobileType: document.getElementById('adminMenuMobileType'),
        mobileLifecycle: document.getElementById('adminMenuMobileLifecycle'),
        mobilePageSize: document.getElementById('adminMenuMobilePageSize'),
        mobileFilterSheet: document.getElementById('adminMenuMobileFilterSheet'),
        mobileApply: document.getElementById('btnAdminMenuMobileApply'),
        mobileClear: document.getElementById('btnAdminMenuMobileClear'),
        activeFilterCount: document.getElementById('adminMenuActiveFilterCount'),
        clearSearch: document.getElementById('btnAdminMenuClearSearch'),
        reset: document.getElementById('btnAdminMenuReset'),
        reload: document.getElementById('btnAdminMenuReload'),
        empty: document.getElementById('adminMenuEmpty'),
        pagingInfo: document.getElementById('adminMenuPagingInfo'),
        pageNumbers: document.getElementById('adminMenuPageNumbers'),
        statTotal: document.getElementById('adminMenuStatTotal'),
        statActive: document.getElementById('adminMenuStatActive'),
        statInactive: document.getElementById('adminMenuStatInactive'),
        statCustom: document.getElementById('adminMenuStatCustom'),
        quickView: document.getElementById('adminMenuQuickView'),
        quickTitle: document.getElementById('adminMenuQuickViewTitle'),
        quickParent: document.getElementById('adminMenuQuickParent'),
        quickDestination: document.getElementById('adminMenuQuickDestination'),
        quickPermission: document.getElementById('adminMenuQuickPermission'),
        quickType: document.getElementById('adminMenuQuickType'),
        quickLifecycle: document.getElementById('adminMenuQuickLifecycle'),
        quickOrder: document.getElementById('adminMenuQuickOrder'),
        quickChildren: document.getElementById('adminMenuQuickChildren'),
        deleteConfirmation: document.getElementById('adminMenuDeleteConfirmation'),
        deleteName: document.getElementById('adminMenuDeleteName'),
        confirmDelete: document.getElementById('btnAdminMenuConfirmDelete')
    };

    const desktopRows = Array.from(root.querySelectorAll('[data-admin-menu-item]'));
    const mobileItems = Array.from(root.querySelectorAll('[data-admin-menu-mobile-item]'));
    const desktopByKey = new Map(desktopRows.map((row) => [row.dataset.menuKey, row]));
    const mobileByKey = new Map(mobileItems.map((card) => [card.dataset.menuKey, card]));
    const collapsedParents = new Set();
    let adminMenuSearchTimer = 0;
    let pendingDeleteForm = null;

    const normalizeSearchText = (value) => String(value || '')
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .replace(/[đĐ]/g, (letter) => letter === 'Đ' ? 'D' : 'd')
        .toLocaleLowerCase('vi-VN')
        .trim();

    const buildAdminMenuItems = () => desktopRows.map((row) => ({
        key: row.dataset.menuKey || '',
        parentKey: row.dataset.menuParentKey || '',
        level: row.dataset.menuLevel || 'child',
        type: row.dataset.menuType || 'custom',
        lifecycle: row.dataset.menuLifecycle || 'inactive',
        title: row.dataset.menuTitle || '—',
        parent: row.dataset.menuParent || 'Menu gốc',
        destination: row.dataset.menuDestination || '—',
        permission: row.dataset.menuPermission || 'Không yêu cầu quyền',
        sortOrder: row.dataset.menuSortOrder || '0',
        typeLabel: row.dataset.menuTypeLabel || '—',
        lifecycleLabel: row.dataset.menuLifecycleLabel || '—',
        childCount: Number.parseInt(row.dataset.menuChildCount || '0', 10) || 0,
        searchable: normalizeSearchText(row.dataset.menuSearch || ''),
        row
    }));

    const allItems = buildAdminMenuItems();

    const setText = (element, value) => {
        if (element) element.textContent = String(value ?? '—');
    };

    const syncControlsFromState = () => {
        if (elements.keyword) elements.keyword.value = state.keyword;
        if (elements.level) elements.level.value = state.level;
        if (elements.type) elements.type.value = state.type;
        if (elements.lifecycle) elements.lifecycle.value = state.lifecycle;
        if (elements.pageSize) elements.pageSize.value = String(state.pageSize);
        if (elements.mobileLevel) elements.mobileLevel.value = state.level;
        if (elements.mobileType) elements.mobileType.value = state.type;
        if (elements.mobileLifecycle) elements.mobileLifecycle.value = state.lifecycle;
        if (elements.mobilePageSize) elements.mobilePageSize.value = String(state.pageSize);
        updateFilterIndicators();
    };

    const updateUrl = () => {
        const next = new URLSearchParams();
        if (state.keyword) next.set('keyword', state.keyword);
        if (state.level !== 'all') next.set('level', state.level);
        if (state.type !== 'all') next.set('type', state.type);
        if (state.lifecycle !== 'all') next.set('lifecycle', state.lifecycle);
        if (state.page > 1) next.set('page', String(state.page));
        if (state.pageSize !== 10) next.set('pageSize', String(state.pageSize));
        const nextUrl = next.size ? window.location.pathname + '?' + next.toString() : window.location.pathname;
        window.history.replaceState({ adminMenuIndex: true }, '', nextUrl);
    };

    const updateFilterIndicators = () => {
        const count = Number(state.level !== 'all')
            + Number(state.type !== 'all')
            + Number(state.lifecycle !== 'all')
            + Number(state.pageSize !== 10);
        const hasAny = count > 0 || Boolean(state.keyword);

        elements.clearSearch?.classList.toggle('d-none', !state.keyword);
        elements.reset?.classList.toggle('d-none', !hasAny);
        if (elements.activeFilterCount) {
            elements.activeFilterCount.textContent = String(count);
            elements.activeFilterCount.classList.toggle('d-none', count === 0);
        }

        document.querySelectorAll('[data-admin-menu-kpi]').forEach((button) => {
            const key = button.dataset.adminMenuKpi;
            const selected = (key === 'total' && state.type === 'all' && state.lifecycle === 'all')
                || (key === 'active' && state.lifecycle === 'active')
                || (key === 'inactive' && state.lifecycle === 'inactive')
                || (key === 'custom' && state.type === 'custom');
            button.classList.toggle('is-selected', selected);
            button.setAttribute('aria-pressed', selected ? 'true' : 'false');
        });
    };

    function renderAdminMenuSummary(scopedItems) {
        setText(elements.statTotal, scopedItems.length);
        setText(elements.statActive, scopedItems.filter((item) => item.lifecycle === 'active').length);
        setText(elements.statInactive, scopedItems.filter((item) => item.lifecycle === 'inactive').length);
        setText(elements.statCustom, scopedItems.filter((item) => item.type === 'custom').length);
    }

    const pageButton = (page, label, content, disabled, active = false) =>
        '<li class="page-item' + (disabled ? ' disabled' : '') + (active ? ' active' : '') + '">' +
        '<button type="button" class="page-link" data-admin-menu-page="' + page + '" aria-label="' + label + '"' +
        (disabled ? ' disabled' : '') + '>' + (content || page) + '</button></li>';

    function renderAdminMenuCircularPagination(page, totalPages, totalItems, pageSize) {
        const safePages = Math.max(1, totalPages);
        const safePage = Math.min(Math.max(1, page), safePages);
        const start = totalItems === 0 ? 0 : ((safePage - 1) * pageSize) + 1;
        const end = Math.min(totalItems, safePage * pageSize);

        setText(elements.pagingInfo, totalItems === 0
            ? '0 kết quả'
            : start + '–' + end + ' trên ' + totalItems + ' mục menu');

        if (!elements.pageNumbers) return;
        const buttons = [];
        const first = Math.max(1, Math.min(safePage - 2, safePages - 4));
        const last = Math.min(safePages, first + 4);
        buttons.push(pageButton(safePage - 1, 'Trang trước', '<i class="bx bx-chevron-left"></i>', safePage <= 1));
        for (let value = first; value <= last; value += 1) {
            buttons.push(pageButton(value, 'Trang ' + value, '', false, value === safePage));
        }
        buttons.push(pageButton(safePage + 1, 'Trang sau', '<i class="bx bx-chevron-right"></i>', safePage >= safePages));
        elements.pageNumbers.innerHTML = buttons.join('');
    }

    const setItemVisibility = (item, visible, context, ignoreCollapse) => {
        const collapsed = item.parentKey && collapsedParents.has(item.parentKey) && !ignoreCollapse;
        const finalVisible = visible && !collapsed;
        item.row.hidden = !finalVisible;
        item.row.classList.toggle('is-context', finalVisible && context);

        const mobile = mobileByKey.get(item.key);
        if (mobile) {
            mobile.hidden = !finalVisible;
            mobile.classList.toggle('is-context', finalVisible && context);
        }
    };

    const syncCollapseButtons = () => {
        root.querySelectorAll('[data-admin-menu-collapse]').forEach((button) => {
            const collapsed = collapsedParents.has(button.dataset.adminMenuCollapse);
            button.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
            const icon = button.querySelector('i');
            if (icon && !button.disabled) {
                icon.className = 'bx ' + (collapsed ? 'bx-chevron-right' : 'bx-chevron-down');
            }
        });
    };

    function applyAdminMenuFilters() {
        const normalizedKeyword = normalizeSearchText(state.keyword);
        const scope = allItems.filter((item) => {
            const keywordMatch = !normalizedKeyword || item.searchable.includes(normalizedKeyword);
            const levelMatch = state.level === 'all' || item.level === state.level;
            return keywordMatch && levelMatch;
        });

        renderAdminMenuSummary(scope);

        const filtered = scope.filter((item) => {
            const typeMatch = state.type === 'all' || item.type === state.type;
            const lifecycleMatch = state.lifecycle === 'all' || item.lifecycle === state.lifecycle;
            return typeMatch && lifecycleMatch;
        });

        const totalPages = Math.max(1, Math.ceil(filtered.length / state.pageSize));
        state.page = Math.min(Math.max(1, state.page), totalPages);
        const offset = (state.page - 1) * state.pageSize;
        const pageItems = filtered.slice(offset, offset + state.pageSize);
        const resultKeys = new Set(pageItems.map((item) => item.key));
        const contextKeys = new Set(pageItems.filter((item) => item.parentKey).map((item) => item.parentKey));
        const ignoreCollapse = Boolean(normalizedKeyword)
            || state.level !== 'all'
            || state.type !== 'all'
            || state.lifecycle !== 'all';

        allItems.forEach((item) => {
            const result = resultKeys.has(item.key);
            const context = !result && contextKeys.has(item.key);
            setItemVisibility(item, result || context, context, ignoreCollapse);
        });

        elements.empty?.classList.toggle('d-none', filtered.length !== 0);
        document.getElementById('adminMenuDesktopList')?.classList.toggle('d-none', filtered.length === 0);
        document.getElementById('adminMenuMobileList')?.classList.toggle('is-empty', filtered.length === 0);
        renderAdminMenuCircularPagination(state.page, totalPages, filtered.length, state.pageSize);
        syncCollapseButtons();
        updateFilterIndicators();
        updateUrl();
    }

    function openAdminMenuQuickView(source) {
        const holder = source.closest('[data-admin-menu-item], [data-admin-menu-mobile-item]');
        if (!holder) return;
        const row = desktopByKey.get(holder.dataset.menuKey);
        if (!row) return;

        setText(elements.quickTitle, row.dataset.menuTitle);
        setText(elements.quickParent, row.dataset.menuParent);
        setText(elements.quickDestination, row.dataset.menuDestination);
        setText(elements.quickPermission, row.dataset.menuPermission);
        setText(elements.quickType, row.dataset.menuTypeLabel);
        setText(elements.quickLifecycle, row.dataset.menuLifecycleLabel);
        setText(elements.quickOrder, row.dataset.menuSortOrder);
        setText(elements.quickChildren, row.dataset.menuChildCount);

        if (elements.quickView && window.bootstrap?.Modal) {
            window.bootstrap.Modal.getOrCreateInstance(elements.quickView).show();
        }
    }

    function showDeleteConfirmation(form) {
        if (!form || !elements.deleteConfirmation || !window.bootstrap?.Modal) return;
        pendingDeleteForm = form;
        setText(elements.deleteName, form.dataset.menuDeleteTitle || 'menu đã chọn');
        window.bootstrap.Modal.getOrCreateInstance(elements.deleteConfirmation).show();
    }

    const readDesktopFilters = () => {
        state.level = allowedLevels.has(elements.level?.value) ? elements.level.value : 'all';
        state.type = allowedTypes.has(elements.type?.value) ? elements.type.value : 'all';
        state.lifecycle = allowedLifecycles.has(elements.lifecycle?.value) ? elements.lifecycle.value : 'all';
        const size = Number.parseInt(elements.pageSize?.value || '10', 10);
        state.pageSize = allowedPageSizes.has(size) ? size : 10;
        state.page = 1;
        syncControlsFromState();
        applyAdminMenuFilters();
    };

    const resetAll = () => {
        state.keyword = '';
        state.level = 'all';
        state.type = 'all';
        state.lifecycle = 'all';
        state.page = 1;
        state.pageSize = 10;
        syncControlsFromState();
        applyAdminMenuFilters();
        elements.keyword?.focus();
    };

    elements.keyword?.addEventListener('input', () => {
        state.keyword = elements.keyword.value.trim();
        state.page = 1;
        updateFilterIndicators();
        window.clearTimeout(adminMenuSearchTimer);
        adminMenuSearchTimer = window.setTimeout(applyAdminMenuFilters, 350);
    });

    elements.clearSearch?.addEventListener('click', () => {
        state.keyword = '';
        state.page = 1;
        if (elements.keyword) elements.keyword.value = '';
        window.clearTimeout(adminMenuSearchTimer);
        applyAdminMenuFilters();
        elements.keyword?.focus();
    });

    [elements.level, elements.type, elements.lifecycle, elements.pageSize].forEach((control) => {
        control?.addEventListener('change', readDesktopFilters);
    });

    elements.reset?.addEventListener('click', resetAll);
    elements.reload?.addEventListener('click', () => window.location.reload());

    document.querySelectorAll('[data-admin-menu-kpi]').forEach((button) => {
        button.addEventListener('click', () => {
            const key = button.dataset.adminMenuKpi;
            if (key === 'total') {
                state.type = 'all';
                state.lifecycle = 'all';
            } else if (key === 'active') {
                state.lifecycle = state.lifecycle === 'active' ? 'all' : 'active';
            } else if (key === 'inactive') {
                state.lifecycle = state.lifecycle === 'inactive' ? 'all' : 'inactive';
            } else if (key === 'custom') {
                state.type = state.type === 'custom' ? 'all' : 'custom';
            }
            state.page = 1;
            syncControlsFromState();
            applyAdminMenuFilters();
        });
    });

    elements.pageNumbers?.addEventListener('click', (event) => {
        const button = event.target.closest('[data-admin-menu-page]');
        if (!button || button.disabled) return;
        state.page = Math.max(1, Number.parseInt(button.dataset.adminMenuPage || '1', 10));
        applyAdminMenuFilters();
        document.getElementById('adminMenuResults')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });

    elements.mobileFilterSheet?.addEventListener('show.bs.offcanvas', () => syncControlsFromState());

    elements.mobileClear?.addEventListener('click', () => {
        if (elements.mobileLevel) elements.mobileLevel.value = 'all';
        if (elements.mobileType) elements.mobileType.value = 'all';
        if (elements.mobileLifecycle) elements.mobileLifecycle.value = 'all';
        if (elements.mobilePageSize) elements.mobilePageSize.value = '10';
    });

    elements.mobileApply?.addEventListener('click', () => {
        state.level = allowedLevels.has(elements.mobileLevel?.value) ? elements.mobileLevel.value : 'all';
        state.type = allowedTypes.has(elements.mobileType?.value) ? elements.mobileType.value : 'all';
        state.lifecycle = allowedLifecycles.has(elements.mobileLifecycle?.value) ? elements.mobileLifecycle.value : 'all';
        const size = Number.parseInt(elements.mobilePageSize?.value || '10', 10);
        state.pageSize = allowedPageSizes.has(size) ? size : 10;
        state.page = 1;
        syncControlsFromState();
        applyAdminMenuFilters();
        if (elements.mobileFilterSheet && window.bootstrap?.Offcanvas) {
            window.bootstrap.Offcanvas.getOrCreateInstance(elements.mobileFilterSheet).hide();
        }
    });

    root.addEventListener('click', (event) => {
        const collapseButton = event.target.closest('[data-admin-menu-collapse]');
        if (collapseButton && !collapseButton.disabled) {
            const key = collapseButton.dataset.adminMenuCollapse;
            if (collapsedParents.has(key)) collapsedParents.delete(key);
            else collapsedParents.add(key);
            applyAdminMenuFilters();
            return;
        }

        const quickButton = event.target.closest('[data-admin-menu-quick-view]');
        if (quickButton) {
            openAdminMenuQuickView(quickButton);
            return;
        }

        const deleteTrigger = event.target.closest('[data-admin-menu-delete-trigger]');
        if (deleteTrigger) {
            showDeleteConfirmation(deleteTrigger.closest('[data-admin-menu-delete-form]'));
            return;
        }

        const mobileDelete = event.target.closest('[data-admin-menu-mobile-delete]');
        if (mobileDelete) {
            const row = desktopByKey.get(mobileDelete.dataset.adminMenuMobileDelete);
            showDeleteConfirmation(row?.querySelector('[data-admin-menu-delete-form]'));
        }
    });

    root.addEventListener('dblclick', (event) => {
        if (event.target.closest('a, button, form, input, select')) return;
        const source = event.target.closest('[data-admin-menu-item], [data-admin-menu-mobile-item]');
        if (source) openAdminMenuQuickView(source);
    });

    elements.confirmDelete?.addEventListener('click', () => {
        if (!pendingDeleteForm) return;
        elements.confirmDelete.disabled = true;
        pendingDeleteForm.requestSubmit();
    });

    elements.deleteConfirmation?.addEventListener('hidden.bs.modal', () => {
        pendingDeleteForm = null;
        if (elements.confirmDelete) elements.confirmDelete.disabled = false;
    });

    window.addEventListener('popstate', () => {
        const current = new URLSearchParams(window.location.search);
        const size = Number.parseInt(current.get('pageSize') || '10', 10);
        state.keyword = (current.get('keyword') || '').trim();
        state.level = allowedLevels.has(current.get('level')) ? current.get('level') : 'all';
        state.type = allowedTypes.has(current.get('type')) ? current.get('type') : 'all';
        state.lifecycle = allowedLifecycles.has(current.get('lifecycle')) ? current.get('lifecycle') : 'all';
        state.page = Math.max(1, Number.parseInt(current.get('page') || '1', 10) || 1);
        state.pageSize = allowedPageSizes.has(size) ? size : 10;
        syncControlsFromState();
        applyAdminMenuFilters();
    });

    syncControlsFromState();
    applyAdminMenuFilters();
})();
