# A1 — Build thật; RUN BLOCKED

Ngày 02/11/2026. Nhãn: **SOURCE VERIFIED**, **MEASURED** cho build/Node; **NOT TESTED** cho Web runtime/browser/SQL. A1 tổng thể **BLOCKED**, không phải PASS.

## Quyết định chạy

Launch profiles `http`, `https`, `IIS Express`, `WSL` đều khai báo Development. **DEV chỉ là tên hosting environment; độ an toàn DB/integration vẫn UNKNOWN.** Không có override DB test được thấy trong launch profile. Không khởi động hoặc truy cập một Web đang chạy sẵn chỉ vì có URL local.

| Kiểm tra | Evidence | Kết luận |
|---|---|---|
| Cấu hình DB | appsettings base/Development có kết nối; Development là local candidate nhưng không có chứng cứ DB test riêng | Không truy vấn DB hoặc công bố tên/connection string. Local không đủ chứng minh an toàn. |
| Effective config | user-secrets Development tồn tại; nội dung không được in; không có relevant ambient override được thấy theo tên đã kiểm | Chưa xác minh giá trị hợp nhất và nguồn dữ liệu. |
| Migration startup | Development config `Database:ApplyMigrationsOnStartup=true`; `Program.cs:399–409` gọi validation rồi `ApplyDevelopmentSchemaAsync`; extension kiểm tra flag rồi `MigrateAsync` | Chạy profile mặc định có nguy cơ đổi schema. Không chạy. |
| Worker | `Program.cs:185–213`; notification setup đăng ký worker | ACB callback, storefront bank/invoice, media, delivery/audit outbox, notifications cần cô lập. |
| Default gate | `StorefrontInvoiceWorker.cs:16`; `DeliveryOutboxWorker.cs:13` | Các gate tương ứng default true; không coi thiếu key là đã tắt. |
| Provider | Infrastructure DI đăng ký Viettel issue/preview/lookup/email; Web đăng ký ACB; notification transport tồn tại | Chưa chứng minh sandbox/không có dữ liệu hàng đợi thật. |
| Recovery/menu | `Program.cs:58–81,340–397` có recovery flag gọi AdminMenuSeeder | Không dùng recovery/provisioning để chạy UI; không tạo menu. |

Chi tiết đã che: [run-safety-redacted.txt](evidence/run-safety-redacted.txt). Không phát request business, phát hành hóa đơn, thanh toán, gửi tin, callback hoặc probe vào SQL. Không bật/tắt Windows Service/IIS/job.

## Các lệnh đã chạy và kết quả

