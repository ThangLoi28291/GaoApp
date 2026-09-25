# GaoStore → GaoApp: bộ chuyển lần đầu, bước 01–03

Bản ngày 23/09/2026 dùng để kiểm thử trên database riêng. **Chưa phải bộ chạy thật hoàn chỉnh**: phần dọn dữ liệu có chọn lọc và kiểm thử xuyên suốt bước 04–06 còn phải hoàn tất. Không dùng ba ZIP cũ để chạy xen vào chuỗi này.

## Phạm vi đã chốt

- Giữ cấu hình, Store/LegalEntity/Warehouse ID 1, tài khoản, nhân viên và phân quyền GaoApp. Kiểm tra nhân viên bằng cả ID và tên đăng nhập; không chép đè tài khoản từ GaoStore.
- Bước 01: danh mục sản phẩm, quy đổi, barcode; kèm các mã lịch sử không còn danh mục và mã điều chỉnh tài chính như TEST. Các mã bổ sung không hoạt động, không cho bán, không tự tạo tồn.
- Bước 02: khách hàng, tích lũy và voucher theo gói trước. **Không chuyển công nợ cũ.**
- Bước 03: đơn bán Category=1 có tổng không âm, chi tiết đủ điều kiện, thanh toán, ca và thu/chi; trả hàng Category=3 có liên kết đơn gốc. Giữ cách TEST: Completed/Paid, BalanceDue=0, ca lịch sử đóng; trả hàng NoRestock. Bước 03 không tạo bút toán tồn kho/giá vốn.
- Trả hàng không có liên kết đơn gốc lưu riêng trong `LegacyReturnArchives`: nguyên bản JSON header/chi tiết cùng ID nguồn, tên khách/nhân viên, ngày và trạng thái. Không tạo đơn giả, bút toán kho hoặc hoàn tiền từ nhóm này.

Đây là chuyển **một snapshot nguồn đầy đủ**, không phải đồng bộ tăng hằng ngày. Dữ liệu thật tăng mỗi ngày được xử lý bằng backup/snapshot mới trước đợt chạy thật; không cố định số lượng theo TEST. Khi chốt chuyển, phải thống nhất thời điểm ngừng ghi nguồn và dùng cùng snapshot cho toàn bộ các bước. Runner giữ khóa đọc nguồn trong lúc chạy để tránh trộn dữ liệu đang thay đổi.

## Quy tắc đối chiếu bước 03

- Giữ ID đơn và ID dòng nguồn; `OrderNumber=LEGACY-{Order.ID}`. Ca thật dùng ID nguồn; ca thiếu tạo theo ngày địa phương và nhân viên, có mã `LEGACY-SYN-*`.
- Ngày GaoStore UTC+7 chuyển thành UTC, giữ độ chính xác SQL. Ca thiếu ngày chốt dùng thời điểm giao dịch cuối có liên quan.
- Dòng có số lượng dương, giá/thành tiền không âm giữ nguyên số lượng bán và thành tiền. `BaseQuantity=Quantity×Factor`; đơn giá tăng tối thiểu đến hai số lẻ khi cần để giữ thành tiền, phần dư là giảm giá dòng.
- Tên hàng lịch sử thiếu danh mục lấy từ bảng Product nguồn khi chỉ có một tên xác định; nếu nhiều tên thì giữ nhãn theo mã, không tự đoán.
- Thành tiền âm của chi tiết được giữ trong giảm giá cấp đơn. Chênh lệch dương cần cân tổng đầu đơn tạo dòng `LEGACY-ADJUSTMENT` không ảnh hưởng kho.
- Đầu đơn âm được xuất báo cáo và không nạp. Dòng không đủ điều kiện không được biến thành mặt hàng bán; tổng đầu đơn được đối chiếu bằng phương trình tài chính.
- Thu/chi loại bỏ `Chi-TienKhachTraHang` và `Chi-TienChuyenKhoan` để tránh đếm trùng. Số cuối ca lịch sử âm được giữ trong ghi chú/báo cáo, trường hiển thị không âm ghi 0 như TEST.
- `ChiTietThuChi.NgayThang` là DATE. Riêng ca chỉ có thu/chi, không có đơn bán/trả và thiếu ngày chốt, thời gian phiếu được neo vào giờ mở ca, giữ ngày nguồn trong ghi chú như TEST; các phiếu khác giữ ngày nguồn.
- Không đoán khi mã có nhiều quy đổi, thiếu liên kết phiếu trả hoặc nhân viên không khớp; cả bước dừng và rollback.

## Điều kiện chạy

Windows Authentication, hai database trên cùng SQL Server. GaoApp có schema đến `20260923180000_AddLegacyReturnArchive`. Các bảng đích thuộc bước chạy phải rỗng; runner không có chức năng xóa dữ liệu. Cấu hình giữ lại và thiết bị legacy phải sẵn sàng. Chạy trong thời gian không có ứng dụng ghi database đích.

