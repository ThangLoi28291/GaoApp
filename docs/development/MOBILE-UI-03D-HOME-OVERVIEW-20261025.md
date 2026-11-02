# UI-03d · Trang chủ tổng quan trên điện thoại

Ngày 25/10/2026. **Đã viết mã và đối chiếu source; chờ người dùng chạy Visual Studio nghiệm thu.** Không chạy app, browser, build, bộ test hoặc migration. UI-02c–f và UI-03a–c tiếp tục chờ nghiệm thu như trước.

## Phần đã áp dụng

Home tại `/admin`, `/admin/home`, `/admin/dashboard` có phần tổng quan riêng **dưới 768 px**: lời chào theo profile đã render, doanh thu thuần trong thẻ navy, hai thẻ Đơn bán/Trung bình mỗi đơn, hoạt động trả hàng/hủy đơn, thời điểm cập nhật và ghim nhanh. Màu, khoảng cách, vùng chạm và chuyển động theo v2; hỗ trợ reduced motion và forced colors trong phạm vi CSS mới.

`Home/Index` giữ nguyên toàn bộ nội dung Home cũ, chỉ thêm điểm gọi partial, hai section nạp asset và thuộc tính đánh dấu phần desktop. Ngoài media query, stylesheet mới chỉ ẩn root mobile; từ 768 px trở lên giao diện Home cũ tiếp tục hiển thị. Các trang POS/báo cáo và khung ứng dụng chung không bị thay.

## Nguồn dữ liệu và quyền

Không tạo API, query doanh thu hoặc chính sách quyền mới. Partial kiểm tra `PermissionCodes.Report.Sales.View`; chỉ khi đạt mới render khu vực số liệu và URL đọc. Quyền mở Home `PermissionCodes.Admin.DashboardView` tại controller giữ nguyên. Người được mở Home nhưng không được xem báo cáo có thông báo giới hạn và các mục ghim/menu được phép; không có URL đọc số liệu trong khu vực mobile của họ.

Adapter gọi **GET** hiện có `SalesExecutiveReportController.GetData`, do `Url.Action` sinh URL. Nguồn này đã bảo vệ bằng `Report.Sales.View`; `SalesReportReadService` lấy `currentStore.StoreId`, repository lọc dữ liệu theo cửa hàng. Không gửi store/user ID hoặc mở rộng quyền từ trình duyệt.

| Trên Home | Trường trong response | Ý nghĩa giữ nguyên từ báo cáo |
|---|---|---|
| Doanh thu thuần | `current.summary.netSales` | `SalesAfterDiscount - Returns`; giữ cả giá trị âm |
| Đơn bán | `current.summary.salesOrders` | Số đơn thuộc tập dữ liệu bán của báo cáo |
| Trung bình / đơn | `current.summary.aov` | Máy chủ tính `SalesAfterDiscount / SalesOrders`, không chia lại từ doanh thu thuần |
| Trả hàng | `returnCount`, `returns` | Số phiếu và giá trị trả hàng; không đổi thành số tiền hoàn |
| Đơn hủy | `voidCount` | Số đơn hủy trong kỳ báo cáo |
| Ngày / Cập nhật | `currentPeriod`, `generatedAtUtc` | Ngày từ máy chủ và thời điểm tạo snapshot |

Không truyền ngày từ đồng hồ máy khách. `SalesReportingPeriodPolicy` tự chọn ngày hiện tại theo **Asia/Ho_Chi_Minh, UTC+7**, toàn cửa hàng/quầy/khách theo mặc định có sẵn. Chỉ thêm `compare=none` để không đọc kỳ so sánh. Ngày và giờ hiển thị lấy từ response, định dạng UTC+7; số tiền hiển thị VND không chữ số thập phân theo UI hiện hành.

Đây là snapshot tại lần tải, có nút Làm mới; không gắn nhãn realtime hoặc tự polling. Adapter chỉ tải khi đang ở breakpoint điện thoại và trang được hiển thị, hoặc khi người dùng bấm Làm mới. Khi quay lại tab hoặc trở về điện thoại, đọc snapshot mới. Không có request báo cáo khi mở Home trực tiếp ở desktop.

