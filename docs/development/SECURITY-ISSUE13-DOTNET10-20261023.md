# Phần 13 — nâng GaoApp lên .NET 10 LTS

Phạm vi đã duyệt: nâng source, thư viện, CI và các gói Web/Migrator/worker/in; kiểm thử biệt lập và chuẩn bị cập nhật bản đã publish. Không kích hoạt bản mới trên IIS/Windows Service trong bước này.

## Bản bàn giao

- Release: `publish/releases/20261023-030413-8210c4dc/`.
- Web: `web/`; công cụ schema: `migrator/`.
- Worker hóa đơn: `autoinvoiceworker/`; dịch vụ in server: `labelprintserver/`.
- Bộ kết nối in Windows tự chứa runtime: `web/ClientPackages/GaoApp.LabelPrintClient-win-x64.zip`. Cấu hình pairing trong gói để trống; dùng lại cấu hình hợp lệ tại máy in khi nâng cấp.
- `release-manifest.json` ghi revision, trạng thái source có thay đổi chưa commit, kích thước và SHA-256 của từng file. Không dùng thư mục `bin` hoặc release thử trước đó để triển khai.
- Bộ cài đã tải và kiểm tra: `.artifacts/security-issue13/installers/dotnet-hosting-10.0.13-win.exe`. Đã kiểm tra SHA-512 theo Microsoft và chữ ký Authenticode hợp lệ. Chưa chạy bộ cài.
- Bộ công cụ deploy độc lập: `publish/deployment-kits/20261023-030413-8210c4dc-de9f0cde/`, có script, hướng dẫn, SQL để duyệt, bộ cài và manifest. Không cần mang toàn bộ source lên máy chủ.
- Bản Web-only đã sao chép đúng byte từ release chính và xác minh manifest: `publish/releases/20261023-030413-8210c4dc-web-only-de9f0cde/`. Chỉ dùng nhánh này khi CSDL đã đủ schema, không dùng trực tiếp với CSDL 79.

SDK dùng build: **10.0.402**. Runtime tối thiểu: **10.0.13**, cập nhật servicing trong nhánh 10.0 được phép; không tự nhảy major. Các gói framework-dependent cần cả `Microsoft.NETCore.App` và `Microsoft.AspNetCore.App`, kể cả worker vì dùng chung Infrastructure. Gói in client tự chứa runtime 10.0.13.

Máy phát triển cần SDK khớp `global.json`. Để build lại ngay trên workspace này bằng SDK portable đã chuẩn bị, mở PowerShell tại thư mục repo:

```powershell
$env:DOTNET_ROOT = (Resolve-Path .artifacts/security-issue13/dotnet-sdk).Path
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
dotnet --version
dotnet build GaoApp.sln -c Release --artifacts-path .artifacts/security-issue13/rebuild
```

Nguồn và checksum bộ cài được ghi tại `eng/deployment/net10-host-requirements.json`. Bản IIS module tối thiểu tương ứng: **20.0.26277.13**.

## Các thay đổi chính

1. Chuyển toàn bộ project hoạt động và publish profile sang `net10.0`; công cụ kiểm thử chuyển tiếp có tùy chọn riêng để dựng baseline .NET 8.
2. Đồng bộ EF Core, Identity, Hosting, DI/Logging và Drawing lên 10.0.13; cập nhật Serilog/tooling theo .NET 10. Giữ bộ Roslyn cùng phiên bản với tooling để tránh xung đột assembly.
3. Ghim `System.Security.Cryptography.Pkcs` 10.0.13, thay phiên bản 9.0.11 bị kéo gián tiếp vào từ SQL tooling. Restore audit cả phụ thuộc trực tiếp và gián tiếp không còn cảnh báo trong bản đã build.
4. Sửa điểm tương thích C# 14 (`field`), API mạng proxy và API nạp PFX mới; giữ nguyên chính sách trust proxy và đường dẫn khóa.
5. Publish worker/in từ cùng source, kiểm tra shared DLL trùng hash. Bộ in cũng sử dụng template cấu hình riêng để tránh đóng gói nhầm cấu hình tại máy.
6. Kiểm tra runtime/package từ chối bản vá cũ, preview và tự roll-forward major. CI dùng `global.json`, tạo gói đầy đủ và chạy các kiểm tra deployment.
7. Sửa lỗi bootstrap phát hiện khi smoke: tài khoản quản trị đầu tiên chưa có liên kết cửa hàng nên audit không biết nơi lưu bằng chứng. Migrator có scope audit riêng gắn với cửa hàng vừa tạo; transaction và yêu cầu lưu audit vẫn được giữ. Web tiếp tục dùng ngữ cảnh audit HTTP hiện có.
8. Sửa fixture EF InMemory để các context đồng thời dùng cùng database root; bổ sung thư mục XML đúng cấu hình trong smoke fixture.

