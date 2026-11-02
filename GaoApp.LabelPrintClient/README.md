# GaoApp Label Print Client

Bộ kết nối này chạy trên **máy Windows đang nối máy in**. Tem cuộn dùng RAW TSPL như bộ in tem quản lý; mẫu A4 dùng driver Windows, phù hợp Brother DCP-B7640DW. Không dùng QZ Tray, không kết nối SQL, không xử lý hàng đợi in của server. Khi bấm in trên trang, máy client gửi lệnh trực tiếp tới máy đã chọn; không mở hộp thoại in.

## Cài một lần tại client

Với Brother: chọn mẫu **A4 · 2 cột × 5 hàng · tối đa 10 tem** và đúng tên
driver Brother trên client. A4 dọc, một mặt, tem 90 × 50 mm; xếp theo hàng
1–2, 3–4, 5–6, 7–8, 9–10. In 4–5 tem chỉ dùng một tờ, ô dư trắng.
Mẫu xem trước có chuyển tờ và mở ảnh lớn. Nút **In thử một tờ A4** gửi 5 tem
mẫu. Các mẫu cuộn 50 × 30 và 80 × 50 dùng TSPL như trước.

Khi nâng cấp client đang cài: đóng tiến trình GaoApp.LabelPrintClient, giải
nén gói mới ra thư mục tạm rồi thay các file chương trình trong thư mục cũ.
Giữ nguyên **appsettings.json** và thư mục StateDirectory để không mất mã
kết nối hoặc nhật ký chống in lặp. Mở lại client và kết nối lại trên trang;
không cần chạy lại Configure-Client.ps1 chỉ để nâng cấp. Trang sẽ chặn in A4
nếu client còn là bản cũ.

1. Cài driver máy in: TSPL cho tem cuộn; driver Windows Brother cho A4. Giải nén toàn bộ ZIP vào thư mục cố định thuộc tài khoản Windows của bạn, ví dụ `%LOCALAPPDATA%\GaoApp\LabelPrintClient`.
2. Gói self-contained không cần cài .NET. Nếu dùng gói framework-dependent, cài [ASP.NET Core Runtime 8 x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) trước.
3. Mở PowerShell tại thư mục đã giải nén rồi chạy (thay địa chỉ GaoApp bằng địa chỉ thực tế):

   ```powershell
   .\Configure-Client.ps1 -GaoAppUrl 'https://gao.tenmien.vn' -AutoStart -Start
   ```

   `-AutoStart` tạo shortcut khởi động cùng tài khoản Windows; `-Start` mở bộ kết nối ẩn. Chạy lại script sẽ đổi mã kết nối, cần cập nhật mã trên trang. Không cần quyền quản trị, không mở cổng mạng LAN. Nếu chính sách máy chặn PowerShell, dùng quy trình cho phép script của quản trị máy.
4. Vào `/admin/promotion-labels`, dán **mã kết nối** được script hiển thị, bấm **Kết nối / tải máy in trên client**. Chọn đúng máy, DPI, vùng in, dịch ngang/dọc. Chrome/Edge có thể hỏi quyền truy cập mạng cục bộ lần thiết lập; cho phép trang GaoApp.
5. Chọn khổ mẫu **50 × 30** hoặc **80 × 50**, chỉnh số cột, lề, cách cột và khe giữa hàng đúng cuộn giấy. Khổ 80 × 50 mặc định một cột; chữ, giá và mã vạch lớn theo khổ. Có thể lấy thông số mẫu đang lưu trong quản lý; mẫu client được lưu riêng, không sửa mẫu server. Bấm **Lưu mẫu và máy in trên client**, **Xem mẫu**, rồi **In thử một hàng**. Lưu bản sao để giữ cả hai mẫu, chọn mẫu đã lưu ngay trên phần xem trước rồi in. Kiểm tra giấy và quét mã trước khi in nhiều.

Mã và mẫu client được lưu trên trình duyệt theo cửa hàng; thay trình duyệt/máy cần cấu hình lại. Chỉ trang GaoApp được cấu hình mới được kết nối. Bộ kết nối chỉ lắng nghe `127.0.0.1:17881`, kiểm tra origin và mã kết nối. Không gửi mã kết nối lên server GaoApp.

Mã vạch tự chọn EAN-13 khi đủ 13 chữ số và đúng số kiểm tra; các mã khác dùng Code 128, giữ nguyên mã. EAN-13 có vạch bảo vệ đầu / giữa / cuối dài hơn, chữ số đầu nằm bên trái và hai nhóm 6 số bên dưới như mã trên bao bì. Xem mẫu và in dùng cùng cách vẽ cho cả khổ 50 × 30 và 80 × 50.

## Lệnh chưa rõ kết quả

Thông báo “đã gửi” xác nhận Windows nhận lệnh, không chứng minh tem đã ra giấy. Khi mất phản hồi, trang giữ trạng thái lệnh và chặn gửi thêm, kể cả tải lại trang. Bấm **Kiểm tra kết quả gửi** để đọc kết quả từ client; nếu vẫn chưa rõ, kiểm tra giấy và hàng đợi Windows rồi mới bấm **Đã kiểm tra giấy · cho phép in mới**. Bộ kết nối lưu mã lệnh trước khi gửi Windows và không tự gửi lại lệnh chưa xác định kết quả khi khởi động lại.

## Build gói cho Web

Tại thư mục mã nguồn chạy `scripts/printing/Build-LabelClientPackage.ps1`. Script publish client và tạo ZIP trong `GaoApp.Web/ClientPackages` để Web phục vụ qua endpoint có quyền quản lý. Publish Web sau khi tạo ZIP; không thay cấu hình của Label Print Server hiện có. Khi cập nhật bộ kết nối, đóng riêng tiến trình client, thay các file chương trình, giữ `appsettings.json` đã cấu hình rồi mở lại. Không chạy đồng thời hai bộ kết nối trên cùng client.

Tham khảo: [Microsoft CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors), [Chrome Local Network Access](https://developer.chrome.com/blog/local-network-access).
