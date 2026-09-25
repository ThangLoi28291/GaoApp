# Rehearsal hiện tại — DataGaoStore → GaoAppDb, 23/09/2026

Đây là kết quả của đợt người vận hành tự chạy trên `DESKTOP-E059ENS\SQLEXPRESS`, Windows Authentication. Không nhầm với đợt trước trên `GaoAppMigrationReview_20260923_v2` trong TEST-STATUS.md. Trợ lý chỉ kiểm tra file báo cáo/source và build ngoại tuyến, không tự chạy SQL trong đợt này.

Nguồn `DataGaoStore` hiện tại được người dùng chấp nhận làm snapshot chạy thử. Không khẳng định đó là dữ liệu mới nhất trên máy đang kinh doanh. Phần tồn hóa đơn lấy từ 01/06/2025 đến hết 11/07/2026.

## Đã đạt

Reset đích giữ cấu hình đã COMMIT, sau khi DRYRUN rollback đạt. Các gói bên dưới đều đã PREVIEW, DRYRUN, COMMIT và VERIFY; lượt VERIFY cuối sau khi chuyển hóa đơn cũng đạt.

| Gói | Kết quả chính | Evidence VERIFY cuối |
|---|---|---|
| 01-products | 31.366 sản phẩm chính + 767 mã lịch sử + 1 mã điều chỉnh | initial-import/evidence/20260923-205958-476 |
| 02-customers | 12.651 khách; 11.914 reward ledgers; 3.358 vouchers | initial-import/evidence/20260923-210013-941 |
| 03-sales | 389.661 đơn; 1.399.591 dòng; 385.940 thanh toán; 117 phiếu trả có liên kết | initial-import/evidence/20260923-210014-814 |
| 03-return-archive | 3.568 phiếu trả thiếu liên kết, lưu tra cứu riêng | initial-import/evidence/20260923-210058-856 |
| 04-inventory | 1.512.413 giao dịch kho; 16.496 số dư kho | initial-import/evidence/20260923-210111-279 |
| 05 invoice-input-stock | Đối chiếu chính xác 255.962 dòng; kế hoạch chạy lại 0/0/0 | invoice-input-stock/evidence/20260923-210217-594 |
| 06 invoices | Đối chiếu chính xác 104.427 đầu / 337.169 chi tiết; kế hoạch chạy lại toàn 0 | invoices/evidence/20260923-205132-295 |
| Ảnh sản phẩm | 14.974 MediaAssets; 16.136 ProductImages; 16.117 ảnh chính; hash file và metadata đạt | product-images/evidence/verify-20260923-215521-088 |
| 01-products sau ảnh | VERIFY_PASS; VerifiedImageExtension khớp manifest ảnh đã COMMIT, nguồn và danh mục được bảo toàn ngoài thay đổi ảnh đã ghi nhận | initial-import/evidence/20260923-220234-687 |

Các đường dẫn evidence tính từ `scripts/migration/`.

Người dùng còn chạy Verifier bằng lớp đọc thật của GaoApp và gửi `INVOICE_AND_STOCK_READ_PASS`: danh sách 104.427 hóa đơn, 66.055 đã có số, 38.372 chưa có số; 108 nhóm điều chỉnh chỉ tra cứu; 114 thiếu liên kết đơn hàng. Tồn hóa đơn đọc 255.962 dòng / 10.339 biến thể, không tính trùng xuất và 216.955 dòng xuất lịch sử liên kết được tới hóa đơn mới. Đây là kết quả công cụ lớp đọc, chưa thay thế kiểm tra giao diện.

## Ngoại lệ và phạm vi đã chốt

