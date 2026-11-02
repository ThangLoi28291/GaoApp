# Hướng dẫn chạy và nghiệm thu P3 GaoApp qua Zalo

P3 lấy bill và hóa đơn từ GaoApp, kiểm tra đúng khách rồi gửi qua hàng đợi ZaloMarketing hiện có. Pilot chỉ dành cho **0398946174**. Chỉ bật gửi sau khi cấu hình kênh và xác minh người nhận.

## Source và tiến trình cần chạy

| Thành phần | Source | Vai trò |
|---|---|---|
| GaoApp Web | `E:\GaoApp\GAOAPP-SA-20260819-01` | Chốt chứng từ, xác minh khách, lưu outbox và đẩy job sang ZaloMarketing |
| ZaloMarketing Web | `E:\repo\ZaloMarketing` | API Inbox, nhận bundle và tra cứu job |
| ZaloMarketing Worker | Cùng solution ZaloMarketing | Giữ kết nối SDK và thực hiện gửi từng phần |

Mở hai solution đúng đường dẫn trên, dừng phiên debug cũ rồi build và chạy lại. ZaloMarketing chạy **Web + Worker**, GaoApp chạy **Web**. AgentAuto không giữ kết nối Zalo trong luồng này. Chỉ một Worker giữ tài khoản Zalo đang thử.

Trang mới của GaoApp: **`/admin/customer-service/invoice-notifications`**. Người dùng tự thêm menu qua Quản lý menu nếu cần; P3 không seed menu.

GaoApp có tác vụ nền chạy mỗi 5 giây, lấy tối đa 5 bản ghi mỗi cửa hàng mỗi lượt. Tác vụ này xử lý outbox; kết nối Zalo vẫn do Worker của ZaloMarketing giữ. Không tải lại liên tục toàn bộ lịch sử chat.

## Database và cấu hình

Delta: `docs/deployment/invoice-notifications-p3.sql`, migration `20261027010000_AddInvoiceNotificationsP3`, baseline `20261025090000_AddAdminMenuPinSettings`. Sao lưu và xác minh trước khi áp dụng vào từng môi trường. Script thêm ba bảng P3 và bốn cột hash/kích thước tệp vào InvoiceHeads; không tạo đơn, khách hoặc menu.

Repository đang dùng snapshot EF cũ cùng các delta viết tay. Development bỏ qua MigrateAsync khi không có migration chờ; nếu có migration chờ, kiểm tra EF vẫn giữ nguyên. Dùng delta đã duyệt cho P3, không bỏ qua cảnh báo model changes toàn cục.

Tạo API credential riêng trên ZaloMarketing, ràng buộc đúng sender profile đang thử và đủ các scope:

`inbox.read`, `inbox.reply`, `jobs.read`, `jobs.cancel`, `jobs.stop`.

GaoApp dùng cấu hình `CustomerChannels` hiện có. Mỗi connection Zalo phải có `Provider=zalomarketing`, `StoreId` thật, `AccountId` là GUID sender profile, `BaseUrl` là origin ZaloMarketing, `AccessTokenFile` trỏ tới tệp khóa riêng ngoài webroot/public uploads và nằm trong CredentialDirectory. Production dùng HTTPS; Development cho phép HTTP loopback. Không đưa khóa vào source, URL hoặc chat.

Ví dụ phần P3, ghi vào cấu hình riêng của môi trường:

```json
{
  "InvoiceNotifications": {
    "Enabled": false,
    "PilotPhone": "0398946174",
    "LeaseSeconds": 120,
    "Artifacts": {
      "RootDirectory": "App_Data/InvoiceNotifications",
      "MaximumStoredBytes": 1073741824,
      "IncludeReceiptImages": true
    },
    "OfficialLinks": { "PublicBaseUrl": "" }
  }
}
```

