# Gửi ảnh và tệp từ CSKH GaoApp tới Zalo cá nhân / nhóm

Ngày 24/10/2026. Nối tiếp bản đồng bộ ảnh/tệp/nhóm vào hộp thư. Nâng cấp này bổ sung chiều gửi ra; cần cập nhật **GaoApp.Web + ZaloMarketing.Web + ZaloMarketing.Worker + migration SQL Zalo**. ChatbotLocal không cần cập nhật runtime.

## Sử dụng

Mở `/admin/customer-service`, chọn hội thoại **Zalo cá nhân** hoặc **Nhóm Zalo**, bấm **Tôi tiếp nhận** rồi **Đính kèm**. Chọn một ảnh hoặc tài liệu, có thể nhập lời nhắn và bấm **Gửi trả lời**. Với nhóm, ảnh/tệp và lời nhắn được gửi tới cả nhóm. Tên tài khoản Zalo và định danh đích được lấy từ kết nối/hội thoại đã lưu, không nhập số điện thoại hoặc ID từ trình duyệt.

- Ảnh JPG/JPEG/PNG tối đa 10 MiB; xem trước và bỏ tệp trước khi gửi.
- Tài liệu PDF, DOC/DOCX, XLS/XLSX tối đa 20 MiB. Mỗi lượt một tệp; tên tệp tối đa 120 ký tự.
- Kiểm tra chữ ký nội dung, không chỉ đuôi tệp/MIME do trình duyệt khai báo.
- Lời nhắn là tùy chọn, tối đa 4000 ký tự Unicode. Zalo gửi ảnh/tệp trước và chữ thành khung tiếp theo; đây không phải chú thích được nhúng vào tệp hay trích dẫn tin cũ.
- Xem lại ảnh/tải tệp từ tin đã tạo trong GaoApp. Các URL yêu cầu đăng nhập, quyền View và đúng cửa hàng/hội thoại.

Nhân viên cần cả quyền View và Reply, đã tiếp nhận đúng hội thoại; nhóm vẫn do nhân viên hỗ trợ. Kênh, cấu hình CSKH cửa hàng phải đang bật và kết nối không tạm dừng. Không thêm menu hoặc tự cấp quyền.

## Hàng đợi và kết quả

GaoApp lưu nội dung nhị phân, SHA-256, tên/loại/kích thước, lời nhắn và outbox trong cùng giao dịch SQLite. Một lượt gửi chụp lại connection/binding, conversation ID và sequence tin nhận. Khi có tin mới hoặc tài khoản thay đổi, không tự chuyển đích của lượt cũ.

Một mã yêu cầu chỉ tạo một tin. Sau timeout/phản hồi 5xx, giao diện giữ tệp và mã yêu cầu để thử lại cùng lượt. Backend dùng `Idempotency-Key: gao-cskh-<outbox-id>` và giữ nguyên bytes/SHA khi lặp API trong cửa sổ 24 giờ (GaoApp ngừng tạo lại sau 23 giờ). Khi đã có jobId, chỉ GET kết quả, không tải lại ảnh/tệp để gửi lần nữa.

`Chờ gửi` / `ZaloMarketing đã nhận` chưa có nghĩa đã gửi. Chỉ sau xác nhận job Sent mới hiện Đã gửi qua kênh; đây cũng chưa phải xác nhận người nhận đã đọc. Nếu ảnh/tệp hoặc lời nhắn chưa rõ kết quả, NeedsReview/PartiallySent hiện **Cần kiểm tra** ở Kênh liên hệ. Worker không tự gửi lại thao tác Zalo chưa rõ kết quả. Hàng đợi giữ thứ tự cho cùng hội thoại, lượt bị lỗi/cần kiểm tra chặn lượt sau cho đến khi quản trị viên xử lý.

Worker kiểm tra chính xác native ID, loại cá nhân/nhóm, tiêu đề, ô soạn trống; cá nhân còn cần tin nhận làm căn cứ đang hiển thị. Kiểm tra lại ngay lúc gắn tệp/thả tệp và sau xác nhận khung gửi mới. Không tìm đích theo tên hiển thị và không ghi đè tin nháp. Nếu Zalo Web không tải được đích/căn cứ, lượt gửi dừng hoặc cần kiểm tra; không bảo đảm gửi mọi lịch sử quá cũ.

## API mới

`POST /api/v1/inbox/conversations/{conversationId}/replies/media`

Bearer API client gắn đúng Sender Profile; scope **inbox.reply**. Không cần thêm group_jobs.create để trả lời nhóm đã đồng bộ. Dùng **jobs.read** theo dõi kết quả và **inbox.read** đọc hội thoại.

Multipart fields:

| Trường | Giá trị |
|---|---|
| ReplyToSequence | Sequence tin nhận thuộc hội thoại |
| DeliveryMode | Image, File, ImageThenText, FileThenText |
| Text | Bắt buộc khi ThenText; bỏ trống với Image/File |
| Asset | Đúng một ảnh/tệp |

Header `Idempotency-Key` bắt buộc. Text và media dùng chung namespace chống trùng của Inbox; thay bytes, tên, loại, lời nhắn hoặc đích với key cũ trả 409. Tệp được kiểm tra trước replay nhưng chỉ stage sau khi xác minh đích và admission.

