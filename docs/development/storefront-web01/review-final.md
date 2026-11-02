# WEB01 — Review độc lập cuối cùng (V3)

Ngày: 30/10/2026. Reviewer: `web01_review`.

**Kết luận:** không còn phát hiện P0/P1/P2 chưa xử lý trong phạm vi W01T01–W01T04 và host khách hàng nền. Có thể nghiệm thu WEB01 để tiếp tục các bước sau; website vẫn đóng, chưa phải cổng mở bán.

## Phạm vi và định danh nguồn

Đã đọc AGENTS.md, kế hoạch W01T01–W01T04, hợp đồng WEB00 revision 4, tài liệu WEB01, nguồn Domain/Application/Infrastructure/Admin/public host, migration/snapshot, helper schema và các kiểm thử độc lập. Reviewer không sửa production, test hoặc tài liệu triển khai; chỉ ghi artifact review.

28/28 tệp của `../production-ready-v3-sha256.json` khớp SHA256 với byte hiện tại. Bằng chứng: `final-source-hashes.json`. Các sửa V3 về manifest được đọc lại, gồm chuẩn hóa lặp ổn định và chuẩn hóa khoảng trắng định nghĩa trigger.

Probe dùng bảy DLL project sao từ `.artifacts/storefront-web01/build-v2/bin/GaoApp.Tests/debug/`, là đầu ra build V3 đã kiểm thử. Mỗi bản sao khớp SHA256 với đầu ra đó (`v3-probe-assembly-hashes.json`), gồm:

- Infrastructure: `8F1106451209EDBBD36B5878EE4FADAAB485AAE6AC0A065F2A978DE342A8BE20`.
- Tests: `05C226CB3493896B46E48E38BC9299BA37701F918B4C40895A3AE97013FFEA6F`.

## Các phát hiện đã khép

1. **P2 — bảo vệ chuỗi bằng chứng:** trước sửa, SQL cho phép đổi `ProtectedEvidence` từ `synthetic-only` thành `SYNTHETIC-ONLY` do so sánh theo collation. `GaoApp.Infrastructure/Data/Migrations/StorefrontFoundationSchemaSqlCatalog.cs:63` nay so byte và độ dài cho các cột được bảo vệ. Probe V3 từ chối đổi case của receipt/intent và thêm khoảng trắng cuối receipt bằng SQL 51090. Các cập nhật trạng thái được cho phép vẫn được test độc lập.

2. **P2 — nguồn hồ sơ giao:** trước sửa, fulfillment kho/chủ thể `1/1` gắn được delivery `2/2` cùng cửa hàng. `GaoApp.Infrastructure/Data/Configurations/StorefrontFoundationConfiguration.cs:198` nay dùng FK gồm cửa hàng, delivery, kho và chủ thể. Probe bị FK 547 từ chối. Khi đã có delivery, trigger cũng chặn bỏ hoặc đổi liên kết; kiểm thử độc lập có ca gắn đúng nguồn và ca single assignment.

3. **P2 — nguồn chứng cứ kho:** trước sửa, dòng SKU/kho `1/1` gắn được transaction SKU/kho `2/2` loại OpeningBalance. `GaoApp.Infrastructure/Data/Migrations/StorefrontFoundationSchemaSqlCatalog.cs:33` nay kiểm tra SKU/kho, policy 2, loại nguồn Delivery, xuất giao âm và delivery reference tương ứng; dữ liệu snapshot dòng nguồn giữ bất biến. Probe bị SQL 51092 từ chối. Liên kết transaction trống vẫn hợp lệ ở bước nền WEB01.

4. **P2 — tạo vùng từ cấu hình cũ:** trước sửa, form tạo vùng đọc chế độ cố định vẫn được lưu theo chế độ kg vừa đổi. Request có `SettingsVersion` tại `GaoApp.Application/Storefront/StorefrontSettingsContracts.cs:39`; controller và form truyền phiên bản, service kiểm tra bên trong khóa SQL tại `GaoApp.Infrastructure/Services/Storefront/StorefrontSettingsService.cs:210`. Probe nay nhận ConflictAppException.

5. **Manifest SQL thật:** sai lệch tên/định nghĩa trigger và dạng predicate SQL Server lưu đã được xử lý bằng chuẩn hóa định danh, literal được giữ nguyên, các cặp predicate nguyên vẹn đã kiểm tra và ba index shape cụ thể. Không có bỏ qua so sánh schema tổng quát. V3 đạt strict live SQL comparison, kiểm tra chuẩn hóa lặp ổn định và các hồi quy phủ định về filter/default/check.

