# Kế hoạch nâng cấp toàn bộ giao diện GaoApp

Ngày lập 25/10/2026. Hướng triển khai lấy bản demo điện thoại đã được thống nhất làm nền cho cách trình bày và thao tác. Toàn bộ màn nghiệp vụ được chuyển dần sang bộ thành phần dùng chung, với bố cục phù hợp cho điện thoại, tablet và desktop. Các quy tắc nghiệp vụ, quyền và dữ liệu được đối chiếu với controller, DTO và service hiện hành khi thực hiện từng bước.

**Ràng buộc cập nhật từ người dùng:** chỉ nâng giao diện điện thoại theo mẫu đã duyệt, giữ giao diện máy tính hiện tại. Menu điện thoại dùng chính `AdminMenuItems`, cùng cửa hàng/quyền/ẩn hiện. Yêu cầu mới từ UI-04 cho phép tự chạy test và đánh giá sau khi hoàn thành; dùng fixture SQL/Web/Chrome riêng và giữ nguyên cửa sổ Chrome đang mở. Yêu cầu không tự chạy ở các bước trước được giữ trong báo cáo lịch sử. Mô tả desktop trong kế hoạch là mốc đối chiếu, không cho phép tự thiết kế lại desktop.

**UI-02c đã viết mã, chờ tự nghiệm thu:** [mô tả và hướng dẫn Visual Studio](MOBILE-UI-02C-PINS-20261025.md). Sau yêu cầu sửa lỗi IDE và cập nhật migration, đã cài đúng SDK/runtime, build Web thành công, áp dụng đủ 81 migration trên `GaoAppDb` local và xác minh schema khớp; [báo cáo cập nhật](MOBILE-UI-02C-ENVIRONMENT-FIX-20261025.md). Chưa mở ứng dụng hoặc chạy bộ test; phần UI vẫn chờ người dùng kiểm tra.

**UI-02d đã viết mã, chờ tự nghiệm thu:** [Quản lý menu và Ẩn/hiện trên điện thoại](MOBILE-UI-02D-MENU-MANAGEMENT-20261025.md). Giữ CSS gốc, bảng/header desktop, form gốc và backend; có bản xem lại trước lưu riêng cho điện thoại. Chỉ kiểm tra tĩnh và cú pháp JS, không tự chạy app/build/test hoặc migration.

**Trạng thái hiện tại:** chuẩn điện thoại v2 đã được duyệt. Toàn UI-22a–f đã triển khai; 170/170 ca .NET duy nhất, 99 ca Node; 42 trạng thái/294 phone/84 desktop/42 print; 24 PDF đối chiếu trên ứng dụng hiện tại với migration SQL thật trong database riêng. Giữ desktop/tablet từ 768px, quyền, menu và cấu hình cửa hàng. Còn thiết bị và máy in vật lý; giữ nguyên phạm vi nghiệm thu lịch sử. Bước tiếp **UI-23a — trang công khai và dự phòng**. [Báo cáo UI-22](MOBILE-UI-22-COMPLETE-20261028.md), [tiến độ](mobile-ui-20261025/implementation-progress.json) và [hợp đồng](MOBILE-UI-INTEGRATION-CONTRACT-20261025.md).

- [Danh mục toàn bộ view và bố cục đích](MOBILE-UI-VIEW-INVENTORY-20261025.md).
- [Baseline có hash, controller, route, layout, partial, CSS và JS](mobile-ui-20261025/view-inventory.json).
- Demo tham chiếu nằm trong cuộc trò chuyện: Ghim nhanh và Menu tập trung. Khi thiết kế chi tiết, dùng các luồng thực tế trong source để thay dữ liệu, vai trò và trạng thái mô phỏng.

## 1 Phạm vi và kết quả kiểm kê

| Thành phần | Số lượng | Cách sử dụng trong kế hoạch |
|---|---:|---|
| Razor thuộc Admin | 248 file | Bao gồm trang nghiệp vụ, layout, partial và component |
| Razor thuộc Views gốc | 12 file | Trang khách hàng, kiosk, trang vào hệ thống và dự phòng |
| Tổng Razor | **260 file** | Mỗi file có một ID và một bước sở hữu chính |
| Trang thông thường | 144 file | Có thể có nhiều trạng thái hoặc nhiều route dùng chung |
| Trang in | 8 file | Bảo toàn khổ in và mục đích chứng từ |
| Màn chuyên dụng | 3 file | Màn khách, kiosk và StoreMonitor |
| Trang trạng thái | 6 file | Lỗi, từ chối, liên kết không khả dụng |
| Trang nhúng | 1 file | Tra cứu ACB trong ngữ cảnh đơn |
| Partial | 85 file | Nâng cấp cùng màn chủ hoặc bộ thành phần chung |
| Layout | 7 file | Đối chiếu nơi dùng và hợp nhất trách nhiệm điều hướng |
| View component | 2 file | Tài khoản nhân viên và trợ lý |
| Cấu hình Razor | 4 file | ViewStart và ViewImports |
| JS tự quản lý | 188 file | Gồm các renderer và loader cần rà theo màn chủ |
| CSS tự quản lý | 112 file | Gồm nền thiết kế, module và màn chuyên dụng |
| JS có dấu hiệu dựng DOM | 139 file | Là phạm vi rà soát UI động, không phải số màn độc lập |

Các loại trang cộng lại có 162 file. Đây là số template, không phải số màn nghiệp vụ đang hoạt động. Ví dụ `ManagementReport/Workspace.cshtml` phục vụ nhiều route phân tích; popup và nội dung động còn nằm trong JavaScript. Danh mục JSON bao phủ 300 tài nguyên CSS/JS tự quản lý ngoài `lib`, `vendor`, `uploads`; xác minh loader và nơi dùng trước khi quyết định sửa từng tài nguyên.

Baseline là working tree hiện tại, có SHA256 từng file. Khi bắt đầu một bước, đối chiếu lại hash và bổ sung những thay đổi phát sinh sau ngày lập kế hoạch. Route trong danh mục là template trích từ source; việc mở route, quyền và chụp trạng thái hiện hành được thực hiện trong bước tương ứng.

Phạm vi chính là giao diện Web. LabelPrintClient, LabelPrintServer, worker và Migrator được kiểm tra tương thích khi thay luồng in hoặc triển khai; việc thiết kế lại công cụ Android phụ trợ hay ứng dụng nền chưa nằm trong đợt này.

## 2 Hướng thiết kế áp dụng toàn hệ thống

### 2.1 Một bộ thiết kế cho ba nhóm thiết bị

| Thiết bị | Bố cục đích | Cách thao tác |
|---|---|---|
| Điện thoại | Một cột chính, header gọn, điều hướng hoặc thanh tác vụ rõ | Chạm, quét, nhập bằng bàn phím phù hợp; hạn chế cuộn hai chiều |
| Tablet | Hai vùng khi đủ rộng; danh sách và chi tiết có thể đặt cạnh nhau | Hỗ trợ chạm, bàn phím và xoay màn hình |
| Desktop | Bảng dữ liệu, nhiều cột và công cụ quản lý phù hợp diện tích | Giữ hiệu quả tra cứu, nhập nhanh, phím tắt và máy quét hiện có |

Kế thừa hạ tầng Bootstrap và hệ `gds` hiện tại khi ghép màn thật. Bản điện thoại v2 đã được duyệt làm chuẩn về màu sắc, phân cấp, bố cục tác vụ và chuyển động. Token navy/lam dùng chung trong `gaoapp.commerce-tokens.css`, áp dụng theo phạm vi trang/thành phần. Giữ bố cục desktop phù hợp diện tích; không đưa nguyên khung điện thoại vào desktop. Cơ chế thích ứng và hợp đồng loader/selector theo báo cáo UI-01c/e; tránh thay hàng loạt CSS toàn cục ngoài bước đang làm.

### 2.2 Khung ứng dụng và điều hướng

Phương án chính trên điện thoại là **Ghim nhanh**: tối đa bốn mục do người quản trị chọn trong menu hiện có và một nút mở Menu. Khi chưa có mục ghim, Menu vẫn là điểm mở đầy đủ. **Menu tập trung** là phương án hiển thị phù hợp khi không dùng mục ghim; dùng cùng dữ liệu và bộ thành phần.

Ngày 25/10/2026, người dùng yêu cầu thanh cố định phía dưới như ảnh Ghim nhanh: icon và nhãn, vạch đánh dấu mục đang chọn, bốn mục nhanh và Menu. Đã bổ sung thành phần và fixture để xem trước; UI-02 sẽ nối cấu hình và quyền thực tế.

- Header hiển thị tên trang, quay lại, tài khoản và ngữ cảnh cửa hàng hoặc kho cần thiết. Tên và thông tin dài phải xuống dòng hợp lý.
- Trang tra cứu có điều hướng chính. Luồng tác nghiệp tập trung như POS hoặc nhận hàng có dock hành động phù hợp và điểm mở Menu ở header; vùng dưới màn hình có một chủ sở hữu rõ.
- Bộ lọc ngắn dùng bảng trượt từ dưới; biểu mẫu dài hoặc chỉnh nhiều dòng dùng trang toàn màn hình. Giữ thao tác đóng và quay lại rõ ràng.
- Quay lại danh sách giữ query, bộ lọc, trang và vị trí khi ứng dụng hỗ trợ. Mỗi bước bổ sung việc khôi phục trạng thái vào đúng module.
- Dữ liệu tìm kiếm và lựa chọn hiển thị có thể lưu theo phạm vi trang. Dữ liệu nghiệp vụ chỉ báo đã lưu khi có xác nhận từ máy chủ; đổi cửa hàng hoặc tài khoản phải xử lý trạng thái theo phạm vi tương ứng.

### 2.3 Quy tắc menu bắt buộc

Áp dụng [AGENTS.md](../../AGENTS.md) và cơ chế [AdminMenuService](../../GaoApp.Application/Services/AdminMenus/AdminMenuService.cs).

1. Sidebar, Menu trên điện thoại và mục ghim đều lấy từ `AdminMenuItems` của cửa hàng sau khi lọc quyền, trạng thái, thứ tự và thiết lập ẩn/hiện theo nhân viên hoặc nhóm.
2. Người dùng tạo và sắp xếp menu trong Quản lý menu. Nâng cấp UI không tự tạo menu qua migration, seeder, script hoặc thao tác dữ liệu.
3. Cấu hình ghim chỉ lưu tham chiếu đến ID menu hiện có và thứ tự do người quản trị chọn. Không nhân bản tên, URL hoặc quyền thành một cây menu khác.
4. Menu bị xóa, ngừng hoạt động, ẩn hoặc mất quyền phải biến mất ở cả vị trí ghim. Không chuyển sang menu của cửa hàng khác để lấp chỗ trống.
5. Chưa cấu hình menu thì hiển thị trạng thái phù hợp và đường dẫn Quản lý menu cho người có quyền; không chèn mục chức năng giả.
6. Kiểm tra quyền tại controller/service giữ độc lập. Nút hành động trong một chứng từ tuân theo quyền và trạng thái chứng từ; việc ẩn menu không được dùng thay kiểm tra quyền truy cập.
7. Lối tắt chức năng tại Home và điều hướng POS được đối chiếu với nguồn menu chung. Các thao tác trong ngữ cảnh như mở bản in của đơn hoặc mở màn khách được phân biệt với menu chức năng và kiểm tra quyền tương ứng.

Nếu cần lưu cấu hình ghim mới, thiết kế lưu trữ và migration chỉ bổ sung cấu trúc cấu hình tham chiếu; không chèn `AdminMenuItems`. Theo lựa chọn mới ngày 28/10/2026, khi chưa có mục ghim riêng khả dụng thì thanh điện thoại ưu tiên Tổng quan, Đơn hàng, Nhập hàng và Bán hàng từ các đích thật đã lọc quyền; không tạo menu, không ghi cấu hình ghim. Lựa chọn ghim riêng vẫn được giữ.

### 2.4 Bộ mẫu bố cục

