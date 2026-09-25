# Giữ URL callback cũ, chọn cửa hàng bằng StoreId

URL đã đăng ký với ACB giữ nguyên: `https://www.gaomart.com.vn/Admin/api-callback`. Ứng dụng chọn cửa hàng nhận bằng `AcbCallbackRoutes.TargetStoreId`, không suy từ subdomain của cửa hàng, query string, nội dung callback hay header do người gọi tự truyền.

## Chọn cửa hàng

1. Build/chạy bản mới và áp dụng migration `20260909062834_AddAcbCallbackStoreRouting` theo quy trình triển khai hiện có. Migration thêm `AcbCallbackRoutes` và `AcbCallbackRouteChanges`; không chọn sẵn một StoreId và không sửa thanh toán. Script `acb-callback-store-routing-upgrade.sql` chỉ chứa migration này; các migration khác của dự án vẫn theo quy trình chung.
2. Đăng nhập tài khoản quản trị toàn hệ thống đang hoạt động (`User.IsHostAdmin`), có quyền quản lý tích hợp. Vào `/admin/acb/callback-routing`. Trong Cài đặt thanh toán ACB, tài khoản đủ quyền cũng thấy nút **Chọn cửa hàng nhận callback từ URL cũ**.
3. Tại URL cũ, chọn đúng **Tên cửa hàng — StoreId — subdomain hiện tại**, rồi bấm **Lưu cửa hàng nhận callback**. Cửa hàng phải đang hoạt động và đã lưu `x-api-key` tại Cài đặt thanh toán ACB của chính cửa hàng đó.
4. Tải lại trang để kiểm tra lựa chọn. Khi đổi subdomain, giữ nguyên bản ghi cửa hàng/StoreId. Không cần đổi URL đăng ký với ACB.

Quản trị riêng một cửa hàng không được xem danh sách hoặc sửa ánh xạ toàn hệ thống. POST cấu hình yêu cầu đăng nhập, quyền tích hợp, cờ quản trị toàn hệ thống trong database và antiforgery token. Phiên cũ không được ghi đè lựa chọn vừa đổi; mỗi thay đổi lưu StoreId trước/sau, UserId và thời điểm. Cấu hình và lịch sử được lưu trong cùng lần SaveChanges.

Chọn **Chưa chọn / Tạm ngưng** rồi lưu để dừng nhận mới tại URL này. Callback đã lưu trong inbox vẫn xử lý theo StoreId ban đầu. Nếu muốn chuyển URL sang cửa hàng khác, cửa hàng nhận trước đó phải xử lý xong QR và callback còn chờ; việc tạm ngưng rồi chọn cửa hàng khác cũng kiểm tra điều kiện này.

## Tên miền được phép nhận tại URL cũ

Danh sách tên miền do cấu hình máy chủ quản lý; StoreId do màn hình bên trên quản lý. Source có sẵn:

```json
"AcbCallbackRouting": {
  "Hosts": [ "www.gaomart.com.vn" ]
}
```

Chỉ thêm `gaomart.com.vn` nếu đó cũng là tên miền callback cần nhận. Có và không có `www` là hai host riêng, mỗi host có lựa chọn StoreId riêng. Không dùng wildcard, URL đầy đủ hoặc cổng trong danh sách. `Hosts: []` tắt cơ chế ánh xạ URL cũ; khi đó hệ thống dùng lại quy tắc tenant thông thường. Để tạm ngưng có ghi nhận lịch sử, nên dùng lựa chọn Tạm ngưng trong giao diện.

Ánh xạ chỉ áp dụng cho POST `/Admin/api-callback` và alias callback `/api/acb/webhook`. Trang bán hàng, trang quản trị, GET và đường dẫn gần giống vẫn xử lý tenant bình thường. Khóa được kiểm tra theo cửa hàng đích; chọn cửa hàng không thay thế xác thực callback.

## Khi đưa lên host

ACB vẫn gửi đến URL cũ. Yêu cầu đó phải tới ứng dụng mới thì phần ánh xạ mới hoạt động:

