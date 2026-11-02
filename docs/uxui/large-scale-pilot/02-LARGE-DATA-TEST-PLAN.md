# A2 — Kế hoạch dữ liệu tổng hợp, chưa tạo dữ liệu

Ngày 02/11/2026. **SOURCE VERIFIED** cho schema; **ASSUMPTION** cho kích cỡ/dung lượng; **NOT TESTED** cho DB thực tế. Trạng thái **BLOCKED xác minh DB/an toàn/dung lượng**. Kế hoạch dưới đây đã soạn; chưa đủ điều kiện PASS A2 PLAN, không có A2 EXECUTED.

## Dataset và tài nguyên hiện tại

Không có DB test được xác minh/được chỉ định trong lượt này. Số sản phẩm, variant, conversion, barcode, Order, OrderLine, InvoiceHead, InvoiceDetail và dung lượng database: **UNKNOWN**, không phải 0. Không kết nối SQL để kiểm kê một DB kinh doanh chưa được phép. Không dùng backup/clone/bằng chứng dataset lịch sử làm dữ liệu mới.

**MEASURED host:** 12 logical processors nhìn thấy. Ổ C/E còn nhiều dung lượng tại thời điểm khảo sát; con số cụ thể trong [host-resources.txt](evidence/host-resources.txt). RAM không xác minh được do quyền CIM; SQL Server edition, quota data/log/tempdb, vị trí data/log và tài nguyên instance: UNKNOWN. Dung lượng trống của máy không chứng minh DB còn quota hoặc SQL chạy trên máy này.

Concurrency đề xuất ban đầu: **1 browser session**, không load/stress/soak. Chỉ T0 sau khi có giới hạn RAM/edition/DB rõ; T1/T2 là các lần duyệt riêng, không phải lệnh thực thi.

## Schema và cardinality đã xác minh

| Quan hệ/giới hạn | Source evidence | Hệ quả cho kế hoạch |
|---|---|---|
| Product → nhiều ProductVariant | `Product.cs:69`; `ProductVariant.cs:10–11`; `ProductVariantConfiguration.cs:32–34` | Số SKU/variant không đồng nghĩa số product. |
| Variant → nhiều ProductUnitConversion → nhiều barcode | các collection và FK trong entity/config tương ứng | Cần đếm đủ đơn vị/lốc/thùng/barcode; không viết lại resolver. |
| Product alias unique theo Store | `ProductConfiguration.cs:15–31` | Alias giả phải unique trong tenant; tên tối đa 200, giá precision18,2, RowVersion giữ nguyên. |
| Conversion theo Store/Variant/Unit; Factor precision18,4 | `ProductUnitConversionConfiguration.cs:13–43` | Dùng quy cách hợp lệ, không gộp số lượng khác đơn vị hoặc tự đổi công thức giá. |
| Barcode active chưa xóa unique Store/Barcode; primary active unique Conversion | `ProductVariantUnitBarcodeConfiguration.cs:41–54` | Dùng prefix giả, không scan vào đơn hàng thật; seed phải qua ràng buộc native. |
| Order → nhiều Lines/Payments | `Order.cs:284–285`; `OrderLine.cs:13`; OrderLineConfiguration | L là dòng đơn, H là đầu đơn; không coi 100k dòng là 100k hóa đơn. Quantity/BaseQuantity precision18,4; Multiplier18,6; tiền18,2. |
| InvoiceHead.OrderId nullable; InvoiceHead → nhiều InvoiceDetail | `InvoiceHead.cs:43,298`; `InvoiceHeadConfiguration.cs:242–245`; `InvoiceDetailConfiguration.cs:60–67` | Số hóa đơn và dòng hóa đơn độc lập với số Order/OrderLine; không giả định 1:1, không tạo invoice provider hợp lệ giả. |
| Balance unique Store/Warehouse/Variant | `InventoryBalanceConfiguration.cs:48–49`; Warehouse/LegalEntity boundary trong source map và decision log | Không tạo mỗi product một balance rồi coi là tồn toàn hệ thống; bảo vệ đúng owner và precision. |

Đây là schema trong source working tree; không xác nhận schema đã được áp dụng ở database nào.

## Tier đề xuất để duyệt

