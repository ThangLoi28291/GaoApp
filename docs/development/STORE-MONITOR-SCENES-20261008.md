# Tích hợp cảnh máy móc 2D đã duyệt

Ngày 08/10/2026, thay hình minh họa nhỏ trên `/admin/store-monitor` bằng các cảnh 2D giống bản mẫu được người dùng duyệt. Các cảnh gồm POS/quét hàng/in bill, nhận hàng với pallet và máy tính, kiểm kê ở kệ, chuyển kiện giữa kho, in/chuẩn bị tem, bàn đọc phiếu và đóng dấu. Không có thanh tiến độ hoặc dữ liệu mô phỏng trong mã chạy thật.

## Nối dữ liệu và hiệu ứng

- Giữ nguyên SSE, xác thực/quyền/cửa hàng, registry và nguồn chi tiết sau lưu. Không sửa backend, nghiệp vụ hoặc migration.
- Giữ nhóm nhân viên + module + quầy + phiếu. POS có tối thiểu ba vị trí; hiện thêm khi thực tế có nhiều hơn. Các khu khác không giới hạn số nhân viên/phiếu trong một khu.
- Máy POS dùng chi tiết đã lưu để hiển thị hàng/tổng; mã phiếu và nội dung đầy đủ nằm dưới hình. Không suy ra phần trăm hoặc tổng lượng đã xử lý từ thông báo.
- Ánh xạ hiệu ứng theo `event.action`: quét/thêm POS, chốt đơn nhả bill, ghi kiểm kê, ghi chuyển kho, gửi lệnh in, nhập/chỉnh phiếu và duyệt thành công. Thanh toán một phần không nhả bill; xác nhận/hủy in không in thêm; gửi duyệt/từ chối/lưu nháp không đóng dấu.
- Hiệu ứng vật dụng hữu hạn, chạy một lần, phát lại riêng khi có sự kiện/tín hiệu mới. Snapshot trùng và ô bên cạnh không khởi động lại mô hình. Hết hạn, mất kết nối hoặc tab ẩn sẽ dừng. Nút hiệu ứng phản ánh đúng trạng thái, có thể bật chủ động khi hệ điều hành yêu cầu giảm chuyển động.
- Đổi đơn trên cùng POS không dùng bill/chi tiết của đơn trước để mô tả đơn đang mở. Bấm/Enter/Space trên ô lọc dòng thời gian theo nhân viên.
- Cảnh CSS nhẹ, không có thư viện mô hình/WebGL hoặc vòng render liên tục. Việc cập nhật thời gian/SSE vẫn theo cơ chế cũ; không khẳng định FPS/CPU trên mọi máy văn phòng chưa đo.

## Kiểm chứng trong lượt này

- `dotnet test GaoApp.Tests/GaoApp.Tests.csproj --no-restore --filter 'FullyQualifiedName~StoreActivityTests|FullyQualifiedName~StoreActivityDetailsTests|FullyQualifiedName~StoreActivityEnricherTests|FullyQualifiedName~StoreMonitorHttpTests'`: **47/47 đạt**, không bỏ qua; build Web/Tests thành công. TRX: `Logs/store-monitor-upgrade/integrated-2d-tests/monitor-scenes.trx`.
- `node --test GaoApp.Tests.Browser/store-monitor.scenes.test.cjs`: **9/9 đạt**. Bao gồm các điều kiện tránh phát nhầm bill/tem/dấu duyệt, loại kho, tên hàng/chi tiết và escape HTML.
- Kiểm tra trình duyệt qua bản sao Razor đã thay URL/resource để chạy fixture cục bộ, dùng chính CSS/JS sản phẩm: **40 kiểm tra đạt**. Bao gồm 11 công việc, một người trên hai phiếu, ba quầy/không bỏ quầy thứ tư, ba vị trí khi rỗng, chuyển động thực sự một lần, snapshot ổn định, lưu liên tiếp, đơn mới, hết hạn tương tác, mất mạng, escape nội dung, không animation chờ lặp. Bấm ô Mai lọc đúng hai phiếu của người đó.
- Kiểm tra 1366×900 và 390×900, nền sáng/tối. Ở kích thước nhỏ: `scrollWidth = clientWidth = 375`, không mô hình nào bị cắt. Mô hình căn giữa và co theo ô. Danh sách nhiều việc cần cuộn trang; không ép tất cả vào 1080px bằng cách thu nhỏ thành biểu tượng.
- Ảnh: `Logs/store-monitor-upgrade/integrated-2d-ui/office-light.jpg`, `office-dark.jpg`, `mobile.jpg`. Ảnh và fixture dùng dữ liệu mẫu, không chứng minh thao tác máy in/kho vật lý. Không đưa dữ liệu mẫu vào registry/SQL hoặc mã runtime thật.
- Cập nhật browser probe có luồng Web/SQL sẵn có sang kiểm tra cảnh mới; đã kiểm tra cú pháp, **không chạy lại toàn bộ luồng đăng nhập browser probe** trong lượt này. Các test .NET và fixture UI phía trên là bằng chứng đã thực thi.

Giao hàng tiếp tục hiện “Chưa kết nối”. Thay đổi hiện ở working tree, không tự triển khai lên server hoặc tạo commit/PR.