| Mã | Mẫu | Cấu trúc điện thoại | Điều kiện nghiệm thu chính |
|---|---|---|---|
| T01 | Danh sách | Tìm trước, trạng thái nhanh, lọc gọn, dòng hoặc thẻ, phân trang | Tìm được dữ liệu; mở chi tiết và quay lại giữ ngữ cảnh |
| T02 | Chi tiết chứng từ | Tóm tắt, trạng thái, nội dung theo phần, dock hành động | Đủ thông tin; hành động đúng quyền và vòng đời |
| T03 | Biểu mẫu | Một cột, chia nhóm, trường phù hợp bàn phím, lỗi tại trường | Lưu hợp lệ; lỗi không làm mất nội dung đang nhập |
| T04 | Tác nghiệp quét và số lượng | Quét hoặc tìm, dòng đang xử lý, số lượng theo quy cách, phản hồi lưu | Quét và thử lại giữ đúng lệnh, dòng và đơn vị |
| T05 | POS | Sản phẩm, giỏ, khách, ưu đãi và thanh toán theo tác vụ | Tổng tiền đúng, thu một lần, xử lý được mất phản hồi |
| T06 | Báo cáo | Kỳ và phạm vi, chỉ số chính, biểu đồ, bảng và drilldown | Giữ định nghĩa chỉ số và trạng thái chất lượng dữ liệu |
| T07 | Hộp thư | Danh sách hội thoại, cuộc trò chuyện, hồ sơ khách tách màn | Bàn phím, tin mới và đính kèm không làm mất thao tác |
| T08 | Cây và phân quyền | Chọn đối tượng, tìm mục, chỉnh theo nhóm, tổng kết trước lưu | Dễ thấy phạm vi và ảnh hưởng của thay đổi |
| T09 | Cài đặt | Chia nhóm, trạng thái hiện hành, chỉnh và xác nhận | Giữ che thông tin nhạy cảm và các lớp kiểm tra hiện có |
| T10 | Media | Lưới, xem ảnh, chọn và upload theo mục đích | Đúng phạm vi file, có tiến trình và lỗi rõ |
| T11 | In hoặc trình chiếu | Điều khiển thích ứng; nội dung theo khổ hoặc màn chuyên dụng | Giữ tỷ lệ, khổ, mã và kết quả in |
| T12 | Trang trạng thái | Lý do, trạng thái và hành động tiếp theo rõ | Có đường phục hồi; không lộ thông tin kỹ thuật ngoài quyền |
| T13 | Thành phần chung | Header, dock, thông báo, sheet, tài khoản và nội dung con | Có chủ sở hữu; nạp và gắn sự kiện một lần |

Mỗi file đã có mẫu và mô tả bố cục đích trong danh mục. Biểu mẫu hay bảng được dựng bằng JavaScript phải dùng cùng tiêu chuẩn với Razor.

### 2.5 Quy tắc thao tác và hiển thị

- Dùng màu thương hiệu, nền sáng hoặc tối theo thiết lập được hỗ trợ, độ tương phản rõ; trạng thái có nhãn chữ cùng màu.
- Vùng chạm chính hướng tới 44–48 CSS px. Trường nhập dùng cỡ chữ phù hợp điện thoại, nhãn luôn nhận biết được, input mode đúng loại dữ liệu.
- Bàn phím, thanh trình duyệt, safe area và xoay màn hình không che trường đang nhập, thông báo lỗi hoặc nút cần dùng.
- Hành động chính có tên cụ thể theo trạng thái; tác vụ phụ đặt trong phần Thêm. Tác vụ không được thực hiện phải có phản hồi phù hợp hoặc được ẩn theo quyền.
- Trạng thái đang tải, chưa có dữ liệu, không có kết quả, lỗi truy vấn, đang gửi và chưa xác nhận kết quả được thiết kế riêng.
- Mọi lỗi gửi lệnh giữ nội dung đủ để xử lý hoặc thử lại theo cơ chế hiện có. Không hiển thị thành công khi chỉ mới cập nhật giao diện phía trình duyệt.
- Dữ liệu cũ đang hiển thị khi tải lại cần có trạng thái rõ; lỗi không biến thành số 0 hoặc ghi đè kết quả bằng dữ liệu không hợp lệ.
- Ưu tiên một lớp chọn hoặc xác nhận; đóng bảng trượt trả focus về đúng điểm mở. Dùng được bàn phím và tùy chọn giảm chuyển động.

## 3 Những phần phải bảo toàn

| Phạm vi | Ràng buộc giữ nguyên khi thay view |
|---|---|
| Cửa hàng và kho | Tenant, phạm vi kho, truy vấn theo cửa hàng và quyền xem dữ liệu |
| POS | Cart/terminal/shift, chống nhấn lặp, journal, tab lock, offline session và quyền thu tiền |
| Tiền và ngân hàng | Tổng tiền từ luồng hiện có, nguồn bằng chứng, đối soát, giao dịch đã thu và trạng thái QR |
| Nhận hàng | Command ID, revision/row version, lệnh đang chờ xác nhận, quy cách và số lượng thực nhận |
| Đơn vị | Không cộng túi, gói, kg, lít hoặc sản phẩm khác đơn vị thành một tổng số lượng chung |
| Phiếu kho | Vòng đời và các chuyển trạng thái thực sự được service cho phép; giao dịch ghi kho giữ ở phía máy chủ |
| Giá và hóa đơn | Quyền xem giá vốn, giá bán, thuế, nguồn định giá, XML đúng chủ thể, khóa hóa đơn và phát hành |
| Công nợ và cọc | Phân bổ theo chứng từ, số dư, hoàn hoặc thu, tham chiếu và cơ chế thử lại |
| Quản trị | Policy, antiforgery, challenge xác nhận, giới hạn gọi, masking và audit hiện có |
| Báo cáo | Định nghĩa kỳ, múi giờ Việt Nam, phạm vi quầy/kho, chất lượng giá vốn và quyền xuất dữ liệu |
| In ấn | Khổ, tỷ lệ, barcode/QR, số bản, transport, retry và vòng đời lệnh in |

Các sửa đổi hợp đồng API hoặc lưu trữ ngoài cấu hình UI được ghi thành mục riêng, nêu rõ lý do và kiểm thử trước khi gộp vào một bước. PWA cài đặt, offline cho nghiệp vụ mới, ứng dụng native và nâng framework không thuộc phạm vi bắt buộc của đợt thay toàn bộ view.

## 4 Cách thực hiện từng bước

Mỗi lượt triển khai nhận một bước hoặc một tiểu bước đã xác định. Hoàn tất phạm vi, kiểm thử và cập nhật trạng thái trước khi mở rộng sang nhóm khác. Một bước nhiều màn được chia thành tiểu bước nhỏ; các file partial, JS và CSS đi kèm vẫn phải hoàn tất cùng màn chủ.

### 4.1 Quy trình cho một tiểu bước

1. **Đối chiếu đầu vào:** danh mục view, route, hash, quyền, vòng đời, assets và các thay đổi đang có trong working tree.
2. **Ghi nhận hiện trạng:** mở đúng route trên môi trường phát triển hoặc kiểm thử; chụp trạng thái chính, lỗi và vai trò được phép. Chọn dữ liệu thử phù hợp; giữ dữ liệu thật ngoài thao tác thử ghi.
3. **Chốt bản bố trí:** chỉ rõ nội dung đầu màn, phần thu gọn, hành động chính, hành động phụ, sheet hoặc trang riêng và hành vi quay lại.
4. **Thực hiện:** dùng thành phần chung; giữ ID và hợp đồng cần cho module hiện có hoặc đổi có kiểm soát cùng nơi gọi. Dùng một nguồn dữ liệu và một lần gắn sự kiện.
5. **Kiểm tra:** build phạm vi có thay đổi; chạy kiểm thử nghiệp vụ liên quan; xác minh mobile, tablet và desktop theo ma trận.
6. **Đối chiếu kết quả:** dữ liệu, số lượng, tiền và chuyển trạng thái trước sau khớp với luồng hiện hành; không tạo menu hay cấp quyền ngoài phạm vi.
7. **Ghi nhận:** cập nhật view, tài nguyên, bằng chứng và phần còn lại. Tách lỗi nền có sẵn khỏi lỗi do thay UI bằng bằng chứng cụ thể.

### 4.2 Kết quả cần bàn giao sau mỗi tiểu bước

| Mục | Nội dung |
|---|---|
| Phạm vi | Mã bước, ID view, route và các file thay đổi |
| Thiết kế | Ảnh mobile và desktop; mô tả luồng chính, sheet và quay lại |
| Kiểm thử | Lệnh hoặc ca đã chạy, kết quả, ảnh trạng thái lỗi và quyền liên quan |
| Bảo toàn | Số liệu đối chiếu; hợp đồng và chuyển trạng thái giữ nguyên hoặc đổi có mô tả |
| Hoàn thành | Danh sách đã xong, phần còn lại và bước kế tiếp đủ điều kiện |

Thư mục bằng chứng dự kiến: `Logs/mobile-ui/UI-xx/<tieu-buoc>/`. Chỉ đưa dữ liệu thử hoặc dữ liệu đã che vào ảnh và log. Build và test dùng output phù hợp với quy trình dự án; không chạy Migrator, publish hoặc phát hành hóa đơn chỉ để kiểm tra bố cục.

## 5 Thứ tự triển khai và theo dõi tiến độ

Các cột Razor và Trang là phạm vi sở hữu trong baseline, không phải số file bắt buộc viết lại. Trang bao gồm mẫu in, trạng thái, nhúng và màn chuyên dụng. Bước nào có file cũ sẽ có kết luận giữ, thay thế hoặc loại bỏ sau khi xác minh.

| Bước | Phạm vi | Razor | Trang | Phụ thuộc | Trạng thái |
|---|---|---:|---:|---|---|
| UI-00 | Kiểm kê và kế hoạch | 0 | 0 | Demo đã thống nhất | Hoàn thành kiểm kê tĩnh và kế hoạch |
| UI-01 | Nền thiết kế và bộ thành phần | 2 | 0 | UI-00 | a/b/c/e hoàn thành; d còn nghiệm thu |
| UI-02 | Menu và mục ghim theo cấu hình | 7 | 4 | UI-01 | a–b hoàn thành; c–f đã viết mã, chờ nghiệm thu |
| UI-03 | Khung ứng dụng và trang nền | 25 | 5 | UI-01, UI-02 | a–f đã viết mã, chờ nghiệm thu |
| UI-04 | Danh sách và chi tiết đơn POS | 2 | 2 | UI-03 | a–d đã triển khai và qua kiểm thử tự động; còn nghiệm thu thiết bị thật |
| UI-05 | Nhập hàng tại cửa hàng | 6 | 2 | UI-03, mẫu từ UI-04 | a–e đã triển khai và qua tự động; còn nghiệm thu thiết bị thật |
| UI-06 | Bán hàng POS | 16 | 1 | UI-03, UI-04, UI-05 | a đã qua tự động; b–f chưa triển khai; còn thiết bị thật |
| UI-07 | Ca làm việc và bàn giao | 6 | 6 | UI-06 | UI-07a–d qua tự động; còn thiết bị thật và helper két ngoài |
| UI-08 | Nghiệp vụ sau bán và trả hàng | 6 | 4 | UI-04, UI-07 | Đã triển khai a/b/c/d và qua tự động; còn thiết bị thật |
| UI-09 | Soạn hàng và giao hàng | 3 | 3 | UI-05, UI-06 | Đã triển khai a/b/c/d và qua tự động; còn thiết bị thật |
| UI-10 | Đề nghị mua và đơn mua | 9 | 9 | UI-04, UI-05 | Chưa bắt đầu |
| UI-11 | Quản lý và duyệt phiếu nhập | 15 | 4 | UI-05, UI-10 | Đã qua tự động; còn thiết bị thật |
| UI-12 | Kiểm kê và chứng từ biến động kho | 12 | 11 | UI-05, UI-11 | Đã qua tự động; còn thiết bị thật |
| UI-13 | Tra cứu kho và pháp nhân | 5 | 5 | UI-12 | Đã qua tự động; còn thiết bị thật |
| UI-14 | Sản phẩm và danh mục | 28 | 18 | UI-04, UI-11 | Phone qua tự động; còn thiết bị thật và lỗi route R05 độc lập |
| UI-15 | Khách hàng nhà cung cấp và công nợ | 14 | 11 | UI-04, UI-08 | Đã qua tự động; còn thiết bị thật và hợp đồng ngày hết hạn voucher |
| UI-16 | Hóa đơn và tích hợp hóa đơn | 25 | 14 | UI-11, UI-14, UI-15 | Đã qua tự động; còn thiết bị thật |
| UI-17 | Ngân hàng và đối soát | 13 | 9 | UI-06, UI-08, UI-15 | Đã qua paired snapshot; chờ workspace/thiết bị thật |
| UI-18 | Báo cáo và chi phí vận hành | 8 | 7 | UI-13, UI-15, UI-16, UI-17 | Đã qua paired snapshot; chờ workspace/thiết bị thật |
| UI-19 | Khuyến mãi và nội dung trình chiếu | 5 | 3 | UI-14, UI-15 | Đã qua paired snapshot; chờ workspace/thiết bị thật |
| UI-20 | Chăm sóc khách hàng trợ lý và media | 10 | 9 | UI-03, UI-14, UI-15 | Đã qua schema hiện tại; chờ migration/thiết bị thật |
| UI-21 | Quản trị nhân viên quyền và thiết bị | 16 | 14 | UI-02, UI-03 | Đã qua tự động và migration; chờ thiết bị thật |
| UI-22 | In ấn và màn hình chuyên dụng | 16 | 14 | Các module tạo chứng từ liên quan | Đã qua tự động; chờ thiết bị/máy in thật |
| UI-23 | Trang công khai và dự phòng | 11 | 7 | UI-03, UI-16, UI-20, UI-22 | Chưa bắt đầu |
| UI-24 | Nghiệm thu và triển khai từng đợt | 0 | 0 | Các bước trong đợt phát hành | Chưa bắt đầu |
| **Tổng phạm vi có file** | | **260** | **162** | | |

Thứ tự mặc định đi từ UI-01 đến UI-24. Sau ba màn thí điểm UI-04 đến UI-06, cập nhật các mẫu dùng chung theo kết quả thử thực tế trước khi nhân rộng. Không đặt thời hạn cố định cho toàn bộ 260 file trước khi có số liệu từ ba bước này.

