# Publish GaoApp Web và Worker lên Windows Server / IIS

Đối chiếu source ngày 27/09/2026. Tài liệu này hướng dẫn thao tác; chưa publish, cài service, chạy migration hay phát hành hóa đơn trên server. Các tên site, pool, đường dẫn và hostname bên dưới là ví dụ, phải thay bằng cấu hình thực tế.

## 1. Những thành phần phải có

| Thành phần | Nơi chạy | Cách khởi động |
|---|---|---|
| GaoApp.Web | IIS, application pool riêng | IIS tự khởi động, AlwaysRunning + Application Initialization + Preload |
| GaoApp.AutoInvoiceWorker | Windows Service riêng | Automatic (Delayed Start), không phụ thuộc Chrome/RDP |
| GaoApp.Migrator | Công cụ triển khai | Chỉ chạy khi có migration cần áp dụng; không cài thành service |
| SQL Server | Server database hiện có | Nếu SQL nằm trên máy này, service SQL cũng phải tự khởi động |

Worker phát hành không nằm trong IIS. Web có AcbCallbackWorker riêng để xử lý callback ACB; nó khác với AutoInvoiceWorker phát hành hóa đơn.

Script `scripts/publish-staging-release.ps1` hiện chỉ đóng gói Web và Migrator (hoặc Web khi có `-WebOnly`), chưa đóng gói AutoInvoiceWorker. Phải publish Worker riêng từ cùng source. Script `scripts/auto-invoice/Install-GaoAppAutoInvoiceWorker.ps1` hiện xóa/cài lại service nếu đã tồn tại và Start ngay; không dùng script đó cho lần nâng cấp cần giữ tài khoản/config/recovery hiện có. Các bước dưới cài Manual trước, cấu hình đủ rồi mới Start.

## 2. Chuẩn bị trên server

1. Cài **.NET 8 Hosting Bundle x64** từ Microsoft. Gói này cung cấp runtime và ASP.NET Core Module cho IIS. Nếu cài Hosting Bundle trước IIS, chạy Repair bộ cài sau khi thêm IIS. Không cần Visual Studio trên server.
2. Thêm IIS role service **Application Initialization**. Nếu POS dùng kết nối SignalR WebSocket, bật **WebSocket Protocol**.
3. Mở Windows PowerShell 5.1 x64 bằng Run as administrator để thao tác IIS/service. Một số script kiểm tra trong repo yêu cầu PowerShell 7; xem dòng `#requires` của từng script.
4. Kiểm tra `dotnet --list-runtimes`: phải có Microsoft.NETCore.App 8.x và Microsoft.AspNetCore.App 8.x.
5. Đảm bảo DNS/HTTPS binding đúng hostname cửa hàng/admin, SQL truy cập được, server gọi được nhà cung cấp hóa đơn và ACB.

Nếu Hosting Bundle yêu cầu restart IIS/server, thực hiện trong thời gian bảo trì vì restart toàn IIS ảnh hưởng các site khác. Cập nhật GaoApp bình thường chỉ dừng site/pool của GaoApp.

Ví dụ bố trí dữ liệu:

```text
D:\GaoAppDeploy\releases\20260927-xxxx\web\       # IIS physicalPath
D:\GaoAppDeploy\releases\20260927-xxxx\migrator\  # nếu cần migration
D:\GaoAppDeploy\workers\20260927-xxxx\             # Worker cùng source
C:\GaoAppData\Uploads\                            # dữ liệu giữ qua các release
C:\GaoAppData\DataProtectionKeys\                 # khóa giữ qua các release
C:\GaoAppData\Certificates\                       # PFX bảo vệ khóa
C:\GaoAppData\Logs\
C:\GaoAppData\DeployConfig\                       # cấu hình riêng host, ACL hạn chế
```

Không đặt Worker, certificate, backup SQL hay thư mục khóa dưới thư mục Web được IIS phục vụ.

## 3. Khóa mã hóa: làm trước khi chuyển máy

