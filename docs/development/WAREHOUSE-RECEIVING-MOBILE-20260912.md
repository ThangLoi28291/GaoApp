# Nhập hàng trên điện thoại — 12/09/2026

Áp dụng cho `/admin/warehouse-receiving/{id}` ở phiếu Đang nhập/Bị trả về.

Cập nhật 13/09/2026: đã bỏ giao diện lịch sử khỏi phiếu nhập trên desktop/mobile; lịch sử quét ở POS giữ nguyên. Build và hai probe `RECEIVING_FEEDBACK_RELOAD_CHECK` / `RECEIVING_FEEDBACK_CHECK` pass. Log: `Logs/receiving-remove-history-build.log`, `Logs/receiving-remove-history-browser.log`, `Logs/receiving-remove-history-feedback.log`. Đã xem ảnh desktop và điện thoại 320 px; không chạy Run All, chưa publish.

## Nhân viên sử dụng

- **Quét camera**: cấp quyền camera, hướng camera sau về mã vạch. Camera dừng ngay sau khi nhận một mã. Kiểm tra sản phẩm, đơn vị nhận và số lượng; bấm **Thêm dòng** hoặc **Thêm & quét tiếp**. Đèn chỉ hiện khi camera hỗ trợ.
- **Nói tên hàng**: đọc tên tiếng Việt; chữ nhận diện và kết quả tìm được hiện ra để nhân viên chọn. Không tự thêm hàng bằng giọng nói.
- **Ô tìm sản phẩm bên dưới**: dùng chung trên điện thoại và máy tính, hỗ trợ máy quét dạng bàn phím và tên có dấu/không dấu. Ví dụ `sua tuoi`, `khong duong`, `dau nanh`. Hai nút phía trên chỉ còn Quét camera và Nói tên hàng.
- **Mã chưa có**: bấm **Ghi nhận hàng chưa có**, khai báo tên, đơn vị, quy đổi và số lượng. Có thể chụp ảnh bao bì. Khai báo chờ quản lý duyệt theo quyền hiện có.
- **Ảnh bao bì đã nhận**: xem ảnh đính kèm tại phiếu. Màn duyệt khai báo cũng hiển thị ảnh. Tối đa một ảnh hiện hành cho mỗi dòng khai báo; ảnh mới thay ảnh cũ khi cộng thêm cùng dòng hàng chưa hoàn thiện.
- Nhãn trạng thái cho biết đang lưu, đã lưu hoặc cần thử lại. Khi không nhận được xác nhận từ máy chủ, giữ nguyên số lượng và bấm lưu lại. Nếu đóng popup, nút **Kiểm tra / lưu lại thay đổi** mở lại nội dung đang chờ.

## Xác nhận lượt nhận

- Thẻ xác nhận nằm ngay dưới ô tìm sản phẩm: ảnh, tên, barcode, đơn vị, quy đổi, lượng vừa cộng và lượng trong phiếu. Thẻ giữ thông tin tới lượt nhận tiếp theo. Chạm ảnh mới mở ảnh lớn; không có popup báo thành công tự bật và không phát âm thanh.
- Sau khi máy chủ xác nhận và danh sách tải thành công, dòng vừa nhận chuyển lên đầu, sáng xanh 1,5 giây, hiện nhãn lượng vừa cộng và nhấn nhẹ ô số lượng. Áp dụng cả dòng chính thức và hàng chờ hoàn thiện. Dùng đúng mã quy cách để phân biệt Hộp/Lốc/Thùng; không cộng gộp các đơn vị để hiển thị tổng.
- Thứ tự 10 lượt nhận gần nhất được đọc từ nhật ký lệnh đã lưu, giữ khi tải riêng bảng, F5 hoặc rời rồi mở lại phiếu. Dùng đúng mã dòng được ghi nhận, không dò bằng tên hay quy cách; không thay LineNo. Hiệu ứng xanh kéo dài 1,5 giây và tôn trọng tùy chọn giảm chuyển động.
- Từ 13/09/2026, bỏ nút lịch sử, bảng lịch sử desktop và tab lịch sử mobile theo yêu cầu chủ cửa hàng. Thanh điều hướng điện thoại còn **Hàng nhập** và **Thông tin**. Lịch sử quét phục vụ thu ngân nằm ở `/admin/pos`.
- Command ID ngăn ghi trùng khi thử lại sau mất phản hồi. Thẻ xác nhận hiển thị tên cùng dòng đã lưu, lượng của lượt nhận và **Hiện trong phiếu** từ số lượng hiện tại. Bỏ phép chuyển đổi tổng cũ → mới để không trộn lượng từng lượt với tổng sau các lần chỉnh. Desktop đặt thẻ xác nhận trên ô tìm để danh sách autocomplete không che; ảnh và nhãn cũ được thay khi nhận sản phẩm tiếp theo.
- Các file mới: `_ScanFeedback.cshtml`, `warehouse-receiving-feedback.js`, `warehouse-receiving-feedback.css`. Nâng cấp xác nhận này không thêm migration hay API mới.

