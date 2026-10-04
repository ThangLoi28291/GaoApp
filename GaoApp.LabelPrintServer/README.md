# Cài dịch vụ in tem Xprinter trên Windows Server

Ứng dụng web có hai khu riêng: **In tem sản phẩm** (`/admin/label-printing`) cho nhân viên xử lý phiếu và lịch sử; **Cấu hình in tem** (`/admin/label-printing-settings`) cho admin quản lý mẫu và máy in. Dịch vụ này lấy các lệnh đã lưu trong cùng SQL Server và gửi RAW TSPL qua Windows spooler. Client chỉ cần trình duyệt; không cài QZ Tray cho chức năng in tem này.

## Client bấm in, máy in ở server

### Mẫu lớn Siêu thị 50 × 30 mm

Trong **Cấu hình in tem → Mẫu tem**, bấm **+ Mẫu lớn 50 × 30**, kiểm tra số cột/lề/khoảng cách theo cuộn giấy rồi **Lưu mẫu**. Mẫu mới dùng tên đậm, giá lớn tự thu nhỏ để vừa, đơn vị riêng và mã vạch phía dưới; mặc định 1 cột, vẫn cho chọn 2 cột (rộng cuộn mặc định 104 mm). Các mẫu nhỏ đã lưu được giữ nguyên.

Khi cập nhật tính năng này lên server, publish lại **cả GaoApp.Web và GaoApp.LabelPrintServer** vì hai chương trình dùng chung bộ vẽ tem. Dừng dịch vụ in trước khi chép bản mới, giữ cấu hình kết nối/StoreId hiện có, rồi khởi động lại dịch vụ. Không cần migration database. Sau cập nhật, dùng **In thử** để căn lề với cuộn 50 × 30 thực tế trước khi in nhiều tem.