Nếu đích đang đúng schema `20260923160000_AddLegacyInvoiceImport`, dùng `03-return-archive/Apply-Schema.ps1 -TargetDatabase <đích> -DryRun` trước, rồi bỏ `-DryRun` để áp dụng schema. Helper dừng nếu tiền nhiệm khác; không áp dụng mù lên schema mới hơn.

## Cách chạy thử

Đợt diễn tập do người vận hành tự chạy ngày 23/09/2026: `GaoAppDb` đã dọn bằng COMMIT ít log, báo cáo `evidence/reset-lowlog-commit-20260923-190904-769` đạt RESET_COMMIT_PASS, kiểm tra dữ liệu sau commit/identity/FK đều PASS. Chuyển sang PREVIEW bước `01-products`; không chạy reset lại. Đây là xác nhận chuẩn bị đích, chưa phải toàn chuỗi đã chuyển thành công.

Runner đã sửa khởi tạo thư mục báo cáo và khai báo assembly C# cho Windows PowerShell 5.1. Có thể dùng `-CheckSetup` để kiểm tra package/helper mà không kết nối SQL. SQL chuyển bước 01 vẫn giữ SHA-256 `5E7DBD15A137D5552F6C3685BB2234BA5A25680206580C69BCA490D169DD94C9`; thay đổi tương thích PowerShell không đổi quy tắc ánh xạ. Các ZIP cũ chưa được đóng gói lại với sửa đổi này; dùng source trong thư mục hiện tại cho đợt diễn tập.

Chạy tại thư mục này, thay tên database bằng tên bản sao kiểm thử đã chuẩn bị. Không sao chép nguyên ví dụ rồi đổi sang database đang vận hành.

```powershell
$sourceDb = 'DataGaoStore'
$targetDb = 'GaoAppMigrationReview_20260923_v2'
$step = '03-sales'

.\Invoke-Migration.ps1 -Package $step -SourceDatabase $sourceDb -TargetDatabase $targetDb -Mode PREVIEW
.\Invoke-Migration.ps1 -Package $step -SourceDatabase $sourceDb -TargetDatabase $targetDb -Mode DRYRUN
# Chỉ sau khi xem báo cáo DRYRUN thành công:
.\Invoke-Migration.ps1 -Package $step -SourceDatabase $sourceDb -TargetDatabase $targetDb -Mode COMMIT -AllowCommit
.\Invoke-Migration.ps1 -Package $step -SourceDatabase $sourceDb -TargetDatabase $targetDb -Mode VERIFY
```

Thứ tự: `01-products` → `02-customers` → `03-sales` → `03-return-archive`. Mỗi bước có PREVIEW, DRYRUN rollback, COMMIT rõ ràng và VERIFY. Với instance khác, thêm `-Server`.

Mỗi lần chạy sinh `manifest.json` và báo cáo CSV trong `evidence/`. Journal `dbo.GaoStoreMigrationRunsV2` lưu SHA-256 SQL, nguồn và bảng đích. Chạy lại cùng gói/nguồn/đích không tạo trùng. Nếu nguồn, SQL hoặc đích thay đổi, runner dừng để đối chiếu; không tự xóa hoặc ghi đè. VERIFY nên thực hiện ngay sau bước tương ứng, trước các bước sau có chủ ý cập nhật bảng đó.

DRYRUN rollback dữ liệu nhưng SQL Server có thể tăng bộ đếm identity; khoảng trống ID sinh tự động là bình thường. Liên kết phải dùng ánh xạ khóa, không dựa vào ID liên tiếp.

## Tra cứu trả hàng thiếu liên kết

Sau khi triển khai source Web kèm migration, mở `/admin/pos/legacy-returns`, hoặc nút lịch sử trả hàng cũ ở trang đơn POS. Quyền `Pos.Order.View`; lọc theo cửa hàng hiện tại. Trang chỉ có GET để xem/tìm kiếm; không có thao tác phát hành, hoàn tiền hay nhập kho.

Thư mục `Verifier` trong repository kiểm tra truy vấn controller, tìm theo OrderID, phân trang, tham số tìm kiếm và chặn truy cập khác cửa hàng. Không phải phép thử phát hành hay thanh toán thật.

## Còn phải hoàn tất trước chạy thật

1. Công cụ dọn TEST theo `TABLE-PLAN.md`, giữ nguyên cấu hình và xử lý ảnh dùng chung; backup và kiểm chứng khôi phục.
2. Kiểm thử bước 04 kho/giá vốn nối với bước 03 này, rồi bước 05 tồn hóa đơn và bước 06 hóa đơn.
3. Đóng gói chuỗi chạy từ đầu, báo cáo đối chiếu và diễn tập trên snapshot mới của dữ liệu thật. Chỉ chốt chạy thật sau khi kiểm chứng chuỗi hoàn chỉnh.