Phản hồi 202 có jobId/conversationId/replyToSequence/status/idempotencyReplayed và header `X-ZaloMarketing-Sender-Profile-Id`. GaoApp kiểm tra cả header và định danh phản hồi. Poll `/api/v1/jobs/{jobId}`; media-only cá nhân trả deliveryMode Image hoặc File. Endpoint JSON `/replies` tiếp tục dùng cho trả lời chữ.

GaoApp endpoint nội bộ, cookie + CSRF + quyền Reply:
`POST /admin/customer-service/conversations/{id}/media-replies`, multipart RequestId, Version, Caption tùy chọn, Asset.
Đọc tệp nội bộ: `GET /admin/customer-service/conversations/{id}/messages/{message}/outgoing-media?download=true`.

## Lưu trữ

SQLite CSKH tự lên schema **6**, thêm CskhOutgoingAssets chứa BLOB ngoài thư mục public. Tệp xóa cùng tin/hội thoại khi hết retention hiện có (1–7 ngày). Không tạo bảng mới trong SQL nghiệp vụ GaoApp. Dung lượng logic mặc định 200 MiB/cửa hàng, tổng 1 GiB, kiểm tra trong cùng giao dịch; đầy trả lỗi rõ ràng, không tạo tin/outbox thiếu tệp.

Cấu hình tùy chọn (giữ nguyên DatabasePath/StorageId và thư mục dữ liệu bền vững đang dùng):

```json
{
  "CustomerService": {
    "MaximumOutgoingMediaBytesPerStore": 209715200,
    "MaximumOutgoingMediaBytesTotal": 1073741824
  }
}
```

Đây là giới hạn tổng độ dài tệp đang lưu, không phải kích thước vật lý SQLite/WAL trên đĩa. Sao lưu SQLite nhất quán cùng WAL theo runbook lưu trữ hiện có; không thay database bằng tệp trống khi publish. Zalo lưu bản tệp trong protected MessageAssets theo cơ chế/quota/cleanup hiện có.

## Publish

1. Tạm dừng kết nối Zalo tại `/admin/customer-service/channels`, dừng đúng app pools GaoApp/Zalo và một Worker Zalo đang vận hành. Sao lưu release, SQL Zalo, SQLite CSKH nhất quán, token/config, Data Protection keys và kho tệp/phiên Zalo ngoài publish.
2. Database Zalo cần có migration `20261014085628_MultipleZaloAccounts`. Áp dụng `docs/deployment/inbox-reply-media.sql` để lên `20261024043102_InboxReplyMedia` hoặc chạy migration bằng quy trình quản trị hiện có. Script idempotent và dừng nếu thiếu phiên bản nền. Không chạy script lên SQL nghiệp vụ GaoApp.
3. Publish Release **ZaloMarketing.Web và ZaloMarketing.Worker** cùng phiên bản, giữ cấu hình Production/kho tệp/Chrome profile và token hiện có. Khởi động một Worker host, tránh chạy song song bản cũ và mới. Web/Worker cũ không hỗ trợ gửi media cá nhân theo luồng này.
4. Publish Release **GaoApp.Web**, giữ đường dẫn dữ liệu bền vững và StorageId nếu đã cấu hình, token file và Sender Profile/binding hiện có. Nếu bản source có nâng cấp lưu trữ khác, hoàn tất hướng dẫn của nâng cấp đó trước khi mở app. Không thay appsettings Production bằng cấu hình thử nghiệm.
5. Cho phép multipart ít nhất 22 MiB qua IIS/reverse proxy trên cả GaoApp và Zalo Web (ví dụ 25 MiB); giới hạn ứng dụng vẫn là ảnh 10/tệp 20 MiB. Không mở kho tệp thành static/public folder.
6. Khởi động app pools, kiểm tra quyền View/Reply, bật lại kết nối và nghiệm thu bằng tài khoản/nhóm TEST: ảnh không chữ, PDF kèm chữ, cá nhân và nhóm, khởi động lại trong lúc chờ, kết quả chưa rõ, tài khoản cửa hàng khác bị chặn. Không xác nhận Sent chỉ dựa vào HTTP 202.

Rollback SQL bị chặn nếu còn delivery ảnh/tệp cá nhân; cần giữ dữ liệu và xử lý hàng đợi theo quy trình trước khi hạ phiên bản. Không xóa delivery/tệp để ép rollback.

## Kiểm chứng

Các bài kiểm tra dùng SQL/SQLite dùng một lần, HTTP giả lập và trang Zalo giả lập, không gọi AI/Zalo thật. Có kiểm tra API/SQL, idempotency, quyền/cửa hàng/CSRF, lưu bytes qua restart, quota/retention, guard đổi hội thoại khi gắn tệp, giao diện Edge 1440 px và 390 px. Script SQL được thử áp dụng hai lần. Báo cáo và ảnh minh họa nằm trong gói artifacts cùng nâng cấp.

Bản source đã được nâng cấp; chưa triển khai production hoặc gửi ảnh/tệp tới khách thật. Các cảnh báo NuGet hiện có của System.Security.Cryptography.Pkcs được ghi trong báo cáo build, không thay đổi dependency trong tính năng này.
