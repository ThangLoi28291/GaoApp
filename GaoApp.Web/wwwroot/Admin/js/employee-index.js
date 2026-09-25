(() => {
    'use strict';

    const root = document.querySelector('[data-employee-index]');
    if (!root) return;

    const allowedLifecycles = new Set(['all', 'active', 'inactive']);
    const allowedPageSizes = new Set([10, 20, 50]);
    const initialQuery = new URLSearchParams(window.location.search);

    const normalizeChoice = (value, allowed, fallback) => allowed.has(value) ? value : fallback;
    const normalizeRoleId = (value) => {
        const parsed = Number.parseInt(value || '', 10);
        return Number.isInteger(parsed) && parsed > 0 ? String(parsed) : '';
    };
    const initialPageSize = Number.parseInt(initialQuery.get('pageSize') || '10', 10);

    const state = {
        keyword: (initialQuery.get('keyword') || '').trim(),
        roleId: normalizeRoleId(initialQuery.get('roleId')),
        lifecycle: normalizeChoice(initialQuery.get('lifecycle') || 'all', allowedLifecycles, 'all'),
        page: Math.max(1, Number.parseInt(initialQuery.get('page') || '1', 10) || 1),
        pageSize: allowedPageSizes.has(initialPageSize) ? initialPageSize : 10
    };

    const urls = {
        data: root.dataset.dataUrl || '',
        edit: root.dataset.editUrl || ''
    };

    const permissions = {
        canUpdate: root.dataset.canUpdate === 'true'
    };

    const elements = {
        keyword: document.getElementById('employeeKeyword'),
        clearSearch: document.getElementById('employeeClearSearch'),
        role: document.getElementById('employeeRole'),
        lifecycle: document.getElementById('employeeLifecycle'),
        pageSize: document.getElementById('employeePageSize'),
        reset: document.getElementById('employeeResetFilters'),
        reload: document.getElementById('btnEmployeeReload'),
        results: document.getElementById('employeeResultsPanel'),
        tableBody: document.getElementById('employeeTableBody'),
        mobileList: document.getElementById('employeeMobileList'),
        pagingInfo: document.getElementById('employeePagingInfo'),
        pageNumbers: document.getElementById('employeePageNumbers'),
        statTotal: document.getElementById('employeeStatTotal'),
        statActive: document.getElementById('employeeStatActive'),
        statInactive: document.getElementById('employeeStatInactive'),
        statRoles: document.getElementById('employeeStatRoles'),
        activeFilterCount: document.getElementById('employeeActiveFilterCount'),
        mobileSheet: document.getElementById('employeeMobileFilterSheet'),
        mobileRole: document.getElementById('employeeMobileRole'),
        mobileLifecycle: document.getElementById('employeeMobileLifecycle'),
        mobilePageSize: document.getElementById('employeeMobilePageSize'),
        mobileApply: document.getElementById('employeeMobileApplyFilters'),
        mobileClear: document.getElementById('employeeMobileClearFilters'),
        quickView: document.getElementById('employeeQuickView'),
        quickAvatar: document.getElementById('employeeQuickAvatar'),
        quickName: document.getElementById('employeeQuickName'),
        quickUsername: document.getElementById('employeeQuickUsername'),
        quickStatus: document.getElementById('employeeQuickStatus'),
        quickEmail: document.getElementById('employeeQuickEmail'),
        quickPhone: document.getElementById('employeeQuickPhone'),
        quickRole: document.getElementById('employeeQuickRole'),
        quickPosition: document.getElementById('employeeQuickPosition'),
        quickJoined: document.getElementById('employeeQuickJoined'),
        quickAssigned: document.getElementById('employeeQuickAssigned'),
        quickNote: document.getElementById('employeeQuickNote'),
        quickEdit: document.getElementById('employeeQuickEdit'),
        resetModal: document.getElementById('employeeResetPasswordModal'),
        resetForm: document.getElementById('employeeResetPasswordForm'),
        resetUserId: document.getElementById('employeeResetUserId'),
        resetIdentity: document.getElementById('employeeResetIdentity'),
        newPassword: document.getElementById('employeeNewPassword'),
        confirmPassword: document.getElementById('employeeConfirmPassword'),
        toggleModal: document.getElementById('employeeToggleModal'),
        toggleMappingId: document.getElementById('employeeToggleMappingId'),
        toggleTitle: document.getElementById('employeeToggleTitle'),
        toggleIdentity: document.getElementById('employeeToggleIdentity'),
        toggleMessage: document.getElementById('employeeToggleMessage'),
        toggleConfirm: document.getElementById('employeeToggleConfirm')
    };

    let employeeIndexSearchTimer = 0;
    let employeeIndexRequestSequence = 0;
    let activeRequest = null;
    let employeeItems = new Map();

    const escapeHtml = (value) => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');

    const escapeAttribute = escapeHtml;
    const displayValue = (value, fallback = '—') => {
        const normalized = String(value ?? '').trim();
        return normalized || fallback;
    };

    const displayName = (item) => displayValue(item.fullName, displayValue(item.username, 'Chưa đặt tên'));

    const initials = (item) => {
        const words = displayName(item).split(/\s+/).filter(Boolean);
        if (!words.length) return '?';
        return `${words[0][0] || ''}${words.length > 1 ? words[words.length - 1][0] || '' : ''}`.toLocaleUpperCase('vi-VN');
    };

    const formatDate = (value, preserveDatePart = false) => {
        if (!value) return 'Chưa cập nhật';
        if (preserveDatePart) {
            const match = String(value).match(/^(\d{4})-(\d{2})-(\d{2})/);
            if (match) return `${match[3]}/${match[2]}/${match[1]}`;
        }
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return 'Chưa cập nhật';
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

    const renderStatus = (item, interactive = false, responsePermissions = permissions) => {
        const canUpdate = responsePermissions.canUpdate ?? permissions.canUpdate;
        const stateClass = item.isActive ? 'is-active' : 'is-inactive';
        const icon = item.isActive ? 'bx-check-circle' : 'bx-pause-circle';
        const label = item.isActive ? 'Đang làm việc' : 'Ngưng làm';

        if (interactive && canUpdate && item.canToggleActive) {
            return `<button type="button" class="employee-index-status employee-index-status-action ${stateClass}" data-employee-toggle="${escapeAttribute(item.mappingId)}" title="Bấm để ${item.isActive ? 'khóa' : 'mở lại'} nhân viên"><i class="bx ${icon}"></i>${label}</button>`;
        }

        return `<span class="employee-index-status ${stateClass}"><i class="bx ${icon}"></i>${label}</span>`;
    };

    const renderPerson = (item) => `
        <div class="employee-index-person">
            <span class="employee-index-avatar">${escapeHtml(initials(item))}</span>
            <div class="employee-index-person__copy">
                <div class="employee-index-name" title="${escapeAttribute(displayName(item))}">${escapeHtml(displayName(item))}</div>
                <span class="employee-index-username" title="${escapeAttribute(item.username)}">${escapeHtml(displayValue(item.username))}</span>
            </div>
        </div>`;

    const renderContact = (item) => `
        <div class="employee-index-contact">
            <div class="employee-index-primary-line" title="${escapeAttribute(displayValue(item.phoneNumber))}"><i class="bx bx-phone me-1"></i>${escapeHtml(displayValue(item.phoneNumber))}</div>
            <div class="employee-index-secondary-line" title="${escapeAttribute(displayValue(item.email))}">${escapeHtml(displayValue(item.email))}</div>
        </div>`;

    const renderRole = (item) => `
        <div class="employee-index-role">
            <div class="employee-index-primary-line" title="${escapeAttribute(item.roleName)}">${escapeHtml(displayValue(item.roleName))}</div>
            <div class="employee-index-secondary-line" title="${escapeAttribute(displayValue(item.positionName, item.roleCode))}">${escapeHtml(displayValue(item.positionName, displayValue(item.roleCode)))}</div>
        </div>`;

    const renderActions = (item, responsePermissions, mobile = false) => {
        const canUpdate = responsePermissions.canUpdate ?? permissions.canUpdate;
        const labelClass = mobile ? '' : ' visually-hidden';
        const actions = [
            `<button type="button" class="btn btn-sm btn-label-secondary employee-index-action" data-employee-quick="${escapeAttribute(item.mappingId)}" title="Xem nhanh"><i class="bx bx-show"></i><span class="${labelClass}">Xem</span></button>`
        ];

        if (canUpdate && item.canEdit) {
            const href = buildActionUrl(urls.edit, 'id', item.mappingId);
            actions.push(`<a class="btn btn-sm btn-label-primary employee-index-action" href="${escapeAttribute(href)}" title="Sửa nhân viên"><i class="bx bx-edit-alt"></i><span class="${labelClass}">Sửa</span></a>`);
        }

        if (canUpdate && item.canResetPassword) {
            actions.push(`<button type="button" class="btn btn-sm btn-label-secondary employee-index-action" data-employee-reset="${escapeAttribute(item.mappingId)}" title="Đặt lại mật khẩu"><i class="bx bx-key"></i><span class="${labelClass}">Mật khẩu</span></button>`);
        }

        if (canUpdate && item.canToggleActive) {
            const style = item.isActive ? 'btn-label-warning' : 'btn-label-success';
            const icon = item.isActive ? 'bx-user-x' : 'bx-user-check';
            const label = item.isActive ? 'Khóa' : 'Mở lại';
            actions.push(`<button type="button" class="btn btn-sm ${style} employee-index-action" data-employee-toggle="${escapeAttribute(item.mappingId)}" title="${label} nhân viên"><i class="bx ${icon}"></i><span class="${labelClass}">${label}</span></button>`);
        } else if (canUpdate && item.actionLockReason) {
            actions.push(`<span class="employee-index-action-lock" title="${escapeAttribute(item.actionLockReason)}"><i class="bx bx-lock-alt"></i>${escapeHtml(item.actionLockReason)}</span>`);
        }

        return actions.join('');
    };

    function renderEmployeeDesktopRows(items, responsePermissions) {
        if (!elements.tableBody) return;
        if (!items?.length) {
            elements.tableBody.innerHTML = '<tr><td colspan="6" class="gds-empty"><i class="bx bx-user-x d-block fs-2 mb-2"></i>Không có nhân viên phù hợp.</td></tr>';
            return;
        }

        elements.tableBody.innerHTML = items.map((item) => `
            <tr data-employee-row="${escapeAttribute(item.mappingId)}" tabindex="0" title="Nhấp đúp để xem nhanh">
                <td>${renderPerson(item)}</td>
                <td>${renderContact(item)}</td>
                <td>${renderRole(item)}</td>
                <td><span class="text-body fw-semibold">${escapeHtml(formatDate(item.joinedDate, true))}</span></td>
                <td>${renderStatus(item, true, responsePermissions)}</td>
                <td><div class="employee-index-actions">${renderActions(item, responsePermissions)}</div></td>
            </tr>`).join('');
    }

    function renderEmployeeMobileCards(items, responsePermissions) {
        if (!elements.mobileList) return;
        if (!items?.length) {
            elements.mobileList.innerHTML = '<div class="gds-empty"><i class="bx bx-user-x d-block fs-2 mb-2"></i>Không có nhân viên phù hợp.</div>';
            return;
        }

        elements.mobileList.innerHTML = items.map((item) => `
            <article class="employee-index-mobile-card" data-employee-row="${escapeAttribute(item.mappingId)}">
                <div class="employee-index-mobile-card__header">${renderPerson(item)}${renderStatus(item, true, responsePermissions)}</div>
                <div class="employee-index-mobile-card__details">
                    <div class="employee-index-mobile-card__detail"><span>Vai trò / chức vụ</span><strong>${escapeHtml(displayValue(item.roleName))}${item.positionName ? ` · ${escapeHtml(item.positionName)}` : ''}</strong></div>
                    <div class="employee-index-mobile-card__detail"><span>Ngày vào làm</span><strong>${escapeHtml(formatDate(item.joinedDate, true))}</strong></div>
                    <div class="employee-index-mobile-card__detail"><span>Điện thoại</span><strong>${escapeHtml(displayValue(item.phoneNumber))}</strong></div>
                    <div class="employee-index-mobile-card__detail"><span>Email</span><strong>${escapeHtml(displayValue(item.email))}</strong></div>
                </div>
                <div class="employee-index-mobile-card__actions">${renderActions(item, responsePermissions, true)}</div>
            </article>`).join('');
    }

    const paginationButton = (page, label, icon, disabled, active = false) => `
        <li class="page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}">
            <button type="button" class="page-link" data-employee-page="${page}" aria-label="${escapeAttribute(label)}" ${disabled ? 'disabled' : ''}>${icon || page}</button>
        </li>`;

    function renderEmployeeCircularPagination(page, totalPages, totalItems, pageSize) {
        const safeTotalPages = Math.max(1, totalPages || 1);
        const safePage = Math.min(Math.max(1, page || 1), safeTotalPages);
        const start = totalItems === 0 ? 0 : ((safePage - 1) * pageSize) + 1;
        const end = Math.min(totalItems, safePage * pageSize);
        if (elements.pagingInfo) {
            elements.pagingInfo.textContent = totalItems === 0
                ? '0 kết quả'
                : `${start}–${end} trên ${totalItems} nhân viên`;
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

    const renderSummary = (summary) => {
        if (elements.statTotal) elements.statTotal.textContent = String(summary?.totalEmployees ?? 0);
        if (elements.statActive) elements.statActive.textContent = String(summary?.activeEmployees ?? 0);
        if (elements.statInactive) elements.statInactive.textContent = String(summary?.inactiveEmployees ?? 0);
        if (elements.statRoles) elements.statRoles.textContent = String(summary?.distinctRoleCount ?? 0);
    };

    const renderRoleOptions = (options) => {
        const markup = '<option value="">Tất cả vai trò</option>' + (options || []).map((role) =>
            `<option value="${escapeAttribute(role.roleId)}">${escapeHtml(role.name)} (${escapeHtml(role.code)})</option>`).join('');
        if (elements.role) elements.role.innerHTML = markup;
        if (elements.mobileRole) elements.mobileRole.innerHTML = markup;
        if (elements.role) elements.role.value = state.roleId;
        if (elements.mobileRole) elements.mobileRole.value = state.roleId;
    };

    const syncControlsFromState = () => {
        if (elements.keyword) elements.keyword.value = state.keyword;
        if (elements.role) elements.role.value = state.roleId;
        if (elements.lifecycle) elements.lifecycle.value = state.lifecycle;
        if (elements.pageSize) elements.pageSize.value = String(state.pageSize);
        if (elements.mobileRole) elements.mobileRole.value = state.roleId;
        if (elements.mobileLifecycle) elements.mobileLifecycle.value = state.lifecycle;
        if (elements.mobilePageSize) elements.mobilePageSize.value = String(state.pageSize);
        updateFilterIndicators();
    };

    const updateUrl = () => {
        const query = new URLSearchParams();
        if (state.keyword) query.set('keyword', state.keyword);
        if (state.roleId) query.set('roleId', state.roleId);
        if (state.lifecycle !== 'all') query.set('lifecycle', state.lifecycle);
        if (state.page > 1) query.set('page', String(state.page));
        if (state.pageSize !== 10) query.set('pageSize', String(state.pageSize));
        const nextUrl = query.size ? `${window.location.pathname}?${query}` : window.location.pathname;
        window.history.replaceState({ employeeIndex: true }, '', nextUrl);
    };

    const updateFilterIndicators = () => {
        const filterCount = Number(Boolean(state.roleId))
            + Number(state.lifecycle !== 'all')
            + Number(state.pageSize !== 10);
        const hasAnyFilter = filterCount > 0 || Boolean(state.keyword);
        elements.clearSearch?.classList.toggle('d-none', !state.keyword);
        elements.reset?.classList.toggle('d-none', !hasAnyFilter);
        if (elements.activeFilterCount) {
            elements.activeFilterCount.textContent = String(filterCount);
            elements.activeFilterCount.classList.toggle('d-none', filterCount === 0);
        }
        document.querySelectorAll('button[data-employee-kpi]').forEach((button) => {
            const key = button.dataset.employeeKpi;
            const active = (key === 'total' && state.lifecycle === 'all')
                || (key === 'active' && state.lifecycle === 'active')
                || (key === 'inactive' && state.lifecycle === 'inactive');
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
    };

    const setLoading = (isLoading) => {
        elements.results?.setAttribute('aria-busy', isLoading ? 'true' : 'false');
        elements.results?.classList.toggle('employee-index-loading', isLoading);
        if (elements.reload) elements.reload.disabled = isLoading;
    };

    const buildQuery = () => {
        const query = new URLSearchParams({
            lifecycle: state.lifecycle,
            page: String(state.page),
            pageSize: String(state.pageSize)
        });
        if (state.keyword) query.set('keyword', state.keyword);
        if (state.roleId) query.set('roleId', state.roleId);
        return query;
    };

    const renderError = (message) => {
        const content = `<div class="gds-empty text-danger"><i class="bx bx-error-circle d-block fs-2 mb-2"></i>${escapeHtml(message)}</div>`;
        if (elements.tableBody) elements.tableBody.innerHTML = `<tr><td colspan="6">${content}</td></tr>`;
        if (elements.mobileList) elements.mobileList.innerHTML = content;
        if (elements.pagingInfo) elements.pagingInfo.textContent = 'Không thể tải dữ liệu';
        if (elements.pageNumbers) elements.pageNumbers.innerHTML = '';
    };

    const loadEmployees = async () => {
        if (!urls.data) return;
        activeRequest?.abort();
        activeRequest = new AbortController();
        const requestSequence = ++employeeIndexRequestSequence;
        setLoading(true);
        updateUrl();

        try {
            const response = await fetch(`${urls.data}?${buildQuery()}`, {
                headers: { Accept: 'application/json' },
                signal: activeRequest.signal
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const data = await response.json();
            if (requestSequence !== employeeIndexRequestSequence) return;

            state.page = data.page || 1;
            permissions.canUpdate = data.canUpdate ?? permissions.canUpdate;
            employeeItems = new Map((data.items || []).map((item) => [String(item.mappingId), item]));
            renderSummary(data.summary);
            renderRoleOptions(data.roleOptions);
            renderEmployeeDesktopRows(data.items, permissions);
            renderEmployeeMobileCards(data.items, permissions);
            renderEmployeeCircularPagination(data.page, data.totalPages, data.totalItems, data.pageSize);
            syncControlsFromState();
            updateUrl();
        } catch (error) {
            if (error?.name !== 'AbortError' && requestSequence === employeeIndexRequestSequence) {
                renderError('Không thể tải danh sách nhân viên. Vui lòng thử lại.');
            }
        } finally {
            if (requestSequence === employeeIndexRequestSequence) setLoading(false);
        }
    };

    function openEmployeeQuickView(item) {
        if (!item || !elements.quickView) return;
        if (elements.quickAvatar) elements.quickAvatar.textContent = initials(item);
        if (elements.quickName) elements.quickName.textContent = displayName(item);
        if (elements.quickUsername) elements.quickUsername.textContent = displayValue(item.username);
        if (elements.quickStatus) elements.quickStatus.innerHTML = renderStatus(item);
        if (elements.quickEmail) elements.quickEmail.textContent = displayValue(item.email);
        if (elements.quickPhone) elements.quickPhone.textContent = displayValue(item.phoneNumber);
        if (elements.quickRole) elements.quickRole.textContent = `${displayValue(item.roleName)} (${displayValue(item.roleCode)})`;
        if (elements.quickPosition) elements.quickPosition.textContent = displayValue(item.positionName);
        if (elements.quickJoined) elements.quickJoined.textContent = formatDate(item.joinedDate, true);
        if (elements.quickAssigned) elements.quickAssigned.textContent = formatDate(item.createdAtUtc);
        if (elements.quickNote) elements.quickNote.textContent = displayValue(item.note);
        if (elements.quickEdit) {
            const showEdit = permissions.canUpdate && item.canEdit;
            elements.quickEdit.classList.toggle('d-none', !showEdit);
            if (showEdit) elements.quickEdit.href = buildActionUrl(urls.edit, 'id', item.mappingId);
        }
        window.bootstrap?.Modal.getOrCreateInstance(elements.quickView).show();
    }

    const openResetPassword = (item) => {
        if (!item || !permissions.canUpdate || !item.canResetPassword || !elements.resetModal) return;
        if (elements.resetUserId) elements.resetUserId.value = String(item.userId);
        if (elements.resetIdentity) elements.resetIdentity.textContent = `${displayName(item)} (${displayValue(item.username)})`;
        if (elements.newPassword) elements.newPassword.value = '';
        if (elements.confirmPassword) {
            elements.confirmPassword.value = '';
            elements.confirmPassword.setCustomValidity('');
        }
        window.bootstrap?.Modal.getOrCreateInstance(elements.resetModal).show();
        window.setTimeout(() => elements.newPassword?.focus(), 180);
    };

    const openToggleActive = (item) => {
        if (!item || !permissions.canUpdate || !item.canToggleActive || !elements.toggleModal) return;
        if (elements.toggleMappingId) elements.toggleMappingId.value = String(item.mappingId);
        if (elements.toggleIdentity) elements.toggleIdentity.textContent = `${displayName(item)} (${displayValue(item.username)})`;
        if (elements.toggleTitle) elements.toggleTitle.textContent = item.isActive ? 'Khóa nhân viên' : 'Mở lại nhân viên';
        if (elements.toggleMessage) {
            elements.toggleMessage.className = `alert mb-0 ${item.isActive ? 'alert-warning' : 'alert-success'}`;
            elements.toggleMessage.textContent = item.isActive
                ? 'Sau khi khóa, nhân viên này không thể sử dụng quyền trong cửa hàng hiện tại.'
                : 'Sau khi mở lại, nhân viên này có thể tiếp tục sử dụng quyền trong cửa hàng hiện tại.';
        }
        if (elements.toggleConfirm) {
            elements.toggleConfirm.className = `btn ${item.isActive ? 'btn-warning' : 'btn-success'}`;
            elements.toggleConfirm.textContent = item.isActive ? 'Xác nhận khóa' : 'Xác nhận mở lại';
        }
        window.bootstrap?.Modal.getOrCreateInstance(elements.toggleModal).show();
    };

    const applyDesktopFilters = () => {
        state.roleId = normalizeRoleId(elements.role?.value);
        state.lifecycle = normalizeChoice(elements.lifecycle?.value || 'all', allowedLifecycles, 'all');
        const pageSize = Number.parseInt(elements.pageSize?.value || '10', 10);
        state.pageSize = allowedPageSizes.has(pageSize) ? pageSize : 10;
        state.page = 1;
        syncControlsFromState();
        loadEmployees();
    };

    const resetAll = () => {
        state.keyword = '';
        state.roleId = '';
        state.lifecycle = 'all';
        state.page = 1;
        state.pageSize = 10;
        syncControlsFromState();
        loadEmployees();
    };

    elements.keyword?.addEventListener('input', () => {
        state.keyword = elements.keyword.value.trim();
        state.page = 1;
        updateFilterIndicators();
        window.clearTimeout(employeeIndexSearchTimer);
        employeeIndexSearchTimer = window.setTimeout(loadEmployees, 350);
    });

    elements.clearSearch?.addEventListener('click', () => {
        state.keyword = '';
        state.page = 1;
        if (elements.keyword) {
            elements.keyword.value = '';
            elements.keyword.focus();
        }
        window.clearTimeout(employeeIndexSearchTimer);
        updateFilterIndicators();
        loadEmployees();
    });

    [elements.role, elements.lifecycle, elements.pageSize].forEach((control) => {
        control?.addEventListener('change', applyDesktopFilters);
    });

    elements.reset?.addEventListener('click', resetAll);
    elements.reload?.addEventListener('click', loadEmployees);

    document.querySelectorAll('button[data-employee-kpi]').forEach((button) => {
        button.addEventListener('click', () => {
            const key = button.dataset.employeeKpi;
            if (key === 'total') state.lifecycle = 'all';
            if (key === 'active') state.lifecycle = state.lifecycle === 'active' ? 'all' : 'active';
            if (key === 'inactive') state.lifecycle = state.lifecycle === 'inactive' ? 'all' : 'inactive';
            state.page = 1;
            syncControlsFromState();
            loadEmployees();
        });
    });

    elements.pageNumbers?.addEventListener('click', (event) => {
        const button = event.target.closest('[data-employee-page]');
        if (!button || button.disabled) return;
        state.page = Math.max(1, Number.parseInt(button.dataset.employeePage, 10) || 1);
        loadEmployees();
        elements.results?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });

    elements.mobileSheet?.addEventListener('show.bs.offcanvas', () => {
        if (elements.mobileRole) elements.mobileRole.value = state.roleId;
        if (elements.mobileLifecycle) elements.mobileLifecycle.value = state.lifecycle;
        if (elements.mobilePageSize) elements.mobilePageSize.value = String(state.pageSize);
    });

    elements.mobileClear?.addEventListener('click', () => {
        if (elements.mobileRole) elements.mobileRole.value = '';
        if (elements.mobileLifecycle) elements.mobileLifecycle.value = 'all';
        if (elements.mobilePageSize) elements.mobilePageSize.value = '10';
    });

    elements.mobileApply?.addEventListener('click', () => {
        state.roleId = normalizeRoleId(elements.mobileRole?.value);
        state.lifecycle = normalizeChoice(elements.mobileLifecycle?.value || 'all', allowedLifecycles, 'all');
        const pageSize = Number.parseInt(elements.mobilePageSize?.value || '10', 10);
        state.pageSize = allowedPageSizes.has(pageSize) ? pageSize : 10;
        state.page = 1;
        syncControlsFromState();
        loadEmployees();
        if (elements.mobileSheet && window.bootstrap?.Offcanvas) {
            window.bootstrap.Offcanvas.getOrCreateInstance(elements.mobileSheet).hide();
        }
    });

    root.addEventListener('click', (event) => {
        const quickButton = event.target.closest('[data-employee-quick]');
        if (quickButton) {
            openEmployeeQuickView(employeeItems.get(quickButton.dataset.employeeQuick));
            return;
        }
        const resetButton = event.target.closest('[data-employee-reset]');
        if (resetButton) {
            openResetPassword(employeeItems.get(resetButton.dataset.employeeReset));
            return;
        }
        const toggleButton = event.target.closest('[data-employee-toggle]');
        if (toggleButton) openToggleActive(employeeItems.get(toggleButton.dataset.employeeToggle));
    });

    root.addEventListener('dblclick', (event) => {
        if (event.target.closest('button, a, input, select, form')) return;
        const row = event.target.closest('[data-employee-row]');
        if (row) openEmployeeQuickView(employeeItems.get(row.dataset.employeeRow));
    });

    elements.confirmPassword?.addEventListener('input', () => {
        const matches = elements.confirmPassword.value === (elements.newPassword?.value || '');
        elements.confirmPassword.setCustomValidity(matches ? '' : 'Mật khẩu nhập lại chưa khớp.');
    });

    elements.newPassword?.addEventListener('input', () => {
        if (!elements.confirmPassword?.value) return;
        const matches = elements.confirmPassword.value === elements.newPassword.value;
        elements.confirmPassword.setCustomValidity(matches ? '' : 'Mật khẩu nhập lại chưa khớp.');
    });

    window.addEventListener('popstate', () => {
        const query = new URLSearchParams(window.location.search);
        const pageSize = Number.parseInt(query.get('pageSize') || '10', 10);
        state.keyword = (query.get('keyword') || '').trim();
        state.roleId = normalizeRoleId(query.get('roleId'));
        state.lifecycle = normalizeChoice(query.get('lifecycle') || 'all', allowedLifecycles, 'all');
        state.page = Math.max(1, Number.parseInt(query.get('page') || '1', 10) || 1);
        state.pageSize = allowedPageSizes.has(pageSize) ? pageSize : 10;
        syncControlsFromState();
        loadEmployees();
    });

    syncControlsFromState();
    loadEmployees();
})();
