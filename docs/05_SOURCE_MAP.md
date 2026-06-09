# Source Map

File này giúp mở chat mới biết nên gửi file nào khi muốn sửa chức năng.

## Services trong Application

- `AdminMenus/AdminMenuService.cs`
- `AttributeValues/AttributeValueService.cs`
- `Audit/AuditLogService.cs`
- `Auth/AuthService.cs`
- `Brands/BrandService.cs`
- `Categories/CategoryService.cs`
- `Display/DisplayPromotionService.cs`
- `Inventory/InputInvoiceXmlService.cs`
- `Inventory/InventoryAdjustmentDocumentNumberService.cs`
- `Inventory/InventoryAdjustmentDocumentService.cs`
- `Inventory/InventoryAdjustmentService.cs`
- `Inventory/InventoryCostSuggestionService.cs`
- `Inventory/InventoryMovementFactory.cs`
- `Inventory/InventoryMovementNoteBuilder.cs`
- `Inventory/InventoryMovementService.cs`
- `Inventory/InventoryReservationService.cs`
- `Inventory/InventoryRevaluationService.cs`
- `Inventory/InventoryService.cs`
- `Inventory/InventoryUnitResolver.cs`
- `Inventory/ReturnCostAllocator.cs`
- `Inventory/ReturnableValuationFragmentService.cs`
- `Inventory/StockCountService.cs`
- `Inventory/StockDocumentLookupService.cs`
- `Inventory/StockDocumentService.cs`
- `Inventory/StockTransferService.cs`
- `Inventory/WarehouseService.cs`
- `Invoices/InvoiceService.cs`
- `Media/ProductImageService.cs`
- `Media/TempUploadService.cs`
- `Orders/OrderDraftTotals.cs`
- `Orders/OrderInventoryIssueService.cs`
- `Orders/POSService.cs`
- `Orders/SalesReturnService.cs`
- `POSPaymentQrs/LocalVietQrGenerator.cs`
- `POSPaymentQrs/POSPaymentQrService.cs`
- `POSShiftClosingSlips/POSShiftClosingSlipService.cs`
- `POSShiftHandoverSlips/POSShiftHandoverSlipService.cs`
- `POSShifts/POSShiftService.cs`
- `ProductAttributes/ProductAttributeService.cs`
- `Products/BarcodeGovernanceService.cs`
- `Products/BarcodeHistoryService.cs`
- `Products/BarcodeLookupService.cs`
- `Products/ProductBarcodeVerificationService.cs`
- `Products/ProductService.cs`
- `Products/ProductUnitBarcodeReadService.cs`
- `Products/ProductUnitConversionService.cs`
- `Products/ProductVariantService.cs`
- `Promotions/PromotionAdminService.cs`
- `Promotions/PromotionEngine.cs`
- `Rewards/CustomerRewardService.cs`
- `Rewards/OrderRewardCalculator.cs`
- `Security/CurrentStorePermissionService.cs`
- `Security/RoleAdminService.cs`
- `Security/RolePermissionAdminService.cs`
- `Security/UserInStoreAdminService.cs`
- `StoreBankAccounts/StoreBankAccountService.cs`
- `Suppliers/SupplierService.cs`
- `Taxes/TaxService.cs`
- `Units/UnitService.cs`

## Repositories trong Infrastructure

