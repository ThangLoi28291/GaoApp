# Chuyển GaoStore sang GaoApp trên WIN-HU6RO2EMIJF\SQLEXPRESS

Gói ngày 23/09/2026. Chạy bằng Windows PowerShell 5.1, Windows Authentication, ngay trên máy SQL. Nguồn `DataGaoStore`; đích `GaoAppDb` hiện chứa dữ liệu TEST. StoreId / LegalEntityId / WarehouseId = 1. Chỉ sử dụng ổ C:.

Bạn tự chạy từng lệnh bên dưới. Mỗi lệnh phải hoàn tất và đạt trạng thái yêu cầu trước khi chạy lệnh tiếp theo. Không chạy nguyên cả tài liệu một lần. Nếu lỗi, giữ cửa sổ và thư mục `runs`, gửi toàn bộ lỗi; không tự dọn rồi chép lại từ đầu.

## 1. Chuẩn bị bộ file và chốt thời điểm chuyển

- Giải nén ZIP MIGRATION vào `C:\GaoMigration`. File `Run-Step.ps1` phải nằm ngay trong thư mục này, không lồng thêm một cấp.
- Giải nén ZIP SOURCE vào `C:\GaoApp` để có `C:\GaoApp\GaoApp.sln`. Đây là source đầy đủ ứng dụng hiện tại, có sửa hiển thị tồn đầu, ảnh và đọc hóa đơn lịch sử. Source không chứa backup database, khóa mã hóa hoặc mật khẩu triển khai. Không ghi đè cấu hình/khóa của ứng dụng đang triển khai.
- ZIP IMAGES có thư mục gốc `legacy-data`; giải nén vào `C:\GaoAppData\Uploads`. Kết quả phải là `C:\GaoAppData\Uploads\legacy-data\images` và `...\files`. Đây là bộ 14.974 file đã chạy thử; nếu dữ liệu thật có thêm ảnh, bổ sung bộ ảnh mới nhất của GaoStore vào đúng cấu trúc trước ImagePreview. Không xóa bộ ảnh gốc trên máy cũ.
- Cần .NET 8 SDK để build source; công cụ ReadVerify cần cả .NET 8 và ASP.NET Core 8 runtime (SDK có kèm). Kiểm tra bằng `dotnet --info`. SQL login Windows đang dùng phải có quyền backup và sửa schema/dữ liệu đích, đọc dữ liệu nguồn.
- Dừng GaoApp, worker/job ghi vào GaoAppDb. Đến lúc chuyển thật, ngừng ghi GaoStore và các job liên quan trong toàn bộ chuỗi chuyển. Có thể chạy diễn tập từ một bản restore cố định; nhưng không gọi bản đó là dữ liệu chốt nếu GaoStore thật vẫn phát sinh. Không chạy từng phần trên nguồn đang thay đổi mỗi ngày.
- Giữ bản source/config/khóa DataProtection của ứng dụng đang dùng để có thể quay lại. Nếu mở bằng Visual Studio, kiểm tra cả User Secrets và biến môi trường có ghi đè connection string hoặc UploadRoot không.
- Chỉ có ổ C: không phải trở ngại, nhưng phải đủ chỗ cho backup hai database, ZIP và file giải nén, data/log và phần tăng trưởng trong lúc import. Gói không tự tăng log lên 64 GB, không shrink, không xóa file backup/ảnh. Bước dọn yêu cầu ít nhất 4 GiB trống bên trong log đã cấp phát và 8 GiB trống trên ổ chứa database; đây là ngưỡng chặn bước dọn, không phải cam kết toàn chuỗi chỉ cần 8 GiB.

Mở Windows PowerShell tại thư mục gói:

```powershell
Set-Location 'C:\GaoMigration'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Verify-Package.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step Inspect
```

Kỳ vọng `PACKAGE_FILES_PASS`, `INSPECTION_FINISHED_READ_ONLY`. Inspect chỉ đọc SQL. Xem `runs\real-01\Inspect-...\report-*.csv`: đúng máy, hai database ONLINE, dung lượng ổ C:, schema và số dòng. Gửi kết quả Inspect để đối chiếu trước khi chuyển sang dọn. Nếu thiếu schema/bảng hoặc thiếu dung lượng, dừng để xử lý đúng nguyên nhân; không bỏ các kiểm tra.