## 6 Công việc chi tiết từng bước

### UI-01 Nền thiết kế và bộ thành phần

**Mục tiêu:** có bộ thành phần đủ dùng để chuyển từng màn mà không sao chép CSS hoặc dựng hai luồng nghiệp vụ cho desktop và điện thoại.

- [x] UI-01a: đối chiếu `gaoapp.design-system.css`, `gaoapp.ui.css`, `site.css`, `demo.css` và bộ Bootstrap; chốt token màu, chữ, khoảng cách, bo góc, focus, lớp nổi và breakpoint. Chọn GaoApp Commerce, tham khảo cách tổ chức UI thương mại của Shopify Polaris.
- [x] UI-01b: chuẩn bị mẫu header, thanh lọc, dòng hoặc thẻ, trạng thái, form, lỗi, sheet, dialog dài, dock và loading để ghép T01 đến T13. Trang xem thử `/admin/ui-foundation`; chưa chuyển các màn nghiệp vụ.
- [x] UI-01c: chốt cơ chế thích ứng theo diện tích và khả năng chạm; giữ một nguồn dữ liệu và mô hình thao tác cho cùng nghiệp vụ. Theo chuẩn v2 được duyệt và hợp đồng tích hợp UI-01c/e.
- [ ] UI-01d: chụp mẫu các thành phần ở 320, 390, 768 và 1440 px; xác minh chữ dài, số lớn, thông báo lỗi, keyboard và reduced motion.
- [x] UI-01e: lập danh sách selector và sự kiện cần giữ ở các màn thí điểm; chốt cách nạp CSS/JS theo layout và trang. Đã ghi snapshot 11 Razor thí điểm, ID/data/loader và quyền sở hữu tương tác.

**Đầu ra:** bộ token và thành phần có phạm vi rõ, trang kiểm tra thành phần nội bộ nếu cần, ma trận thiết bị, bản ghi lựa chọn và lỗi còn lại.

**Bàn giao UI-01a–b ngày 25/10:** [báo cáo và hướng dẫn dùng](MOBILE-UI-01-FOUNDATION-20261025.md). Đã có ảnh 320/390/768/1440 px, kiểm tra form/dialog/bàn phím và đối chiếu CSS cũ. UI-01d còn mở vì chưa kiểm tra thiết bị thật, reduced motion/forced colors khi bật và zoom; build toàn ứng dụng còn cần SDK/runtime đúng baseline repo.

**Đạt khi:** thành phần không tràn ngang, nút có nhãn và dùng được bàn phím; CSS mới không làm đổi các view chưa chuyển; import và section vẫn nạp đúng một lần. UI-01 chỉ đưa nền thiết kế vào phạm vi được kiểm tra, chưa đổi hàng loạt nghiệp vụ.

### UI-02 Menu và mục ghim theo cấu hình

- [x] UI-02a: thống nhất dữ liệu render từ `AdminMenuService.GetForRenderAsync`, `MenuVisibilityRules` và thiết lập ẩn/hiện hiện có. Partial thật dùng đúng cây đủ điều kiện; thiếu tenant/user thì không đọc menu.
- [x] UI-02b: chuyển menu điện thoại sang tìm kiếm, nhóm theo cấu hình và trạng thái trang đang mở; giữ thứ tự do người dùng đặt. Đã chuyển partial Admin/POS, cây nhiều cấp, tìm không dấu, active URL/route, focus và trạng thái trống; kiểm tra qua harness render source thật.
- [ ] UI-02c: đã viết mã lựa chọn tối đa bốn mục có sẵn, thứ tự và xem trước theo đối tượng; lưu ID theo cửa hàng, tiếp tục lọc quyền/ẩn hiện. Đã build Web và cập nhật/xác minh schema local theo yêu cầu sửa lỗi. Chờ người dùng tự kiểm tra giao diện bằng Visual Studio; chưa đánh dấu đạt nghiệm thu.
- [ ] UI-02d: đã viết mã nâng `AdminMenus` và `AdminMenuVisibility` trên điện thoại theo T08; tạo/sửa trên màn riêng, bản xem lại trước lưu, cây nhiều cấp và chọn đối tượng theo bước. Giữ desktop/backend; chờ người dùng build và tự kiểm tra trên Visual Studio theo báo cáo UI-02d, chưa đánh dấu đạt nghiệm thu.
- [ ] UI-02e: đã chuyển `_POSNavTabs`, menu “thêm” POS, lối tắt trang chủ/dashboard/tài khoản và nhận hàng sang menu cửa hàng đã lọc; giữ CSS desktop và controller/service. Chờ người dùng chạy Visual Studio nghiệm thu theo [báo cáo UI-02e](MOBILE-UI-02E-CONFIGURED-SHORTCUTS-20261025.md); chưa build/chạy app/bộ test trong lượt này.
- [ ] UI-02f: đã rà soát tĩnh menu trống/một mục/cây dài/tên dài/quyền/cửa hàng; bổ sung CSS điện thoại cho cây sâu và menu POS dài, chặn phản hồi tải ghim cũ, chuẩn bị hai lớp unit test và ma trận 22 ca. [Báo cáo UI-02f](MOBILE-UI-02F-MENU-EDGE-CASES-20261025.md); chưa build/chạy app/bộ test, chờ người dùng nghiệm thu.

**Đạt khi:** cấu hình menu cũ vẫn dùng được; mục ghim và menu đầy đủ cùng lọc đúng quyền; không có menu hay quyền mới được tạo tự động. Dùng lại `menu-visibility.browser.cjs` và bổ sung ca mục ghim cần thiết.

### UI-03 Khung ứng dụng và trang nền

- [ ] UI-03a: đã viết mã khung điện thoại cho ba layout; header chung và account dùng lại node hiện có, nội dung theo v2, POS/nhập hàng giữ header/dock riêng. [Báo cáo UI-03a](MOBILE-UI-03A-APP-SHELL-20261025.md); chờ người dùng chạy Visual Studio nghiệm thu, chưa build/chạy app/bộ test.
- [ ] UI-03b: đã viết mã safe area/visual viewport, bàn phím, scroll/focus và phối hợp menu/trợ lý/toast/dock; đồng bộ chế độ app nhập hàng về dưới 768 px. [Báo cáo UI-03b](MOBILE-UI-03B-VIEWPORT-FOCUS-20261025.md); đã đối chiếu source/cú pháp JS, chờ người dùng chạy Visual Studio, chưa build/chạy app/bộ test.
- [ ] UI-03c: đã viết mã tài khoản/hồ sơ/mật khẩu/trợ giúp, tiện ích hiện có, toast/xác nhận và ba trang từ chối/lỗi trên điện thoại. Giữ desktop, challenge, submit và quyền tại các điểm gọi. [Báo cáo UI-03c](MOBILE-UI-03C-ACCOUNT-FEEDBACK-20261025.md); chờ người dùng chạy Visual Studio, chưa build/chạy app/bộ test.
- [ ] UI-03d: đã viết mã tổng quan Home điện thoại, đọc báo cáo hôm nay theo quyền/cửa hàng; ghim chiếu cùng thanh đáy, link báo cáo từ menu thật; giữ Home desktop. Chỉ số mở rộng tách riêng trong [báo cáo UI-03d](MOBILE-UI-03D-HOME-OVERVIEW-20261025.md). Đã đối chiếu source/cú pháp JS, chờ người dùng chạy Visual Studio; chưa build/chạy app/bộ test.
- [ ] UI-03e: đã viết mã đăng nhập điện thoại một cột, hiện/ẩn mật khẩu native, lỗi/busy/viewport; giữ desktop Halloween, form/xác thực/cửa hàng/quầy. Theo yêu cầu bổ sung, profile http/https mở Chrome thường sau startup, không quản lý đóng browser. [Báo cáo UI-03e](MOBILE-UI-03E-LOGIN-CHROME-20261025.md); đã đối chiếu source/cú pháp JS, chờ người dùng chạy Visual Studio; chưa build/chạy app/bộ test/Chrome.
- [ ] UI-03f: đã rà tham chiếu/layout/section, ghi quyết định giữ cụm legacy chưa có caller, bổ sung Scripts cho Blank và partial lỗi điện thoại dùng chung cho hai Shared/Error; không xóa/kích hoạt layout/navbar cũ, giữ desktop/menu/quyền. **Chờ người dùng chạy Visual Studio**: chưa build Razor, chạy app/browser/bộ test/CSP suite. Xem [báo cáo UI-03f](MOBILE-UI-03F-LEGACY-LAYOUT-AUDIT-20261025.md) và 16 ca nghiệm thu.

**Đạt khi:** mọi layout đang dùng mở được, section không lỗi, nút tài khoản và Menu có thể sử dụng ở các kích thước; các view chưa chuyển vẫn hoạt động. Kiểm tra CSP liên quan bằng bộ kiểm tra hiện có.

### UI-04 Danh sách và chi tiết đơn POS

- [ ] UI-04a: đã chuyển `POSOrderPage/Index` trên phone sang thẻ T01, dữ liệu phụ/thống kê mở riêng và preview từ đáy; giữ DOM/filter/loader/phân trang/desktop. **Đã chạy kiểm tra theo yêu cầu người dùng:** build Release 0 lỗi/cảnh báo, 10 SQL/quyền/CSP, một test lọc sản phẩm và Chrome ở 10 kích thước đều qua; đã chỉnh mật độ để thấy tiền đơn đầu từ màn 320 px. Còn thiết bị thật/tổ hợp dữ liệu. Xem [báo cáo test UI-04a](MOBILE-UI-04A-RUNTIME-REVIEW-20261025.md).
- [ ] UI-04b: đã chuyển `POSOrderDetail/Index` trên phone sang T02; tóm tắt, Hàng hóa / Thanh toán / Lịch sử, thanh điều hướng và modal thanh toán theo dữ liệu/action gốc. Build 0 lỗi/cảnh báo, 7 hồi quy Razor/CSP và 17 nhóm browser ở 10 kích thước qua; ảnh desktop 1440 px khớp và node gốc được khôi phục. Còn thiết bị thật/tổ hợp UI-04d; xem [báo cáo UI-04b](MOBILE-UI-04B-POS-ORDER-DETAIL-20261025.md).
- [ ] UI-04c: đã đưa trả hàng/thanh toán/lịch sử về đúng thẻ trên phone, lọc nút theo quyền máy chủ và điều kiện đơn cũ; giữ desktop và handler/payload/thu tiền hiện có. Build 0 lỗi/cảnh báo, 12 hồi quy và 26 nhóm browser/10 viewport/bảy tổ hợp quyền qua; ảnh desktop khớp, dữ liệu/40 hash bảo vệ giữ nguyên. Xem [báo cáo UI-04c](MOBILE-UI-04C-CONTEXTUAL-ACTIONS-20261025.md); còn thiết bị thật và UI-04d.
- [ ] UI-04d: đã kiểm tra chín tổ hợp SQL, cọc/công nợ, mã voucher dài, 50 dòng/tổng 3 tỷ, HKD/hóa đơn đủ dữ liệu, lịch sử nhiều lần hoàn, giao hàng, lỗi và quyền; sửa tràn voucher, số đã hoàn trên phone và mật độ lịch sử. Build 0 lỗi/cảnh báo; 25 hồi quy, 7 Razor/CSP chạy lại, 16 nhóm mở rộng + 26 nhóm chi tiết và probe danh sách qua; một khoản hoàn thật qua browser ghi đúng một lần trong fixture. Xem [báo cáo UI-04d](MOBILE-UI-04D-ACCEPTANCE-20261025.md); còn thiết bị thật, không đổi desktop/quyền/menu/Chrome đang mở.

**Đạt khi:** tìm, lọc, mở và quay lại giữ ngữ cảnh; tổng tiền, khoản đã thu và còn phải thu khớp dữ liệu nguồn. Dùng lại `pos-orders-page.browser.cjs`; bổ sung ca bố cục và khôi phục trạng thái còn thiếu. Đây là mẫu danh sách và chi tiết dùng để nhân rộng.

### UI-05 Nhập hàng tại cửa hàng