- `AdminMenus/AdminMenuPermissionRepository.cs`
- `AdminMenus/AdminMenuRepository.cs`
- `AttributeValues/AttributeValueRepository.cs`
- `Audit/AuditLogRepository.cs`
- `Auth/AuthUserRepository.cs`
- `Brands/BrandRepository.cs`
- `Categories/CategoryRepository.cs`
- `Customers/CustomerRepository.cs`
- `Display/DisplayPromotionRepository.cs`
- `Inventory/DocumentNumberSequenceRepository.cs`
- `Inventory/InputInvoiceRepository.cs`
- `Inventory/InventoryAdjustmentDocumentNumberRepository.cs`
- `Inventory/InventoryAdjustmentDocumentRepository.cs`
- `Inventory/InventoryBalanceRepository.cs`
- `Inventory/InventoryCostLayerAllocationRepository.cs`
- `Inventory/InventoryCostLayerRepository.cs`
- `Inventory/InventoryCostSuggestionRepository.cs`
- `Inventory/InventoryReservationRepository.cs`
- `Inventory/InventoryTransactionRepository.cs`
- `Inventory/InventoryValuationEntryRepository.cs`
- `Inventory/NegativeInventoryLogRepository.cs`
- `Inventory/StockCountRepository.cs`
- `Inventory/StockDocumentLookupRepository.cs`
- `Inventory/StockDocumentRepository.cs`
- `Inventory/StockTransferRepository.cs`
- `Inventory/WarehouseRepository.cs`
- `Invoices/InvoiceRepository.cs`
- `Media/MediaAssetRepository.cs`
- `Orders/OrderInventoryIssueRepository.cs`
- `Orders/OrderPaymentRepository.cs`
- `Orders/OrderRepository.cs`
- `Orders/POSAuditLogRepository.cs`
- `Orders/POSShiftRepository.cs`
- `Orders/SalesReturnRepository.cs`
- `POSPaymentQrs/POSPaymentQrRequestRepository.cs`
- `POSShiftClosingSlips/POSShiftClosingSlipRepository.cs`
- `POSShiftHandoverSlips/POSShiftHandoverSlipRepository.cs`
- `POSTerminals/POSTerminalRepository.cs`
- `ProductAttributes/ProductAttributeRepository.cs`
- `Products/ProductBarcodeLookupRepository.cs`
- `Products/ProductBarcodeVerificationRepository.cs`
- `Products/ProductImageRepository.cs`
- `Products/ProductRepository.cs`
- `Products/ProductUnitConversionRepository.cs`
- `Products/ProductVariantBarcodeHistoryRepository.cs`
- `Products/ProductVariantRepository.cs`
- `Products/ProductVariantUnitBarcodeRepository.cs`
- `Products/VariantUsageChecker.cs`
- `Promotions/PromotionRepository.cs`
- `Rewards/CustomerRewardLedgerRepository.cs`
- `Rewards/CustomerRewardVoucherRepository.cs`
- `Rewards/RewardOrderRepository.cs`
- `Rewards/RewardSettingsRepository.cs`
- `Security/PermissionRepository.cs`
- `Security/RolePermissionRepository.cs`
- `Security/RoleRepository.cs`
- `Security/UserInStoreRepository.cs`
- `StoreBankAccounts/StoreBankAccountRepository.cs`
- `Suppliers/SupplierRepository.cs`
- `Taxes/TaxRepository.cs`
- `Units/UnitRepository.cs`
- `Users/UserRepository.cs`

## Controllers trong Web