Kho artifact phải private, không nằm trong webroot hoặc thư mục uploads công khai. Bill PNG dùng System.Drawing trên Windows; môi trường không hỗ trợ renderer cần đặt IncludeReceiptImages=false để chỉ gửi bill chữ. Link PDF là tùy chọn, không cần để thử PDF/XML/ZIP.

## Xác minh đúng người nhận

1. Từ Zalo **0398946174**, gửi một tin mới vào tài khoản Zalo đang kết nối. Chờ tin lưu vào Inbox của ZaloMarketing.
2. Đăng nhập GaoApp trong đúng cửa hàng. Mở trang mới, chọn khách có số điện thoại chuẩn hóa 0398946174 và connection thuộc cửa hàng đó.
3. Tìm hội thoại trực tiếp, bấm kiểm tra bằng chứng. Chỉ bằng chứng tin đến SDK đã lưu, cùng profile, peer và sequence mới hợp lệ. ID native giữ dạng chuỗi, không nhập hoặc đoán UID từ số điện thoại.
4. Đối chiếu khách bằng gặp trực tiếp, gọi xác minh hoặc chứng cứ tham chiếu theo nghiệp vụ; nhập tham chiếu và xác nhận. Tên hiển thị và số điện thoại trong lời chat không tự chứng minh chủ tài khoản.
5. Kiểm tra binding đang hiệu lực. Nếu sai, thu hồi rồi xác minh lại. Không gắn một hội thoại vào hai khách.

Cần quyền CSKH View/Verify, xem khách và xem đơn. Gửi thủ công cần thêm CSKH Reply. Hóa đơn điện tử dùng thêm quyền **System.Integration.Manage** hiện có; quyền được kiểm tra lại từ database. HostAdmin theo quyền hiện hành của hệ thống.

SDK đang có HistoryComplete=false vẫn cho nhân viên chọn và xác minh hội thoại trực tiếp qua API. Điều này không mở tự trả lời AI hoặc cho phép CHI TIẾT tự chạy từ lịch sử chưa đầy đủ.

## Các ca thử gửi

Sau khi đối chiếu đúng khách, bật InvoiceNotifications.Enabled trong cấu hình môi trường và chạy lại GaoApp nếu cần. Chỉ thử dữ liệu được phép gửi cho chính khách pilot.

| Ca thử | Thao tác | Kết quả cần đối chiếu |
|---|---|---|
| Bill đã chốt | Chọn đơn, xem trước, xác nhận đúng phiên bản rồi gửi | Chữ và PNG đủ dòng, giảm giá, thanh toán, đặt cọc, tiền thừa, công nợ và điểm tại thời điểm chốt |
| Bill tự động | Chốt một đơn thật của khách đã xác minh trong POS hoặc luồng giao hàng | Outbox commit cùng giao dịch; rollback không xuất hiện job gửi |
| PDF chính thức | Tải file chính thức từ nhà cung cấp bằng GaoApp rồi chọn gửi | PDF đúng bytes/hash/kích thước; bản cũ chưa có proof phải tải lại chính thức |
| ZIP/XML | Chọn gói chính thức hoặc XML gốc bên trong gói | ZIP không đổi bytes; XML chỉ tách khi có đúng một XML hợp lệ, giữ nguyên bytes và kiểm CRC |
| Điều chỉnh/thay thế | Chọn đúng hóa đơn và pháp nhân, xem trước | Hiển thị số hóa đơn gốc và loại thay đổi; bản nháp, hủy, bị thay thế hoặc sai ownership không gửi như bản hiện hành |
| Đơn cũ | Tìm theo mã đơn/hóa đơn rồi chọn đúng nguồn | Không tự lấy hóa đơn mới nhất để thay cho chứng từ đang yêu cầu |
| Gửi lặp | Nhấn lại cùng nguồn/phiên bản/loại | Trả về bản ghi đã có, không tạo đợt gửi trùng |