- [ ] UI-05a: đã nâng danh sách phone: thống kê gọn, lọc kho/HKD, liên kết đơn mua và trạng thái; thẻ có giai đoạn xử lý, giữ CTA cũ và modal tạo phiếu. Build 0 lỗi/cảnh báo, 7 hồi quy và 28 nhóm browser/10 viewport qua; desktop DOM/pixel khớp, 60 hash và snapshot SQL giữ nguyên. Xem [báo cáo UI-05a](MOBILE-UI-05A-RECEIVING-LIST-20261025.md); còn Android/iOS thật, không đổi quyền/menu/Chrome đang mở.
- [ ] UI-05b: đã nâng chi tiết phiếu phone T04, navy/lam, quét/tìm, xác nhận lần nhận gần nhất và hai tab Hàng nhập / Thông tin; metadata chuyển đúng tab, giữ `_ReceivingApp`, capture/feedback/command. Sửa vùng vuốt che nút Nói, nhãn chỉ xem và cuộn đầu phiếu. Build 0 lỗi/cảnh báo, 18 hồi quy, 23 nhóm browser/10 viewport cùng probe cuộn cũ qua; desktop pixel/DOM và SQL bảo vệ khớp. [Báo cáo UI-05b](MOBILE-UI-05B-RECEIVING-DETAIL-20261025.md); còn thiết bị thật.
- [ ] UI-05c: đã nâng nhãn số lượng từng quy cách, nhập số lẻ, form hàng chưa có, ảnh/ghi chú mở riêng và lối vào mục cần xử lý/ảnh gốc theo quyền. Tóm tắt bằng số dòng và số mục, không cộng lốc/thùng/hộp. Build 0 lỗi/cảnh báo, 26 hồi quy, 18 nhóm browser/10 viewport, 23 nhóm UI-05b và probe cuộn qua; desktop pixel/node/form và SQL bảo vệ khớp. [Báo cáo UI-05c](MOBILE-UI-05C-PACKING-20261025.md); còn thiết bị thật.
- [ ] UI-05d: đã giữ command/payload và trường chờ xác nhận, chặn thành công sớm khi reload lỗi/snapshot cũ; chỉ đóng/quét tiếp khi nhật ký máy chủ khớp dòng hiện tại. Sửa thử lại số lượng sau mất phản hồi với kiểm tra phiên bản gốc, giữ quyền/xung đột. Build 0 lỗi/cảnh báo, 35 hồi quy, 17 nhóm browser, toàn luồng nhận/gửi duyệt/chỉ xem, tương tác gộp và cuộn qua; desktop pixel và SQL bảo vệ khớp. [Báo cáo UI-05d](MOBILE-UI-05D-ACKNOWLEDGEMENT-20261025.md); còn thiết bị thật.
- [ ] UI-05e: đã kiểm tra số lẻ dưới 1/tối thiểu 0,001, quét lặp, hai Lốc ×4/×6, mất phản hồi/thử lại, offline không tự gửi, phiếu chỉ xem/quyền/cửa hàng, camera và bàn phím mô phỏng. Sửa min riêng phone ở ba nút bước; desktop nguyên pixel/DOM/cách tính. Build 0 lỗi/cảnh báo, 50 hồi quy, 28 nhóm browser/10 viewport, 17 nhóm ack và ba probe gốc qua; SQL và 95 hash bảo vệ khớp. [Báo cáo UI-05e](MOBILE-UI-05E-ACCEPTANCE-20261025.md); còn Android/iOS thật.

**Đạt khi:** không cộng trùng, không trộn đơn vị, trạng thái lưu đúng kết quả máy chủ; giữ hai tab Hàng nhập và Thông tin theo quyết định trước đây. Luồng nhận hàng trực tuyến giữ giới hạn hiện hành, không tự thêm hàng đợi offline.

**Bộ kiểm tra liên quan:** `receiving-mobile.browser.cjs`, `receiving-interaction.browser.cjs`, `receiving-scroll.browser.cjs`, `receiving-feedback-reload.browser.cjs`; chọn ca theo phần đã đổi và tài liệu nhập hàng hiện hành.

### UI-06 Bán hàng POS

- [ ] UI-06a: đầu vào POS phone đã triển khai và qua tự động; bổ sung camera theo yêu cầu, dùng decoder nhận hàng và exact scan owner gốc. Build 0 lỗi/cảnh báo, 49 hồi quy, 16 nhóm camera + 16 nhóm POS/10 viewport, SQL và 197 hash bảo vệ qua. Desktop/tablet không hiện nút camera; giỏ/quyền/menu giữ nguyên. [Bổ sung camera POS](MOBILE-UI-06A-POS-CAMERA-20261025.md); còn nghiệm thu camera Android/iOS thật.
- [ ] UI-06b: đã triển khai thẻ giỏ phone, quy cách/lượng/đơn giá/thành tiền, menu dòng trong luồng cuộn, bảng giá theo thẻ, khách/điểm/voucher và editor ghi chú/giảm giá. Giữ các owner/điều kiện gốc; sửa đúng selector nút Bảng giá hiện có. Build 0 lỗi/cảnh báo, 57 hồi quy + 3 JS, 14 nhóm giỏ/7 viewport + 16 nhóm đầu vào POS, SQL và 203 hash bảo vệ qua. [Báo cáo UI-06b](MOBILE-UI-06B-POS-CART-CUSTOMER-20261025.md); còn điện thoại thật.
- [ ] UI-06c: đã triển khai thanh toán phone tập trung: tổng/phương thức/số thu/tiền thừa, footer dùng nút gốc, lỗi và ledger giữ owner hiện hữu. Prime/legacy 14 lượt viewport, 2 bàn phím mô phỏng, 5 đối chiếu desktop; 7 nhóm HTTP/SQL, 59 hồi quy, probe tiền gốc và 7 nhóm phản hồi sau chốt qua. SQL đúng 3 lần thu riêng, 1 đơn, trả 20.000; 206 hash bảo vệ khớp. [Báo cáo UI-06c](MOBILE-UI-06C-POS-PAYMENT-20261026.md); còn điện thoại thật.
- [ ] UI-06d: đã triển khai QR/history/chờ ngân hàng/đối chiếu bằng chứng trên phone, footer dùng tác vụ và vùng lỗi gốc; khôi phục đúng QR/số tiền/ảnh khi mở lại và reload. Build 0 lỗi/cảnh báo, 394 hồi quy, 7 nhóm browser/SQL, 28 phone/4 visualViewport mô phỏng/5 desktop khớp; 211 hash bảo vệ. [Báo cáo UI-06d](MOBILE-UI-06D-POS-QR-BANK-20261026.md); còn điện thoại/ngân hàng thật.
- [ ] UI-06e: đã nâng trạng thái offline/journal, khóa phiên native và form giao hàng phone; giữ owner/hàng đợi/khóa tab/quyền và menu mở màn khách. Build 0 lỗi/cảnh báo, 358 hồi quy, 111 JS, 9 nhóm browser/SQL, 42 phone/2 viewport mô phỏng/6 desktop khớp; 219 hash bảo vệ. [Báo cáo UI-06e](MOBILE-UI-06E-POS-SESSION-DELIVERY-20261026.md); còn thiết bị thật.
- [ ] UI-06f: đã gom hai template ghi chú/giảm giá cũ chưa được gọi thành fragment canonical dùng một lần trong _DraftMetaPanel; HTML giữ nguyên, không sửa CSS/JS/owner/quyền/menu. Build sạch, 67 hồi quy, 8 nhóm browser/SQL, 21 phone/5 desktop, 33 scan DOM không ID trùng, probe tiền gốc + 7 feedback qua; 225 hash bảo vệ. [Báo cáo UI-06f](MOBILE-UI-06F-POS-EDITOR-AUDIT-20261026.md); còn thiết bị thật.

**Đạt khi:** thao tác thường xuyên thực hiện được bằng một tay; tiền và giỏ khớp server; nhấn lặp và mất phản hồi không phát sinh lần thu thứ hai. Chia tối thiểu thành ba tiểu đợt: giỏ và tìm hàng, thanh toán, offline/khôi phục.

**Bộ kiểm tra liên quan:** nhóm `pos-search-enter`, `pos-barcode-search`, `pos-scan-feedback`, `pos-payment-workspace`, `pos-checkout-feedback`, `pos-cash-repeat`, `pos-pending-qr`, `pos-bank-transfer-review`, `pos-offline`, `pos-offline-session`, `pos-offline-privacy`, `pos-tab-lock`, `pos-single-sale-journal` trong `GaoApp.Tests.Browser`.

### UI-07 Ca làm việc và bàn giao

- [ ] UI-07a: đã nâng ca hiện tại, xác nhận mở két/mở/nhận/đóng ca và nháp kiểm đếm trên điện thoại; tách số dự kiến với thực đếm, nút cuối màn hình, giữ owner/quyền/menu và desktop. Build sạch, 48 .NET + 20 JS, 6 nhóm browser/SQL, 21 lượt phone, 10 desktop, 33 scan ID và 3 probe gốc qua. [Báo cáo UI-07a](MOBILE-UI-07A-POS-SHIFT-20261026.md); còn thiết bị thật.
- [ ] UI-07b: đã nâng lịch sử ca/phiếu thành thẻ, filter mở rộng, chi tiết ca mở riêng có quay lại, modal phiếu một cột; lọc quầy dùng API sẵn có. Lịch sử giữ phạm vi ca của tôi, không mở quyền xem thu ngân khác. Build sạch, 48 .NET + 20 JS, 7 nhóm browser/SQL, 30 phone/7 desktop/38 scan ID; probe bàn giao/in/nhận/duyệt gốc qua sau sửa kỳ vọng menu cấu hình. [Báo cáo UI-07b](MOBILE-UI-07B-POS-HISTORY-HANDOVER-20261026.md); còn thiết bị thật.
- [ ] UI-07c: đã nâng đối soát và quản lý duyệt ca phone: tiền dự kiến/thực đếm/chênh lệch rõ, hàng đợi trước, bộ lọc/thống kê mở rộng, form duyệt cùng node gốc. 48 .NET + 20 JS, 5 nhóm browser/SQL, 26 phone/9 desktop/35 scan ID; 315 hash bảo vệ và hai probe gốc qua. [Báo cáo UI-07c](MOBILE-UI-07C-POS-RECONCILIATION-APPROVAL-20261026.md); còn thiết bị thật.
- [ ] UI-07d: đã nâng dashboard phone với KPI hai cột, quỹ/cảnh báo trước và nhóm thu-chi/biểu đồ mở rộng; lối tắt giữ provider cấu hình. Chặn resize vẽ lại ca cũ khi đang tải/lỗi chuyển ca, không đổi tính tiền/API. Build sạch, 52 .NET + 20 JS, 5 nhóm browser/SQL, 12 phone/3 desktop/15 scan ID; 319 hash bảo vệ. [Báo cáo UI-07d](MOBILE-UI-07D-POS-DASHBOARD-20261026.md); còn thiết bị thật.

**Đạt khi:** bàn giao và quỹ tiền giữ đúng nghiệp vụ, đồng thời thao tác mở/đóng và duyệt rõ trên điện thoại. Dùng bộ kiểm tra `pos-shift-cash`, `pos-receive-shift`, `pos-shift-admin`, `pos-shift-reconciliation`, `pos-drawer-atomic`. Bản in ca được kiểm tra giao diện mở trong bước này và nội dung in ở UI-22.

### UI-08 Nghiệp vụ sau bán và trả hàng

- [ ] UI-08a: đã nâng phone danh sách trạng thái dạng thẻ; tạo/duyệt trong native dialog toàn màn hình, action gốc ở cuối viewport, giữ form/preview/busy/quyền. 65 .NET + 20 JS, 5 browser/SQL, 25 phone/10 desktop/36 scan ID; 362 hash và probe requests gốc qua. [Báo cáo UI-08a](MOBILE-UI-08A-POS-REQUESTS-20261026.md); còn thiết bị thật.
- [ ] UI-08b: đã nâng phone hàng chờ nhập theo phiếu, đơn gốc, dòng trả, số nhận/đơn vị bán/quy đổi gốc/đã nhập tại kho/chờ nhập và giá vốn. Chỉ thêm projection đọc; lệnh nhập kho/hoàn tiền giữ nguyên. [Báo cáo toàn UI-08](MOBILE-UI-08-COMPLETE-20261026.md); còn thiết bị thật.
- [ ] UI-08c: đã nâng phone danh sách và chi tiết chứng từ trả cũ, tìm kiếm/phân trang GET, metadata và dữ liệu nguồn mở rộng. Giữ nguyên trạng thái chỉ tra cứu; không tạo nút sửa/liên kết ngoài controller hiện có. [Báo cáo toàn UI-08](MOBILE-UI-08-COMPLETE-20261026.md); còn thiết bị thật.
- [ ] UI-08d: đã kiểm tra gửi lặp, retry, chi phí chưa đủ, từ chối/rút, thiếu quyền, store khác, chứng từ đổi khi form đang mở và đóng/mở ca. 79 .NET + 31 JS, 11 browser/SQL, 55 phone/16 desktop/72 scan ID; ba probe gốc qua. [Báo cáo toàn UI-08](MOBILE-UI-08-COMPLETE-20261026.md); còn thiết bị thật.

**Đạt khi:** yêu cầu và hoàn trả đi đúng controller/service hiện hành, tiền và kho không bị ghi từ sự kiện UI lặp. Dùng nhóm `pos-requests`, `pos-returns`, `pos-pending-restock` và các kiểm tra phần tiền liên quan.

### UI-09 Soạn hàng và giao hàng

