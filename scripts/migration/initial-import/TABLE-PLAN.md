# Kế hoạch bảng GaoApp trước lần chuyển đầu tiên

Trạng thái: **bản đối chiếu để thảo luận; chưa chạy dọn dữ liệu**. Kiểm kê trên `GaoAppDb` TEST ngày 23/09/2026. Số dòng chỉ là bằng chứng TEST, không dùng làm điều kiện cứng cho dữ liệu thật.

Người dùng đã chốt: giữ cửa hàng/chủ thể/kho ID 1, menu, tài khoản/nhân viên và phân quyền; giữ cấu hình tích điểm, hóa đơn, ngân hàng/QR, POS và mẫu in. Làm rỗng dữ liệu nghiệp vụ TEST cùng các bảng liên quan trước lần chuyển đầu tiên.

## Nhóm giữ nguyên

| Bảng | Dòng TEST | Nội dung giữ |
|---|---:|---|
| `__EFMigrationsHistory` | 40 | Lịch sử schema; không xóa hoặc reseed. |
| `AcbCallbackRouteChanges` | 1 | Lịch sử thay đổi cấu hình định tuyến; không phải giao dịch thu tiền. |
| `AcbCallbackRoutes` | 1 | Cấu hình định tuyến callback ngân hàng. |
| `AdminMenuItems` | 55 | Menu và cấu trúc menu cha/con. |
| `Attribute` | 0 | Định nghĩa thuộc tính sản phẩm. |
| `AttributeValue` | 0 | Giá trị thuộc tính; liên kết với variant TEST sẽ xóa riêng. |
| `Brands` | 0 | Danh mục tham chiếu; gói sản phẩm hiện không chép vào bảng này. |
| `InvoiceProviderSettings` | 1 | Cấu hình nhà cung cấp hóa đơn; chỉ giữ, không phát hành khi chuyển. |
| `LegalEntities` | 1 | Chủ thể 1 và liên kết cấu hình/kho. |
| `LegalEntityActivationEvents` | 0 | Lịch sử kích hoạt chế độ chủ thể; giữ cùng cấu hình để tránh lệch trạng thái. |
| `Permissions` | 200 | Danh mục quyền hệ thống. |
| `PosReceiptTemplates` | 1 | Mẫu in hóa đơn bán lẻ. |
| `POSTerminalDevices` | 5 | Liên kết thiết bị với quầy POS. |
| `POSTerminals` | 7 | Máy/quầy POS, gồm mã LEGACY-KET và LEGACY-UNKNOWN đang có. |
| `ProductLabelPrinters` | 0 | Cấu hình máy in tem. |
| `ProductLabelTemplates` | 0 | Mẫu tem. |
| `RewardSettings` | 1 | Cấu hình tích điểm; không bao gồm số dư khách TEST. |
| `RolePermissions` | 287 | Phân quyền theo vai trò. |
| `Roles` | 4 | Vai trò hiện có. |
| `StoreAcbSettings` | 1 | Cấu hình kết nối ngân hàng. |
| `StoreBankAccounts` | 2 | Tài khoản ngân hàng cấu hình. |
| `Stores` | 1 | Cửa hàng 1 và cấu hình vận hành hiện có. |
| `Taxes` | 3 | Danh mục thuế suất tham chiếu. |
| `UserInStores` | 45 | Phân công nhân viên vào cửa hàng và role. |
| `Users` | 46 | Tài khoản, nhân viên, mật khẩu và trạng thái; không chép đè từ GaoStore. |
| `Warehouses` | 2 | Kho 1 và liên kết chủ thể. |

## Nhóm làm rỗng

Danh sách tường minh; không dùng quy tắc “xóa mọi bảng trừ nhóm giữ”. Nếu schema có bảng mới chưa phân loại, công cụ phải dừng để kiểm tra. Làm rỗng cả bảng không được chép lại để chứng từ, số dư và liên kết TEST không ảnh hưởng dữ liệu mới.

