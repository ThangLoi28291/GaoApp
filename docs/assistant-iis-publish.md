# Khôi phục trợ lý sau khi publish GaoApp

Kết nối ChatbotLocal trước đây được ghi vào `environmentVariables` của `web.config`.
Publish GaoApp thay file này bằng bản trong source, nên `Assistant__Enabled` và các
giá trị kết nối biến mất. Cài đặt bật trợ lý theo cửa hàng không thay thế cấu hình
kết nối trên máy chủ.

Chạy script một lần trên máy chủ IIS để đặt cấu hình trợ lý vào **GaoAppPool**,
ngoài thư mục publish. Không cần publish lại hoặc cài lại ChatbotLocal.

1. Chép `scripts/assistant/Restore-GaoAppAssistantIis.ps1` tới
   `C:\ProgramData\ChatbotLocal\config\Restore-GaoAppAssistantIis.ps1`.
2. Lúc quầy tạm nghỉ thao tác, mở **Windows PowerShell → Run as administrator**:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\ProgramData\ChatbotLocal\config\Restore-GaoAppAssistantIis.ps1"
   ```

3. Kết quả cần là `GAOAPP_ASSISTANT_CONFIGURED`. Script khởi động lại riêng
   `GaoAppPool`. Tải lại GaoApp bằng **Ctrl+F5**, kiểm tra
   `/admin/assistant/settings` không còn thông báo kết nối AI đang tắt, rồi mở chat.

Script kiểm tra site/pool/đường dẫn, API ChatbotLocal đã bật và key ID đã có trước
khi ghi. Issuer lấy từ `C:\ProgramData\ChatbotLocal\config\runtime.json`, giữ đúng
giá trị hai ứng dụng đang dùng. Issuer có cổng cũ `8433` vẫn là định danh xác thực;
không đổi nó thành `8443` chỉ vì địa chỉ website đổi cổng.

Các địa chỉ được khôi phục:

- ChatbotLocal: `https://chat.gaomart.com.vn:8444`.
- Khóa ký hiện có: `C:\ProgramData\GaoApp\assistant\secrets\gaoapp-assistant-private.pem`.
- Cấu hình trợ lý theo cửa hàng: `C:\ProgramData\GaoApp\assistant\data`.

Script không đọc nội dung khóa riêng, sửa Gemini/ACB, thay `AllowedHosts`, đổi
binding, tạo menu hoặc sửa dữ liệu nghiệp vụ. Bản sao chỉ chứa cấu hình kết nối
trợ lý trước khi sửa, nằm trong thư mục backup đã có. Nếu ghi biến IIS thất bại,
script khôi phục biến trước đó và không khởi động lại pool.

Có thể chạy với `-WhatIf` để kiểm tra trước; lần chạy lại khi cấu hình đã khớp
trả `GAOAPP_ASSISTANT_ALREADY_CONFIGURED` và không khởi động lại pool.

Khi publish tiếp, giữ **GaoAppPool hiện có**. Cấu hình trong pool tồn tại độc lập
với `web.config` và `appsettings.json` trong gói. Không đưa khóa hoặc cấu hình
Development vào bản publish. Nếu `web.config` còn biến trợ lý cũ khác giá trị,
script dừng và chỉ rõ tên biến cần xem lại.

Kiểm tra cục bộ bằng mock: `powershell.exe -NoProfile -File scripts/assistant/test-assistant-iis.ps1`.