- `Areas/Admin/Controllers/AccountController.cs`
- `Areas/Admin/Controllers/AdminMenusController.cs`
- `Areas/Admin/Controllers/AttributeValueController.cs`
- `Areas/Admin/Controllers/AuditLogsApiController.cs`
- `Areas/Admin/Controllers/AuditLogsController.cs`
- `Areas/Admin/Controllers/BarcodeGovernanceController.cs`
- `Areas/Admin/Controllers/BarcodeHistoryApiController.cs`
- `Areas/Admin/Controllers/BarcodeHistoryController.cs`
- `Areas/Admin/Controllers/BarcodeManagerController.cs`
- `Areas/Admin/Controllers/BarcodeNormalizationController.cs`
- `Areas/Admin/Controllers/BarcodeReadController.cs`
- `Areas/Admin/Controllers/BarcodeVerificationController.cs`
- `Areas/Admin/Controllers/BaseAdminController.cs`
- `Areas/Admin/Controllers/BasePOSPageController.cs`
- `Areas/Admin/Controllers/BrandController.cs`
- `Areas/Admin/Controllers/CategoryController.cs`
- `Areas/Admin/Controllers/CustomerRewardsController.cs`
- `Areas/Admin/Controllers/DisplayPromotionController.cs`
- `Areas/Admin/Controllers/HomeController.cs`
- `Areas/Admin/Controllers/InventoryAdjustmentController.cs`
- `Areas/Admin/Controllers/InventoryAdjustmentDocumentsApiController.cs`
- `Areas/Admin/Controllers/InventoryAdjustmentDocumentsController.cs`
- `Areas/Admin/Controllers/InventoryAdjustmentsController.cs`
- `Areas/Admin/Controllers/InventoryController.cs`
- `Areas/Admin/Controllers/InventoryInquiryController.cs`
- `Areas/Admin/Controllers/InventoryIssueManagementController.cs`
- `Areas/Admin/Controllers/InventoryLedgerController.cs`
- `Areas/Admin/Controllers/InvoiceController.cs`
- `Areas/Admin/Controllers/MediaController.cs`
- `Areas/Admin/Controllers/POSController.cs`
- `Areas/Admin/Controllers/POSOrderDetailController.cs`
- `Areas/Admin/Controllers/POSOrderPageController.cs`
- `Areas/Admin/Controllers/POSReceiptController.cs`
- `Areas/Admin/Controllers/POSReturnController.cs`
- `Areas/Admin/Controllers/POSShiftClosingSlipController.cs`
- `Areas/Admin/Controllers/POSShiftClosingSlipPrintPageController.cs`
- `Areas/Admin/Controllers/POSShiftController.cs`
- `Areas/Admin/Controllers/POSShiftHandoverSlipController.cs`
- `Areas/Admin/Controllers/POSShiftHandoverSlipPageController.cs`
- `Areas/Admin/Controllers/POSShiftHandoverSlipPrintPageController.cs`
- `Areas/Admin/Controllers/POSShiftHistoryPageController.cs`
- `Areas/Admin/Controllers/POSShiftManagerDashboardPageController.cs`
- `Areas/Admin/Controllers/POSShiftPageController.cs`
- `Areas/Admin/Controllers/ProductAttributeController.cs`
- `Areas/Admin/Controllers/ProductController.cs`
- `Areas/Admin/Controllers/ProductUnitConversionController.cs`
- `Areas/Admin/Controllers/PromotionController.cs`
- `Areas/Admin/Controllers/RewardVouchersController.cs`
- `Areas/Admin/Controllers/RolePermissionsController.cs`
- `Areas/Admin/Controllers/RolesController.cs`
- `Areas/Admin/Controllers/StockCountPagesController.cs`
- `Areas/Admin/Controllers/StockCountsController.cs`
- `Areas/Admin/Controllers/StockDocumentManagementController.cs`
- `Areas/Admin/Controllers/StockDocumentsController.cs`
- `Areas/Admin/Controllers/StockTransferController.cs`
- `Areas/Admin/Controllers/StockTransfersController.cs`
- `Areas/Admin/Controllers/StoreBankAccountsController.cs`
- `Areas/Admin/Controllers/SupplierController.cs`
- `Areas/Admin/Controllers/SupplierLookupController.cs`
- `Areas/Admin/Controllers/TaxController.cs`
- `Areas/Admin/Controllers/UnitController.cs`
- `Areas/Admin/Controllers/UserInStoresController.cs`
- `Areas/Admin/Controllers/WarehouseManagementController.cs`
- `Areas/Admin/Controllers/WarehouseReceivingController.cs`
- `Areas/Admin/Controllers/WarehousesController.cs`
- `Controllers/DevTestController.cs`
- `Controllers/ErrorController.cs`
- `Controllers/HomeController.cs`
- `Controllers/StorageTestController.cs`

## Admin Views chính