| Tier | Products P | OrderLines L | Đầu đơn H *giả định* nếu 10 dòng/đơn | Giỏ POS mục tiêu | Thứ tự |
|---|---:|---:|---:|---:|---|
| T0 | 1.000 | 10.000 | 1.000 | 10–30 | Smoke trước; có thể giảm xuống 200/2.000 khi tài nguyên hạn chế. |
| T1 | 10.000 | 100.000 | 10.000 | 50–100 | Chỉ sau smoke và duyệt T1. |
| T2 | 50.000 | 500.000 | 50.000 | 150–300 | Chỉ sau duyệt tài nguyên/capacity, không tự chạy. |

Phân bố giả định để tính dung lượng: V=2P variant, C=3V conversion, B=C barcode; không phải bắt buộc nghiệp vụ. Có sản phẩm tên tiếng Việt dài, không dấu, SKU gần giống, kết quả 0/1/nhiều; nhiều category/brand và trạng thái active/sellable khác nhau. Giữ một nhóm dữ liệu ngoại Store để kiểm tra tenant sau khi được phép tạo fixture. Khách/nhà cung cấp/chủ thể đều giả; dùng `example.invalid` cho email, không dùng điện thoại có thể gọi người thật. Chứng từ chỉ có trạng thái native hợp lệ trong sandbox đã được duyệt; không đi qua thanh toán/phát hành thật.

Giỏ POS là yêu cầu fixture riêng. Không tạo 300 dòng bằng thao tác API trên cửa hàng đang dùng; không tự hoàn tất đơn để sinh lịch sử. Đối với P1 sản phẩm, có thể đo catalog trước khi duyệt sinh L; không bắt buộc tạo Orders để benchmark Product Index.

## Ước lượng dung lượng — ASSUMPTION, không phải số đo

Mô hình thô không bao gồm ảnh: `D = 4KiB·P + 2KiB·V + 1KiB·C + 0,5KiB·B + 4KiB·H + 2KiB·L`.

Với phân bố trên, D khoảng **40 MiB / 400 MiB / 2 GiB** cho T0/T1/T2. Đây là giả định quy hoạch, không phải upper bound. Dữ liệu rộng, index, audit/outbox/history, fragmentation, log và tempdb có thể lớn hơn. Đặt ngân sách tạm tối thiểu **6×D**, khoảng **0,25 / 2,5 / 12 GiB**, rồi hiệu chỉnh sau đo T0; ảnh dùng chung asset giả cục bộ và tính riêng. Không cấp phát các dung lượng này trong pilot.

Sau khi được phép đọc DB test, lấy edition/giới hạn thực tế, space used/reserved, data/log free và tempdb/quota. Không mặc định giới hạn SQL Express theo một phiên bản chưa biết. Chỉ tăng tier nếu ngân sách tổng dưới quota và có headroom được chủ dự án chốt; không ép dùng T2 chỉ vì ổ E trống. Dừng tăng dữ liệu khi chạm ngưỡng dung lượng/latency/CPU/RAM đã duyệt, thay vì tự chỉnh SQL server-wide/index.

## Cơ chế tạo demo đề xuất — chưa viết/chạy script

Source có full-app/SQL test fixtures, nhưng startup của chúng có migration/seed catalog/account/menu. Ví dụ `Ui20CurrentModelApplicationFixture.cs:58–85` gọi migration, seed inventory và security/menu. Không sử dụng nguyên xi vì vi phạm quyền dữ liệu/menu của pilot hiện tại. Không chạy seeder production/Migrator/recovery.

Sau một phê duyệt riêng, trình thiết kế fixture `GaoApp.Tests/UXUI/LargeScalePilotFixture.cs` và tài liệu chạy cùng cấp để review **trước khi tạo file code**. Fixture phải nhận DB test đã xác thực, dry-run mặc định, whitelist tên/owner/run-id và dung lượng; reject mọi connection không khớp. Manifest dự kiến ghi số Products/Variants/Conversions/Barcodes/Orders/OrderLines riêng; không ghi PII/secret. Không seed menu; danh mục chức năng do người dùng cấu hình.

Tài khoản/Store/kho/quầy/role demo nên được chủ dự án chuẩn bị trước. Nếu cần tạo chúng, phải duyệt rõ phạm vi; không coi chúng là phụ phẩm mặc nhiên của seed lớn. Cleanup chỉ nhắm run-owned synthetic records trên DB test được duyệt, có phương án rollback và không reset database chung. Task hiện tại không cấp quyền create/drop/copy/update/insert/delete database.

Điểm duyệt dữ liệu tiếp theo: **chỉ định DB test đã có và quyền đọc**. Việc tạo DB, fixture, seed từng tier, mutation test hoặc concurrency đều chưa được duyệt và không kèm theo duyệt giao diện P1 mặc định.
