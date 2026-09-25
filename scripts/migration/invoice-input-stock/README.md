# Chuyển tồn hàng có hóa đơn GaoStore -> GaoApp — v2 (truy vết chứng từ)

Trạng thái: đang rehearsal TEST; chưa phê duyệt chạy dữ liệu thật.

## Bổ sung v2 — OrderID và số hóa đơn nguồn

- Giữ nguyên khóa GSTORE-IIS-V1 để chạy lại không chép trùng; v2 không tạo namespace mới.
- Xuất: LegacyOrderId = InvoiceDetail.OrderID (= InvoiceHead.Id), LegacyInvoiceNumber = InvoiceHead.InvoiceNumber, LegacyInvoiceSymbol = InvoiceHead.InvoiceSeries.
- Nhập: LegacyOrderId = NULL; LegacyInvoiceNumber = Product.InvoiceSeries, LegacyInvoiceSymbol = Product.InvoiceTemplateCode. Tên cột InvoiceSeries bên Product cũ thực tế chứa số hóa đơn.
- Tồn đầu: cả ba trường NULL, nguồn vẫn là SanPhamKhaiThue.SLDauKy.
- Không suy đoán OrderID bán hàng cho nhập, không nối ID cũ sang hóa đơn GaoApp và không coi chứng từ nhập lịch sử là một bản XML đã được nhập vào GaoApp.
- Lưu đúng giá trị nguồn, kể cả khi số hóa đơn chứa ký hiệu khác với trường ký hiệu; không tự sửa hoặc ghép lại số hóa đơn.
- UI hiển thị OrderID/số hóa đơn/ký hiệu; giữ khóa nguồn ở chi tiết để đối chiếu. Tìm kiếm được cả thông tin hiển thị và khóa nguồn.

Trước khi chạy ứng dụng/importer v2, áp dụng EF migration `20260923140000_AddInvoiceStockLegacyDocumentReferences` bằng quy trình migration của ứng dụng; hoặc dùng script tương đương:

    ./Apply-ReferenceSchema.ps1 -TargetDatabase GaoAppDb

Script chỉ thêm ba cột nullable và ghi lịch sử migration; mặc định kết nối Windows Authentication. Sau đó chạy PREVIEW/DRYRUN/COMMIT/VERIFY như bên dưới. Không chạy importer v1 lại sau v2 vì v1 không đồng bộ các trường tham chiếu mới.

## Hợp đồng đã chốt ngày 23/09/2026

- Tồn đầu tại 01/06/2025: SanPhamKhaiThue.SLDauKy, dùng đúng số hiện có.
- Nhập từ 01/06/2025: Product.InvoiceSeries có nội dung, ngày CreatedDate, lượng Warranty. Không fallback Quantity.
- Xuất: InvoiceHead.InvoiceNumber có nội dung, ngày IssuedDate; InvoiceDetail.OrderID = InvoiceHead.Id; lượng InvoiceDetail.Quantity.
- Mọi mã có chứng từ hợp lệ được xét. Không giới hạn danh mục SanPhamKhaiThue.
- Ánh xạ SKU (hệ số 1) hoặc barcode -> ProductUnitConversion.Factor -> ProductVariant.
- Quy về đơn vị gốc trước khi gộp; nhiều ứng viên/hệ số mâu thuẫn thì dừng.
- Không ánh xạ được thì bỏ qua, xuất danh sách đầy đủ. Số âm giữ nguyên.
- Không lấy số tháng cũ để bù chênh. Hóa đơn thiếu chi tiết không tự suy ra lượng xuất; báo số lượng header đó.
- Warranty NULL và Quantity = 0/NULL: không tạo phát sinh. Warranty NULL nhưng Quantity khác 0: dừng để xem dữ liệu mới.
- Thời gian nguồn UTC+7; đích UTC. Tồn đầu ngày 01/06/2025 được lưu 31/05/2025 17:00 UTC.

## Phạm vi đích

Chỉ ghi InvoiceInputStockSupplementalMovements với StoreId đã chọn và tiền tố GSTORE-IIS-V1|.
Giữ riêng từng nguồn: O|SanPhamKhaiThue.Id, I|Product.ID, X|InvoiceDetail.ID.
Các loại: LegacyOpening=1, LegacyInbound=2, LegacyOutbound=4.
PHẢI chạy bản GaoApp có hỗ trợ LegacyOutbound=4; bản cũ bỏ qua các dòng loại 4 khi tính tồn.
Không tạo hóa đơn runtime, phiếu kho vật lý, FIFO, giá vốn hoặc InventoryBalance.
Khi chuyển đầy đủ InvoiceHeads/InvoiceDetails, dùng gói ../invoices cùng source GaoApp mới: repository loại dòng runtime đã có supplemental tương ứng bằng LegacySourceId, giữ tồn chỉ tính một lần và liên kết mở hóa đơn mới. Không dùng importer hóa đơn khác thiếu cơ chế này.

Kho mặc định theo các gói trước: StoreId=1, WarehouseId=1. Tham số được kiểm tra chủ thể/kho/cửa hàng.
Bảng báo cáo kết quả chỉ bao gồm dữ liệu của gói này. Trang GaoApp cộng thêm các phát sinh runtime và supplemental khác hiện có.

