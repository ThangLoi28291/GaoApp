# UI-23 — Trang công khai và dự phòng

Hoàn tất UI-23a–d trên điện thoại (≤767.98 px), kiểm thử tự động đạt; tiếp theo UI-24a. Desktop/tablet từ 768 px giữ nguyên.

- **Hóa đơn:** thẻ thông tin, tóm tắt đơn, chọn cá nhân/doanh nghiệp, ô nhập và nút lưu; thông báo lỗi/đã tiếp nhận/không khả dụng rõ. Giữ nguyên một form, trường, validation, CSRF và JS xóa dữ liệu khi hết hạn/quay lại.
- **Chat khách:** header cửa hàng, chọn hội thoại, luồng tin, ô nhập hai dòng, trạng thái bot/chờ/nhân viên/đóng; viewport thu gọn không nhân đôi form hay làm mất nháp. Select/input dùng chữ 16 px trên phone. Chat web chỉ có API tin nhắn chữ; tính năng tệp nội bộ của UI-20 giữ nguyên.
- **Home/Privacy/Error:** nội dung phone bằng tiếng Việt và CTA đăng nhập; header/footer công khai riêng. Privacy vẫn POST; nút phone dùng form POST thật. Các error view đã có chuẩn UI-03 được giữ, dùng shell public, không đưa menu hay dữ liệu nhân viên vào trang khách. Nội dung Privacy là hướng dẫn liên hệ cửa hàng, không tự đặt chính sách pháp lý hoặc thời hạn mới.
- **Phạm vi:** 11 file baseline được kết luận: 6 view nâng cấp, 5 giữ có lý do; thêm _PublicPhoneHeader và CSS/JS public. Không sửa controller/service, quyền, cấu hình cửa hàng, menu, migration, desktop hoặc dữ liệu thật.

Kiểm thử đạt **79/79 ca .NET duy nhất** (kết quả mới nhất): 69 nghiệp vụ/bảo vệ dữ liệu, 8 HTTP/SQL, 2 native Chrome. Ma trận gồm **19 trạng thái, 133 lượt phone, 38 đối chiếu desktop**. Có ảnh phone/desktop và kiểm tra ô nhập/nút Gửi ở viewport 390×440. Probe invoice-buyer-self-service có sẵn đã qua: submit, no-store/no-referrer, hết hạn xóa PII, back/bfcache và thu hồi/thay liên kết.

HTTP/SQL xác nhận token hết hạn 410, khóa 409, sai/cross-store 404, từ chối CSRF và quyền không đủ. Privacy GET vẫn 405, POST hoạt động. Snapshot menu/store/grant/tổng đơn không đổi; chỉ cập nhật hóa đơn đang mở trong fixture. Fixture SQL dùng MigrateAsync, không pending model/migration; không EnsureCreated hay shadow/filter source. Toàn bộ **2075 C# production** có trong input MSBuild; hai asset/gzip SDK khớp source. Build cuối 0 lỗi/0 cảnh báo; lượt biên dịch đầy đủ còn cảnh báo xUnit2031 có sẵn.

Bằng chứng giữ 3 lượt thất bại trước khi sửa fixture/thời điểm đo và một lượt testhost bị sandbox chặn loopback; tất cả ca logic cuối đã qua. Các lần thử và TRX giữ riêng. Giữ nguyên 49 verification lịch sử cùng các trạng thái chờ trước đây. Chrome kiểm thử dùng profile/process headless mới và chỉ đóng browser do fixture tạo.

Chưa thay kiểm tra trên Android/iOS thật: bàn phím hệ thống, safe area và hành vi trình duyệt thật. Không tự triển khai UI-24 hoặc publish.

[Nguồn và kết luận 11 view](mobile-ui-20261025/evidence/ui-23/static-review.json), [build](mobile-ui-20261025/evidence/ui-23/current-build-proof.json), [kiểm thử và ảnh](mobile-ui-20261025/evidence/ui-23/runtime-review.json), [ma trận](../../TestResults/mobile-ui-23-20261028/browser/runtime-review.json).