Lịch sử tái hiện trước sửa nằm trong `review-initial.md` và `source-identity-probe.log`; kết quả V2/V3 nằm ở các log riêng.

## Kiểm chứng cuối

- **170/170 WEB01:** thực thi 170, đạt 170, lỗi 0, bỏ qua 0; đã đọc trực tiếp counters trong `../tests/v3/web01-v3.trx`.
- **8/8 hồi quy manifest hiện có:** bảy tên method sinh tám ca thực thi, đạt 8, lỗi 0, bỏ qua 0; `../tests/regression-v3/web01-regression-v3.trx`. Các ca gồm current baseline, rerun no-op và từ chối filter/default/check sai.
- Tester đã đóng kết quả **178/178** trong `../tests/summary-v3.json`: 28 tệp production, tám tệp test và bảy DLL giữ nguyên hash; năm database GUID có trước vẫn đúng năm tên sau kiểm thử, không để lại database GUID R1/R2 mới. Reviewer đã đọc summary và counters TRX hồi quy cuối.
- **6/6 probe reviewer:** `probe/Program.cs`, chạy `Review.dll --expect-rejected`, exit 0. Chỉ các SqlException/ràng buộc dự kiến và ConflictAppException được tính là từ chối đúng. Log: `v3-source-identity-probe.log`.
- Build graph V3 đạt 0 lỗi; có một xUnit2031 ở test InvoiceNotificationTransportTests hiện hữu, ngoài thay đổi WEB01. Probe reviewer build đạt 0 lỗi/0 warning.
- Model khớp snapshot; manifest có 197 bảng. Migration WEB01 tạo 23 bảng, 75 index, bảy khóa nguồn bổ sung và 13 trigger, gồm 12 trigger bất biến và một trigger nguồn dòng. Kiểm thử SQL fresh/upgrade/reapply, bảo toàn POS/menu/grant và downgrade guard đã đạt.

Probe chỉ dùng `InventoryPostingLocalDb`, database GUID mới có guard tên và cleanup. GUID cuối `GaoApp_R2_InventoryPosting_3FC439F02FFD4A89AD1AC58459DB42C8` đã được dọn; log kết thúc `Owned GUID database cleanup completed`. Reviewer không mở database cửa hàng, chạy full GaoApp.Web hoặc gọi seeder. Các lượt setup probe trước đó cũng cleanup trong finally.

## Ranh giới nghiệm thu

- Public host là project/process riêng, không tham chiếu Web quản trị, không nạp controller Admin/POS, cookie admin, worker, migration hoặc seeder khi startup. Ba ca HTTP process độc lập nằm trong bộ 170; thiếu cấu hình vẫn đóng và health không cần SQL.
- Controller có authorization/anti-forgery; service kiểm tra người dùng, quyền hiện tại, tenant và cửa hàng hoạt động. Không nhận StoreId/chủ thể nguồn từ client. Admin full startup/browser và HTTP anti-forgery round trip không được chạy trong review này; phần controller được đọc nguồn, service/quyền được kiểm thử.
- Cấu hình và hàng mới mặc định đóng/chưa publish; không thay hành vi bán tại POS. Quyền được khai báo độc lập menu; migration không tạo menu hoặc gán quyền vai trò. Đường dẫn admin là `/admin/storefront/settings` để người dùng tự quản lý menu.
- StoreServiceFee dùng cho giao gần và toàn quốc; chi phí giao riêng. Rate revisions và rowversion giữ được cấu hình cũ của quote và ngăn ghi đè từ màn hình cũ.
- Checkout khách vãng lai, quote authoritative, idempotency nghiệp vụ, giữ/xuất kho chung và bán âm thực tế, ngân hàng thật, delivery origin cho web, ghi sổ, refund và budget tổng nhiều dòng vẫn thuộc WEB04–WEB07. Chưa kiểm chứng tích hợp sản xuất hoặc UAT/mở bán WEB09.

Trong các ranh giới này, review độc lập không còn blocker đã biết. Việc ghi nhận tiền/kho thật và mở bán phải đi tiếp các cổng đã khóa trong hợp đồng WEB00.
