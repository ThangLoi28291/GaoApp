# Nối callback ACB từ www:443 vào GaoApp:8443

## Kết quả kiểm tra

Người dùng xác nhận hai tên miền chạy trên cùng máy chủ IIS. Kiểm tra Internet ngày 12/10/2026 theo giờ Việt Nam:

| Yêu cầu | Kết quả |
|---|---|
| GET và POST JSON rỗng, không khóa, `https://www.gaomart.com.vn/Admin/api-callback` | 404 từ Microsoft-HTTPAPI/2.0; không có X-Acb-Diagnostic-Id |
| GET `https://chonthanh.gaomart.com.vn:8443/Admin/api-callback` | 405, Allow: POST, có X-Acb-Diagnostic-Id |
| POST JSON rỗng, không khóa, URL mới | 403 AUTH_REJECTED, DiagnosticId `80000a98-0001-f800-b63f-84710c7967bb` |

POST rỗng được người dùng cho phép riêng. Không gửi khóa, thông báo thanh toán giả, chuyển tiền, ghi khoản thu, chốt đơn hoặc in. Kết quả xác định URL cũ chưa vào ứng dụng GaoApp, còn endpoint mới đã hoạt động và từ chối yêu cầu không có khóa. Hai host cùng trỏ về 113.161.180.139; DNS cùng IP không làm hai cổng/website IIS tự nối với nhau.

