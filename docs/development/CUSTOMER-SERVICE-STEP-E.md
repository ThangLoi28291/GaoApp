# CSKH bước E — Messenger và ZaloMarketing

Source nối tiếp A–D. Khách từ Facebook Page và Zalo cá nhân qua ZaloMarketing vào hộp thư `/admin/customer-service`, có nhãn nguồn và trạng thái gửi. Quản lý tại `/admin/customer-service/channels` theo quyền CSKH hiện có; không thêm menu hoặc tự cấp quyền.

- Facebook: webhook HMAC raw bytes + đúng Page/host/cửa hàng, handshake text; xử lý nền và chống trùng.
- Zalo: đọc Inbox/Replies/Jobs đã có của ZaloMarketing. Header xác nhận Sender Profile trong bản Web mới bắt buộc khớp token/tài khoản. Không yêu cầu Zalo OA; không dùng tìm theo tên/phone hoặc API chiến dịch để trả lời.
- Outbox bền vững, giữ đích và mã yêu cầu. `202` của Zalo chỉ là đã xếp job; GET trạng thái không tạo tin mới. Facebook chưa rõ kết quả dừng để Manage đối chiếu. Có tạm dừng và xử lý gửi lại có xác nhận.
- AutoReply mặc định false; bật riêng từng kết nối sau thử. Dùng customer API ChatbotLocal C với scope nguồn A/B hiện có. Chặn kết quả AI về muộn, chuyển người khi thiếu nguồn/ảnh/tệp/dữ liệu riêng.
- Không tự gắn danh tính kênh vào CustomerId hoặc mở grant D. Không nhập nhóm/đọc media, gửi tệp hoặc gửi ngoài cửa sổ hỗ trợ. Một account chỉ gắn một store.
- SQLite CSKH bổ sung bảng bằng khởi tạo, không migration SQL nghiệp vụ. Bảo vệ DB/WAL/SHM, Data Protection keys và token file ngoài publish. Không đổi các luồng hóa đơn/marketing Zalo đã có.

Publish ZaloMarketing.Web mới trước rồi GaoApp.Web; ChatbotLocal cần bản C nếu server chưa có. Cấu hình và bài nghiệm thu thật ở repo ChatbotLocal `docs/customer-service-step-e/README.md`, mẫu `customer-channels.example.json`. Chưa cấu hình hoặc gửi tài khoản thật trong nghiệm thu source. Giữ runtime, URL, Issuer và key đang vận hành; không mở ChatbotLocal ra Internet.

Kết quả source ngày 24/10/2026: 170/170 ca GaoApp (44 ca E nằm trong tổng), 26/26 ca hợp đồng ZaloMarketing và 35/35 ca Edge C/D/E. Dữ liệu giả lập, không gửi tin thật hoặc publish server. Manifest/hash và báo cáo nằm trong gói bước E của repo ChatbotLocal.
