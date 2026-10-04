# In nhanh sản phẩm

Trang `/admin/label-printing` mở tab **In nhanh sản phẩm** mặc định. Hai tab còn lại là **In theo phiếu nhập** và **Lịch sử in**. Link có `task` hoặc `receipt` vẫn mở đúng luồng theo phiếu.

## Nhân viên thao tác

1. Quét mã rồi Enter hoặc tìm tên, alias, SKU. Mã vạch chính khớp duy nhất được thêm thẳng, mặc định một tem; quét lại tăng số tem.
2. Chọn đơn vị tại kết quả tìm kiếm rồi thêm vào danh sách. Có thể in nhiều đơn vị của cùng một sản phẩm. Ảnh, giá bán lẻ và mã vạch lấy từ danh mục.
3. Điều chỉnh số tem. **Cập nhật giá** tải lại dữ liệu cho toàn bộ danh sách mà giữ số lượng.
4. Bấm nút mẫu, kiểm tra popup thống kê và tên máy, rồi xác nhận một lần để gửi in. **Xem trước mẫu** hiển thị tối đa năm đơn vị đầu. Máy in được admin gắn sẵn, nhân viên không chọn lại và không cần chọn lại máy in.
5. Xem tiến độ, hủy lệnh chưa gửi hoặc xác nhận số tem thực nhận trong **Lịch sử in**. Giá trị tồn kho và trạng thái phiếu nhập không bị thay đổi.

Lối tắt **In tem** trong danh sách sản phẩm mở sẵn kết quả cho sản phẩm đó; nếu chỉ có một biến thể, tự thêm đơn vị gốc. Sau khi lưu giá bán trên phiếu nhập, **In tem giá mới** mở tab mới với các đơn vị vừa lưu. Các nút chỉ hiện khi có quyền `System.ProductLabel.Print`.

## Quy tắc dữ liệu

- API tìm kiếm, cập nhật giá, gửi lệnh và xác nhận đều kiểm tra quyền in và cửa hàng. Không yêu cầu quyền chỉnh danh mục cho nhân viên in.
- Chỉ lấy sản phẩm, biến thể, đơn vị và quy đổi đang hoạt động. Đơn vị thiếu mã vạch hợp lệ hoặc giá lớn hơn 0 bị chặn. Tìm kiếm trả tối đa 60 đơn vị; nhập cụ thể hơn nếu cần.
- Giá lẻ ưu tiên giá quy đổi, sau đó giá biến thể, rồi giá Product, cùng thứ tự hiện dùng tại POS. Không nhận giá/mã vạch do client tự khai trong lệnh in nhanh.
- Lệnh in kiểm tra dấu phiên bản và thông tin sản phẩm lúc gửi. Giá, tên hoặc đơn vị đổi sau khi chọn sẽ yêu cầu cập nhật trước khi in.
- Tối đa 500 đơn vị, tổng 10.000 tem/lệnh. Xác nhận dùng cặp biến thể–đơn vị để tách số tem thực nhận của chai/lốc/thùng.
- In nhanh dùng job không gắn phiếu, lưu `UnitId` trong payload JSON; lịch sử phân biệt in nhanh với in thử. Payload cũ không có UnitId vẫn đọc được.
- Khi mất phản hồi gửi lệnh, danh sách được khóa; **Thử lại lệnh trước** dùng cùng RequestId và nội dung. Không tự gửi lại bằng mã mới. Nếu tải lại trang, kiểm tra lịch sử trước khi gửi thêm.

## Triển khai và kiểm thử

### Gắn mẫu với máy in và popup phiếu nhập

