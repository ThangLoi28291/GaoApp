# Hoàn thành UI-21 — Nhân viên, phân quyền và thiết bị

Ngày 28/10/2026. Toàn bộ UI-21a–e đã triển khai và qua kiểm thử tự động; bước tiếp **UI-22a — in ấn và màn hình chuyên dụng**.

- UI-21a: danh sách và hồ sơ nhân viên, tạo/sửa nhân viên, danh sách/tạo/sửa vai trò; biểu mẫu và dữ liệu nháp giữ nguyên khi đổi kích thước.
- UI-21b: nhóm chức năng, tìm quyền, tổng kết thay đổi và hộp xác minh có vùng cuộn; giữ mật khẩu/lý do, readonly/disabled và luồng lưu gốc. Bổ sung hai nhãn CSKH còn thiếu tiếng Việt trong lớp hiển thị chung; không đổi mã hoặc cách cấp quyền.
- UI-21c: quầy POS, tạo khóa, khóa chỉ hiện một lần và thu hồi qua xác nhận gốc; danh sách không lộ khóa.
- UI-21d: tạo kiosk, khóa kích hoạt 30 phút, theo dõi và tạm dừng theo cửa hàng; chỉ có đúng một system user kiosk mới không được đăng nhập trong fixture.
- UI-21e: lọc audit, thẻ kết quả, chi tiết đã làm sạch và trạng thái rỗng; API, tenant và giới hạn truy vấn giữ nguyên.

Bao phủ 16 view/partial; 14 view chỉ thêm import/marker cho điện thoại. Hai partial bảo mật và nhóm quyền giữ nguyên source, nhận bố cục từ trang chủ. Native controller, service, form fields, action, token và handler giữ nguyên. Không tạo menu/seeder/quyền mới trong cửa hàng đang dùng. Bố cục desktop từ 768px và print được đối chiếu khi bật/tắt CSS điện thoại.

149/149 ca .NET duy nhất; 30 trạng thái/210 phone/60 desktop/30 in; hai probe trình duyệt gốc qua; 35 lượt hộp thoại, 5 kiểm tra cuộn, 26 ảnh được xem trực quan. Có 23 lượt đo cảnh báo/nhãn trạng thái, thấp nhất 5.99:1, đạt ngưỡng 4,5:1. Nút Hiện/Ẩn mật khẩu trên điện thoại dùng handler gốc và đã kiểm thử hai chiều. Có 160 lượt .NET, 8 lượt chưa qua được giữ trong lịch sử; mọi testcase chạy cuối đã qua. Không cộng chạy lại thành ca duy nhất. Bộ hồi quy gồm bảo mật nhân viên/role, CSRF, tenant, session revocation, kiosk, POS key, audit và schema P3. Hai probe gốc là role-permissions và admin-menu-permissions; probe menu được bổ sung thao tác xác nhận mobile đã có, các assertion giữ nguyên. Fixture nhân viên khai báo menu POS trong database test thay vì trông chờ menu cố định.

Lỗi migration là P3 đã có Up/Down tạo schema nhưng thiếu Designer target model và snapshot. Đã sinh metadata bằng EF, giữ nguyên byte Up/Down và ID 20261027010000_AddInvoiceNotificationsP3; không thêm migration tạo lại bảng. Hai ca SQL tạo mới/nâng cấp từ trước P3 qua, giữ dữ liệu đã có, không còn HasPendingModelChanges/pending migrations và chạy lần hai không tạo thêm thay đổi. Không ConfigureWarnings để che lỗi. Database cửa hàng đang dùng chưa bị cập nhật.

Kiểm thử chạy toàn bộ nguồn hiện tại: 2074 C# production qua input MSBuild thật; không shadow/rebind/EnsureCreated. Web assembly đã xác nhận đủ 16 Razor; hai asset HTTP khớp nguồn và gzip SDK giải nén khớp. Không khẳng định HTTP đã phục vụ gzip. Build C# gần nhất còn một cảnh báo xUnit2031 P3 có sẵn; lượt build asset cuối không lỗi và không cảnh báo. Test app/cookie/files/SQL đều riêng, provider AI/kênh tắt; Chrome đang mở giữ nguyên.

Kiểm tra dữ liệu native chứng minh các trường đã ghim của cửa hàng, menu/pins/visibility, nhân viên/membership, quyền vai trò đích và thanh toán không đổi; các thay đổi thiết bị/kiosk và system user không đăng nhập là thao tác chủ ý trong fixture. Chưa thử trên điện thoại thật: bàn phím, safe area, sao chép/chia sẻ khóa và xoay/zoom. Giữ 45 verification lịch sử và toàn bộ trạng thái chờ trước đó, không hồi tố phạm vi nghiệm thu cũ. Baseline 560 file: 371 không đổi/189 đã đổi/0 mất.

Đường dẫn chức năng để tự gán vào menu khi cần: /Admin/UserInStores, /Admin/Roles, /Admin/RolePermissions, /admin/pos-terminals, /admin/kiosks, /admin/audit-logs.

[Nguồn](mobile-ui-20261025/evidence/ui-21/static-review.json), [build](mobile-ui-20261025/evidence/ui-21/current-build-proof.json), [runtime](mobile-ui-20261025/evidence/ui-21/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-21/acceptance-matrix.json), [ảnh](mobile-ui-20261025/evidence/ui-21/visual-inspection.json).
