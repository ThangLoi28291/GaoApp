# In tem khuyến mãi cho quản lý

## Giấy A4 trên Brother DCP-B7640DW

- Chọn khổ **A4 · 2 cột × 5 hàng · tối đa 10 tem** tại `/admin/promotion-labels`.
- Giấy thường A4 dọc 210 × 297 mm; mỗi tem 90 × 50 mm. Lề trái/phải 10 mm,
  trên/dưới 13,5 mm; cách cột 10 mm và cách hàng 5 mm. Viền xám mảnh để cắt.
- Tem xếp theo hàng: 1–2, 3–4, 5–6, 7–8, 9–10. In 4–5 tem vẫn chỉ dùng
  một tờ; ô dư trắng. Từ tem thứ 11 dùng tờ tiếp theo. Số tem từng sản phẩm
  được lặp đúng theo danh sách, không tự nhân lên thành 10.
- Xem mẫu dùng chính dữ liệu và hình vẽ dùng khi in. Có nút chuyển tờ và
  **Xem mẫu lớn**. Khi chưa chọn sản phẩm, mẫu minh họa gồm 5 tem.
- Chọn đúng tên driver Brother trong danh sách máy client, lưu mẫu/máy in.
  A4 dùng 300 DPI, một mặt và kích thước thực tế; không gửi TSPL vào Brother.
  Lệnh qua driver Windows tại client, không mở hộp thoại trình duyệt.
- **In thử một tờ A4** in 5 tem mẫu để kiểm tra vị trí và máy in trước khi
  in sản phẩm. Điều chỉnh dịch ngang/dọc khi cần, trong khoảng ±5 mm.
- Các mẫu cuộn 50 × 30, 80 × 50 vẫn dùng cơ chế in hiện có. Không có migration,
  menu hoặc phân quyền mới; dùng quyền quản lý in tem và khuyến mãi hiện tại.

### Cập nhật bộ kết nối đang cài

Sau khi publish Web mới, tải lại gói client từ đường dẫn trên màn hình.
Đóng tiến trình **GaoApp.LabelPrintClient** đang chạy trên máy nối Brother.
Giải nén gói mới ra thư mục tạm, chép các file chương trình/README vào thư mục
client đã cài. **Giữ nguyên `appsettings.json` hiện tại** để giữ mã kết nối,
AllowedOrigins và vị trí nhật ký; không chạy lại Configure-Client.ps1 chỉ để
cập nhật chương trình. Mở lại bộ kết nối, Ctrl+F5 trang rồi bấm **Kết nối**.

Client mới báo tính năng `promotion-a4-v1`. Nếu trang còn báo client chưa hỗ
trợ A4, kiểm tra đang mở đúng bản EXE mới. Các lệnh in không rõ kết quả sau
mất kết nối vẫn phải được kiểm tra; không tự gửi lại hay làm mất nhật ký.

Gói client được tạo bằng `scripts/printing/Build-LabelClientPackage.ps1` trước
khi publish Web. Kiểm tra tự động dùng renderer thật và transport giả; không
gửi lệnh tới máy in vật lý.

## Tem cuộn và quy tắc chung