Web và Worker cần **cùng database, cùng bộ DataProtection keys có khả năng giải mã, cùng certificate/mật khẩu certificate nếu dùng certificate**. Code đã đặt chung ApplicationName = GaoApp.

Nếu chỉ nâng cấp trên server đang chạy: giữ nguyên khóa/certificate của server. Không chép thư mục khóa từ máy Visual Studio đè lên server và không xóa khóa cũ khi deploy.

Nếu chuyển database từ máy khác: phải xử lý khả năng giải mã trước khi cho Worker phát hành. Khóa Windows được bọc bằng DPAPI của máy cũ không thể dùng ở máy mới chỉ bằng việc chép file XML. Đặt một PFX mới cũng không tự chuyển đổi các khóa cũ. Cần di chuyển/re-encrypt bằng môi trường cũ hoặc nhập lại thông tin tích hợp trên server mới, bao gồm ACB Client secret, callback key và thông tin nhà cung cấp hóa đơn liên quan.

Gói Web publish hiện dùng `RequirePortableKeys=true`. Phải cấp `DataProtection:CertificatePath` và `CertificatePassword` hợp lệ; PFX phải có private key RSA >= 2048 bit, còn hạn. Nếu đã có certificate bảo vệ dữ liệu thì tiếp tục dùng certificate đó; không tạo mới để thử sửa lỗi giải mã.

Sao lưu **database + toàn bộ key ring + certificate/private key + mật khẩu tương ứng + uploads** theo quy trình của server. Không đưa secrets vào Git, tệp hướng dẫn hoặc gửi trong chat.

## 4. Publish trên máy phát triển

Dừng sửa source trong lúc đóng gói. Mở PowerShell tại thư mục solution. Các lệnh tạo package, không kết nối production.

```powershell
Set-Location 'D:\GaoApp\GAOAPP-SA-20260819-01'
dotnet restore GaoApp.sln
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
dotnet build GaoApp.sln -c Release --no-restore -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
```

Chạy các kiểm thử liên quan đến thay đổi trên database test theo cấu hình test của repo. Không trỏ test vào database vận hành. Kết quả 208 kiểm thử ACB trong cuộc trao đổi này không thay thế kiểm thử toàn bộ bản phát hành/Worker.

**Web:** chọn một trong hai lệnh:

```powershell
# Schema server đã tương thích, chỉ cập nhật code:
.\scripts\publish-staging-release.ps1 -WebOnly

# Hoặc: có schema cần nâng cấp và đã review:
# .\scripts\publish-staging-release.ps1
```

Script in đường dẫn `publish\releases\<release-id>` sau khi kiểm tra package và tạo release-manifest.json. Ghi lại release-id thực tế.

**Worker:** publish vào thư mục riêng, nằm ngoài release Web để không làm sai manifest của Web:

```powershell
$releaseId = 'THAY_BANG_RELEASE_ID_VUA_TAO'
$workerOut = Join-Path (Get-Location) "publish\workers\$releaseId"
if (Test-Path -LiteralPath $workerOut) { throw 'Dung thu muc Worker moi cho moi release' }
dotnet publish .\GaoApp.AutoInvoiceWorker\GaoApp.AutoInvoiceWorker.csproj `
  -c Release -r win-x64 --self-contained false -o $workerOut
if ($LASTEXITCODE -ne 0) { throw 'Publish Worker failed' }

# Worker hiện chưa import PublishSafety.targets như Web/Migrator.
# Thay cấu hình development trong package bằng mẫu không chứa SQL/secret thực.
Copy-Item -LiteralPath '.\eng\deployment\migrator.appsettings.json' `
  -Destination (Join-Path $workerOut 'appsettings.json')
$workerDevSettings = Join-Path $workerOut 'appsettings.Development.json'
if (Test-Path -LiteralPath $workerDevSettings) {
    Remove-Item -LiteralPath $workerDevSettings
}
```

