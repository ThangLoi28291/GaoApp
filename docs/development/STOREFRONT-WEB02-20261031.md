# WEB02 Quản lý sản phẩm đăng website

Ngày hoàn tất: 31/10/2026, bắt đầu 30/10. **Cổng WEB02 đạt: code, test và review độc lập hoàn tất sau sửa.** Phạm vi [W02T01–W02T04](STOREFRONT-IMPLEMENTATION-PLAN-20261030.md); không mở bán hoặc triển khai lên dữ liệu cửa hàng.

## Chức năng đã có

Đường dẫn quản trị `/admin/storefront/products`, quyền `storefront.products.view` và `storefront.products.manage`. Người dùng tự tạo menu khi cần; không chèn menu hoặc tự gán quyền vai trò.

- Danh sách hàng gốc, tìm/lọc đã đăng/ẩn/chưa cấu hình, phân trang; bật/tắt từng sản phẩm và tối đa 100 lựa chọn mỗi đợt với kết quả rõ theo từng dòng.
- Tên hiển thị, mô tả plain text, nhóm trưng bày, thứ tự; lựa chọn media từ thư viện đúng cửa hàng, không nhận đường dẫn do client tự gửi.
- Biến thể, đơn vị gốc và các đơn vị quy đổi là các lựa chọn riêng. Cửa hàng có thể chỉ bán lốc/thùng. Migration `20261030230000_AddStorefrontBaseUnitPublication` thêm `BaseUnitIsPublished=false`, không tạo quy đổi giả và chặn downgrade nếu còn lựa chọn đơn vị gốc có ý nghĩa.
- Kg đúng đơn vị bán; hàng ẩn có thể giữ cấu hình nháp thiếu kg. Bật chế độ kg kiểm tra các lựa chọn thực sự đăng bán; đăng lại bắt buộc đủ kg.
- Giá lẻ qua chính sách POS hiện có; không nhân lại đơn giá thùng với hệ số. Catalog/Search/Get/Resolve dùng chung kiểm tra tenant, trạng thái nguồn, xuất bản, đơn vị, kg và giá. Đơn cũ dùng snapshot, không bị xóa khi ẩn hàng.

Lớp catalog là đầu vào cho WEB03 và WEB04; public media có URL logic `/shop/media/{id}`, phục vụ file có kiểm tra quyền hiển thị được thực hiện ở WEB03. Public UI/checkout chưa được tính là hoàn thành ở WEB02.

## Lỗi phát hiện và sửa trước khi đạt

Reviewer độc lập tái hiện P1: media chỉ được website tham chiếu bị thư viện coi là chưa dùng, tự đặt hạn dọn rồi xóa. Đã đưa mọi liên kết StorefrontMedia chưa xóa vào usage detection, các bộ lọc và liên kết sản phẩm của thư viện. Hàng ẩn/ngừng hoạt động vẫn giữ file. Chọn media final đang chờ dọn lấy đúng khóa `gao-media-cleanup`, đọc lại asset và xóa deadline trong cùng transaction với ghi liên kết; rollback giữ deadline và liên kết cũ. Không hồi sinh media temp, đã xóa hoặc không an toàn.

P2: lựa chọn con của hàng/biến thể đang ẩn chặn bật kg. Đã kiểm tra đúng các cổng xuất bản; draft được giữ nhưng đăng lại phải hợp lệ. Việc lưu hàng và đổi cấu hình cùng khóa cửa hàng để tránh tranh chấp kg.

Các lỗi dựng dữ liệu test đầu được tester sửa riêng, có precondition và nhật ký; không thay kỳ vọng để ép đạt.

## Kết quả độc lập cuối

| Bộ kiểm tra | Kết quả thực chạy |
|---|---:|
| WEB02 mới: catalog/giá, quản trị, quyền, CSRF, Razor, đồng thời, media và migration | 138/138 |
| Hồi quy WEB01 | 170/170 |
| Hồi quy schema đã chọn | 8/8 |
| Thư viện media hiện hữu, gồm SQL scale | 26/26 |
| Tổng ca khác nhau cuối | **342/342, 0 lỗi, 0 bỏ qua** |

Tester `storefront_tests` ghi oracle trước API, không sửa production. Reviewer `storefront_review` không sửa code hoặc test; tái hiện lỗi trên DLL cũ, kiểm chứng bản sửa trên DLL cuối và xác nhận **không còn P0/P1/P2 chưa xử lý trong phạm vi WEB02**. Probe chứng minh media giữ qua cleanup, claim rollback và kg/hide/republish đúng.

18 file production, 9 file test/helper và 6 DLL không đổi hash trong lượt cuối. Cùng 5 database GUID có trước được giữ nguyên; database mới dọn hết. Chín thư mục media fixture thuộc artifacts đã được dọn với kiểm tra đường dẫn/hash. Không chạm uploads hoặc database cửa hàng. Scale fixture 32.001 sản phẩm/16.006 live assets đạt giới hạn test; không coi số đo fixture là bảo đảm hiệu năng sản xuất. Quy tắc thuần WEB00 hồi quy riêng 378/378.

Build có 0 lỗi, chỉ warning xUnit2031 có sẵn ngoài WEB02. Restore dùng cache và tắt audit truy cập feed cho lượt offline; không đổi package. Output `.artifacts/storefront-rollout/build` không đụng DLL của ứng dụng đang mở.

Bằng chứng lưu: [báo cáo test](storefront-web02/test-final.txt), [review độc lập](storefront-web02/review-final.md), [hash production](storefront-web02/production-sha256.json), [kiểm tra identity và cleanup](storefront-web02/identity-cleanup.json). Log/TRX/probe gốc nằm trong `.artifacts/storefront-rollout/WEB02/`.

WEB02 đã đóng; tiếp tục WEB03 theo [quy trình tuần tự](STOREFRONT-ROLLOUT-20261030.md). Không commit/push/deploy, không tự đăng sản phẩm thật hoặc mở bán.