Lệnh chạy tại repo root, trong terminal riêng. Ba biến chỉ đặt trong tiến trình shell build: `MSBuildEnableWorkloadResolver=false`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`. Không sửa global.json, appsettings, user-secrets hoặc config.toml.

| Lệnh | Exit / kết quả | Bằng chứng |
|---|---|---|
| `dotnet --info` | 1; in được SDK/runtime, nhưng workload inspection bị từ chối quyền mở Service Control Manager | Không gọi thao tác service hay cài runtime; [host-resources](evidence/host-resources.txt) ghi giới hạn. |
| `dotnet build GaoApp.Web/GaoApp.Web.csproj --no-restore -p:UseSharedCompilation=false -m:1 -nr:false -v:minimal` | 1; 30 warning, 6 error MSB3027/MSB3021, DLL đích bị khóa sau 10 retry | [build-web.txt](evidence/build-web.txt). Không dừng tiến trình giữ file. |
| `dotnet build GaoApp.Web/GaoApp.Web.csproj --no-restore --output E:/GaoApp/GAOAPP-SA-20260819-01/.artifacts/uxui-large-scale-pilot-v2-20261102/web -p:UseSharedCompilation=false -m:1 -nr:false -v:minimal` | **0; BUILD PASS, 0 warning, 0 error**, elapsed 75,14 s | [build-web-isolated.txt](evidence/build-web-isolated.txt). Working tree thật, Debug/net10.0; không phải full solution/Release/deployment build. |
| `dotnet restore tools/GaoApp.UiPreview/GaoApp.UiPreview.csproj --source E:/GaoApp/GAOAPP-SA-20260819-01/docs/uxui/large-scale-pilot/evidence -p:NuGetAudit=false -v:minimal` | 0; chỉ restore project preview không có PackageReference, nguồn local | [restore-preview.txt](evidence/restore-preview.txt). Không tải/cài dependency mới. |
| `dotnet build tools/GaoApp.UiPreview/GaoApp.UiPreview.csproj --no-restore -p:UseSharedCompilation=false -m:1 -nr:false -v:minimal` | **1; FAIL**, 3 CS0246, 0 warning | [build-preview.txt](evidence/build-preview.txt): `AdminNavigationProvider.cs` thiếu type `MobilePrimaryTab` trong graph compile của tool. Không sửa tool/source. |
| `node --test --test-concurrency=1 GaoApp.Tests/Ui/pos-barcode-races.test.cjs GaoApp.Tests/Ui/pos-action-locks.test.cjs GaoApp.Tests/Ui/pos-checkout-feedback.test.cjs` | **0; 33/33 pass, 0 fail/skip**, 364,0705 ms runner duration | [node-focused-tests.txt](evidence/node-focused-tests.txt). Chỉ VM/mock, không HTTP/SQL/thiết bị. |

Không chạy `dotnet test`: các full-app fixture hiện có tự migration/seed/tạo tài khoản/quầy và một số seed menu, không thuộc quyền dữ liệu của lượt này. Không đổi fixture để bỏ assertion hoặc bỏ safety gate. Số test .NET thực thi trong pilot: **0**; không báo đã kiểm thử nghiệp vụ .NET.

UiPreview chỉ compile các Razor/model foundation và fixture menu đã có; không map business controllers. Ngay cả khi build thành công, nó cũng không chứng minh P1/P2 chạy trên DB test. Vì build lỗi nên không start preview, không dùng binary cũ hay dựng ứng dụng thay thế. Không có URL localhost thực tế từ console để bàn giao.

## Điều kiện mở chặn (kế hoạch, chưa thực hiện)

1. Chủ dự án chỉ định DB test riêng đã có hoặc duyệt một task tạo DB tổng hợp riêng; xác nhận provenance không sao chép dữ liệu thật. Cấp tài khoản demo có sẵn theo role; cấu hình bí mật ngoài báo cáo.
2. Đối chiếu effective config, schema compatibility và nguồn settings. Migration/seed/bootstrap phải bị chặn trong profile dùng cho pilot. Không chạy Migrator để vượt lỗi schema khi chưa được duyệt.
3. Xác minh từng worker/transport của **toàn graph Web hiện tại**, kể cả storefront/notification/audit/media; dữ liệu queue demo, provider giả/sandbox, chặn outbound thật và thiết bị in. Chỉ tắt chúng trong môi trường test riêng được chấp thuận; không đổi service production.
4. Kiểm tra GET/login/heartbeat/bootstrap có mutation metadata/draft hay không. Nếu cần ghi vào DB test để vận hành fixture, trình phạm vi đó riêng; phê duyệt đọc DB không tự bao gồm seed/giao dịch.
5. Build/run cùng source đã ghi hash, đầu ra riêng. Lấy URL từ console thực tế; một browser profile mới, một phiên, nhịp bình thường. Xác minh role/quyền/Store/terminal, rồi mới chụp BEFORE/đo A4.

Chưa cung cấp lệnh `dotnet run` copy-paste với profile hiện tại vì các tiền điều kiện chưa thỏa. Không yêu cầu người dùng mở Codex bằng PowerShell. Chủ dự án có thể chuẩn bị profile qua VS/VS Code; pilot vẫn chỉ chạy khi có bằng chứng an toàn.
