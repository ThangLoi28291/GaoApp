# UI-05b — Chi tiết phiếu nhận hàng trên điện thoại

Ngày 25/10/2026. Đã triển khai và kiểm thử tự động UI-05b tại `/admin/warehouse-receiving/{id}`. Bước tiếp theo **UI-05c — số lượng từng quy cách và thông tin hàng cần hoàn thiện**. Android/iOS thật còn nghiệm thu riêng.

## Giao diện đã nâng

Chỉ dưới 768 px, chi tiết phiếu dùng màu navy/lam theo mẫu đã duyệt. Vùng tìm/quét có tiêu đề và ô tìm rõ ràng; thẻ Lần nhận gần nhất giữ tên, mã, thời điểm, số lượng vừa nhận và số hiện trong phiếu theo đúng đơn vị. Danh sách dùng thẻ có ảnh, tên, barcode/quy đổi và bộ điều chỉnh số lượng gốc. Nút Nói / Quét mã / Gửi duyệt, trạng thái lưu và hai tab **Hàng nhập / Thông tin** tiếp tục dùng `_ReceivingApp` hiện có.

Tên phiếu đầy đủ, mã, trạng thái, đường quay lại và các thao tác đổi tên/xóa được đưa vào tab Thông tin bằng cách chuyển chính node gốc. Màn Hàng nhập tập trung vào tìm/quét và hàng thực nhận. Khi về máy tính, các node trở về đúng vị trí cũ; không tạo thêm nút hoặc handler giao dịch.

Tổng SL nhập và tổng SL ở dock được ẩn riêng trên phone vì cộng nhiều quy cách không phản ánh tiến độ có ý nghĩa. Số dòng ở tab và số lượng/đơn vị từng dòng giữ nguyên; không sửa phép tính, payload hoặc dựng phần trăm hoàn tất. Nút, thẻ, sheet thao tác và số lượng dùng cùng màu mới; có reduced motion, forced colors và vùng chạm tối thiểu 44 px cho điều hướng/thao tác được kiểm tra.

## Lỗi phát hiện và sửa

1. Vùng `drag-target` mở sidebar rộng 40 px chặn nút Nói sau khi bố trí dock mới. CSS chỉ trên màn nhận hàng phone giới hạn vùng bắt vuốt giữa header và dock. Giữ thao tác vuốt trong nội dung, đồng thời nút menu và Nói đều bấm được. Menu vẫn do nguồn cấu hình và chủ sở hữu cũ xử lý.
2. Phiếu chờ duyệt có thể hiện “Sẵn sàng nhận hàng” do owner cũ sao chép nhãn mặc định. Phone bổ sung nhãn chỉ xem từ `CanEditReceiving` và trạng thái do Razor trả. Nhãn chỉ thay phần trình bày khi trạng thái owner là `saved`; thông báo lỗi gốc vẫn được hiện. Không ghi trạng thái nghiệp vụ hoặc thay quyền chỉnh sửa. Nhãn được bỏ khi trở về desktop.
3. Ca nhận hàng khi đang ở đầu phiếu bị dịch xuống 154 px. Lượt baseline trước sửa cũng thất bại. Trace xác nhận `restoreListViewport` đang bám dòng cũ sau khi dòng mới được đưa lên đầu. Chỉ thêm nhánh **phone và vị trí đã chụp bằng 0 thì giữ 0** trong owner cuộn hiện có; vị trí phía dưới và toàn bộ desktop giữ phép neo cũ. Giữ nguyên bài test cuộn cũ, không bỏ điều kiện về đầu trang. Sau sửa, nguyên probe đã qua.

Không thay controller/service/repository, quyền, `_ReceivingApp`, capture, feedback, command ID, row version, payload, phương thức ghi hoặc điều kiện gửi duyệt. `warehouse-receiving-app.js` chỉ khác nhánh vị trí cuộn nêu trên; phần còn lại đối chiếu với snapshot trước bước này khớp hoàn toàn. Razor chỉ thêm marker và hai import asset. Không tạo API, menu hoặc migration.

## Kiểm thử và đánh giá

- Build Release Web/Razor/Tests/runner: **0 lỗi, 0 cảnh báo**.
- **18/18 hồi quy**: 7 SQL barcode/quyền/cửa hàng/CSRF/owner lease và review; 6 contract nhận hàng chờ hoàn thiện; 3 xử lý hủy truy vấn; 2 CSP.
- **23 nhóm browser** trên Razor/HTTP/SQL thật, ở 10 kích thước: 1600, 1440, 1366, 1024, 768, 767, 430, 390, 360 và 320 px.
- Qua hai tab, bàn phím trái/phải, thao tác khác, mở/đóng sidebar cấu hình, tên phiếu dài có ký tự HTML, tìm không dấu, chọn hàng và giữ số lượng đang nhập khi đổi phone/desktop.
- Camera dùng khung hình canvas có barcode, chạy decoder thật; stream dừng sau nhận dạng. Thao tác nhận **3 Hộp** gọi đúng một lệnh `intake/known`; xác nhận tên/mã/số lượng từ kết quả máy chủ, không tự bật overlay sau lưu. Thẻ xác nhận giữ dữ liệu khi đổi tab. Giọng nói mô phỏng trả kết quả tìm, vẫn cần người dùng chọn sản phẩm.
- Gửi duyệt bằng nút cũ gọi đúng một `submit-approval`. Trang tải lại thành chỉ xem: 23 dòng, không có dock nhập hoặc nút chỉnh số lượng; thông tin chờ duyệt và điểm mở đề nghị sửa vẫn còn. Phiếu trống và phiếu bị trả về dùng đúng dữ liệu, lý do và thao tác hiện có.
- Tài khoản chỉ có quyền xem bị policy tạo nhập hàng gốc chặn bằng JSON 403. Không có quyền mới hoặc nút duyệt của quản lý được thêm vào UI.
- Không tràn ngang ở các viewport, forced colors và chữ 150%. Không lỗi JavaScript/CSP. Probe chính chỉ có một lệnh nhận và một lệnh gửi duyệt.

