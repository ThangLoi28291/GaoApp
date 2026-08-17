(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        bindReceiptRows();
        bindSelfApprovalWarning();
        initResolutionLookups();
        bindQuickCreateProduct();
    });

    function bindReceiptRows() {
        document.querySelectorAll('.receipt-row').forEach(function (row) {
            const checkbox = row.querySelector('.receive-enabled');
            const unitSelect = row.querySelector('.receipt-unit');
            const quantityInput = row.querySelector('.receipt-quantity');
            const conversionText = row.querySelector('.receipt-conversion');
            if (!checkbox) return;
            if (checkbox.dataset.bound === '1') return;
            checkbox.dataset.bound = '1';
            checkbox.addEventListener('change', function () {
                row.querySelectorAll('.receipt-input').forEach(function (input) {
                    input.disabled = !checkbox.checked;
                });
            });

            function updateConversion(resetToMaximum) {
                const option = unitSelect?.selectedOptions[0];
                const factor = Number(option?.dataset.factor || 0);
                const maximum = Number(option?.dataset.max || 0);
                if (quantityInput && Number.isFinite(maximum)) {
                    quantityInput.max = maximum.toFixed(3);
                    const current = Number(quantityInput.value || 0);
                    if (resetToMaximum || !Number.isFinite(current) || current <= 0 || current > maximum) {
                        quantityInput.value = maximum > 0 ? maximum.toFixed(3) : '';
                    }
                }
                const quantity = Number(quantityInput?.value || 0);
                const canonical = quantity * factor;
                if (conversionText) {
                    conversionText.textContent = Number.isFinite(canonical) && canonical > 0
                        ? `Tương đương: ${canonical.toFixed(3)} ${row.dataset.baseUnit || 'đơn vị gốc'}`
                        : 'Tương đương: —';
                }
            }

            unitSelect?.addEventListener('change', function () { updateConversion(true); });
            quantityInput?.addEventListener('input', function () { updateConversion(false); });
            updateConversion(false);
        });
    }

    function bindSelfApprovalWarning() {
        document.querySelectorAll('form[data-self-review="true"]').forEach(function (form) {
            if (form.dataset.confirmBound === '1') return;
            form.dataset.confirmBound = '1';
            form.addEventListener('submit', function (event) {
                if (!window.confirm(
                    'Bạn là người lập và cũng đang duyệt đơn này. Thao tác sẽ được ghi rõ trong lịch sử. Bạn có chắc muốn tiếp tục?')) {
                    event.preventDefault();
                }
            });
        });
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
