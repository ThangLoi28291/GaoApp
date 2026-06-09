window.PosDom = (function () {
    'use strict';

    function get() {
        /* =========================================================
           B. DOM CHUNG
        ========================================================= */
        const txtBarcode = document.getElementById('txtBarcode');
        const btnScan = document.getElementById('btnScan');
        const btnFocusBarcode = document.getElementById('btnFocusBarcode');

        const btnNewCart = document.getElementById('btnNewCart');
        const btnHoldCart = document.getElementById('btnHoldCart');
        const btnOpenPayment = document.getElementById('btnOpenPayment');
        const btnFinalizeCart = document.getElementById('btnFinalizeCart');
        const btnCancelCart = document.getElementById('btnCancelCart');

        const currentDraftBody = document.getElementById('currentDraftBody');
        const currentCartInfo = document.getElementById('currentCartInfo');
        const lblCurrentOrder = document.getElementById('lblCurrentOrder');
        const heldList = document.getElementById('heldList');
        const paymentListBox = document.getElementById('paymentListBox');

        const customerInfoBox = document.getElementById('customerInfoBox');

        const txtOrderNote = document.getElementById('txtOrderNote');
        const btnSaveOrderNote = document.getElementById('btnSaveOrderNote');

        const txtOrderDiscount = document.getElementById('txtOrderDiscount');
        const btnSaveOrderDiscount = document.getElementById('btnSaveOrderDiscount');
        const btnClearOrderDiscount = document.getElementById('btnClearOrderDiscount');

        const sumSubtotal = document.getElementById('sumSubtotal');
        const sumDiscount = document.getElementById('sumDiscount');
        const sumGrandTotal = document.getElementById('sumGrandTotal');
        const sumPaid = document.getElementById('sumPaid');
        const sumBalance = document.getElementById('sumBalance');
        const sumChange = document.getElementById('sumChange');

        const barcodeAutocomplete = document.getElementById('barcodeAutocomplete');

        const metaToggleButtons = document.querySelectorAll('[data-toggle-meta]');
        const metaSections = document.querySelectorAll('[data-meta-section]');

        /* =========================================================
           C. DOM - CUSTOMER MODAL
        ========================================================= */
        const txtCustomerKeyword = document.getElementById('txtCustomerKeyword');
        const btnSearchCustomer = document.getElementById('btnSearchCustomer');
        const customerSearchResult = document.getElementById('customerSearchResult');
        const btnClearCustomer = document.getElementById('btnClearCustomer');

        const btnOpenQuickCreateCustomer = document.getElementById('btnOpenQuickCreateCustomer');
        const qcCustomerName = document.getElementById('qcCustomerName');
        const qcCustomerPhone = document.getElementById('qcCustomerPhone');
        const qcCustomerAddress = document.getElementById('qcCustomerAddress');
        const qcCustomerNote = document.getElementById('qcCustomerNote');
        const btnSubmitQuickCreateCustomer = document.getElementById('btnSubmitQuickCreateCustomer');

        /* =========================================================
           D. DOM - PAYMENT MODAL
        ========================================================= */
        const payMethod = document.getElementById('payMethod');
        const payAmount = document.getElementById('payAmount');
        const payReference = document.getElementById('payReference');
        const payProvider = document.getElementById('payProvider');

        const btnAddPayment = document.getElementById('btnAddPayment');
        const btnFinalizeFromPaymentModal = document.getElementById('btnFinalizeFromPaymentModal');
        const btnPayExact = document.getElementById('btnPayExact');

        const paymentModalListBox = document.getElementById('paymentModalListBox');

        const paySumSubtotal = document.getElementById('paySumSubtotal');
        const paySumDiscount = document.getElementById('paySumDiscount');
        const paySumGrandTotal = document.getElementById('paySumGrandTotal');
        const paySumPaid = document.getElementById('paySumPaid');
        const paySumBalance = document.getElementById('paySumBalance');
        const paySumChange = document.getElementById('paySumChange');

        const payPreviewBalance = document.getElementById('payPreviewBalance');
        const payPreviewChange = document.getElementById('payPreviewChange');

        /* =========================================================
           E. DOM - LINE DISCOUNT / HOLD / QTY EDIT / CONFIRM
        ========================================================= */
        const txtHoldNote = document.getElementById('txtHoldNote');
        const btnConfirmHold = document.getElementById('btnConfirmHold');

        const lineDiscountLineId = document.getElementById('lineDiscountLineId');
        const lineDiscountAmount = document.getElementById('lineDiscountAmount');
        const lineDiscountItemName = document.getElementById('lineDiscountItemName');
        const btnSaveLineDiscount = document.getElementById('btnSaveLineDiscount');
        const btnClearLineDiscount = document.getElementById('btnClearLineDiscount');

        const qtyEditLineId = document.getElementById('qtyEditLineId');
        const qtyEditItemName = document.getElementById('qtyEditItemName');
        const qtyEditValue = document.getElementById('qtyEditValue');
        const btnSaveQtyEdit = document.getElementById('btnSaveQtyEdit');

        const confirmTitle = document.getElementById('confirmTitle');
        const confirmMessage = document.getElementById('confirmMessage');
        const confirmNoteBox = document.getElementById('confirmNoteBox');
        const confirmNoteLabel = document.getElementById('confirmNoteLabel');
        const confirmNoteInput = document.getElementById('confirmNoteInput');
        const btnConfirmAction = document.getElementById('btnConfirmAction');

        /* =========================================================
           F. MODAL BOOTSTRAP
        ========================================================= */
        const paymentModalEl = document.getElementById('paymentModal');
        const paymentModal = paymentModalEl ? new bootstrap.Modal(paymentModalEl) : null;

        const customerModalEl = document.getElementById('customerModal');
        const customerModal = customerModalEl ? new bootstrap.Modal(customerModalEl) : null;

        const quickCreateCustomerModalEl = document.getElementById('quickCreateCustomerModal');
        const quickCreateCustomerModal = quickCreateCustomerModalEl ? new bootstrap.Modal(quickCreateCustomerModalEl) : null;

        const holdModalEl = document.getElementById('holdModal');
        const holdModal = holdModalEl ? new bootstrap.Modal(holdModalEl) : null;

        const confirmModalEl = document.getElementById('confirmModal');
        const confirmModal = confirmModalEl ? new bootstrap.Modal(confirmModalEl) : null;

        const lineDiscountModalEl = document.getElementById('lineDiscountModal');
        const lineDiscountModal = lineDiscountModalEl ? new bootstrap.Modal(lineDiscountModalEl) : null;

        const qtyEditModalEl = document.getElementById('qtyEditModal');
        const qtyEditModal = qtyEditModalEl ? new bootstrap.Modal(qtyEditModalEl) : null;

        return {
            common: {
                txtBarcode,
                btnScan,
                btnFocusBarcode,
                btnNewCart,
                btnHoldCart,
                btnOpenPayment,
                btnFinalizeCart,
                btnCancelCart,
                currentDraftBody,
                currentCartInfo,
                lblCurrentOrder,
                heldList,
                paymentListBox,
                customerInfoBox,
                txtOrderNote,
                btnSaveOrderNote,
                txtOrderDiscount,
                btnSaveOrderDiscount,
                btnClearOrderDiscount,
                metaToggleButtons,
                metaSections,
                sumSubtotal,
                sumDiscount,
                sumGrandTotal,
                sumPaid,
                sumBalance,
                sumChange,
               
                barcodeAutocomplete
            },
            customer: {
                txtCustomerKeyword,
                btnSearchCustomer,
                customerSearchResult,
                btnClearCustomer,
                btnOpenQuickCreateCustomer,
                qcCustomerName,
                qcCustomerPhone,
                qcCustomerAddress,
                qcCustomerNote,
                btnSubmitQuickCreateCustomer
            },
            payment: {
                payMethod,
                payAmount,
                payReference,
                payProvider,
                btnAddPayment,
                btnFinalizeFromPaymentModal,
                btnPayExact,
                paymentModalListBox,
                paySumSubtotal,
                paySumDiscount,
                paySumGrandTotal,
                paySumPaid,
                paySumBalance,
                paySumChange,
                payPreviewBalance,
                payPreviewChange
            },
            support: {
                txtHoldNote,
                btnConfirmHold,
                lineDiscountLineId,
                lineDiscountAmount,
                lineDiscountItemName,
                btnSaveLineDiscount,
                btnClearLineDiscount,
                qtyEditLineId,
                qtyEditItemName,
                qtyEditValue,
                btnSaveQtyEdit,
                confirmTitle,
                confirmMessage,
                confirmNoteBox,
                confirmNoteLabel,
                confirmNoteInput,
                btnConfirmAction
            },
            modalElements: {
                paymentModalEl,
                customerModalEl,
                quickCreateCustomerModalEl,
                holdModalEl,
                confirmModalEl,
                lineDiscountModalEl,
                qtyEditModalEl
            },
            modals: {
                paymentModal,
                customerModal,
                quickCreateCustomerModal,
                holdModal,
                confirmModal,
                lineDiscountModal,
                qtyEditModal
            }
        };
    }

    return {
        get
    };
})();