- [ ] UI-09a: đã nâng phone soạn hàng, danh sách/chi tiết riêng, kho/chủ hàng gốc gần người nhận, lượng theo quy cách và lý do thiếu giữ nguyên; tìm/quét bằng luồng cũ. [Báo cáo toàn UI-09](MOBILE-UI-09-COMPLETE-20261026.md); còn thiết bị thật.
- [ ] UI-09b: đã nâng phone danh sách, tạo/xếp chuyến và chi tiết; người giao, điểm ghé, nguồn hàng, tiến độ và xác nhận giữ hàng rõ. [Báo cáo toàn UI-09](MOBILE-UI-09-COMPLETE-20261026.md); còn thiết bị thật.
- [ ] UI-09c: đã nâng phone bàn giao từng đơn, lượng giao/nhận trả theo đơn vị gốc, tiền và bằng chứng vật lý; giữ preview/revision/counter/source, bill sau chốt và xác nhận cuối. [Báo cáo toàn UI-09](MOBILE-UI-09-COMPLETE-20261026.md); còn thiết bị thật.
- [ ] UI-09d: đã kiểm tra quyền/phiên/store, stale GET/version, thiếu/thay thế hàng, giao một phần/không giao, uncertain retry, custody, đổi người giao, link A5/bill và hoàn thành chuyến; 304 .NET + 107 JS, 26 browser/SQL, 70 phone/20 desktop/90 scan ID. [Báo cáo toàn UI-09](MOBILE-UI-09-COMPLETE-20261026.md); còn thiết bị thật.

**Đạt khi:** soạn, phân chuyến và ghi nhận kết quả không mất nguồn chứng từ hoặc số lượng giữ hộ. Dùng các bộ `delivery`, `delivery-picking`, `delivery-trip`, `delivery-trip-completion`, `delivery-inventory`.

### UI-10 Đề nghị mua và đơn mua

- [ ] UI-10a: đã nâng phone danh sách và biểu mẫu đề nghị mua theo T01/T03; giữ phạm vi của tôi/cửa hàng, bộ lọc, hàng và lượng. Nhà cung cấp/kho được chọn ở bước chuẩn bị theo chức năng hiện có.
- [ ] UI-10b: đã nâng chi tiết duyệt và chuẩn bị; dòng hàng thành thẻ có lượng đề xuất/duyệt/chênh lệch, giữ lý do và xác nhận trước chuyển đơn.
- [ ] UI-10c: đã nâng danh sách, tạo/sửa và chi tiết đơn mua; đúng nhãn từng đơn vị khi cùng sản phẩm có nhiều dòng, tiến độ nhận/còn chờ/đóng thiếu và thao tác theo quyền.
- [ ] UI-10d: đã nâng CommercialEdit theo chính sách hiện tại: đơn mua khóa tên/đơn vị/lượng, nhóm nhà cung cấp/đơn vị mua/kho/ngày/ghi chú; giá thực tế và VAT được quản lý chốt trên phiếu nhập. Không thêm trường giá, thuế hay chiết khấu ngoài luồng hiện có; giá vốn giữ quyền cũ.
- [ ] UI-10e: đã nâng PurchaseReceiving, chọn/quét đúng quy cách, lượng nhận, đối chiếu PO và hàng vừa nhận; giữ lease/RowVersion, chứng từ nguồn và bước gửi quản lý. Đã qua 593 .NET + 37 JS, 7 nhóm browser/SQL, 105 phone/30 desktop/27 hộp thoại. [Báo cáo toàn UI-10](MOBILE-UI-10-COMPLETE-20261026.md); còn thiết bị thật.

**Đạt khi:** đi trọn đề nghị → chuẩn bị → đơn mua → nhận hàng được bằng điện thoại, lượng và nguồn liên kết đúng. Chia thành các tiểu bước đề nghị, đơn mua và điều phối nhận. Kiểm tra bản in đơn mua ở UI-22.

### UI-11 Quản lý và duyệt phiếu nhập

- [x] UI-11a: danh sách và phần đầu phiếu với kho, nguồn, nhà cung cấp, quyền và trạng thái rõ.
- [x] UI-11b: chia nội dung phiếu thành Hàng nhận, Cần hoàn thiện, Thương mại và Chứng từ khi dữ liệu/quyền cho phép.
- [x] UI-11c: giữ luồng hoàn thiện hàng mới, quy cách, barcode, ảnh, hàng ngoài đơn và quá lượng; việc cần xử lý có đường mở cụ thể.
- [x] UI-11d: duyệt thương mại, giá bán, phân bổ và XML dùng phần chi tiết phù hợp; quyền xem giá vốn và chủ thể XML giữ nguyên.
- [x] UI-11e: tách phiếu có xem trước hai phía; ghi chú và lịch sử theo quyền; thao tác duyệt giữ điều kiện hiện hành.
- [x] UI-11f: nâng thư viện hóa đơn đầu vào và picker XML; tìm được chứng từ và kiểm tra nguồn trước liên kết.

**Đạt khi:** người nhận và người duyệt nhìn thấy đúng dữ liệu và tác vụ; giá, lượng, liên kết XML và nguồn đơn mua khớp luồng cũ. Chia ít nhất bốn tiểu bước: danh sách/đầu phiếu, hàng cần hoàn thiện, thương mại/XML, tách phiếu.

**Bộ kiểm tra liên quan:** chọn trong `receipt-entry`, `receipt-intake`, `receipt-document-actions`, `receipt-approval-workspace`, `receipt-barcode-proposals`, `receipt-pricing-allocation`, `receipt-pricing-optional`, `receipt-price-review-layout`, `receipt-xml-pricing`, `receipt-supplier-correction`, `input-invoice-library`, `input-invoice-commercial-access` và các ca liên quan trực tiếp đến phần thay đổi.

### UI-12 Kiểm kê và chứng từ biến động kho

- [x] UI-12a: chi tiết kiểm kê theo dòng có số hệ thống và số đếm; quét hoặc tìm; chênh lệch mở riêng; danh sách phiếu dùng cùng mẫu đã có.
- [x] UI-12b: danh sách và chi tiết chuyển kho; kho đi/đến và lượng dễ đối chiếu; hành động theo vòng đời service.
- [x] UI-12c: tạo, sửa, xem và duyệt điều chỉnh; kho, lý do, lượng nhập/hệ số/lượng gốc và quyền rõ; không suy ra số dư trước/sau khi nguồn chưa cung cấp.
- [x] UI-12d: rà nguồn xuất kho hiện có; nâng danh sách/chi tiết case tồn âm theo đơn của InventoryIssueManagement, giữ nguồn, trạng thái và tác vụ được phép; enum Issue=2 chưa có luồng phiếu xuất độc lập nên không tạo workflow mới.
- [x] UI-12e: xác minh /admin/inventory-adjustment yêu cầu Adjustment.View rồi chuyển về InventoryAdjustmentDocuments.Index; file Index cũ chưa có caller, giữ nguyên controller và view.

**Đạt khi:** tác nghiệp không phụ thuộc bảng rộng 980px; các bảng đối chiếu cần nhiều cột có vùng riêng. Số lượng, kho, lock và chuyển trạng thái khớp backend; không tự thêm trạng thái từ demo. Chia theo kiểm kê, chuyển kho, điều chỉnh và nguồn xuất kho đang có. Dùng `stock-count-warehouse.browser.cjs` và kiểm tra service/repository liên quan đến từng chứng từ.

### UI-13 Tra cứu kho và pháp nhân

- [x] UI-13a: InventoryInquiry ưu tiên tìm hoặc quét; tồn thực, giữ hàng và lượng sẵn có có nhãn theo dữ liệu hiện hành.
- [x] UI-13b: thẻ kho theo sản phẩm, kho và kỳ; giữ tồn trước/biến động/tồn sau từng phát sinh và thông tin receipt/XML gốc. DTO hiện chưa có tổng đầu/cuối kỳ hoặc URL/ID mở từng chứng từ nguồn; không cộng giả từ dữ liệu phân trang hay suy đoán đường dẫn.
- [x] UI-13c: quản lý kho, pháp nhân và đối soát pháp nhân dùng T01/T03/T06 phù hợp; dữ liệu pháp lý chia nhóm.
- [x] UI-13d: kiểm tra phạm vi kho/cửa hàng, quyền giá vốn ADMIN, đơn vị hệ số 4/24, tìm barcode/SKU/tên và danh sách 1,5 triệu dòng. Lượt đầu timeout SQL khi chạy song song; chạy riêng giữ nguyên query/timeout đã qua, thời gian tìm Tất cả vẫn chậm và được ghi trong báo cáo.

**Đạt khi:** số tồn, nguồn phát sinh và đối chiếu được giữ nguyên; dữ liệu giá trị chỉ hiện với quyền tương ứng. Dùng `ledger-search.browser.cjs` và các kiểm tra đọc tồn hiện hành.

### UI-14 Sản phẩm và danh mục

- [x] UI-14a: danh sách/chi tiết sản phẩm trên điện thoại; thu gọn chỉ số, giữ tìm SKU/barcode/tên và bộ lọc. Detail gốc chỉ có thư viện ảnh, tên và bí danh; không tự thêm dữ liệu chưa có.
- [x] UI-14b: tạo/sửa sản phẩm chia thông tin, quy cách, đơn vị/quy đổi, giá và ảnh; bảo toàn trạng thái sẵn sàng bán.
- [x] UI-14c: quy cách, chuyển đổi đơn vị và barcode dùng chọn cấp rồi chỉnh; tránh bảng lồng trên điện thoại.
- [x] UI-14d: đã kiểm toán R05 và chốt dùng luồng hoạt động Sửa sản phẩm → quy cách → Đơn vị & Barcode → đơn vị đã chọn. Route BarcodeManager độc lập vẫn thiếu Index view và partial không có owner; chưa sửa lỗi nguồn này, không đưa partial chưa hoạt động ra màn hình hoặc tự tạo menu.
- [x] UI-14e: thương hiệu, danh mục, đơn vị và thuế áp dụng một bộ danh sách/biểu mẫu; giữ validation và row version tương ứng.
- [x] UI-14f: thuộc tính, giá trị thuộc tính, chuẩn hóa và lịch sử barcode theo ngữ cảnh sản phẩm.

**Đạt khi:** tìm và chỉnh đúng quy cách, giá và đơn vị; quyền giá vốn giữ nguyên. Chia thành sản phẩm, đơn vị/barcode, danh mục nền và thuộc tính. Tái sử dụng các kiểm tra `product-filters`, `product-prices`, `product-variant-cost` cùng ca tương ứng.

### UI-15 Khách hàng nhà cung cấp và công nợ

- [x] UI-15a: danh sách, biểu mẫu khách và hồ sơ; tab Tổng quan, Đơn, Điểm, Voucher theo dữ liệu và quyền hiện có.
- [x] UI-15b: danh sách và biểu mẫu nhà cung cấp trên điện thoại. GET Create và POST lỗi giữ dùng Edit; file Create cũ đã kiểm tra và giữ nguyên, không tạo thêm luồng trùng.
- [x] UI-15c: công nợ, lịch sử thu và thu nợ theo chứng từ; nhóm số tiền rõ và lỗi giữ nội dung thao tác.
- [x] UI-15d: cọc khách, phát sinh thu/hoàn và liên kết đơn; phản hồi chống gửi lặp theo luồng hiện có.
- [x] UI-15e: cấu hình thưởng và danh sách voucher theo owner gốc; sửa đúng ba data-attribute tỷ lệ sang InvariantCulture để xác nhận đọc đúng số theo cấu hình, không hiện 0 do vi-VN. Entity/DTO danh sách/xem nhanh chưa có ngày hết hạn; giữ trạng thái Expired hiện có, không dựng thêm ngày hoặc quy tắc.

**Đạt khi:** hồ sơ không làm lộ đơn hoặc giá ngoài quyền; số dư cọc, nợ, điểm và voucher khớp dữ liệu nguồn. Chia các tiểu bước hồ sơ, nhà cung cấp, công nợ/cọc và ưu đãi. Đối chiếu bản in ở UI-22 và dùng `reward-settings.browser.cjs` khi thay phần thưởng.

### UI-16 Hóa đơn và tích hợp hóa đơn

- [x] UI-16a: danh sách, dashboard và chi tiết hóa đơn theo T01/T02; trạng thái phát hành, người mua và tổng tiền rõ.
- [x] UI-16b: các modal dòng thủ công, hóa đơn nháp từ đơn và lịch sử sửa; biểu mẫu dài toàn màn hình và kiểm tra tổng trước ghi.
- [x] UI-16c: điều chỉnh hóa đơn và dữ liệu tồn liên quan; quyền giá vốn, khóa và điều kiện phát hành giữ nguyên.
- [x] UI-16d: dashboard Viettel, payload đã làm sạch, cấu hình nhà cung cấp và đồng bộ; tác vụ có tiến trình và lỗi theo đợt.
- [x] UI-16e: phát hành tự động và hàng đợi; lý do chưa xử lý trước; không biến tác vụ layout thành phát hành thật.
- [x] UI-16f: log và màn dọn log tách ngữ cảnh; giới hạn đọc, masking và xác nhận giữ nguyên.

**Đạt khi:** view thể hiện đúng khả năng thao tác từ máy chủ; tổng tiền, thuế, nguồn hàng và XML không đổi nghĩa. Chia thành danh sách/chi tiết, nhập/sửa chứng từ, tích hợp/đồng bộ và log. Chạy các ca `invoice-order-draft`, `invoice-input-stock` và kiểm tra hóa đơn liên quan; liên kết trang khách được nghiệm thu thêm ở UI-23.

### UI-17 Ngân hàng và đối soát