Mẫu Migrator được dùng ở đây chỉ làm **appsettings.json tối thiểu, không chứa secret**, với các mục shared infrastructure/seed/DataProtection. Nó không đổi executable Worker thành Migrator. Worker đọc phần Logging mặc định; không tự bật Serilog bằng tệp này.

Kiểm tra Worker có `GaoApp.AutoInvoiceWorker.exe`, `.dll`, `.deps.json`, `.runtimeconfig.json` và các dependency. Rà các appsettings khác nếu source đã thêm cấu hình host riêng; không đưa connection string/secret development vào gói chuyển.

Tạo danh sách hash Worker trước khi chuyển:

```powershell
$workerRoot = (Resolve-Path -LiteralPath $workerOut).Path
Get-ChildItem -LiteralPath $workerRoot -File -Recurse | ForEach-Object {
    [pscustomobject]@{
        Path = $_.FullName.Substring($workerRoot.Length + 1)
        SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
} | Export-Csv -LiteralPath "$workerOut.sha256.csv" -NoTypeInformation -Encoding UTF8
```

Chuyển nguyên release Web, Worker, danh sách hash và các script deploy cần dùng. Giữ hash tin cậy của manifest qua kênh riêng. Không chép chỉ vài DLL, không chép `bin\Debug`, không chép đè thư mục đang chạy.

## 5. Cấu hình Web trên IIS

Nếu site đã vận hành, giữ cấu hình host ở application pool riêng; khi đổi physicalPath sang release mới cấu hình này vẫn còn. Publish không mang appsettings.Development/secrets từ Visual Studio lên server. Nếu cấu hình hiện nằm trong web.config cũ, cần chuyển có kiểm soát sang cấu hình pool trước khi thay release.

Trong IIS Configuration Editor ở cấp server: `system.applicationHost/applicationPools` → collection → chọn đúng pool GaoApp → `environmentVariables`. Giữ các biến đã dùng; thêm/sửa có chọn lọc, không ghi đè môi trường của site khác. Mẫu tên khóa nằm ở `eng/deployment/host-environment.example.json`.

| Biến của riêng pool | Giá trị |
|---|---|
| DOTNET_ENVIRONMENT | Production |
| ASPNETCORE_ENVIRONMENT | Production |
| ConnectionStrings__DefaultConnection | SQL database vận hành thực, tài khoản runtime |
| AppUrl__BaseUrl / AppUrl__AdminUrl | HTTPS origin thật của root/admin |
| Tenant__RootDomain / Tenant__AdminSubdomain | Domain/subdomain đúng cấu hình cửa hàng |
| AllowedHosts | Các host của hệ thống, không dùng wildcard `*` toàn Internet |
| Storage__UploadRoot | C:\GaoAppData\Uploads hoặc thư mục dữ liệu thực |
| Storage__CreateIfMissing | false; tạo thư mục và cấp ACL trước |
| DataProtection__KeysPath | Thư mục khóa bền vững đang dùng |
| DataProtection__RequirePortableKeys | true theo profile deployment hiện có |
| DataProtection__CertificatePath / CertificatePassword | Certificate bảo vệ khóa và mật khẩu tương ứng |
| SeedData__EnableDemoSeed / EnableDefaultAdminSeed | false |
| ProductionBootstrap__Enabled | false |
| Proxy__EnableForwardedHeaders | true theo cấu hình IIS/proxy hiện có |
| Proxy__KnownProxies__0 / __1 | IP proxy tin cậy thực tế; loopback khi IIS local |

Giữ cấu hình tính năng và tích hợp của host. Mẫu publish đặt một số tính năng false để không tự kích hoạt khi chuyển môi trường; ví dụ InputInvoiceLibrary, ReceivingWorkbench. `AcbCallbackRouting__Hosts__0` cần giữ host callback đã đăng ký, nếu hệ thống dùng ánh xạ callback theo host. Không sao chép mù toàn bộ mẫu staging vào host đang dùng.

