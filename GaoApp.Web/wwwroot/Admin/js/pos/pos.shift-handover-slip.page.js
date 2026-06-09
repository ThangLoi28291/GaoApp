(function () {
    'use strict';

    const DENOMS = [500000, 200000, 100000, 50000, 20000, 10000, 5000, 2000, 1000];
    const PAGE_SIZE = 20;

    let currentPage = 1;
    let totalItems = 0;
    let selectedSlipId = null;

    const btnShowCreateSlipModal = document.getElementById('btnShowCreateSlipModal');
    const btnReloadSlips = document.getElementById('btnReloadSlips');
    const btnSearchSlips = document.getElementById('btnSearchSlips');

    const txtSlipKeyword = document.getElementById('txtSlipKeyword');
    const ddlSlipStatus = document.getElementById('ddlSlipStatus');
    const txtSlipFromDate = document.getElementById('txtSlipFromDate');
    const txtSlipToDate = document.getElementById('txtSlipToDate');

    const handoverSlipBody = document.getElementById('handoverSlipBody');
    const handoverSlipPagerInfo = document.getElementById('handoverSlipPagerInfo');
    const btnSlipPrev = document.getElementById('btnSlipPrev');
    const btnSlipNext = document.getElementById('btnSlipNext');

    const createSlipModalEl = document.getElementById('createSlipModal');
    const createSlipModal = createSlipModalEl ? new bootstrap.Modal(createSlipModalEl) : null;

    const slipDetailModalEl = document.getElementById('slipDetailModal');
    const slipDetailModal = slipDetailModalEl ? new bootstrap.Modal(slipDetailModalEl) : null;

    const ddlCreateSlipWarehouse = document.getElementById('ddlCreateSlipWarehouse');
    const txtCreateSlipNote = document.getElementById('txtCreateSlipNote');
    const createSlipDenomGrid = document.getElementById('createSlipDenomGrid');
    const createSlipTotalText = document.getElementById('createSlipTotalText');
    const createSlipBillCountText = document.getElementById('createSlipBillCountText');
    const createSlipSummaryTotalText = document.getElementById('createSlipSummaryTotalText');
    const btnResetCreateSlipDenoms = document.getElementById('btnResetCreateSlipDenoms');
    const btnCreateSlipConfirm = document.getElementById('btnCreateSlipConfirm');

    const slipDetailTitle = document.getElementById('slipDetailTitle');
    const slipDetailSub = document.getElementById('slipDetailSub');
    const slipDetailContent = document.getElementById('slipDetailContent');
    const btnPrintSlipDetail = document.getElementById('btnPrintSlipDetail');

    function getToken() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function formatMoney(value) {
        return Number(value || 0).toLocaleString('vi-VN');
    }

    function formatMoneyText(value) {
        return `${formatMoney(value)} đ`;
    }

    function formatDateTime(value) {
        if (!value) return '-';
        return new Date(value).toLocaleString('vi-VN');
    }

    function toUtcStart(dateValue) {
        if (!dateValue) return null;
        return new Date(`${dateValue}T00:00:00`).toISOString();
    }

    function toUtcEndExclusive(dateValue) {
        if (!dateValue) return null;
        const d = new Date(`${dateValue}T00:00:00`);
        d.setDate(d.getDate() + 1);
        return d.toISOString();
    }

    function showSuccess(msg) {
        if (window.toastr) toastr.success(msg);
        else console.log(msg);
    }

    function showError(msg) {
        if (window.toastr) toastr.error(msg);
        else alert(msg);
    }

    async function fetchJson(url, options) {
        if (window.PosError && window.PosError.fetchJson) {
            return await window.PosError.fetchJson(url, options);
        }

        const res = await fetch(url, options);
        if (!res.ok) throw new Error(await res.text());
        return await res.json();
    }

    async function postJson(url, data) {
        if (window.PosError && window.PosError.postJson) {
            return await window.PosError.postJson(url, data, getToken);
        }

        const res = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getToken()
            },
            body: JSON.stringify(data || {})
        });

        if (!res.ok) throw new Error(await res.text());
        return await res.json();
    }

    async function withButtonLoading(btn, action, text) {
        if (!btn) return await action();

        const old = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = `<span class="spinner-border spinner-border-sm me-2"></span>${text || 'Đang xử lý'}`;

        try {
            return await action();
        } finally {
            btn.disabled = false;
            btn.innerHTML = old;
        }
    }

    function statusText(status) {
        const s = String(status || '').toLowerCase();
        if (s === '1' || s.includes('draft')) return 'Mới tạo';
        if (s === '2' || s.includes('printed')) return 'Đã in';
        if (s === '3' || s.includes('used')) return 'Đã dùng';
        if (s === '4' || s.includes('cancelled')) return 'Đã hủy';
        return '-';
    }

    function statusClass(status) {
        const s = String(status || '').toLowerCase();
        if (s === '1' || s.includes('draft')) return 'draft';
        if (s === '2' || s.includes('printed')) return 'printed';
        if (s === '3' || s.includes('used')) return 'used';
        if (s === '4' || s.includes('cancelled')) return 'cancelled';
        return 'draft';
    }

    function buildDenomGrid() {
        if (!createSlipDenomGrid) return;

        createSlipDenomGrid.innerHTML = DENOMS.map((value, index) => `
            <div class="shift-open-money-row">
                <div class="shift-open-denom-info">
                    <div class="shift-open-bill-img bill-${value}"></div>
                    <div class="shift-open-denom-label">${formatMoney(value)} đ</div>
                </div>

                <div class="shift-open-stepper">
                    <button type="button" class="shift-open-stepper-btn" data-slip-step="down" data-value="${value}">−</button>

                    <input id="slipQty_${value}"
                           data-slip-denom="${value}"
                           data-index="${index}"
                           type="number"
                           min="0"
                           step="1"
                           value="0"
                           class="form-control shift-open-stepper-input" />

                    <button type="button" class="shift-open-stepper-btn" data-slip-step="up" data-value="${value}">+</button>
                </div>

                <div class="shift-open-line-total" id="slipLineTotal_${value}">0 đ</div>

                <button type="button" class="shift-open-line-clear" data-slip-clear="${value}">×</button>
            </div>
        `).join('');
    }

    function getDenomInputs() {
        return Array.from(document.querySelectorAll('input[data-slip-denom]'))
            .sort((a, b) => Number(a.dataset.index) - Number(b.dataset.index));
    }

    function calcSlipTotal() {
        let total = 0;
        let count = 0;

        getDenomInputs().forEach(input => {
            const denom = Number(input.dataset.slipDenom || 0);
            const qty = Math.max(0, Number(input.value || 0));
            const amount = denom * qty;

            total += amount;
            count += qty;

            const line = document.getElementById(`slipLineTotal_${denom}`);
            if (line) line.textContent = formatMoneyText(amount);
        });

        if (createSlipTotalText) createSlipTotalText.textContent = formatMoneyText(total);
        if (createSlipBillCountText) createSlipBillCountText.textContent = `${count} tờ`;
        if (createSlipSummaryTotalText) createSlipSummaryTotalText.textContent = formatMoneyText(total);

        return total;
    }

    function resetSlipDenoms() {
        getDenomInputs().forEach(x => x.value = 0);
        calcSlipTotal();
    }

    function getDenominationPayload() {
        return getDenomInputs()
            .map(input => ({
                denominationValue: Number(input.dataset.slipDenom || 0),
                quantity: Math.max(0, Number(input.value || 0))
            }))
            .filter(x => x.denominationValue > 0 && x.quantity > 0);
    }

    async function loadWarehouses() {
        const list = await fetchJson('/admin/api/warehouses');

        if (!ddlCreateSlipWarehouse) return;

        ddlCreateSlipWarehouse.innerHTML = '<option value="">-- Chọn kho --</option>';

        (list || []).forEach(w => {
            const opt = document.createElement('option');
            opt.value = w.id;
            opt.textContent = `${w.code} - ${w.name}`;
            ddlCreateSlipWarehouse.appendChild(opt);
        });

        const def = (list || []).find(x => x.isDefault || x.isDefaultForSale || x.isDefaultWarehouse);
        if (def) ddlCreateSlipWarehouse.value = def.id;
        else if ((list || []).length === 1) ddlCreateSlipWarehouse.value = list[0].id;
    }

    function buildQueryUrl() {
        const params = new URLSearchParams();

        if (ddlSlipStatus && ddlSlipStatus.value) params.set('status', ddlSlipStatus.value);
        if (txtSlipKeyword && txtSlipKeyword.value.trim()) params.set('keyword', txtSlipKeyword.value.trim());

        const from = txtSlipFromDate ? toUtcStart(txtSlipFromDate.value) : null;
        const to = txtSlipToDate ? toUtcEndExclusive(txtSlipToDate.value) : null;

        if (from) params.set('fromUtc', from);
        if (to) params.set('toUtcExclusive', to);

        params.set('page', currentPage);
        params.set('pageSize', PAGE_SIZE);

        return `/admin/pos/shift-handover-slips?${params.toString()}`;
    }

    async function loadSlips() {
        try {
            const result = await fetchJson(buildQueryUrl());

            const items = result.items || result.Items || [];
            totalItems = result.totalItems || result.total || result.TotalItems || 0;

            renderSlips(items);
            renderPager();
        } catch (err) {
            if (window.PosError) window.PosError.handle(err);
            else showError(err.message);
        }
    }

    function renderSlips(items) {
        if (!handoverSlipBody) return;

        if (!items.length) {
            handoverSlipBody.innerHTML = `
                <tr>
                    <td colspan="8" class="text-center text-muted py-4">
                        Chưa có phiếu nhận ca.
                    </td>
                </tr>`;
            return;
        }

        handoverSlipBody.innerHTML = items.map(x => {
            const stClass = statusClass(x.status);

            return `
                <tr>
                    <td>
                        <div class="handover-code">${x.slipCode || '-'}</div>
                        <div class="small text-muted">${x.barcodeValue || ''}</div>
                    </td>
                    <td>
                        <span class="handover-status ${stClass}">${statusText(x.status)}</span>
                    </td>
                    <td>${[x.warehouseCode, x.warehouseName].filter(Boolean).join(' - ') || '-'}</td>
                    <td>${[x.terminalCode, x.terminalName].filter(Boolean).join(' - ') || 'Dùng chung'}</td>
                    <td>${x.assignedToUserName || 'Chưa chỉ định'}</td>
                    <td class="text-end fw-bold">${formatMoneyText(x.openingCashTotal)}</td>
                    <td>${formatDateTime(x.createdAtUtc)}</td>
                    <td>
                        <div class="handover-actions">
                            <button type="button" class="btn btn-outline-primary handover-action-btn" data-view-slip="${x.id}">
                                Xem
                            </button>
                            <button type="button" class="btn btn-outline-success handover-action-btn" data-print-slip="${x.id}">
                                In
                            </button>
                            ${stClass === 'used' || stClass === 'cancelled'
                    ? ''
                    : `<button type="button" class="btn btn-outline-danger handover-action-btn" data-cancel-slip="${x.id}">
                                    Hủy
                                   </button>`}
                        </div>
                    </td>
                </tr>
            `;
        }).join('');
    }

    function renderPager() {
        const totalPages = Math.max(1, Math.ceil(totalItems / PAGE_SIZE));

        if (handoverSlipPagerInfo) {
            handoverSlipPagerInfo.textContent = `${totalItems} phiếu - Trang ${currentPage}/${totalPages}`;
        }

        if (btnSlipPrev) btnSlipPrev.disabled = currentPage <= 1;
        if (btnSlipNext) btnSlipNext.disabled = currentPage >= totalPages;
    }

    function openCreateModal() {
        resetSlipDenoms();

        if (txtCreateSlipNote) txtCreateSlipNote.value = '';

        if (createSlipModal) createSlipModal.show();

        setTimeout(() => {
            const first = getDenomInputs()[0];
            if (first) {
                first.focus();
                first.select();
            }
        }, 150);
    }

    async function createSlip() {
        const warehouseId = parseInt((ddlCreateSlipWarehouse && ddlCreateSlipWarehouse.value) || '0', 10);

        if (!warehouseId) {
            showError('Vui lòng chọn kho bán hàng.');
            return;
        }

        const denominations = getDenominationPayload();

        if (!denominations.length) {
            showError('Vui lòng nhập ít nhất một mệnh giá tiền.');
            return;
        }

        await withButtonLoading(btnCreateSlipConfirm, async () => {
            try {
                const slip = await postJson('/admin/pos/shift-handover-slips', {
                    warehouseId,
                    terminalId: null,
                    assignedToUserId: null,
                    note: txtCreateSlipNote ? txtCreateSlipNote.value : null,
                    denominations
                });

                showSuccess('Đã tạo phiếu nhận ca.');

                if (createSlipModal) createSlipModal.hide();

                currentPage = 1;
                await loadSlips();

                if (slip && slip.id) {
                    window.open(`/admin/pos-shift/handover-slip-print?id=${slip.id}`, '_blank');
                }
            } catch (err) {
                if (window.PosError) window.PosError.handle(err);
                else showError(err.message);
            }
        }, 'Đang tạo');
    }

    async function viewSlip(id) {
        try {
            const slip = await fetchJson(`/admin/pos/shift-handover-slips/${id}`);
            selectedSlipId = slip.id;

            if (slipDetailTitle) slipDetailTitle.textContent = `Phiếu ${slip.slipCode}`;
            if (slipDetailSub) slipDetailSub.textContent = `${statusText(slip.status)} - ${formatMoneyText(slip.openingCashTotal)}`;

            if (slipDetailContent) {
                slipDetailContent.innerHTML = renderSlipDetail(slip);
            }

            if (slipDetailModal) slipDetailModal.show();
        } catch (err) {
            if (window.PosError) window.PosError.handle(err);
            else showError(err.message);
        }
    }

    function renderSlipDetail(slip) {
        const denoms = slip.denominations || [];

        return `
            <div class="handover-detail-grid">
                <div class="handover-detail-item">
                    <div class="handover-detail-label">Mã phiếu</div>
                    <div class="handover-detail-value">${slip.slipCode}</div>
                </div>
                <div class="handover-detail-item">
                    <div class="handover-detail-label">Barcode</div>
                    <div class="handover-detail-value">${slip.barcodeValue}</div>
                </div>
                <div class="handover-detail-item">
                    <div class="handover-detail-label">Kho</div>
                    <div class="handover-detail-value">${[slip.warehouseCode, slip.warehouseName].filter(Boolean).join(' - ') || '-'}</div>
                </div>
                <div class="handover-detail-item">
                    <div class="handover-detail-label">Terminal</div>
                    <div class="handover-detail-value">${[slip.terminalCode, slip.terminalName].filter(Boolean).join(' - ') || 'Dùng chung'}</div>
                </div>
                <div class="handover-detail-item">
                    <div class="handover-detail-label">Người lập</div>
                    <div class="handover-detail-value">${slip.createdByUserName || '-'}</div>
                </div>
                <div class="handover-detail-item">
                    <div class="handover-detail-label">Người nhận</div>
                    <div class="handover-detail-value">${slip.assignedToUserName || 'Chưa chỉ định'}</div>
                </div>
                <div class="handover-detail-item">
                    <div class="handover-detail-label">Tổng tiền</div>
                    <div class="handover-detail-value">${formatMoneyText(slip.openingCashTotal)}</div>
                </div>
                <div class="handover-detail-item">
                    <div class="handover-detail-label">Ngày tạo</div>
                    <div class="handover-detail-value">${formatDateTime(slip.createdAtUtc)}</div>
                </div>
            </div>

            <table class="handover-denom-table">
                <thead>
                    <tr>
                        <th>Mệnh giá</th>
                        <th>Số tờ</th>
                        <th>Thành tiền</th>
                    </tr>
                </thead>
                <tbody>
                    ${denoms.length
                ? denoms.map(x => `
                            <tr>
                                <td>${formatMoneyText(x.denominationValue)}</td>
                                <td>${x.quantity}</td>
                                <td>${formatMoneyText(x.amount)}</td>
                            </tr>
                        `).join('')
                : `<tr><td colspan="3">Không có dữ liệu</td></tr>`}
                </tbody>
            </table>

            ${slip.note ? `<div class="shift-note-box mt-3"><div class="shift-note-label">Ghi chú</div><div class="shift-note-text">${slip.note}</div></div>` : ''}
        `;
    }

    function printSlip(id) {
        window.open(`/admin/pos-shift/handover-slip-print?id=${id}`, '_blank');
    }

    async function cancelSlip(id) {
        const reason = prompt('Nhập lý do hủy phiếu:');

        if (!reason || !reason.trim()) {
            return;
        }

        try {
            await postJson(`/admin/pos/shift-handover-slips/${id}/cancel`, {
                reason: reason.trim()
            });

            showSuccess('Đã hủy phiếu.');
            await loadSlips();
        } catch (err) {
            if (window.PosError) window.PosError.handle(err);
            else showError(err.message);
        }
    }

    function bindEvents() {
        if (btnShowCreateSlipModal) btnShowCreateSlipModal.addEventListener('click', openCreateModal);
        if (btnReloadSlips) btnReloadSlips.addEventListener('click', () => loadSlips());

        if (btnSearchSlips) {
            btnSearchSlips.addEventListener('click', function () {
                currentPage = 1;
                loadSlips();
            });
        }

        if (txtSlipKeyword) {
            txtSlipKeyword.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    currentPage = 1;
                    loadSlips();
                }
            });
        }

        if (btnSlipPrev) {
            btnSlipPrev.addEventListener('click', function () {
                if (currentPage > 1) {
                    currentPage--;
                    loadSlips();
                }
            });
        }

        if (btnSlipNext) {
            btnSlipNext.addEventListener('click', function () {
                const totalPages = Math.max(1, Math.ceil(totalItems / PAGE_SIZE));
                if (currentPage < totalPages) {
                    currentPage++;
                    loadSlips();
                }
            });
        }

        if (btnCreateSlipConfirm) btnCreateSlipConfirm.addEventListener('click', createSlip);

        if (btnResetCreateSlipDenoms) {
            btnResetCreateSlipDenoms.addEventListener('click', resetSlipDenoms);
        }

        if (btnPrintSlipDetail) {
            btnPrintSlipDetail.addEventListener('click', function () {
                if (selectedSlipId) printSlip(selectedSlipId);
            });
        }

        document.addEventListener('input', function (e) {
            if (e.target && e.target.matches('input[data-slip-denom]')) {
                if (Number(e.target.value || 0) < 0) e.target.value = 0;
                calcSlipTotal();
            }
        });

        document.addEventListener('click', function (e) {
            const stepBtn = e.target.closest('[data-slip-step]');
            if (stepBtn) {
                const value = stepBtn.dataset.value;
                const input = document.getElementById(`slipQty_${value}`);
                if (!input) return;

                const current = Number(input.value || 0);
                input.value = stepBtn.dataset.slipStep === 'up'
                    ? current + 1
                    : Math.max(0, current - 1);

                calcSlipTotal();
                input.focus();
                input.select();
                return;
            }

            const clearBtn = e.target.closest('[data-slip-clear]');
            if (clearBtn) {
                const value = clearBtn.dataset.slipClear;
                const input = document.getElementById(`slipQty_${value}`);
                if (!input) return;

                input.value = 0;
                calcSlipTotal();
                input.focus();
                input.select();
                return;
            }

            const viewBtn = e.target.closest('[data-view-slip]');
            if (viewBtn) {
                viewSlip(Number(viewBtn.dataset.viewSlip));
                return;
            }

            const printBtn = e.target.closest('[data-print-slip]');
            if (printBtn) {
                printSlip(Number(printBtn.dataset.printSlip));
                return;
            }

            const cancelBtn = e.target.closest('[data-cancel-slip]');
            if (cancelBtn) {
                cancelSlip(Number(cancelBtn.dataset.cancelSlip));
            }
        });

        document.addEventListener('keydown', function (e) {
            if (createSlipModalEl && createSlipModalEl.classList.contains('show')) {
                if (e.ctrlKey && e.key === 'Enter') {
                    e.preventDefault();
                    createSlip();
                    return;
                }

                if (e.key === 'Enter' && e.target.matches('input[data-slip-denom]')) {
                    e.preventDefault();
                    const inputs = getDenomInputs();
                    const idx = inputs.indexOf(e.target);
                    const next = inputs[idx + 1];
                    if (next) {
                        next.focus();
                        next.select();
                    }
                }
            }
        });
    }

    document.addEventListener('DOMContentLoaded', async function () {
        buildDenomGrid();
        bindEvents();
        await loadWarehouses();
        await loadSlips();
        calcSlipTotal();
    });
})();