## Menu và ghim nhanh

- Partial nhận chính danh sách ghim từ `Navigation.GetPinsAsync` đã có trong Home. Tối đa bốn ID, cùng thứ tự, tên, icon và URL đã qua nguồn menu/quyền/ẩn hiện của cửa hàng. Không tự lấp chỗ trống.
- `gaoapp.mobile-pins.js` chỉ thêm sự kiện đọc `gao:mobile-pins-rendered` sau khi hoàn tất render. Adapter Home chiếu lại **các anchor thật trên thanh ghim**, không tự gọi API ghim, không tạo logic lọc quyền thứ hai và không ghi cấu hình. Sự kiện vẫn phát khi danh sách rỗng để Home xóa các ghim cũ.
- Sau cập nhật ghim, giữ focus trên ID còn tồn tại hoặc về nút Menu hợp lệ. Chưa tải được thanh ghim thì Home giữ danh sách server đã render cho request hiện tại.
- Liên kết Xem báo cáo chỉ hiện nếu có quyền và có mục `/admin/reports/overview` hoặc `/admin/reports/sales` trong snapshot menu đã lọc. Giữ nguyên `Href`, ID, PathBase/query cấu hình; không tự tạo mục hoặc dựng URL thay thế. Dữ liệu được xem theo quyền độc lập với việc có mục menu, còn đường truy cập chức năng dùng menu cấu hình.
- Không có ghim thì hiện trạng thái trống và nút mở menu hiện có. Lời chào đọc tên từ DOM account đã render, không gọi profile service lần hai hoặc sao chép form/tài khoản.

## Tải, lỗi và vòng đời

Yêu cầu dùng cùng origin/cookie, `cache: no-store`, chặn redirect trong fetch và kiểm tra JSON. Mỗi lần tải có mã lượt và AbortController; kiểm tra quyền sở hữu lượt trước và sau khi đọc body. Không nhận phản hồi cũ sau đổi breakpoint, ẩn trang hoặc hủy.

Khi đang tải, hiện skeleton và khóa nút Làm mới. Dữ liệu sai kiểu, thiếu trường, số không hữu hạn, count không hợp lệ hoặc kỳ không phải một ngày không được đổi thành 0. Response hợp lệ toàn 0 được hiển thị là 0 kèm trạng thái chưa có đơn bán/trả/hủy. Trường hợp lỗi/timeout 18 giây không tự thử lại; người dùng chủ động Làm mới.

Khi 401/403, xóa số đã hiển thị, dừng nút đọc tiếp và đưa nút Tải lại trang qua CSP action cũ. Rời điện thoại, ẩn tab hoặc pagehide cũng xóa số/giờ khỏi DOM và hủy GET đang chờ. Không dùng localStorage/sessionStorage, không giữ snapshot cũ sau lỗi, không log dữ liệu. Guard account hiện có tiếp tục xử lý pageshow từ bfcache; adapter không tạo redirect/challenge hay reload phiên thứ hai.

## Các chỉ số bổ sung tách riêng

Không tự suy diễn số đang chờ từ một trang danh sách, số lượng tồn từ nhiều đơn vị hoặc lợi nhuận từ doanh thu. Những mục sau **không thuộc số liệu đang hiển thị ở UI-03d**; chưa tạo endpoint/schema/menu để thực hiện chúng:

| Mục bổ sung | Hợp đồng đọc cần chốt trước khi nối Home | Bước liên quan |
|---|---|---|
| Đơn cần tiếp tục/xử lý | Count theo trạng thái trên toàn tập đã lọc, cùng scope quyền nhân viên/cửa hàng; không lấy số dòng của trang đầu làm tổng | UI-04/06 |
| Phiếu nhập cần xử lý | Summary count Draft/PendingApproval/Rejected theo kho/quyền; API receipts hiện có trả danh sách đầy đủ nên chưa kéo toàn bộ chỉ để đếm trên Home | UI-05 |
| Cảnh báo tồn thấp | Ngưỡng cấu hình, đơn vị gốc và scope kho/quyền phải rõ; cần read model cảnh báo phù hợp | UI-13 |
| Lợi nhuận/dòng tiền | Có nguồn báo cáo riêng nhưng phải giữ quyền `Report.Profit.View` và định nghĩa hiện có; không gộp dưới quyền xem bán hàng | Bước báo cáo tương ứng |