Kiểm tra riêng bằng `RECEIVING_FEEDBACK_CHECK=1` với browser `--receiving-mobile`: lưu thật trên SQL test, quét lặp, số lượng lẻ, mất phản hồi/thử lại, ảnh theo yêu cầu, đúng quy cách và thứ tự, không âm thanh, giảm chuyển động và bố cục mobile/desktop. Probe `RECEIVING_FEEDBACK_RELOAD_CHECK=1` kiểm tra thêm hai tab bằng chuột/phím, F5, mở lại và phiếu chỉ xem, đồng thời xác nhận không còn giao diện lịch sử. Không chạy Run All.

## Khi mạng yếu

Thêm hàng đã biết chuyển sang API `/intake/known`, dùng command ID cho một lần nhận hàng. Bấm liên tiếp bị chặn. Khi mất phản hồi sau khi SQL đã ghi nhận, thử lại cùng command ID không cộng lại số lượng. Chỉ đóng popup và quét tiếp sau khi tải được danh sách đã lưu. Khi chưa xác nhận được kết quả, các trường được khóa để tránh đổi nội dung của lệnh đang chờ.

Đây là luồng nhận hàng trực tuyến. Không tạo hàng đợi nhận hàng offline. Các trường đang nhập và command ID được giữ trong trang hiện tại; có cảnh báo trước khi rời trang có thay đổi chưa lưu. Không thể bảo đảm giữ bản nháp nếu hệ điều hành đóng trình duyệt đột ngột.

## Triển khai

Phải áp dụng migration **`20260912150000_AddReceivingPackagingPhoto`** bằng quy trình Migrator của dự án trước khi đưa bản Web này lên host. Migration thêm cột nullable `PackagingPhoto` (`varbinary(max)`) vào `StockDocumentProvisionalItems`; không sửa số lượng, giá, tồn kho hay trạng thái của phiếu cũ. Snapshot và manifest kiểm thử đã được cập nhật. Bản này còn bao gồm migration Wi-Fi trước đó; triển khai theo đúng chuỗi migration hiện hành.

Publish kèm `wwwroot/lib/zxing-browser/0.1.5`, CSS/JS mới và Razor đã build. Thư viện được đóng gói nội bộ, chỉ tải khi mở camera, không cần CDN cho bộ quét. Giấy phép và checksum lưu cạnh thư viện.

Ảnh từ điện thoại được thu nhỏ tối đa 1280 pixel và nén JPEG. Máy chủ chỉ nhận JPEG tối đa 256 KB, kích thước tối đa 1600 × 1600, kiểm tra cấu trúc segment; lưu trong giao dịch cùng khai báo. API xem ảnh kiểm tra quyền phiếu, cửa hàng, kho và trả `image/jpeg`, `nosniff`, `private, no-store`; không đưa ảnh vào uploads công khai. Không tự tạo sản phẩm hay cộng tồn khi nhân viên ghi nhận hàng mới.

## Kiểm thử

### Bản sửa trước khi bỏ giao diện lịch sử (13/09/2026)

Bộ test trước dùng tên sản phẩm gốc giống tên biến thể và chủ yếu gọi `refreshReceivingLines()`, nên chưa phát hiện mất thứ tự/lịch sử khi F5, sai tên giữa tìm kiếm và dòng đã lưu, hoặc autocomplete che khu vực xác nhận trên desktop. Lần sửa này lấy 10 lượt nhận từ `PurchaseReceivingActions`, lọc theo cửa hàng/phiếu và bỏ lệnh đã hoàn tác. Chỉ chiếu tên, mã, đơn vị, số lượng, ngày giờ, mã dòng và dấu hiệu có ảnh; không tải dữ liệu ảnh hay trả giá vốn vào lịch sử. Lệnh sửa số lượng và thao tác duyệt đơn thuần không được tính là nhận thêm. Trường hợp vừa nhận thêm vừa duyệt chỉ ghi lượng mới của lượt đó.

