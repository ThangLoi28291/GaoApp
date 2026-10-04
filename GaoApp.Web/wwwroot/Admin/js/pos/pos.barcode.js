/* =========================================================
   FILE: pos.barcode.js
   Mục đích:
   - Barcode + autocomplete
   - Bước 2A:
     + debounce search
     + cache search
     + abort request cũ
     + request seq guard
     + loading nhỏ cho autocomplete
     + chống double submit scan/add
   - BƯỚC 5A.5:
     + chuẩn hóa action strategy cho barcode
     + draft-local action => applyDraftActionSuccess(...)

   NÂNG CẤP HIỆN TẠI:
   - Chuẩn hóa autocomplete parent/child
   - ArrowRight expand parent
   - ArrowLeft:
       + nếu đang ở child => quay về parent
       + nếu đang ở parent expanded => collapse parent
   - Enter/click chọn đúng parent hoặc child
   - Dùng flatItems làm single source of truth cho autocomplete UI
========================================================= */

window.PosBarcode = (function () {
    'use strict';

    function create(deps) {
        const {
            posState,
            barcodeSearchState,
            elements,
            helpers
        } = deps;

        const {
            txtBarcode,
            btnScan,
            btnFocusBarcode,
            barcodeAutocomplete
        } = elements;

        const {
            formatMoney,
            escapeHtml,
            fetchJson,
            postJson,
            runPosAction,
            focusBarcodeInput,
            syncDraftToUi,
            showSuccess,
            showError,

            // BƯỚC 5A.5
            applyDraftActionSuccess,
            applyScreenActionSuccess
        } = helpers;

        const {
            debounce,
            createTtlCache,
            createActionLocker,
            isAbortError,
            registerUiLock,
            isPosOnline
        } = window.PosCommon;

        const {
            renderBarcodeAutocompleteLoading,
            renderBarcodeAutocompleteEmpty
        } = window.PosRender;

        const debouncedSearch = debounce(function (keyword) {
            searchBarcodeAutocomplete(keyword);
        }, 300);

        barcodeSearchState.cache = barcodeSearchState.cache || createTtlCache(60000, 100);
        barcodeSearchState.parentItems = barcodeSearchState.parentItems || [];
        barcodeSearchState.flatItems = barcodeSearchState.flatItems || [];
        barcodeSearchState.expandedVariantIds = barcodeSearchState.expandedVariantIds || new Set();
        barcodeSearchState.items = barcodeSearchState.items || []; // giữ tương thích code cũ
        barcodeSearchState.activeIndex = Number.isInteger(barcodeSearchState.activeIndex)
            ? barcodeSearchState.activeIndex
            : -1;

        barcodeSearchState.requestedQty = Number.isFinite(Number(barcodeSearchState.requestedQty))
            && Number(barcodeSearchState.requestedQty) > 0
            ? Number(barcodeSearchState.requestedQty)
            : 1;

        barcodeSearchState.hasQtyPrefix = barcodeSearchState.hasQtyPrefix === true;

        const actionLocker = createActionLocker();
        let productEnterPending = false;
        let inputRevision = 0;
        barcodeSearchState.mode = 'product';
        barcodeSearchState.customerItems = [];
        barcodeSearchState.voucherItems = [];

        function parseQtyPrefixedInput(rawValue) {
            const input = String(rawValue || '').trim();

            if (!input) {
                return {
                    raw: '',
                    keyword: '',
                    qty: 1,
                    hasQtyPrefix: false
                };
            }

            // Hỗ trợ:
            // 4+sua
            // 4 + sua
            // 4+ 200010...
            // 4+
            const match = input.match(/^(\d+)\s*\+\s*(.*)$/);

            if (!match) {
                return {
                    raw: input,
                    keyword: input,
                    qty: 1,
                    hasQtyPrefix: false
                };
            }

            const parsedQty = parseInt(match[1], 10);
            const keyword = (match[2] || '').trim();

            if (!Number.isFinite(parsedQty) || parsedQty <= 0) {
                return {
                    raw: input,
                    keyword: input,
                    qty: 1,
                    hasQtyPrefix: false
                };
            }

            return {
                raw: input,
                keyword: keyword,
                qty: parsedQty,
                hasQtyPrefix: true
            };
        }
        function ensureQtyPrefixBadge() {
            if (!txtBarcode) return null;

            let badge = document.getElementById('posQtyPrefixBadge');
            if (badge) return badge;

            badge = document.createElement('div');
            badge.id = 'posQtyPrefixBadge';
            badge.className = 'pos-qty-prefix-badge';
            badge.style.display = 'none';

            txtBarcode.parentElement?.appendChild(badge);
            txtBarcode.parentElement?.classList.add('pos-barcode-wrap-with-qty');

            return badge;
        }

        function showQtyPrefixBadge(qty) {
            const badge = ensureQtyPrefixBadge();
            if (!badge) return;

            const safeQty = Number(qty || 1);
            if (safeQty <= 1) {
                hideQtyPrefixBadge();
                return;
            }

            badge.textContent = `SL: ${safeQty}`;
            badge.style.display = 'flex';
        }

        function hideQtyPrefixBadge() {
            const badge = document.getElementById('posQtyPrefixBadge');
            if (badge) badge.style.display = 'none';
        }

        function applyQtyPrefixTyping(rawValue) {
            const raw = String(rawValue || '');
            const match = raw.match(/^(\d+)\s*\+\s*$/);

            if (!match) return false;

            const qty = parseInt(match[1], 10);
            if (!Number.isFinite(qty) || qty <= 0) return false;

            barcodeSearchState.requestedQty = qty;
            barcodeSearchState.hasQtyPrefix = true;

            txtBarcode.value = '';
            showQtyPrefixBadge(qty);
            hideBarcodeAutocomplete();

            return true;
        }

        function getCurrentRequestedQty() {
            const parsed = parseQtyPrefixedInput(txtBarcode?.value || '');

            if (parsed.hasQtyPrefix) {
                return parsed.qty || 1;
            }

            const stateQty = Number(barcodeSearchState.requestedQty || 1);
            return stateQty > 0 ? stateQty : 1;
        }

        function resetRequestedQty() {
            barcodeSearchState.requestedQty = 1;
            barcodeSearchState.hasQtyPrefix = false;
            hideQtyPrefixBadge();
        }

        function highlightMatch(text, keyword) {
            const safeText = escapeHtml(text || '');
            const rawKeyword = (keyword || '').trim();

            if (!rawKeyword) return safeText;

            const escapedKeyword = rawKeyword.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
            const regex = new RegExp(`(${escapedKeyword})`, 'ig');

            return safeText.replace(regex, '<mark>$1</mark>');
        }

        function formatAutocompleteQty(value) {
            const num = Number(value ?? 0);
            if (!Number.isFinite(num) || num <= 0) return '0';
            return String(Math.floor(num));
        }

        function showAutocompleteImagePreview(url, alt, anchorEl) {
            if (!url || !anchorEl) return;

            const previewEl = document.getElementById('cartImageHoverPreview');
            const imgEl = document.getElementById('cartImageHoverPreviewImg');
            if (!previewEl || !imgEl) return;

            imgEl.src = url;
            imgEl.alt = alt || 'Ảnh sản phẩm';

            const rect = anchorEl.getBoundingClientRect();

            let left = rect.right + 10;
            let top = rect.top;

            if (left + 260 > window.innerWidth) {
                left = rect.left - 260 - 10;
            }

            previewEl.style.left = left + 'px';
            previewEl.style.top = top + 'px';
            previewEl.style.display = 'block';
        }

        function hideAutocompleteImagePreview() {
            const el = document.getElementById('cartImageHoverPreview');
            if (el) el.style.display = 'none';
        }
        function buildBadgeHtml(options) {
            const badges = [];

            if (options?.isDefaultSaleUnit) {
                badges.push('<span class="pos-ac-badge pos-ac-badge-default">Mặc định</span>');
            }

            if (options?.isChildUnit) {
                badges.push('<span class="pos-ac-badge pos-ac-badge-child">Đơn vị con</span>');
            }

            if (options?.isNegativeStock) {
                badges.push('<span class="pos-ac-badge pos-ac-badge-negative"> Âm kho</span>');
            }

            return badges.join('');
        }

        function buildMetaChip(label, value, extraClass) {
            const css = extraClass ? `pos-ac-meta-chip ${extraClass}` : 'pos-ac-meta-chip';
            return `
                <span class="${css}">
                    <span class="pos-ac-meta-label">${escapeHtml(label)}:</span>
                    <strong>${escapeHtml(value || '-')}</strong>
                </span>
            `;
        }

        function buildRightSummary(label, value, valueClass) {
            return `
                <div class="pos-ac-summary-item">
                    <span class="pos-ac-summary-label">${escapeHtml(label)}</span>
                    <span class="pos-ac-summary-value ${valueClass || ''}">${escapeHtml(value)}</span>
                </div>
            `;
        }

        function nextRequestSeq() {
            barcodeSearchState.requestSeq = Number(barcodeSearchState.requestSeq || 0) + 1;
            return barcodeSearchState.requestSeq;
        }

        function setLoading(isLoading) {
            barcodeSearchState.isLoading = !!isLoading;

            if (txtBarcode) {
                txtBarcode.dataset.loading = isLoading ? 'true' : 'false';
            }
        }

        function setSubmitting(isSubmitting) {
            barcodeSearchState.isSubmitting = !!isSubmitting;

            if (btnScan) {
                btnScan.disabled = !!isSubmitting;
                btnScan.dataset.loading = isSubmitting ? 'true' : 'false';
            }

            if (txtBarcode) {
                txtBarcode.dataset.submitting = isSubmitting ? 'true' : 'false';
            }
        }

        function abortActiveSearch() {
            if (barcodeSearchState.abortController) {
                try {
                    barcodeSearchState.abortController.abort();
                } catch (_) {
                }
                barcodeSearchState.abortController = null;
            }
        }
      
        function resetAutocompleteState() {
            barcodeSearchState.items = [];
            barcodeSearchState.parentItems = [];
            barcodeSearchState.flatItems = [];
            barcodeSearchState.activeIndex = -1;
            barcodeSearchState.keyword = '';
            barcodeSearchState.expandedVariantIds = new Set();
            barcodeSearchState.requestedQty = 1;
            barcodeSearchState.hasQtyPrefix = false;
        }

        function hideBarcodeAutocomplete() {
            debouncedSearch.cancel?.();
            nextRequestSeq(); // Ignore replies already resolving when the search is dismissed.
            abortActiveSearch();
            setLoading(false);

            if (barcodeAutocomplete) {
                barcodeAutocomplete.style.display = 'none';
                barcodeAutocomplete.innerHTML = '';
            }

            barcodeSearchState.items = [];
            barcodeSearchState.parentItems = [];
            barcodeSearchState.flatItems = [];
            barcodeSearchState.activeIndex = -1;
            barcodeSearchState.keyword = '';
            barcodeSearchState.expandedVariantIds = new Set();
            barcodeSearchState.mode = 'product';
            barcodeSearchState.customerItems = [];
            barcodeSearchState.voucherItems = [];
        }
        function isCustomerCommand(rawValue) {
            return String(rawValue || '').trim().startsWith('@');
        }

        function getCustomerKeyword(rawValue) {
            return String(rawValue || '')
                .trim()
                .replace(/^@/, '')
                .trim();
        }
        function isVoucherCommand(rawValue) {
            return String(rawValue || '').trim().startsWith('#');
        }

        function getVoucherKeyword(rawValue) {
            return String(rawValue || '')
                .trim()
                .replace(/^#/, '')
                .trim();
        }

        function getCurrentDraft() {
            return posState?.business?.currentDraft || posState?.currentDraft || null;
        }

        function getCurrentCustomerId() {
            const draft = getCurrentDraft();

            return Number(
                draft?.customer?.customerId ||
                draft?.customerId ||
                0
            );
        }

        function getAppliedRewardVoucherIds() {
            const draft = getCurrentDraft();
            const applied = draft?.appliedRewardVouchers || draft?.AppliedRewardVouchers || [];

            return (Array.isArray(applied) ? applied : [])
                .map(x => Number(x.voucherId || x.VoucherId || 0))
                .filter(x => x > 0);
        }

        function setVoucherAutocompleteActive(index) {
            if (!barcodeAutocomplete) return;

            const rows = barcodeAutocomplete.querySelectorAll('[data-voucher-index]');
            rows.forEach(x => x.classList.remove('active'));

            if (!rows.length) {
                barcodeSearchState.activeIndex = -1;
                return;
            }

            if (index < 0) index = 0;
            if (index >= rows.length) index = rows.length - 1;

            barcodeSearchState.activeIndex = index;
            rows[index].classList.add('active');
            rows[index].scrollIntoView({ block: 'nearest' });
        }

        function getActiveVoucherItem() {
            const items = Array.isArray(barcodeSearchState.voucherItems)
                ? barcodeSearchState.voucherItems
                : [];

            const index = Number(barcodeSearchState.activeIndex || 0);
            if (index < 0 || index >= items.length) return null;

            return items[index];
        }

        function renderVoucherAutocomplete(items, keyword) {
            const list = Array.isArray(items) ? items : [];

            barcodeSearchState.mode = 'voucher';
            barcodeSearchState.voucherItems = list;
            barcodeSearchState.customerItems = [];
            barcodeSearchState.flatItems = [];
            barcodeSearchState.activeIndex = -1;

            if (!barcodeAutocomplete) return;

            if (!list.length) {
                barcodeAutocomplete.innerHTML = `
            <div class="pos-autocomplete-empty px-3 py-3 text-muted small">
                Không tìm thấy voucher:
                <strong>${escapeHtml(keyword || '')}</strong>
            </div>
        `;
                barcodeAutocomplete.style.display = '';
                return;
            }

            barcodeAutocomplete.innerHTML = list.map(function (x, index) {
                const voucherId = Number(x.id || x.Id || 0);
                const code = escapeHtml(x.voucherCode || x.VoucherCode || '');
                const value = Number(x.value || x.Value || 0);
                const desc = escapeHtml(x.description || x.Description || 'Voucher tích điểm');

                return `
            <button type="button"
                    class="pos-autocomplete-item pos-ac-item pos-ac-voucher-item"
                    data-voucher-index="${index}"
                    data-voucher-id="${voucherId}">
                <div class="pos-ac-row">
                    <div class="pos-ac-thumb-placeholder">
                        VC
                    </div>

                    <div class="pos-ac-main">
                        <div class="pos-ac-title-row">
                            <div class="pos-ac-title">${code}</div>
                            <div class="pos-ac-badges">
                                <span class="pos-ac-badge pos-ac-badge-default">Voucher</span>
                            </div>
                        </div>

                        <div class="pos-ac-meta">
                            <span class="pos-ac-meta-chip">
                                <span class="pos-ac-meta-label">Ghi chú:</span>
                                <strong>${desc}</strong>
                            </span>
                        </div>
                    </div>

                    <div class="pos-ac-side">
                        ${buildRightSummary('Giá trị', `${formatMoney(value)} đ`, 'is-price')}
                    </div>
                </div>
            </button>
        `;
            }).join('');

            barcodeAutocomplete.style.display = '';
            setVoucherAutocompleteActive(0);
        }

        async function searchVoucherFromBarcodeInput(rawValue) {
            const customerId = getCurrentCustomerId();
            const keyword = getVoucherKeyword(rawValue);

            barcodeSearchState.mode = 'voucher';

            if (!customerId) {
                barcodeSearchState.voucherItems = [];
                barcodeSearchState.activeIndex = -1;

                showError('Vui lòng chọn khách hàng trước khi dùng voucher.');
                hideBarcodeAutocomplete();
                return;
            }

            abortActiveSearch();

            const controller = new AbortController();
            barcodeSearchState.abortController = controller;

            const currentSeq = nextRequestSeq();
            setLoading(true);

            if (barcodeAutocomplete) {
                barcodeAutocomplete.innerHTML = `
            <div class="pos-autocomplete-loading px-3 py-2 text-muted small">
                <span class="spinner-border spinner-border-sm me-2"></span>
                Đang tìm voucher...
            </div>
        `;
                barcodeAutocomplete.style.display = '';
            }

            try {
                const items = await fetchJson(
                    `/admin/api/customers/${customerId}/reward-vouchers/available`,
                    {
                        method: 'GET',
                        signal: controller.signal
                    }
                );

                if (currentSeq !== barcodeSearchState.requestSeq) return;

                const rawItems = Array.isArray(items) ? items : [];
                const k = keyword.toLowerCase();

                const filtered = !k
                    ? rawItems
                    : rawItems.filter(function (x) {
                        const code = String(x.voucherCode || x.VoucherCode || '').toLowerCase();
                        const desc = String(x.description || x.Description || '').toLowerCase();
                        const value = String(x.value || x.Value || '').toLowerCase();

                        return code.includes(k) || desc.includes(k) || value.includes(k);
                    });

                renderVoucherAutocomplete(filtered, keyword);
            } catch (err) {
                if (isAbortError(err)) return;

                barcodeSearchState.voucherItems = [];
                barcodeSearchState.activeIndex = -1;

                if (barcodeAutocomplete) {
                    barcodeAutocomplete.innerHTML = `
                <div class="pos-autocomplete-empty px-3 py-3 text-danger small">
                    Không thể tải voucher của khách.
                </div>
            `;
                    barcodeAutocomplete.style.display = '';
                }
            } finally {
                if (barcodeSearchState.abortController === controller) {
                    barcodeSearchState.abortController = null;
                }

                if (currentSeq === barcodeSearchState.requestSeq) {
                    setLoading(false);
                }
            }
        }
        async function lookupAndApplyVoucherByCode(rawValue) {
            const customerId = getCurrentCustomerId();
            const keyword = getVoucherKeyword(rawValue);

            if (!customerId) {
                showError('Vui lòng chọn khách hàng trước khi dùng voucher.');
                hideBarcodeAutocomplete();
                return;
            }

            if (!keyword) {
                await searchVoucherFromBarcodeInput(rawValue);
                return;
            }

            try {
                const voucher = await fetchJson(
                    `/admin/api/customers/reward-vouchers/lookup?code=${encodeURIComponent(keyword)}`
                );

                const voucherCustomerId = Number(voucher.customerId || voucher.CustomerId || 0);

                if (voucherCustomerId !== customerId) {
                    showError('Voucher này không thuộc khách hàng hiện tại.');
                    hideBarcodeAutocomplete();
                    return;
                }

                const status = voucher.status || voucher.Status || '';

                if (status !== 'Available') {
                    const message =
                        status === 'Used' ? 'Voucher đã được sử dụng.' :
                            status === 'Cancelled' ? 'Voucher đã bị hủy.' :
                                status === 'Expired' ? 'Voucher đã hết hạn.' :
                                    status === 'Locked' ? 'Voucher đang bị khóa.' :
                                        'Voucher không còn khả dụng.';

                    showError(message);
                    hideBarcodeAutocomplete();
                    return;
                }

                await selectVoucherFromAutocomplete(voucher);
            } catch (err) {
                const items = barcodeSearchState.voucherItems || [];

                if (items.length === 1) {
                    await selectVoucherFromAutocomplete(items[0]);
                    return;
                }

                showError(err?.message || 'Không tìm thấy voucher.');
                hideBarcodeAutocomplete();
            }
        }

        async function selectVoucherFromAutocomplete(voucher) {
            const voucherId = Number(voucher?.id || voucher?.Id || 0);
            if (!voucherId) return;

            const currentAppliedIds = getAppliedRewardVoucherIds();
            const voucherIds = Array.from(new Set([...currentAppliedIds, voucherId]));

            if (txtBarcode) {
                txtBarcode.value = '';
            }

            resetRequestedQty();
            hideBarcodeAutocomplete();

            await runPosAction(
                posState,
                `reward-voucher:quick-apply:${voucherIds.join(',')}`,
                async function () {
                    return await postJson('/admin/pos/cart/current/reward-vouchers', {
                        voucherIds: voucherIds
                    });
                },
                {
                    fallbackMessage: 'Không thể áp dụng voucher.',
                    requireOnline: true,
                    scopes: ['cartMutate'],
                    blockedMessage: 'POS đang xử lý giỏ hàng, chưa thể áp dụng voucher.',
                    onSuccess: function (draft) {
                        applyDraftSuccess({
                            draft,
                            successMessage: 'Đã áp dụng voucher vào giỏ.',
                            focusBarcode: true,
                            afterSync: function () {
                                if (txtBarcode) {
                                    txtBarcode.value = '';
                                }
                                resetRequestedQty();
                                hideBarcodeAutocomplete();
                            }
                        });
                    }
                }
            );
        }

        function setCustomerAutocompleteActive(index) {
            if (!barcodeAutocomplete) return;

            const rows = barcodeAutocomplete.querySelectorAll('[data-customer-index]');
            rows.forEach(x => x.classList.remove('active'));

            if (!rows.length) {
                barcodeSearchState.activeIndex = -1;
                return;
            }

            if (index < 0) index = 0;
            if (index >= rows.length) index = rows.length - 1;

            barcodeSearchState.activeIndex = index;
            rows[index].classList.add('active');
            rows[index].scrollIntoView({ block: 'nearest' });
        }

        function getActiveCustomerItem() {
            const items = Array.isArray(barcodeSearchState.customerItems)
                ? barcodeSearchState.customerItems
                : [];

            const index = Number(barcodeSearchState.activeIndex || 0);
            if (index < 0 || index >= items.length) return null;

            return items[index];
        }

        function renderCustomerAutocomplete(items, keyword) {
            const list = Array.isArray(items) ? items : [];

            barcodeSearchState.mode = 'customer';
            barcodeSearchState.customerItems = list;
            barcodeSearchState.flatItems = [];
            barcodeSearchState.activeIndex = -1;

            if (!barcodeAutocomplete) return;

            if (!list.length) {
                barcodeAutocomplete.innerHTML = `
            <div class="pos-autocomplete-empty px-3 py-3 text-muted small">
                Không tìm thấy khách hàng:
                <strong>${escapeHtml(keyword || '')}</strong>
            </div>
        `;
                barcodeAutocomplete.style.display = '';
                return;
            }

            barcodeAutocomplete.innerHTML = list.map(function (x, index) {
                const customerId = Number(x.customerId || x.id || 0);
                const name = escapeHtml(x.name || 'Khách hàng');
                const phone = escapeHtml(x.phone || 'Chưa có SĐT');
                const address = escapeHtml(x.address || '');

                return `
            <button type="button"
                    class="pos-autocomplete-item pos-ac-item pos-ac-customer-item"
                    data-customer-index="${index}"
                    data-customer-id="${customerId}">
                <div class="pos-ac-row">
                    <div class="pos-ac-thumb-placeholder">
                        KH
                    </div>

                    <div class="pos-ac-main">
                        <div class="pos-ac-title-row">
                            <div class="pos-ac-title">${name}</div>
                            <div class="pos-ac-badges">
                                <span class="pos-ac-badge pos-ac-badge-default">Khách hàng</span>
                            </div>
                        </div>

                        <div class="pos-ac-meta">
                            <span class="pos-ac-meta-chip">
                                <span class="pos-ac-meta-label">SĐT:</span>
                                <strong>${phone}</strong>
                            </span>

                            ${address ? `
                                <span class="pos-ac-meta-chip">
                                    <span class="pos-ac-meta-label">Địa chỉ:</span>
                                    <strong>${address}</strong>
                                </span>
                            ` : ''}
                        </div>
                    </div>
                </div>
            </button>
        `;
            }).join('');

            barcodeAutocomplete.style.display = '';
            setCustomerAutocompleteActive(0);
        }

        async function searchCustomerFromBarcodeInput(rawValue) {
            const keyword = getCustomerKeyword(rawValue);
            if (keyword.endsWith('+')) {
                const createValue = keyword.slice(0, -1).trim();

                if (txtBarcode) {
                    txtBarcode.value = '';
                }

                hideBarcodeAutocomplete();

                window.posApp?.modules?.posCustomer?.openQuickCreateCustomerModal?.({
                    value: createValue
                });

                return;
            }
            barcodeSearchState.mode = 'customer';

            if (!keyword) {
                barcodeSearchState.customerItems = [];
                barcodeSearchState.activeIndex = -1;

                if (barcodeAutocomplete) {
                    barcodeAutocomplete.innerHTML = `
                <div class="pos-autocomplete-empty px-3 py-3 text-muted small">
                    Gõ SĐT hoặc tên khách sau dấu @
                </div>
            `;
                    barcodeAutocomplete.style.display = '';
                }

                return;
            }

            if (keyword.length < 2) {
                return;
            }

            abortActiveSearch();

            const controller = new AbortController();
            barcodeSearchState.abortController = controller;

            const currentSeq = nextRequestSeq();
            setLoading(true);

            if (barcodeAutocomplete) {
                barcodeAutocomplete.innerHTML = `
            <div class="pos-autocomplete-loading px-3 py-2 text-muted small">
                <span class="spinner-border spinner-border-sm me-2"></span>
                Đang tìm khách: <strong>${escapeHtml(keyword)}</strong>
            </div>
        `;
                barcodeAutocomplete.style.display = '';
            }

            try {
                const items = await fetchJson(
                    `/admin/pos/customers/search?keyword=${encodeURIComponent(keyword)}&take=10`,
                    {
                        method: 'GET',
                        signal: controller.signal
                    }
                );

                if (currentSeq !== barcodeSearchState.requestSeq) return;

                renderCustomerAutocomplete(items || [], keyword);
            } catch (err) {
                if (isAbortError(err)) return;

                barcodeSearchState.customerItems = [];
                barcodeSearchState.activeIndex = -1;

                if (barcodeAutocomplete) {
                    barcodeAutocomplete.innerHTML = `
                <div class="pos-autocomplete-empty px-3 py-3 text-danger small">
                    Không thể tìm khách hàng.
                </div>
            `;
                    barcodeAutocomplete.style.display = '';
                }
            } finally {
                if (barcodeSearchState.abortController === controller) {
                    barcodeSearchState.abortController = null;
                }

                if (currentSeq === barcodeSearchState.requestSeq) {
                    setLoading(false);
                }
            }
        }

        async function selectCustomerFromAutocomplete(customer) {
            const customerId = Number(customer?.customerId || customer?.id || 0);
            if (!customerId) return;

            if (txtBarcode) {
                txtBarcode.value = '';
            }

            resetRequestedQty();
            hideBarcodeAutocomplete();

            await window.posApp?.modules?.posCustomer?.setCustomerToCurrentCart?.(customerId);
        }

        function setAutocompleteActive(index) {
            if (!barcodeAutocomplete) {
                barcodeSearchState.activeIndex = -1;
                return;
            }

            const rows = barcodeAutocomplete.querySelectorAll('[data-autocomplete-index]');
            rows.forEach(x => x.classList.remove('active'));

            if (!rows.length) {
                barcodeSearchState.activeIndex = -1;
                return;
            }

            if (index < 0) index = 0;
            if (index >= rows.length) index = rows.length - 1;

            barcodeSearchState.activeIndex = index;
            rows[index].classList.add('active');
            rows[index].scrollIntoView({ block: 'nearest' });
        }

        function getActiveAutocompleteRow() {
            if (!barcodeAutocomplete) return null;

            const rows = barcodeAutocomplete.querySelectorAll('[data-autocomplete-index]');
            if (!rows.length) return null;

            const index = barcodeSearchState.activeIndex;
            if (index < 0 || index >= rows.length) return null;

            return rows[index];
        }

        function getActiveFlatItem() {
            const flatItems = Array.isArray(barcodeSearchState.flatItems)
                ? barcodeSearchState.flatItems
                : [];

            const index = Number(barcodeSearchState.activeIndex || 0);
            if (index < 0 || index >= flatItems.length) return null;

            return flatItems[index] || null;
        }

        function getCurrentProductSelection() {
            const keyword = parseQtyPrefixedInput(txtBarcode?.value || '').keyword;
            if (!keyword || keyword !== barcodeSearchState.keyword || barcodeSearchState.isLoading
                || !barcodeAutocomplete || barcodeAutocomplete.style.display === 'none') return null;

            // Enter follows the highlighted row, including the automatically selected first result.
            const row = barcodeAutocomplete.querySelector('[data-autocomplete-index].active')
                || barcodeAutocomplete.querySelector('[data-autocomplete-index]');
            if (!row) return null;
            const itemKey = row.getAttribute('data-item-key');
            return (barcodeSearchState.flatItems || []).find(item => item.key === itemKey) || null;
        }

        async function confirmProductSearch() {
            if (barcodeSearchState.isSubmitting || productEnterPending) return;
            const rawInput = txtBarcode?.value || '';
            const keyword = parseQtyPrefixedInput(rawInput).keyword;
            const numericInput = /^\d+$/.test(keyword);
            productEnterPending = true;
            try {
                let selected = getCurrentProductSelection();
                if (!selected && !numericInput && keyword.length >= 2) {
                    // Finish a pending name search instead of submitting the name as a barcode.
                    debouncedSearch.cancel?.();
                    await searchBarcodeAutocomplete(rawInput);
                    if (txtBarcode?.value !== rawInput || barcodeSearchState.keyword !== keyword) return;
                    selected = getCurrentProductSelection();
                }

                // Numeric scans resolve the exact barcode, never a fuzzy first suggestion.
                // An explicitly highlighted unit row still supports keyboard selection.
                if (selected && (!numericInput || selected.kind === 'child')) {
                    await selectAutocompleteItem(selected);
                    return;
                }
                await scanCurrentCart();
            } finally {
                productEnterPending = false;
            }
        }

        function buildParentItem(apiItem) {
            const unitOptions = Array.isArray(apiItem?.unitOptions) ? apiItem.unitOptions : [];
            const defaultConversionId = Number(
                apiItem.productUnitConversionId ??
                apiItem.ProductUnitConversionId ??
                0
            );

            const childItems = unitOptions
                .filter(x => Number(x.factor || 0) > 1)
                .map(function (x, childIndex) {
                    const factor = Number(x.factor || 0);

                    const fullName = window.PosRender.buildVariantDisplayName(
                        apiItem.productName || '',
                        apiItem.productVariantName || apiItem.displayName || ''
                    );

                    return {
                        kind: 'child',
                        key: `variant-${apiItem.variantId || 0}-conv-${x.productUnitConversionId || x.ProductUnitConversionId || 0}-${childIndex}`,
                        parentVariantId: apiItem.variantId || 0,
                        variantId: apiItem.variantId || 0,
                        productUnitConversionId: Number(
                            x.productUnitConversionId ??
                            x.ProductUnitConversionId ??
                            0
                        ),
                        unitId: x.unitId || x.UnitId || 0,
                        unitName: (x.unitName || x.UnitName || '').trim(),
                        factor: factor,
                        price: x.price ?? x.Price ?? 0,
                        barcode: x.barcode || x.Barcode || '',
                        availableQty: Number(x.availableQty ?? x.AvailableQty ?? 0),
                        isNegativeStock: x.isNegativeStock === true || x.IsNegativeStock === true,
                        productName: apiItem.productName || '',
                        productVariantName: apiItem.productVariantName || apiItem.displayName || '',
                       
                        displayName: fullName,
                        conversionText: factor > 0
                            ? `1 ${x.unitName || x.UnitName || ''} = ${factor} đơn vị cơ bản`
                            : '',
                        isChildUnit: true,
                          // NEW
    imageUrl: apiItem.imageUrl || '',
    imageThumbUrl: apiItem.imageThumbUrl || '',
    imageAlt: apiItem.imageAlt || fullName || '',
    hasImage: apiItem.hasImage === true,

                        raw: x
                    };
                });

            return {
                kind: 'parent',
                key: `variant-${apiItem.variantId || 0}`,
                variantId: apiItem.variantId || 0,
                productId: apiItem.productId || 0,
                productName: apiItem.productName || '',
                productVariantName: apiItem.productVariantName || apiItem.displayName || '',
                imageUrl: apiItem.imageUrl || '',
                imageThumbUrl: apiItem.imageThumbUrl || '',
                imageAlt: apiItem.imageAlt || apiItem.displayName || apiItem.productVariantName || apiItem.productName || '',
                hasImage: apiItem.hasImage === true,
                displayName: apiItem.displayName || '',
                barcode: apiItem.barcode || '',
                price: apiItem.price || 0,
                onHandQty: apiItem.onHandQty ?? apiItem.stockQty ?? apiItem.availableQty ?? 0,
                availableQty: Number(apiItem.availableQty ?? apiItem.onHandQty ?? apiItem.stockQty ?? 0),
                isNegativeStock: apiItem.isNegativeStock === true || apiItem.IsNegativeStock === true,
                isActive: apiItem.isActive === true,
                isDefaultSaleUnit: defaultConversionId > 0 || childItems.length > 0,
                unitOptions: unitOptions,
                children: childItems,
                hasChildren: childItems.length > 0,
                raw: apiItem
            };
        }

        function normalizeAutocompleteItems(items) {
            const parents = (items || []).map(buildParentItem);
            barcodeSearchState.parentItems = parents;
            barcodeSearchState.items = parents; // giữ tương thích code cũ
            return parents;
        }

        function buildFlatItems(parentItems) {
            const result = [];
            const expandedVariantIds = barcodeSearchState.expandedVariantIds || new Set();

            (parentItems || []).forEach(function (parent) {
                result.push(parent);

                if (
                    parent?.hasChildren &&
                    expandedVariantIds.has(Number(parent.variantId || 0))
                ) {
                    (parent.children || []).forEach(function (child) {
                        result.push(child);
                    });
                }
            });

            barcodeSearchState.flatItems = result;
            return result;
        }

        function findFlatIndexByKey(itemKey) {
            const flatItems = barcodeSearchState.flatItems || [];
            return flatItems.findIndex(function (x) {
                return x && x.key === itemKey;
            });
        }

        function findParentFlatIndex(variantId) {
            const flatItems = barcodeSearchState.flatItems || [];
            return flatItems.findIndex(function (x) {
                return x && x.kind === 'parent' && Number(x.variantId || 0) === Number(variantId || 0);
            });
        }

        function renderUnitChildItem(child, keyword) {
            const factorText = Number(child.factor || 0) > 0
                ? `x${escapeHtml(String(child.factor))}`
                : '';

            const childTitle = `${child.displayName} ${child.unitName || ''} ${factorText}`.trim();
            const titleHtml = highlightMatch(childTitle, keyword);
            const barcodeText = child.barcode ? child.barcode : '-';
            const priceText = `${formatMoney(child.price || 0)} đ`;

            const convertedAvailableQty = Number(child.availableQty ?? 0);
            const isNegativeStock = child.isNegativeStock === true;
            const stockDisplayText = isNegativeStock
                ? '0'
                : formatAutocompleteQty(convertedAvailableQty);

            const badgeHtml = buildBadgeHtml({
                isChildUnit: true,
                isNegativeStock: isNegativeStock
            });

            const conversionText = child.conversionText || `Quy đổi: ${child.factor || 0}`;

            return `
                <button type="button"
                        class="pos-autocomplete-item pos-ac-item pos-ac-child-item ${isNegativeStock ? 'stock-negative' : ''}"
                        data-autocomplete-kind="child"
                        data-item-key="${escapeHtml(child.key)}"
                        data-variant-id="${child.variantId || 0}"
                        data-unit-id="${child.unitId || 0}"
                        data-product-unit-conversion-id="${child.productUnitConversionId || 0}">
                    <span class="pos-ac-child-rail"></span>

               <div class="pos-ac-row">

  ${
                child?.hasImage && (child?.imageThumbUrl || child?.imageUrl)
                    ? `
        <div class="pos-ac-thumb-wrap"
             data-image-url="${escapeHtml(child.imageUrl || child.imageThumbUrl || '')}">
            <img class="pos-ac-thumb"
                 src="${escapeHtml(child.imageThumbUrl || child.imageUrl || '')}"
                 alt="${escapeHtml(child.imageAlt || child.displayName || '')}"
                 loading="lazy" />
        </div>
        `
                    : `
        <div class="pos-ac-thumb-placeholder">IMG</div>
        `
}
                        <div class="pos-ac-main">
                            <div class="pos-ac-title-row">
                                <div class="pos-ac-title pos-ac-title-child">${titleHtml}</div>
                                <div class="pos-ac-badges">${badgeHtml}</div>
                            </div>

                            <div class="pos-ac-meta">
                                ${buildMetaChip('Mã', barcodeText)}
                                ${buildMetaChip('Quy đổi', conversionText)}
                            </div>
                        </div>

                        <div class="pos-ac-side">
                            ${buildRightSummary('Tồn', stockDisplayText, isNegativeStock ? 'is-negative' : '')}
                            ${buildRightSummary('Giá', priceText, 'is-price')}
                        </div>
                    </div>
                </button>
            `;
        }

        function renderParentItem(parent, keyword) {
            const fullName = window.PosRender.buildVariantDisplayName(
                parent.productName || '',
                parent.productVariantName || parent.displayName || ''
            );

            const titleHtml = highlightMatch(fullName, keyword);
            const barcodeText = parent.barcode ? parent.barcode : '-';
            const priceText = `${formatMoney(parent.price || 0)} đ`;

            const onHandQty = Number(parent.availableQty ?? parent.onHandQty ?? 0);
            const isNegativeStock = parent.isNegativeStock === true;
            const stockDisplayText = isNegativeStock
                ? '0'
                : formatAutocompleteQty(onHandQty);

            const isExpanded = barcodeSearchState.expandedVariantIds.has(Number(parent.variantId || 0));

            const badgeHtml = buildBadgeHtml({
                isDefaultSaleUnit: true,
                isNegativeStock: isNegativeStock
            });

            return `
                <button type="button"
                        class="pos-autocomplete-item pos-ac-item pos-ac-parent-item ${isExpanded ? 'expanded' : ''} ${isNegativeStock ? 'stock-negative' : ''}"
                        data-autocomplete-kind="parent"
                        data-item-key="${escapeHtml(parent.key)}"
                        data-variant-id="${parent.variantId || 0}">
                 <div class="pos-ac-row">

    ${
                parent?.hasImage && (parent?.imageThumbUrl || parent?.imageUrl)
                    ? `
            <div class="pos-ac-thumb-wrap"
                 data-image-url="${escapeHtml(parent.imageUrl || parent.imageThumbUrl || '')}">
                <img class="pos-ac-thumb"
                     src="${escapeHtml(parent.imageThumbUrl || parent.imageUrl || '')}"
                     alt="${escapeHtml(parent.imageAlt || parent.displayName)}"
                     loading="lazy" />
            </div>
            `
                    : `
            <div class="pos-ac-thumb-placeholder">IMG</div>
            `
    }
                        <div class="pos-ac-main">
                            <div class="pos-ac-title-row">
                                <div class="pos-ac-title">${titleHtml}</div>
                                <div class="pos-ac-badges">${badgeHtml}</div>
                            </div>

                            <div class="pos-ac-meta">
                                ${buildMetaChip('Mã', barcodeText)}
                            </div>
                        </div>

                        <div class="pos-ac-side">
                            ${buildRightSummary('Tồn', stockDisplayText, isNegativeStock ? 'is-negative' : '')}
                            ${buildRightSummary('Giá', priceText, 'is-price')}
                        </div>

                        <div class="pos-ac-expand-wrap">
                          ${parent.hasChildren
                    ? `<button type="button"
               class="pos-ac-expand-btn"
               data-expand-toggle="true"
               data-item-key="${escapeHtml(parent.key)}"
               tabindex="-1"
               aria-label="${isExpanded ? 'Thu gọn' : 'Mở rộng'}">
            <span class="pos-ac-expand-hint" aria-hidden="true">${isExpanded ? '▾' : '▸'}</span>
       </button>`
                    : `<span class="pos-ac-expand-hint is-empty" aria-hidden="true"></span>`}
                        </div>
                    </div>
                </button>
            `;
        }

        function renderBarcodeAutocomplete(items) {
            const parentItems = Array.isArray(items) ? items : [];
            const flatItems = buildFlatItems(parentItems);

            barcodeSearchState.activeIndex = -1;

            if (!flatItems.length) {
                hideBarcodeAutocomplete();
                return;
            }

            const keyword = barcodeSearchState.keyword || '';
            const htmlParts = [];

            flatItems.forEach(function (item) {
                if (item.kind === 'child') {
                    htmlParts.push(renderUnitChildItem(item, keyword));
                    return;
                }

                htmlParts.push(renderParentItem(item, keyword));
            });

            barcodeAutocomplete.innerHTML = htmlParts.join('');

            const rows = barcodeAutocomplete.querySelectorAll('[data-autocomplete-kind]');
            rows.forEach((row, index) => {
                row.setAttribute('data-autocomplete-index', index);
            });

            barcodeAutocomplete.style.display = '';
            setAutocompleteActive(0);
        }

        function rerenderAutocompleteKeepActive(itemKeyToKeep) {
            renderBarcodeAutocomplete(barcodeSearchState.parentItems || []);

            if (itemKeyToKeep) {
                const newIndex = findFlatIndexByKey(itemKeyToKeep);
                if (newIndex >= 0) {
                    setAutocompleteActive(newIndex);
                    return;
                }
            }

            setAutocompleteActive(0);
        }

        /* =========================================================
           ACTION STRATEGY WRAPPER
           - barcode hiện tại chỉ cần draft-local action
        ========================================================= */
        function applyDraftSuccess(options) {
            return applyDraftActionSuccess({
                draft: options?.draft,
                posState,
                syncDraftToUi,
                showSuccess: window.PosScanFeedback?.isPending() ? null : showSuccess,
                focusBarcodeInput,
                successMessage: options?.successMessage || '',
                focusBarcode: options?.focusBarcode !== false,
                afterSync: options?.afterSync || null
            });
        }

        async function searchBarcodeAutocomplete(keyword) {
            if (window.PosScanGuard?.isOpen()) return;
            const parsed = parseQtyPrefixedInput(keyword);

            const actualKeyword = (parsed.keyword || '').trim();
            if (actualKeyword !== parseQtyPrefixedInput(txtBarcode?.value || '').keyword) return;
            abortActiveSearch();
            const currentSeq = nextRequestSeq();

            // Lưu lại state qty prefix
            if (parsed.hasQtyPrefix) {
                barcodeSearchState.requestedQty = parsed.qty;
                barcodeSearchState.hasQtyPrefix = true;
            }

            // keyword dùng cho autocomplete/render/search phải là phần đã bỏ "2+"
            barcodeSearchState.keyword = actualKeyword;
            barcodeSearchState.lastKeyword = actualKeyword;

            if (actualKeyword.length < 2) {
                hideBarcodeAutocomplete();
                return;
            }

            // offline: không show toast, chỉ show notice trong autocomplete
            if (!isPosOnline(posState)) {
                abortActiveSearch();
                setLoading(false);

                if (typeof renderBarcodeAutocompleteOffline === 'function') {
                    renderBarcodeAutocompleteOffline(actualKeyword);
                } else {
                    renderBarcodeAutocompleteEmpty(barcodeAutocomplete, actualKeyword);
                }
                return;
            }

            const cacheKey = actualKeyword.toLowerCase();
            const cachedItems = barcodeSearchState.cache.get(cacheKey);

            if (cachedItems) {
                normalizeAutocompleteItems(cachedItems || []);
                renderBarcodeAutocomplete(barcodeSearchState.parentItems || []);
                return;
            }

            abortActiveSearch();

            const controller = new AbortController();
            barcodeSearchState.abortController = controller;

            setLoading(true);
            renderBarcodeAutocompleteLoading(barcodeAutocomplete, actualKeyword);

            try {
                const items = await fetchJson(
                    `/admin/pos/products/search?keyword=${encodeURIComponent(actualKeyword)}&take=10`,
                    {
                        method: 'GET',
                        signal: controller.signal
                    }
                );

                if (currentSeq !== barcodeSearchState.requestSeq) {
                    return;
                }

                barcodeSearchState.lastAppliedSeq = currentSeq;
                barcodeSearchState.cache.set(cacheKey, items || []);

                if (!items || !items.length) {
                    barcodeSearchState.parentItems = [];
                    barcodeSearchState.flatItems = [];
                    barcodeSearchState.items = [];
                    barcodeSearchState.activeIndex = -1;
                    renderBarcodeAutocompleteEmpty(barcodeAutocomplete, actualKeyword);
                    return;
                }

                normalizeAutocompleteItems(items || []);
                renderBarcodeAutocomplete(barcodeSearchState.parentItems || []);
            } catch (err) {
                if (isAbortError(err)) {
                    return;
                }

                if (currentSeq !== barcodeSearchState.requestSeq) {
                    return;
                }

                barcodeSearchState.parentItems = [];
                barcodeSearchState.flatItems = [];
                barcodeSearchState.items = [];
                barcodeSearchState.activeIndex = -1;
                renderBarcodeAutocompleteEmpty(barcodeAutocomplete, actualKeyword);
            } finally {
                if (barcodeSearchState.abortController === controller) {
                    barcodeSearchState.abortController = null;
                }

                if (currentSeq === barcodeSearchState.requestSeq) {
                    setLoading(false);
                }
            }
        }

        async function runScanAction(state, actionKey, handler, options) {
            if (window.PosScanGuard?.isOpen()) return { ok: false, status: 'blocked' };
            const submittedInput = txtBarcode?.value;
            const submittedRevision = inputRevision;
            hideBarcodeAutocomplete();
            let attempt;
            const feedback = window.PosScanFeedback;
            try {
                const result = await runPosAction(state, actionKey, async () => {
                    attempt = feedback?.begin(state.business?.currentDraft || state.currentDraft);
                    return await handler();
                }, { ...options,
                    onSuccess: draft => {
                        options.onSuccess(draft);
                        // Rendering feedback cannot turn an accepted sale into a failed command.
                        try { feedback?.confirmed(attempt, draft); } catch (error) { console.error('POS scan presentation:', error); }
                    }
                });
                if (!result.ok && attempt) feedback?.failed(attempt, result.error, options.retryScan);
                return result;
            } finally {
                // Clear the completed attempt even on failure, without erasing the next scan.
                if (txtBarcode && inputRevision === submittedRevision && txtBarcode.value === submittedInput) {
                    txtBarcode.value = '';
                    resetRequestedQty();
                    hideBarcodeAutocomplete();
                    if (!window.PosScanGuard?.isOpen()) focusBarcodeInput();
                }
            }
        }

        async function scanCurrentCart() {
            if (window.PosScanGuard?.isOpen()) return { ok: false, status: 'blocked' };
            const parsed = parseQtyPrefixedInput(txtBarcode?.value || '');
            const barcode = (parsed.keyword || parsed.raw || '').trim();
            const qty = getCurrentRequestedQty();
            const safeQty = qty > 0 ? qty : 1;

            if (!barcode) {
                focusBarcodeInput();
                return;
            }

            if (!actionLocker.lock(`scanCurrentCart:${barcode}:${safeQty}`)) {
                return;
            }

            setSubmitting(true);
            hideBarcodeAutocomplete();

            try {
                const result = await runScanAction(
                    posState,
                    `barcode:scanCurrentCart:${barcode}:${safeQty}`,
                    async function () {
                        return await postJson('/admin/pos/cart/current/scan', {
                            barcode: barcode,
                            quantity: safeQty
                        });
                    },
                    {
                        retryScan: () => { txtBarcode.value = `${safeQty}+${barcode}`; return scanCurrentCart(); },
                        fallbackMessage: 'Không thể quét barcode.',
                        scopes: ['cartMutate'],
                        blockedMessage: 'POS đang thanh toán hoặc chốt đơn, chưa thể quét sản phẩm lúc này.',
                        requireOnline: true,
                        offlineMessage: 'Đang offline, chưa thể quét hoặc thêm sản phẩm vào giỏ.',
                        offlineDisplayMode: 'toast',
                        onSuccess: function (draft) {
                            applyDraftSuccess({
                                draft,
                                successMessage: safeQty > 1
                                    ? `Đã thêm ${safeQty} sản phẩm vào giỏ`
                                    : 'Đã thêm sản phẩm vào giỏ',
                                focusBarcode: false
                            });
                        },
                        onError: function (normalized) {
                            const message = normalized.message || '';
                            if (window.PosScanGuard && (
                                message.includes('Không tìm thấy sản phẩm theo barcode') ||
                                message.includes('Sản phẩm hoặc đơn vị chưa có trong dữ liệu offline'))) {
                                // Discard any buffered next scan and its pending autocomplete reply.
                                const clearScan = () => {
                                    txtBarcode.value = '';
                                    inputRevision++;
                                    resetRequestedQty();
                                    hideBarcodeAutocomplete();
                                };
                                clearScan();
                                window.PosScanGuard.show(barcode, message, () => { clearScan(); focusBarcodeInput(); });
                                return;
                            }
                            if (!barcodeSearchState.flatItems || !barcodeSearchState.flatItems.length) {
                                showError(normalized.message || 'Không thể quét barcode.');
                            }
                        }
                    }
                );

                return result;
            } finally {
                setSubmitting(false);
                actionLocker.unlock(`scanCurrentCart:${barcode}:${safeQty}`);
            }
        }

        async function addVariantToCurrentCart(variantId, qty) {
            const parsedVariantId = parseInt(variantId || '0', 10);
            const parsedQty = Number(qty || 1);
            const safeQty = parsedQty > 0 ? parsedQty : 1;

            if (!parsedVariantId) {
                showError('Không xác định được sản phẩm cần thêm.');
                return;
            }

            const currentOrderId =
                posState?.business?.currentOrderId ||
                posState?.currentOrderId ||
                posState?.business?.currentDraft?.currentOrderId ||
                posState?.business?.currentDraft?.orderId ||
                posState?.business?.currentDraft?.id ||
                posState?.business?.currentDraft?.OrderId ||
                posState?.business?.screen?.currentDraft?.currentOrderId ||
                posState?.business?.screen?.currentDraft?.orderId ||
                posState?.business?.screen?.currentDraft?.id ||
                posState?.business?.screen?.currentDraft?.OrderId ||
                0;

            if (!currentOrderId) {
                showError('Chưa có giỏ hiện tại để thêm sản phẩm.');
                return;
            }

            if (!actionLocker.lock(`addVariantToCurrentCart:${parsedVariantId}:${safeQty}`)) {
                return;
            }

            setSubmitting(true);

            try {
                return await runScanAction(
                    posState,
                    `barcode:addVariant:${parsedVariantId}:${safeQty}`,
                    async function () {
                        return await postJson(
                            `/admin/pos/${currentOrderId}/items?variantId=${parsedVariantId}&qty=${safeQty}`,
                            {}
                        );
                    },
                    {
                        retryScan: () => addVariantToCurrentCart(parsedVariantId, safeQty),
                        fallbackMessage: 'Không thể thêm sản phẩm.',
                        scopes: ['cartMutate'],
                        blockedMessage: 'POS đang thanh toán hoặc chốt đơn, chưa thể thêm sản phẩm lúc này.',
                        requireOnline: true,
                        offlineMessage: 'Đang offline, chưa thể thêm sản phẩm vào giỏ.',
                        offlineDisplayMode: 'toast',
                        onSuccess: function (draft) {
                            applyDraftSuccess({
                                draft,
                                successMessage: safeQty > 1
                                    ? `Đã thêm ${safeQty} sản phẩm vào giỏ`
                                    : 'Đã thêm sản phẩm vào giỏ',
                                focusBarcode: false
                            });
                        }
                    }
                );
            } finally {
                setSubmitting(false);
                actionLocker.unlock(`addVariantToCurrentCart:${parsedVariantId}:${safeQty}`);
            }
        }

        async function addVariantUnitToCurrentCart(variantId, productUnitConversionId, qty) {
            const parsedVariantId = parseInt(variantId || '0', 10);
            const parsedConversionId = parseInt(productUnitConversionId || '0', 10);
            const parsedQty = Number(qty || 1);
            const safeQty = parsedQty > 0 ? parsedQty : 1;

            if (!parsedVariantId || !parsedConversionId) {
                showError('Không xác định được đơn vị sản phẩm cần thêm.');
                return;
            }

            const currentOrderId =
                posState?.business?.currentOrderId ||
                posState?.currentOrderId ||
                posState?.business?.currentDraft?.currentOrderId ||
                posState?.business?.currentDraft?.orderId ||
                posState?.business?.currentDraft?.id ||
                posState?.business?.currentDraft?.OrderId ||
                posState?.business?.screen?.currentDraft?.currentOrderId ||
                posState?.business?.screen?.currentDraft?.orderId ||
                posState?.business?.screen?.currentDraft?.id ||
                posState?.business?.screen?.currentDraft?.OrderId ||
                0;

            if (!currentOrderId) {
                showError('Chưa có giỏ hiện tại để thêm sản phẩm.');
                return;
            }

            if (!actionLocker.lock(`addVariantUnit:${parsedVariantId}:${parsedConversionId}:${safeQty}`)) {
                return;
            }

            setSubmitting(true);

            try {
                return await runScanAction(
                    posState,
                    `barcode:addVariantUnit:${parsedVariantId}:${parsedConversionId}:${safeQty}`,
                    async function () {
                        return await postJson(
                            `/admin/pos/${currentOrderId}/items?variantId=${parsedVariantId}&productUnitConversionId=${parsedConversionId}&qty=${safeQty}`,
                            {}
                        );
                    },
                    {
                        retryScan: () => addVariantUnitToCurrentCart(parsedVariantId, parsedConversionId, safeQty),
                        fallbackMessage: 'Không thể thêm sản phẩm theo đơn vị quy đổi.',
                        scopes: ['cartMutate'],
                        blockedMessage: 'POS đang thanh toán hoặc chốt đơn, chưa thể thêm sản phẩm lúc này.',
                        requireOnline: true,
                        offlineMessage: 'Đang offline, chưa thể thêm sản phẩm vào giỏ.',
                        offlineDisplayMode: 'toast',
                        onSuccess: function (draft) {
                            applyDraftSuccess({
                                draft,
                                successMessage: safeQty > 1
                                    ? `Đã thêm ${safeQty} sản phẩm vào giỏ`
                                    : 'Đã thêm sản phẩm vào giỏ',
                                focusBarcode: false
                            });
                        }
                    }
                );
            } finally {
                setSubmitting(false);
                actionLocker.unlock(`addVariantUnit:${parsedVariantId}:${parsedConversionId}:${safeQty}`);
            }
        }
        function expandActiveParent() {
            const activeItem = getActiveFlatItem();
            if (!activeItem) return;

            if (activeItem.kind !== 'parent') {
                return;
            }

            if (!activeItem.hasChildren) {
                return;
            }

            barcodeSearchState.expandedVariantIds.add(Number(activeItem.variantId || 0));
            rerenderAutocompleteKeepActive(activeItem.key);
        }

        function collapseFromActiveItem() {
            const activeItem = getActiveFlatItem();
            if (!activeItem) return;

            // Nếu đang đứng ở child => quay về parent
            if (activeItem.kind === 'child') {
                const parentIndex = findParentFlatIndex(activeItem.parentVariantId);
                if (parentIndex >= 0) {
                    setAutocompleteActive(parentIndex);
                }
                return;
            }

            // Nếu đang ở parent expanded => collapse
            if (
                activeItem.kind === 'parent' &&
                barcodeSearchState.expandedVariantIds.has(Number(activeItem.variantId || 0))
            ) {
                barcodeSearchState.expandedVariantIds.delete(Number(activeItem.variantId || 0));
                rerenderAutocompleteKeepActive(activeItem.key);
            }
        }

        async function selectAutocompleteItem(item) {
            if (!item) return;

            const qty = getCurrentRequestedQty();
            hideBarcodeAutocomplete();

            if (item.kind === 'child') {
                if (item.variantId && item.productUnitConversionId) {
                    await addVariantUnitToCurrentCart(item.variantId, item.productUnitConversionId, qty);
                } else {
                    showError('Không xác định được đơn vị sản phẩm cần thêm.');
                }
                return;
            }

            if (item.kind === 'parent') {
                if (item.variantId) {
                    await addVariantToCurrentCart(item.variantId, qty);
                }
            }
        }

        function bindEvents() {
            btnScan?.addEventListener('click', scanCurrentCart);
            btnFocusBarcode?.addEventListener('click', focusBarcodeInput);

            txtBarcode?.addEventListener('keydown', async function (e) {
                if (e.isComposing || e.keyCode === 229) return;
                const isCustomerMode = barcodeSearchState.mode === 'customer'
                    || isCustomerCommand(txtBarcode?.value || '');

                const isVoucherMode = barcodeSearchState.mode === 'voucher'
                    || isVoucherCommand(txtBarcode?.value || '');

                if (isCustomerMode) {
                    const hasCustomerAutocomplete =
                        Array.isArray(barcodeSearchState.customerItems) &&
                        barcodeSearchState.customerItems.length > 0;

                    if (e.key === 'ArrowDown') {
                        if (!hasCustomerAutocomplete) return;
                        e.preventDefault();
                        setCustomerAutocompleteActive(barcodeSearchState.activeIndex + 1);
                        return;
                    }

                    if (e.key === 'ArrowUp') {
                        if (!hasCustomerAutocomplete) return;
                        e.preventDefault();
                        setCustomerAutocompleteActive(barcodeSearchState.activeIndex - 1);
                        return;
                    }

                    if (e.key === 'Escape') {
                        e.preventDefault();
                        txtBarcode.value = '';
                        hideBarcodeAutocomplete();
                        focusBarcodeInput();
                        return;
                    }

                    if (e.key === 'Enter') {
                        e.preventDefault();

                        if (barcodeSearchState.isSubmitting) return;

                        if (hasCustomerAutocomplete) {
                            const activeCustomer = getActiveCustomerItem();
                            if (activeCustomer) {
                                await selectCustomerFromAutocomplete(activeCustomer);
                                return;
                            }
                        }

                        const keyword = getCustomerKeyword(txtBarcode?.value || '');
                        if (keyword.length >= 2) {
                            await searchCustomerFromBarcodeInput(txtBarcode.value);

                            const items = barcodeSearchState.customerItems || [];
                            if (items.length === 1) {
                                await selectCustomerFromAutocomplete(items[0]);
                            }
                        }

                        return;
                    }
                }
                if (isVoucherMode) {
                    const hasVoucherAutocomplete =
                        Array.isArray(barcodeSearchState.voucherItems) &&
                        barcodeSearchState.voucherItems.length > 0;

                    if (e.key === 'ArrowDown') {
                        if (!hasVoucherAutocomplete) return;
                        e.preventDefault();
                        setVoucherAutocompleteActive(barcodeSearchState.activeIndex + 1);
                        return;
                    }

                    if (e.key === 'ArrowUp') {
                        if (!hasVoucherAutocomplete) return;
                        e.preventDefault();
                        setVoucherAutocompleteActive(barcodeSearchState.activeIndex - 1);
                        return;
                    }

                    if (e.key === 'Escape') {
                        e.preventDefault();
                        txtBarcode.value = '';
                        hideBarcodeAutocomplete();
                        focusBarcodeInput();
                        return;
                    }

                    if (e.key === 'Enter') {
                        e.preventDefault();

                        if (barcodeSearchState.isSubmitting) return;

                        if (hasVoucherAutocomplete) {
                            const activeVoucher = getActiveVoucherItem();
                            if (activeVoucher) {
                                await selectVoucherFromAutocomplete(activeVoucher);
                                return;
                            }
                        }

                        await lookupAndApplyVoucherByCode(txtBarcode.value);
                        return;
                    }
                }
                const hasAutocomplete =
                    !!parseQtyPrefixedInput(txtBarcode?.value || '').keyword &&
                    parseQtyPrefixedInput(txtBarcode?.value || '').keyword === barcodeSearchState.keyword &&
                    Array.isArray(barcodeSearchState.flatItems) &&
                    barcodeSearchState.flatItems.length > 0;

                if (e.key === 'ArrowDown') {
                    if (!hasAutocomplete) return;
                    e.preventDefault();
                    setAutocompleteActive(barcodeSearchState.activeIndex + 1);
                    return;
                }

                if (e.key === 'ArrowUp') {
                    if (!hasAutocomplete) return;
                    e.preventDefault();
                    setAutocompleteActive(barcodeSearchState.activeIndex - 1);
                    return;
                }

                if (e.key === 'ArrowRight') {
                    if (!hasAutocomplete) return;
                    e.preventDefault();
                    expandActiveParent();
                    return;
                }

                if (e.key === 'ArrowLeft') {
                    if (!hasAutocomplete) return;
                    e.preventDefault();
                    collapseFromActiveItem();
                    return;
                }

                if (e.key === 'Escape') {
                    txtBarcode.value = '';
                    resetRequestedQty();
                    hideBarcodeAutocomplete();
                    focusBarcodeInput();
                    return;
                }

                if (e.key === 'Enter') {
                    e.preventDefault();
                    await confirmProductSearch();
                }
            });

            txtBarcode?.addEventListener('input', function () {
                inputRevision++;
                const rawInput = (txtBarcode.value || '').trim();
                // Invalidate the previous query immediately, before the 300 ms debounce.
                hideBarcodeAutocomplete();
                if (isCustomerCommand(rawInput)) {
                    debouncedSearch.cancel?.();
                    resetRequestedQty();
                    searchCustomerFromBarcodeInput(rawInput);
                    return;
                }
                if (isVoucherCommand(rawInput)) {
                    debouncedSearch.cancel?.();
                    resetRequestedQty();
                    searchVoucherFromBarcodeInput(rawInput);
                    return;
                }
                const parsed = parseQtyPrefixedInput(rawInput);

                if (applyQtyPrefixTyping(rawInput)) {
                    return;
                }
                // BƯỚC B: lưu requested qty vào state
                // Chỉ đổi SL khi người dùng gõ dạng 6+.
                // Khi đã hiện badge SL: 6 rồi, gõ tên sản phẩm không được reset về 1.
                if (parsed.hasQtyPrefix) {
                    barcodeSearchState.requestedQty = parsed.qty;
                    barcodeSearchState.hasQtyPrefix = true;
                }

               
                if (!rawInput) {
                    debouncedSearch.cancel?.();
                    hideBarcodeAutocomplete();
                    return;
                }

                debouncedSearch(rawInput);
            });

            barcodeAutocomplete?.addEventListener('click', async function (e) {
                const customerRow = e.target.closest('[data-customer-id]');
                if (customerRow) {
                    e.preventDefault();

                    const customerId = Number(customerRow.getAttribute('data-customer-id') || 0);
                    if (!customerId) return;

                    const customer = (barcodeSearchState.customerItems || []).find(x =>
                        Number(x.customerId || x.id || 0) === customerId
                    );

                    await selectCustomerFromAutocomplete(customer || { customerId: customerId });
                    return;
                }
                const voucherRow = e.target.closest('[data-voucher-id]');
                if (voucherRow) {
                    e.preventDefault();

                    const voucherId = Number(voucherRow.getAttribute('data-voucher-id') || 0);
                    if (!voucherId) return;

                    const voucher = (barcodeSearchState.voucherItems || []).find(x =>
                        Number(x.id || x.Id || 0) === voucherId
                    );

                    await selectVoucherFromAutocomplete(voucher || { id: voucherId });
                    return;
                }
                const expandButton = e.target.closest('[data-expand-toggle="true"]');
                if (expandButton) {
                    e.preventDefault();
                    e.stopPropagation();

                    const itemKey = expandButton.getAttribute('data-item-key') || '';
                    if (!itemKey) return;

                    const flatItems = barcodeSearchState.flatItems || [];
                    const item = flatItems.find(function (x) {
                        return x && x.key === itemKey;
                    });

                    if (!item || item.kind !== 'parent' || !item.hasChildren) {
                        return;
                    }

                    const variantId = Number(item.variantId || 0);
                    if (!variantId) return;

                    if (barcodeSearchState.expandedVariantIds.has(variantId)) {
                        barcodeSearchState.expandedVariantIds.delete(variantId);
                    } else {
                        barcodeSearchState.expandedVariantIds.add(variantId);
                    }

                    rerenderAutocompleteKeepActive(item.key);
                    return;
                }
                const thumb = e.target.closest('.pos-ac-thumb-wrap');
                if (thumb) {
                    e.preventDefault();
                    e.stopPropagation();

                    const url = thumb.getAttribute('data-image-url') || '';
                    const alt = thumb.querySelector('img')?.alt || '';

                    const imgEl = document.getElementById('imagePreviewEl');
                    if (imgEl) {
                        imgEl.src = url;
                        imgEl.alt = alt || '';
                    }

                    const modal = bootstrap.Modal.getOrCreateInstance(
                        document.getElementById('imagePreviewModal')
                    );
                    modal.show();

                    return;
                }

                const row = e.target.closest('[data-item-key]');
                if (!row) return;

                if (barcodeSearchState.isSubmitting) {
                    return;
                }

                const itemKey = row.getAttribute('data-item-key') || '';
                if (!itemKey) return;

                const flatItems = barcodeSearchState.flatItems || [];
                const item = flatItems.find(function (x) {
                    return x && x.key === itemKey;
                });

                if (!item) return;

                await selectAutocompleteItem(item);
            });

            document.addEventListener('click', function (e) {
                const target = e.target;
                const clickInsideAutocomplete =
                    barcodeAutocomplete?.contains(target) ||
                    txtBarcode?.contains?.(target);

                if (!clickInsideAutocomplete) {
                    hideBarcodeAutocomplete();
                }
            });
            barcodeAutocomplete?.addEventListener('mousemove', function (e) {
                const row = e.target.closest('[data-autocomplete-index]');
                if (!row) return;

                const index = Number(row.getAttribute('data-autocomplete-index'));
                if (!Number.isInteger(index) || index < 0) return;

                if (barcodeSearchState.activeIndex !== index) {
                    setAutocompleteActive(index);
                }
            });
            barcodeAutocomplete?.addEventListener('mouseover', function (e) {
                const thumb = e.target.closest('.pos-ac-thumb-wrap');
                if (!thumb) return;

                const url = thumb.getAttribute('data-image-url') || '';
                const alt = thumb.querySelector('img')?.alt || '';

                showAutocompleteImagePreview(url, alt, thumb);
            });

            barcodeAutocomplete?.addEventListener('mouseout', function (e) {
                const thumb = e.target.closest('.pos-ac-thumb-wrap');
                if (!thumb) return;

                const related = e.relatedTarget;
                if (related && thumb.contains(related)) return;

                hideAutocompleteImagePreview();
            });
        }

        function bindUiLocks() {
            registerUiLock(posState, {
                target: btnScan,
                requireOnline: true,
                busyScopes: ['cartMutate', 'checkout'],
                pendingActions: ['barcode:scanCurrentCart'],
                offlineMessage: 'Đang offline, chưa thể quét barcode.',
                busyMessage: 'POS đang bận xử lý giỏ hàng hoặc thanh toán.',
                pendingMessage: 'Đang quét barcode.'
            });

            registerUiLock(posState, {
                target: btnFocusBarcode,
                requireOnline: false,
                busyScopes: [],
                pendingActions: []
            });
        }

        bindUiLocks();

        return {
            searchBarcodeAutocomplete,
            scanCurrentCart,
            addVariantToCurrentCart,
            hideBarcodeAutocomplete,
            setAutocompleteActive,
            bindEvents
        };
    }

    return {
        create
    };
})();
