# Dọn TEST: diễn tập do người vận hành tự chạy

## Sau DRYRUN đạt — COMMIT dọn đích TEST

Người vận hành đã chạy `reset-lowlog-dryrun-20260923-184533-092`: dọn thử, dữ liệu giữ lại, rollback toàn bộ 120 bảng, trạng thái identity và FK đều PASS. Log tại mốc sau truncate/tạo lại FK khoảng 82,2 MiB so với 43,2 MiB trước chạy. Không cấp thêm file log.

`00-Commit-Reset-LowLog.ps1` dùng cùng phần dọn/kiểm tra đã thử, yêu cầu `-AllowCommit` và báo cáo DRYRUN tương ứng. Script kiểm tra source/helper/plan/preview còn đúng hash, cùng backup set, fingerprint từng bảng, identity và metadata FK còn khớp trước DRYRUN. Mọi khác biệt dừng trước thao tác dọn; không tự bỏ qua thay đổi của ứng dụng.

Giữ GaoApp dừng. Lệnh sau **xóa dữ liệu nghiệp vụ TEST thật**, không rollback khi thành công:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\00-Commit-Reset-LowLog.ps1 -PreviewDirectory .\evidence\reset-preview-20260923-145830-707 -SuccessfulDryRunDirectory .\evidence\reset-lowlog-dryrun-20260923-184533-092 -AllowCommit
```

BackupFile được lấy từ DRYRUN thành công trên D, rồi kiểm tra lại bằng HEADERONLY/VERIFYONLY và BackupSetGUID. Không cần nhập lại đường dẫn nếu file vẫn còn nguyên. Sau khi kiểm tra bảng CLEAR rỗng, KEEP/media đúng và FK hợp lệ, script COMMIT; tiếp tục đọc đối chiếu toàn bộ bảng với trạng thái dự kiến, identity sau truncate và định nghĩa FK. Trạng thái cần có `RESET_COMMIT_PASS`.

Giữ nguyên 26 bảng KEEP, dọn 93 bảng CLEAR và 16.193 metadata ảnh được chọn trong snapshot đã thử. Files ảnh vật lý không bị xóa. Identity bảng CLEAR được đặt lại bởi TRUNCATE; identity bảng KEEP được giữ. Muốn quay về dữ liệu trước một COMMIT thành công cần khôi phục backup.

Nếu mất kết nối hoặc lỗi ở COMMIT, không tự chạy lại: manifest phân biệt FAILED_BEFORE_COMMIT, COMMIT_OUTCOME_REQUIRES_REVIEW và COMMITTED_POSTCHECK_FAILED. Các trạng thái sau COMMIT không được hiểu là đã hoàn tác dữ liệu. Đọc báo cáo trước khi làm tiếp. Chạy lại sau COMMIT bình thường cũng bị chặn do dữ liệu không còn khớp baseline cũ.

Đã kiểm tra khởi tạo COMMIT trên Windows PowerShell 5.1 và đối chiếu offline phần truncate/media/FK/kiểm tra với source DRYRUN đã đạt. Trợ lý chưa thực thi COMMIT trên SQL. Bước này chỉ chuẩn bị đích TEST; toàn chuỗi chuyển chưa được xác nhận để chạy production.

**Cập nhật hiện hành: dùng `00-DryRun-Reset-LowLog.ps1`.** Script DELETE cũ và đề xuất cấp log 64 GiB đã bị chặn để tránh chạy nhầm. Các phần mô tả cách cũ bên dưới là lịch sử xử lý, không phải hướng dẫn chạy hiện tại.

## Cách chạy hiện tại — TRUNCATE trong transaction rồi rollback

Giữ GaoApp dừng, dùng lại file backup đã xác minh vì 120 bảng sau lần lỗi đã được đối chiếu khớp. Tại thư mục này:

```powershell
$backupFile = Read-Host 'Nhap duong dan day du file .bak da kiem tra, hien con tren may SQL Server'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\00-DryRun-Reset-LowLog.ps1 -PreviewDirectory .\evidence\reset-preview-20260923-145830-707 -BackupFile $backupFile
```

Runner chỉ có DRYRUN, không COMMIT. Nó đọc metadata đầy đủ của các FK; tháo các FK giữa bảng CLEAR trong transaction, TRUNCATE đúng 93 bảng, dọn metadata ảnh có chọn lọc, tạo lại FK bằng WITH CHECK với đúng tên/cột/thứ tự/ON DELETE/ON UPDATE/NOT FOR REPLICATION. Các bảng KEEP không bị TRUNCATE hoặc đổi FK. Không đặt NULL hàng triệu dòng để tháo vòng. TRUNCATE tạm đặt lại bộ đếm identity của bảng CLEAR; rollback phải phục hồi cả dữ liệu lẫn bộ đếm.

Nếu SQL báo Operating system error 2 tại RESTORE HEADERONLY, script chưa bắt đầu transaction dọn: kiểm tra file đã chuyển/đổi tên thì sửa `$backupFile` sang đường dẫn hiện tại. Nếu không còn backup, chạy `00-Backup-Before-Reset-On-D.sql` trong SSMS, lấy BackupFile từ kết quả cuối `BACKUP_VERIFYONLY_PASS`. Đây là bản backup mới trên D, không phải đề xuất cấp log 64 GiB đã bỏ. Không tiếp tục lấy đường dẫn cũ từ manifest khi file đã chuyển hoặc bị xóa.

Trước rollback, kiểm tra bảng CLEAR rỗng, bảng KEEP và media cần giữ nguyên vẹn, FK khớp và constraint hợp lệ. Sau rollback, so SHA-256 toàn bộ 120 bảng, metadata FK và trạng thái identity với trước chạy. Kết quả cần là `DRYRUN_PASS_ROLLED_BACK`, thêm IdentityStateRestored/ForeignKeyDefinitionsRestored=PASS. Các báo cáo `log-*.json` ghi dung lượng thực tế để đánh giá.

TRUNCATE ghi log theo giải phóng trang, vẫn có log và vẫn rollback được trong SQL Server. Runner không cấp thêm file, không sửa autogrowth; yêu cầu tối thiểu 4 GiB log hiện có còn trống và 8 GiB dung lượng dự phòng trên volume database. Sẽ dừng nếu có indexed view, replication, graph/ledger/temporal, CDC, trigger hoặc metadata FK đặc biệt chưa hỗ trợ. Các ngưỡng dự phòng không phải cam kết dung lượng tối đa.

Đã kiểm tra khởi tạo trên Windows PowerShell 5.1 và kiểm thử offline sinh 193 FK cần tháo/tạo lại từ đồ thị PREVIEW, gồm kiểm tra FK nhiều cột, quote identifier, action, replication và chặn FK bảng KEEP. Các thuộc tính chưa có trong PREVIEW chỉ được giả lập trong test; khi chạy thật runner bắt buộc đọc giá trị từ SQL. Trợ lý chưa thực thi SQL của phương án mới. Chờ người vận hành chạy để xác nhận kết quả thực tế.

Tài liệu cơ chế: https://learn.microsoft.com/en-us/sql/t-sql/statements/truncate-table-transact-sql

Nguồn cố định của đợt thử là `DataGaoStore` hiện tại theo quyết định của người dùng. Các script trong bước này chỉ kết nối đích `GaoAppDb` trên `DESKTOP-E059ENS\SQLEXPRESS`. Không chạy chúng trên database vận hành thật.

PREVIEW ngày 23/09/2026: 26 bảng KEEP, 93 CLEAR, 1 SELECTIVE. Bảng `LegacyReturnArchives` chưa có ở schema 16, nên số CLEAR ít hơn kế hoạch schema 18 một bảng. PREVIEW không phải kết quả kiểm thử xóa.

## 1. Backup

Dừng các tiến trình GaoApp ghi đích. Mở `00-Backup-Before-Reset.sql` trong SSMS, kiểm tra instance rồi tự Execute. Script tạo tên file mới trong thư mục backup người dùng cung cấp, chạy COPY_ONLY/CHECKSUM và RESTORE VERIFYONLY. Lấy đường dẫn ở dòng kết quả `BACKUP_VERIFYONLY_PASS`. VERIFYONLY không thay thế kiểm thử restore đầy đủ.

## 2. DRYRUN

Tại thư mục `scripts/migration/initial-import`, chạy trong Windows PowerShell. Thay `$backupFile` bằng đúng tên file vừa tạo (không chỉ thư mục):

```powershell
$backupFile = 'C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\TEN-FILE-VUA-TAO.bak'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\00-DryRun-Reset.ps1 -PreviewDirectory .\evidence\reset-preview-20260923-145830-707 -BackupFile $backupFile
```

Script kiểm tra backup của đúng database/server, checksum, loại COPY_ONLY full và được tạo sau PREVIEW. Tiếp theo kiểm tra lại schema, FK, trigger, bảng, journal và giữ khóa các bảng đích. Script dừng nếu đã có receipt chuyển dữ liệu; không xóa journal để vượt kiểm tra.

Trong transaction, ba liên kết nullable thuộc các bảng CLEAR được đặt NULL để tháo vòng: POSShifts.CurrentOrderId, InventoryValuationEntries.InventoryCostLayerId và ProductVariant.PrimaryProductImageId. Các bảng được xóa theo quan hệ con trước cha. Không tắt constraint, không truncate, không reseed, không đổi schema.

MediaAssets chỉ chọn metadata đang liên kết ProductImages; giữ bản ghi không liên kết, đường dẫn chưa rõ và tên/path xuất hiện trong cột văn bản của bất kỳ bảng KEEP nào. Kiểm tra tên file có thể giữ dư metadata; không xóa file ảnh vật lý trong uploads. Chức năng chép lại ảnh chưa nằm trong bộ 01–03 đã bàn giao, cần hoàn tất riêng trước kiểm thử toàn chuỗi.

Script kiểm tra CLEAR rỗng, giữ nguyên nội dung mọi bảng KEEP, đúng phần MediaAssets còn lại, rồi kiểm tra constraint. Sau đó luôn ROLLBACK; so SHA-256 gồm mọi cột, NULL, dòng trùng và rowversion của toàn bộ bảng với trước khi chạy. Không có chế độ COMMIT trong script này.

Chờ `DRYRUN_PASS_ROLLED_BACK` và đường dẫn Reports. Các bước hash dữ liệu, DELETE và rollback có thể mất nhiều phút. Giữ cửa sổ mở, giữ ứng dụng GaoApp dừng đến khi kiểm tra xong. Khi có lỗi, dừng chuỗi và gửi manifest; nếu có RollbackError thì cần xác minh trạng thái SQL trước khi làm tiếp.

## Trạng thái bàn giao

Đã kiểm tra khởi tạo trên Windows PowerShell 5.1 và lập thứ tự xóa bằng báo cáo PREVIEW; kiểm thử offline chặn vòng FK mới, liên kết không nullable, thiếu liên kết và thao tác ảnh hưởng KEEP. Chưa thực thi backup/DRYRUN/DELETE trên SQL bởi trợ lý. Chờ người vận hành chạy và gửi kết quả. Chưa có script COMMIT dọn dữ liệu được bàn giao; chưa hướng dẫn chạy bước 01 trên đích còn dữ liệu.

Sau lỗi thiếu tham chiếu System.Data trên Windows PowerShell 5.1, helper được tách vào `Reset-Hash.ps1` và khai báo các assembly rõ ràng cho .NET Framework. `-CheckSetup` nay biên dịch helper trước khi báo thành công. Kiểm thử offline chạy chính helper với bảng trong bộ nhớ (rỗng và dòng trùng); không mở SQL. Nếu sao chép script sang máy khác, phải đi kèm `Reset-Plan.ps1`, `Reset-Hash.ps1`, `TABLE-PLAN.json` và thư mục PREVIEW đã kiểm tra.

## Sau lỗi đầy log ngày 23/09/2026

Lần DRYRUN tại `evidence/reset-dryrun-20260923-151109-074` thất bại với lỗi 9002 ACTIVE_TRANSACTION; chưa đạt kiểm thử dọn. Không chạy lại nguyên cách này trước khi đối chiếu dữ liệu và lập phương án dung lượng log. Chạy `00-Verify-After-Failed-Reset.ps1 -FailedRunDirectory .\evidence\reset-dryrun-20260923-151109-074` để so toàn bộ SHA-256 với `before-hashes.json`, đồng thời lấy dung lượng ổ đĩa và SQL error log liên quan. Script chỉ đọc, giữ khóa chia sẻ lúc so dữ liệu; ứng dụng phải dừng. Kết quả cần có `ALL_TABLES_MATCH_BEFORE_FAILED_DRYRUN`. Nếu khác biệt, báo cáo rõ bảng để điều tra, không tự restore hoặc xóa tiếp.

Kết quả người vận hành chạy `reset-recovery-verify-20260923-182214-324`: 120 bảng khớp, DifferenceCount=0. Nhật ký xác nhận OS error 112 khi tăng `GaoAppDb_log.ldf` trên C. Đây là bằng chứng dữ liệu sau rollback khớp trước lần lỗi; không phải kết quả DRYRUN dọn thành công.

Trước lần thử tiếp, người vận hành chạy `00-Prepare-Rehearsal-Log.sql` trong SSMS. Script chỉ dành cho đích TEST đã kiểm tra: bổ sung log 64 GiB trên D, tối đa 96 GiB/tăng mỗi lần 1 GiB, giữ tối thiểu 20 GiB trên D ngay cả khi tăng hết giới hạn; tắt tự tăng của log C sau khi D cấp phát thành công. File mới đặt trong thư mục rehearsal có sẵn, cần SQL Server được phép ghi và filesystem không nén. Tạo file lớn có thể mất vài phút.

Thay đổi file log là cấu hình database, không tự mất khi DRYRUN rollback. File mới cần được quản lý bởi SQL Server; không xóa bằng Explorer. Sau diễn tập sẽ cần đánh giá kích thước log và cách thu hồi dung lượng riêng. Không mang nguyên đường dẫn TEST sang máy thật. Script này không di chuyển log cũ hay xóa nghiệp vụ.

Runner DRYRUN đã thêm kiểm tra cấu hình D và dung lượng trống trước khi xóa: tối thiểu 64 GiB log đã cấp phát còn dùng được, D đủ phần tăng đến 96 GiB cộng 20 GiB dự phòng, C còn tối thiểu 8 GiB. Đây là ngưỡng dự phòng cho lần thử, chưa phải số đo nhu cầu toàn chuỗi. Các thay đổi mới chỉ được kiểm tra offline bởi trợ lý; người vận hành sẽ chạy SQL và báo kết quả.
