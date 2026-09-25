# GaoStore → GaoApp: toàn bộ hóa đơn bán ra — TEST v1

Phạm vi: `/Admin/Invoice`, chép toàn bộ `InvoiceHead` và `InvoiceDetail` trong snapshot nguồn, không giới hạn ngày hoặc trạng thái. Chưa phê duyệt chạy dữ liệu thật.

## Hợp đồng dữ liệu

- Mỗi InvoiceHead cũ giữ một InvoiceHead mới. Chi tiết nối bằng `InvoiceDetail.OrderID = InvoiceHead.Id`; không dùng cột chuỗi `InvoiceDetail.InvoiceHeadId` làm FK.
- Giữ `LegacySourceId` trên cả hai bảng, unique theo StoreId; ID GaoApp được cấp riêng. Giữ đầy đủ nguyên bản từng dòng nguồn trong `LegacySnapshotJson`, gồm các trường chưa có trong mô hình mới.
- Order chỉ ánh xạ khi cả ID và `Orders.OrderNumber = LEGACY-<ID>` khớp trong cùng cửa hàng. Không tạo Order giả, giao dịch POS, công nợ hoặc phiếu kho để lấp chỗ thiếu.
- Hóa đơn chưa có Order GaoApp được lưu độc lập với OrderId NULL, giữ OrderID cũ trong LegacySourceId và khóa tra cứu. CHECK constraint chỉ cho phép thiếu Order khi có nguồn legacy và LegacyReadOnly=1.
- InvoiceNumber có nội dung → ProviderStatus=Issued, giữ số, ngày phát hành, mẫu/ký hiệu, mã bí mật và mã cơ quan thuế; khóa sửa/phát hành lại. Số rỗng → LocalDraft, không tạo UUID hoặc gọi nhà cung cấp.
- Nhóm 13: theo quyết định người dùng, giữ nguyên nguồn và chỉ tra cứu; không tự tạo hồ sơ điều chỉnh mới. Nhóm 8 nhưng thiếu số hóa đơn cũng chỉ tra cứu để tránh phát hành lại khi chưa rõ kết quả cũ.
- Giữ riêng `LegacyOrderCategoryId` và `LegacyMergeId`; không gộp các Order có chung số hóa đơn thành một head, không nhân lại tổng tiền hóa đơn gộp.
- Giữ số lượng, đơn vị, đơn giá và thành tiền trên từng dòng. Lưu hệ số đơn vị gốc riêng trong `LegacyUnitFactor`; dùng hệ số cho tồn và kiểm tra phát hành, không thay đơn vị hiển thị của hóa đơn.
- Không ánh xạ sản phẩm: vẫn giữ dòng chứng từ dưới dạng manual với ProductVariantId NULL, báo cáo riêng; không tính dòng đó vào tồn. Ánh xạ mâu thuẫn thì dừng. Snapshot TEST này ánh xạ được mọi dòng.
- Không tự tính lại 42 tổng tiền đầu hóa đơn đang khác tổng chi tiết. 2.353 Amount NULL đều là đầu hóa đơn không có chi tiết: các tổng bắt buộc ở GaoApp ghi 0, nguyên bản NULL vẫn được lưu.
- 7 dòng UnitPrice NULL: điền Amount/Quantity làm đơn giá tương thích trường NOT NULL của GaoApp, giữ nguyên Amount và lưu nguyên bản NULL. Snapshot này cả 7 có Quantity=1 và thuộc nhóm điều chỉnh chỉ tra cứu.
- Nguồn không có cột VAT tách riêng: VatRate/VatAmount=0, Amount/TotalAmount giữ nguyên số tiền nguồn. Không suy ra thuế suất mới.
- CreatedAtUtc/IssuedAtUtc chuyển UTC+7 → UTC. InvoiceDate dùng ngày phát hành cho hóa đơn có số, ngày tạo cho bản nháp. Giữ kiểu datetime nguồn trước chuyển đổi để không mất phần lẻ giây SQL Server.
- SQL chỉ ánh xạ cấu hình khi mã số thuế, loại, mẫu và ký hiệu khớp duy nhất; không chép mật khẩu. Web đã được cập nhật theo yêu cầu người dùng: bản nháp GaoStore chưa gửi, không thuộc nhóm chỉ tra cứu, dùng cấu hình Viettel đang hoạt động của cửa hàng ngay cả khi chưa ánh xạ hoặc đang trỏ cấu hình cũ. Xem JSON dùng DTO, không sửa dữ liệu đã nhập. Khi bắt đầu phát hành, lưu cấu hình, MST, loại, mẫu, ký hiệu và UUID cùng trạng thái Issuing trước khi gọi nhà cung cấp. Bản ghi đã gửi/đang chờ kết quả dùng thông tin đã ghi nhận; hóa đơn đã phát hành và nhóm chỉ tra cứu không chuyển sang cấu hình mới. LegacySnapshotJson giữ nguyên.

## Tồn hóa đơn không trừ hai lần