- [x] UI-17a: tra cứu ACB ở trang riêng và nhúng; đơn và số tiền cần đối chiếu trước, giao dịch và bằng chứng mở sâu.
- [x] UI-17b: đối soát theo kỳ/nguồn; khoản khớp, chưa khớp và chênh lệch rõ; giữ liên kết đơn nguồn.
- [x] UI-17c: thông báo ngân hàng trực tiếp; cập nhật không giật danh sách hoặc tự đổi giao dịch đang chọn.
- [x] UI-17d: tài khoản cửa hàng, callback routing và cấu hình ACB; trạng thái, masking và xác nhận đúng phạm vi.
- [x] UI-17e: lỗi kết nối, phản hồi chậm, giao dịch trùng và quầy thiếu quyền; quay lại đơn giữ ngữ cảnh.

**Đạt khi:** mọi tác vụ thu hoặc đối soát vẫn dựa vào bằng chứng và service hiện có; không tự ghi nhận tiền từ hiệu ứng UI. Chỉ dùng môi trường và dữ liệu kiểm thử phù hợp; giữ hành vi POS đã kiểm tra ở UI-06.

### UI-18 Báo cáo và chi phí vận hành

- [x] UI-18a: bộ lọc kỳ, so sánh và phạm vi dùng chung; tóm tắt bộ lọc đang áp dụng; quay lại drilldown giữ điều kiện.
- [x] UI-18b: tổng quan kinh doanh và chi tiết bán hàng; đủ các nhóm tổng quan, đơn, sản phẩm, giảm giá, trả hàng và void.
- [x] UI-18c: lợi nhuận và điều chỉnh giá vốn; nhãn tạm tính, thiếu nguồn và số không khả dụng giữ đúng ý nghĩa.
- [x] UI-18d: các section sản phẩm, danh mục, khách và nhóm khách trong workspace; có lựa chọn và bảng tương đương cho biểu đồ.
- [x] UI-18e: thu chi, tồn kho, công nợ và tuổi nợ trong OperationsWorkspace; giữ phạm vi và định nghĩa từng chỉ số.
- [x] UI-18f: sổ chi phí, biểu mẫu nháp và ghi nhận/hủy theo luồng hiện hành; ngày ghi nhận, thanh toán và lý do rõ.
- [x] UI-18g: xuất dữ liệu giữ đúng kỳ và quyền; kiểm tra giới hạn dữ liệu, tải chậm và kết quả rỗng.

**Đạt khi:** các luồng D01 đến D09 đều có bằng chứng; chỉ số và tổng xuất khớp nguồn; dữ liệu tạm tính không hiển thị như số đã chốt. Dùng `management-reports`, `management-report-loading`, `operations-reports` và các kiểm tra báo cáo hiện có. Giữ cách tải tiết kiệm của overview; không tải toàn bộ chi tiết chỉ để làm thẻ mobile.

### UI-19 Khuyến mãi và nội dung trình chiếu

- [x] UI-19a: danh sách và chỉnh chương trình khuyến mãi; điều kiện, hàng áp dụng, quyền lợi và thời gian chia phần.
- [x] UI-19b: nội dung trình chiếu; ảnh, lịch và thứ tự; chọn nội dung rồi sửa, xem trước theo mục đích thiết bị.
- [x] UI-19c: tem khuyến mãi; chọn chương trình, sản phẩm, khổ và máy in; xem trước trước gửi lệnh.
- [x] UI-19d: kiểm tra trạng thái hết hạn, dữ liệu không hợp lệ và quyền; khớp điều kiện thực tế POS áp dụng.

**Đạt khi:** thông tin ưu đãi không đổi cách tính; trình chiếu và tem đúng nội dung, khổ và điều kiện. Dùng `promotion-labels.browser.cjs`; luồng in hoàn chỉnh tiếp tục được đối chiếu ở UI-22.

### UI-20 Chăm sóc khách hàng trợ lý và media

- [x] UI-20a: hộp thư từ danh sách sang cuộc trò chuyện toàn màn hình; chủ hội thoại, chưa đọc, chuyển người và trạng thái gửi rõ.
- [x] UI-20b: hồ sơ khách và media mở đúng ngữ cảnh; bàn phím, tin mới và upload không che vùng soạn.
- [x] UI-20c: kênh liên hệ và cấu hình; trạng thái kết nối, phạm vi cửa hàng và thông tin tài khoản giữ che.
- [x] UI-20d: chất lượng CSKH, kiến thức và chất lượng trợ lý; tìm trường hợp và mở bằng chứng trước duyệt hoặc đề xuất.
- [x] UI-20e: trợ lý nổi dùng cùng quy tắc layer và dock; ngữ cảnh, phản hồi và các luồng xác nhận giữ nguyên.
- [x] UI-20f: thư viện media và màn kiểm tra upload; ảnh, chọn nhiều và tiến trình; giữ cơ chế tải file và quyền truy cập.

**Đạt khi:** không gửi lặp, mất nội dung đang soạn hoặc lộ hội thoại/file khác scope; ứng dụng dùng được khi bàn phím mở. Chia theo hộp thư, cấu hình/chất lượng, trợ lý và media. Kiểm tra thực tế iPhone/Android cho các thao tác đính kèm và bàn phím.

### UI-21 Quản trị nhân viên quyền và thiết bị

- [x] UI-21a: nhân viên, nhóm quyền và biểu mẫu tạo/sửa theo T01/T03; trạng thái và ảnh hưởng tài khoản rõ.
- [x] UI-21b: phân quyền theo T08; tìm chức năng, nhóm đọc/ghi, tổng kết thay đổi và xác nhận bảo mật hiện có.
- [x] UI-21c: quầy POS và khóa thiết bị; tạo, thời hạn, hiển thị một lần hoặc thu hồi theo chính sách hiện hành.
- [x] UI-21d: kiosk management và activation; mã ghép dễ đọc, scope và thời hạn rõ.
- [x] UI-21e: audit log có lọc và chi tiết đã làm sạch; giới hạn truy vấn giữ nguyên.

**Đạt khi:** chỉnh hiển thị menu và cấp quyền vẫn là hai chức năng độc lập; challenge, masking, giới hạn và audit được giữ. Dùng `role-permissions.browser.cjs` và các kiểm tra bảo mật/quầy liên quan.

### UI-22 In ấn và màn hình chuyên dụng

- [x] UI-22a: kiểm kê các route in thực tế; ưu tiên `ReceiptTemplates/Print.cshtml` đang được POS/POSReceipt dùng; ghi kết luận cho bản in cũ.
- [x] UI-22b: mẫu in, tem, phiếu cọc/nợ, bill giao, đơn mua và phiếu ca: điều khiển mobile gọn; giữ khổ, tỷ lệ, QR/barcode và dấu phân trang.
- [x] UI-22c: trình sửa mẫu in theo nhóm; xem trước có mức phóng rõ; không co nhỏ nội dung in đến mức khó đọc để vừa màn hình.
- [x] UI-22d: xác minh vòng đời in, retry, máy in và transport; giữ QZ hoặc helper theo cách đang hỗ trợ.
- [x] UI-22e: màn khách và kiosk giữ trải nghiệm chuyên dụng; kiểm tra chuyển trạng thái giỏ, QR, xác nhận và ngắt kết nối.
- [x] UI-22f: StoreMonitor giữ chế độ màn lớn và cảnh; phần điều khiển từ điện thoại thích ứng; thay bundle qua nguồn/build tương ứng thay vì sửa file sinh ra.

**Đạt khi:** bản in thực tế hoặc PDF kiểm tra giữ khổ và dữ liệu; thao tác mở/in dùng được trên thiết bị hỗ trợ. LabelPrintClient/Server tương thích với hợp đồng hiện có. Dùng các bộ `receipt-templates`, `pos-print-helper`, `pos-helper-busy-queue`, `store-monitor`, `store-monitor.scenes` và ca in liên quan.

### UI-23 Trang công khai và dự phòng

- [x] UI-23a: thông tin người mua hóa đơn ở `/invoice-info/{token}` theo T03; trạng thái nhập, lỗi, đã tiếp nhận và liên kết không khả dụng rõ.
- [x] UI-23b: chat khách theo T07; nhập tin, đính kèm, bàn phím và trạng thái hội thoại; hợp với UI-20.
- [x] UI-23c: trang vào hệ thống, Privacy và lỗi ở Views gốc; giữ route/HTTP và mục đích hiện hành; ngôn ngữ thống nhất.
- [x] UI-23d: layout và validation của trang công khai có phạm vi riêng; không mang menu quản trị hoặc dữ liệu nhân viên sang trang khách.

**Đạt khi:** khách hoàn tất tác vụ trên điện thoại; token hết hạn/đã khóa và lỗi không lộ dữ liệu nội bộ. Dùng `invoice-buyer-self-service.browser.cjs` và kiểm tra CSKH liên quan. File controller nằm trong thư mục Admin không được dùng làm căn cứ đưa trang người mua vào khung Admin.

UI-23 đã kiểm thử tự động: 79 ca .NET duy nhất; 19 trạng thái × 7 kích thước phone và 38 đối chiếu desktop. Chat web công khai hiện chỉ có tin nhắn chữ; đã rà và giữ phần tệp của nhân viên ở UI-20, không thêm API tải lên trong bước giao diện này. Còn thiết bị thật. [Báo cáo UI-23](MOBILE-UI-23-COMPLETE-20261028.md).

### UI-24 Nghiệm thu và triển khai từng đợt

- [x] UI-24a: quét lại toàn bộ view và asset; đối chiếu danh mục baseline, file mới, đổi tên và workspace động; mọi file có kết luận rõ.
- [ ] UI-24b: chạy luồng liên module và ma trận quyền/thiết bị; xác minh desktop và tablet bên cạnh mobile.
- [ ] UI-24c: kiểm tra tốc độ và tải dữ liệu theo baseline thực tế; số request, kích thước phản hồi và thời gian thao tác không tăng vô lý do nhân đôi UI.
- [ ] UI-24d: so sánh bản in, tổng tiền, số lượng, trạng thái, đồng bộ và dữ liệu khi quay lại hoặc mở tab khác.
- [x] UI-24e: đóng gói Web, Razor và CSS/JS cùng phiên bản; cache busting nhất quán; migration cấu hình UI nếu có được kiểm tra riêng và không tạo menu.
- [ ] UI-24f: triển khai theo nhóm module hoặc phạm vi thử đã thống nhất; theo dõi lỗi và thao tác thực tế trước mở rộng. Khi có yêu cầu publish, dùng quy trình triển khai hiện hành.
- [x] UI-24g: chuẩn bị gói quay lui của Web và assets; kiểm tra tương thích cấu hình mới với phiên bản trước; không tự đảo dữ liệu nghiệp vụ để quay lui UI.

UI-24: phần tự động và triển khai cục bộ đã đạt (628 ca .NET, 721 Node, 253 trạng thái chuẩn/1771 phone/506 desktop/234 print; 24 PDF; release WebOnly và ZIP rollback). Các mục b/c/d/f còn mở cho thiết bị/in thật, dữ liệu lớn/tải host và rollout server thực tế. Không coi máy ảo/headless là nghiệm thu phần cứng. [Báo cáo UI-24](MOBILE-UI-24-ACCEPTANCE-20261029.md).

**Đạt khi:** các tiểu bước trong đợt có bằng chứng, không còn lỗi chặn thao tác hoặc sai dữ liệu; mọi mục chưa đạt có xử lý cụ thể trước mở rộng phạm vi. Hoàn thành toàn dự án khi 260 file baseline và mọi file mới đều được nâng cấp, giữ có lý do hoặc xác nhận thay thế, cùng các luồng động và thiết bị liên quan.

## 7 Ma trận nghiệm thu

### 7.1 Kích thước và thiết bị

| Nhóm | Phạm vi kiểm tra | Điểm cần xem |
|---|---|---|
| Điện thoại nhỏ | 320 và 360 CSS px | Nhãn dài, số lớn, nút chạm, tràn ngang |
| Điện thoại phổ biến | 390 và 430 CSS px | Luồng đầy đủ, sheet, dock và điều hướng |
| Tablet | 768 và 1024 CSS px | Bố cục hai vùng, chạm và xoay |
| Desktop | 1280 hoặc 1440 CSS px | Bảng, công cụ quản lý, bàn phím và máy quét |
| Thiết bị thật | Safari iPhone và Chrome Android | Bàn phím, camera, safe area, file đính kèm và quay lại |
| Truy cập | Bàn phím, zoom phù hợp, giảm chuyển động | Focus, nhãn, thứ tự và thông báo trạng thái |

Không bắt buộc chạy mọi ca ở mọi kích thước sau mỗi sửa nhỏ. Mỗi tiểu bước kiểm tra luồng chính trên mobile và desktop; các điểm biên và thiết bị thật được kiểm tra khi thay tương tác liên quan. UI-24 hoàn tất các ca còn lại cho đợt phát hành.

### 7.2 Vai trò và trạng thái