Khi file PDF hoặc ZIP chính thức mới được lưu và có proof, outbox tương ứng được ghi cùng SaveChanges. XML rời và link là lựa chọn gửi thủ công để tránh phát thêm nhiều bản cùng chứng từ. Sự kiện chỉ capture khi P3 bật, chỉ cho số pilot. Bản ghi thiếu binding giữ Blocked; sau khi xác minh, người vận hành phải chọn, xem trước và xác nhận lại.

Điểm chưa đọc được hiển thị chưa xác định, khác 0; giữ nguyên điểm lẻ. Snapshot không cập nhật thành điểm hiện tại khi đang retry. Bill chữ chia theo Unicode, bill ảnh chia trang; vượt giới hạn 10 phần/50 MiB sẽ yêu cầu xử lý, không cắt bớt dòng.

## Đọc trạng thái và thử lỗi

`Pending` chờ xử lý. `Accepted` chỉ là ZaloMarketing nhận job. `Sent` cần kết quả từng phần có receipt; không đồng nghĩa khách đã đọc. Mở chi tiết outbox và job/part ledger của ZaloMarketing để đối soát điện thoại.

- Đổi nguồn hoặc binding sau xem trước: phải báo thay đổi, yêu cầu xem lại; không tự chấp nhận phiên bản mới.
- Thu hồi binding hoặc tắt P3 trước gửi: chặn gửi mới. Job đã nhận được tra cứu và yêu cầu cancel/stop; phần đã qua ranh giới gửi có thể đã tới khách.
- Dừng GaoApp rồi mở lại: tiếp tục cùng RequestId/Idempotency-Key và nội dung đã chuẩn bị. Không tạo yêu cầu mới để vượt kết quả chưa rõ.
- Mất phản hồi HTTP sau POST: tra job bằng RequestId cố định. Không gửi lại cả bundle; trạng thái không xác định hoặc từng phần lỗi cần đối soát.
- Tệp thiếu, bị sửa hash, nhiều XML, sai cửa hàng, hóa đơn đầu vào hoặc ownership chưa chứng minh: chặn/review có nguyên nhân.

Khi khôi phục backup, giữ nhất quán outbox, part ledger ZaloMarketing, kho artifact và SDK journal. Không xóa ledger để gửi lại. Không chạy downgrade khi còn bằng chứng binding/outbox/audit.

## Link xem PDF tùy chọn

Cấu hình OfficialLinks.PublicBaseUrl bằng domain HTTPS thực của GaoApp. Không dùng IP, localhost, query hay fragment. Giữ Data Protection key ring bền vững và đồng nhất giữa các instance; không đổi application key/purpose khi triển khai.

Trước khi bật, cấu hình **IIS/reverse proxy/APM bỏ hoặc che query string** cho `/invoice-document` và `/invoice-document/download`. Token nằm trong query; bộ lọc log ứng dụng đã che token, nhưng log máy chủ là cấu hình riêng cần xác minh.

Chọn “Xem hóa đơn qua liên kết” từ nguồn có PDF chính thức. Tin gửi chỉ gồm chữ và link. Link có hạn 72 giờ, chỉ mở đúng một PDF và kiểm tra lại nguồn, khách, binding, hash trước/sau đọc. Thu hồi binding, đổi nguồn, đổi public audience hoặc tắt P3 làm link hết hiệu lực. Ai có link hợp lệ có thể xem PDF trong thời hạn; không chuyển tiếp link ra ngoài người nhận.

## Phạm vi nghiệm thu

P3 chuẩn bị nguồn và luồng gửi chứng từ. CHI TIẾT tự động theo hội thoại vẫn bị chặn khi lịch sử SDK chưa Ready; nhân viên dùng lựa chọn nguồn có phiên bản ở trang mới. Chưa bật AI tư vấn, tra cứu điểm/voucher riêng hoặc quảng cáo hàng loạt của các mốc sau.

Kiểm thử tự động dùng dữ liệu giả và database SQL sinh riêng. Chỉ đánh dấu nghiệm thu thực tế khi đã gửi tới điện thoại pilot, đối chiếu chứng từ và lưu kết quả từng ca.
