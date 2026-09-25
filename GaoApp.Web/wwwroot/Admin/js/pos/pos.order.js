window.PosOrder = (function () {
    function create(deps) {
        const {
            posState,
            elements,
            modals,
            helpers
        } = deps;

        const {
            btnNewCart,
            btnHoldCart,
            btnOpenPayment,
            btnFinalizeCart,
            btnCancelCart,

            txtHoldNote,
            btnConfirmHold,

            txtOrderNote,
            btnSaveOrderNote,

            txtOrderDiscount,
            btnSaveOrderDiscount,
            btnClearOrderDiscount,

            lineDiscountLineId,
            lineDiscountAmount,
            lineDiscountItemName,
            btnSaveLineDiscount,
            btnClearLineDiscount,

            qtyEditLineId,
            qtyEditValue,
            qtyEditItemName,
            btnSaveQtyEdit,

            confirmTitle,
            confirmMessage,
            confirmNoteBox,
            confirmNoteLabel,
            confirmNoteInput,
            btnConfirmAction,

            currentDraftBody,
            heldList
        } = elements;

        const {
            holdModal,
            confirmModal,
            lineDiscountModal,
            qtyEditModal
        } = modals;

        const {
            postJson,
            patchJson,
            deleteWithToken,
            runPosAction,
            focusBarcodeInput,
            syncDraftToUi,
            showSuccess,
            showError,
            formatMoney,
            openReceiptPrint,
            openPaymentModal,
            requestScreenRefresh,
            loadScreen,
            applyDraftActionSuccess,
            applyScreenActionSuccess
        } = helpers;

        const {
            setInlineError,
            clearInlineError,
            registerUiLock,
            refreshUiLocks
        } = window.PosCommon;

        let activeLineId = 0;
        let lastCartScrollTop = 0;

        function getCartScrollContainer() {
            return document.querySelector('.pos-cart-scroll');
        }

        function rememberCartScrollTop() {
            const container = getCartScrollContainer();
            if (!container) return;
            lastCartScrollTop = container.scrollTop || 0;
        }

        function restoreCartScrollTop() {
            const container = getCartScrollContainer();
            if (!container) return;
            container.scrollTop = lastCartScrollTop || 0;
        }

        function getLineRows() {
            return Array.from(currentDraftBody?.querySelectorAll('tr[data-line-id]') || []);
        }

        function getRowByLineId(lineId) {
            if (!currentDraftBody) return null;
            return currentDraftBody.querySelector(`tr[data-line-id="${Number(lineId || 0)}"]`);
        }

        function getActiveLineId() {
            return Number(activeLineId || 0);
        }

        function renderActiveLineState() {
            const rows = getLineRows();

            rows.forEach(function (row) {
                const rowLineId = Number(row.getAttribute('data-line-id') || '0');
                row.classList.toggle('is-active-line', rowLineId === activeLineId);
            });
        }

        function setActiveLineId(lineId) {
            activeLineId = Number(lineId || 0);
            renderActiveLineState();
        }

        function clearActiveLineId() {
            activeLineId = 0;
            renderActiveLineState();
        }

        function isModalShown(modalElement) {
            if (!modalElement) return false;
            return modalElement.classList.contains('show')
                || modalElement.getAttribute('aria-modal') === 'true'
                || modalElement.style.display === 'block';
        }

        function isConfirmModalOpen() {
            const modalEl = document.getElementById('confirmModal');
            return isModalShown(modalEl);
        }

        function isLineDiscountModalOpen() {
            const modalEl = document.getElementById('lineDiscountModal');
            return isModalShown(modalEl);
        }

        function isHoldModalOpen() {
            const modalEl = document.getElementById('holdModal');
            return isModalShown(modalEl);
        }

        function isQtyEditModalOpen() {
            const modalEl = document.getElementById('qtyEditModal');
            return isModalShown(modalEl);
        }

        function ensureActiveLineVisible() {
            if (!currentDraftBody || !activeLineId) return;

            const row = getRowByLineId(activeLineId);
            const container = getCartScrollContainer();

            if (!row || !container) return;

            const rowTop = row.offsetTop;
            const rowBottom = rowTop + row.offsetHeight;
            const viewTop = container.scrollTop;
            const viewBottom = viewTop + container.clientHeight;

            if (rowTop < viewTop) {
                container.scrollTop = rowTop - 12;
                return;
            }

            if (rowBottom > viewBottom) {
                container.scrollTop = rowBottom - container.clientHeight + 12;
            }
        }

        function moveActiveLine(step) {
            const rows = getLineRows();
            if (!rows.length) return;

            const currentIndex = rows.findIndex(function (row) {
                return Number(row.getAttribute('data-line-id') || '0') === getActiveLineId();
            });

            let nextIndex = 0;

            if (currentIndex < 0) {
                nextIndex = step > 0 ? 0 : rows.length - 1;
            } else {
                nextIndex = Math.max(0, Math.min(rows.length - 1, currentIndex + step));
            }

            const nextRow = rows[nextIndex];
            const nextLineId = Number(nextRow?.getAttribute('data-line-id') || '0');

            if (!nextLineId) return;

            setActiveLineId(nextLineId);
            ensureActiveLineVisible();
        }

        function reapplyActiveLineAfterDraftSync() {
            const rows = getLineRows();

            if (!rows.length) {
                clearActiveLineId();
                return;
            }

            const activeRowStillExists = rows.some(function (row) {
                return Number(row.getAttribute('data-line-id') || '0') === getActiveLineId();
            });

            if (!activeRowStillExists) {
                const firstLineId = Number(rows[0].getAttribute('data-line-id') || '0');
                activeLineId = firstLineId || 0;
            }

            restoreCartScrollTop();
            renderActiveLineState();
            ensureActiveLineVisible();
        }

        function getActiveLineData() {
            const draft =
                posState?.business?.currentDraft ||
                posState?.currentDraft ||
                null;

            const lineId = getActiveLineId();
            if (!draft || !lineId) return null;

            const lines = Array.isArray(draft.lines) ? draft.lines : [];
            return lines.find(x => Number(x?.lineId || 0) === lineId) || null;
        }

        function removeActiveLine() {
            const lineId = getActiveLineId();
            if (!lineId) return;
            removeLine(lineId);
        }

        function increaseActiveLineQty() {
            const lineId = getActiveLineId();
            if (!lineId) return;

            const input = currentDraftBody?.querySelector(`[data-qty-line-id="${lineId}"]`);
            const currentQty = parseFloat(input?.value || '1');
            const nextQty = currentQty + 1;

            if (input) {
                input.value = nextQty;
            }

            rememberCartScrollTop();
            updateLineQty(lineId, nextQty);
        }

        function decreaseActiveLineQty() {
            const lineId = getActiveLineId();
            if (!lineId) return;

            const input = currentDraftBody?.querySelector(`[data-qty-line-id="${lineId}"]`);
            const currentQty = parseFloat(input?.value || '1');
            const nextQty = Math.max(1, currentQty - 1);

            if (input) {
                input.value = nextQty;
            }

            rememberCartScrollTop();
            updateLineQty(lineId, nextQty);
        }
        function getLastTouchedLineId() {
            return Number(window.__posLastTouchedLineId || 0);
        }

        function openQtyEditModalForLastTouchedLine() {
            const lastLineId = getLastTouchedLineId();

            if (!lastLineId) {
                showError('Chưa có dòng vừa thêm để sửa số lượng.');
                return;
            }

            setActiveLineId(lastLineId);
            openQtyEditModalForActiveLine();
        }
        function openQtyEditModalForActiveLine() {
            const line = getActiveLineData();
            if (!line) {
                showError('Vui lòng chọn dòng hàng.');
                return;
            }

            if (!qtyEditLineId || !qtyEditItemName || !qtyEditValue) {
                showError('Popup chỉnh số lượng chưa được khởi tạo đúng.');
                console.error('Qty edit elements missing:', {
                    qtyEditLineId,
                    qtyEditItemName,
                    qtyEditValue
                });
                return;
            }

            qtyEditLineId.value = String(line.lineId || '');
            qtyEditItemName.textContent = line.productVariantName || line.productName || 'Sản phẩm';
            qtyEditValue.value = String(Number(line.quantity || 1));

            qtyEditModal?.show();

            setTimeout(() => {
                qtyEditValue?.focus?.();
                qtyEditValue?.select?.();
            }, 120);
        }
        function closeLineActionMenu(sourceElement) {
            const dropdown =
                sourceElement?.closest?.('.dropdown');

            const toggle =
                dropdown?.querySelector?.(
                    '[data-bs-toggle="dropdown"]'
                );

            if (
                !toggle ||
                !window.bootstrap?.Dropdown
            ) {
                return;
            }

            bootstrap.Dropdown
                .getOrCreateInstance(toggle)
                .hide();
        }
        function openLineDiscountModalForActiveLine() {
            const line = getActiveLineData();
            if (!line) {
                showError('Vui lòng chọn dòng hàng.');
                return;
            }

            const lineId = Number(line.lineId || 0);
            if (!lineId) {
                showError('Không xác định được dòng hàng.');
                return;
            }

            const itemName =
                String(line.productVariantName || '').trim() ||
                String(line.productName || '').trim() ||
                'Sản phẩm';

            const discountAmount = Number(line.discountAmount || 0);

            openLineDiscountModal(lineId, itemName, discountAmount);

            setTimeout(function () {
                lineDiscountAmount?.focus?.();
                lineDiscountAmount?.select?.();
            }, 80);
        }
        function openImagePreview(url, alt) {
            if (!url) return;

            const imgEl = document.getElementById('imagePreviewEl');
            if (!imgEl) return;

            imgEl.src = url;
            imgEl.alt = alt || 'Ảnh sản phẩm';

            const modalEl = document.getElementById('imagePreviewModal');
            if (!modalEl || !window.bootstrap) return;

            const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
            modal.show();
        }
        function showImageHoverPreview(url, alt, anchorEl) {
            if (!url || !anchorEl) return;

            const previewEl = document.getElementById('cartImageHoverPreview');
            const previewImgEl = document.getElementById('cartImageHoverPreviewImg');
            if (!previewEl || !previewImgEl) return;

            previewImgEl.src = url;
            previewImgEl.alt = alt || 'Ảnh sản phẩm';

            const rect = anchorEl.getBoundingClientRect();
            const previewWidth = 240;
            const gap = 12;

            let left = rect.right + gap;
            let top = rect.top;

            const maxLeft = window.innerWidth - previewWidth - 12;
            if (left > maxLeft) {
                left = rect.left - previewWidth - gap;
            }

            if (left < 12) left = 12;

            const maxTop = window.innerHeight - 252;
            if (top > maxTop) top = maxTop;
            if (top < 12) top = 12;

            previewEl.style.left = `${left}px`;
            previewEl.style.top = `${top}px`;
            previewEl.style.display = 'block';
        }

        function hideImageHoverPreview() {
            const previewEl = document.getElementById('cartImageHoverPreview');
            const previewImgEl = document.getElementById('cartImageHoverPreviewImg');

            if (previewEl) {
                previewEl.style.display = 'none';
            }

            if (previewImgEl) {
                previewImgEl.src = '';
                previewImgEl.alt = '';
            }
        }

        async function saveQtyEdit() {
            const lineId = Number(qtyEditLineId?.value || 0);
            const qty = Number(qtyEditValue?.value || 0);

            if (!lineId || qty <= 0) {
                showError('Số lượng không hợp lệ');
                return;
            }

            rememberCartScrollTop();
            setActiveLineId(lineId);

            const result = await updateLineQty(lineId, qty);

            if (result !== false) {
                qtyEditModal?.hide();
            }
        }

        function ensureHoldErrorBox() {
            const modalEl = document.getElementById('holdModal');
            if (!modalEl) return null;

            let box = modalEl.querySelector('#holdInlineErrorBox');
            if (box) return box;

            box = document.createElement('div');
            box.id = 'holdInlineErrorBox';
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

        function ensureOrderFormErrorBox() {
            const anchor =
                txtOrderNote?.closest('.card, .pos-panel, .col, .row, form') ||
                txtOrderNote?.parentElement;

            if (!anchor) return null;

            let box = document.getElementById('orderFormInlineErrorBox');
            if (box) return box;

            box = document.createElement('div');
            box.id = 'orderFormInlineErrorBox';
            box.style.display = 'none';

            if (anchor.firstChild) {
                anchor.insertBefore(box, anchor.firstChild);
            } else {
                anchor.appendChild(box);
            }

            return box;
        }

        function ensureLineDiscountErrorBox() {
            const modalEl = document.getElementById('lineDiscountModal');
            if (!modalEl) return null;

            let box = modalEl.querySelector('#lineDiscountInlineErrorBox');
            if (box) return box;

            box = document.createElement('div');
            box.id = 'lineDiscountInlineErrorBox';
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

        function clearHoldInlineError() {
            clearInlineError(ensureHoldErrorBox());
        }

        function showHoldInlineError(message, variant) {
            setInlineError(ensureHoldErrorBox(), message, variant || 'danger');
        }

        function clearOrderFormInlineError() {
            clearInlineError(ensureOrderFormErrorBox());
        }

        function showOrderFormInlineError(message, variant) {
            setInlineError(ensureOrderFormErrorBox(), message, variant || 'danger');
        }

        function clearLineDiscountInlineError() {
            clearInlineError(ensureLineDiscountErrorBox());
        }

        function showLineDiscountInlineError(message, variant) {
            setInlineError(ensureLineDiscountErrorBox(), message, variant || 'danger');
        }

        function refreshLocksSafe() {
            refreshUiLocks?.(posState);
        }

        function applyDraftSuccess(options) {
            const originalAfterSync =
                typeof options?.afterSync === 'function'
                    ? options.afterSync
                    : null;

            return applyDraftActionSuccess({
                draft: options?.draft,
                posState,
                syncDraftToUi,
                showSuccess,
                focusBarcodeInput,
                successMessage: options?.successMessage || '',
                focusBarcode: options?.focusBarcode !== false,
                afterSync: function (draft) {
                    if (originalAfterSync) {
                        originalAfterSync(draft);
                    }

                    requestAnimationFrame(function () {
                        reapplyActiveLineAfterDraftSync();
                    });
                }
            });
        }

        async function applyScreenSuccess(options) {
            await applyScreenActionSuccess({
                requestScreenRefresh,
                showSuccess,
                successMessage: options?.successMessage || '',
                reason: options?.reason || 'screen-action-success',
                silent: !!options?.silent,
                force: options?.force !== false,
                focusBarcode: options?.focusBarcode !== false,
                beforeRefresh: options?.beforeRefresh || null,
                afterSuccess: options?.afterSuccess || null
            });
        }

        async function createNewCart() {
            const result = await runPosAction(
                posState,
                'order:createNewCart',
                async function () {
                    return await postJson('/admin/pos/cart/current/new', {
                        customerId: null,
                        note: null
                    });
                },
                {
                    button: btnNewCart,
                    busyText: 'Đang tạo giỏ...',
                    fallbackMessage: 'Không thể tạo giỏ mới.',
                    scopes: ['cartMutate'],
                    conflictScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                    blockedMessage: 'POS đang xử lý thanh toán hoặc thao tác giỏ khác, chưa thể tạo giỏ mới.',
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể tạo giỏ mới.',
                    offlineDisplayMode: 'toast',
                    displayMode: undefined,
                    onSuccess: async function (data) {
                        await applyScreenSuccess({
                            successMessage: data?.message || 'Đã sẵn sàng giỏ mới',
                            reason: 'order-create-new-cart',
                            silent: false,
                            force: true,
                            focusBarcode: true
                        });
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );

            return result;
        }

        function openHoldModal() {
            if (txtHoldNote) {
                txtHoldNote.value = '';
            }

            clearHoldInlineError();
            holdModal?.show();
        }

        async function holdCurrentCart() {
            clearHoldInlineError();

            return await runPosAction(
                posState,
                'order:holdCurrentCart',
                async function () {
                    return await postJson('/admin/pos/cart/current/hold', {
                        holdNote: (txtHoldNote?.value || '').trim() || null
                    });
                },
                {
                    button: btnConfirmHold,
                    busyText: 'Đang giữ đơn...',
                    fallbackMessage: 'Không thể giữ đơn.',
                    scopes: ['cartMutate', 'modalSubmit'],
                    conflictScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                    blockedMessage: 'POS đang xử lý thao tác khác, chưa thể giữ đơn lúc này.',
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể giữ đơn hiện tại.',
                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',
                    inlineTarget: ensureHoldErrorBox(),
                    clearInlineOnStart: true,
                    onSuccess: async function (result) {
                        clearHoldInlineError();

                        await applyScreenSuccess({
                            successMessage: result?.message || 'Đã giữ đơn',
                            reason: 'order-hold-current-cart',
                            silent: false,
                            force: true,
                            focusBarcode: true,
                            beforeRefresh: async function () {
                                holdModal?.hide();
                            }
                        });
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );
        }

        function openConfirmModal(options) {
            if (confirmTitle) {
                confirmTitle.textContent = options?.title || 'Xác nhận thao tác';
            }

            if (confirmMessage) {
                confirmMessage.textContent =
                    options?.message || 'Bạn có chắc chắn muốn thực hiện thao tác này?';
            }

            if (btnConfirmAction) {
                btnConfirmAction.onclick = async function () {
                    try {
                        const action = posState?.ui?.confirm?.pendingAction;

                        if (typeof action === 'function') {
                            await action();
                        }
                    } catch (err) {
                        console.error('Confirm action error:', err);
                    } finally {
                        if (posState?.ui?.confirm) {
                            posState.ui.confirm.pendingAction = null;
                        }
                    }
                };
            }

            if (options?.showNote) {
                if (confirmNoteBox) confirmNoteBox.style.display = '';
                if (confirmNoteLabel) {
                    confirmNoteLabel.textContent = options.noteLabel || 'Nội dung';
                }
                if (confirmNoteInput) {
                    confirmNoteInput.value = options.noteValue || '';
                    confirmNoteInput.placeholder = options.notePlaceholder || '';
                }
            } else {
                if (confirmNoteBox) confirmNoteBox.style.display = 'none';
                if (confirmNoteInput) confirmNoteInput.value = '';
            }

            posState.pendingConfirmAction = options?.onConfirm || null;
            confirmModal?.show();

            if (options?.showNote) {
                setTimeout(() => confirmNoteInput?.focus(), 150);
            }
        }

        async function finalizeCurrentCart() {
            openConfirmModal({
                title: 'Chốt đơn hiện tại',
                message: 'Xác nhận chốt đơn và in hóa đơn cho giỏ hiện tại?',
                confirmText: 'Chốt đơn',
                confirmClass: 'btn-success',
                onConfirm: async () => {
                    await runPosAction(
                        posState,
                        'order:finalizeCurrentCart',
                        async function () {
                            return await postJson('/admin/pos/cart/current/finalize', {});
                        },
                        {
                            button: btnConfirmAction,
                            busyText: 'Đang chốt đơn...',
                            fallbackMessage: 'Không thể chốt đơn.',
                            scopes: ['checkout', 'modalSubmit'],
                            conflictScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                            blockedMessage: 'Giỏ đang được cập nhật hoặc thanh toán ở thao tác khác, chưa thể chốt đơn.',
                            requireOnline: true,
                            offlineMessage: 'Đang offline, chưa thể chốt đơn.',
                            offlineDisplayMode: 'toast',
                            displayMode: 'toast',
                            onSuccess: async function (data) {
                                await applyScreenSuccess({
                                    successMessage: data?.message || 'Đã chốt đơn',
                                    reason: 'order-finalize-current-cart',
                                    silent: false,
                                    force: true,
                                    focusBarcode: true,
                                    beforeRefresh: async function () {
                                        confirmModal?.hide();

                                        if (data?.orderId) {
                                            openReceiptPrint(data.orderId, '80', true);
                                        }
                                    }
                                });
                            },
                            onFinally: function () {
                                refreshLocksSafe();
                            }
                        }
                    );
                }
            });
        }

        async function cancelCurrentCart() {
            openConfirmModal({
                title: 'Hủy giỏ hiện tại',
                message: 'Giỏ hiện tại sẽ bị hủy. Bạn có chắc chắn muốn tiếp tục?',
                confirmText: 'Hủy giỏ',
                confirmClass: 'btn-danger',
                onConfirm: async () => {
                    await runPosAction(
                        posState,
                        'order:cancelCurrentCart',
                        async function () {
                            return await postJson('/admin/pos/cart/current/cancel', {
                                reason: null
                            });
                        },
                        {
                            button: btnConfirmAction,
                            busyText: 'Đang hủy giỏ...',
                            fallbackMessage: 'Không thể hủy giỏ hiện tại.',
                            scopes: ['cartMutate', 'modalSubmit'],
                            conflictScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                            blockedMessage: 'POS đang xử lý thanh toán hoặc thao tác giỏ khác, chưa thể hủy giỏ.',
                            requireOnline: true,
                            offlineMessage: 'Đang offline, chưa thể hủy giỏ hiện tại.',
                            offlineDisplayMode: 'toast',
                            displayMode: 'toast',
                            onSuccess: async function () {
                                await applyScreenSuccess({
                                    successMessage: 'Đã hủy giỏ hiện tại',
                                    reason: 'order-cancel-current-cart',
                                    silent: false,
                                    force: true,
                                    focusBarcode: true,
                                    beforeRefresh: async function () {
                                        confirmModal?.hide();
                                    }
                                });
                            },
                            onFinally: function () {
                                refreshLocksSafe();
                            }
                        }
                    );
                }
            });
        }

        async function resumeHeld(orderId) {
            const parsedOrderId = parseInt(orderId || '0', 10);
            if (!parsedOrderId) {
                showError('Không xác định được đơn cần mở lại.');
                return;
            }

            return await runPosAction(
                posState,
                `order:resumeHeld:${parsedOrderId}`,
                async function () {
                    return await postJson(`/admin/pos/orders/${parsedOrderId}/resume`, {});
                },
                {
                    fallbackMessage: 'Không thể mở lại đơn đang giữ.',
                    scopes: ['cartMutate'],
                    conflictScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                    displayMode: 'toast',
                    onSuccess: async function () {
                        const heldOrdersModalEl = document.getElementById('heldOrdersModal');
                        const heldOrdersModal = heldOrdersModalEl
                            ? bootstrap.Modal.getInstance(heldOrdersModalEl)
                            : null;

                        heldOrdersModal?.hide();

                        await applyScreenSuccess({
                            successMessage: `Đã lấy lại đơn #${parsedOrderId}`,
                            reason: 'order-resume-held',
                            silent: false,
                            force: true,
                            focusBarcode: true
                        });
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );
        }

        async function saveCurrentCartNote() {
            clearOrderFormInlineError();

            const note = (txtOrderNote?.value || '').trim();

            return await runPosAction(
                posState,
                'order:saveNote',
                async function () {
                    return await postJson('/admin/pos/cart/current/note', {
                        note: note || null
                    });
                },
                {
                    button: btnSaveOrderNote,
                    busyText: 'Đang lưu...',
                    fallbackMessage: 'Không thể lưu ghi chú đơn.',
                    scopes: ['cartMutate'],
                    requireOnline: true,
                    offlineMessage: 'Đang offline. Ghi chú vẫn giữ trên ô nhập, nhưng chưa thể lưu xuống giỏ.',
                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',
                    inlineTarget: ensureOrderFormErrorBox(),
                    clearInlineOnStart: true,
                    onSuccess: function (draft) {
                        clearOrderFormInlineError();

                        applyDraftSuccess({
                            draft,
                            successMessage: 'Đã lưu ghi chú đơn',
                            focusBarcode: true
                        });
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );
        }

        async function saveCurrentCartDiscount() {
            clearOrderFormInlineError();

            const discountAmount = parseFloat(txtOrderDiscount?.value || '0');

            if (Number.isNaN(discountAmount) || discountAmount < 0) {
                showOrderFormInlineError('Giảm giá đơn không hợp lệ.', 'warning');
                txtOrderDiscount?.focus();
                return;
            }

            return await runPosAction(
                posState,
                'order:saveDiscount',
                async function () {
                    return await postJson('/admin/pos/cart/current/discount', {
                        discountAmount: discountAmount
                    });
                },
                {
                    button: btnSaveOrderDiscount,
                    busyText: 'Đang lưu...',
                    fallbackMessage: 'Không thể cập nhật giảm giá đơn.',
                    scopes: ['cartMutate'],
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể cập nhật giảm giá đơn.',
                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',
                    inlineTarget: ensureOrderFormErrorBox(),
                    clearInlineOnStart: true,
                    onSuccess: function (draft) {
                        clearOrderFormInlineError();

                        applyDraftSuccess({
                            draft,
                            successMessage: 'Đã cập nhật giảm giá đơn',
                            focusBarcode: true
                        });
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );
        }

        async function clearCurrentCartDiscount() {
            clearOrderFormInlineError();

            if (txtOrderDiscount) {
                txtOrderDiscount.value = 0;
            }

            await saveCurrentCartDiscount();
        }

        function openLineDiscountModal(lineId, itemName, discountAmount) {
            if (lineDiscountLineId) {
                lineDiscountLineId.value = lineId || '';
            }

            if (lineDiscountItemName) {
                lineDiscountItemName.textContent = itemName || 'Sản phẩm';
            }

            if (lineDiscountAmount) {
                lineDiscountAmount.value = discountAmount || 0;
            }

            clearLineDiscountInlineError();
            lineDiscountModal?.show();
        }

        async function saveLineDiscount() {
            clearLineDiscountInlineError();

            const lineId = parseInt(lineDiscountLineId?.value || '0', 10);
            const discountAmount = parseFloat(lineDiscountAmount?.value || '0');

            if (!lineId) {
                showLineDiscountInlineError('Không xác định được dòng hàng.', 'warning');
                return;
            }

            if (Number.isNaN(discountAmount) || discountAmount < 0) {
                showLineDiscountInlineError('Giảm giá dòng không hợp lệ.', 'warning');
                lineDiscountAmount?.focus();
                return;
            }

            return await runPosAction(
                posState,
                `order:lineDiscount:${lineId}`,
                async function () {
                    return await postJson(`/admin/pos/lines/${lineId}/discount`, {
                        discountAmount: discountAmount
                    });
                },
                {
                    button: btnSaveLineDiscount,
                    busyText: 'Đang lưu...',
                    fallbackMessage: 'Không thể cập nhật giảm giá dòng.',
                    scopes: ['cartMutate', 'modalSubmit'],
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể cập nhật giảm giá dòng.',
                    offlineDisplayMode: 'inline',
                    displayMode: 'inline',
                    inlineTarget: ensureLineDiscountErrorBox(),
                    clearInlineOnStart: true,
                    onSuccess: function (draft) {
                        clearLineDiscountInlineError();

                        applyDraftSuccess({
                            draft,
                            successMessage: 'Đã cập nhật giảm giá dòng',
                            focusBarcode: true,
                            afterSync: function () {
                                lineDiscountModal?.hide();
                            }
                        });
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );
        }

        async function clearLineDiscount() {
            clearLineDiscountInlineError();

            if (lineDiscountAmount) {
                lineDiscountAmount.value = 0;
            }

            await saveLineDiscount();
        }

        async function updateLineQty(lineId, qty) {
            clearOrderFormInlineError();

            const parsedLineId = Number(lineId || 0);
            const parsedQty = Number(qty || 0);

            if (!parsedLineId) {
                showOrderFormInlineError('Không xác định được dòng hàng.', 'warning');
                return false;
            }

            if (Number.isNaN(parsedQty) || parsedQty <= 0) {
                showOrderFormInlineError('Số lượng phải lớn hơn 0.', 'warning');
                return false;
            }

            const result = await runPosAction(
                posState,
                `order:updateLineQty:${parsedLineId}`,
                async function () {
                    return await patchJson(`/admin/pos/lines/${parsedLineId}?qty=${parsedQty}`);
                },
                {
                    fallbackMessage: 'Không thể cập nhật số lượng.',
                    scopes: ['cartMutate'],
                    requireOnline: true,
                    offlineMessage: 'Đang offline, chưa thể cập nhật số lượng sản phẩm.',
                    offlineDisplayMode: 'toast',
                    displayMode: 'toast',
                    onSuccess: function (draft) {
                        clearOrderFormInlineError();

                        applyDraftSuccess({
                            draft,
                            successMessage: '',
                            focusBarcode: false
                        });
                    },
                    onFinally: function () {
                        refreshLocksSafe();
                    }
                }
            );

            return !!result;
        }

        async function removeLine(lineId) {
            openConfirmModal({
                title: 'Xóa dòng hàng',
                message: 'Bạn có chắc chắn muốn xóa dòng hàng này khỏi giỏ?',
                confirmText: 'Xóa dòng',
                confirmClass: 'btn-danger',
                onConfirm: async () => {
                    await runPosAction(
                        posState,
                        `order:removeLine:${lineId}`,
                        async function () {
                            return await deleteWithToken(`/admin/pos/lines/${lineId}`);
                        },
                        {
                            button: btnConfirmAction,
                            busyText: 'Đang xóa...',
                            fallbackMessage: 'Không thể xóa dòng hàng.',
                            scopes: ['cartMutate', 'modalSubmit'],
                            requireOnline: true,
                            offlineMessage: 'Đang offline, chưa thể xóa dòng hàng.',
                            offlineDisplayMode: 'toast',
                            displayMode: 'toast',
                            onSuccess: function (draft) {
                                const removedLineId = Number(lineId || 0);

                                applyDraftSuccess({
                                    draft,
                                    successMessage: '',
                                    focusBarcode: true,
                                    afterSync: function () {
                                        if (getActiveLineId() === removedLineId) {
                                            activeLineId = 0;
                                        }

                                        confirmModal?.hide();
                                    }
                                });
                            },
                            onFinally: function () {
                                refreshLocksSafe();
                            }
                        }
                    );
                }
            });
        }

        function bindUiLocks() {
            registerUiLock(posState, {
                target: btnNewCart,
                requireOnline: true,
                busyScopes: ['cartMutate', 'checkout'],
                pendingActions: ['order:createNewCart', 'order:holdCurrentCart', 'order:finalizeCurrentCart', 'payment:finalizeFromModal', 'order:cancelCurrentCart'],
                offlineMessage: 'Đang offline, chưa thể tạo giỏ mới.',
                busyMessage: 'POS đang bận xử lý giỏ hoặc thanh toán.',
                pendingMessage: 'Đang tạo giỏ mới.'
            });

            registerUiLock(posState, {
                target: btnHoldCart,
                requireOnline: true,
                busyScopes: ['cartMutate', 'checkout', 'modalSubmit'],
                pendingActions: ['order:holdCurrentCart', 'order:finalizeCurrentCart', 'payment:finalizeFromModal'],
                offlineMessage: 'Đang offline, chưa thể giữ đơn.',
                busyMessage: 'POS đang bận xử lý giỏ hàng.',
                pendingMessage: 'Đang giữ đơn.'
            });

            registerUiLock(posState, {
                target: btnOpenPayment,
                requireOnline: true,
                busyScopes: ['checkout', 'cartMutate'],
                offlineMessage: 'Đang offline, chưa thể mở thanh toán.',
                busyMessage: 'POS đang bận xử lý giỏ hàng.'
            });

            registerUiLock(posState, {
                target: btnFinalizeCart,
                requireOnline: true,
                busyScopes: ['checkout', 'cartMutate', 'modalSubmit'],
                pendingActions: ['order:holdCurrentCart', 'order:finalizeCurrentCart', 'payment:finalizeFromModal', 'order:cancelCurrentCart'],
                offlineMessage: 'Đang offline, chưa thể chốt đơn.',
                busyMessage: 'POS đang bận xử lý thanh toán hoặc cập nhật giỏ.',
                pendingMessage: 'Đơn đang được chốt.'
            });

            registerUiLock(posState, {
                target: btnCancelCart,
                requireOnline: true,
                busyScopes: ['checkout', 'cartMutate', 'modalSubmit'],
                pendingActions: ['order:holdCurrentCart', 'order:finalizeCurrentCart', 'payment:finalizeFromModal', 'order:cancelCurrentCart'],
                offlineMessage: 'Đang offline, chưa thể hủy giỏ.',
                busyMessage: 'POS đang bận xử lý thao tác khác.',
                pendingMessage: 'Đang hủy giỏ.'
            });

            registerUiLock(posState, {
                target: btnSaveOrderNote,
                requireOnline: true,
                busyScopes: ['cartMutate'],
                pendingActions: ['order:saveNote'],
                offlineMessage: 'Đang offline, chưa thể lưu ghi chú đơn.',
                busyMessage: 'POS đang bận xử lý giỏ hàng.',
                pendingMessage: 'Đang lưu ghi chú.'
            });

            registerUiLock(posState, {
                target: btnSaveOrderDiscount,
                requireOnline: true,
                busyScopes: ['cartMutate'],
                pendingActions: ['order:saveDiscount'],
                offlineMessage: 'Đang offline, chưa thể lưu giảm giá đơn.',
                busyMessage: 'POS đang bận xử lý giỏ hàng.',
                pendingMessage: 'Đang lưu giảm giá.'
            });

            registerUiLock(posState, {
                target: btnClearOrderDiscount,
                requireOnline: true,
                busyScopes: ['cartMutate'],
                pendingActions: ['order:saveDiscount'],
                offlineMessage: 'Đang offline, chưa thể xóa giảm giá đơn.',
                busyMessage: 'POS đang bận xử lý giỏ hàng.',
                pendingMessage: 'Đang cập nhật giảm giá.'
            });

            registerUiLock(posState, {
                target: btnSaveLineDiscount,
                requireOnline: true,
                busyScopes: ['cartMutate', 'modalSubmit'],
                pendingActions: [],
                offlineMessage: 'Đang offline, chưa thể lưu giảm giá dòng.',
                busyMessage: 'POS đang bận xử lý giỏ hàng.'
            });
        }

        function bindEvents() {
            ensureHoldErrorBox();
            ensureOrderFormErrorBox();
            ensureLineDiscountErrorBox();
            bindUiLocks();

            btnNewCart?.addEventListener('click', createNewCart);
            btnHoldCart?.addEventListener('click', openHoldModal);
            btnOpenPayment?.addEventListener('click', openPaymentModal);
            btnFinalizeCart?.addEventListener('click', finalizeCurrentCart);
            btnCancelCart?.addEventListener('click', cancelCurrentCart);

            btnSaveOrderNote?.addEventListener('click', saveCurrentCartNote);
            btnSaveOrderDiscount?.addEventListener('click', saveCurrentCartDiscount);
            btnClearOrderDiscount?.addEventListener('click', clearCurrentCartDiscount);

            btnConfirmHold?.addEventListener('click', holdCurrentCart);

            btnConfirmAction?.addEventListener('click', async function () {
                if (typeof posState.pendingConfirmAction === 'function') {
                    await posState.pendingConfirmAction();
                }
            });

            btnSaveLineDiscount?.addEventListener('click', saveLineDiscount);
            btnClearLineDiscount?.addEventListener('click', clearLineDiscount);
            btnSaveQtyEdit?.addEventListener('click', async function () {
                await saveQtyEdit();
            });

            txtOrderNote?.addEventListener('input', clearOrderFormInlineError);
            txtOrderDiscount?.addEventListener('input', clearOrderFormInlineError);
            txtHoldNote?.addEventListener('input', clearHoldInlineError);
            lineDiscountAmount?.addEventListener('input', clearLineDiscountInlineError);

            document.addEventListener('keydown', async function (e) {
                const activeElement = document.activeElement;

                if (isQtyEditModalOpen()) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        await saveQtyEdit();
                        return;
                    }

                    if (e.key === 'Escape') {
                        e.preventDefault();
                        qtyEditModal?.hide();
                        return;
                    }

                    return;
                }

                const activeTag = String(activeElement?.tagName || '').toLowerCase();

                const isTypingContext =
                    activeTag === 'input' ||
                    activeTag === 'textarea' ||
                    activeElement?.isContentEditable === true;

                const isNoteFocused = activeElement === txtOrderNote;
                if (isNoteFocused && e.ctrlKey && e.key.toLowerCase() === 's') {
                    e.preventDefault();
                    await saveCurrentCartNote();
                    return;
                }

                if (isConfirmModalOpen()) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        e.stopPropagation();

                        if (!btnConfirmAction || btnConfirmAction.disabled) {
                            return;
                        }

                        btnConfirmAction.click();
                        return;
                    }

                    if (e.key === 'Escape') {
                        e.preventDefault();
                        e.stopPropagation();
                        confirmModal?.hide();
                        return;
                    }

                    return;
                }

                if (isLineDiscountModalOpen()) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        e.stopPropagation();

                        if (!btnSaveLineDiscount || btnSaveLineDiscount.disabled) {
                            return;
                        }

                        btnSaveLineDiscount.click();
                        return;
                    }

                    if (e.key === 'Escape') {
                        e.preventDefault();
                        e.stopPropagation();
                        lineDiscountModal?.hide();
                        return;
                    }

                    if (e.key === 'Delete' && !isTypingContext) {
                        e.preventDefault();
                        e.stopPropagation();

                        if (!btnClearLineDiscount || btnClearLineDiscount.disabled) {
                            return;
                        }

                        btnClearLineDiscount.click();
                        return;
                    }

                    return;
                }

                const anyOtherModalOpen =
                    isHoldModalOpen() ||
                    posState?.ui?.modals?.payment ||
                    posState?.ui?.modals?.customer ||
                    posState?.ui?.modals?.quickCreateCustomer;

                if (anyOtherModalOpen) {
                    if (isHoldModalOpen() && e.key === 'Escape') {
                        e.preventDefault();
                        e.stopPropagation();
                        holdModal?.hide();
                    }
                    return;
                }

                if (e.key === 'Delete' && !isTypingContext) {
                    if (!getActiveLineId()) return;
                    e.preventDefault();
                    removeActiveLine();
                    return;
                }


                if ((e.key === '+' || e.key === '=') && !isTypingContext) {
                    e.preventDefault();
                    increaseActiveLineQty();
                    return;
                }

                if ((e.key === '-' || e.key === '_') && !isTypingContext) {
                    e.preventDefault();
                    decreaseActiveLineQty();
                    return;
                }

                if (e.key === 'ArrowDown' && !isTypingContext) {
                    e.preventDefault();
                    moveActiveLine(1);
                    return;
                }

                if (e.key === 'ArrowUp' && !isTypingContext) {
                    e.preventDefault();
                    moveActiveLine(-1);
                    return;
                }
            });

            document.addEventListener('pos:open-active-line-qty-editor', function () {
                const anyOtherModalOpen =
                    isConfirmModalOpen() ||
                    isLineDiscountModalOpen() ||
                    isHoldModalOpen() ||
                    isQtyEditModalOpen() ||
                    posState?.ui?.modals?.payment ||
                    posState?.ui?.modals?.customer ||
                    posState?.ui?.modals?.quickCreateCustomer;

                if (anyOtherModalOpen) {
                    return;
                }

                const lastLineId = getLastTouchedLineId();

                if (lastLineId) {
                    openQtyEditModalForLastTouchedLine();
                    return;
                }

                if (!getActiveLineId()) {
                    showError('Vui lòng chọn dòng hàng.');
                    return;
                }

                openQtyEditModalForActiveLine();
            });
            document.addEventListener('pos:open-active-line-discount-editor', function () {
                const anyOtherModalOpen =
                    isConfirmModalOpen() ||
                    isLineDiscountModalOpen() ||
                    isHoldModalOpen() ||
                    isQtyEditModalOpen() ||
                    posState?.ui?.modals?.payment ||
                    posState?.ui?.modals?.customer ||
                    posState?.ui?.modals?.quickCreateCustomer;

                if (anyOtherModalOpen) {
                    return;
                }

                if (!getActiveLineId()) {
                    showError('Vui lòng chọn dòng hàng.');
                    return;
                }

                openLineDiscountModalForActiveLine();
            });

            const confirmModalEl = document.getElementById('confirmModal');
            confirmModalEl?.addEventListener('hidden.bs.modal', function () {
                posState.pendingConfirmAction = null;

                if (confirmNoteInput) {
                    confirmNoteInput.value = '';
                }
            });

            const qtyEditModalEl = document.getElementById('qtyEditModal');
            qtyEditModalEl?.addEventListener('shown.bs.modal', function () {
                posState.qtyEditModalOpen = true;

                setTimeout(function () {
                    qtyEditValue?.focus?.();
                    qtyEditValue?.select?.();
                }, 50);
            });

            qtyEditModalEl?.addEventListener('hidden.bs.modal', function () {
                posState.qtyEditModalOpen = false;

                if (qtyEditLineId) qtyEditLineId.value = '';
                if (qtyEditValue) qtyEditValue.value = '';

                setTimeout(() => focusBarcodeInput?.(), 50);
            });


            currentDraftBody?.addEventListener('focusin', function (e) {
                const qtyInput = e.target.closest('[data-qty-line-id]');
                if (!qtyInput) return;

                const lineId = Number(qtyInput.getAttribute('data-qty-line-id') || '0');
                if (lineId > 0) {
                    setActiveLineId(lineId);
                    ensureActiveLineVisible();
                }

                setTimeout(function () {
                    qtyInput.select?.();
                }, 0);
            });
            currentDraftBody?.addEventListener('mouseover', function (e) {
                const thumb = e.target.closest('.pos-line-thumb-wrap');
                if (!thumb) return;

                const url = thumb.getAttribute('data-image-url') || '';
                const alt = thumb.querySelector('img')?.alt || '';

                showImageHoverPreview(url, alt, thumb);
            });

            currentDraftBody?.addEventListener('mouseout', function (e) {
                const thumb = e.target.closest('.pos-line-thumb-wrap');
                if (!thumb) return;

                const related = e.relatedTarget;
                if (related && thumb.contains(related)) {
                    return;
                }

                hideImageHoverPreview();
            });
            
            currentDraftBody?.addEventListener('click', function (e) {
                const priceBtn = e.target.closest('.pos-line-price-btn');
                if (priceBtn) {
                    e.preventDefault();
                    e.stopPropagation();

                    const lineId = Number(priceBtn.getAttribute('data-price-line-id') || 0);
                    if (!lineId) return;
                    closeLineActionMenu(priceBtn);
                    setActiveLineId(lineId);

                    const draft =
                        posState?.business?.currentDraft ||
                        posState?.currentDraft ||
                        null;

                    const line = Array.isArray(draft?.lines)
                        ? draft.lines.find(x => Number(x.lineId || 0) === lineId)
                        : null;

                    if (!line) {
                        showError('Không tìm thấy dòng hàng để xem bảng giá.');
                        return;
                    }

                    const box = document.getElementById('linePriceTableContent');
                    const modalEl = document.getElementById('linePriceTableModal');

                    if (!box || !modalEl || !window.bootstrap) {
                        showError('Popup bảng giá chưa được khởi tạo.');
                        return;
                    }

                    box.innerHTML = window.PosRender.buildLinePriceTableHtml(line);

                    bootstrap.Modal
                        .getOrCreateInstance(modalEl)
                        .show();

                    return;
                }
                const row = e.target.closest('tr[data-line-id]');
                if (row) {
                    const lineId = Number(row.getAttribute('data-line-id') || '0');
                    if (lineId > 0) {
                        setActiveLineId(lineId);
                    }
                }
                const thumb = e.target.closest('.pos-line-thumb-wrap');
                if (thumb) {
                    e.preventDefault();
                    e.stopPropagation();

                    const url = thumb.getAttribute('data-image-url') || '';
                    const alt = thumb.querySelector('img')?.alt || '';
                    hideImageHoverPreview();
                    openImagePreview(url, alt);
                    return;
                }
            }, true);
            getCartScrollContainer()?.addEventListener('scroll', function () {
                hideImageHoverPreview();
            });

            window.addEventListener('blur', function () {
                hideImageHoverPreview();
            });

            currentDraftBody?.addEventListener('dblclick', function (e) {
                const row = e.target.closest('tr[data-line-id]');
                if (!row) return;

                const lineId = Number(row.getAttribute('data-line-id') || '0');
                if (lineId > 0) {
                    setActiveLineId(lineId);
                    openQtyEditModalForActiveLine();
                }
            });

            lineDiscountAmount?.addEventListener('keydown', async function (e) {
                const isLineDiscountOpen = posState?.ui?.modals?.lineDiscount === true;
                if (!isLineDiscountOpen) return;

                if (e.key === 'Enter') {
                    e.preventDefault();
                    await saveLineDiscount();
                    return;
                }

                if (e.key === 'Escape') {
                    e.preventDefault();
                    lineDiscountModal?.hide();
                    return;
                }

                if (e.key === 'Delete') {
                    e.preventDefault();
                    await clearLineDiscount();
                }
            });

            currentDraftBody?.addEventListener('click', function (e) {
                const qtyEditBtn =
                    e.target.closest(
                        '[data-open-qty-line-id]'
                    );

                if (qtyEditBtn) {
                    e.preventDefault();

                    const lineId = parseInt(
                        qtyEditBtn.getAttribute(
                            'data-open-qty-line-id'
                        ) || '0',
                        10
                    );

                    if (lineId > 0) {
                        closeLineActionMenu(qtyEditBtn);
                        setActiveLineId(lineId);
                        openQtyEditModalForActiveLine();
                    }

                    return;
                }
                const decBtn = e.target.closest('[data-dec-line-id]');
                if (decBtn) {
                    const lineId = parseInt(decBtn.getAttribute('data-dec-line-id') || '0', 10);
                    setActiveLineId(lineId);

                    const input = currentDraftBody.querySelector(`[data-qty-line-id="${lineId}"]`);
                    const currentQty = parseFloat(input?.value || '1');
                    const nextQty = Math.max(1, currentQty - 1);

                    if (input) {
                        input.value = nextQty;
                    }

                    rememberCartScrollTop();
                    updateLineQty(lineId, nextQty);
                    return;
                }

                const incBtn = e.target.closest('[data-inc-line-id]');
                if (incBtn) {
                    const lineId = parseInt(incBtn.getAttribute('data-inc-line-id') || '0', 10);
                    setActiveLineId(lineId);

                    const input = currentDraftBody.querySelector(`[data-qty-line-id="${lineId}"]`);
                    const currentQty = parseFloat(input?.value || '1');
                    const nextQty = currentQty + 1;

                    if (input) {
                        input.value = nextQty;
                    }

                    rememberCartScrollTop();
                    updateLineQty(lineId, nextQty);
                    return;
                }

                const discountBtn = e.target.closest('[data-line-discount-id]');
                if (discountBtn) {
                    closeLineActionMenu(discountBtn);
                    const lineId = parseInt(discountBtn.getAttribute('data-line-discount-id') || '0', 10);
                    const discountAmount = parseFloat(discountBtn.getAttribute('data-line-discount-amount') || '0');
                    const itemName = discountBtn.getAttribute('data-line-name') || '';

                    if (lineId > 0) {
                        setActiveLineId(lineId);
                    }

                    openLineDiscountModal(lineId, itemName, discountAmount);
                    return;
                }

                const removeBtn = e.target.closest('[data-remove-line-id]');
                if (removeBtn) {
                    closeLineActionMenu(discountBtn);
                    const lineId = parseInt(removeBtn.getAttribute('data-remove-line-id') || '0', 10);

                    if (lineId > 0) {
                        setActiveLineId(lineId);
                    }

                    removeLine(lineId);
                }
            });

            currentDraftBody?.addEventListener('change', function (e) {
                const qtyInput = e.target.closest('[data-qty-line-id]');
                if (!qtyInput) return;

                const lineId = parseInt(qtyInput.getAttribute('data-qty-line-id') || '0', 10);
                let qty = parseFloat(qtyInput.value || '1');

                if (lineId > 0) {
                    setActiveLineId(lineId);
                }

                if (qty <= 0) {
                    qty = 1;
                }

                qtyInput.value = qty;
                rememberCartScrollTop();
                updateLineQty(lineId, qty);
            });

            function openResumeOtherShiftConfirm(options) {
                if (!confirmTitle || !confirmMessage || !btnConfirmAction || !confirmModal) {
                    resumeHeld(options?.orderId || 0);
                    return;
                }

                const shiftCode = String(options?.shiftCode || '').trim();
                const terminalName = String(options?.terminalName || '').trim();
                const terminalId = String(options?.terminalId || '').trim();
                const userName = String(options?.userName || '').trim();
                const userId = Number(options?.userId || 0);
                const customerName = String(options?.customerName || '').trim() || 'Khách lẻ';
                const subtotal = Number(options?.subtotal || 0);

                confirmTitle.textContent = 'Mở đơn từ ca khác';

                confirmMessage.innerHTML = `
        <div class="text-start">
            <div class="mb-2">
                Đơn này hiện đang thuộc ca khác. Bạn có muốn tiếp tục mở đơn này trên máy hiện tại không?
            </div>

            <div class="pos-note-box">
                ${shiftCode ? `<div><strong>Ca:</strong> ${shiftCode}</div>` : ''}
                ${(terminalName || terminalId) ? `<div><strong>Terminal:</strong> ${terminalName || terminalId}</div>` : ''}
                ${userName
                        ? `<div><strong>Nhân viên giữ:</strong> ${userName}</div>`
                        : userId > 0
                            ? `<div><strong>Nhân viên giữ:</strong> #${userId}</div>`
                            : ''}
                <div><strong>Khách:</strong> ${customerName}</div>
                <div><strong>Tạm tính:</strong> ${formatMoney(subtotal)}</div>
            </div>
        </div>
    `;

                if (confirmNoteBox) {
                    confirmNoteBox.style.display = 'none';
                }

                btnConfirmAction.textContent = 'Tiếp tục mở đơn';
                btnConfirmAction.className = 'btn btn-warning px-4';

                posState.pendingConfirmAction = async function () {
                    confirmModal.hide();
                    await resumeHeld(options?.orderId || 0);
                };

               
                const heldOrdersModalEl = document.getElementById('heldOrdersModal');
                const heldOrdersModal = heldOrdersModalEl
                    ? bootstrap.Modal.getInstance(heldOrdersModalEl)
                    : null;

                heldOrdersModal?.hide();
                confirmModal.show();
            }

            function bindResumeHeldClick(container) {
                container?.addEventListener('click', function (e) {
                    const btnByNewAttr = e.target.closest('[data-resume-btn-id]');
                    const itemByNewAttr = e.target.closest('[data-resume-order-id]');
                    const btnByOldAttr = e.target.closest('[data-resume-held-id]');

                    const orderId = btnByNewAttr
                        ? parseInt(btnByNewAttr.getAttribute('data-resume-btn-id') || '0', 10)
                        : itemByNewAttr
                            ? parseInt(itemByNewAttr.getAttribute('data-resume-order-id') || '0', 10)
                            : btnByOldAttr
                                ? parseInt(btnByOldAttr.getAttribute('data-resume-held-id') || '0', 10)
                                : 0;

                    if (!orderId) return;

                    const isOtherShift = btnByNewAttr?.getAttribute('data-is-other-shift') === 'true';

                    if (isOtherShift) {
                        openResumeOtherShiftConfirm({
                            orderId: orderId,
                            shiftCode: btnByNewAttr?.getAttribute('data-held-shift-code') || '',
                            terminalId: btnByNewAttr?.getAttribute('data-held-terminal-id') || '',
                            terminalName: btnByNewAttr?.getAttribute('data-held-terminal-name') || '',
                            userId: parseInt(btnByNewAttr?.getAttribute('data-held-user-id') || '0', 10),
                            userName: btnByNewAttr?.getAttribute('data-held-user-name') || '',
                            customerName: btnByNewAttr?.getAttribute('data-held-customer-name') || '',
                            subtotal: Number(btnByNewAttr?.getAttribute('data-held-subtotal') || 0)
                        });
                        return;
                    }

                    resumeHeld(orderId);
                });
            }

            bindResumeHeldClick(heldList);
            bindResumeHeldClick(document.getElementById('heldOrdersCurrentShiftList'));
            bindResumeHeldClick(document.getElementById('heldOrdersOtherShiftList'));

            requestAnimationFrame(function () {
                reapplyActiveLineAfterDraftSync();
            });
        }

        return {
            bindEvents,
            openConfirmModal,
            createNewCart,
            openHoldModal,
            holdCurrentCart,
            finalizeCurrentCart,
            cancelCurrentCart,
            resumeHeld,
            saveCurrentCartNote,
            saveCurrentCartDiscount,
            clearCurrentCartDiscount,
            openLineDiscountModal,
            saveLineDiscount,
            clearLineDiscount,
            updateLineQty,
            removeLine,
            openHoldModal,
            cancelCurrentCart
        };
    }

    return {
        create
    };
})();