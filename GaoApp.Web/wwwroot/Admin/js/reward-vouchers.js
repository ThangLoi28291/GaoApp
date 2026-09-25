(function () {
    'use strict';

    const root = document.querySelector('[data-reward-voucher-index]');
    if (!root) return;

    const dataUrl = root.dataset.dataUrl || '/admin/reward-vouchers/data';
    const quickViewBaseUrl = root.dataset.quickViewBaseUrl || '/admin/reward-vouchers';
    const legacyApiBaseUrl = root.dataset.legacyApiBaseUrl || '/admin/api/customers/reward-vouchers';

    let rewardVoucherIndexRequestSequence = 0;
    let rewardVoucherIndexAbortController = null;
    let rewardVoucherSearchTimer = null;
    let selectedVoucherId = 0;
    let selectedVoucherCode = '';
    let selectedVoucherStatus = '';
    let pendingVoucherAction = null;
    let currentItems = [];

    const state = {
        keyword: '',
        status: 'all',
        fromDate: '',
        toDate: '',
        page: 1,
        pageSize: 20,
        totalPages: 1,
        totalItems: 0
    };

    const quickViewElement = document.getElementById('rewardVoucherQuickView');
    const historyElement = document.getElementById('rewardVoucherHistorySheet');
    const confirmationElement = document.getElementById('rewardVoucherConfirmation');
    const mobileFilterElement = document.getElementById('rewardVoucherMobileFilterSheet');

    const quickViewModal = quickViewElement && window.bootstrap
        ? window.bootstrap.Modal.getOrCreateInstance(quickViewElement)
        : null;
    const historyModal = historyElement && window.bootstrap
        ? window.bootstrap.Modal.getOrCreateInstance(historyElement)
        : null;
    const confirmationModal = confirmationElement && window.bootstrap
        ? window.bootstrap.Modal.getOrCreateInstance(confirmationElement)
        : null;
    const mobileFilter = mobileFilterElement && window.bootstrap
        ? window.bootstrap.Offcanvas.getOrCreateInstance(mobileFilterElement)
        : null;

    function valueOf(source, camelName, pascalName) {
        return source?.[camelName] ?? source?.[pascalName];
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function money(value) {
        return `${new Intl.NumberFormat('vi-VN').format(Number(value || 0))} đ`;
    }

    function parseDate(value) {
        if (!value) return null;
        const parsed = new Date(value);
        return Number.isNaN(parsed.getTime()) ? null : parsed;
    }

    function dateOnly(value) {
        const parsed = parseDate(value);
        return parsed ? parsed.toLocaleDateString('vi-VN') : '—';
    }

    function dateTime(value) {
        const parsed = parseDate(value);
        return parsed
            ? parsed.toLocaleString('vi-VN', {
                day: '2-digit', month: '2-digit', year: 'numeric',
                hour: '2-digit', minute: '2-digit'
            })
            : '—';
    }

    function normalizeStatus(value) {
        const normalized = String(value ?? '').trim().toLowerCase();
        const numeric = Number(value);

        if (numeric === 1 || normalized === 'available') return 'Available';
        if (numeric === 2 || normalized === 'used') return 'Used';
        if (numeric === 3 || normalized === 'cancelled') return 'Cancelled';
        if (numeric === 4 || normalized === 'expired') return 'Expired';
        if (numeric === 5 || normalized === 'locked') return 'Locked';
        return '';
    }

    function statusText(value) {
        switch (normalizeStatus(value)) {
            case 'Available': return 'Khả dụng';
            case 'Used': return 'Đã sử dụng';
            case 'Cancelled': return 'Đã hủy';
            case 'Expired': return 'Hết hạn';
            case 'Locked': return 'Đã khóa';
            default: return 'Chưa xác định';
        }
    }

    function statusClass(value) {
        switch (normalizeStatus(value)) {
            case 'Available': return 'is-available';
            case 'Used': return 'is-used';
            case 'Cancelled': return 'is-cancelled';
            case 'Expired': return 'is-expired';
            case 'Locked': return 'is-locked';
            default: return '';
        }
    }

    function statusBadge(value) {
        return `<span class="reward-voucher-status ${statusClass(value)}">${escapeHtml(statusText(value))}</span>`;
    }

    function historyActionText(value) {
        const normalized = String(value ?? '').trim().toLowerCase();
        const labels = {
            issued: 'Phát hành',
            used: 'Đã sử dụng',
            available: 'Khả dụng',
            cancelled: 'Đã hủy',
            expired: 'Hết hạn',
            locked: 'Đã khóa'
        };
        return labels[normalized] || value || 'Cập nhật phiếu';
    }

    function setText(id, value) {
        const element = document.getElementById(id);
        if (element) element.textContent = value ?? '—';
    }

    function setBusy(isBusy) {
        const panel = document.getElementById('rewardVoucherResultsPanel');
        panel?.setAttribute('aria-busy', isBusy ? 'true' : 'false');
        document.getElementById('rewardVoucherReload')?.toggleAttribute('disabled', isBusy);
    }

    function showError(message) {
        setText('rewardVoucherErrorMessage', message || 'Không thể xử lý yêu cầu.');
        document.getElementById('rewardVoucherError')?.classList.remove('d-none');
    }

    function clearError() {
        document.getElementById('rewardVoucherError')?.classList.add('d-none');
    }

    async function fetchJson(url, options) {
        const response = await fetch(url, {
            ...options,
            headers: {
                Accept: 'application/json',
                'Content-Type': 'application/json',
                ...(options?.headers || {})
            }
        });

        if (!response.ok) {
            const body = await response.text();
            throw new Error(body || 'Máy chủ không thể xử lý yêu cầu.');
        }

        return response.status === 204 ? null : response.json();
    }

    function getAntiforgeryToken() {
        return document.querySelector('#rewardVoucherAntiforgery input[name="__RequestVerificationToken"]')?.value || '';
    }

    function getLegacyActionUrl(voucherId, action) {
        if (action === 'cancel') return `${legacyApiBaseUrl}/${voucherId}/cancel`;
        if (action === 'lock') return `${legacyApiBaseUrl}/${voucherId}/lock`;
        if (action === 'unlock') return `${legacyApiBaseUrl}/${voucherId}/unlock`;
        throw new Error('Thao tác phiếu thưởng không hợp lệ.');
    }

    function buildQuery() {
        const query = new URLSearchParams();
        query.set('page', String(state.page));
        query.set('pageSize', String(state.pageSize));
        if (state.keyword) query.set('keyword', state.keyword);
        if (state.status !== 'all') query.set('status', state.status);
        if (state.fromDate) query.set('fromDate', state.fromDate);
        if (state.toDate) query.set('toDate', state.toDate);
        return query.toString();
    }

    function mapItem(item) {
        return {
            voucherId: Number(valueOf(item, 'voucherId', 'VoucherId') || 0),
            voucherCode: valueOf(item, 'voucherCode', 'VoucherCode') || '',
            referenceCode: valueOf(item, 'referenceCode', 'ReferenceCode') || '',
            customerName: valueOf(item, 'customerName', 'CustomerName') || 'Khách hàng',
            customerCode: valueOf(item, 'customerCode', 'CustomerCode') || '',
            customerPhone: valueOf(item, 'customerPhone', 'CustomerPhone') || '',
            customerEmail: valueOf(item, 'customerEmail', 'CustomerEmail') || '',
            value: Number(valueOf(item, 'value', 'Value') || 0),
            requiredAmount: Number(valueOf(item, 'requiredAmount', 'RequiredAmount') || 0),
            status: normalizeStatus(valueOf(item, 'status', 'Status')),
            issuedAtUtc: valueOf(item, 'issuedAtUtc', 'IssuedAtUtc'),
            usedAtUtc: valueOf(item, 'usedAtUtc', 'UsedAtUtc'),
            isLinkedToOrder: Boolean(valueOf(item, 'isLinkedToOrder', 'IsLinkedToOrder'))
        };
    }

    function renderSummary(summary) {
        setText('rewardVoucherStatTotal', Number(valueOf(summary, 'totalVouchers', 'TotalVouchers') || 0).toLocaleString('vi-VN'));
        setText('rewardVoucherStatAvailable', Number(valueOf(summary, 'availableVouchers', 'AvailableVouchers') || 0).toLocaleString('vi-VN'));
        setText('rewardVoucherStatUsed', Number(valueOf(summary, 'usedVouchers', 'UsedVouchers') || 0).toLocaleString('vi-VN'));
        setText('rewardVoucherStatUnavailable', Number(valueOf(summary, 'unavailableVouchers', 'UnavailableVouchers') || 0).toLocaleString('vi-VN'));
    }

    function renderRewardVoucherDesktopRows(items) {
        const tbody = document.getElementById('rewardVoucherTableBody');
        if (!tbody) return;

        if (!items.length) {
            tbody.innerHTML = '<tr><td colspan="6" class="gds-empty">Không có phiếu giảm giá phù hợp.</td></tr>';
            return;
        }

        tbody.innerHTML = items.map(item => `
            <tr data-voucher-row="${item.voucherId}" tabindex="0">
                <td>
                    <div class="reward-voucher-code">${escapeHtml(item.voucherCode)}</div>
                    ${item.referenceCode ? `<span class="reward-voucher-secondary">Tham chiếu: ${escapeHtml(item.referenceCode)}</span>` : ''}
                </td>
                <td>
                    <div class="reward-voucher-customer">${escapeHtml(item.customerName)}</div>
                    <span class="reward-voucher-secondary">${escapeHtml(item.customerPhone || 'Chưa có SĐT')}</span>
                    ${item.customerEmail ? `<span class="reward-voucher-secondary">${escapeHtml(item.customerEmail)}</span>` : ''}
                </td>
                <td>
                    <div class="reward-voucher-value text-primary">${money(item.value)}</div>
                    <span class="reward-voucher-secondary">Đã trừ: ${money(item.requiredAmount)}</span>
                </td>
                <td>
                    <strong class="text-dark">${dateOnly(item.issuedAtUtc)}</strong>
                    <span class="reward-voucher-secondary">Sử dụng: ${dateOnly(item.usedAtUtc)}</span>
                </td>
                <td>${statusBadge(item.status)}</td>
                <td class="text-end">
                    <button type="button" class="reward-voucher-row-action" data-voucher-view="${item.voucherId}" aria-label="Xem nhanh ${escapeHtml(item.voucherCode)}" title="Xem nhanh"><i class="bx bx-show-alt"></i></button>
                </td>
            </tr>`).join('');
    }

    function renderRewardVoucherMobileCards(items) {
        const list = document.getElementById('rewardVoucherMobileList');
        if (!list) return;

        if (!items.length) {
            list.innerHTML = '<div class="gds-empty">Không có phiếu giảm giá phù hợp.</div>';
            return;
        }

        list.innerHTML = items.map(item => `
            <article class="reward-voucher-mobile-card" data-voucher-card="${item.voucherId}">
                <div class="reward-voucher-mobile-card__head">
                    <div class="reward-voucher-code">${escapeHtml(item.voucherCode)}</div>
                    ${statusBadge(item.status)}
                </div>
                <div class="reward-voucher-mobile-card__customer">
                    <div><div class="reward-voucher-customer">${escapeHtml(item.customerName)}</div><span class="reward-voucher-secondary">${escapeHtml(item.customerPhone || item.customerEmail || 'Chưa có liên hệ')}</span></div>
                </div>
                <div class="reward-voucher-mobile-card__meta">
                    <div><span>Giá trị phiếu</span><strong class="text-primary">${money(item.value)}</strong></div>
                    <div><span>Ngày phát hành</span><strong>${dateOnly(item.issuedAtUtc)}</strong></div>
                </div>
                <div class="reward-voucher-mobile-card__actions">
                    <button type="button" class="btn btn-label-primary reward-voucher-action" data-voucher-view="${item.voucherId}"><i class="bx bx-show-alt"></i> Xem nhanh</button>
                </div>
            </article>`).join('');
    }

    function paginationButton(label, page, disabled, active, ariaLabel) {
        return `<li class="page-item${disabled ? ' disabled' : ''}${active ? ' active' : ''}"><button type="button" class="page-link" data-voucher-page="${page}" ${disabled ? 'disabled' : ''} aria-label="${ariaLabel || label}" ${active ? 'aria-current="page"' : ''}>${label}</button></li>`;
    }

    function renderRewardVoucherCircularPagination() {
        const target = document.getElementById('rewardVoucherPageNumbers');
        if (!target) return;

        const totalPages = Math.max(1, state.totalPages);
        const current = Math.min(Math.max(1, state.page), totalPages);
        const pages = [];
        const start = Math.max(1, current - 1);
        const end = Math.min(totalPages, current + 1);

        pages.push(paginationButton('<i class="bx bx-chevron-left"></i>', current - 1, current <= 1, false, 'Trang trước'));
        if (start > 1) {
            pages.push(paginationButton('1', 1, false, current === 1));
            if (start > 2) pages.push('<li class="page-item disabled"><span class="page-link">…</span></li>');
        }
        for (let page = start; page <= end; page += 1) {
            pages.push(paginationButton(String(page), page, false, page === current));
        }
        if (end < totalPages) {
            if (end < totalPages - 1) pages.push('<li class="page-item disabled"><span class="page-link">…</span></li>');
            pages.push(paginationButton(String(totalPages), totalPages, false, current === totalPages));
        }
        pages.push(paginationButton('<i class="bx bx-chevron-right"></i>', current + 1, current >= totalPages, false, 'Trang sau'));
        target.innerHTML = pages.join('');
    }

    function updateFilterPresentation() {
        const count = (state.status !== 'all' ? 1 : 0) + (state.fromDate ? 1 : 0) + (state.toDate ? 1 : 0) + (state.pageSize !== 20 ? 1 : 0);
        const badge = document.getElementById('rewardVoucherActiveFilterCount');
        if (badge) {
            badge.textContent = String(count);
            badge.classList.toggle('d-none', count === 0);
        }
        document.getElementById('rewardVoucherResetFilters')?.classList.toggle('d-none', count === 0 && !state.keyword);
        document.getElementById('rewardVoucherClearSearch')?.classList.toggle('d-none', !state.keyword);
    }

    function renderPage(data) {
        const rawItems = valueOf(data, 'items', 'Items') || [];
        currentItems = rawItems.map(mapItem);
        state.page = Number(valueOf(data, 'page', 'Page') || 1);
        state.pageSize = Number(valueOf(data, 'pageSize', 'PageSize') || state.pageSize);
        state.totalItems = Number(valueOf(data, 'totalItems', 'TotalItems') || 0);
        state.totalPages = Number(valueOf(data, 'totalPages', 'TotalPages') || 1);

        renderSummary(valueOf(data, 'summary', 'Summary') || {});
        renderRewardVoucherDesktopRows(currentItems);
        renderRewardVoucherMobileCards(currentItems);
        renderRewardVoucherCircularPagination();
        setText('rewardVoucherPagingInfo', `${state.totalItems.toLocaleString('vi-VN')} phiếu · Trang ${state.page}/${state.totalPages}`);
        updateFilterPresentation();
    }

    function validateDateRange() {
        if (state.fromDate && state.toDate && state.fromDate > state.toDate) {
            showError('Ngày bắt đầu không được sau ngày kết thúc.');
            return false;
        }
        return true;
    }

    async function loadRewardVoucherIndex() {
        if (!validateDateRange()) return;

        const requestSequence = ++rewardVoucherIndexRequestSequence;
        rewardVoucherIndexAbortController?.abort();
        rewardVoucherIndexAbortController = new AbortController();
        clearError();
        setBusy(true);

        try {
            const data = await fetchJson(`${dataUrl}?${buildQuery()}`, {
                method: 'GET',
                signal: rewardVoucherIndexAbortController.signal
            });
            if (requestSequence !== rewardVoucherIndexRequestSequence) return;
            renderPage(data || {});
        } catch (error) {
            if (error?.name !== 'AbortError' && requestSequence === rewardVoucherIndexRequestSequence) {
                showError(error?.message || 'Không tải được danh sách phiếu giảm giá.');
            }
        } finally {
            if (requestSequence === rewardVoucherIndexRequestSequence) setBusy(false);
        }
    }

    function updateQuickActionButtons(status) {
        selectedVoucherStatus = normalizeStatus(status);
        const cancelButton = document.getElementById('rewardVoucherCancel');
        const lockButton = document.getElementById('rewardVoucherLock');
        const unlockButton = document.getElementById('rewardVoucherUnlock');
        cancelButton?.classList.toggle('d-none', selectedVoucherStatus !== 'Available');
        lockButton?.classList.toggle('d-none', selectedVoucherStatus !== 'Available');
        unlockButton?.classList.toggle('d-none', selectedVoucherStatus !== 'Locked');
    }

    async function openRewardVoucherQuickView(voucherId) {
        selectedVoucherId = Number(voucherId || 0);
        if (!selectedVoucherId) return;

        clearError();
        setText('rewardVoucherQuickCode', 'Đang tải...');
        updateQuickActionButtons('');
        quickViewModal?.show();

        try {
            const raw = await fetchJson(`${quickViewBaseUrl}/${selectedVoucherId}/quick-view`, { method: 'GET' });
            const item = mapItem(raw || {});
            selectedVoucherCode = item.voucherCode;
            selectedVoucherStatus = item.status;

            setText('rewardVoucherQuickCode', item.voucherCode || '—');
            setText('rewardVoucherQuickCustomer', item.customerName || 'Khách hàng');
            setText('rewardVoucherQuickPhone', item.customerPhone || '—');
            setText('rewardVoucherQuickEmail', item.customerEmail || '—');
            setText('rewardVoucherQuickCustomerCode', item.customerCode || '—');
            setText('rewardVoucherQuickValue', money(item.value));
            setText('rewardVoucherQuickRequiredAmount', money(item.requiredAmount));
            setText('rewardVoucherQuickIssuedAt', dateTime(item.issuedAtUtc));
            setText('rewardVoucherQuickUsedAt', dateTime(item.usedAtUtc));
            setText('rewardVoucherQuickReference', item.referenceCode || '—');
            setText('rewardVoucherQuickOrderLink', item.isLinkedToOrder ? 'Đã liên kết với đơn hàng' : 'Chưa liên kết đơn hàng');
            setText('rewardVoucherQuickDescription', valueOf(raw, 'description', 'Description') || '—');
            const badges = document.getElementById('rewardVoucherQuickBadges');
            if (badges) badges.innerHTML = statusBadge(item.status);
            updateQuickActionButtons(item.status);
        } catch (error) {
            quickViewModal?.hide();
            showError(error?.message || 'Không tải được thông tin phiếu.');
        }
    }

    async function openRewardVoucherHistory() {
        if (!selectedVoucherId) return;
        const list = document.getElementById('rewardVoucherHistoryList');
        if (list) list.innerHTML = '<div class="gds-empty">Đang tải lịch sử...</div>';
        quickViewModal?.hide();
        historyModal?.show();

        try {
            const raw = await fetchJson(`${legacyApiBaseUrl}/${selectedVoucherId}/logs`, { method: 'GET' });
            const items = valueOf(raw, 'items', 'Items') || raw || [];
            if (!Array.isArray(items) || !items.length) {
                if (list) list.innerHTML = '<div class="gds-empty">Chưa có lịch sử phiếu.</div>';
                return;
            }
            if (list) {
                list.innerHTML = items.map(item => `
                    <div class="reward-voucher-history-item">
                        <span class="reward-voucher-history-icon"><i class="bx bx-history"></i></span>
                        <div class="reward-voucher-history-copy">
                            <strong>${escapeHtml(historyActionText(valueOf(item, 'action', 'Action')))}</strong>
                            <span>${escapeHtml(valueOf(item, 'note', 'Note') || 'Không có ghi chú')}</span>
                            <span>${escapeHtml(dateTime(valueOf(item, 'createdAtUtc', 'CreatedAtUtc')))}</span>
                        </div>
                    </div>`).join('');
            }
        } catch (error) {
            historyModal?.hide();
            showError(error?.message || 'Không tải được lịch sử phiếu.');
        }
    }

    function showRewardVoucherConfirmation(action) {
        if (!selectedVoucherId || !selectedVoucherCode) return;
        pendingVoucherAction = action;
        setText('rewardVoucherConfirmationTitle', action.title);
        setText('rewardVoucherConfirmationIdentity', selectedVoucherCode);
        setText('rewardVoucherConfirmationMessage', action.message);
        document.getElementById('rewardVoucherConfirmationMessage')?.setAttribute('class', `alert mb-3 ${action.alertClass}`);
        document.getElementById('rewardVoucherCancelWarning')?.classList.toggle('d-none', action.kind !== 'cancel');
        const reason = document.getElementById('rewardVoucherActionReason');
        if (reason) reason.value = '';
        const submit = document.getElementById('rewardVoucherConfirmationSubmit');
        if (submit) {
            submit.className = `btn ${action.buttonClass}`;
            submit.textContent = action.submitText;
        }
        quickViewModal?.hide();
        confirmationModal?.show();
    }

    async function submitRewardVoucherAction() {
        if (!pendingVoucherAction || !selectedVoucherId) return;
        const submit = document.getElementById('rewardVoucherConfirmationSubmit');
        submit?.setAttribute('disabled', 'disabled');
        clearError();

        try {
            await fetchJson(getLegacyActionUrl(selectedVoucherId, pendingVoucherAction.kind), {
                method: 'POST',
                headers: { RequestVerificationToken: getAntiforgeryToken() },
                body: JSON.stringify({ reason: document.getElementById('rewardVoucherActionReason')?.value || '' })
            });
            confirmationModal?.hide();
            pendingVoucherAction = null;
            await loadRewardVoucherIndex();
        } catch (error) {
            confirmationModal?.hide();
            showError(error?.message || 'Không thể cập nhật trạng thái phiếu.');
        } finally {
            submit?.removeAttribute('disabled');
        }
    }

    async function printRewardVoucher() {
        if (!selectedVoucherId) return;
        try {
            const raw = await fetchJson(`${legacyApiBaseUrl}/${selectedVoucherId}/print`, { method: 'GET' });
            const voucherCode = valueOf(raw, 'voucherCode', 'VoucherCode') || '';
            const customerName = valueOf(raw, 'customerName', 'CustomerName') || '';
            const customerPhone = valueOf(raw, 'customerPhone', 'CustomerPhone') || '';
            const value = Number(valueOf(raw, 'value', 'Value') || 0);
            const issuedAt = valueOf(raw, 'issuedAtUtc', 'IssuedAtUtc');
            const status = valueOf(raw, 'status', 'Status');
            const printWindow = window.open('', '_blank', 'width=480,height=720');
            if (!printWindow) {
                showError('Trình duyệt đã chặn cửa sổ in phiếu.');
                return;
            }
            printWindow.document.open();
            printWindow.document.write(`<!doctype html><html lang="vi"><head><meta charset="utf-8"><title>Phiếu giảm giá ${escapeHtml(voucherCode)}</title><style>body{font-family:Arial,sans-serif;padding:28px;color:#2b2c40}.sheet{max-width:380px;margin:auto;border:2px dashed #696cff;border-radius:18px;padding:24px;text-align:center}.code{font-size:22px;font-weight:800;color:#696cff}.value{font-size:34px;font-weight:900;margin:18px 0}.meta{margin-top:8px;color:#697a8d}.status{display:inline-block;margin-top:16px;padding:7px 14px;border-radius:999px;background:#eef0ff;color:#696cff;font-weight:700}@media print{body{padding:0}.sheet{border-color:#444}}</style></head><body><div class="sheet"><h2>PHIẾU GIẢM GIÁ</h2><div class="code">${escapeHtml(voucherCode)}</div><div class="value">${escapeHtml(money(value))}</div><strong>${escapeHtml(customerName)}</strong><div class="meta">${escapeHtml(customerPhone)}</div><div class="meta">Phát hành: ${escapeHtml(dateTime(issuedAt))}</div><div class="status">${escapeHtml(statusText(status))}</div></div><script>window.addEventListener('load',function(){window.print();});<\/script></body></html>`);
            GaoPrintLifecycle.autoClose(printWindow);
            printWindow.document.close();
        } catch (error) {
            showError(error?.message || 'Không thể chuẩn bị bản in phiếu.');
        }
    }

    function readDesktopFilters() {
        state.keyword = document.getElementById('rewardVoucherKeyword')?.value.trim() || '';
        state.status = document.getElementById('rewardVoucherStatus')?.value || 'all';
        state.fromDate = document.getElementById('rewardVoucherFromDate')?.value || '';
        state.toDate = document.getElementById('rewardVoucherToDate')?.value || '';
        state.pageSize = Number(document.getElementById('rewardVoucherPageSize')?.value || 20);
    }

    function syncMobileFromDesktop() {
        document.getElementById('rewardVoucherMobileStatus').value = state.status;
        document.getElementById('rewardVoucherMobileFromDate').value = state.fromDate;
        document.getElementById('rewardVoucherMobileToDate').value = state.toDate;
        document.getElementById('rewardVoucherMobilePageSize').value = String(state.pageSize);
    }

    function applyMobileFilters() {
        state.status = document.getElementById('rewardVoucherMobileStatus')?.value || 'all';
        state.fromDate = document.getElementById('rewardVoucherMobileFromDate')?.value || '';
        state.toDate = document.getElementById('rewardVoucherMobileToDate')?.value || '';
        state.pageSize = Number(document.getElementById('rewardVoucherMobilePageSize')?.value || 20);
        document.getElementById('rewardVoucherStatus').value = state.status;
        document.getElementById('rewardVoucherFromDate').value = state.fromDate;
        document.getElementById('rewardVoucherToDate').value = state.toDate;
        document.getElementById('rewardVoucherPageSize').value = String(state.pageSize);
        state.page = 1;
        mobileFilter?.hide();
        loadRewardVoucherIndex();
    }

    function resetFilters(includeKeyword) {
        if (includeKeyword) {
            state.keyword = '';
            const keyword = document.getElementById('rewardVoucherKeyword');
            if (keyword) keyword.value = '';
        }
        state.status = 'all';
        state.fromDate = '';
        state.toDate = '';
        state.pageSize = 20;
        state.page = 1;
        document.getElementById('rewardVoucherStatus').value = 'all';
        document.getElementById('rewardVoucherFromDate').value = '';
        document.getElementById('rewardVoucherToDate').value = '';
        document.getElementById('rewardVoucherPageSize').value = '20';
        syncMobileFromDesktop();
        loadRewardVoucherIndex();
    }

    function bindEvents() {
        document.getElementById('rewardVoucherKeyword')?.addEventListener('input', event => {
            state.keyword = event.target.value.trim();
            state.page = 1;
            updateFilterPresentation();
            window.clearTimeout(rewardVoucherSearchTimer);
            rewardVoucherSearchTimer = window.setTimeout(loadRewardVoucherIndex, 350);
        });
        document.getElementById('rewardVoucherClearSearch')?.addEventListener('click', () => resetFilters(true));
        document.getElementById('rewardVoucherResetFilters')?.addEventListener('click', () => resetFilters(true));
        document.getElementById('rewardVoucherReload')?.addEventListener('click', loadRewardVoucherIndex);
        document.getElementById('rewardVoucherDismissError')?.addEventListener('click', clearError);

        ['rewardVoucherStatus', 'rewardVoucherFromDate', 'rewardVoucherToDate', 'rewardVoucherPageSize'].forEach(id => {
            document.getElementById(id)?.addEventListener('change', () => {
                readDesktopFilters();
                state.page = 1;
                syncMobileFromDesktop();
                loadRewardVoucherIndex();
            });
        });

        document.getElementById('rewardVoucherOpenMobileFilters')?.addEventListener('click', syncMobileFromDesktop);
        document.getElementById('rewardVoucherMobileApplyFilters')?.addEventListener('click', applyMobileFilters);
        document.getElementById('rewardVoucherMobileClearFilters')?.addEventListener('click', () => {
            document.getElementById('rewardVoucherMobileStatus').value = 'all';
            document.getElementById('rewardVoucherMobileFromDate').value = '';
            document.getElementById('rewardVoucherMobileToDate').value = '';
            document.getElementById('rewardVoucherMobilePageSize').value = '20';
        });

        root.addEventListener('click', event => {
            const viewButton = event.target.closest('[data-voucher-view]');
            if (viewButton) openRewardVoucherQuickView(viewButton.dataset.voucherView);

            const pageButton = event.target.closest('[data-voucher-page]');
            if (pageButton && !pageButton.disabled) {
                state.page = Number(pageButton.dataset.voucherPage || 1);
                loadRewardVoucherIndex();
            }
        });

        document.getElementById('rewardVoucherTableBody')?.addEventListener('dblclick', event => {
            const row = event.target.closest('[data-voucher-row]');
            if (row) openRewardVoucherQuickView(row.dataset.voucherRow);
        });
        document.getElementById('rewardVoucherMobileList')?.addEventListener('dblclick', event => {
            const card = event.target.closest('[data-voucher-card]');
            if (card) openRewardVoucherQuickView(card.dataset.voucherCard);
        });

        document.getElementById('rewardVoucherOpenHistory')?.addEventListener('click', openRewardVoucherHistory);
        document.getElementById('rewardVoucherPrint')?.addEventListener('click', printRewardVoucher);
        document.getElementById('rewardVoucherCancel')?.addEventListener('click', () => showRewardVoucherConfirmation({ kind: 'cancel', title: 'Hủy phiếu giảm giá', message: 'Phiếu sẽ không còn khả dụng tại POS.', alertClass: 'alert-danger', buttonClass: 'btn-danger', submitText: 'Xác nhận hủy' }));
        document.getElementById('rewardVoucherLock')?.addEventListener('click', () => showRewardVoucherConfirmation({ kind: 'lock', title: 'Khóa phiếu giảm giá', message: 'Phiếu sẽ tạm thời không thể sử dụng tại POS.', alertClass: 'alert-warning', buttonClass: 'btn-warning', submitText: 'Xác nhận khóa' }));
        document.getElementById('rewardVoucherUnlock')?.addEventListener('click', () => showRewardVoucherConfirmation({ kind: 'unlock', title: 'Mở khóa phiếu', message: 'Phiếu sẽ trở lại trạng thái khả dụng.', alertClass: 'alert-success', buttonClass: 'btn-success', submitText: 'Xác nhận mở khóa' }));
        document.getElementById('rewardVoucherConfirmationSubmit')?.addEventListener('click', submitRewardVoucherAction);
    }

    document.addEventListener('DOMContentLoaded', function () {
        bindEvents();
        readDesktopFilters();
        syncMobileFromDesktop();
        loadRewardVoucherIndex();
    });
})();
