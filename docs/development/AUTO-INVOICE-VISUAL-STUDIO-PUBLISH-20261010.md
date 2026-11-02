# AutoInvoice: chạy Visual Studio, publish và nâng cấp

Worker dùng cùng cơ chế nạp cấu hình cho Debug và bản publish. Cấu hình của mỗi máy nằm ngoài thư mục ứng dụng; nâng cấp chỉ thay mã, giữ database, bộ khóa, PFX và tài khoản service.

## Trên máy phát triển

1. Mở solution trong Visual Studio, chọn project `GaoApp.AutoInvoiceWorker`.
2. Chọn profile **GaoApp.AutoInvoiceWorker - Kiểm tra cấu hình** để đọc khóa và thử giải mã mật khẩu Viettel đã lưu. Profile không chạy queue, ghi heartbeat hay gọi Viettel.
3. Khi cần chạy Worker, chọn profile **GaoApp.AutoInvoiceWorker**. Có thể đặt Web và Worker cùng Start trong cấu hình Multiple startup projects của solution.

Development hiện dùng cùng database và KeysPath với Web Development. Đã chạy executable Debug với `--environment Development --check-keys --check-credentials`: cả hai khóa đọc được, mật khẩu cấu hình #2/cửa hàng #1 giải mã OK, exit 0. Đây là kiểm tra trên máy phát triển, không xác nhận quyền tài khoản Worker ở server.

Nếu muốn tách cấu hình local khỏi source, đặt JSON riêng tại `C:\GaoAppData\DeployConfig\worker.appsettings.Development.json`, gồm `ConnectionStrings`, `DataProtection` và các phần cần thiết. File phải trỏ đúng key ring/PFX của Web trên máy đó. Nếu file chưa tồn tại, F5 vẫn dùng appsettings Development hiện có. Không đặt mật khẩu vào launchSettings; đã bỏ override KeysPath cố định ở profile để cấu hình máy có hiệu lực.

## Chuẩn bị server một lần

Giữ file `C:\GaoAppData\DeployConfig\worker.appsettings.Production.json` bên ngoài Web/Worker. Cấu trúc tham khảo trong [runbook IIS](PUBLISH-IIS-WITH-AUTO-INVOICE-WORKER.md#81-cấu-hình-riêng-worker): connection string runtime, storage cùng Web, DataProtection cùng key ring/PFX của Web. File này chứa thông tin riêng của server, không đưa vào Git hoặc publish từ máy phát triển.

Để chuyển cấu hình của Worker cũ ra ngoài và sửa chênh lệch khóa/quyền trên cùng server, chạy PowerShell 7 với quyền Administrator:

```powershell
.\scripts\auto-invoice\Sync-GaoAppAutoInvoiceWorkerProtection.ps1 `
  -AppPoolName 'GaoAppPool' -RepairFilePermissions -WhatIf
# Dừng Worker theo quy trình vận hành; chạy lại bỏ -WhatIf.
```

Thay pool/service bằng tên thật. Script chỉ lấy DataProtection hiệu lực từ Web. Nếu file host chưa tồn tại, script giữ các phần khác từ cấu hình Worker cũ; nếu đã tồn tại, giữ các phần khác từ file host. Nó không sao chép tài khoản SQL của Web. File publish không bị chỉnh. Bản sao file host/ACL nằm cùng thư mục cấu hình riêng. Thư mục riêng mới tạo được bảo vệ khỏi quyền đọc rộng; quyền đọc được thêm cho tài khoản service khi dùng `-RepairFilePermissions`.

Nếu dùng đường dẫn khác, truyền `-WorkerHostSettingsPath` cho Sync, `-HostSettingsPath` cho Install, hoặc `--host-settings` khi chạy executable. Script tự giữ đường dẫn đã ghim trong service hiện có. Các override DataProtection qua biến môi trường/command line vẫn ưu tiên hơn file host; đối chiếu hoặc bỏ override cũ nếu chúng trỏ bộ khóa khác.

## Publish và nâng cấp

Publish project Worker từ Visual Studio với profile Folder/Release/win-x64 hoặc:

```powershell
dotnet publish .\GaoApp.AutoInvoiceWorker\GaoApp.AutoInvoiceWorker.csproj `
  -c Release -r win-x64 --self-contained false -o 'D:\Publish\GaoApp\Worker'
```

Project tự đóng gói `eng/deployment/worker.appsettings.json` không có secret và lọc cấu hình Development, launchSettings, keys/PFX. Chuyển cả thư mục Worker, không chỉ DLL. Publish Web theo quy trình hiện có, từ cùng source.

Trên server, cập nhật service sang thư mục release mới:

```powershell
.\scripts\auto-invoice\Install-GaoAppAutoInvoiceWorker.ps1 `
  -InstallDirectory 'D:\GaoAppDeploy\workers\RELEASE_MOI' -WhatIf
# Khi preview đúng, chạy cùng lệnh bỏ -WhatIf.
```

Script cập nhật tại chỗ, giữ tài khoản/recovery/startup, ghim Production và file host. Nó để service dừng sau cập nhật. Cấp quyền đọc/chạy thư mục release mới cho tài khoản service. Nếu cài lần đầu, service dùng `NT SERVICE\GaoApp.AutoInvoiceWorker`, Manual; cấu hình SQL/ACL và Automatic Delayed Start theo runbook.

Chạy kiểm tra dưới đúng tài khoản/môi trường service trước khi Start:

```powershell
& 'D:\GaoAppDeploy\workers\RELEASE_MOI\GaoApp.AutoInvoiceWorker.exe' `
  --environment Production --check-keys --check-credentials
Start-Service 'GaoApp.AutoInvoiceWorker'
```

Nếu service dùng đường dẫn host tùy chỉnh, thêm `--host-settings 'DUONG_DAN_JSON_THAT'` vào lệnh kiểm tra. Administrator đọc được khóa không chứng minh tài khoản service đọc được. Khi khởi động normal, Worker xử lý theo trạng thái bật/tạm ngưng đang lưu trong database.

Mỗi lần nâng cấp sau chỉ thay release và cập nhật đường dẫn exe nếu dùng thư mục release mới. Không chép cấu hình/password từ máy phát triển lên server; không tạo lại PFX hoặc xóa keys. Khi chuyển sang máy server khác, DPAPI của máy cũ cần quy trình di chuyển riêng.

## Kiểm chứng

- Debug build, 18 test cấu hình/credential/key và hai script test đã qua.
- Debug Development thực tế đọc hai khóa và giải mã mật khẩu Viettel cấu hình #2 thành công; không phát hành hóa đơn.
- Publish bằng đúng profile Folder của Visual Studio (Release/win-x64/framework-dependent) đã qua. Gói chỉ có appsettings mẫu không có secret, không có file Development/launchSettings/PFX.
- Executable publish chạy `--environment Production --host-settings ... --check-keys --check-credentials` từ `C:\Windows\System32`, dùng connection string Development chỉ trong bộ nhớ/child environment: exit 0, hai khóa mở được, mật khẩu đã lưu giải mã được. Hash bộ khóa trước/sau giống nhau. Không chạy queue hoặc gọi Viettel.
- Chưa cập nhật hoặc chạy kiểm tra dưới tài khoản service trên server thật.