`config.json` đã đặt đúng máy mới. `BackupDirectory` để trống nghĩa là dùng thư mục backup mặc định do SQL Server trả về; thư mục này phải tồn tại và SQL Service phải có quyền ghi. Nếu cần đổi, đặt đường dẫn thư mục C: đã tạo/cấp quyền trong config. Không dán đường dẫn `.bak` vào `Read-Host`.

## 2. Backup nguồn; kiểm tra schema đích

Sau khi chốt nguồn:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step BackupSource
```

Kỳ vọng `BACKUP_VERIFYONLY_PASS`; đường dẫn `.bak` nằm trong manifest của bước. File có tên duy nhất; không ghi đè backup cũ. VERIFYONLY kiểm tra bộ backup đọc được, không thay thế một lần diễn tập restore thực tế. Nên có thêm bản sao backup ở nơi lưu trữ khác ngoài ổ C: máy này.

Schema mới nhất dự kiến: `20260923180000_AddLegacyReturnArchive` (xem report-05.csv của Inspect). Nếu đúng, **bỏ qua ba lệnh dưới**. Nếu mới nhất là `20260923160000_AddLegacyInvoiceImport`, chạy lần lượt:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step BackupTarget
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ArchiveSchema -Mode DRYRUN
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ArchiveSchema -Mode COMMIT -AllowCommit
```

Kỳ vọng `SCHEMA_DRYRUN_ROLLED_BACK`, rồi `SCHEMA_APPLIED`. Nếu schema khác hai mốc trên, dừng và gửi report; không tự chạy toàn bộ Migrator/Seed hoặc các script nâng cấp cũ. Backup đích dùng cho dọn bên dưới vẫn phải tạo mới **sau ResetPreview**.

## 3. Dọn dữ liệu TEST của GaoAppDb

Được giữ: cấu hình cửa hàng, pháp danh, kho, menu, quyền, nhân viên và các bảng KEEP theo kế hoạch. Giữ nguyên ID/cấu hình hiện có, không tự đổi tên hoặc xóa kho thứ hai nếu đang có. Dữ liệu kinh doanh TEST và biên nhận import TEST sẽ được dọn; phải xóa biên nhận cũ cùng dữ liệu để chạy lại từ đầu. Không chuyển công nợ cũ. Không xóa file ảnh vật lý.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ResetPreview
```

Kỳ vọng `PREVIEW_READY_FOR_REVIEW`. Mở `tables.csv`/`tables.json` trong thư mục kết quả; xác nhận chỉ dữ liệu TEST thuộc CLEAR, các bảng cần giữ thuộc KEEP. MediaAssets là SELECTIVE, giữ ảnh còn được cấu hình tham chiếu. Danh sách bảng lạ hoặc quan hệ với bảng giữ lại sẽ chặn để xem xét. Số bảng có thể khác lần TEST vì thêm kho lưu trả hàng và hai bảng biên nhận.

Tiếp theo, từng lệnh:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step BackupTarget
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ResetDryRun
```

Phải đạt `BACKUP_VERIFYONLY_PASS`, rồi `DRYRUN_PASS_ROLLED_BACK`. DRYRUN có thử thay đổi dữ liệu trong transaction, kiểm tra giữ nguyên các bảng KEEP và rollback, sau đó kiểm tra toàn bộ nội dung/identity/khóa ngoại được phục hồi. Để cửa sổ chạy đến khi kết thúc; không tắt khi đang rollback.

