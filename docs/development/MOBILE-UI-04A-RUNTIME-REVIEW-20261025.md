# UI-04a · Kết quả chạy test và đánh giá giao diện

Ngày 25/10/2026. Người dùng yêu cầu chạy test sau lượt triển khai ban đầu. **Build và các kiểm tra tự động trong phạm vi UI-04a đều qua trên bản mã cuối.** Đã xem ảnh điện thoại, tablet/desktop và preview; đồng thời sửa mật độ nội dung điện thoại trong quá trình đánh giá.

| Kiểm tra | Kết quả | Bằng chứng |
|---|---|---|
| Build Release Web/Razor và runner | Thành công, 0 warning, 0 error | `.artifacts/ui-04a-tests/build-diagnostic.log` |
| SQL, phạm vi cửa hàng, quyền ghim và CSP | **10/10 passed**, 0 skipped | [TRX](../../TestResults/mobile-ui-04a-20261025/regression/orders-security-regression.trx) |
| Lọc sản phẩm, URL, phân trang, reset, phone | **1/1 passed** | `.artifacts/ui-04a-tests/product-filter-accepted.log` |
| Chrome với login/Razor/API thật và dữ liệu UI được kiểm soát | Qua toàn bộ probe danh sách đơn | `.artifacts/ui-04a-tests/browser-accepted.log` |
| Responsive | Qua **10 kích thước**, 320–1600 CSS px | [Kết quả browser](../../TestResults/mobile-ui-04a-20261025/browser-accepted/result.json) |
| Lỗi JavaScript / vi phạm CSP / lệnh tài chính | **0 / 0 / 0** | Cùng kết quả browser |

[runtime-review.json](mobile-ui-20261025/evidence/ui-04a/runtime-review.json) lưu kết quả, tên từng test, hash source và ảnh, phạm vi cách ly và phần còn lại.

## Đánh giá sau khi xem ảnh

Bản ban đầu không tràn ngang và các luồng hoạt động, nhưng bốn ô thống kê, tiêu đề lặp và phần lọc chiếm gần hết màn đầu. Ở 320 px, người dùng phải cuộn mới nhìn thấy tiền đơn đầu. Đã chỉnh **chỉ trên điện thoại**:

- Dùng tiêu đề của app bar; H1 trong nội dung giữ cho trình đọc màn hình bằng cách ẩn trực quan, desktop giữ nguyên.
- Gom cùng bốn chỉ số vào native disclosure **“Thống kê theo bộ lọc”**, mặc định đóng. Mở ra vẫn đầy đủ nhãn phạm vi/số liệu cũ, không tính hoặc sao chép chỉ số mới.
- Giữ tìm kiếm, nút tìm/lọc và ngày nhanh ở phần mặc định; “Chỉ đơn giao” và “Xóa bộ lọc” nằm trong bộ lọc mở rộng.
- Đặt tiền đơn cạnh khách/thời gian, thu gọn bộ điều khiển số dòng/tải lại. **Tiền đơn đầu đã nằm trên dock tại cả 320 và 390 px**, được kiểm tra bằng bounding box trong probe cuối.
- Giữ các node gốc và trả thống kê/các ô đơn về đúng vị trí khi trở lại desktop. Test xác nhận identity/thứ tự child node, không trùng form/nút và không thêm request do resize.

Ảnh bản cuối: [điện thoại 390 px](../../TestResults/mobile-ui-04a-20261025/browser-accepted/orders-390.png), [điện thoại 320 px](../../TestResults/mobile-ui-04a-20261025/browser-accepted/orders-320.png), [thống kê mở](../../TestResults/mobile-ui-04a-20261025/browser-accepted/overview-390.png), [xem nhanh](../../TestResults/mobile-ui-04a-20261025/browser-accepted/preview-390.png), [desktop 1366 px](../../TestResults/mobile-ui-04a-20261025/browser-accepted/orders-1366.png).

