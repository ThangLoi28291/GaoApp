# GaoApp Auto Invoice Worker

Đây là Windows Service chạy cùng server với GaoApp Web. Worker đọc cấu hình theo cùng quy tắc khi chạy Visual Studio hoặc bản publish: appsettings → file riêng của máy → biến môi trường → command line.

Visual Studio dùng Development. Nếu tồn tại `C:\GaoAppData\DeployConfig\worker.appsettings.Development.json`, Worker tự đọc file đó; nếu chưa có thì dùng appsettings Development hiện có. Profile `GaoApp.AutoInvoiceWorker - Kiểm tra cấu hình` chỉ kiểm tra khóa/mật khẩu đã lưu, không chạy queue.

Server dùng `C:\GaoAppData\DeployConfig\worker.appsettings.Production.json`, nằm ngoài thư mục publish. Publish chỉ đóng gói mẫu cấu hình không có secret; file riêng của server, key ring và PFX được giữ qua các lần nâng cấp. Xem [hướng dẫn chạy Visual Studio và nâng cấp](../docs/development/AUTO-INVOICE-VISUAL-STUDIO-PUBLISH-20261010.md).

Luồng phát hành dùng chung `IAutoInvoiceService` với nút phát hành thủ công trong Web. Worker chỉ gọi Viettel khi Admin đã bật cấu hình trong màn hình `/Admin/AutoInvoice`; trạng thái bật/tạm ngưng nằm trong SQL và vẫn giữ qua restart.

Publish:

```powershell
dotnet publish GaoApp.AutoInvoiceWorker/GaoApp.AutoInvoiceWorker.csproj -c Release -r win-x64 --self-contained false -o C:\GaoApp\AutoInvoiceWorker
```

Sau khi publish, cấu hình file riêng của server và dùng `scripts/auto-invoice/Install-GaoAppAutoInvoiceWorker.ps1` với PowerShell 7/Administrator. Script hỗ trợ `-WhatIf`, ghim môi trường Production/file host, giữ tài khoản/startup/recovery của service cũ, và để service dừng trừ khi truyền `-Start`. Migration `20260924100000_AddAutoInvoiceIssuance` triển khai qua quy trình Migrator đã có.

Worker và Web phải dùng cùng bộ khóa DataProtection và cùng PFX/private key nếu cấu hình PFX. Worker chỉ sử dụng khóa hiện có; Web quản lý tạo/xoay khóa. Quyền của IIS AppPool không tự cấp cho tài khoản Windows Service.

Nếu Web Test kết nối Viettel thành công nhưng Worker báo `Keyset does not exist`, xem [hướng dẫn đồng bộ cấu hình và quyền khóa](../docs/development/AUTO-INVOICE-CREDENTIAL-KEYSET-20261010.md). Không cần đổi mật khẩu Viettel để xử lý chênh lệch cấu hình/quyền khóa.

Hai lệnh chẩn đoán không chạy queue hoặc gọi Viettel:

```powershell
.\GaoApp.AutoInvoiceWorker.exe --environment Production --check-keys
.\GaoApp.AutoInvoiceWorker.exe --environment Production --check-credentials
```

Kiểm tra dưới đúng tài khoản/môi trường Worker. Kết quả 0 là thành công; 1 là lỗi hoặc không có khóa/cấu hình cần kiểm tra.
