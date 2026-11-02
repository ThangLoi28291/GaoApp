# Đồng bộ ảnh, tệp và nhóm Zalo vào CSKH GaoApp

> Đã có nâng cấp tiếp theo gửi ảnh/tệp ra Zalo: xem CUSTOMER-SERVICE-ZALO-OUTGOING-MEDIA.md. Phạm vi và hướng dẫn publish bên dưới mô tả riêng bản đồng bộ chiều nhận.

Ngày 24/10/2026. Nối tiếp CSKH A–F. Nâng cấp nằm chủ yếu trong GaoApp; dùng API Inbox/Media/Replies/Jobs hiện có của ZaloMarketing. ChatbotLocal và Worker Zalo không đổi trong lần này.

## Sử dụng

Mở /admin/customer-service. Bộ lọc Nguồn hội thoại có Web, Messenger, Zalo cá nhân và Nhóm Zalo. Nhóm hiện tên nhóm, tên thành viên gửi và trích dẫn nguồn khi có. Khi nhân viên tiếp nhận rồi trả lời, hệ thống dùng đúng conversation ID và sequence của nhóm, giữ tài khoản Zalo đã gắn cho kết nối.

Ảnh có thể xem trong tin nhắn, mở lớn hoặc tải xuống. Tệp hiện tên/kích thước và được tải qua endpoint GaoApp có kiểm tra đăng nhập/quyền/cửa hàng. Ảnh/tệp xuất hiện muộn được bổ sung vào cùng tin đã lưu, không nhân đôi tin. Ảnh lỗi có nút Thử lại.

Với tin cũ từ bước E chưa lưu định danh nguồn trên từng dòng chat, hệ thống bổ sung mục **Ảnh và tệp từ lịch sử Zalo**. Mục này dùng chính định danh hội thoại/tin/tệp nguồn, không đoán tệp thuộc dòng chat nào dựa trên tên hoặc nội dung. Tệp gửi trực tiếp bằng ứng dụng Zalo cũng được bổ sung vào mục này, có nhãn Bạn (Zalo). Không tạo lượt gửi lại từ dữ liệu lịch sử.

Nhóm luôn vào hàng chờ nhân viên; không cho chuyển nhóm sang bot. Tin ảnh/tệp cá nhân vẫn chuyển người xử lý. Không gửi nội dung nhị phân vào AI và không thêm OCR trong lần nâng cấp này. Gửi ảnh/tệp từ GaoApp ra Zalo chưa thuộc phạm vi; trả lời văn bản tới cá nhân/nhóm vẫn dùng hàng đợi hiện có.

## Kết nối

- API client riêng gắn đúng Sender Profile, scopes inbox.read, inbox.reply, jobs.read.
- ZaloMarketing.Web cần bản E có header X-ZaloMarketing-Sender-Profile-Id; GaoApp kiểm tra header cả khi đọc JSON và khi tải media.
- Giữ AccountId, URL HTTPS, token file và binding cửa hàng hiện có. Không đưa token vào URL/browser hoặc đổi tài khoản theo query/header phía gọi.
- Worker Zalo phải đang chạy để đồng bộ Zalo Web. Nếu chưa có bản nhiều tài khoản/nhóm/media trước đây thì nâng đủ Web + Worker + migrations theo các hướng dẫn tương ứng trước.

## Giới hạn và lưu trữ

API nguồn cung cấp tối đa 50 tin/hội thoại, 25 hội thoại/trang; vòng quét hiện có giới hạn 100 trang và kiểm tra theo chu kỳ 30 giây. Đây không phải công cụ tải toàn bộ lịch sử Zalo. Tin nhóm trong cửa sổ nguồn được đưa về nhân viên; tin cũ ngoài giới hạn ghi nhận/retention không được khôi phục tự động.

Một tin tối đa 20 mô tả tệp; tải tệp tối đa 30 MiB theo kho media Zalo. Không đọc URL đính kèm tùy ý. Chỉ PNG/JPEG/GIF/WebP có chữ ký phù hợp được xem ảnh trực tiếp; các tệp còn lại trả dạng tải xuống. Media chưa được Worker lưu sẽ báo chưa sẵn sàng và có thể thử lại.

GaoApp chỉ lưu metadata và định danh media trong SQLite CSKH, tải nội dung theo yêu cầu qua backend; không sao chép kho tệp Zalo sang thư mục public. Metadata và bộ sưu tập lịch sử (tối đa 50 mục/hội thoại) xóa cùng hội thoại theo retention CSKH 1–7 ngày. Khả năng tải còn phụ thuộc tệp nguồn còn tồn tại tại ZaloMarketing.

## Publish

1. Dừng đúng app pool GaoApp và sao lưu release, cấu hình, Data Protection keys và SQLite CSKH nhất quán. Không chỉ copy riêng file DB khi WAL đang ghi.
2. Publish **GaoApp.Web Release** bằng profile hiện có. Giữ nguyên cấu hình Production/SQL nghiệp vụ, token, callback, URL/Issuer và dữ liệu ngoài publish.
3. Khởi động đúng app pool. SQLite CSKH tự bổ sung ba bảng metadata/nguồn/lịch sử và lên schema 5. Không có migration SQL nghiệp vụ hoặc SQL Zalo mới.
4. Nếu ZaloMarketing.Web trên server đã có bản E thì không cần publish lại Zalo. Nếu chưa có header xác nhận tài khoản thì publish Web Zalo trước. Worker giữ phiên và tiếp tục chạy.
5. ChatbotLocal không cần publish lại cho riêng nâng cấp này. Không đổi model/key/quota.
6. Nghiệm thu bằng tài khoản TEST: nhận ảnh/tệp cá nhân, nhận nhóm có hai thành viên, xem/tải tệp, trả lời chữ đúng nhóm, kiểm tra quyền cửa hàng khác và trạng thái gửi. Chỉ bật AutoReply cho cá nhân sau nghiệm thu; nhóm vẫn do nhân viên xử lý.

Không tự thêm menu hoặc cấp quyền. Menu/sidebar do admin tự cấu hình; quyền View/Reply/Manage hiện có tiếp tục áp dụng. Nâng cấp source và kiểm thử giả không đồng nghĩa đã publish hoặc gửi thử trên server thật.

## Kiểm thử

- GaoApp: nhóm/metadata/người gửi/trích dẫn, ảnh/tệp đến muộn, lịch sử cũ, chống trùng, schema 4→5 và retention.
- HTTP: đăng nhập/quyền thật, tách cửa hàng, tải tệp đúng hội thoại, token giữ ở backend, sai profile/asset bị chặn.
- Transport: origin cố định, timeout/kích thước/MIME/chữ ký ảnh, không theo redirect; HTML tải dưới dạng tệp.
- ZaloMarketing: hai ca API cá nhân/nhóm đọc ảnh/tệp, tên thành viên, header tài khoản và trả lời đúng đích.
- Edge: máy tính/điện thoại, xem ảnh/tải tệp, lọc nhóm, tên tệp không thực thi HTML, ảnh lỗi/thử lại, thư viện lịch sử.

Bộ chạy ở repository ChatbotLocal: scripts/Test-CustomerService-ZaloMediaGroups.ps1. Báo cáo và ảnh minh họa dùng dữ liệu giả, không gọi AI/provider thật.