Tham khảo bố cục: [zplCloud Price Tag with Barcode](https://zplcloud.com/en/templates/barcode-price-tag), [Avery 50 × 30 mm](https://www.avery-zweckform.com/vorlage-50x30-p). Bố cục GaoApp được vẽ bằng bộ raster hiện có, dùng chung cho xem trước và TSPL.

### Luồng gửi lệnh

Luồng hiện có: **trình duyệt client → GaoApp.Web → hàng đợi SQL → GaoApp.LabelPrintServer → Windows Spooler → máy in tem trên server**.

Web và dịch vụ in là hai chương trình riêng. Chỉ publish Web hoặc GaoApp.AutoInvoiceWorker sẽ chưa xử lý được hàng đợi in tem. Không cần mở thêm cổng HTTP cho dịch vụ in hoặc cài phần mềm in ở client. Worker kiểm tra hàng đợi mỗi khoảng 2 giây khi không có lệnh đang xử lý.

Đã có profile Visual Studio: nhấp phải **GaoApp.LabelPrintServer → Publish → FolderProfile**. Đích mặc định là `D:\Publish\GaoApp-ServerMoi\LabelPrintServer`, nằm cạnh `Web` và `Worker`. Có thể publish bằng lệnh:

```powershell
dotnet publish .\GaoApp.LabelPrintServer\GaoApp.LabelPrintServer.csproj -c Release -p:PublishProfile=FolderProfile
```

Chép **toàn bộ** thư mục này lên server, ví dụ `D:\GaoApp\LabelPrintServer` (ngoài thư mục Web của IIS). Gói có `README.md`, `Install-Service.ps1` và `appsettings.json` trống cấu hình kết nối. Điền đúng kết nối SQL/StoreId theo hướng dẫn bên dưới. Biến môi trường của IIS application pool không tự truyền sang dịch vụ in.

Trên server, mở PowerShell bằng quyền quản trị:

```powershell
Set-Location 'D:\GaoApp\LabelPrintServer'
notepad .\appsettings.json
# Luu dung ConnectionStrings.DefaultConnection va LabelPrinting.StoreId.
.\GaoApp.LabelPrintServer.exe --list-printers
.\Install-Service.ps1 -InstallDirectory 'D:\GaoApp\LabelPrintServer'
# Dich vu se xu ly cac lenh cu dang cho ngay khi Start.
Start-Service -Name 'GaoApp.LabelPrintServer'
Get-Service -Name 'GaoApp.LabelPrintServer'
```

Script yêu cầu tài khoản Windows chạy dịch vụ, đăng ký khởi động Automatic và phụ thuộc Print Spooler; không tự bắt đầu in. Tài khoản này cần đọc thư mục publish, quyền in trên máy tem, quyền truy cập database và quyền **Log on as a service** theo chính sách server. Kết quả `--list-printers` thuộc tài khoản đang chạy lệnh; cần bảo đảm tài khoản dịch vụ cũng thấy đúng tên máy. Script không cấp thêm quyền SQL hay thay đổi cấu hình IIS.

Sau khi dịch vụ chạy: vào **Cấu hình in tem → Máy in tại server**, chọn tên Windows chính xác, bật **Cho phép in**. **Dịch vụ liên lạc lần cuối** phải cập nhật. Lệnh chờ sẽ chuyển sang **Chờ xác nhận tem** khi Windows nhận lệnh. Chỉ xác nhận số lượng sau khi nhận tem thực tế. Nếu vẫn chờ: đối chiếu cùng database, đúng StoreId, máy đã bật và log của dịch vụ trong Windows Event Viewer → Windows Logs → Application. Trạng thái Running không tự chứng minh kết nối SQL thành công.

Khi cập nhật bản sau, dừng riêng dịch vụ in trước khi thay file; giữ lại cấu hình SQL/StoreId tại server. Không chép `appsettings.json` trống từ package đè cấu hình đang dùng.

## Chuẩn bị

1. Cài driver Xprinter XP-420B trên Windows Server và kết nối USB. Đặt tên riêng cho từng máy trong Windows, ví dụ `Xprinter Kho 1`, `Xprinter Kho 2`.
2. Triển khai Web/Migrator mới, áp dụng migration **20260910040620_AddProductLabelPrinting** bằng quy trình triển khai hiện có. Migration chỉ thêm 4 bảng tem; không sửa số lượng kho/đơn hàng. Migrator cũng seed quyền và menu. Không chạy dịch vụ trước migration.
3. Cấp nhân viên quyền **In tem sản phẩm và quản lý phiếu in** (`system.productlabel.print`); người quản lý thêm quyền **Quản lý mẫu tem và máy in tem** (`system.productlabel.manage`).
4. Chọn một tài khoản Windows riêng để chạy dịch vụ. Tài khoản phải nhìn thấy các máy USB đã cài, có quyền Print, và có quyền kết nối database GaoApp. Với SQL Integrated Security, danh tính kết nối chính là tài khoản dịch vụ, không phải người đang mở Chrome. Không dùng tài khoản quản trị SQL chỉ để in.

## Build / publish

Từ thư mục mã nguồn:

```powershell
dotnet publish GaoApp.LabelPrintServer/GaoApp.LabelPrintServer.csproj -c Release -r win-x64 --self-contained false -o artifacts/label-print-server
```

Cài .NET 8 và ASP.NET Core Runtime 8 x64 trên server (Infrastructure có FrameworkReference ASP.NET Core), hoặc publish self-contained cho Windows x64 khi môi trường cho phép. Đặt thư mục publish ở vị trí cố định, ví dụ `D:\GaoApp\LabelPrintServer`.

Trong **appsettings.json của thư mục publish**, cấu hình:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=TEN_SQL_SERVER;Database=TEN_DATABASE;Integrated Security=True;Encrypt=True"
  },
  "LabelPrinting": { "StoreId": 1 }
}
```

Thay server/database/StoreId bằng đúng cửa hàng triển khai. Cấu hình SQL TLS theo chứng chỉ SQL Server thực tế; không tắt kiểm tra chứng chỉ để xử lý lỗi kết nối. File cấu hình cần chỉ cho tài khoản dịch vụ và quản trị triển khai đọc. StoreId hiển thị trong thuộc tính `data-store-id` của trang in tem; người quản trị cũng có thể lấy từ cấu hình cửa hàng hiện có.

Chạy `GaoApp.LabelPrintServer.exe` bằng tài khoản sẽ chạy dịch vụ để kiểm tra kết nối trước. Dịch vụ không tự tạo schema hoặc migration. Khi chưa có máy cấu hình, dịch vụ chờ.

Dùng `GaoApp.LabelPrintServer.exe --list-printers` để chỉ liệt kê máy in mà tài khoản hiện tại nhìn thấy, không gửi lệnh in.

## Đăng ký Windows Service

Chạy PowerShell bằng quyền quản trị trên **server cần in**, sau khi chỉnh cấu hình và kiểm tra đường dẫn:

```powershell
$labelExe = 'D:\GaoApp\LabelPrintServer\GaoApp.LabelPrintServer.exe'
$labelAccount = Get-Credential -Message 'Tài khoản Windows chạy dịch vụ in tem'
New-Service -Name 'GaoApp.LabelPrintServer' -DisplayName 'GaoApp - In tem sản phẩm' -BinaryPathName ('"' + $labelExe + '"') -StartupType Automatic -Credential $labelAccount
Start-Service -Name 'GaoApp.LabelPrintServer'
```

Cài một instance cho một StoreId. Khi cần nhiều cửa hàng trên một server, dùng thư mục/config và tên Windows Service riêng; giữ máy in được chọn đúng cửa hàng. Không khởi chạy service trên máy client.

## Cấu hình trong GaoApp

1. Admin vào **Cấu hình in tem → Máy in tại server → Tải máy đã cài**. Chọn đúng tên Windows, đặt tên dễ nhận biết. XP-420B thường 203 DPI và vùng in tối đa 108 mm; đối chiếu máy đang dùng.
2. Vào **Cấu hình in tem → Mẫu tem → Tạo mẫu**. Chọn kiểu **Cân đối**, **Giá nổi bật**, **Giá nền đen** hoặc **Khung thanh lịch** từ các ảnh xem trước. Nhập kích thước **từng con tem** và **số cột** riêng. Ví dụ 35×22 có thể chọn 1 hoặc 2 cột; 50×30 cũng có thể chọn 1 hoặc 2 cột nếu tổng chiều rộng vừa máy. Đổi kiểu trình bày giữ nguyên khổ, số cột và các cấu hình khác.
3. Đo khoảng cách giữa các tem, khe giữa hàng và lề cuộn. Phần mềm không suy ra các khoảng này chỉ từ tên khổ giấy. Tổng rộng = lề trái + số cột × rộng tem + (số cột − 1) × cách cột + lề phải.
4. Chọn thông tin hiện/ẩn và mặc định **1 tem** hoặc **theo SL nhập quy đổi về ĐVT gốc**. Barcode tự nhận trên từng sản phẩm: đúng 13 chữ số và số kiểm tra EAN-13 hợp lệ thì in EAN-13; mã còn lại dùng Code 128. Không thêm/bớt ký tự, giữ cả số 0 ở đầu. Lưu mẫu, hoặc **Lưu thành mẫu mới** để giữ nhiều cấu hình riêng.
5. **In thử một hàng** rồi dùng máy quét đọc mã, kiểm tra tiếng Việt, giá, lề và bước cuộn. Nếu lệch, chỉnh dịch ngang/dọc cho máy, khoảng cách tem cho mẫu, kiểm tra chế độ gap sensor và hiệu chỉnh giấy bằng công cụ/driver Xprinter.
6. Phiếu nhập có nút **Đưa vào in tem**, kể cả chưa duyệt. Màn duyệt hỏi thêm sau khi duyệt thành công. Nhân viên mở **In tem sản phẩm → Phiếu chờ in** từ menu riêng, chọn phiếu rồi chọn mẫu/máy admin đã lưu, kiểm tra số lượng, lưu rồi in. Sau khi nhận tem, xác nhận số tem dùng được; in đủ thì chốt hoàn thành. Tab **Lịch sử in** cũng nằm ở khu nhân viên.

Quyền `system.productlabel.print` cho phép in và quản lý phiếu, không cho mở trang cấu hình hoặc sửa mẫu/máy. Quyền `system.productlabel.manage` cho phép cấu hình độc lập; admin muốn thao tác in hoặc in thử cần có thêm quyền Print. Phân quyền được kiểm tra tại server. Hai mục menu được thêm theo quyền khi ứng dụng Development chạy lại hoặc qua bước seed menu của Migrator khi triển khai; dữ liệu mẫu và phiếu hiện có được giữ nguyên.

## Xử lý gián đoạn

Mã trống, ký tự không được Code 128 hỗ trợ hoặc mã quá dài cho chiều rộng tem sẽ báo lỗi trước khi tạo lệnh in. Với mã dài, chọn khổ tem rộng hơn; phần mềm không ép nhỏ vạch đến mức khó quét. Cùng một phiếu/hàng tem có thể in xen kẽ EAN-13 và Code 128.

- **Chờ server in:** dịch vụ chưa chạy, máy đang tắt trong cấu hình, hoặc đang đợi lệnh trước. Chỉ lệnh còn chờ mới hủy được trên web.
- **Đang gửi máy in:** đã giữ quyền gửi; không tự gửi lại nếu dịch vụ bị dừng. Nếu quá 10 phút và dịch vụ đã dừng/lỗi, kiểm tra máy và hàng đợi Windows trước khi xác nhận kết quả thực nhận.
- **Chờ xác nhận tem:** Windows nhận lệnh, chưa chứng minh tem đã ra đủ. Nhập số tem thực nhận; ghi chú khi thiếu/lỗi.
- **Cần xử lý:** kiểm tra USB/driver/giấy/quyền tài khoản. Xác nhận số tem thực nhận (có thể 0), rồi in phần còn thiếu. Không bấm gửi thêm khi chưa kiểm tra lệnh cũ trong Windows.
- In lại cần lý do và không cộng vào tiến độ lần in gốc. Hàng tạm chưa ghép danh mục phải được hoàn thiện trước khi chốt toàn phiếu. Số lượng nhập lẻ không tự làm tròn ra số tem.
- Mỗi lệnh tối đa 10.000 tem, 500 sản phẩm. Hàng đợi lưu SQL nên đóng Chrome không làm mất lệnh; khi server/SQL tắt phải đợi phục hồi mới xử lý tiếp.

## Nguồn và nghiệm thu

- [Xprinter XP-420B: USB, 203 DPI, TSPL, vùng in 108 mm](https://www.xprintertech.com/xp-420b.html)
- [Microsoft: Windows Service với BackgroundService](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service)
- [Microsoft: trạng thái spool không bảo đảm tem đã ra giấy](https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-1)
- Barcode encoder: ZXing.Net 0.16.11 (Apache-2.0); renderer System.Drawing.Common 8.0.29 trên Windows.

Tự động kiểm tra bằng máy chủ/SQL/Chrome thử nghiệm và transport giả không thay thế nghiệm thu USB thật. Trước vận hành, in và quét thử cả hai khổ, số cột khác nhau, số lẻ tem ở hàng cuối, tên tiếng Việt dài, thiếu giấy, rút USB, khởi động lại dịch vụ và hai client gửi đồng thời. Dữ liệu test không được dùng thay thông tin sản phẩm thực.

## Nâng cấp kiểu tem và tự nhận barcode (2026-09-10)

Cập nhật cả ứng dụng Web và dịch vụ GaoApp Label Print Server cùng bản, rồi khởi động lại dịch vụ. Bản nâng cấp này không có migration mới. Mẫu cũ tự dùng nhận dạng barcode cho các lần in mới, không cần tạo lại mẫu. Các lệnh đã xếp hàng trước khi nâng cấp giữ nguyên bố cục và loại mã đã chốt; không sửa hoặc tự gửi lại lịch sử in. Khi rollback, cần giữ renderer mới cho các lệnh đã tạo với kiểu tem mới/AUTO hoặc xử lý dứt điểm chúng trước khi hạ phiên bản.