- Tại `/admin/label-printing-settings`, thêm máy trong tab **Máy in tại server**, sau đó mở từng mẫu, chọn **Máy in gắn với mẫu**, bật **Hiển thị nút in cho nhân viên** và lưu. Mẫu cũ chưa gắn máy sẽ hiện hướng dẫn và không cho gửi in. Cùng một máy có thể dùng nhiều mẫu; nhân viên cần lắp đúng cuộn giấy.
- Cấu hình máy nằm trong JSON của mẫu (`printerId`, `showPrintButton`), không thêm bảng/cột. Server kiểm tra máy cùng cửa hàng, khổ giấy, máy bật và máy khớp mẫu; phiên bản mẫu đổi sẽ chặn yêu cầu cũ.
- Phiếu mới dùng số lượng mặc định của mẫu khả dụng đầu tiên trong danh sách; nếu chưa có mẫu thì mặc định theo SL nhập. Mẫu mới mặc định `received`, hỗ trợ `one` và `custom`. Sau khi nhân viên chỉnh số lượng, bấm mẫu nào cũng giữ số lượng đó. Mỗi lần mở/tải lại phiếu đều bỏ chọn toàn bộ sản phẩm; đổi cách tính số tem không tự chọn dòng.
- Phiếu dùng **Tem lần này**, hỗ trợ Theo SL nhập / Mỗi SP 1 tem / Tự nhập. Chỉ dòng được chọn mới gửi in. Giao diện mới dùng `ProductProgress = true`; server tự xác định lần đầu hay in lại theo từng dòng, kể cả một lệnh có cả hai loại. In lại không bắt nhập lý do và không cộng tiến độ.
- Khi duyệt phiếu, tùy chọn đưa vào danh sách in tem vẫn do người dùng quyết định. Nút **In tem phiếu này** mở phần chọn sản phẩm trên phiếu đã lưu, gồm cả phiếu đã duyệt. Mở popup chỉ đọc; task chỉ tạo khi in hoặc đưa vào chờ. Bấm mẫu mới mở popup xác nhận máy/số tem; Để sau hoặc Esc không ảnh hưởng kết quả duyệt.
- Trong lúc gửi hoặc mất phản hồi, khóa chỉnh sửa và đóng popup; **Thử lại lệnh trước** dùng nguyên RequestId/nội dung cũ. Không tự gửi sang mẫu hoặc máy khác. Server trả lại job cũ ngay cả khi mẫu vừa đổi sau lần nhận lệnh.

Publish Web và LabelPrintServer cùng phiên bản; giữ cấu hình SQL/StoreId/máy in. Dừng dịch vụ in khi chép gói mới rồi khởi động lại. Không cần migration database.

Kiểm thử SQL bao gồm quyền, CSRF, tách cửa hàng, giá thay đổi, nhiều đơn vị, lặp yêu cầu và xác nhận từng đơn vị; kiểm tra không đổi tồn kho. Kiểm thử Chrome dùng dữ liệu tạm, thử trường hợp server nhận lệnh nhưng client mất phản hồi. Không gửi tới máy in thật.

## Nâng cấp tiến độ theo sản phẩm (30/09/2026)

- Tên phiếu nổi bật, mã phiếu và nhà cung cấp bên dưới. Tìm không dấu theo tên/mã/NCC, có phân trang và bộ lọc Cần xử lý, Đang gửi in, Hoàn tất, Cần kiểm tra, Tất cả. Thống kê tính trên kết quả tìm kiếm, trước bộ lọc trạng thái.
- Tiến độ = (sản phẩm đã xử lý + sản phẩm bỏ qua) / tổng sản phẩm hiện có. Một lần gửi 2 tem cho sản phẩm nhập 48 đơn vị vẫn hoàn tất sản phẩm đó. Sản phẩm cũ đã có `Printed > 0` được hiểu là đã xử lý; không sửa/xóa lịch sử cũ.
- `Sent` (giá trị enum 6) nghĩa là transport Windows đã nhận lệnh, không đảm bảo giấy đã ra. Dispatcher lưu kết quả và cập nhật tiến độ trong cùng transaction, khóa task rồi printer. Lệnh chờ/đang gửi/lỗi không tăng tiến độ và không tự gửi lại khi chưa rõ kết quả. Lệnh legacy và in nhanh giữ quy trình xác nhận thực nhận cũ.
- Khi lệnh mới cần kiểm tra do lỗi, nhân viên đối chiếu số tem dùng được trong lịch sử. Dòng có số thực nhận > 0 được xử lý xong; dòng 0 vẫn còn lại. Không cho xác nhận lại một lệnh đã kết thúc.
- In lại sản phẩm đã xử lý hoặc đã bỏ qua không đổi tiến độ, trạng thái hay thời điểm hoàn tất. Số tem và giá tại thời điểm gửi vẫn lưu trong payload bất biến của từng lệnh.
- Bỏ qua từng dòng đã chọn hoặc toàn bộ phần còn lại: chọn lý do, Khác bắt buộc nhập ghi chú. Lịch sử nằm trong `Actions` của từng dòng JSON, gồm RequestId, nhân viên, thời điểm, lý do và tên sản phẩm tại thời điểm thao tác. Khôi phục chỉ áp dụng dòng đã bỏ qua, mở lại tiến độ và giữ lịch sử. Refresh giữ trạng thái/lịch sử của sản phẩm còn hoặc đã rời phiếu.
- Không mặc định tick sản phẩm. Có nút Chọn chưa xử lý / Chọn tất cả / Bỏ chọn. Phiếu hoàn tất vẫn mở được từ bộ lọc Hoàn tất hoặc Tất cả để in lại.
- Popup trước gửi hiển thị tên phiếu, máy in Windows và tên dễ nhớ, mẫu/khổ tem, số dòng/tổng tem, số dòng lần đầu/in lại, chi tiết tên/giá/số tem. Esc hoặc Quay lại không tạo job. In nhanh cũng có một popup xác nhận trước gửi.
- Ảnh có xem phóng to khi trỏ chuột hoặc bấm. Trạng thái job tự cập nhật; dữ liệu nhập/chọn không bị xóa bởi lần thăm dò trạng thái.

