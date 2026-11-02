# UI-04c — Thao tác sau bán và thanh toán trên điện thoại

Ngày 25/10/2026. Tiếp tục UI-04b tại `/admin/pos/order-detail/{orderId}`. Bước tiếp theo **UI-04d** kiểm tra các tổ hợp đơn và thiết bị còn lại. Desktop từ 768 px giữ giao diện hiện tại.

## Thay đổi

Trên điện thoại, Hàng hóa có nút trả hàng/hoàn tiền khi đơn và quyền hiện tại cho phép. Thanh toán chứa lịch sử thanh toán, thông tin chuyển khoản và liên kết yêu cầu sửa thanh toán có sẵn. Lịch sử chứa lịch sử sau bán và hàng trả chờ nhập kho. Hủy/hoàn toàn bộ nằm trong phần mở riêng với lời giải thích phân biệt xử lý toàn bộ và một phần hàng.

Adapter chuyển chính các nút/liên kết cũ, giữ địa chỉ, ID đơn, `data-csp-action`, `data-acb-order-id`, listener và form. Không tạo nút thu tiền, API, payload, phép tính hoặc menu mới. Các nhóm/nút chưa được cấp quyền nằm trong vùng `hidden` và `inert` trên phone; từ 768 px trả về node và thứ tự cũ. Khi không có quyền in, thanh điều hướng phone chỉ còn Quay lại.

Controller detail chỉ bổ sung kết quả `IAuthorizationService` vào metadata trình bày. Kiểm tra quyền và vòng đời ở các controller/service đích giữ nguyên. Không dùng tên vai trò hoặc quyền do client tự đặt.

| Thao tác phone | Quyền/hành vi hiện có được sử dụng |
|---|---|
| Trả hàng/hoàn tiền, hoàn toàn bộ | Cả `pos.order.refund` và `pos.payment.refund`, kèm điều kiện đơn cũ; eligibility/submit tiếp tục kiểm tra tại máy chủ |
| Hủy đơn | `pos.order.void` và điều kiện đơn cũ |
| In hóa đơn | `pos.order.reprint` |
| Thông tin chuyển khoản | `pos.payment.view`, đơn có chuyển khoản theo Razor cũ |
| Mở trang yêu cầu sửa thanh toán | `pos.shift.view`; tạo/xử lý yêu cầu vẫn theo quyền và service của trang đích, không thêm quyền |
| Hàng trả chờ nhập kho | `inventory.stockdocument.approve` qua điều kiện Razor cũ |
| Lịch sử thanh toán/sau bán | Dữ liệu hiện có của detail theo `pos.order.view`, không mở rộng DTO hoặc quyền |

Payload eligibility cũ không chứa tên khách. Renderer chỉ thêm marker gắn với ID đơn, không đổi nội dung hoặc xử lý nghiệp vụ. Khi ở phone, adapter điền tên từ phần khách hàng của receipt đang hiển thị vào ô tên khách hậu mãi của đúng đơn; dùng `textContent`, không ghép HTML. Khi về desktop, khôi phục text cũ và tắt observer. Lý do/số lượng/phương thức/giá trị form không bị adapter đọc hoặc sửa.

## Kiểm thử và đánh giá

- Build Release Web/Razor/Tests/runner: **0 lỗi, 0 cảnh báo**.
- **12/12** bài hồi quy: bốn SQL trả/hoàn thực tế, một trang yêu cầu nghiệp vụ, năm Razor/đơn giao và hai CSP. Các bài trả/hoàn kiểm tra tiền, tồn kho, quyền, lặp giao dịch và xử lý hàng chờ nhập kho trong database fixture riêng.
- **26 nhóm kiểm tra browser**: 10 viewport từ 320 đến 1600 px; tab/keyboard, node gốc/resize, ảnh desktop 1440 px khớp, context thanh toán/ID liên kết, bốn modal, tên khách phone/khôi phục desktop, giữ lý do đang nhập khi resize, lỗi tải 503 khóa lưu, đóng trong khi tải, đơn trống và đơn giao nguồn.
- Bảy tổ hợp quyền kiểm tra qua Web/SQL thật và phone ở 390/320 px: chỉ xem; chỉ quyền hoàn đơn; chỉ quyền hoàn thanh toán; đủ cặp quyền hoàn; chỉ hủy; công cụ thanh toán/in; chỉ duyệt nhập hàng trả. Nút desktop cũ vẫn hiện theo cách cũ sau resize. Các POST hoàn/trả thiếu quyền, POST hủy thiếu quyền và các trang đích không được phép vẫn trả 403.
- Browser không có lỗi JS/CSP hoặc lệnh tài chính. Snapshot Orders, OrderLines, OrderPayments và AdminMenuItems trước/sau browser khớp. **40 file bảo vệ giữ hash**; ba đối chiếu source chứng minh view chỉ thêm metadata/marker, renderer chỉ thêm một marker, controller chỉ thêm kết quả quyền. Giá, payload, submit, tenant, quyền đích, menu/layout/viewport chung và Chrome launcher không đổi.
- Baseline 560 file: **523 không đổi, 37 đổi, 0 mất**, cộng dồn các bước; không ghi lại baseline gốc.

Đã xem ảnh và thu gọn nhóm trả hàng, đưa công việc chờ nhập kho sang Lịch sử, bỏ mô tả lặp của lịch sử thanh toán trên phone. Thanh điều hướng ẩn hoàn toàn khi có modal/bàn phím để không lộ nút phía dưới lớp phủ. Tổng tiền và ba tab vẫn thấy trên màn nhỏ; phần hàng hóa dài được cuộn như cũ.

Chỉ dùng Web/database GUID dùng một lần và Chrome headless với profile riêng do fixture sở hữu; tắt launcher Chrome thường trong test. Không thay database cửa hàng, menu, migration ứng dụng hoặc đóng cửa sổ/tab Chrome của người dùng.

## Phần còn lại

UI-04d tiếp tục đơn thanh toán một phần/công nợ, voucher có đủ mã, allocation/hóa đơn HKD, lịch sử trả nhiều lần, số lượng/giá trị lớn và các tổ hợp menu/cửa hàng. Browser ở lượt này không bấm ghi giao dịch hợp lệ; nghiệp vụ ghi được kiểm tra bởi bốn bài SQL riêng. Chưa nghiệm thu thao tác ghi toàn luồng bằng điện thoại thật, bàn phím ảo, safe area, phóng chữ hoặc bfcache. Không đánh dấu các bước UI-02/UI-03 hoặc nghiệm thu thiết bị trước đó đã hoàn tất.

## Bằng chứng

- [Source review](mobile-ui-20261025/evidence/ui-04c/static-review.json), [runtime review](mobile-ui-20261025/evidence/ui-04c/runtime-review.json), [ma trận nghiệm thu](mobile-ui-20261025/evidence/ui-04c/acceptance-matrix.json).
- [Kết quả browser](../../TestResults/mobile-ui-04c-20261025/browser-accepted/result.json), [TRX 12 bài hồi quy](../../TestResults/mobile-ui-04c-20261025/regression/regression.trx).
- [Điện thoại 390 px](../../TestResults/mobile-ui-04c-20261025/browser-accepted/detail-phone-390.png), [thẻ Thanh toán](../../TestResults/mobile-ui-04c-20261025/browser-accepted/detail-payment-390.png), [chỉ quyền xem](../../TestResults/mobile-ui-04c-20261025/browser-accepted/detail-view-only-390.png), [máy tính](../../TestResults/mobile-ui-04c-20261025/browser-accepted/detail-desktop-1440.png).
