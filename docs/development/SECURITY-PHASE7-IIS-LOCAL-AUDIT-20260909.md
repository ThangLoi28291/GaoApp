# Đợt 7 — Bảo mật local trước khi chạy IIS

Ngày 09/09/2026. Phạm vi: working tree GaoApp hiện tại, gồm các thay đổi chưa commit. Chủ ứng dụng xác nhận host là IIS trên máy chủ Windows riêng, có domain và IP tĩnh. Chưa triển khai, chưa sửa IIS/router/firewall hoặc chạy tải trên host. Không backup/reset/migrate database ứng dụng đang dùng; các kiểm thử tạo database và tiến trình riêng, tự dọn khi kết thúc.

## Các vấn đề đã sửa

| Vấn đề | Xử lý | Bằng chứng/phạm vi |
|---|---|---|
| Nhiều action chỉ yêu cầu đăng nhập, thiếu quyền nghiệp vụ | Bổ sung quyền cụ thể cho danh mục, barcode, quảng bá, khuyến mãi, voucher, kiểm kê, truy vấn kho và các trang POS liên quan | Kiểm thử trước sửa: tài khoản không có quyền gọi Brand/Create nhận 302. Kiểm thử sau sửa xác minh 403 và không có bản ghi được tạo |
| API ghi tồn kho trực tiếp nhận loại/tham chiếu/số lượng từ client | Đóng `/admin/api/inventory/transactions`; tài khoản có quyền nhận 410 `INVENTORY_DOCUMENT_REQUIRED`, dùng luồng chứng từ/POS hiện có | Request trước sửa đi tới validation (400); không khẳng định đã ghi được tồn. Sau sửa kiểm tra DB giữ nguyên số lượng |
| Quản lý nhân viên có thể nhìn/gán tài khoản toàn hệ thống rồi ảnh hưởng cửa hàng khác | Lookup người dùng của quản trị cửa hàng chỉ trả thành viên cửa hàng; gán tài khoản đã tồn tại yêu cầu Host Admin còn hoạt động trong DB; chặn quản trị cửa hàng reset mật khẩu tài khoản có membership ở cửa hàng khác, kể cả membership đã khóa/xóa | Test HTTP + SQL với hai cửa hàng: lookup không lộ người dùng ngoài cửa hàng, không tạo mapping trái phép, hash của tài khoản dùng chung giữ nguyên; reset nhân viên riêng cửa hàng vẫn hoạt động |
| Mật khẩu nhân viên mới/reset chỉ cần 6 ký tự; reset thiếu giới hạn trên | Yêu cầu 12–128 ký tự ở DTO, service và giao diện; không tự đổi mật khẩu đang có | Test từ chối mật khẩu 6 ký tự và cho phép đổi mật khẩu hợp lệ; tài khoản thật cần được cấp mật khẩu mới khi triển khai |
| Chống giả mạo request chưa áp dụng mặc định mọi MVC write | Đăng ký `AutoValidateAntiforgeryToken` toàn cục; giữ ngoại lệ webhook ACB đã có xác thực API key riêng | API kho cũ chưa có attribute CSRF riêng nay trả 400 khi thiếu token dù tài khoản có quyền; JS chỉ gửi token cho write cùng origin |
| Base URL Viettel do cửa hàng cấu hình có thể hướng request tới địa chỉ khác | Chỉ cho phép HTTPS origin Viettel được liệt kê; 7 HTTP client cùng dùng guard, không theo redirect; origin bổ sung chỉ từ cấu hình host | Test chặn loopback, metadata IP, HTTP, userinfo, domain giả và sai port trước transport; kiểm tra pipeline DI của cả 7 client |
| Thông báo có thể được toastr hiểu là HTML | Bật escape HTML mặc định và bắt buộc trong GaoAppNotify, cả message và title | Chạy module JS thật; tùy chọn do caller truyền không thể tắt escape ở wrapper. Đây là chốt cho toast, không chứng nhận mọi nơi render HTML đã hết XSS |
| Thiếu một số header và chống lưu cache trang đăng nhập/nghiệp vụ | `nosniff`, SAMEORIGIN, Referrer-Policy, CSP giới hạn frame/object/base, no-store cho response đã xác thực và account; tắt Server header Kestrel | Kiểm thử HTTP thật và bản Production HTTPS. CSP chưa có script-src vì ứng dụng còn script inline |
| Chưa có web.config IIS được kiểm tra trong gói publish | ANCM V2 in-process; tắt directory listing/stdout logging; chặn key/database/thư mục nhạy cảm; trần request 64 MiB | Bộ kiểm tra release đọc XML và yêu cầu các mục an toàn. Hai action quảng bá cho phép multipart 64 MiB nhưng validator file vẫn 50 MiB; chưa xác minh IIS thực tế có cho phép các section cấu hình này |