- `Account/AccessDenied.cshtml`
- `Account/Login.cshtml`
- `AdminMenus/Create.cshtml`
- `AdminMenus/Edit.cshtml`
- `AdminMenus/Index.cshtml`
- `AttributeValue/Edit.cshtml`
- `AttributeValue/Index.cshtml`
- `AttributeValue/_AttributeValueTable.cshtml`
- `AuditLogs/Index.cshtml`
- `BarcodeHistory/Index.cshtml`
- `BarcodeNormalization/Index.cshtml`
- `Brand/Edit.cshtml`
- `Brand/Index.cshtml`
- `Brand/_BrandTable.cshtml`
- `Category/Edit.cshtml`
- `Category/Index.cshtml`
- `Category/_CategoryTable.cshtml`
- `DisplayPromotion/Index.cshtml`
- `Error/Error.cshtml`
- `Home/Index.cshtml`
- `InventoryAdjustment/Index.cshtml`
- `InventoryAdjustmentDocuments/Create.cshtml`
- `InventoryAdjustmentDocuments/Detail.cshtml`
- `InventoryAdjustmentDocuments/Edit.cshtml`
- `InventoryAdjustmentDocuments/Index.cshtml`
- `InventoryAdjustmentDocuments/_AdjustmentDocumentForm.cshtml`
- `InventoryInquiry/Index.cshtml`
- `InventoryIssueManagement/Detail.cshtml`
- `InventoryIssueManagement/Index.cshtml`
- `InventoryLedger/Index.cshtml`
- `Invoice/Detail.cshtml`
- `Invoice/Index.cshtml`
- `Invoice/_CreateManualDetailModal.cshtml`
- `Invoice/_InvoiceSummary.cshtml`
- `Invoice/_ManualLinesTable.cshtml`
- `Media/Test.cshtml`
- `POS/CustomerDisplay.cshtml`
- `POS/Dashboard.cshtml`
- `POS/Index.cshtml`
- `POS/PrintReceipt.cshtml`
- `POS/_CartTable.cshtml`
- `POS/_CustomerBox.cshtml`
- `POS/_DraftMetaPanel.cshtml`
- `POS/_HeldOrdersPanel.cshtml`
- `POS/_OrderDiscountBox.cshtml`
- `POS/_OrderNoteBox.cshtml`
- `POS/_SummaryActionsPanel.cshtml`
- `POS/_SummaryPanel.cshtml`
- `POS/_Toolbar.cshtml`
- `POSOrderDetail/Index.cshtml`
- `POSOrderPage/Index.cshtml`
- `POSReceipt/Index.cshtml`
- `POSShiftClosingSlipPrintPage/Index.cshtml`
- `POSShiftHandoverSlipPage/Index.cshtml`
- `POSShiftHandoverSlipPrintPage/Index.cshtml`
- `POSShiftHistoryPage/Index.cshtml`
- `POSShiftManagerDashboardPage/Index.cshtml`
- `POSShiftPage/Index.cshtml`
- `Product/Create.cshtml`
- `Product/Detail.cshtml`
- `Product/Edit.cshtml`
- `Product/Index.cshtml`
- `Product/_ProductTable.cshtml`
- `Product/_VariantUnitConversionsModal.cshtml`
- `Product/_Variants.cshtml`
- `ProductAttribute/Edit.cshtml`
- `ProductAttribute/Index.cshtml`
- `ProductAttribute/_ProductAttributeTable.cshtml`
- `Promotion/Index.cshtml`
- `Promotion/_PromotionTable.cshtml`
- `RewardVouchers/Index.cshtml`
- `RolePermissions/Index.cshtml`
- `RolePermissions/_PermissionGroupCard.cshtml`
- `Roles/Create.cshtml`
- `Roles/Edit.cshtml`
- `Roles/Index.cshtml`
- `Shared/Error.cshtml`
- `Shared/Sections/Footer/_Footer.cshtml`
- `Shared/Sections/Menu/_VerticalMenu.cshtml`
- `Shared/Sections/Navbar/_Navbar.cshtml`
- `Shared/Sections/Navbar/_NavbarPartial.cshtml`
- `Shared/Sections/_Scripts.cshtml`
- `Shared/Sections/_ScriptsIncludes.cshtml`
- `Shared/Sections/_Styles.cshtml`
- `Shared/Sections/_Variables.cshtml`
- `Shared/_AdminLayout.cshtml`
- `Shared/_BarcodeManager.cshtml`
- `Shared/_BlankLayout.cshtml`
- `Shared/_CommonMasterLayout.cshtml`
- `Shared/_ContentNavbarLayout.cshtml`
- `Shared/_Layout.cshtml`
- `Shared/_POSNavTabs.cshtml`
- `Shared/_POSWorkContextBar.cshtml`
- `Shared/_ToastMessages.cshtml`
- `Shared/_ValidationScriptsPartial.cshtml`
- `StockCountPages/Detail.cshtml`
- `StockCountPages/Index.cshtml`
- `StockDocumentManagement/Edit.cshtml`
- `StockDocumentManagement/Index.cshtml`
- `StockDocumentManagement/_StockDocumentLinesTable.cshtml`
- `StockTransfer/Detail.cshtml`
- `StockTransfer/Index.cshtml`
- `StoreBankAccounts/Edit.cshtml`
- `StoreBankAccounts/Index.cshtml`
- `StoreBankAccounts/_StoreBankAccountTable.cshtml`
- `Supplier/Create.cshtml`
- `Supplier/Edit.cshtml`
- `Supplier/Index.cshtml`
- `Supplier/_SupplierTable.cshtml`
- `Tax/Edit.cshtml`
- `Tax/Index.cshtml`
- `Tax/_TaxTable.cshtml`
- `Unit/Edit.cshtml`
- `Unit/Index.cshtml`
- `Unit/_UnitTable.cshtml`
- `UserInStores/Create.cshtml`
- `UserInStores/Edit.cshtml`
- `UserInStores/Index.cshtml`
- `WarehouseManagement/Index.cshtml`
- `WarehouseReceiving/Detail.cshtml`
- `WarehouseReceiving/Index.cshtml`
- `WarehouseReceiving/_ReceivingLinesTable.cshtml`
- `_Partials/_Macros.cshtml`
- `_ViewImports.cshtml`
- `_ViewStart.cshtml`