| Chiều kiểm tra | Ca tối thiểu |
|---|---|
| Quyền | Quản lý, thu ngân, kho, giao hàng, kế toán hoặc quản trị theo tài khoản thử có quyền thực tế; không có quyền đọc/ghi/duyệt |
| Menu | Có menu, menu trống, có/không ghim, ẩn theo nhóm/người, mất quyền, mục ngừng hoạt động |
| Dữ liệu | Rỗng, một dòng, nhiều dòng, tên rất dài, nhiều quy cách, số lượng lẻ, số tiền lớn |
| Chứng từ | Từng trạng thái và chuyển trạng thái service thực sự hỗ trợ; đang bị sửa ở tab khác |
| Mạng | Tải chậm, lỗi đọc, lỗi trước gửi, mất phản hồi sau ghi, thử lại và mở lại trang |
| Tác vụ | Nhấn lặp, hủy sheet, quay lại, đổi bộ lọc, đổi cửa hàng/tài khoản, xoay máy |
| Tính đúng | Tiền, thuế, cọc, nợ, ưu đãi, số lượng, quy cách và đối soát khớp dữ liệu nguồn |

Màn mobile không được dùng dữ liệu mẫu trong demo làm quy tắc nghiệp vụ mới. Lựa chọn tiến độ, trạng thái và tổng hợp phải dựa trên hợp đồng của màn thật.

## 8 Các điểm cần quyết định trong lúc thực hiện

| Điểm | Bước giải quyết | Hướng xử lý |
|---|---|---|
| Nơi lưu cấu hình ghim | UI-02 | Tham chiếu menu có sẵn, thứ tự rõ; không nhân bản cây menu |
| Dữ liệu Home | UI-03 | Dùng read model có quyền; nếu thiếu endpoint thì lập mục bổ sung rõ ràng |
| View cũ đã đổi route | Bước sở hữu | Kiểm tra runtime và loader trước kết luận giữ hoặc dọn |
| Route BarcodeManager thiếu view tương ứng | UI-14 | Đã xác minh route thiếu Index view và partial không có owner. Dùng luồng quy cách/đơn vị/barcode đang hoạt động; route độc lập chưa sửa, giữ thành lỗi nguồn riêng, không tự tạo menu. |
| Bảng đối chiếu nhiều cột | Từng module | Dùng tóm tắt mobile và vùng đối chiếu riêng khi cần |
| Hợp đồng giao dịch và thử lại | UI-05 đến UI-17 | Giữ cơ chế máy chủ; chỉ thay cách mở và hiển thị trạng thái |
| Thiết bị in và kiosk | UI-22 | Kiểm tra thiết bị/hệ điều hành hỗ trợ thực tế trước công bố khả năng |
| Hiệu năng | Mỗi bước và UI-24 | So sánh cùng dữ liệu trước sau; giữ phân trang và cách tải theo nhu cầu |

## 9 Điểm bắt đầu cho lượt triển khai tiếp theo

UI-24 đã hoàn tất kiểm tra tự động và rollout HTTPS cục bộ. Tiếp theo là nghiệm thu host/thiết bị ở UI-24b/c/d/f theo báo cáo UI-24: Android/iOS, camera/scanner/in/két, dữ liệu lớn/tải và máy chủ thực tế. Không tự publish hoặc chạy migrator/seed trên cửa hàng thật. Các ghi nhận lịch sử giữ trong nhật ký và progress JSON; thiếu nghiệm thu thật không được tự chuyển thành đã đạt.

## 10 Nhật ký thực hiện

| Ngày | Bước | Kết quả | Bằng chứng | Phần tiếp theo |
|---|---|---|---|---|
| 25/10/2026 | UI-00 | Hoàn thành kiểm kê tĩnh, bố cục đích theo view và kế hoạch thực hiện | Danh mục và JSON liên kết ở đầu tài liệu | UI-01a và UI-01b |
| 25/10/2026 | UI-01a–b | Hoàn thành bộ nền, thành phần, trang xem thử và ma trận 4 kích thước | Báo cáo UI-01 | UI-01c–e |
| 25/10/2026 | Bổ sung mẫu UI-01 | Trang so sánh desktop/điện thoại; thanh menu đáy theo ảnh người dùng | Báo cáo so sánh thiết bị | UI-01c–e; UI-02 nối cấu hình thật |
| 25/10/2026 | Thiết kế lại điện thoại v2 | Năm màn riêng; navy/lam; danh sách, giỏ, kiểm hàng, sheet và motion mới; kiểm tra 20 bố cục | Báo cáo mobile v2 và thư mục evidence/mobile-v2 | Đánh giá v2; UI-01c–e; UI-02 nối cấu hình thật |
| 25/10/2026 | Duyệt v2 và triển khai UI-02a–b | Người dùng chốt mẫu; hoàn thành hợp đồng UI-01c/e; nâng partial menu thật và giữ nguồn cấu hình/quyền | Báo cáo UI-02a–b, integration contract và implementation-progress.json | UI-02c; UI-01d còn nghiệm thu |
| 25/10/2026 | UI-02c, chờ tự kiểm tra | Đã viết mã ghim tối đa bốn ID theo cửa hàng, sắp xếp/xem trước; chỉ hiện trên điện thoại, giữ desktop | Báo cáo UI-02c; chưa chạy build/test/app/migration theo yêu cầu | Người dùng chạy Visual Studio; sau nghiệm thu đến UI-02d |
| 25/10/2026 | Sửa môi trường và migration theo yêu cầu | Cài SDK 10.0.402/runtime 10.0.13, build Web 0 lỗi/cảnh báo; backup rồi cập nhật local 79→81, schema khớp, tám bảng đối chiếu không đổi | Báo cáo UI-02c environment fix và bằng chứng local | Người dùng đóng/mở Visual Studio, F5 tự kiểm tra |
| 25/10/2026 | UI-02d, chờ tự kiểm tra | Quản lý menu trên điện thoại; thêm/sửa có bản xem lại; chọn đối tượng/chỉnh/xem trước cho ẩn hiện; giữ desktop và backend | Báo cáo UI-02d, đối chiếu source và kiểm tra cú pháp JS; chưa build/run/test | Người dùng chạy Visual Studio; sau phản hồi đến UI-02e |
| 25/10/2026 | UI-02e, chờ tự kiểm tra | POS tabs và lối tắt dùng cây menu đã lọc; ghim trang chủ theo UI-02c; không đổi CSS desktop hoặc backend nghiệp vụ | Báo cáo UI-02e; 12 file bảo vệ giữ hash, hai header giữ phần ngoài menu, JS đạt cú pháp; chưa build/run/bộ test | Người dùng chạy Visual Studio; sau phản hồi đến UI-02f |
| 25/10/2026 | UI-02f, chờ tự kiểm tra | Sửa cây sâu/tên dài trên điện thoại, dropdown cuộn và phản hồi ghim cũ; chuẩn bị ca quyền/cửa hàng/cây biên | Báo cáo UI-02f; 24 file bảo vệ giữ hash, JS đạt cú pháp, 22 ca unit test dự kiến và 22 ca runtime chưa chạy | Người dùng chạy Visual Studio; sau phản hồi đến UI-03a |
| 25/10/2026 | UI-03a, chờ tự kiểm tra | Khung điện thoại ở ba layout, header chung/account một node, POS/nhập hàng giữ header/dock riêng; không đổi desktop hoặc nguồn menu/quyền | Báo cáo UI-03a; 19 file bảo vệ giữ hash, 8 Razor chỉ đổi điểm tích hợp, JS đạt cú pháp; chưa build/run/bộ test | Người dùng chạy Visual Studio; sau phản hồi đến UI-03b |
| 25/10/2026 | UI-03b, chờ tự kiểm tra | Safe area/viewport, bàn phím, menu giữ cuộn, focus trong lớp phủ, trợ lý/toast tránh vùng hành động; app nhập hàng dưới 768 px | Báo cáo UI-03b; 21 file bảo vệ giữ hash, 5 file cũ đối chiếu toàn phần ngoài thay đổi khai báo, 4 JS đạt cú pháp, 16 ca runtime chưa chạy | Người dùng chạy Visual Studio; sau phản hồi đến UI-03c |
| 25/10/2026 | UI-03c, chờ tự kiểm tra | Tài khoản/tiện ích hiện có, toast/xác nhận và trang từ chối/lỗi trên điện thoại; giữ desktop, submit, challenge và quyền | Báo cáo UI-03c; 24 file bảo vệ giữ hash, 9 file cũ đối chiếu ngoài thay đổi khai báo, 2 JS đạt cú pháp, 16 ca runtime chưa chạy | Người dùng chạy Visual Studio; sau phản hồi đến UI-03d |
| 25/10/2026 | UI-03d, chờ tự kiểm tra | Tổng quan Home điện thoại dùng báo cáo hôm nay UTC+7 theo quyền/cửa hàng; ghim cùng nguồn, giữ desktop, không thêm API/menu | Báo cáo UI-03d; 23 file bảo vệ giữ hash, 2 file cũ đối chiếu, 2 JS đạt cú pháp, 16 ca runtime chưa chạy | Người dùng chạy Visual Studio; sau phản hồi đến UI-03e |
| 25/10/2026 | UI-03e, chờ tự kiểm tra | Login phone v2, lỗi/bàn phím và native reveal; giữ desktop/auth; thêm local Chrome launcher cho http/https, không đóng browser | Báo cáo UI-03e; 18 file bảo vệ giữ hash, 5 đối chiếu file cũ, 3 JS đạt cú pháp, 18 ca runtime chưa chạy | Người dùng chạy Visual Studio; sau phản hồi đến UI-03f |
| 25/10/2026 | UI-03f, chờ tự kiểm tra | Rà 279 Razor/9 layout; giữ cụm legacy chưa có caller, thêm Scripts cho Blank, chuyển hai Shared/Error trên điện thoại; desktop/menu/quyền/Chrome giữ nguyên | Báo cáo UI-03f, layout-audit, 35 hash bảo vệ, bốn đối chiếu source, 16 ca runtime chưa chạy | Người dùng chạy Visual Studio; sau phản hồi đến UI-04a |
| 25/10/2026 | UI-04a, chờ tự kiểm tra | Danh sách đơn phone T01, phần phụ mở riêng, bộ lọc/preview/nav cấu hình gọn; giữ renderer, số liệu, quyền và DOM desktop | Báo cáo UI-04a; 30 hash bảo vệ, hai đối chiếu source, hai JS đạt cú pháp; 20 ca runtime và browser fixture chưa chạy | Người dùng chạy Visual Studio; sau phản hồi đến UI-04b |
| 25/10/2026 | UI-04a, chạy test và đánh giá theo yêu cầu | Build, 10 SQL/quyền/CSP, một test product và Chrome 10 kích thước qua; sửa fixture cũ, thu gọn thống kê/lọc và đặt tiền cạnh khách để thấy trên màn đầu | Báo cáo runtime UI-04a, TRX/log/ảnh; 29 hash nghiệp vụ/nền giữ nguyên; còn thiết bị thật và các ca tổng hợp | UI-04b; nghiệm thu còn lại theo ma trận |
| 25/10/2026 | UI-04b, triển khai và tự test theo yêu cầu | Chi tiết đơn phone T02, ba tab, điều hướng/nội dung cũ và modal thanh toán gọn; desktop/menu/quyền giữ nguyên | Báo cáo UI-04b; build 0 lỗi/cảnh báo, 7 hồi quy, 17 nhóm browser/10 viewport; desktop pixel khớp, 34 hash và snapshot SQL khớp | UI-04c; thiết bị thật/tổ hợp UI-04d còn nghiệm thu |
| 25/10/2026 | UI-04c, triển khai và tự test theo yêu cầu | Thao tác sau bán/thanh toán theo tab và quyền phone; đúng khách trong form phone, giữ desktop/handler/menu | Báo cáo UI-04c; build 0 lỗi/cảnh báo, 12 hồi quy và 26 nhóm browser/10 viewport/bảy quyền; pixel desktop/40 hash/SQL snapshot khớp | UI-04d; thiết bị thật và các tổ hợp còn lại |
| 25/10/2026 | UI-04d, nghiệm thu tự động và sửa lỗi phone | Chín tổ hợp SQL; sửa voucher/số đã hoàn/mật độ lịch sử; giữ desktop/quyền/cấu hình | Báo cáo UI-04d; 25 hồi quy + 7 Razor/CSP, 16 nhóm dữ liệu + 26 nhóm chi tiết và probe danh sách; một khoản hoàn browser đúng một lần; 45 hash khớp | UI-05a; thiết bị thật và UI-02/UI-03 còn riêng |

| 25/10/2026 | UI-05a, triển khai và tự test theo yêu cầu | Danh sách phiếu nhập phone theo kho/HKD, nguồn liên kết đơn mua và trạng thái; giai đoạn xử lý, CTA cũ, modal gọn | Báo cáo UI-05a; 7 hồi quy, 28 nhóm browser/10 viewport, desktop pixel/DOM và snapshot SQL khớp, 60 hash bảo vệ | UI-05b; thiết bị thật và UI-02/UI-03 còn riêng |

| 25/10/2026 | UI-05b, triển khai và tự test theo yêu cầu | Chi tiết phiếu phone T04, quét/tìm/feedback, metadata đúng tab, thanh hành động/navy; sửa vùng vuốt, nhãn chỉ xem và cuộn đầu phiếu | Báo cáo UI-05b; 18 hồi quy, 23 nhóm browser/10 viewport, probe cuộn cũ; desktop pixel/DOM, SQL bảo vệ và 72 hash khớp | UI-05c; thiết bị thật còn riêng |

