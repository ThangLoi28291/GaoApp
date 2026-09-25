(function () {
    'use strict';

    const root = document.querySelector('[data-store-bank-account-index]');
    if (!root) return;

    const allowedQrModes = new Set(['all', 'local-emv-qr', 'vietqr-quick-link', 'provider-api']);
    const allowedConfirmModes = new Set(['all', 'manual', 'callback', 'polling']);
    const allowedLifecycles = new Set(['all', 'active', 'inactive']);
    const allowedDefaultRoles = new Set(['all', 'default', 'not-default']);
    const allowedPageSizes = new Set([10, 20, 50]);
    const query = new URLSearchParams(window.location.search);

    const normalizeChoice = (value, allowed, fallback) => allowed.has(value) ? value : fallback;
    const initialPageSize = Number.parseInt(query.get('pageSize') || '20', 10);
    const state = {
        keyword: (query.get('keyword') || '').trim(),
        qrMode: normalizeChoice(query.get('qrMode') || 'all', allowedQrModes, 'all'),
        confirmMode: normalizeChoice(query.get('confirmMode') || 'all', allowedConfirmModes, 'all'),
        lifecycle: normalizeChoice(query.get('lifecycle') || 'all', allowedLifecycles, 'all'),
        defaultRole: normalizeChoice(query.get('defaultRole') || 'all', allowedDefaultRoles, 'all'),
        page: Math.max(1, Number.parseInt(query.get('page') || '1', 10) || 1),
        pageSize: allowedPageSizes.has(initialPageSize) ? initialPageSize : 20
    };

    const elements = {
        keyword: document.getElementById('storeBankKeyword'),
        clearSearch: document.getElementById('storeBankClearSearch'),
        qrMode: document.getElementById('storeBankQrMode'),
        confirmMode: document.getElementById('storeBankConfirmMode'),
        lifecycle: document.getElementById('storeBankLifecycle'),
        defaultRole: document.getElementById('storeBankDefaultRole'),
        pageSize: document.getElementById('storeBankPageSize'),
        reset: document.getElementById('storeBankResetFilters'),
        reload: document.getElementById('storeBankReload'),
        results: document.getElementById('storeBankResultsPanel'),
        tableBody: document.getElementById('storeBankTableBody'),
        mobileList: document.getElementById('storeBankMobileList'),
        pagingInfo: document.getElementById('storeBankPagingInfo'),
        pageNumbers: document.getElementById('storeBankPageNumbers'),
        statTotal: document.getElementById('storeBankStatTotal'),
        statActive: document.getElementById('storeBankStatActive'),
        statInactive: document.getElementById('storeBankStatInactive'),
        statDefault: document.getElementById('storeBankStatDefault'),
        healthWarning: document.getElementById('storeBankHealthWarning'),
        healthMessage: document.getElementById('storeBankHealthMessage'),
        activeFilterCount: document.getElementById('storeBankActiveFilterCount'),
        mobileSheet: document.getElementById('storeBankMobileFilterSheet'),
        mobileQrMode: document.getElementById('storeBankMobileQrMode'),
        mobileConfirmMode: document.getElementById('storeBankMobileConfirmMode'),
        mobileLifecycle: document.getElementById('storeBankMobileLifecycle'),
        mobileDefaultRole: document.getElementById('storeBankMobileDefaultRole'),
        mobilePageSize: document.getElementById('storeBankMobilePageSize'),
        mobileClear: document.getElementById('storeBankMobileClearFilters'),
        mobileApply: document.getElementById('storeBankMobileApplyFilters'),
        quickModal: document.getElementById('storeBankQuickView'),
        quickAvatar: document.getElementById('storeBankQuickAvatar'),
        quickBankName: document.getElementById('storeBankQuickBankName'),
        quickBankCode: document.getElementById('storeBankQuickBankCode'),
        quickBadges: document.getElementById('storeBankQuickBadges'),
        quickAccountNumber: document.getElementById('storeBankQuickAccountNumber'),
        quickAccountName: document.getElementById('storeBankQuickAccountName'),
        quickQrMode: document.getElementById('storeBankQuickQrMode'),
        quickConfirmMode: document.getElementById('storeBankQuickConfirmMode'),
        quickProvider: document.getElementById('storeBankQuickProvider'),
        quickBin: document.getElementById('storeBankQuickBin'),
        quickTemplate: document.getElementById('storeBankQuickTemplate'),
        quickReadiness: document.getElementById('storeBankQuickReadiness'),
        quickCopy: document.getElementById('storeBankQuickCopy'),
        quickEdit: document.getElementById('storeBankQuickEdit'),
        confirmation: document.getElementById('storeBankConfirmation'),
        confirmationTitle: document.getElementById('storeBankConfirmationTitle'),
        confirmationIdentity: document.getElementById('storeBankConfirmationIdentity'),
        confirmationMessage: document.getElementById('storeBankConfirmationMessage'),
        confirmationSubmit: document.getElementById('storeBankConfirmationSubmit')
    };

    const accountItems = new Map();
    let selectedQuickItem = null;
    let pendingMutation = null;
    let storeBankAccountSearchTimer = null;
    let storeBankAccountIndexRequestSequence = 0;
    let activeRequest = null;

    const escapeHtml = (value) => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');

    const displayValue = (value) => {
        const text = String(value ?? '').trim();
        return text || '—';
    };

    const enumKey = (value) => String(value ?? '').trim().toLowerCase();
    const qrLabel = (value) => ({
        '0': 'VietQR tạo tại hệ thống',
        'localemvqr': 'VietQR tạo tại hệ thống',
        '1': 'Liên kết ảnh VietQR',
        'vietqrquicklink': 'Liên kết ảnh VietQR',
        '2': 'API nhà cung cấp',
        'providerapi': 'API nhà cung cấp'
    }[enumKey(value)] || 'Chưa xác định');

    const confirmLabel = (value) => ({
        '0': 'Nhân viên xác nhận',
        'manual': 'Nhân viên xác nhận',
        '1': 'Nhà cung cấp gửi xác nhận',
        'callback': 'Nhà cung cấp gửi xác nhận',
        '2': 'Hệ thống tự kiểm tra',
        'polling': 'Hệ thống tự kiểm tra'
    }[enumKey(value)] || 'Chưa xác định');

    const isLocalQr = (value) => ['0', 'localemvqr'].includes(enumKey(value));
    const isManualConfirm = (value) => ['0', 'manual'].includes(enumKey(value));

    const initials = (item) => {
        const source = displayValue(item.bankCode) !== '—'
            ? displayValue(item.bankCode)
            : displayValue(item.bankName);
        const pieces = source.split(/\s+/).filter(Boolean);
        return (pieces.length > 1
            ? `${pieces[0][0]}${pieces[1][0]}`
            : source.slice(0, 2)).toUpperCase();
    };

    const editHref = (item) => {
        const separator = root.dataset.editUrl.includes('?') ? '&' : '?';
        return `${root.dataset.editUrl}${separator}id=${encodeURIComponent(item.bankAccountId)}`;
    };

    const renderStatus = (item, interactive) => {
        const className = item.isActive ? 'is-active' : 'is-inactive';
        const label = item.isActive ? 'Đang hoạt động' : 'Ngưng hoạt động';
        const icon = item.isActive ? 'bx-check-circle' : 'bx-pause-circle';
        if (!interactive) {
            return `<span class="store-bank-account-status ${className}"><i class="bx ${icon}"></i>${label}</span>`;
        }
        return `<button type="button" class="store-bank-account-status store-bank-status-action ${className}" data-bank-status="${item.bankAccountId}" title="Đổi trạng thái"><i class="bx ${icon}"></i>${label}</button>`;
    };

    const renderDefault = (item, interactive) => {
        if (item.isDefault) {
            return '<span class="store-bank-account-default is-default"><i class="bx bxs-star"></i>Mặc định POS</span>';
        }
        if (!interactive) {
            return '<span class="store-bank-account-default"><i class="bx bx-star"></i>Không mặc định</span>';
        }
        if (!item.isActive) {
            return '<span class="store-bank-account-default is-locked" title="Chỉ tài khoản đang hoạt động mới có thể đặt mặc định"><i class="bx bx-lock-alt"></i>Không thể chọn</span>';
        }
        return `<button type="button" class="store-bank-account-default store-bank-default-action" data-bank-default="${item.bankAccountId}"><i class="bx bx-star"></i>Đặt mặc định</button>`;
    };

    const readiness = (item) => {
        if (!item.isActive) return { ready: false, text: 'Tài khoản đang ngưng hoạt động.' };
        if (!item.isDefault) return { ready: false, text: 'Chưa được chọn làm tài khoản mặc định tại POS.' };
        if (!isLocalQr(item.qrRenderMode)) return { ready: false, text: 'Luồng POS hiện tại yêu cầu VietQR tạo tại hệ thống.' };
        if (!isManualConfirm(item.confirmMode)) return { ready: false, text: 'Luồng POS hiện tại yêu cầu nhân viên xác nhận.' };
        if (!String(item.vietQrBankBin || '').trim()) return { ready: false, text: 'Chưa có BIN VietQR để tạo mã thanh toán.' };
        return { ready: true, text: 'Sẵn sàng cho luồng VietQR hiện tại tại POS.' };
    };

    const renderStoreBankDesktopRows = (items) => {
        if (!items.length) {
            elements.tableBody.innerHTML = '<tr><td colspan="7" class="gds-empty"><i class="bx bx-search-alt"></i><strong>Không có tài khoản phù hợp</strong><span>Thử đổi từ khóa hoặc bộ lọc.</span></td></tr>';
            return;
        }

        elements.tableBody.innerHTML = items.map((item) => `
            <tr data-bank-row="${item.bankAccountId}">
                <td><div class="store-bank-account-identity"><span class="store-bank-account-avatar">${escapeHtml(initials(item))}</span><div><div class="store-bank-account-bank-name">${escapeHtml(displayValue(item.bankName))}</div><div class="store-bank-account-code">${escapeHtml(displayValue(item.bankCode))}${item.vietQrBankBin ? ` · BIN ${escapeHtml(item.vietQrBankBin)}` : ''}</div></div></div></td>
                <td><div class="store-bank-account-number">${escapeHtml(displayValue(item.accountNumber))}</div><div class="store-bank-account-holder">${escapeHtml(displayValue(item.accountName))}</div></td>
                <td><span class="store-bank-account-method is-qr"><i class="bx bx-scan"></i>${escapeHtml(qrLabel(item.qrRenderMode))}</span><div class="store-bank-account-provider">${escapeHtml(displayValue(item.providerCode))}</div></td>
                <td><span class="store-bank-account-method"><i class="bx bx-check-shield"></i>${escapeHtml(confirmLabel(item.confirmMode))}</span></td>
                <td>${renderStatus(item, true)}</td>
                <td>${renderDefault(item, true)}</td>
                <td><div class="store-bank-account-actions"><button type="button" class="btn btn-sm store-bank-account-action" data-bank-quick="${item.bankAccountId}" title="Xem nhanh"><i class="bx bx-show"></i></button><button type="button" class="btn btn-sm store-bank-account-action" data-bank-copy="${item.bankAccountId}" title="Sao chép số tài khoản"><i class="bx bx-copy"></i></button><a class="btn btn-sm store-bank-account-action btn-label-primary" href="${escapeHtml(editHref(item))}" title="Sửa"><i class="bx bx-edit-alt"></i></a></div></td>
            </tr>`).join('');
    };

    const renderStoreBankMobileCards = (items) => {
        if (!items.length) {
            elements.mobileList.innerHTML = '<div class="gds-empty"><i class="bx bx-search-alt"></i><strong>Không có tài khoản phù hợp</strong><span>Thử đổi từ khóa hoặc bộ lọc.</span></div>';
            return;
        }

        elements.mobileList.innerHTML = items.map((item) => `
            <article class="store-bank-account-mobile-card" data-bank-row="${item.bankAccountId}">
                <div class="store-bank-account-mobile-card__header"><div class="store-bank-account-identity"><span class="store-bank-account-avatar">${escapeHtml(initials(item))}</span><div><div class="store-bank-account-bank-name">${escapeHtml(displayValue(item.bankName))}</div><div class="store-bank-account-code">${escapeHtml(displayValue(item.bankCode))}</div></div></div><div class="d-flex flex-column align-items-end gap-2">${item.isDefault ? renderDefault(item, false) : ''}${renderStatus(item, false)}</div></div>
                <div class="store-bank-account-mobile-card__account"><span>Số tài khoản</span><strong>${escapeHtml(displayValue(item.accountNumber))}</strong><small>${escapeHtml(displayValue(item.accountName))}</small></div>
                <div class="store-bank-account-mobile-card__details"><div><span>Tạo QR</span><strong>${escapeHtml(qrLabel(item.qrRenderMode))}</strong></div><div><span>Xác nhận</span><strong>${escapeHtml(confirmLabel(item.confirmMode))}</strong></div><div><span>Nhà cung cấp</span><strong>${escapeHtml(displayValue(item.providerCode))}</strong></div><div><span>BIN VietQR</span><strong>${escapeHtml(displayValue(item.vietQrBankBin))}</strong></div></div>
                <div class="store-bank-account-mobile-card__actions"><button type="button" class="btn btn-label-secondary store-bank-account-action" data-bank-quick="${item.bankAccountId}"><i class="bx bx-show"></i><span>Xem</span></button><button type="button" class="btn btn-label-secondary store-bank-account-action" data-bank-copy="${item.bankAccountId}"><i class="bx bx-copy"></i><span>Chép STK</span></button><a class="btn btn-label-primary store-bank-account-action" href="${escapeHtml(editHref(item))}"><i class="bx bx-edit-alt"></i><span>Sửa</span></a><button type="button" class="btn btn-label-secondary store-bank-account-action store-bank-status-action" data-bank-status="${item.bankAccountId}"><i class="bx ${item.isActive ? 'bx-pause-circle' : 'bx-play-circle'}"></i><span>${item.isActive ? 'Ngưng' : 'Bật'}</span></button>${!item.isDefault && item.isActive ? `<button type="button" class="btn btn-label-warning store-bank-account-action store-bank-default-action" data-bank-default="${item.bankAccountId}"><i class="bx bx-star"></i><span>Mặc định</span></button>` : ''}</div>
            </article>`).join('');
    };

    const pageSequence = (page, totalPages) => {
        const values = new Set([1, totalPages, page - 1, page, page + 1]);
        return [...values]
            .filter((value) => value >= 1 && value <= totalPages)
            .sort((a, b) => a - b);
    };

    const renderStoreBankCircularPagination = (page, totalPages) => {
        if (totalPages <= 1) {
            elements.pageNumbers.innerHTML = '';
            return;
        }

        const pages = pageSequence(page, totalPages);
        let previous = 0;
        const numbered = pages.map((number) => {
            const gap = previous && number - previous > 1
                ? '<li class="page-item disabled"><span class="page-link">…</span></li>'
                : '';
            previous = number;
            return `${gap}<li class="page-item ${number === page ? 'active' : ''}"><button type="button" class="page-link" data-store-bank-page="${number}" ${number === page ? 'aria-current="page"' : ''}>${number}</button></li>`;
        }).join('');

        elements.pageNumbers.innerHTML = `
            <li class="page-item ${page <= 1 ? 'disabled' : ''}"><button type="button" class="page-link" data-store-bank-page="${page - 1}" aria-label="Trang trước"><i class="bx bx-chevron-left"></i></button></li>
            ${numbered}
            <li class="page-item ${page >= totalPages ? 'disabled' : ''}"><button type="button" class="page-link" data-store-bank-page="${page + 1}" aria-label="Trang sau"><i class="bx bx-chevron-right"></i></button></li>`;
    };

    const renderSummary = (summary) => {
        elements.statTotal.textContent = summary.totalAccounts ?? 0;
        elements.statActive.textContent = summary.activeAccounts ?? 0;
        elements.statInactive.textContent = summary.inactiveAccounts ?? 0;
        elements.statDefault.textContent = summary.defaultAccounts ?? 0;

        let warning = '';
        if ((summary.defaultAccounts ?? 0) === 0) {
            warning = 'Chưa có tài khoản mặc định. POS sẽ không thể tự chọn tài khoản nhận chuyển khoản.';
        } else if ((summary.defaultAccounts ?? 0) > 1) {
            warning = `Đang có ${summary.defaultAccounts} tài khoản được đánh dấu mặc định. Hãy kiểm tra lại cấu hình.`;
        } else if ((summary.inactiveDefaultAccounts ?? 0) > 0) {
            warning = 'Tài khoản mặc định đang ngưng hoạt động. POS sẽ không tìm thấy tài khoản mặc định đang hoạt động.';
        }
        elements.healthMessage.textContent = warning;
        elements.healthWarning.classList.toggle('d-none', !warning);
    };

    const updateUrl = () => {
        const params = new URLSearchParams();
        if (state.keyword) params.set('keyword', state.keyword);
        if (state.qrMode !== 'all') params.set('qrMode', state.qrMode);
        if (state.confirmMode !== 'all') params.set('confirmMode', state.confirmMode);
        if (state.lifecycle !== 'all') params.set('lifecycle', state.lifecycle);
        if (state.defaultRole !== 'all') params.set('defaultRole', state.defaultRole);
        if (state.page !== 1) params.set('page', String(state.page));
        if (state.pageSize !== 20) params.set('pageSize', String(state.pageSize));
        const suffix = params.toString();
        window.history.replaceState(null, '', `${window.location.pathname}${suffix ? `?${suffix}` : ''}`);
    };

    const updateFilterIndicators = () => {
        const count = [state.qrMode, state.confirmMode, state.lifecycle, state.defaultRole]
            .filter((value) => value !== 'all').length + (state.pageSize !== 20 ? 1 : 0);
        elements.activeFilterCount.textContent = String(count);
        elements.activeFilterCount.classList.toggle('d-none', count === 0);
        elements.reset.classList.toggle('d-none', count === 0 && !state.keyword);
        elements.clearSearch.classList.toggle('d-none', !state.keyword);
        document.querySelectorAll('button[data-bank-kpi]').forEach((button) => {
            const key = button.dataset.bankKpi;
            const active = (key === 'active' && state.lifecycle === 'active')
                || (key === 'inactive' && state.lifecycle === 'inactive')
                || (key === 'default' && state.defaultRole === 'default')
                || (key === 'total' && state.lifecycle === 'all' && state.defaultRole === 'all');
            button.classList.toggle('is-active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
    };

    const syncControlsFromState = () => {
        elements.keyword.value = state.keyword;
        elements.qrMode.value = state.qrMode;
        elements.confirmMode.value = state.confirmMode;
        elements.lifecycle.value = state.lifecycle;
        elements.defaultRole.value = state.defaultRole;
        elements.pageSize.value = String(state.pageSize);
        updateFilterIndicators();
    };

    const loadStoreBankAccounts = async () => {
        activeRequest?.abort();
        activeRequest = new AbortController();
        const requestSequence = ++storeBankAccountIndexRequestSequence;
        const params = new URLSearchParams({
            keyword: state.keyword,
            qrMode: state.qrMode,
            confirmMode: state.confirmMode,
            lifecycle: state.lifecycle,
            defaultRole: state.defaultRole,
            page: String(state.page),
            pageSize: String(state.pageSize)
        });

        elements.results.setAttribute('aria-busy', 'true');
        elements.results.classList.add('store-bank-account-loading');

        try {
            const response = await fetch(`${root.dataset.dataUrl}?${params}`, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                signal: activeRequest.signal
            });
            if (!response.ok) throw new Error('Không tải được danh sách tài khoản ngân hàng.');
            const data = await response.json();
            if (requestSequence !== storeBankAccountIndexRequestSequence) return;

            state.page = Math.max(1, data.page || 1);
            state.pageSize = data.pageSize || state.pageSize;
            accountItems.clear();
            (data.items || []).forEach((item) => accountItems.set(String(item.bankAccountId), item));
            renderSummary(data.summary || {});
            renderStoreBankDesktopRows(data.items || []);
            renderStoreBankMobileCards(data.items || []);
            renderStoreBankCircularPagination(state.page, data.totalPages || 1);

            const total = data.totalItems || 0;
            const start = total ? ((state.page - 1) * state.pageSize) + 1 : 0;
            const end = total ? Math.min(total, state.page * state.pageSize) : 0;
            elements.pagingInfo.textContent = total ? `${start}–${end} trên ${total} tài khoản` : '0 tài khoản';
            updateUrl();
            updateFilterIndicators();
        } catch (error) {
            if (error.name === 'AbortError') return;
            const message = escapeHtml(error.message || 'Không tải được dữ liệu.');
            elements.tableBody.innerHTML = `<tr><td colspan="7" class="gds-empty text-danger"><i class="bx bx-error-circle"></i><strong>${message}</strong><span>Hãy thử tải lại.</span></td></tr>`;
            elements.mobileList.innerHTML = `<div class="gds-empty text-danger"><i class="bx bx-error-circle"></i><strong>${message}</strong><span>Hãy thử tải lại.</span></div>`;
        } finally {
            if (requestSequence === storeBankAccountIndexRequestSequence) {
                elements.results.setAttribute('aria-busy', 'false');
                elements.results.classList.remove('store-bank-account-loading');
            }
        }
    };

    const toast = (success, message) => {
        if (window.toastr) {
            window.toastr[success ? 'success' : 'error'](message);
            return;
        }
        window.alert(message);
    };

    const copyAccountNumber = async (item) => {
        if (!item) return;
        try {
            await navigator.clipboard.writeText(item.accountNumber || '');
            toast(true, 'Đã sao chép số tài khoản.');
        } catch {
            toast(false, 'Không thể sao chép tự động. Vui lòng sao chép thủ công.');
        }
    };

    const openStoreBankQuickView = (item) => {
        if (!item) return;
        selectedQuickItem = item;
        const statusBadge = renderStatus(item, false);
        const defaultBadge = renderDefault(item, false);
        const ready = readiness(item);
        elements.quickAvatar.textContent = initials(item);
        elements.quickBankName.textContent = displayValue(item.bankName);
        elements.quickBankCode.textContent = displayValue(item.bankCode);
        elements.quickBadges.innerHTML = `${statusBadge}${defaultBadge}`;
        elements.quickAccountNumber.textContent = displayValue(item.accountNumber);
        elements.quickAccountName.textContent = displayValue(item.accountName);
        elements.quickQrMode.textContent = qrLabel(item.qrRenderMode);
        elements.quickConfirmMode.textContent = confirmLabel(item.confirmMode);
        elements.quickProvider.textContent = displayValue(item.providerCode);
        elements.quickBin.textContent = displayValue(item.vietQrBankBin);
        elements.quickTemplate.textContent = displayValue(item.noteTemplate);
        elements.quickReadiness.className = `store-bank-account-readiness is-wide ${ready.ready ? 'is-ready' : 'is-warning'}`;
        elements.quickReadiness.innerHTML = `<i class="bx ${ready.ready ? 'bx-check-shield' : 'bx-info-circle'}"></i><div><strong>${ready.ready ? 'Sẵn sàng cho POS' : 'Cần kiểm tra cấu hình'}</strong><span>${escapeHtml(ready.text)}</span></div>`;
        elements.quickEdit.href = editHref(item);
        window.bootstrap?.Modal.getOrCreateInstance(elements.quickModal).show();
    };

    const showStoreBankConfirmation = (type, item) => {
        if (!item) return;
        if (type === 'default' && !item.isActive) {
            toast(false, 'Không thể đặt mặc định tài khoản đang ngưng hoạt động.');
            return;
        }

        pendingMutation = { type, item };
        elements.confirmationIdentity.textContent = `${displayValue(item.bankName)} · ${displayValue(item.accountNumber)}`;
        if (type === 'status') {
            const deactivating = item.isActive;
            elements.confirmationTitle.textContent = deactivating ? 'Ngưng tài khoản ngân hàng' : 'Bật tài khoản ngân hàng';
            elements.confirmationMessage.className = `alert mb-0 ${deactivating ? 'alert-warning' : 'alert-success'}`;
            elements.confirmationMessage.textContent = deactivating && item.isDefault
                ? 'Đây là tài khoản mặc định. Sau khi ngưng, POS sẽ không tìm thấy tài khoản mặc định đang hoạt động.'
                : deactivating
                    ? 'Tài khoản sẽ không còn được sử dụng cho các lựa chọn đang hoạt động.'
                    : 'Tài khoản sẽ được đưa trở lại trạng thái hoạt động.';
            elements.confirmationSubmit.className = `btn ${deactivating ? 'btn-warning' : 'btn-success'}`;
            elements.confirmationSubmit.textContent = deactivating ? 'Xác nhận ngưng' : 'Xác nhận bật';
        } else {
            elements.confirmationTitle.textContent = 'Đặt làm mặc định tại POS';
            elements.confirmationMessage.className = 'alert alert-info mb-0';
            elements.confirmationMessage.textContent = 'Tài khoản này sẽ thay thế tài khoản mặc định hiện tại. Các quy tắc phía máy chủ vẫn quyết định kết quả cuối cùng.';
            elements.confirmationSubmit.className = 'btn btn-primary';
            elements.confirmationSubmit.textContent = 'Đặt làm mặc định';
        }
        window.bootstrap?.Modal.getOrCreateInstance(elements.confirmation).show();
    };

    const executeMutation = async () => {
        if (!pendingMutation) return;
        const mutation = pendingMutation;
        const mutationName = mutation.type === 'status' ? 'ToggleStatus' : 'SetDefault';
        const targetUrl = mutation.type === 'status' ? root.dataset.toggleUrl : root.dataset.defaultUrl;
        const token = document.querySelector('#storeBankAntiforgery input[name="__RequestVerificationToken"]')?.value || '';
        elements.confirmationSubmit.disabled = true;

        try {
            const response = await fetch(targetUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: `id=${encodeURIComponent(mutation.item.bankAccountId)}&__RequestVerificationToken=${encodeURIComponent(token)}`
            });
            const data = await response.json();
            if (!response.ok || !data.success) throw new Error(data.message || `Không thực hiện được ${mutationName}.`);
            toast(true, data.message || 'Đã cập nhật tài khoản ngân hàng.');
            window.bootstrap?.Modal.getOrCreateInstance(elements.confirmation).hide();
            pendingMutation = null;
            await loadStoreBankAccounts();
        } catch (error) {
            toast(false, error.message || 'Không thể cập nhật tài khoản ngân hàng.');
        } finally {
            elements.confirmationSubmit.disabled = false;
        }
    };

    const applyDesktopFilters = () => {
        state.qrMode = normalizeChoice(elements.qrMode.value, allowedQrModes, 'all');
        state.confirmMode = normalizeChoice(elements.confirmMode.value, allowedConfirmModes, 'all');
        state.lifecycle = normalizeChoice(elements.lifecycle.value, allowedLifecycles, 'all');
        state.defaultRole = normalizeChoice(elements.defaultRole.value, allowedDefaultRoles, 'all');
        const pageSize = Number.parseInt(elements.pageSize.value || '20', 10);
        state.pageSize = allowedPageSizes.has(pageSize) ? pageSize : 20;
        state.page = 1;
        syncControlsFromState();
        loadStoreBankAccounts();
    };

    const resetAll = () => {
        state.keyword = '';
        state.qrMode = 'all';
        state.confirmMode = 'all';
        state.lifecycle = 'all';
        state.defaultRole = 'all';
        state.page = 1;
        state.pageSize = 20;
        syncControlsFromState();
        loadStoreBankAccounts();
    };

    elements.keyword.addEventListener('input', () => {
        state.keyword = elements.keyword.value.trim();
        state.page = 1;
        updateFilterIndicators();
        window.clearTimeout(storeBankAccountSearchTimer);
        storeBankAccountSearchTimer = window.setTimeout(loadStoreBankAccounts, 350);
    });

    elements.clearSearch.addEventListener('click', () => {
        state.keyword = '';
        state.page = 1;
        elements.keyword.value = '';
        elements.keyword.focus();
        window.clearTimeout(storeBankAccountSearchTimer);
        updateFilterIndicators();
        loadStoreBankAccounts();
    });

    [elements.qrMode, elements.confirmMode, elements.lifecycle, elements.defaultRole, elements.pageSize]
        .forEach((control) => control.addEventListener('change', applyDesktopFilters));
    elements.reset.addEventListener('click', resetAll);
    elements.reload.addEventListener('click', loadStoreBankAccounts);

    document.querySelectorAll('button[data-bank-kpi]').forEach((button) => {
        button.addEventListener('click', () => {
            const key = button.dataset.bankKpi;
            if (key === 'total') {
                state.lifecycle = 'all';
                state.defaultRole = 'all';
            }
            if (key === 'active') state.lifecycle = state.lifecycle === 'active' ? 'all' : 'active';
            if (key === 'inactive') state.lifecycle = state.lifecycle === 'inactive' ? 'all' : 'inactive';
            if (key === 'default') state.defaultRole = state.defaultRole === 'default' ? 'all' : 'default';
            state.page = 1;
            syncControlsFromState();
            loadStoreBankAccounts();
        });
    });

    elements.pageNumbers.addEventListener('click', (event) => {
        const button = event.target.closest('[data-store-bank-page]');
        if (!button || button.disabled || button.closest('.disabled')) return;
        state.page = Math.max(1, Number.parseInt(button.dataset.storeBankPage, 10) || 1);
        loadStoreBankAccounts();
        elements.results.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });

    elements.mobileSheet.addEventListener('show.bs.offcanvas', () => {
        elements.mobileQrMode.value = state.qrMode;
        elements.mobileConfirmMode.value = state.confirmMode;
        elements.mobileLifecycle.value = state.lifecycle;
        elements.mobileDefaultRole.value = state.defaultRole;
        elements.mobilePageSize.value = String(state.pageSize);
    });

    elements.mobileClear.addEventListener('click', () => {
        elements.mobileQrMode.value = 'all';
        elements.mobileConfirmMode.value = 'all';
        elements.mobileLifecycle.value = 'all';
        elements.mobileDefaultRole.value = 'all';
        elements.mobilePageSize.value = '20';
    });

    elements.mobileApply.addEventListener('click', () => {
        state.qrMode = normalizeChoice(elements.mobileQrMode.value, allowedQrModes, 'all');
        state.confirmMode = normalizeChoice(elements.mobileConfirmMode.value, allowedConfirmModes, 'all');
        state.lifecycle = normalizeChoice(elements.mobileLifecycle.value, allowedLifecycles, 'all');
        state.defaultRole = normalizeChoice(elements.mobileDefaultRole.value, allowedDefaultRoles, 'all');
        const pageSize = Number.parseInt(elements.mobilePageSize.value || '20', 10);
        state.pageSize = allowedPageSizes.has(pageSize) ? pageSize : 20;
        state.page = 1;
        syncControlsFromState();
        loadStoreBankAccounts();
        window.bootstrap?.Offcanvas.getOrCreateInstance(elements.mobileSheet).hide();
    });

    root.addEventListener('click', (event) => {
        const quickButton = event.target.closest('[data-bank-quick]');
        if (quickButton) {
            openStoreBankQuickView(accountItems.get(quickButton.dataset.bankQuick));
            return;
        }
        const copyButton = event.target.closest('[data-bank-copy]');
        if (copyButton) {
            copyAccountNumber(accountItems.get(copyButton.dataset.bankCopy));
            return;
        }
        const statusButton = event.target.closest('[data-bank-status]');
        if (statusButton) {
            showStoreBankConfirmation('status', accountItems.get(statusButton.dataset.bankStatus));
            return;
        }
        const defaultButton = event.target.closest('[data-bank-default]');
        if (defaultButton) showStoreBankConfirmation('default', accountItems.get(defaultButton.dataset.bankDefault));
    });

    root.addEventListener('dblclick', (event) => {
        if (event.target.closest('button, a, input, select, form')) return;
        const row = event.target.closest('[data-bank-row]');
        if (row) openStoreBankQuickView(accountItems.get(row.dataset.bankRow));
    });

    elements.quickCopy.addEventListener('click', () => copyAccountNumber(selectedQuickItem));
    elements.confirmationSubmit.addEventListener('click', executeMutation);
    elements.confirmation.addEventListener('hidden.bs.modal', () => { pendingMutation = null; });

    window.addEventListener('popstate', () => {
        const current = new URLSearchParams(window.location.search);
        const pageSize = Number.parseInt(current.get('pageSize') || '20', 10);
        state.keyword = (current.get('keyword') || '').trim();
        state.qrMode = normalizeChoice(current.get('qrMode') || 'all', allowedQrModes, 'all');
        state.confirmMode = normalizeChoice(current.get('confirmMode') || 'all', allowedConfirmModes, 'all');
        state.lifecycle = normalizeChoice(current.get('lifecycle') || 'all', allowedLifecycles, 'all');
        state.defaultRole = normalizeChoice(current.get('defaultRole') || 'all', allowedDefaultRoles, 'all');
        state.page = Math.max(1, Number.parseInt(current.get('page') || '1', 10) || 1);
        state.pageSize = allowedPageSizes.has(pageSize) ? pageSize : 20;
        syncControlsFromState();
        loadStoreBankAccounts();
    });

    syncControlsFromState();
    loadStoreBankAccounts();
})();