Nếu ghi log Web ra file, giữ các biến Serilog file sink ở pool trỏ `C:\GaoAppData\Logs\web-.log` (xem mẫu host environment). Một số nhật ký chuyên biệt ACB hiện vẫn dùng App_Data/Logs theo content root; kiểm tra đường dẫn thực và cấp Modify có giới hạn cho thư mục log đó, không cấp Write toàn bộ release.

Pool identity cần Read/Execute thư mục Web, Modify Uploads/Keys/Logs, Read tệp PFX. Nếu dùng Integrated Security cho SQL, tài khoản SQL của pool phải được cấp quyền riêng; chạy được SQL từ tài khoản Administrator không chứng minh pool truy cập được.

## 6. Schema database và thời gian bảo trì

Đây là database đã vận hành: kiểm tra migration đã áp dụng và pending; không khởi tạo database mới để khắc phục lỗi.

Source hiện có `20260924100000_AddAutoInvoiceIssuance` và `20260926113012_AddInvoiceIssuanceRoutingAndBuyerSelfService`. Không chỉ áp migration Worker nếu còn các migration trước đó chưa có. So sánh với database thực, không suy ra từ tên release.

Nếu không có thay đổi schema cần thiết: bỏ qua Migrator.

Nếu có: backup database, tạm ngưng phát hành qua `/Admin/AutoInvoice`, đợi công việc đang chạy hoàn tất/đối soát trạng thái chưa rõ, dừng Worker và Web trước migration. Dừng cả các tiến trình khác có thể ghi cùng database theo kế hoạch bảo trì.

Chạy công cụ với tài khoản migration, cấu hình Production/SQL/Keys/PFX đã xác định. **Biến môi trường của IIS pool không tự truyền sang PowerShell hoặc Worker.** Migrator không dùng launchSettings của Visual Studio. Khi môi trường của phiên Migrator đã được cấu hình đúng:

```powershell
$env:DOTNET_ENVIRONMENT = 'Production'
dotnet 'D:\GaoAppDeploy\releases\RELEASE_ID\migrator\GaoApp.Migrator.dll' --schema-only
if ($LASTEXITCODE -ne 0) { throw 'Migration failed: giu Web/Worker dung va kiem tra' }
```

Không chạy `--security-seed`, `--bootstrap`, `--demo-seed` trong cập nhật schema thông thường. Menu/quyền mới nếu thiếu cần thao tác dữ liệu riêng đúng phạm vi, không seed toàn hệ thống để thử.

Runbook chuẩn TH1/TH2 và script switch/backup/verify nằm trong `GAOAPP-DEPLOYMENT-RUNBOOK-20260913.md`. Các script đó có ràng buộc site GaoApp/pool GaoAppPool/GaoMart; chỉ dùng nguyên trạng nếu topology host thực sự khớp. Chúng chưa quản lý Worker, nên operator phải dừng/chuyển Worker riêng. Với topology khác, không bỏ các guard để ép chạy; thực hiện quy trình IIS tương đương trên đúng site.

## 7. Cho Web tự lên sau khi server reboot

Trong IIS Manager, chọn pool riêng của GaoApp → Advanced Settings:

| Thuộc tính | Giá trị |
|---|---|
| .NET CLR Version | No Managed Code |
| Managed Pipeline Mode | Integrated |
| Enable 32-Bit Applications | False cho bản x64 |
| Start Automatically | True |
| Start Mode | AlwaysRunning |
| Idle Time-out (minutes) | 0 |
| Maximum Worker Processes | 1 |

Chọn site/application GaoApp → Advanced Settings: **Preload Enabled = True**; site có **Start Automatically = True**. Phải cài IIS Application Initialization thì preload mới có tác dụng. Kiểm tra Windows services **WAS** và **W3SVC** không bị Disabled và được cấu hình khởi động phù hợp cùng IIS; SQL local đặt Automatic nếu server này chạy SQL.