## Phân quyền và tương thích chức năng

Bản kiểm kê reflection bao phủ **562 action Admin**, trong đó 61 action đã có kiểm tra quyền theo tài nguyên/trạng thái ngay trong controller (mua hàng, nhận hàng và hóa đơn liên kết). Danh sách ngoại lệ ghi tên từng action; thêm action chỉ có `[Authorize]` mà không có quyền/ngoại lệ rõ sẽ làm test thất bại. Kiểm kê metadata không chứng minh mọi nhánh nghiệp vụ của 562 action đã được khai thác thử. Các kiểm thử HTTP và SQL xác minh thêm các đường trọng yếu.

Thêm 9 mã quyền có thể seed: `catalog.tax.view/create/update/delete`, `catalog.promotion.view/manage`, `catalog.displaypromotion.view/manage`, `catalog.customer.managerewards`. Lần chạy Migrator khi chuẩn bị môi trường mới sẽ đồng bộ catalog quyền; không chạy seed vào database đang dùng trong đợt này. Phải kiểm tra/cấp quyền mới cho các vai trò hiện có; không tự suy ra mọi nhân viên được quyền chỉnh voucher hay khuyến mãi.

`RequireAnyPermission` dùng cho lookup/phần giao diện dùng chung: cho phép một trong các quyền được ghi rõ, vẫn kiểm tra quyền hiện tại trong DB và store. Các attribute riêng biệt tiếp tục yêu cầu đồng thời mọi quyền. Hai API upsert gộp tạo/sửa biến thể và barcode yêu cầu cả quyền tạo và sửa vì cùng request có thể làm cả hai; vai trò chuyên chỉ tạo hoặc chỉ sửa sẽ bị chặn ở API gộp này. POS vẫn xem được nội dung quảng bá qua quyền xem đơn và dùng voucher qua quyền tạo đơn.

Gán nhân viên đã tồn tại nay là thao tác của quản trị hệ thống; giao diện cửa hàng thông thường dùng **Tạo nhân viên mới**. Tài khoản dùng chung nhiều cửa hàng cần quy trình quản trị hệ thống để đổi mật khẩu, thay vì để một cửa hàng chiếm quyền ở cửa hàng khác.