GET intake trả `recentReceipts`; hai thao tác nhận hàng cũng trả kèm danh sách này để biết thứ tự trước khi thay bảng. `warehouse-receiving-feedback.js` dùng mã dòng do máy chủ trả về để đưa lên đầu, nhận đúng biến thể/quy cách và khôi phục sau tải lại. Tên trong autocomplete nhập kho ưu tiên tên biến thể, giống dòng hàng. Lịch sử còn ở phiếu chỉ xem sau gửi duyệt. Không thêm migration; khi triển khai cần đưa cùng bản Web đã build, JS và CSS mới lên host.

Probe mới `RECEIVING_FEEDBACK_RELOAD_CHECK=1 --receiving-mobile`: phiếu SQL tạm hơn 20 dòng, ba biến thể có tên khác tên sản phẩm gốc; chọn bằng tên, quét barcode khác, quét lại sản phẩm cũ, hàng chờ, F5, đi khỏi rồi mở lại phiếu, sửa số lượng, desktop/320/390 px, autocomplete không che nút/thẻ và lịch sử sau gửi duyệt. Kiểm tra trực tiếp mã dòng đứng đầu, tên/đơn vị/số lượng và command ID của lịch sử với dữ liệu máy chủ. Log `Logs/receiving-feedback-reload-browser.log`; ảnh `TestResults/receiving-mobile/browser/feedback-receiving-390.png`, `feedback-receiving-320.png`, `feedback-history-320.png`, `feedback-receiving-desktop.png`.

Các kiểm tra liên quan: `ReceiptIntakeHttpTests` (hai test SQL về phân quyền, idempotency, sửa lượng, nhận/duyệt và lịch sử); probe camera/giọng nói/ảnh/mất phản hồi/10 lượt (`Logs/receiving-feedback-regression.log`); phím Enter và bấm +/− liên tục (`Logs/receiving-feedback-quantity-regression.log`); vị trí cuộn ở phiếu dài (`Logs/receiving-feedback-scroll-regression.log`, độ lệch 0 px). Build `Logs/receiving-feedback-reload-build.log`. Chỉ kiểm thử liên quan, không Run All; chưa triển khai lên host và chưa kiểm thử máy quét/camera vật lý.

Kết quả cuối: build 0 lỗi (10 cảnh báo có sẵn); hai `ReceiptIntakeHttpTests` pass; probe mới và ba probe hồi quy trên đều pass. Đã xem ảnh kết quả thực tế ở 320/390 px và desktop, gồm lúc autocomplete đang mở. Lịch sử tính đúng lượng nhận thêm khi vừa cộng hàng chờ vừa duyệt ngay, và thử lại cùng command không sinh thêm lượt.

### Khoảng cách desktop và đề xuất giao diện điện thoại

Màn chi tiết `/admin/warehouse-receiving/{id}` được đổi riêng sang `container-fluid` cùng `layout-menu-fixed`, bỏ container lồng trong `Detail.cshtml` và giới hạn 1440 px của `.wrd-page`. Khoảng cách nội dung với menu sử dụng padding từ layout chung, tương tự các trang quản trị đã dùng container-fluid. Không sửa layout dùng chung hay nghiệp vụ nhập hàng.

Build riêng pass (0 lỗi, 0 cảnh báo) trong `Logs/receiving-layout-build.log`. Probe `--receiving-mobile` với `RECEIVING_SEARCH_CHECK=1` pass tìm không dấu, chọn sản phẩm, viewport mobile và desktop trên SQL thử nghiệm tạm; log `Logs/receiving-layout-browser.log`. Ảnh desktop: `TestResults/receiving-mobile/browser/desktop-inline-search.png`. Probe chờ backdrop đóng hẳn trước khi chụp để ảnh không bị phủ lớp tối. Không Run All.

### Giao diện điện thoại đã tích hợp (13/09/2026)

Sửa autocomplete và cộng/trừ liên tiếp: Enter dùng kết quả hiện đang tô sáng của Select2 khi truy vấn đã tải xong, không đọc dữ liệu qua cache jQuery không tương thích. Xóa lựa chọn cũ sau khi mở sản phẩm để Enter vẫn chọn được cùng sản phẩm lần tiếp theo. Nhập barcode nhanh vẫn tra đúng chuỗi mới thay vì nhận dòng còn sót của truy vấn cũ.

Module `warehouse-receiving-quantity.js` giữ riêng số lượng đích của các lần chạm mobile, gom lưu sau 650 ms hoặc khi chuyển dòng. Các nút vẫn nhận chạm trong lúc chờ phản hồi; cộng rồi trừ về lượng ban đầu trước khi gửi sẽ hủy bản nháp. Tổng số lượng hiển thị theo bản nháp; trạng thái chỉ báo đã lưu khi máy chủ xác nhận hết. Mở tìm kiếm, camera, giọng nói, bảng sửa số lượng hoặc gửi duyệt đều chờ lưu xong. Khi mất phản hồi với hàng chờ hoàn thiện, thử lại đúng command và số lượng đã gửi trước, rồi mới gửi số lượng đích mới hơn. Không thêm API/migration và không bảo đảm lưu nháp qua đóng trang.