## Chạy trên TEST

PowerShell, Windows Authentication:

Runner hỗ trợ Windows PowerShell 5.1: đường dẫn báo cáo mặc định được tạo trong thân script. Có thể dùng `-CheckSetup` để kiểm tra tải SQL và cấu hình ngoại tuyến, không kết nối database. Mỗi lần chạy cần một thư mục báo cáo mới.

    ./Invoke-Migration.ps1 -Mode PREVIEW
    ./Invoke-Migration.ps1 -Mode DRYRUN

PREVIEW chỉ dựng bảng tạm và báo kế hoạch. DRYRUN thực sự insert/update trong transaction, verify từng dòng rồi ROLLBACK.
Rollback giữ nguyên dữ liệu nghiệp vụ; SQL Server có thể tăng identity/rowversion counter. Không reseed để vá khoảng trống ID.

Sau khi đối chiếu dry run, chạy bản ứng dụng có LegacyOutbound=4, chuẩn bị backup/restore point TEST:

    ./Invoke-Migration.ps1 -Mode COMMIT -AllowCommit
    ./Invoke-Migration.ps1 -Mode VERIFY

Sql file cũng chạy được trong SSMS với SETTINGS mặc định PREVIEW. Connection phải trỏ đến đúng database đích.
Mỗi lần chạy lưu CSV đầy đủ: mã bỏ qua, số dư theo biến thể/đơn vị gốc, phát sinh theo biến thể/tháng, kế hoạch ghi, kết quả verify; manifest lưu hash SQL.
VERIFY yêu cầu nguồn và ánh xạ giống lúc COMMIT. Nếu nguồn đã thay đổi, VERIFY báo lệch là đúng; tạo PREVIEW/DRYRUN mới.

## Nguồn tăng mỗi ngày / chạy lại

Không hard-code row count hay ngày cuối tháng 7. Cutoff mặc định là ngày sau ngày chứng từ hợp lệ cuối cùng của snapshot.
Có thể đặt -CutoffExclusive yyyy-MM-dd để lấy chứng từ trước ngày đó. Opening luôn từ 01/06/2025.
Cùng khóa nguồn được update nếu số lượng/ngày/ánh xạ thay đổi; nguồn mới được insert. Không cộng lặp số cũ.
Nếu khóa cũ không còn trong tập hợp đủ điều kiện, báo RetireRows. Mặc định dừng apply; sau khi xem nguyên nhân mới dùng -AllowRetire.
Dòng retired được soft-delete; nếu nguồn quay lại thì phục hồi đúng khóa cũ.
Không chạy snapshot có cutoff cũ hơn phần đã migrate. Tiền tố nhận diện cùng một nguồn logic; không dùng cho hai hệ thống khác nhau.

Khi rehearsal với dữ liệu cũ mới hơn, refresh bản sao nguồn TEST và cập nhật danh mục/ánh xạ bằng các gói trước rồi chạy lại.
Nếu chưa cập nhật danh mục, hàng mới sẽ nằm trong danh sách bị bỏ qua đúng quy tắc; phải xem báo cáo này mỗi lần.

## Kiểm tra UI trước khi chốt

- Dùng cùng StoreId, WarehouseId; so từng ProductVariantId và BaseUnit trong VARIANT_BALANCES.
- Kiểm mã bình thường, mã lốc/túi được đổi đơn vị, mã chỉ có tồn đầu, mã chỉ có xuất, mã âm.
- So kỳ tháng 6/2025, tháng 12/2025 và kỳ cuối snapshot; ngày UTC phải hiển thị đúng UTC+7.
- Kiểm lịch sử từng sản phẩm: tồn đầu + nhập - xuất; ngày và khóa chứng từ trong Note.
- Chạy lại nguồn không đổi: kế hoạch Insert/Update/Retire đều 0.
- Không dùng tổng số lượng cộng mọi đơn vị làm số tồn kinh doanh; đó chỉ là tổng kiểm tra số học.
- Xác nhận thao tác kiểm tra phát hành dùng cùng số khả dụng, gồm các hóa đơn runtime/đang giữ nếu có.

## Chuyển dữ liệu thật sau khi TEST đạt

Không dùng evidence/count TEST làm expected thật. Lấy bản source/snapshot tại giờ chốt và backup đích.
Nguồn đang dùng mỗi ngày không phải bản đóng băng: cần ngừng ghi lúc lấy snapshot/cutover, hoặc dùng bản sao nhất quán.
Gói dùng SERIALIZABLE và khóa cửa hàng; tránh chạy trực tiếp trên nguồn đang phục vụ bán hàng vì có thể chặn ghi.
Cập nhật danh mục/ánh xạ trước; triển khai GaoApp hỗ trợ loại 4; PREVIEW -> DRYRUN -> xem báo cáo -> COMMIT -> VERIFY -> kiểm UI.
Đây là chuyển dữ liệu tại cutover, không phải dịch vụ đồng bộ hai hệ thống đang bán song song.