Mặc định Viettel cho phép `https://api-vinvoice.viettel.vn`, `https://api-sinvoice.viettel.vn`, `https://demo-sinvoice.viettel.vn:8443`. Cấu hình tùy chọn `Security:Viettel:AdditionalAllowedOrigins` chỉ chứa origin HTTPS chính xác đã được chủ host xác minh; không thêm địa chỉ nội bộ hoặc wildcard. URL provider khác danh sách sẽ bị chặn cho tới khi cấu hình host đúng. Nguồn đối chiếu: [Viettel về endpoint tích hợp](https://viettelsolution.com.vn/san-pham/danh-sach-loi-thuong-gap-khi-tich-hop-api-hoa-don-dien-tu.80.html), [chính sách VInvoice](https://vinvoice.viettel.vn/policy-sinvoice.html).

## Kiểm chứng

Gói cuối: `publish/releases/20260909-094039-770f9fe5`, gồm Web/Migrator và manifest SHA-256; 615 file payload, 130,9 MiB. Đã đối chiếu DLL Web/Application/Infrastructure của build được kiểm thử với gói release: hash trùng. Đây là gói để cấu hình host sau này, chưa được copy/chạy trên IIS thật. Không dùng lại gói đợt 6 thay cho bản vá này.

| Kiểm tra | Kết quả | Bằng chứng |
|---|---|---|
| Toàn bộ suite, trừ 2 test cần bản publish | 2.090 đạt / 2.094, 4 test thất bại ban đầu, 0 skipped; 13 phút 44 giây | `phase7-full-regression.trx` |
| Chạy lại nhóm bị ảnh hưởng trên build cuối: phân quyền, tài khoản, SSRF, giao diện nhân viên và migration | 31/31 đạt, 0 skipped | `phase7-final-rerun.trx` |
| Production HTTPS và các test thanh toán | 7/7 đạt, gồm 2 test bản publish và 5 test thanh toán | `phase7-published-and-payment.trx` |
| Chủ động giữ khóa SQL để buộc trả 409; nhả khóa rồi gửi lại cùng request ID | 1/1 đạt; DB chỉ có một khoản thu, tổng tiền đúng | `phase7-busy-payment.trx` |
| JavaScript | 41/41 đạt | `Logs/security-phase7-js.log` |
| Công cụ triển khai, gồm cấu hình IIS cố ý cho tải PFX | 11/11 đạt | `Logs/security-phase7-tool-checks.log` |
| Build cuối / kiểm tra diff phần thay đổi / kiểm tra lại manifest | 0 lỗi build, 10 warning test đã có; diff và release đạt | `Logs/security-phase7-final-build.log`, `Logs/security-phase7-changed-diff-check.log`, `Logs/security-phase7-release-verify.log` |

Ba lỗi đầu thuộc test giữ danh sách 14 migration cũ trong khi source đã có 21 migration từ các đợt trước. Đã cập nhật đúng danh sách/kích thước, vẫn giữ kiểm tra schema và khả năng migrate lại không đổi dữ liệu. Lỗi thứ tư do test yêu cầu cả 50 request thu tiền trùng phải trả 200 ngay: SQL lock có timeout 5 giây nên có thể trả 409 yêu cầu gửi lại. Giữ nguyên chốt an toàn của ứng dụng; sửa test để chỉ chấp nhận đúng thông báo khóa bận, gửi lại **cùng ID và payload** rồi bắt buộc nhận 200 và kiểm tra không thu trùng. Test giữ khóa chủ động xác minh nhánh 409 này một cách độc lập. Đây không phải phép đo năng lực phục vụ 50 client nghiệp vụ trên host.

`verification-summary.json` tổng hợp **2.097 test .NET phân biệt có kết quả mới nhất đạt**, không còn failure chưa được xử lý trong các lượt trên. Con số là tổng hợp lượt toàn bộ và các lượt chạy lại có mục tiêu; không phải một lượt full-suite duy nhất đạt sạch trên build cuối. Các file TRX nằm trong `TestResults/security-phase7`; không sửa/xóa bằng chứng thất bại ban đầu.

- `phase7-before.trx`: tái hiện thiếu quyền Brand/Create (302) và raw inventory đi tới validation (400).
- `phase7-after-2.trx`: 20/20 kiểm tra đầu tiên đạt; gồm 23 URL với tài khoản không có quyền, quyền tạo/sửa độc lập, AND/OR, đổi tenant, thu hồi quyền không đăng nhập lại và CSRF.
- `admin-permission-inventory.json`: danh sách 562 action và quyền/ngoại lệ.
- `security-phase7-js.log`: 41/41 JavaScript test đạt, bao gồm 2 bài mới cho toast và CSRF; CI đã thêm bước chạy các test JS này.
- `security-phase7-nuget-audit.json`: `dotnet list GaoApp.sln package --vulnerable --include-transitive --format json` không trả package có advisory lỗ hổng cho cả 6 project tại thời điểm chạy. Kết quả này không phải bằng chứng mã ứng dụng hoặc tất cả thư viện JavaScript đều an toàn.

Đã xem các bản thư viện giao diện có sẵn: jQuery 3.7.1/Bootstrap 5.3.8 ở bundle vendor, jQuery 3.6.0/Bootstrap 5.1.0 ở layout public cũ, jquery-validation 1.19.5 và SweetAlert2 11.26.4. Không nâng đồng loạt thư viện theme khi chưa có finding xác định. Advisory [GHSA-ffmh-x56j-9rc3](https://github.com/jquery-validation/jquery-validation/security/advisories/GHSA-ffmh-x56j-9rc3) đã được vá ở jquery-validation 1.19.5; không coi đó là lỗi còn tồn tại trong bản đang có. Chưa có SBOM/advisory scan đầy đủ cho toàn bộ JavaScript vendored.

## Trước khi mở ra Internet

Các bản vá/code test đạt không đủ để kết luận IIS đã an toàn. Cần xác minh trên máy chủ: phiên bản/bản vá, HTTPS và binding domain, app pool identity/ACL, SQL least privilege và TLS, firewall/NAT, private storage/key, cấu hình Production và secret, chạy lại kiểm thử quyền dưới app pool thật. Xem [bảng kiểm IIS trước công bố](IIS-SECURITY-PREPUBLICATION-20260909.md).

Chưa chạy pentest độc lập/DAST toàn ứng dụng, UAT mọi màn hình, kiểm tra TLS/cổng từ mạng ngoài hoặc tải 10–20–50 client trên host. Không có cam kết “không thể bị hack”. Ưu tiên tiếp theo là kiểm chứng các điều kiện bảo mật IIS trong mạng nội bộ trước khi công bố, sau đó mới đánh giá tải theo yêu cầu chủ ứng dụng.
