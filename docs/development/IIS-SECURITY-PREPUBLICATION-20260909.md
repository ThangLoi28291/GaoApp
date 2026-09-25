# Kiểm tra bảo mật trước khi mở GaoApp trên IIS

Máy chủ dự kiến: Windows/IIS của chủ ứng dụng, domain và IP tĩnh. Chưa truy cập hoặc thay đổi máy chủ trong đợt kiểm tra này. Chưa mở site ra Internet. Kiểm thử tải 10–20–50 client trên host được hoãn theo yêu cầu.

## Điều kiện cần xác minh trên máy chủ

Có thể kiểm tra cấu hình hạ tầng trước khi copy ứng dụng. Bước chạy thử ứng dụng sau đó dùng site riêng chỉ cho phép truy cập nội bộ/VPN; chưa công bố domain cho người dùng. Các kiểm thử local không xác minh được cấu hình IIS, router hoặc firewall thực tế.

| Hạng mục | Điều kiện đạt | Bằng chứng cần lấy |
|---|---|---|
| Windows, IIS, .NET | Hệ điều hành còn được hỗ trợ, cập nhật bản vá; cài ASP.NET Core Hosting Bundle tương thích .NET 8 | Phiên bản Windows/IIS, `dotnet --list-runtimes`, có AspNetCoreModuleV2 |
| Site và domain | Site riêng, binding tên miền đúng, certificate HTTPS có chuỗi tin cậy và đúng tất cả hostname cửa hàng | Danh sách binding và kiểm tra TLS từ máy khác; không truy cập nghiệp vụ qua HTTP/IP trực tiếp |
| Tài khoản chạy | App pool riêng, ApplicationPoolIdentity hoặc tài khoản dịch vụ ít quyền; không LocalSystem/Administrator | Tên app pool, identity; xác minh quyền thật của tiến trình |
| Số tiến trình | Một Web instance và `Maximum Worker Processes = 1` cho phiên bản hiện tại | Cấu hình app pool; SignalR, rate limit và một số cache đang dùng bộ nhớ tiến trình |
| Quyền thư mục | Thư mục release chỉ Read/Execute cho tài khoản Web; upload/key/log ở ngoài release, chỉ cấp quyền cần thiết | ACL ở thư mục release và storage, kiểm tra inheritance; không cấp Everyone/Users Full Control |
| Bí mật | Connection string, mật khẩu provider và PFX/key không đặt trong wwwroot hoặc gửi trong báo cáo | Kiểm tra nơi lưu và ACL; web.config publish không chứa secret; cấu hình riêng cho site/app pool |
| SQL | SQL Server dịch vụ, database riêng; tài khoản Web không sysadmin/db_owner/DDL; tài khoản migration riêng; kết nối mã hóa với certificate tin cậy | Kiểm tra SQL roles và kết nối dưới đúng identity Web; không dùng LocalDB trên host |
| Mạng | Router/firewall chỉ công bố cổng Web cần thiết; SQL/SMB/WinRM/RDP không mở công khai; quản trị qua VPN hoặc giới hạn IP | Kiểm tra inbound/NAT và quét cổng từ mạng ngoài do chủ máy cho phép |
| IIS filtering | web.config được IIS chấp nhận; Directory Browsing tắt; file key/database và thư mục nhạy cảm bị chặn | Không lỗi 500.19; request tới đường dẫn nhạy cảm không trả nội dung |
| Proxy | Chỉ bật forwarded headers khi có proxy thật và danh sách proxy tin cậy chính xác | Mô hình IIS trực tiếp hay qua ARR/CDN; kiểm tra không tin header giả từ Internet |
| Khởi động/lỗi | Production, demo seed tắt; thiếu SQL/key/certificate phải dừng an toàn; response không có stack trace/connection string | Chạy preflight và smoke trên site nội bộ với tài khoản ít quyền |
| Quyền người dùng | Seed 9 quyền mới bằng Migrator; cấp đúng quyền cho từng vai trò; dùng mật khẩu mạnh mới cho tài khoản thật | UAT với chủ cửa hàng, thu ngân, kho; tài khoản thiếu quyền nhận 403 |
| Vận hành | Cookie còn hợp lệ sau recycle nhờ key dùng chung đúng cấu hình; background callback chạy ổn; log có giới hạn lưu giữ | Thử recycle trong site nội bộ, kiểm tra callback/SignalR và quyền đọc log |

IIS in-process vẫn áp dụng giới hạn request của IIS trước giới hạn ASP.NET Core. Release đặt mức trần 64 MiB; chỉ hai endpoint media quảng bá được nâng giới hạn ứng dụng lên 64 MiB, validator vẫn giới hạn file 50 MiB. Các endpoint khác giữ mức nhỏ hơn đang có. Xem [hướng dẫn IIS của Microsoft](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-8.0) và [giới hạn request in-process](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/in-process-hosting?view=aspnetcore-8.0).

## Quy trình sau khi kiểm tra local đạt

1. Kiểm tra các hàng hạ tầng trên máy chủ; giữ site chưa công khai. Ghi lại phiên bản Windows/IIS, tên domain/site/app pool, vị trí SQL và storage; không đưa mật khẩu vào báo cáo.
2. Dùng gói release mới được ghi trong báo cáo đợt 7. Chạy `scripts/test-release-package.ps1`, sau đó `scripts/test-host-preflight.ps1` với cấu hình host được nạp trong phiên quản trị phù hợp. Preflight có tạo/xóa một file thăm dò riêng để xác minh quyền ghi, không kết nối SQL và không thay ACL; nó không thay cho kiểm tra dưới identity app pool.
3. Sau khi được phép chạy trên site nội bộ, dùng database khởi tạo riêng và Migrator đúng release; kiểm tra `scripts/test-staging-endpoint.ps1` từ máy khác. Chạy các thao tác đăng nhập, phân quyền, upload, hóa đơn, thanh toán thử và recycle. Không sử dụng thông tin ngân hàng/provider production để kiểm thử tùy tiện.
4. Chỉ xem xét mở truy cập công khai khi các mục bảo mật trên đã có bằng chứng đạt. Đo tải 10–20–50 client làm ở bước riêng theo cấu hình máy chủ thực tế.

Dữ liệu test hiện có không cần backup và không được chuyển sang production theo quyết định của chủ ứng dụng. Khi bắt đầu có dữ liệu kinh doanh thật, cần chốt riêng chính sách lưu giữ/khôi phục vận hành; quyết định bỏ dữ liệu test không tự áp dụng cho dữ liệu thật.