Kiểm tra riêng lần sửa này: `RECEIVING_INTERACTION_CHECK=1 --receiving-mobile` pass chọn chuột, ↑/↓ + Enter, chọn lại cùng sản phẩm, mã mới trước khi autocomplete phản hồi, hủy cặp +/−, 15 lần chạm gom 3 lần lưu, chuyển dòng, chạm trong lúc mạng chậm, mở sửa/tìm/gửi duyệt sau khi lưu, mất phản hồi và thử lại command chính xác. Log `Logs/receiving-interaction-browser.log`. Hồi quy `RECEIVING_SCROLL_CHECK=1 --receiving-mobile` pass độ lệch 0 px ở 320/390 px và bảng sửa số lượng lẻ (`Logs/receiving-interaction-scroll.log`); `--receipt-intake` pass nhập bàn phím, gửi và quản lý duyệt (`Logs/receiving-interaction-desktop.log`). Dùng Chrome và SQL thử nghiệm tạm. Build 0 lỗi, 10 cảnh báo sẵn có trong `Logs/receiving-interaction-build.log`; kiểm tra cú pháp JS pass. Không Run All; chưa triển khai lên host.

Sửa giữ vị trí khi chỉnh số lượng ở cuối phiếu: chụp vị trí dòng đang hiển thị ngay trước khi thay danh sách, dựng lại nút mobile đồng bộ rồi phục hồi vị trí trước khi trình duyệt vẽ. Không kéo về vị trí cũ nếu nhân viên đã cuộn trong lúc chờ phản hồi; chỉ lượt nhận hàng mới vẫn đưa lên đầu theo thiết kế. Probe riêng `RECEIVING_SCROLL_CHECK=1 --receiving-mobile` trên phiếu SQL tạm 22 dòng (thêm một hàng chờ để kiểm tra) đã tái hiện rung 29,5 px trước sửa; sau sửa độ lệch 0 px cho các lần +/− ở 320/390 px, gồm cả hàng chờ và chủ động cuộn khi mạng chậm. Kiểm tra bảng sửa số lượng lẻ và quét mới cũng pass. Log `Logs/receiving-scroll-before.log`, `Logs/receiving-scroll-after.log`; build không lỗi/cảnh báo trong `Logs/receiving-scroll-build.log`. Không Run All.

Đã triển khai bản được chấp thuận vào riêng màn chi tiết `/admin/warehouse-receiving/{id}`. Bản đề xuất tương tác trong thư mục visualization chỉ là tài liệu tham chiếu; giao diện thực tế sử dụng dữ liệu và API hiện hành, camera ZXing và nhận diện giọng nói hiện có.

- Điện thoại đến 768 px: tiêu đề phiếu và trạng thái ở đầu; thanh Nói / Quét mã / Gửi duyệt cố định ở đáy, hai tab Hàng nhập / Thông tin. Nội dung cuộn tự nhiên, chừa khoảng trống cho hai thanh và safe area. Ô tìm không dấu dùng trực tiếp trên tab Hàng nhập.
- Mỗi dòng có nút − / số lượng / +; chạm số lượng mở bảng từ dưới lên, hỗ trợ dấu phẩy thập phân, kiểm tra giá trị và xóa dòng qua luồng xác nhận hiện có. Cộng/trừ cập nhật ngay số lượng đích, gom lần lưu khi ngừng chạm 650 ms hoặc chuyển sang dòng khác; tiếp tục nhận lần chạm trong khi chờ máy chủ. Vẫn dùng các hàm lưu và API hiện hành, không tạo bản sao input nghiệp vụ để tránh đếm/lưu hai lần.
- Quét thành công trở về tab Hàng nhập, hiển thị lần nhận gần nhất và dòng vừa nhận ở đầu. Chỉ cập nhật thẻ khi có xác nhận lưu; không âm thanh, không tự phóng to ảnh.
- Tab Thông tin giữ đầy đủ mục cần xử lý, barcode, hàng/quy cách mới, ảnh, trạng thái bị trả về và đề nghị sửa. Nút ba chấm mở khai báo hàng chưa có hoặc chuyển tới thông tin. Chờ duyệt không có nút sửa số lượng, quét hay gửi duyệt.
- Khi mất phản hồi, giữ thay đổi trong trang, báo lỗi và có Lưu lại; chặn gửi duyệt khi chưa xác nhận. Với hàng chưa có, giữ nguyên số lượng và command ID đang chờ cho đến khi thử lại được xác nhận. Không tạo hàng đợi offline hoặc bảo đảm lưu nháp qua việc đóng trình duyệt.
- Khi đổi sang desktop, đưa các nút về vị trí ban đầu, giữ bảng và khoảng cách menu của layout chung. Modal nằm ngoài các tab để vẫn mở được từ tab Thông tin. Bộ đếm trên phiếu chỉ xem lấy từ dòng dữ liệu đã lưu.