Đây là danh sách mở rộng cụ thể, không phải dữ liệu giả hoặc mục menu chờ sẵn trên giao diện.

## Bằng chứng đã có

[static-review.json](mobile-ui-20261025/evidence/ui-03d/static-review.json) so với `.artifacts/ui-03d/before/`:

- Hai JS `gaoapp.mobile-home.js` và `gaoapp.mobile-pins.js` đạt `node --check`.
- **23 file bảo vệ giữ hash**, gồm Home/report controller, service/repository kỳ ngày và cửa hàng, nguồn menu, account, layout, CSP và module giao dịch.
- **Hai file cũ được so sánh toàn phần** ngoài tích hợp đã khai báo: Home chỉ thêm partial/import/marker; pins chỉ thêm sự kiện sau render. Chuẩn hóa CRLF/BOM/newline cuối file khi so sánh text.
- CSS ngoài breakpoint chỉ ẩn root mới. Partial chỉ có href từ node menu, GET bị chặn trên desktop và khi không có khu vực được cấp quyền; không có request ghi hoặc tham số store/user.
- [Baseline delta](mobile-ui-20261025/evidence/ui-03d/baseline-delta.json): 560 file gốc, **531 không đổi, 29 đổi, 0 mất**, cộng dồn các bước trước. Home vốn đã thay ở UI-02e; ba file mới partial/CSS/JS được ghi riêng, không sửa baseline.

Đây là đối chiếu source/cú pháp, chưa build Razor hoặc đo hiển thị/tải thực tế. [16 ca nghiệm thu](mobile-ui-20261025/evidence/ui-03d/acceptance-matrix.json) đều chưa chạy.

## Bạn chạy Visual Studio để tự test

**Visual Studio → chọn Web → F5**, mở `/admin` ở 320/390/430 px rồi 768/1024/1366 px:

1. Tài khoản có quyền Home và báo cáo bán hàng: đối chiếu cùng ngày/toàn cửa hàng với báo cáo hiện có. Kiểm tra doanh thu âm do trả hàng, số 0 và không phát sinh; AOV theo doanh thu sau giảm giá. Đổi múi giờ máy khách và thử sát ngày mới, dữ liệu vẫn theo UTC+7.
2. Chỉ có quyền Home: không có khu vực/GET số liệu, ghim/menu vẫn đúng. Không có quyền Home: controller tiếp tục từ chối như cũ. Đổi cửa hàng/nhân viên theo luồng hỗ trợ rồi tải lại, số liệu và menu thuộc phiên mới.
3. Dùng Network khi tự test: desktop mở trực tiếp không gọi API Home; điện thoại gọi một GET snapshot, không polling. Thử lỗi mạng, chậm quá 18 giây, 401/403, dữ liệu thiếu/sai định dạng và phản hồi JSON đến muộn sau đổi viewport/ẩn trang; không hiện số 0 giả hoặc số cũ.
4. Thử 0–4 ghim, tên dài, sắp lại và xóa hết; Home và thanh đáy đồng bộ, không tự sinh chức năng. Liên kết báo cáo chỉ có khi mục thật tồn tại, giữ query/PathBase cũ.
5. Desktop giữ Home cũ; Tab/focus, mở Menu/account, trợ lý, xoay máy, zoom và reduced motion vẫn dùng được. POS/nhập hàng/báo cáo mở từ menu giữ các luồng cũ.

Không cần migration. Bước tiếp theo sau phản hồi là **UI-03e: giao diện đăng nhập điện thoại và trạng thái lỗi/bàn phím, giữ xác thực và scope cửa hàng/quầy**.