Không thêm migration mới, không thêm menu chức năng mới cho phần .NET 10.

## CSDL hiện tại cần xử lý riêng

Kiểm tra SELECT-only trên bộ dữ liệu local `GaoAppDb` ngày 23/10/2026; máy chủ thật vẫn phải kiểm tra riêng đúng server/database:

- SQL Server: `17.0.1000.7`; compatibility level: `170`.
- Đã áp dụng **79 migration**, cuối cùng là `20261014140000_AddBankNotificationBridge`.
- Source đã có migration thứ 80 từ phần bảo mật audit trước đó: `20261022090000_AddDurableAuditOutbox`.

Đổi runtime .NET 8 → 10 tự nó không cần đổi cấu trúc dữ liệu. Tuy nhiên, **không thể chỉ chép Web mới lên CSDL 79 rồi coi là đã cập nhật xong**, vì source bảo mật hiện tại còn cần migration audit số 80.

SQL xuất để xem xét: [SECURITY-ISSUE13-AUDIT-79-TO-80.sql](SECURITY-ISSUE13-AUDIT-79-TO-80.sql). Chỉ thêm:

- Cột nullable `AuditLogs.EventId` và unique filtered index.
- Bảng `AuditOutbox`, check constraint và các index hàng đợi.
- Một dòng lịch sử migration mới.

Đã dựng CSDL 79 bằng assembly .NET 8/EF8 cũ, chạy bước 79→80 bằng EF10 trên chính CSDL GUID tạm đó và đối chiếu fingerprint. Các cấu trúc cũ ngoài những phần audit trên, lịch sử migration cũ và dữ liệu hàng hóa được giữ nguyên. Mật khẩu, khóa PFX, dữ liệu được bảo vệ và vé đăng nhập từ .NET 8 vẫn đọc được. CSDL tạm đã được fixture dọn sau kiểm tra; `GaoAppDb` chưa được ghi/thay đổi.

SQL này là tài liệu duyệt. Triển khai schema thật phải có backup đã thử restore, xác nhận đúng CSDL, danh sách migration được duyệt và dừng các nguồn ghi theo quy trình hiện có. Không dùng `--bootstrap`, `--security-seed`, `--demo-seed` để nâng một cửa hàng đang hoạt động.

## Quy trình cập nhật bản đã publish

### Chuẩn bị trước cửa sổ cập nhật

1. Giữ nguyên bản publish cũ và runtime .NET 8 để phục hồi khi cần. Sao lưu cấu hình IIS/Windows Service, cấu hình riêng từng máy, uploads/XML, key ring Data Protection, certificate/PFX và quyền truy cập của tài khoản chạy dịch vụ. Không tạo key ring mới và không đổi `ApplicationName=GaoApp`.
2. Xác nhận backup CSDL có thể restore và thống nhất điểm khôi phục. Với migration audit, giữ bằng chứng review/backup gắn đúng manifest, server, database và môi trường.
3. Chuyển release mới vào thư mục phiên bản mới, giữ cấu hình/khóa/dữ liệu bền vững ngoài thư mục release. Không chép đè file cấu hình thật bằng các template trống trong gói.
4. Kiểm tra cấu hình thư viện XML: giữ đường dẫn đang dùng; thư mục phải tồn tại và tài khoản Web phải đọc được. Template `XML` được chuẩn hóa theo cấu hình storage; nếu dùng biến môi trường thì cung cấp đường dẫn tuyệt đối đã kiểm tra.
5. Cài Hosting Bundle .NET 10.0.13 hoặc servicing mới hơn của nhánh 10.0 trong cửa sổ được duyệt. Bộ cài có thể cần restart IIS/dịch vụ liên quan, cần phối hợp với các site khác. Không gỡ .NET 8 ở bước này.
6. Chạy kiểm tra trên chính máy và tài khoản triển khai:

```powershell
./scripts/test-release-package.ps1 -ReleasePath <thu-muc-release>
./scripts/test-net10-host.ps1 -ReleasePath <thu-muc-release> -IisAppPoolName GaoAppPool
./scripts/test-host-preflight.ps1 -ReleasePath <thu-muc-release>
```

`test-net10-host` chỉ đọc: kiểm tra runtime x64, IIS pool 64-bit và phiên bản ASP.NET Core module. `test-host-preflight` dùng các biến cấu hình Production hiện có, kiểm tra thư mục/khóa/certificate và không kết nối SQL. Chạy thêm preflight cho worker bằng `-Component AutoInvoiceWorker` và `-Component LabelPrintServer` nếu có sử dụng.

Trên máy phát triển hiện tại, runtime cài toàn máy là 10.0.12 và đã bị preflight từ chối đúng yêu cầu. Build/test dùng SDK/runtime 10.0.13 portable trong workspace; không coi việc test thành công này là máy IIS đã được nâng runtime.

### Kích hoạt sau khi được duyệt triển khai

1. Tạm dừng giao dịch/nguồn ghi, timer/worker và hàng đợi in theo kế hoạch. Ghi lại đường dẫn và tài khoản dịch vụ hiện tại. Giữ URL callback, tên miền và cấu hình pairing theo hệ thống đang chạy.
2. Nếu CSDL vẫn ở 79, dùng nhánh schema trong `eng/deployment/Deploy-WithSchemaMigration.ps1`, sau khi đã có review/backup evidence thật và xác nhận `WritersQuiesced`. Script chỉ gọi Migrator `--schema-only`, xác minh schema rồi mới đổi Web. Bốn cấu hình Data Protection của tài khoản Migrator phải lấy từ cấu hình host đúng, đưa qua environment; không ghi vào lệnh hoặc manifest.
3. Nếu CSDL đã ở đủ 80 và preflight cho phép, dùng quy trình đổi Web không đổi schema. `Deploy-WebOnly.ps1` yêu cầu gói Web-only riêng; không truyền nguyên gói đầy đủ vào nhánh này. Có thể tạo bằng `publish-staging-release.ps1 -WebOnly`, sau đó xác minh manifest riêng.
4. Cập nhật worker/dịch vụ in sang thư mục phiên bản mới theo cấu hình Windows Service đã kiểm tra. Giữ identity, thông tin kết nối, StoreId, certificate/key ring và đường dẫn storage. Script `Install-Service.ps1` trong bộ in dành cho cài mới, không ghi đè service đã tồn tại.
5. Mở Web, xác nhận `/health/live`, `/health/ready`, đăng nhập và quyền cửa hàng. Mở worker theo thứ tự đã thống nhất, kiểm tra backlog/audit/lỗi và trạng thái hóa đơn. Kiểm tra máy in tại chỗ bằng lệnh thử có kiểm soát khi người vận hành sẵn sàng.

### Phục hồi

- Nếu chỉ đổi binary và chưa đổi schema, có thể quay IIS/service về thư mục cũ cùng cấu hình/key ring cũ sau khi đánh giá dữ liệu đã ghi trong cửa sổ đó.
- Nếu đã áp dụng 79→80 hoặc đã có giao dịch mới, không tự chạy `Down`, xóa `AuditOutbox`, xóa khóa, hoặc tự khởi động Web cũ. Quy trình hiện có để GaoApp dừng nếu activation sau schema thất bại; người phụ trách quyết định khôi phục DB và binary thành một bộ nhất quán.
- Migration audit có chốt chặn downgrade khi còn bằng chứng chưa xử lý; phải giữ/xuất bằng chứng và xem xét riêng trước mọi thao tác mất dữ liệu.

## Kiểm chứng và giới hạn

Bằng chứng máy đọc được đặt tại `.artifacts/security-issue13/`; tổng hợp cuối cùng tại `verification.json` và `latest-dotnet-results.json`.

