(function () {
    'use strict';

    const apiUrl = '/admin/api/legal-entities';
    const page = document.getElementById('legalEntityPage');
    if (!page) return;

    const permissions = {
        canCreate: page.dataset.canCreate === 'true',
        canUpdate: page.dataset.canUpdate === 'true',
        canPreflight: page.dataset.canPreflight === 'true'
    };

    let state = {
        legalEntities: [],
        warehouses: [],
        invoiceSettings: [],
        isMultiLegalEntityEnabled: false,
        multiLegalEntityActivatedAtUtc: null,
        isConfigurationReady: false,
        canActivate: false,
        canary: null
    };
    let modal;

    document.addEventListener('DOMContentLoaded', function () {
        modal = new bootstrap.Modal(document.getElementById('legalEntityModal'));
        document.getElementById('btnCreateLegalEntity')?.addEventListener('click', openCreate);
        document.getElementById('legalEntityForm')?.addEventListener('submit', save);
        document.getElementById('btnRefreshPreflight')?.addEventListener('click', loadPreflight);
        document.getElementById('multiLegalEntityEnabled')?.addEventListener('change', changeFeatureState);
        loadManagement();
    });

    async function loadManagement() {
        setTableLoading();
        try {
            const response = await fetch(apiUrl, { headers: { Accept: 'application/json' }, cache: 'no-store' });
            if (!response.ok) throw new Error(await readError(response, 'Không tải được danh sách HKD.'));

            const data = await response.json();
            state = {
                legalEntities: (data.legalEntities || []).map(normalizeLegalEntity),
                warehouses: (data.warehouses || []).map(normalizeWarehouse),
                invoiceSettings: (data.invoiceSettings || []).map(normalizeInvoiceSetting),
                isMultiLegalEntityEnabled: toBool(data.isMultiLegalEntityEnabled),
                multiLegalEntityActivatedAtUtc: data.multiLegalEntityActivatedAtUtc || null,
                isConfigurationReady: false,
                canActivate: false,
                canary: null
            };

            renderStatistics();
            renderFeatureGate();
            renderTable();
            if (permissions.canPreflight) {
                await loadPreflight();
                await loadCanaryStatus();
            }
        } catch (error) {
            showTableError(error.message);
            notify('error', error.message);
        }
    }

    async function loadPreflight() {
        const summary = document.getElementById('preflightSummary');
        const container = document.getElementById('preflightChecks');
        if (!summary || !container) return;

        summary.className = 'alert alert-info mb-3';
        summary.textContent = 'Đang kiểm tra cấu hình...';
        container.innerHTML = '';

        try {
            const response = await fetch(`${apiUrl}/preflight`, { headers: { Accept: 'application/json' }, cache: 'no-store' });
            if (!response.ok) throw new Error(await readError(response, 'Không chạy được activation preflight.'));
            const data = await response.json();
            const ready = toBool(data.isConfigurationReady);
            state.isConfigurationReady = ready;
            state.canActivate = toBool(data.canActivate);
            renderFeatureGate();

            summary.className = `alert ${ready ? 'alert-success' : 'alert-danger'} mb-3`;
            summary.innerHTML = `<div class="fw-semibold">${ready ? 'Cấu hình đã sẵn sàng' : 'Cấu hình chưa sẵn sàng'}</div>
                <div class="small mt-1">${escapeHtml(data.activationGateMessage || '')}</div>`;

            container.innerHTML = (data.checks || []).map(check => {
                const passed = toBool(check.isPassed);
                return `<div class="col-md-6 col-xl-4">
                    <div class="preflight-check ${passed ? '' : 'is-failed'}">
                        <div class="d-flex gap-2 align-items-start">
                            <i class="bx ${passed ? 'bx-check-circle text-success' : 'bx-error-circle text-danger'} fs-4"></i>
                            <div>
                                <div class="fw-semibold">${escapeHtml(check.title || '')}</div>
                                <div class="legal-muted mt-1">${escapeHtml(check.message || '')}</div>
                            </div>
                        </div>
                    </div>
                </div>`;
            }).join('');
        } catch (error) {
            state.isConfigurationReady = false;
            state.canActivate = false;
            renderFeatureGate();
            summary.className = 'alert alert-danger mb-3';
            summary.textContent = error.message;
        }
    }

    async function loadCanaryStatus() {
        try {
            const response = await fetch(`${apiUrl}/canary`, {
                headers: { Accept: 'application/json' },
                cache: 'no-store'
            });
            if (!response.ok) throw new Error(await readError(response, 'Không tải được trạng thái canary.'));
            state.canary = await response.json();
            renderCanaryStatus();
        } catch (error) {
            state.canary = null;
            renderCanaryStatus(error.message);
        }
    }

    function renderCanaryStatus(errorMessage) {
        const badge = document.getElementById('canaryHealthBadge');
        const message = document.getElementById('canaryHealthMessage');
        const metricsContainer = document.getElementById('canaryMetrics');
        const checksContainer = document.getElementById('canaryHealthChecks');
        const eventsContainer = document.getElementById('canaryEvents');
        if (!badge || !message || !metricsContainer || !checksContainer || !eventsContainer) return;

        if (!state.canary) {
            badge.className = 'badge bg-label-danger';
            badge.textContent = 'Không tải được';
            message.textContent = errorMessage || 'Không có dữ liệu canary.';
            metricsContainer.innerHTML = '';
            checksContainer.innerHTML = '';
            eventsContainer.innerHTML = '';
            return;
        }

        const canary = state.canary;
        const health = String(canary.healthStatus || 'Stopped');
        const healthStyle = {
            Healthy: ['bg-label-success', 'Ổn định'],
            Warning: ['bg-label-warning', 'Cảnh báo'],
            Critical: ['bg-label-danger', 'Nghiêm trọng'],
            Stopped: ['bg-label-secondary', 'Đã dừng']
        }[health] || ['bg-label-secondary', health];
        badge.className = `badge ${healthStyle[0]}`;
        badge.textContent = healthStyle[1];
        message.textContent = canary.healthMessage || '';
        setText('canaryMonitoringSince', canary.monitoringSinceUtc
            ? `Theo dõi từ ${formatLocalDateTime(canary.monitoringSinceUtc)}`
            : 'Chưa có lần kích hoạt');
        setText('canaryEvaluatedAt', canary.evaluatedAtUtc
            ? `Cập nhật ${formatLocalDateTime(canary.evaluatedAtUtc)}`
            : '');

        const metrics = canary.metrics || {};
        const metricItems = [
            ['Đơn đã allocation', metrics.allocatedOrderCount, 'text-primary'],
            ['Đơn tách nhiều HKD', metrics.splitOrderCount, 'text-info'],
            ['Lệch allocation', metrics.allocationMismatchCount, Number(metrics.allocationMismatchCount) ? 'text-danger' : 'text-success'],
            ['Lệch hóa đơn', metrics.invoiceMismatchCount, Number(metrics.invoiceMismatchCount) ? 'text-danger' : 'text-success'],
            ['Âm tồn đang xử lý', metrics.openInventoryIssueCount, Number(metrics.openInventoryIssueCount) ? 'text-warning' : 'text-success'],
            ['Hóa đơn phát hành lỗi', metrics.failedInvoiceCount, Number(metrics.failedInvoiceCount) ? 'text-warning' : 'text-success'],
            ['Giữ hàng quá 24 giờ', metrics.staleReservationCount, Number(metrics.staleReservationCount) ? 'text-warning' : 'text-success'],
            ['Giỏ Multi đang mở', metrics.pendingMultiModeOrderCount, 'text-primary']
        ];
        metricsContainer.innerHTML = metricItems.map(item => `<div class="col-6 col-md-4 col-xl-3">
            <div class="canary-metric">
                <div class="text-muted small mb-1">${escapeHtml(item[0])}</div>
                <div class="canary-metric-value ${item[2]}">${Number(item[1] || 0)}</div>
            </div>
        </div>`).join('');

        const checks = canary.healthChecks || [];
        checksContainer.innerHTML = checks.length ? checks.map(check => {
            const passed = toBool(check.isPassed);
            const level = String(check.level || 'Info').toLowerCase();
            const failedClass = passed ? '' : `is-failed-${level}`;
            return `<div class="canary-check ${failedClass}">
                <div class="d-flex gap-2 align-items-start">
                    <i class="bx ${passed ? 'bx-check-circle text-success' : level === 'critical' ? 'bx-error-circle text-danger' : 'bx-error text-warning'} fs-5"></i>
                    <div>
                        <div class="fw-semibold">${escapeHtml(check.title || '')}</div>
                        <div class="legal-muted mt-1">${escapeHtml(check.message || '')}</div>
                    </div>
                </div>
            </div>`;
        }).join('') : '<div class="text-muted small">Chưa có kiểm tra sức khỏe.</div>';

        const actionLabel = { Activate: 'Kích hoạt', Reactivate: 'Bật lại', KillSwitch: 'Kill switch' };
        const events = canary.recentEvents || [];
        eventsContainer.innerHTML = events.length ? events.map(item => `<div class="canary-event">
            <div class="d-flex justify-content-between gap-2">
                <span class="fw-semibold">${escapeHtml(actionLabel[item.action] || item.action || '')}</span>
                <span class="badge ${toBool(item.newIsEnabled) ? 'bg-label-success' : 'bg-label-danger'}">${toBool(item.newIsEnabled) ? 'Bật' : 'Tắt'}</span>
            </div>
            <div class="legal-muted mt-1">${formatLocalDateTime(item.occurredAtUtc)}</div>
            <div class="small mt-2">${escapeHtml(item.reason || '')}</div>
            <div class="legal-muted mt-1">${escapeHtml(item.changedByUserName || (item.changedByUserId ? `User #${item.changedByUserId}` : 'Hệ thống'))}</div>
        </div>`).join('') : '<div class="text-muted small">Chưa có lịch sử bật/tắt.</div>';
    }

    function renderStatistics() {
        const active = state.legalEntities.filter(x => x.isActive);
        setText('legalEntityTotal', state.legalEntities.length);
        setText('legalEntityActive', active.length);
        setText('legalEntityWarehouseReady', active.filter(x => x.defaultWarehouseId).length);
        setText('legalEntityInvoiceReady', active.filter(x => x.invoiceProviderSettingId).length);
    }

    function renderFeatureGate() {
        const banner = document.getElementById('featureGateBanner');
        const title = document.getElementById('featureGateTitle');
        const message = document.getElementById('featureGateMessage');
        const toggle = document.getElementById('multiLegalEntityEnabled');
        const toggleLabel = document.getElementById('multiLegalEntityEnabledLabel');
        const activatedAt = document.getElementById('multiLegalEntityActivatedAt');
        const readOnlyState = document.getElementById('multiLegalEntityReadOnlyState');
        if (!banner || !title || !message) return;

        if (state.isMultiLegalEntityEnabled) {
            banner.className = 'alert alert-success d-flex flex-column flex-lg-row gap-3 align-items-lg-center mb-4';
            title.textContent = 'Multi LegalEntity đang bật';
            message.textContent = 'Mỗi lần chốt đơn tiếp theo sẽ phân bổ tồn theo thứ tự ưu tiên của các HKD.';
        } else {
            banner.className = 'alert alert-warning d-flex flex-column flex-lg-row gap-3 align-items-lg-center mb-4';
            title.textContent = 'Multi LegalEntity đang tắt';
            message.textContent = state.isConfigurationReady
                ? 'Cấu hình đã sẵn sàng. Có thể bật để thử phân bổ tồn kho theo HKD.'
                : 'Hệ thống đang dùng kho của ca POS theo luồng legacy. Hãy xử lý các mục preflight trước khi bật.';
        }

        if (toggle) {
            toggle.checked = state.isMultiLegalEntityEnabled;
            toggle.disabled = !permissions.canPreflight ||
                (!state.isMultiLegalEntityEnabled && !state.canActivate);
        }
        if (toggleLabel) {
            toggleLabel.textContent = state.isMultiLegalEntityEnabled ? 'Đang bật' : 'Đang tắt';
        }
        if (activatedAt) {
            activatedAt.textContent = state.isMultiLegalEntityEnabled && state.multiLegalEntityActivatedAtUtc
                ? `Bật lúc ${formatLocalDateTime(state.multiLegalEntityActivatedAtUtc)}`
                : state.isConfigurationReady
                    ? 'Sẵn sàng để bật'
                    : 'Chưa đạt preflight';
        }
        if (readOnlyState) {
            readOnlyState.className = `badge ${state.isMultiLegalEntityEnabled ? 'bg-label-success' : 'bg-label-secondary'}`;
            readOnlyState.textContent = state.isMultiLegalEntityEnabled ? 'Đang bật' : 'Đang tắt';
        }
    }

    async function changeFeatureState(event) {
        const toggle = event.currentTarget;
        const isEnabled = toggle.checked;

        if (isEnabled && !state.canActivate) {
            toggle.checked = false;
            notify('error', 'Chưa thể bật: activation preflight chưa đạt.');
            return;
        }

        const confirmed = window.Swal
            ? (await Swal.fire({
                title: isEnabled ? 'Bật phân bổ theo HKD?' : 'Tắt phân bổ theo HKD?',
                text: isEnabled
                    ? 'Các lần chốt đơn từ thời điểm bật sẽ tự phân bổ tồn theo ưu tiên HKD.'
                    : 'Các lần chốt đơn tiếp theo sẽ quay về xuất kho legacy theo kho của ca POS.',
                icon: 'warning',
                showCancelButton: true,
                confirmButtonText: isEnabled ? 'Bật ngay' : 'Tắt ngay',
                cancelButtonText: 'Hủy',
                confirmButtonColor: isEnabled ? '#198754' : '#dc3545'
            })).isConfirmed
            : confirm(isEnabled ? 'Bật phân bổ tồn theo HKD?' : 'Tắt và quay về luồng legacy?');

        if (!confirmed) {
            toggle.checked = !isEnabled;
            return;
        }

        let reason;
        if (window.Swal) {
            const reasonResult = await Swal.fire({
                title: isEnabled ? 'Lý do kích hoạt canary' : 'Lý do bật kill switch',
                input: 'textarea',
                inputLabel: 'Lý do được lưu vào nhật ký vận hành',
                inputPlaceholder: isEnabled
                    ? 'Ví dụ: Bắt đầu UAT canary tại cửa hàng...'
                    : 'Ví dụ: Phát hiện sai lệch cần dừng order mới...',
                inputAttributes: { maxlength: '500' },
                showCancelButton: true,
                confirmButtonText: 'Xác nhận',
                cancelButtonText: 'Hủy',
                inputValidator: value => {
                    if (!value || value.trim().length < 5) return 'Vui lòng nhập ít nhất 5 ký tự.';
                    return null;
                }
            });
            if (!reasonResult.isConfirmed) {
                toggle.checked = !isEnabled;
                return;
            }
            reason = String(reasonResult.value || '').trim();
        } else {
            reason = String(prompt('Nhập lý do bật/tắt (ít nhất 5 ký tự):') || '').trim();
            if (reason.length < 5) {
                toggle.checked = !isEnabled;
                notify('error', 'Vui lòng nhập lý do ít nhất 5 ký tự.');
                return;
            }
        }

        toggle.disabled = true;
        try {
            const response = await fetch(`${apiUrl}/feature-state`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                body: JSON.stringify({
                    isEnabled,
                    expectedCurrentState: state.isMultiLegalEntityEnabled,
                    reason
                })
            });
            if (!response.ok) throw new Error(await readError(response, 'Không cập nhật được trạng thái Multi LegalEntity.'));
            const result = await response.json();
            notify('success', result.message || 'Đã cập nhật trạng thái.');
            await loadManagement();
        } catch (error) {
            toggle.checked = !isEnabled;
            notify('error', error.message);
            renderFeatureGate();
        }
    }

    function renderTable() {
        const tbody = document.querySelector('#legalEntityTable tbody');
        if (!tbody) return;
        if (!state.legalEntities.length) {
            tbody.innerHTML = '<tr><td colspan="8" class="text-center text-muted py-5">Chưa có HKD nào.</td></tr>';
            return;
        }

        tbody.innerHTML = state.legalEntities.map(x => {
            const warehouse = x.defaultWarehouseId
                ? `<div class="fw-semibold">${escapeHtml(x.defaultWarehouseCode || '')} - ${escapeHtml(x.defaultWarehouseName || '')}</div>`
                : '<span class="badge bg-label-danger">Chưa chọn</span>';
            const invoice = x.invoiceProviderSettingId
                ? `<div class="fw-semibold">${escapeHtml(x.invoiceProviderCode || '')}</div>
                   <div class="legal-muted">MST ${escapeHtml(x.invoiceSupplierTaxCode || '')}</div>`
                : '<span class="badge bg-label-danger">Chưa chọn</span>';
            const actions = permissions.canUpdate
                ? `<button class="btn btn-sm btn-outline-primary" type="button" onclick="LegalEntityPage.openEdit(${x.id})">Sửa</button>`
                : '';
            const toggle = permissions.canUpdate
                ? `<input class="form-check-input" type="checkbox" ${x.isActive ? 'checked' : ''}
                     onchange="LegalEntityPage.confirmActive(${x.id}, this.checked, this)" />`
                : `<span class="badge ${x.isActive ? 'bg-label-success' : 'bg-label-secondary'}">${x.isActive ? 'Hoạt động' : 'Đã khóa'}</span>`;

            return `<tr>
                <td class="text-center"><span class="legal-priority">${x.salePriority}</span></td>
                <td>
                    <div class="legal-code">${escapeHtml(x.code)}</div>
                    <div class="fw-semibold">${escapeHtml(x.name)}</div>
                    <div class="legal-muted">${x.activeWarehouseCount}/${x.warehouseCount} kho hoạt động</div>
                </td>
                <td>
                    <div class="fw-semibold">${escapeHtml(x.legalName)}</div>
                    <div class="legal-muted">MST: ${escapeHtml(x.taxCode || 'Chưa có')}</div>
                    <div class="legal-muted text-truncate" style="max-width:240px">${escapeHtml(x.address || '')}</div>
                </td>
                <td>${warehouse}</td>
                <td>${invoice}</td>
                <td class="text-center">${x.isDefaultForPurchase ? '<span class="badge bg-label-primary"><i class="bx bx-check me-1"></i>Mặc định</span>' : '-'}</td>
                <td class="text-center"><div class="form-check form-switch d-flex justify-content-center">${toggle}</div></td>
                <td class="text-end">${actions}</td>
            </tr>`;
        }).join('');
    }

    function openCreate() {
        if (!permissions.canCreate) return;
        clearError();
        setValue('legalEntityId', 0);
        setValue('legalEntityCode', '');
        setValue('legalEntityName', '');
        setValue('legalEntityLegalName', '');
        setValue('legalEntityTaxCode', '');
        setValue('legalEntityAddress', '');
        setValue('legalEntityPhone', '');
        setValue('legalEntityEmail', '');
        setValue('legalEntitySalePriority', nextPriority());
        setChecked('legalEntityDefaultPurchase', !state.legalEntities.some(x => x.isActive && x.isDefaultForPurchase));
        setValue('legalEntityNote', '');
        document.getElementById('legalEntityModalTitle').textContent = 'Thêm HKD';
        renderWarehouseOptions(null, null);
        renderInvoiceOptions(null, null);
        modal.show();
        setTimeout(() => document.getElementById('legalEntityCode')?.focus(), 250);
    }

    function openEdit(id) {
        if (!permissions.canUpdate) return;
        const item = findEntity(id);
        if (!item) return notify('error', 'Không tìm thấy HKD.');

        clearError();
        setValue('legalEntityId', item.id);
        setValue('legalEntityCode', item.code);
        setValue('legalEntityName', item.name);
        setValue('legalEntityLegalName', item.legalName);
        setValue('legalEntityTaxCode', item.taxCode || '');
        setValue('legalEntityAddress', item.address || '');
        setValue('legalEntityPhone', item.phone || '');
        setValue('legalEntityEmail', item.email || '');
        setValue('legalEntitySalePriority', item.salePriority);
        setChecked('legalEntityDefaultPurchase', item.isDefaultForPurchase);
        setValue('legalEntityNote', item.note || '');
        document.getElementById('legalEntityModalTitle').textContent = `Cập nhật ${item.name}`;
        renderWarehouseOptions(item.id, item.defaultWarehouseId);
        renderInvoiceOptions(item.id, item.invoiceProviderSettingId);
        modal.show();
    }

    function renderWarehouseOptions(legalEntityId, selectedId) {
        const select = document.getElementById('legalEntityDefaultWarehouse');
        const help = document.getElementById('defaultWarehouseHelp');
        const items = legalEntityId
            ? state.warehouses.filter(x => x.legalEntityId === Number(legalEntityId))
            : [];
        select.innerHTML = '<option value="">-- Chưa chọn --</option>' + items.map(x =>
            `<option value="${x.id}" ${x.isActive ? '' : 'disabled'}>${escapeHtml(x.code)} - ${escapeHtml(x.name)}${x.isActive ? '' : ' (đã khóa)'}</option>`
        ).join('');
        select.disabled = !legalEntityId || !items.length;
        select.value = selectedId ? String(selectedId) : '';
        help.textContent = !legalEntityId
            ? 'Tạo HKD trước, sau đó tạo kho thuộc HKD ở màn Quản lý kho.'
            : (items.length ? 'Chỉ hiển thị kho thuộc đúng HKD.' : 'HKD chưa có kho. Hãy tạo kho ở màn Quản lý kho.');
    }

    function renderInvoiceOptions(legalEntityId, selectedId) {
        const assignedByOther = new Set(state.legalEntities
            .filter(x => x.id !== Number(legalEntityId) && x.invoiceProviderSettingId)
            .map(x => x.invoiceProviderSettingId));
        const items = state.invoiceSettings.filter(x => !assignedByOther.has(x.id) || x.id === Number(selectedId));
        const select = document.getElementById('legalEntityInvoiceSetting');
        select.innerHTML = '<option value="">-- Chưa chọn --</option>' + items.map(x =>
            `<option value="${x.id}">${escapeHtml(x.providerCode)} - MST ${escapeHtml(x.supplierTaxCode)} - ${escapeHtml(x.templateCode)}/${escapeHtml(x.invoiceSeries)}${x.isActive ? '' : ' (đã tắt)'}</option>`
        ).join('');
        select.value = selectedId ? String(selectedId) : '';
    }

    async function save(event) {
        event.preventDefault();
        clearError();

        const id = numberValue('legalEntityId');
        const payload = {
            id,
            code: value('legalEntityCode').toUpperCase(),
            name: value('legalEntityName'),
            legalName: value('legalEntityLegalName'),
            taxCode: nullableValue('legalEntityTaxCode'),
            address: nullableValue('legalEntityAddress'),
            phone: nullableValue('legalEntityPhone'),
            email: nullableValue('legalEntityEmail'),
            invoiceProviderSettingId: nullableNumberValue('legalEntityInvoiceSetting'),
            salePriority: numberValue('legalEntitySalePriority'),
            isDefaultForPurchase: document.getElementById('legalEntityDefaultPurchase').checked,
            note: nullableValue('legalEntityNote')
        };
        if (id > 0) payload.defaultWarehouseId = nullableNumberValue('legalEntityDefaultWarehouse');

        if (!payload.code || !payload.name || !payload.legalName || payload.salePriority <= 0) {
            return showError('Vui lòng nhập mã, tên hiển thị, tên pháp lý và ưu tiên bán hợp lệ.');
        }

        const button = document.getElementById('btnSaveLegalEntity');
        const oldText = button.textContent;
        button.disabled = true;
        button.textContent = 'Đang lưu...';
        try {
            const response = await fetch(id > 0 ? `${apiUrl}/${id}` : apiUrl, {
                method: id > 0 ? 'PUT' : 'POST',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                body: JSON.stringify(payload)
            });
            if (!response.ok) throw new Error(await readError(response, 'Lưu HKD thất bại.'));
            const result = await response.json();
            modal.hide();
            notify('success', result.message || 'Đã lưu HKD.');
            await loadManagement();
        } catch (error) {
            showError(error.message);
        } finally {
            button.disabled = false;
            button.textContent = oldText;
        }
    }

    async function confirmActive(id, isActive, checkbox) {
        const item = findEntity(id);
        if (!item) return;
        const confirmed = window.Swal
            ? (await Swal.fire({
                title: isActive ? 'Mở lại HKD?' : 'Khóa HKD?',
                text: isActive
                    ? `HKD "${item.name}" sẽ hoạt động trở lại.`
                    : `Cần khóa toàn bộ kho của "${item.name}" trước khi khóa HKD.`,
                icon: 'warning',
                showCancelButton: true,
                confirmButtonText: isActive ? 'Mở lại' : 'Khóa HKD',
                cancelButtonText: 'Hủy',
                confirmButtonColor: isActive ? '#696cff' : '#dc3545'
            })).isConfirmed
            : confirm(isActive ? 'Mở lại HKD?' : 'Khóa HKD?');
        if (!confirmed) {
            checkbox.checked = !isActive;
            return;
        }

        checkbox.disabled = true;
        try {
            const response = await fetch(`${apiUrl}/${id}/active`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                body: JSON.stringify({ isActive })
            });
            if (!response.ok) throw new Error(await readError(response, 'Cập nhật trạng thái thất bại.'));
            const result = await response.json();
            notify('success', result.message || 'Đã cập nhật trạng thái.');
            await loadManagement();
        } catch (error) {
            checkbox.checked = !isActive;
            notify('error', error.message);
        } finally {
            checkbox.disabled = false;
        }
    }

    function normalizeLegalEntity(x) {
        return {
            id: Number(x.id), code: x.code || '', name: x.name || '', legalName: x.legalName || '',
            taxCode: x.taxCode || null, address: x.address || null, phone: x.phone || null, email: x.email || null,
            defaultWarehouseId: x.defaultWarehouseId ? Number(x.defaultWarehouseId) : null,
            defaultWarehouseName: x.defaultWarehouseName || null, defaultWarehouseCode: x.defaultWarehouseCode || null,
            invoiceProviderSettingId: x.invoiceProviderSettingId ? Number(x.invoiceProviderSettingId) : null,
            invoiceSupplierTaxCode: x.invoiceSupplierTaxCode || null, invoiceProviderCode: x.invoiceProviderCode || null,
            isInvoiceSettingActive: x.isInvoiceSettingActive == null ? null : toBool(x.isInvoiceSettingActive),
            salePriority: Number(x.salePriority), isDefaultForPurchase: toBool(x.isDefaultForPurchase),
            isActive: toBool(x.isActive), warehouseCount: Number(x.warehouseCount || 0),
            activeWarehouseCount: Number(x.activeWarehouseCount || 0), note: x.note || null
        };
    }

    function normalizeWarehouse(x) {
        return { id: Number(x.id), legalEntityId: Number(x.legalEntityId), code: x.code || '', name: x.name || '', isActive: toBool(x.isActive), allowNegativeInventory: toBool(x.allowNegativeInventory) };
    }

    function normalizeInvoiceSetting(x) {
        return { id: Number(x.id), providerCode: x.providerCode || '', supplierTaxCode: x.supplierTaxCode || '', templateCode: x.templateCode || '', invoiceSeries: x.invoiceSeries || '', isActive: toBool(x.isActive) };
    }

    function findEntity(id) { return state.legalEntities.find(x => x.id === Number(id)); }
    function nextPriority() { return state.legalEntities.reduce((max, x) => Math.max(max, x.salePriority), 0) + 1; }
    function setTableLoading() { const x = document.querySelector('#legalEntityTable tbody'); if (x) x.innerHTML = '<tr><td colspan="8" class="text-center text-muted py-5">Đang tải dữ liệu...</td></tr>'; }
    function showTableError(message) { const x = document.querySelector('#legalEntityTable tbody'); if (x) x.innerHTML = `<tr><td colspan="8" class="text-center text-danger py-5">${escapeHtml(message)}</td></tr>`; }
    function setText(id, text) { const x = document.getElementById(id); if (x) x.textContent = text; }
    function setValue(id, text) { const x = document.getElementById(id); if (x) x.value = text == null ? '' : String(text); }
    function setChecked(id, checked) { const x = document.getElementById(id); if (x) x.checked = Boolean(checked); }
    function value(id) { return (document.getElementById(id)?.value || '').trim(); }
    function nullableValue(id) { const x = value(id); return x || null; }
    function numberValue(id) { return Number(document.getElementById(id)?.value || 0); }
    function nullableNumberValue(id) { const x = numberValue(id); return x > 0 ? x : null; }
    function toBool(x) { return x === true || x === 1 || x === '1' || x === 'true'; }
    function formatLocalDateTime(value) { const date = new Date(value); return Number.isNaN(date.getTime()) ? value : date.toLocaleString('vi-VN'); }
    function showError(message) { const x = document.getElementById('legalEntityError'); x.textContent = message; x.classList.remove('d-none'); }
    function clearError() { const x = document.getElementById('legalEntityError'); x.textContent = ''; x.classList.add('d-none'); }
    function notify(type, message) { if (window.GaoAppNotify?.[type]) window.GaoAppNotify[type](message); else if (type === 'error') alert(message); }
    async function readError(response, fallback) { try { const x = await response.json(); return x.message || x.detail || x.title || fallback; } catch { return fallback; } }
    function escapeHtml(x) { return String(x ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;'); }

    window.LegalEntityPage = { openEdit, confirmActive };
})();