Chỉ khi DRYRUN đạt và đúng đích TEST:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ResetCommit -AllowCommit
```

Phải đạt `RESET_COMMIT_PASS`. Lệnh này **dọn thật dữ liệu TEST trong GaoAppDb**. DataGaoStore không bị dọn. Trình chạy khóa việc lặp lại ResetCommit trong cùng lần chuyển. Không sửa state.json để vượt kiểm tra. Nếu có lỗi sau khi COMMIT, kiểm tra trạng thái thực tế trước khi quyết định chạy tiếp.

## 4. Chép nghiệp vụ theo thứ tự

Thứ tự bắt buộc:

| Bước | Nội dung |
|---|---|
| 01-products | Sản phẩm, biến thể, nhóm, đơn vị/quy đổi, nhà cung cấp, mã lịch sử |
| 02-customers | Khách hàng và lịch sử điểm/voucher theo quy tắc đã chốt |
| 03-sales | Đơn bán, chi tiết, thanh toán, ca bán, trả hàng có liên kết; không chuyển công nợ cũ |
| 03-return-archive | Trả hàng thiếu đơn mua, chỉ lưu lịch sử tra cứu |
| 04-inventory | Phiếu nhập, giao dịch kho, giá vốn và tồn kho vật lý |
| 05-invoice-stock | Tồn đầu từ SanPhamKhaiThue.SLDauKy, nhập/xuất hóa đơn từ 01/06/2025, quy đơn vị gốc |
| 06-invoices | Toàn bộ InvoiceHead/InvoiceDetail, cả đã phát hành và chưa; giữ trạng thái/nguồn lịch sử |

Với **mỗi bước**, đặt `$package` theo bảng, rồi chạy bốn lệnh bên dưới **từng lệnh một**, xem kết quả rồi mới sang lệnh sau. Ví dụ bắt đầu sản phẩm:

```powershell
$package = '01-products'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step $package -Mode PREVIEW
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step $package -Mode DRYRUN
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step $package -Mode COMMIT -AllowCommit
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step $package -Mode VERIFY
```

Sau VERIFY_PASS, đổi `$package` lần lượt thành `'02-customers'`, `'03-sales'`, `'03-return-archive'`, `'04-inventory'`, `'05-invoice-stock'`, `'06-invoices'` và lặp bốn lệnh. Không chạy song song, không mở Web trong lúc chép.

- Gói 01–04: `PREVIEW_PASS_ROLLED_BACK` → `DRYRUN_PASS_ROLLED_BACK` → `COMMIT_PASS` → `VERIFY_PASS`.
- Gói 05–06: `PREVIEW_ONLY` → `DRYRUN_ROLLED_BACK` với `EXACT_VERIFY_PASS` → `COMMIT_PASS` → `VERIFY_PASS`; kế hoạch khi VERIFY phải không còn thêm/sửa/xóa ngoài dự kiến.
- Mỗi bước có thư mục kết quả riêng dưới `runs\real-01`. Số lượng thật có thể lớn hơn TEST, không ép về số lượng cũ. Kiểm tra các CSV mã bỏ qua, phiếu thiếu liên kết, chênh lệch tiền/tồn âm trước COMMIT.
- Bước 05 không cố định cutoff tháng 7; script tự lấy mốc từ dữ liệu nguồn. Xem dòng CONTRACT để chắc ngày bắt đầu và mốc kết thúc đúng snapshot hiện tại.
- Mã không ánh xạ bỏ qua và có báo cáo; không tự tạo nhập/xuất thiếu chứng từ. Tồn âm giữ theo dữ liệu tính được. Đơn trả thiếu liên kết và nhóm điều chỉnh hóa đơn lịch sử chỉ tra cứu. Ba MST nguồn cùng một pháp danh, vẫn giữ thông tin nguồn.
- Cảnh báo chất lượng không tự mất đi khi VERIFY đạt: kiểm tra có đúng ngoại lệ đã chấp nhận không. 682 mã âm, 99 mã bỏ, 114 hóa đơn thiếu liên kết, 42 chênh lệch tiền là **số liệu TEST tham khảo**, không phải giới hạn cho dữ liệu thật.

## 5. Liên kết ảnh

Đặt đúng bộ ảnh mới nhất vào `C:\GaoAppData\Uploads\legacy-data` trước khi chạy. Không đổi file ảnh giữa PREVIEW, DRYRUN, COMMIT và VERIFY.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ImagePreview
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step Images -Mode DRYRUN
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step Images -Mode COMMIT -AllowCommit
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step Images -Mode VERIFY
```

Kỳ vọng `IMAGE_PREVIEW_READY_FOR_REVIEW`, `IMAGE_DRYRUN_PASS_ROLLED_BACK`, `IMAGE_COMMIT_PASS`, `IMAGE_VERIFY_PASS`. Xem số file thiếu và đường dẫn không hỗ trợ ở PREVIEW. Nếu còn MediaAssets được cấu hình sử dụng và importer chặn vì đích chưa rỗng, dừng để đối chiếu; không xóa các ảnh cấu hình giữ lại để ép qua kiểm tra.

