/* =========================================================
   FILE: pos.customer.js
   Mục đích:
   - Tách toàn bộ customer flow khỏi Index.cshtml
   - Không đụng business flow khác
   - BƯỚC 1 PRODUCTION:
     + debounce search
     + cache search
     + abort request cũ
     + request seq guard
     + loading state
     + không spam toast khi đang search
   - BƯỚC 4B:
     + inline error cho quick create customer
     + inline error mềm cho set/clear customer trong modal
   - BƯỚC 5A.4:
     + chuẩn hóa action strategy cho customer
     + draft-local action => applyDraftActionSuccess(...)
========================================================= */

window.PosCustomer = (function () {
    'use strict';

    function create(deps) {
        const {
            posState,
            customerSearchState,
            elements,
            modals,
            helpers
        } = deps;

        const {
            customerInfoBox,
            txtCustomerKeyword,
            btnSearchCustomer,
            customerSearchResult,
            btnClearCustomer,
            btnOpenQuickCreateCustomer,
            qcCustomerName,
            qcCustomerPhone,
            qcCustomerAddress,
            qcCustomerNote,
            qcCustomerPriceTier,
            btnSubmitQuickCreateCustomer
        } = elements;

        const {
            customerModal,
            quickCreateCustomerModal
        } = modals;

        const {
            fetchJson,
            postJson,
            deleteWithToken,
            runPosAction,
            focusBarcodeInput,
            syncDraftToUi,
            renderCustomerInfo,
            showSuccess,
            showError,
            formatMoney,
            // BƯỚC 5A.4
            applyDraftActionSuccess,
            applyScreenActionSuccess
        } = helpers;

        const {
            debounce,
            createTtlCache,
            isAbortError,
            setInlineError,
            clearInlineError,
            registerUiLock,
            isPosOnline
        } = window.PosCommon;

        customerSearchState.cache = customerSearchState.cache || createTtlCache(60000, 100);

        const debouncedSearchCustomers = debounce(function (keyword) {
            searchCustomers(keyword);
        }, 300);

        const customerRewardModalEl = document.getElementById('customerRewardModal');

        const customerRewardModal = customerRewardModalEl && window.bootstrap
            ? bootstrap.Modal.getOrCreateInstance(customerRewardModalEl)
            : null;
        const useRewardVoucherModalEl = document.getElementById('useRewardVoucherModal');

        const useRewardVoucherModal = useRewardVoucherModalEl && window.bootstrap
            ? bootstrap.Modal.getOrCreateInstance(useRewardVoucherModalEl)
            : null;

        let useVoucherItems = [];
        /* =========================================================
           INLINE ERROR BOX
        ========================================================= */
        function ensureCustomerModalErrorBox() {
            const modalEl = document.getElementById('customerModal');
            if (!modalEl) return null;

            let box = modalEl.querySelector('#customerInlineErrorBox');
            if (box) return box;

            box = document.createElement('div');
            box.id = 'customerInlineErrorBox';
            box.style.display = 'none';

            const modalBody =
                modalEl.querySelector('.modal-body') ||
                modalEl.querySelector('.modal-content') ||
                modalEl;

            if (modalBody.firstChild) {
                modalBody.insertBefore(box, modalBody.firstChild);
            } else {
                modalBody.appendChild(box);
            }

            return box;
        }

        function ensureQuickCreateErrorBox() {
            const modalEl = document.getElementById('quickCreateCustomerModal');
            if (!modalEl) return null;

            let box = modalEl.querySelector('#quickCreateCustomerInlineErrorBox');
            if (box) return box;

            box = document.createElement('div');
            box.id = 'quickCreateCustomerInlineErrorBox';
            box.style.display = 'none';

            const modalBody =
                modalEl.querySelector('.modal-body') ||
                modalEl.querySelector('.modal-content') ||
                modalEl;

            if (modalBody.firstChild) {
                modalBody.insertBefore(box, modalBody.firstChild);
            } else {
                modalBody.appendChild(box);
            }

            return box;
        }

        function clearCustomerModalInlineError() {
            clearInlineError(ensureCustomerModalErrorBox());
        }

        function showCustomerModalInlineError(message, variant) {
            setInlineError(ensureCustomerModalErrorBox(), message, variant || 'danger');
        }

        function clearQuickCreateInlineError() {
            clearInlineError(ensureQuickCreateErrorBox());
        }

        function showQuickCreateInlineError(message, variant) {
            setInlineError(ensureQuickCreateErrorBox(), message, variant || 'danger');
        }

        function nextRequestSeq() {
            customerSearchState.requestSeq = Number(customerSearchState.requestSeq || 0) + 1;
            return customerSearchState.requestSeq;
        }

        function setLoading(isLoading) {
            customerSearchState.isLoading = !!isLoading;

            if (txtCustomerKeyword) {
                txtCustomerKeyword.dataset.loading = isLoading ? 'true' : 'false';
            }

            if (btnSearchCustomer) {
                btnSearchCustomer.disabled = !!isLoading;
                btnSearchCustomer.dataset.loading = isLoading ? 'true' : 'false';
            }
        }

        function abortActiveSearch() {
            if (customerSearchState.abortController) {
                try {
                    customerSearchState.abortController.abort();
                } catch (_) {
                }
                customerSearchState.abortController = null;
            }
        }

        function clearSearchState() {
            abortActiveSearch();
            setLoading(false);

            customerSearchState.items = [];
            customerSearchState.keyword = '';
            customerSearchState.lastKeyword = '';
        }

        /* =========================================================
           SEARCH RESULT RENDER
        ========================================================= */
        function renderSearchHint() {
            if (!customerSearchResult) return;

            customerSearchResult.innerHTML = `
                <div class="p-3 text-muted">
                    Nhập tên hoặc số điện thoại để tìm khách hàng.
                </div>
            `;
        }

        function renderSearchMinKeyword() {
            if (!customerSearchResult) return;

            customerSearchResult.innerHTML = `
                <div class="p-3 text-muted">
                    Vui lòng nhập ít nhất 2 ký tự để tìm khách hàng.
                </div>
            `;
        }

        function renderSearchLoading(keyword) {
            if (!customerSearchResult) return;

            const safeKeyword = window.PosCommon.escapeHtml(keyword || '');

            customerSearchResult.innerHTML = `
                <div class="p-3">
                    <div class="small text-muted mb-2">
                        Đang tìm khách hàng cho: <strong>${safeKeyword}</strong>
                    </div>

                    <div class="placeholder-glow mb-2">
                        <span class="placeholder col-12"></span>
                    </div>
                    <div class="placeholder-glow mb-2">
                        <span class="placeholder col-10"></span>
                    </div>
                    <div class="placeholder-glow">
                        <span class="placeholder col-11"></span>
                    </div>
                </div>
            `;
        }

        function renderSearchEmpty() {
            if (!customerSearchResult) return;

            customerSearchResult.innerHTML = `
                <div class="p-3 text-muted">
                    Không tìm thấy khách hàng phù hợp.
                </div>
            `;
        }

        function renderSearchError(message) {
            if (!customerSearchResult) return;

            customerSearchResult.innerHTML = `
                <div class="p-3 text-danger">
                    ${window.PosCommon.escapeHtml(message || 'Không thể tìm khách hàng.')}
                </div>
            `;
        }

        function renderSearchResults(items) {
            if (!customerSearchResult) return;

            const list = Array.isArray(items) ? items : [];

            if (!list.length) {
                renderSearchEmpty();
                return;
            }

            customerSearchResult.innerHTML = `
                <div class="list-group list-group-flush">
                    ${list.map(function (x) {
                const customerId = Number(x?.customerId || x?.id || 0);
                const name = window.PosCommon.escapeHtml(x?.name || 'Khách hàng');
                const phone = window.PosCommon.escapeHtml(x?.phone || '');
                const address = window.PosCommon.escapeHtml(x?.address || '');

                return `
                            <button type="button"
                                    class="list-group-item list-group-item-action"
                                    data-customer-id="${customerId}">
                                <div class="fw-semibold">${name}</div>
                                <div class="small text-muted">${phone || 'Chưa có số điện thoại'}</div>
                                ${address ? `<div class="small text-muted">${address}</div>` : ''}
                            </button>
                        `;
            }).join('')}
                </div>
            `;
        }

        /* =========================================================
           ACTION STRATEGY WRAPPER
           - customer hiện tại chỉ cần draft-local action
        ========================================================= */
        function applyDraftSuccess(options) {
            return applyDraftActionSuccess({
                draft: options?.draft,
                posState,
                syncDraftToUi,
                showSuccess,
                focusBarcodeInput,
                successMessage: options?.successMessage || '',
                focusBarcode: options?.focusBarcode !== false,
                afterSync: options?.afterSync || null
            });
        }

        /* =========================================================
           CUSTOMER SEARCH
        ========================================================= */
        function openCustomerModal() {
            clearCustomerModalInlineError();

            if (txtCustomerKeyword) {
                txtCustomerKeyword.value = '';
            }

            clearSearchState();
            renderSearchHint();
            customerModal?.show();

            setTimeout(() => {
                txtCustomerKeyword?.focus();
            }, 180);
        }

        async function searchCustomers(keyword) {
            keyword = (keyword || txtCustomerKeyword?.value || '').trim();
            customerSearchState.keyword = keyword;
            customerSearchState.lastKeyword = keyword;

            if (keyword.length < 2) {
                abortActiveSearch();
                setLoading(false);
                renderSearchMinKeyword();
                return;
            }

            if (!isPosOnline?.(posState)) {
                renderSearchError('Đang offline, chưa thể tìm khách hàng.');
                return;
            }

            const cacheKey = keyword.toLowerCase();
            const cachedItems = customerSearchState.cache.get(cacheKey);

            if (cachedItems) {
                customerSearchState.items = Array.isArray(cachedItems) ? cachedItems : [];
                renderSearchResults(customerSearchState.items);
                return;
            }

            abortActiveSearch();

            const controller = new AbortController();
            customerSearchState.abortController = controller;

            const currentSeq = nextRequestSeq();
            setLoading(true);
            renderSearchLoading(keyword);

            try {
                const items = await fetchJson(
                    `/admin/pos/customers/search?keyword=${encodeURIComponent(keyword)}`,
                    {
                        signal: controller.signal
                    }
                );

                if (currentSeq !== customerSearchState.requestSeq) {
                    return;
                }

                customerSearchState.items = Array.isArray(items) ? items : [];
                customerSearchState.cache.set(cacheKey, customerSearchState.items);

                renderSearchResults(customerSearchState.items);
            } catch (err) {
                if (isAbortError(err)) {
                    return;
                }

                renderSearchError(err?.message || 'Không thể tìm khách hàng.');
            } finally {
                if (currentSeq === customerSearchState.requestSeq) {
                    setLoading(false);
                }
            }
        }

        async function searchCustomersImmediate() {
            debouncedSearchCustomers.cancel?.();
            await searchCustomers();
        }

        /* =========================================================
           SET / CLEAR CUSTOMER
           - draft-local action
        ========================================================= */
        async function shouldRepriceExistingLinesForCustomerChange(customerId) {
            const draft = getCurrentDraft();
            const lines = draft?.lines || draft?.Lines || [];

            if (!Array.isArray(lines) || lines.length <= 0) {
                return false;
            }

            if (typeof Swal === 'undefined') {
                return false;
            }

            const result = await Swal.fire({
                title: 'Áp lại giá cho giỏ hàng?',
                html: `
            Giỏ hiện tại đã có sản phẩm.<br>
            Bạn có muốn cập nhật lại giá theo khách vừa chọn không?
        `,
                icon: 'question',
                showCancelButton: true,
                confirmButtonText: 'Áp lại giá',
                cancelButtonText: 'Giữ giá hiện tại',
                reverseButtons: true
            });

            return result.isConfirmed;
        }

        async function setCustomerToCurrentCart(customerId) {
            clearCustomerModalInlineError();

            const repriceExistingLines =
                await shouldRepriceExistingLinesForCustomerChange(customerId);

            return await runPosAction(
                posState,
                `customer:set:${customerId}:${repriceExistingLines ? 'reprice' : 'keep'}`,
                async function () {
                    return await postJson(`/admin/pos/cart/current/customer/${customerId}`, {
                        repriceExistingLines: repriceExistingLines
                    });
                },
                {
                    fallbackMessage: 'Không thể chọn khách hàng.',
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể gán khách hàng vào giỏ.',
                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',
                    inlineTarget: ensureCustomerModalErrorBox(),
                    clearInlineOnStart: true,
                    onSuccess: function (draft) {
                        clearCustomerModalInlineError();

                        applyDraftSuccess({
                            draft,
                            successMessage: repriceExistingLines
                                ? 'Đã chọn khách hàng và áp lại giá'
                                : 'Đã chọn khách hàng cho giỏ hiện tại',
                            focusBarcode: true,
                            afterSync: function () {
                                customerModal?.hide?.();
                            }
                        });
                    }
                }
            );
        }

        async function clearCustomerFromCurrentCart() {
            clearCustomerModalInlineError();

            return await runPosAction(
                posState,
                'customer:clearCurrent',
                async function () {
                    return await deleteWithToken('/admin/pos/cart/current/customer');
                },
                {
                    fallbackMessage: 'Không thể bỏ khách khỏi giỏ hiện tại.',
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể bỏ khách hàng khỏi giỏ.',
                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',
                    inlineTarget: ensureCustomerModalErrorBox(),
                    clearInlineOnStart: true,
                    onSuccess: function (draft) {
                        clearCustomerModalInlineError();

                        applyDraftSuccess({
                            draft,
                            successMessage: 'Đã bỏ khách hàng khỏi giỏ hiện tại',
                            focusBarcode: true
                        });
                    }
                }
            );
        }

        /* =========================================================
           QUICK CREATE CUSTOMER
           - tạo nhanh rồi patch luôn current draft
        ========================================================= */
        function focusQuickCreateName() {
            setTimeout(function () {
                qcCustomerName?.focus();
                qcCustomerName?.select?.();
            }, 250);
        }
        function openQuickCreateCustomerModal(prefill) {
            const raw = String(prefill?.value || '').trim();
            const isPhone = /^[0-9]{8,15}$/.test(raw);

            if (qcCustomerName) qcCustomerName.value = isPhone ? '' : raw;
            if (qcCustomerPhone) qcCustomerPhone.value = isPhone ? raw : '';
            if (qcCustomerAddress) qcCustomerAddress.value = '';
            if (qcCustomerNote) qcCustomerNote.value = '';
            if (qcCustomerPriceTier) qcCustomerPriceTier.value = 'RETAIL';
            clearQuickCreateInlineError();
            quickCreateCustomerModal?.show();

        }
        function bindQuickCreateKeyboard() {
            const fields = [
                qcCustomerName,
                qcCustomerPhone,
                document.getElementById('qcCustomerPriceTier'),
                qcCustomerAddress,
                qcCustomerNote
            ].filter(Boolean);

            fields.forEach(function (field, index) {
                field.addEventListener('keydown', async function (e) {
                    // Ctrl + Enter: lưu khách
                    if (e.key === 'Enter' && e.ctrlKey) {
                        e.preventDefault();
                        await submitQuickCreateCustomer();
                        return;
                    }

                    // Enter thường: chuyển ô kế tiếp
                    if (e.key === 'Enter') {
                        e.preventDefault();

                        const next = fields[index + 1];
                        if (next) {
                            next.focus();
                            next.select?.();
                        } else {
                            await submitQuickCreateCustomer();
                        }
                    }
                });
            });
        }
        function validateQuickCreateInput() {
            const name = (qcCustomerName?.value || '').trim();

            if (!name) {
                return 'Vui lòng nhập tên khách hàng.';
            }

            return '';
        }

        async function submitQuickCreateCustomer() {
            clearQuickCreateInlineError();

            const validationMessage = validateQuickCreateInput();
            if (validationMessage) {
                showQuickCreateInlineError(validationMessage, 'warning');
                qcCustomerName?.focus();
                return;
            }

            const name = (qcCustomerName?.value || '').trim();
            const phone = (qcCustomerPhone?.value || '').trim();
            const address = (qcCustomerAddress?.value || '').trim();
            const note = (qcCustomerNote?.value || '').trim();
            const priceTier = (qcCustomerPriceTier?.value || 'RETAIL').trim();
            return await runPosAction(
                posState,
                'customer:quickCreate',
                async function () {
                    return await postJson('/admin/pos/customers/quick-create', {
                        name,
                        phone: phone || null,
                        address: address || null,
                        note: note || null,
                        priceTier: priceTier || 'RETAIL'
                    });
                },
                {
                    button: btnSubmitQuickCreateCustomer,
                    busyText: 'Đang tạo...',
                    fallbackMessage: 'Không thể tạo nhanh khách hàng.',
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể tạo nhanh khách hàng.',
                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',
                    inlineTarget: ensureQuickCreateErrorBox(),
                    clearInlineOnStart: true,
                    scopes: ['cartMutate', 'modalSubmit'],
                    onSuccess: function (draft) {
                        clearQuickCreateInlineError();

                        applyDraftSuccess({
                            draft,
                            successMessage: 'Đã tạo và chọn khách hàng cho giỏ hiện tại',
                            focusBarcode: true,
                            afterSync: function () {
                                quickCreateCustomerModal?.hide?.();
                                customerModal?.hide?.();
                            }
                        });
                    }
                }
            );
        }
        function getCurrentDraft() {
            return posState?.business?.currentDraft || posState?.currentDraft || null;
        }

        function getCurrentCustomerId() {
            const draft = getCurrentDraft();
            return Number(draft?.customer?.customerId || draft?.customerId || 0);
        }

        function renderRewardModal(summary) {
            const box = document.getElementById('rewardModalSummary');
            if (!box) return;

            box.innerHTML = `
        <div class="pos-reward-modal-grid">
            <div>
                <span>Điểm hiện có</span>
              <strong>${window.PosCommon.formatMoney(summary?.availablePoints || 0)}</strong>
            </div>
            <div>
                <span>Có thể đổi</span>
                <strong>${summary?.redeemableVoucherCount || 0} phiếu</strong>
            </div>
            <div>
                <span>Phiếu sẵn</span>
                <strong>${summary?.availableVoucherCount || 0} phiếu</strong>
            </div>
            <div>
                <span>Giá trị phiếu sẵn</span>
             <strong>${window.PosCommon.formatMoney(summary?.availableVoucherValue || 0)}</strong>
            </div>
        </div>
    `;
            const input = document.getElementById('rewardVoucherCount');
            if (input) {
                const maxCount = Number(summary?.redeemableVoucherCount || 0);
                input.max = String(maxCount);
                input.value = maxCount > 0 ? '1' : '0';
            }
        }

        async function reloadRewardSummary(customerId) {
            const summary = await fetchJson(`/admin/api/customers/${customerId}/reward-summary`);

            const draft = getCurrentDraft();
            if (draft) {
                draft.rewardSummary = summary;
                draft.RewardSummary = summary;
                syncDraftToUi(draft, posState);
            }

            renderRewardModal(summary);
            return summary;
        }

        async function openRewardModal() {
            const customerId = getCurrentCustomerId();
            if (!customerId) {
                showError('Vui lòng chọn khách hàng trước.');
                return;
            }

            const draft = getCurrentDraft();
            const nameEl = document.getElementById('rewardCustomerName');
            if (nameEl) {
                nameEl.textContent = draft?.customerName || draft?.customer?.name || 'Khách hàng';
            }

            const input = document.getElementById('rewardVoucherCount');
            if (input) input.value = '1';

            document.getElementById('rewardModalError')?.classList.add('d-none');

            customerRewardModal?.show();
            await reloadRewardSummary(customerId);
            setTimeout(function () {
                const input = document.getElementById('rewardVoucherCount');
                if (!input) return;

                input.focus();
                input.select?.();
            }, 150);
        }

        async function redeemRewardVoucher() {
            const customerId = getCurrentCustomerId();
            if (!customerId) return;

            const input = document.getElementById('rewardVoucherCount');
            const count = Number(input?.value || 0);
            const maxCount = Number(input?.max || 0);

            if (maxCount > 0 && count > maxCount) {
                showError(`Khách chỉ đổi tối đa ${maxCount} phiếu.`);
                input.value = String(maxCount);
                return;
            }
            if (!count || count <= 0) {
                showError('Số phiếu muốn đổi không hợp lệ.');
                return;
            }

            await runPosAction(
                posState,
                `reward:redeem:${customerId}:${count}`,
                async function () {
                    return await postJson('/admin/api/customers/redeem-voucher', {
                        customerId: customerId,
                        voucherCount: count,
                        description: 'Đổi phiếu tại POS'
                    });
                },
                {
                    fallbackMessage: 'Không thể đổi điểm thành phiếu.',
                    requireOnline: true,
                    button: document.getElementById('btnRedeemRewardVoucher'),
                    busyText: 'Đang đổi...',
                    onSuccess: async function () {
                        showSuccess('Đã đổi điểm thành phiếu.');
                        await reloadRewardSummary(customerId);
                    }
                }
            );
        }
        function updateUseVoucherTotal() {
            const selectedIds = Array.from(
                document.querySelectorAll('.use-voucher-check:checked')
            ).map(x => Number(x.value || 0));

            const total = useVoucherItems
                .filter(x => selectedIds.includes(Number(x.id || x.Id || 0)))
                .reduce((sum, x) => sum + Number(x.value || x.Value || 0), 0);

            const el = document.getElementById('useVoucherTotal');
            if (el) el.textContent = window.PosCommon.formatMoney(total);
        }

        async function applySelectedRewardVouchers() {
            const selectedIds = Array.from(
                document.querySelectorAll('.use-voucher-check:checked')
            )
                .map(x => Number(x.value || 0))
                .filter(x => x > 0);

            if (!selectedIds.length) {
                await clearAppliedRewardVouchers();
                return;
            }

            await runPosAction(
                posState,
                `reward-voucher:apply:${selectedIds.join(',')}`,
                async function () {
                    return await postJson('/admin/pos/cart/current/reward-vouchers', {
                        voucherIds: selectedIds
                    });
                },
                {
                    fallbackMessage: 'Không thể áp dụng voucher.',
                    requireOnline: true,
                    button: document.getElementById('btnApplyRewardVouchers'),
                    busyText: 'Đang áp dụng...',
                    onSuccess: function (draft) {
                        applyDraftSuccess({
                            draft,
                            successMessage: 'Đã áp dụng voucher vào giỏ.',
                            focusBarcode: true,
                            afterSync: function () {
                                useRewardVoucherModal?.hide?.();
                            }
                        });
                    }
                }
            );
        }

        async function clearAppliedRewardVouchers() {
            await runPosAction(
                posState,
                'reward-voucher:clear',
                async function () {
                    return await deleteWithToken('/admin/pos/cart/current/reward-vouchers');
                },
                {
                    fallbackMessage: 'Không thể bỏ voucher khỏi giỏ.',
                    requireOnline: true,
                    button: document.getElementById('btnClearAppliedVouchers'),
                    busyText: 'Đang bỏ...',
                    onSuccess: function (draft) {
                        applyDraftSuccess({
                            draft,
                            successMessage: 'Đã bỏ voucher khỏi giỏ.',
                            focusBarcode: true,
                            afterSync: function () {
                                useRewardVoucherModal?.hide?.();
                            }
                        });
                    }
                }
            );
        }
        /* =========================================================
           EVENTS
        ========================================================= */
        function bindEvents() {

            ensureCustomerModalErrorBox();
            ensureQuickCreateErrorBox();
            bindQuickCreateKeyboard();
            document
                .getElementById('quickCreateCustomerModal')
                ?.addEventListener('shown.bs.modal', function () {
                    setTimeout(function () {
                        qcCustomerName?.focus();
                        qcCustomerName?.select?.();
                    }, 50);
                });
            document.getElementById('btnRedeemRewardVoucher')
                ?.addEventListener('click', async function () {
                    await redeemRewardVoucher();
                });
            btnSearchCustomer?.addEventListener('click', async function () {
                await searchCustomersImmediate();
            });

            btnOpenQuickCreateCustomer?.addEventListener('click', function () {
                openQuickCreateCustomerModal();
            });

            btnSubmitQuickCreateCustomer?.addEventListener('click', async function () {
                await submitQuickCreateCustomer();
            });
            customerRewardModalEl?.addEventListener('shown.bs.modal', function () {

                const input = document.getElementById('rewardVoucherCount');

                if (!input) return;

                input.focus();

                setTimeout(() => {
                    input.select?.();
                }, 50);
            });

            btnClearCustomer?.addEventListener('click', async function () {
                await clearCustomerFromCurrentCart();
            });

            txtCustomerKeyword?.addEventListener('input', function () {
                const keyword = txtCustomerKeyword?.value || '';
                clearCustomerModalInlineError();
                debouncedSearchCustomers(keyword);
            });
            const rewardInput = document.getElementById('rewardVoucherCount');

            rewardInput?.addEventListener('focus', function () {
                setTimeout(() => this.select(), 10);
            });

            rewardInput?.addEventListener('mouseup', function (e) {
                e.preventDefault();
            });
            document.getElementById('useVoucherList')
                ?.addEventListener('change', function (e) {
                    if (e.target.classList.contains('use-voucher-check')) {
                        updateUseVoucherTotal();
                    }
                });

            document.getElementById('btnApplyRewardVouchers')
                ?.addEventListener('click', async function () {
                    await applySelectedRewardVouchers();
                });

            document.getElementById('btnClearAppliedVouchers')
                ?.addEventListener('click', async function () {
                    await clearAppliedRewardVouchers();
                });
            customerRewardModalEl?.addEventListener('shown.bs.modal', function () {

                rewardInput?.focus();

                setTimeout(() => {
                    rewardInput?.select();
                }, 50);
            });
            txtCustomerKeyword?.addEventListener('keydown', async function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    await searchCustomersImmediate();
                }
            });
            document.getElementById('rewardVoucherCount')
                ?.addEventListener('keydown', async function (e) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        await redeemRewardVoucher();
                    }
                });
            qcCustomerName?.addEventListener('input', clearQuickCreateInlineError);
            qcCustomerPhone?.addEventListener('input', clearQuickCreateInlineError);
            qcCustomerAddress?.addEventListener('input', clearQuickCreateInlineError);
            qcCustomerNote?.addEventListener('input', clearQuickCreateInlineError);

            customerSearchResult?.addEventListener('click', async function (e) {
                const item = e.target.closest('[data-customer-id]');
                if (!item) return;

                const customerId = parseInt(item.getAttribute('data-customer-id') || '0', 10);
                if (!customerId) return;

                await setCustomerToCurrentCart(customerId);
            });

            customerInfoBox?.addEventListener('click', async function (e) {
                const button = e.target.closest('[data-customer-action]');
                if (!button) return;

                const action = button.getAttribute('data-customer-action');

                if (action === 'open') {
                    openCustomerModal();
                    return;
                }

                if (action === 'reward') {
                    await openRewardModal();
                    return;
                }
                if (action === 'use-voucher') {
                    await openUseVoucherModal();
                    return;
                }

                if (action === 'clear') {
                    await clearCustomerFromCurrentCart();
                    return;
                }
            });
        }
        async function openUseVoucherModal() {
            const customerId = getCurrentCustomerId();

            if (!customerId) {
                showError('Vui lòng chọn khách hàng trước khi dùng voucher.');
                return;
            }

            const draft = getCurrentDraft();

            const nameEl = document.getElementById('useVoucherCustomerName');
            if (nameEl) {
                nameEl.textContent = draft?.customerName || draft?.customer?.name || 'Khách hàng';
            }

            const listBox = document.getElementById('useVoucherList');
            if (listBox) {
                listBox.innerHTML = `<div class="text-muted">Đang tải voucher...</div>`;
            }

            const totalEl = document.getElementById('useVoucherTotal');
            if (totalEl) {
                totalEl.textContent = window.PosCommon.formatMoney(0);
            }

            useRewardVoucherModal?.show();

            try {
                const items = await fetchJson(`/admin/api/customers/${customerId}/reward-vouchers/available`);

                useVoucherItems = Array.isArray(items) ? items : [];

                renderUseVoucherList(useVoucherItems);
            } catch (err) {
                if (listBox) {
                    listBox.innerHTML = `
                <div class="text-danger">
                    ${window.PosCommon.escapeHtml(err?.message || 'Không tải được voucher.')}
                </div>
            `;
                }
            }
        }
        function renderUseVoucherList(items) {
            const box = document.getElementById('useVoucherList');
            if (!box) return;

            const list = Array.isArray(items) ? items : [];
            useVoucherItems = list;

            if (!list.length) {
                box.innerHTML = `
            <div class="text-muted">
                Khách chưa có voucher khả dụng.
            </div>
        `;
                updateUseVoucherTotal();
                return;
            }

            const appliedIds = getAppliedRewardVoucherIds();

            box.innerHTML = list.map(function (x) {
                const id = Number(x.id || x.Id || 0);
                const code = x.voucherCode || x.VoucherCode || '';
                const value = Number(x.value || x.Value || 0);
                const desc = x.description || x.Description || 'Voucher tích điểm';
                const checked = appliedIds.includes(id) ? 'checked' : '';

                return `
            <label class="pos-use-voucher-item">
                <div class="pos-use-voucher-left">
                    <input type="checkbox"
                           class="form-check-input use-voucher-check"
                           value="${id}"
                           ${checked} />

                    <div>
                        <div class="pos-use-voucher-code">
                            ${window.PosCommon.escapeHtml(code)}
                        </div>
                        <div class="pos-use-voucher-desc">
                            ${window.PosCommon.escapeHtml(desc)}
                        </div>
                    </div>
                </div>

                <div class="pos-use-voucher-value">
                    ${window.PosCommon.formatMoney(value)}
                </div>
            </label>
        `;
            }).join('');

            updateUseVoucherTotal();
        }
        function getAppliedRewardVoucherIds() {
            const draft = getCurrentDraft();
            const applied = draft?.appliedRewardVouchers || draft?.AppliedRewardVouchers || [];

            return (Array.isArray(applied) ? applied : [])
                .map(x => Number(x.voucherId || x.VoucherId || 0))
                .filter(x => x > 0);
        }
    
        /* =========================================================
           UI LOCKS
        ========================================================= */
        function bindUiLocks() {
            registerUiLock(posState, {
                target: btnSearchCustomer,
                requireOnline: true,
                busyScopes: ['cartMutate', 'checkout'],
                offlineMessage: 'Đang offline, chưa thể tìm khách hàng.',
                busyMessage: 'POS đang bận xử lý đơn hàng.'
            });

            registerUiLock(posState, {
                target: btnClearCustomer,
                requireOnline: true,
                busyScopes: ['cartMutate', 'checkout'],
                pendingActions: ['customer:clearCurrent'],
                offlineMessage: 'Đang offline, chưa thể bỏ khách hàng khỏi giỏ.',
                busyMessage: 'POS đang bận xử lý giỏ hàng.',
                pendingMessage: 'Đang xử lý thao tác khách hàng.'
            });

            registerUiLock(posState, {
                target: btnSubmitQuickCreateCustomer,
                requireOnline: true,
                busyScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                pendingActions: ['customer:quickCreate'],
                offlineMessage: 'Đang offline, chưa thể tạo nhanh khách hàng.',
                busyMessage: 'POS đang bận xử lý thao tác khác.',
                pendingMessage: 'Đang tạo khách hàng.'
            });
        }

        bindUiLocks();

        return {
            openCustomerModal,
            searchCustomers,
            setCustomerToCurrentCart,
            clearCustomerFromCurrentCart,
            openQuickCreateCustomerModal,
            submitQuickCreateCustomer,
            bindEvents
        };
    }

    return {
        create
    };
})();