- Build solution, browser runner và 6 công cụ maintenance/migration bằng .NET 10: không lỗi/cảnh báo ở các lần build thành công.
- Kiểm tra chuyển tiếp .NET 8/EF8 → .NET 10/EF10 ở schema 80 không đổi schema/history/catalog; bước 79→80 chỉ có các bổ sung audit đã mô tả.
- 333 test thanh toán đạt; các test đồng thời đã được chạy lại với fixture sửa đúng.
- 41 test CSP/Chrome, audit, khóa/proxy/scope bootstrap đạt.
- Hai test trên bản sao 7 bảng phân quyền từ `GaoAppDb` đạt, SELECT-only ở nguồn.
- Hai smoke test gói Production cuối cùng đạt: schema/bootstrap, bằng chứng audit, HTTPS login, restart/giữ phiên, từ chối cấu hình sai.
- Cả 5 test receipt legacy đạt sau khi fixture mô phỏng rõ `ProductVersion=8.0.29`, không lấy phiên bản EF hiện tại làm lịch sử cũ. Script repair production vẫn chỉ chấp nhận trạng thái EF8 đã biết; không mở rộng whitelist theo runtime mới.
- 16 kiểm tra baseline/runtime, 18 kiểm tra deployment boundary/rollback, 11 kiểm tra package/preflight đạt.
- Client in tự chứa khởi động được, giữ protocol `gao-label-client-v1`, từ chối thiếu token và origin không hợp lệ. Worker publish đọc được key ring .NET 8 trong chế độ `--check-keys` không chạy job/không kết nối SQL.
- Hai exe worker đã khởi động trong console ẩn trên CSDL GUID rỗng: worker hóa đơn chạy timer nhưng không có cửa hàng/job; dịch vụ in không có máy in cấu hình. Không cài Windows Service, không phát hóa đơn hoặc lệnh in thật.

Kết quả regression cuối cùng: **4.749 ca .NET, 4.637 đạt và 112 lỗi; không còn phương thức thiếu kết quả**. Cả 112 ca lỗi đều tái hiện trên assembly .NET 8 trước nâng cấp; không còn ca lỗi chưa đối chiếu hoặc ca đang lỗi trên .NET 10 mà đối chứng .NET 8 đạt. Đây không phải bằng chứng mọi chức năng đều đúng: có lỗi chức năng cần điều tra trong danh sách tồn tại. Không bỏ assertion để làm xanh kết quả.

Bộ JavaScript hiện có **580/588 bài đạt, 8 lỗi** ở fixture bàn giao vận chuyển/tọa độ 3D; các source JS đó không đổi trong phần nâng .NET.

Tiến trình regression đơn được chủ động kết thúc sau hơn hai giờ do chạy chậm và giữ nhiều bộ nhớ; báo cáo TRX ban đầu là lượt bị ngắt, không phải bằng chứng toàn bộ suite hoàn tất hoặc runtime tự crash. Các phương thức chưa có đủ kết quả đã chạy tiếp bằng bốn testhost mới. `verification.json` đối chiếu theo phương thức với 4.741 mục discovery; hai theory được phân rã khi chạy thành nhiều ca hơn, nên tổng thực thi là 4.749. Tổng hợp giữ một lượt đầy đủ cho mỗi phương thức, ưu tiên kết quả mới của fixture đã sửa; không cộng trùng các lượt chạy lại. `fullRegressionComplete=true` chỉ có nghĩa đủ phạm vi kiểm thử, còn `fullRegressionAllPassed=false` và `readyForProductionActivation=false`.

Xem [các nhóm lỗi và phương án xử lý tiếp theo](SECURITY-ISSUE13-REGRESSION-FOLLOWUP-20261023.md). Có cả lỗi chức năng cần điều tra, không chỉ contract test cũ; chưa khuyến nghị kích hoạt production trước khi phân loại/xử lý chúng.

Không deploy/restart Web hoặc worker thật, không cài Hosting Bundle toàn máy, không gọi máy in thật, không gửi hóa đơn/callback ra dịch vụ bên ngoài, không ghi `GaoAppDb`.

Nguồn đối chiếu: [release metadata .NET 10 của Microsoft](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json), [hướng dẫn ASP.NET Core 9→10](https://learn.microsoft.com/en-us/aspnet/core/migration/90-to-100?view=aspnetcore-10.0), [EF Core 9 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes), [EF Core 10 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes), [cảnh báo PKCS đã vá ở 10.0.13](https://github.com/advisories/GHSA-846f-f764-76mx).
