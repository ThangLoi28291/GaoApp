# Chuyển GaoStore → GaoApp — 29/09/2026

Máy `WIN-HU6RO2EMIJF\SQLEXPRESS`, Windows Authentication. Nguồn **DataGaoStore**, đích **GaoAppDb chứa nghiệp vụ TEST**, mapping Store/LegalEntity/Warehouse = 1/1/1. Schema yêu cầu `20260928200000_AddStoreReceiptDefault`. Không chạy nâng schema cũ.

Gói chỉ chứa công cụ migration và đọc kiểm tra; không cần chép source Web hoặc publish IIS. Người vận hành tự chạy **từng lệnh**, xem kết quả rồi mới tiếp tục. Không chạy cả tài liệu một lần. Xem `QUY-TAC-CHUYEN.md` và `TABLE-PLAN.csv` trước dọn.

## 1. Cập nhật công cụ, giữ lịch sử

Giải nén ZIP vào thư mục mới `C:\GaoMigration-20260929`, có Run-Step.ps1 ngay trong đó. Giữ nguyên thư mục cũ. Chạy đoạn sao chép này một lần trên thư mục mới chưa sử dụng:

```powershell
Copy-Item -LiteralPath 'C:\GaoMigration\config.json' -Destination 'C:\GaoMigration-20260929\config.json'
Copy-Item -LiteralPath 'C:\GaoMigration\runs' -Destination 'C:\GaoMigration-20260929\runs' -Recurse
Set-Location 'C:\GaoMigration-20260929'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Verify-Package.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step Inspect
```

Kỳ vọng PACKAGE_FILES_PASS và INSPECTION_FINISHED_READ_ONLY. Kiểm tra đúng server/database/schema/dung lượng. State cũ ResetCommitted=false được giữ, nhưng phê duyệt cũ không áp dụng cho gói mới. Nếu đã reset thì dừng để xác minh; không xóa state hoặc đổi RunName.

## 2. Chốt nguồn và preview

Tắt phát hành tự động trong UI GaoApp, xử lý xong giao dịch đang chạy rồi dừng Web và worker Windows Service/job. Chặn POS/tích hợp ghi đích. Ngừng ghi GaoStore và các job nguồn từ lúc chốt đến hết chuỗi. Kiểm tra cờ/heartbeat trong script không thay thế việc dừng tất cả tiến trình ghi. Cần .NET 8 và ASP.NET Core 8 runtime cho ReadVerify.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ResetPreview
```

Kỳ vọng PREVIEW_READY_FOR_REVIEW: kế hoạch 128 bảng (27 KEEP, 100 CLEAR, 1 SELECTIVE). Xem tables.csv, foreign-keys.csv và manifest. Số dòng thực tế lấy từ báo cáo; TestRows trong JSON chỉ là tham khảo cũ. Bảng lạ/schema khác/quan hệ không an toàn/worker bật sẽ chặn. Preview chưa xóa dữ liệu. Chạy lại preview sẽ hủy phê duyệt backup và DRYRUN trước đó.

## 3. Backup mới sau preview; kiểm tra riêng

BackupDirectory trong config.json là thư mục server đã tồn tại và SQL Service được phép ghi, ví dụ `C:\Data`. Để trống dùng thư mục mặc định SQL. Chạy từng lệnh:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step BackupSource
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step VerifySourceBackup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step BackupTarget
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step VerifyTargetBackup
```

Backup phải báo BACKUP_CREATED_UNVERIFIED; Verify phải báo BACKUP_VERIFYONLY_PASS. Tên file GUID riêng, COPY_ONLY + CHECKSUM, không ghi đè backup cũ. SQL Express không dùng COMPRESSION. Có thông báo tiến độ SQL/thời gian chờ; điều này không cam kết đã chữa nguyên nhân máy lag. Không chạy nhiều backup song song.

Nếu đã có backup đúng được tạo **sau preview hiện tại**, có thể dùng VerifySourceBackup hoặc VerifyTargetBackup kèm `-BackupFile 'C:\...\ten-file.bak'`. Script kiểm tra đúng DB/server, một bộ full copy-only có checksum. Backup ngày 24/09 không thay cho backup trước lần dọn mới. VERIFYONLY không thay diễn tập restore; nên có bản sao ngoài ổ C khi có điều kiện. Script không shrink log hoặc xóa backup.

## 4. Dọn thử rồi dọn thật TEST ở GaoAppDb

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ResetDryRun
```

Phải đạt DRYRUN_PASS_ROLLED_BACK: TRUNCATE trong transaction, khôi phục FK, so sánh bảng giữ, rollback và kiểm tra toàn bộ nội dung/identity/FK. Backup đích được đọc kiểm tra lại ở mỗi bước dọn, có thể tốn thời gian. Đừng tắt SQL hoặc cửa sổ khi rollback.

Sau khi xem báo cáo và xác nhận CLEAR chỉ chứa TEST:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ResetCommit -AllowCommit
```

