window.PosKeyboard = (function () {
    'use strict';

    function create(deps) {
        const { posState, elements } = deps;

        const {
            txtBarcode,
            txtCustomerKeyword,
            payAmount,
            confirmNoteInput,
            lineDiscountAmount,
            txtOrderDiscount,
            txtOrderNote,
            qtyEditValue,

            btnNewCart,
            btnHoldCart,
            btnOpenPayment,
            btnFinalizeCart,
            btnCancelCart,
            btnSaveQtyEdit
        } = elements;

        function isTypingTarget(target) {
            if (!target) return false;

            const tag = (target.tagName || '').toLowerCase();
            if (tag === 'input' || tag === 'textarea' || tag === 'select') return true;
            if (target.isContentEditable) return true;

            return false;
        }

        function isModalBusy() {
            return !!(
                posState.paymentModalOpen ||
                posState.customerModalOpen ||
                posState.quickCreateCustomerModalOpen ||
                posState.lineDiscountModalOpen ||
                posState.qtyEditModalOpen ||
                posState.holdModalOpen ||
                posState.confirmModalOpen
            );
        }

        // Chỉ chặn hotkey ở các input nhập liệu nghiệp vụ thật sự.
        // Riêng ô barcode vẫn cho phép hotkey hoạt động.
        function isProtectedInput(target) {
            return target === txtCustomerKeyword
                || target === payAmount
                || target === confirmNoteInput
                || target === lineDiscountAmount
                || target === txtOrderDiscount
                || target === txtOrderNote
                || target === qtyEditValue;
        }

        function triggerButton(button) {
            if (!button) return false;
            if (button.disabled) return false;

            button.click();
            return true;
        }

        function focusBarcode() {
            setTimeout(() => {
                txtBarcode?.focus();
                txtBarcode?.select?.();
            }, 50);
        }

        function handleGlobalKeydown(e) {
            if (posState.ui?.modals?.invoiceIntent) return;
            const target = e.target;
            const code = e.code || e.key || '';

            // Nếu đang mở popup chỉnh số lượng:
            // Enter = lưu
            // Esc   = đóng popup
            if (posState.qtyEditModalOpen) {
                if (code === 'Enter' || e.key === 'Enter') {
                    e.preventDefault();
                    e.stopPropagation();
                    triggerButton(btnSaveQtyEdit);
                    return;
                }

                if (code === 'Escape' || e.key === 'Escape') {
                    e.preventDefault();
                    e.stopPropagation();

                    const modalEl = document.getElementById('qtyEditModal');
                    const modal = modalEl ? bootstrap.Modal.getInstance(modalEl) : null;
                    modal?.hide();
                    return;
                }

                return;
            }

            if (code === 'Escape' || e.key === 'Escape') {
                e.preventDefault();

                if (!isModalBusy()) {
                    focusBarcode();
                }
                return;
            }

            // Chỉ chặn hotkey ở các input cần nhập liệu thật sự.
            // Riêng ô barcode vẫn cho phép hotkey hoạt động.
            if (isProtectedInput(target)) {
                return;
            }

            // F2 = Giữ đơn
            if (code === 'F2' || e.key === 'F2') {
                e.preventDefault();
                e.stopPropagation();

                if (!isModalBusy()) {
                    triggerButton(btnHoldCart);
                }
                return;
            }

            // F3 = Thanh toán
            if (code === 'F3' || e.key === 'F3') {
                e.preventDefault();
                e.stopPropagation();

                if (!posState.paymentModalOpen) {
                    triggerButton(btnOpenPayment);
                }
                return;
            }

            // F4 = Chốt đơn
            if (code === 'F4' || e.key === 'F4') {
                e.preventDefault();
                e.stopPropagation();

                if (!isModalBusy()) {
                    triggerButton(btnFinalizeCart);
                }
                return;
            }

            // F6 = Mở popup đơn giữ
            if (code === 'F6' || e.key === 'F6') {
                e.preventDefault();
                e.stopPropagation();

                if (!isModalBusy()) {
                    const heldOrdersModalEl = document.getElementById('heldOrdersModal');
                    const heldOrdersModal = heldOrdersModalEl
                        ? bootstrap.Modal.getOrCreateInstance(heldOrdersModalEl)
                        : null;

                    heldOrdersModal?.show();
                }
                return;
            }

            // F7 = Mở popup chỉnh số lượng line đang active
            // Alt + F7 = Mở popup giảm giá dòng đang active
            if ((code === 'F7' || e.key === 'F7') && e.altKey) {
                e.preventDefault();
                e.stopPropagation();

                if (!isModalBusy()) {
                    document.dispatchEvent(new CustomEvent('pos:open-active-line-discount-editor'));
                }
                return;
            }

            // F7 = Mở popup chỉnh số lượng line đang active
            if (code === 'F8' || e.key === 'F8') {
                e.preventDefault();
                e.stopPropagation();

                if (!isModalBusy()) {
                    document.dispatchEvent(new CustomEvent('pos:open-active-line-qty-editor'));
                }
                return;
            }

            // Ctrl + Delete = Hủy giỏ
            if (e.ctrlKey && (code === 'Delete' || e.key === 'Delete')) {
                e.preventDefault();
                e.stopPropagation();

                if (!isModalBusy()) {
                    triggerButton(btnCancelCart);
                }
                return;
            }

            // Enter ngoài input khác -> focus lại barcode
            if ((code === 'Enter' || e.key === 'Enter') && !isTypingTarget(target) && !isModalBusy()) {
                focusBarcode();
            }
        }

        function bind() {
            window.addEventListener('keydown', handleGlobalKeydown, true);
        }

        return {
            bind
        };
    }

    return {
        create
    };
})();
