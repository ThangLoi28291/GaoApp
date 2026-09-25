# GaoApp Auto Invoice Worker

Đây là Windows Service chạy cùng server với SQL Server và GaoApp Web. Service đọc `ConnectionStrings:DefaultConnection` từ `appsettings.json` hoặc biến môi trường, không cần mở trình duyệt.

Luồng phát hành dùng chung `IAutoInvoiceService` với nút phát hành thủ công trong Web. Worker chỉ gọi Viettel khi Admin đã bật cấu hình trong màn hình `/Admin/AutoInvoice`; trạng thái bật/tạm ngưng nằm trong SQL và vẫn giữ qua restart.

Publish:

```powershell
dotnet publish GaoApp.AutoInvoiceWorker/GaoApp.AutoInvoiceWorker.csproj -c Release -r win-x64 --self-contained false -o C:\GaoApp\AutoInvoiceWorker
```

Sau khi publish, cấu hình connection string và cài service bằng `scripts/auto-invoice/Install-GaoAppAutoInvoiceWorker.ps1` chạy với quyền Administrator. Migration `20260924100000_AddAutoInvoiceIssuance` phải được triển khai qua quy trình Migrator đã có; không chạy trực tiếp trên production trong bước build này.