ACB vẫn gửi tới URL đã đăng ký `https://www.gaomart.com.vn/Admin/api-callback`. Đổi URL hiển thị ở Cài đặt GaoApp không thay đăng ký đó. [Quy trình QR của ACB](https://developer.acb.com.vn/acb/open/vi/node/2342) phân biệt thông báo tức thời và danh sách cuối ngày T−1.

## Chọn cách nối trên máy chủ IIS

### Binding thực tế người dùng đã cung cấp lúc 01:14:57 ngày 12/10/2026 (Việt Nam)

- GaoMart / DefaultAppPool đang chạy: `http|*:80:`, `https|*:443:gaomart.com.vn`.
- GaoApp / GaoAppPool đang chạy: `http|*:80:chonthanh.gaomart.com.vn`, `https|*:8443:chonthanh.gaomart.com.vn`.
- Không website nào trong danh sách có binding `https|*:443:www.gaomart.com.vn`.

Với topology này, bước xử lý ưu tiên là thêm binding **HTTPS / All Unassigned / 443 / www.gaomart.com.vn / SNI** vào **GaoApp**, chọn chứng chỉ hợp lệ bao phủ www. Binding `gaomart.com.vn:443` tiếp tục thuộc GaoMart. Không cần ARR/proxy cho trường hợp này. Sau đó kiểm tra lựa chọn cửa hàng ở `/admin/acb/callback-routing`. Chỉ việc thêm binding và chạy script chẩn đoán không đòi hỏi deploy source mới; giao diện chẩn đoán nâng cấp cần deploy riêng.

GET URL cũ sau khi thêm binding có thể vẫn là 404 do middleware tenant chỉ ánh xạ callback **POST**. Nếu phản hồi đã có X-Acb-Diagnostic-Id thì request đã vào GaoApp. Nghiệm thu bằng POST rỗng không khóa đã được phép: 403 AUTH_REJECTED là đường nhận và store đã đi tới xác thực; 503 CALLBACK_STORE_NOT_SELECTED là đã vào GaoApp nhưng còn phải chọn cửa hàng. Đây chưa phải xác nhận callback thật của ACB.

Mở **IIS Manager → Sites → Bindings** trên máy chủ, xác định website đang phục vụ `chonthanh.gaomart.com.vn:8443` và website/binding `www.gaomart.com.vn:443`. Máy phát triển hiện tại không đọc được cấu hình IIS máy chủ này; chưa thay đổi binding hoặc triển khai source lên host.

### Nếu www:443 chưa phục vụ website cần giữ

Thêm HTTPS binding vào website **GaoApp**: host `www.gaomart.com.vn`, port `443`, bật SNI và chọn chứng chỉ hợp lệ có tên `www.gaomart.com.vn` (hoặc wildcard phù hợp). Kiểm tra không có binding trùng hoặc ứng dụng khác đang dùng cùng binding trước khi thêm. Không xóa/chuyển binding của website cũ còn đang sử dụng. Firewall/NAT phải chuyển cổng 443 vào máy IIS này. Giữ binding POS 8443 đang dùng.

### Nếu www:443 đang phục vụ website cũ

Giữ website cũ, dùng **IIS URL Rewrite + Application Request Routing (ARR)** để chuyển tiếp riêng đường dẫn callback. [Hướng dẫn Microsoft](https://learn.microsoft.com/en-us/iis/extensions/url-rewrite-module/reverse-proxy-with-url-rewrite-v2-and-application-request-routing) mô tả việc bật ARR proxy và tạo rule Rewrite.

Đây là **đoạn rule để ghép** vào website www, đặt trước các rule chuyển hướng/route khác; không thay toàn bộ web.config của website cũ:

```xml
<rule name="ACB callback to GaoApp" stopProcessing="true">
  <match url="^Admin/api-callback/?$" ignoreCase="true" />
  <conditions logicalGrouping="MatchAll">
    <add input="{HTTP_HOST}" pattern="^www\.gaomart\.com\.vn(:443)?$" />
    <add input="{REQUEST_METHOD}" pattern="^POST$" />
  </conditions>
  <serverVariables>
    <set name="HTTP_X_FORWARDED_HOST" value="www.gaomart.com.vn" />
    <set name="HTTP_X_FORWARDED_PROTO" value="https" />
  </serverVariables>
  <action type="Rewrite"
          url="https://chonthanh.gaomart.com.vn:8443/Admin/api-callback"
          appendQueryString="false" />
</rule>
```

Điều kiện cần trên host:

- Đã cài URL Rewrite/ARR, bật proxy; chứng chỉ HTTPS backend phải hợp lệ. Không tắt kiểm tra TLS.
- Cho phép hai server variables trên trong **URL Rewrite → View Server Variables**. Proxy ghi đè chúng bằng giá trị cố định; không chuyển nguyên giá trị do bên ngoài gửi lên.
- GaoApp bật `Proxy:EnableForwardedHeaders`, cấu hình `Proxy:KnownProxies` đúng địa chỉ IP mà kết nối ARR thực tế đến từ đó. Chỉ trust IP proxy cụ thể, không dùng toàn Internet. Nếu giữ Host gốc trực tiếp bằng cấu hình ARR đã có, không cần thêm cơ chế tin header mới. Không đổi cấu hình proxy toàn máy nếu làm ảnh hưởng website khác.
- Giữ body JSON, Content-Type, x-api-key, mã phản hồi và X-Acb-Diagnostic-Id; không dùng Redirect 301/302. Cho phép body tới 4 MiB. Nếu website cũ có lỗi tùy biến, giữ phản hồi ứng dụng cho riêng đường dẫn callback.
- Khi chỉ proxy POST, GET URL cũ có thể vẫn trả 404; phải kiểm tra đúng phương thức POST để nghiệm thu.

## Cấu hình và nghiệm thu trong GaoApp

### Cấu hình đã chuẩn bị sẵn cho publish từ máy lập trình

File nguồn cần chỉnh là `eng/deployment/web.appsettings.json`. `PublishSafety.targets` chỉ xuất file này thành `appsettings.json`; không tìm hoặc tạo `appsettings.Production.json` trên server. Mẫu hiện có:

```json
"AllowedHosts": "gaomart.com.vn;www.gaomart.com.vn;chonthanh.gaomart.com.vn;admin.gaomart.com.vn;*.gaomart.com.vn",
"AcbCallbackRouting": { "Hosts": [ "www.gaomart.com.vn" ] },
"ProductionBootstrap": { "Enabled": false },
"InputInvoiceLibrary": {
  "Enabled": true,
  "RootPath": "XML",
  "MaxMonthPartitions": 12,
  "MaxCandidates": 200
}
```

Publish Web bằng profile Folder hiện có. Trên server, giữ SQL connection, Storage:UploadRoot, Data Protection keys/certificate và các cấu hình riêng đang dùng. Nếu chúng đang nằm trong appsettings.json cũ, giữ bản cấu hình đó và ghép bốn mục trên vào khi triển khai; mẫu publish cố ý để trống secrets và dùng placeholder cho AppUrl/Tenant. Nếu chúng đã nằm trong environmentVariables của GaoAppPool, giữ nguyên các biến. Các biến AllowedHosts, AcbCallbackRouting__Hosts__0 và InputInvoiceLibrary__Enabled nếu đã có sẽ ghi đè file JSON, cần kiểm tra và sửa theo cấu hình mới.

`RootPath: "XML"` dùng `<Storage:UploadRoot>\XML`; không mang theo dữ liệu XML trong publish. Với Storage:CreateIfMissing=false, thư mục này phải tồn tại và tài khoản GaoAppPool có quyền đọc. Cấu hình callback không tự thêm binding IIS hoặc tự chọn cửa hàng. Sau triển khai, host www xuất hiện ở trang callback-routing; chọn đúng cửa hàng trên database server rồi nghiệm thu theo các bước dưới.

1. Deploy source mới theo quy trình đang có. Cần các migration ACB hiện hành; bản nâng cấp này không thêm migration.
2. `/admin/acb/settings`: lưu đúng x-api-key ACB đã đăng ký tại cửa hàng Chơn Thành. Khóa phải giải mã được trên host; giữ Data Protection keys khi chuyển máy. Kiểm tra token thành công chưa chứng minh nhận callback.
3. Tài khoản quản trị toàn hệ thống mở `/admin/acb/callback-routing`: chọn **đúng cửa hàng/StoreId của database trên host** cho `www.gaomart.com.vn`, lưu và tải lại. Không suy StoreId host từ database máy phát triển.
4. `/admin/acb/reconciliation`: mặc định hôm nay theo giờ Việt Nam, tất cả loại thông báo. Bật **Theo dõi mỗi 10 giây**; phần theo dõi chỉ đọc trạng thái GaoApp, không gọi API ngân hàng hay ghi thanh toán. Biên nhận gần nhất không bị ẩn bởi bộ lọc ngày hiệu lực. Dùng **Hôm qua · Cuối ngày** khi xem báo cáo T−1.
5. Kiểm tra POST rỗng không khóa sau khi được phép: URL cũ phải vào GaoApp và bị từ chối xác thực (403 AUTH_REJECTED); 503 CALLBACK_STORE_NOT_SELECTED nghĩa là đường truyền đã vào nhưng chưa chọn cửa hàng. 404 không có mã chẩn đoán nghĩa là còn phải kiểm tra IIS/proxy.
6. Người dùng thực hiện một giao dịch QR thật tại POS gốc; nhờ ACB gửi lại thông báo nếu cần. Cần thấy HTTP ACCEPTED + biên nhận, sau đó xác minh API ngân hàng thành công. POS gốc đang mở nhận cập nhật, chỉ ghi/chốt/in một lần. Callback lặp không được cộng tiền hoặc in lại. Tiền được nhận qua đếm giây/kiểm tra tay phải giữ nguồn xác nhận đó; không đổi thành Callback để làm đẹp đối soát.

Biên nhận lưu bền trong database; 20 dòng POST trên màn hình lấy từ tối đa 200 yêu cầu gần nhất của **tiến trình hiện tại**. Khi restart hoặc chạy nhiều instance, dùng nhật ký đầy đủ `App_Data/Logs/acb-callback/callback-YYYYMMDD.jsonl` để nối theo DiagnosticId/ReceiptId. Chưa thấy POST trên một instance không chứng minh ACB chưa gọi instance khác. Khóa/token/payload không hiển thị trong nhật ký này.

## Script đọc cấu hình

Trên máy chủ, mở **Windows PowerShell 5.1 64-bit → Run as administrator** tại thư mục source và chạy:

```powershell
.\scripts\Test-AcbCallbackHosting.ps1 -CheckIis
```

Script chỉ GET hai URL và đọc tên website/binding. Không POST, không sửa IIS/database, không restart. Có thể thêm `-OutputPath .\Logs\acb-hosting-check.json` nếu thư mục Logs đã tồn tại. Bản này chưa tự động sửa IIS vì chưa xác định binding www trên máy chủ thuộc website nào và có website cũ cần giữ hay không.