Không thêm migration trong nâng cấp này: tận dụng JSON dòng/payload đã có và thêm enum status. **Cần publish cả Web và GaoApp.LabelPrintServer cùng phiên bản**, dừng dịch vụ in khi thay gói và giữ cấu hình SQL/StoreId. Không chạy đồng thời worker cũ và mới. Chưa kiểm chứng tem giấy/máy in vật lý bằng bản nâng cấp này.

Kiểm thử bổ sung: `ProductLabelSqlServerTests.Product_progress_*` và browser `--label-progress` (SQL tạm, transport giả). Ảnh kiểm tra tại `TestResults/label-progress/`.

Phần in nhanh khóa ô quét khi đang tải cấu hình để Enter không gửi biểu mẫu trước khi JavaScript sẵn sàng. Việc theo dõi phiếu dùng API trạng thái nhỏ; chỉ tải lại chi tiết và thống kê khi phiên bản hoặc trạng thái lệnh thay đổi.


## Bổ sung barcode phù hợp trước khi in (30/09/2026)

- Cả in nhanh và in theo phiếu gọi `POST barcodes/check` theo mẫu/máy đã chọn. Kiểm tra bằng cùng renderer và DPI dùng để in, chỉ xét các sản phẩm/đơn vị được chọn; mã đã vừa tem không thay đổi.
- Popup liệt kê ảnh, tên, đơn vị, mã hiện tại, mã đề nghị. Ưu tiên barcode đang hoạt động của đúng đơn vị đã in vừa; nếu chưa có, sinh EAN-13 bằng `Ean13Helper` hiện hành, kiểm tra trùng toàn cửa hàng. Không sửa chuỗi mã cũ hoặc chuyển mã giữa đơn vị.
- `POST barcodes/prepare` yêu cầu token của bản kiểm tra; kiểm tra lại phiên bản mẫu, máy, giá/sản phẩm, các alias và task trước khi lưu. Dùng quyền in tem, CSRF, lọc cửa hàng, transaction serializable và khóa cấp mã; index unique bảo vệ xung đột với các luồng danh mục khác. Hai xác nhận cùng snapshot chỉ một lần được áp dụng; yêu cầu cũ trả 409, kiểm tra lại sẽ dùng mã đã lưu.
- Chỉ bỏ IsPrimary của mã cũ, giữ IsActive; mã đích thành mặc định. Không gọi luồng ChangeBarcodeAsync vì luồng đó ngưng mã cũ. Ghi lịch sử ProductVariantBarcodeHistory gồm mã cũ/mới, nhân viên, thời gian, lý do. Nhân viên có quyền in chỉ được xác nhận phương án do server tính, không nhập barcode hoặc chọn đơn vị ngoài phạm vi phiếu.
- Cập nhật barcode trong snapshot phiếu và SourceHash cùng transaction, giữ nguyên tiến độ, trạng thái bỏ qua/hoàn tất, số lượng và payload lịch sử. Phiếu khác đang dùng barcode cũ sẽ yêu cầu cập nhật từ nguồn theo cơ chế sẵn có. In nhanh nhận lại fingerprint mới và giữ số lượng đã nhập.
- Hủy popup mã: không đổi barcode. Đồng ý cập nhật mã rồi hủy popup in: mã mặc định vẫn được lưu, không tạo job, không cộng tiến độ. Popup thông báo rõ hành vi này. Khi in tiếp bằng cùng mẫu, mã đã phù hợp không được tạo lại.
- Không thêm migration. Với riêng nâng cấp barcode này cần publish Web (bao gồm static assets); nếu server chưa có bản tiến độ sản phẩm nêu trên thì cập nhật cả LabelPrintServer đồng bộ.
- Kiểm thử: `ProductLabelSqlServerTests.Barcode_*` (SQL tạm) và `--label-barcodes` (Chrome, transport giả), gồm hủy, tái sử dụng/tạo, hai yêu cầu cùng lúc, phân quyền/CSRF/cửa hàng, dữ liệu thay đổi, đơn vị quy đổi, giữ mã cũ, tiến độ và số lượng. Không in trên máy thật.