Chạy gói invoice-input-stock trước cho cùng snapshot. Khi đã có dòng supplemental `GSTORE-IIS-V1|X|<InvoiceDetail.ID>`, repository không sinh thêm dòng trừ tồn từ InvoiceDetail vừa chuyển. Dòng supplemental giữ số lượng cũ và được nối tới InvoiceHead GaoApp để mở chứng từ.

Hóa đơn nháp chưa trừ tồn. Nếu sau này một bản nháp được phát hành trong GaoApp và chưa có dòng supplemental tương ứng, tính xuất một lần theo Quantity × LegacyUnitFactor.

Importer kiểm tra từng dòng xuất đã có khớp biến thể, số lượng quy đổi, kho và ngày với hóa đơn nguồn; dừng nếu hai gói dùng khác snapshot. Không xóa supplemental để làm mất lịch sử.

## Cách chạy

Windows Authentication, mặc định `.\SQLEXPRESS`, nguồn `DataGaoStore`, đích `GaoAppDb`, Store/LegalEntity/Warehouse=1. Đích phải là bản TEST đã backup.

Runner hỗ trợ Windows PowerShell 5.1: đường dẫn báo cáo mặc định được tạo trong thân script. Dùng `-CheckSetup` để kiểm tra tải SQL và cấu hình ngoại tuyến, không kết nối database. Mỗi lần chạy cần một thư mục báo cáo mới.

1. Build source GaoApp hiện tại. Áp dụng EF migration `20260923160000_AddLegacyInvoiceImport` bằng quy trình migration ứng dụng, hoặc script tương đương:

       ./Apply-Schema.ps1 -TargetDatabase GaoAppDb -DryRun
       ./Apply-Schema.ps1 -TargetDatabase GaoAppDb

2. Xem kế hoạch, chạy thử và đối chiếu:

       ./Invoke-Migration.ps1 -Mode PREVIEW
       ./Invoke-Migration.ps1 -Mode DRYRUN

3. Ghi TEST sau khi dry run đạt và có backup:

       ./Invoke-Migration.ps1 -Mode COMMIT -AllowCommit
       ./Invoke-Migration.ps1 -Mode VERIFY

4. Build/chạy `Verifier/Verifier.csproj` từ repository đầy đủ để kiểm tra service danh sách/chi tiết và tồn:

       dotnet build ./Verifier/Verifier.csproj
       dotnet ./Verifier/bin/Debug/net8.0/Verifier.dll '.\SQLEXPRESS' GaoAppDb DataGaoStore 1

`InvoiceMigration.sql` tự chạy được trong SSMS với SETTINGS mặc định PREVIEW. `Build-Sql.py` là source sinh SQL để bảo trì; khi chạy migration không cần Python. `Apply-Schema.sql` sinh từ EF migration, phải dùng đúng database đích.

## Chạy lại và bảo vệ dữ liệu

- Nguồn thêm hàng ngày: refresh snapshot TEST, cập nhật danh mục/Order và chạy lại gói tồn trước, sau đó hóa đơn. Khóa legacy ổn định, không chép trùng.
- Nguồn thay đổi: cập nhật bản ghi đã nhập nếu GaoApp chưa sửa bản ghi đó. Hash của toàn bộ trường do importer quản lý phát hiện sửa/xóa/phát hành ở đích và dừng thay vì ghi đè.
- Nguồn mất một bản ghi đã chuyển: dừng để đối chiếu, không tự xóa chứng từ kế toán.
- Không chép hai nguồn GaoStore khác nhau vào cùng namespace StoreId này.
- DRYRUN thực sự ghi rồi rollback. SQL Server có thể tăng identity counter; không reseed để che khoảng trống.
- `Test-Rerun.ps1` chỉ dùng trên TEST đã COMMIT: thêm/sửa nguồn tạm, thử xung đột sửa tại đích, luôn rollback, không ghi bảng nguồn.
- Giữ nguyên các hóa đơn GaoApp sẵn có không mang LegacySourceId. Không gửi email, tải file, đồng bộ hoặc phát hành lên nhà cung cấp trong quá trình chuyển.

## Trước khi chạy dữ liệu thật

Người dùng đã xác nhận 3 SellerCode nguồn thuộc cùng một chủ thể qua các thời kỳ. Gói dùng chung LegalEntityId=1 / WarehouseId=1 theo gói tồn trước; SupplierTaxCode từng hóa đơn vẫn giữ đúng nguồn. Trước khi phát hành thật trên GaoApp, cập nhật thông tin chủ thể demo và cấu hình phát hành phù hợp; không tự thay mã số thuế lịch sử của chứng từ.

Người dùng phải kiểm tra UI TEST. Chuẩn bị snapshot nguồn nhất quán tại giờ chốt và backup đích; cập nhật Order/danh mục/chủ thể; deploy source mới và schema tương ứng. Chạy PREVIEW → DRYRUN → đối chiếu → COMMIT → VERIFY. Đây là chuyển dữ liệu tại cutover, không phải đồng bộ hai ứng dụng cùng bán/phát hành song song.

Gói source đi kèm là các file tham chiếu từ workspace hiện tại, không phải bộ cài/publish độc lập. Không tự chép đè toàn bộ source production.