| Bảng | Dòng TEST | Bước nhận dữ liệu cũ |
|---|---:|---|
| `AcbCallbackReceipts` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `AcbPaymentTransactions` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `AcbQrNotificationItems` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `AcbQrSessions` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `AuditLogs` | 646 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `Category` | 6 | 01 sản phẩm |
| `CustomerDebtReceipts` | 2 | Không chuyển công nợ cũ (đã chốt); số dư đặt cọc cũ chưa nằm trong phạm vi các gói hiện tại |
| `CustomerDepositEntries` | 4 | Không chuyển công nợ cũ (đã chốt); số dư đặt cọc cũ chưa nằm trong phạm vi các gói hiện tại |
| `CustomerDeposits` | 3 | Không chuyển công nợ cũ (đã chốt); số dư đặt cọc cũ chưa nằm trong phạm vi các gói hiện tại |
| `CustomerReceivableEntries` | 6 | Không chuyển công nợ cũ (đã chốt); số dư đặt cọc cũ chưa nằm trong phạm vi các gói hiện tại |
| `CustomerRewardLedgers` | 11,914 | 02 tích lũy |
| `CustomerRewardVouchers` | 3,358 | 02 voucher |
| `Customers` | 12,651 | 02 khách hàng |
| `DisplayPromotions` | 4 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `DocumentNumberSequences` | 5 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `InputInvoiceDetail` | 5 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `InputInvoiceHead` | 2 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `InputInvoiceItemCatalogMap` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `InputInvoiceSupplierResolutionEvent` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `InventoryAdjustmentDocuments` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `InventoryAdjustmentLines` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `InventoryBalances` | 16,496 | 04 tồn kho |
| `InventoryCostLayerAllocations` | 1,410,764 | 04 phân bổ giá vốn |
| `InventoryCostLayers` | 124,359 | 04 lớp giá vốn |
| `InventoryReservations` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `InventoryTransactions` | 1,512,413 | 04 lịch sử kho |
| `InventoryValuationEntries` | 1,535,123 | 04 giá vốn |
| `InvoiceBuyerProfiles` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `InvoiceCorrectionCases` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `InvoiceDetails` | 337,176 | 06 chi tiết hóa đơn |
| `InvoiceHeads` | 104,432 | 06 toàn bộ hóa đơn |
| `InvoiceInputStockSupplementalMovements` | 255,962 | 05 tồn hóa đơn |
| `InvoiceIntegrationLogs` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `NegativeInventoryLog` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `OrderInventoryIssueActions` | 10 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `OrderInventoryIssueLineAllocations` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `OrderInventoryIssueLines` | 8 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `OrderInventoryIssues` | 5 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `OrderLegalEntityAllocationReversals` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `OrderLegalEntityAllocations` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `OrderLines` | 1,399,600 | 03 chi tiết đơn bán |
| `OrderNumberSequences` | 5 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `OrderPayments` | 385,947 | 03 thanh toán |
| `OrderRewardVouchers` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `Orders` | 389,667 | 03 đơn bán |
| `POSAuditLogs` | 8 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PosOperationReceipts` | 121 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PosPaymentQrRequests` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `POSShiftCashDenominations` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `POSShiftCashTransactions` | 7,080 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `POSShiftClosingSlipDenominations` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `POSShiftClosingSlips` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `POSShiftHandoverSlipDenominations` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `POSShiftHandoverSlips` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `POSShifts` | 8,098 | 03 ca legacy và ca thay thế có ghi nguồn |
| `ProductBarcodeVerificationRequests` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `ProductImages` | 16,193 | 01 ảnh sản phẩm |
| `ProductLabelJobs` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `ProductLabelTasks` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `Products` | 32,134 | 01 sản phẩm |
| `ProductUnitConversion` | 35,287 | 01 sản phẩm |
| `ProductVariant` | 32,134 | 01 sản phẩm |
| `ProductVariantAttributeValue` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `ProductVariantBarcodeHistory` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `ProductVariantUnitBarcode` | 34,526 | 01 sản phẩm |
| `PromotionComboRule` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PromotionItems` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `Promotions` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PurchaseOrderActions` | 5 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PurchaseOrderLines` | 4 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PurchaseOrders` | 2 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PurchasePayables` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PurchaseReceiptAuditEvents` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PurchaseReceivingActions` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PurchaseRequestActions` | 14 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PurchaseRequestLines` | 6 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `PurchaseRequests` | 3 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `SalesReturnLines` | 274 | 03 trả hàng có liên kết |
| `SalesReturnPayments` | 117 | 03 hoàn tiền theo cách đã chuyển TEST |
| `SalesReturns` | 117 | 03 trả hàng có liên kết |
| `StockCountDocument` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `StockCountLine` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `StockDocument` | 3,385 | 04 phiếu nhập |
| `StockDocumentInputInvoiceDetailReconciliation` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `StockDocumentInputInvoiceMap` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `StockDocumentInputInvoiceReconciliation` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `StockDocumentLine` | 36,011 | 04 chi tiết phiếu nhập |
| `StockDocumentLineInputInvoiceMap` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `StockDocumentProvisionalItems` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `StockTransferDocument` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `StockTransferLine` | 0 | Không tự tái tạo từ nguồn cũ trong phạm vi đã chốt |
| `Suppliers` | 292 | 01 sản phẩm |
| `Unit` | 106 | 01 sản phẩm |
| `LegacyReturnArchives` | 0 | 03 lưu nguyên bản phiếu trả thiếu liên kết để tra cứu (bảng mới) |

## Bảng dùng chung

`MediaAssets` cần xử lý theo bản ghi: giữ logo/tài nguyên cấu hình, xác định ảnh sản phẩm TEST qua `ProductImages` và kiểm tra tham chiếu trước khi dọn metadata. Thư mục uploads không nằm trong lệnh xóa toàn bộ.

## Điều kiện trước khi viết/chạy bước dọn

- Báo cáo đúng server/database đích, số dòng từng bảng, tập bảng giữ và tập bảng dọn.
- Có backup đích kiểm chứng khôi phục được; dừng các tiến trình ghi GaoApp khi thực hiện.
- Chạy thử trên bản sao TEST riêng; không dùng TEST đang đối chiếu làm nơi thử xóa.
- Mọi thao tác dọn nằm trong transaction; lỗi thì rollback. Trước commit phải xác nhận nhóm dọn rỗng, nhóm giữ không thay đổi, khóa ngoại còn hợp lệ.
- Không dọn lại khi một gói đã được import. Lần chạy lại chỉ kiểm tra/chạy tiếp đúng bước, không tự reset.
- `GaoStoreMigrationRunsV2` là journal do bộ chuyển tạo khi COMMIT, không có trong kiểm kê TEST gốc. Không xóa journal để vượt kiểm tra chạy lại; nếu có receipt đã chuyển, bước dọn phải dừng.
- Giữ schema và lịch sử EF; không xóa database hoặc tự chạy seed làm thay đổi cấu hình đã giữ.
- Kiểm kê hiện tại: 121 bảng; 26 bảng giữ nguyên; 94 bảng dọn toàn bộ; 1 bảng dọn có chọn lọc.
- Khóa ngoại từ nhóm giữ sang nhóm dọn phát hiện trong schema hiện tại: 0. Vẫn phải kiểm tra tham chiếu nằm trong JSON/chuỗi và tài nguyên dùng chung trước khi thực hiện.

## Quyết định đã chốt ở bước 03

Dữ liệu TEST hiện có 389.661 đơn `LEGACY-*`, tất cả Completed/Paid và BalanceDue=0; trong đó 3.633 đơn nguồn có HaveDebt=1. Bảng Debt nguồn có 355 khách có Total ở dòng ID cuối dương; đây là số dư theo bảng nguồn, chưa phải kết luận đối soát thực tế. Người dùng đã chốt: **không chuyển công nợ cũ**. Không tái tạo Debt nguồn thành số dư phải thu hay phiếu thu nợ trên GaoApp. Dọn công nợ TEST trước import theo phạm vi đã đồng ý.

Nguồn có 3.685 đầu phiếu trả hàng Category=3; TEST có 117 `LEGACY-RETURN-*`. 3.568 phiếu còn lại thiếu OrderIDMuaHang. Người dùng đã chốt: **lưu nhóm thiếu liên kết riêng để tra cứu**. Giữ header/chi tiết nguồn, mã phiếu, ngày, khách hàng, nhân viên và trạng thái nguồn; không tạo đơn bán giả, không tự ghi nhập kho hay hoàn tiền từ nhóm lưu trữ. Số lượng thực tế được tính từ snapshot chạy, không hard-code 3.568.

Nhân viên và thiết bị: TEST đang ánh xạ nhân viên theo ID cũ; hiện 389.661/389.661 đơn có CreatedBy trùng UserID nguồn và tồn tại trong Users. Dữ liệu thật phải kiểm tra thêm danh tính, không xem trùng số ID là đủ. Ca thực lấy ManagementJob, ca thiếu nguồn dùng ca legacy thay thế như TEST; không biến các ca lịch sử thành ca bán đang mở.

Một gói trả hàng khác được tìm thấy tại `D:/GaoApp/Chuyển Dữ Liệu/GAOAPP-RETURN-FULL-DATA-FINAL-PACKAGE.zip`; chưa chạy. Gói này không ghi SalesReturnPayments, trong khi TEST hiện có 117 khoản hoàn tiền. Theo yêu cầu giữ cách TEST, không dùng gói tìm thấy để ghi đè quy tắc hiện tại.