## JS/CSS nghiệp vụ đáng chú ý

- `Admin/js/account-login.js`
- `Admin/js/attribute-values.index.js`
- `Admin/js/audit-log-index.js`
- `Admin/js/barcode-history.js`
- `Admin/js/barcode-normalization.js`
- `Admin/js/category.js`
- `Admin/js/inventory-adjustment-document-detail.js`
- `Admin/js/inventory-adjustment-document-form.js`
- `Admin/js/inventory-adjustment-documents-index.js`
- `Admin/js/inventory-adjustment.js`
- `Admin/js/inventory-inquiry.js`
- `Admin/js/inventory-ledger.js`
- `Admin/js/invoice-detail.js`
- `Admin/js/pos/pos-shift-manager-dashboard.js`
- `Admin/js/pos/pos.app.js`
- `Admin/js/pos/pos.barcode.js`
- `Admin/js/pos/pos.common.js`
- `Admin/js/pos/pos.customer-display.js`
- `Admin/js/pos/pos.customer.js`
- `Admin/js/pos/pos.dom.js`
- `Admin/js/pos/pos.error.js`
- `Admin/js/pos/pos.keyboard.js`
- `Admin/js/pos/pos.order.js`
- `Admin/js/pos/pos.payment.js`
- `Admin/js/pos/pos.render.js`
- `Admin/js/pos/pos.shift-handover-slip.page.js`
- `Admin/js/pos/pos.shift.page.js`
- `Admin/js/pos/pos.state.js`
- `Admin/js/product-attributes.index.js`
- `Admin/js/product-variant-unit-conversion.js`
- `Admin/js/promotions.page.js`
- `Admin/js/reward-vouchers.js`
- `Admin/js/stock-count.js`
- `Admin/js/stock-document-management.js`
- `Admin/js/stock-transfer.js`
- `Admin/js/suppliers.index.js`
- `Admin/js/unit.js`
- `Admin/js/warehouse-management.js`
- `Admin/js/warehouse-receiving-detail.js`
- `Admin/js/warehouse-receiving.js`
- `js/config.js`
- `js/dashboards-analytics.js`
- `js/extended-ui-perfect-scrollbar.js`
- `js/form-basic-inputs.js`
- `js/main.js`
- `js/pages-account-settings-account.js`
- `js/site.js`
- `js/ui-modals.js`
- `js/ui-popover.js`
- `js/ui-toasts.js`