RESET_COMMIT_PASS nghĩa là đã **xóa thật phần TEST ở GaoAppDb**. Không xóa nguồn hoặc file ảnh vật lý. Giữ nội dung KEEP; không tự loại kho thứ hai/nhân viên khác. Reset yêu cầu tối thiểu 4 GiB trống trong log và 8 GiB trống trên ổ database; đây không phải cam kết toàn chuỗi chỉ cần 8 GiB.

## 5. Chép theo thứ tự

| Bước | Nội dung |
|---|---|
| 01-products | Sản phẩm/biến thể/category/unit/quy đổi/supplier/mapping |
| 02-customers | Khách, điểm, voucher |
| 03-sales | Đơn/dòng/thanh toán/ca/trả có liên kết; route theo LayHD |
| 03-return-archive | Trả không liên kết, lưu tra cứu |
| 04-inventory | Phiếu nhập/kho vật lý/giao dịch/giá vốn |
| 05-invoice-stock | Tồn đầu SLDauKy, nhập/xuất từ 01/06/2025 đến nguồn mới nhất |
| 06-invoices | Toàn bộ hóa đơn và chi tiết, giữ trạng thái và chứng từ nguồn |

Mỗi bước chạy đủ bốn lệnh **từng lệnh một**:

```powershell
$package = '01-products'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step $package -Mode PREVIEW
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step $package -Mode DRYRUN
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step $package -Mode COMMIT -AllowCommit
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step $package -Mode VERIFY
```

Sau VERIFY_PASS, đổi `$package` sang bước tiếp theo. 01–04: PREVIEW_PASS_ROLLED_BACK → DRYRUN_PASS_ROLLED_BACK → COMMIT_PASS → VERIFY_PASS. 05–06: PREVIEW_ONLY → DRYRUN_ROLLED_BACK với EXACT_VERIFY_PASS → COMMIT_PASS → VERIFY_PASS. Không chạy song song hoặc mở Web.

Xem các CSV mã bỏ qua/chứng từ thiếu liên kết/chênh lệch tiền/tồn âm trước COMMIT. Số liệu TEST cũ không phải số kỳ vọng trên nguồn thật. Bước 05 tự lấy cutoff, không giữ mốc tháng 7. Không chuyển công nợ cũ.

LayHD=1 → route thủ công (2); còn lại → tự động (1). Đơn không có InvoiceHead giữ chưa chọn (0). Hóa đơn không có đơn hợp lệ giữ tra cứu theo luật cũ. Phân luồng không phát hành/API; hóa đơn đã phát hành và điều chỉnh chỉ đọc không đổi trạng thái. Giữ thông tin khách cũ, không xóa MST để ép đủ điều kiện tự động.

## 6. Ảnh và kiểm tra cuối

Ảnh mới nhất ở `C:\GaoAppData\Uploads\legacy-data`, giữ đúng thư mục con. Không thay ảnh giữa các bước. Chạy lần lượt:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ImagePreview
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step Images -Mode DRYRUN
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step Images -Mode COMMIT -AllowCommit
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step Images -Mode VERIFY
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step VerifyAll
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Step.ps1 -Step ReadVerify
```

Phải đạt IMAGE_VERIFY_PASS, FINAL_CHAIN_VERIFY_PASS, INVOICE_AND_STOCK_READ_PASS, ARCHIVE_READ_VERIFIED. ReadVerify đối chiếu thêm route/LayHD, không gọi API. Nếu MediaAssets giữ lại làm importer chặn, dừng đối chiếu, không xóa ảnh cấu hình để ép chạy.

Mở bản Web hiện có đúng DB/UploadRoot sau khi đạt kiểm tra, vẫn để tự động tắt. Đối chiếu ảnh; tồn đầu/nhập/xuất/cuối; OrderID/InvoiceNumber; hóa đơn phát hành/nháp; trả hàng; route thủ công/tự động. Chỉ mở bán sau nghiệm thu. Trước bật worker, duyệt tháng/phạm vi và cấu hình hóa đơn thật. Phân luồng không tự sửa thiếu đơn vị, tồn, thông tin khách.

## Khi lỗi và giới hạn kiểm tra

Giữ log/runs, dừng đúng bước lỗi. Không reset lại, sửa state hoặc đổi RunName để bỏ khóa. Nếu COMMIT xong mà mất kết nối/không ghi state, phải kiểm tra SQL thực tế trước khi tiếp tục. Nguồn đổi giữa chuỗi thì không ghép hai thời điểm. Khi cần quay lại, xác minh backup đích đúng lần chuyển trước restore; không restore đè DataGaoStore. Sau phát sinh giao dịch thật, không dọn/import lại từ đầu.

Gói mới kiểm tra offline, chưa chạy SQL trên server. Chuỗi cũ đã diễn tập PASS nhưng schema và dữ liệu mới cần PREVIEW/DRYRUN/VERIFY mới. PACKAGE_FILES_PASS chỉ là tính toàn vẹn file.