- Đường dẫn: `/admin/promotion-labels`. Có nút mở tại Quản lý khuyến mãi và khu in tem hiện có. Người dùng tự cấu hình menu nếu cần; chức năng không tạo menu hay sửa dữ liệu menu.
- Cần đồng thời quyền `catalog.promotion.manage` và `system.productlabel.manage`, kể cả các API đọc / chuẩn bị in. Tài khoản chỉ có quyền in tem thường không truy cập được.
- Chỉ lấy chương trình **Giảm giá sản phẩm** đang mở, chưa hết hạn, dành cho khách lẻ hoặc tất cả khách. Có thể chuẩn bị tem cho chương trình sắp chạy. Không lấy combo, mua X tặng Y hoặc chương trình chỉ cho khách sỉ.
- Chọn đúng đơn vị, số tem, kiểm tra mẫu 50 × 30 hoặc 80 × 50 mm. Cả hai khổ lưu thành mẫu riêng và chọn trực tiếp trước khi in; khổ lớn phóng chữ, giá và mã vạch theo tỷ lệ. Điều kiện số lượng tối thiểu được in trên tem. Chương trình bị ưu tiên khác tại mức số lượng này bị chặn in.
- Giá sau giảm phần trăm được làm tròn đến 100đ gần nhất, điểm giữa làm tròn lên. Ví dụ 32.145đ → 32.100đ; 32.150đ → 32.200đ. POS online / offline dùng cùng quy tắc trên giá mỗi đơn vị trước khi nhân số lượng. Giảm tiền cố định giữ nguyên cách tính.
- Cài **GaoApp Label Print Client** một lần trên máy Windows đang nối máy in. Gói self-contained tại `GaoApp.Web/ClientPackages/GaoApp.LabelPrintClient-win-x64.zip`, có thể tải trực tiếp qua nút trên màn hoặc `/admin/promotion-labels/client-download` (cùng quyền quản lý). Hướng dẫn tại `GaoApp.LabelPrintClient/README.md`. Không dùng QZ Tray và không mở hộp thoại in trình duyệt.
- Máy và mẫu client lưu trên trình duyệt theo cửa hàng. Có đủ khổ tem, số cột, lề trái/phải, cách cột, khe giữa hàng, hiển thị nội dung, DPI, vùng in, dịch ngang/dọc và cho phép in. Mỗi mẫu gắn máy in trên client. Có thể lấy thông số từ mẫu/máy đang lưu trong quản lý, rồi lưu riêng trên client. Không sửa cấu hình server.
- Xem mẫu sử dụng cùng `ProductLabelRenderer` với lệnh RAW TSPL. Khi đã kết nối, xem trước được vẽ trực tiếp tại client, đúng DPI, số cột và dịch ngang/dọc. Trước kết nối, Web chỉ tạo ảnh xem trước bằng cùng bộ vẽ; Web không gửi máy in. In thử một hàng tạo đúng số tem theo số cột.
- Mã vạch được tự nhận dạng riêng cho từng sản phẩm trên cả khổ 50 × 30 và 80 × 50: ưu tiên EAN-13 khi mã có đúng 13 chữ số ASCII và số kiểm tra hợp lệ; mọi mã còn lại dùng Code 128. Giữ nguyên nội dung và số 0 ở đầu, không thêm số hay sửa số kiểm tra. Mẫu lấy từ quản lý cũng dùng quy tắc tự động này khi xem trước và in tem khuyến mãi.
- EAN-13 vẽ vạch bảo vệ đầu / giữa / cuối dài hơn 5 module; chữ số đầu nằm bên trái và 12 chữ số còn lại chia hai nhóm 6 số dưới vùng vạch tương ứng. Chữ số tự vừa theo độ rộng module ở DPI máy in, không kéo giãn ảnh mã vạch. Khi ẩn chữ số, các vạch bảo vệ vẫn dài hơn và nằm trong vùng tem. Code 128 giữ dãy chữ số liền dưới vạch.
- Trước khi gửi tới client, API kiểm tra lại giá, mã vạch, sản phẩm, thời gian, điều kiện và ưu tiên khuyến mãi. Dữ liệu đổi sẽ yêu cầu cập nhật danh sách. Mỗi lần tối đa 1.000 tem / 500 đơn vị.
- API Web chỉ trả dữ liệu đã kiểm tra, không thêm `ProductLabelJob` hay gửi máy in server. Trình duyệt gửi payload tới `127.0.0.1:17881`; bộ kết nối dùng `WindowsLabelPrinter` trên client, đúng tên máy đã chọn. Mỗi lệnh tối đa 1.000 tem. Chỉ origin GaoApp đã cấu hình với đúng mã kết nối được gọi; không gửi mã kết nối lên Web hay cài SQL tại client.
- Lệnh có request ID và nhật ký bền vững trước khi gửi Windows. Không tự in lại khi mất phản hồi hoặc khởi động lại bộ kết nối; trang giữ lệnh chưa rõ kết quả qua tải lại. Kiểm tra kết quả gửi hoặc kiểm tra giấy rồi xác nhận trước khi tạo lệnh mới. Windows nhận lệnh chưa chứng minh giấy đã ra đủ.
- `scripts/printing/Build-LabelClientPackage.ps1` tạo gói cài chưa có origin/mã kết nối. Publish Web đóng gói đúng ZIP này theo ngoại lệ đường dẫn cụ thể; bộ lọc loại các ZIP khác vẫn giữ nguyên. Không cần cập nhật hay khởi động lại Label Print Server hiện có cho chức năng in client này.