File giao diện mới: `WarehouseReceiving/_ReceivingApp.cshtml`, `warehouse-receiving-app.css`, `warehouse-receiving-app.js`; nối tại `Detail.cshtml`. Không thêm migration hoặc API cho lần nâng cấp bố cục này.

Kiểm tra riêng: build Release không lỗi (`Logs/receiving-app-build.log`; 10 cảnh báo analyzer xUnit có sẵn ở các test khác). Probe `--receiving-mobile` với `RECEIVING_APP_CHECK=1` kiểm tra camera/giọng nói, đổi tab, ảnh, cộng/trừ, số lượng lẻ, mất phản hồi và cùng command ID khi thử lại, xóa dòng, gửi duyệt thực tế, chỉ xem và đề nghị sửa; các độ rộng 320/390/768/1440 px. Log `Logs/receiving-app-browser.log`. Probe hồi quy `--receipt-intake` kiểm tra tăng giảm liên tiếp, nhập bàn phím, đơn vị/quy đổi, gửi và quản lý duyệt; log `Logs/receiving-app-desktop-regression.log`. Không chạy Run All.

Ảnh giao diện thực tế: `TestResults/receiving-mobile/browser/app-390.png`, `app-320.png`, `app-information.png`, `app-edit-quantity.png`, `app-pending-revision.png`. Camera và giọng nói được giả lập trong kiểm thử trình duyệt, dữ liệu lưu trên SQL thử nghiệm tạm; chưa thử thiết bị camera/micro vật lý và chưa triển khai lên host.

- Build Release vào `Logs/pos-regression-20260912-artifacts`, không thay tiến trình Web đang chạy.
- `ReceiptIntakePhotoTests`, `ReceivingPackagingPhotoHttpTests`, `ReceiptIntakeHttpTests`, `DatabaseSchemaManifestTests`.
- Browser `--receiving-mobile`: Chrome viewport 390 × 844, camera giả lập bằng video canvas nhưng sử dụng bộ giải mã ZXing thật; CODE128, dừng track, quyền camera, hủy trong khi cấp quyền, đèn, quét tiếp, giọng nói bằng mock sự kiện trình duyệt, SQL thật, mất phản hồi và thử lại, ảnh, quản lý xem ảnh.
- Browser `--receipt-intake`: hồi quy nhập bằng bàn phím, đơn vị/quy đổi mới, tăng giảm liên tiếp, gửi duyệt và quản lý duyệt.
- Kết quả và ảnh: `TestResults/receiving-mobile`; không dùng dữ liệu cửa hàng đang vận hành.

Camera cần HTTPS trên điện thoại. Nhận diện giọng nói phụ thuộc trình duyệt và có thể cần dịch vụ Internet của trình duyệt; luôn có gõ tìm dự phòng. Tham khảo [getUserMedia](https://developer.mozilla.org/en-US/docs/Web/API/MediaDevices/getUserMedia), [SpeechRecognition](https://developer.mozilla.org/en-US/docs/Web/API/SpeechRecognition) và [ZXing Browser](https://github.com/zxing-js/browser).

Chưa triển khai lên host và chưa kiểm thử camera/micro vật lý trên Android/iPhone. Sau khi triển khai cần thử mã EAN/UPC/Code128 thực tế, quầy thiếu sáng, quyền camera/micro trên thiết bị nhân viên sử dụng.

Cập nhật tìm không dấu: đối chiếu tên variant, tên sản phẩm gốc và đơn vị ngay trên SQL Server bằng collation cho tìm kiếm, kể cả tên chuẩn hóa bị thiếu/cũ. Không thay đổi cách đối chiếu barcode hay collation của cơ sở dữ liệu; lần chỉnh giao diện/tìm kiếm này không phát sinh migration mới. Giữ giới hạn lấy ID trước khi nạp chi tiết. Kiểm thử SQL riêng có cả chữ Đ/đ và mã đơn vị chính xác; tham khảo cách chỉ định collation trong [tài liệu EF Core](https://learn.microsoft.com/en-us/ef/core/miscellaneous/collations-and-case-sensitivity).