- Nếu tên miền `www` và POS chạy cùng ứng dụng: cấu hình HTTPS/binding cho `www.gaomart.com.vn`, cho phép host này trong `AllowedHosts` và chuyển POST tới ứng dụng.
- Nếu `www` vẫn phục vụ website cũ: cấu hình reverse proxy riêng đường dẫn `/Admin/api-callback` trên website cũ tới ứng dụng mới. Giữ nguyên POST, JSON, Content-Type, `x-api-key` và mã HTTP phản hồi. Cần giữ host gốc `www.gaomart.com.vn` khi ứng dụng xử lý, qua Host hoặc X-Forwarded-Host từ proxy đã khai báo tin cậy; không để host nội bộ hoặc subdomain POS thay thế host gốc.
- Không dùng chuyển hướng 301/302 cho callback. Cho phép body 4 MiB, HTTPS đúng scheme và ứng dụng/worker hoạt động liên tục. Giữ Data Protection keys khi chuyển máy để giải mã khóa ACB đã lưu.
- Nếu đưa database sang host, phải giữ đúng StoreId, các bảng cấu hình và lịch sử. Chọn lại bằng giao diện nếu database đích có bộ StoreId khác.

Code không tự sửa DNS/IIS hoặc website cũ. Chưa cấu hình đường chuyển tiếp trên host và chưa thử callback thật qua Internet trong lần triển khai này.

## Đọc log

HTTP callback giữ `X-Acb-Diagnostic-Id`, kể cả khi không chọn được cửa hàng. Log trong `App_Data/Logs/acb-callback` thêm `RequestHost`, `RoutedStoreId`, `StoreId`, `ReceiptId` và kết quả:

| Kết quả | Ý nghĩa |
|---|---|
| CALLBACK_STORE_NOT_SELECTED | Host đã khai báo nhưng chưa chọn cửa hàng hoặc đang tạm ngưng; HTTP 503, không ghi inbox. |
| CALLBACK_STORE_NOT_FOUND | StoreId đã chọn không tồn tại, bị xóa hoặc ngừng hoạt động; HTTP 503, không tự chọn cửa hàng khác. |
| STORE_ACB_NOT_CONFIGURED | Cửa hàng đích chưa có cấu hình ACB. |
| AUTH_REJECTED | x-api-key không đúng khóa cửa hàng đích; HTTP 403. |
| ACCEPTED | Đã lưu biên nhận hợp lệ vào đúng cửa hàng; worker sẽ xác minh ngân hàng. |

Không ghi giá trị khóa hoặc nội dung giao dịch vào log chữ. Nếu không có log callback, kiểm tra URL cũ, binding, proxy và log website/IIS trước.

## Kiểm thử

Bộ tự động hiện tại đạt **240 ca .NET + 32 JavaScript**. Có HTTP Kestrel giả lập nhận URL cũ, đúng/sai khóa, gửi lại và chốt một lần, store không tồn tại/ngừng hoạt động, chưa chọn/tạm ngưng, chống đổi store bằng query/header, đổi subdomain nhưng giữ StoreId, quyền quản trị, lịch sử, phiên cấu hình cũ, tên miền được phép và proxy tin cậy. Razor của màn chọn cửa hàng được render để kiểm tra StoreId, lựa chọn hiện tại, antiforgery và mã hóa tên cửa hàng.

Không gửi callback giả vào database bán hàng và không thực hiện chuyển tiền thật. Chưa áp migration lên database đang chạy. Bài thao tác bổ sung:

| Mã | Thao tác | Kết quả |
|---|---|---|
| R01 | Chọn cửa hàng, lưu, tải lại trang | Giữ đúng StoreId, có dòng lịch sử thay đổi. |
| R02 | Đổi subdomain cửa hàng trên môi trường thử, giữ Id | Lựa chọn và nơi nhận callback vẫn là cửa hàng đó. |
| R03 | Chọn Tạm ngưng rồi lưu | Không nhận callback mới qua URL cũ; báo CALLBACK_STORE_NOT_SELECTED, inbox cũ vẫn xử lý. |
| R04 | Dùng tài khoản quản trị cửa hàng thông thường mở trang chọn | Bị từ chối, không thay đổi cấu hình toàn hệ thống. |
| R05 | Trên host, ACB gửi thông báo vào URL cũ | Log có RequestHost=www.gaomart.com.vn và đúng RoutedStoreId; xử lý bằng khóa cửa hàng đích. |
| R06 | ACB gửi lại thông báo đã xử lý | Không thêm tiền, không chốt/in lại; nguồn xác nhận đầu tiên giữ nguyên. |

R01–R06 là phiếu nghiệm thu để người dùng thực hiện; callback Internet cần thử sau khi hosting chuyển tiếp đúng.