- Không chuyển công nợ cũ.
- Ba mã số thuế người bán thuộc cùng một chủ thể qua các thời kỳ, dùng LegalEntityId=1; giữ mã nguồn trên hóa đơn.
- Bước 04: 427 dòng nhập không đủ điều kiện ánh xạ, 13 phiếu không còn dòng nhập; 32.674 phân bổ giá vốn tạm còn mở. Tổng lượng cuối chỉ là tổng đối chiếu số học giữa các đơn vị, không phải một lượng hàng có cùng đơn vị.
- Bước 05: bỏ 99 mã chưa ánh xạ, không có dòng xuất bị bỏ vì thiếu ánh xạ; giữ 682 biến thể tồn âm. 121 hóa đơn có số thiếu chi tiết không tự suy ra lượng xuất; 13 dòng Warranty NULL và Quantity NULL/0 không tạo nhập.
- Bước 06: giữ 42 chênh lệch tổng đầu/chi tiết; 2.353 tổng đầu NULL lưu nguyên bản, trường tổng bắt buộc ghi 0; 7 đơn giá NULL được suy ra theo quy tắc đã ghi trong README. Ba dòng nhóm đã phát hành nhưng thiếu số chỉ tra cứu.
- Chưa có cấu hình nhà cung cấp hóa đơn khớp nguồn; việc chuyển lịch sử không đồng nghĩa các bản nháp đã sẵn sàng phát hành.

## Xác nhận giao diện và bàn giao

Người dùng đã xác nhận **PASS** sau khi build/chạy lại trong Visual Studio tại `http://localhost:7051`: ảnh sản phẩm hiển thị và tồn đầu được tách riêng trên giao diện. Trước đó đã xác nhận nhập/xuất/tồn cuối đúng. Source tách Opening khỏi Received/Issued mà giữ nguyên Remaining/Available; Development được cấu hình `Storage:UploadRoot=C:\GaoAppData\Uploads` thay cho giá trị kế thừa `wwwroot/uploads`. 47 kiểm thử liên quan đạt; không chạy lại migration SQL cho hai sửa đổi này. Kết quả UI này áp dụng cho các màn hình/mẫu đã yêu cầu, chưa thay thế việc kiểm tra hóa đơn, đơn hàng, trả hàng và kho vật lý.

1. Ảnh sản phẩm đã được người vận hành DRYRUN, COMMIT và VERIFY thành công từ bộ file `C:\GaoAppData\Uploads\legacy-data`. COMMIT evidence: `product-images/evidence/commit-20260923-215242-768`; VERIFY như bảng trên. Các trường variant ngoài PrimaryProductImageId/RowVersion không đổi. VERIFY bước 01 sau ảnh đã đạt tại `initial-import/evidence/20260923-220234-687`, gồm nhánh biên nhận ảnh. Source Web đã được sửa và kiểm thử HTTP cho legacy-data/images và legacy-data/files; người dùng xác nhận ảnh hiển thị sau chạy bản mới. Bỏ 10.173 dòng thiếu file, 3 đường dẫn sai, 1 mã không ánh xạ; gộp 57 liên kết trùng cùng sản phẩm/file, không sửa bộ file vật lý.
2. Người dùng đã PASS mẫu ảnh, tồn đầu/nhập/xuất/tồn cuối và tiếp tục xác nhận PASS kiểm tra hóa đơn trước yêu cầu đóng gói. Kiểm tra đơn hàng/trả hàng và kho vật lý vẫn cần đối chiếu trên snapshot chốt thật.
3. Hoàn thiện tài liệu và ZIP chạy từ đầu, gồm các sửa lỗi PowerShell 5.1 vừa thực hiện. Không lấy các ZIP cũ làm bản đã cập nhật.
4. Đối chiếu snapshot chốt thực tế và thông tin cấu hình trước cutover thật; chưa kết luận sẵn sàng chạy thật chỉ từ các PASS hiện tại.

Chưa cần chạy lại reset hoặc COMMIT các gói đã đạt. Những lượt VERIFY tiếp theo chỉ cần khi có thay đổi liên quan hoặc phát hiện chênh lệch.

## Cấu hình bàn giao máy WIN

Người dùng xác nhận `WIN-HU6RO2EMIJF\SQLEXPRESS`, Windows Authentication, `DataGaoStore` → `GaoAppDb`, chỉ có ổ C:. Đích có dữ liệu TEST đã chép trước. Gói release dùng backup mặc định SQL, file ảnh tại `C:\GaoAppData\Uploads\legacy-data`. Reset mới cho phép dọn thêm hai bảng biên nhận import TEST khi có cờ rõ ràng và preview/dryrun tương ứng. Script reset này đã đổi hash: evidence reset trên máy DESKTOP không dùng để COMMIT máy WIN. Chưa thực thi SQL trên máy WIN; phải kiểm tra, backup, DRYRUN mới tại đó.