| 25/10/2026 | UI-05c, triển khai và tự test theo yêu cầu | Số lượng gắn quy cách, form hàng mới/ảnh/ghi chú phone, tóm tắt dòng/mục và đúng tác vụ theo quyền | Báo cáo UI-05c; 26 hồi quy, 18 nhóm browser/10 viewport, 23 nhóm UI-05b, probe cuộn; desktop pixel/form và SQL khớp, 88 hash bảo vệ | UI-05d; thiết bị thật còn riêng |

| 25/10/2026 | UI-05d, triển khai và tự test theo yêu cầu | Command/payload chờ, xác nhận nhật ký và dòng hiện tại, thử lại số lượng sau mất phản hồi | Báo cáo UI-05d; 35 hồi quy, 17 nhóm browser, toàn luồng, tương tác gộp và cuộn; desktop pixel, SQL và 87 hash bảo vệ | UI-05e; thiết bị thật còn riêng |

| 25/10/2026 | UI-05e, kiểm tra chấp nhận và sửa lỗi số lẻ phone | Ba nút bước dưới 1, quét lặp, cùng tên đơn vị, offline/retry, chỉ xem/quyền/cửa hàng, viewport/camera/bàn phím mô phỏng | Báo cáo UI-05e; 50 hồi quy, 28 nhóm browser/10 viewport, 17 nhóm ack và ba probe gốc; desktop pixel/DOM/cách tính, SQL và 95 hash bảo vệ | UI-06a; UI-05 còn nghiệm thu Android/iOS thật |

| 25/10/2026 | UI-06a, đầu vào POS và tự test | Header/ca/quầy, tìm/quét/chọn hàng phone; sửa nút chồng/tràn và cuộn giỏ màn thấp | 49 hồi quy, 16 nhóm browser/10 viewport, hai probe tìm/barcode; desktop CSS/kích thước/DOM, SQL và 191 hash bảo vệ | UI-06b; còn thiết bị/máy quét thật |

| 25/10/2026 | UI-06a camera, theo yêu cầu bổ sung | Camera sau/đèn/quét lại, một mã mỗi lần mở; handoff đúng scan owner, dừng media và chặn phiên cũ | 49 hồi quy, 16 nhóm camera + 16 nhóm POS; SQL/quyền/menu/desktop và 197 hash bảo vệ | UI-06b; còn camera/thiết bị thật |

| 25/10/2026 | UI-06b, giỏ/khách/công cụ POS | Thẻ giỏ/quy cách, tác vụ dòng, bảng giá đủ cột dạng thẻ, khách/điểm/voucher/ghi chú/giảm giá; sửa selector Bảng giá gốc | 57 hồi quy + 3 JS, 14 nhóm giỏ + 16 nhóm POS, SQL, desktop và 203 hash bảo vệ | UI-06c; còn điện thoại thật |


| 26/10/2026 | UI-06c, thanh toán POS | Tổng/phương thức/số thu/tiền thừa, footer nguyên nút gốc, lỗi/thử lại và hoàn tất | 59 hồi quy, 7 nhóm HTTP/SQL, probe tiền gốc + 7 nhóm phản hồi; 14 phone/2 bàn phím/5 desktop, 206 hash | UI-06d; còn thiết bị thật |


| 26/10/2026 | UI-06d, QR và đối chiếu ngân hàng POS | QR/history, chờ/kiểm tra thủ công, bằng chứng người chuyển, lỗi/thử lại nguyên owner | 394 hồi quy, 7 nhóm browser/SQL, 28 phone/4 viewport mô phỏng/5 desktop; 211 hash | UI-06e; còn thiết bị/ngân hàng thật |

| 26/10/2026 | UI-06e, phiên/offline/giao hàng POS | Trạng thái và tác vụ offline, khóa native, giao hàng phone, giữ menu màn khách | 358 hồi quy + 111 JS; 9 nhóm browser/SQL, 42 phone/2 viewport/6 desktop; 219 hash | UI-06f; còn thiết bị thật |

| 26/10/2026 | UI-06f, audit ghi chú/giảm giá POS | Một parent/hai fragment canonical; giữ toàn bộ HTML/CSS/JS/owner | 67 hồi quy, 8 browser/SQL, 21 phone/5 desktop/33 scan ID; 7 feedback và probe tiền gốc, 225 hash | UI-07a; còn thiết bị thật |

Cập nhật bảng tiến độ, mục công việc của tiểu bước và trạng thái trong `implementation-progress.json` sau mỗi lượt. `view-inventory.json` giữ nguyên làm baseline; kết quả triển khai và hash sau thay đổi ghi riêng để tránh đánh dấu một màn hoàn thành chỉ vì đã thay layout.


| 26/10/2026 | UI-07a, ca hiện tại và mở/nhận/đóng ca | Phone một cột, kiểm đếm/nháp giữ owner gốc; desktop giữ nguyên | 48 .NET + 20 JS, 6 nhóm browser/SQL, 21 phone/10 desktop, 298 hash; 3 probe gốc | UI-07b; còn thiết bị thật |

| 26/10/2026 | UI-07b, lịch sử ca và phiếu bàn giao | Phone dạng thẻ, chi tiết ca riêng, lọc quầy hiện hữu; desktop/quyền giữ nguyên | 48 .NET + 20 JS, 7 browser/SQL, 30 phone/7 desktop, 305 hash, probe bàn giao/in gốc | UI-07c; còn thiết bị thật |

| 26/10/2026 | UI-07c, đối soát và duyệt ca | Phone rõ quỹ/chênh lệch, hàng đợi trước, form duyệt vừa viewport | 48 .NET + 20 JS, 5 browser/SQL, 26 phone/9 desktop/35 scan ID, 315 hash; hai probe gốc | UI-07d; còn thiết bị thật |

| 26/10/2026 | UI-07d, dashboard POS | KPI phone hai cột, quỹ/cảnh báo, nhóm chi tiết; chặn resize vẽ lại snapshot ca cũ | 52 .NET + 20 JS, 5 browser/SQL, 12 phone/3 desktop/15 scan ID, 319 hash; SQL read-only | UI-08a; còn thiết bị/helper két ngoài |

| 26/10/2026 | UI-08a, yêu cầu nghiệp vụ | Phone trạng thái dạng thẻ, tạo/duyệt riêng, native form và footer gốc | 65 .NET + 20 JS, 5 browser/SQL, 25 phone/10 desktop/36 scan ID, 362 hash; probe requests gốc | UI-08b; còn thiết bị thật |

| 26/10/2026 | Toàn UI-08a/b/c/d | Hàng chờ nhập, chứng từ cũ; rà lại requests, duplicate/stale/quyền/đổi ca | 79 .NET + 31 JS, 11 browser/SQL, 55 phone/16 desktop/72 scan ID, 410 hash; ba probe gốc | UI-09a; còn thiết bị thật |

| 26/10/2026 | Toàn UI-09a/b/c/d | Soạn hàng, xếp chuyến, bàn giao hàng/tiền và kiểm tra stale/retry/quyền/bill | 304 .NET + 107 JS, 26 browser/SQL, 70 phone/20 desktop/90 scan ID, 553 hash | UI-10a; còn thiết bị thật |

| 26/10/2026 | Toàn UI-10a/b/c/d/e | Đề nghị mua, duyệt/chuẩn bị, đơn mua và nhận hàng; sửa cách đọc số lẻ của form | 593 .NET + 37 JS, 7 browser/SQL, 105 phone/30 desktop/27 hộp thoại, 796 hash | UI-11a; còn thiết bị thật |

| 26/10/2026 | Toàn UI-11a/b/c/d/e/f | Phiếu nhập, hàng cần hoàn thiện, duyệt thương mại/XML, tách phiếu, lịch sử và thư viện hóa đơn | 993 .NET duy nhất +143 JS; 8 CLI +4 browser/SQL release, 133 phone/38 desktop/48 hộp thoại; 84 lượt quyền thư viện; 1.304 hash | UI-12a; còn thiết bị thật |
| 26/10/2026 | Toàn UI-12a/b/c/d/e | Kiểm kê, chuyển kho, điều chỉnh, case tồn âm theo đơn và rà legacy | 177/177 .NET; 2 luồng browser/SQL, 22 trạng thái, 154 phone/44 desktop/42 hộp thoại; 3.488 hash | UI-13a; còn thiết bị thật |
| 26/10/2026 | Toàn UI-13a/b/c/d | Tra cứu tồn, thẻ kho, quản lý kho, pháp nhân và đối soát | 47/47 .NET duy nhất; probe sổ kho gốc và browser/SQL 5 trang qua; 119 phone/34 desktop/30 hộp thoại; 1,5 triệu dòng chạy riêng qua; 3.510 hash | UI-14a; còn thiết bị thật |
| 26/10/2026 | Toàn UI-14a/b/c/d/e/f | Giao diện sản phẩm, đơn vị/barcode và sáu danh mục; d chốt kiểm toán R05 | 101/101 .NET duy nhất; probe giá đơn vị gốc và browser/SQL qua; 182 phone/52 desktop/45 hộp thoại; 3.513 hash | UI-15a; còn thiết bị thật và route R05 độc lập |
| 26/10/2026 | Toàn UI-15a/b/c/d/e | Khách hàng, nhà cung cấp, công nợ, cọc và ưu đãi trên điện thoại | 124/124 .NET duy nhất; ba CLI live và probe thưởng từ Razor gốc qua; 203 phone/58 desktop/29 in/27 hộp thoại; 3.537 hash | UI-16a; còn thiết bị thật và hợp đồng ngày hết hạn voucher |
| 27/10/2026 | Toàn UI-16a/b/c/d/e/f | Hóa đơn và tích hợp hóa đơn trên điện thoại | 727/727 .NET duy nhất gồm SQL220k riêng; CLI tồn chứng từ gốc qua; 31 trạng thái/217 phone/62 desktop/31 in/21 hộp thoại; 3.548 hash | UI-17a; còn thiết bị thật |
| 27/10/2026 | Toàn UI-17a/b/c/d/e | Ngân hàng và đối soát trên điện thoại | 461/461 .NET duy nhất; 50 Node và 3 probe gốc qua; 24 trạng thái/168 phone/48 desktop/24 in/12 hộp thoại; paired snapshot, chưa xác minh toàn workspace | UI-18a; còn workspace chung và thiết bị thật |
| 27/10/2026 | Toàn UI-18a/b/c/d/e/f/g | Báo cáo quản trị và chi phí trên điện thoại | 150/150 .NET duy nhất; 16 Node VM và 3 probe gốc qua; 39 trạng thái phân biệt/40 lượt thực thi/280 phone/80 desktop/40 in/33 lượt hộp thoại (11 loại); composite full39 raw giữ nguyên + focused Sales thay một case, nguồn cuối22, paired snapshot, chưa xác minh end-to-end toàn workspace | UI-19a; còn workspace chung và thiết bị thật |
| 28/10/2026 | Toàn UI-19a/b/c/d | Khuyến mãi, nội dung trình chiếu và tem trên điện thoại | 73/73 .NET duy nhất; 9 Node và probe promotion-labels gốc qua; 27 trạng thái/189 phone/54 desktop/27 in/91 lượt hộp thoại (13 loại); paired snapshot | UI-20a; còn workspace chung và thiết bị/in thật |
| 28/10/2026 | Toàn UI-20a/b/c/d/e/f | CSKH, trợ lý và media trên điện thoại | 299/299 .NET duy nhất, 3 Node, 41 trạng thái/287 phone/82 desktop/41 in; current-model schema | UI-21a; còn migration và thiết bị thật |
| 28/10/2026 | Toàn UI-21a/b/c/d/e | Nhân viên, phân quyền, thiết bị POS, kiosk và audit; sửa metadata migration P3 | 149/149 ca .NET duy nhất; 30 trạng thái/210 phone/60 desktop/30 in; hai probe trình duyệt gốc qua; strict full migration | UI-22a; còn thiết bị thật |
| 28/10/2026 | Toàn UI-22a/b/c/d/e/f | In, mẫu/tem, màn khách, kiosk và StoreMonitor; giữ bản in và desktop | 170/170 ca .NET duy nhất, 99 ca Node; 42 trạng thái/294 phone/84 desktop/42 print; 24 PDF đối chiếu; strict full migration | UI-23a; còn thiết bị và máy in thật |

### Bổ sung thanh điều hướng chính, 28/10/2026

Người dùng chọn ưu tiên Tổng quan – Đơn hàng – Nhập hàng – Bán hàng – Menu. Đã bổ sung mặc định trình bày khi chưa có mục ghim khả dụng, SSR ngay khi tải trang và preview theo vai trò trên điện thoại. Không tạo menu/cấu hình, không đổi desktop; giữ ghim riêng. Đây là lựa chọn mới thay quy tắc chỉ hiện Menu khi chưa ghim trước đây. 42 ca .NET và 3 ca Node qua; còn thiết bị thật. [Báo cáo](MOBILE-PRIMARY-NAV-20261028.md). Bước chính tiếp theo vẫn UI-23a.