Đổi Physical Path của riêng site GaoApp sang `...\releases\RELEASE_ID\web`, sau khi dừng pool/site đúng quy trình. Giữ bindings, certificate HTTPS và cấu hình pool. Start pool/site, kiểm tra HTTPS `/health/ready` trả Healthy.

AlwaysRunning + Preload phục vụ cả việc khởi động Web khi chưa có khách mở trình duyệt. Đây là điểm cần cho hosted worker callback ACB trong Web. Không cần để Chrome hoặc phiên Remote Desktop mở trên server.

## 8. Cài AutoInvoiceWorker thành Windows Service

### 8.1. Cấu hình riêng Worker

Tạo bản cấu hình gốc tại `C:\GaoAppData\DeployConfig\worker.appsettings.Production.json`, chỉ admin triển khai và các tài khoản thực sự cần mới được đọc. Không đưa tệp này vào source/package public. Ví dụ cấu trúc (thay placeholder, không chạy với chuỗi mẫu):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "CHUOI_SQL_CUA_SERVER_CHO_WORKER"
  },
  "Storage": {
    "UploadRoot": "C:\\GaoAppData\\Uploads",
    "CreateIfMissing": false
  },
  "DataProtection": {
    "KeysPath": "C:\\GaoAppData\\DataProtectionKeys",
    "RequirePortableKeys": true,
    "CertificatePath": "C:\\GaoAppData\\Certificates\\gaoapp-data-protection.pfx",
    "CertificatePassword": "MAT_KHAU_PFX_TREN_SERVER"
  },
  "SeedData": {
    "EnableDemoSeed": false,
    "EnableDefaultAdminSeed": false
  },
  "ProductionBootstrap": { "Enabled": false },
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft": "Warning" }
  }
}
```

SQL runtime nên dùng `Encrypt=True;TrustServerCertificate=False` với chứng thư SQL tin cậy, đúng database. Web/Worker có thể dùng tài khoản SQL khác nhau nhưng cùng database và quyền runtime phù hợp. Không dùng tài khoản migration/db-owner mặc định cho Worker. Storage:UploadRoot của Worker phải trỏ cùng thư mục external storage với Web để lưu/đọc file hóa đơn; nếu bỏ trống, InvoiceFileStorage hiện fallback vào wwwroot/uploads dưới thư mục Worker.

Sau khi đối chiếu hash của package Worker trên server, chép cấu hình host vào **thư mục Worker mới**:

```powershell
$workerDir = 'D:\GaoAppDeploy\workers\RELEASE_ID'
Copy-Item -LiteralPath 'C:\GaoAppData\DeployConfig\worker.appsettings.Production.json' `
    -Destination (Join-Path $workerDir 'appsettings.Production.json')
```

Đây là file host được thêm sau khi kiểm tra package, không đưa ngược về gói development. Mỗi lần đổi release Worker phải chép lại từ bản cấu hình host đã bảo quản. Không sửa appsettings development rồi cho rằng service sẽ đọc file đó.

### 8.2. Lần đầu: tạo service nhưng chưa chạy

Ví dụ dùng virtual service account trên Windows, không cần mật khẩu. Nếu SQL ở máy khác và dùng Windows Authentication, cần tài khoản domain/gMSA phù hợp thay vì mặc định ví dụ này; DBA cấp quyền theo identity thực.

```powershell
$serviceName = 'GaoApp.AutoInvoiceWorker'
$workerDir = 'D:\GaoAppDeploy\workers\RELEASE_ID'
$workerExe = Join-Path $workerDir 'GaoApp.AutoInvoiceWorker.exe'
if (!(Test-Path -LiteralPath $workerExe)) { throw 'Khong tim thay Worker exe' }
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw 'Service da ton tai: dung quy trinh cap nhat, khong cai/xoa lai'
}
New-Service -Name $serviceName `
    -DisplayName 'GaoApp Auto Invoice Worker' `
    -Description 'Phat hanh hoa don tu dong GaoApp' `
    -BinaryPathName ('"{0}" --environment Production' -f $workerExe) `
    -StartupType Manual
sc.exe config $serviceName obj= 'NT SERVICE\GaoApp.AutoInvoiceWorker'
if ($LASTEXITCODE -ne 0) { throw 'Chua cau hinh duoc tai khoan service' }
```