## 6. Kiểm tra toàn chuỗi trước mở ứng dụng

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step VerifyAll
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ReadVerify
```

Kỳ vọng `FINAL_CHAIN_VERIFY_PASS`, `INVOICE_AND_STOCK_READ_PASS`, `ARCHIVE_READ_VERIFIED`. ReadVerify dùng lớp đọc thật của GaoApp, không khởi động Web, không gọi API phát hành, không ghi SQL. Nếu dữ liệu nguồn có cấu trúc nghiệp vụ mới làm kiểm tra không đạt, dừng để xem nguyên nhân, không bỏ qua.

## 7. Chạy bản Web đúng source và đối chiếu giao diện

Trong ZIP SOURCE, Development đã đặt máy SQL mới và `Storage:UploadRoot=C:\GaoAppData\Uploads`. Mở `C:\GaoApp\GaoApp.sln` bằng Visual Studio, chọn GaoApp.Web, profile cổng 7051, build và chạy sau khi VerifyAll/ReadVerify đạt. Không chạy nhầm bản publish/source cũ. Cấu hình Production là mẫu cần ghép với cấu hình host thật, giữ khóa DataProtection và tài khoản tích hợp đang có; không dùng Development làm cấu hình công khai.

Kiểm tra tối thiểu:

1. `/Admin/Product`: tìm vài mã có ảnh và mã quy đổi; ảnh hiện và giá/đơn vị đúng. Thư viện ảnh phải tải được ảnh, không chỉ có số đếm.
2. `/admin/invoice-input-stock`: tồn đầu riêng; tồn cuối = tồn đầu + nhập − xuất. Mở chứng từ để tra OrderID/InvoiceNumber; kiểm tra một mã từng tháng từ 06/2025 và các tháng mới nhất.
3. `/Admin/Invoice`: lọc đã phát hành/chưa phát hành, tổng danh sách khớp báo cáo. Mở hóa đơn có số, bản nháp, hóa đơn thiếu Order và điều chỉnh tra cứu. Tồn hóa đơn không bị trừ hai lần.
4. Đơn bán, trả hàng liên kết, mục lưu lịch sử trả hàng, kho vật lý: đối chiếu chứng từ mẫu và tổng với snapshot nguồn.
5. Kiểm tra đúng cửa hàng/pháp danh/kho; chỉnh tên/cấu hình thật khi sẵn sàng. Không hiểu các bản nháp lịch sử là đã đủ điều kiện phát hành: cần cấu hình nhà cung cấp hóa đơn phù hợp riêng.

Chỉ mở ghi nhận giao dịch mới khi các đối chiếu đạt. Sau khi ứng dụng phát sinh dữ liệu mới, việc VERIFY nguồn-vs-đích theo snapshot hoặc chạy lại import từ đầu không còn phù hợp; không dọn lại đích.

## Khi lỗi hoặc cần quay lại

- Gửi bước đang chạy, toàn bộ thông báo và thư mục evidence tương ứng. Không chỉ gửi chữ PASS của bước khác.
- Các COMMIT đã đạt vẫn được giữ. Trình chạy lưu tiến độ; tiếp tục đúng bước sau khi xác định lỗi, không đổi RunName để vượt trạng thái.
- Nếu cần hủy cutover: dừng GaoApp, dùng SSMS khôi phục **GaoAppDb** từ backup đích của chính lần chuyển này, kiểm tra đúng file/server/database trước thao tác ghi đè. Không restore đè DataGaoStore. Nếu schema đã nâng trước reset, backup trước nâng schema là mốc quay lại xa hơn.
- Nếu nguồn đã thay đổi trong giữa chuỗi, dừng; cần chốt lại phương án snapshot và làm lại trên đích chuẩn bị phù hợp, không ghép các bước của hai thời điểm.

## Phạm vi đã kiểm tra của gói

Chuỗi SQL nghiệp vụ đã được người vận hành chạy PREVIEW/DRYRUN/COMMIT/VERIFY trên snapshot TEST, gồm ảnh và lớp đọc. Người dùng đã xác nhận PASS sau sửa ảnh, tồn đầu và kiểm tra hóa đơn trên Web. File `REHEARSAL-CURRENT-20260923.md` là số liệu đối chiếu lịch sử, không phải evidence cho máy WIN.

Lớp trình chạy mới, cấu hình chỉ ổ C:, kiểm tra gói và phần dọn thêm biên nhận TEST đã được kiểm tra offline/PowerShell 5.1. **Chưa chạy SQL trên WIN-HU6RO2EMIJF**. Vì vậy bắt buộc tạo PREVIEW, backup và DRYRUN mới tại máy đó; tuyệt đối không dùng evidence/backup-path của máy DESKTOP để phê duyệt dọn máy WIN.
