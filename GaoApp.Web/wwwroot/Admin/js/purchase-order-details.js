(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        bindReceiptRows();
        initApprovalSupplier();
        bindSelfApprovalWarning();
        initResolutionLookups();
        bindQuickCreateProduct();
        bindPageShortcuts();
    });

    function bindReceiptRows() {
        document.querySelectorAll('.receipt-row').forEach(function (row) {
            const checkbox = row.querySelector('.receive-enabled');
            const unitSelect = row.querySelector('.receipt-unit');
            const quantityInput = row.querySelector('.receipt-quantity');
            const conversionText = row.querySelector('.receipt-conversion');
            const overdeliveryText = row.querySelector('.receipt-overdelivery');
            if (!checkbox) return;
            if (checkbox.dataset.bound === '1') return;
            checkbox.dataset.bound = '1';
            checkbox.addEventListener('change', function () {
                row.querySelectorAll('.receipt-input').forEach(function (input) {
                    input.disabled = !checkbox.checked;
                });
                updateConversion(false);
            });

            function updateConversion(resetToMaximum) {
                const option = unitSelect?.selectedOptions[0];
                const factor = Number(option?.dataset.factor || 0);
                const plannedMaximum = Number(option?.dataset.plannedMax || 0);
                const entryMaximum = Number(option?.dataset.entryMax || 0);
                if (quantityInput && Number.isFinite(entryMaximum)) {
                    quantityInput.max = entryMaximum.toFixed(3);
                    const current = Number(quantityInput.value || 0);
                    if (resetToMaximum || !Number.isFinite(current) || current <= 0 || current > entryMaximum) {
                        quantityInput.value = plannedMaximum > 0 ? plannedMaximum.toFixed(3) : '';
                    }
                }
                const quantity = Number(quantityInput?.value || 0);
                const canonical = quantity * factor;
                if (conversionText) {
                    conversionText.textContent = Number.isFinite(canonical) && canonical > 0
                        ? `Tương đương: ${canonical.toFixed(3)} ${row.dataset.baseUnit || 'đơn vị gốc'}`
                        : 'Tương đương: —';
                }
                const canonicalOrdered = Number(row.dataset.canonicalOrdered || 0);
                const canonicalConfirmed = Number(row.dataset.canonicalConfirmed || 0);
                const projectedOverdelivery = Math.max(0, canonicalConfirmed + canonical - canonicalOrdered);
                if (overdeliveryText) {
                    overdeliveryText.textContent = checkbox.checked && projectedOverdelivery > 0
                        ? `Dự kiến vượt ${projectedOverdelivery.toFixed(3)} ${row.dataset.baseUnit || 'đơn vị gốc'}; cần Manager/Admin xác nhận khi duyệt.`
                        : '';
                }
            }

            unitSelect?.addEventListener('change', function () { updateConversion(true); });
            quantityInput?.addEventListener('input', function () { updateConversion(false); });
            updateConversion(false);
        });
    }

    function bindSelfApprovalWarning() {
        const form = document.querySelector('form[data-self-review="true"]');
        const modalElement = document.getElementById('selfApprovalConfirmModal');
        const confirmButton = document.getElementById('confirmSelfApprovalButton');
        if (!form || !modalElement || !confirmButton || !window.bootstrap) return;

        const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
        form.addEventListener('submit', function (event) {
            if (event.defaultPrevented) return;
            if (form.dataset.selfReviewConfirmed === '1') {
                delete form.dataset.selfReviewConfirmed;
                return;
            }

            event.preventDefault();
            modal.show();
        });

        confirmButton.addEventListener('click', function () {
            form.dataset.selfReviewConfirmed = '1';
            modal.hide();
            form.requestSubmit();
        });

        modalElement.addEventListener('shown.bs.modal', function () {
            confirmButton.focus();
        });
    }

    function initApprovalSupplier() {
        const page = document.getElementById('purchaseOrderDetailsPage');
        const form = document.getElementById('purchaseOrderApproveForm');
        const select = document.getElementById('approvalSupplierSelect');
        if (!page || !form || !select) return;

        if (window.jQuery && jQuery.fn?.select2) {
            jQuery(select).select2({
                theme: 'bootstrap-5',
                width: '100%',
                placeholder: 'Tìm tên, mã, SĐT hoặc mã số thuế...',
                minimumInputLength: 1,
                ajax: {
                    url: page.dataset.supplierLookupUrl,
                    dataType: 'json',
                    delay: 250,
                    cache: true,
                    data: function (params) {
                        return { term: params.term || '', page: params.page || 1 };
                    },
                    processResults: function (data) {
                        return {
                            results: data.results || [],
                            pagination: data.pagination || { more: false }
                        };
                    }
                },
                language: {
                    inputTooShort: function () { return 'Nhập ít nhất 1 ký tự để tìm nhà cung cấp'; },
                    searching: function () { return 'Đang tìm...'; },
                    noResults: function () { return 'Không tìm thấy nhà cung cấp phù hợp'; },
                    errorLoading: function () { return 'Không tải được danh sách nhà cung cấp'; }
                }
            });
        }

        form.addEventListener('submit', function (event) {
            if (select.value) return;
            event.preventDefault();
            select.nextElementSibling?.classList.add('is-invalid');
            if (window.jQuery && jQuery.fn?.select2) jQuery(select).select2('open');
            else select.focus();
        });

        if (window.jQuery) {
            jQuery(select).on('select2:select', function () {
                select.nextElementSibling?.classList.remove('is-invalid');
            });
        }
    }

    function initResolutionLookups() {
        const card = document.getElementById('catalogResolutionCard');
        if (!card || !window.jQuery || !jQuery.fn.select2) return;
        const lookupUrl = card.dataset.productLookupUrl;

        jQuery('.po-resolution-product').each(function () {
            const select = jQuery(this);
            const row = this.closest('.po-resolution-row');
            const form = this.closest('.po-resolution-form');
            const suggestedName = row?.dataset.productName || '';

            select.select2({
                theme: 'bootstrap-5',
                width: '100%',
                placeholder: 'Gõ tên, SKU hoặc quét barcode để ghép...',
                minimumInputLength: 1,
                ajax: {
                    url: lookupUrl,
                    dataType: 'json',
                    delay: 220,
                    data: function (params) {
                        return { term: params.term || '', page: params.page || 1 };
                    },
                    processResults: function (data) {
                        return {
                            results: data.results || [],
                            pagination: data.pagination || { more: false }
                        };
                    }
                },
                templateResult: formatCatalogResult,
                templateSelection: function (item) {
                    return item.productName
                        ? `${item.productName} · ${item.unitName || ''}`
                        : item.text || '';
                },
                language: {
                    inputTooShort: function () { return 'Nhập tên, SKU hoặc barcode'; },
                    searching: function () { return 'Đang tìm trong danh mục...'; },
                    noResults: function () { return 'Không tìm thấy. Có thể bấm “Hàng mới”.'; }
                },
                escapeMarkup: function (markup) { return markup; }
            });

            select.on('select2:select', function (event) {
                const item = event.params.data || {};
                const hidden = form?.querySelector('.po-resolution-variant-id');
                if (hidden) hidden.value = item.productVariantId || '';
            });

            form?.addEventListener('submit', function (event) {
                const hidden = form.querySelector('.po-resolution-variant-id');
                if (!select.val() || !hidden?.value) {
                    event.preventDefault();
                    select.select2('open');
                }
            });

            // Mở lookup với từ khóa gần tên nhân viên gõ, không tải cả catalog.
            row?.querySelector('.po-resolution-form')?.setAttribute('data-suggested-term', suggestedName);
        });
    }

    function formatCatalogResult(item) {
        if (!item.id) return escapeHtml(item.text || '');
        const supplier = item.supplierName
            ? `<span class="ms-2 ${item.supplierMatch ? 'text-success' : 'text-warning'}">NCC: ${escapeHtml(item.supplierName)}</span>`
            : '';
        return `<div class="py-1">
            <div class="fw-semibold">${escapeHtml(item.productName || item.text || '')}</div>
            <div class="small text-muted">SKU: ${escapeHtml(item.sku || '-')} · ${escapeHtml(item.unitName || '-')} × ${escapeHtml(item.factor || 1)}${supplier}</div>
        </div>`;
    }

    function bindQuickCreateProduct() {
        const modalElement = document.getElementById('poQuickCreateProductModal');
        const form = document.getElementById('poQuickCreateProductForm');
        if (!modalElement || !form || !window.bootstrap) return;

        const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
        const nameInput = document.getElementById('poQuickProductName');
        const unitSelect = document.getElementById('poQuickUnitId');
        const otherWrap = document.getElementById('poQuickNewUnitWrap');
        const otherInput = document.getElementById('poQuickNewUnitName');

        function openForRow(row) {
            if (!row) return;
            const lineId = row.dataset.lineId;
            form.action = `${form.dataset.actionPrefix}${lineId}/quick-create`;
            if (nameInput) nameInput.value = row.dataset.productName || '';

            const requestedUnit = normalize(row.dataset.unitName);
            if (unitSelect) {
                let matched = false;
                Array.from(unitSelect.options).forEach(function (option) {
                    if (option.value && option.value !== '__other' && normalize(option.text) === requestedUnit) {
                        unitSelect.value = option.value;
                        matched = true;
                    }
                });
                if (!matched && unitSelect.querySelector('option[value="__other"]')) {
                    unitSelect.value = '__other';
                    if (otherInput) otherInput.value = row.dataset.unitName || '';
                } else if (!matched) {
                    unitSelect.value = '';
                }
                updateOtherUnitState();
            }
            modal.show();
            setTimeout(function () { nameInput?.focus(); nameInput?.select(); }, 250);
        }

        document.querySelectorAll('.btn-po-quick-create').forEach(function (button) {
            button.addEventListener('click', function () {
                openForRow(button.closest('.po-resolution-row'));
            });
        });

        function updateOtherUnitState() {
            const isOther = unitSelect?.value === '__other';
            otherWrap?.classList.toggle('d-none', !isOther);
            if (otherInput) {
                otherInput.disabled = !isOther;
                otherInput.required = !!isOther;
            }
        }

        unitSelect?.addEventListener('change', updateOtherUnitState);
        form.addEventListener('submit', function () {
            if (unitSelect?.value === '__other') unitSelect.disabled = true;
        });
        modalElement.addEventListener('hidden.bs.modal', function () {
            if (unitSelect) unitSelect.disabled = false;
        });
        modalElement.addEventListener('keydown', function (event) {
            if (event.ctrlKey && event.key === 'Enter') {
                event.preventDefault();
                form.requestSubmit();
            }
        });
        document.addEventListener('keydown', function (event) {
            if (event.key !== 'F4' || modalElement.classList.contains('show')) return;
            const first = document.querySelector('.po-resolution-row');
            if (!first) return;
            event.preventDefault();
            openForRow(first);
        });
        updateOtherUnitState();
    }

    function bindPageShortcuts() {
        const helpModalElement = document.getElementById('purchaseOrderHelpModal');

        document.addEventListener('keydown', function (event) {
            if (event.key === 'F1') {
                event.preventDefault();
                if (helpModalElement && window.bootstrap) {
                    bootstrap.Modal.getOrCreateInstance(helpModalElement).show();
                }
                return;
            }

            if (!event.ctrlKey || event.key !== 'Enter') return;
            if (document.querySelector('.modal.show')) return;

            const primaryForm = document.querySelector('[data-pod-primary-form]');
            if (!primaryForm) return;
            event.preventDefault();
            primaryForm.requestSubmit();
        });
    }

    function normalize(value) {
        return String(value || '').trim().toLocaleLowerCase('vi-VN');
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }
})();