Đối số `--environment Production` bảo đảm Worker đọc appsettings.Production.json; không phụ thuộc biến `$env:` trong phiên PowerShell đã đóng. Worker trong source đã đọc cấu hình từ thư mục executable, không từ System32.

Cấp quyền cho `NT SERVICE\GaoApp.AutoInvoiceWorker`: Read/Execute thư mục Worker và cấu hình của release; Modify DataProtectionKeys và thư mục lưu file hóa đơn trong Uploads (đúng phạm vi ứng dụng cần); Read certificate PFX. Cấu hình/keys/PFX không cấp đọc rộng cho Users/Everyone. Chỉ thêm quyền vào thư mục đúng phạm vi, không reset ACL cả C:\GaoAppData.

Nếu SQL local dùng Integrated Security, tạo login/user SQL cho identity Worker và cấp quyền runtime cần thiết. Quyền của IIS AppPool không tự áp dụng cho Worker. Nếu SQL dùng SQL Authentication, connection string Worker phải chứa tài khoản runtime hợp lệ và file config được bảo vệ ACL.

### 8.3. Tự khởi động và recovery

Sau khi config/ACL/SQL/schema đã sẵn sàng:

```powershell
sc.exe config 'GaoApp.AutoInvoiceWorker' start= delayed-auto
if ($LASTEXITCODE -ne 0) { throw 'Khong dat duoc Delayed Auto Start' }
sc.exe failure 'GaoApp.AutoInvoiceWorker' reset= 86400 actions= restart/60000/restart/60000/restart/60000
if ($LASTEXITCODE -ne 0) { throw 'Khong dat duoc Recovery' }
sc.exe failureflag 'GaoApp.AutoInvoiceWorker' 1
if ($LASTEXITCODE -ne 0) { throw 'Khong dat duoc failure flag' }
Start-Service 'GaoApp.AutoInvoiceWorker'
Get-Service 'GaoApp.AutoInvoiceWorker' | Select-Object Name, Status, StartType
sc.exe qc 'GaoApp.AutoInvoiceWorker'
sc.exe qfailure 'GaoApp.AutoInvoiceWorker'
```

Trong Services, trạng thái phải Running, startup Automatic (Delayed Start), đường dẫn exe đúng release, có `--environment Production`. Recovery Restart sau 60 giây cho các lần lỗi là cấu hình ví dụ có thể chỉnh theo vận hành.

Recovery của Windows chỉ áp dụng khi SCM ghi nhận service failure, không tự chạy lại khi người quản trị chủ động Stop. Code hiện bắt lỗi SQL/mạng trong chu kỳ và thử lại khoảng 5 giây sau. Ngoại lệ nghiêm trọng khác có thể khiến .NET dừng host với exit code bình thường; cấu hình Recovery không bảo đảm mọi tình huống này đều được khởi động lại. Sau reboot, Automatic Delayed Start vẫn là cơ chế tự khởi động độc lập. Muốn giám sát tình trạng treo/dừng sạch cần theo dõi heartbeat hoặc bổ sung cơ chế giám sát riêng.

## 9. Cho phép phát hành và xác nhận Worker thực sự chạy

Trước lần Start đầu trên production, kiểm tra trạng thái bật/tạm ngưng phát hành của tất cả cửa hàng. Trạng thái này lưu trong database; nếu đang bật, Start Worker có thể bắt đầu xử lý hóa đơn đủ điều kiện. Việc chỉ mở service không phải phép thử vô hại nếu queue đang có việc.

