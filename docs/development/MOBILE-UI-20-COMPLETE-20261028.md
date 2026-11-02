# Hoàn thành UI-20 — Chăm sóc khách hàng, trợ lý và media

Ngày 28/10/2026. Toàn bộ UI-20a–f đã triển khai và qua kiểm tra tự động trên nguồn/schema hiện tại; tiếp **UI-21a — nhân viên và nhóm quyền**.

- UI-20a/b: hộp thư từ danh sách tới hội thoại điện thoại, chủ hội thoại/trạng thái/draft, hồ sơ và media giữ đúng ngữ cảnh; vùng soạn dùng viewport/safe area gốc.
- UI-20c/d: kênh và cấu hình giữ che thông tin tài khoản; báo cáo/chất lượng/kiến thức/tìm hàng được chia thẻ và biểu mẫu rõ, mở bằng chứng trước các lệnh native.
- UI-20e: trợ lý nổi theo viewport/layer/dock, giữ ngữ cảnh, draft và xác nhận gốc.
- UI-20f: thư viện ảnh/tệp và màn upload giữ native policy, quyền, lựa chọn và tiến trình.

299/299 .NET duy nhất, 3 Node, 41 trạng thái/287 phone/82 desktop/41 in; 21 lượt hộp thoại và 6 kiểm tra cuộn. Đã kiểm tra trực quan 24 ảnh gắn hash. Có 303 lượt .NET; 3 lượt native chưa qua được giữ trong lịch sử; gồm một lỗi checkbox bị vùng vuốt che và hai lỗi đồng bộ của bộ kiểm thử. Đã sửa đúng nguyên nhân, testcase chạy cuối đã qua. Không cộng lượt chạy lại thành test duy nhất. Điện thoại: 320×568, 360×780, 390×844, 430×932, 600×900, 767×900 và 390×440; desktop 768/1440px và print đối chiếu computed CSS/hình học.

Kiểm thử dùng toàn bộ nguồn Web/Application/Domain/Infrastructure hiện tại và schema SQL GUID mới tạo bằng EnsureCreated; không dùng paired snapshot, không lọc hoặc thay thế nguồn production. Mười bản test HTTP giữ nguyên toàn bộ assertions và các byte khác, chỉ đổi binding FullApplicationFixture sang Ui20CurrentModelApplicationFixture để chạy trên schema hiện tại. Fixture migration nghiêm ngặt gốc giữ nguyên; checkpoint đó vẫn bị EF PendingModelChanges trước các assertion UI/nghiệp vụ. Kết quả này không nghiệm thu migration hoặc cơ sở dữ liệu cửa hàng đang dùng, không bỏ qua cảnh báo EF. SQLite CSKH, file thử và các provider giả lập chỉ nằm trong fixture riêng; không gửi tin đến khách, AI hay kênh thật.

Build cuối: 1 cảnh báo kiểm thử P3 có sẵn, 0 lỗi. Các yêu cầu HTTP identity/gzip đều nhận đúng nội dung nguồn; middleware hiện trả identity. Tệp gzip do SDK tạo đã được đối chiếu riêng, không khẳng định đã phục vụ gzip qua HTTP.

Native SQL/SQLite/file và quyền được xác minh bằng các assertion fixture đã ghim trong [runtime](mobile-ui-20261025/evidence/ui-20/runtime-review.json). Các lệnh nhạy cảm, phê duyệt cấu hình, retry kênh và gửi thật không được thực thi đối với cửa hàng đang dùng. Không thêm menu/seeder/migration/quyền; controller/service và các owner nghiệp vụ giữ nguyên. Toàn bộ browser dùng profile headless mới, giữ các cửa sổ/tab Chrome đang mở.

Baseline: 385 không đổi/175 đã đổi/0 mất trên 560 file. Ghi riêng thay đổi có sẵn đầu bước; không gán chúng cho UI-20. Giữ 44 verification và tất cả trạng thái chờ từ trước.

Còn nghiệm thu điện thoại thật, chọn tệp/microphone, bàn phím/safe area, zoom/xoay và migration trên workspace. [Nguồn](mobile-ui-20261025/evidence/ui-20/static-review.json), [build](mobile-ui-20261025/evidence/ui-20/current-build-proof.json), [ma trận](mobile-ui-20261025/evidence/ui-20/acceptance-matrix.json), [ảnh](mobile-ui-20261025/evidence/ui-20/visual-inspection.json).