SQL sau probe chính xác nhận **23 dòng, dòng mới Quantity = BaseQuantity = 3, trạng thái PendingApproval**. Phiếu khác, số dư tồn, posting/công nợ và toàn bộ cấu hình menu được snapshot giữ nguyên. Việc gửi duyệt chưa thực hiện duyệt thương mại/nhập tồn. Fixture cuộn dùng database khác, chỉ thay các dòng thử phục vụ ca tăng/giảm và số lẻ.

Nguyên probe cuộn cũ qua các ca 320/390 px, dòng cuối phiếu dài, tăng/giảm, sheet số lẻ của hàng đã biết và hàng chờ hoàn thiện, người dùng cuộn trong lúc phản hồi chậm, và thứ tự/xác nhận sau nhận mã mới. Log các lần đo tăng/giảm và phản hồi chậm ghi **max frame shift 0 px**. Probe không bị sửa để làm nhẹ điều kiện nghiệm thu.

Đã xem ảnh Hàng nhập, Thông tin, lần nhận gần nhất, phiếu chỉ xem và desktop. Danh sách gọn hơn do metadata nằm đúng tab; trạng thái lưu/phản hồi nhận hàng rõ hơn. Ảnh desktop 1440 px khớp khi bật/tắt stylesheet mới, có cố định animation trong phép chụp. Node/nội dung desktop khôi phục; phép so HTML chỉ chuẩn hóa `style=""` rỗng trên input do thư viện đo kích thước để lại.

**72 file bảo vệ giữ hash**, hai đối chiếu source toàn phần qua. Baseline 560 file giữ nguyên: **521 không đổi, 39 đổi cộng dồn, 0 mất**; các asset mới được ghi trong manifest riêng.

Toàn bộ test chỉ dùng SQL GUID dùng một lần, Web và Chrome headless/profile do fixture sở hữu, tắt launcher Chrome thường. Các fixture đã kết thúc; không đóng hoặc đổi cửa sổ/tab Chrome đang mở, không chỉnh database cửa hàng.

## Phần còn nghiệm thu riêng

Camera và SpeechRecognition trong browser được mô phỏng; chưa kiểm tra camera/micro, bàn phím ảo, safe area và thao tác chạm trên Android/iOS thật. UI-05c tiếp tục số lượng từng quy cách và thông tin hàng; UI-05d/e tiếp tục ma trận command/retry và trường hợp biên. Những ca vật lý/menu/ghim/F5-Stop UI-02/UI-03 chưa được tự động đánh dấu hoàn tất bằng bước này.

## Bằng chứng

- [Runtime review](mobile-ui-20261025/evidence/ui-05b/runtime-review.json), [source review](mobile-ui-20261025/evidence/ui-05b/static-review.json), [ma trận nghiệm thu](mobile-ui-20261025/evidence/ui-05b/acceptance-matrix.json), [baseline delta](mobile-ui-20261025/evidence/ui-05b/baseline-delta.json).
- [18 bài hồi quy](../../TestResults/mobile-ui-05b-20261025/regression/regression.trx), [23 nhóm browser](../../TestResults/mobile-ui-05b-20261025/browser/result.json), [đối chiếu SQL](../../TestResults/mobile-ui-05b-20261025/browser/sql-checks.json), [log cuộn cuối](../../.artifacts/ui-05b/scroll-final.log).
- [Điện thoại 390 px](../../TestResults/mobile-ui-05b-20261025/browser/detail-phone-390.png), [320 px](../../TestResults/mobile-ui-05b-20261025/browser/detail-phone-320.png), [Thông tin](../../TestResults/mobile-ui-05b-20261025/browser/detail-info-390.png), [lần nhận gần nhất](../../TestResults/mobile-ui-05b-20261025/browser/detail-last-received-390.png), [phiếu chỉ xem](../../TestResults/mobile-ui-05b-20261025/browser/detail-readonly-390.png).
- [Máy tính](../../TestResults/mobile-ui-05b-20261025/browser/detail-desktop-current.png), [máy tính khi tắt CSS mới](../../TestResults/mobile-ui-05b-20261025/browser/detail-desktop-baseline.png), [dòng cuối 320 px](../../TestResults/mobile-ui-05b-20261025/scroll-final/scroll-stable-320.png).