## Gửi file theo nhóm lỗi

### Lỗi POS quét mã / giỏ hàng / thanh toán

Gửi các file:

```txt
docs/00_AI_CONTEXT.md
docs/modules/POS.md
docs/modules/PROMOTION.md nếu có khuyến mãi
GaoApp.Application/Services/Orders/POSService.cs
GaoApp.Application/Interfaces/Services/Orders/IPOSService.cs
GaoApp.Infrastructure/Repositories/Orders/OrderRepository.cs
GaoApp.Web/Areas/Admin/Controllers/POSController.cs
GaoApp.Web/wwwroot/Admin/js/pos/pos.app.js
GaoApp.Web/wwwroot/Admin/js/pos/pos.order.js
GaoApp.Web/wwwroot/Admin/js/pos/pos.render.js
```

### Lỗi Buy X Get Y / Promotion

```txt
docs/00_AI_CONTEXT.md
docs/modules/PROMOTION.md
GaoApp.Domain/Entities/Promotion.cs
GaoApp.Domain/Entities/PromotionItem.cs
GaoApp.Domain/Entities/PromotionComboRule.cs
GaoApp.Domain/Entities/OrderLine.cs
GaoApp.Application/Services/Promotions/PromotionEngine.cs
GaoApp.Application/Services/Promotions/PromotionAdminService.cs
GaoApp.Application/Services/Orders/POSService.cs
GaoApp.Infrastructure/Repositories/Promotions/PromotionRepository.cs
GaoApp.Web/Areas/Admin/Controllers/PromotionController.cs
GaoApp.Web/wwwroot/Admin/js/promotions.page.js
```

### Lỗi tích điểm / voucher

```txt
docs/00_AI_CONTEXT.md
docs/modules/REWARD.md
GaoApp.Application/Services/Rewards/CustomerRewardService.cs
GaoApp.Application/Services/Rewards/OrderRewardCalculator.cs
GaoApp.Application/Services/Orders/POSService.cs
GaoApp.Infrastructure/Repositories/Rewards/*.cs
GaoApp.Web/Areas/Admin/Controllers/CustomerRewardsController.cs
GaoApp.Web/Areas/Admin/Controllers/RewardVouchersController.cs
```

### Lỗi ca làm việc

```txt
docs/00_AI_CONTEXT.md
docs/modules/SHIFT.md
GaoApp.Domain/Entities/POSShift.cs
GaoApp.Application/Services/POSShifts/POSShiftService.cs
GaoApp.Application/Services/POSShiftHandoverSlips/POSShiftHandoverSlipService.cs
GaoApp.Application/Services/POSShiftClosingSlips/POSShiftClosingSlipService.cs
GaoApp.Web/Areas/Admin/Controllers/POSShiftController.cs
GaoApp.Web/wwwroot/Admin/js/pos/pos.shift.page.js
```

### Lỗi kho / nhập kho / tồn kho

```txt
docs/00_AI_CONTEXT.md
docs/modules/INVENTORY.md
GaoApp.Application/Services/Inventory/StockDocumentService.cs
GaoApp.Application/Services/Inventory/InventoryMovementService.cs
GaoApp.Application/Services/Inventory/InventoryService.cs
GaoApp.Infrastructure/Repositories/Inventory/*.cs
```

### Lỗi sản phẩm / đơn vị / barcode

```txt
docs/00_AI_CONTEXT.md
docs/modules/PRODUCT.md
GaoApp.Application/Services/Products/ProductService.cs
GaoApp.Application/Services/Products/ProductVariantService.cs
GaoApp.Application/Services/Products/ProductUnitConversionService.cs
GaoApp.Application/Services/Products/BarcodeLookupService.cs
GaoApp.Infrastructure/Repositories/Products/*.cs
```
