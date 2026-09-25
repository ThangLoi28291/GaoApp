(() => {
    'use strict';

    const root = document.querySelector('[data-role-index]');
    if (!root) return;

    const allowedTypes = new Set(['all', 'system', 'custom']);
    const allowedLifecycles = new Set(['all', 'active', 'inactive']);
    const allowedPageSizes = new Set([10, 20, 50]);
    const initialQuery = new URLSearchParams(window.location.search);

    const normalizeChoice = (value, allowed, fallback) => allowed.has(value) ? value : fallback;
    const initialPageSize = Number.parseInt(initialQuery.get('pageSize') || '10', 10);

    const state = {
        keyword: (initialQuery.get('keyword') || '').trim(),
        type: normalizeChoice(initialQuery.get('type') || 'all', allowedTypes, 'all'),
        lifecycle: normalizeChoice(initialQuery.get('lifecycle') || 'all', allowedLifecycles, 'all'),
        page: Math.max(1, Number.parseInt(initialQuery.get('page') || '1', 10) || 1),
        pageSize: allowedPageSizes.has(initialPageSize) ? initialPageSize : 10
    };

    const urls = {
        data: root.dataset.dataUrl || '',
        edit: root.dataset.editUrl || '',
        permissions: root.dataset.permissionsUrl || '',
        delete: root.dataset.deleteUrl || ''
    };

    const permissions = {
        canUpdate: root.dataset.canUpdate === 'true',
        canDelete: root.dataset.canDelete === 'true',
        canPermissions: root.dataset.canPermissions === 'true'
    };

    const elements = {
        keyword: document.getElementById('roleKeyword'),
        clearSearch: document.getElementById('roleClearSearch'),
        type: document.getElementById('roleType'),
        lifecycle: document.getElementById('roleLifecycle'),
        pageSize: document.getElementById('rolePageSize'),
        reset: document.getElementById('roleResetFilters'),
        reload: document.getElementById('btnRoleReload'),
        results: document.getElementById('roleResultsPanel'),
        tableBody: document.getElementById('roleTableBody'),
        mobileList: document.getElementById('roleMobileList'),
        pagingInfo: document.getElementById('rolePagingInfo'),
        pageNumbers: document.getElementById('rolePageNumbers'),
        statTotal: document.getElementById('roleStatTotal'),
        statActive: document.getElementById('roleStatActive'),
        statInactive: document.getElementById('roleStatInactive'),
        statSystem: document.getElementById('roleStatSystem'),
        activeFilterCount: document.getElementById('roleActiveFilterCount'),
        mobileSheet: document.getElementById('roleMobileFilterSheet'),
        mobileType: document.getElementById('roleMobileType'),
        mobileLifecycle: document.getElementById('roleMobileLifecycle'),
        mobilePageSize: document.getElementById('roleMobilePageSize'),
        mobileApply: document.getElementById('roleMobileApplyFilters'),
        mobileClear: document.getElementById('roleMobileClearFilters'),
        deleteToken: document.querySelector('#roleDeleteToken input[name="__RequestVerificationToken"]')
    };

    let roleIndexSearchTimer = 0;
    let roleIndexRequestSequence = 0;
    let activeRequest = null;

    const escapeHtml = (value) => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');

    const escapeAttribute = escapeHtml;

    const formatDate = (value) => {
        if (!value) return '—';
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return '—';
        return new Intl.DateTimeFormat('vi-VN', {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric'
        }).format(date);
    };

    const buildActionUrl = (baseUrl, key, value) => {
        const url = new URL(baseUrl, window.location.origin);
        url.searchParams.set(key, String(value));
        return `${url.pathname}${url.search}`;
    };

    const syncControlsFromState = () => {
        if (elements.keyword) elements.keyword.value = state.keyword;
        if (elements.type) elements.type.value = state.type;
        if (elements.lifecycle) elements.lifecycle.value = state.lifecycle;
        if (elements.pageSize) elements.pageSize.value = String(state.pageSize);
        if (elements.mobileType) elements.mobileType.value = state.type;
        if (elements.mobileLifecycle) elements.mobileLifecycle.value = state.lifecycle;
        if (elements.mobilePageSize) elements.mobilePageSize.value = String(state.pageSize);
        updateFilterIndicators();
    };

    const updateUrl = () => {
        const query = new URLSearchParams();
        if (state.keyword) query.set('keyword', state.keyword);
        if (state.type !== 'all') query.set('type', state.type);
        if (state.lifecycle !== 'all') query.set('lifecycle', state.lifecycle);
        if (state.page > 1) query.set('page', String(state.page));
        if (state.pageSize !== 10) query.set('pageSize', String(state.pageSize));
        const nextUrl = query.size ? `${window.location.pathname}?${query}` : window.location.pathname;
        window.history.replaceState({ roleIndex: true }, '', nextUrl);
    };

    const updateFilterIndicators = () => {
        const filterCount = Number(state.type !== 'all')
            + Number(state.lifecycle !== 'all')
            + Number(state.pageSize !== 10);
        const hasAnyFilter = filterCount > 0 || Boolean(state.keyword);

        elements.clearSearch?.classList.toggle('d-none', !state.keyword);
        elements.reset?.classList.toggle('d-none', !hasAnyFilter);
        if (elements.activeFilterCount) {
            elements.activeFilterCount.textContent = String(filterCount);
            elements.activeFilterCount.classList.toggle('d-none', filterCount === 0);
        }

        document.querySelectorAll('[data-role-kpi]').forEach((button) => {
            const key = button.dataset.roleKpi;
            const active = (key === 'total' && state.type === 'all' && state.lifecycle === 'all')
                || (key === 'active' && state.lifecycle === 'active')
                || (key === 'inactive' && state.lifecycle === 'inactive')
                || (key === 'system' && state.type === 'system');
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
    };

    const setLoading = (isLoading) => {
        elements.results?.setAttribute('aria-busy', isLoading ? 'true' : 'false');
        elements.results?.classList.toggle('role-index-loading', isLoading);
        if (elements.reload) elements.reload.disabled = isLoading;
    };

    const buildQuery = () => {
        const query = new URLSearchParams({
            type: state.type,
            lifecycle: state.lifecycle,
            page: String(state.page),
            pageSize: String(state.pageSize)
        });
        if (state.keyword) query.set('keyword', state.keyword);
        return query;
    };

    const renderSummary = (summary) => {
        if (elements.statTotal) elements.statTotal.textContent = String(summary?.totalRoles ?? 0);
        if (elements.statActive) elements.statActive.textContent = String(summary?.activeRoles ?? 0);
        if (elements.statInactive) elements.statInactive.textContent = String(summary?.inactiveRoles ?? 0);
        if (elements.statSystem) elements.statSystem.textContent = String(summary?.systemRoles ?? 0);
    };

    const renderScope = (item) => item.isSystemRole
        ? '<span class="role-index-scope is-system"><i class="bx bx-lock-alt"></i>Hệ thống</span>'
        : '<span class="role-index-scope is-custom"><i class="bx bx-user-check"></i>Tùy chỉnh</span>';

    const renderStatus = (item) => item.isActive
        ? '<span class="role-index-status is-active"><i class="bx bx-check-circle"></i>Đang hoạt động</span>'
        : '<span class="role-index-status is-inactive"><i class="bx bx-pause-circle"></i>Ngưng sử dụng</span>';

    const renderActions = (item, responsePermissions, mobile = false) => {
        const canUpdate = responsePermissions.canUpdate ?? permissions.canUpdate;
        const canDelete = responsePermissions.canDelete ?? permissions.canDelete;
        const canPermissions = responsePermissions.canPermissions ?? permissions.canPermissions;
        const actionClass = mobile ? 'btn btn-sm role-index-action' : 'btn btn-sm role-index-action';
        const actions = [];

        if (canPermissions) {
            const href = buildActionUrl(urls.permissions, 'roleId', item.roleId);
            actions.push(`<a class="${actionClass} btn-primary" href="${escapeAttribute(href)}" title="Phân quyền"><i class="bx bx-key"></i><span>Phân quyền</span></a>`);
        }

        if (canUpdate) {
            const href = buildActionUrl(urls.edit, 'id', item.roleId);
            actions.push(`<a class="${actionClass} btn-label-primary" href="${escapeAttribute(href)}" title="Sửa vai trò"><i class="bx bx-edit-alt"></i><span>Sửa</span></a>`);
        }

        if (item.isSystemRole) {
            actions.push('<span class="role-index-lock-hint" title="Vai trò hệ thống không thể xóa"><i class="bx bx-lock-alt"></i>Được hệ thống bảo vệ</span>');
        } else if (!item.isActive) {
            actions.push('<span class="role-index-lock-hint" title="Vai trò đã ngưng sử dụng"><i class="bx bx-pause-circle"></i>Đã ngưng sử dụng</span>');
        } else if ((item.assignedUserCount ?? 0) > 0) {
            actions.push(`<span class="role-index-lock-hint" title="Vai trò đang được gán nên không thể xóa"><i class="bx bx-user-check"></i>Đang gán ${escapeHtml(item.assignedUserCount)} nhân viên</span>`);
        } else if (canDelete) {
            actions.push(`<button type="button" class="${actionClass} btn-label-danger" data-role-delete="${escapeAttribute(item.roleId)}" data-role-name="${escapeAttribute(item.name)}" title="Xóa vai trò"><i class="bx bx-trash"></i><span>Xóa</span></button>`);
        }

        return actions.length ? actions.join('') : '<span class="text-muted small">Không có thao tác</span>';
    };

    function renderRoleDesktopRows(items, responsePermissions) {
        if (!elements.tableBody) return;
        if (!items?.length) {
            elements.tableBody.innerHTML = '<tr><td colspan="7" class="gds-empty"><i class="bx bx-shield-x d-block fs-2 mb-2"></i>Không có vai trò phù hợp.</td></tr>';
            return;
        }

        elements.tableBody.innerHTML = items.map((item) => `
            <tr>
                <td><div class="role-index-identity"><div class="role-index-name" title="${escapeAttribute(item.name)}">${escapeHtml(item.name)}</div><span class="role-index-code" title="${escapeAttribute(item.code)}">${escapeHtml(item.code)}</span></div></td>
                <td>${renderScope(item)}</td>
                <td>${renderStatus(item)}</td>
                <td class="text-center"><span class="role-index-count">${escapeHtml(item.permissionCount)}</span><span class="role-index-count-note">quyền</span></td>
                <td class="text-center"><span class="role-index-count">${escapeHtml(item.activeUserCount)}</span><span class="role-index-count-note">đang hoạt động</span></td>
                <td><span class="text-body fw-semibold">${escapeHtml(formatDate(item.createdAtUtc))}</span></td>
                <td><div class="role-index-actions">${renderActions(item, responsePermissions)}</div></td>
            </tr>`).join('');
    }

    function renderRoleMobileCards(items, responsePermissions) {
        if (!elements.mobileList) return;
        if (!items?.length) {
            elements.mobileList.innerHTML = '<div class="gds-empty"><i class="bx bx-shield-x d-block fs-2 mb-2"></i>Không có vai trò phù hợp.</div>';
            return;
        }

        elements.mobileList.innerHTML = items.map((item) => `
            <article class="role-index-mobile-card">
                <div class="role-index-mobile-card__header">
                    <div class="role-index-identity"><div class="role-index-name">${escapeHtml(item.name)}</div><span class="role-index-code">${escapeHtml(item.code)}</span></div>
                    ${renderStatus(item)}
                </div>
                <div class="role-index-mobile-card__badges">${renderScope(item)}</div>
                <div class="role-index-mobile-card__metrics">
                    <div class="role-index-mobile-card__metric"><span>Quyền đang chọn</span><strong>${escapeHtml(item.permissionCount)} quyền</strong></div>
                    <div class="role-index-mobile-card__metric"><span>Nhân viên hoạt động</span><strong>${escapeHtml(item.activeUserCount)} nhân viên</strong></div>
                    <div class="role-index-mobile-card__metric"><span>Đang được gán</span><strong>${escapeHtml(item.assignedUserCount)} nhân viên</strong></div>
                    <div class="role-index-mobile-card__metric"><span>Ngày tạo</span><strong>${escapeHtml(formatDate(item.createdAtUtc))}</strong></div>
                </div>
                <div class="role-index-mobile-card__actions">${renderActions(item, responsePermissions, true)}</div>
            </article>`).join('');
    }

    const paginationButton = (page, label, icon, disabled, active = false) => `
        <li class="page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}">
            <button type="button" class="page-link" data-role-page="${page}" aria-label="${escapeAttribute(label)}" ${disabled ? 'disabled' : ''}>${icon || page}</button>
        </li>`;

    function renderRoleCircularPagination(page, totalPages, totalItems, pageSize) {
        const safeTotalPages = Math.max(1, totalPages || 1);
        const safePage = Math.min(Math.max(1, page || 1), safeTotalPages);
        const start = totalItems === 0 ? 0 : ((safePage - 1) * pageSize) + 1;
        const end = Math.min(totalItems, safePage * pageSize);
        if (elements.pagingInfo) {
            elements.pagingInfo.textContent = totalItems === 0
                ? '0 kết quả'
                : `${start}–${end} trên ${totalItems} vai trò`;
        }
        if (!elements.pageNumbers) return;

        const pages = [];
        const first = Math.max(1, Math.min(safePage - 2, safeTotalPages - 4));
        const last = Math.min(safeTotalPages, first + 4);
        pages.push(paginationButton(safePage - 1, 'Trang trước', '<i class="bx bx-chevron-left"></i>', safePage <= 1));
        for (let value = first; value <= last; value += 1) {
            pages.push(paginationButton(value, `Trang ${value}`, '', false, value === safePage));
        }
        pages.push(paginationButton(safePage + 1, 'Trang sau', '<i class="bx bx-chevron-right"></i>', safePage >= safeTotalPages));
        elements.pageNumbers.innerHTML = pages.join('');
    }

    const renderError = (message) => {
        const content = `<div class="gds-empty text-danger"><i class="bx bx-error-circle d-block fs-2 mb-2"></i>${escapeHtml(message)}</div>`;
        if (elements.tableBody) elements.tableBody.innerHTML = `<tr><td colspan="7">${content}</td></tr>`;
        if (elements.mobileList) elements.mobileList.innerHTML = content;
        if (elements.pagingInfo) elements.pagingInfo.textContent = 'Không thể tải dữ liệu';
        if (elements.pageNumbers) elements.pageNumbers.innerHTML = '';
    };

    const loadRoles = async () => {
        if (!urls.data) return;
        activeRequest?.abort();
        activeRequest = new AbortController();
        const requestSequence = ++roleIndexRequestSequence;
        setLoading(true);
        updateUrl();

        try {
            const response = await fetch(`${urls.data}?${buildQuery()}`, {
                headers: { Accept: 'application/json' },
                signal: activeRequest.signal
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const data = await response.json();
            if (requestSequence !== roleIndexRequestSequence) return;

            state.page = data.page || 1;
            permissions.canUpdate = data.canUpdate ?? permissions.canUpdate;
            permissions.canDelete = data.canDelete ?? permissions.canDelete;
            permissions.canPermissions = data.canPermissions ?? permissions.canPermissions;
            renderSummary(data.summary);
            renderRoleDesktopRows(data.items, permissions);
            renderRoleMobileCards(data.items, permissions);
            renderRoleCircularPagination(data.page, data.totalPages, data.totalItems, data.pageSize);
            updateUrl();
        } catch (error) {
            if (error?.name !== 'AbortError' && requestSequence === roleIndexRequestSequence) {
                renderError('Không thể tải danh sách vai trò. Vui lòng thử lại.');
            }
        } finally {
            if (requestSequence === roleIndexRequestSequence) setLoading(false);
        }
    };

    const applyDesktopFilters = () => {
        state.type = normalizeChoice(elements.type?.value || 'all', allowedTypes, 'all');
        state.lifecycle = normalizeChoice(elements.lifecycle?.value || 'all', allowedLifecycles, 'all');
        const pageSize = Number.parseInt(elements.pageSize?.value || '10', 10);
        state.pageSize = allowedPageSizes.has(pageSize) ? pageSize : 10;
        state.page = 1;
        syncControlsFromState();
        loadRoles();
    };

    const resetAll = () => {
        state.keyword = '';
        state.type = 'all';
        state.lifecycle = 'all';
        state.page = 1;
        state.pageSize = 10;
        syncControlsFromState();
        loadRoles();
    };

    const submitDelete = (roleId, roleName) => {
        if (!permissions.canDelete || !urls.delete || !elements.deleteToken) return;
        if (!window.confirm(`Bạn có chắc chắn muốn xóa vai trò “${roleName}” không?`)) return;

        const form = document.createElement('form');
        form.method = 'post';
        form.action = urls.delete;
        form.className = 'd-none';

        const roleInput = document.createElement('input');
        roleInput.type = 'hidden';
        roleInput.name = 'id';
        roleInput.value = String(roleId);
        form.append(roleInput);
        form.append(elements.deleteToken.cloneNode(true));
        document.body.append(form);
        form.submit();
    };

    elements.keyword?.addEventListener('input', () => {
        state.keyword = elements.keyword.value.trim();
        state.page = 1;
        updateFilterIndicators();
        window.clearTimeout(roleIndexSearchTimer);
        roleIndexSearchTimer = window.setTimeout(loadRoles, 350);
    });

    elements.clearSearch?.addEventListener('click', () => {
        state.keyword = '';
        state.page = 1;
        if (elements.keyword) {
            elements.keyword.value = '';
            elements.keyword.focus();
        }
        window.clearTimeout(roleIndexSearchTimer);
        updateFilterIndicators();
        loadRoles();
    });

    [elements.type, elements.lifecycle, elements.pageSize].forEach((control) => {
        control?.addEventListener('change', applyDesktopFilters);
    });

    elements.reset?.addEventListener('click', resetAll);
    elements.reload?.addEventListener('click', loadRoles);

    document.querySelectorAll('[data-role-kpi]').forEach((button) => {
        button.addEventListener('click', () => {
            const key = button.dataset.roleKpi;
            if (key === 'total') {
                state.type = 'all';
                state.lifecycle = 'all';
            } else if (key === 'active') {
                state.lifecycle = state.lifecycle === 'active' ? 'all' : 'active';
            } else if (key === 'inactive') {
                state.lifecycle = state.lifecycle === 'inactive' ? 'all' : 'inactive';
            } else if (key === 'system') {
                state.type = state.type === 'system' ? 'all' : 'system';
            }
            state.page = 1;
            syncControlsFromState();
            loadRoles();
        });
    });

    elements.pageNumbers?.addEventListener('click', (event) => {
        const button = event.target.closest('[data-role-page]');
        if (!button || button.disabled) return;
        state.page = Math.max(1, Number.parseInt(button.dataset.rolePage, 10) || 1);
        loadRoles();
        elements.results?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });

    elements.mobileSheet?.addEventListener('show.bs.offcanvas', () => {
        if (elements.mobileType) elements.mobileType.value = state.type;
        if (elements.mobileLifecycle) elements.mobileLifecycle.value = state.lifecycle;
        if (elements.mobilePageSize) elements.mobilePageSize.value = String(state.pageSize);
    });

    elements.mobileClear?.addEventListener('click', () => {
        if (elements.mobileType) elements.mobileType.value = 'all';
        if (elements.mobileLifecycle) elements.mobileLifecycle.value = 'all';
        if (elements.mobilePageSize) elements.mobilePageSize.value = '10';
    });

    elements.mobileApply?.addEventListener('click', () => {
        state.type = normalizeChoice(elements.mobileType?.value || 'all', allowedTypes, 'all');
        state.lifecycle = normalizeChoice(elements.mobileLifecycle?.value || 'all', allowedLifecycles, 'all');
        const pageSize = Number.parseInt(elements.mobilePageSize?.value || '10', 10);
        state.pageSize = allowedPageSizes.has(pageSize) ? pageSize : 10;
        state.page = 1;
        syncControlsFromState();
        loadRoles();
        if (elements.mobileSheet && window.bootstrap?.Offcanvas) {
            window.bootstrap.Offcanvas.getOrCreateInstance(elements.mobileSheet).hide();
        }
    });

    root.addEventListener('click', (event) => {
        const deleteButton = event.target.closest('[data-role-delete]');
        if (!deleteButton) return;
        submitDelete(deleteButton.dataset.roleDelete, deleteButton.dataset.roleName || '');
    });

    window.addEventListener('popstate', () => {
        const query = new URLSearchParams(window.location.search);
        const pageSize = Number.parseInt(query.get('pageSize') || '10', 10);
        state.keyword = (query.get('keyword') || '').trim();
        state.type = normalizeChoice(query.get('type') || 'all', allowedTypes, 'all');
        state.lifecycle = normalizeChoice(query.get('lifecycle') || 'all', allowedLifecycles, 'all');
        state.page = Math.max(1, Number.parseInt(query.get('page') || '1', 10) || 1);
        state.pageSize = allowedPageSizes.has(pageSize) ? pageSize : 10;
        syncControlsFromState();
        loadRoles();
    });

    syncControlsFromState();
    loadRoles();
})();