1. Khi nghiệm thu cấu hình, để phát hành Tạm ngưng trước, Start service và kiểm tra kết nối/heartbeat.
2. Truy cập đúng cửa hàng tại `/Admin/AutoInvoice`: **Worker: Đang chạy**, Heartbeat cập nhật, không có lỗi giải mã/SQL.
3. Kiểm tra cấu hình nhà cung cấp hóa đơn, mẫu/ký hiệu, phạm vi ngày, lịch phát hành, giờ địa phương và hóa đơn đang chờ.
4. Khi sẵn sàng vận hành, bật **Bật phát hành tự động sau khi lưu**. Không phát hành hóa đơn giả để thử trên môi trường thật.
5. Sau khởi động lại server có kiểm soát, kiểm tra IIS/pool, `/health/ready`, Worker service và heartbeat từ máy client. Worker không cần tài khoản đăng nhập RDP; không cần F5 Visual Studio.

`/health/ready` của Web chỉ kiểm tra readiness Web/DB/storage, không chứng minh Worker phát hành đang hoạt động. Service báo Running cũng chưa chứng minh kết nối Viettel/thông tin phát hành hợp lệ; cần xem heartbeat và lỗi nghiệp vụ.

Worker hiện chỉ cấu hình console logging (ClearProviders rồi AddConsole), không có file log Worker bền vững hay Event Log provider riêng. Không kết luận không lỗi chỉ vì Event Viewer trống. Xem LastError/heartbeat trong màn hình AutoInvoice; nếu cần console để chẩn đoán, dừng service trước và để phát hành tạm ngưng rồi mới chạy exe thủ công, tránh hai Worker cùng hoạt động.

## 10. Những lần nâng cấp sau

1. Publish Web và Worker cùng source vào release mới; giữ release/config cũ để quay lại.
2. Tạm ngưng phát hành, đợi tác vụ đang gửi kết thúc/đối soát; dừng Worker, rồi dừng riêng Web/pool theo maintenance.
3. Chỉ áp migration khi cần, có backup và review. Không chạy seed tự động.
4. Đổi physicalPath IIS sang Web mới. Chép config production host vào Worker mới, cấp ACL như release cũ.
5. Đổi đường dẫn service đang có, không xóa/cài lại service:

```powershell
$workerExe = 'D:\GaoAppDeploy\workers\RELEASE_ID_MOI\GaoApp.AutoInvoiceWorker.exe'
Stop-Service 'GaoApp.AutoInvoiceWorker'
$serviceConfig = Get-CimInstance Win32_Service -Filter "Name='GaoApp.AutoInvoiceWorker'"
if (!$serviceConfig) { throw 'Khong tim thay service' }
$change = Invoke-CimMethod -InputObject $serviceConfig -MethodName Change `
    -Arguments @{ PathName = ('"{0}" --environment Production' -f $workerExe) }
if ($change.ReturnValue -ne 0) { throw "Khong doi duoc duong dan service: $($change.ReturnValue)" }
```

6. Start Web, kiểm tra readiness; Start Worker, kiểm tra heartbeat/giải mã; bật lại phát hành khi đã sẵn sàng. Kiểm tra service identity/Delayed Start/Recovery được giữ.
7. Nếu cần rollback, quay cả Web/Worker về cặp release tương thích. Nếu schema đã thay đổi phải đánh giá tương thích; không tự chạy Down hoặc restore SQL làm mất giao dịch mới.

Không cần mở Visual Studio, chạy file exe bằng tay mỗi ngày, hay tạo shortcut trong Startup. Windows Service và IIS xử lý việc tự khởi động.

## Nguồn

- Source: GaoApp.AutoInvoiceWorker/Program.cs, AutoInvoiceWorkerHostedService.cs; scripts/publish-staging-release.ps1; scripts/auto-invoice/Install-GaoAppAutoInvoiceWorker.ps1; eng/deployment/PublishSafety.targets; GaoApp.Infrastructure/Security/HostDataProtectionEncryption.cs.
- [Microsoft: ASP.NET Core trên IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-8.0).
- [Microsoft: Windows Service và recovery](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service).
- [Microsoft: sc.exe config](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-config).