Đánh giá: thẻ đơn rõ khách/tiền/trạng thái hơn và màn đầu ưu tiên tra cứu; thống kê và thông tin phụ vẫn có thể mở khi cần. Màn 320 px xuống dòng mã/khách hợp lý; không ép cắt mất dữ liệu. Từ 768 px giữ bộ CSS/render desktop cũ. Probe xác nhận không tràn document hoặc ô đơn, nhưng chưa thực hiện so sánh pixel tự động với bản phát hành trước.

## Các luồng đã chạy

Chrome kiểm tra ở 1600×1000, 1440×900, 1366×768, 1024×768, 768×1024, 767×900, 430×932, 390×844, 360×800 và 320×740. Đã chạy tìm/lọc, lookup nhân viên/máy bằng chuột và bàn phím, ngày sai, reset, phân trang/page size, URL/reload, phản hồi chậm/cũ, rỗng/lỗi/thử lại, disclosure bằng Enter, xem nhanh, đóng/Escape/trả focus và các đích In/ACB/hoàn/hủy sẵn có.

Năm test SQL kiểm tra tên/SKU/barcode trước phân trang, nhiều bộ lọc, dữ liệu thanh toán thực, ID nhân viên/máy, phân biệt cửa hàng và nguồn đơn giao trước/sau chốt. Ba test quy tắc ghim kiểm tra danh sách cấu hình, ID sai/trùng/khác cây, tối đa bốn mục và nhánh quyền quản trị. Hai test CSP kiểm tra nonce, tài nguyên nội bộ và không nhúng CDN/inline handler.

Trong quá trình chạy, sửa thêm hai vấn đề của fixture: test lọc sản phẩm còn tìm root HTML theo chuỗi cũ, và mẫu receipt thiếu `grossAmount` khiến ảnh xem nhanh hiện giá gốc bằng 0. Fixture nay nạp đúng CSS/JS phone và dữ liệu giá gốc. Không đổi phép tính của ứng dụng. Browser probe bổ sung kiểm tra CSP, ảnh khi thất bại, thư mục output riêng và kiểm tra tiền đơn đầu nằm trên dock. Có lưu các lượt đầu thất bại để phân biệt lỗi fixture, hạn chế môi trường và vấn đề bố cục đã sửa.

## Cách ly và phần chưa xác minh

Build dùng `.artifacts/ui-04a-tests/build`. SQL/Web dùng fixture hiện có với database GUID tạm trên LocalDB, storage/key tạm và cổng loopback riêng. Không đọc/ghi database cửa hàng, không cập nhật migration/menu/ghim của cửa hàng. Chrome thường bị tắt bằng cờ riêng cho môi trường test; Playwright chỉ tạo và dọn Chrome headless của chính probe. Các cửa sổ/tab Chrome người dùng đang dùng không bị kiểm tra hay đóng.

Lượt đầu trong sandbox không truy cập được LocalDB/testhost IPC; sau khi xác minh kết nối local, chạy lại bằng quyền host cần thiết đã qua. Không đổi SDK, profile Visual Studio hoặc cấu hình ứng dụng để khắc phục việc này.

**29 file nghiệp vụ/nền được bảo vệ vẫn giữ hash**: renderer/filter/CSS desktop, controller, DTO/validator/repository, menu/layout/viewport, tenant/terminal, CSP và Chrome launcher/profile. View không thay đổi sau build. Hai asset phone và hai fixture test là các source được chỉnh trong lượt đánh giá.

[Ma trận 20 ca](mobile-ui-20261025/evidence/ui-04a/acceptance-matrix.json) đã ghi rõ: 10 ca qua với dữ liệu fixture, 9 ca xác minh một phần, 1 ca F5/Stop còn thử thủ công. Không coi các ca tổng hợp là hoàn tất chỉ từ test tự động.

Còn cần điện thoại thật Safari iPhone/Chrome Android, bàn phím/notch/zoom/forced colors/bfcache; menu POS đã cấu hình với nhiều vai trò; các tổ hợp giao hàng/cọc ở UI-04d và nghiệm thu F5/Stop riêng của UI-03e. Probe kiểm tra đường dẫn in/ngân hàng/hoàn/hủy bằng dữ liệu được kiểm soát, không thực hiện các nghiệp vụ đó ngoài đời. Các bước UI trước không được tự đánh dấu đã nghiệm thu.
